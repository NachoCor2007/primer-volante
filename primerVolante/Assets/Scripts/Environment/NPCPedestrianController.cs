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

        [Tooltip("Velocidad mínima del Animator mientras camina (evita pasos en cámara lenta).")]
        [SerializeField] private float m_MinAnimatorSpeed = 0.6f;

        [Tooltip("Velocidad máxima del Animator (evita pasos frenéticos si el crucero supera mucho la referencia).")]
        [SerializeField] private float m_MaxAnimatorSpeed = 2f;

        private NPCPathFollower m_Follower;
        private Animator m_Animator;

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
        }

        private void Update()
        {
            if (m_Animator.runtimeAnimatorController == null) return;

            float speed = m_Follower.CurrentSpeed;
            m_Animator.SetFloat(s_SpeedHash, speed, m_SpeedDampTime, Time.deltaTime);
            m_Animator.speed = ComputeAnimatorSpeed(speed, m_WalkReferenceSpeed, m_MinAnimatorSpeed, m_MaxAnimatorSpeed);
        }

        /// <summary>
        /// Multiplicador de velocidad del Animator: velocidad / referencia mientras camina (para que el paso
        /// acompañe al desplazamiento), 1 cuando está parado (el Idle sigue a ritmo normal).
        /// </summary>
        public static float ComputeAnimatorSpeed(float speed, float referenceSpeed, float minSpeed, float maxSpeed)
        {
            if (speed < 0.05f || referenceSpeed <= 0f) return 1f;
            return Mathf.Clamp(speed / referenceSpeed, minSpeed, maxSpeed);
        }
    }
}
