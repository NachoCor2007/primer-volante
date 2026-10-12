using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Configura de forma reproducible la escena de prueba CityWithCar_TrafficLights
    /// con una intersección controlada por semáforos y línea de detención para detección de infracciones.
    /// </summary>
    public static class TrafficLightSceneSetup
    {
        public const string TARGET_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityWithCar_TrafficLights.unity";
        public const string ORIGINAL_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityWithCar.unity";

        public const string BIG_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Signal_big.prefab";
        public const string SMALL_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Signal_small.prefab";

        [MenuItem("Tools/Primer Volante/Setup Traffic Light Test Scene")]
        public static void SetupScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path == ORIGINAL_SCENE_PATH)
            {
                Debug.LogError($"[TrafficLightSceneSetup] ❌ ABORTADO: La escena activa es la escena original ({ORIGINAL_SCENE_PATH}). ¡La escena original debe permanecer intacta!");
                return;
            }

            if (activeScene.path != TARGET_SCENE_PATH)
            {
                Debug.LogWarning($"[TrafficLightSceneSetup] ⚠️ Advertencia: La escena activa '{activeScene.path}' no es '{TARGET_SCENE_PATH}'. Asegúrese de estar en la escena de prueba.");
            }

            Debug.Log("[TrafficLightSceneSetup] 🚦 Iniciando configuración de intersección controlada en la escena...");

            // 1. Cargar los prefabs de semáforos
            GameObject bigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BIG_PREFAB_PATH);
            GameObject smallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SMALL_PREFAB_PATH);

            if (bigPrefab == null || smallPrefab == null)
            {
                Debug.LogError($"[TrafficLightSceneSetup] ❌ No se pudieron cargar los prefabs de semáforos desde '{BIG_PREFAB_PATH}' o '{SMALL_PREFAB_PATH}'.");
                return;
            }

            // 2. Eliminar semáforos antiguos sin configurar en la intersección (60, 0, 120) si existen
            string[] oldSignalsToRemove = { "Props_Traffic Signal_big (31)", "Props_Traffic Signal_big (32)", "Props_Traffic Signal_big (33)" };
            foreach (string signalName in oldSignalsToRemove)
            {
                GameObject oldSignal = GameObject.Find(signalName);
                if (oldSignal != null)
                {
                    Undo.DestroyObjectImmediate(oldSignal);
                    Debug.Log($"[TrafficLightSceneSetup] Eliminado semáforo anterior no agrupado: {signalName}");
                }
            }

            // 3. Crear o limpiar el contenedor principal de la intersección
            const string rootContainerName = "Controlled_Intersection_T";
            GameObject intersectionRoot = GameObject.Find(rootContainerName);
            if (intersectionRoot != null)
            {
                Undo.DestroyObjectImmediate(intersectionRoot);
            }

            intersectionRoot = new GameObject(rootContainerName);
            intersectionRoot.transform.position = new Vector3(60f, 0f, 120f);
            Undo.RegisterCreatedObjectUndo(intersectionRoot, "Create Controlled Intersection Root");

            // 4. Instanciar semáforo principal del carril del jugador (Props_Traffic Signal_big)
            // Ubicado en la acera derecha (X=67.62, Z=112.51) con el brazo extendido sobre el carril del jugador (X~63.5..65), mirando hacia el Sur (Rot Y=180)
            GameObject playerSignalBig = (GameObject)PrefabUtility.InstantiatePrefab(bigPrefab, activeScene);
            playerSignalBig.name = "TrafficSignal_PlayerLane_Big";
            playerSignalBig.transform.SetParent(intersectionRoot.transform, false);
            playerSignalBig.transform.position = new Vector3(67.62f, 0.20f, 112.51f);
            playerSignalBig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            TrafficLightController playerControllerBig = playerSignalBig.GetComponent<TrafficLightController>();
            ConfigureTrafficLightController(playerControllerBig, 8f, 2.5f, 8f, true, TrafficLightState.Red);

            // 5. Instanciar semáforo secundario a nivel de suelo en la línea de parada (Props_Traffic Signal_small)
            // Ubicado en la acera derecha a la altura de la línea de detención (X=67.62, Z=108.50)
            GameObject playerSignalSmall = (GameObject)PrefabUtility.InstantiatePrefab(smallPrefab, activeScene);
            playerSignalSmall.name = "TrafficSignal_PlayerLane_Small";
            playerSignalSmall.transform.SetParent(intersectionRoot.transform, false);
            playerSignalSmall.transform.position = new Vector3(67.62f, 0.20f, 108.50f);
            playerSignalSmall.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            TrafficLightController playerControllerSmall = playerSignalSmall.GetComponent<TrafficLightController>();
            ConfigureTrafficLightController(playerControllerSmall, 8f, 2.5f, 8f, true, TrafficLightState.Red);

            // 6. Instanciar semáforos para las calles transversales (Oeste y Este)
            // Semáforo Oeste (controla tráfico que viene desde el Oeste X<60 hacia el Este)
            GameObject westSignalBig = (GameObject)PrefabUtility.InstantiatePrefab(bigPrefab, activeScene);
            westSignalBig.name = "TrafficSignal_CrossStreet_West";
            westSignalBig.transform.SetParent(intersectionRoot.transform, false);
            westSignalBig.transform.position = new Vector3(52.42f, 0.20f, 112.51f);
            westSignalBig.transform.rotation = Quaternion.Euler(0f, 270f, 0f);

            TrafficLightController westController = westSignalBig.GetComponent<TrafficLightController>();
            ConfigureTrafficLightController(westController, 8f, 2.5f, 8f, true, TrafficLightState.Green);

            // Semáforo Este (controla tráfico que viene desde el Este X>60 hacia el Oeste)
            GameObject eastSignalBig = (GameObject)PrefabUtility.InstantiatePrefab(bigPrefab, activeScene);
            eastSignalBig.name = "TrafficSignal_CrossStreet_East";
            eastSignalBig.transform.SetParent(intersectionRoot.transform, false);
            eastSignalBig.transform.position = new Vector3(67.62f, 0.20f, 127.61f);
            eastSignalBig.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            TrafficLightController eastController = eastSignalBig.GetComponent<TrafficLightController>();
            ConfigureTrafficLightController(eastController, 8f, 2.5f, 8f, true, TrafficLightState.Green);

            // 7. Configurar la Línea de Detención (StopLine_Trigger) con BoxCollider Trigger y RedLightViolationDetector
            GameObject stopLineObj = new GameObject("StopLine_Trigger");
            stopLineObj.transform.SetParent(intersectionRoot.transform, false);
            stopLineObj.transform.position = new Vector3(63.60f, 0.50f, 108.50f);
            stopLineObj.transform.rotation = Quaternion.identity;

            BoxCollider stopCollider = stopLineObj.AddComponent<BoxCollider>();
            stopCollider.isTrigger = true;
            stopCollider.center = Vector3.zero;
            stopCollider.size = new Vector3(7.20f, 1.00f, 1.00f);

            RedLightViolationDetector detector = stopLineObj.AddComponent<RedLightViolationDetector>();
            detector.TrafficLight = playerControllerBig;
            detector.CooldownDuration = 3.0f;
            detector.CheckVehicleController = true;
            detector.VehicleTag = "";

            // 8. Agregar visualización de la línea de detención en el asfalto (StopLine_Visual)
            GameObject visualObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visualObj.name = "StopLine_Visual";
            visualObj.transform.SetParent(stopLineObj.transform, false);
            visualObj.transform.localPosition = new Vector3(0f, -0.48f, 0f); // A nivel de la superficie del asfalto (~Y=0.02)
            visualObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visualObj.transform.localScale = new Vector3(7.00f, 0.45f, 1.00f);

            // Remover el MeshCollider que crea CreatePrimitive por defecto para no interferir con la física
            Collider visualCollider = visualObj.GetComponent<Collider>();
            if (visualCollider != null)
            {
                Object.DestroyImmediate(visualCollider);
            }

            // Asignar material blanco o lit existente
            Material whiteMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Samples/XR Interaction Toolkit/3.0.7/Starter Assets/Materials/Lit White.mat");
            if (whiteMat != null)
            {
                MeshRenderer mr = visualObj.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = whiteMat;
            }

            // 9. Marcar la escena como modificada para asegurar persistencia
            EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log("[TrafficLightSceneSetup] ✅ Configuración de intersección y detector completada con éxito.");
        }

        private static void ConfigureTrafficLightController(
            TrafficLightController controller,
            float greenDur,
            float yellowDur,
            float redDur,
            bool autoStart,
            TrafficLightState initialState)
        {
            if (controller == null) return;

            SerializedObject so = new SerializedObject(controller);
            so.Update();

            SerializedProperty greenProp = so.FindProperty("m_GreenDuration");
            SerializedProperty yellowProp = so.FindProperty("m_YellowDuration");
            SerializedProperty redProp = so.FindProperty("m_RedDuration");
            SerializedProperty autoStartProp = so.FindProperty("m_AutoStart");
            SerializedProperty initProp = so.FindProperty("m_InitialState");

            if (greenProp != null) greenProp.floatValue = greenDur;
            if (yellowProp != null) yellowProp.floatValue = yellowDur;
            if (redProp != null) redProp.floatValue = redDur;
            if (autoStartProp != null) autoStartProp.boolValue = autoStart;
            if (initProp != null) initProp.enumValueIndex = (int)initialState;

            so.ApplyModifiedProperties();
            controller.UpdateVisuals();
        }
    }
}
