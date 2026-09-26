using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Mini-panel eléctrico de control de espejos laterales montado en la puerta del conductor.
    /// Implementa:
    /// 1. Selector de 3 posiciones: [ Left | OFF | Right ]. En OFF el sistema queda bloqueado.
    /// 2. Mini D-Pad con 4 direcciones (Arriba, Abajo, Izquierda, Derecha).
    /// 3. Interacción en VR mediante XRSimpleInteractable (tactil/poke/ray) para mover suavemente el espejo seleccionado.
    /// 4. Controles de depuración por teclado: '[' y ']' para alternar espejo, flechas del teclado para orientar.
    /// 5. Sonidos diegéticos de conmutador (clic) y servomotor eléctrico.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VRSideMirrorControlPanel : MonoBehaviour
    {
        public enum MirrorSelection
        {
            Left = 0,
            Off = 1,
            Right = 2
        }

        [Header("Referencias a Espejos Laterales")]
        [Tooltip("Espejo lateral izquierdo exterior.")]
        [SerializeField] private VRSideMirror m_LeftMirror;

        [Tooltip("Espejo lateral derecho exterior.")]
        [SerializeField] private VRSideMirror m_RightMirror;

        [Header("Estado del Selector")]
        [Tooltip("Posición actual del conmutador (Left, Off, Right).")]
        [SerializeField] private MirrorSelection m_CurrentSelection = MirrorSelection.Off;

        [Tooltip("Velocidad de ajuste de los servomotores en grados por segundo.")]
        [SerializeField] private float m_AdjustmentSpeed = 16f;

        [Header("Interactables del Selector (3 Posiciones)")]
        [SerializeField] private XRSimpleInteractable m_BtnSelectLeft;
        [SerializeField] private XRSimpleInteractable m_BtnSelectOff;
        [SerializeField] private XRSimpleInteractable m_BtnSelectRight;

        [Header("Interactables del Mini D-Pad (4 Direcciones)")]
        [SerializeField] private XRSimpleInteractable m_BtnUp;
        [SerializeField] private XRSimpleInteractable m_BtnDown;
        [SerializeField] private XRSimpleInteractable m_BtnLeft;
        [SerializeField] private XRSimpleInteractable m_BtnRight;

        [Header("Feedback Visual")]
        [SerializeField] private Renderer m_IndicatorLeft;
        [SerializeField] private Renderer m_IndicatorOff;
        [SerializeField] private Renderer m_IndicatorRight;
        [SerializeField] private Color m_ActiveColor = new Color(0.15f, 0.85f, 0.25f);
        [SerializeField] private Color m_InactiveColor = new Color(0.35f, 0.35f, 0.38f);
        [SerializeField] private Color m_OffSelectedColor = new Color(0.85f, 0.25f, 0.15f);

        [Header("Efectos de Audio")]
        [SerializeField] private AudioSource m_AudioSource;
        [SerializeField] private AudioClip m_ClickSound;
        [SerializeField] private AudioClip m_MotorSound;

        private Rigidbody m_Rigidbody;
        private bool m_IsMotorPlaying = false;
        private Material m_MatLeft;
        private Material m_MatOff;
        private Material m_MatRight;

        public MirrorSelection CurrentSelection => m_CurrentSelection;
        public VRSideMirror LeftMirror => m_LeftMirror;
        public VRSideMirror RightMirror => m_RightMirror;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            if (m_Rigidbody == null)
            {
                m_Rigidbody = gameObject.AddComponent<Rigidbody>();
            }
            m_Rigidbody.isKinematic = true;
            m_Rigidbody.useGravity = false;

            if (m_AudioSource == null)
            {
                m_AudioSource = GetComponent<AudioSource>();
                if (m_AudioSource == null)
                {
                    m_AudioSource = gameObject.AddComponent<AudioSource>();
                    m_AudioSource.playOnAwake = false;
                    m_AudioSource.spatialBlend = 1f;
                    m_AudioSource.minDistance = 0.1f;
                    m_AudioSource.maxDistance = 3f;
                }
            }

            CacheVisualMaterials();
        }

        private void Start()
        {
            AutoLocateMirrorsIfMissing();
            RegisterInteractableListeners();
            UpdateVisuals();
        }

        private void OnDestroy()
        {
            UnregisterInteractableListeners();
            DestroyCachedMaterials();
        }

        private void AutoLocateMirrorsIfMissing()
        {
            if (m_LeftMirror == null || m_RightMirror == null)
            {
                var mirrors = Object.FindObjectsByType<VRSideMirror>(FindObjectsInactive.Exclude);
                foreach (var mirror in mirrors)
                {
                    if (mirror.Side == VRSideMirror.MirrorSide.Left && m_LeftMirror == null)
                    {
                        m_LeftMirror = mirror;
                    }
                    else if (mirror.Side == VRSideMirror.MirrorSide.Right && m_RightMirror == null)
                    {
                        m_RightMirror = mirror;
                    }
                }
            }
        }

        private void CacheVisualMaterials()
        {
            if (m_IndicatorLeft != null) m_MatLeft = m_IndicatorLeft.material;
            if (m_IndicatorOff != null) m_MatOff = m_IndicatorOff.material;
            if (m_IndicatorRight != null) m_MatRight = m_IndicatorRight.material;
        }

        private void DestroyCachedMaterials()
        {
            if (m_MatLeft != null) Destroy(m_MatLeft);
            if (m_MatOff != null) Destroy(m_MatOff);
            if (m_MatRight != null) Destroy(m_MatRight);
        }

        private void RegisterInteractableListeners()
        {
            if (m_BtnSelectLeft != null) m_BtnSelectLeft.selectEntered.AddListener(OnSelectLeftClicked);
            if (m_BtnSelectOff != null) m_BtnSelectOff.selectEntered.AddListener(OnSelectOffClicked);
            if (m_BtnSelectRight != null) m_BtnSelectRight.selectEntered.AddListener(OnSelectRightClicked);

            if (m_BtnUp != null) m_BtnUp.selectEntered.AddListener(OnUpPulse);
            if (m_BtnDown != null) m_BtnDown.selectEntered.AddListener(OnDownPulse);
            if (m_BtnLeft != null) m_BtnLeft.selectEntered.AddListener(OnLeftPulse);
            if (m_BtnRight != null) m_BtnRight.selectEntered.AddListener(OnRightPulse);
        }

        private void UnregisterInteractableListeners()
        {
            if (m_BtnSelectLeft != null) m_BtnSelectLeft.selectEntered.RemoveListener(OnSelectLeftClicked);
            if (m_BtnSelectOff != null) m_BtnSelectOff.selectEntered.RemoveListener(OnSelectOffClicked);
            if (m_BtnSelectRight != null) m_BtnSelectRight.selectEntered.RemoveListener(OnSelectRightClicked);

            if (m_BtnUp != null) m_BtnUp.selectEntered.RemoveListener(OnUpPulse);
            if (m_BtnDown != null) m_BtnDown.selectEntered.RemoveListener(OnDownPulse);
            if (m_BtnLeft != null) m_BtnLeft.selectEntered.RemoveListener(OnLeftPulse);
            if (m_BtnRight != null) m_BtnRight.selectEntered.RemoveListener(OnRightPulse);
        }

        private void OnSelectLeftClicked(SelectEnterEventArgs args) => SetSelection(MirrorSelection.Left);
        private void OnSelectOffClicked(SelectEnterEventArgs args) => SetSelection(MirrorSelection.Off);
        private void OnSelectRightClicked(SelectEnterEventArgs args) => SetSelection(MirrorSelection.Right);

        private void OnUpPulse(SelectEnterEventArgs args) => AdjustPulse(new Vector2(0f, 1f));
        private void OnDownPulse(SelectEnterEventArgs args) => AdjustPulse(new Vector2(0f, -1f));
        private void OnLeftPulse(SelectEnterEventArgs args) => AdjustPulse(new Vector2(-1f, 0f));
        private void OnRightPulse(SelectEnterEventArgs args) => AdjustPulse(new Vector2(1f, 0f));

        private void Update()
        {
            Vector2 inputDir = Vector2.zero;

            // 1. D-Pad en VR (mantener presionado el botón para movimiento continuo)
            if (m_BtnUp != null && m_BtnUp.isSelected) inputDir.y += 1f;
            if (m_BtnDown != null && m_BtnDown.isSelected) inputDir.y -= 1f;
            if (m_BtnLeft != null && m_BtnLeft.isSelected) inputDir.x -= 1f;
            if (m_BtnRight != null && m_BtnRight.isSelected) inputDir.x += 1f;

            // 2. Controles de depuración por teclado
            ProcessKeyboardInput(ref inputDir);

            // 3. Aplicar ajuste analógico continuo al espejo seleccionado
            if (inputDir.sqrMagnitude > 0.001f && m_CurrentSelection != MirrorSelection.Off)
            {
                VRSideMirror targetMirror = GetSelectedMirror();
                if (targetMirror != null)
                {
                    targetMirror.Adjust(inputDir.normalized, m_AdjustmentSpeed);
                    PlayMotorSound();
                }
            }
            else
            {
                StopMotorSound();
            }
        }

        private void ProcessKeyboardInput(ref Vector2 inputDir)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return;

            // Alternar selector de espejo con '[' y ']'
            if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
            {
                CycleSelection(-1);
            }
            if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
            {
                CycleSelection(1);
            }

            // Flechas de dirección
            if (Keyboard.current.upArrowKey.isPressed) inputDir.y += 1f;
            if (Keyboard.current.downArrowKey.isPressed) inputDir.y -= 1f;
            if (Keyboard.current.leftArrowKey.isPressed) inputDir.x -= 1f;
            if (Keyboard.current.rightArrowKey.isPressed) inputDir.x += 1f;
