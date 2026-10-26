using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Chime de dos tonos del testigo de infracción del tablero. Escucha
    /// <see cref="DashboardUIController.InfractionWarningTriggered"/> (no el registro de infracciones
    /// directamente), así testigo y sonido siempre coinciden. Va en el mismo GameObject que
    /// <c>Dashboard_Cluster_Canvas</c>: la fuente queda en el tablero, delante del conductor.
    /// El cooldown mínimo entre sonidos vive acá (<see cref="VehicleAudioProfile.InfractionChimeCooldown"/>).
    /// </summary>
    public class VehicleInfractionAudio : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Tablero cuyo evento dispara el chime. Si se deja vacío se autodetecta en este GameObject.")]
        [SerializeField] private DashboardUIController m_Dashboard;
        [SerializeField] private VehicleAudioProfile m_Profile;

        [Header("Fuente")]
        [SerializeField] private AudioSource m_Source;

        [Tooltip("0 = 2D (suena igual de fuerte desde cualquier posición, como el aviso de un auto real); 1 = 3D desde el tablero. En 3D se atenúa con la distancia y queda tapado por el motor y el ambiente.")]
        [Range(0f, 1f)]
        [SerializeField] private float m_SpatialBlend = 0f;

        [Header("Mixer")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private float m_ReadyAt;
        private float m_LastChimeTime = float.NegativeInfinity;
        private DashboardUIController m_SubscribedDashboard;

        private void Awake()
        {
            if (m_Dashboard == null)
                m_Dashboard = GetComponent<DashboardUIController>();
            if (m_Source == null)
                m_Source = GetComponent<AudioSource>();

            float min = m_Profile != null ? m_Profile.CabinMinDistance : 0.3f;
            float max = m_Profile != null ? m_Profile.CabinMaxDistance : 5f;
            VehicleAudioUtil.ConfigureSource(m_Source, false, min, max, m_MixerGroup);
            if (m_Source != null) m_Source.spatialBlend = m_SpatialBlend;
        }

        private void OnEnable()
        {
            m_ReadyAt = Time.time + VehicleAudioUtil.InitializationGraceSeconds;

            if (m_Dashboard != null && m_SubscribedDashboard == null)
            {
                m_SubscribedDashboard = m_Dashboard;
                m_SubscribedDashboard.InfractionWarningTriggered += OnInfractionWarningTriggered;
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (m_SubscribedDashboard == null) return;
            m_SubscribedDashboard.InfractionWarningTriggered -= OnInfractionWarningTriggered;
            m_SubscribedDashboard = null;
        }

        private void OnInfractionWarningTriggered()
        {
            if (m_Profile == null || Time.time < m_ReadyAt) return;
            if (Time.time - m_LastChimeTime < m_Profile.InfractionChimeCooldown) return;

            m_LastChimeTime = Time.time;
            VehicleAudioUtil.PlayOneShot(m_Source, m_Profile.InfractionChime, m_Profile.InfractionChimeVolume);
        }
    }
}
