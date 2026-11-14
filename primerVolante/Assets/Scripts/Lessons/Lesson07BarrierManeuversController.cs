using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Controlador integral de la Lección 7: Dominio del Volante y Maniobras con Vallas (Slalom y Reversa con Espejos).
    /// Supervisa:
    /// 1. Slalom hacia adelante en zig-zag esquivando vallas alternadas hasta la dársena de meta frontal.
    /// 2. Maniobra de slalom en reversa utilizando la regla del "medio auto" en los espejos retrovisores laterales.
    /// 3. Detención y aseguramiento del vehículo (P y freno de mano).
    /// Cumple estrictamente con la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class Lesson07BarrierManeuversController : MonoBehaviour
    {
        private static Lesson07BarrierManeuversController s_Instance;

        [Header("Subsistemas de Lección")]
        [SerializeField] private LessonManager m_LessonManager;
        [SerializeField] private VehicleController m_VehicleController;
        [SerializeField] private LessonHUDPanel m_HUDPanel;
        [SerializeField] private VRScreenFader m_ScreenFader;

        [Header("Dársenas de Maniobra")]
        [Tooltip("Trigger de detección para la dársena de inicio / retorno en reversa.")]
        [SerializeField] private ParkingZoneTrigger m_StartZone;

        [Tooltip("Trigger de detección para la dársena final del slalom hacia adelante.")]
        [SerializeField] private ParkingZoneTrigger m_ForwardFinishZone;

        [Header("Demarcación Visual en Asfalto (Recuadros Amarillos)")]
        [SerializeField] private GameObject m_StartZoneHighlight;
        [SerializeField] private GameObject m_ForwardZoneHighlight;

        [Header("Vallas de Slalom")]
        [SerializeField] private List<LessonBarrier> m_SlalomBarriers = new List<LessonBarrier>();

        [Header("Estadísticas de Lección")]
        [SerializeField] private int m_BarrierCollisions = 0;

        public static Lesson07BarrierManeuversController Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = UnityEngine.Object.FindAnyObjectByType<Lesson07BarrierManeuversController>();
                    if (s_Instance == null)
                    {
                        GameObject go = new GameObject("Lesson07_Controller");
                        s_Instance = go.AddComponent<Lesson07BarrierManeuversController>();
                    }
                }
                return s_Instance;
            }
        }

        public LessonManager LessonManager => m_LessonManager;
        public VehicleController Vehicle => m_VehicleController;
        public ParkingZoneTrigger StartZone => m_StartZone;
        public ParkingZoneTrigger ForwardFinishZone => m_ForwardFinishZone;
        public int BarrierCollisions => m_BarrierCollisions;

        /// <summary>
        /// Valida si el vehículo completó el slalom hacia adelante y quedó detenido dentro de la dársena frontal.
        /// </summary>
        public bool IsForwardSlalomComplete
        {
            get
            {
                if (m_ForwardFinishZone == null || m_VehicleController == null)
                {
                    AutoDiscoverReferences();
                    if (m_ForwardFinishZone == null || m_VehicleController == null) return false;
                }

                if (m_ForwardFinishZone.IsVehicleParkedProperly)
                {
                    return true;
                }

                bool isInside = m_ForwardFinishZone.IsVehicleInside;
                bool isStopped = m_VehicleController.CurrentSpeedKmh <= 1.5f;
                return isInside && isStopped;
            }
        }

        /// <summary>
        /// Valida si el vehículo completó el slalom en reversa y quedó detenido dentro de la dársena de inicio.
        /// </summary>
        public bool IsReverseSlalomComplete
        {
            get
            {
                if (m_StartZone == null || m_VehicleController == null)
                {
                    AutoDiscoverReferences();
                    if (m_StartZone == null || m_VehicleController == null) return false;
                }

                if (m_StartZone.IsVehicleParkedProperly)
                {
                    return true;
                }

                bool isInside = m_StartZone.IsVehicleInside;
                bool isStopped = m_VehicleController.CurrentSpeedKmh <= 1.5f;
                return isInside && isStopped;
            }
        }

        private void Awake()
        {
            s_Instance = this;
            EnsureBarriersAndZones();
            AutoDiscoverReferences();
            UpdateHighlightsForStep(0);
        }

        private void OnEnable()
        {
            s_Instance = this;
            EnsureBarriersAndZones();
            AutoDiscoverReferences();
            SubscribeToBarriers();
            UpdateHighlightsForStep(0);

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= HandleStepChanged;
                m_LessonManager.StepChanged += HandleStepChanged;
            }
        }

        /// <summary>
        /// Asegura que las vallas y las dársenas amarilla existan en la escena tanto en Editor como en Runtime.
        /// </summary>
        public void EnsureBarriersAndZones()
        {
            // 1. Dársena de Inicio / Retorno (Z ≈ 14.3, X = 63.5)
            if (m_StartZone == null)
            {
                GameObject szObj = GameObject.Find("ParkingZone_Start");
                if (szObj == null)
                {
                    szObj = new GameObject("ParkingZone_Start");
                    szObj.transform.position = new Vector3(63.5f, 0.05f, 14.3f);
                    szObj.transform.rotation = Quaternion.identity;
                    BoxCollider col = szObj.AddComponent<BoxCollider>();
                    col.isTrigger = true;
                    col.center = Vector3.zero;
                    col.size = new Vector3(4.2f, 2.0f, 6.5f);
                    m_StartZone = szObj.AddComponent<ParkingZoneTrigger>();
                    m_StartZone.StationType = ParkingStationType.ParallelRight;

                    m_StartZoneHighlight = CreateGroundVisual(szObj.transform, "GroundVisual_StartYellow", new Vector3(4.0f, 6.0f, 1f));
                }
                else
                {
                    szObj.transform.position = new Vector3(63.5f, 0.05f, 14.3f);
                    m_StartZone = szObj.GetComponent<ParkingZoneTrigger>();
                    Transform gv = szObj.transform.Find("GroundVisual_StartYellow");
                    if (gv != null) m_StartZoneHighlight = gv.gameObject;
                }
            }
            else
            {
                m_StartZone.transform.position = new Vector3(63.5f, 0.05f, 14.3f);
            }

            // 2. Dársena de Llegada hacia Adelante (Z ≈ 84.0, X = 60.0)
            if (m_ForwardFinishZone == null)
            {
                GameObject fzObj = GameObject.Find("ParkingZone_ForwardFinish");
                if (fzObj == null)
                {
                    fzObj = new GameObject("ParkingZone_ForwardFinish");
                    fzObj.transform.position = new Vector3(60.0f, 0.05f, 84.0f);
                    fzObj.transform.rotation = Quaternion.identity;
                    BoxCollider col = fzObj.AddComponent<BoxCollider>();
                    col.isTrigger = true;
                    col.center = Vector3.zero;
                    col.size = new Vector3(5.0f, 2.0f, 7.0f);
                    m_ForwardFinishZone = fzObj.AddComponent<ParkingZoneTrigger>();
                    m_ForwardFinishZone.StationType = ParkingStationType.ParallelRight;

                    m_ForwardZoneHighlight = CreateGroundVisual(fzObj.transform, "GroundVisual_ForwardYellow", new Vector3(4.5f, 6.5f, 1f));
                }
                else
                {
                    fzObj.transform.position = new Vector3(60.0f, 0.05f, 84.0f);
                    m_ForwardFinishZone = fzObj.GetComponent<ParkingZoneTrigger>();
                    Transform gv = fzObj.transform.Find("GroundVisual_ForwardYellow");
                    if (gv != null) m_ForwardZoneHighlight = gv.gameObject;
                }
            }
            else
            {
                m_ForwardFinishZone.transform.position = new Vector3(60.0f, 0.05f, 84.0f);
            }

            // 3. Vallas de Slalom en zig-zag alternado
            var slalomSpecs = new (string name, Vector3 pos, float yaw, Vector3 coneOffset)[]
            {
                ("Slalom_Barrier_01_Left", new Vector3(56.5f, 0f, 28.0f), 0f, new Vector3(2.0f, 0f, 0f)),
                ("Slalom_Barrier_02_Right", new Vector3(63.5f, 0f, 42.0f), 0f, new Vector3(-2.0f, 0f, 0f)),
                ("Slalom_Barrier_03_Left", new Vector3(56.5f, 0f, 56.0f), 0f, new Vector3(2.0f, 0f, 0f)),
                ("Slalom_Barrier_04_Right", new Vector3(63.5f, 0f, 70.0f), 0f, new Vector3(-2.0f, 0f, 0f))
            };

            GameObject root = GameObject.Find("Slalom_Barriers");
            if (root == null)
            {
                root = new GameObject("Slalom_Barriers");
                GameObject barrierPrefab = null;
                GameObject conePrefab = null;
#if UNITY_EDITOR
                barrierPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/LessonBarrier.prefab");
                conePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab");
#endif
                m_SlalomBarriers = new List<LessonBarrier>();
                foreach (var spec in slalomSpecs)
                {
                    GameObject bObj = null;
                    if (barrierPrefab != null)
                    {
                        bObj = Instantiate(barrierPrefab, root.transform);
                    }
                    else
                    {
                        bObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        bObj.transform.localScale = new Vector3(3.6f, 1f, 0.4f);
                        bObj.transform.SetParent(root.transform);
                    }

                    bObj.name = spec.name;
                    bObj.transform.position = spec.pos;
                    bObj.transform.rotation = Quaternion.Euler(0f, spec.yaw, 0f);

                    if (conePrefab != null)
                    {
                        GameObject cone = Instantiate(conePrefab, bObj.transform);
                        cone.name = "Cone_Tip";
                        cone.transform.localPosition = spec.coneOffset;
                        cone.transform.localRotation = Quaternion.identity;
                    }

                    LessonBarrier lb = bObj.GetComponentInChildren<LessonBarrier>(true);
                    if (lb != null) m_SlalomBarriers.Add(lb);
                }
            }
            else
            {
                // Reposicionar vallas existentes para garantizar las coordenadas correctas sobre el asfalto
                foreach (var spec in slalomSpecs)
                {
                    Transform t = root.transform.Find(spec.name);
                    if (t != null)
                    {
                        t.position = spec.pos;
                        t.rotation = Quaternion.Euler(0f, spec.yaw, 0f);
                    }
                }
                m_SlalomBarriers = new List<LessonBarrier>(root.GetComponentsInChildren<LessonBarrier>(true));
            }
        }

        private GameObject CreateGroundVisual(Transform parent, string name, Vector3 scale)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = scale;

            Collider c = quad.GetComponent<Collider>();
            if (c != null) Destroy(c);

