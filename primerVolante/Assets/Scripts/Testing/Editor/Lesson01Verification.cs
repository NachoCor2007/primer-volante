using System.Linq;
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada para la arquitectura de lecciones y Lección 1 (TASK-204 / Issue #19).
    /// Valida idempotencia, bloqueo selectivo de controles, posicionamiento del HUD, raycasting VR,
    /// disponibilidad de InGameMenuController y conformidad estricta con las reglas de AGENTS.md (cero APIs obsoletas).
    /// </summary>
    public static class Lesson01Verification
    {
        public static readonly System.Collections.Generic.List<string> LastFailures = new System.Collections.Generic.List<string>();

        [MenuItem("Tools/Primer Volante/Lessons/Verify Lesson 1")]
        public static bool RunVerification()
        {
            LastFailures.Clear();
            Debug.Log("=================================================================");
            Debug.Log("🧪 [Lesson01Verification] Iniciando suite de pruebas de Lección 1...");
            Debug.Log("=================================================================");

            int totalTests = 0;
            int passedTests = 0;

            try
            {
                // 1. Verificación de Archivos y Asset de Lección
                VerifyFilesAndLessonAsset(ref totalTests, ref passedTests);

                // 2. Verificación de Escena y Jerarquía
                VerifySceneAndComponents(ref totalTests, ref passedTests);

                // 3. Verificación de Código y APIs Obsoletas (AGENTS.md)
                VerifyNoObsoleteAPIs(ref totalTests, ref passedTests);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Lesson01Verification] 💥 Excepción no controlada durante las pruebas: {ex}");
            }

            Debug.Log("=================================================================");
            bool allPassed = (totalTests > 0 && passedTests == totalTests);
            if (allPassed)
            {
                Debug.Log($"🏁 [Lesson01Verification] ¡TODAS LAS PRUEBAS PASARON! ({passedTests}/{totalTests} pasadas)");
            }
            else
            {
                Debug.LogError($"🏁 [Lesson01Verification] PRUEBAS FALLIDAS: ({passedTests}/{totalTests} pasadas)");
            }
            Debug.Log("=================================================================");

            return allPassed;
        }

        public static void RunVerificationBatch()
        {
            // Ejecutar setup para garantizar que la escena, asset y componentes estén actualizados y guardados
            Lesson01SceneSetup.SetupLesson01();

            bool pass = RunVerification();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(pass ? 0 : 1);
            }
        }

        // =========================================================================
        // 1. Verificación de Archivos y ScriptableObject
        // =========================================================================

        private static void VerifyFilesAndLessonAsset(ref int total, ref int passed)
        {
            Debug.Log("--- 1. Verificación de Asset y Escena ---");

            bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(Lesson01SceneSetup.TARGET_SCENE_PATH) != null ||
                               File.Exists(Lesson01SceneSetup.TARGET_SCENE_PATH) ||
                               File.Exists(Path.Combine(Application.dataPath, "../", Lesson01SceneSetup.TARGET_SCENE_PATH));
            Assert(sceneExists, $"La escena debe existir en {Lesson01SceneSetup.TARGET_SCENE_PATH}", ref total, ref passed);

            bool dataAssetExists = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Lesson01SceneSetup.LESSON_DATA_PATH) != null ||
                                   File.Exists(Lesson01SceneSetup.LESSON_DATA_PATH) ||
                                   File.Exists(Path.Combine(Application.dataPath, "../", Lesson01SceneSetup.LESSON_DATA_PATH));
            Assert(dataAssetExists, $"El asset de definición debe existir en {Lesson01SceneSetup.LESSON_DATA_PATH}", ref total, ref passed);

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Lesson01SceneSetup.LESSON_DATA_PATH);
            Assert(lesson != null, "El asset LessonDefinitionSO debe cargarse correctamente", ref total, ref passed);

            if (lesson != null)
            {
                Assert(lesson.lessonId == "LESSON_01", "lessonId debe ser 'LESSON_01'", ref total, ref passed);
                Assert(!string.IsNullOrEmpty(lesson.lessonTitle), "lessonTitle no debe estar vacío", ref total, ref passed);
                Assert(lesson.steps != null && lesson.steps.Count >= 3,
                    $"La lección debe contener al menos 3 pasos (encontrados: {lesson.steps?.Count ?? 0})", ref total, ref passed);

                if (lesson.steps != null)
                {
                    for (int i = 0; i < lesson.steps.Count; i++)
                    {
                        LessonStepSO step = lesson.steps[i];
                        Assert(step != null, $"El paso {i + 1} no debe ser nulo", ref total, ref passed);
                        if (step != null)
                        {
                            Assert(!string.IsNullOrEmpty(step.stepTitle), $"El paso {i + 1} debe tener stepTitle", ref total, ref passed);
                            Assert(!string.IsNullOrEmpty(step.instructionText), $"El paso {i + 1} debe tener instructionText", ref total, ref passed);
                            CockpitControlFlags expectedFlags = (i == 2)
                                ? (CockpitControlFlags.SteeringWheel | CockpitControlFlags.Mirrors)
                                : CockpitControlFlags.SteeringWheel;
                            Assert((step.allowedControls & expectedFlags) == expectedFlags,
                                $"En la Lección 1, el paso {i + 1} debe permitir los controles esperados (tiene: {step.allowedControls})", ref total, ref passed);

                            if (i == 0)
                            {
                                bool mentionsMenu = step.instructionText.Contains("Menu");
                                Assert(mentionsMenu, "El Paso 1 debe instruir al conductor a usar el botón Menu para calibrar postura", ref total, ref passed);
                            }
                            else if (i == 1)
                            {
                                bool mentionsGrip = step.instructionText.Contains("grip");
                                bool mentionsWheel = step.instructionText.Contains("volante");
                                Assert(mentionsGrip && mentionsWheel, "El Paso 2 debe instruir a tomar el volante presionando el grip", ref total, ref passed);
                                Assert(step.highlightTargetName == "SteeringWheel_Pivot", "El Paso 2 debe resaltar 'SteeringWheel_Pivot'", ref total, ref passed);
                            }
                            else if (i == 2)
                            {
                                bool mentionsMirrors = step.instructionText.Contains("espejos retrovisores") || step.instructionText.Contains("espejos");
                                Assert(mentionsMirrors, "El Paso 3 debe instruir a verificar o ajustar los espejos retrovisores", ref total, ref passed);
                                Assert(step.highlightTargetName == "Mirrors", "El Paso 3 debe tener highlightTargetName 'Mirrors'", ref total, ref passed);
                            }
                        }
                    }
                }
            }
        }

        // =========================================================================
        // 2. Verificación de Escena y Componentes en Jerarquía
        // =========================================================================

        private static void VerifySceneAndComponents(ref int total, ref int passed)
        {
            Debug.Log("--- 2. Verificación de Escena, Mandos, HUD y InGameMenu ---");

            Scene currentScene = EditorSceneManager.GetActiveScene();
            Scene scene = (currentScene.IsValid() && currentScene.path == Lesson01SceneSetup.TARGET_SCENE_PATH)
                ? currentScene
                : EditorSceneManager.OpenScene(Lesson01SceneSetup.TARGET_SCENE_PATH, OpenSceneMode.Single);
            Assert(scene.IsValid() && scene.isLoaded, $"La escena {Lesson01SceneSetup.TARGET_SCENE_PATH} debe cargarse correctamente", ref total, ref passed);

            // Vehículo
            GameObject car09 = GameObject.Find("Car09");
            Assert(car09 != null, "GameObject 'Car09' debe existir en la escena", ref total, ref passed);
            if (car09 == null) return;

            // Sincronización de posición con el asiento (DriverSeatFollower en XR Origin)
            DriverSeatFollower seatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            Assert(seatFollower != null, "DriverSeatFollower debe existir en la escena (en XR Origin)", ref total, ref passed);
            if (seatFollower != null)
            {
                Assert(seatFollower.enabled, "DriverSeatFollower debe estar activo (enabled == true)", ref total, ref passed);
                Assert(seatFollower.FollowSeat, "DriverSeatFollower.FollowSeat debe ser true", ref total, ref passed);
                Assert(seatFollower.DriverSeat != null, "DriverSeatFollower.DriverSeat NO debe ser nulo (debe estar vinculado a Car09)", ref total, ref passed);
                if (seatFollower.DriverSeat != null)
                {
                    Assert(seatFollower.DriverSeat.IsChildOf(car09.transform),
                        "DriverSeatFollower.DriverSeat debe ser un Transform hijo del vehículo Car09", ref total, ref passed);
                    Assert(string.Equals(seatFollower.DriverSeat.name, "DriverSeat", StringComparison.OrdinalIgnoreCase),
                        $"DriverSeatFollower.DriverSeat debe llamarse 'DriverSeat' (actual: {seatFollower.DriverSeat.name})", ref total, ref passed);
                }
            }

            // CockpitControlLockManager
            CockpitControlLockManager lockManager = car09.GetComponent<CockpitControlLockManager>();
            Assert(lockManager != null, "CockpitControlLockManager debe estar adjunto a Car09", ref total, ref passed);

            if (lockManager != null)
            {
                lockManager.AutoDiscoverControls(car09.transform);

                // Comprobar que los mandos físicos existan
                Assert(lockManager.SteeringWheel != null, "VRSteeringWheel debe encontrarse en Car09", ref total, ref passed);
                Assert(lockManager.GearShifter != null, "VRGearShifter debe encontrarse en Car09", ref total, ref passed);
                Assert(lockManager.Handbrake != null, "VRHandbrake debe encontrarse en Car09", ref total, ref passed);
                Assert(lockManager.StartButtonControl != null, "StartButton debe encontrarse en Car09", ref total, ref passed);
                Assert(lockManager.HazardButton != null, "HazardButton (VRToggleButton) debe encontrarse en Car09", ref total, ref passed);
                Assert(lockManager.TurnSignal != null, "VRTurnSignal debe encontrarse en Car09", ref total, ref passed);
                Assert(lockManager.HeadlightKnob != null, "VRHeadlightKnob debe encontrarse en Car09", ref total, ref passed);

                // Probar bloqueo selectivo de la Lección 1
                lockManager.ApplyAllowedControls(CockpitControlFlags.SteeringWheel);

                // El volante DEBE permanecer libre e interactivo
                XRBaseInteractable wheelInteractable = lockManager.GetInteractable(CockpitControlFlags.SteeringWheel);
                Assert(wheelInteractable != null && wheelInteractable.interactionLayers.value != 0,
                    "VRSteeringWheel DEBE permanecer interactivo en la Lección 1 (interactionLayers != 0)", ref total, ref passed);

                // Todos los demás controles DEBEN estar bloqueados (interactionLayers == 0)
                XRBaseInteractable shifterInteractable = lockManager.GetInteractable(CockpitControlFlags.GearShifter);
                Assert(shifterInteractable != null && shifterInteractable.interactionLayers.value == 0,
                    "VRGearShifter DEBE estar bloqueado en la Lección 1 (interactionLayers == 0)", ref total, ref passed);

                XRBaseInteractable handbrakeInteractable = lockManager.GetInteractable(CockpitControlFlags.Handbrake);
                Assert(handbrakeInteractable != null && handbrakeInteractable.interactionLayers.value == 0,
                    "VRHandbrake DEBE estar bloqueado en la Lección 1 (interactionLayers == 0)", ref total, ref passed);

                XRBaseInteractable startInteractable = lockManager.GetInteractable(CockpitControlFlags.StartButton);
                Assert(startInteractable != null && startInteractable.interactionLayers.value == 0,
                    "StartButton DEBE estar bloqueado en la Lección 1 (interactionLayers == 0)", ref total, ref passed);

                XRBaseInteractable hazardInteractable = lockManager.GetInteractable(CockpitControlFlags.HazardButton);
                Assert(hazardInteractable != null && hazardInteractable.interactionLayers.value == 0,
                    "HazardButton DEBE estar bloqueado en la Lección 1 (interactionLayers == 0)", ref total, ref passed);

                XRBaseInteractable turnInteractable = lockManager.GetInteractable(CockpitControlFlags.TurnSignal);
                Assert(turnInteractable != null && turnInteractable.interactionLayers.value == 0,
                    "VRTurnSignal DEBE estar bloqueado en la Lección 1 (interactionLayers == 0)", ref total, ref passed);

                XRBaseInteractable knobInteractable = lockManager.GetInteractable(CockpitControlFlags.HeadlightKnob);
                Assert(knobInteractable != null && knobInteractable.interactionLayers.value == 0,
                    "VRHeadlightKnob DEBE estar bloqueado en la Lección 1 (interactionLayers == 0)", ref total, ref passed);
            }

            // LessonHUDPanel
            LessonHUDPanel hudPanel = car09.GetComponentInChildren<LessonHUDPanel>(true);
            Assert(hudPanel != null, "LessonHUDPanel debe existir como hijo en la cabina de Car09", ref total, ref passed);

            if (hudPanel != null)
            {
                Canvas hudCanvas = hudPanel.GetComponent<Canvas>();
                Assert(hudCanvas != null && hudCanvas.renderMode == RenderMode.WorldSpace,
                    "El HUD debe ser un Canvas en World Space", ref total, ref passed);

                GraphicRaycaster gr = hudPanel.GetComponent<GraphicRaycaster>();
                Assert(gr != null, "El HUD debe incluir GraphicRaycaster", ref total, ref passed);

                TrackedDeviceGraphicRaycaster tdgr = hudPanel.GetComponent<TrackedDeviceGraphicRaycaster>();
                Assert(tdgr != null, "El HUD debe incluir TrackedDeviceGraphicRaycaster para interacción VR", ref total, ref passed);

                Assert(hudPanel.gameObject.layer == 5, "El Canvas del HUD debe estar en la capa UI (Layer 5)", ref total, ref passed);

                Assert(hudPanel.LessonTitleText != null, "hudPanel.LessonTitleText debe estar asignado", ref total, ref passed);
                Assert(hudPanel.StepProgressText != null, "hudPanel.StepProgressText debe estar asignado", ref total, ref passed);
                Assert(hudPanel.InstructionText != null, "hudPanel.InstructionText debe estar asignado", ref total, ref passed);
                Assert(hudPanel.ContinueButton != null, "hudPanel.ContinueButton debe estar asignado", ref total, ref passed);
                Assert(hudPanel.CompletionPanel != null, "hudPanel.CompletionPanel debe estar asignado", ref total, ref passed);
            Assert(hudPanel.NextLessonButton != null, "hudPanel.NextLessonButton debe estar asignado", ref total, ref passed);

                // REGLA CLAVE 1: NO debe haber panel de ajuste ni sliders embebidos en el HUD
                bool hasSeatPanel = hudPanel.transform.Find("StepContentPanel/SeatAdjustmentPanel") != null;
                Assert(!hasSeatPanel, "LessonHUDPanel NO debe poseer subpanel de sliders de asiento embebidos", ref total, ref passed);
                Slider[] embeddedSliders = hudPanel.GetComponentsInChildren<Slider>(true);
                Assert(embeddedSliders.Length == 0, $"LessonHUDPanel NO debe contener sliders embebidos (encontrados: {embeddedSliders.Length})", ref total, ref passed);

                // REGLA CLAVE 2: NearFarInteractors y WorldCamera asignados para Raycasting
                Assert(hudPanel.FarCastInteractors != null && hudPanel.FarCastInteractors.Length > 0,
                    $"LessonHUDPanel debe tener NearFarInteractors asignados para far-casting ({hudPanel.FarCastInteractors?.Length ?? 0} encontrados)",
                    ref total, ref passed);
                Assert(hudPanel.WorldCamera != null, "LessonHUDPanel debe tener worldCamera asignada", ref total, ref passed);

                // Validar que SetFarCasting active enableFarCasting y restrinja el rayo a la capa UI
                int uiMask = LayerMask.GetMask("UI");
                var originalEnable = new System.Collections.Generic.List<bool>();
                var originalMask = new System.Collections.Generic.List<int>();
                var farInteractors = new System.Collections.Generic.List<NearFarInteractor>();
                if (hudPanel.FarCastInteractors != null)
                {
                    foreach (var interactor in hudPanel.FarCastInteractors)
                    {
                        if (interactor == null) continue;
                        farInteractors.Add(interactor);
                        originalEnable.Add(interactor.enableFarCasting);
                        var c = interactor.farInteractionCaster as CurveInteractionCaster;
                        originalMask.Add(c != null ? c.raycastMask.value : 0);
                    }
                }

                bool hudWasActive = hudPanel.FarCastingActive;
                hudPanel.SetFarCasting(false); // partir de un estado limpio para comparar contra los valores originales
                originalEnable.Clear();
                originalMask.Clear();
                foreach (var interactor in farInteractors)
                {
                    originalEnable.Add(interactor.enableFarCasting);
                    var c = interactor.farInteractionCaster as CurveInteractionCaster;
                    originalMask.Add(c != null ? c.raycastMask.value : 0);
                }

                hudPanel.SetFarCasting(true);
                hudPanel.SetFarCasting(true); // idempotente
                bool allEnabled = farInteractors.Count > 0;
                bool allUiOnly = farInteractors.Count > 0;
                foreach (var interactor in farInteractors)
                {
                    if (!interactor.enableFarCasting) allEnabled = false;
                    var caster = interactor.farInteractionCaster as CurveInteractionCaster;
                    if (caster == null || caster.raycastMask.value != uiMask) allUiOnly = false;
                }
                Assert(allEnabled, "SetFarCasting(true) debe habilitar enableFarCasting en los interactores de VR", ref total, ref passed);
                Assert(allUiOnly, "SetFarCasting(true) debe restringir el raycastMask del far caster exactamente a la capa UI", ref total, ref passed);

                VRSteeringWheel wheelForMask = car09.GetComponentInChildren<VRSteeringWheel>(true);
                if (wheelForMask != null)
                {
                    int wheelLayerBit = 1 << wheelForMask.gameObject.layer;
                    bool excludesCabin = farInteractors.Count > 0;
                    foreach (var interactor in farInteractors)
                    {
                        var caster = interactor.farInteractionCaster as CurveInteractionCaster;
                        if (caster == null || (caster.raycastMask.value & wheelLayerBit) != 0) excludesCabin = false;
                    }
                    Assert(excludesCabin, "Con el HUD activo, el rayo a distancia no debe incluir la capa de los interactuables de cabina (VRSteeringWheel)", ref total, ref passed);
                }

                hudPanel.SetFarCasting(false);
                bool restored = true;
                for (int i = 0; i < farInteractors.Count; i++)
                {
                    var caster = farInteractors[i].farInteractionCaster as CurveInteractionCaster;
                    if (farInteractors[i].enableFarCasting != originalEnable[i]) restored = false;
                    if (caster != null && caster.raycastMask.value != originalMask[i]) restored = false;
                }
                Assert(restored, "SetFarCasting(false) debe restaurar enableFarCasting y raycastMask originales", ref total, ref passed);

                // El rayo visible: cada interactor debe tener un CurveVisualController localizable con su LineRenderer
                bool allVisuals = farInteractors.Count > 0;
                var visualEnabled = new System.Collections.Generic.List<bool>();
                var lineEnabled = new System.Collections.Generic.List<bool>();
                var visuals = new System.Collections.Generic.List<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>();
                foreach (var interactor in farInteractors)
                {
                    UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController found = null;
                    foreach (var v in interactor.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>(true))
                    {
                        if ((UnityEngine.Object)v.curveInteractionDataProvider == interactor) { found = v; break; }
                    }
                    LineRenderer lr = found != null ? found.GetComponentInChildren<LineRenderer>(true) : null;
                    if (found == null || lr == null) { allVisuals = false; continue; }
                    visuals.Add(found);
                    visualEnabled.Add(found.enabled);
                    lineEnabled.Add(lr.enabled);
                }
                Assert(allVisuals, "Cada NearFarInteractor del HUD debe tener un CurveVisualController (con LineRenderer) localizable", ref total, ref passed);

                // Tras SetFarCasting(true)/(false) el visual y el LineRenderer deben volver a su estado original
                hudPanel.SetFarCasting(true);
                hudPanel.SetFarCasting(false);
                bool visualsRestored = true;
                for (int i = 0; i < visuals.Count; i++)
                {
                    LineRenderer lr = visuals[i].GetComponentInChildren<LineRenderer>(true);
                    if (visuals[i].enabled != visualEnabled[i] || lr.enabled != lineEnabled[i]) visualsRestored = false;
                }
                Assert(visualsRestored, "SetFarCasting(false) debe restaurar el enabled original del CurveVisualController y del LineRenderer", ref total, ref passed);

                // Se deja el estado del HUD como estaba antes de esta verificación
                if (hudWasActive) hudPanel.SetFarCasting(true);

                // Comprobar posición local por encima del volante
                VRSteeringWheel wheel = car09.GetComponentInChildren<VRSteeringWheel>(true);
                if (wheel != null)
                {
                    float hudLocalY = hudPanel.transform.localPosition.y;
                    float wheelLocalY = wheel.transform.localPosition.y;
                    Assert(hudLocalY > wheelLocalY,
                        $"La posición local Y del HUD ({hudLocalY:F3}) debe estar por encima del centro del volante ({wheelLocalY:F3})",
                        ref total, ref passed);
                }
            }

            // InGameMenuController y InGameMenu_Canvas
            InGameMenuController menuController = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>();
            Assert(menuController != null, "InGameMenuController debe existir en la escena de la lección", ref total, ref passed);

            GameObject menuCanvas = GameObject.Find("InGameMenu_Canvas");
            Assert(menuCanvas != null, "InGameMenu_Canvas debe existir en la escena de la lección", ref total, ref passed);
            if (menuCanvas != null)
            {
                TrackedDeviceGraphicRaycaster tdgrMenu = menuCanvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                Assert(tdgrMenu != null, "InGameMenu_Canvas debe incluir TrackedDeviceGraphicRaycaster para interacción VR", ref total, ref passed);
            }

            // EventSystem con XRUIInputModule
            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            Assert(eventSystem != null, "EventSystem debe existir en la escena", ref total, ref passed);
            if (eventSystem != null)
            {
                XRUIInputModule xrModule = eventSystem.GetComponent<XRUIInputModule>();
                Assert(xrModule != null, "EventSystem debe contar con XRUIInputModule configurado", ref total, ref passed);
            }

            // LessonManager
            LessonManager lessonManager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            Assert(lessonManager != null, "LessonManager debe existir en la escena de la lección", ref total, ref passed);
            if (lessonManager != null)
            {
                Assert(lessonManager.ActiveLesson != null, "LessonManager.ActiveLesson debe estar asignado", ref total, ref passed);
                Assert(lessonManager.LockManager != null, "LessonManager.LockManager debe estar asignado", ref total, ref passed);
                Assert(lessonManager.Highlighter != null, "LessonManager.Highlighter debe estar asignado", ref total, ref passed);
                Assert(lessonManager.HUDPanel != null, "LessonManager.HUDPanel debe estar asignado", ref total, ref passed);
            }

            // Ensamble de retrovisores en Car09 (Paso 3)
            VRRearviewMirror mirrorComponent = car09.GetComponentInChildren<VRRearviewMirror>(true);
            Assert(mirrorComponent != null, "VRRearviewMirror debe existir en los componentes hijos de Car09", ref total, ref passed);

            Transform rearviewMount = null;
            Transform rearviewCasing = null;
            foreach (Transform t in car09.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "RearviewMirror_Mount") rearviewMount = t;
                if (t.name == "RearviewMirror_Casing") rearviewCasing = t;
            }
            Assert(rearviewMount != null, "RearviewMirror_Mount debe existir en Car09", ref total, ref passed);
            Assert(rearviewCasing != null, "RearviewMirror_Casing debe existir en Car09", ref total, ref passed);

            Transform casingMesh = rearviewCasing != null ? rearviewCasing.Find("Casing_Mesh") : null;
            Assert(casingMesh != null, "Casing_Mesh debe existir como hijo de RearviewMirror_Casing", ref total, ref passed);
            if (casingMesh != null)
            {
                Renderer casingRenderer = casingMesh.GetComponent<Renderer>();
                Assert(casingRenderer != null, "Casing_Mesh debe tener un componente Renderer para el brillo pulsante", ref total, ref passed);
            }

            // Verificación del resaltado visual y su transición entre pasos
            InteractableHighlighter highlighter = car09.GetComponent<InteractableHighlighter>();
            Assert(highlighter != null, "InteractableHighlighter debe existir en Car09", ref total, ref passed);

            if (lessonManager != null && highlighter != null && lessonManager.ActiveLesson != null)
            {
                lessonManager.StartLesson(lessonManager.ActiveLesson);

                // Paso 1: Sin resaltado visual
                Assert(!highlighter.IsHighlighting && highlighter.CurrentTarget == null,
                    "En Paso 1 no debe haber objetivo resaltado", ref total, ref passed);

                // Paso 2: Volante resaltado
                lessonManager.GoToStep(1);
                Assert(highlighter.IsHighlighting, "En Paso 2 el resaltado debe estar activo", ref total, ref passed);
                Assert(highlighter.CurrentTarget != null && highlighter.CurrentTarget.name == "SteeringWheel_Pivot",
                    $"En Paso 2 el objetivo resaltado debe ser SteeringWheel_Pivot (actual: {highlighter.CurrentTarget?.name})", ref total, ref passed);

                // Paso 3: Tres espejos retrovisores resaltados simultáneamente y volante apagado
                lessonManager.GoToStep(2);
                Assert(highlighter.IsHighlighting, "En Paso 3 el resaltado debe estar activo", ref total, ref passed);
                Assert(highlighter.CurrentTarget != null && highlighter.CurrentTarget.name != "SteeringWheel_Pivot",
                    "En Paso 3 el volante ya NO debe permanecer resaltado", ref total, ref passed);
                Assert(highlighter.CurrentTargets != null && highlighter.CurrentTargets.Count == 3,
                    $"En Paso 3 deben estar resaltados los 3 espejos retrovisores simultáneamente (actual: {highlighter.CurrentTargets?.Count ?? 0})", ref total, ref passed);
                Assert(System.Linq.Enumerable.Any(highlighter.CurrentTargets, t => t != null && (t.name == "Casing_Mesh" || t.name == "RearviewMirror_Casing")),
                    "En Paso 3 la carcasa del retrovisor central (Casing_Mesh / RearviewMirror_Casing) debe estar entre los objetivos resaltados", ref total, ref passed);
                Assert(System.Linq.Enumerable.Any(highlighter.CurrentTargets, t => t != null && (t.name == "Mirror_Housing" || t.name == "SideMirror_Left")),
                    "En Paso 3 el soporte del espejo izquierdo (Mirror_Housing / SideMirror_Left) debe estar entre los objetivos resaltados", ref total, ref passed);
                Assert(System.Linq.Enumerable.Any(highlighter.CurrentTargets, t => t != null && (t.name == "Mirror_Housing" || t.name == "SideMirror_Right")),
                    "En Paso 3 el soporte del espejo derecho (Mirror_Housing / SideMirror_Right) debe estar entre los objetivos resaltados", ref total, ref passed);

                // Exclusión estricta de cristal reflectante (Mirror_Glass) y cámaras
                Assert(!highlighter.TargetRenderers.Any(r => r != null && r.gameObject.name == "Mirror_Glass"),
                    "En Paso 3 los cristales (Mirror_Glass) NO deben ser resaltados", ref total, ref passed);
                Assert(highlighter.TargetRenderers.Any(r => r != null && (r.gameObject.name == "Mirror_Housing" || r.gameObject.name == "Casing_Mesh")),
                    "En Paso 3 los soportes/carcasas físicas (Mirror_Housing y Casing_Mesh) deben emitir brillo pulsante", ref total, ref passed);

                // Limpieza final
                highlighter.ClearHighlight();
                Assert(!highlighter.IsHighlighting && highlighter.CurrentTarget == null,
                    "ClearHighlight() debe apagar el resaltado y limpiar CurrentTarget", ref total, ref passed);
            }
        }

        // =========================================================================
        // 3. Verificación de Cero APIs Obsoletas (Regla de AGENTS.md)
        // =========================================================================

        private static void VerifyNoObsoleteAPIs(ref int total, ref int passed)
        {
            Debug.Log("--- 3. Verificación de Reglas del Proyecto (Cero APIs Obsoletas) ---");

            string[] directoriesToScan = new string[]
            {
                "Assets/Scripts/Lessons",
                "Assets/Scripts/Testing/Editor"
            };

            int obsoleteCount = 0;
            Regex obsoleteRegex = new Regex(@"\bObject\.FindFirstObjectByType\b|\bFindFirstObjectByType\b");

            foreach (string dir in directoriesToScan)
            {
                if (!Directory.Exists(dir)) continue;

                string[] csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                foreach (string file in csFiles)
                {
                    // Excluir metadatos y este archivo de pruebas (que contiene el nombre de la API en strings y regex)
                    if (file.EndsWith(".meta")) continue;
                    if (file.EndsWith("Verification.cs")) continue;

                    string content = File.ReadAllText(file);
                    MatchCollection matches = obsoleteRegex.Matches(content);
                    if (matches.Count > 0)
                    {
                        Debug.LogError($"[Lesson01Verification] ❌ Se encontró API obsoleta 'FindFirstObjectByType' en: {file}");
                        obsoleteCount += matches.Count;
                    }
                }
            }

            Assert(obsoleteCount == 0,
                $"CERO llamadas a FindFirstObjectByType en scripts creados/modificados (encontradas: {obsoleteCount})",
                ref total, ref passed);
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
                LastFailures.Add(testName);
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
