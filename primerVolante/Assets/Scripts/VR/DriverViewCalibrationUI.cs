using UnityEngine;
using UnityEngine.UI;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Conecta los sliders del panel de calibración con el desplazamiento de vista de
    /// DriverSeatFollower. Los sliders representan el desplazamiento relativo al asiento
    /// (espacio local del DriverSeat): X (lateral), Y (altura), Z (adelante/atrás).
    /// </summary>
    public class DriverViewCalibrationUI : MonoBehaviour
    {
        [Header("Dependencias")]
        [Tooltip("Componente que sincroniza la vista con el asiento. Si se deja vacío, se autodetecta.")]
        [SerializeField] private DriverSeatFollower m_DriverSeatFollower;

        [Header("Sliders de Calibración")]
        [Tooltip("Slider del eje lateral (Izquierda / Derecha).")]
        [SerializeField] private Slider m_LateralSlider;

        [Tooltip("Slider del eje vertical (Altura).")]
        [SerializeField] private Slider m_HeightSlider;

        [Tooltip("Slider del eje longitudinal (Adelante / Atrás).")]
        [SerializeField] private Slider m_DepthSlider;

        private void Awake()
        {
            if (m_DriverSeatFollower == null)
            {
                m_DriverSeatFollower = Object.FindAnyObjectByType<DriverSeatFollower>();
            }
        }

        private void OnEnable()
        {
            ConfigureSliderRanges();
            RefreshSlidersFromCurrentOffset();

            if (m_LateralSlider != null) m_LateralSlider.onValueChanged.AddListener(OnLateralChanged);
            if (m_HeightSlider != null) m_HeightSlider.onValueChanged.AddListener(OnHeightChanged);
            if (m_DepthSlider != null) m_DepthSlider.onValueChanged.AddListener(OnDepthChanged);
        }

        private void OnDisable()
        {
            if (m_LateralSlider != null) m_LateralSlider.onValueChanged.RemoveListener(OnLateralChanged);
            if (m_HeightSlider != null) m_HeightSlider.onValueChanged.RemoveListener(OnHeightChanged);
            if (m_DepthSlider != null) m_DepthSlider.onValueChanged.RemoveListener(OnDepthChanged);
        }

        private void ConfigureSliderRanges()
        {
            if (m_DriverSeatFollower == null) return;

            Vector3 min = m_DriverSeatFollower.CalibrationLimitsMin;
            Vector3 max = m_DriverSeatFollower.CalibrationLimitsMax;

            SetSliderRange(m_LateralSlider, min.x, max.x);
            SetSliderRange(m_HeightSlider, min.y, max.y);
            SetSliderRange(m_DepthSlider, min.z, max.z);
        }

        private static void SetSliderRange(Slider slider, float min, float max)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
        }

        /// <summary>
        /// Sincroniza la posición visual de los sliders con el desplazamiento de calibración actual,
        /// sin disparar sus eventos (útil al reabrir el panel).
        /// </summary>
        public void RefreshSlidersFromCurrentOffset()
        {
            if (m_DriverSeatFollower == null) return;

            Vector3 offset = m_DriverSeatFollower.CalibrationOffset;
            SetSliderValueSilently(m_LateralSlider, offset.x, OnLateralChanged);
            SetSliderValueSilently(m_HeightSlider, offset.y, OnHeightChanged);
            SetSliderValueSilently(m_DepthSlider, offset.z, OnDepthChanged);
        }

        private static void SetSliderValueSilently(Slider slider, float value, UnityEngine.Events.UnityAction<float> handler)
        {
            if (slider == null) return;
            slider.onValueChanged.RemoveListener(handler);
            slider.value = value;
            slider.onValueChanged.AddListener(handler);
        }

        private void OnLateralChanged(float value)
        {
            if (m_DriverSeatFollower != null) m_DriverSeatFollower.SetCalibrationAxis(0, value);
        }

        private void OnHeightChanged(float value)
        {
            if (m_DriverSeatFollower != null) m_DriverSeatFollower.SetCalibrationAxis(1, value);
        }

        private void OnDepthChanged(float value)
        {
            if (m_DriverSeatFollower != null) m_DriverSeatFollower.SetCalibrationAxis(2, value);
        }

        /// <summary>
        /// Restablece la vista a la posición inicial y refleja el cambio en los sliders.
        /// </summary>
        public void ResetCalibration()
        {
            if (m_DriverSeatFollower == null) return;

            m_DriverSeatFollower.ResetCalibration();
            RefreshSlidersFromCurrentOffset();
        }
    }
}
