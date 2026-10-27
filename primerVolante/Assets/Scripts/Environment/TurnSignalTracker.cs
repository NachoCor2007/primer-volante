using PrimerVolante.VR;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Lógica pura (sin escena, testeable) de la infracción de giro sin señalizar. Lo usa
    /// <see cref="TurnSignalDetector"/>: en cada paso recibe el rumbo del auto, la distancia recorrida, el estado
    /// del guiño y si está en una zona exenta, y devuelve una falta (o null) exactamente una vez por giro.
    /// </summary>
    /// <remarks>
    /// <para><b>Qué es un giro.</b> Se guarda un rumbo de referencia mientras el auto va derecho. El desvío neto
    /// <c>δ = DeltaAngle(referencia, rumbo)</c> es positivo para un giro a la derecha (el yaw crece en sentido
    /// horario visto desde arriba). Mientras |δ| &lt; <see cref="TurnStartDeg"/> se registra la distancia
    /// acumulada como posible inicio del giro y el guiño de ese momento. Cuando |δ| llega a
    /// <see cref="TurnDetectDeg"/> hay un giro en la dirección de sign(δ) y se evalúa <b>una sola vez</b>. Un
    /// cambio de carril (±15° y vuelta) no llega a ese umbral y no es un giro.</para>
    /// <para><b>Referencia.</b> Mientras no hay un giro en curso y el rumbo está estable (ver Fin del giro), la
    /// referencia pasa a ser el rumbo actual y se actualiza el posible inicio: así un conductor que endereza
    /// después de un giro, o que deriva lentamente, no deja el inicio pegado al giro anterior.</para>
    /// <para><b>Anticipación estricta.</b> La ventana es <c>[max(inicio − </c><see cref="AnticipationM"/><c>, borde +
    /// margen), inicio]</c>. Está bien señalizado solo si el guiño fue igual a la dirección del giro en <b>todas</b>
    /// las muestras de la ventana, incluida la del inicio. La ventana se recorta para no exigir lo que el conductor
    /// no pudo hacer antes. Borde de reset (arranque, supresión, reversa sin giro): una muestra
    /// (<see cref="SampleSpacingM"/>) después. Borde de fin de giro: <see cref="ReactionMarginM"/> (15 m) después,
    /// el tiempo que tarda en volver a poner el guiño tras el auto-cancelado de la palanca. Si una pausa reinicia
    /// el rastreo dentro de ese margen, lo que quedaba se conserva ("margen pendiente") y la ventana del próximo
    /// giro no empieza antes. Si la ventana queda vacía alcanza con el guiño al inicio. El guiño después del inicio no importa (el <c>VRTurnSignal</c> se auto-cancela en el giro).</para>
    /// <para><b>Precedencia del motivo</b> (<see cref="TurnSignalFaultReason"/>), según el guiño al inicio:
    /// (1) igual a la dirección del giro: si toda la ventana fue correcta no hay falta; si no, <c>TooLate</c>
    /// (puesto tarde o con cortes); (2) hacia el lado contrario: <c>WrongSide</c>, aunque antes hubiera estado
    /// bien; (3) apagado: <c>TooLate</c> si hubo alguna muestra correcta en la ventana (se apagó antes de girar),
    /// y <c>NoSignal</c> si nunca estuvo correcto.</para>
    /// <para><b>Fin del giro.</b> El rumbo es estable cuando, en los últimos <see cref="StableDistanceM"/> recorridos,
    /// el cambio neto de rumbo es menor que <see cref="StableDeg"/> y el rango (máximo menos mínimo) menor que
    /// <see cref="StableRangeDeg"/>: tolera las correcciones del volante. Al estabilizarse, el rumbo actual pasa a
    /// ser la nueva referencia y el comienzo de ese tramo queda como fin del giro (borde de la ventana del
    /// próximo). Como tope de seguridad, a los <see cref="MaxTurnDistanceM"/> de la detección el giro se da por
    /// terminado igual. Una vuelta en U da una sola falta porque el giro sigue "en curso" hasta que el rumbo se
    /// estabiliza. Un giro real (6 a 9°/m) y un cambio de carril no cumplen el criterio mientras ocurren.</para>
    /// <para><b>Pausa.</b> <see cref="Pause"/> se llama cuando el auto se frena casi del todo o va en marcha
    /// atrás. Sin giro en curso reinicia (<see cref="Reset"/>, conservando el margen de reacción pendiente): lo girado en reversa no cuenta. Con un giro ya
    /// detectado (|δ| llegó a <see cref="TurnDetectDeg"/>) conserva la referencia, el inicio y el estado
    /// "evaluado", así una maniobra de 3 puntos o una U lenta da como máximo una falta. Un giro parcial no
    /// detectado (|δ| ≥ <see cref="TurnStartDeg"/>) se conserva si la pausa es por baja velocidad, pero se descarta
    /// si fue por marcha atrás: lo rotado en reversa no se suma al próximo giro hacia adelante (estacionar).</para>
    /// <para><b>Tope de 30 m.</b> Al llegar a <see cref="MaxTurnDistanceM"/> el giro se cierra sin fijar una
    /// referencia a mitad de la curva: el tracker queda esperando estabilidad (<see cref="IsAwaitingStability"/>),
    /// no detecta giros nuevos y recién cuando el rumbo es estable la referencia pasa a ese rumbo. Una vuelta amplia
    /// o un círculo da una sola falta.</para>
    /// <para><b>Exención.</b> Si el auto estuvo en una zona exenta al inicio o durante el giro, hasta el momento de
    /// detectarlo, el giro se consume sin falta (<see cref="TurnSignalOutcome.Exempt"/>).</para>
    /// <para>Sin allocations: el historial es un buffer circular preasignado, con una muestra cada
    /// <see cref="SampleSpacingM"/> metros.</para>
    /// </remarks>
    public sealed class TurnSignalTracker
    {
        private const int k_BufferSize = 256;
        private const float k_Epsilon = 1e-4f;

        // Un rumbo a menos de esto (grados) del actual se considera ya asentado, para ubicar el fin del giro.
        private const float k_SettledDeg = 1f;

        // Historial por distancia: ring buffer de (distancia acumulada, guiño, rumbo).
        private readonly float[] m_SampleDistance = new float[k_BufferSize];
        private readonly sbyte[] m_SampleSignal = new sbyte[k_BufferSize];
        private readonly float[] m_SampleHeading = new float[k_BufferSize];
        private int m_SampleHead;
        private int m_SampleCount;
        private float m_LastSampleDistance;

        private float m_Cumulative;
        private bool m_HasReference;
        private float m_Reference;
        private float m_BoundaryDistance;

        // Distancia acumulada hasta la que no se exige guiño por el margen de reacción tras el fin de un giro. La
        // ventana del próximo giro no empieza antes. Una pausa con reinicio conserva lo que quedaba del margen.
        private float m_MarginEndDistance;

        // Tramo recto en curso: posible inicio del giro.
        private float m_StartDistance;
        private TurnSignalState m_SignalAtStart;
        private bool m_ExemptSeen;

        // Giro ya detectado, esperando que el rumbo se estabilice.
        private bool m_Turning;
        private bool m_AwaitingStability;
        private float m_DetectDistance;

        /// <summary>|δ| (grados) por debajo del cual el auto va derecho y se actualiza el posible inicio del giro.</summary>
        public float TurnStartDeg { get; set; } = 10f;

        /// <summary>|δ| (grados) a partir del cual hay un giro.</summary>
        public float TurnDetectDeg { get; set; } = 45f;

        /// <summary>Metros de anticipación exigidos al guiño antes del inicio del giro.</summary>
        public float AnticipationM { get; set; } = 20f;

        /// <summary>
        /// Metros después del fin de un giro anterior durante los cuales no se exige guiño: tiempo de reacción tras el
        /// auto-cancelado de la palanca. Solo aplica después del fin de un giro, y una pausa que reinicia
        /// (<see cref="Pause"/>) conserva lo que quedaba; <see cref="Reset"/> (arranque, supresión) no lo conserva.
        /// Nunca baja de <see cref="SampleSpacingM"/>.
        /// </summary>
        public float ReactionMarginM { get; set; } = 15f;

        /// <summary>Cambio neto máximo del rumbo (grados) en los últimos <see cref="StableDistanceM"/> para considerarlo estable.</summary>
        public float StableDeg { get; set; } = 5f;

        /// <summary>Rango máximo (máx − mín, grados) del rumbo en los últimos <see cref="StableDistanceM"/> para considerarlo estable.</summary>
        public float StableRangeDeg { get; set; } = 12f;

        /// <summary>Metros recorridos que se miran hacia atrás para decidir si el rumbo es estable.</summary>
        public float StableDistanceM { get; set; } = 5f;

        /// <summary>Metros desde la detección tras los cuales el giro se da por terminado aunque el rumbo siga inestable.</summary>
        public float MaxTurnDistanceM { get; set; } = 30f;

        /// <summary>Metros entre muestras del historial.</summary>
        public float SampleSpacingM { get; set; } = 0.5f;

        /// <summary>Último desvío δ respecto del rumbo de referencia (grados; positivo = derecha).</summary>
        public float LastDeltaDeg { get; private set; }

        /// <summary>true mientras hay un giro ya detectado que todavía no terminó.</summary>
        public bool IsTurning => m_Turning;

        /// <summary>
        /// true tras el tope de <see cref="MaxTurnDistanceM"/>: el giro está cerrado pero el rumbo todavía no es
        /// estable, así que no se detectan giros nuevos ni hay referencia nueva hasta que se estabilice.
        /// </summary>
        public bool IsAwaitingStability => m_AwaitingStability;

        /// <summary>
        /// true si hay un giro detectado, o un desvío de al menos <see cref="TurnStartDeg"/> respecto de la
        /// referencia que todavía puede convertirse en giro. Una pausa (<see cref="Pause"/>) no lo descarta.
        /// </summary>
        public bool HasTurnInProgress => m_Turning || m_AwaitingStability || (m_HasReference && Mathf.Abs(LastDeltaDeg) >= TurnStartDeg);

        /// <summary>true solo en el paso en que se detectó un giro (con falta o sin ella).</summary>
        public bool TurnDetectedThisStep { get; private set; }

        /// <summary>Qué pasó con el último giro detectado.</summary>
        public TurnSignalOutcome LastOutcome { get; private set; }

        /// <summary>Dirección del último giro detectado (Left o Right; Off si no hubo ninguno).</summary>
        public TurnSignalState LastTurnDirection { get; private set; }

        /// <summary>Última falta informada (válida si <see cref="LastOutcome"/> es <see cref="TurnSignalOutcome.Fault"/>).</summary>
        public TurnSignalFault LastFault { get; private set; }

        /// <summary>Distancia acumulada desde el último reset (m).</summary>
        public float CumulativeDistanceM => m_Cumulative;

        public TurnSignalTracker()
        {
            Reset();
        }

        /// <summary>
        /// Limpia el historial y descarta el giro en curso. El rumbo actual pasa a ser la referencia en el próximo
        /// paso. Se usa al suspender el detector y al arrancar; frenar casi del todo o ir en marcha atrás usa
        /// <see cref="Pause"/>.
        /// </summary>
        public void Reset()
        {
            m_SampleHead = 0;
            m_SampleCount = 0;
            m_LastSampleDistance = float.NegativeInfinity;
            m_Cumulative = 0f;
            m_BoundaryDistance = 0f;
            m_MarginEndDistance = 0f;
            m_HasReference = false;
            m_Turning = false;
            m_AwaitingStability = false;
            m_ExemptSeen = false;
            m_StartDistance = 0f;
            m_SignalAtStart = TurnSignalState.Off;
            m_DetectDistance = 0f;
            LastDeltaDeg = 0f;
            TurnDetectedThisStep = false;
            LastOutcome = TurnSignalOutcome.None;
            LastTurnDirection = TurnSignalState.Off;
        }

        /// <summary>
        /// El auto está casi detenido o en marcha atrás (<paramref name="reversing"/>). Sin un giro en curso
        /// (<see cref="HasTurnInProgress"/> en false) reinicia como <see cref="Reset"/>: al volver a avanzar el rumbo
        /// actual es la referencia. Con un giro ya detectado (o esperando estabilidad tras el tope) conserva la
        /// referencia, el inicio y el estado "evaluado". Un giro parcial no detectado se conserva si la pausa es
        /// por baja velocidad, pero se descarta (reinicio) si es por marcha atrás.
        /// </summary>
        public void Pause(bool reversing = false)
        {
            TurnDetectedThisStep = false;
            if (m_Turning || m_AwaitingStability) return;
            if (reversing || !HasTurnInProgress)
            {
                // El reinicio de la pausa conserva lo que quedaba del margen de reacción del giro anterior: el
                // conductor que frena un instante en la senda después de girar no pierde esos metros.
                float pendingMargin = Mathf.Max(0f, m_MarginEndDistance - m_Cumulative);
                Reset();
                m_MarginEndDistance = pendingMargin;
            }
        }

        /// <summary>
        /// Avanza un paso. <paramref name="headingDeg"/> es el yaw del auto en grados (horario visto desde arriba),
        /// <paramref name="distanceM"/> la distancia recorrida hacia adelante en el paso, <paramref name="signal"/>
        /// el estado del guiño (las balizas no cuentan) e <paramref name="inExemptZone"/> si el punto de consulta
        /// está en una zona exenta. Devuelve la falta del giro detectado en este paso o null.
        /// </summary>
        public TurnSignalFault? Step(float headingDeg, float distanceM, TurnSignalState signal, bool inExemptZone)
        {
            TurnDetectedThisStep = false;
            if (distanceM < 0f) distanceM = 0f;

            m_Cumulative += distanceM;
            PushSample(signal, headingDeg);

            if (!m_HasReference)
            {
                m_HasReference = true;
                m_Reference = headingDeg;
                m_StartDistance = m_Cumulative;
                m_SignalAtStart = signal;
                m_ExemptSeen = inExemptZone;
            }

            if (m_Turning || m_AwaitingStability)
            {
                LastDeltaDeg = Mathf.DeltaAngle(m_Reference, headingDeg);
                UpdateStability(headingDeg, signal, inExemptZone);
                return null;
            }

            // Sin giro en curso y con el rumbo estable, la referencia sigue al auto.
            if (IsHeadingStable(headingDeg)) m_Reference = headingDeg;

            float delta = Mathf.DeltaAngle(m_Reference, headingDeg);
            LastDeltaDeg = delta;

            float abs = Mathf.Abs(delta);
            if (abs < TurnStartDeg)
            {
                // Va derecho: este paso es el posible inicio de un giro.
                m_StartDistance = m_Cumulative;
                m_SignalAtStart = signal;
                m_ExemptSeen = inExemptZone;
                return null;
            }

            m_ExemptSeen |= inExemptZone;
            if (abs < TurnDetectDeg) return null;

            return DetectTurn(delta);
        }

        private TurnSignalFault? DetectTurn(float delta)
        {
            TurnSignalState direction = delta > 0f ? TurnSignalState.Right : TurnSignalState.Left;

            m_Turning = true;
            m_DetectDistance = m_Cumulative;
            TurnDetectedThisStep = true;
            LastTurnDirection = direction;

            if (m_ExemptSeen)
            {
                LastOutcome = TurnSignalOutcome.Exempt;
                return null;
            }

            // Ventana [inicio − X', inicio]: recortada por la anticipación, por el borde más una muestra, por el margen
            // de reacción pendiente (fin de giro, o lo que quedaba al reiniciar por una pausa) y nunca después del inicio.
            float windowStart = Mathf.Max(m_StartDistance - AnticipationM, m_BoundaryDistance + SampleSpacingM, m_MarginEndDistance);
            if (windowStart > m_StartDistance) windowStart = m_StartDistance;

            bool allCorrect = true, anyCorrect = false;
            for (int i = 0; i < m_SampleCount; i++)
            {
                float distance = m_SampleDistance[i];
                if (distance < windowStart - k_Epsilon || distance > m_StartDistance + k_Epsilon) continue;

                if (m_SampleSignal[i] == (sbyte)direction) anyCorrect = true;
                else allCorrect = false;
            }

            TurnSignalFaultReason reason;
            if (m_SignalAtStart == direction)
            {
                if (allCorrect)
                {
                    LastOutcome = TurnSignalOutcome.Signaled;
                    return null;
                }

                reason = TurnSignalFaultReason.TooLate;
            }
            else if (m_SignalAtStart != TurnSignalState.Off)
            {
                reason = TurnSignalFaultReason.WrongSide;
            }
            else
            {
                reason = anyCorrect ? TurnSignalFaultReason.TooLate : TurnSignalFaultReason.NoSignal;
            }

            LastFault = new TurnSignalFault(direction, reason);
            LastOutcome = TurnSignalOutcome.Fault;
            return LastFault;
        }

        // Con el giro detectado: termina cuando el rumbo es estable. A MaxTurnDistanceM de la detección el giro se
        // cierra igual, pero sin fijar referencia a mitad de la curva: queda esperando estabilidad.
        private void UpdateStability(float headingDeg, TurnSignalState signal, bool inExemptZone)
        {
            if (IsHeadingStable(headingDeg))
            {
                // Fin del giro: nueva referencia y borde de la ventana del próximo.
                m_Turning = false;
                m_AwaitingStability = false;
                m_Reference = headingDeg;
                m_BoundaryDistance = Mathf.Max(SettledSince(headingDeg), m_DetectDistance);
                m_MarginEndDistance = m_BoundaryDistance + Mathf.Max(ReactionMarginM, SampleSpacingM);
                m_StartDistance = m_Cumulative;
                m_SignalAtStart = signal;
                m_ExemptSeen = inExemptZone;
                LastDeltaDeg = 0f;
                return;
            }

            if (m_Turning && m_Cumulative - m_DetectDistance >= MaxTurnDistanceM)
            {
                m_Turning = false;
                m_AwaitingStability = true;
            }
        }

        // Distancia de la muestra más vieja de la racha final cuyo rumbo está a menos de k_SettledDeg del actual: dónde
        // terminó de girar el auto. Sin muestras asentadas devuelve la distancia actual.
        private float SettledSince(float headingDeg)
        {
            float settled = m_Cumulative;
            for (int k = 0; k < m_SampleCount; k++)
            {
                int index = (m_SampleHead - 1 - k + k_BufferSize) % k_BufferSize;
                if (Mathf.Abs(Mathf.DeltaAngle(headingDeg, m_SampleHeading[index])) >= k_SettledDeg) break;
                settled = m_SampleDistance[index];
            }

            return settled;
        }

        // true si en los últimos StableDistanceM recorridos el rumbo cambió menos de StableDeg en neto y varió menos
        // de StableRangeDeg (máx − mín). Exige historial suficiente. Recorre el buffer hacia atrás, sin allocations.
        private bool IsHeadingStable(float headingDeg)
        {
            float windowStart = m_Cumulative - StableDistanceM;
            float min = 0f, max = 0f, oldest = 0f;
            bool covered = false;

            for (int k = 0; k < m_SampleCount; k++)
            {
                int index = (m_SampleHead - 1 - k + k_BufferSize) % k_BufferSize;
                if (m_SampleDistance[index] < windowStart - k_Epsilon)
                {
                    covered = true;
                    break;
                }

                float relative = Mathf.DeltaAngle(headingDeg, m_SampleHeading[index]);
                if (relative < min) min = relative;
                if (relative > max) max = relative;
                oldest = relative;
            }

            return covered && Mathf.Abs(oldest) < StableDeg && max - min < StableRangeDeg;
        }

        // Una muestra cada SampleSpacingM metros. Sobrescribe la más vieja cuando el buffer se llena.
        private void PushSample(TurnSignalState signal, float headingDeg)
        {
            if (m_Cumulative - m_LastSampleDistance < SampleSpacingM) return;

            m_LastSampleDistance = m_Cumulative;
            m_SampleDistance[m_SampleHead] = m_Cumulative;
            m_SampleSignal[m_SampleHead] = (sbyte)signal;
            m_SampleHeading[m_SampleHead] = headingDeg;
            m_SampleHead = (m_SampleHead + 1) % k_BufferSize;
            if (m_SampleCount < k_BufferSize) m_SampleCount++;
        }
    }
}
