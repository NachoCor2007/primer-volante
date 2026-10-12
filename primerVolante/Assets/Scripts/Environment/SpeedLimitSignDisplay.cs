using TMPro;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Controlador visual para la señal reglamentaria de velocidad máxima (Normativa Argentina R.14).
    /// Maneja el display de dígitos con TextMeshPro y calibra la tipografía dinámicamente entre
    /// valores de 2 dígitos (20-90 km/h) y 3 dígitos (100-130 km/h) para garantizar que los números
    /// permanezcan holgadamente dentro del disco blanco central sin invadir el aro perimetral rojo.
    /// </summary>
    [ExecuteAlways]
    [SelectionBase]
    public class SpeedLimitSignDisplay : MonoBehaviour
    {
        public const int MIN_SPEED_KMH = 20;
        public const int MAX_SPEED_KMH = 130;

        [Header("Configuración de Velocidad")]
        [Tooltip("Límite de velocidad en km/h reglamentario argentino (20 a 130 km/h).")]
        [Range(MIN_SPEED_KMH, MAX_SPEED_KMH)]
        [SerializeField] private int m_SpeedLimitKmh = 40;

        [Header("Referencias de Display")]
        [Tooltip("Componente TextMeshPro que renderiza los dígitos en el frente del disco.")]
        [SerializeField] private TextMeshPro m_TextDisplay;

        [Header("Calibración Tipográfica - 2 Dígitos (20 - 99 km/h)")]
        [Tooltip("Tamaño de fuente estándar y amplio para 2 dígitos.")]
        [SerializeField] private float m_FontSizeTwoDigits = 3.2f;

        [Tooltip("Espaciado entre caracteres para 2 dígitos.")]
        [SerializeField] private float m_CharacterSpacingTwoDigits = 0f;

        [Tooltip("Márgenes laterales/verticales para 2 dígitos (Left, Top, Right, Bottom).")]
        [SerializeField] private Vector4 m_MarginTwoDigits = Vector4.zero;

        [Header("Calibración Tipográfica - 3 Dígitos (100 - 130 km/h)")]
        [Tooltip("Tamaño de fuente reducido para 3 dígitos para evitar invadir el aro rojo.")]
        [SerializeField] private float m_FontSizeThreeDigits = 2.3f;

        [Tooltip("Espaciado entre caracteres condensado para 3 dígitos.")]
        [SerializeField] private float m_CharacterSpacingThreeDigits = -6f;

        [Tooltip("Márgenes laterales/verticales para 3 dígitos (Left, Top, Right, Bottom).")]
        [SerializeField] private Vector4 m_MarginThreeDigits = Vector4.zero;

        /// <summary>
        /// Límite de velocidad actual en km/h.
        /// </summary>
        public int SpeedLimitKmh
        {
            get => m_SpeedLimitKmh;
            set => SetSpeedLimit(value);
        }

        /// <summary>
        /// Componente TextMeshPro asignado o detectado.
        /// </summary>
        public TextMeshPro TextDisplay => m_TextDisplay;

        /// <summary>
        /// Tamaño de fuente actualmente aplicado al texto.
        /// </summary>
        public float CurrentFontSize => m_TextDisplay != null ? m_TextDisplay.fontSize : 0f;

        /// <summary>
        /// Espaciado de caracteres actualmente aplicado.
        /// </summary>
        public float CurrentCharacterSpacing => m_TextDisplay != null ? m_TextDisplay.characterSpacing : 0f;

        private void Awake()
        {
            ResolveReferences();
            UpdateDisplay();
        }

        private void OnEnable()
        {
            UpdateDisplay();
        }

        private void OnValidate()
        {
            m_SpeedLimitKmh = Mathf.Clamp(m_SpeedLimitKmh, MIN_SPEED_KMH, MAX_SPEED_KMH);
            ResolveReferences();
            UpdateDisplay();
        }

        /// <summary>
        /// Cambia el límite de velocidad y recalibra la presentación tipográfica.
        /// </summary>
        /// <param name="speedKmh">Velocidad en km/h (se clampa entre 20 y 130 km/h).</param>
        public void SetSpeedLimit(int speedKmh)
        {
            m_SpeedLimitKmh = Mathf.Clamp(speedKmh, MIN_SPEED_KMH, MAX_SPEED_KMH);
            UpdateDisplay();
        }

        /// <summary>
        /// Actualiza el texto y calibra la tipografía en base a la cantidad de dígitos.
        /// </summary>
        public void UpdateDisplay()
        {
            if (m_TextDisplay == null)
            {
                ResolveReferences();
                if (m_TextDisplay == null) return;
            }

            string speedText = m_SpeedLimitKmh.ToString();
            m_TextDisplay.text = speedText;

            // Centrado estricto tanto vertical como horizontal
            m_TextDisplay.alignment = TextAlignmentOptions.Center;

            // Calibración según cantidad de dígitos
            if (m_SpeedLimitKmh >= 100)
            {
                // 3 Dígitos (100, 110, 120, 130 km/h):
                // Reduce fontSize y comprime el spacing para que permanezcan holgadamente
                // dentro del disco blanco y NUNCA toquen ni invadan el aro perimetral rojo.
                m_TextDisplay.fontSize = m_FontSizeThreeDigits;
                m_TextDisplay.characterSpacing = m_CharacterSpacingThreeDigits;
                m_TextDisplay.margin = m_MarginThreeDigits;
            }
            else
            {
                // 2 Dígitos (20, 30, 40, 60, etc.):
                // Fuente a tamaño estándar/amplio, espaciado regular, perfectamente centrada.
                m_TextDisplay.fontSize = m_FontSizeTwoDigits;
                m_TextDisplay.characterSpacing = m_CharacterSpacingTwoDigits;
                m_TextDisplay.margin = m_MarginTwoDigits;
            }

            m_TextDisplay.ForceMeshUpdate();
        }

        private void ResolveReferences()
        {
            if (m_TextDisplay == null)
            {
                m_TextDisplay = GetComponentInChildren<TextMeshPro>(true);
            }
        }
    }
}
