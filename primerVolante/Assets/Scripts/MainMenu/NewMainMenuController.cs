using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

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
        public string difficulty = "Intermedio";
        public string estimatedTime = "10 - 15 min";
    }

    /// <summary>
    /// Controlador del Nuevo Menú Principal en Realidad Virtual ("NewMainMenu").
    /// Ofrece una experiencia ortodoxa, accesible y estática en World Space:
    /// - Navegación lateral por solapas (Controles, Lecciones, Circuitos, Calibración, Créditos).
    /// - Área central de contenido con selección de lecciones y circuitos.
    /// - Botón verde destacado "Iniciar Práctica" / "Comenzar Lección".
    /// - Sistema de salida seguro con modal de confirmación.
    /// - Soporte de interacción por rayo (XR Raycast) acotado a la UI mientras el menú está abierto
    ///   (usando <see cref="UIOnlyFarCasting"/>) y liberado al iniciar una lección.
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
        [SerializeField] private Color m_ActiveTabColor = new Color(0.15f, 0.39f, 0.92f, 1f);   // Azul destacado #2563eb
        [SerializeField] private Color m_InactiveTabColor = new Color(0.12f, 0.15f, 0.22f, 1f); // Gris azulado oscuro
        [SerializeField] private Color m_ActiveTextColor = Color.white;
        [SerializeField] private Color m_InactiveTextColor = new Color(0.70f, 0.74f, 0.82f, 1f);

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
        [SerializeField] private TextMeshProUGUI m_LessonDetailSteps;
        [SerializeField] private Button m_BtnStartLesson;
        [SerializeField] private TextMeshProUGUI m_BtnStartLessonText;

        [Header("Sección Circuitos / Integradores")]
        [SerializeField] private List<CircuitDefinition> m_Circuits = new List<CircuitDefinition>();
        [SerializeField] private Transform m_CircuitItemsContainer;
        [SerializeField] private TextMeshProUGUI m_CircuitDetailTitle;
        [SerializeField] private TextMeshProUGUI m_CircuitDetailDescription;
        [SerializeField] private TextMeshProUGUI m_CircuitDetailInfo;
        [SerializeField] private Button m_BtnStartCircuit;
        [SerializeField] private TextMeshProUGUI m_BtnStartCircuitText;

        [Header("Sección Calibración")]
        [SerializeField] private Slider m_CalibLateralSlider;
        [SerializeField] private Slider m_CalibHeightSlider;
        [SerializeField] private Slider m_CalibDepthSlider;
        [SerializeField] private Button m_BtnResetCalibration;
        [SerializeField] private DriverSeatFollower m_DriverSeatFollower;

        [Header("Modal de Salida")]
        [SerializeField] private GameObject m_ExitModal;
        [SerializeField] private Button m_BtnConfirmExit;
        [SerializeField] private Button m_BtnCancelExit;

        [Header("Interactores VR (Rayo acotado a la UI)")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        // Estado interno
        private NewMenuTab m_CurrentTab = NewMenuTab.Lecciones;
        private int m_SelectedLessonIndex = 0;
        private int m_SelectedCircuitIndex = 0;
        private readonly List<Button> m_LessonCardButtons = new List<Button>();
        private readonly List<Button> m_CircuitCardButtons = new List<Button>();

        public NewMenuTab CurrentTab => m_CurrentTab;
        public int SelectedLessonIndex => m_SelectedLessonIndex;
        public int SelectedCircuitIndex => m_SelectedCircuitIndex;
        public bool IsExitModalOpen => m_ExitModal != null && m_ExitModal.activeSelf;
        public IReadOnlyList<LessonDefinitionSO> Lessons => m_Lessons;
        public IReadOnlyList<CircuitDefinition> Circuits => m_Circuits;

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
            SetupTabButtons();
            SetupLessonItems();
            SetupCircuitItems();
            SetupCalibrationSection();
            SetupExitModal();

            // Activar solapa inicial (Lecciones)
            SelectTab(NewMenuTab.Lecciones);
        }

        private void OnEnable()
        {
            EnableUIFarCasting(true);
        }

        private void OnDisable()
        {
            EnableUIFarCasting(false);
        }

        private void OnDestroy()
        {
            EnableUIFarCasting(false);
        }

        private void FindDependenciesIfNull()
        {
            if (m_DriverSeatFollower == null)
            {
                m_DriverSeatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            }

            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0)
            {
                m_FarCastInteractors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);
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
                    title = "Circuito Integrador 1: Tráfico y Señalización Urbana",
                    description = "Recorrido integral por el casco urbano con intersecciones reguladas por semáforos, señales reglamentarias y evaluación en tiempo real de infracciones de tránsito.",
                    sceneName = "Lesson_05_TurnIntersections",
                    difficulty = "Intermedio",
                    estimatedTime = "10 - 15 min"
                },
                new CircuitDefinition
                {
                    circuitId = "CIRCUIT_02",
                    title = "Circuito Integrador 2: Maniobras y Estacionamiento",
                    description = "Práctica integral de conducción que combina desplazamiento seguro por calles urbanas y maniobras avanzadas de estacionamiento en dársena a 45° y en paralelo.",
                    sceneName = "Lesson_06_ParkingManeuvers",
                    difficulty = "Avanzado",
                    estimatedTime = "12 - 18 min"
                },
                new CircuitDefinition
                {
                    circuitId = "CIRCUIT_CITY",
                    title = "Práctica Libre: Ciudad Completa",
                    description = "Manejo libre e independiente a lo largo de todo el mapa de la ciudad. Ideal para familiarizarse con el comportamiento dinámico del vehículo y los controles.",
                    sceneName = "City_Master",
                    difficulty = "Libre",
                    estimatedTime = "Sin límite"
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
        // NAVEGACIÓN POR SOLAPAS
        // ====================================================================
        private void SetupTabButtons()
        {
            if (m_TabBtnControls != null)
                m_TabBtnControls.onClick.AddListener(() => SelectTab(NewMenuTab.Controles));

            if (m_TabBtnLessons != null)
                m_TabBtnLessons.onClick.AddListener(() => SelectTab(NewMenuTab.Lecciones));

            if (m_TabBtnCircuits != null)
                m_TabBtnCircuits.onClick.AddListener(() => SelectTab(NewMenuTab.Circuitos));

            if (m_TabBtnCalibration != null)
                m_TabBtnCalibration.onClick.AddListener(() => SelectTab(NewMenuTab.Calibracion));

            if (m_TabBtnCredits != null)
                m_TabBtnCredits.onClick.AddListener(() => SelectTab(NewMenuTab.Creditos));

            if (m_BtnExit != null)
                m_BtnExit.onClick.AddListener(OpenExitModal);
        }

        public void SelectTab(NewMenuTab tab)
        {
            m_CurrentTab = tab;

            // Paneles
            if (m_PanelControls != null) m_PanelControls.SetActive(tab == NewMenuTab.Controles);
            if (m_PanelLessons != null) m_PanelLessons.SetActive(tab == NewMenuTab.Lecciones);
            if (m_PanelCircuits != null) m_PanelCircuits.SetActive(tab == NewMenuTab.Circuitos);
            if (m_PanelCalibration != null) m_PanelCalibration.SetActive(tab == NewMenuTab.Calibracion);
            if (m_PanelCredits != null) m_PanelCredits.SetActive(tab == NewMenuTab.Creditos);

            // Estilos de botones
            UpdateTabButtonStyle(m_TabBtnControls, tab == NewMenuTab.Controles);
            UpdateTabButtonStyle(m_TabBtnLessons, tab == NewMenuTab.Lecciones);
            UpdateTabButtonStyle(m_TabBtnCircuits, tab == NewMenuTab.Circuitos);
            UpdateTabButtonStyle(m_TabBtnCalibration, tab == NewMenuTab.Calibracion);
            UpdateTabButtonStyle(m_TabBtnCredits, tab == NewMenuTab.Creditos);

            // Refresco contextual
            if (tab == NewMenuTab.Lecciones)
            {
                UpdateLessonDetailView();
            }
            else if (tab == NewMenuTab.Circuitos)
            {
                UpdateCircuitDetailView();
            }
            else if (tab == NewMenuTab.Calibracion)
            {
                RefreshCalibrationSliders();
            }

            OnTabChanged?.Invoke(tab);
        }

        private void UpdateTabButtonStyle(Button button, bool isActive)
        {
            if (button == null) return;

            Image bg = button.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = isActive ? m_ActiveTabColor : m_InactiveTabColor;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
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
            Button[] buttons = m_LessonItemsContainer.GetComponentsInChildren<Button>(true);
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

            SelectLesson(m_SelectedLessonIndex);
        }

        public void SelectLesson(int index)
        {
            if (m_Lessons == null || m_Lessons.Count == 0) return;
            m_SelectedLessonIndex = Mathf.Clamp(index, 0, m_Lessons.Count - 1);

            // Resaltar tarjeta seleccionada
            for (int i = 0; i < m_LessonCardButtons.Count; i++)
            {
                bool isSel = (i == m_SelectedLessonIndex);
                Image cardBg = m_LessonCardButtons[i].GetComponent<Image>();
                if (cardBg != null)
                {
                    cardBg.color = isSel ? new Color(0.18f, 0.28f, 0.48f, 1f) : new Color(0.11f, 0.14f, 0.20f, 1f);
                }
            }

            UpdateLessonDetailView();
            OnLessonSelected?.Invoke(m_SelectedLessonIndex);
        }

        private void UpdateLessonDetailView()
        {
            if (m_Lessons == null || m_SelectedLessonIndex < 0 || m_SelectedLessonIndex >= m_Lessons.Count) return;

            LessonDefinitionSO lesson = m_Lessons[m_SelectedLessonIndex];
            if (lesson == null) return;

            if (m_LessonDetailTitle != null)
            {
                m_LessonDetailTitle.text = lesson.lessonTitle;
            }

            if (m_LessonDetailSteps != null)
            {
                int count = lesson.steps != null ? lesson.steps.Count : 0;
                m_LessonDetailSteps.text = $"Pasos formativos a cumplir: {count}";
            }

            if (m_LessonDetailDescription != null)
            {
                if (lesson.steps != null && lesson.steps.Count > 0)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("<b>Contenido y Objetivos de la Práctica:</b>");
                    for (int i = 0; i < lesson.steps.Count && i < 6; i++)
                    {
                        var step = lesson.steps[i];
                        if (step != null)
                        {
                            sb.AppendLine($"• {step.stepTitle}");
                        }
                    }
                    if (lesson.steps.Count > 6)
                    {
                        sb.AppendLine($"... y {lesson.steps.Count - 6} pasos más.");
                    }
                    m_LessonDetailDescription.text = sb.ToString();
                }
                else
                {
                    m_LessonDetailDescription.text = "Lección práctica orientada al aprendizaje progresivo de maniobras y seguridad vial.";
                }
            }

            if (m_BtnStartLessonText != null)
            {
                m_BtnStartLessonText.text = "COMENZAR LECCIÓN";
            }
        }

        public void StartSelectedLesson()
        {
            if (m_Lessons == null || m_SelectedLessonIndex < 0 || m_SelectedLessonIndex >= m_Lessons.Count) return;

            LessonDefinitionSO lesson = m_Lessons[m_SelectedLessonIndex];
            if (lesson == null) return;

            Debug.Log($"[NewMainMenu] Iniciando lección formativa: {lesson.lessonTitle} ({lesson.lessonId})");
            EnableUIFarCasting(false);

            if (LessonSceneFlowManager.Instance != null)
            {
                LessonSceneFlowManager.Instance.LoadLesson(lesson);
            }
            else
            {
                Debug.LogWarning("[NewMainMenu] LessonSceneFlowManager no disponible. Comprobando escena directamente.");
            }
        }

        // ====================================================================
        // SECCIÓN CIRCUITOS / INTEGRADORES
        // ====================================================================
        private void SetupCircuitItems()
        {
            if (m_CircuitItemsContainer == null) return;

            m_CircuitCardButtons.Clear();
            Button[] buttons = m_CircuitItemsContainer.GetComponentsInChildren<Button>(true);
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

            SelectCircuit(m_SelectedCircuitIndex);
        }

        public void SelectCircuit(int index)
        {
            if (m_Circuits == null || m_Circuits.Count == 0) return;
            m_SelectedCircuitIndex = Mathf.Clamp(index, 0, m_Circuits.Count - 1);

            for (int i = 0; i < m_CircuitCardButtons.Count; i++)
            {
                bool isSel = (i == m_SelectedCircuitIndex);
                Image cardBg = m_CircuitCardButtons[i].GetComponent<Image>();
                if (cardBg != null)
                {
                    cardBg.color = isSel ? new Color(0.18f, 0.28f, 0.48f, 1f) : new Color(0.11f, 0.14f, 0.20f, 1f);
                }
            }

            UpdateCircuitDetailView();
            OnCircuitSelected?.Invoke(m_SelectedCircuitIndex);
        }

        private void UpdateCircuitDetailView()
        {
            if (m_Circuits == null || m_SelectedCircuitIndex < 0 || m_SelectedCircuitIndex >= m_Circuits.Count) return;

            CircuitDefinition circuit = m_Circuits[m_SelectedCircuitIndex];
            if (circuit == null) return;

            if (m_CircuitDetailTitle != null)
            {
                m_CircuitDetailTitle.text = circuit.title;
            }

            if (m_CircuitDetailDescription != null)
            {
                m_CircuitDetailDescription.text = circuit.description;
            }

            if (m_CircuitDetailInfo != null)
            {
                m_CircuitDetailInfo.text = $"Dificultad: <color=#38bdf8>{circuit.difficulty}</color>   |   Duración estimada: <color=#38bdf8>{circuit.estimatedTime}</color>";
            }

            if (m_BtnStartCircuitText != null)
            {
                m_BtnStartCircuitText.text = "INICIAR RECORRIDO";
            }
        }

        public void StartSelectedCircuit()
        {
            if (m_Circuits == null || m_SelectedCircuitIndex < 0 || m_SelectedCircuitIndex >= m_Circuits.Count) return;

            CircuitDefinition circuit = m_Circuits[m_SelectedCircuitIndex];
            if (circuit == null || string.IsNullOrEmpty(circuit.sceneName)) return;

            Debug.Log($"[NewMainMenu] Iniciando circuito: {circuit.title} (Escena: {circuit.sceneName})");
            EnableUIFarCasting(false);

            if (LessonSceneFlowManager.Instance != null)
            {
                // Si es una lección del diccionario, cargarla vía su ID o cargar la escena directamente
                bool matchedLesson = false;
                foreach (var pair in LessonSceneFlowManager.LessonScenes)
                {
                    if (pair.Value == circuit.sceneName)
                    {
                        LessonSceneFlowManager.Instance.LoadLesson(pair.Key);
                        matchedLesson = true;
                        break;
                    }
                }

                if (!matchedLesson)
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(circuit.sceneName);
                }
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(circuit.sceneName);
            }
        }

        // ====================================================================
        // SECCIÓN CALIBRACIÓN
        // ====================================================================
        private void SetupCalibrationSection()
        {
            if (m_DriverSeatFollower == null)
            {
                m_DriverSeatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            }

            if (m_DriverSeatFollower != null)
            {
                Vector3 min = m_DriverSeatFollower.CalibrationLimitsMin;
                Vector3 max = m_DriverSeatFollower.CalibrationLimitsMax;

                if (m_CalibLateralSlider != null) { m_CalibLateralSlider.minValue = min.x; m_CalibLateralSlider.maxValue = max.x; }
                if (m_CalibHeightSlider != null) { m_CalibHeightSlider.minValue = min.y; m_CalibHeightSlider.maxValue = max.y; }
                if (m_CalibDepthSlider != null) { m_CalibDepthSlider.minValue = min.z; m_CalibDepthSlider.maxValue = max.z; }
            }

            if (m_CalibLateralSlider != null)
            {
                m_CalibLateralSlider.onValueChanged.RemoveAllListeners();
                m_CalibLateralSlider.onValueChanged.AddListener(val =>
                {
                    if (m_DriverSeatFollower != null) m_DriverSeatFollower.SetCalibrationAxis(0, val);
                });
            }

            if (m_CalibHeightSlider != null)
            {
                m_CalibHeightSlider.onValueChanged.RemoveAllListeners();
                m_CalibHeightSlider.onValueChanged.AddListener(val =>
                {
                    if (m_DriverSeatFollower != null) m_DriverSeatFollower.SetCalibrationAxis(1, val);
                });
            }

            if (m_CalibDepthSlider != null)
            {
                m_CalibDepthSlider.onValueChanged.RemoveAllListeners();
                m_CalibDepthSlider.onValueChanged.AddListener(val =>
                {
                    if (m_DriverSeatFollower != null) m_DriverSeatFollower.SetCalibrationAxis(2, val);
                });
            }

            if (m_BtnResetCalibration != null)
            {
                m_BtnResetCalibration.onClick.RemoveAllListeners();
                m_BtnResetCalibration.onClick.AddListener(ResetCalibration);
            }

            RefreshCalibrationSliders();
        }

        public void ResetCalibration()
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.ResetCalibration();
            }
            RefreshCalibrationSliders();
        }

        public void RefreshCalibrationSliders()
        {
            if (m_DriverSeatFollower == null) return;

            Vector3 offset = m_DriverSeatFollower.CalibrationOffset;
            if (m_CalibLateralSlider != null) m_CalibLateralSlider.SetValueWithoutNotify(offset.x);
            if (m_CalibHeightSlider != null) m_CalibHeightSlider.SetValueWithoutNotify(offset.y);
            if (m_CalibDepthSlider != null) m_CalibDepthSlider.SetValueWithoutNotify(offset.z);
        }

        // ====================================================================
        // MODAL DE SALIDA (SALIR)
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

            CloseExitModal();
        }

        public void OpenExitModal()
        {
            if (m_ExitModal != null)
            {
                m_ExitModal.SetActive(true);
            }
            OnExitRequested?.Invoke();
        }

        public void CloseExitModal()
        {
            if (m_ExitModal != null)
            {
                m_ExitModal.SetActive(false);
            }
        }

        public void ConfirmExit()
        {
            Debug.Log("[NewMainMenu] Confirmando salida del simulador.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
