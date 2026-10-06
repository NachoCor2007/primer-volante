using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para la creación, configuración y testeo de la señalización
    /// vial reglamentaria argentina (R.14 velocidad máxima) y sus zonas de evaluación.
    /// Es completamente idempotente: puede ejecutarse múltiples veces sin corromper assets ni duplicar objetos.
    /// </summary>
    public static class TrafficSignSetup
    {
        public const string TEXTURE_PATH = "Assets/Environment/Textures/TrafficSign_SpeedLimit_R14.png";
        public const string MATERIAL_PATH = "Assets/Environment/Materials/M_TrafficSign_Argentine_R14.mat";
        public const string MESH_DISC_PATH = "Assets/Environment/Meshes/TrafficSign_Argentine_CircularDisc.asset";
        public const string MESH_POLE_PATH = "Assets/Environment/Meshes/TrafficSign_Pole.asset";
        public const string PREFAB_SIGN_PATH = "Assets/Prefabs/TrafficSigns/SpeedLimitSign_Argentine.prefab";
        public const string PREFAB_ZONE_PATH = "Assets/Prefabs/TrafficSigns/SpeedLimitSignWithEvaluationZone.prefab";
        public const string BASE_MODEL_PATH = "Assets/SimplePoly City - Low Poly Assets/Models/Props_Traffic Sign_speed limit.fbx";
        public const string BASE_MATERIAL_PATH = "Assets/SimplePoly City - Low Poly Assets/Materials/Props_TrafficProps.mat";
        public const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        public const string SOURCE_SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/SoundIntegrationScene.unity";
        public const string TEST_SCENE_PATH = "Assets/Scenes/TestingScenes/SpeedLimitTestingScene.unity";

        public const float SIGN_CENTER_Y = 2.024f;
        public const float SIGN_RADIUS = 0.35f;

        [MenuItem("Tools/Primer Volante/Setup Speed Limit Test Scene")]
        public static void SetupAll()
        {
            Debug.Log("=========================================================");
            Debug.Log("🚦 CONFIGURANDO SISTEMA DE SEÑALIZACIÓN VIAL REGLAMENTARIA");
            Debug.Log("=========================================================");

            EnsureDirectories();
            GenerateSignTexture();
            CreateSignMaterial();
            CreateCircularMeshes();
            CreateSignPrefab();
            CreateSignWithZonePrefab();
            SetupTestScene();

            Debug.Log("=========================================================");
            Debug.Log("✅ CONFIGURACIÓN COMPLETADA CON ÉXITO");
            Debug.Log("=========================================================");
        }

        private static void EnsureDirectories()
        {
            EnsureFolder("Assets", "Environment");
            EnsureFolder("Assets/Environment", "Textures");
            EnsureFolder("Assets/Environment", "Materials");
            EnsureFolder("Assets/Environment", "Meshes");
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets/Prefabs", "TrafficSigns");
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets/Scenes", "TestingScenes");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string full = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(full))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        // ---------------------------------------------------------------- Textura R.14
        public static Texture2D GenerateSignTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TEXTURE_PATH);
            if (existing != null) return existing;

            int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            float center = size * 0.5f;
            float maxR = size * 0.485f;

            Color white = new Color(0.96f, 0.96f, 0.96f, 1.0f);
            Color red = new Color(0.85f, 0.10f, 0.10f, 1.0f); // Rojo vial reglamentario
            Color transparent = new Color(0f, 0f, 0f, 0f);

            // Proporciones reglamentarias R.14:
            // R > 1.0: transparente
            // 0.96 < R <= 1.0: fino borde perimetral blanco
            // 0.68 < R <= 0.96: aro circular rojo
            // R <= 0.68: disco interior blanco

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float rNorm = dist / maxR;

                    if (rNorm > 1.01f)
                    {
                        tex.SetPixel(x, y, transparent);
                    }
                    else if (rNorm > 0.96f)
                    {
                        // Borde exterior blanco con antialias
                        float alpha = Mathf.Clamp01((1.01f - rNorm) / 0.05f);
                        tex.SetPixel(x, y, new Color(white.r, white.g, white.b, alpha));
                    }
                    else if (rNorm > 0.68f)
                    {
                        // Aro perimetral rojo
                        float blendWhite = Mathf.Clamp01((rNorm - 0.94f) / 0.02f);
                        Color c = Color.Lerp(red, white, blendWhite);
                        float blendInner = Mathf.Clamp01((0.70f - rNorm) / 0.02f);
                        c = Color.Lerp(c, white, blendInner);
                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        // Disco central blanco
                        tex.SetPixel(x, y, white);
                    }
                }
            }

            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes(TEXTURE_PATH, bytes);
            AssetDatabase.ImportAsset(TEXTURE_PATH, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(TEXTURE_PATH) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            Debug.Log($"[TrafficSignSetup] 🎨 Textura R.14 generada en {TEXTURE_PATH}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TEXTURE_PATH);
        }

        // ---------------------------------------------------------------- Material R.14
        public static Material CreateSignMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MATERIAL_PATH);
            }

            var tex = GenerateSignTexture();
            mat.mainTexture = tex;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ---------------------------------------------------------------- Mallas 3D
        public static void CreateCircularMeshes()
        {
            // 1. Malla del Poste (extraída de Props_Traffic Sign_speed limit.fbx)
            var baseModel = AssetDatabase.LoadAssetAtPath<GameObject>(BASE_MODEL_PATH);
            if (baseModel != null)
            {
                var mf = baseModel.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var sourceMesh = mf.sharedMesh;
                    var poleMesh = new Mesh { name = "TrafficSign_Pole" };

                    // Conservar vértices del poste y base (índices 24 a 47)
                    var origVerts = sourceMesh.vertices;
                    var origNorms = sourceMesh.normals;
                    var origUvs = sourceMesh.uv;
                    var origTris = sourceMesh.triangles;

                    int poleStart = 24;
                    int poleCount = origVerts.Length - poleStart;

                    var newVerts = new Vector3[poleCount];
                    var newNorms = new Vector3[poleCount];
                    var newUvs = new Vector2[poleCount];

                    for (int i = 0; i < poleCount; i++)
                    {
                        newVerts[i] = origVerts[poleStart + i];
                        newNorms[i] = origNorms[poleStart + i];
                        newUvs[i] = origUvs[poleStart + i];
                    }

                    var newTris = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < origTris.Length; i += 3)
                    {
                        int i0 = origTris[i] - poleStart;
                        int i1 = origTris[i + 1] - poleStart;
                        int i2 = origTris[i + 2] - poleStart;
                        if (i0 >= 0 && i1 >= 0 && i2 >= 0 && i0 < poleCount && i1 < poleCount && i2 < poleCount)
                        {
                            newTris.Add(i0);
                            newTris.Add(i1);
                            newTris.Add(i2);
                        }
                    }

                    poleMesh.vertices = newVerts;
                    poleMesh.normals = newNorms;
                    poleMesh.uv = newUvs;
                    poleMesh.triangles = newTris.ToArray();
                    poleMesh.RecalculateBounds();

                    AssetDatabase.CreateAsset(poleMesh, MESH_POLE_PATH);
                }
            }

            // 2. Malla del Disco Circular R.14 (32 lados, espesor 0.015m, submesh frontal y trasera)
            var discMesh = new Mesh { name = "TrafficSign_Argentine_CircularDisc" };
            int segments = 32;
            float r = SIGN_RADIUS;
            float halfThickness = 0.008f;

            var verts = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();

            // Vértice central frontal
            verts.Add(new Vector3(0f, 0f, halfThickness));
            normals.Add(new Vector3(0f, 0f, 1f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            // Circunferencia frontal
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                verts.Add(new Vector3(cos * r, sin * r, halfThickness));
                normals.Add(new Vector3(0f, 0f, 1f));
                uvs.Add(new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f));
            }

            var frontTris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                frontTris.Add(0);
                frontTris.Add(1 + i);
                frontTris.Add(1 + next);
            }

            // Vértice central trasero
            int backCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, 0f, -halfThickness));
            normals.Add(new Vector3(0f, 0f, -1f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            // Circunferencia trasera
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                verts.Add(new Vector3(cos * r, sin * r, -halfThickness));
                normals.Add(new Vector3(0f, 0f, -1f));
                uvs.Add(new Vector2(0.5f, 0.5f)); // Mapeado a gris
            }

            var backTris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                backTris.Add(backCenterIdx);
                backTris.Add(backCenterIdx + 1 + next);
                backTris.Add(backCenterIdx + 1 + i);
            }

            // Aro perimetral / canto
            int rimStart = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                Vector3 n = new Vector3(cos, sin, 0f);

                verts.Add(new Vector3(cos * r, sin * r, halfThickness));
                normals.Add(n);
                uvs.Add(new Vector2(0.5f, 0.5f));

                verts.Add(new Vector3(cos * r, sin * r, -halfThickness));
                normals.Add(n);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int currentTop = rimStart + i * 2;
                int currentBot = rimStart + i * 2 + 1;
                int nextTop = rimStart + next * 2;
                int nextBot = rimStart + next * 2 + 1;

                backTris.Add(currentTop);
                backTris.Add(nextTop);
                backTris.Add(currentBot);

                backTris.Add(nextTop);
                backTris.Add(nextBot);
                backTris.Add(currentBot);
            }

            discMesh.subMeshCount = 2;
            discMesh.vertices = verts.ToArray();
            discMesh.normals = normals.ToArray();
            discMesh.uv = uvs.ToArray();
            discMesh.SetTriangles(frontTris.ToArray(), 0);
            discMesh.SetTriangles(backTris.ToArray(), 1);
            discMesh.RecalculateBounds();

            AssetDatabase.CreateAsset(discMesh, MESH_DISC_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TrafficSignSetup] 📐 Mallas generadas: {MESH_POLE_PATH} y {MESH_DISC_PATH}");
        }

        // ---------------------------------------------------------------- Prefab Cartel
        public static GameObject CreateSignPrefab()
        {
            var poleMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_POLE_PATH);
            var discMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_DISC_PATH);
            var signMat = CreateSignMaterial();
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(BASE_MATERIAL_PATH);
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            GameObject root = new GameObject("SpeedLimitSign_Argentine");

            // 1. Poste
            GameObject poleGo = new GameObject("Post");
            poleGo.transform.SetParent(root.transform, false);
            var poleMf = poleGo.AddComponent<MeshFilter>();
            poleMf.sharedMesh = poleMesh;
            var poleMr = poleGo.AddComponent<MeshRenderer>();
            poleMr.sharedMaterial = baseMat;

            // Colisionador físico del poste
            var col = poleGo.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 0.05f;
            col.height = 2f;

            // 2. Disco Circular
            GameObject discGo = new GameObject("SignDisc");
            discGo.transform.SetParent(root.transform, false);
            discGo.transform.localPosition = new Vector3(0f, SIGN_CENTER_Y, 0.035f);
            var discMf = discGo.AddComponent<MeshFilter>();
            discMf.sharedMesh = discMesh;
            var discMr = discGo.AddComponent<MeshRenderer>();
            discMr.sharedMaterials = new Material[] { signMat, baseMat };

            // 3. Display TextMeshPro
            GameObject textGo = new GameObject("Text_SpeedLimit");
            textGo.transform.SetParent(discGo.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.color = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(0.48f, 0.48f);

            // 4. Componente Controlador Display
            var display = root.AddComponent<SpeedLimitSignDisplay>();
            var so = new SerializedObject(display);
            so.FindProperty("m_TextDisplay").objectReferenceValue = tmp;
            so.FindProperty("m_SpeedLimitKmh").intValue = 40;
            so.FindProperty("m_FontSizeTwoDigits").floatValue = 3.2f;
            so.FindProperty("m_CharacterSpacingTwoDigits").floatValue = 0f;
            so.FindProperty("m_FontSizeThreeDigits").floatValue = 2.3f;
            so.FindProperty("m_CharacterSpacingThreeDigits").floatValue = -6f;
            so.ApplyModifiedPropertiesWithoutUndo();

            display.UpdateDisplay();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_SIGN_PATH);
            Object.DestroyImmediate(root);

            Debug.Log($"[TrafficSignSetup] 🏷️ Prefab cartel generado en {PREFAB_SIGN_PATH}");
            return prefab;
        }

        // ---------------------------------------------------------------- Prefab con Zona
        public static GameObject CreateSignWithZonePrefab()
        {
            var signPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_SIGN_PATH);
            if (signPrefab == null) signPrefab = CreateSignPrefab();

            GameObject instance = Object.Instantiate(signPrefab);
            instance.name = "SpeedLimitSignWithEvaluationZone";

            var display = instance.GetComponent<SpeedLimitSignDisplay>();

            // Crear GameObject de la zona de evaluación
            GameObject zoneGo = new GameObject("EvaluationZone");
            zoneGo.transform.SetParent(instance.transform, false);
            zoneGo.transform.localPosition = new Vector3(0f, 0f, 0f);

            var box = zoneGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            // Tramo vial típico: 8m ancho, 3.5m alto, 30m longitud frente a la señal
            box.center = new Vector3(0f, 1.75f, 15f);
            box.size = new Vector3(8f, 3.5f, 30f);

            var evaluator = zoneGo.AddComponent<SpeedLimitEvaluator>();
            evaluator.SpeedLimitKmh = 40;
            evaluator.ToleranceKmh = 3f;
            evaluator.AlertCooldown = 1.5f;
            evaluator.SignDisplay = display;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PREFAB_ZONE_PATH);
            Object.DestroyImmediate(instance);

            Debug.Log($"[TrafficSignSetup] 🏁 Prefab con zona de evaluación generado en {PREFAB_ZONE_PATH}");
            return prefab;
        }

        // ---------------------------------------------------------------- Escena de Prueba
        public static void SetupTestScene()
        {
            if (!File.Exists(TEST_SCENE_PATH))
            {
                if (File.Exists(SOURCE_SCENE_PATH))
                {
                    AssetDatabase.CopyAsset(SOURCE_SCENE_PATH, TEST_SCENE_PATH);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), TEST_SCENE_PATH);
                }
            }

            Scene scene = EditorSceneManager.OpenScene(TEST_SCENE_PATH, OpenSceneMode.Single);

            // Buscar Car08 en la escena usando la API no obsoleta reglamentaria
            var vehicle = Object.FindAnyObjectByType<VehicleController>();
            Vector3 carPos = vehicle != null ? vehicle.transform.position : Vector3.zero;
            Vector3 carForward = vehicle != null ? vehicle.transform.forward : Vector3.forward;
            Vector3 carRight = vehicle != null ? vehicle.transform.right : Vector3.right;

            // Limpiar señales de pruebas previas si ya existían
            var oldSigns = GameObject.Find("SpeedLimitTestCourse");
            if (oldSigns != null)
            {
                Object.DestroyImmediate(oldSigns);
            }

            GameObject courseRoot = new GameObject("SpeedLimitTestCourse");

            var prefabZone = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_ZONE_PATH);
            if (prefabZone == null) prefabZone = CreateSignWithZonePrefab();

            // 1. Tramo Urbano (40 km/h) - a 10m de distancia del auto
            Vector3 pos40 = carPos + carForward * 10f + carRight * 3.5f;
            var sign40 = (GameObject)PrefabUtility.InstantiatePrefab(prefabZone, scene);
            sign40.name = "SpeedLimitSign_40_Urban";
            sign40.transform.SetParent(courseRoot.transform, false);
            sign40.transform.position = pos40;
            // Mirando hacia el conductor (+Z del cartel apuntando contra el avance)
            sign40.transform.rotation = Quaternion.LookRotation(-carForward, Vector3.up);

            var disp40 = sign40.GetComponent<SpeedLimitSignDisplay>();
            if (disp40 != null) disp40.SetSpeedLimit(40);

            var eval40 = sign40.GetComponentInChildren<SpeedLimitEvaluator>();
            if (eval40 != null)
            {
                eval40.SpeedLimitKmh = 40;
                eval40.ToleranceKmh = 3f;
                // Ajustar collider para cubrir la calzada
                var box = eval40.GetComponent<BoxCollider>();
                box.center = new Vector3(3.5f, 1.75f, 12.5f);
                box.size = new Vector3(8f, 3.5f, 25f);
            }

            // 2. Tramo Rápido / Autopista (110 km/h) - a 45m de distancia
            Vector3 pos110 = carPos + carForward * 45f + carRight * 3.5f;
            var sign110 = (GameObject)PrefabUtility.InstantiatePrefab(prefabZone, scene);
            sign110.name = "SpeedLimitSign_110_Highway";
            sign110.transform.SetParent(courseRoot.transform, false);
            sign110.transform.position = pos110;
            sign110.transform.rotation = Quaternion.LookRotation(-carForward, Vector3.up);

            var disp110 = sign110.GetComponent<SpeedLimitSignDisplay>();
            if (disp110 != null) disp110.SetSpeedLimit(110);

            var eval110 = sign110.GetComponentInChildren<SpeedLimitEvaluator>();
            if (eval110 != null)
            {
                eval110.SpeedLimitKmh = 110;
                eval110.ToleranceKmh = 5f;
                var box = eval110.GetComponent<BoxCollider>();
                box.center = new Vector3(3.5f, 1.75f, 20f);
                box.size = new Vector3(8f, 3.5f, 40f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = courseRoot;
            Debug.Log($"[TrafficSignSetup] 🏎️ Escena de prueba configurada en {TEST_SCENE_PATH} con tramos de 40 km/h y 110 km/h.");
        }
    }
}
