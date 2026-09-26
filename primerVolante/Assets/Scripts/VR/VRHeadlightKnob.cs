using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Perilla rotativa interactuable en VR para el control de luces (Off / LowBeam / HighBeam).
    /// Soporta interacción física mediante XRGrabInteractable (rotación continua y snap magnético suave),
    /// pasos por clic/poke y controles de depuración mediante Teclado (L) y Gamepad (D-Pad Arriba/Abajo).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class VRHeadlightKnob : MonoBehaviour
    {
        [Header("Estado Actual")]
        [Tooltip("Modo actual de las luces.")]
        [SerializeField] private HeadlightMode m_CurrentMode = HeadlightMode.Off;

        [Header("Eje y Ángulos de Rotación")]
        [Tooltip("Eje local sobre el que rota la perilla.")]
        [SerializeField] private Vector3 m_RotationAxis = Vector3.back;

        [Tooltip("Ángulo local para la posición Off (0°).")]
        [SerializeField] private float m_OffAngle = 0f;

        [Tooltip("Ángulo local para la posición Luces Bajas (45°).")]
        [SerializeField] private float m_LowBeamAngle = 45f;

        [Tooltip("Ángulo local para la posición Luces Altas (90°).")]
        [SerializeField] private float m_HighBeamAngle = 90f;

        [Tooltip("Transform que rota visualmente. Si se deja nulo, rota este mismo GameObject.")]
        [SerializeField] private Transform m_RotorTransform;

        [Header("Snap Magnético")]
        [Tooltip("Tolerancia angular en grados para detectar enganche magnético.")]
        [SerializeField] private float m_SnapThreshold = 8f;

        [Tooltip("Velocidad de interpolación angular para el snap magnético suave.")]
        [SerializeField] private float m_SnapSpeed = 14f;

        [Header("Audio / Feedback (Opcional)")]
        [Tooltip("AudioSource para el clic al cambiar de posición.")]
        [SerializeField] private AudioSource m_AudioSource;

        [Tooltip("Clip de sonido de clic rotativo.")]
        [SerializeField] private AudioClip m_ClickClip;

        [Header("Eventos")]
        [Tooltip("Evento invocado al cambiar el modo de luces.")]
        public HeadlightModeEvent OnModeChanged = new HeadlightModeEvent();

        public event Action<HeadlightMode> ModeChanged;

        /// <summary>
        /// Modo actual de las luces.
        /// </summary>
        public HeadlightMode CurrentMode => m_CurrentMode;

        /// <summary>
        /// Indica si la perilla está actualmente sostenida por un interactor VR.
        /// </summary>
        public bool IsGrabbed => m_IsGrabbed;

        private XRGrabInteractable m_Interactable;
        private Rigidbody m_Rigidbody;
        private bool m_IsGrabbed = false;
        private Coroutine m_SnapRoutine;
        private Quaternion m_InitialLocalRotation;
        private Transform m_OriginalParent;
        private Vector3 m_InitialLocalPosition;

        private float m_CurrentAngle = 0f;
        private float m_GrabStartAngle = 0f;
        private float m_GrabStartHandAngle = 0f;
        private float m_GrabStartRoll = 0f;
        private bool m_HasRotatedWhileGrabbed = false;

        private void Awake()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }

            if (m_RotorTransform == null)
            {
                m_RotorTransform = transform;
            }

            m_OriginalParent = transform.parent;
            m_InitialLocalPosition = transform.localPosition;
            m_InitialLocalRotation = m_RotorTransform.localRotation;

            ConfigureGrabInteractable();

            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.AddListener(OnGrab);
                m_Interactable.selectExited.AddListener(OnRelease);
                m_Interactable.activated.AddListener(OnActivated);
            }
        }

        private void Start()
        {
            m_CurrentAngle = GetAngleForMode(m_CurrentMode);
            ApplyRotation(m_CurrentAngle);
        }

        private void OnDestroy()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.RemoveListener(OnGrab);
                m_Interactable.selectExited.RemoveListener(OnRelease);
                m_Interactable.activated.RemoveListener(OnActivated);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ConfigureGrabInteractable();
        }