#endif
        }

        /// <summary>
        /// Aplica un pulso discreto de movimiento al espejo activo.
        /// </summary>
        public void AdjustPulse(Vector2 direction, float degrees = 1.2f)
        {
            if (m_CurrentSelection == MirrorSelection.Off) return;

            VRSideMirror targetMirror = GetSelectedMirror();
            if (targetMirror != null)
            {
                targetMirror.AdjustPulse(direction, degrees);
                PlayMotorSoundOneShot();
            }
        }

        /// <summary>
        /// Cambia la posición del selector (Left, Off, Right) y actualiza feedback.
        /// </summary>
        public void SetSelection(MirrorSelection selection)
        {
            if (m_CurrentSelection == selection) return;

            m_CurrentSelection = selection;
            PlayClickSound();
            UpdateVisuals();
        }

        /// <summary>
        /// Alterna cíclicamente la selección (-1: hacia la izquierda, +1: hacia la derecha).
        /// </summary>
        public void CycleSelection(int step)
        {
            int next = (int)m_CurrentSelection + step;
            next = Mathf.Clamp(next, 0, 2);
            SetSelection((MirrorSelection)next);
        }

        public VRSideMirror GetSelectedMirror()
        {
            switch (m_CurrentSelection)
            {
                case MirrorSelection.Left:
                    return m_LeftMirror;
                case MirrorSelection.Right:
                    return m_RightMirror;
                default:
                    return null;
            }
        }

        public void UpdateVisuals()
        {
            if (m_MatLeft != null)
            {
                m_MatLeft.color = (m_CurrentSelection == MirrorSelection.Left) ? m_ActiveColor : m_InactiveColor;
            }
            if (m_MatOff != null)
            {
                m_MatOff.color = (m_CurrentSelection == MirrorSelection.Off) ? m_OffSelectedColor : m_InactiveColor;
            }
            if (m_MatRight != null)
            {
                m_MatRight.color = (m_CurrentSelection == MirrorSelection.Right) ? m_ActiveColor : m_InactiveColor;
            }
        }

        private void PlayClickSound()
        {
            if (m_AudioSource != null && m_ClickSound != null)
            {
                m_AudioSource.PlayOneShot(m_ClickSound, 0.7f);
            }
        }

        private void PlayMotorSound()
        {
            if (m_AudioSource != null && m_MotorSound != null && !m_IsMotorPlaying)
            {
                m_AudioSource.clip = m_MotorSound;
                m_AudioSource.loop = true;
                m_AudioSource.Play();
                m_IsMotorPlaying = true;
            }
        }

        private void PlayMotorSoundOneShot()
        {
            if (m_AudioSource != null && m_MotorSound != null && !m_IsMotorPlaying)
            {
                m_AudioSource.PlayOneShot(m_MotorSound, 0.4f);
            }
        }

        private void StopMotorSound()
        {
            if (m_IsMotorPlaying && m_AudioSource != null)
            {
                m_AudioSource.Stop();
                m_IsMotorPlaying = false;
            }
        }

        public void Configure(VRSideMirror leftMirror, VRSideMirror rightMirror,
                              XRSimpleInteractable btnLeft, XRSimpleInteractable btnOff, XRSimpleInteractable btnRight,
                              XRSimpleInteractable btnUp, XRSimpleInteractable btnDown, XRSimpleInteractable btnL, XRSimpleInteractable btnR,
                              Renderer indLeft, Renderer indOff, Renderer indRight)
        {
            m_LeftMirror = leftMirror;
            m_RightMirror = rightMirror;
            m_BtnSelectLeft = btnLeft;
            m_BtnSelectOff = btnOff;
            m_BtnSelectRight = btnRight;
            m_BtnUp = btnUp;
            m_BtnDown = btnDown;
            m_BtnLeft = btnL;
            m_BtnRight = btnR;
            m_IndicatorLeft = indLeft;
            m_IndicatorOff = indOff;
            m_IndicatorRight = indRight;

            CacheVisualMaterials();
            UpdateVisuals();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_Rigidbody == null)
            {
                m_Rigidbody = GetComponent<Rigidbody>();
            }
            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }
        }
#endif
    }
}
