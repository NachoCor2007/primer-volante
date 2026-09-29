using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Loop de viento/rodadura cuyo volumen y pitch dependen de la velocidad. La fuente se detiene
    /// cuando el volumen es prácticamente cero (auto detenido).
    /// </summary>
    public class VehicleRoadNoiseAudio : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private VehicleAudioProfile m_Profile;

        [Header("Fuente")]
        [Tooltip("Fuente bajo el piso/centro del auto.")]
        [SerializeField] private AudioSource m_Source;

        [Header("Mixer")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();

            float min = m_Profile != null ? m_Profile.RoadMinDistance : 1f;
            float max = m_Profile != null ? m_Profile.RoadMaxDistance : 10f;
            VehicleAudioUtil.ConfigureSource(m_Source, true, min, max, m_MixerGroup);

            if (m_Source != null && m_Profile != null)
                m_Source.clip = m_Profile.WindRoadLoop;
        }

        private void OnDisable()
        {
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();
        }

        private void Update()
        {
            if (!Application.isPlaying || m_Source == null || m_Profile == null || m_VehicleController == null) return;

            float speed = Mathf.Abs(m_VehicleController.CurrentSpeedKmh);
            float volume = Mathf.Clamp01(m_Profile.RoadVolumeBySpeed.Evaluate(speed)) * m_Profile.WindRoadMaxVolume;

            if (volume <= m_Profile.RoadSilenceThreshold)
            {
                if (m_Source.isPlaying) m_Source.Stop();
                return;
            }

            m_Source.volume = volume;
            m_Source.pitch = Mathf.Max(0.1f, m_Profile.RoadPitchBySpeed.Evaluate(speed));

            if (!m_Source.isPlaying && m_Source.clip != null) m_Source.Play();
        }
    }
}
