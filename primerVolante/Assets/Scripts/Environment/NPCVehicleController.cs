using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Parte visual de un vehículo NPC: giro de las ruedas según la distancia recorrida y dirección de las
    /// ruedas delanteras con el modelo de bicicleta inverso. No implementa luces (eso es de TASK-107);
    /// solo expone <see cref="CurrentSpeed"/> e <see cref="IsBraking"/> para conectarlas después.
    /// </summary>
    [RequireComponent(typeof(NPCPathFollower))]
    public class NPCVehicleController : MonoBehaviour
    {
        // Constante de tiempo del suavizado del ángulo de dirección (s).
        private const float k_SteerSmoothingTime = 0.12f;

        // Por debajo de esta velocidad se conserva el último ángulo de dirección (m/s).
        private const float k_MinSteerSpeed = 0.3f;

        [Header("Ruedas")]
        [Tooltip("Ruedas delanteras: giran sobre su eje y además se orientan con la dirección.")]
        [SerializeField] private Transform[] m_FrontWheels;

        [Tooltip("Ruedas traseras (2 o 4): solo giran. Se autodetectan por nombre.")]
        [SerializeField] private Transform[] m_RearWheels;

        [Tooltip("Radio de rueda en metros (mundo). Si es 0 se calcula desde los bounds del mesh × escala.")]
        [SerializeField] private float m_WheelRadius = 0f;

        [Tooltip("Distancia entre ejes en metros (mundo). Si es 0 se mide desde las ruedas.")]
        [SerializeField] private float m_WheelBase = 0f;

        [Header("Dirección")]
        [Tooltip("Ángulo máximo de las ruedas delanteras en grados.")]
        [SerializeField] private float m_MaxSteerAngle = 35f;

        [Header("Frenado")]
        [Tooltip("Desaceleración (m/s^2) por encima de la cual se considera que el vehículo está frenando.")]
        [SerializeField] private float m_BrakingThreshold = 0.3f;

        private NPCPathFollower m_Follower;
        private Transform[] m_AllWheels;
        private Quaternion[] m_BaseRotations;
        private bool[] m_IsFront;
        private float m_RollAngle;       // rad acumulados
        private float m_LastFixedSpeed;  // m/s en el último FixedUpdate
        private float m_SteerAngle;      // grados, ya suavizado
        private float m_SteerTarget;     // grados
        private bool m_Initialized;

        /// <summary>Velocidad actual en m/s.</summary>
        public float CurrentSpeed => m_Follower != null ? m_Follower.CurrentSpeed : 0f;

        /// <summary>true si está desacelerando por encima del umbral configurado.</summary>
        public bool IsBraking => m_Follower != null && m_Follower.CurrentAcceleration < -m_BrakingThreshold;

        /// <summary>Ángulo actual de las ruedas delanteras en grados (positivo = derecha).</summary>
        public float CurrentSteerAngle => m_SteerAngle;

        /// <summary>Radio de rueda en metros (mundo).</summary>
        public float WheelRadius => m_WheelRadius;

        /// <summary>Distancia entre ejes en metros (mundo).</summary>
        public float WheelBase => m_WheelBase;

        /// <summary>Ruedas delanteras asignadas.</summary>
        public IReadOnlyList<Transform> FrontWheels => m_FrontWheels;

        /// <summary>Ruedas traseras asignadas.</summary>
        public IReadOnlyList<Transform> RearWheels => m_RearWheels;

        /// <summary>
        /// Ángulo de dirección del modelo de bicicleta inverso: δ = atan(L · ω / v), limitado a ±maxAngle.
        /// Devuelve grados; positivo al doblar a la derecha.
        /// </summary>
        public static float ComputeSteerAngle(float wheelBase, float yawRate, float speed, float maxAngleDeg)
        {
            if (speed <= 0f) return 0f;
            float angle = Mathf.Atan(wheelBase * yawRate / speed) * Mathf.Rad2Deg;
            return Mathf.Clamp(angle, -maxAngleDeg, maxAngleDeg);
        }

        private void Reset()
        {
            AutoAssignWheels();
        }

        private void Awake()
        {
            m_Follower = GetComponent<NPCPathFollower>();
            Initialize();
        }

        /// <summary>
        /// Autodetecta las ruedas por nombre (Wheel_FL / Wheel_FR delanteras; el resto de Wheel_* traseras)
        /// y calcula radio y distancia entre ejes.
        /// </summary>
        public void AutoAssignWheels()
        {
            var front = new List<Transform>();
            var rear = new List<Transform>();

            foreach (Transform wheel in GetComponentsInChildren<Transform>(true))
            {
                if (wheel == transform || !wheel.name.StartsWith("Wheel_")) continue;
                if (wheel.name == "Wheel_FL" || wheel.name == "Wheel_FR") front.Add(wheel);
                else rear.Add(wheel);
            }

            m_FrontWheels = front.ToArray();
            m_RearWheels = rear.ToArray();
            m_WheelRadius = 0f;
            m_WheelBase = 0f;
            MeasureGeometry();
        }

        /// <summary>
        /// Calcula (si faltan) el radio de rueda desde los bounds del mesh × escala y la distancia entre ejes
        /// desde las posiciones de las ruedas, en metros de mundo.
        /// </summary>
        public void MeasureGeometry()
        {
            if (m_WheelRadius <= 0f)
            {
                Transform sample = m_FrontWheels != null && m_FrontWheels.Length > 0 ? m_FrontWheels[0] : null;
                var filter = sample != null ? sample.GetComponent<MeshFilter>() : null;
                if (filter != null && filter.sharedMesh != null)
                {
                    Vector3 size = filter.sharedMesh.bounds.size;
                    Vector3 scale = sample.lossyScale;
                    // El eje de giro es X local: el diámetro es el mayor de las otras dos dimensiones.
                    float diameter = Mathf.Max(size.y * Mathf.Abs(scale.y), size.z * Mathf.Abs(scale.z));
                    m_WheelRadius = diameter * 0.5f;
                }
            }

            if (m_WheelBase <= 0f && m_FrontWheels != null && m_FrontWheels.Length > 0 &&
                m_RearWheels != null && m_RearWheels.Length > 0)
            {
                // Eje delantero a promedio de los ejes traseros (en los camiones de 2 ejes traseros).
                float frontZ = AverageForwardCoordinate(m_FrontWheels);
                float rearZ = AverageForwardCoordinate(m_RearWheels);
                m_WheelBase = Mathf.Abs(frontZ - rearZ);
            }
        }

        private float AverageForwardCoordinate(Transform[] wheels)
        {
            float sum = 0f;
            foreach (Transform wheel in wheels)
            {
                sum += Vector3.Dot(wheel.position - transform.position, transform.forward);
            }

            return sum / wheels.Length;
        }

        private void Initialize()
        {
            if (m_Initialized) return;

            MeasureGeometry();

            var all = new List<Transform>();
            var isFront = new List<bool>();
            AddWheels(m_FrontWheels, true, all, isFront);
            AddWheels(m_RearWheels, false, all, isFront);
            m_AllWheels = all.ToArray();
            m_IsFront = isFront.ToArray();

            m_BaseRotations = new Quaternion[m_AllWheels.Length];
            for (int i = 0; i < m_AllWheels.Length; i++)
            {
                m_BaseRotations[i] = m_AllWheels[i].localRotation;
            }

            if (m_WheelRadius <= 0f)
            {
                Debug.LogWarning($"[NPCVehicleController] ⚠️ {name}: no se pudo determinar el radio de rueda; las ruedas no giran.");
            }

            m_Initialized = true;
        }

        private static void AddWheels(Transform[] source, bool front, List<Transform> wheels, List<bool> isFront)
        {
            if (source == null) return;
            foreach (Transform wheel in source)
            {
                if (wheel == null) continue;
                wheels.Add(wheel);
                isFront.Add(front);
            }
        }

        private void FixedUpdate()
        {
            if (!m_Initialized) return;

            float dt = Time.fixedDeltaTime;
            float speed = m_Follower.CurrentSpeed;

            if (m_WheelRadius > 0f)
            {
                m_RollAngle = Mathf.Repeat(m_RollAngle + speed * dt / m_WheelRadius, Mathf.PI * 2f);
            }

            // Con v ≈ 0 el ángulo inverso no está definido: se conserva el último.
            if (speed > k_MinSteerSpeed && m_WheelBase > 0f)
            {
                m_SteerTarget = ComputeSteerAngle(m_WheelBase, m_Follower.CurrentYawRate, speed, m_MaxSteerAngle);
            }

            float blend = 1f - Mathf.Exp(-dt / k_SteerSmoothingTime);
            m_SteerAngle = Mathf.Lerp(m_SteerAngle, m_SteerTarget, blend);
            m_LastFixedSpeed = speed;
        }

        private void LateUpdate()
        {
            if (!m_Initialized || m_AllWheels.Length == 0) return;

            // Extrapola el giro desde el último paso fijo para que la rotación sea fluida entre pasos.
            float sinceFixed = Mathf.Clamp(Time.time - Time.fixedTime, 0f, Time.fixedDeltaTime);
            float roll = m_RollAngle;
            if (m_WheelRadius > 0f)
            {
                roll += m_LastFixedSpeed * sinceFixed / m_WheelRadius;
            }

            Quaternion rollRotation = Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.right);
            Quaternion steerRotation = Quaternion.AngleAxis(m_SteerAngle, Vector3.up);

            for (int i = 0; i < m_AllWheels.Length; i++)
            {
                // Rotaciones en el espacio del padre (X = giro, Y = dirección), antepuestas a la pose base.
                Quaternion applied = m_IsFront[i] ? steerRotation * rollRotation : rollRotation;
                m_AllWheels[i].localRotation = applied * m_BaseRotations[i];
            }
        }
    }
}
