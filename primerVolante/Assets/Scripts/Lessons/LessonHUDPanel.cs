using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Panel HUD en espacio de mundo situado sobre el volante en la cabina del vehículo.
    /// Muestra la información del paso actual, la instrucción para el conductor,
    /// y el botón interactivo de confirmación/avance mediante raycast de VR.
    /// Al terminar la lección, presenta el panel de felicitaciones.
    /// </summary>
    public class LessonHUDPanel : MonoBehaviour
    {
        [Header("Referencias de Lección")]
        [Tooltip("Gestor de lecciones asociado. Si es nulo, se busca automáticamente.")]
        [SerializeField] private LessonManager m_LessonManager;

        [Header("Textos Principales")]
        [Tooltip("Texto para el título de la lección.")]
        [SerializeField] private TextMeshProUGUI m_LessonTitleText;

        [Tooltip("Texto que muestra 'Paso X de Y'.")]
        [SerializeField] private TextMeshProUGUI m_StepProgressText;

        [Tooltip("Texto con la instrucción detallada del paso.")]
        [SerializeField] private TextMeshProUGUI m_InstructionText;

        [Header("Controles de Flujo")]
        [Tooltip("Contenedor general del contenido de pasos.")]
        [SerializeField] private GameObject m_StepContentPanel;

        [Tooltip("Botón para avanzar al siguiente paso o confirmar posición.")]
        [SerializeField] private Button m_ContinueButton;

        [Tooltip("Texto dentro del botón Continuar/Confirmar.")]
        [SerializeField] private TextMeshProUGUI m_ContinueButtonText;

        [Header("Panel de Felicitaciones / Fin de Lección")]
        [Tooltip("Contenedor mostrado al completar todos los pasos.")]
        [SerializeField] private GameObject m_CompletionPanel;

        [Tooltip("Título del panel de felicitaciones.")]
        [SerializeField] private TextMeshProUGUI m_CompletionTitleText;

        [Tooltip("Descripción del panel de felicitaciones.")]
        [SerializeField] private TextMeshProUGUI m_CompletionDescriptionText;

        [Tooltip("Botón para reiniciar la lección.")]
        [SerializeField] private Button m_RestartButton;

        [Header("Interacción VR (Raycast / Far Casting)")]
        [Tooltip("Near-Far Interactors de las manos VR a los que se les habilita el rayo a distancia " +
                 "mientras se interactúa con el HUD de la lección.")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        private Canvas m_Canvas;
        private bool[] m_FarCastPreviousStates;
        private bool m_FarCastingActive = false;

        // Propiedades públicas para acceso e inspección en tests
        public TextMeshProUGUI LessonTitleText => m_LessonTitleText;
        public TextMeshProUGUI StepProgressText => m_StepProgressText;
        public TextMeshProUGUI InstructionText => m_InstructionText;
        public Button ContinueButton => m_ContinueButton;
        public TextMeshProUGUI ContinueButtonText => m_ContinueButtonText;
        public GameObject StepContentPanel => m_StepContentPanel;
        public GameObject CompletionPanel => m_CompletionPanel;
        public Button RestartButton => m_RestartButton;
        public NearFarInteractor[] FarCastInteractors => m_FarCastInteractors;
        public Canvas Canvas => m_Canvas != null ? m_Canvas : (m_Canvas = GetComponent<Canvas>());
        public Camera WorldCamera => Canvas != null ? Canvas.worldCamera : null;
        public bool FarCastingActive => m_FarCastingActive;

        private void Awake()
        {
            m_Canvas = GetComponent<Canvas>();
            if (m_Canvas != null && m_Canvas.worldCamera == null)
            {
                m_Canvas.worldCamera = Camera.main;
            }

            if (m_LessonManager == null)
            {
                m_LessonManager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            }

            DiscoverFarCastInteractors();
            SetupButtons();
        }

        private void OnEnable()
        {
            SetFarCasting(true);
        }

        private void OnDisable()
        {
            SetFarCasting(false);
        }

        public void BindToLessonManager(LessonManager manager)
        {
            m_LessonManager = manager;
        }

        public void SetFarCastInteractors(NearFarInteractor[] interactors)
        {
            m_FarCastInteractors = interactors;
        }

        private void DiscoverFarCastInteractors()
        {
            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0)
            {
                m_FarCastInteractors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);
            }
        }

        private void SetupButtons()
        {
            if (m_ContinueButton != null)
            {
                m_ContinueButton.onClick.RemoveAllListeners();
                m_ContinueButton.onClick.AddListener(OnContinueClicked);
            }

            if (m_RestartButton != null)
            {
                m_RestartButton.onClick.RemoveAllListeners();
                m_RestartButton.onClick.AddListener(OnRestartClicked);
            }
        }

        /// <summary>
        /// Habilita o restaura el cast a distancia de los NearFarInteractors del jugador
        /// para permitir interactuar con el botón Continuar del HUD mediante puntero láser.
        /// </summary>
        public void SetFarCasting(bool enabled)
        {
            if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0)
            {
                DiscoverFarCastInteractors();
                if (m_FarCastInteractors == null || m_FarCastInteractors.Length == 0) return;
            }

            if (enabled)
            {
                if (!m_FarCastingActive)
                {
                    m_FarCastPreviousStates = new bool[m_FarCastInteractors.Length];
                    for (int i = 0; i < m_FarCastInteractors.Length; i++)
                    {
                        if (m_FarCastInteractors[i] != null)
                        {
                            m_FarCastPreviousStates[i] = m_FarCastInteractors[i].enableFarCasting;
                            m_FarCastInteractors[i].enableFarCasting = true;
                        }
                    }
                    m_FarCastingActive = true;
                }
            }
            else
            {
                if (m_FarCastingActive && m_FarCastPreviousStates != null)
                {
                    for (int i = 0; i < m_FarCastInteractors.Length; i++)
                    {
                        if (m_FarCastInteractors[i] != null && i < m_FarCastPreviousStates.Length)
                        {
                            m_FarCastInteractors[i].enableFarCasting = m_FarCastPreviousStates[i];
                        }
                    }
                    m_FarCastingActive = false;
                }
            }
        }

        private void OnContinueClicked()
        {
            if (m_LessonManager != null)
            {
                m_LessonManager.CompleteCurrentStep();
            }
        }

        private void OnRestartClicked()
        {
            if (m_LessonManager != null && m_LessonManager.ActiveLesson != null)
            {
                m_LessonManager.StartLesson(m_LessonManager.ActiveLesson);
            }
        }

        /// <summary>
        /// Presenta la información y controles del paso actual de la lección.
        /// </summary>
        public void DisplayStep(LessonStepSO step, int stepIndex, int totalSteps)
        {
            if (m_StepContentPanel != null) m_StepContentPanel.SetActive(true);
            if (m_CompletionPanel != null) m_CompletionPanel.SetActive(false);

            if (m_LessonManager != null && m_LessonManager.ActiveLesson != null && m_LessonTitleText != null)
            {
                m_LessonTitleText.text = m_LessonManager.ActiveLesson.lessonTitle;
            }

            if (m_StepProgressText != null)
            {
                m_StepProgressText.text = $"Paso {stepIndex + 1} de {totalSteps}: {step.stepTitle}";
            }

            if (m_InstructionText != null)
            {
                m_InstructionText.text = step.instructionText;
            }

            if (m_ContinueButtonText != null)
            {
                if (stepIndex == totalSteps - 1)
                {
                    m_ContinueButtonText.text = "Finalizar Calibración";
                }
                else
                {
                    m_ContinueButtonText.text = "Continuar";
                }
            }

            SetFarCasting(true);
        }

        /// <summary>
        /// Muestra la pantalla de finalización de la lección con felicitaciones.
        /// </summary>
        public void DisplayCompletion(LessonDefinitionSO lesson)
        {
            if (m_StepContentPanel != null) m_StepContentPanel.SetActive(false);
            if (m_CompletionPanel != null) m_CompletionPanel.SetActive(true);

            if (m_CompletionTitleText != null)
            {
                m_CompletionTitleText.text = "¡Excelente Trabajo!";
            }

            if (m_CompletionDescriptionText != null)
            {
                m_CompletionDescriptionText.text = $"Has completado satisfactoriamente la {lesson.lessonTitle}.\nTu ambiente y vista de manejo han sido calibrados de manera óptima.";
            }

            SetFarCasting(true);
        }
    }
}
