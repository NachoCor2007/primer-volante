using PrimerVolante.Characters;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Anima a un peatón NPC según la velocidad del <see cref="NPCPathFollower"/>: escribe el parámetro
    /// <c>Speed</c> (m/s) del Animator Controller (blend tree Idle → Walking) y ajusta la velocidad del
    /// Animator para que los pies no se deslicen cuando la velocidad difiere de la de referencia del clip.
    /// </summary>
    [RequireComponent(typeof(NPCPathFollower))]
    [RequireComponent(typeof(Animator))]
    public class NPCPedestrianController : MonoBehaviour
    {
        /// <summary>Nombre del parámetro float del Animator Controller con la velocidad en m/s.</summary>
        public const string SpeedParameter = "Speed";

        private static readonly int s_SpeedHash = Animator.StringToHash(SpeedParameter);

        /// <summary>
        /// Velocidad (m/s) a la que el blend tree llega al clip Walking a peso 1. Es el umbral del Walking en el
        /// Animator Controller: por encima de ella solo se escala la cadencia, así el largo de paso es el del clip.
        /// </summary>
        public const float WalkBlendEndSpeed = 0.35f;

        [Header("Animación")]
        [Tooltip("Velocidad (m/s) del cuerpo con la que el clip Walking, reproducido a velocidad 1, no desliza los pies. " +
                 "El setup la mide sobre el esqueleto del personaje (retroceso del pie en apoyo).")]
        [SerializeField] private float m_WalkReferenceSpeed = 1.6f;

        [Tooltip("Tiempo de suavizado del parámetro Speed en segundos.")]
        [SerializeField] private float m_SpeedDampTime = 0.1f;

        [Tooltip("Velocidad máxima del Animator cuando se camina más rápido que la referencia (evita pasos frenéticos).")]
        [SerializeField] private float m_MaxAnimatorSpeed = 2f;

        [Header("Colisión")]
        [Tooltip("Único collider activo del peatón. Los de las piezas de la ropa (torso, gorra, lentes) se desactivan, " +
                 "incluidos los que el UrbanManCustomizer instancia al iniciar.")]
        [SerializeField] private Collider m_BodyCollider;

        private NPCPathFollower m_Follower;
        private Animator m_Animator;
        private UrbanManCustomizer m_Customizer;
        private NPCSafetyStop m_SafetyStop;

        /// <summary>Velocidad de referencia del clip Walking en m/s.</summary>
        public float WalkReferenceSpeed => m_WalkReferenceSpeed;

        /// <summary>Velocidad máxima del Animator (cadencia máxima respecto del clip).</summary>
        public float MaxAnimatorSpeed => m_MaxAnimatorSpeed;

        /// <summary>Velocidad de crucero por defecto de un peatón en m/s.</summary>
        public const float DefaultCruiseSpeed = 1.4f;

        private void Reset()
        {
            var animator = GetComponent<Animator>();
            if (animator != null) animator.applyRootMotion = false;
        }

        private void Awake()
        {
            m_Follower = GetComponent<NPCPathFollower>();
            m_Animator = GetComponent<Animator>();
            m_Animator.applyRootMotion = false;
            m_Customizer = GetComponent<UrbanManCustomizer>();
            m_SafetyStop = GetComponent<NPCSafetyStop>();
            EnsureNPCLayer();
        }

        private void OnEnable()
        {
            if (m_Customizer != null)
            {
                m_Customizer.Changed += DisablePartColliders;
                m_Customizer.Changed += EnsureNPCLayer;
            }
        }

        private void OnDisable()
        {
            if (m_Customizer != null)
            {
                m_Customizer.Changed -= DisablePartColliders;
                m_Customizer.Changed -= EnsureNPCLayer;
            }
        }

        private void Start()
        {
            if (m_SafetyStop == null) m_SafetyStop = GetComponent<NPCSafetyStop>();
            DisablePartColliders();
            EnsureNPCLayer();
        }

        /// <summary>
        /// Deja activo solo <see cref="m_BodyCollider"/>: los colliders de las piezas (con tamaños de T-pose)
        /// harían un compuesto que no cubre el cuerpo animado.
        /// </summary>
        private void DisablePartColliders()
        {
            if (m_BodyCollider == null) return;

            foreach (Collider part in GetComponentsInChildren<Collider>(true))
            {
                if (part != m_BodyCollider) part.enabled = false;
            }
        }

        private void EnsureNPCLayer()
        {
            int npcLayer = LayerMask.NameToLayer("NPC");
            if (npcLayer == -1) return;

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = npcLayer;
            }
        }

        private void Update()
        {
            if (m_Animator.runtimeAnimatorController == null) return;

            bool isEmergency = (m_SafetyStop != null && m_SafetyStop.IsEmergencyBraking) ||
                               (m_Follower != null && m_Follower.IsEmergencyStopped);

            if (isEmergency)
            {
                m_Animator.SetFloat(s_SpeedHash, 0f);
                m_Animator.speed = 1f;
                return;
            }

            float speed = m_Follower != null ? m_Follower.CurrentSpeed : 0f;
            m_Animator.SetFloat(s_SpeedHash, speed, m_SpeedDampTime, Time.deltaTime);
            m_Animator.speed = ComputeAnimatorSpeed(speed, m_WalkReferenceSpeed, m_MaxAnimatorSpeed);
        }

        /// <summary>
        /// Multiplicador de velocidad del Animator. Con el Walking a peso 1 (desde
        /// <see cref="WalkBlendEndSpeed"/>) el largo de paso es fijo y para que el pie en apoyo vaya a la velocidad
        /// del cuerpo la cadencia debe ser velocidad / referencia. Por debajo se interpola desde 1 (Idle a ritmo
        /// normal en reposo) para que no haya saltos.
        /// </summary>
        public static float ComputeAnimatorSpeed(float speed, float referenceSpeed, float maxSpeed)
        {
            if (referenceSpeed <= 0f) return 1f;

            float cadence = speed / referenceSpeed;
            if (speed >= WalkBlendEndSpeed) return Mathf.Min(cadence, Mathf.Max(1f, maxSpeed));

            float atBlendEnd = WalkBlendEndSpeed / referenceSpeed;
            return Mathf.Lerp(1f, atBlendEnd, Mathf.Max(0f, speed) / WalkBlendEndSpeed);
        }
    }
}
