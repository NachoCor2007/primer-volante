using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controla el parpadeo de las luces de guiño izquierda/derecha del vehículo,
    /// según el estado reportado por VehicleController.CurrentTurnSignal.
    /// </summary>
    public class TurnSignalLightController : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Controlador del vehículo. Se autodetecta en los padres si se deja vacío.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Luces del lado izquierdo (delantera, trasera, etc.).")]
        [SerializeField] private BlinkerLight[] m_LeftLights;

        [Tooltip("Luces del lado derecho (delantera, trasera, etc.).")]
        [SerializeField] private BlinkerLight[] m_RightLights;

        [Header("Parpadeo")]
        [Tooltip("Frecuencia de parpadeo en Hz (ciclos completos encendido+apagado por segundo).")]
        [SerializeField] private float m_BlinkFrequencyHz = 1.5f;

        private bool m_BlinkOn;
        private float m_BlinkTimer;
        private TurnSignalState m_LastAppliedSignal = TurnSignalState.Off;
        private bool m_LastHazardActive = false;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
        }

        private void Update()
        {
            if (m_VehicleController == null) return;

            TurnSignalState signal = m_VehicleController.CurrentTurnSignal;
            bool hazard = m_VehicleController.IsHazardActive;

            if (signal != m_LastAppliedSignal || hazard != m_LastHazardActive)
            {
                m_LastAppliedSignal = signal;
                m_LastHazardActive = hazard;
                m_BlinkTimer = 0f;
                m_BlinkOn = true;
                ApplyBlinkState();
            }

            bool isActive = hazard || (signal != TurnSignalState.Off);
            if (!isActive) return;

            float halfPeriod = m_BlinkFrequencyHz > 0f ? 0.5f / m_BlinkFrequencyHz : 0f;
            if (halfPeriod <= 0f) return;

            m_BlinkTimer += Time.deltaTime;
            if (m_BlinkTimer >= halfPeriod)
            {
                m_BlinkTimer -= halfPeriod;
                m_BlinkOn = !m_BlinkOn;
                ApplyBlinkState();
            }
        }

        private void ApplyBlinkState()
        {
            bool isHazard = m_VehicleController != null && m_VehicleController.IsHazardActive;
            bool leftOn = (isHazard || m_LastAppliedSignal == TurnSignalState.Left) && m_BlinkOn;
            bool rightOn = (isHazard || m_LastAppliedSignal == TurnSignalState.Right) && m_BlinkOn;

            SetLights(m_LeftLights, leftOn);
            SetLights(m_RightLights, rightOn);
        }

        private static void SetLights(BlinkerLight[] lights, bool on)
        {
            if (lights == null) return;
            foreach (var light in lights)
            {
                if (light != null) light.SetOn(on);
            }
        }
    }
}
