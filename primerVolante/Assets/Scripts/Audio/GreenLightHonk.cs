using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Bocina de impaciencia detrás del jugador (US20): si el auto del jugador está dentro de la zona de la línea de
    /// detención, el semáforo lleva más de <c>delay</c> en verde y el jugador sigue (casi) quieto, suena una bocina
    /// desde unos 8 m detrás del auto (fuente 3D, grupo Alerts). Repite cada 3–4 s, primero corta y después larga,
    /// hasta 3 veces. Se resetea al salir de la zona o al ponerse el semáforo en rojo. La demora depende de la
    /// intensidad del <see cref="AmbienceDirector"/> (Calm: desactivada, Normal 4 s, Tense 2 s).
    /// </summary>
    /// <remarks>
    /// Reutiliza el trigger de <c>StopLine_Trigger</c> sin modificar <see cref="RedLightViolationDetector"/>: como los
    /// mensajes de trigger llegan solo al GameObject del collider, aquí se consulta la caja con
    /// <see cref="Physics.OverlapBoxNonAlloc(Vector3, Vector3, Collider[], Quaternion, int, QueryTriggerInteraction)"/> y se usa
    /// el mismo criterio de vehículo (un <see cref="VehicleController"/> en el collider o sus padres).
    /// </remarks>
    [DisallowMultipleComponent]
    public class GreenLightHonk : MonoBehaviour
    {
        // Segundos entre consultas de la caja (no hace falta cada frame).
        private const float k_PollInterval = 0.2f;

        [Header("Semáforo y línea de detención")]
        [Tooltip("Semáforo de la intersección.")]
        [SerializeField] private TrafficLightController m_TrafficLight;

        [Tooltip("Collider trigger de la línea de detención (el de StopLine_Trigger). Debe ser un BoxCollider.")]
        [SerializeField] private BoxCollider m_StopLineTrigger;

        [Header("Audio")]
        [Tooltip("Perfil de las alertas (bocinas, volumen y distancias).")]
        [SerializeField] private EnvironmentAlertsProfile m_Profile;

        [Tooltip("Fuente 3D de la bocina (hija, grupo Alerts). Se ubica detrás del auto antes de sonar.")]
        [SerializeField] private AudioSource m_Source;

        [Header("Condiciones")]
        [Tooltip("Velocidad (km/h) por debajo de la cual el jugador se considera detenido.")]
        [SerializeField] private float m_MaxStopSpeedKmh = 1f;

        [Tooltip("Segundos mínimos entre bocinas.")]
        [SerializeField] private float m_RepeatMinSeconds = 3f;

        [Tooltip("Segundos máximos entre bocinas.")]
        [SerializeField] private float m_RepeatMaxSeconds = 4f;

        [Tooltip("Máximo de bocinas por episodio.")]
        [SerializeField, Min(1)] private int m_MaxHonks = 3;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por bocina y por reseteo.")]
        [SerializeField] private bool m_LogEvents = false;

        private readonly Collider[] m_Hits = new Collider[16];
        private System.Random m_Random;
        private float m_GreenSince;
        private float m_NextPollTime;
        private float m_NextHonkTime;
        private int m_HonkCount;
        private VehicleController m_Player;

        /// <summary>Bocinas tocadas en el episodio actual.</summary>
        public int HonkCount => m_HonkCount;

        /// <summary>El auto del jugador detectado dentro de la zona, o <c>null</c>.</summary>
        public VehicleController PlayerInside => m_Player;

        /// <summary>Fuente 3D de la bocina.</summary>
        public AudioSource Source => m_Source;

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

        /// <summary>Trigger de la línea de detención.</summary>
        public BoxCollider StopLineTrigger
        {
            get => m_StopLineTrigger;
            set => m_StopLineTrigger = value;
        }

        /// <summary>
        /// Crea (o reutiliza) el hijo con la <see cref="AudioSource"/> 3D de la bocina y la asigna al campo.
        /// </summary>
        public void BuildSource(AudioMixerGroup alertsGroup)
        {
            float min = m_Profile != null ? m_Profile.HornMinDistance : 5f;
            float max = m_Profile != null ? m_Profile.HornMaxDistance : 60f;
            m_Source = EnvironmentAudioUtil.CreateSource3D(transform, "GreenLightHonkSource", alertsGroup, EnvironmentAudioUtil.PriorityAlert, min, max);
        }

        private void Awake()
        {
            m_Random = new System.Random(EnvironmentAudioUtil.SeedFromPosition(transform.position) ^ 0x6A11);
            if (m_Source == null) BuildSource(null);

            if (m_Profile != null)
            {
                m_Source.minDistance = m_Profile.HornMinDistance;
                m_Source.maxDistance = m_Profile.HornMaxDistance;
            }
        }

        private void OnEnable()
        {
            if (m_TrafficLight == null) return;

            m_TrafficLight.OnStateChanged += HandleStateChanged;
            m_GreenSince = Time.time;
            ResetEpisode();
        }

        private void OnDisable()
        {
            if (m_TrafficLight != null) m_TrafficLight.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(TrafficLightState state)
        {
            // Cada cambio de estado empieza un episodio nuevo; el reloj del verde arranca al ponerse en verde.
            m_GreenSince = Time.time;
            ResetEpisode();
        }

        private void ResetEpisode()
        {
            if (m_HonkCount > 0 && m_LogEvents) Debug.Log("[GreenLightHonk] 🔄 Reseteo del episodio.", this);
            m_HonkCount = 0;
            m_NextHonkTime = 0f;
            m_Player = null;
        }

        private void Update()
        {
            if (m_TrafficLight == null || m_StopLineTrigger == null || m_Profile == null) return;

            // Con el semáforo en rojo o amarillo no hay nada que tocar.
            if (!m_TrafficLight.IsGreen)
            {
                if (m_HonkCount > 0 || m_Player != null) ResetEpisode();
                return;
            }

            if (Time.time >= m_NextPollTime)
            {
                m_NextPollTime = Time.time + k_PollInterval;
                VehicleController found = FindPlayerInside();

                // Salió de la zona: el episodio se resetea.
                if (found == null && m_Player != null) ResetEpisode();
                m_Player = found;
            }

            if (m_Player == null) return;

            float delay = AmbienceDirector.CurrentGreenHonkDelay;
            float greenElapsed = Time.time - m_GreenSince;
            if (!NPCAudioMath.ShouldImpatientHonk(greenElapsed, m_Player.CurrentSpeedKmh, delay, m_MaxStopSpeedKmh)) return;
            if (m_HonkCount >= m_MaxHonks || Time.time < m_NextHonkTime) return;

            Honk(m_Player);
        }

        // Mismo criterio que RedLightViolationDetector: un VehicleController en el collider o sus padres.
        private VehicleController FindPlayerInside()
        {
            Transform t = m_StopLineTrigger.transform;
            Vector3 center = t.TransformPoint(m_StopLineTrigger.center);
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(m_StopLineTrigger.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

            int count = Physics.OverlapBoxNonAlloc(center, half, m_Hits, t.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = m_Hits[i];
                if (hit == null) continue;

                VehicleController vehicle = hit.GetComponentInParent<VehicleController>();
                if (vehicle != null) return vehicle;
            }

            return null;
        }

        private void Honk(VehicleController player)
        {
            // Primero corta y después larga.
            AudioClip clip = m_HonkCount == 0 ? m_Profile.HornShort : m_Profile.HornLong;

            Transform car = player.transform;
            Vector3 position = car.position - car.forward * m_Profile.HornBehindDistance + Vector3.up;
            m_Source.transform.position = position;

            if (clip != null) PrimerVolante.VR.VehicleAudioUtil.PlayOneShot(m_Source, clip, m_Profile.HornVolume);
            AmbienceDirector.Duck(m_Profile.HornDuckDb, m_Profile.HornDuckHoldSeconds);

            m_HonkCount++;
            m_NextHonkTime = Time.time + EnvironmentAudioUtil.Range(m_Random, m_RepeatMinSeconds, m_RepeatMaxSeconds);

            if (m_LogEvents) Debug.Log($"[GreenLightHonk] 📯 Bocina #{m_HonkCount} ({(clip != null ? clip.name : "sin clip")}) detrás del jugador.", this);
        }
    }
}
