using PrimerVolante.VR;

namespace PrimerVolante.Environment
{
    /// <summary>Motivo por el que un giro se considera mal señalizado.</summary>
    public enum TurnSignalFaultReason
    {
        /// <summary>Doblar sin guiño: estaba apagado al empezar el giro y nunca correcto dentro de la ventana.</summary>
        NoSignal = 0,

        /// <summary>El guiño estaba puesto hacia el lado contrario al empezar el giro.</summary>
        WrongSide = 1,

        /// <summary>El guiño correcto no cubrió toda la ventana de anticipación (puesto tarde o con cortes).</summary>
        TooLate = 2,
    }

    /// <summary>Qué pasó con el último giro detectado por el <see cref="TurnSignalTracker"/>.</summary>
    public enum TurnSignalOutcome
    {
        /// <summary>Todavía no se detectó ningún giro (o se descartó con un reset).</summary>
        None = 0,

        /// <summary>El giro estaba bien señalizado.</summary>
        Signaled = 1,

        /// <summary>El giro tuvo falta.</summary>
        Fault = 2,

        /// <summary>El giro se hizo en una zona exenta (<see cref="TurnSignalExemptZone"/>) y se consumió sin falta.</summary>
        Exempt = 3,
    }

    /// <summary>Resultado de una falta de giro: hacia dónde dobló y por qué se considera mal señalizado.</summary>
    public readonly struct TurnSignalFault
    {
        /// <summary>Dirección del giro: <see cref="TurnSignalState.Left"/> o <see cref="TurnSignalState.Right"/>.</summary>
        public readonly TurnSignalState Direction;

        /// <summary>Motivo de la falta.</summary>
        public readonly TurnSignalFaultReason Reason;

        public TurnSignalFault(TurnSignalState direction, TurnSignalFaultReason reason)
        {
            Direction = direction;
            Reason = reason;
        }

        /// <summary>Texto del parámetro <c>direction</c> de la infracción.</summary>
        public string DirectionText => TurnSignalMath.DirectionText(Direction);

        /// <summary>Texto del parámetro <c>reason</c> de la infracción.</summary>
        public string ReasonText => TurnSignalMath.ReasonText(Reason);
    }

    /// <summary>
    /// Textos y constantes de la infracción <c>TURN_SIGNAL</c> (sin escena, testeables en el Editor).
    /// </summary>
    public static class TurnSignalMath
    {
        /// <summary>Valor del parámetro <c>direction</c> para un giro a la derecha.</summary>
        public const string DirectionRight = "a la derecha";

        /// <summary>Valor del parámetro <c>direction</c> para un giro a la izquierda.</summary>
        public const string DirectionLeft = "a la izquierda";

        /// <summary>Valor del parámetro <c>reason</c>: el guiño no estaba puesto.</summary>
        public const string ReasonNoSignal = "sin poner el guiño";

        /// <summary>Valor del parámetro <c>reason</c>: el guiño estaba hacia el lado contrario.</summary>
        public const string ReasonWrongSide = "con el guiño hacia el lado contrario";

        /// <summary>Valor del parámetro <c>reason</c>: el guiño no se puso con suficiente anticipación.</summary>
        public const string ReasonTooLate = "sin señalizar con suficiente anticipación";

        /// <summary>Texto de la dirección del giro. <see cref="TurnSignalState.Off"/> no es una dirección: devuelve vacío.</summary>
        public static string DirectionText(TurnSignalState direction)
        {
            switch (direction)
            {
                case TurnSignalState.Right: return DirectionRight;
                case TurnSignalState.Left: return DirectionLeft;
                default: return string.Empty;
            }
        }

        /// <summary>Texto del motivo de la falta.</summary>
        public static string ReasonText(TurnSignalFaultReason reason)
        {
            switch (reason)
            {
                case TurnSignalFaultReason.NoSignal: return ReasonNoSignal;
                case TurnSignalFaultReason.WrongSide: return ReasonWrongSide;
                default: return ReasonTooLate;
            }
        }
    }
}
