using System;
using PrimerVolante.Environment;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Audio 3D de un vehículo NPC (US20): motor en dos capas (ralentí y régimen medio) con pitch según la velocidad,
    /// frenada suave, frenada de emergencia (chirrido + bocina corta + ducking), bocinas de impaciencia si queda
    /// bloqueado y sirena opcional (apagada por defecto). Solo suena: las reacciones del NPC y la atribución de culpa
    /// son de otras tareas. Con clips nulos no suena nada y no hay excepciones.
    /// </summary>
    [RequireComponent(typeof(NPCPathFollower))]
    [DisallowMultipleComponent]
    public class NPCVehicleAudio : MonoBehaviour
    {
        // Holgura sobre la distancia máxima antes de apagar una fuente.
        private const float k_CullFactor = 1.1f;

        // Segundos entre el chirrido y la bocina de una frenada de emergencia.
        private const float k_HornDelayAfterBrake = 0.25f;

        private const string k_AudioRootName = "NPCAudio";

        [Header("Perfil")]
        [Tooltip("Perfil de audio del vehículo (Default o Heavy para buses, camiones y contenedores).")]
        [SerializeField] private NPCVehicleAudioProfile m_Profile;

        [Header("Fuentes (hijos 3D)")]
        [SerializeField] private AudioSource m_EngineIdleSource;
        [SerializeField] private AudioSource m_EngineMidSource;
        [SerializeField] private AudioSource m_BrakeSource;
        [Tooltip("Frenada de emergencia y bocinas (grupo Alerts).")]
        [SerializeField] private AudioSource m_AlertSource;
        [SerializeField] private AudioSource m_SirenSource;

        [Header("Sirena")]
        [Tooltip("Clip de la sirena. Solo se asigna en NPC_Ambulance y NPC_Police Car.")]
        [SerializeField] private AudioClip m_SirenClip;

        [Tooltip("Si la sirena suena. Apagada por defecto: no hay ambulancias circulando todavía.")]
        [SerializeField] private bool m_SirenEnabled = false;

        [Header("Impaciencia")]
        [Tooltip("Segundos detenido por el NPCSafetyStop antes de tocar la bocina. 0 = según la intensidad del AmbienceDirector " +
                 "(Calm 8 s, Normal 5 s, Tense 2.5 s).")]
        [SerializeField] private float m_HonkAfterBlockedSeconds = 0f;

        [Tooltip("Segundos entre bocinas mientras sigue bloqueado.")]
        [SerializeField] private float m_HonkRepeatSeconds = 3f;

        [Tooltip("Máximo de bocinas por bloqueo.")]
        [SerializeField, Min(1)] private int m_MaxBlockedHonks = 4;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por frenada de emergencia y por bocina.")]
        [SerializeField] private bool m_LogEvents = false;

        private NPCPathFollower m_Follower;
        private NPCVehicleController m_Controller;
        private NPCSafetyStop m_Safety;
        private System.Random m_Random;

        private bool m_EngineOn;
        private bool m_WasBraking;
        private float m_NextSoftBrakeTime;
        private float m_LastSpeed;
        private float m_BlockedSeconds;
        private int m_HonkCount;
        private float m_NextBlockedHonkTime;
        private float m_PendingHornTime = -1f;
        private float m_NextSirenDuckTime;

        /// <summary>
        /// Se dispara cuando cualquier vehículo NPC hace una frenada de emergencia, con su posición: los peatones
        /// cercanos se suscriben para gritar. Se limpia al entrar a Play Mode.
        /// </summary>
        public static event Action<Vector3> AnyEmergencyBrake;

        /// <summary>Perfil de audio.</summary>
        public NPCVehicleAudioProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        /// <summary>Clip de la sirena asignado (solo ambulancia y patrulla).</summary>
        public AudioClip SirenClip
        {
            get => m_SirenClip;
            set => m_SirenClip = value;
        }

        /// <summary>true si la sirena está encendida.</summary>
        public bool SirenEnabled => m_SirenEnabled;

        /// <summary>Segundos bloqueado por el NPCSafetyStop.</summary>
        public float BlockedSeconds => m_BlockedSeconds;

        /// <summary>Bocinas de impaciencia tocadas en el bloqueo actual.</summary>
        public int BlockedHonkCount => m_HonkCount;

        /// <summary>Fuente del loop de ralentí.</summary>
        public AudioSource EngineIdleSource => m_EngineIdleSource;

        /// <summary>Fuente del loop de régimen medio.</summary>
        public AudioSource EngineMidSource => m_EngineMidSource;

        /// <summary>Fuente de la frenada suave.</summary>
        public AudioSource BrakeSource => m_BrakeSource;

        /// <summary>Fuente de la frenada de emergencia y las bocinas (grupo Alerts).</summary>
        public AudioSource AlertSource => m_AlertSource;

        /// <summary>Fuente de la sirena.</summary>
        public AudioSource SirenSource => m_SirenSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AnyEmergencyBrake = null;
        }

        /// <summary>
        /// Crea (o reutiliza) los hijos con las <see cref="AudioSource"/> 3D y los asigna a los campos. Lo usa el
        /// setup de Editor y, como respaldo, el runtime si faltan.
        /// </summary>
        public void BuildSources(AudioMixerGroup npcGroup, AudioMixerGroup alertsGroup)
        {
            Transform root = transform.Find(k_AudioRootName);
            if (root == null)
            {
                root = new GameObject(k_AudioRootName).transform;
                root.SetParent(transform, false);
            }

            float engMin = m_Profile != null ? m_Profile.EngineMinDistance : 3f;
            float engMax = m_Profile != null ? m_Profile.EngineMaxDistance : 50f;
            float alMin = m_Profile != null ? m_Profile.AlertMinDistance : 5f;
            float alMax = m_Profile != null ? m_Profile.AlertMaxDistance : 80f;
            float sirenMax = m_Profile != null ? m_Profile.SirenMaxDistance : 120f;

            m_EngineIdleSource = EnvironmentAudioUtil.CreateSource3D(root, "EngineIdle", npcGroup, EnvironmentAudioUtil.PriorityNpc, engMin, engMax, true);
            m_EngineMidSource = EnvironmentAudioUtil.CreateSource3D(root, "EngineMid", npcGroup, EnvironmentAudioUtil.PriorityNpc, engMin, engMax, true);
            m_BrakeSource = EnvironmentAudioUtil.CreateSource3D(root, "SoftBrake", npcGroup, EnvironmentAudioUtil.PriorityNpc, engMin, engMax);
            m_AlertSource = EnvironmentAudioUtil.CreateSource3D(root, "Alert", alertsGroup, EnvironmentAudioUtil.PriorityAlert, alMin, alMax);
            m_SirenSource = EnvironmentAudioUtil.CreateSource3D(root, "Siren", alertsGroup, EnvironmentAudioUtil.PriorityAlert, alMin, sirenMax, true);

            // Solo la sirena usa Doppler (moderado): se nota la aproximación sin deformar el resto.
            m_SirenSource.dopplerLevel = 0.5f;
        }

        private void Awake()
        {
            m_Follower = GetComponent<NPCPathFollower>();
            m_Controller = GetComponent<NPCVehicleController>();
            m_Safety = GetComponent<NPCSafetyStop>();
            m_Random = new System.Random(EnvironmentAudioUtil.SeedFromPosition(transform.position) + name.Length);

            // Respaldo: si el setup no creó las fuentes se crean sin grupo (el setup es lo que las rutea al mixer).
            if (m_EngineIdleSource == null || m_EngineMidSource == null || m_AlertSource == null)
            {
                BuildSources(null, null);
            }

            ApplyProfileToSources();
        }

        private void OnEnable()
        {
            if (m_Safety == null) m_Safety = GetComponent<NPCSafetyStop>();
            if (m_Safety != null)
            {
                m_Safety.OnEmergencyBrake += HandleEmergencyBrake;
                m_Safety.OnResumeMovement += HandleResume;
            }
        }

        private void OnDisable()
        {
            if (m_Safety != null)
            {
                m_Safety.OnEmergencyBrake -= HandleEmergencyBrake;
                m_Safety.OnResumeMovement -= HandleResume;
            }

            StopEngine();
            if (m_SirenSource != null && m_SirenSource.isPlaying) m_SirenSource.Stop();
            ResetBlocked();
            m_PendingHornTime = -1f;
        }

        private void ApplyProfileToSources()
        {
            if (m_Profile == null) return;

            SetDistances(m_EngineIdleSource, m_Profile.EngineMinDistance, m_Profile.EngineMaxDistance);
            SetDistances(m_EngineMidSource, m_Profile.EngineMinDistance, m_Profile.EngineMaxDistance);
            SetDistances(m_BrakeSource, m_Profile.EngineMinDistance, m_Profile.EngineMaxDistance);
            SetDistances(m_AlertSource, m_Profile.AlertMinDistance, m_Profile.AlertMaxDistance);
            SetDistances(m_SirenSource, m_Profile.AlertMinDistance, m_Profile.SirenMaxDistance);

            if (m_SirenSource != null) m_SirenSource.dopplerLevel = 0.5f;
        }

        private static void SetDistances(AudioSource source, float min, float max)
        {
            if (source == null) return;
            source.minDistance = min;
            source.maxDistance = max;
        }

        /// <summary>Enciende o apaga la sirena (si hay clip asignado).</summary>
        public void SetSiren(bool enabled)
        {
            m_SirenEnabled = enabled;
            if (!enabled && m_SirenSource != null && m_SirenSource.isPlaying) m_SirenSource.Stop();
        }

        // ------------------------------------------------------------------ eventos

        private void HandleEmergencyBrake()
        {
            Vector3 position = transform.position;
            AnyEmergencyBrake?.Invoke(position);

            if (m_Profile == null || m_AlertSource == null) return;
            if (!IsWithinAlertRange()) return;

            if (m_LastSpeed > m_Profile.ScreechMinSpeed)
            {
                VehicleOneShot(m_AlertSource, m_Profile.TireScreech, m_Profile.TireScreechVolume);
                m_PendingHornTime = Time.time + k_HornDelayAfterBrake;
            }
            else
            {
                // Venía lento: sin chirrido, solo la bocina corta.
                PlayHorn(false);
            }

            AmbienceDirector.Duck(m_Profile.DuckDb, m_Profile.DuckHoldSeconds);
            if (m_LogEvents) Debug.Log($"[NPCVehicleAudio] 🛑 {name}: frenada de emergencia ({m_LastSpeed:F1} m/s).", this);
        }

        private void HandleResume()
        {
            ResetBlocked();
        }

        private void ResetBlocked()
        {
            m_BlockedSeconds = 0f;
            m_HonkCount = 0;
            m_NextBlockedHonkTime = 0f;
        }

        // ------------------------------------------------------------------ ciclo

        private void Update()
        {
            if (m_Profile == null || m_Follower == null) return;

            float dt = Time.deltaTime;
            float speed = m_Follower.CurrentSpeed;
            bool haveListener = EnvironmentAudioUtil.TryGetListenerPosition(out Vector3 listenerPos);
            float distance = haveListener ? Vector3.Distance(listenerPos, transform.position) : float.PositiveInfinity;

            UpdateEngine(speed, distance);
            UpdateSoftBrake(speed, distance);
            UpdateBlockedHonk(dt);
            UpdatePendingHorn();
            UpdateSiren(distance);

            m_LastSpeed = speed;
        }

        private void UpdateEngine(float speed, float distance)
        {
            bool wanted = distance <= m_Profile.EngineMaxDistance * k_CullFactor;
            if (wanted && !m_EngineOn) StartEngine();
            else if (!wanted && m_EngineOn) StopEngine();
            if (!m_EngineOn) return;

            float blend = NPCAudioMath.EngineBlend(speed, m_Profile.ReferenceSpeed);
            float pitch = NPCAudioMath.EnginePitch(speed, m_Profile.ReferenceSpeed, m_Profile.MinPitch, m_Profile.MaxPitch);
            float edge = AmbienceMath.EdgeFade(distance, m_Profile.EngineMaxDistance);
            float volume = m_Profile.EngineVolume * edge;

            // Potencia constante entre las dos capas.
            SetEngineLayer(m_EngineIdleSource, m_Profile.EngineIdleLoop, volume * Mathf.Cos(blend * Mathf.PI * 0.5f), pitch);
            SetEngineLayer(m_EngineMidSource, m_Profile.EngineMidLoop, volume * Mathf.Sin(blend * Mathf.PI * 0.5f), pitch);
        }

        private static void SetEngineLayer(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            if (source == null || clip == null) return;
            source.volume = volume;
            source.pitch = pitch;
        }

        private void StartEngine()
        {
            m_EngineOn = true;
            StartLoop(m_EngineIdleSource, m_Profile.EngineIdleLoop);
            StartLoop(m_EngineMidSource, m_Profile.EngineMidLoop);
        }

        private void StartLoop(AudioSource source, AudioClip clip)
        {
            if (source == null || clip == null) return;

            source.clip = clip;
            source.volume = 0f;
            source.Play();

            // Punto aleatorio del clip: dos NPC iguales no suenan en fase.
            if (clip.samples > 0) source.timeSamples = m_Random.Next(clip.samples);
        }

        private void StopEngine()
        {
            m_EngineOn = false;
            if (m_EngineIdleSource != null && m_EngineIdleSource.isPlaying) m_EngineIdleSource.Stop();
            if (m_EngineMidSource != null && m_EngineMidSource.isPlaying) m_EngineMidSource.Stop();
        }

        private void UpdateSoftBrake(float speed, float distance)
        {
            bool braking = m_Controller != null && m_Controller.IsBraking;
            bool emergency = m_Safety != null && m_Safety.IsEmergencyBraking;

            if (braking && !m_WasBraking && !emergency
                && speed > m_Profile.SoftBrakeMinSpeed
                && Time.time >= m_NextSoftBrakeTime
                && distance <= m_Profile.EngineMaxDistance)
            {
                VehicleOneShot(m_BrakeSource, m_Profile.SoftBrake, m_Profile.SoftBrakeVolume);
                m_NextSoftBrakeTime = Time.time + m_Profile.SoftBrakeCooldown;
            }

            m_WasBraking = braking;
        }

        private void UpdateBlockedHonk(float dt)
        {
            bool blocked = m_Safety != null && m_Safety.IsEmergencyBraking;
            if (!blocked)
            {
                if (m_BlockedSeconds > 0f || m_HonkCount > 0) ResetBlocked();
                return;
            }

            m_BlockedSeconds += dt;

            float delay = m_HonkAfterBlockedSeconds > 0f ? m_HonkAfterBlockedSeconds : AmbienceDirector.CurrentNpcHonkDelay;
            if (!NPCAudioMath.ShouldBlockedHonk(m_BlockedSeconds, delay)) return;
            if (m_HonkCount >= m_MaxBlockedHonks || Time.time < m_NextBlockedHonkTime) return;

            // Alterna corta y larga.
            bool isLong = m_HonkCount % 2 == 1;
            m_HonkCount++;
            m_NextBlockedHonkTime = Time.time + m_HonkRepeatSeconds;

            if (IsWithinAlertRange())
            {
                PlayHorn(isLong);
                AmbienceDirector.Duck(m_Profile.DuckDb, m_Profile.DuckHoldSeconds);
            }

            if (m_LogEvents) Debug.Log($"[NPCVehicleAudio] 📯 {name}: bocina de impaciencia #{m_HonkCount} ({(isLong ? "larga" : "corta")}).", this);
        }

        private void UpdatePendingHorn()
        {
            if (m_PendingHornTime < 0f || Time.time < m_PendingHornTime) return;

            m_PendingHornTime = -1f;
            PlayHorn(false);
        }

        private void UpdateSiren(float distance)
        {
            if (m_SirenSource == null) return;

            bool wanted = m_SirenEnabled && m_SirenClip != null && distance <= m_Profile.SirenMaxDistance * k_CullFactor;
            if (wanted && !m_SirenSource.isPlaying)
            {
                m_SirenSource.clip = m_SirenClip;
                m_SirenSource.volume = m_Profile.SirenVolume;
                m_SirenSource.Play();
            }
            else if (!wanted && m_SirenSource.isPlaying)
            {
                m_SirenSource.Stop();
            }

            if (wanted)
            {
                m_SirenSource.volume = m_Profile.SirenVolume * AmbienceMath.EdgeFade(distance, m_Profile.SirenMaxDistance);

                // La sirena cercana también baja el ambiente.
                if (distance < m_Profile.SirenMaxDistance * 0.5f && Time.time >= m_NextSirenDuckTime)
                {
                    AmbienceDirector.Duck(m_Profile.DuckDb * 0.6f, 1.5f);
                    m_NextSirenDuckTime = Time.time + 1f;
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private bool IsWithinAlertRange()
        {
            if (!EnvironmentAudioUtil.TryGetListenerPosition(out Vector3 listenerPos)) return false;
            return Vector3.Distance(listenerPos, transform.position) <= m_Profile.AlertMaxDistance * k_CullFactor;
        }

        private void PlayHorn(bool isLong)
        {
            AudioClip clip = isLong ? m_Profile.HornLong : m_Profile.HornShort;
            VehicleOneShot(m_AlertSource, clip, m_Profile.HornVolume);
        }

        private static void VehicleOneShot(AudioSource source, AudioClip clip, float volume)
        {
            PrimerVolante.VR.VehicleAudioUtil.PlayOneShot(source, clip, volume);
        }
    }
}
