using System;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Categoría háptica de un control de cabina. Define qué firma de alcance/agarre usa
    /// (ver <see cref="CabinHapticProfile"/>).
    /// </summary>
    public enum CabinHapticCategory
    {
        /// <summary>Palancas: guiño, cambios y freno de mano.</summary>
        Levers = 0,

        /// <summary>Volante.</summary>
        Wheel = 1,

        /// <summary>Botones y perillas: arranque, balizas, perilla de luces, espejos.</summary>
        ButtonsAndKnobs = 2
    }

    /// <summary>
    /// Un impulso háptico: amplitud (0–1) y duración en segundos. La frecuencia no se configura
    /// (los Touch Plus no la respetan de forma confiable vía OpenXR): las firmas se distinguen por patrón.
    /// </summary>
    [Serializable]
    public class HapticPulse
    {
        [Tooltip("Amplitud del impulso (0–1).")]
        [Range(0f, 1f)] public float Amplitude = 0.3f;

        [Tooltip("Duración del impulso en segundos.")]
        [Min(0f)] public float Duration = 0.03f;

        public HapticPulse() { }

        public HapticPulse(float amplitude, float duration)
        {
            Amplitude = amplitude;
            Duration = duration;
        }
    }

    /// <summary>
    /// Patrón háptico: un impulso repetido <see cref="Count"/> veces con una separación entre pulsos.
    /// </summary>
    [Serializable]
    public class HapticPattern
    {
        [Tooltip("Impulso base del patrón.")]
        public HapticPulse Pulse = new HapticPulse();

        [Tooltip("Cantidad de veces que se repite el impulso.")]
        [Min(1)] public int Count = 1;

        [Tooltip("Separación en segundos entre el final de un pulso y el inicio del siguiente.")]
        [Min(0f)] public float Gap = 0f;

        public HapticPattern() { }

        public HapticPattern(float amplitude, float duration, int count = 1, float gap = 0f)
        {
            Pulse = new HapticPulse(amplitude, duration);
            Count = count;
            Gap = gap;
        }

        /// <summary>Duración total del patrón en segundos (pulsos más separaciones).</summary>
        public float TotalDuration => Pulse == null
            ? 0f
            : Mathf.Max(1, Count) * Pulse.Duration + Mathf.Max(0, Count - 1) * Gap;
    }

    /// <summary>
    /// Firma háptica de una categoría de controles: patrón al acercar la mano (hover) y al agarrar (select).
    /// </summary>
    [Serializable]
    public class ControlHaptics
    {
        [Tooltip("Al alcanzar el control (hover real, con la mano a pocos centímetros).")]
        public HapticPattern Reach = new HapticPattern();

        [Tooltip("Al agarrar o pulsar el control (select).")]
        public HapticPattern Grab = new HapticPattern();

        public ControlHaptics() { }

        public ControlHaptics(HapticPattern reach, HapticPattern grab)
        {
            Reach = reach;
            Grab = grab;
        }
    }

    /// <summary>
    /// Confirmaciones hápticas de los eventos del vehículo, enviadas solo a la mano que opera el control.
    /// </summary>
    [Serializable]
    public class ConfirmationHaptics
    {
        [Tooltip("Cambio de marcha.")]
        public HapticPattern GearChange = new HapticPattern(0.6f, 0.06f);

        [Tooltip("Clic del guiño (solo con la leva agarrada; la auto-cancelación no vibra).")]
        public HapticPattern TurnSignalClick = new HapticPattern(0.4f, 0.025f);

        [Tooltip("Cada posición (detent) de la perilla de luces, mientras está agarrada.")]
        public HapticPattern HeadlightDetent = new HapticPattern(0.3f, 0.02f);

        [Tooltip("Cada escalón del trinquete del freno de mano al subir.")]
        public HapticPattern HandbrakeRatchet = new HapticPattern(0.25f, 0.015f);

        [Tooltip("Freno de mano liberado por completo.")]
        public HapticPattern HandbrakeRelease = new HapticPattern(0.4f, 0.08f);

        [Tooltip("Arranque (o apagado) aceptado.")]
        public HapticPattern EngineStartAccepted = new HapticPattern(0.5f, 0.05f);

        [Tooltip("Arranque (o apagado) rechazado. Reemplaza al patrón de aceptado.")]
        public HapticPattern EngineStartRejected = new HapticPattern(0.4f, 0.04f, 3, 0.05f);

        [Tooltip("Balizas activadas o desactivadas.")]
        public HapticPattern HazardToggle = new HapticPattern(0.4f, 0.025f);
    }

    /// <summary>
    /// Hápticos de la UI en world space (más tenues que los de la cabina).
    /// </summary>
    [Serializable]
    public class UIHaptics
    {
        [Tooltip("El puntero entra a un Selectable interactuable.")]
        public HapticPattern Hover = new HapticPattern(0.15f, 0.015f);

        [Tooltip("Click sobre un Button.")]
        public HapticPattern Click = new HapticPattern(0.3f, 0.03f);

        [Tooltip("Slider: cada vez que cruza un múltiplo del step.")]
        public HapticPattern SliderTick = new HapticPattern(0.12f, 0.01f);

        [Tooltip("Slider: pasa por 0 (posición original del asiento).")]
        public HapticPattern SliderZero = new HapticPattern(0.4f, 0.04f);

        [Tooltip("Slider: llega a su valor mínimo o máximo.")]
        public HapticPattern SliderLimit = new HapticPattern(0.5f, 0.06f);
    }

    /// <summary>
    /// Perfil de vibraciones hápticas del habitáculo y la UI. Es solo data de configuración:
    /// los valores son de partida y se ajustan en el visor.
    /// </summary>
    [CreateAssetMenu(fileName = "CabinHapticProfile", menuName = "Primer Volante/Cabin Haptic Profile")]
    public class CabinHapticProfile : ScriptableObject
    {
        [Header("Globales")]
        [Tooltip("Multiplicador maestro de amplitud aplicado a todos los patrones.")]
        [Min(0f)] public float MasterAmplitude = 1f;

        [Tooltip("Tiempo mínimo en segundos entre hápticos de alcance en una misma mano.")]
        [Min(0f)] public float ReachCooldownSeconds = 0.25f;

        [Tooltip("Distancia máxima en metros entre el attach de la mano y el collider más cercano para contar como 'alcance real'. " +
                 "Evita que el rayo del menú dispare háptica de alcance desde lejos.")]
        [Min(0f)] public float MaxReachDistance = 0.12f;

        [Tooltip("Paso del slider (en metros) con el que se emite un tick. 0.01 = 1 cm.")]
        [Min(0.0001f)] public float SliderStep = 0.01f;

        [Header("Categorías de controles")]
        [Tooltip("Palancas: guiño, cambios, freno de mano.")]
        public ControlHaptics Levers = new ControlHaptics(
            new HapticPattern(0.35f, 0.04f, 2, 0.06f),
            new HapticPattern(0.5f, 0.05f));

        [Tooltip("Volante.")]
        public ControlHaptics Wheel = new ControlHaptics(
            new HapticPattern(0.2f, 0.12f),
            new HapticPattern(0.4f, 0.08f));

        [Tooltip("Botones y perillas: arranque, balizas, perilla de luces, espejo retrovisor y panel de espejos laterales.")]
        public ControlHaptics ButtonsAndKnobs = new ControlHaptics(
            new HapticPattern(0.3f, 0.03f),
            new HapticPattern(0.45f, 0.04f));

        [Header("Confirmaciones")]
        public ConfirmationHaptics Confirmations = new ConfirmationHaptics();

        [Header("UI")]
        public UIHaptics UI = new UIHaptics();

        /// <summary>Devuelve la firma (alcance + agarre) de una categoría.</summary>
        public ControlHaptics GetControl(CabinHapticCategory category)
        {
            switch (category)
            {
                case CabinHapticCategory.Wheel: return Wheel;
                case CabinHapticCategory.ButtonsAndKnobs: return ButtonsAndKnobs;
                default: return Levers;
            }
        }
    }
}
