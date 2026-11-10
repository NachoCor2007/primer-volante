using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Sincroniza la posición y rotación del XROrigin con el asiento del conductor (DriverSeat).
    /// Ejecución prioritaria (ExecutionOrder = -300) para que ocurra ANTES del XRInteractionManager (-105)
    /// y de los scripts de cabina. Con el auto en movimiento, si el XROrigin se reposiciona después de que
    /// el XRInteractionManager detecta hover/select, las manos quedan un frame atrasadas respecto de los
    /// interactuables del vehículo (desfase = velocidad × deltaTime) y los colliders chicos, como los del
    /// retrovisor, dejan de poder agarrarse.
    /// <para>
    /// En VR, la pose de la cabeza llega relativa al origen de tracking del runtime (en el Quest, el del último
    /// recentrado del sistema), no a donde está sentado el usuario. Para que la cámara quede en el asiento mirando hacia
    /// adelante, se guarda una "foto" (<see cref="RecenterHeadPose"/>) del desplazamiento horizontal y el yaw de la cabeza
    /// en el espacio del XROrigin, y se compensa al posicionar el rig. No se compensa la altura ni el pitch/roll.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class DriverSeatFollower : MonoBehaviour
    {
        [Header("Referencias de Asiento")]
        [Tooltip("Transform del asiento del conductor dentro del vehículo.")]
        [SerializeField] private Transform m_DriverSeat;

        [Tooltip("Si se activa, el jugador se mantiene continuamente alineado al asiento.")]
        [SerializeField] private bool m_FollowSeat = true;

        [Header("Calibración de Vista")]
        [Tooltip("Desplazamiento de la vista respecto al asiento, en el espacio local del DriverSeat (X: lateral, Y: altura, Z: adelante/atrás).")]
        [SerializeField] private Vector3 m_CalibrationOffset = Vector3.zero;

        [Tooltip("Límite mínimo (izquierda, abajo, atrás) del desplazamiento de calibración, en metros locales al asiento.")]
        [SerializeField] private Vector3 m_CalibrationLimitsMin = new Vector3(-0.25f, -0.15f, -0.25f);

        [Tooltip("Límite máximo (derecha, arriba, adelante) del desplazamiento de calibración, en metros locales al asiento.")]
        [SerializeField] private Vector3 m_CalibrationLimitsMax = new Vector3(0.25f, 0.20f, 0.25f);

        // Espera mínima, en frames, entre un recentrado del runtime y la recaptura: la pose tiene que reflejar el origen nuevo.
        private const int RECAPTURE_DELAY_FRAMES = 2;
        // Cada cuántos segundos se revisa si cambió el conjunto de subsistemas de entrada XR (reinicio de la sesión VR).
        private const float SUBSCRIPTION_REFRESH_SECONDS = 2f;
        // Si el forward horizontal de la cabeza es menor (mirando casi recto arriba/abajo), el yaw es indeterminado.
        private const float MIN_HORIZONTAL_FORWARD_SQR = 0.04f;

        private XROrigin m_Origin;
        private static readonly List<XRDisplaySubsystem> s_DisplaySubsystems = new();
        private static readonly List<XRInputSubsystem> s_InputSubsystems = new();

        // Seam de tests: fuerza la rama VR (true) o escritorio (false). Null = comportamiento real (subsistemas de display).
        private static bool? s_VRActiveOverride;

        // Compensación de la pose de la cabeza (solo VR). Es una foto, no se recalcula por frame.
        private float m_HeadYawCompensation;      // grados, yaw de la cabeza en el espacio local del XROrigin
        private Vector3 m_HeadOffsetCompensation; // posición horizontal de la cabeza en el espacio local del XROrigin (y = 0)
        private bool m_CapturePending = true;
        private int m_CaptureNotBeforeFrame;
        private volatile bool m_RecenterRequested;
        private float m_NextSubscriptionRefresh;
        private readonly List<XRInputSubsystem> m_SubscribedSubsystems = new();

        private float m_DesktopLookPitch = 0f;
        private float m_DesktopLookYaw = 0f;

        public Transform DriverSeat
        {
            get => m_DriverSeat;
            set
            {
                m_DriverSeat = value;
                if (m_DriverSeat != null && m_FollowSeat)
                {
                    SnapToSeat();
                }
            }
        }

        public bool FollowSeat
        {
            get => m_FollowSeat;
            set
            {
                m_FollowSeat = value;
                if (m_FollowSeat && m_DriverSeat != null)
                {
                    SnapToSeat();
                }
            }
        }

        /// <summary>
        /// Permite asignar limpiamente el transform del asiento y opcionalmente forzar snap inmediato.
        /// </summary>
        public void SetDriverSeat(Transform seat, bool snap = true)
        {
            m_DriverSeat = seat;
            if (snap && m_DriverSeat != null && m_FollowSeat)
            {
                SnapToSeat();
            }
        }

        /// <summary>
        /// Desplazamiento de calibración actual (espacio local del asiento). Al asignarlo se recorta a los límites configurados.
        /// </summary>
        public Vector3 CalibrationOffset
        {
            get => m_CalibrationOffset;
            set => SetCalibrationOffset(value);
        }

        /// <summary>
        /// Se dispara cuando cambia el desplazamiento de calibración, con el desplazamiento en
        /// espacio de mundo (delta) que sufrió la vista. Permite que otros elementos (ej. el menú
        /// in-game) se muevan junto con el usuario sin quedar pegados a su cabeza.
        /// </summary>
        public event Action<Vector3> CalibrationOffsetChanged;

        public Vector3 CalibrationLimitsMin => m_CalibrationLimitsMin;

        public Vector3 CalibrationLimitsMax => m_CalibrationLimitsMax;

        private void Awake()
        {
            m_Origin = GetComponent<XROrigin>();
            if (m_DriverSeat == null)
            {
                AutoDiscoverDriverSeat();
            }
        }

        private void OnEnable()
        {
            // En cada escena (y al reactivar el componente) se captura de nuevo apenas el HMD esté trackeando.
            m_CapturePending = true;
            m_CaptureNotBeforeFrame = 0;
            m_NextSubscriptionRefresh = 0f;
        }

        private void OnDisable()
        {
            UnsubscribeFromTrackingOrigin();
        }

        private void OnDestroy()
        {
            UnsubscribeFromTrackingOrigin();
        }

        private void Start()
        {
            FollowSeatPosition();
        }

        /// <summary>
        /// Seam de tests: fuerza <see cref="IsVRHeadsetActive"/> a true/false. Pasar null restaura el comportamiento real.
        /// </summary>
        public static void SetVRActiveOverrideForTests(bool? vrActive)
        {
            s_VRActiveOverride = vrActive;
        }

        /// <summary>
        /// Determina si hay un visor de Realidad Virtual (HMD) conectado y renderizando activamente.
        /// No usa APIs obsoletas (como XRSettings.isDeviceActive).
        /// </summary>
        public static bool IsVRHeadsetActive()
        {
            if (s_VRActiveOverride.HasValue) return s_VRActiveOverride.Value;

            s_DisplaySubsystems.Clear();
            SubsystemManager.GetSubsystems(s_DisplaySubsystems);
            for (int i = 0; i < s_DisplaySubsystems.Count; i++)
            {
                if (s_DisplaySubsystems[i].running)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Localiza automáticamente el vehículo del jugador en la escena usando FindAnyObjectByType
        /// y vincula el Transform 'DriverSeat'.
        /// </summary>
        public bool AutoDiscoverDriverSeat()
        {
            if (m_DriverSeat != null) return true;

            var vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            if (vehicle != null)
            {
                Transform seat = FindDriverSeatRecursive(vehicle.transform);
                if (seat != null)
                {
                    m_DriverSeat = seat;
                    if (m_FollowSeat)
                    {
                        SnapToSeat();
                    }
                    return true;
                }
            }

            return false;
        }

        private Transform FindDriverSeatRecursive(Transform current)
        {
            if (current == null) return null;
            if (string.Equals(current.name, "DriverSeat", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform found = FindDriverSeatRecursive(current.GetChild(i));
                if (found != null) return found;
            }

            return null;
        }

        private void Update()
        {
            UpdateHeadPoseCapture();
            FollowSeatPosition();
        }

        private void LateUpdate()
        {
            FollowSeatPosition();
        }

        private void FollowSeatPosition()
        {
            if (m_DriverSeat == null && m_FollowSeat)
            {
                AutoDiscoverDriverSeat();
            }

            if (m_FollowSeat && m_DriverSeat != null)
            {
                bool vrActive = IsVRHeadsetActive();
                Vector3 position = m_DriverSeat.TransformPoint(m_CalibrationOffset);
                Quaternion rotation = m_DriverSeat.rotation;

                if (vrActive)
                {
                    // Compensación de la pose de la cabeza: se gira el rig -yaw para que la cabeza (que mira con yaw local)
                    // quede mirando hacia adelante del asiento, y se desplaza el rig para que la cabeza (desplazada
                    // m_HeadOffsetCompensation en el espacio del rig, ya rotado) caiga justo en el punto de vista del asiento.
                    rotation = m_DriverSeat.rotation * Quaternion.Euler(0f, -m_HeadYawCompensation, 0f);
                    position -= rotation * m_HeadOffsetCompensation;
                }

                transform.position = position;
                transform.rotation = rotation;

                AlignCameraOffset(vrActive);
            }
        }

        /// <summary>
        /// Captura la compensación de la pose de la cabeza (desplazamiento horizontal y yaw respecto del XROrigin), de modo
        /// que la cámara quede horizontalmente en el punto de vista del asiento (asiento + calibración) y mirando hacia
        /// el frente del asiento. Reemplaza cualquier captura anterior: la pose se mide en el espacio local del XROrigin,
        /// que no depende de dónde esté el rig ni de la compensación que ya tenía aplicada. No toca altura, pitch ni roll.
        /// Devuelve false si no hay cámara o si la cabeza mira casi vertical (yaw indeterminado); en ese caso no cambia nada.
        /// </summary>
        public bool RecenterHeadPose()
        {
            if (m_Origin == null)
            {
                m_Origin = GetComponent<XROrigin>();
            }

            Camera head = m_Origin != null ? m_Origin.Camera : null;
            if (head == null) return false;

            Vector3 localForward = transform.InverseTransformDirection(head.transform.forward);
            localForward.y = 0f;
            if (localForward.sqrMagnitude < MIN_HORIZONTAL_FORWARD_SQR) return false;

            Vector3 localPosition = transform.InverseTransformPoint(head.transform.position);
            m_HeadOffsetCompensation = new Vector3(localPosition.x, 0f, localPosition.z);
            m_HeadYawCompensation = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;

            if (m_FollowSeat && m_DriverSeat != null)
            {
                FollowSeatPosition();
            }
            return true;
        }

        /// <summary>Pide recapturar la compensación en cuanto el HMD tenga una pose válida (ej. tras un recentrado).</summary>
        public void RequestHeadPoseRecenter()
        {
            m_RecenterRequested = true;
        }

        private void UpdateHeadPoseCapture()
        {
            if (!IsVRHeadsetActive()) return;

            RefreshTrackingOriginSubscriptions();

            if (m_RecenterRequested)
            {
                // El runtime recentró: la pose del frame siguiente ya refleja el origen nuevo.
                m_RecenterRequested = false;
                m_CapturePending = true;
                m_CaptureNotBeforeFrame = Time.frameCount + RECAPTURE_DELAY_FRAMES;
            }

            if (!m_CapturePending || Time.frameCount < m_CaptureNotBeforeFrame) return;
            if (!IsHeadPoseValid()) return;

            if (RecenterHeadPose())
            {
                m_CapturePending = false;
            }
        }

        /// <summary>
        /// El HMD está trackeando y su pose ya no es la identidad (al arrancar la app el runtime puede entregar la pose
        /// identidad durante unos frames). Se usan InputDevices y también la pose local de la cámara del rig.
        /// </summary>
        private bool IsHeadPoseValid()
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return false;
            if (head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) return false;

            bool hasPosition = head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 devicePosition);
            bool hasRotation = head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion deviceRotation);
            bool positionIsIdentity = !hasPosition || devicePosition == Vector3.zero;
            bool rotationIsIdentity = !hasRotation || deviceRotation == Quaternion.identity;
            if (positionIsIdentity && rotationIsIdentity) return false;

            Camera cam = m_Origin != null ? m_Origin.Camera : null;
            if (cam == null) return false;
            Transform camTransform = cam.transform;
            return camTransform.localPosition != Vector3.zero || camTransform.localRotation != Quaternion.identity;
        }

        // Los subsistemas pueden crearse después del arranque (o recrearse al reiniciar la sesión VR): se revisan cada tanto.
        // Un subsistema nuevo implica un origen de tracking potencialmente nuevo, así que dispara una recaptura.
        private void RefreshTrackingOriginSubscriptions()
        {
            if (Time.unscaledTime < m_NextSubscriptionRefresh) return;
            m_NextSubscriptionRefresh = Time.unscaledTime + SUBSCRIPTION_REFRESH_SECONDS;

            s_InputSubsystems.Clear();
            SubsystemManager.GetSubsystems(s_InputSubsystems);

            for (int i = m_SubscribedSubsystems.Count - 1; i >= 0; i--)
            {
                XRInputSubsystem known = m_SubscribedSubsystems[i];
                if (known == null || !s_InputSubsystems.Contains(known))
                {
                    if (known != null) known.trackingOriginUpdated -= OnTrackingOriginUpdated;
                    m_SubscribedSubsystems.RemoveAt(i);
                }
            }

            for (int i = 0; i < s_InputSubsystems.Count; i++)
            {
                XRInputSubsystem subsystem = s_InputSubsystems[i];
                if (m_SubscribedSubsystems.Contains(subsystem)) continue;

                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                m_SubscribedSubsystems.Add(subsystem);
                m_CapturePending = true;
            }
        }

        private void UnsubscribeFromTrackingOrigin()
        {
            for (int i = 0; i < m_SubscribedSubsystems.Count; i++)
            {
                if (m_SubscribedSubsystems[i] != null)
                {
                    m_SubscribedSubsystems[i].trackingOriginUpdated -= OnTrackingOriginUpdated;
                }
            }
            m_SubscribedSubsystems.Clear();
        }

        // Puede llamarse desde el runtime XR: solo se marca el pedido, la recaptura ocurre en Update.
        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            m_RecenterRequested = true;
        }

        private void AlignCameraOffset(bool vrActive)
        {
            if (m_Origin == null)
            {
                m_Origin = GetComponent<XROrigin>();
            }

            if (m_Origin == null || m_Origin.CameraFloorOffsetObject == null)
            {
                return;
            }

            Transform offsetTransform = m_Origin.CameraFloorOffsetObject.transform;

            if (!vrActive)
            {
                // Modo Desktop / Simulador (sin cascos VR):
                // XR Interaction Simulator o XR Device Simulator fuerzan CameraFloorOffsetObject.transform.position = Vector3.zero
                // en espacio de mundo al inicializarse, lo cual en una escena con vehículo manda la cámara al origen del mundo (0,0,0)
                // debajo del mapa en lugar de estar dentro del habitáculo.
                // Aseguramos que Camera Offset permanezca relativo al asiento del auto a la altura de los ojos:
                float targetY = m_Origin.CameraYOffset > 0.001f ? m_Origin.CameraYOffset : 1.36144f;
                offsetTransform.localPosition = new Vector3(0f, targetY, 0f);

                HandleDesktopLook(offsetTransform);
            }
            else
            {
                // Modo VR con cascos puestos:
                // El tracking de orientación y posición de cabeza lo gestiona el HMD a través del TrackedPoseDriver en la cámara.
                // Solo corregimos si el Camera Offset fue corrompido lateralmente (X o Z != 0) por inicializaciones previas del simulador:
                Vector3 localPos = offsetTransform.localPosition;
                if (Mathf.Abs(localPos.x) > 0.01f || Mathf.Abs(localPos.z) > 0.01f)
                {
                    localPos.x = 0f;
                    localPos.z = 0f;
                    offsetTransform.localPosition = localPos;
                }
            }
        }

        private void HandleDesktopLook(Transform offsetTransform)
        {
            // Soporte para mirar alrededor con Gamepad (PS4 / Xbox / genérico): stick derecho gira, R3 recentra:
            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.rightStickButton.wasPressedThisFrame)
                {
                    m_DesktopLookYaw = 0f;
                    m_DesktopLookPitch = 0f;
                    offsetTransform.localRotation = Quaternion.identity;
                }
                else
                {
                    Vector2 look = gamepad.rightStick.ReadValue();
                    if (look.sqrMagnitude > 0.04f)
                    {
                        m_DesktopLookYaw += look.x * 90f * Time.unscaledDeltaTime;
                        m_DesktopLookPitch = Mathf.Clamp(m_DesktopLookPitch - look.y * 70f * Time.unscaledDeltaTime, -60f, 60f);
                        offsetTransform.localRotation = Quaternion.Euler(m_DesktopLookPitch, m_DesktopLookYaw, 0f);
                    }
                }
            }

            // Soporte para mirar alrededor con Mouse (manteniendo click derecho) en Desktop:
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();
                if (mouseDelta.sqrMagnitude > 0.01f)
                {
                    m_DesktopLookYaw += mouseDelta.x * 0.15f;
                    m_DesktopLookPitch = Mathf.Clamp(m_DesktopLookPitch - mouseDelta.y * 0.15f, -60f, 60f);
                    offsetTransform.localRotation = Quaternion.Euler(m_DesktopLookPitch, m_DesktopLookYaw, 0f);
                }
            }

            // Tecla R en teclado para recentrar la vista en Desktop:
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                m_DesktopLookYaw = 0f;
                m_DesktopLookPitch = 0f;
                offsetTransform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// Ajusta un solo eje del desplazamiento de calibración (0: lateral, 1: altura, 2: adelante/atrás).
        /// </summary>
        public void SetCalibrationAxis(int axis, float value)
        {
            Vector3 offset = m_CalibrationOffset;
            offset[axis] = value;
            CalibrationOffset = offset;
        }

        /// <summary>
        /// Restablece la vista a la posición inicial (sin desplazamiento respecto al asiento).
        /// </summary>
        public void ResetCalibration()
        {
            SetCalibrationOffset(Vector3.zero);
        }

        private void SetCalibrationOffset(Vector3 newOffset)
        {
            newOffset = ClampToLimits(newOffset);
            if (newOffset == m_CalibrationOffset) return;

            Vector3 worldDelta = Vector3.zero;
            if (m_DriverSeat != null)
            {
                worldDelta = m_DriverSeat.TransformPoint(newOffset) - m_DriverSeat.TransformPoint(m_CalibrationOffset);
            }

            m_CalibrationOffset = newOffset;
            CalibrationOffsetChanged?.Invoke(worldDelta);
        }

        private Vector3 ClampToLimits(Vector3 offset)
        {
            return new Vector3(
                Mathf.Clamp(offset.x, m_CalibrationLimitsMin.x, m_CalibrationLimitsMax.x),
                Mathf.Clamp(offset.y, m_CalibrationLimitsMin.y, m_CalibrationLimitsMax.y),
                Mathf.Clamp(offset.z, m_CalibrationLimitsMin.z, m_CalibrationLimitsMax.z));
        }

        /// <summary>
        /// Fuerza una alineación instantánea con el asiento del conductor.
        /// </summary>
        public void SnapToSeat()
        {
            FollowSeatPosition();
        }
    }
}
