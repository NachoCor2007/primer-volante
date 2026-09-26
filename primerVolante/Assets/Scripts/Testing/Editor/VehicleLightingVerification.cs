using System;
using UnityEditor;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Script de verificación automatizada en Editor para el sistema integral de iluminación.
    /// Comprueba la perilla VRHeadlightKnob, el orquestador VehicleLightingController,
    /// las luces de freno, reversa y la sincronización con DashboardUIController.
    /// </summary>
    public static class VehicleLightingVerification
    {
        [MenuItem("Tools/Primer Volante/Run Vehicle Lighting Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL SISTEMA DE ILUMINACIÓN");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            // 1. Instanciar Car06 desde el Prefab para prueba aislada
            const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car06.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert(prefab != null, "Car06.prefab debe existir", ref totalTests, ref passedTests);

            GameObject carInstance = UnityEngine.Object.Instantiate(prefab);
            carInstance.name = "Car06_TestVerification";

            try
            {
                VehicleController vehicleCtrl = carInstance.GetComponent<VehicleController>();
                Assert(vehicleCtrl != null, "VehicleController debe estar adjunto a Car06", ref totalTests, ref passedTests);

                VRHeadlightKnob knob = carInstance.GetComponentInChildren<VRHeadlightKnob>(true);
                Assert(knob != null, "VRHeadlightKnob debe existir en Car06", ref totalTests, ref passedTests);

                VehicleLightingController lightingCtrl = carInstance.GetComponent<VehicleLightingController>();
                Assert(lightingCtrl != null, "VehicleLightingController debe existir en Car06", ref totalTests, ref passedTests);

                DashboardUIController dashboardUI = carInstance.GetComponentInChildren<DashboardUIController>(true);
                Assert(dashboardUI != null, "DashboardUIController debe existir en Car06", ref totalTests, ref passedTests);

                // Comprobar posición y orientación de VRHeadlightKnob
                Assert(Mathf.Abs(knob.transform.localPosition.x - (-0.595f)) < 0.01f &&
                       Mathf.Abs(knob.transform.localPosition.y - 0.916f) < 0.01f &&
                       Mathf.Abs(knob.transform.localPosition.z - 0.285f) < 0.01f,
                       "VRHeadlightKnob está ubicada al ras en el tablero (-0.595, 0.916, 0.285)", ref totalTests, ref passedTests);

                SphereCollider knobCol = knob.GetComponent<SphereCollider>();
                Assert(knobCol != null && Mathf.Abs(knobCol.center.z - (-0.012f)) < 0.005f,
                       "SphereCollider está centrado en la perilla visible (center.z ~ -0.012)", ref totalTests, ref passedTests);

                Transform rotorT = knob.transform.Find("Knob_Rotor");
                Transform dialMesh = rotorT != null ? rotorT.Find("Dial_Mesh") : null;
                Assert(dialMesh != null && Mathf.DeltaAngle(dialMesh.localEulerAngles.x, -90f) < 1f,
                       "Dial_Mesh está orientado hacia el conductor (-90° X)", ref totalTests, ref passedTests);

                // Comprobar jerarquía de faros y luces
                Transform frontLights = carInstance.transform.Find("Headlights_Front");
                Assert(frontLights != null, "Headlights_Front debe existir", ref totalTests, ref passedTests);

                Transform rearLights = carInstance.transform.Find("Taillights_Rear");
                Assert(rearLights != null, "Taillights_Rear debe existir", ref totalTests, ref passedTests);

                // Comprobar testigos del tablero
                Transform indicatorLow = carInstance.transform.Find("Dashboard_Cluster_Canvas/Indicator_Headlights");
                Assert(indicatorLow != null, "Indicator_Headlights debe existir en el cluster", ref totalTests, ref passedTests);

                Transform indicatorHigh = carInstance.transform.Find("Dashboard_Cluster_Canvas/Indicator_HighBeams");
                Assert(indicatorHigh != null, "Indicator_HighBeams debe existir en el cluster", ref totalTests, ref passedTests);

                // TEST 2: Estado inicial Off
                knob.SetMode(HeadlightMode.Off, animated: false);
                Assert(knob.CurrentMode == HeadlightMode.Off, "Knob en Off", ref totalTests, ref passedTests);
                Assert(lightingCtrl.CurrentHeadlightMode == HeadlightMode.Off, "LightingController en Off", ref totalTests, ref passedTests);
                Assert(dashboardUI.CurrentHeadlightMode == HeadlightMode.Off, "DashboardUI en Off", ref totalTests, ref passedTests);

                // TEST 3: Modo Luces Bajas (LowBeam)
                knob.SetMode(HeadlightMode.LowBeam, animated: false);
                Assert(knob.CurrentMode == HeadlightMode.LowBeam, "Knob en LowBeam", ref totalTests, ref passedTests);
                Assert(lightingCtrl.CurrentHeadlightMode == HeadlightMode.LowBeam, "LightingController en LowBeam", ref totalTests, ref passedTests);
                Assert(dashboardUI.CurrentHeadlightMode == HeadlightMode.LowBeam, "DashboardUI en LowBeam", ref totalTests, ref passedTests);

                // TEST 4: Modo Luces Altas (HighBeam)
                knob.SetMode(HeadlightMode.HighBeam, animated: false);
                Assert(knob.CurrentMode == HeadlightMode.HighBeam, "Knob en HighBeam", ref totalTests, ref passedTests);
                Assert(lightingCtrl.CurrentHeadlightMode == HeadlightMode.HighBeam, "LightingController en HighBeam", ref totalTests, ref passedTests);
                Assert(dashboardUI.CurrentHeadlightMode == HeadlightMode.HighBeam, "DashboardUI en HighBeam", ref totalTests, ref passedTests);

                // TEST 5: Ciclo de perilla (CycleMode: HighBeam -> Off)
                knob.CycleMode();
                Assert(knob.CurrentMode == HeadlightMode.Off, "CycleMode de HighBeam pasa a Off", ref totalTests, ref passedTests);

                // TEST 6: StepMode (+1)
                knob.StepMode(1);
                Assert(knob.CurrentMode == HeadlightMode.LowBeam, "StepMode(+1) pasa a LowBeam", ref totalTests, ref passedTests);

                // TEST 7: Luces de Freno (BrakeValue > 0.05)
                var brakeField = typeof(VehicleController).GetField("m_BrakeValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (brakeField != null)
                {
                    brakeField.SetValue(vehicleCtrl, 0.8f);
                }

                // Invocar actualización en LightingController
                lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                Assert(lightingCtrl.IsBrakeActive == true, "LightingController detecta freno activo (BrakeValue > 0.05)", ref totalTests, ref passedTests);

                // Soltar freno
                if (brakeField != null)
                {
                    brakeField.SetValue(vehicleCtrl, 0.0f);
                }
                lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                Assert(lightingCtrl.IsBrakeActive == false, "LightingController detecta freno liberado", ref totalTests, ref passedTests);

                // TEST 8: Marcha Atrás (GearState.Reverse)
                vehicleCtrl.CurrentGear = GearState.Reverse;
                lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                Assert(lightingCtrl.IsReverseActive == true, "LightingController detecta marcha atrás activa", ref totalTests, ref passedTests);

                vehicleCtrl.CurrentGear = GearState.Drive;
                lightingCtrl.SendMessage("UpdateBrakeAndReverse", true, SendMessageOptions.DontRequireReceiver);
                Assert(lightingCtrl.IsReverseActive == false, "LightingController desactiva marcha atrás al salir de R", ref totalTests, ref passedTests);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(carInstance);
            }

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
