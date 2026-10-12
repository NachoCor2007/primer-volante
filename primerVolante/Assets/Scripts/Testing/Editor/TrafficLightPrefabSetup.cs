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
        public const string LOW_POLY_LENS_MESH_PATH = "Assets/SimplePoly City - Low Poly Assets/Models/Props_TrafficLightLens_LowPoly.asset";

        // Coordenadas calculadas exactamente a partir de los centros de los focos en las mallas 3D
        // Para Traffic Signal Small (distribución vertical en poste):
        private static readonly Vector3 s_SmallRedPos = new Vector3(0.00f, 5.395f, 0.192f);
        private static readonly Vector3 s_SmallYellowPos = new Vector3(0.00f, 4.945f, 0.192f);
        private static readonly Vector3 s_SmallGreenPos = new Vector3(0.00f, 4.495f, 0.192f);

        // Para Traffic Signal Big (distribución horizontal en brazo):
        private static readonly Vector3 s_BigRedPos = new Vector3(2.23f, 5.28f, 0.23f);
        private static readonly Vector3 s_BigYellowPos = new Vector3(1.80f, 5.28f, 0.23f);
        private static readonly Vector3 s_BigGreenPos = new Vector3(1.35f, 5.28f, 0.23f);

        // Escala de la lente Low Poly aumentada para cubrir el visor del foco (~0.32 en X e Y)
        private static readonly Vector3 s_LensScale = new Vector3(0.32f, 0.32f, 0.12f);

        // Offset en Z hacia adelante para foco de luz Point Light
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
            Debug.Log("[TrafficLightPrefabSetup] 🚦 Iniciando configuración de Prefabs de Semáforos con Lentes Low Poly...");
            GetOrCreateLowPolyLensMesh(true);
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

        /// <summary>
        /// Obtiene o genera la malla Low Poly facetada para las lentes de los semáforos,
        /// guardándola de forma persistente como asset.
        /// </summary>
        public static Mesh GetOrCreateLowPolyLensMesh(bool forceRecreate = false)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(LOW_POLY_LENS_MESH_PATH);
            if (mesh == null || forceRecreate)
            {
                mesh = GenerateLowPolyLensMesh();
                if (AssetDatabase.LoadAssetAtPath<Mesh>(LOW_POLY_LENS_MESH_PATH) == null)
                {
                    AssetDatabase.CreateAsset(mesh, LOW_POLY_LENS_MESH_PATH);
                }
                else
                {
                    Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(LOW_POLY_LENS_MESH_PATH);
                    existing.Clear();
                    existing.vertices = mesh.vertices;
                    existing.triangles = mesh.triangles;
                    existing.normals = mesh.normals;
                    existing.uv = mesh.uv;
                    existing.RecalculateBounds();
                    mesh = existing;
                }
                AssetDatabase.SaveAssets();
            }
            return mesh;
        }

        /// <summary>
        /// Genera una malla Low Poly facetada (octagonal con sombreado plano),
        /// coherente con el estilo de SimplePoly City.
        /// </summary>
        public static Mesh GenerateLowPolyLensMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Props_TrafficLightLens_LowPoly";

            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();

            int sides = 8;
            float radius = 0.5f;
            float frontTipZ = 0.5f;
            float frontRimZ = 0.2f;
            float backRimZ = -0.5f;

            var frontRim = new Vector3[sides];
            var backRim = new Vector3[sides];
            float angleStep = 2f * Mathf.PI / sides;

            for (int i = 0; i < sides; i++)
            {
                float angle = (i + 0.5f) * angleStep;
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                frontRim[i] = new Vector3(x, y, frontRimZ);
                backRim[i] = new Vector3(x, y, backRimZ);
            }

            void AddTriangle(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                int baseIdx = vertices.Count;
                vertices.Add(v0);
                vertices.Add(v1);
                vertices.Add(v2);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);

                Vector3 n = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                normals.Add(n);
                normals.Add(n);
                normals.Add(n);

                uvs.Add(new Vector2(v0.x + 0.5f, v0.y + 0.5f));
                uvs.Add(new Vector2(v1.x + 0.5f, v1.y + 0.5f));
                uvs.Add(new Vector2(v2.x + 0.5f, v2.y + 0.5f));
            }

            Vector3 frontCenter = new Vector3(0f, 0f, frontTipZ);
            Vector3 backCenter = new Vector3(0f, 0f, backRimZ);

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;

                // Tapa frontal facetada (apunta hacia +Z)
                AddTriangle(frontCenter, frontRim[i], frontRim[next]);

                // Caras laterales cilíndricas facetadas
                AddTriangle(frontRim[i], backRim[next], frontRim[next]);
                AddTriangle(frontRim[i], backRim[i], backRim[next]);

                // Tapa trasera (apunta hacia -Z)
                AddTriangle(backCenter, backRim[next], backRim[i]);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

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

                // 2. Preparar malla Low Poly y material para las lentes
                Mesh lensMesh = GetOrCreateLowPolyLensMesh();
                Material lensMaterial = GetOrCreateLensMaterial();

                Vector3 redLensPos = redPos;
                Vector3 yellowLensPos = yellowPos;
                Vector3 greenLensPos = greenPos;

                Vector3 redLightPos = new Vector3(redPos.x, redPos.y, redPos.z + k_LightZOffset);
                Vector3 yellowLightPos = new Vector3(yellowPos.x, yellowPos.y, yellowPos.z + k_LightZOffset);
                Vector3 greenLightPos = new Vector3(greenPos.x, greenPos.y, greenPos.z + k_LightZOffset);

                // 3. Crear o actualizar objetos hijos de lentes físicas Low Poly
                MeshRenderer redLens = EnsureLensChild(prefabRoot.transform, "Lens_Red", redLensPos, s_LensScale, lensMaterial, lensMesh);
                MeshRenderer yellowLens = EnsureLensChild(prefabRoot.transform, "Lens_Yellow", yellowLensPos, s_LensScale, lensMaterial, lensMesh);
                MeshRenderer greenLens = EnsureLensChild(prefabRoot.transform, "Lens_Green", greenLensPos, s_LensScale, lensMaterial, lensMesh);

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
                SerializedProperty yellowToGreenDurProp = serializedObj.FindProperty("m_YellowToGreenDuration");
                SerializedProperty greenDurProp = serializedObj.FindProperty("m_GreenDuration");
                if (redDurProp != null && redDurProp.floatValue <= 0.01f) redDurProp.floatValue = 8f;
                if (yellowDurProp != null && yellowDurProp.floatValue <= 0.01f) yellowDurProp.floatValue = 2.5f;
                if (yellowToGreenDurProp != null && yellowToGreenDurProp.floatValue <= 0.01f) yellowToGreenDurProp.floatValue = 2.0f;
                if (greenDurProp != null && greenDurProp.floatValue <= 0.01f) greenDurProp.floatValue = 8f;

                serializedObj.ApplyModifiedProperties();

                // 6. Aplicar visuales de inmediato para persistir el estado inicial en el prefab
                controller.UpdateVisuals();

                // 7. Guardar prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return $"Configurado exitosamente con lentes Low Poly: {prefabPath}";
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