#endif

        private void ConfigureGrabInteractable()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();

            if (m_Interactable != null)
            {
                m_Interactable.trackPosition = false;
                m_Interactable.trackRotation = false;
                m_Interactable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                m_Interactable.throwOnDetach = false;
                m_Interactable.forceGravityOnDetach = false;
                m_Interactable.retainTransformParent = true;
            }
        }

        private void Update()
        {
            HandleDebugInputs();
        }

        private void LateUpdate()
        {
            if (m_IsGrabbed && m_Interactable != null && m_Interactable.interactorsSelecting.Count > 0)
            {
                EnsureHierarchyAndLocalTransform();

                var interactor = m_Interactable.interactorsSelecting[0];
                if (interactor != null)
                {
                    UpdateRotationFromHand(interactor.transform);
                }
            }
        }

        /// <summary>
        /// Maneja los controles de teclado (L) y Gamepad (D-Pad Arriba/Abajo) para pruebas y depuración.
        /// </summary>
        private void HandleDebugInputs()
        {
            if (m_IsGrabbed) return;

            // Teclado: tecla L cicla Off -> LowBeam -> HighBeam -> Off
            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                CycleMode();
                return;
            }

            // Gamepad: D-Pad Arriba sube nivel, D-Pad Abajo baja nivel
            if (Gamepad.current != null)
            {
                if (Gamepad.current.dpad.up.wasPressedThisFrame)
                {
                    StepMode(1);
                    return;
                }
                if (Gamepad.current.dpad.down.wasPressedThisFrame)
                {
                    StepMode(-1);
                    return;
                }
            }
        }

        /// <summary>
        /// Cicla al siguiente modo: Off -> LowBeam -> HighBeam -> Off.
        /// </summary>
        public void CycleMode()
        {
            int nextMode = ((int)m_CurrentMode + 1) % 3;
            SetMode((HeadlightMode)nextMode, animated: true);
        }

        /// <summary>
        /// Desplaza el modo de forma incremental (+1 subir, -1 bajar).
        /// </summary>
        public void StepMode(int direction)
        {
            int next = Mathf.Clamp((int)m_CurrentMode + direction, 0, 2);
            SetMode((HeadlightMode)next, animated: true);
        }

        /// <summary>
        /// Establece el modo de las luces, con o sin animación de snap.
        /// </summary>
        public void SetMode(HeadlightMode newMode, bool animated = true)
        {
            bool changed = (newMode != m_CurrentMode);
            m_CurrentMode = newMode;
            float targetAngle = GetAngleForMode(newMode);

            if (m_SnapRoutine != null)
            {
                StopCoroutine(m_SnapRoutine);
                m_SnapRoutine = null;
            }

            if (animated && gameObject.activeInHierarchy)
            {
                m_SnapRoutine = StartCoroutine(SnapToAngle(targetAngle));
            }
            else
            {
                m_CurrentAngle = targetAngle;
                ApplyRotation(targetAngle);
            }

            if (changed)
            {
                PlayClickSound();
                OnModeChanged?.Invoke(m_CurrentMode);
                ModeChanged?.Invoke(m_CurrentMode);
                Debug.Log($"[VRHeadlightKnob] Modo de luces cambiado a: {m_CurrentMode}");
            }
        }

        private void OnGrab(SelectEnterEventArgs args)
        {
            m_IsGrabbed = true;
            m_HasRotatedWhileGrabbed = false;

            if (m_SnapRoutine != null)
            {
                StopCoroutine(m_SnapRoutine);
                m_SnapRoutine = null;
            }

            EnsureHierarchyAndLocalTransform();

            m_GrabStartAngle = m_CurrentAngle;

            var interactor = args.interactorObject;
            if (interactor != null)
            {
                m_GrabStartHandAngle = CalculateHandAngle(interactor.transform.position);
                m_GrabStartRoll = GetControllerRoll(interactor.transform);
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;
            EnsureHierarchyAndLocalTransform();

            // Si el agarre fue un toque rápido sin giro perceptible, avanzar al siguiente modo por clic
            if (!m_HasRotatedWhileGrabbed && Mathf.Abs(m_CurrentAngle - m_GrabStartAngle) < 5f)
            {
                CycleMode();
                return;
            }

            HeadlightMode targetMode = GetClosestMode(m_CurrentAngle);
            SetMode(targetMode, animated: true);
        }

        private void OnActivated(ActivateEventArgs args)
        {
            // Soporte adicional para gatillo o poke activate
            CycleMode();
        }

        private void UpdateRotationFromHand(Transform interactorTransform)
        {
            float currentHandAngle = CalculateHandAngle(interactorTransform.position);
            float deltaHand = Mathf.DeltaAngle(m_GrabStartHandAngle, currentHandAngle);

            float currentRoll = GetControllerRoll(interactorTransform);
            float deltaRoll = Mathf.DeltaAngle(m_GrabStartRoll, currentRoll);

            // Escoge el delta dominante entre giro de muñeca (roll) y traslación angular de mano
            float angleDelta = (Mathf.Abs(deltaRoll) > Mathf.Abs(deltaHand)) ? deltaRoll : deltaHand;

            if (Mathf.Abs(angleDelta) > 3f)
            {
                m_HasRotatedWhileGrabbed = true;
            }

            float minAngle = Mathf.Min(m_OffAngle, Mathf.Min(m_LowBeamAngle, m_HighBeamAngle));
            float maxAngle = Mathf.Max(m_OffAngle, Mathf.Max(m_LowBeamAngle, m_HighBeamAngle));
            float newAngle = Mathf.Clamp(m_GrabStartAngle + angleDelta, minAngle - 10f, maxAngle + 10f);
            m_CurrentAngle = newAngle;
            ApplyRotation(m_CurrentAngle);

            HeadlightMode closest = GetClosestMode(m_CurrentAngle);
            if (closest != m_CurrentMode && Mathf.Abs(m_CurrentAngle - GetAngleForMode(closest)) <= m_SnapThreshold)
            {
                m_CurrentMode = closest;
                PlayClickSound();
                OnModeChanged?.Invoke(m_CurrentMode);
                ModeChanged?.Invoke(m_CurrentMode);
            }
        }

        private float GetControllerRoll(Transform interactorTransform)
        {
            Transform parentT = transform.parent != null ? transform.parent : m_OriginalParent;
            Vector3 worldAxis = parentT != null
                ? (parentT.rotation * m_InitialLocalRotation * m_RotationAxis.normalized)
                : (transform.rotation * m_RotationAxis.normalized);

            Vector3 reference = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(reference, worldAxis)) > 0.9f)
            {
                reference = Vector3.forward;
            }

            Vector3 projectedReference = Vector3.ProjectOnPlane(reference, worldAxis).normalized;
            Vector3 projectedCurrent = Vector3.ProjectOnPlane(interactorTransform.up, worldAxis).normalized;

            return Vector3.SignedAngle(projectedReference, projectedCurrent, worldAxis);
        }

        private float CalculateHandAngle(Vector3 handWorldPos)
        {
            Transform parentT = transform.parent != null ? transform.parent : m_OriginalParent;
            Vector3 localHandPos;

            if (parentT != null)
            {
                localHandPos = parentT.InverseTransformPoint(handWorldPos) - transform.localPosition;
            }
            else
            {
                localHandPos = handWorldPos - transform.position;
            }

            // Proyectar sobre el plano perpendicular a m_RotationAxis
            Vector3 axis = m_RotationAxis.normalized;
            Vector3 projected = Vector3.ProjectOnPlane(localHandPos, axis);

            if (projected.sqrMagnitude < 0.0001f)
                return 0f;

            Vector3 referenceRight = Vector3.Cross(axis, Vector3.up);
            if (referenceRight.sqrMagnitude < 0.001f)
                referenceRight = Vector3.Cross(axis, Vector3.forward);
            referenceRight.Normalize();

            Vector3 referenceUp = Vector3.Cross(referenceRight, axis).normalized;

            float x = Vector3.Dot(projected, referenceRight);
            float y = Vector3.Dot(projected, referenceUp);

            return Mathf.Atan2(x, y) * Mathf.Rad2Deg;
        }

        private IEnumerator SnapToAngle(float targetAngle)
        {
            while (Mathf.Abs(Mathf.DeltaAngle(m_CurrentAngle, targetAngle)) > 0.2f)
            {
                m_CurrentAngle = Mathf.MoveTowardsAngle(m_CurrentAngle, targetAngle, m_SnapSpeed * 60f * Time.deltaTime);
                ApplyRotation(m_CurrentAngle);
                yield return null;
            }

            m_CurrentAngle = targetAngle;
            ApplyRotation(m_CurrentAngle);
            m_SnapRoutine = null;
        }

        private void EnsureInitialized()
        {
            if (m_RotorTransform == null) m_RotorTransform = transform;
            if (m_InitialLocalRotation == default(Quaternion) || (m_InitialLocalRotation.x == 0f && m_InitialLocalRotation.y == 0f && m_InitialLocalRotation.z == 0f && m_InitialLocalRotation.w == 0f))
            {
                m_InitialLocalRotation = m_RotorTransform.localRotation;
            }
        }

        private void ApplyRotation(float angle)
        {
            EnsureInitialized();
            if (m_RotorTransform != null)
            {
                m_RotorTransform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(angle, m_RotationAxis);
            }
        }

        private float GetAngleForMode(HeadlightMode mode)
        {
            switch (mode)
            {
                case HeadlightMode.LowBeam: return m_LowBeamAngle;
                case HeadlightMode.HighBeam: return m_HighBeamAngle;
                default: return m_OffAngle;
            }
        }

        private HeadlightMode GetClosestMode(float angle)
        {
            float dOff = Mathf.Abs(angle - m_OffAngle);
            float dLow = Mathf.Abs(angle - m_LowBeamAngle);
            float dHigh = Mathf.Abs(angle - m_HighBeamAngle);

            if (dOff <= dLow && dOff <= dHigh) return HeadlightMode.Off;
            if (dLow <= dOff && dLow <= dHigh) return HeadlightMode.LowBeam;
            return HeadlightMode.HighBeam;
        }

        private void PlayClickSound()
        {
            if (m_AudioSource != null && m_ClickClip != null)
            {
                m_AudioSource.PlayOneShot(m_ClickClip, 0.7f);
            }
        }

        private void EnsureHierarchyAndLocalTransform()
        {
            if (m_OriginalParent != null && transform.parent != m_OriginalParent)
            {
                transform.SetParent(m_OriginalParent, false);
            }
            transform.localPosition = m_InitialLocalPosition;
        }
    }
}
