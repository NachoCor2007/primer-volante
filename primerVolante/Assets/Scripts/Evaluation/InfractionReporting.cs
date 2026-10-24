using System.Text;
using UnityEngine;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Punto de acceso estático por el que los detectores informan faltas. El <see cref="InfractionManager"/>
    /// se registra al habilitarse; si no hay ninguno, un respaldo escribe la falta en la consola, así las
    /// escenas sin gestor siguen mostrando las faltas. Los detectores no necesitan referencias en el Inspector.
    /// </summary>
    /// <remarks>
    /// <b>Agregar una infracción nueva</b> requiere solo tres pasos, sin tocar <see cref="InfractionManager"/>:
    /// <list type="number">
    /// <item>Una constante en <see cref="InfractionIds"/>: <c>public const string HardBrake = "HARD_BRAKE";</c></item>
    /// <item>Un asset <see cref="InfractionDefinitionSO"/> con ese id, título, plantilla y gravedad, agregado al
    /// <see cref="InfractionCatalogSO"/>.</item>
    /// <item>Una llamada en el sistema que la detecta:
    /// <c>InfractionReporting.Report(InfractionReport.Create(InfractionIds.HardBrake, this, transform.position, ("speed", kmh.ToString("F0"))));</c></item>
    /// </list>
    /// <see cref="Report"/> atrapa cualquier excepción del reporter: un fallo del registro nunca rompe al detector.
    /// </remarks>
    public static class InfractionReporting
    {
        private static IInfractionReporter s_Registered;
        private static readonly ConsoleInfractionReporter s_Fallback = new ConsoleInfractionReporter();

        /// <summary>El reporter registrado o, si no hay ninguno, el respaldo de consola. Nunca es null.</summary>
        public static IInfractionReporter Current => s_Registered ?? s_Fallback;

        /// <summary>El registro de lectura del gestor registrado, o null si no hay (o si el reporter no es un log).</summary>
        public static IInfractionLog Log => s_Registered as IInfractionLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Registered = null;
        }

        /// <summary>
        /// Registra el reporter (normalmente el <see cref="InfractionManager"/>). Devuelve false, sin reemplazar al
        /// actual, si ya hay otro registrado.
        /// </summary>
        public static bool Register(IInfractionReporter reporter)
        {
            if (reporter == null) return false;
            if (s_Registered != null && !ReferenceEquals(s_Registered, reporter)) return false;

            s_Registered = reporter;
            return true;
        }

        /// <summary>Quita el reporter solo si es el registrado actual.</summary>
        public static void Unregister(IInfractionReporter reporter)
        {
            if (reporter != null && ReferenceEquals(s_Registered, reporter)) s_Registered = null;
        }

        /// <summary>Atajo a <c>Current.Report(...)</c>. Atrapa y loguea cualquier excepción del reporter.</summary>
        public static void Report(in InfractionReport report)
        {
            try
            {
                Current.Report(report);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    /// <summary>
    /// Respaldo cuando no hay gestor en la escena: escribe la falta en la consola. No tiene catálogo, así que
    /// no conoce los textos.
    /// </summary>
    internal sealed class ConsoleInfractionReporter : IInfractionReporter
    {
        public void Report(in InfractionReport report)
        {
            var message = new StringBuilder("[Infraction] ⚠️ ").Append(report.InfractionId);
            if (report.Parameters != null && report.Parameters.Count > 0)
            {
                message.Append(" (");
                bool first = true;
                foreach (var pair in report.Parameters)
                {
                    if (!first) message.Append(", ");
                    message.Append(pair.Key).Append('=').Append(pair.Value);
                    first = false;
                }

                message.Append(')');
            }

            message.Append(" en ").Append(report.Position);
            Debug.LogWarning(message.ToString(), report.Source);
        }
    }
}
