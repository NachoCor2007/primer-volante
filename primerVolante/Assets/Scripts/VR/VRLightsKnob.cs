using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Estados de la perilla de luces: Off (apagadas), High (altas) y Low (bajas).
    /// El orden declarado (Off, High, Low) es también el orden físico de la perilla
    /// (izquierda, vertical, derecha), usado para restringir los saltos a estados adyacentes.
    /// </summary>
    public enum HeadlightState
    {
        Off,
        High,
        Low
    }

    [System.Serializable]
    public class HeadlightStateEvent : UnityEvent<HeadlightState> { }

    /// <summary>
    /// Perilla de luces física e interactiva en Realidad Virtual (mdl_car02_lights_knob).
    /// mdl_car02_lights es la base fija; este componente solo rota la perilla.
    /// La perilla es un cilindro simétrico sin marca visible, por lo que la inclinación
    /// visual (Off=izquierda, High=vertical, Low=derecha) se logra rotando sobre el eje
    /// LOCAL Z (perpendicular al eje propio del cilindro), mientras que la entrada del
    /// usuario es un giro de muñeca (roll del controlador) alrededor del eje que sale del
    /// tablero. Solo existen 3 orientaciones discretas: no hay snapping continuo ni
    /// estados intermedios.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class VRLightsKnob : MonoBehaviour
    {
        [Header("Estado Actual")]
        [Tooltip("Estado actual de la perilla de luces.")]
        [SerializeField] private HeadlightState m_CurrentState = HeadlightState.High;

        [Header("Eje y Ángulos por Estado")]
        [Tooltip("Eje local sobre el que se inclina la perilla (perpendicular al eje propio del cilindro).")]
        public Vector3 rotationAxis = Vector3.forward;

        [Tooltip("Ángulo (offset desde la pose del prefab) para el estado Off, inclinada a la izquierda.")]
        [SerializeField] private float m_OffAngleDegrees = 20f;

        [Tooltip("Ángulo (offset desde la pose del prefab) para el estado High, vertical. La pose actual del prefab corresponde a este estado (0°).")]
        [SerializeField] private float m_HighAngleDegrees = 0f;

        [Tooltip("Ángulo (offset desde la pose del prefab) para el estado Low, inclinada a la derecha.")]
        [SerializeField] private float m_LowAngleDegrees = -20f;

        [Header("Interacción por Giro de Muñeca")]
        [Tooltip("Eje local de la perilla que sale del tablero (el eje propio del cilindro), usado solo para medir el giro de muñeca.")]
        public Vector3 shaftAxisLocal = Vector3.up;

        [Tooltip("Grados de giro de muñeca necesarios para saltar al estado adyacente.")]
        [SerializeField] private float m_RollThresholdDegrees = 20f;

        [Header("Transición Visual")]
        [Tooltip("Velocidad de interpolación del snap corto entre orientaciones.")]
        [SerializeField] private float m_SnapSpeed = 16f;

        [Header("Eventos")]
        [Tooltip("Evento disparado cada vez que cambia el estado de la perilla.")]
        public HeadlightStateEvent OnHeadlightStateChanged = new HeadlightStateEvent();

        public HeadlightState CurrentState => m_CurrentState;

        private XRBaseInteractable m_Interactable;
        private Rigidbody m_Rigidbody;
        private bool m_IsGrabbed = false;
        private Coroutine m_SnapRoutine;
        private Quaternion m_ZeroRotation; // Rotación del prefab = estado High (vertical)

        private Transform m_OriginalParent;
        private Vector3 m_InitialLocalPosition;

        private Vector3 m_ShaftAxisWorld;
        private float m_GrabStartRoll;

        private void Awake()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }

            m_OriginalParent = transform.parent;
            m_InitialLocalPosition = transform.localPosition;
            m_ZeroRotation = transform.localRotation;

            ConfigureGrabInteractable();

            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.AddListener(OnGrab);
                m_Interactable.selectExited.AddListener(OnRelease);
            }
        }

        private void Start()
        {
            ApplyRotation(GetAngleForState(m_CurrentState));
        }

        private void OnDestroy()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.RemoveListener(OnGrab);
                m_Interactable.selectExited.RemoveListener(OnRelease);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ConfigureGrabInteractable();
        }
