using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Clics mecánicos de los controles de cabina: botón de arranque (y sonido de arranque
    /// rechazado), botón de balizas, palanca de guiño y palanca de cambios. La perilla de luces
    /// usa los campos <c>m_AudioSource</c>/<c>m_ClickClip</c> propios de <see cref="VRHeadlightKnob"/>.
    /// </summary>
    public class VehicleCabinControlsAudio : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private VRTurnSignal m_TurnSignal;
        [SerializeField] private VRGearShifter m_GearShifter;
        [SerializeField] private VehicleAudioProfile m_Profile;

        [Header("Fuentes (ubicadas en cada control)")]
        [SerializeField] private AudioSource m_StartButtonSource;
        [SerializeField] private AudioSource m_HazardButtonSource;
        [SerializeField] private AudioSource m_TurnSignalSource;
        [SerializeField] private AudioSource m_GearShifterSource;

        [Header("Mixer")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private float m_ReadyAt;
        private float m_LastStalkTime = -10f;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
            if (m_VehicleController != null)
            {
                if (m_TurnSignal == null) m_TurnSignal = m_VehicleController.GetComponentInChildren<VRTurnSignal>(true);
                if (m_GearShifter == null) m_GearShifter = m_VehicleController.GetComponentInChildren<VRGearShifter>(true);
            }

            float min = m_Profile != null ? m_Profile.CabinMinDistance : 0.3f;
            float max = m_Profile != null ? m_Profile.CabinMaxDistance : 5f;
            VehicleAudioUtil.ConfigureSource(m_StartButtonSource, false, min, max, m_MixerGroup);
            VehicleAudioUtil.ConfigureSource(m_HazardButtonSource, false, min, max, m_MixerGroup);
            VehicleAudioUtil.ConfigureSource(m_TurnSignalSource, false, min, max, m_MixerGroup);
            VehicleAudioUtil.ConfigureSource(m_GearShifterSource, false, min, max, m_MixerGroup);
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
        }

        private bool IsReady => m_Profile != null && Time.time >= m_ReadyAt;

        private void OnEngineToggleRequested()
        {
            if (!IsReady) return;
            VehicleAudioUtil.PlayOneShot(m_StartButtonSource, m_Profile.ButtonClick, m_Profile.ButtonClickVolume);
        }

        private void OnEngineStartRejected(string reason)
        {
            if (!IsReady) return;
            VehicleAudioUtil.PlayOneShot(m_StartButtonSource, m_Profile.EngineStartFail, m_Profile.EngineStartFailVolume);
        }

        private void OnHazardChanged(bool active)
        {
            if (!IsReady) return;
            VehicleAudioUtil.PlayOneShot(m_HazardButtonSource, m_Profile.ButtonClick, m_Profile.ButtonClickVolume);
        }

        private void OnTurnSignalChanged(TurnSignalState state)
        {
            if (!IsReady) return;
            if (Time.time - m_LastStalkTime < m_Profile.StalkClickDebounce) return;
            m_LastStalkTime = Time.time;
            VehicleAudioUtil.PlayOneShot(m_TurnSignalSource, m_Profile.StalkClick, m_Profile.StalkClickVolume);
        }

        private void OnGearChanged(GearState gear)
        {
            if (!IsReady) return;
            VehicleAudioUtil.PlayOneShot(m_GearShifterSource, m_Profile.GearClunk, m_Profile.GearClunkVolume);
        }
    }
}
