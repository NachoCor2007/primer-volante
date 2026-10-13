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
                            Assert(step.allowedControls == CockpitControlFlags.SteeringWheel,
                                $"En la Lección 1, el paso {i + 1} solo debe permitir SteeringWheel (tiene: {step.allowedControls})", ref total, ref passed);

                            if (i == 0)
                            {
                                bool mentionsMenu = step.instructionText.Contains("Menu");
                                bool mentionsContinue = step.instructionText.Contains("Continuar");
                                Assert(mentionsMenu && mentionsContinue, "El Paso 1 debe instruir al conductor a usar el botón Menu y presionar 'Continuar'", ref total, ref passed);
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
                                bool mentionsMirrors = step.instructionText.Contains("espejos retrovisores");
                                bool mentionsFinalize = step.instructionText.Contains("Finalizar Calibración");
                                Assert(mentionsMirrors && mentionsFinalize, "El Paso 3 debe instruir a verificar espejos retrovisores y presionar 'Finalizar Calibración'", ref total, ref passed);
                                Assert(step.highlightTargetName == "VRRearviewMirror", "El Paso 3 debe tener highlightTargetName 'VRRearviewMirror'", ref total, ref passed);
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
            GameObject car08 = GameObject.Find("Car08");
            Assert(car08 != null, "GameObject 'Car08' debe existir en la escena", ref total, ref passed);
            if (car08 == null) return;

            // CockpitControlLockManager
            CockpitControlLockManager lockManager = car08.GetComponent<CockpitControlLockManager>();
            Assert(lockManager != null, "CockpitControlLockManager debe estar adjunto a Car08", ref total, ref passed);

            if (lockManager != null)
            {
                lockManager.AutoDiscoverControls(car08.transform);

                // Comprobar que los mandos físicos existan
                Assert(lockManager.SteeringWheel != null, "VRSteeringWheel debe encontrarse en Car08", ref total, ref passed);
                Assert(lockManager.GearShifter != null, "VRGearShifter debe encontrarse en Car08", ref total, ref passed);
                Assert(lockManager.Handbrake != null, "VRHandbrake debe encontrarse en Car08", ref total, ref passed);
                Assert(lockManager.StartButtonControl != null, "StartButton debe encontrarse en Car08", ref total, ref passed);
                Assert(lockManager.HazardButton != null, "HazardButton (VRToggleButton) debe encontrarse en Car08", ref total, ref passed);
                Assert(lockManager.TurnSignal != null, "VRTurnSignal debe encontrarse en Car08", ref total, ref passed);
                Assert(lockManager.HeadlightKnob != null, "VRHeadlightKnob debe encontrarse en Car08", ref total, ref passed);

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
            LessonHUDPanel hudPanel = car08.GetComponentInChildren<LessonHUDPanel>(true);
            Assert(hudPanel != null, "LessonHUDPanel debe existir como hijo en la cabina de Car08", ref total, ref passed);

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

                // Validar que SetFarCasting active enableFarCasting en los interactors
                hudPanel.SetFarCasting(true);
                bool anyFarCastingEnabled = false;
                if (hudPanel.FarCastInteractors != null)
                {
                    foreach (var interactor in hudPanel.FarCastInteractors)
                    {
                        if (interactor != null && interactor.enableFarCasting)
                        {
                            anyFarCastingEnabled = true;
                            break;
                        }
                    }
                }
                Assert(anyFarCastingEnabled, "SetFarCasting(true) debe habilitar enableFarCasting en los interactores de VR", ref total, ref passed);

                // Comprobar posición local por encima del volante
                VRSteeringWheel wheel = car08.GetComponentInChildren<VRSteeringWheel>(true);
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

            // Ensamble de retrovisores en Car08 (Paso 3)
            VRRearviewMirror mirrorComponent = car08.GetComponentInChildren<VRRearviewMirror>(true);
            Assert(mirrorComponent != null, "VRRearviewMirror debe existir en los componentes hijos de Car08", ref total, ref passed);

            Transform rearviewMount = null;
            Transform rearviewCasing = null;
            foreach (Transform t in car08.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "RearviewMirror_Mount") rearviewMount = t;
                if (t.name == "RearviewMirror_Casing") rearviewCasing = t;
            }
            Assert(rearviewMount != null, "RearviewMirror_Mount debe existir en Car08", ref total, ref passed);
            Assert(rearviewCasing != null, "RearviewMirror_Casing debe existir en Car08", ref total, ref passed);

            Transform casingMesh = rearviewCasing != null ? rearviewCasing.Find("Casing_Mesh") : null;
            Assert(casingMesh != null, "Casing_Mesh debe existir como hijo de RearviewMirror_Casing", ref total, ref passed);
            if (casingMesh != null)
            {
                Renderer casingRenderer = casingMesh.GetComponent<Renderer>();
                Assert(casingRenderer != null, "Casing_Mesh debe tener un componente Renderer para el brillo pulsante", ref total, ref passed);
            }

            // Verificación del resaltado visual y su transición entre pasos
            InteractableHighlighter highlighter = car08.GetComponent<InteractableHighlighter>();
            Assert(highlighter != null, "InteractableHighlighter debe existir en Car08", ref total, ref passed);

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

                // Paso 3: Retrovisor resaltado y volante apagado
                lessonManager.GoToStep(2);
                Assert(highlighter.IsHighlighting, "En Paso 3 el resaltado debe estar activo", ref total, ref passed);
                Assert(highlighter.CurrentTarget != null && highlighter.CurrentTarget.name != "SteeringWheel_Pivot",
                    "En Paso 3 el volante ya NO debe permanecer resaltado", ref total, ref passed);
                Assert(highlighter.CurrentTarget != null && highlighter.CurrentTarget.name == "RearviewMirror_Casing",
                    $"En Paso 3 el objetivo resaltado debe ser RearviewMirror_Casing (actual: {highlighter.CurrentTarget?.name})", ref total, ref passed);

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
                    if (file.EndsWith("Lesson01Verification.cs")) continue;

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
