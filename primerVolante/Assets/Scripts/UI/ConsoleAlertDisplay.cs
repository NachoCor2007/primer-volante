using System.Collections.Generic;
using PrimerVolante.Evaluation;
using TMPro;
using UnityEngine;

namespace PrimerVolante.UI
{
    /// <summary>
    /// Aviso diegético de infracción en la pantalla del GPS (US24). Cada vez que el <see cref="InfractionManager"/>
    /// registra una falta, muestra su título corto (<see cref="InfractionRecord.ShortTitle"/>) durante
    /// <see cref="m_DisplayDuration"/> segundos sobre la barra superior del GPS, con una barra de tiempo que se vacía.
    /// Si llegan varias infracciones juntas se encolan y se muestran una tras otra, 3 s cada una.
    /// No suena: el sonido de la infracción es del testigo del tablero (<c>VehicleInfractionAudio</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public class ConsoleAlertDisplay : MonoBehaviour
    {
        private const string FALLBACK_TITLE = "Infracción";

        [Header("Aviso")]
        [Tooltip("Overlay del aviso (Infraction_Alert). Se activa mientras hay una infracción visible y se desactiva al terminar.")]
        [SerializeField] private GameObject m_AlertRoot;

        [Tooltip("Texto donde se escribe el título corto de la infracción.")]
        [SerializeField] private TMP_Text m_TitleText;

        [Tooltip("Relleno de la barra de tiempo. Su anchorMax.x baja de 1 a 0 mientras corre el aviso (arranca llena y se vacía).")]
        [SerializeField] private RectTransform m_ProgressFill;

        [Header("Tiempos")]
        [Tooltip("Segundos que se muestra cada infracción. Cada infracción de la cola reinicia el tiempo.")]
        [SerializeField] private float m_DisplayDuration = 3f;

        private readonly Queue<string> m_Queue = new Queue<string>();
        private IInfractionLog m_SubscribedLog;
        private string m_CurrentTitle;
        private float m_Remaining;

        /// <summary>True mientras hay una infracción visible.</summary>
        public bool IsShowing => m_Remaining > 0f;

        /// <summary>Título que se está mostrando, o null si el aviso está oculto.</summary>
        public string CurrentTitle => IsShowing ? m_CurrentTitle : null;

        /// <summary>Cantidad de infracciones esperando su turno (sin contar la que se está mostrando).</summary>
        public int QueuedCount => m_Queue.Count;

        /// <summary>Fracción de tiempo que le queda a la infracción actual: 1 recién mostrada, 0 al terminar u oculta.</summary>
        public float RemainingFraction => IsShowing ? Mathf.Clamp01(m_Remaining / DisplayDuration) : 0f;

        private float DisplayDuration => Mathf.Max(m_DisplayDuration, Mathf.Epsilon);

        private void OnEnable()
        {
            HideNow();
            RefreshInfractionLogSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromInfractionLog();
            m_Queue.Clear();
            HideNow();
        }

        private void OnDestroy()
        {
            UnsubscribeFromInfractionLog();
        }

        private void Update()
        {
            RefreshInfractionLogSubscription();

            if (!IsShowing) return;

            m_Remaining -= Time.deltaTime;
            if (m_Remaining > 0f)
            {
                UpdateProgress();
                return;
            }

            if (m_Queue.Count > 0)
                ShowNext();
            else
                HideNow();
        }

        /// <summary>
        /// Encola un título como si viniera del registro de infracciones. Si no hay nada visible, se muestra de
        /// inmediato. Es público para poder probar el aviso sin gestor de infracciones.
        /// </summary>
        public void Show(string shortTitle)
        {
            m_Queue.Enqueue(string.IsNullOrWhiteSpace(shortTitle) ? FALLBACK_TITLE : shortTitle);
            if (!IsShowing) ShowNext();
        }

        private void ShowNext()
        {
            m_CurrentTitle = m_Queue.Dequeue();
            m_Remaining = DisplayDuration;

            if (m_TitleText != null) m_TitleText.text = m_CurrentTitle;
            if (m_AlertRoot != null) m_AlertRoot.SetActive(true);
            UpdateProgress();
        }

        private void HideNow()
        {
            m_Remaining = 0f;
            m_CurrentTitle = null;
            if (m_AlertRoot != null) m_AlertRoot.SetActive(false);
        }

        // La barra se acorta con anchorMax.x (no con Image.Type.Filled, que sin sprite no recorta).
        private void UpdateProgress()
        {
            if (m_ProgressFill == null) return;

            m_ProgressFill.anchorMin = Vector2.zero;
            m_ProgressFill.anchorMax = new Vector2(RemainingFraction, 1f);
        }

        /// <summary>
        /// Mantiene la suscripción al registro del gestor de infracciones activo. El gestor puede habilitarse
        /// después que este componente o ser reemplazado por otro en espera, así que se compara una referencia
        /// por frame en lugar de asumir el orden de OnEnable (mismo patrón que <c>DashboardUIController</c>).
        /// </summary>
        private void RefreshInfractionLogSubscription()
        {
            IInfractionLog log = InfractionReporting.Log;
            if (ReferenceEquals(log, m_SubscribedLog)) return;

            UnsubscribeFromInfractionLog();
            if (log == null) return;

            m_SubscribedLog = log;
            m_SubscribedLog.InfractionRecorded += OnInfractionRecorded;
            m_SubscribedLog.Cleared += OnInfractionsCleared;
        }

        private void UnsubscribeFromInfractionLog()
        {
            if (m_SubscribedLog == null) return;

            m_SubscribedLog.InfractionRecorded -= OnInfractionRecorded;
            m_SubscribedLog.Cleared -= OnInfractionsCleared;
            m_SubscribedLog = null;
        }

        private void OnInfractionRecorded(InfractionRecord record)
        {
            Show(record?.ShortTitle);
        }

        private void OnInfractionsCleared()
        {
            m_Queue.Clear();
            HideNow();
        }
    }
}
