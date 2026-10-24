using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Una infracción ya registrada. Es inmutable y la crea solo <see cref="InfractionManager"/>. Guarda lo
    /// necesario para mostrarla de dos formas: <see cref="ShortTitle"/> en el tablero (US24) y
    /// <see cref="Description"/> en el reporte de fin de lección (US26).
    /// </summary>
    public sealed class InfractionRecord
    {
        /// <summary>Número correlativo (1, 2, 3...) desde el último <c>Clear</c>.</summary>
        public int Sequence { get; }

        /// <summary>Definición del catálogo con la que se registró.</summary>
        public InfractionDefinitionSO Definition { get; }

        /// <summary>Descripción completa: la plantilla ya formateada con los parámetros del reporte.</summary>
        public string Description { get; }

        /// <summary>Copia de los parámetros del reporte.</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary><see cref="UnityEngine.Time.time"/> al registrarse.</summary>
        public float Time { get; }

        /// <summary>Segundos desde el último <c>Clear</c> o el inicio de la lección.</summary>
        public float ElapsedSinceStart { get; }

        /// <summary>Posición en mundo donde ocurrió.</summary>
        public Vector3 Position { get; }

        /// <summary>Nombre del GameObject que reportó (el nombre, no la referencia: sigue válido si se destruye).</summary>
        public string SourceName { get; }

        /// <summary>Id de la lección activa al registrarse, o null si no hay <c>LessonManager</c>.</summary>
        public string LessonId { get; }

        /// <summary>Índice del paso de la lección al registrarse, o −1 si no hay <c>LessonManager</c>.</summary>
        public int StepIndex { get; }

        /// <summary>Id estable de la infracción.</summary>
        public string Id => Definition.Id;

        /// <summary>Gravedad de la infracción.</summary>
        public InfractionSeverity Severity => Definition.Severity;

        /// <summary>Título corto para el tablero.</summary>
        public string ShortTitle => Definition.ShortTitle;

        internal InfractionRecord(int sequence, InfractionDefinitionSO definition, string description,
                                  IReadOnlyDictionary<string, string> parameters, float time, float elapsedSinceStart,
                                  Vector3 position, string sourceName, string lessonId, int stepIndex)
        {
            Sequence = sequence;
            Definition = definition;
            Description = description;
            Parameters = CopyOf(parameters);
            Time = time;
            ElapsedSinceStart = elapsedSinceStart;
            Position = position;
            SourceName = sourceName;
            LessonId = lessonId;
            StepIndex = stepIndex;
        }

        private static IReadOnlyDictionary<string, string> CopyOf(IReadOnlyDictionary<string, string> source)
        {
            var copy = new Dictionary<string, string>();
            if (source != null)
            {
                foreach (KeyValuePair<string, string> pair in source) copy[pair.Key] = pair.Value;
            }

            return copy;
        }
    }
}
