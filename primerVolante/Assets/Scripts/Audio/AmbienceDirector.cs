using System;
using System.Collections.Generic;
using PrimerVolante.Lessons;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Director del ambiente de la escena (US20); uno por escena. Cada frame calcula el peso de cada
    /// <see cref="AmbienceZone"/> según la posición del oyente (la cabeza del XR Origin), hace crossfade suave del
    /// lecho de cada perfil, dispara los one-shots dispersos 3D, fija el filtro de cabina cerrada, aplica el ducking
    /// del ambiente cuando suena una alerta y guarda el nivel de intensidad (<see cref="AmbienceIntensity"/>).
    /// </summary>
    /// <remarks>
    /// Sin director todo lo demás (NPC, alertas) funciona igual: sin ducking y con intensidad Normal.
    /// </remarks>
    [DisallowMultipleComponent]
    public class AmbienceDirector : MonoBehaviour
    {
        // Parámetros expuestos del EnvironmentAudioMixer.
        public const string ExteriorCutoffParameter = "ExteriorCutoff";
        public const string AlertsCutoffParameter = "AlertsCutoff";
        public const string AmbienceVolumeParameter = "AmbienceVolume";

        // Debajo de este peso una zona no dispara dispersos.
        private const float k_MinScatterWeight = 0.01f;

        // Debajo de este nivel un lecho se considera silencioso y se detiene.
        private const float k_SilentLevel = 0.001f;

        [Header("Mixer")]
        [Tooltip("EnvironmentAudioMixer. Debe exponer ExteriorCutoff, AlertsCutoff y AmbienceVolume.")]
        [SerializeField] private AudioMixer m_Mixer;

        [Tooltip("Grupo Ambience del mixer: lechos y dispersos.")]
        [SerializeField] private AudioMixerGroup m_AmbienceGroup;

        [Header("Zonas")]
        [Tooltip("Perfil que suena si el oyente queda fuera de todas las zonas (perímetro). Por defecto, el pueblo.")]
        [SerializeField] private AmbienceZoneProfile m_FallbackProfile;

        [Tooltip("Segundos que tarda un lecho en pasar de 0 a 1 (crossfade).")]
        [SerializeField] private float m_FadeSeconds = 2f;

        [Header("Cabina cerrada")]
        [Tooltip("Corte (Hz) del pasa-bajos del exterior: se oye como desde dentro del auto cerrado. " +
                 "Una tarea futura de ventanillas abribles cambiaría este valor.")]
        [SerializeField] private float m_ExteriorCutoffHz = 2200f;

        [Tooltip("Corte (Hz) del pasa-bajos de las alertas: más abierto que el exterior para que bocinas y sirenas siempre se oigan. " +
                 "Una tarea futura de ventanillas abribles cambiaría este valor.")]
        [SerializeField] private float m_AlertsCutoffHz = 6000f;

        [Header("Intensidad")]
        [Tooltip("Intensidad al empezar.")]
        [SerializeField] private AmbienceIntensity m_InitialIntensity = AmbienceIntensity.Normal;

        [Tooltip("Ganancia del lecho en dB para Calm (x), Normal (y) y Tense (z).")]
        [SerializeField] private Vector3 m_BedGainDb = new Vector3(-3f, 0f, 2f);

        [Tooltip("Multiplicador de la tasa de dispersos para Calm (x), Normal (y) y Tense (z).")]
        [SerializeField] private Vector3 m_ScatterRate = new Vector3(0.5f, 1f, 2f);

        [Tooltip("Segundos de bloqueo antes de que un NPC toque la bocina, para Calm (x), Normal (y) y Tense (z).")]
        [SerializeField] private Vector3 m_NpcHonkDelays = new Vector3(8f, 5f, 2.5f);

        [Tooltip("Segundos en verde antes de la bocina detrás del jugador, para Calm (x), Normal (y) y Tense (z). Un valor menor o igual a 0 la desactiva.")]
        [SerializeField] private Vector3 m_GreenHonkDelays = new Vector3(0f, 4f, 2f);

        [Tooltip("Segundos que tarda el lecho en pasar al nuevo nivel de ganancia (0 a 1).")]
        [SerializeField] private float m_IntensityGainSeconds = 2f;

        [Header("One-shots dispersos")]
        [Tooltip("Cantidad de AudioSource 3D del pool de dispersos.")]
        [SerializeField, Min(1)] private int m_ScatterPoolSize = 4;

        [Header("Ducking")]
        [Tooltip("Velocidad (dB/s) con la que baja el ambiente cuando suena una alerta.")]
        [SerializeField] private float m_DuckAttackDbPerSecond = 120f;

        [Tooltip("Velocidad (dB/s) con la que vuelve a 0 dB.")]
        [SerializeField] private float m_DuckReleaseDbPerSecond = 12f;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por cada cambio de intensidad y de ducking.")]
        [SerializeField] private bool m_LogEvents = false;

        private sealed class BedState
        {
            public AmbienceZoneProfile Profile;
            public AudioSource Source;
            public float Weight;            // peso normalizado actual (0..1)
            public float Level;             // nivel suavizado (0..1) de la ganancia de potencia
            public float NextScatterTime = -1f;
        }

        private readonly Dictionary<AmbienceZoneProfile, BedState> m_States = new Dictionary<AmbienceZoneProfile, BedState>();
        private readonly List<BedState> m_StateList = new List<BedState>();
        private readonly Dictionary<AmbienceZoneProfile, float> m_ScratchWeights = new Dictionary<AmbienceZoneProfile, float>();
        private readonly Dictionary<AmbienceZoneProfile, int> m_ScratchPriorities = new Dictionary<AmbienceZoneProfile, int>();
        private readonly List<AmbienceZoneProfile> m_ScratchProfiles = new List<AmbienceZoneProfile>();
        private float[] m_WeightBuffer = new float[8];
        private int[] m_PriorityBuffer = new int[8];
        private AudioSource[] m_ScatterSources;
        private System.Random m_Random;
        private LessonManager m_LessonManager;
        private bool m_Started;

        private AmbienceIntensity m_Intensity;
        private float m_BedGain = 1f;       // ganancia lineal actual de la intensidad
        private float m_DuckCurrentDb;
        private float m_DuckTargetDb;
        private float m_DuckHoldUntil = -1f;
        private float m_LastAppliedDuckDb = float.NaN;

        /// <summary>El director habilitado, o <c>null</c> si no hay ninguno.</summary>
        public static AmbienceDirector Instance { get; private set; }

        /// <summary>Se dispara cuando cambia la intensidad.</summary>
        public static event Action<AmbienceIntensity> IntensityChanged;

        /// <summary>Intensidad actual.</summary>
        public AmbienceIntensity Intensity => m_Intensity;

        /// <summary>Intensidad del director habilitado, o Normal si no hay.</summary>
        public static AmbienceIntensity CurrentIntensity => Instance != null ? Instance.m_Intensity : AmbienceIntensity.Normal;

        /// <summary>
        /// Segundos de bloqueo tras los cuales un NPC toca la bocina, según la intensidad actual (Normal si no hay director).
        /// </summary>
        public static float CurrentNpcHonkDelay =>
            Instance != null ? Pick(Instance.m_NpcHonkDelays, Instance.m_Intensity) : NPCAudioMath.DefaultNpcHonkDelays[(int)AmbienceIntensity.Normal];

        /// <summary>
        /// Segundos en verde tras los cuales suena la bocina detrás del jugador (0 o menos = desactivada), según la
        /// intensidad actual (Normal si no hay director).
        /// </summary>
        public static float CurrentGreenHonkDelay =>
            Instance != null ? Pick(Instance.m_GreenHonkDelays, Instance.m_Intensity) : NPCAudioMath.DefaultGreenHonkDelays[(int)AmbienceIntensity.Normal];

        /// <summary>Corte actual del filtro del exterior (Hz).</summary>
        public float ExteriorCutoffHz => m_ExteriorCutoffHz;

        /// <summary>Corte actual del filtro de las alertas (Hz).</summary>
        public float AlertsCutoffHz => m_AlertsCutoffHz;

        /// <summary>Ducking actual del ambiente en dB (0 o negativo).</summary>
        public float CurrentDuckDb => m_DuckCurrentDb;

        /// <summary>Perfil de respaldo para el perímetro.</summary>
        public AmbienceZoneProfile FallbackProfile
        {
            get => m_FallbackProfile;
            set => m_FallbackProfile = value;
        }

        /// <summary>Mixer del entorno.</summary>
        public AudioMixer Mixer => m_Mixer;

        /// <summary>Grupo Ambience del mixer.</summary>
        public AudioMixerGroup AmbienceGroup => m_AmbienceGroup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            IntensityChanged = null;
        }

        private static float Pick(Vector3 values, AmbienceIntensity intensity)
        {
            switch (intensity)
            {
                case AmbienceIntensity.Calm: return values.x;
                case AmbienceIntensity.Tense: return values.z;
                default: return values.y;
            }
        }

        private void Awake()
        {
            m_Random = new System.Random(EnvironmentAudioUtil.SeedFromPosition(transform.position) ^ 0x5EED);
            m_Intensity = m_InitialIntensity;
            m_BedGain = AmbienceMath.DbToLinear(Pick(m_BedGainDb, m_Intensity));
            CreateScatterPool();
        }

        private void OnEnable()
        {
            if (Instance == null || !Instance.isActiveAndEnabled)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Debug.LogWarning("[AmbienceDirector] ⚠️ Ya hay otro AmbienceDirector habilitado; este se ignora como Instance.", this);
            }

            if (m_Started) SubscribeToLessons();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= HandleLessonStepChanged;
                m_LessonManager = null;
            }

            // El ambiente no puede quedar con ducking pegado.
            ApplyDuck(0f);
            foreach (BedState state in m_StateList)
            {
                if (state.Source != null) state.Source.Stop();
            }
        }

        private void Start()
        {
            ApplyCabinFilter();
            m_Started = true;
            SubscribeToLessons();
        }

        // Integración opcional con lecciones: CityMerged3 no tiene LessonManager, así que si no hay no pasa nada.
        private void SubscribeToLessons()
        {
            if (m_LessonManager != null) return;

            m_LessonManager = FindAnyObjectByType<LessonManager>();
            if (m_LessonManager == null) return;

            m_LessonManager.StepChanged += HandleLessonStepChanged;

            // Si la lección ya arrancó antes de que este director se suscribiera, se aplica el paso actual.
            ApplyLessonOverride(m_LessonManager.CurrentStep);
        }

        private void HandleLessonStepChanged(LessonStepSO step, int index, int total)
        {
            ApplyLessonOverride(step);
        }

        private void ApplyLessonOverride(LessonStepSO step)
        {
            if (step == null || step.ambienceIntensity == AmbienceIntensityOverride.Unchanged) return;

            switch (step.ambienceIntensity)
            {
                case AmbienceIntensityOverride.Calm: SetIntensity(AmbienceIntensity.Calm); break;
                case AmbienceIntensityOverride.Normal: SetIntensity(AmbienceIntensity.Normal); break;
                case AmbienceIntensityOverride.Tense: SetIntensity(AmbienceIntensity.Tense); break;
            }
        }

        private void OnValidate()
        {
            m_FadeSeconds = Mathf.Max(0.05f, m_FadeSeconds);
            m_ExteriorCutoffHz = Mathf.Clamp(m_ExteriorCutoffHz, 10f, 22000f);
            m_AlertsCutoffHz = Mathf.Clamp(m_AlertsCutoffHz, 10f, 22000f);
            m_IntensityGainSeconds = Mathf.Max(0.05f, m_IntensityGainSeconds);
            m_DuckAttackDbPerSecond = Mathf.Max(1f, m_DuckAttackDbPerSecond);
            m_DuckReleaseDbPerSecond = Mathf.Max(1f, m_DuckReleaseDbPerSecond);
        }

        /// <summary>
        /// Fija los cortes de los pasa-bajos del mixer (exterior y alertas): el exterior se oye como desde la cabina
        /// cerrada. Una tarea futura de ventanillas abribles cambiaría estos valores en tiempo real.
        /// </summary>
        public void ApplyCabinFilter()
        {
            if (m_Mixer == null) return;
            m_Mixer.SetFloat(ExteriorCutoffParameter, m_ExteriorCutoffHz);
            m_Mixer.SetFloat(AlertsCutoffParameter, m_AlertsCutoffHz);
        }

        /// <summary>Cambia los cortes del filtro de cabina en tiempo real.</summary>
        public void SetCabinCutoffs(float exteriorHz, float alertsHz)
        {
            m_ExteriorCutoffHz = Mathf.Clamp(exteriorHz, 10f, 22000f);
            m_AlertsCutoffHz = Mathf.Clamp(alertsHz, 10f, 22000f);
            ApplyCabinFilter();
        }

        // ------------------------------------------------------------------ intensidad

        /// <summary>
        /// Cambia la intensidad del ambiente: escala el volumen del lecho, la tasa de dispersos y la impaciencia de
        /// los NPC. Dispara <see cref="IntensityChanged"/>.
        /// </summary>
        public void SetIntensity(AmbienceIntensity intensity)
        {
            if (intensity == m_Intensity) return;

            m_Intensity = intensity;
            if (m_LogEvents) Debug.Log($"[AmbienceDirector] 🎚️ Intensidad: {intensity}", this);
            IntensityChanged?.Invoke(intensity);
        }

        // ------------------------------------------------------------------ ducking

        /// <summary>
        /// Baja el volumen del ambiente <paramref name="amountDb"/> dB durante <paramref name="holdSeconds"/> segundos
        /// y vuelve suavemente a 0 dB. Lo llaman las alertas fuertes (bocina, chirrido, sirena, alarma), no el semáforo.
        /// Varios pedidos simultáneos se quedan con el más profundo.
        /// </summary>
        public void RequestDuck(float amountDb, float holdSeconds)
        {
            float target = -Mathf.Abs(amountDb);
            float now = Time.time;

            m_DuckTargetDb = now < m_DuckHoldUntil ? Mathf.Min(m_DuckTargetDb, target) : target;
            m_DuckHoldUntil = Mathf.Max(m_DuckHoldUntil, now + Mathf.Max(0f, holdSeconds));

            if (m_LogEvents) Debug.Log($"[AmbienceDirector] 🔉 Ducking {m_DuckTargetDb:F1} dB por {holdSeconds:F1} s", this);
        }

        /// <summary>Pide ducking al director habilitado; no hace nada si no hay.</summary>
        public static void Duck(float amountDb, float holdSeconds)
        {
            if (Instance != null) Instance.RequestDuck(amountDb, holdSeconds);
        }

        private void UpdateDuck(float deltaTime)
        {
            float desired = Time.time < m_DuckHoldUntil ? m_DuckTargetDb : 0f;
            float rate = desired < m_DuckCurrentDb ? m_DuckAttackDbPerSecond : m_DuckReleaseDbPerSecond;
            m_DuckCurrentDb = Mathf.MoveTowards(m_DuckCurrentDb, desired, rate * deltaTime);
            ApplyDuck(m_DuckCurrentDb);
        }

        private void ApplyDuck(float db)
        {
            if (m_Mixer == null) return;
            if (!float.IsNaN(m_LastAppliedDuckDb) && Mathf.Approximately(m_LastAppliedDuckDb, db)) return;

            m_LastAppliedDuckDb = db;
            m_Mixer.SetFloat(AmbienceVolumeParameter, db);
        }

        // ------------------------------------------------------------------ pesos

        /// <summary>
        /// Calcula los pesos normalizados de cada perfil para una posición: máximo de las zonas que comparten perfil,
        /// normalizados para que la suma no pase de 1 respetando la prioridad. Si la suma da 0 usa el perfil de respaldo.
        /// </summary>
        public void EvaluateWeights(Vector3 position, Dictionary<AmbienceZoneProfile, float> result)
        {
            result.Clear();
            m_ScratchWeights.Clear();
            m_ScratchPriorities.Clear();
            m_ScratchProfiles.Clear();

            IReadOnlyList<AmbienceZone> zones = AmbienceZone.Active;
            for (int i = 0; i < zones.Count; i++)
            {
                AmbienceZone zone = zones[i];
                if (zone == null || zone.Profile == null) continue;

                float w = zone.GetWeight(position);
                if (!m_ScratchWeights.TryGetValue(zone.Profile, out float known))
                {
                    m_ScratchProfiles.Add(zone.Profile);
                    m_ScratchWeights[zone.Profile] = w;
                    m_ScratchPriorities[zone.Profile] = zone.Priority;
                }
                else
                {
                    m_ScratchWeights[zone.Profile] = Mathf.Max(known, w);
                    m_ScratchPriorities[zone.Profile] = Mathf.Max(m_ScratchPriorities[zone.Profile], zone.Priority);
                }
            }

            int count = m_ScratchProfiles.Count;
            if (m_WeightBuffer.Length < count)
            {
                m_WeightBuffer = new float[count * 2];
                m_PriorityBuffer = new int[count * 2];
            }

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                m_WeightBuffer[i] = m_ScratchWeights[m_ScratchProfiles[i]];
                m_PriorityBuffer[i] = m_ScratchPriorities[m_ScratchProfiles[i]];
                sum += m_WeightBuffer[i];
            }

            AmbienceMath.NormalizeWeights(m_WeightBuffer, m_PriorityBuffer, count);

            if (sum <= 1e-4f && m_FallbackProfile != null)
            {
                result[m_FallbackProfile] = 1f;
                return;
            }

            for (int i = 0; i < count; i++) result[m_ScratchProfiles[i]] = m_WeightBuffer[i];
        }

        /// <summary>Peso normalizado actual de un perfil (0 si no suena).</summary>
        public float GetProfileWeight(AmbienceZoneProfile profile)
        {
            return profile != null && m_States.TryGetValue(profile, out BedState state) ? state.Weight : 0f;
        }

        /// <summary>Nivel suavizado actual (0..1) del lecho de un perfil.</summary>
        public float GetProfileLevel(AmbienceZoneProfile profile)
        {
            return profile != null && m_States.TryGetValue(profile, out BedState state) ? state.Level : 0f;
        }

        /// <summary>AudioSource del lecho de un perfil, o <c>null</c> si todavía no se creó.</summary>
        public AudioSource GetBedSource(AmbienceZoneProfile profile)
        {
            return profile != null && m_States.TryGetValue(profile, out BedState state) ? state.Source : null;
        }

        // ------------------------------------------------------------------ ciclo

        private readonly Dictionary<AmbienceZoneProfile, float> m_FrameWeights = new Dictionary<AmbienceZoneProfile, float>();

        private void Update()
        {
            float dt = Time.deltaTime;
            UpdateDuck(dt);

            // La ganancia de la intensidad se suaviza para que un cambio de paso no sea un salto.
            float gainTarget = AmbienceMath.DbToLinear(Pick(m_BedGainDb, m_Intensity));
            m_BedGain = Mathf.MoveTowards(m_BedGain, gainTarget, dt / m_IntensityGainSeconds);

            if (!EnvironmentAudioUtil.TryGetListenerPosition(out Vector3 listenerPos)) return;

            EvaluateWeights(listenerPos, m_FrameWeights);

            // Los perfiles que aparecen se crean; el resto baja a 0.
            foreach (KeyValuePair<AmbienceZoneProfile, float> pair in m_FrameWeights) GetOrCreateState(pair.Key);

            float now = Time.time;
            for (int i = 0; i < m_StateList.Count; i++)
            {
                BedState state = m_StateList[i];
                state.Weight = m_FrameWeights.TryGetValue(state.Profile, out float w) ? w : 0f;

                UpdateBed(state, dt);
                UpdateScatter(state, listenerPos, now);
            }
        }

        private BedState GetOrCreateState(AmbienceZoneProfile profile)
        {
            if (m_States.TryGetValue(profile, out BedState existing)) return existing;

            AudioSource source = new GameObject($"Bed_{profile.name}").AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = EnvironmentAudioUtil.PriorityBed;
            source.volume = 0f;
            if (m_AmbienceGroup != null) source.outputAudioMixerGroup = m_AmbienceGroup;

            var state = new BedState { Profile = profile, Source = source };
            m_States.Add(profile, state);
            m_StateList.Add(state);
            return state;
        }

        private void UpdateBed(BedState state, float dt)
        {
            AudioSource source = state.Source;
            AudioClip clip = state.Profile.BedLoop;

            // Sin clip esa fuente no suena.
            if (clip == null)
            {
                if (source.isPlaying) source.Stop();
                state.Level = 0f;
                return;
            }

            float target = AmbienceMath.EqualPowerGain(state.Weight);
            state.Level = AmbienceMath.SmoothVolume(state.Level, target, m_FadeSeconds, dt);
            source.volume = Mathf.Clamp01(state.Level * state.Profile.BedVolume * m_BedGain);

            if (state.Level > k_SilentLevel)
            {
                if (source.clip != clip) source.clip = clip;
                if (!source.isPlaying)
                {
                    // Arranca en un punto aleatorio para que no suene siempre igual al entrar.
                    source.Play();
                    if (clip.samples > 0) source.timeSamples = m_Random.Next(clip.samples);
                }
            }
            else if (state.Weight <= 0f && source.isPlaying)
            {
                source.Stop();
            }
        }

        // ------------------------------------------------------------------ dispersos

        private void CreateScatterPool()
        {
            int size = Mathf.Max(1, m_ScatterPoolSize);
            m_ScatterSources = new AudioSource[size];
            for (int i = 0; i < size; i++)
            {
                m_ScatterSources[i] = EnvironmentAudioUtil.CreateSource3D(
                    transform, $"Scatter_{i}", m_AmbienceGroup, EnvironmentAudioUtil.PriorityScatter, 8f, 120f);
            }
        }

        private void UpdateScatter(BedState state, Vector3 listenerPos, float now)
        {
            AmbienceZoneProfile profile = state.Profile;
            if (state.Weight < k_MinScatterWeight || !profile.HasScatter)
            {
                state.NextScatterTime = -1f;
                return;
            }

            float rate = Pick(m_ScatterRate, m_Intensity);

            if (state.NextScatterTime < 0f)
            {
                state.NextScatterTime = now + ScheduleInterval(profile, state.Weight, rate);
                return;
            }

            if (now < state.NextScatterTime) return;

            PlayScatter(profile, state.Weight, listenerPos);
            state.NextScatterTime = now + ScheduleInterval(profile, state.Weight, rate);
        }

        private float ScheduleInterval(AmbienceZoneProfile profile, float weight, float rate)
        {
            float baseInterval = EnvironmentAudioUtil.Range(m_Random, profile.ScatterMinInterval, profile.ScatterMaxInterval);
            float scaled = AmbienceMath.ScaledInterval(baseInterval, weight, rate);
            return float.IsInfinity(scaled) ? 3600f : scaled;
        }

        private void PlayScatter(AmbienceZoneProfile profile, float weight, Vector3 listenerPos)
        {
            AudioSource source = null;
            for (int i = 0; i < m_ScatterSources.Length; i++)
            {
                if (m_ScatterSources[i] != null && !m_ScatterSources[i].isPlaying)
                {
                    source = m_ScatterSources[i];
                    break;
                }
            }

            // Pool lleno: se salta este disparo.
            if (source == null) return;

            AudioClip clip = EnvironmentAudioProfileBase.PickClip(profile.ScatterClips, m_Random);
            if (clip == null) return;

            float angle = EnvironmentAudioUtil.Range(m_Random, 0f, Mathf.PI * 2f);
            float distance = EnvironmentAudioUtil.Range(m_Random, profile.ScatterMinDistance, profile.ScatterMaxDistance);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            source.transform.position = listenerPos + offset;
            source.minDistance = profile.ScatterMinRolloffDistance;
            source.maxDistance = Mathf.Max(profile.ScatterMaxRolloffDistance, distance * 1.2f);
            source.pitch = EnvironmentAudioUtil.Range(m_Random, profile.ScatterMinPitch, profile.ScatterMaxPitch);
            source.volume = Mathf.Clamp01(EnvironmentAudioUtil.Range(m_Random, profile.ScatterMinVolume, profile.ScatterMaxVolume)
                                          * AmbienceMath.EqualPowerGain(weight)
                                          * Mathf.Min(m_BedGain, 1.5f));
            source.clip = clip;
            source.loop = false;
            source.Play();
        }
    }
}
