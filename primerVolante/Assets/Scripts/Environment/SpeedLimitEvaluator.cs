using UnityEngine;
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

        [Header("Vinculación Opcional")]
        [Tooltip("Referencia al cartel vial asociado (si se asigna, sincroniza el límite automáticamente).")]
        [SerializeField] private SpeedLimitSignDisplay m_SignDisplay;

        // Estado en tiempo de ejecución
        private VehicleController m_CurrentVehicle;
        private bool m_HasInfraction;
        private float m_MaxSpeedRecorded;
        private float m_LastAlertTime = -100f;
        private BoxCollider m_BoxCollider;
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

        private void OnTriggerEnter(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null) return;

            // Si ya estamos evaluando otro vehículo distinto, ignorarlo
            if (m_CurrentVehicle != null && m_CurrentVehicle != vehicle) return;

            bool isFirstCollider = (m_ActiveColliders.Count == 0);
            m_ActiveColliders.Add(other);

            // Solo inicializar y notificar con el primer colisionador del auto que ingresa
            if (isFirstCollider)
            {
                m_CurrentVehicle = vehicle;
                m_HasInfraction = false;
                m_MaxSpeedRecorded = vehicle.CurrentSpeedKmh;
                m_LastAlertTime = -m_AlertCooldown;

                SyncWithSign();

                Debug.Log($"[TRAFFIC-SPEED] ℹ Ingreso a tramo con límite de {m_SpeedLimitKmh} km/h");
            }
        }

        private void OnTriggerStay(Collider other)
        {
            var vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null || vehicle != m_CurrentVehicle) return;

            float currentSpeed = vehicle.CurrentSpeedKmh;
            if (currentSpeed > m_MaxSpeedRecorded)
            {
                m_MaxSpeedRecorded = currentSpeed;
            }

            if (IsExcessSpeed(currentSpeed))
            {
                m_HasInfraction = true;

                if (Time.time - m_LastAlertTime >= m_AlertCooldown)
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

                if (m_HasInfraction)
                {
                    Debug.Log($"[TRAFFIC-SPEED] ❌ Tramo finalizado con infracción. Velocidad máxima alcanzada: {m_MaxSpeedRecorded:F1} km/h (Permitido: {m_SpeedLimitKmh} km/h)");
                }
                else
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
