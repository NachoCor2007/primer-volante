using PrimerVolante.Environment;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Semáforo sonoro para peatones (US20). Va junto a cada <see cref="TrafficLightController"/>: con el semáforo
    /// vehicular en rojo (los peatones cruzan) toca un beep rápido; en verde o amarillo, un tick localizador lento.
    /// Suena por el grupo Alerts y no pide ducking (es informativo, no presiona). Con clips nulos no hace nada.
    /// </summary>
    [DisallowMultipleComponent]
    public class CrosswalkBeeper : MonoBehaviour
    {
        // Holgura sobre la distancia máxima antes de dejar de sonar.
        private const float k_CullFactor = 1.1f;

        [Header("Semáforo")]
        [Tooltip("Semáforo vehicular que gobierna el cruce. Si está vacío se busca en este GameObject.")]
        [SerializeField] private TrafficLightController m_TrafficLight;

        [Header("Audio")]
        [Tooltip("Perfil de las alertas (tick, beep, intervalos y distancias).")]
        [SerializeField] private EnvironmentAlertsProfile m_Profile;

        [Tooltip("Fuente 3D del beeper (hija, grupo Alerts).")]
        [SerializeField] private AudioSource m_Source;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por cambio de estado.")]
        [SerializeField] private bool m_LogEvents = false;

        private TrafficLightState m_State;
        private float m_NextSoundTime;

        /// <summary>Perfil de alertas.</summary>
        public EnvironmentAlertsProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        /// <summary>Semáforo asociado.</summary>
        public TrafficLightController TrafficLight
        {
            get => m_TrafficLight;
            set => m_TrafficLight = value;
        }

        /// <summary>Fuente 3D del beeper.</summary>
        public AudioSource Source => m_Source;

        /// <summary>
        /// Crea (o reutiliza) el hijo con la <see cref="AudioSource"/> 3D del beeper y la asigna al campo.
        /// </summary>
        public void BuildSource(AudioMixerGroup alertsGroup)
        {
            float min = m_Profile != null ? m_Profile.CrosswalkMinDistance : 3f;
            float max = m_Profile != null ? m_Profile.CrosswalkMaxDistance : 25f;
            m_Source = EnvironmentAudioUtil.CreateSource3D(transform, "CrosswalkBeeperSource", alertsGroup, EnvironmentAudioUtil.PriorityAlert, min, max);
        }

        private void Awake()
        {
            if (m_TrafficLight == null) m_TrafficLight = GetComponent<TrafficLightController>();
            if (m_Source == null) BuildSource(null);

            if (m_Profile != null)
            {
                m_Source.minDistance = m_Profile.CrosswalkMinDistance;
                m_Source.maxDistance = m_Profile.CrosswalkMaxDistance;
            }
        }

        private void OnEnable()
        {
            if (m_TrafficLight == null) return;

            m_TrafficLight.OnStateChanged += HandleStateChanged;

            // Estado inicial: el semáforo pudo arrancar antes que este componente.
            m_State = m_TrafficLight.CurrentState;
            m_NextSoundTime = 0f;
        }

        private void OnDisable()
        {
            if (m_TrafficLight != null) m_TrafficLight.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(TrafficLightState state)
        {
            m_State = state;
            m_NextSoundTime = 0f; // cambia la cadencia: el próximo sonido es inmediato
            if (m_LogEvents) Debug.Log($"[CrosswalkBeeper] 🚦 {name}: {state} ({(state == TrafficLightState.Red ? "beep rápido" : "tick lento")}).", this);
        }

        private void Update()
        {
            if (m_Profile == null || m_Source == null) return;
            if (Time.time < m_NextSoundTime) return;
            if (!EnvironmentAudioUtil.TryGetListenerPosition(out Vector3 listenerPos)) return;

            float distance = Vector3.Distance(listenerPos, transform.position);
            if (distance > m_Profile.CrosswalkMaxDistance * k_CullFactor) return;

            // Rojo vehicular: los peatones cruzan, beep rápido. Verde o amarillo: tick localizador lento.
            bool pedestriansCross = m_State == TrafficLightState.Red;
            AudioClip clip = pedestriansCross ? m_Profile.CrosswalkBeep : m_Profile.CrosswalkTick;
            float interval = pedestriansCross ? m_Profile.BeepInterval : m_Profile.TickInterval;

            m_NextSoundTime = Time.time + interval;
            if (clip == null) return;

            m_Source.PlayOneShot(clip, m_Profile.CrosswalkVolume * AmbienceMath.EdgeFade(distance, m_Profile.CrosswalkMaxDistance));
        }
    }
}
