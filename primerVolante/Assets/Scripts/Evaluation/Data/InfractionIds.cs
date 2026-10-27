namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Identificadores estables de las infracciones. Es la única forma en que un detector nombra una falta:
    /// con un id, nunca con una referencia al asset <see cref="InfractionDefinitionSO"/>. Cada constante debe
    /// tener su definición en el <see cref="InfractionCatalogSO"/> (la verificación de Editor lo comprueba).
    /// </summary>
    public static class InfractionIds
    {
        /// <summary>Cruce de la línea de detención con el semáforo en rojo (grave).</summary>
        public const string RedLight = "RED_LIGHT";

        /// <summary>Exceso de velocidad, una por tramo (leve). Parámetro: <c>limit</c>.</summary>
        public const string SpeedLimit = "SPEED_LIMIT";

        /// <summary>
        /// Choque con un obstáculo: cono, vehículo, peatón o valla de la lección (grave).
        /// Parámetros: <c>object</c> (ej. "un cono") y <c>speed</c>.
        /// </summary>
        public const string Crash = "CRASH";

        /// <summary>
        /// Roce con un obstáculo: cono o vehículo, a baja velocidad (leve).
        /// Parámetros: <c>object</c> (ej. "un vehículo") y <c>speed</c>.
        /// </summary>
        public const string Graze = "GRAZE";

        /// <summary>
        /// Circulación en contramano: por el carril contrario de una calle de doble mano o en contra del sentido de
        /// una calle de mano única (grave). Parámetro: <c>where</c>.
        /// </summary>
        public const string WrongWay = "WRONG_WAY";

        /// <summary>
        /// Giro sin señalizar: doblar sin guiño, con el guiño al lado contrario o sin la anticipación suficiente (leve).
        /// Parámetros: <c>direction</c> (ej. "a la derecha") y <c>reason</c> (ej. "sin poner el guiño").
        /// </summary>
        public const string TurnSignal = "TURN_SIGNAL";
    }
}
