using UnityEngine;

namespace PrimerVolante.MainMenu
{
    /// <summary>
    /// Matemática pura del desplazamiento del cursor con el volante: convierte el ángulo del volante en ítems por
    /// segundo. Zona muerta |ángulo| menor a <see cref="DefaultDeadZoneDegrees"/> (0 ítems/s); lineal desde la zona
    /// muerta hasta <see cref="DefaultFullAngleDegrees"/> (<see cref="DefaultMaxItemsPerSecond"/> ítems/s) y saturado
    /// más allá (el volante igual puede girar hasta 450°). Signo: derecha (+) = bajar (índice +1), izquierda = subir.
    /// </summary>
    public static class MenuScrollMath
    {
        public const float DefaultDeadZoneDegrees = 10f;
        public const float DefaultFullAngleDegrees = 90f;
        public const float DefaultMaxItemsPerSecond = 4f;

        /// <summary>Ítems por segundo con signo (+ = bajar) para un ángulo de volante, con los valores por defecto.</summary>
        public static float ItemsPerSecond(float wheelAngleDeg)
        {
            return ItemsPerSecond(wheelAngleDeg, DefaultDeadZoneDegrees, DefaultFullAngleDegrees, DefaultMaxItemsPerSecond);
        }

        public static float ItemsPerSecond(float wheelAngleDeg, float deadZoneDeg, float fullAngleDeg, float maxItemsPerSecond)
        {
            float abs = Mathf.Abs(wheelAngleDeg);
            if (abs < deadZoneDeg) return 0f;

            float span = Mathf.Max(0.0001f, fullAngleDeg - deadZoneDeg);
            float t = Mathf.Clamp01((abs - deadZoneDeg) / span);
            return Mathf.Sign(wheelAngleDeg) * t * maxItemsPerSecond;
        }
    }

    /// <summary>
    /// Convierte un rate en ítems/s en pasos enteros por frame. Regla exacta:
    /// <list type="bullet">
    /// <item>Rate 0 (zona muerta): se reinicia (signo y resto) y no da pasos.</item>
    /// <item>Primer frame fuera de la zona muerta, o frame con el signo cambiado: se reinicia y da UN paso inmediato
    /// en esa dirección; el <c>dt</c> de ese frame no se acumula.</item>
    /// <item>Frames siguientes con el mismo signo: acumula <c>|rate| * dt</c> y devuelve la parte entera como pasos
    /// (con signo), conservando el resto fraccionario.</item>
    /// </list>
    /// Así, a 4 ítems/s durante 1 s (con dt exactos) dan 1 paso inmediato + 4 acumulados = 5 en total.
    /// </summary>
    public struct MenuScrollAccumulator
    {
        private int m_Sign;
        private float m_Remainder;

        /// <summary>Signo de la dirección actual (-1, 0, +1).</summary>
        public int Sign => m_Sign;

        /// <summary>Parte fraccionaria acumulada (en ítems) que todavía no completó un paso.</summary>
        public float Remainder => m_Remainder;

        public void Reset()
        {
            m_Sign = 0;
            m_Remainder = 0f;
        }

        /// <summary>Pasos enteros a aplicar en este frame (+ = bajar, - = subir).</summary>
        public int Step(float itemsPerSecond, float dt)
        {
            if (itemsPerSecond == 0f)
            {
                Reset();
                return 0;
            }

            int sign = itemsPerSecond > 0f ? 1 : -1;
            if (sign != m_Sign)
            {
                m_Sign = sign;
                m_Remainder = 0f;
                return sign;
            }

            m_Remainder += Mathf.Abs(itemsPerSecond) * dt;
            int whole = Mathf.FloorToInt(m_Remainder);
            m_Remainder -= whole;
            return sign * whole;
        }
    }
}
