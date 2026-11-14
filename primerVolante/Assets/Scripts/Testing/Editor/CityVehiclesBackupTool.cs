using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta para respaldar todos los vehículos decorativos/NPC de City_Master
    /// en una escena dedicada (City_Vehicles_Backup.unity) y eliminarlos de City_Master,
    /// dejando la ciudad base 100% limpia y modular para cada lección.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class CityVehiclesBackupTool
    {
        public const string CITY_MASTER_PATH = "Assets/Scenes/City_Master.unity";
        public const string BACKUP_SCENE_PATH = "Assets/Scenes/City_Vehicles_Backup.unity";

        [MenuItem("Tools/Primer Volante/Environment/Backup and Clean City_Master Vehicles")]
        public static void BackupAndCleanVehicles()
        {
            string currentScenePath = SceneManager.GetActiveScene().path;

            // 1. Guardar la escena abierta actualmente
            EditorSceneManager.SaveOpenScenes();

            // 2. Abrir City_Master en modo Single
            Scene cityScene = EditorSceneManager.OpenScene(CITY_MASTER_PATH, OpenSceneMode.Single);

            // 3. Crear la escena de respaldo en modo Additive
            Scene backupScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            // 4. Identificar todos los vehículos de City_Master
            List<GameObject> vehiclesToMove = new List<GameObject>();
            foreach (GameObject rootGo in cityScene.GetRootGameObjects())
            {
                if (rootGo.name.StartsWith("Vehicle_"))
                {
                    vehiclesToMove.Add(rootGo);
                }
            }

            Debug.Log($"[CityVehiclesBackupTool] Encontrados {vehiclesToMove.Count} vehículos en City_Master.");

            // 5. Mover los vehículos a la escena de respaldo
            foreach (GameObject v in vehiclesToMove)
            {
                SceneManager.MoveGameObjectToScene(v, backupScene);
            }

            // 6. Guardar la escena de respaldo
            bool backupSaved = EditorSceneManager.SaveScene(backupScene, BACKUP_SCENE_PATH);
            Debug.Log($"[CityVehiclesBackupTool] Escena de respaldo guardada en '{BACKUP_SCENE_PATH}': {backupSaved}");

            // 7. Guardar City_Master limpia
            bool citySaved = EditorSceneManager.SaveScene(cityScene);
            Debug.Log($"[CityVehiclesBackupTool] City_Master guardada limpia (sin vehículos): {citySaved}");

            // 8. Reabrir la escena original del usuario
            if (!string.IsNullOrEmpty(currentScenePath))
            {
                EditorSceneManager.OpenScene(currentScenePath, OpenSceneMode.Single);
            }

            Debug.Log($"[CityVehiclesBackupTool] ✅ Operación completada: {vehiclesToMove.Count} vehículos respaldados en {BACKUP_SCENE_PATH} y removidos de City_Master.");
        }
    }
}

