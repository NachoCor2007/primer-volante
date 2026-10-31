using UnityEngine;
using UnityEngine.InputSystem;
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

        private bool m_IsSimulationPaused;
        private bool m_PausedAudio;
        private float m_TimeScaleBeforePause = 1f;
        private bool m_AudioPauseBeforePause;

        public bool IsOpen => m_MenuRoot != null && m_MenuRoot.activeSelf;

        /// <summary>true si el botón Menu del control y la tecla de teclado abren/cierran el menú.</summary>
        public bool MenuButtonEnabled => m_MenuButtonEnabled;

        /// <summary>Raíz del Canvas del menú (para ubicación en tests).</summary>
        public GameObject MenuRoot => m_MenuRoot;

        /// <summary>Evento disparado cuando el menú in-game se abre.</summary>
        public event System.Action MenuOpened;

        /// <summary>Evento disparado cuando el menú in-game se cierra.</summary>
        public event System.Action MenuClosed;

        /// <summary>true mientras el menú tiene la simulación detenida.</summary>
        public bool IsSimulationPaused => m_IsSimulationPaused;

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

            m_MenuRoot.SetActive(false);
            SetFarCastingForMenu(false);
            SetSimulationPaused(false);
            if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(true);
            MenuClosed?.Invoke();
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
            ShowCalibrationPanel();
            if (m_CalibrationBackButton != null) m_CalibrationBackButton.SetActive(false);
            SetFarCastingForMenu(true);
            MenuOpened?.Invoke();
        }

        /// <summary>
        /// Detiene o reanuda la simulación. Guarda la escala de tiempo y el estado del audio previos para
        /// restaurarlos tal cual estaban (no asume que el juego corría a escala 1).
        /// </summary>
        private void SetSimulationPaused(bool paused)
        {
            if (paused == m_IsSimulationPaused) return;

            if (paused)
            {
                if (!m_PauseSimulation) return;

                m_TimeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;

                m_PausedAudio = m_PauseAudio;
                if (m_PausedAudio)
                {
                    m_AudioPauseBeforePause = AudioListener.pause;
                    AudioListener.pause = true;
                }
            }
            else
            {
                Time.timeScale = m_TimeScaleBeforePause;
                if (m_PausedAudio) AudioListener.pause = m_AudioPauseBeforePause;
                m_PausedAudio = false;
            }

            m_IsSimulationPaused = paused;
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
