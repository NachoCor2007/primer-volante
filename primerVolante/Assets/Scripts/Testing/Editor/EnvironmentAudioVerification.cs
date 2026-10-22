using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PrimerVolante.Audio;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de US20 (#12): mixer del entorno, perfiles, importación de clips, prefabs NPC,
    /// escena <c>CityMerged3</c>, funciones puras, <c>LessonStepSO</c>, presupuesto de voces y regresión de las suites
    /// existentes. El comportamiento en movimiento (pesos con el oyente en distintas posiciones, ducking, pitch) se
    /// prueba en Play Mode.
    /// </summary>
    public static class EnvironmentAudioVerification
    {
        // Parámetros expuestos del EnvironmentAudioMixer.
        private static readonly string[] s_ExposedParameters =
        {
            "ExteriorCutoff", "AlertsCutoff", "AmbienceVolume", "NPCVolume", "AlertsVolume"
        };

        [MenuItem("Tools/Primer Volante/Run Environment Audio Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE AUDIO DEL ENTORNO (US20 #12)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            AudioMixer mixer = VerifyMixer(ref total, ref passed);
            VerifyProfiles(ref total, ref passed);
            VerifyClips(ref total, ref passed);
            VerifyPrefabs(mixer, ref total, ref passed);
            VerifyScene(mixer, ref total, ref passed);
            VerifyPureFunctions(ref total, ref passed);
            VerifyLessonStep(ref total, ref passed);
            VerifyVoiceBudget(ref total, ref passed);
            VerifyExistingSuites(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Mixer

        private static AudioMixer VerifyMixer(ref int total, ref int passed)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(EnvironmentAudioSetup.MIXER_PATH);
            Assert(mixer != null, "EnvironmentAudioMixer.mixer existe", ref total, ref passed);
            if (mixer == null) return null;

            foreach (string group in new[] { "Master", "Exterior", "Ambience", "NPC", "Alerts" })
            {
                bool found = mixer.FindMatchingGroups(group).Any(g => g.name == group);
                Assert(found, $"Mixer: grupo '{group}'", ref total, ref passed);
            }

            foreach (string parameter in s_ExposedParameters)
            {
                bool exposed = mixer.GetFloat(parameter, out _);
                Assert(exposed, $"Mixer: parámetro expuesto '{parameter}'", ref total, ref passed);
            }

            return mixer;
        }

        // ---------------------------------------------------------------- Perfiles

        private static void VerifyProfiles(ref int total, ref int passed)
        {
            var profiles = EnvironmentAudioSetup.AllProfiles().ToList();
            Assert(profiles.Count == 15, $"Existen los 15 perfiles de audio (encontrados {profiles.Count})", ref total, ref passed);

            var allNames = EnvironmentAudioSetup.ZoneProfileNames
                .Concat(EnvironmentAudioSetup.EmitterProfileNames)
                .Concat(new[]
                {
                    EnvironmentAudioSetup.NPC_VEHICLE_DEFAULT, EnvironmentAudioSetup.NPC_VEHICLE_HEAVY,
                    EnvironmentAudioSetup.NPC_PEDESTRIAN_DEFAULT, EnvironmentAudioSetup.ALERTS_DEFAULT
                });
            foreach (string name in allNames)
            {
                var profile = EnvironmentAudioSetup.LoadProfile<EnvironmentAudioProfileBase>(name);
                Assert(profile != null, $"Perfil {name} existe", ref total, ref passed);
                if (profile == null) continue;

                string[] missing = profile.GetMissingPlaceholders();
                Assert(missing.Length == 0, $"{name}: sin placeholders faltantes" + (missing.Length > 0 ? $" (faltan: {string.Join(", ", missing)})" : string.Empty),
                    ref total, ref passed);

                // Los orgánicos pendientes son información: no hacen fallar la suite.
                string[] pending = profile.GetPendingOrganic();
                if (pending.Length > 0) Debug.Log($"  ℹ️ [INFO] {name}: orgánicos pendientes → {string.Join(", ", pending)}");
            }

            var heavy = EnvironmentAudioSetup.LoadProfile<NPCVehicleAudioProfile>(EnvironmentAudioSetup.NPC_VEHICLE_HEAVY);
            var normal = EnvironmentAudioSetup.LoadProfile<NPCVehicleAudioProfile>(EnvironmentAudioSetup.NPC_VEHICLE_DEFAULT);
            Assert(heavy != null && normal != null && heavy.MaxPitch < normal.MaxPitch && heavy.MinPitch < normal.MinPitch,
                "Perfil Heavy: pitch más grave que el Default", ref total, ref passed);
            var stadium = EnvironmentAudioSetup.LoadProfile<AmbientEmitterProfile>(EnvironmentAudioSetup.EMITTER_STADIUM);
            Assert(stadium != null && stadium.MaxDistance >= 120f, "Emitter_Stadium: maxDistance ≈ 120", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Clips

        private static void VerifyClips(ref int total, ref int passed)
        {
            foreach (var entry in EnvironmentAudioPlaceholderGenerator.Files)
            {
                string path = EnvironmentAudioPlaceholderGenerator.PathOf(entry.file);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert(clip != null && importer != null, $"Clip {entry.file} existe e importa", ref total, ref passed);
                if (clip == null || importer == null) continue;

                bool bed = entry.kind == EnvironmentClipKind.BedStreaming;
                AudioImporterSampleSettings s = importer.defaultSampleSettings;

                // Todo lo que suena en 3D es mono; los lechos 2D pueden ser estéreo.
                Assert(bed || (importer.forceToMono && clip.channels == 1), $"{entry.file}: 3D en mono (forceToMono)", ref total, ref passed);
                Assert(clip.frequency == 44100, $"{entry.file}: 44.1 kHz", ref total, ref passed);

                AudioClipLoadType expected;
                switch (entry.kind)
                {
                    case EnvironmentClipKind.BedStreaming: expected = AudioClipLoadType.Streaming; break;
                    case EnvironmentClipKind.LoopMedium: expected = AudioClipLoadType.CompressedInMemory; break;
                    default: expected = AudioClipLoadType.DecompressOnLoad; break;
                }

                Assert(s.loadType == expected, $"{entry.file}: Load Type {expected} (es {s.loadType})", ref total, ref passed);
            }

            // Loops: continuos (el último sample enlaza con el primero).
            foreach (string loop in new[] { "bed_city_traffic_loop.wav", "bed_town_wind_loop.wav", "bed_plaza_air_loop.wav", "siren_wail_loop.wav", "car_alarm_loop.wav", "factory_hum_loop.wav" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(EnvironmentAudioPlaceholderGenerator.PathOf(loop));
                Assert(clip != null && clip.length >= 2f, $"{loop}: loop de al menos 2 s", ref total, ref passed);
            }
        }

        // ---------------------------------------------------------------- Prefabs NPC

        private static void VerifyPrefabs(AudioMixer mixer, ref int total, ref int passed)
        {
            string[] vehicles = ObstacleCollisionSetup.FindNpcVehiclePaths();
            string[] pedestrians = ObstacleCollisionSetup.FindNpcPedestrianPaths();
            Assert(vehicles.Length == 21, $"Deben existir 21 vehículos NPC (encontrados {vehicles.Length})", ref total, ref passed);
            Assert(pedestrians.Length == 4, $"Deben existir 4 peatones NPC (encontrados {pedestrians.Length})", ref total, ref passed);

            var heavy = EnvironmentAudioSetup.LoadProfile<NPCVehicleAudioProfile>(EnvironmentAudioSetup.NPC_VEHICLE_HEAVY);
            var normal = EnvironmentAudioSetup.LoadProfile<NPCVehicleAudioProfile>(EnvironmentAudioSetup.NPC_VEHICLE_DEFAULT);
            var pedProfile = EnvironmentAudioSetup.LoadProfile<NPCPedestrianAudioProfile>(EnvironmentAudioSetup.NPC_PEDESTRIAN_DEFAULT);
            AudioMixerGroup npcGroup = EnvironmentAudioSetup.FindGroup(mixer, "NPC");
            AudioMixerGroup alertsGroup = EnvironmentAudioSetup.FindGroup(mixer, "Alerts");

            foreach (string path in vehicles)
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var audio = prefab.GetComponent<NPCVehicleAudio>();
                Assert(audio != null, $"{label}: NPCVehicleAudio en la raíz", ref total, ref passed);
                if (audio == null) continue;

                bool isHeavy = EnvironmentAudioSetup.IsHeavyVehicle(label);
                Assert(audio.Profile == (isHeavy ? heavy : normal), $"{label}: perfil {(isHeavy ? "Heavy" : "Default")}", ref total, ref passed);

                bool isSiren = EnvironmentAudioSetup.IsSirenVehicle(label);
                Assert((audio.SirenClip != null) == isSiren && !audio.SirenEnabled,
                    $"{label}: sirena {(isSiren ? "asignada" : "sin asignar")} y apagada", ref total, ref passed);

                VerifyNpcSources(label, new[] { audio.EngineIdleSource, audio.EngineMidSource, audio.BrakeSource }, npcGroup, ref total, ref passed);
                VerifyNpcSources(label + " (alertas)", new[] { audio.AlertSource, audio.SirenSource }, alertsGroup, ref total, ref passed);
            }

            foreach (string path in pedestrians)
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var audio = prefab.GetComponent<NPCPedestrianAudio>();
                Assert(audio != null && audio.Profile == pedProfile, $"{label}: NPCPedestrianAudio con perfil", ref total, ref passed);
                if (audio == null) continue;

                VerifyNpcSources(label, new[] { audio.FootstepSource, audio.VoiceSource }, npcGroup, ref total, ref passed);
            }
        }

        private static void VerifyNpcSources(string label, AudioSource[] sources, AudioMixerGroup expectedGroup, ref int total, ref int passed)
        {
            bool ok = expectedGroup != null && sources.All(s =>
                s != null && !s.playOnAwake && Mathf.Approximately(s.spatialBlend, 1f) && s.outputAudioMixerGroup == expectedGroup);
            Assert(ok, $"{label}: AudioSource con playOnAwake=false, spatialBlend=1 y grupo {(expectedGroup != null ? expectedGroup.name : "?")}",
                ref total, ref passed);
        }

        // ---------------------------------------------------------------- Escena

        private static void VerifyScene(AudioMixer mixer, ref int total, ref int passed)
        {
            Scene scene = default;
            bool openedHere = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).path == EnvironmentAudioSetup.CITY_SCENE_PATH) scene = SceneManager.GetSceneAt(i);
            }

            if (!scene.IsValid())
            {
                scene = EditorSceneManager.OpenScene(EnvironmentAudioSetup.CITY_SCENE_PATH, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                VerifySceneContents(scene, mixer, ref total, ref passed);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void VerifySceneContents(Scene scene, AudioMixer mixer, ref int total, ref int passed)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject env = roots.FirstOrDefault(g => g.name == EnvironmentAudioSetup.ROOT_NAME);
            Assert(env != null, "CityMerged3: existe Audio_Environment", ref total, ref passed);
            if (env == null) return;

            AmbienceDirector[] directors = roots.SelectMany(r => r.GetComponentsInChildren<AmbienceDirector>(true)).ToArray();
            Assert(directors.Length == 1 && directors[0].gameObject == env, "Un solo AmbienceDirector, en Audio_Environment", ref total, ref passed);
            AmbienceDirector director = directors.FirstOrDefault();

            AudioMixerGroup ambience = EnvironmentAudioSetup.FindGroup(mixer, "Ambience");
            AudioMixerGroup alerts = EnvironmentAudioSetup.FindGroup(mixer, "Alerts");

            if (director != null)
            {
                Assert(director.Mixer == mixer && director.AmbienceGroup == ambience,
                    "AmbienceDirector: mixer del entorno y grupo Ambience asignados", ref total, ref passed);
                Assert(director.FallbackProfile == EnvironmentAudioSetup.LoadProfile<AmbienceZoneProfile>(EnvironmentAudioSetup.ZONE_TOWN),
                    "AmbienceDirector: perfil de respaldo = Pueblo", ref total, ref passed);
                Assert(Mathf.Approximately(director.ExteriorCutoffHz, 2200f) && Mathf.Approximately(director.AlertsCutoffHz, 6000f),
                    "AmbienceDirector: corte de cabina 2200 Hz (exterior) y 6000 Hz (alertas)", ref total, ref passed);
            }

            // ---- Zonas
            AmbienceZone[] zones = env.GetComponentsInChildren<AmbienceZone>(true);
            Assert(zones.Length == 4 && zones.All(z => z.Profile != null), $"4 zonas con perfil (encontradas {zones.Length})", ref total, ref passed);
            AmbienceZone plazaWest = zones.FirstOrDefault(z => z.name == "Zone_Plaza_West");
            AmbienceZone plazaEast = zones.FirstOrDefault(z => z.name == "Zone_Plaza_East");
            Assert(plazaWest != null && plazaEast != null && plazaWest.Profile == plazaEast.Profile,
                "Las dos mitades de la plaza comparten perfil", ref total, ref passed);
            Assert(plazaWest != null && zones.Where(z => !z.name.StartsWith("Zone_Plaza")).All(z => plazaWest.Priority > z.Priority),
                "La plaza tiene prioridad sobre la ciudad y el pueblo", ref total, ref passed);

            // Pesos en tres puntos conocidos y a lo largo del camino ciudad → pueblo.
            if (director != null && zones.Length == 4)
            {
                VerifyWeightsAtPoints(director, zones, ref total, ref passed);
            }

            // ---- Emisores
            AmbientEmitter[] emitters = env.GetComponentsInChildren<AmbientEmitter>(true);
            var expected = EnvironmentAudioSetup.ComputeEmitters(scene);
            Assert(emitters.Length == expected.Count && expected.Count > 0,
                $"Emisores en la cantidad esperada ({emitters.Length}/{expected.Count})", ref total, ref passed);
            string perProfile = string.Join(", ", emitters.Where(e => e.Profile != null).GroupBy(e => e.Profile.name)
                .OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
            Debug.Log($"  ℹ️ [INFO] Emisores por perfil: {perProfile}");

            bool emittersOk = emitters.All(e =>
            {
                var src = e.GetComponent<AudioSource>();
                return e.Profile != null && src != null && !src.playOnAwake && Mathf.Approximately(src.spatialBlend, 1f)
                       && Mathf.Approximately(src.dopplerLevel, 0f) && src.rolloffMode == AudioRolloffMode.Logarithmic
                       && src.outputAudioMixerGroup == ambience && src.priority == EnvironmentAudioUtil.PriorityEmitter;
            });
            Assert(emittersOk, "Todos los emisores: perfil, 3D, rolloff logarítmico, sin Doppler, grupo Ambience, prioridad 160", ref total, ref passed);

            // Casas con perro: 1 de cada 3.
            int houseCount = roots.Count(r => (EnvironmentAudioSetup.OriginName(r) ?? string.Empty).StartsWith("Building_House"));
            int dogCount = emitters.Count(e => e.Profile != null && e.Profile.name == EnvironmentAudioSetup.EMITTER_DOG);
            Assert(houseCount > 0 && dogCount == Mathf.CeilToInt(houseCount / 3f), $"Perros: 1 de cada 3 casas ({dogCount} de {houseCount})", ref total, ref passed);

            // ---- Semáforo sonoro y bocina
            TrafficLightController[] lights = roots.SelectMany(r => r.GetComponentsInChildren<TrafficLightController>(true)).ToArray();
            GameObject intersection = roots.FirstOrDefault(g => g.name == EnvironmentAudioSetup.INTERSECTION_NAME);
            TrafficLightController playerSignal = intersection != null
                ? intersection.GetComponentsInChildren<TrafficLightController>(true).FirstOrDefault(t => t.name == EnvironmentAudioSetup.PLAYER_SIGNAL_NAME)
                : null;
            var beeper = playerSignal != null ? playerSignal.GetComponent<CrosswalkBeeper>() : null;
            Assert(beeper != null && beeper.TrafficLight == playerSignal && beeper.Source != null && beeper.Source.outputAudioMixerGroup == alerts
                   && !beeper.Source.playOnAwake && Mathf.Approximately(beeper.Source.spatialBlend, 1f),
                "CrosswalkBeeper en el semáforo de Controlled_Intersection_T (3D, grupo Alerts)", ref total, ref passed);
            int withBeeper = lights.Count(l => l.GetComponent<CrosswalkBeeper>() != null);
            Debug.Log($"  ℹ️ [INFO] TrafficLightController en la escena: {lights.Length} (con CrosswalkBeeper: {withBeeper}). Los {lights.Length - withBeeper} " +
                      "restantes son señales decorativas de los props y no llevan beeper (sonarían en cada esquina de la ciudad).");

            var honk = intersection != null ? intersection.GetComponent<GreenLightHonk>() : null;
            Assert(honk != null && honk.TrafficLight == playerSignal && honk.StopLineTrigger != null && honk.Source != null
                   && honk.Source.outputAudioMixerGroup == alerts && Mathf.Approximately(honk.Source.spatialBlend, 1f),
                "GreenLightHonk en Controlled_Intersection_T con semáforo, trigger de la línea y fuente 3D (Alerts)", ref total, ref passed);

            // ---- Alarma de autos estacionados
            ParkedCarAlarm[] alarms = roots.SelectMany(r => r.GetComponentsInChildren<ParkedCarAlarm>(true)).ToArray();
            Assert(alarms.Length == 1 && alarms[0].Source != null && alarms[0].Source.outputAudioMixerGroup == alerts
                   && alarms[0].Profile != null && Mathf.Approximately(alarms[0].Source.spatialBlend, 1f) && !alarms[0].Source.playOnAwake,
                "Un solo ParkedCarAlarm con fuente 3D en Alerts", ref total, ref passed);

            // ---- Listener
            int listeners = roots.SelectMany(r => r.GetComponentsInChildren<AudioListener>(false)).Count(l => l.isActiveAndEnabled);
            Assert(listeners == 1, $"Un solo AudioListener activo en la jerarquía (hay {listeners})", ref total, ref passed);

            // ---- Ninguna fuente nueva en VehicleAudioMixer
            var vehicleMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Vehicle/VehicleAudioMixer.mixer");
            var newSources = new List<AudioSource>();
            newSources.AddRange(env.GetComponentsInChildren<AudioSource>(true));
            if (beeper != null) newSources.Add(beeper.Source);
            if (honk != null) newSources.Add(honk.Source);
            foreach (NPCVehicleAudio v in roots.SelectMany(r => r.GetComponentsInChildren<NPCVehicleAudio>(true)))
                newSources.AddRange(new[] { v.EngineIdleSource, v.EngineMidSource, v.BrakeSource, v.AlertSource, v.SirenSource });
            foreach (NPCPedestrianAudio p in roots.SelectMany(r => r.GetComponentsInChildren<NPCPedestrianAudio>(true)))
                newSources.AddRange(new[] { p.FootstepSource, p.VoiceSource });

            bool noneInVehicle = newSources.Count > 0 && newSources.All(s => s != null && s.outputAudioMixerGroup != null
                && s.outputAudioMixerGroup.audioMixer == mixer && s.outputAudioMixerGroup.audioMixer != vehicleMixer);
            Assert(noneInVehicle, $"Ninguna AudioSource nueva ({newSources.Count}) va a VehicleAudioMixer: todas al EnvironmentAudioMixer", ref total, ref passed);

            // ---- NPC de la escena
            int followers = 0, withAudio = 0;
            foreach (NPCPathFollower f in roots.SelectMany(r => r.GetComponentsInChildren<NPCPathFollower>(true)))
            {
                followers++;
                if (f.GetComponent<NPCVehicleAudio>() != null || f.GetComponent<NPCPedestrianAudio>() != null) withAudio++;
            }

            Assert(followers > 0 && withAudio == followers, $"Todos los NPC de la escena tienen audio ({withAudio}/{followers})", ref total, ref passed);

            // ---- Car09 sin cambios de audio propio
            GameObject car = roots.FirstOrDefault(g => g.name == "Car09");
            Assert(car != null && car.GetComponentsInChildren<AmbientEmitter>(true).Length == 0 && car.GetComponentsInChildren<NPCVehicleAudio>(true).Length == 0,
                "Car09: sin componentes de audio del entorno", ref total, ref passed);
        }

        private static void VerifyWeightsAtPoints(AmbienceDirector director, AmbienceZone[] zones, ref int total, ref int passed)
        {
            var city = EnvironmentAudioSetup.LoadProfile<AmbienceZoneProfile>(EnvironmentAudioSetup.ZONE_CITY);
            var plaza = EnvironmentAudioSetup.LoadProfile<AmbienceZoneProfile>(EnvironmentAudioSetup.ZONE_PLAZA);
            var town = EnvironmentAudioSetup.LoadProfile<AmbienceZoneProfile>(EnvironmentAudioSetup.ZONE_TOWN);
            var weights = new Dictionary<AmbienceZoneProfile, float>();

            float W(AmbienceZoneProfile p) => weights.TryGetValue(p, out float w) ? w : 0f;

            director.EvaluateWeights(new Vector3(100f, 1.2f, 50f), weights, zones);
            Assert(Mathf.Approximately(W(city), 1f) && W(plaza) < 0.001f && W(town) < 0.001f, "Pesos en (100, 50): Ciudad = 1", ref total, ref passed);

            director.EvaluateWeights(new Vector3(60f, 1.2f, 140f), weights, zones);
            Assert(Mathf.Approximately(W(plaza), 1f) && W(city) < 0.001f && W(town) < 0.001f, "Pesos en (60, 140): Plaza = 1", ref total, ref passed);

            director.EvaluateWeights(new Vector3(120f, 1.2f, 200f), weights, zones);
            Assert(Mathf.Approximately(W(town), 1f) && W(city) < 0.001f && W(plaza) < 0.001f, "Pesos en (120, 200): Pueblo = 1", ref total, ref passed);

            // Camino (100,100) → (100,170): transición gradual, sin saltos, con suma 1.
            float prevCity = 1f, prevTown = 0f, maxJump = 0f, minSum = 2f, maxSum = 0f;
            bool monotone = true;
            for (float z = 100f; z <= 170f; z += 1f)
            {
                director.EvaluateWeights(new Vector3(100f, 1.2f, z), weights, zones);
                float c = W(city), t = W(town), sum = weights.Values.Sum();
                if (c > prevCity + 1e-4f || t < prevTown - 1e-4f) monotone = false;
                maxJump = Mathf.Max(maxJump, Mathf.Abs(c - prevCity), Mathf.Abs(t - prevTown));
                minSum = Mathf.Min(minSum, sum);
                maxSum = Mathf.Max(maxSum, sum);
                prevCity = c;
                prevTown = t;
            }

            Assert(monotone && maxJump < 0.2f && minSum > 0.99f && maxSum < 1.01f,
                $"Transición (100,100) → (100,170): Ciudad baja y Pueblo sube sin saltos (salto máx. por metro {maxJump:F3}, suma {minSum:F2}..{maxSum:F2})",
                ref total, ref passed);
        }

        // ---------------------------------------------------------------- Funciones puras

        private static void VerifyPureFunctions(ref int total, ref int passed)
        {
            // ZoneWeight
            Assert(Approx(AmbienceMath.ZoneWeight(0f, 10f), 1f) && Approx(AmbienceMath.ZoneWeight(-3f, 10f), 1f),
                "ZoneWeight: dentro → 1", ref total, ref passed);
            Assert(Approx(AmbienceMath.ZoneWeight(10f, 10f), 0f) && Approx(AmbienceMath.ZoneWeight(25f, 10f), 0f),
                "ZoneWeight: en el borde del fade → 0", ref total, ref passed);
            Assert(Approx(AmbienceMath.ZoneWeight(5f, 10f), 0.5f), "ZoneWeight: a mitad del fade → 0.5", ref total, ref passed);
            Assert(Approx(AmbienceMath.ZoneWeight(0.1f, 0f), 0f), "ZoneWeight: sin fade y fuera → 0", ref total, ref passed);

            // NormalizeWeights
            var a = new[] { 0.8f, 0.7f };
            AmbienceMath.NormalizeWeights(a, new[] { 0, 0 });
            Assert(a.Sum() <= 1.0001f && Approx(a[0] / a[1], 0.8f / 0.7f), "NormalizeWeights: la suma no pasa de 1 y conserva la proporción con igual prioridad", ref total, ref passed);

            var b = new[] { 0.5f, 0.9f, 0.4f };
            AmbienceMath.NormalizeWeights(b, new[] { 0, 10, 0 });
            Assert(Approx(b[1], 0.9f) && b.Sum() <= 1.0001f && b[0] + b[2] <= 0.1001f, "NormalizeWeights: la prioridad alta conserva su peso", ref total, ref passed);

            var c = new[] { 1f, 1f };
            AmbienceMath.NormalizeWeights(c, new[] { 10, 0 });
            Assert(Approx(c[0], 1f) && Approx(c[1], 0f), "NormalizeWeights: la plaza dentro (1) deja a la ciudad en 0", ref total, ref passed);

            var d = new[] { 0.2f, 0.3f };
            AmbienceMath.NormalizeWeights(d, new[] { 5, 0 });
            Assert(Approx(d[0], 0.2f) && Approx(d[1], 0.3f), "NormalizeWeights: si la suma ya es ≤ 1 no cambia nada", ref total, ref passed);

            // SmoothVolume
            Assert(Approx(AmbienceMath.SmoothVolume(0f, 1f, 2f, 1f), 0.5f) && Approx(AmbienceMath.SmoothVolume(0.9f, 1f, 2f, 1f), 1f)
                   && Approx(AmbienceMath.SmoothVolume(1f, 0f, 2f, 0.5f), 0.75f), "SmoothVolume: velocidad constante y sin pasarse del objetivo", ref total, ref passed);

            // ScaledInterval
            Assert(Approx(AmbienceMath.ScaledInterval(10f, 1f, 1f), 10f) && Approx(AmbienceMath.ScaledInterval(10f, 0.5f, 1f), 20f)
                   && float.IsInfinity(AmbienceMath.ScaledInterval(10f, 0f, 1f)), "ScaledInterval: escala con el peso (peso 0 → infinito)", ref total, ref passed);
            Assert(Approx(AmbienceMath.ScaledInterval(10f, 1f, 0.5f), 20f) && Approx(AmbienceMath.ScaledInterval(10f, 1f, 2f), 5f),
                "ScaledInterval con intensidad: Calm ×0.5 → 20 s, Tense ×2 → 5 s", ref total, ref passed);

            // FootstepInterval
            Assert(float.IsInfinity(NPCAudioMath.FootstepInterval(0f, 0.7f)), "FootstepInterval: velocidad 0 → sin pasos", ref total, ref passed);
            Assert(Approx(NPCAudioMath.FootstepInterval(1.4f, 0.7f), 0.5f), "FootstepInterval: 1.4 m/s con paso de 0.7 m → 0.5 s", ref total, ref passed);

            // Motor
            bool monotone = true, inRange = true;
            float previous = float.NegativeInfinity;
            for (float speed = 0f; speed <= 40f; speed += 0.5f)
            {
                float pitch = NPCAudioMath.EnginePitch(speed, 14f, 0.9f, 1.5f);
                if (pitch < previous - 1e-5f) monotone = false;
                if (pitch < 0.9f - 1e-5f || pitch > 1.5f + 1e-5f) inRange = false;
                previous = pitch;
            }

            Assert(monotone && inRange, "EnginePitch: monótona y dentro del rango", ref total, ref passed);
            Assert(Approx(NPCAudioMath.EngineBlend(0f, 14f), 0f) && Approx(NPCAudioMath.EngineBlend(7f, 14f), 0.5f) && Approx(NPCAudioMath.EngineBlend(99f, 14f), 1f),
                "EngineBlend: 0 detenido, 0.5 a media velocidad de referencia, 1 por encima", ref total, ref passed);

            // Bocina de impaciencia
            Assert(!NPCAudioMath.ShouldImpatientHonk(3f, 0f, 4f, 1f), "ShouldImpatientHonk: no antes de la demora", ref total, ref passed);
            Assert(!NPCAudioMath.ShouldImpatientHonk(10f, 12f, 4f, 1f), "ShouldImpatientHonk: no si se mueve", ref total, ref passed);
            Assert(NPCAudioMath.ShouldImpatientHonk(5f, 0f, 4f, 1f), "ShouldImpatientHonk: sí después de la demora y quieto", ref total, ref passed);
            Assert(!NPCAudioMath.ShouldImpatientHonk(60f, 0f, 0f, 1f), "ShouldImpatientHonk: demora 0 (Calm) la desactiva", ref total, ref passed);

            // Demoras por intensidad
            Assert(NPCAudioMath.DefaultNpcHonkDelays[0] > NPCAudioMath.DefaultNpcHonkDelays[1] && NPCAudioMath.DefaultNpcHonkDelays[1] > NPCAudioMath.DefaultNpcHonkDelays[2],
                "Demora de bocina NPC: Calm > Normal > Tense (8 / 5 / 2.5 s)", ref total, ref passed);

            // EdgeFade
            Assert(Approx(AmbienceMath.EdgeFade(10f, 50f), 1f) && Approx(AmbienceMath.EdgeFade(50f, 50f), 0f) && Approx(AmbienceMath.EdgeFade(42.5f, 50f), 0.5f),
                "EdgeFade: 1 cerca, 0 en maxDistance", ref total, ref passed);
        }

        // ---------------------------------------------------------------- LessonStepSO

        private static void VerifyLessonStep(ref int total, ref int passed)
        {
            var step = ScriptableObject.CreateInstance<LessonStepSO>();
            Assert(step.ambienceIntensity == AmbienceIntensityOverride.Unchanged, "LessonStepSO: ambienceIntensity por defecto = Unchanged", ref total, ref passed);
            Object.DestroyImmediate(step);

            string[] guids = AssetDatabase.FindAssets("t:LessonStepSO");
            int changed = guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<LessonStepSO>)
                .Count(s => s != null && s.ambienceIntensity != AmbienceIntensityOverride.Unchanged);
            Assert(guids.Length > 0 && changed == 0, $"Los {guids.Length} pasos de lección existentes quedan en Unchanged", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Voces

        private static void VerifyVoiceBudget(ref int total, ref int passed)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            int real = assets != null && assets.Length > 0 ? new SerializedObject(assets[0]).FindProperty("m_RealVoiceCount").intValue : -1;
            Assert(real == 64, $"AudioManager: m_RealVoiceCount = 64 (es {real})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Regresión

        // Cada suite se corre por su MenuItem y se lee su línea de resultado. Se saltan si hay escenas sin guardar:
        // algunas guardan la escena abierta.
        private static void VerifyExistingSuites(ref int total, ref int passed)
        {
            if (HasDirtyScene(out string dirty))
            {
                Debug.LogWarning($"[EnvironmentAudioVerification] ⚠️ {dirty} tiene cambios sin guardar: no se corren las suites " +
                                 "existentes porque guardan la escena abierta. Guardá la escena y volvé a correr.");
                return;
            }

            // Estas tres suites anidan VehicleLightingVerification, que hoy da 31/31 pero espera 1 fallo preexistente (el cluster ya tiene
            // Indicator_PositionLights): esa expectativa vencida les suma fallos que son ajenos a US20 y a todo el audio. Se descuentan
            // explícitamente (Sound 1, NPC 2, Obstacle 3 = línea base medida antes de tocar el audio del entorno).
            string startPath = SceneManager.GetActiveScene().path;

            RunSuite("Tools/Primer Volante/Run Sound Integration Verification", "SoundIntegrationVerification", ref total, ref passed, 1);
            RunSuite("Tools/Primer Volante/Run Obstacle Collision Verification", "ObstacleCollisionVerification", ref total, ref passed, 3);
            RunSuite("Tools/Primer Volante/Run NPC Verification", "NPCVerification", ref total, ref passed, 2);
            RunSuite("Tools/Primer Volante/Run Traffic Light Tests", "TrafficLightTests", ref total, ref passed);
            RunSuite("Tools/Primer Volante/Run Scripted Event Tests", "ScriptedEventTests", ref total, ref passed);
            RunSuite("Tools/Primer Volante/Run Cockpit Integration Verification", "CockpitIntegrationVerification", ref total, ref passed);
            // LessonBoundaryVerification abre CityWithBarriers en modo aditivo: con otra escena abierta que ya tiene
            // Lesson_Boundaries en las mismas coordenadas (CityMerged3) las vallas se solapan y su chequeo de
            // "ningún collider ajeno" falla por construcción. Se corre solo en un contexto válido.
            if (HasDuplicateLessonBoundaries())
            {
                Debug.LogWarning("[EnvironmentAudioVerification] ⚠️ LessonBoundaryVerification no se corre desde esta escena: " +
                                 "abre CityWithBarriers en modo aditivo y las vallas de Lesson_Boundaries quedarían duplicadas. " +
                                 "Corré 'Run Lesson Boundary Verification' con CityWithBarriers o CityWithCar como escena abierta.");
            }
            else
            {
                RunSuite("Tools/Primer Volante/Run Lesson Boundary Verification", "LessonBoundaryVerification", ref total, ref passed);
            }

            // Lesson01Verification abre la escena de la lección 1 en modo Single: va última.
            RunSuite("Tools/Primer Volante/Lessons/Verify Lesson 1", "Lesson01Verification", ref total, ref passed);

            // Algunas suites cambian la escena activa: se restaura la que estaba abierta al empezar.
            if (!string.IsNullOrEmpty(startPath) && SceneManager.GetActiveScene().path != startPath && !HasDirtyScene(out _))
            {
                EditorSceneManager.OpenScene(startPath, OpenSceneMode.Single);
            }
        }

        private static void RunSuite(string menuPath, string name, ref int total, ref int passed, int knownStaleFailures = 0)
        {
            int suiteTotal = 0, suitePassed = 0;
            void OnLog(string message, string stack, LogType type)
            {
                // La última línea de resultado es la de la suite externa (las anidadas imprimen antes).
                Match match = Regex.Match(message, @"(\d+)/(\d+) (?:pruebas )?pasadas");
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

            Assert(menuFound && suiteTotal > 0 && suiteTotal - suitePassed <= knownStaleFailures,
                $"{name} sigue pasando ({suitePassed}/{suiteTotal}" + (knownStaleFailures > 0 ? $", {knownStaleFailures} fallo(s) de línea base ajenos" : string.Empty) + ")", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Helpers

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

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 1e-3f;

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
