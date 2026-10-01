using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Genera clips de audio placeholder sintéticos (WAV 16-bit PCM mono 44.1 kHz) en
    /// <c>Assets/Audio/Vehicle/</c> y configura su importación (mono, load type, compresión).
    /// NUNCA sobrescribe un archivo existente: para usar clips reales (CC0) basta reemplazar el
    /// archivo con el mismo nombre; el perfil de audio sigue apuntando a él sin tocar código.
    ///
    /// Tabla de clips (qué buscar al reemplazarlos por clips reales):
    ///
    /// | Archivo                       | Tipo            | Descripción / referencia                                     |
    /// |-------------------------------|-----------------|--------------------------------------------------------------|
    /// | engine_start.wav              | one-shot ~1.2 s | burro de arranque (~12 Hz) que termina "prendiendo"          |
    /// | engine_stop.wav               | one-shot ~0.8 s | caída de RPM con decaimiento                                 |
    /// | engine_start_fail.wav         | one-shot ~0.25 s| clic seco de solenoide / buzz corto de rechazo               |
    /// | engine_idle_loop.wav          | loop ~2 s       | motor 4 cil. a ~800 RPM, loop sin corte, mono                |
    /// | engine_mid_loop.wav           | loop ~2 s       | ídem a ~2500 RPM                                             |
    /// | engine_high_loop.wav          | loop ~2 s       | ídem a ~4500 RPM                                             |
    /// | blinker_tick.wav / _tock.wav  | one-shot ~40 ms | clic de relé (tick más agudo que tock)                       |
    /// | handbrake_ratchet_click.wav   | one-shot ~30 ms | diente de trinquete                                          |
    /// | handbrake_pull_full.wav       | one-shot ~0.4 s | ráfaga de ~7 clics de trinquete                              |
    /// | handbrake_release.wav         | one-shot ~0.2 s | "clunk" metálico grave                                       |
    /// | button_click.wav              | one-shot ~50 ms | clic de botón                                                |
    /// | stalk_click.wav               | one-shot ~60 ms | clic de palanca de guiño                                     |
    /// | knob_click.wav                | one-shot ~45 ms | clic de perilla                                              |
    /// | gear_clunk.wav                | one-shot ~0.15 s| golpe grave de selector                                      |
    /// | wind_road_loop.wav            | loop ~3 s       | ruido rosa/marrón filtrado, sin corte                        |
    ///
    /// Los loops del motor deben ser mono, sin clic en el punto de loop y con las RPM de
    /// referencia de arriba (el pitch se calcula como rpm / rpmReferencia de cada capa).
    /// </summary>
    public static class VehicleAudioPlaceholderGenerator
    {
        public const string AUDIO_FOLDER = "Assets/Audio/Vehicle";
        private const int SAMPLE_RATE = 44100;

        private static readonly string[] s_LoopFiles =
        {
            "engine_idle_loop.wav", "engine_mid_loop.wav", "engine_high_loop.wav", "wind_road_loop.wav"
        };

        private static readonly string[] s_OneShotFiles =
        {
            "engine_start.wav", "engine_stop.wav", "engine_start_fail.wav",
            "blinker_tick.wav", "blinker_tock.wav",
            "handbrake_ratchet_click.wav", "handbrake_pull_full.wav", "handbrake_release.wav",
            "button_click.wav", "stalk_click.wav", "knob_click.wav", "gear_clunk.wav"
        };

        [MenuItem("Tools/Primer Volante/Generate Vehicle Audio Placeholders")]
        public static void GenerateFromMenu()
        {
            GeneratePlaceholders();
        }

        /// <summary>
        /// Genera los WAV faltantes, importa y aplica la configuración de importación.
        /// </summary>
        public static void GeneratePlaceholders()
        {
            Directory.CreateDirectory(AUDIO_FOLDER);

            int created = 0;
            created += Write("engine_start.wav", GenEngineStart());
            created += Write("engine_stop.wav", GenEngineStop());
            created += Write("engine_start_fail.wav", GenStartFail());
            created += Write("engine_idle_loop.wav", GenEngineLoop(800f, 1));
            created += Write("engine_mid_loop.wav", GenEngineLoop(2500f, 2));
            created += Write("engine_high_loop.wav", GenEngineLoop(4500f, 3));
            created += Write("blinker_tick.wav", GenClick(0.040f, 2600f, 0.005f, 0.8f, 11));
            created += Write("blinker_tock.wav", GenClick(0.040f, 1500f, 0.006f, 0.8f, 12));
            created += Write("handbrake_ratchet_click.wav", GenClick(0.030f, 3400f, 0.004f, 0.7f, 13));
            created += Write("handbrake_pull_full.wav", GenRatchetBurst());
            created += Write("handbrake_release.wav", GenThunk(0.2f, 95f, 420f, 0.05f, 21));
            created += Write("button_click.wav", GenClick(0.050f, 2000f, 0.008f, 0.6f, 14));
            created += Write("stalk_click.wav", GenDoubleClick());
            created += Write("knob_click.wav", GenClick(0.045f, 950f, 0.010f, 0.7f, 16));
            created += Write("gear_clunk.wav", GenThunk(0.15f, 70f, 260f, 0.035f, 22));
            created += Write("wind_road_loop.wav", GenWindRoad());

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyImportSettings();

            Debug.Log($"[VehicleAudioPlaceholderGenerator] 🔊 Placeholders generados: {created} nuevos (los existentes no se tocan).");
        }

        /// <summary>
        /// Configura la importación de todos los clips del vehículo (mono, load type, compresión).
        /// </summary>
        public static void ApplyImportSettings()
        {
            foreach (string file in s_LoopFiles)
                ConfigureImporter($"{AUDIO_FOLDER}/{file}", true);
            foreach (string file in s_OneShotFiles)
                ConfigureImporter($"{AUDIO_FOLDER}/{file}", false);
        }

        private static void ConfigureImporter(string path, bool isLoop)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;

            importer.forceToMono = true;
            importer.ambisonic = false;

            // Loops: Vorbis (Android decodifica por software) descomprimido al cargar: son cortos
            // (2-3 s mono ≈ 170-260 KB) y no pagan CPU por reproducción ni por loop.
            // One-shots cortos: ADPCM descomprimido al cargar (latencia mínima, 3.5:1).
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            settings.compressionFormat = isLoop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            settings.quality = isLoop ? 0.7f : 1f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;

            // Override Android (Meta Quest): mismo criterio, para que sea explícito por plataforma.
            AudioImporterSampleSettings android = settings;
            importer.SetOverrideSampleSettings("Android", android);

            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- escritura WAV

        private static int Write(string fileName, float[] samples)
        {
            string path = $"{AUDIO_FOLDER}/{fileName}";
            if (File.Exists(path)) return 0;

            Normalize(samples, 0.85f);
            WriteWav16(path, samples);
            return 1;
        }

        private static void Normalize(float[] s, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < s.Length; i++) max = Mathf.Max(max, Mathf.Abs(s[i]));
            float g = peak / max;
            for (int i = 0; i < s.Length; i++) s[i] *= g;
        }

        private static void WriteWav16(string path, float[] samples)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);            // PCM
                w.Write((short)1);            // mono
                w.Write(SAMPLE_RATE);
                w.Write(SAMPLE_RATE * 2);     // byte rate
                w.Write((short)2);            // block align
                w.Write((short)16);           // bits
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++)
                    w.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * 32767f));
            }
        }

        // ---------------------------------------------------------------- síntesis base

        private static float[] Buffer(float seconds) => new float[Mathf.RoundToInt(seconds * SAMPLE_RATE)];

        private static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        private static float Tri(float x) => Mathf.Clamp01(x);

        /// <summary>Filtro pasa-bajos de un polo, in-place.</summary>
        private static void LowPass(float[] s, float cutoffHz)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SAMPLE_RATE);
            float y = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                y += a * (s[i] - y);
                s[i] = y;
            }
        }

        /// <summary>
        /// Convierte un buffer de longitud N+X en un loop de N muestras sin clic: los primeros X
        /// samples se mezclan con potencia constante con la cola extra, de modo que el final
        /// (m[N-1]) continúa naturalmente en el inicio (m[N]).
        /// </summary>
        private static float[] MakeSeamlessLoop(float[] extended, int n, int x)
        {
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = extended[i];
            for (int i = 0; i < x; i++)
            {
                float t = (i / (float)x) * Mathf.PI * 0.5f;
                o[i] = extended[i] * Mathf.Sin(t) + extended[n + i] * Mathf.Cos(t);
            }
            return o;
        }

        private static float DampedSine(float t, float freq, float decay)
        {
            return Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t / decay);
        }

        private static void AddClick(float[] buf, float startSec, float freq, float decay, float amp, System.Random r)
        {
            int start = Mathf.RoundToInt(startSec * SAMPLE_RATE);
            int len = Mathf.Min(buf.Length - start, Mathf.RoundToInt(decay * 8f * SAMPLE_RATE));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float n = Noise(r) * Mathf.Exp(-t / (decay * 0.35f)) * 0.4f;
                buf[start + i] += amp * (DampedSine(t, freq, decay) + n);
            }
        }

        // ---------------------------------------------------------------- clips

        private static float[] GenClick(float seconds, float freq, float decay, float amp, int seed)
        {
            var b = Buffer(seconds);
            AddClick(b, 0f, freq, decay, amp, new System.Random(seed));
            return b;
        }

        private static float[] GenDoubleClick()
        {
            var b = Buffer(0.060f);
            var r = new System.Random(15);
            AddClick(b, 0f, 1300f, 0.006f, 0.8f, r);
            AddClick(b, 0.028f, 1700f, 0.005f, 0.6f, r);
            return b;
        }

        private static float[] GenRatchetBurst()
        {
            var b = Buffer(0.4f);
            var r = new System.Random(17);
            // 7 clics con intervalos decrecientes (la palanca acelera al tirar).
            float t = 0.005f;
            float gap = 0.075f;
            for (int i = 0; i < 7; i++)
            {
                AddClick(b, t, 3200f + i * 90f, 0.004f, 0.7f, r);
                t += gap;
                gap *= 0.88f;
            }
            return b;
        }

        private static float[] GenThunk(float seconds, float lowFreq, float metalFreq, float decay, int seed)
        {
            var b = Buffer(seconds);
            var r = new System.Random(seed);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float low = DampedSine(t, lowFreq, decay * 1.6f);
                float metal = DampedSine(t, metalFreq, decay * 0.5f) * 0.45f;
                float n = Noise(r) * Mathf.Exp(-t / (decay * 0.2f)) * 0.5f;
                b[i] = low + metal + n;
            }
            return b;
        }

        private static float[] GenStartFail()
        {
            var b = Buffer(0.25f);
            var r = new System.Random(18);
            AddClick(b, 0f, 1800f, 0.004f, 0.9f, r);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                if (t < 0.03f || t > 0.2f) continue;
                float env = Mathf.Exp(-(t - 0.03f) / 0.06f);
                float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 120f * t)) * 0.25f
                             + Mathf.Sin(2f * Mathf.PI * 240f * t) * 0.15f;
                b[i] += buzz * env;
            }
            AddClick(b, 0.2f, 1400f, 0.005f, 0.7f, r);
            return b;
        }

        /// <summary>
        /// Suma de armónicos de la frecuencia de explosión con fase acumulada (permite variar la frecuencia).
        /// </summary>
        private static float Harmonics(double phase, int count, float rolloff, float brightness)
        {
            float v = 0f;
            for (int k = 1; k <= count; k++)
            {
                float w = 1f / Mathf.Pow(k, rolloff);
                // El brillo realza los armónicos altos en regímenes más altos.
                w *= Mathf.Lerp(1f, 0.6f + 0.4f * k / count * 3f, brightness);
                v += w * Mathf.Sin((float)(phase * k));
            }
            return v;
        }

        private static float[] GenEngineStart()
        {
            var b = Buffer(1.2f);
            var r = new System.Random(31);
            double crankPhase = 0, firePhase = 0;
            float[] noise = new float[b.Length];
            for (int i = 0; i < noise.Length; i++) noise[i] = Noise(r);
            LowPass(noise, 900f);

            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;

                // Burro: pulsos ~12 Hz (leve aceleración) con zumbido del motor de arranque.
                float crankHz = Mathf.Lerp(11f, 13f, t / 0.9f);
                crankPhase += 2.0 * Math.PI * crankHz / SAMPLE_RATE;
                float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin((float)crankPhase)), 3f);
                float buzz = Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.3f + Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 190f * t)) * 0.1f;
                float crank = (pulse * (0.5f + buzz) + noise[i] * 0.3f * pulse);
                float crankEnv = 1f - Tri((t - 0.85f) / 0.2f); // se apaga al prender

                // Encendido: a partir de ~0.8 s aparecen explosiones que llegan a ralentí (26.7 Hz).
                float fireHz = Mathf.Lerp(10f, 26.7f, Tri((t - 0.75f) / 0.45f));
                firePhase += 2.0 * Math.PI * fireHz / SAMPLE_RATE;
                float fireEnv = Tri((t - 0.75f) / 0.15f);
                float fire = Harmonics(firePhase, 8, 0.9f, 0f) * 0.25f + noise[i] * 0.15f;

                b[i] = crank * crankEnv + fire * fireEnv;
            }
            return b;
        }

        private static float[] GenEngineStop()
        {
            var b = Buffer(0.8f);
            var r = new System.Random(32);
            double firePhase = 0;
            float[] noise = new float[b.Length];
            for (int i = 0; i < noise.Length; i++) noise[i] = Noise(r);
            LowPass(noise, 700f);

            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float hz = Mathf.Lerp(26.7f, 6f, Mathf.Pow(t / 0.8f, 0.7f));
                firePhase += 2.0 * Math.PI * hz / SAMPLE_RATE;
                float env = Mathf.Exp(-t / 0.28f);
                b[i] = (Harmonics(firePhase, 8, 0.9f, 0f) * 0.3f + noise[i] * 0.18f) * env;
            }
            return b;
        }

        /// <summary>
        /// Loop de motor de 4 cilindros: armónicos de la frecuencia de explosión (RPM/60 × 2), con
        /// número entero de ciclos, más ruido filtrado con crossfade en el punto de loop.
        /// </summary>
        private static float[] GenEngineLoop(float rpm, int seed)
        {
            const float seconds = 2f;
            int n = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            int x = Mathf.RoundToInt(0.15f * SAMPLE_RATE);

            float fireHz = rpm / 60f * 2f;
            // Ciclos enteros y pares, para que también sea periódica la subarmónica (irregularidad).
            int cycles = Mathf.Max(2, Mathf.RoundToInt(fireHz * seconds / 2f) * 2);
            float f0 = cycles / seconds;

            float brightness = Mathf.InverseLerp(800f, 4500f, rpm);
            var r = new System.Random(seed * 101);

            // Ruido de admisión/mecánico filtrado (extendido para el crossfade).
            var noise = new float[n + x];
            for (int i = 0; i < noise.Length; i++) noise[i] = Noise(r);
            LowPass(noise, Mathf.Lerp(500f, 2500f, brightness));
            float noiseAmp = Mathf.Lerp(0.35f, 0.6f, brightness) * 2.2f;

            var loop = MakeSeamlessLoop(noise, n, x);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SAMPLE_RATE;
                double phase = 2.0 * Math.PI * f0 * t;
                float h = Harmonics(phase, 14, Mathf.Lerp(1.0f, 0.7f, brightness), brightness);
                float sub = Mathf.Sin((float)(phase * 0.5)) * 0.35f; // irregularidad entre cilindros
                loop[i] = h * 0.35f + sub * 0.3f + loop[i] * noiseAmp * 0.35f;
            }
            return loop;
        }

        private static float[] GenWindRoad()
        {
            const float seconds = 3f;
            int n = Mathf.RoundToInt(seconds * SAMPLE_RATE);
            int x = Mathf.RoundToInt(0.3f * SAMPLE_RATE);
            var r = new System.Random(77);

            var brown = new float[n + x];
            float acc = 0f;
            for (int i = 0; i < brown.Length; i++)
            {
                acc = acc * 0.995f + Noise(r) * 0.08f; // ruido marrón con fuga (evita deriva)
                brown[i] = acc;
            }

            var pink = new float[n + x];
            for (int i = 0; i < pink.Length; i++) pink[i] = Noise(r);
            LowPass(pink, 1800f);

            for (int i = 0; i < brown.Length; i++) brown[i] = brown[i] * 1.6f + pink[i] * 0.5f;

            return MakeSeamlessLoop(brown, n, x);
        }
    }
}
