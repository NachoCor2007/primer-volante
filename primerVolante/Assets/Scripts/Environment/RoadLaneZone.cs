using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Zona de un carril de una pieza de calle recta. Es un <see cref="BoxCollider"/> trigger en la capa
    /// <c>LaneZone</c>, un hijo por carril bajo <c>LaneZones</c> en la pieza. No tiene <c>Update</c> ni callbacks de
    /// trigger: el <see cref="WrongWayDetector"/> la encuentra con una consulta de física y le pregunta el
    /// sentido permitido, que sale del <see cref="RoadLaneDirection"/> de la pieza.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class RoadLaneZone : MonoBehaviour
    {
        /// <summary>Nombre de la capa de las zonas.</summary>
        public const string LayerName = "LaneZone";

        [Tooltip("Lado de la pieza (X local) que cubre esta zona.")]
        [SerializeField] private LaneSide m_Side = LaneSide.PositiveX;

        private RoadLaneDirection m_Direction;

        /// <summary>Lado de la pieza que cubre esta zona.</summary>
        public LaneSide Side
        {
            get => m_Side;
            set => m_Side = value;
        }

        /// <summary>El <see cref="RoadLaneDirection"/> de la pieza (se busca en los padres y se guarda).</summary>
        public RoadLaneDirection Direction
        {
            get
            {
                if (m_Direction == null) m_Direction = GetComponentInParent<RoadLaneDirection>();
                return m_Direction;
            }
        }

        /// <summary>true si la pieza es de mano única.</summary>
        public bool IsOneWay => Direction != null && Direction.IsOneWay;

        /// <summary>
        /// Sentido permitido del carril en el mundo. Sin <see cref="RoadLaneDirection"/> en los padres devuelve
        /// <see cref="Vector3.zero"/>, que el detector clasifica como neutro.
        /// </summary>
        public Vector3 AllowedWorldDirection => Direction != null ? Direction.AllowedWorldDirection(m_Side) : Vector3.zero;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0) gameObject.layer = layer;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.15f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.8f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previous;
        }
#endif
    }
}
