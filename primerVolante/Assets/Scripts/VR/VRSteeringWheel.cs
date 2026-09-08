using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Componente de Volante VR de alta precisión.
    /// Opera sobre un GameObject Pivot posicionado en el centro exacto de la geometría del volante.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class VRSteeringWheel : MonoBehaviour
    {
        [Header("Configuración del Volante")]
        [Tooltip("Ángulo máximo de giro hacia cada lado en grados (ej: 450 = 1.5 vueltas a la izquierda / derecha).")]
        [SerializeField] private float m_MaxSteeringAngle = 450f;

        [Tooltip("Si se activa, el volante vuelve automáticamente al centro al soltarlo.")]
        [SerializeField] private bool m_ReturnToCenter = true;

        [Tooltip("Velocidad de retorno al centro en grados por segundo.")]
        [SerializeField] private float m_ReturnSpeed = 250f;

        [Header("Salida de Datos")]
        [Tooltip("Valor actual del volante normalizado entre -1.0 (total izquierda) y +1.0 (total derecha).")]
        [SerializeField] private float m_SteeringValue = 0f;

        private XRGrabInteractable m_GrabInteractable;
        private Rigidbody m_Rigidbody;

        private float m_CurrentAngle = 0f;
        private float m_StartHandAngle = 0f;
        private float m_StartWheelAngle = 0f;

        private Quaternion m_InitialLocalRotation;

        /// <summary>
        /// Valor normalizado del volante (-1.0 a +1.0).
        /// </summary>
        public float SteeringValue => m_SteeringValue;

        /// <summary>
        /// Ángulo actual del volante en grados.
        /// </summary>
        public float CurrentAngle => m_CurrentAngle;

        private void Awake()
        {
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            m_GrabInteractable.trackPosition = false;
            m_GrabInteractable.trackRotation = false;
            m_GrabInteractable.throwOnDetach = false;

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }

            m_InitialLocalRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_StartHandAngle = GetHandAngleInParentSpace();
            m_StartWheelAngle = m_CurrentAngle;
        }

        private void Update()
        {
            if (m_GrabInteractable.isSelected)
            {
                float currentHandAngle = GetHandAngleInParentSpace();
                float deltaAngle = Mathf.DeltaAngle(m_StartHandAngle, currentHandAngle);

                m_CurrentAngle = Mathf.Clamp(m_StartWheelAngle + deltaAngle, -m_MaxSteeringAngle, m_MaxSteeringAngle);
                ApplyRotation(m_CurrentAngle);
            }
            else if (m_ReturnToCenter && m_CurrentAngle != 0f)
            {
                m_CurrentAngle = Mathf.MoveTowards(m_CurrentAngle, 0f, m_ReturnSpeed * Time.deltaTime);
                ApplyRotation(m_CurrentAngle);
            }

            m_SteeringValue = m_MaxSteeringAngle > 0 ? Mathf.Clamp(m_CurrentAngle / m_MaxSteeringAngle, -1f, 1f) : 0f;
        }

        /// <summary>
        /// Calcula el ángulo de la mano en el espacio de coordenadas fijo del Padre del coche,
        /// garantizando cero bucles de retroalimentación o desplazamiento del centro.
        /// </summary>
        private float GetHandAngleInParentSpace()
        {
            var interactors = m_GrabInteractable.interactorsSelecting;
            if (interactors == null || interactors.Count == 0)
                return 0f;

            Vector3 combinedWorldPos = Vector3.zero;
            int count = 0;

            foreach (var interactor in interactors)
            {
                if (interactor is IXRInteractor xrInteractor)
                {
                    combinedWorldPos += xrInteractor.transform.position;
                    count++;
                }
            }

            if (count == 0) return 0f;
            combinedWorldPos /= count;

            Transform parentTr = transform.parent;

            // 1. Proyectar posición de la mano y centro del volante en el espacio del coche (Padre)
            Vector3 handPosInParent = parentTr != null ? parentTr.InverseTransformPoint(combinedWorldPos) : combinedWorldPos;
            Vector3 wheelCenterInParent = parentTr != null ? parentTr.InverseTransformPoint(transform.position) : transform.position;

            Vector3 dirInParent = handPosInParent - wheelCenterInParent;

            // 2. Usar los ejes X e Y de la rotación inicial neutra como base plana
            Vector3 parentRight = m_InitialLocalRotation * Vector3.right;
            Vector3 parentUp = m_InitialLocalRotation * Vector3.up;

            float x = Vector3.Dot(dirInParent, parentRight);
            float y = Vector3.Dot(dirInParent, parentUp);

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        private void ApplyRotation(float angle)
        {
            // Rota el Pivot sobre su eje Z local sin alterar su posición ni crear inercias físicas
            transform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}
