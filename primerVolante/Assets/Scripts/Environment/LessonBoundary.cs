using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Pose de una valla de la línea: posición (altura incluida), yaw a lo largo de la cuerda del tramo y el
    /// tramo de spline (distancias) que cubre.
    /// </summary>
    public struct BarrierPose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>Distancia sobre la spline donde empieza el tramo. Puede exceder el largo en splines cerradas.</summary>
        public float StartDistance;

        /// <summary>Distancia sobre la spline donde termina el tramo. Puede exceder el largo en splines cerradas.</summary>
        public float EndDistance;
    }

    /// <summary>
    /// Funciones puras (sin escena ni estado) del reparto de vallas sobre una spline y de la deduplicación de
    /// avisos. Son deterministas y testeables en el Editor, igual que <see cref="NPCPathMath"/>.
    /// </summary>
    public static class LessonBoundaryMath
    {
        // Avance mínimo entre vallas (m): evita bucles degenerados si el solape es casi el largo de la valla.
        private const float k_MinStep = 0.1f;

        // Tolerancia al redondear la cantidad de vallas, para que un largo exacto no sume una valla de más.
        private const float k_CountEpsilon = 1e-4f;

        // Distancia a cada lado del punto medio con que se obtiene la dirección si la cuerda es degenerada.
        private const float k_FallbackProbe = 0.05f;

        /// <summary>Avance entre el inicio de dos vallas contiguas: largo menos solape.</summary>
        public static float Step(float barrierLength, float overlap)
        {
            float step = barrierLength - Mathf.Max(0f, overlap);
            return step >= k_MinStep ? step : Mathf.Max(barrierLength, k_MinStep);
        }

        /// <summary>
        /// Distancia que debe cubrir la línea: el largo de la spline y, si es cerrada, el solape extra con que
        /// la última valla se monta sobre la primera para que la costura no deje hueco.
        /// </summary>
        private static float Reach(float length, bool closed, float overlap)
        {
            return closed ? length + Mathf.Max(0f, overlap) : length;
        }

        /// <summary>Cantidad de vallas necesarias para cubrir una spline de <paramref name="length"/> metros.</summary>
        public static int CountBarriers(float length, bool closed, float barrierLength, float overlap)
        {
            if (length <= 0f || barrierLength <= 0f) return 0;

            float step = Step(barrierLength, overlap);
            float reach = Reach(length, closed, overlap);
            float extra = reach - Mathf.Max(0f, overlap);
            return Mathf.Max(1, Mathf.CeilToInt(extra / step - k_CountEpsilon));
        }

        /// <summary>
        /// Reparte vallas a lo largo de una spline. Avanza en pasos de <c>largo − solape</c>; cada valla se ubica
        /// en el punto medio de su tramo y se orienta con su eje X a lo largo de la cuerda entre el inicio y el fin
        /// del tramo (yaw en XZ, sin pitch ni roll; la altura sale de la spline). La última valla retrocede para
        /// terminar justo en el final sin pasarse. En una spline cerrada termina <paramref name="overlap"/> metros
        /// después del cierre, montada sobre la primera, así que no queda hueco en la costura.
        /// </summary>
        /// <param name="length">Largo de la spline en metros.</param>
        /// <param name="closed">true si la spline es cerrada.</param>
        /// <param name="barrierLength">Ancho de cada valla en metros.</param>
        /// <param name="overlap">Solape entre vallas contiguas en metros.</param>
        /// <param name="evaluate">Posición de mundo de la spline a una distancia dada (0..largo).</param>
        public static List<BarrierPose> ComputePoses(
            float length, bool closed, float barrierLength, float overlap, Func<float, Vector3> evaluate)
        {
            var poses = new List<BarrierPose>();
            int count = CountBarriers(length, closed, barrierLength, overlap);
            if (count == 0 || evaluate == null) return poses;

            float step = Step(barrierLength, overlap);
            float reach = Reach(length, closed, overlap);

            for (int i = 0; i < count; i++)
            {
                float start, end;
                if (i < count - 1)
                {
                    start = i * step;
                    end = start + barrierLength;
                }
                else if (count == 1)
                {
                    // Un tramo más corto que la valla: se centra en la línea.
                    start = 0f;
                    end = reach;
                }
                else
                {
                    end = reach;
                    start = reach - barrierLength;
                }

                poses.Add(MakePose(length, closed, start, end, evaluate));
            }

            return poses;
        }

        /// <summary>
        /// true si corresponde avisar: no hubo aviso previo para ese auto o ya pasó el enfriamiento.
        /// <paramref name="lastAlertTime"/> es <see cref="float.NegativeInfinity"/> si nunca avisó. Un reloj que
        /// retrocede (reinicio de escena) también habilita el aviso.
        /// </summary>
        public static bool ShouldAlert(float lastAlertTime, float now, float cooldownSeconds)
        {
            if (float.IsNegativeInfinity(lastAlertTime) || now < lastAlertTime) return true;
            return now - lastAlertTime >= Mathf.Max(0f, cooldownSeconds);
        }

        private static BarrierPose MakePose(float length, bool closed, float start, float end, Func<float, Vector3> evaluate)
        {
            Vector3 from = evaluate(WrapDistance(start, length, closed));
            Vector3 to = evaluate(WrapDistance(end, length, closed));
            Vector3 middle = evaluate(WrapDistance((start + end) * 0.5f, length, closed));

            Vector3 chord = new Vector3(to.x - from.x, 0f, to.z - from.z);
            if (chord.sqrMagnitude < 1e-6f)
            {
                // Cuerda degenerada (tramo cerrado sobre sí mismo): se toma la dirección local en el medio.
                float mid = (start + end) * 0.5f;
                Vector3 before = evaluate(WrapDistance(mid - k_FallbackProbe, length, closed));
                Vector3 after = evaluate(WrapDistance(mid + k_FallbackProbe, length, closed));
                chord = new Vector3(after.x - before.x, 0f, after.z - before.z);
            }

            float yaw = chord.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(-chord.z, chord.x) * Mathf.Rad2Deg;

            return new BarrierPose
            {
                Position = middle,
                Rotation = Quaternion.AngleAxis(yaw, Vector3.up),
                StartDistance = start,
                EndDistance = end,
            };
        }

        private static float WrapDistance(float distance, float length, bool closed)
        {
            if (closed)
            {
                float wrapped = distance % length;
                return wrapped < 0f ? wrapped + length : wrapped;
            }

            return Mathf.Clamp(distance, 0f, length);
        }
    }

    /// <summary>
    /// Línea de vallas de control policial definida por una spline: delimita el área de práctica de una lección
    /// (US30). Reparte instancias del prefab <see cref="LessonBarrier"/> a lo largo de la spline, las deja
    /// guardadas en la escena como hijos de un contenedor <c>Barriers</c> y reexpone el evento de contacto con
    /// deduplicación por vehículo. Solo avisa: la falta o el reinicio no son responsabilidad de este componente.
    /// Para apagar la línea alcanza con <c>SetActive(false)</c> sobre este GameObject.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    [DisallowMultipleComponent]
    public class LessonBoundary : MonoBehaviour
    {
        /// <summary>Nombre del contenedor de vallas generadas.</summary>
        public const string CONTAINER_NAME = "Barriers";

        [Header("Vallas")]
        [Tooltip("Prefab de valla a repartir (LessonBarrier).")]
        [SerializeField] private GameObject m_BarrierPrefab;

        [Tooltip("Índice de la spline dentro del SplineContainer.")]
        [SerializeField] private int m_SplineIndex = 0;

        [Tooltip("Ancho de cada valla en metros (el de la malla: 3.6).")]
        [SerializeField] private float m_BarrierLength = 3.6f;

        [Tooltip("Solape entre vallas contiguas en metros, para no dejar rendijas en las curvas.")]
        [SerializeField] private float m_Overlap = 0.1f;

        [Header("Aviso")]
        [Tooltip("Segundos durante los que no se repite el aviso para el mismo auto. El auto puede tocar dos vallas contiguas a la vez.")]
        [SerializeField] private float m_AlertCooldownSeconds = 1f;

        [Header("Depuración")]
        [Tooltip("Dibuja la línea y el rectángulo de cada valla en la Scene view.")]
        [SerializeField] private bool m_DrawGizmos = true;

        private readonly List<LessonBarrier> m_Subscribed = new List<LessonBarrier>();
        private readonly Dictionary<EntityId, float> m_LastAlertTime = new Dictionary<EntityId, float>();

        /// <summary>
        /// Se dispara una sola vez por pasada cuando el auto del jugador alcanza cualquier valla de la línea:
        /// los avisos repetidos del mismo auto dentro de <see cref="AlertCooldownSeconds"/> se descartan.
        /// </summary>
        public event Action<LessonBarrierContact> OnVehicleReachedBoundary;

        public GameObject BarrierPrefab => m_BarrierPrefab;
        public int SplineIndex => m_SplineIndex;
        public float BarrierLength => m_BarrierLength;
        public float Overlap => m_Overlap;
        public float AlertCooldownSeconds => m_AlertCooldownSeconds;

        private void OnEnable()
        {
            foreach (LessonBarrier barrier in GetComponentsInChildren<LessonBarrier>(true))
            {
                barrier.OnVehicleReached += HandleBarrierReached;
                m_Subscribed.Add(barrier);
            }
        }

        private void OnDisable()
        {
            foreach (LessonBarrier barrier in m_Subscribed)
            {
                if (barrier != null) barrier.OnVehicleReached -= HandleBarrierReached;
            }

            m_Subscribed.Clear();
            m_LastAlertTime.Clear();
        }

        private void HandleBarrierReached(LessonBarrierContact contact)
        {
            if (contact.Vehicle == null) return;

            // Los avisos llegan desde callbacks de física: se mide con el reloj fijo.
            float now = Time.fixedTime;
            EntityId key = contact.Vehicle.GetEntityId();
            float last = m_LastAlertTime.TryGetValue(key, out float stored) ? stored : float.NegativeInfinity;
            if (!LessonBoundaryMath.ShouldAlert(last, now, m_AlertCooldownSeconds)) return;

            m_LastAlertTime[key] = now;
            OnVehicleReachedBoundary?.Invoke(contact);
        }

        /// <summary>
        /// Poses de las vallas según la spline y los parámetros actuales. Devuelve false si no hay spline válida.
        /// </summary>
        public bool TryComputePoses(out List<BarrierPose> poses, out float splineLength)
        {
            poses = null;
            splineLength = 0f;

            var container = GetComponent<SplineContainer>();
            if (container == null || m_SplineIndex < 0 || m_SplineIndex >= container.Splines.Count) return false;

            Spline spline = container.Splines[m_SplineIndex];
            float worldLength = container.CalculateLength(m_SplineIndex);
            float localLength = spline.GetLength();
            if (worldLength <= 0f || localLength <= 0f) return false;

            float worldPerLocal = worldLength / localLength;
            Vector3 Evaluate(float distance)
            {
                Vector3 position = default;
                Quaternion rotation = Quaternion.identity;
                NPCPathFollower.EvaluatePoseOnPath(container, m_SplineIndex, worldPerLocal, distance, ref position, ref rotation);
                return position;
            }

            splineLength = worldLength;
            poses = LessonBoundaryMath.ComputePoses(worldLength, spline.Closed, m_BarrierLength, m_Overlap, Evaluate);
            return true;
        }

        /// <summary>
        /// Borra y recrea el contenedor <c>Barriers</c> con una instancia del prefab por valla. Las instancias
        /// quedan guardadas en la escena (existen en el build sin regenerarse). Solo en el Editor y fuera de Play.
        /// </summary>
        [ContextMenu("Reconstruir barreras")]
        public void RebuildBarriers()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning("[LessonBoundary] ⚠️ Las barreras se reconstruyen en el Editor, fuera de Play Mode.");
                return;
            }

            if (m_BarrierPrefab == null)
            {
                Debug.LogWarning($"[LessonBoundary] ⚠️ {name}: falta asignar el prefab de valla (LessonBarrier).");
                return;
            }

            if (!TryComputePoses(out List<BarrierPose> poses, out float splineLength))
            {
                Debug.LogWarning($"[LessonBoundary] ⚠️ {name}: no hay una spline válida en el índice {m_SplineIndex}.");
                return;
            }

            if (!Mathf.Approximately(transform.lossyScale.x, 1f) || !Mathf.Approximately(transform.lossyScale.y, 1f) ||
                !Mathf.Approximately(transform.lossyScale.z, 1f))
            {
                Debug.LogWarning($"[LessonBoundary] ⚠️ {name}: la escala no es 1; las vallas heredarán la deformación.");
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Reconstruir barreras");
            int undoGroup = Undo.GetCurrentGroup();

            Transform old = transform.Find(CONTAINER_NAME);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var container = new GameObject(CONTAINER_NAME);
            Undo.RegisterCreatedObjectUndo(container, "Reconstruir barreras");
            container.transform.SetParent(transform, false);

            for (int i = 0; i < poses.Count; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(m_BarrierPrefab, container.transform);
                Undo.RegisterCreatedObjectUndo(instance, "Reconstruir barreras");
                instance.name = $"Barrier_{i:00}";
                instance.transform.SetPositionAndRotation(poses[i].Position, poses[i].Rotation);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[LessonBoundary] 🚧 {name}: {poses.Count} vallas sobre {splineLength:F1} m de spline.");
#else
            Debug.LogWarning("[LessonBoundary] ⚠️ RebuildBarriers solo está disponible en el Editor.");
#endif
        }

        private void OnValidate()
        {
            m_BarrierLength = Mathf.Max(0.5f, m_BarrierLength);
            m_Overlap = Mathf.Clamp(m_Overlap, 0f, m_BarrierLength * 0.5f);
            m_AlertCooldownSeconds = Mathf.Max(0f, m_AlertCooldownSeconds);
            m_SplineIndex = Mathf.Max(0, m_SplineIndex);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            m_BarrierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/LessonBarrier.prefab");
        }

        private void OnDrawGizmos()
        {
            if (!m_DrawGizmos) return;
            if (!TryComputePoses(out List<BarrierPose> poses, out float _)) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 1f);

            // Rectángulo de cada valla (largo × alto × espesor del sólido), visible aunque el renderer esté oculto.
            var size = new Vector3(m_BarrierLength, 2.05f, 0.4f);
            foreach (BarrierPose pose in poses)
            {
                Gizmos.matrix = Matrix4x4.TRS(pose.Position + Vector3.up * (size.y * 0.5f), pose.Rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, size);
            }

            // Línea que une los puntos medios de las vallas.
            Gizmos.matrix = Matrix4x4.identity;
            for (int i = 1; i < poses.Count; i++)
            {
                Gizmos.DrawLine(poses[i - 1].Position, poses[i].Position);
            }

            Gizmos.matrix = previous;
        }
#endif
    }
}
