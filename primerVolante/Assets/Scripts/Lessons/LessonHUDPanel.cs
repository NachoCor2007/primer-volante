using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Panel HUD en espacio de mundo situado sobre el volante en la cabina del vehículo.
    /// Muestra la información del paso actual, la instrucción para el conductor,
    /// controles de calibración rápida del asiento y el botón de confirmación/avance.
    /// Al terminar la lección, muestra la pantalla de felicitaciones.
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

        [Header("Panel de Ajuste Rápido de Asiento")]
        [Tooltip("Subpanel de calibración de asiento (visible en el paso 1).")]
        [SerializeField] private GameObject m_SeatAdjustmentPanel;

        [Tooltip("Seguidor de asiento del conductor.")]
        [SerializeField] private DriverSeatFollower m_DriverSeatFollower;

        [Tooltip("Slider para regular la altura de la vista (Eje Y).")]
        [SerializeField] private Slider m_HeightSlider;

        [Tooltip("Slider para regular la distancia adelante/atrás (Eje Z).")]
        [SerializeField] private Slider m_DepthSlider;

        [Tooltip("Botón para restablecer la calibración a cero.")]
        [SerializeField] private Button m_ResetSeatButton;

        [Header("Panel de Felicitaciones / Fin de Lección")]
        [Tooltip("Contenedor mostrado al completar todos los pasos.")]
        [SerializeField] private GameObject m_CompletionPanel;

        [Tooltip("Título del panel de felicitaciones.")]
        [SerializeField] private TextMeshProUGUI m_CompletionTitleText;

        [Tooltip("Descripción del panel de felicitaciones.")]
        [SerializeField] private TextMeshProUGUI m_CompletionDescriptionText;

        [Tooltip("Botón para reiniciar la lección.")]
        [SerializeField] private Button m_RestartButton;

        // Propiedades públicas para acceso e inspección en tests
        public TextMeshProUGUI LessonTitleText => m_LessonTitleText;
        public TextMeshProUGUI StepProgressText => m_StepProgressText;
        public TextMeshProUGUI InstructionText => m_InstructionText;
        public Button ContinueButton => m_ContinueButton;
        public GameObject SeatAdjustmentPanel => m_SeatAdjustmentPanel;
        public GameObject CompletionPanel => m_CompletionPanel;
        public DriverSeatFollower SeatFollower => m_DriverSeatFollower;

        private void Awake()
        {
            if (m_LessonManager == null)
            {
                m_LessonManager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            }

            if (m_DriverSeatFollower == null)
            {
                m_DriverSeatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            }

            SetupContinueButton();
            SetupSeatAdjustmentControls();
        }

        private void OnEnable()
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.CalibrationOffsetChanged += OnSeatOffsetChanged;
            }

            RefreshSlidersFromOffset();
        }

        private void OnDisable()
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.CalibrationOffsetChanged -= OnSeatOffsetChanged;
            }
        }

        public void BindToLessonManager(LessonManager manager)
        {
            m_LessonManager = manager;
        }

        private void SetupContinueButton()
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

        private void SetupSeatAdjustmentControls()
        {
            if (m_DriverSeatFollower == null) return;

            Vector3 min = m_DriverSeatFollower.CalibrationLimitsMin;
            Vector3 max = m_DriverSeatFollower.CalibrationLimitsMax;

            if (m_HeightSlider != null)
            {
                m_HeightSlider.minValue = min.y;
                m_HeightSlider.maxValue = max.y;
                m_HeightSlider.onValueChanged.RemoveAllListeners();
                m_HeightSlider.onValueChanged.AddListener(OnHeightChanged);
            }

            if (m_DepthSlider != null)
            {
                m_DepthSlider.minValue = min.z;
                m_DepthSlider.maxValue = max.z;
                m_DepthSlider.onValueChanged.RemoveAllListeners();
                m_DepthSlider.onValueChanged.AddListener(OnDepthChanged);
            }

            if (m_ResetSeatButton != null)
            {
                m_ResetSeatButton.onClick.RemoveAllListeners();
                m_ResetSeatButton.onClick.AddListener(OnResetSeatClicked);
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

        private void OnHeightChanged(float value)
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.SetCalibrationAxis(1, value);
            }
        }

        private void OnDepthChanged(float value)
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.SetCalibrationAxis(2, value);
            }
        }

        private void OnResetSeatClicked()
        {
            if (m_DriverSeatFollower != null)
            {
                m_DriverSeatFollower.ResetCalibration();
                RefreshSlidersFromOffset();
            }
        }

        private void OnSeatOffsetChanged(Vector3 delta)
        {
            RefreshSlidersFromOffset();
        }

        private void RefreshSlidersFromOffset()
        {
            if (m_DriverSeatFollower == null) return;

            Vector3 offset = m_DriverSeatFollower.CalibrationOffset;

            if (m_HeightSlider != null)
            {
                m_HeightSlider.onValueChanged.RemoveListener(OnHeightChanged);
                m_HeightSlider.value = offset.y;
                m_HeightSlider.onValueChanged.AddListener(OnHeightChanged);
            }

            if (m_DepthSlider != null)
            {
                m_DepthSlider.onValueChanged.RemoveListener(OnDepthChanged);
                m_DepthSlider.value = offset.z;
                m_DepthSlider.onValueChanged.AddListener(OnDepthChanged);
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

            // Mostrar u ocultar el panel de ajuste rápido según corresponda
            if (m_SeatAdjustmentPanel != null)
            {
                // El paso 0 es el ajuste del asiento
                bool isSeatStep = (stepIndex == 0) || (step.stepTitle.ToLower().Contains("asiento"));
                m_SeatAdjustmentPanel.SetActive(isSeatStep);
            }

            // Etiqueta del botón
            if (m_ContinueButtonText != null)
            {
                if (stepIndex == 0)
                {
                    m_ContinueButtonText.text = "Confirmar Posición";
                }
                else if (stepIndex == totalSteps - 1)
                {
                    m_ContinueButtonText.text = "Finalizar Calibración";
                }
                else
                {
                    m_ContinueButtonText.text = "Continuar";
                }
            }

            RefreshSlidersFromOffset();
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
        }
    }
}
