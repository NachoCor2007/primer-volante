using System;
using PrimerVolante.Evaluation;
using PrimerVolante.VR;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Detector de circulación sin luces bajas. Va en la raíz del auto del jugador, junto al
    /// <c>VehicleController</c>. Sigue el estado del motor (<see cref="VehicleController.OnEngineStateChanged"/>) y,
    /// en cada paso de física, alimenta al <see cref="LowBeamTracker"/> con el modo de luces, la distancia recorrida
    /// y el tiempo. Si el tracker devuelve una falta, informa <see cref="InfractionIds.LowBeam"/> con los parámetros
    /// <c>when</c> y <c>lights</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La distancia es el desplazamiento neto real del Rigidbody (en XZ) desde un ancla, adelante o en reversa (no su
    /// velocidad: el <c>VehicleController</c> escribe la velocidad comandada aunque el auto esté contra una valla).
    /// Solo cuenta cuando el auto se alejó al menos <c>k_AnchorStepM</c> del ancla, y entonces el ancla se mueve: la
    /// vibración milimétrica contra un obstáculo o el asentamiento de la física no suman. Un salto mayor a
    /// <c>k_TeleportStepM</c> (reset o teletransporte) mueve el ancla sin sumar.
    /// </para>
    /// <para>
    /// Suspendido (<see cref="SetSuppressed"/>) no acumula distancia ni tiempo ni bloquea una falta, pero sigue
    /// observando las bajas: circular con LowBeam rearma y marca que ya circuló. El detector escucha los eventos del
    /// motor desde <c>Awake</c> hasta <c>OnDestroy</c>, también deshabilitado, así que ningún ciclo del motor se
    /// pierde; <c>OnEnable</c> solo resincroniza. Sin allocations por frame, salvo el reporte cuando hay falta.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class LowBeamDetector : MonoBehaviour
    {
        // El auto debe alejarse al menos esto (m) del ancla para que cuente como distancia. El Rigidbody nunca queda
        // en 0 exacto (asentamiento, vibración contra una valla) y ese ruido no debe contar como manejo.
        private const float k_AnchorStepM = 0.25f;

        // Un desplazamiento mayor a esto (m) respecto del ancla en un solo paso de física es un teletransporte o un reset.
        private const float k_TeleportStepM = 3f;

        [Header("Detección")]
        [Tooltip("Metros recorridos sin las luces bajas (con el motor en marcha) a partir de los cuales hay falta, junto con el tiempo mínimo.")]
        [Min(0.1f)]
        [SerializeField] private float m_ThresholdM = 2f;

        [Tooltip("Segundos seguidos sin las luces bajas (con el motor en marcha) a partir de los cuales hay falta, junto con la distancia. Evita que un parpadeo de altas a alta velocidad sea falta.")]
        [Min(0f)]
        [SerializeField] private float m_MinTimeS = 1f;

        [Header("Depuración")]
        [Tooltip("Escribe los logs [LowBeamDetector] de motor, falta y rearme.")]
        [SerializeField] private bool m_LogEvents = false;

        private Rigidbody m_Body;
        private VehicleController m_Vehicle;
        private LowBeamTracker m_Tracker;
        private readonly SuppressionSet m_Suppression = new SuppressionSet("[LowBeamDetector]");
        private Vector3 m_Anchor;
        private bool m_WarnedNoVehicle;
        private bool m_Subscribed;

        /// <summary>
        /// Solo para tests: si no es null, reemplaza la lectura de <c>VehicleController.CurrentHeadlights</c>. Permite
        /// probar el detector sin un <c>VehicleLightingController</c> que se enganche a la escena abierta.
        /// </summary>
        public Func<HeadlightMode> HeadlightSource { get; set; }

        /// <summary>
        /// true mientras algún dueño tenga suspendido el detector (ver <see cref="SetSuppressed"/>). Los dueños
        /// que son objetos de Unity ya destruidos se descartan solos.
        /// </summary>
        public bool IsSuppressed => m_Suppression.IsActive;

        /// <summary>Última falta informada (default si todavía no hubo ninguna).</summary>
        public LowBeamFault LastFault { get; private set; }

        /// <summary>Metros recorridos sin bajas desde el último rearme.</summary>
        public float AccumulatedM => m_Tracker != null ? m_Tracker.AccumulatedM : 0f;

        /// <summary>Segundos seguidos sin bajas desde el último rearme.</summary>
        public float AccumulatedS => m_Tracker != null ? m_Tracker.AccumulatedS : 0f;

        /// <summary>true si ya se informó la falta del tramo actual.</summary>
        public bool IsLatched => m_Tracker != null && m_Tracker.IsLatched;

        /// <summary>true si en este encendido ya se recorrió distancia con las bajas puestas.</summary>
        public bool HasDrivenWithLowBeam => m_Tracker != null && m_Tracker.HasDrivenWithLowBeam;

        /// <summary>Metros sin bajas a partir de los cuales hay falta.</summary>
        public float ThresholdM
        {
            get => m_ThresholdM;
            set
            {
                m_ThresholdM = Mathf.Max(0.1f, value);
                SyncTracker();
            }
        }

        /// <summary>Segundos sin bajas a partir de los cuales hay falta.</summary>
        public float MinTimeS
        {
            get => m_MinTimeS;
            set
            {
                m_MinTimeS = Mathf.Max(0f, value);
                SyncTracker();
            }
        }

        private LowBeamTracker Tracker
        {
            get
            {
                if (m_Tracker == null)
                {
                    m_Tracker = new LowBeamTracker();
                    SyncTracker();
                }

                return m_Tracker;
            }
        }

        /// <summary>
        /// Descarta todo el rastreo (acumulados, bloqueo y "ya circuló con bajas"). Después, si el motor está en
        /// marcha, vuelve a contar como un encendido nuevo. Lo debe llamar quien reinicie la simulación (por ejemplo
        /// <c>VehicleController.ResetToBaseState</c> desde un futuro gestor de estado).
        /// </summary>
        public void ResetTracking()
        {
            Tracker.Reset();
            ResetAnchor();
            if (m_Vehicle != null && m_Vehicle.IsEngineRunning) Tracker.EngineStarted();
        }

        /// <summary>
        /// Suspende o reanuda el detector en nombre de <paramref name="owner"/>. El detector está suspendido
        /// mientras algún dueño lo tenga suspendido. Suspendido no acumula distancia ni tiempo ni bloquea una falta;
        /// poner las bajas igual rearma y marca que ya circuló.
        /// </summary>
        public void SetSuppressed(object owner, bool suppressed)
        {
            m_Suppression.Set(owner, suppressed);
        }

        // Se suscribe al motor acá (y no en OnEnable) para no perder ningún evento mientras el componente está deshabilitado.
        private void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Vehicle = GetComponent<VehicleController>();

            if (m_Vehicle != null && !m_Subscribed)
            {
                m_Vehicle.OnEngineStateChanged += HandleEngineStateChanged;
                m_Subscribed = true;
            }
        }

        // Solo resincroniza con el motor (por si se agregó con el motor ya prendido): no borra el estado.
        private void OnEnable()
        {
            SyncTracker();
            ResetAnchor();

            if (m_Vehicle == null) return;

            if (m_Vehicle.IsEngineRunning) Tracker.EngineStarted();
            else Tracker.EngineStopped();
        }

        private void OnDestroy()
        {
            if (m_Subscribed && m_Vehicle != null) m_Vehicle.OnEngineStateChanged -= HandleEngineStateChanged;
            m_Subscribed = false;
        }

        // Las ediciones del Inspector en Play Mode llegan por acá.
        private void OnValidate()
        {
            m_ThresholdM = Mathf.Max(0.1f, m_ThresholdM);
            m_MinTimeS = Mathf.Max(0f, m_MinTimeS);
            if (m_Tracker != null) SyncTracker();
        }

        private void ResetAnchor()
        {
            if (m_Body != null) m_Anchor = m_Body.position;
        }

        private void SyncTracker()
        {
            if (m_Tracker == null) return;
            m_Tracker.ThresholdM = m_ThresholdM;
            m_Tracker.MinTimeS = m_MinTimeS;
        }

        private void HandleEngineStateChanged(EngineState state)
        {
            switch (state)
            {
                case EngineState.Running:
                    Tracker.EngineStarted();
                    if (m_LogEvents) Debug.Log("[LowBeamDetector] ℹ️ Motor en marcha: se empieza a controlar las luces bajas.");
                    break;
                case EngineState.Off:
                    Tracker.EngineStopped();
                    if (m_LogEvents) Debug.Log("[LowBeamDetector] ℹ️ Motor apagado: se reinicia el control de las luces bajas.");
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (m_Vehicle == null)
            {
                if (!m_WarnedNoVehicle)
                {
                    m_WarnedNoVehicle = true;
                    Debug.LogWarning("[LowBeamDetector] ⚠️ No hay VehicleController en el mismo objeto: no se controlan las luces.", this);
                }

                return;
            }

            LowBeamTracker tracker = Tracker;

            // 1. Distancia real del paso: desplazamiento neto del Rigidbody (XZ) respecto del ancla. El ancla se mueve en
            // todos los pasos que cuentan (también suspendido) y mientras el motor no está en marcha, para no contar
            // lo recorrido en esos tramos al reanudar.
            Vector3 position = m_Body.position;
            Vector3 delta = position - m_Anchor;
            delta.y = 0f;
            float displacement = delta.magnitude;
            float distance = 0f;
            if (!tracker.IsEngineRunning || displacement > k_TeleportStepM)
            {
                m_Anchor = position;
            }
            else if (displacement >= k_AnchorStepM)
            {
                distance = displacement;
                m_Anchor = position;
            }

            float dt = Time.fixedDeltaTime;
            HeadlightMode mode = HeadlightSource != null ? HeadlightSource() : m_Vehicle.CurrentHeadlights;

            // 2. Suspendido: no acumula ni bloquea nada. Con las bajas puestas sí se observa (rearma y marca que ya
            // circuló); con LowBeam el tracker nunca devuelve una falta.
            if (IsSuppressed)
            {
                if (mode == HeadlightMode.LowBeam) tracker.Step(mode, distance, dt);
                return;
            }

            bool wasLatched = tracker.IsLatched;
            LowBeamFault? fault = tracker.Step(mode, distance, dt);

            if (m_LogEvents && wasLatched && !tracker.IsLatched)
            {
                Debug.Log("[LowBeamDetector] ✅ Luces bajas puestas: se rearma el control.");
            }

            if (!fault.HasValue) return;

            LowBeamFault value = fault.Value;
            LastFault = value;
            if (m_LogEvents)
            {
                Debug.Log($"[LowBeamDetector] 🚫 {value.WhenText} {value.LightsText} (modo {value.Mode}, {tracker.AccumulatedM:F1} m y {tracker.AccumulatedS:F1} s sin bajas).");
            }

            InfractionReporting.Report(InfractionReport.Create(
                InfractionIds.LowBeam, this, transform.position,
                ("when", value.WhenText),
                ("lights", value.LightsText)));
        }
    }
}
