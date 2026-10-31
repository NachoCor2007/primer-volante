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

        /// <summary>
        /// Ángulo por debajo del cual, una vez activo el desplazamiento, se vuelve a la zona muerta (histéresis):
        /// se sale al superar <see cref="DefaultDeadZoneDegrees"/> y se vuelve recién al bajar de este valor.
        /// </summary>
        public const float DefaultReleaseDegrees = 7f;
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
    /// Convierte el desplazamiento del volante en pasos enteros por frame. Regla exacta:
    /// <list type="bullet">
    /// <item>Rate 0 (zona muerta): se libera (signo y resto en 0), no da pasos y queda ARMADO.</item>
    /// <item>Desarmado (recién creado o tras <see cref="Reset"/>): no da ningún paso, aunque el volante ya esté girado,
    /// hasta que pase por la zona muerta al menos una vez. Evita el salto al entrar a una página con el volante girado.</item>
    /// <item>Armado, primer frame fuera de la zona muerta, o frame con el signo cambiado: da UN paso inmediato en esa
    /// dirección; el <c>dt</c> de ese frame no se acumula.</item>
    /// <item>Frames siguientes con el mismo signo: acumula <c>|rate| * dt</c> y devuelve la parte entera como pasos
    /// (con signo), conservando el resto fraccionario.</item>
    /// <item>Histéresis (<see cref="StepAngle"/>): ya activo, entre el ángulo de liberación (7°) y la zona muerta (10°)
    /// se mantiene el estado sin avanzar (el rate calculado es 0) ni reiniciar; solo bajo 7° se libera. Un temblor
    /// alrededor de 10° da así un único paso.</item>
    /// </list>
    /// Así, a 4 ítems/s durante 1 s (con dt exactos) dan 1 paso inmediato + 4 acumulados = 5 en total.
    /// </summary>
    public struct MenuScrollAccumulator
    {
        private int m_Sign;
        private float m_Remainder;
        private bool m_Armed;

        /// <summary>true si ya pasó por la zona muerta desde el último <see cref="Reset"/>.</summary>
        public bool Armed => m_Armed;

        /// <summary>Signo de la dirección actual (-1, 0, +1).</summary>
        public int Sign => m_Sign;

        /// <summary>Parte fraccionaria acumulada (en ítems) que todavía no completó un paso.</summary>
        public float Remainder => m_Remainder;

        /// <summary>Desarma: no habrá pasos hasta que el volante pase por la zona muerta (cambio de página, reinicio).</summary>
        public void Reset()
        {
            m_Sign = 0;
            m_Remainder = 0f;
            m_Armed = false;
        }

        /// <summary>
        /// Versión con histéresis a partir del ángulo del volante: se sale de la zona muerta al superar
        /// <paramref name="deadZoneDeg"/> y se vuelve recién al bajar de <paramref name="releaseDeg"/>.
        /// </summary>
        public int StepAngle(float wheelAngleDeg, float dt, float deadZoneDeg, float releaseDeg, float fullAngleDeg, float maxItemsPerSecond)
        {
            float abs = Mathf.Abs(wheelAngleDeg);
            if (m_Sign != 0 && abs >= releaseDeg && abs < deadZoneDeg) return 0;

            return Step(MenuScrollMath.ItemsPerSecond(wheelAngleDeg, deadZoneDeg, fullAngleDeg, maxItemsPerSecond), dt);
        }

        /// <summary>Pasos enteros a aplicar en este frame (+ = bajar, - = subir).</summary>
        public int Step(float itemsPerSecond, float dt)
        {
            if (itemsPerSecond == 0f)
            {
                m_Sign = 0;
                m_Remainder = 0f;
                m_Armed = true;
                return 0;
            }

            if (!m_Armed) return 0;

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
