using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.UI;
using PrimerVolante.Characters;
using PrimerVolante.Environment;
using PrimerVolante.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para la configuración de la pantalla diegética de GPS
    /// en la consola central de Car09 y la escena de prueba GPSNavigationDemo.unity.
    /// Fuente única de verdad: Car09.prefab.
    /// En la escena de prueba se eliminan duplicados y se cablea el circuito SplineContainer sin duplicar componentes.
    /// Responde a los requerimientos de US17, US18, US19 (Issue #16 / PRI-40).
    /// </summary>
    public static class GPSNavigationSceneSetup
    {
        public const string SOURCE_SCENE_PATH = "Assets/Scenes/car09/CityMerged3.unity";
        public const string DEMO_SCENE_DIR = "Assets/Scenes/TestingScenes";
        public const string DEMO_SCENE_PATH = "Assets/Scenes/TestingScenes/GPSNavigationDemo.unity";
        public const string WRONG_WAY_SOURCE_SCENE_PATH = WrongWaySetup.SCENE_PATH;
        public const string WRONG_WAY_DEMO_SCENE_PATH = "Assets/Scenes/TestingScenes/GPSWrongWayDemo.unity";
        private const float STADIUM_CENTER_X = 150f;
        private static readonly Vector3 WRONG_WAY_OBSTACLE_OFFSET = new Vector3(60f, 0f, 0f);
        public const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";

        public const string GPS_TEXTURES_DIR = "Assets/Textures/UI/GPS";
        public const string RT_PATH = GPS_TEXTURES_DIR + "/RT_GPS_Navigation.renderTexture";
        public const string MAT_PATH = GPS_TEXTURES_DIR + "/Mat_GPS_Route.mat";
        public const string ARROW_ICON_PATH = GPS_TEXTURES_DIR + "/icon_gps_arrow.png";
        public const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        public const string ROUTE_LAYER_NAME = "GPSRoute";
        public const string VEHICLE_LAYER_NAME = "Vehicle";
        public const string NPC_LAYER_NAME = "NPC";
        public const string ROADS_LAYER_NAME = "Roads";
        public const string BUILDING_LAYER_NAME = "Building";
        public const string NPC_BLOCK_ROUTE_NAME = "Route_Vehicle_Block";
        public const string NPC_GROUP_NAME = "NPC_Demo";
        public const string NPC_CAR_PREFAB_PATH = "Assets/Prefabs/NPCs/Vehicles/NPC_Car_color01.prefab";

        // Pose de GPS_Display_Canvas ajustada a mano en GPSNavigationDemo para no atravesar el tablero del auto
        public static readonly Vector3 CANVAS_LOCAL_POSITION = new Vector3(0.0401f, 0.9836f, 0.2631f);
        public static readonly Vector3 CANVAS_LOCAL_EULER = new Vector3(10.734f, 14.893f, 2.089f);
        public const string ROUTE_OBJECT_NAME = "GPS_Circuit_Route";

        [MenuItem("Tools/Primer Volante/GPS/Setup GPS Demo Scene")]
        public static void SetupGPSDemoScene()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [GPSNavigationSceneSetup] Configurando Escena GPSNavigationDemo...");
            Debug.Log("=================================================================");

            // 1. Asegurar layers requeridos en TagManager
            int routeLayer = EnsureLayer(ROUTE_LAYER_NAME);
            int vehicleLayer = EnsureLayer(VEHICLE_LAYER_NAME);
            int npcLayer = EnsureLayer(NPC_LAYER_NAME);
            int roadsLayer = EnsureLayer(ROADS_LAYER_NAME);
            int buildingLayer = EnsureLayer(BUILDING_LAYER_NAME);

            // 2. Asegurar recursos (RenderTexture, Material, Ícono de Flecha)
            EnsureDirectories();
            GetOrCreateRenderTexture(RT_PATH, 512, 512);
            GetOrCreateRouteMaterial(MAT_PATH);
            GetOrCreateArrowSprite(ARROW_ICON_PATH);

            // 3. PASO 1 (Fuente única de verdad): Configurar el Prefab Car09.prefab
            SetupGPSInPrefab();

            // 4. Asegurar que exista la escena clonada
            EnsureClonedSceneExists(SOURCE_SCENE_PATH, DEMO_SCENE_PATH);

            // 5-16. Configurar y guardar la escena de prueba
            if (!ConfigureGPSScene(DEMO_SCENE_PATH, routeLayer, vehicleLayer, npcLayer, roadsLayer, buildingLayer)) return;

            Debug.Log("=================================================================");
            Debug.Log("✅ [GPSNavigationSceneSetup] ¡Escena GPSNavigationDemo configurada sin duplicados!");
            Debug.Log("=================================================================");
        }

        /// <summary>
        /// Escena con GPS sobre CityWithWrongWay: suma el circuito del GPS a las infracciones, la calle de mano única,
        /// las vallas y los obstáculos. Obstacle_Demo se mueve a la calle x = 180 porque en x = 120 quedaba sobre el circuito.
        /// </summary>
        [MenuItem("Tools/Primer Volante/GPS/Setup GPS Wrong Way Demo Scene")]
        public static void SetupGPSWrongWayDemoScene()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [GPSNavigationSceneSetup] Configurando Escena GPSWrongWayDemo...");
            Debug.Log("=================================================================");

            int routeLayer = EnsureLayer(ROUTE_LAYER_NAME);
            int vehicleLayer = EnsureLayer(VEHICLE_LAYER_NAME);
            int npcLayer = EnsureLayer(NPC_LAYER_NAME);
            int roadsLayer = EnsureLayer(ROADS_LAYER_NAME);
            int buildingLayer = EnsureLayer(BUILDING_LAYER_NAME);

            // El GPS viene de Car09.prefab (configurado por SetupGPSInPrefab): no se vuelve a ensamblar acá.
            EnsureDirectories();
            EnsureClonedSceneExists(WRONG_WAY_SOURCE_SCENE_PATH, WRONG_WAY_DEMO_SCENE_PATH);

            if (!ConfigureGPSScene(WRONG_WAY_DEMO_SCENE_PATH, routeLayer, vehicleLayer, npcLayer, roadsLayer, buildingLayer,
                    MoveObstacleDemoOffCircuit)) return;

            Debug.Log("=================================================================");
            Debug.Log("✅ [GPSNavigationSceneSetup] ¡Escena GPSWrongWayDemo configurada sin duplicados!");
            Debug.Log("=================================================================");
        }

        // Obstacle_Demo (US33) está en la calle x = 120, entre z = 60 y z = 120, justo donde baja el circuito (x = 116.5).
        // Se corre a la paralela x = 180, del otro lado del estadio: misma pieza de calle y los mismos cruces, con un
        // camión y un patrullero decorativos estacionados que dejan un paso parecido al de la calle original.
        // Solo se mueven los que siguen al oeste del estadio, para que repetir el setup no los corra dos veces.
        private static void MoveObstacleDemoOffCircuit(Scene scene)
        {
            GameObject demo = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == ObstacleCollisionSetup.DEMO_GROUP_NAME) demo = root;
            }

            if (demo == null)
            {
                Debug.LogWarning($"[GPSNavigationSceneSetup] ⚠️ La escena no tiene {ObstacleCollisionSetup.DEMO_GROUP_NAME}: no hay obstáculos que mover.");
                return;
            }

            int moved = 0;
            foreach (Transform child in demo.transform)
            {
                if (child.position.x >= STADIUM_CENTER_X) continue;
                child.position += WRONG_WAY_OBSTACLE_OFFSET;
                EditorUtility.SetDirty(child);
                moved++;
            }

            Debug.Log($"[GPSNavigationSceneSetup] 🚧 {moved} obstáculos de {demo.name} movidos {WRONG_WAY_OBSTACLE_OFFSET.x:F0} m a la calle x = 180.");
        }

        // Pasos 5-16 de la escena de prueba. beforeSave aplica los ajustes propios de cada escena.
        private static bool ConfigureGPSScene(string scenePath, int routeLayer, int vehicleLayer, int npcLayer, int roadsLayer,
            int buildingLayer, System.Action<Scene> beforeSave = null)
        {
            // 5. Abrir la escena de prueba
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[GPSNavigationSceneSetup] ❌ No se pudo abrir la escena en {scenePath}");
                return false;
            }

            // 6. Localizar Car09 en la escena
            GameObject carObj = FindCar09InScene(scene);
            if (carObj == null)
            {
                Debug.LogError("[GPSNavigationSceneSetup] ❌ No se encontró la instancia de Car09 en la escena!");
                return false;
            }

            // 7. Limpieza estricta de duplicados y overrides locales en Car09 (la fuente de verdad es el prefab)
            PurgeDuplicateGPSObjects(scene, carObj);

            // Posicionar Car09 exactamente en el punto de partida del circuito (63.5, 0.02, 95.0) orientado hacia adelante (+Z)
            carObj.transform.position = new Vector3(63.5f, 0.02f, 95.0f);
            carObj.transform.rotation = Quaternion.identity;
            EditorUtility.SetDirty(carObj);

            // 8. Diseñar el circuito urbano con SplineContainer en la escena
            SplineContainer splineContainer = BuildUrbanCircuitSpline(scene);

            // 9. Cablear el SplineContainer al GPSRouteRenderer que ya proviene de Car09.prefab
            GPSRouteRenderer routeRenderer = carObj.GetComponent<GPSRouteRenderer>();
            if (routeRenderer == null) routeRenderer = carObj.GetComponentInChildren<GPSRouteRenderer>(true);
            if (routeRenderer != null && splineContainer != null)
            {
                routeRenderer.RouteContainer = splineContainer;
                routeRenderer.InitializeRoute();
                EditorUtility.SetDirty(routeRenderer);
            }

            GPSController gpsCtrl = carObj.GetComponent<GPSController>();
            if (gpsCtrl != null)
            {
                gpsCtrl.ResolveVehicle();
                gpsCtrl.ConfigureCameraTechnical();
                EditorUtility.SetDirty(gpsCtrl);
            }

            // 10. Asegurar que la cámara principal de VR no renderice la capa GPSRoute
            // 10. Asignar capa Vehicle a vehículos decorativos / estáticos de la ciudad
            AssignLayerToStaticVehicles(scene, vehicleLayer);

            // 11. Configurar ruta y auto NPC patrullando en loop cerca de la meta
            SetupNPCVehicleLoopNearDestination(scene, npcLayer);

            // 12. Asignación de capa Roads y Building al entorno (Enfoque Lista Blanca)
            ClassifyEnvironmentLayers(scene, roadsLayer, buildingLayer);

            // 13. Asignación recursiva de la capa NPC a todos los NPCs y peatones de la escena
            ApplyNPCLayerToAllNPCs(scene, npcLayer);

            // 14. Asegurar que la cámara principal de VR y cámaras del jugador vean todo el entorno real excepto GPSRoute
            ConfigureMainCameraVR(scene, routeLayer, npcLayer, vehicleLayer, roadsLayer, buildingLayer);

            // 15. Limpieza de simulador desktop y cámaras no-VR en la escena
            PurgeDesktopTesterAndPOVCamera(scene, carObj);

            // 16. Asegurar que Car09 en la escena esté en capa Default (0), con VehicleController habilitado y físicas no cinemáticas
            SetLayerRecursively(carObj, 0, excludeChildrenWithName: new[] { "GPS_Route_Line" });

            VehicleController carVehicleCtrl = carObj.GetComponent<VehicleController>();
            if (carVehicleCtrl != null)
            {
                carVehicleCtrl.enabled = true;
                EditorUtility.SetDirty(carVehicleCtrl);
            }

            Rigidbody carRb = carObj.GetComponent<Rigidbody>();
            if (carRb != null)
            {
                carRb.isKinematic = false;
                carRb.useGravity = true;
                EditorUtility.SetDirty(carRb);
            }

            beforeSave?.Invoke(scene);

            // 11. Guardar escena
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        [MenuItem("Tools/Primer Volante/GPS/Setup GPS in Car09 Prefab")]
        public static void SetupGPSInPrefab()
        {
            if (!File.Exists(PREFAB_PATH))
            {
                Debug.LogWarning($"[GPSNavigationSceneSetup] No se encontró el prefab en {PREFAB_PATH}");
                return;
            }

            EnsureDirectories();
            int routeLayer = EnsureLayer(ROUTE_LAYER_NAME);
            int vehicleLayer = EnsureLayer(VEHICLE_LAYER_NAME);
            int npcLayer = EnsureLayer(NPC_LAYER_NAME);
            int roadsLayer = EnsureLayer(ROADS_LAYER_NAME);
            int buildingLayer = EnsureLayer(BUILDING_LAYER_NAME);

            RenderTexture rt = GetOrCreateRenderTexture(RT_PATH, 512, 512);
            Material routeMat = GetOrCreateRouteMaterial(MAT_PATH);
            Sprite arrowSprite = GetOrCreateArrowSprite(ARROW_ICON_PATH);
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabRoot == null) return;

            try
            {
                ConfigureGPSInVehicle(prefabRoot, rt, routeMat, arrowSprite, fontAsset, routeLayer, vehicleLayer, npcLayer, roadsLayer, buildingLayer);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
                Debug.Log($"[GPSNavigationSceneSetup] ✅ Prefab {Path.GetFileName(PREFAB_PATH)} actualizado con pantalla diegética de GPS!");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        // =========================================================================
        // Limpieza Rigurosa de Duplicados y Overrides Locales
        // =========================================================================

        public static void PurgeDuplicateGPSObjects(Scene scene, GameObject carObj)
        {
            // 1. Limpieza de huérfanos sueltos en la raíz de la escena
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root != carObj && (root.name == "GPS_Display_Canvas" || root.name == "GPS_TopDownCamera" || root.name == "GPS_Route_Line" || root.name == "[Desktop_Cockpit_Tester]"))
                    {
                        root.SetActive(false);
                        Object.DestroyImmediate(root);
                    }
                }
            }

            if (carObj == null) return;

            // Destruir cualquier Desktop_POV_Camera en el auto
            foreach (Camera cam in carObj.GetComponentsInChildren<Camera>(true))
            {
                if (cam != null && cam.name == "Desktop_POV_Camera")
                {
                    cam.gameObject.SetActive(false);
                    Object.DestroyImmediate(cam.gameObject);
                }
            }

            // 2. Si carObj es una instancia de prefab, eliminar overrides de tipo AddedGameObject para el GPS
            if (PrefabUtility.IsPartOfPrefabInstance(carObj))
            {
                List<AddedGameObject> addedGos = PrefabUtility.GetAddedGameObjects(carObj);
                for (int i = addedGos.Count - 1; i >= 0; i--)
                {
                    var added = addedGos[i];
                    if (added.instanceGameObject != null)
                    {
                        string n = added.instanceGameObject.name;
                        if (n == "GPS_Display_Canvas" || n == "GPS_TopDownCamera" || n == "GPS_Route_Line")
                        {
                            added.instanceGameObject.SetActive(false);
                            Object.DestroyImmediate(added.instanceGameObject);
                        }
                    }
                }

                List<AddedComponent> addedComps = PrefabUtility.GetAddedComponents(carObj);
                for (int i = addedComps.Count - 1; i >= 0; i--)
                {
                    var added = addedComps[i];
                    if (added.instanceComponent != null)
                    {
                        if (added.instanceComponent is GPSController || added.instanceComponent is GPSRouteRenderer)
                        {
                            Object.DestroyImmediate(added.instanceComponent);
                        }
                    }
                }
            }

            // 3. Inspección directa de Transform en carObj por si existen duplicados
            string[] targetNames = new string[] { "GPS_Display_Canvas", "GPS_TopDownCamera", "GPS_Route_Line" };
            foreach (string targetName in targetNames)
            {
                List<GameObject> found = new List<GameObject>();
                foreach (Transform t in carObj.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.gameObject != carObj && t.name == targetName)
                    {
                        found.Add(t.gameObject);
                    }
                }

                if (found.Count > 1)
                {
                    // Conservar el que pertenece al prefab (no es override agregado)
                    GameObject keep = null;
                    foreach (var go in found)
                    {
                        if (!PrefabUtility.IsAddedGameObjectOverride(go))
                        {
                            keep = go;
                            break;
                        }
                    }
                    if (keep == null) keep = found[0];

                    for (int i = 0; i < found.Count; i++)
                    {
                        if (found[i] != keep)
                        {
                            found[i].SetActive(false);
                            Object.DestroyImmediate(found[i]);
                        }
                    }
                }
            }

            // 4. Limpieza de componentes duplicados en carObj
            var controllers = carObj.GetComponents<GPSController>();
            if (controllers.Length > 1)
            {
                for (int i = 1; i < controllers.Length; i++)
                {
                    Object.DestroyImmediate(controllers[i]);
                }
            }

            var renderers = carObj.GetComponents<GPSRouteRenderer>();
            if (renderers.Length > 1)
            {
                for (int i = 1; i < renderers.Length; i++)
                {
                    Object.DestroyImmediate(renderers[i]);
                }
            }
        }

        // =========================================================================
        // Ensamble Físico y de UI en el Vehículo (Solo ejecutado sobre Prefab)
        // =========================================================================

        public static void ConfigureGPSInVehicle(
            GameObject carRoot,
            RenderTexture rt,
            Material routeMat,
            Sprite arrowSprite,
            TMP_FontAsset fontAsset,
            int routeLayer,
            int vehicleLayer,
            int npcLayer = -1,
            int roadsLayer = -1,
            int buildingLayer = -1)
        {
            // Limpiar duplicados previos antes de ensamblar
            PurgeDuplicateGPSObjects(default, carRoot);

            // 1. Asignar capa Default (0) al auto e interactables de cabina, excepto GPS_Route_Line (GPSRoute)
            SetLayerRecursively(carRoot, 0, excludeChildrenWithName: new[] { "GPS_Route_Line" });

            // Eliminar cualquier Desktop_POV_Camera residual en el auto
            foreach (Camera cam in carRoot.GetComponentsInChildren<Camera>(true))
            {
                if (cam != null && cam.name == "Desktop_POV_Camera")
                {
                    Object.DestroyImmediate(cam.gameObject);
                }
            }

            // Asegurar que VehicleController esté habilitado y Rigidbody no sea cinemático
            var vehicleCtrl = carRoot.GetComponent<VehicleController>();
            if (vehicleCtrl != null)
            {
                vehicleCtrl.enabled = true;
                EditorUtility.SetDirty(vehicleCtrl);
            }
            var vehicleRb = carRoot.GetComponent<Rigidbody>();
            if (vehicleRb != null)
            {
                vehicleRb.isKinematic = false;
                vehicleRb.useGravity = true;
                EditorUtility.SetDirty(vehicleRb);
            }

            // 2. Ubicar o crear GPS_Display_Canvas
            Transform existingCanvas = carRoot.transform.Find("GPS_Display_Canvas");
            GameObject canvasObj;
            if (existingCanvas != null)
            {
                canvasObj = existingCanvas.gameObject;
                for (int i = canvasObj.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(canvasObj.transform.GetChild(i).gameObject);
                }
            }
            else
            {
                canvasObj = new GameObject("GPS_Display_Canvas");
                canvasObj.transform.SetParent(carRoot.transform, false);
            }

            // Dimensiones y ubicación sobre la consola central, arriba de HazardButton
            // HazardButton está en (0.0771, 0.8406, 0.2988)
            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            if (canvasRect == null) canvasRect = canvasObj.AddComponent<RectTransform>();

            canvasRect.localPosition = CANVAS_LOCAL_POSITION;
            // Inclinación hacia la vista del conductor
            canvasRect.localRotation = Quaternion.Euler(CANVAS_LOCAL_EULER);
            canvasRect.localScale = new Vector3(0.0004f, 0.0004f, 0.0004f);
            canvasRect.sizeDelta = new Vector2(480f, 360f);

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // 3. Bisel / Marco exterior oscuro
            GameObject bezelObj = new GameObject("Bezel_Frame");
            bezelObj.transform.SetParent(canvasObj.transform, false);
            RectTransform bezelRect = bezelObj.AddComponent<RectTransform>();
            bezelRect.anchorMin = new Vector2(0.5f, 0.5f);
            bezelRect.anchorMax = new Vector2(0.5f, 0.5f);
            bezelRect.anchoredPosition = Vector2.zero;
            bezelRect.sizeDelta = new Vector2(504f, 384f);
            Image bezelImg = bezelObj.AddComponent<Image>();
            bezelImg.color = new Color(0.08f, 0.09f, 0.11f, 1f);

            // 4. Panel de pantalla interior
            GameObject screenObj = new GameObject("Screen_Panel");
            screenObj.transform.SetParent(canvasObj.transform, false);
            RectTransform screenRect = screenObj.AddComponent<RectTransform>();
            screenRect.anchorMin = Vector2.zero;
            screenRect.anchorMax = Vector2.one;
            screenRect.offsetMin = Vector2.zero;
            screenRect.offsetMax = Vector2.zero;
            Image screenImg = screenObj.AddComponent<Image>();
            screenImg.color = new Color(0.12f, 0.14f, 0.16f, 1f);

            // 5. RawImage para el Mapa (RenderTexture)
            GameObject mapObj = new GameObject("Map_Display");
            mapObj.transform.SetParent(screenObj.transform, false);
            RectTransform mapRect = mapObj.AddComponent<RectTransform>();
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;
            RawImage rawImage = mapObj.AddComponent<RawImage>();
            rawImage.texture = rt;
            rawImage.color = Color.white;

            // 6. Flecha fija de navegación (apunta fija hacia arriba al centro)
            GameObject arrowObj = new GameObject("Navigation_Arrow");
            arrowObj.transform.SetParent(mapObj.transform, false);
            RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRect.anchoredPosition = Vector2.zero;
            arrowRect.sizeDelta = new Vector2(44f, 44f);
            arrowRect.localRotation = Quaternion.identity;
            Image arrowImg = arrowObj.AddComponent<Image>();
            if (arrowSprite != null) arrowImg.sprite = arrowSprite;
            arrowImg.color = Color.white;

            // 7. Barra Superior (Próxima Maniobra)
            GameObject headerObj = new GameObject("Header_ManeuverBar");
            headerObj.transform.SetParent(screenObj.transform, false);
            RectTransform headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 52f);
            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.06f, 0.12f, 0.18f, 0.94f);

            // Ícono indicador de maniobra
            GameObject maneuverIconObj = new GameObject("Maneuver_Icon");
            maneuverIconObj.transform.SetParent(headerObj.transform, false);
            RectTransform mIconRect = maneuverIconObj.AddComponent<RectTransform>();
            mIconRect.anchorMin = new Vector2(0f, 0.5f);
            mIconRect.anchorMax = new Vector2(0f, 0.5f);
            mIconRect.anchoredPosition = new Vector2(30f, 0f);
            mIconRect.sizeDelta = new Vector2(36f, 36f);
            TextMeshProUGUI mIconText = maneuverIconObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) mIconText.font = fontAsset;
            mIconText.text = "▲";
            mIconText.fontSize = 24f;
            mIconText.alignment = TextAlignmentOptions.Center;
            mIconText.color = new Color(0f, 0.9f, 1f, 1f);

            // Texto de maniobra
            GameObject maneuverTextObj = new GameObject("Maneuver_Text");
            maneuverTextObj.transform.SetParent(headerObj.transform, false);
            RectTransform mTextRect = maneuverTextObj.AddComponent<RectTransform>();
            mTextRect.anchorMin = new Vector2(0f, 0f);
            mTextRect.anchorMax = new Vector2(1f, 1f);
            mTextRect.offsetMin = new Vector2(58f, 0f);
            mTextRect.offsetMax = new Vector2(-15f, 0f);
            TextMeshProUGUI maneuverText = maneuverTextObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) maneuverText.font = fontAsset;
            maneuverText.text = "Continúe por la avenida";
            maneuverText.fontSize = 20f;
            maneuverText.fontStyle = FontStyles.Bold;
            maneuverText.alignment = TextAlignmentOptions.Left;
            maneuverText.color = new Color(0.92f, 0.95f, 0.98f, 1f);

            // 8. Barra Inferior (Distancia Restante y Destino)
            GameObject footerObj = new GameObject("Footer_InfoBar");
            footerObj.transform.SetParent(screenObj.transform, false);
            RectTransform footerRect = footerObj.AddComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = Vector2.zero;
            footerRect.sizeDelta = new Vector2(0f, 48f);
            Image footerBg = footerObj.AddComponent<Image>();
            footerBg.color = new Color(0.08f, 0.10f, 0.12f, 0.94f);

            // Distancia (izquierda)
            GameObject distObj = new GameObject("Distance_Value");
            distObj.transform.SetParent(footerObj.transform, false);
            RectTransform distRect = distObj.AddComponent<RectTransform>();
            distRect.anchorMin = new Vector2(0f, 0.5f);
            distRect.anchorMax = new Vector2(0f, 0.5f);
            distRect.anchoredPosition = new Vector2(80f, 0f);
            distRect.sizeDelta = new Vector2(130f, 40f);
            TextMeshProUGUI distText = distObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) distText.font = fontAsset;
            distText.text = "230 m";
            distText.fontSize = 24f;
            distText.fontStyle = FontStyles.Bold;
            distText.alignment = TextAlignmentOptions.Left;
            distText.color = new Color(0f, 0.9f, 1f, 1f);

            // Destino (derecha)
            GameObject destObj = new GameObject("Destination_Text");
            destObj.transform.SetParent(footerObj.transform, false);
            RectTransform destRect = destObj.AddComponent<RectTransform>();
            destRect.anchorMin = new Vector2(1f, 0.5f);
            destRect.anchorMax = new Vector2(1f, 0.5f);
            destRect.anchoredPosition = new Vector2(-120f, 0f);
            destRect.sizeDelta = new Vector2(210f, 40f);
            TextMeshProUGUI destText = destObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) destText.font = fontAsset;
            destText.text = "Destino: Sector Norte";
            destText.fontSize = 17f;
            destText.fontStyle = FontStyles.Bold;
            destText.alignment = TextAlignmentOptions.Right;
            destText.color = new Color(0.85f, 0.88f, 0.92f, 1f);

            // 9. Panel de Llegada (Overlay al arribar)
            GameObject arrivalObj = new GameObject("Arrival_Panel");
            arrivalObj.transform.SetParent(screenObj.transform, false);
            RectTransform arrivalRect = arrivalObj.AddComponent<RectTransform>();
            arrivalRect.anchorMin = new Vector2(0.5f, 0.5f);
            arrivalRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrivalRect.anchoredPosition = Vector2.zero;
            arrivalRect.sizeDelta = new Vector2(360f, 110f);
            Image arrivalBg = arrivalObj.AddComponent<Image>();
            arrivalBg.color = new Color(0.06f, 0.24f, 0.14f, 0.96f);

            GameObject arrivalTitleObj = new GameObject("Arrival_Title");
            arrivalTitleObj.transform.SetParent(arrivalObj.transform, false);
            RectTransform aTitleRect = arrivalTitleObj.AddComponent<RectTransform>();
            aTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            aTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            aTitleRect.anchoredPosition = new Vector2(0f, 14f);
            aTitleRect.sizeDelta = new Vector2(340f, 36f);
            TextMeshProUGUI aTitleText = arrivalTitleObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) aTitleText.font = fontAsset;
            aTitleText.text = "¡Llegaste a tu destino!";
            aTitleText.fontSize = 24f;
            aTitleText.fontStyle = FontStyles.Bold;
            aTitleText.alignment = TextAlignmentOptions.Center;
            aTitleText.color = new Color(0.36f, 1f, 0.55f, 1f);

            GameObject arrivalSubObj = new GameObject("Arrival_Subtitle");
            arrivalSubObj.transform.SetParent(arrivalObj.transform, false);
            RectTransform aSubRect = arrivalSubObj.AddComponent<RectTransform>();
            aSubRect.anchorMin = new Vector2(0.5f, 0.5f);
            aSubRect.anchorMax = new Vector2(0.5f, 0.5f);
            aSubRect.anchoredPosition = new Vector2(0f, -20f);
            aSubRect.sizeDelta = new Vector2(340f, 26f);
            TextMeshProUGUI aSubText = arrivalSubObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) aSubText.font = fontAsset;
            aSubText.text = "Recorrido completado con éxito";
            aSubText.fontSize = 15f;
            aSubText.alignment = TextAlignmentOptions.Center;
            aSubText.color = new Color(0.85f, 0.98f, 0.90f, 1f);

            arrivalObj.SetActive(false);

            // 10. Cámara Cenital Dedicada (GPS_TopDownCamera)
            Transform existingCam = carRoot.transform.Find("GPS_TopDownCamera");
            GameObject camObj = existingCam != null ? existingCam.gameObject : new GameObject("GPS_TopDownCamera");
            camObj.transform.SetParent(carRoot.transform, false);
            camObj.transform.localPosition = new Vector3(0f, 45f, 0f);
            camObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Camera topDownCam = camObj.GetComponent<Camera>();
            if (topDownCam == null) topDownCam = camObj.AddComponent<Camera>();
            topDownCam.orthographic = true;
            topDownCam.orthographicSize = 25f;
            topDownCam.nearClipPlane = 1f;
            topDownCam.farClipPlane = 120f;
            topDownCam.clearFlags = CameraClearFlags.SolidColor;
            topDownCam.backgroundColor = new Color(0.16f, 0.18f, 0.20f, 1f);
            topDownCam.targetTexture = rt;
            topDownCam.allowHDR = false;
            topDownCam.allowMSAA = false;

            // Culling mask con Enfoque de Lista Blanca (Allowlist):
            // ÚNICAMENTE renderizar Roads, Building y GPSRoute.
            // La capa Default (0) y todas las demás quedan automáticamente excluidas.
            int mask = 0;

            if (roadsLayer != -1) mask |= (1 << roadsLayer);
            else
            {
                int rLayer = LayerMask.NameToLayer(ROADS_LAYER_NAME);
                if (rLayer != -1) mask |= (1 << rLayer);
            }

            if (buildingLayer != -1) mask |= (1 << buildingLayer);
            else
            {
                int bLayer = LayerMask.NameToLayer(BUILDING_LAYER_NAME);
                if (bLayer != -1) mask |= (1 << bLayer);
            }

            if (routeLayer != -1) mask |= (1 << routeLayer);
            else
            {
                int rtLayer = LayerMask.NameToLayer(ROUTE_LAYER_NAME);
                if (rtLayer != -1) mask |= (1 << rtLayer);
            }

            topDownCam.cullingMask = mask;

            // Eliminar AudioListener
            AudioListener listener = camObj.GetComponent<AudioListener>();
            if (listener != null) Object.DestroyImmediate(listener);

            // Configurar UniversalAdditionalCameraData (URP) sin sombras para rendimiento en VR
            UniversalAdditionalCameraData camData = camObj.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null) camData = camObj.AddComponent<UniversalAdditionalCameraData>();
            camData.renderShadows = false;
            camData.renderPostProcessing = false;
            camData.requiresColorOption = CameraOverrideOption.Off;
            camData.requiresDepthOption = CameraOverrideOption.Off;

            // Deshabilitar la cámara para refresco manual a 30 FPS
            topDownCam.enabled = false;

            // 11. Componente LineRenderer para la ruta (GPS_Route_Line)
            Transform lineObjTransform = carRoot.transform.Find("GPS_Route_Line");
            GameObject lineObj = lineObjTransform != null ? lineObjTransform.gameObject : new GameObject("GPS_Route_Line");
            lineObj.transform.SetParent(carRoot.transform, false);
            if (routeLayer != -1) lineObj.layer = routeLayer;

            LineRenderer line = lineObj.GetComponent<LineRenderer>();
            if (line == null) line = lineObj.AddComponent<LineRenderer>();
            line.sharedMaterial = routeMat;
            line.startWidth = 2.8f;
            line.endWidth = 2.8f;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // 12. Componente GPSRouteRenderer
            GPSRouteRenderer routeRenderer = carRoot.GetComponent<GPSRouteRenderer>();
            if (routeRenderer == null) routeRenderer = carRoot.AddComponent<GPSRouteRenderer>();

            SerializedObject soRoute = new SerializedObject(routeRenderer);
            soRoute.Update();
            soRoute.FindProperty("m_VehicleTransform").objectReferenceValue = carRoot.transform;
            soRoute.FindProperty("m_LineRenderer").objectReferenceValue = line;
            soRoute.FindProperty("m_SampleStep").floatValue = 1.0f;
            soRoute.FindProperty("m_RouteHeight").floatValue = 0.20f;
            soRoute.FindProperty("m_ArrivalThreshold").floatValue = 4.0f;
            soRoute.FindProperty("m_RouteLayerName").stringValue = ROUTE_LAYER_NAME;
            soRoute.ApplyModifiedProperties();

            // 13. Componente GPSController
            GPSController gpsCtrl = carRoot.GetComponent<GPSController>();
            if (gpsCtrl == null) gpsCtrl = carRoot.AddComponent<GPSController>();

            SerializedObject soGps = new SerializedObject(gpsCtrl);
            soGps.Update();
            soGps.FindProperty("m_VehicleTransform").objectReferenceValue = carRoot.transform;
            soGps.FindProperty("m_TopDownCamera").objectReferenceValue = topDownCam;
            soGps.FindProperty("m_MapRenderTexture").objectReferenceValue = rt;
            soGps.FindProperty("m_MapDisplay").objectReferenceValue = rawImage;
            soGps.FindProperty("m_CameraHeight").floatValue = 45f;
            soGps.FindProperty("m_OrthographicSize").floatValue = 25f;
            soGps.FindProperty("m_RotationSmoothSpeed").floatValue = 8f;
            soGps.FindProperty("m_TargetFrameRate").floatValue = 30f;
            soGps.FindProperty("m_RouteRenderer").objectReferenceValue = routeRenderer;
            soGps.FindProperty("m_NavigationArrow").objectReferenceValue = arrowRect;
            soGps.FindProperty("m_DistanceValueText").objectReferenceValue = distText;
            soGps.FindProperty("m_ManeuverText").objectReferenceValue = maneuverText;
            soGps.FindProperty("m_DestinationText").objectReferenceValue = destText;
            soGps.FindProperty("m_ArrivalPanel").objectReferenceValue = arrivalObj;
            soGps.FindProperty("m_ArrivalTitleText").objectReferenceValue = aTitleText;
            soGps.ApplyModifiedProperties();

            // 14. Aviso de infracción (US24): el canvas se reconstruyó desde cero, así que se vuelve a agregar acá.
            ConsoleAlertSetup.EnsureConsoleAlert(carRoot, fontAsset);
        }

        // =========================================================================
        // Circuito Urbano (SplineContainer)
        // =========================================================================

        public static SplineContainer BuildUrbanCircuitSpline(Scene scene)
        {
            GameObject routeRoot = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == ROUTE_OBJECT_NAME)
                {
                    routeRoot = root;
                    break;
                }
            }

            if (routeRoot == null)
            {
                routeRoot = new GameObject(ROUTE_OBJECT_NAME);
                SceneManager.MoveGameObjectToScene(routeRoot, scene);
            }

            routeRoot.transform.position = Vector3.zero;
            routeRoot.transform.rotation = Quaternion.identity;
            routeRoot.transform.localScale = Vector3.one;

            SplineContainer container = routeRoot.GetComponent<SplineContainer>();
            if (container == null) container = routeRoot.AddComponent<SplineContainer>();

            // Construir la spline
            Spline spline = new Spline();
            spline.Closed = false;

            const float R = 6.0f;
            const float K = 0.55228475f * R; // ~3.3137m
            const float Y = 0.02f;

            var knots = new List<BezierKnot>
            {
                new BezierKnot(new float3(63.5f, Y, 95.0f), float3.zero, new float3(0f, 0f, 5f), quaternion.identity),
                new BezierKnot(new float3(63.5f, Y, 110.5f), new float3(0f, 0f, -5f), new float3(0f, 0f, K), quaternion.identity),
                new BezierKnot(new float3(69.5f, Y, 116.5f), new float3(-K, 0f, 0f), new float3(5f, 0f, 0f), quaternion.identity),
                new BezierKnot(new float3(110.5f, Y, 116.5f), new float3(-5f, 0f, 0f), new float3(K, 0f, 0f), quaternion.identity),
                new BezierKnot(new float3(116.5f, Y, 110.5f), new float3(0f, 0f, K), new float3(0f, 0f, -5f), quaternion.identity),
                new BezierKnot(new float3(116.5f, Y, 69.5f), new float3(0f, 0f, 5f), new float3(0f, 0f, -K), quaternion.identity),
                new BezierKnot(new float3(110.5f, Y, 63.5f), new float3(K, 0f, 0f), new float3(-5f, 0f, 0f), quaternion.identity),
                new BezierKnot(new float3(65.0f, Y, 63.5f), new float3(5f, 0f, 0f), float3.zero, quaternion.identity)
            };

            foreach (var knot in knots)
            {
                spline.Add(knot, TangentMode.Broken);
            }

            container.Spline = spline;
            EditorUtility.SetDirty(container);
            Debug.Log($"[GPSNavigationSceneSetup] 📍 Circuito urbano creado con {knots.Count} nudos (longitud: {container.CalculateLength(0):F1} m).");
            return container;
        }

        // =========================================================================
        // =========================================================================
        // Asignación de Capa Vehicle a Vehículos Decorativos / Estáticos
        // =========================================================================

        public static void AssignLayerToStaticVehicles(Scene scene, int vehicleLayer)
        {
            if (!scene.IsValid() || !scene.isLoaded || vehicleLayer == -1) return;

            List<GameObject> staticVehicles = new List<GameObject>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                FindStaticVehiclesRecursive(root, staticVehicles);
            }

            int count = staticVehicles.Count;
            foreach (GameObject vehicle in staticVehicles)
            {
                if (vehicle != null)
                {
                    SetLayerRecursively(vehicle, vehicleLayer);
                    EditorUtility.SetDirty(vehicle);
                }
            }

            Debug.Log($"[GPSNavigationSceneSetup] 🚗 Capa '{VEHICLE_LAYER_NAME}' (layer {vehicleLayer}) asignada a {count} vehículos decorativos estáticos.");
        }

        private static void FindStaticVehiclesRecursive(GameObject obj, List<GameObject> staticVehicles)
        {
            if (obj == null) return;

            // Preservar Car09 y el vehículo del jugador
            if (obj.name.Contains("Car09") || obj.GetComponent<VehicleController>() != null)
            {
                return;
            }

            // Preservar cualquier NPC o parte de un NPC
            if (obj.name.StartsWith("NPC_") ||
                obj.GetComponent<NPCPathFollower>() != null ||
                obj.GetComponent<NPCPedestrianController>() != null ||
                obj.GetComponent<NPCVehicleController>() != null)
            {
                return;
            }

            // Identificar vehículos decorativos/estáticos de la ciudad original (Vehicle_*)
            // ej. Vehicle_Car_color*, Vehicle_Police Car*, Vehicle_Taxi*, Vehicle_SUV*, etc.
            if (obj.name.StartsWith("Vehicle_") ||
                (obj.name.StartsWith("Vehicle") && obj.GetComponent<NPCPathFollower>() == null))
            {
                staticVehicles.Add(obj);
                return; // Asignar capa recursivamente al vehículo decorativo completo
            }

            foreach (Transform child in obj.transform)
            {
                FindStaticVehiclesRecursive(child.gameObject, staticVehicles);
            }
        }

        // =========================================================================
        // Clasificación de Entorno (Lista Blanca: Calles y Edificios)
        // =========================================================================

        public static void ClassifyEnvironmentLayers(Scene scene, int roadsLayer, int buildingLayer)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            List<GameObject> roadRoots = new List<GameObject>();
            List<GameObject> buildingRoots = new List<GameObject>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                FindEnvironmentObjectsRecursive(root, roadRoots, buildingRoots);
            }

            int roadsCount = 0;
            foreach (GameObject r in roadRoots)
            {
                if (r != null && roadsLayer != -1)
                {
                    // Las zonas de carril de contramano conservan su capa LaneZone (WrongWayDetector solo mira esa capa).
                    SetLayerRecursively(r, roadsLayer, excludeChildrenWithName: new[] { WrongWaySetup.LANE_ZONES_NAME });
                    EditorUtility.SetDirty(r);
                    roadsCount++;
                }
            }

            int buildingsCount = 0;
            foreach (GameObject b in buildingRoots)
            {
                if (b != null && buildingLayer != -1)
                {
                    SetLayerRecursively(b, buildingLayer);
                    EditorUtility.SetDirty(b);
                    buildingsCount++;
                }
            }

            Debug.Log($"[GPSNavigationSceneSetup] 🏙️ Clasificación de Entorno completada: {roadsCount} objetos en '{ROADS_LAYER_NAME}' (layer {roadsLayer}) y {buildingsCount} en '{BUILDING_LAYER_NAME}' (layer {buildingLayer}).");
        }

        private static void FindEnvironmentObjectsRecursive(GameObject obj, List<GameObject> roads, List<GameObject> buildings)
        {
            if (obj == null) return;

            // Preservar Car09 y el vehículo del jugador
            if (obj.name.Contains("Car09") || obj.GetComponent<VehicleController>() != null)
            {
                return;
            }

            // Preservar cualquier vehículo (estático o NPC)
            if (obj.name.StartsWith("Vehicle_") ||
                (obj.name.StartsWith("Vehicle") && obj.GetComponent<NPCPathFollower>() == null))
            {
                return;
            }

            // Preservar cualquier NPC o parte de un NPC
            if (obj.name.StartsWith("NPC_") ||
                obj.name == NPC_GROUP_NAME ||
                obj.GetComponent<NPCPathFollower>() != null ||
                obj.GetComponent<NPCPedestrianController>() != null ||
                obj.GetComponent<NPCVehicleController>() != null)
            {
                return;
            }

            // Preservar componentes de sistema, cámaras, luces, testers y rutas de Spline
            if (obj.name == "GPS_Circuit_Route" ||
                obj.name.StartsWith("Route_") ||
                obj.name.StartsWith("[Desktop") ||
                obj.name.StartsWith("Trigger_") ||
                obj.name.StartsWith("Controlled_") ||
                obj.name.StartsWith("StopLine_") ||
                obj.name == "Barriers" ||
                obj.GetComponent<Camera>() != null ||
                obj.GetComponent<Light>() != null)
            {
                return;
            }

            string name = obj.name;

            // Edificios: nombre comienza o contiene "Building"
            if (name.IndexOf("Building", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                buildings.Add(obj);
                return;
            }

            // Calles / Asfalto: nombre comienza o contiene Road, Lane, Tile, Intersection, Sidewalk, Concrete Tile o Ground
            bool isRoad = name.IndexOf("Road", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          name.IndexOf("Lane", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          name.IndexOf("Tile", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          name.IndexOf("Intersection", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          name.IndexOf("Sidewalk", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          name.IndexOf("Concrete Tile", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          name.IndexOf("Ground", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (isRoad)
            {
                roads.Add(obj);
                return;
            }

            foreach (Transform child in obj.transform)
            {
                FindEnvironmentObjectsRecursive(child.gameObject, roads, buildings);
            }
        }

        // Circuito y Vehículo NPC en Loop Cerca de la Meta (US17/US18)
        // =========================================================================

        public static void SetupNPCVehicleLoopNearDestination(Scene scene, int npcLayer)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            // 1. Ubicar o crear grupo NPC_Demo
            GameObject npcGroup = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == NPC_GROUP_NAME)
                {
                    npcGroup = root;
                    break;
                }
            }

            if (npcGroup == null)
            {
                npcGroup = new GameObject(NPC_GROUP_NAME);
                SceneManager.MoveGameObjectToScene(npcGroup, scene);
            }

            // 2. Ubicar o crear Route_Vehicle_Block
            Transform blockRouteTransform = npcGroup.transform.Find(NPC_BLOCK_ROUTE_NAME);
            GameObject blockRouteObj = blockRouteTransform != null ? blockRouteTransform.gameObject : null;
            if (blockRouteObj == null)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == NPC_BLOCK_ROUTE_NAME)
                    {
                        blockRouteObj = root;
                        blockRouteObj.transform.SetParent(npcGroup.transform, false);
                        break;
                    }
                }
            }

            if (blockRouteObj == null)
            {
                blockRouteObj = new GameObject(NPC_BLOCK_ROUTE_NAME);
                blockRouteObj.transform.SetParent(npcGroup.transform, false);
            }

            blockRouteObj.transform.localPosition = Vector3.zero;
            blockRouteObj.transform.localRotation = Quaternion.identity;
            blockRouteObj.transform.localScale = Vector3.one;

            SplineContainer routeContainer = blockRouteObj.GetComponent<SplineContainer>();
            if (routeContainer == null) routeContainer = blockRouteObj.AddComponent<SplineContainer>();

            // Manzana contigua al destino (X in [60, 120], Z in [0, 60], carril 3.5m)
            // Vértices: (63.5, 0, 3.5), (63.5, 0, 56.5), (116.5, 0, 56.5), (116.5, 0, 3.5) con radio 6m
            Vector3[] carLoop = new Vector3[]
            {
                new Vector3(63.5f, 0f, 3.5f),
                new Vector3(63.5f, 0f, 56.5f),
                new Vector3(116.5f, 0f, 56.5f),
                new Vector3(116.5f, 0f, 3.5f),
            };

            Spline loopSpline = NPCDemoRoutesSetup.BuildRoundedLoop(carLoop, 6.0f, null);
            routeContainer.Spline = loopSpline;
            EditorUtility.SetDirty(routeContainer);
            EditorUtility.SetDirty(blockRouteObj);

            // 3. Buscar o instanciar vehículo NPC (NPC_Car_color01)
            Transform carTransform = npcGroup.transform.Find("NPC_Car_color01");
            GameObject npcCar = carTransform != null ? carTransform.gameObject : null;

            if (npcCar == null)
            {
                foreach (var follower in UnityEngine.Object.FindObjectsByType<NPCPathFollower>())
                {
                    if (follower.name.Contains("NPC_Car_color01"))
                    {
                        npcCar = follower.gameObject;
                        npcCar.transform.SetParent(npcGroup.transform, true);
                        break;
                    }
                }
            }

            if (npcCar == null)
            {
                var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NPC_CAR_PREFAB_PATH);
                if (carPrefab != null)
                {
                    npcCar = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, scene);
                    npcCar.name = "NPC_Car_color01";
                    npcCar.transform.SetParent(npcGroup.transform, false);
                    Debug.Log("[GPSNavigationSceneSetup] 🚙 Instanciado prefab NPC_Car_color01.");
                }
            }

            if (npcCar != null)
            {
                var follower = npcCar.GetComponent<NPCPathFollower>();
                if (follower != null)
                {
                    SerializedObject so = new SerializedObject(follower);
                    so.Update();
                    so.FindProperty("m_Path").objectReferenceValue = routeContainer;
                    so.FindProperty("m_SplineIndex").intValue = 0;
                    so.FindProperty("m_EndBehavior").enumValueIndex = (int)NPCPathEndBehavior.Loop;
                    so.FindProperty("m_PlayOnStart").boolValue = true;
                    so.FindProperty("m_CruiseSpeed").floatValue = 7.0f; // ~25 km/h
                    so.FindProperty("m_InitialDistance").floatValue = 0f;
                    so.ApplyModifiedProperties();

                    follower.SetPath(routeContainer, 0);
                    follower.RefreshPath();
                    follower.TeleportToDistance(0f);
                    EditorUtility.SetDirty(follower);
                }

                if (npcLayer != -1)
                {
                    SetLayerRecursively(npcCar, npcLayer);
                }
                EditorUtility.SetDirty(npcCar);
                Debug.Log("[GPSNavigationSceneSetup] 🚙 NPC_Car_color01 configurado en loop patrullando manzana contigua.");
            }

            // Si también existe NPC_Taxi, configurarlo en loop con desfase
            Transform taxiTransform = npcGroup.transform.Find("NPC_Taxi");
            if (taxiTransform != null)
            {
                var taxiFollower = taxiTransform.GetComponent<NPCPathFollower>();
                if (taxiFollower != null)
                {
                    float halfDist = routeContainer.CalculateLength(0) * 0.5f;
                    SerializedObject soTaxi = new SerializedObject(taxiFollower);
                    soTaxi.Update();
                    soTaxi.FindProperty("m_Path").objectReferenceValue = routeContainer;
                    soTaxi.FindProperty("m_SplineIndex").intValue = 0;
                    soTaxi.FindProperty("m_EndBehavior").enumValueIndex = (int)NPCPathEndBehavior.Loop;
                    soTaxi.FindProperty("m_PlayOnStart").boolValue = true;
                    soTaxi.FindProperty("m_CruiseSpeed").floatValue = 7.0f;
                    soTaxi.FindProperty("m_InitialDistance").floatValue = halfDist;
                    soTaxi.ApplyModifiedProperties();

                    taxiFollower.SetPath(routeContainer, 0);
                    taxiFollower.RefreshPath();
                    taxiFollower.TeleportToDistance(halfDist);
                    EditorUtility.SetDirty(taxiFollower);
                }
                if (npcLayer != -1)
                {
                    SetLayerRecursively(taxiTransform.gameObject, npcLayer);
                }
            }
        }

        // =========================================================================
        // Asignación de Capa NPC a todos los NPCs y Peatones
        // =========================================================================

        public static void ApplyNPCLayerToAllNPCs(Scene scene, int npcLayer)
        {
            if (!scene.IsValid() || !scene.isLoaded || npcLayer == -1) return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                ApplyNPCLayerRecursive(root, npcLayer);
            }

            foreach (var pedestrian in Object.FindObjectsByType<NPCPedestrianController>())
            {
                SetLayerRecursively(pedestrian.gameObject, npcLayer);
            }
            foreach (var customizer in Object.FindObjectsByType<UrbanManCustomizer>())
            {
                SetLayerRecursively(customizer.gameObject, npcLayer);
            }
            foreach (var vehicle in Object.FindObjectsByType<NPCVehicleController>())
            {
                SetLayerRecursively(vehicle.gameObject, npcLayer);
            }

            Debug.Log($"[GPSNavigationSceneSetup] 👥 Capa '{NPC_LAYER_NAME}' (layer {npcLayer}) asignada a todos los NPCs y peatones.");
        }

        private static void ApplyNPCLayerRecursive(GameObject obj, int npcLayer)
        {
            if (obj == null) return;

            // Preservar Car09
            if (obj.name.Contains("Car09") || obj.GetComponent<VehicleController>() != null)
            {
                return;
            }

            // Si es grupo NPC_Demo
            if (obj.name == NPC_GROUP_NAME)
            {
                SetLayerRecursively(obj, npcLayer);
                EditorUtility.SetDirty(obj);
                return;
            }

            // Si es un NPC individual o subgrupo
            bool isNPC = obj.name.StartsWith("NPC_") ||
                         obj.GetComponent<NPCPathFollower>() != null ||
                         obj.GetComponent<NPCPedestrianController>() != null ||
                         obj.GetComponent<NPCVehicleController>() != null;

            if (isNPC)
            {
                SetLayerRecursively(obj, npcLayer);
                EditorUtility.SetDirty(obj);
                return;
            }

            foreach (Transform child in obj.transform)
            {
                ApplyNPCLayerRecursive(child.gameObject, npcLayer);
            }
        }

        // Configuración de VR y Layer Culling
        // =========================================================================

        private static void ConfigureMainCameraVR(Scene scene, int routeLayer, int npcLayer, int vehicleLayer, int roadsLayer, int buildingLayer)
        {
            // Configurar culling mask en cámaras principales:
            // Mantener visible todo el entorno real (Roads, Building, Vehicle, NPC, Default) y excluir GPSRoute
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam.gameObject.name == "GPS_TopDownCamera") continue;
                    if (routeLayer != -1) cam.cullingMask &= ~(1 << routeLayer);
                    if (npcLayer != -1) cam.cullingMask |= (1 << npcLayer);
                    if (vehicleLayer != -1) cam.cullingMask |= (1 << vehicleLayer);
                    if (roadsLayer != -1) cam.cullingMask |= (1 << roadsLayer);
                    if (buildingLayer != -1) cam.cullingMask |= (1 << buildingLayer);
                    EditorUtility.SetDirty(cam);
                }
            }
        }

        // =========================================================================
        // Recursos Gráficos
        // =========================================================================

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/TestingScenes"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                {
                    AssetDatabase.CreateFolder("Assets", "Scenes");
                }
                AssetDatabase.CreateFolder("Assets/Scenes", "TestingScenes");
            }

            if (!AssetDatabase.IsValidFolder(GPS_TEXTURES_DIR))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Textures"))
                {
                    AssetDatabase.CreateFolder("Assets", "Textures");
                }
                if (!AssetDatabase.IsValidFolder("Assets/Textures/UI"))
                {
                    AssetDatabase.CreateFolder("Assets/Textures", "UI");
                }
                AssetDatabase.CreateFolder("Assets/Textures/UI", "GPS");
            }
        }

        private static void EnsureClonedSceneExists(string sourcePath, string targetPath)
        {
            var targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(targetPath);
            if (targetScene == null)
            {
                var sourceScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(sourcePath);
                if (sourceScene == null)
                {
                    Debug.LogError($"[GPSNavigationSceneSetup] ❌ No se encontró la escena fuente en {sourcePath}");
                    return;
                }

                bool copied = AssetDatabase.CopyAsset(sourcePath, targetPath);
                if (copied)
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[GPSNavigationSceneSetup] 📋 Escena clonada exitosamente en {targetPath}");
                }
                else
                {
                    Debug.LogError($"[GPSNavigationSceneSetup] ❌ Error al clonar {sourcePath} a {targetPath}");
                }
            }
        }

        private static RenderTexture GetOrCreateRenderTexture(string path, int width, int height)
        {
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt == null)
            {
                rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                AssetDatabase.CreateAsset(rt, path);
                AssetDatabase.SaveAssets();
            }
            return rt;
        }

        private static Material GetOrCreateRouteMaterial(string path)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Color");

                mat = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    color = new Color(0.12f, 0.53f, 0.98f, 1f)
                };
                AssetDatabase.CreateAsset(mat, path);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static Sprite GetOrCreateArrowSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            const int S = 128;
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            Color transparent = new Color(0, 0, 0, 0);
            Color cyanColor = new Color(0f, 0.85f, 1f, 1f);
            Color shadowColor = new Color(0f, 0f, 0f, 0.35f);

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S;
                    float v = (y + 0.5f) / S;

                    bool inLeftWing = PointInTriangle(u, v, 0.5f, 0.92f, 0.16f, 0.12f, 0.5f, 0.34f);
                    bool inRightWing = PointInTriangle(u, v, 0.5f, 0.92f, 0.5f, 0.34f, 0.84f, 0.12f);

                    bool inShadowLeft = PointInTriangle(u, v + 0.04f, 0.5f, 0.92f, 0.16f, 0.12f, 0.5f, 0.34f);
                    bool inShadowRight = PointInTriangle(u, v + 0.04f, 0.5f, 0.92f, 0.5f, 0.34f, 0.84f, 0.12f);

                    if (inLeftWing || inRightWing)
                    {
                        Color c = inLeftWing ? cyanColor : new Color(0f, 0.72f, 0.92f, 1f);
                        tex.SetPixel(x, y, c);
                    }
                    else if (inShadowLeft || inShadowRight)
                    {
                        tex.SetPixel(x, y, shadowColor);
                    }
                    else
                    {
                        tex.SetPixel(x, y, transparent);
                    }
                }
            }

            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool PointInTriangle(float px, float py, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            float d1 = Sign(px, py, x1, y1, x2, y2);
            float d2 = Sign(px, py, x2, y2, x3, y3);
            float d3 = Sign(px, py, x3, y3, x1, y1);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        private static float Sign(float p1x, float p1y, float p2x, float p2y, float p3x, float p3y)
        {
            return (p1x - p3x) * (p2y - p3y) - (p2x - p3x) * (p1y - p3y);
        }

        // =========================================================================
        // Utilitarios
        // =========================================================================

        public static int EnsureLayer(string layerName)
        {
            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0) return -1;

            SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null) return -1;

            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty layerProp = layers.GetArrayElementAtIndex(i);
                if (layerProp.stringValue == layerName)
                {
                    return i;
                }
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layerProp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layerProp.stringValue))
                {
                    layerProp.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[GPSNavigationSceneSetup] Layer '{layerName}' asignado en el índice {i}.");
                    return i;
                }
            }

            return -1;
        }

        public static void SetLayerRecursively(GameObject obj, int newLayer, string[] excludeChildrenWithName = null)
        {
            if (excludeChildrenWithName != null)
            {
                foreach (string excluded in excludeChildrenWithName)
                {
                    if (obj.name == excluded) return;
                }
            }

            obj.layer = newLayer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            EditorUtility.SetDirty(obj);
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, newLayer, excludeChildrenWithName);
            }
        }

                public static void PurgeDesktopTesterAndPOVCamera(Scene scene, GameObject carObj)
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = roots.Length - 1; i >= 0; i--)
                {
                    GameObject root = roots[i];
                    if (root == null) continue;
                    if (root.name == "[Desktop_Cockpit_Tester]" || root.GetComponent<PrimerVolante.Testing.CockpitDesktopTester>() != null)
                    {
                        root.SetActive(false);
                        Object.DestroyImmediate(root);
                    }
                }

                foreach (Camera cam in Object.FindObjectsByType<Camera>())
                {
                    if (cam != null && cam.name == "Desktop_POV_Camera")
                    {
                        cam.gameObject.SetActive(false);
                        Object.DestroyImmediate(cam.gameObject);
                    }
                }
            }

            if (carObj != null)
            {
                foreach (Camera cam in carObj.GetComponentsInChildren<Camera>(true))
                {
                    if (cam != null && cam.name == "Desktop_POV_Camera")
                    {
                        cam.gameObject.SetActive(false);
                        Object.DestroyImmediate(cam.gameObject);
                    }
                }
            }
        }

        private static GameObject FindCar09InScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.Contains("Car09") || root.name.StartsWith("Car"))
                {
                    return root;
                }

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Car09") return t.gameObject;
                }
            }

            var vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            if (vehicle != null) return vehicle.gameObject;

            return null;
        }
    }
}
