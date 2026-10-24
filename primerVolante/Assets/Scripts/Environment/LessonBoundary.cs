using System;
using System.Collections.Generic;
using PrimerVolante.Evaluation;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Pose de una valla de la línea: posición (altura incluida), yaw a lo largo de la cuerda del tramo, el
    /// tramo de spline (distancias) que cubre y cuánto se estira a lo largo respecto del ancho natural.
    /// </summary>
    public struct BarrierPose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>Distancia sobre la spline donde empieza el tramo.</summary>
        public float StartDistance;

        /// <summary>Distancia sobre la spline donde termina el tramo.</summary>
        public float EndDistance;

        /// <summary>
        /// Factor sobre el eje X (el largo) de la valla: tramo / ancho natural. 1 = sin deformar. Solo es distinto
        /// de 1 con <c>stretchToFit</c>, que reparte el ancho sobrante o faltante entre todas las vallas por igual.
        /// </summary>
        public float LengthScale;
    }

    /// <summary>
    /// Funciones puras (sin escena ni estado) del reparto de vallas sobre una spline y de la deduplicación de
    /// avisos. Son deterministas y testeables en el Editor, igual que <see cref="NPCPathMath"/>.
    /// </summary>
    public static class LessonBoundaryMath
    {
        // Ancho mínimo de un tramo (m): evita vallas degeneradas con splines casi vacías.
        private const float k_MinWidth = 0.05f;

        // Tolerancia al redondear la cantidad de vallas, para que un largo exacto no sume una valla de más.
        private const float k_CountEpsilon = 1e-4f;

        // Distancia a cada lado del punto medio con que se obtiene la dirección si la cuerda es degenerada.
        private const float k_FallbackProbe = 0.05f;

        /// <summary>
        /// Cantidad de vallas para una spline de <paramref name="length"/> metros. Con <paramref name="stretchToFit"/>
        /// es la que mejor se ajusta (la valla se estira o encoge un poco para llenar el espacio); sin él es la
        /// mayor cantidad que entra sin pisarse y el sobrante queda repartido como separación.
        /// </summary>
        public static int CountBarriers(float length, bool closed, float barrierLength, float gap, bool stretchToFit = true)
        {
            if (length <= 0f || barrierLength <= 0f) return 0;

            float safeGap = Mathf.Max(0f, gap);
            float pitch = barrierLength + safeGap;

            // Abierta: n vallas y n−1 separaciones. Cerrada: n vallas y n separaciones (hay una en la costura).
            float ratio = closed ? length / pitch : (length + safeGap) / pitch;
            int count = stretchToFit ? Mathf.RoundToInt(ratio) : Mathf.FloorToInt(ratio + k_CountEpsilon);
            return Mathf.Max(1, count);
        }

        /// <summary>
        /// Reparte vallas a lo largo de una spline sin que se pisen: todas miden lo mismo, quedan separadas por una
        /// distancia pareja y la primera arranca en el inicio y la última termina justo en el final (en una spline
        /// cerrada también queda separación en la costura). Cada valla se ubica en el punto medio de su tramo y se
        /// orienta con su eje X a lo largo de la cuerda entre el inicio y el fin del tramo (yaw en XZ, sin pitch ni
        /// roll; la altura sale de la spline).
        /// </summary>
        /// <param name="length">Largo de la spline en metros.</param>
        /// <param name="closed">true si la spline es cerrada.</param>
        /// <param name="barrierLength">Ancho natural de cada valla en metros.</param>
        /// <param name="gap">Separación deseada entre vallas contiguas en metros.</param>
        /// <param name="evaluate">Posición de mundo de la spline a una distancia dada (0..largo).</param>
        /// <param name="stretchToFit">
        /// true: la cantidad se redondea y cada valla se estira o encoge lo mismo para que la separación sea
        /// exactamente <paramref name="gap"/> (nunca quedan huecos por donde pase el auto). false: no se deforma
        /// ninguna valla y la separación crece para repartir el sobrante.
        /// </param>
        public static List<BarrierPose> ComputePoses(
            float length, bool closed, float barrierLength, float gap, Func<float, Vector3> evaluate, bool stretchToFit = true)
        {
            var poses = new List<BarrierPose>();
            int count = CountBarriers(length, closed, barrierLength, gap, stretchToFit);
            if (count == 0 || evaluate == null) return poses;

            float safeGap = Mathf.Max(0f, gap);
            float width;
            float spacing;

            if (stretchToFit)
            {
                float gaps = closed ? count : count - 1;
                width = Mathf.Max(k_MinWidth, (length - gaps * safeGap) / count);
                spacing = safeGap;
            }
            else
            {
                width = barrierLength;
                float gaps = closed ? count : count - 1;
                spacing = gaps > 0f ? Mathf.Max(0f, (length - count * width) / gaps) : 0f;
            }

            // Una sola valla en una spline abierta: se centra en la línea.
            float offset = (!stretchToFit && count == 1 && !closed) ? (length - width) * 0.5f : 0f;

            for (int i = 0; i < count; i++)
            {
                float start = offset + i * (width + spacing);
                float end = start + width;
                poses.Add(MakePose(length, closed, start, end, barrierLength, evaluate));
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

        private static BarrierPose MakePose(
            float length, bool closed, float start, float end, float barrierLength, Func<float, Vector3> evaluate)
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
                LengthScale = barrierLength > 0f ? (end - start) / barrierLength : 1f,
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
    /// deduplicación por vehículo. Informa cada pasada como falta por <see cref="InfractionReporting"/> y emite el
    /// evento para quien quiera reaccionar (el reinicio de la prueba no es responsabilidad de este componente).
    /// Para apagar la línea alcanza con <c>SetActive(false)</c> sobre este GameObject.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    [DisallowMultipleComponent]
    public class LessonBoundary : MonoBehaviour
    {
        /// <summary>Nombre del contenedor de vallas generadas.</summary>
        public const string CONTAINER_NAME = "Barriers";

        // Alto y espesor del sólido si el prefab no tiene BoxCollider (solo se usan para los gizmos).
        private const float k_FallbackSolidHeight = 2.05f;
        private const float k_FallbackSolidThickness = 0.4f;

        [Header("Vallas")]
        [Tooltip("Prefab de valla a repartir (LessonBarrier).")]
        [SerializeField] private GameObject m_BarrierPrefab;

        [Tooltip("Índice de la spline dentro del SplineContainer.")]
        [SerializeField] private int m_SplineIndex = 0;

        [Tooltip("Si está activo, el ancho de cada valla sale del prefab (BoxCollider × escala), así que cambiar la escala del prefab no desarma la línea. Si no, se usa Barrier Length.")]
        [SerializeField] private bool m_UsePrefabWidth = true;

        [Tooltip("Ancho natural de la valla en metros. Solo se usa con Use Prefab Width desactivado.")]
        [SerializeField] private float m_BarrierLength = 3.6f;

        [FormerlySerializedAs("m_Overlap")]
        [Tooltip("Separación entre vallas contiguas en metros: evita que queden pegadas o acopladas.")]
        [SerializeField] private float m_Gap = 0.1f;

        [Tooltip("Ajusta la cantidad de vallas a la distancia y las estira o encoge un poco (todas por igual) para que la separación sea siempre la indicada. Desactivado: no se deforman, y el sobrante agranda la separación.")]
        [SerializeField] private bool m_StretchToFit = true;

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
        public float Gap => m_Gap;
        public bool StretchToFit => m_StretchToFit;
        public float AlertCooldownSeconds => m_AlertCooldownSeconds;

        /// <summary>
        /// Ancho natural de una valla en metros: el del prefab (collider sólido × escala) o, si no se puede leer
        /// o se desactivó <c>Use Prefab Width</c>, el campo <c>Barrier Length</c>.
        /// </summary>
        public float BarrierWidth
        {
            get
            {
                if (m_UsePrefabWidth && m_BarrierPrefab != null)
                {
                    var solid = m_BarrierPrefab.GetComponent<BoxCollider>();
                    if (solid != null && solid.size.x > 0f)
                    {
                        return solid.size.x * Mathf.Abs(m_BarrierPrefab.transform.lossyScale.x);
                    }
                }

                return m_BarrierLength;
            }
        }

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

            InfractionReporting.Report(InfractionReport.Create(
                InfractionIds.LessonBoundary, this, contact.Position,
                ("speed", contact.SpeedKmh.ToString("F0"))));

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
            poses = LessonBoundaryMath.ComputePoses(worldLength, spline.Closed, BarrierWidth, m_Gap, Evaluate, m_StretchToFit);
            return true;
        }

        /// <summary>
        /// Igual que <see cref="TryRebuildBarriers"/>; es el punto de entrada del menú contextual.
        /// </summary>
        [ContextMenu("Reconstruir barreras")]
        public void RebuildBarriers()
        {
            TryRebuildBarriers();
        }

        /// <summary>
        /// Borra y recrea el contenedor <c>Barriers</c> con una instancia del prefab por valla. Las instancias
        /// quedan guardadas en la escena (existen en el build sin regenerarse). Solo en el Editor y fuera de Play.
        /// Devuelve false (sin tocar nada) si no pudo reconstruir: Play Mode, falta el prefab o la spline no es válida.
        /// </summary>
        public bool TryRebuildBarriers()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning("[LessonBoundary] ⚠️ Las barreras se reconstruyen en el Editor, fuera de Play Mode.");
                return false;
            }

            if (m_BarrierPrefab == null)
            {
                Debug.LogWarning($"[LessonBoundary] ⚠️ {name}: falta asignar el prefab de valla (LessonBarrier).");
                return false;
            }

            if (!TryComputePoses(out List<BarrierPose> poses, out float splineLength))
            {
                Debug.LogWarning($"[LessonBoundary] ⚠️ {name}: no hay una spline válida en el índice {m_SplineIndex}.");
                return false;
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

                // El estirado va solo sobre el eje del largo; alto y espesor conservan la escala del prefab.
                Vector3 scale = instance.transform.localScale;
                scale.x *= poses[i].LengthScale;
                instance.transform.localScale = scale;
            }

            Undo.CollapseUndoOperations(undoGroup);

            float gap = poses.Count > 1 ? poses[1].StartDistance - poses[0].EndDistance : 0f;
            Debug.Log($"[LessonBoundary] 🚧 {name}: {poses.Count} vallas sobre {splineLength:F1} m de spline " +
                      $"(ancho {BarrierWidth * (poses.Count > 0 ? poses[0].LengthScale : 1f):F2} m, separación {gap:F2} m).");
            return true;
#else
            Debug.LogWarning("[LessonBoundary] ⚠️ RebuildBarriers solo está disponible en el Editor.");
            return false;
#endif
        }

        private void OnValidate()
        {
            m_BarrierLength = Mathf.Max(0.1f, m_BarrierLength);
            m_Gap = Mathf.Clamp(m_Gap, 0f, 1f);
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
            // Alto y espesor salen del collider sólido y de la escala del prefab.
            float width = BarrierWidth;
            float height = k_FallbackSolidHeight;
            float thickness = k_FallbackSolidThickness;
            if (m_BarrierPrefab != null)
            {
                var solid = m_BarrierPrefab.GetComponent<BoxCollider>();
                if (solid != null)
                {
                    Vector3 prefabScale = m_BarrierPrefab.transform.lossyScale;
                    height = solid.size.y * Mathf.Abs(prefabScale.y);
                    thickness = solid.size.z * Mathf.Abs(prefabScale.z);
                }
            }

            foreach (BarrierPose pose in poses)
            {
                var size = new Vector3(width * pose.LengthScale, height, thickness);
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
