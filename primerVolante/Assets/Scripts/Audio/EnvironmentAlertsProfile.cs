using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Audio
{
    /// <summary>
    /// Perfil de las alertas fijas del entorno (US20): semáforo sonoro, alarma de auto estacionado y bocina de
    /// impaciencia detrás del jugador. Todos los clips son placeholders procedurales.
    /// </summary>
    [CreateAssetMenu(fileName = "EnvironmentAlerts_New", menuName = "Primer Volante/Audio/Environment Alerts Profile", order = 14)]
    public class EnvironmentAlertsProfile : EnvironmentAudioProfileBase
    {
        [Header("Semáforo sonoro")]
        [Tooltip("Tick lento localizador (semáforo en verde o amarillo).")]
        public AudioClip CrosswalkTick;

        [Tooltip("Beep rápido (semáforo vehicular en rojo: los peatones cruzan).")]
        public AudioClip CrosswalkBeep;

        [Tooltip("Segundos entre ticks.")]
        public float TickInterval = 1f;

        [Tooltip("Segundos entre beeps.")]
        public float BeepInterval = 0.25f;

        [Range(0f, 1f)] public float CrosswalkVolume = 0.6f;

        [Tooltip("Distancia mínima del rolloff del semáforo (metros).")]
        public float CrosswalkMinDistance = 3f;

        [Tooltip("Distancia máxima del rolloff del semáforo (metros).")]
        public float CrosswalkMaxDistance = 25f;

        [Header("Alarma de auto estacionado")]
        [Tooltip("Loop de la alarma al chocar un auto decorativo.")]
        public AudioClip CarAlarmLoop;

        [Tooltip("Segundos que suena la alarma.")]
        public float AlarmSeconds = 8f;

        [Range(0f, 1f)] public float AlarmVolume = 0.8f;

        [Tooltip("Distancia mínima del rolloff de la alarma (metros).")]
        public float AlarmMinDistance = 5f;

        [Tooltip("Distancia máxima del rolloff de la alarma (metros).")]
        public float AlarmMaxDistance = 60f;

        [Tooltip("Cuánto baja el ambiente (dB) mientras suena la alarma.")]
        public float AlarmDuckDb = 5f;

        [Header("Bocina de impaciencia")]
        [Tooltip("Bocina corta.")]
        public AudioClip HornShort;

        [Tooltip("Bocina larga.")]
        public AudioClip HornLong;

        [Range(0f, 1f)] public float HornVolume = 0.9f;

        [Tooltip("Distancia detrás del auto del jugador desde la que suena la bocina (metros).")]
        public float HornBehindDistance = 8f;

        [Tooltip("Distancia mínima del rolloff de la bocina (metros).")]
        public float HornMinDistance = 5f;

        [Tooltip("Distancia máxima del rolloff de la bocina (metros).")]
        public float HornMaxDistance = 60f;

        [Tooltip("Cuánto baja el ambiente (dB) con la bocina.")]
        public float HornDuckDb = 6f;

        [Tooltip("Segundos que se mantiene bajo el ambiente.")]
        public float HornDuckHoldSeconds = 1.2f;

        protected override void CollectMissingPlaceholders(List<string> missing)
        {
            if (CrosswalkTick == null) missing.Add(nameof(CrosswalkTick));
            if (CrosswalkBeep == null) missing.Add(nameof(CrosswalkBeep));
            if (CarAlarmLoop == null) missing.Add(nameof(CarAlarmLoop));
            if (HornShort == null) missing.Add(nameof(HornShort));
            if (HornLong == null) missing.Add(nameof(HornLong));
        }

        protected override void CollectPendingOrganic(List<string> pending)
        {
        }

        private void OnValidate()
        {
            TickInterval = Mathf.Max(0.05f, TickInterval);
            BeepInterval = Mathf.Max(0.05f, BeepInterval);
            AlarmSeconds = Mathf.Max(0.5f, AlarmSeconds);
            CrosswalkMinDistance = Mathf.Max(0.01f, CrosswalkMinDistance);
            CrosswalkMaxDistance = Mathf.Max(CrosswalkMinDistance, CrosswalkMaxDistance);
            AlarmMinDistance = Mathf.Max(0.01f, AlarmMinDistance);
            AlarmMaxDistance = Mathf.Max(AlarmMinDistance, AlarmMaxDistance);
            HornMinDistance = Mathf.Max(0.01f, HornMinDistance);
            HornMaxDistance = Mathf.Max(HornMinDistance, HornMaxDistance);
            HornDuckHoldSeconds = Mathf.Max(0f, HornDuckHoldSeconds);
        }
    }
}
