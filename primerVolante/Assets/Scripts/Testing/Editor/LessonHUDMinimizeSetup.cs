using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Lessons;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Setup incremental que agrega a las 6 escenas de lección los controles para minimizar/maximizar el
    /// HUD del paso (franja <c>MinimizedBar</c>, <c>Button_Maximize</c>, <c>Button_Minimize</c>) y cablea las
    /// referencias nuevas de <see cref="LessonHUDPanel"/>. No vuelve a armar las lecciones: es idempotente y
    /// reutiliza los objetos si ya existen. El código de construcción es el de
    /// <see cref="Lesson01SceneSetup.BuildMinimizeControls"/>.
    /// </summary>
    public static class LessonHUDMinimizeSetup
    {
        public static readonly string[] LESSON_SCENE_PATHS =
        {
            "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity",
            "Assets/Scenes/Lessons/Lesson_02_EngineTransmission.unity",
            "Assets/Scenes/Lessons/Lesson_03_LightingSignals.unity",
            "Assets/Scenes/Lessons/Lesson_04_DrivingManeuvers.unity",
            "Assets/Scenes/Lessons/Lesson_05_TurnIntersections.unity",
            "Assets/Scenes/Lessons/Lesson_06_ParkingManeuvers.unity",
        };

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson HUD Minimize")]
        public static void SetupAllLessonScenes()
        {
            // Las escenas se abren en modo Single: pedir guardar las que estén sucias y abortar si el usuario cancela
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[LessonHUDMinimizeSetup] Cancelado por el usuario: hay escenas sin guardar.");
                return;
            }

            // Escenas abiertas al empezar (y cuál era la activa) para volver a dejarlas como estaban
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

            int ok = 0;
            try
            {
                foreach (string path in LESSON_SCENE_PATHS)
                {
                    if (SetupScene(path)) ok++;
                }
            }
            finally
            {
                RestoreOpenScenes(setup);
            }

            if (ok == LESSON_SCENE_PATHS.Length)
            {
                Debug.Log($"[LessonHUDMinimizeSetup] Controles de minimizar/maximizar configurados en {ok}/{LESSON_SCENE_PATHS.Length} escenas de lección.");
            }
            else
            {
                Debug.LogError($"[LessonHUDMinimizeSetup] [FAIL] Solo se configuraron {ok}/{LESSON_SCENE_PATHS.Length} escenas de lección.");
            }
        }

        private static void RestoreOpenScenes(SceneSetup[] setup)
        {
            // Una escena sin guardar (path vacío) no se puede restaurar: se deja el setup por defecto de Unity
            if (setup.Length == 0 || System.Array.Exists(setup, s => string.IsNullOrEmpty(s.path)))
            {
                Debug.LogWarning("[LessonHUDMinimizeSetup] Había una escena sin guardar abierta: no se puede restaurar. " +
                                 "Se deja una escena nueva por defecto.");
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }

            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        /// <summary>Abre la escena, agrega/reconfigura los controles en su LessonHUD_Canvas y la guarda.</summary>
        public static bool SetupScene(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[LessonHUDMinimizeSetup] [FAIL] No se pudo abrir {scenePath}");
                return false;
            }

            LessonHUDPanel hud = Object.FindAnyObjectByType<LessonHUDPanel>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError($"[LessonHUDMinimizeSetup] [FAIL] {scenePath} no tiene LessonHUDPanel (LessonHUD_Canvas).");
                return false;
            }

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Lesson01SceneSetup.FONT_PATH);
            Lesson01SceneSetup.BuildMinimizeControls(hud, font);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved) Debug.LogError($"[LessonHUDMinimizeSetup] [FAIL] No se pudo guardar {scenePath}");
            return saved;
        }
    }
}
