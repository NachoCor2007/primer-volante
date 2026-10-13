using System;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Orquestador principal del flujo de lecciones del simulador "Primer Volante".
    /// Coordina el paso activo, notifica al panel HUD, ejecuta el bloqueo selectivo
    /// de mandos de cabina mediante CockpitControlLockManager y resalta interactuables
    /// a través de InteractableHighlighter.
    /// </summary>
    public class LessonManager : MonoBehaviour
    {
        [Header("Configuración de Lección")]
        [Tooltip("Definición ScriptableObject de la lección activa.")]
        [SerializeField] private LessonDefinitionSO m_ActiveLesson;

        [Tooltip("Iniciar automáticamente la lección al inicio (Start).")]
        [SerializeField] private bool m_AutoStartOnStart = true;

        [Header("Subsistemas")]
        [Tooltip("Gestor de bloqueo selectivo de interactuables de cabina.")]
        [SerializeField] private CockpitControlLockManager m_LockManager;

        [Tooltip("Componente para resaltar mandos con brillo pulsante.")]
        [SerializeField] private InteractableHighlighter m_Highlighter;

        [Tooltip("Panel HUD situado sobre el volante.")]
        [SerializeField] private LessonHUDPanel m_HUDPanel;

        [Tooltip("Transform raíz del vehículo (para búsqueda contextual de interactuables).")]
        [SerializeField] private Transform m_VehicleRoot;

        private int m_CurrentStepIndex = -1;
        private bool m_IsLessonActive = false;
        private bool m_IsLessonCompleted = false;

        public LessonDefinitionSO ActiveLesson => m_ActiveLesson;
        public int CurrentStepIndex => m_CurrentStepIndex;
        public LessonStepSO CurrentStep =>
            (m_ActiveLesson != null && m_CurrentStepIndex >= 0 && m_CurrentStepIndex < m_ActiveLesson.steps.Count)
                ? m_ActiveLesson.steps[m_CurrentStepIndex]
                : null;
        public bool IsLessonActive => m_IsLessonActive;
        public bool IsLessonCompleted => m_IsLessonCompleted;

        public CockpitControlLockManager LockManager => m_LockManager;
        public InteractableHighlighter Highlighter => m_Highlighter;
        public LessonHUDPanel HUDPanel => m_HUDPanel;

        public event Action<LessonDefinitionSO> LessonStarted;
        public event Action<LessonStepSO, int, int> StepChanged;
        public event Action<LessonDefinitionSO> LessonCompleted;

        private void Awake()
        {
            if (m_LockManager == null)
            {
                m_LockManager = GetComponentInChildren<CockpitControlLockManager>() ??
                                UnityEngine.Object.FindAnyObjectByType<CockpitControlLockManager>();
            }

            if (m_Highlighter == null)
            {
                m_Highlighter = GetComponentInChildren<InteractableHighlighter>() ??
                                UnityEngine.Object.FindAnyObjectByType<InteractableHighlighter>();
            }

            if (m_HUDPanel == null)
            {
                m_HUDPanel = GetComponentInChildren<LessonHUDPanel>() ??
                             UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            }

            if (m_VehicleRoot == null)
            {
                VehicleController vc = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
                if (vc != null) m_VehicleRoot = vc.transform;
            }
        }

        private void Start()
        {
            if (m_AutoStartOnStart && m_ActiveLesson != null)
            {
                StartLesson(m_ActiveLesson);
            }
        }

        /// <summary>
        /// Inicia una nueva lección, reseteando el progreso al primer paso.
        /// </summary>
        public void StartLesson(LessonDefinitionSO lesson)
        {
            if (lesson == null)
            {
                Debug.LogError("[LessonManager] Intento de iniciar una lección nula.");
                return;
            }

            m_ActiveLesson = lesson;
            m_IsLessonActive = true;
            m_IsLessonCompleted = false;
            m_CurrentStepIndex = -1;

            if (m_HUDPanel != null)
            {
                m_HUDPanel.BindToLessonManager(this);
            }

            LessonStarted?.Invoke(m_ActiveLesson);
            GoToStep(0);
        }

        /// <summary>
        /// Avanza la lección al paso con el índice especificado.
        /// </summary>
        public void GoToStep(int stepIndex)
        {
            if (m_ActiveLesson == null || m_ActiveLesson.steps == null || m_ActiveLesson.steps.Count == 0)
            {
                Debug.LogWarning("[LessonManager] La lección no contiene pasos definidos.");
                return;
            }

            if (stepIndex >= m_ActiveLesson.steps.Count)
            {
                CompleteLesson();
                return;
            }

            m_CurrentStepIndex = stepIndex;
            LessonStepSO step = m_ActiveLesson.steps[m_CurrentStepIndex];

            // 1. Aplicar bloqueo selectivo de controles de cabina
            if (m_LockManager != null)
            {
                m_LockManager.ApplyAllowedControls(step.allowedControls);
            }

            // 2. Gestionar resaltado visual de interactuable objetivo
            if (m_Highlighter != null)
            {
                m_Highlighter.ClearHighlight();
                if (!string.IsNullOrWhiteSpace(step.highlightTargetName))
                {
                    m_Highlighter.HighlightByName(step.highlightTargetName, m_VehicleRoot);
                }
            }

            // 3. Notificar al HUD y suscriptores
            if (m_HUDPanel != null)
            {
                m_HUDPanel.DisplayStep(step, m_CurrentStepIndex, m_ActiveLesson.steps.Count);
            }

            StepChanged?.Invoke(step, m_CurrentStepIndex, m_ActiveLesson.steps.Count);
        }

        /// <summary>
        /// Avanza automáticamente al siguiente paso o finaliza la lección si era el último.
        /// </summary>
        public void CompleteCurrentStep()
        {
            if (!m_IsLessonActive || m_IsLessonCompleted) return;
            GoToStep(m_CurrentStepIndex + 1);
        }

        /// <summary>
        /// Marca la lección como completada, desbloquea controles y muestra felicitaciones en el HUD.
        /// </summary>
        public void CompleteLesson()
        {
            m_IsLessonActive = false;
            m_IsLessonCompleted = true;

            if (m_Highlighter != null)
            {
                m_Highlighter.ClearHighlight();
            }

            if (m_HUDPanel != null)
            {
                m_HUDPanel.DisplayCompletion(m_ActiveLesson);
            }

            LessonCompleted?.Invoke(m_ActiveLesson);
        }
    }
}
