using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Tipo de importación de un clip del entorno (US20).
    /// </summary>
    public enum EnvironmentClipKind
    {
        /// <summary>Lecho 2D largo: Vorbis, Streaming, sin forzar mono.</summary>
        BedStreaming = 0,

        /// <summary>Loop 3D de duración media (~10 s): Vorbis, CompressedInMemory, mono.</summary>
        LoopMedium = 1,

        /// <summary>Loop 3D corto (2–4 s): Vorbis, DecompressOnLoad, mono.</summary>
        LoopShort = 2,

        /// <summary>One-shot corto 3D: ADPCM, DecompressOnLoad, mono.</summary>
        OneShot = 3
    }

    /// <summary>
    /// Genera los clips placeholder procedurales del audio del entorno (US20): WAV 16-bit PCM mono 44.1 kHz,
    /// deterministas (semilla fija), en <c>Assets/Audio/Environment/Placeholders/</c>. Solo los sonidos mecánicos o
    /// abstractos; los orgánicos (voces, pájaros, perros, multitudes) quedan como espacios vacíos en los perfiles
    /// (ver <c>Assets/Audio/Environment/SOUNDS_TODO.md</c>). NUNCA sobrescribe un archivo existente: para usar un clip
    /// real basta reemplazar el archivo con el mismo nombre; los perfiles siguen apuntando a él sin tocar código.
    ///
    /// | Archivo                      | Carácter                                               | Loop |
    /// |------------------------------|--------------------------------------------------------|------|
    /// | bed_city_traffic_loop.wav    | ruido marrón ~500 Hz con ondulaciones lentas; 30 s     | sí   |
    /// | bed_town_wind_loop.wav       | viento pasabanda con ráfagas lentas; 30 s              | sí   |
    /// | bed_plaza_air_loop.wav       | aire suave y agudo, muy bajo; 20 s                     | sí   |
    /// | city_car_pass_01..03.wav     | "whoosh" con barrido de filtro; 2–4 s                  | no   |
    /// | horn_short.wav, horn_long.wav| dos tonos cuadrados (~400 + 500 Hz); 0.3 s y 1.2 s     | no   |
    /// | siren_wail_loop.wav          | barrido 650 ↔ 1350 Hz, período 4 s                     | sí   |
    /// | brake_squeal_soft.wav        | chillido agudo leve con vibrato; 0.8 s                 | no   |
    /// | tire_screech.wav             | chirrido fuerte con ruido; 1.2 s                       | no   |
    /// | crosswalk_tick/beep.wav      | click seco y beep de ~2.5 kHz                          | no   |
    /// | car_alarm_loop.wav           | alternancia de dos tonos; 2 s                          | sí   |
    /// | footstep_01..04.wav          | golpes sordos cortos de ruido filtrado                 | no   |
    /// | windmill_creak_01..02.wav    | crujido de fricción grave modulado; 1–2 s              | no   |
    /// | factory_hum_loop.wav         | zumbido grave con armónicos y un clank periódico; 10 s | sí   |
    /// | workshop_clank_01..02.wav    | golpes metálicos cortos                                | no   |
    /// </summary>
    public static class EnvironmentAudioPlaceholderGenerator
    {
        public const string AUDIO_FOLDER = "Assets/Audio/Environment/Placeholders";

        private const int SAMPLE_RATE = VehicleAudioPlaceholderGenerator.SAMPLE_RATE;

        /// <summary>Todos los placeholders con su tipo de importación.</summary>
        public static readonly (string file, EnvironmentClipKind kind)[] Files =
        {
            ("bed_city_traffic_loop.wav", EnvironmentClipKind.BedStreaming),
            ("bed_town_wind_loop.wav", EnvironmentClipKind.BedStreaming),
            ("bed_plaza_air_loop.wav", EnvironmentClipKind.BedStreaming),
            ("city_car_pass_01.wav", EnvironmentClipKind.OneShot),
            ("city_car_pass_02.wav", EnvironmentClipKind.OneShot),
            ("city_car_pass_03.wav", EnvironmentClipKind.OneShot),
            ("horn_short.wav", EnvironmentClipKind.OneShot),
            ("horn_long.wav", EnvironmentClipKind.OneShot),
            ("siren_wail_loop.wav", EnvironmentClipKind.LoopShort),
            ("brake_squeal_soft.wav", EnvironmentClipKind.OneShot),
            ("tire_screech.wav", EnvironmentClipKind.OneShot),
            ("crosswalk_tick.wav", EnvironmentClipKind.OneShot),
            ("crosswalk_beep.wav", EnvironmentClipKind.OneShot),
            ("car_alarm_loop.wav", EnvironmentClipKind.LoopShort),
            ("footstep_01.wav", EnvironmentClipKind.OneShot),
            ("footstep_02.wav", EnvironmentClipKind.OneShot),
            ("footstep_03.wav", EnvironmentClipKind.OneShot),
            ("footstep_04.wav", EnvironmentClipKind.OneShot),
            ("windmill_creak_01.wav", EnvironmentClipKind.OneShot),
            ("windmill_creak_02.wav", EnvironmentClipKind.OneShot),
            ("factory_hum_loop.wav", EnvironmentClipKind.LoopMedium),
            ("workshop_clank_01.wav", EnvironmentClipKind.OneShot),
            ("workshop_clank_02.wav", EnvironmentClipKind.OneShot),
        };

        /// <summary>Ruta de assets de un placeholder.</summary>
        public static string PathOf(string fileName) => $"{AUDIO_FOLDER}/{fileName}";

        /// <summary>Tipo de importación de un archivo, o <c>null</c> si no es un placeholder de esta lista.</summary>
        public static EnvironmentClipKind? KindOf(string fileName)
        {
            foreach (var entry in Files)
            {
                if (entry.file == fileName) return entry.kind;
            }

            return null;
        }

        /// <summary>
        /// Genera los WAV faltantes y configura la importación de los recién creados. Devuelve cuántos creó.
        /// </summary>
        public static int GeneratePlaceholders()
        {
            Directory.CreateDirectory(AUDIO_FOLDER);

            var created = new List<string>();
            void Gen(string file, float[] samples, float peak)
            {
                if (Write(file, samples, peak)) created.Add(file);
            }

            Gen("bed_city_traffic_loop.wav", GenCityTrafficBed(), 0.7f);
            Gen("bed_town_wind_loop.wav", GenTownWindBed(), 0.7f);
            Gen("bed_plaza_air_loop.wav", GenPlazaAirBed(), 0.45f);
            Gen("city_car_pass_01.wav", GenCarPass(2.4f, 301, 150f, 95f), 0.8f);
            Gen("city_car_pass_02.wav", GenCarPass(3.2f, 302, 120f, 80f), 0.8f);
            Gen("city_car_pass_03.wav", GenCarPass(4.0f, 303, 105f, 70f), 0.8f);
            Gen("horn_short.wav", GenHorn(0.3f), 0.8f);
            Gen("horn_long.wav", GenHorn(1.2f), 0.8f);
            Gen("siren_wail_loop.wav", GenSiren(), 0.75f);
            Gen("brake_squeal_soft.wav", GenBrakeSqueal(), 0.6f);
            Gen("tire_screech.wav", GenTireScreech(), 0.85f);
            Gen("crosswalk_tick.wav", GenCrosswalkTick(), 0.8f);
            Gen("crosswalk_beep.wav", GenCrosswalkBeep(), 0.7f);
            Gen("car_alarm_loop.wav", GenCarAlarm(), 0.75f);
            Gen("footstep_01.wav", GenFootstep(401, 350f, 90f), 0.8f);
            Gen("footstep_02.wav", GenFootstep(402, 450f, 100f), 0.8f);
            Gen("footstep_03.wav", GenFootstep(403, 300f, 85f), 0.8f);
            Gen("footstep_04.wav", GenFootstep(404, 400f, 110f), 0.8f);
            Gen("windmill_creak_01.wav", GenCreak(1.0f, 501, 150f), 0.7f);
            Gen("windmill_creak_02.wav", GenCreak(1.8f, 502, 120f), 0.7f);
            Gen("factory_hum_loop.wav", GenFactoryHum(), 0.7f);
            Gen("workshop_clank_01.wav", GenClank(601, 1f, false), 0.85f);
            Gen("workshop_clank_02.wav", GenClank(602, 0.8f, true), 0.85f);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Solo se configuran los clips recién generados: un clip real que reemplazó al placeholder conserva
            // la importación que se le haya dado.
            ApplyImportSettings(created);

            Debug.Log($"[EnvironmentAudioPlaceholderGenerator] 🔊 Placeholders generados: {created.Count} nuevos " +
                      $"(los existentes no se tocan). Carpeta: {AUDIO_FOLDER}");
            return created.Count;
        }

        /// <summary>
        /// Aplica la configuración de importación (mono, Load Type, compresión) a los placeholders. Sin argumento
        /// configura todos; con una lista, solo esos archivos.
        /// </summary>
        public static void ApplyImportSettings(ICollection<string> onlyFiles = null)
        {
            foreach (var entry in Files)
            {
                if (onlyFiles != null && !onlyFiles.Contains(entry.file)) continue;

                var importer = AssetImporter.GetAtPath(PathOf(entry.file)) as AudioImporter;
                if (importer == null) continue;

                bool bed = entry.kind == EnvironmentClipKind.BedStreaming;
                importer.forceToMono = !bed;
                importer.ambisonic = false;
                importer.loadInBackground = bed;

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                switch (entry.kind)
                {
                    case EnvironmentClipKind.BedStreaming:
                        settings.loadType = AudioClipLoadType.Streaming;
                        settings.compressionFormat = AudioCompressionFormat.Vorbis;
                        settings.quality = 0.5f;
                        break;
                    case EnvironmentClipKind.LoopMedium:
                        settings.loadType = AudioClipLoadType.CompressedInMemory;
                        settings.compressionFormat = AudioCompressionFormat.Vorbis;
                        settings.quality = 0.6f;
                        break;
                    case EnvironmentClipKind.LoopShort:
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.compressionFormat = AudioCompressionFormat.Vorbis;
                        settings.quality = 0.7f;
                        break;
                    default:
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.compressionFormat = AudioCompressionFormat.ADPCM;
                        settings.quality = 1f;
                        break;
                }

                importer.defaultSampleSettings = settings;

                // Override Android (Meta Quest): mismo criterio, explícito por plataforma.
                importer.SetOverrideSampleSettings("Android", settings);
                importer.SaveAndReimport();
            }
        }

        // ---------------------------------------------------------------- escritura

        private static bool Write(string fileName, float[] samples, float peak)
        {
            string path = PathOf(fileName);
            if (File.Exists(path)) return false;

            VehicleAudioPlaceholderGenerator.Normalize(samples, peak);
            VehicleAudioPlaceholderGenerator.WriteWav16(path, samples);
            return true;
        }

        // ---------------------------------------------------------------- síntesis base

        private static float[] Buf(float seconds) => new float[Mathf.RoundToInt(seconds * SAMPLE_RATE)];

        private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        private static float[] NoiseBuf(int length, System.Random r)
        {
            var b = new float[length];
            for (int i = 0; i < length; i++) b[i] = Noise(r);
            return b;
        }

        /// <summary>Ruido marrón con fuga (evita deriva), extendido para el crossfade del loop.</summary>
        private static float[] BrownNoise(int length, System.Random r, float leak = 0.995f, float step = 0.08f)
        {
            var b = new float[length];
            float acc = 0f;
            for (int i = 0; i < length; i++)
            {
                acc = acc * leak + Noise(r) * step;
                b[i] = acc;
            }

            return b;
        }

        /// <summary>Copia pasa-bajos de un polo.</summary>
        private static float[] LowPassCopy(float[] s, float cutoffHz)
        {
            var c = (float[])s.Clone();
            VehicleAudioPlaceholderGenerator.LowPass(c, cutoffHz);
            return c;
        }

        /// <summary>Pasa-banda aproximado: pasa-bajos alto menos pasa-bajos bajo.</summary>
        private static float[] BandPass(float[] s, float lowHz, float highHz)
        {
            float[] hi = LowPassCopy(s, highHz);
            float[] lo = LowPassCopy(s, lowHz);
            for (int i = 0; i < hi.Length; i++) hi[i] -= lo[i];
            return hi;
        }

        /// <summary>Pasa-altos aproximado: señal menos su pasa-bajos.</summary>
        private static float[] HighPass(float[] s, float cutoffHz)
        {
            float[] lo = LowPassCopy(s, cutoffHz);
            var o = new float[s.Length];
            for (int i = 0; i < o.Length; i++) o[i] = s[i] - lo[i];
            return o;
        }

        private static float TwoPi => 2f * Mathf.PI;

        // ---------------------------------------------------------------- lechos

        // Los lechos usan modulaciones con una cantidad ENTERA de ciclos sobre la duración del loop y un crossfade del
        // ruido de base, de modo que el final continúa en el inicio sin clic.

        private static float[] GenCityTrafficBed()
        {
            const float seconds = 30f;
            int n = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            int x = Mathf.RoundToInt(0.5f * SAMPLE_RATE);
            var r = new System.Random(2001);

            float[] brown = LowPassCopy(BrownNoise(n + x, r), 500f);
            float[] hiss = LowPassCopy(NoiseBuf(n + x, r), 1800f);
            for (int i = 0; i < brown.Length; i++) brown[i] = brown[i] * 2.2f + hiss[i] * 0.12f;

            float[] loop = VehicleAudioPlaceholderGenerator.MakeSeamlessLoop(brown, n, x);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SAMPLE_RATE / seconds; // 0..1 sobre el loop
                // Autos lejanos que se acercan y se alejan: ondulaciones lentas.
                float m = 1f + 0.35f * Mathf.Sin(TwoPi * 3f * t + 0.7f)
                             + 0.20f * Mathf.Sin(TwoPi * 7f * t + 2.1f)
                             + 0.10f * Mathf.Sin(TwoPi * 13f * t + 4.0f);
                loop[i] *= m;
            }

            return loop;
        }

        private static float[] GenTownWindBed()
        {
            const float seconds = 30f;
            int n = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            int x = Mathf.RoundToInt(0.5f * SAMPLE_RATE);
            var r = new System.Random(2002);

            float[] wind = BandPass(NoiseBuf(n + x, r), 120f, 900f);
            float[] whistle = BandPass(NoiseBuf(n + x, r), 500f, 1100f);
            for (int i = 0; i < wind.Length; i++) wind[i] = wind[i] * 2.0f + whistle[i] * 0.8f;

            float[] loop = VehicleAudioPlaceholderGenerator.MakeSeamlessLoop(wind, n, x);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SAMPLE_RATE / seconds;
                // Ráfagas lentas: el viento nunca se calla del todo.
                float gust = 0.5f + 0.5f * Mathf.Sin(TwoPi * 2f * t + 1.3f);
                float gust2 = 0.5f + 0.5f * Mathf.Sin(TwoPi * 5f * t + 0.2f);
                loop[i] *= 0.3f + 0.7f * gust * gust + 0.25f * gust2;
            }

            return loop;
        }

        private static float[] GenPlazaAirBed()
        {
            const float seconds = 20f;
            int n = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            int x = Mathf.RoundToInt(0.5f * SAMPLE_RATE);
            var r = new System.Random(2003);

            float[] air = HighPass(NoiseBuf(n + x, r), 2000f);
            air = LowPassCopy(air, 7000f);

            float[] loop = VehicleAudioPlaceholderGenerator.MakeSeamlessLoop(air, n, x);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SAMPLE_RATE / seconds;
                loop[i] *= 0.8f + 0.2f * Mathf.Sin(TwoPi * 2f * t) + 0.1f * Mathf.Sin(TwoPi * 5f * t + 1f);
            }

            return loop;
        }

        // ---------------------------------------------------------------- vehículos

        private static float[] GenCarPass(float seconds, int seed, float startHz, float endHz)
        {
            var b = Buf(seconds);
            var r = new System.Random(seed);
            float[] noise = NoiseBuf(b.Length, r);

            float y = 0f;
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float u = i / (float)b.Length;
                float env = Mathf.Sin(Mathf.PI * u);
                env *= env; // sube y baja

                // Pasa-bajos con corte variable: más brillante en el punto más cercano.
                float cutoff = 250f + 1500f * Mathf.Pow(env, 1.5f);
                float a = 1f - Mathf.Exp(-TwoPi * cutoff / SAMPLE_RATE);
                y += a * (noise[i] - y);

                // Zumbido del motor con caída de frecuencia (efecto Doppler aproximado).
                float hz = Mathf.Lerp(startHz, endHz, u);
                phase += TwoPi * hz / SAMPLE_RATE;
                float engine = Mathf.Sin((float)phase) + 0.5f * Mathf.Sin((float)(phase * 2.0)) + 0.25f * Mathf.Sin((float)(phase * 3.0));

                b[i] = (y * 3.0f + engine * 0.25f) * env;
            }

            return b;
        }

        private static float[] GenHorn(float seconds)
        {
            var b = Buf(seconds);
            const float attack = 0.012f;
            const float release = 0.05f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float sq = Mathf.Sign(Mathf.Sin(TwoPi * 400f * t)) + Mathf.Sign(Mathf.Sin(TwoPi * 500f * t));
                float env = Mathf.Clamp01(t / attack) * Mathf.Clamp01((seconds - t) / release);
                b[i] = sq * 0.5f * env;
            }

            VehicleAudioPlaceholderGenerator.LowPass(b, 2500f);
            return b;
        }

        private static float[] GenSiren()
        {
            const float seconds = 4f; // período de la sirena
            var b = Buf(seconds);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                // 650 ↔ 1350 Hz: centro 1000 Hz, desvío 350 Hz. La integral de la parte senoidal sobre un período es 0
                // y el centro da una cantidad entera de ciclos: la fase cierra y el loop es continuo.
                float hz = 1000f + 350f * Mathf.Sin(TwoPi * t / seconds - Mathf.PI * 0.5f);
                phase += TwoPi * hz / SAMPLE_RATE;
                float p = (float)phase;
                b[i] = Mathf.Sin(p) + 0.35f * Mathf.Sin(2f * p) + 0.15f * Mathf.Sin(3f * p);
            }

            return b;
        }

        private static float[] GenBrakeSqueal()
        {
            const float seconds = 0.8f;
            var b = Buf(seconds);
            var r = new System.Random(701);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float hz = 2800f + 70f * Mathf.Sin(TwoPi * 9f * t) - 400f * t / seconds;
                phase += TwoPi * hz / SAMPLE_RATE;
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Exp(-Mathf.Max(0f, t - 0.15f) / 0.35f);
                b[i] = (Mathf.Sin((float)phase) * 0.8f + Noise(r) * 0.15f) * env;
            }

            return HighPass(b, 1200f);
        }

        private static float[] GenTireScreech()
        {
            const float seconds = 1.2f;
            var b = Buf(seconds);
            var r = new System.Random(702);
            float[] noise = BandPass(NoiseBuf(b.Length, r), 900f, 3500f);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float hz = 1100f + 80f * Mathf.Sin(TwoPi * 14f * t) - 250f * t / seconds;
                phase += TwoPi * hz / SAMPLE_RATE;
                float stickSlip = 0.7f + 0.3f * Mathf.Sin(TwoPi * 45f * t);
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((seconds - t) / 0.4f);
                b[i] = (noise[i] * 3.5f + Mathf.Sin((float)phase) * 0.5f) * stickSlip * env;
            }

            return b;
        }

        // ---------------------------------------------------------------- semáforo y alarma

        private static float[] GenCrosswalkTick()
        {
            var b = Buf(0.08f);
            VehicleAudioPlaceholderGenerator.AddClick(b, 0f, 1800f, 0.004f, 0.9f, new System.Random(703));
            return b;
        }

        private static float[] GenCrosswalkBeep()
        {
            const float seconds = 0.1f;
            var b = Buf(seconds);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float env = Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((seconds - t) / 0.01f);
                b[i] = Mathf.Sin(TwoPi * 2500f * t) * env;
            }

            return b;
        }

        private static float[] GenCarAlarm()
        {
            const float seconds = 2f;
            const int segments = 8;
            var b = Buf(seconds);
            float segLen = seconds / segments;
            const float ramp = 0.004f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                int seg = Mathf.Min(segments - 1, (int)(t / segLen));
                float local = t - seg * segLen;
                float hz = seg % 2 == 0 ? 900f : 1300f;
                float env = Mathf.Clamp01(local / ramp) * Mathf.Clamp01((segLen - local) / ramp);
                b[i] = Mathf.Sign(Mathf.Sin(TwoPi * hz * t)) * env;
            }

            VehicleAudioPlaceholderGenerator.LowPass(b, 4000f);
            return b;
        }

        // ---------------------------------------------------------------- peatones y entorno

        private static float[] GenFootstep(int seed, float cutoffHz, float thumpHz)
        {
            const float seconds = 0.18f;
            var b = Buf(seconds);
            var r = new System.Random(seed);
            float[] noise = LowPassCopy(NoiseBuf(b.Length, r), cutoffHz);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float thump = Mathf.Sin(TwoPi * thumpHz * t) * Mathf.Exp(-t / 0.05f);
                float scuff = noise[i] * 4f * Mathf.Exp(-t / 0.035f);
                b[i] = (thump * 0.8f + scuff) * Mathf.Clamp01(t / 0.002f);
            }

            return b;
        }

        private static float[] GenCreak(float seconds, int seed, float baseHz)
        {
            var b = Buf(seconds);
            var r = new System.Random(seed);

            // Frecuencia con caminata aleatoria suave: fricción irregular.
            float[] wander = LowPassCopy(NoiseBuf(b.Length, r), 6f);
            float[] rub = LowPassCopy(NoiseBuf(b.Length, r), 700f);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float u = t / seconds;
                float hz = baseHz * (1f + wander[i] * 18f) * (1f - 0.15f * u);
                phase += TwoPi * Mathf.Max(30f, hz) / SAMPLE_RATE;
                float p = (float)phase;
                float tone = 0f;
                for (int k = 1; k <= 6; k++) tone += Mathf.Sin(p * k) / k;
                float stickSlip = 0.5f + 0.5f * Mathf.Sin(TwoPi * 7f * t + 0.9f);
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f);
                b[i] = (tone * 0.4f + rub[i] * 2.5f) * (0.35f + 0.65f * stickSlip) * env;
            }

            return b;
        }

        private static float[] GenFactoryHum()
        {
            const float seconds = 10f;
            int n = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            int x = Mathf.RoundToInt(0.4f * SAMPLE_RATE);
            var r = new System.Random(801);

            float[] noise = LowPassCopy(NoiseBuf(n + x, r), 400f);
            float[] loop = VehicleAudioPlaceholderGenerator.MakeSeamlessLoop(noise, n, x);

            // 100 Hz de fundamental: 1000 ciclos exactos en 10 s.
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float tone = 0f;
                for (int k = 1; k <= 8; k++) tone += Mathf.Sin(TwoPi * 100f * k * t + k) / k;
                float sub = Mathf.Sin(TwoPi * 50f * t) * 0.6f;
                float am = 1f + 0.1f * Mathf.Sin(TwoPi * 3f * t / seconds);
                loop[i] = (tone * 0.35f + sub * 0.3f + loop[i] * 2.5f) * am;
            }

            // Clank periódico (golpe metálico con dos modos). Termina antes del final: el loop no se corta.
            var rc = new System.Random(802);
            foreach (float at in new[] { 1.7f, 4.2f, 6.7f, 8.9f })
            {
                VehicleAudioPlaceholderGenerator.AddClick(loop, at, 900f, 0.03f, 0.9f, rc);
                VehicleAudioPlaceholderGenerator.AddClick(loop, at, 520f, 0.05f, 0.5f, rc);
            }

            return loop;
        }

        private static float[] GenClank(int seed, float pitch, bool doubleHit)
        {
            var b = Buf(0.5f);
            var r = new System.Random(seed);
            float[] freqs = { 820f, 1370f, 2210f, 3300f };
            float[] amps = { 1f, 0.6f, 0.4f, 0.25f };
            float[] decays = { 0.12f, 0.09f, 0.06f, 0.04f };

            void Hit(float startSec, float gain)
            {
                int start = Mathf.RoundToInt(startSec * SAMPLE_RATE);
                for (int i = 0; start + i < b.Length; i++)
                {
                    float t = i / (float)SAMPLE_RATE;
                    float v = 0f;
                    for (int m = 0; m < freqs.Length; m++)
                        v += amps[m] * Mathf.Sin(TwoPi * freqs[m] * pitch * t) * Mathf.Exp(-t / decays[m]);
                    v += Noise(r) * Mathf.Exp(-t / 0.004f) * 0.8f;
                    b[start + i] += v * gain;
                }
            }

            Hit(0f, 1f);
            if (doubleHit) Hit(0.14f, 0.6f);
            return b;
        }
    }
}
