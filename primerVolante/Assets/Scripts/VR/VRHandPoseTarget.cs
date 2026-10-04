using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Componente que define la postura de la mano requerida para interactuar con este elemento,
    /// los puntos de anclaje (attach) y la sincronización del movimiento/giro en tiempo real.
    /// Se acopla a VRSteeringWheel, VRHeadlightKnob, VRGearShifter, VRHandbrake y VRRearviewMirror.
    /// </summary>
    [DisallowMultipleComponent]
    public class VRHandPoseTarget : MonoBehaviour
    {
        [Header("Postura Requerida")]
        [Tooltip("Postura contextual que adoptará la mano al agarrar este objeto.")]
        [SerializeField] private HandPoseType m_PoseType = HandPoseType.Default;

        [Header("Seguimiento de Movimiento")]
        [Tooltip("Si es verdadero, la mano visual se ancla y acompaña el giro/traslación del objeto mientras esté agarrado.")]
        [SerializeField] private bool m_FollowObjectMotion = true;

        [Tooltip("Si es verdadero, calcula el punto de agarre dinámicamente en el aro/superficie del objeto al momento del contacto.")]
        [SerializeField] private bool m_UseDynamicContactPoint = false;

        [Tooltip("Radio del aro (en metros) si el objeto es un volante con contacto dinámico.")]
        [SerializeField] private float m_WheelRadius = 0.165f;

        [Tooltip("Desplazamiento métrico local desde el pivot de la muñeca hasta el centro de agarre de la palma en la mano.")]
        [SerializeField] private Vector3 m_HandPalmGripOffset = new Vector3(0f, -0.015f, 0f);

        [Header("Puntos de Anclaje Específicos (Opcional)")]
        [Tooltip("Transform de anclaje para la mano izquierda. Si es nulo, usa el centro o punto de contacto.")]
        [SerializeField] private Transform m_LeftAttachTransform;

        [Tooltip("Transform de anclaje para la mano derecha. Si es nulo, usa el centro o punto de contacto.")]
        [SerializeField] private Transform m_RightAttachTransform;

        [Header("Ajustes Finos de Offset")]
        [Tooltip("Offset de posición local aplicado a la mano respecto al anclaje.")]
        [SerializeField] private Vector3 m_PositionOffset = Vector3.zero;

        [Tooltip("Offset de rotación local en ángulos de Euler aplicado a la mano respecto al anclaje.")]
        [SerializeField] private Vector3 m_RotationOffset = Vector3.zero;

        public HandPoseType PoseType
        {
            get => m_PoseType;
            set => m_PoseType = value;
        }

        public bool FollowObjectMotion
        {
            get => m_FollowObjectMotion;
            set => m_FollowObjectMotion = value;
        }

        public bool UseDynamicContactPoint
        {
            get => m_UseDynamicContactPoint;
            set => m_UseDynamicContactPoint = value;
        }

        public float WheelRadius
        {
            get => m_WheelRadius;
            set => m_WheelRadius = value;
        }

        public Vector3 HandPalmGripOffset
        {
            get => m_HandPalmGripOffset;
            set => m_HandPalmGripOffset = value;
        }

        public Transform LeftAttachTransform
        {
            get => m_LeftAttachTransform;
            set => m_LeftAttachTransform = value;
        }

        public Transform RightAttachTransform
        {
            get => m_RightAttachTransform;
            set => m_RightAttachTransform = value;
        }

        public Vector3 PositionOffset
        {
            get => m_PositionOffset;
            set => m_PositionOffset = value;
        }

        public Vector3 RotationOffset
        {
            get => m_RotationOffset;
            set => m_RotationOffset = value;
        }

        /// <summary>
        /// Obtiene el transform de anclaje base para la mano especificada (izq/der).
        /// Si no hay transform explícito, retorna el transform de este componente.
        /// </summary>
        public Transform GetAttachTransform(bool isLeft)
        {
            if (isLeft && m_LeftAttachTransform != null) return m_LeftAttachTransform;
            if (!isLeft && m_RightAttachTransform != null) return m_RightAttachTransform;
            return transform;
        }

        /// <summary>
        /// Calcula el punto de anclaje y rotación local relativos al transform del objeto al momento de iniciar el agarre.
        /// </summary>
        public void CalculateInitialGrip(bool isLeft, Transform interactorTransform, out Vector3 localPosition, out Quaternion localRotation)
        {
            Transform targetTr = GetAttachTransform(isLeft);

            if (m_UseDynamicContactPoint)
            {
                // Proyectar la posición del mando sobre el plano del volante (XY local rotacional, sin verse afectado por escalas)
                Vector2 planarDir;
                if (interactorTransform != null)
                {
                    Vector3 toHand = interactorTransform.position - targetTr.position;
                    Vector3 unscaledLocal = Quaternion.Inverse(targetTr.rotation) * toHand;
                    planarDir = new Vector2(unscaledLocal.x, unscaledLocal.y);
                }
                else
                {
                    planarDir = Vector2.zero;
                }

                if (planarDir.sqrMagnitude < 0.001f)
                {
                    planarDir = isLeft ? new Vector2(-1f, 0.25f) : new Vector2(1f, 0.25f);
                }
                planarDir.Normalize();

                // 1. Orientación de la mano para envolver el marco cilíndrico del volante:
                // Dedos apuntando radialmente hacia el eje central (-rDir), dorso enfrentando al conductor (-Vector3.forward)
                // y palma orientada hacia el tubo del marco del volante (+Vector3.forward en espacio local).
                Vector3 rDir = new Vector3(planarDir.x, planarDir.y, 0f);
                localRotation = Quaternion.LookRotation(-rDir, -Vector3.forward) * Quaternion.Euler(m_RotationOffset);

                // 2. Compensación concéntrica muñeca-palma desplazando el anclaje radialmente hacia adentro:
                // El pivot de la mano está en la muñeca (Z = 0) y los dedos flexionados envuelven hacia adentro.
                // Al desplazar el anclaje hacia adentro en la dirección radial del aro (m_WheelRadius - inwardOffset),
                // el contorno del marco cilíndrico queda rodeado y encerrado por la mano en la postura SteeringWheel.
                // Este cálculo es estrictamente concéntrico a m_WheelRadius, evitando cualquier desfase acumulativo.
                float inwardOffset = m_HandPalmGripOffset.z;
                float depthOffset = m_HandPalmGripOffset.y != 0f ? m_HandPalmGripOffset.y : -0.015f;
                float effectiveRadius = Mathf.Max(0.05f, m_WheelRadius - inwardOffset);

                localPosition = new Vector3(planarDir.x * effectiveRadius, planarDir.y * effectiveRadius, depthOffset) + m_PositionOffset;
            }
            else if (targetTr != transform)
            {
                // Cuando hay un Transform de anclaje hijo dedicado asignado, la posición y rotación
                // base ya están configuradas de forma limpia en el espacio de la cabina.
                Vector3 palmOffset = (m_PoseType == HandPoseType.GearShifter)
                    ? new Vector3(0f, -0.015f, 0.045f)
                    : (m_PoseType == HandPoseType.Handbrake)
                        ? new Vector3(0f, -0.015f, 0.055f)
                        : Vector3.zero;

                localPosition = -palmOffset + m_PositionOffset;
                localRotation = Quaternion.Euler(m_RotationOffset);
            }
            else if (m_PoseType == HandPoseType.GearShifter)
            {
                // Fallback automático para palanca de cambios sin anclaje dedicado:
                // Detectar si el transform tiene rotación local de Blender (-90° en X, donde fwd = Up, up = Back)
                bool isBlenderRotation = Vector3.Dot(targetTr.forward, Vector3.up) > 0.7f;
                Vector3 localKnobPos;
                Quaternion localGripRot;

                if (isBlenderRotation)
                {
                    // En espacio Blender (-90° X): +Z es Up (0.18m hacia el pomo), -Y es Forward
                    localKnobPos = new Vector3(0f, 0f, 0.18f);
                    Quaternion worldRot = Quaternion.Euler(15f, 0f, 0f);
                    localGripRot = Quaternion.Inverse(targetTr.rotation) * worldRot;
                }
                else
                {
                    localKnobPos = new Vector3(0f, 0.18f, 0f);
                    localGripRot = Quaternion.Euler(15f, 0f, 0f);
                }

                Vector3 palmOffset = new Vector3(0f, -0.015f, 0.045f);
                localPosition = localKnobPos - (localGripRot * palmOffset) + m_PositionOffset;
                localRotation = localGripRot * Quaternion.Euler(m_RotationOffset);
            }
            else if (m_PoseType == HandPoseType.Handbrake)
            {
                // Fallback automático para freno de mano sin anclaje dedicado
                bool isBlenderRotation = Vector3.Dot(targetTr.forward, Vector3.up) > 0.7f;
                Vector3 localHandlePos;
                Quaternion localGripRot;

                if (isBlenderRotation)
                {
                    // En espacio Blender (-90° X): +Z es Up (0.12m), -Y es Forward (0.18m hacia el frente)
                    localHandlePos = new Vector3(0f, -0.18f, 0.12f);
                    Quaternion worldRot = isLeft ? Quaternion.Euler(20f, 10f, -35f) : Quaternion.Euler(20f, -10f, 35f);
                    localGripRot = Quaternion.Inverse(targetTr.rotation) * worldRot;
                }
                else
                {
                    localHandlePos = new Vector3(0f, 0.12f, 0.18f);
                    localGripRot = isLeft ? Quaternion.Euler(20f, 10f, -35f) : Quaternion.Euler(20f, -10f, 35f);
                }

                Vector3 palmOffset = new Vector3(0f, -0.015f, 0.055f);
                localPosition = localHandlePos - (localGripRot * palmOffset) + m_PositionOffset;
                localRotation = localGripRot * Quaternion.Euler(m_RotationOffset);
            }
            else if (m_PoseType == HandPoseType.KnobPinch)
            {
                // Fijar de frente a la perilla de luces (3.5 cm hacia el frente)
                localPosition = new Vector3(0f, 0f, -0.035f) + m_PositionOffset;
                if (isLeft)
                {
                    localRotation = Quaternion.Euler(0f, 0f, 0f) * Quaternion.Euler(m_RotationOffset);
                }
                else
                {
                    localRotation = Quaternion.Euler(0f, 0f, 180f) * Quaternion.Euler(m_RotationOffset);
                }
            }
            else if (interactorTransform != null && m_LeftAttachTransform == null && m_RightAttachTransform == null)
            {
                // Espejo retrovisor o interactables generales: calcular posición relativa al contacto sin escala de jerarquía
                Vector3 toHand = interactorTransform.position - targetTr.position;
                localPosition = Quaternion.Inverse(targetTr.rotation) * toHand + m_PositionOffset;
                localRotation = Quaternion.Inverse(targetTr.rotation) * interactorTransform.rotation * Quaternion.Euler(m_RotationOffset);
            }
            else
            {
                // Anclaje explícito general
                localPosition = m_PositionOffset;
                localRotation = Quaternion.Euler(m_RotationOffset);
            }
        }
    }
}
