using PrimerVolante.Evaluation;
using PrimerVolante.VR;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Detector de giros sin señalizar. Va en la raíz del auto del jugador, junto al <c>VehicleController</c>. En
    /// cada paso de física calcula el rumbo y la distancia recorrida hacia adelante, consulta si el punto de
    /// consulta del auto está en una <see cref="TurnSignalExemptZone"/> y alimenta al <see cref="TurnSignalTracker"/>
    /// con el guiño (<see cref="VehicleController.CurrentTurnSignal"/>). Si el tracker devuelve una falta, informa
    /// <see cref="InfractionIds.TurnSignal"/> con los parámetros <c>direction</c> y <c>reason</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las balizas (<see cref="VehicleController.IsHazardActive"/>) <b>no</b> cuentan como señalización: solo se
    /// lee el guiño. Por debajo de <c>m_MinSpeedKmh</c> y en marcha atrás no se evalúa ni se acumula y el tracker
    /// se pone en pausa (<see cref="TurnSignalTracker.Pause"/>): sin un giro en curso se reinicia (lo girado en
    /// reversa no cuenta y al volver a avanzar el rumbo de referencia es el actual); con un giro en curso conserva
    /// su estado, así una maniobra de 3 puntos o una U lenta no cuentan dos veces.
    /// </para>
    /// <para>
    /// Las zonas exentas están en la capa <c>LaneZone</c>, que no colisiona con ninguna capa: el detector las
    /// encuentra con <see cref="Physics.OverlapSphereNonAlloc(Vector3, float, Collider[], int, QueryTriggerInteraction)"/>.
    /// Sin allocations por frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class TurnSignalDetector : MonoBehaviour
    {
        private const float k_ProbeRadius = 0.05f;
        private const int k_BufferSize = 32;

        // Velocidad hacia atrás (m/s) por encima de la cual se considera marcha atrás y no un temblor del auto detenido.
        private const float k_ReverseSpeedMs = 0.3f;

        [Header("Detección del giro")]
        [Tooltip("Por debajo de esta velocidad (km/h), o en marcha atrás, no se evalúa ni se acumula distancia: el rastreo se reinicia, salvo que haya un giro en curso (entonces se conserva).")]
        [SerializeField] private float m_MinSpeedKmh = 3f;

        [Tooltip("Desvío neto (grados) respecto del rumbo de referencia a partir del cual hay un giro.")]
        [SerializeField] private float m_TurnAngleDeg = 45f;

        [Tooltip("Mientras el desvío sea menor que esto (grados) el auto va derecho y se registra el posible inicio del giro.")]
        [SerializeField] private float m_TurnStartAngleDeg = 10f;

        [Tooltip("Metros de anticipación con el guiño correcto antes del inicio del giro.")]
        [SerializeField] private float m_AnticipationM = 20f;

        [Tooltip("Metros después del fin del giro anterior en los que no se exige guiño (reacción tras el auto-cancelado de la palanca). Una pausa (frenada casi total, reversa) conserva lo que quede; un arranque o una supresión no. Mínimo 0,5 m.")]
        [Min(0.5f)]
        [SerializeField] private float m_ReactionMarginM = 15f;

        [Header("Fin del giro")]
        [Tooltip("Cambio neto máximo del rumbo (grados) en los últimos metros recorridos para considerarlo estable (el giro terminó).")]
        [SerializeField] private float m_StableAngleDeg = 5f;

        [Tooltip("Rango máximo del rumbo (máx − mín, grados) en los últimos metros recorridos para considerarlo estable. Tolera las correcciones del volante.")]
        [SerializeField] private float m_StableRangeDeg = 12f;

        [Tooltip("Metros recorridos hacia atrás que se miran para decidir si el rumbo es estable.")]
        [SerializeField] private float m_StableDistanceM = 5f;

        [Tooltip("Tope de seguridad: metros desde la detección tras los cuales el giro se da por terminado aunque el rumbo siga inestable.")]
        [SerializeField] private float m_MaxTurnDistanceM = 30f;

        [Header("Consulta")]
        [Tooltip("Punto del auto (espacio local) que se consulta contra las zonas exentas: el mismo que usa el WrongWayDetector.")]
        [SerializeField] private Vector3 m_ProbeLocalOffset = new Vector3(0f, 0.5f, -0.45f);

        [Tooltip("Capas que consulta el detector. Debe ser solo la capa LaneZone.")]
        [SerializeField] private LayerMask m_ZoneMask;

        [Header("Depuración")]
        [Tooltip("Escribe los logs [TurnSignalDetector] de giro detectado, falta, giro bien señalizado y exención.")]
        [SerializeField] private bool m_LogEvents = false;

        private Rigidbody m_Body;
        private VehicleController m_Vehicle;
        private TurnSignalTracker m_Tracker;
        private readonly Collider[] m_Buffer = new Collider[k_BufferSize];
        private readonly SuppressionSet m_Suppression = new SuppressionSet("[TurnSignalDetector]");
        private bool m_InExemptZone;
        private bool m_WarnedFullBuffer;
        private bool m_WarnedNoVehicle;

        /// <summary>
        /// true mientras algún dueño tenga suspendido el detector (ver <see cref="SetSuppressed"/>). Los dueños
        /// que son objetos de Unity ya destruidos se descartan solos, así que un paso de lección destruido sin
        /// liberar no deja el detector suspendido para siempre.
        /// </summary>
        public bool IsSuppressed => m_Suppression.IsActive;

        /// <summary>Último desvío (grados) respecto del rumbo de referencia; positivo = a la derecha.</summary>
        public float LastDeltaDeg => m_Tracker != null ? m_Tracker.LastDeltaDeg : 0f;

        /// <summary>true mientras hay un giro detectado que todavía no terminó.</summary>
        public bool IsTurning => m_Tracker != null && m_Tracker.IsTurning;

        /// <summary>Qué pasó con el último giro detectado (falta, bien señalizado o exento).</summary>
        public TurnSignalOutcome LastOutcome => m_Tracker != null ? m_Tracker.LastOutcome : TurnSignalOutcome.None;

        /// <summary>Dirección del último giro detectado.</summary>
        public TurnSignalState LastTurnDirection => m_Tracker != null ? m_Tracker.LastTurnDirection : TurnSignalState.Off;

        /// <summary>Última falta informada (válida si <see cref="LastOutcome"/> es <see cref="TurnSignalOutcome.Fault"/>).</summary>
        public TurnSignalFault LastFault => m_Tracker != null ? m_Tracker.LastFault : default;

        /// <summary>true si el punto de consulta estuvo en una zona exenta en el último paso evaluado.</summary>
        public bool InExemptZone => m_InExemptZone;

        /// <summary>Punto de consulta en el mundo.</summary>
        public Vector3 ProbeWorldPosition => transform.TransformPoint(m_ProbeLocalOffset);

        /// <summary>Máscara de la consulta de zonas.</summary>
        public LayerMask ZoneMask
        {
            get => m_ZoneMask;
            set => m_ZoneMask = value;
        }

        /// <summary>Velocidad mínima (km/h) para evaluar.</summary>
        public float MinSpeedKmh
        {
            get => m_MinSpeedKmh;
            set => m_MinSpeedKmh = Mathf.Max(0f, value);
        }

        private TurnSignalTracker Tracker
        {
            get
            {
                if (m_Tracker == null)
                {
                    m_Tracker = new TurnSignalTracker();
                    SyncTrackerThresholds();
                }

                return m_Tracker;
            }
        }

        /// <summary>
        /// Descarta todo el rastreo (historial, referencia y giro en curso): el rumbo actual pasa a ser la
        /// referencia en el próximo paso. Lo debe llamar quien reinicie la simulación o teletransporte el auto
        /// (por ejemplo <c>VehicleController.ResetToBaseState</c> desde un futuro gestor de estado). Equivale a
        /// deshabilitar y volver a habilitar el componente.
        /// </summary>
        public void ResetTracking()
        {
            Tracker.Reset();
        }

        private void Reset()
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (layer >= 0) m_ZoneMask = 1 << layer;
        }

        private void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Vehicle = GetComponent<VehicleController>();
            EnsureZoneMask();
        }

        // Respaldo de la máscara: un componente agregado con AddComponent en runtime no pasa por Reset().
        private void EnsureZoneMask()
        {
            if (m_ZoneMask.value != 0) return;

            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (layer >= 0)
            {
                m_ZoneMask = 1 << layer;
            }
            else
            {
                Debug.LogWarning($"[TurnSignalDetector] ⚠️ No existe la capa '{RoadLaneZone.LayerName}': las zonas exentas no se detectan. Corré Tools/Primer Volante/Setup Turn Signal Detection.", this);
            }
        }

        private void OnEnable()
        {
            SyncTrackerThresholds();
            Tracker.Reset();
        }

        private void OnDisable()
        {
            if (m_Tracker != null) m_Tracker.Reset();
        }

        // Las ediciones del Inspector en Play Mode llegan por acá.
        private void OnValidate()
        {
            if (m_Tracker != null) SyncTrackerThresholds();
        }

        private void SyncTrackerThresholds()
        {
            if (m_Tracker == null) return;
            m_Tracker.TurnDetectDeg = m_TurnAngleDeg;
            m_Tracker.TurnStartDeg = m_TurnStartAngleDeg;
            m_Tracker.AnticipationM = m_AnticipationM;
            m_Tracker.ReactionMarginM = m_ReactionMarginM;
            m_Tracker.StableDeg = m_StableAngleDeg;
            m_Tracker.StableRangeDeg = m_StableRangeDeg;
            m_Tracker.StableDistanceM = m_StableDistanceM;
            m_Tracker.MaxTurnDistanceM = m_MaxTurnDistanceM;
        }

        /// <summary>
        /// Suspende o reanuda el detector en nombre de <paramref name="owner"/>. El detector está suspendido
        /// mientras algún dueño lo tenga suspendido, así dos sistemas no se pisan. Suspender reinicia el rastreo y
        /// no se informa ninguna falta mientras dure.
        /// </summary>
        /// <example>
        /// Desde un paso de lección futuro (slalom de conos):
        /// <code>
        /// private void OnStepStarted() { m_Detector.SetSuppressed(this, true); }
        /// private void OnStepFinished() { m_Detector.SetSuppressed(this, false); }
        /// private void OnDisable() { if (m_Detector != null) m_Detector.SetSuppressed(this, false); }
        /// </code>
        /// </example>
        public void SetSuppressed(object owner, bool suppressed)
        {
            if (!m_Suppression.Set(owner, suppressed)) return;

            if (suppressed) Tracker.Reset();
        }

        private void FixedUpdate()
        {
            TurnSignalTracker tracker = Tracker;

            // 1. Suspendido: se reinicia y no se evalúa.
            if (IsSuppressed)
            {
                tracker.Reset();
                return;
            }

            // 2. Velocidad horizontal. Lento o en marcha atrás: no evalúa ni acumula. Sin giro en curso reinicia la
            // referencia; con un giro en curso se conserva (maniobra de 3 puntos, U lenta).
            Vector3 velocity = m_Body.linearVelocity;
            velocity.y = 0f;
            float speedMs = velocity.magnitude;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            float forwardSpeed = Vector3.Dot(velocity, forward);
            if (speedMs * 3.6f < m_MinSpeedKmh || forwardSpeed < 0f)
            {
                tracker.Pause(forwardSpeed < -k_ReverseSpeedMs);
                return;
            }

            // 3. Rumbo (yaw horario visto desde arriba) y distancia recorrida en el paso.
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float distance = speedMs * Time.fixedDeltaTime;

            // 4. Exenciones.
            m_InExemptZone = QueryExemptZone();

            // 5. Tracker. Solo cuenta el guiño: las balizas no señalizan.
            TurnSignalState signal = TurnSignalState.Off;
            if (m_Vehicle != null) signal = m_Vehicle.CurrentTurnSignal;
            else if (!m_WarnedNoVehicle)
            {
                m_WarnedNoVehicle = true;
                Debug.LogWarning("[TurnSignalDetector] ⚠️ No hay VehicleController en el mismo objeto: se asume el guiño apagado.", this);
            }

            TurnSignalFault? fault = tracker.Step(heading, distance, signal, m_InExemptZone);
            if (!tracker.TurnDetectedThisStep) return;

            if (fault.HasValue)
            {
                TurnSignalFault value = fault.Value;
                if (m_LogEvents)
                {
                    Debug.Log($"[TurnSignalDetector] 🚫 Giro {value.DirectionText} {value.ReasonText} (desvío {tracker.LastDeltaDeg:F0}°, guiño {signal}).");
                }

                InfractionReporting.Report(InfractionReport.Create(
                    InfractionIds.TurnSignal, this, transform.position,
                    ("direction", value.DirectionText),
                    ("reason", value.ReasonText)));
            }
            else if (m_LogEvents)
            {
                switch (tracker.LastOutcome)
                {
                    case TurnSignalOutcome.Signaled:
                        Debug.Log($"[TurnSignalDetector] ✅ Giro {TurnSignalMath.DirectionText(tracker.LastTurnDirection)} bien señalizado (desvío {tracker.LastDeltaDeg:F0}°).");
                        break;
                    case TurnSignalOutcome.Exempt:
                        Debug.Log($"[TurnSignalDetector] ℹ️ Giro {TurnSignalMath.DirectionText(tracker.LastTurnDirection)} en zona exenta: sin control (desvío {tracker.LastDeltaDeg:F0}°).");
                        break;
                }
            }
        }

        // true si el punto de consulta está dentro de alguna TurnSignalExemptZone.
        private bool QueryExemptZone()
        {
            Vector3 probe = ProbeWorldPosition;
            int count = Physics.OverlapSphereNonAlloc(probe, k_ProbeRadius, m_Buffer, m_ZoneMask, QueryTriggerInteraction.Collide);

            if (count >= k_BufferSize && !m_WarnedFullBuffer)
            {
                // Una exención podría quedar afuera del buffer: se avisa una sola vez.
                m_WarnedFullBuffer = true;
                Debug.LogWarning($"[TurnSignalDetector] ⚠️ Hay {k_BufferSize} o más colliders de LaneZone en el punto de consulta; puede que se ignore alguno.", this);
            }

            bool exempt = false;
            for (int i = 0; i < count; i++)
            {
                if (m_Buffer[i].TryGetComponent(out TurnSignalExemptZone _)) exempt = true;
                m_Buffer[i] = null;
            }

            return exempt;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Color color = Color.green;
            if (IsSuppressed) color = Color.gray;
            else if (m_InExemptZone) color = new Color(1f, 0.85f, 0.2f);
            else if (IsTurning) color = Color.cyan;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(ProbeWorldPosition, 0.25f);
            Gizmos.DrawLine(ProbeWorldPosition, ProbeWorldPosition + Vector3.up * 3f);
        }
#endif
    }
}
