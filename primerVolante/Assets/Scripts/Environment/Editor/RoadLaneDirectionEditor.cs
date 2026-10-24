using System.Collections.Generic;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Inspector de <see cref="RoadLaneDirection"/> con edición múltiple: tres botones para dejar la pieza en doble
    /// mano o en mano única hacia +Z / −Z local. Todo con Undo; cambiar el sentido en una instancia de la escena
    /// es un override de propiedad del prefab.
    /// </summary>
    [CustomEditor(typeof(RoadLaneDirection))]
    [CanEditMultipleObjects]
    public class RoadLaneDirectionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "La regla de contramano vive acá: el cartel es solo la señal visible. Con varias piezas seleccionadas, " +
                "los botones se aplican a todas. Para marcar una calle entera con piezas rotadas distinto, usá " +
                "Tools > Primer Volante > Wrong Way > Make Selected Roads One-Way (Scene View Direction).",
                MessageType.None);

            if (GUILayout.Button("Doble mano")) Apply(TrafficFlow.TwoWay);
            if (GUILayout.Button("Mano única → +Z local")) Apply(TrafficFlow.OneWayPositiveZ);
            if (GUILayout.Button("Mano única → −Z local")) Apply(TrafficFlow.OneWayNegativeZ);
        }

        private void Apply(TrafficFlow flow)
        {
            foreach (Object t in targets)
            {
                RoadLaneDirectionTools.SetFlow((RoadLaneDirection)t, flow);
            }
        }
    }

    /// <summary>
    /// Herramientas de Editor para marcar calles de mano única: MenuItems bajo <c>Tools/Primer Volante/Wrong Way</c>
    /// y las funciones compartidas con el setup y la verificación. Nada guarda la escena por su cuenta.
    /// </summary>
    public static class RoadLaneDirectionTools
    {
        private const string k_MenuRoot = "Tools/Primer Volante/Wrong Way/";

        /// <summary>Por debajo de este |dot| la pieza queda casi perpendicular a la vista y se saltea.</summary>
        public const float AmbiguousDotThreshold = 0.5f;

        /// <summary>
        /// Elige el sentido de mano única de una pieza a partir de hacia dónde circula el tránsito (en el mundo):
        /// <c>OneWayPositiveZ</c> si <c>dot(pieceForward, travelDirection)</c> proyectado en XZ es positivo,
        /// <c>OneWayNegativeZ</c> si es negativo. <paramref name="ambiguous"/> es true si |dot| &lt; 0,5 o si alguna
        /// de las dos direcciones no tiene componente horizontal (en ese caso el menú saltea la pieza).
        /// </summary>
        public static TrafficFlow FlowFromDirection(Vector3 pieceForward, Vector3 travelDirection, out bool ambiguous)
        {
            pieceForward.y = 0f;
            travelDirection.y = 0f;

            // Una dirección vertical (vista cenital) no define un sentido en el plano: se avisa como ambigua.
            if (pieceForward.sqrMagnitude < 1e-8f || travelDirection.sqrMagnitude < 1e-8f)
            {
                ambiguous = true;
                return TrafficFlow.OneWayPositiveZ;
            }

            pieceForward.Normalize();
            travelDirection.Normalize();

            float dot = Vector3.Dot(pieceForward, travelDirection);
            ambiguous = Mathf.Abs(dot) < AmbiguousDotThreshold;
            return dot >= 0f ? TrafficFlow.OneWayPositiveZ : TrafficFlow.OneWayNegativeZ;
        }

        /// <summary>Cambia el sentido de una pieza con Undo y deja el override registrado en la instancia.</summary>
        public static void SetFlow(RoadLaneDirection direction, TrafficFlow flow)
        {
            var so = new SerializedObject(direction);
            SerializedProperty property = so.FindProperty("m_Flow");
            if (property.enumValueIndex == (int)flow) return;

            property.enumValueIndex = (int)flow;
            so.ApplyModifiedProperties();
            var scene = direction.gameObject.scene;
            if (scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ MenuItems

        [MenuItem(k_MenuRoot + "Make Selected Roads One-Way (Scene View Direction)")]
        public static void MakeSelectedOneWay()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
            {
                Debug.LogWarning("[RoadLaneDirectionTools] ⚠️ No hay una Scene view activa: mirá hacia donde va el tránsito y volvé a ejecutar.");
                return;
            }

            Vector3 viewForward = view.camera.transform.forward;
            List<RoadLaneDirection> pieces = SelectedPieces();
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[RoadLaneDirectionTools] ⚠️ Seleccioná piezas de calle con RoadLaneDirection.");
                return;
            }

            Undo.SetCurrentGroupName("Make Roads One-Way");
            int group = Undo.GetCurrentGroup();

            int changed = 0, skipped = 0;
            foreach (RoadLaneDirection piece in pieces)
            {
                TrafficFlow flow = FlowFromDirection(piece.transform.forward, viewForward, out bool ambiguous);
                if (ambiguous)
                {
                    skipped++;
                    Debug.LogWarning($"[RoadLaneDirectionTools] ⚠️ {piece.name} queda casi perpendicular a la vista (|dot| < {AmbiguousDotThreshold}): se saltea.", piece);
                    continue;
                }

                Undo.RecordObject(piece, "Make Road One-Way");
                SetFlow(piece, flow);
                changed++;
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"[RoadLaneDirectionTools] ➡️ {changed} piezas en mano única, {skipped} salteadas. Guardá la escena para conservar el cambio.");
        }

        [MenuItem(k_MenuRoot + "Make Selected Roads Two-Way")]
        public static void MakeSelectedTwoWay()
        {
            List<RoadLaneDirection> pieces = SelectedPieces();
            if (pieces.Count == 0)
            {
                Debug.LogWarning("[RoadLaneDirectionTools] ⚠️ Seleccioná piezas de calle con RoadLaneDirection.");
                return;
            }

            Undo.SetCurrentGroupName("Make Roads Two-Way");
            int group = Undo.GetCurrentGroup();
            foreach (RoadLaneDirection piece in pieces)
            {
                Undo.RecordObject(piece, "Make Road Two-Way");
                SetFlow(piece, TrafficFlow.TwoWay);
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"[RoadLaneDirectionTools] ↔️ {pieces.Count} piezas en doble mano. Guardá la escena para conservar el cambio.");
        }

        [MenuItem(k_MenuRoot + "Toggle Lane Direction Gizmos")]
        public static void ToggleGizmos()
        {
            RoadLaneDirection.ShowAllGizmos = !RoadLaneDirection.ShowAllGizmos;
            SceneView.RepaintAll();
            Debug.Log($"[RoadLaneDirectionTools] 🧭 Flechas de sentido en toda la escena: {(RoadLaneDirection.ShowAllGizmos ? "activadas" : "desactivadas")}.");
        }

        [MenuItem(k_MenuRoot + "Toggle Lane Direction Gizmos", true)]
        private static bool ToggleGizmosValidate()
        {
            Menu.SetChecked(k_MenuRoot + "Toggle Lane Direction Gizmos", RoadLaneDirection.ShowAllGizmos);
            return true;
        }

        // Piezas seleccionadas con RoadLaneDirection (en la raíz del objeto o en un padre, para tolerar seleccionar un hijo).
        private static List<RoadLaneDirection> SelectedPieces()
        {
            var result = new List<RoadLaneDirection>();
            foreach (GameObject go in Selection.gameObjects)
            {
                var direction = go.GetComponentInParent<RoadLaneDirection>();
                if (direction != null && !result.Contains(direction)) result.Add(direction);
            }

            return result;
        }
    }
}
