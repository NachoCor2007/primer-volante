using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Controlador visual y cinemático de manos VR en reemplazo de los mandos tradicionales.
    /// Gestiona la postura contextual de los dedos (volante, perilla, cambios, freno de mano, espejo),
    /// el seguimiento rígido con sincronización angular cuando se agarra un control del vehículo,
    /// y el retorno suave al tracking del mando al soltar.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class VRHandPoseController : MonoBehaviour
    {
        [Header("Configuración de Mano")]
        [Tooltip("Indica si este controlador corresponde a la mano izquierda (true) o derecha (false).")]
        [SerializeField] private bool m_IsLeftHand = true;

        [Tooltip("Transform raíz del modelo visual de la mano.")]
        [SerializeField] private Transform m_HandVisual;

        [Tooltip("GameObject del modelo de mando tradicional que debe ocultarse cuando la mano esté activa.")]
        [SerializeField] private GameObject m_ControllerVisualToHide;

        [Tooltip("Material aplicado al SkinnedMeshRenderer de la mano (color piel natural).")]
        [SerializeField] private Material m_HandMaterial;

        [Header("Cinemática y Transiciones")]
        [Tooltip("Velocidad de transición angular entre posturas de los dedos.")]
        [SerializeField] private float m_PoseTransitionSpeed = 16f;

        [Tooltip("Tiempo de retorno suave (en segundos) desde el objeto agarrado hasta la posición actual del mando al soltar.")]
        [SerializeField] private float m_ReleaseReturnDuration = 0.18f;

        [Header("Postura Actual")]
        [Tooltip("Postura de mano que se está reproduciendo actualmente.")]
        [SerializeField] private HandPoseType m_CurrentPose = HandPoseType.Default;

        [Header("Entradas de Mando (Dedos Libres)")]
        [Tooltip("Acción de Input System para el gatillo (índice)")]
        [SerializeField] private InputActionProperty m_TriggerAction;

        [Tooltip("Acción de Input System para el agarre/grip (dedos medio, anular, meñique)")]
        [SerializeField] private InputActionProperty m_GripAction;

        // Estructuras de articulaciones de los dedos
        [System.Serializable]
        private class FingerJoints
        {
            public Transform proximal;
            public Transform intermediate;
            public Transform distal;

            [HideInInspector] public Quaternion initialProximalRot;
            [HideInInspector] public Quaternion initialIntermediateRot;
            [HideInInspector] public Quaternion initialDistalRot;
        }

        private FingerJoints m_Thumb = new FingerJoints();
        private FingerJoints m_Index = new FingerJoints();
        private FingerJoints m_Middle = new FingerJoints();
        private FingerJoints m_Ring = new FingerJoints();
        private FingerJoints m_Pinky = new FingerJoints();
        private bool m_BonesMapped = false;

        // Curls actuales y objetivo (0 = extendido, 1 = flexionado al máximo)
        private struct FingerCurls
        {
            public float thumb;
            public float index;
            public float middle;
            public float ring;
            public float pinky;
        }

        private FingerCurls m_CurrentCurls;
        private FingerCurls m_TargetCurls;

        // Lista de interactores de agarre suscritos (NearFar, Direct, Ray, excluyendo Poke)
        private readonly List<IXRSelectInteractor> m_Interactors = new List<IXRSelectInteractor>();

        // Estado de agarre y anclaje
        private VRHandPoseTarget m_ActiveTarget;
        private Transform m_ActiveTargetTransform;
        private Vector3 m_LocalGripPosition;
        private Quaternion m_LocalGripRotation;
        private bool m_IsHoldingTarget = false;

        // Transición de soltado (Release return)
        private bool m_IsReturningFromRelease = false;
        private float m_ReleaseTimer = 0f;
        private Vector3 m_ReleaseWorldPos;
        private Quaternion m_ReleaseWorldRot;

        public bool IsLeftHand => m_IsLeftHand;
        public HandPoseType CurrentPose => m_CurrentPose;
        public bool IsHoldingTarget => m_IsHoldingTarget;
        public Transform HandVisual => m_HandVisual;
        public bool BonesMapped => m_BonesMapped;

        private void Awake()
        {
            FindAndSubscribeInteractors();
            InitializeHandVisual();
        }

        private void OnEnable()
        {
            FindAndSubscribeInteractors();
        }

        private void OnDisable()
        {
            UnsubscribeInteractors();
        }

        private void FindAndSubscribeInteractors()
        {
            UnsubscribeInteractors();

            // Buscar interactores en este GameObject y en todos sus hijos (incluyendo NearFarInteractor)
            IXRSelectInteractor[] found = GetComponentsInChildren<IXRSelectInteractor>(true);
            foreach (var interactor in found)
            {
                // Ignorar XRPokeInteractor para que no interfiera con el agarre
                if (interactor is XRPokeInteractor) continue;

                if (!m_Interactors.Contains(interactor))
                {
                    m_Interactors.Add(interactor);
                    interactor.selectEntered.AddListener(OnSelectEntered);
                    interactor.selectExited.AddListener(OnSelectExited);
                }
            }
        }

        private void UnsubscribeInteractors()
        {
            foreach (var interactor in m_Interactors)
            {
                if (interactor != null)
                {
                    interactor.selectEntered.RemoveListener(OnSelectEntered);
                    interactor.selectExited.RemoveListener(OnSelectExited);
                }
            }
            m_Interactors.Clear();
        }

        /// <summary>
        /// Asigna y configura el modelo visual de la mano, ocultando el mando tradicional y mapeando huesos.
        /// </summary>
        public void SetHandVisual(Transform handVisual, bool isLeft)
        {
            m_HandVisual = handVisual;
            m_IsLeftHand = isLeft;
            InitializeHandVisual();
        }

        /// <summary>
        /// Configura el GameObject del mando tradicional a ocultar.
        /// </summary>
        public void SetControllerVisualToHide(GameObject visual)
        {
            m_ControllerVisualToHide = visual;
            if (m_ControllerVisualToHide != null && m_HandVisual != null && m_HandVisual.gameObject.activeInHierarchy)
            {
                m_ControllerVisualToHide.SetActive(false);
            }
        }

        public void InitializeHandVisual()
        {
            if (m_HandVisual == null) return;

            // Ocultar modelo del mando tradicional si existe
            if (m_ControllerVisualToHide != null)
            {
                m_ControllerVisualToHide.SetActive(false);
            }

            // Aplicar material color piel personalizado y asegurar updateWhenOffscreen para evitar culling de cámara
            var smr = m_HandVisual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                smr.updateWhenOffscreen = true;
                if (m_HandMaterial != null)
                {
                    smr.material = m_HandMaterial;
                }
            }

            MapBones();
            SetPose(m_CurrentPose, instant: true);
        }

        /// <summary>
        /// Detecta y mapea la jerarquía de huesos de la mano automáticamente.
        /// Compatible con XR Hands estándar (OpenXR), Urban Man, Mixamo y PolyOne VR Hands.
        /// </summary>
        public void MapBones()
        {
            if (m_HandVisual == null) return;

            m_Thumb = new FingerJoints();
            m_Index = new FingerJoints();
            m_Middle = new FingerJoints();
            m_Ring = new FingerJoints();
            m_Pinky = new FingerJoints();

            Transform[] allBones = m_HandVisual.GetComponentsInChildren<Transform>(true);

            foreach (Transform t in allBones)
            {
                string name = t.name.ToLowerInvariant();

                bool matchesSide = m_IsLeftHand
                    ? (name.StartsWith("l_") || name.EndsWith(".l") || name.EndsWith("_l") || name.Contains("left") || (!name.StartsWith("r_") && !name.EndsWith(".r") && !name.EndsWith("_r") && !name.Contains("right")))
                    : (name.StartsWith("r_") || name.EndsWith(".r") || name.EndsWith("_r") || name.Contains("right") || (!name.StartsWith("l_") && !name.EndsWith(".l") && !name.EndsWith("_l") && !name.Contains("left")));

                if (!matchesSide) continue;

                if (name.Contains("thumb"))
                {
                    MapJointToFinger(t, name, m_Thumb);
                }
                else if (name.Contains("index"))
                {
                    MapJointToFinger(t, name, m_Index);
                }
                else if (name.Contains("middle"))
                {
                    MapJointToFinger(t, name, m_Middle);
                }
                else if (name.Contains("ring"))
                {
                    MapJointToFinger(t, name, m_Ring);
                }
                else if (name.Contains("pinky") || name.Contains("little"))
                {
                    MapJointToFinger(t, name, m_Pinky);
                }
            }

            RecordRestRotation(m_Thumb);
            RecordRestRotation(m_Index);
            RecordRestRotation(m_Middle);
            RecordRestRotation(m_Ring);
            RecordRestRotation(m_Pinky);

            m_BonesMapped = (m_Index.proximal != null || m_Middle.proximal != null);
        }

        private void MapJointToFinger(Transform t, string lowerName, FingerJoints finger)
        {
            if (lowerName.Contains("proximal") || lowerName.Contains("1") || lowerName.Contains("metacarpal"))
            {
                if (finger.proximal == null || lowerName.Contains("proximal"))
                    finger.proximal = t;
            }
            else if (lowerName.Contains("intermediate") || lowerName.Contains("2"))
            {
                finger.intermediate = t;
            }
            else if (lowerName.Contains("distal") || lowerName.Contains("3"))
            {
                finger.distal = t;
            }
        }

        private void RecordRestRotation(FingerJoints finger)
        {
            if (finger.proximal != null) finger.initialProximalRot = finger.proximal.localRotation;
            if (finger.intermediate != null) finger.initialIntermediateRot = finger.intermediate.localRotation;
            if (finger.distal != null) finger.initialDistalRot = finger.distal.localRotation;
        }

        /// <summary>
        /// Cambia la postura de la mano a la indicada.
        /// </summary>
        public void SetPose(HandPoseType pose, bool instant = false)
        {
            m_CurrentPose = pose;
            m_TargetCurls = GetCurlPreset(pose);

            if (instant)
            {
                m_CurrentCurls = m_TargetCurls;
                ApplyCurlsToBones(m_CurrentCurls);
            }
        }

        private FingerCurls GetCurlPreset(HandPoseType pose)
        {
            FingerCurls c = new FingerCurls();
            switch (pose)
            {
                case HandPoseType.SteeringWheel:
                    // Agarre cerrado y firme envolviendo el aro del volante
                    c.thumb = 0.55f;
                    c.index = 0.90f;
                    c.middle = 0.95f;
                    c.ring = 0.95f;
                    c.pinky = 0.90f;
                    break;

                case HandPoseType.KnobPinch:
                    // Pinza precisa entre pulgar e índice sobre la perilla de luces
                    c.thumb = 0.38f;
                    c.index = 0.42f;
                    c.middle = 0.75f;
                    c.ring = 0.80f;
                    c.pinky = 0.85f;
                    break;

                case HandPoseType.GearShifter:
                    // Palma apoyada y ahuecada sobre la perilla de la palanca de cambios
                    c.thumb = 0.40f;
                    c.index = 0.65f;
                    c.middle = 0.70f;
                    c.ring = 0.70f;
                    c.pinky = 0.65f;
                    break;

                case HandPoseType.Handbrake:
                    // Empuñadura envolvente de puño cerrado sobre el freno de mano
                    c.thumb = 0.55f;
                    c.index = 0.88f;
                    c.middle = 0.92f;
                    c.ring = 0.92f;
                    c.pinky = 0.88f;
                    break;

                case HandPoseType.RearviewMirror:
                    // Sujeción lateral con dos dedos en el marco del espejo
                    c.thumb = 0.32f;
                    c.index = 0.38f;
                    c.middle = 0.70f;
                    c.ring = 0.75f;
                    c.pinky = 0.75f;
                    break;

                case HandPoseType.Default:
                default:
                    // Postura natural de reposo/conducción (dedos suavemente curvados, mano relajada)
                    c.thumb = 0.18f;
                    c.index = 0.25f;
                    c.middle = 0.30f;
                    c.ring = 0.32f;
                    c.pinky = 0.35f;
                    break;
            }
            return c;
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactableObject == null) return;

            MonoBehaviour mb = args.interactableObject as MonoBehaviour;
            if (mb == null) return;

            VRHandPoseTarget target = mb.GetComponent<VRHandPoseTarget>();
            if (target == null) target = mb.GetComponentInParent<VRHandPoseTarget>();

            if (target != null)
            {
                Transform interactorTr = (args.interactorObject as IXRInteractor)?.transform ?? transform;
                AttachToTarget(target, interactorTr);
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            // Solo liberar si no quedan interactables activos seleccionados
            bool hasOtherSelection = false;
            foreach (var interactor in m_Interactors)
            {
                if (interactor != null && interactor.hasSelection)
                {
                    hasOtherSelection = true;
                    break;
                }
            }

            if (!hasOtherSelection && m_IsHoldingTarget)
            {
                DetachFromTarget();
            }
        }

        public void AttachToTarget(VRHandPoseTarget target, Transform interactorTransform)
        {
            if (target == null) return;

            m_ActiveTarget = target;
            m_ActiveTargetTransform = target.GetAttachTransform(m_IsLeftHand);
            m_IsHoldingTarget = true;
            m_IsReturningFromRelease = false;

            // Fijar mano calculando punto y rotación de agarre iniciales
            target.CalculateInitialGrip(m_IsLeftHand, interactorTransform != null ? interactorTransform : transform, out m_LocalGripPosition, out m_LocalGripRotation);

            SetPose(target.PoseType);
        }

        public void DetachFromTarget()
        {
            if (!m_IsHoldingTarget) return;

            // Iniciar interpolación suave de regreso al tracking del mando
            if (m_HandVisual != null)
            {
                m_ReleaseWorldPos = m_HandVisual.position;
                m_ReleaseWorldRot = m_HandVisual.rotation;
                m_IsReturningFromRelease = true;
                m_ReleaseTimer = 0f;
            }

            m_IsHoldingTarget = false;
            m_ActiveTarget = null;
            m_ActiveTargetTransform = null;

            SetPose(HandPoseType.Default);
        }

        private void Update()
        {
            // Verificación continua de agarre para máxima robustez en XRI
            CheckActiveInteractions();
        }

        private void CheckActiveInteractions()
        {
            if (!m_IsHoldingTarget)
            {
                // Si algún interactor tiene seleccionado un objeto con VRHandPoseTarget, fijarse a él
                for (int i = 0; i < m_Interactors.Count; i++)
                {
                    var interactor = m_Interactors[i];
                    if (interactor != null && interactor.hasSelection)
                    {
                        foreach (var selected in interactor.interactablesSelected)
                        {
                            if (selected is MonoBehaviour mb)
                            {
                                var target = mb.GetComponent<VRHandPoseTarget>() ?? mb.GetComponentInParent<VRHandPoseTarget>();
                                if (target != null)
                                {
                                    Transform interactorTr = (interactor as IXRInteractor)?.transform ?? transform;
                                    AttachToTarget(target, interactorTr);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Si el objeto fue soltado por todos los interactores, liberar la mano
                bool stillSelected = false;
                for (int i = 0; i < m_Interactors.Count; i++)
                {
                    var interactor = m_Interactors[i];
                    if (interactor != null && interactor.hasSelection)
                    {
                        foreach (var selected in interactor.interactablesSelected)
                        {
                            if (selected is MonoBehaviour mb)
                            {
                                var target = mb.GetComponent<VRHandPoseTarget>() ?? mb.GetComponentInParent<VRHandPoseTarget>();
                                if (target == m_ActiveTarget)
                                {
                                    stillSelected = true;
                                    break;
                                }
                            }
                        }
                        if (stillSelected) break;
                    }
                }

                if (!stillSelected)
                {
                    DetachFromTarget();
                }
            }
        }

        private void LateUpdate()
        {
            UpdateHandTransform();
            UpdateFingerPoses();
        }

        /// <summary>
        /// Actualiza la posición y rotación de la mano visual en el espacio:
        /// Si está agarrando un objeto con seguimiento, se ancla y rota rígidamente con él.
        /// Si se acaba de soltar, interpola suavemente de regreso al mando físico.
        /// Si está en reposo, sigue 1:1 el transform del mando.
        /// </summary>
        private void UpdateHandTransform()
        {
            if (m_HandVisual == null) return;

            if (m_IsHoldingTarget && m_ActiveTarget != null && m_ActiveTargetTransform != null)
            {
                if (m_ActiveTarget.FollowObjectMotion)
                {
                    // Acompañar el giro/movimiento del volante, perilla, cambios o freno en tiempo real
                    // Se usa position + rotation * offset métrico para ser 100% inmune a escalas de modelos FBX en la jerarquía
                    m_HandVisual.position = m_ActiveTargetTransform.position + m_ActiveTargetTransform.rotation * m_LocalGripPosition;
                    m_HandVisual.rotation = m_ActiveTargetTransform.rotation * m_LocalGripRotation;
                    return;
                }
            }

            if (m_IsReturningFromRelease)
            {
                m_ReleaseTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(m_ReleaseTimer / Mathf.Max(0.01f, m_ReleaseReturnDuration));
                float smoothT = Mathf.SmoothStep(0f, 1f, progress);

                m_HandVisual.position = Vector3.Lerp(m_ReleaseWorldPos, transform.position, smoothT);
                m_HandVisual.rotation = Quaternion.Slerp(m_ReleaseWorldRot, transform.rotation, smoothT);

                if (progress >= 1f)
                {
                    m_IsReturningFromRelease = false;
                    m_HandVisual.localPosition = Vector3.zero;
                    m_HandVisual.localRotation = Quaternion.identity;
                }
                return;
            }

            // Tracking normal alineado con el mando
            m_HandVisual.localPosition = Vector3.zero;
            m_HandVisual.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Interpola y aplica los ángulos de flexión a cada falange de los dedos.
        /// En modo Default, añade modulación con gatillo y agarre si se detectan pulsaciones libres.
        /// </summary>
        private void UpdateFingerPoses()
        {
            if (!m_BonesMapped) return;

            FingerCurls target = m_TargetCurls;

            // Flexión dinámica libre por gatillo/grip al estar en el aire (modo Default)
            if (!m_IsHoldingTarget && m_CurrentPose == HandPoseType.Default)
            {
                float triggerVal = 0f;
                float gripVal = 0f;

                if (m_TriggerAction.action != null)
                {
                    triggerVal = m_TriggerAction.action.ReadValue<float>();
                }
                if (m_GripAction.action != null)
                {
                    gripVal = m_GripAction.action.ReadValue<float>();
                }

                if (triggerVal > 0.05f)
                {
                    target.index = Mathf.Lerp(target.index, 0.95f, triggerVal);
                }
                if (gripVal > 0.05f)
                {
                    target.middle = Mathf.Lerp(target.middle, 0.95f, gripVal);
                    target.ring = Mathf.Lerp(target.ring, 0.95f, gripVal);
                    target.pinky = Mathf.Lerp(target.pinky, 0.95f, gripVal);
                    target.thumb = Mathf.Lerp(target.thumb, 0.65f, gripVal);
                }
            }

            // Suavizado hacia el curl objetivo
            float step = m_PoseTransitionSpeed * Time.deltaTime;
            m_CurrentCurls.thumb = Mathf.MoveTowards(m_CurrentCurls.thumb, target.thumb, step);
            m_CurrentCurls.index = Mathf.MoveTowards(m_CurrentCurls.index, target.index, step);
            m_CurrentCurls.middle = Mathf.MoveTowards(m_CurrentCurls.middle, target.middle, step);
            m_CurrentCurls.ring = Mathf.MoveTowards(m_CurrentCurls.ring, target.ring, step);
            m_CurrentCurls.pinky = Mathf.MoveTowards(m_CurrentCurls.pinky, target.pinky, step);

            ApplyCurlsToBones(m_CurrentCurls);
        }

        private void ApplyCurlsToBones(FingerCurls curls)
        {
            ApplyFingerCurl(m_Thumb, curls.thumb, isThumb: true);
            ApplyFingerCurl(m_Index, curls.index, isThumb: false);
            ApplyFingerCurl(m_Middle, curls.middle, isThumb: false);
            ApplyFingerCurl(m_Ring, curls.ring, isThumb: false);
            ApplyFingerCurl(m_Pinky, curls.pinky, isThumb: false);
        }

        private void ApplyFingerCurl(FingerJoints finger, float curlValue, bool isThumb)
        {
            // Ángulo base de rotación sobre el eje local X de flexión
            float maxAnglePerJoint = isThumb ? 40f : 55f;
            float angle = curlValue * maxAnglePerJoint;

            Quaternion bendRot = Quaternion.Euler(angle, 0f, 0f);

            // Multiplicar a la derecha para rotar sobre el sistema de coordenadas local del propio hueso
            if (finger.proximal != null)
            {
                finger.proximal.localRotation = finger.initialProximalRot * bendRot;
            }
            if (finger.intermediate != null)
            {
                finger.intermediate.localRotation = finger.initialIntermediateRot * bendRot;
            }
            if (finger.distal != null)
            {
                finger.distal.localRotation = finger.initialDistalRot * bendRot;
            }
        }
    }
}
