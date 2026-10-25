using System;
using System.Collections.Generic;
using PrimerVolante.Lessons;
using UnityEngine;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Repositorio central de infracciones (US22). Recibe las faltas por <see cref="IInfractionReporter"/>
    /// (a través de <see cref="InfractionReporting"/>), las busca en el <see cref="InfractionCatalogSO"/>, las
    /// guarda como <see cref="InfractionRecord"/> y lleva contadores independientes de faltas leves y graves. No
    /// interrumpe la simulación: no toca <c>Time.timeScale</c>, no frena el auto y no lanza hacia el detector.
    /// Solo guarda y avisa: la UI (tablero US24, reporte US26) lee el registro por <see cref="IInfractionLog"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class InfractionManager : MonoBehaviour, IInfractionReporter, IInfractionLog
    {
        [Header("Catálogo")]
        [Tooltip("Catálogo con las definiciones de infracción (ids, títulos, plantillas y gravedad).")]
        [SerializeField] private InfractionCatalogSO m_Catalog;

        [Header("Lección (opcional)")]
        [Tooltip("LessonManager de la escena. Si queda vacío se busca en Awake; puede no existir.")]
        [SerializeField] private LessonManager m_LessonManager;

        [Header("Depuración")]
        [Tooltip("Escribe un log por cada infracción registrada.")]
        [SerializeField] private bool m_LogEvents = true;

        private readonly List<InfractionRecord> m_Records = new List<InfractionRecord>();
        private IReadOnlyList<InfractionRecord> m_RecordsView;
        private int m_MinorCount;
        private int m_MajorCount;
        private int m_NextSequence = 1;
        private float m_StartTime;
        private string m_LessonId;
        private LessonManager m_SubscribedLesson;

        /// <inheritdoc />
        public IReadOnlyList<InfractionRecord> Records => m_RecordsView ??= m_Records.AsReadOnly();

        /// <inheritdoc />
        public int MinorCount => m_MinorCount;

        /// <inheritdoc />
        public int MajorCount => m_MajorCount;

        /// <inheritdoc />
        public int TotalCount => m_MinorCount + m_MajorCount;

        /// <inheritdoc />
        public event Action<InfractionRecord> InfractionRecorded;

        /// <inheritdoc />
        public event Action Cleared;

        /// <summary>Catálogo de definiciones en uso.</summary>
        public InfractionCatalogSO Catalog => m_Catalog;

        /// <summary>Id de la lección activa (null si no hay <see cref="LessonManager"/> o no empezó).</summary>
        public string LessonId => m_LessonId;

        private void Awake()
        {
            if (m_LessonManager == null) m_LessonManager = FindAnyObjectByType<LessonManager>();
            m_StartTime = Time.time;
        }

        private void OnEnable()
        {
            // Si ya hay otro, este queda en espera y toma el lugar cuando el otro se deshabilite.
            if (!InfractionReporting.Register(this))
            {
                Debug.LogWarning("[InfractionManager] ⚠️ Ya hay otro InfractionManager habilitado; este queda en espera y no recibe infracciones.", this);
            }

            SubscribeToLesson(m_LessonManager);
        }

        private void OnDisable()
        {
            InfractionReporting.Unregister(this);
            SubscribeToLesson(null);
        }

        /// <summary>
        /// Asigna el <see cref="LessonManager"/> (o ninguno, con null) y, si este componente está habilitado, se
        /// suscribe a su <c>LessonStarted</c>: al empezar una lección el registro se limpia y se guarda su
        /// <c>lessonId</c>. Queda guardado, así que se vuelve a suscribir al rehabilitarse.
        /// </summary>
        public void BindLessonManager(LessonManager lessonManager)
        {
            m_LessonManager = lessonManager;
            SubscribeToLesson(isActiveAndEnabled ? lessonManager : null);
        }

        private void SubscribeToLesson(LessonManager lessonManager)
        {
            if (m_SubscribedLesson != null) m_SubscribedLesson.LessonStarted -= HandleLessonStarted;
            m_SubscribedLesson = lessonManager;
            if (m_SubscribedLesson != null) m_SubscribedLesson.LessonStarted += HandleLessonStarted;
        }

        /// <summary>Asigna el catálogo en uso (para armar el gestor por código).</summary>
        public void SetCatalog(InfractionCatalogSO catalog)
        {
            m_Catalog = catalog;
        }

        private void HandleLessonStarted(LessonDefinitionSO lesson)
        {
            // Primero el id: quien escuche Cleared ya lee el id de la lección nueva.
            m_LessonId = lesson != null ? lesson.lessonId : null;
            Clear();
        }

        /// <inheritdoc />
        public void Report(in InfractionReport report)
        {
            InfractionDefinitionSO definition = null;
            if (m_Catalog == null || !m_Catalog.TryGet(report.InfractionId, out definition))
            {
                Debug.LogWarning($"[InfractionManager] ⚠️ Infracción '{report.InfractionId}' sin definición en el catálogo; no se registra.", this);
                return;
            }

            string description = InfractionTextFormatter.Format(definition.DescriptionTemplate, report.Parameters);
            string sourceName = report.Source != null ? report.Source.name : null;

            // Contexto de la lección: si no hay LessonManager (o no hay lección activa) queda null y -1.
            string lessonId = null;
            int stepIndex = -1;
            if (m_SubscribedLesson != null && m_SubscribedLesson.IsLessonActive && m_SubscribedLesson.ActiveLesson != null)
            {
                lessonId = m_SubscribedLesson.ActiveLesson.lessonId;
                stepIndex = m_SubscribedLesson.CurrentStepIndex;
            }

            var record = new InfractionRecord(
                m_NextSequence++, definition, description, report.Parameters,
                Time.time, Time.time - m_StartTime, report.Position, sourceName,
                lessonId, stepIndex);

            m_Records.Add(record);
            if (record.Severity == InfractionSeverity.Major) m_MajorCount++;
            else m_MinorCount++;

            if (m_LogEvents)
            {
                string severity = record.Severity == InfractionSeverity.Major ? "Grave" : "Leve";
                Debug.Log($"[InfractionManager] 🚨 #{record.Sequence} [{severity}] {record.ShortTitle}: {record.Description} ({sourceName ?? "sin fuente"})", this);
            }

            SafeInvoke(InfractionRecorded, handler => ((Action<InfractionRecord>)handler)(record));
        }

        /// <summary>Vacía los registros, pone los contadores en 0 y reinicia el tiempo transcurrido.</summary>
        public void Clear()
        {
            m_Records.Clear();
            m_MinorCount = 0;
            m_MajorCount = 0;
            m_NextSequence = 1;
            m_StartTime = Time.time;

            SafeInvoke(Cleared, handler => ((Action)handler)());
        }

        // Cada suscriptor se invoca por separado: uno roto no impide que los demás reciban el evento.
        private void SafeInvoke(Delegate handlers, Action<Delegate> invoke)
        {
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    invoke(handler);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }
    }
}
