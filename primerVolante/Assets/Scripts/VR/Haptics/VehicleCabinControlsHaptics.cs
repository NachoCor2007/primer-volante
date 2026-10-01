using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Confirmaciones hápticas de los controles de cabina (espejo de <see cref="VehicleCabinControlsAudio"/>
    /// y <see cref="VehicleHandbrakeAudio"/>): cambio de marcha, clic del guiño, detents de la perilla
    /// de luces, trinquete y liberación del freno de mano, arranque aceptado/rechazado y balizas.
    /// Cada confirmación se envía solo a la mano que está operando el control; sin mano
    /// (pruebas con teclado) no hace nada.
    /// </summary>
    public class VehicleCabinControlsHaptics : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private VRTurnSignal m_TurnSignal;
        [SerializeField] private VRGearShifter m_GearShifter;
        [SerializeField] private VRHeadlightKnob m_HeadlightKnob;
        [SerializeField] private VRHandbrake m_Handbrake;

        [Tooltip("Interactable del botón de arranque (StartButton).")]
        [SerializeField] private XRBaseInteractable m_StartInteractable;

        [Tooltip("Interactable del botón de balizas (HazardButton).")]
        [SerializeField] private XRBaseInteractable m_HazardInteractable;

        [Header("Perfiles")]
        [SerializeField] private CabinHapticProfile m_Profile;

        [Tooltip("Perfil de audio: de acá se toma el escalón del trinquete del freno de mano (RatchetStep).")]
        [SerializeField] private VehicleAudioProfile m_AudioProfile;

        [Header("Debugging / Logs")]
        [SerializeField] private bool m_EnableDebugLogs = false;

        private const float k_DefaultRatchetStep = 0.125f;

        private XRBaseInteractable m_TurnSignalInteractable;
        private XRBaseInteractable m_GearInteractable;
        private XRBaseInteractable m_KnobInteractable;
        private XRBaseInteractable m_HandbrakeInteractable;

        private float m_ReadyAt;
        private float m_LastEngagement;
        private bool m_WasEngaged;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
            if (m_VehicleController != null)
            {
                if (m_TurnSignal == null) m_TurnSignal = m_VehicleController.GetComponentInChildren<VRTurnSignal>(true);
                if (m_GearShifter == null) m_GearShifter = m_VehicleController.GetComponentInChildren<VRGearShifter>(true);
                if (m_HeadlightKnob == null) m_HeadlightKnob = m_VehicleController.GetComponentInChildren<VRHeadlightKnob>(true);
                if (m_Handbrake == null) m_Handbrake = m_VehicleController.GetComponentInChildren<VRHandbrake>(true);
            }

            m_TurnSignalInteractable = m_TurnSignal != null ? m_TurnSignal.GetComponent<XRBaseInteractable>() : null;
            m_GearInteractable = m_GearShifter != null ? m_GearShifter.GetComponent<XRBaseInteractable>() : null;
            m_KnobInteractable = m_HeadlightKnob != null ? m_HeadlightKnob.GetComponent<XRBaseInteractable>() : null;
            m_HandbrakeInteractable = m_Handbrake != null ? m_Handbrake.GetComponent<XRBaseInteractable>() : null;
        }

        private void OnEnable()
        {
            m_ReadyAt = Time.time + VehicleAudioUtil.InitializationGraceSeconds;

            if (m_VehicleController != null)
            {
                m_VehicleController.OnEngineToggleRequested += OnEngineToggleRequested;
                m_VehicleController.OnEngineStartRejected += OnEngineStartRejected;
                m_VehicleController.OnHazardChanged += OnHazardChanged;
            }
            if (m_TurnSignal != null) m_TurnSignal.OnTurnSignalChanged.AddListener(OnTurnSignalChanged);
            if (m_GearShifter != null) m_GearShifter.OnGearChanged.AddListener(OnGearChanged);
            if (m_HeadlightKnob != null) m_HeadlightKnob.ModeChanged += OnHeadlightModeChanged;
            if (m_Handbrake != null)
            {
                m_LastEngagement = m_Handbrake.Engagement;
                m_WasEngaged = m_Handbrake.IsEngaged;
                m_Handbrake.OnEngagementChanged.AddListener(OnHandbrakeEngagementChanged);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (m_VehicleController != null)
            {
                m_VehicleController.OnEngineToggleRequested -= OnEngineToggleRequested;
                m_VehicleController.OnEngineStartRejected -= OnEngineStartRejected;
                m_VehicleController.OnHazardChanged -= OnHazardChanged;
            }
            if (m_TurnSignal != null) m_TurnSignal.OnTurnSignalChanged.RemoveListener(OnTurnSignalChanged);
            if (m_GearShifter != null) m_GearShifter.OnGearChanged.RemoveListener(OnGearChanged);
            if (m_HeadlightKnob != null) m_HeadlightKnob.ModeChanged -= OnHeadlightModeChanged;
            if (m_Handbrake != null) m_Handbrake.OnEngagementChanged.RemoveListener(OnHandbrakeEngagementChanged);
        }

        private bool IsReady => m_Profile != null && Time.time >= m_ReadyAt;

        // ------------------------------------------------------------------ Arranque

        // TryToggleEngine() dispara OnEngineToggleRequested antes de validar y, si rechaza,
        // OnEngineStartRejected después: el patrón de error se reproduce segundo y reemplaza al de aceptado.
        private void OnEngineToggleRequested()
        {
            Play(m_StartInteractable, m_Profile != null ? m_Profile.Confirmations.EngineStartAccepted : null, "arranque aceptado");
        }

        private void OnEngineStartRejected(string reason)
        {
            Play(m_StartInteractable, m_Profile != null ? m_Profile.Confirmations.EngineStartRejected : null, "arranque rechazado");
        }

        private void OnHazardChanged(bool active)
        {
            Play(m_HazardInteractable, m_Profile != null ? m_Profile.Confirmations.HazardToggle : null, "balizas");
        }

        // ------------------------------------------------------------------ Palancas y perilla

        private void OnTurnSignalChanged(TurnSignalState state)
        {
            // Solo con la leva agarrada: la auto-cancelación que provoca el volante no vibra.
            if (m_TurnSignal == null || !m_TurnSignal.IsGrabbed) return;
            Play(m_TurnSignalInteractable, m_Profile != null ? m_Profile.Confirmations.TurnSignalClick : null, "clic del guiño");
        }

        private void OnGearChanged(GearState gear)
        {
            Play(m_GearInteractable, m_Profile != null ? m_Profile.Confirmations.GearChange : null, "cambio de marcha");
        }

        private void OnHeadlightModeChanged(HeadlightMode mode)
        {
            // Solo mientras la perilla está agarrada (los detents que se sienten al girarla).
            if (m_HeadlightKnob == null || !m_HeadlightKnob.IsGrabbed) return;
            Play(m_KnobInteractable, m_Profile != null ? m_Profile.Confirmations.HeadlightDetent : null, "detent de luces");
        }

        // ------------------------------------------------------------------ Freno de mano

        private void OnHandbrakeEngagementChanged(float engagement)
        {
            float previous = m_LastEngagement;
            bool wasEngaged = m_WasEngaged;
            m_LastEngagement = engagement;
            m_WasEngaged = m_Handbrake != null && m_Handbrake.IsEngaged;

            if (!IsReady) return;
            if (m_Profile == null) return;

            if (wasEngaged && !m_WasEngaged)
            {
                Play(m_HandbrakeInteractable, m_Profile.Confirmations.HandbrakeRelease, "freno de mano liberado");
                return;
            }

            float step = m_AudioProfile != null ? m_AudioProfile.RatchetStep : k_DefaultRatchetStep;
            if (engagement > previous && VehicleHandbrakeAudio.CrossedRatchetStep(previous, engagement, step))
            {
                Play(m_HandbrakeInteractable, m_Profile.Confirmations.HandbrakeRatchet, "trinquete");
            }
        }

        // ------------------------------------------------------------------ Helpers

        private void Play(XRBaseInteractable interactable, HapticPattern pattern, string label)
        {
            if (!IsReady || pattern == null) return;

            HapticImpulsePlayer player = GetOperatorPlayer(interactable);
            if (player == null) return;

            HapticPlayback.Play(player, pattern, m_Profile.MasterAmplitude);
            if (m_EnableDebugLogs)
                Debug.Log($"[VehicleCabinControlsHaptics] 📳 {label} → {player.name}");
        }

        /// <summary>
        /// Player de la mano que está operando el interactable (la primera que lo selecciona). Null si nadie lo hace.
        /// </summary>
        private static HapticImpulsePlayer GetOperatorPlayer(XRBaseInteractable interactable)
        {
            if (interactable == null || interactable.interactorsSelecting.Count == 0) return null;
            return HapticPlayback.ResolvePlayer(interactable.interactorsSelecting[0]);
        }
    }
}
