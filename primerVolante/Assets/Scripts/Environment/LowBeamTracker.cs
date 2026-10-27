using PrimerVolante.VR;

namespace PrimerVolante.Environment
{
    /// <summary>Una falta de luces bajas detectada por el <see cref="LowBeamTracker"/>.</summary>
    public readonly struct LowBeamFault
    {
        /// <summary>Modo de luces en el momento de la falta (nunca <see cref="HeadlightMode.LowBeam"/>).</summary>
        public readonly HeadlightMode Mode;

        /// <summary>true si en este encendido el auto todavía no había recorrido distancia con las bajas puestas ("Arrancaste").</summary>
        public readonly bool AtStart;

        public LowBeamFault(HeadlightMode mode, bool atStart)
        {
            Mode = mode;
            AtStart = atStart;
        }

        /// <summary>Texto del parámetro <c>when</c>: "Arrancaste" o "Circulaste".</summary>
        public string WhenText => LowBeamTracker.WhenText(AtStart);

        /// <summary>Texto del parámetro <c>lights</c> según el modo de luces.</summary>
        public string LightsText => LowBeamTracker.LightsText(Mode);
    }

    /// <summary>
    /// Lógica pura (sin escena, testeable) de la infracción de circular sin luces bajas. La usa
    /// <see cref="LowBeamDetector"/>: en cada paso recibe el modo de luces y la distancia recorrida y devuelve una
    /// falta (o null) como máximo una vez por tramo sin bajas.
    /// </summary>
    /// <remarks>
    /// <para><b>Regla.</b> Con el motor en marcha (<see cref="EngineStarted"/>), cada paso suma la distancia y el
    /// tiempo mientras el modo no sea <see cref="HeadlightMode.LowBeam"/> (solo cuentan las bajas: las altas, las de
    /// posición y apagadas son falta). La falta se informa <b>una</b> vez, cuando se cumplen las dos condiciones:
    /// el acumulado llega a <see cref="ThresholdM"/> <b>y</b> el tiempo seguido sin bajas llega a
    /// <see cref="MinTimeS"/> (así un parpadeo de altas a alta velocidad no es falta). Después el tracker queda
    /// bloqueado (<see cref="IsLatched"/>).</para>
    /// <para><b>Rearme.</b> Un paso con <see cref="HeadlightMode.LowBeam"/> vuelve distancia y tiempo a 0 y
    /// desbloquea. Si después se cambia de modo y se cumplen de nuevo las dos condiciones, salta otra vez. Detenerse
    /// no reinicia nada: la distancia 0 no suma, pero el tiempo sí corre. Un paso con distancia y tiempo 0 sirve para
    /// observar el modo sin acumular (el detector suspendido): pasar a bajas igual rearma.</para>
    /// <para><b>Motor.</b> <see cref="EngineStopped"/> (y <see cref="Reset"/>) reinicia todo, incluido el estado "ya
    /// circuló con bajas". Con el motor apagado o arrancando no se acumula.</para>
    /// <para><b>Arrancaste o Circulaste.</b> <see cref="LowBeamFault.AtStart"/> es true si en este encendido todavía
    /// no se sumó distancia mayor a 0 con las bajas puestas (<see cref="HasDrivenWithLowBeam"/>).</para>
    /// <para>Sin allocations.</para>
    /// </remarks>
    public sealed class LowBeamTracker
    {
        /// <summary>Umbral por defecto (m) de distancia sin bajas antes de informar la falta.</summary>
        public const float DefaultThresholdM = 2f;

        /// <summary>Tiempo mínimo por defecto (s) sin bajas antes de informar la falta.</summary>
        public const float DefaultMinTimeS = 1f;

        public const string WhenAtStart = "Arrancaste";
        public const string WhenWhileDriving = "Circulaste";
        public const string LightsOff = "con las luces apagadas";
        public const string LightsPosition = "solo con las luces de posición";
        public const string LightsHigh = "con las luces altas";

