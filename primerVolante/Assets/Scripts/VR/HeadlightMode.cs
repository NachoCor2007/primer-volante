using System;
using UnityEngine.Events;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Modos discretos del sistema de luces frontales:
    /// Off: Todo apagado (0°).
    /// LowBeam: Luces bajas / posición (45°).
    /// HighBeam: Luces altas de largo alcance (90°).
    /// </summary>
    public enum HeadlightMode
    {
        Off = 0,
        LowBeam = 1,
        HighBeam = 2
    }

    [Serializable]
    public class HeadlightModeEvent : UnityEvent<HeadlightMode> { }
}
