using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para configurar la Lección 7:
    /// "Dominio del Volante y Maniobras con Vallas (Slalom y Reversa con Espejos)".
    /// - Clona de forma segura Lesson_01_BasicDriving.unity a Assets/Scenes/Lessons/Lesson_07_BarrierManeuvers.unity.
    /// - Genera los ScriptableObjects didácticos (LessonData_07.asset y los 7 pasos).
    /// - Construye el circuito de slalom con vallas alternadas (LessonBarrier.prefab) y conos de advertencia.
    /// - Configura las dársenas de inicio (Z ≈ 14.3) y de llegada (Z ≈ 84) con recuadros amarillos en el piso.
    /// - Ensambla el Lesson07BarrierManeuversController en la escena.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [InitializeOnLoad]
    public static class Lesson07BarrierManeuversSetup
    {
        static Lesson07BarrierManeuversSetup()
        {
            EditorApplication.delayCall += AutoSetupIfMissing;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += AutoSetupIfMissing;
            }
        }

        private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.IsValid() && scene.path == TARGET_SCENE_PATH)
            {
                AutoSetupIfMissing();
            }
        }

        private static void AutoSetupIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.path == TARGET_SCENE_PATH)
            {
                GameObject startZone = GameObject.Find(START_ZONE_NAME);
                bool needsUpdate = startZone == null || startZone.transform.position.z > 50f || GameObject.Find(SLALOM_ROOT_NAME) == null;
                if (needsUpdate)
                {
                    Debug.Log("[Lesson07BarrierManeuversSetup] 🔄 Auto-ejecutando SetupLesson07() en escena activa...");
                    SetupLesson07();
                }
            }
        }
        public const string ORIGINAL_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_07_BarrierManeuvers.unity";

        public const string CURRICULUM_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps/Lesson07";
        public const string LESSON_07_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_07.asset";

        public const string BARRIER_PREFAB_PATH = "Assets/Prefabs/Environment/LessonBarrier.prefab";
        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";
        public const string YELLOW_MATERIAL_PATH = "Assets/Environment/Materials/M_Parking_YellowZone.mat";

        public const string SLALOM_ROOT_NAME = "Slalom_Barriers";
        public const string START_ZONE_NAME = "ParkingZone_Start";
        public const string FORWARD_FINISH_ZONE_NAME = "ParkingZone_ForwardFinish";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 7 Scene")]
        public static void SetupLesson07()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [Lesson07BarrierManeuversSetup] Iniciando configuración de Lección 7...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Crear o actualizar los ScriptableObjects de la Lección 7
            LessonDefinitionSO lessonData = CreateOrUpdateLesson07Assets();

            // 2. Asegurar que la escena clonada exista
            EnsureClonedSceneExists();

            // 3. Abrir la escena de la Lección 7
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != TARGET_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 4. Asegurar Car09 y sus componentes de cabina (posición inicial al inicio de la calle)
            GameObject car09 = GameObject.Find("Car09");
            if (car09 != null)
            {
                car09.transform.position = new Vector3(63.5f, 0.02f, 14.3f);
                car09.transform.rotation = Quaternion.identity;
                EditorUtility.SetDirty(car09);
            }

            // 5. Configurar Lesson_Manager con LessonData_07
            GameObject mgrObj = GameObject.Find("Lesson_Manager");
            if (mgrObj != null)
            {
                LessonManager lessonMgr = mgrObj.GetComponent<LessonManager>();
                if (lessonMgr != null)
                {
                    var soMgr = new SerializedObject(lessonMgr);
                    SerializedProperty activeProp = soMgr.FindProperty("m_ActiveLesson");
                    if (activeProp != null) activeProp.objectReferenceValue = lessonData;
                    soMgr.ApplyModifiedProperties();
                    EditorUtility.SetDirty(lessonMgr);
                }
            }

            // 6. Limpiar elementos residuales de Lección 1 (como StopLine_Trigger viejo)
            CleanResidualObjects(scene);

            // 7. Construir Dársenas de Inicio y Llegada (Recuadros amarillos con ParkingZoneTrigger)
            ParkingZoneTrigger startZone, forwardZone;
            GameObject startHighlight, forwardHighlight;
            BuildParkingZones(scene, out startZone, out forwardZone, out startHighlight, out forwardHighlight);

            // 8. Construir las Vallas de Slalom en zig-zag alternado
            List<LessonBarrier> barriers = BuildSlalomBarriers(scene);

            // 9. Configurar Lesson07BarrierManeuversController
            SetupController(scene, startZone, forwardZone, startHighlight, forwardHighlight, barriers);

            // 10. Asegurar escena en EditorBuildSettings
            EnsureSceneInBuildSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("=================================================================");
            Debug.Log("✅ [Lesson07BarrierManeuversSetup] ¡Lección 7 configurada y guardada con éxito!");
            Debug.Log("=================================================================");
        }

        private static void EnsureDirectories()
        {
            if (!Directory.Exists(CURRICULUM_DIR)) Directory.CreateDirectory(CURRICULUM_DIR);
            if (!Directory.Exists(STEPS_DIR)) Directory.CreateDirectory(STEPS_DIR);
            if (!Directory.Exists(TARGET_SCENE_DIR)) Directory.CreateDirectory(TARGET_SCENE_DIR);
        }

        public static LessonDefinitionSO CreateOrUpdateLesson07Assets()
        {
            EnsureDirectories();

            var steps = new List<LessonStepSO>
            {
                // Paso 1: Comprobación y Giro del Volante
                CreateOrUpdateStep("Lesson07_Step01_SteeringGripTechnique",
                    "Comprobación y Giro del Volante",
                    "Gira el volante con fluidez hacia ambos lados (derecha e izquierda) para comprobar su recorrido y la respuesta del vehículo antes de iniciar la marcha.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.RotateSteeringWheel,
                    CockpitControlFlags.SteeringWheel,
                    "SteeringWheel_Pivot",
                    20f),

                // Paso 2: Encendido del Motor
                CreateOrUpdateStep("Lesson07_Step02_StartAndDrive",
                    "Encendido del Motor",
                    "Presiona el botón START para encender el motor del vehículo.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.StartEngine,
                    CockpitControlFlags.All,
                    "StartButton"),

                // Paso 3: Puesta en Marcha (Drive)
                CreateOrUpdateStep("Lesson07_Step03_ForwardSlalom",
                    "Puesta en Marcha (Drive)",
                    "Pisa y mantén presionado el pedal de freno, coloca la palanca de cambios en Drive (D) y baja el freno de mano.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.ShiftToDrive,
                    CockpitControlFlags.All,
                    "VRGearShifter"),

                // Paso 4: Slalom hacia Adelante entre Vallas
                CreateOrUpdateStep("Lesson07_Step04_StopAndShiftReverse",
                    "Slalom hacia Adelante entre Vallas",
                    "Avanza a velocidad moderada (10-15 km/h) esquivando las vallas en zig-zag con giros progresivos de volante. Al finalizar, detén el vehículo por completo dentro de la dársena amarilla.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.SlalomForwardComplete,
                    CockpitControlFlags.All,
                    "ParkingZone_ForwardFinish"),

                // Paso 5: Detención y Cambio a Reversa
                CreateOrUpdateStep("Lesson07_Step05_MirrorHalfCarRule",
                    "Detención y Cambio a Reversa",
                    "¡Excelente slalom hacia adelante! Detén el auto por completo en la dársena. Pisa el freno y coloca la palanca de cambios en Reversa (R).",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.ShiftToReverse,
                    CockpitControlFlags.All,
                    "VRGearShifter"),

                // Paso 6: Slalom en Reversa con Espejos
                CreateOrUpdateStep("Lesson07_Step06_ReverseSlalom",
                    "Slalom en Reversa con Espejos",
                    "Retrocede despacio aplicando la regla del medio auto: observa con atención el espejo del lado de la valla y, cuando veas que pasó la mitad del auto, invierte el volante para esquivar la siguiente hasta detenerte en la dársena de inicio.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.SlalomReverseComplete,
                    CockpitControlFlags.All,
                    "ParkingZone_Start"),

                // Paso 7: Asegurar el Vehículo
                CreateOrUpdateStep("Lesson07_Step07_SecureVehicle",
                    "Asegurar el Vehículo",
                    "Detén la marcha, coloca la palanca de cambios en Parking (P) y acciona el freno de mano hacia arriba para finalizar la lección exitosamente.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.SecureStopAndPark,
                    CockpitControlFlags.All,
                    "VRGearShifter")
            };

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_07_PATH);
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                AssetDatabase.CreateAsset(lesson, LESSON_07_PATH);
            }

            lesson.lessonId = "LESSON_07";
            lesson.lessonTitle = "Lección 7: Dominio del Volante y Vallas";
            lesson.steps = steps;

            EditorUtility.SetDirty(lesson);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Lesson07BarrierManeuversSetup] 📋 LessonData_07 actualizado con {steps.Count} pasos didácticos automáticos.");
            return lesson;
        }

        private static LessonStepSO CreateOrUpdateStep(string assetName, string title, string instruction,
            LessonStepCompletionMode mode, LessonActionType action, CockpitControlFlags controls,
            string highlightTarget = "", float numericParam = 0f)
        {
            string path = $"{STEPS_DIR}/{assetName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                AssetDatabase.CreateAsset(step, path);
            }

            step.stepTitle = title;
            step.instructionText = instruction;
            step.completionMode = mode;
            step.requiredAction = action;
            step.allowedControls = controls;
            step.highlightTargetName = highlightTarget;
            step.actionNumericParameter = numericParam;
            step.ambienceIntensity = AmbienceIntensityOverride.Normal;

            EditorUtility.SetDirty(step);
            return step;
        }

        private static void EnsureClonedSceneExists()
        {
            if (File.Exists(TARGET_SCENE_PATH))
            {
                return;
            }

            Debug.Log($"[Lesson07BarrierManeuversSetup] 📋 Clonando escena {ORIGINAL_SCENE_PATH} -> {TARGET_SCENE_PATH}...");
            bool ok = AssetDatabase.CopyAsset(ORIGINAL_SCENE_PATH, TARGET_SCENE_PATH);
            if (!ok)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ Falló la clonación de la escena a {TARGET_SCENE_PATH}");
            }
        }

        private static void CleanResidualObjects(Scene scene)
        {
            string[] objectsToRemove = { "StopLine_Trigger", "Controlled_Intersection_T" };
            foreach (var name in objectsToRemove)
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null)
                {
                    Undo.DestroyObjectImmediate(obj);
                }
            }
        }

        private static void BuildParkingZones(Scene scene,
            out ParkingZoneTrigger startZone, out ParkingZoneTrigger forwardZone,
            out GameObject startHighlight, out GameObject forwardHighlight)
        {
            Material yellowMat = AssetDatabase.LoadAssetAtPath<Material>(YELLOW_MATERIAL_PATH);

            // 1. Dársena de Inicio / Retorno en Reversa (Z ≈ 14.3, X = 63.5)
            GameObject startObj = GameObject.Find(START_ZONE_NAME);
            if (startObj == null)
            {
                startObj = new GameObject(START_ZONE_NAME);
                Undo.RegisterCreatedObjectUndo(startObj, "Create Start Parking Zone");
                SceneManager.MoveGameObjectToScene(startObj, scene);
            }
            startObj.transform.position = new Vector3(63.5f, 0.05f, 14.3f);
            startObj.transform.rotation = Quaternion.identity;

            BoxCollider startBox = startObj.GetComponent<BoxCollider>();
            if (startBox == null) startBox = startObj.AddComponent<BoxCollider>();
            startBox.isTrigger = true;
            startBox.center = Vector3.zero;
            startBox.size = new Vector3(4.2f, 2.0f, 6.5f);

            startZone = startObj.GetComponent<ParkingZoneTrigger>();
            if (startZone == null) startZone = startObj.AddComponent<ParkingZoneTrigger>();
            var soStart = new SerializedObject(startZone);
            SerializedProperty propStartType = soStart.FindProperty("m_StationType");
            if (propStartType != null) propStartType.enumValueIndex = (int)ParkingStationType.ParallelRight;
            SerializedProperty propStartSpeed = soStart.FindProperty("m_MaxStopSpeedKmh");
            if (propStartSpeed != null) propStartSpeed.floatValue = 1.5f;
            SerializedProperty propStartAngle = soStart.FindProperty("m_MaxAlignmentAngleDeg");
            if (propStartAngle != null) propStartAngle.floatValue = 30f;
            soStart.ApplyModifiedProperties();

            startHighlight = EnsureGroundVisual(startObj.transform, "GroundVisual_StartYellow", new Vector3(4.0f, 6.0f, 1f), yellowMat);

            // 2. Dársena de Llegada del Slalom hacia Adelante (Z ≈ 84.0, X = 60.0)
            GameObject forwardObj = GameObject.Find(FORWARD_FINISH_ZONE_NAME);
            if (forwardObj == null)
            {
                forwardObj = new GameObject(FORWARD_FINISH_ZONE_NAME);
                Undo.RegisterCreatedObjectUndo(forwardObj, "Create Forward Finish Parking Zone");
                SceneManager.MoveGameObjectToScene(forwardObj, scene);
            }
            forwardObj.transform.position = new Vector3(60.0f, 0.05f, 84.0f);
            forwardObj.transform.rotation = Quaternion.identity;

            BoxCollider forwardBox = forwardObj.GetComponent<BoxCollider>();
            if (forwardBox == null) forwardBox = forwardObj.AddComponent<BoxCollider>();
            forwardBox.isTrigger = true;
            forwardBox.center = Vector3.zero;
            forwardBox.size = new Vector3(5.0f, 2.0f, 7.0f);

            forwardZone = forwardObj.GetComponent<ParkingZoneTrigger>();
            if (forwardZone == null) forwardZone = forwardObj.AddComponent<ParkingZoneTrigger>();
            var soForward = new SerializedObject(forwardZone);
            SerializedProperty propForwardType = soForward.FindProperty("m_StationType");
            if (propForwardType != null) propForwardType.enumValueIndex = (int)ParkingStationType.ParallelRight;
            SerializedProperty propForwardSpeed = soForward.FindProperty("m_MaxStopSpeedKmh");
            if (propForwardSpeed != null) propForwardSpeed.floatValue = 1.5f;
            SerializedProperty propForwardAngle = soForward.FindProperty("m_MaxAlignmentAngleDeg");
            if (propForwardAngle != null) propForwardAngle.floatValue = 35f;
            soForward.ApplyModifiedProperties();

            forwardHighlight = EnsureGroundVisual(forwardObj.transform, "GroundVisual_ForwardYellow", new Vector3(4.5f, 6.5f, 1f), yellowMat);

            EditorUtility.SetDirty(startZone);
            EditorUtility.SetDirty(forwardZone);
        }

        private static GameObject EnsureGroundVisual(Transform parent, string name, Vector3 scale, Material mat)
        {
            Transform existing = parent.Find(name);
            GameObject quad;
            if (existing != null)
            {
                quad = existing.gameObject;
            }
            else
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                quad.transform.SetParent(parent, false);
                Collider c = quad.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }

            quad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = scale;

            if (mat != null)
            {
                MeshRenderer mr = quad.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            return quad;
        }

        private static List<LessonBarrier> BuildSlalomBarriers(Scene scene)
        {
            GameObject barrierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BARRIER_PREFAB_PATH);
            GameObject conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);

            if (barrierPrefab == null)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ No se encontró prefab de valla: {BARRIER_PREFAB_PATH}");
                return new List<LessonBarrier>();
            }

            GameObject root = GameObject.Find(SLALOM_ROOT_NAME);
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
            }

            root = new GameObject(SLALOM_ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Slalom Barriers Root");
            SceneManager.MoveGameObjectToScene(root, scene);

            var barrierList = new List<LessonBarrier>();

            // Especificaciones del circuito de slalom en zig-zag:
            // Calle recta de asfalto entre Z=14.3 y Z=84.0. Centro de calzada en X=60. Cordones en X=53 y X=67.
            // Alternamos valla izquierda (bloquea X ~ 54..58, paso abierto a la derecha X ~ 59..66)
            // y valla derecha (bloquea X ~ 62..66, paso abierto a la izquierda X ~ 54..61).
            var slalomSpecs = new (string name, Vector3 pos, float yaw, Vector3 coneOffset)[]
            {
                // Valla 1: Izquierda en Z=28 (obliga a abrirse a la derecha en carril derecho)
                ("Slalom_Barrier_01_Left", new Vector3(56.5f, 0f, 28.0f), 0f, new Vector3(2.0f, 0f, 0f)),

                // Valla 2: Derecha en Z=42 (obliga a abrirse a la izquierda en carril izquierdo)
                ("Slalom_Barrier_02_Right", new Vector3(63.5f, 0f, 42.0f), 0f, new Vector3(-2.0f, 0f, 0f)),

                // Valla 3: Izquierda en Z=56 (obliga a abrirse a la derecha en carril derecho)
                ("Slalom_Barrier_03_Left", new Vector3(56.5f, 0f, 56.0f), 0f, new Vector3(2.0f, 0f, 0f)),

                // Valla 4: Derecha en Z=70 (obliga a abrirse a la izquierda hacia la dársena de meta)
                ("Slalom_Barrier_04_Right", new Vector3(63.5f, 0f, 70.0f), 0f, new Vector3(-2.0f, 0f, 0f))
            };

            foreach (var spec in slalomSpecs)
            {
                GameObject bObj = (GameObject)PrefabUtility.InstantiatePrefab(barrierPrefab, root.transform);
                bObj.name = spec.name;
                bObj.transform.position = spec.pos;
                bObj.transform.rotation = Quaternion.Euler(0f, spec.yaw, 0f);

                LessonBarrier lb = bObj.GetComponentInChildren<LessonBarrier>(true);
                if (lb != null)
                {
                    barrierList.Add(lb);
                }

                // Agregar cono de tráfico en el extremo de la valla para delimitación visual
                if (conePrefab != null)
                {
                    GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, bObj.transform);
                    cone.name = "Cone_Tip";
                    cone.transform.localPosition = spec.coneOffset;
                    cone.transform.localRotation = Quaternion.identity;
                }
            }

            Debug.Log($"[Lesson07BarrierManeuversSetup] 🚧 {barrierList.Count} vallas de slalom instaladas en zig-zag.");
            return barrierList;
        }

        private static void SetupController(Scene scene,
            ParkingZoneTrigger startZone, ParkingZoneTrigger forwardZone,
            GameObject startHighlight, GameObject forwardHighlight,
            List<LessonBarrier> barriers)
        {
            GameObject ctrlObj = GameObject.Find("Lesson07_Controller");
            if (ctrlObj == null)
            {
                ctrlObj = new GameObject("Lesson07_Controller");
                Undo.RegisterCreatedObjectUndo(ctrlObj, "Create Lesson07 Controller");
                SceneManager.MoveGameObjectToScene(ctrlObj, scene);
            }

            Lesson07BarrierManeuversController ctrl = ctrlObj.GetComponent<Lesson07BarrierManeuversController>();
            if (ctrl == null) ctrl = ctrlObj.AddComponent<Lesson07BarrierManeuversController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("m_LessonManager").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            so.FindProperty("m_VehicleController").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            so.FindProperty("m_HUDPanel").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            so.FindProperty("m_ScreenFader").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            so.FindProperty("m_StartZone").objectReferenceValue = startZone;
            so.FindProperty("m_ForwardFinishZone").objectReferenceValue = forwardZone;
            so.FindProperty("m_StartZoneHighlight").objectReferenceValue = startHighlight;
            so.FindProperty("m_ForwardZoneHighlight").objectReferenceValue = forwardHighlight;

            SerializedProperty barriersProp = so.FindProperty("m_SlalomBarriers");
            barriersProp.ClearArray();
            for (int i = 0; i < barriers.Count; i++)
            {
                barriersProp.InsertArrayElementAtIndex(i);
                barriersProp.GetArrayElementAtIndex(i).objectReferenceValue = barriers[i];
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ctrl);
        }

        private static void EnsureSceneInBuildSettings()
        {
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == TARGET_SCENE_PATH))
            {
                buildScenes.Add(new EditorBuildSettingsScene(TARGET_SCENE_PATH, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
                Debug.Log($"[Lesson07BarrierManeuversSetup] ➕ Escena {TARGET_SCENE_PATH} agregada a EditorBuildSettings.");
            }
        }

        [MenuItem("Tools/Primer Volante/Lessons/Verify Lesson 7")]
        public static bool VerifyLesson07()
        {
            Debug.Log("=================================================================");
            Debug.Log("🔍 [Lesson07BarrierManeuversSetup] Verificando Lección 7...");
            Debug.Log("=================================================================");

            var failures = new List<string>();

            // 1. Verificar ScriptableObjects
            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_07_PATH);
            if (lesson == null) failures.Add($"No se encontró {LESSON_07_PATH}");
            else
            {
                if (lesson.steps == null || lesson.steps.Count != 7)
                {
                    failures.Add($"LessonData_07 debe tener 7 pasos (tiene {lesson.steps?.Count ?? 0}).");
                }
                else
                {
                    // Verificar mención a la regla del medio auto
                    bool hasHalfCarRule = false;
                    foreach (var s in lesson.steps)
                    {
                        if (s != null && s.instructionText.ToLower().Contains("medio auto"))
                        {
                            hasHalfCarRule = true;
                            break;
                        }
                    }
                    if (!hasHalfCarRule) failures.Add("Falta la explicación de la regla del 'medio auto' en los pasos de reversa.");
                }
            }

            // 2. Verificar Escena
            Scene scene = SceneManager.GetSceneByName("Lesson_07_BarrierManeuvers");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                failures.Add($"No se pudo abrir la escena {TARGET_SCENE_PATH}");
            }
            else
            {
                // Car09
                GameObject car09 = GameObject.Find("Car09");
                if (car09 == null) failures.Add("Falta Car09 en la escena.");

                // Lesson_Manager
                GameObject mgr = GameObject.Find("Lesson_Manager");
                if (mgr == null) failures.Add("Falta Lesson_Manager en la escena.");

                // Controller
                Lesson07BarrierManeuversController ctrl = UnityEngine.Object.FindAnyObjectByType<Lesson07BarrierManeuversController>();
                if (ctrl == null) failures.Add("Falta Lesson07BarrierManeuversController en la escena.");
                else
                {
                    if (ctrl.StartZone == null) failures.Add("Controller: StartZone está null.");
                    if (ctrl.ForwardFinishZone == null) failures.Add("Controller: ForwardFinishZone está null.");
                }

                // Vallas
                GameObject slalomRoot = GameObject.Find(SLALOM_ROOT_NAME);
                if (slalomRoot == null) failures.Add($"Falta el contenedor {SLALOM_ROOT_NAME}.");
                else
                {
                    int count = slalomRoot.transform.childCount;
                    if (count < 4) failures.Add($"Se esperaban al menos 4 vallas en slalom (hay {count}).");
                }
            }

            if (failures.Count > 0)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ Verificación falló con {failures.Count} errores:\n - " + string.Join("\n - ", failures));
                return false;
            }

            Debug.Log("[Lesson07BarrierManeuversSetup] 🏆 ¡VERIFICACIÓN DE LECCIÓN 7 EXITOSA! Todos los assets, escena, vallas y controlador están en orden.");
            return true;
        }
    }
}
