using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Utilidades compartidas por los componentes de audio del vehículo.
    /// </summary>
    public static class VehicleAudioUtil
    {
        /// <summary>
        /// Tiempo en segundos, tras habilitarse un componente, durante el cual se ignoran eventos
        /// (evita que suene algo al cargar la escena, p. ej. el freno de mano arranca en 1).
        /// </summary>
        public const float InitializationGraceSeconds = 0.3f;

        /// <summary>
        /// Configura una AudioSource para audio 3D de cabina: sin Doppler (el oyente viaja con el auto),
        /// spatialBlend 1, rolloff logarítmico, sin play on awake.
        /// </summary>
        public static void ConfigureSource(
            AudioSource source, bool loop, float minDistance, float maxDistance,
            AudioMixerGroup group, int priority = 128)
        {
            if (source == null) return;

            source.playOnAwake = false;
            source.loop = loop;
            source.dopplerLevel = 0f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.priority = priority;
            source.spatialize = false;

            if (group != null)
                source.outputAudioMixerGroup = group;
        }

        /// <summary>
        /// Reproduce un one-shot de forma segura ante fuente/clip nulos.
        /// </summary>
        public static void PlayOneShot(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null || clip == null || !source.isActiveAndEnabled) return;
            source.PlayOneShot(clip, volume);
        }
    }
}
