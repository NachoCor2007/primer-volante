using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PrimerVolante.UI
{
    /// <summary>
    /// Componente encargado de renderizar y consumir dinámicamente la ruta de navegación (tipo Waze)
    /// sobre una capa exclusiva (GPSRoute) visible únicamente para la cámara cenital del GPS.
    /// Lee la ruta de un SplineContainer, va consumiendo el camino recorrido y detecta la llegada al destino (< 4m).
    /// </summary>
    public class GPSRouteRenderer : MonoBehaviour
    {
        [Header("Configuración de Ruta")]
        [Tooltip("Contenedor de spline con el trazado del circuito urbano.")]
        [SerializeField] private SplineContainer m_SplineContainer;

        [Tooltip("Índice de la spline dentro del contenedor (por defecto 0).")]
        [SerializeField] private int m_SplineIndex = 0;

        [Tooltip("Transform del vehículo. Si es null, se detecta automáticamente.")]
        [SerializeField] private Transform m_VehicleTransform;

        [Tooltip("Paso de muestreo en metros a lo largo de la spline para generar la polilínea.")]
        [SerializeField] private float m_SampleStep = 1.0f;

        [Tooltip("Elevación en Y sobre el asfalto para evitar Z-fighting.")]
        [SerializeField] private float m_RouteHeight = 0.20f;

        [Tooltip("Distancia en metros al punto final para considerar arribo a destino.")]
        [SerializeField] private float m_ArrivalThreshold = 4.0f;

        [Tooltip("Nombre de la capa exclusiva para el trazado de la ruta del GPS.")]
        [SerializeField] private string m_RouteLayerName = "GPSRoute";

        [Header("Componentes")]
        [Tooltip("LineRenderer del trazado (normalmente en el hijo GPS_Route_Line). Si se deja vacío, se busca en los hijos.")]
        [SerializeField] private LineRenderer m_LineRenderer;

        // Estado interno
        private readonly List<Vector3> m_SampledPoints = new List<Vector3>();
        private readonly List<float> m_CumulativeLengths = new List<float>();
        private int m_CurrentSegmentIndex = 0;
        private float m_TotalRouteLength = 0f;
        private float m_RemainingDistance = 0f;
        private bool m_HasArrived = false;
        private string m_CurrentManeuver = "Iniciando navegación...";
        private Vector3 m_LastCarPosition;

        /// <summary>Evento disparado cuando el vehículo arriba al destino (< 4m).</summary>
        public event Action OnDestinationReached;

        public bool HasArrived => m_HasArrived;
        public float RemainingDistance => m_RemainingDistance;
        public float TotalRouteLength => m_TotalRouteLength;
        public string CurrentManeuver => m_CurrentManeuver;
        public LineRenderer RouteLineRenderer => m_LineRenderer;

        public SplineContainer RouteContainer
        {
            get => m_SplineContainer;
            set
            {
                m_SplineContainer = value;
                InitializeRoute();
            }
        }

        public Transform VehicleTransform
        {
            get => m_VehicleTransform;
            set => m_VehicleTransform = value;
        }

        private void Awake()
        {
            if (m_LineRenderer == null)
            {
                m_LineRenderer = GetComponentInChildren<LineRenderer>(true);
            }

            ConfigureLineRendererLayer();
        }

        private void Start()
        {
            ResolveVehicle();
            InitializeRoute();
        }

        /// <summary>
        /// Asegura que el LineRenderer se encuentre en la capa dedicada (GPSRoute).
        /// </summary>
        public void ConfigureLineRendererLayer()
        {
            if (m_LineRenderer == null) return;

            int layer = LayerMask.NameToLayer(m_RouteLayerName);
            if (layer != -1)
            {
                m_LineRenderer.gameObject.layer = layer;
            }
            m_LineRenderer.useWorldSpace = true;
        }

        private void ResolveVehicle()
        {
            if (m_VehicleTransform != null) return;

            // Buscar en ancestros o escena mediante FindAnyObjectByType (cumplimiento estricto de AGENTS.md)
            Transform t = transform;
            while (t != null)
            {
                if (t.name.StartsWith("Car") || t.GetComponent<Rigidbody>() != null)
                {
                    m_VehicleTransform = t;
                    break;
                }
                t = t.parent;
            }

            if (m_VehicleTransform == null)
            {
                var vehicleController = UnityEngine.Object.FindAnyObjectByType<PrimerVolante.VR.VehicleController>();
                if (vehicleController != null)
                {
                    m_VehicleTransform = vehicleController.transform;
                }
            }
        }

        /// <summary>
        /// Muestrea la spline y precalcula los puntos y distancias acumuladas.
        /// </summary>
        public void InitializeRoute()
        {
            if (m_LineRenderer == null)
            {
                m_LineRenderer = GetComponentInChildren<LineRenderer>(true);
            }

            m_SampledPoints.Clear();
            m_CumulativeLengths.Clear();
            m_CurrentSegmentIndex = 0;
            m_HasArrived = false;
            m_RemainingDistance = 0f;
            m_TotalRouteLength = 0f;

            if (m_SplineContainer == null || m_SplineContainer.Splines == null || m_SplineIndex >= m_SplineContainer.Splines.Count)
            {
                if (m_LineRenderer != null)
                {
                    m_LineRenderer.positionCount = 0;
                }
                m_CurrentManeuver = "Sin ruta asignada";
                return;
            }

            Spline spline = m_SplineContainer.Splines[m_SplineIndex];
            if (spline == null || spline.Count < 2)
            {
                if (m_LineRenderer != null)
                {
                    m_LineRenderer.positionCount = 0;
                }
                m_CurrentManeuver = "Ruta insuficiente";
                return;
            }

            float worldLength = m_SplineContainer.CalculateLength(m_SplineIndex);
            m_TotalRouteLength = worldLength;

            if (worldLength <= 0.1f)
            {
                return;
            }

            int sampleCount = Mathf.Max(10, Mathf.CeilToInt(worldLength / Mathf.Max(0.2f, m_SampleStep)));
            float cumulative = 0f;
            Vector3 previousPoint = Vector3.zero;

            for (int i = 0; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float3 evalPos = m_SplineContainer.EvaluatePosition(m_SplineIndex, t);
                Vector3 worldPt = new Vector3(evalPos.x, m_RouteHeight, evalPos.z);

                if (i == 0)
                {
                    cumulative = 0f;
                }
                else
                {
                    cumulative += Vector2.Distance(new Vector2(previousPoint.x, previousPoint.z), new Vector2(worldPt.x, worldPt.z));
                }

                m_SampledPoints.Add(worldPt);
                m_CumulativeLengths.Add(cumulative);
                previousPoint = worldPt;
            }

            m_TotalRouteLength = cumulative;
            m_RemainingDistance = cumulative;
            m_CurrentManeuver = "Continúe por la avenida";

            // Inicializar visualización del LineRenderer
            if (m_LineRenderer != null)
            {
                m_LineRenderer.positionCount = m_SampledPoints.Count;
                for (int i = 0; i < m_SampledPoints.Count; i++)
                {
                    m_LineRenderer.SetPosition(i, m_SampledPoints[i]);
                }
                m_LineRenderer.enabled = true;
            }
        }

        /// <summary>
        /// Actualiza la posición del auto, consume el camino recorrido y calcula la distancia y próxima maniobra.
        /// </summary>
        public void UpdateRoute(Vector3 carPosition)
        {
            if (m_SampledPoints.Count < 2 || m_HasArrived)
            {
                return;
            }

            m_LastCarPosition = carPosition;

            // Consumo dinámico de ruta (Waze style):
            // Avanzamos el segmento actual proyectando la posición del auto
            while (m_CurrentSegmentIndex < m_SampledPoints.Count - 2)
            {
                Vector3 segP0 = m_SampledPoints[m_CurrentSegmentIndex];
                Vector3 segP1 = m_SampledPoints[m_CurrentSegmentIndex + 1];

                Vector2 segVec = new Vector2(segP1.x - segP0.x, segP1.z - segP0.z);
                Vector2 carVec = new Vector2(carPosition.x - segP0.x, carPosition.z - segP0.z);

                float lenSq = segVec.sqrMagnitude;
                if (lenSq < 1e-4f)
                {
                    m_CurrentSegmentIndex++;
                    continue;
                }

                float projection = Vector2.Dot(carVec, segVec) / lenSq;
                if (projection >= 1.0f)
                {
                    m_CurrentSegmentIndex++;
                }
                else
                {
                    break;
                }
            }

            Vector3 destPoint = m_SampledPoints[m_SampledPoints.Count - 1];
            float distToEnd = Vector2.Distance(new Vector2(carPosition.x, carPosition.z), new Vector2(destPoint.x, destPoint.z));

            // Detección de llegada a destino (< 4m y en el tramo final de la ruta)
            if (distToEnd <= m_ArrivalThreshold && m_CurrentSegmentIndex >= m_SampledPoints.Count - 8)
            {
                m_HasArrived = true;
                m_RemainingDistance = 0f;
                m_CurrentManeuver = "¡Llegaste a tu destino!";

                if (m_LineRenderer != null)
                {
                    m_LineRenderer.positionCount = 0;
                    m_LineRenderer.enabled = false;
                }

                OnDestinationReached?.Invoke();
                return;
            }

            // A. Proyección Ortogonal sobre el Carril (Cross-Track Projection)
            Vector3 p0 = m_SampledPoints[m_CurrentSegmentIndex];
            Vector3 p1 = m_SampledPoints[m_CurrentSegmentIndex + 1];
            Vector2 seg = new Vector2(p1.x - p0.x, p1.z - p0.z);
            Vector2 toCar = new Vector2(carPosition.x - p0.x, carPosition.z - p0.z);
            float segLenSq = seg.sqrMagnitude;
            float currentProjection = 0f;
            if (segLenSq > 1e-4f)
            {
                currentProjection = Mathf.Clamp01(Vector2.Dot(toCar, seg) / segLenSq);
            }
            Vector3 projectedPoint = Vector3.Lerp(p0, p1, currentProjection);

            // Cálculo de Distancia Restante Estable:
            float segLen = Vector2.Distance(new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z));
            float remainingInSeg = (1.0f - currentProjection) * segLen;
            float remainingAfterSeg = m_CumulativeLengths[m_SampledPoints.Count - 1] - m_CumulativeLengths[m_CurrentSegmentIndex + 1];
            m_RemainingDistance = Mathf.Max(0f, remainingInSeg + remainingAfterSeg);

            // B. Renderizado Limpio de la Ruta (Sin ganchos ni quiebres de 90° hacia el auto)
            if (m_LineRenderer != null)
            {
                int remainingPointsCount = m_SampledPoints.Count - (m_CurrentSegmentIndex + 1);
                m_LineRenderer.positionCount = 1 + remainingPointsCount;

                // El primer punto de la línea se fija sobre la proyección del auto en el carril
                Vector3 routeStartPoint = new Vector3(projectedPoint.x, m_RouteHeight, projectedPoint.z);
                m_LineRenderer.SetPosition(0, routeStartPoint);

                for (int i = 0; i < remainingPointsCount; i++)
                {
                    m_LineRenderer.SetPosition(1 + i, m_SampledPoints[m_CurrentSegmentIndex + 1 + i]);
                }
                m_LineRenderer.enabled = true;
            }

            // C. Detección de Maniobras Basada en la Tangente de la Calle
            UpdateManeuverInstruction(carPosition, p0, p1, currentProjection, segLen);
        }

        private void UpdateManeuverInstruction(Vector3 carPosition, Vector3 p0, Vector3 p1, float currentProjection, float segLen)
        {
            if (m_RemainingDistance <= 15f)
            {
                m_CurrentManeuver = "Avanzando hacia el destino";
                return;
            }

            // Dirección de referencia usando la tangente del carril/calle (NO el vector auto -> spline)
            Vector3 roadDir = (p1 - p0).normalized;
            roadDir.y = 0f;
            if (roadDir.sqrMagnitude < 1e-4f && m_VehicleTransform != null)
            {
                roadDir = m_VehicleTransform.forward;
                roadDir.y = 0f;
            }

            bool foundTurn = false;
            float currentTraveledInSeg = currentProjection * segLen;

            for (int k = m_CurrentSegmentIndex + 1; k < Mathf.Min(m_SampledPoints.Count - 1, m_CurrentSegmentIndex + 60); k++)
            {
                float distToSegment = Mathf.Max(0f, m_CumulativeLengths[k] - (m_CumulativeLengths[m_CurrentSegmentIndex] + currentTraveledInSeg));
                if (distToSegment < 4.0f) continue; // Ignorar el segmento inmediato que ya define roadDir

                Vector3 segDir = (m_SampledPoints[k + 1] - m_SampledPoints[k]).normalized;
                segDir.y = 0f;

                float turnAngle = Vector3.SignedAngle(roadDir, segDir, Vector3.up);
                if (Mathf.Abs(turnAngle) > 28.0f)
                {
                    string turnDirection = turnAngle > 0f ? "a la derecha" : "a la izquierda";
                    if (distToSegment > 20f)
                    {
                        m_CurrentManeuver = $"Gire {turnDirection} en {distToSegment:F0} m";
                    }
                    else
                    {
                        m_CurrentManeuver = $"Gire {turnDirection}";
                    }
                    foundTurn = true;
                    break;
                }
            }

            if (!foundTurn)
            {
                m_CurrentManeuver = "Continúe recto";
            }
        }
    }
}
