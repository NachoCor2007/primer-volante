using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Funciones puras (sin escena ni estado) del audio de los NPC y de las bocinas de impaciencia (US20).
    /// </summary>
    public static class NPCAudioMath
    {
        /// <summary>Demora de bocina de un NPC bloqueado por defecto: Calm, Normal y Tense (segundos).</summary>
        public static readonly float[] DefaultNpcHonkDelays = { 8f, 5f, 2.5f };

        /// <summary>
        /// Demora de bocina del jugador que no arranca en verde por defecto: Calm, Normal y Tense (segundos).
        /// Un valor menor o igual a 0 desactiva la bocina.
        /// </summary>
        public static readonly float[] DefaultGreenHonkDelays = { 0f, 4f, 2f };

        /// <summary>
        /// Mezcla entre el loop de ralentí (0) y el de régimen medio (1) según la velocidad.
        /// </summary>
        public static float EngineBlend(float speed, float referenceSpeed)
        {
            if (referenceSpeed <= 0f) return 0f;
            return Mathf.Clamp01(speed / referenceSpeed);
        }

        /// <summary>
        /// Pitch del motor: va de <paramref name="minPitch"/> a <paramref name="maxPitch"/> con la velocidad. Es
        /// monótona y queda siempre dentro del rango.
        /// </summary>
        public static float EnginePitch(float speed, float referenceSpeed, float minPitch, float maxPitch)
        {
            float lo = Mathf.Min(minPitch, maxPitch);
            float hi = Mathf.Max(minPitch, maxPitch);
            return Mathf.Lerp(lo, hi, EngineBlend(speed, referenceSpeed));
        }

        /// <summary>
        /// Segundos entre dos pasos: largo de paso / velocidad, con un tope inferior para que no sea una ametralladora.
        /// Con velocidad (casi) nula no hay pasos y devuelve infinito.
        /// </summary>
        public static float FootstepInterval(float speed, float strideLength, float minInterval = 0.2f)
        {
            if (speed <= 0.05f || strideLength <= 0f) return float.PositiveInfinity;
            return Mathf.Max(minInterval, strideLength / speed);
        }

        /// <summary>
        /// true si hay que tocar la bocina de impaciencia: la demora está activa (&gt; 0), el semáforo lleva en verde al
        /// menos <paramref name="delay"/> segundos y el jugador está (casi) quieto.
        /// </summary>
        public static bool ShouldImpatientHonk(float greenElapsed, float speedKmh, float delay, float maxSpeedKmh)
        {
            if (delay <= 0f) return false;
            if (speedKmh >= maxSpeedKmh) return false;
            return greenElapsed >= delay;
        }

        /// <summary>true si un NPC bloqueado ya debe tocar la bocina (demora &gt; 0 y tiempo bloqueado cumplido).</summary>
        public static bool ShouldBlockedHonk(float blockedSeconds, float delay)
        {
            return delay > 0f && blockedSeconds >= delay;
        }
    }
}
