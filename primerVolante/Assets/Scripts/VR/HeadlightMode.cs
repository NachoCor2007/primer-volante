using System;
using UnityEngine.Events;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Modos discretos del sistema de luces frontales:
    /// Off: Todo apagado.
    /// Position: Luces de posición encendidas (emisión tenue delantera y trasera).
    /// LowBeam: Luces bajas (posición + faros externos + spotlights bajas).
    /// HighBeam: Luces altas de largo alcance (posición + faros externos e internos + spotlights bajas y altas).
    /// </summary>
    public enum HeadlightMode
    {
        Off = 0,
        Position = 1,
        LowBeam = 2,
        HighBeam = 3
    }

    [Serializable]
    public class HeadlightModeEvent : UnityEvent<HeadlightMode> { }
}
