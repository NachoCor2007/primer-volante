using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Espejo retrovisor central interactuable en Realidad Virtual.
    /// Posee un soporte fijo al techo/parabrisas y un punto de pivote (rótula esférica).
    /// Compatible con XR Interaction Toolkit mediante XRGrabInteractable (sin desplazar posición ni rotar libremente por física).
    /// Al agarrar la carcasa con la mano (Grip):
    /// - Rota en tiempo real siguiendo el desplazamiento de la mano alrededor de su pivote.
    /// - Límites estrictos: Yaw [-30°, +30°], Pitch [-20°, +20°] y Roll bloqueado en 0°.
    /// - Al soltarlo, mantiene firmemente la rotación fijada por el conductor.
    /// Incluye controles de depuración por teclado para el Editor (I/K: Pitch, J/L: Yaw, U: Centrar).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class VRRearviewMirror : MonoBehaviour
    {
        [Header("Referencias de Transform y Pivote")]
        [Tooltip("Transform que rota (la carcasa del espejo o rótula). Si es null se usa este mismo Transform.")]
        [SerializeField] private Transform m_PivotTransform;

        [Tooltip("Transform del soporte fijo al parabrisas / techo.")]
        [SerializeField] private Transform m_MountTransform;

        [Tooltip("Cámara retrovisora opcional emparentada a la carcasa.")]
        [SerializeField] private Transform m_RearviewCameraTransform;

        [Header("Límites Angulares Estrictos")]
        [Tooltip("Límite mínimo de Yaw (giro horizontal hacia la izquierda) en grados.")]
        [SerializeField] private float m_MinYaw = -30f;

        [Tooltip("Límite máximo de Yaw (giro horizontal hacia la derecha) en grados.")]
        [SerializeField] private float m_MaxYaw = 30f;

        [Tooltip("Límite mínimo de Pitch (inclinación vertical hacia abajo) en grados.")]
        [SerializeField] private float m_MinPitch = -20f;

        [Tooltip("Límite máximo de Pitch (inclinación vertical hacia arriba) en grados.")]
        [SerializeField] private float m_MaxPitch = 20f;

        [Header("Sensibilidad y Brazos de Palanca")]
        [Tooltip("Radio virtual de palanca de la mano respecto al pivote (en metros). Determina la respuesta angular al mover la mano.")]
        [SerializeField] private float m_HandLeverArm = 0.15f;

        [Header("Estado Actual")]
        [SerializeField] private float m_CurrentYaw = 0f;
        [SerializeField] private float m_CurrentPitch = 0f;

        [Header("Controles de Teclado (Editor / Fallback)")]
        [Tooltip("Habilita controles por teclado (I/K: subir/bajar, J/L: izquierda/derecha, U: centrar).")]
        [SerializeField] private bool m_EnableKeyboardDebug = true;

        [Tooltip("Velocidad de rotación con teclado en grados por segundo.")]
        [SerializeField] private float m_KeyboardRotationSpeed = 35f;

        private XRGrabInteractable m_GrabInteractable;
        private Rigidbody m_Rigidbody;
        private IXRInteractor m_ActiveInteractor;
        private bool m_IsGrabbed = false;

        private Quaternion m_InitialLocalRotation;
        private Vector3 m_GrabStartHandWorldPos;
        private float m_GrabStartYaw;
        private float m_GrabStartPitch;

        public float CurrentYaw => m_CurrentYaw;
        public float CurrentPitch => m_CurrentPitch;
        public bool IsGrabbed => m_IsGrabbed;

        private void Awake()
        {
            if (m_PivotTransform == null)
            {
                m_PivotTransform = transform;
            }

            m_InitialLocalRotation = m_PivotTransform.localRotation;
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            ConfigureInteractableAndPhysics();
        }

        private void Start()
        {
            ConfigureInteractableAndPhysics();
            ApplyRotation();
        }

        private void OnEnable()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.AddListener(OnGrabEntered);
                m_GrabInteractable.selectExited.AddListener(OnGrabExited);
            }
        }

        private void OnDisable()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.RemoveListener(OnGrabEntered);
                m_GrabInteractable.selectExited.RemoveListener(OnGrabExited);
            }
            m_IsGrabbed = false;
            m_ActiveInteractor = null;
        }

        public void ConfigureInteractableAndPhysics()
        {
            if (m_Rigidbody == null)
            {
                m_Rigidbody = GetComponent<Rigidbody>();
            }

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }

            if (m_GrabInteractable == null)
            {
                m_GrabInteractable = GetComponent<XRGrabInteractable>();
            }

            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.trackPosition = false;
                m_GrabInteractable.trackRotation = false;
                m_GrabInteractable.throwOnDetach = false;
                m_GrabInteractable.forceGravityOnDetach = false;
                m_GrabInteractable.retainTransformParent = true;
                m_GrabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;

                if (m_GrabInteractable.startingSingleGrabTransformers != null)
                {
                    m_GrabInteractable.startingSingleGrabTransformers.Clear();
                }
            }
        }

        private void OnGrabEntered(SelectEnterEventArgs args)
        {
            m_IsGrabbed = true;
            m_ActiveInteractor = args.interactorObject;

            Transform attach = m_ActiveInteractor != null ? m_ActiveInteractor.GetAttachTransform(m_GrabInteractable) : null;
            m_GrabStartHandWorldPos = attach != null ? attach.position : (m_ActiveInteractor != null ? m_ActiveInteractor.transform.position : transform.position);

            m_GrabStartYaw = m_CurrentYaw;
            m_GrabStartPitch = m_CurrentPitch;
        }

        private void OnGrabExited(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;
            m_ActiveInteractor = null;

            // Al soltar, mantiene firmemente la rotación fijada
            ApplyRotation();
        }

        private void Update()
        {
            if (m_IsGrabbed && m_ActiveInteractor != null)
            {
                ProcessHandDisplacement();
            }

            if (m_EnableKeyboardDebug)
            {
                ProcessKeyboardInput();
            }
        }

        /// <summary>
        /// Procesa el desplazamiento tridimensional de la mano alrededor del pivote para calcular Yaw y Pitch.
        /// </summary>
        private void ProcessHandDisplacement()
        {
            Transform attach = m_ActiveInteractor.GetAttachTransform(m_GrabInteractable);
            Vector3 currentHandPos = attach != null ? attach.position : m_ActiveInteractor.transform.position;

            Vector3 deltaWorld = currentHandPos - m_GrabStartHandWorldPos;

            // Transformar el desplazamiento al espacio de referencia (soporte o padre del espejo)
            Transform refSpace = m_MountTransform != null ? m_MountTransform : (m_PivotTransform.parent != null ? m_PivotTransform.parent : m_PivotTransform);
            Vector3 localDelta = refSpace.InverseTransformDirection(deltaWorld);

            float leverArm = Mathf.Max(0.05f, m_HandLeverArm);

            // Mover mano a la derecha (+X) incrementa Yaw (+ rotación hacia la derecha)
            float deltaYaw = (localDelta.x / leverArm) * Mathf.Rad2Deg;

            // Mover mano hacia arriba (+Y) inclina Pitch hacia arriba
            float deltaPitch = (localDelta.y / leverArm) * Mathf.Rad2Deg;

            m_CurrentYaw = Mathf.Clamp(m_GrabStartYaw + deltaYaw, m_MinYaw, m_MaxYaw);
            m_CurrentPitch = Mathf.Clamp(m_GrabStartPitch + deltaPitch, m_MinPitch, m_MaxPitch);

            ApplyRotation();
        }

        /// <summary>
        /// Procesa atajos de depuración por teclado:
        /// I: Inclinar hacia arriba, K: Inclinar hacia abajo.
        /// J: Girar a la izquierda, L: Girar a la derecha.
        /// U: Centrar a 0°, 0°.
        /// </summary>
        private void ProcessKeyboardInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return;

            float dt = Time.deltaTime;
            bool changed = false;

            if (Keyboard.current.iKey.isPressed)
            {
                m_CurrentPitch += m_KeyboardRotationSpeed * dt;
                changed = true;
            }
            if (Keyboard.current.kKey.isPressed)
            {
                m_CurrentPitch -= m_KeyboardRotationSpeed * dt;
                changed = true;
            }
            if (Keyboard.current.jKey.isPressed)
            {
                m_CurrentYaw -= m_KeyboardRotationSpeed * dt;
                changed = true;
            }
            if (Keyboard.current.lKey.isPressed)
            {
                m_CurrentYaw += m_KeyboardRotationSpeed * dt;
                changed = true;
            }
            if (Keyboard.current.uKey.wasPressedThisFrame)
            {
                m_CurrentYaw = 0f;
                m_CurrentPitch = 0f;
                changed = true;
            }

            if (changed)
            {
                m_CurrentYaw = Mathf.Clamp(m_CurrentYaw, m_MinYaw, m_MaxYaw);
                m_CurrentPitch = Mathf.Clamp(m_CurrentPitch, m_MinPitch, m_MaxPitch);
                ApplyRotation();
            }
