using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Botón de arranque/apagado del motor. Alterna VehicleController.IsEngineRunning
    /// y sólo permite el cambio de estado si la palanca de cambios está en Park o Neutral.
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
        /// Intenta alternar el estado del motor. Bloqueado si se intenta encender fuera de Park/Neutral.
        /// </summary>
        public void ToggleEngine()
        {
            if (m_VehicleController == null)
            {
                if (m_EnableDebugLogs)
                    Debug.LogWarning($"[StartButton:{gameObject.name}] No hay VehicleController asignado.");
                return;
            }

            bool wantsToStart = !m_VehicleController.IsEngineRunning;
            if (wantsToStart && !IsGearSafeToStart())
            {
                if (m_EnableDebugLogs)
                    Debug.Log($"[StartButton:{gameObject.name}] Encendido bloqueado: la palanca debe estar en Park o Neutral (actual: {m_VehicleController.CurrentGear}).");
                return;
            }

            m_VehicleController.SetEngineRunning(wantsToStart);
            UpdateVisual();

            if (m_EnableDebugLogs)
                Debug.Log($"[StartButton:{gameObject.name}] Motor {(wantsToStart ? "ENCENDIDO 🟢" : "APAGADO 🔴")}.");
        }

        private bool IsGearSafeToStart()
        {
            GearState gear = m_VehicleController.CurrentGear;
            return gear == GearState.Park || gear == GearState.Neutral;
        }

        private void UpdateVisual()
        {
            if (m_InstancedMaterial == null || m_VehicleController == null) return;
            m_InstancedMaterial.color = m_VehicleController.IsEngineRunning ? m_EngineOnColor : m_EngineOffColor;
        }
    }
}
