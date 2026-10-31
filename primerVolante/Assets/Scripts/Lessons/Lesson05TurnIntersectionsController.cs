using System;
using System.Collections;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.UI;
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
        [SerializeField] private TrafficLightController m_SecondaryTrafficLight;
        [SerializeField] private InfractionManager m_InfractionManager;
        [SerializeField] private GPSRouteRenderer m_RouteRenderer;

        [Header("Puntos de Partida de Cada Paso")]
        [Tooltip("Transform de spawn para cada uno de los 5 pasos de la Lección 5.")]
        [SerializeField] private Transform[] m_StepSpawnPoints = new Transform[5];

        [Header("Referencias Geométricas de Paso")]
        [Tooltip("Marca visual o línea de advertencia en el asfalto (a 20m de la esquina).")]
        [SerializeField] private Transform m_WarningLine20m;

        [Tooltip("Pórtico / Barrera translúcida holográfica flotante de advertencia (20 m).")]
        [SerializeField] private GameObject m_WarningGateHologram;

        [Tooltip("Punto de detención antes de la senda peatonal para el semáforo.")]
        [SerializeField] private Transform m_StopLineTrafficLight;

        [Header("Tiempos de Transición")]
        [SerializeField] private float m_FadeDuration = 0.35f;

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
        public TrafficLightController TrafficLight => m_TrafficLight;
        public TrafficLightController SecondaryTrafficLight => m_SecondaryTrafficLight;

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
                GameObject light10 = GameObject.Find("Props_Traffic Signal_big (10)");
                if (light10 != null)
                {
                    m_TrafficLight = light10.GetComponent<TrafficLightController>();
                }
                if (m_TrafficLight == null)
                {
                    m_TrafficLight = UnityEngine.Object.FindAnyObjectByType<TrafficLightController>();
                }
            }

            if (m_SecondaryTrafficLight == null)
            {
                GameObject smallLight = GameObject.Find("TrafficSignal_Step03_Small");
                if (smallLight != null)
                {
                    m_SecondaryTrafficLight = smallLight.GetComponent<TrafficLightController>();
                }
            }

            if (m_InfractionManager == null)
            {
                m_InfractionManager = UnityEngine.Object.FindAnyObjectByType<InfractionManager>();
            }

            if (m_WarningGateHologram == null)
            {
                m_WarningGateHologram = GameObject.Find("WarningGate_20m_Hologram");
            }

            if (m_RouteRenderer == null && m_VehicleController != null)
            {
                m_RouteRenderer = m_VehicleController.GetComponent<GPSRouteRenderer>() ??
                                  m_VehicleController.GetComponentInChildren<GPSRouteRenderer>(true);
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
            UpdateVisualIndicators(0);
        }

        private void OnStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            m_CurrentStepIndex = stepIndex;
            m_IsCompletingStep = false;
            m_IsRetrying = false;

            // Si pasamos a un paso subsiguiente (stepIndex > 0), realizar una transición limpia
            // con reposición automática en el SpawnPoint correspondiente para arrancar de cero de forma prolija.
            if (stepIndex > 0 && gameObject.activeInHierarchy)
            {
                StartCoroutine(TransitionToStepRoutine(stepIndex));
            }
            else
            {
                ResetStepMetrics(stepIndex);
                UpdateVisualIndicators(stepIndex);
            }
        }

        private IEnumerator TransitionToStepRoutine(int stepIndex)
        {
            m_IsRetrying = true;

            yield return new WaitForSeconds(0.2f);

            // 1. Fade Out suave (0.35s)
            if (m_ScreenFader != null)
            {
                yield return m_ScreenFader.FadeOutRoutine(m_FadeDuration);
            }
            else
            {
                yield return new WaitForSeconds(m_FadeDuration);
            }

            // 2. Reposicionar vehículo al spawn point del paso
            RepositionVehicleToStepStart(stepIndex);

            // 3. Reiniciar detector de guiños y posición de visor
            if (m_TurnSignalDetector != null)
            {
                m_TurnSignalDetector.ResetTracking();
            }

            if (m_SeatFollower != null)
            {
                m_SeatFollower.SnapToSeat();
            }

            ResetStepMetrics(stepIndex);
            UpdateVisualIndicators(stepIndex);

            if (m_HUDPanel != null && m_LessonManager != null && m_LessonManager.CurrentStep != null)
            {
                m_HUDPanel.DisplayStep(m_LessonManager.CurrentStep, stepIndex, m_LessonManager.ActiveLesson.steps.Count);
            }

            // 4. Fade In suave (0.35s)
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
        /// Activa u oculta indicadores visuales contextuales según el paso activo.
        /// </summary>
        public void UpdateVisualIndicators(int stepIndex)
        {
            // La franja en el asfalto y el pórtico holográfico de advertencia a 20m se activan en el Paso 2 (índice 1)
            bool show20mWarning = (stepIndex == 1);
            if (m_WarningLine20m != null)
            {
                m_WarningLine20m.gameObject.SetActive(show20mWarning);
            }
            if (m_WarningGateHologram != null)
            {
                m_WarningGateHologram.SetActive(show20mWarning);
            }
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

            // Configurar el semáforo para el paso 3 (Cruce semaforizado): arranca en Rojo y con ciclo pausado
            if (stepIndex == 2)
            {
                if (m_TrafficLight != null)
                {
                    m_TrafficLight.PauseCycle();
                    m_TrafficLight.SetState(TrafficLightState.Red);
                }
                if (m_SecondaryTrafficLight != null)
                {
                    m_SecondaryTrafficLight.PauseCycle();
                    m_SecondaryTrafficLight.SetState(TrafficLightState.Red);
                }
            }

            if (m_RouteRenderer != null)
            {
                m_RouteRenderer.SplineIndex = stepIndex;
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
                // Solo sancionar si no se había completado la detención reglamentaria ante la senda
                if (!m_StoppedAtRedLight)
                {
                    TriggerTurnSignalRetry("Falta: Cruce con semáforo en rojo");
                }
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

            // Asegurar motor en marcha y transmisión en Drive (salvo paso 5)
            m_VehicleController.SetEngineRunning(true);
            if (stepIndex < 4)
            {
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

            // Para el Paso 3 (Cruce con semáforo), asegurar semáforos en Rojo y ciclo pausado al posicionar
            if (stepIndex == 2)
            {
                if (m_TrafficLight != null)
                {
                    m_TrafficLight.PauseCycle();
                    m_TrafficLight.SetState(TrafficLightState.Red);
                }
                if (m_SecondaryTrafficLight != null)
                {
                    m_SecondaryTrafficLight.PauseCycle();
                    m_SecondaryTrafficLight.SetState(TrafficLightState.Red);
                }
            }

            // Actualizar la ruta del GPS para que se sincronice con el nuevo spawn del paso
            if (m_RouteRenderer != null)
            {
                m_RouteRenderer.SplineIndex = stepIndex;
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
            Vector3 carPos = m_VehicleController.transform.position;

            switch (step.requiredAction)
            {
                case LessonActionType.CompleteTurnSignalRightIntro:
                    // Paso 1: El auto arranca detenido cerca de la esquina (X=63.5, Z=110, rumbo Norte).
                    // Debe avanzar, haber accionado el guiño a la derecha y completado el giro de 90° hacia el Este (+X)
                    if (distanceTraveled >= 8.0f && deltaHeading >= 70f && deltaHeading <= 110f)
                    {
                        if (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault)
                        {
                            m_IsTurnIntroCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.TurnSignalAnticipation20m:
                    // Paso 2: Guiño antes de la línea/portal de 20m y completar giro a la derecha a 90° hacia el Sur (-Z)
                    if (m_WarningLine20m != null)
                    {
                        // Auto viaja hacia el Este (+X) desde X=66 hacia la esquina X=110.5. La línea está en X=90.5.
                        float distAlongX = m_WarningLine20m.position.x - carPos.x;
                        if (distAlongX > 0f && m_VehicleController.CurrentTurnSignal == TurnSignalState.Right)
                        {
                            m_TurnSignalPlacedBefore20m = true;
                        }
                        else if (distAlongX <= -0.5f && !m_TurnSignalPlacedBefore20m && !m_IsRetrying)
                        {
                            // Si cruzó la línea o portal de 20m sin poner el guiño, se reinicia el paso
                            TriggerTurnSignalRetry("Falta: Debes colocar el guiño ANTES de cruzar el portal de los 20 metros");
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
                    if (distanceTraveled >= 22f && deltaHeading >= 70f && deltaHeading <= 110f)
                    {
                        if (m_TurnSignalPlacedBefore20m && (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault))
                        {
                            m_IsAnticipationTurnCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.CrossTrafficLightIntersection:
                    // Paso 3: Semáforo y Giro a la Derecha (90°)
                    // Auto viaja hacia el Sur (-Z) desde Z=95 hacia la esquina Z=63.5.
                    // Semáforo en (112.38, 0.20, 67.58) mirando al Norte. Senda en Z=71.5.

                    // Subfase A: Detención reglamentaria ante la luz roja antes de la senda
                    if (!m_StoppedAtRedLight)
                    {
                        // Rango de detención generoso antes de la senda (Z entre 68.0 y 86.0 m) a velocidad de frenado (< 0.8 km/h)
                        if (carPos.z <= 86.0f && carPos.z >= 68.0f && m_VehicleController.CurrentSpeedKmh < 0.8f)
                        {
                            m_RedLightStopTimer += Time.deltaTime;

                            // Tras 2 segundos de detención reglamentaria, pasar el semáforo a Verde y pausar el ciclo
                            if (m_RedLightStopTimer >= 2.0f)
                            {
                                m_StoppedAtRedLight = true;
                                if (m_TrafficLight != null)
                                {
                                    m_TrafficLight.SetState(TrafficLightState.Green);
                                    m_TrafficLight.PauseCycle();
                                }
                                if (m_SecondaryTrafficLight != null)
                                {
                                    m_SecondaryTrafficLight.SetState(TrafficLightState.Green);
                                    m_SecondaryTrafficLight.PauseCycle();
                                }
                            }
                        }
                        else if (carPos.z < 68.0f && !m_IsRetrying)
                        {
                            // Infracción: avanzó y cruzó la senda (Z < 68m) sin haberse detenido reglamentariamente
                            TriggerTurnSignalRetry("Falta: Cruzaste la senda peatonal con el semáforo en rojo");
                            return;
                        }
                    }

                    // Subfase B: Tras ponerse en verde, completar el giro a la derecha a 90° e incorporarse a la calle Z=63.5 (X <= 110m)
                    if (m_StoppedAtRedLight)
                    {
                        if (deltaHeading >= 65f && deltaHeading <= 115f && carPos.x <= 110.0f)
                        {
                            if (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault)
                            {
                                m_IsTrafficLightCrossCompleted = true;
                                if (m_TrafficLight != null)
                                {
                                    m_TrafficLight.ResumeCycle();
                                }
                                if (m_SecondaryTrafficLight != null)
                                {
                                    m_SecondaryTrafficLight.ResumeCycle();
                                }
                            }
                        }
                    }
                    break;

                case LessonActionType.CompleteLeftTurnCrossing:
                    // Paso 4: Giro 90° a la IZQUIERDA en intersección con anticipación de 20 m y sin contramano
                    // Auto viaja hacia el Oeste (-X) desde X=105 hacia la esquina X=60 en Z=63.5.
                    float distToLeftTurnCorner = carPos.x - 60.0f;
                    if (distToLeftTurnCorner > 18.0f && m_VehicleController.CurrentTurnSignal == TurnSignalState.Left)
                    {
                        m_TurnSignalPlacedBefore20m = true;
                    }
                    else if (distToLeftTurnCorner <= 1.0f && !m_TurnSignalPlacedBefore20m && !m_IsRetrying)
                    {
                        TriggerTurnSignalRetry("Falta: Debes colocar el guiño a la izquierda con más de 20 metros de anticipación");
                        return;
                    }

                    // Giro a la izquierda completado: deltaHeading negativo (-70° a -115°)
                    // e ingresado al carril correspondiente de la calle X=60 rumbo Sur (Z <= 54.0f)
                    if (deltaHeading <= -70f && deltaHeading >= -115f && carPos.z <= 54.0f)
                    {
                        bool noWrongWay = (m_WrongWayDetector == null || !m_WrongWayDetector.IsWrongWay);
                        bool noTurnFault = (m_TurnSignalDetector == null || m_TurnSignalDetector.LastOutcome != TurnSignalOutcome.Fault);
                        if (noWrongWay && noTurnFault && m_TurnSignalPlacedBefore20m)
                        {
                            m_IsLeftTurnCompleted = true;
                        }
                    }
                    break;

                case LessonActionType.CurbsideStop:
                    // Paso 5: Arrime a cordón derecho en calle X=60 rumbo Sur, detenido, Parking (P) y freno de mano
                    // En calle X=60 rumbo Sur, el cordón derecho está en X <= 55.0f
                    bool atRightCurb = carPos.x <= 55.0f;
                    bool stopped = m_VehicleController.CurrentSpeedKmh < 0.2f && distanceTraveled >= 4.0f;
                    bool park = (m_VehicleController.CurrentGear == GearState.Park || m_VehicleController.CurrentGear == GearState.P);
                    bool handbrake = m_VehicleController.HandbrakeEngagement >= 0.85f;
                    if (stopped && park && handbrake && atRightCurb)
                    {
                        m_IsCurbsideStopCompleted = true;
                    }
                    break;
            }
        }
    }
}
