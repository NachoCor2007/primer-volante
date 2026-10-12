using System.IO;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente para configurar la escena CityMerged_TrafficSigns
    /// con el sistema de señalización reglamentaria vial argentina (R.14 velocidad máxima)
    /// y zonas de evaluación de velocidad (Issue #20 - US34).
    /// 
    /// Clona de forma segura CityMerged_ScriptedEvents.unity preservando intactas todas las escenas originales,
    /// e integra el cartel de velocidad máxima (40 km/h) y el trigger evaluador en la calle transversal
    /// a la derecha de la intersección en T del jugador.
    /// </summary>
    public static class TrafficSignSceneSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged_ScriptedEvents.unity";
        public const string TARGET_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged_TrafficSigns.unity";

        public const string ROOT_CONTAINER_NAME = "TrafficSigns_Regulatory";
        public const string PREFAB_PATH = "Assets/Prefabs/TrafficSigns/SpeedLimitSignWithEvaluationZone.prefab";

        public static readonly Vector3 SIGN_POSITION = new Vector3(72.0f, 0.20f, 114.0f);
        public static readonly Quaternion SIGN_ROTATION = Quaternion.Euler(0f, -90f, 0f);

        public static readonly Vector3 ZONE_LOCAL_CENTER = new Vector3(2.5f, 1.55f, -16.0f);
        public static readonly Vector3 ZONE_LOCAL_SIZE = new Vector3(7.0f, 3.5f, 30.0f);

        public const int SPEED_LIMIT_KMH = 40;
        public const float TOLERANCE_KMH = 3.0f;
        public const float ALERT_COOLDOWN = 1.5f;

        [MenuItem("Tools/Primer Volante/Setup Traffic Signs Scene")]
        public static void SetupScene()
        {
            Debug.Log("=========================================================");
            Debug.Log("🚦 CONFIGURANDO ESCENA CON SEÑALIZACIÓN VIAL (R.14 - 40 km/h)");
            Debug.Log("=========================================================");

            // 1. Asegurar clonación e integridad estricta de escenas originales
            EnsureClonedSceneExists();

            // 2. Abrir la escena destino
            Scene scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[TrafficSignSceneSetup] ❌ No se pudo abrir la escena destino: {TARGET_SCENE_PATH}");
                return;
            }

            // Regla estricta: proteger escenas originales
            if (scene.path == ORIGINAL_SCENE_PATH ||
                scene.path.EndsWith("CityMerged.unity") ||
                scene.path.EndsWith("CityWithCar.unity") ||
                scene.path.EndsWith("CityMerged_ScriptedEvents.unity"))
            {
                Debug.LogError($"[TrafficSignSceneSetup] ❌ ABORTADO: La escena activa es protegida ({scene.path}). ¡Debe permanecer 100% intacta!");
                return;
            }

            // 3. Limpiar contenedor previo para garantizar idempotencia total
            Transform existingContainer = FindRootContainer(scene);
            if (existingContainer != null)
            {
                Undo.DestroyObjectImmediate(existingContainer.gameObject);
                Debug.Log($"[TrafficSignSceneSetup] ♻️ Contenedor anterior '{ROOT_CONTAINER_NAME}' limpiado para reconstrucción limpia.");
            }

            // 4. Crear contenedor raíz
            GameObject rootContainer = new GameObject(ROOT_CONTAINER_NAME);
            SceneManager.MoveGameObjectToScene(rootContainer, scene);
            Undo.RegisterCreatedObjectUndo(rootContainer, "Create Traffic Signs Root");

            // 5. Cargar e instanciar el prefab reglamentario
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogWarning($"[TrafficSignSceneSetup] ⚠️ Prefab no encontrado en {PREFAB_PATH}. Generando assets reglamentarios...");
                prefab = TrafficSignSetup.CreateSignWithZonePrefab();
            }

            if (prefab == null)
            {
                Debug.LogError($"[TrafficSignSceneSetup] ❌ Error fatal: No se pudo obtener el prefab '{PREFAB_PATH}'.");
                return;
            }

            GameObject signInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            signInstance.name = "SpeedLimitSignWithEvaluationZone";
            signInstance.transform.SetParent(rootContainer.transform, false);

            // Posicionar y orientar el cartel en la acera derecha de la calle transversal (mirando hacia el Oeste)
            signInstance.transform.position = SIGN_POSITION;
            signInstance.transform.rotation = SIGN_ROTATION;
            signInstance.transform.localScale = Vector3.one;

            // 6. Configurar el display visual del cartel (R.14 - 40 km/h)
            var display = signInstance.GetComponent<SpeedLimitSignDisplay>();
            if (display != null)
            {
                var soDisplay = new SerializedObject(display);
                var propLimit = soDisplay.FindProperty("m_SpeedLimitKmh");
                if (propLimit != null) propLimit.intValue = SPEED_LIMIT_KMH;
                soDisplay.ApplyModifiedPropertiesWithoutUndo();

                display.SetSpeedLimit(SPEED_LIMIT_KMH);
                EditorUtility.SetDirty(display);
            }

            // 7. Configurar la zona de evaluación y monitoreo de velocidad
            var evaluator = signInstance.GetComponentInChildren<SpeedLimitEvaluator>(true);
            if (evaluator != null)
            {
                evaluator.SpeedLimitKmh = SPEED_LIMIT_KMH;
                evaluator.ToleranceKmh = TOLERANCE_KMH;
                evaluator.AlertCooldown = ALERT_COOLDOWN;
                evaluator.SignDisplay = display;

                var soEval = new SerializedObject(evaluator);
                var pLimit = soEval.FindProperty("m_SpeedLimitKmh");
                if (pLimit != null) pLimit.intValue = SPEED_LIMIT_KMH;
                var pTol = soEval.FindProperty("m_ToleranceKmh");
                if (pTol != null) pTol.floatValue = TOLERANCE_KMH;
                var pCool = soEval.FindProperty("m_AlertCooldown");
                if (pCool != null) pCool.floatValue = ALERT_COOLDOWN;
                var pSign = soEval.FindProperty("m_SignDisplay");
                if (pSign != null && display != null) pSign.objectReferenceValue = display;
                soEval.ApplyModifiedPropertiesWithoutUndo();

                var box = evaluator.GetComponent<BoxCollider>();
                if (box != null)
                {
                    box.isTrigger = true;
                    box.center = ZONE_LOCAL_CENTER;
                    box.size = ZONE_LOCAL_SIZE;

                    var soBox = new SerializedObject(box);
                    var pTrigger = soBox.FindProperty("m_IsTrigger");
                    if (pTrigger != null) pTrigger.boolValue = true;
                    var pCenter = soBox.FindProperty("m_Center");
                    if (pCenter != null) pCenter.vector3Value = ZONE_LOCAL_CENTER;
                    var pSize = soBox.FindProperty("m_Size");
                    if (pSize != null) pSize.vector3Value = ZONE_LOCAL_SIZE;
                    soBox.ApplyModifiedPropertiesWithoutUndo();

                    EditorUtility.SetDirty(box);
                }

                EditorUtility.SetDirty(evaluator);
            }
            else
            {
                Debug.LogError("[TrafficSignSceneSetup] ❌ No se encontró SpeedLimitEvaluator en el prefab instanciado.");
            }

            // 8. Guardar la escena y sincronizar AssetDatabase
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = signInstance;
            Debug.Log($"[TrafficSignSceneSetup] ✅ Escena '{TARGET_SCENE_PATH}' configurada y guardada con éxito.");
            Debug.Log($"[TrafficSignSceneSetup] 📍 Cartel R.14: Posición {SIGN_POSITION}, Rotación {SIGN_ROTATION.eulerAngles}");
            Debug.Log($"[TrafficSignSceneSetup] 📏 Zona Evaluadora: Longitud {ZONE_LOCAL_SIZE.z}m, Ancho {ZONE_LOCAL_SIZE.x}m, Límite {SPEED_LIMIT_KMH} km/h (Tol {TOLERANCE_KMH} km/h)");
            Debug.Log("=========================================================");
        }

        private static void EnsureClonedSceneExists()
        {
            if (File.Exists(TARGET_SCENE_PATH))
            {
                Debug.Log($"[TrafficSignSceneSetup] Escena destino ya existe en {TARGET_SCENE_PATH}.");
                return;
            }

            if (!File.Exists(ORIGINAL_SCENE_PATH))
            {
                Debug.LogError($"[TrafficSignSceneSetup] ❌ No se encontró la escena original en {ORIGINAL_SCENE_PATH}.");
                return;
            }

            Debug.Log($"[TrafficSignSceneSetup] 📋 Clonando escena {ORIGINAL_SCENE_PATH} -> {TARGET_SCENE_PATH}...");
            bool copied = AssetDatabase.CopyAsset(ORIGINAL_SCENE_PATH, TARGET_SCENE_PATH);
            AssetDatabase.Refresh();

            if (!copied)
            {
                Debug.LogError($"[TrafficSignSceneSetup] ❌ Falló la clonación de la escena mediante AssetDatabase.");
            }
            else
            {
                Debug.Log($"[TrafficSignSceneSetup] ✅ Escena clonada exitosamente.");
            }
        }

        private static Transform FindRootContainer(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == ROOT_CONTAINER_NAME)
                {
                    return roots[i].transform;
                }
            }
            return null;
        }
    }
}
