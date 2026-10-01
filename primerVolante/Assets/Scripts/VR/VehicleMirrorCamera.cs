using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Gestor de cámara y RenderTexture para espejos vehiculares en VR (retrovisor y laterales).
    /// Garantiza:
    /// 1. Renderizado efectivo hacia atrás en URP mediante RenderTexture en memoria GPU.
    /// 2. Inversión óptica horizontal (UV Tiling X = -1, Offset X = 1: x' = 1 - x).
    /// 3. Asignación robusta de la cabeza del conductor (Camera.main / XROrigin) con fallback seguro.
    /// 4. Angle Culling relajado (desactivado por defecto para evitar pantallas negras en Editor o sin casco VR).
    /// 5. Time-Slicing determinista con ejecución explícita de Render() para actualización garantizada.
    /// 6. Eliminación de AudioListener y recorte de FarClipPlane (100 - 120m).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public class VehicleMirrorCamera : MonoBehaviour
    {
        [Header("Componentes de Cámara y Render")]
        [Tooltip("Cámara dedicada mirando hacia atrás para este espejo.")]
        [SerializeField] private Camera m_MirrorCamera;

        [Tooltip("RenderTexture de destino donde se dibuja el reflejo.")]
        [SerializeField] private RenderTexture m_RenderTexture;

        [Tooltip("Renderer de la superficie física del espejo que muestra la textura.")]
        [SerializeField] private Renderer m_MirrorSurfaceRenderer;

        [Tooltip("Material específico de la superficie del espejo (opcional si se usa m_MirrorSurfaceRenderer).")]
        [SerializeField] private Material m_MirrorMaterial;

        [Header("Parámetros Ópticos y de Cámara")]
        [Tooltip("Plano de recorte lejano acotado para VR (100 - 120m).")]
        [SerializeField] private float m_FarClipPlane = 120f;

        [Tooltip("Campo de visión (FOV) vertical de la cámara del espejo.")]
        [SerializeField] private float m_FieldOfView = 60f;

        [Tooltip("Aplica automáticamente la inversión óptica horizontal (Tiling X = -1, Offset X = 1) al iniciar.")]
        [SerializeField] private bool m_ApplyOpticalInversionOnStart = true;

        [Header("Optimización VR: Culling por Ángulo de Visión")]
        [Tooltip("Transform de la cabeza del conductor (HMD / Camera.main). Si es null se obtiene automáticamente de XROrigin / Camera.main.")]
        [SerializeField] private Transform m_DriverHead;

        [Tooltip("Desactivado por defecto para que los espejos nunca queden negros en el Editor o antes de poner el casco.")]
        [SerializeField] private bool m_EnableAngleCulling = false;

        [Tooltip("Ángulo máximo (en grados) entre la dirección de mirada y el espejo para considerarlo visible.")]
        [SerializeField] private float m_CullingAngleThreshold = 75f;

        [Header("Optimización VR: Time-Slicing (Alternancia de Cuadros)")]
        [Tooltip("Activa la distribución de cuadros entre los espejos activos para no renderizar todas las cámaras simultáneamente.")]
        [SerializeField] private bool m_EnableTimeSlicing = true;

        // Registro estático de todas las cámaras de espejo activas para coordinación de Time-Slicing
        private static readonly List<VehicleMirrorCamera> s_AllMirrorCameras = new List<VehicleMirrorCamera>();
        private static int s_CurrentSliceIndex = 0;
        private static int s_LastCoordinatorFrame = -1;

        private Material m_InstancedMaterial;
        private bool m_IsVisibleToDriver = true;
        private int m_StartupFramesRendered = 0;
        private const int STARTUP_FRAMES_REQUIRED = 5;

        public Camera MirrorCamera => m_MirrorCamera;
        public RenderTexture TargetTexture => m_RenderTexture;
        public bool IsVisibleToDriver => m_IsVisibleToDriver;
        public bool IsCameraRendering => m_MirrorCamera != null && m_MirrorCamera.enabled;

        public Transform DriverHead
        {
            get => m_DriverHead;
            set => m_DriverHead = value;
        }

        public bool EnableAngleCulling
        {
            get => m_EnableAngleCulling;
            set => m_EnableAngleCulling = value;
        }

        private void Awake()
        {
            if (m_MirrorCamera == null)
            {
                m_MirrorCamera = GetComponent<Camera>();
            }

            ConfigureCamera();
            EnsureNoAudioListener();
            EnsureRenderTextureCreated();
        }

        private void Start()
        {
            EnsureRenderTextureCreated();
            ResolveDriverHead();

            if (m_ApplyOpticalInversionOnStart)
            {
                ApplyOpticalInversion();
            }

            // Forzar render inicial para que la textura no comience negra
            ForceRender();
        }

        private void OnEnable()
        {
            if (!s_AllMirrorCameras.Contains(this))
            {
                s_AllMirrorCameras.Add(this);
            }

            EnsureRenderTextureCreated();
            if (m_MirrorCamera != null)
            {
                m_MirrorCamera.enabled = true;
            }
        }

        private void OnDisable()
        {
            s_AllMirrorCameras.Remove(this);
            if (m_MirrorCamera != null)
            {
                m_MirrorCamera.enabled = false;
            }
        }

        private void OnDestroy()
        {
            s_AllMirrorCameras.Remove(this);
            if (m_InstancedMaterial != null)
            {
                Destroy(m_InstancedMaterial);
                m_InstancedMaterial = null;
            }
        }

        /// <summary>
        /// Asegura que la RenderTexture exista y esté creada en memoria GPU.
        /// </summary>
        public void EnsureRenderTextureCreated()
        {
            if (m_RenderTexture != null)
            {
                if (!m_RenderTexture.IsCreated())
                {
                    m_RenderTexture.Create();
                }

                if (m_MirrorCamera != null && m_MirrorCamera.targetTexture != m_RenderTexture)
                {
                    m_MirrorCamera.targetTexture = m_RenderTexture;
                }
            }
        }

        /// <summary>
        /// Configura los parámetros técnicos de la cámara para rendimiento en VR (URP).
        /// </summary>
        public void ConfigureCamera()
        {
            if (m_MirrorCamera == null) return;

            m_MirrorCamera.farClipPlane = m_FarClipPlane;
            m_MirrorCamera.fieldOfView = m_FieldOfView;
            m_MirrorCamera.nearClipPlane = 0.05f;
            m_MirrorCamera.allowHDR = false;
            m_MirrorCamera.allowMSAA = false;
            m_MirrorCamera.clearFlags = CameraClearFlags.Skybox;

            EnsureRenderTextureCreated();

            // Configurar URP UniversalAdditionalCameraData para deshabilitar sobrecostes en VR
            UniversalAdditionalCameraData camData = m_MirrorCamera.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
            {
                camData = m_MirrorCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            if (camData != null)
            {
                camData.renderPostProcessing = false;
                camData.renderShadows = false;
                camData.requiresColorOption = CameraOverrideOption.Off;
                camData.requiresDepthOption = CameraOverrideOption.Off;
            }
        }

        /// <summary>
        /// Asegura que la cámara del espejo NUNCA posea un AudioListener activo.
        /// </summary>
        public void EnsureNoAudioListener()
        {
            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(listener);
                }
                else
                {
                    DestroyImmediate(listener);
                }
            }
        }

        /// <summary>
        /// Aplica la inversión horizontal óptica requerida:
        /// UV Tiling X = -1, Offset X = 1 (efecto óptico especular x' = 1 - x).
        /// También vincula la RenderTexture tanto a mainTexture como a _BaseMap.
        /// </summary>
        public void ApplyOpticalInversion()
        {
            EnsureRenderTextureCreated();
            Material mat = GetMirrorMaterial();
            if (mat == null) return;

            // Vincular RenderTexture a la textura principal y _BaseMap
            if (m_RenderTexture != null)
            {
                mat.mainTexture = m_RenderTexture;
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", m_RenderTexture);
                }
            }

            // Inversión en mainTexture (offset 1, tiling -1)
            mat.mainTextureScale = new Vector2(-1f, 1f);
            mat.mainTextureOffset = new Vector2(1f, 0f);

            // Inversión explícita para shader URP Lit y Unlit (_BaseMap)
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
                mat.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
            }

            // Material de doble cara (Cull Off) para garantizar visibilidad sin problemas de backface culling
            if (mat.HasProperty("_Cull"))
            {
                mat.SetFloat("_Cull", 0f);
            }
            mat.doubleSidedGI = true;
        }

        /// <summary>
        /// Retorna el material de la superficie del espejo, instanciándolo en Play Mode si es necesario.
        /// </summary>
        public Material GetMirrorMaterial()
        {
            if (m_InstancedMaterial != null) return m_InstancedMaterial;

            if (m_MirrorSurfaceRenderer != null)
            {
                if (Application.isPlaying)
                {
                    m_InstancedMaterial = m_MirrorSurfaceRenderer.material;
                    return m_InstancedMaterial;
                }
                return m_MirrorSurfaceRenderer.sharedMaterial;
            }

            if (m_MirrorMaterial != null)
            {
                if (Application.isPlaying)
                {
                    m_InstancedMaterial = new Material(m_MirrorMaterial);
                    return m_InstancedMaterial;
                }
                return m_MirrorMaterial;
            }

            return null;
        }

        /// <summary>
        /// Verifica si la cabeza del conductor está orientada hacia el espejo.
        /// Si m_EnableAngleCulling es false o no hay cabeza asignada, devuelve true para no dejar el espejo negro.
        /// </summary>
        public bool CheckVisibilityFromHead()
        {
            if (!m_EnableAngleCulling) return true;

            ResolveDriverHead();
            if (m_DriverHead == null) return true;

            Vector3 dirToMirror = (transform.position - m_DriverHead.position).normalized;
            float angle = Vector3.Angle(m_DriverHead.forward, dirToMirror);
            return angle <= m_CullingAngleThreshold;
        }

        /// <summary>
        /// Resuelve la cabeza del conductor buscando primero Camera.main, luego XROrigin y finalmente cualquier cámara activa.
        /// </summary>
        public void ResolveDriverHead()
        {
            if (m_DriverHead != null && m_DriverHead.gameObject.activeInHierarchy) return;

            // 1. Camera.main activa
            Camera mainCam = Camera.main;
            if (mainCam != null && mainCam.gameObject.activeInHierarchy && mainCam != m_MirrorCamera)
            {
                m_DriverHead = mainCam.transform;
                return;
            }

            // 2. XR Origin (XR Rig) en la escena
            var xrOrigin = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                m_DriverHead = xrOrigin.Camera.transform;
                return;
            }

            // 3. Fallback a GameObject con tag MainCamera
            GameObject tagged = GameObject.FindWithTag("MainCamera");
            if (tagged != null && tagged.activeInHierarchy)
            {
                m_DriverHead = tagged.transform;
                return;
            }

            // 4. Fallback a cualquier cámara de juego activa que no sea un espejo
            var allCams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            foreach (var c in allCams)
            {
                if (c.GetComponent<VehicleMirrorCamera>() == null && c.isActiveAndEnabled)
                {
                    m_DriverHead = c.transform;
                    return;
                }
            }
        }

        /// <summary>
        /// Fuerza un renderizado explícito inmediato de esta cámara de espejo sobre su RenderTexture.
        /// </summary>
        [ContextMenu("Force Render")]
        public void ForceRender()
        {
            EnsureRenderTextureCreated();
            ApplyOpticalInversion();

            if (m_MirrorCamera == null) return;

            m_MirrorCamera.enabled = true;
            try
            {
                m_MirrorCamera.Render();
            }
            catch (Exception)
            {
                // En URP, si Render() directo es interceptado, enabled=true garantiza el pase en el frame loop
            }
        }

        private void LateUpdate()
        {
            // Ejecutar el coordinador de Time-Slicing una sola vez por cuadro para todas las cámaras
            if (s_LastCoordinatorFrame != Time.frameCount)
            {
                s_LastCoordinatorFrame = Time.frameCount;
                CoordinateAllMirrors();
            }
        }

        /// <summary>
        /// Coordinador global de cámaras de espejos: evalúa visibilidad y time-slicing garantizado.
        /// </summary>
        private static void CoordinateAllMirrors()
        {
            if (s_AllMirrorCameras.Count == 0) return;

            List<VehicleMirrorCamera> visibleTimeSliced = new List<VehicleMirrorCamera>();

            for (int i = 0; i < s_AllMirrorCameras.Count; i++)
            {
                VehicleMirrorCamera mirror = s_AllMirrorCameras[i];
                if (mirror == null || mirror.m_MirrorCamera == null) continue;

                mirror.EnsureRenderTextureCreated();
                mirror.m_IsVisibleToDriver = mirror.CheckVisibilityFromHead();

                // Durante los primeros frames tras inicio, renderizar todos para asegurar buffers cálidos
                if (mirror.m_StartupFramesRendered < STARTUP_FRAMES_REQUIRED)
                {
                    mirror.m_StartupFramesRendered++;
                    mirror.m_MirrorCamera.enabled = true;
                    try { mirror.m_MirrorCamera.Render(); } catch { }
                    continue;
                }

                if (!mirror.m_IsVisibleToDriver)
                {
                    mirror.m_MirrorCamera.enabled = false;
                }
                else if (!mirror.m_EnableTimeSlicing)
                {
                    mirror.m_MirrorCamera.enabled = true;
                    try { mirror.m_MirrorCamera.Render(); } catch { }
                }
                else
                {
                    visibleTimeSliced.Add(mirror);
                }
            }

            if (visibleTimeSliced.Count == 0) return;

            if (visibleTimeSliced.Count == 1)
            {
                VehicleMirrorCamera single = visibleTimeSliced[0];
                single.m_MirrorCamera.enabled = true;
                try { single.m_MirrorCamera.Render(); } catch { }
            }
            else
            {
                // Alternancia round-robin estricta: un solo espejo renderiza por cuadro
                s_CurrentSliceIndex = (s_CurrentSliceIndex + 1) % visibleTimeSliced.Count;

                for (int i = 0; i < visibleTimeSliced.Count; i++)
                {
                    VehicleMirrorCamera mirror = visibleTimeSliced[i];
                    bool isMySlice = (i == s_CurrentSliceIndex);
                    mirror.m_MirrorCamera.enabled = isMySlice;

                    if (isMySlice)
                    {
                        try { mirror.m_MirrorCamera.Render(); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Asigna programáticamente la RenderTexture y actualiza la cámara y el material.
        /// </summary>
        public void SetRenderTexture(RenderTexture rt)
        {
            m_RenderTexture = rt;
            EnsureRenderTextureCreated();
            ApplyOpticalInversion();
        }

        /// <summary>
        /// Configura el Renderer de la superficie del espejo y aplica la inversión óptica.
        /// </summary>
        public void SetSurfaceRenderer(Renderer rend)
        {
            m_MirrorSurfaceRenderer = rend;
            ApplyOpticalInversion();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_MirrorCamera == null)
            {
                m_MirrorCamera = GetComponent<Camera>();
            }

            if (m_MirrorCamera != null)
            {
                m_MirrorCamera.farClipPlane = m_FarClipPlane;
                m_MirrorCamera.fieldOfView = m_FieldOfView;
            }

            EnsureNoAudioListener();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_MirrorCamera != null)
            {
                Gizmos.color = m_IsVisibleToDriver ? Color.green : Color.red;
                Gizmos.DrawRay(transform.position, transform.forward * 2f);

                if (m_DriverHead != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(m_DriverHead.position, transform.position);
                }
            }
        }
#endif
    }
}
