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
    /// Ejecuta el procesamiento en LateUpdate (ExecutionOrder = 100) inmediatamente después del posicionamiento del jugador,
    /// eliminando cualquier desfase de 1 frame entre la cámara y la posición del mando al avanzar el auto.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class VRSteeringWheel : MonoBehaviour
    {
        [Header("Configuración del Volante")]
        [Tooltip("Ángulo máximo de giro hacia cada lado en grados (ej: 450 = 1.5 vueltas a la izquierda / derecha).")]
        [SerializeField] private float m_MaxSteeringAngle = 450f;

        [Tooltip("Velocidad de retorno al centro en grados por segundo.")]
        [SerializeField] private float m_ReturnSpeed = 250f;

        [Tooltip("Si se activa, el volante vuelve automáticamente al centro al soltarlo.")]
        [SerializeField] private bool m_ReturnToCenter = true;

        [Header("Salida de Datos")]
        [Tooltip("Valor actual del volante normalizado entre -1.0 (total izquierda) y +1.0 (total derecha).")]
        [SerializeField] private float m_SteeringValue = 0f;

        private XRGrabInteractable m_GrabInteractable;
        private Rigidbody m_Rigidbody;

        private float m_CurrentAngle = 0f;
        private float m_LastHandAngle = 0f;
        private Quaternion m_StartControllerRotation;
        private IXRInteractor m_ActiveInteractor;

        private Transform m_InitialParent;
        private Quaternion m_InitialLocalRotation;
        private Vector3 m_InitialLocalPosition;

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

            m_InitialParent = transform.parent;
            m_InitialLocalPosition = transform.localPosition;
            m_InitialLocalRotation = transform.localRotation;

            ConfigureGrabInteractable();
            EnsureSimplifiedGrabCollider();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }
        }

        public void ConfigureGrabInteractable()
        {
            if (m_GrabInteractable == null) m_GrabInteractable = GetComponent<XRGrabInteractable>();

            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.trackPosition = false;
                m_GrabInteractable.trackRotation = false;
                m_GrabInteractable.throwOnDetach = false;
                m_GrabInteractable.useDynamicAttach = false;
                m_GrabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;

                if (m_GrabInteractable.startingSingleGrabTransformers != null)
                {
                    m_GrabInteractable.startingSingleGrabTransformers.Clear();
                }
            }
        }

        public void EnsureSimplifiedGrabCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col == null || col is MeshCollider)
            {
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                BoxCollider boxCol = gameObject.AddComponent<BoxCollider>();
                boxCol.isTrigger = false;
                boxCol.center = Vector3.zero;
                boxCol.size = new Vector3(0.45f, 0.45f, 0.15f);
            }
        }

        private void OnEnable()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
                m_GrabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_ActiveInteractor = args.interactorObject;
            if (m_ActiveInteractor != null && m_ActiveInteractor.transform != null)
            {
                m_LastHandAngle = CalculateHandAngle(m_ActiveInteractor.transform);
                m_StartControllerRotation = m_ActiveInteractor.transform.rotation;
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_GrabInteractable.interactorsSelecting != null && m_GrabInteractable.interactorsSelecting.Count > 0)
            {
                m_ActiveInteractor = m_GrabInteractable.interactorsSelecting[0];
                if (m_ActiveInteractor != null && m_ActiveInteractor.transform != null)
                {
                    m_LastHandAngle = CalculateHandAngle(m_ActiveInteractor.transform);
                    m_StartControllerRotation = m_ActiveInteractor.transform.rotation;
                }
            }
            else
            {
                m_ActiveInteractor = null;
            }
        }

        private void Update()
        {
            ApplyLocalTransform(m_CurrentAngle);
        }

        private void FixedUpdate()
        {
            ApplyLocalTransform(m_CurrentAngle);
        }

        private void LateUpdate()
        {
            // 1. Restaurar jerarquía si un componente externo intentara desparentar el volante
            if (transform.parent != m_InitialParent && m_InitialParent != null)
            {
                transform.SetParent(m_InitialParent, false);
            }

            // 2. Procesar el ángulo del volante en LateUpdate, DESPUÉS de que DriverSeatFollower (Order=50) haya actualizado XROrigin
            if (m_GrabInteractable != null && m_GrabInteractable.isSelected && m_ActiveInteractor != null && m_ActiveInteractor.transform != null)
            {
                float currentHandAngle = CalculateHandAngle(m_ActiveInteractor.transform);
                float stepDelta = -Mathf.DeltaAngle(m_LastHandAngle, currentHandAngle);
                m_LastHandAngle = currentHandAngle;

                m_CurrentAngle = Mathf.Clamp(m_CurrentAngle + stepDelta, -m_MaxSteeringAngle, m_MaxSteeringAngle);
            }
            else if (m_ReturnToCenter && m_CurrentAngle != 0f)
            {
                m_CurrentAngle = Mathf.MoveTowards(m_CurrentAngle, 0f, m_ReturnSpeed * Time.deltaTime);
            }

            m_SteeringValue = m_MaxSteeringAngle > 0 ? Mathf.Clamp(m_CurrentAngle / m_MaxSteeringAngle, -1f, 1f) : 0f;

            // 3. Aplicar las coordenadas locales fijas y la rotación Z calculada
            ApplyLocalTransform(m_CurrentAngle);
        }

        private void ApplyLocalTransform(float angle)
        {
            transform.localPosition = m_InitialLocalPosition;
            transform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(-angle, Vector3.forward);
        }

        /// <summary>
        /// Calcula el ángulo instantáneo de la mano en el plano 2D del volante.
        /// </summary>
        private float CalculateHandAngle(Transform interactorTransform)
        {
            Transform parentTr = transform.parent != null ? transform.parent : m_InitialParent;
            if (parentTr == null) return 0f;

            // Proyección 2D de la mano en el plano del volante (Empujar con la palma / arco del volante)
            Vector3 handLocalPos = parentTr.InverseTransformPoint(interactorTransform.position);
            Vector3 wheelLocalCenter = parentTr.InverseTransformPoint(transform.position);
            Vector3 dir = handLocalPos - wheelLocalCenter;

            Vector3 parentRight = m_InitialLocalRotation * Vector3.right;
            Vector3 parentUp = m_InitialLocalRotation * Vector3.up;

            float x = Vector3.Dot(dir, parentRight);
            float y = Vector3.Dot(dir, parentUp);

            float distSq = x * x + y * y;
            if (distSq < 0.0001f) // Mano a menos de 1cm del centro exacto
            {
                return m_LastHandAngle;
            }

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }
    }
}
