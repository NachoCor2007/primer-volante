using PrimerVolante.Environment;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para configurar los prefabs
    /// de semáforos (Props_Traffic Signal_big y Props_Traffic Signal_small) con
    /// componentes Light (Point Lights) y TrafficLightController cableado.
    /// </summary>
    public static class TrafficLightPrefabSetup
    {
        public const string BIG_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Signal_big.prefab";
        public const string SMALL_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Signal_small.prefab";

        // Coordenadas calculadas exactamente a partir de los centros de los focos en las mallas 3D
        // Para Traffic Signal Small (distribución vertical en poste):
        private static readonly Vector3 s_SmallRedPos = new Vector3(0.00f, 5.38f, 0.22f);
        private static readonly Vector3 s_SmallYellowPos = new Vector3(0.00f, 4.94f, 0.22f);
        private static readonly Vector3 s_SmallGreenPos = new Vector3(0.00f, 4.50f, 0.22f);

        // Para Traffic Signal Big (distribución horizontal en brazo):
        private static readonly Vector3 s_BigRedPos = new Vector3(2.23f, 5.28f, 0.25f);
        private static readonly Vector3 s_BigYellowPos = new Vector3(1.80f, 5.28f, 0.25f);
        private static readonly Vector3 s_BigGreenPos = new Vector3(1.35f, 5.28f, 0.25f);

        // Parámetros de iluminación
        private static readonly Color s_RedColor = new Color(1.0f, 0.1f, 0.1f, 1.0f);       // #FF1A1A
        private static readonly Color s_YellowColor = new Color(1.0f, 0.72f, 0.05f, 1.0f);   // #FFB80D
        private static readonly Color s_GreenColor = new Color(0.18f, 0.85f, 0.44f, 1.0f);   // #2ECC71
        private const float k_LightRange = 5.0f;
        private const float k_LightIntensity = 3.0f;

        [MenuItem("Tools/Primer Volante/Setup Traffic Light Prefabs")]
        public static string SetupAllPrefabs()
        {
            Debug.Log("[TrafficLightPrefabSetup] 🚦 Iniciando configuración de Prefabs de Semáforos...");
            string r1 = ConfigurePrefab(SMALL_PREFAB_PATH, s_SmallRedPos, s_SmallYellowPos, s_SmallGreenPos);
            string r2 = ConfigurePrefab(BIG_PREFAB_PATH, s_BigRedPos, s_BigYellowPos, s_BigGreenPos);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string result = $"{r1}\n{r2}";
            Debug.Log($"[TrafficLightPrefabSetup] ✅ Configuración finalizada:\n{result}");
            return result;
        }

        private static string ConfigurePrefab(string prefabPath, Vector3 redPos, Vector3 yellowPos, Vector3 greenPos)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                string err = $"Error: No se pudo cargar el prefab en {prefabPath}";
                Debug.LogError(err);
                return err;
            }

            try
            {
                // 1. Obtener o agregar TrafficLightController
                TrafficLightController controller = prefabRoot.GetComponent<TrafficLightController>();
                if (controller == null)
                {
                    controller = prefabRoot.AddComponent<TrafficLightController>();
                }

                // 2. Crear o actualizar objetos hijos de luces
                Light redLight = EnsureLightChild(prefabRoot.transform, "Light_Red", redPos, s_RedColor, true);
                Light yellowLight = EnsureLightChild(prefabRoot.transform, "Light_Yellow", yellowPos, s_YellowColor, false);
                Light greenLight = EnsureLightChild(prefabRoot.transform, "Light_Green", greenPos, s_GreenColor, false);

                // 3. Serializar referencias en el controlador
                SerializedObject serializedObj = new SerializedObject(controller);
                serializedObj.Update();

                SerializedProperty redProp = serializedObj.FindProperty("m_RedLight");
                SerializedProperty yellowProp = serializedObj.FindProperty("m_YellowLight");
                SerializedProperty greenProp = serializedObj.FindProperty("m_GreenLight");

                if (redProp != null) redProp.objectReferenceValue = redLight;
                if (yellowProp != null) yellowProp.objectReferenceValue = yellowLight;
                if (greenProp != null) greenProp.objectReferenceValue = greenLight;

                SerializedProperty redDurProp = serializedObj.FindProperty("m_RedDuration");
                SerializedProperty yellowDurProp = serializedObj.FindProperty("m_YellowDuration");
                SerializedProperty greenDurProp = serializedObj.FindProperty("m_GreenDuration");
                if (redDurProp != null && redDurProp.floatValue <= 0.01f) redDurProp.floatValue = 8f;
                if (yellowDurProp != null && yellowDurProp.floatValue <= 0.01f) yellowDurProp.floatValue = 2.5f;
                if (greenDurProp != null && greenDurProp.floatValue <= 0.01f) greenDurProp.floatValue = 8f;

                serializedObj.ApplyModifiedProperties();

                // 4. Guardar prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return $"Configurado exitosamente: {prefabPath}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Light EnsureLightChild(Transform parent, string childName, Vector3 localPos, Color color, bool initialEnabled)
        {
            Transform existingChild = parent.Find(childName);
            GameObject lightObj;

            if (existingChild != null)
            {
                lightObj = existingChild.gameObject;
            }
            else
            {
                lightObj = new GameObject(childName);
                lightObj.transform.SetParent(parent, false);
            }

            lightObj.transform.localPosition = localPos;
            lightObj.transform.localRotation = Quaternion.identity;
            lightObj.transform.localScale = Vector3.one;

            Light lightComp = lightObj.GetComponent<Light>();
            if (lightComp == null)
            {
                lightComp = lightObj.AddComponent<Light>();
            }

            lightComp.type = LightType.Point;
            lightComp.color = color;
            lightComp.range = k_LightRange;
            lightComp.intensity = k_LightIntensity;
            lightComp.shadows = LightShadows.None;
            lightComp.enabled = initialEnabled;

            return lightComp;
        }
    }
}
