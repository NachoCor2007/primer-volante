using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Base de los perfiles de audio del entorno (US20). Son solo datos de configuración compartidos
    /// (ScriptableObject): los clips placeholder se reemplazan por clips reales sin tocar código y los clips
    /// orgánicos (voces, pájaros, perros, multitudes) quedan vacíos hasta que se descarguen.
    /// </summary>
    /// <remarks>
    /// La validación separa dos listas: los <b>placeholders faltantes</b> (clips mecánicos que el generador debería
    /// haber asignado; es un error) y los <b>orgánicos pendientes</b> (clips que el usuario consigue después; solo
    /// informativo). Con clips nulos el audio simplemente no suena.
    /// </remarks>
    public abstract class EnvironmentAudioProfileBase : ScriptableObject
    {
        /// <summary>Nombres de los clips mecánicos (placeholder) que faltan. Vacío si está completo.</summary>
        public string[] GetMissingPlaceholders()
        {
            var missing = new List<string>();
            CollectMissingPlaceholders(missing);
            return missing.ToArray();
        }

        /// <summary>Nombres de los clips orgánicos que todavía no se asignaron. Es solo informativo.</summary>
        public string[] GetPendingOrganic()
        {
            var pending = new List<string>();
            CollectPendingOrganic(pending);
            return pending.ToArray();
        }

        /// <summary>Agrega a <paramref name="missing"/> los clips mecánicos sin asignar.</summary>
        protected abstract void CollectMissingPlaceholders(List<string> missing);

        /// <summary>Agrega a <paramref name="pending"/> los clips orgánicos sin asignar.</summary>
        protected abstract void CollectPendingOrganic(List<string> pending);

        /// <summary>Escribe en la consola el resultado de la validación del perfil.</summary>
        [ContextMenu("Validar clips")]
        public void ValidateClips()
        {
            string[] missing = GetMissingPlaceholders();
            string[] pending = GetPendingOrganic();

            if (missing.Length > 0)
                Debug.LogError($"[{GetType().Name}] ❌ '{name}': faltan placeholders: {string.Join(", ", missing)}", this);
            if (pending.Length > 0)
                Debug.Log($"[{GetType().Name}] ℹ️ '{name}': clips orgánicos pendientes (ver SOUNDS_TODO.md): {string.Join(", ", pending)}", this);
            if (missing.Length == 0 && pending.Length == 0)
                Debug.Log($"[{GetType().Name}] ✅ '{name}': todos los clips asignados.", this);
        }

        /// <summary>true si el arreglo tiene al menos un clip asignado.</summary>
        protected static bool HasAnyClip(AudioClip[] clips)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null) return true;
            }

            return false;
        }

        /// <summary>
        /// Elige un clip al azar entre los asignados (ignora los nulos). Devuelve <c>null</c> si no hay ninguno.
        /// </summary>
        public static AudioClip PickClip(AudioClip[] clips, System.Random random, AudioClip avoid = null)
        {
            if (clips == null || clips.Length == 0) return null;

            int valid = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i] != avoid) valid++;
            }

            // Si el único clip es el que hay que evitar, se repite.
            if (valid == 0) avoid = null;
            if (valid == 0)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null) valid++;
                }
            }

            if (valid == 0) return null;

            int pick = random.Next(valid);
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null || clips[i] == avoid) continue;
                if (pick-- == 0) return clips[i];
            }

            return null;
        }
    }
}
