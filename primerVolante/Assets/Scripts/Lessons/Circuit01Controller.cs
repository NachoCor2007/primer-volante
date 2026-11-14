using System;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Controlador integral para el Circuito Integrador 1 (Circuit_01).
    /// Supervisa el recorrido urbano guiado por GPS y valida la finalización
    /// del circuito cuando el vehículo ingresa y estaciona correctamente en
    /// la dársena de meta demarcada en amarillo (ParkingZone_Finish).
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class Circuit01Controller : MonoBehaviour
    {
        private static Circuit01Controller s_Instance;

        [Header("Subsistemas del Circuito")]
        [Tooltip("Gestor de lecciones asociado.")]
        [SerializeField] private LessonManager m_LessonManager;

        [Tooltip("Controlador del vehículo del jugador.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Panel HUD sobre el volante.")]
        [SerializeField] private LessonHUDPanel m_HUDPanel;

        [Header("Dársena de Meta")]
        [Tooltip("Trigger de detección en el recuadro amarillo final.")]
        [SerializeField] private ParkingZoneTrigger m_FinishZone;

        [Header("Configuración Inicial")]
        [Tooltip("Encender el motor automáticamente al iniciar el circuito. Apagado por defecto: el alumno arranca el auto.")]
        [SerializeField] private bool m_AutoStartEngine = false;

        public static Circuit01Controller Instance => s_Instance;
        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public ParkingZoneTrigger FinishZone => m_FinishZone;

        /// <summary>
        /// Evalúa si el vehículo completó el recorrido y quedó estacionado en el sector amarillo final.
        /// </summary>
        public bool IsFinished
        {
            get
            {
                if (m_FinishZone == null)
                {
                    AutoDiscoverReferences();
                    if (m_FinishZone == null) return false;
                }

                if (m_VehicleController == null)
                {
                    AutoDiscoverReferences();
                    if (m_VehicleController == null) return false;
                }

                // 1. Si ParkingZoneTrigger valida la condición completa de estacionamiento
                if (m_FinishZone.IsVehicleParkedProperly)
                {
                    return true;
                }

                // 2. Validación robusta directa:
                // - Auto adentro del trigger
                // - Auto completamente contenido dentro del recuadro
                // - Detenido (velocidad <= 0.8 km/h)
                // - Orientación alineada con la dársena (ángulo <= 25°)
                // - Transmisión en Parking (P), Neutro (N) o freno de mano activado
                if (m_FinishZone.IsVehicleInside && m_FinishZone.IsVehicleFullyInside(m_VehicleController, 0.40f))
                {
                    bool isStopped = m_VehicleController.CurrentSpeedKmh <= 0.8f;
                    Vector3 targetFwd = m_FinishZone.GetTargetForwardDirection();
                    float angle = Vector3.Angle(m_VehicleController.transform.forward, targetFwd);
                    bool isAligned = angle <= 25f;

                    bool isSecuredOrParked = (m_VehicleController.CurrentGear == GearState.Park ||
                                              m_VehicleController.CurrentGear == GearState.P ||
                                              m_VehicleController.CurrentGear == GearState.Neutral ||
                                              m_VehicleController.CurrentGear == GearState.N ||
                                              m_VehicleController.HandbrakeEngagement >= 0.75f);

                    return isStopped && isAligned && isSecuredOrParked;
                }

                return false;
            }
        }

        private void Awake()
        {
            if (s_Instance == null) s_Instance = this;
            AutoDiscoverReferences();
        }

        private void OnEnable()
        {
            if (s_Instance == null) s_Instance = this;
        }

        private void Start()
        {
            AutoDiscoverReferences();

            // Opcional: encender el motor al iniciar (desactivado por defecto, el auto arranca apagado)
            if (m_AutoStartEngine && m_VehicleController != null && !m_VehicleController.IsEngineRunning)
            {
                m_VehicleController.SetEngineRunning(true);
            }
        }

        /// <summary>
        /// Vincula automáticamente las dependencias de escena mediante Object.FindAnyObjectByType.
        /// </summary>
        public void AutoDiscoverReferences()
        {
            if (m_LessonManager == null)
            {
                m_LessonManager = GetComponent<LessonManager>() ??
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

            if (m_FinishZone == null)
            {
                GameObject finishObj = GameObject.Find("ParkingZone_Finish");
                if (finishObj != null)
                {
                    m_FinishZone = finishObj.GetComponent<ParkingZoneTrigger>();
                }
                if (m_FinishZone == null)
                {
                    m_FinishZone = UnityEngine.Object.FindAnyObjectByType<ParkingZoneTrigger>();
                }
            }
        }
    }
}

