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

        [Tooltip("Duración de la fase de luz amarilla (Verde -> Rojo).")]
        [SerializeField] private float m_YellowDuration = 2.5f;

        [Tooltip("Duración de la fase de luz amarilla previa a verde (Rojo -> Amarillo -> Verde). Si es <= 0, usa m_YellowDuration.")]
        [SerializeField] private float m_YellowToGreenDuration = 2.0f;

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

        [Header("Lentes Visuales de Focos")]
        [Tooltip("Renderer de la lente física para el foco rojo.")]
        [SerializeField] private MeshRenderer m_RedLensRenderer;

        [Tooltip("Renderer de la lente física para el foco amarillo.")]
        [SerializeField] private MeshRenderer m_YellowLensRenderer;

        [Tooltip("Renderer de la lente física para el foco verde.")]
        [SerializeField] private MeshRenderer m_GreenLensRenderer;

        [Header("Colores de Lentes (Activo vs Atenuado)")]
        [Tooltip("Color HDR vívido para la lente roja activa.")]
        [SerializeField] private Color m_RedActiveColor = new Color(1.5f, 0.15f, 0.15f, 1f);

        [Tooltip("Color atenuado/apagado para la lente roja inactiva.")]
        [SerializeField] private Color m_RedDimmedColor = new Color(0.18f, 0.03f, 0.03f, 1f);

        [Tooltip("Color HDR vívido para la lente amarilla activa.")]
        [SerializeField] private Color m_YellowActiveColor = new Color(1.5f, 1.125f, 0.075f, 1f);

        [Tooltip("Color atenuado/apagado para la lente amarilla inactiva.")]
        [SerializeField] private Color m_YellowDimmedColor = new Color(0.18f, 0.13f, 0.01f, 1f);

        [Tooltip("Color HDR vívido para la lente verde activa.")]
        [SerializeField] private Color m_GreenActiveColor = new Color(0.15f, 1.5f, 0.525f, 1f);

        [Tooltip("Color atenuado/apagado para la lente verde inactiva.")]
        [SerializeField] private Color m_GreenDimmedColor = new Color(0.01f, 0.18f, 0.05f, 1f);

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
        private TrafficLightState m_PreviousState = TrafficLightState.Yellow;
        private float m_TimeRemainingInState = 0f;
        private bool m_IsCycleActive = false;
        private bool m_IsPaused = false;
        private MaterialPropertyBlock m_PropertyBlock;
        private MaterialPropertyBlock m_LensPropertyBlock;
        private int m_EmissionColorPropId = 0;

        #region Propiedades Públicas

        /// <summary>Estado actual de la señal de tráfico.</summary>
        public TrafficLightState CurrentState => m_CurrentState;

        /// <summary>Estado inmediatamente anterior en el ciclo del semáforo.</summary>
        public TrafficLightState PreviousState => m_PreviousState;

        /// <summary>Indica si el semáforo se encuentra en la fase amarilla preparatoria previa a pasar a verde.</summary>
        public bool IsTransitioningToGreen => (m_CurrentState == TrafficLightState.Yellow && m_PreviousState == TrafficLightState.Red);

        /// <summary>Próximo estado objetivo hacia el cual se dirige el ciclo actual.</summary>
        public TrafficLightState TargetState
        {
            get
            {
                if (m_CurrentState == TrafficLightState.Green) return TrafficLightState.Yellow;
                if (m_CurrentState == TrafficLightState.Red) return TrafficLightState.Yellow;
                return (m_PreviousState == TrafficLightState.Red) ? TrafficLightState.Green : TrafficLightState.Red;
            }
        }

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

        /// <summary>Duración configurable de la luz amarilla (Verde -> Rojo) en segundos.</summary>
        public float YellowDuration
        {
            get => m_YellowDuration;
            set => m_YellowDuration = Mathf.Max(0.1f, value);
        }

        /// <summary>Duración configurable de la luz amarilla previa a verde (Rojo -> Verde) en segundos.</summary>
        public float YellowToGreenDuration
        {
            get => (m_YellowToGreenDuration > 0f) ? m_YellowToGreenDuration : m_YellowDuration;
            set => m_YellowToGreenDuration = Mathf.Max(0.1f, value);
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

        public MeshRenderer RedLensRenderer
        {
            get => m_RedLensRenderer;
            set => m_RedLensRenderer = value;
        }

        public MeshRenderer YellowLensRenderer
        {
            get => m_YellowLensRenderer;
            set => m_YellowLensRenderer = value;
        }

        public MeshRenderer GreenLensRenderer
        {
            get => m_GreenLensRenderer;
            set => m_GreenLensRenderer = value;
        }

        public Color RedActiveColor
        {
            get => m_RedActiveColor;
            set => m_RedActiveColor = value;
        }

        public Color RedDimmedColor
        {
            get => m_RedDimmedColor;
            set => m_RedDimmedColor = value;
        }

        public Color YellowActiveColor
        {
            get => m_YellowActiveColor;
            set => m_YellowActiveColor = value;
        }

        public Color YellowDimmedColor
        {
            get => m_YellowDimmedColor;
            set => m_YellowDimmedColor = value;
        }

        public Color GreenActiveColor
        {
            get => m_GreenActiveColor;
            set => m_GreenActiveColor = value;
        }

        public Color GreenDimmedColor
        {
            get => m_GreenDimmedColor;
            set => m_GreenDimmedColor = value;
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
            m_PreviousState = (m_InitialState == TrafficLightState.Green) ? TrafficLightState.Red : TrafficLightState.Green;
            m_TimeRemainingInState = GetDurationForState(m_InitialState, m_PreviousState);
            m_EmissionColorPropId = Shader.PropertyToID(m_EmissionColorProperty);
            m_PropertyBlock = new MaterialPropertyBlock();
            m_LensPropertyBlock = new MaterialPropertyBlock();
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
                m_TimeRemainingInState = GetDurationForState(m_InitialState, m_PreviousState);
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
            if (m_YellowToGreenDuration > 0f)
            {
                m_YellowToGreenDuration = Mathf.Max(0.1f, m_YellowToGreenDuration);
            }

            if (!Application.isPlaying)
            {
                m_CurrentState = m_InitialState;
                m_PreviousState = (m_InitialState == TrafficLightState.Green) ? TrafficLightState.Red : TrafficLightState.Green;
                m_TimeRemainingInState = GetDurationForState(m_InitialState, m_PreviousState);
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
            m_PreviousState = (initialState == TrafficLightState.Green) ? TrafficLightState.Yellow : TrafficLightState.Green;
            m_TimeRemainingInState = GetDurationForState(initialState, m_PreviousState);
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
            if (state == TrafficLightState.Yellow)
            {
                if (m_CurrentState == TrafficLightState.Red || m_CurrentState == TrafficLightState.Green)
                {
                    m_PreviousState = m_CurrentState;
                }
            }
            else
            {
                m_PreviousState = (state == TrafficLightState.Green) ? TrafficLightState.Yellow : TrafficLightState.Green;
            }

            m_CurrentState = state;
            m_TimeRemainingInState = GetDurationForState(state, m_PreviousState);

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
            m_PreviousState = (m_InitialState == TrafficLightState.Green) ? TrafficLightState.Red : TrafficLightState.Green;
            m_TimeRemainingInState = GetDurationForState(m_InitialState, m_PreviousState);
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
                TrafficLightState nextState = GetNextState(m_CurrentState, m_PreviousState);
                float nextDuration = GetDurationForState(nextState, m_CurrentState);

                if (nextDuration <= 0f)
                {
                    m_TimeRemainingInState = 0f;
                    break;
                }

                m_PreviousState = m_CurrentState;
                m_CurrentState = nextState;
                m_TimeRemainingInState += nextDuration;

                UpdateVisuals();
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// Obtiene el estado subsiguiente a partir del estado actual y previo del controlador.
        /// </summary>
        public TrafficLightState GetNextState()
        {
            return GetNextState(m_CurrentState, m_PreviousState);
        }

        /// <summary>
        /// Obtiene el estado subsiguiente en la secuencia de ciclo:
        /// Green -> Yellow -> Red -> Yellow -> Green...
        /// De Verde a Rojo pasa por Amarillo; de Rojo a Verde también pasa por Amarillo.
        /// </summary>
        public static TrafficLightState GetNextState(TrafficLightState current, TrafficLightState previous = TrafficLightState.Green)
        {
            switch (current)
            {
                case TrafficLightState.Green:
                    return TrafficLightState.Yellow;
                case TrafficLightState.Yellow:
                    return (previous == TrafficLightState.Red) ? TrafficLightState.Green : TrafficLightState.Red;
                case TrafficLightState.Red:
                    return TrafficLightState.Yellow;
                default:
                    return TrafficLightState.Red;
            }
        }

        /// <summary>
        /// Retorna la duración configurada para un estado determinado, considerando el estado previo si es Yellow.
        /// </summary>
        public float GetDurationForState(TrafficLightState state)
        {
            return GetDurationForState(state, m_PreviousState);
        }

        /// <summary>
        /// Retorna la duración configurada para un estado determinado según el estado precedente.
        /// Permite duración independiente para la fase amarilla de advertencia previa a verde.
        /// </summary>
        public float GetDurationForState(TrafficLightState state, TrafficLightState previousState)
        {
            switch (state)
            {
                case TrafficLightState.Green:
                    return m_GreenDuration;
                case TrafficLightState.Yellow:
                    if (previousState == TrafficLightState.Red && m_YellowToGreenDuration > 0f)
                    {
                        return m_YellowToGreenDuration;
                    }
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

            UpdateLensMaterials();
            UpdateEmissiveMaterial();
        }

        #endregion

        #region Métodos Privados

        private static readonly int s_BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorPropId = Shader.PropertyToID("_Color");
        private static readonly int s_EmissionColorPropId = Shader.PropertyToID("_EmissionColor");

        private void NotifyStateChanged()
        {
            OnStateChanged?.Invoke(m_CurrentState);
            onStateChangedEvent?.Invoke(m_CurrentState);
        }

        private void UpdateLensMaterials()
        {
            Color redColor = (m_CurrentState == TrafficLightState.Red) ? m_RedActiveColor : m_RedDimmedColor;
            Color yellowColor = (m_CurrentState == TrafficLightState.Yellow) ? m_YellowActiveColor : m_YellowDimmedColor;
            Color greenColor = (m_CurrentState == TrafficLightState.Green) ? m_GreenActiveColor : m_GreenDimmedColor;

            Color redEmission = (m_CurrentState == TrafficLightState.Red) ? m_RedActiveColor : Color.black;
            Color yellowEmission = (m_CurrentState == TrafficLightState.Yellow) ? m_YellowActiveColor : Color.black;
            Color greenEmission = (m_CurrentState == TrafficLightState.Green) ? m_GreenActiveColor : Color.black;

            ApplyLensColor(m_RedLensRenderer, redColor, redEmission);
            ApplyLensColor(m_YellowLensRenderer, yellowColor, yellowEmission);
            ApplyLensColor(m_GreenLensRenderer, greenColor, greenEmission);
        }

        private void ApplyLensColor(MeshRenderer lensRenderer, Color baseColor, Color emissionColor)
        {
            if (lensRenderer == null)
            {
                return;
            }

            if (m_LensPropertyBlock == null)
            {
                m_LensPropertyBlock = new MaterialPropertyBlock();
            }

            lensRenderer.GetPropertyBlock(m_LensPropertyBlock);
            m_LensPropertyBlock.SetColor(s_BaseColorPropId, baseColor);
            m_LensPropertyBlock.SetColor(s_ColorPropId, baseColor);
            m_LensPropertyBlock.SetColor(s_EmissionColorPropId, emissionColor);
            lensRenderer.SetPropertyBlock(m_LensPropertyBlock);
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

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            DrawTrafficLightGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawTrafficLightGizmos(true);
        }

        private void DrawTrafficLightGizmos(bool isSelected)
        {
            Vector3 redPos = m_RedLensRenderer != null ? m_RedLensRenderer.transform.position : (m_RedLight != null ? m_RedLight.transform.position : Vector3.zero);
            Vector3 yellowPos = m_YellowLensRenderer != null ? m_YellowLensRenderer.transform.position : (m_YellowLight != null ? m_YellowLight.transform.position : Vector3.zero);
            Vector3 greenPos = m_GreenLensRenderer != null ? m_GreenLensRenderer.transform.position : (m_GreenLight != null ? m_GreenLight.transform.position : Vector3.zero);

            bool hasBulbPositions = (m_RedLensRenderer != null || m_RedLight != null);
            if (!hasBulbPositions) return;

            float activeRadius = 0.14f;
            float dimmedRadius = 0.09f;

            // Foco Rojo
            if (m_CurrentState == TrafficLightState.Red)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(redPos, activeRadius);
            }
            else
            {
                Gizmos.color = new Color(0.45f, 0.05f, 0.05f, 0.6f);
                Gizmos.DrawWireSphere(redPos, dimmedRadius);
            }

            // Foco Amarillo
            if (m_CurrentState == TrafficLightState.Yellow)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(yellowPos, activeRadius);
            }
            else
            {
                Gizmos.color = new Color(0.45f, 0.35f, 0.05f, 0.6f);
                Gizmos.DrawWireSphere(yellowPos, dimmedRadius);
            }

            // Foco Verde
            if (m_CurrentState == TrafficLightState.Green)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(greenPos, activeRadius);
            }
            else
            {
                Gizmos.color = new Color(0.05f, 0.40f, 0.10f, 0.6f);
                Gizmos.DrawWireSphere(greenPos, dimmedRadius);
            }

            // Etiquetas y depuración en vista de Escena
            if (Application.isPlaying || isSelected)
            {
                Vector3 labelPos = redPos + Vector3.up * 0.35f;
                GUIStyle style = new GUIStyle();
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 12;
                style.fontStyle = FontStyle.Bold;

                string stateText;
                switch (m_CurrentState)
                {
                    case TrafficLightState.Red:
                        style.normal.textColor = new Color(1f, 0.3f, 0.3f);
                        stateText = "RED";
                        break;
                    case TrafficLightState.Yellow:
                        style.normal.textColor = new Color(1f, 0.85f, 0.2f);
                        stateText = (m_PreviousState == TrafficLightState.Red) ? "YELLOW (->GREEN)" : "YELLOW (->RED)";
                        break;
                    case TrafficLightState.Green:
                        style.normal.textColor = new Color(0.3f, 1f, 0.4f);
                        stateText = "GREEN";
                        break;
                    default:
                        style.normal.textColor = Color.white;
                        stateText = m_CurrentState.ToString();
                        break;
                }

                string text = Application.isPlaying
                    ? $"[{stateText}] {TimeRemainingInState:F1}s"
                    : $"[{stateText}]";

                UnityEditor.Handles.Label(labelPos, text, style);
            }
        }
#endif
    }
}
