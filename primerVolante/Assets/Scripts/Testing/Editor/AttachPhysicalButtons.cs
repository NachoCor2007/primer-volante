using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    [InitializeOnLoad]
    public static class AttachPhysicalButtons
    {
        private const string SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/DemoComponents.unity";
        private static readonly string FLAG_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "AttachPhysicalButtons_Done.flag");
        private static readonly string RESULT_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "AttachPhysicalButtons_Result.txt");

        static AttachPhysicalButtons()
        {
            EditorApplication.delayCall += OnEditorLoaded;
        }

        private static void OnEditorLoaded()
        {
            if (File.Exists(FLAG_FILE))
            {
                return;
            }

            Debug.Log("[AttachPhysicalButtons] 🚀 Executing automated button setup...");
            Execute();
        }

        [MenuItem("Tools/Primer Volante/Attach Physical Touch Buttons to Car04")]
        public static void MenuExecute()
        {
            Debug.Log("[AttachPhysicalButtons] 🔘 Menu item triggered.");
            Execute();
        }

        public static void Execute()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Attach Physical Touch Buttons Execution Log ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // 1. Abrir la escena DemoComponents.unity
                sb.AppendLine("\n--- 1. Abriendo escena DemoComponents.unity ---");
                Scene scene = EditorSceneManager.GetActiveScene();
                if (scene.path != SCENE_PATH)
                {
                    scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
                }

                if (!scene.IsValid())
                {
                    throw new Exception($"No se pudo abrir la escena en {SCENE_PATH}");
                }
                sb.AppendLine($"Escena activa: {scene.name} (Ruta: {scene.path})");

                // 2. Localizar GameObject car04
                sb.AppendLine("\n--- 2. Localizando car04 ---");
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
                sb.AppendLine($"Car04 encontrado: '{car04.name}'");

                // 3. Buscar StartButton y HazardButton
                sb.AppendLine("\n--- 3. Localizando StartButton y HazardButton ---");
                Transform startBtnTransform = car04.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.Equals("StartButton", StringComparison.OrdinalIgnoreCase));
                Transform hazardBtnTransform = car04.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.Equals("HazardButton", StringComparison.OrdinalIgnoreCase));

                if (startBtnTransform == null) throw new Exception("No se encontró StartButton dentro de Car04");
                if (hazardBtnTransform == null) throw new Exception("No se encontró HazardButton dentro de Car04");

                sb.AppendLine($"StartButton encontrado en: {startBtnTransform.name} (Ruta local: {AnimationUtility.CalculateTransformPath(startBtnTransform, car04.transform)})");
                sb.AppendLine($"HazardButton encontrado en: {hazardBtnTransform.name} (Ruta local: {AnimationUtility.CalculateTransformPath(hazardBtnTransform, car04.transform)})");

                // 4. Configurar StartButton
                sb.AppendLine("\n--- 4. Configurando StartButton ---");
                ConfigureButton(startBtnTransform.gameObject, sb);

                // 5. Configurar HazardButton
                sb.AppendLine("\n--- 5. Configurando HazardButton ---");
                ConfigureButton(hazardBtnTransform.gameObject, sb);

                // 6. Guardar escena DemoComponents
                sb.AppendLine("\n--- 6. Guardando escena DemoComponents ---");
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                sb.AppendLine($"Escena guardada: {saved}");
                sb.AppendLine("\n✅ CONFIGURACIÓN DE BOTONES COMPLETADA CON ÉXITO.");
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

        private static void ConfigureButton(GameObject btnObj, StringBuilder sb)
        {
            // BoxCollider
            BoxCollider col = btnObj.GetComponent<BoxCollider>();
            if (col == null)
            {
                col = btnObj.AddComponent<BoxCollider>();
                sb.AppendLine($"  ➕ BoxCollider agregado a {btnObj.name}");
            }
            else
            {
                sb.AppendLine($"  ℹ️ BoxCollider ya existente en {btnObj.name}");
            }

            col.isTrigger = true;
            sb.AppendLine($"  ✔️ isTrigger configurado en TRUE");

            // Ajustar tamaño del collider según el MeshFilter si está disponible
            MeshFilter mf = btnObj.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds;
                col.center = b.center;
                col.size = new Vector3(
                    Mathf.Max(b.size.x, 0.02f),
                    Mathf.Max(b.size.y, 0.02f),
                    Mathf.Max(b.size.z, 0.02f)
                );
                sb.AppendLine($"  📐 Bounds calculados desde mesh: Center={col.center}, Size={col.size}");
            }
            else
            {
                sb.AppendLine($"  📐 Colisionador actual: Center={col.center}, Size={col.size}");
            }

            // VRPhysicalTouchButton
            VRPhysicalTouchButton touchBtn = btnObj.GetComponent<VRPhysicalTouchButton>();
            if (touchBtn == null)
            {
                touchBtn = btnObj.AddComponent<VRPhysicalTouchButton>();
                sb.AppendLine($"  ➕ VRPhysicalTouchButton agregado a {btnObj.name}");
            }
            else
            {
                sb.AppendLine($"  ℹ️ VRPhysicalTouchButton ya existente en {btnObj.name}");
            }

            // Configuramos un offset local de hundimiento por defecto adecuado hacia el frente local del botón
            touchBtn.PressOffset = new Vector3(0f, 0f, 0.015f);
            touchBtn.PressDepth = 0.015f;
            sb.AppendLine($"  🔘 Offset de hundimiento: {touchBtn.PressOffset}, Profundidad: {touchBtn.PressDepth}");

            EditorUtility.SetDirty(btnObj);
        }
    }
}