        /// <summary>Tolerancia al comparar acumulados de punto flotante contra los umbrales.</summary>
        private const float k_Epsilon = 1e-4f;

        private bool m_EngineRunning;

        /// <summary>Metros recorridos sin bajas desde el último rearme.</summary>
        public float AccumulatedM { get; private set; }

        /// <summary>Segundos seguidos sin bajas con el motor en marcha desde el último rearme (cuenta aunque el auto esté detenido).</summary>
        public float AccumulatedS { get; private set; }

        /// <summary>true si ya se informó la falta de este tramo y no se vuelve a informar hasta poner las bajas.</summary>
        public bool IsLatched { get; private set; }

        /// <summary>true si en este encendido ya se recorrió distancia con las bajas puestas.</summary>
        public bool HasDrivenWithLowBeam { get; private set; }

        /// <summary>true mientras el motor está en marcha (entre <see cref="EngineStarted"/> y <see cref="EngineStopped"/>).</summary>
        public bool IsEngineRunning => m_EngineRunning;

        /// <summary>Metros sin bajas a partir de los cuales hay falta (junto con <see cref="MinTimeS"/>).</summary>
        public float ThresholdM { get; set; } = DefaultThresholdM;

        /// <summary>Segundos seguidos sin bajas a partir de los cuales hay falta (junto con <see cref="ThresholdM"/>).</summary>
        public float MinTimeS { get; set; } = DefaultMinTimeS;

        /// <summary>El motor pasó a Running: empieza un encendido nuevo. Llamarlo con el motor ya en marcha no hace nada.</summary>
        public void EngineStarted()
        {
            if (m_EngineRunning) return;

            ClearState();
            m_EngineRunning = true;
        }

        /// <summary>El motor se apagó: se reinicia todo y deja de acumular.</summary>
        public void EngineStopped()
        {
            Reset();
        }

        /// <summary>Descarta todo el estado, incluido que el motor estaba en marcha.</summary>
        public void Reset()
        {
            ClearState();
            m_EngineRunning = false;
        }

        /// <summary>
        /// Procesa un paso de física. <paramref name="distanceM"/> es la distancia recorrida en el paso (adelante o
        /// en reversa, nunca negativa) y <paramref name="deltaTimeS"/> su duración. Devuelve la falta la primera vez
        /// que se cumplen a la vez el umbral de distancia y el de tiempo.
        /// </summary>
        public LowBeamFault? Step(HeadlightMode mode, float distanceM, float deltaTimeS)
        {
            if (!m_EngineRunning) return null;

            if (mode == HeadlightMode.LowBeam)
            {
                AccumulatedM = 0f;
                AccumulatedS = 0f;
                IsLatched = false;
                if (distanceM > 0f) HasDrivenWithLowBeam = true;
                return null;
            }

            if (IsLatched) return null;

            if (distanceM > 0f) AccumulatedM += distanceM;
            if (deltaTimeS > 0f) AccumulatedS += deltaTimeS;
            if (AccumulatedM + k_Epsilon < ThresholdM || AccumulatedS + k_Epsilon < MinTimeS) return null;

            IsLatched = true;
            return new LowBeamFault(mode, !HasDrivenWithLowBeam);
        }

        /// <summary>Texto del parámetro <c>when</c>.</summary>
        public static string WhenText(bool atStart) => atStart ? WhenAtStart : WhenWhileDriving;

        /// <summary>Texto del parámetro <c>lights</c> (el modo es siempre uno que no sea bajas: Off, posición o altas).</summary>
        public static string LightsText(HeadlightMode mode)
        {
            switch (mode)
            {
                case HeadlightMode.Position: return LightsPosition;
                case HeadlightMode.HighBeam: return LightsHigh;
                default: return LightsOff;
            }
        }

        private void ClearState()
        {
            AccumulatedM = 0f;
            AccumulatedS = 0f;
            IsLatched = false;
            HasDrivenWithLowBeam = false;
        }
    }
}
