using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Gestiona el bloqueo y desbloqueo selectivo de los mandos físicos de cabina
    /// durante las diferentes lecciones y pasos de manejo.
    /// Para bloquear un interactuable, altera su XRBaseInteractable.interactionLayers a 0
    /// (máscara vacía) preservando la máscara original para su posterior restauración.
    /// </summary>
    public class CockpitControlLockManager : MonoBehaviour
    {
        [Header("Mandos de Cabina")]
        [Tooltip("Volante de dirección del vehículo.")]
        [SerializeField] private VRSteeringWheel m_SteeringWheel;

        [Tooltip("Palanca de cambios.")]
        [SerializeField] private VRGearShifter m_GearShifter;

        [Tooltip("Freno de mano físico.")]
        [SerializeField] private VRHandbrake m_Handbrake;

        [Tooltip("Botón de arranque / apagado del motor.")]
        [SerializeField] private StartButton m_StartButton;

        [Tooltip("Botón de balizas (luces de emergencia).")]
        [SerializeField] private VRToggleButton m_HazardButton;

        [Tooltip("Palanca de guiños / luces de giro.")]
        [SerializeField] private VRTurnSignal m_TurnSignal;

        [Tooltip("Perilla selectora de luces reglamentarias.")]
        [SerializeField] private VRHeadlightKnob m_HeadlightKnob;

        private readonly Dictionary<XRBaseInteractable, InteractionLayerMask> m_OriginalLayers =
            new Dictionary<XRBaseInteractable, InteractionLayerMask>();

        public VRSteeringWheel SteeringWheel => m_SteeringWheel;
        public VRGearShifter GearShifter => m_GearShifter;
        public VRHandbrake Handbrake => m_Handbrake;
        public StartButton StartButtonControl => m_StartButton;
        public VRToggleButton HazardButton => m_HazardButton;
        public VRTurnSignal TurnSignal => m_TurnSignal;
        public VRHeadlightKnob HeadlightKnob => m_HeadlightKnob;

        private void Awake()
        {
            AutoDiscoverControls();
            CacheOriginalLayerMasks();
        }

        /// <summary>
        /// Descubre automáticamente los componentes interactuables en la jerarquía del vehículo si no están asignados.
        /// </summary>
        public void AutoDiscoverControls(Transform searchRoot = null)
        {
            if (searchRoot == null) searchRoot = transform;

            if (m_SteeringWheel == null) m_SteeringWheel = searchRoot.GetComponentInChildren<VRSteeringWheel>(true);
            if (m_GearShifter == null) m_GearShifter = searchRoot.GetComponentInChildren<VRGearShifter>(true);
            if (m_Handbrake == null) m_Handbrake = searchRoot.GetComponentInChildren<VRHandbrake>(true);
            if (m_StartButton == null) m_StartButton = searchRoot.GetComponentInChildren<StartButton>(true);
            if (m_HazardButton == null) m_HazardButton = searchRoot.GetComponentInChildren<VRToggleButton>(true);
            if (m_TurnSignal == null) m_TurnSignal = searchRoot.GetComponentInChildren<VRTurnSignal>(true);
            if (m_HeadlightKnob == null) m_HeadlightKnob = searchRoot.GetComponentInChildren<VRHeadlightKnob>(true);

            // Búsqueda en la escena utilizando APIs no obsoletas si aún no se encontraron
            if (m_SteeringWheel == null) m_SteeringWheel = UnityEngine.Object.FindAnyObjectByType<VRSteeringWheel>();
            if (m_GearShifter == null) m_GearShifter = UnityEngine.Object.FindAnyObjectByType<VRGearShifter>();
            if (m_Handbrake == null) m_Handbrake = UnityEngine.Object.FindAnyObjectByType<VRHandbrake>();
            if (m_StartButton == null) m_StartButton = UnityEngine.Object.FindAnyObjectByType<StartButton>();
            if (m_HazardButton == null) m_HazardButton = UnityEngine.Object.FindAnyObjectByType<VRToggleButton>();
            if (m_TurnSignal == null) m_TurnSignal = UnityEngine.Object.FindAnyObjectByType<VRTurnSignal>();
            if (m_HeadlightKnob == null) m_HeadlightKnob = UnityEngine.Object.FindAnyObjectByType<VRHeadlightKnob>();
        }

        /// <summary>
        /// Guarda en caché las máscaras originales de interacción para poder restaurarlas sin pérdida.
        /// </summary>
        public void CacheOriginalLayerMasks()
        {
            CacheInteractable(GetInteractable(CockpitControlFlags.SteeringWheel));
            CacheInteractable(GetInteractable(CockpitControlFlags.GearShifter));
            CacheInteractable(GetInteractable(CockpitControlFlags.Handbrake));
            CacheInteractable(GetInteractable(CockpitControlFlags.StartButton));
            CacheInteractable(GetInteractable(CockpitControlFlags.HazardButton));
            CacheInteractable(GetInteractable(CockpitControlFlags.TurnSignal));
            CacheInteractable(GetInteractable(CockpitControlFlags.HeadlightKnob));
        }

        private void CacheInteractable(XRBaseInteractable interactable)
        {
            if (interactable == null) return;
            if (!m_OriginalLayers.ContainsKey(interactable))
            {
                InteractionLayerMask mask = interactable.interactionLayers;
                if (mask.value == 0) mask = ~0; // Valor por defecto si estuviese en 0
                m_OriginalLayers[interactable] = mask;
            }
        }

        /// <summary>
        /// Obtiene el XRBaseInteractable correspondiente a un control de cabina.
        /// </summary>
        public XRBaseInteractable GetInteractable(CockpitControlFlags flag)
        {
            switch (flag)
            {
                case CockpitControlFlags.SteeringWheel:
                    return m_SteeringWheel != null ? m_SteeringWheel.GetComponent<XRBaseInteractable>() : null;
                case CockpitControlFlags.GearShifter:
                    return m_GearShifter != null ? m_GearShifter.GetComponent<XRBaseInteractable>() : null;
                case CockpitControlFlags.Handbrake:
                    return m_Handbrake != null ? m_Handbrake.GetComponent<XRBaseInteractable>() : null;
                case CockpitControlFlags.StartButton:
                    return m_StartButton != null ? m_StartButton.GetComponent<XRBaseInteractable>() : null;
                case CockpitControlFlags.HazardButton:
                    return m_HazardButton != null ? m_HazardButton.GetComponent<XRBaseInteractable>() : null;
                case CockpitControlFlags.TurnSignal:
                    return m_TurnSignal != null ? m_TurnSignal.GetComponent<XRBaseInteractable>() : null;
                case CockpitControlFlags.HeadlightKnob:
                    return m_HeadlightKnob != null ? m_HeadlightKnob.GetComponent<XRBaseInteractable>() : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Indica si un interactuable específico está bloqueado (máscara de interacción vacía).
        /// </summary>
        public bool IsInteractableLocked(XRBaseInteractable interactable)
        {
            if (interactable == null) return false;
            return interactable.interactionLayers.value == 0;
        }

        /// <summary>
        /// Indica si un control de cabina está bloqueado.
        /// </summary>
        public bool IsControlLocked(CockpitControlFlags flag)
        {
            XRBaseInteractable interactable = GetInteractable(flag);
            return IsInteractableLocked(interactable);
        }

        /// <summary>
        /// Bloquea o desbloquea un interactuable individual.
        /// </summary>
        public void SetInteractableLock(XRBaseInteractable interactable, bool locked)
        {
            if (interactable == null) return;

            if (locked)
            {
                CacheInteractable(interactable);
                interactable.interactionLayers = 0;
            }
            else
            {
                if (m_OriginalLayers.TryGetValue(interactable, out InteractionLayerMask mask))
                {
                    interactable.interactionLayers = mask;
                }
                else if (interactable.interactionLayers.value == 0)
                {
                    interactable.interactionLayers = ~0;
                }
            }
        }

        /// <summary>
        /// Bloquea o desbloquea un mando de cabina según su flag.
        /// </summary>
        public void SetControlLock(CockpitControlFlags flag, bool locked)
        {
            XRBaseInteractable interactable = GetInteractable(flag);
            SetInteractableLock(interactable, locked);
        }

        /// <summary>
        /// Aplica la máscara de controles permitidos para el paso actual.
        /// Todo control no incluido en 'allowedControls' es bloqueado.
        /// </summary>
        public void ApplyAllowedControls(CockpitControlFlags allowedControls)
        {
            CacheOriginalLayerMasks();

            SetControlLock(CockpitControlFlags.SteeringWheel, !allowedControls.HasFlag(CockpitControlFlags.SteeringWheel));
            SetControlLock(CockpitControlFlags.GearShifter, !allowedControls.HasFlag(CockpitControlFlags.GearShifter));
            SetControlLock(CockpitControlFlags.Handbrake, !allowedControls.HasFlag(CockpitControlFlags.Handbrake));
            SetControlLock(CockpitControlFlags.StartButton, !allowedControls.HasFlag(CockpitControlFlags.StartButton));
            SetControlLock(CockpitControlFlags.HazardButton, !allowedControls.HasFlag(CockpitControlFlags.HazardButton));
            SetControlLock(CockpitControlFlags.TurnSignal, !allowedControls.HasFlag(CockpitControlFlags.TurnSignal));
            SetControlLock(CockpitControlFlags.HeadlightKnob, !allowedControls.HasFlag(CockpitControlFlags.HeadlightKnob));
        }

        /// <summary>
        /// Desbloquea todos los controles de cabina.
        /// </summary>
        public void UnlockAll()
        {
            ApplyAllowedControls(CockpitControlFlags.All);
        }
    }
}
