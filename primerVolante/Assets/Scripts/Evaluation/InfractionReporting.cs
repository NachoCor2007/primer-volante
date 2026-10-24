using System.Collections.Generic;
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
        // El primero es el activo; los demás esperan en orden de registro y toman su lugar cuando se va.
        private static readonly List<IInfractionReporter> s_Registered = new List<IInfractionReporter>();
        private static readonly ConsoleInfractionReporter s_Fallback = new ConsoleInfractionReporter();

        /// <summary>El reporter registrado o, si no hay ninguno, el respaldo de consola. Nunca es null.</summary>
        public static IInfractionReporter Current => Active ?? s_Fallback;

        /// <summary>El registro de lectura del gestor registrado, o null si no hay (o si el reporter no es un log).</summary>
        public static IInfractionLog Log => Active as IInfractionLog;

        // El activo, descartando antes los componentes destruidos que no llegaron a desregistrarse.
        private static IInfractionReporter Active
        {
            get
            {
                s_Registered.RemoveAll(r => r is Object unityObject && unityObject == null);
                return s_Registered.Count > 0 ? s_Registered[0] : null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Registered.Clear();
        }

        /// <summary>
        /// Registra el reporter (normalmente el <see cref="InfractionManager"/>). Si ya hay otro activo no lo
        /// reemplaza: queda en espera y pasa a ser el activo cuando el actual se quite. Devuelve true si quedó activo.
        /// </summary>
        public static bool Register(IInfractionReporter reporter)
        {
            if (reporter == null) return false;
            if (!s_Registered.Contains(reporter)) s_Registered.Add(reporter);
            return ReferenceEquals(Active, reporter);
        }

        /// <summary>Quita el reporter, esté activo o en espera. Si era el activo, toma su lugar el siguiente en espera.</summary>
        public static void Unregister(IInfractionReporter reporter)
        {
            if (reporter != null) s_Registered.Remove(reporter);
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
