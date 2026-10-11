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

        // Un pie se considera en apoyo mientras su altura está en el 15 % inferior de su recorrido vertical.
        private const float STANCE_FRACTION = 0.15f;

        /// <summary>
        /// Velocidad (m/s) a la que el pie retrocede respecto del cuerpo durante el apoyo cuando el clip se
        /// reproduce a velocidad 1: es la velocidad del cuerpo con la que el paso del clip no desliza. Se mide
        /// sobre el esqueleto del propio personaje, así que incluye su largo de piernas.
        /// </summary>
        public static float MeasureWalkReferenceSpeed(GameObject pedestrianPrefab, AnimationClip walkingClip)
        {
            if (pedestrianPrefab == null || walkingClip == null) return 0f;

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pedestrianPrefab, preview);
                var animator = instance.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.Rebind();

                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if (leftFoot == null || rightFoot == null) return 0f;

                int frames = Mathf.Max(8, Mathf.RoundToInt(walkingClip.length / STEP));
                float dt = walkingClip.length / frames;
                var z = new float[2, frames];
                var y = new float[2, frames];
                for (int i = 0; i < frames; i++)
                {
                    walkingClip.SampleAnimation(instance, i * dt);
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
                        backward += -(z[foot, i] - z[foot, i - 1]) / dt;
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

                for (int i = 0; i < 90; i++) animator.Update(STEP);

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
