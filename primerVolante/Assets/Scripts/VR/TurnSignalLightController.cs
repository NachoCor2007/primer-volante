using System;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controla el parpadeo de las luces de guiño izquierda/derecha del vehículo,
    /// según el estado reportado por VehicleController.CurrentTurnSignal.
    /// </summary>
    [DefaultExecutionOrder(-10)]
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

        /// <summary>
        /// Fase actual del parpadeo (true = encendido). Reloj único para luces, tablero y audio.
        /// </summary>
        public bool IsBlinkOn => m_BlinkOn;

        /// <summary>
        /// Indica si el lado izquierdo está activo (guiño izquierdo o balizas).
        /// </summary>
        public bool IsLeftActive => m_VehicleController != null &&
            (m_VehicleController.IsHazardActive || m_LastAppliedSignal == TurnSignalState.Left);

        /// <summary>
        /// Indica si el lado derecho está activo (guiño derecho o balizas).
        /// </summary>
        public bool IsRightActive => m_VehicleController != null &&
            (m_VehicleController.IsHazardActive || m_LastAppliedSignal == TurnSignalState.Right);

        /// <summary>
        /// Indica si hay algún guiño o las balizas activas.
        /// </summary>
        public bool IsAnyActive => IsLeftActive || IsRightActive;

        /// <summary>
        /// Se dispara en cada cambio de fase (true = encendido), incluido el arranque en "on" al
        /// activar y el paso a apagado al desactivar.
        /// </summary>
        public event Action<bool> OnBlinkPhaseChanged;

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

                bool nowActive = hazard || (signal != TurnSignalState.Off);
                bool previousPhase = m_BlinkOn;
                m_BlinkOn = nowActive;
                ApplyBlinkState();

                // Al activar: arranca en "on". Al desactivar: pasa a "off" (si estaba encendido).
                if (nowActive || previousPhase)
                    OnBlinkPhaseChanged?.Invoke(m_BlinkOn);
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
                OnBlinkPhaseChanged?.Invoke(m_BlinkOn);
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
