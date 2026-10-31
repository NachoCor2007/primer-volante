using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using PrimerVolante.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente para configurar y ensamblar la Lección 5:
    /// "Giros e Intersecciones" (TASK-301 / Issue #10 / PRI-46).
    /// Clona de forma segura Lesson_01_BasicDriving.unity a Assets/Scenes/Lessons/Lesson_05_TurnIntersections.unity,
    /// crea los ScriptableObjects correspondientes y configura la señalización dinámica (regla estricta de 20 m),
    /// la línea de advertencia en el asfalto, el cruce semaforizado y la mecánica de reintento suave en VR.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Lesson05TurnIntersectionsSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_05_TurnIntersections.unity";

        public const string CURRICULUM_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps/Lesson05";
        public const string LESSON_05_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_05.asset";

        public const string WARNING_GATE_MAT_PATH = "Assets/Environment/Materials/M_WarningGate_Translucent.mat";
        public const string YELLOW_ZONE_MAT_PATH = "Assets/Environment/Materials/M_Parking_YellowZone.mat";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 5 Scene")]
        public static void SetupLesson05()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [Lesson05TurnIntersectionsSetup] Iniciando configuración de Lección 5...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Crear o actualizar los ScriptableObjects de la Lección 5
            LessonDefinitionSO lessonData = CreateOrUpdateLesson05Assets();

            // 2. Asegurar que la escena clonada exista
            EnsureClonedSceneExists();

            // 3. Abrir la escena de la Lección 5 si no está abierta
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != TARGET_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson05TurnIntersectionsSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 4. Asegurar Car09 y sus componentes de cabina
            GameObject car09 = EnsureCar09InScene(scene);
            if (car09 == null)
            {
                Debug.LogError("[Lesson05TurnIntersectionsSetup] ❌ No se pudo encontrar o configurar Car09 en la escena.");
                return;
            }

            // 5. Configurar el detector de guiños en Car09
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

            // 7. Configurar puntos de spawn para cada paso de la lección
            Transform[] spawnPoints = EnsureSpawnPoints(scene);

            // 8. Posicionar Car09 en el punto de partida del Paso 1 (5 m antes de la esquina)
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

            // 9. Crear la línea de advertencia en el asfalto y el pórtico holográfico (a 20m de la esquina) para el Paso 2
            Transform warningLine = EnsureWarningLine20m(scene);

            // 10. Configurar o encontrar el semáforo para el Paso 3
            TrafficLightController trafficLight = UnityEngine.Object.FindAnyObjectByType<TrafficLightController>();

            // 11. Configurar Pantalla Diegética de GPS y Trazado de Ruta Urbana en Car09
            GPSRouteRenderer routeRenderer = SetupGPSCircuitInScene(scene, car09);

            // 12. Configurar LessonHUDPanel, LessonManager y Lesson05TurnIntersectionsController
            SetupLessonControllers(scene, car09, lessonData, spawnPoints, warningLine, trafficLight, screenFader, turnDetector, routeRenderer);

            // 13. Asegurar zonas exentas en prefabs e infracciones en la escena
            TurnSignalSetup.EnsureAssets(out _, out _);

            // 14. Guardar escena y assets
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("=================================================================");
            Debug.Log("✅ [Lesson05TurnIntersectionsSetup] ¡Lección 5 configurada exitosamente!");
            Debug.Log($"   Escena: {TARGET_SCENE_PATH}");
            Debug.Log($"   Asset:  {LESSON_05_PATH}");
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
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons/Steps", "Lesson05");
            }
        }

        public static LessonDefinitionSO CreateOrUpdateLesson05Assets()
        {
            EnsureDirectories();

            LessonStepSO step1 = CreateOrUpdateStep("Lesson05_Step01_TurnSignalIntro",
                "Giro a la Derecha (90°)",
                "1. Coloca el guiño hacia arriba (derecha).\n2. Avanza y dobla la esquina en tu carril.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.CompleteTurnSignalRightIntro,
                0f,
                CockpitControlFlags.All,
                "VRTurnSignal");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson05_Step02_TurnSignal20mDistance",
                "Anticipación 20 Metros",
                "Acelera y activa el guiño derecho ANTES de cruzar la franja en el asfalto. Completa el giro.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.TurnSignalAnticipation20m,
                20f,
                CockpitControlFlags.All,
                "VRTurnSignal");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson05_Step03_TrafficLightIntersection",
                "Cruce con Semáforo",
                "1. Detén el auto antes de la senda ante la luz roja.\n2. Espera la luz verde y cruza.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.CrossTrafficLightIntersection,
                0f,
                CockpitControlFlags.All,
                "");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson05_Step04_LeftTurnCrossing",
                "Giro a la Izquierda",
                "Coloca el guiño hacia abajo (izquierda) con anticipación y gira al carril derecho.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.CompleteLeftTurnCrossing,
                20f,
                CockpitControlFlags.All,
                "VRTurnSignal");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson05_Step05_CurbsideStop",
                "Detención en Cordón",
                "Arrimarse al cordón, frenar por completo, colocar Parking (P) y subir el freno de mano.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.CurbsideStop,
                0f,
                CockpitControlFlags.All,
                "VRHandbrake, VRGearShifter");

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_05_PATH);
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                AssetDatabase.CreateAsset(lesson, LESSON_05_PATH);
            }

            lesson.lessonId = "LESSON_05";
            lesson.lessonTitle = "Lección 5: Giros e Intersecciones";
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
                Debug.Log($"[Lesson05TurnIntersectionsSetup] 📋 Clonando escena base desde {source} hacia {TARGET_SCENE_PATH}...");
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

        private static Transform[] EnsureSpawnPoints(Scene scene)
        {
            GameObject container = GameObject.Find("Lesson05_SpawnPoints");
            if (container == null)
            {
                container = new GameObject("Lesson05_SpawnPoints");
                SceneManager.MoveGameObjectToScene(container, scene);
            }

            Transform[] spawns = new Transform[5];

            // Paso 1: Auto detenido a 5 m de la esquina norte (X=63.5, Z=110, rumbo Norte +Z)
            spawns[0] = EnsureChildTransform(container.transform, "SpawnPoint_01_TurnIntro",
                new Vector3(63.5f, 0.02f, 110.0f), Quaternion.Euler(0f, 0f, 0f));

            // Paso 2: Auto parte a 45 m de la siguiente esquina (X=66.0, Z=116.5, rumbo Este +X)
            spawns[1] = EnsureChildTransform(container.transform, "SpawnPoint_02_Turn20m",
                new Vector3(66.0f, 0.02f, 116.5f), Quaternion.Euler(0f, 90f, 0f));

            // Paso 3: Cruce semaforizado (acercándose al semáforo en X=116.5, Z=105, rumbo Sur -Z)
            spawns[2] = EnsureChildTransform(container.transform, "SpawnPoint_03_TrafficLight",
                new Vector3(116.5f, 0.02f, 105.0f), Quaternion.Euler(0f, 180f, 0f));

            // Paso 4: Giro a la izquierda a 90° (rumbo Sur aproximándose al cruce para girar al Este)
            spawns[3] = EnsureChildTransform(container.transform, "SpawnPoint_04_LeftTurn",
                new Vector3(116.5f, 0.02f, 75.0f), Quaternion.Euler(0f, 180f, 0f));

            // Paso 5: Arrime y detención en cordón derecho
            spawns[4] = EnsureChildTransform(container.transform, "SpawnPoint_05_CurbsideStop",
                new Vector3(105.0f, 0.02f, 63.5f), Quaternion.Euler(0f, 270f, 0f));

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

        private static Material EnsureWarningGateMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(WARNING_GATE_MAT_PATH);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = "M_WarningGate_Translucent";

                // Configurar como superficie transparente en URP
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);   // Alpha
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

                Color yellowTrans = new Color(1.0f, 0.85f, 0.0f, 0.35f);
                mat.color = yellowTrans;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellowTrans);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(1.0f, 0.85f, 0.0f) * 0.5f);
                }
                AssetDatabase.CreateAsset(mat, WARNING_GATE_MAT_PATH);
            }
            return mat;
        }

        private static Transform EnsureWarningLine20m(Scene scene)
        {
            GameObject lineObj = GameObject.Find("WarningLine_20m_Asphalt");
            if (lineObj == null)
            {
                lineObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                lineObj.name = "WarningLine_20m_Asphalt";
                SceneManager.MoveGameObjectToScene(lineObj, scene);
                Collider col = lineObj.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }

            // Ubicada transversalmente en el carril exactamente a 20 m antes de la esquina X=110.5
            lineObj.transform.position = new Vector3(90.5f, 0.03f, 116.5f);
            lineObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lineObj.transform.localScale = new Vector3(0.5f, 7.5f, 1f); // 0.5m de espesor, 7.5m ancho cruzando la calzada en Z

            MeshRenderer mr = lineObj.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material yellowMat = AssetDatabase.LoadAssetAtPath<Material>(YELLOW_ZONE_MAT_PATH);
                if (yellowMat != null)
                {
                    mr.sharedMaterial = yellowMat;
                }
            }

            // Crear o actualizar la pared / portal translúcido holográfico flotante de advertencia
            GameObject gateObj = GameObject.Find("WarningGate_20m_Hologram");
            if (gateObj == null)
            {
                gateObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                gateObj.name = "WarningGate_20m_Hologram";
                SceneManager.MoveGameObjectToScene(gateObj, scene);
                Collider col = gateObj.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }
            gateObj.transform.position = new Vector3(90.5f, 2.0f, 116.5f);
            gateObj.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            gateObj.transform.localScale = new Vector3(7.5f, 3.5f, 1f);

            MeshRenderer gateMr = gateObj.GetComponent<MeshRenderer>();
            if (gateMr != null)
            {
                Material holoMat = EnsureWarningGateMaterial();
                if (holoMat != null)
                {
                    gateMr.sharedMaterial = holoMat;
                }
            }

            return lineObj.transform;
        }

        private static GPSRouteRenderer SetupGPSCircuitInScene(Scene scene, GameObject car09)
        {
            // Asegurar capas requeridas para la cámara del GPS
            int routeLayer = GPSNavigationSceneSetup.EnsureLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
            int vehicleLayer = GPSNavigationSceneSetup.EnsureLayer(GPSNavigationSceneSetup.VEHICLE_LAYER_NAME);
            int roadsLayer = GPSNavigationSceneSetup.EnsureLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME);
            int buildingLayer = GPSNavigationSceneSetup.EnsureLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME);

            // Trazar el circuito urbano con SplineContainer en la escena
            SplineContainer splineContainer = GPSNavigationSceneSetup.BuildUrbanCircuitSpline(scene);

            // Cablear el SplineContainer a GPSRouteRenderer en Car09
            GPSRouteRenderer routeRenderer = car09.GetComponent<GPSRouteRenderer>() ?? car09.GetComponentInChildren<GPSRouteRenderer>(true);
            if (routeRenderer != null && splineContainer != null)
            {
                routeRenderer.RouteContainer = splineContainer;
                routeRenderer.InitializeRoute();
                EditorUtility.SetDirty(routeRenderer);
            }

            GPSController gpsCtrl = car09.GetComponent<GPSController>() ?? car09.GetComponentInChildren<GPSController>(true);
            if (gpsCtrl != null)
            {
                gpsCtrl.ResolveVehicle();
                gpsCtrl.ConfigureCameraTechnical();
                EditorUtility.SetDirty(gpsCtrl);
            }

            return routeRenderer;
        }

        private static void SetupLessonControllers(Scene scene, GameObject car09, LessonDefinitionSO lessonData,
            Transform[] spawnPoints, Transform warningLine, TrafficLightController trafficLight,
            VRScreenFader screenFader, TurnSignalDetector turnDetector, GPSRouteRenderer routeRenderer)
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
            WrongWayDetector wrongWay = car09.GetComponent<WrongWayDetector>();
            InfractionManager infractionMgr = UnityEngine.Object.FindAnyObjectByType<InfractionManager>();

            lessonData = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_05_PATH);

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

            // Configurar Lesson05TurnIntersectionsController
            Lesson05TurnIntersectionsController ctrl = managerObj.GetComponent<Lesson05TurnIntersectionsController>();
            if (ctrl == null) ctrl = managerObj.AddComponent<Lesson05TurnIntersectionsController>();

            var soCtrl = new SerializedObject(ctrl);
            soCtrl.FindProperty("m_LessonManager").objectReferenceValue = manager;
            soCtrl.FindProperty("m_VehicleController").objectReferenceValue = vc;
            soCtrl.FindProperty("m_HUDPanel").objectReferenceValue = hud;
            soCtrl.FindProperty("m_TurnSignalDetector").objectReferenceValue = turnDetector;
            soCtrl.FindProperty("m_WrongWayDetector").objectReferenceValue = wrongWay;
            soCtrl.FindProperty("m_ScreenFader").objectReferenceValue = screenFader;
            soCtrl.FindProperty("m_SeatFollower").objectReferenceValue = seatFollower;
            soCtrl.FindProperty("m_TrafficLight").objectReferenceValue = trafficLight;
            soCtrl.FindProperty("m_InfractionManager").objectReferenceValue = infractionMgr;
            soCtrl.FindProperty("m_WarningLine20m").objectReferenceValue = warningLine;

            GameObject warningGate = GameObject.Find("WarningGate_20m_Hologram");
            if (warningGate != null && soCtrl.FindProperty("m_WarningGateHologram") != null)
            {
                soCtrl.FindProperty("m_WarningGateHologram").objectReferenceValue = warningGate;
            }

            if (routeRenderer != null && soCtrl.FindProperty("m_RouteRenderer") != null)
            {
                soCtrl.FindProperty("m_RouteRenderer").objectReferenceValue = routeRenderer;
            }

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
