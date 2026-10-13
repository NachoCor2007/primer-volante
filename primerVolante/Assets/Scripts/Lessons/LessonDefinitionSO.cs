using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Definición data-driven de una lección de manejo completa.
    /// Contiene la lista ordenada de pasos que el conductor debe completar.
    /// </summary>
    [CreateAssetMenu(fileName = "LessonDefinition", menuName = "Primer Volante/Lessons/Lesson Definition", order = 1)]
    public class LessonDefinitionSO : ScriptableObject
    {
        [Tooltip("Identificador único de la lección (ej. LESSON_01).")]
        public string lessonId;

        [Tooltip("Título general de la lección (ej. Lección 1: Calibración del Ambiente de Manejo).")]
        public string lessonTitle;

        [Tooltip("Lista ordenada de pasos que componen la lección.")]
        public List<LessonStepSO> steps = new List<LessonStepSO>();
    }
}
