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

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
        }

        private void Update()
        {
            if (m_VehicleController == null) return;

            TurnSignalState signal = m_VehicleController.CurrentTurnSignal;

            if (signal != m_LastAppliedSignal)
            {
                m_LastAppliedSignal = signal;
                m_BlinkTimer = 0f;
                m_BlinkOn = true;
                ApplyBlinkState();
            }

            if (signal == TurnSignalState.Off) return;

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
            bool leftOn = m_LastAppliedSignal == TurnSignalState.Left && m_BlinkOn;
            bool rightOn = m_LastAppliedSignal == TurnSignalState.Right && m_BlinkOn;

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
