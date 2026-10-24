using System;
using PrimerVolante.Evaluation;
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
    /// Datos de un contacto entre el auto del jugador y un obstáculo. Solo informa: la falta la reporta
    /// <see cref="ObstacleCollisionDetector"/> a <see cref="InfractionReporting"/> (gestor de infracciones, US22).
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

        /// <summary>
        /// Id de infracción (<see cref="InfractionIds"/>) que corresponde a un contacto: cono, vehículo o peatón
        /// según el tipo, y roce o choque según la gravedad. Un peatón es <see cref="InfractionIds.PedestrianHit"/>
        /// con cualquier gravedad.
        /// </summary>
        public static string InfractionIdFor(ObstacleKind kind, ObstacleImpactSeverity severity)
        {
            bool crash = severity == ObstacleImpactSeverity.Crash;
            switch (kind)
            {
                case ObstacleKind.Cone: return crash ? InfractionIds.ConeCrash : InfractionIds.ConeGraze;
                case ObstacleKind.Vehicle: return crash ? InfractionIds.VehicleCrash : InfractionIds.VehicleGraze;
                default: return InfractionIds.PedestrianHit;
            }
        }

        /// <summary>
        /// true si el auto empuja de frente contra la superficie de un contacto: el sentido de avance apunta contra
        /// la normal (que sale de la superficie hacia el auto) con un coseno menor a −<paramref name="minCosine"/>
        /// (0.9 ≈ dentro de 25° de la normal). Un contacto de costado, en diagonal o mientras el auto se separa no
        /// bloquea el avance: el solver lo desliza a lo largo de la superficie y ahí no hay que limitar la velocidad
        /// (la fricción del suelo, descontada de la velocidad medida, lo dejaría clavado).
        /// </summary>
        public static bool IsPressingInto(Vector3 travelDirection, Vector3 contactNormal, float minCosine = 0.9f)
        {
            if (travelDirection.sqrMagnitude < 1e-8f || contactNormal.sqrMagnitude < 1e-8f) return false;
            return Vector3.Dot(travelDirection.normalized, contactNormal.normalized) < -minCosine;
        }
    }

    /// <summary>
    /// Obstáculo que registra los roces y choques del auto del jugador (US33): conos, vehículos NPC y estacionados
    /// y peatones. Va en el mismo GameObject que el collider del obstáculo. Clasifica cada contacto como
    /// <see cref="ObstacleImpactSeverity.Graze"/> o <see cref="ObstacleImpactSeverity.Crash"/> y emite un evento
    /// por episodio de contacto. Cada impacto se informa como falta por <see cref="InfractionReporting"/> y además
    /// se emite el evento, que queda para reacciones (audio, NPC, futura UI).
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

        [Tooltip("Si el contacto se corta menos de estos segundos, al volver a tocar sigue siendo el mismo episodio: un roce sostenido " +
                 "parpadea (el contacto se pierde y se recupera) sin que eso cuente como un impacto nuevo.")]
        [SerializeField] private float m_ContactGraceSeconds = 0.3f;

        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log por cada impacto emitido.")]
        [SerializeField] private bool m_LogEvents = false;

        private NPCPathFollower m_Follower;
        private int m_ActiveContacts;
        private float m_LastImpactTime = float.NegativeInfinity;
        private float m_LastContactEndTime = float.NegativeInfinity;

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
            m_LastContactEndTime = float.NegativeInfinity;
        }

        private void OnValidate()
        {
            m_CrashSpeedKmh = Mathf.Max(0f, m_CrashSpeedKmh);
            m_RearmSeconds = Mathf.Max(0f, m_RearmSeconds);
            m_ContactGraceSeconds = Mathf.Max(0f, m_ContactGraceSeconds);
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

            // El contacto se cortó hace un instante: es el mismo episodio, no un impacto nuevo.
            if (now >= m_LastContactEndTime && now - m_LastContactEndTime < m_ContactGraceSeconds) return null;

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

            InfractionReporting.Report(InfractionReport.Create(
                ObstacleImpactMath.InfractionIdFor(impact.Kind, impact.Severity), this, impact.Point,
                ("speed", impact.PlayerSpeedKmh.ToString("F0"))));

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
            if (m_ActiveContacts == 0) m_LastContactEndTime = Time.fixedTime;
        }
    }
}