#endif

        private void ConfigureGrabInteractable()
        {
            var grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;
                grab.unparentTransformOnGrab = false;
                grab.retainTransformParent = true;
            }
        }

        private void EnsureHierarchyAndLocalTransform()
        {
            if (m_OriginalParent != null && transform.parent != m_OriginalParent)
            {
                transform.SetParent(m_OriginalParent, false);
            }
            transform.localPosition = m_InitialLocalPosition;
        }

        private void OnGrab(SelectEnterEventArgs args)
        {
            m_IsGrabbed = true;
            if (m_SnapRoutine != null)
            {
                StopCoroutine(m_SnapRoutine);
                m_SnapRoutine = null;
            }

            EnsureHierarchyAndLocalTransform();

            Transform parentT = transform.parent != null ? transform.parent : m_OriginalParent;
            Quaternion baseRotation = parentT != null ? parentT.rotation * m_ZeroRotation : m_ZeroRotation;
            m_ShaftAxisWorld = (baseRotation * shaftAxisLocal.normalized);

            var interactor = args.interactorObject;
            if (interactor != null)
            {
                m_GrabStartRoll = GetControllerRoll(interactor.transform);
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;
            EnsureHierarchyAndLocalTransform();
            // No hay retorno automático: la perilla se queda en el último estado discreto alcanzado.
        }

        private void LateUpdate()
        {
            if (m_IsGrabbed && m_Interactable != null && m_Interactable.interactorsSelecting.Count > 0)
            {
                EnsureHierarchyAndLocalTransform();

                var interactor = m_Interactable.interactorsSelecting[0];
                if (interactor != null)
                {
                    float currentRoll = GetControllerRoll(interactor.transform);
                    float delta = Mathf.DeltaAngle(m_GrabStartRoll, currentRoll);

                    if (Mathf.Abs(delta) >= m_RollThresholdDegrees)
                    {
                        int direction = delta > 0f ? 1 : -1;
                        m_GrabStartRoll = currentRoll;
                        StepState(direction, wrapAround: false);
                    }
                }
            }
        }

        private void Update()
        {
#if UNITY_EDITOR || DEBUG
            if (!m_IsGrabbed && Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                StepState(1, wrapAround: true);
                Debug.Log($"[VRLightsKnob] {gameObject.name}: Ciclado por teclado a {m_CurrentState}.");
            }
#endif
        }

        /// <summary>
        /// Mide el giro de muñeca del controlador (roll) alrededor del eje que sale del tablero,
        /// proyectando el vector "up" del controlador sobre el plano perpendicular a ese eje.
        /// </summary>
        private float GetControllerRoll(Transform interactorTransform)
        {
            Vector3 axis = m_ShaftAxisWorld;
            Vector3 reference = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(reference, axis)) > 0.9f)
            {
                reference = Vector3.forward;
            }

            Vector3 projectedReference = Vector3.ProjectOnPlane(reference, axis).normalized;
            Vector3 projectedCurrent = Vector3.ProjectOnPlane(interactorTransform.up, axis).normalized;

            return Vector3.SignedAngle(projectedReference, projectedCurrent, axis);
        }

        private void StepState(int direction, bool wrapAround)
        {
            int count = System.Enum.GetValues(typeof(HeadlightState)).Length;
            int nextIndex = (int)m_CurrentState + direction;

            if (wrapAround)
            {
                nextIndex = ((nextIndex % count) + count) % count;
            }
            else
            {
                nextIndex = Mathf.Clamp(nextIndex, 0, count - 1);
            }

            SetState((HeadlightState)nextIndex);
        }

        /// <summary>
        /// Cambia el estado de la perilla de forma animada (snap corto). No tiene efecto
        /// visual si ya se encuentra en ese estado, pero igual reasigna la referencia interna.
        /// </summary>
        public void SetState(HeadlightState newState)
        {
            bool changed = newState != m_CurrentState;
            m_CurrentState = newState;

            if (m_SnapRoutine != null) StopCoroutine(m_SnapRoutine);
            m_SnapRoutine = StartCoroutine(SnapToAngle(GetAngleForState(newState)));

            if (changed)
            {
                Debug.Log($"[VRLightsKnob] {gameObject.name}: Estado cambiado a {m_CurrentState}.");
                OnHeadlightStateChanged?.Invoke(m_CurrentState);
            }
        }

        private float GetAngleForState(HeadlightState state)
        {
            switch (state)
            {
                case HeadlightState.Off:
                    return m_OffAngleDegrees;
                case HeadlightState.Low:
                    return m_LowAngleDegrees;
                default:
                    return m_HighAngleDegrees;
            }
        }

        private void ApplyRotation(float angle)
        {
            transform.localRotation = m_ZeroRotation * Quaternion.AngleAxis(angle, rotationAxis);
        }

        private float GetCurrentAngle()
        {
            Quaternion diff = Quaternion.Inverse(m_ZeroRotation) * transform.localRotation;
            diff.ToAngleAxis(out float angle, out Vector3 axis);

            if (Vector3.Dot(axis, rotationAxis) < 0)
            {
                angle = -angle;
            }

            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private IEnumerator SnapToAngle(float targetAngle)
        {
            while (true)
            {
                EnsureHierarchyAndLocalTransform();

                float currentAngle = GetCurrentAngle();
                float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * m_SnapSpeed);
                ApplyRotation(newAngle);

                if (Mathf.Abs(Mathf.DeltaAngle(newAngle, targetAngle)) < 0.2f)
                {
                    ApplyRotation(targetAngle);
                    break;
                }
                yield return null;
            }

            m_SnapRoutine = null;
        }
    }
}
