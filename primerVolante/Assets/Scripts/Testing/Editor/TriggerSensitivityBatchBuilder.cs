using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Editor
{
    public static class TriggerSensitivityBatchBuilder
    {
        public static void BuildSceneAndSave()
        {
            string scenePath = "Assets/Scenes/TestingScenes/TriggerSensitivity.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            
            TriggerSensitivityMonitor existingMonitor = Object.FindAnyObjectByType<TriggerSensitivityMonitor>();
            if (existingMonitor != null)
            {
                Object.DestroyImmediate(existingMonitor.gameObject);
            }

            GameObject screenObj = new GameObject("TriggerSensitivityScreen");
            screenObj.transform.position = new Vector3(0f, 1.4f, 2.0f);
            screenObj.transform.rotation = Quaternion.identity;

            TriggerSensitivityMonitor monitor = screenObj.AddComponent<TriggerSensitivityMonitor>();
            monitor.CreateWorldSpaceUI();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Primer Volante] Scene TriggerSensitivity.unity updated and saved successfully!");
        }
    }
}
