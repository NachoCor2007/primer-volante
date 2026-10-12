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

        #region 1. Transiciones de Ciclo (Green -> Yellow -> Red -> Green)

        private static void TestCycleTransitions(ref int total, ref int passed)
        {
            GameObject go = new GameObject("TestTrafficLight_Cycle");
            try
            {
                TrafficLightController controller = go.AddComponent<TrafficLightController>();
                controller.GreenDuration = 8f;
                controller.YellowDuration = 2.5f;
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

                // Completamos los 4s restantes -> transiciona a Yellow
                controller.Tick(4f);
                Assert(controller.IsYellow && controller.CurrentState == TrafficLightState.Yellow,
                    "Transición Green -> Yellow tras cumplir greenDuration", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 2.5f),
                    "Tiempo restante en Yellow es 2.5s", ref total, ref passed);

                // Avanzamos 2.5s -> transiciona a Red
                controller.Tick(2.5f);
                Assert(controller.IsRed && controller.CurrentState == TrafficLightState.Red,
                    "Transición Yellow -> Red tras cumplir yellowDuration", ref total, ref passed);
                Assert(Mathf.Approximately(controller.TimeRemainingInState, 8f),
                    "Tiempo restante en Red es 8s", ref total, ref passed);

                // Avanzamos 8s -> transiciona a Green nuevamente
                controller.Tick(8f);
                Assert(controller.IsGreen && controller.CurrentState == TrafficLightState.Green,
                    "Transición Red -> Green tras cumplir redDuration", ref total, ref passed);

                // Validamos que los eventos se emitieron en el orden exacto
                Assert(receivedEvents.Count >= 4 &&
                       receivedEvents[0] == TrafficLightState.Green &&
                       receivedEvents[1] == TrafficLightState.Yellow &&
                       receivedEvents[2] == TrafficLightState.Red &&
                       receivedEvents[3] == TrafficLightState.Green,
                    "Eventos OnStateChanged emitidos en secuencia: Green -> Yellow -> Red -> Green", ref total, ref passed);
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

                GameObject yellowObj = new GameObject("Yellow");
                yellowObj.transform.SetParent(go.transform);
                Light yellowLight = yellowObj.AddComponent<Light>();

                GameObject greenObj = new GameObject("Green");
                greenObj.transform.SetParent(go.transform);
                Light greenLight = greenObj.AddComponent<Light>();

                controller.RedLight = redLight;
                controller.YellowLight = yellowLight;
                controller.GreenLight = greenLight;

                // Estado Red
                controller.SetState(TrafficLightState.Red);
                Assert(redLight.enabled && !yellowLight.enabled && !greenLight.enabled,
                    "En estado Red: luz roja encendida, amarilla y verde apagadas", ref total, ref passed);

                // Estado Yellow
                controller.SetState(TrafficLightState.Yellow);
                Assert(!redLight.enabled && yellowLight.enabled && !greenLight.enabled,
                    "En estado Yellow: luz amarilla encendida, roja y verde apagadas", ref total, ref passed);

                // Estado Green
                controller.SetState(TrafficLightState.Green);
                Assert(!redLight.enabled && !yellowLight.enabled && greenLight.enabled,
                    "En estado Green: luz verde encendida, roja y amarilla apagadas", ref total, ref passed);
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

            if (controller.RedLight != null)
            {
                Assert(controller.RedLight.type == LightType.Point, $"{prefabName} RedLight es Point Light", ref total, ref passed);
                Assert(controller.RedLight.range > 0f && controller.RedLight.intensity > 0f,
                    $"{prefabName} RedLight tiene rango e intensidad válidos (> 0)", ref total, ref passed);
            }

            if (controller.YellowLight != null)
            {
                Assert(controller.YellowLight.type == LightType.Point, $"{prefabName} YellowLight es Point Light", ref total, ref passed);
            }

            if (controller.GreenLight != null)
            {
                Assert(controller.GreenLight.type == LightType.Point, $"{prefabName} GreenLight es Point Light", ref total, ref passed);
            }

            Assert(controller.RedDuration > 0f && controller.YellowDuration > 0f && controller.GreenDuration > 0f,
                $"{prefabName} tiene duraciones de fase positivas por defecto", ref total, ref passed);
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

                // Prueba 4: Cruce en AMARILLO -> NO debe registrar infracción
                detector.ResetCooldown();
                tl.SetState(TrafficLightState.Yellow);
                bool result4 = detector.TryRecordViolation(vehicleCollider);
                Assert(!result4 && detector.TotalViolationsRecorded == 1,
                    "No se registra infracción cuando el semáforo está en AMARILLO", ref total, ref passed);

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
