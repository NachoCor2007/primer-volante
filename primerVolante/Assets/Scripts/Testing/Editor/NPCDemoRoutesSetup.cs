using System.Collections.Generic;
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
    /// Construye en CityWithCar el grupo NPC_Demo: 2 autos que recorren una manzana por el carril derecho y
    /// 2 peatones (uno cruza una senda peatonal, otro camina por la vereda), todos sobre rutas Spline.
    /// Idempotente: recrea el grupo en cada corrida.
    /// </summary>
    public static class NPCDemoRoutesSetup
    {
        public const string SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityWithCar.unity";
        public const string GROUP_NAME = "NPC_Demo";

        // Geometría de la ciudad (ver documents/prompts): asfalto a y = 0, veredas a y = 0.2,
        // carril de 7 m con el centro a 3.5 m del eje, mano derecha.
        private const float LANE_OFFSET = 3.5f;
        private const float SIDEWALK_HEIGHT = 0.2f;
        private const float CAR_CORNER_RADIUS = 6f;

        // Manzana de los autos: ejes de calle en x = 60 y 120, z = 0 y 60 (lazo horario por el carril interior).
        private const float BLOCK_X0 = 60f;
        private const float BLOCK_X1 = 120f;
        private const float BLOCK_Z0 = 0f;
        private const float BLOCK_Z1 = 60f;

        // Caja con la que se busca autos estacionados sobre la ruta (un Car NPC mide 2.04 × 4.36 m).
        private static readonly Vector3 s_VehicleProbeHalfExtents = new Vector3(1.05f, 0.7f, 2.25f);
        private const float VEHICLE_PROBE_CENTER_Y = 1.0f;

        [MenuItem("Tools/Primer Volante/Setup NPC Demo Routes (CityWithCar)")]
        public static void SetupDemoRoutes()
        {
            Scene scene = EnsureSceneLoaded();
            if (!scene.IsValid() || !scene.isLoaded) return;

            Transform existing = FindGroup(scene);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var group = new GameObject(GROUP_NAME);
            SceneManager.MoveGameObjectToScene(group, scene);

            // --- Rutas ---------------------------------------------------------------------------
            Vector3[] carLoop =
            {
                new Vector3(BLOCK_X0 + LANE_OFFSET, 0f, BLOCK_Z0 + LANE_OFFSET),
                new Vector3(BLOCK_X0 + LANE_OFFSET, 0f, BLOCK_Z1 - LANE_OFFSET),
                new Vector3(BLOCK_X1 - LANE_OFFSET, 0f, BLOCK_Z1 - LANE_OFFSET),
                new Vector3(BLOCK_X1 - LANE_OFFSET, 0f, BLOCK_Z0 + LANE_OFFSET),
            };
            SplineContainer carRoute = CreateRoute(group.transform, "Route_Vehicle_Block",
                BuildRoundedLoop(carLoop, CAR_CORNER_RADIUS, null));

            // Peatón 1: cruza de ida y vuelta la senda del brazo oeste del cruce (60, 60).
            // La senda ocupa x = 50.4..53.4 y z = 53.4..66.6; las esquinas de vereda quedan a y = 0.2.
            Vector3[] crossing =
            {
                new Vector3(52.6f, 0f, 51.5f), new Vector3(52.6f, 0f, 53.4f), new Vector3(52.6f, 0f, 66.6f), new Vector3(52.6f, 0f, 68.5f),
                new Vector3(51.2f, 0f, 68.5f), new Vector3(51.2f, 0f, 66.6f), new Vector3(51.2f, 0f, 53.4f), new Vector3(51.2f, 0f, 51.5f),
            };
            SplineContainer crossingRoute = CreateRoute(group.transform, "Route_Pedestrian_Crossing",
                BuildRoundedLoop(crossing, 0.7f, (x, z) => Mathf.Abs(z - 60f) <= 6.65f ? 0f : SIDEWALK_HEIGHT));

            // Peatón 2: ida y vuelta por la vereda oeste de la calle x = 60 (veredas de x = 50 a 53), al norte del cruce.
            Vector3[] sidewalk =
            {
                new Vector3(52.2f, SIDEWALK_HEIGHT, 74f), new Vector3(52.2f, SIDEWALK_HEIGHT, 88f),
                new Vector3(50.8f, SIDEWALK_HEIGHT, 88f), new Vector3(50.8f, SIDEWALK_HEIGHT, 74f),
            };
            SplineContainer sidewalkRoute = CreateRoute(group.transform, "Route_Pedestrian_Sidewalk",
                BuildRoundedLoop(sidewalk, 0.7f, (x, z) => SIDEWALK_HEIGHT));

            // --- Autos estacionados que bloquean la manzana --------------------------------------------
            DeactivateBlockingVehicles(carRoute);

            // --- NPC ------------------------------------------------------------------------------------
            float carLength = carRoute.CalculateLength(0);
            PlaceNPC(group.transform, scene, NPCPrefabSetup.VEHICLE_OUTPUT_FOLDER + "/NPC_Car_color01.prefab",
                carRoute, 0f);
            PlaceNPC(group.transform, scene, NPCPrefabSetup.VEHICLE_OUTPUT_FOLDER + "/NPC_Taxi.prefab",
                carRoute, carLength * 0.5f);
            PlaceNPC(group.transform, scene, NPCPrefabSetup.PEDESTRIAN_OUTPUT_FOLDER + "/NPC_Pedestrian_02.prefab",
                crossingRoute, 0f);
            PlaceNPC(group.transform, scene, NPCPrefabSetup.PEDESTRIAN_OUTPUT_FOLDER + "/NPC_Pedestrian_03.prefab",
                sidewalkRoute, 0f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[NPCDemoRoutesSetup] ✅ Grupo {GROUP_NAME} creado en {SCENE_PATH} (ruta de autos: {carLength:F1} m).");
        }

        // ------------------------------------------------------------------ Escena

        /// <summary>
        /// Devuelve la escena CityWithCar cargada. Si no está cargada la abre en modo Single solo cuando
        /// ninguna escena abierta tiene cambios sin guardar; si algo está sucio no toca nada.
        /// </summary>
        private static Scene EnsureSceneLoaded()
        {
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty)
                {
                    Debug.LogError($"[NPCDemoRoutesSetup] ❌ {SCENE_PATH} tiene cambios sin guardar. Guardalos o descartalos y volvé a correr el setup.");
                    return default;
                }

                return scene;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogError($"[NPCDemoRoutesSetup] ❌ La escena abierta '{open.path}' tiene cambios sin guardar; no se abre {SCENE_PATH}.");
                    return default;
                }
            }

            return EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
        }

        /// <summary>
        /// Raíz NPC_Demo de la escena, o null.
        /// </summary>
        public static Transform FindGroup(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == GROUP_NAME) return root.transform;
            }

            return null;
        }

        // ------------------------------------------------------------------ Rutas

        private static SplineContainer CreateRoute(Transform parent, string name, Spline spline)
        {
            var routeObject = new GameObject(name);
            routeObject.transform.SetParent(parent, false);

            var container = routeObject.AddComponent<SplineContainer>();
            container.Spline = spline;
            return container;
        }

        /// <summary>
        /// Spline cerrada que recorre los vértices (en el orden dado) con las esquinas redondeadas por arcos
        /// circulares. Los vértices colineales se conservan como nudos para controlar la altura con
        /// <paramref name="heightAt"/> (x, z → y). El radio efectivo se limita a la mitad del tramo entre esquinas.
        /// El contenedor debe estar en el origen sin rotación ni escala: los nudos quedan en espacio de mundo.
        /// </summary>
        public static Spline BuildRoundedLoop(IList<Vector3> vertices, float cornerRadius, System.Func<float, float, float> heightAt)
        {
            int n = vertices.Count;
            var knots = new List<RouteKnot>();

            // Distancia horizontal a la esquina anterior/siguiente (saltando vértices colineales).
            var corner = new bool[n];
            var dirIn = new Vector3[n];
            var dirOut = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = vertices[(i - 1 + n) % n];
                Vector3 next = vertices[(i + 1) % n];
                dirIn[i] = Flat(vertices[i] - prev).normalized;
                dirOut[i] = Flat(next - vertices[i]).normalized;
                corner[i] = Vector3.Angle(dirIn[i], dirOut[i]) > 1f;
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 p = vertices[i];
                if (!corner[i])
                {
                    AddKnot(knots, new RouteKnot { Position = p, DirIn = dirIn[i], DirOut = dirOut[i] });
                    continue;
                }

                float theta = Vector3.Angle(dirIn[i], dirOut[i]) * Mathf.Deg2Rad;
                float cut = cornerRadius * Mathf.Tan(theta * 0.5f);
                float limit = Mathf.Min(DistanceToCorner(vertices, corner, i, -1), DistanceToCorner(vertices, corner, i, +1)) * 0.5f;
                cut = Mathf.Min(cut, limit);
                float radius = cut / Mathf.Tan(theta * 0.5f);
                float handle = 4f / 3f * Mathf.Tan(theta * 0.25f) * radius;

                AddKnot(knots, new RouteKnot
                {
                    Position = p - dirIn[i] * cut,
                    DirIn = dirIn[i],
                    DirOut = dirIn[i],
                    ArcAfter = true,
                    ArcHandle = handle,
                });
                AddKnot(knots, new RouteKnot { Position = p + dirOut[i] * cut, DirIn = dirOut[i], DirOut = dirOut[i] });
            }

            // El último y el primer nudo pueden coincidir (arcos contiguos): se fusionan.
            if (knots.Count > 1 && Flat(knots[knots.Count - 1].Position - knots[0].Position).sqrMagnitude < 1e-6f)
            {
                RouteKnot last = knots[knots.Count - 1];
                RouteKnot first = knots[0];
                first.DirIn = last.DirIn;
                knots[0] = first;
                knots.RemoveAt(knots.Count - 1);
            }

            // Altura de cada nudo.
            for (int i = 0; i < knots.Count; i++)
            {
                RouteKnot k = knots[i];
                if (heightAt != null) k.Position.y = heightAt(k.Position.x, k.Position.z);
                knots[i] = k;
            }

            var bezier = new List<BezierKnot>(knots.Count);
            for (int i = 0; i < knots.Count; i++)
            {
                RouteKnot current = knots[i];
                RouteKnot previous = knots[(i - 1 + knots.Count) % knots.Count];
                RouteKnot next = knots[(i + 1) % knots.Count];

                Vector3 tangentOut = current.ArcAfter
                    ? current.DirOut * current.ArcHandle
                    : (next.Position - current.Position) / 3f;
                Vector3 tangentIn = previous.ArcAfter
                    ? -current.DirIn * previous.ArcHandle
                    : (previous.Position - current.Position) / 3f;

                bezier.Add(new BezierKnot((float3)current.Position, (float3)tangentIn, (float3)tangentOut, quaternion.identity));
            }

            var spline = new Spline(bezier, true);
            spline.SetTangentMode(TangentMode.Broken);
            return spline;
        }

        private struct RouteKnot
        {
            public Vector3 Position;
            public Vector3 DirIn;
            public Vector3 DirOut;
            public bool ArcAfter;
            public float ArcHandle;
        }

        private static void AddKnot(List<RouteKnot> knots, RouteKnot knot)
        {
            if (knots.Count > 0)
            {
                RouteKnot last = knots[knots.Count - 1];
                if (Flat(last.Position - knot.Position).sqrMagnitude < 1e-6f)
                {
                    // Dos arcos que se tocan en un punto: un único nudo.
                    last.DirOut = knot.DirOut;
                    last.ArcAfter = knot.ArcAfter;
                    last.ArcHandle = knot.ArcHandle;
                    knots[knots.Count - 1] = last;
                    return;
                }
            }

            knots.Add(knot);
        }

        private static float DistanceToCorner(IList<Vector3> vertices, bool[] corner, int index, int step)
        {
            int n = vertices.Count;
            float distance = 0f;
            int current = index;
            for (int guard = 0; guard < n; guard++)
            {
                int next = (current + step + n) % n;
                distance += Flat(vertices[next] - vertices[current]).magnitude;
                if (corner[next]) return distance;
                current = next;
            }

            return distance;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// Muestrea una ruta cada <paramref name="step"/> metros (espacio de mundo): posición y orientación
        /// (yaw de la tangente).
        /// </summary>
        public static List<(Vector3 position, Quaternion rotation)> SampleRoute(SplineContainer container, int splineIndex, float step)
        {
            var samples = new List<(Vector3, Quaternion)>();
            Spline spline = container.Splines[splineIndex];
            float worldLength = container.CalculateLength(splineIndex);
            float localLength = spline.GetLength();
            if (worldLength <= 0f || localLength <= 0f) return samples;

            float scale = worldLength / localLength;
            for (float distance = 0f; distance < worldLength; distance += step)
            {
                float t = spline.ConvertIndexUnit(distance / scale, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                container.Evaluate(splineIndex, t, out float3 position, out float3 tangent, out float3 _);
                Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
                Quaternion rotation = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
                samples.Add((position, rotation));
            }

            return samples;
        }

        // ------------------------------------------------------------------ Autos estacionados

        /// <summary>
        /// Colliders (fuera de calles, suelo y NPC_Demo) que se superponen con la ruta de un auto. Devuelve la
        /// lista de GameObjects raíz distintos.
        /// </summary>
        public static List<GameObject> FindRouteObstacles(SplineContainer route)
        {
            var obstacles = new List<GameObject>();
            var seen = new HashSet<GameObject>();

            foreach (var (position, rotation) in SampleRoute(route, 0, 1f))
            {
                Collider[] hits = Physics.OverlapBox(
                    position + Vector3.up * VEHICLE_PROBE_CENTER_Y,
                    s_VehicleProbeHalfExtents,
                    rotation,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                foreach (Collider hit in hits)
                {
                    GameObject root = hit.transform.root.gameObject;
                    if (root.name.StartsWith("Road") || root.name.StartsWith("Ground") || root.name == GROUP_NAME) continue;
                    if (seen.Add(root)) obstacles.Add(root);
                }
            }

            return obstacles;
        }

        /// <summary>
        /// Desactiva los vehículos decorativos (raíz "Vehicle_*") que bloquean la ruta y lista cualquier otro
        /// obstáculo. Es idempotente: los ya desactivados no vuelven a aparecer como obstáculos.
        /// </summary>
        private static void DeactivateBlockingVehicles(SplineContainer route)
        {
            List<GameObject> obstacles = FindRouteObstacles(route);
            int deactivated = 0;
            foreach (GameObject obstacle in obstacles)
            {
                if (obstacle.name.StartsWith("Vehicle_"))
                {
                    obstacle.SetActive(false);
                    deactivated++;
                    Debug.Log($"[NPCDemoRoutesSetup] 🚧 Auto decorativo desactivado por bloquear la ruta: {obstacle.name} ({obstacle.transform.position})");
                }
                else
                {
                    Debug.LogWarning($"[NPCDemoRoutesSetup] ⚠️ Obstáculo no vehicular sobre la ruta: {obstacle.name}");
                }
            }

            Debug.Log($"[NPCDemoRoutesSetup] 🚧 {deactivated} auto(s) decorativo(s) desactivado(s).");
        }

        // ------------------------------------------------------------------ NPC

        private static void PlaceNPC(Transform group, Scene scene, string prefabPath, SplineContainer route, float initialDistance)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[NPCDemoRoutesSetup] ❌ Falta el prefab {prefabPath}. Corré 'Setup NPC Prefabs' primero.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(group, false);

            var follower = instance.GetComponent<NPCPathFollower>();
            var so = new SerializedObject(follower);
            so.FindProperty("m_Path").objectReferenceValue = route;
            so.FindProperty("m_SplineIndex").intValue = 0;
            so.FindProperty("m_EndBehavior").enumValueIndex = (int)NPCPathEndBehavior.Loop;
            so.FindProperty("m_InitialDistance").floatValue = initialDistance;
            so.FindProperty("m_PlayOnStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            follower.SetPath(route, 0);
        }
    }
}
