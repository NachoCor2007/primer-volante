using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controlador cinemático-híbrido de movimiento longitudinal de vehículo para VR.
    /// Maneja Aceleración (Gatillo Derecho) y Frenado (Gatillo Izquierdo).
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

        [Header("Enganche de Dirección (Futuro)")]
        [Tooltip("Componente de volante opcional para enganche futuro de dirección.")]
        [SerializeField] private VRSteeringWheel m_SteeringWheel;

        [Header("Debugging / Logs de Gatillos")]
        [Tooltip("Si se activa, imprime mensajes en la Consola de Unity al presionar los gatillos.")]
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
        /// Referencia al volante asignado (preparado para dirección).
        /// </summary>
        public VRSteeringWheel SteeringWheel
        {
            get => m_SteeringWheel;
            set => m_SteeringWheel = value;
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
                m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            EnsureCollider();

            if (m_SteeringWheel == null)
            {
                m_SteeringWheel = GetComponentInChildren<VRSteeringWheel>();
            }

            if (Application.isPlaying && m_EnableDebugLogs)
            {
                Debug.Log($"[VehicleController] 🚗 Inicializado en '{gameObject.name}'. Rigidbody (isKinematic={m_Rigidbody?.isKinematic}), Collider: {GetComponent<Collider>()?.GetType().Name}");
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

            // Loggeo en Consola de Unity incluyendo posición 3D actual
            if (m_EnableDebugLogs && Time.time - m_LastLogTime >= m_LogInterval)
            {
                if (m_BrakeValue > 0.01f)
                {
                    Debug.Log($"[VehicleController] 🛑 FRENO: {m_BrakeValue * 100f:F1}% | Vel: {CurrentSpeedKmh:F1} km/h | Pos: {transform.position}");
                    m_LastLogTime = Time.time;
                }
                else if (m_ThrottleValue > 0.01f)
                {
                    Debug.Log($"[VehicleController] 🏎️ ACELERADOR: {m_ThrottleValue * 100f:F1}% | Vel: {CurrentSpeedKmh:F1} km/h | Pos: {transform.position} | Forward: {GetForwardVector()}");
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
                // Aplicar desaceleración de freno
                float decel = m_BrakeForce * m_BrakeValue;
                m_CurrentSpeedMs = Mathf.MoveTowards(m_CurrentSpeedMs, 0f, decel * Time.fixedDeltaTime);
            }
            else if (m_ThrottleValue > 0.01f)
            {
                // Aplicar aceleración proporcional
                float targetSpeed = maxSpeedMs * m_ThrottleValue;
                m_CurrentSpeedMs = Mathf.MoveTowards(m_CurrentSpeedMs, targetSpeed, m_AccelerationRate * Time.fixedDeltaTime);
            }
            else
            {
                // Desaceleración pasiva por inercia / freno de motor
                m_CurrentSpeedMs = Mathf.MoveTowards(m_CurrentSpeedMs, 0f, m_IdleDeceleration * Time.fixedDeltaTime);
            }

            m_CurrentSpeedMs = Mathf.Max(0f, m_CurrentSpeedMs);

            if (m_Rigidbody != null)
            {
                m_Rigidbody.WakeUp();
                Vector3 moveDir = GetForwardVector();
                Vector3 deltaMove = moveDir * (m_CurrentSpeedMs * Time.fixedDeltaTime);

                if (m_Rigidbody.isKinematic)
                {
                    // Si el Rigidbody es cinemático, desplazar directamente con MovePosition
                    m_Rigidbody.MovePosition(m_Rigidbody.position + deltaMove);
                }
                else
                {
                    // Aplicar velocidad lineal + MovePosition de respaldo para garantizar desplazamiento
                    Vector3 forwardVel = moveDir * m_CurrentSpeedMs;
                    Vector3 currentVel = m_Rigidbody.linearVelocity;
                    m_Rigidbody.linearVelocity = new Vector3(forwardVel.x, currentVel.y, forwardVel.z);

                    if (m_CurrentSpeedMs > 0.01f)
                    {
                        m_Rigidbody.MovePosition(m_Rigidbody.position + deltaMove);
                    }
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
