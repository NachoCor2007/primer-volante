using PrimerVolante.VR;
using UnityEngine;

namespace PrimerVolante.Lessons
{
    public enum ParkingStationType
    {
        Diagonal45 = 0,
        Bay90Reverse = 1,
        ParallelRight = 2
    }

    /// <summary>
    /// Zona de detección volumétrica para dársenas de estacionamiento (Lección 6).
    /// Valida que el vehículo ingrese correctamente, se detenga con baja velocidad,
    /// mantenga la alineación angular adecuada con respecto al cordón/dársena,
    /// y aplique los mandos correctos (Park, freno de mano o reversa según la estación).
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ParkingZoneTrigger : MonoBehaviour
    {
        [Header("Tipo de Estación")]
        [Tooltip("Tipo de maniobra que evalúa esta dársena.")]
        [SerializeField] private ParkingStationType m_StationType = ParkingStationType.Diagonal45;

        [Header("Criterios de Validación")]
        [Tooltip("Velocidad máxima permitida (km/h) para considerar el vehículo detenido.")]
        [SerializeField] private float m_MaxStopSpeedKmh = 0.5f;

        [Tooltip("Desviación angular máxima (grados) permitida respecto a la orientación de la dársena.")]
        [SerializeField] private float m_MaxAlignmentAngleDeg = 18f;

        [Header("Depuración")]
        [SerializeField] private bool m_DrawGizmos = true;

        private BoxCollider m_Collider;
        private VehicleController m_CurrentVehicle;
        private bool m_IsVehicleInside = false;

        public ParkingStationType StationType
        {
            get => m_StationType;
            set => m_StationType = value;
        }

        public bool IsVehicleInside => m_IsVehicleInside;
        public VehicleController CurrentVehicle => m_CurrentVehicle;

        public bool IsVehicleParkedProperly
        {
            get
            {
                if (m_CurrentVehicle == null || !m_IsVehicleInside) return false;
                return EvaluateParkingCondition(m_CurrentVehicle);
            }
        }

        public bool IsVehiclePositionedProperly => m_CurrentVehicle != null && m_IsVehicleInside && IsVehiclePositioned(m_CurrentVehicle);
        public bool IsVehicleSecuredProperly => m_CurrentVehicle != null && m_IsVehicleInside && IsVehicleSecuredInZone(m_CurrentVehicle);

        private void Awake()
        {
            m_Collider = GetComponent<BoxCollider>();
            if (m_Collider != null)
            {
                m_Collider.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            VehicleController vc = other.GetComponentInParent<VehicleController>();
            if (vc != null)
            {
                m_CurrentVehicle = vc;
                m_IsVehicleInside = true;
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (m_CurrentVehicle == null)
            {
                VehicleController vc = other.GetComponentInParent<VehicleController>();
                if (vc != null)
                {
                    m_CurrentVehicle = vc;
                    m_IsVehicleInside = true;
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            VehicleController vc = other.GetComponentInParent<VehicleController>();
            if (vc != null && vc == m_CurrentVehicle)
            {
                m_IsVehicleInside = false;
                m_CurrentVehicle = null;
            }
        }

        /// <summary>
        /// Devuelve el vector unitario mundial hacia donde debe apuntar el frente (morro) del vehículo.
        /// - 90° de culata: el frente apunta hacia la calle (-transform.forward).
        /// - 45° diagonal y Paralelo: el frente apunta hacia adelante (transform.forward).
        /// </summary>
        public Vector3 GetTargetForwardDirection()
        {
            switch (m_StationType)
            {
                case ParkingStationType.Bay90Reverse:
                    // En 90° de culata, la parte trasera entra a la dársena y el frente queda hacia la calle
                    return -transform.forward;
                case ParkingStationType.Diagonal45:
                case ParkingStationType.ParallelRight:
                default:
                    return transform.forward;
            }
        }

        /// <summary>
        /// Evalúa si el vehículo ingresó al recuadro de la dársena, está detenido y correctamente alineado.
        /// No exige palanca en P ni freno de mano todavía (permite completar la maniobra de conducción pura).
        /// </summary>
        public bool IsVehiclePositioned(VehicleController vehicle)
        {
            if (vehicle == null || !m_IsVehicleInside) return false;

            if (vehicle.CurrentSpeedKmh > m_MaxStopSpeedKmh) return false;

            Vector3 targetHeading = GetTargetForwardDirection();
            float headingAngle = Vector3.Angle(vehicle.transform.forward, targetHeading);
            if (headingAngle > m_MaxAlignmentAngleDeg) return false;

            return true;
        }

        /// <summary>
        /// Evalúa si el vehículo, estando dentro de la dársena, colocó la marcha en Park (P)
        /// y subió firmemente el freno de mano (>= 0.85).
        /// </summary>
        public bool IsVehicleSecuredInZone(VehicleController vehicle)
        {
            if (vehicle == null || !m_IsVehicleInside) return false;

            bool isParkGear = (vehicle.CurrentGear == GearState.Park || vehicle.CurrentGear == GearState.P);
            bool isHandbrakeEngaged = vehicle.HandbrakeEngagement >= 0.85f;
            return isParkGear && isHandbrakeEngaged;
        }

        /// <summary>
        /// Evalúa si el vehículo cumple todas las condiciones de estacionamiento de la dársena.
        /// </summary>
        public bool EvaluateParkingCondition(VehicleController vehicle)
        {
            if (vehicle == null) return false;

            // 1. Condición de detención: el vehículo debe estar detenido (velocidad < 0.5 km/h)
            if (vehicle.CurrentSpeedKmh > m_MaxStopSpeedKmh)
            {
                return false;
            }

            // 2. Condición de alineación angular respecto a la dirección obligatoria
            Vector3 targetHeading = GetTargetForwardDirection();
            float headingAngle = Vector3.Angle(vehicle.transform.forward, targetHeading);

            if (headingAngle > m_MaxAlignmentAngleDeg)
            {
                return false;
            }

            // 3. Condición de mandos de cabina según tipo de estación
            switch (m_StationType)
            {
                case ParkingStationType.Diagonal45:
                    // Dársena a 45: detenido, palanca en P y freno de mano accionado
                    bool isParkGear = (vehicle.CurrentGear == GearState.Park || vehicle.CurrentGear == GearState.P);
                    bool isHandbrakeEngaged = vehicle.HandbrakeEngagement >= 0.85f;
                    return isParkGear && isHandbrakeEngaged;

                case ParkingStationType.Bay90Reverse:
                    // Dársena a 90 de culata: maniobra marcha atrás completada, centrado y frenado
                    bool isReverseOrPark = (vehicle.CurrentGear == GearState.Reverse || vehicle.CurrentGear == GearState.R ||
                                            vehicle.CurrentGear == GearState.Park || vehicle.CurrentGear == GearState.P);
                    bool isBraked = vehicle.BrakeValue >= 0.4f || vehicle.HandbrakeEngagement >= 0.5f ||
                                    vehicle.CurrentGear == GearState.Park || vehicle.CurrentGear == GearState.P;
                    return isReverseOrPark && isBraked;

                case ParkingStationType.ParallelRight:
                    // Dársena en paralelo al cordón: alineado al cordón y detenido
                    bool isParallelStopped = vehicle.CurrentSpeedKmh <= m_MaxStopSpeedKmh;
                    bool isGearValid = (vehicle.CurrentGear == GearState.Park || vehicle.CurrentGear == GearState.P ||
                                        vehicle.CurrentGear == GearState.Reverse || vehicle.CurrentGear == GearState.R ||
                                        vehicle.CurrentGear == GearState.Neutral || vehicle.CurrentGear == GearState.N);
                    return isParallelStopped && isGearValid;

                default:
                    return false;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!m_DrawGizmos) return;

            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Color fillColor = m_IsVehicleInside ? new Color(0f, 1f, 0.4f, 0.25f) : new Color(0.2f, 0.6f, 1f, 0.15f);
            Color wireColor = m_IsVehicleInside ? Color.green : new Color(0.2f, 0.7f, 1f, 0.9f);

            Gizmos.color = fillColor;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = wireColor;
            Gizmos.DrawWireCube(box.center, box.size);

            Gizmos.matrix = prev;

            // Dibujar la flecha indicadora del sentido obligatorio del frente del auto
            DrawDirectionArrowGizmo(box);
        }

        private void DrawDirectionArrowGizmo(BoxCollider box)
        {
            Vector3 worldCenter = transform.TransformPoint(box.center);
            worldCenter.y = transform.position.y + 0.08f;

            Vector3 targetDir = GetTargetForwardDirection();
            float arrowLength = Mathf.Max(2.2f, box.size.z * 0.45f);
            Vector3 startPos = worldCenter - targetDir * (arrowLength * 0.5f);
            Vector3 endPos = worldCenter + targetDir * (arrowLength * 0.5f);

            // Color amarillo vial brillante con punta de flecha
            Gizmos.color = new Color(1.0f, 0.85f, 0.0f, 1.0f);
            Gizmos.DrawLine(startPos, endPos);

            Vector3 right = Vector3.Cross(Vector3.up, targetDir).normalized;
            float tipSize = 0.65f;
            Vector3 leftBarb = endPos - targetDir * tipSize - right * (tipSize * 0.5f);
            Vector3 rightBarb = endPos - targetDir * tipSize + right * (tipSize * 0.5f);

            Gizmos.DrawLine(endPos, leftBarb);
            Gizmos.DrawLine(endPos, rightBarb);
            Gizmos.DrawLine(leftBarb, rightBarb);

            // Handles para grosor y etiqueta visible en la Scene View de Unity Editor
            UnityEditor.Handles.color = new Color(1.0f, 0.85f, 0.0f, 0.95f);
            UnityEditor.Handles.DrawAAPolyLine(6f, startPos, endPos);
            UnityEditor.Handles.DrawAAPolyLine(6f, endPos, leftBarb);
            UnityEditor.Handles.DrawAAPolyLine(6f, endPos, rightBarb);
            UnityEditor.Handles.DrawAAPolyLine(4f, leftBarb, rightBarb);

            string labelText = m_StationType switch
            {
                ParkingStationType.Bay90Reverse => "➔ Frente del auto (De culata hacia la calle)",
                ParkingStationType.Diagonal45 => "➔ Frente del auto (De frente a 45°)",
                ParkingStationType.ParallelRight => "➔ Frente del auto (Sentido del cordón)",
                _ => "➔ Frente del auto"
            };

            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.yellow;
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 12;
            style.alignment = TextAnchor.MiddleCenter;

            UnityEditor.Handles.Label(endPos + Vector3.up * 0.25f, labelText, style);
        }
#endif
    }
}
