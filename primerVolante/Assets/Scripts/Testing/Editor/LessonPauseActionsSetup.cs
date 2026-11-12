using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Setup incremental que agrega al menú de pausa de las 6 escenas de lección los botones "Reiniciar lección" y
    /// "Salir de la lección", el <c>ConfirmPanel</c> y el componente <see cref="LessonPauseActions"/>. No vuelve a armar el menú:
    /// es idempotente y reutiliza los objetos si ya existen. Solo toca escenas de lección: <c>InGameMenuSetup.BuildMenuCanvas</c>
    /// (menú de otros escenarios) queda igual. <see cref="Lesson01SceneSetup"/> lo llama tras reconstruir el menú.
    /// </summary>
    public static class LessonPauseActionsSetup
    {
        public const string RESTART_BUTTON = "Button_RestartLesson";
        public const string EXIT_BUTTON = "Button_ExitLesson";
        public const string CONFIRM_PANEL = "ConfirmPanel";

        public static readonly Vector2 CANVAS_SIZE = new Vector2(600f, 500f);
        public static readonly Vector2 RESTART_POSITION = new Vector2(0f, -310f);
        public static readonly Vector2 EXIT_POSITION = new Vector2(0f, -390f);
        public static readonly Vector2 MAIN_BUTTON_SIZE = new Vector2(300f, 60f);
        public static readonly Vector2 CONFIRM_POSITION = new Vector2(-130f, -380f);
        public static readonly Vector2 CANCEL_POSITION = new Vector2(130f, -380f);
        public static readonly Vector2 CONFIRM_BUTTON_SIZE = new Vector2(240f, 60f);

        [MenuItem("Tools/Primer Volante/Setup Lesson Pause Actions (6 lessons)")]
        public static void SetupAllLessonScenes()
        {
            // Las escenas se abren en modo Single: pedir guardar las que estén sucias y abortar si el usuario cancela
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[LessonPauseActionsSetup] Cancelado por el usuario: hay escenas sin guardar.");
                return;
            }

            // Escenas abiertas al empezar (y cuál era la activa) para volver a dejarlas como estaban
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

            int ok = 0;
            try
            {
                foreach (string path in LessonHUDMinimizeSetup.LESSON_SCENE_PATHS)
                {
                    // Un fallo en una escena no corta las demás
                    try
                    {
                        if (SetupScene(path)) ok++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[LessonPauseActionsSetup] [FAIL] {path}: {ex}");
                    }
                }
            }
            finally
            {
                RestoreOpenScenes(setup);
            }

            if (ok == LessonHUDMinimizeSetup.LESSON_SCENE_PATHS.Length)
            {
                Debug.Log($"[LessonPauseActionsSetup] Reiniciar/Salir de la lección configurados en {ok}/{LessonHUDMinimizeSetup.LESSON_SCENE_PATHS.Length} escenas de lección.");
            }
            else
            {
                Debug.LogError($"[LessonPauseActionsSetup] [FAIL] Solo se configuraron {ok}/{LessonHUDMinimizeSetup.LESSON_SCENE_PATHS.Length} escenas de lección.");
            }
        }

        private static void RestoreOpenScenes(SceneSetup[] setup)
        {
            // Una escena sin guardar (path vacío) no se puede restaurar: se deja el setup por defecto de Unity
            if (setup.Length == 0 || System.Array.Exists(setup, s => string.IsNullOrEmpty(s.path)))
            {
                Debug.LogWarning("[LessonPauseActionsSetup] Había una escena sin guardar abierta: no se puede restaurar. " +
                                 "Se deja una escena nueva por defecto.");
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }

            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        /// <summary>Abre la escena, configura el menú de pausa y la guarda.</summary>
        public static bool SetupScene(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[LessonPauseActionsSetup] [FAIL] No se pudo abrir {scenePath}");
                return false;
            }

            if (SetupActiveScene() == null)
            {
                Debug.LogError($"[LessonPauseActionsSetup] [FAIL] {scenePath} no tiene InGameMenuController con su canvas armado.");
                return false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved) Debug.LogError($"[LessonPauseActionsSetup] [FAIL] No se pudo guardar {scenePath}");
            return saved;
        }

        /// <summary>
        /// Idempotente: en la escena activa crea o reubica Button_RestartLesson / Button_ExitLesson y el ConfirmPanel, deja el canvas del
        /// menú en 600x500 y agrega y cablea <see cref="LessonPauseActions"/>. No guarda la escena. Devuelve null si la escena no tiene
        /// el menú in-game armado.
        /// </summary>
        public static LessonPauseActions SetupActiveScene()
        {
            // Solo la escena activa: con otras escenas cargadas (la verificación trabaja sobre una copia) no se toca el menú ajeno.
            InGameMenuController menu = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                menu = root.GetComponentInChildren<InGameMenuController>(true);
                if (menu != null) break;
            }
            if (menu == null) return null;

            var soMenu = new SerializedObject(menu);
            GameObject canvasRoot = soMenu.FindProperty("m_MenuRoot").objectReferenceValue as GameObject;
            GameObject mainPanel = soMenu.FindProperty("m_MainPanel").objectReferenceValue as GameObject;
            if (canvasRoot == null || mainPanel == null) return null;

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(InGameMenuSetup.FONT_PATH);

            // Canvas más alto para los dos botones nuevos
            var canvasRect = (RectTransform)canvasRoot.transform;
            canvasRect.sizeDelta = CANVAS_SIZE;

            // Botones del panel principal (Reanudar y Calibrar posición no se tocan)
            Button restart = EnsureButton(mainPanel.transform, font, RESTART_BUTTON, "Reiniciar lección", RESTART_POSITION, MAIN_BUTTON_SIZE);
            Button exit = EnsureButton(mainPanel.transform, font, EXIT_BUTTON, "Salir de la lección", EXIT_POSITION, MAIN_BUTTON_SIZE);

            // Panel de confirmación: hermano de MainPanel y CalibrationPanel
            Transform confirmTransform = canvasRoot.transform.Find(CONFIRM_PANEL);
            GameObject confirmPanel = confirmTransform != null
                ? confirmTransform.gameObject
                : InGameMenuSetup.CreatePanel(CONFIRM_PANEL, canvasRoot.transform);

            TMP_Text title = EnsureLabel(confirmPanel.transform, font, "Title", LessonPauseActions.RESTART_TITLE, 32f, new Vector2(0f, -40f), new Vector2(520f, 50f));
            TMP_Text message = EnsureLabel(confirmPanel.transform, font, "Message", LessonPauseActions.RESTART_MESSAGE, 26f, new Vector2(0f, -220f), new Vector2(520f, 220f));
            message.alignment = TextAlignmentOptions.Center;
            message.textWrappingMode = TextWrappingModes.Normal;
            message.raycastTarget = false;
            title.raycastTarget = false;

            Button confirm = EnsureButton(confirmPanel.transform, font, "Button_Confirm", LessonPauseActions.RESTART_CONFIRM, CONFIRM_POSITION, CONFIRM_BUTTON_SIZE);
            Button cancel = EnsureButton(confirmPanel.transform, font, "Button_Cancel", "Cancelar", CANCEL_POSITION, CONFIRM_BUTTON_SIZE);
            confirmPanel.SetActive(false);

            // Capa UI en todo lo nuevo (el rayo a distancia solo raycastea esa capa mientras el menú está abierto)
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                foreach (Transform t in canvasRoot.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = uiLayer;
                }
            }

            // Componente en el GameObject InGameMenuController
            LessonPauseActions actions = menu.GetComponent<LessonPauseActions>();
            if (actions == null) actions = menu.gameObject.AddComponent<LessonPauseActions>();

            var so = new SerializedObject(actions);
            so.Update();
            so.FindProperty("m_Menu").objectReferenceValue = menu;
            so.FindProperty("m_MainPanel").objectReferenceValue = mainPanel;
            so.FindProperty("m_RestartButton").objectReferenceValue = restart;
            so.FindProperty("m_ExitButton").objectReferenceValue = exit;
            so.FindProperty("m_ConfirmPanel").objectReferenceValue = confirmPanel;
            so.FindProperty("m_ConfirmTitle").objectReferenceValue = title;
            so.FindProperty("m_ConfirmMessage").objectReferenceValue = message;
            so.FindProperty("m_ConfirmButton").objectReferenceValue = confirm;
            so.FindProperty("m_ConfirmButtonLabel").objectReferenceValue = confirm.GetComponentInChildren<TMP_Text>(true);
            so.FindProperty("m_CancelButton").objectReferenceValue = cancel;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(actions);
            EditorUtility.SetDirty(canvasRoot);
            EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
            return actions;
        }

        // Reutiliza el botón si ya existe (sin duplicarlo) y lo deja en la posición, tamaño y texto acordados.
        private static Button EnsureButton(Transform parent, TMP_FontAsset font, string name, string text, Vector2 position, Vector2 size)
        {
            // Un hijo homónimo sin Button (o sin su Label) se recrea en lugar de reutilizarse
            Transform existing = parent.Find(name);
            Button button = existing != null ? existing.GetComponent<Button>() : null;
            if (button != null && button.GetComponentInChildren<TMP_Text>(true) == null) button = null;
            if (button == null)
            {
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                button = InGameMenuSetup.CreateButton(name, parent, font, text, position, size);
            }

            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
            return button;
        }

        private static TMP_Text EnsureLabel(Transform parent, TMP_FontAsset font, string name, string text, float fontSize, Vector2 position, Vector2 size)
        {
            // Un hijo homónimo sin TMP_Text se recrea en lugar de reutilizarse
            Transform existing = parent.Find(name);
            TMP_Text label = existing != null ? existing.GetComponent<TMP_Text>() : null;
            if (label == null)
            {
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                label = InGameMenuSetup.CreateLabel(name, parent, font, text, fontSize, position, size);
            }

            var rect = (RectTransform)label.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return label;
        }
    }
}
