using UnityEngine;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Modo de compleción de un paso de lección.
    /// ManualContinue: El conductor avanza pulsando un botón de confirmación en la UI.
    /// ActionRequired: El paso espera a que se realice una acción interactiva específica.
    /// </summary>
    public enum LessonStepCompletionMode
    {
        ManualContinue = 0,
        ActionRequired = 1
    }

    /// <summary>
    /// Tipo de acción física requerida por el conductor para avanzar de paso de forma guiada e interactiva.
    /// </summary>
    public enum LessonActionType
    {
        None = 0,               // Informativo o manual (presionar Continuar / lectura)
        OpenCalibrationMenu,    // Abrir menú de calibración (Escape o botón Menú)
        RotateSteeringWheel,    // Girar el volante hacia ambos lados (recorrido comprobado)
        AdjustMirrors,          // Ajustar espejo interior o laterales
        PressBrakePedal,        // Pisar pedal de freno a fondo (BrakeValue >= 0.8)
        StartEngine,            // Arrancar el motor (IsEngineRunning == true)
        ReleaseHandbrake,       // Bajar freno de mano (Engagement <= 0.05f)
        EngageHandbrake,        // Subir freno de mano (Engagement >= 0.95f)
        ShiftToDrive,           // Palanca en D
        ShiftToReverse,         // Palanca en R
        ShiftToPark,            // Palanca en P
        ShiftToNeutral,         // Palanca en N
        ActivateTurnSignalLeft, // Guiño izquierdo activo
        ActivateTurnSignalRight,// Guiño derecho activo
        ActivateHazardLights,   // Balizas activadas
        CycleHeadlights,        // Encender luces reglamentarias (bajas o posición)
        DriveForward,           // Avanzar en D al menos X metros
        DriveReverse            // Retroceder en R al menos X metros
    }

    /// <summary>
    /// Flags de controles de cabina permitidos durante un paso de lección.
    /// Permite habilitar o deshabilitar selectivamente interactuables del vehículo.
    /// </summary>
    [System.Flags]
    public enum CockpitControlFlags
    {
        None = 0,
        SteeringWheel = 1 << 0,
        GearShifter = 1 << 1,
        Handbrake = 1 << 2,
        StartButton = 1 << 3,
        HazardButton = 1 << 4,
        TurnSignal = 1 << 5,
        HeadlightKnob = 1 << 6,
        Mirrors = 1 << 7,
        All = ~0
    }

    /// <summary>
    /// Intensidad del ambiente sonoro que un paso de lección puede imponer (US20).
    /// Unchanged: el paso no toca la intensidad. Las demás corresponden a AmbienceIntensity.
    /// </summary>
    public enum AmbienceIntensityOverride
    {
        Unchanged = 0,
        Calm = 1,
        Normal = 2,
        Tense = 3
    }

    /// <summary>
    /// Define un paso individual dentro de una lección de manejo.
    /// </summary>
    [CreateAssetMenu(fileName = "LessonStep", menuName = "Primer Volante/Lessons/Lesson Step", order = 2)]
    public class LessonStepSO : ScriptableObject
    {
        [Tooltip("Título del paso visible en la cabecera del HUD.")]
        public string stepTitle;

        [TextArea(3, 8)]
        [Tooltip("Texto instructivo para el conductor.")]
        public string instructionText;

        [Tooltip("Modo de compleción del paso.")]
        public LessonStepCompletionMode completionMode = LessonStepCompletionMode.ManualContinue;

        [Tooltip("Acción requerida del conductor para completar el paso de forma guiada.")]
        public LessonActionType requiredAction = LessonActionType.None;

        [Tooltip("Parámetro numérico opcional para la acción (ej: metros a recorrer o umbrales de giro/freno).")]
        public float actionNumericParameter = 0f;

        [Tooltip("Flags de controles permitidos durante este paso.")]
        public CockpitControlFlags allowedControls = CockpitControlFlags.None;

        [Tooltip("Nombre o tag del interactuable a resaltar visualmente si el paso lo requiere.")]
        public string highlightTargetName;

        [Tooltip("Intensidad del ambiente sonoro (Calm, Normal o Tense) que se aplica al empezar este paso. " +
                 "Unchanged deja la intensidad como está. Escala el volumen del ambiente, la densidad de sonidos y la impaciencia de los NPC.")]
        public AmbienceIntensityOverride ambienceIntensity = AmbienceIntensityOverride.Unchanged;
    }
}
