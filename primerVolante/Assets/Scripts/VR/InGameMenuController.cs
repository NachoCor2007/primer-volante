using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using XRNode = UnityEngine.XR.XRNode;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
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

        [Tooltip("Botón Volver del panel de calibración. Se oculta cuando la calibración se abre standalone " +
                 "(OpenCalibrationStandalone), porque no hay panel principal al que volver.")]
        [SerializeField] private GameObject m_CalibrationBackButton;

        [Header("Posicionamiento")]
        [Tooltip("Transform de la cabeza del usuario (cámara del XR Origin). Si se deja vacío, se usa Camera.main.")]
        [SerializeField] private Transform m_HeadTransform;

        [Tooltip("Distancia a la que se ubica el menú frente a la cabeza al abrirse (metros).")]
        [SerializeField] private float m_DistanceFromHead = 0.8f;

        [Tooltip("Ángulo horizontal, a la derecha del frente de la cabeza, al que se ubica el panel de calibración " +
                 "abierto standalone (grados).")]
        [SerializeField] private float m_StandaloneYawOffsetDegrees = 45f;

        [Tooltip("Transform cuyo frente horizontal es la referencia de los 45° del panel de calibración standalone " +
                 "(el vehículo o el asiento): el panel queda siempre al noreste del auto, sin importar hacia dónde mire la cabeza. " +
                 "Si se deja vacío se autodetecta el VehicleController (o el asiento del DriverSeatFollower); si no hay ninguno, " +
                 "cae al comportamiento anterior (45° a la derecha del frente de la cabeza).")]
        [SerializeField] private Transform m_StandaloneForwardReference;

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

        private bool m_MenuButtonWasPressed = false;


        // Aviso de la pausa por desconexión VR (TextMeshProUGUI creado por código, hijo de MainPanel) y bloqueo de Reanudar.
        private TextMeshProUGUI m_NoticeLabel;
        private Button m_ResumeButton;
        private bool m_ResumeBlocked;
        private bool m_OpenedByConnectionPause;

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

        /// <summary>true si el botón Menu del control y la tecla de teclado abren/cierran el menú.</summary>
        public bool MenuButtonEnabled => m_MenuButtonEnabled;

        /// <summary>Raíz del Canvas del menú (para ubicación en tests).</summary>
        public GameObject MenuRoot => m_MenuRoot;

        /// <summary>Evento disparado cuando el menú in-game se abre.</summary>
        public event System.Action MenuOpened;

        /// <summary>Evento disparado cuando el menú in-game se cierra.</summary>
        public event System.Action MenuClosed;

        /// <summary>
        /// Evento disparado cuando termina la pausa por conexión VR (el jugador reanudó, o <see cref="EndConnectionPause"/>), tras
        /// restaurar el modo en el que estaba el menú. Puede dispararse sin <see cref="MenuClosed"/> si el menú ya estaba abierto.
        /// </summary>
        public event System.Action ConnectionPauseEnded;

        /// <summary>true mientras el menú tiene la simulación detenida.</summary>
        public bool IsSimulationPaused => SimulationPause.Holds(this);

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

            if (m_StandaloneForwardReference == null)
            {
                VehicleController vehicle = Object.FindAnyObjectByType<VehicleController>();
                if (vehicle != null) m_StandaloneForwardReference = vehicle.transform;
                else if (m_DriverSeatFollower != null) m_StandaloneForwardReference = m_DriverSeatFollower.DriverSeat;
            }

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
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.CalibrationOffsetChanged += OnCalibrationOffsetChanged;
            }
        }

        private void OnDisable()
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.CalibrationOffsetChanged -= OnCalibrationOffsetChanged;
            }

            // Si el menú se destruye o desactiva abierto (cambio de escena), la simulación no puede quedar congelada.
            SetSimulationPaused(false);
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
            if (!m_MenuButtonEnabled) return;

            if (Keyboard.current != null && Keyboard.current[m_ToggleMenuKey].wasPressedThisFrame)
            {
                ToggleMenu();
            }

            CheckMenuButtonPress();
        }

        /// <summary>
        /// Sondea el botón Menu físico del control VR vía la API legada de XR (la única que, en
        /// este proyecto, refleja en vivo el estado de los controles OpenXR) y dispara el toggle
        /// solo en el flanco de subida (al apretar, no mientras se mantiene apretado).
        /// </summary>
        private void CheckMenuButtonPress()
        {
            XRInputDevice device = XRInputDevices.GetDeviceAtXRNode(m_MenuButtonNode);
            bool pressed = false;
            if (device.isValid && device.TryGetFeatureValue(XRCommonUsages.menuButton, out pressed))
            {
                if (pressed && !m_MenuButtonWasPressed)
                {
                    ToggleMenu();
                }
                m_MenuButtonWasPressed = pressed;
            }
        }

        /// <summary>
        /// Abre el menú si está cerrado, o lo cierra si está abierto.
        /// </summary>
        public void ToggleMenu()
        {
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

            PositionInFrontOfHead();
            m_MenuRoot.SetActive(true);
            m_StandaloneOpen = false;
            ShowMainPanel();
            SetFarCastingForMenu(true);
            SetSimulationPaused(true);
            MenuOpened?.Invoke();
        }

        /// <summary>
        /// Cierra el menú ("Reanudar") y reanuda la simulación.
        /// </summary>
        public void CloseMenu()
        {
            if (m_MenuRoot == null) return;

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

            // Cerrado o calibración standalone: se ubica frente a la cabeza. El menú del jugador se deja donde está.
            if (!wasOpen || (first && m_PreviousMode != PreviousMenuMode.Normal)) PositionInFrontOfHead();
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
        }

        /// <summary>
        /// Vuelve a ubicar el menú de la pausa por conexión frente a la cabeza. Al recuperarse la conexión la cabeza puede estar
        /// en otro lado (visor apoyado en la mesa), así que Reanudar tiene que quedar al alcance de nuevo.
        /// </summary>
        public void RecenterConnectionPauseMenu()
        {
            if (IsBlockingGameplayInput) PositionInFrontOfHead();
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
                    if (!m_PauseSimulation) SetSimulationPaused(false);
                    break;

                case PreviousMenuMode.Standalone:
                    // La calibración standalone no pausa el tiempo: se reabre reubicada y se libera la pausa.
                    SetSimulationPaused(false);
                    PositionInFrontOfHead(m_StandaloneYawOffsetDegrees, m_StandaloneForwardReference);
                    ShowCalibrationPanel();
                    if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(false);
                    m_StandaloneOpen = true;
                    break;
            }

            ConnectionPauseEnded?.Invoke();
        }

        /// <summary>
        /// Bloquea o desbloquea Reanudar: bloqueado, el botón no es interactuable y <see cref="CloseMenu"/> no cierra el menú.
        /// </summary>
        public void SetResumeBlocked(bool blocked)
        {
            m_ResumeBlocked = blocked;
            Button resume = ResumeButton;
            if (resume != null) resume.interactable = !blocked;
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
        /// <c>m_StandaloneYawOffsetDegrees</c> (45°) a la derecha del frente horizontal del auto
        /// (<c>m_StandaloneForwardReference</c>; sin referencia, de la cabeza), medido desde los ojos, a
        /// <c>m_DistanceFromHead</c>, a la altura de los ojos y mirando a la cabeza. Acompaña al jugador al calibrar
        /// (<see cref="DriverSeatFollower.CalibrationOffsetChanged"/>). Se cierra con <see cref="CloseMenu"/>.
        /// Lo usa el menú principal en cabina (página Calibración).
        /// </summary>
        public void OpenCalibrationStandalone()
        {
            if (m_MenuRoot == null) return;

            PositionInFrontOfHead(m_StandaloneYawOffsetDegrees, m_StandaloneForwardReference);
            m_MenuRoot.SetActive(true);
            m_StandaloneOpen = true;
            ShowCalibrationPanel();
            if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(false);
            SetFarCastingForMenu(true);
            MenuOpened?.Invoke();
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
            if (m_MainPanel != null) m_MainPanel.SetActive(true);
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
