using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PrimerVolante.Lessons;
using PrimerVolante.VR;
using XRNode = UnityEngine.XR.XRNode;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace PrimerVolante.MainMenu
{
    public enum NewMenuTab
    {
        Controles = 0,
        Lecciones = 1,
        Circuitos = 2,
        Calibracion = 3,
        Creditos = 4
    }

    [Serializable]
    public class CircuitDefinition
    {
        public string circuitId;
        public string title;
        [TextArea(2, 4)]
        public string description;
        public string sceneName;
    }

    /// <summary>
    /// Controlador del Nuevo Menú Principal en Realidad Virtual ("NewMainMenu").
    /// Ofrece una experiencia ortodoxa, accesible, estática y sin desorden visual en World Space:
    /// - Navegación lateral por solapas (Controles, Lecciones, Circuitos, Calibración, Créditos).
    /// - Módulo de controles con ilustraciones 3D interactivas del mando VR.
    /// - Selección clara de lecciones y circuitos con resumen legible y botón verde "Comenzar Lección" / "Iniciar Recorrido".
    /// - Modal de confirmación de salida limpio y centrado.
    /// - Doble soporte de interacción VR: XRI TrackedDeviceGraphicRaycaster estándar + sondeo directo
    ///   de hardware (XRInputDevices.trigger / primaryButton) con UIOnlyFarCasting para garantizar 100%
    ///   la interactividad del gatillo en visores Meta Quest / OpenXR.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public class NewMainMenuController : MonoBehaviour
    {
        [Header("Navegación Lateral")]
        [SerializeField] private Button m_TabBtnControls;
        [SerializeField] private Button m_TabBtnLessons;
        [SerializeField] private Button m_TabBtnCircuits;
        [SerializeField] private Button m_TabBtnCalibration;
        [SerializeField] private Button m_TabBtnCredits;
        [SerializeField] private Button m_BtnExit;

        [Header("Colores de Solapas")]
        [SerializeField] private Color m_ActiveTabColor = new Color(0.15f, 0.42f, 0.90f, 1f);   // Azul #266be6
        [SerializeField] private Color m_InactiveTabColor = new Color(0.10f, 0.12f, 0.18f, 1f); // Slate oscuro #1a1f2e
        [SerializeField] private Color m_ActiveTextColor = Color.white;
        [SerializeField] private Color m_InactiveTextColor = new Color(0.70f, 0.75f, 0.85f, 1f);

        [Header("Paneles de Contenido")]
        [SerializeField] private GameObject m_PanelControls;
        [SerializeField] private GameObject m_PanelLessons;
        [SerializeField] private GameObject m_PanelCircuits;
        [SerializeField] private GameObject m_PanelCalibration;
        [SerializeField] private GameObject m_PanelCredits;

        [Header("Sección Lecciones")]
        [SerializeField] private List<LessonDefinitionSO> m_Lessons = new List<LessonDefinitionSO>();
        [SerializeField] private Transform m_LessonItemsContainer;
        [SerializeField] private TextMeshProUGUI m_LessonDetailTitle;
        [SerializeField] private TextMeshProUGUI m_LessonDetailDescription;
        [SerializeField] private Button m_BtnStartLesson;
        [SerializeField] private TextMeshProUGUI m_BtnStartLessonText;

        [Header("Sección Circuitos / Integradores")]
        [SerializeField] private List<CircuitDefinition> m_Circuits = new List<CircuitDefinition>();
        [SerializeField] private Transform m_CircuitItemsContainer;
        [SerializeField] private TextMeshProUGUI m_CircuitDetailTitle;
        [SerializeField] private TextMeshProUGUI m_CircuitDetailDescription;
        [SerializeField] private Button m_BtnStartCircuit;
        [SerializeField] private TextMeshProUGUI m_BtnStartCircuitText;

        [Header("Modal de Salida")]
        [SerializeField] private GameObject m_ExitModal;
        [SerializeField] private Button m_BtnConfirmExit;
        [SerializeField] private Button m_BtnCancelExit;

        [Header("Interactores VR (Rayo acotado a la UI)")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        [Header("Anclaje Estático del Jugador")]
        [Tooltip("XR Origin del menú. Si tiene DriverSeatFollower (con MenuViewpoint como asiento), el follower lo coloca y " +
                 "compensa la pose de la cabeza; si no, este controlador lo fija en (0, 0, 0).")]
        [SerializeField] private Transform m_XROriginTransform;

        [Header("Pausa por desconexión VR")]
        [Tooltip("Menú de pausa de la escena. Mientras está abierto por una desconexión VR (IsBlockingGameplayInput) este menú " +
                 "ignora clics, gatillo y Escape: el CanvasGroup de la raíz del canvas queda no interactuable. " +
                 "Si se deja vacío se busca en Awake.")]
        [SerializeField] private InGameMenuController m_InGameMenu;

        // Estado interno
        // Follower del XR Origin (se cachea al resolver m_XROriginTransform); null = el controlador fija el rig en cero.
        private DriverSeatFollower m_SeatFollower;
        private NewMenuTab m_CurrentTab = NewMenuTab.Controles;
        private int m_SelectedLessonIndex = 0;
        private int m_SelectedCircuitIndex = 0;
        private readonly List<Button> m_LessonCardButtons = new List<Button>();
        private readonly List<Button> m_CircuitCardButtons = new List<Button>();

        // Estado de hardware para fallback de gatillo / botones
        private bool m_RightTriggerWasPressed = false;
        private bool m_LeftTriggerWasPressed = false;
        private float m_LastHardwareClickTime = 0f;

        // Bloqueo de entrada por la pausa de conexión VR (ver ApplyInputBlock).
        private CanvasGroup m_InteractionGroup;
        private bool m_BlockApplied;
        private bool m_InteractableBeforeBlock = true;
        private bool m_BlocksRaycastsBeforeBlock = true;

        /// <summary>
        /// true mientras el menú de pausa está abierto por una desconexión VR. Es el único punto de bloqueo de la entrada de
        /// este menú: no conoce al monitor, solo consulta <see cref="InGameMenuController.IsBlockingGameplayInput"/>.
        /// </summary>
        public bool InputBlocked => m_InGameMenu != null && m_InGameMenu.IsBlockingGameplayInput;

        /// <summary>
        /// CanvasGroup de la raíz del canvas (se agrega en runtime si falta; nunca uno anidado). Con <c>interactable = false</c>
        /// ningún botón de este menú recibe clics, ni los de XRI ni los del fallback de gatillo (<c>IsInteractable()</c>).
        /// </summary>
        public CanvasGroup InteractionGroup
        {
            get
            {
                if (m_InteractionGroup == null)
                {
                    Canvas canvas = GetComponentInParent<Canvas>(true);
                    GameObject root = canvas != null ? canvas.rootCanvas.gameObject : gameObject;
                    m_InteractionGroup = root.GetComponent<CanvasGroup>();
                    if (m_InteractionGroup == null) m_InteractionGroup = root.AddComponent<CanvasGroup>();
                }
                return m_InteractionGroup;
            }
        }

        public NewMenuTab CurrentTab => m_CurrentTab;
        public int SelectedLessonIndex => m_SelectedLessonIndex;
        public int SelectedCircuitIndex => m_SelectedCircuitIndex;
        public bool IsExitModalOpen => m_ExitModal != null && m_ExitModal.activeSelf;
        public IReadOnlyList<LessonDefinitionSO> Lessons => m_Lessons;
        public IReadOnlyList<CircuitDefinition> Circuits => m_Circuits;
        public Transform XROriginTransform => m_XROriginTransform;

        public event Action<NewMenuTab> OnTabChanged;
        public event Action<int> OnLessonSelected;
        public event Action<int> OnCircuitSelected;
        public event Action OnExitRequested;

        public void SetLessons(List<LessonDefinitionSO> lessons)
        {
            m_Lessons = lessons != null ? new List<LessonDefinitionSO>(lessons) : new List<LessonDefinitionSO>();
        }

        public void SetCircuits(List<CircuitDefinition> circuits)
        {
            m_Circuits = circuits != null ? new List<CircuitDefinition>(circuits) : new List<CircuitDefinition>();
        }

        private void Awake()
        {
            EnsureLessonsLoadedInEditor();
            InitializeDefaultCircuitsIfEmpty();
            FindDependenciesIfNull();
            LockPlayerStationary();
        }

        private void OnValidate()
        {
            EnsureLessonsLoadedInEditor();
            InitializeDefaultCircuitsIfEmpty();
        }

        private void EnsureLessonsLoadedInEditor()
        {
#if UNITY_EDITOR
            if (m_Lessons == null || m_Lessons.Count == 0)
            {
                m_Lessons = new List<LessonDefinitionSO>();
                for (int i = 1; i <= 6; i++)
                {
                    var lesson = UnityEditor.AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>($"Assets/Scripts/Lessons/Curriculum/LessonData_{i:00}.asset");
                    if (lesson != null) m_Lessons.Add(lesson);
                }
            }
#endif
        }

        private void Start()
        {
            LockPlayerStationary();
            SetupTabButtons();
            SetupLessonItems();
            SetupCircuitItems();
            SetupExitModal();

            // Solapa inicial: Controles y Mandos
            SelectTab(NewMenuTab.Controles);
        }

        private void OnEnable()
        {
            EnableUIFarCasting(true);
            ConnectPauseMenu();
        }

        private void OnDisable()
        {
            EnableUIFarCasting(false);
            DisconnectPauseMenu();
        }

        private void OnDestroy()
        {
            EnableUIFarCasting(false);
            DisconnectPauseMenu();
        }

        private void FindDependenciesIfNull()
        {
            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0)
            {
                m_FarCastInteractors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);
            }

            if (m_InGameMenu == null)
            {
                m_InGameMenu = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>(FindObjectsInactive.Include);
            }
        }

        public void InitializeDefaultCircuitsIfEmpty()
        {
            if (m_Circuits != null && m_Circuits.Count > 0) return;
            m_Circuits = GetDefaultCircuits();
        }

        public static List<CircuitDefinition> GetDefaultCircuits()
        {
            return new List<CircuitDefinition>
            {
                new CircuitDefinition
                {
                    circuitId = "CIRCUIT_01",
                    title = "Circuito Integrador 1: Tráfico y Señales",
                    description = "Recorrido integral por el casco urbano con intersecciones reguladas por semáforos, señales reglamentarias y evaluación en tiempo real de infracciones.",
                    sceneName = "Lesson_05_TurnIntersections"
                },
                new CircuitDefinition
                {
                    circuitId = "CIRCUIT_02",
                    title = "Circuito Integrador 2: Maniobras y Parking",
                    description = "Práctica integral que combina desplazamiento seguro por calles urbanas y maniobras de estacionamiento en dársena a 45° y en paralelo.",
                    sceneName = "Lesson_06_ParkingManeuvers"
                },
                new CircuitDefinition
                {
                    circuitId = "CIRCUIT_CITY",
                    title = "Práctica Libre: Ciudad Completa",
                    description = "Manejo libre e independiente a lo largo de todo el mapa de la ciudad. Ideal para familiarizarse con el comportamiento dinámico del vehículo.",
                    sceneName = "City_Master"
                }
            };
        }

        // ====================================================================
        // GESTIÓN DEL RAYCAST VR (UIOnlyFarCasting)
        // ====================================================================
        private void EnableUIFarCasting(bool enable)
        {
            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0)
            {
                m_FarCastInteractors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);
            }

            if (m_FarCastInteractors == null) return;

            for (int i = 0; i < m_FarCastInteractors.Length; i++)
            {
                if (m_FarCastInteractors[i] == null) continue;

                if (enable)
                {
                    UIOnlyFarCasting.Request(m_FarCastInteractors[i], this);
                }
                else
                {
                    UIOnlyFarCasting.Release(m_FarCastInteractors[i], this);
                }
            }
        }

        // ====================================================================
        // SONDEO DE HARDWARE VR (FALLBACK ROBUSTO PARA TRIGGER / BOTONES)
        // ====================================================================
        private void Update()
        {
            // Atajo de teclado para pruebas en Editor (Escape abre/cierra modal de salida)
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.Escape].wasPressedThisFrame)
            {
                HandleEscapePressed();
            }

            // Sondeo directo de controles OpenXR / Meta Quest
            CheckHardwareTrigger(XRNode.RightHand, ref m_RightTriggerWasPressed);
            CheckHardwareTrigger(XRNode.LeftHand, ref m_LeftTriggerWasPressed);
        }

        /// <summary>
        /// Se engancha al menú de pausa: el bloqueo de entrada se aplica en el momento en que éste abre o termina la pausa
        /// (<see cref="InGameMenuController.GameplayInputBlockingChanged"/>), no en el próximo Update, así no queda ningún hueco
        /// en el que un clic llegue a este menú con la simulación detenida. Comprueba también el estado actual, por si la pausa ya
        /// estaba activa al cargar la escena. Idempotente. Lo llama OnEnable.
        /// </summary>
        public void ConnectPauseMenu()
        {
            if (m_InGameMenu == null) m_InGameMenu = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>(FindObjectsInactive.Include);
            if (m_InGameMenu == null) return;

            m_InGameMenu.GameplayInputBlockingChanged -= ApplyInputBlock;
            m_InGameMenu.GameplayInputBlockingChanged += ApplyInputBlock;
            ApplyInputBlock(InputBlocked);
        }

        /// <summary>Se desengancha del menú de pausa y, si había un bloqueo aplicado, lo levanta. Lo llaman OnDisable y OnDestroy.</summary>
        public void DisconnectPauseMenu()
        {
            if (m_InGameMenu != null) m_InGameMenu.GameplayInputBlockingChanged -= ApplyInputBlock;
            ApplyInputBlock(false);
        }

        // Mientras hay bloqueo el CanvasGroup queda no interactuable y sin bloquear raycasts (el menú sigue visible, sin tocar alpha)
        // y al terminar se restauran los valores previos de interactable y blocksRaycasts. Los botones de este menú dejan de recibir
        // clics de XRI y del fallback de gatillo (IsInteractable), y el canvas ya no captura el rayo: si queda entre el rayo y el
        // botón Reanudar (que vive en otro canvas) no lo tapa.
        private void ApplyInputBlock(bool blocked)
        {
            if (blocked == m_BlockApplied) return;
            m_BlockApplied = blocked;

            CanvasGroup group = InteractionGroup;
            if (blocked)
            {
                m_InteractableBeforeBlock = group.interactable;
                m_BlocksRaycastsBeforeBlock = group.blocksRaycasts;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            else
            {
                group.interactable = m_InteractableBeforeBlock;
                group.blocksRaycasts = m_BlocksRaycastsBeforeBlock;
            }
        }

        /// <summary>
        /// Escape abre/cierra el modal de salida. Se ignora mientras <see cref="InputBlocked"/>: es el único entry point que no
        /// pasa por un botón y por lo tanto no lo cubre el CanvasGroup.
        /// </summary>
        public void HandleEscapePressed()
        {
            if (InputBlocked) return;

            if (IsExitModalOpen) CloseExitModal();
            else OpenExitModal();
        }

        /// <summary>
        /// Paso de entrada de un gatillo/botón: actualiza <paramref name="wasPressed"/> siempre (también durante el bloqueo, así
        /// un gatillo que sigue apretado al terminar la pausa no cuenta como flanco nuevo) y devuelve true solo si hay un
        /// flanco de subida. Qué botón puede recibir el clic lo decide <see cref="TryClickButton"/>: Reanudar sí, los botones de
        /// este menú no mientras dura la pausa.
        /// </summary>
        public bool ProcessTriggerEdge(bool isPressed, ref bool wasPressed)
        {
            bool risingEdge = isPressed && !wasPressed;
            wasPressed = isPressed;
            return risingEdge;
        }

        /// <summary>
        /// Muestra de un control: con el dispositivo inválido (se cayó o parpadea) <paramref name="wasPressed"/> pasa a true, es decir
        /// "apretado": al volver hay que soltar y apretar de nuevo para que cuente como flanco, así un parpadeo del control con el
        /// gatillo o A apretados nunca produce un clic sin pulsación nueva. Con el dispositivo válido es <see cref="ProcessTriggerEdge"/>.
        /// </summary>
        public bool ProcessTriggerSample(bool deviceValid, bool isPressed, ref bool wasPressed)
        {
            if (!deviceValid)
            {
                wasPressed = true;
                return false;
            }
            return ProcessTriggerEdge(isPressed, ref wasPressed);
        }

        /// <summary>
        /// Hace clic en <paramref name="btn"/> si está activo e interactuable. <c>IsInteractable()</c> respeta los CanvasGroup
        /// de los ancestros: con la pausa vigente rechaza los botones de este menú (CanvasGroup no interactuable) y deja pasar
        /// el botón Reanudar del menú de pausa, que está en otro canvas (y es no interactuable mientras la conexión no vuelve).
        /// </summary>
        public bool TryClickButton(Button btn)
        {
            if (btn == null || !btn.IsInteractable() || !btn.gameObject.activeInHierarchy) return false;

            btn.onClick.Invoke();
            return true;
        }

        private void LateUpdate()
        {
            // Con DriverSeatFollower el rig lo coloca el follower sobre su anclaje (MenuViewpoint), con la compensación de la pose
            // de la cabeza; fijarlo acá en cero la pisaría y no habría dependencia del orden de ejecución entre scripts.
            if (m_SeatFollower != null) return;

            // Sin follower (fallback): garantizar que la base del visor se mantenga firmemente anclada al centro de la escena
            // sin caer al vacío por gravedad física ni derivar por controles de locomoción
            if (m_XROriginTransform != null)
            {
                m_XROriginTransform.position = Vector3.zero;
                m_XROriginTransform.rotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// Desactiva componentes de locomoción y gravedad física sobre el XR Origin,
        /// manteniendo al jugador en una estación de visualización fija y cómoda.
        /// </summary>
        public void LockPlayerStationary()
        {
            if (m_XROriginTransform == null)
            {
                var origin = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
                if (origin != null) m_XROriginTransform = origin.transform;
            }

            if (m_XROriginTransform != null)
            {
                // Desactivar CharacterController para que no ejecute gravedad ni colisiones físicas
                var cc = m_XROriginTransform.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                // Desactivar GameObject de Locomotion (GravityProvider, ContinuousMove, SnapTurn, etc.)
                Transform locomotion = m_XROriginTransform.Find("Locomotion");
                if (locomotion != null) locomotion.gameObject.SetActive(false);

                // Desactivar proveedores de locomoción residuales
                foreach (var provider in m_XROriginTransform.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true))
                {
                    provider.enabled = false;
                }

                m_SeatFollower = m_XROriginTransform.GetComponent<DriverSeatFollower>();
                if (m_SeatFollower != null)
                {
                    // El follower lleva el rig al anclaje del menú (MenuViewpoint) con la compensación de la cabeza.
                    m_SeatFollower.SnapToSeat();
                }
                else
                {
                    m_XROriginTransform.position = Vector3.zero;
                    m_XROriginTransform.rotation = Quaternion.identity;
                }
            }
        }

        private void CheckHardwareTrigger(XRNode handNode, ref bool wasPressed)
        {
            XRInputDevice device = XRInputDevices.GetDeviceAtXRNode(handNode);
            bool isPressed = device.isValid && IsTriggerPressed(device);

            if (ProcessTriggerSample(device.isValid, isPressed, ref wasPressed))
            {
                if (Time.unscaledTime - m_LastHardwareClickTime > 0.20f)
                {
                    if (TryTriggerClickOnHoveredUI(handNode))
                    {
                        m_LastHardwareClickTime = Time.unscaledTime;
                    }
                }
            }
        }

        private static bool IsTriggerPressed(XRInputDevice device)
        {
            if (device.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool tb) && tb) return true;
            if (device.TryGetFeatureValue(XRCommonUsages.trigger, out float triggerVal) && triggerVal > 0.5f) return true;
            return device.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool pb) && pb;
        }

        private bool TryTriggerClickOnHoveredUI(XRNode handNode)
        {
            if (m_FarCastInteractors == null) return false;

            for (int i = 0; i < m_FarCastInteractors.Length; i++)
            {
                var interactor = m_FarCastInteractors[i];
                if (interactor == null || !interactor.isActiveAndEnabled) continue;

                // Verificar si este interactor corresponde a la mano sondeada
                bool matchesHand = (handNode == XRNode.RightHand && interactor.name.Contains("Right")) ||
                                   (handNode == XRNode.LeftHand && interactor.name.Contains("Left"));

                if (!matchesHand && m_FarCastInteractors.Length > 1) continue;

                if (interactor.TryGetCurrentUIRaycastResult(out RaycastResult hit) && hit.isValid && hit.gameObject != null)
                {
                    if (TryClickButton(hit.gameObject.GetComponentInParent<Button>())) return true;
                }
            }

            return false;
        }

        // ====================================================================
        // CONFIGURACIÓN DE NAVEGACIÓN Y SOLAPAS
        // ====================================================================
        private void SetupTabButtons()
        {
            if (m_TabBtnControls != null) m_TabBtnControls.onClick.AddListener(() => SelectTab(NewMenuTab.Controles));
            if (m_TabBtnLessons != null) m_TabBtnLessons.onClick.AddListener(() => SelectTab(NewMenuTab.Lecciones));
            if (m_TabBtnCircuits != null) m_TabBtnCircuits.onClick.AddListener(() => SelectTab(NewMenuTab.Circuitos));
            if (m_TabBtnCalibration != null) m_TabBtnCalibration.onClick.AddListener(() => SelectTab(NewMenuTab.Calibracion));
            if (m_TabBtnCredits != null) m_TabBtnCredits.onClick.AddListener(() => SelectTab(NewMenuTab.Creditos));

            if (m_BtnExit != null) m_BtnExit.onClick.AddListener(OpenExitModal);
        }

        public void SelectTab(NewMenuTab tab)
        {
            m_CurrentTab = tab;

            if (m_PanelControls != null) m_PanelControls.SetActive(tab == NewMenuTab.Controles);
            if (m_PanelLessons != null) m_PanelLessons.SetActive(tab == NewMenuTab.Lecciones);
            if (m_PanelCircuits != null) m_PanelCircuits.SetActive(tab == NewMenuTab.Circuitos);
            if (m_PanelCalibration != null) m_PanelCalibration.SetActive(tab == NewMenuTab.Calibracion);
            if (m_PanelCredits != null) m_PanelCredits.SetActive(tab == NewMenuTab.Creditos);

            UpdateTabButtonVisuals(m_TabBtnControls, tab == NewMenuTab.Controles);
            UpdateTabButtonVisuals(m_TabBtnLessons, tab == NewMenuTab.Lecciones);
            UpdateTabButtonVisuals(m_TabBtnCircuits, tab == NewMenuTab.Circuitos);
            UpdateTabButtonVisuals(m_TabBtnCalibration, tab == NewMenuTab.Calibracion);
            UpdateTabButtonVisuals(m_TabBtnCredits, tab == NewMenuTab.Creditos);

            OnTabChanged?.Invoke(tab);
        }

        private void UpdateTabButtonVisuals(Button btn, bool isActive)
        {
            if (btn == null) return;

            Image bg = btn.GetComponent<Image>();
            if (bg != null) bg.color = isActive ? m_ActiveTabColor : m_InactiveTabColor;

            TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = isActive ? m_ActiveTextColor : m_InactiveTextColor;
                label.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ====================================================================
        // SECCIÓN LECCIONES
        // ====================================================================
        private void SetupLessonItems()
        {
            if (m_LessonItemsContainer == null) return;

            m_LessonCardButtons.Clear();
            var buttons = m_LessonItemsContainer.GetComponentsInChildren<Button>(true);
            m_LessonCardButtons.AddRange(buttons);

            for (int i = 0; i < m_LessonCardButtons.Count; i++)
            {
                int index = i;
                m_LessonCardButtons[i].onClick.RemoveAllListeners();
                m_LessonCardButtons[i].onClick.AddListener(() => SelectLesson(index));
            }

            if (m_BtnStartLesson != null)
            {
                m_BtnStartLesson.onClick.RemoveAllListeners();
                m_BtnStartLesson.onClick.AddListener(StartSelectedLesson);
            }

            SelectLesson(0);
        }

        public void SelectLesson(int index)
        {
            if (m_Lessons == null || m_Lessons.Count == 0) return;
            m_SelectedLessonIndex = Mathf.Clamp(index, 0, m_Lessons.Count - 1);

            var lesson = m_Lessons[m_SelectedLessonIndex];
            if (lesson != null)
            {
                if (m_LessonDetailTitle != null) m_LessonDetailTitle.text = lesson.lessonTitle;
                if (m_LessonDetailDescription != null) m_LessonDetailDescription.text = GetLessonDescription(lesson);
            }

            // Actualizar estilo visual de la lista
            for (int i = 0; i < m_LessonCardButtons.Count; i++)
            {
                bool isSelected = (i == m_SelectedLessonIndex);
                var img = m_LessonCardButtons[i].GetComponent<Image>();
                if (img != null) img.color = isSelected ? new Color(0.12f, 0.28f, 0.65f, 1f) : new Color(0.12f, 0.15f, 0.22f, 1f);

                var txt = m_LessonCardButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                {
                    txt.color = isSelected ? Color.white : new Color(0.80f, 0.85f, 0.95f, 1f);
                    txt.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
                }
            }

            OnLessonSelected?.Invoke(m_SelectedLessonIndex);
        }

        public void StartSelectedLesson()
        {
            if (m_Lessons == null || m_SelectedLessonIndex < 0 || m_SelectedLessonIndex >= m_Lessons.Count) return;

            var lesson = m_Lessons[m_SelectedLessonIndex];
            if (lesson == null) return;

            Debug.Log($"[NewMainMenuController] Comenzando Lección: {lesson.lessonId} - {lesson.lessonTitle}");
            EnableUIFarCasting(false);
            LessonSceneFlowManager.Instance.LoadLesson(lesson.lessonId);
        }

        private static string GetLessonDescription(LessonDefinitionSO lesson)
        {
            if (lesson == null) return string.Empty;
            if (lesson.steps != null && lesson.steps.Count > 0 && lesson.steps[0] != null)
            {
                return $"{lesson.steps[0].stepTitle}: {lesson.steps[0].instructionText}";
            }
            return "Módulo formativo de conducción vehicular con asistencia y evaluación en tiempo real.";
        }

        // ====================================================================
        // SECCIÓN CIRCUITOS / INTEGRADORES
        // ====================================================================
        private void SetupCircuitItems()
        {
            if (m_CircuitItemsContainer == null) return;

            m_CircuitCardButtons.Clear();
            var buttons = m_CircuitItemsContainer.GetComponentsInChildren<Button>(true);
            m_CircuitCardButtons.AddRange(buttons);

            for (int i = 0; i < m_CircuitCardButtons.Count; i++)
            {
                int index = i;
                m_CircuitCardButtons[i].onClick.RemoveAllListeners();
                m_CircuitCardButtons[i].onClick.AddListener(() => SelectCircuit(index));
            }

            if (m_BtnStartCircuit != null)
            {
                m_BtnStartCircuit.onClick.RemoveAllListeners();
                m_BtnStartCircuit.onClick.AddListener(StartSelectedCircuit);
            }

            SelectCircuit(0);
        }

        public void SelectCircuit(int index)
        {
            if (m_Circuits == null || m_Circuits.Count == 0) return;
            m_SelectedCircuitIndex = Mathf.Clamp(index, 0, m_Circuits.Count - 1);

            var circuit = m_Circuits[m_SelectedCircuitIndex];
            if (circuit != null)
            {
                if (m_CircuitDetailTitle != null) m_CircuitDetailTitle.text = circuit.title;
                if (m_CircuitDetailDescription != null) m_CircuitDetailDescription.text = circuit.description;
            }

            for (int i = 0; i < m_CircuitCardButtons.Count; i++)
            {
                bool isSelected = (i == m_SelectedCircuitIndex);
                var img = m_CircuitCardButtons[i].GetComponent<Image>();
                if (img != null) img.color = isSelected ? new Color(0.12f, 0.28f, 0.65f, 1f) : new Color(0.12f, 0.15f, 0.22f, 1f);

                var txt = m_CircuitCardButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null)
                {
                    txt.color = isSelected ? Color.white : new Color(0.80f, 0.85f, 0.95f, 1f);
                    txt.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
                }
            }

            OnCircuitSelected?.Invoke(m_SelectedCircuitIndex);
        }

        public void StartSelectedCircuit()
        {
            if (m_Circuits == null || m_SelectedCircuitIndex < 0 || m_SelectedCircuitIndex >= m_Circuits.Count) return;

            var circuit = m_Circuits[m_SelectedCircuitIndex];
            if (circuit == null) return;

            Debug.Log($"[NewMainMenuController] Iniciando Recorrido: {circuit.circuitId} - {circuit.title} (Escena: {circuit.sceneName})");
            EnableUIFarCasting(false);

            if (!string.IsNullOrEmpty(circuit.sceneName))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(circuit.sceneName);
            }
        }

        // ====================================================================
        // MODAL DE SALIDA
        // ====================================================================
        private void SetupExitModal()
        {
            if (m_BtnConfirmExit != null)
            {
                m_BtnConfirmExit.onClick.RemoveAllListeners();
                m_BtnConfirmExit.onClick.AddListener(ConfirmExit);
            }

            if (m_BtnCancelExit != null)
            {
                m_BtnCancelExit.onClick.RemoveAllListeners();
                m_BtnCancelExit.onClick.AddListener(CloseExitModal);
            }

            if (m_ExitModal != null)
            {
                m_ExitModal.SetActive(false);
            }
        }

        public void OpenExitModal()
        {
            if (m_ExitModal != null) m_ExitModal.SetActive(true);
            OnExitRequested?.Invoke();
        }

        public void CloseExitModal()
        {
            if (m_ExitModal != null) m_ExitModal.SetActive(false);
        }

        public void ConfirmExit()
        {
            Debug.Log("[NewMainMenuController] Confirmación de salida: cerrando aplicación...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
