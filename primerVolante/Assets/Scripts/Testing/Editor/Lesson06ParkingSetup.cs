using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente para configurar y ensamblar la Lección 6:
    /// "Maniobras de Estacionamiento y Precisión" (TASK-301 / Issue #10 / PRI-46).
    /// Clona de forma segura Lesson_01_BasicDriving.unity a Assets/Scenes/Lessons/Lesson_06_ParkingManeuvers.unity,
    /// crea los ScriptableObjects correspondientes y construye el área de dársenas en el playón (X=120, Z=60 a 120)
    /// con TurnSignalExemptZone, conos de tránsito y vehículos estáticos equipados con ObstacleCollisionDetector.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Lesson06ParkingSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_06_ParkingManeuvers.unity";

        public const string CURRICULUM_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps/Lesson06";
        public const string LESSON_06_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_06.asset";

        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";
        public const string STATIC_CAR_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels/Vehicle_Car_color01.prefab";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 6 Scene")]
        public static void SetupLesson06()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [Lesson06ParkingSetup] Iniciando configuración de Lección 6...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Crear o actualizar los ScriptableObjects de la Lección 6
            LessonDefinitionSO lessonData = CreateOrUpdateLesson06Assets();

            // 2. Asegurar que la escena clonada exista
            EnsureClonedSceneExists();

            // 3. Abrir la escena de la Lección 6
            Scene scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson06ParkingSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 4. Asegurar Car09 y sus componentes de cabina
            GameObject car09 = EnsureCar09InScene(scene);
            if (car09 == null)
            {
                Debug.LogError("[Lesson06ParkingSetup] ❌ No se pudo encontrar o configurar Car09 en la escena.");
                return;
            }

            // 5. Configurar detector de guiños en Car09
            TurnSignalDetector turnDetector = car09.GetComponent<TurnSignalDetector>();
            if (turnDetector == null)
            {
                turnDetector = car09.AddComponent<TurnSignalDetector>();
            }
            int laneZoneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (laneZoneLayer >= 0)
            {
                turnDetector.ZoneMask = 1 << laneZoneLayer;
            }
            EditorUtility.SetDirty(turnDetector);

            // 6. Asegurar VRScreenFader en la escena
            VRScreenFader screenFader = VRScreenFader.EnsureScreenFader();
            EditorUtility.SetDirty(screenFader);

            // 7. Configurar la TurnSignalExemptZone en el playón (X=120, Z=60 a 120)
            TurnSignalExemptZone exemptZone = SetupTurnSignalExemptZone(scene);

            // 8. Construir las tres estaciones de estacionamiento con conos y autos estáticos
            ParkingZoneTrigger trigger45, trigger90, triggerParallel;
            BuildParkingStations(scene, out trigger45, out trigger90, out triggerParallel);

            // 9. Configurar puntos de spawn para cada paso
            Transform[] spawnPoints = EnsureSpawnPoints(scene);

            // 10. Posicionar Car09 inicialmente en la aproximación al área de maniobras
            if (spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[0] != null)
            {
                car09.transform.position = spawnPoints[0].position;
                car09.transform.rotation = spawnPoints[0].rotation;

                Rigidbody rb = car09.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = spawnPoints[0].position;
                    rb.rotation = spawnPoints[0].rotation;
                }
            }

            // 11. Configurar LessonManager, HUD y Lesson06ParkingController
            SetupLessonControllers(scene, car09, lessonData, spawnPoints, exemptZone, trigger45, trigger90, triggerParallel, screenFader);

            // 12. Guardar escena y assets
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("=================================================================");
            Debug.Log("✅ [Lesson06ParkingSetup] ¡Lección 6 configurada exitosamente!");
            Debug.Log($"   Escena: {TARGET_SCENE_PATH}");
            Debug.Log($"   Asset:  {LESSON_06_PATH}");
            Debug.Log("=================================================================");
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
            if (!AssetDatabase.IsValidFolder(TARGET_SCENE_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scenes", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Scripts/Lessons"))
            {
                AssetDatabase.CreateFolder("Assets/Scripts", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder(CURRICULUM_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Curriculum");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Scripts/Lessons/Steps"))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Steps");
            }
            if (!AssetDatabase.IsValidFolder(STEPS_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons/Steps", "Lesson06");
            }
        }

        public static LessonDefinitionSO CreateOrUpdateLesson06Assets()
        {
            EnsureDirectories();

            LessonStepSO step1 = CreateOrUpdateStep("Lesson06_Step01_HazardLightsApproach",
                "Aproximación con Balizas de Emergencia",
                "Pulsa con tu mano virtual el botón de balizas de emergencia en la consola central para advertir la maniobra a los vehículos traseros.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ActivateHazardLights,
                0f,
                CockpitControlFlags.All,
                "HazardButton");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson06_Step02_DiagonalParking45",
                "Estacionamiento a 45° (Espiga)",
                "Modula los pedales a baja velocidad, encara la dársena a 45° de frente entre los conos y deténte antes del cordón. Coloca Parking (P) y freno de mano.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ParkDiagonal45,
                0f,
                CockpitControlFlags.All,
                "VRGearShifter, VRHandbrake");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson06_Step03_BayParking90Reverse",
                "Estacionamiento a 90° de Culata",
                "Sobrepasa la dársena a 90°, coloca Reversa (R) y maniobra hacia atrás utilizando los espejos retrovisores laterales para centrar el auto entre los conos delimitadores y frena.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ParkBay90Reverse,
                0f,
                CockpitControlFlags.All,
                "VRGearShifter");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson06_Step04_ParallelParkingRight",
                "Estacionamiento en Paralelo al Cordón",
                "Alinea el auto paralelo al vehículo de referencia delantero, coloca Reversa (R) girando a 45° hacia la vereda, y quiebra la dirección al lado opuesto al tomar la referencia del espejo para acomodar el vehículo paralelo al cordón a menos de 30 cm sin golpear los obstáculos.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ParkParallelRight,
                0f,
                CockpitControlFlags.All,
                "VRGearShifter, SteeringWheel_Pivot");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson06_Step05_SecurePark",
                "Aseguramiento del Vehículo",
                "Apaga las balizas de emergencia, coloca la palanca en Parking (P) y aplica firmemente el freno de mano.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SecureParkDeactivateHazards,
                0f,
                CockpitControlFlags.All,
                "HazardButton, VRGearShifter, VRHandbrake");

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_06_PATH);
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                AssetDatabase.CreateAsset(lesson, LESSON_06_PATH);
            }

            lesson.lessonId = "LESSON_06";
            lesson.lessonTitle = "Lección 6: Maniobras de Estacionamiento y Precisión";
            lesson.steps = new List<LessonStepSO> { step1, step2, step3, step4, step5 };

            EditorUtility.SetDirty(lesson);
            AssetDatabase.SaveAssets();
            return lesson;
        }

        private static LessonStepSO CreateOrUpdateStep(string fileName, string title, string instruction,
            LessonStepCompletionMode mode, LessonActionType requiredAction, float actionNumericParameter,
            CockpitControlFlags allowedControls, string highlightTarget)
        {
            string path = $"{STEPS_DIR}/{fileName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                AssetDatabase.CreateAsset(step, path);
            }

            step.stepTitle = title;
            step.instructionText = instruction;
            step.completionMode = mode;
            step.requiredAction = requiredAction;
            step.actionNumericParameter = actionNumericParameter;
            step.allowedControls = allowedControls;
            step.highlightTargetName = highlightTarget;

            EditorUtility.SetDirty(step);
            return step;
        }

        private static void EnsureClonedSceneExists()
        {
            if (!File.Exists(TARGET_SCENE_PATH))
            {
                string source = File.Exists(ORIGINAL_SCENE_PATH) ? ORIGINAL_SCENE_PATH : Lesson01SceneSetup.ORIGINAL_SCENE_PATH;
                Debug.Log($"[Lesson06ParkingSetup] 📋 Clonando escena base desde {source} hacia {TARGET_SCENE_PATH}...");
                File.Copy(source, TARGET_SCENE_PATH, true);
                AssetDatabase.ImportAsset(TARGET_SCENE_PATH, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static GameObject EnsureCar09InScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.Contains("Car09") || root.GetComponent<VehicleController>() != null)
                {
                    return root;
                }
            }
            return UnityEngine.Object.FindAnyObjectByType<VehicleController>()?.gameObject;
        }

        private static TurnSignalExemptZone SetupTurnSignalExemptZone(Scene scene)
        {
            GameObject exemptObj = GameObject.Find("TurnSignalExempt_ParkingZone");
            if (exemptObj == null)
            {
                exemptObj = new GameObject("TurnSignalExempt_ParkingZone");
                SceneManager.MoveGameObjectToScene(exemptObj, scene);
            }

            int laneZoneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (laneZoneLayer >= 0)
            {
                exemptObj.layer = laneZoneLayer;
            }

            // Ubicada en el playón de maniobras (X=120, entre Z=60 y Z=120)
            exemptObj.transform.position = new Vector3(120f, 0f, 90f);

            BoxCollider box = exemptObj.GetComponent<BoxCollider>();
            if (box == null) box = exemptObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(35f, 4f, 75f);

            TurnSignalExemptZone zone = exemptObj.GetComponent<TurnSignalExemptZone>();
            if (zone == null) zone = exemptObj.AddComponent<TurnSignalExemptZone>();

            EditorUtility.SetDirty(exemptObj);
            return zone;
        }

        private static void BuildParkingStations(Scene scene,
            out ParkingZoneTrigger trigger45, out ParkingZoneTrigger trigger90, out ParkingZoneTrigger triggerParallel)
        {
            GameObject stationsContainer = GameObject.Find("Parking_Stations_Container");
            if (stationsContainer == null)
            {
                stationsContainer = new GameObject("Parking_Stations_Container");
                SceneManager.MoveGameObjectToScene(stationsContainer, scene);
            }

            GameObject conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);
            GameObject carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(STATIC_CAR_PREFAB_PATH);

            // -------------------------------------------------------------
            // Estación 1: Dársena a 45° (Espiga)
            // -------------------------------------------------------------
            GameObject station45Obj = EnsureChild(stationsContainer.transform, "Station_01_Diagonal45", new Vector3(123.5f, 0f, 105f), Quaternion.Euler(0f, 45f, 0f));
            trigger45 = station45Obj.GetComponent<ParkingZoneTrigger>();
            if (trigger45 == null) trigger45 = station45Obj.AddComponent<ParkingZoneTrigger>();
            trigger45.StationType = ParkingStationType.Diagonal45;
            BoxCollider col45 = station45Obj.GetComponent<BoxCollider>();
            col45.isTrigger = true;
            col45.center = new Vector3(0f, 1f, 0f);
            col45.size = new Vector3(3.2f, 2.5f, 6.0f);

            SpawnConesAround(station45Obj.transform, conePrefab, new Vector2(3.2f, 6.0f));
            SpawnStaticCar(station45Obj.transform, carPrefab, new Vector3(-3.2f, 0f, 0f), Quaternion.identity);
            SpawnStaticCar(station45Obj.transform, carPrefab, new Vector3(3.2f, 0f, 0f), Quaternion.identity);

            // -------------------------------------------------------------
            // Estación 2: Dársena a 90° de Culata (Perpendicular)
            // -------------------------------------------------------------
            GameObject station90Obj = EnsureChild(stationsContainer.transform, "Station_02_Bay90Reverse", new Vector3(123.5f, 0f, 88f), Quaternion.Euler(0f, 90f, 0f));
            trigger90 = station90Obj.GetComponent<ParkingZoneTrigger>();
            if (trigger90 == null) trigger90 = station90Obj.AddComponent<ParkingZoneTrigger>();
            trigger90.StationType = ParkingStationType.Bay90Reverse;
            BoxCollider col90 = station90Obj.GetComponent<BoxCollider>();
            col90.isTrigger = true;
            col90.center = new Vector3(0f, 1f, 0f);
            col90.size = new Vector3(3.2f, 2.5f, 6.0f);

            SpawnConesAround(station90Obj.transform, conePrefab, new Vector2(3.2f, 6.0f));
            SpawnStaticCar(station90Obj.transform, carPrefab, new Vector3(-3.2f, 0f, 0f), Quaternion.identity);
            SpawnStaticCar(station90Obj.transform, carPrefab, new Vector3(3.2f, 0f, 0f), Quaternion.identity);

            // -------------------------------------------------------------
            // Estación 3: Dársena en Paralelo al Cordón Derecho
            // -------------------------------------------------------------
            GameObject stationParallelObj = EnsureChild(stationsContainer.transform, "Station_03_ParallelRight", new Vector3(122.5f, 0f, 72f), Quaternion.Euler(0f, 180f, 0f));
            triggerParallel = stationParallelObj.GetComponent<ParkingZoneTrigger>();
            if (triggerParallel == null) triggerParallel = stationParallelObj.AddComponent<ParkingZoneTrigger>();
            triggerParallel.StationType = ParkingStationType.ParallelRight;
            BoxCollider colParallel = stationParallelObj.GetComponent<BoxCollider>();
            colParallel.isTrigger = true;
            colParallel.center = new Vector3(0f, 1f, 0f);
            colParallel.size = new Vector3(3.0f, 2.5f, 7.5f);

            // Autos estáticos delimitando el cajón paralelo: delantero y trasero
            SpawnStaticCar(stationParallelObj.transform, carPrefab, new Vector3(0f, 0f, 6.5f), Quaternion.identity);
            SpawnStaticCar(stationParallelObj.transform, carPrefab, new Vector3(0f, 0f, -6.5f), Quaternion.identity);
            SpawnConesAlongLine(stationParallelObj.transform, conePrefab, new Vector3(1.6f, 0f, -4.5f), new Vector3(1.6f, 0f, 4.5f), 3);
        }

        private static void SpawnConesAround(Transform parent, GameObject conePrefab, Vector2 size)
        {
            if (conePrefab == null) return;
            Vector3[] offsets = new Vector3[]
            {
                new Vector3(-size.x * 0.5f, 0f, -size.y * 0.5f),
                new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f),
                new Vector3(-size.x * 0.5f, 0f, size.y * 0.5f),
                new Vector3(size.x * 0.5f, 0f, size.y * 0.5f)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                string cName = $"Cone_{i}";
                Transform existing = parent.Find(cName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, parent);
                cone.name = cName;
                cone.transform.localPosition = offsets[i];
                cone.transform.localRotation = Quaternion.identity;

                EnsureObstacleDetector(cone, ObstacleKind.Cone);
            }
        }

        private static void SpawnConesAlongLine(Transform parent, GameObject conePrefab, Vector3 start, Vector3 end, int count)
        {
            if (conePrefab == null || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : (float)i / (count - 1);
                Vector3 pos = Vector3.Lerp(start, end, t);

                string cName = $"CurbCone_{i}";
                Transform existing = parent.Find(cName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, parent);
                cone.name = cName;
                cone.transform.localPosition = pos;
                cone.transform.localRotation = Quaternion.identity;

                EnsureObstacleDetector(cone, ObstacleKind.Cone);
            }
        }

        private static void SpawnStaticCar(Transform parent, GameObject carPrefab, Vector3 localPos, Quaternion localRot)
        {
            if (carPrefab == null) return;

            string carName = $"StaticCar_{localPos.x:F0}_{localPos.z:F0}";
            Transform existing = parent.Find(carName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, parent);
            car.name = carName;
            car.transform.localPosition = localPos;
            car.transform.localRotation = localRot;

            EnsureObstacleDetector(car, ObstacleKind.Vehicle);
        }

        private static void EnsureObstacleDetector(GameObject go, ObstacleKind kind)
        {
            Collider col = go.GetComponent<Collider>() ?? go.GetComponentInChildren<Collider>();
            if (col == null)
            {
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.size = (kind == ObstacleKind.Cone) ? new Vector3(0.4f, 0.7f, 0.4f) : new Vector3(2f, 1.5f, 4.5f);
                box.center = (kind == ObstacleKind.Cone) ? new Vector3(0f, 0.35f, 0f) : new Vector3(0f, 0.75f, 0f);
            }

            ObstacleCollisionDetector detector = go.GetComponent<ObstacleCollisionDetector>();
            if (detector == null) detector = go.AddComponent<ObstacleCollisionDetector>();

            var so = new SerializedObject(detector);
            SerializedProperty kindProp = so.FindProperty("m_Kind");
            if (kindProp != null) kindProp.enumValueIndex = (int)kind;
            so.ApplyModifiedProperties();
        }

        private static GameObject EnsureChild(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }
            child.position = pos;
            child.rotation = rot;
            return child.gameObject;
        }

        private static Transform[] EnsureSpawnPoints(Scene scene)
        {
            GameObject container = GameObject.Find("Lesson06_SpawnPoints");
            if (container == null)
            {
                container = new GameObject("Lesson06_SpawnPoints");
                SceneManager.MoveGameObjectToScene(container, scene);
            }

            Transform[] spawns = new Transform[5];

            // Paso 1: Aproximación con balizas
            spawns[0] = EnsureChildTransform(container.transform, "SpawnPoint_01_HazardApproach",
                new Vector3(120.0f, 0.02f, 115.0f), Quaternion.Euler(0f, 180f, 0f));

            // Paso 2: Entrada a dársena de 45°
            spawns[1] = EnsureChildTransform(container.transform, "SpawnPoint_02_Diagonal45",
                new Vector3(120.0f, 0.02f, 110.0f), Quaternion.Euler(0f, 180f, 0f));

            // Paso 3: Posición adelantada para maniobra de culata a 90°
            spawns[2] = EnsureChildTransform(container.transform, "SpawnPoint_03_Bay90Reverse",
                new Vector3(120.0f, 0.02f, 82.0f), Quaternion.Euler(0f, 180f, 0f));

            // Paso 4: Posición paralela al auto delantero para reversa en cordón
            spawns[3] = EnsureChildTransform(container.transform, "SpawnPoint_04_ParallelApproach",
                new Vector3(119.5f, 0.02f, 78.5f), Quaternion.Euler(0f, 180f, 0f));

            // Paso 5: Auto estacionado listo para asegurar
            spawns[4] = EnsureChildTransform(container.transform, "SpawnPoint_05_SecurePark",
                new Vector3(122.5f, 0.02f, 72.0f), Quaternion.Euler(0f, 180f, 0f));

            return spawns;
        }

        private static Transform EnsureChildTransform(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }
            child.position = pos;
            child.rotation = rot;
            return child;
        }

        private static void SetupLessonControllers(Scene scene, GameObject car09, LessonDefinitionSO lessonData,
            Transform[] spawnPoints, TurnSignalExemptZone exemptZone,
            ParkingZoneTrigger trigger45, ParkingZoneTrigger trigger90, ParkingZoneTrigger triggerParallel,
            VRScreenFader screenFader)
        {
            GameObject managerObj = GameObject.Find("Lesson_Manager");
            if (managerObj == null)
            {
                managerObj = new GameObject("Lesson_Manager");
                SceneManager.MoveGameObjectToScene(managerObj, scene);
            }

            LessonManager manager = managerObj.GetComponent<LessonManager>();
            if (manager == null) manager = managerObj.AddComponent<LessonManager>();

            LessonActionMonitor actionMonitor = managerObj.GetComponent<LessonActionMonitor>();
            if (actionMonitor == null) actionMonitor = managerObj.AddComponent<LessonActionMonitor>();

            VehicleController vc = car09.GetComponent<VehicleController>();
            LessonHUDPanel hud = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            DriverSeatFollower seatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();

            lessonData = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_06_PATH);

            // Configurar LessonManager
            var so = new SerializedObject(manager);
            so.FindProperty("m_ActiveLesson").objectReferenceValue = lessonData;
            so.FindProperty("m_AutoStartOnStart").boolValue = true;
            so.FindProperty("m_VehicleRoot").objectReferenceValue = car09.transform;
            so.FindProperty("m_VehicleController").objectReferenceValue = vc;
            so.FindProperty("m_HUDPanel").objectReferenceValue = hud;
            so.FindProperty("m_ActionMonitor").objectReferenceValue = actionMonitor;

            SerializedProperty currProp = so.FindProperty("m_Curriculum");
            if (currProp != null)
            {
                var list = new List<LessonDefinitionSO>();
                for (int l = 1; l <= 6; l++)
                {
                    string path = $"Assets/Scripts/Lessons/Curriculum/LessonData_0{l}.asset";
                    LessonDefinitionSO def = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(path);
                    if (def != null) list.Add(def);
                }
                currProp.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    currProp.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
                }
            }
            so.ApplyModifiedProperties();

            // Configurar Lesson06ParkingController
            Lesson06ParkingController ctrl = managerObj.GetComponent<Lesson06ParkingController>();
            if (ctrl == null) ctrl = managerObj.AddComponent<Lesson06ParkingController>();

            var soCtrl = new SerializedObject(ctrl);
            soCtrl.FindProperty("m_LessonManager").objectReferenceValue = manager;
            soCtrl.FindProperty("m_VehicleController").objectReferenceValue = vc;
            soCtrl.FindProperty("m_HUDPanel").objectReferenceValue = hud;
            soCtrl.FindProperty("m_ScreenFader").objectReferenceValue = screenFader;
            soCtrl.FindProperty("m_SeatFollower").objectReferenceValue = seatFollower;
            soCtrl.FindProperty("m_ExemptZone").objectReferenceValue = exemptZone;
            soCtrl.FindProperty("m_Diagonal45Zone").objectReferenceValue = trigger45;
            soCtrl.FindProperty("m_Bay90Zone").objectReferenceValue = trigger90;
            soCtrl.FindProperty("m_ParallelZone").objectReferenceValue = triggerParallel;

            SerializedProperty spawnsProp = soCtrl.FindProperty("m_StepSpawnPoints");
            if (spawnsProp != null && spawnPoints != null)
            {
                spawnsProp.arraySize = spawnPoints.Length;
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    spawnsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
                }
            }
            soCtrl.ApplyModifiedProperties();

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(ctrl);
        }
    }
}
