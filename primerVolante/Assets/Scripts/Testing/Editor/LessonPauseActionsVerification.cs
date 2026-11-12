using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de "Reiniciar / Salir de la lección" desde el menú de pausa:
    /// 1.  Las 6 escenas de lección tienen el menú armado (canvas 600x500, botones, ConfirmPanel, LessonPauseActions cableado).
    /// 2.  NewMainMenu y CityWithInfractions no tienen el componente ni los botones.
    /// 3.  Correr el setup dos veces no duplica ni cambia nada.
    /// 4-7, 10. Comportamiento del componente y del menú (confirmación, cierre, bloqueo por desconexión, bloqueo de la transición).
    /// 8.  Sin LessonManager los botones quedan inactivos.
    /// 9.  El flow manager devuelve false con una transición en curso y limpia todos los InfractionManager.
    /// 11. El Reiniciar del HUD cae a StartLesson cuando el flow manager devuelve false.
    /// La rama "true" de Reiniciar/Salir (transición real) se prueba en Play Mode. Todo sobre copias temporales de las escenas.
    /// </summary>
    public static class LessonPauseActionsVerification
    {
        private const string NEW_MAIN_MENU_SCENE = "Assets/Scenes/car09/NewMainMenu.unity";
        private const string CITY_WITH_INFRACTIONS_SCENE = "Assets/Scenes/car09/CityWithInfractions.unity";
        private const string BEHAVIOUR_SCENE = "Assets/Scenes/Lessons/Lesson_05_TurnIntersections.unity";
        private const BindingFlags k_Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Verify Lesson Pause Actions")]
        public static void RunVerification()
        {
            s_Total = 0;
            s_Passed = 0;
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE REINICIAR / SALIR DE LA LECCION (MENU DE PAUSA)");
            Debug.Log("=========================================================");

            // Escenas guardadas que estaban limpias: la verificación no puede ensuciarlas
            var cleanScenePaths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(open.path) && !open.isDirty) cleanScenePaths.Add(open.path);
            }

            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;
            Scene previousActive = SceneManager.GetActiveScene();
            try
            {
                VerifyLessonScenes();
                VerifyOtherScenesUntouched();
                VerificationSceneUtil.WithSceneCopy(BEHAVIOUR_SCENE, VerifyIdempotentSetup);
                VerificationSceneUtil.WithSceneCopy(BEHAVIOUR_SCENE, VerifyBehaviour);
                VerificationSceneUtil.WithSceneCopy(BEHAVIOUR_SCENE, VerifyWithoutLessonManager);
                VerificationSceneUtil.WithSceneCopy(BEHAVIOUR_SCENE, VerifyFlowManager);
                VerificationSceneUtil.WithSceneCopy(BEHAVIOUR_SCENE, VerifyHudFallback);
                VerifySourceRules();
                Check(!SimulationPause.IsPaused, "Al terminar no queda ningún dueño de la pausa de simulación");

                bool stillClean = true;
                foreach (string path in cleanScenePaths)
                {
                    Scene scene = SceneManager.GetSceneByPath(path);
                    if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    {
                        stillClean = false;
                        Debug.LogError($"[FAIL] La verificación dejó sucia la escena abierta {path}");
                    }
                }
                Check(stillClean, "Ninguna escena guardada del usuario quedó modificada (isDirty) tras la verificación");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FAIL] LessonPauseActionsVerification: excepción no controlada: {ex}");
            }
            finally
            {
                SimulationPause.ResetForTests();
                Time.timeScale = timeScaleBefore;
                AudioListener.pause = audioPauseBefore;
                ResetFlowSingleton();
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }

            Debug.Log("=========================================================");
            if (s_Total > 0 && s_Passed == s_Total)
            {
                Debug.Log($"LESSON PAUSE ACTIONS: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            }
            else
            {
                Debug.LogError($"[FAIL] LESSON PAUSE ACTIONS: {s_Passed}/{s_Total} pruebas pasadas");
            }
            Debug.Log("=========================================================");
        }

        // =========================================================================
        // 1. Las 6 escenas de lección
        // =========================================================================
        private static void VerifyLessonScenes()
        {
            foreach (string path in LessonHUDMinimizeSetup.LESSON_SCENE_PATHS)
            {
                string label = Path.GetFileNameWithoutExtension(path);
                VerificationSceneUtil.WithScene(path, scene => VerifyLessonScene(scene, label));
            }
        }

        private static void VerifyLessonScene(Scene scene, string label)
        {
            LessonPauseActions[] all = VerificationSceneUtil.AllInScene<LessonPauseActions>(scene);
            Check(all.Length == 1, $"{label}: hay exactamente un LessonPauseActions ({all.Length})");
            if (all.Length != 1) return;

            LessonPauseActions actions = all[0];
            InGameMenuController menu = actions.GetComponent<InGameMenuController>();
            Check(menu != null && actions.Menu == menu, $"{label}: LessonPauseActions está en el GameObject del InGameMenuController y apunta a él");
            Check(actions.MainPanel != null && actions.RestartButton != null && actions.ExitButton != null && actions.ConfirmPanel != null
                  && actions.ConfirmTitle != null && actions.ConfirmMessage != null && actions.ConfirmButton != null
                  && actions.ConfirmButtonLabel != null && actions.CancelButton != null,
                $"{label}: todas las referencias de LessonPauseActions están cableadas");
            if (actions.MainPanel == null || actions.ConfirmPanel == null || menu == null) return;

            RectTransform canvas = menu.MenuRoot != null ? menu.MenuRoot.transform as RectTransform : null;
            Check(canvas != null && canvas.sizeDelta == new Vector2(600f, 500f), $"{label}: InGameMenu_Canvas mide 600x500 ({(canvas != null ? canvas.sizeDelta.ToString() : "?")})");

            Transform main = actions.MainPanel.transform;
            CheckButton(main, "Button_Resume", "Reanudar", new Vector2(0f, -150f), new Vector2(300f, 60f), label);
            CheckButton(main, "Button_Calibrate", "Calibrar posición", new Vector2(0f, -230f), new Vector2(300f, 60f), label);
            CheckButton(main, LessonPauseActionsSetup.RESTART_BUTTON, "Reiniciar lección", new Vector2(0f, -310f), new Vector2(300f, 60f), label);
            CheckButton(main, LessonPauseActionsSetup.EXIT_BUTTON, "Salir de la lección", new Vector2(0f, -390f), new Vector2(300f, 60f), label);
            Check(main.Find(LessonPauseActionsSetup.RESTART_BUTTON) == actions.RestartButton.transform
                  && main.Find(LessonPauseActionsSetup.EXIT_BUTTON) == actions.ExitButton.transform,
                $"{label}: los botones nuevos del componente son los de MainPanel");

            // Reanudar sigue cableado a CloseMenu (persistente)
            Transform resume = main.Find("Button_Resume");
            Check(resume != null && resume.GetComponent<Button>().onClick.GetPersistentEventCount() == 1
                  && resume.GetComponent<Button>().onClick.GetPersistentMethodName(0) == nameof(InGameMenuController.CloseMenu),
                $"{label}: Button_Resume sigue cableado a CloseMenu");

            // ConfirmPanel
            Transform confirm = actions.ConfirmPanel.transform;
            Check(confirm.parent == main.parent && !actions.ConfirmPanel.activeSelf, $"{label}: ConfirmPanel es hermano de MainPanel y está inactivo");
            Check(confirm.Find("Title") != null && confirm.Find("Message") != null, $"{label}: ConfirmPanel tiene Title y Message");
            TMP_Text message = confirm.Find("Message") != null ? confirm.Find("Message").GetComponent<TMP_Text>() : null;
            Check(message != null && message.textWrappingMode == TextWrappingModes.Normal && message.alignment == TextAlignmentOptions.Center
                  && Mathf.Approximately(((RectTransform)message.transform).sizeDelta.x, 520f),
                $"{label}: Message tiene wrapping, está centrado y mide ~520 de ancho");
            CheckButton(confirm, "Button_Confirm", null, new Vector2(-130f, -380f), new Vector2(240f, 60f), label);
            CheckButton(confirm, "Button_Cancel", "Cancelar", new Vector2(130f, -380f), new Vector2(240f, 60f), label);
            Check(confirm.Find("Button_Confirm") != null && confirm.Find("Button_Confirm").GetComponent<Button>() == actions.ConfirmButton
                  && confirm.Find("Button_Cancel") != null && confirm.Find("Button_Cancel").GetComponent<Button>() == actions.CancelButton,
                $"{label}: Button_Confirm y Button_Cancel son los del componente");
        }

        private static void CheckButton(Transform parent, string name, string expectedText, Vector2 position, Vector2 size, string label)
        {
            Transform t = parent.Find(name);
            Check(t != null && t.GetComponent<Button>() != null, $"{label}: existe {name} en {parent.name}");
            if (t == null) return;

            var rect = (RectTransform)t;
            Check(rect.anchoredPosition == position && rect.sizeDelta == size,
                $"{label}: {name} en {position} con tamaño {size} (es {rect.anchoredPosition} / {rect.sizeDelta})");
            if (expectedText != null)
            {
                TMP_Text text = t.GetComponentInChildren<TMP_Text>(true);
                Check(text != null && text.text == expectedText, $"{label}: {name} dice \"{expectedText}\" (dice \"{(text != null ? text.text : "?")}\")");
            }
        }

        // =========================================================================
        // 2. Escenas que no deben cambiar
        // =========================================================================
        private static void VerifyOtherScenesUntouched()
        {
            foreach (string path in new[] { NEW_MAIN_MENU_SCENE, CITY_WITH_INFRACTIONS_SCENE })
            {
                string label = Path.GetFileNameWithoutExtension(path);
                VerificationSceneUtil.WithScene(path, scene =>
                {
                    Check(VerificationSceneUtil.AllInScene<LessonPauseActions>(scene).Length == 0, $"{label}: no tiene LessonPauseActions");
                    bool hasButtons = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                        .Any(t => t.name == LessonPauseActionsSetup.RESTART_BUTTON || t.name == LessonPauseActionsSetup.EXIT_BUTTON
                                  || t.name == LessonPauseActionsSetup.CONFIRM_PANEL);
                    Check(!hasButtons, $"{label}: no tiene Button_RestartLesson, Button_ExitLesson ni ConfirmPanel");
                });
            }
        }

        // =========================================================================
        // 3. Idempotencia del setup
        // =========================================================================
        private static void VerifyIdempotentSetup(Scene scene)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                string before = Signature(scene);
                LessonPauseActions first = LessonPauseActionsSetup.SetupActiveScene();
                string afterOne = Signature(scene);
                LessonPauseActionsSetup.SetupActiveScene();
                string afterTwo = Signature(scene);

                Check(first != null, "SetupActiveScene devuelve el componente");
                Check(before == afterOne, "Correr el setup sobre una escena ya configurada no cambia nada");
                Check(afterOne == afterTwo, "Correr el setup dos veces no cambia nada");

                int Count(string n) => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Count(t => t.name == n);
                Check(Count(LessonPauseActionsSetup.RESTART_BUTTON) == 1 && Count(LessonPauseActionsSetup.EXIT_BUTTON) == 1
                      && Count(LessonPauseActionsSetup.CONFIRM_PANEL) == 1 && Count("Button_Confirm") == 1 && Count("Button_Cancel") == 1
                      && VerificationSceneUtil.AllInScene<LessonPauseActions>(scene).Length == 1,
                    "Tras dos corridas no hay objetos ni componentes duplicados");

                // Reubica lo que alguien movió a mano
                Transform restartT = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .First(t => t.name == LessonPauseActionsSetup.RESTART_BUTTON);
                ((RectTransform)restartT).anchoredPosition = new Vector2(55f, 55f);
                LessonPauseActionsSetup.SetupActiveScene();
                Check(((RectTransform)restartT).anchoredPosition == LessonPauseActionsSetup.RESTART_POSITION, "El setup reubica un botón que estaba movido");

                // 7. Hijos homónimos sin Button / TMP_Text: el setup los recrea y no lanza
                Transform exitT = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .First(t => t.name == LessonPauseActionsSetup.EXIT_BUTTON);
                Transform messageT = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .First(t => t.name == "Message" && t.parent != null && t.parent.name == LessonPauseActionsSetup.CONFIRM_PANEL);
                UnityEngine.Object.DestroyImmediate(exitT.GetComponent<Button>());
                UnityEngine.Object.DestroyImmediate(messageT.GetComponent<TMP_Text>());
                LessonPauseActions repaired = null;
                bool threw = false;
                try { repaired = LessonPauseActionsSetup.SetupActiveScene(); }
                catch (Exception ex) { threw = true; Debug.LogWarning($"SetupActiveScene lanzó: {ex}"); }
                Check(!threw && repaired != null, "El setup no lanza con hijos homónimos sin Button ni TMP_Text");
                if (repaired != null)
                {
                    Check(repaired.ExitButton != null && repaired.ExitButton.GetComponentInChildren<TMP_Text>(true) != null
                          && repaired.ConfirmMessage != null && repaired.ConfirmMessage.text.Length > 0
                          && Count(LessonPauseActionsSetup.EXIT_BUTTON) == 1
                          && repaired.ConfirmPanel.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Message") == 1,
                        "El setup recrea el botón y el label rotos, y los vuelve a cablear sin duplicar");
                }
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        private static string Signature(Scene scene)
        {
            var sb = new StringBuilder();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    sb.Append(PathOf(t)).Append('|').Append(t.gameObject.activeSelf).Append('|').Append(t.gameObject.layer)
                      .Append('|').Append(t.GetComponents<Component>().Length);
                    if (t is RectTransform r) sb.Append('|').Append(r.anchoredPosition).Append(r.sizeDelta);
                    TMP_Text text = t.GetComponent<TMP_Text>();
                    if (text != null) sb.Append('|').Append(text.text);
                    sb.Append('\n');
                }
            }

            foreach (LessonPauseActions a in VerificationSceneUtil.AllInScene<LessonPauseActions>(scene))
            {
                var so = new SerializedObject(a);
                SerializedProperty it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        UnityEngine.Object target = it.objectReferenceValue;
                        Transform targetTransform = target is Component comp ? comp.transform : target is GameObject go ? go.transform : null;
                        sb.Append(it.propertyPath).Append('=').Append(targetTransform != null ? PathOf(targetTransform) : "null").Append('\n');
                    }
                }
            }
            return sb.ToString();
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null; c = c.parent) parts.Add(c.name + "#" + c.GetSiblingIndex());
            parts.Reverse();
            return string.Join("/", parts);
        }

        // =========================================================================
        // 4-7, 10. Comportamiento
        // =========================================================================
        private static void Prepare(Scene scene, out InGameMenuController menu, out LessonPauseActions actions)
        {
            menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).First();
            actions = VerificationSceneUtil.AllInScene<LessonPauseActions>(scene).First();
            menu.MenuRoot.SetActive(false);
            SimulationPause.ResetForTests();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Invoke(actions, "OnEnable");
        }

        private static void Finish(InGameMenuController menu, LessonPauseActions actions)
        {
            Invoke(actions, "OnDisable");
            if (menu != null && menu.MenuRoot != null) menu.MenuRoot.SetActive(false);
            SimulationPause.ResetForTests();
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private static void VerifyBehaviour(Scene scene)
        {
            Prepare(scene, out InGameMenuController menu, out LessonPauseActions actions);
            GameObject flowGo = null;
            try
            {
                Check(actions.IsActive, "Con LessonManager en la escena, el componente queda activo");
                Check(actions.RestartButton.gameObject.activeSelf && actions.ExitButton.gameObject.activeSelf, "Reiniciar y Salir están activos");

                // 4. Reiniciar / Salir -> confirmación -> cancelar
                menu.OpenMenu();
                Check(actions.MainPanel.activeSelf && !actions.ConfirmPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None,
                    "Menú recién abierto: MainPanel visible y sin confirmación");

                actions.RestartButton.onClick.Invoke();
                Check(!actions.MainPanel.activeSelf && actions.ConfirmPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.Restart,
                    "Click en Reiniciar: MainPanel se oculta y ConfirmPanel se muestra");
                Check(actions.ConfirmTitle.text == "¿Reiniciar la lección?"
                      && actions.ConfirmMessage.text == "Vas a volver al primer paso. Se pierden el progreso y las infracciones de esta lección."
                      && actions.ConfirmButtonLabel.text == "Reiniciar",
                    "Confirmación de reinicio: título, mensaje y botón con los textos acordados");
                actions.CancelButton.onClick.Invoke();
                Check(actions.MainPanel.activeSelf && !actions.ConfirmPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None
                      && !menu.IsLockedForTransition && menu.IsOpen,
                    "Cancelar vuelve a MainPanel, oculta ConfirmPanel y no bloquea el menú");

                actions.ExitButton.onClick.Invoke();
                Check(!actions.MainPanel.activeSelf && actions.ConfirmPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.Exit,
                    "Click en Salir: MainPanel se oculta y ConfirmPanel se muestra");
                Check(actions.ConfirmTitle.text == "¿Salir de la lección?"
                      && actions.ConfirmMessage.text == "Vas a volver al menú principal. Se pierden el progreso y las infracciones de esta lección."
                      && actions.ConfirmButtonLabel.text == "Salir" && actions.CancelButton.GetComponentInChildren<TMP_Text>().text == "Cancelar",
                    "Confirmación de salida: título, mensaje y botones con los textos acordados");
                actions.CancelButton.onClick.Invoke();
                Check(actions.MainPanel.activeSelf && !actions.ConfirmPanel.activeSelf, "Cancelar la salida vuelve a MainPanel");

                // 5. MenuOpened / MenuClosed / GameplayInputBlockingChanged(true) cierran la confirmación
                actions.RestartButton.onClick.Invoke();
                menu.OpenMenu();
                Check(!actions.ConfirmPanel.activeSelf && actions.MainPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None,
                    "MenuOpened cierra la confirmación pendiente");

                actions.RestartButton.onClick.Invoke();
                menu.CloseMenu();
                Check(!actions.ConfirmPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None && !menu.IsOpen,
                    "MenuClosed cierra la confirmación pendiente");

                menu.OpenMenu();
                actions.ExitButton.onClick.Invoke();
                menu.OpenMenuWithNotice("Se desconectó el visor");
                Check(!actions.ConfirmPanel.activeSelf && actions.MainPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None
                      && menu.IsNoticeVisible,
                    "GameplayInputBlockingChanged(true) cierra la confirmación y deja visible MainPanel con el aviso");

                // 6. Con bloqueo por desconexión, Reiniciar y Salir no son interactuables
                menu.SetResumeBlocked(true);
                actions.RefreshInteractable();
                Check(!actions.RestartButton.interactable && !actions.ExitButton.interactable && !menu.ResumeButton.interactable,
                    "Con la pausa por desconexión, Reiniciar y Salir no son interactuables (igual que Reanudar)");
                actions.RestartButton.onClick.Invoke();
                Check(actions.Pending == LessonPauseActions.PendingAction.None && !actions.ConfirmPanel.activeSelf,
                    "Con la pausa por desconexión, un click en Reiniciar no abre la confirmación");

                menu.EndConnectionPause();
                Check(actions.RestartButton.interactable && actions.ExitButton.interactable && menu.ResumeButton.interactable,
                    "Al terminar el bloqueo, Reiniciar y Salir vuelven a ser interactuables");

                // 7. LockForTransition (el bloqueo depende de que el flow manager esté en transición)
                menu.CloseMenu();
                flowGo = NewFlowManager(scene, transitioning: true);
                LessonSceneFlowManager flow = flowGo.GetComponent<LessonSceneFlowManager>();
                menu.OpenMenu();
                Check(menu.IsOpen && menu.IsSimulationPaused && Time.timeScale == 0f, "Precondición: menú abierto y simulación pausada");
                menu.LockForTransition();
                menu.CloseMenu();
                Check(menu.IsLockedForTransition && menu.IsOpen && Time.timeScale == 0f, "Con el bloqueo, CloseMenu (Reanudar) no cierra el menú ni despausa");
                menu.ToggleMenu();
                Check(menu.IsOpen && Time.timeScale == 0f && menu.IsSimulationPaused, "Con el bloqueo, ToggleMenu (Menu / Escape) no cierra el menú ni despausa");
                actions.RefreshInteractable();
                Check(!actions.RestartButton.interactable && !actions.ExitButton.interactable && !actions.ConfirmButton.interactable
                      && !actions.CancelButton.interactable && !menu.ResumeButton.interactable,
                    "Con el bloqueo, todos los botones del menú quedan no interactuables");
                menu.SetResumeBlocked(false);
                actions.RefreshInteractable();
                Check(!actions.RestartButton.interactable && !menu.ResumeButton.interactable,
                    "Terminar un bloqueo por desconexión no reactiva los botones mientras dura la transición");

                // OpenMenuWithNotice sigue funcionando con el menú bloqueado, y RestorePreviousMode no lo cierra
                menu.OpenMenuWithNotice("Se desconectó el visor");
                Check(menu.IsBlockingGameplayInput && menu.IsNoticeVisible && menu.IsOpen, "Con el bloqueo, OpenMenuWithNotice sigue funcionando");
                menu.EndConnectionPause();
                Check(menu.IsOpen && Time.timeScale == 0f, "Con el bloqueo, terminar la pausa por desconexión no cierra el menú");

                // Camino Closed de RestorePreviousMode (pasa por CloseMenu): se quita el bloqueo por reflexión para armarlo
                SetLockedForTransition(menu, false);
                menu.CloseMenu();
                Check(!menu.IsOpen, "Precondición: menú cerrado para probar RestorePreviousMode");
                menu.OpenMenuWithNotice("Se desconectó el visor");
                menu.LockForTransition();
                menu.CloseMenu();
                menu.ToggleMenu();
                menu.EndConnectionPause();
                Check(menu.IsOpen && Time.timeScale == 0f && menu.IsSimulationPaused,
                    "Con el bloqueo, RestorePreviousMode (modo previo cerrado) no cierra el menú ni libera la pausa");
                SetLockedForTransition(menu, false);

                // 10. Confirmar con el flow manager devolviendo false
                menu.CloseMenu();
                menu.OpenMenu();
                actions.RestartButton.onClick.Invoke();
                actions.ConfirmButton.onClick.Invoke();
                Check(!menu.IsLockedForTransition && actions.MainPanel.activeSelf && !actions.ConfirmPanel.activeSelf
                      && actions.Pending == LessonPauseActions.PendingAction.None && menu.IsOpen,
                    "Confirmar Reiniciar con el flow manager devolviendo false no bloquea el menú y vuelve a MainPanel");
                actions.ExitButton.onClick.Invoke();
                actions.ConfirmButton.onClick.Invoke();
                Check(!menu.IsLockedForTransition && actions.MainPanel.activeSelf && !actions.ConfirmPanel.activeSelf,
                    "Confirmar Salir con el flow manager devolviendo false no bloquea el menú y vuelve a MainPanel");
                actions.ConfirmButton.onClick.Invoke();
                Check(!menu.IsLockedForTransition && actions.Pending == LessonPauseActions.PendingAction.None,
                    "Confirmar sin confirmación pendiente no hace nada");

                // 2. El bloqueo se libera cuando la transición termina y el menú sigue vivo
                actions.RestartButton.onClick.Invoke();
                menu.LockForTransition();
                actions.RefreshInteractable();
                Check(menu.IsLockedForTransition && !actions.RestartButton.interactable && actions.ConfirmPanel.activeSelf,
                    "Precondición: con la transición en curso el menú está bloqueado y la confirmación sigue abierta");
                SetField(flow, "m_IsTransitioning", false);
                Check(!menu.IsLockedForTransition, "Si la transición termina con el menú vivo, el bloqueo se libera solo");
                actions.RefreshInteractable();
                Check(actions.RestartButton.interactable && actions.ExitButton.interactable && actions.ConfirmButton.interactable
                      && actions.CancelButton.interactable && menu.ResumeButton.interactable,
                    "Liberado el bloqueo, los botones del menú vuelven a ser interactuables");
                Check(!actions.ConfirmPanel.activeSelf && actions.MainPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None,
                    "Liberado el bloqueo, la confirmación se cierra y se vuelve a MainPanel");
                menu.CloseMenu();
                Check(!menu.IsOpen && !menu.IsSimulationPaused && Time.timeScale == 1f, "Liberado el bloqueo, Reanudar cierra el menú y despausa");

                // Sin flow manager el bloqueo no se queda colgado
                UnityEngine.Object.DestroyImmediate(flowGo);
                flowGo = null;
                ResetFlowSingleton();
                menu.OpenMenu();
                menu.LockForTransition();
                Check(!menu.IsLockedForTransition, "Sin flow manager el bloqueo se libera (no hay transición que lo mantenga)");
                menu.CloseMenu();
                Check(!menu.IsOpen && Time.timeScale == 1f, "Sin flow manager el menú puede cerrarse");

                // 6. TryGetInstance no crea el singleton y el componente se comporta sin flow manager
                Check(!LessonSceneFlowManager.TryGetInstance(out LessonSceneFlowManager none) && none == null
                      && UnityEngine.Object.FindAnyObjectByType<LessonSceneFlowManager>() == null,
                    "TryGetInstance sin flow manager devuelve false y no crea ningún GameObject");
                menu.OpenMenu();
                actions.RestartButton.onClick.Invoke();
                actions.ConfirmButton.onClick.Invoke();
                Check(!menu.IsLockedForTransition && actions.MainPanel.activeSelf && !actions.ConfirmPanel.activeSelf
                      && UnityEngine.Object.FindAnyObjectByType<LessonSceneFlowManager>() == null,
                    "Confirmar sin flow manager no bloquea el menú, vuelve a MainPanel y no crea el singleton");
                flowGo = NewFlowManager(scene, transitioning: false);
                Check(LessonSceneFlowManager.TryGetInstance(out LessonSceneFlowManager found) && found == flowGo.GetComponent<LessonSceneFlowManager>(),
                    "TryGetInstance devuelve el flow manager existente");

                // 5. OnDisable con la confirmación abierta restaura los paneles
                actions.RestartButton.onClick.Invoke();
                Check(actions.ConfirmPanel.activeSelf && !actions.MainPanel.activeSelf, "Precondición: confirmación abierta");
                Invoke(actions, "OnDisable");
                Check(!actions.ConfirmPanel.activeSelf && actions.MainPanel.activeSelf && actions.Pending == LessonPauseActions.PendingAction.None,
                    "OnDisable con la confirmación abierta: ConfirmPanel se cierra y MainPanel vuelve a mostrarse");
                Invoke(actions, "OnEnable");
                menu.CloseMenu();
            }
            finally
            {
                if (flowGo != null) UnityEngine.Object.DestroyImmediate(flowGo);
                ResetFlowSingleton();
                Finish(menu, actions);
            }
        }

        // =========================================================================
        // 8. Sin LessonManager
        // =========================================================================
        private static void VerifyWithoutLessonManager(Scene scene)
        {
            foreach (LessonManager lm in VerificationSceneUtil.AllInScene<LessonManager>(scene))
            {
                UnityEngine.Object.DestroyImmediate(lm);
            }
            Check(VerificationSceneUtil.AllInScene<LessonManager>(scene).Length == 0, "Precondición: la copia de la escena no tiene LessonManager");

            InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).First();
            LessonPauseActions actions = VerificationSceneUtil.AllInScene<LessonPauseActions>(scene).First();
            try
            {
                menu.MenuRoot.SetActive(false);
                Invoke(actions, "OnEnable");
                Check(!actions.IsActive && !actions.RestartButton.gameObject.activeSelf && !actions.ExitButton.gameObject.activeSelf,
                    "Sin LessonManager, Button_RestartLesson y Button_ExitLesson quedan inactivos");
                actions.RestartButton.onClick.Invoke();
                actions.ConfirmButton.onClick.Invoke();
                Check(actions.Pending == LessonPauseActions.PendingAction.None && !actions.ConfirmPanel.activeSelf && !menu.IsLockedForTransition,
                    "Sin LessonManager, el componente no hace nada más (sin confirmación ni bloqueo)");
            }
            finally
            {
                Invoke(actions, "OnDisable");
            }
        }

        // =========================================================================
        // 9. LessonSceneFlowManager
        // =========================================================================
        private static void VerifyFlowManager(Scene scene)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            Check(catalog != null, "Existe el catálogo de infracciones para la prueba");
            if (catalog == null) return;

            GameObject enabledGo = null, disabledGo = null, flowGo = null;
            try
            {
                enabledGo = new GameObject("TMP_InfractionManager_Enabled");
                disabledGo = new GameObject("TMP_InfractionManager_Disabled");
                SceneManager.MoveGameObjectToScene(enabledGo, scene);
                SceneManager.MoveGameObjectToScene(disabledGo, scene);
                InfractionManager enabledMgr = enabledGo.AddComponent<InfractionManager>();
                InfractionManager disabledMgr = disabledGo.AddComponent<InfractionManager>();
                enabledMgr.SetCatalog(catalog);
                disabledMgr.SetCatalog(catalog);
                disabledGo.SetActive(false);

                int cleared = 0;
                enabledMgr.Cleared += () => cleared++;
                disabledMgr.Cleared += () => cleared++;
                Report(enabledMgr);
                Report(disabledMgr);
                Check(enabledMgr.TotalCount == 1 && disabledMgr.TotalCount == 1, "Precondición: los dos InfractionManager tienen una infracción");

                // Transición en curso: devuelven false y no limpian
                flowGo = NewFlowManager(scene, transitioning: true);
                var flow = flowGo.GetComponent<LessonSceneFlowManager>();
                SetField(flow, "m_CurrentOverlayScene", "Lesson_05_TurnIntersections");
                bool restart = flow.RestartCurrentLesson();
                bool exit = flow.ReturnToMainMenu();
                Check(!restart && !exit, "Con una transición en curso, RestartCurrentLesson y ReturnToMainMenu devuelven false");
                Check(enabledMgr.TotalCount == 1 && disabledMgr.TotalCount == 1 && cleared == 0, "Con una transición en curso no se limpian las infracciones");

                // Overlay que no es una lección: Reiniciar devuelve false y no limpia
                SetField(flow, "m_IsTransitioning", false);
                foreach (string menuScene in new[] { "", LessonSceneFlowManager.NEW_MAIN_MENU_SCENE, LessonSceneFlowManager.MAIN_MENU_SCENE })
                {
                    SetField(flow, "m_CurrentOverlayScene", menuScene);
                    bool r = flow.RestartCurrentLesson();
                    Check(!r && enabledMgr.TotalCount == 1 && disabledMgr.TotalCount == 1,
                        $"RestartCurrentLesson con el overlay \"{menuScene}\" devuelve false y no limpia");
                }

                // La limpieza que hacen ambos métodos al arrancar la transición: todos los gestores, incluido el deshabilitado
                int count = LessonSceneFlowManager.ClearAllInfractionManagers();
                Check(count >= 2, $"ClearAllInfractionManagers encuentra al menos los 2 gestores de la prueba ({count})");
                Check(enabledMgr.TotalCount == 0 && disabledMgr.TotalCount == 0 && cleared == 2,
                    "ClearAllInfractionManagers vacía el gestor habilitado y el deshabilitado (Clear() una vez cada uno)");
            }
            finally
            {
                if (flowGo != null) UnityEngine.Object.DestroyImmediate(flowGo);
                if (enabledGo != null) UnityEngine.Object.DestroyImmediate(enabledGo);
                if (disabledGo != null) UnityEngine.Object.DestroyImmediate(disabledGo);
                ResetFlowSingleton();
            }
        }

        private static void Report(InfractionManager manager)
        {
            InfractionReport report = InfractionReport.Create(InfractionIds.SpeedLimit, null, Vector3.zero, ("limit", "40"), ("speed", "52"));
            manager.Report(report);
        }

        // =========================================================================
        // 11. LessonHUDPanel.OnRestartClicked
        // =========================================================================
        private static void VerifyHudFallback(Scene scene)
        {
            LessonHUDPanel hud = VerificationSceneUtil.AllInScene<LessonHUDPanel>(scene).First();
            GameObject managerGo = null, flowGo = null;
            LessonDefinitionSO lesson = null;
            LessonStepSO step = null;
            try
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                step.stepTitle = "Paso de prueba";
                step.completionMode = LessonStepCompletionMode.ManualContinue;
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                lesson.lessonId = "LESSON_TEST";
                lesson.steps = new List<LessonStepSO> { step };

                managerGo = new GameObject("TMP_LessonManager");
                SceneManager.MoveGameObjectToScene(managerGo, scene);
                LessonManager manager = managerGo.AddComponent<LessonManager>();
                var soManager = new SerializedObject(manager);
                soManager.FindProperty("m_ActiveLesson").objectReferenceValue = lesson;
                soManager.ApplyModifiedPropertiesWithoutUndo();

                var soHud = new SerializedObject(hud);
                soHud.FindProperty("m_LessonManager").objectReferenceValue = manager;
                soHud.ApplyModifiedPropertiesWithoutUndo();

                int started = 0;
                manager.LessonStarted += _ => started++;
                Check(!manager.IsLessonActive, "Precondición: la lección de prueba no empezó");

                // Con una transición en curso no se hace nada: StartLesson en el lugar actuaría sobre la escena que se descarga
                flowGo = NewFlowManager(scene, transitioning: true);
                Invoke(hud, "OnRestartClicked");
                Invoke(hud, "OnRestartClicked");
                Check(started == 0 && !manager.IsLessonActive, "OnRestartClicked con una transición en curso no llama a StartLesson (ni con doble click)");

                // Sin transición pero sin un overlay de lección, RestartCurrentLesson devuelve false y se cae al fallback
                SetField(flowGo.GetComponent<LessonSceneFlowManager>(), "m_IsTransitioning", false);
                SetField(flowGo.GetComponent<LessonSceneFlowManager>(), "m_CurrentOverlayScene", LessonSceneFlowManager.NEW_MAIN_MENU_SCENE);
                Invoke(hud, "OnRestartClicked");
                Check(started == 1 && manager.IsLessonActive && manager.CurrentStepIndex == 0,
                    "OnRestartClicked con un overlay que no es lección cae al StartLesson en el lugar");

                // Sin flow manager también se usa el fallback
                UnityEngine.Object.DestroyImmediate(flowGo);
                flowGo = null;
                ResetFlowSingleton();
                Invoke(hud, "OnRestartClicked");
                Check(started == 2, "OnRestartClicked sin flow manager cae al StartLesson en el lugar");
                Check(UnityEngine.Object.FindAnyObjectByType<LessonSceneFlowManager>() == null, "OnRestartClicked no creó un LessonSceneFlowManager");
            }
            finally
            {
                if (flowGo != null) UnityEngine.Object.DestroyImmediate(flowGo);
                if (managerGo != null) UnityEngine.Object.DestroyImmediate(managerGo);
                if (lesson != null) UnityEngine.Object.DestroyImmediate(lesson);
                if (step != null) UnityEngine.Object.DestroyImmediate(step);
                ResetFlowSingleton();
            }
        }

        // =========================================================================
        // 1 y 4. Reglas de código de la transición (no se puede correr la corrutina real en Edit Mode)
        // =========================================================================
        private static void VerifySourceRules()
        {
            const string path = "Assets/Scripts/Lessons/LessonSceneFlowManager.cs";
            string source = File.ReadAllText(path);

            Check(!System.Text.RegularExpressions.Regex.IsMatch(source, @"new\s+WaitForSeconds\s*\(") && source.Contains("WaitForSecondsRealtime(0.2f)"),
                "La transición no usa esperas escaladas (WaitForSeconds): con timeScale 0 se colgaría; usa WaitForSecondsRealtime");

            int routine = source.IndexOf("private IEnumerator TransitionToSceneRoutine", StringComparison.Ordinal);
            int unload = source.IndexOf("SceneManager.UnloadSceneAsync(currentScene)", routine, StringComparison.Ordinal);
            int clear = source.IndexOf("if (clearInfractions) ClearAllInfractionManagers();", routine, StringComparison.Ordinal);
            int load = source.IndexOf("SceneManager.LoadSceneAsync(targetSceneName", routine, StringComparison.Ordinal);
            Check(routine > 0 && unload > routine && clear > unload && load > clear,
                "La limpieza de infracciones ocurre en la corrutina, después de descargar el overlay y antes de cargar el destino");

            string restart = MethodBody(source, "public bool RestartCurrentLesson()");
            string menu = MethodBody(source, "public bool ReturnToMainMenu()");
            Check(restart.Length > 0 && menu.Length > 0 && !restart.Contains("ClearAllInfractionManagers") && !menu.Contains("ClearAllInfractionManagers")
                  && restart.Contains("TransitionToSceneRoutine(m_CurrentOverlayScene, true)") && menu.Contains(", true)"),
                "RestartCurrentLesson y ReturnToMainMenu no limpian antes del fade: piden la limpieza a la corrutina");
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return "";
            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            return "";
        }

        // =========================================================================
        // Utilidades
        // =========================================================================

        // Awake no corre en Edit Mode: el singleton se resuelve por FindAnyObjectByType (Instance), nunca crea uno con DontDestroyOnLoad.
        private static GameObject NewFlowManager(Scene scene, bool transitioning)
        {
            ResetFlowSingleton();
            var go = new GameObject("TMP_LessonSceneFlowManager");
            SceneManager.MoveGameObjectToScene(go, scene);
            var flow = go.AddComponent<LessonSceneFlowManager>();
            SetField(flow, "m_IsTransitioning", transitioning);
            return go;
        }

        private static void ResetFlowSingleton()
        {
            typeof(LessonSceneFlowManager).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        }

        private static void SetLockedForTransition(InGameMenuController menu, bool value) => SetField(menu, "m_LockedForTransition", value);

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, k_Instance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, k_Instance);
            if (m == null) throw new MissingMethodException(target.GetType().Name, method);
            try { m.Invoke(target, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static void Check(bool condition, string testName)
        {
            s_Total++;
            if (condition)
            {
                s_Passed++;
                Debug.Log($"  [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"[FAIL] {testName}");
            }
        }
    }
}
