using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PrimerVolante.VR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRNode = UnityEngine.XR.XRNode;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Panel HUD en espacio de mundo situado sobre el volante en la cabina del vehículo.
    /// Muestra la información del paso actual, la instrucción para el conductor,
    /// y el botón interactivo de confirmación/avance mediante raycast de VR.
    /// Al terminar la lección, presenta el panel de felicitaciones con opciones para
    /// repetir la lección o continuar a la siguiente lección del currículum.
    /// El panel se puede minimizar a una franja fina en su borde inferior (botón A del control derecho,
    /// tecla H o botones del propio panel) para no tapar la calle, y se maximiza solo cuando hay información nueva.
    /// </summary>
    public class LessonHUDPanel : MonoBehaviour
    {
        [Header("Referencias de Lección")]
        [Tooltip("Gestor de lecciones asociado. Si es nulo, se busca automáticamente.")]
        [SerializeField] private LessonManager m_LessonManager;

        [Tooltip("Controlador del menú in-game / pausa / calibración.")]
        [SerializeField] private InGameMenuController m_InGameMenuController;

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

        [Tooltip("Botón para volver al menú principal.")]
        [SerializeField] private Button m_MainMenuButton;

        [Tooltip("Botón para avanzar a la siguiente lección.")]
        [SerializeField] private Button m_NextLessonButton;

        [Tooltip("Texto dentro del botón Siguiente Lección.")]
        [SerializeField] private TextMeshProUGUI m_NextLessonButtonText;

        [Header("Interacción VR (Raycast / Far Casting)")]
        [Tooltip("Near-Far Interactors de las manos VR a los que se les habilita el rayo a distancia " +
                 "mientras se interactúa con el HUD de la lección. El rayo queda restringido a la capa UI " +
                 "(no interactúa con volante, espejos, etc.).")]
        [SerializeField] private NearFarInteractor[] m_FarCastInteractors;

        [Header("Feedback Sensorial de Acción")]
        [Tooltip("Borde o marco brillante del HUD para feedback visual de acción completada.")]
        [SerializeField] private Image m_SuccessBorderGlow;

        [Tooltip("Fuente de audio para el sonido de confirmación.")]
        [SerializeField] private AudioSource m_AudioSource;

        [Tooltip("Clip sonoro de confirmación / chime positivo.")]
        [SerializeField] private AudioClip m_SuccessChimeClip;

        [Header("Minimizar / Maximizar")]
        [Tooltip("Fondo general del panel (se oculta al minimizar).")]
        [SerializeField] private GameObject m_Background;

        [Tooltip("Franja fina anclada al borde inferior que se muestra mientras el panel está minimizado.")]
        [SerializeField] private GameObject m_MinimizedBar;

        [Tooltip("Texto 'Paso X de Y: título' de la franja minimizada.")]
        [SerializeField] private TextMeshProUGUI m_MinimizedStepText;

        [Tooltip("Botón 'Ocultar (A)' del panel maximizado. Minimiza el panel.")]
        [SerializeField] private Button m_MinimizeButton;

        [Tooltip("Botón 'Mostrar (A)' de la franja minimizada. Maximiza el panel.")]
        [SerializeField] private Button m_MaximizeButton;

        [Tooltip("Tecla de teclado que minimiza/maximiza el panel para pruebas en el Editor/XR Device Simulator. " +
                 "No debe coincidir con las teclas de depuración de VehicleController (W, S, E, Z, C, H, flechas). " +
                 "Key.None desactiva la tecla.")]
        [SerializeField] private Key m_ToggleMinimizeKey = Key.O;

        [Tooltip("Nodo XR del control cuyo botón A (primaryButton) minimiza/maximiza el panel (mano derecha en Meta Quest).")]
        [SerializeField] private XRNode m_ToggleMinimizeNode = XRNode.RightHand;

        private Canvas m_Canvas;
        private bool m_FarCastingActive = false;
        private Coroutine m_GlowCoroutine;
        private bool m_IsMinimized;
        private bool m_WarningActive;
        private XRButtonEdgeDetector m_MinimizeButtonEdge;
        private static Sprite s_HollowBorderSprite;

        // Propiedades públicas para acceso e inspección en tests
        public TextMeshProUGUI LessonTitleText => m_LessonTitleText;
        public TextMeshProUGUI StepProgressText => m_StepProgressText;
        public TextMeshProUGUI InstructionText => m_InstructionText;
        public Button ContinueButton => m_ContinueButton;
        public TextMeshProUGUI ContinueButtonText => m_ContinueButtonText;
        public GameObject StepContentPanel => m_StepContentPanel;
        public GameObject CompletionPanel => m_CompletionPanel;
        public Button RestartButton => m_RestartButton;
        public Button MainMenuButton => m_MainMenuButton;
        public Button NextLessonButton => m_NextLessonButton;
        public TextMeshProUGUI NextLessonButtonText => m_NextLessonButtonText;
        public NearFarInteractor[] FarCastInteractors => m_FarCastInteractors;
        public Image SuccessBorderGlow => m_SuccessBorderGlow;
        public AudioSource AudioSource => m_AudioSource;
        public AudioClip SuccessChimeClip
        {
            get => m_SuccessChimeClip;
            set => m_SuccessChimeClip = value;
        }
        public InGameMenuController InGameMenuController
        {
            get => m_InGameMenuController;
            set => SetInGameMenuController(value);
        }
        public Canvas Canvas => m_Canvas != null ? m_Canvas : (m_Canvas = GetComponent<Canvas>());
        public Camera WorldCamera => Canvas != null ? Canvas.worldCamera : null;
        public bool FarCastingActive => m_FarCastingActive;
        public GameObject Background => m_Background;
        public GameObject MinimizedBar => m_MinimizedBar;
        public TextMeshProUGUI MinimizedStepText => m_MinimizedStepText;
        public Button MinimizeButton => m_MinimizeButton;
        public Button MaximizeButton => m_MaximizeButton;

        /// <summary>true mientras el panel está reducido a la franja inferior.</summary>
        public bool IsMinimized => m_IsMinimized;

        /// <summary>Se dispara solo cuando el estado minimizado cambia de verdad. El argumento es el nuevo estado.</summary>
        public event Action<bool> MinimizedChanged;

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

            if (m_InGameMenuController == null)
            {
                m_InGameMenuController = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>();
            }

            if (m_Background == null)
            {
                Transform background = transform.Find("Background");
                if (background != null) m_Background = background.gameObject;
            }

            if (m_MinimizedBar != null) m_MinimizedBar.SetActive(m_IsMinimized);

            ConfigureHollowBorderGlow();
            DiscoverFarCastInteractors();
            SetupButtons();
        }

        private void Update()
        {
            // Key.None (o un valor fuera de rango) desactiva la tecla; el botón A se lee igual
            bool keyValid = m_ToggleMinimizeKey > Key.None && m_ToggleMinimizeKey <= Key.OEM5;
            if (keyValid && Keyboard.current != null && Keyboard.current[m_ToggleMinimizeKey].wasPressedThisFrame)
            {
                ToggleMinimized();
            }

            CheckMinimizeButtonPress();
        }

        /// <summary>
        /// Sondea el botón A del control derecho (API legada de XR, ver <see cref="XRButtonEdgeDetector"/>)
        /// y minimiza/maximiza solo en el flanco de subida.
        /// </summary>
        private void CheckMinimizeButtonPress()
        {
            if (m_MinimizeButtonEdge.Poll(m_ToggleMinimizeNode, XRCommonUsages.primaryButton))
            {
                ToggleMinimized();
            }
        }

        private void OnEnable()
        {
            SetFarCasting(true);
            SubscribeToMenuController(true);
        }

        private void OnDisable()
        {
            SetFarCasting(false);
            SubscribeToMenuController(false);
        }

        public void SetInGameMenuController(InGameMenuController menuController)
        {
            SubscribeToMenuController(false);
            m_InGameMenuController = menuController;
            SubscribeToMenuController(true);
        }

        private void SubscribeToMenuController(bool subscribe)
        {
            if (m_InGameMenuController == null) return;
            if (subscribe)
            {
                m_InGameMenuController.MenuOpened -= HandleInGameMenuOpened;
                m_InGameMenuController.MenuOpened += HandleInGameMenuOpened;
                m_InGameMenuController.MenuClosed -= HandleInGameMenuClosed;
                m_InGameMenuController.MenuClosed += HandleInGameMenuClosed;
                if (m_InGameMenuController.IsOpen && Canvas != null)
                {
                    Canvas.enabled = false;
                }
            }
            else
            {
                m_InGameMenuController.MenuOpened -= HandleInGameMenuOpened;
                m_InGameMenuController.MenuClosed -= HandleInGameMenuClosed;
            }
        }

        private void HandleInGameMenuOpened()
        {
            if (Canvas != null)
            {
                Canvas.enabled = false;
            }
            SetFarCasting(false);
        }

        private void HandleInGameMenuClosed()
        {
            if (Canvas != null)
            {
                Canvas.enabled = true;
            }
            SetFarCasting(true);
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

            if (m_MainMenuButton != null)
            {
                m_MainMenuButton.onClick.RemoveAllListeners();
                m_MainMenuButton.onClick.AddListener(OnMainMenuClicked);
            }

            if (m_NextLessonButton != null)
            {
                m_NextLessonButton.onClick.RemoveAllListeners();
                m_NextLessonButton.onClick.AddListener(OnNextLessonClicked);
            }

            if (m_MinimizeButton != null)
            {
                m_MinimizeButton.onClick.RemoveAllListeners();
                m_MinimizeButton.onClick.AddListener(OnMinimizeClicked);
            }

            if (m_MaximizeButton != null)
            {
                m_MaximizeButton.onClick.RemoveAllListeners();
                m_MaximizeButton.onClick.AddListener(OnMaximizeClicked);
            }
        }

        /// <summary>
        /// true si el usuario puede minimizar/maximizar el panel: no con el menú de pausa abierto
        /// ni con el panel de felicitaciones visible.
        /// </summary>
        private bool CanToggleMinimized()
        {
            if (m_InGameMenuController != null && m_InGameMenuController.IsOpen) return false;
            if (m_CompletionPanel != null && m_CompletionPanel.activeSelf) return false;
            return true;
        }

        private void OnMinimizeClicked()
        {
            if (CanToggleMinimized()) SetMinimized(true);
        }

        private void OnMaximizeClicked()
        {
            if (CanToggleMinimized()) SetMinimized(false);
        }

        /// <summary>
        /// Alterna entre el panel completo y la franja inferior. Se ignora con el menú de pausa abierto
        /// o con el panel de felicitaciones visible.
        /// </summary>
        public void ToggleMinimized()
        {
            if (!CanToggleMinimized()) return;
            SetMinimized(!m_IsMinimized);
        }

        /// <summary>
        /// Minimiza el panel a una franja fina en su borde inferior, o lo restaura. El canvas y el rayo VR siguen
        /// activos para poder usar la franja. Dispara <see cref="MinimizedChanged"/> solo si el estado cambia.
        /// Minimizar no hace nada con el panel de felicitaciones visible; maximizar funciona siempre.
        /// </summary>
        public void SetMinimized(bool minimized)
        {
            if (m_IsMinimized == minimized) return;
            if (minimized && m_CompletionPanel != null && m_CompletionPanel.activeSelf) return;
            m_IsMinimized = minimized;
            ApplyMinimizedState();
            MinimizedChanged?.Invoke(m_IsMinimized);
        }

        private void ApplyMinimizedState()
        {
            if (m_IsMinimized)
            {
                if (m_Background != null) m_Background.SetActive(false);
                if (m_StepContentPanel != null) m_StepContentPanel.SetActive(false);
                if (m_SuccessBorderGlow != null) m_SuccessBorderGlow.gameObject.SetActive(false);
                if (m_MinimizedBar != null) m_MinimizedBar.SetActive(true);
            }
            else
            {
                if (m_MinimizedBar != null) m_MinimizedBar.SetActive(false);
                if (m_Background != null) m_Background.SetActive(true);

                bool completionVisible = m_CompletionPanel != null && m_CompletionPanel.activeSelf;
                if (m_StepContentPanel != null) m_StepContentPanel.SetActive(!completionVisible);

                // El glow solo vuelve si había un feedback o una advertencia activos
                if (m_SuccessBorderGlow != null)
                {
                    m_SuccessBorderGlow.gameObject.SetActive(m_GlowCoroutine != null || m_WarningActive);
                }
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

        private void OnMainMenuClicked()
        {
            if (LessonSceneFlowManager.Instance != null)
            {
                LessonSceneFlowManager.Instance.ReturnToMainMenu();
            }
        }

        private void OnNextLessonClicked()
        {
            if (LessonSceneFlowManager.Instance != null && m_LessonManager != null && m_LessonManager.ActiveLesson != null)
            {
                LessonSceneFlowManager.Instance.LoadNextLesson(m_LessonManager.ActiveLesson);
            }
            else if (m_LessonManager != null)
            {
                m_LessonManager.StartNextLesson();
            }
        }

        /// <summary>
        /// Presenta la información y controles del paso actual de la lección.
        /// </summary>
        public void DisplayStep(LessonStepSO step, int stepIndex, int totalSteps)
        {
            if (m_GlowCoroutine != null)
            {
                StopCoroutine(m_GlowCoroutine);
                m_GlowCoroutine = null;
            }

            if (m_SuccessBorderGlow != null)
            {
                m_SuccessBorderGlow.gameObject.SetActive(false);
            }

            m_WarningActive = false;
            if (m_CompletionPanel != null) m_CompletionPanel.SetActive(false);
            if (m_StepContentPanel != null) m_StepContentPanel.SetActive(true);
            // Un paso nuevo es información nueva: se maximiza solo
            SetMinimized(false);

            if (m_LessonManager != null && m_LessonManager.ActiveLesson != null && m_LessonTitleText != null)
            {
                m_LessonTitleText.text = m_LessonManager.ActiveLesson.lessonTitle;
            }

            ClearStepWarning();

            string stepProgress = $"Paso {stepIndex + 1} de {totalSteps}: {step.stepTitle}";
            if (m_StepProgressText != null) m_StepProgressText.text = stepProgress;
            if (m_MinimizedStepText != null) m_MinimizedStepText.text = stepProgress;

            if (m_InstructionText != null)
            {
                m_InstructionText.text = step.instructionText;
            }

            bool isActionRequired = (step.completionMode == LessonStepCompletionMode.ActionRequired);

            if (m_ContinueButton != null)
            {
                m_ContinueButton.interactable = !isActionRequired;
            }

            if (m_ContinueButtonText != null)
            {
                if (isActionRequired)
                {
                    m_ContinueButtonText.text = "Esperando acción...";
                }
                else
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
            }

            SetFarCasting(true);
        }

        /// <summary>
        /// Dispara el feedback sensorial completo: borde verde esmeralda brillante (#00E676)
        /// pulsando suavemente durante 0.8s, chime auditivo de confirmación y cambio en el texto del botón a '¡Acción Completada!'.
        /// </summary>
        public void PlayActionSuccessFeedback(System.Action onCompleted = null)
        {
            if (m_GlowCoroutine != null)
            {
                StopCoroutine(m_GlowCoroutine);
            }

            // Reproducir notificación sonora de acción completada de inmediato
            PlayChimeSound();

            if (gameObject.activeInHierarchy)
            {
                m_GlowCoroutine = StartCoroutine(ActionSuccessFeedbackRoutine(onCompleted));
            }
            else
            {
                onCompleted?.Invoke();
            }
        }

        private System.Collections.IEnumerator ActionSuccessFeedbackRoutine(System.Action onCompleted)
        {
            if (m_ContinueButtonText != null)
            {
                m_ContinueButtonText.text = "¡Acción Completada!";
            }

            Color emerald = new Color(0f, 0.902f, 0.463f, 1f); // #00E676

            if (m_SuccessBorderGlow != null)
            {
                ConfigureHollowBorderGlow();
                m_SuccessBorderGlow.color = new Color(emerald.r, emerald.g, emerald.b, 0f);
                // Minimizado, el glow no se ve (el paso siguiente maximiza el panel)
                m_SuccessBorderGlow.gameObject.SetActive(!m_IsMinimized);
            }

            float duration = 0.8f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Pulso suave senoidal
                float pulse = Mathf.Sin(t * Mathf.PI);
                float alpha = Mathf.Clamp01(pulse * 1.1f);

                if (m_SuccessBorderGlow != null)
                {
                    m_SuccessBorderGlow.color = new Color(emerald.r, emerald.g, emerald.b, alpha);
                }

                yield return null;
            }

            if (m_SuccessBorderGlow != null)
            {
                m_SuccessBorderGlow.color = new Color(emerald.r, emerald.g, emerald.b, 0f);
                m_SuccessBorderGlow.gameObject.SetActive(false);
            }

            m_GlowCoroutine = null;
            onCompleted?.Invoke();
        }

        /// <summary>
        /// Muestra una advertencia o falta en color rojo en el HUD sobre el volante y reproduce el sonido de advertencia.
        /// </summary>
        public void ShowStepWarning(string warningMessage, AudioClip warningClip = null)
        {
            if (m_GlowCoroutine != null)
            {
                StopCoroutine(m_GlowCoroutine);
                m_GlowCoroutine = null;
            }

            // Una falta es información nueva: se maximiza solo
            m_WarningActive = true;
            SetMinimized(false);

            if (m_InstructionText != null)
            {
                m_InstructionText.text = $"<color=#FF3B30>{warningMessage}</color>";
            }

            if (m_ContinueButtonText != null)
            {
                m_ContinueButtonText.text = "Falta Cometida";
            }

            if (m_SuccessBorderGlow != null)
            {
                ConfigureHollowBorderGlow();
                m_SuccessBorderGlow.color = new Color(1f, 0.23f, 0.19f, 0.95f);
                m_SuccessBorderGlow.gameObject.SetActive(true);
            }

            PlayWarningSound(warningClip);
        }

        /// <summary>
        /// Limpia el estado de advertencia del HUD.
        /// </summary>
        public void ClearStepWarning()
        {
            m_WarningActive = false;
            if (m_SuccessBorderGlow != null && m_GlowCoroutine == null)
            {
                m_SuccessBorderGlow.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Emite un sonido de advertencia audible para alertar de la falta.
        /// </summary>
        public void PlayWarningSound(AudioClip customClip = null)
        {
            if (m_AudioSource == null)
            {
                m_AudioSource = GetComponent<AudioSource>();
                if (m_AudioSource == null)
                {
                    m_AudioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (m_AudioSource != null)
            {
                m_AudioSource.playOnAwake = false;
                m_AudioSource.spatialBlend = 0f;
                m_AudioSource.volume = 1f;

                AudioClip clipToPlay = customClip;
                if (clipToPlay == null)
                {
#if UNITY_EDITOR
                    clipToPlay = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Vehicle/infraction_chime.wav");
#endif
                }

                if (clipToPlay != null)
                {
                    m_AudioSource.PlayOneShot(clipToPlay, 1.0f);
                }
            }
        }

        /// <summary>
        /// Configura el componente Image del marco brillante con el sprite 9-slice hueco,
        /// asegurando que fillCenter sea false para que solo se renderice el perímetro exterior.
        /// </summary>
        public void ConfigureHollowBorderGlow()
        {
            if (m_SuccessBorderGlow != null)
            {
                m_SuccessBorderGlow.sprite = GetOrCreateHollowBorderSprite();
                m_SuccessBorderGlow.type = Image.Type.Sliced;
                m_SuccessBorderGlow.fillCenter = false;
                m_SuccessBorderGlow.raycastTarget = false;

                // Eliminar cualquier componente Outline que actúe como sombra asimétrica
                Outline outline = m_SuccessBorderGlow.GetComponent<Outline>();
                if (outline != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(outline);
                    }
                    else
                    {
                        DestroyImmediate(outline);
                    }
                }
            }
        }

        /// <summary>
        /// Genera procedimentalmente un sprite 9-slice de 16x16 con un marco exterior de 4px opaco
        /// y un interior de 8x8 transparente (Color.clear) para renderizar bordes perimetrales huecos.
        /// </summary>
        public static Sprite GetOrCreateHollowBorderSprite()
        {
            if (s_HollowBorderSprite != null) return s_HollowBorderSprite;

            int width = 16;
            int height = 16;
            int border = 4;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = "HUD_HollowBorder_Texture";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = (x < border || x >= width - border || y < border || y >= height - border);
                    pixels[y * width + x] = isBorder ? Color.white : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            s_HollowBorderSprite = Sprite.Create(
                tex,
                new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border)
            );
            s_HollowBorderSprite.name = "HUD_HollowBorder_Sprite";
            return s_HollowBorderSprite;
        }

        /// <summary>
        /// Reproduce el sonido de confirmación agradable (chime). Si no hay clip asignado,
        /// genera un chime armónico procedimental de dos tonos.
        /// </summary>
        /// <summary>
        /// Reproduce el sonido de confirmación agradable (chime) para notificar al conductor que la acción
        /// requerida fue completada con éxito y que debe volver a prestar atención al panel HUD sobre el volante.
        /// Utiliza el asset físico 'step_notification_chime.wav' configurado a máximo volumen y en 2D estéreo directo.
        /// </summary>
        public void PlayChimeSound()
        {
            if (m_AudioSource == null)
            {
                m_AudioSource = GetComponent<AudioSource>();
                if (m_AudioSource == null)
                {
                    m_AudioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (m_AudioSource != null)
            {
                m_AudioSource.playOnAwake = false;
                m_AudioSource.spatialBlend = 0f;
                m_AudioSource.volume = 1f;

                if (m_SuccessChimeClip == null)
                {
#if UNITY_EDITOR
                    m_SuccessChimeClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Lessons/step_notification_chime.wav");
#endif
                    if (m_SuccessChimeClip == null)
                    {
                        m_SuccessChimeClip = Resources.Load<AudioClip>("Audio/Lessons/step_notification_chime");
                    }
                }

                if (m_SuccessChimeClip != null)
                {
                    m_AudioSource.PlayOneShot(m_SuccessChimeClip, 1.0f);
                }
            }
        }

        /// <summary>
        /// Muestra la pantalla de finalización de la lección con felicitaciones
        /// y ofrece la opción de repetir la lección o continuar a la siguiente si existe.
        /// REGLA EXPLÍCITA: Permanece en completo silencio (NO reproduce ningún sonido ni fanfarria).
        /// </summary>
        public void DisplayCompletion(LessonDefinitionSO lesson)
        {
            // Silencio garantizado al finalizar la lección entera (sin chimes ni fanfarrias)
            if (m_AudioSource != null && m_AudioSource.isPlaying)
            {
                m_AudioSource.Stop();
            }

            // El fin de la lección es información nueva: se maximiza solo (antes de mostrar el panel de felicitaciones)
            m_WarningActive = false;
            SetMinimized(false);

            if (m_StepContentPanel != null) m_StepContentPanel.SetActive(false);
            if (m_CompletionPanel != null) m_CompletionPanel.SetActive(true);

            bool hasNextLesson = m_LessonManager != null && m_LessonManager.HasNextLesson;

            if (m_NextLessonButton != null)
            {
                m_NextLessonButton.gameObject.SetActive(hasNextLesson);
            }

            if (m_MainMenuButton != null)
            {
                m_MainMenuButton.gameObject.SetActive(true);
            }

            // Acomodar botones según disponibilidad de siguiente lección
            if (hasNextLesson)
            {
                // 3 botones: Reiniciar | Volver al Menú | Siguiente Lección
                if (m_RestartButton != null)
                {
                    RectTransform restartRt = m_RestartButton.GetComponent<RectTransform>();
                    if (restartRt != null)
                    {
                        restartRt.anchoredPosition = new Vector2(-180f, -195f);
                        restartRt.sizeDelta = new Vector2(165f, 46f);
                    }
                }

                if (m_MainMenuButton != null)
                {
                    RectTransform menuRt = m_MainMenuButton.GetComponent<RectTransform>();
                    if (menuRt != null)
                    {
                        menuRt.anchoredPosition = new Vector2(0f, -195f);
                        menuRt.sizeDelta = new Vector2(165f, 46f);
                    }
                }

                if (m_NextLessonButton != null)
                {
                    RectTransform nextRt = m_NextLessonButton.GetComponent<RectTransform>();
                    if (nextRt != null)
                    {
                        nextRt.anchoredPosition = new Vector2(180f, -195f);
                        nextRt.sizeDelta = new Vector2(165f, 46f);
                    }
                }
            }
            else
            {
                // 2 botones: Reiniciar | Volver al Menú
                if (m_RestartButton != null)
                {
                    RectTransform restartRt = m_RestartButton.GetComponent<RectTransform>();
                    if (restartRt != null)
                    {
                        restartRt.anchoredPosition = new Vector2(-95f, -195f);
                        restartRt.sizeDelta = new Vector2(180f, 46f);
                    }
                }

                if (m_MainMenuButton != null)
                {
                    RectTransform menuRt = m_MainMenuButton.GetComponent<RectTransform>();
                    if (menuRt != null)
                    {
                        menuRt.anchoredPosition = new Vector2(95f, -195f);
                        menuRt.sizeDelta = new Vector2(180f, 46f);
                    }
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
