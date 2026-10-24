using System.IO;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Carteles de la detección de contramano: contramano (prohibido el paso en ese sentido) y sentido de
    /// circulación (flecha). Reutiliza el método procedural de <see cref="TrafficSignSetup"/> (malla de disco y de
    /// poste, textura generada por código). Son solo visuales: la regla vive en <c>RoadLaneDirection</c>.
    /// </summary>
    /// <remarks>
    /// No se pudo confirmar el código ni el diseño exactos en el Anexo L del Decreto 779/95 (el Anexo L fue
    /// sustituido por el Decreto 196/2025 y no se pudo consultar su texto), así que los assets se nombran sin
    /// código: <c>OneWayProhibitedSign_Argentine</c> y <c>OneWayDirectionSign_Argentine</c>.
    /// </remarks>
    public static partial class WrongWaySetup
    {
        public const string PROHIBITED_TEXTURE_PATH = "Assets/Environment/Textures/TrafficSign_OneWayProhibited.png";
        public const string DIRECTION_TEXTURE_PATH = "Assets/Environment/Textures/TrafficSign_OneWayDirection.png";
        public const string PROHIBITED_MATERIAL_PATH = "Assets/Environment/Materials/M_TrafficSign_OneWayProhibited.mat";
        public const string DIRECTION_MATERIAL_PATH = "Assets/Environment/Materials/M_TrafficSign_OneWayDirection.mat";
        public const string PANEL_MESH_PATH = "Assets/Environment/Meshes/TrafficSign_Argentine_RectPanel.asset";
        public const string PROHIBITED_PREFAB_PATH = "Assets/Prefabs/TrafficSigns/OneWayProhibitedSign_Argentine.prefab";
        public const string DIRECTION_PREFAB_PATH = "Assets/Prefabs/TrafficSigns/OneWayDirectionSign_Argentine.prefab";

        /// <summary>Ancho y alto del panel de sentido de circulación (m).</summary>
        public const float PANEL_WIDTH = 0.5f;
        public const float PANEL_HEIGHT = 0.75f;

        private const float k_PanelCenterY = 2.0f;

        private static void SetupSignPrefabs()
        {
            EnsureFolder("Assets/Environment");
            EnsureFolder("Assets/Environment/Textures");
            EnsureFolder("Assets/Environment/Materials");
            EnsureFolder("Assets/Environment/Meshes");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/TrafficSigns");

            // Las mallas del poste y del disco las genera TrafficSignSetup (si faltan, se crean).
            if (AssetDatabase.LoadAssetAtPath<Mesh>(TrafficSignSetup.MESH_DISC_PATH) == null ||
                AssetDatabase.LoadAssetAtPath<Mesh>(TrafficSignSetup.MESH_POLE_PATH) == null)
            {
                TrafficSignSetup.CreateCircularMeshes();
            }

            Mesh disc = AssetDatabase.LoadAssetAtPath<Mesh>(TrafficSignSetup.MESH_DISC_PATH);
            Mesh pole = AssetDatabase.LoadAssetAtPath<Mesh>(TrafficSignSetup.MESH_POLE_PATH);
            Mesh panel = EnsurePanelMesh();

            Texture2D prohibitedTexture = EnsureTexture(PROHIBITED_TEXTURE_PATH, 512, 512, PaintProhibited);
            Texture2D directionTexture = EnsureTexture(DIRECTION_TEXTURE_PATH, 256, 384, PaintDirection);
            Material prohibitedMaterial = EnsureMaterial(PROHIBITED_MATERIAL_PATH, prohibitedTexture);
            Material directionMaterial = EnsureMaterial(DIRECTION_MATERIAL_PATH, directionTexture);
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(TrafficSignSetup.BASE_MATERIAL_PATH);

            BuildSignPrefab("OneWayProhibitedSign_Argentine", PROHIBITED_PREFAB_PATH, pole, disc,
                TrafficSignSetup.SIGN_CENTER_Y, prohibitedMaterial, baseMaterial);
            BuildSignPrefab("OneWayDirectionSign_Argentine", DIRECTION_PREFAB_PATH, pole, panel,
                k_PanelCenterY, directionMaterial, baseMaterial);

            AssetDatabase.SaveAssets();
            Debug.Log($"[WrongWaySetup] 🪧 Carteles listos: {PROHIBITED_PREFAB_PATH} y {DIRECTION_PREFAB_PATH}.");
        }

        // ------------------------------------------------------------------ Prefabs

        private static void BuildSignPrefab(string name, string prefabPath, Mesh pole, Mesh face, float faceCenterY,
            Material faceMaterial, Material baseMaterial)
        {
            var root = new GameObject(name);
            try
            {
                var post = new GameObject("Post");
                post.transform.SetParent(root.transform, false);
                post.AddComponent<MeshFilter>().sharedMesh = pole;
                post.AddComponent<MeshRenderer>().sharedMaterial = baseMaterial;

                // Colisionador físico del poste (como el del cartel de velocidad); no es un trigger de detección.
                var capsule = post.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 1f, 0f);
                capsule.radius = 0.05f;
                capsule.height = 2f;

                var signFace = new GameObject("SignFace");
                signFace.transform.SetParent(root.transform, false);
                signFace.transform.localPosition = new Vector3(0f, faceCenterY, 0.035f);
                signFace.AddComponent<MeshFilter>().sharedMesh = face;
                signFace.AddComponent<MeshRenderer>().sharedMaterials = new[] { faceMaterial, baseMaterial };

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Panel rectangular de 2 submallas (frente / dorso y cantos), como el disco.
        private static Mesh EnsurePanelMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PANEL_MESH_PATH);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = "TrafficSign_Argentine_RectPanel" };
            else mesh.Clear();

            float hw = PANEL_WIDTH * 0.5f, hh = PANEL_HEIGHT * 0.5f, t = 0.008f;
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var front = new System.Collections.Generic.List<int>();
            var rest = new System.Collections.Generic.List<int>();

            void Quad(System.Collections.Generic.List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool texture)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                if (texture)
                {
                    // Visto desde +Z el eje +X queda a la izquierda: u = 1 en x = -hw para que la textura no salga espejada.
                    uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(0f, 0f));
                }
                else
                {
                    for (int k = 0; k < 4; k++) uv.Add(new Vector2(0.5f, 0.5f));
                }

                // Sentido horario visto desde afuera (cara frontal en Unity).
                tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            // Frente (+Z).
            Quad(front, new Vector3(-hw, -hh, t), new Vector3(-hw, hh, t), new Vector3(hw, hh, t), new Vector3(hw, -hh, t), Vector3.forward, true);
            // Dorso (−Z).
            Quad(rest, new Vector3(hw, -hh, -t), new Vector3(hw, hh, -t), new Vector3(-hw, hh, -t), new Vector3(-hw, -hh, -t), Vector3.back, false);
            // Cantos.
            Quad(rest, new Vector3(-hw, -hh, -t), new Vector3(-hw, hh, -t), new Vector3(-hw, hh, t), new Vector3(-hw, -hh, t), Vector3.left, false);
            Quad(rest, new Vector3(hw, -hh, t), new Vector3(hw, hh, t), new Vector3(hw, hh, -t), new Vector3(hw, -hh, -t), Vector3.right, false);
            Quad(rest, new Vector3(-hw, hh, t), new Vector3(-hw, hh, -t), new Vector3(hw, hh, -t), new Vector3(hw, hh, t), Vector3.up, false);
            Quad(rest, new Vector3(-hw, -hh, -t), new Vector3(-hw, -hh, t), new Vector3(hw, -hh, t), new Vector3(hw, -hh, -t), Vector3.down, false);

            mesh.subMeshCount = 2;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(front, 0);
            mesh.SetTriangles(rest, 1);
            mesh.RecalculateBounds();

            if (isNew) AssetDatabase.CreateAsset(mesh, PANEL_MESH_PATH);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material EnsureMaterial(string path, Texture2D texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.mainTexture = texture;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ Texturas procedurales

        private delegate Color PixelPainter(float x, float y, int width, int height);

        private static Texture2D EnsureTexture(string path, int width, int height, PixelPainter painter)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) texture.SetPixel(x, y, painter(x + 0.5f, y + 0.5f, width, height));
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            Debug.Log($"[WrongWaySetup] 🎨 Textura generada en {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Contramano: disco rojo con borde blanco fino y una barra horizontal blanca.
        private static Color PaintProhibited(float x, float y, int width, int height)
        {
            float center = width * 0.5f;
            float maxR = width * 0.49f;
            float dx = x - center, dy = y - center;
            float rNorm = Mathf.Sqrt(dx * dx + dy * dy) / maxR;

            Color white = new Color(0.96f, 0.96f, 0.96f, 1f);
            Color red = new Color(0.85f, 0.10f, 0.10f, 1f);

            if (rNorm > 1.01f) return new Color(0f, 0f, 0f, 0f);
            if (rNorm > 0.96f) return new Color(white.r, white.g, white.b, Mathf.Clamp01((1.01f - rNorm) / 0.05f));

            // Barra: 62 % del radio a cada lado y 16 % de alto, con borde suave de ~1 px.
            float barHalfW = 0.62f * maxR, barHalfH = 0.16f * maxR;
            float edge = Mathf.Max(Mathf.Abs(dx) - barHalfW, Mathf.Abs(dy) - barHalfH);
            float bar = Mathf.Clamp01(0.5f - edge);
            return Color.Lerp(red, white, bar);
        }

        // Sentido de circulación: panel azul con borde blanco y una flecha blanca hacia arriba.
        private static Color PaintDirection(float x, float y, int width, int height)
        {
            Color blue = new Color(0.04f, 0.24f, 0.60f, 1f);
            Color white = new Color(0.96f, 0.96f, 0.96f, 1f);

            float u = x / width, v = y / height;

            // Borde blanco a 4 % del lado menor.
            float border = 0.04f * width;
            bool inBorder = x < border || x > width - border || y < border || y > height - border;
            bool inInnerBorder = x < border * 2f || x > width - border * 2f || y < border * 2f || y > height - border * 2f;
            if (inBorder) return white;
            if (inInnerBorder) return blue;

            // Flecha: cabeza triangular (base en v = 0.48, punta en v = 0.86) y cuerpo rectangular.
            float cx = Mathf.Abs(u - 0.5f);
            bool shaft = cx < 0.09f && v > 0.14f && v <= 0.50f;
            float headProgress = (v - 0.48f) / (0.86f - 0.48f);
            bool head = headProgress >= 0f && headProgress <= 1f && cx < 0.27f * (1f - headProgress);
            return shaft || head ? white : blue;
        }
    }
}
