using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Háptica de alcance y de agarre de un control del habitáculo. Va en cada interactable
    /// (XRGrabInteractable / XRSimpleInteractable) y reproduce la firma de su categoría según el
    /// <see cref="CabinHapticProfile"/>. Reemplaza al hover/select genérico de <c>SimpleHapticFeedback</c>,
    /// que se apaga en la escena.
    /// </summary>
    [DisallowMultipleComponent]
    public class CabinHapticTarget : MonoBehaviour
    {
        [Header("Configuración")]
        [Tooltip("Perfil con las firmas hápticas.")]
        [SerializeField] private CabinHapticProfile m_Profile;

        [Tooltip("Categoría del control: define qué firma de alcance/agarre usa.")]
        [SerializeField] private CabinHapticCategory m_Category = CabinHapticCategory.ButtonsAndKnobs;

        [Tooltip("Si es falso no vibra al alcanzar el control (solo al agarrarlo). Se desactiva en los botones del panel de espejos, " +
                 "que están muy juntos.")]
        [SerializeField] private bool m_PlayReach = true;

        [Header("Debugging / Logs")]
        [SerializeField] private bool m_EnableDebugLogs = false;

        private XRBaseInteractable m_Interactable;
        private float m_ReadyAt;

        /// <summary>Categoría del control.</summary>
        public CabinHapticCategory Category => m_Category;

        /// <summary>Indica si el control vibra al alcanzarlo.</summary>
        public bool PlayReach => m_PlayReach;

        /// <summary>Perfil asignado.</summary>
        public CabinHapticProfile Profile => m_Profile;

        private void Awake()
        {
            m_Interactable = GetComponent<XRBaseInteractable>();
        }

        private void OnEnable()
        {
            m_ReadyAt = Time.time + VehicleAudioUtil.InitializationGraceSeconds;

            if (m_Interactable == null) m_Interactable = GetComponent<XRBaseInteractable>();
            if (m_Interactable == null) return;

            m_Interactable.hoverEntered.AddListener(OnHoverEntered);
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (m_Interactable == null) return;
            m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!m_PlayReach || m_Profile == null || Time.time < m_ReadyAt) return;

            IXRHoverInteractor interactor = args.interactorObject;
            if (!(interactor is NearFarInteractor) && !(interactor is XRPokeInteractor)) return;

            bool isSelecting = interactor is IXRSelectInteractor selectInteractor && selectInteractor.hasSelection;
            float distance = DistanceToInteractor(interactor);
            if (!ShouldPlayReach(distance, m_Profile.MaxReachDistance, isSelecting)) return;

            HapticImpulsePlayer player = HapticPlayback.ResolvePlayer(interactor);
            if (player == null) return;
            if (!HapticPlayback.TryConsumeReachCooldown(player, m_Profile.ReachCooldownSeconds)) return;

            HapticPlayback.Play(player, m_Profile.GetControl(m_Category).Reach, m_Profile.MasterAmplitude);
            if (m_EnableDebugLogs)
                Debug.Log($"[CabinHapticTarget:{name}] 🤚 Alcance ({m_Category}) a {distance * 100f:F1} cm.");
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (m_Profile == null || Time.time < m_ReadyAt) return;

            HapticPlayback.Play(args.interactorObject, m_Profile.GetControl(m_Category).Grab, m_Profile.MasterAmplitude);
            if (m_EnableDebugLogs)
                Debug.Log($"[CabinHapticTarget:{name}] ✊ Agarre ({m_Category}).");
        }

        /// <summary>
        /// Distancia en metros del attach transform del interactor al punto más cercano de los
        /// colliders del interactable (0 si está dentro). Infinito si no hay colliders válidos.
        /// </summary>
        private float DistanceToInteractor(IXRHoverInteractor interactor)
        {
            Transform attach = interactor.GetAttachTransform(m_Interactable);
            if (attach == null) return float.PositiveInfinity;

            Vector3 position = attach.position;
            float minSqr = float.PositiveInfinity;
            foreach (Collider col in m_Interactable.colliders)
            {
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

                // Collider.ClosestPoint solo admite Box/Sphere/Capsule/Mesh convexo.
                Vector3 closest = col is MeshCollider mesh && !mesh.convex
                    ? col.bounds.ClosestPoint(position)
                    : col.ClosestPoint(position);

                float sqr = (position - closest).sqrMagnitude;
                if (sqr < minSqr) minSqr = sqr;
            }

            return float.IsPositiveInfinity(minSqr) ? float.PositiveInfinity : Mathf.Sqrt(minSqr);
        }

        /// <summary>
        /// Indica si la distancia corresponde a un "alcance real" (≤ máximo).
        /// </summary>
        public static bool IsWithinReach(float distance, float maxDistance)
        {
            return distance <= maxDistance;
        }

        /// <summary>
        /// Filtro del háptico de alcance: la mano debe estar realmente cerca del control y no estar
        /// agarrando nada. Evita que el rayo del menú abierto dispare háptica de alcance desde lejos.
        /// </summary>
        public static bool ShouldPlayReach(float distance, float maxDistance, bool interactorIsSelecting)
        {
            return !interactorIsSelecting && IsWithinReach(distance, maxDistance);
        }
    }
}
