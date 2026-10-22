using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Perfil de audio de una zona de ambiente (US20): el lecho 2D que suena mientras el jugador está en la zona y
    /// los one-shots dispersos 3D que aparecen alrededor (un auto que pasa lejos en la ciudad, pájaros en el pueblo).
    /// </summary>
    [CreateAssetMenu(fileName = "Zone_New", menuName = "Primer Volante/Audio/Ambience Zone Profile", order = 10)]
    public class AmbienceZoneProfile : EnvironmentAudioProfileBase
    {
        [Header("Lecho (2D, en loop)")]
        [Tooltip("Loop del lecho ambiental. Suena en 2D mientras el jugador está en la zona (placeholder procedural).")]
        public AudioClip BedLoop;

        [Tooltip("Volumen base del lecho (0..1) con la zona al 100 % e intensidad Normal.")]
        [Range(0f, 1f)] public float BedVolume = 0.5f;

        [Header("One-shots dispersos (3D)")]
        [Tooltip("Clips que se reproducen en una posición aleatoria alrededor del oyente.")]
        public AudioClip[] ScatterClips = new AudioClip[0];

        [Tooltip("Si es true los dispersos son orgánicos (pájaros, voces): un arreglo vacío es 'pendiente' (informativo). " +
                 "Si es false son placeholders procedurales: un arreglo vacío es un error.")]
        public bool ScatterIsOrganic;

        [Tooltip("Intervalo mínimo entre dos dispersos (segundos), con la zona al 100 % e intensidad Normal.")]
        public float ScatterMinInterval = 8f;

        [Tooltip("Intervalo máximo entre dos dispersos (segundos).")]
        public float ScatterMaxInterval = 20f;

        [Tooltip("Distancia mínima al oyente a la que aparece un disperso (metros).")]
        public float ScatterMinDistance = 20f;

        [Tooltip("Distancia máxima al oyente a la que aparece un disperso (metros).")]
        public float ScatterMaxDistance = 40f;

        [Tooltip("Volumen mínimo de un disperso (0..1).")]
        [Range(0f, 1f)] public float ScatterMinVolume = 0.4f;

        [Tooltip("Volumen máximo de un disperso (0..1).")]
        [Range(0f, 1f)] public float ScatterMaxVolume = 0.8f;

        [Tooltip("Pitch mínimo de un disperso.")]
        [Range(0.5f, 2f)] public float ScatterMinPitch = 0.95f;

        [Tooltip("Pitch máximo de un disperso.")]
        [Range(0.5f, 2f)] public float ScatterMaxPitch = 1.05f;

        [Tooltip("Distancia a la que el rolloff 3D del disperso se estabiliza (metros).")]
        public float ScatterMinRolloffDistance = 8f;

        [Tooltip("Distancia máxima del rolloff 3D del disperso (metros). Debe superar a la distancia máxima de aparición.")]
        public float ScatterMaxRolloffDistance = 120f;

        /// <summary>true si hay al menos un disperso asignado.</summary>
        public bool HasScatter => HasAnyClip(ScatterClips);

        protected override void CollectMissingPlaceholders(List<string> missing)
        {
            if (BedLoop == null) missing.Add(nameof(BedLoop));
            if (!ScatterIsOrganic && !HasScatter) missing.Add(nameof(ScatterClips));
        }

        protected override void CollectPendingOrganic(List<string> pending)
        {
            if (ScatterIsOrganic && !HasScatter) pending.Add(nameof(ScatterClips));
        }

        private void OnValidate()
        {
            ScatterMinInterval = Mathf.Max(0.1f, ScatterMinInterval);
            ScatterMaxInterval = Mathf.Max(ScatterMinInterval, ScatterMaxInterval);
            ScatterMinDistance = Mathf.Max(0f, ScatterMinDistance);
            ScatterMaxDistance = Mathf.Max(ScatterMinDistance, ScatterMaxDistance);
            ScatterMaxVolume = Mathf.Max(ScatterMinVolume, ScatterMaxVolume);
            ScatterMaxPitch = Mathf.Max(ScatterMinPitch, ScatterMaxPitch);
            ScatterMinRolloffDistance = Mathf.Max(0.01f, ScatterMinRolloffDistance);
            ScatterMaxRolloffDistance = Mathf.Max(ScatterMinRolloffDistance, ScatterMaxRolloffDistance);
        }
    }
}
