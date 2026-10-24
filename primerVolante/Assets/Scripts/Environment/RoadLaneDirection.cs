using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Sentido de circulación de una pieza de calle recta. Va en la raíz de cada prefab de calle con
    /// <see cref="RoadLaneZone"/>s. La regla de contramano vive acá y no en ningún cartel: para hacer una calle
    /// de mano única se cambia <c>Flow</c> en la instancia de la escena (un override de propiedad, sin agregar
    /// componentes).
    /// </summary>
    /// <remarks>
    /// Convención de la pieza, en su espacio local (+Z adelante, +X derecha): con <see cref="TrafficFlow.TwoWay"/>
    /// el carril +X es la mano de quien va hacia +Z y el carril −X la de quien va hacia −Z (mano derecha).
    /// </remarks>
    [DisallowMultipleComponent]
    public class RoadLaneDirection : MonoBehaviour
    {
        [Header("Sentido de circulación")]
        [Tooltip("Doble mano (mano derecha) o mano única hacia +Z / −Z local de la pieza. Es la regla que usa el detector de contramano.")]
        [SerializeField] private TrafficFlow m_Flow = TrafficFlow.TwoWay;

        /// <summary>Sentido de circulación de la pieza.</summary>
        public TrafficFlow Flow
        {
            get => m_Flow;
            set => m_Flow = value;
        }

        /// <summary>true si los dos carriles permiten el mismo sentido.</summary>
        public bool IsOneWay => m_Flow != TrafficFlow.TwoWay;

        /// <summary>Sentido permitido del carril <paramref name="side"/> en el espacio local de la pieza.</summary>
        public Vector3 AllowedLocalDirection(LaneSide side)
        {
            switch (m_Flow)
            {
                case TrafficFlow.OneWayPositiveZ:
                    return Vector3.forward;
                case TrafficFlow.OneWayNegativeZ:
                    return Vector3.back;
                default:
                    return side == LaneSide.PositiveX ? Vector3.forward : Vector3.back;
            }
        }

        /// <summary>Sentido permitido del carril <paramref name="side"/> en el espacio del mundo.</summary>
        public Vector3 AllowedWorldDirection(LaneSide side)
        {
            return transform.TransformDirection(AllowedLocalDirection(side));
        }

#if UNITY_EDITOR
        private const string k_GizmoPrefKey = "PrimerVolante.RoadLaneDirection.ShowAllGizmos";
        private static bool s_ShowAllGizmos;
        private static bool s_ShowAllGizmosLoaded;

        /// <summary>
        /// Solo Editor: dibuja las flechas de sentido en todas las piezas de la escena, no solo en la seleccionada.
        /// Se recuerda entre sesiones.
        /// </summary>
        public static bool ShowAllGizmos
        {
            get
            {
                if (!s_ShowAllGizmosLoaded)
                {
                    s_ShowAllGizmos = UnityEditor.EditorPrefs.GetBool(k_GizmoPrefKey, false);
                    s_ShowAllGizmosLoaded = true;
                }

                return s_ShowAllGizmos;
            }
            set
            {
                s_ShowAllGizmos = value;
                s_ShowAllGizmosLoaded = true;
                UnityEditor.EditorPrefs.SetBool(k_GizmoPrefKey, value);
            }
        }

        private static readonly Color s_TwoWayColor = new Color(0.2f, 0.85f, 1f, 1f);
        private static readonly Color s_OneWayColor = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly LaneSide[] s_Sides = { LaneSide.PositiveX, LaneSide.NegativeX };

        private void OnDrawGizmos()
        {
            // Modo "toda la escena": sin buscar componentes por pieza en cada repintado de la Scene view.
            if (ShowAllGizmos) DrawArrows(precise: false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawArrows(precise: true);
        }

        // precise: centra cada flecha en su zona (la pieza seleccionada). Sin eso usa el centro nominal del carril.
        private void DrawArrows(bool precise)
        {
            Gizmos.color = IsOneWay ? s_OneWayColor : s_TwoWayColor;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            RoadLaneZone[] zones = precise ? GetComponentsInChildren<RoadLaneZone>(true) : System.Array.Empty<RoadLaneZone>();
            foreach (LaneSide side in s_Sides)
            {
                // Centro de cada carril según su zona (si no hay zonas, la mitad del carril en el origen de la pieza).
                float laneX = side == LaneSide.PositiveX ? 3.575f : -3.575f;
                float centerZ = 0f;
                foreach (RoadLaneZone zone in zones)
                {
                    if (zone.Side != side) continue;
                    var box = zone.GetComponent<BoxCollider>();
                    if (box == null) continue;
                    Vector3 center = zone.transform.localPosition + box.center;
                    laneX = center.x;
                    centerZ = center.z;
                    break;
                }

                Vector3 dir = AllowedLocalDirection(side);
                Vector3 mid = new Vector3(laneX, 0.4f, centerZ);
                Vector3 tail = mid - dir * 3f;
                Vector3 tip = mid + dir * 3f;
                Vector3 perpendicular = Vector3.Cross(Vector3.up, dir);
                Gizmos.DrawLine(tail, tip);
                Gizmos.DrawLine(tip, tip - dir * 1.2f + perpendicular * 0.7f);
                Gizmos.DrawLine(tip, tip - dir * 1.2f - perpendicular * 0.7f);
            }

            Gizmos.matrix = previous;
        }
#endif
    }
}
