using TMPro;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Enciende en rojo la letra (P, R, N, D) correspondiente al cambio actual
    /// de VehicleController.CurrentGear, y deja las demás en blanco.
    /// </summary>
    public class GearIndicatorController : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Controlador del vehículo. Se autodetecta en los padres si se deja vacío.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Letras del indicador, en orden P, R, N, D (mismo orden que el enum GearState).")]
        [SerializeField] private TMP_Text[] m_Labels;

        [Header("Colores")]
        [Tooltip("Color de una letra cuando no es el cambio actual.")]
        [SerializeField] private Color m_UnselectedColor = Color.white;

        [Tooltip("Color de la letra correspondiente al cambio actual.")]
        [SerializeField] private Color m_SelectedColor = Color.red;

        private GearState m_LastAppliedGear;
        private bool m_HasAppliedOnce;

        private void Awake()
        {
            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();
        }

        private void Start()
        {
            ApplyGear(m_VehicleController != null ? m_VehicleController.CurrentGear : GearState.Park);
        }

        private void Update()
        {
            if (m_VehicleController == null) return;

            GearState gear = m_VehicleController.CurrentGear;
            if (!m_HasAppliedOnce || gear != m_LastAppliedGear)
            {
                ApplyGear(gear);
            }
        }

        private void ApplyGear(GearState gear)
        {
            m_LastAppliedGear = gear;
            m_HasAppliedOnce = true;

            if (m_Labels == null) return;

            for (int i = 0; i < m_Labels.Length; i++)
            {
                if (m_Labels[i] == null) continue;
                m_Labels[i].color = (i == (int)gear) ? m_SelectedColor : m_UnselectedColor;
            }
        }
    }
}
