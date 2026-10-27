using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Zona de exención del control de giro sin señalizar (<c>TURN_SIGNAL</c>). Es un <see cref="BoxCollider"/>
    /// trigger en la capa <c>LaneZone</c>: mientras el <b>punto de consulta</b> del <see cref="TurnSignalDetector"/>
    /// (no la carrocería) está adentro, un giro que empieza o llega a la detección ahí no da falta. Va en las
    /// piezas donde no hay otra opción que doblar o volver: las curvas (<c>Road Corner_01</c>) y las puntas de calle
    /// sin salida (<c>Road Lane_04</c>). No afecta al <see cref="WrongWayDetector"/>, que solo mira
    /// <see cref="WrongWayExemptZone"/> y <see cref="RoadLaneZone"/>.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TurnSignalExemptZone : MonoBehaviour
    {
        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (layer >= 0) gameObject.layer = layer;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.15f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;

            UnityEditor.Handles.Label(transform.TransformPoint(box.center + Vector3.up * (box.size.y * 0.5f)), "Sin control de guiño");
        }
#endif
    }
}
