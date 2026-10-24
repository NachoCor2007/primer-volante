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

        /// <summary>Exceso de velocidad, una por tramo (leve).</summary>
        public const string Speeding = "SPEEDING";

        /// <summary>Alcanzar las vallas de la lección (grave).</summary>
        public const string LessonBoundary = "LESSON_BOUNDARY";

        /// <summary>Roce con un cono (leve).</summary>
        public const string ConeGraze = "CONE_GRAZE";

        /// <summary>Choque con un cono (grave).</summary>
        public const string ConeCrash = "CONE_CRASH";

        /// <summary>Roce con un vehículo, NPC o estacionado (leve).</summary>
        public const string VehicleGraze = "VEHICLE_GRAZE";

        /// <summary>Choque con un vehículo (grave).</summary>
        public const string VehicleCrash = "VEHICLE_CRASH";

        /// <summary>Atropello de un peatón (grave).</summary>
        public const string PedestrianHit = "PEDESTRIAN_HIT";
    }
}
