using System;
using System.Collections.Generic;
using System.Globalization;
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

        [Tooltip("Si se activa, la calibración se carga de PlayerPrefs al iniciar y se guarda cada vez que cambia (una sola calibración global entre escenas y sesiones). El menú principal lo apaga porque no tiene asiento.")]
        [SerializeField] private bool m_UsePersistentCalibration = true;

        // Espera mínima, en frames, entre un recentrado del runtime y la recaptura: la pose tiene que reflejar el origen nuevo.
        private const int RECAPTURE_DELAY_FRAMES = 2;
        // Si el forward horizontal de la cabeza es menor (mirando casi recto arriba/abajo), el yaw es indeterminado.
        private const float MIN_HORIZONTAL_FORWARD_SQR = 0.04f;

        private XROrigin m_Origin;
        private static readonly List<XRDisplaySubsystem> s_DisplaySubsystems = new();

        // Seam de tests: fuerza la rama VR (true) o escritorio (false). Null = comportamiento real (subsistemas de display).
        private static bool? s_VRActiveOverride;
        private static TrackingOriginModeFlags? s_TrackingOriginModeOverride;
        private static bool? s_AppFocusedOverride;

        // Por debajo de esto (en m²) la pose local de la cámara se considera caída a cero (sin foco), no una pose real.
        private const float COLLAPSED_POSE_SQR = 1e-10f;

        // Última pose de cabeza válida con foco, para sostener la vista mientras el menú del sistema está abierto.
        private Vector3 m_LastHeadLocalPosition;
        private Quaternion m_LastHeadLocalRotation = Quaternion.identity;
        private bool m_HasLastHeadPose;
        private bool m_HoldingHeadPose;
        private float m_HoldBaseHeight;
        private float m_HeldPoseBaseHeight;
        private HeldPoseSource m_LoggedHeldPoseSource = HeldPoseSource.None;

        private static bool s_HasLegacyHeadPoseOverride;
        private static bool s_LegacyHeadPoseOverrideValid;
        private static Pose s_LegacyHeadPoseOverride;

        // Compensación de la pose de la cabeza (solo VR). Es una foto, no se recalcula por frame.
        private float m_HeadYawCompensation;      // grados, yaw de la cabeza en el espacio local del XROrigin
        private Vector3 m_HeadOffsetCompensation; // posición horizontal de la cabeza en el espacio local del XROrigin (y = 0)
        private bool m_CapturePending = true;
        private int m_CaptureNotBeforeFrame;
        // Época de recaptura de HeadPoseCompensationSession que este follower ya atendió.
        private int m_KnownRecaptureEpoch;

        // Seams de tests: sin visor, la pose de cabeza de InputDevices nunca es válida y Time.frameCount no avanza en Edit Mode.
        private static bool? s_HeadPoseValidOverride;
        private static int? s_FrameCountOverride;
        private static Action s_AfterEpochReadHookForTests;

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

        /// <summary>Si este follower carga y guarda la calibración en PlayerPrefs (ver <see cref="IsPersistentCalibrationActive"/>).</summary>
        public bool UsePersistentCalibration
        {
            get => m_UsePersistentCalibration;
            set => m_UsePersistentCalibration = value;
        }

        /// <summary>
        /// La persistencia está en uso: el flag está encendido y estamos en Play Mode (o en un test con el backend en memoria).
        /// En Edit Mode real no se carga ni se guarda nada, para no ensuciar los PlayerPrefs del Editor.
        /// </summary>
        public bool IsPersistentCalibrationActive =>
            m_UsePersistentCalibration && (Application.isPlaying || DriverViewCalibrationStore.IsInMemoryBackendActive);

        private void Awake()
        {
            m_Origin = GetComponent<XROrigin>();
            // Primero la calibración guardada: AutoDiscoverDriverSeat hace un snap al asiento que ya tiene que usar el offset.
            LoadPersistentCalibration();
            if (m_DriverSeat == null)
            {
                AutoDiscoverDriverSeat();
            }
        }

        /// <summary>
        /// Aplica la calibración guardada, recortada a los límites. Asigna el campo directamente, sin disparar
        /// <see cref="CalibrationOffsetChanged"/> (se llama en Awake: sin suscriptores y con el rig sin posicionar).
        /// Devuelve true si había algo guardado y se aplicó.
        /// </summary>
        public bool LoadPersistentCalibration()
        {
            if (!IsPersistentCalibrationActive) return false;
            if (!DriverViewCalibrationStore.TryLoad(out Vector3 saved)) return false;

            m_CalibrationOffset = ClampToLimits(saved);
            Debug.Log($"[DriverSeat] Calibración guardada aplicada: ({FormatAxis(m_CalibrationOffset.x)}, {FormatAxis(m_CalibrationOffset.y)}, {FormatAxis(m_CalibrationOffset.z)})");
            return true;
        }

        private static string FormatAxis(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

        private void OnEnable()
        {
            ResetHeadPoseCaptureState();
            Application.onBeforeRender += OnBeforeRenderHoldPose;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRenderHoldPose;
        }

        /// <summary>
        /// Al habilitarse (cada escena nueva, o al reactivar el componente): si la sesión ya tiene una compensación vigente se
        /// reutiliza de inmediato; si no, se captura apenas el HMD esté trackeando.
        /// </summary>
        private void ResetHeadPoseCaptureState()
        {
            m_CapturePending = true;
            m_KnownRecaptureEpoch = HeadPoseCompensationSession.Epoch;
            // Con una invalidación reciente (ej. un recentrado durante la carga de la escena) la pose todavía puede ser anterior al
            // recentrado: se espera el mismo retardo que a un follower vivo, contado desde que la sesión notó la invalidación.
            int lastInvalidationFrame = HeadPoseCompensationSession.ObserveInvalidation(CurrentFrame());
            m_CaptureNotBeforeFrame = HeadPoseCompensationSession.RecapturePending ? lastInvalidationFrame + RECAPTURE_DELAY_FRAMES : 0;
            if (IsVRHeadsetActive()) TryReuseSessionCompensation();
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
        /// Seam de tests: fuerza el modo de origen de tracking que se usa para la altura del Camera Offset en VR
        /// (en el Editor sin runtime, <see cref="XROrigin.CurrentTrackingOriginMode"/> es Unknown). Null = el real.
        /// </summary>
        public static void SetTrackingOriginModeOverrideForTests(TrackingOriginModeFlags? mode)
        {
            s_TrackingOriginModeOverride = mode;
        }

        /// <summary>Seam de tests: fuerza si la app tiene foco (<see cref="Application.isFocused"/>). Null = el real.</summary>
        public static void SetAppFocusedOverrideForTests(bool? focused)
        {
            s_AppFocusedOverride = focused;
        }

        private static bool IsAppFocused() => s_AppFocusedOverride ?? Application.isFocused;

        /// <summary>Seam de tests: fuerza si la pose de cabeza se considera válida para capturar. Null = la lectura real de InputDevices.</summary>
        public static void SetHeadPoseValidOverrideForTests(bool? valid)
        {
            s_HeadPoseValidOverride = valid;
        }

        /// <summary>Seam de tests: fuerza el número de frame que usa la espera de recaptura. Null = <c>Time.frameCount</c>.</summary>
        public static void SetFrameCountOverrideForTests(int? frameCount)
        {
            s_FrameCountOverride = frameCount;
        }

        private static int CurrentFrame() => s_FrameCountOverride ?? Time.frameCount;

        /// <summary>
        /// Seam de tests: se invoca dentro de <see cref="RecenterHeadPose"/> justo después de leer la época de la sesión y antes de
        /// muestrear la cabeza, para simular una invalidación que llega durante la captura. Null = sin hook.
        /// </summary>
        public static void SetAfterEpochReadHookForTests(Action hook)
        {
            s_AfterEpochReadHookForTests = hook;
        }

        /// <summary>Solo para tests: corre la lógica de habilitación (en Edit Mode OnEnable no se ejecuta).</summary>
        public void RunEnableLogicForTests() => ResetHeadPoseCaptureState();

        /// <summary>Solo para tests: corre una iteración de la captura/reutilización automática que hace Update.</summary>
        public void RunHeadPoseCaptureUpdateForTests() => UpdateHeadPoseCapture();

        /// <summary>
        /// Seam de tests: fuerza la pose de cabeza de la API XR heredada. <paramref name="valid"/> = false simula que no hay
        /// pose. Pasar null en <paramref name="pose"/> restaura la lectura real de InputDevices.
        /// </summary>
        public static void SetLegacyHeadPoseOverrideForTests(Pose? pose, bool valid = true)
        {
            s_HasLegacyHeadPoseOverride = pose.HasValue;
            s_LegacyHeadPoseOverride = pose ?? Pose.identity;
            s_LegacyHeadPoseOverrideValid = pose.HasValue && valid;
        }

        /// <summary>Solo para tests: aplica lo mismo que corre justo antes de dibujar mientras se sostiene la vista.</summary>
        public void ApplyBeforeRenderForTests() => OnBeforeRenderHoldPose();

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

            // La época se lee ANTES de muestrear la cabeza: si llega una invalidación mientras se mide, el store descarta la captura.
            int expectedEpoch = HeadPoseCompensationSession.Epoch;
            s_AfterEpochReadHookForTests?.Invoke();
            Vector3 localForward = transform.InverseTransformDirection(head.transform.forward);
            localForward.y = 0f;
            if (localForward.sqrMagnitude < MIN_HORIZONTAL_FORWARD_SQR) return false;

            Vector3 localPosition = transform.InverseTransformPoint(head.transform.position);
            m_HeadOffsetCompensation = new Vector3(localPosition.x, 0f, localPosition.z);
            m_HeadYawCompensation = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;
            bool stored = HeadPoseCompensationSession.Store(expectedEpoch, m_HeadYawCompensation, m_HeadOffsetCompensation);
            Debug.Log(stored
                ? $"[DriverSeat] Compensación de cabeza capturada: yaw={FormatAxis(m_HeadYawCompensation)}, offset=({FormatAxis(m_HeadOffsetCompensation.x)}, {FormatAxis(m_HeadOffsetCompensation.z)})"
                : $"[DriverSeat] Compensación de cabeza capturada pero no guardada en la sesión (se invalidó durante la captura): yaw={FormatAxis(m_HeadYawCompensation)}");

            if (m_FollowSeat && m_DriverSeat != null)
            {
                FollowSeatPosition();
            }
            return true;
        }

        /// <summary>
        /// Pide recapturar la compensación en cuanto el HMD tenga una pose válida (ej. tras un recentrado). El pedido es de la
        /// sesión: lo atienden todos los followers vivos y los que se creen después no reutilizan la compensación vieja.
        /// </summary>
        public void RequestHeadPoseRecenter()
        {
            HeadPoseCompensationSession.Invalidate("RequestHeadPoseRecenter");
        }

        // Aplica la compensación de la sesión si hay una vigente (sin recaptura pendiente). Devuelve true si la aplicó.
        private bool TryReuseSessionCompensation()
        {
            if (!HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset)) return false;

            m_HeadYawCompensation = yaw;
            m_HeadOffsetCompensation = offset;
            m_CapturePending = false;
            Debug.Log($"[DriverSeat] Compensación de cabeza de la sesión reutilizada: yaw={FormatAxis(yaw)}, offset=({FormatAxis(offset.x)}, {FormatAxis(offset.z)})");

            if (m_FollowSeat && m_DriverSeat != null)
            {
                FollowSeatPosition();
            }
            return true;
        }

        private void UpdateHeadPoseCapture()
        {
            if (!IsVRHeadsetActive()) return;

            HeadPoseCompensationSession.RefreshSubsystemsIfDue();

            int lastInvalidationFrame = HeadPoseCompensationSession.ObserveInvalidation(CurrentFrame());
            int epoch = HeadPoseCompensationSession.Epoch;
            if (epoch != m_KnownRecaptureEpoch)
            {
                // La sesión pidió recapturar (ej. el runtime recentró): la pose del frame siguiente ya refleja el origen nuevo.
                m_KnownRecaptureEpoch = epoch;
                m_CapturePending = true;
                m_CaptureNotBeforeFrame = lastInvalidationFrame + RECAPTURE_DELAY_FRAMES;
                Debug.Log($"[DriverSeat] Compensación de cabeza: recaptura pedida: {HeadPoseCompensationSession.LastReason}");
            }

            if (!m_CapturePending || CurrentFrame() < m_CaptureNotBeforeFrame) return;

            // Una compensación vigente de la sesión (ej. el follower se habilitó antes de resolver la rama VR) no se recaptura.
            if (TryReuseSessionCompensation()) return;
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
            if (s_HeadPoseValidOverride.HasValue) return s_HeadPoseValidOverride.Value;

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
                // El Camera Offset debe quedar como lo deja el XROrigin: X/Z en 0, sin rotación y con la altura de su modo de
                // tracking. Se restaura si algo lo pisó: el simulador al inicializarse o la rama de escritorio de arriba. Esta
                // última corre un frame al volver de passthrough en el Quest (el display figura detenido justo al reanudar) y
                // dejaba la cámara 1,36 m más alta hasta que el usuario recentrara.
                Vector3 localPos = offsetTransform.localPosition;
                float baseHeight;
                if (TryGetVRCameraOffsetHeight(out float vrHeight))
                {
                    baseHeight = vrHeight;
                }
                else
                {
                    // Modo desconocido: se conserva la altura actual (sin contar lo que sumó el sostén de la vista).
                    baseHeight = m_HoldingHeadPose ? m_HoldBaseHeight : localPos.y;
                }

                Vector3 targetPos = new Vector3(0f, baseHeight, 0f);
                Quaternion targetRot = Quaternion.identity;

                // Sin foco (menú del botón Meta) el Quest sigue mostrando la app de fondo, pero la pose local de la cámara
                // cae a (0, 0, 0) y la vista queda al ras del asiento. Mientras dure, el Camera Offset lleva la pose de la
                // cabeza: la de la API XR heredada si sigue viva (el fondo acompaña la cabeza) o, si no, la última conocida.
                Transform head = m_Origin.Camera != null ? m_Origin.Camera.transform : null;
                if (head != null)
                {
                    bool focused = IsAppFocused();
                    bool positionCollapsed = head.localPosition.sqrMagnitude < COLLAPSED_POSE_SQR;
                    if (focused && !positionCollapsed)
                    {
                        m_LastHeadLocalPosition = head.localPosition;
                        m_LastHeadLocalRotation = head.localRotation;
                        m_HasLastHeadPose = true;
                    }

                    if (!focused && positionCollapsed && m_HasLastHeadPose)
                    {
                        if (!m_HoldingHeadPose)
                        {
                            m_HoldingHeadPose = true;
                            m_HoldBaseHeight = baseHeight;
                        }

                        m_HeldPoseBaseHeight = baseHeight;
                        ComputeHeldOffset(head, baseHeight, out targetPos, out targetRot);
                    }
                    else
                    {
                        if (m_HoldingHeadPose) LogHeldPoseSource(HeldPoseSource.None);
                        m_HoldingHeadPose = false;
                    }
                }

                if ((localPos - targetPos).sqrMagnitude > 0.0001f)
                {
                    offsetTransform.localPosition = targetPos;
                }

                if (offsetTransform.localRotation != targetRot)
                {
                    offsetTransform.localRotation = targetRot;
                }
            }
        }

        private enum HeldPoseSource { None, LegacyLive, LastKnown }

        /// <summary>
        /// Pose del Camera Offset mientras se sostiene la vista sin foco. La cámara quedó en (0, 0, 0), así que el offset
        /// lleva la pose entera: altura base + posición de la cabeza, y la rotación si la de la cámara también cayó.
        /// </summary>
        private void ComputeHeldOffset(Transform head, float baseHeight, out Vector3 position, out Quaternion rotation)
        {
            Vector3 headPosition;
            Quaternion headRotation;
            if (TryGetLegacyHeadPose(out headPosition, out headRotation))
            {
                LogHeldPoseSource(HeldPoseSource.LegacyLive);
            }
            else
            {
                headPosition = m_LastHeadLocalPosition;
                headRotation = m_LastHeadLocalRotation;
                LogHeldPoseSource(HeldPoseSource.LastKnown);
            }

            position = new Vector3(0f, baseHeight, 0f) + headPosition;
            // Si la rotación de la cámara sigue viva, el offset no agrega rotación (se duplicaría).
            rotation = head.localRotation == Quaternion.identity ? headRotation : Quaternion.identity;
        }

        /// <summary>
        /// Pose de la cabeza según la API XR heredada (InputDevices), en el mismo espacio que la pose local de la cámara.
        /// No depende del Input System, que sin foco deja la cámara en cero. False si no hay una pose trackeada no nula.
        /// </summary>
        private static bool TryGetLegacyHeadPose(out Vector3 position, out Quaternion rotation)
        {
            if (s_HasLegacyHeadPoseOverride)
            {
                position = s_LegacyHeadPoseOverride.position;
                rotation = s_LegacyHeadPoseOverride.rotation;
                return s_LegacyHeadPoseOverrideValid;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid) return false;
            if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) return false;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out position)) return false;
            if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation)) return false;

            float rotationNorm = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            return position.sqrMagnitude >= COLLAPSED_POSE_SQR && rotationNorm > 0.5f;
        }

        // Justo antes de dibujar se vuelve a leer la pose, para que el fondo siga la cabeza con la menor latencia posible.
        private void OnBeforeRenderHoldPose()
        {
            if (!m_HoldingHeadPose || m_Origin == null || m_Origin.CameraFloorOffsetObject == null || m_Origin.Camera == null) return;

            Transform head = m_Origin.Camera.transform;
            if (head.localPosition.sqrMagnitude >= COLLAPSED_POSE_SQR) return;

            ComputeHeldOffset(head, m_HeldPoseBaseHeight, out Vector3 position, out Quaternion rotation);
            Transform offsetTransform = m_Origin.CameraFloorOffsetObject.transform;
            offsetTransform.localPosition = position;
            offsetTransform.localRotation = rotation;
        }

        private void LogHeldPoseSource(HeldPoseSource source)
        {
            if (source == m_LoggedHeldPoseSource) return;
            m_LoggedHeldPoseSource = source;
            switch (source)
            {
                case HeldPoseSource.LegacyLive:
                    Debug.Log("[DriverSeat] Sin foco: la vista sigue la cabeza con la pose de la API XR heredada.");
                    break;
                case HeldPoseSource.LastKnown:
                    Debug.Log("[DriverSeat] Sin foco: no hay pose XR heredada válida, se congela la última pose conocida.");
                    break;
                default:
                    Debug.Log("[DriverSeat] Foco recuperado: se suelta la vista sostenida.");
                    break;
            }
        }

        /// <summary>
        /// Altura del Camera Offset que corresponde en VR, igual que la aplica el XROrigin: 0 en modo Floor y
        /// <see cref="XROrigin.CameraYOffset"/> en Device/Unbounded. False si el modo todavía es desconocido (no se toca la altura).
        /// </summary>
        private bool TryGetVRCameraOffsetHeight(out float height)
        {
            TrackingOriginModeFlags mode = s_TrackingOriginModeOverride ?? m_Origin.CurrentTrackingOriginMode;
            switch (mode)
            {
                case TrackingOriginModeFlags.Floor:
                    height = 0f;
                    return true;
                case TrackingOriginModeFlags.Device:
                case TrackingOriginModeFlags.Unbounded:
                    height = m_Origin.CameraYOffset;
                    return true;
                default:
                    height = 0f;
                    return false;
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
            if (IsPersistentCalibrationActive) DriverViewCalibrationStore.Save(m_CalibrationOffset);
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