#endif
        }

        /// <summary>
        /// Aplica la rotación local asegurando Roll estrictamente bloqueado en 0°.
        /// </summary>
        public void ApplyRotation()
        {
            if (m_PivotTransform == null) m_PivotTransform = transform;

            // Pitch en X, Yaw en Y, Roll bloqueado en 0°
            Quaternion targetRot = m_InitialLocalRotation * Quaternion.Euler(m_CurrentPitch, m_CurrentYaw, 0f);
            m_PivotTransform.localRotation = targetRot;

            // Si la cámara no es hija del pivote pero está asignada, sincronizar su rotación
            if (m_RearviewCameraTransform != null && m_RearviewCameraTransform.parent != m_PivotTransform)
            {
                m_RearviewCameraTransform.rotation = m_PivotTransform.rotation;
            }
        }

        /// <summary>
        /// Asigna explícitamente los ángulos de inclinación (Yaw y Pitch) y actualiza el espejo.
        /// </summary>
        public void SetAngles(float pitch, float yaw)
        {
            m_CurrentPitch = Mathf.Clamp(pitch, m_MinPitch, m_MaxPitch);
            m_CurrentYaw = Mathf.Clamp(yaw, m_MinYaw, m_MaxYaw);
            ApplyRotation();
        }

        /// <summary>
        /// Restablece el espejo retrovisor a su posición neutra central.
        /// </summary>
        public void ResetToCenter()
        {
            SetAngles(0f, 0f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_PivotTransform == null)
            {
                m_PivotTransform = transform;
            }
            ConfigureInteractableAndPhysics();
        }
#endif
    }
}
