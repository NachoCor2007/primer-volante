using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Controlador integral de la Lección 7: Dominio del Volante y Maniobras con Vallas (Slalom y Reversa con Espejos).
    /// Supervisa:
    /// 1. Slalom hacia adelante en zig-zag esquivando vallas alternadas hasta la dársena de meta frontal.
    /// 2. Maniobra de slalom en reversa utilizando la regla del "medio auto" en los espejos retrovisores laterales.
    /// 3. Detención y aseguramiento del vehículo (P y freno de mano).
    /// Cumple estrictamente con la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class Lesson07BarrierManeuversController : MonoBehaviour
    {
        private static Lesson07BarrierManeuversController s_Instance;

        [Header("Subsistemas de Lección")]
        [SerializeField] private LessonManager m_LessonManager;
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private LessonHUDPanel m_HUDPanel;
        [SerializeField] private VRScreenFader m_ScreenFader;

        [Header("Dársenas de Maniobra")]
        [Tooltip("Trigger de detección para la dársena de inicio / retorno en reversa.")]
        [SerializeField] private ParkingZoneTrigger m_StartZone;

        [Tooltip("Trigger de detección para la dársena final del slalom hacia adelante.")]
        [SerializeField] private ParkingZoneTrigger m_ForwardFinishZone;

        [Header("Demarcación Visual en Asfalto (Recuadros Amarillos)")]
        [SerializeField] private GameObject m_StartZoneHighlight;
        [SerializeField] private GameObject m_ForwardZoneHighlight;

        [Header("Vallas de Slalom")]
        [SerializeField] private List<LessonBarrier> m_SlalomBarriers = new List<LessonBarrier>();

        [Header("Estadísticas de Lección")]
        [SerializeField] private int m_BarrierCollisions = 0;

        public static Lesson07BarrierManeuversController Instance => s_Instance;
        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public ParkingZoneTrigger StartZone => m_StartZone;
        public ParkingZoneTrigger ForwardFinishZone => m_ForwardFinishZone;
        public int BarrierCollisions => m_BarrierCollisions;

        /// <summary>
        /// Valida si el vehículo completó el slalom hacia adelante y quedó detenido dentro de la dársena frontal.
        /// </summary>
        public bool IsForwardSlalomComplete
        {
            get
            {
                if (m_ForwardFinishZone == null || m_VehicleController == null)
                {
                    AutoDiscoverReferences();
                    if (m_ForwardFinishZone == null || m_VehicleController == null) return false;
                }

                if (m_ForwardFinishZone.IsVehicleParkedProperly)
                {
                    return true;
                }

                bool isInside = m_ForwardFinishZone.IsVehicleInside;
                bool isStopped = m_VehicleController.CurrentSpeedKmh <= 1.2f;
                return isInside && isStopped;
            }
        }

        /// <summary>
        /// Valida si el vehículo completó el slalom en reversa y quedó detenido dentro de la dársena de inicio.
        /// </summary>
        public bool IsReverseSlalomComplete
        {
            get
            {
                if (m_StartZone == null || m_VehicleController == null)
                {
                    AutoDiscoverReferences();
                    if (m_StartZone == null || m_VehicleController == null) return false;
                }

                if (m_StartZone.IsVehicleParkedProperly)
                {
                    return true;
                }

                bool isInside = m_StartZone.IsVehicleInside;
                bool isStopped = m_VehicleController.CurrentSpeedKmh <= 1.2f;
                return isInside && isStopped;
            }
        }

        private void Awake()
        {
            s_Instance = this;
            AutoDiscoverReferences();
        }

        private void OnEnable()
        {
            s_Instance = this;
            SubscribeToBarriers();

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged += HandleStepChanged;
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromBarriers();

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= HandleStepChanged;
            }
        }

        private void HandleStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            UpdateHighlightsForStep(stepIndex);
        }

        /// <summary>
        /// Actualiza la visibilidad de los recuadros amarillos en el piso según el paso actual.
        /// </summary>
        public void UpdateHighlightsForStep(int stepIndex)
        {
            // Pasos 0 a 2 (hacia adelante): Resaltar la dársena frontal objetivo
            // Pasos 3 a 5 (reversa): Resaltar la dársena de retorno inicial
            if (m_ForwardZoneHighlight != null)
            {
                m_ForwardZoneHighlight.SetActive(stepIndex <= 2);
            }
            if (m_StartZoneHighlight != null)
            {
                m_StartZoneHighlight.SetActive(stepIndex >= 3);
            }
        }

        private void SubscribeToBarriers()
        {
            if (m_SlalomBarriers == null || m_SlalomBarriers.Count == 0)
            {
                m_SlalomBarriers = new List<LessonBarrier>(UnityEngine.Object.FindObjectsByType<LessonBarrier>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            foreach (var barrier in m_SlalomBarriers)
            {
                if (barrier != null)
                {
                    barrier.OnVehicleReached -= HandleBarrierContact;
                    barrier.OnVehicleReached += HandleBarrierContact;
                }
            }
        }

        private void UnsubscribeFromBarriers()
        {
            if (m_SlalomBarriers == null) return;
            foreach (var barrier in m_SlalomBarriers)
            {
                if (barrier != null)
                {
                    barrier.OnVehicleReached -= HandleBarrierContact;
                }
            }
        }

        private void HandleBarrierContact(LessonBarrierContact contact)
        {
            m_BarrierCollisions++;
            Debug.LogWarning($"[Lesson07BarrierManeuversController] ⚠️ ¡Contacto con valla '{contact.Barrier.name}' a {contact.SpeedKmh:F1} km/h! Total de toques: {m_BarrierCollisions}");
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
        }
    }
}
