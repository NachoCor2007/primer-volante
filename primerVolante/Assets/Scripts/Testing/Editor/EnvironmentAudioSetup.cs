using System;
using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Audio;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Setup idempotente del audio del entorno (US20 #12): placeholders procedurales, perfiles (ScriptableObjects),
    /// componentes de audio de los prefabs NPC y el GameObject <c>Audio_Environment</c> de la escena
    /// <c>CityMerged3</c>. Cada paso se puede correr varias veces sin duplicar nada.
    /// </summary>
    public static partial class EnvironmentAudioSetup
    {
        public const string MIXER_PATH = "Assets/Audio/Environment/EnvironmentAudioMixer.mixer";
        public const string PROFILES_FOLDER = "Assets/Audio/Environment/Profiles";
        public const string VEHICLE_AUDIO_FOLDER = "Assets/Audio/Vehicle";

        // Nombres de los assets de perfil.
        public const string ZONE_CITY = "Zone_City";
        public const string ZONE_PLAZA = "Zone_Plaza";
        public const string ZONE_TOWN = "Zone_Town";
        public const string EMITTER_FOOD = "Emitter_FoodAndDrink";
        public const string EMITTER_STADIUM = "Emitter_Stadium";
        public const string EMITTER_WORKSHOP = "Emitter_Workshop";
        public const string EMITTER_GAS = "Emitter_GasStation";
        public const string EMITTER_FACTORY = "Emitter_Factory";
        public const string EMITTER_WINDMILL = "Emitter_Windmill";
        public const string EMITTER_DOG = "Emitter_HouseDog";
        public const string EMITTER_PLAZA = "Emitter_PlazaLife";
        public const string NPC_VEHICLE_DEFAULT = "NPCVehicleAudio_Default";
        public const string NPC_VEHICLE_HEAVY = "NPCVehicleAudio_Heavy";
        public const string NPC_PEDESTRIAN_DEFAULT = "NPCPedestrianAudio_Default";
        public const string ALERTS_DEFAULT = "EnvironmentAlerts_Default";

        /// <summary>Nombres de todos los perfiles de zona.</summary>
        public static readonly string[] ZoneProfileNames = { ZONE_CITY, ZONE_PLAZA, ZONE_TOWN };

        /// <summary>Nombres de todos los perfiles de emisor.</summary>
        public static readonly string[] EmitterProfileNames =
        {
            EMITTER_FOOD, EMITTER_STADIUM, EMITTER_WORKSHOP, EMITTER_GAS,
            EMITTER_FACTORY, EMITTER_WINDMILL, EMITTER_DOG, EMITTER_PLAZA
        };

        /// <summary>Ruta de un perfil por nombre.</summary>
        public static string ProfilePath(string name) => $"{PROFILES_FOLDER}/{name}.asset";

        /// <summary>Carga un perfil por nombre (o <c>null</c> si no existe).</summary>
        public static T LoadProfile<T>(string name) where T : ScriptableObject
        {
            return AssetDatabase.LoadAssetAtPath<T>(ProfilePath(name));
        }

        // ------------------------------------------------------------------ menú: placeholders

        [MenuItem("Tools/Primer Volante/Environment Audio/Generate Environment Audio Placeholders")]
        public static void GeneratePlaceholdersFromMenu()
        {
            EnvironmentAudioPlaceholderGenerator.GeneratePlaceholders();
        }

        // ------------------------------------------------------------------ menú: perfiles

        [MenuItem("Tools/Primer Volante/Environment Audio/Create or Update Environment Audio Profiles")]
        public static void CreateOrUpdateProfilesFromMenu()
        {
            CreateOrUpdateProfiles();
        }

        /// <summary>
        /// Crea los 15 perfiles que falten con sus valores por defecto y asigna los clips placeholder que todavía no
        /// tengan clip. Los clips orgánicos quedan en <c>null</c>. No pisa clips ya asignados ni valores ajustados a mano.
        /// </summary>
        public static void CreateOrUpdateProfiles()
        {
            LessonBoundarySetup.EnsureFolder("Assets/Audio/Environment");
            LessonBoundarySetup.EnsureFolder(PROFILES_FOLDER);

            AudioClip Ph(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>(EnvironmentAudioPlaceholderGenerator.PathOf(file));
            AudioClip Veh(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{VEHICLE_AUDIO_FOLDER}/{file}");
            AudioClip[] Many(params string[] files) => files.Select(Ph).ToArray();

            // ---- Zonas
            var city = GetOrCreate<AmbienceZoneProfile>(ZONE_CITY, p =>
            {
                p.BedVolume = 0.5f;
                p.ScatterIsOrganic = false;
                p.ScatterMinInterval = 6f; p.ScatterMaxInterval = 14f;
                p.ScatterMinDistance = 40f; p.ScatterMaxDistance = 80f;
                p.ScatterMinVolume = 0.35f; p.ScatterMaxVolume = 0.7f;
                p.ScatterMinPitch = 0.9f; p.ScatterMaxPitch = 1.1f;
                p.ScatterMinRolloffDistance = 10f; p.ScatterMaxRolloffDistance = 150f;
            });
            Assign(city, p => { if (p.BedLoop == null) p.BedLoop = Ph("bed_city_traffic_loop.wav"); });
            Assign(city, p => { if (!p.HasScatter) p.ScatterClips = Many("city_car_pass_01.wav", "city_car_pass_02.wav", "city_car_pass_03.wav"); });

            var plaza = GetOrCreate<AmbienceZoneProfile>(ZONE_PLAZA, p =>
            {
                // Dispersos orgánicos (pájaros, palomas, chicos a lo lejos): vacíos hasta descargar los clips.
                p.BedVolume = 0.35f;
                p.ScatterIsOrganic = true;
                p.ScatterMinInterval = 6f; p.ScatterMaxInterval = 16f;
                p.ScatterMinDistance = 8f; p.ScatterMaxDistance = 25f;
                p.ScatterMinVolume = 0.4f; p.ScatterMaxVolume = 0.8f;
                p.ScatterMinRolloffDistance = 4f; p.ScatterMaxRolloffDistance = 60f;
            });
            Assign(plaza, p => { if (p.BedLoop == null) p.BedLoop = Ph("bed_plaza_air_loop.wav"); });

            var town = GetOrCreate<AmbienceZoneProfile>(ZONE_TOWN, p =>
            {
                // Dispersos orgánicos (pájaros, gallo): vacíos hasta descargar los clips.
                p.BedVolume = 0.5f;
                p.ScatterIsOrganic = true;
                p.ScatterMinInterval = 8f; p.ScatterMaxInterval = 22f;
                p.ScatterMinDistance = 10f; p.ScatterMaxDistance = 30f;
                p.ScatterMinVolume = 0.4f; p.ScatterMaxVolume = 0.8f;
                p.ScatterMinRolloffDistance = 5f; p.ScatterMaxRolloffDistance = 80f;
            });
            Assign(town, p => { if (p.BedLoop == null) p.BedLoop = Ph("bed_town_wind_loop.wav"); });

            // ---- Emisores
            GetOrCreate<AmbientEmitterProfile>(EMITTER_FOOD, p =>
            {
                p.Mode = AmbientEmitterMode.Loop; p.ClipsAreOrganic = true;
                p.MinVolume = 0.5f; p.MaxVolume = 0.7f;
                p.MinDistance = 2f; p.MaxDistance = 25f;
            });

            GetOrCreate<AmbientEmitterProfile>(EMITTER_STADIUM, p =>
            {
                p.Mode = AmbientEmitterMode.Loop; p.ClipsAreOrganic = true;
                p.MinVolume = 0.7f; p.MaxVolume = 0.9f;
                p.MinDistance = 10f; p.MaxDistance = 120f;
            });

            var workshop = GetOrCreate<AmbientEmitterProfile>(EMITTER_WORKSHOP, p =>
            {
                p.Mode = AmbientEmitterMode.RandomOneShots; p.ClipsAreOrganic = false;
                p.MinInterval = 4f; p.MaxInterval = 10f;
                p.MinVolume = 0.6f; p.MaxVolume = 0.9f;
                p.MinPitch = 0.9f; p.MaxPitch = 1.1f;
                p.MinDistance = 3f; p.MaxDistance = 40f;
            });
            Assign(workshop, p => { if (!p.HasOneShots) p.OneShots = Many("workshop_clank_01.wav", "workshop_clank_02.wav"); });

            GetOrCreate<AmbientEmitterProfile>(EMITTER_GAS, p =>
            {
                p.Mode = AmbientEmitterMode.RandomOneShots; p.ClipsAreOrganic = true;
                p.MinInterval = 10f; p.MaxInterval = 25f;
                p.MinVolume = 0.5f; p.MaxVolume = 0.8f;
                p.MinDistance = 3f; p.MaxDistance = 30f;
            });

            var factory = GetOrCreate<AmbientEmitterProfile>(EMITTER_FACTORY, p =>
            {
                p.Mode = AmbientEmitterMode.Loop; p.ClipsAreOrganic = false;
                p.MinVolume = 0.6f; p.MaxVolume = 0.8f;
                p.MinDistance = 6f; p.MaxDistance = 60f;
            });
            Assign(factory, p => { if (p.LoopClip == null) p.LoopClip = Ph("factory_hum_loop.wav"); });

            var windmill = GetOrCreate<AmbientEmitterProfile>(EMITTER_WINDMILL, p =>
            {
                p.Mode = AmbientEmitterMode.RandomOneShots; p.ClipsAreOrganic = false;
                p.MinInterval = 8f; p.MaxInterval = 20f;
                p.MinVolume = 0.4f; p.MaxVolume = 0.7f;
                p.MinPitch = 0.85f; p.MaxPitch = 1.15f;
                p.MinDistance = 3f; p.MaxDistance = 45f;
            });
            Assign(windmill, p => { if (!p.HasOneShots) p.OneShots = Many("windmill_creak_01.wav", "windmill_creak_02.wav"); });

            GetOrCreate<AmbientEmitterProfile>(EMITTER_DOG, p =>
            {
                p.Mode = AmbientEmitterMode.RandomOneShots; p.ClipsAreOrganic = true;
                p.MinInterval = 6f; p.MaxInterval = 15f;
                p.MinVolume = 0.7f; p.MaxVolume = 1f;
                p.MinDistance = 3f; p.MaxDistance = 40f;
                p.ApproachRadius = 14f; p.ApproachProbability = 0.5f; p.ApproachCooldown = 40f;
                p.BurstMin = 1; p.BurstMax = 3;
            });

            GetOrCreate<AmbientEmitterProfile>(EMITTER_PLAZA, p =>
            {
                p.Mode = AmbientEmitterMode.RandomOneShots; p.ClipsAreOrganic = true;
                p.MinInterval = 5f; p.MaxInterval = 14f;
                p.MinVolume = 0.4f; p.MaxVolume = 0.8f;
                p.MinDistance = 3f; p.MaxDistance = 35f;
            });

            // ---- NPC y alertas
            var vehDefault = GetOrCreate<NPCVehicleAudioProfile>(NPC_VEHICLE_DEFAULT, p =>
            {
                p.ReferenceSpeed = 14f; p.MinPitch = 0.9f; p.MaxPitch = 1.5f; p.EngineVolume = 0.55f;
                p.EngineMinDistance = 3f; p.EngineMaxDistance = 50f;
            });
            var vehHeavy = GetOrCreate<NPCVehicleAudioProfile>(NPC_VEHICLE_HEAVY, p =>
            {
                p.ReferenceSpeed = 11f; p.MinPitch = 0.6f; p.MaxPitch = 1.0f; p.EngineVolume = 0.65f;
                p.EngineMinDistance = 4f; p.EngineMaxDistance = 60f;
                p.AlertMaxDistance = 90f;
            });
            foreach (NPCVehicleAudioProfile p in new[] { vehDefault, vehHeavy })
            {
                Assign(p, v =>
                {
                    if (v.EngineIdleLoop == null) v.EngineIdleLoop = Veh("engine_idle_loop.wav");
                    if (v.EngineMidLoop == null) v.EngineMidLoop = Veh("engine_mid_loop.wav");
                    if (v.SoftBrake == null) v.SoftBrake = Ph("brake_squeal_soft.wav");
                    if (v.TireScreech == null) v.TireScreech = Ph("tire_screech.wav");
                    if (v.HornShort == null) v.HornShort = Ph("horn_short.wav");
                    if (v.HornLong == null) v.HornLong = Ph("horn_long.wav");
                    if (v.SirenLoop == null) v.SirenLoop = Ph("siren_wail_loop.wav");
                });
            }

            var ped = GetOrCreate<NPCPedestrianAudioProfile>(NPC_PEDESTRIAN_DEFAULT, p => { });
            Assign(ped, p =>
            {
                if (!p.HasFootsteps) p.Footsteps = Many("footstep_01.wav", "footstep_02.wav", "footstep_03.wav", "footstep_04.wav");
            });

            var alerts = GetOrCreate<EnvironmentAlertsProfile>(ALERTS_DEFAULT, p => { });
            Assign(alerts, p =>
            {
                if (p.CarAlarmLoop == null) p.CarAlarmLoop = Ph("car_alarm_loop.wav");
                if (p.HornShort == null) p.HornShort = Ph("horn_short.wav");
                if (p.HornLong == null) p.HornLong = Ph("horn_long.wav");
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int missing = 0, pending = 0;
            foreach (EnvironmentAudioProfileBase profile in AllProfiles())
            {
                missing += profile.GetMissingPlaceholders().Length;
                pending += profile.GetPendingOrganic().Length;
            }

            Debug.Log($"[EnvironmentAudioSetup] ✅ {AllProfiles().Count()} perfiles en {PROFILES_FOLDER}: " +
                      $"{missing} placeholders faltantes, {pending} grupos de clips orgánicos pendientes (ver SOUNDS_TODO.md).");
        }

        /// <summary>Los 15 perfiles que existen en la carpeta de perfiles.</summary>
        public static IEnumerable<EnvironmentAudioProfileBase> AllProfiles()
        {
            foreach (string name in ZoneProfileNames.Concat(EmitterProfileNames))
            {
                var profile = LoadProfile<EnvironmentAudioProfileBase>(name);
                if (profile != null) yield return profile;
            }

            foreach (string name in new[] { NPC_VEHICLE_DEFAULT, NPC_VEHICLE_HEAVY, NPC_PEDESTRIAN_DEFAULT, ALERTS_DEFAULT })
            {
                var profile = LoadProfile<EnvironmentAudioProfileBase>(name);
                if (profile != null) yield return profile;
            }
        }

        // Crea el asset si falta y le aplica los valores por defecto solo en ese caso (no pisa ajustes manuales).
        private static T GetOrCreate<T>(string name, Action<T> initialize) where T : ScriptableObject
        {
            string path = ProfilePath(name);
            var profile = AssetDatabase.LoadAssetAtPath<T>(path);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<T>();
            initialize?.Invoke(profile);
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void Assign<T>(T profile, Action<T> assignment) where T : ScriptableObject
        {
            if (profile == null) return;
            assignment(profile);
            EditorUtility.SetDirty(profile);
        }
    }
}
