using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor que agrega el sistema de sonido del vehículo sin tocar Car07 ni
    /// CockpitIntegrationScene: genera placeholders, crea el VehicleAudioProfile, deriva
    /// Car08.prefab de Car07 y SoundIntegrationScene de CockpitIntegrationScene.
    /// Es idempotente: se puede re-ejecutar sin duplicar nada.
    /// </summary>
    public static class SoundIntegrationSetup
    {
        private const string CAR_FOLDER = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02";
        public const string PREFAB_CAR08_PATH = CAR_FOLDER + "/Prefabs/Car08.prefab";
        public const string SCENE_SOUND_PATH = CAR_FOLDER + "/Scene/SoundIntegrationScene.unity";
        public const string PROFILE_PATH = VehicleAudioPlaceholderGenerator.AUDIO_FOLDER + "/VehicleAudioProfile_Car08.asset";
        public const string MIXER_PATH = VehicleAudioPlaceholderGenerator.AUDIO_FOLDER + "/VehicleAudioMixer.mixer";

        /// <summary>Duración del arranque (s) fijada en Car08, ≈ duración útil de engine_start.wav.</summary>
        public const float CRANK_DURATION = 1.0f;

        [MenuItem("Tools/Primer Volante/Integrate Vehicle Sound (Car08 Prefab and Scene)")]
        public static void IntegrateAll()
        {
            VehicleAudioPlaceholderGenerator.GeneratePlaceholders();
            CreateProfile();
            SetupCar08Prefab();
            SetupSoundIntegrationScene();
        }

        // ------------------------------------------------------------------ Profile

        [MenuItem("Tools/Primer Volante/Create Vehicle Audio Profile (Car08)")]
        public static VehicleAudioProfile CreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>(PROFILE_PATH);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VehicleAudioProfile>();
                AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            }

            // Solo completa clips vacíos: no pisa asignaciones manuales.
            profile.EngineStart = Fill(profile.EngineStart, "engine_start");
            profile.EngineStop = Fill(profile.EngineStop, "engine_stop");
            profile.EngineStartFail = Fill(profile.EngineStartFail, "engine_start_fail");
            profile.EngineIdleLoop = Fill(profile.EngineIdleLoop, "engine_idle_loop");
            profile.EngineMidLoop = Fill(profile.EngineMidLoop, "engine_mid_loop");
            profile.EngineHighLoop = Fill(profile.EngineHighLoop, "engine_high_loop");
            profile.BlinkerTick = Fill(profile.BlinkerTick, "blinker_tick");
            profile.BlinkerTock = Fill(profile.BlinkerTock, "blinker_tock");
            profile.HandbrakeRatchetClick = Fill(profile.HandbrakeRatchetClick, "handbrake_ratchet_click");
            profile.HandbrakePullFull = Fill(profile.HandbrakePullFull, "handbrake_pull_full");
            profile.HandbrakeRelease = Fill(profile.HandbrakeRelease, "handbrake_release");
            profile.ButtonClick = Fill(profile.ButtonClick, "button_click");
            profile.StalkClick = Fill(profile.StalkClick, "stalk_click");
            profile.KnobClick = Fill(profile.KnobClick, "knob_click");
            profile.GearClunk = Fill(profile.GearClunk, "gear_clunk");
            profile.WindRoadLoop = Fill(profile.WindRoadLoop, "wind_road_loop");

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SoundIntegrationSetup] 🎚️ VehicleAudioProfile listo en {PROFILE_PATH}");
            return profile;
        }

        private static AudioClip Fill(AudioClip current, string name)
        {
            if (current != null) return current;
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{VehicleAudioPlaceholderGenerator.AUDIO_FOLDER}/{name}.wav");
        }

        // ------------------------------------------------------------------ Car08

        [MenuItem("Tools/Primer Volante/Setup Car08 Prefab")]
        public static void SetupCar08Prefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_CAR08_PATH) == null)
            {
                // CopyAsset conserva los fileIDs internos: las instancias de escena siguen resolviendo overrides.
                if (!AssetDatabase.CopyAsset(CockpitIntegrationSetup.PREFAB_CAR07_PATH, PREFAB_CAR08_PATH))
                {
                    Debug.LogError($"[SoundIntegrationSetup] ❌ No se pudo copiar Car07 a {PREFAB_CAR08_PATH}");
                    return;
                }
            }

            var profile = CreateProfile();
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_CAR08_PATH);
            try
            {
                ConfigureCar08(root, profile);
                root.name = "Car08";
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_CAR08_PATH, out bool ok);
                if (ok) Debug.Log($"[SoundIntegrationSetup] ✅ {PREFAB_CAR08_PATH} configurado con audio.");
                else Debug.LogError("[SoundIntegrationSetup] ❌ Falló el guardado de Car08.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Agrega simulador de RPM, componentes de audio, AudioSources y cableado. No vuelve a
        /// correr ConfigureCar07.
        /// </summary>
        public static void ConfigureCar08(GameObject root, VehicleAudioProfile profile)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MIXER_PATH);
            if (mixer == null)
                Debug.LogWarning($"[SoundIntegrationSetup] ⚠️ No existe el mixer en {MIXER_PATH}: las fuentes quedan sin grupo.");

            AudioMixerGroup engineGroup = FindGroup(mixer, "Engine");
            AudioMixerGroup cabinGroup = FindGroup(mixer, "Cabin");
            AudioMixerGroup roadGroup = FindGroup(mixer, "Road");

            var vehicle = root.GetComponent<VehicleController>();
            SetFloat(vehicle, "m_CrankDuration", CRANK_DURATION);

            // Simulador de RPM en la raíz.
            var simulator = GetOrAdd<VehicleEngineSimulator>(root);
            SetObj(simulator, "m_VehicleController", vehicle);
            SetObj(simulator, "m_Profile", profile);

            // Contenedor de los componentes de audio.
            Transform audioRoot = EnsureChild(root.transform, "VehicleAudio", Vector3.zero);
            float cabinMin = profile.CabinMinDistance, cabinMax = profile.CabinMaxDistance;

            // --- Motor (vano motor, adelante bajo el capó)
            Transform engine = EnsureChild(root.transform, "Audio_Engine", new Vector3(0.07f, 0.6f, 1.5f));
            AudioSource engineOneShot = EnsureSource(engine, "Engine_OneShot", Vector3.zero, false, profile.EngineMinDistance, profile.EngineMaxDistance, engineGroup, 0);
            AudioSource idle = EnsureSource(engine, "Engine_Idle", Vector3.zero, true, profile.EngineMinDistance, profile.EngineMaxDistance, engineGroup, 0);
            AudioSource mid = EnsureSource(engine, "Engine_Mid", Vector3.zero, true, profile.EngineMinDistance, profile.EngineMaxDistance, engineGroup, 0);
            AudioSource high = EnsureSource(engine, "Engine_High", Vector3.zero, true, profile.EngineMinDistance, profile.EngineMaxDistance, engineGroup, 0);
            idle.clip = profile.EngineIdleLoop;
            mid.clip = profile.EngineMidLoop;
            high.clip = profile.EngineHighLoop;

            var engineAudio = GetOrAdd<VehicleEngineAudio>(audioRoot.gameObject);
            SetObj(engineAudio, "m_VehicleController", vehicle);
            SetObj(engineAudio, "m_Simulator", simulator);
            SetObj(engineAudio, "m_Profile", profile);
            SetObj(engineAudio, "m_OneShotSource", engineOneShot);
            SetObj(engineAudio, "m_IdleSource", idle);
            SetObj(engineAudio, "m_MidSource", mid);
            SetObj(engineAudio, "m_HighSource", high);
            SetObj(engineAudio, "m_MixerGroup", engineGroup);

            // --- Guiños: a cada lado del conductor, a la altura del tablero.
            // En espacio local del auto (+Z adelante, +Y arriba) la izquierda es -X.
            Transform driverSeat = FindDeep(root.transform, "DriverSeat");
            float driverX = driverSeat != null ? driverSeat.localPosition.x : -0.3f;
            var dashPos = new Vector3(driverX, 0.97f, 0.29f);
            Transform blinkerLeft = EnsureChild(root.transform, "Audio_Blinker_Left", dashPos + new Vector3(-0.4f, 0f, 0f));
            Transform blinkerRight = EnsureChild(root.transform, "Audio_Blinker_Right", dashPos + new Vector3(0.4f, 0f, 0f));
            AudioSource blinkL = EnsureSource(blinkerLeft, "Source", Vector3.zero, false, cabinMin, cabinMax, cabinGroup);
            AudioSource blinkR = EnsureSource(blinkerRight, "Source", Vector3.zero, false, cabinMin, cabinMax, cabinGroup);

            var blinkClock = root.GetComponent<TurnSignalLightController>();
            var turnAudio = GetOrAdd<VehicleTurnSignalAudio>(audioRoot.gameObject);
            SetObj(turnAudio, "m_BlinkClock", blinkClock);
            SetObj(turnAudio, "m_Profile", profile);
            SetObj(turnAudio, "m_LeftSource", blinkL);
            SetObj(turnAudio, "m_RightSource", blinkR);
            SetObj(turnAudio, "m_MixerGroup", cabinGroup);

            // --- Freno de mano
            var handbrake = root.GetComponentInChildren<VRHandbrake>(true);
            AudioSource handbrakeSource = null;
            if (handbrake != null)
                handbrakeSource = EnsureSource(handbrake.transform, "Audio_Handbrake", Vector3.zero, false, cabinMin, cabinMax, cabinGroup);

            var handbrakeAudio = GetOrAdd<VehicleHandbrakeAudio>(audioRoot.gameObject);
            SetObj(handbrakeAudio, "m_Handbrake", handbrake);
            SetObj(handbrakeAudio, "m_Profile", profile);
            SetObj(handbrakeAudio, "m_Source", handbrakeSource);
            SetObj(handbrakeAudio, "m_MixerGroup", cabinGroup);

            // --- Controles de cabina
            var startButton = root.GetComponentInChildren<StartButton>(true);
            var turnSignal = root.GetComponentInChildren<VRTurnSignal>(true);
            var gearShifter = root.GetComponentInChildren<VRGearShifter>(true);
            Transform hazard = FindDeep(root.transform, "HazardButton");

            var cabinAudio = GetOrAdd<VehicleCabinControlsAudio>(audioRoot.gameObject);
            SetObj(cabinAudio, "m_VehicleController", vehicle);
            SetObj(cabinAudio, "m_TurnSignal", turnSignal);
            SetObj(cabinAudio, "m_GearShifter", gearShifter);
            SetObj(cabinAudio, "m_Profile", profile);
            if (startButton != null)
                SetObj(cabinAudio, "m_StartButtonSource", EnsureSource(startButton.transform, "Audio_StartButton", Vector3.zero, false, cabinMin, cabinMax, cabinGroup));
            if (hazard != null)
                SetObj(cabinAudio, "m_HazardButtonSource", EnsureSource(hazard, "Audio_HazardButton", Vector3.zero, false, cabinMin, cabinMax, cabinGroup));
            if (turnSignal != null)
                SetObj(cabinAudio, "m_TurnSignalSource", EnsureSource(turnSignal.transform, "Audio_TurnSignal", Vector3.zero, false, cabinMin, cabinMax, cabinGroup));
            if (gearShifter != null)
                SetObj(cabinAudio, "m_GearShifterSource", EnsureSource(gearShifter.transform, "Audio_GearShifter", Vector3.zero, false, cabinMin, cabinMax, cabinGroup));
            SetObj(cabinAudio, "m_MixerGroup", cabinGroup);

            // --- Perilla de luces: reutiliza los campos existentes de VRHeadlightKnob.
            var knob = root.GetComponentInChildren<VRHeadlightKnob>(true);
            if (knob != null)
            {
                AudioSource knobSource = EnsureSource(knob.transform, "Audio_Knob", Vector3.zero, false, cabinMin, cabinMax, cabinGroup);
                SetObj(knob, "m_AudioSource", knobSource);
                SetObj(knob, "m_ClickClip", profile.KnobClick);
            }

            // --- Viento / rodadura bajo el piso, al centro del auto.
            Transform road = EnsureChild(root.transform, "Audio_RoadNoise", new Vector3(0.07f, 0.1f, 0f));
            AudioSource roadSource = EnsureSource(road, "Source", Vector3.zero, true, profile.RoadMinDistance, profile.RoadMaxDistance, roadGroup);
            roadSource.clip = profile.WindRoadLoop;

            var roadAudio = GetOrAdd<VehicleRoadNoiseAudio>(audioRoot.gameObject);
            SetObj(roadAudio, "m_VehicleController", vehicle);
            SetObj(roadAudio, "m_Profile", profile);
            SetObj(roadAudio, "m_Source", roadSource);
            SetObj(roadAudio, "m_MixerGroup", roadGroup);
        }

        // ------------------------------------------------------------------ Escena

        [MenuItem("Tools/Primer Volante/Setup Sound Integration Scene")]
        public static void SetupSoundIntegrationScene()
        {
            var car08 = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_CAR08_PATH);
            if (car08 == null)
            {
                Debug.LogError("[SoundIntegrationSetup] ❌ Car08.prefab no existe: ejecutar primero Setup Car08 Prefab.");
                return;
            }

            if (!File.Exists(SCENE_SOUND_PATH))
            {
                if (!AssetDatabase.CopyAsset(CockpitIntegrationSetup.SCENE_INTEGRATION_PATH, SCENE_SOUND_PATH))
                {
                    Debug.LogError($"[SoundIntegrationSetup] ❌ No se pudo copiar la escena a {SCENE_SOUND_PATH}");
                    return;
                }
            }

            Scene scene = EditorSceneManager.OpenScene(SCENE_SOUND_PATH, OpenSceneMode.Single);

            int replaced = 0;
            var controllers = Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include);
            foreach (var controller in controllers)
            {
                GameObject instanceRoot = controller.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(instanceRoot)) continue;

                string sourcePath = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot));
                if (sourcePath != CockpitIntegrationSetup.PREFAB_CAR07_PATH) continue;

                var settings = new PrefabReplacingSettings
                {
                    objectMatchMode = ObjectMatchMode.ByHierarchy,
                    prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
                    changeRootNameToAssetName = false,
                    logInfo = true
                };
                PrefabUtility.ReplacePrefabAssetOfPrefabInstance(instanceRoot, car08, settings, InteractionMode.AutomatedAction);
                instanceRoot.name = "Car08";
                replaced++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SoundIntegrationSetup] ✅ Escena {SCENE_SOUND_PATH}: {replaced} instancia(s) de Car07 reemplazadas por Car08.");
        }

        // ------------------------------------------------------------------ Helpers

        private static AudioMixerGroup FindGroup(AudioMixer mixer, string name)
        {
            if (mixer == null) return null;
            foreach (var g in mixer.FindMatchingGroups(name))
            {
                if (g.name == name) return g;
            }
            Debug.LogWarning($"[SoundIntegrationSetup] ⚠️ El mixer no tiene el grupo '{name}'.");
            return null;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Transform EnsureChild(Transform parent, string name, Vector3 localPosition)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                t = go.transform;
                t.SetParent(parent, false);
            }
            t.localPosition = localPosition;
            t.localRotation = Quaternion.identity;
            return t;
        }

        private static AudioSource EnsureSource(
            Transform parent, string name, Vector3 localPosition, bool loop,
            float minDistance, float maxDistance, AudioMixerGroup group, int priority = 128)
        {
            Transform t = EnsureChild(parent, name, localPosition);
            var src = GetOrAdd<AudioSource>(t.gameObject);
            VehicleAudioUtil.ConfigureSource(src, loop, minDistance, maxDistance, group, priority);
            return src;
        }

        private static void SetObj(Object target, string property, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning($"[SoundIntegrationSetup] ⚠️ Propiedad '{property}' no encontrada en {target.GetType().Name}.");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string property, float value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning($"[SoundIntegrationSetup] ⚠️ Propiedad '{property}' no encontrada en {target.GetType().Name}.");
                return;
            }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
