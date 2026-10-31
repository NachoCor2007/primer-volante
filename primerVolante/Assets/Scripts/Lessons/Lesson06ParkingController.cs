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
    /// Asegura el uso de balizas y el aseguramiento del vehículo (P y freno de mano).
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

        [Header("Puntos de Partida de Cada Maniobra")]
        [Tooltip("Puntos de partida inicial para cada uno de los 5 pasos.")]
        [SerializeField] private Transform[] m_StepSpawnPoints = new Transform[5];

        private int m_CurrentStepIndex = -1;
        private bool m_IsCompletingStep = false;

        public static Lesson06ParkingController Instance => s_Instance;
        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public ParkingZoneTrigger Diagonal45Zone => m_Diagonal45Zone;
        public ParkingZoneTrigger Bay90Zone => m_Bay90Zone;
        public ParkingZoneTrigger ParallelZone => m_ParallelZone;
        public TurnSignalExemptZone ExemptZone => m_ExemptZone;

        public bool IsDiagonalParked => m_Diagonal45Zone != null && m_Diagonal45Zone.IsVehicleParkedProperly;
        public bool IsBay90Parked => m_Bay90Zone != null && m_Bay90Zone.IsVehicleParkedProperly;
        public bool IsParallelParked => m_ParallelZone != null && m_ParallelZone.IsVehicleParkedProperly;
        public bool IsVehicleSecured => m_VehicleController != null && !m_VehicleController.IsHazardActive &&
                                        (m_VehicleController.CurrentGear == GearState.Park || m_VehicleController.CurrentGear == GearState.P) &&
                                        m_VehicleController.HandbrakeEngagement >= 0.85f;

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
            m_IsCompletingStep = false;
        }

        private void Update()
        {
            if (m_IsCompletingStep || m_LessonManager == null || !m_LessonManager.IsLessonActive)
            {
                return;
            }

            LessonStepSO step = m_LessonManager.CurrentStep;
            if (step == null || step.completionMode != LessonStepCompletionMode.ActionRequired)
            {
                return;
            }

            EvaluateStepProgress(m_CurrentStepIndex, step);
        }

        private void EvaluateStepProgress(int stepIndex, LessonStepSO step)
        {
            if (m_VehicleController == null) return;

            switch (step.requiredAction)
            {
                case LessonActionType.ActivateHazardLights:
                    // Paso 1: Pulsar botón de balizas de emergencia
                    if (m_VehicleController.IsHazardActive)
                    {
                        TriggerStepSuccess();
                    }
                    break;

                case LessonActionType.ParkDiagonal45:
                    // Paso 2: Entrada a 45°, detenido, Parking (P) y freno de mano
                    if (m_Diagonal45Zone != null && m_Diagonal45Zone.IsVehicleParkedProperly)
                    {
                        TriggerStepSuccess();
                    }
                    break;

                case LessonActionType.ParkBay90Reverse:
                    // Paso 3: Estacionamiento a 90° marcha atrás y frenar
                    if (m_Bay90Zone != null && m_Bay90Zone.IsVehicleParkedProperly)
                    {
                        TriggerStepSuccess();
                    }
                    break;

                case LessonActionType.ParkParallelRight:
                    // Paso 4: Estacionamiento en paralelo alineado al cordón derecho
                    if (m_ParallelZone != null && m_ParallelZone.IsVehicleParkedProperly)
                    {
                        TriggerStepSuccess();
                    }
                    break;

                case LessonActionType.SecureParkDeactivateHazards:
                    // Paso 5: Apagar balizas, colocar Parking y aplicar freno de mano
                    if (IsVehicleSecured)
                    {
                        TriggerStepSuccess();
                    }
                    break;
            }
        }

        private void TriggerStepSuccess()
        {
            if (m_IsCompletingStep) return;
            m_IsCompletingStep = true;

            if (m_HUDPanel != null)
            {
                m_HUDPanel.PlayActionSuccessFeedback(() =>
                {
                    if (m_LessonManager != null && m_LessonManager.IsLessonActive)
                    {
                        m_LessonManager.CompleteCurrentStep();
                    }
                });
            }
            else if (m_LessonManager != null)
            {
                m_LessonManager.CompleteCurrentStep();
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

            if (m_SeatFollower != null)
            {
                m_SeatFollower.SnapToSeat();
            }
        }
    }
}
