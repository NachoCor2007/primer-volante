using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Tic-tac de los guiños. Dos fuentes 3D en el tablero (izquierda y derecha del conductor) que
    /// suenan en sincronía con el reloj único de parpadeo (<see cref="TurnSignalLightController"/>):
    /// fase "on" → tick, fase "off" → tock, solo en los lados activos (balizas = ambos).
    /// </summary>
    public class VehicleTurnSignalAudio : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private TurnSignalLightController m_BlinkClock;
        [SerializeField] private VehicleAudioProfile m_Profile;

        [Header("Fuentes")]
        [Tooltip("Fuente en el tablero, lado izquierdo del conductor.")]
        [SerializeField] private AudioSource m_LeftSource;

        [Tooltip("Fuente en el tablero, lado derecho del conductor.")]
        [SerializeField] private AudioSource m_RightSource;

        [Header("Mixer")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private float m_ReadyAt;

        private void Awake()
        {
            if (m_BlinkClock == null)
                m_BlinkClock = GetComponentInParent<TurnSignalLightController>();
            if (m_BlinkClock == null)
            {
                var vehicle = GetComponentInParent<VehicleController>();
                if (vehicle != null) m_BlinkClock = vehicle.GetComponentInChildren<TurnSignalLightController>(true);
            }

            float min = m_Profile != null ? m_Profile.CabinMinDistance : 0.3f;
            float max = m_Profile != null ? m_Profile.CabinMaxDistance : 5f;
            VehicleAudioUtil.ConfigureSource(m_LeftSource, false, min, max, m_MixerGroup);
            VehicleAudioUtil.ConfigureSource(m_RightSource, false, min, max, m_MixerGroup);
        }

        private void OnEnable()
        {
            m_ReadyAt = Time.time + VehicleAudioUtil.InitializationGraceSeconds;
            if (m_BlinkClock != null) m_BlinkClock.OnBlinkPhaseChanged += OnBlinkPhaseChanged;
        }

        private void OnDisable()
        {
            if (m_BlinkClock != null) m_BlinkClock.OnBlinkPhaseChanged -= OnBlinkPhaseChanged;
        }

        private void OnDestroy()
        {
            if (m_BlinkClock != null) m_BlinkClock.OnBlinkPhaseChanged -= OnBlinkPhaseChanged;
        }

        private void OnBlinkPhaseChanged(bool on)
        {
            if (m_Profile == null || Time.time < m_ReadyAt) return;

            AudioClip clip = on ? m_Profile.BlinkerTick : m_Profile.BlinkerTock;
            float volume = m_Profile.BlinkerVolume;

            if (m_BlinkClock.IsLeftActive) VehicleAudioUtil.PlayOneShot(m_LeftSource, clip, volume);
            if (m_BlinkClock.IsRightActive) VehicleAudioUtil.PlayOneShot(m_RightSource, clip, volume);
        }
    }
}
