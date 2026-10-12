using System.Collections.Generic;
using System.IO;
using PrimerVolante.Environment;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente y automatizada para configurar la escena
    /// CityMerged_ScriptedEvents con eventos guionados por triggers (US31 / Issue #23).
    /// Clona de forma segura CityMerged.unity preservándola intacta y configura:
    /// - Caso 1: Peatón Sorpresivo que cruza senda peatonal con NPCSafetyStop.
    /// - Caso 2: Vehículo NPC en intersección con NPCSafetyStop.
    /// - Caso 3: Semáforo sincronizado con trigger.
    /// </summary>
    public static class ScriptedEventSceneSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged.unity";
        public const string TARGET_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged_ScriptedEvents.unity";

        public const string ROOT_GROUP_NAME = "Scripted_Events";

        public const string PEDESTRIAN_PREFAB_PATH = "Assets/Prefabs/NPCs/Pedestrians/NPC_Pedestrian_01.prefab";
        public const string VEHICLE_PREFAB_PATH = "Assets/Prefabs/NPCs/Vehicles/NPC_Car_color02.prefab";

        [MenuItem("Tools/Primer Volante/Setup Scripted Events Scene")]
        public static void SetupScene()
        {
            Debug.Log("[ScriptedEventSceneSetup] 🚦 Iniciando configuración de la escena de eventos guionados...");

            // 1. Asegurar la existencia de la escena clonada a partir de CityMerged.unity
            EnsureClonedSceneExists();

            // 2. Abrir la escena destino
            Scene scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[ScriptedEventSceneSetup] ❌ No se pudo abrir la escena destino: {TARGET_SCENE_PATH}");
                return;
            }

            if (scene.path == ORIGINAL_SCENE_PATH)
            {
                Debug.LogError($"[ScriptedEventSceneSetup] ❌ ABORTADO: La escena activa es la original ({ORIGINAL_SCENE_PATH}). ¡Debe permanecer intacta!");
                return;
            }

            // 3. Limpiar grupo anterior para idempotencia
            Transform existingGroup = FindRootGroup(scene);
            if (existingGroup != null)
            {
                Undo.DestroyObjectImmediate(existingGroup.gameObject);
                Debug.Log($"[ScriptedEventSceneSetup] ♻️ Limpiado grupo anterior '{ROOT_GROUP_NAME}' para reconfiguración limpia.");
            }

            GameObject rootObj = new GameObject(ROOT_GROUP_NAME);
            SceneManager.MoveGameObjectToScene(rootObj, scene);
            Undo.RegisterCreatedObjectUndo(rootObj, "Create Scripted Events Root");

            // 4. Configurar Caso 1: Peatón Sorpresivo en senda peatonal (Z = 108.5m)
            SetupCase1_PedestrianCrosswalk(rootObj.transform, scene, out NPCPathFollower pedestrianFollower);

            // 5. Configurar Caso 2: Vehículo NPC cruzando la bocacalle (Z = 120m / Z = 123.5m)
            SetupCase2_IntersectionVehicle(rootObj.transform, scene, out NPCPathFollower intersectionVehicleFollower);

            // 6. Configurar Caso 3: Semáforo sincronizado
            TrafficLightController syncedTrafficLight = SetupCase3_TrafficLightSync();

            // 7. Configurar Triggers en calzada para el jugador (carril X = 63.5m, avanzando hacia Z+)
            SetupTriggers(rootObj.transform, pedestrianFollower, intersectionVehicleFollower, syncedTrafficLight);

            // 8. Guardar escena
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ScriptedEventSceneSetup] ✅ ¡Escena '{TARGET_SCENE_PATH}' configurada y guardada con éxito!");
        }

        #region Creación y Validación de Escena

        private static void EnsureClonedSceneExists()
        {
            if (File.Exists(TARGET_SCENE_PATH))
            {
                Debug.Log($"[ScriptedEventSceneSetup] Escena destino ya existe en {TARGET_SCENE_PATH}.");
                return;
            }

            if (!File.Exists(ORIGINAL_SCENE_PATH))
            {
                Debug.LogError($"[ScriptedEventSceneSetup] ❌ No se encontró la escena original en {ORIGINAL_SCENE_PATH}.");
                return;
            }

            Debug.Log($"[ScriptedEventSceneSetup] 📋 Clonando {ORIGINAL_SCENE_PATH} -> {TARGET_SCENE_PATH}...");
            bool copied = AssetDatabase.CopyAsset(ORIGINAL_SCENE_PATH, TARGET_SCENE_PATH);
            AssetDatabase.Refresh();

            if (!copied)
            {
                Debug.LogError($"[ScriptedEventSceneSetup] ❌ Falló la clonación de la escena mediante AssetDatabase.");
            }
            else
            {
                Debug.Log($"[ScriptedEventSceneSetup] ✅ Escena clonada exitosamente.");
            }
        }

        private static Transform FindRootGroup(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == ROOT_GROUP_NAME)
                {
                    return roots[i].transform;
                }
            }
            return null;
        }

        #endregion

        #region Caso 1: Peatón Sorpresivo

        private static void SetupCase1_PedestrianCrosswalk(Transform parent, Scene scene, out NPCPathFollower follower)
        {
            follower = null;

            // Ruta de la senda peatonal a la altura de Z = 108.5:
            // Arranca en la vereda este (X=67.5, Y=0.2), baja a la calzada (X=65.5..54.5, Y=0.0) y sube a la vereda oeste (X=52.5, Y=0.2).
            GameObject routeObj = new GameObject("Route_Pedestrian_CrosswalkSurprise");
            routeObj.transform.SetParent(parent, false);

            SplineContainer splineContainer = routeObj.AddComponent<SplineContainer>();
            Spline spline = new Spline();

            spline.Add(new BezierKnot(new float3(67.5f, 0.20f, 108.5f)));
            spline.Add(new BezierKnot(new float3(65.8f, 0.00f, 108.5f)));
            spline.Add(new BezierKnot(new float3(54.2f, 0.00f, 108.5f)));
            spline.Add(new BezierKnot(new float3(52.5f, 0.20f, 108.5f)));
            splineContainer.Spline = spline;

            // Instanciar Prefab de Peatón
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PEDESTRIAN_PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"[ScriptedEventSceneSetup] ❌ Falta el prefab de peatón en {PEDESTRIAN_PREFAB_PATH}.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "NPC_Pedestrian_CrosswalkSurprise";
            instance.transform.SetParent(parent, false);

            follower = instance.GetComponent<NPCPathFollower>();
            if (follower == null) follower = instance.AddComponent<NPCPathFollower>();

            var so = new SerializedObject(follower);
            SetSerializedProperty(so, "m_Path", splineContainer);
            SetSerializedProperty(so, "m_SplineIndex", 0);
            SetSerializedProperty(so, "m_EndBehavior", (int)NPCPathEndBehavior.Stop);
            SetSerializedProperty(so, "m_InitialDistance", 0f);
            SetSerializedProperty(so, "m_PlayOnStart", false); // ¡Detenido en espera del trigger!
            SetSerializedProperty(so, "m_CruiseSpeed", 1.8f);
            SetSerializedProperty(so, "m_Acceleration", 2.0f);
            SetSerializedProperty(so, "m_Deceleration", 3.0f);
            so.ApplyModifiedPropertiesWithoutUndo();

            follower.SetPath(splineContainer, 0);

            // Agregar y configurar NPCSafetyStop
            NPCSafetyStop safety = instance.GetComponent<NPCSafetyStop>();
            if (safety == null) safety = instance.AddComponent<NPCSafetyStop>();

            var soSafety = new SerializedObject(safety);
            SetSerializedProperty(soSafety, "m_DetectionDistance", 4.0f);
            SetSerializedProperty(soSafety, "m_ResumeDelay", 0.6f);
            SetSerializedProperty(soSafety, "m_SensorOffset", new Vector3(0f, 1.0f, 0f));
            SetSerializedProperty(soSafety, "m_BoxHalfExtents", new Vector3(0.6f, 0.45f, 0.3f));
            SetSerializedProperty(soSafety, "m_DynamicDistanceWithSpeed", true);
            SetSerializedProperty(soSafety, "m_SpeedLookaheadFactor", 1.0f);
            soSafety.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[ScriptedEventSceneSetup] 🧍 Caso 1: Peatón sorpresivo en senda peatonal configurado.");
        }

        #endregion

        #region Caso 2: Vehículo NPC en Intersección

        private static void SetupCase2_IntersectionVehicle(Transform parent, Scene scene, out NPCPathFollower follower)
        {
            follower = null;

            // Ruta vehicular en la calle transversal de Z = 120 (carril norte Z = 123.5):
            // Cruza de Oeste a Este desde X = 45 hasta X = 75.
            GameObject routeObj = new GameObject("Route_Vehicle_IntersectionCross");
            routeObj.transform.SetParent(parent, false);

            SplineContainer splineContainer = routeObj.AddComponent<SplineContainer>();
            Spline spline = new Spline();

            spline.Add(new BezierKnot(new float3(45.0f, 0.00f, 123.5f)));
            spline.Add(new BezierKnot(new float3(75.0f, 0.00f, 123.5f)));
            splineContainer.Spline = spline;

            // Instanciar Prefab de Vehículo NPC
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VEHICLE_PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"[ScriptedEventSceneSetup] ❌ Falta el prefab de vehículo en {VEHICLE_PREFAB_PATH}.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "NPC_Vehicle_IntersectionCross";
            instance.transform.SetParent(parent, false);

            follower = instance.GetComponent<NPCPathFollower>();
            if (follower == null) follower = instance.AddComponent<NPCPathFollower>();

            var so = new SerializedObject(follower);
            SetSerializedProperty(so, "m_Path", splineContainer);
            SetSerializedProperty(so, "m_SplineIndex", 0);
            SetSerializedProperty(so, "m_EndBehavior", (int)NPCPathEndBehavior.Stop);
            SetSerializedProperty(so, "m_InitialDistance", 0f);
            SetSerializedProperty(so, "m_PlayOnStart", false); // ¡Detenido en espera del trigger!
            SetSerializedProperty(so, "m_CruiseSpeed", 6.5f);
            SetSerializedProperty(so, "m_Acceleration", 2.5f);
            SetSerializedProperty(so, "m_Deceleration", 4.0f);
            so.ApplyModifiedPropertiesWithoutUndo();

            follower.SetPath(splineContainer, 0);

            // Agregar y configurar NPCSafetyStop
            NPCSafetyStop safety = instance.GetComponent<NPCSafetyStop>();
            if (safety == null) safety = instance.AddComponent<NPCSafetyStop>();

            var soSafety = new SerializedObject(safety);
            SetSerializedProperty(soSafety, "m_DetectionDistance", 5.5f);
            SetSerializedProperty(soSafety, "m_ResumeDelay", 0.5f);
            SetSerializedProperty(soSafety, "m_SensorOffset", new Vector3(0f, 0.7f, 0f));
            SetSerializedProperty(soSafety, "m_BoxHalfExtents", new Vector3(0.9f, 0.6f, 0.2f));
            SetSerializedProperty(soSafety, "m_DynamicDistanceWithSpeed", true);
            SetSerializedProperty(soSafety, "m_SpeedLookaheadFactor", 1.0f);
            soSafety.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[ScriptedEventSceneSetup] 🚗 Caso 2: Vehículo NPC en intersección configurado.");
        }

        #endregion

        #region Caso 3: Sincronización con Semáforo

        private static TrafficLightController SetupCase3_TrafficLightSync()
        {
            // Buscar los semáforos de la intersección. Usar FindObjectsByType conforme a AGENTS.md
            TrafficLightController[] allLights = Object.FindObjectsByType<TrafficLightController>(FindObjectsSortMode.None);
            TrafficLightController playerLaneLight = null;

            for (int i = 0; i < allLights.Length; i++)
            {
                TrafficLightController light = allLights[i];
                if (light.name.Contains("PlayerLane") || Vector3.Distance(light.transform.position, new Vector3(67.62f, 0.2f, 112.51f)) < 6f)
                {
                    playerLaneLight = light;
                    break;
                }
            }

            if (playerLaneLight == null && allLights.Length > 0)
            {
                playerLaneLight = allLights[0];
            }

            if (playerLaneLight != null)
            {
                // Configurarlo para iniciar en espera/rojo hasta que el trigger lo sincronice
                var soLight = new SerializedObject(playerLaneLight);
                SetSerializedProperty(soLight, "m_AutoStart", false);
                SetSerializedProperty(soLight, "m_InitialState", (int)TrafficLightState.Red);
                soLight.ApplyModifiedPropertiesWithoutUndo();

                playerLaneLight.AutoStart = false;
                playerLaneLight.InitialState = TrafficLightState.Red;
                playerLaneLight.ResetCycle();

                Debug.Log($"[ScriptedEventSceneSetup] 🚦 Caso 3: Semáforo '{playerLaneLight.name}' sincronizado para inicio por trigger.");
            }
            else
            {
                Debug.LogWarning("[ScriptedEventSceneSetup] ⚠️ No se encontró TrafficLightController en la escena para sincronizar.");
            }

            return playerLaneLight;
        }

        #endregion

        #region Triggers en Calzada

        private static void SetupTriggers(Transform parent, NPCPathFollower pedestrianFollower, NPCPathFollower vehicleFollower, TrafficLightController trafficLight)
        {
            // Trigger 1: Activa peatón sorpresivo.
            // Ubicado en el carril del jugador (X = 63.5m) a la altura de Z = 101m (7.5m antes de la senda en Z=108.5m).
            GameObject trigger1Obj = new GameObject("Trigger_Scripted_PedestrianCrossing");
            trigger1Obj.transform.SetParent(parent, false);
            trigger1Obj.transform.position = new Vector3(63.5f, 1.5f, 101.0f);

            BoxCollider col1 = trigger1Obj.AddComponent<BoxCollider>();
            col1.isTrigger = true;
            col1.size = new Vector3(8.0f, 3.0f, 4.0f);

            TriggerScriptedEvent trigger1 = trigger1Obj.AddComponent<TriggerScriptedEvent>();
            trigger1.TriggerOnce = true;
            trigger1.Delay = 0.0f;
            trigger1.CheckVehicleController = true;
            if (pedestrianFollower != null)
            {
                trigger1.TargetNPCs.Add(pedestrianFollower);
            }

            // Trigger 2: Activa vehículo saliendo a la intersección y ciclo de semáforo.
            // Ubicado a la altura de Z = 111m (aproximación final a la intersección en Z=120m).
            GameObject trigger2Obj = new GameObject("Trigger_Scripted_IntersectionTraffic");
            trigger2Obj.transform.SetParent(parent, false);
            trigger2Obj.transform.position = new Vector3(63.5f, 1.5f, 111.0f);

            BoxCollider col2 = trigger2Obj.AddComponent<BoxCollider>();
            col2.isTrigger = true;
            col2.size = new Vector3(8.0f, 3.0f, 4.0f);

            TriggerScriptedEvent trigger2 = trigger2Obj.AddComponent<TriggerScriptedEvent>();
            trigger2.TriggerOnce = true;
            trigger2.Delay = 0.2f;
            trigger2.CheckVehicleController = true;

            if (vehicleFollower != null)
            {
                trigger2.TargetNPCs.Add(vehicleFollower);
            }

            if (trafficLight != null)
            {
                trigger2.TargetTrafficLights.Add(trafficLight);
                trigger2.StartTrafficLightCycle = true;
                trigger2.TrafficLightInitialState = TrafficLightState.Red;
            }

            Debug.Log("[ScriptedEventSceneSetup] 🎯 Triggers de calzada configurados (Z=101m y Z=111m).");
        }

        #endregion

        #region Helpers de Serialización

        private static void SetSerializedProperty(SerializedObject so, string propertyName, object value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[ScriptedEventSceneSetup] Propiedad '{propertyName}' no encontrada en {so.targetObject.GetType().Name}.");
                return;
            }

            if (value is bool b) prop.boolValue = b;
            else if (value is int i) prop.intValue = i;
            else if (value is float f) prop.floatValue = f;
            else if (value is string s) prop.stringValue = s;
            else if (value is Vector3 v) prop.vector3Value = v;
            else if (value is Object o) prop.objectReferenceValue = o;
        }

        #endregion
    }
}
