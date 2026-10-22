using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Perfil de audio de un vehículo NPC (US20): motor en dos capas (ralentí y régimen medio) cuyo pitch sigue a la
    /// velocidad, frenadas, bocinas y sirena. <c>NPCVehicleAudio_Default</c> sirve para autos y camionetas;
    /// <c>NPCVehicleAudio_Heavy</c> (pitch más grave) para buses, camiones y contenedores.
    /// </summary>
    [CreateAssetMenu(fileName = "NPCVehicleAudio_New", menuName = "Primer Volante/Audio/NPC Vehicle Audio Profile", order = 12)]
    public class NPCVehicleAudioProfile : EnvironmentAudioProfileBase
    {
        [Header("Motor - Clips")]
        [Tooltip("Loop de ralentí. Se reutiliza el del auto del jugador con otro pitch.")]
        public AudioClip EngineIdleLoop;

        [Tooltip("Loop de régimen medio. Se reutiliza el del auto del jugador con otro pitch.")]
        public AudioClip EngineMidLoop;

        [Header("Motor - Parámetros")]
        [Tooltip("Velocidad (m/s) a la que el motor llega a su pitch máximo y al loop medio puro.")]
        public float ReferenceSpeed = 14f;

        [Tooltip("Pitch con el vehículo detenido.")]
        [Range(0.3f, 2.5f)] public float MinPitch = 0.9f;

        [Tooltip("Pitch a la velocidad de referencia.")]
        [Range(0.3f, 2.5f)] public float MaxPitch = 1.5f;

        [Tooltip("Volumen del motor (0..1).")]
        [Range(0f, 1f)] public float EngineVolume = 0.55f;

        [Header("Frenadas y bocinas - Clips")]
        [Tooltip("Chillido leve de la frenada suave.")]
        public AudioClip SoftBrake;

        [Tooltip("Chirrido de neumáticos de la frenada de emergencia.")]
        public AudioClip TireScreech;

        [Tooltip("Bocina corta.")]
        public AudioClip HornShort;

        [Tooltip("Bocina larga.")]
        public AudioClip HornLong;

        [Tooltip("Loop de la sirena (ambulancia y patrulla). La sirena viene apagada por defecto.")]
        public AudioClip SirenLoop;

        [Header("Frenadas y bocinas - Volúmenes")]
        [Range(0f, 1f)] public float SoftBrakeVolume = 0.5f;
        [Range(0f, 1f)] public float TireScreechVolume = 0.9f;
        [Range(0f, 1f)] public float HornVolume = 0.9f;
        [Range(0f, 1f)] public float SirenVolume = 0.8f;

        [Header("Umbrales")]
        [Tooltip("Velocidad mínima (m/s) para que la frenada suave suene.")]
        public float SoftBrakeMinSpeed = 3f;

        [Tooltip("Segundos mínimos entre dos frenadas suaves.")]
        public float SoftBrakeCooldown = 2f;

        [Tooltip("Velocidad mínima (m/s) previa a la frenada de emergencia para que suene el chirrido.")]
        public float ScreechMinSpeed = 2f;

        [Header("Ducking del ambiente")]
        [Tooltip("Cuánto baja el ambiente (dB) con el chirrido o la bocina.")]
        public float DuckDb = 6f;

        [Tooltip("Segundos que se mantiene bajo el ambiente antes de volver.")]
        public float DuckHoldSeconds = 1.2f;

        [Header("Audio 3D")]
        [Tooltip("Distancia mínima del rolloff del motor (metros).")]
        public float EngineMinDistance = 3f;

        [Tooltip("Distancia máxima del rolloff del motor (metros).")]
        public float EngineMaxDistance = 50f;

        [Tooltip("Distancia mínima del rolloff de las alertas (frenada y bocina) (metros).")]
        public float AlertMinDistance = 5f;

        [Tooltip("Distancia máxima del rolloff de las alertas (metros).")]
        public float AlertMaxDistance = 80f;

        [Tooltip("Distancia máxima del rolloff de la sirena (metros).")]
        public float SirenMaxDistance = 120f;

        protected override void CollectMissingPlaceholders(List<string> missing)
        {
            if (EngineIdleLoop == null) missing.Add(nameof(EngineIdleLoop));
            if (EngineMidLoop == null) missing.Add(nameof(EngineMidLoop));
            if (SoftBrake == null) missing.Add(nameof(SoftBrake));
            if (TireScreech == null) missing.Add(nameof(TireScreech));
            if (HornShort == null) missing.Add(nameof(HornShort));
            if (HornLong == null) missing.Add(nameof(HornLong));
            if (SirenLoop == null) missing.Add(nameof(SirenLoop));
        }

        protected override void CollectPendingOrganic(List<string> pending)
        {
        }

        private void OnValidate()
        {
            ReferenceSpeed = Mathf.Max(0.1f, ReferenceSpeed);
            MaxPitch = Mathf.Max(MinPitch, MaxPitch);
            EngineMinDistance = Mathf.Max(0.01f, EngineMinDistance);
            EngineMaxDistance = Mathf.Max(EngineMinDistance, EngineMaxDistance);
            AlertMinDistance = Mathf.Max(0.01f, AlertMinDistance);
            AlertMaxDistance = Mathf.Max(AlertMinDistance, AlertMaxDistance);
            SirenMaxDistance = Mathf.Max(AlertMinDistance, SirenMaxDistance);
            DuckHoldSeconds = Mathf.Max(0f, DuckHoldSeconds);
        }
    }
}
