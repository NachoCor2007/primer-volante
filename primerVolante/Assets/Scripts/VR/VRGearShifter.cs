using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections;

namespace PrimerVolante.VR
{
    public enum GearState
    {
        Park,
        Reverse,
        Neutral,
        Drive
    }

    [System.Serializable]
    public class GearStateEvent : UnityEvent<GearState> { }

    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
    public class VRGearShifter : MonoBehaviour
    {
        [Header("Gear Settings")]
        public GearState currentGear = GearState.Park;
        
        [Tooltip("Eje local sobre el que rota la palanca (X por defecto).")]
        public Vector3 rotationAxis = Vector3.right;

        [Header("Ángulos de los Cambios (Limites)")]
        [Tooltip("Ángulo de la palanca hacia adelante (Parking)")]
        public float parkAngle = 40f;
        [Tooltip("Ángulo intermedio-adelante (Reversa)")]
        public float reverseAngle = 15f;
        [Tooltip("Ángulo intermedio-atrás (Neutral)")]
        public float neutralAngle = -10f;
        [Tooltip("Ángulo de la palanca hacia atrás (Drive)")]
        public float driveAngle = -35f;

        [Header("Comportamiento")]
        [Tooltip("Velocidad a la que la palanca se imanta a la posición más cercana al soltarla")]
        public float snapSpeed = 10f;

        [Header("Eventos")]
        public GearStateEvent OnGearChanged;

        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable m_Interactable;
        private bool m_IsGrabbed = false;
        private Coroutine m_SnapRoutine;
        
        // Variables para el agarre suave (sin saltos bruscos)
        private float m_GrabStartLeverAngle;
        private float m_GrabStartHandAngle;
        
        // Rotación "Cero" de la palanca (para evitar problemas de Gimbal Lock)
        private Quaternion m_ZeroRotation;

        private void Awake()
        {
            m_Interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
            
            // EVITAR QUE CAIGA AL SUELO: Forzamos el Rigidbody a ser cinemático
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true;
            }
            
            // La posición exacta en la que pusiste la palanca en Unity es nuestro "Cero" (0 grados).
            // A partir de ahí, se sumarán o restarán los grados de las marchas.
            m_ZeroRotation = transform.localRotation;

            // Si el usuario sigue usando XRGrabInteractable, configuramos las físicas
            var grab = m_Interactable as UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable;
            if (grab != null)
            {
                grab.trackPosition = false; 
                grab.trackRotation = true; // Se requiere true en algunas versiones de XRI para que el agarre no se suelte
                grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Kinematic;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;
            }

            m_Interactable.selectEntered.AddListener(OnGrab);
            m_Interactable.selectExited.AddListener(OnRelease);
        }

        private void Start()
        {
            // Imantar a la posición inicial al comenzar
            SetRotationToGear(currentGear);
            
            m_LastGearEditor = currentGear;
            m_LastPark = parkAngle;
            m_LastRev = reverseAngle;
            m_LastNeu = neutralAngle;
            m_LastDrive = driveAngle;
        }

        private GearState m_LastGearEditor;
        private float m_LastPark, m_LastRev, m_LastNeu, m_LastDrive;

