using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de US30 (#24): colliders permanentes de calle dentro de los prefabs
    /// <c>Road*</c>, limpieza de overrides en escenas, prefab <c>LessonBarrier</c> y demo de barreras.
    /// </summary>
    public static class LessonBoundarySetup
    {
        public const string ROADS_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Prefab/Roads";
        public const string ENVIRONMENT_FOLDER = "Assets/Prefabs/Environment";
        public const string NO_FRICTION_PATH = ENVIRONMENT_FOLDER + "/NoFriction.asset";

        public const string CURB_WALLS_NAME = "CurbWalls";
        public const string DIAGONAL_PREFIX = "Diagonal_";
        public const string SPLIT_LINE_NAME = "Road Split Line";

        /// <summary>Espesor de los muros de cordón hacia la vereda (m).</summary>
        public const float WALL_THICKNESS = 0.4f;

        /// <summary>Altura de los muros de cordón desde y = 0 (m).</summary>
        public const float WALL_HEIGHT = 2f;

        // Prefijos de las piezas con cordón; el resto (Road Tile*, Road Concrete Tile*, Road Sidewalk) es plano.
        private static readonly string[] s_CurbPrefixes =
        {
            "Road Lane_", "Road Lane Half", "Road Lane Bus Stop",
            "Road Corner_", "Road Intersection_", "Road T_Intersection_",
        };

        /// <summary>true si la pieza tiene cordón y por lo tanto lleva muros de borde de calzada.</summary>
        public static bool HasCurbs(string prefabName)
        {
            return s_CurbPrefixes.Any(prefix => prefabName.StartsWith(prefix));
        }

        /// <summary>Rutas de los prefabs de calle, ordenadas por nombre.</summary>
        public static string[] FindRoadPrefabPaths()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { ROADS_FOLDER })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileName(path).StartsWith("Road"))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();
        }

        // ------------------------------------------------------------------ Parte A: colliders de calle

        [MenuItem("Tools/Primer Volante/Setup Road Colliders")]
        public static void SetupRoadColliders()
        {
            Debug.Log("[LessonBoundarySetup] 🛣️ Configurando colliders de los prefabs de calle...");

            PhysicsMaterial noFriction = EnsureNoFrictionMaterial();
            string[] paths = FindRoadPrefabPaths();

            int floors = 0, withWalls = 0, boxes = 0;
            foreach (string path in paths)
            {
                SetupRoadPrefab(path, noFriction, out bool floor, out int wallCount);
                if (floor) floors++;
                if (wallCount > 0) withWalls++;
                boxes += wallCount;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LessonBoundarySetup] ✅ {paths.Length} prefabs de calle: {floors} con MeshCollider de piso, " +
                      $"{withWalls} con muros de cordón ({boxes} cajas).");
        }

        /// <summary>
        /// Crea o actualiza el <see cref="PhysicsMaterial"/> sin fricción (fricción 0, combinación Minimum) que
        /// comparten los muros y las vallas: <c>VehicleController</c> fija la velocidad en cada paso y la fricción
        /// lo dejaría pegado al rozar de costado.
        /// </summary>
        public static PhysicsMaterial EnsureNoFrictionMaterial()
        {
            EnsureFolder(ENVIRONMENT_FOLDER);

            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(NO_FRICTION_PATH);
            if (material == null)
            {
                material = new PhysicsMaterial("NoFriction");
                AssetDatabase.CreateAsset(material, NO_FRICTION_PATH);
            }

            material.dynamicFriction = 0f;
            material.staticFriction = 0f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetupRoadPrefab(string path, PhysicsMaterial noFriction, out bool hasFloor, out int wallCount)
        {
            hasFloor = false;
            wallCount = 0;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var filter = root.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    Debug.LogWarning($"[LessonBoundarySetup] ⚠️ {root.name} no tiene MeshFilter con malla; se omite.");
                    return;
                }

                // Piso: MeshCollider no convexo con la malla de la pieza. La marca Split Line es decorativa
                // (±0.1 m sobre y = 0) y no debe frenar al auto al cruzarla.
                var floor = root.GetComponent<MeshCollider>();
                if (root.name == SPLIT_LINE_NAME)
                {
                    if (floor != null) Object.DestroyImmediate(floor);
                }
                else
                {
                    if (floor == null) floor = root.AddComponent<MeshCollider>();
                    floor.sharedMesh = filter.sharedMesh;
                    floor.convex = false;
                    floor.isTrigger = false;
                    hasFloor = true;
                }

                Transform curbWalls = root.transform.Find(CURB_WALLS_NAME);
                if (HasCurbs(root.name))
                {
                    wallCount = SyncCurbWalls(root, filter.sharedMesh, noFriction);
                }
                else if (curbWalls != null)
                {
                    Object.DestroyImmediate(curbWalls.gameObject);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Deja el hijo <c>CurbWalls</c> con una caja por cada tramo de cordón. Reutiliza los componentes
        /// existentes para que volver a correr el setup no duplique nada ni cambie los fileIDs del prefab.
        /// </summary>
        private static int SyncCurbWalls(GameObject root, Mesh mesh, PhysicsMaterial noFriction)
        {
            List<CurbFace> faces = RoadCurbWalls.FindRoadFacingFaces(mesh);
            List<CurbWall> walls = RoadCurbWalls.BuildWalls(faces, WALL_THICKNESS);

            Transform curb = root.transform.Find(CURB_WALLS_NAME);
            if (curb == null)
            {
                var go = new GameObject(CURB_WALLS_NAME);
                go.transform.SetParent(root.transform, false);
                curb = go.transform;
            }

            curb.localPosition = Vector3.zero;
            curb.localRotation = Quaternion.identity;
            curb.localScale = Vector3.one;

            // Sin renderers: solo colliders.
            foreach (Renderer renderer in curb.GetComponents<Renderer>()) Object.DestroyImmediate(renderer);
            foreach (MeshFilter meshFilter in curb.GetComponents<MeshFilter>()) Object.DestroyImmediate(meshFilter);

            var boxes = new List<BoxCollider>(curb.GetComponents<BoxCollider>());
            int boxIndex = 0, diagonalIndex = 0;

            foreach (CurbWall wall in walls)
            {
                if (wall.IsAxisAligned)
                {
                    BoxCollider box = boxIndex < boxes.Count ? boxes[boxIndex] : curb.gameObject.AddComponent<BoxCollider>();
                    boxIndex++;

                    bool alongZ = Mathf.Abs(wall.Normal.x) > 0.999f;
                    box.center = new Vector3(wall.Center.x, WALL_HEIGHT * 0.5f, wall.Center.y);
                    box.size = alongZ
                        ? new Vector3(wall.Thickness, WALL_HEIGHT, wall.Length)
                        : new Vector3(wall.Length, WALL_HEIGHT, wall.Thickness);
                    ConfigureWallCollider(box, noFriction);
                }
                else
                {
                    // Un BoxCollider no se puede rotar solo: los tramos diagonales (p. ej. la bahía del Bus Stop)
                    // van en un hijo propio orientado a lo largo del tramo.
                    string childName = $"{DIAGONAL_PREFIX}{diagonalIndex:00}";
                    diagonalIndex++;

                    Transform child = curb.Find(childName);
                    if (child == null)
                    {
                        var go = new GameObject(childName);
                        go.transform.SetParent(curb, false);
                        child = go.transform;
                    }

                    child.localPosition = new Vector3(wall.Center.x, 0f, wall.Center.y);
                    child.localRotation = Quaternion.FromToRotation(Vector3.right, new Vector3(wall.Tangent.x, 0f, wall.Tangent.y));
                    child.localScale = Vector3.one;

                    var box = child.GetComponent<BoxCollider>();
                    if (box == null) box = child.gameObject.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, WALL_HEIGHT * 0.5f, 0f);
                    box.size = new Vector3(wall.Length, WALL_HEIGHT, wall.Thickness);
                    ConfigureWallCollider(box, noFriction);
                }
            }

            for (int i = boxIndex; i < boxes.Count; i++) Object.DestroyImmediate(boxes[i]);

            var surplus = new List<GameObject>();
            foreach (Transform child in curb)
            {
                if (!child.name.StartsWith(DIAGONAL_PREFIX)) continue;
                if (!int.TryParse(child.name.Substring(DIAGONAL_PREFIX.Length), out int index) || index >= diagonalIndex)
                {
                    surplus.Add(child.gameObject);
                }
            }

            foreach (GameObject go in surplus) Object.DestroyImmediate(go);

            return walls.Count;
        }

        private static void ConfigureWallCollider(BoxCollider box, PhysicsMaterial noFriction)
        {
            box.isTrigger = false;
            box.sharedMaterial = noFriction;
        }

        // ------------------------------------------------------------------ Limpieza de overrides de escena

        /// <summary>
        /// Quita de la escena activa los <c>MeshCollider</c> agregados como override (*Added Component*) a las
        /// instancias de pieza de calle: ahora el collider vive en el prefab y quedaría duplicado. No toca ningún
        /// otro override ni componente.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Remove Road Collider Overrides (Active Scene)")]
        public static void RemoveRoadColliderOverridesActiveScene()
        {
            RemoveRoadColliderOverrides(SceneManager.GetActiveScene(), save: true);
        }

        /// <summary>
        /// Quita los <c>MeshCollider</c> agregados a las instancias de calle de <paramref name="scene"/>. Se niega a
        /// trabajar sobre una escena con cambios sin guardar. Devuelve la cantidad de overrides quitados o -1 si no
        /// pudo trabajar.
        /// </summary>
        public static int RemoveRoadColliderOverrides(Scene scene, bool save)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[LessonBoundarySetup] ❌ La escena no está cargada.");
                return -1;
            }

            if (scene.isDirty)
            {
                Debug.LogError($"[LessonBoundarySetup] ❌ La escena {scene.name} tiene cambios sin guardar: guardalos o descartalos antes de limpiar.");
                return -1;
            }

            int instances = 0, removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (!IsRoadInstanceRoot(go)) continue;
                    instances++;

                    foreach (AddedComponent added in PrefabUtility.GetAddedComponents(go))
                    {
                        if (added.instanceComponent is MeshCollider)
                        {
                            added.Revert(InteractionMode.AutomatedAction);
                            removed++;
                        }
                    }
                }
            }

            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (save) EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"[LessonBoundarySetup] 🧹 {scene.name}: {removed} MeshCollider agregados quitados de {instances} instancias de calle.");
            return removed;
        }

        /// <summary>true si <paramref name="go"/> es la raíz de una instancia de un prefab de <c>Prefab/Roads</c>.</summary>
        public static bool IsRoadInstanceRoot(GameObject go)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return false;

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return false;

            string path = AssetDatabase.GetAssetPath(source);
            return path.StartsWith(ROADS_FOLDER + "/") && System.IO.Path.GetFileName(path).StartsWith("Road");
        }

        // ------------------------------------------------------------------ Helpers

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
