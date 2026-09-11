using UnityEngine;
using Unity.XR.CoreUtils;

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

        [Header("Ajustes de Posición")]
        [Tooltip("Offset relativo al asiento del conductor (para afinar la altura o posición de la vista).")]
        [SerializeField] private Vector3 m_SeatOffset = new Vector3(0f, 1.2f, 0f);

        private XROrigin m_XROrigin;

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

        public Vector3 SeatOffset
        {
            get => m_SeatOffset;
            set => m_SeatOffset = value;
        }

        private void Awake()
        {
            m_XROrigin = GetComponent<XROrigin>();
        }

        private void Start()
        {
            FollowSeatPosition();
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
                transform.position = m_DriverSeat.position + m_DriverSeat.rotation * m_SeatOffset;
                transform.rotation = m_DriverSeat.rotation;

                // Corrige el bug del XR Interaction Simulator / XR Device Simulator en Unity:
                // Unity ejecuta CameraFloorOffsetObject.transform.position = Vector3.zero (en coordenadas de mundo),
                // lo que desfasa el Camera Offset en el valor inverso exacto del XROrigin (ej. x: -2.374, y: 0.09, z: 7.39)
                // y envía la cámara al origen del mundo (0, 0, 0).
                Transform offsetTransform = m_XROrigin != null && m_XROrigin.CameraFloorOffsetObject != null
                    ? m_XROrigin.CameraFloorOffsetObject.transform
                    : transform.Find("Camera Offset");

                if (offsetTransform != null && offsetTransform.localPosition != Vector3.zero)
                {
                    offsetTransform.localPosition = Vector3.zero;
                    offsetTransform.localRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>
        /// Fuerza una alineación instantánea con el asiento del conductor.
        /// </summary>
        public void SnapToSeat()
        {
            FollowSeatPosition();
        }
    }
}
