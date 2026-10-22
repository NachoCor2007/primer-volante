using PrimerVolante.Environment;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Audio 3D de un peatón NPC (US20): pasos con intervalo largo de paso / velocidad (anticipan a un peatón que va a
    /// cruzar), voces ocasionales y grito ante una frenada de emergencia cercana. Las voces y el grito son orgánicos
    /// y pueden quedar vacíos: el componente funciona igual con clips nulos.
    /// </summary>
    [RequireComponent(typeof(NPCPathFollower))]
    [DisallowMultipleComponent]
    public class NPCPedestrianAudio : MonoBehaviour
    {
        // Holgura sobre la distancia máxima antes de dejar de trabajar.
        private const float k_CullFactor = 1.1f;

        // Reacción del peatón al frenazo antes de gritar (segundos).
        private const float k_ScreamReactionDelay = 0.15f;

        private const string k_AudioRootName = "NPCAudio";

        [Header("Perfil")]
        [Tooltip("Perfil de audio del peatón.")]
        [SerializeField] private NPCPedestrianAudioProfile m_Profile;

        [Header("Fuentes (hijos 3D)")]
        [SerializeField] private AudioSource m_FootstepSource;
        [Tooltip("Voces ocasionales y grito.")]
        [SerializeField] private AudioSource m_VoiceSource;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por grito.")]
        [SerializeField] private bool m_LogEvents = false;

        private NPCPathFollower m_Follower;
        private System.Random m_Random;
        private float m_StepTimer;
        private AudioClip m_LastStep;
        private float m_NextVoiceTime;
        private float m_NextScreamTime;
        private float m_PendingScreamTime = -1f;

        /// <summary>Perfil de audio.</summary>
        public NPCPedestrianAudioProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        /// <summary>Fuente de los pasos.</summary>
        public AudioSource FootstepSource => m_FootstepSource;

        /// <summary>Fuente de las voces y el grito.</summary>
        public AudioSource VoiceSource => m_VoiceSource;

        /// <summary>
        /// Crea (o reutiliza) los hijos con las <see cref="AudioSource"/> 3D y los asigna a los campos. Lo usa el
        /// setup de Editor y, como respaldo, el runtime si faltan.
        /// </summary>
        public void BuildSources(AudioMixerGroup npcGroup)
        {
            Transform root = transform.Find(k_AudioRootName);
            if (root == null)
            {
                root = new GameObject(k_AudioRootName).transform;
                root.SetParent(transform, false);
            }

            float min = m_Profile != null ? m_Profile.MinDistance : 1.5f;
            float max = m_Profile != null ? m_Profile.MaxDistance : 15f;

            m_FootstepSource = EnvironmentAudioUtil.CreateSource3D(root, "Footsteps", npcGroup, EnvironmentAudioUtil.PriorityNpc, min, max);
            m_VoiceSource = EnvironmentAudioUtil.CreateSource3D(root, "Voice", npcGroup, EnvironmentAudioUtil.PriorityNpc, min, max * 2f);
        }

        private void Awake()
        {
            m_Follower = GetComponent<NPCPathFollower>();
            m_Random = new System.Random(EnvironmentAudioUtil.SeedFromPosition(transform.position) + name.Length);

            if (m_FootstepSource == null || m_VoiceSource == null) BuildSources(null);

            if (m_Profile != null)
            {
                m_FootstepSource.minDistance = m_Profile.MinDistance;
                m_FootstepSource.maxDistance = m_Profile.MaxDistance;
                m_VoiceSource.minDistance = m_Profile.MinDistance;
                m_VoiceSource.maxDistance = m_Profile.MaxDistance * 2f;
                ScheduleVoice();
            }
        }

        private void OnEnable()
        {
            NPCVehicleAudio.AnyEmergencyBrake += HandleVehicleEmergencyBrake;
        }

        private void OnDisable()
        {
            NPCVehicleAudio.AnyEmergencyBrake -= HandleVehicleEmergencyBrake;
            m_PendingScreamTime = -1f;
        }

        private void ScheduleVoice()
        {
            m_NextVoiceTime = Time.time + EnvironmentAudioUtil.Range(m_Random, m_Profile.VoiceMinInterval, m_Profile.VoiceMaxInterval);
        }

        // Un vehículo hizo una frenada de emergencia: si está dentro del radio, el peatón grita.
        private void HandleVehicleEmergencyBrake(Vector3 position)
        {
            if (m_Profile == null || !m_Profile.HasScreams) return;
            if (Time.time < m_NextScreamTime) return;
            if ((position - transform.position).sqrMagnitude > m_Profile.ScreamRadius * m_Profile.ScreamRadius) return;

            m_PendingScreamTime = Time.time + k_ScreamReactionDelay;
            m_NextScreamTime = Time.time + m_Profile.ScreamCooldown;
        }

        private void Update()
        {
            if (m_Profile == null || m_Follower == null) return;
            if (!EnvironmentAudioUtil.TryGetListenerPosition(out Vector3 listenerPos)) return;

            float distance = Vector3.Distance(listenerPos, transform.position);
            bool near = distance <= m_Profile.MaxDistance * k_CullFactor;

            UpdateFootsteps(near, distance);
            UpdateVoice(distance);
        }

        private void UpdateFootsteps(bool near, float distance)
        {
            if (!near || !m_Follower.IsMoving || !m_Profile.HasFootsteps)
            {
                // Al arrancar a caminar el primer paso suena enseguida.
                m_StepTimer = 0f;
                return;
            }

            m_StepTimer -= Time.deltaTime;
            if (m_StepTimer > 0f) return;

            float interval = NPCAudioMath.FootstepInterval(m_Follower.CurrentSpeed, m_Profile.StrideLength, m_Profile.MinStepInterval);
            if (float.IsInfinity(interval)) return;

            AudioClip clip = EnvironmentAudioProfileBase.PickClip(m_Profile.Footsteps, m_Random, m_LastStep);
            if (clip != null)
            {
                m_LastStep = clip;
                m_FootstepSource.pitch = EnvironmentAudioUtil.Range(m_Random, m_Profile.FootstepMinPitch, m_Profile.FootstepMaxPitch);
                float volume = EnvironmentAudioUtil.Range(m_Random, m_Profile.FootstepMinVolume, m_Profile.FootstepMaxVolume);
                m_FootstepSource.PlayOneShot(clip, volume * AmbienceMath.EdgeFade(distance, m_Profile.MaxDistance));
            }

            m_StepTimer = interval;
        }

        private void UpdateVoice(float distance)
        {
            // Grito pendiente (reacción a un frenazo cercano).
            if (m_PendingScreamTime >= 0f && Time.time >= m_PendingScreamTime)
            {
                m_PendingScreamTime = -1f;
                AudioClip scream = EnvironmentAudioProfileBase.PickClip(m_Profile.Screams, m_Random);
                if (scream != null)
                {
                    m_VoiceSource.pitch = 1f;
                    m_VoiceSource.PlayOneShot(scream, m_Profile.ScreamVolume);
                    if (m_LogEvents) Debug.Log($"[NPCPedestrianAudio] 😱 {name}: grito.", this);
                }

                return;
            }

            if (Time.time < m_NextVoiceTime) return;
            ScheduleVoice();

            if (!m_Profile.HasVoices || distance > m_Profile.MaxDistance * k_CullFactor) return;

            AudioClip voice = EnvironmentAudioProfileBase.PickClip(m_Profile.Voices, m_Random);
            if (voice == null) return;

            m_VoiceSource.pitch = EnvironmentAudioUtil.Range(m_Random, 0.95f, 1.05f);
            m_VoiceSource.PlayOneShot(voice, m_Profile.VoiceVolume);
        }
    }
}
