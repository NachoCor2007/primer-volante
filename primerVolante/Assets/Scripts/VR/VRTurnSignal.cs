using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Estados de la palanca de guiño: Left (izquierda), Off (centro) y Right (derecha).
    /// </summary>
    public enum TurnSignalState
    {
        Left = -1,
        Off = 0,
        Right = 1
    }

    [System.Serializable]
    public class TurnSignalStateEvent : UnityEvent<TurnSignalState> { }

    /// <summary>
    /// Palanca de guiño física e interactiva en Realidad Virtual.
    /// Rota sobre un único eje local con imanes (snapping) en Left, Off y Right.
    /// Se auto-cancela (vuelve a Off) cuando el volante gira lo suficiente en la
    /// dirección contraria al guiño activo, imitando el mecanismo de un auto real.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class VRTurnSignal : MonoBehaviour
    {
        [Header("Estado Actual")]
        [Tooltip("Estado actual de la palanca de guiño.")]
        public TurnSignalState currentSignal = TurnSignalState.Off;

        [Header("Eje y Límites de Movimiento")]
        [Tooltip("Eje local sobre el que rota la palanca.")]
        public Vector3 rotationAxis = Vector3.right;

        [Tooltip("Ángulo cuando la palanca está levantada (Guiño Derecho).")]
        public float upLimit = 25f;

        [Tooltip("Ángulo cuando la palanca está bajada (Guiño Izquierdo).")]
        public float downLimit = -25f;

        [Header("Sistema de Imanes (Snapping)")]
        [Tooltip("Rango de tolerancia (en grados) para atracción magnética y enganche.")]
        public float snapThreshold = 6f;

        [Tooltip("Velocidad de interpolación suave al soltar, engancharse o auto-cancelarse.")]
        public float snapSpeed = 12f;

        [Header("Auto-Cancelado por Volante")]
        [Tooltip("Controlador del vehículo, usado para leer el ángulo del volante. Se autodetecta en los padres si se deja vacío.")]
        [SerializeField] private VehicleController m_VehicleController;

        [Tooltip("Grados que el volante debe girar en la dirección contraria al guiño activo antes de que la palanca vuelva sola a Off.")]
        [SerializeField] private float m_SelfCancelGraceAngle = 15f;

        [Header("Eventos")]
        [Tooltip("Evento disparado cada vez que cambia el estado de la palanca.")]
        public TurnSignalStateEvent OnTurnSignalChanged = new TurnSignalStateEvent();

        public TurnSignalState CurrentSignal => currentSignal;

        private XRBaseInteractable m_Interactable;
        private Rigidbody m_Rigidbody;
        private bool m_IsGrabbed = false;
        private Coroutine m_SnapRoutine;
        private Quaternion m_ZeroRotation;

        private Transform m_OriginalParent;
        private Vector3 m_InitialLocalPosition;

        private float m_GrabStartLeverAngle;
        private float m_GrabStartHandAngle;

        private void Awake()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }

            if (m_VehicleController == null)
                m_VehicleController = GetComponentInParent<VehicleController>();

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
            float targetAngle = GetAngleForState(currentSignal);
            ApplyRotation(targetAngle);
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

        private bool SetSignalState(TurnSignalState newState)
        {
            if (newState != currentSignal)
            {
                currentSignal = newState;
                OnTurnSignalChanged?.Invoke(currentSignal);
                Debug.Log($"[VRTurnSignal] {gameObject.name}: Guiño cambiado a {currentSignal}");
                return true;
            }
            return false;
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

            m_GrabStartLeverAngle = GetCurrentAngle();

            var interactor = args.interactorObject;
            if (interactor != null)
            {
                Transform parentT = transform.parent != null ? transform.parent : m_OriginalParent;
                Vector3 dirToHandLocal;

                if (parentT != null)
                {
                    Vector3 handLocalPos = parentT.InverseTransformPoint(interactor.transform.position);
                    dirToHandLocal = handLocalPos - transform.localPosition;
                }
                else
                {
                    dirToHandLocal = interactor.transform.position - transform.position;
                }

                if (dirToHandLocal.sqrMagnitude > 0.0001f)
                {
                    Vector3 localDir = Quaternion.Inverse(m_ZeroRotation) * dirToHandLocal;
                    m_GrabStartHandAngle = CalculateHandAngle(localDir);
                }
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;

            EnsureHierarchyAndLocalTransform();

            float currentAngle = GetCurrentAngle();
            TurnSignalState targetState = GetClosestState(currentAngle);

            if (m_SnapRoutine != null) StopCoroutine(m_SnapRoutine);
            m_SnapRoutine = StartCoroutine(SnapToState(targetState));
        }

        private void LateUpdate()
        {
            if (m_IsGrabbed && m_Interactable != null && m_Interactable.interactorsSelecting.Count > 0)
            {
                EnsureHierarchyAndLocalTransform();

                var interactor = m_Interactable.interactorsSelecting[0];
                if (interactor != null)
                {
                    Transform parentT = transform.parent != null ? transform.parent : m_OriginalParent;
                    Vector3 dirToHandLocal;

                    if (parentT != null)
                    {
                        Vector3 handLocalPos = parentT.InverseTransformPoint(interactor.transform.position);
                        dirToHandLocal = handLocalPos - transform.localPosition;
                    }
                    else
                    {
                        dirToHandLocal = interactor.transform.position - transform.position;
                    }

                    if (dirToHandLocal.sqrMagnitude > 0.0001f)
                    {
                        Vector3 localDir = Quaternion.Inverse(m_ZeroRotation) * dirToHandLocal;
                        float currentHandAngle = CalculateHandAngle(localDir);
                        float deltaAngle = Mathf.DeltaAngle(m_GrabStartHandAngle, currentHandAngle);
                        float rawTargetAngle = m_GrabStartLeverAngle + deltaAngle;

                        float minAllowed = Mathf.Min(upLimit, downLimit);
                        float maxAllowed = Mathf.Max(upLimit, downLimit);
                        float clampedAngle = Mathf.Clamp(rawTargetAngle, minAllowed, maxAllowed);

                        float finalAngle = ApplyMagneticSnapping(clampedAngle);

                        ApplyRotation(finalAngle);
                    }
                }
            }
            else if (!m_IsGrabbed)
            {
                CheckSelfCancel();
            }
        }

        private void CheckSelfCancel()
        {
            if (currentSignal == TurnSignalState.Off) return;
            if (m_SnapRoutine != null) return; // ya está volviendo a Off
            if (m_VehicleController == null) return;

            VRSteeringWheel wheel = m_VehicleController.SteeringWheel;
            if (wheel == null) return;

            float wheelAngle = wheel.CurrentAngle;

            if (currentSignal == TurnSignalState.Right && wheelAngle <= -m_SelfCancelGraceAngle)
            {
                CenterSignal();
            }
            else if (currentSignal == TurnSignalState.Left && wheelAngle >= m_SelfCancelGraceAngle)
            {
                CenterSignal();
            }
        }

        /// <summary>
        /// Devuelve la palanca a Off de forma animada (usado por el auto-cancelado del volante).
        /// </summary>
        public void CenterSignal()
        {
            if (m_SnapRoutine != null) StopCoroutine(m_SnapRoutine);
            m_SnapRoutine = StartCoroutine(SnapToState(TurnSignalState.Off));
        }

        private float CalculateHandAngle(Vector3 localDir)
        {
            if (rotationAxis == Vector3.right)
                return Mathf.Atan2(localDir.z, localDir.y) * Mathf.Rad2Deg;
            if (rotationAxis == Vector3.up)
                return Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            if (rotationAxis == Vector3.forward)
                return Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;

            return Mathf.Atan2(localDir.z, localDir.y) * Mathf.Rad2Deg;
        }

        private float ApplyMagneticSnapping(float angle)
        {
            TurnSignalState[] states = { TurnSignalState.Left, TurnSignalState.Off, TurnSignalState.Right };
            foreach (var s in states)
            {
                float targetAngle = GetAngleForState(s);
                float diff = Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle));

                if (diff <= snapThreshold)
                {
                    float factor = 1f - (diff / snapThreshold);
                    float snappedAngle = Mathf.LerpAngle(angle, targetAngle, factor * 0.4f);

                    SetSignalState(s);
                    return snappedAngle;
                }
            }

            return angle;
        }

        /// <summary>
        /// Retorna el ángulo correspondiente a cada estado: Right en upLimit, Left en downLimit, Off en 0.
        /// </summary>
        public float GetAngleForState(TurnSignalState state)
        {
            switch (state)
            {
                case TurnSignalState.Right:
                    return upLimit;
                case TurnSignalState.Left:
                    return downLimit;
                default:
                    return 0f;
            }
        }

        public TurnSignalState GetClosestState(float angle)
        {
            TurnSignalState[] allStates = { TurnSignalState.Left, TurnSignalState.Off, TurnSignalState.Right };
            TurnSignalState closest = TurnSignalState.Off;
            float minDiff = float.MaxValue;

            foreach (var s in allStates)
            {
                float targetAngle = GetAngleForState(s);
                float diff = Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle));
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closest = s;
                }
            }
            return closest;
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

        private IEnumerator SnapToState(TurnSignalState targetState)
        {
            float targetAngle = GetAngleForState(targetState);
            while (true)
            {
                EnsureHierarchyAndLocalTransform();

                float currentAngle = GetCurrentAngle();
                float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * snapSpeed);
                ApplyRotation(newAngle);

                if (Mathf.Abs(Mathf.DeltaAngle(newAngle, targetAngle)) < 0.2f)
                {
                    ApplyRotation(targetAngle);
                    break;
                }
                yield return null;
            }

            SetSignalState(targetState);
            m_SnapRoutine = null;
        }
    }
}
