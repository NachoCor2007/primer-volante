using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    [InitializeOnLoad]
    public static class SetupSimulatorAndVerifyRegression
    {
        private const string SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/DemoComponents.unity";
        private const string SIMULATOR_PREFAB_PATH = "Assets/Samples/XR Interaction Toolkit/3.6.0/XR Interaction Simulator/XR Interaction Simulator.prefab";
        private static readonly string FLAG_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "SetupSimulatorAndVerifyRegression_Done.flag");
        private static readonly string RESULT_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "SetupSimulatorAndVerifyRegression_Result.txt");

        static SetupSimulatorAndVerifyRegression()
        {
            EditorApplication.delayCall += OnEditorLoaded;
        }

        private static void OnEditorLoaded()
        {
            if (File.Exists(FLAG_FILE))
            {
                return;
            }

            Debug.Log("[SetupSimulatorAndVerifyRegression] 🚀 Auto-executing Simulator Setup and Regression Verification...");
            Execute();
        }

        [MenuItem("Tools/Primer Volante/Setup Simulator and Verify Regression")]
        public static void MenuExecute()
        {
            Debug.Log("[SetupSimulatorAndVerifyRegression] 🔘 Menu triggered.");
            Execute();
        }

        public static void Execute()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Setup Simulator & Regression Verification Log ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // 1. Abrir la escena DemoComponents.unity
                sb.AppendLine("\n--- 1. Verificando escena DemoComponents.unity ---");
                Scene scene = EditorSceneManager.GetActiveScene();
                if (scene.path != SCENE_PATH)
                {
                    scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
                }

                if (!scene.IsValid())
                {
                    throw new Exception($"No se pudo abrir la escena en {SCENE_PATH}");
                }
                sb.AppendLine($"Escena activa: {scene.name} ({scene.path})");

                // 2. SETUP DEL SIMULADOR: Verificar o agregar XR Interaction Simulator
                sb.AppendLine("\n--- 2. Verificando XR Device / Interaction Simulator en la escena ---");
                GameObject simulatorObj = null;

                // Buscar por componente o nombre
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name.IndexOf("Simulator", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        simulatorObj = root;
                        break;
                    }
                }

                if (simulatorObj == null)
                {
                    var allSimulators = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                        .Where(m => m.GetType().Name.IndexOf("Simulator", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                    if (allSimulators.Count > 0)
                    {
                        simulatorObj = allSimulators[0].gameObject;
                    }
                }

                if (simulatorObj != null)
                {
                    sb.AppendLine($"  ℹ️ Simulador ya existente en la escena: '{simulatorObj.name}'");
                    if (!simulatorObj.activeSelf)
                    {
                        simulatorObj.SetActive(true);
                        EditorUtility.SetDirty(simulatorObj);
                        sb.AppendLine("  ✔️ Simulador activado correctamente.");
                    }
                }
                else
                {
                    sb.AppendLine("  ⚠️ No se encontró el simulador en la jerarquía. Instanciando desde Prefab...");
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SIMULATOR_PREFAB_PATH);
                    if (prefab == null)
                    {
                        throw new Exception($"No se pudo cargar el prefab del simulador en {SIMULATOR_PREFAB_PATH}");
                    }

                    simulatorObj = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                    if (simulatorObj == null)
                    {
                        throw new Exception("Error al instanciar el prefab del simulador.");
                    }

                    simulatorObj.name = "XR Interaction Simulator";
                    simulatorObj.SetActive(true);
                    Undo.RegisterCreatedObjectUndo(simulatorObj, "Instantiate XR Interaction Simulator");
                    sb.AppendLine($"  ➕ Prefab instanciado exitosamente: '{simulatorObj.name}' y activo en la escena.");
                }

                // 3. VERIFICACIÓN DE REGRESIÓN: Car04 y componentes de movimiento
                sb.AppendLine("\n--- 3. Verificando Car04 y componentes de movimiento (Regresión) ---");
                GameObject car04 = null;
                foreach (var r in scene.GetRootGameObjects())
                {
                    if (r.name.Equals("Car04", StringComparison.OrdinalIgnoreCase) ||
                        r.name.StartsWith("Car04", StringComparison.OrdinalIgnoreCase))
                    {
                        car04 = r;
                        break;
                    }
                }

                if (car04 == null)
                {
                    throw new Exception("No se encontró el GameObject Car04 en la escena.");
                }
                sb.AppendLine($"  ✔️ Car04 presente en: {car04.transform.position}");

                // Rigidbody y Collider de Car04
                var rb = car04.GetComponent<Rigidbody>();
                if (rb == null) throw new Exception("Car04 no tiene Rigidbody");
                sb.AppendLine($"  ✔️ Rigidbody en Car04: isKinematic={rb.isKinematic}, useGravity={rb.useGravity}, interpolation={rb.interpolation}");

                var carCollider = car04.GetComponent<BoxCollider>();
                if (carCollider == null) throw new Exception("Car04 no tiene BoxCollider");
                sb.AppendLine($"  ✔️ BoxCollider en Car04: isTrigger={carCollider.isTrigger}, Center={carCollider.center}, Size={carCollider.size}");

                // VehicleController
                var vehicleCtrl = car04.GetComponent<VehicleController>();
                if (vehicleCtrl == null) throw new Exception("Car04 no tiene VehicleController");
                sb.AppendLine("  ✔️ VehicleController presente en Car04");

                // Volante
                var steeringWheel = car04.GetComponentInChildren<VRSteeringWheel>(true);
                if (steeringWheel == null) throw new Exception("No se encontró VRSteeringWheel en Car04");
                vehicleCtrl.SteeringWheel = steeringWheel;
                sb.AppendLine($"  ✔️ VRSteeringWheel conectado: '{steeringWheel.name}'");

                // Palanca de cambios (GearShifter)
                var gearShifter = car04.GetComponentInChildren<VRGearShifter>(true);
                if (gearShifter == null) throw new Exception("No se encontró VRGearShifter en Car04");
                vehicleCtrl.ConfigurePhysicsAndColliders(); // reconecta listeners y shifter
                sb.AppendLine($"  ✔️ VRGearShifter conectado: '{gearShifter.name}', Marcha actual: {gearShifter.currentGear}");

                // Botones StartButton y HazardButton
                var startBtn = car04.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.Equals("StartButton", StringComparison.OrdinalIgnoreCase));
                var hazardBtn = car04.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.Equals("HazardButton", StringComparison.OrdinalIgnoreCase));

                if (startBtn == null) throw new Exception("StartButton no encontrado");
                if (hazardBtn == null) throw new Exception("HazardButton no encontrado");

                var startTouch = startBtn.GetComponent<VRPhysicalTouchButton>();
                var startCol = startBtn.GetComponent<BoxCollider>();
                sb.AppendLine($"  ✔️ StartButton: VRPhysicalTouchButton={(startTouch != null ? "OK" : "FALTA")}, BoxCollider isTrigger={(startCol != null && startCol.isTrigger ? "OK" : "FALTA")}");

                var hazardTouch = hazardBtn.GetComponent<VRPhysicalTouchButton>();
                var hazardCol = hazardBtn.GetComponent<BoxCollider>();
                sb.AppendLine($"  ✔️ HazardButton: VRPhysicalTouchButton={(hazardTouch != null ? "OK" : "FALTA")}, BoxCollider isTrigger={(hazardCol != null && hazardCol.isTrigger ? "OK" : "FALTA")}");

                // Asiento del conductor y XR Origin
                var driverSeat = car04.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.Equals("DriverSeat", StringComparison.OrdinalIgnoreCase));
                if (driverSeat == null) throw new Exception("DriverSeat no encontrado");

                var xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
                if (xrOriginObj != null)
                {
                    var seatFollower = xrOriginObj.GetComponent<DriverSeatFollower>();
                    if (seatFollower != null)
                    {
                        sb.AppendLine($"  ✔️ DriverSeatFollower en '{xrOriginObj.name}' vinculado a: {(seatFollower.DriverSeat != null ? seatFollower.DriverSeat.name : "NULL")}");
                    }
                }

                // 4. Guardar escena DemoComponents
                sb.AppendLine("\n--- 4. Guardando escena DemoComponents ---");
                EditorUtility.SetDirty(car04);
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();

                sb.AppendLine($"  Escena guardada: {saved}");
                sb.AppendLine("\n✅ SETUP DEL SIMULADOR Y VERIFICACIÓN DE REGRESIÓN COMPLETADOS CON ÉXITO.");

                Debug.Log(sb.ToString());
                File.WriteAllText(FLAG_FILE, DateTime.Now.ToString("o"));
                File.WriteAllText(RESULT_FILE, sb.ToString());
            }
            catch (Exception ex)
            {
                sb.AppendLine($"\n❌ ERROR: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError(sb.ToString());
                File.WriteAllText(RESULT_FILE, sb.ToString());
            }
        }
    }
}
