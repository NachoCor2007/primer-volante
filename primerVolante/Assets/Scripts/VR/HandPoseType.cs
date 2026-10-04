namespace PrimerVolante.VR
{
    /// <summary>
    /// Tipos de posturas contextuales para las manos del conductor en Realidad Virtual.
    /// </summary>
    public enum HandPoseType
    {
        Default = 0,       // Postura natural de reposo/conducción con dedos semi-relajados
        SteeringWheel = 1, // Agarre cerrado y firme envolviendo el aro del volante
        KnobPinch = 2,     // Agarre de pinza (pulgar e índice) para la perilla de luces
        GearShifter = 3,   // Agarre con la palma apoyada sobre la perilla de cambios
        Handbrake = 4,     // Empuñadura envolvente de puño cerrado sobre el freno de mano
        RearviewMirror = 5 // Sujeción lateral para orientar el espejo retrovisor
    }
}
