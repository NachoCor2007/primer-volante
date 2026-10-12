using System.Collections.Generic;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de pruebas automatizadas en Editor para el sistema de semáforos
    /// e intersecciones controladas (US32 / Issue #22).
    /// Valida transiciones de ciclo, inicio por evento, componentes visuales de luz,
    /// configuración de prefabs y detección de infracciones con cooldown antirrebote.
    /// </summary>
    public static class TrafficLightTests
    {
        [MenuItem("Tools/Primer Volante/Run Traffic Light Tests")]
        public static void RunAllTests()
        {
            Debug.Log("=========================================================");
            Debug.Log("🚦 INICIANDO PRUEBAS DE SEMÁFOROS E INTERSECCIONES (US32)");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            TestCycleTransitions(ref totalTests, ref passedTests);
            TestTriggerStart(ref totalTests, ref passedTests);
            TestControlMethods(ref totalTests, ref passedTests);
            TestVisualLighting(ref totalTests, ref passedTests);
            TestPrefabsConfiguration(ref totalTests, ref passedTests);
            TestRedLightViolationDetector(ref totalTests, ref passedTests);

            Debug.Log("=========================================================");
            if (passedTests == totalTests)
            {
                Debug.Log($"<color=green>✅ PRUEBAS DE SEMÁFOROS COMPLETADAS CON ÉXITO: {passedTests}/{totalTests} pruebas pasadas.</color>");
            }
            else
            {
                Debug.LogError($"<color=red>❌ PRUEBAS DE SEMÁFOROS FALLIDAS: {passedTests}/{totalTests} pruebas pasadas.</color>");
            }
            Debug.Log("=========================================================");
        }

        public static string RunAllTestsSummary()
        {
            int totalTests = 0;
            int passedTests = 0;

            TestCycleTransitions(ref totalTests, ref passedTests);
            TestTriggerStart(ref totalTests, ref passedTests);
            TestControlMethods(ref totalTests, ref passedTests);
            TestVisualLighting(ref totalTests, ref passedTests);
            TestPrefabsConfiguration(ref totalTests, ref passedTests);
            TestRedLightViolationDetector(ref totalTests, ref passedTests);

            return $"{passedTests}/{totalTests} passed (Failures: {totalTests - passedTests})";
        }

        #region 1. Transiciones de Ciclo (Green -> Yellow -> Red -> Yellow -> Green)

        private static void TestCycleTransitions(ref int total, ref int passed)
        {
            GameObject go = new GameObject("TestTrafficLight_Cycle");
            try
            {
                TrafficLightController controller = go.AddComponent<TrafficLightController>();
                controller.GreenDuration = 8f;
                controller.YellowDuration = 2.5f;
                controller.YellowToGreenDuration = 2.0f;
                controller.RedDuration = 8f;

                List<TrafficLightState> receivedEvents = new List<TrafficLightState>();
                controller.OnStateChanged += state => receivedEvents.Add(state);

                // Arrancamos en Verde
                controller.TriggerStart(TrafficLightState.Green);
                Assert(controller.IsGreen && controller.CurrentState == TrafficLightState.Green,
                    "Estado inicial de ciclo es Green", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 8f),
                    "Tiempo restante inicial en Green es 8s", ref total, ref passed);

                // Avanzamos 4s (aún en Green)
                controller.Tick(4f);
                Assert(controller.IsGreen && Mathf.Approximately(controller.TimeRemainingInState, 4f),
                    "Permanece en Green tras 4s con 4s restantes", ref total, ref passed);

                // Completamos los 4s restantes -> transiciona a Yellow (hacia Red)
                controller.Tick(4f);
                Assert(controller.IsYellow && controller.CurrentState == TrafficLightState.Yellow,
                    "Transición Green -> Yellow tras cumplir greenDuration", ref total, ref passed);
                Assert(!controller.IsTransitioningToGreen,
                    "Fase Yellow no es transición hacia verde (es hacia rojo)", ref total, ref passed);
                Assert(controller.TargetState == TrafficLightState.Red,
                    "TargetState en Yellow (venía de Green) es Red", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 2.5f),
                    "Tiempo restante en Yellow (hacia Red) es 2.5s", ref total, ref passed);

                // Avanzamos 2.5s -> transiciona a Red
                controller.Tick(2.5f);
                Assert(controller.IsRed && controller.CurrentState == TrafficLightState.Red,
                    "Transición Yellow -> Red tras cumplir yellowDuration", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 8f),
                    "Tiempo restante en Red es 8s", ref total, ref passed);

                // Avanzamos 8s -> transiciona a Yellow PREVIO A VERDE (Fase preparatoria de aviso)
                controller.Tick(8f);
                Assert(controller.IsYellow && controller.CurrentState == TrafficLightState.Yellow,
                    "Transición Red -> Yellow (aviso previo a verde) tras cumplir redDuration", ref total, ref passed);
                Assert(controller.IsTransitioningToGreen,
                    "Fase Yellow es transición hacia verde (IsTransitioningToGreen = true)", ref total, ref passed);
                Assert(controller.TargetState == TrafficLightState.Green,
                    "TargetState en Yellow (venía de Red) es Green", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 2.0f),
                    "Tiempo restante en Yellow previo a verde es yellowToGreenDuration (2.0s)", ref total, ref passed);

                // Avanzamos 2.0s -> transiciona a Green finalmente
                controller.Tick(2.0f);
                Assert(controller.IsGreen && controller.CurrentState == TrafficLightState.Green,
                    "Transición Yellow -> Green tras cumplir yellowToGreenDuration", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 8f),
                    "Tiempo restante en Green es 8s", ref total, ref passed);

                // Validamos que los eventos se emitieron en el orden exacto de 4 fases
                Assert(receivedEvents.Count >= 5 &&
                       receivedEvents[0] == TrafficLightState.Green &&
                       receivedEvents[1] == TrafficLightState.Yellow &&
                       receivedEvents[2] == TrafficLightState.Red &&
                       receivedEvents[3] == TrafficLightState.Yellow &&
                       receivedEvents[4] == TrafficLightState.Green,
                    "Eventos OnStateChanged emitidos en secuencia completa: Green -> Yellow -> Red -> Yellow -> Green", ref total, ref passed);

                // Sub-prueba: Iniciar directamente en Red y comprobar Red -> Yellow -> Green
                receivedEvents.Clear();
                controller.TriggerStart(TrafficLightState.Red);
                controller.Tick(8f);
                Assert(controller.IsYellow && controller.IsTransitioningToGreen,
                    "Al iniciar en Red, tras redDuration pasa a Yellow (hacia Verde)", ref total, ref passed);
                controller.Tick(2f);
                Assert(controller.IsGreen,
                    "Tras yellowToGreenDuration pasa a Green", ref total, ref passed);
                Assert(receivedEvents.Count >= 3 &&
                       receivedEvents[0] == TrafficLightState.Red &&
                       receivedEvents[1] == TrafficLightState.Yellow &&
                       receivedEvents[2] == TrafficLightState.Green,
                    "Secuencia Red -> Yellow -> Green validada correctamente", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region 2. Inicio por Evento (TriggerStart Verde vs Rojo)

        private static void TestTriggerStart(ref int total, ref int passed)
        {
            GameObject go = new GameObject("TestTrafficLight_TriggerStart");
            try
            {
                TrafficLightController controller = go.AddComponent<TrafficLightController>();
                controller.AutoStart = false;
                controller.GreenDuration = 6f;
                controller.RedDuration = 10f;
                controller.InitialState = TrafficLightState.Red;

                // Con autoStart = false y sin disparar, el ciclo no debe avanzar
                controller.Tick(5f);
                Assert(!controller.IsCycleActive,
                    "Ciclo inactivo antes de llamar a TriggerStart con autoStart=false", ref total, ref passed);

                // Disparo de inicio en Verde
                controller.TriggerStart(TrafficLightState.Green);
                Assert(controller.IsCycleActive && controller.IsGreen,
                    "TriggerStart(Green) activa el ciclo en estado Verde", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 6f),
                    "Tiempo restante configurado para Verde (6s)", ref total, ref passed);

                // Disparo de inicio en Rojo
                controller.TriggerStart(TrafficLightState.Red);
                Assert(controller.IsCycleActive && controller.IsRed,
                    "TriggerStart(Red) reinicia el ciclo en estado Rojo", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 10f),
                    "Tiempo restante configurado para Rojo (10s)", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region 3. Métodos de Control (Pause, Resume, SetState, Reset)

        private static void TestControlMethods(ref int total, ref int passed)
        {
            GameObject go = new GameObject("TestTrafficLight_Control");
            try
            {
                TrafficLightController controller = go.AddComponent<TrafficLightController>();
                controller.GreenDuration = 5f;
                controller.YellowDuration = 2f;
                controller.RedDuration = 5f;
                controller.TriggerStart(TrafficLightState.Green);

                // Pausa
                controller.PauseCycle();
                Assert(controller.IsPaused, "PauseCycle() marca IsPaused = true", ref total, ref passed);
                controller.Tick(10f);
                Assert(controller.IsGreen && Mathf.Approximately(controller.TimeRemainingInState, 5f),
                    "Tick durante pausa no avanza el temporizador ni cambia de estado", ref total, ref passed);

                // Reanudar
                controller.ResumeCycle();
                Assert(!controller.IsPaused, "ResumeCycle() reanuda el ciclo", ref total, ref passed);
                controller.Tick(5.1f);
                Assert(controller.IsYellow, "Tick tras reanudar transiciona a Yellow", ref total, ref passed);

                // Forzar estado
                controller.SetState(TrafficLightState.Red);
                Assert(controller.IsRed && Mathf.Approximately(controller.TimeRemainingInState, 5f),
                    "SetState(Red) fuerza el estado y reinicia su tiempo", ref total, ref passed);

                // ResetCycle
                controller.InitialState = TrafficLightState.Green;
                controller.ResetCycle();
                Assert(controller.IsGreen && Mathf.Approximately(controller.TimeRemainingInState, 5f),
                    "ResetCycle() restaura el estado a InitialState y su duración", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region 4. Control Visual de Iluminación

        private static void TestVisualLighting(ref int total, ref int passed)
        {
            GameObject go = new GameObject("TestTrafficLight_Visuals");
            try
            {
                TrafficLightController controller = go.AddComponent<TrafficLightController>();

                GameObject redObj = new GameObject("Red");
                redObj.transform.SetParent(go.transform);
                Light redLight = redObj.AddComponent<Light>();
                MeshRenderer redLens = redObj.AddComponent<MeshRenderer>();

                GameObject yellowObj = new GameObject("Yellow");
                yellowObj.transform.SetParent(go.transform);
                Light yellowLight = yellowObj.AddComponent<Light>();
                MeshRenderer yellowLens = yellowObj.AddComponent<MeshRenderer>();

                GameObject greenObj = new GameObject("Green");
                greenObj.transform.SetParent(go.transform);
                Light greenLight = greenObj.AddComponent<Light>();
                MeshRenderer greenLens = greenObj.AddComponent<MeshRenderer>();

                controller.RedLight = redLight;
                controller.YellowLight = yellowLight;
                controller.GreenLight = greenLight;

                controller.RedLensRenderer = redLens;
                controller.YellowLensRenderer = yellowLens;
                controller.GreenLensRenderer = greenLens;

                MaterialPropertyBlock redPb = new MaterialPropertyBlock();
                MaterialPropertyBlock yellowPb = new MaterialPropertyBlock();
                MaterialPropertyBlock greenPb = new MaterialPropertyBlock();

                // Estado Red
                controller.SetState(TrafficLightState.Red);
                Assert(redLight.enabled && !yellowLight.enabled && !greenLight.enabled,
                    "En estado Red: luz roja encendida, amarilla y verde apagadas", ref total, ref passed);

                redLens.GetPropertyBlock(redPb);
                yellowLens.GetPropertyBlock(yellowPb);
                greenLens.GetPropertyBlock(greenPb);
                Assert(redPb.GetColor("_BaseColor") == controller.RedActiveColor,
                    "En estado Red: lente roja tiene color activo vívido", ref total, ref passed);
                Assert(yellowPb.GetColor("_BaseColor") == controller.YellowDimmedColor,
                    "En estado Red: lente amarilla tiene color atenuado/oscuro", ref total, ref passed);
                Assert(greenPb.GetColor("_BaseColor") == controller.GreenDimmedColor,
                    "En estado Red: lente verde tiene color atenuado/oscuro", ref total, ref passed);

                // Estado Yellow
                controller.SetState(TrafficLightState.Yellow);
                Assert(!redLight.enabled && yellowLight.enabled && !greenLight.enabled,
                    "En estado Yellow: luz amarilla encendida, roja y verde apagadas", ref total, ref passed);

                redLens.GetPropertyBlock(redPb);
                yellowLens.GetPropertyBlock(yellowPb);
                greenLens.GetPropertyBlock(greenPb);
                Assert(redPb.GetColor("_BaseColor") == controller.RedDimmedColor,
                    "En estado Yellow: lente roja tiene color atenuado/oscuro", ref total, ref passed);
                Assert(yellowPb.GetColor("_BaseColor") == controller.YellowActiveColor,
                    "En estado Yellow: lente amarilla tiene color activo vívido", ref total, ref passed);
                Assert(greenPb.GetColor("_BaseColor") == controller.GreenDimmedColor,
                    "En estado Yellow: lente verde tiene color atenuado/oscuro", ref total, ref passed);

                // Estado Green
                controller.SetState(TrafficLightState.Green);
                Assert(!redLight.enabled && !yellowLight.enabled && greenLight.enabled,
                    "En estado Green: luz verde encendida, roja y amarilla apagadas", ref total, ref passed);

                redLens.GetPropertyBlock(redPb);
                yellowLens.GetPropertyBlock(yellowPb);
                greenLens.GetPropertyBlock(greenPb);
                Assert(redPb.GetColor("_BaseColor") == controller.RedDimmedColor,
                    "En estado Green: lente roja tiene color atenuado/oscuro", ref total, ref passed);
                Assert(yellowPb.GetColor("_BaseColor") == controller.YellowDimmedColor,
                    "En estado Green: lente amarilla tiene color atenuado/oscuro", ref total, ref passed);
                Assert(greenPb.GetColor("_BaseColor") == controller.GreenActiveColor,
                    "En estado Green: lente verde tiene color activo vívido", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region 5. Verificación de Prefabs Actualizados

        private static void TestPrefabsConfiguration(ref int total, ref int passed)
        {
            VerifyTrafficLightPrefab(TrafficLightPrefabSetup.SMALL_PREFAB_PATH, "Props_Traffic Signal_small", ref total, ref passed);
            VerifyTrafficLightPrefab(TrafficLightPrefabSetup.BIG_PREFAB_PATH, "Props_Traffic Signal_big", ref total, ref passed);
        }

        private static void VerifyTrafficLightPrefab(string path, string prefabName, ref int total, ref int passed)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert(prefab != null, $"{prefabName}.prefab existe en el proyecto", ref total, ref passed);

            if (prefab == null) return;

            TrafficLightController controller = prefab.GetComponent<TrafficLightController>();
            Assert(controller != null, $"{prefabName} posee el componente TrafficLightController en la raíz", ref total, ref passed);

            if (controller == null) return;

            Assert(controller.RedLight != null, $"{prefabName} tiene referencia asignada a RedLight", ref total, ref passed);
            Assert(controller.YellowLight != null, $"{prefabName} tiene referencia asignada a YellowLight", ref total, ref passed);
            Assert(controller.GreenLight != null, $"{prefabName} tiene referencia asignada a GreenLight", ref total, ref passed);

            Assert(controller.RedLensRenderer != null, $"{prefabName} tiene referencia asignada a RedLensRenderer", ref total, ref passed);
            Assert(controller.YellowLensRenderer != null, $"{prefabName} tiene referencia asignada a YellowLensRenderer", ref total, ref passed);
            Assert(controller.GreenLensRenderer != null, $"{prefabName} tiene referencia asignada a GreenLensRenderer", ref total, ref passed);

            // Verificación de Malla Low Poly y Escala Aumentada (~0.32)
            Mesh lowPolyMesh = AssetDatabase.LoadAssetAtPath<Mesh>(TrafficLightPrefabSetup.LOW_POLY_LENS_MESH_PATH);
            Assert(lowPolyMesh != null, "Asset Props_TrafficLightLens_LowPoly.asset existe", ref total, ref passed);

            if (controller.RedLensRenderer != null)
            {
                MeshFilter redMf = controller.RedLensRenderer.GetComponent<MeshFilter>();
                Assert(redMf != null && redMf.sharedMesh == lowPolyMesh,
                    $"{prefabName} RedLens tiene asignada la malla Low Poly facetada", ref total, ref passed);

                Vector3 scale = controller.RedLensRenderer.transform.localScale;
                Assert(Mathf.Abs(scale.x - 0.32f) < 0.02f && Mathf.Abs(scale.y - 0.32f) < 0.02f,
                    $"{prefabName} RedLens tiene escala aumentada ~0.32f ({scale.x:F2}, {scale.y:F2})", ref total, ref passed);
            }

            if (controller.YellowLensRenderer != null)
            {
                MeshFilter yellowMf = controller.YellowLensRenderer.GetComponent<MeshFilter>();
                Assert(yellowMf != null && yellowMf.sharedMesh == lowPolyMesh,
                    $"{prefabName} YellowLens tiene asignada la malla Low Poly facetada", ref total, ref passed);

                Vector3 scale = controller.YellowLensRenderer.transform.localScale;
                Assert(Mathf.Abs(scale.x - 0.32f) < 0.02f && Mathf.Abs(scale.y - 0.32f) < 0.02f,
                    $"{prefabName} YellowLens tiene escala aumentada ~0.32f ({scale.x:F2}, {scale.y:F2})", ref total, ref passed);
            }

            if (controller.GreenLensRenderer != null)
            {
                MeshFilter greenMf = controller.GreenLensRenderer.GetComponent<MeshFilter>();
                Assert(greenMf != null && greenMf.sharedMesh == lowPolyMesh,
                    $"{prefabName} GreenLens tiene asignada la malla Low Poly facetada", ref total, ref passed);

                Vector3 scale = controller.GreenLensRenderer.transform.localScale;
                Assert(Mathf.Abs(scale.x - 0.32f) < 0.02f && Mathf.Abs(scale.y - 0.32f) < 0.02f,
                    $"{prefabName} GreenLens tiene escala aumentada ~0.32f ({scale.x:F2}, {scale.y:F2})", ref total, ref passed);
            }

            if (controller.RedLight != null)
            {
                Assert(controller.RedLight.type == LightType.Point, $"{prefabName} RedLight es Point Light", ref total, ref passed);
                Assert(controller.RedLight.range >= 6.0f && controller.RedLight.intensity >= 6.0f,
                    $"{prefabName} RedLight tiene rango e intensidad para exteriores (>= 6)", ref total, ref passed);
            }

            if (controller.YellowLight != null)
            {
                Assert(controller.YellowLight.type == LightType.Point, $"{prefabName} YellowLight es Point Light", ref total, ref passed);
            }

            if (controller.GreenLight != null)
            {
                Assert(controller.GreenLight.type == LightType.Point, $"{prefabName} GreenLight es Point Light", ref total, ref passed);
            }

            Assert(controller.RedDuration > 0f && controller.YellowDuration > 0f && controller.GreenDuration > 0f && controller.YellowToGreenDuration > 0f,
                $"{prefabName} tiene duraciones de fase positivas por defecto (incluyendo yellowToGreen)", ref total, ref passed);
        }

        #endregion

        #region 6. Detección de Infracciones de Luz Roja (RedLightViolationDetector)

        private static void TestRedLightViolationDetector(ref int total, ref int passed)
        {
            GameObject tlObj = new GameObject("TrafficLight_For_Violation_Test");
            GameObject detectorObj = new GameObject("Detector");
            GameObject vehicleObj = new GameObject("TestVehicle");
            GameObject dummyObj = new GameObject("NonVehicleObject");

            try
            {
                TrafficLightController tl = tlObj.AddComponent<TrafficLightController>();
                tl.TriggerStart(TrafficLightState.Red);

                BoxCollider triggerCol = detectorObj.AddComponent<BoxCollider>();
                triggerCol.isTrigger = true;
                RedLightViolationDetector detector = detectorObj.AddComponent<RedLightViolationDetector>();
                detector.TrafficLight = tl;
                detector.CooldownDuration = 2f;

                // Vehículo con VehicleController y collider
                vehicleObj.AddComponent<Rigidbody>();
                VehicleController vc = vehicleObj.AddComponent<VehicleController>();
                BoxCollider vehicleCollider = vehicleObj.AddComponent<BoxCollider>();

                // Objeto no vehículo
                BoxCollider dummyCollider = dummyObj.AddComponent<BoxCollider>();

                int violationEventCount = 0;
                detector.OnViolationDetected += col => violationEventCount++;

                // Prueba 1: Cruce en ROJO con vehículo -> debe registrar infracción
                tl.SetState(TrafficLightState.Red);
                bool result1 = detector.TryRecordViolation(vehicleCollider);
                Assert(result1 && detector.TotalViolationsRecorded == 1 && violationEventCount == 1,
                    "Infracción detectada exitosamente cuando el semáforo está en ROJO", ref total, ref passed);

                // Prueba 2: Segundo cruce inmediato -> debe descartarse por cooldown antirrebote
                bool result2 = detector.TryRecordViolation(vehicleCollider);
                Assert(!result2 && detector.TotalViolationsRecorded == 1 && violationEventCount == 1,
                    "Segundo toque consecutivo dentro del cooldown es ignorado (antirrebote)", ref total, ref passed);

                // Prueba 3: Reset de cooldown y cruce en VERDE -> NO debe registrar infracción
                detector.ResetCooldown();
                tl.SetState(TrafficLightState.Green);
                bool result3 = detector.TryRecordViolation(vehicleCollider);
                Assert(!result3 && detector.TotalViolationsRecorded == 1,
                    "No se registra infracción cuando el semáforo está en VERDE", ref total, ref passed);

                // Prueba 4: Cruce en AMARILLO (hacia Rojo) -> NO debe registrar infracción
                detector.ResetCooldown();
                tl.SetState(TrafficLightState.Yellow);
                bool result4 = detector.TryRecordViolation(vehicleCollider);
                Assert(!result4 && detector.TotalViolationsRecorded == 1,
                    "No se registra infracción cuando el semáforo está en AMARILLO (hacia Rojo)", ref total, ref passed);

                // Prueba 4b: Cruce en AMARILLO (aviso previo a Verde) -> NO debe registrar infracción
                detector.ResetCooldown();
                tl.TriggerStart(TrafficLightState.Red);
                tl.Tick(8f); // Pasa a Yellow previo a Verde
                Assert(tl.IsYellow && tl.IsTransitioningToGreen, "Semáforo en fase amarilla previa a verde", ref total, ref passed);
                bool result4b = detector.TryRecordViolation(vehicleCollider);
                Assert(!result4b && detector.TotalViolationsRecorded == 1,
                    "No se registra infracción en fase amarilla previa a verde (aviso preparatorio)", ref total, ref passed);

                // Prueba 5: Cruce de objeto NO vehículo (sin VehicleController ni tag) -> ignorado
                detector.ResetCooldown();
                tl.SetState(TrafficLightState.Red);
                bool result5 = detector.TryRecordViolation(dummyCollider);
                Assert(!result5 && detector.TotalViolationsRecorded == 1,
                    "Collider sin VehicleController ni Tag es ignorado por el detector", ref total, ref passed);

                // Prueba 6: Cruce con tag configurado
                detector.CheckVehicleController = false;
                detector.VehicleTag = "Player";
                dummyObj.tag = "Player";
                bool result6 = detector.TryRecordViolation(dummyCollider);
                Assert(result6 && detector.TotalViolationsRecorded == 2,
                    "Detector reconoce vehículo por Tag configurado", ref total, ref passed);

                // Prueba 7: Semáforo nulo / desconectado -> no registra infracción
                detector.ResetCooldown();
                detector.TrafficLight = null;
                bool result7 = detector.TryRecordViolation(vehicleCollider);
                Assert(!result7, "Sin semáforo asignado no se registran infracciones", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(tlObj);
                Object.DestroyImmediate(detectorObj);
                Object.DestroyImmediate(vehicleObj);
                Object.DestroyImmediate(dummyObj);
            }
        }

        #endregion

        #region Helpers

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
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }

        #endregion
    }
}
