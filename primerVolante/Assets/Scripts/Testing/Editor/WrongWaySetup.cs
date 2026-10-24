using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de la detección de contramano (<c>WRONG_WAY</c>): capa <c>LaneZone</c> y
    /// matriz de colisiones, <see cref="RoadLaneDirection"/> y zonas por carril en los prefabs de calle,
    /// <see cref="WrongWayDetector"/> en los autos del jugador, prefabs de los carteles de contramano y de
    /// sentido de circulación, y la escena <c>CityWithWrongWay</c> con una calle de mano única de demostración.
    /// </summary>
    public static partial class WrongWaySetup
    {
        public const string CAR_FOLDER = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs";

        /// <summary>Prefabs del auto del jugador (los mismos que llevan el <c>ObstacleContactSensor</c>).</summary>
        public static readonly string[] CarPaths =
        {
            CAR_FOLDER + "/Car07.prefab",
            CAR_FOLDER + "/Car08.prefab",
            CAR_FOLDER + "/Car09.prefab",
        };

        /// <summary>Piezas de calle recta de doble carril que llevan zonas (nombre del prefab).</summary>
        public static readonly string[] ZonePrefabNames =
        {
            "Road Lane_01", "Road Lane Half", "Road Lane_02", "Road Lane_04", "Road Lane Bus Stop", "Road Lane_03",
        };

        public const string LANE_ZONES_NAME = "LaneZones";
        public const string LANE_POS_NAME = "Lane_PosX";
        public const string LANE_NEG_NAME = "Lane_NegX";

        /// <summary>Altura de las cajas de zona sobre el piso (m).</summary>
        public const float ZONE_HEIGHT = 3f;

        // Road Lane_03 tiene una senda peatonal en z ∈ [1, 4] (medido en la malla). La zona termina en z = 0:
        // 1 m antes de la senda y sin zona hasta el borde z = +10, como margen de gracia para doblar.
        private const float k_Lane03ZMax = 0f;

        // Un muro de cordón "a lo largo de Z" mide más que esto (m); los tramos cortos son aberturas o esquinas.
        private const float k_MinCurbRunLength = 10f;

        public static bool HasLaneZones(string prefabName) => ZonePrefabNames.Contains(prefabName);

        // ------------------------------------------------------------------ Menú

        [MenuItem("Tools/Primer Volante/Setup Wrong Way Detection")]
        public static void SetupAll()
        {
            Debug.Log("[WrongWaySetup] 🔧 Configurando la detección de contramano...");

            int layer = EnsureLaneZoneLayer();
            if (layer < 0) return;
            ConfigureCollisionMatrix(layer);

            SetupRoadPrefabs(layer, out int withZones, out int cleaned);
            int cars = SetupCars(layer);
            SetupSignPrefabs();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string demo = SetupDemoScene();

            Debug.Log($"[WrongWaySetup] ✅ Listo. Capa LaneZone = {layer}. Prefabs de calle con zonas: {withZones}; " +
                      $"prefabs limpiados: {cleaned}; autos con detector: {cars}. {demo}");
        }

        // ------------------------------------------------------------------ Capa y matriz

        /// <summary>Crea la capa <c>LaneZone</c> en el primer índice libre (≥ 8). Devuelve el índice o -1.</summary>
        public static int EnsureLaneZoneLayer()
        {
            int existing = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[WrongWaySetup] ❌ No se pudo abrir TagManager.asset.");
                return -1;
            }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 9; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = RoadLaneZone.LayerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[WrongWaySetup] 🏷️ Capa '{RoadLaneZone.LayerName}' creada en el índice {i}.");
                return i;
            }

            Debug.LogError("[WrongWaySetup] ❌ No hay índices de capa libres.");
            return -1;
        }

        /// <summary>
        /// La capa <paramref name="layer"/> no colisiona con ninguna capa (ni consigo misma). Así los
        /// <c>Rigidbody</c> no generan eventos de trigger contra las zonas y el detector las encuentra igual con
        /// una consulta explícita (las consultas de física no usan la matriz).
        /// </summary>
        public static void ConfigureCollisionMatrix(int layer)
        {
            for (int other = 0; other < 32; other++) Physics.IgnoreLayerCollision(layer, other, true);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Prefabs de calle

        private struct LaneBox
        {
            public float XMin, XMax, ZMin, ZMax;
            public Vector3 Center => new Vector3((XMin + XMax) * 0.5f, ZONE_HEIGHT * 0.5f, (ZMin + ZMax) * 0.5f);
            public Vector3 Size => new Vector3(XMax - XMin, ZONE_HEIGHT, ZMax - ZMin);
        }

        private static void SetupRoadPrefabs(int layer, out int withZones, out int cleaned)
        {
            withZones = 0;
            cleaned = 0;
            foreach (string path in LessonBoundarySetup.FindRoadPrefabPaths())
            {
                string prefabName = System.IO.Path.GetFileNameWithoutExtension(path);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (HasLaneZones(prefabName))
                    {
                        SyncLaneZones(root, layer);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        withZones++;
                    }
                    else if (RemoveLaneZones(root))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        cleaned++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        // Quita zonas y dirección de una pieza excluida. true si había algo.
        private static bool RemoveLaneZones(GameObject root)
        {
            bool changed = false;
            Transform zones = root.transform.Find(LANE_ZONES_NAME);
            if (zones != null)
            {
                Object.DestroyImmediate(zones.gameObject);
                changed = true;
            }

            var direction = root.GetComponent<RoadLaneDirection>();
            if (direction != null)
            {
                Object.DestroyImmediate(direction);
                changed = true;
            }

            return changed;
        }

        // Crea o recalcula RoadLaneDirection y los dos hijos de zona, sin duplicar nada.
        private static void SyncLaneZones(GameObject root, int layer)
        {
            var direction = root.GetComponent<RoadLaneDirection>();
            if (direction == null)
            {
                direction = root.AddComponent<RoadLaneDirection>();
                direction.Flow = TrafficFlow.TwoWay;
            }

            Transform zones = root.transform.Find(LANE_ZONES_NAME);
            if (zones == null)
            {
                var go = new GameObject(LANE_ZONES_NAME);
                go.transform.SetParent(root.transform, false);
                zones = go.transform;
            }

            zones.localPosition = Vector3.zero;
            zones.localRotation = Quaternion.identity;
            zones.localScale = Vector3.one;
            zones.gameObject.layer = layer;

            SyncLane(zones, LANE_POS_NAME, LaneSide.PositiveX, ComputeLaneBox(root, LaneSide.PositiveX), layer);
            SyncLane(zones, LANE_NEG_NAME, LaneSide.NegativeX, ComputeLaneBox(root, LaneSide.NegativeX), layer);
        }

        private static void SyncLane(Transform zones, string name, LaneSide side, LaneBox box, int layer)
        {
            Transform lane = zones.Find(name);
            if (lane == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(zones, false);
                lane = go.transform;
            }

            lane.localPosition = Vector3.zero;
            lane.localRotation = Quaternion.identity;
            lane.localScale = Vector3.one;
            lane.gameObject.layer = layer;

            var zone = lane.GetComponent<RoadLaneZone>();
            if (zone == null) zone = lane.gameObject.AddComponent<RoadLaneZone>();
            zone.Side = side;

            var collider = lane.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = box.Center;
            collider.size = box.Size;
        }

        /// <summary>
        /// Caja de un carril en el espacio local de la pieza. X: de la línea central (0) hasta el cordón (el muro
        /// de cordón más alejado que corre a lo largo de Z de ese lado; si ese lado no tiene muro, como en el
        /// costado abierto de <c>Road Lane_02</c>, el del lado opuesto). Z: el largo de la malla; en
        /// <c>Road Lane_04</c> hasta la tapa del cordón y en <c>Road Lane_03</c> hasta antes de la senda.
        /// </summary>
        private static LaneBox ComputeLaneBox(GameObject root, LaneSide side)
        {
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            Bounds bounds = mesh.bounds;

            List<BoxCollider> walls = root.transform.Find(LessonBoundarySetup.CURB_WALLS_NAME)
                .GetComponentsInChildren<BoxCollider>().ToList();

            float CurbX(LaneSide s)
            {
                float best = 0f;
                foreach (BoxCollider wall in walls)
                {
                    Vector3 size = wall.transform.TransformVector(wall.size);
                    size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    if (size.z < k_MinCurbRunLength || size.z < size.x) continue;

                    float x = root.transform.InverseTransformPoint(wall.transform.TransformPoint(wall.center)).x;
                    if ((s == LaneSide.PositiveX && x <= 0f) || (s == LaneSide.NegativeX && x >= 0f)) continue;
                    if (Mathf.Abs(x) > Mathf.Abs(best)) best = x;
                }

                return best;
            }

            float curb = CurbX(side);
            if (Mathf.Approximately(curb, 0f)) curb = -CurbX(side == LaneSide.PositiveX ? LaneSide.NegativeX : LaneSide.PositiveX);

            float zMin = bounds.min.z;
            float zMax = bounds.max.z;

            // Calle sin salida: la tapa del cordón es un muro a lo largo de X cerca del centro.
            foreach (BoxCollider wall in walls)
            {
                Vector3 size = wall.transform.TransformVector(wall.size);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                Vector3 center = root.transform.InverseTransformPoint(wall.transform.TransformPoint(wall.center));
                if (size.x > size.z && Mathf.Abs(center.x) < 1f && size.x > 5f)
                {
                    if (center.z > 0f) zMax = Mathf.Min(zMax, center.z);
                    else zMin = Mathf.Max(zMin, center.z);
                }
            }

            if (root.name == "Road Lane_03") zMax = k_Lane03ZMax;

            var box = new LaneBox { ZMin = zMin, ZMax = zMax };
            if (side == LaneSide.PositiveX)
            {
                box.XMin = 0f;
                box.XMax = curb;
            }
            else
            {
                box.XMin = curb;
                box.XMax = 0f;
            }

            return box;
        }

        // ------------------------------------------------------------------ Autos del jugador

        private static int SetupCars(int layer)
        {
            int count = 0;
            foreach (string path in CarPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"[WrongWaySetup] ⚠️ No existe {path}; se omite.");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var detector = root.GetComponent<WrongWayDetector>();
                    if (detector == null) detector = root.AddComponent<WrongWayDetector>();

                    var so = new SerializedObject(detector);
                    so.FindProperty("m_ZoneMask").FindPropertyRelative("m_Bits").intValue = 1 << layer;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ Helpers

        private static void EnsureFolder(string folder) => LessonBoundarySetup.EnsureFolder(folder);

        internal static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
