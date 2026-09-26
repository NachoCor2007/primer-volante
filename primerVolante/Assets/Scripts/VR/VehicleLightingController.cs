using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Orquestador central del sistema integral de iluminación vehicular exterior e interior.
    /// Escucha la perilla VRHeadlightKnob, la marcha (GearState) y la presión de frenado (BrakeValue)
    /// desde VehicleController, sincronizando ópticas emisivas, Spotlights 3D y el tablero digital.
    /// </summary>
    [ExecuteAlways]
    public class VehicleLightingController : MonoBehaviour
    {
        [Header("Dependencias")]
        [Tooltip("Controlador físico/cinemático del vehículo. Si se deja vacío se busca en este objeto o en padres/escena.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Perilla rotativa de 3 posiciones en cabina.")]
        [SerializeField] private VRHeadlightKnob m_HeadlightKnob;

        [Tooltip("Controlador del cluster/tablero diegético.")]
        [SerializeField] private DashboardUIController m_DashboardUI;

        [Tooltip("Controlador de guiños y balizas.")]
        [SerializeField] private TurnSignalLightController m_TurnSignalController;

        [Header("Iluminación Delantera - Ópticas Emisivas")]
        [Tooltip("Faros externos delanteros (se encienden en Bajas y Altas).")]
        [SerializeField] private Renderer[] m_FrontOuterHeadlights;

        [Tooltip("Faros internos delanteros (se encienden únicamente en Altas).")]
        [SerializeField] private Renderer[] m_FrontInnerHeadlights;

        [Tooltip("Color base de las ópticas delanteras al estar apagadas.")]
        [SerializeField] private Color m_FrontOffColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        [Tooltip("Color emisivo de luces bajas (blanco/cálido).")]
        [SerializeField] private Color m_LowBeamColor = new Color(1f, 0.95f, 0.88f, 1f);

        [Tooltip("Intensidad de emisión de luces bajas.")]
        [SerializeField] private float m_LowBeamEmissionIntensity = 2.5f;

        [Tooltip("Color emisivo de luces altas (blanco frío de alta luminosidad).")]
        [SerializeField] private Color m_HighBeamColor = new Color(0.96f, 0.98f, 1f, 1f);

        [Tooltip("Intensidad de emisión de luces altas.")]
        [SerializeField] private float m_HighBeamEmissionIntensity = 4.5f;

        [Header("Iluminación Delantera - Spotlights 3D")]
        [Tooltip("2 Spotlights de luces bajas (alcance ~25m, ángulo ancho, ligera inclinación hacia abajo).")]
        [SerializeField] private Light[] m_LowBeamSpotlights;

        [Tooltip("2 Spotlights de luces altas (alcance ~60m, ángulo concentrado, haz horizontal recto).")]
        [SerializeField] private Light[] m_HighBeamSpotlights;

        [Header("Iluminación Trasera - Ópticas Emisivas")]
        [Tooltip("Ópticas traseras de posición / freno (rojo).")]
        [SerializeField] private Renderer[] m_RearTailBrakeLights;

        [Tooltip("Tercera luz de freno central (opcional).")]
        [SerializeField] private Renderer m_ThirdBrakeLight;

        [Tooltip("Color apagado de los faros traseros.")]
        [SerializeField] private Color m_RearOffColor = new Color(0.3f, 0.05f, 0.05f, 1f);

        [Tooltip("Color rojo de posición / marcha.")]
        [SerializeField] private Color m_TailLightColor = new Color(1f, 0.08f, 0.08f, 1f);

        [Tooltip("Intensidad de emisión tenue de posición (en Bajas o Altas).")]
        [SerializeField] private float m_TailLightIntensity = 0.7f;

        [Tooltip("Intensidad de emisión máxima de freno (Stop).")]
        [SerializeField] private float m_BrakeLightIntensity = 4.0f;

        [Tooltip("Umbral de freno en VehicleController para encender luces de freno (default 0.05).")]
        [SerializeField] private float m_BrakeThreshold = 0.05f;

        [Header("Luz de Marcha Atrás (Reversa)")]
        [Tooltip("Sectores blancos traseros que se iluminan al poner Reverse.")]
        [SerializeField] private Renderer[] m_ReverseLights;

        [Tooltip("Color apagado de las ópticas de marcha atrás.")]
        [SerializeField] private Color m_ReverseOffColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        [Tooltip("Color emisivo blanco de marcha atrás.")]
        [SerializeField] private Color m_ReverseOnColor = new Color(0.95f, 0.98f, 1f, 1f);

        [Tooltip("Intensidad emisiva de marcha atrás.")]
        [SerializeField] private float m_ReverseEmissionIntensity = 3.0f;

        [Tooltip("Luz 3D auxiliar de retroceso.")]
        [SerializeField] private Light m_ReverseSpotlight;

        [Header("Eventos Desacoplados")]
        public HeadlightModeEvent OnHeadlightModeChanged = new HeadlightModeEvent();
        public UnityEvent<bool> OnBrakeLightsChanged = new UnityEvent<bool>();
        public UnityEvent<bool> OnReverseLightsChanged = new UnityEvent<bool>();

        // Materiales instanciados para control dinámico de emisión
        private readonly List<Material> m_InstancedMaterials = new List<Material>();
        private readonly List<Material> m_OuterHeadlightMaterials = new List<Material>();
        private readonly List<Material> m_InnerHeadlightMaterials = new List<Material>();
        private readonly List<Material> m_TailBrakeMaterials = new List<Material>();
        private readonly List<Material> m_ReverseMaterials = new List<Material>();

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private HeadlightMode m_CurrentMode = HeadlightMode.Off;
        private bool m_BrakeActive = false;
        private bool m_ReverseActive = false;

        public HeadlightMode CurrentHeadlightMode => m_CurrentMode;
        public bool IsBrakeActive => m_BrakeActive;
        public bool IsReverseActive => m_ReverseActive;

        private void Awake()
        {
            FindDependencies();
            SetupInstancedMaterials();
        }

        private void OnEnable()
        {
            if (m_HeadlightKnob != null)
            {
                m_HeadlightKnob.OnModeChanged.AddListener(OnKnobModeChanged);
            }
        }

        private void OnDisable()
        {
            if (m_HeadlightKnob != null)
            {
                m_HeadlightKnob.OnModeChanged.RemoveListener(OnKnobModeChanged);
            }
        }

        private void OnDestroy()
        {
            // Limpiar materiales instanciados en runtime
            if (Application.isPlaying)
            {
                foreach (var mat in m_InstancedMaterials)
                {
                    if (mat != null) Destroy(mat);
                }
            }
            m_InstancedMaterials.Clear();
            m_OuterHeadlightMaterials.Clear();
            m_InnerHeadlightMaterials.Clear();
            m_TailBrakeMaterials.Clear();
            m_ReverseMaterials.Clear();
        }

        private void Start()
        {
            HeadlightMode initialMode = m_HeadlightKnob != null ? m_HeadlightKnob.CurrentMode : HeadlightMode.Off;
            SetHeadlightMode(initialMode);
            UpdateBrakeAndReverse(force: true);
        }

        private void Update()
        {
            UpdateBrakeAndReverse(force: false);
        }

        private void FindDependencies()
        {
            if (m_VehicleController == null)
            {
                m_VehicleController = GetComponent<VehicleController>();
                if (m_VehicleController == null)
                    m_VehicleController = GetComponentInParent<VehicleController>();
                if (m_VehicleController == null)
                    m_VehicleController = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            }

            if (m_HeadlightKnob == null)
            {
                m_HeadlightKnob = GetComponentInChildren<VRHeadlightKnob>(true);
                if (m_HeadlightKnob == null)
                    m_HeadlightKnob = UnityEngine.Object.FindAnyObjectByType<VRHeadlightKnob>();
            }

            if (m_DashboardUI == null)
            {
                m_DashboardUI = GetComponentInChildren<DashboardUIController>(true);
                if (m_DashboardUI == null)
                    m_DashboardUI = UnityEngine.Object.FindAnyObjectByType<DashboardUIController>();
            }

            if (m_TurnSignalController == null)
            {
                m_TurnSignalController = GetComponentInChildren<TurnSignalLightController>(true);
            }
        }

        private void SetupInstancedMaterials()
        {
            m_OuterHeadlightMaterials.Clear();
            m_InnerHeadlightMaterials.Clear();
            m_TailBrakeMaterials.Clear();
            m_ReverseMaterials.Clear();

            RegisterRendererList(m_FrontOuterHeadlights, m_OuterHeadlightMaterials);
            RegisterRendererList(m_FrontInnerHeadlights, m_InnerHeadlightMaterials);
            RegisterRendererList(m_RearTailBrakeLights, m_TailBrakeMaterials);

            if (m_ThirdBrakeLight != null)
            {
                RegisterRenderer(m_ThirdBrakeLight, m_TailBrakeMaterials);
            }

            RegisterRendererList(m_ReverseLights, m_ReverseMaterials);
        }

        private void RegisterRendererList(Renderer[] renderers, List<Material> targetList)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r != null) RegisterRenderer(r, targetList);
            }
        }

        private void RegisterRenderer(Renderer renderer, List<Material> targetList)
        {
            if (renderer == null) return;

            Material mat = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
            if (mat != null)
            {
                if (Application.isPlaying)
                {
                    m_InstancedMaterials.Add(mat);
                }
                targetList.Add(mat);
            }
        }

        /// <summary>
        /// Recibe la señal de la perilla rotativa VRHeadlightKnob.
        /// </summary>
        public void OnKnobModeChanged(HeadlightMode newMode)
        {
            SetHeadlightMode(newMode);
        }

        /// <summary>
        /// Aplica un nuevo modo de iluminación en todo el vehículo.
        /// </summary>
        public void SetHeadlightMode(HeadlightMode mode)
        {
            m_CurrentMode = mode;

            ApplyFrontLighting(m_CurrentMode);
            ApplyRearTailLighting(m_CurrentMode, m_BrakeActive);

            if (m_DashboardUI != null)
            {
                m_DashboardUI.SetHeadlightMode(m_CurrentMode);
            }

            OnHeadlightModeChanged?.Invoke(m_CurrentMode);
        }

        /// <summary>
        /// Monitorea freno (BrakeValue) y marcha atrás (GearState.Reverse) en tiempo real.
        /// </summary>
        private void UpdateBrakeAndReverse(bool force)
        {
            if (m_VehicleController == null) return;

            bool isBraking = (m_VehicleController.BrakeValue > m_BrakeThreshold);
            if (isBraking != m_BrakeActive || force)
            {
                m_BrakeActive = isBraking;
                ApplyRearTailLighting(m_CurrentMode, m_BrakeActive);
                OnBrakeLightsChanged?.Invoke(m_BrakeActive);
            }

            bool isReverse = (m_VehicleController.CurrentGear == GearState.Reverse);
            if (isReverse != m_ReverseActive || force)
            {
                m_ReverseActive = isReverse;
                ApplyReverseLighting(m_ReverseActive);
                OnReverseLightsChanged?.Invoke(m_ReverseActive);
            }
        }

        /// <summary>
        /// Aplica la lógica delantera (faros dobles frontales y spotlights 3D).
        /// </summary>
        private void ApplyFrontLighting(HeadlightMode mode)
        {
            switch (mode)
            {
                case HeadlightMode.Off:
                    // Emisivos apagados
                    SetMaterialsState(m_OuterHeadlightMaterials, m_FrontOffColor, Color.black, 0f, false);
                    SetMaterialsState(m_InnerHeadlightMaterials, m_FrontOffColor, Color.black, 0f, false);

                    // Spotlights apagados
                    SetLightsEnabled(m_LowBeamSpotlights, false);
                    SetLightsEnabled(m_HighBeamSpotlights, false);
                    break;

                case HeadlightMode.LowBeam:
                    // Ópticas externas: blanco/cálido activo
                    SetMaterialsState(m_OuterHeadlightMaterials, Color.white, m_LowBeamColor, m_LowBeamEmissionIntensity, true);
                    // Ópticas internas: apagadas
                    SetMaterialsState(m_InnerHeadlightMaterials, m_FrontOffColor, Color.black, 0f, false);

                    // 2 Spotlights de alcance medio encendidos
                    SetLightsEnabled(m_LowBeamSpotlights, true);
                    SetLightsEnabled(m_HighBeamSpotlights, false);
                    break;

                case HeadlightMode.HighBeam:
                    // 4 faros (externas e internas): alta intensidad activa
                    SetMaterialsState(m_OuterHeadlightMaterials, Color.white, m_HighBeamColor, m_HighBeamEmissionIntensity, true);
                    SetMaterialsState(m_InnerHeadlightMaterials, Color.white, m_HighBeamColor, m_HighBeamEmissionIntensity, true);

                    // Luces bajas + altas encendidas simultáneamente para máxima cobertura
                    SetLightsEnabled(m_LowBeamSpotlights, true);
                    SetLightsEnabled(m_HighBeamSpotlights, true);
                    break;
            }
        }

        /// <summary>
        /// Aplica la lógica trasera para luces de posición y de freno (Stop).
        /// </summary>
        private void ApplyRearTailLighting(HeadlightMode mode, bool braking)
        {
            bool positionOn = (mode == HeadlightMode.LowBeam || mode == HeadlightMode.HighBeam);

            if (braking)
            {
                // Freno pisado: intensidad brillante máxima en ópticas rojas
                SetMaterialsState(m_TailBrakeMaterials, Color.red, m_TailLightColor, m_BrakeLightIntensity, true);
            }
            else if (positionOn)
            {
                // Posición encendida: rojo tenue constante
                SetMaterialsState(m_TailBrakeMaterials, new Color(0.6f, 0.1f, 0.1f, 1f), m_TailLightColor, m_TailLightIntensity, true);
            }
            else
            {
                // Apagado
                SetMaterialsState(m_TailBrakeMaterials, m_RearOffColor, Color.black, 0f, false);
            }
        }

        /// <summary>
        /// Aplica la lógica de luz de marcha atrás (emisivo blanco + luz 3D de retroceso).
        /// </summary>
        private void ApplyReverseLighting(bool reverseOn)
        {
            if (reverseOn)
            {
                SetMaterialsState(m_ReverseMaterials, Color.white, m_ReverseOnColor, m_ReverseEmissionIntensity, true);
                if (m_ReverseSpotlight != null)
                {
                    m_ReverseSpotlight.enabled = true;
                }
            }
            else
            {
                SetMaterialsState(m_ReverseMaterials, m_ReverseOffColor, Color.black, 0f, false);
                if (m_ReverseSpotlight != null)
                {
                    m_ReverseSpotlight.enabled = false;
                }
            }
        }

        private static void SetMaterialsState(List<Material> materials, Color baseColor, Color emissiveColor, float intensity, bool on)
        {
            if (materials == null) return;
            foreach (var mat in materials)
            {
                if (mat == null) continue;

                if (mat.HasProperty(BaseColorId))
                    mat.SetColor(BaseColorId, on ? baseColor : Color.Lerp(baseColor, Color.black, 0.4f));
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", on ? baseColor : Color.Lerp(baseColor, Color.black, 0.4f));

                if (on && intensity > 0.001f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(EmissionColorId, emissiveColor * intensity);
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.SetColor(EmissionColorId, Color.black);
                }
            }
        }

        private static void SetLightsEnabled(Light[] lights, bool enabled)
        {
            if (lights == null) return;
            foreach (var l in lights)
            {
                if (l != null) l.enabled = enabled;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Asigna referencias a componentes en el Editor.
        /// </summary>
        public void EditorSetReferences(
            VehicleController vehicleController,
            VRHeadlightKnob knob,
            DashboardUIController dashboardUI,
            Renderer[] outerHeadlights,
            Renderer[] innerHeadlights,
            Light[] lowBeamSpots,
            Light[] highBeamSpots,
            Renderer[] tailBrakeLights,
            Renderer thirdBrakeLight,
            Renderer[] reverseLights,
            Light reverseSpot)
        {
            m_VehicleController = vehicleController;
            m_HeadlightKnob = knob;
            m_DashboardUI = dashboardUI;
            m_FrontOuterHeadlights = outerHeadlights;
            m_FrontInnerHeadlights = innerHeadlights;
            m_LowBeamSpotlights = lowBeamSpots;
            m_HighBeamSpotlights = highBeamSpots;
            m_RearTailBrakeLights = tailBrakeLights;
            m_ThirdBrakeLight = thirdBrakeLight;
            m_ReverseLights = reverseLights;
            m_ReverseSpotlight = reverseSpot;
        }
#endif
    }
}
