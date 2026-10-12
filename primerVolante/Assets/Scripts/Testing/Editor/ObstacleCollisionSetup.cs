using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de US33 (#21): sensor de contactos en el auto del jugador, detectores en
    /// el cono, los NPC y los autos estacionados, y la zona de práctica de la escena
    /// <c>CityWithCollisionDetectors</c>. Cada paso se puede volver a correr sin duplicar nada.
    /// </summary>
    public static class ObstacleCollisionSetup
    {
        private const string CAR_FOLDER = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs";
        public const string CAR08_PATH = CAR_FOLDER + "/Car08.prefab";
        public const string CAR07_PATH = CAR_FOLDER + "/Car07.prefab";

        /// <summary>Nombre del hijo con la <c>AudioSource</c> del impacto.</summary>
        public const string IMPACT_SOURCE_NAME = "Audio_ObstacleImpact";

        // Posición local de la fuente: aproximadamente el centro de la carrocería. En cada impacto se mueve al punto de contacto.
        private static readonly Vector3 s_ImpactSourceLocalPosition = new Vector3(0f, 0.7f, 0f);

        // ------------------------------------------------------------------ Auto del jugador

        /// <summary>
        /// Agrega o actualiza <see cref="ObstacleContactSensor"/> en <c>Car08</c> y <c>Car07</c>, con la
        /// <c>AudioSource</c> hija, el grupo <c>Road</c> del mixer y el perfil de audio asignados.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Car Obstacle Sensor")]
        public static void SetupCarObstacleSensor()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ObstacleImpactAudioProfile>(ObstacleImpactAudioPlaceholderGenerator.PROFILE_PATH);
            if (profile == null)
            {
                Debug.LogError($"[ObstacleCollisionSetup] ❌ Falta el perfil {ObstacleImpactAudioPlaceholderGenerator.PROFILE_PATH}. " +
                               "Corré 'Generate Obstacle Impact Audio Placeholders' primero.");
                return;
            }

            AudioMixerGroup roadGroup = FindRoadGroup();
            if (roadGroup == null)
            {
                Debug.LogError("[ObstacleCollisionSetup] ❌ El mixer no tiene el grupo 'Road'; no se configura la salida de audio.");
                return;
            }

            foreach (string path in new[] { CAR08_PATH, CAR07_PATH })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"[ObstacleCollisionSetup] ⚠️ No existe {path}; se omite.");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureCar(root, profile, roadGroup);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                Debug.Log($"[ObstacleCollisionSetup] ✅ ObstacleContactSensor listo en {path}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void ConfigureCar(GameObject root, ObstacleImpactAudioProfile profile, AudioMixerGroup roadGroup)
        {
            var sensor = GetOrAdd<ObstacleContactSensor>(root);

            Transform child = root.transform.Find(IMPACT_SOURCE_NAME);
            if (child == null)
            {
                var go = new GameObject(IMPACT_SOURCE_NAME);
                go.transform.SetParent(root.transform, false);
                child = go.transform;
            }

            child.localPosition = s_ImpactSourceLocalPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;

            var source = GetOrAdd<AudioSource>(child.gameObject);
            VehicleAudioUtil.ConfigureSource(source, false, profile.MinDistance, profile.MaxDistance, roadGroup, 64);
            source.clip = null;

            var so = new SerializedObject(sensor);
            so.FindProperty("m_AudioProfile").objectReferenceValue = profile;
            so.FindProperty("m_ImpactSource").objectReferenceValue = source;
            so.FindProperty("m_MixerGroup").objectReferenceValue = roadGroup;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Grupo <c>Road</c> de <c>VehicleAudioMixer</c> (no se crean grupos nuevos).</summary>
        public static AudioMixerGroup FindRoadGroup()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(SoundIntegrationSetup.MIXER_PATH);
            if (mixer == null) return null;

            return mixer.FindMatchingGroups("Road").FirstOrDefault(group => group.name == "Road");
        }

        // ------------------------------------------------------------------ Cono (prefab original)

        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";

        /// <summary>
        /// Deja el prefab original del cono con un <c>MeshCollider</c> convexo (material sin fricción) y un
        /// <see cref="ObstacleCollisionDetector"/> de tipo <see cref="ObstacleKind.Cone"/>. Sin Rigidbody: el cono
        /// es fijo. No toca la malla, el material visual ni la escala.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Obstacle Cone Prefab")]
        public static void SetupObstacleConePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH) == null)
            {
                Debug.LogError($"[ObstacleCollisionSetup] ❌ No existe el prefab del cono en {CONE_PREFAB_PATH}.");
                return;
            }

            PhysicsMaterial noFriction = LessonBoundarySetup.EnsureNoFrictionMaterial();

            GameObject root = PrefabUtility.LoadPrefabContents(CONE_PREFAB_PATH);
            try
            {
                var filter = root.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    Debug.LogError("[ObstacleCollisionSetup] ❌ El cono no tiene MeshFilter con malla; no se puede crear el collider.");
                    return;
                }

                var collider = GetOrAdd<MeshCollider>(root);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
                collider.isTrigger = false;
                collider.sharedMaterial = noFriction;

                // El cono es fijo: un Rigidbody lo haría derribable y la decisión es que no lo sea.
                var body = root.GetComponent<Rigidbody>();
                if (body != null) Object.DestroyImmediate(body);

                SetKind(GetOrAdd<ObstacleCollisionDetector>(root), ObstacleKind.Cone);
                PrefabUtility.SaveAsPrefabAsset(root, CONE_PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ObstacleCollisionSetup] ✅ Cono listo en {CONE_PREFAB_PATH} (MeshCollider convexo + detector, sin Rigidbody).");
        }

        // ------------------------------------------------------------------ Prefabs NPC

        /// <summary>Rutas de los prefabs de vehículo NPC (<c>NPC_*.prefab</c>), ordenadas por nombre.</summary>
        public static string[] FindNpcVehiclePaths() => FindPrefabPaths(NPCPrefabSetup.VEHICLE_OUTPUT_FOLDER, "NPC_");

        /// <summary>Rutas de los prefabs de peatón NPC (<c>NPC_Pedestrian_*.prefab</c>), ordenadas por nombre.</summary>
        public static string[] FindNpcPedestrianPaths() => FindPrefabPaths(NPCPrefabSetup.PEDESTRIAN_OUTPUT_FOLDER, "NPC_Pedestrian_");

        private static string[] FindPrefabPaths(string folder, string namePrefix)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return new string[0];

            return AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileName(path).StartsWith(namePrefix))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Agrega <see cref="ObstacleCollisionDetector"/> en la raíz (donde están el Rigidbody y el BoxCollider) de
        /// los vehículos NPC (<see cref="ObstacleKind.Vehicle"/>) y los peatones NPC (<see cref="ObstacleKind.Pedestrian"/>).
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup NPC Obstacle Detectors")]
        public static void SetupNpcObstacleDetectors()
        {
            int vehicles = SetupDetectorOnPrefabs(FindNpcVehiclePaths(), ObstacleKind.Vehicle);
            int pedestrians = SetupDetectorOnPrefabs(FindNpcPedestrianPaths(), ObstacleKind.Pedestrian);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ObstacleCollisionSetup] ✅ Detectores en {vehicles} vehículos NPC y {pedestrians} peatones NPC.");
        }

        private static int SetupDetectorOnPrefabs(string[] paths, ObstacleKind kind)
        {
            int count = 0;
            foreach (string path in paths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    SetKind(GetOrAdd<ObstacleCollisionDetector>(root), kind);
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

        // ------------------------------------------------------------------ Escena de prueba

        public const string SCENES_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes";
        public const string SOURCE_SCENE_PATH = SCENES_FOLDER + "/CityMerged.unity";

        /// <summary>Escena de eventos guionados de US31 (#23): base de la escena integrada. No se modifica.</summary>
        public const string SCRIPTED_EVENTS_SCENE_PATH = SCENES_FOLDER + "/CityMerged_ScriptedEvents.unity";

        /// <summary>
        /// Escena de trabajo integrada: copia de <c>CityMerged_ScriptedEvents</c> con los obstáculos de US33. La zona de
        /// demo va en otra calle que la de los eventos guionados, que usan la calle x = 60 frente al Car08.
        /// </summary>
        public const string SCENE_PATH = SCENES_FOLDER + "/CityMerged2.unity";

        /// <summary>Escena de US33 previa a la integración (zona de demo en la calle x = 60). Se conserva como estaba.</summary>
        public const string LEGACY_SCENE_PATH = SCENES_FOLDER + "/CityWithCollisionDetectors.unity";

        public const string DECORATIVE_VEHICLES_FOLDER = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles";

        public const string DEMO_GROUP_NAME = "Obstacle_Demo";
        public const string DEMO_CONE_PREFIX = "Cone_";
        public const string DEMO_PARKED_CAR_NAME = "NPC_ParkedCar";
        public const string DEMO_PEDESTRIAN_NAME = "NPC_Pedestrian_Standing";
        public const string DEMO_PARKED_CAR_PREFAB = NPCPrefabSetup.VEHICLE_OUTPUT_FOLDER + "/NPC_Car_color02.prefab";
        public const string DEMO_PEDESTRIAN_PREFAB = NPCPrefabSetup.PEDESTRIAN_OUTPUT_FOLDER + "/NPC_Pedestrian_01.prefab";
        public const int DEMO_CONE_COUNT = 5;

        // Zona de práctica sobre la calle x = 120, entre los cruces de z = 60 y z = 120 (carriles de 7 m, mano derecha).
        // La calle x = 60 frente al Car08 la ocupan los eventos guionados de US31 (peatón en la senda de z = 108.5,
        // triggers en z = 101 y 111, auto que cruza el T en z = 123.5). Desde el inicio: hasta el T, a la derecha por
        // z = 116.5 y otra vez a la derecha en el cruce (120, 120) para bajar por el carril oeste (x = 116.5) hacia −Z.
        // El slalom va sobre la línea central, cada 6 m, y deja ~3 m de paso contra los dos autos decorativos que ya
        // están estacionados en la calle (SUV en (116.6, 84.6) y auto en (123.9, 90.1)).
        private static readonly Vector3[] s_DemoConePositions =
        {
            new Vector3(119f, 0f, 104f),
            new Vector3(121f, 0f, 98f),
            new Vector3(119f, 0f, 92f),
            new Vector3(121f, 0f, 86f),
            new Vector3(119f, 0f, 80f),
        };

        // Auto estacionado pegado al cordón este (cara del cordón en x = 126.6), mirando a +Z como el carril este.
        private static readonly Vector3 s_DemoParkedCarPosition = new Vector3(125.1f, 0f, 100f);
        private const float DEMO_PARKED_CAR_YAW = 0f;

        // Peatón quieto sobre la senda del cruce sur (z = 66.6..69.6), en el carril por el que se baja.
        private static readonly Vector3 s_DemoPedestrianPosition = new Vector3(117.5f, 0f, 68f);
        private const float DEMO_PEDESTRIAN_YAW = 90f;

        /// <summary>Posición XZ del cono <paramref name="index"/> (0..4) de la zona de demo.</summary>
        public static Vector3 DemoConePosition(int index)
        {
            return s_DemoConePositions[index];
        }

        /// <summary>Punto de referencia de la zona de demo (para reportes y pruebas): el centro del slalom.</summary>
        public static Vector3 DemoCenter => (s_DemoConePositions[0] + s_DemoConePositions[DEMO_CONE_COUNT - 1]) * 0.5f;

        /// <summary>
        /// Agrega <see cref="ObstacleCollisionDetector"/> (tipo <see cref="ObstacleKind.Vehicle"/>) a cada instancia
        /// de los autos decorativos de la escena activa. Solo para la escena nueva: <c>CityMerged</c> no se toca.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Parked Vehicle Obstacles (Active Scene)")]
        public static void SetupParkedVehicleObstaclesActiveScene()
        {
            SetupParkedVehicleObstacles(SceneManager.GetActiveScene(), save: true);
        }

        /// <summary>
        /// Para cada instancia de un prefab de <c>Prefab/Vehicles/</c> con un collider no trigger en su raíz agrega
        /// el detector (o actualiza su tipo si ya lo tiene). Idempotente y con Undo. Se niega a trabajar sobre
        /// <c>CityMerged</c> o sobre una escena con cambios sin guardar. Devuelve cuántas instancias procesó o −1.
        /// </summary>
        public static int SetupParkedVehicleObstacles(Scene scene, bool save)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[ObstacleCollisionSetup] ❌ La escena no está cargada.");
                return -1;
            }

            if (scene.path == SOURCE_SCENE_PATH || scene.path == SCRIPTED_EVENTS_SCENE_PATH)
            {
                Debug.LogError($"[ObstacleCollisionSetup] ❌ {scene.name} no se modifica: los autos estacionados solo registran " +
                               $"contactos en {SCENE_PATH}.");
                return -1;
            }

            if (scene.isDirty)
            {
                Debug.LogError($"[ObstacleCollisionSetup] ❌ {scene.name} tiene cambios sin guardar: guardalos o descartalos antes de seguir.");
                return -1;
            }

            int processed = 0, added = 0, corrected = 0;
            foreach (GameObject go in FindDecorativeVehicleRoots(scene))
            {
                processed++;
                var detector = go.GetComponent<ObstacleCollisionDetector>();
                bool isNew = detector == null;
                if (isNew)
                {
                    detector = Undo.AddComponent<ObstacleCollisionDetector>(go);
                    added++;
                }

                var so = new SerializedObject(detector);
                SerializedProperty kind = so.FindProperty("m_Kind");
                if (kind.enumValueIndex != (int)ObstacleKind.Vehicle)
                {
                    kind.enumValueIndex = (int)ObstacleKind.Vehicle;
                    if (!isNew) corrected++;
                    so.ApplyModifiedProperties();
                }
            }

            if (added + corrected > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (save) EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"[ObstacleCollisionSetup] 🚗 {scene.name}: {processed} autos decorativos con detector ({added} agregados, {corrected} con el tipo corregido).");
            return processed;
        }

        /// <summary>
        /// Raíces de instancia de los prefabs de <c>Prefab/Vehicles/</c> que tienen un collider no trigger en la raíz.
        /// Los <c>NPC_*</c> y el auto del jugador son de otra carpeta y quedan afuera.
        /// </summary>
        public static List<GameObject> FindDecorativeVehicleRoots(Scene scene)
        {
            var result = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                    if (go.GetComponent<VehicleController>() != null) continue;

                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    if (source == null) continue;

                    string path = AssetDatabase.GetAssetPath(source);
                    if (!path.StartsWith(DECORATIVE_VEHICLES_FOLDER + "/")) continue;

                    Collider collider = go.GetComponent<Collider>();
                    if (collider == null || collider.isTrigger) continue;

                    result.Add(go);
                }
            }

            return result;
        }

        // Objetos que la escena integrada no lleva: el grupo de pruebas de escala (ya quitado en la escena de US33) y un
        // TrafficLightController suelto que dejó una corrida de ScriptedEventTests en CityMerged_ScriptedEvents.
        private static readonly string[] s_MergedSceneLeftovers = { "ScaleTest", "TestTrafficLight" };

        /// <summary>
        /// Arma la escena integrada <c>CityMerged2</c>: copia <c>CityMerged_ScriptedEvents</c> (US31) si todavía no
        /// existe, le quita los restos de pruebas, apaga los logs del Car08 (como en la escena de US33), agrega los
        /// detectores a los autos decorativos y crea la zona de demo de US33 en la calle x = 120. Idempotente.
        /// <c>CityMerged</c> y <c>CityMerged_ScriptedEvents</c> no se modifican.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Merged Scene (CityMerged2)")]
        public static void SetupMergedScene()
        {
            if (!TryGetCleanScene(SCENE_PATH, out Scene scene)) return;

            bool changed = false;
            foreach (string leftover in s_MergedSceneLeftovers)
            {
                GameObject root = LessonBoundarySetup.FindRootByName(scene, leftover);
                if (root == null) continue;

                Undo.DestroyObjectImmediate(root);
                changed = true;
                Debug.Log($"[ObstacleCollisionSetup] 🧹 {scene.name}: quitado '{leftover}'.");
            }

            VehicleController car = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<VehicleController>(true))
                .FirstOrDefault(v => v.name == "Car08");
            if (car != null)
            {
                var so = new SerializedObject(car);
                SerializedProperty logs = so.FindProperty("m_EnableDebugLogs");
                if (logs != null && logs.boolValue)
                {
                    logs.boolValue = false;
                    so.ApplyModifiedProperties();
                    changed = true;
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            SetupParkedVehicleObstacles(scene, save: true);
            SetupObstacleDemo();
        }

        /// <summary>
        /// Crea o actualiza en <c>CityMerged2</c> el grupo <c>Obstacle_Demo</c>: 5 conos en slalom, un auto NPC
        /// estacionado y un peatón NPC quieto (ambos sin ruta y con Play On Start apagado). Idempotente.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Obstacle Demo (CityMerged2)")]
        public static void SetupObstacleDemo()
        {
            if (!TryGetCleanScene(SCENE_PATH, out Scene scene)) return;

            var conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DEMO_PARKED_CAR_PREFAB);
            var pedestrianPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DEMO_PEDESTRIAN_PREFAB);
            if (conePrefab == null || carPrefab == null || pedestrianPrefab == null)
            {
                Debug.LogError("[ObstacleCollisionSetup] ❌ Faltan prefabs (cono o NPC). Corré los setups de NPC y de cono primero.");
                return;
            }

            GameObject group = LessonBoundarySetup.FindRootByName(scene, DEMO_GROUP_NAME);
            if (group == null)
            {
                group = new GameObject(DEMO_GROUP_NAME);
                SceneManager.MoveGameObjectToScene(group, scene);
            }

            group.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            group.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            for (int i = 0; i < DEMO_CONE_COUNT; i++)
            {
                GameObject cone = GetOrInstantiate(group.transform, $"{DEMO_CONE_PREFIX}{i + 1:00}", conePrefab, scene);
                cone.transform.SetPositionAndRotation(OnGround(DemoConePosition(i)), Quaternion.identity);
            }

            GameObject car = GetOrInstantiate(group.transform, DEMO_PARKED_CAR_NAME, carPrefab, scene);
            car.transform.SetPositionAndRotation(OnGround(s_DemoParkedCarPosition), Quaternion.Euler(0f, DEMO_PARKED_CAR_YAW, 0f));
            DisablePlayOnStart(car);

            GameObject pedestrian = GetOrInstantiate(group.transform, DEMO_PEDESTRIAN_NAME, pedestrianPrefab, scene);
            pedestrian.transform.SetPositionAndRotation(OnGround(s_DemoPedestrianPosition), Quaternion.Euler(0f, DEMO_PEDESTRIAN_YAW, 0f));
            DisablePlayOnStart(pedestrian);

            Physics.SyncTransforms();
            List<string> problems = ValidateDemoPlacement(scene);
            foreach (string problem in problems) Debug.LogWarning($"[ObstacleCollisionSetup] ⚠️ {problem}");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ObstacleCollisionSetup] ✅ {DEMO_GROUP_NAME} listo en {scene.name}: {DEMO_CONE_COUNT} conos, " +
                      $"auto estacionado en {s_DemoParkedCarPosition}, peatón en {s_DemoPedestrianPosition} " +
                      $"({problems.Count} advertencias de ubicación).");
        }

        private static GameObject GetOrInstantiate(Transform parent, string name, GameObject prefab, Scene scene)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            return instance;
        }

        // Sin ruta y sin Play On Start el NPC queda quieto y no emite warnings.
        private static void DisablePlayOnStart(GameObject npc)
        {
            var follower = npc.GetComponent<NPCPathFollower>();
            if (follower == null) return;

            var so = new SerializedObject(follower);
            NPCPrefabSetup.RequireProperty(so, "m_PlayOnStart").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Apoya la posición sobre el asfalto: primera pieza <c>Road *</c> bajo el punto. Si no hay ninguna deja y = 0.
        /// </summary>
        public static Vector3 OnGround(Vector3 position)
        {
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(position.x, 5f, position.z), Vector3.down, 10f, ~0,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.root.name.StartsWith("Road")) return new Vector3(position.x, hit.point.y, position.z);
            }

            return new Vector3(position.x, 0f, position.z);
        }

        /// <summary>
        /// Controla la ubicación de la zona de demo: cada objeto apoya sobre una pieza <c>Road *</c> en el asfalto
        /// (y ≈ 0), no se superpone con ningún collider ajeno (autos decorativos, semáforos, vallas, edificios) y
        /// queda a más de 3 m de las rutas de los NPC de la escena y de <c>Lesson_Boundaries</c>, a más de 1 m de las
        /// líneas de detención y triggers de eventos guionados, y a más de 2.5 m de los semáforos. Devuelve la lista de
        /// problemas (vacía si está bien).
        /// </summary>
        public static List<string> ValidateDemoPlacement(Scene scene)
        {
            var problems = new List<string>();
            GameObject group = LessonBoundarySetup.FindRootByName(scene, DEMO_GROUP_NAME);
            if (group == null)
            {
                problems.Add($"No existe el grupo {DEMO_GROUP_NAME}.");
                return problems;
            }

            Physics.SyncTransforms();

            // Rutas y vallas con las que no se debe superponer: las rutas de todos los NPC de la escena (NPC_Demo y los
            // eventos guionados de US31), salvo las de la propia zona de demo.
            var routePoints = new List<Vector3>();
            var routes = new HashSet<SplineContainer>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == group) continue;
                foreach (NPCPathFollower follower in root.GetComponentsInChildren<NPCPathFollower>(true))
                {
                    if (follower.Path != null) routes.Add(follower.Path);
                }
            }

            foreach (SplineContainer container in routes)
            {
                for (int s = 0; s < container.Splines.Count; s++)
                {
                    foreach (var sample in NPCDemoRoutesSetup.SampleRoute(container, s, 1f)) routePoints.Add(sample.position);
                }
            }

            GameObject boundaries = LessonBoundarySetup.FindRootByName(scene, LessonBoundarySetup.BOUNDARIES_GROUP_NAME);
            Collider[] boundaryColliders = boundaries != null ? boundaries.GetComponentsInChildren<Collider>(true) : new Collider[0];

            // Volúmenes trigger que el auto no debe activar al usar la zona: líneas de detención (US32) y triggers de
            // eventos guionados (US31).
            Collider[] stopLines = Object.FindObjectsByType<RedLightViolationDetector>(FindObjectsInactive.Include)
                .Select(detector => detector.GetComponent<Collider>())
                .Concat(Object.FindObjectsByType<TriggerScriptedEvent>(FindObjectsInactive.Include)
                    .Select(trigger => trigger.GetComponent<Collider>()))
                .Where(collider => collider != null)
                .ToArray();

            TrafficLightController[] signals = Object.FindObjectsByType<TrafficLightController>(FindObjectsInactive.Include);

            var overlaps = new Collider[32];
            foreach (Transform item in group.transform)
            {
                Collider[] own = item.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
                if (own.Length == 0)
                {
                    problems.Add($"{item.name}: no tiene collider.");
                    continue;
                }

                // Apoyo: asfalto de una pieza Road * bajo el pivote.
                Vector3 position = item.position;
                Vector3 ground = OnGround(position);
                RaycastHit[] hits = Physics.RaycastAll(new Vector3(position.x, 5f, position.z), Vector3.down, 10f, ~0,
                    QueryTriggerInteraction.Ignore);
                bool onRoad = hits.Any(hit => hit.collider.transform.root.name.StartsWith("Road") && hit.point.y < 0.1f);
                if (!onRoad) problems.Add($"{item.name}: no apoya sobre el asfalto de una pieza Road * en {position}.");
                if (Mathf.Abs(position.y - ground.y) > 0.05f) problems.Add($"{item.name}: y = {position.y:F2}, el asfalto está en {ground.y:F2}.");

                // Superposición con colliders ajenos.
                foreach (Collider collider in own)
                {
                    Bounds bounds = collider.bounds;
                    bounds.Expand(0.1f);
                    int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, overlaps, Quaternion.identity, ~0,
                        QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        Collider other = overlaps[i];
                        if (other == collider || other.transform.IsChildOf(group.transform)) continue;

                        string rootName = other.transform.root.name;
                        if (rootName.StartsWith("Ground")) continue;
                        if (rootName.StartsWith("Road") && other is MeshCollider) continue; // piso de la calle

                        problems.Add($"{item.name}: se superpone con {rootName}/{other.name}.");
                    }
                }

                // Distancia a rutas de NPC (3 m), vallas de lección (3 m) y volúmenes trigger (1 m).
                var flat = new Vector2(position.x, position.z);
                float minRoute = routePoints.Count == 0
                    ? float.PositiveInfinity
                    : routePoints.Min(p => Vector2.Distance(new Vector2(p.x, p.z), flat));
                if (minRoute < 3f) problems.Add($"{item.name}: a {minRoute:F1} m de la ruta de un NPC.");

                foreach (Collider boundary in boundaryColliders)
                {
                    if (Vector3.Distance(boundary.ClosestPoint(position), position) < 3f)
                    {
                        problems.Add($"{item.name}: a menos de 3 m de {LessonBoundarySetup.BOUNDARIES_GROUP_NAME}.");
                        break;
                    }
                }

                foreach (Collider stopLine in stopLines)
                {
                    Vector3 closest = stopLine.ClosestPoint(position);
                    if (Vector2.Distance(new Vector2(closest.x, closest.z), flat) < 1.0f)
                    {
                        problems.Add($"{item.name}: a menos de 1 m del trigger {stopLine.name}.");
                    }
                }

                // Semáforos: los postes deben quedar a más de 2.5 m.
                foreach (TrafficLightController signal in signals)
                {
                    float distance = Vector2.Distance(new Vector2(signal.transform.position.x, signal.transform.position.z), flat);
                    if (distance < 2.5f) problems.Add($"{item.name}: a {distance:F1} m del semáforo {signal.name}.");
                }
            }

            return problems;
        }

        // Escena cargada y sin cambios sin guardar. Si no existe, la crea como copia de la escena de eventos guionados
        // (US31), que ya incluye todo CityMerged. Nunca abre en modo Single sobre cambios sin guardar.
        private static bool TryGetCleanScene(string scenePath, out Scene scene)
        {
            scene = SceneManager.GetSceneByPath(scenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty)
                {
                    Debug.LogError($"[ObstacleCollisionSetup] ❌ {scene.name} tiene cambios sin guardar: guardalos o descartalos antes de seguir.");
                    return false;
                }

                return true;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogError($"[ObstacleCollisionSetup] ❌ {open.name} tiene cambios sin guardar: guardalos antes de abrir {scenePath}.");
                    return false;
                }
            }

            if (!System.IO.File.Exists(scenePath))
            {
                if (!AssetDatabase.CopyAsset(SCRIPTED_EVENTS_SCENE_PATH, scenePath))
                {
                    Debug.LogError($"[ObstacleCollisionSetup] ❌ No se pudo copiar {SCRIPTED_EVENTS_SCENE_PATH} a {scenePath}.");
                    return false;
                }
            }

            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return scene.IsValid() && scene.isLoaded;
        }

        // ------------------------------------------------------------------ Helpers

        private static void SetKind(ObstacleCollisionDetector detector, ObstacleKind kind)
        {
            var so = new SerializedObject(detector);
            so.FindProperty("m_Kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
