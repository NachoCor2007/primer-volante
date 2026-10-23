using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Modo de reproducción de un <see cref="AmbientEmitter"/>.
    /// </summary>
    public enum AmbientEmitterMode
    {
        /// <summary>Loop continuo mientras el oyente está en rango.</summary>
        Loop = 0,

        /// <summary>One-shots a intervalos aleatorios mientras el oyente está en rango.</summary>
        RandomOneShots = 1,

        /// <summary>Al acercarse el oyente, con cierta probabilidad dispara una ráfaga corta y luego espera un cooldown.</summary>
        OnApproach = 2
    }

    /// <summary>
    /// Perfil de un emisor puntual fijo (US20): un bar, el estadio, un taller, un molino, una casa con perro.
    /// </summary>
    [CreateAssetMenu(fileName = "Emitter_New", menuName = "Primer Volante/Audio/Ambient Emitter Profile", order = 11)]
    public class AmbientEmitterProfile : EnvironmentAudioProfileBase
    {
        [Header("Modo")]
        [Tooltip("Loop continuo, one-shots aleatorios o ráfaga al acercarse.")]
        public AmbientEmitterMode Mode = AmbientEmitterMode.Loop;

        [Tooltip("Si es true los clips son orgánicos (voces, multitudes, perros): quedar vacíos es 'pendiente' (informativo). " +
                 "Si es false son placeholders procedurales: quedar vacíos es un error.")]
        public bool ClipsAreOrganic;

        [Header("Clips")]
        [Tooltip("Clip del modo Loop.")]
        public AudioClip LoopClip;

        [Tooltip("Clips de los modos RandomOneShots y OnApproach.")]
        public AudioClip[] OneShots = new AudioClip[0];

        [Header("Volumen y pitch")]
        [Tooltip("Volumen mínimo (0..1). En el modo Loop se usa el promedio de mínimo y máximo.")]
        [Range(0f, 1f)] public float MinVolume = 0.6f;

        [Tooltip("Volumen máximo (0..1).")]
        [Range(0f, 1f)] public float MaxVolume = 0.9f;

        [Tooltip("Pitch mínimo.")]
        [Range(0.5f, 2f)] public float MinPitch = 0.95f;

        [Tooltip("Pitch máximo.")]
        [Range(0.5f, 2f)] public float MaxPitch = 1.05f;

        [Header("RandomOneShots")]
        [Tooltip("Intervalo mínimo entre one-shots (segundos).")]
        public float MinInterval = 6f;

        [Tooltip("Intervalo máximo entre one-shots (segundos).")]
        public float MaxInterval = 15f;

        [Header("Audio 3D")]
        [Tooltip("Distancia mínima del rolloff (metros).")]
        public float MinDistance = 3f;

        [Tooltip("Distancia máxima del rolloff (metros). Fuera de 1.1 × este valor el emisor no trabaja.")]
        public float MaxDistance = 40f;

        [Header("OnApproach")]
        [Tooltip("Radio del oyente que dispara la ráfaga (metros). Debe ser menor que la distancia máxima.")]
        public float ApproachRadius = 14f;

        [Tooltip("Probabilidad (0..1) de disparar al entrar en el radio.")]
        [Range(0f, 1f)] public float ApproachProbability = 0.6f;

        [Tooltip("Segundos de espera tras una ráfaga antes de poder disparar otra.")]
        public float ApproachCooldown = 45f;

        [Tooltip("Cantidad mínima de one-shots en la ráfaga.")]
        [Min(1)] public int BurstMin = 1;

        [Tooltip("Cantidad máxima de one-shots en la ráfaga.")]
        [Min(1)] public int BurstMax = 3;

        /// <summary>true si hay al menos un one-shot asignado.</summary>
        public bool HasOneShots => HasAnyClip(OneShots);

        /// <summary>true si el modo actual tiene el clip que necesita.</summary>
        public bool HasClipsForMode => Mode == AmbientEmitterMode.Loop ? LoopClip != null : HasOneShots;

        protected override void CollectMissingPlaceholders(List<string> missing)
        {
            if (!ClipsAreOrganic && !HasClipsForMode)
                missing.Add(Mode == AmbientEmitterMode.Loop ? nameof(LoopClip) : nameof(OneShots));
        }

        protected override void CollectPendingOrganic(List<string> pending)
        {
            if (ClipsAreOrganic && !HasClipsForMode)
                pending.Add(Mode == AmbientEmitterMode.Loop ? nameof(LoopClip) : nameof(OneShots));
        }

        private void OnValidate()
        {
            MaxVolume = Mathf.Max(MinVolume, MaxVolume);
            MaxPitch = Mathf.Max(MinPitch, MaxPitch);
            MinInterval = Mathf.Max(0.1f, MinInterval);
            MaxInterval = Mathf.Max(MinInterval, MaxInterval);
            MinDistance = Mathf.Max(0.01f, MinDistance);
            MaxDistance = Mathf.Max(MinDistance, MaxDistance);
            ApproachRadius = Mathf.Clamp(ApproachRadius, 0.1f, MaxDistance);
            ApproachCooldown = Mathf.Max(0f, ApproachCooldown);
            BurstMin = Mathf.Max(1, BurstMin);
            BurstMax = Mathf.Max(BurstMin, BurstMax);
        }
    }
}
