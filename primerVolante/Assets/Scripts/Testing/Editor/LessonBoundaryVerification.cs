using System.Collections.Generic;
using System.Linq;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            VerifyCarDetection(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
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
