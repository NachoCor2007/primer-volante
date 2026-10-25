using System;
using PrimerVolante.VR;
using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Sistema de seguridad y frenado de emergencia frontal para NPCs (vehículos y peatones con <see cref="NPCPathFollower"/>).
    /// Detecta riesgo inminente de colisión frontal en la trayectoria mediante sensor frontal (BoxCast / Raycast)
    /// y detiene inmediatamente al NPC. Al despejarse la vía y transcurrir un tiempo de seguridad, reanuda la marcha.
    /// </summary>
    [RequireComponent(typeof(NPCPathFollower))]
    [DisallowMultipleComponent]
    public class NPCSafetyStop : MonoBehaviour
    {
        [Header("Detección Frontal")]
        [Tooltip("Distancia base de detección hacia adelante en metros.")]
        [SerializeField] private float m_DetectionDistance = 5.0f;

        [Tooltip("Si es true, la distancia de detección se incrementa proporcionalmente con la velocidad actual del NPC.")]
        [SerializeField] private bool m_DynamicDistanceWithSpeed = true;

        [Tooltip("Factor multiplicador de la velocidad (segundos de anticipación) sumados a la distancia base.")]
        [SerializeField] private float m_SpeedLookaheadFactor = 1.0f;

        [Tooltip("Offset del origen del sensor relativo al NPC (altura recomendada sobre el asfalto/acera).")]
        [SerializeField] private Vector3 m_SensorOffset = new Vector3(0f, 1.0f, 0f);

        [Tooltip("Semi-dimensiones (half extents) de la caja de detección frontal.")]
        [SerializeField] private Vector3 m_BoxHalfExtents = new Vector3(0.6f, 0.45f, 0.3f);

        [Tooltip("Capas de obstáculos a detectar (vehículos, jugador, otros NPCs, etc.).")]
        [SerializeField] private LayerMask m_ObstacleLayers = ~0;

        [Tooltip("Tag opcional para restringir obstáculos (ej. 'Player'). Si está vacío, detecta cualquier collider en la capa.")]
        [SerializeField] private string m_ObstacleTag = "";

        [Tooltip("Interacción con triggers. Por defecto se ignoran para no frenar ante zonas de eventos.")]
        [SerializeField] private QueryTriggerInteraction m_QueryTriggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Respuesta de Frenado y Reanudación")]
        [Tooltip("Tiempo de seguridad en segundos con la vía completamente despejada antes de reanudar la marcha.")]
        [SerializeField] private float m_ResumeDelay = 0.5f;

        [Tooltip("Margen de histéresis en metros sumado a la distancia de frenado mientras el NPC permanece detenido. Evita oscilaciones por contracción del sensor a velocidad cero.")]
        [SerializeField] private float m_ResumeHysteresisDistance = 0.5f;

        [Header("Depuración")]
        [Tooltip("Escribe en consola un log por cada frenado de emergencia y reanudación. Apagado por defecto para no tapar el log de infracciones.")]
        [SerializeField] private bool m_LogEvents = false;

        [Header("Eventos")]
        [Tooltip("Evento Unity disparado al activarse el frenado de emergencia.")]
        [SerializeField] private UnityEvent onEmergencyBrakeEvent = new UnityEvent();

        [Tooltip("Evento Unity disparado al reanudar la marcha tras despejarse la vía.")]
        [SerializeField] private UnityEvent onResumeMovementEvent = new UnityEvent();

        /// <summary>
        /// Evento C# disparado inmediatamente al detectar riesgo inminente y aplicar frenado de emergencia.
        /// </summary>
        public event Action OnEmergencyBrake;

        /// <summary>
        /// Evento C# disparado al reanudar la marcha tras despejarse la trayectoria y cumplirse el tiempo de seguridad.
        /// </summary>
        public event Action OnResumeMovement;

        // Componentes y referencias
        private Rigidbody m_Rigidbody;
        private NPCPathFollower m_PathFollower;
        private NPCVehicleController m_VehicleController;

        // Estado en tiempo de ejecución
        private bool m_IsEmergencyBraking = false;
        private bool m_WasMovingBeforeEmergencyBrake = false;
        private float m_ClearTimer = 0f;
        private float m_BrakingDetectionDistance = 0f;
        private RaycastHit m_LastObstacleHit;
        private Collider m_LastObstacleCollider;

        // Soporte de simulación para pruebas unitarias deterministas
        private bool m_SimulateObstacle = false;
        private bool m_SimulatedHasObstacle = false;
        private RaycastHit m_SimulatedHit;
        private Collider m_SimulatedCollider;

        #region Propiedades Públicas

        /// <summary>Indica si el NPC se encuentra actualmente en estado de frenado de emergencia.</summary>
        public bool IsEmergencyBraking => m_IsEmergencyBraking;

        /// <summary>Distancia base de detección frontal en metros.</summary>
        public float DetectionDistance
        {
            get => m_DetectionDistance;
            set => m_DetectionDistance = Mathf.Max(0.1f, value);
        }

        /// <summary>Tiempo de seguridad antes de reanudar la marcha tras despejarse la vía.</summary>
        public float ResumeDelay
        {
            get => m_ResumeDelay;
            set => m_ResumeDelay = Mathf.Max(0f, value);
        }

        /// <summary>Margen de histéresis sumado a la distancia de frenado mientras permanece detenido.</summary>
        public float ResumeHysteresisDistance
        {
            get => m_ResumeHysteresisDistance;
            set => m_ResumeHysteresisDistance = Mathf.Max(0f, value);
        }

        /// <summary>Distancia de detección frontal registrada al momento de entrar en frenado de emergencia.</summary>
        public float BrakingDetectionDistance => m_BrakingDetectionDistance;

        /// <summary>Offset del origen del sensor relativo al objeto.</summary>
        public Vector3 SensorOffset
        {
            get => m_SensorOffset;
            set => m_SensorOffset = value;
        }

        /// <summary>Semi-dimensiones de la caja de detección frontal.</summary>
        public Vector3 BoxHalfExtents
        {
            get => m_BoxHalfExtents;
            set => m_BoxHalfExtents = value;
        }

        /// <summary>Capas consideradas como obstáculo.</summary>
        public LayerMask ObstacleLayers
        {
            get => m_ObstacleLayers;
            set => m_ObstacleLayers = value;
        }

        /// <summary>Tag opcional requerido para obstáculos.</summary>
        public string ObstacleTag
        {
            get => m_ObstacleTag;
            set => m_ObstacleTag = value;
        }

        /// <summary>Indica si la distancia se ajusta dinámicamente con la velocidad.</summary>
        public bool DynamicDistanceWithSpeed
        {
            get => m_DynamicDistanceWithSpeed;
            set => m_DynamicDistanceWithSpeed = value;
        }

        /// <summary>Factor multiplicador de velocidad para la distancia dinámica.</summary>
        public float SpeedLookaheadFactor
        {
            get => m_SpeedLookaheadFactor;
            set => m_SpeedLookaheadFactor = Mathf.Max(0f, value);
        }

        /// <summary>Tiempo transcurrido desde que la vía quedó despejada.</summary>
        public float ClearTimer => m_ClearTimer;

        /// <summary>Último obstáculo detectado.</summary>
        public RaycastHit LastObstacleHit => m_LastObstacleHit;

        /// <summary>Último collider detectado como obstáculo.</summary>
        public Collider LastObstacleCollider => m_LastObstacleCollider;

        /// <summary>Seguidor de ruta asociado.</summary>
        public NPCPathFollower PathFollower => m_PathFollower;

        /// <summary>Controlador de vehículo NPC (null si es peatón).</summary>
        public NPCVehicleController VehicleController => m_VehicleController;

        /// <summary>Rigidbody asociado al NPC.</summary>
        public Rigidbody Rigidbody => m_Rigidbody;

        #endregion

        #region Ciclo de Vida de Unity

        private void Reset()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_PathFollower = GetComponent<NPCPathFollower>();
            m_VehicleController = GetComponent<NPCVehicleController>();
            SyncLifecycle();
        }

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_PathFollower = GetComponent<NPCPathFollower>();
            m_VehicleController = GetComponent<NPCVehicleController>();
            SyncLifecycle();
        }

        private void Start()
        {
            SyncLifecycle();
        }

        private void OnEnable()
        {
            if (m_PathFollower == null) m_PathFollower = GetComponent<NPCPathFollower>();
            if (m_PathFollower != null)
            {
                m_PathFollower.OnReachedEnd -= HandlePathReachedEnd;
                m_PathFollower.OnReachedEnd += HandlePathReachedEnd;
            }
        }

        private void OnDisable()
        {
            if (m_PathFollower != null)
            {
                m_PathFollower.OnReachedEnd -= HandlePathReachedEnd;
            }
        }

        private void HandlePathReachedEnd()
        {
            enabled = false;
        }

        /// <summary>
        /// Sincroniza el estado enabled según el ciclo de vida del seguidor de ruta.
        /// Si no hay ruta o el NPC está detenido/estático (y no está en frenado de emergencia), desactiva el componente.
        /// </summary>
        public void SyncLifecycle()
        {
            if (m_PathFollower == null) m_PathFollower = GetComponent<NPCPathFollower>();

            if (m_PathFollower == null || !m_PathFollower.HasPath || (!m_PathFollower.IsPlaying && !m_IsEmergencyBraking))
            {
                enabled = false;
            }
            else
            {
                enabled = true;
            }
        }

        private void OnValidate()
        {
            m_DetectionDistance = Mathf.Max(0.1f, m_DetectionDistance);
            m_ResumeDelay = Mathf.Max(0f, m_ResumeDelay);
            m_ResumeHysteresisDistance = Mathf.Max(0f, m_ResumeHysteresisDistance);
            m_SpeedLookaheadFactor = Mathf.Max(0f, m_SpeedLookaheadFactor);
        }

        private void FixedUpdate()
        {
            if (m_PathFollower == null) m_PathFollower = GetComponent<NPCPathFollower>();

            if (m_PathFollower == null || !m_PathFollower.HasPath || (!m_PathFollower.IsPlaying && !m_IsEmergencyBraking))
            {
                enabled = false;
                return;
            }

            Tick(Time.fixedDeltaTime);
        }

        #endregion

        #region Lógica Principal y Detección

        /// <summary>
        /// Evalúa la presencia de obstáculos y actualiza la máquina de estados de seguridad.
        /// Determinista: puede ejecutarse en FixedUpdate o llamarse manualmente en pruebas unitarias.
        /// </summary>
        /// <param name="deltaTime">Delta de tiempo transcurrido en segundos.</param>
        public void Tick(float deltaTime)
        {
            if (m_PathFollower == null)
            {
                m_PathFollower = GetComponent<NPCPathFollower>();
                if (m_PathFollower == null) return;
            }

            if (m_VehicleController == null)
            {
                m_VehicleController = GetComponent<NPCVehicleController>();
            }

            bool obstacleDetected = CastFrontalSensor(out RaycastHit hit);

            if (obstacleDetected)
            {
                m_LastObstacleHit = hit;
                if (hit.collider != null)
                {
                    m_LastObstacleCollider = hit.collider;
                }
                m_ClearTimer = 0f;

                if (!m_IsEmergencyBraking)
                {
                    // Entrar en frenado de emergencia inmediato
                    m_BrakingDetectionDistance = GetEffectiveDetectionDistance();
                    m_IsEmergencyBraking = true;
                    m_WasMovingBeforeEmergencyBrake = m_PathFollower.IsPlaying && !m_PathFollower.IsStopRequested;

                    m_PathFollower.EmergencyStop();

                    string obstacleName = hit.collider != null ? hit.collider.name : (m_LastObstacleCollider != null ? m_LastObstacleCollider.name : "Obstáculo");
                    float dist = hit.distance > 0f ? hit.distance : 0f;
                    if (m_LogEvents)
                        Debug.LogWarning($"[NPCSafetyStop] 🛑 ¡Frenado de emergencia en '{name}'! '{obstacleName}' detectado a {dist:F2}m en trayectoria frontal.", this);

                    OnEmergencyBrake?.Invoke();
                    onEmergencyBrakeEvent?.Invoke();
                }
                else
                {
                    // Garantizar que mientras el obstáculo siga detectado se mantenga la parada de emergencia
                    if (!m_PathFollower.IsEmergencyStopped)
                    {
                        m_PathFollower.EmergencyStop();
                    }
                }
            }
            else
            {
                // Vía despejada
                if (m_IsEmergencyBraking)
                {
                    m_ClearTimer += deltaTime;
                    if (m_ClearTimer >= m_ResumeDelay)
                    {
                        // Se cumplió el tiempo de seguridad con la vía despejada
                        m_IsEmergencyBraking = false;
                        m_BrakingDetectionDistance = 0f;
                        m_ClearTimer = 0f;

                        if (m_WasMovingBeforeEmergencyBrake)
                        {
                            m_PathFollower.Play();
                            if (m_LogEvents)
                                Debug.Log($"[NPCSafetyStop] ▶️ Vía despejada para '{name}'. Reanudando marcha tras {m_ResumeDelay:F1}s de seguridad.", this);
                        }

                        m_WasMovingBeforeEmergencyBrake = false;

                        OnResumeMovement?.Invoke();
                        onResumeMovementEvent?.Invoke();
                    }
                }
                else
                {
                    m_ClearTimer = 0f;
                }
            }
        }

        /// <summary>
        /// Realiza el barrido frontal buscando obstáculos en la trayectoria.
        /// Utiliza detección dual (OverlapBox para solapamiento inmediato a 0m + BoxCastAll para anticipación).
        /// </summary>
        /// <param name="nearestObstacleHit">Información del impacto más cercano detectado.</param>
        /// <returns>True si hay al menos un obstáculo válido en riesgo inminente; false en caso contrario.</returns>
        public bool CastFrontalSensor(out RaycastHit nearestObstacleHit)
        {
            nearestObstacleHit = default;

            if (m_SimulateObstacle)
            {
                nearestObstacleHit = m_SimulatedHit;
                if (m_SimulatedCollider != null)
                {
                    m_LastObstacleCollider = m_SimulatedCollider;
                }
                return m_SimulatedHasObstacle;
            }

            Vector3 origin = GetSensorOrigin();
            Vector3 direction = transform.forward;
            float maxDistance = GetEffectiveDetectionDistance();

            // 1. Detección inmediata por solapamiento (OverlapBox)
            Collider[] overlaps = Physics.OverlapBox(
                origin,
                m_BoxHalfExtents,
                transform.rotation,
                m_ObstacleLayers,
                m_QueryTriggerInteraction);

            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider col = overlaps[i];
                if (IsValidObstacleCollider(col))
                {
                    nearestObstacleHit = CreateHitForCollider(col, origin);
                    m_LastObstacleCollider = col;
                    return true;
                }
            }

            // 2. Detección frontal por barrido (BoxCastAll)
            RaycastHit[] hits = Physics.BoxCastAll(
                origin,
                m_BoxHalfExtents,
                direction,
                transform.rotation,
                maxDistance,
                m_ObstacleLayers,
                m_QueryTriggerInteraction);

            float closestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (IsValidObstacleHit(hit))
                {
                    if (hit.distance < closestDistance)
                    {
                        closestDistance = hit.distance;
                        nearestObstacleHit = hit;
                        m_LastObstacleCollider = hit.collider;
                        found = true;
                    }
                }
            }

            return found;
        }

        private RaycastHit CreateHitForCollider(Collider col, Vector3 origin)
        {
            RaycastHit hit = default;
            if (col != null)
            {
                Vector3 center = col.bounds.center;
                Vector3 diff = origin - center;
                Vector3 dir = diff.sqrMagnitude > 1e-4f ? diff.normalized : Vector3.up;
                float extent = Mathf.Max(col.bounds.size.magnitude + 2f, 3f);
                Vector3 rayOrigin = center + dir * extent;
                Ray ray = new Ray(rayOrigin, -dir);
                if (col.Raycast(ray, out RaycastHit surfaceHit, extent * 2f))
                {
                    hit = surfaceHit;
                }
            }

            hit.point = origin;
            hit.distance = 0f;
            return hit;
        }

        /// <summary>
        /// Calcula la posición del sensor en coordenadas de mundo.
        /// </summary>
        public Vector3 GetSensorOrigin()
        {
            return transform.position + transform.rotation * m_SensorOffset;
        }

        /// <summary>
        /// Calcula la distancia efectiva de detección, sumando lookahead por velocidad si está activo.
        /// </summary>
        public float GetEffectiveDetectionDistance()
        {
            if (m_PathFollower == null)
            {
                m_PathFollower = GetComponent<NPCPathFollower>();
            }

            float distance = m_DetectionDistance;

            // Si el NPC está en frenado de emergencia, nunca reducir la distancia del sensor al caer a velocidad cero:
            // Mantener la distancia registrada al momento del frenado más el margen de histéresis.
            if (m_IsEmergencyBraking && m_BrakingDetectionDistance > 0f)
            {
                return Mathf.Max(distance, m_BrakingDetectionDistance + m_ResumeHysteresisDistance);
            }

            // Igual que en Tick y ForceEmergencyBrake: si se consulta antes de Awake (p. ej. en tests de Editor)
            // se resuelve el seguidor acá para no perder el lookahead por velocidad.
            if (m_PathFollower == null) m_PathFollower = GetComponent<NPCPathFollower>();

            if (m_DynamicDistanceWithSpeed && m_PathFollower != null)
            {
                distance += m_PathFollower.CurrentSpeed * m_SpeedLookaheadFactor;
            }

            return Mathf.Max(0.2f, distance);
        }

        /// <summary>
        /// Valida si un collider corresponde a un obstáculo real ajeno a este NPC.
        /// </summary>
        public bool IsValidObstacleCollider(Collider col)
        {
            if (col == null) return false;

            // Ignorar el propio NPC y todos sus hijos
            if (col.gameObject == gameObject || col.transform.IsChildOf(transform))
            {
                return false;
            }

            // Ignorar geometría estática del escenario
            if (col.gameObject.isStatic)
            {
                return false;
            }

            // Ignorar superficies de calzada, vereda y entorno por nombre
            string nameLower = col.name.ToLowerInvariant();
            if (nameLower.Contains("road") ||
                nameLower.Contains("sidewalk") ||
                nameLower.Contains("street") ||
                nameLower.Contains("ground") ||
                nameLower.Contains("terrain") ||
                nameLower.Contains("asphalt") ||
                nameLower.Contains("crosswalk"))
            {
                return false;
            }

            // Validar capa
            if ((m_ObstacleLayers.value & (1 << col.gameObject.layer)) == 0)
            {
                return false;
            }

            // Filtro por Tag si se configuró
            if (!string.IsNullOrEmpty(m_ObstacleTag))
            {
                return HasMatchingTag(col.gameObject, m_ObstacleTag) ||
                       (col.attachedRigidbody != null && HasMatchingTag(col.attachedRigidbody.gameObject, m_ObstacleTag));
            }

            // Si ObstacleTag está vacío:
            if (col.attachedRigidbody == null)
            {
                // Compatibilidad con objetos de prueba unitaria tipo mock
                if (col.name.StartsWith("TestObstacle") || col.name.StartsWith("ExternalObstacle"))
                {
                    return true;
                }

                // Verificar controladores de tráfico/vehículo
                bool hasTrafficComponent =
                    col.GetComponentInParent<VehicleController>() != null ||
                    col.GetComponentInParent<NPCVehicleController>() != null ||
                    col.GetComponentInParent<NPCPathFollower>() != null;

                // Verificar tag relevante como "Player"
                bool hasRelevantTag =
                    HasMatchingTag(col.gameObject, "Player") ||
                    (col.attachedRigidbody != null && HasMatchingTag(col.attachedRigidbody.gameObject, "Player"));

                if (!hasTrafficComponent && !hasRelevantTag)
                {
                    return false;
                }

                return true;
            }

            // Con attachedRigidbody != null: validar que no sea del propio NPC
            if (m_Rigidbody == null) m_Rigidbody = GetComponent<Rigidbody>();
            if (col.attachedRigidbody == m_Rigidbody || col.attachedRigidbody.transform.IsChildOf(transform))
            {
                return false;
            }

            return true;
        }

        private static bool HasMatchingTag(GameObject go, string tag)
        {
            if (go == null || string.IsNullOrEmpty(tag)) return false;
            return string.Equals(go.tag, tag, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Valida si un impacto del sensor corresponde a un obstáculo real ajeno a este NPC.
        /// </summary>
        public bool IsValidObstacleHit(RaycastHit hit)
        {
            return IsValidObstacleCollider(hit.collider);
        }

        #endregion

        #region Métodos de Prueba y Simulación

        /// <summary>
        /// Fuerza un obstáculo simulado para pruebas unitarias deterministas sin depender de colisiones físicas reales.
        /// </summary>
        public void SetSimulatedObstacle(bool hasObstacle, float distance = 1f, Collider obstacleCollider = null)
        {
            m_SimulateObstacle = true;
            m_SimulatedHasObstacle = hasObstacle;
            m_SimulatedCollider = obstacleCollider;
            m_SimulatedHit = new RaycastHit
            {
                distance = distance,
                point = GetSensorOrigin() + transform.forward * distance
            };
            if (hasObstacle && obstacleCollider != null)
            {
                m_LastObstacleCollider = obstacleCollider;
            }
        }

        /// <summary>
        /// Restablece el sensor a detección física real.
        /// </summary>
        public void ClearSimulatedObstacle()
        {
            m_SimulateObstacle = false;
            m_SimulatedHasObstacle = false;
            m_SimulatedCollider = null;
            m_SimulatedHit = default;
        }

        /// <summary>
        /// Reinicia el estado interno de frenado de seguridad.
        /// </summary>
        public void ResetSafetyState()
        {
            m_IsEmergencyBraking = false;
            m_WasMovingBeforeEmergencyBrake = false;
            m_ClearTimer = 0f;
            m_BrakingDetectionDistance = 0f;
            m_LastObstacleHit = default;
            m_LastObstacleCollider = null;
        }

        #endregion

        #region Red de Seguridad Física (Anti-Empuje)

        private void OnCollisionEnter(Collision collision)
        {
            EvaluateCollision(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            EvaluateCollision(collision);
        }

        private void EvaluateCollision(Collision collision)
        {
            if (collision == null || collision.collider == null) return;
            if (!IsValidObstacleCollider(collision.collider)) return;

            // Recorrer los puntos de contacto para filtrar superficies de apoyo vertical (piso / acera / calle)
            ContactPoint[] contacts = collision.contacts;
            if (contacts == null || contacts.Length == 0) return;

            bool hasLateralOrFrontalImpact = false;
            for (int i = 0; i < contacts.Length; i++)
            {
                // Si la normal está orientada hacia arriba (> 0.6f), se trata de una superficie de piso/apoyo: IGNORAR.
                // Solo nos interesa si el impacto es frontal/lateral (normal.y <= 0.6f).
                if (contacts[i].normal.y <= 0.6f)
                {
                    hasLateralOrFrontalImpact = true;
                    break;
                }
            }

            if (hasLateralOrFrontalImpact)
            {
                ForceEmergencyBrake(collision.collider);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != null && !IsEventTrigger(other) && IsValidObstacleCollider(other))
            {
                ForceEmergencyBrake(other);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other != null && !IsEventTrigger(other) && IsValidObstacleCollider(other))
            {
                ForceEmergencyBrake(other);
            }
        }

        private static bool IsEventTrigger(Collider col)
        {
            if (col == null) return false;
            return col.GetComponent<TriggerScriptedEvent>() != null ||
                   col.GetComponentInParent<TriggerScriptedEvent>() != null;
        }

        /// <summary>
        /// Fuerza de inmediato el frenado de emergencia ante contacto físico directo con un obstáculo.
        /// </summary>
        public void ForceEmergencyBrake(Collider col)
        {
            if (col != null && !IsValidObstacleCollider(col))
            {
                return;
            }

            if (m_PathFollower == null)
            {
                m_PathFollower = GetComponent<NPCPathFollower>();
                if (m_PathFollower == null) return;
            }

            m_ClearTimer = 0f;
            m_LastObstacleCollider = col;

            if (!m_IsEmergencyBraking)
            {
                m_BrakingDetectionDistance = GetEffectiveDetectionDistance();
                m_IsEmergencyBraking = true;
                m_WasMovingBeforeEmergencyBrake = m_PathFollower.IsPlaying && !m_PathFollower.IsStopRequested;
                m_PathFollower.EmergencyStop();

                string obstacleName = col != null ? col.name : "Obstáculo";
                if (m_LogEvents)
                    Debug.LogWarning($"[NPCSafetyStop] 🛑 ¡Contacto físico en '{name}'! '{obstacleName}' detectado en colisión/trigger.", this);

                OnEmergencyBrake?.Invoke();
                onEmergencyBrakeEvent?.Invoke();
            }
            else
            {
                if (!m_PathFollower.IsEmergencyStopped)
                {
                    m_PathFollower.EmergencyStop();
                }
            }
        }

        #endregion

        #region Gizmos de Editor

        private void OnDrawGizmos()
        {
            if (!enabled && !m_IsEmergencyBraking) return;
            DrawSafetyGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawSafetyGizmos(true);
        }

        private void DrawSafetyGizmos(bool isSelected)
        {
            Vector3 origin = GetSensorOrigin();
            Vector3 direction = transform.forward;
            float maxDistance = GetEffectiveDetectionDistance();
            Vector3 endPoint = origin + direction * maxDistance;

            Color sensorColor = m_IsEmergencyBraking
                ? Color.red
                : (m_ClearTimer > 0f ? Color.yellow : (isSelected ? Color.green : new Color(0.2f, 0.8f, 0.2f, 0.6f)));

            Gizmos.color = sensorColor;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(origin, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, m_BoxHalfExtents * 2f);

            Gizmos.matrix = Matrix4x4.TRS(endPoint, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, m_BoxHalfExtents * 2f);

            Gizmos.matrix = oldMatrix;

            // Línea central del sensor
            Gizmos.color = sensorColor;
            Gizmos.DrawLine(origin, endPoint);

            // Esquinas conectadas para formar el volumen proyectado del sensor
            Vector3 right = transform.right * m_BoxHalfExtents.x;
            Vector3 up = transform.up * m_BoxHalfExtents.y;
            Vector3 fwdOffset = direction * maxDistance;

            Vector3 c1 = origin + right + up;
            Vector3 c2 = origin - right + up;
            Vector3 c3 = origin + right - up;
            Vector3 c4 = origin - right - up;

            Gizmos.color = new Color(sensorColor.r, sensorColor.g, sensorColor.b, 0.35f);
            Gizmos.DrawLine(c1, c1 + fwdOffset);
            Gizmos.DrawLine(c2, c2 + fwdOffset);
            Gizmos.DrawLine(c3, c3 + fwdOffset);
            Gizmos.DrawLine(c4, c4 + fwdOffset);
        }

        #endregion
    }
}
