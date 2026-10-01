using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Editor
{
    [InitializeOnLoad]
    public class TriggerSensitivitySetup
    {
        static TriggerSensitivitySetup()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private static void OnHierarchyChanged()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.name == "TriggerSensitivity")
            {
                EnsureMonitorInScene();
            }
        }

        [MenuItem("Tools/Primer Volante/Crear Pantalla Sensibilidad Gatillos")]
        public static void EnsureMonitorInScene()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.name != "TriggerSensitivity")
                return;

            TriggerSensitivityMonitor existingMonitor = Object.FindAnyObjectByType<TriggerSensitivityMonitor>();
            if (existingMonitor == null)
            {
                GameObject screenObj = new GameObject("TriggerSensitivityScreen");
                screenObj.transform.position = new Vector3(0f, 1.4f, 2.0f);
                screenObj.transform.rotation = Quaternion.identity;

                TriggerSensitivityMonitor monitor = screenObj.AddComponent<TriggerSensitivityMonitor>();
                monitor.CreateWorldSpaceUI();

                Undo.RegisterCreatedObjectUndo(screenObj, "Create Trigger Sensitivity Screen");
                EditorSceneManager.MarkSceneDirty(activeScene);
                Debug.Log("[Primer Volante] Pantalla de sensibilidad de gatillos creada en TriggerSensitivity.unity");
            }
        }
    }
}
