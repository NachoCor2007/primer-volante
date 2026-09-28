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
    /// frente a la cabeza del usuario. No pausa el juego (no modifica Time.timeScale).
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

        [Header("Posicionamiento")]
        [Tooltip("Transform de la cabeza del usuario (cámara del XR Origin). Si se deja vacío, se usa Camera.main.")]
        [SerializeField] private Transform m_HeadTransform;

        [Tooltip("Distancia a la que se ubica el menú frente a la cabeza al abrirse (metros).")]
        [SerializeField] private float m_DistanceFromHead = 0.8f;

        [Header("Entrada (VR - Botón Menu físico)")]
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
                 "Normalmente el juego lo mantiene apagado para no interferir con agarrar controles.")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        private bool m_MenuButtonWasPressed = false;
        private bool[] m_FarCastPreviousStates;

        public bool IsOpen => m_MenuRoot != null && m_MenuRoot.activeSelf;

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

            if (m_MenuRoot != null)
            {
                m_MenuRoot.SetActive(false);
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
        }

        /// <summary>
        /// Cierra el menú ("Reanudar"). El juego continúa corriendo mientras el menú estaba abierto.
        /// </summary>
        public void CloseMenu()
        {
            if (m_MenuRoot == null) return;

            m_MenuRoot.SetActive(false);
            SetFarCastingForMenu(false);
        }

        /// <summary>
        /// Habilita el cast a distancia de los Near-Far Interactors mientras el menú está abierto
        /// (para poder usar el rayo con los sliders sin acercarse), y restaura el estado previo
        /// de cada uno al cerrarlo.
        /// </summary>
        private void SetFarCastingForMenu(bool enableForMenu)
        {
            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0) return;

            if (enableForMenu)
            {
                m_FarCastPreviousStates = new bool[m_FarCastInteractors.Length];
                for (int i = 0; i < m_FarCastInteractors.Length; i++)
                {
                    if (m_FarCastInteractors[i] == null) continue;
                    m_FarCastPreviousStates[i] = m_FarCastInteractors[i].enableFarCasting;
                    m_FarCastInteractors[i].enableFarCasting = true;
                }
            }
            else if (m_FarCastPreviousStates != null)
            {
                for (int i = 0; i < m_FarCastInteractors.Length; i++)
                {
                    if (m_FarCastInteractors[i] == null) continue;
                    m_FarCastInteractors[i].enableFarCasting = m_FarCastPreviousStates[i];
                }
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

        private void PositionInFrontOfHead()
        {
            if (m_HeadTransform == null)
            {
                if (Camera.main != null) m_HeadTransform = Camera.main.transform;
                if (m_HeadTransform == null) return;
            }

            Vector3 forward = m_HeadTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = m_HeadTransform.up;
            }
            forward.Normalize();

            Vector3 targetPosition = m_HeadTransform.position + forward * m_DistanceFromHead;
            m_MenuRoot.transform.position = targetPosition;
            m_MenuRoot.transform.rotation = Quaternion.LookRotation(targetPosition - m_HeadTransform.position, Vector3.up);
        }
    }
}
