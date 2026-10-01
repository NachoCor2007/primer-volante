using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Utilidad de reproducción de patrones hápticos. Resuelve el <see cref="HapticImpulsePlayer"/>
    /// de una mano a partir de su interactor y reproduce patrones de varios pulsos con un temporizador.
    /// Un patrón nuevo cancela al que estuviera sonando en esa misma mano (un impulso nuevo reemplaza
    /// al anterior en el dispositivo). Si no hay player (simulador, teclado) no hace nada.
    /// </summary>
    public static class HapticPlayback
    {
        private static PlaybackRunner s_Runner;
        private static readonly Dictionary<HapticImpulsePlayer, float> s_LastReachTime = new Dictionary<HapticImpulsePlayer, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Runner = null;
            s_LastReachTime.Clear();
        }

        /// <summary>Busca el player de la mano a la que pertenece un interactor (3D). Null si no hay.</summary>
        public static HapticImpulsePlayer ResolvePlayer(IXRInteractor interactor)
        {
            Component component = interactor as Component;
            return component != null ? component.GetComponentInParent<HapticImpulsePlayer>() : null;
        }

        /// <summary>Busca el player de la mano a la que pertenece un interactor de UI. Null si no hay (p. ej. mouse).</summary>
        public static HapticImpulsePlayer ResolvePlayer(IUIInteractor interactor)
        {
            Component component = interactor as Component;
            return component != null ? component.GetComponentInParent<HapticImpulsePlayer>() : null;
        }

        /// <summary>
        /// Reproduce un patrón en la mano indicada, reemplazando al que estuviera sonando en ella.
        /// Devuelve false si no se pudo (player o patrón nulos).
        /// </summary>
        public static bool Play(HapticImpulsePlayer player, HapticPattern pattern, float masterAmplitude = 1f)
        {
            if (player == null || pattern == null || pattern.Pulse == null) return false;
            if (!player.isActiveAndEnabled) return false;

            PlaybackRunner runner = GetRunner();
            if (runner == null) return false;

            runner.Play(player, pattern, masterAmplitude);
            return true;
        }

        /// <summary>Reproduce un patrón en la mano de un interactor 3D.</summary>
        public static bool Play(IXRInteractor interactor, HapticPattern pattern, float masterAmplitude = 1f)
        {
            return Play(ResolvePlayer(interactor), pattern, masterAmplitude);
        }

        /// <summary>Reproduce un patrón en la mano de un interactor de UI.</summary>
        public static bool Play(IUIInteractor interactor, HapticPattern pattern, float masterAmplitude = 1f)
        {
            return Play(ResolvePlayer(interactor), pattern, masterAmplitude);
        }

        /// <summary>
        /// Registra un háptico de alcance en la mano y devuelve true si ya pasó el cooldown desde el anterior.
        /// El cooldown es por mano y compartido entre todos los controles.
        /// </summary>
        public static bool TryConsumeReachCooldown(HapticImpulsePlayer player, float cooldownSeconds)
        {
            if (player == null) return false;

            float now = Time.unscaledTime;
            float last = s_LastReachTime.TryGetValue(player, out float stored) ? stored : float.NegativeInfinity;

            if (!HasCooldownElapsed(now, last, cooldownSeconds)) return false;

            s_LastReachTime[player] = now;
            return true;
        }

        /// <summary>Indica si pasó el cooldown entre <paramref name="last"/> y <paramref name="now"/>.</summary>
        public static bool HasCooldownElapsed(float now, float last, float cooldownSeconds)
        {
            return now - last >= cooldownSeconds;
        }

        private static PlaybackRunner GetRunner()
        {
            if (s_Runner != null) return s_Runner;
            if (!Application.isPlaying) return null;

            var go = new GameObject("[HapticPlayback]") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            s_Runner = go.AddComponent<PlaybackRunner>();
            return s_Runner;
        }

        /// <summary>MonoBehaviour oculto que ejecuta los patrones multi-pulso.</summary>
        private sealed class PlaybackRunner : MonoBehaviour
        {
            private readonly Dictionary<HapticImpulsePlayer, Coroutine> m_Active = new Dictionary<HapticImpulsePlayer, Coroutine>();

            public void Play(HapticImpulsePlayer player, HapticPattern pattern, float masterAmplitude)
            {
                if (m_Active.TryGetValue(player, out Coroutine running) && running != null)
                    StopCoroutine(running);

                m_Active[player] = StartCoroutine(Run(player, pattern, masterAmplitude));
            }

            private IEnumerator Run(HapticImpulsePlayer player, HapticPattern pattern, float masterAmplitude)
            {
                int count = Mathf.Max(1, pattern.Count);
                for (int i = 0; i < count; i++)
                {
                    if (player == null) yield break;

                    float amplitude = Mathf.Clamp01(pattern.Pulse.Amplitude * masterAmplitude);
                    if (amplitude > 0f && pattern.Pulse.Duration > 0f)
                        player.SendHapticImpulse(amplitude, pattern.Pulse.Duration);

                    if (i < count - 1)
                        yield return new WaitForSecondsRealtime(pattern.Pulse.Duration + Mathf.Max(0f, pattern.Gap));
                }

                if (player != null) m_Active.Remove(player);
            }
        }
    }
}
