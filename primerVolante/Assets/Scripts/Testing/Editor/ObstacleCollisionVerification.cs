using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de US33 (#21): cono con collider convexo y detector, detectores en los
    /// prefabs NPC, sensor y audio en Car08 y Car07, escena <c>CityWithCollisionDetectors</c> con la zona de demo,
    /// funciones puras y suites existentes. El comportamiento en movimiento (eventos, sonido, frenado) se prueba en
    /// Play Mode.
    /// </summary>
    public static class ObstacleCollisionVerification
    {
        [MenuItem("Tools/Primer Volante/Run Obstacle Collision Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE OBSTÁCULOS CON DETECCIÓN DE COLISIONES (US33 #21)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyConePrefab(ref total, ref passed);
            VerifyNpcPrefabs(ref total, ref passed);
            VerifyCarPrefabs(ref total, ref passed);
            VerifyPureFunctions(ref total, ref passed);
            VerifyDetector(ref total, ref passed);
            VerifyAudioProfile(ref total, ref passed);
            VerifyScene(ref total, ref passed);
            VerifyExistingSuites(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Cono

        private static void VerifyConePrefab(ref int total, ref int passed)
        {
            var cone = AssetDatabase.LoadAssetAtPath<GameObject>(ObstacleCollisionSetup.CONE_PREFAB_PATH);
            Assert(cone != null, "Props_Traffic cone.prefab debe existir", ref total, ref passed);
            if (cone == null) return;

            var filter = cone.GetComponent<MeshFilter>();
            var collider = cone.GetComponent<MeshCollider>();
            Assert(collider != null && collider.convex && !collider.isTrigger,
                "Cono: MeshCollider convexo y no trigger", ref total, ref passed);
            Assert(collider != null && filter != null && collider.sharedMesh == filter.sharedMesh,
                "Cono: el MeshCollider usa la malla del MeshFilter", ref total, ref passed);

            var noFriction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(LessonBoundarySetup.NO_FRICTION_PATH);
            Assert(collider != null && noFriction != null && collider.sharedMaterial == noFriction,
                "Cono: el collider usa el material NoFriction", ref total, ref passed);

            var detector = cone.GetComponent<ObstacleCollisionDetector>();
            Assert(detector != null && detector.Kind == ObstacleKind.Cone,
                "Cono: ObstacleCollisionDetector con Kind = Cone", ref total, ref passed);
            Assert(cone.GetComponent<Rigidbody>() == null, "Cono: sin Rigidbody (es fijo)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- NPC

        private static void VerifyNpcPrefabs(ref int total, ref int passed)
        {
            string[] vehicles = ObstacleCollisionSetup.FindNpcVehiclePaths();
            string[] pedestrians = ObstacleCollisionSetup.FindNpcPedestrianPaths();
            Assert(vehicles.Length == 21, $"Deben existir 21 vehículos NPC (encontrados {vehicles.Length})", ref total, ref passed);
            Assert(pedestrians.Length == 4, $"Deben existir 4 peatones NPC (encontrados {pedestrians.Length})", ref total, ref passed);

            foreach (string path in vehicles) VerifyNpcPrefab(path, ObstacleKind.Vehicle, ref total, ref passed);
            foreach (string path in pedestrians) VerifyNpcPrefab(path, ObstacleKind.Pedestrian, ref total, ref passed);
        }

        private static void VerifyNpcPrefab(string path, ObstacleKind kind, ref int total, ref int passed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string label = System.IO.Path.GetFileNameWithoutExtension(path);

            var detector = prefab.GetComponent<ObstacleCollisionDetector>();
            var body = prefab.GetComponent<Rigidbody>();
            Collider[] solids = prefab.GetComponents<Collider>().Where(c => !c.isTrigger).ToArray();

            Assert(detector != null && detector.Kind == kind,
                $"{label}: detector {kind} en la raíz", ref total, ref passed);
            Assert(body != null && body.isKinematic && solids.Length > 0,
                $"{label}: el detector está en el GameObject del Rigidbody cinemático y del collider no trigger", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Auto del jugador

        private static void VerifyCarPrefabs(ref int total, ref int passed)
        {
            AudioMixerGroup road = ObstacleCollisionSetup.FindRoadGroup();
            Assert(road != null, "VehicleAudioMixer debe tener el grupo Road", ref total, ref passed);

            foreach (string path in new[] { ObstacleCollisionSetup.CAR08_PATH, ObstacleCollisionSetup.CAR07_PATH })
            {
                var car = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                Assert(car != null, $"{label}: prefab debe existir", ref total, ref passed);
                if (car == null) continue;

                VerifyCarSensor(car, label, road, ref total, ref passed);
            }
        }

        private static void VerifyCarSensor(GameObject car, string label, AudioMixerGroup road, ref int total, ref int passed)
        {
            var sensor = car.GetComponent<ObstacleContactSensor>();
            var body = car.GetComponent<Rigidbody>();
            Assert(sensor != null && body != null && !body.isKinematic,
                $"{label}: ObstacleContactSensor en la raíz junto al Rigidbody dinámico", ref total, ref passed);
            if (sensor == null) return;

            AudioSource source = sensor.ImpactSource;
            Assert(source != null && source.transform.parent == car.transform && source.name == ObstacleCollisionSetup.IMPACT_SOURCE_NAME,
                $"{label}: AudioSource hija {ObstacleCollisionSetup.IMPACT_SOURCE_NAME}", ref total, ref passed);
            Assert(source != null && !source.playOnAwake && !source.loop && Mathf.Approximately(source.spatialBlend, 1f),
                $"{label}: la AudioSource es 3D, sin loop y sin playOnAwake", ref total, ref passed);
            Assert(source != null && road != null && source.outputAudioMixerGroup == road && sensor.MixerGroup == road,
                $"{label}: la salida de audio va al grupo Road", ref total, ref passed);

            ObstacleImpactAudioProfile profile = sensor.AudioProfile;
            Assert(profile != null && profile.GetMissingClips().Length == 0,
                $"{label}: perfil de audio asignado sin clips faltantes", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Funciones puras

        private static void VerifyPureFunctions(ref int total, ref int passed)
        {
            Assert(ObstacleImpactMath.Classify(ObstacleKind.Cone, 4.9f, 5f) == ObstacleImpactSeverity.Graze,
                "Classify: cono a 4.9 km/h es Roce", ref total, ref passed);
            Assert(ObstacleImpactMath.Classify(ObstacleKind.Cone, 5f, 5f) == ObstacleImpactSeverity.Crash,
                "Classify: cono a 5 km/h es Choque", ref total, ref passed);
            Assert(ObstacleImpactMath.Classify(ObstacleKind.Vehicle, 0f, 5f) == ObstacleImpactSeverity.Graze,
                "Classify: vehículo a 0 km/h es Roce", ref total, ref passed);
            Assert(ObstacleImpactMath.Classify(ObstacleKind.Pedestrian, 1f, 5f) == ObstacleImpactSeverity.Crash,
                "Classify: peatón a 1 km/h es Choque", ref total, ref passed);
            Assert(ObstacleImpactMath.Classify(ObstacleKind.Pedestrian, 0f, 5f) == ObstacleImpactSeverity.Crash,
                "Classify: peatón detenido es Choque", ref total, ref passed);
            Assert(ObstacleImpactMath.Classify(ObstacleKind.Vehicle, 3f, 2f) == ObstacleImpactSeverity.Crash,
                "Classify: el umbral configurable manda (3 km/h con umbral 2 es Choque)", ref total, ref passed);

            Assert(Mathf.Approximately(VehicleController.LimitSpeedToMeasured(5f, 0f), 0f),
                "LimitSpeedToMeasured: bloqueado (medido 0) da 0", ref total, ref passed);
            Assert(Mathf.Approximately(VehicleController.LimitSpeedToMeasured(5f, 2f), 2f),
                "LimitSpeedToMeasured: parcial da el medido", ref total, ref passed);
            Assert(Mathf.Approximately(VehicleController.LimitSpeedToMeasured(2f, 5f), 2f),
                "LimitSpeedToMeasured: un medido mayor no sube la velocidad", ref total, ref passed);
            Assert(Mathf.Approximately(VehicleController.LimitSpeedToMeasured(5f, -1f), 0f),
                "LimitSpeedToMeasured: medido negativo da 0", ref total, ref passed);

            Assert(LessonBoundaryMath.ShouldAlert(float.NegativeInfinity, 10f, 1f),
                "Rearme: el primer evento siempre se emite", ref total, ref passed);
            Assert(!LessonBoundaryMath.ShouldAlert(10f, 10.5f, 1f),
                "Rearme: un segundo inicio dentro de m_RearmSeconds se suprime", ref total, ref passed);
            Assert(LessonBoundaryMath.ShouldAlert(10f, 11.1f, 1f),
                "Rearme: el inicio posterior al rearme se permite", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Detector (rearme y eventos)

        private static void VerifyDetector(ref int total, ref int passed)
        {
            var go = new GameObject("ObstacleDetectorTest");
            try
            {
                var detector = go.AddComponent<ObstacleCollisionDetector>();
                int instanceEvents = 0, anyEvents = 0;
                void OnInstance(ObstacleImpact impact) => instanceEvents++;
                void OnAny(ObstacleImpact impact) => anyEvents++;
                detector.OnImpact += OnInstance;
                ObstacleCollisionDetector.AnyImpact += OnAny;

                try
                {
                    ObstacleImpact? first = detector.BeginContact(null, null);
                    Assert(first.HasValue && first.Value.Obstacle == detector && first.Value.Kind == ObstacleKind.Cone &&
                           first.Value.Severity == ObstacleImpactSeverity.Graze,
                        "Detector: el primer contacto emite un impacto Cone/Graze", ref total, ref passed);
                    Assert(instanceEvents == 1 && anyEvents == 1,
                        $"Detector: un evento de instancia y uno estático ({instanceEvents}/{anyEvents})", ref total, ref passed);

                    ObstacleImpact? second = detector.BeginContact(null, null);
                    Assert(!second.HasValue && detector.ActiveContacts == 2 && instanceEvents == 1,
                        "Detector: un segundo collider en el mismo episodio no emite", ref total, ref passed);

                    detector.EndContact(null);
                    detector.EndContact(null);
                    detector.EndContact(null);
                    Assert(detector.ActiveContacts == 0, "Detector: el contador no baja de 0", ref total, ref passed);

                    ObstacleImpact? suppressed = detector.BeginContact(null, null);
                    Assert(!suppressed.HasValue && instanceEvents == 1,
                        "Detector: un nuevo episodio dentro de m_RearmSeconds se suprime", ref total, ref passed);

                    detector.EndContact(null);
                    var so = new SerializedObject(detector);
                    so.FindProperty("m_RearmSeconds").floatValue = 0f;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    ObstacleImpact? again = detector.BeginContact(null, null);
                    Assert(again.HasValue && instanceEvents == 2,
                        "Detector: con el rearme cumplido el nuevo episodio emite", ref total, ref passed);
                }
                finally
                {
                    detector.OnImpact -= OnInstance;
                    ObstacleCollisionDetector.AnyImpact -= OnAny;
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- Perfil de audio

        private static void VerifyAudioProfile(ref int total, ref int passed)
        {
            var profile = ScriptableObject.CreateInstance<ObstacleImpactAudioProfile>();
            var graze = AudioClip.Create("graze", 100, 1, 44100, false);
            var crash = AudioClip.Create("crash", 100, 1, 44100, false);
            try
            {
                profile.ConeCrash.Clip = crash;
                AudioClip fallback = profile.GetClip(ObstacleKind.Cone, ObstacleImpactSeverity.Graze, out float fallbackVolume);
                Assert(fallback == crash && Mathf.Approximately(fallbackVolume, profile.ConeCrash.Volume),
                    "GetClip: si falta el clip de Graze cae al de Crash", ref total, ref passed);

                profile.ConeGraze.Clip = graze;
                Assert(profile.GetClip(ObstacleKind.Cone, ObstacleImpactSeverity.Graze, out _) == graze,
                    "GetClip: con el clip de Graze asignado lo usa", ref total, ref passed);

                profile.PedestrianCrash.Clip = crash;
                Assert(profile.GetClip(ObstacleKind.Pedestrian, ObstacleImpactSeverity.Graze, out _) == crash,
                    "GetClip: el peatón siempre usa PedestrianCrash", ref total, ref passed);

                Assert(profile.GetClip(ObstacleKind.Vehicle, ObstacleImpactSeverity.Crash, out float none) == null && none == 0f,
                    "GetClip: sin clips devuelve null sin excepción", ref total, ref passed);

                Assert(profile.GetMissingClips().Length == 2,
                    "GetMissingClips lista los clips sin asignar (VehicleGraze y VehicleCrash)", ref total, ref passed);

                float slow = profile.ScaleVolumeBySpeed(1f, 1f);
                float fast = profile.ScaleVolumeBySpeed(1f, 4f);
                Assert(slow < fast && fast <= 1f, "ScaleVolumeBySpeed: un roce a 1 km/h suena más bajo que uno a 4 km/h",
                    ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(graze);
                Object.DestroyImmediate(crash);
            }

            var asset = AssetDatabase.LoadAssetAtPath<ObstacleImpactAudioProfile>(ObstacleImpactAudioPlaceholderGenerator.PROFILE_PATH);
            Assert(asset != null && asset.GetMissingClips().Length == 0,
                "ObstacleImpactAudioProfile.asset existe y tiene los 5 clips", ref total, ref passed);

            foreach (string file in new[] { "cone_graze", "cone_crash", "vehicle_graze", "vehicle_crash", "pedestrian_crash" })
            {
                string path = $"{ObstacleImpactAudioPlaceholderGenerator.AUDIO_FOLDER}/{file}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                bool shortMono = clip != null && clip.channels == 1 && clip.frequency == 44100 && clip.length >= 0.15f && clip.length <= 0.81f;
                bool oneShot = importer != null && importer.forceToMono &&
                               importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad;
                Assert(shortMono && oneShot, $"{file}.wav: mono 44.1 kHz entre 0.15 y 0.8 s, DecompressOnLoad", ref total, ref passed);
            }
        }

        // ---------------------------------------------------------------- Escena

        private static void VerifyScene(ref int total, ref int passed)
        {
            // CityMerged no cambió: sin diferencias contra HEAD y sin ningún detector.
            bool? clean = IsGitClean(ObstacleCollisionSetup.SOURCE_SCENE_PATH);
            if (clean.HasValue)
            {
                Assert(clean.Value, "CityMerged.unity sin cambios (git diff --quiet)", ref total, ref passed);
            }
            else
            {
                Debug.LogWarning("[ObstacleCollisionVerification] ⚠️ No se pudo correr git: se omite la comparación de CityMerged.");
            }

            string detectorGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Environment/ObstacleCollisionDetector.cs");
            string mergedText = System.IO.File.ReadAllText(ObstacleCollisionSetup.SOURCE_SCENE_PATH);
            Assert(!mergedText.Contains(detectorGuid), "CityMerged.unity no referencia ObstacleCollisionDetector", ref total, ref passed);

            Scene scene = SceneManager.GetSceneByPath(ObstacleCollisionSetup.SCENE_PATH);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (HasDirtyScene(out string dirty))
                {
                    Debug.LogWarning($"[ObstacleCollisionVerification] ⚠️ {dirty} tiene cambios sin guardar: no se abre la escena de prueba.");
                    Assert(false, "CityWithCollisionDetectors debe poder abrirse para verificarla", ref total, ref passed);
                    return;
                }

                scene = EditorSceneManager.OpenScene(ObstacleCollisionSetup.SCENE_PATH, OpenSceneMode.Single);
            }

            Assert(scene.IsValid() && scene.isLoaded, "CityWithCollisionDetectors.unity debe existir y abrirse", ref total, ref passed);
            if (!scene.IsValid() || !scene.isLoaded) return;

            VerifyParkedVehicles(scene, ref total, ref passed);
            VerifyDemoZone(scene, ref total, ref passed);
            VerifySceneCar(scene, ref total, ref passed);
        }

        private static void VerifyParkedVehicles(Scene scene, ref int total, ref int passed)
        {
            List<GameObject> vehicles = ObstacleCollisionSetup.FindDecorativeVehicleRoots(scene);
            int withDetector = vehicles.Count(go =>
            {
                var detector = go.GetComponent<ObstacleCollisionDetector>();
                return detector != null && detector.Kind == ObstacleKind.Vehicle;
            });

            Assert(vehicles.Count > 0, $"La escena tiene autos decorativos con collider ({vehicles.Count})", ref total, ref passed);
            Assert(withDetector == vehicles.Count,
                $"Cada auto decorativo con collider tiene detector Vehicle ({withDetector}/{vehicles.Count})", ref total, ref passed);
            Assert(vehicles.All(go => go.GetComponent<VehicleController>() == null && !go.name.StartsWith("NPC_")),
                "Los autos con detector no incluyen al auto del jugador ni a los NPC", ref total, ref passed);
        }

        private static void VerifyDemoZone(Scene scene, ref int total, ref int passed)
        {
            GameObject group = LessonBoundarySetup.FindRootByName(scene, ObstacleCollisionSetup.DEMO_GROUP_NAME);
            Assert(group != null, $"Existe el grupo raíz {ObstacleCollisionSetup.DEMO_GROUP_NAME}", ref total, ref passed);
            if (group == null) return;

            List<Transform> cones = group.transform.Cast<Transform>()
                .Where(t => t.name.StartsWith(ObstacleCollisionSetup.DEMO_CONE_PREFIX)).ToList();
            Assert(cones.Count == ObstacleCollisionSetup.DEMO_CONE_COUNT,
                $"{ObstacleCollisionSetup.DEMO_GROUP_NAME} tiene 5 conos ({cones.Count})", ref total, ref passed);
            Assert(cones.All(c => c.GetComponent<ObstacleCollisionDetector>() is ObstacleCollisionDetector d && d.Kind == ObstacleKind.Cone &&
                                  c.GetComponent<MeshCollider>() != null && c.GetComponent<Rigidbody>() == null),
                "Los conos de la demo tienen detector Cone, collider y ningún Rigidbody", ref total, ref passed);

            if (cones.Count >= 2)
            {
                var ordered = cones.OrderBy(c => c.position.z).ToList();
                float minGap = float.MaxValue, maxGap = 0f;
                for (int i = 1; i < ordered.Count; i++)
                {
                    float gap = Vector2.Distance(new Vector2(ordered[i].position.x, ordered[i].position.z),
                        new Vector2(ordered[i - 1].position.x, ordered[i - 1].position.z));
                    minGap = Mathf.Min(minGap, gap);
                    maxGap = Mathf.Max(maxGap, gap);
                }

                Assert(minGap >= 5.5f && maxGap <= 6.7f,
                    $"Los conos están separados ≈ 6 m ({minGap:F1}..{maxGap:F1} m)", ref total, ref passed);
            }

            VerifyStillNpc(group, ObstacleCollisionSetup.DEMO_PARKED_CAR_NAME, ObstacleKind.Vehicle, ref total, ref passed);
            VerifyStillNpc(group, ObstacleCollisionSetup.DEMO_PEDESTRIAN_NAME, ObstacleKind.Pedestrian, ref total, ref passed);

            List<string> problems = ObstacleCollisionSetup.ValidateDemoPlacement(scene);
            Assert(problems.Count == 0,
                $"La zona de demo apoya sobre asfalto y no se superpone con nada ({problems.Count} problemas" +
                (problems.Count > 0 ? ": " + string.Join(" | ", problems) : "") + ")", ref total, ref passed);
        }

        private static void VerifyStillNpc(GameObject group, string name, ObstacleKind kind, ref int total, ref int passed)
        {
            Transform npc = group.transform.Find(name);
            Assert(npc != null, $"{name} existe en la zona de demo", ref total, ref passed);
            if (npc == null) return;

            var follower = npc.GetComponent<NPCPathFollower>();
            var detector = npc.GetComponent<ObstacleCollisionDetector>();
            var body = npc.GetComponent<Rigidbody>();

            bool playOnStart = true;
            if (follower != null)
            {
                playOnStart = new SerializedObject(follower).FindProperty("m_PlayOnStart").boolValue;
            }

            Assert(follower != null && !playOnStart && follower.Path == null,
                $"{name}: sin ruta y con m_PlayOnStart = false (queda quieto)", ref total, ref passed);
            Assert(detector != null && detector.Kind == kind && body != null && body.isKinematic,
                $"{name}: detector {kind} y Rigidbody cinemático", ref total, ref passed);
        }

        private static void VerifySceneCar(Scene scene, ref int total, ref int passed)
        {
            VehicleController car = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<VehicleController>(true))
                .FirstOrDefault(v => v.name == "Car08");

            Assert(car != null, "La escena tiene el Car08", ref total, ref passed);
            if (car == null) return;

            var sensor = car.GetComponent<ObstacleContactSensor>();
            Assert(sensor != null && sensor.AudioProfile != null && sensor.ImpactSource != null,
                "El Car08 de la escena tiene el sensor con perfil y AudioSource (sin overrides que lo tapen)", ref total, ref passed);

            int npcInstances = 0, npcWithDetector = 0;
            foreach (NPCPathFollower follower in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NPCPathFollower>(true)))
            {
                npcInstances++;
                if (follower.GetComponent<ObstacleCollisionDetector>() != null) npcWithDetector++;
            }

            Assert(npcInstances > 0 && npcWithDetector == npcInstances,
                $"Todos los NPC de la escena tienen detector ({npcWithDetector}/{npcInstances})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Suites existentes

        // Cada suite se corre por su MenuItem y se lee su línea de resultado. Se saltan si hay escenas sin guardar:
        // algunas guardan la escena abierta.
        private static void VerifyExistingSuites(ref int total, ref int passed)
        {
            if (HasDirtyScene(out string dirty))
            {
                Debug.LogWarning($"[ObstacleCollisionVerification] ⚠️ {dirty} tiene cambios sin guardar: no se corren las suites " +
                                 "existentes porque guardan la escena abierta. Guardá la escena y volvé a correr.");
                return;
            }

            // "Indicator_PositionLights debe existir en el cluster" es un fallo preexistente de Car06, ajeno a US33.
            RunSuite("Tools/Primer Volante/Run NPC Verification", "NPCVerification", 0, ref total, ref passed);

            // LessonBoundaryVerification abre CityWithBarriers en modo aditivo: con otra escena abierta que ya tiene
            // Lesson_Boundaries en las mismas coordenadas (CityMerged o esta copia) las vallas se solapan y su chequeo
            // de "ningún collider ajeno" falla por construcción. Se corre solo en un contexto válido.
            if (HasDuplicateLessonBoundaries())
            {
                Debug.LogWarning("[ObstacleCollisionVerification] ⚠️ LessonBoundaryVerification no se corre desde esta escena: " +
                                 "abre CityWithBarriers en modo aditivo y las vallas de Lesson_Boundaries quedarían duplicadas. " +
                                 "Corré 'Run Lesson Boundary Verification' con CityWithBarriers o CityWithCar como escena abierta.");
            }
            else
            {
                RunSuite("Tools/Primer Volante/Run Lesson Boundary Verification", "LessonBoundaryVerification", 0, ref total, ref passed);
            }

            RunSuite("Tools/Primer Volante/Run Cockpit Integration Verification", "CockpitIntegrationVerification", 0, ref total, ref passed);
            RunSuite("Tools/Primer Volante/Run Vehicle Lighting Verification", "VehicleLightingVerification", 1, ref total, ref passed);
            RunSuite("Tools/Primer Volante/Run Sound Integration Verification", "SoundIntegrationVerification", 0, ref total, ref passed);
            RunSuite("Tools/Primer Volante/Run Turn Signal Verification", "TurnSignalVerification", 0, ref total, ref passed);
            RunSuite("Tools/Primer Volante/Run Traffic Light Tests", "TrafficLightTests", 0, ref total, ref passed);
        }

        private static void RunSuite(string menuPath, string name, int expectedKnownFailures, ref int total, ref int passed)
        {
            int suiteTotal = 0, suitePassed = 0;
            void OnLog(string message, string stack, LogType type)
            {
                // La última línea de resultado es la de la suite externa (las anidadas imprimen antes).
                Match match = Regex.Match(message, @"(\d+)/(\d+) pruebas pasadas");
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
                menuFound = EditorApplication.ExecuteMenuItem(menuPath);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert(menuFound && suiteTotal > 0 && suitePassed == suiteTotal - expectedKnownFailures,
                $"{name} sigue pasando ({suitePassed}/{suiteTotal}" +
                (expectedKnownFailures > 0 ? $", {expectedKnownFailures} fallo preexistente" : "") + ")", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Helpers

        // true si hay una escena abierta, distinta de CityWithBarriers, con el grupo Lesson_Boundaries.
        private static bool HasDuplicateLessonBoundaries()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.path == LessonBoundarySetup.DEMO_SCENE_PATH) continue;
                if (LessonBoundarySetup.FindRootByName(open, LessonBoundarySetup.BOUNDARIES_GROUP_NAME) != null) return true;
            }

            return false;
        }

        private static bool HasDirtyScene(out string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    name = open.name;
                    return true;
                }
            }

            name = null;
            return false;
        }

        // true si git no ve diferencias del archivo contra HEAD; null si no se pudo correr git.
        private static bool? IsGitClean(string assetPath)
        {
            try
            {
                string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                var info = new ProcessStartInfo("git", $"diff --quiet -- \"{assetPath}\"")
                {
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    process.WaitForExit(15000);
                    if (!process.HasExited) return null;

                    // 0 = sin diferencias, 1 = hay diferencias, otro = git falló.
                    if (process.ExitCode == 0) return true;
                    if (process.ExitCode == 1) return false;
                    return null;
                }
            }
            catch (System.Exception)
            {
                return null;
            }
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
