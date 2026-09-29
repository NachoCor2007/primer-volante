using System;
using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Parámetros del RPM simulado (ver <see cref="VehicleEngineSimulator"/>). Es solo data de
    /// configuración: los valores por defecto están pensados para un sedán de 4 cilindros con
    /// caja automática de 4 marchas virtuales y velocidad máxima de 60 km/h.
    /// </summary>
    [Serializable]
    public class EngineSimulationSettings
    {
        [Header("Rango de RPM")]
        [Tooltip("RPM del ralentí.")]
        public float IdleRpm = 800f;

        [Tooltip("RPM máximas absolutas (línea roja).")]
        public float MaxRpm = 5000f;

        [Tooltip("RPM promedio durante el burro de arranque (Cranking).")]
        public float CrankRpm = 250f;

        [Header("Punto muerto / Park")]
        [Tooltip("RPM que alcanza el motor en P/N con el acelerador a fondo (revoluciones libres).")]
        public float FreeRevMaxRpm = 4500f;

        [Header("Caja automática virtual")]
        [Tooltip("Velocidad máxima del vehículo en km/h (debe coincidir con el VehicleController). Se reparte entre las marchas.")]
        public float MaxSpeedKmh = 60f;

        [Tooltip("Cantidad de marchas virtuales hacia adelante.")]
        [Range(2, 6)] public int ForwardGears = 4;

        [Tooltip("RPM (sin carga) que se alcanzan al final de cada marcha, justo antes de subir la siguiente.")]
        public float UpshiftRpm = 4000f;

        [Tooltip("Histéresis en km/h: se baja de marcha solo si la velocidad cae este margen por debajo del límite de la marcha inferior.")]
        public float DownshiftHysteresisKmh = 2f;

        [Tooltip("RPM extra por carga del acelerador (patinamiento del convertidor de par).")]
        public float ThrottleLoadRpmBonus = 500f;

        [Tooltip("Fracción de la velocidad máxima que cubre la marcha atrás (una sola marcha).")]
        [Range(0.1f, 1f)] public float ReverseSpeedFraction = 0.5f;

        [Header("Freno de mano")]
        [Tooltip("Accionamiento del freno de mano (0..1) a partir del cual el auto se considera trabado.")]
        [Range(0f, 1f)] public float HandbrakeHoldThreshold = 0.5f;

        [Tooltip("RPM máximas al acelerar en D/R contra el freno de mano.")]
        public float HandbrakeStallMaxRpm = 2000f;

        [Header("Suavizado (RPM por segundo)")]
        [Tooltip("Velocidad de subida de RPM.")]
        public float RiseRate = 4500f;

        [Tooltip("Velocidad de caída de RPM al soltar el acelerador (freno motor: más lenta que la subida).")]
        public float FallRate = 1200f;

        [Tooltip("Velocidad de caída durante un cambio de marcha hacia arriba.")]
        public float ShiftFallRate = 9000f;

        [Tooltip("Duración en segundos de la caída rápida tras subir de marcha.")]
        public float ShiftFallDuration = 0.25f;

        [Tooltip("Velocidad de caída al apagar el motor.")]
        public float ShutdownFallRate = 2500f;

        [Header("Carga")]
        [Tooltip("Suavizado de la carga del motor (1/s).")]
        public float LoadSmoothing = 8f;
    }

    /// <summary>
    /// Perfil de audio del vehículo: referencias a todos los clips, volúmenes, capas del motor,
    /// curvas del viento/rodadura y parámetros 3D. Es solo datos de configuración compartidos
    /// (ScriptableObject); no guarda estado de runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "VehicleAudioProfile", menuName = "Primer Volante/Vehicle Audio Profile", order = 0)]
    public class VehicleAudioProfile : ScriptableObject
    {
        [Header("Motor - Clips")]
        public AudioClip EngineStart;
        public AudioClip EngineStop;
        public AudioClip EngineStartFail;
        [Tooltip("Loop de ralentí (referencia ~800 RPM).")]
        public AudioClip EngineIdleLoop;
        [Tooltip("Loop de régimen medio (referencia ~2500 RPM).")]
        public AudioClip EngineMidLoop;
        [Tooltip("Loop de régimen alto (referencia ~4500 RPM).")]
        public AudioClip EngineHighLoop;

        [Header("Guiños - Clips")]
        public AudioClip BlinkerTick;
        public AudioClip BlinkerTock;

        [Header("Freno de mano - Clips")]
        public AudioClip HandbrakeRatchetClick;
        public AudioClip HandbrakePullFull;
        public AudioClip HandbrakeRelease;

        [Header("Controles de cabina - Clips")]
        public AudioClip ButtonClick;
        public AudioClip StalkClick;
        public AudioClip KnobClick;
        public AudioClip GearClunk;

        [Header("Ambiente - Clips")]
        public AudioClip WindRoadLoop;

        [Header("Volúmenes (0..1)")]
        [Range(0f, 1f)] public float EngineStartVolume = 0.9f;
        [Range(0f, 1f)] public float EngineStopVolume = 0.8f;
        [Range(0f, 1f)] public float EngineStartFailVolume = 0.8f;
        [Range(0f, 1f)] public float EngineLoopVolume = 0.7f;
        [Range(0f, 1f)] public float BlinkerVolume = 0.6f;
        [Range(0f, 1f)] public float HandbrakeRatchetVolume = 0.6f;
        [Range(0f, 1f)] public float HandbrakePullFullVolume = 0.7f;
        [Range(0f, 1f)] public float HandbrakeReleaseVolume = 0.8f;
        [Range(0f, 1f)] public float ButtonClickVolume = 0.7f;
        [Range(0f, 1f)] public float StalkClickVolume = 0.7f;
        [Range(0f, 1f)] public float KnobClickVolume = 0.7f;
        [Range(0f, 1f)] public float GearClunkVolume = 0.8f;
        [Range(0f, 1f)] public float WindRoadMaxVolume = 0.5f;

        [Header("Motor - Capas y crossfade")]
        [Tooltip("RPM de referencia de la capa de ralentí (pitch = 1 a estas RPM).")]
        public float IdleLayerRpm = 800f;
        [Tooltip("RPM de referencia de la capa media.")]
        public float MidLayerRpm = 2500f;
        [Tooltip("RPM de referencia de la capa alta.")]
        public float HighLayerRpm = 4500f;

        [Tooltip("Crossfade ralentí → medio: (x = RPM donde empieza, y = RPM donde termina).")]
        public Vector2 IdleToMidCrossfade = new Vector2(1000f, 2000f);
        [Tooltip("Crossfade medio → alto: (x = RPM donde empieza, y = RPM donde termina).")]
        public Vector2 MidToHighCrossfade = new Vector2(3000f, 4000f);

        [Tooltip("Pitch mínimo y máximo permitidos por capa.")]
        public Vector2 PitchRange = new Vector2(0.5f, 2f);

        [Tooltip("Volumen global del motor sin carga (retención), relativo a carga plena = 1.")]
        [Range(0f, 1f)] public float EngineNoLoadVolumeFactor = 0.65f;

        [Tooltip("Duración en segundos del fade-in del ralentí al pasar a Running.")]
        public float EngineFadeInSeconds = 0.25f;

        [Tooltip("Duración en segundos del fade-out de los loops al apagar el motor.")]
        public float EngineFadeOutSeconds = 0.4f;

        [Header("RPM simulado")]
        public EngineSimulationSettings EngineSimulation = new EngineSimulationSettings();

        [Header("Viento / Rodadura")]
        [Tooltip("Volumen (0..1) según la velocidad en km/h (eje X de la curva = km/h).")]
        public AnimationCurve RoadVolumeBySpeed = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(3f, 0.08f), new Keyframe(20f, 0.45f), new Keyframe(60f, 1f));

        [Tooltip("Pitch según la velocidad en km/h.")]
        public AnimationCurve RoadPitchBySpeed = new AnimationCurve(
            new Keyframe(0f, 0.8f), new Keyframe(60f, 1.3f));

        [Tooltip("Por debajo de este volumen relativo la fuente de viento se pausa.")]
        public float RoadSilenceThreshold = 0.005f;

        [Header("Freno de mano - Trinquete")]
        [Tooltip("Paso de Engagement (0..1) entre clics de trinquete.")]
        [Range(0.02f, 0.5f)] public float RatchetStep = 0.125f;

        [Tooltip("Rango de pitch aleatorio de cada clic de trinquete.")]
        public Vector2 RatchetPitchRange = new Vector2(0.95f, 1.05f);

        [Tooltip("Intervalo mínimo en segundos entre clics de trinquete.")]
        public float RatchetMinInterval = 0.025f;

        [Tooltip("Un único salto de Engagement hacia arriba mayor a este valor reproduce el tirón completo.")]
        [Range(0.1f, 1f)] public float PullFullThreshold = 0.5f;

        [Header("Cabina")]
        [Tooltip("Debounce en segundos del clic de la palanca de guiño.")]
        public float StalkClickDebounce = 0.08f;

        [Header("Audio 3D")]
        [Tooltip("Distancia mínima del rolloff en la cabina (metros).")]
        public float CabinMinDistance = 0.3f;

        [Tooltip("Distancia máxima del rolloff en la cabina (metros).")]
        public float CabinMaxDistance = 5f;

        [Tooltip("Distancia mínima del rolloff para el motor (metros).")]
        public float EngineMinDistance = 1f;

        [Tooltip("Distancia máxima del rolloff para el motor (metros).")]
        public float EngineMaxDistance = 12f;

        [Tooltip("Distancia mínima del rolloff para el viento/rodadura (metros).")]
        public float RoadMinDistance = 1f;

        [Tooltip("Distancia máxima del rolloff para el viento/rodadura (metros).")]
        public float RoadMaxDistance = 10f;

        /// <summary>
        /// Devuelve la lista de nombres de clips no asignados (para verificación).
        /// </summary>
        public string[] GetMissingClips()
        {
            var missing = new System.Collections.Generic.List<string>();
            void Check(AudioClip c, string n) { if (c == null) missing.Add(n); }
            Check(EngineStart, nameof(EngineStart));
            Check(EngineStop, nameof(EngineStop));
            Check(EngineStartFail, nameof(EngineStartFail));
            Check(EngineIdleLoop, nameof(EngineIdleLoop));
            Check(EngineMidLoop, nameof(EngineMidLoop));
            Check(EngineHighLoop, nameof(EngineHighLoop));
            Check(BlinkerTick, nameof(BlinkerTick));
            Check(BlinkerTock, nameof(BlinkerTock));
            Check(HandbrakeRatchetClick, nameof(HandbrakeRatchetClick));
            Check(HandbrakePullFull, nameof(HandbrakePullFull));
            Check(HandbrakeRelease, nameof(HandbrakeRelease));
            Check(ButtonClick, nameof(ButtonClick));
            Check(StalkClick, nameof(StalkClick));
            Check(KnobClick, nameof(KnobClick));
            Check(GearClunk, nameof(GearClunk));
            Check(WindRoadLoop, nameof(WindRoadLoop));
            return missing.ToArray();
        }
    }
}
