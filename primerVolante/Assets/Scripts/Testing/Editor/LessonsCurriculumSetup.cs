using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente para configurar y mantener el currículum completo
    /// de lecciones del simulador "Primer Volante" (Lecciones 1, 2, 3 y 4).
    /// Genera los ScriptableObjects correspondientes y cablea la progresión dentro de la
    /// escena unificada Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity.
    /// Cumple estrictamente las reglas de AGENTS.md (cero APIs obsoletas).
    /// </summary>
    public static class LessonsCurriculumSetup
    {
        public const string LESSONS_DIR = "Assets/Scripts/Lessons";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps";
        public const string SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";

        public const string LESSON_01_PATH = "Assets/Scripts/Lessons/LessonData_01.asset";
        public const string LESSON_02_PATH = "Assets/Scripts/Lessons/LessonData_02.asset";
        public const string LESSON_03_PATH = "Assets/Scripts/Lessons/LessonData_03.asset";
        public const string LESSON_04_PATH = "Assets/Scripts/Lessons/LessonData_04.asset";

        [MenuItem("Tools/Primer Volante/Lessons/Setup All Lessons Curriculum")]
        public static void SetupAllCurriculum()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [LessonsCurriculumSetup] Configurando currículum completo (Lecciones 1 a 4)...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Asegurar setup básico de escena (Car09, HUD, espejos)
            Lesson01SceneSetup.SetupLesson01();

            // 2. Configurar o actualizar activos de Lección 1 a 4
            LessonDefinitionSO lesson1 = SetupLesson01();
            LessonDefinitionSO lesson2 = SetupLesson02();
            LessonDefinitionSO lesson3 = SetupLesson03();
            LessonDefinitionSO lesson4 = SetupLesson04();

            List<LessonDefinitionSO> curriculum = new List<LessonDefinitionSO>
            {
                lesson1,
                lesson2,
                lesson3,
                lesson4
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 3. Configurar la escena unificada con el currículum completo
            SetupCurriculumInScene(curriculum);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("=================================================================");
            Debug.Log("✅ [LessonsCurriculumSetup] ¡Currículum completo configurado exitosamente!");
            Debug.Log("=================================================================");
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scripts"))
            {
                AssetDatabase.CreateFolder("Assets", "Scripts");
            }
            if (!AssetDatabase.IsValidFolder(LESSONS_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder(STEPS_DIR))
            {
                AssetDatabase.CreateFolder(LESSONS_DIR, "Steps");
            }
        }

        /// <summary>
        /// Lección 1: Calibración del Ambiente de Manejo.
        /// </summary>
        public static LessonDefinitionSO SetupLesson01()
        {
            LessonStepSO step1 = CreateOrUpdateStep("Lesson01_Step01_SeatCalibration",
                "Ajuste de Asiento y Postura",
                "Para ajustar tu postura, altura y distancia del asiento, presiona el botón Menu en tu control izquierdo (o tecla Escape) para abrir el menú de calibración.\nAjusta tu posición y cierra el menú al finalizar para avanzar.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.OpenCalibrationMenu,
                0f,
                CockpitControlFlags.SteeringWheel,
                "");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson01_Step02_SteeringWheelCheck",
                "Comprobación del Volante",
                "Toma el volante con ambas manos y gíralo hacia ambos lados para familiarizarte con su recorrido y verificar la flexión cómoda de tus brazos.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.RotateSteeringWheel,
                20f,
                CockpitControlFlags.SteeringWheel,
                "SteeringWheel_Pivot");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson01_Step03_RearviewMirror",
                "Ajuste de Espejo Retrovisor Central",
                "Toma el espejo retrovisor interior desde su borde con tu mano (Grip) o usa las teclas I/K/J/L y oriéntalo hasta tener una vista limpia de la luneta trasera.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.AdjustRearviewMirror,
                2.5f,
                CockpitControlFlags.Mirrors,
                "RearviewMirror_Mount");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson01_Step04_LeftMirror",
                "Ajuste de Espejo Lateral Izquierdo",
                "En el panel de la puerta, coloca el selector en 'L' y usa el mando de dirección o flechas para orientar el espejo exterior izquierdo hasta observar el flanco del coche.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.AdjustLeftMirror,
                0f,
                CockpitControlFlags.Mirrors,
                "SideMirror_Left, VRSideMirrorControlPanel");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson01_Step05_RightMirror",
                "Ajuste de Espejo Lateral Derecho",
                "Coloca el selector en 'R' y ajusta el espejo exterior derecho hasta observar el costado del vehículo y el carril derecho.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.AdjustRightMirror,
                0f,
                CockpitControlFlags.Mirrors,
                "SideMirror_Right, VRSideMirrorControlPanel");

            return CreateOrUpdateLesson(LESSON_01_PATH, "LESSON_01", "Lección 1: Calibración del Ambiente de Manejo",
                new[] { step1, step2, step3, step4, step5 });
        }

        /// <summary>
        /// Lección 2: Puesta en Marcha y Transmisión.
        /// </summary>
        public static LessonDefinitionSO SetupLesson02()
        {
            LessonStepSO step1 = CreateOrUpdateStep("Lesson02_Step01_BrakePrecondition",
                "Precondición de Seguridad: Freno de Pie",
                "Antes de arrancar o manipular los mandos, mantén presionado el pedal de freno con tu pie derecho como precondición fundamental de seguridad.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.PressBrakePedal,
                0.8f,
                CockpitControlFlags.StartButton | CockpitControlFlags.Handbrake,
                "");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson02_Step02_EngineStart",
                "Encendido del Motor (Start)",
                "Manteniendo presionado el pedal de freno a fondo, presiona el botón Start/Stop en el tablero para encender el motor del vehículo.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.StartEngine,
                0f,
                CockpitControlFlags.StartButton,
                "StartButton");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson02_Step03_HandbrakeRelease",
                "Liberación del Freno de Mano",
                "Toma la palanca del freno de mano con tu mano derecha y bájala por completo para liberar el freno de estacionamiento.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ReleaseHandbrake,
                0.05f,
                CockpitControlFlags.Handbrake,
                "VRHandbrake");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson02_Step04_GearShifterPN",
                "Selector de Marchas (P y N)",
                "Con el pedal de freno presionado, mueve la palanca de cambios desde Parking (P) hacia la posición Neutro (N).",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ShiftToNeutral,
                0f,
                CockpitControlFlags.GearShifter,
                "VRGearShifter");

            return CreateOrUpdateLesson(LESSON_02_PATH, "LESSON_02", "Lección 2: Puesta en Marcha y Transmisión",
                new[] { step1, step2, step3, step4 });
        }

        /// <summary>
        /// Lección 3: Sistema de Iluminación y Señalización.
        /// </summary>
        public static LessonDefinitionSO SetupLesson03()
        {
            LessonStepSO step1 = CreateOrUpdateStep("Lesson03_Step01_TurnSignalRight",
                "Luz de Giro a la Derecha",
                "Acciona la palanca de luces de giro situada a la izquierda del volante: muévela hacia arriba para indicar giro a la derecha.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ActivateTurnSignalRight,
                0f,
                CockpitControlFlags.TurnSignal,
                "VRTurnSignal");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson03_Step02_TurnSignalLeft",
                "Luz de Giro a la Izquierda",
                "Acciona la palanca de luces de giro situada a la izquierda del volante: muévela hacia abajo para indicar giro a la izquierda.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ActivateTurnSignalLeft,
                0f,
                CockpitControlFlags.TurnSignal,
                "VRTurnSignal");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson03_Step03_HazardLights",
                "Balizas de Emergencia",
                "Presiona el botón de balizas de emergencia (triángulo rojo en la consola) para advertir una detención riesgosa o advertencia vial.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ActivateHazardLights,
                0f,
                CockpitControlFlags.HazardButton,
                "HazardButton");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson03_Step04_HeadlightsPosition",
                "Luces de Posición",
                "Gira la perilla selectora de luces en el tablero al primer punto (Posición) y suéltala para comprobar las luces reglamentarias de estacionamiento.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SetHeadlightsPosition,
                0f,
                CockpitControlFlags.HeadlightKnob,
                "VRHeadlightKnob");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson03_Step05_HeadlightsLowBeam",
                "Luces Bajas Reglamentarias",
                "Gira la perilla selectora de luces al segundo punto (Bajas) y suéltala para encender las luces de circulación obligatoria.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SetHeadlightsLowBeam,
                0f,
                CockpitControlFlags.HeadlightKnob,
                "VRHeadlightKnob");

            LessonStepSO step6 = CreateOrUpdateStep("Lesson03_Step06_HeadlightsHighBeam",
                "Luces Altas de Ruta",
                "Gira la perilla selectora de luces al tercer punto (Altas) y suéltala para activar el haz largo de ruta.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SetHeadlightsHighBeam,
                0f,
                CockpitControlFlags.HeadlightKnob,
                "VRHeadlightKnob");

            return CreateOrUpdateLesson(LESSON_03_PATH, "LESSON_03", "Lección 3: Sistema de Iluminación y Señalización",
                new[] { step1, step2, step3, step4, step5, step6 });
        }

        /// <summary>
        /// Lección 4: Dinámica de Marcha, Maniobras y Detención.
        /// </summary>
        public static LessonDefinitionSO SetupLesson04()
        {
            LessonStepSO step1 = CreateOrUpdateStep("Lesson04_Step01_StartEngine",
                "Encendido del Motor",
                "Pisa el freno con tu pie derecho y presiona el botón Start/Stop para arrancar el motor.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.StartEngine,
                0f,
                CockpitControlFlags.All,
                "StartButton");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson04_Step02_HeadlightsLowBeam",
                "Luces Bajas Reglamentarias",
                "Gira la perilla selectora para encender las luces bajas de circulación obligatoria.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SetHeadlightsLowBeam,
                0f,
                CockpitControlFlags.All,
                "VRHeadlightKnob");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson04_Step03_ReleaseHandbrake",
                "Liberación del Freno de Mano",
                "Baja por completo la palanca del freno de mano para habilitar el rodaje.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ReleaseHandbrake,
                0f,
                CockpitControlFlags.All,
                "VRHandbrake");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson04_Step04_ShiftToDrive",
                "Marcha en Drive (D)",
                "Manteniendo presionado el freno de pie, coloca la palanca selectora en posición Drive (D).",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ShiftToDrive,
                0f,
                CockpitControlFlags.All,
                "VRGearShifter");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson04_Step05_DriveForward",
                "Avance y Aceleración",
                "Suelta suavemente el freno y acelera hacia adelante en línea recta.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.DriveForward,
                4f,
                CockpitControlFlags.All,
                "VRGearShifter");

            LessonStepSO step6 = CreateOrUpdateStep("Lesson04_Step06_ProgressiveBraking",
                "Frenado Progresivo",
                "Presiona el pedal de freno de manera controlada hasta detener el vehículo por completo.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.PressBrakePedal,
                0.8f,
                CockpitControlFlags.All,
                "");

            LessonStepSO step7 = CreateOrUpdateStep("Lesson04_Step07_ReverseManeuver",
                "Maniobra de Reversa (R)",
                "Comprueba los espejos, pasa la palanca a Reversa (R) y retrocede con precaución.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.DriveReverse,
                3f,
                CockpitControlFlags.All,
                "VRGearShifter");

            LessonStepSO step8 = CreateOrUpdateStep("Lesson04_Step08_SecureStopAndPark",
                "Estacionamiento Seguro",
                "Detén el vehículo, coloca la palanca en Parking (P) y sube el freno de mano firmemente.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SecureStopAndPark,
                0f,
                CockpitControlFlags.All,
                "VRHandbrake, VRGearShifter");

            return CreateOrUpdateLesson(LESSON_04_PATH, "LESSON_04", "Lección 4: Dinámica de Marcha, Maniobras y Detención",
                new[] { step1, step2, step3, step4, step5, step6, step7, step8 });
        }

        private static LessonStepSO CreateOrUpdateStep(string fileName, string title, string instruction,
            LessonStepCompletionMode mode, LessonActionType requiredAction, float actionNumericParameter,
            CockpitControlFlags allowedControls, string highlightTarget)
        {
            string path = $"{STEPS_DIR}/{fileName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            bool isNew = false;
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                isNew = true;
            }

            step.stepTitle = title;
            step.instructionText = instruction;
            step.completionMode = mode;
            step.requiredAction = requiredAction;
            step.actionNumericParameter = actionNumericParameter;
            step.allowedControls = allowedControls;
            step.highlightTargetName = highlightTarget;

            if (isNew)
            {
                AssetDatabase.CreateAsset(step, path);
            }
            else
            {
                EditorUtility.SetDirty(step);
            }

            return step;
        }

        private static LessonDefinitionSO CreateOrUpdateLesson(string path, string lessonId, string lessonTitle,
            IEnumerable<LessonStepSO> steps)
        {
            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(path);
            bool isNew = false;
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                isNew = true;
            }

            lesson.lessonId = lessonId;
            lesson.lessonTitle = lessonTitle;
            lesson.steps.Clear();
            if (steps != null)
            {
                lesson.steps.AddRange(steps);
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(lesson, path);
            }
            else
            {
                EditorUtility.SetDirty(lesson);
            }

            return lesson;
        }

        private static void SetupCurriculumInScene(List<LessonDefinitionSO> curriculum)
        {
            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (!currentScene.IsValid() || currentScene.path != SCENE_PATH)
            {
                currentScene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }

            if (!currentScene.IsValid() || !currentScene.isLoaded)
            {
                Debug.LogError($"[LessonsCurriculumSetup] ❌ No se pudo abrir la escena {SCENE_PATH}");
                return;
            }

            LessonManager manager = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            if (manager != null)
            {
                manager.SetCurriculum(curriculum);

                var so = new SerializedObject(manager);
                SerializedProperty currProp = so.FindProperty("m_Curriculum");
                if (currProp != null)
                {
                    currProp.arraySize = curriculum.Count;
                    for (int i = 0; i < curriculum.Count; i++)
                    {
                        currProp.GetArrayElementAtIndex(i).objectReferenceValue = curriculum[i];
                    }
                }

                if (curriculum.Count > 0)
                {
                    so.FindProperty("m_ActiveLesson").objectReferenceValue = curriculum[0];
                }

                VehicleController vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
                LessonActionMonitor monitor = manager.GetComponent<LessonActionMonitor>();
                if (monitor == null)
                {
                    monitor = manager.gameObject.AddComponent<LessonActionMonitor>();
                }
                so.FindProperty("m_VehicleController").objectReferenceValue = vehicle;
                so.FindProperty("m_ActionMonitor").objectReferenceValue = monitor;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }

            LessonHUDPanel hudPanel = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            if (hudPanel != null)
            {
                if (hudPanel.SuccessChimeClip == null)
                {
                    AudioClip chimeClip = Lesson01SceneSetup.EnsureStepNotificationChimeAsset();
                    if (chimeClip != null)
                    {
                        hudPanel.SuccessChimeClip = chimeClip;
                        var soHud = new SerializedObject(hudPanel);
                        soHud.FindProperty("m_SuccessChimeClip").objectReferenceValue = chimeClip;
                        soHud.ApplyModifiedProperties();
                    }
                }
                EditorUtility.SetDirty(hudPanel);
            }

            // Sincronizar asiento del conductor en XR Origin (DriverSeatFollower)
            DriverSeatFollower seatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            VehicleController playerVehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            if (seatFollower != null && playerVehicle != null)
            {
                Transform driverSeat = playerVehicle.transform.Find("DriverSeat");
                if (driverSeat == null)
                {
                    foreach (Transform t in playerVehicle.GetComponentsInChildren<Transform>(true))
                    {
                        if (string.Equals(t.name, "DriverSeat", System.StringComparison.OrdinalIgnoreCase))
                        {
                            driverSeat = t;
                            break;
                        }
                    }
                }

                if (driverSeat != null)
                {
                    seatFollower.DriverSeat = driverSeat;
                    seatFollower.FollowSeat = true;
                    seatFollower.SnapToSeat();

                    var soFollower = new SerializedObject(seatFollower);
                    soFollower.FindProperty("m_DriverSeat").objectReferenceValue = driverSeat;
                    soFollower.FindProperty("m_FollowSeat").boolValue = true;
                    soFollower.ApplyModifiedProperties();

                    EditorUtility.SetDirty(seatFollower);
                    Debug.Log($"[LessonsCurriculumSetup] 💺 DriverSeatFollower vinculado a '{driverSeat.name}' en '{playerVehicle.name}'.");
                }
            }

            if (playerVehicle != null)
            {
                Lesson01SceneSetup.RevertCockpitControlOverrides(playerVehicle.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);
        }
    }
}
