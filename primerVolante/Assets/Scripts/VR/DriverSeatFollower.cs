using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Sincroniza la posición y rotación del XROrigin con el asiento del conductor (DriverSeat).
    /// Ejecución prioritaria (ExecutionOrder = 50) para que ocurra ANTES del procesamiento de scripts de cabina.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class DriverSeatFollower : MonoBehaviour
    {
        [Header("Referencias de Asiento")]
        [Tooltip("Transform del asiento del conductor dentro del vehículo.")]
        [SerializeField] private Transform m_DriverSeat;

        [Tooltip("Si se activa, el jugador se mantiene continuamente alineado al asiento.")]
        [SerializeField] private bool m_FollowSeat = true;

        public Transform DriverSeat
        {
            get => m_DriverSeat;
            set => m_DriverSeat = value;
        }

        public bool FollowSeat
        {
            get => m_FollowSeat;
            set => m_FollowSeat = value;
        }

        private void Update()
        {
            FollowSeatPosition();
        }

        private void LateUpdate()
        {
            FollowSeatPosition();
        }

        private void FollowSeatPosition()
        {
            if (m_FollowSeat && m_DriverSeat != null)
            {
                transform.position = m_DriverSeat.position;
                transform.rotation = m_DriverSeat.rotation;
            }
        }

        /// <summary>
        /// Fuerza una alineación instantánea con el asiento del conductor.
        /// </summary>
        public void SnapToSeat()
        {
            LateUpdate();
        }
    }
}