        private void Update()
        {
            // Esto permite probar los ángulos y cambiar la marcha desde el Inspector en vivo mientras juegas
            if (!m_IsGrabbed)
            {
                if (m_LastGearEditor != currentGear || m_LastPark != parkAngle || m_LastRev != reverseAngle || m_LastNeu != neutralAngle || m_LastDrive != driveAngle)
                {
                    if (m_SnapRoutine != null) StopCoroutine(m_SnapRoutine);
                    SetRotationToGear(currentGear);
                    
                    if (m_LastGearEditor != currentGear) {
                        OnGearChanged?.Invoke(currentGear);
                    }

                    m_LastGearEditor = currentGear;
                    m_LastPark = parkAngle;
                    m_LastRev = reverseAngle;
                    m_LastNeu = neutralAngle;
                    m_LastDrive = driveAngle;
                }
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

        private void OnGrab(SelectEnterEventArgs args)
        {
            m_IsGrabbed = true;
            if (m_SnapRoutine != null)
            {
                StopCoroutine(m_SnapRoutine);
            }
            
            // Guardar en qué ángulo estaba la palanca exactamente
            m_GrabStartLeverAngle = GetCurrentAngle();
            
            // Calcular en qué ángulo está la mano al momento de agarrar
            var interactor = args.interactorObject;
            Vector3 handWorldPos = interactor.transform.position;
            Vector3 dirToHand = handWorldPos - transform.position;
            
            if (dirToHand.sqrMagnitude > 0.0001f)
            {
                // Convertir la dirección de la mano al espacio "Cero" de la palanca
                Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
                Quaternion zeroWorldRot = parentRot * m_ZeroRotation;
                Vector3 localDir = Quaternion.Inverse(zeroWorldRot) * dirToHand;
                
                m_GrabStartHandAngle = 0f;
                if (rotationAxis == Vector3.right) m_GrabStartHandAngle = Mathf.Atan2(localDir.z, localDir.y) * Mathf.Rad2Deg;
                else if (rotationAxis == Vector3.up) m_GrabStartHandAngle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                else if (rotationAxis == Vector3.forward) m_GrabStartHandAngle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            m_IsGrabbed = false;
            
            // Determinar cuál es el cambio más cercano al soltar
            float currentAngle = GetCurrentAngle();
            GearState closestGear = GetClosestGear(currentAngle);
            
            if (closestGear != currentGear)
            {
                currentGear = closestGear;
                OnGearChanged?.Invoke(currentGear);
                Debug.Log($"[VRGearShifter] Cambio insertado: {currentGear}");
            }

            // Iniciar animación de encaje (imán)
            m_SnapRoutine = StartCoroutine(SnapToGear(closestGear));
        }

        private void LateUpdate()
        {
            if (m_IsGrabbed && m_Interactable.interactorsSelecting.Count > 0)
            {
                // Obtener la posición de la mano (o láser)
                var interactor = m_Interactable.interactorsSelecting[0];
                Vector3 handWorldPos = interactor.transform.position;
                
                // Calcular la dirección desde la base de la palanca hacia la mano
                Vector3 dirToHand = handWorldPos - transform.position;
                
                if (dirToHand.sqrMagnitude > 0.0001f)
                {
                    // Convertir la dirección de la mano al espacio "Cero" de la palanca
                    Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
                    Quaternion zeroWorldRot = parentRot * m_ZeroRotation;
                    Vector3 localDir = Quaternion.Inverse(zeroWorldRot) * dirToHand;
                    
                    float currentHandAngle = 0f;
                    
                    if (rotationAxis == Vector3.right) 
                        currentHandAngle = Mathf.Atan2(localDir.z, localDir.y) * Mathf.Rad2Deg;
                    else if (rotationAxis == Vector3.up) 
                        currentHandAngle = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                    else if (rotationAxis == Vector3.forward) 
                        currentHandAngle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;

                    // Cuánto rotó la mano desde el momento del agarre
                    float deltaAngle = Mathf.DeltaAngle(m_GrabStartHandAngle, currentHandAngle);
                    
                    // Sumárselo a donde estaba la palanca
                    float targetAngle = m_GrabStartLeverAngle + deltaAngle;

                    // Limitar a los máximos permitidos
                    float maxAngle = Mathf.Max(parkAngle, driveAngle);
                    float minAngle = Mathf.Min(parkAngle, driveAngle);
                    float clampedAngle = Mathf.Clamp(targetAngle, minAngle, maxAngle);
                    
                    // Aplicar
                    ApplyRotation(clampedAngle);
                }
            }
        }

        private float GetCurrentAngle()
        {
            // Usamos Quaternions para comparar rotaciones sin sufrir de "Gimbal Lock" (los ejes locos de Unity)
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

        private GearState GetClosestGear(float angle)
        {
            GearState closest = GearState.Park;
            float minDiff = float.MaxValue;

            float[] angles = { parkAngle, reverseAngle, neutralAngle, driveAngle };
            GearState[] states = { GearState.Park, GearState.Reverse, GearState.Neutral, GearState.Drive };

            for (int i = 0; i < angles.Length; i++)
            {
                float diff = Mathf.Abs(Mathf.DeltaAngle(angle, angles[i]));
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closest = states[i];
                }
            }
            return closest;
        }

        private void SetRotationToGear(GearState gear)
        {
            float targetAngle = GetAngleForGear(gear);
            ApplyRotation(targetAngle);
        }

        private void ApplyRotation(float angle)
        {
            transform.localRotation = m_ZeroRotation * Quaternion.AngleAxis(angle, rotationAxis);
        }

        private float GetAngleForGear(GearState gear)
        {
            switch (gear)
            {
                case GearState.Park: return parkAngle;
                case GearState.Reverse: return reverseAngle;
                case GearState.Neutral: return neutralAngle;
                case GearState.Drive: return driveAngle;
                default: return parkAngle;
            }
        }

        private IEnumerator SnapToGear(GearState gear)
        {
            float targetAngle = GetAngleForGear(gear);
            while (true)
            {
                float currentAngle = GetCurrentAngle();
                float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * snapSpeed);
                ApplyRotation(newAngle);

                if (Mathf.Abs(Mathf.DeltaAngle(newAngle, targetAngle)) < 0.5f)
                {
                    SetRotationToGear(gear); // Fijar exactamente en el ángulo final
                    break;
                }
                yield return null;
            }
        }
    }
}
