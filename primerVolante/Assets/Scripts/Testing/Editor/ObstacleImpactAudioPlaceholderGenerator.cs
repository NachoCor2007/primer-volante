using System.Collections.Generic;
using System.IO;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Genera los sonidos de impacto placeholder de US33 (WAV 16-bit PCM mono 44.1 kHz, deterministas) en
    /// <c>Assets/Audio/Obstacles/</c>, configura su importación como one-shots cortos y crea o actualiza el
    /// <see cref="ObstacleImpactAudioProfile"/>. NUNCA sobrescribe un archivo existente: para usar clips reales
    /// basta reemplazar el archivo con el mismo nombre; el perfil sigue apuntando a él sin tocar código.
    ///
    /// | Archivo              | Duración | Carácter                                              |
    /// |----------------------|----------|-------------------------------------------------------|
    /// | cone_graze.wav       | ~0.30 s  | raspón suave de plástico, bajo                        |
    /// | cone_crash.wav       | ~0.35 s  | golpe hueco de plástico, agudo y corto                |
    /// | vehicle_graze.wav    | ~0.50 s  | raspón metálico suave con algo de resonancia          |
    /// | vehicle_crash.wav    | ~0.80 s  | golpe metálico con resonancia                         |
    /// | pedestrian_crash.wav | ~0.40 s  | golpe sordo y grave                                   |
    /// </summary>
    public static class ObstacleImpactAudioPlaceholderGenerator
    {
        public const string AUDIO_FOLDER = "Assets/Audio/Obstacles";
        public const string PROFILE_PATH = AUDIO_FOLDER + "/ObstacleImpactAudioProfile.asset";

        private const int SAMPLE_RATE = VehicleAudioPlaceholderGenerator.SAMPLE_RATE;

        // Picos tras normalizar: los raspones quedan más bajos que los golpes.
        private const float k_HitPeak = 0.85f;
        private const float k_GrazePeak = 0.5f;

        private static readonly string[] s_Files =
        {
            "cone_graze.wav", "cone_crash.wav", "vehicle_graze.wav", "vehicle_crash.wav", "pedestrian_crash.wav"
        };

        [MenuItem("Tools/Primer Volante/Generate Obstacle Impact Audio Placeholders")]
        public static void GenerateFromMenu()
        {
            GeneratePlaceholders();
        }

        /// <summary>
        /// Genera los WAV faltantes, aplica la importación y crea o actualiza el perfil con los clips.
        /// </summary>
        public static ObstacleImpactAudioProfile GeneratePlaceholders()
        {
            Directory.CreateDirectory(AUDIO_FOLDER);

            var created = new List<string>();
            void Generate(string fileName, float[] samples, float peak)
            {
                if (Write(fileName, samples, peak) == 1) created.Add(fileName);
            }

            Generate("cone_graze.wav", GenConeGraze(), k_GrazePeak);
            Generate("cone_crash.wav", GenConeCrash(), k_HitPeak);
            Generate("vehicle_graze.wav", GenVehicleGraze(), k_GrazePeak);
            Generate("vehicle_crash.wav", GenVehicleCrash(), k_HitPeak);
            Generate("pedestrian_crash.wav", GenPedestrianCrash(), k_HitPeak);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Solo se configuran los clips recién generados: un clip real que reemplazó al placeholder conserva
            // la importación que se le haya dado.
            ApplyImportSettings(created);

            ObstacleImpactAudioProfile profile = CreateOrUpdateProfile();
            Debug.Log($"[ObstacleImpactAudioPlaceholderGenerator] 🔊 Placeholders generados: {created.Count} nuevos " +
                      "(los existentes no se tocan). Perfil en " + PROFILE_PATH);
            return profile;
        }

        /// <summary>
        /// Configura la importación de los clips: mono forzado, descomprimidos al cargar, ADPCM (one-shots cortos).
        /// Sin argumento configura los cinco; con una lista, solo esos archivos.
        /// </summary>
        public static void ApplyImportSettings(ICollection<string> onlyFiles = null)
        {
            foreach (string file in s_Files)
            {
                if (onlyFiles != null && !onlyFiles.Contains(file)) continue;

                var importer = AssetImporter.GetAtPath($"{AUDIO_FOLDER}/{file}") as AudioImporter;
                if (importer == null) continue;

                importer.forceToMono = true;
                importer.ambisonic = false;
                importer.loadInBackground = false;

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = true;
                settings.compressionFormat = AudioCompressionFormat.ADPCM;
                settings.quality = 1f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;

                // Override Android (Meta Quest): mismo criterio, explícito por plataforma.
                importer.SetOverrideSampleSettings("Android", settings);
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// Crea el perfil si falta y asigna cada clip que todavía no tenga uno. No pisa clips ya asignados.
        /// </summary>
        public static ObstacleImpactAudioProfile CreateOrUpdateProfile()
        {
            LessonBoundarySetup.EnsureFolder(AUDIO_FOLDER);

            var profile = AssetDatabase.LoadAssetAtPath<ObstacleImpactAudioProfile>(PROFILE_PATH);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ObstacleImpactAudioProfile>();
                AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            }

            var so = new SerializedObject(profile);
            AssignIfEmpty(so, "ConeGraze", "cone_graze.wav");
            AssignIfEmpty(so, "ConeCrash", "cone_crash.wav");
            AssignIfEmpty(so, "VehicleGraze", "vehicle_graze.wav");
            AssignIfEmpty(so, "VehicleCrash", "vehicle_crash.wav");
            AssignIfEmpty(so, "PedestrianCrash", "pedestrian_crash.wav");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void AssignIfEmpty(SerializedObject so, string field, string fileName)
        {
            SerializedProperty clip = so.FindProperty(field + ".Clip");
            if (clip == null || clip.objectReferenceValue != null) return;

            clip.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AUDIO_FOLDER}/{fileName}");
        }

        // ---------------------------------------------------------------- escritura

        private static int Write(string fileName, float[] samples, float peak)
        {
            string path = $"{AUDIO_FOLDER}/{fileName}";
            if (File.Exists(path)) return 0;

            VehicleAudioPlaceholderGenerator.Normalize(samples, peak);
            VehicleAudioPlaceholderGenerator.WriteWav16(path, samples);
            return 1;
        }

        // ---------------------------------------------------------------- síntesis

        private static float[] Buffer(float seconds) => new float[Mathf.RoundToInt(seconds * SAMPLE_RATE)];

        private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        private static float Sine(float t, float freq, float decay) =>
            Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t / decay);

        /// <summary>Ruido blanco con la semilla dada, filtrado pasa-bajos.</summary>
        private static float[] FilteredNoise(int length, int seed, float cutoffHz)
        {
            var r = new System.Random(seed);
            var n = new float[length];
            for (int i = 0; i < n.Length; i++) n[i] = Noise(r);
            VehicleAudioPlaceholderGenerator.LowPass(n, cutoffHz);
            return n;
        }

        /// <summary>Cono, choque: golpe hueco de plástico (dos modos agudos de decaimiento rápido).</summary>
        private static float[] GenConeCrash()
        {
            var b = Buffer(0.35f);
            float[] air = FilteredNoise(b.Length, 101, 3500f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float body = Sine(t, 640f, 0.055f) + Sine(t, 1180f, 0.035f) * 0.6f + Sine(t, 1750f, 0.02f) * 0.3f;
                b[i] = body + air[i] * Mathf.Exp(-t / 0.012f) * 1.6f;
            }

            VehicleAudioPlaceholderGenerator.AddClick(b, 0f, 2400f, 0.004f, 0.5f, new System.Random(102));
            return b;
        }

        /// <summary>Cono, roce: raspón suave de plástico, más bajo y sin ataque duro.</summary>
        private static float[] GenConeGraze()
        {
            var b = Buffer(0.3f);
            float[] scrape = FilteredNoise(b.Length, 111, 2200f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float attack = Mathf.Clamp01(t / 0.03f);
                float env = attack * Mathf.Exp(-t / 0.12f);
                float flutter = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 38f * t);
                b[i] = scrape[i] * env * flutter + Sine(t, 420f, 0.04f) * 0.25f;
            }

            return b;
        }

        /// <summary>Vehículo, choque: golpe grave de chapa con resonancia metálica que se apaga despacio.</summary>
        private static float[] GenVehicleCrash()
        {
            var b = Buffer(0.8f);
            float[] burst = FilteredNoise(b.Length, 121, 4000f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float thud = Sine(t, 78f, 0.09f) * 1.1f + Sine(t, 150f, 0.06f) * 0.5f;
                float ring = Sine(t, 410f, 0.28f) * 0.5f + Sine(t, 830f, 0.22f) * 0.4f +
                             Sine(t, 1370f, 0.16f) * 0.3f + Sine(t, 2210f, 0.10f) * 0.2f;
                b[i] = thud + ring + burst[i] * Mathf.Exp(-t / 0.02f) * 1.2f;
            }

            return b;
        }

        /// <summary>Vehículo, roce: raspón metálico con una resonancia suave.</summary>
        private static float[] GenVehicleGraze()
        {
            var b = Buffer(0.5f);
            float[] scrape = FilteredNoise(b.Length, 131, 3200f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float attack = Mathf.Clamp01(t / 0.04f);
                float env = attack * Mathf.Exp(-t / 0.2f);
                float flutter = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 27f * t);
                float ring = Sine(t, 620f, 0.15f) * 0.18f + Sine(t, 1240f, 0.1f) * 0.1f;
                b[i] = scrape[i] * env * flutter + ring;
            }

            return b;
        }

        /// <summary>Peatón, choque: golpe sordo y grave, casi sin agudos.</summary>
        private static float[] GenPedestrianCrash()
        {
            var b = Buffer(0.4f);
            float[] dull = FilteredNoise(b.Length, 141, 420f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float thud = Sine(t, 58f, 0.11f) + Sine(t, 112f, 0.07f) * 0.6f;
                b[i] = thud + dull[i] * Mathf.Exp(-t / 0.05f) * 2.2f;
            }

            return b;
        }
    }
}
