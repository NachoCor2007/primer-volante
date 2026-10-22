using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Fuente sonora puntual fija en un edificio (US20): un bar, el estadio, un taller, un molino, una casa con
    /// perro. Tiene una <see cref="AudioSource"/> 3D propia (spatialBlend 1, rolloff logarítmico, sin Doppler) y solo
    /// trabaja si el oyente está a menos de <c>maxDistance × 1.1</c> (culling). Con clips nulos no hace nada.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class AmbientEmitter : MonoBehaviour
    {
        // Holgura sobre maxDistance antes de dejar de trabajar.
        private const float k_CullFactor = 1.1f;

        [Header("Emisor")]
        [Tooltip("Perfil con el modo, los clips y los parámetros de este emisor.")]
        [SerializeField] private AmbientEmitterProfile m_Profile;

        [Tooltip("Grupo Ambience del EnvironmentAudioMixer.")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log al entrar y salir de rango.")]
        [SerializeField] private bool m_LogEvents = false;

        private AudioSource m_Source;
        private System.Random m_Random;
        private bool m_Active;
        private float m_NextShotTime;
        private bool m_WasInsideApproach;
        private float m_CooldownUntil;
        private int m_BurstRemaining;
        private float m_NextBurstTime;
        private AudioClip m_LastClip;

        /// <summary>Perfil del emisor.</summary>
        public AmbientEmitterProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        /// <summary>Grupo de mixer de la fuente (Ambience).</summary>
        public AudioMixerGroup MixerGroup
        {
            get => m_MixerGroup;
            set => m_MixerGroup = value;
        }

        /// <summary>true si el oyente está en rango y el emisor está trabajando.</summary>
        public bool IsActive => m_Active;

        /// <summary>AudioSource 3D del emisor.</summary>
        public AudioSource Source => m_Source != null ? m_Source : GetComponent<AudioSource>();

        private void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_Random = new System.Random(EnvironmentAudioUtil.SeedFromPosition(transform.position));
            ConfigureSource();
        }

        private void OnDisable()
        {
            Deactivate();
        }

        private void ConfigureSource()
        {
            if (m_Profile == null) return;

            bool loop = m_Profile.Mode == AmbientEmitterMode.Loop;
            PrimerVolante.VR.VehicleAudioUtil.ConfigureSource(
                m_Source, loop, m_Profile.MinDistance, m_Profile.MaxDistance, m_MixerGroup, EnvironmentAudioUtil.PriorityEmitter);
        }

        private void Update()
        {
            if (m_Profile == null) return;
            if (!EnvironmentAudioUtil.TryGetListenerPosition(out Vector3 listenerPos)) return;

            float distance = Vector3.Distance(listenerPos, transform.position);
            bool inRange = distance <= m_Profile.MaxDistance * k_CullFactor;

            if (!inRange)
            {
                if (m_Active) Deactivate();
                m_WasInsideApproach = false;
                return;
            }

            if (!m_Active) Activate();

            switch (m_Profile.Mode)
            {
                case AmbientEmitterMode.Loop:
                    UpdateLoop(distance);
                    break;
                case AmbientEmitterMode.RandomOneShots:
                    UpdateRandomOneShots(distance);
                    break;
                case AmbientEmitterMode.OnApproach:
                    UpdateOnApproach(distance);
                    break;
            }
        }

        private void Activate()
        {
            m_Active = true;
            m_NextShotTime = Time.time + EnvironmentAudioUtil.Range(m_Random, 0f, m_Profile.MaxInterval);
            m_WasInsideApproach = false;
            m_BurstRemaining = 0;

            // Se reconfigura al activarse: si el perfil se asignó después de Awake la fuente no queda 2D ni sin grupo.
            ConfigureSource();

            if (m_Profile.Mode == AmbientEmitterMode.Loop && m_Profile.LoopClip != null)
            {
                m_Source.clip = m_Profile.LoopClip;
                m_Source.volume = 0f;
                m_Source.Play();

                // Punto aleatorio del clip: dos emisores iguales no suenan en fase.
                if (m_Profile.LoopClip.samples > 0)
                    m_Source.timeSamples = m_Random.Next(m_Profile.LoopClip.samples);
            }

            if (m_LogEvents) Debug.Log($"[AmbientEmitter] 🔊 {name}: en rango ({m_Profile.name}).", this);
        }

        private void Deactivate()
        {
            if (!m_Active) return;

            m_Active = false;
            m_BurstRemaining = 0;
            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();
            if (m_LogEvents) Debug.Log($"[AmbientEmitter] 🔇 {name}: fuera de rango.", this);
        }

        private void UpdateLoop(float distance)
        {
            if (m_Profile.LoopClip == null) return;

            // El rolloff logarítmico no llega a 0 en maxDistance: se apaga suave para no cortar al salir de rango.
            float volume = (m_Profile.MinVolume + m_Profile.MaxVolume) * 0.5f;
            m_Source.volume = volume * AmbienceMath.EdgeFade(distance, m_Profile.MaxDistance);

            if (!m_Source.isPlaying) m_Source.Play();
        }

        private void UpdateRandomOneShots(float distance)
        {
            if (!m_Profile.HasOneShots || Time.time < m_NextShotTime) return;

            PlayOneShot(distance);
            m_NextShotTime = Time.time + EnvironmentAudioUtil.Range(m_Random, m_Profile.MinInterval, m_Profile.MaxInterval);
        }

        private void UpdateOnApproach(float distance)
        {
            if (!m_Profile.HasOneShots) return;

            bool inside = distance <= m_Profile.ApproachRadius;
            if (inside && !m_WasInsideApproach && Time.time >= m_CooldownUntil)
            {
                if (m_Random.NextDouble() <= m_Profile.ApproachProbability)
                {
                    m_BurstRemaining = m_Random.Next(m_Profile.BurstMin, m_Profile.BurstMax + 1);
                    m_NextBurstTime = Time.time;
                    m_CooldownUntil = Time.time + m_Profile.ApproachCooldown;
                }
                else
                {
                    // La tirada falló: también respeta el cooldown, para no reintentar al instante.
                    m_CooldownUntil = Time.time + m_Profile.ApproachCooldown * 0.5f;
                }
            }

            m_WasInsideApproach = inside;

            if (m_BurstRemaining > 0 && Time.time >= m_NextBurstTime)
            {
                PlayOneShot(distance);
                m_BurstRemaining--;
                m_NextBurstTime = Time.time + EnvironmentAudioUtil.Range(m_Random, 0.4f, 0.9f);
            }
        }

        private void PlayOneShot(float distance)
        {
            AudioClip clip = EnvironmentAudioProfileBase.PickClip(m_Profile.OneShots, m_Random, m_LastClip);
            if (clip == null) return;

            m_LastClip = clip;
            m_Source.pitch = EnvironmentAudioUtil.Range(m_Random, m_Profile.MinPitch, m_Profile.MaxPitch);
            float volume = EnvironmentAudioUtil.Range(m_Random, m_Profile.MinVolume, m_Profile.MaxVolume);
            m_Source.PlayOneShot(clip, volume * AmbienceMath.EdgeFade(distance, m_Profile.MaxDistance));
        }

        private void OnDrawGizmosSelected()
        {
            if (m_Profile == null) return;

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, m_Profile.MaxDistance);
            if (m_Profile.Mode == AmbientEmitterMode.OnApproach)
            {
                Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
                Gizmos.DrawWireSphere(transform.position, m_Profile.ApproachRadius);
            }
        }
    }
}
