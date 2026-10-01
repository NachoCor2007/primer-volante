using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    [System.Serializable]
    public class HandbrakeEngagementEvent : UnityEvent<float> { }

    /// <summary>
    /// Freno de mano físico e interactivo en Realidad Virtual.
    /// Rota sobre un único eje local desde su punta oculta dentro de mdl_car04_body,
    /// con recorrido continuo (sin imanes) entre "no accionado" y "accionado".
    /// A diferencia de la palanca de cambios y el guiño, al soltar queda trabada
    /// en la posición liberada (trinquete): no vuelve sola a ningún extremo.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class VRHandbrake : MonoBehaviour
    {
        [Header("Eje y Rango de Movimiento")]
        [Tooltip("Eje local sobre el que rota la palanca (X local: el mismo usado por la palanca de cambios y el guiño).")]
        public Vector3 rotationAxis = Vector3.right;

        [Tooltip("Recorrido total en grados entre accionado (pose actual del prefab, 0°) y no accionado (este valor).")]
        public float releaseAngleRange = 36f;

        [Header("Testigo de Tablero")]
        [Tooltip("Tolerancia en grados, medida desde la posición totalmente liberada, dentro de la cual se considera 'no accionado'.")]
        [SerializeField] private float m_ReleaseThresholdDegrees = 1f;

        [Header("Eventos")]
        [Tooltip("Evento disparado con el nuevo valor de Engagement (0..1) cada vez que cambia.")]
        public HandbrakeEngagementEvent OnEngagementChanged = new HandbrakeEngagementEvent();

        private XRBaseInteractable m_Interactable;
        private Rigidbody m_Rigidbody;
        private bool m_IsGrabbed = false;
        private Quaternion m_ZeroRotation; // Rotación del prefab = freno accionado (Engagement = 1)

        private Transform m_OriginalParent;
        private Vector3 m_InitialLocalPosition;

        private float m_GrabStartLoweredAngle;
        private float m_GrabStartHandAngle;

        private float m_LastEngagement = 1f;

        private void EnsureInitialized()
        {
            if (m_ZeroRotation == default(Quaternion) || (m_ZeroRotation.x == 0f && m_ZeroRotation.y == 0f && m_ZeroRotation.z == 0f && m_ZeroRotation.w == 0f))
            {
                m_ZeroRotation = transform.localRotation;
            }
            if (m_OriginalParent == null)
            {
                m_OriginalParent = transform.parent;
            }
            if (m_InitialLocalPosition == Vector3.zero && transform.localPosition != Vector3.zero)
            {
                m_InitialLocalPosition = transform.localPosition;
            }
        }

        /// <summary>
        /// Nivel de accionamiento del freno de mano (0 = liberado, 1 = accionado a fondo).
        /// </summary>
        public float Engagement
        {
            get
            {
                EnsureInitialized();
                float lowered = GetLoweredAngle();
                if (lowered >= releaseAngleRange - 0.05f) return 0f;
                if (lowered <= 0.05f) return 1f;
                return 1f - Mathf.Clamp01(lowered / releaseAngleRange);
            }
        }

        /// <summary>
        /// Indica si el freno de mano no está completamente liberado.
        /// </summary>
        public bool IsEngaged
        {
            get
            {
                EnsureInitialized();
                return (releaseAngleRange - GetLoweredAngle()) > m_ReleaseThresholdDegrees;
            }
        }

        /// <summary>
        /// Indica si la palanca está actualmente agarrada por un interactor VR.
        /// </summary>
        public bool IsGrabbed => m_IsGrabbed;

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

            EnsureHierarchyAndLocalTransform();

            m_GrabStartLoweredAngle = GetLoweredAngle();

            var interactor = args.interactorObject;
            if (interactor != null)
            {
                Vector3 localDir = GetHandDirectionLocal(interactor.transform);
                if (localDir.sqrMagnitude > 0.0001f)
                {
                    m_GrabStartHandAngle = CalculateHandAngle(localDir);
                }
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;
            EnsureHierarchyAndLocalTransform();
            // Trinquete: no hay snapping ni retorno automático, queda donde se soltó.
        }

        private void Update()
        {
#if UNITY_EDITOR || DEBUG
            if (!m_IsGrabbed && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            {
                float target = (Engagement > 0.5f) ? 0f : 1f;
                SetEngagementImmediate(target);
                Debug.Log($"[VRHandbrake] {gameObject.name}: Freno de mano alternado por teclado a Engagement={target:F0}.");
            }
#endif
        }

        private void LateUpdate()
        {
            if (!m_IsGrabbed || m_Interactable == null || m_Interactable.interactorsSelecting.Count == 0)
            {
                return;
            }

            EnsureHierarchyAndLocalTransform();

            var interactor = m_Interactable.interactorsSelecting[0];
            if (interactor == null) return;

            Vector3 localDir = GetHandDirectionLocal(interactor.transform);
            if (localDir.sqrMagnitude <= 0.0001f) return;

            float currentHandAngle = CalculateHandAngle(localDir);
            float deltaAngle = Mathf.DeltaAngle(m_GrabStartHandAngle, currentHandAngle);
            float rawLoweredAngle = m_GrabStartLoweredAngle + deltaAngle;
            float clampedLoweredAngle = Mathf.Clamp(rawLoweredAngle, 0f, releaseAngleRange);

            ApplyLoweredAngle(clampedLoweredAngle);
        }

        private Vector3 GetHandDirectionLocal(Transform interactorTransform)
        {
            Transform parentT = transform.parent != null ? transform.parent : m_OriginalParent;
            Vector3 dirToHandLocal;

            if (parentT != null)
            {
                Vector3 handLocalPos = parentT.InverseTransformPoint(interactorTransform.position);
                dirToHandLocal = handLocalPos - transform.localPosition;
            }
            else
            {
                dirToHandLocal = interactorTransform.position - transform.position;
            }

            return Quaternion.Inverse(m_ZeroRotation) * dirToHandLocal;
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

        /// <summary>
        /// Ángulo actual, en grados, bajado desde la pose accionada (0 = accionado a fondo,
        /// releaseAngleRange = totalmente liberado).
        /// </summary>
        private float GetLoweredAngle()
        {
            EnsureInitialized();
            Quaternion diff = Quaternion.Inverse(m_ZeroRotation) * transform.localRotation;
            diff.ToAngleAxis(out float angle, out Vector3 axis);

            if (Vector3.Dot(axis, rotationAxis) < 0)
            {
                angle = -angle;
            }

            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;

            return Mathf.Clamp(angle, 0f, releaseAngleRange);
        }

        private void ApplyLoweredAngle(float loweredAngle)
        {
            EnsureInitialized();
            transform.localRotation = m_ZeroRotation * Quaternion.AngleAxis(loweredAngle, rotationAxis);
            NotifyEngagementIfChanged();
        }

        /// <summary>
        /// Fuerza el freno a un nivel de accionamiento (0..1) de forma inmediata, sin animación.
        /// Usado por el atajo de teclado de depuración.
        /// </summary>
        public void SetEngagementImmediate(float engagement)
        {
            EnsureInitialized();
            float loweredAngle = (1f - Mathf.Clamp01(engagement)) * releaseAngleRange;
            ApplyLoweredAngle(loweredAngle);
        }

        private void NotifyEngagementIfChanged()
        {
            float current = Engagement;
            if (!Mathf.Approximately(current, m_LastEngagement))
            {
                m_LastEngagement = current;
                OnEngagementChanged?.Invoke(current);
            }
        }
    }
}
