using System;
using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Estados posibles para un semáforo vehicular.
    /// </summary>
    public enum TrafficLightState
    {
        Red,
        Yellow,
        Green
    }

    /// <summary>
    /// Controlador autónomo y desacoplado para semáforos e intersecciones.
    /// Soporta ciclos automáticos o disparados por eventos externos (triggers, directores de escena),
    /// sincronización visual mediante componentes Light / materiales emisivos, y notificación
    /// de cambios de estado vía C# Action y UnityEvent.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficLightController : MonoBehaviour
    {
        [Header("Tiempos de Ciclo (Segundos)")]
        [Tooltip("Duración de la fase de luz roja.")]
        [SerializeField] private float m_RedDuration = 8f;

        [Tooltip("Duración de la fase de luz amarilla.")]
        [SerializeField] private float m_YellowDuration = 2.5f;

        [Tooltip("Duración de la fase de luz verde.")]
        [SerializeField] private float m_GreenDuration = 8f;

        [Header("Modo de Inicio")]
        [Tooltip("Si es true, el semáforo comienza a ciclar automáticamente al iniciar la escena.")]
        [SerializeField] private bool m_AutoStart = true;

        [Tooltip("Estado inicial con el que comienza el semáforo.")]
        [SerializeField] private TrafficLightState m_InitialState = TrafficLightState.Red;

        [Header("Componentes Visuales de Luz")]
        [Tooltip("Luz Point/Spot correspondiente al foco rojo.")]
        [SerializeField] private Light m_RedLight;

        [Tooltip("Luz Point/Spot correspondiente al foco amarillo.")]
        [SerializeField] private Light m_YellowLight;

        [Tooltip("Luz Point/Spot correspondiente al foco verde.")]
        [SerializeField] private Light m_GreenLight;

        [Header("Emisividad de Materiales (Opcional)")]
        [Tooltip("Renderer objetivo para aplicar color emisivo.")]
        [SerializeField] private Renderer m_EmissiveRenderer;

        [Tooltip("Índice del sub-material a afectar en el Renderer.")]
        [SerializeField] private int m_EmissiveMaterialIndex = 0;

        [Tooltip("Nombre de la propiedad shader para emisión.")]
        [SerializeField] private string m_EmissionColorProperty = "_EmissionColor";

        [SerializeField] private Color m_RedEmissionColor = Color.red;
        [SerializeField] private Color m_YellowEmissionColor = new Color(1f, 0.7f, 0f);
        [SerializeField] private Color m_GreenEmissionColor = Color.green;
        [SerializeField] private Color m_OffEmissionColor = Color.black;

        [Header("Eventos")]
        [Tooltip("Evento Unity disparado al transicionar de estado.")]
        [SerializeField] public UnityEvent<TrafficLightState> onStateChangedEvent = new UnityEvent<TrafficLightState>();

        /// <summary>
        /// Evento C# disparado cada vez que el semáforo cambia de fase/estado.
        /// Diseñado para desacoplamiento reactivo con directores de tráfico o NPCs.
        /// </summary>
        public event Action<TrafficLightState> OnStateChanged;

        // Estado interno en tiempo de ejecución
        private TrafficLightState m_CurrentState = TrafficLightState.Red;
        private float m_TimeRemainingInState = 0f;
        private bool m_IsCycleActive = false;
        private bool m_IsPaused = false;
        private MaterialPropertyBlock m_PropertyBlock;
        private int m_EmissionColorPropId = 0;

        #region Propiedades Públicas

        /// <summary>Estado actual de la señal de tráfico.</summary>
        public TrafficLightState CurrentState => m_CurrentState;

        /// <summary>Tiempo restante en segundos para la fase actual.</summary>
        public float TimeRemainingInState => Mathf.Max(0f, m_TimeRemainingInState);

        /// <summary>Indica si el semáforo se encuentra en fase roja.</summary>
        public bool IsRed => m_CurrentState == TrafficLightState.Red;

        /// <summary>Indica si el semáforo se encuentra en fase amarilla.</summary>
        public bool IsYellow => m_CurrentState == TrafficLightState.Yellow;

        /// <summary>Indica si el semáforo se encuentra en fase verde.</summary>
        public bool IsGreen => m_CurrentState == TrafficLightState.Green;

        /// <summary>Indica si el ciclo del semáforo está pausado.</summary>
        public bool IsPaused => m_IsPaused;

        /// <summary>Indica si el ciclo del semáforo está activo y corriendo.</summary>
        public bool IsCycleActive => m_IsCycleActive;

        /// <summary>Duración configurable de la luz roja en segundos.</summary>
        public float RedDuration
        {
            get => m_RedDuration;
            set => m_RedDuration = Mathf.Max(0.1f, value);
        }

        /// <summary>Duración configurable de la luz amarilla en segundos.</summary>
        public float YellowDuration
        {
            get => m_YellowDuration;
            set => m_YellowDuration = Mathf.Max(0.1f, value);
        }

        /// <summary>Duración configurable de la luz verde en segundos.</summary>
        public float GreenDuration
        {
            get => m_GreenDuration;
            set => m_GreenDuration = Mathf.Max(0.1f, value);
        }

        /// <summary>Si es true, comienza su ciclo automáticamente en Start.</summary>
        public bool AutoStart
        {
            get => m_AutoStart;
            set => m_AutoStart = value;
        }

        /// <summary>Estado inicial configurado.</summary>
        public TrafficLightState InitialState
        {
            get => m_InitialState;
            set => m_InitialState = value;
        }

        public Light RedLight
        {
            get => m_RedLight;
            set => m_RedLight = value;
        }

        public Light YellowLight
        {
            get => m_YellowLight;
            set => m_YellowLight = value;
        }

        public Light GreenLight
        {
            get => m_GreenLight;
            set => m_GreenLight = value;
        }

        public Renderer EmissiveRenderer
        {
            get => m_EmissiveRenderer;
            set => m_EmissiveRenderer = value;
        }

        #endregion

        #region Ciclo de Vida de Unity

        private void Awake()
        {
            m_CurrentState = m_InitialState;
            m_TimeRemainingInState = GetDurationForState(m_InitialState);
            m_EmissionColorPropId = Shader.PropertyToID(m_EmissionColorProperty);
            m_PropertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (m_AutoStart)
            {
                TriggerStart(m_InitialState);
            }
            else
            {
                m_IsCycleActive = false;
                m_IsPaused = false;
                m_TimeRemainingInState = GetDurationForState(m_InitialState);
                UpdateVisuals();
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnValidate()
        {
            m_RedDuration = Mathf.Max(0.1f, m_RedDuration);
            m_YellowDuration = Mathf.Max(0.1f, m_YellowDuration);
            m_GreenDuration = Mathf.Max(0.1f, m_GreenDuration);

            if (!Application.isPlaying)
            {
                m_CurrentState = m_InitialState;
                m_TimeRemainingInState = GetDurationForState(m_InitialState);
                UpdateVisuals();
            }
        }

        #endregion

        #region Control y Métodos Públicos

        /// <summary>
        /// Inicia o reinicia el ciclo del semáforo forzando un estado inicial explícito (Green o Red).
        /// </summary>
        /// <param name="initialState">Estado con el que arranca el ciclo (por defecto Red).</param>
        public void TriggerStart(TrafficLightState initialState = TrafficLightState.Red)
        {
            m_CurrentState = initialState;
            m_TimeRemainingInState = GetDurationForState(initialState);
            m_IsCycleActive = true;
            m_IsPaused = false;

            UpdateVisuals();
            NotifyStateChanged();
        }

        /// <summary>
        /// Fuerza un estado específico en el semáforo y reinicia el temporizador de dicho estado.
        /// </summary>
        /// <param name="state">Nuevo estado a imponer.</param>
        public void SetState(TrafficLightState state)
        {
            m_CurrentState = state;
            m_TimeRemainingInState = GetDurationForState(state);

            UpdateVisuals();
            NotifyStateChanged();
        }

        /// <summary>
        /// Pausa la marcha del temporizador sin alterar el estado actual ni las luces.
        /// </summary>
        public void PauseCycle()
        {
            m_IsPaused = true;
        }

        /// <summary>
        /// Reanuda el avance del ciclo si estaba pausado.
        /// </summary>
        public void ResumeCycle()
        {
            m_IsPaused = false;
        }

        /// <summary>
        /// Reinicia el ciclo a la configuración inicial.
        /// </summary>
        public void ResetCycle()
        {
            m_CurrentState = m_InitialState;
            m_TimeRemainingInState = GetDurationForState(m_InitialState);
            m_IsPaused = false;
            m_IsCycleActive = m_AutoStart;

            UpdateVisuals();
            NotifyStateChanged();
        }

        /// <summary>
        /// Avanza el tiempo del semáforo de manera determinista por una cantidad de segundos.
        /// Permite ejecución en Update() y pruebas unitarias instantáneas sin depender de tiempo real.
        /// </summary>
        /// <param name="deltaTime">Delta de tiempo en segundos.</param>
        public void Tick(float deltaTime)
        {
            if (!m_IsCycleActive || m_IsPaused || deltaTime <= 0f)
            {
                return;
            }

            m_TimeRemainingInState -= deltaTime;

            while (m_TimeRemainingInState <= 0f)
            {
                TrafficLightState nextState = GetNextState(m_CurrentState);
                float nextDuration = GetDurationForState(nextState);

                if (nextDuration <= 0f)
                {
                    m_TimeRemainingInState = 0f;
                    break;
                }

                m_CurrentState = nextState;
                m_TimeRemainingInState += nextDuration;

                UpdateVisuals();
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// Obtiene el estado subsiguiente en el ciclo estándar:
        /// Green -> Yellow -> Red -> Green.
        /// </summary>
        public static TrafficLightState GetNextState(TrafficLightState current)
        {
            switch (current)
            {
                case TrafficLightState.Green:
                    return TrafficLightState.Yellow;
                case TrafficLightState.Yellow:
                    return TrafficLightState.Red;
                case TrafficLightState.Red:
                    return TrafficLightState.Green;
                default:
                    return TrafficLightState.Red;
            }
        }

        /// <summary>
        /// Retorna la duración configurada para un estado determinado.
        /// </summary>
        public float GetDurationForState(TrafficLightState state)
        {
            switch (state)
            {
                case TrafficLightState.Green:
                    return m_GreenDuration;
                case TrafficLightState.Yellow:
                    return m_YellowDuration;
                case TrafficLightState.Red:
                    return m_RedDuration;
                default:
                    return m_RedDuration;
            }
        }

        /// <summary>
        /// Actualiza la visualización de las luces físicas y del material emisivo opcional.
        /// </summary>
        public void UpdateVisuals()
        {
            if (m_RedLight != null)
            {
                m_RedLight.enabled = (m_CurrentState == TrafficLightState.Red);
            }

            if (m_YellowLight != null)
            {
                m_YellowLight.enabled = (m_CurrentState == TrafficLightState.Yellow);
            }

            if (m_GreenLight != null)
            {
                m_GreenLight.enabled = (m_CurrentState == TrafficLightState.Green);
            }

            UpdateEmissiveMaterial();
        }

        #endregion

        #region Métodos Privados

        private void NotifyStateChanged()
        {
            OnStateChanged?.Invoke(m_CurrentState);
            onStateChangedEvent?.Invoke(m_CurrentState);
        }

        private void UpdateEmissiveMaterial()
        {
            if (m_EmissiveRenderer == null)
            {
                return;
            }

            if (m_PropertyBlock == null)
            {
                m_PropertyBlock = new MaterialPropertyBlock();
            }

            if (m_EmissionColorPropId == 0)
            {
                m_EmissionColorPropId = Shader.PropertyToID(m_EmissionColorProperty);
            }

            Color activeColor = m_OffEmissionColor;
            switch (m_CurrentState)
            {
                case TrafficLightState.Red:
                    activeColor = m_RedEmissionColor;
                    break;
                case TrafficLightState.Yellow:
                    activeColor = m_YellowEmissionColor;
                    break;
                case TrafficLightState.Green:
                    activeColor = m_GreenEmissionColor;
                    break;
            }

            m_EmissiveRenderer.GetPropertyBlock(m_PropertyBlock, m_EmissiveMaterialIndex);
            m_PropertyBlock.SetColor(m_EmissionColorPropId, activeColor);
            m_EmissiveRenderer.SetPropertyBlock(m_PropertyBlock, m_EmissiveMaterialIndex);
        }

        #endregion
    }
}
