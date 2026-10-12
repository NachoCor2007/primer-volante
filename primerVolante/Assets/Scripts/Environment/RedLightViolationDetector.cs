using System;
using System.Collections.Generic;
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
    /// dispara eventos de infracción y registra una advertencia con cooldown antirrebote.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class RedLightViolationDetector : MonoBehaviour
    {
        [Header("Semáforo Asociado")]
        [Tooltip("Controlador del semáforo asignado a este carril o cruce.")]
        [SerializeField] private TrafficLightController m_TrafficLight;

        [Header("Filtros de Vehículo")]
        [Tooltip("Tag opcional de vehículo permitido para disparar la infracción (ej. 'Player'). Si está vacío, solo busca VehicleController.")]
        [SerializeField] private string m_VehicleTag = "";

        [Tooltip("Si es true, busca el componente VehicleController en el objeto o sus padres.")]
        [SerializeField] private bool m_CheckVehicleController = true;

        [Header("Prevención de Rebote / Cooldown")]
        [Tooltip("Tiempo mínimo en segundos entre detecciones sucesivas para el mismo vehículo o cruce.")]
        [SerializeField] private float m_CooldownDuration = 3f;

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

            string vehicleName = vehicleKey != null ? vehicleKey.name : other.name;
            string lightName = m_TrafficLight != null ? m_TrafficLight.name : "N/A";
            Debug.LogWarning($"[RedLightViolationDetector] ⚠️ ¡Infracción de luz roja detectada! El vehículo '{vehicleName}' cruzó la línea de detención en semáforo '{lightName}' (Total: {m_TotalViolationsRecorded}).", this);

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

        #endregion
    }
}
