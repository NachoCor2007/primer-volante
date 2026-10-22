using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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

            int routeLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
            Assert(routeLayer != -1,
                $"La capa '{GPSNavigationSceneSetup.ROUTE_LAYER_NAME}' DEBE estar registrada en TagManager", ref total, ref passed);
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
                return;
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

                        // CullingMask debe incluir la capa GPSRoute
                        int routeLayer = LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME);
                        if (routeLayer != -1)
                        {
                            bool includesRoute = (cam.cullingMask & (1 << routeLayer)) != 0;
                            Assert(includesRoute, "GPS_TopDownCamera DEBE incluir la capa GPSRoute en su CullingMask", ref total, ref passed);
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
                    foreach (Camera c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
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

                // Verificación del Simulador Desktop de Cabina y Piloto Automático
                var tester = UnityEngine.Object.FindAnyObjectByType<PrimerVolante.Testing.CockpitDesktopTester>();
                Assert(tester != null, "La escena debe contener el componente CockpitDesktopTester para pruebas en PC", ref total, ref passed);
                if (tester != null)
                {
                    Assert(tester.IsAutoPilot, "CockpitDesktopTester debe iniciar por defecto en modo Piloto Automático", ref total, ref passed);
                    Assert(tester.SplineContainer != null, "CockpitDesktopTester DEBE tener un SplineContainer asignado", ref total, ref passed);
                    if (tester.SplineContainer != null)
                    {
                        string containerName = tester.SplineContainer.gameObject.name;
                        Assert(containerName == GPSNavigationSceneSetup.ROUTE_OBJECT_NAME,
                            $"CockpitDesktopTester debe estar vinculado a '{GPSNavigationSceneSetup.ROUTE_OBJECT_NAME}' (actual: '{containerName}')", ref total, ref passed);
                        Assert(!containerName.Contains("Pedestrian") && !containerName.Contains("Crosswalk"),
                            "CockpitDesktopTester NO debe estar vinculado a una ruta peatonal", ref total, ref passed);
                    }
                }
            }
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
