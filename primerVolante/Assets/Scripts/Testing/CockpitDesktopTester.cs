using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using PrimerVolante.UI;
using PrimerVolante.VR;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace PrimerVolante.Testing
{
    /// <summary>
    /// Simulador Desktop de Cabina y Piloto Automático para Pruebas sin Casco VR.
    /// Permite inspeccionar y validar la escena GPSNavigationDemo y el sistema GPS
    /// directamente desde el monitor de PC:
    /// 1. Desktop POV: Cámara en los ojos del conductor (DriverSeat) cuando no hay casco VR.
    /// 2. Mouse-Look & Zoom: Mirar parabrisas, espejos y consola central (GPS), con zoom por rueda.
    /// 3. Modo Piloto Automático: Sigue autónomamente la spline (GPS_Circuit_Route) a ~35 km/h,
    ///    frenando en curvas y deteniéndose en destino para validar Course-Up y consumo de ruta.
    /// 4. Modo Manual: Manejo por teclado WASD alternable con tecla M o Espacio.
    /// 5. No interfiere con VR: Se desactiva automáticamente si detecta un visor VR activo.
    /// Cumple estrictamente con AGENTS.md (cero APIs obsoletas; uso de FindAnyObjectByType).
    /// </summary>
    [DisallowMultipleComponent]
    public class CockpitDesktopTester : MonoBehaviour
    {
        public enum DriveMode
        {
            AutoPilot,
            Manual
        }

        [Header("Referencias Principales")]
        [Tooltip("Transform raíz del vehículo (Car09).")]
        [SerializeField] private Transform m_CarTransform;

        [Tooltip("Transform del asiento del conductor dentro de la cabina.")]
        [SerializeField] private Transform m_DriverSeat;

        [Tooltip("Contenedor de la spline para el piloto automático (GPS_Circuit_Route).")]
        [SerializeField] private SplineContainer m_SplineContainer;

        [Tooltip("Índice de la spline dentro del contenedor (por defecto 0).")]
        [SerializeField] private int m_SplineIndex = 0;

        [Tooltip("Controlador del vehículo.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Volante interactuable de la cabina.")]
        [SerializeField] private VRSteeringWheel m_SteeringWheel;

        [Tooltip("Controlador central del GPS.")]
        [SerializeField] private GPSController m_GpsController;

        [Header("Cámara Desktop POV")]
        [Tooltip("Cámara creada o asignada para la vista de escritorio.")]
        [SerializeField] private Camera m_DesktopCamera;

        [Tooltip("Desplazamiento local de los ojos respecto al DriverSeat (X: lateral, Y: altura, Z: adelante).")]
        [SerializeField] private Vector3 m_EyeOffset = new Vector3(0f, 1.15f, 0.05f);

        [Tooltip("Sensibilidad de giro de cabeza con el mouse.")]
        [SerializeField] private float m_MouseSensitivity = 1.8f;

        [Tooltip("FOV normal para visión de cabina.")]
        [SerializeField] private float m_DefaultFOV = 60f;

        [Tooltip("FOV mínimo al hacer zoom a la pantalla del GPS.")]
        [SerializeField] private float m_MinFOV = 25f;

        [Tooltip("FOV máximo de visión amplia.")]
        [SerializeField] private float m_MaxFOV = 75f;

        [Header("Parámetros de Piloto Automático")]
        [Tooltip("Velocidad de crucero en km/h sobre tramos rectos.")]
        [SerializeField] private float m_CruiseSpeedKmh = 35f;

        [Tooltip("Velocidad reducida en km/h durante curvas pronunciadas.")]
        [SerializeField] private float m_CornerSpeedKmh = 20f;

        [Tooltip("Aceleración del piloto automático en m/s^2.")]
        [SerializeField] private float m_AutoPilotAccel = 3.5f;

        [Tooltip("Desaceleración del piloto automático en m/s^2.")]
        [SerializeField] private float m_AutoPilotDecel = 5.0f;

        [Tooltip("Distancia en metros para considerar que arribó a la meta.")]
        [SerializeField] private float m_ArrivalThreshold = 3.5f;

        [Header("Estado en Tiempo Real")]
        [SerializeField] private DriveMode m_CurrentMode = DriveMode.AutoPilot;
        [SerializeField] private bool m_IsPaused = false;
        [SerializeField] private float m_CurrentSpeedKmh = 0f;
        [SerializeField] private float m_CurrentDistanceOnSpline = 0f;
        [SerializeField] private bool m_ReachedDestination = false;
        [SerializeField] private bool m_ShowHelpHUD = true;

        // Variables de orientación de la cabeza (Mouse Look)
        private float m_Yaw = 0f;
        private float m_Pitch = 0f;
        private float m_TargetFOV = 60f;
        private bool m_CursorLocked = false;
        private bool m_IsVRActive = false;
        private Rigidbody m_VehicleRigidbody;
        private float m_TotalSplineLength = 0f;

        // Posiciones de enfoque rápido hacia el GPS (consola central)
        private const float k_GpsFocusPitch = -18.6f;
        private const float k_GpsFocusYaw = 34.6f;
        private const float k_GpsFocusFOV = 35.0f;

        public DriveMode CurrentMode => m_CurrentMode;
        public Camera DesktopCamera => m_DesktopCamera;
        public bool IsAutoPilot => m_CurrentMode == DriveMode.AutoPilot;
        public float CurrentSpeedKmh => m_CurrentSpeedKmh;

        public SplineContainer SplineContainer
        {
            get => m_SplineContainer;
            set => m_SplineContainer = value;
        }

        public Transform CarTransform
        {
            get => m_CarTransform;
            set => m_CarTransform = value;
        }

        public VehicleController VehicleController
        {
            get => m_VehicleController;
            set => m_VehicleController = value;
        }

        private void Awake()
        {
            // 1. Detección estricta de visor VR: si hay VR activo, este tester se auto-desactiva
            if (IsVRHeadsetActive())
            {
                m_IsVRActive = true;
                Debug.Log("[CockpitDesktopTester] 🥽 Visor VR detectado y activo. Desactivando simulador desktop para no interferir con el visor VR.");
                if (m_DesktopCamera != null)
                {
                    m_DesktopCamera.gameObject.SetActive(false);
                }
                enabled = false;
                return;
            }

            ResolveReferences();
            SetupDesktopCamera();

            m_TargetFOV = m_DefaultFOV;
            if (m_DesktopCamera != null)
            {
                m_DesktopCamera.fieldOfView = m_DefaultFOV;
            }
        }

        private void Start()
        {
            if (m_IsVRActive) return;

            InitializeSplineTracking();

            // Configurar el estado de físicas según el modo inicial
            ApplyPhysicsMode(m_CurrentMode == DriveMode.AutoPilot);

            Debug.Log("[CockpitDesktopTester] 🖥️ Simulador Desktop de Cabina y Piloto Automático ACTIVO.");
            Debug.Log("   • Tecla [M]: Alternar entre Piloto Automático (35 km/h) y Manejo Manual (WASD).");
            Debug.Log("   • Mouse Derecho / Clic: Mirar cabina y espejos. Rueda: Zoom.");
            Debug.Log("   • Tecla [F]: Enfoque directo a la pantalla del GPS. [R]: Resetear vista.");
        }

        private void OnDisable()
        {
            // Restaurar estado físico seguro para que el vehículo no quede bloqueado
            if (m_VehicleRigidbody != null)
            {
                m_VehicleRigidbody.isKinematic = false;
            }

            if (m_VehicleController != null)
            {
                m_VehicleController.enabled = true;
            }
        }

        /// <summary>
        /// Detecta si hay un visor VR activo en Unity (OpenXR / Mock HMD / Subsystem activo).
        /// </summary>
        public static bool IsVRHeadsetActive()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive)
            {
                return true;
            }

            var displays = new List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running) return true;
            }

            return false;
        }

        /// <summary>
        /// Resuelve referencias en la escena usando FindAnyObjectByType (estricto cumplimiento de AGENTS.md).
        /// Blindado para evitar engancharse a sendas peatonales o rutas de NPCs.
        /// </summary>
        public void ResolveReferences()
        {
            if (m_VehicleController == null)
            {
                m_VehicleController = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            }

            if (m_CarTransform == null && m_VehicleController != null)
            {
                m_CarTransform = m_VehicleController.transform;
            }

            if (m_CarTransform != null && m_VehicleRigidbody == null)
            {
                m_VehicleRigidbody = m_CarTransform.GetComponent<Rigidbody>();
            }

            if (m_DriverSeat == null && m_CarTransform != null)
            {
                Transform[] children = m_CarTransform.GetComponentsInChildren<Transform>(true);
                foreach (var t in children)
                {
                    if (t.name == "DriverSeat")
                    {
                        m_DriverSeat = t;
                        break;
                    }
                }
            }

            if (m_GpsController == null)
            {
                m_GpsController = UnityEngine.Object.FindAnyObjectByType<GPSController>();
            }

            // Búsqueda blindada de GPS_Circuit_Route
            if (m_SplineContainer == null)
            {
                // 1. Buscar objeto con el nombre canónico de la ruta GPS
                GameObject routeObj = GameObject.Find("GPS_Circuit_Route");
                if (routeObj != null)
                {
                    m_SplineContainer = routeObj.GetComponent<SplineContainer>();
                }

                // 2. Si no se encontró, obtenerlo a través de GPSRouteRenderer
                if (m_SplineContainer == null && m_GpsController != null && m_GpsController.RouteRenderer != null)
                {
                    m_SplineContainer = m_GpsController.RouteRenderer.RouteContainer;
                }

                // 3. Fallback estricto excluyendo cualquier ruta de peatones o NPCs
                if (m_SplineContainer == null)
                {
                    var containers = UnityEngine.Object.FindObjectsByType<SplineContainer>();
                    foreach (var c in containers)
                    {
                        string n = c.gameObject.name;
                        // Prohibir rutas peatonales o de cruces peatonales
                        if (n.Contains("Pedestrian") || n.Contains("Crosswalk") || n.Contains("Sidewalk") ||
                            n.Contains("NPC") || n.Contains("People") || n.Contains("Person"))
                        {
                            continue;
                        }

                        if (n.Contains("GPS") || n.Contains("Circuit") || n.Contains("Route"))
                        {
                            m_SplineContainer = c;
                            break;
                        }
                    }
                }
            }

            if (m_SteeringWheel == null && m_CarTransform != null)
            {
                m_SteeringWheel = m_CarTransform.GetComponentInChildren<VRSteeringWheel>(true);
            }
        }

        /// <summary>
        /// Aplica la separación de físicas: en Piloto Automático se desactiva VehicleController
        /// y el Rigidbody pasa a Kinematic para evitar que luchen con la spline.
        /// En Manual, se reactiva VehicleController y Rigidbody físico.
        /// </summary>
        private void ApplyPhysicsMode(bool isAutoPilot)
        {
            if (isAutoPilot)
            {
                if (m_VehicleController != null)
                {
                    m_VehicleController.enabled = false;
                    m_VehicleController.SetEngineRunning(true);
                    m_VehicleController.SetGear(GearState.Drive);
                }

                if (m_VehicleRigidbody != null)
                {
                    m_VehicleRigidbody.isKinematic = true;
                }
            }
            else
            {
                if (m_VehicleRigidbody != null)
                {
                    m_VehicleRigidbody.isKinematic = false;
                    if (m_CarTransform != null)
                    {
                        m_VehicleRigidbody.linearVelocity = m_CarTransform.forward * (m_CurrentSpeedKmh / 3.6f);
                    }
                }

                if (m_VehicleController != null)
                {
                    m_VehicleController.enabled = true;
                    m_VehicleController.SetEngineRunning(true);
                    m_VehicleController.SetGear(GearState.Drive);
                }
            }
        }

        /// <summary>
        /// Configura la cámara POV dentro del asiento del conductor.
        /// </summary>
        public void SetupDesktopCamera()
        {
            if (m_DesktopCamera == null)
            {
                // Buscar si ya existe una Desktop_POV_Camera bajo DriverSeat o Car
                Transform existingCam = null;
                if (m_DriverSeat != null) existingCam = m_DriverSeat.Find("Desktop_POV_Camera");
                if (existingCam == null && m_CarTransform != null) existingCam = m_CarTransform.Find("Desktop_POV_Camera");

                if (existingCam != null)
                {
                    m_DesktopCamera = existingCam.GetComponent<Camera>();
                }
            }

            if (m_DesktopCamera == null)
            {
                GameObject camObj = new GameObject("Desktop_POV_Camera");
                if (m_DriverSeat != null)
                {
                    camObj.transform.SetParent(m_DriverSeat, false);
                }
                else if (m_CarTransform != null)
                {
                    camObj.transform.SetParent(m_CarTransform, false);
                }
                else
                {
                    camObj.transform.SetParent(transform, false);
                }

                m_DesktopCamera = camObj.AddComponent<Camera>();
            }

            // Ubicación en los ojos del conductor
            m_DesktopCamera.transform.localPosition = m_EyeOffset;
            m_DesktopCamera.transform.localRotation = Quaternion.identity;

            // Configurar propiedades de cámara
            m_DesktopCamera.tag = "MainCamera";
            m_DesktopCamera.nearClipPlane = 0.05f;
            m_DesktopCamera.farClipPlane = 1000f;
            m_DesktopCamera.fieldOfView = m_DefaultFOV;

            // Universal Additional Camera Data
            var camData = m_DesktopCamera.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
            {
                camData = m_DesktopCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            if (camData != null)
            {
                camData.renderPostProcessing = true;
                camData.renderShadows = true;
            }

            // Culling Mask: EXCLUIR la capa de ruta GPS (GPSRoute) para que el asfalto real se vea limpio
            int routeLayer = LayerMask.NameToLayer("GPSRoute");
            if (routeLayer != -1)
            {
                m_DesktopCamera.cullingMask &= ~(1 << routeLayer);
            }

            // Asegurar que la cámara de cabina tenga habilitadas las capas NPC y Vehicle
            int npcLayer = LayerMask.NameToLayer("NPC");
            if (npcLayer != -1)
            {
                m_DesktopCamera.cullingMask |= (1 << npcLayer);
            }

            int vehicleLayer = LayerMask.NameToLayer("Vehicle");
            if (vehicleLayer != -1)
            {
                m_DesktopCamera.cullingMask |= (1 << vehicleLayer);
            }

            // AudioListener: asegurar uno activo en la cabeza del conductor
            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>();
            bool hasActiveListener = false;
            foreach (var l in listeners)
            {
                if (l != null && l.enabled && l.gameObject.activeInHierarchy && l.gameObject != m_DesktopCamera.gameObject)
                {
                    hasActiveListener = true;
                    break;
                }
            }

            if (!hasActiveListener)
            {
                var listener = m_DesktopCamera.GetComponent<AudioListener>();
                if (listener == null) m_DesktopCamera.gameObject.AddComponent<AudioListener>();
            }

            m_DesktopCamera.gameObject.SetActive(true);
            m_DesktopCamera.enabled = true;
        }

        private void InitializeSplineTracking()
        {
            if (m_SplineContainer == null || m_SplineContainer.Splines == null || m_SplineIndex >= m_SplineContainer.Splines.Count)
            {
                return;
            }

            m_TotalSplineLength = m_SplineContainer.CalculateLength(m_SplineIndex);

            // Proyectar posición inicial del auto sobre la spline
            if (m_CarTransform != null && m_TotalSplineLength > 0.1f)
            {
                Spline spline = m_SplineContainer.Splines[m_SplineIndex];
                Vector3 carLocal = m_SplineContainer.transform.InverseTransformPoint(m_CarTransform.position);
                SplineUtility.GetNearestPoint(spline, carLocal, out float3 nearestPoint, out float nearestT);
                m_CurrentDistanceOnSpline = spline.ConvertIndexUnit(nearestT, PathIndexUnit.Normalized, PathIndexUnit.Distance);
            }
        }

        private void Update()
        {
            if (m_IsVRActive) return;

            HandleInputModeToggle();
            HandleMouseLook();
            HandleZoom();
            HandleQuickFocus();

            if (m_CurrentMode == DriveMode.Manual)
            {
                UpdateManualDriving();
            }
        }

        private void FixedUpdate()
        {
            if (m_IsVRActive) return;

            if (m_CurrentMode == DriveMode.AutoPilot)
            {
                UpdateAutoPilotDriving();
            }
            else
            {
                // En modo manual, leer velocidad del controlador
                if (m_VehicleController != null)
                {
                    m_CurrentSpeedKmh = m_VehicleController.CurrentSpeedKmh;
                }
                else if (m_VehicleRigidbody != null)
                {
                    m_CurrentSpeedKmh = m_VehicleRigidbody.linearVelocity.magnitude * 3.6f;
                }
            }
        }

        // =========================================================================
        // Control de Modos y Teclado
        // =========================================================================

        private void HandleInputModeToggle()
        {
            bool toggleKey = false;
            bool pauseKey = false;

            if (Keyboard.current != null)
            {
                toggleKey = Keyboard.current.mKey.wasPressedThisFrame;
                pauseKey = Keyboard.current.spaceKey.wasPressedThisFrame;

                if (Keyboard.current.tabKey.wasPressedThisFrame)
                {
                    m_ShowHelpHUD = !m_ShowHelpHUD;
                }
            }

            if (toggleKey)
            {
                SetDriveMode(m_CurrentMode == DriveMode.AutoPilot ? DriveMode.Manual : DriveMode.AutoPilot);
            }

            if (pauseKey)
            {
                if (m_CurrentMode == DriveMode.AutoPilot)
                {
                    m_IsPaused = !m_IsPaused;
                    Debug.Log($"[CockpitDesktopTester] Piloto Automático {(m_IsPaused ? "PAUSADO" : "REANUDADO")}.");
                }
            }
        }

        public void SetDriveMode(DriveMode newMode)
        {
            if (m_CurrentMode == newMode) return;
            m_CurrentMode = newMode;

            if (m_CurrentMode == DriveMode.AutoPilot)
            {
                ApplyPhysicsMode(true);

                // Re-sincronizar posición sobre la spline desde la posición actual del auto
                if (m_SplineContainer != null && m_CarTransform != null && m_SplineContainer.Splines != null && m_SplineIndex < m_SplineContainer.Splines.Count)
                {
                    Spline spline = m_SplineContainer.Splines[m_SplineIndex];
                    Vector3 carLocal = m_SplineContainer.transform.InverseTransformPoint(m_CarTransform.position);
                    SplineUtility.GetNearestPoint(spline, carLocal, out float3 nearestPoint, out float nearestT);
                    m_CurrentDistanceOnSpline = spline.ConvertIndexUnit(nearestT, PathIndexUnit.Normalized, PathIndexUnit.Distance);
                }
                m_IsPaused = false;
                Debug.Log("[CockpitDesktopTester] 🏎️ Modo [PILOTO AUTOMÁTICO] activado. Siguiendo circuito a 35 km/h.");
            }
            else
            {
                ApplyPhysicsMode(false);
                Debug.Log("[CockpitDesktopTester] 🎮 Modo [MANUAL WASD] activado. Usa W/S para acelerar/frenar y A/D para girar.");
            }
        }

        // =========================================================================
        // Piloto Automático por la Spline (~35 km/h con frenado en curvas y arribo)
        // =========================================================================

        private void UpdateAutoPilotDriving()
        {
            if (m_SplineContainer == null || m_CarTransform == null || m_TotalSplineLength <= 0.1f) return;

            Spline spline = m_SplineContainer.Splines[m_SplineIndex];
            float remaining = Mathf.Max(0f, m_TotalSplineLength - m_CurrentDistanceOnSpline);

            // Detección de arribo a la meta (< 4m)
            if (remaining <= m_ArrivalThreshold)
            {
                if (!m_ReachedDestination)
                {
                    m_ReachedDestination = true;
                    Debug.Log("🏁 [CockpitDesktopTester] ¡Llegaste a tu destino! Piloto automático completado.");
                }
            }

            // Calcular velocidad objetivo
            float targetSpeedMs = 0f;

            if (!m_IsPaused && !m_ReachedDestination)
            {
                float cruiseSpeedMs = m_CruiseSpeedKmh / 3.6f;
                float cornerSpeedMs = m_CornerSpeedKmh / 3.6f;

                // 1. Detección anticipada de curvas (muestreo a 6 metros por delante)
                float lookaheadDist = Mathf.Min(m_TotalSplineLength, m_CurrentDistanceOnSpline + 6.0f);
                float tNow = spline.ConvertIndexUnit(m_CurrentDistanceOnSpline, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                float tAhead = spline.ConvertIndexUnit(lookaheadDist, PathIndexUnit.Distance, PathIndexUnit.Normalized);

                m_SplineContainer.Evaluate(m_SplineIndex, tNow, out float3 _, out float3 tanNow, out float3 _);
                m_SplineContainer.Evaluate(m_SplineIndex, tAhead, out float3 _, out float3 tanAhead, out float3 _);

                Vector2 vNow = new Vector2(tanNow.x, tanNow.z).normalized;
                Vector2 vAhead = new Vector2(tanAhead.x, tanAhead.z).normalized;
                float angleDiff = Vector2.Angle(vNow, vAhead);

                // Si hay curvatura > 15°, reducir velocidad gradualmente hacia velocidad de curva
                if (angleDiff > 15f)
                {
                    float factor = Mathf.Clamp01((angleDiff - 15f) / 30f);
                    targetSpeedMs = Mathf.Lerp(cruiseSpeedMs, cornerSpeedMs, factor);
                }
                else
                {
                    targetSpeedMs = cruiseSpeedMs;
                }

                // 2. Desaceleración suave al aproximarse a la meta (últimos 15 metros)
                if (remaining < 15.0f)
                {
                    float stopSpeed = Mathf.Sqrt(2f * m_AutoPilotDecel * Mathf.Max(0f, remaining - 1.0f));
                    targetSpeedMs = Mathf.Min(targetSpeedMs, stopSpeed);
                }
            }

            // Aceleración / Desaceleración
            float currentSpeedMs = m_CurrentSpeedKmh / 3.6f;
            float rate = (targetSpeedMs >= currentSpeedMs) ? m_AutoPilotAccel : m_AutoPilotDecel;
            currentSpeedMs = Mathf.MoveTowards(currentSpeedMs, targetSpeedMs, rate * Time.fixedDeltaTime);
            m_CurrentSpeedKmh = currentSpeedMs * 3.6f;

            // Avanzar distancia
            m_CurrentDistanceOnSpline += currentSpeedMs * Time.fixedDeltaTime;
            m_CurrentDistanceOnSpline = Mathf.Clamp(m_CurrentDistanceOnSpline, 0f, m_TotalSplineLength);

            // Evaluar pose en la spline
            float normT = spline.ConvertIndexUnit(m_CurrentDistanceOnSpline, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            if (m_SplineContainer.Evaluate(m_SplineIndex, normT, out float3 localPos, out float3 localTangent, out float3 _))
            {
                Vector3 worldPos = m_SplineContainer.transform.TransformPoint((Vector3)localPos);
                Vector3 worldTangent = m_SplineContainer.transform.TransformDirection((Vector3)localTangent);
                worldTangent.y = 0f;

                if (worldTangent.sqrMagnitude > 1e-4f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(worldTangent.normalized, Vector3.up);

                    if (m_VehicleRigidbody != null)
                    {
                        m_VehicleRigidbody.MovePosition(worldPos);
                        m_VehicleRigidbody.MoveRotation(targetRot);
                    }
                    else
                    {
                        m_CarTransform.position = worldPos;
                        m_CarTransform.rotation = targetRot;
                    }

                    // Animar visualmente el volante en curvas para mayor inmersión
                    if (m_SteeringWheel != null)
                    {
                        float yawDelta = Vector3.SignedAngle(m_CarTransform.forward, worldTangent.normalized, Vector3.up);
                        float steerTarget = Mathf.Clamp(yawDelta * 8.0f, -400f, 400f);
                        m_SteeringWheel.SetAngleProgrammatic(steerTarget);
                    }
                }
            }

            // Sincronizar VehicleController para que el velocímetro del tablero digital muestre los 35 km/h
            if (m_VehicleController != null)
            {
                m_VehicleController.OverrideSpeed(currentSpeedMs);
                m_VehicleController.SetGear(GearState.Drive);
            }
        }

        // =========================================================================
        // Manejo Manual (WASD)
        // =========================================================================

        private void UpdateManualDriving()
        {
            if (m_VehicleController == null) return;

            // Asegurar que el motor esté encendido
            if (!m_VehicleController.IsEngineRunning)
            {
                m_VehicleController.SetEngineRunning(true);
            }

            // Detección de marcha atrás arcade (GTA/Forza style):
            // Si el auto está prácticamente detenido (< 0.5 km/h) y se mantiene presionado S, pasa a Reverse.
            // Si está en Reverse y se presiona W, vuelve a Drive.
            bool wPressed = false;
            bool sPressed = false;

            if (Keyboard.current != null)
            {
                wPressed = Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed;
                sPressed = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;
            }

            if (sPressed && m_VehicleController.CurrentSpeedKmh < 0.5f && m_VehicleController.CurrentGear == GearState.Drive)
            {
                m_VehicleController.SetGear(GearState.Reverse);
            }
            else if (wPressed && m_VehicleController.CurrentGear == GearState.Reverse)
            {
                m_VehicleController.SetGear(GearState.Drive);
            }
        }

        // =========================================================================
        // Vista de Cabina (Mouse Look & Zoom)
        // =========================================================================

        private void HandleMouseLook()
        {
            if (m_DesktopCamera == null) return;

            // Se puede mirar manteniendo presionado el botón derecho del mouse (estándar en Unity)
            // o haciendo clic para fijar/liberar el cursor
            bool rightMouseHeld = false;
            float deltaX = 0f;
            float deltaY = 0f;

            if (Mouse.current != null)
            {
                rightMouseHeld = Mouse.current.rightButton.isPressed;
                Vector2 delta = Mouse.current.delta.ReadValue();
                deltaX = delta.x;
                deltaY = delta.y;

                if (Mouse.current.leftButton.wasPressedThisFrame && !m_CursorLocked && !IsPointerOverUI())
                {
                    SetCursorLock(true);
                }
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetCursorLock(false);
            }

            if (rightMouseHeld || m_CursorLocked)
            {
                m_Yaw += deltaX * m_MouseSensitivity;
                m_Pitch -= deltaY * m_MouseSensitivity;

                // Limitar giros realistas de cuello en cabina:
                // Pitch: -70° (mirar abajo a la consola/pedales) a +60° (mirar techo/retrovisor central)
                // Yaw: -110° (mirar ventanilla y espejo izquierdo) a +110° (mirar ventanilla derecha)
                m_Pitch = Mathf.Clamp(m_Pitch, -70f, 60f);
                m_Yaw = Mathf.Clamp(m_Yaw, -110f, 110f);
            }

            m_DesktopCamera.transform.localRotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }

        private void HandleZoom()
        {
            if (m_DesktopCamera == null) return;

            float scroll = 0f;
            if (Mouse.current != null)
            {
                scroll = Mouse.current.scroll.ReadValue().y;
            }

            if (Mathf.Abs(scroll) > 0.01f)
            {
                float step = Mathf.Sign(scroll) * 5.0f;
                m_TargetFOV = Mathf.Clamp(m_TargetFOV - step, m_MinFOV, m_MaxFOV);
            }

            m_DesktopCamera.fieldOfView = Mathf.Lerp(m_DesktopCamera.fieldOfView, m_TargetFOV, Time.deltaTime * 12f);
        }

        private void HandleQuickFocus()
        {
            bool focusKey = false;
            bool resetKey = false;

            if (Keyboard.current != null)
            {
                focusKey = Keyboard.current.fKey.wasPressedThisFrame;
                resetKey = Keyboard.current.rKey.wasPressedThisFrame;
            }

            if (focusKey)
            {
                // Enfocar directamente la consola central con el display del GPS
                m_Pitch = k_GpsFocusPitch;
                m_Yaw = k_GpsFocusYaw;
                m_TargetFOV = k_GpsFocusFOV;
                Debug.Log("[CockpitDesktopTester] 🔍 Enfoque rápido hacia pantalla del GPS.");
            }

            if (resetKey)
            {
                // Resetear vista al frente por el parabrisas
                m_Pitch = 0f;
                m_Yaw = 0f;
                m_TargetFOV = m_DefaultFOV;
                Debug.Log("[CockpitDesktopTester] 👁️ Vista reestablecida hacia el frente.");
            }
        }

        private void SetCursorLock(bool locked)
        {
            m_CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private bool IsPointerOverUI()
        {
            return UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        // =========================================================================
        // Interfaz en Pantalla (On-Screen Display / HUD de Pruebas)
        // =========================================================================

        private void OnGUI()
        {
            if (m_IsVRActive || !m_ShowHelpHUD) return;

            // Panel estilizado en la esquina superior izquierda
            GUILayout.BeginArea(new Rect(15, 15, 340, 290), GUI.skin.box);
            GUILayout.Label("<b>🚗 PRIMER VOLANTE - SIMULADOR DESKTOP</b>", GetHeaderStyle());
            GUILayout.Space(4);

            string modeText = (m_CurrentMode == DriveMode.AutoPilot)
                ? (m_IsPaused ? "<color=#FFA500>PILOTO AUTOMÁTICO (PAUSADO)</color>" : "<color=#00FF88>PILOTO AUTOMÁTICO (ACTIVO)</color>")
                : "<color=#00CCFF>MANUAL (WASD)</color>";

            GUILayout.Label($"<b>Modo:</b> {modeText}");
            GUILayout.Label($"<b>Velocidad:</b> {m_CurrentSpeedKmh:F1} km/h");

            if (m_GpsController != null && m_GpsController.RouteRenderer != null)
            {
                float dist = m_GpsController.RouteRenderer.RemainingDistance;
                string maneuver = m_GpsController.RouteRenderer.CurrentManeuver;
                GUILayout.Label($"<b>Distancia al Destino:</b> {dist:F0} m");
                GUILayout.Label($"<b>Próxima Maniobra:</b> {maneuver}");
            }

            GUILayout.Space(6);
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
            GUILayout.Space(4);

            GUILayout.Label("<b>Controles:</b>");
            GUILayout.Label("• <b>[M]:</b> Alternar Piloto Auto / Manual");
            GUILayout.Label("• <b>[Espacio]:</b> Pausar Piloto / Freno");
            GUILayout.Label("• <b>[WASD]:</b> Conducir en manual (W/S/A/D)");
            GUILayout.Label("• <b>[Clic Derecho / Mover]:</b> Girar cabeza");
            GUILayout.Label("• <b>[Rueda Mouse]:</b> Zoom a pantalla");
            GUILayout.Label("• <b>[F]:</b> Enfoque directo a pantalla GPS");
            GUILayout.Label("• <b>[R]:</b> Resetear vista al frente");
            GUILayout.Label("• <b>[Tab]:</b> Ocultar / Mostrar este HUD");

            GUILayout.EndArea();
        }

        private GUIStyle GetHeaderStyle()
        {
            GUIStyle s = new GUIStyle(GUI.skin.label);
            s.alignment = TextAnchor.MiddleCenter;
            s.fontSize = 12;
            s.normal.textColor = Color.white;
            return s;
        }

#if UNITY_EDITOR
        [MenuItem("Tools/Primer Volante/GPS/Add Desktop Tester to Scene")]
        public static void AddDesktopTesterToScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Debug.LogError("[CockpitDesktopTester] No hay escena activa.");
                return;
            }

            GameObject testerObj = GameObject.Find("[Desktop_Cockpit_Tester]");
            if (testerObj == null)
            {
                testerObj = new GameObject("[Desktop_Cockpit_Tester]");
                Undo.RegisterCreatedObjectUndo(testerObj, "Create Desktop Cockpit Tester");
            }

            var tester = testerObj.GetComponent<CockpitDesktopTester>();
            if (tester == null)
            {
                tester = Undo.AddComponent<CockpitDesktopTester>(testerObj);
            }

            tester.ResolveReferences();
            tester.SetupDesktopCamera();

            EditorUtility.SetDirty(testerObj);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log("[CockpitDesktopTester] ✅ [Desktop_Cockpit_Tester] configurado en la escena actual.");
        }
#endif
    }
}
