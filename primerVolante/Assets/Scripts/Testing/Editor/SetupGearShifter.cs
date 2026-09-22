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
    public static class SetupGearShifter
    {
        private const string SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/DemoComponents.unity";
        private static readonly string FLAG_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "SetupGearShifter_Done.flag");
        private static readonly string RESULT_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "SetupGearShifter_Result.txt");

        static SetupGearShifter()
        {
            EditorApplication.delayCall += OnEditorLoaded;
        }

        private static void OnEditorLoaded()
        {
            if (File.Exists(FLAG_FILE))
            {
                return;
            }

            Debug.Log("[SetupGearShifter] 🚀 Auto-executing gear shifter setup...");
            Execute();
        }

        [MenuItem("Tools/Primer Volante/Setup GearShifter in Car04")]
        public static void MenuExecute()
        {
            Debug.Log("[SetupGearShifter] 🔘 Menu triggered.");
            Execute();
        }

        public static void Execute()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Setup GearShifter Execution Log ===");
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
                    throw new Exception($"No se pudo abrir la escena {SCENE_PATH}");
                }
                sb.AppendLine($"Escena activa: {scene.name}");

                // 2. Localizar Car04
                sb.AppendLine("\n--- 2. Localizando Car04 ---");
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
                    throw new Exception("No se encontró Car04 en la jerarquía.");
                }
                sb.AppendLine($"Car04 encontrado: {car04.name}");

                // 3. Localizar GearShifter (o GearShigter)
                sb.AppendLine("\n--- 3. Localizando GearShifter / GearShigter en Car04 ---");
                Transform shifterTransform = car04.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.IndexOf("GearShifter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         t.name.IndexOf("GearShigter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         t.name.IndexOf("Shifter", StringComparison.OrdinalIgnoreCase) >= 0);

                if (shifterTransform == null)
                {
                    throw new Exception("No se encontró ningún objeto de palanca (GearShifter / GearShigter) dentro de Car04.");
                }

                GameObject shifterObj = shifterTransform.gameObject;
                sb.AppendLine($"Palanca encontrada: '{shifterObj.name}' (Ruta: {AnimationUtility.CalculateTransformPath(shifterTransform, car04.transform)})");

                // 4. Configurar Rigidbody
                sb.AppendLine("\n--- 4. Configurando Rigidbody cinemático ---");
                Rigidbody rb = shifterObj.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = shifterObj.AddComponent<Rigidbody>();
                    sb.AppendLine("  ➕ Rigidbody agregado");
                }
                else
                {
                    sb.AppendLine("  ℹ️ Rigidbody ya existente");
                }
                rb.isKinematic = true;
                rb.useGravity = false;

                // 5. Configurar Collider
                sb.AppendLine("\n--- 5. Configurando Collider ---");
                BoxCollider col = shifterObj.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = shifterObj.AddComponent<BoxCollider>();
                    sb.AppendLine("  ➕ BoxCollider agregado");
                }
                else
                {
                    sb.AppendLine("  ℹ️ BoxCollider ya existente");
                }

                MeshFilter mf = shifterObj.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds b = mf.sharedMesh.bounds;
                    col.center = b.center;
                    col.size = new Vector3(
                        Mathf.Max(b.size.x, 0.06f),
                        Mathf.Max(b.size.y, 0.12f),
                        Mathf.Max(b.size.z, 0.06f)
                    );
                    sb.AppendLine($"  📐 BoxCollider ajustado a bounds de la palanca: Center={col.center}, Size={col.size}");
                }
                else
                {
                    col.size = new Vector3(0.08f, 0.15f, 0.08f);
                    col.center = new Vector3(0f, 0.075f, 0f);
                    sb.AppendLine($"  📐 BoxCollider configurado por defecto: Center={col.center}, Size={col.size}");
                }
                col.isTrigger = false; // El agarre continuo de XRI funciona con colliders sólidos

                // 6. Configurar XRGrabInteractable
                sb.AppendLine("\n--- 6. Configurando XRGrabInteractable ---");
                XRGrabInteractable grab = shifterObj.GetComponent<XRGrabInteractable>();
                if (grab == null)
                {
                    grab = shifterObj.AddComponent<XRGrabInteractable>();
                    sb.AppendLine("  ➕ XRGrabInteractable agregado");
                }
                else
                {
                    sb.AppendLine("  ℹ️ XRGrabInteractable ya existente");
                }
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;

                // 7. Configurar VRGearShifter
                sb.AppendLine("\n--- 7. Configurando VRGearShifter ---");
                VRGearShifter gearShifter = shifterObj.GetComponent<VRGearShifter>();
                if (gearShifter == null)
                {
                    gearShifter = shifterObj.AddComponent<VRGearShifter>();
                    sb.AppendLine("  ➕ VRGearShifter agregado");
                }
                else
                {
                    sb.AppendLine("  ℹ️ VRGearShifter ya existente");
                }

                gearShifter.currentGear = GearState.P;
                gearShifter.rotationAxis = Vector3.right;
                gearShifter.forwardLimit = 35f;
                gearShifter.backwardLimit = -30f;
                gearShifter.snapThreshold = 6f;
                gearShifter.snapSpeed = 12f;

                sb.AppendLine($"  ⚙️ Límites configurados: P (Adelante)={gearShifter.forwardLimit}°, D (Atrás)={gearShifter.backwardLimit}°");
                sb.AppendLine($"  ⚙️ Puntos calculados: P={gearShifter.GetAngleForGear(GearState.P):F1}°, R={gearShifter.GetAngleForGear(GearState.R):F1}°, N={gearShifter.GetAngleForGear(GearState.N):F1}°, D={gearShifter.GetAngleForGear(GearState.D):F1}°");

                // 8. Vincular con VehicleController si existe
                VehicleController vc = car04.GetComponent<VehicleController>();
                if (vc != null)
                {
                    SerializedObject so = new SerializedObject(vc);
                    SerializedProperty sp = so.FindProperty("m_GearShifter");
                    if (sp != null)
                    {
                        sp.objectReferenceValue = gearShifter;
                        so.ApplyModifiedProperties();
                        sb.AppendLine("  🔗 Palanca vinculada a VehicleController.m_GearShifter");
                    }
                }

                EditorUtility.SetDirty(shifterObj);

                // 9. Guardar escena
                sb.AppendLine("\n--- 8. Guardando escena DemoComponents ---");
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                sb.AppendLine($"Escena guardada: {saved}");
                sb.AppendLine("\n✅ CONFIGURACIÓN DE GEARSHIFTER COMPLETADA EXITOSAMENTE.");
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
