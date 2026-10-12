using System;
using System.Collections.Generic;
using PrimerVolante.VR;
using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Zona de trigger que activa eventos guionados cuando el vehículo del jugador entra en el área.
    /// Permite que NPCs (vehículos y peatones) y semáforos que se encontraban detenidos o en espera
    /// inicien su marcha o ciclo de funcionamiento de manera sincronizada.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class TriggerScriptedEvent : MonoBehaviour
    {
        [Header("Detección de Vehículo")]
        [Tooltip("Si es true, busca el componente VehicleController en el objeto entrante o sus componentes padre.")]
        [SerializeField] private bool m_CheckVehicleController = true;

        [Tooltip("Tag requerido opcional para el vehículo (ej. 'Player'). Si está vacío, solo valida VehicleController.")]
        [SerializeField] private string m_RequiredTag = "";

        [Tooltip("Máscara de capas permitidas para activar este trigger.")]
        [SerializeField] private LayerMask m_TriggerLayers = ~0;

        [Header("Acciones y Objetivos")]
        [Tooltip("NPCs (vehículos o peatones) que deben comenzar a moverse llamando a Play().")]
        [SerializeField] private List<NPCPathFollower> m_TargetNPCs = new List<NPCPathFollower>();

        [Tooltip("Semáforos que deben iniciar su ciclo o forzar un estado llamando a TriggerStart().")]
        [SerializeField] private List<TrafficLightController> m_TargetTrafficLights = new List<TrafficLightController>();

        [Tooltip("Estado inicial con el que arrancan los semáforos asignados.")]
        [SerializeField] private TrafficLightState m_TrafficLightInitialState = TrafficLightState.Red;

        [Tooltip("Si es true, llama a TriggerStart() en los semáforos; si es false, llama a SetState().")]
        [SerializeField] private bool m_StartTrafficLightCycle = true;

        [Header("Control de Disparo")]
        [Tooltip("Retardo opcional en segundos antes de ejecutar las acciones tras la entrada del jugador.")]
        [SerializeField] private float m_Delay = 0f;

        [Tooltip("Si es true, el evento se ejecuta una única vez.")]
        [SerializeField] private bool m_TriggerOnce = true;

        [Header("Eventos")]
        [Tooltip("Evento Unity disparado cuando el trigger es activado.")]
        [SerializeField] private UnityEvent onTriggerActivatedEvent = new UnityEvent();

        /// <summary>
        /// Evento C# disparado inmediatamente al activarse el trigger, pasando el collider del jugador.
        /// </summary>
        public event Action<Collider> OnTriggerActivated;

        // Estado en tiempo de ejecución
        private bool m_HasTriggered = false;
        private bool m_IsDelayPending = false;
        private float m_RemainingDelay = 0f;
        private Collider m_TriggerCollider;

        #region Propiedades Públicas

        /// <summary>NPCs objetivo configurados para ser activados.</summary>
        public List<NPCPathFollower> TargetNPCs => m_TargetNPCs;

        /// <summary>Semáforos objetivo configurados para ser activados.</summary>
        public List<TrafficLightController> TargetTrafficLights => m_TargetTrafficLights;

        /// <summary>Retardo en segundos entre la activación del trigger y la ejecución de las acciones.</summary>
        public float Delay
        {
            get => m_Delay;
            set => m_Delay = Mathf.Max(0f, value);
        }

        /// <summary>Indica si el trigger debe ejecutarse únicamente una vez.</summary>
        public bool TriggerOnce
        {
            get => m_TriggerOnce;
            set => m_TriggerOnce = value;
        }

        /// <summary>Indica si el trigger ya fue activado al menos una vez.</summary>
        public bool HasTriggered => m_HasTriggered;

        /// <summary>Indica si hay un retardo pendiente de cumplirse antes de ejecutar las acciones.</summary>
        public bool IsDelayPending => m_IsDelayPending;

        /// <summary>Tiempo restante de retardo en segundos.</summary>
        public float RemainingDelay => Mathf.Max(0f, m_RemainingDelay);

        /// <summary>Tag requerido opcional para el vehículo activador.</summary>
        public string RequiredTag
        {
            get => m_RequiredTag;
            set => m_RequiredTag = value;
        }

        /// <summary>Indica si se busca el componente VehicleController.</summary>
        public bool CheckVehicleController
        {
            get => m_CheckVehicleController;
            set => m_CheckVehicleController = value;
        }

        /// <summary>Estado inicial que se asigna a los semáforos al disparar.</summary>
        public TrafficLightState TrafficLightInitialState
        {
            get => m_TrafficLightInitialState;
            set => m_TrafficLightInitialState = value;
        }

        /// <summary>Si es true, inicia el ciclo del semáforo con TriggerStart; si es false, impone SetState.</summary>
        public bool StartTrafficLightCycle
        {
            get => m_StartTrafficLightCycle;
            set => m_StartTrafficLightCycle = value;
        }

        /// <summary>Evento Unity invocable al activarse el trigger.</summary>
        public UnityEvent OnTriggerActivatedEvent => onTriggerActivatedEvent;

        #endregion

        #region Ciclo de Vida de Unity

        private void Reset()
        {
            EnsureTriggerCollider();
        }

        private void Awake()
        {
            EnsureTriggerCollider();
        }

        private void OnValidate()
        {
            m_Delay = Mathf.Max(0f, m_Delay);
            EnsureTriggerCollider();
        }

        private void Update()
        {
            if (m_IsDelayPending)
            {
                Tick(Time.deltaTime);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryTrigger(other);
        }

        #endregion

        #region Métodos Públicos

        /// <summary>
        /// Evalúa si el collider entrante es válido para activar el trigger y dispara las acciones.
        /// </summary>
        /// <param name="other">Collider que ingresó a la zona.</param>
        /// <returns>True si el evento fue activado; false si fue ignorado o ya se disparó.</returns>
        public bool TryTrigger(Collider other)
        {
            if (other == null) return false;
            if (m_TriggerOnce && m_HasTriggered) return false;

            if (!IsValidTriggerSource(other))
            {
                return false;
            }

            m_HasTriggered = true;

            OnTriggerActivated?.Invoke(other);
            onTriggerActivatedEvent?.Invoke();

            if (m_Delay > 0f)
            {
                m_RemainingDelay = m_Delay;
                m_IsDelayPending = true;
            }
            else
            {
                ExecuteActionsImmediately();
            }

            return true;
        }

        /// <summary>
        /// Valida si el collider cumple con las condiciones configuradas de vehículo o tag.
        /// </summary>
        public bool IsValidTriggerSource(Collider other)
        {
            if (other == null) return false;

            // Filtro por capa
            if ((m_TriggerLayers.value & (1 << other.gameObject.layer)) == 0)
            {
                return false;
            }

            // Filtro por componente VehicleController
            if (m_CheckVehicleController)
            {
                VehicleController vehicle = other.GetComponentInParent<VehicleController>();
                if (vehicle != null)
                {
                    return true;
                }
            }

            // Filtro por Tag
            if (!string.IsNullOrEmpty(m_RequiredTag))
            {
                if (other.CompareTag(m_RequiredTag) ||
                    (other.transform.root != null && other.transform.root.CompareTag(m_RequiredTag)))
                {
                    return true;
                }
            }

            // Si ambos filtros estaban desactivados, cualquier collider sobre la capa activa
            if (!m_CheckVehicleController && string.IsNullOrEmpty(m_RequiredTag))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ejecuta de inmediato las acciones sobre los NPCs y semáforos asignados.
        /// </summary>
        public void ExecuteActionsImmediately()
        {
            m_IsDelayPending = false;
            m_RemainingDelay = 0f;

            // 1. Activar NPCs objetivo
            if (m_TargetNPCs != null)
            {
                for (int i = 0; i < m_TargetNPCs.Count; i++)
                {
                    NPCPathFollower npc = m_TargetNPCs[i];
                    if (npc != null)
                    {
                        npc.Play();
                    }
                }
            }

            // 2. Activar Semáforos objetivo
            if (m_TargetTrafficLights != null)
            {
                for (int i = 0; i < m_TargetTrafficLights.Count; i++)
                {
                    TrafficLightController light = m_TargetTrafficLights[i];
                    if (light != null)
                    {
                        if (m_StartTrafficLightCycle)
                        {
                            light.TriggerStart(m_TrafficLightInitialState);
                        }
                        else
                        {
                            light.SetState(m_TrafficLightInitialState);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Avanza el contador de retardo de manera determinista por una cantidad de segundos.
        /// Permite ejecución en Update() y pruebas unitarias instantáneas sin depender de tiempo real.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!m_IsDelayPending || deltaTime <= 0f) return;

            m_RemainingDelay -= deltaTime;
            if (m_RemainingDelay <= 0f)
            {
                ExecuteActionsImmediately();
            }
        }

        /// <summary>
        /// Reinicia el estado del trigger permitiendo volver a dispararlo.
        /// </summary>
        public void ResetTrigger()
        {
            m_HasTriggered = false;
            m_IsDelayPending = false;
            m_RemainingDelay = 0f;
        }

        #endregion

        #region Métodos Privados y Gizmos

        private void EnsureTriggerCollider()
        {
            if (m_TriggerCollider == null)
            {
                m_TriggerCollider = GetComponent<Collider>();
            }

            if (m_TriggerCollider != null && !m_TriggerCollider.isTrigger)
            {
                m_TriggerCollider.isTrigger = true;
            }
        }

        private void OnDrawGizmos()
        {
            DrawTriggerGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawTriggerGizmos(true);
        }

        private void DrawTriggerGizmos(bool isSelected)
        {
            EnsureTriggerCollider();

            // Color del volumen del trigger
            Color fillColor = m_HasTriggered
                ? new Color(0.2f, 0.85f, 0.3f, isSelected ? 0.45f : 0.25f)
                : new Color(0.1f, 0.7f, 1.0f, isSelected ? 0.45f : 0.25f);
            Color wireColor = m_HasTriggered
                ? new Color(0.1f, 1f, 0.2f, isSelected ? 1f : 0.7f)
                : new Color(0f, 0.8f, 1f, isSelected ? 1f : 0.7f);

            if (m_TriggerCollider is BoxCollider box)
            {
                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;

                Gizmos.color = fillColor;
                Gizmos.DrawCube(box.center, box.size);

                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(box.center, box.size);

                Gizmos.matrix = oldMatrix;
            }
            else if (m_TriggerCollider is SphereCollider sphere)
            {
                Vector3 center = transform.TransformPoint(sphere.center);
                float radius = sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);

                Gizmos.color = fillColor;
                Gizmos.DrawSphere(center, radius);

                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(center, radius);
            }
            else if (m_TriggerCollider != null)
            {
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(m_TriggerCollider.bounds.center, m_TriggerCollider.bounds.size);
            }

            Vector3 triggerOrigin = m_TriggerCollider != null ? m_TriggerCollider.bounds.center : transform.position;

            // Líneas/flechas hacia NPCs objetivo
            if (m_TargetNPCs != null)
            {
                for (int i = 0; i < m_TargetNPCs.Count; i++)
                {
                    NPCPathFollower npc = m_TargetNPCs[i];
                    if (npc != null)
                    {
                        Gizmos.color = isSelected ? Color.yellow : new Color(1f, 0.85f, 0.2f, 0.75f);
                        DrawArrow(triggerOrigin, npc.transform.position);
                    }
                }
            }

            // Líneas/flechas hacia semáforos objetivo
            if (m_TargetTrafficLights != null)
            {
                for (int i = 0; i < m_TargetTrafficLights.Count; i++)
                {
                    TrafficLightController light = m_TargetTrafficLights[i];
                    if (light != null)
                    {
                        Gizmos.color = isSelected ? Color.magenta : new Color(1f, 0.3f, 0.8f, 0.75f);
                        DrawArrow(triggerOrigin, light.transform.position);
                    }
                }
            }
        }

        private static void DrawArrow(Vector3 from, Vector3 to)
        {
            Gizmos.DrawLine(from, to);

            Vector3 direction = to - from;
            float distance = direction.magnitude;
            if (distance < 0.2f) return;

            Vector3 normalized = direction / distance;
            Vector3 arrowPoint = Vector3.Lerp(from, to, 0.8f);
            Vector3 perpendicular = Vector3.Cross(normalized, Vector3.up).normalized * 0.4f;
            if (perpendicular.sqrMagnitude < 0.001f)
            {
                perpendicular = Vector3.Cross(normalized, Vector3.right).normalized * 0.4f;
            }

            Gizmos.DrawLine(to, arrowPoint + perpendicular);
            Gizmos.DrawLine(to, arrowPoint - perpendicular);
        }

        #endregion
    }
}
