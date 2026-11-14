using System;
using System.Collections.Generic;
using PrimerVolante.Evaluation;
using PrimerVolante.VR;
using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Detector de infracciones de semáforo en rojo para cruces e intersecciones.
    /// Utiliza un Collider tipo Trigger ubicado en la línea de detención para detectar
    /// el paso de vehículos (jugador identificado por <see cref="VehicleController"/> o Tag).
    /// Si el <see cref="TrafficLightController"/> asociado se encuentra en luz roja,
    /// informa la falta por <see cref="InfractionReporting"/> y dispara los eventos de infracción, con cooldown antirrebote.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class RedLightViolationDetector : MonoBehaviour
    {
        [Header("Semáforo Asociado")]
        [Tooltip("Controlador del semáforo asignado a este carril o cruce.")]
        [SerializeField] private TrafficLightController m_TrafficLight;

        [Tooltip("Nombre del GameObject del semáforo para resolución dinámica en runtime (crucial para escenas aditivas como City_Master).")]
        [SerializeField] private string m_TrafficLightName = "";

        [Header("Filtros de Vehículo")]
        [Tooltip("Tag opcional de vehículo permitido para disparar la infracción (ej. 'Player'). Si está vacío, solo busca VehicleController.")]
        [SerializeField] private string m_VehicleTag = "";

        [Tooltip("Si es true, busca el componente VehicleController en el objeto o sus padres.")]
        [SerializeField] private bool m_CheckVehicleController = true;

        [Header("Prevención de Rebote / Cooldown")]
        [Tooltip("Tiempo mínimo en segundos entre detecciones sucesivas para el mismo vehículo o cruce.")]
        [SerializeField] private float m_CooldownDuration = 3f;

        [Header("Depuración")]
        [Tooltip("Escribe un LogWarning propio por cada infracción. El InfractionManager ya la loguea; sin gestor loguea el respaldo de consola.")]
        [SerializeField] private bool m_LogEvents = false;

        [Header("Eventos de Infracción")]
        [Tooltip("Evento Unity disparado cuando se confirma una infracción de luz roja.")]
        [SerializeField] public UnityEvent<Collider> onViolationDetectedEvent = new UnityEvent<Collider>();

        /// <summary>
        /// Evento C# disparado al detectar una infracción de luz roja.
        /// Proporciona el Collider que atravesó la línea de detención.
        /// </summary>
        public event Action<Collider> OnViolationDetected;

        // Registro de timestamps para cooldown por vehículo (GameObject raíz/vehículo)
        private readonly Dictionary<GameObject, float> m_LastViolationTimeByVehicle = new Dictionary<GameObject, float>();
        private int m_TotalViolationsRecorded = 0;

        #region Propiedades Públicas

        /// <summary>Semáforo asociado a la línea de detención.</summary>
        public TrafficLightController TrafficLight
        {
            get => m_TrafficLight;
            set => m_TrafficLight = value;
        }

        /// <summary>Nombre del GameObject del semáforo en la escena.</summary>
        public string TrafficLightName
        {
            get => m_TrafficLightName;
            set => m_TrafficLightName = value;
        }

        /// <summary>Duración del período de cooldown antirrebote.</summary>
        public float CooldownDuration
        {
            get => m_CooldownDuration;
            set => m_CooldownDuration = Mathf.Max(0.1f, value);
        }

        /// <summary>Tag requerido opcional para el vehículo infractor.</summary>
        public string VehicleTag
        {
            get => m_VehicleTag;
            set => m_VehicleTag = value;
        }

        /// <summary>Indica si se requiere validar el componente VehicleController.</summary>
        public bool CheckVehicleController
        {
            get => m_CheckVehicleController;
            set => m_CheckVehicleController = value;
        }

        /// <summary>Total de infracciones registradas por este detector.</summary>
        public int TotalViolationsRecorded => m_TotalViolationsRecorded;

        #endregion

        #region Ciclo de Vida de Unity

        private void Reset()
        {
            EnsureTriggerCollider();
        }

        private void Awake()
        {
            EnsureTriggerCollider();
            EnsureTrafficLightReference();
        }

        private void Start()
        {
            EnsureTrafficLightReference();
        }

        private void OnValidate()
        {
            m_CooldownDuration = Mathf.Max(0.1f, m_CooldownDuration);
            EnsureTriggerCollider();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryRecordViolation(other);
        }

        #endregion

        #region Métodos Públicos

        /// <summary>
        /// Evalúa si el collider entrante constituye una infracción y, de ser así, la procesa y registra.
        /// </summary>
        /// <param name="other">Collider que ingresó al trigger.</param>
        /// <returns>True si se registró una infracción nueva; false si fue ignorado o descartado por cooldown/estado verde.</returns>
        public bool TryRecordViolation(Collider other)
        {
            if (other == null)
            {
                return false;
            }

            // 1. Verificar si el semáforo está presente y en Rojo
            EnsureTrafficLightReference();
            if (m_TrafficLight == null || !m_TrafficLight.IsRed)
            {
                return false;
            }

            // 2. Comprobar si el collider pertenece a un vehículo
            GameObject vehicleKey = null;
            if (!IsVehicle(other, out vehicleKey))
            {
                return false;
            }

            // 3. Validar cooldown antirrebote para no disparar múltiples veces con las ruedas y carrocería
            float currentTime = GetCurrentTime();
            if (m_LastViolationTimeByVehicle.TryGetValue(vehicleKey, out float lastTime))
            {
                if (currentTime - lastTime < m_CooldownDuration)
                {
                    // Ignorado por cooldown
                    return false;
                }
            }

            // 4. Registrar la infracción
            m_LastViolationTimeByVehicle[vehicleKey] = currentTime;
            m_TotalViolationsRecorded++;

            InfractionReporting.Report(InfractionReport.Create(InfractionIds.RedLight, this, vehicleKey.transform.position));

            if (m_LogEvents)
            {
                string vehicleName = vehicleKey != null ? vehicleKey.name : other.name;
                string lightName = m_TrafficLight != null ? m_TrafficLight.name : "N/A";
                Debug.LogWarning($"[RedLightViolationDetector] ⚠️ ¡Infracción de luz roja detectada! El vehículo '{vehicleName}' cruzó la línea de detención en semáforo '{lightName}' (Total: {m_TotalViolationsRecorded}).", this);
            }

            OnViolationDetected?.Invoke(other);
            onViolationDetectedEvent?.Invoke(other);

            return true;
        }

        /// <summary>
        /// Limpia el historial de cooldown para permitir nuevas detecciones de prueba.
        /// </summary>
        public void ResetCooldown()
        {
            m_LastViolationTimeByVehicle.Clear();
        }

        #endregion

        #region Métodos Privados

        public void EnsureTrafficLightReference()
        {
            if (m_TrafficLight != null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(m_TrafficLightName))
            {
                GameObject go = GameObject.Find(m_TrafficLightName);
                if (go != null)
                {
                    m_TrafficLight = go.GetComponent<TrafficLightController>();
                    if (m_TrafficLight != null)
                    {
                        return;
                    }
                }
            }

            // Búsqueda espacial del semáforo más cercano dentro de un radio de 25m
            var allLights = UnityEngine.Object.FindObjectsByType<TrafficLightController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            TrafficLightController closest = null;
            float minDistSq = 25f * 25f;
            Vector3 myPos = transform.position;
            foreach (var l in allLights)
            {
                float dSq = (l.transform.position - myPos).sqrMagnitude;
                if (dSq < minDistSq)
                {
                    minDistSq = dSq;
                    closest = l;
                }
            }
            m_TrafficLight = closest;
        }

        private void EnsureTriggerCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
            }
        }

        private bool IsVehicle(Collider other, out GameObject vehicleKey)
        {
            vehicleKey = other.transform.root != null ? other.transform.root.gameObject : other.gameObject;

            if (m_CheckVehicleController)
            {
                VehicleController vehicle = other.GetComponentInParent<VehicleController>();
                if (vehicle != null)
                {
                    vehicleKey = vehicle.gameObject;
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(m_VehicleTag))
            {
                if (other.CompareTag(m_VehicleTag) || (other.transform.root != null && other.transform.root.CompareTag(m_VehicleTag)))
                {
                    return true;
                }
            }

            // Si no se exige VehicleController ni Tag específico, cualquier collider cuenta como vehículo
            if (!m_CheckVehicleController && string.IsNullOrEmpty(m_VehicleTag))
            {
                return true;
            }

            return false;
        }

        private float GetCurrentTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return (float)UnityEditor.EditorApplication.timeSinceStartup;
            }
#endif
            return Time.time;
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            bool isRed = (m_TrafficLight != null && m_TrafficLight.IsRed);
            Gizmos.color = isRed
                ? new Color(1f, 0.1f, 0.1f, 0.40f)
                : new Color(1f, 0.4f, 0.4f, 0.20f);

            Matrix4x4 oldMat = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = isRed ? Color.red : new Color(1f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = oldMat;
        }

        #endregion
    }
}
