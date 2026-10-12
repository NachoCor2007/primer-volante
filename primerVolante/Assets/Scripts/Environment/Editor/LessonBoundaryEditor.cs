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
                float width = poses.Count > 0 ? boundary.BarrierWidth * poses[0].LengthScale : 0f;
                float gap = poses.Count > 1 ? poses[1].StartDistance - poses[0].EndDistance : 0f;
                EditorGUILayout.HelpBox(
                    $"Spline de {length:F1} m: {poses.Count} vallas de {width:F2} m (natural {boundary.BarrierWidth:F2} m), " +
                    $"separadas {gap:F2} m.", MessageType.Info);
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
