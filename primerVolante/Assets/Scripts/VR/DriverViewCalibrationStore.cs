using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Persistencia (PlayerPrefs) de la calibración de vista del conductor: una sola calibración global, compartida por
    /// todas las escenas de manejo y que sobrevive al cierre de la app. Las claves están versionadas por si cambia el formato.
    /// <para>
    /// <see cref="Save"/> solo actualiza el caché y los PlayerPrefs en memoria (los sliders cambian el valor en cada frame);
    /// el volcado a disco lo hace <see cref="Flush"/>, que se llama al cerrar el panel, al descargar la escena y al pausar la app.
    /// </para>
    /// </summary>
    public static class DriverViewCalibrationStore
    {
        public const string KEY_X = "PrimerVolante.DriverView.Calibration.v1.x";
        public const string KEY_Y = "PrimerVolante.DriverView.Calibration.v1.y";
        public const string KEY_Z = "PrimerVolante.DriverView.Calibration.v1.z";
        public const string KEY_HAS = "PrimerVolante.DriverView.Calibration.v1.has";

        // Caché en memoria: se lee del backend una sola vez.
        private static bool s_CacheLoaded;
        private static bool s_CachedHasValue;
        private static Vector3 s_CachedOffset;
        private static bool s_Dirty;

        // Backend en memoria para los tests: nunca toca PlayerPrefs.
        private static bool s_InMemoryBackend;
        private static readonly Dictionary<string, float> s_MemoryFloats = new();
        private static readonly Dictionary<string, int> s_MemoryInts = new();
        private static int s_FlushCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_CacheLoaded = false;
            s_CachedHasValue = false;
            s_CachedOffset = Vector3.zero;
            s_Dirty = false;
        }

        /// <summary>True si hay una calibración guardada válida (sin NaN ni infinitos).</summary>
        public static bool HasSaved => TryLoad(out _);

        /// <summary>
        /// Devuelve la calibración guardada. False si no hay nada guardado o si algún componente es NaN o infinito
        /// (en ese caso se ignora lo guardado).
        /// </summary>
        public static bool TryLoad(out Vector3 offset)
        {
            EnsureCacheLoaded();
            offset = Vector3.zero;
            if (!s_CachedHasValue || !IsFinite(s_CachedOffset)) return false;

            offset = s_CachedOffset;
            return true;
        }

        /// <summary>
        /// Guarda la calibración en el caché y en los PlayerPrefs (en memoria), y la marca pendiente de volcar a disco.
        /// No llama a <c>PlayerPrefs.Save()</c>: usar <see cref="Flush"/>.
        /// </summary>
        public static void Save(Vector3 offset)
        {
            s_CacheLoaded = true;
            s_CachedHasValue = true;
            s_CachedOffset = offset;

            SetFloat(KEY_X, offset.x);
            SetFloat(KEY_Y, offset.y);
            SetFloat(KEY_Z, offset.z);
            SetInt(KEY_HAS, 1);
            s_Dirty = true;
        }

        /// <summary>Vuelca a disco lo guardado, solo si hubo cambios desde el último volcado.</summary>
        public static void Flush()
        {
            if (!s_Dirty) return;

            WriteToDisk();
            s_Dirty = false;
        }

        /// <summary>Borra la calibración guardada y el caché, y vuelca el borrado a disco.</summary>
        public static void Clear()
        {
            DeleteKey(KEY_X);
            DeleteKey(KEY_Y);
            DeleteKey(KEY_Z);
            DeleteKey(KEY_HAS);

            s_CacheLoaded = true;
            s_CachedHasValue = false;
            s_CachedOffset = Vector3.zero;
            WriteToDisk();
            s_Dirty = false;
        }

        // ------------------------------------------------------------------ Backend

        private static void EnsureCacheLoaded()
        {
            if (s_CacheLoaded) return;

            s_CacheLoaded = true;
            s_CachedHasValue = GetInt(KEY_HAS) == 1;
            s_CachedOffset = s_CachedHasValue
                ? new Vector3(GetFloat(KEY_X), GetFloat(KEY_Y), GetFloat(KEY_Z))
                : Vector3.zero;
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        private static void SetFloat(string key, float value)
        {
            if (s_InMemoryBackend) s_MemoryFloats[key] = value;
            else PlayerPrefs.SetFloat(key, value);
        }

        private static void SetInt(string key, int value)
        {
            if (s_InMemoryBackend) s_MemoryInts[key] = value;
            else PlayerPrefs.SetInt(key, value);
        }

        private static float GetFloat(string key)
        {
            if (s_InMemoryBackend) return s_MemoryFloats.TryGetValue(key, out float f) ? f : 0f;
            return PlayerPrefs.GetFloat(key, 0f);
        }

        private static int GetInt(string key)
        {
            if (s_InMemoryBackend) return s_MemoryInts.TryGetValue(key, out int i) ? i : 0;
            return PlayerPrefs.GetInt(key, 0);
        }

        private static void DeleteKey(string key)
        {
            if (s_InMemoryBackend)
            {
                s_MemoryFloats.Remove(key);
                s_MemoryInts.Remove(key);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        private static void WriteToDisk()
        {
            if (s_InMemoryBackend) s_FlushCount++;
            else PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ Seams de tests

        /// <summary>
        /// Seam de tests: con el backend en memoria todas las operaciones usan un diccionario interno y nunca tocan PlayerPrefs.
        /// Al activarlo o desactivarlo se descarta el estado en memoria (valores, caché, dirty y contador de volcados).
        /// </summary>
        public static void UseInMemoryBackendForTests(bool enabled)
        {
            s_InMemoryBackend = enabled;
            s_MemoryFloats.Clear();
            s_MemoryInts.Clear();
            s_FlushCount = 0;
            ResetStatics();
        }

        /// <summary>True si el backend en memoria de tests está activo.</summary>
        public static bool IsInMemoryBackendActive => s_InMemoryBackend;

        /// <summary>Con el backend en memoria, cuántas veces se habría escrito a disco (<c>PlayerPrefs.Save()</c>).</summary>
        public static int FlushCountForTests => s_FlushCount;

        /// <summary>Descarta el caché para forzar que la próxima lectura venga del backend.</summary>
        public static void InvalidateCacheForTests()
        {
            s_CacheLoaded = false;
        }
    }
}
