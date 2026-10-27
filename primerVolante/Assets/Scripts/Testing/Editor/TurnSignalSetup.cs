using System.Linq;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de la infracción de giro sin señalizar (<c>TURN_SIGNAL</c>): zona exenta
    /// <see cref="TurnSignalExemptZone"/> dentro de los prefabs <c>Road Corner_01</c> (curvas) y <c>Road Lane_04</c>
    /// (puntas de calle sin salida, de 6 m antes del centro al extremo cerrado), definición y entrada de catálogo de la infracción, la
    /// escena <c>GPSTurnSignalDemo</c> (copia de <c>GPSWrongWayDemo</c>) y el <see cref="TurnSignalDetector"/> en el
    /// <c>Car09</c> de esa escena. Para llevar la detección a otras escenas ver <see cref="TurnSignalSceneTool"/>.
    /// </summary>
    public static class TurnSignalSetup
    {
        public const string ROADS_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Prefab/Roads";
        public const string CORNER_PREFAB_PATH = ROADS_FOLDER + "/Road Corner_01.prefab";
        public const string CORNER_02_PREFAB_PATH = ROADS_FOLDER + "/Road Corner_02.prefab";
        public const string DEAD_END_PREFAB_PATH = ROADS_FOLDER + "/Road Lane_04.prefab";

        public const string SOURCE_SCENE_PATH = "Assets/Scenes/TestingScenes/GPSWrongWayDemo.unity";
        public const string SCENE_PATH = "Assets/Scenes/TestingScenes/GPSTurnSignalDemo.unity";

        public const string ZONE_NAME = "TurnSignalExemptZone";
        public const string CAR_NAME = "Car09";

        /// <summary>Metros de la zona de la punta sin salida que entran en la mitad abierta (antes del centro de la pieza).</summary>
        public const float DEAD_END_ZONE_BACK_M = 6f;

        /// <summary>Altura de la caja de la zona exenta sobre el piso (m).</summary>
        public const float ZONE_HEIGHT = 3f;

        /// <summary>Prefabs de calle que llevan la zona exenta.</summary>
        public static readonly string[] ExemptPrefabPaths = { CORNER_PREFAB_PATH, CORNER_02_PREFAB_PATH, DEAD_END_PREFAB_PATH };

        /// <summary>Cuántos prefabs guardó la última llamada a <see cref="EnsureAssets"/> (0 si ya estaban al día).</summary>
        public static int LastPrefabsSaved { get; private set; }

        [MenuItem("Tools/Primer Volante/Setup Turn Signal Detection")]
        public static void SetupAll()
        {
            Debug.Log("[TurnSignalSetup] 🔧 Configurando la detección de giro sin señalizar (TURN_SIGNAL)...");

            int layer = EnsureAssets(out int zones, out InfractionCatalogSO catalog);
            if (layer < 0) return;

            string sceneStatus = SetupScene(layer);
            if (sceneStatus == null) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TurnSignalSetup] ✅ Listo. Prefabs con zona exenta: {zones}/{ExemptPrefabPaths.Length}. " +
                      $"Catálogo con {catalog.Definitions.Count} definiciones. Escena {SCENE_PATH}: {sceneStatus}.");
        }

        /// <summary>
        /// Asegura lo que la infracción necesita fuera de las escenas: la capa <c>LaneZone</c> y su matriz de
        /// colisiones, la zona exenta de los prefabs de calle, las definiciones y el catálogo. Idempotente.
        /// Devuelve el índice de la capa, o -1 si algo falló.
        /// </summary>
        public static int EnsureAssets(out int zones, out InfractionCatalogSO catalog)
        {
            zones = 0;
            catalog = null;
            LastPrefabsSaved = 0;

            int layer = WrongWaySetup.EnsureLaneZoneLayer();
            if (layer < 0) return -1;
            WrongWaySetup.ConfigureCollisionMatrix(layer);

            foreach (string path in ExemptPrefabPaths)
            {
                if (EnsureExemptZone(path, layer)) zones++;
            }

            catalog = InfractionManagerSetup.EnsureDefinitionsAndCatalog();
            if (catalog == null)
            {
                Debug.LogError("[TurnSignalSetup] ❌ No se pudo crear el catálogo de infracciones.");
                return -1;
            }

            return layer;
        }

        // ------------------------------------------------------------------ Prefabs

        /// <summary>
        /// Crea o recalcula el hijo <see cref="ZONE_NAME"/> del prefab: un BoxCollider trigger en la capa
        /// <c>LaneZone</c> de <see cref="ZONE_HEIGHT"/> de alto que cubre toda la pieza (según los bounds de su malla).
        /// En la punta sin salida (<c>Road Lane_04</c>) cubre z local de −6 a +10 (el extremo cerrado más 6 m de
        /// la mitad abierta, para las U que empiezan antes del centro): la caja entera dejaba exento salir de la calle
        /// y doblar sin guiño en una intersección contigua. El prefab se guarda <b>solo si algo cambió</b>.
        /// Devuelve false si falló.
        /// </summary>
        public static bool EnsureExemptZone(string prefabPath, int layer)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError($"[TurnSignalSetup] ❌ No existe {prefabPath}.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var meshFilter = root.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    Debug.LogError($"[TurnSignalSetup] ❌ {root.name} no tiene malla: no se puede calcular la zona.");
                    return false;
                }

                Bounds bounds = meshFilter.sharedMesh.bounds;
                Vector3 center, size;
                if (prefabPath == DEAD_END_PREFAB_PATH)
                {
                    // El extremo cerrado (CurbWalls) es z local +: la zona va de DEAD_END_ZONE_BACK_M antes del centro
                    // hasta el extremo cerrado (z local de -6 a +10 en la pieza de 20 m).
                    float zMin = bounds.center.z - DEAD_END_ZONE_BACK_M;
                    float zMax = bounds.max.z;
                    center = new Vector3(bounds.center.x, ZONE_HEIGHT * 0.5f, (zMin + zMax) * 0.5f);
                    size = new Vector3(bounds.size.x, ZONE_HEIGHT, zMax - zMin);
                }
                else
                {
                    center = new Vector3(bounds.center.x, ZONE_HEIGHT * 0.5f, bounds.center.z);
                    size = new Vector3(bounds.size.x, ZONE_HEIGHT, bounds.size.z);
                }

                Transform zone = root.transform.Find(ZONE_NAME);
                if (zone != null && ZoneIsUpToDate(zone, layer, center, size)) return true;

                if (zone == null)
                {
                    var go = new GameObject(ZONE_NAME);
                    go.transform.SetParent(root.transform, false);
                    zone = go.transform;
                }

                zone.localPosition = Vector3.zero;
                zone.localRotation = Quaternion.identity;
                zone.localScale = Vector3.one;
                zone.gameObject.layer = layer;

                var marker = zone.GetComponent<TurnSignalExemptZone>();
                if (marker == null) marker = zone.gameObject.AddComponent<TurnSignalExemptZone>();

                var box = zone.GetComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = center;
                box.size = size;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                LastPrefabsSaved++;
                Debug.Log($"[TurnSignalSetup] 💾 Prefab {root.name}: zona exenta creada o actualizada ({center} / {size}).");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // true si la zona ya tiene la capa, el marcador, el trigger y la caja esperados.
        private static bool ZoneIsUpToDate(Transform zone, int layer, Vector3 center, Vector3 size)
        {
            var box = zone.GetComponent<BoxCollider>();
            return zone.gameObject.layer == layer
                   && zone.GetComponent<TurnSignalExemptZone>() != null
                   && box != null && box.isTrigger
                   && zone.localPosition == Vector3.zero && zone.localRotation == Quaternion.identity && zone.localScale == Vector3.one
                   && (box.center - center).sqrMagnitude < 1e-6f
                   && (box.size - size).sqrMagnitude < 1e-6f;
        }

        // ------------------------------------------------------------------ Escena

        // Devuelve una descripción de lo hecho o null si falló.
        private static string SetupScene(int layer)
        {
            string status;
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (scene.IsValid() && scene.isLoaded)
            {
                status = "ya estaba abierta";
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene open = SceneManager.GetSceneAt(i);
                    if (open.isDirty)
                    {
                        Debug.LogError($"[TurnSignalSetup] ❌ {open.name} tiene cambios sin guardar: guardalos antes de abrir {SCENE_PATH}.");
                        return null;
                    }
                }

                if (!System.IO.File.Exists(SCENE_PATH))
                {
                    if (!AssetDatabase.CopyAsset(SOURCE_SCENE_PATH, SCENE_PATH))
                    {
                        Debug.LogError($"[TurnSignalSetup] ❌ No se pudo copiar {SOURCE_SCENE_PATH} a {SCENE_PATH}.");
                        return null;
                    }

                    status = "copiada de GPSWrongWayDemo";
                }
                else
                {
                    status = "reutilizada";
                }

                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }

            VehicleController car = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<VehicleController>(true))
                .FirstOrDefault(v => v.gameObject.name == CAR_NAME);
            if (car == null)
            {
                Debug.LogError($"[TurnSignalSetup] ❌ {scene.name} no tiene un '{CAR_NAME}' con VehicleController.");
                return null;
            }

            var detector = car.GetComponent<TurnSignalDetector>();
            if (detector == null)
            {
                detector = Undo.AddComponent<TurnSignalDetector>(car.gameObject);
                status += ", detector agregado";
            }

            var so = new SerializedObject(detector);
            so.FindProperty("m_ZoneMask").FindPropertyRelative("m_Bits").intValue = 1 << layer;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(detector);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return status;
        }
    }
}
