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
        private bool m_Registered;
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
            if (InfractionReporting.Register(this))
            {
                m_Registered = true;
            }
            else
            {
                m_Registered = false;
                Debug.LogWarning("[InfractionManager] ⚠️ Ya hay otro InfractionManager habilitado; este no se registra como reporter.", this);
            }

            BindLessonManager(m_LessonManager);
        }

        private void OnDisable()
        {
            if (m_Registered) InfractionReporting.Unregister(this);
            m_Registered = false;

            BindLessonManager(null);
        }

        /// <summary>
        /// Se suscribe a <c>LessonStarted</c> del <see cref="LessonManager"/> indicado (o solo se desuscribe si es
        /// null). Al empezar una lección el registro se limpia y se guarda su <c>lessonId</c>.
        /// </summary>
        public void BindLessonManager(LessonManager lessonManager)
        {
            if (m_SubscribedLesson != null) m_SubscribedLesson.LessonStarted -= HandleLessonStarted;
            m_SubscribedLesson = null;

            if (lessonManager != null && isActiveAndEnabled)
            {
                m_SubscribedLesson = lessonManager;
                m_SubscribedLesson.LessonStarted += HandleLessonStarted;
            }
        }

        /// <summary>Asigna el catálogo en uso (para armar el gestor por código).</summary>
        public void SetCatalog(InfractionCatalogSO catalog)
        {
            m_Catalog = catalog;
        }

        private void HandleLessonStarted(LessonDefinitionSO lesson)
        {
            Clear();
            m_LessonId = lesson != null ? lesson.lessonId : null;
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
            if (m_SubscribedLesson != null)
            {
                lessonId = m_LessonId;
                if (m_SubscribedLesson.IsLessonActive && m_SubscribedLesson.ActiveLesson != null)
                {
                    lessonId = m_SubscribedLesson.ActiveLesson.lessonId;
                }

                if (lessonId != null) stepIndex = m_SubscribedLesson.CurrentStepIndex;
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

            RaiseRecorded(record);
        }

        /// <summary>Vacía los registros, pone los contadores en 0 y reinicia el tiempo transcurrido.</summary>
        public void Clear()
        {
            m_Records.Clear();
            m_MinorCount = 0;
            m_MajorCount = 0;
            m_NextSequence = 1;
            m_StartTime = Time.time;

            Action handlers = Cleared;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        // Cada suscriptor se invoca por separado: uno roto no impide que los demás reciban el evento.
        private void RaiseRecorded(InfractionRecord record)
        {
            Action<InfractionRecord> handlers = InfractionRecorded;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<InfractionRecord>)handler)(record);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }
    }
}
