using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controlador cinemático-híbrido de movimiento y dirección de vehículo para VR.
    /// Maneja Aceleración (Gatillo Derecho), Frenado (Gatillo Izquierdo) y Giro por Volante (VRSteeringWheel).
    /// Evita explosiones físicas ignorando colisiones internas con el XROrigin del jugador.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public enum ForwardDirection
        {
            TransformForward,
            TransformRight,
            TransformUp,
            InverseForward
        }

        [Header("Acciones de Gatillos VR (Input System)")]
        [Tooltip("Acción para Gatillo Derecho (Acelerador 0.0 - 1.0)")]
        [SerializeField] private InputActionProperty m_RightTriggerAction;

        [Tooltip("Acción para Gatillo Izquierdo (Freno 0.0 - 1.0)")]
        [SerializeField] private InputActionProperty m_LeftTriggerAction;

        [Header("Parámetros de Rendimiento y Físicas")]
        [Tooltip("Velocidad máxima en km/h.")]
        [SerializeField] private float m_MaxSpeedKmh = 60f;

        [Tooltip("Tasa de aceleración en m/s^2.")]
        [SerializeField] private float m_AccelerationRate = 8f;

        [Tooltip("Tasa de desaceleración por freno en m/s^2.")]
        [SerializeField] private float m_BrakeForce = 18f;

        [Tooltip("Desaceleración pasiva / freno de motor en m/s^2.")]
        [SerializeField] private float m_IdleDeceleration = 3f;

        [Tooltip("Orientación del eje frontal del vehículo.")]
        [SerializeField] private ForwardDirection m_ForwardAxis = ForwardDirection.TransformForward;

        [Header("Parámetros de Dirección por Volante")]
        [Tooltip("Componente de volante VR para dirección.")]
        [SerializeField] private VRSteeringWheel m_SteeringWheel;

        [Tooltip("Velocidad máxima de giro de la carrocería en grados por segundo.")]
        [SerializeField] private float m_MaxTurnSpeed = 45f;

        [Tooltip("Si se activa, el coche solo gira si tiene velocidad de avance.")]
        [SerializeField] private bool m_ScaleTurnWithSpeed = true;

        [Header("Palanca de Cambios")]
        [Tooltip("Palanca de cambios VR (se autodetecta si es hijo del coche).")]
        [SerializeField] private VRGearShifter m_GearShifter;

        [Header("Debugging / Logs de Gatillos y Dirección")]
        [Tooltip("Si se activa, imprime mensajes en la Consola de Unity al presionar los gatillos o girar el volante.")]
        [SerializeField] private bool m_EnableDebugLogs = true;

        [Tooltip("Frecuencia máxima de impresión de logs en segundos.")]
        [SerializeField] private float m_LogInterval = 0.25f;

        // Fórmulas y valores en tiempo real
        private float m_CurrentSpeedMs = 0f; // m/s
        private float m_ThrottleValue = 0f;
        private float m_BrakeValue = 0f;
        private float m_LastLogTime = 0f;

        private Rigidbody m_Rigidbody;
        private InputAction m_DefaultLeftAction;
        private InputAction m_DefaultRightAction;
        
        private GearState m_CurrentGear = GearState.Park;

        /// <summary>
        /// Velocidad actual del vehículo en km/h.
        /// </summary>
        public float CurrentSpeedKmh => m_CurrentSpeedMs * 3.6f;

        /// <summary>
        /// Velocidad actual del vehículo en m/s.
        /// </summary>
        public float CurrentSpeedMs => m_CurrentSpeedMs;

        /// <summary>
        /// Valor actual de presión del acelerador (0.0 a 1.0).
        /// </summary>
        public float ThrottleValue => m_ThrottleValue;

        /// <summary>
        /// Valor actual de presión del freno (0.0 a 1.0).
        /// </summary>
        public float BrakeValue => m_BrakeValue;

        /// <summary>
        /// Referencia al volante asignado para dirección.
        /// </summary>
        public VRSteeringWheel SteeringWheel
        {
            get => m_SteeringWheel;
            set => m_SteeringWheel = value;
        }

        /// <summary>
        /// Marcha actual (Drive, Reverse, Neutral, Park)
        /// </summary>
        public GearState CurrentGear
        {
            get => m_CurrentGear;
            set => m_CurrentGear = value;
        }

        public void SetGear(GearState newGear)
        {
            m_CurrentGear = newGear;
        }

        public Vector3 GetForwardVector()
        {
            switch (m_ForwardAxis)
            {
                case ForwardDirection.TransformRight: return transform.right;
                case ForwardDirection.TransformUp: return transform.up;
                case ForwardDirection.InverseForward: return -transform.forward;
                default: return transform.forward;
            }
        }

        private void Reset()
        {
            ConfigurePhysicsAndColliders();
        }

        private void Awake()
        {
            ConfigurePhysicsAndColliders();
        }

        [ContextMenu("Reconfigurar Físicas y Collider Ahora")]
        public void ConfigurePhysicsAndColliders()
        {
            m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = false;
                m_Rigidbody.useGravity = true;
                m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            EnsureCollider();
            IgnoreInternalChildCollisions();

            if (m_SteeringWheel == null)
            {
                m_SteeringWheel = GetComponentInChildren<VRSteeringWheel>();
            }

            if (m_GearShifter == null)
            {
                m_GearShifter = GetComponentInChildren<VRGearShifter>();
            }

            if (m_GearShifter != null)
            {
                m_GearShifter.OnGearChanged.RemoveListener(SetGear);
                m_GearShifter.OnGearChanged.AddListener(SetGear);
                m_CurrentGear = m_GearShifter.currentGear;
            }

            if (Application.isPlaying && m_EnableDebugLogs)
            {
                Debug.Log($"[VehicleController] 🚗 Inicializado en '{gameObject.name}'. Rigidbody (Interpolate=OK), Volante: {(m_SteeringWheel != null ? "Conectado" : "No asignado")}");
            }
        }

        private void IgnoreInternalChildCollisions()
        {
            Collider mainCol = GetComponent<Collider>();
            if (mainCol == null) return;

            Collider[] childCols = GetComponentsInChildren<Collider>(true);
            foreach (var childCol in childCols)
            {
                if (childCol != null && childCol != mainCol)
                {
                    // Ignorar colisión interna con cualquier colisionador en los hijos (como el XROrigin o el jugador)
                    Physics.IgnoreCollision(mainCol, childCol, true);
                }
            }
        }

        private void EnsureCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                col = GetComponentInChildren<Collider>();
            }

            if (col == null)
            {
                BoxCollider boxCol = gameObject.AddComponent<BoxCollider>();
                SetupBoxColliderBounds(gameObject, boxCol);
            }
            else if (col is BoxCollider boxCol)
            {
                boxCol.isTrigger = false;
                if (boxCol.size.x <= 0.01f || boxCol.size.y <= 0.01f || boxCol.size.z <= 0.01f)
                {
                    SetupBoxColliderBounds(gameObject, boxCol);
                }
            }
        }

        public static void SetupBoxColliderBounds(GameObject root, BoxCollider boxCol)
        {
            boxCol.isTrigger = false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                Bounds worldBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    worldBounds.Encapsulate(renderers[i].bounds);
                }

                Vector3 localCenter = root.transform.InverseTransformPoint(worldBounds.center);
                Vector3 lossyScale = root.transform.lossyScale;
                Vector3 localSize = new Vector3(
                    lossyScale.x != 0 ? worldBounds.size.x / Mathf.Abs(lossyScale.x) : worldBounds.size.x,
                    lossyScale.y != 0 ? worldBounds.size.y / Mathf.Abs(lossyScale.y) : worldBounds.size.y,
                    lossyScale.z != 0 ? worldBounds.size.z / Mathf.Abs(lossyScale.z) : worldBounds.size.z
                );

                boxCol.center = localCenter;
                boxCol.size = Vector3.Max(localSize, new Vector3(0.8f, 0.8f, 1.5f));
            }
            else
            {
                boxCol.center = new Vector3(0, 0.8f, 0);
                boxCol.size = new Vector3(2.0f, 1.5f, 4.5f);
            }
        }

        private void OnEnable()
        {
            if (m_RightTriggerAction.action != null) m_RightTriggerAction.action.Enable();
            if (m_LeftTriggerAction.action != null) m_LeftTriggerAction.action.Enable();

            SetupDefaultActionsIfNeeded();
        }

        private void OnDisable()
        {
            if (m_RightTriggerAction.action != null) m_RightTriggerAction.action.Disable();
            if (m_LeftTriggerAction.action != null) m_LeftTriggerAction.action.Disable();

            m_DefaultLeftAction?.Disable();
            m_DefaultRightAction?.Disable();
        }

        private void SetupDefaultActionsIfNeeded()
        {
            if (m_LeftTriggerAction.action == null)
            {
                m_DefaultLeftAction = new InputAction("LeftTriggerDefault", InputActionType.Value, "<XRController>{LeftHand}/trigger");
                m_DefaultLeftAction.AddBinding("<XRController>{LeftHand}/activate");
                m_DefaultLeftAction.Enable();
            }

            if (m_RightTriggerAction.action == null)
            {
                m_DefaultRightAction = new InputAction("RightTriggerDefault", InputActionType.Value, "<XRController>{RightHand}/trigger");
                m_DefaultRightAction.AddBinding("<XRController>{RightHand}/activate");
                m_DefaultRightAction.Enable();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            m_ThrottleValue = ReadTriggerValue(m_RightTriggerAction, m_DefaultRightAction, XRNode.RightHand);
            m_BrakeValue = ReadTriggerValue(m_LeftTriggerAction, m_DefaultLeftAction, XRNode.LeftHand);

            if (m_EnableDebugLogs && Time.time - m_LastLogTime >= m_LogInterval)
            {
                if (m_BrakeValue > 0.01f)
                {
                    Debug.Log($"[VehicleController] 🛑 FRENO: {m_BrakeValue * 100f:F1}% | Vel: {CurrentSpeedKmh:F1} km/h | Pos: {transform.position}");
                    m_LastLogTime = Time.time;
                }
                else if (m_ThrottleValue > 0.01f)
                {
                    float steeringVal = m_SteeringWheel != null ? m_SteeringWheel.SteeringValue : 0f;
                    Debug.Log($"[VehicleController] 🏎️ ACELERADOR ({m_CurrentGear}): {m_ThrottleValue * 100f:F1}% | Giro: {steeringVal * 100f:F0}% | Vel: {CurrentSpeedKmh:F1} km/h | Pos: {transform.position}");
                    m_LastLogTime = Time.time;
                }
                else if (m_SteeringWheel != null && Mathf.Abs(m_SteeringWheel.SteeringValue) > 0.05f)
                {
                    Debug.Log($"[VehicleController] 🔄 GIRO VOLANTE: {m_SteeringWheel.SteeringValue * 100f:F0}% | Vel: {CurrentSpeedKmh:F1} km/h");
                    m_LastLogTime = Time.time;
                }
            }
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying) return;

            float maxSpeedMs = m_MaxSpeedKmh / 3.6f;

            if (m_BrakeValue > 0.01f)
            {
                float decel = m_BrakeForce * m_BrakeValue;
                m_CurrentSpeedMs = Mathf.MoveTowards(m_CurrentSpeedMs, 0f, decel * Time.fixedDeltaTime);
            }
            else if (m_ThrottleValue > 0.01f && (m_CurrentGear == GearState.Drive || m_CurrentGear == GearState.Reverse))
            {
                float targetSpeed = maxSpeedMs * m_ThrottleValue;
                m_CurrentSpeedMs = Mathf.MoveTowards(m_CurrentSpeedMs, targetSpeed, m_AccelerationRate * Time.fixedDeltaTime);
            }
            else
            {
                // Frenar bruscamente en Park, o inercia en Neutral/Drive
                float decel = (m_CurrentGear == GearState.Park) ? m_BrakeForce : m_IdleDeceleration;
                m_CurrentSpeedMs = Mathf.MoveTowards(m_CurrentSpeedMs, 0f, decel * Time.fixedDeltaTime);
            }

            m_CurrentSpeedMs = Mathf.Max(0f, m_CurrentSpeedMs);

            if (m_Rigidbody != null)
            {
                m_Rigidbody.WakeUp();

                // 1. Aplicar Giro de Dirección basado en VRSteeringWheel y velocidad de avance
                if (m_SteeringWheel != null && Mathf.Abs(m_SteeringWheel.SteeringValue) > 0.001f)
                {
                    // Si va en reversa, la rotación global se invierte visualmente
                    float directionSign = (m_CurrentGear == GearState.Reverse) ? -1f : 1f;
                    float speedFactor = m_ScaleTurnWithSpeed ? Mathf.Clamp01(m_CurrentSpeedMs / maxSpeedMs) : 1f;
                    float turnAmount = m_MaxTurnSpeed * m_SteeringWheel.SteeringValue * speedFactor * directionSign * Time.fixedDeltaTime;

                    Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);
                    m_Rigidbody.MoveRotation(m_Rigidbody.rotation * turnRotation);
                }

                // 2. Aplicar Desplazamiento Longitudinal
                Vector3 moveDir = GetForwardVector();
                if (m_CurrentGear == GearState.Reverse)
                {
                    moveDir = -moveDir;
                }

                if (m_Rigidbody.isKinematic)
                {
                    Vector3 deltaMove = moveDir * (m_CurrentSpeedMs * Time.fixedDeltaTime);
                    m_Rigidbody.MovePosition(m_Rigidbody.position + deltaMove);
                }
                else
                {
                    Vector3 forwardVel = moveDir * m_CurrentSpeedMs;
                    Vector3 currentVel = m_Rigidbody.linearVelocity;
                    m_Rigidbody.linearVelocity = new Vector3(forwardVel.x, currentVel.y, forwardVel.z);
                }
            }
        }

        private float ReadTriggerValue(InputActionProperty property, InputAction defaultAction, XRNode handNode)
        {
            float val = 0f;
            if (property.action != null && property.action.enabled)
                val = property.action.ReadValue<float>();

            if (val <= 0.0001f && defaultAction != null && defaultAction.enabled)
                val = defaultAction.ReadValue<float>();

            if (val <= 0.0001f)
            {
                var controller = (handNode == XRNode.LeftHand) ?
                    UnityEngine.InputSystem.XR.XRController.leftHand :
                    UnityEngine.InputSystem.XR.XRController.rightHand;

                if (controller != null)
                {
                    var triggerControl = controller.GetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("trigger");
                    if (triggerControl != null) val = triggerControl.ReadValue();
                }
            }

            if (val <= 0.0001f)
            {
                var device = InputDevices.GetDeviceAtXRNode(handNode);
                if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float devVal))
                    val = devVal;
            }

            return Mathf.Clamp01(val);
        }
    }
}
