using System.Collections.Generic;
using System.Text;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Función pura (sin escena ni estado) que completa las plantillas de descripción. Es determinista y
    /// testeable en el Editor, igual que <c>LessonBoundaryMath</c>.
    /// </summary>
    public static class InfractionTextFormatter
    {
        /// <summary>
        /// Reemplaza cada <c>{clave}</c> de la plantilla por su valor. Un marcador sin valor queda tal cual (así
        /// se nota en el reporte y en la verificación). Nunca lanza: plantilla null devuelve "" y parámetros null
        /// equivale a un diccionario vacío.
        /// </summary>
        public static string Format(string template, IReadOnlyDictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (parameters == null || parameters.Count == 0) return template;

            var result = new StringBuilder(template.Length + 16);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{')
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close > i + 1)
                    {
                        string key = template.Substring(i + 1, close - i - 1);
                        if (parameters.TryGetValue(key, out string value) && value != null)
                        {
                            result.Append(value);
                            i = close + 1;
                            continue;
                        }
                    }
                }

                result.Append(c);
                i++;
            }

            return result.ToString();
        }
    }
}
