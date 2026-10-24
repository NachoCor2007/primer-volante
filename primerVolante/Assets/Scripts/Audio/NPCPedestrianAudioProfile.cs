using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Perfil de audio de un peatón NPC (US20): pasos según su velocidad (placeholder procedural), voces ocasionales
    /// y grito ante una frenada de emergencia cercana (ambos orgánicos, vacíos hasta que se descarguen).
    /// </summary>
    [CreateAssetMenu(fileName = "NPCPedestrianAudio_New", menuName = "Primer Volante/Audio/NPC Pedestrian Audio Profile", order = 13)]
    public class NPCPedestrianAudioProfile : EnvironmentAudioProfileBase
    {
        [Header("Pasos")]
        [Tooltip("Variantes de paso (placeholders). Se alternan sin repetir el anterior.")]
        public AudioClip[] Footsteps = new AudioClip[0];

        [Tooltip("Largo de un paso en metros: el intervalo entre pasos es largo de paso / velocidad.")]
        public float StrideLength = 0.7f;

        [Tooltip("Intervalo mínimo entre pasos (segundos).")]
        public float MinStepInterval = 0.25f;

        [Tooltip("Volumen mínimo de un paso (0..1).")]
        [Range(0f, 1f)] public float FootstepMinVolume = 0.35f;

        [Tooltip("Volumen máximo de un paso (0..1).")]
        [Range(0f, 1f)] public float FootstepMaxVolume = 0.55f;

        [Tooltip("Pitch mínimo de un paso.")]
        [Range(0.5f, 2f)] public float FootstepMinPitch = 0.9f;

        [Tooltip("Pitch máximo de un paso.")]
        [Range(0.5f, 2f)] public float FootstepMaxPitch = 1.1f;

        [Header("Voces ocasionales (orgánico)")]
        [Tooltip("Tos, charla corta. Pueden quedar vacías.")]
        public AudioClip[] Voices = new AudioClip[0];

        [Tooltip("Intervalo mínimo entre voces (segundos).")]
        public float VoiceMinInterval = 15f;

        [Tooltip("Intervalo máximo entre voces (segundos).")]
        public float VoiceMaxInterval = 40f;

        [Range(0f, 1f)] public float VoiceVolume = 0.6f;

        [Header("Grito (orgánico)")]
        [Tooltip("Grito o '¡eh!' ante una frenada de emergencia cercana. Puede quedar vacío.")]
        public AudioClip[] Screams = new AudioClip[0];

        [Tooltip("Radio (metros) de la frenada de emergencia de un vehículo NPC que dispara el grito.")]
        public float ScreamRadius = 12f;

        [Range(0f, 1f)] public float ScreamVolume = 0.9f;

        [Tooltip("Segundos mínimos entre dos gritos del mismo peatón.")]
        public float ScreamCooldown = 6f;

        [Header("Audio 3D")]
        [Tooltip("Distancia mínima del rolloff (metros).")]
        public float MinDistance = 1.5f;

        [Tooltip("Distancia máxima del rolloff (metros). Los pasos deben poder anticipar a un peatón que va a cruzar.")]
        public float MaxDistance = 15f;

        /// <summary>true si hay pasos asignados.</summary>
        public bool HasFootsteps => HasAnyClip(Footsteps);

        /// <summary>true si hay voces asignadas.</summary>
        public bool HasVoices => HasAnyClip(Voices);

        /// <summary>true si hay gritos asignados.</summary>
        public bool HasScreams => HasAnyClip(Screams);

        protected override void CollectMissingPlaceholders(List<string> missing)
        {
            if (!HasFootsteps) missing.Add(nameof(Footsteps));
        }

        protected override void CollectPendingOrganic(List<string> pending)
        {
            if (!HasVoices) pending.Add(nameof(Voices));
            if (!HasScreams) pending.Add(nameof(Screams));
        }

        private void OnValidate()
        {
            StrideLength = Mathf.Max(0.1f, StrideLength);
            MinStepInterval = Mathf.Max(0.05f, MinStepInterval);
            FootstepMaxVolume = Mathf.Max(FootstepMinVolume, FootstepMaxVolume);
            FootstepMaxPitch = Mathf.Max(FootstepMinPitch, FootstepMaxPitch);
            VoiceMaxInterval = Mathf.Max(VoiceMinInterval, VoiceMaxInterval);
            ScreamRadius = Mathf.Max(0f, ScreamRadius);
            MinDistance = Mathf.Max(0.01f, MinDistance);
            MaxDistance = Mathf.Max(MinDistance, MaxDistance);
        }
    }
}
