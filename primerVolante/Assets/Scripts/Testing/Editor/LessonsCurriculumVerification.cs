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

                // 5. Verificación de Cero APIs Obsoletas (Regla de AGENTS.md)
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
                Assert(l1.steps[0].allowedControls == CockpitControlFlags.SteeringWheel && string.IsNullOrEmpty(l1.steps[0].highlightTargetName),
                    "Lección 1 Paso 1: SteeringWheel y sin resaltado", ref total, ref passed);
                Assert(l1.steps[1].allowedControls == CockpitControlFlags.SteeringWheel && l1.steps[1].highlightTargetName == "SteeringWheel_Pivot",
                    "Lección 1 Paso 2: SteeringWheel y resaltado SteeringWheel_Pivot", ref total, ref passed);
                Assert(l1.steps[2].allowedControls == CockpitControlFlags.SteeringWheel && l1.steps[2].highlightTargetName == "Mirrors",
                    "Lección 1 Paso 3: SteeringWheel y resaltado multiobjetivo 'Mirrors'", ref total, ref passed);
                Assert(l1.steps[2].instructionText.Contains("3 espejos") || l1.steps[2].instructionText.Contains("tres espejos"),
                    "Lección 1 Paso 3: Texto debe mencionar los espejos retrovisores", ref total, ref passed);
            }

            // Lección 2
            LessonDefinitionSO l2 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_02_PATH);
            if (l2 != null && l2.steps.Count >= 4)
            {
                Assert(l2.steps[0].allowedControls == (CockpitControlFlags.StartButton | CockpitControlFlags.Handbrake),
                    "Lección 2 Paso 1: Freno de pie / Precondición permite StartButton | Handbrake", ref total, ref passed);
                Assert(l2.steps[1].allowedControls == CockpitControlFlags.StartButton && l2.steps[1].highlightTargetName == "StartButton",
                    "Lección 2 Paso 2: Encendido permite StartButton y resalta 'StartButton'", ref total, ref passed);
                Assert(l2.steps[2].allowedControls == CockpitControlFlags.Handbrake && l2.steps[2].highlightTargetName == "VRHandbrake",
                    "Lección 2 Paso 3: Freno de mano permite Handbrake y resalta 'VRHandbrake'", ref total, ref passed);
                Assert(l2.steps[3].allowedControls == CockpitControlFlags.GearShifter && l2.steps[3].highlightTargetName == "VRGearShifter",
                    "Lección 2 Paso 4: Selector de marchas permite GearShifter y resalta 'VRGearShifter'", ref total, ref passed);
            }

            // Lección 3
            LessonDefinitionSO l3 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_03_PATH);
            if (l3 != null && l3.steps.Count >= 3)
            {
                Assert(l3.steps[0].allowedControls == CockpitControlFlags.TurnSignal && l3.steps[0].highlightTargetName == "VRTurnSignal",
                    "Lección 3 Paso 1: Luces de giro permite TurnSignal y resalta 'VRTurnSignal'", ref total, ref passed);
                Assert(l3.steps[1].allowedControls == CockpitControlFlags.HazardButton && l3.steps[1].highlightTargetName == "HazardButton",
                    "Lección 3 Paso 2: Balizas permite HazardButton y resalta 'HazardButton'", ref total, ref passed);
                Assert(l3.steps[2].allowedControls == CockpitControlFlags.HeadlightKnob && l3.steps[2].highlightTargetName == "VRHeadlightKnob",
                    "Lección 3 Paso 3: Perilla de luces permite HeadlightKnob y resalta 'VRHeadlightKnob'", ref total, ref passed);
            }

            // Lección 4
            LessonDefinitionSO l4 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LessonsCurriculumSetup.LESSON_04_PATH);
            if (l4 != null && l4.steps.Count >= 5)
            {
                Assert(l4.steps[0].allowedControls == CockpitControlFlags.All,
                    "Lección 4 Paso 1: Checklist de salida permite CockpitControlFlags.All", ref total, ref passed);
                Assert(l4.steps[1].allowedControls == CockpitControlFlags.All && l4.steps[1].highlightTargetName == "VRGearShifter",
                    "Lección 4 Paso 2: Drive permite All y resalta 'VRGearShifter'", ref total, ref passed);
                Assert(l4.steps[2].allowedControls == CockpitControlFlags.All,
                    "Lección 4 Paso 3: Frenado progresivo permite All", ref total, ref passed);
                Assert(l4.steps[3].allowedControls == CockpitControlFlags.All && l4.steps[3].highlightTargetName == "VRGearShifter",
                    "Lección 4 Paso 4: Reversa permite All y resalta 'VRGearShifter'", ref total, ref passed);
                Assert(l4.steps[4].allowedControls == CockpitControlFlags.All && l4.steps[4].highlightTargetName == "VRHandbrake",
                    "Lección 4 Paso 5: Estacionamiento permite All y resalta 'VRHandbrake'", ref total, ref passed);
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

            GameObject car08 = GameObject.Find("Car08");
            Assert(car08 != null, "Car08 debe existir en la escena", ref total, ref passed);
            if (car08 == null) return;

            InteractableHighlighter highlighter = car08.GetComponent<InteractableHighlighter>();
            Assert(highlighter != null, "InteractableHighlighter debe existir en Car08", ref total, ref passed);
            if (highlighter == null) return;

            // Probar HighlightByName("Mirrors")
            highlighter.HighlightByName("Mirrors", car08.transform);
            Assert(highlighter.IsHighlighting, "HighlightByName('Mirrors') debe activar el resaltado", ref total, ref passed);
            Assert(highlighter.CurrentTargets != null && highlighter.CurrentTargets.Count == 3,
                $"HighlightByName('Mirrors') debe registrar exactamente 3 objetivos (actual: {highlighter.CurrentTargets?.Count ?? 0})",
                ref total, ref passed);

            bool hasRearview = highlighter.CurrentTargets.Any(t => t != null && t.name == "RearviewMirror_Casing");
            bool hasLeftMirror = highlighter.CurrentTargets.Any(t => t != null && (t.name == "SideMirror_Left" ||
                (t.GetComponent<VRSideMirror>() != null && t.GetComponent<VRSideMirror>().Side == VRSideMirror.MirrorSide.Left)));
            bool hasRightMirror = highlighter.CurrentTargets.Any(t => t != null && (t.name == "SideMirror_Right" ||
                (t.GetComponent<VRSideMirror>() != null && t.GetComponent<VRSideMirror>().Side == VRSideMirror.MirrorSide.Right)));

            Assert(hasRearview, "El retrovisor central interior (RearviewMirror_Casing) debe estar resaltado", ref total, ref passed);
            Assert(hasLeftMirror, "El espejo lateral izquierdo debe estar resaltado", ref total, ref passed);
            Assert(hasRightMirror, "El espejo lateral derecho debe estar resaltado", ref total, ref passed);

            // Verificar que existan renderers de carcasa y de cristal
            Assert(highlighter.TargetRenderers != null && highlighter.TargetRenderers.Count >= 3,
                $"Deben existir renderers en los objetivos de espejo (encontrados: {highlighter.TargetRenderers?.Count ?? 0})",
                ref total, ref passed);

            bool hasGlassRenderer = highlighter.TargetRenderers.Any(r => r != null && r.gameObject.name == "Mirror_Glass");
            Assert(hasGlassRenderer, "Los cristales de los espejos (Mirror_Glass) deben incluirse en los renderers a modular", ref total, ref passed);

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
        // 5. Verificación de Cero APIs Obsoletas (Regla de AGENTS.md)
        // =========================================================================

        private static void VerifyNoObsoleteAPIs(ref int total, ref int passed)
        {
            Debug.Log("--- 5. Verificación de Reglas del Proyecto (Cero APIs Obsoletas) ---");

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
