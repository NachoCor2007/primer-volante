using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Monitor en tiempo real para lecciones guiadas por acciones físicas (Action-Driven).
    /// Detecta las acciones motrices e interacciones reales del conductor según la acción requerida
    /// en el LessonStepSO activo, disparando el feedback sensorial y avanzando automáticamente de paso.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public class LessonActionMonitor : MonoBehaviour
    {
        [Header("Subsistemas")]
        [Tooltip("Gestor de lecciones asociado.")]
        [SerializeField] private LessonManager m_LessonManager;

        [Tooltip("Controlador del vehículo.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Panel HUD sobre el volante.")]
        [SerializeField] private LessonHUDPanel m_HUDPanel;

        [Tooltip("Controlador del menú in-game / calibración.")]
        [SerializeField] private InGameMenuController m_MenuController;

        [Tooltip("Espejo retrovisor central.")]
        [SerializeField] private VRRearviewMirror m_RearviewMirror;

        [Tooltip("Panel eléctrico de espejos laterales.")]
        [SerializeField] private VRSideMirrorControlPanel m_SideMirrorPanel;

        [Header("Configuración")]
        [Tooltip("Retraso en segundos tras cumplir la acción antes de avanzar al siguiente paso.")]
        [SerializeField] private float m_AdvanceDelay = 0.6f;

        // Estado del paso actual
        private LessonStepSO m_CurrentStep;
        private bool m_IsCompletingStep = false;
        private Coroutine m_AdvanceCoroutine;

        // Métricas de seguimiento de acciones
        private Vector3 m_InitialStepPosition = Vector3.zero;
        private bool m_SteeringTurnedLeft = false;
        private bool m_SteeringTurnedRight = false;
        private bool m_MirrorWasAdjusted = false;
        private VRSideMirrorControlPanel.MirrorSelection m_InitialMirrorSelection = VRSideMirrorControlPanel.MirrorSelection.Off;

        public LessonStepSO CurrentStep => m_CurrentStep;
        public bool IsCompletingStep => m_IsCompletingStep;
        public VehicleController VehicleController
        {
            get => m_VehicleController;
            set => m_VehicleController = value;
        }

        public event Action<LessonStepSO> ActionCompleted;

        private void Awake()
        {
            AutoDiscoverReferences();
        }

        private void OnEnable()
        {
            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged += OnStepChanged;
                m_LessonManager.LessonStarted += OnLessonStarted;
            }

            BindVehicleEvents(true);
        }

        private void OnDisable()
        {
            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= OnStepChanged;
                m_LessonManager.LessonStarted -= OnLessonStarted;
            }

            BindVehicleEvents(false);

            if (m_AdvanceCoroutine != null)
            {
                StopCoroutine(m_AdvanceCoroutine);
                m_AdvanceCoroutine = null;
            }
        }

        /// <summary>
        /// Descubre automáticamente las referencias en la escena o vehículo usando APIs no obsoletas.
        /// </summary>
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
                m_VehicleController = GetComponentInChildren<VehicleController>() ??
                                      UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            }

            if (m_HUDPanel == null)
            {
                m_HUDPanel = GetComponentInChildren<LessonHUDPanel>() ??
                             UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            }

            if (m_MenuController == null)
            {
                m_MenuController = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>();
            }

            if (m_RearviewMirror == null)
            {
                m_RearviewMirror = UnityEngine.Object.FindAnyObjectByType<VRRearviewMirror>();
            }

            if (m_SideMirrorPanel == null)
            {
                m_SideMirrorPanel = UnityEngine.Object.FindAnyObjectByType<VRSideMirrorControlPanel>();
            }
        }

        private void BindVehicleEvents(bool bind)
        {
            if (m_VehicleController == null) return;

            if (bind)
            {
                m_VehicleController.OnEngineStateChanged += OnEngineStateChanged;
                m_VehicleController.OnHazardChanged += OnHazardChanged;
            }
            else
            {
                m_VehicleController.OnEngineStateChanged -= OnEngineStateChanged;
                m_VehicleController.OnHazardChanged -= OnHazardChanged;
            }
        }

        private void OnLessonStarted(LessonDefinitionSO lesson)
        {
            m_IsCompletingStep = false;
            if (m_AdvanceCoroutine != null)
            {
                StopCoroutine(m_AdvanceCoroutine);
                m_AdvanceCoroutine = null;
            }
        }

        private void OnStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            m_CurrentStep = step;
            m_IsCompletingStep = false;

            if (m_AdvanceCoroutine != null)
            {
                StopCoroutine(m_AdvanceCoroutine);
                m_AdvanceCoroutine = null;
            }

            ResetStepMetrics();
        }

        private void ResetStepMetrics()
        {
            if (m_VehicleController != null)
            {
                m_InitialStepPosition = m_VehicleController.transform.position;
            }
            else
            {
                m_InitialStepPosition = Vector3.zero;
            }

            m_SteeringTurnedLeft = false;
            m_SteeringTurnedRight = false;
            m_MirrorWasAdjusted = false;

            if (m_SideMirrorPanel != null)
            {
                m_InitialMirrorSelection = m_SideMirrorPanel.CurrentSelection;
            }
        }

        private void OnEngineStateChanged(EngineState state)
        {
            CheckActionProgress();
        }

        private void OnHazardChanged(bool active)
        {
            CheckActionProgress();
        }

        private void Update()
        {
            if (m_CurrentStep == null || m_CurrentStep.completionMode != LessonStepCompletionMode.ActionRequired)
            {
                return;
            }

            if (m_IsCompletingStep)
            {
                return;
            }

            CheckActionProgress();
        }

        /// <summary>
        /// Comprueba si la acción requerida para el paso actual ha sido completada por el conductor.
        /// </summary>
        public void CheckActionProgress()
        {
            if (m_CurrentStep == null || m_CurrentStep.completionMode != LessonStepCompletionMode.ActionRequired || m_IsCompletingStep)
            {
                return;
            }

            if (IsConditionSatisfied(m_CurrentStep))
            {
                TriggerActionSuccess();
            }
        }

        /// <summary>
        /// Evalúa si la condición de la acción requerida en un paso está actualmente satisfecha.
        /// </summary>
        public bool IsConditionSatisfied(LessonStepSO step)
        {
            if (step == null) return false;

            if (m_VehicleController == null || m_MenuController == null || m_RearviewMirror == null || m_SideMirrorPanel == null)
            {
                AutoDiscoverReferences();
            }

            switch (step.requiredAction)
            {
                case LessonActionType.None:
                    return false;

                case LessonActionType.OpenCalibrationMenu:
                    if (m_MenuController != null && m_MenuController.IsOpen) return true;
                    if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
                    return false;

                case LessonActionType.RotateSteeringWheel:
                    if (m_VehicleController != null && m_VehicleController.SteeringWheel != null)
                    {
                        float angle = m_VehicleController.SteeringWheel.CurrentAngle;
                        float val = m_VehicleController.SteeringWheel.SteeringValue;
                        float threshold = step.actionNumericParameter > 0 ? step.actionNumericParameter : 20f;

                        if (angle <= -threshold || val <= -0.15f) m_SteeringTurnedLeft = true;
                        if (angle >= threshold || val >= 0.15f) m_SteeringTurnedRight = true;

                        return m_SteeringTurnedLeft && m_SteeringTurnedRight;
                    }
                    return false;

                case LessonActionType.AdjustMirrors:
                    if (m_RearviewMirror != null && m_RearviewMirror.IsGrabbed)
                    {
                        m_MirrorWasAdjusted = true;
                    }
                    if (m_SideMirrorPanel != null && m_SideMirrorPanel.CurrentSelection != VRSideMirrorControlPanel.MirrorSelection.Off)
                    {
                        m_MirrorWasAdjusted = true;
                    }
                    if (Keyboard.current != null && (Keyboard.current.leftBracketKey.wasPressedThisFrame ||
                                                     Keyboard.current.rightBracketKey.wasPressedThisFrame ||
                                                     Keyboard.current.leftArrowKey.isPressed ||
                                                     Keyboard.current.rightArrowKey.isPressed))
                    {
                        m_MirrorWasAdjusted = true;
                    }
                    return m_MirrorWasAdjusted;

                case LessonActionType.PressBrakePedal:
                    if (m_VehicleController != null)
                    {
                        float targetBrake = step.actionNumericParameter > 0 ? step.actionNumericParameter : 0.8f;
                        return m_VehicleController.BrakeValue >= targetBrake;
                    }
                    return false;

                case LessonActionType.StartEngine:
                    return m_VehicleController != null && m_VehicleController.IsEngineRunning;

                case LessonActionType.ReleaseHandbrake:
                    if (m_VehicleController != null)
                    {
                        float targetEngagement = step.actionNumericParameter > 0 ? step.actionNumericParameter : 0.05f;
                        return m_VehicleController.HandbrakeEngagement <= targetEngagement;
                    }
                    return false;

                case LessonActionType.EngageHandbrake:
                    if (m_VehicleController != null)
                    {
                        float targetEngagement = step.actionNumericParameter > 0 ? step.actionNumericParameter : 0.95f;
                        return m_VehicleController.HandbrakeEngagement >= targetEngagement;
                    }
                    return false;

                case LessonActionType.ShiftToDrive:
                    return m_VehicleController != null && (m_VehicleController.CurrentGear == GearState.Drive || m_VehicleController.CurrentGear == GearState.D);

                case LessonActionType.ShiftToReverse:
                    return m_VehicleController != null && (m_VehicleController.CurrentGear == GearState.Reverse || m_VehicleController.CurrentGear == GearState.R);

                case LessonActionType.ShiftToPark:
                    return m_VehicleController != null && (m_VehicleController.CurrentGear == GearState.Park || m_VehicleController.CurrentGear == GearState.P);

                case LessonActionType.ShiftToNeutral:
                    return m_VehicleController != null && (m_VehicleController.CurrentGear == GearState.Neutral || m_VehicleController.CurrentGear == GearState.N);

                case LessonActionType.ActivateTurnSignalLeft:
                    return m_VehicleController != null && m_VehicleController.CurrentTurnSignal == TurnSignalState.Left;

                case LessonActionType.ActivateTurnSignalRight:
                    return m_VehicleController != null && m_VehicleController.CurrentTurnSignal == TurnSignalState.Right;

                case LessonActionType.ActivateHazardLights:
                    return m_VehicleController != null && m_VehicleController.IsHazardActive;

                case LessonActionType.CycleHeadlights:
                    return m_VehicleController != null && m_VehicleController.CurrentHeadlights != HeadlightMode.Off;

                case LessonActionType.DriveForward:
                    if (m_VehicleController != null && (m_VehicleController.CurrentGear == GearState.Drive || m_VehicleController.CurrentGear == GearState.D))
                    {
                        float dist = Vector3.Dot(m_VehicleController.transform.position - m_InitialStepPosition, m_VehicleController.GetForwardVector());
                        float requiredDist = step.actionNumericParameter > 0 ? step.actionNumericParameter : 3f;
                        return dist >= requiredDist;
                    }
                    return false;

                case LessonActionType.DriveReverse:
                    if (m_VehicleController != null && (m_VehicleController.CurrentGear == GearState.Reverse || m_VehicleController.CurrentGear == GearState.R))
                    {
                        float dist = Vector3.Dot(m_VehicleController.transform.position - m_InitialStepPosition, -m_VehicleController.GetForwardVector());
                        float requiredDist = step.actionNumericParameter > 0 ? step.actionNumericParameter : 2f;
                        return dist >= requiredDist;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private void TriggerActionSuccess()
        {
            if (m_IsCompletingStep) return;
            m_IsCompletingStep = true;

            ActionCompleted?.Invoke(m_CurrentStep);

            if (m_HUDPanel != null)
            {
                m_HUDPanel.PlayActionSuccessFeedback();
            }

            if (gameObject.activeInHierarchy)
            {
                m_AdvanceCoroutine = StartCoroutine(AdvanceStepRoutine());
            }
            else if (m_LessonManager != null)
            {
                m_LessonManager.CompleteCurrentStep();
            }
        }

        private IEnumerator AdvanceStepRoutine()
        {
            yield return new WaitForSeconds(m_AdvanceDelay);
            m_AdvanceCoroutine = null;

            if (m_LessonManager != null && m_LessonManager.IsLessonActive && !m_LessonManager.IsLessonCompleted)
            {
                m_LessonManager.CompleteCurrentStep();
            }
        }
    }
}
