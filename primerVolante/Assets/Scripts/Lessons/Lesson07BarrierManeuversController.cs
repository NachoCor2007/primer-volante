using System.Collections.Generic;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Controlador integral de la Lección 7: Dominio del Volante y Slalom entre Conos (adelante y en reversa con espejos).
    /// Supervisa:
    /// 1. Slalom hacia adelante en zig-zag entre una fila de conos, con vallas transversales que obligan el zigzag.
    /// 2. Maniobra de slalom en reversa utilizando la regla del "medio auto" en los espejos retrovisores laterales.
    /// 3. Detención y aseguramiento del vehículo (P y freno de mano).
    /// La finalización de los recuadros amarillos es puramente geométrica (no depende de eventos de trigger) y los
    /// toques de conos y vallas se cuentan y se avisan en el HUD sin penalizar ni reiniciar el tramo.
    /// La pista (conos, vallas, corredor y recuadros) está armada en la escena por <c>Lesson07BarrierManeuversSetup</c>:
    /// este componente no construye nada en runtime.
    /// Cumple estrictamente con la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class Lesson07BarrierManeuversController : MonoBehaviour
    {
        /// <summary>Índice del paso "Slalom hacia Adelante" dentro de la lección.</summary>
        public const int ForwardSlalomStepIndex = 3;

        /// <summary>Índice del paso "Slalom en Reversa" dentro de la lección.</summary>
        public const int ReverseSlalomStepIndex = 5;

        /// <summary>Velocidad (km/h) por debajo de la cual el auto se considera detenido.</summary>
        public const float StoppedSpeedKmh = 1.5f;

        /// <summary>Margen (m) con el que el auto se considera completo dentro de un recuadro.</summary>
        public const float ZoneMarginTolerance = 0.25f;

        /// <summary>Aviso cuando el auto queda detenido solapando el recuadro sin estar completo adentro.</summary>
        public const string PositionWarningMessage = "Ubicá el auto completo dentro del recuadro amarillo";

        private static Lesson07BarrierManeuversController s_Instance;

        [Header("Subsistemas de Lección")]
        [SerializeField] private LessonManager m_LessonManager;
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private LessonHUDPanel m_HUDPanel;
        [SerializeField] private VRScreenFader m_ScreenFader;

        [Header("Recuadros de Maniobra")]
        [Tooltip("Recuadro de inicio / retorno en reversa.")]
        [SerializeField] private ParkingZoneTrigger m_StartZone;

        [Tooltip("Recuadro final del slalom hacia adelante.")]
        [SerializeField] private ParkingZoneTrigger m_ForwardFinishZone;

        [Header("Demarcación Visual en Asfalto (Recuadros Amarillos)")]
        [SerializeField] private GameObject m_StartZoneHighlight;
        [SerializeField] private GameObject m_ForwardZoneHighlight;

        [Header("Pista de Slalom")]
        [Tooltip("Conos de la fila (cada uno ya informa su falta a InfractionReporting).")]
        [SerializeField] private List<ObstacleCollisionDetector> m_Cones = new List<ObstacleCollisionDetector>();

        [Tooltip("Vallas transversales que obligan el zigzag (una valla suelta no informa falta).")]
        [SerializeField] private List<LessonBarrier> m_TransverseBarriers = new List<LessonBarrier>();

        [Tooltip("Paredes del corredor (cada contacto ya informa un CRASH a InfractionReporting).")]
        [SerializeField] private List<LessonBoundary> m_Boundaries = new List<LessonBoundary>();

        [Header("Avisos")]
        [Tooltip("Segundos detenido solapando el recuadro, sin estar completo adentro, antes de mostrar el aviso de posición.")]
        [SerializeField] private float m_PositionWarningDelaySeconds = 1.5f;

        [Tooltip("Segundos que se muestra el aviso de un toque antes de restaurar la instrucción del paso.")]
        [SerializeField] private float m_TouchWarningSeconds = 3f;

        [Tooltip("Segundos mínimos entre dos toques contados de la misma valla transversal.")]
        [SerializeField] private float m_BarrierTouchCooldownSeconds = 1f;

        private int m_ConeContacts;
        private int m_BarrierContacts;

        private float m_StoppedSeconds;
        private bool m_PositionWarningShown;
        private float m_TouchWarningEndTime = -1f;
        // Cooldown por valla transversal (el grupo Transverse_0N_*, que reúne sus dos piezas), no por pieza.
        private readonly Dictionary<Transform, float> m_LastBarrierTouchTime = new Dictionary<Transform, float>();

        /// <summary>Controlador de la escena, o null si no hay ninguno (no se crea uno solo).</summary>
        public static Lesson07BarrierManeuversController Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = UnityEngine.Object.FindAnyObjectByType<Lesson07BarrierManeuversController>();
                }
                return s_Instance;
            }
        }

        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public ParkingZoneTrigger StartZone => m_StartZone;
        public ParkingZoneTrigger ForwardFinishZone => m_ForwardFinishZone;
        public GameObject StartZoneHighlight => m_StartZoneHighlight;
        public GameObject ForwardZoneHighlight => m_ForwardZoneHighlight;
        public IReadOnlyList<ObstacleCollisionDetector> Cones => m_Cones;
        public IReadOnlyList<LessonBarrier> TransverseBarriers => m_TransverseBarriers;
        public IReadOnlyList<LessonBoundary> Boundaries => m_Boundaries;

        /// <summary>Toques de conos contados en la lección.</summary>
        public int ConeContacts => m_ConeContacts;

        /// <summary>Toques de vallas transversales y de las paredes del corredor contados en la lección.</summary>
        public int BarrierContacts => m_BarrierContacts;

        /// <summary>
        /// El auto está completo dentro del recuadro final (con margen) y detenido. Es geométrico: no depende de
        /// los eventos de trigger ni de la orientación o la marcha.
        /// </summary>
        public bool IsForwardSlalomComplete => IsCompleteIn(m_ForwardFinishZone);

        /// <summary>El auto está completo dentro del recuadro de inicio (con margen) y detenido.</summary>
        public bool IsReverseSlalomComplete => IsCompleteIn(m_StartZone);

        private bool IsCompleteIn(ParkingZoneTrigger zone)
        {
            if (zone == null || m_VehicleController == null)
            {
                AutoDiscoverReferences();
                if (zone == null || m_VehicleController == null) return false;
            }

            return zone.IsVehicleFullyInside(m_VehicleController, ZoneMarginTolerance) &&
                   m_VehicleController.CurrentSpeedKmh <= StoppedSpeedKmh;
        }

        private void Awake()
        {
            s_Instance = this;
            AutoDiscoverReferences();
            UpdateHighlightsForStep(0);
        }

        private void OnEnable()
        {
            s_Instance = this;
            AutoDiscoverReferences();
            SubscribeToTrack();
            UpdateHighlightsForStep(0);

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= HandleStepChanged;
                m_LessonManager.StepChanged += HandleStepChanged;
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromTrack();

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= HandleStepChanged;
            }
        }

        private void HandleStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            // El HUD ya mostró el paso nuevo (y limpió cualquier aviso): se reinicia el estado de avisos.
            m_TouchWarningEndTime = -1f;
            m_PositionWarningShown = false;
            m_StoppedSeconds = 0f;
            UpdateHighlightsForStep(stepIndex);
        }

        /// <summary>
        /// Actualiza la visibilidad de los recuadros amarillos en el piso según el paso actual.
        /// </summary>
        public void UpdateHighlightsForStep(int stepIndex)
        {
            // Pasos 0 a 3 (hacia adelante: volante, motor, drive, slalom adelante): Resaltar recuadro frontal objetivo
            // Pasos 4 a 6 (en reversa: poner marcha atrás, slalom reversa, asegurar auto): Resaltar recuadro de inicio
            if (m_ForwardZoneHighlight != null)
            {
                m_ForwardZoneHighlight.SetActive(stepIndex <= ForwardSlalomStepIndex);
            }
            if (m_StartZoneHighlight != null)
            {
                m_StartZoneHighlight.SetActive(stepIndex > ForwardSlalomStepIndex);
            }
        }

        private void Update()
        {
            UpdateTouchWarning();
            UpdatePositionWarning();
        }

        // --- Aviso de toques ---------------------------------------------------------------------------------

        private void UpdateTouchWarning()
        {
            if (m_TouchWarningEndTime < 0f || Time.time < m_TouchWarningEndTime) return;

            m_TouchWarningEndTime = -1f;
            RestoreStepInstruction();
        }

        private void ShowTouchWarning(string message)
        {
            if (m_HUDPanel == null) return;

            m_PositionWarningShown = false;
            m_HUDPanel.ShowStepWarning(message);
            m_TouchWarningEndTime = Time.time + m_TouchWarningSeconds;
        }

        // Vuelve a mostrar el paso actual: ShowStepWarning reemplaza el texto de la instrucción.
        private void RestoreStepInstruction()
        {
            if (m_HUDPanel == null || m_LessonManager == null) return;
            if (!m_LessonManager.IsLessonActive || m_LessonManager.IsLessonCompleted) return;

            LessonStepSO step = m_LessonManager.CurrentStep;
            if (step == null || m_LessonManager.ActiveLesson == null) return;

            m_HUDPanel.DisplayStep(step, m_LessonManager.CurrentStepIndex, m_LessonManager.ActiveLesson.steps.Count);
        }

        private void HandleConeImpact(ObstacleImpact impact)
        {
            m_ConeContacts++;
            Debug.Log($"[Lesson07] Toque de cono '{(impact.Obstacle != null ? impact.Obstacle.name : "?")}' a {impact.PlayerSpeedKmh:F1} km/h. Toques de cono: {m_ConeContacts}");
            ShowTouchWarning($"Tocaste un cono — {m_ConeContacts} {(m_ConeContacts == 1 ? "toque" : "toques")}");
        }

        private void HandleBarrierContact(LessonBarrierContact contact)
        {
            Transform key = TransverseBarrierGroup(contact.Barrier);
            if (key != null)
            {
                if (m_LastBarrierTouchTime.TryGetValue(key, out float last) && Time.time >= last &&
                    Time.time - last < m_BarrierTouchCooldownSeconds)
                {
                    return;
                }
                m_LastBarrierTouchTime[key] = Time.time;
            }

            RegisterBarrierTouch(key != null ? key.name : "?", contact.SpeedKmh);
        }

        // LessonBarrier vive en el hijo AlertTrigger de la pieza; la pieza cuelga del grupo de la valla transversal.
        private static Transform TransverseBarrierGroup(LessonBarrier barrier)
        {
            if (barrier == null) return null;
            Transform piece = barrier.transform.parent;
            if (piece == null) return barrier.transform;
            return piece.parent != null ? piece.parent : piece;
        }

        private void HandleBoundaryContact(LessonBarrierContact contact)
        {
            RegisterBarrierTouch(contact.Barrier != null ? contact.Barrier.name : "corredor", contact.SpeedKmh);
        }

        private void RegisterBarrierTouch(string barrierName, float speedKmh)
        {
            m_BarrierContacts++;
            Debug.Log($"[Lesson07] Toque de valla '{barrierName}' a {speedKmh:F1} km/h. Toques de valla: {m_BarrierContacts}");
            ShowTouchWarning($"Tocaste una valla — {m_BarrierContacts} {(m_BarrierContacts == 1 ? "toque" : "toques")}");
        }

        private void SubscribeToTrack()
        {
            UnsubscribeFromTrack();

            foreach (ObstacleCollisionDetector cone in m_Cones)
            {
                if (cone != null) cone.OnImpact += HandleConeImpact;
            }

            foreach (LessonBarrier barrier in m_TransverseBarriers)
            {
                if (barrier != null) barrier.OnVehicleReached += HandleBarrierContact;
            }

            foreach (LessonBoundary boundary in m_Boundaries)
            {
                if (boundary != null) boundary.OnVehicleReachedBoundary += HandleBoundaryContact;
            }
        }

        private void UnsubscribeFromTrack()
        {
            foreach (ObstacleCollisionDetector cone in m_Cones)
            {
                if (cone != null) cone.OnImpact -= HandleConeImpact;
            }

            foreach (LessonBarrier barrier in m_TransverseBarriers)
            {
                if (barrier != null) barrier.OnVehicleReached -= HandleBarrierContact;
            }

            foreach (LessonBoundary boundary in m_Boundaries)
            {
                if (boundary != null) boundary.OnVehicleReachedBoundary -= HandleBoundaryContact;
            }
        }

        // --- Aviso de posición -------------------------------------------------------------------------------

        private void UpdatePositionWarning()
        {
            if (m_LessonManager == null || m_VehicleController == null || m_HUDPanel == null) return;

            ParkingZoneTrigger zone = ActiveZoneForStep(m_LessonManager.CurrentStepIndex);
            bool stepActive = zone != null && m_LessonManager.IsLessonActive && !m_LessonManager.IsLessonCompleted;
            bool stopped = m_VehicleController.CurrentSpeedKmh <= StoppedSpeedKmh;

            // El tiempo cuenta solo mientras el auto está detenido, solapando el recuadro y sin estar completo adentro.
            bool needsWarning = false;
            bool overlapsIncomplete = stepActive && stopped &&
                                      !zone.IsVehicleFullyInside(m_VehicleController, ZoneMarginTolerance) &&
                                      OverlapsZone(zone, m_VehicleController);
            if (overlapsIncomplete)
            {
                m_StoppedSeconds += Time.deltaTime;
                needsWarning = m_StoppedSeconds >= m_PositionWarningDelaySeconds;
            }
            else
            {
                m_StoppedSeconds = 0f;
            }

            if (needsWarning)
            {
                // El aviso de un toque tiene prioridad hasta que se restaure la instrucción.
                if (!m_PositionWarningShown && m_TouchWarningEndTime < 0f)
                {
                    m_PositionWarningShown = true;
                    m_HUDPanel.ShowStepWarning(PositionWarningMessage);
                }
            }
            else if (m_PositionWarningShown)
            {
                m_PositionWarningShown = false;
                RestoreStepInstruction();
            }
        }

        private ParkingZoneTrigger ActiveZoneForStep(int stepIndex)
        {
            if (stepIndex == ForwardSlalomStepIndex) return m_ForwardFinishZone;
            if (stepIndex == ReverseSlalomStepIndex) return m_StartZone;
            return null;
        }

        /// <summary>
        /// true si el BoxCollider del auto se superpone con el del recuadro, en el plano XZ (SAT entre dos
        /// rectángulos orientados). Geométrico: no depende de los eventos de trigger.
        /// </summary>
        public static bool OverlapsZone(ParkingZoneTrigger zone, VehicleController vehicle)
        {
            if (zone == null || vehicle == null) return false;

            BoxCollider zoneBox = zone.GetComponent<BoxCollider>();
            BoxCollider carBox = vehicle.GetComponent<BoxCollider>();
            if (carBox == null) carBox = vehicle.GetComponentInChildren<BoxCollider>();
            if (zoneBox == null || carBox == null) return false;

            GetOrientedRect(zoneBox, out Vector2 zc, out Vector2 zr, out Vector2 zf, out Vector2 zh);
            GetOrientedRect(carBox, out Vector2 cc, out Vector2 cr, out Vector2 cf, out Vector2 ch);

            return OverlapsOrientedRects(zc, zr, zf, zh, cc, cr, cf, ch);
        }

        private static void GetOrientedRect(BoxCollider box, out Vector2 center, out Vector2 right, out Vector2 forward, out Vector2 half)
        {
            Transform t = box.transform;
            Vector3 c = t.TransformPoint(box.center);
            Vector3 scale = t.lossyScale;
            center = new Vector2(c.x, c.z);
            right = new Vector2(t.right.x, t.right.z).normalized;
            forward = new Vector2(t.forward.x, t.forward.z).normalized;
            half = new Vector2(box.size.x * 0.5f * Mathf.Abs(scale.x), box.size.z * 0.5f * Mathf.Abs(scale.z));
        }

        /// <summary>SAT entre dos rectángulos orientados en el plano (ejes unitarios, semiejes en <c>half</c>).</summary>
        public static bool OverlapsOrientedRects(
            Vector2 centerA, Vector2 rightA, Vector2 forwardA, Vector2 halfA,
            Vector2 centerB, Vector2 rightB, Vector2 forwardB, Vector2 halfB)
        {
            Vector2 delta = centerB - centerA;
            Vector2[] axes = { rightA, forwardA, rightB, forwardB };
            foreach (Vector2 axis in axes)
            {
                float radiusA = halfA.x * Mathf.Abs(Vector2.Dot(rightA, axis)) + halfA.y * Mathf.Abs(Vector2.Dot(forwardA, axis));
                float radiusB = halfB.x * Mathf.Abs(Vector2.Dot(rightB, axis)) + halfB.y * Mathf.Abs(Vector2.Dot(forwardB, axis));
                if (Mathf.Abs(Vector2.Dot(delta, axis)) > radiusA + radiusB) return false;
            }
            return true;
        }

        public void AutoDiscoverReferences()
        {
            if (m_LessonManager == null)
            {
                m_LessonManager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            }

            if (m_VehicleController == null)
            {
                m_VehicleController = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            }

            if (m_HUDPanel == null)
            {
                m_HUDPanel = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            }

            if (m_ScreenFader == null)
            {
                m_ScreenFader = UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            }

            if (m_StartZone == null)
            {
                GameObject sz = GameObject.Find("ParkingZone_Start");
                if (sz != null) m_StartZone = sz.GetComponent<ParkingZoneTrigger>();
            }

            if (m_ForwardFinishZone == null)
            {
                GameObject fz = GameObject.Find("ParkingZone_ForwardFinish");
                if (fz != null) m_ForwardFinishZone = fz.GetComponent<ParkingZoneTrigger>();
            }

            if (m_Cones.Count == 0 || m_TransverseBarriers.Count == 0 || m_Boundaries.Count == 0)
            {
                GameObject course = GameObject.Find("Slalom_Course");
                if (course != null)
                {
                    if (m_Cones.Count == 0) m_Cones.AddRange(course.GetComponentsInChildren<ObstacleCollisionDetector>(true));
                    if (m_Boundaries.Count == 0) m_Boundaries.AddRange(course.GetComponentsInChildren<LessonBoundary>(true));
                    if (m_TransverseBarriers.Count == 0)
                    {
                        foreach (LessonBarrier barrier in course.GetComponentsInChildren<LessonBarrier>(true))
                        {
                            if (barrier.GetComponentInParent<LessonBoundary>(true) == null) m_TransverseBarriers.Add(barrier);
                        }
                    }
                }
            }
        }
    }
}
