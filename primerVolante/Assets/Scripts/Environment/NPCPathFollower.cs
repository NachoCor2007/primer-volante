using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Qué hace el NPC al llegar al final de la ruta.
    /// </summary>
    public enum NPCPathEndBehavior
    {
        /// <summary>Se detiene exactamente en el final de la ruta.</summary>
        Stop = 0,

        /// <summary>Vuelve al inicio y sigue (solo tiene sentido en splines cerradas).</summary>
        Loop = 1
    }

    /// <summary>
    /// Estado cinemático de un NPC sobre su ruta: distancia recorrida y velocidad.
    /// </summary>
    public struct NPCPathState
    {
        /// <summary>Distancia recorrida sobre la ruta en metros.</summary>
        public float Distance;

        /// <summary>Velocidad actual en m/s (nunca negativa).</summary>
        public float Speed;

        /// <summary>true si llegó al final de una ruta en modo Stop.</summary>
        public bool Finished;
    }

    /// <summary>
    /// Funciones puras (sin estado ni dependencias del motor) del seguimiento de ruta.
    /// Entran estado y parámetros, sale el nuevo estado: son deterministas y testeables en el Editor.
    /// </summary>
    public static class NPCPathMath
    {
        /// <summary>
        /// Velocidad máxima para tomar una curva de radio <paramref name="radius"/> sin superar la aceleración
        /// lateral: sqrt(aLat · R). Un radio infinito (o no positivo) no impone límite.
        /// </summary>
        public static float CurveSpeedLimit(float maxLateralAcceleration, float radius)
        {
            if (radius <= 0f || float.IsInfinity(radius) || float.IsNaN(radius)) return float.PositiveInfinity;
            return Mathf.Sqrt(Mathf.Max(0f, maxLateralAcceleration) * radius);
        }

        /// <summary>
        /// Velocidad máxima que se puede llevar hoy para llegar, a <paramref name="distanceAhead"/> metros,
        /// con velocidad <paramref name="speedAtPoint"/> frenando como mucho con <paramref name="deceleration"/>:
        /// sqrt(v² + 2 · d · s).
        /// </summary>
        public static float ApproachSpeedLimit(float speedAtPoint, float distanceAhead, float deceleration)
        {
            if (float.IsInfinity(speedAtPoint)) return float.PositiveInfinity;
            float s = Mathf.Max(0f, distanceAhead);
            return Mathf.Sqrt(speedAtPoint * speedAtPoint + 2f * Mathf.Max(0f, deceleration) * s);
        }

        /// <summary>
        /// Avanza un paso fijo: la velocidad tiende a <paramref name="targetSpeed"/> respetando la aceleración y el
        /// frenado, y la distancia avanza velocidad · dt. En modo Stop se detiene exactamente en el final;
        /// en modo Loop la distancia da la vuelta.
        /// </summary>
        public static NPCPathState Step(
            NPCPathState state,
            float targetSpeed,
            float acceleration,
            float deceleration,
            float pathLength,
            NPCPathEndBehavior endBehavior,
            float deltaTime)
        {
            if (state.Finished || pathLength <= 0f || deltaTime <= 0f) return state;

            float target = Mathf.Max(0f, targetSpeed);
            float rate = target > state.Speed ? acceleration : deceleration;
            float speed = Mathf.MoveTowards(state.Speed, target, Mathf.Max(0f, rate) * deltaTime);
            float distance = state.Distance + speed * deltaTime;

            if (endBehavior == NPCPathEndBehavior.Loop)
            {
                distance -= Mathf.Floor(distance / pathLength) * pathLength;
                return new NPCPathState { Distance = distance, Speed = speed, Finished = false };
            }

            if (distance >= pathLength)
            {
                return new NPCPathState { Distance = pathLength, Speed = 0f, Finished = true };
            }

            return new NPCPathState { Distance = distance, Speed = speed, Finished = false };
        }
    }

    /// <summary>
    /// Mueve un NPC (vehículo o peatón) a lo largo de una spline de forma determinista:
    /// Rigidbody cinemático movido con MovePosition/MoveRotation en FixedUpdate usando solo
    /// <see cref="Time.fixedDeltaTime"/>. Frena antes de las curvas y al final de la ruta.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NPCPathFollower : MonoBehaviour
    {
        // Paso del muestreo de curvatura hacia adelante (m).
        private const float k_LookaheadStep = 1.5f;

        // Distancia extra de anticipación sobre la de frenado desde la velocidad crucero (m).
        private const float k_LookaheadMargin = 3f;

        [Header("Ruta")]
        [Tooltip("Contenedor con la spline a seguir. Debe tener escala uniforme.")]
        [SerializeField] private SplineContainer m_Path;

        [Tooltip("Índice de la spline dentro del contenedor.")]
        [SerializeField] private int m_SplineIndex = 0;

        [Tooltip("Distancia inicial sobre la ruta en metros.")]
        [SerializeField] private float m_InitialDistance = 0f;

        [Tooltip("Qué hace al llegar al final. Loop solo aplica a splines cerradas.")]
        [SerializeField] private NPCPathEndBehavior m_EndBehavior = NPCPathEndBehavior.Stop;

        [Tooltip("Si se activa, empieza a moverse al iniciar la escena.")]
        [SerializeField] private bool m_PlayOnStart = true;

        [Header("Movimiento")]
        [Tooltip("Velocidad crucero en m/s.")]
        [SerializeField] private float m_CruiseSpeed = 8f;

        [Tooltip("Aceleración máxima en m/s^2.")]
        [SerializeField] private float m_Acceleration = 2.5f;

        [Tooltip("Desaceleración máxima (frenado) en m/s^2.")]
        [SerializeField] private float m_Deceleration = 4f;

        [Tooltip("Aceleración lateral máxima en m/s^2: limita la velocidad en las curvas (v = sqrt(aLat · R)).")]
        [SerializeField] private float m_MaxLateralAcceleration = 3f;

        private Rigidbody m_Rigidbody;
        private Spline m_Spline;
        private NPCPathState m_State;
        private NPCPathEndBehavior m_EffectiveEndBehavior;
        private float m_PathLength;
        private float m_WorldPerLocalLength = 1f;
        private float m_CurrentAcceleration;
        private bool m_IsPlaying;
        private bool m_StopRequested;
        private bool m_ReportedEnd;

        /// <summary>Se dispara una vez al llegar al final de la ruta (solo modo Stop).</summary>
        public event Action OnReachedEnd;

        /// <summary>Velocidad actual en m/s.</summary>
        public float CurrentSpeed => m_State.Speed;

        /// <summary>Aceleración longitudinal del último paso en m/s^2 (negativa al frenar).</summary>
        public float CurrentAcceleration => m_CurrentAcceleration;

        /// <summary>Distancia recorrida sobre la ruta en metros.</summary>
        public float DistanceTravelled => m_State.Distance;

        /// <summary>Largo de la ruta en metros (espacio de mundo).</summary>
        public float PathLength => m_PathLength;

        /// <summary>true mientras el NPC tiene velocidad.</summary>
        public bool IsMoving => m_State.Speed > 0.01f;

        /// <summary>Velocidad crucero configurada en m/s.</summary>
        public float CruiseSpeed => m_CruiseSpeed;

        /// <summary>Contenedor de la ruta asignada (null si no tiene).</summary>
        public SplineContainer Path => m_Path;

        /// <summary>Indica si hay una ruta válida asignada.</summary>
        public bool HasPath => m_Spline != null && m_PathLength > 0f;

        private void Reset()
        {
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            RefreshPath();
        }

        private void Start()
        {
            if (m_PlayOnStart) Play();
        }

        private void OnValidate()
        {
            m_CruiseSpeed = Mathf.Max(0f, m_CruiseSpeed);
            m_Acceleration = Mathf.Max(0.01f, m_Acceleration);
            m_Deceleration = Mathf.Max(0.01f, m_Deceleration);
            m_MaxLateralAcceleration = Mathf.Max(0.01f, m_MaxLateralAcceleration);
            m_InitialDistance = Mathf.Max(0f, m_InitialDistance);

#if UNITY_EDITOR
            // Coloca el objeto en el punto inicial de la ruta para verlo en el Editor.
            if (!Application.isPlaying && m_Path != null && m_Path.Splines != null &&
                m_SplineIndex >= 0 && m_SplineIndex < m_Path.Splines.Count)
            {
                RefreshPath();
                if (EvaluatePose(m_InitialDistance, out Vector3 position, out Quaternion rotation))
                {
                    transform.SetPositionAndRotation(position, rotation);
                }
            }
#endif
        }

        /// <summary>
        /// Asigna la ruta y vuelve a colocar al NPC en la distancia inicial.
        /// </summary>
        public void SetPath(SplineContainer path, int splineIndex)
        {
            m_Path = path;
            m_SplineIndex = splineIndex;
            RefreshPath();
            TeleportToDistance(m_InitialDistance);
        }

        /// <summary>
        /// Empieza (o reanuda) el movimiento. Si había terminado la ruta, vuelve al inicio.
        /// </summary>
        public void Play()
        {
            if (!HasPath)
            {
                Debug.LogWarning($"[NPCPathFollower] ⚠️ {name} no tiene ruta asignada: no se mueve.");
                return;
            }

            if (m_State.Finished) TeleportToDistance(0f);

            m_StopRequested = false;
            m_IsPlaying = true;
        }

        /// <summary>
        /// Frena con la desaceleración configurada hasta detenerse. <see cref="Play"/> reanuda.
        /// </summary>
        public void Stop()
        {
            m_StopRequested = true;
        }

        /// <summary>
        /// Cambia la velocidad crucero en m/s.
        /// </summary>
        public void SetCruiseSpeed(float cruiseSpeed)
        {
            m_CruiseSpeed = Mathf.Max(0f, cruiseSpeed);
        }

        /// <summary>
        /// Coloca al NPC de inmediato en la distancia indicada, con velocidad cero.
        /// </summary>
        public void TeleportToDistance(float distance)
        {
            if (m_Rigidbody == null) m_Rigidbody = GetComponent<Rigidbody>();
            if (m_Spline == null) RefreshPath();

            float clamped = m_EffectiveEndBehavior == NPCPathEndBehavior.Loop && m_PathLength > 0f
                ? Mathf.Repeat(distance, m_PathLength)
                : Mathf.Clamp(distance, 0f, m_PathLength);

            m_State = new NPCPathState { Distance = clamped, Speed = 0f, Finished = false };
            m_CurrentAcceleration = 0f;
            m_ReportedEnd = false;

            if (EvaluatePose(clamped, out Vector3 position, out Quaternion rotation))
            {
                transform.SetPositionAndRotation(position, rotation);
                if (m_Rigidbody != null)
                {
                    m_Rigidbody.position = position;
                    m_Rigidbody.rotation = rotation;
                }
            }
        }

        /// <summary>
        /// Posición y orientación (yaw según la tangente proyectada en XZ) en la distancia indicada,
        /// en espacio de mundo. La altura sale de la spline.
        /// </summary>
        public bool EvaluatePose(float distance, out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;
            if (!HasPath) return false;

            float t = DistanceToNormalized(distance);
            if (!m_Path.Evaluate(m_SplineIndex, t, out float3 pos, out float3 tangent, out float3 _))
            {
                return false;
            }

            position = pos;
            Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }

            return true;
        }

        private void FixedUpdate()
        {
            if (!m_IsPlaying || !HasPath) return;

            float dt = Time.fixedDeltaTime;
            float previousSpeed = m_State.Speed;

            float target = m_StopRequested ? 0f : ComputeTargetSpeed();
            m_State = NPCPathMath.Step(m_State, target, m_Acceleration, m_Deceleration, m_PathLength, m_EffectiveEndBehavior, dt);
            m_CurrentAcceleration = (m_State.Speed - previousSpeed) / dt;

            if (EvaluatePose(m_State.Distance, out Vector3 position, out Quaternion rotation))
            {
                m_Rigidbody.MovePosition(position);
                m_Rigidbody.MoveRotation(rotation);
            }

            if (m_State.Finished && !m_ReportedEnd)
            {
                m_ReportedEnd = true;
                m_IsPlaying = false;
                OnReachedEnd?.Invoke();
            }
            else if (m_StopRequested && m_State.Speed <= 0f)
            {
                m_IsPlaying = false;
            }
        }

        /// <summary>
        /// Mínimo entre la velocidad crucero, las de las curvas por delante (con el frenado necesario para
        /// llegar a ellas) y la de frenado para detenerse al final de la ruta.
        /// </summary>
        private float ComputeTargetSpeed()
        {
            float target = m_CruiseSpeed;
            bool loop = m_EffectiveEndBehavior == NPCPathEndBehavior.Loop;

            float window = (m_CruiseSpeed * m_CruiseSpeed) / (2f * m_Deceleration) + k_LookaheadMargin;
            for (float ahead = 0f; ahead <= window; ahead += k_LookaheadStep)
            {
                float distance = m_State.Distance + ahead;
                if (!loop && distance > m_PathLength) break;

                float radius = RadiusAtDistance(loop ? Mathf.Repeat(distance, m_PathLength) : distance);
                float curveSpeed = NPCPathMath.CurveSpeedLimit(m_MaxLateralAcceleration, radius);
                target = Mathf.Min(target, NPCPathMath.ApproachSpeedLimit(curveSpeed, ahead, m_Deceleration));
            }

            if (!loop)
            {
                float remaining = m_PathLength - m_State.Distance;
                target = Mathf.Min(target, NPCPathMath.ApproachSpeedLimit(0f, remaining, m_Deceleration));
            }

            return target;
        }

        /// <summary>
        /// Radio de curvatura (m, espacio de mundo) de la ruta en la distancia indicada.
        /// </summary>
        private float RadiusAtDistance(float distance)
        {
            float t = DistanceToNormalized(distance);
            float curvatureLocal = SplineUtility.EvaluateCurvature(m_Spline, t);
            float curvatureWorld = curvatureLocal / m_WorldPerLocalLength;
            return curvatureWorld > 1e-5f ? 1f / curvatureWorld : float.PositiveInfinity;
        }

        /// <summary>
        /// Convierte distancia de mundo a parámetro normalizado de la spline (distancia ≠ t).
        /// </summary>
        private float DistanceToNormalized(float worldDistance)
        {
            float localDistance = worldDistance / m_WorldPerLocalLength;
            return m_Spline.ConvertIndexUnit(localDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
        }

        /// <summary>
        /// Recalcula y cachea la spline, su largo en mundo y la relación mundo/local.
        /// </summary>
        public void RefreshPath()
        {
            m_Spline = null;
            m_PathLength = 0f;
            m_WorldPerLocalLength = 1f;
            m_EffectiveEndBehavior = m_EndBehavior;

            if (m_Path == null || m_Path.Splines == null ||
                m_SplineIndex < 0 || m_SplineIndex >= m_Path.Splines.Count)
            {
                return;
            }

            Spline spline = m_Path.Splines[m_SplineIndex];
            float localLength = spline.GetLength();
            float worldLength = m_Path.CalculateLength(m_SplineIndex);
            if (localLength <= 0f || worldLength <= 0f) return;

            m_Spline = spline;
            m_PathLength = worldLength;
            m_WorldPerLocalLength = worldLength / localLength;

            if (m_EndBehavior == NPCPathEndBehavior.Loop && !spline.Closed)
            {
                m_EffectiveEndBehavior = NPCPathEndBehavior.Stop;
                if (Application.isPlaying)
                {
                    Debug.LogWarning($"[NPCPathFollower] ⚠️ {name}: Loop requiere una spline cerrada; se usa Stop.");
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (m_Path == null || m_Path.Splines == null ||
                m_SplineIndex < 0 || m_SplineIndex >= m_Path.Splines.Count)
            {
                return;
            }

            const int segments = 96;
            Gizmos.color = Color.cyan;
            Vector3 previous = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = m_Path.EvaluatePosition(m_SplineIndex, t);
                if (i > 0) Gizmos.DrawLine(previous, point);
                previous = point;
            }

            float startT = m_Spline != null ? DistanceToNormalized(m_InitialDistance) : 0f;
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(m_Path.EvaluatePosition(m_SplineIndex, startT), 0.4f);
        }
    }
}
