using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Perilla rotativa interactuable en VR para el control de luces (Off / Position / LowBeam / HighBeam).
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

        [Tooltip("Ángulo local para la posición Luces de Posición (30°).")]
        [SerializeField] private float m_PositionAngle = 30f;

        [Tooltip("Ángulo local para la posición Luces Bajas (60°).")]
        [SerializeField] private float m_LowBeamAngle = 60f;

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

        [Header("Depuración")]
        [Tooltip("Escribe en consola un log por cada cambio de estado. Apagado por defecto para no tapar el log de infracciones.")]
        [SerializeField] private bool m_EnableDebugLogs = false;

        [Header("Eventos")]
        [Tooltip("Evento invocado al cambiar el modo de luces.")]
        public HeadlightModeEvent OnModeChanged = new HeadlightModeEvent();

        public event Action<HeadlightMode> ModeChanged;

        /// <summary>
        /// Modo actual de las luces.
        /// </summary>
        public HeadlightMode CurrentMode => m_CurrentMode;

        /// <summary>
        /// Ángulos locales de la perilla indexados por el valor entero de <see cref="HeadlightMode"/>
        /// (Off, Position, LowBeam, HighBeam). Única fuente de verdad para la cantidad de modos.
        /// </summary>
        private float[] AngleTable
        {
            get
            {
                // Buffer reutilizado: se consulta varias veces por frame mientras la perilla está agarrada.
                m_AngleBuffer[0] = m_OffAngle;
                m_AngleBuffer[1] = m_PositionAngle;
                m_AngleBuffer[2] = m_LowBeamAngle;
                m_AngleBuffer[3] = m_HighBeamAngle;
                return m_AngleBuffer;
            }
        }

        private readonly float[] m_AngleBuffer = new float[4];

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
        /// <summary>Rotación de la mano al agarrar, expresada en el marco del auto (ver <see cref="HandRotationInCarFrame"/>).</summary>
        private Quaternion m_GrabStartHandRotation = Quaternion.identity;
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
            // Atajos bloqueados mientras la simulación está pausada.
            if (m_IsGrabbed || SimulationPause.IsPaused) return;

            // Teclado: tecla L cicla Off -> Position -> LowBeam -> HighBeam -> Off
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
        /// Cicla al siguiente modo: Off -> Position -> LowBeam -> HighBeam -> Off.
        /// </summary>
        public void CycleMode()
        {
            int nextMode = ((int)m_CurrentMode + 1) % AngleTable.Length;
            SetMode((HeadlightMode)nextMode, animated: true);
        }

        /// <summary>
        /// Desplaza el modo de forma incremental (+1 subir, -1 bajar).
        /// </summary>
        public void StepMode(int direction)
        {
            int next = Mathf.Clamp((int)m_CurrentMode + direction, 0, AngleTable.Length - 1);
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
                if (m_EnableDebugLogs)
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
                m_GrabStartHandRotation = HandRotationInCarFrame(interactor.transform);
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

        /// <summary>
        /// Signo que convierte el twist de la muñeca (rotación positiva alrededor del forward del control)
        /// en ángulo de la perilla. En Unity una rotación positiva alrededor del forward del control es
        /// antihoraria vista desde atrás (desde el conductor); la perilla gira horario (visto desde el conductor)
        /// con ángulo positivo porque su eje de giro apunta hacia el conductor. Por eso el signo es -1:
        /// muñeca en sentido horario => ángulo creciente => Off hacia Altas.
        /// </summary>
        public const float TwistToKnobSign = -1f;

        /// <summary>
        /// Twist en grados [-180, 180] de la rotación relativa entre <paramref name="start"/> y <paramref name="current"/>
        /// alrededor del eje forward local del control (descomposición swing-twist). Ignora por completo la posición
        /// de la mano y el swing (inclinación) del control.
        /// </summary>
        public static float ComputeTwistDegrees(Quaternion start, Quaternion current)
        {
            Quaternion delta = Quaternion.Inverse(start) * current;
            float twist = 2f * Mathf.Atan2(delta.z, delta.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, twist);
        }

        /// <summary>
        /// Ángulo de la perilla (sin clamp) para un agarre iniciado en <paramref name="grabStartAngle"/> y una muñeca
        /// que giró <paramref name="twistDegrees"/> (ver <see cref="ComputeTwistDegrees"/>). Relación 1:1.
        /// </summary>
        public static float KnobAngleFromTwist(float grabStartAngle, float twistDegrees)
        {
            return grabStartAngle + TwistToKnobSign * twistDegrees;
        }

        /// <summary>
        /// Rotación de la mano relativa al padre de la perilla (el auto). Así, si el auto gira o se inclina
        /// mientras la perilla está agarrada, ese movimiento no se lee como giro de muñeca.
        /// </summary>
        private Quaternion HandRotationInCarFrame(Transform hand)
        {
            Transform frame = m_OriginalParent != null ? m_OriginalParent : transform.parent;
            return frame != null ? Quaternion.Inverse(frame.rotation) * hand.rotation : hand.rotation;
        }

        private void UpdateRotationFromHand(Transform interactorTransform)
        {
            float twist = ComputeTwistDegrees(m_GrabStartHandRotation, HandRotationInCarFrame(interactorTransform));
            float rawAngle = KnobAngleFromTwist(m_GrabStartAngle, twist);

            if (Mathf.Abs(rawAngle - m_GrabStartAngle) > 3f)
            {
                m_HasRotatedWhileGrabbed = true;
            }

            float minAngle = Mathf.Min(AngleTable);
            float maxAngle = Mathf.Max(AngleTable);
            float newAngle = Mathf.Clamp(rawAngle, minAngle - 10f, maxAngle + 10f);
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

        /// <summary>
        /// Ángulo local de la perilla en el que queda el modo indicado.
        /// </summary>
        public float GetAngleForMode(HeadlightMode mode)
        {
            float[] angles = AngleTable;
            int index = (int)mode;
            return (index >= 0 && index < angles.Length) ? angles[index] : m_OffAngle;
        }

        private HeadlightMode GetClosestMode(float angle)
        {
            float[] angles = AngleTable;
            int closestIndex = 0;
            float closestDelta = Mathf.Abs(angle - angles[0]);

            for (int i = 1; i < angles.Length; i++)
            {
                float delta = Mathf.Abs(angle - angles[i]);
                if (delta < closestDelta)
                {
                    closestDelta = delta;
                    closestIndex = i;
                }
            }

            return (HeadlightMode)closestIndex;
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
