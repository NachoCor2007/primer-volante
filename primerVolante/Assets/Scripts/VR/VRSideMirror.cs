using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Espejo lateral motorizado (izquierdo o derecho) montado en el exterior del vehículo.
    /// Posee un pivote interno para el vidrio reflectante (y la cámara asociada) que permite
    /// ajuste motorizado en Yaw (hasta 20°) y Pitch (hasta 15°).
    /// Expone métodos públicos para recibir comandos continuos o por pulsos desde el panel de control.
    /// </summary>
    public class VRSideMirror : MonoBehaviour
    {
        public enum MirrorSide
        {
            Left,
            Right
        }

        [Header("Identificación de Espejo")]
        [Tooltip("Lado del vehículo donde está montado el espejo.")]
        [SerializeField] private MirrorSide m_Side = MirrorSide.Left;

        [Header("Pivote y Cinemática")]
        [Tooltip("Transform del soporte basculante interno donde se monta el vidrio del espejo y su cámara.")]
        [SerializeField] private Transform m_GlassPivot;

        [Header("Límites de Recorrido Motorizado")]
        [Tooltip("Rango máximo de giro horizontal (Yaw) en grados (±20°).")]
        [SerializeField] private float m_MaxYaw = 20f;

        [Tooltip("Rango máximo de inclinación vertical (Pitch) en grados (±15°).")]
        [SerializeField] private float m_MaxPitch = 15f;

        [Header("Velocidad de Ajuste")]
        [Tooltip("Velocidad por defecto del servomotor en grados por segundo.")]
        [SerializeField] private float m_DefaultSpeed = 15f;

        [Header("Estado Actual")]
        [SerializeField] private float m_CurrentYaw = 0f;
        [SerializeField] private float m_CurrentPitch = 0f;

        private Quaternion m_InitialGlassRotation;
        private bool m_IsInitialized = false;

        public MirrorSide Side => m_Side;
        public float CurrentYaw => m_CurrentYaw;
        public float CurrentPitch => m_CurrentPitch;
        public float MaxYaw => m_MaxYaw;
        public float MaxPitch => m_MaxPitch;
        public Transform GlassPivot => m_GlassPivot;

        private void Awake()
        {
            InitializePivot();
        }

        private void Start()
        {
            InitializePivot();
            ApplyRotation();
        }

        public void InitializePivot()
        {
            if (m_IsInitialized) return;

            if (m_GlassPivot == null)
            {
                // Si no se asignó pivote interno, buscar un hijo llamado Glass_Pivot o usar este Transform
                Transform found = transform.Find("Glass_Pivot");
                m_GlassPivot = found != null ? found : transform;
            }

            m_InitialGlassRotation = m_GlassPivot.localRotation;
            m_IsInitialized = true;
        }

        /// <summary>
        /// Aplica un comando de movimiento analógico o continuo al servomotor.
        /// </summary>
        /// <param name="delta">Vector2 con dirección horizontal (X = Yaw) y vertical (Y = Pitch).</param>
        /// <param name="speed">Velocidad de ajuste en grados por segundo (si es <= 0 usa m_DefaultSpeed).</param>
        public void Adjust(Vector2 delta, float speed)
        {
            if (delta.sqrMagnitude < 0.0001f) return;

            float moveSpeed = speed > 0f ? speed : m_DefaultSpeed;

            // X = Yaw (izquierda / derecha), Y = Pitch (arriba / abajo)
            m_CurrentYaw = Mathf.Clamp(m_CurrentYaw + delta.x * moveSpeed * Time.deltaTime, -m_MaxYaw, m_MaxYaw);
            m_CurrentPitch = Mathf.Clamp(m_CurrentPitch + delta.y * moveSpeed * Time.deltaTime, -m_MaxPitch, m_MaxPitch);

            ApplyRotation();
        }

        /// <summary>
        /// Aplica un ajuste discreto por pulso/paso fijo (ej. al pulsar una tecla o botón táctil).
        /// </summary>
        /// <param name="direction">Dirección unitaria del pulso.</param>
        /// <param name="stepDegrees">Grados de desplazamiento por pulso.</param>
        public void AdjustPulse(Vector2 direction, float stepDegrees = 1f)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            m_CurrentYaw = Mathf.Clamp(m_CurrentYaw + direction.x * stepDegrees, -m_MaxYaw, m_MaxYaw);
            m_CurrentPitch = Mathf.Clamp(m_CurrentPitch + direction.y * stepDegrees, -m_MaxPitch, m_MaxPitch);

            ApplyRotation();
        }

        /// <summary>
        /// Establece directamente los ángulos de Pitch y Yaw dentro de los límites seguros.
        /// </summary>
        public void SetAngles(float pitch, float yaw)
        {
            InitializePivot();
            m_CurrentPitch = Mathf.Clamp(pitch, -m_MaxPitch, m_MaxPitch);
            m_CurrentYaw = Mathf.Clamp(yaw, -m_MaxYaw, m_MaxYaw);
            ApplyRotation();
        }

        /// <summary>
        /// Centra el espejo lateral a su posición neutral original.
        /// </summary>
        public void ResetToCenter()
        {
            SetAngles(0f, 0f);
        }

        /// <summary>
        /// Aplica la rotación al pivote del vidrio conservando la orientación base.
        /// </summary>
        public void ApplyRotation()
        {
            if (m_GlassPivot == null) return;

            // Roll siempre bloqueado en 0°
            m_GlassPivot.localRotation = m_InitialGlassRotation * Quaternion.Euler(m_CurrentPitch, m_CurrentYaw, 0f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_GlassPivot == null)
            {
                Transform found = transform.Find("Glass_Pivot");
                if (found != null) m_GlassPivot = found;
            }
        }
#endif
    }
}
