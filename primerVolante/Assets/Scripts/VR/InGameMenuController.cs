using TMPro;
using PrimerVolante.Lessons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using XRNode = UnityEngine.XR.XRNode;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controla la apertura/cierre del menú in-game (Canvas World Space) y su posicionamiento
    /// frente a la cabeza del usuario. Mientras está abierto detiene la simulación (Time.timeScale = 0):
    /// física, vehículos, NPC, semáforos y animaciones quedan congelados, pero el seguimiento de la cabeza,
    /// los rayos de los controles y la UI siguen respondiendo porque corren en Update sin depender del tiempo
    /// escalado. Al cerrarlo se restaura la escala de tiempo que había.
    /// Se abre/cierra con el botón Menu físico del control VR (leído vía la API legada
    /// UnityEngine.XR.InputDevices, la única que en este proyecto refleja en vivo el estado
    /// de los controles OpenXR; la capa de dispositivos del nuevo Input System no se actualiza
    /// para estos perfiles) o con una tecla de teclado para pruebas en el Editor/XR Device Simulator.
    /// </summary>
    public class InGameMenuController : MonoBehaviour
    {
        [Header("Referencias de UI")]
        [Tooltip("Raíz del Canvas del menú (se activa/desactiva al abrir/cerrar).")]
        [SerializeField] private GameObject m_MenuRoot;

        [Tooltip("Panel principal del menú (Reanudar / Calibrar posición).")]
        [SerializeField] private GameObject m_MainPanel;

        [Tooltip("Panel de calibración de la vista (sliders).")]
        [SerializeField] private GameObject m_CalibrationPanel;

        [Tooltip("Muestra el botón \"Calibrar posición\" (MainPanel/Button_Calibrate) cada vez que se abre el menú. Se apaga en el " +
                 "NewMainMenu, que no tiene auto ni asiento que calibrar: el botón queda inactivo en el menú de pausa por desconexión VR.")]
        [SerializeField] private bool m_ShowCalibrationButton = true;

        [Tooltip("Botón \"Calibrar posición\" del panel principal. Si se deja vacío se busca como MainPanel/Button_Calibrate " +
                 "(escenas con el menú serializado antes de este campo).")]
        [SerializeField] private GameObject m_CalibrateButton;

        [Tooltip("Botón Volver del panel de calibración. Se oculta cuando la calibración se abre standalone " +
                 "(OpenCalibrationStandalone), porque no hay panel principal al que volver.")]
        [SerializeField] private GameObject m_CalibrationBackButton;

        [Header("Posicionamiento")]
        [Tooltip("Transform de la cabeza del usuario (cámara del XR Origin). Si se deja vacío, se usa Camera.main.")]
        [SerializeField] private Transform m_HeadTransform;

        [Tooltip("Distancia a la que se ubica el menú frente a la cabeza al abrirse (metros).")]
        [SerializeField] private float m_DistanceFromHead = 0.8f;

        [Tooltip("Ángulo horizontal, a la derecha del frente de la referencia (el auto), al que se ubica el menú de pausa, el aviso " +
                 "de desconexión y el panel de calibración standalone (grados). Sin referencia solo aplica al panel standalone.")]
        [FormerlySerializedAs("m_StandaloneYawOffsetDegrees")]
        [SerializeField] private float m_YawOffsetDegrees = 45f;

        [Tooltip("Transform cuyo frente horizontal es la referencia de los 45° del menú (el vehículo o el asiento): el menú queda " +
                 "siempre a la derecha del auto, medido desde la cabeza, sin importar hacia dónde mire el jugador. " +
                 "Si se deja vacío se busca en la escena del menú el VehicleController (o el asiento del DriverSeatFollower); si no hay " +
                 "ninguno, el menú de pausa se ubica frente a la cabeza (0°) y el panel standalone a 45° de la cabeza.")]
        [FormerlySerializedAs("m_StandaloneForwardReference")]
        [SerializeField] private Transform m_ForwardReference;

        [Header("Entrada (VR - Botón Menu físico)")]
        [Tooltip("Si se apaga, se ignoran el botón Menu del control y la tecla de teclado (el menú de pausa no se puede abrir). " +
                 "Se apaga en el menú principal en cabina, donde el menú de pausa no aplica.")]
        [SerializeField] private bool m_MenuButtonEnabled = true;

        [Tooltip("Nodo XR del control cuyo botón Menu abre/cierra el menú (mano izquierda en Meta Quest).")]
        [SerializeField] private XRNode m_MenuButtonNode = XRNode.LeftHand;

        [Header("Entrada (Teclado, pruebas en Editor)")]
        [Tooltip("Tecla de teclado que abre/cierra el menú para pruebas en el Editor/XR Device Simulator.")]
        [SerializeField] private Key m_ToggleMenuKey = Key.Escape;

        [Header("Interacción a distancia")]
        [Tooltip("Componente que sincroniza la vista con el asiento. Si se deja vacío, se autodetecta. " +
                 "Se usa para que el menú se desplace junto con la vista al calibrar (sin rotar hacia la cabeza).")]
        [SerializeField] private DriverSeatFollower m_DriverSeatFollower;

        [Tooltip("Near-Far Interactors (uno por mano) a los que se les habilita el cast a distancia " +
                 "mientras el menú está abierto, y se les restaura su estado previo al cerrarlo. " +
                 "El rayo queda restringido a la capa UI (no interactúa con la cabina). " +
                 "Normalmente el juego lo mantiene apagado para no interferir con agarrar controles.")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        [Header("Pausa")]
        [Tooltip("Detiene la simulación (Time.timeScale = 0) mientras el menú está abierto. La cabeza, los rayos y la UI siguen funcionando.")]
        [SerializeField] private bool m_PauseSimulation = true;

        [Tooltip("Pausa también el audio del mundo (motor, viento, guiños) mientras la simulación está detenida.")]
        [SerializeField] private bool m_PauseAudio = true;

        private XRButtonEdgeDetector m_MenuButtonEdge;


        // Aviso de la pausa por desconexión VR (TextMeshProUGUI creado por código, hijo de MainPanel) y bloqueo de Reanudar.
        private TextMeshProUGUI m_NoticeLabel;
        private Button m_ResumeButton;
        private bool m_ResumeBlocked;
        private bool m_OpenedByConnectionPause;

        // Transición de escena en curso (reiniciar/salir de la lección): el menú no se puede cerrar mientras dure. Se libera solo si la
        // transición termina y este menú sigue vivo (ver IsLockedForTransition).
        private bool m_LockedForTransition;

        // Modo en el que estaba el menú cuando la pausa por conexión lo tomó, para restaurarlo al reanudar.
        private enum PreviousMenuMode { Closed, Normal, Standalone }
        private PreviousMenuMode m_PreviousMode;
        private bool m_PreviousCalibrationPanelActive;
        private bool m_PreviousBackButtonActive;
        private bool m_StandaloneOpen;

        public bool IsOpen => m_MenuRoot != null && m_MenuRoot.activeSelf;

        /// <summary>
        /// true mientras el menú está abierto por una pausa de conexión VR (<see cref="OpenMenuWithNotice"/>), no por el jugador.
        /// Es el único punto de bloqueo de la entrada de juego: el menú principal y los pasos de lección lo consultan para
        /// ignorar lo que pase mientras dura la pausa.
        /// </summary>
        public bool IsBlockingGameplayInput => m_OpenedByConnectionPause && IsOpen;

        /// <summary>
        /// true tras <see cref="LockForTransition"/> mientras el <see cref="LessonSceneFlowManager"/> sigue en transición:
        /// <see cref="CloseMenu"/> y <see cref="ToggleMenu"/> no cierran el menú, así que la simulación sigue pausada durante el fade-out.
        /// El bloqueo depende de la transición: si esta termina (o no existe) y el menú sigue vivo, se libera solo y el menú vuelve a
        /// poder cerrarse. Se consulta bajo demanda (sin evento ni polling propio), no crea el flow manager.
        /// </summary>
        public bool IsLockedForTransition
        {
            get
            {
                if (!m_LockedForTransition) return false;
                if (LessonSceneFlowManager.TryGetInstance(out LessonSceneFlowManager flow) && flow.IsTransitioning) return true;

                m_LockedForTransition = false;
                SetResumeBlocked(m_ResumeBlocked);
                return false;
            }
        }

        /// <summary>true mientras Reanudar está bloqueado: <see cref="CloseMenu"/> y <see cref="ToggleMenu"/> no cierran el menú.</summary>
        public bool ResumeBlocked => m_ResumeBlocked;

        /// <summary>Etiqueta del aviso (null hasta que se muestra el primero).</summary>
        public TextMeshProUGUI NoticeLabel => m_NoticeLabel;

        /// <summary>true si el aviso de la pausa está visible.</summary>
        public bool IsNoticeVisible => m_NoticeLabel != null && m_NoticeLabel.gameObject.activeSelf;

        /// <summary>Botón Reanudar del panel principal (null si no existe).</summary>
        public Button ResumeButton
        {
            get
            {
                if (m_ResumeButton == null && m_MainPanel != null)
                {
                    Transform t = m_MainPanel.transform.Find("Button_Resume");
                    if (t != null) m_ResumeButton = t.GetComponent<Button>();
                }
                return m_ResumeButton;
            }
        }

        /// <summary>Botón "Calibrar posición" del panel principal (null si no existe). Si no está serializado se busca como MainPanel/Button_Calibrate.</summary>
        public GameObject CalibrateButton
        {
            get
            {
                if (m_CalibrateButton == null && m_MainPanel != null)
                {
                    Transform t = m_MainPanel.transform.Find("Button_Calibrate");
                    if (t != null) m_CalibrateButton = t.gameObject;
                }
                return m_CalibrateButton;
            }
        }

        /// <summary>true (por defecto) si el botón "Calibrar posición" del panel principal se muestra; false lo deja inactivo cada vez que el menú se abre.</summary>
        public bool ShowCalibrationButton => m_ShowCalibrationButton;

        /// <summary>true si el botón Menu del control y la tecla de teclado abren/cierran el menú.</summary>
        public bool MenuButtonEnabled => m_MenuButtonEnabled;

        /// <summary>Raíz del Canvas del menú (para ubicación en tests).</summary>
        public GameObject MenuRoot => m_MenuRoot;

        /// <summary>Evento disparado cuando el menú in-game se abre.</summary>
        public event System.Action MenuOpened;

        /// <summary>Evento disparado cuando el menú in-game se cierra.</summary>
        public event System.Action MenuClosed;

        /// <summary>
        /// Evento disparado en el momento en que cambia <see cref="IsBlockingGameplayInput"/> (true al abrir el menú por una pausa
        /// de conexión VR, false cuando termina). Permite a los menús que bloquean su entrada reaccionar de forma sincrónica, sin
        /// esperar a su próximo Update.
        /// </summary>
        public event System.Action<bool> GameplayInputBlockingChanged;

        /// <summary>
        /// Evento disparado cuando termina la pausa por conexión VR (el jugador reanudó, o <see cref="EndConnectionPause"/>), tras
        /// restaurar el modo en el que estaba el menú. Puede dispararse sin <see cref="MenuClosed"/> si el menú ya estaba abierto.
        /// </summary>
        public event System.Action ConnectionPauseEnded;

        /// <summary>true mientras el menú tiene la simulación detenida.</summary>
        public bool IsSimulationPaused => SimulationPause.Holds(this);

        // Resolución perezosa de la referencia de frente. Caché privado NO serializado: el campo m_ForwardReference queda solo como
        // override explícito del autor y resolver no modifica la escena.
        private Transform m_ResolvedVehicle;
        private bool m_NothingToResolve;
        private static int s_VehicleSearchCount;

        /// <summary>Cantidad de búsquedas de <see cref="VehicleController"/> hechas por todos los menús (solo para tests).</summary>
        public static int VehicleSearchCountForTests => s_VehicleSearchCount;

        /// <summary>
        /// Transform cuyo frente horizontal ordena la pose estándar del menú (el auto). Prioridad: el override serializado
        /// <c>m_ForwardReference</c>; el <see cref="VehicleController"/> de la escena del menú (cacheado mientras exista y esté activo, igual que lo ve la búsqueda); el asiento
        /// del <see cref="DriverSeatFollower"/> si es de la misma escena (se lee siempre del follower, así que acompaña a
        /// <c>SetDriverSeat</c> y, mientras el asiento sea la referencia, el auto se vuelve a buscar y se prefiere en cuanto aparece).
        /// Solo mira la escena del propio menú. Es perezosa (también funciona en Edit Mode, donde Awake no corre) y devuelve null
        /// real (nunca un objeto destruido) si no hay ninguno. Ese resultado se recuerda para no repetir la búsqueda en cada llamada
        /// (NewMainMenu) hasta que aparezca un asiento en la escena o el menú se vuelva a habilitar.
        /// </summary>
        public Transform ForwardReference
        {
            get
            {
                if (m_ForwardReference != null) return m_ForwardReference;
                if (m_ResolvedVehicle != null && m_ResolvedVehicle.gameObject.activeInHierarchy) return m_ResolvedVehicle;

                UnityEngine.SceneManagement.Scene scene = gameObject.scene;
                Transform seat = m_DriverSeatFollower != null ? m_DriverSeatFollower.DriverSeat : null;
                if (seat != null && seat.gameObject.scene != scene) seat = null;

                // Ya se buscó y no había nada: no se repite hasta que aparezca un asiento (o se re-habilite el menú).
                if (m_NothingToResolve && seat == null) return null;

                m_ResolvedVehicle = null;
                s_VehicleSearchCount++;
                foreach (VehicleController vehicle in Object.FindObjectsByType<VehicleController>())
                {
                    if (vehicle.gameObject.scene == scene)
                    {
                        m_ResolvedVehicle = vehicle.transform;
                        break;
                    }
                }

                if (m_ResolvedVehicle != null)
                {
                    m_NothingToResolve = false;
                    return m_ResolvedVehicle;
                }

                m_NothingToResolve = seat == null;
                return seat;
            }
        }

        private void Awake()
        {
            if (m_HeadTransform == null && Camera.main != null)
            {
                m_HeadTransform = Camera.main.transform;
            }

            if (m_DriverSeatFollower == null)
            {
                m_DriverSeatFollower = Object.FindAnyObjectByType<DriverSeatFollower>();
            }

            // Resuelve la referencia de frente (auto/asiento) de la escena del menú.
            _ = ForwardReference;

            if (m_MenuRoot != null)
            {
                MoveMenuToUILayer();
                m_MenuRoot.SetActive(false);
            }
        }

        /// <summary>
        /// Pasa toda la jerarquía del menú a la capa "UI". Mientras el menú está abierto, UIOnlyFarCasting
        /// restringe el raycastMask del rayo a esa capa, y el TrackedDeviceGraphicRaycaster descarta los
        /// gráficos que estén fuera de la máscara; los menús ya construidos en escenas quedaron en Default.
        /// </summary>
        private void MoveMenuToUILayer()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) return;

            foreach (Transform t in m_MenuRoot.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = uiLayer;
            }
        }

        private void OnEnable()
        {
            m_NothingToResolve = false;
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.CalibrationOffsetChanged += OnCalibrationOffsetChanged;
            }

            // Si se reactiva con la pausa por conexión todavía vigente, vuelve a bloquear la entrada.
            NotifyBlockingChanged();
        }

        private void OnDisable()
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.CalibrationOffsetChanged -= OnCalibrationOffsetChanged;
            }

            // Un menú desactivado no puede seguir bloqueando la entrada de nadie (aunque la pausa siga marcada como vigente).
            ReleaseBlockingNotification();

            // Si el menú se destruye o desactiva abierto (cambio de escena), la simulación no puede quedar congelada.
            SetSimulationPaused(false);
        }

        private void OnDestroy()
        {
            // Si el menú desaparece con la pausa vigente, quienes bloquean su entrada por el evento no pueden quedar bloqueados.
            ReleaseBlockingNotification();
        }

        private bool m_LastBlocking;

        // Notifica false si la última notificación fue true. Idempotente: OnDisable y OnDestroy no duplican el evento.
        private void ReleaseBlockingNotification()
        {
            if (!m_LastBlocking) return;

            m_LastBlocking = false;
            GameplayInputBlockingChanged?.Invoke(false);
        }

        // Dispara GameplayInputBlockingChanged solo cuando IsBlockingGameplayInput cambió desde la última notificación.
        private void NotifyBlockingChanged()
        {
            bool blocking = IsBlockingGameplayInput;
            if (blocking == m_LastBlocking) return;

            m_LastBlocking = blocking;
            GameplayInputBlockingChanged?.Invoke(blocking);
        }

        /// <summary>
        /// Traslada el menú por el mismo desplazamiento de mundo que sufrió la vista al calibrar,
        /// para que quede siempre al alcance sin quedar pegado a la cabeza (evita mareos).
        /// </summary>
        private void OnCalibrationOffsetChanged(Vector3 worldDelta)
        {
            if (m_MenuRoot != null)
            {
                m_MenuRoot.transform.position += worldDelta;
            }
        }

        private void Update()
        {
            // Cubre que algo ajeno cierre o abra MenuRoot (SetActive) con la pausa vigente: el estado de bloqueo se re-evalúa cada
            // frame (una comparación de bools; solo dispara el evento si cambió).
            NotifyBlockingChanged();

            if (!m_MenuButtonEnabled) return;

            if (Keyboard.current != null && Keyboard.current[m_ToggleMenuKey].wasPressedThisFrame)
            {
                ToggleMenu();
            }

            CheckMenuButtonPress();
        }

        /// <summary>
        /// Sondea el botón Menu físico del control VR vía la API legada de XR (ver
        /// <see cref="XRButtonEdgeDetector"/>) y dispara el toggle solo en el flanco de subida
        /// (al apretar, no mientras se mantiene apretado).
        /// </summary>
        private void CheckMenuButtonPress()
        {
            if (m_MenuButtonEdge.Poll(m_MenuButtonNode, XRCommonUsages.menuButton))
            {
                ToggleMenu();
            }
        }

        /// <summary>
        /// Abre el menú si está cerrado, o lo cierra si está abierto.
        /// </summary>
        public void ToggleMenu()
        {
            if (IsLockedForTransition) return;

            if (IsOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        /// <summary>
        /// Abre el menú, ubicándolo frente a la cabeza del usuario, y muestra el panel principal.
        /// </summary>
        public void OpenMenu()
        {
            if (m_MenuRoot == null) return;

            PositionAtStandardPose();
            m_MenuRoot.SetActive(true);
            m_StandaloneOpen = false;
            ShowMainPanel();
            SetFarCastingForMenu(true);
            SetSimulationPaused(true);
            MenuOpened?.Invoke();
            NotifyBlockingChanged();
        }

        /// <summary>
        /// Cierra el menú ("Reanudar") y reanuda la simulación.
        /// </summary>
        public void CloseMenu()
        {
            if (m_MenuRoot == null) return;

            // Transición de escena en curso: el menú queda abierto y la simulación pausada hasta que se descargue la escena.
            if (IsLockedForTransition) return;

            // Pausa por desconexión VR sin recuperar: no hay reanudación (ni por Reanudar, ni por el botón Menu o Escape).
            if (m_ResumeBlocked) return;

            // Pausa por conexión recuperada: se vuelve al modo previo (cerrado, menú del jugador o calibración standalone).
            if (m_OpenedByConnectionPause)
            {
                RestorePreviousMode();
                return;
            }

            m_StandaloneOpen = false;
            m_MenuRoot.SetActive(false);
            SetFarCastingForMenu(false);
            SetSimulationPaused(false);
            if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(true);
            MenuClosed?.Invoke();
            NotifyBlockingChanged();
        }

        /// <summary>
        /// Bloquea el cierre del menú durante la transición de escena (reiniciar o salir de la lección): ni Menu/Escape ni Reanudar
        /// despausan la simulación mientras dura el fade-out. Se llama con la transición ya iniciada; la pausa la libera <c>OnDisable</c>
        /// al descargarse la escena, y si la transición termina sin destruir este menú el bloqueo se libera solo
        /// (<see cref="IsLockedForTransition"/>). <see cref="OpenMenuWithNotice"/> sigue funcionando.
        /// </summary>
        public void LockForTransition()
        {
            m_LockedForTransition = true;
            SetResumeBlocked(m_ResumeBlocked);
        }

        /// <summary>
        /// Abre el menú normal (panel principal, pausa de tiempo y audio) y muestra <paramref name="notice"/> debajo del título.
        /// Lo usa <see cref="VRHardwareStateMonitor"/> al pausar por una desconexión VR. La primera vez guarda el modo en el que
        /// estaba el menú (cerrado, menú del jugador con su panel, o calibración standalone) para restaurarlo al reanudar. La
        /// pausa de tiempo y audio se fuerza aunque <c>m_PauseSimulation</c> esté apagado. Es idempotente: si ya fue tomado por la
        /// pausa no se reabre ni se duplica el aviso; solo se actualiza (y se reaplica la pausa si algo la levantó).
        /// </summary>
        public void OpenMenuWithNotice(string notice)
        {
            if (m_MenuRoot == null) return;

            bool wasOpen = IsOpen;
            bool first = !m_OpenedByConnectionPause;
            if (first) CapturePreviousMode(wasOpen);
            m_OpenedByConnectionPause = true;

            // Cerrado o calibración standalone: se ubica en la pose estándar. El menú del jugador se deja donde está.
            if (!wasOpen || (first && m_PreviousMode != PreviousMenuMode.Normal)) PositionAtStandardPose();
            m_MenuRoot.SetActive(true);
            m_StandaloneOpen = false;
            // El aviso vive en el panel principal: se muestra una sola vez y después solo se actualiza el texto.
            if (!wasOpen || first)
            {
                ShowMainPanel();
                if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(true);
            }

            SetFarCastingForMenu(true);
            SetSimulationPaused(true, true);
            SetNotice(notice);
            if (!wasOpen) MenuOpened?.Invoke();
            NotifyBlockingChanged();
        }

        /// <summary>
        /// Vuelve a ubicar el menú de la pausa por conexión en la pose estándar (ver <see cref="PositionAtStandardPose"/>). Al
        /// recuperarse la conexión la cabeza puede estar en otro lado (visor apoyado en la mesa), así que Reanudar tiene que quedar
        /// al alcance de nuevo.
        /// </summary>
        public void RecenterConnectionPauseMenu()
        {
            if (IsBlockingGameplayInput) PositionAtStandardPose();
        }

        /// <summary>
        /// Termina la pausa por conexión sin esperar al jugador (el monitor se apaga): desbloquea Reanudar, limpia el aviso y
        /// restaura el modo previo. Sin pausa vigente solo desbloquea.
        /// </summary>
        public void EndConnectionPause()
        {
            SetResumeBlocked(false);
            if (m_OpenedByConnectionPause && m_MenuRoot != null) RestorePreviousMode();
        }

        private void CapturePreviousMode(bool wasOpen)
        {
            m_PreviousMode = !wasOpen ? PreviousMenuMode.Closed : m_StandaloneOpen ? PreviousMenuMode.Standalone : PreviousMenuMode.Normal;
            m_PreviousCalibrationPanelActive = m_CalibrationPanel != null && m_CalibrationPanel.activeSelf;
            m_PreviousBackButtonActive = m_CalibrationBackButton == null || m_CalibrationBackButton.activeSelf;
        }

        // Quita el aviso y el bloqueo y devuelve el menú a como estaba antes de la pausa por conexión.
        private void RestorePreviousMode()
        {
            m_OpenedByConnectionPause = false;
            SetResumeBlocked(false);
            if (m_NoticeLabel != null) m_NoticeLabel.gameObject.SetActive(false);

            switch (m_PreviousMode)
            {
                case PreviousMenuMode.Closed:
                    // Con la transición de escena en curso el menú no se cierra: queda abierto y pausado hasta que se descargue la escena.
                    if (IsLockedForTransition)
                    {
                        ShowMainPanel();
                        break;
                    }

                    m_MenuRoot.SetActive(false);
                    SetFarCastingForMenu(false);
                    SetSimulationPaused(false);
                    if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(true);
                    MenuClosed?.Invoke();
                    break;

                case PreviousMenuMode.Normal:
                    // El menú del jugador sigue abierto, en el panel en el que estaba y con el tiempo pausado como antes.
                    if (m_PreviousCalibrationPanelActive) ShowCalibrationPanel();
                    else ShowMainPanel();
                    if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(m_PreviousBackButtonActive);
                    if (!m_PauseSimulation && !IsLockedForTransition) SetSimulationPaused(false);
                    break;

                case PreviousMenuMode.Standalone:
                    // La calibración standalone no pausa el tiempo: se reabre reubicada y se libera la pausa.
                    if (!IsLockedForTransition) SetSimulationPaused(false);
                    PositionInFrontOfHead(m_YawOffsetDegrees, ForwardReference);
                    ShowCalibrationPanel();
                    if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(false);
                    m_StandaloneOpen = true;
                    break;
            }

            NotifyBlockingChanged();
            ConnectionPauseEnded?.Invoke();
        }

        /// <summary>
        /// Bloquea o desbloquea Reanudar: bloqueado, el botón no es interactuable y <see cref="CloseMenu"/> no cierra el menú.
        /// </summary>
        public void SetResumeBlocked(bool blocked)
        {
            m_ResumeBlocked = blocked;
            Button resume = ResumeButton;
            if (resume != null) resume.interactable = !blocked && !m_LockedForTransition;
        }

        /// <summary>Muestra (o actualiza) el aviso debajo del título del panel principal. Con texto vacío lo oculta.</summary>
        private void SetNotice(string notice)
        {
            if (string.IsNullOrEmpty(notice))
            {
                if (m_NoticeLabel != null) m_NoticeLabel.gameObject.SetActive(false);
                return;
            }

            EnsureNoticeLabel();
            if (m_NoticeLabel == null) return;
            m_NoticeLabel.text = notice;
            m_NoticeLabel.gameObject.SetActive(true);
        }

        /// <summary>
        /// Crea el aviso la primera vez: hijo de MainPanel, justo debajo de Title, con la fuente y el color del título pero más
        /// chico. Ocupa el hueco entre el título y el primer botón.
        /// </summary>
        private void EnsureNoticeLabel()
        {
            if (m_NoticeLabel != null || m_MainPanel == null) return;

            Transform titleTransform = m_MainPanel.transform.Find("Title");
            TextMeshProUGUI title = titleTransform != null ? titleTransform.GetComponent<TextMeshProUGUI>() : null;
            RectTransform titleRect = titleTransform as RectTransform;

            var go = new GameObject("Notice", typeof(RectTransform));
            go.layer = m_MainPanel.layer;
            go.transform.SetParent(m_MainPanel.transform, false);
            if (titleTransform != null) go.transform.SetSiblingIndex(titleTransform.GetSiblingIndex() + 1);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            // Hueco entre el borde inferior del título y el borde superior de Reanudar (mismo espacio de anclas: arriba al centro).
            float top = titleRect != null ? titleRect.anchoredPosition.y - titleRect.sizeDelta.y * 0.5f - 4f : -70f;
            float bottom = top - 56f;
            Transform resumeTransform = m_MainPanel.transform.Find("Button_Resume");
            if (resumeTransform is RectTransform resumeRect)
            {
                bottom = resumeRect.anchoredPosition.y + resumeRect.sizeDelta.y * 0.5f + 4f;
            }
            rect.anchoredPosition = new Vector2(0f, top);
            rect.sizeDelta = new Vector2(titleRect != null ? Mathf.Max(titleRect.sizeDelta.x, 520f) : 520f, Mathf.Max(top - bottom, 40f));

            m_NoticeLabel = go.AddComponent<TextMeshProUGUI>();
            float baseSize = title != null ? title.fontSize * 0.5f : 18f;
            if (title != null)
            {
                m_NoticeLabel.font = title.font;
                m_NoticeLabel.fontSharedMaterial = title.fontSharedMaterial;
                m_NoticeLabel.color = title.color;
            }
            else
            {
                m_NoticeLabel.color = Color.white;
            }
            m_NoticeLabel.fontSize = Mathf.Clamp(baseSize, 16f, 22f);
            m_NoticeLabel.enableAutoSizing = true;
            m_NoticeLabel.fontSizeMax = m_NoticeLabel.fontSize;
            m_NoticeLabel.fontSizeMin = 12f;
            m_NoticeLabel.alignment = TextAlignmentOptions.Center;
            m_NoticeLabel.textWrappingMode = TextWrappingModes.Normal;
            m_NoticeLabel.raycastTarget = false;
        }

        /// <summary>
        /// Abre solo el panel de calibración, sin pausar el tiempo ni el audio y sin botón Volver. Lo ubica a
        /// <c>m_YawOffsetDegrees</c> (45°) a la derecha del frente horizontal del auto
        /// (<see cref="ForwardReference"/>; sin referencia, de la cabeza), medido desde los ojos, a
        /// <c>m_DistanceFromHead</c>, a la altura de los ojos y mirando a la cabeza. Acompaña al jugador al calibrar
        /// (<see cref="DriverSeatFollower.CalibrationOffsetChanged"/>). Se cierra con <see cref="CloseMenu"/>.
        /// Lo usa el menú principal en cabina (página Calibración).
        /// </summary>
        public void OpenCalibrationStandalone()
        {
            if (m_MenuRoot == null) return;

            PositionInFrontOfHead(m_YawOffsetDegrees, ForwardReference);
            m_MenuRoot.SetActive(true);
            m_StandaloneOpen = true;
            ShowCalibrationPanel();
            if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(false);
            SetFarCastingForMenu(true);
            MenuOpened?.Invoke();
            NotifyBlockingChanged();
        }

        /// <summary>
        /// Detiene o reanuda la simulación a través de <see cref="SimulationPause"/>, que guarda la escala de tiempo y el
        /// estado del audio previos y los restaura tal cual (no asume que el juego corría a escala 1) aunque haya otro dueño
        /// de la pausa solapado (la pausa por desconexión VR).
        /// </summary>
        private void SetSimulationPaused(bool paused, bool force = false)
        {
            if (paused)
            {
                // La pausa por conexión congela siempre, aunque la escena tenga m_PauseSimulation apagado. Repetirlo reafirma
                // el congelamiento si algo pisó Time.timeScale, sin perder los valores previos que guarda SimulationPause.
                if (!m_PauseSimulation && !force) return;
                SimulationPause.Acquire(this, m_PauseAudio || force);
            }
            else
            {
                SimulationPause.Release(this);
            }
        }

        /// <summary>
        /// Habilita el cast a distancia de los Near-Far Interactors mientras el menú está abierto
        /// (para poder usar el rayo con los sliders sin acercarse) y lo libera al cerrarlo. El rayo queda
        /// restringido a la capa UI y el estado original lo restaura <see cref="UIOnlyFarCasting"/>
        /// cuando ya no queda ningún solicitante (p. ej. el HUD de una lección).
        /// </summary>
        private void SetFarCastingForMenu(bool enableForMenu)
        {
            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0) return;

            for (int i = 0; i < m_FarCastInteractors.Length; i++)
            {
                if (enableForMenu) UIOnlyFarCasting.Request(m_FarCastInteractors[i], this);
                else UIOnlyFarCasting.Release(m_FarCastInteractors[i], this);
            }
        }

        /// <summary>
        /// Muestra el panel principal y oculta el de calibración.
        /// </summary>
        public void ShowMainPanel()
        {
            if (m_MainPanel != null)
            {
                m_MainPanel.SetActive(true);
                // Todos los caminos que muestran el panel principal (OpenMenu, OpenMenuWithNotice, restaurar el modo previo) pasan por acá.
                GameObject calibrateButton = CalibrateButton;
                if (calibrateButton != null) calibrateButton.SetActive(m_ShowCalibrationButton);
            }
            if (m_CalibrationPanel != null) m_CalibrationPanel.SetActive(false);
        }

        /// <summary>
        /// Muestra el panel de calibración ("Calibrar posición") y oculta el principal.
        /// </summary>
        public void ShowCalibrationPanel()
        {
            if (m_MainPanel != null) m_MainPanel.SetActive(false);
            if (m_CalibrationPanel != null) m_CalibrationPanel.SetActive(true);
        }

        /// <summary>
        /// Pose estándar del menú de pausa y del aviso de desconexión. Con referencia de frente (el auto): a
        /// <c>m_YawOffsetDegrees</c> (45°) a la derecha de su frente horizontal actual, medido desde la posición actual de los ojos,
        /// a <c>m_DistanceFromHead</c>, a la altura de los ojos y mirando a la cabeza; la orientación de la cabeza se ignora. Sin
        /// referencia (NewMainMenu): frente horizontal de la cabeza, a 0°.
        /// </summary>
        private void PositionAtStandardPose()
        {
            Transform reference = ForwardReference;
            if (reference != null) PositionInFrontOfHead(m_YawOffsetDegrees, reference);
            else PositionInFrontOfHead();
        }

        private void PositionInFrontOfHead(float yawOffsetDegrees = 0f, Transform forwardReference = null)
        {
            if (m_HeadTransform == null)
            {
                if (Camera.main != null) m_HeadTransform = Camera.main.transform;
                if (m_HeadTransform == null) return;
            }

            // El frente horizontal sale de la referencia (auto/asiento) si la hay; si no, de la cabeza.
            Transform frontSource = forwardReference != null ? forwardReference : m_HeadTransform;
            Vector3 forward = frontSource.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = m_HeadTransform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = m_HeadTransform.up;
            }
            forward.Normalize();
            if (yawOffsetDegrees != 0f) forward = Quaternion.AngleAxis(yawOffsetDegrees, Vector3.up) * forward;

            Vector3 targetPosition = m_HeadTransform.position + forward * m_DistanceFromHead;
            m_MenuRoot.transform.position = targetPosition;
            m_MenuRoot.transform.rotation = Quaternion.LookRotation(targetPosition - m_HeadTransform.position, Vector3.up);
        }
    }
}
