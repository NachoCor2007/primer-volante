using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Utilidades compartidas por los componentes de audio del entorno (US20): localizar al oyente (la cabeza del
    /// jugador), crear fuentes 3D y semillas deterministas.
    /// </summary>
    public static class EnvironmentAudioUtil
    {
        /// <summary>Prioridad de las alertas (menor = más importante).</summary>
        public const int PriorityAlert = 32;

        /// <summary>Prioridad de los lechos (pocos y siempre audibles).</summary>
        public const int PriorityBed = 64;

        /// <summary>Prioridad del motor y los pasos de los NPC.</summary>
        public const int PriorityNpc = 96;

        /// <summary>Prioridad de los emisores puntuales.</summary>
        public const int PriorityEmitter = 160;

        /// <summary>Prioridad de los one-shots dispersos.</summary>
        public const int PriorityScatter = 192;

        // Segundos entre búsquedas del oyente cuando no hay ninguno activo.
        private const float k_SearchInterval = 0.5f;

        private static AudioListener s_Listener;
        private static float s_NextSearchTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Listener = null;
            s_NextSearchTime = 0f;
        }

        /// <summary>
        /// El <see cref="AudioListener"/> activo (el de la cámara del XR Origin: la cabeza del jugador). Se busca solo
        /// entre los activos y se vuelve a buscar, como máximo cada medio segundo, si queda nulo o inactivo.
        /// Devuelve <c>null</c> si no hay ninguno.
        /// </summary>
        public static AudioListener GetListener()
        {
            if (s_Listener != null && s_Listener.isActiveAndEnabled) return s_Listener;

            if (Time.unscaledTime < s_NextSearchTime) return null;
            s_NextSearchTime = Time.unscaledTime + k_SearchInterval;
            s_Listener = Object.FindAnyObjectByType<AudioListener>();
            return s_Listener;
        }

        /// <summary>Posición del oyente. Devuelve false si no hay ninguno.</summary>
        public static bool TryGetListenerPosition(out Vector3 position)
        {
            AudioListener listener = GetListener();
            if (listener == null)
            {
                position = default;
                return false;
            }

            position = listener.transform.position;
            return true;
        }

        /// <summary>
        /// Crea (o reutiliza) un hijo con una <see cref="AudioSource"/> 3D sin Doppler, rolloff logarítmico y sin
        /// play on awake, ruteada a <paramref name="group"/>.
        /// </summary>
        public static AudioSource CreateSource3D(
            Transform parent, string name, AudioMixerGroup group, int priority, float minDistance, float maxDistance, bool loop = false)
        {
            Transform child = parent.Find(name);
            GameObject go = child != null ? child.gameObject : new GameObject(name);
            if (child == null)
            {
                go.transform.SetParent(parent, false);
            }

            var source = go.GetComponent<AudioSource>();
            if (source == null) source = go.AddComponent<AudioSource>();

            PrimerVolante.VR.VehicleAudioUtil.ConfigureSource(source, loop, minDistance, maxDistance, group, priority);
            return source;
        }

        /// <summary>
        /// Semilla determinista derivada de una posición (no usa <c>GetInstanceID</c>, que cambia entre sesiones).
        /// </summary>
        public static int SeedFromPosition(Vector3 position)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(position.x * 100f);
                h = h * 31 + Mathf.RoundToInt(position.y * 100f);
                h = h * 31 + Mathf.RoundToInt(position.z * 100f);
                return h;
            }
        }

        /// <summary>Número aleatorio uniforme entre min y max con un <see cref="System.Random"/>.</summary>
        public static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
    }
}
