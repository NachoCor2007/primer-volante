using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>Resultado de comparar la velocidad del auto con el sentido permitido del carril.</summary>
    public enum WrongWayClassification
    {
        /// <summary>El auto avanza a favor del sentido permitido (ángulo chico).</summary>
        Correct = 0,

        /// <summary>Ni a favor ni en contra: doblando, maniobrando o cruzando en perpendicular.</summary>
        Neutral = 1,

        /// <summary>El auto avanza en contra del sentido permitido (ángulo grande).</summary>
        Wrong = 2,
    }

    /// <summary>
    /// Funciones puras del detector de contramano (sin escena, testeables en el Editor).
    /// </summary>
    public static class WrongWayMath
    {
        /// <summary>Valor del parámetro <c>where</c> de la infracción en una calle de doble mano.</summary>
        public const string WhereTwoWay = "por el carril contrario de una calle de doble mano";

        /// <summary>Valor del parámetro <c>where</c> de la infracción en una calle de mano única.</summary>
        public const string WhereOneWay = "en contra del sentido de una calle de mano única";

        // Por debajo de este módulo horizontal (m/s) la dirección no está definida.
        private const float k_MinPlanarSqrMagnitude = 1e-8f;

        /// <summary>
        /// Clasifica la velocidad contra el sentido permitido, proyectando ambos al plano horizontal (XZ): la
        /// componente vertical se ignora. <see cref="WrongWayClassification.Correct"/> si el ángulo es menor o
        /// igual que <paramref name="correctMaxDeg"/>, <see cref="WrongWayClassification.Wrong"/> si es mayor o
        /// igual que <paramref name="wrongMinDeg"/> y <see cref="WrongWayClassification.Neutral"/> entre los dos.
        /// Una velocidad horizontal nula, o un sentido permitido nulo, es <c>Neutral</c>.
        /// </summary>
        public static WrongWayClassification Classify(Vector3 velocity, Vector3 allowedDir, float correctMaxDeg, float wrongMinDeg)
        {
            velocity.y = 0f;
            allowedDir.y = 0f;
            if (velocity.sqrMagnitude < k_MinPlanarSqrMagnitude || allowedDir.sqrMagnitude < k_MinPlanarSqrMagnitude)
            {
                return WrongWayClassification.Neutral;
            }

            float angle = Vector3.Angle(velocity, allowedDir);
            if (angle <= correctMaxDeg) return WrongWayClassification.Correct;
            if (angle >= wrongMinDeg) return WrongWayClassification.Wrong;
            return WrongWayClassification.Neutral;
        }
    }
}
