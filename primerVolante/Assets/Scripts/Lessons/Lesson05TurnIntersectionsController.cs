using System;
using System.Collections;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Controlador integral de la Lección 5: Giros e Intersecciones.
    /// Supervisa la señalización dinámica (anticipación de 20 m), la semaforización urbana,
    /// el giro amplio sin invasión de contramano y la parada junto al cordón.
    /// Implementa la mecánica de reintento de paso en Realidad Virtual (Fade Out 0.4s -> Reposición -> ResetTracking -> Fade In 0.4s)
    /// garantizando confort visual y cero mareos o frustración.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class Lesson05TurnIntersectionsController : MonoBehaviour
    {
        private static Lesson05TurnIntersectionsController s_Instance;

        [Header("Subsistemas de Lección")]
        [SerializeField] private LessonManager m_LessonManager;
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private LessonHUDPanel m_HUDPanel;
        [SerializeField] private TurnSignalDetector m_TurnSignalDetector;
        [SerializeField] private WrongWayDetector m_WrongWayDetector;
        [SerializeField] private VRScreenFader m_ScreenFader;
        [SerializeField] private DriverSeatFollower m_SeatFollower;
        [SerializeField] private TrafficLightController m_TrafficLight;
        [SerializeField] private InfractionManager m_InfractionManager;

        [Header("Puntos de Partida de Cada Paso")]
        [Tooltip("Transform de spawn para cada uno de los 5 pasos de la Lección 5.")]
        [SerializeField] private Transform[] m_StepSpawnPoints = new Transform[5];

        [Header("Referencias Geométricas de Paso")]
        [Tooltip("Marca visual o línea de advertencia en el asfalto (a 20m de la esquina).")]
        [SerializeField] private Transform m_WarningLine20m;

        [Tooltip("Punto de detención antes de la senda peatonal para el semáforo.")]
        [SerializeField] private Transform m_StopLineTrafficLight;

        [Header("Tiempos de Transición")]
        [SerializeField] private float m_FadeDuration = 0.4f;

        // Métricas internas de seguimiento de pasos
        private Vector3 m_StepStartPosition;
        private Quaternion m_StepStartRotation;
        private float m_StepStartHeading;
        private bool m_TurnSignalPlacedBefore20m = false;
        private bool m_StoppedAtRedLight = false;
        private float m_RedLightStopTimer = 0f;
        private bool m_IsRetrying = false;
        private bool m_IsCompletingStep = false;
        private int m_CurrentStepIndex = -1;

        // Estados booleanos de compleción real de maniobras
        private bool m_IsTurnIntroCompleted = false;
        private bool m_IsAnticipationTurnCompleted = false;
        private bool m_IsTrafficLightCrossCompleted = false;
        private bool m_IsLeftTurnCompleted = false;
        private bool m_IsCurbsideStopCompleted = false;

        public static Lesson05TurnIntersectionsController Instance => s_Instance;
        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public TurnSignalDetector TurnDetector => m_TurnSignalDetector;
        public VRScreenFader ScreenFader => m_ScreenFader;
        public bool IsRetrying => m_IsRetrying;

        // Propiedades públicas para consulta desde LessonActionMonitor
        public bool IsTurnIntroCompleted => m_IsTurnIntroCompleted;
        public bool IsAnticipationTurnCompleted => m_IsAnticipationTurnCompleted;
        public bool IsTrafficLightCrossCompleted => m_IsTrafficLightCrossCompleted;
        public bool IsLeftTurnCompleted => m_IsLeftTurnCompleted;
        public bool IsCurbsideStopCompleted => m_IsCurbsideStopCompleted;

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

            if (m_TurnSignalDetector == null && m_VehicleController != null)
            {
                m_TurnSignalDetector = m_VehicleController.GetComponent<TurnSignalDetector>() ??
                                      m_VehicleController.GetComponentInChildren<TurnSignalDetector>(true);
            }

            if (m_WrongWayDetector == null && m_VehicleController != null)
            {
                m_WrongWayDetector = m_VehicleController.GetComponent<WrongWayDetector>() ??
                                    m_VehicleController.GetComponentInChildren<WrongWayDetector>(true);
            }

            if (m_ScreenFader == null)
            {
                m_ScreenFader = VRScreenFader.EnsureScreenFader();
            }

            if (m_SeatFollower == null)
            {
                m_SeatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            }

            if (m_TrafficLight == null)
            {
                m_TrafficLight = UnityEngine.Object.FindAnyObjectByType<TrafficLightController>();
            }

            if (m_InfractionManager == null)
            {
                m_InfractionManager = UnityEngine.Object.FindAnyObjectByType<InfractionManager>();
            }
        }

        private void SubscribeEvents(bool subscribe)
        {
            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= OnStepChanged;
                m_LessonManager.LessonStarted -= OnLessonStarted;
                if (subscribe)
                {
                    m_LessonManager.StepChanged += OnStepChanged;
                    m_LessonManager.LessonStarted += OnLessonStarted;
                }
            }

            if (m_InfractionManager != null)
            {
                m_InfractionManager.InfractionRecorded -= OnInfractionRecorded;
                if (subscribe)
                {
                    m_InfractionManager.InfractionRecorded += OnInfractionRecorded;
                }
            }
        }

        private void OnLessonStarted(LessonDefinitionSO lesson)
        {
            if (lesson == null || lesson.lessonId != "LESSON_05") return;
            ResetStepMetrics(0);
        }

        private void OnStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            m_CurrentStepIndex = stepIndex;
            m_IsCompletingStep = false;
            m_IsRetrying = false;
            ResetStepMetrics(stepIndex);
        }

        private void ResetStepMetrics(int stepIndex)
        {
            m_IsTurnIntroCompleted = false;
            m_IsAnticipationTurnCompleted = false;
            m_IsTrafficLightCrossCompleted = false;
            m_IsLeftTurnCompleted = false;
            m_IsCurbsideStopCompleted = false;

            m_TurnSignalPlacedBefore20m = false;
            m_StoppedAtRedLight = false;
            m_RedLightStopTimer = 0f;

            if (m_VehicleController != null)
            {
                m_StepStartPosition = m_VehicleController.transform.position;
                m_StepStartRotation = m_VehicleController.transform.rotation;
                m_StepStartHeading = m_VehicleController.transform.eulerAngles.y;
            }

            // Configurar el semáforo para el paso 3 (Cruce semaforizado): arranca en Rojo
            if (stepIndex == 2 && m_TrafficLight != null)
            {
                m_TrafficLight.SetState(TrafficLightState.Red);
            }
        }

        private void OnInfractionRecorded(InfractionRecord record)
        {
            if (m_IsRetrying || m_IsCompletingStep) return;

            // Verificar si la falta corresponde a señalización o maniobra indebida en Lección 5
            if (record.Definition != null && record.Definition.Id == InfractionIds.TurnSignal)
            {
                TriggerTurnSignalRetry("Falta: Giro sin señalizar con anticipación de 20 metros");
            }
            else if (record.Definition != null && record.Definition.Id == InfractionIds.WrongWay && m_CurrentStepIndex == 3)
            {
                TriggerTurnSignalRetry("Falta: Invasión de contramano en giro a la izquierda");
            }
            else if (record.Definition != null && record.Definition.Id == InfractionIds.RedLight && m_CurrentStepIndex == 2)
            {
                TriggerTurnSignalRetry("Falta: Cruce con semáforo en rojo");
            }
        }

        /// <summary>
        /// Inicia la secuencia de reintento suave con Fade Out -> Reposición -> ResetTracking -> Fade In.
        /// </summary>
        public void TriggerTurnSignalRetry(string reason)
        {
            if (m_IsRetrying || m_IsCompletingStep) return;
            StartCoroutine(RetryStepRoutine(reason));
        }

        private IEnumerator RetryStepRoutine(string reason)
        {
            m_IsRetrying = true;

            // 1. Mostrar advertencia sonora y visual en rojo sobre el HUD
            if (m_HUDPanel != null)
            {
                m_HUDPanel.ShowStepWarning(reason);
            }

            // 2. Esperar a que el vehículo se detenga por completo para evitar mareo en VR
            while (m_VehicleController != null && m_VehicleController.CurrentSpeedKmh > 1.0f)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.4f);

            // 3. Fade Out a negro suave (0.4s)
            if (m_ScreenFader != null)
            {
                yield return m_ScreenFader.FadeOutRoutine(m_FadeDuration);
            }
            else
            {
                yield return new WaitForSeconds(m_FadeDuration);
            }

            // 4. Reposicionar el vehículo al punto de partida de este paso
            RepositionVehicleToStepStart(m_CurrentStepIndex);

            // 5. Reiniciar el tracking del detector de guiños
            if (m_TurnSignalDetector != null)
            {
                m_TurnSignalDetector.ResetTracking();
            }

            // 6. Sincronizar el asiento del visor
            if (m_SeatFollower != null)
            {
                m_SeatFollower.SnapToSeat();
            }

            // 7. Restaurar métricas y panel HUD al estado normal del paso
            ResetStepMetrics(m_CurrentStepIndex);
            if (m_HUDPanel != null && m_LessonManager != null && m_LessonManager.CurrentStep != null)
            {
                m_HUDPanel.DisplayStep(m_LessonManager.CurrentStep, m_CurrentStepIndex, m_LessonManager.ActiveLesson.steps.Count);
            }

            // 8. Fade In suave (0.4s)
            if (m_ScreenFader != null)
            {
                yield return m_ScreenFader.FadeInRoutine(m_FadeDuration);
            }
            else
            {
                yield return new WaitForSeconds(m_FadeDuration);
            }

            m_IsRetrying = false;
        }

        /// <summary>
        /// Reposiciona el vehículo físicamente en el punto de partida del paso especificado.
        /// </summary>
        public void RepositionVehicleToStepStart(int stepIndex)
        {
            if (m_VehicleController == null) return;

            Transform spawn = (stepIndex >= 0 && stepIndex < m_StepSpawnPoints.Length) ? m_StepSpawnPoints[stepIndex] : null;
            Vector3 targetPos = spawn != null ? spawn.position : m_StepStartPosition;
            Quaternion targetRot = spawn != null ? spawn.rotation : m_StepStartRotation;

            m_VehicleController.transform.position = targetPos;
            m_VehicleController.transform.rotation = targetRot;

            Rigidbody rb = m_VehicleController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = targetPos;
                rb.rotation = targetRot;
            }

            if (m_VehicleController.SteeringWheel != null)
            {
                m_VehicleController.SteeringWheel.ResetToCenter();
            }
            m_VehicleController.SetTurnSignal(TurnSignalState.Off);
            VRTurnSignal stalk = m_VehicleController.GetComponentInChildren<VRTurnSignal>(true);
            if (stalk != null)
            {
                stalk.SetSignalAnimated(TurnSignalState.Off);
            }
        }

        private void Update()
        {
            if (m_IsRetrying || m_IsCompletingStep || m_LessonManager == null || !m_LessonManager.IsLessonActive)
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

            float currentHeading = m_VehicleController.transform.eulerAngles.y;
            float deltaHeading = Mathf.DeltaAngle(m_StepStartHeading, currentHeading);
            float distanceTraveled = Vector3.Distance(m_StepStartPosition, m_VehicleController.transform.position);

            switch (step.requiredAction)
            {
                case LessonActionType.CompleteTurnSignalRightIntro:
                    // Paso 1: El auto arranca detenido cerca de la esquina.
                    // Debe avanzar al menos 8m, haber accionado el guiño a la derecha y completado el giro de 90° (deltaHeading 75° a 110°)
                    if (distanceTraveled >= 8.0f && deltaHeading >= 75f && deltaHeading <= 110f)
                    {
                        if (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault)
                        {
                            m_IsTurnIntroCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.TurnSignalAnticipation20m:
                    // Paso 2: Guiño antes de la línea de 20m y completar giro a la derecha a 90°
                    if (m_WarningLine20m != null)
                    {
                        float distToLine = Vector3.Dot(m_WarningLine20m.position - m_VehicleController.transform.position, m_WarningLine20m.forward);
                        if (distToLine > 0f && m_VehicleController.CurrentTurnSignal == TurnSignalState.Right)
                        {
                            m_TurnSignalPlacedBefore20m = true;
                        }
                        else if (distToLine <= -1.0f && !m_TurnSignalPlacedBefore20m && !m_IsRetrying)
                        {
                            // Si cruzó la línea sin poner el guiño, se le avisa y reinicia
                            TriggerTurnSignalRetry("Falta: Debes colocar el guiño ANTES de cruzar la franja en el asfalto");
                            return;
                        }
                    }
                    else
                    {
                        if (distanceTraveled < 20f && m_VehicleController.CurrentTurnSignal == TurnSignalState.Right)
                        {
                            m_TurnSignalPlacedBefore20m = true;
                        }
                    }

                    // Giro completado habiendo puesto el guiño previamente
                    if (distanceTraveled >= 25f && deltaHeading >= 75f && deltaHeading <= 110f)
                    {
                        if (m_TurnSignalPlacedBefore20m && (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault))
                        {
                            m_IsAnticipationTurnCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.CrossTrafficLightIntersection:
                    // Paso 3: Detenerse en rojo antes de la senda peatonal y cruzar en verde
                    if (m_TrafficLight != null && m_TrafficLight.CurrentState == TrafficLightState.Red)
                    {
                        // Verificar si está frenado ante el semáforo
                        if (m_VehicleController.CurrentSpeedKmh < 0.5f && distanceTraveled >= 5.0f)
                        {
                            m_StoppedAtRedLight = true;
                            m_RedLightStopTimer += Time.deltaTime;

                            // Tras 2 segundos de detención reglamentaria, pasar a Verde
                            if (m_RedLightStopTimer >= 2.0f)
                            {
                                m_TrafficLight.SetState(TrafficLightState.Green);
                            }
                        }
                    }

                    // Una vez en verde, el auto avanza y cruza la intersección
                    if (m_StoppedAtRedLight && m_TrafficLight != null && m_TrafficLight.CurrentState == TrafficLightState.Green)
                    {
                        if (distanceTraveled >= 22f && m_VehicleController.CurrentSpeedKmh > 2f)
                        {
                            m_IsTrafficLightCrossCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.CompleteLeftTurnCrossing:
                    // Paso 4: Giro 90° a la izquierda con más de 20 m de anticipación y sin contramano
                    // Giro a la izquierda: deltaHeading negativo (-75° a -115°)
                    if (distanceTraveled >= 12f && deltaHeading <= -75f && deltaHeading >= -115f)
                    {
                        bool noWrongWay = (m_WrongWayDetector == null || !m_WrongWayDetector.IsWrongWay);
                        bool noTurnFault = (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault);
                        if (noWrongWay && noTurnFault)
                        {
                            m_IsLeftTurnCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.CurbsideStop:
                    // Paso 5: Arrime a cordón, detenido, Parking (P) y freno de mano
                    bool stopped = m_VehicleController.CurrentSpeedKmh < 0.2f && distanceTraveled >= 4.0f;
                    bool park = (m_VehicleController.CurrentGear == GearState.Park || m_VehicleController.CurrentGear == GearState.P);
                    bool handbrake = m_VehicleController.HandbrakeEngagement >= 0.85f;
                    if (stopped && park && handbrake)
                    {
                        m_IsCurbsideStopCompleted = true;
                    }
                    break;
            }
        }
    }
}
