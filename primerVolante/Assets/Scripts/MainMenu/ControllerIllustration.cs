using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.MainMenu
{
    /// <summary>
    /// Ilustración 3D del control VR para las tarjetas del menú principal. Instancia el modelo del control
    /// (UniversalController.fbx) como hijo, escalado para la tarjeta, y pinta una pieza (<c>Bumper</c> = grip,
    /// <c>Trigger</c>, <c>Button_Home</c> = menú...) con un color de resaltado y emisión. Los materiales se instancian
    /// (copias en memoria): nunca se modifican los del asset.
    /// Modo opcional "Pulsar": animación en loop por código en que el control se acerca a un botón 3D simple
    /// (cilindro), lo hunde, vuelve y espera.
    /// </summary>
    public class ControllerIllustration : MonoBehaviour
    {
        private const string ROOT_NAME = "_Illustration";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Modelo")]
        [Tooltip("Modelo 3D del control (UniversalController.fbx).")]
        [SerializeField] private GameObject m_ControllerModel;

        [Tooltip("Escala local de la ilustración respecto a este transform. El canvas está a 0,001 m por unidad, así que " +
                 "un control de ~0,11 m ocupa ~200 unidades con escala 1800.")]
        [SerializeField] private float m_ModelScale = 1800f;

        [Tooltip("Rotación local (Euler) de la ilustración, para elegir desde qué ángulo se ve el control.")]
        [SerializeField] private Vector3 m_ModelEuler = new Vector3(20f, -35f, 0f);

        [Header("Pieza resaltada")]
        [Tooltip("Nombre de la pieza del modelo a pintar (Bumper, Trigger, Button_Home...). Vacío = no pintar nada.")]
        [SerializeField] private string m_PieceName = "Bumper";

        [SerializeField] private Color m_HighlightColor = new Color(1f, 0.4f, 0f, 1f);

        [Tooltip("Multiplicador de la emisión de la pieza resaltada.")]
        [SerializeField] private float m_EmissionIntensity = 1.2f;

        [Header("Modo Pulsar")]
        [Tooltip("Anima el control acercándose a un botón 3D (cilindro), hundiéndolo, volviendo y esperando.")]
        [SerializeField] private bool m_PressAnimation = false;

        [Tooltip("Duración total del ciclo de la animación (segundos).")]
        [SerializeField] private float m_CycleSeconds = 3.2f;

        [Tooltip("Distancia (en metros del modelo) entre el control en reposo y el botón.")]
        [SerializeField] private float m_ApproachDistance = 0.13f;

        [Tooltip("Cuánto se hunde el botón al pulsarlo (metros del modelo).")]
        [SerializeField] private float m_ButtonTravel = 0.014f;

        [SerializeField] private Color m_ButtonColor = new Color(0.85f, 0.15f, 0.15f, 1f);

        private Transform m_IllustrationRoot;
        private Transform m_ControllerTransform;
        private Transform m_ButtonTransform;
        private Renderer m_HighlightedRenderer;
        private Vector3 m_ButtonRestLocalPosition;
        private Vector3 m_ModelCenterOffset;
        private float m_AssemblyZShift;
        private readonly List<Material> m_InstancedMaterials = new List<Material>();

        // Punto de contacto: el control toca el botón con la punta delantera (~5,5 cm desde el centro del modelo).
        private const float CONTROLLER_TIP = 0.055f;
        private const float BUTTON_HEIGHT = 0.012f;

        /// <summary>true si la ilustración está en modo Pulsar.</summary>
        public bool PressAnimation => m_PressAnimation;

        /// <summary>Transform del control instanciado (null hasta <see cref="Build"/>).</summary>
        public Transform ControllerTransform => m_ControllerTransform;

        /// <summary>Transform del botón 3D del modo Pulsar (null fuera de ese modo).</summary>
        public Transform ButtonTransform => m_ButtonTransform;

        /// <summary>Renderer de la pieza pintada, o null si no hay pieza o no se encontró.</summary>
        public Renderer HighlightedRenderer => m_HighlightedRenderer;

        /// <summary>Material instanciado (copia) de la pieza pintada, o null.</summary>
        public Material HighlightedMaterial => m_HighlightedRenderer != null ? m_HighlightedRenderer.sharedMaterial : null;

        private void Awake()
        {
            Build();
        }

        private void OnDestroy()
        {
            DestroyInstancedMaterials();
        }

        private void Update()
        {
            if (!m_PressAnimation || m_ControllerTransform == null) return;
            EvaluateAt(Time.unscaledTime);
        }

        /// <summary>(Re)arma la ilustración. Idempotente: reemplaza lo armado antes.</summary>
        public void Build()
        {
            Teardown();
            if (m_ControllerModel == null) return;

            var rootGo = new GameObject(ROOT_NAME);
            rootGo.layer = gameObject.layer;
            m_IllustrationRoot = rootGo.transform;
            m_IllustrationRoot.SetParent(transform, false);
            m_IllustrationRoot.localScale = Vector3.one * m_ModelScale;
            m_IllustrationRoot.localRotation = Quaternion.Euler(m_ModelEuler);

            GameObject model = Instantiate(m_ControllerModel, m_IllustrationRoot);
            model.name = "Controller";
            m_ControllerTransform = model.transform;
            m_ControllerTransform.localPosition = Vector3.zero;
            m_ControllerTransform.localRotation = Quaternion.identity;
            SetLayerRecursive(model, gameObject.layer);

            // Centra el modelo en la ilustración: el origen del FBX queda en la empuñadura, no en el centro del control.
            m_ModelCenterOffset = -ComputeLocalCenter(m_IllustrationRoot, model);
            m_ControllerTransform.localPosition = m_ModelCenterOffset;

            PaintPiece(model.transform);

            if (m_PressAnimation)
            {
                BuildButton();
                EvaluateAt(0f);
            }
        }

        private void PaintPiece(Transform modelRoot)
        {
            if (string.IsNullOrEmpty(m_PieceName)) return;

            Transform piece = FindDeep(modelRoot, m_PieceName);
            if (piece == null) return;

            m_HighlightedRenderer = piece.GetComponent<Renderer>();
            if (m_HighlightedRenderer == null || m_HighlightedRenderer.sharedMaterial == null) return;

            // Copia en memoria: no se toca el material del asset compartido.
            Material copy = new Material(m_HighlightedRenderer.sharedMaterial) { name = m_PieceName + " (Highlight)" };
            copy.SetColor(BaseColorId, m_HighlightColor);
            copy.color = m_HighlightColor;
            copy.EnableKeyword("_EMISSION");
            copy.SetColor(EmissionColorId, m_HighlightColor * m_EmissionIntensity);
            m_HighlightedRenderer.sharedMaterial = copy;
            m_InstancedMaterials.Add(copy);
        }

        private void BuildButton()
        {
            // Cilindro simple con el eje a lo largo de Z (la cara plana mira al control, que se acerca desde ese lado).
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            button.name = "Button3D";
            button.layer = gameObject.layer;
            var collider = button.GetComponent<Collider>();
            if (collider != null) DestroyUnityObject(collider);

            button.transform.SetParent(m_IllustrationRoot, false);
            button.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // El cilindro primitivo mide 1 de diámetro y 2 de alto: se escala a ~5 cm de diámetro y BUTTON_HEIGHT de alto.
            button.transform.localScale = new Vector3(0.05f, BUTTON_HEIGHT * 0.5f, 0.05f);
            m_ButtonRestLocalPosition = new Vector3(0f, 0f, m_ApproachDistance * 0.5f + BUTTON_HEIGHT * 0.5f);
            button.transform.localPosition = m_ButtonRestLocalPosition;

            var rend = button.GetComponent<Renderer>();
            Material source = m_HighlightedRenderer != null ? m_HighlightedRenderer.sharedMaterial : FirstMaterialOf(m_ControllerTransform);
            if (rend != null && source != null)
            {
                Material mat = new Material(source) { name = "Button3D (Instance)" };
                mat.SetColor(BaseColorId, m_ButtonColor);
                mat.color = m_ButtonColor;
                mat.DisableKeyword("_EMISSION");
                mat.SetColor(EmissionColorId, Color.black);
                rend.sharedMaterial = mat;
                m_InstancedMaterials.Add(mat);
            }

            m_ButtonTransform = button.transform;

            // Centra el conjunto control + botón (no solo el control).
            float restZ = m_ButtonRestLocalPosition.z - BUTTON_HEIGHT * 0.5f - CONTROLLER_TIP - m_ApproachDistance * 0.5f;
            m_AssemblyZShift = -(restZ + m_ButtonRestLocalPosition.z) * 0.5f;
        }

        private static Vector3 ComputeLocalCenter(Transform root, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return Vector3.zero;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return root.InverseTransformPoint(bounds.center);
        }

        private static Material FirstMaterialOf(Transform root)
        {
            if (root == null) return null;
            Renderer r = root.GetComponentInChildren<Renderer>();
            return r != null ? r.sharedMaterial : null;
        }

        /// <summary>
        /// Posiciona control y botón según el instante <paramref name="time"/> del ciclo (loop). Fases (fracción del ciclo):
        /// 0–25% se acerca, 25–32% hunde el botón, 32–40% suelta, 40–62% se aleja, 62–100% espera en reposo.
        /// </summary>
        public void EvaluateAt(float time)
        {
            if (m_ControllerTransform == null || !m_PressAnimation) return;

            float cycle = Mathf.Max(0.1f, m_CycleSeconds);
            float u = Mathf.Repeat(time, cycle) / cycle;

            float buttonFaceZ = m_ButtonRestLocalPosition.z - BUTTON_HEIGHT * 0.5f;
            float touchZ = buttonFaceZ - CONTROLLER_TIP;
            float restZ = touchZ - m_ApproachDistance * 0.5f;

            float controllerZ;
            float sink = 0f;
            if (u < 0.25f)
            {
                controllerZ = Mathf.Lerp(restZ, touchZ, Smooth(u / 0.25f));
            }
            else if (u < 0.32f)
            {
                sink = Smooth((u - 0.25f) / 0.07f);
                controllerZ = touchZ + sink * m_ButtonTravel;
            }
            else if (u < 0.40f)
            {
                sink = 1f - Smooth((u - 0.32f) / 0.08f);
                controllerZ = touchZ + sink * m_ButtonTravel;
            }
            else if (u < 0.62f)
            {
                controllerZ = Mathf.Lerp(touchZ, restZ, Smooth((u - 0.40f) / 0.22f));
            }
            else
            {
                controllerZ = restZ;
            }

            // El frente del modelo (el extremo del anillo) apunta a -Z: se espeja el eje de la animación.
            m_ControllerTransform.localPosition = m_ModelCenterOffset + new Vector3(0f, 0f, -(controllerZ + m_AssemblyZShift));
            if (m_ButtonTransform != null)
            {
                m_ButtonTransform.localPosition = new Vector3(0f, 0f, -(m_ButtonRestLocalPosition.z + sink * m_ButtonTravel + m_AssemblyZShift));
            }
        }

        private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        private void Teardown()
        {
            DestroyInstancedMaterials();
            m_HighlightedRenderer = null;
            m_ControllerTransform = null;
            m_ButtonTransform = null;

            // Elimina también lo armado en una sesión anterior (guardado con la escena) para no duplicar.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == ROOT_NAME) DestroyUnityObject(child.gameObject);
            }
            m_IllustrationRoot = null;
        }

        private void DestroyInstancedMaterials()
        {
            for (int i = 0; i < m_InstancedMaterials.Count; i++)
            {
                if (m_InstancedMaterials[i] != null) DestroyUnityObject(m_InstancedMaterials[i]);
            }
            m_InstancedMaterials.Clear();
        }

        private static void DestroyUnityObject(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
