using System;
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

        [Header("Calibración de Vista")]
        [Tooltip("Desplazamiento de la vista respecto al asiento, en el espacio local del DriverSeat (X: lateral, Y: altura, Z: adelante/atrás).")]
        [SerializeField] private Vector3 m_CalibrationOffset = Vector3.zero;

        [Tooltip("Límite mínimo (izquierda, abajo, atrás) del desplazamiento de calibración, en metros locales al asiento.")]
        [SerializeField] private Vector3 m_CalibrationLimitsMin = new Vector3(-0.25f, -0.15f, -0.25f);

        [Tooltip("Límite máximo (derecha, arriba, adelante) del desplazamiento de calibración, en metros locales al asiento.")]
        [SerializeField] private Vector3 m_CalibrationLimitsMax = new Vector3(0.25f, 0.20f, 0.25f);

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

        /// <summary>
        /// Desplazamiento de calibración actual (espacio local del asiento). Al asignarlo se recorta a los límites configurados.
        /// </summary>
        public Vector3 CalibrationOffset
        {
            get => m_CalibrationOffset;
            set => SetCalibrationOffset(value);
        }

        /// <summary>
        /// Se dispara cuando cambia el desplazamiento de calibración, con el desplazamiento en
        /// espacio de mundo (delta) que sufrió la vista. Permite que otros elementos (ej. el menú
        /// in-game) se muevan junto con el usuario sin quedar pegados a su cabeza.
        /// </summary>
        public event Action<Vector3> CalibrationOffsetChanged;

        public Vector3 CalibrationLimitsMin => m_CalibrationLimitsMin;

        public Vector3 CalibrationLimitsMax => m_CalibrationLimitsMax;

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
                transform.position = m_DriverSeat.TransformPoint(m_CalibrationOffset);
                transform.rotation = m_DriverSeat.rotation;
            }
        }

        /// <summary>
        /// Ajusta un solo eje del desplazamiento de calibración (0: lateral, 1: altura, 2: adelante/atrás).
        /// </summary>
        public void SetCalibrationAxis(int axis, float value)
        {
            Vector3 offset = m_CalibrationOffset;
            offset[axis] = value;
            CalibrationOffset = offset;
        }

        /// <summary>
        /// Restablece la vista a la posición inicial (sin desplazamiento respecto al asiento).
        /// </summary>
        public void ResetCalibration()
        {
            SetCalibrationOffset(Vector3.zero);
        }

        private void SetCalibrationOffset(Vector3 newOffset)
        {
            newOffset = ClampToLimits(newOffset);
            if (newOffset == m_CalibrationOffset) return;

            Vector3 worldDelta = Vector3.zero;
            if (m_DriverSeat != null)
            {
                worldDelta = m_DriverSeat.TransformPoint(newOffset) - m_DriverSeat.TransformPoint(m_CalibrationOffset);
            }

            m_CalibrationOffset = newOffset;
            CalibrationOffsetChanged?.Invoke(worldDelta);
        }

        private Vector3 ClampToLimits(Vector3 offset)
        {
            return new Vector3(
                Mathf.Clamp(offset.x, m_CalibrationLimitsMin.x, m_CalibrationLimitsMax.x),
                Mathf.Clamp(offset.y, m_CalibrationLimitsMin.y, m_CalibrationLimitsMax.y),
                Mathf.Clamp(offset.z, m_CalibrationLimitsMin.z, m_CalibrationLimitsMax.z));
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
