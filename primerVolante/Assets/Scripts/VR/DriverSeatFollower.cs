using System;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Sincroniza la posición y rotación del XROrigin con el asiento del conductor (DriverSeat).
    /// Ejecución prioritaria (ExecutionOrder = -300) para que ocurra ANTES del XRInteractionManager (-105)
    /// y de los scripts de cabina. Con el auto en movimiento, si el XROrigin se reposiciona después de que
    /// el XRInteractionManager detecta hover/select, las manos quedan un frame atrasadas respecto de los
    /// interactuables del vehículo (desfase = velocidad × deltaTime) y los colliders chicos, como los del
    /// retrovisor, dejan de poder agarrarse.
    /// </summary>
    [DefaultExecutionOrder(-300)]
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
            set
            {
                m_DriverSeat = value;
                if (m_DriverSeat != null && m_FollowSeat)
                {
                    SnapToSeat();
                }
            }
        }

        public bool FollowSeat
        {
            get => m_FollowSeat;
            set
            {
                m_FollowSeat = value;
                if (m_FollowSeat && m_DriverSeat != null)
                {
                    SnapToSeat();
                }
            }
        }

        /// <summary>
        /// Permite asignar limpiamente el transform del asiento y opcionalmente forzar snap inmediato.
        /// </summary>
        public void SetDriverSeat(Transform seat, bool snap = true)
        {
            m_DriverSeat = seat;
            if (snap && m_DriverSeat != null && m_FollowSeat)
            {
                SnapToSeat();
            }
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

        private void Awake()
        {
            if (m_DriverSeat == null)
            {
                AutoDiscoverDriverSeat();
            }
        }

        /// <summary>
        /// Localiza automáticamente el vehículo del jugador en la escena usando FindAnyObjectByType
        /// y vincula el Transform 'DriverSeat'.
        /// </summary>
        public bool AutoDiscoverDriverSeat()
        {
            if (m_DriverSeat != null) return true;

            var vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            if (vehicle != null)
            {
                Transform seat = FindDriverSeatRecursive(vehicle.transform);
                if (seat != null)
                {
                    m_DriverSeat = seat;
                    if (m_FollowSeat)
                    {
                        SnapToSeat();
                    }
                    return true;
                }
            }

            return false;
        }

        private Transform FindDriverSeatRecursive(Transform current)
        {
            if (current == null) return null;
            if (string.Equals(current.name, "DriverSeat", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform found = FindDriverSeatRecursive(current.GetChild(i));
                if (found != null) return found;
            }

            return null;
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
            if (m_DriverSeat == null && m_FollowSeat)
            {
                AutoDiscoverDriverSeat();
            }

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
            FollowSeatPosition();
        }
    }
}
