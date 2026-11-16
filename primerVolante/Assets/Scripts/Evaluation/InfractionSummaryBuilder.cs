using System.Collections.Generic;
using System.Text;

namespace PrimerVolante.Evaluation
{
    /// <summary>Una descripción distinta dentro de un tipo de falta, con las veces que se repitió idéntica.</summary>
    public sealed class InfractionSummaryDetail
    {
        public string Description { get; }
        public int Count { get; internal set; }

        internal InfractionSummaryDetail(string description)
        {
            Description = description;
            Count = 1;
        }
    }

    /// <summary>Todas las faltas de un mismo tipo (Id) en el resumen de fin de circuito.</summary>
    public sealed class InfractionSummaryGroup
    {
        public InfractionSeverity Severity { get; }
        public string Id { get; }
        public string ShortTitle { get; }

        /// <summary>Ocurrencias del tipo (suma de los <c>Count</c> de sus detalles).</summary>
        public int Count { get; internal set; }

        /// <summary>Descripciones distintas del tipo, por primera aparición.</summary>
        public IReadOnlyList<InfractionSummaryDetail> Details => m_Details;

        private readonly List<InfractionSummaryDetail> m_Details = new List<InfractionSummaryDetail>();

        internal InfractionSummaryGroup(InfractionSeverity severity, string id, string shortTitle)
        {
            Severity = severity;
            Id = id;
            ShortTitle = shortTitle;
        }

        internal void Add(string description)
        {
            Count++;
            foreach (InfractionSummaryDetail detail in m_Details)
            {
                if (detail.Description == description)
                {
                    detail.Count++;
                    return;
                }
            }

            m_Details.Add(new InfractionSummaryDetail(description));
        }
    }

    /// <summary>Resultado de <see cref="InfractionSummaryBuilder.Build"/>: grupos ordenados y contadores.</summary>
    public sealed class InfractionSummary
    {
        /// <summary>Primero los tipos graves y después los leves; dentro de cada gravedad, por primera aparición.</summary>
        public IReadOnlyList<InfractionSummaryGroup> Groups { get; }

        /// <summary>Cantidad de faltas graves (ocurrencias, no tipos).</summary>
        public int MajorCount { get; }

        /// <summary>Cantidad de faltas leves (ocurrencias, no tipos).</summary>
        public int MinorCount { get; }

        public int TotalCount => MajorCount + MinorCount;

        internal InfractionSummary(IReadOnlyList<InfractionSummaryGroup> groups, int majorCount, int minorCount)
        {
            Groups = groups;
            MajorCount = majorCount;
            MinorCount = minorCount;
        }
    }

    /// <summary>
    /// Función pura (sin escena ni estado) que agrupa las infracciones registradas para el resumen de fin de
    /// circuito y arma su texto. Es determinista y testeable en el Editor, igual que <see cref="InfractionTextFormatter"/>.
    /// </summary>
    public static class InfractionSummaryBuilder
    {
        public const string MAJOR_COLOR = "#FF3B30";
        public const string MINOR_COLOR = "#FFB020";
        public const string NO_INFRACTIONS_TEXT = "No cometiste faltas. ¡Muy buen manejo!";

        /// <summary>
        /// Agrupa los registros por tipo (Id). Las descripciones idénticas dentro de un tipo se colapsan en un detalle
        /// con su cantidad. Lista null equivale a vacía. Nunca lanza.
        /// </summary>
        public static InfractionSummary Build(IReadOnlyList<InfractionRecord> records)
        {
            var groups = new List<InfractionSummaryGroup>();
            int major = 0, minor = 0;

            if (records != null)
            {
                // Los registros ya vienen en orden de Sequence, pero no se asume: se ordena una copia para fijar
                // "primera aparición" sin depender de quien llama (OrderBy es estable).
                var ordered = new List<InfractionRecord>(records.Count);
                foreach (InfractionRecord record in records)
                {
                    if (record != null) ordered.Add(record);
                }
                ordered = new List<InfractionRecord>(System.Linq.Enumerable.OrderBy(ordered, r => r.Sequence));

                var byId = new Dictionary<string, InfractionSummaryGroup>();
                foreach (InfractionRecord record in ordered)
                {
                    string id = record.Id ?? string.Empty;
                    if (!byId.TryGetValue(id, out InfractionSummaryGroup group))
                    {
                        group = new InfractionSummaryGroup(record.Severity, id, record.ShortTitle);
                        byId.Add(id, group);
                        groups.Add(group);
                    }

                    group.Add(record.Description ?? string.Empty);
                    if (record.Severity == InfractionSeverity.Major) major++;
                    else minor++;
                }
            }

            // Graves primero, conservando el orden de primera aparición dentro de cada gravedad.
            var sorted = new List<InfractionSummaryGroup>(groups.Count);
            foreach (InfractionSummaryGroup group in groups)
            {
                if (group.Severity == InfractionSeverity.Major) sorted.Add(group);
            }
            foreach (InfractionSummaryGroup group in groups)
            {
                if (group.Severity != InfractionSeverity.Major) sorted.Add(group);
            }

            return new InfractionSummary(sorted, major, minor);
        }

        /// <summary>Línea de conteos del encabezado, ej. "Graves: 1 · Leves: 2".</summary>
        public static string BuildCountsText(InfractionSummary summary)
        {
            int major = summary != null ? summary.MajorCount : 0;
            int minor = summary != null ? summary.MinorCount : 0;
            return $"Graves: {major} · Leves: {minor}";
        }

        /// <summary>
        /// Texto rich-text de la lista. <paramref name="timesSymbol"/> es el signo de repetición ("×", o "x" si la
        /// fuente no tiene el glifo). Sin faltas devuelve <see cref="NO_INFRACTIONS_TEXT"/>.
        /// </summary>
        public static string BuildText(InfractionSummary summary, string timesSymbol = "×")
        {
            if (summary == null || summary.Groups.Count == 0) return NO_INFRACTIONS_TEXT;

            var text = new StringBuilder();
            AppendSection(text, summary, InfractionSeverity.Major, "Faltas graves", MAJOR_COLOR, summary.MajorCount, timesSymbol);
            AppendSection(text, summary, InfractionSeverity.Minor, "Faltas leves", MINOR_COLOR, summary.MinorCount, timesSymbol);
            return text.ToString().TrimEnd('\n');
        }

        private static void AppendSection(StringBuilder text, InfractionSummary summary, InfractionSeverity severity,
                                          string heading, string color, int count, string timesSymbol)
        {
            if (count == 0) return;

            if (text.Length > 0) text.Append('\n');
            text.Append("<color=").Append(color).Append("><b>").Append(heading).Append(" (").Append(count).Append(")</b></color>\n");

            foreach (InfractionSummaryGroup group in summary.Groups)
            {
                if (group.Severity != severity) continue;

                text.Append("<b>").Append(group.ShortTitle).Append("</b>");
                if (group.Count > 1) text.Append(' ').Append(timesSymbol).Append(group.Count);
                text.Append('\n');

                foreach (InfractionSummaryDetail detail in group.Details)
                {
                    text.Append("• ").Append(detail.Description);
                    if (detail.Count > 1) text.Append(" (").Append(timesSymbol).Append(detail.Count).Append(')');
                    text.Append('\n');
                }
            }
        }
    }
}
