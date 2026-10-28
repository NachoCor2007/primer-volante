using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Expone las suites de verificación en Editor (<c>Tools/Primer Volante/...</c>) como tests NUnit para el Test Runner
    /// y el CI. Un test falla si la suite reporta alguna prueba fallida (<c>Debug.LogError</c> con <c>[FAIL]</c> o
    /// <c>❌</c>) o lanza una excepción.
    /// Cada suite arranca en una escena vacía guardada, así no depende de lo que dejó abierto la anterior.
    /// </summary>
    public class VerificationSuiteTests
    {
        // Escena vacía guardada: con una escena untitled abierta Unity no deja crear escenas aditivas (LowBeamInfraction).
        private const string TMP_FOLDER = "Assets/Scenes/TestingScenes/_TmpVerificationSuiteTests";
        private const string EMPTY_SCENE_PATH = TMP_FOLDER + "/Empty.unity";

        private static IEnumerable<TestCaseData> Suites()
        {
            yield return Suite("CockpitIntegration", CockpitIntegrationVerification.RunVerification);
            yield return Suite("ConsoleAlert", ConsoleAlertVerification.RunVerification);
            yield return Suite("EnvironmentAudio", EnvironmentAudioVerification.RunVerification);
            yield return Suite("GPSNavigation", () => GPSNavigationVerification.RunVerification());
            yield return Suite("HapticsIntegration", HapticsIntegrationVerification.RunVerification);
            yield return Suite("InfractionManager", InfractionManagerVerification.RunVerification);
            yield return Suite("InfractionWarning", InfractionWarningVerification.RunVerification);
            yield return Suite("InteractableHighlighter", InteractableHighlighterVerification.RunVerification);
            yield return Suite("Lesson01", () => Lesson01Verification.RunVerification());
            yield return Suite("LessonBoundary", LessonBoundaryVerification.RunVerification);
            yield return Suite("LessonsCurriculum", () => LessonsCurriculumVerification.RunVerification());
            yield return Suite("LowBeamInfraction", LowBeamInfractionVerification.RunVerification);
            yield return Suite("NPC", NPCVerification.RunVerification);
            yield return Suite("NPCSafetyStop", NPCSafetyStopLifecycleTests.RunAllTests);
            yield return Suite("ObstacleCollision", ObstacleCollisionVerification.RunVerification);
            yield return Suite("ScriptedEvent", ScriptedEventTests.RunAllTests);
            yield return Suite("SoundIntegration", SoundIntegrationVerification.RunVerification);
            yield return Suite("SteeringWheelTwoHands", SteeringWheelTwoHandsVerification.RunVerification);
            yield return Suite("TrafficLight", TrafficLightTests.RunAllTests);
            yield return Suite("TrafficSign", TrafficSignVerification.RunVerification);
            yield return Suite("TurnSignalInfraction", TurnSignalInfractionVerification.RunVerification);
            yield return Suite("TurnSignal", TurnSignalVerification.RunVerification);
            yield return Suite("VehicleLighting", VehicleLightingVerification.RunVerification);
            yield return Suite("VehicleMirror", VehicleMirrorVerification.RunVerification);
            yield return Suite("WrongWay", WrongWayVerification.RunVerification);
        }

        private static TestCaseData Suite(string name, Action run) => new TestCaseData(run).SetName(name);

        [OneTimeSetUp]
        public void CreateEmptyScene()
        {
            AssetDatabase.DeleteAsset(TMP_FOLDER);
            AssetDatabase.CreateFolder("Assets/Scenes/TestingScenes", "_TmpVerificationSuiteTests");
            EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single), EMPTY_SCENE_PATH);
        }

        // Descarta las escenas que dejó abiertas (y quizás sucias) la suite anterior, sin guardarlas.
        [SetUp]
        public void OpenEmptyScene() => EditorSceneManager.OpenScene(EMPTY_SCENE_PATH, OpenSceneMode.Single);

        [OneTimeTearDown]
        public void DeleteEmptyScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TMP_FOLDER);
        }

        [Test, TestCaseSource(nameof(Suites)), Timeout(600000)]
        public void Verification(Action run)
        {
            var failures = new List<string>();
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error && IsSuiteFailure(message)) failures.Add(message);
            }

            // Solo cuentan los fallos que reporta la suite; el resto de los errores es ruido de llamar componentes en
            // Edit Mode (ShouldRunBehaviour, renderer.material, tags no definidos) o excepciones provocadas a propósito.
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            try
            {
                run();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert.IsEmpty(failures, $"La suite reportó {failures.Count} fallo(s):\n{string.Join("\n", failures)}");
        }

        // Las suites marcan cada prueba fallida con "[FAIL]" o con un "❌" inicial (ConsoleAlert, InfractionWarning).
        private static bool IsSuiteFailure(string message) =>
            message.Contains("[FAIL]") || message.TrimStart().StartsWith("❌") || message.Contains(">❌");
    }
}
