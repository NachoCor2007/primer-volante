using System;
using UnityEditor;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada en Editor para la integración unificada de cabina (Car07):
    /// Comprueba Freno de mano nativo (VRHandbrake + desaceleración 12 m/s² + testigo P),
    /// Perilla nativa (VRHeadlightKnob + mdl_car02_lights_knob + testigos Bajas/Altas),
    /// Sistema completo de iluminación (delantera 4 puntos, trasera freno/posición/reversa)
    /// y controles diegéticos (volante, palanca P R N D, guiños, balizas, ignición).
    /// </summary>
    public static class CockpitIntegrationVerification
    {
        [MenuItem("Tools/Primer Volante/Run Cockpit Integration Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE INTEGRACIÓN DE CABINA (Car07)");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            // 1. Comprobar existencia del Prefab Car07
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CockpitIntegrationSetup.PREFAB_CAR07_PATH);
            Assert(prefab != null, $"Car07.prefab debe existir en {CockpitIntegrationSetup.PREFAB_CAR07_PATH}", ref totalTests, ref passedTests);

            GameObject carInstance = UnityEngine.Object.Instantiate(prefab);
            carInstance.name = "Car07_IntegrationTest";
            carInstance.BroadcastMessage("Awake", SendMessageOptions.DontRequireReceiver);
            carInstance.BroadcastMessage("Start", SendMessageOptions.DontRequireReceiver);

            try
            {
                // =========================================================================
                // 2. Controladores principales
                // =========================================================================
                VehicleController vehicleCtrl = carInstance.GetComponent<VehicleController>();
                Assert(vehicleCtrl != null, "VehicleController debe estar adjunto a Car07", ref totalTests, ref passedTests);

                VehicleLightingController lightingCtrl = carInstance.GetComponent<VehicleLightingController>();
                Assert(lightingCtrl != null, "VehicleLightingController debe estar adjunto a Car07", ref totalTests, ref passedTests);

                DashboardUIController dashboardUI = carInstance.GetComponentInChildren<DashboardUIController>(true);
                Assert(dashboardUI != null, "DashboardUIController debe existir en Car07", ref totalTests, ref passedTests);

                // =========================================================================
                // 3. Freno de mano nativo (mdl_car02_brake / VRHandbrake)
                // =========================================================================
                Transform brakeTransform = carInstance.transform.Find("mdl_car02_brake");
                Assert(brakeTransform != null, "mdl_car02_brake debe existir como geometría nativa de Car07", ref totalTests, ref passedTests);

                VRHandbrake handbrake = carInstance.GetComponentInChildren<VRHandbrake>(true);
                Assert(handbrake != null, "VRHandbrake debe estar adjunto a mdl_car02_brake", ref totalTests, ref passedTests);
                if (handbrake != null)
                {
                    Assert(handbrake.rotationAxis == Vector3.right, "VRHandbrake rotationAxis debe ser Vector3.right", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(handbrake.releaseAngleRange, 36f), "VRHandbrake releaseAngleRange debe ser 36°", ref totalTests, ref passedTests);
                }

                // Desaceleración proporcional en VehicleController (m_HandbrakeForce = 12f)
                var hfProp = typeof(VehicleController).GetField("m_HandbrakeForce", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                float handbrakeForce = hfProp != null ? (float)hfProp.GetValue(vehicleCtrl) : 0f;
                Assert(Mathf.Approximately(handbrakeForce, 12f), "VehicleController.m_HandbrakeForce debe ser 12 m/s²", ref totalTests, ref passedTests);

                // =========================================================================
                // 4. Perilla de luces nativa (mdl_car02_lights_knob / VRHeadlightKnob)
                // =========================================================================
                Transform knobTransform = carInstance.transform.Find("mdl_car02_lights_knob");
                Assert(knobTransform != null, "mdl_car02_lights_knob debe existir como geometría nativa en Car07", ref totalTests, ref passedTests);

                VRHeadlightKnob knob = carInstance.GetComponentInChildren<VRHeadlightKnob>(true);
                Assert(knob != null, "VRHeadlightKnob debe estar adjunto a mdl_car02_lights_knob", ref totalTests, ref passedTests);
                if (knob != null)
                {
                    Assert(knob.transform == knobTransform, "VRHeadlightKnob debe estar exactamente sobre el transform mdl_car02_lights_knob", ref totalTests, ref passedTests);
                }

                Transform proceduralKnob = carInstance.transform.Find("VRHeadlightKnob");
                Assert(proceduralKnob == null, "No debe existir cilindro procedural duplicado VRHeadlightKnob en Car07", ref totalTests, ref passedTests);

                // =========================================================================
                // 5. Tablero Diegético (Testigos)
                // =========================================================================
                Transform handbrakeIndicator = carInstance.transform.Find("Dashboard_Cluster_Canvas/Indicator_Handbrake");
                Assert(handbrakeIndicator != null, "Indicator_Handbrake (P) debe existir en el cluster diegético", ref totalTests, ref passedTests);

                Transform headlightsIndicator = carInstance.transform.Find("Dashboard_Cluster_Canvas/Indicator_Headlights");
                Assert(headlightsIndicator != null, "Indicator_Headlights debe existir en el cluster diegético", ref totalTests, ref passedTests);

                Transform highBeamsIndicator = carInstance.transform.Find("Dashboard_Cluster_Canvas/Indicator_HighBeams");
                Assert(highBeamsIndicator != null, "Indicator_HighBeams [ D ] debe existir en el cluster diegético", ref totalTests, ref passedTests);

                // =========================================================================
                // 6. Pruebas Funcionales: Freno de Mano -> Tablero & Conducción
                // =========================================================================
                if (handbrake != null)
                {
                    // Accionar a fondo
                    handbrake.SetEngagementImmediate(1f);
                    Assert(handbrake.IsEngaged == true, "VRHandbrake accionado (Engagement=1) reporta IsEngaged=true", ref totalTests, ref passedTests);
                    Assert(vehicleCtrl.IsHandbrakeEngaged == true, "VehicleController detecta freno de mano accionado", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(vehicleCtrl.HandbrakeEngagement, 1f), "VehicleController.HandbrakeEngagement == 1", ref totalTests, ref passedTests);

                    // Liberar totalmente
                    handbrake.SetEngagementImmediate(0f);
                    Assert(handbrake.IsEngaged == false, "VRHandbrake liberado (Engagement=0) reporta IsEngaged=false", ref totalTests, ref passedTests);
                    Assert(vehicleCtrl.IsHandbrakeEngaged == false, "VehicleController detecta freno de mano liberado", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(vehicleCtrl.HandbrakeEngagement) < 0.01f, "VehicleController.HandbrakeEngagement == 0", ref totalTests, ref passedTests);

                    // Volver a accionar
                    handbrake.SetEngagementImmediate(1f);
                }

                // =========================================================================
                // 7. Pruebas Funcionales: Perilla de Luces -> Tablero & Faros
                // =========================================================================
                if (knob != null)
                {
                    // Modo Off
                    knob.SetMode(HeadlightMode.Off, animated: false);
                    Assert(knob.CurrentMode == HeadlightMode.Off, "Knob en Off", ref totalTests, ref passedTests);
                    Assert(lightingCtrl.CurrentHeadlightMode == HeadlightMode.Off, "VehicleLightingController en Off", ref totalTests, ref passedTests);
                    Assert(dashboardUI.CurrentHeadlightMode == HeadlightMode.Off, "DashboardUIController en Off", ref totalTests, ref passedTests);

                    // Modo LowBeam (Bajas)
                    knob.SetMode(HeadlightMode.LowBeam, animated: false);
                    Assert(knob.CurrentMode == HeadlightMode.LowBeam, "Knob en LowBeam", ref totalTests, ref passedTests);
                    Assert(lightingCtrl.CurrentHeadlightMode == HeadlightMode.LowBeam, "VehicleLightingController en LowBeam", ref totalTests, ref passedTests);
                    Assert(dashboardUI.CurrentHeadlightMode == HeadlightMode.LowBeam, "DashboardUIController en LowBeam", ref totalTests, ref passedTests);

                    // Modo HighBeam (Altas)
                    knob.SetMode(HeadlightMode.HighBeam, animated: false);
                    Assert(knob.CurrentMode == HeadlightMode.HighBeam, "Knob en HighBeam", ref totalTests, ref passedTests);
                    Assert(lightingCtrl.CurrentHeadlightMode == HeadlightMode.HighBeam, "VehicleLightingController en HighBeam", ref totalTests, ref passedTests);
                    Assert(dashboardUI.CurrentHeadlightMode == HeadlightMode.HighBeam, "DashboardUIController en HighBeam", ref totalTests, ref passedTests);

                    // Ciclo por teclado (L): HighBeam -> Off
                    knob.CycleMode();
                    Assert(knob.CurrentMode == HeadlightMode.Off, "CycleMode (tecla L) desde HighBeam pasa a Off", ref totalTests, ref passedTests);

                    // StepMode (+1): Off -> LowBeam
                    knob.StepMode(1);
                    Assert(knob.CurrentMode == HeadlightMode.LowBeam, "StepMode(+1) desde Off pasa a LowBeam", ref totalTests, ref passedTests);
                }

                // =========================================================================
                // 8. Luces de Freno y Marcha Atrás
                // =========================================================================
                if (lightingCtrl != null)
                {
                    var brakeField = typeof(VehicleController).GetField("m_BrakeValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (brakeField != null)
                    {
                        brakeField.SetValue(vehicleCtrl, 0.8f);
                    }
                    lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                    Assert(lightingCtrl.IsBrakeActive == true, "Luces de freno activas al pisar freno (BrakeValue > 0.05)", ref totalTests, ref passedTests);

                    if (brakeField != null)
                    {
                        brakeField.SetValue(vehicleCtrl, 0.0f);
                    }
                    lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                    Assert(lightingCtrl.IsBrakeActive == false, "Luces de freno desactivadas al soltar el freno", ref totalTests, ref passedTests);

                    vehicleCtrl.CurrentGear = GearState.Reverse;
                    lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                    Assert(lightingCtrl.IsReverseActive == true, "Luz de retroceso activa al engranar Reverse (R)", ref totalTests, ref passedTests);

                    vehicleCtrl.CurrentGear = GearState.Drive;
                    lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                    Assert(lightingCtrl.IsReverseActive == false, "Luz de retroceso apagada al salir de Reverse", ref totalTests, ref passedTests);
                }

                // =========================================================================
                // 9. Controles complementarios de cabina
                // =========================================================================
                VRSteeringWheel wheel = carInstance.GetComponentInChildren<VRSteeringWheel>(true);
                Assert(wheel != null, "VRSteeringWheel debe estar presente en Car07", ref totalTests, ref passedTests);

                VRGearShifter shifter = carInstance.GetComponentInChildren<VRGearShifter>(true);
                Assert(shifter != null, "VRGearShifter debe estar presente en Car07", ref totalTests, ref passedTests);

                VRTurnSignal turnSignal = carInstance.GetComponentInChildren<VRTurnSignal>(true);
                Assert(turnSignal != null, "VRTurnSignal debe estar presente en Car07", ref totalTests, ref passedTests);

                // =========================================================================
                // 10. Blindaje Físico y Eliminación de Fuerzas Fantasma
                // =========================================================================
                // A. Ninguna luz debe tener colliders físicos
                int lightColliderCount = 0;
                foreach (var col in carInstance.GetComponentsInChildren<Collider>(true))
                {
                    if (col.gameObject == carInstance) continue;
                    string colName = col.gameObject.name;
                    if (colName.StartsWith("BlinkerLight") ||
                        colName.StartsWith("Taillight") ||
                        colName.StartsWith("Headlight") ||
                        colName.StartsWith("ReverseLight") ||
                        colName.StartsWith("TailBrakeLight") ||
                        colName.StartsWith("SpotLight") ||
                        col.GetComponent<BlinkerLight>() != null)
                    {
                        lightColliderCount++;
                    }
                }
                Assert(lightColliderCount == 0, $"Las luces no deben tener colliders físicos (encontrados: {lightColliderCount})", ref totalTests, ref passedTests);

                // B. mdl_car02_lights_knob debe tener solo SphereCollider (sin BoxCollider)
                Transform lightsKnobTrans = carInstance.transform.Find("mdl_car02_lights_knob");
                if (lightsKnobTrans != null)
                {
                    BoxCollider knobBox = lightsKnobTrans.GetComponent<BoxCollider>();
                    SphereCollider knobSphere = lightsKnobTrans.GetComponent<SphereCollider>();
                    Assert(knobBox == null, "mdl_car02_lights_knob NO debe tener BoxCollider", ref totalTests, ref passedTests);
                    Assert(knobSphere != null, "mdl_car02_lights_knob debe tener SphereCollider", ref totalTests, ref passedTests);
                }

                // C. mdl_car04_body NO debe tener Rigidbody propio
                Transform bodyTrans = carInstance.transform.Find("mdl_car04_body");
                if (bodyTrans != null)
                {
                    Rigidbody bodyRb = bodyTrans.GetComponent<Rigidbody>();
                    Assert(bodyRb == null, "mdl_car04_body NO debe tener Rigidbody", ref totalTests, ref passedTests);
                }

                // D. mdl_car02_steering_wheel NO debe tener Collider/MeshCollider (para no rozar el chasis)
                Transform wheelMeshTrans = carInstance.transform.Find("SteeringWheel_Pivot/mdl_car02_steering_wheel");
                if (wheelMeshTrans == null)
                {
                    foreach (var t in carInstance.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "mdl_car02_steering_wheel")
                        {
                            wheelMeshTrans = t;
                            break;
                        }
                    }
                }
                if (wheelMeshTrans != null)
                {
                    Collider wheelCol = wheelMeshTrans.GetComponent<Collider>();
                    Assert(wheelCol == null, "mdl_car02_steering_wheel NO debe tener Collider (evita fricción y empuje con el chasis)", ref totalTests, ref passedTests);
                }

                // E. Todos los Rigidbodies hijos deben ser cinemáticos y sin gravedad
                int dynamicChildRbCount = 0;
                int gravityChildRbCount = 0;
                foreach (var rb in carInstance.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (rb.gameObject != carInstance)
                    {
                        if (!rb.isKinematic) dynamicChildRbCount++;
                        if (rb.useGravity) gravityChildRbCount++;
                    }
                }
                Assert(dynamicChildRbCount == 0, $"Rigidbodies hijos dinámicos no permitidos (encontrados: {dynamicChildRbCount})", ref totalTests, ref passedTests);
                Assert(gravityChildRbCount == 0, $"Rigidbodies hijos con useGravity no permitidos (encontrados: {gravityChildRbCount})", ref totalTests, ref passedTests);

                // F. Rigidbody raíz de Car07 debe ser el único dinámico con gravedad
                Rigidbody rootRb = carInstance.GetComponent<Rigidbody>();
                Assert(rootRb != null && !rootRb.isKinematic && rootRb.useGravity, "Car07 raíz debe ser Rigidbody dinámico con useGravity=true", ref totalTests, ref passedTests);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(carInstance);
            }

            // 11. Comprobar existencia de la escena integradora
            bool sceneExists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CockpitIntegrationSetup.SCENE_INTEGRATION_PATH));
            Assert(sceneExists, $"Escena de integración {CockpitIntegrationSetup.SCENE_INTEGRATION_PATH} debe existir", ref totalTests, ref passedTests);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passedTests}/{totalTests} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
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
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
