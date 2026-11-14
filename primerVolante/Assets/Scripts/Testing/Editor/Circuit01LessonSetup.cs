using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;
using PrimerVolante.MainMenu;
using PrimerVolante.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para ensamblar y configurar la lección interactiva de Circuit_01:
    /// - Genera los ScriptableObjects (LessonData_Circuit_01 y Circuit01_Step01_GPSNavigation).
    /// - Configura el mensaje instructivo de inicio guiado por GPS y normas viales.
    /// - Habilita todos los controles de cabina (volante, pedales, palanca, guiños, luces, etc.).
    /// - Conecta Circuit01Controller en Lesson_Manager para validar la llegada y estacionamiento en el cajón amarillo.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Circuit01LessonSetup
    {
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Circuit_01.unity";
        public const string NEW_MAIN_MENU_SCENE_PATH = "Assets/Scenes/car09/NewMainMenu.unity";
        public const string CURRICULUM_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps/Circuit01";
        public const string LESSON_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_Circuit_01.asset";
        public const string STEP_PATH = "Assets/Scripts/Lessons/Steps/Circuit01/Circuit01_Step01_GPSNavigation.asset";

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Circuit 01 Lesson")]
        public static void SetupCircuit01Lesson()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [Circuit01LessonSetup] Configurando lección para Circuit_01...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Crear o actualizar los ScriptableObjects de la lección
            LessonDefinitionSO lessonData = CreateOrUpdateLessonAssets();

            // 2. Asegurar que Circuit_01 esté abierta
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.path.Contains("Circuit_01"))
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Circuit01LessonSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 3. Configurar Lesson_Manager y sus componentes
            GameObject managerObj = GameObject.Find("Lesson_Manager");
            if (managerObj == null)
            {
                managerObj = new GameObject("Lesson_Manager");
                Undo.RegisterCreatedObjectUndo(managerObj, "Create Lesson_Manager");
                SceneManager.MoveGameObjectToScene(managerObj, scene);
            }

            LessonManager lm = managerObj.GetComponent<LessonManager>();
            if (lm == null) lm = managerObj.AddComponent<LessonManager>();

            LessonActionMonitor am = managerObj.GetComponent<LessonActionMonitor>();
            if (am == null) am = managerObj.AddComponent<LessonActionMonitor>();

            Circuit01Controller circuitCtrl = managerObj.GetComponent<Circuit01Controller>();
            if (circuitCtrl == null) circuitCtrl = managerObj.AddComponent<Circuit01Controller>();

            // 4. Asignar LessonDefinitionSO activa
            SerializedObject lmSo = new SerializedObject(lm);
            lmSo.FindProperty("m_ActiveLesson").objectReferenceValue = lessonData;
            lmSo.FindProperty("m_AutoStartOnStart").boolValue = true;
            lmSo.ApplyModifiedProperties();

            // 5. Vincular referencias en LessonActionMonitor
            SerializedObject amSo = new SerializedObject(am);
            LessonHUDPanel hud = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            if (hud != null) amSo.FindProperty("m_HUDPanel").objectReferenceValue = hud;

            VehicleController vc = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            if (vc != null) amSo.FindProperty("m_VehicleController").objectReferenceValue = vc;

            InGameMenuController menu = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>();
            if (menu != null) amSo.FindProperty("m_MenuController").objectReferenceValue = menu;

            VRRearviewMirror mirror = UnityEngine.Object.FindAnyObjectByType<VRRearviewMirror>();
            if (mirror != null) amSo.FindProperty("m_RearviewMirror").objectReferenceValue = mirror;

            VRSideMirrorControlPanel sidePanel = UnityEngine.Object.FindAnyObjectByType<VRSideMirrorControlPanel>();
            if (sidePanel != null) amSo.FindProperty("m_SideMirrorPanel").objectReferenceValue = sidePanel;

            VRHeadlightKnob knob = UnityEngine.Object.FindAnyObjectByType<VRHeadlightKnob>();
            if (knob != null) amSo.FindProperty("m_HeadlightKnob").objectReferenceValue = knob;

            amSo.ApplyModifiedProperties();

            // 6. Configurar Circuit01Controller
            circuitCtrl.AutoDiscoverReferences();
            EditorUtility.SetDirty(circuitCtrl);
            EditorUtility.SetDirty(lm);
            EditorUtility.SetDirty(am);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = managerObj;
            Debug.Log($"[Circuit01LessonSetup] ✅ Lección '{lessonData.lessonTitle}' configurada con éxito en Circuit_01!");
        }

        private static void EnsureDirectories()
        {
            if (!Directory.Exists(CURRICULUM_DIR))
            {
                Directory.CreateDirectory(CURRICULUM_DIR);
            }
            if (!Directory.Exists(STEPS_DIR))
            {
                Directory.CreateDirectory(STEPS_DIR);
            }
            AssetDatabase.Refresh();
        }

        private static LessonDefinitionSO CreateOrUpdateLessonAssets()
        {
            // Paso 1: Navegación GPS y normas viales hasta la meta
            LessonStepSO step1 = AssetDatabase.LoadAssetAtPath<LessonStepSO>(STEP_PATH);
            if (step1 == null)
            {
                step1 = ScriptableObject.CreateInstance<LessonStepSO>();
                AssetDatabase.CreateAsset(step1, STEP_PATH);
            }

            step1.stepTitle = "Navegación Urbana y Normas de Tránsito";
            step1.instructionText = "Sigue la ruta guiada en la pantalla del GPS conduciendo con atención y respetando las normas de tránsito. Al finalizar el recorrido, estaciona dentro del sector amarillo demarcado.";
            step1.completionMode = LessonStepCompletionMode.ActionRequired;
            step1.requiredAction = LessonActionType.CompleteCircuitNavigation;
            step1.allowedControls = CockpitControlFlags.All;
            step1.ambienceIntensity = AmbienceIntensityOverride.Normal;
            step1.highlightTargetName = "";
            EditorUtility.SetDirty(step1);

            // Lección: Circuito Urbano 1
            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_PATH);
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                AssetDatabase.CreateAsset(lesson, LESSON_PATH);
            }

            lesson.lessonId = "CIRCUIT_01";
            lesson.lessonTitle = "Circuito Urbano 1: Tráfico y Navegación GPS";
            lesson.steps = new List<LessonStepSO> { step1 };
            EditorUtility.SetDirty(lesson);

            AssetDatabase.SaveAssets();
            return lesson;
        }

        [MenuItem("Tools/Primer Volante/Circuit 01/Update NewMainMenu Circuits Tab")]
        public static void UpdateNewMainMenuCircuits()
        {
            EnsureCircuit01InBuildSettings();

            string currentScene = SceneManager.GetActiveScene().path;
            Scene menuScene = EditorSceneManager.OpenScene(NEW_MAIN_MENU_SCENE_PATH, OpenSceneMode.Single);

            NewMainMenuController menuCtrl = UnityEngine.Object.FindAnyObjectByType<NewMainMenuController>();
            if (menuCtrl != null)
            {
                var circuits = NewMainMenuController.GetDefaultCircuits();
                menuCtrl.SetCircuits(circuits);

                Transform itemsBox = GameObject.Find("ItemsBox")?.transform;
                if (itemsBox != null)
                {
                    Transform item0 = itemsBox.Find("CircuitItem_0");
                    if (item0 != null)
                    {
                        var txt = item0.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                        if (txt != null) txt.text = circuits[0].title;
                    }

                    Transform item1 = itemsBox.Find("CircuitItem_1");
                    if (item1 != null) UnityEngine.Object.DestroyImmediate(item1.gameObject);

                    Transform item2 = itemsBox.Find("CircuitItem_2");
                    if (item2 != null) UnityEngine.Object.DestroyImmediate(item2.gameObject);
                }

                EditorUtility.SetDirty(menuCtrl);
            }

            EditorSceneManager.MarkSceneDirty(menuScene);
            EditorSceneManager.SaveScene(menuScene);

            if (!string.IsNullOrEmpty(currentScene) && currentScene != NEW_MAIN_MENU_SCENE_PATH)
            {
                EditorSceneManager.OpenScene(currentScene, OpenSceneMode.Single);
            }

            Debug.Log("[Circuit01LessonSetup] ✅ Solapa Circuitos en NewMainMenu actualizada con Circuit_01 como único circuito real.");
        }

        public static void EnsureCircuit01InBuildSettings()
        {
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == TARGET_SCENE_PATH))
            {
                buildScenes.Add(new EditorBuildSettingsScene(TARGET_SCENE_PATH, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
                Debug.Log($"[Circuit01LessonSetup] ➕ Escena {TARGET_SCENE_PATH} agregada a EditorBuildSettings.");
            }
        }
    }
}

