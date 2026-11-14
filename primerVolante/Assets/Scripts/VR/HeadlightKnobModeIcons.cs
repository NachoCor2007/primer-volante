using UnityEngine;
using UnityEngine.UI;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Íconos de modo sobre el bisel de la perilla de luces: uno por posición de snap (Off, Posición, Bajas, Altas),
    /// con los mismos íconos y colores del tablero de testigos. El del modo activo se ve a alfa 1 y el resto
    /// a alfa tenue. No gira con la perilla: vive en un Canvas aparte, hermano de la perilla.
    /// </summary>
    public class HeadlightKnobModeIcons : MonoBehaviour
    {
        /// <summary>Cantidad de modos (tamaño del array de íconos).</summary>
        public const int ModeCount = 4;

        [Tooltip("Perilla de luces cuyo modo se refleja.")]
        [SerializeField] private VRHeadlightKnob m_Knob;

        [Tooltip("Un gráfico por modo, indexado por HeadlightMode (Off, Position, LowBeam, HighBeam).")]
        [SerializeField] private Graphic[] m_Icons = new Graphic[ModeCount];

        [Tooltip("Color base por modo, indexado por HeadlightMode. Off = blanco neutro; el resto, los colores activos del tablero.")]
        [SerializeField] private Color[] m_ModeColors =
        {
            new Color(0.9f, 0.9f, 0.9f, 1f),
            new Color(0.2f, 0.85f, 0.3f, 1f),
            new Color(0.102f, 0.949f, 0.627f, 1f),
            new Color(0.102f, 0.482f, 1f, 1f)
        };

        [Tooltip("Alfa de los íconos de los modos inactivos.")]
        [SerializeField, Range(0f, 1f)] private float m_InactiveAlpha = 0.45f;

        private void OnEnable()
        {
            if (m_Knob != null) m_Knob.ModeChanged += Refresh;
        }

        private void OnDisable()
        {
            if (m_Knob != null) m_Knob.ModeChanged -= Refresh;
        }

        private void Start()
        {
            if (m_Knob != null) Refresh(m_Knob.CurrentMode);
        }

        /// <summary>
        /// Deja el ícono del modo indicado a alfa 1 y los demás a <c>m_InactiveAlpha</c>.
        /// </summary>
        public void Refresh(HeadlightMode active)
        {
            if (m_Icons == null) return;

            for (int i = 0; i < m_Icons.Length; i++)
            {
                Graphic icon = m_Icons[i];
                if (icon == null) continue;

                Color color = (m_ModeColors != null && i < m_ModeColors.Length) ? m_ModeColors[i] : Color.white;
                color.a = (i == (int)active) ? 1f : m_InactiveAlpha;
                icon.color = color;
            }
        }
    }
}
