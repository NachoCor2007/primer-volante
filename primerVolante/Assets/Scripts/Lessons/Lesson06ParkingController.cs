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
        public bool IsVehicleSecured => m_VehicleController != null &&
                                        !m_VehicleController.IsHazardActive &&
                                        (m_VehicleController.CurrentGear == GearState.Park || m_VehicleController.CurrentGear == GearState.P) &&
                                        m_VehicleController.HandbrakeEngagement >= 0.85f &&
                                        m_VehicleController.CurrentSpeedKmh < 0.2f;

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
            // Paso 1 (45°): Si el vehículo ya está en el acceso a la playa (cerca del Spawn 1/2),
            // no interrumpir con fade para permitir continuar la marcha tras activar las balizas.
            if (stepIndex == 1 && m_VehicleController != null)
            {
                Transform spawn1 = (m_StepSpawnPoints != null && m_StepSpawnPoints.Length > 1) ? m_StepSpawnPoints[1] : null;
                if (spawn1 != null && Vector3.Distance(m_VehicleController.transform.position, spawn1.position) < 4.5f)
                {
                    yield break;
                }
            }

            // Paso 4 (Asegurar vehículo): Si el auto ya se encuentra dentro de la dársena en paralelo,
            // no teletransportar para que el alumno complete el apagado de balizas y freno de mano.
            if (stepIndex == 4 && m_ParallelZone != null && m_ParallelZone.IsVehicleInside)
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
        /// </summary>
        public void UpdateGroundHighlights(int stepIndex)
        {
            // Paso 0: Balizas en aproximación -> recuadros visibles de forma neutra
            // Paso 1: Dársena 45° activo
            // Paso 2: Dársena 90° activo
            // Paso 3: Dársena Paralelo activo
            // Paso 4: Asegurar vehículo
            SetStationVisualActive(m_GroundHighlight45, stepIndex <= 1);
            SetStationVisualActive(m_GroundHighlight90, stepIndex == 0 || stepIndex == 2);
            SetStationVisualActive(m_GroundHighlightParallel, stepIndex == 0 || stepIndex == 3);
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
        /// Reposiciona el vehículo suavemente en el punto de inicio de la estación correspondiente.
        /// </summary>
        public void RepositionToStation(int stepIndex)
        {
            if (m_VehicleController == null) return;
            Transform spawn = (stepIndex >= 0 && stepIndex < m_StepSpawnPoints.Length) ? m_StepSpawnPoints[stepIndex] : null;
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
            else if (stepIndex >= 1 && stepIndex <= 3)
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
