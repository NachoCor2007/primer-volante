using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controla los faros delanteros reales del vehículo (objetos Light), según el
    /// HeadlightState reportado por VehicleController.CurrentHeadlights.
    /// </summary>
    public class HeadlightController : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Controlador del vehículo. Se autodetecta en los padres si se deja vacío.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Luces delanteras del vehículo (una o más).")]
        [SerializeField] private Light[] m_Lights;

        [Header("Luces Bajas (Low)")]
        [SerializeField] private float m_LowIntensity = 4f;
        [SerializeField] private float m_LowRange = 12f;
        [SerializeField] private Color m_LowColor = new Color(1f, 0.95f, 0.85f);

        [Header("Luces Altas (High)")]
        [SerializeField] private float m_HighIntensity = 9f;
        [SerializeField] private float m_HighRange = 25f;
        [SerializeField] private Color m_HighColor = new Color(0.9f, 0.95f, 1f);

        private HeadlightState m_LastApplied = (HeadlightState)(-1);

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();

            if (m_Lights == null || m_Lights.Length == 0)
                m_Lights = GetComponentsInChildren<Light>(true);
        }

        private void Update()
        {
            if (m_VehicleController == null) return;

            HeadlightState state = m_VehicleController.CurrentHeadlights;
            if (state != m_LastApplied)
            {
                m_LastApplied = state;
                ApplyState(state);
            }
        }

        private void ApplyState(HeadlightState state)
        {
            if (m_Lights == null) return;

            bool on = state != HeadlightState.Off;
            float intensity = state == HeadlightState.High ? m_HighIntensity : m_LowIntensity;
            float range = state == HeadlightState.High ? m_HighRange : m_LowRange;
            Color color = state == HeadlightState.High ? m_HighColor : m_LowColor;

            foreach (var light in m_Lights)
            {
                if (light == null) continue;
                light.enabled = on;
                light.intensity = intensity;
                light.range = range;
                light.color = color;
            }
        }
    }
}
