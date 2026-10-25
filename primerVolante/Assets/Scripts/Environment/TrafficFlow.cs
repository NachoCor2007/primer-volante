namespace PrimerVolante.Environment
{
    /// <summary>Sentido de circulación permitido en una pieza de calle.</summary>
    public enum TrafficFlow
    {
        /// <summary>Doble mano: mano derecha. El carril +X va hacia +Z local y el carril −X hacia −Z local.</summary>
        TwoWay = 0,

        /// <summary>Mano única: los dos carriles van hacia +Z local.</summary>
        OneWayPositiveZ = 1,

        /// <summary>Mano única: los dos carriles van hacia −Z local.</summary>
        OneWayNegativeZ = 2,
    }

    /// <summary>Lado (en X local de la pieza) de un carril.</summary>
    public enum LaneSide
    {
        /// <summary>Carril x &gt; 0 local.</summary>
        PositiveX = 0,

        /// <summary>Carril x &lt; 0 local.</summary>
        NegativeX = 1,
    }
}
