using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Feedback;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de las vibraciones hápticas (Car08 + SoundIntegrationScene).
    /// </summary>
    public static class HapticsIntegrationVerification
    {
        [MenuItem("Tools/Primer Volante/Run Haptics Integration Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE HÁPTICOS (Car08)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyProfile(ref total, ref passed);
            VerifyCar08Prefab(ref total, ref passed);
            VerifyPureLogic(ref total, ref passed);
            VerifyScene(ref total, ref passed);
            VerifyExistingSuites(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Profile

        private static void VerifyProfile(ref int total, ref int passed)
        {
            var profile = AssetDatabase.LoadAssetAtPath<CabinHapticProfile>(HapticsIntegrationSetup.PROFILE_PATH);
            Assert(profile != null, $"CabinHapticProfile debe existir en {HapticsIntegrationSetup.PROFILE_PATH}", ref total, ref passed);
            if (profile == null) return;

            // Globales
            Assert(Mathf.Approximately(profile.MasterAmplitude, 1f), "Multiplicador maestro = 1.0", ref total, ref passed);
            Assert(Mathf.Approximately(profile.ReachCooldownSeconds, 0.25f), "Cooldown de alcance = 0.25 s", ref total, ref passed);
            Assert(Mathf.Approximately(profile.MaxReachDistance, 0.12f), "Distancia máxima de alcance = 0.12 m", ref total, ref passed);
            Assert(Mathf.Approximately(profile.SliderStep, 0.01f), "Step del slider = 0.01", ref total, ref passed);

            // Categorías
            CheckControl(profile.Levers, "Palancas", (0.35f, 0.04f, 2, 0.06f), (0.5f, 0.05f, 1, 0f), ref total, ref passed);
            CheckControl(profile.Wheel, "Volante", (0.2f, 0.12f, 1, 0f), (0.4f, 0.08f, 1, 0f), ref total, ref passed);
            CheckControl(profile.ButtonsAndKnobs, "Botones y perillas", (0.3f, 0.03f, 1, 0f), (0.45f, 0.04f, 1, 0f), ref total, ref passed);

            // Confirmaciones
            var c = profile.Confirmations;
            CheckPattern(c.GearChange, "Cambio de marcha", 0.6f, 0.06f, 1, 0f, ref total, ref passed);
            CheckPattern(c.TurnSignalClick, "Clic del guiño", 0.4f, 0.025f, 1, 0f, ref total, ref passed);
            CheckPattern(c.HeadlightDetent, "Detent de la perilla de luces", 0.3f, 0.02f, 1, 0f, ref total, ref passed);
            CheckPattern(c.HandbrakeRatchet, "Trinquete del freno de mano", 0.25f, 0.015f, 1, 0f, ref total, ref passed);
            CheckPattern(c.HandbrakeRelease, "Freno de mano liberado", 0.4f, 0.08f, 1, 0f, ref total, ref passed);
            CheckPattern(c.EngineStartAccepted, "Arranque aceptado", 0.5f, 0.05f, 1, 0f, ref total, ref passed);
            CheckPattern(c.EngineStartRejected, "Arranque rechazado", 0.4f, 0.04f, 3, 0.05f, ref total, ref passed);
            CheckPattern(c.HazardToggle, "Balizas", 0.4f, 0.025f, 1, 0f, ref total, ref passed);

            // UI
            var ui = profile.UI;
            CheckPattern(ui.Hover, "UI hover", 0.15f, 0.015f, 1, 0f, ref total, ref passed);
            CheckPattern(ui.Click, "UI click", 0.3f, 0.03f, 1, 0f, ref total, ref passed);
            CheckPattern(ui.SliderTick, "UI slider tick", 0.12f, 0.01f, 1, 0f, ref total, ref passed);
            CheckPattern(ui.SliderZero, "UI slider cero", 0.4f, 0.04f, 1, 0f, ref total, ref passed);
            CheckPattern(ui.SliderLimit, "UI slider límite", 0.5f, 0.06f, 1, 0f, ref total, ref passed);
        }

        private static void CheckControl(ControlHaptics control, string name,
            (float amp, float dur, int count, float gap) reach, (float amp, float dur, int count, float gap) grab,
            ref int total, ref int passed)
        {
            CheckPattern(control.Reach, $"{name}: alcance", reach.amp, reach.dur, reach.count, reach.gap, ref total, ref passed);
            CheckPattern(control.Grab, $"{name}: agarre", grab.amp, grab.dur, grab.count, grab.gap, ref total, ref passed);
        }

        private static void CheckPattern(HapticPattern p, string name, float amp, float dur, int count, float gap,
            ref int total, ref int passed)
        {
            bool ok = p != null && p.Pulse != null &&
                      Mathf.Approximately(p.Pulse.Amplitude, amp) && Mathf.Approximately(p.Pulse.Duration, dur) &&
                      p.Count == count && Mathf.Approximately(p.Gap, gap);
            string actual = p != null && p.Pulse != null
                ? $"{p.Count} × ({p.Pulse.Amplitude}, {p.Pulse.Duration}), separación {p.Gap}"
                : "nulo";
            Assert(ok, $"{name} = {count} × ({amp}, {dur}), separación {gap} (actual: {actual})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Car08

        private static void VerifyCar08Prefab(ref int total, ref int passed)
        {
            var profile = AssetDatabase.LoadAssetAtPath<CabinHapticProfile>(HapticsIntegrationSetup.PROFILE_PATH);
            Assert(AssetDatabase.LoadAssetAtPath<GameObject>(SoundIntegrationSetup.PREFAB_CAR08_PATH) != null,
                $"Car08.prefab debe existir en {SoundIntegrationSetup.PREFAB_CAR08_PATH}", ref total, ref passed);

            GameObject root = PrefabUtility.LoadPrefabContents(SoundIntegrationSetup.PREFAB_CAR08_PATH);
            try
            {
                foreach (var spec in HapticsIntegrationSetup.Targets)
                {
                    XRBaseInteractable interactable = null;
                    foreach (var i in root.GetComponentsInChildren<XRBaseInteractable>(true))
                        if (i.name == spec.Name) { interactable = i; break; }

                    Assert(interactable != null, $"{spec.Name}: debe existir como interactable", ref total, ref passed);
                    if (interactable == null) continue;

                    var target = interactable.GetComponent<CabinHapticTarget>();
                    Assert(target != null, $"{spec.Name}: debe tener CabinHapticTarget", ref total, ref passed);
                    if (target == null) continue;

                    Assert(target.Category == spec.Category, $"{spec.Name}: categoría {spec.Category} (actual {target.Category})", ref total, ref passed);
                    Assert(target.PlayReach == spec.PlayReach, $"{spec.Name}: PlayReach = {spec.PlayReach}", ref total, ref passed);
                    Assert(target.Profile == profile && profile != null, $"{spec.Name}: debe tener el CabinHapticProfile_Car08 asignado", ref total, ref passed);
                }

                int targetCount = root.GetComponentsInChildren<CabinHapticTarget>(true).Length;
                Assert(targetCount == HapticsIntegrationSetup.Targets.Length,
                    $"Car08 debe tener exactamente {HapticsIntegrationSetup.Targets.Length} CabinHapticTarget (encontrados {targetCount})", ref total, ref passed);

                var cabin = root.GetComponentInChildren<VehicleCabinControlsHaptics>(true);
                Assert(cabin != null, "Car08 debe tener VehicleCabinControlsHaptics", ref total, ref passed);
                if (cabin != null)
                {
                    var so = new SerializedObject(cabin);
                    var it = so.GetIterator();
                    var unassigned = new List<string>();
                    for (bool enter = true; it.NextVisible(enter); enter = false)
                    {
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.name != "m_Script" &&
                            it.objectReferenceValue == null)
                            unassigned.Add(it.name);
                    }
                    Assert(unassigned.Count == 0, $"VehicleCabinControlsHaptics: referencias sin asignar ({string.Join(", ", unassigned)})", ref total, ref passed);
                    Assert(so.FindProperty("m_Profile").objectReferenceValue == profile && profile != null,
                        "VehicleCabinControlsHaptics debe tener el CabinHapticProfile_Car08 asignado", ref total, ref passed);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- Lógica pura

        private static void VerifyPureLogic(ref int total, ref int passed)
        {
            // Cruce de escalón del slider (step 0.01)
            Assert(UIHapticFeedback.CrossedSliderStep(0.0099f, 0.0101f, 0.01f), "Slider: cruzar 0.01 hacia arriba es un tick", ref total, ref passed);
            Assert(UIHapticFeedback.CrossedSliderStep(0.0101f, 0.0099f, 0.01f), "Slider: cruzar 0.01 hacia abajo es un tick", ref total, ref passed);
            Assert(!UIHapticFeedback.CrossedSliderStep(0.011f, 0.019f, 0.01f), "Slider: moverse dentro de un mismo escalón no es un tick", ref total, ref passed);
            Assert(UIHapticFeedback.CrossedSliderStep(-0.0099f, -0.0101f, 0.01f), "Slider: los escalones negativos también cuentan", ref total, ref passed);
            Assert(UIHapticFeedback.CrossedSliderStep(0.07f, 0.08f, 0.01f), "Slider: 0.07 → 0.08 cruza un escalón pese al ruido de float", ref total, ref passed);
            Assert(!UIHapticFeedback.CrossedSliderStep(0f, 1f, 0f), "Slider: step 0 no genera ticks", ref total, ref passed);

            // Cruce por 0
            Assert(UIHapticFeedback.CrossedSliderZero(0.003f, -0.002f), "Slider: pasar de + a − cruza el 0", ref total, ref passed);
            Assert(UIHapticFeedback.CrossedSliderZero(-0.05f, 0.05f), "Slider: pasar de − a + cruza el 0", ref total, ref passed);
            Assert(UIHapticFeedback.CrossedSliderZero(0.02f, 0f), "Slider: aterrizar en 0 cuenta", ref total, ref passed);
            Assert(!UIHapticFeedback.CrossedSliderZero(0f, 0.02f), "Slider: salir de 0 no cuenta", ref total, ref passed);
            Assert(!UIHapticFeedback.CrossedSliderZero(0.05f, 0.10f), "Slider: moverse sin pasar por 0 no cuenta", ref total, ref passed);

            // Límites (rango de la altura: −0.15 / +0.20)
            Assert(UIHapticFeedback.ReachedSliderLimit(-0.14f, -0.15f, -0.15f, 0.20f), "Slider: llegar a minValue es un límite", ref total, ref passed);
            Assert(UIHapticFeedback.ReachedSliderLimit(0.19f, 0.20f, -0.15f, 0.20f), "Slider: llegar a maxValue es un límite", ref total, ref passed);
            Assert(!UIHapticFeedback.ReachedSliderLimit(0.20f, 0.20f, -0.15f, 0.20f), "Slider: seguir en el límite no vuelve a disparar", ref total, ref passed);
            Assert(!UIHapticFeedback.ReachedSliderLimit(0.05f, 0.10f, -0.15f, 0.20f), "Slider: un movimiento intermedio no es un límite", ref total, ref passed);

            // Clasificación (límite > cero > tick > nada)
            Assert(UIHapticFeedback.ClassifySliderMove(-0.24f, -0.25f, -0.25f, 0.25f, 0.01f) == SliderHapticEvent.Limit,
                "Slider: llegar a −0.25 (que también es múltiplo del step) vibra como límite", ref total, ref passed);
            Assert(UIHapticFeedback.ClassifySliderMove(0.004f, -0.004f, -0.25f, 0.25f, 0.01f) == SliderHapticEvent.Zero,
                "Slider: cruzar el 0 (que también es múltiplo del step) vibra como cero", ref total, ref passed);
            Assert(UIHapticFeedback.ClassifySliderMove(0.052f, 0.061f, -0.25f, 0.25f, 0.01f) == SliderHapticEvent.Tick,
                "Slider: cruzar 0.06 vibra como tick", ref total, ref passed);
            Assert(UIHapticFeedback.ClassifySliderMove(0.052f, 0.058f, -0.25f, 0.25f, 0.01f) == SliderHapticEvent.None,
                "Slider: moverse dentro de un escalón no vibra", ref total, ref passed);
            Assert(UIHapticFeedback.ClassifySliderMove(0.1f, 0.1f, -0.25f, 0.25f, 0.01f) == SliderHapticEvent.None,
                "Slider: sin cambio de valor no vibra", ref total, ref passed);

            // Filtro de alcance (distancia máxima 0.12 m)
            Assert(CabinHapticTarget.ShouldPlayReach(0.05f, 0.12f, false), "Alcance: mano a 5 cm vibra", ref total, ref passed);
            Assert(CabinHapticTarget.ShouldPlayReach(0.12f, 0.12f, false), "Alcance: el límite de 12 cm es inclusivo", ref total, ref passed);
            Assert(!CabinHapticTarget.ShouldPlayReach(0.80f, 0.12f, false), "Alcance: el rayo del menú a 80 cm no vibra", ref total, ref passed);
            Assert(!CabinHapticTarget.ShouldPlayReach(float.PositiveInfinity, 0.12f, false), "Alcance: sin colliders válidos no vibra", ref total, ref passed);
            Assert(!CabinHapticTarget.ShouldPlayReach(0.05f, 0.12f, true), "Alcance: con la mano agarrando algo no vibra", ref total, ref passed);

            // Cooldown por mano
            Assert(!HapticPlayback.HasCooldownElapsed(10.1f, 10f, 0.25f), "Cooldown: 0.1 s después de un alcance sigue activo", ref total, ref passed);
            Assert(HapticPlayback.HasCooldownElapsed(10.3f, 10f, 0.25f), "Cooldown: 0.3 s después de un alcance ya expiró", ref total, ref passed);
            Assert(HapticPlayback.HasCooldownElapsed(0f, float.NegativeInfinity, 0.25f), "Cooldown: el primer alcance de una mano siempre pasa", ref total, ref passed);

            // Trinquete (reutiliza la lógica del audio)
            Assert(VehicleHandbrakeAudio.CrossedRatchetStep(0.10f, 0.13f, 0.125f), "Trinquete: se detecta el cruce de escalón", ref total, ref passed);

            // Patrones
            var rejected = new HapticPattern(0.4f, 0.04f, 3, 0.05f);
            Assert(Mathf.Approximately(rejected.TotalDuration, 3 * 0.04f + 2 * 0.05f), "HapticPattern: duración total = pulsos + separaciones", ref total, ref passed);

            // Sin player (simulador, teclado) no hace nada ni tira error
            bool noPlayerOk = !HapticPlayback.Play((HapticImpulsePlayer)null, rejected) && !HapticPlayback.TryConsumeReachCooldown(null, 0.25f);
            Assert(noPlayerOk, "HapticPlayback: sin HapticImpulsePlayer no hace nada y no falla", ref total, ref passed);
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
                var profile = AssetDatabase.LoadAssetAtPath<CabinHapticProfile>(HapticsIntegrationSetup.PROFILE_PATH);

                // Hover/select genéricos apagados en Near-Far y Poke; Teleport intacto.
                int checkedFeedbacks = 0, teleportFeedbacks = 0;
                foreach (var haptic in Object.FindObjectsByType<SimpleHapticFeedback>(FindObjectsInactive.Include))
                {
                    string label = $"SimpleHapticFeedback '{haptic.transform.parent?.name}/{haptic.name}'";
                    bool nearFarOrPoke = haptic.GetComponent<NearFarInteractor>() != null || haptic.GetComponent<XRPokeInteractor>() != null;
                    if (nearFarOrPoke)
                    {
                        checkedFeedbacks++;
                        Assert(!haptic.playHoverEntered, $"{label}: playHoverEntered debe ser false", ref total, ref passed);
                        Assert(!haptic.playSelectEntered, $"{label}: playSelectEntered debe ser false", ref total, ref passed);
                    }
                    else
                    {
                        teleportFeedbacks++;
                        Assert(!HasOverride(haptic, "m_PlayHoverEntered") && !HasOverride(haptic, "m_PlaySelectEntered"),
                            $"{label}: el Teleport no debe tener overrides de hover/select", ref total, ref passed);
                    }
                }
                Assert(checkedFeedbacks == 4, $"Deben existir 4 SimpleHapticFeedback de Near-Far/Poke (encontrados {checkedFeedbacks})", ref total, ref passed);
                Assert(teleportFeedbacks == 2, $"Deben existir 2 SimpleHapticFeedback de Teleport (encontrados {teleportFeedbacks})", ref total, ref passed);

                // Sorting ClosestPointOnCollider en ambos Near-Far, sin tocar el prefab de Samples.
                int nearFarCount = 0;
                foreach (var nearFar in Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include))
                {
                    nearFarCount++;
                    Assert(nearFar.nearCasterSortingStrategy == NearFarInteractor.NearCasterSortingStrategy.ClosestPointOnCollider,
                        $"NearFarInteractor '{nearFar.transform.parent?.name}': sorting debe ser ClosestPointOnCollider", ref total, ref passed);

                    var source = PrefabUtility.GetCorrespondingObjectFromSource(nearFar);
                    Assert(source != null && source.nearCasterSortingStrategy == NearFarInteractor.NearCasterSortingStrategy.SquareDistance,
                        $"NearFarInteractor '{nearFar.transform.parent?.name}': el prefab de Samples debe seguir en SquareDistance (override de instancia)", ref total, ref passed);
                }
                Assert(nearFarCount == 2, $"Deben existir 2 NearFarInteractor (encontrados {nearFarCount})", ref total, ref passed);

                // UIHapticFeedback: uno solo, con perfil
                var uiHaptics = Object.FindObjectsByType<UIHapticFeedback>(FindObjectsInactive.Include);
                Assert(uiHaptics.Length == 1, $"Debe haber exactamente 1 UIHapticFeedback (encontrados {uiHaptics.Length})", ref total, ref passed);
                if (uiHaptics.Length == 1)
                    Assert(uiHaptics[0].Profile == profile && profile != null, "UIHapticFeedback debe tener el CabinHapticProfile_Car08 asignado", ref total, ref passed);

                // La instancia de Car08 hereda los componentes nuevos
                int car08Targets = Object.FindObjectsByType<CabinHapticTarget>(FindObjectsInactive.Include).Length;
                Assert(car08Targets == HapticsIntegrationSetup.Targets.Length,
                    $"La escena debe tener {HapticsIntegrationSetup.Targets.Length} CabinHapticTarget (encontrados {car08Targets})", ref total, ref passed);
                Assert(Object.FindObjectsByType<VehicleCabinControlsHaptics>(FindObjectsInactive.Include).Length == 1,
                    "La escena debe tener 1 VehicleCabinControlsHaptics", ref total, ref passed);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath) && previousPath != scene.path)
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            }
        }

        /// <summary>Indica si la instancia de prefab tiene un override sobre la propiedad del componente.</summary>
        private static bool HasOverride(Component component, string propertyPath)
        {
            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(component.gameObject);
            if (outermost == null) return false;

            foreach (var mod in PrefabUtility.GetPropertyModifications(outermost))
            {
                if (mod.propertyPath == propertyPath && mod.target is Component c && c.gameObject == component.gameObject)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- Suites previas

        private static void VerifyExistingSuites(ref int total, ref int passed)
        {
            // La suite de sonido ejecuta a su vez las de cockpit e iluminación (con su fallo preexistente conocido),
            // así que sirve para detectar que Car08 y la escena siguen funcionando tras agregar los hápticos.
            RunSuite("Tools/Primer Volante/Run Sound Integration Verification", "SoundIntegrationVerification", ref total, ref passed);
        }

        private static void RunSuite(string menuPath, string name, ref int total, ref int passed)
        {
            int fails = 0;
            int suiteTotal = 0, suitePassed = 0;
            void OnLog(string message, string stack, LogType type)
            {
                // Fallo preexistente y ajeno (ver SoundIntegrationVerification.VerifyExistingSuites).
                if (message.Contains("[FAIL]") && !message.Contains("Indicator_PositionLights debe existir en el cluster")) fails++;
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

            Assert(suiteTotal > 0 && fails == 0 && suitePassed == suiteTotal,
                $"{name} sigue pasando ({suitePassed}/{suiteTotal}, {fails} fallos)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Helpers

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
