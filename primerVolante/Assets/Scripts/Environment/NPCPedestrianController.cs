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

        [Header("Animación")]
        [Tooltip("Velocidad (m/s) a la que el clip Walking no desliza los pies. Es el umbral del blend tree; " +
                 "el default sale de escalar la medición de X Bot (1.79 m/s, cadera a 1.02 m) por la altura de cadera del personaje.")]
        [SerializeField] private float m_WalkReferenceSpeed = 1.89f;

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

        /// <summary>Velocidad de referencia del clip Walking en m/s.</summary>
        public float WalkReferenceSpeed => m_WalkReferenceSpeed;

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
        }

        private void OnEnable()
        {
            if (m_Customizer != null) m_Customizer.Changed += DisablePartColliders;
        }

        private void OnDisable()
        {
            if (m_Customizer != null) m_Customizer.Changed -= DisablePartColliders;
        }

        private void Start()
        {
            DisablePartColliders();
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

        private void Update()
        {
            if (m_Animator.runtimeAnimatorController == null) return;

            float speed = m_Follower.CurrentSpeed;
            m_Animator.SetFloat(s_SpeedHash, speed, m_SpeedDampTime, Time.deltaTime);
            m_Animator.speed = ComputeAnimatorSpeed(speed, m_WalkReferenceSpeed, m_MaxAnimatorSpeed);
        }

        /// <summary>
        /// Multiplicador de velocidad del Animator. Por debajo de la referencia el blend tree (Idle → Walking por
        /// <c>Speed</c>) ya acorta el paso en proporción a la velocidad, así que el Animator va a 1: escalarlo
        /// también compensaría dos veces y los pies patinarían. Por encima de la referencia el blend tree
        /// satura en Walking y el cadencia se escala con velocidad / referencia.
        /// </summary>
        public static float ComputeAnimatorSpeed(float speed, float referenceSpeed, float maxSpeed)
        {
            if (referenceSpeed <= 0f || speed <= referenceSpeed) return 1f;
            return Mathf.Min(speed / referenceSpeed, Mathf.Max(1f, maxSpeed));
        }
    }
}
