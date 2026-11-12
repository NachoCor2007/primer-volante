using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Acciones "Reiniciar lección" y "Salir de la lección" del menú de pausa de las lecciones. Cada botón pide una confirmación
    /// (<c>ConfirmPanel</c>) antes de llamar al <see cref="LessonSceneFlowManager"/>, que recarga la escena o vuelve al menú
    /// principal con fade. Al confirmar, el menú queda bloqueado (<see cref="InGameMenuController.LockForTransition"/>), abierto y con
    /// la simulación pausada hasta que se descargue la escena. Va en el mismo GameObject que el <see cref="InGameMenuController"/>.
    /// Si la escena no tiene <see cref="LessonManager"/> (manejo libre) los dos botones quedan inactivos y no hace nada más.
    /// </summary>
    [DisallowMultipleComponent]
    public class LessonPauseActions : MonoBehaviour
    {
        public const string RESTART_TITLE = "¿Reiniciar la lección?";
        public const string RESTART_MESSAGE = "Vas a volver al primer paso. Se pierden el progreso y las infracciones de esta lección.";
        public const string RESTART_CONFIRM = "Reiniciar";
        public const string EXIT_TITLE = "¿Salir de la lección?";
        public const string EXIT_MESSAGE = "Vas a volver al menú principal. Se pierden el progreso y las infracciones de esta lección.";
        public const string EXIT_CONFIRM = "Salir";

        public enum PendingAction { None, Restart, Exit }

        [Header("Menú")]
        [Tooltip("Menú de pausa. Si se deja vacío se toma el del mismo GameObject.")]
        [SerializeField] private InGameMenuController m_Menu;

        [Tooltip("Panel principal del menú (se oculta mientras se pide la confirmación).")]
        [SerializeField] private GameObject m_MainPanel;

        [Header("Botones del panel principal")]
        [SerializeField] private Button m_RestartButton;
        [SerializeField] private Button m_ExitButton;

        [Header("Confirmación")]
        [SerializeField] private GameObject m_ConfirmPanel;
        [SerializeField] private TMP_Text m_ConfirmTitle;
        [SerializeField] private TMP_Text m_ConfirmMessage;
        [SerializeField] private Button m_ConfirmButton;
        [SerializeField] private TMP_Text m_ConfirmButtonLabel;
        [SerializeField] private Button m_CancelButton;

        private PendingAction m_Pending;
        private bool m_Active;
        private bool m_Subscribed;
        private bool m_WasLocked;

        public InGameMenuController Menu => m_Menu;
        public GameObject MainPanel => m_MainPanel;
        public Button RestartButton => m_RestartButton;
        public Button ExitButton => m_ExitButton;
        public GameObject ConfirmPanel => m_ConfirmPanel;
        public TMP_Text ConfirmTitle => m_ConfirmTitle;
        public TMP_Text ConfirmMessage => m_ConfirmMessage;
        public Button ConfirmButton => m_ConfirmButton;
        public TMP_Text ConfirmButtonLabel => m_ConfirmButtonLabel;
        public Button CancelButton => m_CancelButton;

        /// <summary>Acción que espera confirmación (None si no hay ninguna).</summary>
        public PendingAction Pending => m_Pending;

        /// <summary>true si la escena tiene LessonManager y las acciones están activas.</summary>
        public bool IsActive => m_Active;

        private void Awake()
        {
            if (m_Menu == null) m_Menu = GetComponent<InGameMenuController>();
        }

        private void OnEnable()
        {
            if (m_Menu == null) m_Menu = GetComponent<InGameMenuController>();

            m_Active = SceneHasLessonManager();
            if (!m_Active)
            {
                if (m_RestartButton != null) m_RestartButton.gameObject.SetActive(false);
                if (m_ExitButton != null) m_ExitButton.gameObject.SetActive(false);
                return;
            }

            if (m_RestartButton != null) m_RestartButton.gameObject.SetActive(true);
            if (m_ExitButton != null) m_ExitButton.gameObject.SetActive(true);

            Subscribe();
            RefreshInteractable();
        }

        private void OnDisable()
        {
            Unsubscribe();
            m_Pending = PendingAction.None;
            m_WasLocked = false;

            // Sin listeners la confirmación quedaría con botones muertos y se superpondría con MainPanel al reactivarse. Si la escena se
            // está descargando no se toca nada (los paneles se destruyen igual).
            if (gameObject.scene.isLoaded && m_ConfirmPanel != null && m_ConfirmPanel.activeSelf)
            {
                m_ConfirmPanel.SetActive(false);
                if (m_Menu != null && m_Menu.IsOpen) m_Menu.ShowMainPanel();
            }
        }

        private void Update()
        {
            // ResumeBlocked cambia sin evento (el monitor lo marca después de abrir el menú): se re-evalúa cada frame.
            if (m_Active) RefreshInteractable();
        }

        private bool SceneHasLessonManager()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<LessonManager>(true) != null) return true;
            }
            return false;
        }

        private void Subscribe()
        {
            if (m_Subscribed) return;
            m_Subscribed = true;

            if (m_RestartButton != null) m_RestartButton.onClick.AddListener(OnRestartClicked);
            if (m_ExitButton != null) m_ExitButton.onClick.AddListener(OnExitClicked);
            if (m_ConfirmButton != null) m_ConfirmButton.onClick.AddListener(OnConfirmClicked);
            if (m_CancelButton != null) m_CancelButton.onClick.AddListener(OnCancelClicked);

            if (m_Menu != null)
            {
                m_Menu.MenuOpened += HandleMenuOpened;
                m_Menu.MenuClosed += HandleMenuClosed;
                m_Menu.GameplayInputBlockingChanged += HandleBlockingChanged;
            }
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed) return;
            m_Subscribed = false;

            if (m_RestartButton != null) m_RestartButton.onClick.RemoveListener(OnRestartClicked);
            if (m_ExitButton != null) m_ExitButton.onClick.RemoveListener(OnExitClicked);
            if (m_ConfirmButton != null) m_ConfirmButton.onClick.RemoveListener(OnConfirmClicked);
            if (m_CancelButton != null) m_CancelButton.onClick.RemoveListener(OnCancelClicked);

            if (m_Menu != null)
            {
                m_Menu.MenuOpened -= HandleMenuOpened;
                m_Menu.MenuClosed -= HandleMenuClosed;
                m_Menu.GameplayInputBlockingChanged -= HandleBlockingChanged;
            }
        }

        // ------------------------------------------------------------------ Botones

        private void OnRestartClicked() => ShowConfirmation(PendingAction.Restart);

        private void OnExitClicked() => ShowConfirmation(PendingAction.Exit);

        private void ShowConfirmation(PendingAction action)
        {
            if (!m_Active || action == PendingAction.None) return;
            if (m_Menu != null && (m_Menu.IsLockedForTransition || m_Menu.IsBlockingGameplayInput || m_Menu.ResumeBlocked)) return;

            m_Pending = action;
            bool restart = action == PendingAction.Restart;
            if (m_ConfirmTitle != null) m_ConfirmTitle.text = restart ? RESTART_TITLE : EXIT_TITLE;
            if (m_ConfirmMessage != null) m_ConfirmMessage.text = restart ? RESTART_MESSAGE : EXIT_MESSAGE;
            if (m_ConfirmButtonLabel != null) m_ConfirmButtonLabel.text = restart ? RESTART_CONFIRM : EXIT_CONFIRM;

            if (m_MainPanel != null) m_MainPanel.SetActive(false);
            if (m_ConfirmPanel != null) m_ConfirmPanel.SetActive(true);
        }

        private void OnCancelClicked()
        {
            CloseConfirmation(true);
        }

        private void OnConfirmClicked()
        {
            if (m_Pending == PendingAction.None) return;
            if (m_Menu != null && m_Menu.IsLockedForTransition) return;

            PendingAction action = m_Pending;
            bool started = false;
            if (LessonSceneFlowManager.TryGetInstance(out LessonSceneFlowManager flow))
            {
                started = action == PendingAction.Restart ? flow.RestartCurrentLesson() : flow.ReturnToMainMenu();
            }

            if (started)
            {
                // El menú queda abierto y la simulación pausada durante el fade-out; la pausa la libera el OnDisable del menú
                // cuando se descarga la escena.
                if (m_Menu != null) m_Menu.LockForTransition();
                RefreshInteractable();
            }
            else
            {
                Debug.LogWarning($"[LessonPauseActions] No se pudo iniciar la transición ({action}): no hay LessonSceneFlowManager, ya hay una transición en curso o la escena activa no es una lección.");
                CloseConfirmation(true);
            }
        }

        // ------------------------------------------------------------------ Eventos del menú

        private void HandleMenuOpened() => CloseConfirmation(false);

        private void HandleMenuClosed() => CloseConfirmation(false);

        private void HandleBlockingChanged(bool blocking)
        {
            // El aviso de desconexión vive en MainPanel: si había una confirmación se vuelve al panel principal para que se vea.
            if (blocking) CloseConfirmation(true);
            RefreshInteractable();
        }

        /// <summary>Descarta la confirmación pendiente. Con <paramref name="showMainPanel"/> vuelve a mostrar el panel principal.</summary>
        private void CloseConfirmation(bool showMainPanel)
        {
            bool hadConfirmation = m_Pending != PendingAction.None || (m_ConfirmPanel != null && m_ConfirmPanel.activeSelf);
            m_Pending = PendingAction.None;
            if (m_ConfirmPanel != null) m_ConfirmPanel.SetActive(false);
            if (showMainPanel && hadConfirmation && m_Menu != null) m_Menu.ShowMainPanel();
        }

        /// <summary>
        /// Reiniciar y Salir quedan no interactuables mientras el menú bloquea Reanudar (pausa por desconexión VR) o hay una
        /// transición en curso; los botones de la confirmación, solo durante la transición.
        /// </summary>
        public void RefreshInteractable()
        {
            bool locked = m_Menu != null && m_Menu.IsLockedForTransition;

            // La transición terminó con este menú todavía vivo (el bloqueo se liberó solo): se vuelve al panel principal.
            if (m_WasLocked && !locked) CloseConfirmation(true);
            m_WasLocked = locked;

            bool blocked = m_Menu != null && (m_Menu.IsBlockingGameplayInput || m_Menu.ResumeBlocked);

            if (m_RestartButton != null) m_RestartButton.interactable = !locked && !blocked;
            if (m_ExitButton != null) m_ExitButton.interactable = !locked && !blocked;
            if (m_ConfirmButton != null) m_ConfirmButton.interactable = !locked;
            if (m_CancelButton != null) m_CancelButton.interactable = !locked;
        }
    }
}
