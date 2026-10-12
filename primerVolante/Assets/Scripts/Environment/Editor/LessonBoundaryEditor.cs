using PrimerVolante.Environment;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Inspector simple de <see cref="LessonBoundary"/>: muestra el largo de la spline y la cantidad de vallas
    /// y ofrece el botón para reconstruirlas.
    /// </summary>
    [CustomEditor(typeof(LessonBoundary))]
    public class LessonBoundaryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var boundary = (LessonBoundary)target;
            EditorGUILayout.Space();

            if (boundary.TryComputePoses(out var poses, out float length))
            {
                EditorGUILayout.HelpBox($"Spline de {length:F1} m: {poses.Count} vallas.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("No hay una spline válida en el índice indicado.", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Reconstruir barreras"))
                {
                    boundary.RebuildBarriers();
                }
            }
        }
    }
}
