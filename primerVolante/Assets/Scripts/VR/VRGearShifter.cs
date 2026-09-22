using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Estados de la transmisión de la palanca de cambios:
    /// P (Parking), R (Reverse), N (Neutral) y D (Drive).
    /// </summary>
    public enum GearState
    {
        P = 0,
        R = 1,
        N = 2,
        D = 3,

        // Alias para compatibilidad con VehicleController
        Park = P,
        Reverse = R,
        Neutral = N,
        Drive = D
    }

    [System.Serializable]
    public class GearStateEvent : UnityEvent<GearState> { }

    /// <summary>
    /// Palanca de cambios física e interactiva en Realidad Virtual.
    /// Movimiento restringido a un solo eje local con imanes (snapping) en P, R, N y D.
    /// Requiere agarre continuo mediante XRGrabInteractable.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class VRGearShifter : MonoBehaviour
    {
        [Header("Estado Actual")]
        [Tooltip("Estado actual de la palanca de cambios.")]
        public GearState currentGear = GearState.P;

        [Header("Eje y Límites de Movimiento")]
        [Tooltip("Eje local sobre el que rota la palanca (X local hacia adelante y hacia atrás).")]
        public Vector3 rotationAxis = Vector3.right;

        [Tooltip("Límite máximo hacia adelante en grados, correspondiente a Parking (P).")]
        public float forwardLimit = 40f;

        [Tooltip("Límite máximo hacia atrás / hacia el usuario en grados, correspondiente a Drive (D).")]
        public float backwardLimit = -35f;

        [Header("Sistema de Imanes (Snapping)")]
        [Tooltip("Rango de tolerancia (en grados) para atracción magnética y enganche.")]
        public float snapThreshold = 6f;

        [Tooltip("Velocidad de interpolación suave al soltar o engancharse en un cambio.")]
        public float snapSpeed = 12f;

        [Header("Eventos")]
        [Tooltip("Evento disparado cada vez que cambia el estado de la palanca.")]
        public GearStateEvent OnGearChanged = new GearStateEvent();

        public GearState CurrentGear => currentGear;

        private XRBaseInteractable m_Interactable;
        private Rigidbody m_Rigidbody;
        private bool m_IsGrabbed = false;
        private Coroutine m_SnapRoutine;
        private Quaternion m_ZeroRotation;

        // Variables auxiliares para seguimiento suave de mano
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

            m_ZeroRotation = transform.localRotation;

            var grab = m_Interactable as XRGrabInteractable;
            if (grab != null)
            {
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;
            }

            m_Interactable.selectEntered.AddListener(OnGrab);
            m_Interactable.selectExited.AddListener(OnRelease);
        }

        private void Start()
        {
            // Ubicar en la rotación exacta del cambio inicial al comenzar
            SetRotationToGear(currentGear);
        }

        private void OnDestroy()
        {
            if (m_Interactable != null)
            {
                m_Interactable.selectEntered.RemoveListener(OnGrab);
                m_Interactable.selectExited.RemoveListener(OnRelease);
            }
        }

        private void OnGrab(SelectEnterEventArgs args)
        {
            m_IsGrabbed = true;
            if (m_SnapRoutine != null)
            {
                StopCoroutine(m_SnapRoutine);
                m_SnapRoutine = null;
            }

            m_GrabStartLeverAngle = GetCurrentAngle();

            var interactor = args.interactorObject;
            if (interactor != null)
            {
                Vector3 handWorldPos = interactor.transform.position;
                Vector3 dirToHand = handWorldPos - transform.position;

                if (dirToHand.sqrMagnitude > 0.0001f)
                {
                    Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
                    Quaternion zeroWorldRot = parentRot * m_ZeroRotation;
                    Vector3 localDir = Quaternion.Inverse(zeroWorldRot) * dirToHand;

                    m_GrabStartHandAngle = CalculateHandAngle(localDir);
                }
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;

            float currentAngle = GetCurrentAngle();
            GearState closestGear = GetClosestGear(currentAngle);

            if (closestGear != currentGear)
            {
                currentGear = closestGear;
                OnGearChanged?.Invoke(currentGear);
                Debug.Log($"[VRGearShifter] {gameObject.name} soltado y enganchado en {currentGear}");
            }

            if (m_SnapRoutine != null) StopCoroutine(m_SnapRoutine);
            m_SnapRoutine = StartCoroutine(SnapToGear(closestGear));
        }

        private void LateUpdate()
        {
            if (m_IsGrabbed && m_Interactable != null && m_Interactable.interactorsSelecting.Count > 0)
            {
                var interactor = m_Interactable.interactorsSelecting[0];
                Vector3 handWorldPos = interactor.transform.position;
                Vector3 dirToHand = handWorldPos - transform.position;

                if (dirToHand.sqrMagnitude > 0.0001f)
                {
                    Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
                    Quaternion zeroWorldRot = parentRot * m_ZeroRotation;
                    Vector3 localDir = Quaternion.Inverse(zeroWorldRot) * dirToHand;

                    float currentHandAngle = CalculateHandAngle(localDir);
                    float deltaAngle = Mathf.DeltaAngle(m_GrabStartHandAngle, currentHandAngle);
                    float rawTargetAngle = m_GrabStartLeverAngle + deltaAngle;

                    float minAllowed = Mathf.Min(forwardLimit, backwardLimit);
                    float maxAllowed = Mathf.Max(forwardLimit, backwardLimit);
                    float clampedAngle = Mathf.Clamp(rawTargetAngle, minAllowed, maxAllowed);

                    // Sistema de imanes mientras se sostiene (Snapping en rango de tolerancia)
                    float finalAngle = ApplyMagneticSnapping(clampedAngle);

                    ApplyRotation(finalAngle);
                }
            }
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
            GearState[] gears = { GearState.P, GearState.R, GearState.N, GearState.D };
            foreach (var g in gears)
            {
                float targetAngle = GetAngleForGear(g);
                float diff = Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle));

                if (diff <= snapThreshold)
                {
                    float factor = 1f - (diff / snapThreshold);
                    float snappedAngle = Mathf.LerpAngle(angle, targetAngle, factor * 0.4f);

                    if (currentGear != g)
                    {
                        currentGear = g;
                        OnGearChanged?.Invoke(currentGear);
                        Debug.Log($"[VRGearShifter] {gameObject.name} magnetizado a {currentGear}");
                    }
                    return snappedAngle;
                }
            }

            // Si está fuera de los umbrales de imán, actualizar el estado más cercano
            GearState closest = GetClosestGear(angle);
            if (closest != currentGear)
            {
                currentGear = closest;
                OnGearChanged?.Invoke(currentGear);
            }

            return angle;
        }

        /// <summary>
        /// Retorna el ángulo correspondiente para cada cambio:
        /// P en forwardLimit, D en backwardLimit, y R y N distribuidos equitativamente en el medio.
        /// </summary>
        public float GetAngleForGear(GearState gear)
        {
            switch (gear)
            {
                case GearState.P:
                    return forwardLimit;
                case GearState.R:
                    return Mathf.Lerp(forwardLimit, backwardLimit, 1f / 3f);
                case GearState.N:
                    return Mathf.Lerp(forwardLimit, backwardLimit, 2f / 3f);
                case GearState.D:
                    return backwardLimit;
                default:
                    return forwardLimit;
            }
        }

        public GearState GetClosestGear(float angle)
        {
            GearState[] allGears = { GearState.P, GearState.R, GearState.N, GearState.D };
            GearState closest = GearState.P;
            float minDiff = float.MaxValue;

            foreach (var g in allGears)
            {
                float targetAngle = GetAngleForGear(g);
                float diff = Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle));
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closest = g;
                }
            }
            return closest;
        }

        public void SetGear(GearState gear)
        {
            currentGear = gear;
            if (m_SnapRoutine != null) StopCoroutine(m_SnapRoutine);
            m_SnapRoutine = StartCoroutine(SnapToGear(gear));
        }

        public void SetRotationToGear(GearState gear)
        {
            float targetAngle = GetAngleForGear(gear);
            ApplyRotation(targetAngle);
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

        private IEnumerator SnapToGear(GearState targetGear)
        {
            float targetAngle = GetAngleForGear(targetGear);
            while (true)
            {
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
            m_SnapRoutine = null;
        }
    }
}
