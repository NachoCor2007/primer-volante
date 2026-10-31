using System;
using System.Collections;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Controlador integral de la Lección 6: Maniobras de Estacionamiento y Precisión.
    /// Supervisa las maniobras en las tres dársenas reglamentarias:
    /// 1. Dársena a 45° (en espiga / diagonal).
    /// 2. Dársena a 90° (en batería perpendicular de reversa).
    /// 3. Dársena en paralelo (cordón derecho entre dos vehículos).
    /// Asegura el uso de balizas, la orientación dentro del recuadro amarillo en el asfalto
    /// y el aseguramiento del vehículo (P y freno de mano).
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class Lesson06ParkingController : MonoBehaviour
    {
        private static Lesson06ParkingController s_Instance;

        [Header("Subsistemas de Lección")]
        [SerializeField] private LessonManager m_LessonManager;
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private LessonHUDPanel m_HUDPanel;
        [SerializeField] private VRScreenFader m_ScreenFader;
        [SerializeField] private DriverSeatFollower m_SeatFollower;
        [SerializeField] private TurnSignalExemptZone m_ExemptZone;

        [Header("Zonas de Dársenas")]
        [Tooltip("Trigger de detección para la dársena a 45°.")]
        [SerializeField] private ParkingZoneTrigger m_Diagonal45Zone;

        [Tooltip("Trigger de detección para la dársena a 90° de culata.")]
        [SerializeField] private ParkingZoneTrigger m_Bay90Zone;

        [Tooltip("Trigger de detección para la dársena en paralelo al cordón derecho.")]
        [SerializeField] private ParkingZoneTrigger m_ParallelZone;

        [Header("Demarcación Visual en Asfalto (Recuadros Amarillos)")]
        [Tooltip("Recuadro amarillo en el piso de la dársena a 45°.")]
        [SerializeField] private GameObject m_GroundHighlight45;

        [Tooltip("Recuadro amarillo en el piso de la dársena a 90°.")]
        [SerializeField] private GameObject m_GroundHighlight90;

        [Tooltip("Recuadro amarillo en el piso de la dársena en paralelo.")]
        [SerializeField] private GameObject m_GroundHighlightParallel;

        [Header("Puntos de Partida de Cada Maniobra")]
        [Tooltip("Puntos de partida inicial para cada uno de los 5 pasos.")]
        [SerializeField] private Transform[] m_StepSpawnPoints = new Transform[5];

        private int m_CurrentStepIndex = -1;
        private Coroutine m_TransitionCoroutine;

        public static Lesson06ParkingController Instance => s_Instance;
        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public ParkingZoneTrigger Diagonal45Zone => m_Diagonal45Zone;
        public ParkingZoneTrigger Bay90Zone => m_Bay90Zone;
        public ParkingZoneTrigger ParallelZone => m_ParallelZone;
        public TurnSignalExemptZone ExemptZone => m_ExemptZone;

        public GameObject GroundHighlight45 { get => m_GroundHighlight45; set => m_GroundHighlight45 = value; }
        public GameObject GroundHighlight90 { get => m_GroundHighlight90; set => m_GroundHighlight90 = value; }
        public GameObject GroundHighlightParallel { get => m_GroundHighlightParallel; set => m_GroundHighlightParallel = value; }

        public bool IsDiagonalParked => m_Diagonal45Zone != null && m_Diagonal45Zone.IsVehicleParkedProperly;
        public bool IsBay90Parked => m_Bay90Zone != null && m_Bay90Zone.IsVehicleParkedProperly;
        public bool IsParallelParked => m_ParallelZone != null && m_ParallelZone.IsVehicleParkedProperly;

        public bool IsDiagonalPositioned => m_Diagonal45Zone != null && m_Diagonal45Zone.IsVehiclePositionedProperly;
        public bool IsDiagonalSecured => m_Diagonal45Zone != null && m_Diagonal45Zone.IsVehicleSecuredProperly;

        public bool IsBay90Positioned => m_Bay90Zone != null && m_Bay90Zone.IsVehiclePositionedProperly;
        public bool IsBay90Secured => m_Bay90Zone != null && m_Bay90Zone.IsVehicleSecuredProperly;

        public bool IsParallelPositioned => m_ParallelZone != null && m_ParallelZone.IsVehiclePositionedProperly;

        public bool IsVehicleSecured => m_VehicleController != null &&
                                        !m_VehicleController.IsHazardActive &&
                                        (m_VehicleController.CurrentGear == GearState.Park || m_VehicleController.CurrentGear == GearState.P) &&
                                        m_VehicleController.HandbrakeEngagement >= 0.85f &&
                                        m_VehicleController.CurrentSpeedKmh < 0.2f &&
                                        (m_ParallelZone == null || m_ParallelZone.IsVehicleInside);

        private void Awake()
        {
            if (s_Instance == null) s_Instance = this;
            AutoDiscoverReferences();
        }

        private void OnEnable()
        {
            if (s_Instance == null) s_Instance = this;
            SubscribeEvents(true);
        }

        private void OnDisable()
        {
            SubscribeEvents(false);
            if (s_Instance == this) s_Instance = null;
        }

        public void AutoDiscoverReferences()
        {
            if (m_LessonManager == null)
            {
                m_LessonManager = GetComponent<LessonManager>() ??
                                  GetComponentInParent<LessonManager>() ??
                                  UnityEngine.Object.FindAnyObjectByType<LessonManager>();
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
                m_ScreenFader = VRScreenFader.EnsureScreenFader();
            }

            if (m_SeatFollower == null)
            {
                m_SeatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            }

            if (m_ExemptZone == null)
            {
                m_ExemptZone = UnityEngine.Object.FindAnyObjectByType<TurnSignalExemptZone>();
            }

            if (m_Diagonal45Zone == null || m_Bay90Zone == null || m_ParallelZone == null)
            {
                ParkingZoneTrigger[] triggers = UnityEngine.Object.FindObjectsByType<ParkingZoneTrigger>(FindObjectsInactive.Exclude);
                foreach (var trig in triggers)
                {
                    if (trig.StationType == ParkingStationType.Diagonal45 && m_Diagonal45Zone == null)
                        m_Diagonal45Zone = trig;
                    else if (trig.StationType == ParkingStationType.Bay90Reverse && m_Bay90Zone == null)
                        m_Bay90Zone = trig;
                    else if (trig.StationType == ParkingStationType.ParallelRight && m_ParallelZone == null)
                        m_ParallelZone = trig;
                }
            }

            // Buscar recuadros amarillos en la escena si no están asignados
            if (m_GroundHighlight45 == null) m_GroundHighlight45 = GameObject.Find("GroundVisual_YellowZone_45");
            if (m_GroundHighlight90 == null) m_GroundHighlight90 = GameObject.Find("GroundVisual_YellowZone_90");
            if (m_GroundHighlightParallel == null) m_GroundHighlightParallel = GameObject.Find("GroundVisual_YellowZone_Parallel");
        }

        private void SubscribeEvents(bool subscribe)
        {
            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= OnStepChanged;
                if (subscribe) m_LessonManager.StepChanged += OnStepChanged;
            }
        }

        private void OnStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            m_CurrentStepIndex = stepIndex;
            UpdateGroundHighlights(stepIndex);

            if (m_TransitionCoroutine != null)
            {
                StopCoroutine(m_TransitionCoroutine);
            }
            m_TransitionCoroutine = StartCoroutine(TransitionToStepRoutine(stepIndex));
        }

        private IEnumerator TransitionToStepRoutine(int stepIndex)
        {
            // Pasos que continúan fluidos en el mismo sitio (NO reposicionar ni interrumpir la vista):
            // - Paso 1: Acaba de encender balizas en el acceso, continúa la marcha hacia 45°.
            if (stepIndex == 1 && m_VehicleController != null)
            {
                Transform spawn1 = GetSpawnForStep(1);
                if (spawn1 != null && Vector3.Distance(m_VehicleController.transform.position, spawn1.position) < 4.5f)
                {
                    yield break;
                }
            }

            // - Paso 2: Acaba de frenar centrado en 45°, debe poner P y freno de mano en ese mismo sitio.
            if (stepIndex == 2 && m_Diagonal45Zone != null && m_Diagonal45Zone.IsVehicleInside)
            {
                yield break;
            }

            // - Paso 4: Acaba de frenar de culata en 90°, debe poner P y freno de mano en ese mismo sitio.
            if (stepIndex == 4 && m_Bay90Zone != null && m_Bay90Zone.IsVehicleInside)
            {
                yield break;
            }

            // - Paso 6: Acaba de frenar en paralelo, debe apagar balizas, poner P y freno de mano.
            if (stepIndex == 6 && m_ParallelZone != null && m_ParallelZone.IsVehicleInside)
            {
                yield break;
            }

            if (m_ScreenFader != null)
            {
                yield return m_ScreenFader.FadeOutRoutine(0.4f);
            }
            else
            {
                yield return new WaitForSeconds(0.2f);
            }

            RepositionToStation(stepIndex);

            yield return new WaitForSeconds(0.15f);

            if (m_ScreenFader != null)
            {
                yield return m_ScreenFader.FadeInRoutine(0.4f);
            }
        }

        /// <summary>
        /// Resalta el recuadro amarillo en el asfalto y su flecha indicadora correspondiente al objetivo activo.
        /// En el Paso 0 (balizas) NINGÚN plano es visible.
        /// </summary>
        public void UpdateGroundHighlights(int stepIndex)
        {
            // Paso 0: Balizas -> NINGÚN recuadro visible en el asfalto
            // Pasos 1 y 2: Dársena 45° visible (maniobra y aseguramiento)
            // Pasos 3 y 4: Dársena 90° visible (maniobra y aseguramiento)
            // Pasos 5 y 6: Dársena Paralelo visible (maniobra y aseguramiento)
            SetStationVisualActive(m_GroundHighlight45, stepIndex == 1 || stepIndex == 2);
            SetStationVisualActive(m_GroundHighlight90, stepIndex == 3 || stepIndex == 4);
            SetStationVisualActive(m_GroundHighlightParallel, stepIndex == 5 || stepIndex == 6);
        }

        private void SetStationVisualActive(GameObject highlight, bool active)
        {
            if (highlight == null) return;
            highlight.SetActive(active);
            if (highlight.transform.parent != null)
            {
                foreach (Transform child in highlight.transform.parent)
                {
                    if (child.name.StartsWith("GroundVisual_Arrow"))
                    {
                        child.gameObject.SetActive(active);
                    }
                }
            }
        }

        /// <summary>
        /// Obtiene el punto de partida correspondiente al paso actual.
        /// </summary>
        public Transform GetSpawnForStep(int stepIndex)
        {
            if (m_StepSpawnPoints == null || m_StepSpawnPoints.Length == 0) return null;

            switch (stepIndex)
            {
                case 0: // Balizas
                    return m_StepSpawnPoints.Length > 0 ? m_StepSpawnPoints[0] : null;
                case 1: // Maniobra 45°
                case 2: // Asegurar 45°
                    return m_StepSpawnPoints.Length > 1 ? m_StepSpawnPoints[1] : null;
                case 3: // Maniobra 90°
                case 4: // Asegurar 90°
                    return m_StepSpawnPoints.Length > 2 ? m_StepSpawnPoints[2] : null;
                case 5: // Maniobra Paralelo
                    return m_StepSpawnPoints.Length > 3 ? m_StepSpawnPoints[3] : null;
                case 6: // Asegurar final
                default:
                    return m_StepSpawnPoints.Length > 4 ? m_StepSpawnPoints[4] : null;
            }
        }

        /// <summary>
        /// Reposiciona el vehículo suavemente en el punto de inicio de la estación correspondiente.
        /// </summary>
        public void RepositionToStation(int stepIndex)
        {
            if (m_VehicleController == null) return;
            Transform spawn = GetSpawnForStep(stepIndex);
            if (spawn == null) return;

            m_VehicleController.transform.position = spawn.position;
            m_VehicleController.transform.rotation = spawn.rotation;

            Rigidbody rb = m_VehicleController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = spawn.position;
                rb.rotation = spawn.rotation;
            }

            if (m_VehicleController.SteeringWheel != null)
            {
                m_VehicleController.SteeringWheel.ResetToCenter();
            }

            m_VehicleController.SetEngineRunning(true);

            if (stepIndex == 0)
            {
                // En el primer paso debe encender las balizas el alumno
                m_VehicleController.SetHazardActive(false);
                m_VehicleController.SetGear(GearState.Drive);
            }
            else if (stepIndex == 1 || stepIndex == 3 || stepIndex == 5)
            {
                // Para las maniobras 45°, 90° y Paralelo:
                // Balizas activadas, transmisión en Drive (D) y freno de mano abajo
                m_VehicleController.SetHazardActive(true);
                m_VehicleController.SetGear(GearState.Drive);

                VRGearShifter shifter = m_VehicleController.GetComponentInChildren<VRGearShifter>(true);
                if (shifter != null)
                {
                    shifter.SetRotationToGear(GearState.Drive);
                }

                VRHandbrake handbrake = m_VehicleController.GetComponentInChildren<VRHandbrake>(true);
                if (handbrake != null)
                {
                    handbrake.SetEngagementImmediate(0f);
                }
            }

            if (m_SeatFollower != null)
            {
                m_SeatFollower.SnapToSeat();
            }
        }
    }
}
