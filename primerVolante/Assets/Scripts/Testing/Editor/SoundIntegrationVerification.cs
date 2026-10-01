using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor del sistema de sonido del vehículo (Car08 + SoundIntegrationScene).
    /// </summary>
    public static class SoundIntegrationVerification
    {
        [MenuItem("Tools/Primer Volante/Run Sound Integration Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL SISTEMA DE SONIDO (Car08)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyProfile(ref total, ref passed);
            VerifyCar08Prefab(ref total, ref passed);
            VerifyCar07Untouched(ref total, ref passed);
            VerifyRpmMath(ref total, ref passed);
            VerifyAudioMath(ref total, ref passed);
            VerifyScene(ref total, ref passed);
            VerifyExistingSuites(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Profile

        private static void VerifyProfile(ref int total, ref int passed)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>(SoundIntegrationSetup.PROFILE_PATH);
            Assert(profile != null, $"VehicleAudioProfile debe existir en {SoundIntegrationSetup.PROFILE_PATH}", ref total, ref passed);
            if (profile == null) return;

            string[] missing = profile.GetMissingClips();
            Assert(missing.Length == 0, $"Todos los clips del profile deben estar asignados (faltan: {string.Join(", ", missing)})", ref total, ref passed);
            Assert(Mathf.Approximately(profile.IdleLayerRpm, 800f) && Mathf.Approximately(profile.MidLayerRpm, 2500f)
                   && Mathf.Approximately(profile.HighLayerRpm, 4500f), "RPM de referencia de las capas deben ser 800/2500/4500", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Car08

        private static void VerifyCar08Prefab(ref int total, ref int passed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SoundIntegrationSetup.PREFAB_CAR08_PATH);
            Assert(prefab != null, $"Car08.prefab debe existir en {SoundIntegrationSetup.PREFAB_CAR08_PATH}", ref total, ref passed);
            if (prefab == null) return;

            var profile = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>(SoundIntegrationSetup.PROFILE_PATH);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(SoundIntegrationSetup.MIXER_PATH);

            GameObject root = PrefabUtility.LoadPrefabContents(SoundIntegrationSetup.PREFAB_CAR08_PATH);
            try
            {
                Assert(root.name == "Car08", "La raíz del prefab debe llamarse Car08", ref total, ref passed);

                // Componentes nuevos con profile asignado
                CheckProfile<VehicleEngineSimulator>(root, profile, "m_Profile", ref total, ref passed);
                CheckProfile<VehicleEngineAudio>(root, profile, "m_Profile", ref total, ref passed);
                CheckProfile<VehicleTurnSignalAudio>(root, profile, "m_Profile", ref total, ref passed);
                CheckProfile<VehicleHandbrakeAudio>(root, profile, "m_Profile", ref total, ref passed);
                CheckProfile<VehicleCabinControlsAudio>(root, profile, "m_Profile", ref total, ref passed);
                CheckProfile<VehicleRoadNoiseAudio>(root, profile, "m_Profile", ref total, ref passed);

                // Cada componente con todas sus referencias de fuente asignadas
                foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!(c is VehicleEngineAudio || c is VehicleTurnSignalAudio || c is VehicleHandbrakeAudio ||
                          c is VehicleCabinControlsAudio || c is VehicleRoadNoiseAudio || c is VehicleEngineSimulator)) continue;

                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    var unassigned = new List<string>();
                    for (bool enter = true; it.NextVisible(enter); enter = false)
                    {
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.name != "m_Script" &&
                            it.name != "m_MixerGroup" && it.objectReferenceValue == null)
                            unassigned.Add(it.name);
                    }
                    Assert(unassigned.Count == 0, $"{c.GetType().Name}: referencias sin asignar ({string.Join(", ", unassigned)})", ref total, ref passed);
                }

                // AudioSources propias (se excluye el AudioSource preexistente del panel de espejos)
                var loopNames = new HashSet<string> { "Engine_Idle", "Engine_Mid", "Engine_High" };
                int checkedSources = 0;
                foreach (var src in root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (src.GetComponentInParent<VRSideMirrorControlPanel>(true) != null) continue;
                    checkedSources++;

                    bool expectLoop = loopNames.Contains(src.name) || src.transform.parent.name == "Audio_RoadNoise";
                    string label = $"AudioSource '{src.transform.parent.name}/{src.name}'";
                    Assert(!src.playOnAwake, $"{label}: playOnAwake debe ser false", ref total, ref passed);
                    Assert(Mathf.Approximately(src.dopplerLevel, 0f), $"{label}: dopplerLevel debe ser 0", ref total, ref passed);
                    Assert(Mathf.Approximately(src.spatialBlend, 1f), $"{label}: spatialBlend debe ser 1", ref total, ref passed);
                    Assert(src.loop == expectLoop, $"{label}: loop debe ser {expectLoop}", ref total, ref passed);
                    if (mixer != null)
                        Assert(src.outputAudioMixerGroup != null, $"{label}: debe tener grupo de mixer asignado", ref total, ref passed);
                }
                Assert(checkedSources == 13, $"Deben existir 13 AudioSources de vehículo (encontradas {checkedSources})", ref total, ref passed);

                // Guiños: lado izquierdo/derecho respecto del conductor, en espacio local del auto
                Transform seat = FindDeep(root.transform, "DriverSeat");
                Transform left = root.transform.Find("Audio_Blinker_Left");
                Transform right = root.transform.Find("Audio_Blinker_Right");
                Assert(seat != null && left != null && right != null, "Deben existir DriverSeat, Audio_Blinker_Left y Audio_Blinker_Right", ref total, ref passed);
                if (seat != null && left != null && right != null)
                {
                    float seatX = root.transform.InverseTransformPoint(seat.position).x;
                    float lx = root.transform.InverseTransformPoint(left.position).x;
                    float rx = root.transform.InverseTransformPoint(right.position).x;
                    Assert(lx < seatX, $"Audio_Blinker_Left (x={lx:F2}) debe quedar a la izquierda del conductor (x={seatX:F2})", ref total, ref passed);
                    Assert(rx > seatX, $"Audio_Blinker_Right (x={rx:F2}) debe quedar a la derecha del conductor (x={seatX:F2})", ref total, ref passed);
                }

                // Perilla de luces: campos existentes asignados
                var knob = root.GetComponentInChildren<VRHeadlightKnob>(true);
                if (knob != null)
                {
                    var so = new SerializedObject(knob);
                    Assert(so.FindProperty("m_AudioSource").objectReferenceValue != null, "VRHeadlightKnob.m_AudioSource debe estar asignado", ref total, ref passed);
                    Assert(so.FindProperty("m_ClickClip").objectReferenceValue != null, "VRHeadlightKnob.m_ClickClip debe estar asignado", ref total, ref passed);
                }
                else
                {
                    Assert(false, "VRHeadlightKnob debe existir en Car08", ref total, ref passed);
                }

                var vehicle = root.GetComponent<VehicleController>();
                Assert(vehicle != null && vehicle.CrankDuration > 0f, "Car08: m_CrankDuration debe ser > 0", ref total, ref passed);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CheckProfile<T>(GameObject root, VehicleAudioProfile profile, string field, ref int total, ref int passed)
            where T : Component
        {
            var c = root.GetComponentInChildren<T>(true);
            Assert(c != null, $"{typeof(T).Name} debe existir en Car08", ref total, ref passed);
            if (c == null) return;
            var so = new SerializedObject(c);
            Assert(so.FindProperty(field).objectReferenceValue == profile && profile != null,
                $"{typeof(T).Name} debe tener el VehicleAudioProfile_Car08 asignado", ref total, ref passed);
        }

        private static void VerifyCar07Untouched(ref int total, ref int passed)
        {
            GameObject car07 = AssetDatabase.LoadAssetAtPath<GameObject>(CockpitIntegrationSetup.PREFAB_CAR07_PATH);
            Assert(car07 != null, "Car07.prefab debe seguir existiendo", ref total, ref passed);
            if (car07 == null) return;

            var vc = car07.GetComponent<VehicleController>();
            Assert(vc != null && Mathf.Approximately(vc.CrankDuration, 0f), "Car07: m_CrankDuration debe ser 0 (sin cambios de comportamiento)", ref total, ref passed);
            Assert(car07.GetComponentInChildren<VehicleEngineSimulator>(true) == null &&
                   car07.GetComponentInChildren<VehicleEngineAudio>(true) == null,
                "Car07 no debe tener componentes de audio nuevos", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Matemática pura

        private static void VerifyRpmMath(ref int total, ref int passed)
        {
            var s = new EngineSimulationSettings();

            var idle = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Park, 0f, 0f, 1f, 0);
            Assert(Mathf.Approximately(idle.TargetRpm, s.IdleRpm), $"Ralentí en P sin acelerador = {s.IdleRpm} RPM (obtenido {idle.TargetRpm})", ref total, ref passed);

            var off = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Off, GearState.Park, 0f, 0f, 1f, 0);
            Assert(off.TargetRpm == 0f, "Motor apagado = 0 RPM", ref total, ref passed);

            var crank = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Cranking, GearState.Park, 0f, 0f, 1f, 0);
            Assert(Mathf.Approximately(crank.TargetRpm, s.CrankRpm), "Cranking = RPM de burro", ref total, ref passed);

            var free = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Neutral, 0f, 1f, 0f, 0);
            Assert(free.TargetRpm > s.IdleRpm + 1000f, "Revoluciones libres en N con acelerador a fondo suben de ralentí", ref total, ref passed);

            // Monotonía dentro de la primera marcha (0..15 km/h, sin cambio)
            bool monotonic = true;
            float prev = -1f;
            int gear = 0;
            for (float v = 0f; v < 14.9f; v += 0.5f)
            {
                var r = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Drive, v, 0.5f, 0f, gear);
                gear = r.VirtualGear;
                if (r.VirtualGear != 1 || r.TargetRpm < prev) monotonic = false;
                prev = r.TargetRpm;
            }
            Assert(monotonic, "RPM monótonas crecientes con la velocidad dentro de la 1ª marcha", ref total, ref passed);

            // Caída de RPM al subir de marcha
            float gearSpan = s.MaxSpeedKmh / s.ForwardGears;
            var before = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Drive, gearSpan - 0.1f, 0f, 0f, 1);
            var after = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Drive, gearSpan + 0.1f, 0f, 0f, 1);
            Assert(after.VirtualGear == before.VirtualGear + 1, "Al cruzar el límite de la 1ª marcha se sube a la 2ª", ref total, ref passed);
            Assert(after.TargetRpm < before.TargetRpm - 500f, $"Las RPM caen al subir de marcha ({before.TargetRpm:F0} → {after.TargetRpm:F0})", ref total, ref passed);

            // Histéresis al bajar
            var stay = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Drive, gearSpan - 0.5f, 0f, 0f, 2);
            Assert(stay.VirtualGear == 2, "Histéresis: no baja de marcha apenas cruza el límite", ref total, ref passed);
            var down = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Drive, gearSpan - s.DownshiftHysteresisKmh - 0.5f, 0f, 0f, 2);
            Assert(down.VirtualGear == 1, "Baja de marcha superado el margen de histéresis", ref total, ref passed);

            // Tope con freno de mano + acelerador
            var stall = VehicleEngineSimulator.ComputeTargetRpm(s, EngineState.Running, GearState.Drive, 0f, 1f, 1f, 1);
            Assert(Mathf.Approximately(stall.TargetRpm, s.HandbrakeStallMaxRpm), $"Freno de mano + acelerador a fondo en D: tope {s.HandbrakeStallMaxRpm} RPM (obtenido {stall.TargetRpm})", ref total, ref passed);

            // Suavizado asimétrico
            float up = VehicleEngineSimulator.StepRpm(s, 1000f, 5000f, 0.1f, EngineState.Running, false) - 1000f;
            float dn = 1000f - VehicleEngineSimulator.StepRpm(s, 1000f, 0f, 0.1f, EngineState.Running, false);
            Assert(dn < up, "La caída de RPM (freno motor) es más lenta que la subida", ref total, ref passed);
        }

        private static void VerifyAudioMath(ref int total, ref int passed)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>(SoundIntegrationSetup.PROFILE_PATH);
            if (profile == null) return;

            bool constantPower = true;
            for (float rpm = 0f; rpm <= 5000f; rpm += 100f)
            {
                VehicleEngineAudio.ComputeLayerWeights(profile, rpm, out float a, out float b, out float c);
                if (Mathf.Abs(a * a + b * b + c * c - 1f) > 0.001f) constantPower = false;
            }
            Assert(constantPower, "El crossfade de capas del motor es de potencia constante en todo el rango", ref total, ref passed);

            float p = VehicleEngineAudio.ComputePitch(profile, 100000f, profile.IdleLayerRpm);
            Assert(Mathf.Approximately(p, profile.PitchRange.y), "El pitch se clampea al rango del profile", ref total, ref passed);

            Assert(VehicleHandbrakeAudio.CrossedRatchetStep(0.10f, 0.13f, 0.125f), "Trinquete: se detecta el cruce de escalón", ref total, ref passed);
            Assert(!VehicleHandbrakeAudio.CrossedRatchetStep(0.13f, 0.20f, 0.125f), "Trinquete: sin cruce dentro de un mismo escalón", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Escena

        private static void VerifyScene(ref int total, ref int passed)
        {
            Assert(!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(SoundIntegrationSetup.SCENE_SOUND_PATH)),
                $"SoundIntegrationScene debe existir en {SoundIntegrationSetup.SCENE_SOUND_PATH}", ref total, ref passed);

            Scene previous = SceneManager.GetActiveScene();
            string previousPath = previous.path;
            if (previous.isDirty) EditorSceneManager.SaveScene(previous);

            Scene scene = EditorSceneManager.OpenScene(SoundIntegrationSetup.SCENE_SOUND_PATH, OpenSceneMode.Single);
            try
            {
                int car08 = 0, car07 = 0;
                foreach (var vc in Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include))
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(vc.gameObject)) continue;
                    string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(vc.gameObject));
                    if (path == SoundIntegrationSetup.PREFAB_CAR08_PATH) car08++;
                    else if (path == CockpitIntegrationSetup.PREFAB_CAR07_PATH) car07++;
                }
                Assert(car08 == 1, $"La escena debe tener exactamente 1 instancia de Car08 (encontradas {car08})", ref total, ref passed);
                Assert(car07 == 0, $"La escena no debe tener instancias de Car07 (encontradas {car07})", ref total, ref passed);

                int listeners = 0;
                foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                    if (l.enabled && l.gameObject.activeInHierarchy) listeners++;
                Assert(listeners == 1, $"Debe haber exactamente 1 AudioListener habilitado (encontrados {listeners})", ref total, ref passed);

                var followers = Object.FindObjectsByType<DriverSeatFollower>(FindObjectsInactive.Include);
                Assert(followers.Length >= 1, "Debe existir un DriverSeatFollower", ref total, ref passed);
                foreach (var f in followers)
                {
                    Transform seat = f.DriverSeat;
                    bool ok = seat != null && seat.name == "DriverSeat" && seat.GetComponentInParent<VehicleEngineSimulator>() != null;
                    Assert(ok, "DriverSeatFollower.DriverSeat debe apuntar al DriverSeat de Car08", ref total, ref passed);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath) && previousPath != scene.path)
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            }
        }

        // ---------------------------------------------------------------- Suites previas

        private static void VerifyExistingSuites(ref int total, ref int passed)
        {
            RunSuite("Tools/Primer Volante/Run Cockpit Integration Verification", "CockpitIntegrationVerification", ref total, ref passed);
            // VehicleLightingVerification instancia Car06 (no Car07/Car08) y ya fallaba antes de este trabajo en
            // "Indicator_PositionLights debe existir en el cluster": es un fallo preexistente y ajeno al audio.
            RunSuite("Tools/Primer Volante/Run Vehicle Lighting Verification", "VehicleLightingVerification", ref total, ref passed,
                "Indicator_PositionLights debe existir en el cluster");
        }

        private static void RunSuite(string menuPath, string name, ref int total, ref int passed, string knownFailure = null)
        {
            int fails = 0;
            int suiteTotal = 0, suitePassed = 0;
            void OnLog(string message, string stack, LogType type)
            {
                if (message.Contains("[FAIL]") && (knownFailure == null || !message.Contains(knownFailure))) fails++;
                Match m = Regex.Match(message, @"RESULTADO FINAL:\s*(\d+)/(\d+)");
                if (m.Success)
                {
                    suitePassed = int.Parse(m.Groups[1].Value);
                    suiteTotal = int.Parse(m.Groups[2].Value);
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                EditorApplication.ExecuteMenuItem(menuPath);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            int expectedFailures = knownFailure != null ? 1 : 0;
            Assert(suiteTotal > 0 && fails == 0 && suitePassed == suiteTotal - expectedFailures,
                $"{name} sigue pasando ({suitePassed}/{suiteTotal}, {fails} fallos nuevos, {expectedFailures} preexistente)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Helpers

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
