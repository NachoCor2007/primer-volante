using UnityEngine;
using PrimerVolante.Evaluation;
using PrimerVolante.VR;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Zona de evaluación y monitoreo de velocidad vehicular para tramos viales regulados.
    /// Detecta el vehículo del jugador al ingresar, monitorea excesos de velocidad con tolerancia
    /// y emite alertas y reporte final al finalizar el tramo.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [SelectionBase]
    public class SpeedLimitEvaluator : MonoBehaviour
    {
        [Header("Parámetros de Evaluación")]
        [Tooltip("Límite de velocidad reglamentario en km/h para este tramo.")]
        [SerializeField] private int m_SpeedLimitKmh = 40;

        [Tooltip("Tolerancia permitida en km/h sobre el límite antes de computar infracción (ej. 3 km/h).")]
        [SerializeField] private float m_ToleranceKmh = 3f;

        [Tooltip("Tiempo mínimo en segundos entre avisos consecutivos de infracción en consola.")]
        [SerializeField] private float m_AlertCooldown = 1.5f;

        [Header("Depuración")]
        [Tooltip("Escribe los logs [TRAFFIC-SPEED] de ingreso, alerta periódica y salida del tramo.")]
        [SerializeField] private bool m_LogEvents = false;

        [Header("Vinculación Opcional")]
        [Tooltip("Referencia al cartel vial asociado (si se asigna, sincroniza el límite automáticamente).")]
        [SerializeField] private SpeedLimitSignDisplay m_SignDisplay;

        // Estado en tiempo de ejecución
        private VehicleController m_CurrentVehicle;
        private bool m_HasInfraction;
        private float m_MaxSpeedRecorded;
        private float m_LastAlertTime = -100f;
        private BoxCollider m_BoxCollider;
        private float m_LastStayTime = float.NegativeInfinity;

        // Segundos sin OnTriggerStay a partir de los cuales el tramo abierto se considera abandonado.
        private const float k_StaleStaySeconds = 0.25f;
        private readonly System.Collections.Generic.HashSet<Collider> m_ActiveColliders = new System.Collections.Generic.HashSet<Collider>();

        /// <summary>
        /// Límite de velocidad reglamentario asignado.
        /// </summary>
        public int SpeedLimitKmh
        {
            get => m_SpeedLimitKmh;
            set => m_SpeedLimitKmh = value;
        }

        /// <summary>
        /// Tolerancia permitida en km/h.
        /// </summary>
        public float ToleranceKmh
        {
            get => m_ToleranceKmh;
            set => m_ToleranceKmh = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Intervalo mínimo entre alertas consecutivas.
        /// </summary>
        public float AlertCooldown
        {
            get => m_AlertCooldown;
            set => m_AlertCooldown = Mathf.Max(0.1f, value);
        }

        /// <summary>
        /// Cartel de señalización vinculado.
        /// </summary>
        public SpeedLimitSignDisplay SignDisplay
        {
            get => m_SignDisplay;
            set
            {
                m_SignDisplay = value;
                SyncWithSign();
            }
        }

        /// <summary>
        /// Indica si se cometió alguna infracción en el tramo actual.
        /// </summary>
        public bool HasInfraction => m_HasInfraction;

        /// <summary>
        /// Velocidad máxima registrada en el tramo actual (km/h).
        /// </summary>
        public float MaxSpeedRecorded => m_MaxSpeedRecorded;

        /// <summary>
        /// Vehículo actualmente en evaluación dentro del tramo.
        /// </summary>
        public VehicleController CurrentVehicle => m_CurrentVehicle;

        private void Awake()
        {
            EnsureColliderIsTrigger();
            SyncWithSign();
        }

        private void OnValidate()
        {
            EnsureColliderIsTrigger();
            SyncWithSign();
        }

        /// <summary>
        /// Sincroniza el límite de velocidad con el cartel asignado si existe.
        /// </summary>
        public void SyncWithSign()
        {
            if (m_SignDisplay != null)
            {
                m_SpeedLimitKmh = m_SignDisplay.SpeedLimitKmh;
            }
        }

        /// <summary>
        /// Determina si una velocidad dada excede el límite sumado a la tolerancia.
        /// </summary>
        /// <param name="speedKmh">Velocidad instantánea en km/h.</param>
        /// <returns>True si constituye infracción por exceso de velocidad.</returns>
        public bool IsExcessSpeed(float speedKmh)
        {
            return speedKmh > (m_SpeedLimitKmh + m_ToleranceKmh);
        }

        private void EnsureColliderIsTrigger()
        {
            if (m_BoxCollider == null)
            {
                m_BoxCollider = GetComponent<BoxCollider>();
            }

            if (m_BoxCollider != null && !m_BoxCollider.isTrigger)
            {
                m_BoxCollider.isTrigger = true;
            }
        }

        private void OnDisable()
        {
            // Apagado, OnTriggerExit no llega: el tramo se cierra para que al volver empiece limpio.
            m_ActiveColliders.Clear();
            m_CurrentVehicle = null;
            m_HasInfraction = false;
        }

        /// <summary>
        /// Quita los colliders del auto que ya no están dentro de la zona: destruidos o apagados, o todos si hace
        /// más de <see cref="k_StaleStaySeconds"/> que no llega un <c>OnTriggerStay</c> (mientras el auto está
        /// adentro llega uno por paso de física; si dejó de llegar, el auto salió sin <c>OnTriggerExit</c>, por
        /// ejemplo teletransportado). Si no queda ninguno, cierra el tramo.
        /// </summary>
        private void PruneStaleColliders()
        {
            if (m_ActiveColliders.Count == 0) return;

            if (Time.fixedTime - m_LastStayTime > k_StaleStaySeconds)
            {
                m_ActiveColliders.Clear();
            }
            else
            {
                m_ActiveColliders.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            }

            if (m_ActiveColliders.Count == 0)
            {
                m_CurrentVehicle = null;
                m_HasInfraction = false;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null) return;

            // Colliders que salieron sin OnTriggerExit (auto teletransportado, apagado o destruido): sin esto el
            // tramo quedaría abierto con la infracción ya marcada y no volvería a registrar.
            PruneStaleColliders();

            // Si ya estamos evaluando otro vehículo distinto, ignorarlo
            if (m_CurrentVehicle != null && m_CurrentVehicle != vehicle) return;

            bool isFirstCollider = (m_ActiveColliders.Count == 0);
            m_ActiveColliders.Add(other);

            // Cuenta como señal de presencia: otro collider del auto puede entrar antes del primer OnTriggerStay.
            m_LastStayTime = Time.fixedTime;

            // Solo inicializar y notificar con el primer colisionador del auto que ingresa
            if (isFirstCollider)
            {
                m_CurrentVehicle = vehicle;
                m_HasInfraction = false;
                m_MaxSpeedRecorded = vehicle.CurrentSpeedKmh;
                m_LastAlertTime = -m_AlertCooldown;

                SyncWithSign();

                if (m_LogEvents)
                {
                    Debug.Log($"[TRAFFIC-SPEED] ℹ Ingreso a tramo con límite de {m_SpeedLimitKmh} km/h");
                }
            }
        }

        private void OnTriggerStay(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null || vehicle != m_CurrentVehicle) return;

            m_LastStayTime = Time.fixedTime;

            float currentSpeed = vehicle.CurrentSpeedKmh;
            if (currentSpeed > m_MaxSpeedRecorded)
            {
                m_MaxSpeedRecorded = currentSpeed;
            }

            if (IsExcessSpeed(currentSpeed))
            {
                // Una sola infracción por tramo: se informa solo en la transición a "con infracción". No lleva la
                // velocidad: en ese instante apenas pasó la tolerancia y no refleja el máximo que alcance después.
                if (!m_HasInfraction)
                {
                    InfractionReporting.Report(InfractionReport.Create(
                        InfractionIds.SpeedLimit, this, vehicle.transform.position,
                        ("limit", m_SpeedLimitKmh.ToString())));
                }

                m_HasInfraction = true;

                if (m_LogEvents && Time.time - m_LastAlertTime >= m_AlertCooldown)
                {
                    m_LastAlertTime = Time.time;
                    Debug.Log($"[TRAFFIC-SPEED] ⚠ INFRACCIÓN: Exceso de velocidad detectado ({currentSpeed:F1} km/h en zona de {m_SpeedLimitKmh} km/h)");
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null || vehicle != m_CurrentVehicle) return;

            m_ActiveColliders.Remove(other);

            // Solo finalizar el tramo y emitir reporte cuando TODOS los colisionadores del auto hayan salido
            if (m_ActiveColliders.Count == 0)
            {
                float finalSpeed = vehicle.CurrentSpeedKmh;
                if (finalSpeed > m_MaxSpeedRecorded)
                {
                    m_MaxSpeedRecorded = finalSpeed;
                }

                if (m_LogEvents && m_HasInfraction)
                {
                    Debug.Log($"[TRAFFIC-SPEED] ❌ Tramo finalizado con infracción. Velocidad máxima alcanzada: {m_MaxSpeedRecorded:F1} km/h (Permitido: {m_SpeedLimitKmh} km/h)");
                }
                else if (m_LogEvents)
                {
                    Debug.Log($"[TRAFFIC-SPEED] ✅ Tramo completado sin infracciones. Velocidad máxima alcanzada: {m_MaxSpeedRecorded:F1} km/h (Permitido: {m_SpeedLimitKmh} km/h)");
                }

                m_CurrentVehicle = null;
            }
        }

        private void OnDrawGizmos()
        {
            EnsureColliderIsTrigger();
            if (m_BoxCollider == null) return;

            bool isExcessNow = (m_CurrentVehicle != null && IsExcessSpeed(m_CurrentVehicle.CurrentSpeedKmh));

            Color fillColor = isExcessNow
                ? new Color(1f, 0.15f, 0.15f, 0.30f)
                : new Color(0f, 0.85f, 0.80f, 0.20f);

            Color wireColor = isExcessNow
                ? new Color(1f, 0.2f, 0.2f, 0.90f)
                : new Color(0f, 0.9f, 0.7f, 0.85f);

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Color oldColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = fillColor;
            Gizmos.DrawCube(m_BoxCollider.center, m_BoxCollider.size);

            Gizmos.color = wireColor;
            Gizmos.DrawWireCube(m_BoxCollider.center, m_BoxCollider.size);

            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;

#if UNITY_EDITOR
            Vector3 worldCenter = transform.TransformPoint(m_BoxCollider.center);
            Vector3 labelPos = worldCenter + Vector3.up * (m_BoxCollider.size.y * 0.5f + 0.35f);
            UnityEditor.Handles.Label(labelPos, $"[Zona {m_SpeedLimitKmh} km/h | Tol: {m_ToleranceKmh:F0} km/h]");
#endif
        }
    }
}
