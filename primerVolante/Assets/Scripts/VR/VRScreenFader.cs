using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controlador de oscurecimiento de pantalla (Fade Out / Fade In) para transiciones y reintentos en VR.
    /// Crea o gestiona un canvas frontal sobre la cámara principal del usuario para realizar fundidos
    /// a negro suaves, previniendo la frustración, el parpadeo abrupto y la cinetosis (VR motion sickness).
    /// Cumple estrictamente la regla de cero APIs obsoletas de AGENTS.md.
    /// </summary>
    public class VRScreenFader : MonoBehaviour
    {
        private static VRScreenFader s_Instance;

        [Header("Configuración de Fade")]
        [Tooltip("Color del fundido (por defecto negro opaco).")]
        [SerializeField] private Color m_FadeColor = Color.black;

        [Tooltip("Distancia frontal a la que se posiciona el plano de fade respecto al visor.")]
        [SerializeField] private float m_DistanceToCamera = 0.20f;

        [Tooltip("Cámara objetivo. Si es nula se busca Camera.main o cualquier cámara activa.")]
        [SerializeField] private Camera m_TargetCamera;

        private GameObject m_OverlayObject;
        private Canvas m_Canvas;
        private CanvasGroup m_CanvasGroup;
        private Image m_BlackImage;
        private Coroutine m_CurrentFadeCoroutine;
        private float m_CurrentAlpha = 0f;

        public static VRScreenFader Instance => s_Instance;
        public float CurrentAlpha => m_CurrentAlpha;
        public bool IsFading => m_CurrentFadeCoroutine != null;
        public Camera TargetCamera
        {
            get => m_TargetCamera;
            set
            {
                m_TargetCamera = value;
                AttachToCamera();
            }
        }

        private void Awake()
        {
            if (s_Instance == null)
            {
                s_Instance = this;
            }
            else if (s_Instance != this)
            {
                Destroy(this);
                return;
            }

            EnsureCameraReference();
            SetupOverlayHierarchy();
            SetAlphaImmediate(0f);
        }

        private void OnEnable()
        {
            if (s_Instance == null) s_Instance = this;
            EnsureCameraReference();
            AttachToCamera();
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        /// <summary>
        /// Asegura la existencia de un VRScreenFader en la escena actual utilizando APIs no obsoletas.
        /// </summary>
        public static VRScreenFader EnsureScreenFader()
        {
            if (s_Instance != null) return s_Instance;

            VRScreenFader found = UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            if (found != null)
            {
                s_Instance = found;
                return s_Instance;
            }

            GameObject faderObj = new GameObject("VR_ScreenFader");
            s_Instance = faderObj.AddComponent<VRScreenFader>();
            return s_Instance;
        }

        private void EnsureCameraReference()
        {
            if (m_TargetCamera == null)
            {
                m_TargetCamera = Camera.main;
            }

            if (m_TargetCamera == null)
            {
                m_TargetCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            }
        }

        private void SetupOverlayHierarchy()
        {
            if (m_OverlayObject == null)
            {
                Transform existing = transform.Find("ScreenFadeOverlay");
                if (existing != null)
                {
                    m_OverlayObject = existing.gameObject;
                }
                else
                {
                    m_OverlayObject = new GameObject("ScreenFadeOverlay");
                    m_OverlayObject.transform.SetParent(transform, false);
                }
            }

            m_Canvas = m_OverlayObject.GetComponent<Canvas>();
            if (m_Canvas == null)
            {
                m_Canvas = m_OverlayObject.AddComponent<Canvas>();
            }
            m_Canvas.renderMode = RenderMode.WorldSpace;
            m_Canvas.sortingOrder = 32767;

            m_CanvasGroup = m_OverlayObject.GetComponent<CanvasGroup>();
            if (m_CanvasGroup == null)
            {
                m_CanvasGroup = m_OverlayObject.AddComponent<CanvasGroup>();
            }
            m_CanvasGroup.blocksRaycasts = false;
            m_CanvasGroup.interactable = false;

            m_BlackImage = m_OverlayObject.GetComponentInChildren<Image>(true);
            if (m_BlackImage == null)
            {
                GameObject imgObj = new GameObject("BlackQuad");
                imgObj.transform.SetParent(m_OverlayObject.transform, false);
                m_BlackImage = imgObj.AddComponent<Image>();
            }

            m_BlackImage.color = m_FadeColor;
            m_BlackImage.raycastTarget = false;
            RectTransform rt = m_BlackImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = new Vector2(4000f, 4000f);
            rt.anchoredPosition = Vector2.zero;

            AttachToCamera();
        }

        private void AttachToCamera()
        {
            if (m_OverlayObject == null) return;

            if (m_TargetCamera != null)
            {
                m_OverlayObject.transform.SetParent(m_TargetCamera.transform, false);
                m_OverlayObject.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0.1f, m_DistanceToCamera));
                m_OverlayObject.transform.localRotation = Quaternion.identity;
                m_OverlayObject.transform.localScale = Vector3.one * 0.001f;
            }
        }

        /// <summary>
        /// Aplica inmediatamente una opacidad a la pantalla sin animación.
        /// </summary>
        public void SetAlphaImmediate(float alpha)
        {
            m_CurrentAlpha = Mathf.Clamp01(alpha);
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.alpha = m_CurrentAlpha;
            }
            if (m_OverlayObject != null)
            {
                m_OverlayObject.SetActive(m_CurrentAlpha > 0.001f);
            }
        }

        /// <summary>
        /// Inicia un fundido a negro suave durante la duración indicada.
        /// </summary>
        public Coroutine FadeOut(float duration = 0.4f, Action onComplete = null)
        {
            if (m_CurrentFadeCoroutine != null) StopCoroutine(m_CurrentFadeCoroutine);
            m_CurrentFadeCoroutine = StartCoroutine(FadeRoutine(m_CurrentAlpha, 1f, duration, onComplete));
            return m_CurrentFadeCoroutine;
        }

        /// <summary>
        /// Inicia un fundido desde negro hacia transparente durante la duración indicada.
        /// </summary>
        public Coroutine FadeIn(float duration = 0.4f, Action onComplete = null)
        {
            if (m_CurrentFadeCoroutine != null) StopCoroutine(m_CurrentFadeCoroutine);
            m_CurrentFadeCoroutine = StartCoroutine(FadeRoutine(m_CurrentAlpha, 0f, duration, onComplete));
            return m_CurrentFadeCoroutine;
        }

        /// <summary>
        /// Corutina que realiza fundido a negro suave (Fade Out).
        /// </summary>
        public IEnumerator FadeOutRoutine(float duration = 0.4f)
        {
            yield return FadeRoutine(m_CurrentAlpha, 1f, duration, null);
        }

        /// <summary>
        /// Corutina que realiza fundido hacia la luz/transparente (Fade In).
        /// </summary>
        public IEnumerator FadeInRoutine(float duration = 0.4f)
        {
            yield return FadeRoutine(m_CurrentAlpha, 0f, duration, null);
        }

        private IEnumerator FadeRoutine(float fromAlpha, float toAlpha, float duration, Action onComplete)
        {
            EnsureCameraReference();
            AttachToCamera();

            if (m_OverlayObject != null)
            {
                m_OverlayObject.SetActive(true);
            }

            float elapsed = 0f;
            float totalDuration = Mathf.Max(0.01f, duration);

            while (elapsed < totalDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / totalDuration);
                // Curva de interpolación suave senoidal
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                SetAlphaImmediate(Mathf.Lerp(fromAlpha, toAlpha, smoothT));
                yield return null;
            }

            SetAlphaImmediate(toAlpha);
            m_CurrentFadeCoroutine = null;
            onComplete?.Invoke();
        }
    }
}
