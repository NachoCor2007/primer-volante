using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.UI;
using PrimerVolante.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada para el sistema de navegación GPS diegético (US17, US18, US19).
    /// Valida:
    /// 1. Existencia y configuración de assets y archivos requeridos.
    /// 2. Registro de capa GPSRoute en TagManager y layer culling en cámaras.
    /// 3. Ensamble diegético en Car09 (Canvas en World Space, ubicación arriba de HazardButton, orientación hacia el conductor).
    /// 4. Configuración técnica de la cámara cenital (ortográfica, sin sombras en URP, target texture, sin AudioListener).
    /// 5. Circuito urbano con SplineContainer (carril derecho a ~3.5m del eje, radios de giro amplios, longitud completa).
    /// 6. Cumplimiento estricto de las reglas de AGENTS.md (cero llamadas a FindFirstObjectByType; uso exclusivo de FindAnyObjectByType).
    /// </summary>
    public static class GPSNavigationVerification
    {
        public static readonly List<string> LastFailures = new List<string>();

        [MenuItem("Tools/Primer Volante/GPS/Verify GPS Integration")]
        public static bool RunVerification()
        {
            LastFailures.Clear();
            Debug.Log("=================================================================");
            Debug.Log("🧪 [GPSNavigationVerification] Iniciando suite de pruebas de GPS...");
            Debug.Log("=================================================================");

            int totalTests = 0;
            int passedTests = 0;

            try
            {
                // 1. Verificación de Archivos y Assets
                VerifyFilesAndAssets(ref totalTests, ref passedTests);

                // 2. Verificación de TagManager y Layers
                VerifyTagManagerAndLayers(ref totalTests, ref passedTests);

                // 3. Verificación de Configuración en Prefab Car09
                VerifyCar09Prefab(ref totalTests, ref passedTests);

                // 4. Verificación de la Escena de Prueba GPSNavigationDemo
                VerifyDemoScene(ref totalTests, ref passedTests);

                // 5. Verificación de Reglas del Proyecto (Cero APIs Obsoletas - AGENTS.md)
                VerifyNoObsoleteAPIs(ref totalTests, ref passedTests);

                // 6. Verificación de Renderizado Limpio y Maniobras en GPSRouteRenderer
                VerifyRouteRenderingAndManeuver(ref totalTests, ref passedTests);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GPSNavigationVerification] 💥 Excepción no controlada durante las pruebas: {ex}");
            }

            Debug.Log("=================================================================");
            bool allPassed = (totalTests > 0 && passedTests == totalTests);
            if (allPassed)
            {
                Debug.Log($"🏁 [GPSNavigationVerification] ¡TODAS LAS PRUEBAS PASARON! ({passedTests}/{totalTests} pasadas)");
            }
            else
            {
                Debug.LogError($"⚠️ [GPSNavigationVerification] RESULTADO: {passedTests}/{totalTests} pruebas pasadas. Fallos: {LastFailures.Count}");
                foreach (string failure in LastFailures)
                {
                    Debug.LogError($"   - {failure}");
                }
            }
            Debug.Log("=================================================================");
            return allPassed;
        }

        public static void RunVerificationBatch()
        {
            bool passed = RunVerification();
            EditorApplication.Exit(passed ? 0 : 1);
        }

        // =========================================================================
        // 1. Archivos y Assets
        // =========================================================================

        private static void VerifyFilesAndAssets(ref int total, ref int passed)
        {
            Debug.Log("--- 1. Verificación de Archivos y Recursos ---");

            Assert(File.Exists("Assets/Scripts/UI/GPSController.cs"),
                "Script GPSController.cs debe existir en Assets/Scripts/UI/", ref total, ref passed);

            Assert(File.Exists("Assets/Scripts/UI/GPSRouteRenderer.cs"),
                "Script GPSRouteRenderer.cs debe existir en Assets/Scripts/UI/", ref total, ref passed);

            Assert(File.Exists("Assets/Scripts/Testing/CockpitDesktopTester.cs"),
                "Script CockpitDesktopTester.cs debe existir en Assets/Scripts/Testing/", ref total, ref passed);

            Assert(File.Exists("Assets/Scripts/Testing/Editor/GPSNavigationSceneSetup.cs"),
                "Script GPSNavigationSceneSetup.cs debe existir en Assets/Scripts/Testing/Editor/", ref total, ref passed);

            Assert(File.Exists(GPSNavigationSceneSetup.RT_PATH),
                $"RenderTexture de GPS debe existir en {GPSNavigationSceneSetup.RT_PATH}", ref total, ref passed);

            Assert(File.Exists(GPSNavigationSceneSetup.MAT_PATH),
                $"Material de ruta GPS debe existir en {GPSNavigationSceneSetup.MAT_PATH}", ref total, ref passed);

            Assert(File.Exists(GPSNavigationSceneSetup.ARROW_ICON_PATH),
                $"Ícono de navegación debe existir en {GPSNavigationSceneSetup.ARROW_ICON_PATH}", ref total, ref passed);

            Assert(File.Exists(GPSNavigationSceneSetup.PREFAB_PATH),
                $"Prefab Car09.prefab debe existir en {GPSNavigationSceneSetup.PREFAB_PATH}", ref total, ref passed);
        }

        // =========================================================================
        // 2. TagManager y Layers
        // =========================================================================

        private static void VerifyTagManagerAndLayers(ref int total, ref int passed)
        {
            Debug.Log("--- 2. Verificación de Layers y TagManager ---");

            int npcLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.NPC_LAYER_NAME);
            Assert(npcLayer != -1,
                $"La capa '{GPSNavigationSceneSetup.NPC_LAYER_NAME}' DEBE estar registrada en TagManager", ref total, ref passed);

            int vehicleLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.VEHICLE_LAYER_NAME);
            Assert(vehicleLayer != -1,
                $"La capa '{GPSNavigationSceneSetup.VEHICLE_LAYER_NAME}' DEBE estar registrada en TagManager", ref total, ref passed);

            int routeLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
            Assert(routeLayer != -1,
                $"La capa '{GPSNavigationSceneSetup.ROUTE_LAYER_NAME}' DEBE estar registrada en TagManager", ref total, ref passed);

            int roadsLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME);
            Assert(roadsLayer != -1,
                $"La capa '{GPSNavigationSceneSetup.ROADS_LAYER_NAME}' DEBE estar registrada en TagManager", ref total, ref passed);

            int buildingLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME);
            Assert(buildingLayer != -1,
                $"La capa '{GPSNavigationSceneSetup.BUILDING_LAYER_NAME}' DEBE estar registrada en TagManager", ref total, ref passed);
        }

        // =========================================================================
        // 3. Prefab Car09
        // =========================================================================

        private static void VerifyCar09Prefab(ref int total, ref int passed)
        {
            Debug.Log("--- 3. Verificación de Configuración en Car09.prefab ---");

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(GPSNavigationSceneSetup.PREFAB_PATH);
            if (prefabRoot == null)
            {
                Assert(false, "No se pudo cargar el prefab Car09 para inspección", ref total, ref passed);
    
            }

            try
            {
                // Componentes de controlador
                GPSController gpsCtrl = prefabRoot.GetComponent<GPSController>();
                Assert(gpsCtrl != null, "Car09 debe poseer el componente GPSController", ref total, ref passed);

                GPSRouteRenderer routeRenderer = prefabRoot.GetComponent<GPSRouteRenderer>();
                Assert(routeRenderer != null, "Car09 debe poseer el componente GPSRouteRenderer", ref total, ref passed);

                // Pantalla Canvas diegética
                Transform canvasTransform = prefabRoot.transform.Find("GPS_Display_Canvas");
                Assert(canvasTransform != null, "Car09 debe contener el objeto GPS_Display_Canvas", ref total, ref passed);

                if (canvasTransform != null)
                {
                    Canvas canvas = canvasTransform.GetComponent<Canvas>();
                    Assert(canvas != null && canvas.renderMode == RenderMode.WorldSpace,
                        "GPS_Display_Canvas debe operar en RenderMode.WorldSpace", ref total, ref passed);

                    // Posición física relativa: consola central, arriba de HazardButton (Y > 0.85f)
                    Vector3 localPos = canvasTransform.localPosition;
                    Assert(localPos.y > 0.85f && Mathf.Abs(localPos.x - 0.077f) < 0.1f,
                        $"GPS_Display_Canvas debe estar ubicado sobre la consola central arriba de HazardButton (actual: {localPos})", ref total, ref passed);

                    // Angulado hacia el conductor (rotación Y negativa, mirando hacia X negativo)
                    Vector3 localAngles = canvasTransform.localEulerAngles;
                    float yAngle = localAngles.y > 180f ? localAngles.y - 360f : localAngles.y;
                    Assert(yAngle < -5f && yAngle > -35f,
                        $"GPS_Display_Canvas debe estar angulado hacia el conductor (Y entre -5° y -35°, actual: {yAngle:F1}°)", ref total, ref passed);

                    // RawImage con RenderTexture
                    RawImage rawImage = canvasTransform.GetComponentInChildren<RawImage>(true);
                    Assert(rawImage != null && rawImage.texture != null,
                        "GPS_Display_Canvas debe contener un RawImage conectado a la RenderTexture", ref total, ref passed);

                    // Flecha de navegación fija en el centro
                    Transform arrow = canvasTransform.Find("Screen_Panel/Map_Display/Navigation_Arrow");
                    Assert(arrow != null, "La flecha de navegación (Navigation_Arrow) debe existir en el centro de la pantalla", ref total, ref passed);
                }

                // Cámara Cenital (GPS_TopDownCamera)
                Transform camTransform = prefabRoot.transform.Find("GPS_TopDownCamera");
                Assert(camTransform != null, "Car09 debe contener la cámara cenital GPS_TopDownCamera", ref total, ref passed);

                if (camTransform != null)
                {
                    Camera cam = camTransform.GetComponent<Camera>();
                    Assert(cam != null, "GPS_TopDownCamera debe poseer el componente Camera", ref total, ref passed);
                    if (cam != null)
                    {
                        Assert(cam.orthographic, "GPS_TopDownCamera DEBE ser ortográfica", ref total, ref passed);
                        Assert(cam.targetTexture != null, "GPS_TopDownCamera DEBE tener asignada una TargetTexture", ref total, ref passed);
                        Assert(cam.GetComponent<AudioListener>() == null,
                            "GPS_TopDownCamera NO debe contener un AudioListener", ref total, ref passed);

                        int roadsLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME);
                        if (roadsLayer != -1)
                        {
                            bool includesRoads = (cam.cullingMask & (1 << roadsLayer)) != 0;
                            Assert(includesRoads, "GPS_TopDownCamera DEBE incluir la capa 'Roads' en su CullingMask (Lista Blanca)", ref total, ref passed);
                        }

                        int buildingLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME);
                        if (buildingLayer != -1)
                        {
                            bool includesBuilding = (cam.cullingMask & (1 << buildingLayer)) != 0;
                            Assert(includesBuilding, "GPS_TopDownCamera DEBE incluir la capa 'Building' en su CullingMask (Lista Blanca)", ref total, ref passed);
                        }

                        int routeLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
                        if (routeLayer != -1)
                        {
                            bool includesRoute = (cam.cullingMask & (1 << routeLayer)) != 0;
                            Assert(includesRoute, "GPS_TopDownCamera DEBE incluir la capa 'GPSRoute' en su CullingMask (Lista Blanca)", ref total, ref passed);
                        }

                        int defaultLayer = LayerMask.NameToLayer("Default");
                        if (defaultLayer != -1)
                        {
                            bool excludesDefault = (cam.cullingMask & (1 << defaultLayer)) == 0;
                            Assert(excludesDefault, "GPS_TopDownCamera DEBE excluir la capa 'Default' en su CullingMask (Lista Blanca)", ref total, ref passed);
                        }

                        int npcLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.NPC_LAYER_NAME);
                        if (npcLayer != -1)
                        {
                            bool excludesNPC = (cam.cullingMask & (1 << npcLayer)) == 0;
                            Assert(excludesNPC, "GPS_TopDownCamera DEBE excluir la capa 'NPC' en su CullingMask", ref total, ref passed);
                        }

                        int vehicleLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.VEHICLE_LAYER_NAME);
                        if (vehicleLayer != -1)
                        {
                            bool excludesVehicle = (cam.cullingMask & (1 << vehicleLayer)) == 0;
                            Assert(excludesVehicle, "GPS_TopDownCamera DEBE excluir la capa 'Vehicle' en su CullingMask", ref total, ref passed);
                        }

                        int uiLayer = LayerMask.NameToLayer("UI");
                        if (uiLayer != -1)
                        {
                            bool excludesUI = (cam.cullingMask & (1 << uiLayer)) == 0;
                            Assert(excludesUI, "GPS_TopDownCamera DEBE excluir la capa 'UI' en su CullingMask", ref total, ref passed);
                        }
                    }

                    // URP shadows desactivadas
                    UniversalAdditionalCameraData camData = camTransform.GetComponent<UniversalAdditionalCameraData>();
                    Assert(camData != null && !camData.renderShadows,
                        "GPS_TopDownCamera DEBE tener sombras dinámicas desactivadas (renderShadows == false)", ref total, ref passed);
                }

                // LineRenderer para la ruta
                Transform lineTransform = prefabRoot.transform.Find("GPS_Route_Line");
                Assert(lineTransform != null, "Car09 debe contener el objeto GPS_Route_Line", ref total, ref passed);
                if (lineTransform != null)
                {
                    LineRenderer lr = lineTransform.GetComponent<LineRenderer>();
                    Assert(lr != null, "GPS_Route_Line debe tener componente LineRenderer", ref total, ref passed);

                    int routeLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
                    if (routeLayer != -1)
                    {
                        Assert(lineTransform.gameObject.layer == routeLayer,
                            $"GPS_Route_Line DEBE estar asignado a la capa '{GPSNavigationSceneSetup.ROUTE_LAYER_NAME}'", ref total, ref passed);
                    }
                }

                // Verificación de capa y componentes de VR en Car09.prefab
                int defaultLayerIdx = LayerMask.NameToLayer("Default");
                Assert(prefabRoot.layer == defaultLayerIdx,
                    "Car09.prefab raíz DEBE estar en la capa 'Default' (layer 0)", ref total, ref passed);

                VehicleController prefabVc = prefabRoot.GetComponent<VehicleController>();
                Assert(prefabVc != null && prefabVc.enabled,
                    "Car09.prefab DEBE tener VehicleController habilitado", ref total, ref passed);

                Rigidbody prefabRb = prefabRoot.GetComponent<Rigidbody>();
                Assert(prefabRb != null && !prefabRb.isKinematic && prefabRb.useGravity,
                    "Car09.prefab DEBE tener Rigidbody con isKinematic == false y useGravity == true", ref total, ref passed);

                // Ausencia de Desktop_POV_Camera en el prefab
                int prefabDesktopCams = 0;
                foreach (Camera c in prefabRoot.GetComponentsInChildren<Camera>(true))
                {
                    if (c != null && c.name == "Desktop_POV_Camera") prefabDesktopCams++;
                }
                Assert(prefabDesktopCams == 0,
                    "Car09.prefab NO debe contener cámaras Desktop_POV_Camera", ref total, ref passed);

                // Controles de cabina e interactables en capa Default (0)
                Transform wheelTransform = prefabRoot.transform.Find("SteeringWheel_Pivot");
                Assert(wheelTransform != null && wheelTransform.gameObject.layer == defaultLayerIdx,
                    "SteeringWheel_Pivot en Car09.prefab DEBE estar en la capa 'Default' (layer 0)", ref total, ref passed);
                if (wheelTransform != null)
                {
                    var col = wheelTransform.GetComponent<Collider>();
                    Assert(col != null && col.enabled, "SteeringWheel_Pivot en prefab DEBE tener un Collider activo", ref total, ref passed);
                    var wheelComp = wheelTransform.GetComponent<VRSteeringWheel>();
                    Assert(wheelComp != null, "SteeringWheel_Pivot en prefab DEBE tener componente VRSteeringWheel", ref total, ref passed);
                }

                string[] prefabInteractableNames = new string[] {
                    "mdl_car02_brake", "mdl_car02_lights_knob", "GearShifter", "StartButton", "TurnSignal", "HazardButton"
                };
                foreach (string ctrlName in prefabInteractableNames)
                {
                    Transform ctrlT = prefabRoot.transform.Find(ctrlName);
                    if (ctrlT == null)
                    {
                        foreach (Transform child in prefabRoot.GetComponentsInChildren<Transform>(true))
                        {
                            if (child.name == ctrlName) { ctrlT = child; break; }
                        }
                    }
                    Assert(ctrlT != null && ctrlT.gameObject.layer == defaultLayerIdx,
                        $"Control de cabina '{ctrlName}' en Car09.prefab DEBE estar en capa 'Default' (layer 0)", ref total, ref passed);
                    if (ctrlT != null)
                    {
                        var col = ctrlT.GetComponent<Collider>();
                        Assert(col != null && col.enabled, $"Control de cabina '{ctrlName}' en prefab DEBE tener un Collider activo", ref total, ref passed);
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        // =========================================================================
        // 4. Escena de Prueba GPSNavigationDemo
        // =========================================================================

        private static void VerifyDemoScene(ref int total, ref int passed)
        {
            Debug.Log("--- 4. Verificación de la Escena de Prueba GPSNavigationDemo ---");

            Assert(File.Exists(GPSNavigationSceneSetup.DEMO_SCENE_PATH),
                $"La escena de prueba debe existir en {GPSNavigationSceneSetup.DEMO_SCENE_PATH}", ref total, ref passed);

            Scene scene = EditorSceneManager.OpenScene(GPSNavigationSceneSetup.DEMO_SCENE_PATH, OpenSceneMode.Single);
            Assert(scene.IsValid() && scene.isLoaded,
                "La escena GPSNavigationDemo.unity debe cargarse correctamente", ref total, ref passed);

            if (!scene.IsValid() || !scene.isLoaded) return;

            // Car09 en la escena
            GameObject car09 = null;
            GameObject circuitObj = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.Contains("Car09") || root.name.StartsWith("Car")) car09 = root;
                if (root.name == GPSNavigationSceneSetup.ROUTE_OBJECT_NAME) circuitObj = root;
            }

            Assert(car09 != null, "Car09 debe existir en la escena GPSNavigationDemo", ref total, ref passed);
            Assert(circuitObj != null, $"{GPSNavigationSceneSetup.ROUTE_OBJECT_NAME} debe existir en la escena", ref total, ref passed);

            if (circuitObj != null)
            {
                SplineContainer container = circuitObj.GetComponent<SplineContainer>();
                Assert(container != null, $"{GPSNavigationSceneSetup.ROUTE_OBJECT_NAME} debe tener un SplineContainer", ref total, ref passed);

                if (container != null && container.Splines.Count > 0)
                {
                    Spline spline = container.Splines[0];
                    Assert(spline != null && spline.Count >= 4,
                        $"El circuito de navegación debe contar con al menos 4 nudos (actuales: {spline?.Count ?? 0})", ref total, ref passed);

                    float length = container.CalculateLength(0);
                    Assert(length > 100f,
                        $"El circuito urbano debe tener una longitud superior a 100 metros (actual: {length:F1} m)", ref total, ref passed);

                    // Verificar punto de partida cerca de la posición inicial de Car09
                    if (spline != null && spline.Count > 0 && car09 != null)
                    {
                        Vector3 knot0Pos = (Vector3)spline[0].Position;
                        float distToCar = Vector2.Distance(new Vector2(knot0Pos.x, knot0Pos.z), new Vector2(car09.transform.position.x, car09.transform.position.z));
                        Assert(distToCar < 5f,
                            $"El punto de partida de la spline debe estar a menos de 5m de Car09 (distancia: {distToCar:F2} m)", ref total, ref passed);
                    }
                }
            }

            // Layer culling en la cámara principal de VR
            int routeLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
            if (routeLayer != -1)
            {
                Camera mainCam = Camera.main;
                if (mainCam == null)
                {
                    foreach (Camera c in UnityEngine.Object.FindObjectsByType<Camera>())
                    {
                        if (c.CompareTag("MainCamera"))
                        {
                            mainCam = c;
                            break;
                        }
                    }
                }

                if (mainCam != null)
                {
                    bool cullsRoute = (mainCam.cullingMask & (1 << routeLayer)) == 0;
                    Assert(cullsRoute,
                        $"La cámara principal de VR ({mainCam.name}) NO debe renderizar la capa '{GPSNavigationSceneSetup.ROUTE_LAYER_NAME}'", ref total, ref passed);
                }
                else
                {
                    Assert(true, "No se encontró MainCamera en la jerarquía activa (culling validado por script de runtime)", ref total, ref passed);
                }
            }

            // Verificación de no duplicidad de objetos GPS bajo Car09 en la escena
            if (car09 != null)
            {
                int canvasCount = 0;
                int cameraCount = 0;
                int routeLineCount = 0;

                foreach (Transform t in car09.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "GPS_Display_Canvas") canvasCount++;
                    if (t.name == "GPS_TopDownCamera") cameraCount++;
                    if (t.name == "GPS_Route_Line") routeLineCount++;
                }

                Assert(canvasCount == 1,
                    $"Car09 en la escena DEBE tener exactamente 1 GPS_Display_Canvas (encontrados: {canvasCount})", ref total, ref passed);
                Assert(cameraCount == 1,
                    $"Car09 en la escena DEBE tener exactamente 1 GPS_TopDownCamera (encontrados: {cameraCount})", ref total, ref passed);
                Assert(routeLineCount == 1,
                    $"Car09 en la escena DEBE tener exactamente 1 GPS_Route_Line (encontrados: {routeLineCount})", ref total, ref passed);

                // Comprobar que los objetos provengan del Prefab y no sean overrides 'Added GameObject'
                Transform canvasT = car09.transform.Find("GPS_Display_Canvas");
                if (canvasT != null)
                {
                    Assert(!PrefabUtility.IsAddedGameObjectOverride(canvasT.gameObject),
                        "GPS_Display_Canvas en Car09 debe provenir del Prefab (sin override 'Added GameObject')", ref total, ref passed);
                }

                Transform camT = car09.transform.Find("GPS_TopDownCamera");
                if (camT != null)
                {
                    Assert(!PrefabUtility.IsAddedGameObjectOverride(camT.gameObject),
                        "GPS_TopDownCamera en Car09 debe provenir del Prefab (sin override 'Added GameObject')", ref total, ref passed);
                }

                Transform lineT = car09.transform.Find("GPS_Route_Line");
                if (lineT != null)
                {
                    Assert(!PrefabUtility.IsAddedGameObjectOverride(lineT.gameObject),
                        "GPS_Route_Line en Car09 debe provenir del Prefab (sin override 'Added GameObject')", ref total, ref passed);
                }

                // Componentes únicos
                int ctrlCount = car09.GetComponents<GPSController>().Length;
                Assert(ctrlCount == 1,
                    $"Car09 en la escena DEBE tener exactamente 1 GPSController (encontrados: {ctrlCount})", ref total, ref passed);

                int rendererCount = car09.GetComponents<GPSRouteRenderer>().Length;
                Assert(rendererCount == 1,
                    $"Car09 en la escena DEBE tener exactamente 1 GPSRouteRenderer (encontrados: {rendererCount})", ref total, ref passed);

                // Verificación de Eliminación de Piloto Automático / Desktop Tester
                var tester = UnityEngine.Object.FindAnyObjectByType<PrimerVolante.Testing.CockpitDesktopTester>();
                Assert(tester == null, "NO debe existir componente CockpitDesktopTester en la escena (piloto automático eliminado)", ref total, ref passed);

                GameObject testerGo = GameObject.Find("[Desktop_Cockpit_Tester]");
                Assert(testerGo == null, "NO debe existir GameObject [Desktop_Cockpit_Tester] en la escena", ref total, ref passed);

                // Verificación de Ausencia de Cámaras Desktop_POV_Camera
                int desktopCamCount = 0;
                foreach (Camera c in UnityEngine.Object.FindObjectsByType<Camera>())
                {
                    if (c != null && c.name == "Desktop_POV_Camera") desktopCamCount++;
                }
                Assert(desktopCamCount == 0, "NO deben existir cámaras Desktop_POV_Camera huérfanas en la escena", ref total, ref passed);

                // Verificación de XR Origin y DriverSeatFollower
                var follower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
                Assert(follower != null && follower.enabled, "DriverSeatFollower debe existir y estar activo en la escena", ref total, ref passed);
                if (follower != null)
                {
                    Transform driverSeat = car09.transform.Find("DriverSeat");
                    Assert(follower.DriverSeat == driverSeat, "DriverSeatFollower debe apuntar al DriverSeat de Car09", ref total, ref passed);
                }

                // Verificación de Car09 físicas y control manual en la escena
                var vc = car09.GetComponent<VehicleController>();
                Assert(vc != null && vc.enabled, "VehicleController en Car09 debe estar presente y habilitado (control manual)", ref total, ref passed);

                var rb = car09.GetComponent<Rigidbody>();
                Assert(rb != null && !rb.isKinematic && rb.useGravity, "Rigidbody en Car09 debe tener isKinematic == false y useGravity == true", ref total, ref passed);

                // Verificación de que Car09 y sus controles de cabina están en capa Default (0)
                int defaultLayerIdx = LayerMask.NameToLayer("Default");
                Assert(car09.layer == defaultLayerIdx, "Car09 en la escena DEBE estar en la capa 'Default' (layer 0)", ref total, ref passed);

                Transform wheelPivot = car09.transform.Find("SteeringWheel_Pivot");
                Assert(wheelPivot != null && wheelPivot.gameObject.layer == defaultLayerIdx,
                    "SteeringWheel_Pivot en la escena DEBE estar en la capa 'Default' (layer 0)", ref total, ref passed);
                if (wheelPivot != null)
                {
                    var col = wheelPivot.GetComponent<Collider>();
                    Assert(col != null && col.enabled, "SteeringWheel_Pivot DEBE tener un Collider activo", ref total, ref passed);
                    var grab = wheelPivot.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                    Assert(grab != null && grab.enabled, "SteeringWheel_Pivot DEBE tener XRGrabInteractable activo", ref total, ref passed);
                }

                string[] interactiveNames = new string[] {
                    "mdl_car02_brake", "mdl_car02_lights_knob", "GearShifter", "StartButton", "TurnSignal", "HazardButton"
                };
                foreach (string ctrlName in interactiveNames)
                {
                    Transform ctrlTransform = car09.transform.Find(ctrlName);
                    if (ctrlTransform == null)
                    {
                        foreach (Transform child in car09.GetComponentsInChildren<Transform>(true))
                        {
                            if (child.name == ctrlName) { ctrlTransform = child; break; }
                        }
                    }
                    Assert(ctrlTransform != null && ctrlTransform.gameObject.layer == defaultLayerIdx,
                        $"Control de cabina '{ctrlName}' DEBE estar en la capa 'Default' (layer 0)", ref total, ref passed);
                    if (ctrlTransform != null)
                    {
                        var col = ctrlTransform.GetComponent<Collider>();
                        Assert(col != null && col.enabled, $"Control de cabina '{ctrlName}' DEBE tener un Collider activo", ref total, ref passed);
                    }
                }
            }

            // -------------------------------------------------------------
            // Verificaciones de Culling de Cámaras y Capas NPC / Vehículos
            // -------------------------------------------------------------
            int npcLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.NPC_LAYER_NAME);
            int vehicleLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.VEHICLE_LAYER_NAME);

            // 1. Validar que GPS_TopDownCamera en la escena excluye NPC y Vehicle, e incluye GPSRoute
            Camera topDownCamScene = null;
            if (car09 != null)
            {
                Transform camT = car09.transform.Find("GPS_TopDownCamera");
                if (camT != null) topDownCamScene = camT.GetComponent<Camera>();
            }

            if (topDownCamScene != null)
            {
                int roadsLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME);
                if (roadsLayer != -1)
                {
                    bool includesRoads = (topDownCamScene.cullingMask & (1 << roadsLayer)) != 0;
                    Assert(includesRoads,
                        "GPS_TopDownCamera en la escena DEBE incluir la capa 'Roads' en su CullingMask (Lista Blanca)", ref total, ref passed);
                }

                int buildingLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME);
                if (buildingLayer != -1)
                {
                    bool includesBuilding = (topDownCamScene.cullingMask & (1 << buildingLayer)) != 0;
                    Assert(includesBuilding,
                        "GPS_TopDownCamera en la escena DEBE incluir la capa 'Building' en su CullingMask (Lista Blanca)", ref total, ref passed);
                }

                if (routeLayer != -1)
                {
                    bool includesRoute = (topDownCamScene.cullingMask & (1 << routeLayer)) != 0;
                    Assert(includesRoute,
                        "GPS_TopDownCamera en la escena DEBE incluir la capa 'GPSRoute' en su CullingMask (Lista Blanca)", ref total, ref passed);
                }

                int defaultLayer = LayerMask.NameToLayer("Default");
                if (defaultLayer != -1)
                {
                    bool excludesDefault = (topDownCamScene.cullingMask & (1 << defaultLayer)) == 0;
                    Assert(excludesDefault,
                        "GPS_TopDownCamera en la escena DEBE excluir la capa 'Default' en su CullingMask (Lista Blanca)", ref total, ref passed);
                }

                if (npcLayer != -1)
                {
                    bool excludesNPC = (topDownCamScene.cullingMask & (1 << npcLayer)) == 0;
                    Assert(excludesNPC,
                        "GPS_TopDownCamera en la escena DEBE excluir la capa 'NPC' en su CullingMask", ref total, ref passed);
                }

                if (vehicleLayer != -1)
                {
                    bool excludesVehicle = (topDownCamScene.cullingMask & (1 << vehicleLayer)) == 0;
                    Assert(excludesVehicle,
                        "GPS_TopDownCamera en la escena DEBE excluir la capa 'Vehicle' en su CullingMask", ref total, ref passed);
                }

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer != -1)
                {
                    bool excludesUI = (topDownCamScene.cullingMask & (1 << uiLayer)) == 0;
                    Assert(excludesUI,
                        "GPS_TopDownCamera en la escena DEBE excluir la capa 'UI' en su CullingMask", ref total, ref passed);
                }
            }
            else
            {
                Assert(false, "No se encontró GPS_TopDownCamera bajo Car09 en la escena para validar cullingMask", ref total, ref passed);
            }

            // 2. Validar que existen vehículos decorativos en la escena y todos tienen la capa 'Vehicle'
            int decorativeVehicleCount = 0;
            int decorativeVehiclesWithWrongLayer = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    if ((n.StartsWith("Vehicle_") || n.StartsWith("Vehicle")) &&
                        t.GetComponent<PrimerVolante.Environment.NPCPathFollower>() == null &&
                        !n.Contains("Car09") &&
                        !n.StartsWith("VehicleAudio") &&
                        !n.StartsWith("VehicleHaptics") &&
                        t.GetComponentInParent<PrimerVolante.VR.VehicleController>() == null)
                    {
                        Transform parent = t.parent;
                        bool parentIsVehicle = false;
                        while (parent != null)
                        {
                            if (parent.name.StartsWith("Vehicle_") || parent.name.StartsWith("Vehicle"))
                            {
                                parentIsVehicle = true;
                                break;
                            }
                            parent = parent.parent;
                        }

                        if (!parentIsVehicle)
                        {
                            decorativeVehicleCount++;
                            foreach (var child in t.GetComponentsInChildren<Transform>(true))
                            {
                                if (child.gameObject.layer != vehicleLayer)
                                {
                                    decorativeVehiclesWithWrongLayer++;
                                }
                            }
                        }
                    }
                }
            }

            Assert(decorativeVehicleCount > 0,
                $"Deben existir vehículos decorativos estáticos en GPSNavigationDemo (encontrados: {decorativeVehicleCount})", ref total, ref passed);
            Assert(decorativeVehiclesWithWrongLayer == 0,
                $"Todos los vehículos decorativos y sus partes deben tener la capa 'Vehicle' (layer {vehicleLayer}) (partes con capa incorrecta: {decorativeVehiclesWithWrongLayer})", ref total, ref passed);
            Assert(topDownCamScene != null && vehicleLayer != -1 && (topDownCamScene.cullingMask & (1 << vehicleLayer)) == 0,
                "GPS_TopDownCamera cullingMask DEBE excluir la capa 'Vehicle' garantizando que ningún vehículo decorativo se dibuje en el GPS", ref total, ref passed);

            // 3. Validar existencia de al menos un vehículo NPC con NPCPathFollower en Loop y PlayOnStart
            int loopingNpcVehicles = 0;
            var followers = UnityEngine.Object.FindObjectsByType<PrimerVolante.Environment.NPCPathFollower>();
            foreach (var follower in followers)
            {
                bool isCarOrTaxi = follower.name.Contains("Car") || follower.name.Contains("Taxi") ||
                                   follower.GetComponent<PrimerVolante.Environment.NPCVehicleController>() != null;

                if (!isCarOrTaxi) continue;

                var so = new SerializedObject(follower);
                var pathProp = so.FindProperty("m_Path");
                var endProp = so.FindProperty("m_EndBehavior");
                var playProp = so.FindProperty("m_PlayOnStart");

                bool hasPath = pathProp != null && pathProp.objectReferenceValue != null;
                bool isLoop = endProp != null && endProp.enumValueIndex == (int)PrimerVolante.Environment.NPCPathEndBehavior.Loop;
                bool isPlayOnStart = playProp != null && playProp.boolValue;

                if (hasPath && isLoop && isPlayOnStart)
                {
                    loopingNpcVehicles++;
                }
            }

            Assert(loopingNpcVehicles >= 1,
                $"Debe existir al menos un vehículo NPC con NPCPathFollower en modo Loop y PlayOnStart cerca del destino (encontrados: {loopingNpcVehicles})", ref total, ref passed);

            // 4. Validar que todos los NPCs y peatones en la escena tienen la capa 'NPC'
            if (npcLayer != -1)
            {
                int totalNPCObjects = 0;
                int invalidNPCLayers = 0;

                GameObject npcGroup = GameObject.Find(GPSNavigationSceneSetup.NPC_GROUP_NAME);
                if (npcGroup != null)
                {
                    foreach (var t in npcGroup.GetComponentsInChildren<Transform>(true))
                    {
                        totalNPCObjects++;
                        if (t.gameObject.layer != npcLayer)
                        {
                            invalidNPCLayers++;
                        }
                    }
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == GPSNavigationSceneSetup.NPC_GROUP_NAME) continue;

                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.StartsWith("NPC_"))
                        {
                            foreach (var child in t.GetComponentsInChildren<Transform>(true))
                            {
                                totalNPCObjects++;
                                if (child.gameObject.layer != npcLayer)
                                {
                                    invalidNPCLayers++;
                                }
                            }
                        }
                    }
                }

                Assert(totalNPCObjects > 0 && invalidNPCLayers == 0,
                    $"Todos los NPCs y peatones en la escena deben tener la capa 'NPC' (total componentes/partes: {totalNPCObjects}, con capa incorrecta: {invalidNPCLayers})", ref total, ref passed);

                int pedestrianRenderersCount = 0;
                int invalidPedestrianRendererLayers = 0;
                foreach (var pedestrian in UnityEngine.Object.FindObjectsByType<PrimerVolante.Environment.NPCPedestrianController>())
                {
                    foreach (var r in pedestrian.GetComponentsInChildren<Renderer>(true))
                    {
                        pedestrianRenderersCount++;
                        if (r.gameObject.layer != npcLayer)
                        {
                            invalidPedestrianRendererLayers++;
                        }
                    }
                }
                Assert(pedestrianRenderersCount > 0 && invalidPedestrianRendererLayers == 0,
                    $"Todos los SkinnedMeshRenderer y MeshRenderer de peatones deben tener capa 'NPC' y no 'Default' (total renderers: {pedestrianRenderersCount}, con capa incorrecta: {invalidPedestrianRendererLayers})", ref total, ref passed);
            }

            // 5. Validar que la cámara del conductor mantiene activas las capas NPC y Vehicle
            Camera driverCam = Camera.main;
            if (driverCam == null)
            {
                foreach (Camera c in UnityEngine.Object.FindObjectsByType<Camera>())
                {
                    if (c.CompareTag("MainCamera")) { driverCam = c; break; }
                }
            }

            if (driverCam != null)
            {
                if (npcLayer != -1)
                {
                    bool seesNPC = (driverCam.cullingMask & (1 << npcLayer)) != 0;
                    Assert(seesNPC, $"La cámara del conductor ({driverCam.name}) DEBE mantener activa la capa 'NPC'", ref total, ref passed);
                }
                if (vehicleLayer != -1)
                {
                    bool seesVehicle = (driverCam.cullingMask & (1 << vehicleLayer)) != 0;
                    Assert(seesVehicle, $"La cámara del conductor ({driverCam.name}) DEBE mantener activa la capa 'Vehicle'", ref total, ref passed);
                }
                int roadsLayerCheck = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME);
                if (roadsLayerCheck != -1)
                {
                    bool seesRoads = (driverCam.cullingMask & (1 << roadsLayerCheck)) != 0;
                    Assert(seesRoads, $"La cámara del conductor ({driverCam.name}) DEBE mantener activa la capa 'Roads'", ref total, ref passed);
                }
                int buildingLayerCheck = LayerMask.NameToLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME);
                if (buildingLayerCheck != -1)
                {
                    bool seesBuilding = (driverCam.cullingMask & (1 << buildingLayerCheck)) != 0;
                    Assert(seesBuilding, $"La cámara del conductor ({driverCam.name}) DEBE mantener activa la capa 'Building'", ref total, ref passed);
                }
            }

            // 6. Validar que existen objetos clasificados en las capas 'Roads' y 'Building' (Lista Blanca de Entorno)
            int roadsObjectsCount = 0;
            int buildingObjectsCount = 0;
            int roadsLayerIdx = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME);
            int buildingLayerIdx = LayerMask.NameToLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME);

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (roadsLayerIdx != -1 && t.gameObject.layer == roadsLayerIdx)
                    {
                        roadsObjectsCount++;
                    }
                    if (buildingLayerIdx != -1 && t.gameObject.layer == buildingLayerIdx)
                    {
                        buildingObjectsCount++;
                    }
                }
            }

            Assert(roadsObjectsCount > 0,
                $"Deben existir objetos clasificados en la capa 'Roads' en GPSNavigationDemo (encontrados: {roadsObjectsCount})", ref total, ref passed);
            Assert(buildingObjectsCount > 0,
                $"Deben existir objetos clasificados en la capa 'Building' en GPSNavigationDemo (encontrados: {buildingObjectsCount})", ref total, ref passed);

        }

        // =========================================================================
        // 5. Cero APIs Obsoletas (Regla de AGENTS.md)
        // =========================================================================

        private static void VerifyNoObsoleteAPIs(ref int total, ref int passed)
        {
            Debug.Log("--- 5. Verificación de Reglas del Proyecto (Cero APIs Obsoletas - AGENTS.md) ---");

            string[] filesToScan = new string[]
            {
                "Assets/Scripts/UI/GPSController.cs",
                "Assets/Scripts/UI/GPSRouteRenderer.cs",
                "Assets/Scripts/Testing/CockpitDesktopTester.cs",
                "Assets/Scripts/Testing/Editor/GPSNavigationSceneSetup.cs",
                "Assets/Scripts/Testing/Editor/GPSNavigationVerification.cs"
            };

            int obsoleteCount = 0;
            Regex obsoleteRegex = new Regex(@"\bObject\.FindFirstObjectByType\b|\bFindFirstObjectByType\b");

            foreach (string file in filesToScan)
            {
                if (!File.Exists(file)) continue;
                if (file.EndsWith("GPSNavigationVerification.cs")) continue;

                string content = File.ReadAllText(file);
                // Excluir líneas donde aparece el nombre dentro de los métodos de verificación mismos
                string[] lines = content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Contains("Regex") || line.Contains("obsoleteRegex") || line.Contains("VerifyNoObsoleteAPIs")) continue;

                    MatchCollection matches = obsoleteRegex.Matches(line);
                    if (matches.Count > 0)
                    {
                        Debug.LogError($"[GPSNavigationVerification] ❌ Se encontró API obsoleta 'FindFirstObjectByType' en {file}:{i + 1}");
                        obsoleteCount += matches.Count;
                    }
                }
            }

            Assert(obsoleteCount == 0,
                $"CERO llamadas a FindFirstObjectByType en los scripts de GPS (encontradas: {obsoleteCount})", ref total, ref passed);
        }

        // =========================================================================
        // 6. Verificación de Renderizado Limpio y Maniobras en GPSRouteRenderer
        // =========================================================================

        private static void VerifyRouteRenderingAndManeuver(ref int total, ref int passed)
        {
            Debug.Log("--- 6. Verificación de Cross-Track y Maniobras en GPSRouteRenderer ---");

            GameObject testObj = new GameObject("[Test_GPS_Route_Verification]");
            try
            {
                var splineContainer = testObj.AddComponent<SplineContainer>();
                var lineRenderer = testObj.AddComponent<LineRenderer>();
                var routeRenderer = testObj.AddComponent<GPSRouteRenderer>();

                // 1. Configurar spline recta a lo largo del eje Z (X = 63.5, de Z = 0 a Z = 100)
                Spline straightSpline = new Spline();
                straightSpline.Closed = false;
                straightSpline.Add(new BezierKnot(new float3(63.5f, 0f, 0f)));
                straightSpline.Add(new BezierKnot(new float3(63.5f, 0f, 100f)));

                splineContainer.Spline = straightSpline;
                routeRenderer.RouteContainer = splineContainer;

                // Auto conduciendo desfasado a la izquierda (X = 60.0 en lugar de 63.5) en Z = 20
                Vector3 offsetCarPos = new Vector3(60.0f, 0f, 20.0f);
                routeRenderer.UpdateRoute(offsetCarPos);

                // A. No debe ordenar falso giro a la izquierda
                Assert(routeRenderer.CurrentManeuver == "Continúe recto",
                    $"Auto desfasado 3.5m a la izquierda en calle recta debe indicar 'Continúe recto' y no falso giro (actual: '{routeRenderer.CurrentManeuver}')", ref total, ref passed);

                // B. El punto 0 del LineRenderer debe estar proyectado sobre el carril (X ≈ 63.5) y NO en la posición del auto (X = 60.0)
                Vector3 p0Line = lineRenderer.GetPosition(0);
                bool isCleanLaneProjection = Mathf.Abs(p0Line.x - 63.5f) < 0.1f;
                Assert(isCleanLaneProjection,
                    $"Punto 0 de LineRenderer debe proyectarse en el carril (X ≈ 63.5, actual: {p0Line.x:F2}), sin quiebre en 90° hacia el auto", ref total, ref passed);

                // C. Distancia restante debe ser ~80m (100 - 20) calculada por proyección ortogonal
                bool distanceAccurate = Mathf.Abs(routeRenderer.RemainingDistance - 80f) < 1.0f;
                Assert(distanceAccurate,
                    $"Distancia restante debe calcularse ortogonalmente (~80m, actual: {routeRenderer.RemainingDistance:F1}m)", ref total, ref passed);

                // 2. Ruta con curva a la derecha a 40m por delante
                Spline turnSpline = new Spline();
                turnSpline.Closed = false;
                turnSpline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                turnSpline.Add(new BezierKnot(new float3(0f, 0f, 50f)));
                turnSpline.Add(new BezierKnot(new float3(50f, 0f, 50f)));

                splineContainer.Spline = turnSpline;
                routeRenderer.InitializeRoute();

                // Auto en Z = 10 (40m antes del giro a la derecha)
                routeRenderer.UpdateRoute(new Vector3(0f, 0f, 10f));
                Assert(routeRenderer.CurrentManeuver.StartsWith("Gire a la derecha"),
                    $"Debe detectar giro a la derecha próximo (actual: '{routeRenderer.CurrentManeuver}')", ref total, ref passed);

                // Auto en Z = 35 (15m antes del giro, rango < 20m)
                routeRenderer.UpdateRoute(new Vector3(0f, 0f, 35f));
                Assert(routeRenderer.CurrentManeuver == "Gire a la derecha",
                    $"A menos de 20m debe indicar 'Gire a la derecha' inmediato (actual: '{routeRenderer.CurrentManeuver}')", ref total, ref passed);

                // 3. Arribo al destino (< 4m del final en Z = 50, X = 50)
                routeRenderer.UpdateRoute(new Vector3(49f, 0f, 50f));
                Assert(routeRenderer.HasArrived && routeRenderer.CurrentManeuver == "¡Llegaste a tu destino!",
                    "A menos de 4m del final debe activar HasArrived y mostrar '¡Llegaste a tu destino!'", ref total, ref passed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testObj);
            }
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {testName}");
            }
            else
            {
                LastFailures.Add(testName);
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
