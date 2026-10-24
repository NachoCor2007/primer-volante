using System;
using System.Collections.Generic;
using PrimerVolante.Evaluation;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Detector de circulación en contramano. Va en la raíz del auto del jugador, junto al
    /// <c>VehicleController</c>. En cada paso de física consulta en qué carril (<see cref="RoadLaneZone"/>) está el
    /// punto de consulta del auto, compara la dirección de su velocidad con el sentido permitido del carril y, si
    /// recorre más de <c>m_WrongWayDistanceM</c> metros en contra a más de <c>m_MinSpeedKmh</c>, informa
    /// <see cref="InfractionIds.WrongWay"/> una sola vez por episodio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El estado del episodio vive acá (en el auto) y no en las piezas de calle: una cuadra son varias piezas y no
    /// puede sumar una falta por pieza. Pasar por una intersección (sin zonas) no corta ni rearma el episodio.
    /// </para>
    /// <para>
    /// Se mide con la velocidad (<see cref="Rigidbody.linearVelocity"/>) y no con la aceleración, así que la marcha
    /// atrás cuenta: retroceder más de la tolerancia en contra del sentido del carril también es contramano.
    /// Un adelantamiento completo por el carril contrario cuenta como falta. Dos esquivas cortas separadas por
    /// menos de <c>m_RearmDistanceM</c> de manejo a mano se suman (ver <see cref="WrongWayEpisode"/>).
    /// </para>
    /// <para>
    /// Las zonas están en la capa <c>LaneZone</c>, que no colisiona con ninguna capa: el detector las encuentra
    /// con <see cref="Physics.OverlapSphereNonAlloc(Vector3, float, Collider[], int, QueryTriggerInteraction)"/>.
    /// Sin allocations por frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class WrongWayDetector : MonoBehaviour
    {
        private const float k_ProbeRadius = 0.05f;
        private const int k_BufferSize = 16;

        [Header("Tolerancia")]
        [Tooltip("Metros recorridos en contramano antes de informar la falta. Un adelantamiento completo la supera.")]
        [SerializeField] private float m_WrongWayDistanceM = 10f;

        [Tooltip("Por debajo de esta velocidad (km/h) no se acumula distancia ni se rearma el episodio.")]
        [SerializeField] private float m_MinSpeedKmh = 3f;

        [Header("Clasificación por ángulo")]
        [Tooltip("Ángulo máximo (grados) entre la velocidad y el sentido permitido para considerar que va a mano.")]
        [SerializeField] private float m_CorrectMaxAngleDeg = 60f;

        [Tooltip("Ángulo mínimo (grados) para considerar contramano. Entre los dos ángulos el estado es neutro (doblar, maniobrar, cruzar en perpendicular).")]
        [SerializeField] private float m_WrongMinAngleDeg = 120f;

        [Tooltip("Metros circulando a mano para cerrar el episodio y rearmar la detección.")]
        [SerializeField] private float m_RearmDistanceM = 10f;

        [Header("Consulta")]
        [Tooltip("Punto del auto (espacio local) que se consulta contra las zonas: el centro de la caja del auto.")]
        [SerializeField] private Vector3 m_ProbeLocalOffset = new Vector3(0f, 0.5f, -0.45f);

        [Tooltip("Capas que consulta el detector. Debe ser solo la capa LaneZone.")]
        [SerializeField] private LayerMask m_ZoneMask;

        [Header("Depuración")]
        [Tooltip("Escribe los logs [WrongWayDetector] de inicio y fin de episodio y de la falta.")]
        [SerializeField] private bool m_LogEvents = false;

        private Rigidbody m_Body;
        private WrongWayEpisode m_Episode;
        private readonly Collider[] m_Buffer = new Collider[k_BufferSize];
        private readonly HashSet<object> m_Suppressors = new HashSet<object>();
        private WrongWayClassification m_LastClassification = WrongWayClassification.Neutral;
        private bool m_WasWrongWay;

        /// <summary>Se dispara al empezar un episodio de contramano (primer paso en contra). No registra la falta.</summary>
        public event Action<WrongWayDetector> OnWrongWayStarted;

        /// <summary>Se dispara al cerrarse o descartarse un episodio de contramano. No registra la falta.</summary>
        public event Action<WrongWayDetector> OnWrongWayEnded;

        /// <summary>true mientras hay un episodio de contramano en curso (distancia en contramano mayor que cero).</summary>
        public bool IsWrongWay => Episode.WrongDistanceM > 0f;

        /// <summary>Metros acumulados en contramano en el episodio en curso.</summary>
        public float CurrentWrongDistanceM => Episode.WrongDistanceM;

        /// <summary>true si la falta ya se informó en el episodio en curso.</summary>
        public bool HasReportedCurrentEpisode => Episode.Reported;

        /// <summary>true mientras algún dueño tenga suspendido el detector (ver <see cref="SetSuppressed"/>).</summary>
        public bool IsSuppressed => m_Suppressors.Count > 0;

        /// <summary>Última clasificación calculada (para depurar).</summary>
        public WrongWayClassification LastClassification => m_LastClassification;

        /// <summary>Punto de consulta en el mundo.</summary>
        public Vector3 ProbeWorldPosition => transform.TransformPoint(m_ProbeLocalOffset);

        /// <summary>Máscara de la consulta de zonas.</summary>
        public LayerMask ZoneMask
        {
            get => m_ZoneMask;
            set => m_ZoneMask = value;
        }

        /// <summary>Punto de consulta en el espacio local del auto.</summary>
        public Vector3 ProbeLocalOffset => m_ProbeLocalOffset;

        /// <summary>Metros en contramano antes de la falta.</summary>
        public float WrongWayDistanceM
        {
            get => m_WrongWayDistanceM;
            set
            {
                m_WrongWayDistanceM = Mathf.Max(0f, value);
                Episode.WrongWayDistanceM = m_WrongWayDistanceM;
            }
        }

        /// <summary>Velocidad mínima (km/h) para evaluar.</summary>
        public float MinSpeedKmh
        {
            get => m_MinSpeedKmh;
            set => m_MinSpeedKmh = Mathf.Max(0f, value);
        }

        private WrongWayEpisode Episode
        {
            get
            {
                if (m_Episode == null) m_Episode = new WrongWayEpisode(m_WrongWayDistanceM, m_RearmDistanceM);
                return m_Episode;
            }
        }

        private void Reset()
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (layer >= 0) m_ZoneMask = 1 << layer;
        }

        private void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            Episode.WrongWayDistanceM = m_WrongWayDistanceM;
            Episode.RearmDistanceM = m_RearmDistanceM;
        }

        private void OnDisable()
        {
            DiscardEpisode();
        }

        /// <summary>
        /// Suspende o reanuda el detector en nombre de <paramref name="owner"/>. El detector está suspendido
        /// mientras algún dueño lo tenga suspendido, así dos sistemas no se pisan. Suspender descarta el episodio
        /// en curso y no se informa ninguna falta mientras dure.
        /// </summary>
        /// <example>
        /// Desde un paso de lección futuro (práctica con conos):
        /// <code>
        /// private void OnStepStarted() { m_Detector.SetSuppressed(this, true); }
        /// private void OnStepFinished() { m_Detector.SetSuppressed(this, false); }
        /// private void OnDisable() { if (m_Detector != null) m_Detector.SetSuppressed(this, false); }
        /// </code>
        /// </example>
        public void SetSuppressed(object owner, bool suppressed)
        {
            if (owner == null)
            {
                Debug.LogWarning("[WrongWayDetector] ⚠️ SetSuppressed requiere un dueño (this); se ignora la llamada.");
                return;
            }

            if (suppressed)
            {
                m_Suppressors.Add(owner);
                DiscardEpisode();
            }
            else
            {
                m_Suppressors.Remove(owner);
            }
        }

        private void FixedUpdate()
        {
            // 1. Suspendido: se descarta el episodio y no se evalúa.
            if (m_Suppressors.Count > 0)
            {
                DiscardEpisode();
                return;
            }

            // 2. Velocidad horizontal. Muy lenta: no cambia el estado.
            Vector3 velocity = m_Body.linearVelocity;
            velocity.y = 0f;
            float speedMs = velocity.magnitude;
            if (speedMs * 3.6f < m_MinSpeedKmh) return;

            // 3. Dónde está el centro del auto.
            Vector3 probe = ProbeWorldPosition;
            int count = Physics.OverlapSphereNonAlloc(probe, k_ProbeRadius, m_Buffer, m_ZoneMask, QueryTriggerInteraction.Collide);

            RoadLaneZone best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = m_Buffer[i];
                m_Buffer[i] = null;

                if (hit.TryGetComponent(out WrongWayExemptZone _))
                {
                    // Exención: no se evalúa y se descarta el episodio. Limpia lo que falte del buffer.
                    for (int j = i + 1; j < count; j++) m_Buffer[j] = null;
                    DiscardEpisode();
                    return;
                }

                if (!hit.TryGetComponent(out RoadLaneZone zone)) continue;

                // En la junta entre dos piezas puede haber dos zonas: gana la de centro más cercano.
                float sqr = (hit.bounds.center - probe).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = zone;
                }
            }

            // Sin zona (intersección, plaza, fuera de la calle): no cambia el estado.
            if (best == null) return;

            // 4. Clasifica y 5. acumula.
            m_LastClassification = WrongWayMath.Classify(velocity, best.AllowedWorldDirection, m_CorrectMaxAngleDeg, m_WrongMinAngleDeg);

            WrongWayEpisode episode = Episode;
            episode.WrongWayDistanceM = m_WrongWayDistanceM;
            episode.RearmDistanceM = m_RearmDistanceM;

            bool shouldReport = episode.Step(m_LastClassification, speedMs * Time.fixedDeltaTime);
            UpdateWrongWayState();

            // 6. Informa la falta.
            if (shouldReport)
            {
                if (m_LogEvents)
                {
                    Debug.Log($"[WrongWayDetector] 🚫 Contramano ({(best.IsOneWay ? "mano única" : "doble mano")}) tras {episode.WrongDistanceM:F1} m.");
                }

                InfractionReporting.Report(InfractionReport.Create(
                    InfractionIds.WrongWay, this, transform.position,
                    ("where", best.IsOneWay ? WrongWayMath.WhereOneWay : WrongWayMath.WhereTwoWay)));
            }
        }

        private void DiscardEpisode()
        {
            if (m_Episode == null) return;
            m_Episode.Reset();
            UpdateWrongWayState();
        }

        // Dispara OnWrongWayStarted / OnWrongWayEnded en las transiciones de IsWrongWay.
        private void UpdateWrongWayState()
        {
            bool isWrong = m_Episode != null && m_Episode.WrongDistanceM > 0f;
            if (isWrong == m_WasWrongWay) return;

            m_WasWrongWay = isWrong;
            if (m_LogEvents) Debug.Log(isWrong ? "[WrongWayDetector] ⚠️ Empezó un episodio en contramano." : "[WrongWayDetector] ✅ Terminó el episodio en contramano.");

            try
            {
                if (isWrong) OnWrongWayStarted?.Invoke(this);
                else OnWrongWayEnded?.Invoke(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Color color;
            switch (m_LastClassification)
            {
                case WrongWayClassification.Correct: color = Color.green; break;
                case WrongWayClassification.Wrong: color = Color.red; break;
                default: color = Color.yellow; break;
            }

            if (IsSuppressed) color = Color.gray;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(ProbeWorldPosition, 0.25f);
            Gizmos.DrawLine(ProbeWorldPosition, ProbeWorldPosition + Vector3.up * 3f);
        }
#endif
    }
}
