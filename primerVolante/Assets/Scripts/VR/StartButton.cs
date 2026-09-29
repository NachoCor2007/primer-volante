using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Botón de arranque/apagado del motor. Delega en VehicleController.TryToggleEngine.
    /// Tanto para arrancar como para apagar se exige la palanca en Park y el freno a fondo.
    /// </summary>
    public class StartButton : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Controlador del vehículo a encender/apagar. Si se deja vacío, se busca en los padres.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Header("Estado Visual")]
        [Tooltip("Renderer cuyo color cambia según el estado del motor. Si se deja vacío, se busca en los hijos.")]
        [SerializeField] private Renderer m_TargetRenderer;

        [Tooltip("Color del botón con el motor apagado.")]
        [SerializeField] private Color m_EngineOffColor = Color.white;

        [Tooltip("Color del botón durante el arranque (Cranking).")]
        [SerializeField] private Color m_EngineCrankingColor = new Color(1f, 0.65f, 0.1f);

        [Tooltip("Color del botón con el motor encendido.")]
        [SerializeField] private Color m_EngineOnColor = new Color(0.1f, 0.9f, 0.2f);

        [Header("Debugging / Logs")]
        [SerializeField] private bool m_EnableDebugLogs = true;

        private Material m_InstancedMaterial;
        private XRSimpleInteractable m_Interactable;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
        }

        private void Start()
        {
            if (m_TargetRenderer == null)
                m_TargetRenderer = GetComponentInChildren<Renderer>();

            if (m_TargetRenderer != null)
                m_InstancedMaterial = m_TargetRenderer.material;
            else if (m_EnableDebugLogs)
                Debug.LogWarning($"[StartButton:{gameObject.name}] No se encontró Renderer para aplicar el color de estado.");

            m_Interactable = GetComponent<XRSimpleInteractable>();
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.AddListener(OnSelectEntered);
            }
            else if (m_EnableDebugLogs)
            {
                Debug.LogWarning($"[StartButton:{gameObject.name}] No se encontró XRSimpleInteractable en este objeto.");
            }

            UpdateVisual();
        }

        private void Update()
        {
            UpdateVisual();
        }

        private void OnDestroy()
        {
            if (m_Interactable != null)
                m_Interactable.selectEntered.RemoveListener(OnSelectEntered);

            if (m_InstancedMaterial != null)
                Destroy(m_InstancedMaterial);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            ToggleEngine();
        }

        /// <summary>
        /// Intenta alternar el estado del motor. La validación (Park + freno a fondo) vive en
        /// <see cref="VehicleController.TryToggleEngine"/>.
        /// </summary>
        public void ToggleEngine()
        {
            if (m_VehicleController == null)
            {
                if (m_EnableDebugLogs)
                    Debug.LogWarning($"[StartButton:{gameObject.name}] No hay VehicleController asignado.");
                return;
            }

            bool accepted = m_VehicleController.TryToggleEngine();
            UpdateVisual();

            if (m_EnableDebugLogs)
                Debug.Log($"[StartButton:{gameObject.name}] Pulsación {(accepted ? "aceptada ✅" : "rechazada ⛔")} (estado: {m_VehicleController.CurrentEngineState}).");
        }

        private void UpdateVisual()
        {
            if (m_InstancedMaterial == null || m_VehicleController == null) return;
            switch (m_VehicleController.CurrentEngineState)
            {
                case EngineState.Running:
                    m_InstancedMaterial.color = m_EngineOnColor;
                    break;
                case EngineState.Cranking:
                    m_InstancedMaterial.color = m_EngineCrankingColor;
                    break;
                default:
                    m_InstancedMaterial.color = m_EngineOffColor;
                    break;
            }
        }
    }
}
