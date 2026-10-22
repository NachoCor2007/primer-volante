using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Componente de Volante VR de alta precisión.
    /// Opera sobre un GameObject Pivot posicionado en el centro exacto de la geometría del volante.
    /// Ejecuta el procesamiento en LateUpdate (ExecutionOrder = 100) inmediatamente después del posicionamiento del jugador,
    /// eliminando cualquier desfase de 1 frame entre la cámara y la posición del mando al avanzar el auto.
    /// <para>
    /// Soporta agarre con las dos manos a la vez (<see cref="InteractableSelectMode.Multiple"/>). Cada mano
    /// mantiene su propio ángulo de referencia (se inicializa al agarrar y se borra al soltar), por lo que agarrar,
    /// soltar o cambiar de mano nunca provoca un salto. El giro de cada frame es el promedio de los deltas angulares
    /// de las manos, ponderado por la distancia de cada mano al centro en el plano del volante:
    /// <c>Σ(delta_i · radio_i) / Σ(radio_i)</c>. Con una sola mano equivale exactamente al delta de esa mano.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class VRSteeringWheel : MonoBehaviour
    {
        [Header("Configuración del Volante")]
        [Tooltip("Ángulo máximo de giro hacia cada lado en grados (ej: 450 = 1.5 vueltas a la izquierda / derecha).")]
        [SerializeField] private float m_MaxSteeringAngle = 450f;

        [Tooltip("Velocidad de retorno al centro en grados por segundo.")]
        [SerializeField] private float m_ReturnSpeed = 250f;

        [Tooltip("Si se activa, el volante vuelve automáticamente al centro al soltarlo.")]
        [SerializeField] private bool m_ReturnToCenter = true;

        [Header("Salida de Datos")]
        [Tooltip("Valor actual del volante normalizado entre -1.0 (total izquierda) y +1.0 (total derecha).")]
        [SerializeField] private float m_SteeringValue = 0f;

        private XRGrabInteractable m_GrabInteractable;
        private Rigidbody m_Rigidbody;

        private float m_CurrentAngle = 0f;

        // Ángulo de referencia (frame anterior) de cada mano que agarra el volante.
        private readonly Dictionary<IXRSelectInteractor, float> m_LastHandAngles = new Dictionary<IXRSelectInteractor, float>();
        // Buffer reutilizable para purgar referencias de manos que ya no agarran (sin allocations por frame).
        private readonly List<IXRSelectInteractor> m_StaleHands = new List<IXRSelectInteractor>();

        private Transform m_InitialParent;
        private Quaternion m_InitialLocalRotation;
        private Vector3 m_InitialLocalPosition;

        /// <summary>
        /// Valor normalizado del volante (-1.0 a +1.0).
        /// </summary>
        public float SteeringValue => m_SteeringValue;

        /// <summary>
        /// Ángulo actual del volante en grados.
        /// </summary>
        public float CurrentAngle => m_CurrentAngle;

        /// <summary>
        /// Indica si el volante está siendo agarrado por un interactor VR.
        /// </summary>
        public bool IsGrabbed => m_GrabInteractable != null && m_GrabInteractable.isSelected;

        /// <summary>
        /// Cantidad de manos (interactores) que agarran el volante en este momento.
        /// </summary>
        public int GrabbingHandCount => m_GrabInteractable != null ? m_GrabInteractable.interactorsSelecting.Count : 0;

        /// <summary>
        /// Indica si el volante está siendo agarrado con las dos manos a la vez.
        /// </summary>
        public bool IsHeldWithBothHands => GrabbingHandCount >= 2;

        private void Awake()
        {
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();

            m_InitialParent = transform.parent;
            m_InitialLocalPosition = transform.localPosition;
            m_InitialLocalRotation = transform.localRotation;

            ConfigureGrabInteractable();
            EnsureSimplifiedGrabCollider();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }
        }

        public void ConfigureGrabInteractable()
        {
            if (m_GrabInteractable == null) m_GrabInteractable = GetComponent<XRGrabInteractable>();

            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.trackPosition = false;
                m_GrabInteractable.trackRotation = false;
                m_GrabInteractable.trackScale = false;
                m_GrabInteractable.throwOnDetach = false;
                m_GrabInteractable.useDynamicAttach = false;
                m_GrabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                // Permite que las dos manos agarren a la vez sin que una le cancele la selección a la otra
                m_GrabInteractable.selectMode = InteractableSelectMode.Multiple;

                if (m_GrabInteractable.startingSingleGrabTransformers != null)
                {
                    m_GrabInteractable.startingSingleGrabTransformers.Clear();
                }
            }
        }

        public void EnsureSimplifiedGrabCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col == null || col is MeshCollider)
            {
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                BoxCollider boxCol = gameObject.AddComponent<BoxCollider>();
                boxCol.isTrigger = false;
                boxCol.center = Vector3.zero;
                boxCol.size = new Vector3(0.45f, 0.45f, 0.15f);
            }
        }

        private void OnEnable()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
                m_GrabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            if (m_GrabInteractable != null)
            {
                m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // Solo se inicializa la referencia de ESTA mano; las demás siguen siendo válidas
            IXRSelectInteractor hand = args.interactorObject;
            if (hand != null && hand.transform != null && TryGetHandPolar(hand.transform, out float angle, out _))
            {
                m_LastHandAngles[hand] = angle;
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            // Al disparar selectExited, el interactor ya salió de interactorsSelecting
            if (args.interactorObject != null) m_LastHandAngles.Remove(args.interactorObject);
        }

        private void Update()
        {
            ApplyLocalTransform(m_CurrentAngle);
        }

        private void FixedUpdate()
        {
            ApplyLocalTransform(m_CurrentAngle);
        }

        private void LateUpdate()
        {
            // 1. Restaurar jerarquía si un componente externo intentara desparentar el volante
            if (transform.parent != m_InitialParent && m_InitialParent != null)
            {
                transform.SetParent(m_InitialParent, false);
            }

            // 2. Procesar el ángulo del volante en LateUpdate, DESPUÉS de que DriverSeatFollower (Order=-300) haya actualizado XROrigin
            if (IsGrabbed)
            {
                m_CurrentAngle = Mathf.Clamp(m_CurrentAngle + ComputeHandsDelta(), -m_MaxSteeringAngle, m_MaxSteeringAngle);
            }
            else
            {
                // Soporte Gamepad (DualShock 4 / genérico) y Teclado como capa de testing
                float inputSteer = 0f;
                if (Gamepad.current != null)
                {
                    inputSteer = Gamepad.current.leftStick.x.ReadValue();
                }
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) inputSteer = -1f;
                    else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) inputSteer = 1f;
                }

                if (Mathf.Abs(inputSteer) > 0.05f)
                {
                    float targetAngle = inputSteer * m_MaxSteeringAngle;
                    m_CurrentAngle = Mathf.MoveTowards(m_CurrentAngle, targetAngle, m_ReturnSpeed * 2f * Time.deltaTime);
                }
                else if (m_ReturnToCenter && m_CurrentAngle != 0f)
                {
                    m_CurrentAngle = Mathf.MoveTowards(m_CurrentAngle, 0f, m_ReturnSpeed * Time.deltaTime);
                }
            }

            m_SteeringValue = m_MaxSteeringAngle > 0 ? Mathf.Clamp(m_CurrentAngle / m_MaxSteeringAngle, -1f, 1f) : 0f;

            // 3. Aplicar las coordenadas locales fijas y la rotación Z calculada
            ApplyLocalTransform(m_CurrentAngle);
        }

        private void ApplyLocalTransform(float angle)
        {
            transform.localPosition = m_InitialLocalPosition;
            transform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(-angle, Vector3.forward);
        }

        /// <summary>
        /// Permite fijar el ángulo del volante programáticamente (ej. en modo Piloto Automático).
        /// </summary>
        public void SetAngleProgrammatic(float angle)
        {
            m_CurrentAngle = Mathf.Clamp(angle, -m_MaxSteeringAngle, m_MaxSteeringAngle);
            m_SteeringValue = m_MaxSteeringAngle > 0 ? Mathf.Clamp(m_CurrentAngle / m_MaxSteeringAngle, -1f, 1f) : 0f;
            ApplyLocalTransform(m_CurrentAngle);
        }

        /// <summary>
        /// Calcula el delta angular combinado de todas las manos que agarran el volante en este frame.
        /// </summary>
        private float ComputeHandsDelta()
        {
            var hands = m_GrabInteractable.interactorsSelecting;
            float weightedDeltaSum = 0f;
            float weightSum = 0f;

            for (int i = 0; i < hands.Count; i++)
            {
                IXRSelectInteractor hand = hands[i];
                if (hand == null || hand.transform == null) continue;

                // Mano a menos de 1 cm del centro: no aporta este frame y conserva su referencia
                if (!TryGetHandPolar(hand.transform, out float currentAngle, out float radius)) continue;

                if (m_LastHandAngles.TryGetValue(hand, out float lastAngle))
                {
                    float handDelta = -Mathf.DeltaAngle(lastAngle, currentAngle);
                    weightedDeltaSum += handDelta * radius;
                    weightSum += radius;
                }
                // Sin referencia previa (orden de eventos): se inicializa y aporta delta 0 este frame
                m_LastHandAngles[hand] = currentAngle;
            }

            // Purgar referencias de manos que ya no agarran
            if (m_LastHandAngles.Count > hands.Count)
            {
                m_StaleHands.Clear();
                foreach (var kv in m_LastHandAngles)
                {
                    if (!IsSelecting(hands, kv.Key)) m_StaleHands.Add(kv.Key);
                }
                for (int i = 0; i < m_StaleHands.Count; i++) m_LastHandAngles.Remove(m_StaleHands[i]);
                m_StaleHands.Clear();
            }

            return WeightedAverageDelta(weightedDeltaSum, weightSum);
        }

        private static bool IsSelecting(IReadOnlyList<IXRSelectInteractor> hands, IXRSelectInteractor hand)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] == hand) return true;
            }
            return false;
        }

        /// <summary>
        /// Promedio de deltas angulares ponderado por radio: <c>Σ(delta·radio) / Σ(radio)</c>.
        /// Devuelve 0 si la suma de pesos es 0 o negativa (ninguna mano aporta).
        /// </summary>
        public static float WeightedAverageDelta(float weightedDeltaSum, float weightSum)
        {
            return weightSum > 0f ? weightedDeltaSum / weightSum : 0f;
        }

        /// <summary>
        /// Calcula la posición polar (ángulo en grados y radio en metros) de la mano en el plano 2D del volante.
        /// Devuelve false si la mano está a menos de 1 cm del centro (no aporta).
        /// </summary>
        private bool TryGetHandPolar(Transform hand, out float angleDeg, out float radius)
        {
            angleDeg = 0f;
            radius = 0f;

            Transform parentTr = transform.parent != null ? transform.parent : m_InitialParent;
            if (parentTr == null) return false;

            // Proyección 2D de la mano en el plano del volante (Empujar con la palma / arco del volante)
            Vector3 handLocalPos = parentTr.InverseTransformPoint(hand.position);
            Vector3 wheelLocalCenter = parentTr.InverseTransformPoint(transform.position);
            Vector3 dir = handLocalPos - wheelLocalCenter;

            Vector3 parentRight = m_InitialLocalRotation * Vector3.right;
            Vector3 parentUp = m_InitialLocalRotation * Vector3.up;

            float x = Vector3.Dot(dir, parentRight);
            float y = Vector3.Dot(dir, parentUp);

            float distSq = x * x + y * y;
            if (distSq < 0.0001f) return false; // Mano a menos de 1cm del centro exacto

            angleDeg = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            radius = Mathf.Sqrt(distSq);
            return true;
        }
    }
}
