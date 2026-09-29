using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Sonidos del freno de mano: clics de trinquete al subir la palanca, "clunk" al liberar por
    /// completo y tirón completo cuando el cambio es instantáneo (tecla B).
    /// </summary>
    public class VehicleHandbrakeAudio : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private VRHandbrake m_Handbrake;
        [SerializeField] private VehicleAudioProfile m_Profile;

        [Header("Fuente")]
        [Tooltip("Fuente ubicada en la palanca del freno de mano.")]
        [SerializeField] private AudioSource m_Source;

        [Header("Mixer")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private float m_ReadyAt;
        private float m_LastEngagement;
        private bool m_WasEngaged;
        private float m_LastClickTime = -1f;

        private void Awake()
        {
            if (m_Handbrake == null)
                m_Handbrake = GetComponentInParent<VRHandbrake>();
            if (m_Handbrake == null)
            {
                var vehicle = GetComponentInParent<VehicleController>();
                if (vehicle != null) m_Handbrake = vehicle.GetComponentInChildren<VRHandbrake>(true);
            }

            float min = m_Profile != null ? m_Profile.CabinMinDistance : 0.3f;
            float max = m_Profile != null ? m_Profile.CabinMaxDistance : 5f;
            VehicleAudioUtil.ConfigureSource(m_Source, false, min, max, m_MixerGroup);
        }

        private void OnEnable()
        {
            m_ReadyAt = Time.time + VehicleAudioUtil.InitializationGraceSeconds;
            if (m_Handbrake == null) return;

            m_LastEngagement = m_Handbrake.Engagement;
            m_WasEngaged = m_Handbrake.IsEngaged;
            m_Handbrake.OnEngagementChanged.AddListener(OnEngagementChanged);
        }

        private void OnDisable()
        {
            if (m_Handbrake != null) m_Handbrake.OnEngagementChanged.RemoveListener(OnEngagementChanged);
        }

        private void OnDestroy()
        {
            if (m_Handbrake != null) m_Handbrake.OnEngagementChanged.RemoveListener(OnEngagementChanged);
        }

        private void OnEngagementChanged(float engagement)
        {
            float previous = m_LastEngagement;
            bool wasEngaged = m_WasEngaged;
            m_LastEngagement = engagement;
            m_WasEngaged = m_Handbrake != null && m_Handbrake.IsEngaged;

            if (m_Profile == null || Time.time < m_ReadyAt) return;

            float delta = engagement - previous;

            if (delta > m_Profile.PullFullThreshold)
            {
                // Salto instantáneo hacia arriba (tecla B): un tirón completo en lugar de clics sueltos.
                ResetPitch();
                VehicleAudioUtil.PlayOneShot(m_Source, m_Profile.HandbrakePullFull, m_Profile.HandbrakePullFullVolume);
                return;
            }

            if (wasEngaged && !m_WasEngaged)
            {
                ResetPitch();
                VehicleAudioUtil.PlayOneShot(m_Source, m_Profile.HandbrakeRelease, m_Profile.HandbrakeReleaseVolume);
                return;
            }

            if (delta > 0f && CrossedRatchetStep(previous, engagement, m_Profile.RatchetStep))
            {
                PlayRatchetClick();
            }
        }

        /// <summary>
        /// Indica si al subir de <paramref name="from"/> a <paramref name="to"/> se cruzó un escalón.
        /// </summary>
        public static bool CrossedRatchetStep(float from, float to, float step)
        {
            if (step <= 0f) return false;
            return Mathf.FloorToInt(to / step + 1e-4f) > Mathf.FloorToInt(from / step + 1e-4f);
        }

        private void ResetPitch()
        {
            if (m_Source != null) m_Source.pitch = 1f;
        }

        private void PlayRatchetClick()
        {
            if (m_Source == null || m_Profile.HandbrakeRatchetClick == null) return;
            if (Time.time - m_LastClickTime < m_Profile.RatchetMinInterval) return;
            m_LastClickTime = Time.time;

            // El pitch es de la fuente; los one-shots lo comparten, se restablece para el resto de sonidos.
            m_Source.pitch = Random.Range(m_Profile.RatchetPitchRange.x, m_Profile.RatchetPitchRange.y);
            m_Source.PlayOneShot(m_Profile.HandbrakeRatchetClick, m_Profile.HandbrakeRatchetVolume);
        }
    }
}
