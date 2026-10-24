using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PrimerVolante.UI
{
    /// <summary>
    /// Controlador central de la pantalla diegética de GPS para la consola central del vehículo.
    /// Coordina:
    /// - Cámara cenital ortográfica (GPS_TopDownCamera) con renderizado a ~30 FPS hacia una RenderTexture.
    /// - Modo de navegación Course-Up (la flecha UI apunta fija hacia arriba y la cámara rota con el rumbo del auto).
    /// - Consumo y trazado de ruta delegados a GPSRouteRenderer.
    /// - Interfaz de usuario: distancia restante, próxima maniobra y panel de llegada a destino.
    /// </summary>
    [DisallowMultipleComponent]
    public class GPSController : MonoBehaviour
    {
        [Header("Referencias de Vehículo")]
        [Tooltip("Transform del vehículo. Si se deja vacío, se detecta automáticamente.")]
        [SerializeField] private Transform m_VehicleTransform;

        [Header("Cámara Cenital y Mapa (US17 & US18)")]
        [Tooltip("Cámara ortográfica cenital dedicada para el mapa.")]
        [SerializeField] private Camera m_TopDownCamera;

        [Tooltip("RenderTexture donde la cámara cenital dibuja el mapa.")]
        [SerializeField] private RenderTexture m_MapRenderTexture;

        [Tooltip("RawImage del Canvas que muestra la textura del mapa.")]
        [SerializeField] private RawImage m_MapDisplay;

        [Tooltip("Altura de la cámara sobre el vehículo en metros.")]
        [SerializeField] private float m_CameraHeight = 45f;

        [Tooltip("Tamaño ortográfico de la cámara (zoom del mapa).")]
        [SerializeField] private float m_OrthographicSize = 25f;

        [Tooltip("Velocidad de interpolación angular para el modo Course-Up.")]
        [SerializeField] private float m_RotationSmoothSpeed = 8f;

        [Tooltip("Tasa de refresco objetivo para la cámara del GPS (30 FPS ahorra GPU en VR).")]
        [SerializeField] private float m_TargetFrameRate = 30f;

        [Header("Ruta y Consumo (US19)")]
        [Tooltip("Componente encargado del trazado y consumo dinámico de la ruta.")]
        [SerializeField] private GPSRouteRenderer m_RouteRenderer;

        [Header("Elementos de UI")]
        [Tooltip("Ícono de flecha de navegación (fijo al centro apuntando hacia arriba).")]
        [SerializeField] private RectTransform m_NavigationArrow;

        [Tooltip("Texto para la distancia restante al destino.")]
        [SerializeField] private TMP_Text m_DistanceValueText;

        [Tooltip("Texto para la instrucción o próxima maniobra.")]
        [SerializeField] private TMP_Text m_ManeuverText;

        [Tooltip("Texto para el nombre del destino actual.")]
        [SerializeField] private TMP_Text m_DestinationText;

        [Tooltip("Panel emergente de felicitación al llegar a destino.")]
        [SerializeField] private GameObject m_ArrivalPanel;

        [Tooltip("Texto dentro del panel de llegada.")]
        [SerializeField] private TMP_Text m_ArrivalTitleText;

        // Estado interno
        private float m_CurrentYaw = 0f;
        private float m_RenderTimer = 0f;
        private bool m_IsInitialized = false;

        public Camera TopDownCamera => m_TopDownCamera;
        public RenderTexture MapRenderTexture => m_MapRenderTexture;
        public GPSRouteRenderer RouteRenderer => m_RouteRenderer;
        public Transform VehicleTransform => m_VehicleTransform;
        public float TargetFrameRate => m_TargetFrameRate;
        public float CameraHeight => m_CameraHeight;
        public float OrthographicSize => m_OrthographicSize;

        private void Awake()
        {
            ResolveVehicle();
            ConfigureCameraTechnical();
            ConfigureMainCameraCulling();

            if (m_ArrivalPanel != null)
            {
                m_ArrivalPanel.SetActive(false);
            }

            if (m_RouteRenderer != null)
            {
                m_RouteRenderer.OnDestinationReached += HandleArrival;
            }
        }

        private void Start()
        {
            if (m_VehicleTransform != null)
            {
                m_CurrentYaw = m_VehicleTransform.eulerAngles.y;
                SyncCameraImmediate();
            }

            m_IsInitialized = true;
        }

        private void OnDestroy()
        {
            if (m_RouteRenderer != null)
            {
                m_RouteRenderer.OnDestinationReached -= HandleArrival;
            }
        }

        /// <summary>
        /// Resuelve la referencia al vehículo usando FindAnyObjectByType (cumpliendo AGENTS.md).
        /// </summary>
        public void ResolveVehicle()
        {
            if (m_VehicleTransform != null) return;

            // 1. Buscar en ancestros
            Transform t = transform;
            while (t != null)
            {
                if (t.name.StartsWith("Car") || t.GetComponent<Rigidbody>() != null)
                {
                    m_VehicleTransform = t;
                    break;
                }
                t = t.parent;
            }

            // 2. Fallback seguro con FindAnyObjectByType según AGENTS.md
            if (m_VehicleTransform == null)
            {
                var vehicle = UnityEngine.Object.FindAnyObjectByType<PrimerVolante.VR.VehicleController>();
                if (vehicle != null)
                {
                    m_VehicleTransform = vehicle.transform;
                }
            }

            if (m_RouteRenderer != null && m_RouteRenderer.VehicleTransform == null)
            {
                m_RouteRenderer.VehicleTransform = m_VehicleTransform;
            }
        }

        /// <summary>
        /// Configura los parámetros técnicos de la cámara cenital (ortográfica, sin sombras, target texture).
        /// </summary>
        public void ConfigureCameraTechnical()
        {
            if (m_TopDownCamera == null) return;

            m_TopDownCamera.orthographic = true;
            m_TopDownCamera.orthographicSize = m_OrthographicSize;
            m_TopDownCamera.nearClipPlane = 1.0f;
            m_TopDownCamera.farClipPlane = m_CameraHeight + 50f;
            m_TopDownCamera.clearFlags = CameraClearFlags.SolidColor;
            m_TopDownCamera.backgroundColor = new Color(0.16f, 0.18f, 0.20f, 1f);
            m_TopDownCamera.allowHDR = false;
            m_TopDownCamera.allowMSAA = false;

            if (m_MapRenderTexture != null)
            {
                if (!m_MapRenderTexture.IsCreated())
                {
                    m_MapRenderTexture.Create();
                }
                m_TopDownCamera.targetTexture = m_MapRenderTexture;

                if (m_MapDisplay != null && m_MapDisplay.texture != m_MapRenderTexture)
                {
                    m_MapDisplay.texture = m_MapRenderTexture;
                }
            }

            // Eliminar AudioListener si existiese accidentalmente
            AudioListener listener = m_TopDownCamera.GetComponent<AudioListener>();
            if (listener != null)
            {
                DestroyImmediate(listener);
            }

            // Configurar datos URP: desactivar sombras dinámicas y post-processing para VR
            UniversalAdditionalCameraData camData = m_TopDownCamera.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
            {
                camData = m_TopDownCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            if (camData != null)
            {
                camData.renderShadows = false;
                camData.renderPostProcessing = false;
                camData.requiresColorOption = CameraOverrideOption.Off;
                camData.requiresDepthOption = CameraOverrideOption.Off;
            }

            // Configurar CullingMask con Enfoque de Lista Blanca (Allowlist):
            // ÚNICAMENTE renderizar las capas Roads, Building y GPSRoute.
            // Cualquier otro objeto del juego (autos de jugador y NPCs, peatones,
            // conos, vallas, farolas, árboles, etc.) queda automáticamente invisible
            // en la pantalla del GPS sin requerir capas ni filtros individuales.
            int mask = 0;

            int roadsLayer = LayerMask.NameToLayer("Roads");
            if (roadsLayer != -1) mask |= (1 << roadsLayer);

            int buildingLayer = LayerMask.NameToLayer("Building");
            if (buildingLayer != -1) mask |= (1 << buildingLayer);

            int routeLayer = LayerMask.NameToLayer("GPSRoute");
            if (routeLayer != -1) mask |= (1 << routeLayer);

            m_TopDownCamera.cullingMask = mask;

            // Desactivar el componente para controlar el renderizado manual a ~30 FPS
            m_TopDownCamera.enabled = false;
        }

        /// <summary>
        /// Asegura que la cámara principal de VR del jugador no renderice la capa GPSRoute,
        /// pero mantenga visibles todas las capas del mundo real (Roads, Building, Vehicle, NPC, etc.).
        /// </summary>
        public void ConfigureMainCameraCulling()
        {
            int routeLayer = LayerMask.NameToLayer("GPSRoute");
            int npcLayer = LayerMask.NameToLayer("NPC");
            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            int roadsLayer = LayerMask.NameToLayer("Roads");
            int buildingLayer = LayerMask.NameToLayer("Building");

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                if (routeLayer != -1) mainCam.cullingMask &= ~(1 << routeLayer);
                if (npcLayer != -1) mainCam.cullingMask |= (1 << npcLayer);
                if (vehicleLayer != -1) mainCam.cullingMask |= (1 << vehicleLayer);
                if (roadsLayer != -1) mainCam.cullingMask |= (1 << roadsLayer);
                if (buildingLayer != -1) mainCam.cullingMask |= (1 << buildingLayer);
            }
        }

        private void LateUpdate()
        {
            if (m_VehicleTransform == null)
            {
                ResolveVehicle();
                if (m_VehicleTransform == null) return;
            }

            Vector3 carPos = m_VehicleTransform.position;

            // 1. Posicionamiento cenital sobre el vehículo
            Vector3 camPos = new Vector3(carPos.x, carPos.y + m_CameraHeight, carPos.z);
            m_TopDownCamera.transform.position = camPos;

            // 2. Modo Course-Up: rotación suave sincronizada con el rumbo del auto
            float vehicleYaw = m_VehicleTransform.eulerAngles.y;
            m_CurrentYaw = Mathf.LerpAngle(m_CurrentYaw, vehicleYaw, m_RotationSmoothSpeed * Time.deltaTime);
            m_TopDownCamera.transform.rotation = Quaternion.Euler(90f, m_CurrentYaw, 0f);

            // 3. Tasa de refresco controlada (~30 FPS)
            m_RenderTimer += Time.deltaTime;
            float targetInterval = 1.0f / Mathf.Max(1f, m_TargetFrameRate);
            if (m_RenderTimer >= targetInterval)
            {
                m_RenderTimer = 0f;
                if (m_TopDownCamera != null && m_TopDownCamera.targetTexture != null)
                {
                    m_TopDownCamera.Render();
                }
            }

            // 4. Actualizar ruta dinámica y consumo
            if (m_RouteRenderer != null)
            {
                m_RouteRenderer.UpdateRoute(carPos);
                UpdateUI(m_RouteRenderer.RemainingDistance, m_RouteRenderer.CurrentManeuver, m_RouteRenderer.HasArrived);
            }
        }

        private void SyncCameraImmediate()
        {
            if (m_VehicleTransform == null || m_TopDownCamera == null) return;

            Vector3 carPos = m_VehicleTransform.position;
            m_TopDownCamera.transform.position = new Vector3(carPos.x, carPos.y + m_CameraHeight, carPos.z);
            m_CurrentYaw = m_VehicleTransform.eulerAngles.y;
            m_TopDownCamera.transform.rotation = Quaternion.Euler(90f, m_CurrentYaw, 0f);
        }

        private void UpdateUI(float remainingDist, string maneuver, bool arrived)
        {
            if (arrived)
            {
                if (m_DistanceValueText != null) m_DistanceValueText.text = "0 m";
                if (m_ManeuverText != null) m_ManeuverText.text = "Destino alcanzado";
                if (m_ArrivalPanel != null && !m_ArrivalPanel.activeSelf)
                {
                    m_ArrivalPanel.SetActive(true);
                }
                return;
            }

            if (m_DistanceValueText != null)
            {
                if (remainingDist >= 1000f)
                {
                    m_DistanceValueText.text = $"{remainingDist / 1000f:F1} km";
                }
                else
                {
                    m_DistanceValueText.text = $"{remainingDist:F0} m";
                }
            }

            if (m_ManeuverText != null)
            {
                m_ManeuverText.text = maneuver;
            }
        }

        private void HandleArrival()
        {
            if (m_ArrivalPanel != null)
            {
                m_ArrivalPanel.SetActive(true);
            }
            if (m_ArrivalTitleText != null)
            {
                m_ArrivalTitleText.text = "¡Llegaste a tu destino!";
            }
        }

        /// <summary>
        /// Asigna el texto del destino mostrado en la barra inferior.
        /// </summary>
        public void SetDestinationName(string destinationName)
        {
            if (m_DestinationText != null)
            {
                m_DestinationText.text = destinationName;
            }
        }
    }
}
