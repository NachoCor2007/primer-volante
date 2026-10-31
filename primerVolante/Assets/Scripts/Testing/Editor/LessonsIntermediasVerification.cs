using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada para la Lección 5 (Giros e Intersecciones)
    /// y Lección 6 (Maniobras de Estacionamiento y Precisión) del simulador "Primer Volante" (TASK-301 / PRI-46).
    /// Valida:
    /// 1. Integridad de ScriptableObjects y pasos de currículum (Lecciones 5 y 6).
    /// 2. Textos de instrucciones 100% Realidad Virtual (cero menciones a teclado, mouse o mandos estándar).
    /// 3. Apertura limpia y cableado de mandos de cabina en Lesson_05_TurnIntersections.unity.
    /// 4. Detección de giros con anticipación de 20 m y controladores de reintento suave (VRScreenFader).
    /// 5. Apertura limpia, dársenas de estacionamiento, TurnSignalExemptZone y ObstacleCollisionDetector en Lesson_06_ParkingManeuvers.unity.
    /// 6. Cumplimiento estricto de la regla de CERO APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class LessonsIntermediasVerification
    {
        public static readonly List<string> LastFailures = new List<string>();
        public static readonly List<string> LogLines = new List<string>();

        [MenuItem("Tools/Primer Volante/Lessons/Verify Lessons 5 and 6")]
        public static bool RunVerification()
        {
            LastFailures.Clear();
            LogLines.Clear();

            Debug.Log("=================================================================");
            Debug.Log("🧪 [LessonsIntermediasVerification] Iniciando suite de pruebas de Lecciones 5 y 6...");
            Debug.Log("=================================================================");

            int totalTests = 0;
            int passedTests = 0;

            try
            {
                // 1. Verificación de ScriptableObjects de Lección 5
                VerifyLesson05Assets(ref totalTests, ref passedTests);

                // 2. Verificación de ScriptableObjects de Lección 6
                VerifyLesson06Assets(ref totalTests, ref passedTests);

                // 3. Verificación de Escena de Lección 5 (Giros e Intersecciones)
                VerifyLesson05Scene(ref totalTests, ref passedTests);

                // 4. Verificación de Escena de Lección 6 (Estacionamiento y Maniobras)
                VerifyLesson06Scene(ref totalTests, ref passedTests);

                // 5. Verificación de Regla de Cero APIs Obsoletas (AGENTS.md)
                VerifyNoObsoleteAPIs(ref totalTests, ref passedTests);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LessonsIntermediasVerification] 💥 Excepción no controlada durante las pruebas: {ex}");
            }

            Debug.Log("=================================================================");
            bool allPassed = (totalTests > 0 && passedTests == totalTests);
            if (allPassed)
            {
                Debug.Log($"🏁 [LessonsIntermediasVerification] ¡TODAS LAS PRUEBAS PASARON EXITOSAMENTE! ({passedTests}/{totalTests})");
            }
            else
            {
                Debug.LogError($"🏁 [LessonsIntermediasVerification] PRUEBAS FALLIDAS: ({passedTests}/{totalTests} pasadas)");
                foreach (string fail in LastFailures)
                {
                    Debug.LogError($"   ❌ {fail}");
                }
            }
            Debug.Log("=================================================================");

            try
            {
                File.WriteAllLines("verification_lessons_5_and_6_results.txt", LogLines);
            }
            catch { }

            return allPassed;
        }

        public static bool RunVerificationQuickAssets()
        {
            LastFailures.Clear();
            LogLines.Clear();
            int totalTests = 0;
            int passedTests = 0;
            VerifyLesson05Assets(ref totalTests, ref passedTests);
            VerifyLesson06Assets(ref totalTests, ref passedTests);
            VerifyNoObsoleteAPIs(ref totalTests, ref passedTests);
            return totalTests > 0 && passedTests == totalTests;
        }

        public static bool RunVerificationLesson05Scene()
        {
            LastFailures.Clear();
            LogLines.Clear();
            int totalTests = 0;
            int passedTests = 0;
            VerifyLesson05Scene(ref totalTests, ref passedTests);
            return totalTests > 0 && passedTests == totalTests;
        }

        public static bool RunVerificationLesson06Scene()
        {
            LastFailures.Clear();
            LogLines.Clear();
            int totalTests = 0;
            int passedTests = 0;
            VerifyLesson06Scene(ref totalTests, ref passedTests);
            return totalTests > 0 && passedTests == totalTests;
        }

        public static void RunVerificationBatch()
        {
            // Ejecutar setups idempotentes previos
            Lesson05TurnIntersectionsSetup.SetupLesson05();
            Lesson06ParkingSetup.SetupLesson06();

            bool pass = RunVerification();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(pass ? 0 : 1);
            }
        }

        // =========================================================================
        // 1. Verificación de ScriptableObjects de Lección 5
        // =========================================================================

        private static void VerifyLesson05Assets(ref int total, ref int passed)
        {
            Debug.Log("--- 1. Verificación de ScriptableObjects de Lección 5 (Giros e Intersecciones) ---");

            LessonDefinitionSO l5 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Lesson05TurnIntersectionsSetup.LESSON_05_PATH);
            Assert(l5 != null, $"Asset LessonData_05.asset debe existir en {Lesson05TurnIntersectionsSetup.LESSON_05_PATH}", ref total, ref passed);

            if (l5 != null)
            {
                Assert(l5.lessonId == "LESSON_05", $"LessonData_05 debe tener lessonId 'LESSON_05' (actual: '{l5.lessonId}')", ref total, ref passed);
                Assert(!string.IsNullOrEmpty(l5.lessonTitle), "LessonData_05 debe tener lessonTitle no vacío", ref total, ref passed);
                Assert(l5.steps != null && l5.steps.Count == 5, $"LessonData_05 debe contener exactamente 5 pasos (encontrados: {l5.steps?.Count ?? 0})", ref total, ref passed);

                LessonActionType[] expectedActions = new[]
                {
                    LessonActionType.CompleteTurnSignalRightIntro,
                    LessonActionType.TurnSignalAnticipation20m,
                    LessonActionType.CrossTrafficLightIntersection,
                    LessonActionType.CompleteLeftTurnCrossing,
                    LessonActionType.CurbsideStop
                };

                for (int s = 0; s < (l5.steps != null ? l5.steps.Count : 0); s++)
                {
                    LessonStepSO step = l5.steps[s];
                    Assert(step != null, $"Paso {s + 1} de Lección 5 no debe ser nulo", ref total, ref passed);
                    if (step != null)
                    {
                        Assert(!string.IsNullOrWhiteSpace(step.stepTitle), $"Paso {s + 1} de Lección 5 debe tener stepTitle", ref total, ref passed);
                        Assert(!string.IsNullOrWhiteSpace(step.instructionText), $"Paso {s + 1} de Lección 5 debe tener instructionText", ref total, ref passed);
                        Assert(step.completionMode == LessonStepCompletionMode.ActionRequired, $"Paso {s + 1} de Lección 5 debe ser ActionRequired", ref total, ref passed);
                        Assert(s < expectedActions.Length && step.requiredAction == expectedActions[s],
                            $"Paso {s + 1} de Lección 5 debe tener requiredAction = {expectedActions[s]} (actual: {step.requiredAction})", ref total, ref passed);
                        Assert(step.allowedControls == CockpitControlFlags.All, $"Paso {s + 1} de Lección 5 debe permitir CockpitControlFlags.All", ref total, ref passed);

                        // Validar que no contenga referencias a controles no VR
                        AssertVRTextDiscipline(step.instructionText, $"Lección 5 Paso {s + 1}", ref total, ref passed);
                    }
                }
            }
        }

        // =========================================================================
        // 2. Verificación de ScriptableObjects de Lección 6
        // =========================================================================

        private static void VerifyLesson06Assets(ref int total, ref int passed)
        {
            Debug.Log("--- 2. Verificación de ScriptableObjects de Lección 6 (Estacionamiento y Maniobras) ---");

            LessonDefinitionSO l6 = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Lesson06ParkingSetup.LESSON_06_PATH);
            Assert(l6 != null, $"Asset LessonData_06.asset debe existir en {Lesson06ParkingSetup.LESSON_06_PATH}", ref total, ref passed);

            if (l6 != null)
            {
                Assert(l6.lessonId == "LESSON_06", $"LessonData_06 debe tener lessonId 'LESSON_06' (actual: '{l6.lessonId}')", ref total, ref passed);
                Assert(!string.IsNullOrEmpty(l6.lessonTitle), "LessonData_06 debe tener lessonTitle no vacío", ref total, ref passed);
                Assert(l6.steps != null && l6.steps.Count == 5, $"LessonData_06 debe contener exactamente 5 pasos (encontrados: {l6.steps?.Count ?? 0})", ref total, ref passed);

                LessonActionType[] expectedActions = new[]
                {
                    LessonActionType.ActivateHazardLights,
                    LessonActionType.ParkDiagonal45,
                    LessonActionType.ParkBay90Reverse,
                    LessonActionType.ParkParallelRight,
                    LessonActionType.SecureParkDeactivateHazards
                };

                for (int s = 0; s < (l6.steps != null ? l6.steps.Count : 0); s++)
                {
                    LessonStepSO step = l6.steps[s];
                    Assert(step != null, $"Paso {s + 1} de Lección 6 no debe ser nulo", ref total, ref passed);
                    if (step != null)
                    {
                        Assert(!string.IsNullOrWhiteSpace(step.stepTitle), $"Paso {s + 1} de Lección 6 debe tener stepTitle", ref total, ref passed);
                        Assert(!string.IsNullOrWhiteSpace(step.instructionText), $"Paso {s + 1} de Lección 6 debe tener instructionText", ref total, ref passed);
                        Assert(step.completionMode == LessonStepCompletionMode.ActionRequired, $"Paso {s + 1} de Lección 6 debe ser ActionRequired", ref total, ref passed);
                        Assert(s < expectedActions.Length && step.requiredAction == expectedActions[s],
                            $"Paso {s + 1} de Lección 6 debe tener requiredAction = {expectedActions[s]} (actual: {step.requiredAction})", ref total, ref passed);
                        Assert(step.allowedControls == CockpitControlFlags.All, $"Paso {s + 1} de Lección 6 debe permitir CockpitControlFlags.All", ref total, ref passed);

                        // Validar que no contenga referencias a controles no VR
                        AssertVRTextDiscipline(step.instructionText, $"Lección 6 Paso {s + 1}", ref total, ref passed);
                    }
                }
            }
        }

        // =========================================================================
        // 3. Verificación de Escena de Lección 5
        // =========================================================================

        private static void VerifyLesson05Scene(ref int total, ref int passed)
        {
            Debug.Log("--- 3. Verificación de Escena de Lección 5 (Lesson_05_TurnIntersections.unity) ---");

            Assert(File.Exists(Lesson05TurnIntersectionsSetup.TARGET_SCENE_PATH),
                $"Archivo de escena {Lesson05TurnIntersectionsSetup.TARGET_SCENE_PATH} debe existir en el proyecto", ref total, ref passed);

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != Lesson05TurnIntersectionsSetup.TARGET_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(Lesson05TurnIntersectionsSetup.TARGET_SCENE_PATH, OpenSceneMode.Single);
            }
            Assert(scene.IsValid() && scene.isLoaded, "Escena de Lección 5 debe abrir limpiamente", ref total, ref passed);

            VehicleController vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            Assert(vehicle != null, "VehicleController (Car09) debe existir en la escena de Lección 5", ref total, ref passed);

            if (vehicle != null)
            {
                TurnSignalDetector turnDetector = vehicle.GetComponent<TurnSignalDetector>();
                Assert(turnDetector != null, "TurnSignalDetector debe estar presente en Car09", ref total, ref passed);
                if (turnDetector != null)
                {
                    int laneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
                    bool hasLaneLayer = (turnDetector.ZoneMask.value & (1 << laneLayer)) != 0;
                    Assert(hasLaneLayer, "TurnSignalDetector.ZoneMask debe contener la capa LaneZone", ref total, ref passed);
                }

                CockpitControlLockManager lockMgr = vehicle.GetComponent<CockpitControlLockManager>();
                Assert(lockMgr != null, "CockpitControlLockManager debe estar presente en Car09", ref total, ref passed);

                LessonHUDPanel hud = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
                Assert(hud != null, "LessonHUDPanel debe estar presente en la escena de Lección 5", ref total, ref passed);
            }

            VRScreenFader fader = UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            Assert(fader != null, "VRScreenFader debe estar presente en la escena de Lección 5", ref total, ref passed);

            LessonManager manager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            Assert(manager != null, "LessonManager debe estar presente en la escena de Lección 5", ref total, ref passed);
            if (manager != null)
            {
                Assert(manager.ActiveLesson != null && manager.ActiveLesson.lessonId == "LESSON_05",
                    "LessonManager debe tener asignada la Lección 5 como ActiveLesson", ref total, ref passed);
            }

            Lesson05TurnIntersectionsController ctrl = UnityEngine.Object.FindAnyObjectByType<Lesson05TurnIntersectionsController>();
            Assert(ctrl != null, "Lesson05TurnIntersectionsController debe estar presente en la escena", ref total, ref passed);

            GameObject warningLine = GameObject.Find("WarningLine_20m_Asphalt");
            Assert(warningLine != null, "WarningLine_20m_Asphalt debe existir en la escena a 20m de la esquina", ref total, ref passed);

            GameObject warningGate = GameObject.Find("WarningGate_20m_Hologram");
            Assert(warningGate != null, "WarningGate_20m_Hologram debe existir como portal holográfico a 20m de la esquina", ref total, ref passed);

            GameObject gpsRoute = GameObject.Find(GPSNavigationSceneSetup.ROUTE_OBJECT_NAME);
            Assert(gpsRoute != null, "GPS_Circuit_Route (SplineContainer) debe existir en la escena de Lección 5 para navegación GPS", ref total, ref passed);
            if (gpsRoute != null)
            {
                var sc = gpsRoute.GetComponent<UnityEngine.Splines.SplineContainer>();
                Assert(sc != null && sc.Splines != null && sc.Splines.Count == 5,
                    $"GPS_Circuit_Route debe contener exactamente 5 splines (una por cada paso) (encontradas: {sc?.Splines?.Count ?? 0})", ref total, ref passed);
            }

            GameObject stopLine = GameObject.Find("StopLine_TrafficLight_Step03");
            Assert(stopLine != null, "StopLine_TrafficLight_Step03 debe existir en la senda peatonal del semáforo para el Paso 3", ref total, ref passed);

            GameObject smallSignal = GameObject.Find("TrafficSignal_Step03_Small");
            Assert(smallSignal != null, "TrafficSignal_Step03_Small debe existir a la altura de los ojos en la senda para el Paso 3", ref total, ref passed);
            if (ctrl != null)
            {
                Assert(ctrl.TrafficLight != null, "Lesson05TurnIntersectionsController debe tener asignado TrafficLight", ref total, ref passed);
                Assert(ctrl.SecondaryTrafficLight != null, "Lesson05TurnIntersectionsController debe tener asignado SecondaryTrafficLight", ref total, ref passed);
            }

            GameObject spawns = GameObject.Find("Lesson05_SpawnPoints");
            Assert(spawns != null && spawns.transform.childCount >= 5, "Lesson05_SpawnPoints debe contener al menos 5 puntos de spawn", ref total, ref passed);
        }

        // =========================================================================
        // 4. Verificación de Escena de Lección 6
        // =========================================================================

        private static void VerifyLesson06Scene(ref int total, ref int passed)
        {
            Debug.Log("--- 4. Verificación de Escena de Lección 6 (Lesson_06_ParkingManeuvers.unity) ---");

            Assert(File.Exists(Lesson06ParkingSetup.TARGET_SCENE_PATH),
                $"Archivo de escena {Lesson06ParkingSetup.TARGET_SCENE_PATH} debe existir en el proyecto", ref total, ref passed);

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != Lesson06ParkingSetup.TARGET_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(Lesson06ParkingSetup.TARGET_SCENE_PATH, OpenSceneMode.Single);
            }
            Assert(scene.IsValid() && scene.isLoaded, "Escena de Lección 6 debe abrir limpiamente", ref total, ref passed);

            VehicleController vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            Assert(vehicle != null, "VehicleController (Car09) debe existir en la escena de Lección 6", ref total, ref passed);

            TurnSignalExemptZone exemptZone = UnityEngine.Object.FindAnyObjectByType<TurnSignalExemptZone>();
            Assert(exemptZone != null, "TurnSignalExemptZone debe existir en el playón de dársenas de Lección 6", ref total, ref passed);
            if (exemptZone != null)
            {
                BoxCollider box = exemptZone.GetComponent<BoxCollider>();
                Assert(box != null && box.isTrigger, "TurnSignalExemptZone debe tener un BoxCollider como trigger", ref total, ref passed);
                int laneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
                Assert(exemptZone.gameObject.layer == laneLayer, $"TurnSignalExemptZone debe estar en la capa LaneZone ({laneLayer})", ref total, ref passed);
            }

            ParkingZoneTrigger[] triggers = UnityEngine.Object.FindObjectsByType<ParkingZoneTrigger>(FindObjectsInactive.Exclude);
            Assert(triggers != null && triggers.Length >= 3,
                $"Deben existir al menos 3 ParkingZoneTriggers (encontrados: {triggers?.Length ?? 0})", ref total, ref passed);

            bool found45 = false, found90 = false, foundParallel = false;
            if (triggers != null)
            {
                foreach (var t in triggers)
                {
                    if (t.StationType == ParkingStationType.Diagonal45) found45 = true;
                    if (t.StationType == ParkingStationType.Bay90Reverse) found90 = true;
                    if (t.StationType == ParkingStationType.ParallelRight) foundParallel = true;
                }
            }
            Assert(found45, "Debe existir la estación de estacionamiento a 45°", ref total, ref passed);
            Assert(found90, "Debe existir la estación de estacionamiento a 90° de reversa", ref total, ref passed);
            Assert(foundParallel, "Debe existir la estación de estacionamiento paralelo al cordón derecho", ref total, ref passed);

            ObstacleCollisionDetector[] obstacleDetectors = UnityEngine.Object.FindObjectsByType<ObstacleCollisionDetector>(FindObjectsInactive.Exclude);
            Assert(obstacleDetectors != null && obstacleDetectors.Length >= 4,
                $"Deben existir obstáculos (conos y autos estáticos) con ObstacleCollisionDetector (encontrados: {obstacleDetectors?.Length ?? 0})",
                ref total, ref passed);

            LessonManager manager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            Assert(manager != null, "LessonManager debe estar presente en la escena de Lección 6", ref total, ref passed);
            if (manager != null)
            {
                Assert(manager.ActiveLesson != null && manager.ActiveLesson.lessonId == "LESSON_06",
                    "LessonManager debe tener asignada la Lección 6 como ActiveLesson", ref total, ref passed);
            }

            Lesson06ParkingController ctrl = UnityEngine.Object.FindAnyObjectByType<Lesson06ParkingController>();
            Assert(ctrl != null, "Lesson06ParkingController debe estar presente en la escena", ref total, ref passed);

            // Validar recuadros amarillos demarcados en el asfalto para guiar visualmente al alumno
            GameObject hl45 = GameObject.Find("GroundVisual_YellowZone_45");
            GameObject hl90 = GameObject.Find("GroundVisual_YellowZone_90");
            GameObject hlParallel = GameObject.Find("GroundVisual_YellowZone_Parallel");
            Assert(hl45 != null, "GroundVisual_YellowZone_45 debe existir en el piso de la dársena a 45°", ref total, ref passed);
            Assert(hl90 != null, "GroundVisual_YellowZone_90 debe existir en el piso de la dársena a 90°", ref total, ref passed);
            Assert(hlParallel != null, "GroundVisual_YellowZone_Parallel debe existir en el piso de la dársena en paralelo", ref total, ref passed);

            // Validar flechas direccionales sobre el asfalto indicando el frente requerido del auto
            GameObject arrow45 = GameObject.Find("GroundVisual_Arrow_45");
            GameObject arrow90 = GameObject.Find("GroundVisual_Arrow_90");
            GameObject arrowParallel = GameObject.Find("GroundVisual_Arrow_Parallel");
            Assert(arrow45 != null, "GroundVisual_Arrow_45 debe existir marcando la dirección del auto en dársena 45°", ref total, ref passed);
            Assert(arrow90 != null, "GroundVisual_Arrow_90 debe existir marcando la dirección de culata hacia la calle en dársena 90°", ref total, ref passed);
            Assert(arrowParallel != null, "GroundVisual_Arrow_Parallel debe existir marcando la dirección de circulación en dársena paralelo", ref total, ref passed);
        }

        // =========================================================================
        // 5. Verificación de Cero APIs Obsoletas (AGENTS.md)
        // =========================================================================

        private static void VerifyNoObsoleteAPIs(ref int total, ref int passed)
        {
            Debug.Log("--- 5. Verificación de Reglas del Proyecto (Cero APIs Obsoletas) ---");

            string[] directoriesToScan = new string[]
            {
                "Assets/Scripts/Lessons",
                "Assets/Scripts/VR",
                "Assets/Scripts/Testing/Editor"
            };

            int obsoleteCount = 0;
            Regex obsoleteRegex = new Regex(@"\bObject\.FindFirstObjectByType\b|\bFindFirstObjectByType\b|\bFindObjectsSortMode\b");

            foreach (string dir in directoriesToScan)
            {
                if (!Directory.Exists(dir)) continue;

                string[] csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                foreach (string file in csFiles)
                {
                    if (file.EndsWith(".meta")) continue;
                    if (file.EndsWith("Verification.cs")) continue;

                    string content = File.ReadAllText(file);
                    MatchCollection matches = obsoleteRegex.Matches(content);
                    if (matches.Count > 0)
                    {
                        Debug.LogError($"[LessonsIntermediasVerification] ❌ Se encontró API obsoleta en: {file}");
                        obsoleteCount += matches.Count;
                    }
                }
            }

            Assert(obsoleteCount == 0,
                $"No deben existir llamadas a APIs obsoletas ('FindFirstObjectByType' o 'FindObjectsSortMode') en el código (encontradas: {obsoleteCount}). Usar 'FindAnyObjectByType' o 'FindObjectsByType(FindObjectsInactive.Exclude)'.",
                ref total, ref passed);
        }

        // =========================================================================
        // Métodos Auxiliares de Aserción y Disciplina VR
        // =========================================================================

        private static void AssertVRTextDiscipline(string text, string context, ref int total, ref int passed)
        {
            string lower = text.ToLowerInvariant();
            Assert(!lower.Contains("teclado"), $"{context}: No debe mencionar 'teclado' (100% VR)", ref total, ref passed);
            Assert(!lower.Contains("mouse") && !lower.Contains("ratón"), $"{context}: No debe mencionar 'mouse' o 'ratón' (100% VR)", ref total, ref passed);
            Assert(!lower.Contains("gamepad") && !lower.Contains("joystick"), $"{context}: No debe mencionar 'gamepad' o 'joystick' estándar (100% VR)", ref total, ref passed);
            Assert(!lower.Contains("continuar"), $"{context}: No debe instruir a presionar 'Continuar' (Action-Driven)", ref total, ref passed);
            Assert(!lower.Contains("haz clic") && !lower.Contains("hacé clic"), $"{context}: No debe instruir a 'hacer clic'", ref total, ref passed);
        }

        private static void Assert(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            string log = $"[Test #{total:00}] {(condition ? "✅ PASS" : "❌ FAIL")}: {message}";
            LogLines.Add(log);

            if (condition)
            {
                passed++;
                Debug.Log($"   ✅ PASS: {message}");
            }
            else
            {
                LastFailures.Add(message);
                Debug.LogError($"   ❌ FAIL: {message}");
            }
        }
    }
}
