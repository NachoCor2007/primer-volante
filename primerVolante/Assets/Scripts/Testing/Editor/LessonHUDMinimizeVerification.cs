using System;
using System.IO;
using System.Reflection;
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
    /// Verificación en Editor de minimizar/maximizar el HUD del paso de las lecciones:
    /// 1-2. Las 6 escenas de lección tienen MinimizedBar, Button_Minimize y Button_Maximize bien anclados y cableados.
    /// 3-7. Comportamiento de <see cref="LessonHUDPanel"/> sobre un HUD de prueba construido con el mismo
    ///      código que el setup (<see cref="Lesson01SceneSetup.BuildMinimizeControls"/>).
    /// 8.   La tarjeta Menú / Pausa del menú principal menciona el botón A.
    /// 9.   El código lee el botón A del control derecho y la tecla H, y no usa APIs obsoletas.
    /// Las escenas se inspeccionan como escenas de preview, sin tocar la escena abierta.
    /// </summary>
    public static class LessonHUDMinimizeVerification
    {
        private const string MAIN_MENU_SCENE = "Assets/Scenes/car09/NewMainMenu.unity";
        private const string HUD_SCRIPT = "Assets/Scripts/Lessons/LessonHUDPanel.cs";
        private const string VEHICLE_SCRIPT = "Assets/Scripts/VR/VehicleController.cs";

        [MenuItem("Tools/Primer Volante/Verify Lesson HUD Minimize")]
        public static void RunVerification()
        {
            int total = 0, passed = 0;
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE MINIMIZAR/MAXIMIZAR DEL HUD DE LECCIONES");
            Debug.Log("=========================================================");

            try
            {
                VerifyLessonScenes(ref total, ref passed);
                VerifyHudBehaviour(ref total, ref passed);
                VerifyMainMenuCard(ref total, ref passed);
                VerifySourceRules(ref total, ref passed);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FAIL] LessonHUDMinimizeVerification: excepción no controlada: {ex}");
            }

            Debug.Log("=========================================================");
            if (total > 0 && passed == total)
            {
                Debug.Log($"LESSON HUD MINIMIZE: {passed}/{total} pruebas pasadas con exito!");
            }
            else
            {
                Debug.LogError($"[FAIL] LESSON HUD MINIMIZE: {passed}/{total} pruebas pasadas");
            }
            Debug.Log("=========================================================");
        }

        // =========================================================================
        // 1-2. Escenas de lección
        // =========================================================================
        private static void VerifyLessonScenes(ref int total, ref int passed)
        {
            foreach (string path in LessonHUDMinimizeSetup.LESSON_SCENE_PATHS)
            {
                string label = Path.GetFileNameWithoutExtension(path);
                Scene scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    LessonHUDPanel hud = null;
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        hud = root.GetComponentInChildren<LessonHUDPanel>(true);
                        if (hud != null) break;
                    }

                    Assert(hud != null, $"{label}: existe LessonHUDPanel (LessonHUD_Canvas)", ref total, ref passed);
                    if (hud == null) continue;

                    Transform bar = FindDeep(hud.transform, "MinimizedBar");
                    Transform minBtn = FindDeep(hud.transform, "Button_Minimize");
                    Transform maxBtn = FindDeep(hud.transform, "Button_Maximize");
                    Assert(bar != null && minBtn != null && maxBtn != null,
                        $"{label}: existen MinimizedBar, Button_Minimize y Button_Maximize", ref total, ref passed);

                    Assert(hud.Background != null && hud.MinimizedBar != null && hud.MinimizedStepText != null
                           && hud.MinimizeButton != null && hud.MaximizeButton != null,
                        $"{label}: las referencias nuevas de LessonHUDPanel no son nulas", ref total, ref passed);

                    if (bar == null || minBtn == null) continue;

                    RectTransform barRect = (RectTransform)bar;
                    Assert(Mathf.Approximately(barRect.anchorMax.y, 0f) && Mathf.Approximately(barRect.anchorMin.y, 0f)
                           && Mathf.Approximately(barRect.pivot.y, 0f),
                        $"{label}: MinimizedBar está anclada al borde inferior (anchor y = 0, pivot y = 0)", ref total, ref passed);
                    Assert(barRect.sizeDelta.y <= 50f && barRect.sizeDelta.y > 0f,
                        $"{label}: MinimizedBar tiene alto <= 50 ({barRect.sizeDelta.y})", ref total, ref passed);
                    Assert(!bar.gameObject.activeSelf, $"{label}: MinimizedBar arranca inactiva", ref total, ref passed);
                    Assert(hud.StepContentPanel != null && minBtn.IsChildOf(hud.StepContentPanel.transform),
                        $"{label}: Button_Minimize está dentro de StepContentPanel", ref total, ref passed);
                    Assert(maxBtn != null && maxBtn.IsChildOf(bar) && hud.MinimizedStepText != null
                           && hud.MinimizedStepText.transform.IsChildOf(bar),
                        $"{label}: Button_Maximize y el texto del paso están dentro de MinimizedBar", ref total, ref passed);
                    Assert(hud.Background != null && hud.Background.activeSelf && hud.Background.transform.parent == hud.transform,
                        $"{label}: Background está activo y es hijo directo del canvas", ref total, ref passed);

                    SerializedProperty keyProp = new SerializedObject(hud).FindProperty("m_ToggleMinimizeKey");
                    string keyName = keyProp.enumNames[keyProp.enumValueIndex];
                    Assert(keyName == "O" && !VehicleControllerReadsKey(keyName),
                        $"{label}: la tecla serializada del HUD es O y no choca con VehicleController (es {keyName})", ref total, ref passed);
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }

        // =========================================================================
        // 3-7. Comportamiento sobre un HUD de prueba
        // =========================================================================
        private static void VerifyHudBehaviour(ref int total, ref int passed)
        {
            // Los objetos de prueba se crean en una escena de trabajo aditiva y vacía (activa mientras corre la
            // verificación): no se pierde ni se ensucia la escena que tenga abierta el usuario.
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);

            var created = new System.Collections.Generic.List<UnityEngine.Object>();
            try
            {
                LessonHUDPanel hud = BuildTestHud(created, out GameObject menuRoot, out InGameMenuController menu);
                LessonStepSO step = ScriptableObject.CreateInstance<LessonStepSO>();
                step.stepTitle = "Encender el motor";
                step.instructionText = "Gira la llave.";
                step.completionMode = LessonStepCompletionMode.ManualContinue;
                var lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                lesson.lessonId = "LESSON_TEST";
                lesson.lessonTitle = "Lección de prueba";
                created.Add(step);
                created.Add(lesson);

                // Los listeners de los botones se cablean en SetupButtons (Awake no corre en Edit Mode y, además,
                // buscaría LessonManager/InGameMenuController en las escenas abiertas del usuario)
                Invoke(hud, "SetupButtons");
                hud.InGameMenuController = menu;

                // La tecla configurada del HUD no puede coincidir con una tecla que lea VehicleController
                string toggleKeyName = new SerializedObject(hud).FindProperty("m_ToggleMinimizeKey").enumNames[
                    new SerializedObject(hud).FindProperty("m_ToggleMinimizeKey").enumValueIndex];
                Assert(toggleKeyName == "O", $"La tecla por defecto del HUD es O (es {toggleKeyName})", ref total, ref passed);
                Assert(VehicleControllerReadsKey("H") && !VehicleControllerReadsKey("O"),
                    "El escaneo de VehicleController.cs detecta la tecla H (ToggleHazard) y no detecta la O", ref total, ref passed);
                Assert(!VehicleControllerReadsKey(toggleKeyName),
                    $"La tecla del HUD ({toggleKeyName}) no coincide con ninguna tecla que lea VehicleController", ref total, ref passed);

                // 3. SetMinimized / eventos
                Assert(!hud.IsMinimized && hud.Background.activeSelf && hud.StepContentPanel.activeSelf && !hud.MinimizedBar.activeSelf,
                    "Estado inicial: maximizado (Background y StepContentPanel activos, franja inactiva)", ref total, ref passed);

                int events = 0;
                bool lastValue = false;
                Action<bool> handler = v => { events++; lastValue = v; };
                hud.MinimizedChanged += handler;

                hud.SetMinimized(true);
                Assert(hud.IsMinimized && !hud.Background.activeSelf && !hud.StepContentPanel.activeSelf
                       && !hud.SuccessBorderGlow.gameObject.activeSelf && hud.MinimizedBar.activeSelf,
                    "SetMinimized(true) desactiva Background, StepContentPanel y SuccessBorderGlow y activa MinimizedBar", ref total, ref passed);
                Assert(hud.Canvas != null && hud.Canvas.enabled, "Minimizado: el canvas sigue habilitado (el rayo puede usar la franja)", ref total, ref passed);

                hud.SetMinimized(true);
                Assert(events == 1 && lastValue, "MinimizedChanged se dispara una sola vez al minimizar (llamar de nuevo con true no lo dispara)", ref total, ref passed);

                hud.SetMinimized(false);
                Assert(!hud.IsMinimized && hud.Background.activeSelf && hud.StepContentPanel.activeSelf
                       && !hud.SuccessBorderGlow.gameObject.activeSelf && !hud.MinimizedBar.activeSelf,
                    "SetMinimized(false) restaura el estado (el glow no se reactiva sin feedback ni advertencia)", ref total, ref passed);
                hud.SetMinimized(false);
                Assert(events == 2 && !lastValue, "MinimizedChanged se dispara una sola vez al maximizar (2 eventos en total)", ref total, ref passed);
                hud.MinimizedChanged -= handler;

                // 4. Auto-maximizar
                hud.SetMinimized(true);
                hud.DisplayStep(step, 0, 3);
                Assert(!hud.IsMinimized && hud.StepContentPanel.activeSelf && hud.Background.activeSelf && !hud.MinimizedBar.activeSelf,
                    "DisplayStep maximiza el panel minimizado", ref total, ref passed);
                Assert(hud.MinimizedStepText.text == hud.StepProgressText.text && hud.MinimizedStepText.text == "Paso 1 de 3: Encender el motor",
                    "DisplayStep deja MinimizedStepText igual que StepProgress", ref total, ref passed);

                hud.SetMinimized(true);
                hud.ShowStepWarning("Falta cometida");
                Assert(!hud.IsMinimized && hud.StepContentPanel.activeSelf && hud.SuccessBorderGlow.gameObject.activeSelf,
                    "ShowStepWarning maximiza el panel y muestra el glow de la falta", ref total, ref passed);

                hud.SetMinimized(true);
                Assert(!hud.SuccessBorderGlow.gameObject.activeSelf, "Minimizado con una advertencia activa: el glow no se ve", ref total, ref passed);
                hud.SetMinimized(false);
                Assert(hud.SuccessBorderGlow.gameObject.activeSelf, "Maximizar con una advertencia activa vuelve a mostrar el glow", ref total, ref passed);

                hud.DisplayStep(step, 1, 3);
                hud.SetMinimized(true);
                hud.SetMinimized(false);
                Assert(!hud.SuccessBorderGlow.gameObject.activeSelf, "Tras DisplayStep, minimizar y maximizar no reactiva el glow", ref total, ref passed);

                hud.SetMinimized(true);
                hud.DisplayCompletion(lesson);
                Assert(!hud.IsMinimized && hud.CompletionPanel.activeSelf && !hud.StepContentPanel.activeSelf && hud.Background.activeSelf,
                    "DisplayCompletion maximiza el panel y muestra el panel de felicitaciones", ref total, ref passed);

                // 5. Toggle ignorado con CompletionPanel o con el menú de pausa abierto
                int eventsBefore = 0;
                Action<bool> counter = v => eventsBefore++;
                hud.MinimizedChanged += counter;
                hud.SetMinimized(true);
                Assert(!hud.IsMinimized && eventsBefore == 0 && hud.StepContentPanel.activeSelf == false && hud.CompletionPanel.activeSelf,
                    "Con CompletionPanel activo, SetMinimized(true) no hace nada", ref total, ref passed);
                hud.ToggleMinimized();
                Assert(!hud.IsMinimized && eventsBefore == 0, "Con CompletionPanel activo, ToggleMinimized no hace nada", ref total, ref passed);
                hud.MinimizeButton.onClick.Invoke();
                hud.MaximizeButton.onClick.Invoke();
                Assert(!hud.IsMinimized && eventsBefore == 0, "Con CompletionPanel activo, los botones tampoco hacen nada", ref total, ref passed);
                hud.MinimizedChanged -= counter;

                // SetMinimized(false) sigue funcionando siempre, incluso con CompletionPanel activo
                hud.DisplayStep(step, 0, 3);
                hud.SetMinimized(true);
                hud.CompletionPanel.SetActive(true);
                hud.SetMinimized(false);
                Assert(!hud.IsMinimized && hud.MinimizedBar.activeSelf == false && hud.Background.activeSelf,
                    "Con CompletionPanel activo, SetMinimized(false) igual maximiza", ref total, ref passed);
                hud.CompletionPanel.SetActive(false);

                // Update no tira excepción con Key.None ni con una tecla fuera de rango (y corre la lectura del botón A)
                var keySo = new SerializedObject(hud);
                SerializedProperty keyProp = keySo.FindProperty("m_ToggleMinimizeKey");
                int keyBefore = keyProp.intValue;
                bool updateOk = true;
                foreach (int badKey in new[] { 0, 9999 })
                {
                    keyProp.intValue = badKey;
                    keySo.ApplyModifiedPropertiesWithoutUndo();
                    try { Invoke(hud, "Update"); }
                    catch (Exception ex) { updateOk = false; Debug.LogWarning($"Update con tecla {badKey} lanzó: {ex.InnerException ?? ex}"); }
                }
                keyProp.intValue = keyBefore;
                keySo.ApplyModifiedPropertiesWithoutUndo();
                Assert(updateOk, "Update no lanza excepción con Key.None ni con una tecla inválida", ref total, ref passed);

                hud.DisplayStep(step, 0, 3);
                menuRoot.SetActive(true);
                Assert(menu.IsOpen, "Precondición: el InGameMenuController de prueba está abierto", ref total, ref passed);
                hud.ToggleMinimized();
                hud.MinimizeButton.onClick.Invoke();
                Assert(!hud.IsMinimized, "Con el menú de pausa abierto, ToggleMinimized y el botón no hacen nada", ref total, ref passed);

                menuRoot.SetActive(false);
                hud.ToggleMinimized();
                Assert(hud.IsMinimized, "Con el menú cerrado, ToggleMinimized minimiza", ref total, ref passed);

                // Con el menú abierto tampoco se puede maximizar
                menuRoot.SetActive(true);
                hud.ToggleMinimized();
                hud.MaximizeButton.onClick.Invoke();
                Assert(hud.IsMinimized, "Con el menú de pausa abierto, tampoco se puede maximizar", ref total, ref passed);

                // 6. Minimizado -> abrir menú -> cerrar menú: sigue minimizado
                Invoke(hud, "HandleInGameMenuOpened");
                Assert(!hud.Canvas.enabled, "Al abrir el menú de pausa el canvas del HUD se oculta", ref total, ref passed);
                menuRoot.SetActive(false);
                // El rayo del HUD figura como activo: SetFarCasting(true) no toca los interactors de otras escenas
                SetPrivateField(hud, "m_FarCastingActive", true);
                Invoke(hud, "HandleInGameMenuClosed");
                Assert(hud.Canvas.enabled && hud.IsMinimized && !hud.StepContentPanel.activeSelf && hud.MinimizedBar.activeSelf && !hud.Background.activeSelf,
                    "Minimizado -> abrir menú -> cerrar menú: sigue minimizado (StepContentPanel inactivo, MinimizedBar activa)", ref total, ref passed);

                // 7. Botones cableados
                hud.SetMinimized(false);
                hud.MinimizeButton.onClick.Invoke();
                Assert(hud.IsMinimized, "Button_Minimize.onClick minimiza", ref total, ref passed);
                hud.MaximizeButton.onClick.Invoke();
                Assert(!hud.IsMinimized, "Button_Maximize.onClick maximiza", ref total, ref passed);

                // Idempotencia del constructor: volver a construir no duplica objetos
                Lesson01SceneSetup.BuildMinimizeControls(hud, null);
                Assert(CountByName(hud.transform, "MinimizedBar") == 1 && CountByName(hud.transform, "Button_Minimize") == 1
                       && CountByName(hud.transform, "Button_Maximize") == 1,
                    "BuildMinimizeControls es idempotente (no duplica MinimizedBar ni los botones)", ref total, ref passed);
            }
            finally
            {
                foreach (UnityEngine.Object o in created)
                {
                    if (o != null) UnityEngine.Object.DestroyImmediate(o);
                }

                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (scratch.IsValid()) EditorSceneManager.CloseScene(scratch, true);
            }
        }

        private static LessonHUDPanel BuildTestHud(System.Collections.Generic.List<UnityEngine.Object> created,
            out GameObject menuRoot, out InGameMenuController menu)
        {
            var canvasObj = new GameObject("TMP_LessonHUD_Canvas");
            created.Add(canvasObj);
            ((RectTransform)canvasObj.AddComponent<RectTransform>()).sizeDelta = new Vector2(600f, 260f);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            LessonHUDPanel hud = canvasObj.AddComponent<LessonHUDPanel>();

            GameObject bg = NewChild("Background", canvasObj.transform);
            bg.AddComponent<Image>();

            GameObject stepContent = NewChild("StepContentPanel", canvasObj.transform);
            TextMeshProUGUI progress = NewChild("StepProgress", stepContent.transform).AddComponent<TextMeshProUGUI>();
            TextMeshProUGUI instruction = NewChild("InstructionText", stepContent.transform).AddComponent<TextMeshProUGUI>();
            GameObject continueObj = NewChild("Button_Continue", stepContent.transform);
            Image continueImg = continueObj.AddComponent<Image>();
            Button continueBtn = continueObj.AddComponent<Button>();
            continueBtn.targetGraphic = continueImg;
            TextMeshProUGUI continueText = NewChild("Text", continueObj.transform).AddComponent<TextMeshProUGUI>();

            GameObject glow = NewChild("SuccessBorderGlow", canvasObj.transform);
            Image glowImg = glow.AddComponent<Image>();
            glow.SetActive(false);

            GameObject completion = NewChild("CompletionPanel", canvasObj.transform);
            TextMeshProUGUI compTitle = NewChild("CompletionTitle", completion.transform).AddComponent<TextMeshProUGUI>();
            TextMeshProUGUI compDesc = NewChild("CompletionDesc", completion.transform).AddComponent<TextMeshProUGUI>();
            completion.SetActive(false);

            // InGameMenuController de prueba: IsOpen depende de m_MenuRoot.activeSelf (sin Awake no lo desactiva)
            var menuObj = new GameObject("TMP_InGameMenuController");
            created.Add(menuObj);
            menu = menuObj.AddComponent<InGameMenuController>();
            menuRoot = new GameObject("TMP_MenuRoot");
            created.Add(menuRoot);
            menuRoot.SetActive(false);
            var menuSo = new SerializedObject(menu);
            menuSo.FindProperty("m_MenuRoot").objectReferenceValue = menuRoot;
            menuSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(hud);
            so.FindProperty("m_StepProgressText").objectReferenceValue = progress;
            so.FindProperty("m_InstructionText").objectReferenceValue = instruction;
            so.FindProperty("m_StepContentPanel").objectReferenceValue = stepContent;
            so.FindProperty("m_ContinueButton").objectReferenceValue = continueBtn;
            so.FindProperty("m_ContinueButtonText").objectReferenceValue = continueText;
            so.FindProperty("m_CompletionPanel").objectReferenceValue = completion;
            so.FindProperty("m_CompletionTitleText").objectReferenceValue = compTitle;
            so.FindProperty("m_CompletionDescriptionText").objectReferenceValue = compDesc;
            so.FindProperty("m_SuccessBorderGlow").objectReferenceValue = glowImg;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Mismo código que usan los setups de las escenas
            Lesson01SceneSetup.BuildMinimizeControls(hud, null);
            return hud;
        }

        // =========================================================================
        // 8. Menú principal
        // =========================================================================
        private static void VerifyMainMenuCard(ref int total, ref int passed)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(MAIN_MENU_SCENE);
            try
            {
                string text = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (TextMeshProUGUI tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                    {
                        if (tmp.name == "Description" && tmp.transform.parent != null && tmp.transform.parent.name == "Card_Menu")
                        {
                            text = tmp.text;
                        }
                    }
                }

                Assert(text != null, "NewMainMenu: existe Card_Menu/Description", ref total, ref passed);
                Assert(text != null && text.Contains("botón A") && text.Contains("panel del paso"),
                    "NewMainMenu: la descripción de Card_Menu menciona el botón A y el panel del paso", ref total, ref passed);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // =========================================================================
        // 9. Reglas de código
        // =========================================================================
        private static void VerifySourceRules(ref int total, ref int passed)
        {
            string hud = File.ReadAllText(HUD_SCRIPT);
            string edge = File.ReadAllText("Assets/Scripts/VR/XRButtonEdgeDetector.cs");
            Assert(hud.Contains("XRNode.RightHand") && hud.Contains("XRCommonUsages.primaryButton") && hud.Contains("XRButtonEdgeDetector")
                   && edge.Contains("GetDeviceAtXRNode"),
                "LessonHUDPanel lee el botón A (primaryButton) del control derecho con la API legada de XR", ref total, ref passed);
            Assert(hud.Contains("Key.O"), "LessonHUDPanel usa la tecla O por defecto", ref total, ref passed);

            bool obsolete = false;
            foreach (string path in new[] { HUD_SCRIPT, "Assets/Scripts/Testing/Editor/LessonHUDMinimizeSetup.cs",
                         "Assets/Scripts/Testing/Editor/LessonHUDMinimizeVerification.cs" })
            {
                if (File.ReadAllText(path).Contains("FindFirst" + "ObjectByType")) obsolete = true;
            }
            Assert(!obsolete, "Sin la API obsoleta FindFirst..ObjectByType en los archivos de la feature", ref total, ref passed);
        }

        // =========================================================================
        // Helpers
        // =========================================================================
        /// <summary>
        /// true si VehicleController.cs lee la tecla (p. ej. "H" -> <c>Keyboard.current.hKey.wasPressedThisFrame</c> o
        /// <c>isPressed</c>). Escanea el fuente: es lo que usa el bloque de teclas de depuración del Editor.
        /// </summary>
        private static bool VehicleControllerReadsKey(string keyName)
        {
            string source = File.ReadAllText(VEHICLE_SCRIPT);
            string property = char.ToLowerInvariant(keyName[0]) + keyName.Substring(1) + "Key";
            return System.Text.RegularExpressions.Regex.IsMatch(source, @"\." + property + @"\.(wasPressedThisFrame|isPressed|wasReleasedThisFrame)");
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            f.SetValue(target, value);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static int CountByName(Transform root, string name)
        {
            int n = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) n++;
            }
            return n;
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new MissingMethodException(target.GetType().Name, method);
            m.Invoke(target, null);
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"[FAIL] {testName}");
            }
        }
    }
}