#if UNITY_EDITOR
            Material yellowMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Materials/M_Parking_YellowZone.mat");
            if (yellowMat != null)
            {
                MeshRenderer mr = quad.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = yellowMat;
            }
#endif
            return quad;
        }

        private void OnDisable()
        {
            UnsubscribeFromBarriers();

            if (m_LessonManager != null)
            {
                m_LessonManager.StepChanged -= HandleStepChanged;
            }
        }

        private void HandleStepChanged(LessonStepSO step, int stepIndex, int totalSteps)
        {
            UpdateHighlightsForStep(stepIndex);
        }

        /// <summary>
        /// Actualiza la visibilidad de los recuadros amarillos en el piso según el paso actual.
        /// </summary>
        public void UpdateHighlightsForStep(int stepIndex)
        {
            // Pasos 0 a 3 (hacia adelante: volante, motor, drive, slalom adelante): Resaltar dársena frontal objetivo
            // Pasos 4 a 6 (en reversa: poner marcha atrás, slalom reversa, asegurar auto): Resaltar dársena de retorno inicial
            if (m_ForwardZoneHighlight != null)
            {
                m_ForwardZoneHighlight.SetActive(stepIndex <= 3);
            }
            if (m_StartZoneHighlight != null)
            {
                m_StartZoneHighlight.SetActive(stepIndex >= 4);
            }
        }

        private void SubscribeToBarriers()
        {
            if (m_SlalomBarriers == null || m_SlalomBarriers.Count == 0)
            {
                m_SlalomBarriers = new List<LessonBarrier>(UnityEngine.Object.FindObjectsByType<LessonBarrier>(FindObjectsInactive.Include));
            }

            foreach (var barrier in m_SlalomBarriers)
            {
                if (barrier != null)
                {
                    barrier.OnVehicleReached -= HandleBarrierContact;
                    barrier.OnVehicleReached += HandleBarrierContact;
                }
            }
        }

        private void UnsubscribeFromBarriers()
        {
            if (m_SlalomBarriers == null) return;
            foreach (var barrier in m_SlalomBarriers)
            {
                if (barrier != null)
                {
                    barrier.OnVehicleReached -= HandleBarrierContact;
                }
            }
        }

        private void HandleBarrierContact(LessonBarrierContact contact)
        {
            m_BarrierCollisions++;
            Debug.LogWarning($"[Lesson07BarrierManeuversController] ⚠️ ¡Contacto con valla '{contact.Barrier.name}' a {contact.SpeedKmh:F1} km/h! Total de toques: {m_BarrierCollisions}");
        }

        public void AutoDiscoverReferences()
        {
            if (m_LessonManager == null)
            {
                m_LessonManager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            }

            if (m_VehicleController == null)
            {
                m_VehicleController = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            }

            if (m_HUDPanel == null)
            {
                m_HUDPanel = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            }

            if (m_ScreenFader == null)
            {
                m_ScreenFader = UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            }

            if (m_StartZone == null)
            {
                GameObject sz = GameObject.Find("ParkingZone_Start");
                if (sz != null) m_StartZone = sz.GetComponent<ParkingZoneTrigger>();
            }

            if (m_ForwardFinishZone == null)
            {
                GameObject fz = GameObject.Find("ParkingZone_ForwardFinish");
                if (fz != null) m_ForwardFinishZone = fz.GetComponent<ParkingZoneTrigger>();
            }
        }
    }
}
