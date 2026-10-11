using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente que prepara los NPC de TASK-202: configura los importers de los
    /// clips de Mixamo, crea el Animator Controller del peatón y genera las variantes de prefab.
    /// </summary>
    public static class NPCPrefabSetup
    {
        public const string MIXAMO_FOLDER = "Assets/Characters/Animations/Mixamo";
        public const string IDLE_FBX_PATH = MIXAMO_FOLDER + "/X Bot@Idle.fbx";
        public const string WALKING_FBX_PATH = MIXAMO_FOLDER + "/X Bot@Walking.fbx";
        public const string CONTROLLER_PATH = "Assets/Characters/Animations/NPC_Pedestrian.controller";
        public const string URBAN_MAN_PREFAB_PATH = "Assets/Characters/UrbanManCustomizable.prefab";

        /// <summary>Velocidad medida del clip Walking de X Bot con desplazamiento (m/s).</summary>
        public const float XBOT_WALK_SPEED = 1.79f;

        /// <summary>Altura de cadera de X Bot en metros, con la que se midió <see cref="XBOT_WALK_SPEED"/>.</summary>
        public const float XBOT_HIP_HEIGHT = 1.02f;

        [MenuItem("Tools/Primer Volante/Setup NPC Prefabs")]
        public static void SetupAll()
        {
            Debug.Log("[NPCPrefabSetup] 🧍 Configurando NPC (importers, Animator Controller y prefabs)...");

            bool clipsOk = ConfigureMixamoImporters();
            float walkReference = ComputeUrbanManWalkReferenceSpeed();
            CreateOrUpdateAnimatorController(walkReference);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[NPCPrefabSetup] ✅ Setup base terminado (clips Mixamo: {(clipsOk ? "OK" : "faltan")}, " +
                      $"velocidad de referencia Walking: {walkReference:F2} m/s).");
        }

        /// <summary>
        /// Velocidad de referencia del clip Walking para el UrbanMan: la medida en X Bot escalada por la altura
        /// de cadera (<c>Animator.humanScale</c> del UrbanMan / altura de cadera de X Bot).
        /// </summary>
        public static float ComputeUrbanManWalkReferenceSpeed()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(URBAN_MAN_PREFAB_PATH);
            var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[NPCPrefabSetup] ⚠️ No se pudo leer humanScale del UrbanMan; se usa la velocidad de X Bot.");
                return XBOT_WALK_SPEED;
            }

            return XBOT_WALK_SPEED * animator.humanScale / XBOT_HIP_HEIGHT;
        }

        /// <summary>
        /// Importa los FBX de Mixamo como Humanoid con avatar propio, clips en loop y *bake into pose*
        /// de rotación, altura y posición XZ. Si falta algún FBX avisa y devuelve false sin fallar.
        /// </summary>
        public static bool ConfigureMixamoImporters()
        {
            bool allOk = true;
            foreach (string path in new[] { IDLE_FBX_PATH, WALKING_FBX_PATH })
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[NPCPrefabSetup] ⚠️ No se encontró el clip de Mixamo en {path}.");
                    allOk = false;
                    continue;
                }

                bool changed = false;

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    changed = true;
                }

                if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    changed = true;
                }

                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

                for (int i = 0; i < clips.Length; i++)
                {
                    if (!clips[i].loopTime || !clips[i].lockRootRotation ||
                        !clips[i].lockRootHeightY || !clips[i].lockRootPositionXZ)
                    {
                        clips[i].loopTime = true;
                        clips[i].lockRootRotation = true;
                        clips[i].lockRootHeightY = true;
                        clips[i].lockRootPositionXZ = true;
                        changed = true;
                    }
                }

                if (changed || importer.clipAnimations == null || importer.clipAnimations.Length == 0)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                    Debug.Log($"[NPCPrefabSetup] 🎞️ Importer configurado: {path}");
                }

                if (!IsHumanWithValidAvatar(path))
                {
                    Debug.LogWarning($"[NPCPrefabSetup] ⚠️ {path}: el avatar humanoide no es válido.");
                    allOk = false;
                }
            }

            return allOk;
        }

        /// <summary>
        /// true si el FBX tiene un avatar humanoide válido.
        /// </summary>
        public static bool IsHumanWithValidAvatar(string fbxPath)
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            return avatar != null && avatar.isHuman && avatar.isValid;
        }

        /// <summary>
        /// Devuelve el clip de animación del FBX (ignora el clip de preview que Unity genera).
        /// </summary>
        public static AnimationClip LoadClip(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__"));
        }

        /// <summary>
        /// Crea o actualiza <c>NPC_Pedestrian.controller</c>: blend tree 1D por <c>Speed</c> (m/s) con Idle en 0
        /// y Walking en la velocidad de referencia. Se conserva el asset (y su GUID) si ya existe.
        /// Si faltan clips avisa y deja el controller creado sin movimientos.
        /// </summary>
        public static AnimatorController CreateOrUpdateAnimatorController(float walkReferenceSpeed)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
            }

            if (controller.parameters.All(p => p.name != "Speed"))
            {
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            }

            AnimationClip idle = LoadClip(IDLE_FBX_PATH);
            AnimationClip walking = LoadClip(WALKING_FBX_PATH);
            if (idle == null || walking == null)
            {
                Debug.LogWarning("[NPCPrefabSetup] ⚠️ Faltan los clips Idle/Walking de Mixamo: el Animator Controller queda sin blend tree.");
                return controller;
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states.ToArray())
            {
                machine.RemoveState(child.state);
            }

            controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walking, walkReferenceSpeed);

            EditorUtility.SetDirty(controller);
            Debug.Log($"[NPCPrefabSetup] 🎛️ Animator Controller listo: {CONTROLLER_PATH} (Walking en {walkReferenceSpeed:F2} m/s).");
            return controller;
        }
    }
}
