using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada para el currículum data-driven completo (Lecciones 1 a 4)
    /// y el resaltado multiobjetivo de espejos retrovisores en el simulador "Primer Volante".
    /// Valida la integridad de ScriptableObjects, la coherencia de controles y objetivos de resaltado,
    /// la reactividad del HUD, la navegación secuencial y el cumplimiento de la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class LessonsCurriculumVerification
    {
        public static readonly List<string> LastFailures = new List<string>();

        [MenuItem("Tools/Primer Volante/Lessons/Verify All Lessons Curriculum")]
        public static bool RunVerification()
        {
            LastFailures.Clear();
            Debug.Log("=================================================================");
            Debug.Log("🧪 [LessonsCurriculumVerification] Iniciando verificación de Currículum Completo...");
            Debug.Log("=================================================================");

            int totalTests = 0;
            int passedTests = 0;

            try
            {
                // 1. Verificación de Integridad de los ScriptableObjects (Lecciones 1 a 4)
                VerifyCurriculumScriptableObjects(ref totalTests, ref passedTests);

                // 2. Verificación de Coherencia de Controles y Resaltado en Steps
                VerifyStepControlsAndHighlighting(ref totalTests, ref passedTests);

                // 3. Verificación en Escena: Resaltado Multiobjetivo de los 3 Espejos
                VerifyMultiMirrorHighlightingInScene(ref totalTests, ref passedTests);

                // 4. Verificación en Escena: Progresión de Lecciones y Navegación en HUD
                VerifyCurriculumNavigationInScene(ref totalTests, ref passedTests);

                // 5. Verificación de Reset del Vehículo a "Base 0" entre Lecciones
                VerifyVehicleResetToBaseState(ref totalTests, ref passedTests);

                // 6. Verificación de Monitor de Acciones en Tiempo Real y HUD Feedback
                VerifyActionDrivenProgressionAndFeedback(ref totalTests, ref passedTests);

                // 7. Verificación de Sincronización del Jugador con el Asiento (DriverSeatFollower)
                VerifyDriverSeatFollowerInScene(ref totalTests, ref passedTests);

                // 8. Verificación de Cero APIs Obsoletas (Regla de AGENTS.md)
                VerifyNoObsoleteAPIs(ref totalTests, ref passedTests);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LessonsCurriculumVerification] 💥 Excepción no controlada durante las pruebas: {ex}");
            }

            Debug.Log("=================================================================");
            bool allPassed = (totalTests > 0 && passedTests == totalTests);
            if (allPassed)
            {
                Debug.Log($"🏁 [LessonsCurriculumVerification] ¡TODAS LAS PRUEBAS PASARON! ({passedTests}/{totalTests} pasadas)");
            }
            else
            {
                Debug.LogError($"🏁 [LessonsCurriculumVerification] PRUEBAS FALLIDAS: ({passedTests}/{totalTests} pasadas)");
                foreach (string fail in LastFailures)
                {
                    Debug.LogError($"   ❌ {fail}");
                }
            }
            Debug.Log("=================================================================");

            return allPassed;
        }

        public static void RunVerificationBatch()
        {
            LessonsCurriculumSetup.SetupAllCurriculum();

            bool pass = RunVerification();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(pass ? 0 : 1);
            }
        }

        // =========================================================================
        // 1. Verificación de Integridad de ScriptableObjects (Lecciones 1 a 4)
        // =========================================================================

        private static void VerifyCurriculumScriptableObjects(ref int total, ref int passed)
        {
            Debug.Log("--- 1. Verificación de Integridad de ScriptableObjects (Lecciones 1 a 4) ---");

            string[] lessonPaths = new[]
            {
                LessonsCurriculumSetup.LESSON_01_PATH,
                LessonsCurriculumSetup.LESSON_02_PATH,
                LessonsCurriculumSetup.LESSON_03_PATH,
                LessonsCurriculumSetup.LESSON_04_PATH
            };

            int[] expectedStepCounts = new[] { 3, 4, 3, 5 };
            string[] expectedIds = new[] { "LESSON_01", "LESSON_02", "LESSON_03", "LESSON_04" };

            for (int i = 0; i < lessonPaths.Length; i++)
            {
                string path = lessonPaths[i];
                LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(path);
                Assert(lesson != null, $"El asset de lección {i + 1} debe existir en {path}", ref total, ref passed);

                if (lesson != null)
                {
                    Assert(lesson.lessonId == expectedIds[i],
                        $"Lección {i + 1} debe tener lessonId '{expectedIds[i]}' (actual: '{lesson.lessonId}')", ref total, ref passed);
                    Assert(!string.IsNullOrEmpty(lesson.lessonTitle),
                        $"Lección {i + 1} debe poseer un título no vacío", ref total, ref passed);
                    Assert(lesson.steps != null && lesson.steps.Count == expectedStepCounts[i],
                        $"Lección {i + 1} debe contener exactamente {expectedStepCounts[i]} pasos (encontrados: {lesson.steps?.Count ?? 0})",
                        ref total, ref passed);

                    if (lesson.steps != null)
                    {
                        for (int s = 0; s < lesson.steps.Count; s++)
                        {
                            LessonStepSO step = lesson.steps[s];
                            Assert(step != null, $"Paso {s + 1} de {expectedIds[i]} no debe ser nulo", ref total, ref passed);
                            if (step != null)
                            {
                                Assert(!string.IsNullOrWhiteSpace(step.stepTitle),
                                    $"Paso {s + 1} de {expectedIds[i]} debe tener stepTitle", ref total, ref passed);
                                Assert(!string.IsNullOrWhiteSpace(step.instructionText),
                                    $"Paso {s + 1} de {expectedIds[i]} debe tener instructionText", ref total, ref passed);
                                Assert(step.completionMode == LessonStepCompletionMode.ActionRequired,
                                    $"Paso {s + 1} de {expectedIds[i]} debe tener completionMode = ActionRequired", ref total, ref passed);
                                Assert(step.requiredAction != LessonActionType.None,
                                    $"Paso {s + 1} de {expectedIds[i]} debe tener requiredAction asignada (actual: {step.requiredAction})", ref total, ref passed);
                                Assert(!step.instructionText.ToLower().Contains("continuar"),
                                    $"Paso {s + 1} de {expectedIds[i]} no debe instruir a presionar 'Continuar'", ref total, ref passed);
                                Assert(!step.instructionText.ToLower().Contains("haz clic"),
                                    $"Paso {s + 1} de {expectedIds[i]} no debe instruir a hacer clic en botones", ref total, ref passed);
                            }
                        }
                    }
                }
            }
        }

        // =========================================================================
        // 2. Verificación de Coherencia de Controles y Resaltado en Steps
        // =========================================================================

        private static void VerifyStepControlsAndHighlighting(ref int total, ref int passed)
        {
            Debug.Log("--- 2. Verificación de Coherencia de Controles y Resaltado en Steps ---");

            // Lección 1
            LessonDefinitionSO l1 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_01_PATH);
            if (l1 != null && l1.steps.Count >= 3)
            {
                Assert(l1.steps[0].allowedControls == CockpitControlFlags.SteeringWheel && l1.steps[0].requiredAction == LessonActionType.OpenCalibrationMenu && string.IsNullOrEmpty(l1.steps[0].highlightTargetName),
                    "Lección 1 Paso 1: SteeringWheel, acción OpenCalibrationMenu y sin resaltado", ref total, ref passed);
                Assert(l1.steps[1].allowedControls == CockpitControlFlags.SteeringWheel && l1.steps[1].requiredAction == LessonActionType.RotateSteeringWheel && l1.steps[1].highlightTargetName == "SteeringWheel_Pivot",
                    "Lección 1 Paso 2: SteeringWheel, acción RotateSteeringWheel y resaltado SteeringWheel_Pivot", ref total, ref passed);
                Assert(l1.steps[2].allowedControls.HasFlag(CockpitControlFlags.Mirrors) && l1.steps[2].requiredAction == LessonActionType.AdjustMirrors && l1.steps[2].highlightTargetName == "Mirrors",
                    "Lección 1 Paso 3: Permite Mirrors, acción AdjustMirrors y resaltado multiobjetivo 'Mirrors'", ref total, ref passed);
                Assert(l1.steps[2].instructionText.Contains("3 espejos") || l1.steps[2].instructionText.Contains("tres espejos"),
                    "Lección 1 Paso 3: Texto debe mencionar los espejos retrovisores", ref total, ref passed);
            }

            // Lección 2
            LessonDefinitionSO l2 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_02_PATH);
            if (l2 != null && l2.steps.Count >= 4)
            {
                Assert(l2.steps[0].allowedControls == (CockpitControlFlags.StartButton | CockpitControlFlags.Handbrake) && l2.steps[0].requiredAction == LessonActionType.PressBrakePedal,
                    "Lección 2 Paso 1: Freno de pie permite StartButton | Handbrake con acción PressBrakePedal", ref total, ref passed);
                Assert(l2.steps[1].allowedControls == CockpitControlFlags.StartButton && l2.steps[1].requiredAction == LessonActionType.StartEngine && l2.steps[1].highlightTargetName == "StartButton",
                    "Lección 2 Paso 2: Encendido permite StartButton, acción StartEngine y resalta 'StartButton'", ref total, ref passed);
                Assert(l2.steps[2].allowedControls == CockpitControlFlags.Handbrake && l2.steps[2].requiredAction == LessonActionType.ReleaseHandbrake && l2.steps[2].highlightTargetName == "VRHandbrake",
                    "Lección 2 Paso 3: Freno de mano permite Handbrake, acción ReleaseHandbrake y resalta 'VRHandbrake'", ref total, ref passed);
                Assert(l2.steps[3].allowedControls == CockpitControlFlags.GearShifter && l2.steps[3].requiredAction == LessonActionType.ShiftToNeutral && l2.steps[3].highlightTargetName == "VRGearShifter",
                    "Lección 2 Paso 4: Selector de marchas permite GearShifter, acción ShiftToNeutral y resalta 'VRGearShifter'", ref total, ref passed);
            }

            // Lección 3
            LessonDefinitionSO l3 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_03_PATH);
            if (l3 != null && l3.steps.Count >= 3)
            {
                Assert(l3.steps[0].allowedControls == CockpitControlFlags.TurnSignal && l3.steps[0].requiredAction == LessonActionType.ActivateTurnSignalLeft && l3.steps[0].highlightTargetName == "VRTurnSignal",
                    "Lección 3 Paso 1: Luces de giro permite TurnSignal, acción ActivateTurnSignalLeft y resalta 'VRTurnSignal'", ref total, ref passed);
                Assert(l3.steps[1].allowedControls == CockpitControlFlags.HazardButton && l3.steps[1].requiredAction == LessonActionType.ActivateHazardLights && l3.steps[1].highlightTargetName == "HazardButton",
                    "Lección 3 Paso 2: Balizas permite HazardButton, acción ActivateHazardLights y resalta 'HazardButton'", ref total, ref passed);
                Assert(l3.steps[2].allowedControls == CockpitControlFlags.HeadlightKnob && l3.steps[2].requiredAction == LessonActionType.CycleHeadlights && l3.steps[2].highlightTargetName == "VRHeadlightKnob",
                    "Lección 3 Paso 3: Perilla de luces permite HeadlightKnob, acción CycleHeadlights y resalta 'VRHeadlightKnob'", ref total, ref passed);
            }

            // Lección 4
            LessonDefinitionSO l4 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_04_PATH);
            if (l4 != null && l4.steps.Count >= 5)
            {
                Assert(l4.steps[0].allowedControls == CockpitControlFlags.All && l4.steps[0].requiredAction == LessonActionType.StartEngine,
                    "Lección 4 Paso 1: Checklist de salida permite CockpitControlFlags.All y acción StartEngine", ref total, ref passed);
                Assert(l4.steps[1].allowedControls == CockpitControlFlags.All && l4.steps[1].requiredAction == LessonActionType.DriveForward && l4.steps[1].highlightTargetName == "VRGearShifter",
                    "Lección 4 Paso 2: Drive permite All, acción DriveForward y resalta 'VRGearShifter'", ref total, ref passed);
                Assert(l4.steps[2].allowedControls == CockpitControlFlags.All && l4.steps[2].requiredAction == LessonActionType.PressBrakePedal,
                    "Lección 4 Paso 3: Frenado progresivo permite All y acción PressBrakePedal", ref total, ref passed);
                Assert(l4.steps[3].allowedControls == CockpitControlFlags.All && l4.steps[3].requiredAction == LessonActionType.DriveReverse && l4.steps[3].highlightTargetName == "VRGearShifter",
                    "Lección 4 Paso 4: Reversa permite All, acción DriveReverse y resalta 'VRGearShifter'", ref total, ref passed);
                Assert(l4.steps[4].allowedControls == CockpitControlFlags.All && l4.steps[4].requiredAction == LessonActionType.ShiftToPark && l4.steps[4].highlightTargetName == "VRHandbrake",
                    "Lección 4 Paso 5: Estacionamiento permite All, acción ShiftToPark y resalta 'VRHandbrake'", ref total, ref passed);
            }
        }

        // =========================================================================
        // 3. Verificación en Escena: Resaltado Multiobjetivo de los 3 Espejos
        // =========================================================================

        private static void VerifyMultiMirrorHighlightingInScene(ref int total, ref int passed)
        {
            Debug.Log("--- 3. Verificación en Escena: Resaltado Multiobjetivo de los 3 Espejos ---");

            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (!currentScene.IsValid() || currentScene.path != LessonsCurriculumSetup.SCENE_PATH)
            {
                currentScene = EditorSceneManager.OpenScene(LessonsCurriculumSetup.SCENE_PATH, OpenSceneMode.Single);
            }

            Assert(currentScene.IsValid() && currentScene.isLoaded,
                $"La escena {LessonsCurriculumSetup.SCENE_PATH} debe cargarse correctamente", ref total, ref passed);

            GameObject car09 = GameObject.Find("Car09");
            Assert(car09 != null, "Car09 debe existir en la escena", ref total, ref passed);
            if (car09 == null) return;

            InteractableHighlighter highlighter = car09.GetComponent<InteractableHighlighter>();
            Assert(highlighter != null, "InteractableHighlighter debe existir en Car09", ref total, ref passed);
            if (highlighter == null) return;

            // Probar HighlightByName("Mirrors")
            highlighter.HighlightByName("Mirrors", car09.transform);
            Assert(highlighter.IsHighlighting, "HighlightByName('Mirrors') debe activar el resaltado", ref total, ref passed);
            Assert(highlighter.CurrentTargets != null && highlighter.CurrentTargets.Count == 3,
                $"HighlightByName('Mirrors') debe registrar exactamente 3 objetivos (actual: {highlighter.CurrentTargets?.Count ?? 0})",
                ref total, ref passed);

            bool hasRearview = highlighter.CurrentTargets.Any(t => t != null && (t.name == "Casing_Mesh" || t.name == "RearviewMirror_Casing"));
            bool hasLeftMirror = highlighter.CurrentTargets.Any(t => t != null && (t.name == "Mirror_Housing" || t.name == "SideMirror_Left"));
            bool hasRightMirror = highlighter.CurrentTargets.Any(t => t != null && (t.name == "Mirror_Housing" || t.name == "SideMirror_Right"));

            Assert(hasRearview, "El soporte del retrovisor central interior (Casing_Mesh / RearviewMirror_Casing) debe estar resaltado", ref total, ref passed);
            Assert(hasLeftMirror, "El soporte del espejo lateral izquierdo (Mirror_Housing) debe estar resaltado", ref total, ref passed);
            Assert(hasRightMirror, "El soporte del espejo lateral derecho (Mirror_Housing) debe estar resaltado", ref total, ref passed);

            // Verificar que existan renderers de carcasa y que los cristales estén excluidos
            Assert(highlighter.TargetRenderers != null && highlighter.TargetRenderers.Count >= 3,
                $"Deben existir renderers en los objetivos de carcasa de espejo (encontrados: {highlighter.TargetRenderers?.Count ?? 0})",
                ref total, ref passed);

            bool hasGlassRenderer = highlighter.TargetRenderers.Any(r => r != null && r.gameObject.name == "Mirror_Glass");
            Assert(!hasGlassRenderer, "Los cristales de los espejos (Mirror_Glass) NO deben incluirse en los renderers a resaltar", ref total, ref passed);
            bool hasHousingRenderer = highlighter.TargetRenderers.Any(r => r != null && (r.gameObject.name == "Mirror_Housing" || r.gameObject.name == "Casing_Mesh"));
            Assert(hasHousingRenderer, "Las carcasas de soporte (Mirror_Housing / Casing_Mesh) deben incluirse en los renderers a resaltar", ref total, ref passed);

            // Probar ClearHighlight
            highlighter.ClearHighlight();
            Assert(!highlighter.IsHighlighting && highlighter.CurrentTargets.Count == 0,
                "ClearHighlight() debe apagar el resaltado y vaciar la lista de objetivos", ref total, ref passed);
        }

        // =========================================================================
        // 4. Verificación en Escena: Progresión de Lecciones y Navegación en HUD
        // =========================================================================

        private static void VerifyCurriculumNavigationInScene(ref int total, ref int passed)
        {
            Debug.Log("--- 4. Verificación en Escena: Progresión de Lecciones y Navegación en HUD ---");

            LessonManager manager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            Assert(manager != null, "LessonManager debe existir en la escena", ref total, ref passed);
            if (manager == null) return;

            LessonHUDPanel hud = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            Assert(hud != null, "LessonHUDPanel debe existir en la escena", ref total, ref passed);
            if (hud == null) return;

            Assert(hud.RestartButton != null, "HUD debe tener asignado RestartButton", ref total, ref passed);
            Assert(hud.NextLessonButton != null, "HUD debe tener asignado NextLessonButton", ref total, ref passed);

            Assert(manager.Curriculum != null && manager.Curriculum.Count == 4,
                $"LessonManager debe tener el currículum de 4 lecciones cargado (actual: {manager.Curriculum?.Count ?? 0})", ref total, ref passed);

            // Iniciar Lección 1
            LessonDefinitionSO l1 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_01_PATH);
            manager.StartLesson(l1);
            Assert(manager.ActiveLesson == l1, "LessonManager debe tener activa la Lección 1", ref total, ref passed);
            Assert(manager.HasNextLesson, "Estando en Lección 1, HasNextLesson debe ser true", ref total, ref passed);
            Assert(manager.GetNextLesson() != null && manager.GetNextLesson().lessonId == "LESSON_02",
                "La siguiente lección a la Lección 1 debe ser LESSON_02", ref total, ref passed);

            // Probar pantalla de compleción de Lección 1
            manager.CompleteLesson();
            Assert(manager.IsLessonCompleted, "La lección debe marcarse como completada", ref total, ref passed);
            Assert(hud.CompletionPanel != null && hud.CompletionPanel.activeSelf,
                "Al completar la lección, el CompletionPanel del HUD debe estar activo", ref total, ref passed);
            Assert(hud.NextLessonButton != null && hud.NextLessonButton.gameObject.activeSelf,
                "Al completar Lección 1, el botón NextLessonButton debe estar visible", ref total, ref passed);

            // Avanzar a la siguiente lección
            manager.StartNextLesson();
            Assert(manager.ActiveLesson != null && manager.ActiveLesson.lessonId == "LESSON_02",
                "StartNextLesson() debe avanzar activamente a la Lección 2", ref total, ref passed);
            Assert(manager.CurrentStepIndex == 0, "Al comenzar Lección 2, el paso debe ser el 0", ref total, ref passed);

            // Reiniciar a Lección 1 para dejar la escena lista
            manager.StartLesson(l1);
        }

        // =========================================================================
        // 5. Verificación de Reset del Vehículo a "Base 0" entre Lecciones
        // =========================================================================

        private static void VerifyVehicleResetToBaseState(ref int total, ref int passed)
        {
            Debug.Log("--- 5. Verificación de Reset del Vehículo a 'Base 0' entre Lecciones ---");

            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (!currentScene.IsValid() || currentScene.path != LessonsCurriculumSetup.SCENE_PATH)
            {
                currentScene = EditorSceneManager.OpenScene(LessonsCurriculumSetup.SCENE_PATH, OpenSceneMode.Single);
            }

            VehicleController vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            Assert(vehicle != null, "VehicleController debe existir en la escena para prueba de ResetToBaseState", ref total, ref passed);
            if (vehicle == null) return;

            // Simular alteraciones en el estado del vehículo
            vehicle.SetEngineRunning(true);
            vehicle.SetGear(GearState.Drive);
            VRHandbrake handbrake = vehicle.GetComponentInChildren<VRHandbrake>(true);
            if (handbrake != null)
            {
                handbrake.SetEngagementImmediate(0f);
            }
            vehicle.SetHeadlightMode(HeadlightMode.LowBeam);
            vehicle.SetHazardActive(true);
            vehicle.SetTurnSignal(TurnSignalState.Left);
            vehicle.transform.position += new Vector3(8f, 0f, 12f);

            // Ejecutar ResetToBaseState()
            vehicle.ResetToBaseState();

            // Verificar condiciones de "Base 0"
            Assert(!vehicle.IsEngineRunning, "ResetToBaseState: Motor debe estar apagado", ref total, ref passed);
            Assert(vehicle.CurrentGear == GearState.Park, "ResetToBaseState: Palanca de cambios debe estar en Park (P)", ref total, ref passed);
            Assert(vehicle.HandbrakeEngagement >= 0.95f, "ResetToBaseState: Freno de mano debe estar totalmente accionado (>= 0.95)", ref total, ref passed);
            Assert(vehicle.CurrentHeadlights == HeadlightMode.Off, "ResetToBaseState: Luces reglamentarias deben estar apagadas (Off)", ref total, ref passed);
            Assert(!vehicle.IsHazardActive, "ResetToBaseState: Balizas de emergencia deben estar inactivas (false)", ref total, ref passed);
            Assert(vehicle.CurrentTurnSignal == TurnSignalState.Off, "ResetToBaseState: Guiños deben estar apagados (Off)", ref total, ref passed);
            Assert(Mathf.Approximately(vehicle.CurrentSpeedMs, 0f), "ResetToBaseState: Velocidad interna debe ser 0", ref total, ref passed);

            Vector3 spawnPos = vehicle.InitialSpawnPosition;
            Assert(Vector3.Distance(vehicle.transform.position, spawnPos) < 0.05f,
                $"ResetToBaseState: Posición del vehículo debe restablecerse al spawn inicial (actual: {vehicle.transform.position}, esperado: {spawnPos})",
                ref total, ref passed);
        }

        // =========================================================================
        // 6. Verificación de Monitor de Acciones en Tiempo Real y HUD Feedback
        // =========================================================================

        private static void VerifyActionDrivenProgressionAndFeedback(ref int total, ref int passed)
        {
            Debug.Log("--- 6. Verificación de Monitor de Acciones en Tiempo Real y HUD Feedback ---");

            LessonManager manager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            Assert(manager != null, "LessonManager debe existir en la escena", ref total, ref passed);
            if (manager == null) return;

            LessonHUDPanel hud = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            Assert(hud != null, "LessonHUDPanel debe existir en la escena", ref total, ref passed);
            if (hud == null) return;

            VehicleController vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            Assert(vehicle != null, "VehicleController debe existir", ref total, ref passed);

            LessonActionMonitor monitor = manager.GetComponent<LessonActionMonitor>();
            if (monitor == null)
            {
                monitor = UnityEngine.Object.FindAnyObjectByType<LessonActionMonitor>();
            }
            if (monitor != null && vehicle != null)
            {
                monitor.VehicleController = vehicle;
            }
            Assert(monitor != null, "LessonActionMonitor debe existir en LessonManager o en la escena", ref total, ref passed);

            // 1. Probar estado del HUD con paso ActionRequired
            LessonDefinitionSO l2 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_02_PATH);
            manager.StartLesson(l2);

            Assert(hud.ContinueButton != null, "HUD debe poseer ContinueButton", ref total, ref passed);
            Assert(!hud.ContinueButton.interactable,
                "En paso con ActionRequired, el botón Continuar del HUD debe estar DESHABILITADO", ref total, ref passed);
            Assert(hud.ContinueButtonText != null && hud.ContinueButtonText.text == "Esperando acción...",
                $"El texto del botón debe indicar 'Esperando acción...' (actual: '{hud.ContinueButtonText?.text}')", ref total, ref passed);

            // 2. Probar evaluación de condiciones en LessonActionMonitor
            if (monitor != null && l2 != null && l2.steps.Count >= 2)
            {
                LessonStepSO engineStep = l2.steps[1]; // Encendido del motor
                vehicle.SetEngineRunning(false);
                Assert(!monitor.IsConditionSatisfied(engineStep),
                    "Monitor: Con motor apagado, condición de StartEngine debe ser false", ref total, ref passed);

                vehicle.SetEngineRunning(true);
                Assert(monitor.IsConditionSatisfied(engineStep),
                    "Monitor: Con motor encendido, condición de StartEngine debe ser true", ref total, ref passed);
            }

            // 3. Probar feedback sensorial en HUD
            Assert(hud.SuccessBorderGlow != null, "LessonHUDPanel debe tener asignado SuccessBorderGlow", ref total, ref passed);
            if (hud.SuccessBorderGlow != null)
            {
                hud.ConfigureHollowBorderGlow();
                Assert(!hud.SuccessBorderGlow.fillCenter, "SuccessBorderGlow debe tener fillCenter == false (marco hueco perimetral)", ref total, ref passed);
                Assert(hud.SuccessBorderGlow.type == UnityEngine.UI.Image.Type.Sliced, "SuccessBorderGlow debe ser Image.Type.Sliced (9-slice)", ref total, ref passed);
                Assert(hud.SuccessBorderGlow.sprite != null, "SuccessBorderGlow debe tener sprite asignado", ref total, ref passed);
            }
            Assert(hud.SuccessBadgeText != null, "LessonHUDPanel debe tener asignado SuccessBadgeText", ref total, ref passed);

            // Probar llamada a sonido de confirmación (chime)
            try
            {
                hud.PlayChimeSound();
                Assert(hud.AudioSource != null, "PlayChimeSound(): AudioSource debe estar configurado y operativo", ref total, ref passed);
            }
            catch (Exception ex)
            {
                Assert(false, $"PlayChimeSound() lanzó excepción: {ex.Message}", ref total, ref passed);
            }

            // Dejar el vehículo reseteado a base 0 y en Lección 1
            if (vehicle != null)
            {
                vehicle.ResetToBaseState();
            }
            LessonDefinitionSO l1 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_01_PATH);
            manager.StartLesson(l1);
        }

        // =========================================================================
        // 7. Verificación de Sincronización del Jugador con el Asiento (DriverSeatFollower)
        // =========================================================================

        private static void VerifyDriverSeatFollowerInScene(ref int total, ref int passed)
        {
            Debug.Log("--- 7. Verificación de Sincronización del Jugador con el Asiento (DriverSeatFollower) ---");

            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (!currentScene.IsValid() || currentScene.path != LessonsCurriculumSetup.SCENE_PATH)
            {
                currentScene = EditorSceneManager.OpenScene(LessonsCurriculumSetup.SCENE_PATH, OpenSceneMode.Single);
            }

            GameObject car09 = GameObject.Find("Car09");
            if (car09 == null)
            {
                foreach (var root in currentScene.GetRootGameObjects())
                {
                    if (root.GetComponentInChildren<VehicleController>(true) != null)
                    {
                        car09 = root;
                        break;
                    }
                }
            }
            Assert(car09 != null, "El vehículo Car09 debe existir en la escena", ref total, ref passed);

            DriverSeatFollower seatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            Assert(seatFollower != null, "DriverSeatFollower debe existir en la escena (en XR Origin)", ref total, ref passed);

            if (seatFollower != null && car09 != null)
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

                // Probar el autodescubrimiento si se desvincula temporalmente
                Transform originalSeat = seatFollower.DriverSeat;
                seatFollower.DriverSeat = null;
                bool discovered = seatFollower.AutoDiscoverDriverSeat();
                Assert(discovered && seatFollower.DriverSeat != null,
                    "AutoDiscoverDriverSeat() debe encontrar y reasignar automáticamente el asiento de Car09 cuando sea nulo", ref total, ref passed);

                // Restaurar asiento original y alinear
                seatFollower.DriverSeat = originalSeat;
                seatFollower.SnapToSeat();
            }
        }

        // =========================================================================
        // 8. Verificación de Cero APIs Obsoletas (Regla de AGENTS.md)
        // =========================================================================

        private static void VerifyNoObsoleteAPIs(ref int total, ref int passed)
        {
            Debug.Log("--- 8. Verificación de Reglas del Proyecto (Cero APIs Obsoletas) ---");

            string[] directoriesToScan = new string[]
            {
                "Assets/Scripts/Lessons",
                "Assets/Scripts/VR",
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
                    // Excluir metadatos y scripts de pruebas que declaran la regex de chequeo
                    if (file.EndsWith(".meta")) continue;
                    if (file.EndsWith("Verification.cs")) continue;

                    string content = File.ReadAllText(file);
                    MatchCollection matches = obsoleteRegex.Matches(content);
                    if (matches.Count > 0)
                    {
                        Debug.LogError($"[LessonsCurriculumVerification] ❌ Se encontró API obsoleta 'FindFirstObjectByType' en: {file}");
                        obsoleteCount += matches.Count;
                    }
                }
            }

            Assert(obsoleteCount == 0,
                $"No deben existir llamadas a 'FindFirstObjectByType' en el código (encontradas: {obsoleteCount}). Usar 'FindAnyObjectByType'.",
                ref total, ref passed);
        }

        private static void Assert(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {message}");
            }
            else
            {
                LastFailures.Add(message);
                Debug.LogError($"  ❌ [FAIL] {message}");
            }
        }
    }
}
