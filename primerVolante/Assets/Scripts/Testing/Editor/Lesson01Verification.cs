using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada en Editor para la arquitectura de lecciones
    /// y la Lección 1 ("Calibración del ambiente de manejo") - TASK-204 / Issue #19.
    /// Valida existencia de escena y asset, bloqueo selectivo de mandos, interactividad
    /// del volante, montaje del HUD sobre el volante y ausencia de APIs obsoletas de Unity.
    /// </summary>
    public static class Lesson01Verification
    {
        [MenuItem("Tools/Primer Volante/Lessons/Verify Lesson 1")]
        public static bool RunVerification()
        {
            Debug.Log("=================================================================");
            Debug.Log("🔍 [Lesson01Verification] Iniciando Verificación de Lección 1...");
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
            Assert(dataAssetExists, $"El asset de datos debe existir en {Lesson01SceneSetup.LESSON_DATA_PATH}", ref total, ref passed);

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Lesson01SceneSetup.LESSON_DATA_PATH);
            Assert(lesson != null, "LessonData_01.asset debe cargarse como LessonDefinitionSO válido", ref total, ref passed);

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
            Debug.Log("--- 2. Verificación de Escena, Mandos y HUD ---");

            Scene scene = EditorSceneManager.OpenScene(Lesson01SceneSetup.TARGET_SCENE_PATH, OpenSceneMode.Single);
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

                // El volante debe permanecer interactivo
                XRBaseInteractable wheelInteractable = lockManager.GetInteractable(CockpitControlFlags.SteeringWheel);
                Assert(wheelInteractable != null && wheelInteractable.interactionLayers.value != 0,
                    "VRSteeringWheel NO debe estar bloqueado (interactionLayers != 0)", ref total, ref passed);

                // Todos los demás mandos deben estar bloqueados (interactionLayers == 0)
                XRBaseInteractable gearInteractable = lockManager.GetInteractable(CockpitControlFlags.GearShifter);
                Assert(gearInteractable != null && gearInteractable.interactionLayers.value == 0,
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

                Assert(hudPanel.LessonTitleText != null, "hudPanel.LessonTitleText debe estar asignado", ref total, ref passed);
                Assert(hudPanel.StepProgressText != null, "hudPanel.StepProgressText debe estar asignado", ref total, ref passed);
                Assert(hudPanel.InstructionText != null, "hudPanel.InstructionText debe estar asignado", ref total, ref passed);
                Assert(hudPanel.ContinueButton != null, "hudPanel.ContinueButton debe estar asignado", ref total, ref passed);
                Assert(hudPanel.SeatAdjustmentPanel != null, "hudPanel.SeatAdjustmentPanel debe estar asignado", ref total, ref passed);
                Assert(hudPanel.CompletionPanel != null, "hudPanel.CompletionPanel debe estar asignado", ref total, ref passed);

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
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
