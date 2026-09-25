using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controlador del tablero diegético digital del vehículo (World Space Canvas).
    /// Actualiza en tiempo real:
    /// - Velocímetro digital (KM/H)
    /// - Testigos de guiños / balizas (flechas izquierda y derecha con parpadeo)
    /// - Tira de marchas (P, R, N, D) resaltando la activa
    /// - Testigo de freno de mano (P / !) en rojo
    /// - Testigo de luces frontales / bajas en verde o azul
    /// </summary>
    public class DashboardUIController : MonoBehaviour
    {
        [Header("Dependencias")]
        [Tooltip("Controlador del vehículo. Si se deja vacío se autodetecta.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Header("Velocímetro Digital")]
        [Tooltip("Texto para mostrar la velocidad en números.")]
        [SerializeField] private TMP_Text m_SpeedText;

        [Tooltip("Texto opcional para la unidad de medida (KM/H).")]
        [SerializeField] private TMP_Text m_UnitText;

        [Header("Guiños / Luces de Giro")]
        [Tooltip("Flecha o indicador de giro izquierdo (←).")]
        [SerializeField] private Graphic m_TurnLeftIndicator;

        [Tooltip("Flecha o indicador de giro derecho (→).")]
        [SerializeField] private Graphic m_TurnRightIndicator;

        [Tooltip("Color del indicador de guiño cuando está encendido.")]
        [SerializeField] private Color m_TurnSignalActiveColor = new Color(0.2f, 1f, 0.2f, 1f);

        [Tooltip("Color del indicador de guiño cuando está apagado / inactivo.")]
        [SerializeField] private Color m_TurnSignalInactiveColor = new Color(0.15f, 0.15f, 0.15f, 0.25f);

        [Tooltip("Frecuencia de parpadeo de los guiños en segundos (~0.4s).")]
        [SerializeField] private float m_BlinkInterval = 0.4f;

        [Header("Selector de Marchas (P, R, N, D)")]
        [Tooltip("Texto de la marcha actual o referencias a las 4 marchas.")]
        [SerializeField] private TMP_Text m_CurrentGearText;

        [Tooltip("Tira de textos para P, R, N, D en orden del enum GearState (0: P, 1: R, 2: N, 3: D).")]
        [SerializeField] private TMP_Text[] m_GearLabels;

        [Tooltip("Color de la marcha activa (resaltada).")]
        [SerializeField] private Color m_GearActiveColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Tooltip("Color de las marchas inactivas (atenuadas).")]
        [SerializeField] private Color m_GearInactiveColor = new Color(0.25f, 0.25f, 0.25f, 0.4f);

        [Header("Testigo de Freno de Mano")]
        [Tooltip("Elemento visual del freno de mano (ej. texto '(P)' o '!').")]
        [SerializeField] private Graphic m_HandbrakeIndicator;

        [Tooltip("Color activo del freno de mano.")]
        [SerializeField] private Color m_HandbrakeActiveColor = new Color(1f, 0.15f, 0.15f, 1f);

        [Tooltip("Color inactivo del freno de mano.")]
        [SerializeField] private Color m_HandbrakeInactiveColor = new Color(0.25f, 0.1f, 0.1f, 0.2f);

        [Header("Testigo de Luces Frontales / Bajas")]
        [Tooltip("Elemento visual de luces bajas.")]
        [SerializeField] private Graphic m_HeadlightsIndicator;

        [Tooltip("Color activo de las luces frontales.")]
        [SerializeField] private Color m_HeadlightsActiveColor = new Color(0.1f, 0.85f, 1f, 1f);

        [Tooltip("Color inactivo de las luces frontales.")]
        [SerializeField] private Color m_HeadlightsInactiveColor = new Color(0.1f, 0.2f, 0.25f, 0.2f);

        // Estados internos
        private bool m_HandbrakeActive = true;
        private bool m_HeadlightsActive = false;
        private bool m_BlinkState = false;
        private float m_BlinkTimer = 0f;
        private bool m_WasTurnSignalOrHazardActive = false;
        private GearState m_LastGear = (GearState)(-1);

        /// <summary>
        /// Estado actual del freno de mano.
        /// </summary>
        public bool IsHandbrakeActive => m_HandbrakeActive;

        /// <summary>
        /// Estado actual de las luces frontales.
        /// </summary>
        public bool IsHeadlightsActive => m_HeadlightsActive;

        private void Awake()
        {
            if (m_VehicleController == null)
            {
                m_VehicleController = GetComponentInParent<VehicleController>();
                if (m_VehicleController == null)
                {
                    m_VehicleController = Object.FindAnyObjectByType<VehicleController>();
                }
            }
        }

        private void Start()
        {
            if (m_UnitText != null)
                m_UnitText.text = "KM/H";

            UpdateGearDisplay(m_VehicleController != null ? m_VehicleController.CurrentGear : GearState.Park);
            UpdateHandbrakeDisplay();
            UpdateHeadlightsDisplay();
            ResetTurnSignalIndicators();
        }

        private void Update()
        {
            UpdateSpeedometer();
            UpdateTurnSignals();
            UpdateGear();
            CheckDefaultHandbrakeLogic();
        }

        /// <summary>
        /// Actualiza el display digital de velocidad con un número entero redondeado.
        /// </summary>
        private void UpdateSpeedometer()
        {
            if (m_SpeedText == null) return;

            float speed = 0f;
            if (m_VehicleController != null)
            {
                speed = Mathf.Max(0f, m_VehicleController.CurrentSpeedKmh);
            }

            int speedRounded = Mathf.RoundToInt(speed);
            m_SpeedText.text = speedRounded.ToString();
        }

        /// <summary>
        /// Gestiona el parpadeo de las flechas de giro y balizas.
        /// </summary>
        private void UpdateTurnSignals()
        {
            TurnSignalState signal = TurnSignalState.Off;
            bool isHazard = false;
            if (m_VehicleController != null)
            {
                signal = m_VehicleController.CurrentTurnSignal;
                isHazard = m_VehicleController.IsHazardActive;
            }

            bool leftShouldBlink = isHazard || (signal == TurnSignalState.Left);
            bool rightShouldBlink = isHazard || (signal == TurnSignalState.Right);

            if (!leftShouldBlink && !rightShouldBlink)
            {
                ResetTurnSignalIndicators();
                m_BlinkTimer = 0f;
                m_BlinkState = false;
                m_WasTurnSignalOrHazardActive = false;
                return;
            }

            if (!m_WasTurnSignalOrHazardActive)
            {
                m_WasTurnSignalOrHazardActive = true;
                m_BlinkState = true;
                m_BlinkTimer = 0f;
            }

            m_BlinkTimer += Time.deltaTime;
            if (m_BlinkTimer >= m_BlinkInterval)
            {
                m_BlinkTimer -= m_BlinkInterval;
                m_BlinkState = !m_BlinkState;
            }

            if (m_TurnLeftIndicator != null)
            {
                m_TurnLeftIndicator.color = (leftShouldBlink && m_BlinkState)
                    ? m_TurnSignalActiveColor
                    : m_TurnSignalInactiveColor;
            }

            if (m_TurnRightIndicator != null)
            {
                m_TurnRightIndicator.color = (rightShouldBlink && m_BlinkState)
                    ? m_TurnSignalActiveColor
                    : m_TurnSignalInactiveColor;
            }
        }

        private void ResetTurnSignalIndicators()
        {
            if (m_TurnLeftIndicator != null)
                m_TurnLeftIndicator.color = m_TurnSignalInactiveColor;

            if (m_TurnRightIndicator != null)
                m_TurnRightIndicator.color = m_TurnSignalInactiveColor;
        }

        /// <summary>
        /// Monitorea y actualiza la tira o texto de marchas (P, R, N, D).
        /// </summary>
        private void UpdateGear()
        {
            if (m_VehicleController == null) return;

            GearState currentGear = m_VehicleController.CurrentGear;
            if (currentGear != m_LastGear)
            {
                UpdateGearDisplay(currentGear);
            }
        }

        private void UpdateGearDisplay(GearState currentGear)
        {
            m_LastGear = currentGear;

            if (m_CurrentGearText != null)
            {
                m_CurrentGearText.text = currentGear.ToString();
            }

            if (m_GearLabels != null && m_GearLabels.Length > 0)
            {
                for (int i = 0; i < m_GearLabels.Length; i++)
                {
                    if (m_GearLabels[i] == null) continue;
                    bool isSelected = (i == (int)currentGear);
                    m_GearLabels[i].color = isSelected ? m_GearActiveColor : m_GearInactiveColor;
                }
            }
        }

        /// <summary>
        /// Lógica por defecto de freno de mano si no hay palanca externa conectada:
        /// se activa automáticamente si está en Parking y detenido.
        /// </summary>
        private void CheckDefaultHandbrakeLogic()
        {
            if (m_VehicleController == null) return;

            if (m_VehicleController.CurrentGear == GearState.Park && m_VehicleController.CurrentSpeedKmh < 0.2f)
            {
                if (!m_HandbrakeActive)
                {
                    SetHandbrakeState(true);
                }
            }
            else if (m_VehicleController.CurrentGear == GearState.Drive || m_VehicleController.CurrentGear == GearState.Reverse)
            {
                if (m_HandbrakeActive && m_VehicleController.ThrottleValue > 0.05f)
                {
                    SetHandbrakeState(false);
                }
            }
        }

        /// <summary>
        /// Activa o desactiva el testigo de freno de mano.
        /// </summary>
        public void SetHandbrakeState(bool active)
        {
            m_HandbrakeActive = active;
            UpdateHandbrakeDisplay();
        }

        private void UpdateHandbrakeDisplay()
        {
            if (m_HandbrakeIndicator != null)
            {
                m_HandbrakeIndicator.color = m_HandbrakeActive
                    ? m_HandbrakeActiveColor
                    : m_HandbrakeInactiveColor;
            }
        }

        /// <summary>
        /// Activa o desactiva el testigo de luces frontales / bajas.
        /// </summary>
        public void SetHeadlightsState(bool active)
        {
            m_HeadlightsActive = active;
            UpdateHeadlightsDisplay();
        }

        private void UpdateHeadlightsDisplay()
        {
            if (m_HeadlightsIndicator != null)
            {
                m_HeadlightsIndicator.color = m_HeadlightsActive
                    ? m_HeadlightsActiveColor
                    : m_HeadlightsInactiveColor;
            }
        }
    }
}
