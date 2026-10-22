using PrimerVolante.Environment;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Alarma de los autos estacionados decorativos (US20). Escucha <see cref="ObstacleCollisionDetector.AnyImpact"/>:
    /// si el jugador choca (<see cref="ObstacleImpactSeverity.Crash"/>) un vehículo que no es un NPC (no tiene
    /// <see cref="NPCPathFollower"/>), mueve su fuente 3D al auto y reproduce la alarma en loop unos segundos, con
    /// ducking del ambiente. Una sola alarma a la vez: un choque nuevo la reinicia en el auto nuevo. Un único
    /// componente en la escena (<c>Audio_Environment</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public class ParkedCarAlarm : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("Perfil de las alertas (alarma, duración y distancias).")]
        [SerializeField] private EnvironmentAlertsProfile m_Profile;

        [Tooltip("Fuente 3D de la alarma (hija, grupo Alerts). Se mueve al auto chocado.")]
        [SerializeField] private AudioSource m_Source;

        [Tooltip("Segundos que suena la alarma. Si es 0 se usa el del perfil.")]
        [SerializeField] private float m_AlarmSeconds = 0f;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por alarma.")]
        [SerializeField] private bool m_LogEvents = false;

        private float m_EndTime = -1f;
        private ObstacleCollisionDetector m_CurrentCar;

        /// <summary>Perfil de alertas.</summary>
        public EnvironmentAlertsProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        /// <summary>Fuente 3D de la alarma.</summary>
        public AudioSource Source => m_Source;

        /// <summary>true si la alarma está sonando.</summary>
        public bool IsAlarmPlaying => m_Source != null && m_Source.isPlaying && m_EndTime >= 0f;

        /// <summary>Auto que tiene la alarma sonando, o <c>null</c>.</summary>
        public ObstacleCollisionDetector CurrentCar => IsAlarmPlaying ? m_CurrentCar : null;

        /// <summary>Segundos de duración efectivos de la alarma.</summary>
        public float AlarmSeconds => m_AlarmSeconds > 0f ? m_AlarmSeconds : (m_Profile != null ? m_Profile.AlarmSeconds : 8f);

        /// <summary>
        /// Crea (o reutiliza) el hijo con la <see cref="AudioSource"/> 3D de la alarma y la asigna al campo.
        /// </summary>
        public void BuildSource(AudioMixerGroup alertsGroup, Transform parent = null)
        {
            float min = m_Profile != null ? m_Profile.AlarmMinDistance : 5f;
            float max = m_Profile != null ? m_Profile.AlarmMaxDistance : 60f;
            m_Source = EnvironmentAudioUtil.CreateSource3D(parent != null ? parent : transform, "CarAlarmSource", alertsGroup, EnvironmentAudioUtil.PriorityAlert, min, max, true);
        }

        private void Awake()
        {
            if (m_Source == null) BuildSource(null);

            if (m_Profile != null)
            {
                m_Source.minDistance = m_Profile.AlarmMinDistance;
                m_Source.maxDistance = m_Profile.AlarmMaxDistance;
            }
        }

        private void OnEnable()
        {
            ObstacleCollisionDetector.AnyImpact += HandleImpact;
        }

        private void OnDisable()
        {
            ObstacleCollisionDetector.AnyImpact -= HandleImpact;
            StopAlarm();
        }

        private void HandleImpact(ObstacleImpact impact)
        {
            if (impact.Kind != ObstacleKind.Vehicle || impact.Severity != ObstacleImpactSeverity.Crash) return;
            if (impact.Obstacle == null) return;

            // Un NPC (con ruta) no dispara alarma: solo los autos decorativos estacionados.
            if (impact.Obstacle.GetComponent<NPCPathFollower>() != null) return;

            StartAlarm(impact.Obstacle);
        }

        /// <summary>
        /// Inicia la alarma en un auto. Si ya sonaba en otro, la reinicia en este.
        /// </summary>
        public void StartAlarm(ObstacleCollisionDetector car)
        {
            if (m_Profile == null || m_Source == null || car == null) return;

            AudioClip clip = m_Profile.CarAlarmLoop;
            if (clip == null) return;

            m_CurrentCar = car;
            m_Source.Stop();
            m_Source.transform.position = car.transform.position;
            m_Source.clip = clip;
            m_Source.loop = true;
            m_Source.volume = m_Profile.AlarmVolume;
            m_Source.Play();

            m_EndTime = Time.time + AlarmSeconds;
            AmbienceDirector.Duck(m_Profile.AlarmDuckDb, AlarmSeconds);

            if (m_LogEvents) Debug.Log($"[ParkedCarAlarm] 🚨 Alarma en '{car.name}' durante {AlarmSeconds:F1} s.", this);
        }

        /// <summary>Detiene la alarma.</summary>
        public void StopAlarm()
        {
            m_EndTime = -1f;
            m_CurrentCar = null;
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();
        }

        private void Update()
        {
            if (m_EndTime < 0f || m_Source == null) return;

            if (Time.time >= m_EndTime)
            {
                StopAlarm();
                return;
            }

            // Se apaga con un fade corto para no cortar de golpe.
            float remaining = m_EndTime - Time.time;
            m_Source.volume = m_Profile.AlarmVolume * Mathf.Clamp01(remaining / 0.4f);
        }
    }
}
