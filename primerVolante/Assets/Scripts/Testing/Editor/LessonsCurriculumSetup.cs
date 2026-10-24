using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Lessons;

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

            // 1. Asegurar setup básico de escena (Car08, HUD, espejos)
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
                "Para ajustar tu postura, altura y distancia del asiento, presiona el botón Menu en tu control izquierdo para abrir el menú de calibración.\nUna vez ajustado a tu comodidad, o si la posición inicial ya es óptima, apunta con el rayo y presiona 'Continuar'.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.SteeringWheel,
                "");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson01_Step02_SteeringWheelCheck",
                "Comprobación del Volante",
                "Toma el volante presionando el grip de cualquier controlador y gíralo suavemente hacia ambos lados para familiarizarte con su recorrido y verificar que tus brazos tengan la flexión adecuada.\nPresiona 'Continuar' cuando estés listo.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.SteeringWheel,
                "SteeringWheel_Pivot");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson01_Step03_MirrorsCheck",
                "Visibilidad de Espejos y Entorno",
                "Comprueba los 3 espejos retrovisores resaltados en amarillo (central y laterales). Debes observar el tráfico posterior sin desviar en exceso la cabeza.\nPresiona 'Finalizar Calibración' para concluir la lección.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.SteeringWheel,
                "Mirrors");

            return CreateOrUpdateLesson(LESSON_01_PATH, "LESSON_01", "Lección 1: Calibración del Ambiente de Manejo",
                new[] { step1, step2, step3 });
        }

        /// <summary>
        /// Lección 2: Puesta en Marcha y Transmisión.
        /// </summary>
        public static LessonDefinitionSO SetupLesson02()
        {
            LessonStepSO step1 = CreateOrUpdateStep("Lesson02_Step01_BrakePrecondition",
                "Precondición de Seguridad: Freno de Pie",
                "Antes de arrancar o manipular los mandos, mantén presionado el pedal de freno con tu pie derecho como precondición fundamental de seguridad.\nPresiona 'Continuar' manteniendo la posición.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.StartButton | CockpitControlFlags.Handbrake,
                "");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson02_Step02_EngineStart",
                "Encendido del Motor (Start)",
                "Manteniendo presionado el pedal de freno, presiona el botón Start/Stop en el tablero para encender el motor del vehículo.\nPresiona 'Continuar' una vez encendido.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.StartButton,
                "StartButton");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson02_Step03_HandbrakeRelease",
                "Liberación del Freno de Mano",
                "Toma la palanca del freno de mano con tu mano derecha y bájala para liberar el freno de estacionamiento.\nPresiona 'Continuar' cuando esté completamente abajo.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.Handbrake,
                "VRHandbrake");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson02_Step04_GearShifterPN",
                "Selector de Marchas (P y N)",
                "Familiarízate con las posiciones de reposo de la transmisión automática: Parking (P) para estacionar y Neutro (N) para punto muerto.\nPresiona 'Continuar' para concluir la lección.",
                LessonStepCompletionMode.ManualContinue,
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
            LessonStepSO step1 = CreateOrUpdateStep("Lesson03_Step01_TurnSignals",
                "Luces de Giro / Guiños",
                "Acciona la palanca de luces de giro situada a la izquierda del volante: muévela hacia arriba para girar a la derecha y hacia abajo para girar a la izquierda.\nPresiona 'Continuar' tras probar ambos lados.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.TurnSignal,
                "VRTurnSignal");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson03_Step02_HazardLights",
                "Balizas de Emergencia",
                "Presiona el botón de balizas de emergencia (triángulo rojo en la consola) para advertir una detención riesgosa o emergencia en la vía.\nPresiona 'Continuar' cuando hayas verificado su encendido.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.HazardButton,
                "HazardButton");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson03_Step03_HeadlightKnob",
                "Perilla de Luces Reglamentarias",
                "Gira la perilla selectora de luces por sus posiciones reglamentarias: Apagadas (Off), Luces de Posición, Luces Bajas y guiño de Luces Altas.\nPresiona 'Continuar' para concluir la lección.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.HeadlightKnob,
                "VRHeadlightKnob");

            return CreateOrUpdateLesson(LESSON_03_PATH, "LESSON_03", "Lección 3: Sistema de Iluminación y Señalización",
                new[] { step1, step2, step3 });
        }

        /// <summary>
        /// Lección 4: Dinámica de Marcha, Maniobras y Detención.
        /// </summary>
        public static LessonDefinitionSO SetupLesson04()
        {
            LessonStepSO step1 = CreateOrUpdateStep("Lesson04_Step01_PreDriveChecklist",
                "Checklist de Salida",
                "Realiza el checklist previo a la marcha: enciende el motor, activa las luces reglamentarias bajas y libera el freno de mano manteniendo el pie en el freno.\nPresiona 'Continuar' para iniciar la marcha.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.All,
                "");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson04_Step02_DriveAcceleration",
                "Tracción en Drive (D) y Aceleración",
                "Coloca la palanca de cambios en posición Drive (D), suelta progresivamente el pedal de freno y acelera suavemente en línea recta hacia adelante.\nPresiona 'Continuar' al aproximarte al punto de frenado.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.All,
                "VRGearShifter");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson04_Step03_ProgressiveBraking",
                "Frenado Progresivo",
                "Presiona el pedal de freno de manera suave y progresiva hasta detener la marcha por completo, manteniendo la distancia de seguridad.\nPresiona 'Continuar' cuando el vehículo esté totalmente inmóvil.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.All,
                "");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson04_Step04_ReverseManeuver",
                "Maniobra en Reversa (R)",
                "Observa los 3 espejos retrovisores para comprobar que la zona posterior esté despejada, selecciona la marcha Reversa (R) y retrocede con precaución a baja velocidad.\nPresiona 'Continuar' tras completar la maniobra.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.All,
                "VRGearShifter");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson04_Step05_SecureStopAndPark",
                "Detención Segura y Estacionamiento",
                "Con el vehículo totalmente detenido, coloca la palanca de cambios en Parking (P), tira del freno de mano para asegurarlo y presiona el botón Start/Stop para apagar el motor.\nPresiona 'Finalizar Calibración' para concluir el curso de manejo.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.All,
                "VRHandbrake");

            return CreateOrUpdateLesson(LESSON_04_PATH, "LESSON_04", "Lección 4: Dinámica de Marcha, Maniobras y Detención",
                new[] { step1, step2, step3, step4, step5 });
        }

        private static LessonStepSO CreateOrUpdateStep(string fileName, string title, string instruction,
            LessonStepCompletionMode mode, CockpitControlFlags allowedControls, string highlightTarget)
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

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }

            LessonHUDPanel hudPanel = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            if (hudPanel != null)
            {
                EditorUtility.SetDirty(hudPanel);
            }

            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);
        }
    }
}
