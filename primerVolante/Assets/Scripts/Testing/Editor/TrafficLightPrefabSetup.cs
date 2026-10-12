using PrimerVolante.Environment;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para configurar los prefabs
    /// de semáforos (Props_Traffic Signal_big y Props_Traffic Signal_small) con
    /// lentes físicas 3D (MeshRenderer + MaterialPropertyBlock) y componentes Light (Point Lights).
    /// </summary>
    public static class TrafficLightPrefabSetup
    {
        public const string BIG_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Signal_big.prefab";
        public const string SMALL_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Signal_small.prefab";
        public const string LENS_MATERIAL_PATH = "Assets/SimplePoly City - Low Poly Assets/Materials/Props_TrafficLightLens.mat";

        // Coordenadas calculadas exactamente a partir de los centros de los focos en las mallas 3D
        // Para Traffic Signal Small (distribución vertical en poste):
        private static readonly Vector3 s_SmallRedPos = new Vector3(0.00f, 5.38f, 0.22f);
        private static readonly Vector3 s_SmallYellowPos = new Vector3(0.00f, 4.94f, 0.22f);
        private static readonly Vector3 s_SmallGreenPos = new Vector3(0.00f, 4.50f, 0.22f);

        // Para Traffic Signal Big (distribución horizontal en brazo):
        private static readonly Vector3 s_BigRedPos = new Vector3(2.23f, 5.28f, 0.25f);
        private static readonly Vector3 s_BigYellowPos = new Vector3(1.80f, 5.28f, 0.25f);
        private static readonly Vector3 s_BigGreenPos = new Vector3(1.35f, 5.28f, 0.25f);

        // Escala de la lente esférica achatada para encajar en el visor del foco
        private static readonly Vector3 s_LensScale = new Vector3(0.22f, 0.22f, 0.08f);

        // Offsets en Z hacia adelante para lente física y foco de luz (evitar z-fighting y oclusión)
        private const float k_LensZOffset = 0.015f;
        private const float k_LightZOffset = 0.06f;

        // Parámetros de iluminación exterior visible diurna
        private static readonly Color s_RedColor = new Color(1.0f, 0.1f, 0.1f, 1.0f);       // #FF1A1A
        private static readonly Color s_YellowColor = new Color(1.0f, 0.72f, 0.05f, 1.0f);   // #FFB80D
        private static readonly Color s_GreenColor = new Color(0.18f, 0.85f, 0.44f, 1.0f);   // #2ECC71
        private const float k_LightRange = 7.0f;
        private const float k_LightIntensity = 7.0f;

        [MenuItem("Tools/Primer Volante/Setup Traffic Light Prefabs")]
        public static string SetupAllPrefabs()
        {
            Debug.Log("[TrafficLightPrefabSetup] 🚦 Iniciando configuración de Prefabs de Semáforos con Lentes Físicas...");
            string r1 = ConfigurePrefab(SMALL_PREFAB_PATH, s_SmallRedPos, s_SmallYellowPos, s_SmallGreenPos);
            string r2 = ConfigurePrefab(BIG_PREFAB_PATH, s_BigRedPos, s_BigYellowPos, s_BigGreenPos);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string result = $"{r1}\n{r2}";
            Debug.Log($"[TrafficLightPrefabSetup] ✅ Configuración finalizada:\n{result}");
            return result;
        }

        public static Material GetOrCreateLensMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(LENS_MATERIAL_PATH);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                mat = new Material(shader);
                mat.name = "Props_TrafficLightLens";
                if (mat.HasProperty("_Smoothness"))
                {
                    mat.SetFloat("_Smoothness", 0.9f);
                }
                if (mat.HasProperty("_Metallic"))
                {
                    mat.SetFloat("_Metallic", 0.0f);
                }
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                AssetDatabase.CreateAsset(mat, LENS_MATERIAL_PATH);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static Mesh GetSpherePrimitiveMesh()
        {
            GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tempSphere);
            return mesh;
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

                // 2. Preparar malla y material para las lentes
                Mesh sphereMesh = GetSpherePrimitiveMesh();
                Material lensMaterial = GetOrCreateLensMaterial();

                Vector3 redLensPos = new Vector3(redPos.x, redPos.y, redPos.z + k_LensZOffset);
                Vector3 yellowLensPos = new Vector3(yellowPos.x, yellowPos.y, yellowPos.z + k_LensZOffset);
                Vector3 greenLensPos = new Vector3(greenPos.x, greenPos.y, greenPos.z + k_LensZOffset);

                Vector3 redLightPos = new Vector3(redPos.x, redPos.y, redPos.z + k_LightZOffset);
                Vector3 yellowLightPos = new Vector3(yellowPos.x, yellowPos.y, yellowPos.z + k_LightZOffset);
                Vector3 greenLightPos = new Vector3(greenPos.x, greenPos.y, greenPos.z + k_LightZOffset);

                // 3. Crear o actualizar objetos hijos de lentes físicas
                MeshRenderer redLens = EnsureLensChild(prefabRoot.transform, "Lens_Red", redLensPos, s_LensScale, lensMaterial, sphereMesh);
                MeshRenderer yellowLens = EnsureLensChild(prefabRoot.transform, "Lens_Yellow", yellowLensPos, s_LensScale, lensMaterial, sphereMesh);
                MeshRenderer greenLens = EnsureLensChild(prefabRoot.transform, "Lens_Green", greenLensPos, s_LensScale, lensMaterial, sphereMesh);

                // 4. Crear o actualizar objetos hijos de luces Point
                Light redLight = EnsureLightChild(prefabRoot.transform, "Light_Red", redLightPos, s_RedColor, true);
                Light yellowLight = EnsureLightChild(prefabRoot.transform, "Light_Yellow", yellowLightPos, s_YellowColor, false);
                Light greenLight = EnsureLightChild(prefabRoot.transform, "Light_Green", greenLightPos, s_GreenColor, false);

                // 5. Serializar referencias en el controlador
                SerializedObject serializedObj = new SerializedObject(controller);
                serializedObj.Update();

                SerializedProperty redLightProp = serializedObj.FindProperty("m_RedLight");
                SerializedProperty yellowLightProp = serializedObj.FindProperty("m_YellowLight");
                SerializedProperty greenLightProp = serializedObj.FindProperty("m_GreenLight");

                if (redLightProp != null) redLightProp.objectReferenceValue = redLight;
                if (yellowLightProp != null) yellowLightProp.objectReferenceValue = yellowLight;
                if (greenLightProp != null) greenLightProp.objectReferenceValue = greenLight;

                SerializedProperty redLensProp = serializedObj.FindProperty("m_RedLensRenderer");
                SerializedProperty yellowLensProp = serializedObj.FindProperty("m_YellowLensRenderer");
                SerializedProperty greenLensProp = serializedObj.FindProperty("m_GreenLensRenderer");

                if (redLensProp != null) redLensProp.objectReferenceValue = redLens;
                if (yellowLensProp != null) yellowLensProp.objectReferenceValue = yellowLens;
                if (greenLensProp != null) greenLensProp.objectReferenceValue = greenLens;

                SerializedProperty redDurProp = serializedObj.FindProperty("m_RedDuration");
                SerializedProperty yellowDurProp = serializedObj.FindProperty("m_YellowDuration");
                SerializedProperty greenDurProp = serializedObj.FindProperty("m_GreenDuration");
                if (redDurProp != null && redDurProp.floatValue <= 0.01f) redDurProp.floatValue = 8f;
                if (yellowDurProp != null && yellowDurProp.floatValue <= 0.01f) yellowDurProp.floatValue = 2.5f;
                if (greenDurProp != null && greenDurProp.floatValue <= 0.01f) greenDurProp.floatValue = 8f;

                serializedObj.ApplyModifiedProperties();

                // 6. Aplicar visuales de inmediato para persistir el estado inicial en el prefab
                controller.UpdateVisuals();

                // 7. Guardar prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return $"Configurado exitosamente con lentes físicas: {prefabPath}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static MeshRenderer EnsureLensChild(
            Transform parent,
            string childName,
            Vector3 localPos,
            Vector3 localScale,
            Material material,
            Mesh mesh)
        {
            Transform existingChild = parent.Find(childName);
            GameObject lensObj;

            if (existingChild != null)
            {
                lensObj = existingChild.gameObject;
            }
            else
            {
                lensObj = new GameObject(childName);
                lensObj.transform.SetParent(parent, false);
            }

            lensObj.transform.localPosition = localPos;
            lensObj.transform.localRotation = Quaternion.identity;
            lensObj.transform.localScale = localScale;

            // Eliminar colisionadores si existieran para evitar interferencia física
            Collider col = lensObj.GetComponent<Collider>();
            if (col != null)
            {
                Object.DestroyImmediate(col);
            }

            MeshFilter filter = lensObj.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = lensObj.AddComponent<MeshFilter>();
            }
            filter.sharedMesh = mesh;

            MeshRenderer renderer = lensObj.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = lensObj.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return renderer;
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
