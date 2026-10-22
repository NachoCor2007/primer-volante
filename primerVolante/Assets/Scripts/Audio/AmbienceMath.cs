using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Nivel de intensidad del ambiente (US20): escala la densidad y el volumen del ambiente y la impaciencia de los
    /// NPC. Lo controla <see cref="AmbienceDirector.SetIntensity"/> y, opcionalmente, cada paso de lección.
    /// </summary>
    public enum AmbienceIntensity
    {
        /// <summary>Ambiente tranquilo: menos densidad, más bajo y NPC pacientes.</summary>
        Calm = 0,

        /// <summary>Ambiente normal.</summary>
        Normal = 1,

        /// <summary>Ambiente tenso: más denso, más fuerte y NPC impacientes.</summary>
        Tense = 2
    }

    /// <summary>
    /// Funciones puras (sin escena ni estado) del sistema de ambiente por zonas. Son deterministas y testeables en
    /// el Editor, igual que <c>LessonBoundaryMath</c>.
    /// </summary>
    public static class AmbienceMath
    {
        /// <summary>
        /// Peso de una zona según la distancia del oyente a su caja: dentro (distancia 0 o negativa) vale 1, en el
        /// borde de la franja de fade vale 0 y entre medio baja linealmente.
        /// </summary>
        /// <param name="distanceOutside">Distancia del oyente a la caja, en metros (0 si está dentro).</param>
        /// <param name="fade">Ancho de la franja de fade, en metros.</param>
        public static float ZoneWeight(float distanceOutside, float fade)
        {
            if (distanceOutside <= 0f) return 1f;
            if (fade <= 0f) return 0f;
            return Mathf.Clamp01(1f - distanceOutside / fade);
        }

        /// <summary>
        /// Normaliza los pesos en el lugar para que su suma no pase de 1 respetando la prioridad: se reparte el cupo
        /// (1) por niveles de prioridad de mayor a menor. Un nivel entero entra si cabe; si no, se escala en
        /// proporción al cupo que queda y los niveles inferiores reciben 0. Dentro de un mismo nivel la proporción se
        /// conserva. La función es continua respecto de los pesos de entrada.
        /// </summary>
        public static void NormalizeWeights(float[] weights, int[] priorities)
        {
            if (weights == null || weights.Length == 0) return;

            int count = weights.Length;
            float remaining = 1f;
            var done = new bool[count];

            for (int pass = 0; pass < count; pass++)
            {
                // Mayor prioridad todavía sin repartir.
                int best = int.MinValue;
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    if (done[i]) continue;
                    int p = PriorityAt(priorities, i);
                    if (!found || p > best)
                    {
                        best = p;
                        found = true;
                    }
                }

                if (!found) break;

                float levelSum = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (!done[i] && PriorityAt(priorities, i) == best) levelSum += Mathf.Max(0f, weights[i]);
                }

                float scale = levelSum > remaining && levelSum > 0f ? remaining / levelSum : 1f;
                for (int i = 0; i < count; i++)
                {
                    if (done[i] || PriorityAt(priorities, i) != best) continue;
                    weights[i] = Mathf.Max(0f, weights[i]) * scale;
                    done[i] = true;
                }

                remaining = Mathf.Max(0f, remaining - levelSum * scale);
            }
        }

        private static int PriorityAt(int[] priorities, int index)
        {
            return priorities != null && index < priorities.Length ? priorities[index] : 0;
        }

        /// <summary>
        /// Acerca <paramref name="current"/> a <paramref name="target"/> con velocidad constante: recorre todo el
        /// rango 0..1 en <paramref name="fadeSeconds"/> segundos.
        /// </summary>
        public static float SmoothVolume(float current, float target, float fadeSeconds, float deltaTime)
        {
            float maxDelta = deltaTime / Mathf.Max(fadeSeconds, 1e-4f);
            return Mathf.MoveTowards(current, target, maxDelta);
        }

        /// <summary>
        /// Ganancia de potencia constante para un peso (0..1): dos fuentes no correlacionadas que cruzan suenan con
        /// el mismo nivel total en todo el recorrido.
        /// </summary>
        public static float EqualPowerGain(float weight)
        {
            return Mathf.Sqrt(Mathf.Clamp01(weight));
        }

        /// <summary>
        /// Intervalo entre disparos dispersos: el intervalo base dividido por el peso de la zona y por el multiplicador
        /// de tasa de la intensidad. Con peso o tasa nulos es infinito (no se dispara).
        /// </summary>
        public static float ScaledInterval(float baseInterval, float weight, float intensityRateMultiplier)
        {
            float rate = weight * intensityRateMultiplier;
            if (rate <= 1e-4f) return float.PositiveInfinity;
            return Mathf.Max(0f, baseInterval) / rate;
        }

        /// <summary>Convierte decibeles a ganancia lineal.</summary>
        public static float DbToLinear(float db)
        {
            return Mathf.Pow(10f, db / 20f);
        }

        /// <summary>
        /// Factor (1..0) para apagar suavemente una fuente 3D cerca de su distancia máxima: el rolloff logarítmico no
        /// llega a 0 en <c>maxDistance</c>, así que al salir de rango habría un corte. Vale 1 hasta
        /// <paramref name="startFraction"/> × maxDistance y baja linealmente a 0 en maxDistance.
        /// </summary>
        public static float EdgeFade(float distance, float maxDistance, float startFraction = 0.7f)
        {
            if (maxDistance <= 0f) return 0f;
            float start = maxDistance * Mathf.Clamp01(startFraction);
            if (distance <= start) return 1f;
            return Mathf.Clamp01(1f - (distance - start) / Mathf.Max(maxDistance - start, 1e-4f));
        }
    }
}
