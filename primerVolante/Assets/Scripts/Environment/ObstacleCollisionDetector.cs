using System;
using PrimerVolante.VR;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Tipo de obstáculo que puede tocar el auto del jugador (US33).
    /// </summary>
    public enum ObstacleKind
    {
        /// <summary>Cono de tránsito fijo.</summary>
        Cone = 0,

        /// <summary>Vehículo: NPC o auto estacionado.</summary>
        Vehicle = 1,

        /// <summary>Peatón NPC.</summary>
        Pedestrian = 2
    }

    /// <summary>
    /// Gravedad de un contacto con un obstáculo.
    /// </summary>
    public enum ObstacleImpactSeverity
    {
        /// <summary>Roce: contacto a baja velocidad (por debajo del umbral del obstáculo).</summary>
        Graze = 0,

        /// <summary>Choque: contacto a velocidad igual o mayor al umbral. Un peatón siempre es choque.</summary>
        Crash = 1
    }

    /// <summary>
    /// Datos de un contacto entre el auto del jugador y un obstáculo. Solo informa: registrar la falta es
    /// responsabilidad de quien se suscriba (gestor de infracciones, US22).
    /// </summary>
    public struct ObstacleImpact
    {
        /// <summary>Obstáculo que fue tocado.</summary>
        public ObstacleCollisionDetector Obstacle;

        /// <summary>Auto del jugador que lo tocó.</summary>
        public VehicleController Vehicle;

        /// <summary>Tipo de obstáculo.</summary>
        public ObstacleKind Kind;

        /// <summary>Gravedad del contacto, según la velocidad del jugador.</summary>
        public ObstacleImpactSeverity Severity;

        /// <summary>Velocidad del jugador en km/h justo antes del contacto: es la que decide la gravedad.</summary>
        public float PlayerSpeedKmh;

        /// <summary>Velocidad relativa de los dos cuerpos en km/h según el solver (informativa).</summary>
        public float RelativeSpeedKmh;

        /// <summary>
        /// Velocidad del obstáculo en km/h: la del NPC según su <see cref="NPCPathFollower"/>, 0 en un obstáculo
        /// estático. Permite saber después si el NPC venía en movimiento (atribución de culpa, US31).
        /// </summary>
        public float ObstacleSpeedKmh;

        /// <summary>Punto de contacto en coordenadas de mundo.</summary>
        public Vector3 Point;

        /// <summary>Instante del contacto en <see cref="UnityEngine.Time.fixedTime"/>.</summary>
        public float Time;
    }

    /// <summary>
    /// Funciones puras (sin escena ni estado) de la clasificación de contactos con obstáculos. Son deterministas
    /// y testeables en el Editor, igual que <see cref="LessonBoundaryMath"/>. Para el rearme entre eventos se usa
    /// <see cref="LessonBoundaryMath.ShouldAlert"/> (misma semántica).
    /// </summary>
    public static class ObstacleImpactMath
    {
        /// <summary>
        /// Clasifica un contacto: un peatón siempre es <see cref="ObstacleImpactSeverity.Crash"/>; el resto es
        /// <see cref="ObstacleImpactSeverity.Graze"/> si la velocidad del jugador es menor al umbral y
        /// <see cref="ObstacleImpactSeverity.Crash"/> si es igual o mayor.
        /// </summary>
        public static ObstacleImpactSeverity Classify(ObstacleKind kind, float playerSpeedKmh, float crashThresholdKmh)
        {
            if (kind == ObstacleKind.Pedestrian) return ObstacleImpactSeverity.Crash;
            return playerSpeedKmh < crashThresholdKmh ? ObstacleImpactSeverity.Graze : ObstacleImpactSeverity.Crash;
        }
    }

    /// <summary>
    /// Obstáculo que registra los roces y choques del auto del jugador (US33): conos, vehículos NPC y estacionados
    /// y peatones. Va en el mismo GameObject que el collider del obstáculo. Clasifica cada contacto como
    /// <see cref="ObstacleImpactSeverity.Graze"/> o <see cref="ObstacleImpactSeverity.Crash"/> y emite un evento
    /// por episodio de contacto. Solo emite el evento: la falta y la reacción de los NPC no son responsabilidad
    /// de este componente.
    /// </summary>
    /// <remarks>
    /// No implementa <c>OnCollisionEnter</c>: los contactos se los informa <see cref="ObstacleContactSensor"/>, que
    /// vive en el Rigidbody dinámico del auto. Los NPC son cinemáticos y los conos y autos decorativos son colliders
    /// estáticos, así que el lado fiable para recibir el mensaje es el del auto. Con un único camino no se duplican eventos.
    /// </remarks>
    [DisallowMultipleComponent]
    public class ObstacleCollisionDetector : MonoBehaviour
    {
        private const float k_KmhPerMs = 3.6f;

        [Header("Obstáculo")]
        [Tooltip("Tipo de obstáculo. Un peatón es siempre choque.")]
        [SerializeField] private ObstacleKind m_Kind = ObstacleKind.Cone;

        [Tooltip("Velocidad del jugador en km/h a partir de la cual el contacto es un choque. Por debajo es un roce.")]
        [SerializeField] private float m_CrashSpeedKmh = 5f;

        [Tooltip("Segundos mínimos entre dos eventos del mismo obstáculo.")]
        [SerializeField] private float m_RearmSeconds = 1f;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por cada impacto emitido.")]
        [SerializeField] private bool m_LogEvents = false;

        private NPCPathFollower m_Follower;
        private int m_ActiveContacts;
        private float m_LastImpactTime = float.NegativeInfinity;

        /// <summary>Se dispara una vez por episodio de contacto con el auto del jugador.</summary>
        public event Action<ObstacleImpact> OnImpact;

        /// <summary>
        /// Se dispara por cada impacto de cualquier obstáculo: el gestor de faltas se suscribe una sola vez sin
        /// buscar todos los obstáculos de la escena. Se limpia al entrar a Play Mode.
        /// </summary>
        public static event Action<ObstacleImpact> AnyImpact;

        /// <summary>Tipo de obstáculo.</summary>
        public ObstacleKind Kind => m_Kind;

        /// <summary>Umbral roce/choque en km/h.</summary>
        public float CrashSpeedKmh => m_CrashSpeedKmh;

        /// <summary>Segundos mínimos entre dos eventos.</summary>
        public float RearmSeconds => m_RearmSeconds;

        /// <summary>Cantidad de colliders del auto que lo están tocando ahora.</summary>
        public int ActiveContacts => m_ActiveContacts;

        /// <summary>
        /// Velocidad actual del obstáculo en km/h: la del <see cref="NPCPathFollower"/> si lo tiene, 0 si no.
        /// </summary>
        public float ObstacleSpeedKmh => m_Follower != null ? m_Follower.CurrentSpeed * k_KmhPerMs : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AnyImpact = null;
        }

        private void Awake()
        {
            m_Follower = GetComponent<NPCPathFollower>();
        }

        private void OnDisable()
        {
            // Si el collider se apaga, OnCollisionExit puede no llegar: el contador no puede quedar pegado.
            m_ActiveContacts = 0;
        }

        private void OnValidate()
        {
            m_CrashSpeedKmh = Mathf.Max(0f, m_CrashSpeedKmh);
            m_RearmSeconds = Mathf.Max(0f, m_RearmSeconds);
        }

        /// <summary>
        /// Un collider del auto empezó a tocar el obstáculo. Solo el paso de 0 a 1 contactos abre un episodio y,
        /// si ya pasó el rearme, emite el impacto. Devuelve el impacto emitido o <c>null</c> si no se emitió
        /// ninguno, para que quien llama sepa si debe sonar.
        /// </summary>
        public ObstacleImpact? BeginContact(VehicleController vehicle, Collision collision)
        {
            m_ActiveContacts++;
            if (m_ActiveContacts != 1) return null;

            // Los avisos llegan desde callbacks de física: se mide con el reloj fijo.
            float now = Time.fixedTime;
            if (!LessonBoundaryMath.ShouldAlert(m_LastImpactTime, now, m_RearmSeconds)) return null;

            m_LastImpactTime = now;

            float playerSpeedKmh = vehicle != null ? vehicle.CurrentSpeedKmh : 0f;
            Vector3 point = transform.position;
            float relativeSpeedKmh = 0f;
            if (collision != null)
            {
                relativeSpeedKmh = collision.relativeVelocity.magnitude * k_KmhPerMs;
                if (collision.contactCount > 0) point = collision.GetContact(0).point;
            }

            var impact = new ObstacleImpact
            {
                Obstacle = this,
                Vehicle = vehicle,
                Kind = m_Kind,
                Severity = ObstacleImpactMath.Classify(m_Kind, playerSpeedKmh, m_CrashSpeedKmh),
                PlayerSpeedKmh = playerSpeedKmh,
                RelativeSpeedKmh = relativeSpeedKmh,
                ObstacleSpeedKmh = ObstacleSpeedKmh,
                Point = point,
                Time = now,
            };

            if (m_LogEvents)
            {
                Debug.Log($"[ObstacleCollisionDetector] 💥 {name}: {impact.Kind}/{impact.Severity} | jugador {impact.PlayerSpeedKmh:F1} km/h, " +
                          $"relativa {impact.RelativeSpeedKmh:F1} km/h, obstáculo {impact.ObstacleSpeedKmh:F1} km/h | {impact.Point}");
            }

            OnImpact?.Invoke(impact);
            AnyImpact?.Invoke(impact);
            return impact;
        }

        /// <summary>
        /// Un collider del auto dejó de tocar el obstáculo. El contador no baja de 0.
        /// </summary>
        public void EndContact(VehicleController vehicle)
        {
            if (m_ActiveContacts > 0) m_ActiveContacts--;
        }
    }
}
