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
        All = ~0
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

        [Tooltip("Flags de controles permitidos durante este paso.")]
        public CockpitControlFlags allowedControls = CockpitControlFlags.None;

        [Tooltip("Nombre o tag del interactuable a resaltar visualmente si el paso lo requiere.")]
        public string highlightTargetName;
    }
}
