using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para generar bloqueos viales y delimitadores contextuales en Circuit_01:
    /// - Crea bloqueos combinados (Patrullero + Vallas, Patrullero + Conos, Vallas + Conos, Autos civiles con balizas).
    /// - Cierra todas las salidas erróneas e intersecciones fuera del trazado oficial del GPS.
    /// - Agrega ObstacleCollisionDetector en todos los vehículos y conos para integrarse al sistema de infracciones.
    /// - Es 100% idempotente y mantiene todos los objetos en la escena Circuit_01 sin tocar City_Master.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Circuit01BlockadeSetup
    {
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Circuit_01.unity";
        public const string ROOT_NAME = "Street_Blockades";

        public const string BARRIER_PREFAB_PATH = "Assets/Prefabs/Environment/LessonBarrier.prefab";
        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";
        public const string POLICE_CAR_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels/Vehicle_Police Car.prefab";
        public const string CIVIL_CAR_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels/Vehicle_Car_color01.prefab";
        public const string CIVIL_SUV_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels/Vehicle_SUV_color01.prefab";

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Street Blockades")]
        public static void SetupStreetBlockades()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚧 [Circuit01BlockadeSetup] Configurando bloqueos y delimitadores viales en Circuit_01...");
            Debug.Log("=================================================================");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "Circuit_01" && !scene.path.Contains("Circuit_01"))
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Circuit01BlockadeSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // Cargar prefabs requeridos
            GameObject barrierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BARRIER_PREFAB_PATH);
            GameObject conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);
            GameObject policePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(POLICE_CAR_PREFAB_PATH);
            GameObject carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CIVIL_CAR_PREFAB_PATH);
            GameObject suvPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CIVIL_SUV_PREFAB_PATH);

            if (barrierPrefab == null || conePrefab == null || policePrefab == null)
            {
                Debug.LogError("[Circuit01BlockadeSetup] ❌ Faltan prefabs requeridos en el proyecto.");
                return;
            }

            // Contenedor raíz
            GameObject root = GameObject.Find(ROOT_NAME);
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
            }

            root = new GameObject(ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Street Blockades Root");
            SceneManager.MoveGameObjectToScene(root, scene);

            int totalObstacles = 0;

            // 1. Salida del Estacionamiento a Avenida X=240 hacia el Norte (+Z)
            // Composición: Patrullero policial + Vallas dobles + Conos de advertencia
            totalObstacles += BuildBlockade(root.transform, "Blockade_01_Avenue240_North", new Vector3(240f, 0f, 95f),
                policePrefab, new Vector3(-3.5f, 0f, 0.5f), Quaternion.Euler(0f, 25f, 0f),
                barrierPrefab, new[] { new Vector3(1.0f, 0f, 0f), new Vector3(4.5f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-5.5f, 0f, -1.8f), new Vector3(-2.5f, 0f, -1.8f), new Vector3(1.0f, 0f, -1.8f), new Vector3(4.5f, 0f, -1.8f) });

            // 2. Esquina 1 (X=240, Z=0): Hacia el Sur (-Z)
            // Composición: Vallas dobles con Conos de obra
            totalObstacles += BuildBlockade(root.transform, "Blockade_02_Avenue240_CornerSouth", new Vector3(236.5f, 0f, -6f),
                null, Vector3.zero, Quaternion.identity,
                barrierPrefab, new[] { new Vector3(-1.8f, 0f, 0f), new Vector3(1.8f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-2.8f, 0f, 2.0f), new Vector3(0f, 0f, 2.2f), new Vector3(2.8f, 0f, 2.0f) });

            // 3. Esquina 2 (X=120, Z=0): Continuando derecho hacia el Oeste (-X)
            // Composición: Patrullero policial + Conos + Valla
            totalObstacles += BuildBlockade(root.transform, "Blockade_03_Street0_West", new Vector3(111f, 0f, 0f),
                policePrefab, new Vector3(0f, 0f, -1.8f), Quaternion.Euler(0f, 115f, 0f),
                barrierPrefab, new[] { new Vector3(0f, 0f, 2.0f) }, Quaternion.Euler(0f, 90f, 0f),
                conePrefab, new[] { new Vector3(2.5f, 0f, -2.5f), new Vector3(2.5f, 0f, 0f), new Vector3(2.5f, 0f, 2.5f) });

            // 4A. Cruce 3 (X=120, Z=60): Continuando derecho hacia el Norte (+Z)
            // Composición: Vallas dobles con Conos en extremos
            totalObstacles += BuildBlockade(root.transform, "Blockade_04A_Street120_North", new Vector3(120f, 0f, 69f),
                null, Vector3.zero, Quaternion.identity,
                barrierPrefab, new[] { new Vector3(-2.0f, 0f, 0f), new Vector3(1.8f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-3.8f, 0f, -0.6f), new Vector3(0f, 0f, -1.5f), new Vector3(3.8f, 0f, -0.6f) });

            // 4B. Cruce 3 (X=120, Z=60): Doblando a la derecha hacia el Este (+X)
            // Composición: Auto civil averiado + Valla + Conos
            totalObstacles += BuildBlockade(root.transform, "Blockade_04B_Street60_East", new Vector3(129f, 0f, 60f),
                carPrefab, new Vector3(0.5f, 0f, -2.0f), Quaternion.Euler(0f, 85f, 0f),
                barrierPrefab, new[] { new Vector3(0f, 0f, 2.0f) }, Quaternion.Euler(0f, 90f, 0f),
                conePrefab, new[] { new Vector3(-2.2f, 0f, -2.5f), new Vector3(-2.2f, 0f, 0f), new Vector3(-2.2f, 0f, 2.5f) });

            // 5A. Esquina 4 (X=0, Z=60): Doblando a la izquierda hacia el Sur (-Z)
            // Composición: Patrullero policial + Vallas dobles + Conos
            totalObstacles += BuildBlockade(root.transform, "Blockade_05A_Avenue0_South", new Vector3(0f, 0f, 51f),
                policePrefab, new Vector3(-3.5f, 0f, 0.5f), Quaternion.Euler(0f, 30f, 0f),
                barrierPrefab, new[] { new Vector3(1.0f, 0f, 0f), new Vector3(4.5f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-5.5f, 0f, 2.0f), new Vector3(-1.5f, 0f, 2.0f), new Vector3(1.5f, 0f, 2.0f), new Vector3(4.5f, 0f, 2.0f) });

            // 5B. Esquina 4 (X=0, Z=60): Continuando al Oeste (-X)
            // Composición: Vallas con Conos
            totalObstacles += BuildBlockade(root.transform, "Blockade_05B_Street60_West", new Vector3(-9f, 0f, 60f),
                null, Vector3.zero, Quaternion.identity,
                barrierPrefab, new[] { new Vector3(0f, 0f, -1.8f), new Vector3(0f, 0f, 1.8f) }, Quaternion.Euler(0f, 90f, 0f),
                conePrefab, new[] { new Vector3(1.8f, 0f, -2.5f), new Vector3(1.8f, 0f, 0f), new Vector3(1.8f, 0f, 2.5f) });

            // 6. Esquina 5 (X=0, Z=160): Continuando derecho hacia el Norte (+Z)
            // Composición: Vallas triples de obra con Conos frontales
            totalObstacles += BuildBlockade(root.transform, "Blockade_06_Avenue0_North", new Vector3(0f, 0f, 169f),
                null, Vector3.zero, Quaternion.identity,
                barrierPrefab, new[] { new Vector3(-3.8f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(3.8f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-5.0f, 0f, -2.0f), new Vector3(-1.8f, 0f, -2.0f), new Vector3(1.8f, 0f, -2.0f), new Vector3(5.0f, 0f, -2.0f) });

            // 7A. Cruce 6 (X=120, Z=160): Continuando derecho hacia el Este (+X)
            // Composición: Patrullero policial + Valla + Conos
            totalObstacles += BuildBlockade(root.transform, "Blockade_07A_Street160_East", new Vector3(129f, 0f, 160f),
                policePrefab, new Vector3(0.5f, 0f, -1.8f), Quaternion.Euler(0f, 75f, 0f),
                barrierPrefab, new[] { new Vector3(0f, 0f, 2.0f) }, Quaternion.Euler(0f, 90f, 0f),
                conePrefab, new[] { new Vector3(-2.2f, 0f, -2.5f), new Vector3(-2.2f, 0f, 0f), new Vector3(-2.2f, 0f, 2.5f) });

            // 7B. Cruce 6 (X=120, Z=160): Doblando a la derecha hacia el Sur (-Z)
            // Composición: Camioneta SUV averiada + Valla + Conos
            totalObstacles += BuildBlockade(root.transform, "Blockade_07B_Street120_South", new Vector3(120f, 0f, 151f),
                suvPrefab, new Vector3(-1.8f, 0f, -0.5f), Quaternion.Euler(0f, 20f, 0f),
                barrierPrefab, new[] { new Vector3(1.8f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-2.5f, 0f, 1.8f), new Vector3(0f, 0f, 1.8f), new Vector3(2.5f, 0f, 1.8f) });

            // 8. Meta / Llegada (X=120, Z=200): Fin de calle hacia el Norte (+Z)
            // Composición: Vallas dobles de fin de trayecto con Conos
            totalObstacles += BuildBlockade(root.transform, "Blockade_08_Finish_North", new Vector3(120f, 0f, 208f),
                null, Vector3.zero, Quaternion.identity,
                barrierPrefab, new[] { new Vector3(-1.8f, 0f, 0f), new Vector3(1.8f, 0f, 0f) }, Quaternion.identity,
                conePrefab, new[] { new Vector3(-2.8f, 0f, -1.8f), new Vector3(0f, 0f, -1.8f), new Vector3(2.8f, 0f, -1.8f) });

            EditorSceneManager.MarkSceneDirty(scene);

            Selection.activeGameObject = root;
            Debug.Log($"[Circuit01BlockadeSetup] ✅ ¡Bloqueos viales configurados con éxito! Total de obstáculos instalados: {totalObstacles}.");
        }

        private static int BuildBlockade(Transform parent, string groupName, Vector3 center,
            GameObject vehiclePrefab, Vector3 vehicleLocalOffset, Quaternion vehicleLocalRot,
            GameObject barrierPrefab, Vector3[] barrierOffsets, Quaternion barrierRot,
            GameObject conePrefab, Vector3[] coneOffsets)
        {
            GameObject group = new GameObject(groupName);
            group.transform.SetParent(parent, false);
            group.transform.position = center;
            group.transform.rotation = Quaternion.identity;

            int count = 0;

            // 1. Vehículo (si aplica)
            if (vehiclePrefab != null)
            {
                GameObject vehicle = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab, group.transform);
                vehicle.name = "Blocking_" + vehiclePrefab.name;
                vehicle.transform.localPosition = vehicleLocalOffset;
                vehicle.transform.localRotation = vehicleLocalRot;

                // Asegurar collider
                BoxCollider box = vehicle.GetComponent<BoxCollider>();
                if (box == null) box = vehicle.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.75f, 0f);
                box.size = new Vector3(1.9f, 1.5f, 4.4f);

                // Asegurar detector de colisión para el sistema de infracciones
                ObstacleCollisionDetector detector = vehicle.GetComponent<ObstacleCollisionDetector>();
                if (detector == null) detector = vehicle.AddComponent<ObstacleCollisionDetector>();
                var so = new SerializedObject(detector);
                so.FindProperty("m_Kind").enumValueIndex = (int)ObstacleKind.Vehicle;
                so.ApplyModifiedProperties();

                count++;
            }

            // 2. Vallas
            if (barrierPrefab != null && barrierOffsets != null)
            {
                for (int i = 0; i < barrierOffsets.Length; i++)
                {
                    GameObject barrier = (GameObject)PrefabUtility.InstantiatePrefab(barrierPrefab, group.transform);
                    barrier.name = $"Barrier_{i}";
                    barrier.transform.localPosition = barrierOffsets[i];
                    barrier.transform.localRotation = barrierRot;

                    ObstacleCollisionDetector detector = barrier.GetComponent<ObstacleCollisionDetector>();
                    if (detector == null) detector = barrier.AddComponent<ObstacleCollisionDetector>();
                    var so = new SerializedObject(detector);
                    so.FindProperty("m_Kind").enumValueIndex = (int)ObstacleKind.Cone;
                    so.ApplyModifiedProperties();

                    count++;
                }
            }

            // 3. Conos
            if (conePrefab != null && coneOffsets != null)
            {
                for (int i = 0; i < coneOffsets.Length; i++)
                {
                    GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, group.transform);
                    cone.name = $"Cone_{i}";
                    cone.transform.localPosition = coneOffsets[i];
                    cone.transform.localRotation = Quaternion.identity;

                    Collider col = cone.GetComponent<Collider>();
                    if (col == null)
                    {
                        var box = cone.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, 0.4f, 0f);
                        box.size = new Vector3(0.45f, 0.8f, 0.45f);
                    }

                    ObstacleCollisionDetector detector = cone.GetComponent<ObstacleCollisionDetector>();
                    if (detector == null) detector = cone.AddComponent<ObstacleCollisionDetector>();
                    var so = new SerializedObject(detector);
                    so.FindProperty("m_Kind").enumValueIndex = (int)ObstacleKind.Cone;
                    so.ApplyModifiedProperties();

                    count++;
                }
            }

            return count;
        }

        [MenuItem("Tools/Primer Volante/Circuit 01/Clear Street Blockades")]
        public static void ClearStreetBlockades()
        {
            GameObject root = GameObject.Find(ROOT_NAME);
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
                Scene scene = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Circuit01BlockadeSetup] 🧹 Bloqueos viales eliminados de la escena.");
            }
        }
    }
}
