using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Zona de exención del control de contramano (por ejemplo, el área de la práctica con conos). Es un
    /// <see cref="BoxCollider"/> trigger en la capa <c>LaneZone</c>: mientras el <b>punto de consulta</b> del
    /// <see cref="WrongWayDetector"/> (no la carrocería) está adentro, el detector no evalúa y descarta el
    /// episodio en curso. Se coloca a mano alrededor del área. Hoy ninguna escena de la ciudad tiene una.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WrongWayExemptZone : MonoBehaviour
    {
        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (layer >= 0) gameObject.layer = layer;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.18f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;

            UnityEditor.Handles.Label(transform.TransformPoint(box.center + Vector3.up * (box.size.y * 0.5f)), "Sin control de contramano");
        }
#endif
    }
}
