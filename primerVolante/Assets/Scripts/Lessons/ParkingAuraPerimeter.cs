using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Genera y anima un aura perimetral holográfica 3D luminosa en los bordes de la dársena de estacionamiento.
    /// Crea paredes perimetrales verticales de luz suave que se levantan desde el asfalto y se disuelven en el aire,
    /// delimitando visualmente el espacio de maniobra en VR.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ParkingAuraPerimeter : MonoBehaviour
    {
        [Header("Dimensiones del Área")]
        [Tooltip("Ancho (X) y Largo (Z) del recuadro de estacionamiento en metros.")]
        [SerializeField] private Vector2 m_Size = new Vector2(3.0f, 5.5f);

        [Tooltip("Altura que se levanta el aura desde el asfalto (en metros).")]
        [SerializeField] private float m_Height = 0.60f;

        [Tooltip("Desfase local respecto al pivote del objeto.")]
        [SerializeField] private Vector3 m_CenterOffset = Vector3.zero;

        [Header("Material y Apariencia")]
        [Tooltip("Material con el shader Custom/ParkingAura.")]
        [SerializeField] private Material m_AuraMaterial;

        [Tooltip("Color base del aura (amarillo neón cálido por defecto).")]
        [SerializeField] private Color m_BaseColor = new Color(1.0f, 0.82f, 0.08f, 1.0f);

        [Header("Animación de Pulso")]
        [Tooltip("Velocidad de oscilación del brillo.")]
        [SerializeField] private float m_PulseSpeed = 3.5f;

        [Tooltip("Intensidad mínima del pulso.")]
        [SerializeField] private float m_MinIntensity = 1.8f;

        [Tooltip("Intensidad máxima del pulso.")]
        [SerializeField] private float m_MaxIntensity = 3.4f;

        private MeshFilter m_MeshFilter;
        private MeshRenderer m_MeshRenderer;
        private Mesh m_GeneratedMesh;
        private MaterialPropertyBlock m_PropBlock;

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_IntensityId = Shader.PropertyToID("_Intensity");

        public Vector2 Size
        {
            get => m_Size;
            set
            {
                if (m_Size != value)
                {
                    m_Size = value;
                    BuildAuraMesh();
                }
            }
        }

        public float Height
        {
            get => m_Height;
            set
            {
                if (Mathf.Abs(m_Height - value) > 0.001f)
                {
                    m_Height = value;
                    BuildAuraMesh();
                }
            }
        }

        public Vector3 CenterOffset
        {
            get => m_CenterOffset;
            set
            {
                if (m_CenterOffset != value)
                {
                    m_CenterOffset = value;
                    BuildAuraMesh();
                }
            }
        }

        public Material AuraMaterial
        {
            get => m_AuraMaterial;
            set
            {
                m_AuraMaterial = value;
                if (m_MeshRenderer != null && m_AuraMaterial != null)
                {
                    m_MeshRenderer.sharedMaterial = m_AuraMaterial;
                }
            }
        }

        private void OnEnable()
        {
            InitComponents();
            BuildAuraMesh();
        }

        private void OnValidate()
        {
            InitComponents();
            BuildAuraMesh();
        }

        private void InitComponents()
        {
            if (m_MeshFilter == null) m_MeshFilter = GetComponent<MeshFilter>();
            if (m_MeshRenderer == null) m_MeshRenderer = GetComponent<MeshRenderer>();

            if (m_MeshRenderer != null && m_AuraMaterial != null && m_MeshRenderer.sharedMaterial != m_AuraMaterial)
            {
                m_MeshRenderer.sharedMaterial = m_AuraMaterial;
            }

            if (m_PropBlock == null)
            {
                m_PropBlock = new MaterialPropertyBlock();
            }
        }

        private void Update()
        {
            if (m_MeshRenderer == null) m_MeshRenderer = GetComponent<MeshRenderer>();
            if (m_PropBlock == null) m_PropBlock = new MaterialPropertyBlock();

            float timeVal = Time.time;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                timeVal = (float)UnityEditor.EditorApplication.timeSinceStartup;
            }
#endif
            float pulse = (Mathf.Sin(timeVal * m_PulseSpeed) + 1f) * 0.5f;
            float currentIntensity = Mathf.Lerp(m_MinIntensity, m_MaxIntensity, pulse);

            m_MeshRenderer.GetPropertyBlock(m_PropBlock);
            m_PropBlock.SetColor(s_BaseColorId, m_BaseColor);
            m_PropBlock.SetFloat(s_IntensityId, currentIntensity);
            m_MeshRenderer.SetPropertyBlock(m_PropBlock);
        }

        /// <summary>
        /// Construye la malla con las 4 paredes perimetrales verticales y 4 postes de esquina.
        /// </summary>
        public void BuildAuraMesh()
        {
            InitComponents();
            if (m_MeshFilter == null) return;

            if (m_GeneratedMesh == null)
            {
                m_GeneratedMesh = new Mesh
                {
                    name = "ParkingAuraPerimeter_Mesh"
                };
            }
            else
            {
                m_GeneratedMesh.Clear();
            }

            float hw = m_Size.x * 0.5f;
            float hl = m_Size.y * 0.5f;
            float h = Mathf.Max(0.1f, m_Height);
            float cx = m_CenterOffset.x;
            float cz = m_CenterOffset.z;
            float y0 = 0.02f;
            float y1 = y0 + h;

            float x0 = cx - hw;
            float x1 = cx + hw;
            float z0 = cz - hl;
            float z1 = cz + hl;

            List<Vector3> vertices = new List<Vector3>(32);
            List<Vector2> uvs = new List<Vector2>(32);
            List<int> triangles = new List<int>(48);

            // 1. Pared Izquierda (X = x0, z0 -> z1)
            AddVerticalQuad(vertices, uvs, triangles,
                new Vector3(x0, y0, z0), new Vector3(x0, y0, z1),
                new Vector3(x0, y1, z0), new Vector3(x0, y1, z1));

            // 2. Pared Frontal (Z = z1, x0 -> x1)
            AddVerticalQuad(vertices, uvs, triangles,
                new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
                new Vector3(x0, y1, z1), new Vector3(x1, y1, z1));

            // 3. Pared Derecha (X = x1, z1 -> z0)
            AddVerticalQuad(vertices, uvs, triangles,
                new Vector3(x1, y0, z1), new Vector3(x1, y0, z0),
                new Vector3(x1, y1, z1), new Vector3(x1, y1, z0));

            // 4. Pared Trasera (Z = z0, x1 -> x0)
            AddVerticalQuad(vertices, uvs, triangles,
                new Vector3(x1, y0, z0), new Vector3(x0, y0, z0),
                new Vector3(x1, y1, z0), new Vector3(x0, y1, z0));

            // 5. Postes en las 4 esquinas (cintas verticales de refuerzo lumínico)
            float cornerW = 0.12f;
            AddCornerPillar(vertices, uvs, triangles, new Vector3(x0, y0, z0), y1, cornerW);
            AddCornerPillar(vertices, uvs, triangles, new Vector3(x0, y0, z1), y1, cornerW);
            AddCornerPillar(vertices, uvs, triangles, new Vector3(x1, y0, z1), y1, cornerW);
            AddCornerPillar(vertices, uvs, triangles, new Vector3(x1, y0, z0), y1, cornerW);

            m_GeneratedMesh.SetVertices(vertices);
            m_GeneratedMesh.SetUVs(0, uvs);
            m_GeneratedMesh.SetTriangles(triangles, 0);
            m_GeneratedMesh.RecalculateNormals();
            m_GeneratedMesh.RecalculateBounds();

            m_MeshFilter.sharedMesh = m_GeneratedMesh;
        }

        private static void AddVerticalQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 b0, Vector3 b1, Vector3 t0, Vector3 t1)
        {
            int baseIdx = verts.Count;

            verts.Add(b0); // 0
            verts.Add(b1); // 1
            verts.Add(t0); // 2
            verts.Add(t1); // 3

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));

            // Cara frontal
            tris.Add(baseIdx + 0);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 1);

            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 3);
            tris.Add(baseIdx + 1);

            // Cara trasera
            tris.Add(baseIdx + 0);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);

            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 3);
        }

        private static void AddCornerPillar(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 baseCenter, float topY, float halfW)
        {
            // Cinta vertical a lo largo de X
            AddVerticalQuad(verts, uvs, tris,
                new Vector3(baseCenter.x - halfW, baseCenter.y, baseCenter.z),
                new Vector3(baseCenter.x + halfW, baseCenter.y, baseCenter.z),
                new Vector3(baseCenter.x - halfW, topY, baseCenter.z),
                new Vector3(baseCenter.x + halfW, topY, baseCenter.z));

            // Cinta vertical a lo largo de Z
            AddVerticalQuad(verts, uvs, tris,
                new Vector3(baseCenter.x, baseCenter.y, baseCenter.z - halfW),
                new Vector3(baseCenter.x, baseCenter.y, baseCenter.z + halfW),
                new Vector3(baseCenter.x, topY, baseCenter.z - halfW),
                new Vector3(baseCenter.x, topY, baseCenter.z + halfW));
        }
    }
}
