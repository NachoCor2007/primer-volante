using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de US30 (#24): colliders permanentes de calle, detección continua del auto,
    /// barreras de lección y análisis de fugas.
    /// </summary>
    public static class LessonBoundaryVerification
    {
        private const string CAR08_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car08.prefab";
        private const string CAR07_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car07.prefab";

        private const string SCENES_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes";
        public const string BARRIERS_SCENE_PATH = SCENES_FOLDER + "/CityWithBarriers.unity";
        public const string CAR_SCENE_PATH = SCENES_FOLDER + "/CityWithCar.unity";

        // Cordones esperados por pieza, medidos a mano sobre las mallas (tabla de §2 del prompt): normal (nx, nz),
        // offset del plano y tramo [s0, s1] sobre la tangente (-nz, nx).
        private static readonly Dictionary<string, (float nx, float nz, float d, float s0, float s1)[]> s_ExpectedFaces =
            new Dictionary<string, (float, float, float, float, float)[]>
            {
                ["Road Lane_01"] = new[] { (-1f, 0f, -7f, -10f, 10f), (1f, 0f, -7f, -10f, 10f) },
                ["Road Lane_03"] = new[] { (-1f, 0f, -7f, -10f, 10f), (1f, 0f, -7f, -10f, 10f) },
                ["Road Lane Half"] = new[] { (-1f, 0f, -7f, -5f, 5f), (1f, 0f, -7f, -5f, 5f) },
                // Hueco de 14 m sin cordón en x− (z de −7 a +7) con tapas de vereda en z = ±7.
                ["Road Lane_02"] = new[]
                {
                    (-1f, 0f, -7f, -10f, 10f),
                    (1f, 0f, -7f, -10f, -7f), (1f, 0f, -7f, 7f, 10f),
                    (0f, -1f, -7f, -10f, -7f), (0f, 1f, -7f, 7f, 10f),
                },
                // Calle sin salida: vereda de lado a lado en z = 7..10.
                ["Road Lane_04"] = new[] { (-1f, 0f, -7f, -7f, 10f), (1f, 0f, -7f, -10f, 7f), (0f, -1f, -7f, -7f, 7f) },
                // La calzada entra por z+ y sale por x−; cordón en x+ y vereda de lado a lado en z−.
                ["Road Corner_01"] = new[]
                {
                    (-1f, 0f, -7f, -10f, 7f), (0f, 1f, -7f, -7f, 10f),
                    (1f, 0f, -7f, 7f, 10f), (0f, -1f, -7f, -10f, -7f),
                },
                // Dos esquinas arriba (z+) y vereda de lado a lado en z−.
                ["Road T_Intersection_01"] = new[]
                {
                    (0f, 1f, -7f, -10f, 10f),
                    (-1f, 0f, -7f, -10f, -7f), (0f, -1f, -7f, -10f, -7f),
                    (1f, 0f, -7f, 7f, 10f), (0f, -1f, -7f, 7f, 10f),
                },
                // Cuatro esquinas de 3 × 3 m, ocho caras.
                ["Road Intersection_01"] = new[]
                {
                    (-1f, 0f, -7f, -10f, -7f), (-1f, 0f, -7f, 7f, 10f),
                    (1f, 0f, -7f, -10f, -7f), (1f, 0f, -7f, 7f, 10f),
                    (0f, -1f, -7f, -10f, -7f), (0f, -1f, -7f, 7f, 10f),
                    (0f, 1f, -7f, -10f, -7f), (0f, 1f, -7f, 7f, 10f),
                },
            };

        [MenuItem("Tools/Primer Volante/Run Lesson Boundary Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE LÍMITES DE LECCIÓN (US30 #24)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyRoadPrefabs(ref total, ref passed);
            VerifyCurbTable(ref total, ref passed);
            VerifyWallGeometry(ref total, ref passed);
            VerifySceneRoadInstances(BARRIERS_SCENE_PATH, ref total, ref passed);
            VerifySceneRoadInstances(CAR_SCENE_PATH, ref total, ref passed);
            VerifyCarDetection(ref total, ref passed);
            VerifyBarrierPrefab(ref total, ref passed);
            VerifyBoundaryMath(ref total, ref passed);
            VerifyBoundaryComponent(ref total, ref passed);
            VerifyDemoScene(ref total, ref passed);
            VerifyExistingSuites(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");

            // Informativo: no cuenta en el resultado.
            AnalyzeRoadLeaks();
        }

        // ---------------------------------------------------------------- Prefabs de calle

        private static void VerifyRoadPrefabs(ref int total, ref int passed)
        {
            string[] paths = LessonBoundarySetup.FindRoadPrefabPaths();
            Assert(paths.Length == 18, $"Deben existir 18 prefabs de calle (encontrados {paths.Length})", ref total, ref passed);

            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(LessonBoundarySetup.NO_FRICTION_PATH);
            Assert(material != null && material.dynamicFriction == 0f && material.staticFriction == 0f &&
                   material.frictionCombine == PhysicsMaterialCombine.Minimum,
                "El PhysicsMaterial NoFriction debe tener fricción 0 y combinación Minimum", ref total, ref passed);

            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string label = prefab.name;
                var filter = prefab.GetComponent<MeshFilter>();
                var floor = prefab.GetComponent<MeshCollider>();
                Transform curbWalls = prefab.transform.Find(LessonBoundarySetup.CURB_WALLS_NAME);

                if (label == LessonBoundarySetup.SPLIT_LINE_NAME)
                {
                    Assert(prefab.GetComponentsInChildren<Collider>(true).Length == 0 && curbWalls == null,
                        $"{label}: es una marca decorativa y no lleva ningún collider", ref total, ref passed);
                    continue;
                }

                Assert(floor != null && !floor.convex && !floor.isTrigger && filter != null && floor.sharedMesh == filter.sharedMesh,
                    $"{label}: MeshCollider de piso no convexo con la malla del MeshFilter", ref total, ref passed);

                if (!LessonBoundarySetup.HasCurbs(label))
                {
                    Assert(curbWalls == null, $"{label}: pieza plana, no debe tener {LessonBoundarySetup.CURB_WALLS_NAME}", ref total, ref passed);
                    continue;
                }

                Assert(curbWalls != null, $"{label}: debe tener el hijo {LessonBoundarySetup.CURB_WALLS_NAME}", ref total, ref passed);
                if (curbWalls == null) continue;

                var boxes = curbWalls.GetComponentsInChildren<BoxCollider>(true);
                Assert(boxes.Length >= 1 && boxes.All(b => !b.isTrigger),
                    $"{label}: CurbWalls con ≥ 1 BoxCollider no trigger ({boxes.Length})", ref total, ref passed);
                Assert(curbWalls.GetComponentsInChildren<Renderer>(true).Length == 0 &&
                       curbWalls.GetComponentsInChildren<MeshFilter>(true).Length == 0,
                    $"{label}: CurbWalls no debe tener renderers", ref total, ref passed);
                Assert(boxes.All(b => Mathf.Min(b.size.x, b.size.z) >= LessonBoundarySetup.WALL_THICKNESS - 0.001f && b.size.y >= 1.8f),
                    $"{label}: muros de espesor ≥ 0.4 m y altura ≥ 1.8 m", ref total, ref passed);
                Assert(boxes.All(b => b.sharedMaterial == material),
                    $"{label}: muros con el material sin fricción", ref total, ref passed);
            }
        }

        // Contraste de la derivación de la malla contra la tabla medida a mano.
        private static void VerifyCurbTable(ref int total, ref int passed)
        {
            foreach (var entry in s_ExpectedFaces)
            {
                var prefab = FindRoadPrefab(entry.Key);
                if (prefab == null)
                {
                    Assert(false, $"{entry.Key}: prefab debe existir", ref total, ref passed);
                    continue;
                }

                List<CurbFace> faces = RoadCurbWalls.FindRoadFacingFaces(prefab.GetComponent<MeshFilter>().sharedMesh);
                var expected = entry.Value;

                bool allMatch = faces.Count == expected.Length && expected.All(e => faces.Any(f =>
                    Mathf.Abs(f.Normal.x - e.nx) < 0.01f && Mathf.Abs(f.Normal.y - e.nz) < 0.01f &&
                    Mathf.Abs(f.Offset - e.d) < 0.05f && Mathf.Abs(f.Start - e.s0) < 0.05f && Mathf.Abs(f.End - e.s1) < 0.05f));

                Assert(allMatch,
                    $"{entry.Key}: los cordones derivados de la malla coinciden con la tabla ({faces.Count} de {expected.Length} caras" +
                    (allMatch ? ")" : $"; derivadas: {string.Join(" | ", faces)})"), ref total, ref passed);
            }

            // T_Intersection_02 no estaba en la tabla: debe resultar igual a la 01 (misma planta).
            var t01 = FindRoadPrefab("Road T_Intersection_01");
            var t02 = FindRoadPrefab("Road T_Intersection_02");
            if (t01 != null && t02 != null)
            {
                int count01 = RoadCurbWalls.FindRoadFacingFaces(t01.GetComponent<MeshFilter>().sharedMesh).Count;
                int count02 = RoadCurbWalls.FindRoadFacingFaces(t02.GetComponent<MeshFilter>().sharedMesh).Count;
                Assert(count01 == count02 && count02 > 0,
                    $"T_Intersection_02: misma cantidad de caras de cordón que la 01 ({count02} vs {count01})", ref total, ref passed);
            }
        }

        // ---------------------------------------------------------------- Geometría de los muros (preview scene)

        private static void VerifyWallGeometry(ref int total, ref int passed)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                PhysicsScene physics = preview.GetPhysicsScene();

                foreach (string path in LessonBoundarySetup.FindRoadPrefabPaths())
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!LessonBoundarySetup.HasCurbs(prefab.name)) continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    try
                    {
                        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        Physics.SyncTransforms();

                        VerifyFacesBlockRays(prefab, instance, physics, ref total, ref passed);
                        VerifyRoadStaysFree(prefab, instance, physics, ref total, ref passed);
                    }
                    finally
                    {
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // Desde 0.3 m dentro de la calzada, un rayo horizontal a y = 1 hacia cada cara debe chocar un muro.
        private static void VerifyFacesBlockRays(GameObject prefab, GameObject instance, PhysicsScene physics,
            ref int total, ref int passed)
        {
            List<CurbFace> faces = RoadCurbWalls.FindRoadFacingFaces(prefab.GetComponent<MeshFilter>().sharedMesh);
            Transform curbWalls = instance.transform.Find(LessonBoundarySetup.CURB_WALLS_NAME);

            var misses = new List<string>();
            foreach (CurbFace face in faces)
            {
                foreach (float fraction in new[] { 0.1f, 0.5f, 0.9f })
                {
                    float s = Mathf.Lerp(face.Start, face.End, fraction);
                    Vector2 onFace = face.PointAt(s);
                    Vector2 origin = onFace + face.Normal * 0.3f;
                    var from = new Vector3(origin.x, 1f, origin.y);
                    var direction = new Vector3(-face.Normal.x, 0f, -face.Normal.y);

                    // Puede chocar antes de la cara (p. ej. una diagonal que pasa delante de una tapa chica): sigue
                    // bloqueando. Lo que no vale es chocar más allá de la cara o chocar algo que no es un muro.
                    bool hitWall = physics.Raycast(from, direction, out RaycastHit hit, 0.6f, ~0, QueryTriggerInteraction.Ignore) &&
                                   curbWalls != null && hit.collider.transform.IsChildOf(curbWalls) &&
                                   hit.distance <= 0.33f;
                    if (!hitWall) misses.Add($"{face} @{fraction:F1}");
                }
            }

            Assert(faces.Count > 0 && misses.Count == 0,
                $"{prefab.name}: un rayo horizontal desde la calzada hacia cada cara de cordón choca un muro" +
                (misses.Count == 0 ? "" : $" (fallan: {string.Join("; ", misses)})"), ref total, ref passed);
        }

        // La calzada no se angosta: ningún muro invade los puntos de asfalto a ≥ 0.3 m del cordón.
        private static void VerifyRoadStaysFree(GameObject prefab, GameObject instance, PhysicsScene physics,
            ref int total, ref int passed)
        {
            Bounds bounds = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
            var results = new Collider[8];
            var halfExtents = new Vector3(0.15f, 0.5f, 0.15f);

            int checkedPoints = 0;
            var blocked = new List<string>();
            for (float x = bounds.min.x + 0.5f; x < bounds.max.x - 0.4f; x += 0.5f)
            {
                for (float z = bounds.min.z + 0.5f; z < bounds.max.z - 0.4f; z += 0.5f)
                {
                    if (!IsOpenRoad(physics, x, z)) continue;
                    checkedPoints++;

                    int count = physics.OverlapBox(new Vector3(x, 1f, z), halfExtents, results, Quaternion.identity, ~0,
                        QueryTriggerInteraction.Ignore);
                    if (count > 0) blocked.Add($"({x:F1},{z:F1})");
                }
            }

            Assert(checkedPoints > 20 && blocked.Count == 0,
                $"{prefab.name}: la calzada sigue libre ({checkedPoints} puntos de asfalto sin muros)" +
                (blocked.Count == 0 ? "" : $" (bloqueados: {string.Join(" ", blocked.Take(8))})"), ref total, ref passed);

            // Eje de cada carril abierto: un rayo a lo largo de los tramos de asfalto continuo no debe chocar nada.
            var axisBlocked = new List<string>();
            int axisRuns = 0;
            foreach (bool alongX in new[] { true, false })
            {
                float min = alongX ? bounds.min.x : bounds.min.z;
                float max = alongX ? bounds.max.x : bounds.max.z;

                float runStart = float.NaN;
                for (float t = min + 0.1f; t <= max - 0.1f + 0.001f; t += 0.25f)
                {
                    bool open = alongX ? IsOpenRoad(physics, t, 0f) : IsOpenRoad(physics, 0f, t);
                    if (open && float.IsNaN(runStart)) runStart = t;

                    bool runEnds = float.IsNaN(runStart) == false && (!open || t + 0.25f > max - 0.1f);
                    if (!runEnds) continue;

                    float runEnd = open ? t : t - 0.25f;
                    if (runEnd - runStart >= 2f)
                    {
                        axisRuns++;
                        Vector3 from = alongX ? new Vector3(runStart, 1f, 0f) : new Vector3(0f, 1f, runStart);
                        Vector3 direction = alongX ? Vector3.right : Vector3.forward;
                        if (physics.Raycast(from, direction, out RaycastHit hit, runEnd - runStart, ~0, QueryTriggerInteraction.Ignore))
                        {
                            axisBlocked.Add($"{(alongX ? "x" : "z")}[{runStart:F1}..{runEnd:F1}] choca {hit.collider.name}");
                        }
                    }

                    runStart = float.NaN;
                }
            }

            Assert(axisRuns > 0 && axisBlocked.Count == 0,
                $"{prefab.name}: un rayo a lo largo del eje de cada carril abierto no choca nada ({axisRuns} tramos)" +
                (axisBlocked.Count == 0 ? "" : $" (choca: {string.Join("; ", axisBlocked)})"), ref total, ref passed);
        }

        // Asfalto con ≥ 0.3 m de margen a la vereda y al borde de la malla, medido contra el MeshCollider de piso.
        private static bool IsOpenRoad(PhysicsScene physics, float x, float z)
        {
            const float margin = 0.3f;
            return IsGround(physics, x, z) &&
                   IsGround(physics, x + margin, z) && IsGround(physics, x - margin, z) &&
                   IsGround(physics, x, z + margin) && IsGround(physics, x, z - margin);
        }

        private static bool IsGround(PhysicsScene physics, float x, float z)
        {
            return physics.Raycast(new Vector3(x, 1f, z), Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore) &&
                   hit.point.y < RoadCurbWalls.GROUND_MAX_HEIGHT;
        }

        // ---------------------------------------------------------------- Instancias de calle en escenas

        // Sin overrides de MeshCollider agregados y exactamente 1 MeshCollider efectivo (0 en Split Line).
        private static void VerifySceneRoadInstances(string scenePath, ref int total, ref int passed)
        {
            string label = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (!System.IO.File.Exists(scenePath))
            {
                Assert(false, $"{label}: la escena {scenePath} debe existir", ref total, ref passed);
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // Aditiva: nunca se reemplaza una escena abierta.
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                int instances = 0;
                var withOverride = new List<string>();
                var wrongCount = new List<string>();

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GameObject go = t.gameObject;
                        if (!LessonBoundarySetup.IsRoadInstanceRoot(go)) continue;
                        instances++;

                        int added = PrefabUtility.GetAddedComponents(go).Count(a => a.instanceComponent is MeshCollider);
                        if (added > 0) withOverride.Add(go.name);

                        string source = PrefabUtility.GetCorrespondingObjectFromSource(go).name;
                        int expected = source == LessonBoundarySetup.SPLIT_LINE_NAME ? 0 : 1;
                        int actual = go.GetComponents<MeshCollider>().Length;
                        if (actual != expected) wrongCount.Add($"{go.name}={actual}");
                    }
                }

                Assert(instances > 0, $"{label}: debe tener instancias de calle ({instances})", ref total, ref passed);
                Assert(withOverride.Count == 0,
                    $"{label}: ninguna instancia de calle tiene un MeshCollider agregado como override ({withOverride.Count})",
                    ref total, ref passed);
                Assert(wrongCount.Count == 0,
                    $"{label}: cada instancia tiene exactamente 1 MeshCollider efectivo, 0 en Split Line " +
                    $"({wrongCount.Count} incorrectas{(wrongCount.Count == 0 ? "" : ": " + string.Join(", ", wrongCount.Take(5)))})",
                    ref total, ref passed);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---------------------------------------------------------------- Auto del jugador

        private static void VerifyCarDetection(ref int total, ref int passed)
        {
            foreach (string path in new[] { CAR08_PATH, CAR07_PATH })
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert(prefab != null, $"{label}: prefab debe existir", ref total, ref passed);
                if (prefab == null) continue;

                var controllerAsset = prefab.GetComponentInChildren<VehicleController>(true);
                var bodyAsset = controllerAsset != null ? controllerAsset.GetComponent<Rigidbody>() : null;
                Assert(bodyAsset != null && bodyAsset.collisionDetectionMode == CollisionDetectionMode.Continuous,
                    $"{label}: el valor serializado del Rigidbody es Continuous", ref total, ref passed);

                Scene preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    var controller = instance.GetComponentInChildren<VehicleController>(true);
                    var body = controller != null ? controller.GetComponent<Rigidbody>() : null;
                    Assert(body != null, $"{label}: debe tener VehicleController con Rigidbody", ref total, ref passed);
                    if (body == null) continue;

                    // Se fuerza otro valor para comprobar que ConfigurePhysicsAndColliders lo vuelve a Continuous.
                    body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    controller.ConfigurePhysicsAndColliders();
                    Assert(body.collisionDetectionMode == CollisionDetectionMode.Continuous,
                        $"{label}: ConfigurePhysicsAndColliders deja la detección en Continuous ({body.collisionDetectionMode})",
                        ref total, ref passed);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(preview);
                }
            }
        }

        // ---------------------------------------------------------------- Prefab LessonBarrier

        private static void VerifyBarrierPrefab(ref int total, ref int passed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LessonBoundarySetup.BARRIER_PREFAB_PATH);
            Assert(prefab != null, "LessonBarrier.prefab debe existir", ref total, ref passed);
            if (prefab == null) return;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            Assert(PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant &&
                   source != null && AssetDatabase.GetAssetPath(source) == LessonBoundarySetup.FENCE_SOURCE_PATH,
                "LessonBarrier: debe ser variante de la valla de control policial", ref total, ref passed);

            var original = AssetDatabase.LoadAssetAtPath<GameObject>(LessonBoundarySetup.FENCE_SOURCE_PATH);
            Assert(original != null && original.GetComponentsInChildren<Collider>(true).Length == 0,
                "La valla original sigue sin collider (no se modifica)", ref total, ref passed);

            var noFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(LessonBoundarySetup.NO_FRICTION_PATH);
            var solid = prefab.GetComponent<BoxCollider>();
            Assert(solid != null && !solid.isTrigger && solid.size.z >= 0.4f - 0.001f,
                $"LessonBarrier: BoxCollider sólido de espesor ≥ 0.4 m ({(solid != null ? solid.size.z.ToString("F2") : "sin collider")})",
                ref total, ref passed);
            Assert(solid != null && Mathf.Abs(solid.size.x - 3.6f) < 0.01f && Mathf.Abs(solid.size.y - 2.05f) < 0.01f,
                "LessonBarrier: el sólido mide 3.6 × 2.05 m de frente", ref total, ref passed);
            Assert(solid != null && solid.sharedMaterial == noFriction,
                "LessonBarrier: el sólido usa el material sin fricción", ref total, ref passed);

            var meshFilter = prefab.GetComponentInChildren<MeshFilter>(true);
            Assert(solid != null && meshFilter != null &&
                   Vector3.Distance(solid.center, prefab.transform.InverseTransformPoint(meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center))) < 0.01f,
                "LessonBarrier: el sólido está centrado en la malla", ref total, ref passed);

            Transform alert = prefab.transform.Find(LessonBoundarySetup.ALERT_TRIGGER_NAME);
            var trigger = alert != null ? alert.GetComponent<BoxCollider>() : null;
            Assert(trigger != null && trigger.isTrigger && alert.GetComponent<LessonBarrier>() != null,
                $"LessonBarrier: hijo {LessonBoundarySetup.ALERT_TRIGGER_NAME} con BoxCollider trigger y el script LessonBarrier",
                ref total, ref passed);
            Assert(solid != null && trigger != null && trigger.size.z >= solid.size.z + 0.8f - 0.001f &&
                   Mathf.Abs(trigger.size.x - solid.size.x) < 0.01f,
                "LessonBarrier: el trigger deja 0.4 m de aire a cada lado del sólido", ref total, ref passed);

            Assert(prefab.GetComponentsInChildren<VehicleController>(true).Length == 0 &&
                   prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0,
                "LessonBarrier: sin VehicleController ni Rigidbody", ref total, ref passed);

            var renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
            Assert(renderer != null && renderer.enabled, "LessonBarrier: conserva su MeshRenderer visible", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Funciones puras de LessonBoundary

        private static void VerifyBoundaryMath(ref int total, ref int passed)
        {
            const float len = 3.6f, gap = 0.1f;

            // Cantidad con estirado: round((largo + sep) / (ancho + sep)); cerrada: round(largo / (ancho + sep)).
            AssertCount(0f, false, true, 0, ref total, ref passed);
            AssertCount(3.6f, false, true, 1, ref total, ref passed);
            AssertCount(7f, false, true, 2, ref total, ref passed);
            AssertCount(10f, false, true, 3, ref total, ref passed);
            AssertCount(36f, false, true, 10, ref total, ref passed);
            AssertCount(40f, true, true, 11, ref total, ref passed);
            // Sin estirar: la mayor cantidad que entra sin pisarse.
            AssertCount(10f, false, false, 2, ref total, ref passed);
            AssertCount(40f, true, false, 10, ref total, ref passed);

            // Línea recta abierta: de punta a punta, separación pareja exacta, vallas iguales y poco deformadas.
            foreach (float length in new[] { 5.5f, 7f, 10f, 36f, 53.7f })
            {
                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(length, false, len, gap, d => new Vector3(d, 0.3f, 0f));
                bool endToEnd = poses.Count > 0 && Mathf.Abs(poses[0].StartDistance) < 1e-3f &&
                                Mathf.Abs(poses[poses.Count - 1].EndDistance - length) < 1e-3f;
                bool evenGaps = true, sameWidth = true, mildScale = true;
                for (int i = 0; i < poses.Count; i++)
                {
                    float width = poses[i].EndDistance - poses[i].StartDistance;
                    if (Mathf.Abs(width - (poses[0].EndDistance - poses[0].StartDistance)) > 1e-3f) sameWidth = false;
                    if (poses[i].LengthScale < 0.7f || poses[i].LengthScale > 1.3f) mildScale = false;
                    if (i > 0 && Mathf.Abs(poses[i].StartDistance - poses[i - 1].EndDistance - gap) > 1e-3f) evenGaps = false;
                }

                Assert(endToEnd && evenGaps && sameWidth && mildScale,
                    $"Reparto en recta de {length:F1} m: {poses.Count} vallas de punta a punta, separación pareja de {gap:F2} m, " +
                    $"sin pisarse y con escala {(poses.Count > 0 ? poses[0].LengthScale : 0f):F2}",
                    ref total, ref passed);
            }

            // Posición en el punto medio del tramo y altura tomada de la spline.
            {
                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(10f, false, len, gap, d => new Vector3(d, 0.3f, 0f));
                bool ok = poses.Count == 3 &&
                          Mathf.Abs(poses[0].Position.x - 1.6333f) < 1e-3f && Mathf.Abs(poses[1].Position.x - 5f) < 1e-3f &&
                          Mathf.Abs(poses[2].Position.x - 8.3667f) < 1e-3f && poses.All(p => Mathf.Abs(p.Position.y - 0.3f) < 1e-4f);
                Assert(ok, "Cada valla se ubica en el punto medio de su tramo con la altura de la spline", ref total, ref passed);
            }

            // Una sola valla: ocupa toda la línea corta.
            {
                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(3f, false, len, gap, d => new Vector3(d, 0f, 0f));
                bool ok = poses.Count == 1 && Mathf.Abs(poses[0].StartDistance) < 1e-3f && Mathf.Abs(poses[0].EndDistance - 3f) < 1e-3f &&
                          Mathf.Abs(poses[0].LengthScale - 3f / len) < 1e-3f;
                Assert(ok, "Una línea más corta que la valla lleva una sola valla ajustada a su largo", ref total, ref passed);
            }

            // Sin estirar: ninguna valla se deforma y el sobrante se reparte como separación (nunca menor a la pedida).
            {
                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(10f, false, len, gap, d => new Vector3(d, 0f, 0f), false);
                bool ok = poses.Count == 2 && poses.All(p => Mathf.Abs(p.LengthScale - 1f) < 1e-4f) &&
                          Mathf.Abs(poses[0].StartDistance) < 1e-3f && Mathf.Abs(poses[1].EndDistance - 10f) < 1e-3f &&
                          poses[1].StartDistance - poses[0].EndDistance >= gap;
                Assert(ok, "Sin estirar: vallas sin deformar, de punta a punta y con la separación repartida", ref total, ref passed);
            }

            // Orientación: el eje X de la valla va a lo largo de la cuerda, sin pitch ni roll.
            {
                var direction = new Vector3(Mathf.Cos(30f * Mathf.Deg2Rad), 0f, Mathf.Sin(30f * Mathf.Deg2Rad));
                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(20f, false, len, gap, d => direction * d + Vector3.up * (0.05f * d));
                bool alongChord = poses.All(p => Vector3.Dot(p.Rotation * Vector3.right, direction) > 0.9999f);
                bool noTilt = poses.All(p => Vector3.Dot(p.Rotation * Vector3.up, Vector3.up) > 0.9999f);
                Assert(poses.Count > 0 && alongChord && noTilt,
                    "Las vallas se orientan a lo largo de la cuerda con yaw puro (sin pitch ni roll, aun con pendiente)", ref total, ref passed);
            }

            // Curva: cuarto de círculo de radio 10, cada valla sigue su cuerda.
            {
                const float radius = 10f;
                float arc = Mathf.PI * 0.5f * radius;
                Vector3 OnArc(float d)
                {
                    float a = d / radius;
                    return new Vector3(Mathf.Sin(a) * radius, 0f, (1f - Mathf.Cos(a)) * radius);
                }

                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(arc, false, len, gap, OnArc);
                bool chordOk = poses.Count > 2 && poses.All(p =>
                {
                    Vector3 chord = OnArc(p.EndDistance) - OnArc(p.StartDistance);
                    chord.y = 0f;
                    return Vector3.Dot(p.Rotation * Vector3.right, chord.normalized) > 0.9999f;
                });
                Assert(chordOk, $"En una curva cada valla sigue la cuerda de su tramo ({poses.Count} vallas)", ref total, ref passed);
            }

            // Spline cerrada: cuadrado de 10 m de lado; también queda separación en la costura.
            {
                const float side = 10f, perimeter = 40f;
                Vector3 OnSquare(float d)
                {
                    d %= perimeter;
                    if (d < side) return new Vector3(d, 0f, 0f);
                    if (d < 2f * side) return new Vector3(side, 0f, d - side);
                    if (d < 3f * side) return new Vector3(side - (d - 2f * side), 0f, side);
                    return new Vector3(0f, 0f, side - (d - 3f * side));
                }

                List<BarrierPose> poses = LessonBoundaryMath.ComputePoses(perimeter, true, len, gap, OnSquare);
                bool evenGaps = true;
                for (int i = 1; i < poses.Count; i++)
                {
                    if (Mathf.Abs(poses[i].StartDistance - poses[i - 1].EndDistance - gap) > 1e-3f) evenGaps = false;
                }

                // Costura: el final de la última más la separación da la vuelta completa hasta el inicio de la primera.
                bool seamGap = poses.Count > 1 &&
                               Mathf.Abs(poses[0].StartDistance + perimeter - poses[poses.Count - 1].EndDistance - gap) < 1e-3f;
                Assert(poses.Count == 11 && evenGaps && seamGap,
                    $"Spline cerrada: {poses.Count} vallas con separación pareja de {gap:F2} m, también en la costura", ref total, ref passed);
            }

            // Deduplicación: el segundo aviso dentro del enfriamiento se suprime, el que viene después no.
            Assert(LessonBoundaryMath.ShouldAlert(float.NegativeInfinity, 0f, 1f),
                "Dedup: el primer aviso pasa", ref total, ref passed);
            Assert(!LessonBoundaryMath.ShouldAlert(0f, 0.5f, 1f),
                "Dedup: el segundo aviso dentro del enfriamiento se suprime", ref total, ref passed);
            Assert(LessonBoundaryMath.ShouldAlert(0f, 1f, 1f) && LessonBoundaryMath.ShouldAlert(0f, 1.5f, 1f),
                "Dedup: el aviso posterior al enfriamiento pasa", ref total, ref passed);
            Assert(LessonBoundaryMath.ShouldAlert(5f, 0f, 1f),
                "Dedup: si el reloj retrocede (reinicio) el aviso pasa", ref total, ref passed);
        }

        private static void AssertCount(float length, bool closed, bool stretch, int expected, ref int total, ref int passed)
        {
            int count = LessonBoundaryMath.CountBarriers(length, closed, 3.6f, 0.1f, stretch);
            Assert(count == expected,
                $"CountBarriers({length:F1} m, {(closed ? "cerrada" : "abierta")}, {(stretch ? "estirando" : "sin estirar")}) = {expected} (obtuvo {count})",
                ref total, ref passed);
        }

        // ---------------------------------------------------------------- Componente LessonBoundary

        private static void VerifyBoundaryComponent(ref int total, ref int passed)
        {
            var barrierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LessonBoundarySetup.BARRIER_PREFAB_PATH);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("TestBoundary");
                SceneManager.MoveGameObjectToScene(go, preview);

                var container = go.AddComponent<SplineContainer>();
                if (container.Splines.Count == 0) container.AddSpline(new Spline());
                Spline spline = container.Splines[0];
                spline.Clear();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)), TangentMode.Linear);
                spline.Add(new BezierKnot(new float3(36f, 0f, 0f)), TangentMode.Linear);

                var boundary = go.AddComponent<LessonBoundary>();
                var so = new SerializedObject(boundary);
                so.FindProperty("m_BarrierPrefab").objectReferenceValue = barrierPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();

                // El ancho sale del prefab (collider sólido × escala): si se cambia la escala del prefab, la línea sigue bien.
                var solidAsset = barrierPrefab.GetComponent<BoxCollider>();
                float expectedWidth = solidAsset.size.x * Mathf.Abs(barrierPrefab.transform.lossyScale.x);
                Assert(Mathf.Abs(boundary.BarrierWidth - expectedWidth) < 1e-3f,
                    $"LessonBoundary: el ancho de la valla sale del prefab ({boundary.BarrierWidth:F2} m = collider × escala {barrierPrefab.transform.lossyScale.x:F2})",
                    ref total, ref passed);

                int expectedCount = LessonBoundaryMath.CountBarriers(36f, false, boundary.BarrierWidth, boundary.Gap, boundary.StretchToFit);
                bool computed = boundary.TryComputePoses(out List<BarrierPose> poses, out float length);
                Assert(computed && Mathf.Abs(length - 36f) < 0.01f && poses.Count == expectedCount,
                    $"LessonBoundary: spline de 36 m reparte {expectedCount} vallas (obtuvo {(poses != null ? poses.Count : 0)}, largo {length:F2})",
                    ref total, ref passed);

                // Idempotente: regenerar dos veces deja un solo contenedor con la misma cantidad de vallas.
                boundary.RebuildBarriers();
                boundary.RebuildBarriers();
                int containers = go.transform.Cast<Transform>().Count(c => c.name == LessonBoundary.CONTAINER_NAME);
                Transform barriers = go.transform.Find(LessonBoundary.CONTAINER_NAME);
                int children = barriers != null ? barriers.childCount : -1;
                Assert(containers == 1 && children == expectedCount,
                    $"LessonBoundary: reconstruir dos veces deja 1 contenedor con {expectedCount} vallas (contenedores {containers}, vallas {children})",
                    ref total, ref passed);

                int barrierScripts = go.GetComponentsInChildren<LessonBarrier>(true).Length;
                bool prefabInstances = barriers != null && barriers.Cast<Transform>().All(c =>
                    PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject) &&
                    AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject)) == LessonBoundarySetup.BARRIER_PREFAB_PATH);
                Assert(barrierScripts == expectedCount && prefabInstances,
                    "LessonBoundary: las vallas son instancias del prefab LessonBarrier, cada una con su aviso", ref total, ref passed);

                // Físicamente: los sólidos no se pisan y entre dos vecinos queda la separación pedida.
                if (barriers != null && barriers.childCount >= 2)
                {
                    float minGap = float.MaxValue, maxGap = float.MinValue;
                    for (int i = 1; i < barriers.childCount; i++)
                    {
                        Transform a = barriers.GetChild(i - 1), b = barriers.GetChild(i);
                        var solidA = a.GetComponent<BoxCollider>();
                        var solidB = b.GetComponent<BoxCollider>();
                        float halfA = solidA.size.x * Mathf.Abs(a.lossyScale.x) * 0.5f;
                        float halfB = solidB.size.x * Mathf.Abs(b.lossyScale.x) * 0.5f;
                        float between = Vector3.Distance(a.position, b.position) - halfA - halfB;
                        minGap = Mathf.Min(minGap, between);
                        maxGap = Mathf.Max(maxGap, between);
                    }

                    Assert(minGap > 0f && Mathf.Abs(minGap - boundary.Gap) < 0.02f && Mathf.Abs(maxGap - boundary.Gap) < 0.02f,
                        $"LessonBoundary: las vallas generadas no se tocan y quedan separadas {minGap:F2}–{maxGap:F2} m (pedido {boundary.Gap:F2} m)",
                        ref total, ref passed);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // ---------------------------------------------------------------- Escena de demo

        private static void VerifyDemoScene(ref int total, ref int passed)
        {
            string scenePath = LessonBoundarySetup.DEMO_SCENE_PATH;
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    Assert(false, $"La escena {scenePath} debe existir", ref total, ref passed);
                    return;
                }

                // Aditiva: nunca se reemplaza una escena abierta.
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                Physics.SyncTransforms();

                GameObject group = LessonBoundarySetup.FindRootByName(scene, LessonBoundarySetup.BOUNDARIES_GROUP_NAME);
                Assert(group != null, $"CityWithBarriers debe tener el grupo {LessonBoundarySetup.BOUNDARIES_GROUP_NAME}", ref total, ref passed);
                if (group == null) return;

                var boundaries = group.GetComponentsInChildren<LessonBoundary>(true);
                Assert(boundaries.Length >= 1, $"El grupo debe tener al menos una línea de barreras ({boundaries.Length})", ref total, ref passed);

                Transform npcGroup = LessonBoundarySetup.FindRootByName(scene, "NPC_Demo")?.transform;
                VehicleController car = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<VehicleController>(true))
                    .FirstOrDefault(v => v.name == "Car08");

                foreach (LessonBoundary boundary in boundaries)
                {
                    string label = boundary.name;
                    bool computed = boundary.TryComputePoses(out List<BarrierPose> poses, out float length);
                    Transform barriers = boundary.transform.Find(LessonBoundary.CONTAINER_NAME);

                    Assert(computed && barriers != null && barriers.childCount == poses.Count && poses.Count > 0,
                        $"{label}: las vallas generadas están al día con la spline y los parámetros ({(barriers != null ? barriers.childCount : 0)} en escena, {(poses != null ? poses.Count : 0)} esperadas; si no coincide, usá Reconstruir barreras)",
                        ref total, ref passed);

                    Assert(boundary.BarrierPrefab != null &&
                           AssetDatabase.GetAssetPath(boundary.BarrierPrefab) == LessonBoundarySetup.BARRIER_PREFAB_PATH,
                        $"{label}: usa el prefab LessonBarrier", ref total, ref passed);
                    if (!computed || barriers == null) continue;

                    // Los chequeos geométricos de abajo son de la demo (la línea que corta la calle de cordón a
                    // cordón); las líneas que diseñe el usuario solo se validan en lo genérico.
                    if (boundary.name == LessonBoundarySetup.DEMO_BOUNDARY_NAME)
                    {
                        // La línea corta la calle de cordón a cordón: cada extremo termina contra un muro de cordón.
                        var container = boundary.GetComponent<SplineContainer>();
                        Spline spline = container.Splines[boundary.SplineIndex];
                        Vector3 first = container.transform.TransformPoint((Vector3)spline[0].Position);
                        Vector3 last = container.transform.TransformPoint((Vector3)spline[spline.Count - 1].Position);
                        Vector3 direction = (last - first).normalized;
                        Assert(!spline.Closed && spline.Count == 2, $"{label}: spline abierta de dos nudos", ref total, ref passed);

                        float gapStart = DistanceToCurbWall(first + direction * 0.5f, -direction, boundary.transform);
                        float gapEnd = DistanceToCurbWall(last - direction * 0.5f, direction, boundary.transform);
                        Assert(gapStart >= 0f && gapStart <= 0.65f && gapEnd >= 0f && gapEnd <= 0.65f,
                            $"{label}: cada extremo llega al cordón (distancia a los muros {gapStart:F2} y {gapEnd:F2} m)",
                            ref total, ref passed);

                        // Largo de la línea: de cordón a cordón (≈ 14 m), sin invadir otra calle.
                        Assert(Mathf.Abs(length - 14f) < 0.5f, $"{label}: largo de la línea ≈ 14 m ({length:F1} m)", ref total, ref passed);

                        // No cruza ninguna ruta de NPC_Demo.
                        float minRoute = float.MaxValue;
                        if (npcGroup != null)
                        {
                            foreach (SplineContainer route in npcGroup.GetComponentsInChildren<SplineContainer>(true))
                            {
                                minRoute = Mathf.Min(minRoute, MinDistanceToRoute(route, first, last));
                            }
                        }

                        Assert(minRoute > 1.5f, $"{label}: a más de 1.5 m de toda ruta de NPC_Demo ({minRoute:F1} m)", ref total, ref passed);

                        // Lejos del punto de inicio del Car08.
                        if (car != null)
                        {
                            float away = Vector3.Distance(new Vector3(car.transform.position.x, 0f, car.transform.position.z),
                                new Vector3((first.x + last.x) * 0.5f, 0f, (first.z + last.z) * 0.5f));
                            Assert(away > 30f, $"{label}: lejos del inicio del Car08 ({away:F0} m)", ref total, ref passed);
                        }
                    }

                    // Ningún prop o edificio dentro de las vallas.
                    var intruders = new List<string>();
                    foreach (BoxCollider solid in barriers.GetComponentsInChildren<BoxCollider>(true).Where(b => !b.isTrigger))
                    {
                        Vector3 center = solid.transform.TransformPoint(solid.center);
                        Vector3 half = Vector3.Scale(solid.size * 0.5f, solid.transform.lossyScale) * 0.95f;
                        foreach (Collider hit in Physics.OverlapBox(center, half, solid.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                        {
                            // Otras escenas abiertas (p. ej. GPSNavigationDemo, que copia este grupo) comparten la física del Editor.
                            if (hit.gameObject.scene != scene) continue;
                            if (hit.transform.IsChildOf(boundary.transform)) continue;
                            if (hit.transform.root.name.StartsWith("Road")) continue;
                            intruders.Add(hit.transform.root.name);
                        }
                    }

                    Assert(intruders.Count == 0,
                        $"{label}: ningún collider ajeno dentro de las vallas{(intruders.Count == 0 ? "" : " (" + string.Join(", ", intruders.Distinct()) + ")")}",
                        ref total, ref passed);
                }
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // Distancia al primer muro de cordón en la dirección dada (ignora lo que cuelga de la propia línea);
        // -1 si no hay ninguno en 1.5 m. El rayo va a 1 m de altura, donde ya no hay malla de calle.
        private static float DistanceToCurbWall(Vector3 origin, Vector3 direction, Transform boundary)
        {
            origin.y = 1f;
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                Transform t = hit.collider.transform;
                if (t.IsChildOf(boundary)) continue;

                // Un muro de cordón es una caja sobre CurbWalls o sobre uno de sus hijos (tramos diagonales).
                bool isCurbWall = t.name == LessonBoundarySetup.CURB_WALLS_NAME ||
                                  (t.parent != null && t.parent.name == LessonBoundarySetup.CURB_WALLS_NAME);
                if (!isCurbWall) continue;

                best = Mathf.Min(best, hit.distance);
            }

            return best == float.MaxValue ? -1f : best;
        }

        // Distancia mínima en XZ entre una ruta muestreada cada 0.5 m y el segmento a–b.
        private static float MinDistanceToRoute(SplineContainer route, Vector3 a, Vector3 b)
        {
            float min = float.MaxValue;
            if (route.Splines.Count == 0) return min;

            float length = route.CalculateLength(0);
            for (float d = 0f; d <= length; d += 0.5f)
            {
                float t = route.Splines[0].ConvertIndexUnit(d / (length / route.Splines[0].GetLength()), PathIndexUnit.Distance, PathIndexUnit.Normalized);
                Vector3 p = route.EvaluatePosition(0, t);
                min = Mathf.Min(min, DistancePointToSegmentXZ(p, a, b));
            }

            return min;
        }

        private static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var pa = new Vector2(p.x - a.x, p.z - a.z);
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(pa, ab) / ab.sqrMagnitude);
            return (pa - ab * t).magnitude;
        }

        // ---------------------------------------------------------------- Suites existentes

        // NPCVerification vuelve a correr Cockpit, VehicleLighting, SoundIntegration y TurnSignal, así que su resultado
        // final cubre las cinco suites.
        private static void VerifyExistingSuites(ref int total, ref int passed)
        {
            // Las suites existentes (Sound, Haptics) guardan la escena abierta si estaba sucia: si hay cambios
            // sin guardar no se corren, para no escribir en disco trabajo del usuario sin que lo decida.
            var dirtyScenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty) dirtyScenes.Add(open.name);
            }

            if (dirtyScenes.Count > 0)
            {
                Debug.LogWarning($"[LessonBoundaryVerification] ⚠️ Escenas con cambios sin guardar ({string.Join(", ", dirtyScenes)}): " +
                                 "no se corren las suites existentes porque guardan la escena abierta. Guardá la escena y volvé a correr.");
                return;
            }

            int suitePassed = 0, suiteTotal = 0;
            void OnLog(string message, string stack, LogType type)
            {
                Match match = Regex.Match(message, @"RESULTADO FINAL:\s*(\d+)/(\d+)");
                if (match.Success)
                {
                    suitePassed = int.Parse(match.Groups[1].Value);
                    suiteTotal = int.Parse(match.Groups[2].Value);
                }
            }

            bool menuFound;
            Application.logMessageReceived += OnLog;
            try
            {
                menuFound = EditorApplication.ExecuteMenuItem("Tools/Primer Volante/Run NPC Verification");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert(menuFound, "El MenuItem Run NPC Verification debe existir", ref total, ref passed);
            Assert(menuFound && suiteTotal > 0 && suitePassed == suiteTotal,
                $"NPCVerification (y las suites Cockpit, VehicleLighting, SoundIntegration y TurnSignal) sigue pasando ({suitePassed}/{suiteTotal})",
                ref total, ref passed);
        }

        // ---------------------------------------------------------------- Análisis de fugas (informativo)

        // Grilla de 1 m sobre x ∈ [−20, 260], z ∈ [−40, 240].
        private const float k_LeakXMin = -20f, k_LeakXMax = 260f, k_LeakZMin = -40f, k_LeakZMax = 240f;

        private enum CellKind : byte
        {
            Other = 0,      // sin suelo conocido o bloqueada
            Road = 1,       // calzada: asfalto de una pieza Road* a y < 0.1, sin muro ni obstáculo
            Sidewalk = 2,   // vereda (pieza Road* a y >= 0.1)
            Fallback = 3,   // solo el Ground_FallbackCollider debajo y nada que bloquee: el auto puede dejar la red
        }

        /// <summary>
        /// Recorre la grilla de la escena activa y lista las fugas: celdas de calzada alcanzables desde el Car08 cuyo
        /// vecino solo tiene el <c>Ground_FallbackCollider</c> debajo. No falla: es un informe para decidir qué cerrar.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Run Road Leak Analysis")]
        public static void RunLeakAnalysis()
        {
            Debug.Log("[LessonBoundaryVerification] 🕳️ Analizando fugas de la red de calles...");
            AnalyzeRoadLeaks();
        }

        private static void AnalyzeRoadLeaks()
        {
            Physics.SyncTransforms();

            int nx = Mathf.RoundToInt(k_LeakXMax - k_LeakXMin);
            int nz = Mathf.RoundToInt(k_LeakZMax - k_LeakZMin);
            var kind = new CellKind[nx * nz];
            var pieceName = new string[nx * nz];

            var overlap = new Collider[16];
            var halfExtents = new Vector3(0.45f, 0.75f, 0.45f);

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    float x = k_LeakXMin + ix + 0.5f;
                    float z = k_LeakZMin + iz + 0.5f;
                    int index = ix * nz + iz;

                    // Suelo: primer collider no trigger bajo la celda.
                    if (!Physics.Raycast(new Vector3(x, 0.5f, z), Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    string rootName = hit.collider.transform.root.name;
                    bool isRoad = rootName.StartsWith("Road");
                    bool isFallback = rootName.StartsWith("Ground");
                    if (!isRoad && !isFallback) continue;

                    // Obstáculos entre 0.3 y 1.8 m: muros de cordón, edificios, vallas, vehículos.
                    int count = Physics.OverlapBoxNonAlloc(new Vector3(x, 1.05f, z), halfExtents, overlap, Quaternion.identity, ~0,
                        QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int i = 0; i < count && !blocked; i++)
                    {
                        Collider c = overlap[i];
                        string name = c.transform.root.name;

                        // El piso de la calle (MeshCollider de la raíz Road*) y el suelo de respaldo no cuentan.
                        if (c is MeshCollider && name.StartsWith("Road")) continue;
                        if (name.StartsWith("Ground")) continue;

                        // El propio auto del jugador y el rig XR no son obstáculos.
                        VehicleController owner = c.GetComponentInParent<VehicleController>();
                        if (owner != null && owner.name == "Car08") continue;
                        if (name.Contains("XR")) continue;

                        blocked = true;
                    }

                    if (blocked) continue;

                    if (isRoad)
                    {
                        kind[index] = hit.point.y < RoadCurbWalls.GROUND_MAX_HEIGHT ? CellKind.Road : CellKind.Sidewalk;
                        pieceName[index] = rootName;
                    }
                    else
                    {
                        kind[index] = CellKind.Fallback;
                    }
                }
            }

            // Celda de inicio: la del Car08.
            VehicleController car = Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include)
                .FirstOrDefault(v => v.name == "Car08");
            if (car == null)
            {
                Debug.LogWarning("[LessonBoundaryVerification] ⚠️ No hay Car08 en la escena activa; no se puede rellenar desde su celda.");
                return;
            }

            int startX = Mathf.FloorToInt(car.transform.position.x - k_LeakXMin);
            int startZ = Mathf.FloorToInt(car.transform.position.z - k_LeakZMin);
            if (startX < 0 || startX >= nx || startZ < 0 || startZ >= nz || kind[startX * nz + startZ] != CellKind.Road)
            {
                Debug.LogWarning($"[LessonBoundaryVerification] ⚠️ La celda del Car08 ({startX}, {startZ}) no es calzada libre; no se rellena.");
                return;
            }

            // Relleno por inundación (4 vecinos).
            var reached = new bool[nx * nz];
            var queue = new Queue<int>();
            queue.Enqueue(startX * nz + startZ);
            reached[startX * nz + startZ] = true;
            var neighbors = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

            // Fuga: (celda de salida sobre suelo de respaldo, pieza de calle desde la que se llega).
            var exits = new Dictionary<int, string>();
            int reachedCount = 0;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                reachedCount++;
                int cx = current / nz, cz = current % nz;

                foreach (var (dx, dz) in neighbors)
                {
                    int px = cx + dx, pz = cz + dz;
                    if (px < 0 || px >= nx || pz < 0 || pz >= nz) continue;

                    int next = px * nz + pz;
                    CellKind nextKind = kind[next];

                    if (nextKind == CellKind.Fallback)
                    {
                        if (!exits.ContainsKey(next)) exits[next] = pieceName[current];
                    }
                    else if (nextKind == CellKind.Road && !reached[next])
                    {
                        reached[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            int roadCells = kind.Count(k => k == CellKind.Road);
            Debug.Log($"[LessonBoundaryVerification] 🕳️ Calzada libre: {roadCells} celdas de 1 m², {reachedCount} alcanzables desde el Car08.");

            // Agrupar las salidas contiguas (8 vecinos).
            var clusters = new List<List<int>>();
            var assigned = new HashSet<int>();
            foreach (int cell in exits.Keys.OrderBy(c => c))
            {
                if (assigned.Contains(cell)) continue;

                var cluster = new List<int>();
                var open = new Queue<int>();
                open.Enqueue(cell);
                assigned.Add(cell);
                while (open.Count > 0)
                {
                    int c = open.Dequeue();
                    cluster.Add(c);
                    int cx = c / nz, cz = c % nz;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int px = cx + dx, pz = cz + dz;
                            if (px < 0 || px >= nx || pz < 0 || pz >= nz) continue;
                            int n = px * nz + pz;
                            if (exits.ContainsKey(n) && assigned.Add(n)) open.Enqueue(n);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            Debug.Log($"[LessonBoundaryVerification] 🕳️ FUGAS: {exits.Count} celdas de salida en {clusters.Count} grupos " +
                      "(celdas con solo el Ground_FallbackCollider debajo, adyacentes a calzada alcanzable).");

            foreach (List<int> cluster in clusters.OrderByDescending(c => c.Count).Take(60))
            {
                int minX = cluster.Min(c => c / nz), maxX = cluster.Max(c => c / nz);
                int minZ = cluster.Min(c => c % nz), maxZ = cluster.Max(c => c % nz);
                string pieces = string.Join(", ", cluster.Select(c => exits[c]).Distinct().OrderBy(n => n).Take(4));
                Debug.Log($"[LessonBoundaryVerification]    · x[{k_LeakXMin + minX:F0}..{k_LeakXMin + maxX + 1:F0}] " +
                          $"z[{k_LeakZMin + minZ:F0}..{k_LeakZMin + maxZ + 1:F0}] {cluster.Count} celdas · desde: {pieces}");
            }

            if (clusters.Count > 60) Debug.Log($"[LessonBoundaryVerification]    … y {clusters.Count - 60} grupos más.");

            // Resumen por tipo de pieza responsable.
            var perType = exits.Values
                .GroupBy(n => System.Text.RegularExpressions.Regex.Replace(n, @"\s*\(\d+\)$", ""))
                .OrderByDescending(g => g.Count());
            foreach (var group in perType)
            {
                Debug.Log($"[LessonBoundaryVerification]    Σ {group.Key}: {group.Count()} celdas de salida");
            }
        }

        // ---------------------------------------------------------------- Helpers

        private static GameObject FindRoadPrefab(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{LessonBoundarySetup.ROADS_FOLDER}/{name}.prefab");
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
