using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para configurar el tráfico vivo, vehículos de estacionamiento y eventos en Circuit_01:
    /// - Paso 1: Puebla las cocheras del estacionamiento inicial con vehículos estacionados estáticos (con ObstacleCollisionDetector).
    /// - Paso 2: Genera dos circuitos cerrados con vehículos NPC circulando EXCLUSIVAMENTE en sentido contrario al jugador.
    /// - Paso 3: Configura el evento scripteado en el Cruce (120, 60): trigger de aproximación, semáforo en rojo,
    ///           peatón cruzando por la senda y retorno a verde con detector de cruce en rojo.
    /// - Respeta 100% los bloqueos viales manuales de Street_Blockades sin modificarlos.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Circuit01TrafficSetup
    {
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Circuit_01.unity";
        public const string PARKING_ROOT_NAME = "Parking_Vehicles";
        public const string ONCOMING_ROOT_NAME = "Traffic_Oncoming";
        public const string SCRIPTED_ROOT_NAME = "ScriptedEvent_Intersection_120_60";

        public const string STATIC_VEHICLES_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels";
        public const string NPC_VEHICLES_FOLDER = "Assets/Prefabs/NPCs/Vehicles";
        public const string NPC_PEDESTRIANS_FOLDER = "Assets/Prefabs/NPCs/Pedestrians";

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup All Traffic and Events")]
        public static void SetupAllTrafficAndEvents()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚦 [Circuit01TrafficSetup] Configurando Tráfico Vivo y Eventos en Circuit_01...");
            Debug.Log("=================================================================");

            Scene scene = SceneManager.GetSceneByName("Circuit_01");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Circuit01TrafficSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 1. Paso 1: Autos estacionados en el playón inicial
            SetupParkingLotVehicles(scene);

            // 2. Paso 2: Tráfico en sentido contrario (Oncoming)
            SetupOncomingTraffic(scene);

            // 3. Paso 3: Evento scripteado de cruce peatonal y semáforo en Cruce (120, 60)
            SetupScriptedCrossingEvent(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("=================================================================");
            Debug.Log("✅ [Circuit01TrafficSetup] ¡Pasos 1, 2 y 3 configurados con éxito en Circuit_01!");
            Debug.Log("=================================================================");
        }

        [MenuItem("Tools/Primer Volante/Circuit 01/Inspect All NPC Paths")]
        public static void InspectAllNPCPaths()
        {
            var followers = UnityEngine.Object.FindObjectsByType<NPCPathFollower>(FindObjectsSortMode.None);
            Debug.Log($"=== NPC PATH FOLLOWER INSPECTION ({followers.Length} followers found) ===");
            foreach (var f in followers)
            {
                var pathName = f.Path != null ? f.Path.gameObject.name : "NULL";
                var sceneName = f.gameObject.scene.name;
                Debug.Log($"NPC: '{f.gameObject.name}' in Scene: '{sceneName}' -> Path: '{pathName}', CruiseSpeed: {f.CruiseSpeed}, Playing: {f.IsPlaying}");
            }
        }

        // ==============================================================================================
        // PASO 1: AUTOS ESTACIONADOS EN EL PLAYÓN INICIAL
        // ==============================================================================================

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Step 1: Parking Lot Vehicles")]
        public static void SetupParkingLotVehiclesMenu()
        {
            Scene scene = SceneManager.GetSceneByName("Circuit_01");
            if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.GetActiveScene();
            SetupParkingLotVehicles(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void SetupParkingLotVehicles(Scene scene)
        {
            GameObject root = GameObject.Find(PARKING_ROOT_NAME);
            if (root != null) Undo.DestroyObjectImmediate(root);

            root = new GameObject(PARKING_ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Parking Vehicles");
            SceneManager.MoveGameObjectToScene(root, scene);

            // Definición de cocheras y vehículos a ubicar
            var parkingSpecs = new (string prefabName, Vector3 pos, float yaw)[]
            {
                // Fila donde spawnea el jugador (X ≈ 196 a 226, Z ≈ 77.8, orientación NE ~32°)
                ("Vehicle_Taxi.prefab", new Vector3(196.9f, 0f, 77.8f), 32f),
                ("Vehicle_Pick up Truck_color02.prefab", new Vector3(202.7f, 0f, 77.8f), 32f),
                // (208.19, 78.22 es donde está Car09)
                ("Vehicle_SUV_color02.prefab", new Vector3(214.0f, 0f, 77.7f), 32f),
                ("Vehicle_Car_color01.prefab", new Vector3(219.8f, 0f, 77.7f), 32f),
                ("Vehicle_Car_color03.prefab", new Vector3(225.6f, 0f, 77.7f), 32f),

                // Fila opuesta (orientada hacia el SO ~212°)
                ("Vehicle_Car_color02.prefab", new Vector3(205.0f, 0f, 94.0f), 212f),
                ("Vehicle_Pick up Truck_color01.prefab", new Vector3(211.0f, 0f, 94.0f), 212f),
                ("Vehicle_SUV_color01.prefab", new Vector3(217.0f, 0f, 94.0f), 212f),

                // Camión contenedor estacionado al fondo junto a la pared
                ("Vehicle_Container_color02.prefab", new Vector3(228.24f, 0f, 63.3f), 0f)
            };

            int spawned = 0;
            foreach (var spec in parkingSpecs)
            {
                string path = $"{STATIC_VEHICLES_FOLDER}/{spec.prefabName}";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.name = "Parked_" + spec.prefabName.Replace(".prefab", "");
                instance.transform.position = spec.pos;
                instance.transform.rotation = Quaternion.Euler(0f, spec.yaw, 0f);

                // Asegurar BoxCollider
                BoxCollider box = instance.GetComponent<BoxCollider>();
                if (box == null) box = instance.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.75f, 0f);
                box.size = new Vector3(1.9f, 1.5f, 4.4f);

                // Asegurar ObstacleCollisionDetector para el sistema de infracciones
                ObstacleCollisionDetector detector = instance.GetComponent<ObstacleCollisionDetector>();
                if (detector == null) detector = instance.AddComponent<ObstacleCollisionDetector>();
                var so = new SerializedObject(detector);
                so.FindProperty("m_Kind").enumValueIndex = (int)ObstacleKind.Vehicle;
                so.ApplyModifiedProperties();

                spawned++;
            }

            Debug.Log($"[Circuit01TrafficSetup] 🅿️ Paso 1: {spawned} vehículos estacionados en el playón de inicio.");
        }

        // ==============================================================================================
        // PASO 2: TRÁFICO EN SENTIDO CONTRARIO (ONCOMING TRAFFIC)
        // ==============================================================================================

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Step 2: Oncoming Traffic")]
        public static void SetupOncomingTrafficMenu()
        {
            Scene scene = SceneManager.GetSceneByName("Circuit_01");
            if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.GetActiveScene();
            SetupOncomingTraffic(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void SetupOncomingTraffic(Scene scene)
        {
            GameObject root = GameObject.Find(ONCOMING_ROOT_NAME);
            if (root != null) Undo.DestroyObjectImmediate(root);

            root = new GameObject(ONCOMING_ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Oncoming Traffic");
            SceneManager.MoveGameObjectToScene(root, scene);

            // 1. Lazo Manzana Este [X: 120..240, Z: 0..60] (Tráfico en mano contraria a la salida y rectas 1, 2 y 3)
            // Circula en sentido horario:
            // - X=240 hacia el Norte (+Z) por carril X=243.5 (De frente al jugador que baja por 236.5)
            // - Z=60 hacia el Oeste (-X) por carril Z=56.5
            // - X=120 hacia el Sur (-Z) por carril X=116.5 (De frente al jugador que sube por 123.5)
            // - Z=0 hacia el Este (+X) por carril Z=-3.5 (De frente al jugador que va al Oeste por 3.5)
            Vector3[] eastLoopVertices = new Vector3[]
            {
                new Vector3(243.5f, 0f, 6.0f),
                new Vector3(243.5f, 0f, 54.0f),
                new Vector3(126.0f, 0f, 56.5f),
                new Vector3(116.5f, 0f, 6.0f)
            };

            SplineContainer eastContainer = CreateSplineLoop(root.transform, "Route_Oncoming_EastBlock", eastLoopVertices, 6.0f);
            float eastLength = eastContainer.CalculateLength(0);

            // Spawnear Colectivo y Taxi en la Manzana Este
            SpawnNPCVehicle(root.transform, scene, $"{NPC_VEHICLES_FOLDER}/NPC_Bus_color01.prefab",
                eastContainer, 0f, 7.5f, "NPC_Oncoming_East_Bus");
            SpawnNPCVehicle(root.transform, scene, $"{NPC_VEHICLES_FOLDER}/NPC_Taxi.prefab",
                eastContainer, eastLength * 0.5f, 7.5f, "NPC_Oncoming_East_Taxi");

            // 2. Lazo Manzana Oeste [X: 0..60, Z: 60..160] (Tráfico en mano contraria en Av. X=0 y Calle Z=160)
            // Circula en sentido horario:
            // - X=0 hacia el Sur (-Z) por carril X=-3.5 (De frente al jugador que sube al Norte por 3.5)
            // - Z=60 hacia el Este (+X) por carril Z=56.5
            // - X=60 hacia el Norte (+Z) por carril X=63.5
            // - Z=160 hacia el Oeste (-X) por carril Z=163.5 (De frente al jugador que va al Este por 156.5)
            Vector3[] westLoopVertices = new Vector3[]
            {
                new Vector3(-3.5f, 0f, 154.0f),
                new Vector3(-3.5f, 0f, 66.0f),
                new Vector3(54.0f, 0f, 56.5f),
                new Vector3(63.5f, 0f, 154.0f)
            };

            SplineContainer westContainer = CreateSplineLoop(root.transform, "Route_Oncoming_WestBlock", westLoopVertices, 6.0f);
            float westLength = westContainer.CalculateLength(0);

            // Spawnear Auto civil y Camioneta en la Manzana Oeste
            SpawnNPCVehicle(root.transform, scene, $"{NPC_VEHICLES_FOLDER}/NPC_Car_color01.prefab",
                westContainer, 0f, 7.5f, "NPC_Oncoming_West_Car");
            SpawnNPCVehicle(root.transform, scene, $"{NPC_VEHICLES_FOLDER}/NPC_SUV_color01.prefab",
                westContainer, westLength * 0.5f, 7.5f, "NPC_Oncoming_West_SUV");

            Debug.Log($"[Circuit01TrafficSetup] 🚗 Paso 2: 4 vehículos NPC en sentido contrario activos en dos circuitos cerrados.");
        }

        // ==============================================================================================
        // PASO 3: EVENTO SCRIPTEADO EN CRUCE (120, 60) (SEMÁFORO ROJO + PEATÓN)
        // ==============================================================================================

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Step 3: Scripted Crossing Event")]
        public static void SetupScriptedCrossingEventMenu()
        {
            Scene scene = SceneManager.GetSceneByName("Circuit_01");
            if (!scene.IsValid() || !scene.isLoaded) scene = SceneManager.GetActiveScene();
            SetupScriptedCrossingEvent(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void SetupScriptedCrossingEvent(Scene scene)
        {
            GameObject root = GameObject.Find(SCRIPTED_ROOT_NAME);
            if (root != null) Undo.DestroyObjectImmediate(root);

            root = new GameObject(SCRIPTED_ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Scripted Crossing Event");
            SceneManager.MoveGameObjectToScene(root, scene);

            // 1. Encontrar o asegurar el semáforo vehicular de la esquina X=120, Z=60
            // Props_Traffic Signal_big (8) está en (127.6, 52.4)
            TrafficLightController trafficLight = null;
            GameObject sigObj = GameObject.Find("Props_Traffic Signal_big (8)");
            if (sigObj != null) trafficLight = sigObj.GetComponent<TrafficLightController>();
            if (trafficLight == null) trafficLight = UnityEngine.Object.FindAnyObjectByType<TrafficLightController>();

            if (trafficLight != null)
            {
                trafficLight.AutoStart = false;
                trafficLight.SetState(TrafficLightState.Green);
                EditorUtility.SetDirty(trafficLight);
            }

            // 2. Crear Spline recta de cruce peatonal por la senda en Z = 54.0 (de vereda Este X=127.5 a vereda Oeste X=112.5)
            GameObject pathObj = new GameObject("Route_Pedestrian_Crosswalk");
            pathObj.transform.SetParent(root.transform, false);
            SplineContainer crosswalkSpline = pathObj.AddComponent<SplineContainer>();
            Spline s = new Spline { Closed = false };
            s.Add(new BezierKnot(new float3(127.5f, 0.2f, 54.0f), float3.zero, new float3(-2f, 0f, 0f)), TangentMode.Continuous);
            s.Add(new BezierKnot(new float3(125.0f, 0.0f, 54.0f), new float3(1f, 0f, 0f), new float3(-1f, 0f, 0f)), TangentMode.Continuous);
            s.Add(new BezierKnot(new float3(115.0f, 0.0f, 54.0f), new float3(1f, 0f, 0f), new float3(-1f, 0f, 0f)), TangentMode.Continuous);
            s.Add(new BezierKnot(new float3(112.5f, 0.2f, 54.0f), new float3(2f, 0f, 0f), float3.zero), TangentMode.Continuous);
            crosswalkSpline.Spline = s;

            // 3. Instanciar Peatón NPC
            string pedPrefabPath = $"{NPC_PEDESTRIANS_FOLDER}/NPC_Pedestrian_01.prefab";
            GameObject pedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pedPrefabPath);
            NPCPathFollower pedFollower = null;

            if (pedPrefab != null)
            {
                GameObject pedObj = (GameObject)PrefabUtility.InstantiatePrefab(pedPrefab, root.transform);
                pedObj.name = "NPC_Pedestrian_Crosswalk";
                pedObj.transform.position = new Vector3(127.5f, 0.2f, 54.0f);
                pedObj.transform.rotation = Quaternion.LookRotation(Vector3.left);

                pedFollower = pedObj.GetComponent<NPCPathFollower>();
                if (pedFollower != null)
                {
                    var so = new SerializedObject(pedFollower);
                    NPCPrefabSetup.RequireProperty(so, "m_Path").objectReferenceValue = crosswalkSpline;
                    NPCPrefabSetup.RequireProperty(so, "m_SplineIndex").intValue = 0;
                    NPCPrefabSetup.RequireProperty(so, "m_EndBehavior").enumValueIndex = (int)NPCPathEndBehavior.Stop;
                    NPCPrefabSetup.RequireProperty(so, "m_InitialDistance").floatValue = 0f;
                    NPCPrefabSetup.RequireProperty(so, "m_PlayOnStart").boolValue = false;
                    NPCPrefabSetup.RequireProperty(so, "m_CruiseSpeed").floatValue = 1.35f;
                    so.ApplyModifiedProperties();
                    pedFollower.SetPath(crosswalkSpline, 0);
                }

                ObstacleCollisionDetector obsDetector = pedObj.GetComponent<ObstacleCollisionDetector>();
                if (obsDetector == null) obsDetector = pedObj.AddComponent<ObstacleCollisionDetector>();
                var soObs = new SerializedObject(obsDetector);
                soObs.FindProperty("m_Kind").enumValueIndex = (int)ObstacleKind.Pedestrian;
                soObs.ApplyModifiedProperties();
            }

            // 4. Crear Stop Line con RedLightViolationDetector
            GameObject stopLineObj = new GameObject("StopLine_Crosswalk_120_60");
            stopLineObj.transform.SetParent(root.transform, false);
            stopLineObj.transform.position = new Vector3(123.5f, 0.05f, 51.5f);

            BoxCollider stopBox = stopLineObj.AddComponent<BoxCollider>();
            stopBox.isTrigger = true;
            stopBox.size = new Vector3(7.5f, 2.0f, 0.5f);

            RedLightViolationDetector redDetector = stopLineObj.AddComponent<RedLightViolationDetector>();
            redDetector.TrafficLight = trafficLight;

            // 5. Crear Trigger de Aproximación con Circuit01PedestrianCrossingEvent
            GameObject triggerObj = new GameObject("Trigger_PedestrianCrossingEvent");
            triggerObj.transform.SetParent(root.transform, false);
            triggerObj.transform.position = new Vector3(123.5f, 1.0f, 35.0f);

            BoxCollider trigBox = triggerObj.AddComponent<BoxCollider>();
            trigBox.isTrigger = true;
            trigBox.size = new Vector3(7.5f, 3.0f, 8.0f);

            Circuit01PedestrianCrossingEvent crossingCtrl = triggerObj.AddComponent<Circuit01PedestrianCrossingEvent>();
            var ctrlSo = new SerializedObject(crossingCtrl);
            ctrlSo.FindProperty("m_TrafficLight").objectReferenceValue = trafficLight;
            ctrlSo.FindProperty("m_Pedestrian").objectReferenceValue = pedFollower;
            ctrlSo.FindProperty("m_RedLightDetector").objectReferenceValue = redDetector;
            ctrlSo.ApplyModifiedProperties();

            Debug.Log("[Circuit01TrafficSetup] 🚦 Paso 3: Evento scripteado de semáforo y cruce peatonal configurado en Cruce (120, 60).");
        }

        // ==============================================================================================
        // HELPERS
        // ==============================================================================================

        private static SplineContainer CreateSplineLoop(Transform parent, string name, Vector3[] corners, float radius)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SplineContainer container = go.AddComponent<SplineContainer>();

            container.Spline = NPCDemoRoutesSetup.BuildRoundedLoop(corners, radius, null);
            return container;
        }

        private static void SpawnNPCVehicle(Transform parent, Scene scene, string prefabPath,
            SplineContainer route, float initialDist, float speed, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Circuit01TrafficSetup] ❌ No se encontró prefab NPC: {prefabPath}");
                return;
            }

            GameObject npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            npc.name = name;
            npc.transform.SetParent(parent, false);

            NPCPathFollower follower = npc.GetComponent<NPCPathFollower>();
            if (follower != null)
            {
                var so = new SerializedObject(follower);
                NPCPrefabSetup.RequireProperty(so, "m_Path").objectReferenceValue = route;
                NPCPrefabSetup.RequireProperty(so, "m_SplineIndex").intValue = 0;
                NPCPrefabSetup.RequireProperty(so, "m_EndBehavior").enumValueIndex = (int)NPCPathEndBehavior.Loop;
                NPCPrefabSetup.RequireProperty(so, "m_InitialDistance").floatValue = initialDist;
                NPCPrefabSetup.RequireProperty(so, "m_PlayOnStart").boolValue = true;
                NPCPrefabSetup.RequireProperty(so, "m_CruiseSpeed").floatValue = speed;
                so.ApplyModifiedProperties();

                follower.SetPath(route, 0);
            }
        }
    }
}

