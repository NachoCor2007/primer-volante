using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Audio del motor: 3 loops simultáneos (ralentí / medio / alto) con crossfade de potencia
    /// constante según las RPM simuladas, pitch por capa, modulación por carga y secuencias de
    /// arranque/apagado. Las fuentes viven en el vano motor (Audio_Engine).
    /// </summary>
    public class VehicleEngineAudio : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private VehicleEngineSimulator m_Simulator;
        [SerializeField] private VehicleAudioProfile m_Profile;

        [Header("Fuentes")]
        [Tooltip("Fuente para los one-shots (arranque, apagado).")]
        [SerializeField] private AudioSource m_OneShotSource;
        [SerializeField] private AudioSource m_IdleSource;
        [SerializeField] private AudioSource m_MidSource;
        [SerializeField] private AudioSource m_HighSource;

        [Header("Mixer")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private float m_Gain;
        private float m_GainTarget;
        private EngineState m_PreviousState;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
            if (m_Simulator == null)
                m_Simulator = GetComponentInParent<VehicleEngineSimulator>();

            ConfigureSources();
        }

        private void ConfigureSources()
        {
            float min = m_Profile != null ? m_Profile.EngineMinDistance : 1f;
            float max = m_Profile != null ? m_Profile.EngineMaxDistance : 12f;

            // Prioridad alta (0 = máxima): no virtualizar el motor.
            VehicleAudioUtil.ConfigureSource(m_OneShotSource, false, min, max, m_MixerGroup, 0);
            VehicleAudioUtil.ConfigureSource(m_IdleSource, true, min, max, m_MixerGroup, 0);
            VehicleAudioUtil.ConfigureSource(m_MidSource, true, min, max, m_MixerGroup, 0);
            VehicleAudioUtil.ConfigureSource(m_HighSource, true, min, max, m_MixerGroup, 0);

            if (m_Profile == null) return;
            AssignLoop(m_IdleSource, m_Profile.EngineIdleLoop);
            AssignLoop(m_MidSource, m_Profile.EngineMidLoop);
            AssignLoop(m_HighSource, m_Profile.EngineHighLoop);
        }

        private static void AssignLoop(AudioSource src, AudioClip clip)
        {
            if (src != null && src.clip != clip) src.clip = clip;
        }

        private void OnEnable()
        {
            if (m_VehicleController == null) return;

            m_PreviousState = m_VehicleController.CurrentEngineState;
            m_GainTarget = m_PreviousState == EngineState.Running ? 1f : 0f;
            m_Gain = m_GainTarget; // si ya está en marcha al cargar, sin fade
            m_VehicleController.OnEngineStateChanged += OnEngineStateChanged;
        }

        private void OnDisable()
        {
            if (m_VehicleController != null)
                m_VehicleController.OnEngineStateChanged -= OnEngineStateChanged;

            StopLoops();
        }

        private void OnDestroy()
        {
            if (m_VehicleController != null)
                m_VehicleController.OnEngineStateChanged -= OnEngineStateChanged;
        }

        private void OnEngineStateChanged(EngineState state)
        {
            if (m_Profile == null) return;

            switch (state)
            {
                case EngineState.Cranking:
                    m_GainTarget = 0f;
                    m_Gain = 0f;
                    VehicleAudioUtil.PlayOneShot(m_OneShotSource, m_Profile.EngineStart, m_Profile.EngineStartVolume);
                    break;

                case EngineState.Running:
                    m_GainTarget = 1f;
                    if (m_PreviousState == EngineState.Off) m_Gain = 0f;
                    break;

                case EngineState.Off:
                    m_GainTarget = 0f;
                    if (m_PreviousState == EngineState.Running)
                        VehicleAudioUtil.PlayOneShot(m_OneShotSource, m_Profile.EngineStop, m_Profile.EngineStopVolume);
                    else if (m_PreviousState == EngineState.Cranking && m_OneShotSource != null)
                        m_OneShotSource.Stop();
                    break;
            }

            m_PreviousState = state;
        }

        private void Update()
        {
            if (!Application.isPlaying || m_Profile == null) return;

            float fadeSeconds = m_GainTarget > m_Gain ? m_Profile.EngineFadeInSeconds : m_Profile.EngineFadeOutSeconds;
            float step = fadeSeconds > 0.0001f ? Time.deltaTime / fadeSeconds : 1f;
            m_Gain = Mathf.MoveTowards(m_Gain, m_GainTarget, step);

            if (m_Gain <= 0.0001f && m_GainTarget <= 0f)
            {
                StopLoops();
                return;
            }

            EnsureLoopsPlaying();
            ApplyLayers();
        }

        private void EnsureLoopsPlaying()
        {
            if (m_IdleSource != null && !m_IdleSource.isPlaying)
            {
                // Arrancar las tres capas en el mismo frame para mantenerlas en fase.
                m_IdleSource.Play();
                if (m_MidSource != null) m_MidSource.Play();
                if (m_HighSource != null) m_HighSource.Play();
            }
        }

        private void StopLoops()
        {
            if (m_IdleSource != null && m_IdleSource.isPlaying) m_IdleSource.Stop();
            if (m_MidSource != null && m_MidSource.isPlaying) m_MidSource.Stop();
            if (m_HighSource != null && m_HighSource.isPlaying) m_HighSource.Stop();
        }

        private void ApplyLayers()
        {
            float rpm = m_Simulator != null ? m_Simulator.CurrentRpm : m_Profile.IdleLayerRpm;
            float load = m_Simulator != null ? m_Simulator.EngineLoad : 0f;

            ComputeLayerWeights(m_Profile, rpm, out float idleW, out float midW, out float highW);

            float loadFactor = Mathf.Lerp(m_Profile.EngineNoLoadVolumeFactor, 1f, load);
            float master = m_Profile.EngineLoopVolume * loadFactor * m_Gain;

            SetLayer(m_IdleSource, idleW * master, rpm, m_Profile.IdleLayerRpm);
            SetLayer(m_MidSource, midW * master, rpm, m_Profile.MidLayerRpm);
            SetLayer(m_HighSource, highW * master, rpm, m_Profile.HighLayerRpm);
        }

        private void SetLayer(AudioSource src, float volume, float rpm, float layerRpm)
        {
            if (src == null) return;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = ComputePitch(m_Profile, rpm, layerRpm);
        }

        /// <summary>
        /// Pesos de las tres capas por crossfade de potencia constante (cos/sin) según las RPM.
        /// La suma de los cuadrados es 1 en todo el rango.
        /// </summary>
        public static void ComputeLayerWeights(
            VehicleAudioProfile p, float rpm, out float idle, out float mid, out float high)
        {
            float t1 = Mathf.InverseLerp(p.IdleToMidCrossfade.x, p.IdleToMidCrossfade.y, rpm);
            float t2 = Mathf.InverseLerp(p.MidToHighCrossfade.x, p.MidToHighCrossfade.y, rpm);
            const float halfPi = Mathf.PI * 0.5f;

            idle = Mathf.Cos(t1 * halfPi);
            mid = Mathf.Sin(t1 * halfPi) * Mathf.Cos(t2 * halfPi);
            high = Mathf.Sin(t2 * halfPi);
        }

        /// <summary>
        /// Pitch de una capa: rpm / rpmReferencia, dentro del rango permitido del perfil.
        /// </summary>
        public static float ComputePitch(VehicleAudioProfile p, float rpm, float layerRpm)
        {
            if (layerRpm <= 0f) return 1f;
            return Mathf.Clamp(rpm / layerRpm, p.PitchRange.x, p.PitchRange.y);
        }
    }
}
