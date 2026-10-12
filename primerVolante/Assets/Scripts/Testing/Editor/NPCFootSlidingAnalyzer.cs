using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Mide en el Editor el deslizamiento de los pies de un peatón NPC. Instancia el prefab en una preview scene
    /// (no toca la escena abierta), avanza el Animator o muestrea el clip a paso fijo y compara la velocidad del
    /// pie en apoyo con la del cuerpo. Un pie en apoyo debería estar quieto respecto del suelo.
    /// </summary>
    public static class NPCFootSlidingAnalyzer
    {
        private const float STEP = 1f / 60f;

        /// <summary>Pie en apoyo promediado: altura del tobillo y de los dedos (m) y pitch (grados).</summary>
        public struct StanceFoot
        {
            public float AnkleHeight;
            public float ToesHeight;
            public float Pitch;
            public int Frames;
        }

        // Un pie se considera en apoyo mientras su altura está en el 15 % inferior de su recorrido vertical.
        private const float STANCE_FRACTION = 0.15f;

        /// <summary>
        /// Velocidad (m/s) a la que el pie retrocede respecto del cuerpo durante el apoyo cuando Walking se
        /// reproduce a peso 1 y velocidad 1: es la velocidad del cuerpo con la que el paso no desliza. Se mide
        /// sobre el esqueleto del propio personaje y con el mismo Animator Controller que en ejecución (blend tree y
        /// Foot IK), igual que <see cref="MeasureFootSliding"/>: Foot IK alarga el apoyo y muestrear el clip crudo
        /// subestimaba el paso (1.49 m/s contra 1.71 m/s reales), lo que hacía resbalar los pies.
        /// </summary>
        /// <param name="pedestrianPrefab">Personaje con Animator humanoide.</param>
        /// <param name="controller">Controller a usar; si es null se usa el que ya tiene el prefab.</param>
        public static float MeasureWalkReferenceSpeed(GameObject pedestrianPrefab, RuntimeAnimatorController controller = null)
        {
            if (pedestrianPrefab == null) return 0f;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pedestrianPrefab, preview);
                var animator = instance.GetComponent<Animator>();
                if (animator == null) return 0f;
                if (controller != null) animator.runtimeAnimatorController = controller;
                if (animator.runtimeAnimatorController == null) return 0f;

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();

                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if (leftFoot == null || rightFoot == null) return 0f;

                animator.SetFloat(NPCPedestrianController.SpeedParameter, NPCPedestrianController.WalkBlendEndSpeed);
                animator.speed = 1f;
                for (int i = 0; i < 90; i++)
                {
                    animator.Update(STEP);
                }

                const int frames = 360;
                var z = new float[2, frames];
                var y = new float[2, frames];
                for (int i = 0; i < frames; i++)
                {
                    animator.Update(STEP);
                    Vector3 left = instance.transform.InverseTransformPoint(leftFoot.position);
                    Vector3 right = instance.transform.InverseTransformPoint(rightFoot.position);
                    z[0, i] = left.z; y[0, i] = left.y;
                    z[1, i] = right.z; y[1, i] = right.y;
                }

                float backward = 0f;
                int count = 0;
                for (int foot = 0; foot < 2; foot++)
                {
                    float threshold = StanceThreshold(y, foot, frames);
                    for (int i = 1; i < frames; i++)
                    {
                        if (y[foot, i] > threshold) continue;
                        backward += -(z[foot, i] - z[foot, i - 1]) / STEP;
                        count++;
                    }
                }

                return count > 0 ? backward / count : 0f;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Mide con el clip de reposo (Idle) y el pie plano: altura del tobillo sobre el suelo y pitch del vector
        /// tobillo → dedos (grados). Es la pose contra la que se compara el apoyo al caminar.
        /// </summary>
        public static bool MeasureRestFoot(GameObject pedestrianPrefab, AnimationClip idleClip, out float ankleHeight, out float pitch)
        {
            ankleHeight = 0f;
            pitch = 0f;
            if (pedestrianPrefab == null || idleClip == null) return false;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pedestrianPrefab, preview);
                var animator = instance.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.Rebind();
                idleClip.SampleAnimation(instance, 0f);

                float height = 0f;
                float pitchSum = 0f;
                int feet = 0;
                foreach (HumanBodyBones[] pair in new[]
                {
                    new[] { HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes },
                    new[] { HumanBodyBones.RightFoot, HumanBodyBones.RightToes },
                })
                {
                    Transform foot = animator.GetBoneTransform(pair[0]);
                    Transform toes = animator.GetBoneTransform(pair[1]);
                    if (foot == null || toes == null) continue;

                    Vector3 ankle = instance.transform.InverseTransformPoint(foot.position);
                    Vector3 direction = instance.transform.InverseTransformPoint(toes.position) - ankle;
                    height += ankle.y;
                    pitchSum += Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
                    feet++;
                }

                if (feet == 0) return false;
                ankleHeight = height / feet;
                pitch = pitchSum / feet;
                return true;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Pose media de cada pie (0 = izquierdo, 1 = derecho) durante el apoyo, muestreando el clip directamente
        /// sobre el esqueleto del personaje (sin Animator Controller ni Foot IK): muestra el sesgo que deja el
        /// retargeting por sí solo.
        /// </summary>
        public static StanceFoot[] MeasureStanceFromClip(GameObject pedestrianPrefab, AnimationClip clip)
        {
            var result = new StanceFoot[2];
            if (pedestrianPrefab == null || clip == null) return result;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pedestrianPrefab, preview);
                var animator = instance.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.Rebind();

                Transform[] feet =
                {
                    animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                    animator.GetBoneTransform(HumanBodyBones.RightFoot),
                };
                Transform[] toes =
                {
                    animator.GetBoneTransform(HumanBodyBones.LeftToes),
                    animator.GetBoneTransform(HumanBodyBones.RightToes),
                };
                if (feet[0] == null || feet[1] == null || toes[0] == null || toes[1] == null) return result;

                int frames = Mathf.Max(8, Mathf.RoundToInt(clip.length / STEP));
                var ankle = new float[2, frames];
                var toe = new float[2, frames];
                var pitch = new float[2, frames];
                for (int i = 0; i < frames; i++)
                {
                    clip.SampleAnimation(instance, clip.length * i / frames);
                    for (int f = 0; f < 2; f++)
                    {
                        Vector3 a = instance.transform.InverseTransformPoint(feet[f].position);
                        Vector3 t = instance.transform.InverseTransformPoint(toes[f].position);
                        Vector3 d = t - a;
                        ankle[f, i] = a.y;
                        toe[f, i] = t.y;
                        pitch[f, i] = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    }
                }

                for (int f = 0; f < 2; f++)
                {
                    float threshold = StanceThreshold(ankle, f, frames);
                    var stance = new StanceFoot();
                    for (int i = 0; i < frames; i++)
                    {
                        if (ankle[f, i] > threshold) continue;
                        stance.AnkleHeight += ankle[f, i];
                        stance.ToesHeight += toe[f, i];
                        stance.Pitch += pitch[f, i];
                        stance.Frames++;
                    }

                    if (stance.Frames > 0)
                    {
                        stance.AnkleHeight /= stance.Frames;
                        stance.ToesHeight /= stance.Frames;
                        stance.Pitch /= stance.Frames;
                    }

                    result[f] = stance;
                }

                return result;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Pose media de cada pie (0 = izquierdo, 1 = derecho) durante el apoyo al caminar a
        /// <paramref name="bodySpeed"/> con el Animator Controller del prefab (blend tree y Foot IK), como en el juego.
        /// </summary>
        public static StanceFoot[] MeasureStance(GameObject pedestrianPrefab, float bodySpeed)
        {
            var result = new StanceFoot[2];
            if (pedestrianPrefab == null) return result;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pedestrianPrefab, preview);
                var animator = instance.GetComponent<Animator>();
                var pedestrian = instance.GetComponent<NPCPedestrianController>();
                if (animator == null || pedestrian == null || animator.runtimeAnimatorController == null) return result;

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetFloat(NPCPedestrianController.SpeedParameter, bodySpeed);
                animator.speed = NPCPedestrianController.ComputeAnimatorSpeed(
                    bodySpeed, pedestrian.WalkReferenceSpeed, pedestrian.MaxAnimatorSpeed);

                Transform[] feet =
                {
                    animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                    animator.GetBoneTransform(HumanBodyBones.RightFoot),
                };
                Transform[] toes =
                {
                    animator.GetBoneTransform(HumanBodyBones.LeftToes),
                    animator.GetBoneTransform(HumanBodyBones.RightToes),
                };

                for (int i = 0; i < 90; i++)
                {
                    animator.Update(STEP);
                }

                const int frames = 360;
                var ankle = new float[2, frames];
                var toe = new float[2, frames];
                var pitch = new float[2, frames];
                for (int i = 0; i < frames; i++)
                {
                    animator.Update(STEP);

                    for (int f = 0; f < 2; f++)
                    {
                        Vector3 a = instance.transform.InverseTransformPoint(feet[f].position);
                        Vector3 t = instance.transform.InverseTransformPoint(toes[f].position);
                        Vector3 d = t - a;
                        ankle[f, i] = a.y;
                        toe[f, i] = t.y;
                        pitch[f, i] = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    }
                }

                for (int f = 0; f < 2; f++)
                {
                    float threshold = StanceThreshold(ankle, f, frames);
                    var stance = new StanceFoot();
                    for (int i = 0; i < frames; i++)
                    {
                        if (ankle[f, i] > threshold) continue;
                        stance.AnkleHeight += ankle[f, i];
                        stance.ToesHeight += toe[f, i];
                        stance.Pitch += pitch[f, i];
                        stance.Frames++;
                    }

                    if (stance.Frames > 0)
                    {
                        stance.AnkleHeight /= stance.Frames;
                        stance.ToesHeight /= stance.Frames;
                        stance.Pitch /= stance.Frames;
                    }

                    result[f] = stance;
                }

                return result;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// Deslizamiento medio (m/s) de los pies en apoyo cuando el peatón se mueve a
        /// <paramref name="bodySpeed"/>, con la misma lógica de animación que usa en ejecución
        /// (parámetro Speed del blend tree + <see cref="NPCPedestrianController.ComputeAnimatorSpeed"/>).
        /// 0 = pies clavados al suelo.
        /// </summary>
        public static float MeasureFootSliding(GameObject pedestrianPrefab, float bodySpeed)
        {
            if (pedestrianPrefab == null) return float.NaN;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pedestrianPrefab, preview);
                var animator = instance.GetComponent<Animator>();
                var pedestrian = instance.GetComponent<NPCPedestrianController>();
                if (animator == null || pedestrian == null || animator.runtimeAnimatorController == null) return float.NaN;

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();

                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if (leftFoot == null || rightFoot == null) return float.NaN;

                animator.SetFloat(NPCPedestrianController.SpeedParameter, bodySpeed);
                animator.speed = NPCPedestrianController.ComputeAnimatorSpeed(
                    bodySpeed, pedestrian.WalkReferenceSpeed, pedestrian.MaxAnimatorSpeed);

                for (int i = 0; i < 90; i++)
                {
                    animator.Update(STEP);
                }

                const int frames = 360;
                var z = new float[2, frames];
                var y = new float[2, frames];
                for (int i = 0; i < frames; i++)
                {
                    animator.Update(STEP);
                    Vector3 left = instance.transform.InverseTransformPoint(leftFoot.position);
                    Vector3 right = instance.transform.InverseTransformPoint(rightFoot.position);
                    z[0, i] = left.z; y[0, i] = left.y;
                    z[1, i] = right.z; y[1, i] = right.y;
                }

                float sum = 0f;
                int count = 0;
                for (int foot = 0; foot < 2; foot++)
                {
                    float threshold = StanceThreshold(y, foot, frames);
                    for (int i = 1; i < frames; i++)
                    {
                        if (y[foot, i] > threshold) continue;
                        // Velocidad del pie respecto del suelo = velocidad del cuerpo + variación local en z.
                        sum += Mathf.Abs(bodySpeed + (z[foot, i] - z[foot, i - 1]) / STEP);
                        count++;
                    }
                }

                return count > 0 ? sum / count : float.NaN;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static float StanceThreshold(float[,] y, int foot, int frames)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < frames; i++)
            {
                min = Mathf.Min(min, y[foot, i]);
                max = Mathf.Max(max, y[foot, i]);
            }

            return min + STANCE_FRACTION * (max - min);
        }
    }
}
