using System;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Simula las RPM del motor a partir del estado del <see cref="VehicleController"/>
    /// (que es cinemático y no calcula RPM). No reproduce audio: lo consumen
    /// <c>VehicleEngineAudio</c> y, a futuro, el tacómetro (US13).
    /// </summary>
    public class VehicleEngineSimulator : MonoBehaviour
    {
        /// <summary>
        /// Resultado del cálculo puro del objetivo de RPM.
        /// </summary>
        public struct RpmTarget
        {
            public float TargetRpm;
            public int VirtualGear;
        }

        [Header("Referencias")]
        [Tooltip("Controlador del vehículo. Se autodetecta en los padres si se deja vacío.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Perfil de audio con los parámetros del RPM simulado.")]
        [SerializeField] private VehicleAudioProfile m_Profile;

        private float m_CurrentRpm;
        private float m_EngineLoad;
        private int m_CurrentVirtualGear;
        private float m_ShiftFallTimer;

        /// <summary>RPM actuales (suavizadas).</summary>
        public float CurrentRpm => m_CurrentRpm;

        /// <summary>RPM normalizadas 0..1 entre 0 y la línea roja.</summary>
        public float NormalizedRpm => Settings.MaxRpm > 0f ? Mathf.Clamp01(m_CurrentRpm / Settings.MaxRpm) : 0f;

        /// <summary>Carga del motor 0..1 (acelerador suavizado).</summary>
        public float EngineLoad => m_EngineLoad;

        /// <summary>Marcha virtual actual: 1..N adelante, -1 reversa, 0 sin marcha (P/N/apagado).</summary>
        public int CurrentVirtualGear => m_CurrentVirtualGear;

        /// <summary>Se dispara al cambiar de marcha virtual (argumento = marcha nueva).</summary>
        public event Action<int> OnVirtualGearShift;

        public VehicleAudioProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        private static readonly EngineSimulationSettings s_DefaultSettings = new EngineSimulationSettings();

        private EngineSimulationSettings Settings =>
            m_Profile != null && m_Profile.EngineSimulation != null ? m_Profile.EngineSimulation : s_DefaultSettings;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
        }

        private void Update()
        {
            if (!Application.isPlaying || m_VehicleController == null) return;

            EngineSimulationSettings s = Settings;
            float dt = Time.deltaTime;

            EngineState state = m_VehicleController.CurrentEngineState;
            float throttle = m_VehicleController.ThrottleValue;

            RpmTarget target = ComputeTargetRpm(
                s, state, m_VehicleController.CurrentGear, m_VehicleController.CurrentSpeedKmh,
                throttle, m_VehicleController.HandbrakeEngagement, m_CurrentVirtualGear);

            if (target.VirtualGear != m_CurrentVirtualGear)
            {
                if (target.VirtualGear > m_CurrentVirtualGear && m_CurrentVirtualGear > 0)
                    m_ShiftFallTimer = s.ShiftFallDuration;

                m_CurrentVirtualGear = target.VirtualGear;
                OnVirtualGearShift?.Invoke(m_CurrentVirtualGear);
            }

            m_ShiftFallTimer = Mathf.Max(0f, m_ShiftFallTimer - dt);
            m_CurrentRpm = StepRpm(s, m_CurrentRpm, target.TargetRpm, dt, state, m_ShiftFallTimer > 0f);

            float targetLoad = state == EngineState.Running ? Mathf.Clamp01(throttle) : 0f;
            m_EngineLoad = Mathf.Lerp(m_EngineLoad, targetLoad, 1f - Mathf.Exp(-s.LoadSmoothing * dt));
        }

        /// <summary>
        /// Cálculo puro (sin estado) del objetivo de RPM y de la marcha virtual.
        /// </summary>
        /// <param name="s">Parámetros de simulación.</param>
        /// <param name="state">Estado del motor.</param>
        /// <param name="gear">Marcha de la palanca (P/R/N/D).</param>
        /// <param name="speedKmh">Velocidad en km/h (>= 0).</param>
        /// <param name="throttle">Acelerador 0..1.</param>
        /// <param name="handbrake">Accionamiento del freno de mano 0..1.</param>
        /// <param name="currentVirtualGear">Marcha virtual actual (para la histéresis al bajar).</param>
        public static RpmTarget ComputeTargetRpm(
            EngineSimulationSettings s, EngineState state, GearState gear,
            float speedKmh, float throttle, float handbrake, int currentVirtualGear)
        {
            throttle = Mathf.Clamp01(throttle);
            speedKmh = Mathf.Max(0f, speedKmh);

            if (state == EngineState.Off)
                return new RpmTarget { TargetRpm = 0f, VirtualGear = 0 };

            if (state == EngineState.Cranking)
                return new RpmTarget { TargetRpm = s.CrankRpm, VirtualGear = 0 };

            // Running
            if (gear == GearState.Park || gear == GearState.Neutral)
            {
                float free = Mathf.Lerp(s.IdleRpm, s.FreeRevMaxRpm, throttle);
                return new RpmTarget { TargetRpm = free, VirtualGear = 0 };
            }

            // D / R con el freno de mano puesto: el auto no avanza y las RPM suben contra el freno.
            if (handbrake >= s.HandbrakeHoldThreshold)
            {
                float stall = Mathf.Lerp(s.IdleRpm, s.HandbrakeStallMaxRpm, throttle);
                return new RpmTarget { TargetRpm = stall, VirtualGear = gear == GearState.Reverse ? -1 : 1 };
            }

            if (gear == GearState.Reverse)
            {
                float top = Mathf.Max(1f, s.MaxSpeedKmh * s.ReverseSpeedFraction);
                float rpm = s.IdleRpm + Mathf.Clamp01(speedKmh / top) * (s.UpshiftRpm - s.IdleRpm)
                            + throttle * s.ThrottleLoadRpmBonus;
                return new RpmTarget { TargetRpm = Mathf.Min(rpm, s.MaxRpm), VirtualGear = -1 };
            }

            // Drive: caja automática virtual con marchas repartidas en la velocidad máxima.
            int gears = Mathf.Max(1, s.ForwardGears);
            float gearSpan = s.MaxSpeedKmh / gears;

            int g = currentVirtualGear > 0 ? Mathf.Min(currentVirtualGear, gears) : 1;
            // Subir mientras se supere el límite superior de la marcha actual.
            while (g < gears && speedKmh >= g * gearSpan) g++;
            // Bajar solo con histéresis respecto del límite inferior.
            while (g > 1 && speedKmh < (g - 1) * gearSpan - s.DownshiftHysteresisKmh) g--;

            float gearTop = g * gearSpan;
            float rpmDrive = s.IdleRpm + Mathf.Clamp01(speedKmh / gearTop) * (s.UpshiftRpm - s.IdleRpm)
                             + throttle * s.ThrottleLoadRpmBonus;
            return new RpmTarget { TargetRpm = Mathf.Min(rpmDrive, s.MaxRpm), VirtualGear = g };
        }

        /// <summary>
        /// Paso de suavizado (función pura): sube y baja con tasas distintas.
        /// </summary>
        public static float StepRpm(
            EngineSimulationSettings s, float current, float target, float dt, EngineState state, bool shiftDrop)
        {
            float rate;
            if (target >= current)
                rate = s.RiseRate;
            else if (state == EngineState.Off)
                rate = s.ShutdownFallRate;
            else
                rate = shiftDrop ? s.ShiftFallRate : s.FallRate;

            return Mathf.MoveTowards(current, target, rate * dt);
        }
    }
}
