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
    [DefaultExecutionOrder(100)]
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

        [Header("Zonas de Agarre Laterales")]
        [Tooltip("Collider de la zona de agarre izquierda del espejo.")]
        [SerializeField] private Collider m_LeftGrabCollider;

        [Tooltip("Collider de la zona de agarre derecha del espejo.")]
        [SerializeField] private Collider m_RightGrabCollider;

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

        private Transform m_InitialParent;
        private Vector3 m_InitialLocalPosition;
        private Quaternion m_InitialLocalRotation;

        private Vector3 m_GrabStartHandLocalPos;
        private float m_GrabStartYaw;
        private float m_GrabStartPitch;

        public float CurrentYaw => m_CurrentYaw;
        public float CurrentPitch => m_CurrentPitch;
        public bool IsGrabbed => m_IsGrabbed;
        public Collider LeftGrabCollider => m_LeftGrabCollider;
        public Collider RightGrabCollider => m_RightGrabCollider;

        private void Awake()
        {
            if (m_PivotTransform == null)
            {
                m_PivotTransform = transform;
            }

            m_InitialParent = transform.parent;
            m_InitialLocalPosition = transform.localPosition;
            m_InitialLocalRotation = m_PivotTransform.localRotation;
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            EnsureLateralGrabColliders();
            ConfigureInteractableAndPhysics();
        }

        private void Start()
        {
            if (m_InitialParent == null && transform.parent != null)
            {
                m_InitialParent = transform.parent;
                m_InitialLocalPosition = transform.localPosition;
            }

            EnsureLateralGrabColliders();
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

        /// <summary>
        /// Asegura que existan únicamente dos zonas de agarre laterales (izquierda y derecha),
        /// dejando el centro completamente libre para evitar interferir con el pivote del espejo.
        /// </summary>
        public void EnsureLateralGrabColliders()
        {
            Collider[] rootColliders = GetComponents<Collider>();
            foreach (var col in rootColliders)
            {
                if (col != null)
                {
                    col.enabled = false;
                    if (Application.isPlaying)
                    {
                        Destroy(col);
                    }
                    else
                    {
#if UNITY_EDITOR
                        UnityEditor.EditorApplication.delayCall += () =>
                        {
                            if (col != null)
                            {
                                DestroyImmediate(col, true);
                            }
                        };
#endif
                    }
                }
            }

            // 2. Zona de agarre lateral izquierda
            Transform leftTr = transform.Find("GrabZone_Left");
            if (leftTr == null)
            {
                GameObject leftObj = new GameObject("GrabZone_Left");
                leftObj.transform.SetParent(transform, false);
                leftObj.transform.localPosition = new Vector3(-0.075f, 0f, 0f);
                leftObj.transform.localRotation = Quaternion.identity;
                leftTr = leftObj.transform;
            }
            BoxCollider leftBox = leftTr.GetComponent<BoxCollider>();
            if (leftBox == null) leftBox = leftTr.gameObject.AddComponent<BoxCollider>();
            leftBox.center = Vector3.zero;
            leftBox.size = new Vector3(0.07f, 0.065f, 0.045f);
            leftBox.isTrigger = false;
            m_LeftGrabCollider = leftBox;

            // 3. Zona de agarre lateral derecha
            Transform rightTr = transform.Find("GrabZone_Right");
            if (rightTr == null)
            {
                GameObject rightObj = new GameObject("GrabZone_Right");
                rightObj.transform.SetParent(transform, false);
                rightObj.transform.localPosition = new Vector3(0.075f, 0f, 0f);
                rightObj.transform.localRotation = Quaternion.identity;
                rightTr = rightObj.transform;
            }
            BoxCollider rightBox = rightTr.GetComponent<BoxCollider>();
            if (rightBox == null) rightBox = rightTr.gameObject.AddComponent<BoxCollider>();
            rightBox.center = Vector3.zero;
            rightBox.size = new Vector3(0.07f, 0.065f, 0.045f);
            rightBox.isTrigger = false;
            m_RightGrabCollider = rightBox;

            // 4. Actualizar lista de colliders en el interactuable
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.colliders.Clear();
                if (m_LeftGrabCollider != null) m_GrabInteractable.colliders.Add(m_LeftGrabCollider);
                if (m_RightGrabCollider != null) m_GrabInteractable.colliders.Add(m_RightGrabCollider);
            }
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
                m_GrabInteractable.unparentTransformOnGrab = false;
                m_GrabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;

                if (m_GrabInteractable.startingSingleGrabTransformers != null)
                {
                    m_GrabInteractable.startingSingleGrabTransformers.Clear();
                }

                if (m_LeftGrabCollider != null || m_RightGrabCollider != null)
                {
                    m_GrabInteractable.colliders.Clear();
                    if (m_LeftGrabCollider != null) m_GrabInteractable.colliders.Add(m_LeftGrabCollider);
                    if (m_RightGrabCollider != null) m_GrabInteractable.colliders.Add(m_RightGrabCollider);
                }
            }
        }

        /// <summary>
        /// Garantiza que el espejo conserve su jerarquía original en el vehículo y su posición local.
        /// </summary>
        public void EnsureHierarchyAndLocalTransform()
        {
            if (m_InitialParent != null && transform.parent != m_InitialParent)
            {
                transform.SetParent(m_InitialParent, false);
            }
            transform.localPosition = m_InitialLocalPosition;
        }

        private Transform GetReferenceSpace()
        {
            if (m_MountTransform != null) return m_MountTransform;
            if (m_InitialParent != null) return m_InitialParent;
            if (transform.parent != null) return transform.parent;
            return transform;
        }

        private void OnGrabEntered(SelectEnterEventArgs args)
        {
            EnsureHierarchyAndLocalTransform();

            m_IsGrabbed = true;
            m_ActiveInteractor = args.interactorObject;

            Transform refSpace = GetReferenceSpace();
            Transform attach = m_ActiveInteractor != null ? m_ActiveInteractor.GetAttachTransform(m_GrabInteractable) : null;
            Vector3 handWorldPos = attach != null ? attach.position : (m_ActiveInteractor != null ? m_ActiveInteractor.transform.position : transform.position);

            m_GrabStartHandLocalPos = refSpace.InverseTransformPoint(handWorldPos);
            m_GrabStartYaw = m_CurrentYaw;
            m_GrabStartPitch = m_CurrentPitch;
        }

        private void OnGrabExited(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;
            m_ActiveInteractor = null;

            EnsureHierarchyAndLocalTransform();
            ApplyRotation();
        }

        private void Update()
        {
            EnsureHierarchyAndLocalTransform();

            if (m_EnableKeyboardDebug)
            {
                ProcessKeyboardInput();
            }
        }

        private void LateUpdate()
        {
            EnsureHierarchyAndLocalTransform();

            if (m_IsGrabbed && m_ActiveInteractor != null)
            {
                ProcessHandDisplacement();
            }
        }

        private void FixedUpdate()
        {
            EnsureHierarchyAndLocalTransform();
        }

        /// <summary>
        /// Procesa el desplazamiento tridimensional de la mano relativo al soporte del espejo en la cabina
        /// para calcular Yaw y Pitch de manera completamente inmune a la velocidad de avance/retroceso del auto.
        /// </summary>
        private void ProcessHandDisplacement()
        {
            if (m_ActiveInteractor == null) return;

            Transform refSpace = GetReferenceSpace();
            Transform attach = m_ActiveInteractor.GetAttachTransform(m_GrabInteractable);
            Vector3 currentHandWorldPos = attach != null ? attach.position : m_ActiveInteractor.transform.position;
            Vector3 currentHandLocalPos = refSpace.InverseTransformPoint(currentHandWorldPos);

            Vector3 localDelta = currentHandLocalPos - m_GrabStartHandLocalPos;

            float leverArm = Mathf.Max(0.05f, m_HandLeverArm);

            // Mover mano a la derecha (+X en cabina) incrementa Yaw (+ rotación hacia la derecha)
            float deltaYaw = (localDelta.x / leverArm) * Mathf.Rad2Deg;

            // Mover mano hacia arriba (+Y en cabina) inclina Pitch hacia arriba
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

            // Pitch en X, Yaw en Y, Roll bloqueado en 0°.
            // La cara del vidrio mira hacia -Z (hacia el conductor): un giro positivo en Y giraría esa
            // cara hacia la izquierda, así que se invierte el signo para que Yaw > 0 = cara hacia la derecha
            // (mano a la derecha o tecla L => el espejo sigue a la mano). El Pitch ya coincide (+ = cara hacia arriba).
            Quaternion targetRot = m_InitialLocalRotation * Quaternion.Euler(m_CurrentPitch, -m_CurrentYaw, 0f);
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
        }
#endif
    }
}
