using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Lo que un sistema envía a <see cref="IInfractionReporter"/> cuando detecta una falta: el id de la
    /// infracción, quién la detectó, dónde ocurrió y los valores ya formateados para la plantilla de descripción.
    /// </summary>
    public readonly struct InfractionReport
    {
        /// <summary>Id estable de la infracción (ver <see cref="InfractionIds"/>).</summary>
        public readonly string InfractionId;

        /// <summary>Componente que detectó la falta. Puede ser null.</summary>
        public readonly Object Source;

        /// <summary>Posición en mundo donde ocurrió la falta.</summary>
        public readonly Vector3 Position;

        /// <summary>
        /// Valores para los marcadores <c>{clave}</c> de la plantilla de descripción, ya formateados por quien
        /// reporta (por ejemplo <c>"speed" → "52"</c>). Puede ser null o estar vacío.
        /// </summary>
        public readonly IReadOnlyDictionary<string, string> Parameters;

        public InfractionReport(string infractionId, Object source, Vector3 position,
                                IReadOnlyDictionary<string, string> parameters)
        {
            InfractionId = infractionId;
            Source = source;
            Position = position;
            Parameters = parameters;
        }

        /// <summary>
        /// Crea un reporte con los parámetros como pares clave-valor, para dejar la llamada en el detector en una
        /// o dos líneas: <c>InfractionReport.Create(id, this, pos, ("limit", "40"), ("speed", "52"))</c>.
        /// </summary>
        public static InfractionReport Create(string infractionId, Object source, Vector3 position,
                                              params (string key, string value)[] parameters)
        {
            Dictionary<string, string> map = null;
            if (parameters != null && parameters.Length > 0)
            {
                map = new Dictionary<string, string>(parameters.Length);
                foreach ((string key, string value) in parameters)
                {
                    if (!string.IsNullOrEmpty(key)) map[key] = value;
                }
            }

            return new InfractionReport(infractionId, source, position, map);
        }
    }
}
