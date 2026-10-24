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
    /// Al terminar la lección, presenta el panel de felicitaciones con opciones para
    /// repetir la lección o continuar a la siguiente lección del currículum.
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

        [Tooltip("Botón para avanzar a la siguiente lección.")]
        [SerializeField] private Button m_NextLessonButton;

        [Tooltip("Texto dentro del botón Siguiente Lección.")]
        [SerializeField] private TextMeshProUGUI m_NextLessonButtonText;

        [Header("Interacción VR (Raycast / Far Casting)")]
        [Tooltip("Near-Far Interactors de las manos VR a los que se les habilita el rayo a distancia " +
                 "mientras se interactúa con el HUD de la lección. El rayo queda restringido a la capa UI " +
                 "(no interactúa con volante, espejos, etc.).")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        private Canvas m_Canvas;
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
        public Button NextLessonButton => m_NextLessonButton;
        public TextMeshProUGUI NextLessonButtonText => m_NextLessonButtonText;
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

            if (m_NextLessonButton != null)
            {
                m_NextLessonButton.onClick.RemoveAllListeners();
                m_NextLessonButton.onClick.AddListener(OnNextLessonClicked);
            }
        }

        /// <summary>
        /// Habilita o libera el cast a distancia de los NearFarInteractors del jugador para poder usar el
        /// botón Continuar del HUD mediante puntero láser. Mientras está activo, el rayo a distancia queda
        /// restringido a la capa UI (ver <see cref="UIOnlyFarCasting"/>) y no interactúa con la cabina.
        /// Es idempotente: el estado original lo guarda y restaura UIOnlyFarCasting.
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
                if (m_FarCastingActive) return;
                for (int i = 0; i < m_FarCastInteractors.Length; i++)
                {
                    UIOnlyFarCasting.Request(m_FarCastInteractors[i], this);
                }
                m_FarCastingActive = true;
            }
            else
            {
                if (!m_FarCastingActive) return;
                for (int i = 0; i < m_FarCastInteractors.Length; i++)
                {
                    UIOnlyFarCasting.Release(m_FarCastInteractors[i], this);
                }
                m_FarCastingActive = false;
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

        private void OnNextLessonClicked()
        {
            if (m_LessonManager != null)
            {
                m_LessonManager.StartNextLesson();
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
                    if (m_LessonManager != null && m_LessonManager.ActiveLesson != null && m_LessonManager.ActiveLesson.lessonId == "LESSON_01")
                    {
                        m_ContinueButtonText.text = "Finalizar Calibración";
                    }
                    else
                    {
                        m_ContinueButtonText.text = "Finalizar Lección";
                    }
                }
                else
                {
                    m_ContinueButtonText.text = "Continuar";
                }
            }

            SetFarCasting(true);
        }

        /// <summary>
        /// Muestra la pantalla de finalización de la lección con felicitaciones
        /// y ofrece la opción de repetir la lección o continuar a la siguiente si existe.
        /// </summary>
        public void DisplayCompletion(LessonDefinitionSO lesson)
        {
            if (m_StepContentPanel != null) m_StepContentPanel.SetActive(false);
            if (m_CompletionPanel != null) m_CompletionPanel.SetActive(true);

            bool hasNextLesson = m_LessonManager != null && m_LessonManager.HasNextLesson;

            if (m_NextLessonButton != null)
            {
                m_NextLessonButton.gameObject.SetActive(hasNextLesson);
            }

            // Acomodar botones según disponibilidad de siguiente lección
            if (m_RestartButton != null)
            {
                RectTransform restartRt = m_RestartButton.GetComponent<RectTransform>();
                if (restartRt != null)
                {
                    restartRt.anchoredPosition = hasNextLesson ? new Vector2(-135f, -195f) : new Vector2(0f, -195f);
                    restartRt.sizeDelta = hasNextLesson ? new Vector2(240f, 46f) : new Vector2(260f, 46f);
                }
            }

            if (m_NextLessonButton != null)
            {
                RectTransform nextRt = m_NextLessonButton.GetComponent<RectTransform>();
                if (nextRt != null)
                {
                    nextRt.anchoredPosition = new Vector2(135f, -195f);
                    nextRt.sizeDelta = new Vector2(240f, 46f);
                }
            }

            if (m_CompletionTitleText != null)
            {
                m_CompletionTitleText.text = "¡Excelente Trabajo!";
            }

            if (m_CompletionDescriptionText != null)
            {
                if (hasNextLesson)
                {
                    m_CompletionDescriptionText.text = $"Has completado satisfactoriamente la {lesson.lessonTitle}.\n¿Deseas continuar a la siguiente lección o repetir la actual?";
                }
                else
                {
                    m_CompletionDescriptionText.text = $"Has completado satisfactoriamente la {lesson.lessonTitle}.\n¡Felicitaciones por completar el curso de manejo en Primer Volante!";
                }
            }

            SetFarCasting(true);
        }
    }
}
