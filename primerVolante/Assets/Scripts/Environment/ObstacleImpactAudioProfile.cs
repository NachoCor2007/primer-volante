using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Clip de impacto y su volumen base (0..1).
    /// </summary>
    [Serializable]
    public class ObstacleImpactSound
    {
        public AudioClip Clip;

        [Range(0f, 1f)] public float Volume = 0.8f;
    }

    /// <summary>
    /// Perfil de audio de los impactos contra obstáculos (US33): un clip y un volumen por combinación de tipo y
    /// gravedad, y los parámetros 3D de la fuente. Es solo datos de configuración compartidos (ScriptableObject);
    /// los clips placeholder se reemplazan por clips reales sin tocar código.
    /// </summary>
    [CreateAssetMenu(fileName = "ObstacleImpactAudioProfile", menuName = "Primer Volante/Obstacle Impact Audio Profile", order = 1)]
    public class ObstacleImpactAudioProfile : ScriptableObject
    {
        [Header("Cono")]
        public ObstacleImpactSound ConeGraze = new ObstacleImpactSound { Volume = 0.55f };
        public ObstacleImpactSound ConeCrash = new ObstacleImpactSound { Volume = 0.85f };

        [Header("Vehículo")]
        public ObstacleImpactSound VehicleGraze = new ObstacleImpactSound { Volume = 0.6f };
        public ObstacleImpactSound VehicleCrash = new ObstacleImpactSound { Volume = 1f };

        [Header("Peatón")]
        public ObstacleImpactSound PedestrianCrash = new ObstacleImpactSound { Volume = 0.9f };

        [Header("Volumen según la velocidad")]
        [Tooltip("Factor de volumen (0..1) a 0 km/h: un roce a 1 km/h suena más bajo que uno a 4 km/h.")]
        [Range(0f, 1f)] public float SpeedVolumeFloor = 0.5f;

        [Tooltip("Velocidad del jugador en km/h a partir de la cual el volumen es el pleno del clip.")]
        public float SpeedVolumeFullKmh = 10f;

        [Header("Audio 3D")]
        [Tooltip("Distancia mínima del rolloff del impacto (metros).")]
        public float MinDistance = 1f;

        [Tooltip("Distancia máxima del rolloff del impacto (metros).")]
        public float MaxDistance = 30f;

        /// <summary>
        /// Clip y volumen base para un tipo y una gravedad. Si falta el de <see cref="ObstacleImpactSeverity.Graze"/>
        /// cae al de <see cref="ObstacleImpactSeverity.Crash"/> (y con su volumen). Un peatón solo tiene choque.
        /// Devuelve <c>null</c> (y volumen 0) si no hay clip asignado.
        /// </summary>
        public AudioClip GetClip(ObstacleKind kind, ObstacleImpactSeverity severity, out float volume)
        {
            ObstacleImpactSound crash;
            ObstacleImpactSound graze;
            switch (kind)
            {
                case ObstacleKind.Cone:
                    crash = ConeCrash;
                    graze = ConeGraze;
                    break;
                case ObstacleKind.Vehicle:
                    crash = VehicleCrash;
                    graze = VehicleGraze;
                    break;
                default:
                    crash = PedestrianCrash;
                    graze = null;
                    break;
            }

            ObstacleImpactSound chosen = severity == ObstacleImpactSeverity.Graze && graze != null && graze.Clip != null
                ? graze
                : crash;

            if (chosen == null || chosen.Clip == null)
            {
                volume = 0f;
                return null;
            }

            volume = chosen.Volume;
            return chosen.Clip;
        }

        /// <summary>
        /// Escala <paramref name="baseVolume"/> según la velocidad del jugador: de <see cref="SpeedVolumeFloor"/> a
        /// 1 entre 0 y <see cref="SpeedVolumeFullKmh"/> km/h.
        /// </summary>
        public float ScaleVolumeBySpeed(float baseVolume, float speedKmh)
        {
            float t = SpeedVolumeFullKmh > 0f ? Mathf.Clamp01(speedKmh / SpeedVolumeFullKmh) : 1f;
            return Mathf.Clamp01(baseVolume * Mathf.Lerp(SpeedVolumeFloor, 1f, t));
        }

        /// <summary>
        /// Devuelve la lista de nombres de clips no asignados (para verificación).
        /// </summary>
        public string[] GetMissingClips()
        {
            var missing = new List<string>();
            void Check(ObstacleImpactSound sound, string name)
            {
                if (sound == null || sound.Clip == null) missing.Add(name);
            }

            Check(ConeGraze, nameof(ConeGraze));
            Check(ConeCrash, nameof(ConeCrash));
            Check(VehicleGraze, nameof(VehicleGraze));
            Check(VehicleCrash, nameof(VehicleCrash));
            Check(PedestrianCrash, nameof(PedestrianCrash));
            return missing.ToArray();
        }

        private void OnValidate()
        {
            SpeedVolumeFullKmh = Mathf.Max(0f, SpeedVolumeFullKmh);
            MinDistance = Mathf.Max(0.01f, MinDistance);
            MaxDistance = Mathf.Max(MinDistance, MaxDistance);
        }
    }
}
