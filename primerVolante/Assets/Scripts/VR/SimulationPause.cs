using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Dueño único de <c>Time.timeScale</c> y <c>AudioListener.pause</c> cuando algo congela la simulación (menú in-game,
    /// pausa por desconexión VR). Cada dueño toma un token con <see cref="Acquire"/> y lo suelta con <see cref="Release"/>:
    /// el primer Acquire guarda los valores previos y el último Release los restaura tal cual, sin importar el orden en que
    /// se solapen los dueños. El audio queda pausado mientras algún token lo pida. Es idempotente por dueño: repetir Acquire
    /// solo vuelve a aplicar el congelamiento (por si algo pisó <c>timeScale</c>) sin perder el valor original, y Release de
    /// un dueño que no tiene token no hace nada. Un dueño destruido sin soltar su token (p. ej. una escena que se
    /// descarga) se descarta solo en la siguiente operación.
    /// </summary>
    public static class SimulationPause
    {
        // dueño -> si pide pausar también el audio
        private static readonly Dictionary<object, bool> s_Owners = new Dictionary<object, bool>();
        private static readonly List<object> s_Buffer = new List<object>();
        private static bool s_HasSnapshot;
        private static float s_SavedTimeScale = 1f;
        private static bool s_SavedAudioPause;

        /// <summary>true mientras algún dueño tiene el token (la simulación está congelada).</summary>
        public static bool IsPaused
        {
            get
            {
                Prune();
                return s_Owners.Count > 0;
            }
        }

        /// <summary>true mientras algún token pide también el audio pausado.</summary>
        public static bool IsAudioPaused
        {
            get
            {
                Prune();
                foreach (bool pauseAudio in s_Owners.Values)
                {
                    if (pauseAudio) return true;
                }
                return false;
            }
        }

        /// <summary>Cantidad de dueños con token vigente.</summary>
        public static int OwnerCount
        {
            get
            {
                Prune();
                return s_Owners.Count;
            }
        }

        /// <summary>true si <paramref name="owner"/> tiene el token.</summary>
        public static bool Holds(object owner)
        {
            Prune();
            return owner != null && s_Owners.ContainsKey(owner);
        }

        // Con "Enter Play Mode Options" sin recarga de dominio los estáticos sobreviven entre Play: se reinician aquí.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Clear();

        /// <summary>
        /// Congela la simulación para <paramref name="owner"/>. Si es el primer dueño guarda <c>timeScale</c> y el audio previos.
        /// Repetirlo para el mismo dueño reaplica el congelamiento sin tocar los valores guardados.
        /// </summary>
        public static void Acquire(object owner, bool pauseAudio)
        {
            if (owner == null) return;

            Prune();
            if (s_Owners.Count == 0)
            {
                s_HasSnapshot = true;
                s_SavedTimeScale = Time.timeScale;
                s_SavedAudioPause = AudioListener.pause;
            }
            s_Owners[owner] = pauseAudio;
            Apply();
        }

        /// <summary>Suelta el token de <paramref name="owner"/>. El último en soltar restaura los valores previos.</summary>
        public static void Release(object owner)
        {
            if (owner == null) return;

            bool removed = s_Owners.Remove(owner);
            Prune();
            if (!removed) return;

            if (s_Owners.Count == 0) Restore();
            else Apply();
        }

#if UNITY_EDITOR
        /// <summary>Solo Editor (tests): olvida todos los tokens sin tocar <c>timeScale</c> ni el audio.</summary>
        public static void ResetForTests() => Clear();
#endif

        private static void Clear()
        {
            s_Owners.Clear();
            s_HasSnapshot = false;
        }

        private static void Apply()
        {
            Time.timeScale = 0f;
            bool anyAudio = false;
            foreach (bool pauseAudio in s_Owners.Values)
            {
                if (pauseAudio) { anyAudio = true; break; }
            }
            AudioListener.pause = anyAudio || s_SavedAudioPause;
        }

        private static void Restore()
        {
            if (!s_HasSnapshot) return;
            s_HasSnapshot = false;
            Time.timeScale = s_SavedTimeScale;
            AudioListener.pause = s_SavedAudioPause;
        }

        // Descarta los dueños destruidos que no soltaron su token (el último en irse restaura los valores previos).
        private static void Prune()
        {
            if (s_Owners.Count == 0) return;

            s_Buffer.Clear();
            foreach (object owner in s_Owners.Keys)
            {
                if (owner is Object unityObject && unityObject == null) s_Buffer.Add(owner);
            }
            if (s_Buffer.Count == 0) return;

            foreach (object dead in s_Buffer) s_Owners.Remove(dead);
            s_Buffer.Clear();
            if (s_Owners.Count == 0) Restore();
        }
    }
}
