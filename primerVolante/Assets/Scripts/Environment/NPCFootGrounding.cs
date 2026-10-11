using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Corrige en LateUpdate el sesgo que el retargeting humanoide deja en los pies de un peatón: el clip de
    /// Mixamo toma su pose de referencia del primer frame de la animación (no de una T-pose), así que sobre otro
    /// personaje el cuerpo camina algo hundido y un pie pisa con el talón mientras el otro lo hace de punta.
    /// Se aplican dos offsets constantes medidos en el setup (no se fuerza el pie plano, para conservar el
    /// rodado natural talón → punta y no introducir deslizamiento): una elevación del esqueleto y un pitch por pie.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class NPCFootGrounding : MonoBehaviour
    {
        [Header("Sesgo medido del retargeting")]
        [Tooltip("Elevación del esqueleto en metros: diferencia entre la altura del tobillo en reposo y en apoyo.")]
        [SerializeField] private float m_HipsLift = 0f;

        [Tooltip("Corrección de pitch del pie izquierdo en grados (positivo = dedos hacia arriba).")]
        [SerializeField] private float m_LeftPitchOffset = 0f;

        [Tooltip("Corrección de pitch del pie derecho en grados (positivo = dedos hacia arriba).")]
        [SerializeField] private float m_RightPitchOffset = 0f;

        private Animator m_Animator;
        private Transform m_Hips;
        private Transform m_LeftFoot;
        private Transform m_LeftToes;
        private Transform m_RightFoot;
        private Transform m_RightToes;
        private bool m_Resolved;

        /// <summary>Elevación del esqueleto en metros.</summary>
        public float HipsLift => m_HipsLift;

        /// <summary>Corrección de pitch del pie izquierdo en grados.</summary>
        public float LeftPitchOffset => m_LeftPitchOffset;

        /// <summary>Corrección de pitch del pie derecho en grados.</summary>
        public float RightPitchOffset => m_RightPitchOffset;

        /// <summary>
        /// Fija la corrección (la mide el setup comparando el apoyo al caminar con la pose de reposo).
        /// </summary>
        public void Calibrate(float hipsLift, float leftPitchOffset, float rightPitchOffset)
        {
            m_HipsLift = hipsLift;
            m_LeftPitchOffset = leftPitchOffset;
            m_RightPitchOffset = rightPitchOffset;
        }

        private void LateUpdate()
        {
            Apply();
        }

        /// <summary>
        /// Aplica la corrección sobre la pose que acaba de evaluar el Animator. Es pública para poder medirla
        /// en el Editor avanzando el Animator a mano, sin pasar por LateUpdate.
        /// </summary>
        public void Apply()
        {
            if (!Resolve()) return;

            m_Hips.position += Vector3.up * m_HipsLift;
            Pitch(m_LeftFoot, m_LeftToes, m_LeftPitchOffset);
            Pitch(m_RightFoot, m_RightToes, m_RightPitchOffset);
        }

        /// <summary>
        /// Rota el pie alrededor del tobillo para que el vector tobillo → dedos cambie su pitch en
        /// <paramref name="offsetDegrees"/> manteniendo su dirección horizontal.
        /// </summary>
        private static void Pitch(Transform foot, Transform toes, float offsetDegrees)
        {
            if (Mathf.Abs(offsetDegrees) < 0.01f) return;

            Vector3 current = toes.position - foot.position;
            Vector3 horizontal = new Vector3(current.x, 0f, current.z);
            if (horizontal.sqrMagnitude < 1e-6f) return;

            float pitch = Mathf.Atan2(current.y, horizontal.magnitude) + offsetDegrees * Mathf.Deg2Rad;
            Vector3 desired = horizontal.normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            foot.rotation = Quaternion.FromToRotation(current, desired) * foot.rotation;
        }

        private bool Resolve()
        {
            if (m_Resolved) return m_Hips != null;

            m_Animator = GetComponent<Animator>();
            if (m_Animator == null || !m_Animator.isHuman) return false;

            m_Hips = m_Animator.GetBoneTransform(HumanBodyBones.Hips);
            m_LeftFoot = m_Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            m_LeftToes = m_Animator.GetBoneTransform(HumanBodyBones.LeftToes);
            m_RightFoot = m_Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            m_RightToes = m_Animator.GetBoneTransform(HumanBodyBones.RightToes);

            bool complete = m_Hips != null && m_LeftFoot != null && m_LeftToes != null &&
                            m_RightFoot != null && m_RightToes != null;
            if (!complete)
            {
                Debug.LogWarning($"[NPCFootGrounding] ⚠️ {name}: faltan huesos humanoides (cadera, pies o dedos); no se corrige el apoyo.");
                m_Hips = null;
            }

            m_Resolved = true;
            return complete;
        }
    }
}
