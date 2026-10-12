using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente que prepara los NPC de TASK-202: configura los importers de los
    /// clips de Mixamo, crea el Animator Controller del peatón y genera las variantes de prefab de
    /// vehículos y peatones.
    /// </summary>
    public static class NPCPrefabSetup
    {
        public const string MIXAMO_FOLDER = "Assets/Characters/Animations/Mixamo";
        public const string IDLE_FBX_PATH = MIXAMO_FOLDER + "/X Bot@Idle.fbx";
        public const string WALKING_FBX_PATH = MIXAMO_FOLDER + "/X Bot@Walking.fbx";
        public const string CONTROLLER_PATH = "Assets/Characters/Animations/NPC_Pedestrian.controller";
        public const string URBAN_MAN_PREFAB_PATH = "Assets/Characters/UrbanManCustomizable.prefab";

        public const string VEHICLE_SOURCE_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Separated Wheels";
        public const string VEHICLE_OUTPUT_FOLDER = "Assets/Prefabs/NPCs/Vehicles";
        public const string PEDESTRIAN_OUTPUT_FOLDER = "Assets/Prefabs/NPCs/Pedestrians";

        /// <summary>Escala común de todos los vehículos NPC (un Car queda 2.04 × 4.36 m).</summary>
        public const float VEHICLE_SCALE = 0.69f;

        /// <summary>Cantidad de variantes de peatón.</summary>
        public const int PEDESTRIAN_COUNT = 4;

        /// <summary>Velocidad medida del clip Walking de X Bot con desplazamiento (m/s).</summary>
        public const float XBOT_WALK_SPEED = 1.79f;

        /// <summary>Altura de cadera de X Bot en metros, con la que se midió <see cref="XBOT_WALK_SPEED"/>.</summary>
        public const float XBOT_HIP_HEIGHT = 1.02f;

        // Apariencia de cada peatón: torso, accesorios y tono de piel (ids de UrbanManCustomizer).
        private static readonly (string torso, string[] accessories, string skin)[] s_PedestrianLooks =
        {
            ("", new string[0], "Medium"),
            ("HoodieUp", new[] { "Cap" }, "Fair"),
            ("", new[] { "Glasses" }, "Dark"),
            ("HoodieUp", new[] { "Sunglasses" }, "Olive"),
        };

        [MenuItem("Tools/Primer Volante/Setup NPC Prefabs")]
        public static void SetupAll()
        {
            Debug.Log("[NPCPrefabSetup] 🧍 Configurando NPC (importers, Animator Controller y prefabs)...");

            bool clipsOk = ConfigureMixamoImporters();
            AnimatorController controller = CreateOrUpdateAnimatorController();
            float walkReference = ComputeUrbanManWalkReferenceSpeed(controller);

            int vehicles = SetupVehiclePrefabs();
            int pedestrians = SetupPedestrianPrefabs(controller, walkReference);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[NPCPrefabSetup] ✅ Setup terminado: {vehicles} vehículos y {pedestrians} peatones " +
                      $"(clips Mixamo: {(clipsOk ? "OK" : "faltan")}, velocidad de referencia Walking: {walkReference:F2} m/s).");
        }

        // ------------------------------------------------------------------ Mixamo

        /// <summary>
        /// Velocidad de referencia del clip Walking para el UrbanMan, medida sobre su esqueleto con el Animator
        /// Controller del peatón (blend tree y Foot IK): es la velocidad con la que retrocede el pie en apoyo. Si no
        /// se puede medir (faltan el clip, el avatar o el controller) se estima escalando la medición de X Bot por la
        /// altura de cadera (<c>Animator.humanScale</c>).
        /// </summary>
        public static float ComputeUrbanManWalkReferenceSpeed(RuntimeAnimatorController controller)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(URBAN_MAN_PREFAB_PATH);
            var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[NPCPrefabSetup] ⚠️ No se pudo leer el avatar del UrbanMan; se usa la velocidad de X Bot.");
                return XBOT_WALK_SPEED;
            }

            float measured = NPCFootSlidingAnalyzer.MeasureWalkReferenceSpeed(prefab, controller);
            if (measured > 0.5f && measured < 4f) return measured;

            Debug.LogWarning("[NPCPrefabSetup] ⚠️ No se pudo medir el paso del clip Walking; se estima por altura de cadera.");
            return XBOT_WALK_SPEED * animator.humanScale / XBOT_HIP_HEIGHT;
        }

        /// <summary>
        /// Importa los FBX de Mixamo como Humanoid, clips en loop y *bake into pose* de rotación, altura y posición
        /// XZ. Si falta algún FBX avisa y devuelve false sin fallar.
        /// </summary>
        /// <remarks>
        /// Los FBX se descargaron sin malla, así que el avatar que Unity crea desde cada uno toma como pose de
        /// referencia el primer frame de su animación. El de Walking arranca a mitad de paso (pie izquierdo en el
        /// aire, −77° de pitch contra −39° del derecho), y retargetear con esa referencia hundía el cuerpo 4.6 cm y
        /// hacía pisar con el talón izquierdo. Por eso solo Idle crea su avatar (su primer frame es una postura de pie
        /// simétrica) y Walking lo copia: los dos clips comparten la misma referencia y no hace falta corregir nada
        /// por script.
        /// </remarks>
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

                if (path == IDLE_FBX_PATH)
                {
                    if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        changed = true;
                    }
                }
                else
                {
                    Avatar idleAvatar = LoadAvatar(IDLE_FBX_PATH);
                    if (idleAvatar == null)
                    {
                        Debug.LogWarning($"[NPCPrefabSetup] ⚠️ No hay avatar de Idle para compartir con {path}.");
                        allOk = false;
                    }
                    else if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != idleAvatar)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        importer.sourceAvatar = idleAvatar;
                        changed = true;
                    }
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
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            Avatar avatar = importer != null && importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther
                ? importer.sourceAvatar
                : LoadAvatar(fbxPath);
            return avatar != null && avatar.isHuman && avatar.isValid;
        }

        /// <summary>
        /// Avatar que el FBX crea desde su propio modelo (null si no crea ninguno).
        /// </summary>
        public static Avatar LoadAvatar(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
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
        /// y Walking a peso 1 desde <see cref="NPCPedestrianController.WalkBlendEndSpeed"/>; por encima solo cambia la
        /// cadencia (Animator.speed). Un blend lento hasta la velocidad de referencia aplana el paso y los pies
        /// resbalan. Se conserva el asset (y su GUID) si ya existe. Si faltan clips avisa y lo deja sin movimientos.
        /// </summary>
        public static AnimatorController CreateOrUpdateAnimatorController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
            }

            if (controller.parameters.All(p => p.name != NPCPedestrianController.SpeedParameter))
            {
                controller.AddParameter(NPCPedestrianController.SpeedParameter, AnimatorControllerParameterType.Float);
            }

            AnimationClip idle = LoadClip(IDLE_FBX_PATH);
            AnimationClip walking = LoadClip(WALKING_FBX_PATH);
            if (idle == null || walking == null)
            {
                Debug.LogWarning("[NPCPrefabSetup] ⚠️ Faltan los clips Idle/Walking de Mixamo: el Animator Controller queda sin blend tree.");
                return controller;
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            if (HasExpectedBlendTree(machine, idle, walking))
            {
                EnsureFootIK(machine.states[0].state);
                return controller;
            }

            foreach (ChildAnimatorState child in machine.states.ToArray())
            {
                machine.RemoveState(child.state);
            }

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = NPCPedestrianController.SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walking, NPCPedestrianController.WalkBlendEndSpeed);
            EnsureFootIK(locomotion);

            EditorUtility.SetDirty(controller);
            Debug.Log($"[NPCPrefabSetup] 🎛️ Animator Controller listo: {CONTROLLER_PATH} " +
                      $"(Walking a peso 1 desde {NPCPedestrianController.WalkBlendEndSpeed:F2} m/s; Foot IK activo).");
            return controller;
        }

        /// <summary>
        /// Activa Foot IK en el estado: los pies del UrbanMan van a la altura de los del clip original aunque las
        /// proporciones de las piernas difieran de X Bot. Con el avatar compartido, el apoyo queda a menos de 1 cm
        /// y 2° del pie plano en reposo.
        /// </summary>
        private static void EnsureFootIK(AnimatorState state)
        {
            if (state == null || state.iKOnFeet) return;

            state.iKOnFeet = true;
            EditorUtility.SetDirty(state);
        }

        /// <summary>
        /// true si el controller ya tiene el blend tree esperado (se evita reescribir el asset sin cambios).
        /// </summary>
        private static bool HasExpectedBlendTree(AnimatorStateMachine machine, AnimationClip idle, AnimationClip walking)
        {
            ChildAnimatorState[] states = machine.states;
            if (states.Length != 1 || !(states[0].state.motion is BlendTree tree)) return false;

            ChildMotion[] children = tree.children;
            return tree.blendType == BlendTreeType.Simple1D &&
                   tree.blendParameter == NPCPedestrianController.SpeedParameter &&
                   children.Length == 2 &&
                   children[0].motion == idle && Mathf.Approximately(children[0].threshold, 0f) &&
                   children[1].motion == walking && Mathf.Approximately(children[1].threshold, NPCPedestrianController.WalkBlendEndSpeed);
        }

        // ------------------------------------------------------------------ Vehículos

        /// <summary>
        /// Rutas de los 21 prefabs <c>_separate</c> originales.
        /// </summary>
        public static string[] FindVehicleSourcePaths()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { VEHICLE_SOURCE_FOLDER })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("_separate.prefab"))
                .OrderBy(path => path)
                .ToArray();
        }

        /// <summary>
        /// Ruta del prefab NPC para un original: <c>Vehicle_Car_color01_separate</c> → <c>NPC_Car_color01</c>.
        /// </summary>
        public static string VehicleOutputPath(string sourcePath)
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            if (name.StartsWith("Vehicle_")) name = name.Substring("Vehicle_".Length);
            if (name.EndsWith("_separate")) name = name.Substring(0, name.Length - "_separate".Length);
            return $"{VEHICLE_OUTPUT_FOLDER}/NPC_{name}.prefab";
        }

        /// <summary>
        /// Crea o actualiza una variante por cada vehículo <c>_separate</c>. Devuelve cuántas dejó listas.
        /// </summary>
        public static int SetupVehiclePrefabs()
        {
            EnsureFolder(VEHICLE_OUTPUT_FOLDER);

            int count = 0;
            foreach (string sourcePath in FindVehicleSourcePaths())
            {
                string outputPath = VehicleOutputPath(sourcePath);
                CreateVariantIfMissing(sourcePath, outputPath);

                GameObject contents = PrefabUtility.LoadPrefabContents(outputPath);
                try
                {
                    ConfigureVehicle(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, outputPath);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            Debug.Log($"[NPCPrefabSetup] 🚗 {count} variantes de vehículo en {VEHICLE_OUTPUT_FOLDER}.");
            return count;
        }

        private static void ConfigureVehicle(GameObject root)
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * VEHICLE_SCALE;

            // Los NPC no llevan VehicleController: las zonas del compañero los ignoran.
            foreach (var vehicleController in root.GetComponents<PrimerVolante.VR.VehicleController>())
            {
                Object.DestroyImmediate(vehicleController);
            }

            var follower = GetOrAdd<NPCPathFollower>(root);
            ConfigureKinematicBody(root.GetComponent<Rigidbody>());

            var box = GetOrAdd<BoxCollider>(root);
            FitBoxToBody(root.transform, box);

            var controller = GetOrAdd<NPCVehicleController>(root);
            controller.AutoAssignWheels();

            var so = new SerializedObject(follower);
            RequireProperty(so, "m_CruiseSpeed").floatValue = 8f;
            RequireProperty(so, "m_Acceleration").floatValue = 2.5f;
            RequireProperty(so, "m_Deceleration").floatValue = 4f;
            RequireProperty(so, "m_MaxLateralAcceleration").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller);
        }

        /// <summary>
        /// Ajusta un BoxCollider a los bounds del mesh de la carrocería, expresados en el espacio de la raíz.
        /// </summary>
        private static void FitBoxToBody(Transform root, BoxCollider box)
        {
            MeshFilter body = root.GetComponentsInChildren<MeshFilter>(true)
                .FirstOrDefault(filter => filter.transform != root && !filter.name.StartsWith("Wheel"));

            if (body == null || body.sharedMesh == null)
            {
                Debug.LogWarning($"[NPCPrefabSetup] ⚠️ {root.name}: no se encontró la carrocería para ajustar el collider.");
                return;
            }

            Bounds meshBounds = body.sharedMesh.bounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;

            bool first = true;
            Bounds result = default;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                Vector3 point = toRoot.MultiplyPoint3x4(corner);
                if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                else result.Encapsulate(point);
            }

            box.center = result.center;
            box.size = result.size;
            box.isTrigger = false;
        }

        // ------------------------------------------------------------------ Peatones

        /// <summary>
        /// Crea o actualiza las variantes de peatón (4 combinaciones de torso, accesorios y piel).
        /// </summary>
        public static int SetupPedestrianPrefabs(AnimatorController controller, float walkReferenceSpeed)
        {
            EnsureFolder(PEDESTRIAN_OUTPUT_FOLDER);

            int count = 0;
            for (int i = 0; i < PEDESTRIAN_COUNT; i++)
            {
                string outputPath = $"{PEDESTRIAN_OUTPUT_FOLDER}/NPC_Pedestrian_{i + 1:00}.prefab";
                CreateVariantIfMissing(URBAN_MAN_PREFAB_PATH, outputPath);

                GameObject contents = PrefabUtility.LoadPrefabContents(outputPath);
                try
                {
                    ConfigurePedestrian(contents, s_PedestrianLooks[i], controller, walkReferenceSpeed);
                    PrefabUtility.SaveAsPrefabAsset(contents, outputPath);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            Debug.Log($"[NPCPrefabSetup] 🚶 {count} variantes de peatón en {PEDESTRIAN_OUTPUT_FOLDER}.");
            return count;
        }

        private static void ConfigurePedestrian(
            GameObject root,
            (string torso, string[] accessories, string skin) look,
            AnimatorController controller,
            float walkReferenceSpeed)
        {
            // Apariencia determinista (campos serializados del customizer).
            var customizer = root.GetComponent<PrimerVolante.Characters.UrbanManCustomizer>();
            if (customizer != null)
            {
                var so = new SerializedObject(customizer);
                so.FindProperty("m_InitialTorsoId").stringValue = look.torso;
                SerializedProperty accessories = so.FindProperty("m_InitialAccessoryIds");
                accessories.arraySize = look.accessories.Length;
                for (int i = 0; i < look.accessories.Length; i++)
                {
                    accessories.GetArrayElementAtIndex(i).stringValue = look.accessories[i];
                }

                so.FindProperty("m_InitialSkinToneId").stringValue = look.skin;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var follower = GetOrAdd<NPCPathFollower>(root);
            ConfigureKinematicBody(root.GetComponent<Rigidbody>());

            // Los colliders de las partes del cuerpo tienen tamaños de T-pose: se deja un único BoxCollider
            // en la raíz, que cubre el cuerpo de pie.
            foreach (Collider part in root.GetComponentsInChildren<Collider>(true))
            {
                if (part.gameObject != root) part.enabled = false;
            }

            var box = GetOrAdd<BoxCollider>(root);
            FitBoxToStandingBody(root, box);

            // El apoyo de los pies se resuelve en la importación (avatar compartido) y en el Animator (Foot IK). La
            // corrección por script que existía antes (NPCFootGrounding) se borró: se limpia el componente huérfano
            // que pueda haber quedado en prefabs generados con una versión anterior del setup.
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            if (removed > 0) Debug.Log($"[NPCPrefabSetup] 🧹 {root.name}: {removed} componente(s) sin script quitado(s).");

            var pedestrian = GetOrAdd<NPCPedestrianController>(root);
            var pedestrianSo = new SerializedObject(pedestrian);
            RequireProperty(pedestrianSo, "m_WalkReferenceSpeed").floatValue = walkReferenceSpeed;
            RequireProperty(pedestrianSo, "m_BodyCollider").objectReferenceValue = box;
            pedestrianSo.ApplyModifiedPropertiesWithoutUndo();

            var followerSo = new SerializedObject(follower);
            RequireProperty(followerSo, "m_CruiseSpeed").floatValue = NPCPedestrianController.DefaultCruiseSpeed;
            RequireProperty(followerSo, "m_Acceleration").floatValue = 1f;
            RequireProperty(followerSo, "m_Deceleration").floatValue = 2f;
            RequireProperty(followerSo, "m_MaxLateralAcceleration").floatValue = 1.5f;
            followerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// BoxCollider de pie: altura tomada de los renderers (pies a cabeza), ancho y fondo de un cuerpo
        /// con los brazos caídos. La pose T de los meshes no sirve para el ancho.
        /// </summary>
        private static void FitBoxToStandingBody(GameObject root, BoxCollider box)
        {
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                Bounds bounds = smr.sharedMesh.bounds;
                // Los SkinnedMeshRenderer del asset cuelgan de la raíz con transform identidad.
                Vector3 min = root.transform.InverseTransformPoint(smr.transform.TransformPoint(bounds.min));
                Vector3 max = root.transform.InverseTransformPoint(smr.transform.TransformPoint(bounds.max));
                minY = Mathf.Min(minY, Mathf.Min(min.y, max.y));
                maxY = Mathf.Max(maxY, Mathf.Max(min.y, max.y));
            }

            if (float.IsInfinity(minY) || maxY - minY < 0.5f)
            {
                minY = 0f;
                maxY = 1.9f;
            }

            minY = Mathf.Max(0f, minY);
            float height = maxY - minY;
            box.center = new Vector3(0f, minY + height * 0.5f, 0f);
            box.size = new Vector3(0.55f, height, 0.4f);
            box.isTrigger = false;
        }

        // ------------------------------------------------------------------ Utilidades

        /// <summary>
        /// Rigidbody cinemático, interpolado y sin gravedad (los NPC se mueven con MovePosition/MoveRotation).
        /// </summary>
        private static void ConfigureKinematicBody(Rigidbody body)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>
        /// Propiedad serializada por nombre; si no existe (por ejemplo porque se renombró el campo) falla con un
        /// mensaje claro en lugar de una NullReferenceException a mitad del setup.
        /// </summary>
        public static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new System.InvalidOperationException(
                    $"[NPCPrefabSetup] Falta el campo serializado '{propertyName}' en {serializedObject.targetObject.GetType().Name}: ¿se renombró?");
            }

            return property;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        /// <summary>
        /// Crea un Prefab Variant de <paramref name="sourcePath"/> si todavía no existe. La instancia
        /// intermedia vive en una preview scene para no ensuciar la escena abierta.
        /// </summary>
        private static void CreateVariantIfMissing(string sourcePath, string outputPath)
        {
            if (File.Exists(outputPath)) return;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = new List<string>(folder.Split('/'));
            string current = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
