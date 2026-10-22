using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrimerVolante.Audio;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Parte de prefabs NPC y de escena del setup de audio del entorno (US20). Todo idempotente.
    /// </summary>
    public static partial class EnvironmentAudioSetup
    {
        public const string CITY_SCENE_PATH = "Assets/Scenes/car09/CityMerged3.unity";
        public const string ROOT_NAME = "Audio_Environment";
        public const string ZONES_NAME = "Zones";
        public const string EMITTERS_NAME = "Emitters";
        public const string ALERTS_NAME = "Alerts";
        public const string INTERSECTION_NAME = "Controlled_Intersection_T";
        public const string PLAYER_SIGNAL_NAME = "TrafficSignal_PlayerLane_Big";
        public const string STOP_LINE_NAME = "StopLine_Trigger";

        // Cajas de las zonas: altura fija y márgenes sobre los bounds de los renderers.
        private const float k_ZoneHeight = 40f;
        private const float k_ZoneCenterY = 15f;
        private const float k_CityMargin = 5f;
        private const float k_TownMargin = 5f;
        private const float k_PlazaMargin = 1.5f;
        private const float k_TownMinZ = 150f;       // el pueblo empieza donde termina la plaza (tabla de §2)
        private const float k_EmitterHeight = 2f;

        // La plaza son dos mitades a ambos lados de la avenida; se parte la franja de macetas por esta x.
        private const float k_PlazaSplitX = 110f;
        private const float k_PlazaMinX = 30f;
        private const float k_PlazaMaxX = 210f;
        private const float k_PlazaMinZ = 125f;
        private const float k_PlazaMaxZ = 155f;

        // ------------------------------------------------------------------ prefabs NPC

        [MenuItem("Tools/Primer Volante/Environment Audio/Setup NPC Audio Prefabs")]
        public static void SetupNpcAudioPrefabsFromMenu()
        {
            SetupNpcAudioPrefabs();
        }

        /// <summary>
        /// Agrega <see cref="NPCVehicleAudio"/> a los 21 vehículos NPC (perfil Heavy para buses, camiones y
        /// contenedores; sirena asignada solo en ambulancia y patrulla, apagada) y <see cref="NPCPedestrianAudio"/> a los
        /// 4 peatones, con sus fuentes hijas ruteadas al mixer. No duplica componentes ni hijos.
        /// </summary>
        public static void SetupNpcAudioPrefabs()
        {
            if (!EnsureProfilesAndMixer(out AudioMixer mixer)) return;

            AudioMixerGroup npc = FindGroup(mixer, "NPC");
            AudioMixerGroup alerts = FindGroup(mixer, "Alerts");
            if (npc == null || alerts == null) return;

            var defaultProfile = LoadProfile<NPCVehicleAudioProfile>(NPC_VEHICLE_DEFAULT);
            var heavyProfile = LoadProfile<NPCVehicleAudioProfile>(NPC_VEHICLE_HEAVY);
            var pedestrianProfile = LoadProfile<NPCPedestrianAudioProfile>(NPC_PEDESTRIAN_DEFAULT);

            int vehicles = 0, pedestrians = 0;
            foreach (string path in ObstacleCollisionSetup.FindNpcVehiclePaths())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    var audio = GetOrAdd<NPCVehicleAudio>(root);
                    NPCVehicleAudioProfile profile = IsHeavyVehicle(name) ? heavyProfile : defaultProfile;
                    audio.Profile = profile;
                    audio.BuildSources(npc, alerts);

                    // Sirena configurada solo en ambulancia y patrulla; sigue apagada (m_SirenEnabled = false).
                    audio.SirenClip = IsSirenVehicle(name) ? profile.SirenLoop : null;
                    EditorUtility.SetDirty(audio);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    vehicles++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            foreach (string path in ObstacleCollisionSetup.FindNpcPedestrianPaths())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var audio = GetOrAdd<NPCPedestrianAudio>(root);
                    audio.Profile = pedestrianProfile;
                    audio.BuildSources(npc);
                    EditorUtility.SetDirty(audio);

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    pedestrians++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[EnvironmentAudioSetup] ✅ NPCVehicleAudio en {vehicles} vehículos y NPCPedestrianAudio en {pedestrians} peatones.");
        }

        /// <summary>true para buses, camiones y contenedores (no para las camionetas pick up).</summary>
        public static bool IsHeavyVehicle(string prefabName)
        {
            return prefabName.StartsWith("NPC_Bus") || prefabName.StartsWith("NPC_Truck") || prefabName.StartsWith("NPC_Container");
        }

        /// <summary>true para la ambulancia y la patrulla.</summary>
        public static bool IsSirenVehicle(string prefabName)
        {
            return prefabName.StartsWith("NPC_Ambulance") || prefabName.StartsWith("NPC_Police Car");
        }

        // ------------------------------------------------------------------ voces (presupuesto)

        /// <summary>
        /// Sube <c>m_RealVoiceCount</c> a 64 en <c>AudioManager.asset</c> (target PCVR) usando el SerializedObject del
        /// asset, sin editar el YAML. Devuelve el valor anterior.
        /// </summary>
        public static int SetRealVoiceCount(int voices)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[EnvironmentAudioSetup] ❌ No se pudo cargar ProjectSettings/AudioManager.asset.");
                return -1;
            }

            var so = new SerializedObject(assets[0]);
            SerializedProperty prop = so.FindProperty("m_RealVoiceCount");
            int previous = prop.intValue;
            if (previous != voices)
            {
                prop.intValue = voices;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[EnvironmentAudioSetup] 🔊 m_RealVoiceCount: {previous} → {voices}.");
            return previous;
        }

        // ------------------------------------------------------------------ escena

        /// <summary>
        /// Valores finales de las zonas (para el reporte).
        /// </summary>
        public struct ZoneBox
        {
            public string Name;
            public Vector3 Center;
            public Vector3 Size;
            public float Fade;
            public int Priority;
        }

        [MenuItem("Tools/Primer Volante/Environment Audio/Setup Environment Audio (CityMerged3)")]
        public static void SetupEnvironmentAudioSceneFromMenu()
        {
            SetupEnvironmentAudioScene();
        }

        /// <summary>
        /// Crea o actualiza <c>Audio_Environment</c> en <c>CityMerged3</c>: director, alarma de autos, zonas (cajas
        /// calculadas con los bounds de los renderers), emisores por prefab de origen, <see cref="CrosswalkBeeper"/> en el
        /// semáforo de la intersección controlada y <see cref="GreenLightHonk"/>. Guarda la escena. Si hay cambios sin
        /// guardar en alguna escena abierta, se detiene.
        /// </summary>
        public static void SetupEnvironmentAudioScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[EnvironmentAudioSetup] ❌ No se puede correr el setup en Play Mode.");
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogError($"[EnvironmentAudioSetup] ❌ La escena '{open.name}' tiene cambios sin guardar. Guardala (o descartá los cambios) y volvé a correr el setup.");
                    return;
                }
            }

            if (!EnsureProfilesAndMixer(out AudioMixer mixer)) return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != CITY_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(CITY_SCENE_PATH, OpenSceneMode.Single);
            }

            AudioMixerGroup ambience = FindGroup(mixer, "Ambience");
            AudioMixerGroup alerts = FindGroup(mixer, "Alerts");
            if (ambience == null || alerts == null) return;

            // ---- Raíz y hijos
            GameObject root = FindOrCreateRoot(scene, ROOT_NAME);
            Transform zonesParent = EnsureChild(root.transform, ZONES_NAME);
            Transform emittersParent = EnsureChild(root.transform, EMITTERS_NAME);
            Transform alertsParent = EnsureChild(root.transform, ALERTS_NAME);

            var alertsProfile = LoadProfile<EnvironmentAlertsProfile>(ALERTS_DEFAULT);

            // ---- Director
            var director = GetOrAddUndo<AmbienceDirector>(root);
            var directorSo = new SerializedObject(director);
            directorSo.FindProperty("m_Mixer").objectReferenceValue = mixer;
            directorSo.FindProperty("m_AmbienceGroup").objectReferenceValue = ambience;
            directorSo.FindProperty("m_FallbackProfile").objectReferenceValue = LoadProfile<AmbienceZoneProfile>(ZONE_TOWN);
            directorSo.ApplyModifiedProperties();

            // ---- Alarma de autos estacionados
            var alarm = GetOrAddUndo<ParkedCarAlarm>(root);
            alarm.Profile = alertsProfile;
            alarm.BuildSource(alerts, alertsParent);
            EditorUtility.SetDirty(alarm);

            // ---- Zonas
            List<ZoneBox> zones = ComputeZoneBoxes(scene);
            SyncZones(zonesParent, zones);

            // ---- Emisores
            Dictionary<string, int> emitterCounts = SyncEmitters(scene, emittersParent, ambience);

            // ---- Semáforo sonoro y bocina de impaciencia en la intersección controlada
            int beepers = SetupIntersection(scene, alertsProfile, alerts);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string zoneSummary = string.Join("; ", zones.Select(z =>
                $"{z.Name}: centro ({z.Center.x:F1}, {z.Center.z:F1}) tamaño ({z.Size.x:F1} × {z.Size.z:F1}) fade {z.Fade:F0} prio {z.Priority}"));
            string emitterSummary = string.Join(", ", emitterCounts.Select(kv => $"{kv.Key}={kv.Value}"));
            Debug.Log($"[EnvironmentAudioSetup] ✅ Escena configurada. Zonas → {zoneSummary}. Emisores → {emitterSummary} " +
                      $"(total {emitterCounts.Values.Sum()}). CrosswalkBeeper: {beepers}.");
        }

        // ------------------------------------------------------------------ zonas

        /// <summary>
        /// Calcula las cajas de las cuatro zonas a partir de los bounds de los renderers de cada grupo de prefabs
        /// (ciudad: torres + comercios; pueblo: casas, pisos y cercas; plaza: macetas y bancos de cada mitad).
        /// </summary>
        public static List<ZoneBox> ComputeZoneBoxes(Scene scene)
        {
            var city = new List<Bounds>();
            var town = new List<Bounds>();
            var plazaWest = new List<Bounds>();
            var plazaEast = new List<Bounds>();

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                string origin = OriginName(go);
                if (origin == null || !TryGetRendererBounds(go, out Bounds b)) continue;

                if (origin.StartsWith("Building Sky_") || origin.StartsWith("Building_Residential_")
                    || (origin.StartsWith("Building_") && !origin.StartsWith("Building_House")))
                {
                    city.Add(b);
                }
                else if (origin.StartsWith("Building_House") || origin == "Natures_House Floor" || origin == "Natures_Grass Fence")
                {
                    // Algunas casas están en los bordes de la ciudad: al pueblo solo van las que están en su lado.
                    if (b.center.z >= k_TownMinZ) town.Add(b);
                }
                else if (origin == "Natures_Pot Bush_big" || origin == "Natures_Pot Bush_small" || origin == "Props_Bench_1")
                {
                    if (b.center.x < k_PlazaMinX || b.center.x > k_PlazaMaxX || b.center.z < k_PlazaMinZ || b.center.z > k_PlazaMaxZ) continue;
                    (b.center.x < k_PlazaSplitX ? plazaWest : plazaEast).Add(b);
                }
            }

            var result = new List<ZoneBox>();
            result.Add(MakeBox("Zone_City", city, k_CityMargin, 20f, 0, null));
            result.Add(MakeBox("Zone_Plaza_West", plazaWest, k_PlazaMargin, 12f, 10, null));
            result.Add(MakeBox("Zone_Plaza_East", plazaEast, k_PlazaMargin, 12f, 10, null));
            result.Add(MakeBox("Zone_Town", town, k_TownMargin, 25f, 0, k_TownMinZ));
            return result;
        }

        private static ZoneBox MakeBox(string name, List<Bounds> parts, float margin, float fade, int priority, float? minZ)
        {
            if (parts.Count == 0) return new ZoneBox { Name = name, Fade = fade, Priority = priority };

            Bounds b = parts[0];
            for (int i = 1; i < parts.Count; i++) b.Encapsulate(parts[i]);

            float xMin = b.min.x - margin, xMax = b.max.x + margin;
            float zMin = b.min.z - margin, zMax = b.max.z + margin;
            if (minZ.HasValue) zMin = Mathf.Min(zMin, minZ.Value);

            return new ZoneBox
            {
                Name = name,
                Center = new Vector3((xMin + xMax) * 0.5f, k_ZoneCenterY, (zMin + zMax) * 0.5f),
                Size = new Vector3(xMax - xMin, k_ZoneHeight, zMax - zMin),
                Fade = fade,
                Priority = priority,
            };
        }

        private static void SyncZones(Transform parent, List<ZoneBox> boxes)
        {
            var cityProfile = LoadProfile<AmbienceZoneProfile>(ZONE_CITY);
            var plazaProfile = LoadProfile<AmbienceZoneProfile>(ZONE_PLAZA);
            var townProfile = LoadProfile<AmbienceZoneProfile>(ZONE_TOWN);

            foreach (ZoneBox box in boxes)
            {
                if (box.Size == Vector3.zero)
                {
                    Debug.LogWarning($"[EnvironmentAudioSetup] ⚠️ No se encontraron objetos para calcular {box.Name}.");
                    continue;
                }

                Transform child = EnsureChild(parent, box.Name);
                child.position = box.Center;
                child.rotation = Quaternion.identity;
                child.localScale = Vector3.one;

                var zone = GetOrAddUndo<AmbienceZone>(child.gameObject);
                zone.Size = box.Size;
                zone.FadeDistance = box.Fade;
                zone.Priority = box.Priority;
                zone.Profile = box.Name.StartsWith("Zone_City") ? cityProfile : box.Name.StartsWith("Zone_Plaza") ? plazaProfile : townProfile;
                EditorUtility.SetDirty(zone);
            }
        }

        // ------------------------------------------------------------------ emisores

        private struct EmitterSpec
        {
            public string Name;
            public Vector3 Position;
            public string ProfileName;
        }

        /// <summary>
        /// Lista de emisores esperados en la escena, por prefab de origen (tabla de §4.8).
        /// </summary>
        public static List<(string name, Vector3 position, string profile)> ComputeEmitters(Scene scene)
        {
            var specs = new List<EmitterSpec>();
            var houses = new List<(GameObject go, Bounds b)>();
            var plazaWest = new List<Bounds>();
            var plazaEast = new List<Bounds>();

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                string origin = OriginName(go);
                if (origin == null || !TryGetRendererBounds(go, out Bounds b)) continue;

                string profile = ProfileForOrigin(origin);
                if (profile != null)
                {
                    specs.Add(new EmitterSpec
                    {
                        Name = $"Emitter_{profile.Replace("Emitter_", string.Empty)}_{go.name}",
                        Position = new Vector3(b.center.x, k_EmitterHeight, b.center.z),
                        ProfileName = profile,
                    });
                }
                else if (origin.StartsWith("Building_House"))
                {
                    houses.Add((go, b));
                }
                else if (origin == "Natures_Pot Bush_big" || origin == "Natures_Pot Bush_small" || origin == "Props_Bench_1")
                {
                    if (b.center.x < k_PlazaMinX || b.center.x > k_PlazaMaxX || b.center.z < k_PlazaMinZ || b.center.z > k_PlazaMaxZ) continue;
                    (b.center.x < k_PlazaSplitX ? plazaWest : plazaEast).Add(b);
                }
            }

            // Casas: 1 de cada 3, elegidas en forma determinista (ordenadas por posición).
            var sortedHouses = houses
                .OrderBy(h => Mathf.Round(h.b.center.x * 10f))
                .ThenBy(h => Mathf.Round(h.b.center.z * 10f))
                .ThenBy(h => h.go.name, System.StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < sortedHouses.Count; i += 3)
            {
                var (go, b) = sortedHouses[i];
                specs.Add(new EmitterSpec
                {
                    Name = $"Emitter_HouseDog_{go.name}",
                    Position = new Vector3(b.center.x, k_EmitterHeight, b.center.z),
                    ProfileName = EMITTER_DOG,
                });
            }

            // Plaza: 2 por mitad, entre las hileras de macetas (a un tercio y dos tercios del largo, en el medio de z).
            AddPlazaEmitters(specs, plazaWest, "West");
            AddPlazaEmitters(specs, plazaEast, "East");

            return specs.Select(s => (s.Name, s.Position, s.ProfileName)).ToList();
        }

        private static void AddPlazaEmitters(List<EmitterSpec> specs, List<Bounds> parts, string half)
        {
            if (parts.Count == 0) return;

            Bounds b = parts[0];
            for (int i = 1; i < parts.Count; i++) b.Encapsulate(parts[i]);

            float z = b.center.z;
            for (int i = 0; i < 2; i++)
            {
                float x = Mathf.Lerp(b.min.x, b.max.x, i == 0 ? 0.3f : 0.7f);
                specs.Add(new EmitterSpec
                {
                    Name = $"Emitter_PlazaLife_{half}_{i + 1}",
                    Position = new Vector3(x, k_EmitterHeight, z),
                    ProfileName = EMITTER_PLAZA,
                });
            }
        }

        // Perfil del emisor para un prefab de origen (null si no corresponde). Las casas se resuelven aparte.
        private static string ProfileForOrigin(string origin)
        {
            switch (origin)
            {
                case "Building_Bar":
                case "Building_Restaurant":
                case "Building_Coffee Shop":
                case "Building_Pizza":
                case "Building_Fast Food":
                case "Building_Chicken Shop":
                    return EMITTER_FOOD;
                case "Building_Stadium": return EMITTER_STADIUM;
                case "Building_Auto Service": return EMITTER_WORKSHOP;
                case "Building_Gas Station": return EMITTER_GAS;
                case "Building_Factory": return EMITTER_FACTORY;
                case "Props_Windmill": return EMITTER_WINDMILL;
                default: return null;
            }
        }

        private static Dictionary<string, int> SyncEmitters(Scene scene, Transform parent, AudioMixerGroup ambience)
        {
            var counts = new Dictionary<string, int>();
            var wanted = new HashSet<string>();

            foreach (var (name, position, profileName) in ComputeEmitters(scene))
            {
                wanted.Add(name);
                counts[profileName] = counts.TryGetValue(profileName, out int c) ? c + 1 : 1;

                var profile = LoadProfile<AmbientEmitterProfile>(profileName);
                Transform child = EnsureChild(parent, name);
                child.position = position;

                var emitter = GetOrAddUndo<AmbientEmitter>(child.gameObject);
                emitter.Profile = profile;
                emitter.MixerGroup = ambience;

                // Configura la AudioSource en el editor (3D, rolloff logarítmico, sin Doppler, prioridad de emisor).
                var source = child.GetComponent<AudioSource>();
                PrimerVolante.VR.VehicleAudioUtil.ConfigureSource(
                    source, profile.Mode == AmbientEmitterMode.Loop, profile.MinDistance, profile.MaxDistance, ambience, EnvironmentAudioUtil.PriorityEmitter);
                EditorUtility.SetDirty(emitter);
                EditorUtility.SetDirty(source);
            }

            // Quita los emisores que ya no corresponden (p. ej. si cambió la selección de casas).
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (!wanted.Contains(child.name)) Undo.DestroyObjectImmediate(child.gameObject);
            }

            return counts;
        }

        // ------------------------------------------------------------------ intersección

        private static int SetupIntersection(Scene scene, EnvironmentAlertsProfile alertsProfile, AudioMixerGroup alerts)
        {
            GameObject intersection = scene.GetRootGameObjects().FirstOrDefault(g => g.name == INTERSECTION_NAME);
            if (intersection == null)
            {
                Debug.LogWarning($"[EnvironmentAudioSetup] ⚠️ No se encontró {INTERSECTION_NAME}: sin CrosswalkBeeper ni GreenLightHonk.");
                return 0;
            }

            // El semáforo que gobierna el cruce de peatones frente a la línea de detención.
            TrafficLightController playerSignal = intersection.GetComponentsInChildren<TrafficLightController>(true)
                .FirstOrDefault(t => t.name == PLAYER_SIGNAL_NAME);
            Transform stopLine = intersection.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == STOP_LINE_NAME);
            var stopLineBox = stopLine != null ? stopLine.GetComponent<BoxCollider>() : null;

            int beepers = 0;
            if (playerSignal != null)
            {
                var beeper = GetOrAddUndo<CrosswalkBeeper>(playerSignal.gameObject);
                beeper.TrafficLight = playerSignal;
                beeper.Profile = alertsProfile;
                beeper.BuildSource(alerts);
                beeper.Source.transform.localPosition = new Vector3(0f, 2.5f / Mathf.Max(0.01f, playerSignal.transform.lossyScale.y), 0f);
                EditorUtility.SetDirty(beeper);
                EditorUtility.SetDirty(beeper.Source);
                beepers++;
            }
            else
            {
                Debug.LogWarning($"[EnvironmentAudioSetup] ⚠️ No se encontró {PLAYER_SIGNAL_NAME}.");
            }

            if (playerSignal != null && stopLineBox != null)
            {
                var honk = GetOrAddUndo<GreenLightHonk>(intersection);
                honk.TrafficLight = playerSignal;
                honk.StopLineTrigger = stopLineBox;
                honk.Profile = alertsProfile;
                honk.BuildSource(alerts);
                EditorUtility.SetDirty(honk);
                EditorUtility.SetDirty(honk.Source);
            }
            else
            {
                Debug.LogWarning($"[EnvironmentAudioSetup] ⚠️ No se pudo configurar GreenLightHonk (falta {PLAYER_SIGNAL_NAME} o {STOP_LINE_NAME}).");
            }

            return beepers;
        }

        // ------------------------------------------------------------------ helpers

        private static bool EnsureProfilesAndMixer(out AudioMixer mixer)
        {
            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MIXER_PATH);
            if (mixer == null)
            {
                Debug.LogError($"[EnvironmentAudioSetup] ❌ Falta {MIXER_PATH}. Crealo a mano (ver el prompt de US20, §4.1).");
                return false;
            }

            if (LoadProfile<EnvironmentAlertsProfile>(ALERTS_DEFAULT) == null)
            {
                Debug.Log("[EnvironmentAudioSetup] ℹ️ Faltan los perfiles: se crean ahora.");
                EnvironmentAudioPlaceholderGenerator.GeneratePlaceholders();
                CreateOrUpdateProfiles();
            }

            return true;
        }

        /// <summary>Grupo del EnvironmentAudioMixer por nombre exacto (o <c>null</c> y un warning).</summary>
        public static AudioMixerGroup FindGroup(AudioMixer mixer, string name)
        {
            if (mixer == null) return null;
            foreach (AudioMixerGroup g in mixer.FindMatchingGroups(name))
            {
                if (g.name == name) return g;
            }

            Debug.LogWarning($"[EnvironmentAudioSetup] ⚠️ El mixer no tiene el grupo '{name}'.");
            return null;
        }

        /// <summary>Nombre del prefab de origen de una instancia de la escena (o <c>null</c> si no es un prefab).</summary>
        public static string OriginName(GameObject go)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null ? Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source)) : null;
        }

        private static bool TryGetRendererBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return has;
        }

        private static GameObject FindOrCreateRoot(Scene scene, string name)
        {
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (root != null) return root;

            root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Create " + name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static T GetOrAddUndo<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
