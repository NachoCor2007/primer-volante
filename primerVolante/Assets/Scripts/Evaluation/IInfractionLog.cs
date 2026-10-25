using System;
using System.Collections.Generic;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Interfaz de lectura del registro de infracciones: la usarán el tablero (US24), el filtro de visión (US23)
    /// y la pantalla de resultados (US26).
    /// </summary>
    public interface IInfractionLog
    {
        /// <summary>Infracciones registradas desde el último <c>Clear</c>, en orden.</summary>
        IReadOnlyList<InfractionRecord> Records { get; }

        /// <summary>Cantidad de faltas leves.</summary>
        int MinorCount { get; }

        /// <summary>Cantidad de faltas graves.</summary>
        int MajorCount { get; }

        /// <summary>Total de faltas (leves + graves).</summary>
        int TotalCount { get; }

        /// <summary>
        /// Se dispara por cada infracción nueva. El tablero muestra <c>ShortTitle</c>; el filtro de US23 usa
        /// <c>Severity</c>.
        /// </summary>
        event Action<InfractionRecord> InfractionRecorded;

        /// <summary>Se dispara cuando el registro se vacía.</summary>
        event Action Cleared;
    }
}
