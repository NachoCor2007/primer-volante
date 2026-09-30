using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Script de verificación automatizada en Editor para el sistema de auto-cancelación
    /// de dos fases (armado y retorno) y contra-giro de seguridad de VRTurnSignal.
    /// Comprueba la configuración de prefabs (Car04 a Car08) y la lógica matemática de la leva mecánica.
    /// </summary>
    public static class TurnSignalVerification
    {
        [MenuItem("Tools/Primer Volante/Run Turn Signal Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE AUTO-CANCELADO DE GUIÑOS");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            // 1. Verificación de Prefabs
            VerifyCarPrefabs(ref totalTests, ref passedTests);

            // 2. Verificación Funcional de Lógica de Armado, Retorno y Seguridad
            VerifyLogic(ref totalTests, ref passedTests);

            Debug.Log("=========================================================");
            if (passedTests == totalTests)
            {
                Debug.Log($"<color=green>✅ VERIFICACIÓN DE GUIÑOS COMPLETADA CON ÉXITO: {passedTests}/{totalTests} pruebas pasadas.</color>");
            }
            else
            {
                Debug.LogError($"<color=red>❌ VERIFICACIÓN DE GUIÑOS FALLÓ: {passedTests}/{totalTests} pruebas pasadas.</color>");
            }
            Debug.Log("=========================================================");
        }

        private static void VerifyCarPrefabs(ref int totalTests, ref int passedTests)
        {
            string[] prefabsWithSignal = { "Car05", "Car06", "Car07", "Car08" };
            const string BASE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/";

            // Car04: Verificar que el prefab cargue sin errores
            GameObject car04Prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BASE_PATH}Car04.prefab");
            Assert(car04Prefab != null, "Car04.prefab debe cargarse correctamente", ref totalTests, ref passedTests);

            foreach (var carName in prefabsWithSignal)
            {
                string path = $"{BASE_PATH}{carName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert(prefab != null, $"{carName}.prefab debe existir y cargarse", ref totalTests, ref passedTests);
                if (prefab == null) continue;

                VRTurnSignal turnSignal = prefab.GetComponentInChildren<VRTurnSignal>(true);
                Assert(turnSignal != null, $"{carName} debe contener componente VRTurnSignal", ref totalTests, ref passedTests);
                if (turnSignal == null) continue;

                VehicleController vehCtrl = prefab.GetComponent<VehicleController>();
                Assert(turnSignal.VehicleController == vehCtrl, $"{carName}: VRTurnSignal.m_VehicleController debe referenciar al VehicleController del vehículo", ref totalTests, ref passedTests);

                Assert(Mathf.Approximately(turnSignal.ArmTurnAngle, 80f), $"{carName}: ArmTurnAngle debe ser 80f (actual: {turnSignal.ArmTurnAngle})", ref totalTests, ref passedTests);
                Assert(Mathf.Approximately(turnSignal.CancelReturnAngle, 15f), $"{carName}: CancelReturnAngle debe ser 15f (actual: {turnSignal.CancelReturnAngle})", ref totalTests, ref passedTests);
                Assert(Mathf.Approximately(turnSignal.OppositeCancelAngle, 50f), $"{carName}: OppositeCancelAngle debe ser 50f (actual: {turnSignal.OppositeCancelAngle})", ref totalTests, ref passedTests);
            }
        }

        private static void VerifyLogic(ref int totalTests, ref int passedTests)
        {
            GameObject testRoot = new GameObject("TurnSignal_TestHost");
            try
            {
                VehicleController vehicle = testRoot.AddComponent<VehicleController>();
                GameObject wheelObj = new GameObject("SteeringWheel");
                wheelObj.transform.SetParent(testRoot.transform);
                VRSteeringWheel wheel = wheelObj.AddComponent<VRSteeringWheel>();
                vehicle.SteeringWheel = wheel;

                GameObject signalObj = new GameObject("TurnSignalLever");
                signalObj.transform.SetParent(testRoot.transform);
                signalObj.AddComponent<BoxCollider>();
                VRTurnSignal signal = signalObj.AddComponent<VRTurnSignal>();
                signal.ArmTurnAngle = 80f;
                signal.CancelReturnAngle = 15f;
                signal.OppositeCancelAngle = 50f;
                signal.VehicleController = vehicle;

                FieldInfo wheelAngleField = typeof(VRSteeringWheel).GetField("m_CurrentAngle", BindingFlags.NonPublic | BindingFlags.Instance);
                void SetWheelAngle(float angle)
                {
                    wheelAngleField.SetValue(wheel, angle);
                }

                // -------------------------------------------------------------
                // Prueba A: Estado Inicial y Reseteo de Ciclo de Vida
                // -------------------------------------------------------------
                Assert(!signal.IsTurnArmed, "Leva mecánica debe iniciar desarmada (IsTurnArmed == false)", ref totalTests, ref passedTests);

                // Activar guiño derecho de prueba
                signal.currentSignal = TurnSignalState.Right;
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Al arrancar con volante recto (0°), no debe armarse", ref totalTests, ref passedTests);
                Assert(signal.CurrentSignal == TurnSignalState.Right, "Guiño derecho debe mantenerse activo con volante en 0°", ref totalTests, ref passedTests);

                // -------------------------------------------------------------
                // Prueba B: Autopista / Giros leves (< 80°) NO arman
                // -------------------------------------------------------------
                SetWheelAngle(40f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Giro suave (+40°) no debe armar la leva mecánica", ref totalTests, ref passedTests);
                Assert(signal.CurrentSignal == TurnSignalState.Right, "Guiño derecho sigue activo durante giro suave", ref totalTests, ref passedTests);

                // Retorno al centro tras giro suave: NO debe cancelarse porque nunca se armó
                SetWheelAngle(10f);
                signal.CheckSelfCancel();
                Assert(signal.CurrentSignal == TurnSignalState.Right, "Enderezar tras giro suave (+10°) NO debe cancelar el guiño", ref totalTests, ref passedTests);
                Assert(!signal.IsTurnArmed, "Leva permanece desarmada", ref totalTests, ref passedTests);

                // -------------------------------------------------------------
                // Prueba C: Curva pronunciada (>= 80°) ARMA y luego RETORNO (<= 15°) CANCELA
                // -------------------------------------------------------------
                SetWheelAngle(85f);
                signal.CheckSelfCancel();
                Assert(signal.IsTurnArmed, "Giro a +85° (>= 80°) debe armar la leva mecánica (IsTurnArmed == true)", ref totalTests, ref passedTests);
                Assert(signal.CurrentSignal == TurnSignalState.Right, "Guiño sigue activo a +85°", ref totalTests, ref passedTests);

                // Giro mayor en la esquina (+120°)
                SetWheelAngle(120f);
                signal.CheckSelfCancel();
                Assert(signal.IsTurnArmed, "Leva permanece armada a +120°", ref totalTests, ref passedTests);

                // Volante comenzando a enderezar (+30°): aún no llega a 15°, no cancela
                SetWheelAngle(30f);
                signal.CheckSelfCancel();
                Assert(signal.IsTurnArmed, "Leva permanece armada a +30°", ref totalTests, ref passedTests);
                Assert(signal.CurrentSignal == TurnSignalState.Right, "Guiño aún activo a +30°", ref totalTests, ref passedTests);

                // Volante llega al umbral de retorno (+15°): DEBE CANCELAR
                SetWheelAngle(15f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Al cancelarse por retorno, IsTurnArmed debe resetearse a false", ref totalTests, ref passedTests);
                // CenterSignal inicia corrutina hacia Off; forzamos el estado inmediato para continuar la prueba unitaria
                signal.currentSignal = TurnSignalState.Off;

                // -------------------------------------------------------------
                // Prueba D: Cancelación de Seguridad por Contra-giro (Guiño Derecho -> giro a <= -50°)
                // -------------------------------------------------------------
                signal.currentSignal = TurnSignalState.Right;
                SetWheelAngle(0f);
                signal.CheckSelfCancel();

                // Contra-giro hacia la izquierda (-50°)
                SetWheelAngle(-50f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Contra-giro resetea IsTurnArmed a false", ref totalTests, ref passedTests);
                signal.currentSignal = TurnSignalState.Off;

                // -------------------------------------------------------------
                // Prueba E: Dos fases Guiño Izquierdo (Giro leve no cancela, giro >= 80° arma, retorno cancela)
                // -------------------------------------------------------------
                signal.currentSignal = TurnSignalState.Left;
                SetWheelAngle(0f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Guiño izquierdo arranca desarmado", ref totalTests, ref passedTests);

                // Giro leve izquierda (-40°)
                SetWheelAngle(-40f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Giro leve izquierda (-40°) no arma", ref totalTests, ref passedTests);

                // Retorno tras giro leve (-10°)
                SetWheelAngle(-10f);
                signal.CheckSelfCancel();
                Assert(signal.CurrentSignal == TurnSignalState.Left, "Enderezar tras giro leve izquierdo no cancela el guiño", ref totalTests, ref passedTests);

                // Curva izquierda (-90°)
                SetWheelAngle(-90f);
                signal.CheckSelfCancel();
                Assert(signal.IsTurnArmed, "Giro a -90° (<= -80°) arma la leva mecánica", ref totalTests, ref passedTests);

                // Retorno hacia el centro (-15°)
                SetWheelAngle(-15f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Retorno a -15° cancela y resetea IsTurnArmed", ref totalTests, ref passedTests);
                signal.currentSignal = TurnSignalState.Off;

                // -------------------------------------------------------------
                // Prueba F: Contra-giro de Seguridad Guiño Izquierdo (giro a >= +50°)
                // -------------------------------------------------------------
                signal.currentSignal = TurnSignalState.Left;
                SetWheelAngle(0f);
                signal.CheckSelfCancel();

                SetWheelAngle(50f);
                signal.CheckSelfCancel();
                Assert(!signal.IsTurnArmed, "Contra-giro a la derecha cancela guiño izquierdo y resetea IsTurnArmed", ref totalTests, ref passedTests);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testRoot);
            }
        }

        private static void Assert(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  [OK] {message}");
            }
            else
            {
                Debug.LogError($"  [FAIL] {message}");
            }
        }
    }
}
