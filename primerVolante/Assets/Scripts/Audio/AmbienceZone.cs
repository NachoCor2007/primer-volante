using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Volumen en forma de caja (sin collider) que delimita una zona de ambiente (US20): ciudad, plaza o pueblo.
    /// El <see cref="AmbienceDirector"/> calcula su peso según la distancia del oyente a la caja. Varias zonas
    /// pueden compartir perfil (las dos mitades de la plaza).
    /// </summary>
    [DisallowMultipleComponent]
    public class AmbienceZone : MonoBehaviour
    {
        private static readonly List<AmbienceZone> s_Active = new List<AmbienceZone>();

        [Header("Zona")]
        [Tooltip("Tamaño de la caja en el espacio local (metros con escala 1).")]
        [SerializeField] private Vector3 m_Size = new Vector3(50f, 30f, 50f);

        [Tooltip("Ancho de la franja de fade alrededor de la caja (metros): el peso baja de 1 a 0 a lo largo de ella.")]
        [SerializeField] private float m_FadeDistance = 20f;

        [Tooltip("Perfil de audio de la zona (lecho y dispersos).")]
        [SerializeField] private AmbienceZoneProfile m_Profile;

        [Tooltip("Prioridad ante solapamientos: la mayor domina. La plaza está pegada a la ciudad y al pueblo y su peso tiene que ganar cuando el oyente está dentro.")]
        [SerializeField] private int m_Priority;

        /// <summary>Zonas habilitadas en la escena.</summary>
        public static IReadOnlyList<AmbienceZone> Active => s_Active;

        /// <summary>Tamaño de la caja en el espacio local.</summary>
        public Vector3 Size
        {
            get => m_Size;
            set => m_Size = new Vector3(Mathf.Max(0f, value.x), Mathf.Max(0f, value.y), Mathf.Max(0f, value.z));
        }

        /// <summary>Ancho de la franja de fade en metros.</summary>
        public float FadeDistance
        {
            get => m_FadeDistance;
            set => m_FadeDistance = Mathf.Max(0f, value);
        }

        /// <summary>Perfil de audio de la zona.</summary>
        public AmbienceZoneProfile Profile
        {
            get => m_Profile;
            set => m_Profile = value;
        }

        /// <summary>Prioridad ante solapamientos.</summary>
        public int Priority
        {
            get => m_Priority;
            set => m_Priority = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Active.Clear();
        }

        private void OnEnable()
        {
            if (!s_Active.Contains(this)) s_Active.Add(this);
        }

        private void OnDisable()
        {
            s_Active.Remove(this);
        }

        private void OnValidate()
        {
            m_Size = new Vector3(Mathf.Max(0f, m_Size.x), Mathf.Max(0f, m_Size.y), Mathf.Max(0f, m_Size.z));
            m_FadeDistance = Mathf.Max(0f, m_FadeDistance);
        }

        /// <summary>
        /// Distancia en metros del punto a la caja (0 si está dentro). Se mide en el espacio de la zona, así que
        /// respeta su rotación y escala.
        /// </summary>
        public float DistanceOutside(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 scale = transform.lossyScale;
            Vector3 half = m_Size * 0.5f;

            float dx = Mathf.Max(Mathf.Abs(local.x) - half.x, 0f) * Mathf.Abs(scale.x);
            float dy = Mathf.Max(Mathf.Abs(local.y) - half.y, 0f) * Mathf.Abs(scale.y);
            float dz = Mathf.Max(Mathf.Abs(local.z) - half.z, 0f) * Mathf.Abs(scale.z);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Peso (0..1) de la zona para un punto: 1 dentro, 0 fuera de la franja de fade.</summary>
        public float GetWeight(Vector3 worldPosition)
        {
            return AmbienceMath.ZoneWeight(DistanceOutside(worldPosition), m_FadeDistance);
        }

        private void OnDrawGizmos()
        {
            DrawGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmo(true);
        }

        private void DrawGizmo(bool selected)
        {
            Color color = ProfileColor();
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            // Caja de la zona.
            Gizmos.color = new Color(color.r, color.g, color.b, selected ? 0.18f : 0.06f);
            Gizmos.DrawCube(Vector3.zero, m_Size);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireCube(Vector3.zero, m_Size);

            // Caja de fade (la zona expandida por la franja, en unidades locales).
            Vector3 scale = transform.lossyScale;
            Vector3 fade = new Vector3(
                Mathf.Abs(scale.x) > 1e-4f ? m_FadeDistance / Mathf.Abs(scale.x) : 0f,
                Mathf.Abs(scale.y) > 1e-4f ? m_FadeDistance / Mathf.Abs(scale.y) : 0f,
                Mathf.Abs(scale.z) > 1e-4f ? m_FadeDistance / Mathf.Abs(scale.z) : 0f);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, m_Size + fade * 2f);

            Gizmos.matrix = previous;
        }

        private Color ProfileColor()
        {
            if (m_Profile == null) return Color.gray;
            // Color estable por nombre de perfil: las zonas que comparten perfil comparten color.
            unchecked
            {
                int h = 23;
                foreach (char c in m_Profile.name) h = h * 31 + c;
                return Color.HSVToRGB((h & 0xFFFF) / 65535f, 0.6f, 0.9f);
            }
        }
    }
}
