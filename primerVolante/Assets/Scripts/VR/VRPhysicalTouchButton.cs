using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Componente de botón físico para interacción en VR mediante colisionadores Trigger.
    /// Detecta cuando un control de VR toca o traspasa el botón y genera un efecto visual de hundimiento.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class VRPhysicalTouchButton : MonoBehaviour
    {
        public enum ButtonState
        {
            Idle,
            Presionado
        }

        [Header("Estado")]
        [Tooltip("Estado actual del botón.")]
        [SerializeField] private ButtonState m_CurrentState = ButtonState.Idle;

        [Header("Configuración de Hundimiento")]
        [Tooltip("Offset vectorial local que se suma a la posición original cuando el botón está presionado.")]
        [SerializeField] private Vector3 m_PressOffset = new Vector3(0f, 0f, 0.015f);

        [Tooltip("Profundidad de hundimiento configurable.")]
        [SerializeField] private float m_PressDepth = 0.015f;

        [Header("Filtrado de Interacción")]
        [Tooltip("Si es true, ignora colisionadores que pertenezcan a la raíz del vehículo.")]
        [SerializeField] private bool m_IgnoreVehicleColliders = true;

        [Header("Eventos")]
        [Tooltip("Evento disparado al cambiar a estado Presionado.")]
        [SerializeField] private UnityEvent m_OnPressed;

        [Tooltip("Evento disparado al cambiar a estado Idle.")]
        [SerializeField] private UnityEvent m_OnReleased;

        private Vector3 m_OriginalLocalPosition;
        private readonly HashSet<Collider> m_ActiveColliders = new HashSet<Collider>();

        public ButtonState CurrentState => m_CurrentState;

        public Vector3 PressOffset
        {
            get => m_PressOffset;
            set => m_PressOffset = value;
        }

        public float PressDepth
        {
            get => m_PressDepth;
            set
            {
                m_PressDepth = value;
                if (m_PressOffset == Vector3.zero)
                {
                    m_PressOffset = Vector3.forward * m_PressDepth;
                }
            }
        }

        public UnityEvent OnPressed => m_OnPressed;
        public UnityEvent OnReleased => m_OnReleased;

        private void Awake()
        {
            m_OriginalLocalPosition = transform.localPosition;
            if (m_PressOffset == Vector3.zero && m_PressDepth > 0f)
            {
                m_PressOffset = Vector3.forward * m_PressDepth;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

            // Ignorar el propio GameObject
            if (other.gameObject == gameObject) return;

            // Ignorar colisionadores del propio vehículo si está activado
            if (m_IgnoreVehicleColliders && other.transform.IsChildOf(transform.root)) return;

            m_ActiveColliders.Add(other);

            if (m_CurrentState != ButtonState.Presionado)
            {
                SetState(ButtonState.Presionado);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;

            m_ActiveColliders.Remove(other);
            m_ActiveColliders.RemoveWhere(c => c == null || !c.gameObject.activeInHierarchy || !c.enabled);

            if (m_ActiveColliders.Count == 0 && m_CurrentState != ButtonState.Idle)
            {
                SetState(ButtonState.Idle);
            }
        }

        private void SetState(ButtonState newState)
        {
            m_CurrentState = newState;

            if (m_CurrentState == ButtonState.Presionado)
            {
                Vector3 offset = m_PressOffset != Vector3.zero ? m_PressOffset : Vector3.forward * m_PressDepth;
                transform.localPosition = m_OriginalLocalPosition + offset;
                Debug.Log($"{gameObject.name} está ahora {m_CurrentState}");
                m_OnPressed?.Invoke();
            }
            else
            {
                transform.localPosition = m_OriginalLocalPosition;
                Debug.Log($"{gameObject.name} está ahora {m_CurrentState}");
                m_OnReleased?.Invoke();
            }
        }

        private void OnDisable()
        {
            if (m_CurrentState != ButtonState.Idle)
            {
                m_ActiveColliders.Clear();
                SetState(ButtonState.Idle);
            }
        }

        private void OnValidate()
        {
            if (m_PressOffset == Vector3.zero && m_PressDepth > 0f)
            {
                m_PressOffset = Vector3.forward * m_PressDepth;
            }
        }
    }
}
