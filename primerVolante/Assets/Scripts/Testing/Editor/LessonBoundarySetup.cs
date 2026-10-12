using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Environment;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

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

        /// <summary>
        /// Cuánto se adelanta la cara del muro hacia la calzada respecto de la cara del cordón (m). Con 0 el auto
        /// roza primero el plano superior de la vereda (y = 0.2) y rebota ~0.16 m hacia arriba; con 0.05 desliza
        /// limpio. Poner en 0 para que el muro arranque exactamente en la cara.
        /// </summary>
        public const float WALL_INSET = 0.05f;

        public const string FENCE_SOURCE_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic Control Barrier Fence.prefab";
        public const string BARRIER_PREFAB_PATH = ENVIRONMENT_FOLDER + "/LessonBarrier.prefab";
        public const string ALERT_TRIGGER_NAME = "AlertTrigger";

        /// <summary>Ancho de la valla (eje X de la malla) en metros.</summary>
        public const float BARRIER_WIDTH = 3.6f;

        /// <summary>Alto del collider de la valla en metros.</summary>
        public const float BARRIER_HEIGHT = 2.05f;

        /// <summary>Espesor del collider sólido: la malla mide 0.2 m, el sólido 0.4 m para no ser atravesado.</summary>
        public const float BARRIER_SOLID_THICKNESS = 0.4f;

        /// <summary>Profundidad del trigger de aviso: el sólido más 0.4 m de aire a cada lado.</summary>
        public const float BARRIER_ALERT_DEPTH = 1.2f;

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
            List<CurbWall> walls = RoadCurbWalls.BuildWalls(faces, WALL_THICKNESS, WALL_INSET);

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

                    bool alongZ = Mathf.Abs(wall.Normal.x) > Mathf.Abs(wall.Normal.y);
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

        // ------------------------------------------------------------------ Parte B: prefab LessonBarrier

        /// <summary>
        /// Crea o actualiza <c>LessonBarrier.prefab</c>: variante de la valla de control policial con un
        /// collider sólido grueso y un hijo trigger de aviso. La valla original no se modifica.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Lesson Barrier Prefab")]
        public static void SetupLessonBarrierPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(FENCE_SOURCE_PATH);
            if (source == null)
            {
                Debug.LogError($"[LessonBoundarySetup] ❌ No se encontró la valla original en {FENCE_SOURCE_PATH}.");
                return;
            }

            PhysicsMaterial noFriction = EnsureNoFrictionMaterial();
            EnsureFolder(ENVIRONMENT_FOLDER);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(BARRIER_PREFAB_PATH) == null)
            {
                CreateVariant(source, BARRIER_PREFAB_PATH);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BARRIER_PREFAB_PATH);
            try
            {
                var filter = root.GetComponentInChildren<MeshFilter>(true);
                if (filter == null || filter.sharedMesh == null)
                {
                    Debug.LogError("[LessonBoundarySetup] ❌ La valla no tiene malla; no se puede centrar el collider.");
                    return;
                }

                // Centro de la malla en el espacio de la raíz.
                Vector3 center = root.transform.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.bounds.center));
                center = new Vector3(Mathf.Round(center.x * 1000f) / 1000f, Mathf.Round(center.y * 1000f) / 1000f, Mathf.Round(center.z * 1000f) / 1000f);

                // Sólido: frena físicamente al auto. Con 0.4 m de espesor y detección continua no se atraviesa.
                var solid = GetOrAdd<BoxCollider>(root);
                solid.isTrigger = false;
                solid.center = center;
                solid.size = new Vector3(BARRIER_WIDTH, BARRIER_HEIGHT, BARRIER_SOLID_THICKNESS);
                solid.sharedMaterial = noFriction;

                // Aviso: el mensaje de trigger de un collider estático llega al GameObject del collider, por eso
                // el trigger y el script LessonBarrier van juntos en un hijo.
                Transform alert = root.transform.Find(ALERT_TRIGGER_NAME);
                if (alert == null)
                {
                    var go = new GameObject(ALERT_TRIGGER_NAME);
                    go.transform.SetParent(root.transform, false);
                    alert = go.transform;
                }

                alert.localPosition = Vector3.zero;
                alert.localRotation = Quaternion.identity;
                alert.localScale = Vector3.one;

                var trigger = GetOrAdd<BoxCollider>(alert.gameObject);
                trigger.isTrigger = true;
                trigger.center = center;
                trigger.size = new Vector3(BARRIER_WIDTH, BARRIER_HEIGHT, BARRIER_ALERT_DEPTH);

                GetOrAdd<LessonBarrier>(alert.gameObject);

                PrefabUtility.SaveAsPrefabAsset(root, BARRIER_PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[LessonBoundarySetup] ✅ LessonBarrier.prefab listo en {BARRIER_PREFAB_PATH}.");
        }

        private static void CreateVariant(GameObject source, string outputPath)
        {
            // La instancia intermedia vive en una preview scene para no ensuciar la escena abierta.
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        // ------------------------------------------------------------------ Escena de demo

        public const string DEMO_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityWithBarriers.unity";
        public const string BOUNDARIES_GROUP_NAME = "Lesson_Boundaries";
        public const string DEMO_BOUNDARY_NAME = "Boundary_Demo";

        // Tramo Road Lane_01 en (120, 140), yaw 0: calle a lo largo de Z con los cordones en x = 113 y x = 127. La
        // línea va en z = 134: a ~70 m del inicio del Car08, fuera de las tres rutas de NPC_Demo (el bloque del auto
        // NPC llega hasta z = 56.5 y los peatones hasta z = 88) y antes del contenedor estacionado en z ≈ 139..145.
        private static readonly Vector3 s_DemoCenter = new Vector3(120f, 0f, 134f);
        private const float DEMO_HALF_WIDTH = 7f;

        /// <summary>
        /// Crea en <c>CityWithBarriers</c> el grupo <c>Lesson_Boundaries</c> con una línea de vallas que corta la
        /// calle de cordón a cordón. Idempotente: si ya existe, actualiza la spline y regenera las vallas.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Lesson Boundary Demo (CityWithBarriers)")]
        public static void SetupLessonBoundaryDemo()
        {
            if (!TryGetCleanScene(DEMO_SCENE_PATH, out Scene scene)) return;

            var barrierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BARRIER_PREFAB_PATH);
            if (barrierPrefab == null)
            {
                Debug.LogError($"[LessonBoundarySetup] ❌ Falta {BARRIER_PREFAB_PATH}. Corré 'Setup Lesson Barrier Prefab' primero.");
                return;
            }

            // Grupo y línea (reutiliza los existentes).
            GameObject group = FindRootByName(scene, BOUNDARIES_GROUP_NAME);
            if (group == null)
            {
                group = new GameObject(BOUNDARIES_GROUP_NAME);
                SceneManager.MoveGameObjectToScene(group, scene);
            }

            Transform line = group.transform.Find(DEMO_BOUNDARY_NAME);
            if (line == null)
            {
                var go = new GameObject(DEMO_BOUNDARY_NAME);
                go.transform.SetParent(group.transform, false);
                line = go.transform;
            }

            line.position = s_DemoCenter;
            line.rotation = Quaternion.identity;
            line.localScale = Vector3.one;

            var container = GetOrAdd<SplineContainer>(line.gameObject);
            if (container.Splines.Count == 0) container.AddSpline(new Spline());
            Spline spline = container.Splines[0];
            spline.Clear();
            spline.Closed = false;
            spline.Add(new BezierKnot(new float3(-DEMO_HALF_WIDTH, 0f, 0f)), TangentMode.Linear);
            spline.Add(new BezierKnot(new float3(DEMO_HALF_WIDTH, 0f, 0f)), TangentMode.Linear);

            var boundary = GetOrAdd<LessonBoundary>(line.gameObject);
            var so = new SerializedObject(boundary);
            so.FindProperty("m_BarrierPrefab").objectReferenceValue = barrierPrefab;
            so.FindProperty("m_SplineIndex").intValue = 0;
            so.FindProperty("m_UsePrefabWidth").boolValue = true;
            so.FindProperty("m_Gap").floatValue = 0.1f;
            so.FindProperty("m_StretchToFit").boolValue = true;
            so.FindProperty("m_AlertCooldownSeconds").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!boundary.TryRebuildBarriers())
            {
                Debug.LogError("[LessonBoundarySetup] ❌ No se pudieron generar las vallas (¿Play Mode, prefab o spline inválidos?). La escena no se guardó.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[LessonBoundarySetup] ✅ Demo de barreras lista en {scene.name}: {BOUNDARIES_GROUP_NAME}/{DEMO_BOUNDARY_NAME} en {s_DemoCenter}.");
        }

        // Escena cargada y sin cambios sin guardar. Nunca abre en modo Single sobre cambios sin guardar.
        private static bool TryGetCleanScene(string scenePath, out Scene scene)
        {
            scene = SceneManager.GetSceneByPath(scenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty)
                {
                    Debug.LogError($"[LessonBoundarySetup] ❌ {scene.name} tiene cambios sin guardar: guardalos o descartalos antes de seguir.");
                    return false;
                }

                return true;
            }

            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogError($"[LessonBoundarySetup] ❌ No existe la escena {scenePath}.");
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogError($"[LessonBoundarySetup] ❌ {open.name} tiene cambios sin guardar: guardalos antes de abrir {scenePath}.");
                    return false;
                }
            }

            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return scene.IsValid() && scene.isLoaded;
        }

        public static GameObject FindRootByName(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
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
