using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PrimerVolante.Lessons;
using PrimerVolante.MainMenu;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor que arma la escena del menú principal en cabina (<c>MainMenu.unity</c>): la duplica de
    /// <c>Lesson_01_BasicDriving2</c> (si no existe), quita los objetos de la lección, aplica los overrides opt-in de
    /// la instancia de Car09 (sin tocar el prefab) y construye <c>MainMenu_Canvas</c> + <c>MainMenuController</c>.
    /// Idempotente: puede ejecutarse repetidamente sin duplicar objetos.
    /// </summary>
    public static class MainMenuSetup
    {
        public const string SCENE_PATH = "Assets/Scenes/car09/MainMenu.unity";
        public const string SOURCE_SCENE_PATH = "Assets/Scenes/car09/Lesson_01_BasicDriving2.unity";
        public const string CANVAS_NAME = "MainMenu_Canvas";
        public const string CONTROLLER_NAME = "MainMenuController";
        public const string HIGHLIGHTER_NAME = "MainMenu_Highlighter";
        public const string ExitOverlayName = "ExitOverlay";

        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string CONTROLLER_MODEL_PATH = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Models/UniversalController.fbx";
        private const string CURRICULUM_FOLDER = "Assets/Scripts/Lessons/Curriculum";

        private static readonly string[] s_LessonRootsToRemove =
        {
            "Lesson_Manager", "NPC_Demo", "Scripted_Events", "Lesson_Boundaries",
            "Controlled_Intersection_T", "TestTrafficLight", "ScaleTest"
        };

        // Canvas 2,4 x 1,4 m (0,001 m por unidad) frente al auto, fuera del parabrisas.
        private const float CANVAS_W = 2400f;
        private const float CANVAS_H = 1400f;
        private const float STRIP_H = 130f;
        private const float LEGEND_W = 700f;
        private const float MARGIN = 40f;

        // Posición en mundo del canvas: sobre el eje del auto, ~3 m delante de los ojos, a la altura de los ojos.
        private static readonly Vector3 s_CanvasOffsetFromCar = new Vector3(0f, 1.30f, 3.0f);

        private static readonly Color s_BackgroundColor = new Color(0.04f, 0.05f, 0.09f, 1f);
        private static readonly Color s_PanelColor = new Color(0.10f, 0.12f, 0.18f, 1f);
        private static readonly Color s_CardColor = new Color(0.14f, 0.16f, 0.24f, 1f);

        [MenuItem("Tools/Primer Volante/Setup Main Menu Scene")]
        public static void SetupMenu()
        {
            Debug.Log($"[MainMenuSetup] Configurando el menú principal en: {SCENE_PATH}...");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) == null)
            {
                if (!AssetDatabase.CopyAsset(SOURCE_SCENE_PATH, SCENE_PATH))
                {
                    Debug.LogError($"[MainMenuSetup] No se pudo duplicar {SOURCE_SCENE_PATH} a {SCENE_PATH}.");
                    return;
                }
                AssetDatabase.Refresh();
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }

            if (BuildInActiveScene() == null) return;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[MainMenuSetup] Menú principal configurado y guardado.");
        }

        /// <summary>
        /// Construye (o reconstruye) todo el menú en la escena activa. Solo actúa si la escena activa es
        /// <see cref="SCENE_PATH"/>: borra objetos de lección, así que en cualquier otra escena no hace nada y loguea un error.
        /// </summary>
        public static MainMenuController BuildInActiveScene()
        {
            string activePath = EditorSceneManager.GetActiveScene().path;
            if (activePath != SCENE_PATH)
            {
                Debug.LogError($"[MainMenuSetup] La escena activa es '{activePath}', no {SCENE_PATH}: no se modifica nada.");
                return null;
            }

            VehicleController car = Object.FindAnyObjectByType<VehicleController>(FindObjectsInactive.Include);
            if (car == null)
            {
                Debug.LogError("[MainMenuSetup] No hay ningún VehicleController (Car09) en la escena: no se modifica nada.");
                return null;
            }

            RemoveLessonObjects();

            VRGearShifter shifter = car.GetComponentInChildren<VRGearShifter>(true);
            VRSteeringWheel wheel = car.GetComponentInChildren<VRSteeringWheel>(true);
            StartButton startButton = car.GetComponentInChildren<StartButton>(true);
            VRHandbrake handbrake = car.GetComponentInChildren<VRHandbrake>(true);
            InGameMenuController inGameMenu = Object.FindAnyObjectByType<InGameMenuController>(FindObjectsInactive.Include);

            // Overrides de la instancia (el prefab Car09 no se toca).
            SetBool(shifter, "m_IgnoreDrivingRestrictions", true);
            SetBool(startButton, "m_TogglesEngine", false);
            SetBool(inGameMenu, "m_MenuButtonEnabled", false);

            Transform backButton = FindDeep(inGameMenu != null ? inGameMenu.MenuRoot != null ? inGameMenu.MenuRoot.transform : null : null, "Button_Back");
            if (backButton == null)
            {
                GameObject root = GameObject.Find("InGameMenu_Canvas");
                backButton = FindDeep(root != null ? root.transform : null, "Button_Back");
            }
            SetObject(inGameMenu, "m_CalibrationBackButton", backButton != null ? backButton.gameObject : null);
            // La calibración standalone se ubica al noreste del frente del auto, no de la cabeza (instancia de escena, no el prefab).
            SetObject(inGameMenu, "m_StandaloneForwardReference", car.transform);

            InteractableHighlighter highlighter = EnsureHighlighter();

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            GameObject controllerModel = AssetDatabase.LoadAssetAtPath<GameObject>(CONTROLLER_MODEL_PATH);
            List<LessonDefinitionSO> lessons = LoadLessons();

            GameObject canvasObj = BuildCanvas(car, font, controllerModel, lessons,
                out GameObject[] pageRoots, out TextMeshProUGUI[] gearTabs, out GameObject gearStrip,
                out GameObject legendRoot, out TextMeshProUGUI legendText, out LessonListPanel lessonList);

            MainMenuController controller = EnsureController();
            var so = new SerializedObject(controller);
            so.Update();
            so.FindProperty("m_Vehicle").objectReferenceValue = car;
            so.FindProperty("m_GearShifter").objectReferenceValue = shifter;
            so.FindProperty("m_SteeringWheel").objectReferenceValue = wheel;
            so.FindProperty("m_StartButton").objectReferenceValue = startButton;
            so.FindProperty("m_Handbrake").objectReferenceValue = handbrake;
            so.FindProperty("m_InGameMenu").objectReferenceValue = inGameMenu;
            so.FindProperty("m_Highlighter").objectReferenceValue = highlighter;
            so.FindProperty("m_LessonList").objectReferenceValue = lessonList;
            so.FindProperty("m_GearStripRoot").objectReferenceValue = gearStrip;
            so.FindProperty("m_LegendRoot").objectReferenceValue = legendRoot;
            so.FindProperty("m_LegendText").objectReferenceValue = legendText;
            so.FindProperty("m_ExitOverlay").objectReferenceValue = canvasObj.transform.Find(ExitOverlayName).gameObject;
            SetObjectArray(so.FindProperty("m_PageRoots"), pageRoots);
            SetObjectArray(so.FindProperty("m_GearTabs"), gearTabs);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);

            return controller;
        }

        // ------------------------------------------------------------------ escena

        private static void RemoveLessonObjects()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(s_LessonRootsToRemove, root.name) >= 0)
                {
                    Object.DestroyImmediate(root);
                }
            }

            // El HUD de lección (si el Car09 trae uno) no aplica al menú.
            foreach (LessonHUDPanel hud in Object.FindObjectsByType<LessonHUDPanel>(FindObjectsInactive.Include))
            {
                hud.gameObject.SetActive(false);
            }
        }

        private static InteractableHighlighter EnsureHighlighter()
        {
            InteractableHighlighter existing = Object.FindAnyObjectByType<InteractableHighlighter>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var go = new GameObject(HIGHLIGHTER_NAME);
            return go.AddComponent<InteractableHighlighter>();
        }

        private static MainMenuController EnsureController()
        {
            MainMenuController existing = Object.FindAnyObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var go = new GameObject(CONTROLLER_NAME);
            return go.AddComponent<MainMenuController>();
        }

        private static List<LessonDefinitionSO> LoadLessons()
        {
            var list = new List<LessonDefinitionSO>();
            for (int i = 1; i <= 4; i++)
            {
                var lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>($"{CURRICULUM_FOLDER}/LessonData_{i:00}.asset");
                if (lesson != null) list.Add(lesson);
            }
            return list;
        }

        // ------------------------------------------------------------------ canvas

        private static GameObject BuildCanvas(
            VehicleController car, TMP_FontAsset font, GameObject controllerModel, List<LessonDefinitionSO> lessons,
            out GameObject[] pageRoots, out TextMeshProUGUI[] gearTabs, out GameObject gearStrip,
            out GameObject legendRoot, out TextMeshProUGUI legendText, out LessonListPanel lessonList)
        {
            GameObject old = GameObject.Find(CANVAS_NAME);
            if (old != null) Object.DestroyImmediate(old);

            var canvasObj = new GameObject(CANVAS_NAME, typeof(RectTransform));
            RectTransform canvasRect = (RectTransform)canvasObj.transform;
            canvasRect.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);
            canvasObj.transform.localScale = Vector3.one * 0.001f;
            canvasObj.transform.position = car.transform.position + car.transform.TransformDirection(s_CanvasOffsetFromCar);
            // Mirando hacia el conductor: la cara legible del canvas mira hacia -Z del auto.
            canvasObj.transform.rotation = Quaternion.LookRotation(car.transform.forward, Vector3.up);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;

            // Fondo
            Image bg = AddImage(canvasObj.transform, "Background", s_BackgroundColor);
            Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            // Tira de marchas
            gearStrip = CreateRect("GearStrip", canvasObj.transform).gameObject;
            var stripRect = (RectTransform)gearStrip.transform;
            stripRect.anchorMin = new Vector2(0f, 1f);
            stripRect.anchorMax = new Vector2(1f, 1f);
            stripRect.pivot = new Vector2(0.5f, 1f);
            stripRect.sizeDelta = new Vector2(-2f * MARGIN, STRIP_H);
            stripRect.anchoredPosition = new Vector2(0f, -MARGIN);
            Image stripBg = gearStrip.AddComponent<Image>();
            stripBg.color = s_PanelColor;
            var stripLayout = gearStrip.AddComponent<HorizontalLayoutGroup>();
            stripLayout.childAlignment = TextAnchor.MiddleCenter;
            stripLayout.childControlWidth = true;
            stripLayout.childControlHeight = true;
            stripLayout.childForceExpandWidth = true;
            stripLayout.childForceExpandHeight = true;
            stripLayout.padding = new RectOffset(20, 20, 10, 10);

            string[] tabNames = { "P  Lecciones", "R  Controles", "N  Calibración", "D  Créditos" };
            gearTabs = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                gearTabs[i] = AddText(gearStrip.transform, $"Tab_{"PRND"[i]}", font, tabNames[i], 60f, Color.white, TextAlignmentOptions.Center);
            }

            // Leyenda lateral
            legendRoot = CreateRect("Legend", canvasObj.transform).gameObject;
            var legendRect = (RectTransform)legendRoot.transform;
            legendRect.anchorMin = new Vector2(0f, 0f);
            legendRect.anchorMax = new Vector2(0f, 1f);
            legendRect.pivot = new Vector2(0f, 0.5f);
            legendRect.sizeDelta = new Vector2(LEGEND_W, -(STRIP_H + 3f * MARGIN));
            legendRect.anchoredPosition = new Vector2(MARGIN, -(STRIP_H + MARGIN) * 0.5f);
            Image legendBg = legendRoot.AddComponent<Image>();
            legendBg.color = s_PanelColor;
            TextMeshProUGUI legendTitle = AddText(legendRoot.transform, "LegendTitle", font, "Cómo se usa", 54f, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.TopLeft);
            Stretch(legendTitle.rectTransform, 30f, 30f, 30f, 30f);
            legendText = AddText(legendRoot.transform, "LegendText", font, string.Empty, 50f, Color.white, TextAlignmentOptions.TopLeft);
            legendText.textWrappingMode = TextWrappingModes.Normal;
            legendText.rectTransform.anchorMin = Vector2.zero;
            legendText.rectTransform.anchorMax = Vector2.one;
            legendText.rectTransform.offsetMin = new Vector2(30f, 30f);
            legendText.rectTransform.offsetMax = new Vector2(-30f, -120f);

            // Área central
            RectTransform content = CreateRect("Content", canvasObj.transform);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(2f * MARGIN + LEGEND_W, MARGIN);
            content.offsetMax = new Vector2(-MARGIN, -(STRIP_H + 2f * MARGIN));

            pageRoots = new GameObject[5];
            pageRoots[(int)MainMenuPage.Splash] = BuildSplashPage(content, font, controllerModel);
            pageRoots[(int)MainMenuPage.Lessons] = BuildLessonsPage(content, font, lessons, out lessonList);
            pageRoots[(int)MainMenuPage.Controls] = BuildControlsPage(content, font, controllerModel);
            pageRoots[(int)MainMenuPage.Calibration] = BuildSimplePage(content, "Page_Calibration", font,
                "Calibración", "Ajustá la posición de la vista con el panel de la derecha", 76f);
            pageRoots[(int)MainMenuPage.Credits] = BuildSimplePage(content, "Page_Credits", font, "Créditos", string.Empty, 76f);

            // Estado inicial en edición: se ve el splash.
            for (int i = 0; i < pageRoots.Length; i++) pageRoots[i].SetActive(i == (int)MainMenuPage.Splash);
            gearStrip.SetActive(false);

            BuildExitOverlay(canvasObj.transform, font);

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                foreach (Transform t in canvasObj.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = uiLayer;
                }
            }

            return canvasObj;
        }

        // Overlay de confirmación de salida (freno de mano tirado del todo). Último hijo: se dibuja sobre todo lo demás.
        private static void BuildExitOverlay(Transform canvas, TMP_FontAsset font)
        {
            Image overlay = AddImage(canvas, ExitOverlayName, new Color(0.02f, 0.02f, 0.04f, 1f));
            Stretch(overlay.rectTransform, 0f, 0f, 0f, 0f);

            RectTransform box = CreateRect("Box", overlay.transform);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(2000f, 800f);
            Image boxBg = box.gameObject.AddComponent<Image>();
            boxBg.color = new Color(0.30f, 0.07f, 0.07f, 1f);

            TextMeshProUGUI title = AddText(box, "Title", font, "¿Salir del simulador?", 120f, Color.white, TextAlignmentOptions.Center);
            AnchorTop(title.rectTransform, 70f, 170f);

            TextMeshProUGUI body = AddText(box, "Body", font,
                "Pulsá el botón de encendido para confirmar · Bajá el freno de mano para cancelar", 76f,
                new Color(1f, 0.9f, 0.7f), TextAlignmentOptions.Center);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.rectTransform.anchorMin = new Vector2(0.05f, 0f);
            body.rectTransform.anchorMax = new Vector2(0.95f, 0f);
            body.rectTransform.pivot = new Vector2(0.5f, 0f);
            body.rectTransform.sizeDelta = new Vector2(0f, 360f);
            body.rectTransform.anchoredPosition = new Vector2(0f, 90f);

            overlay.gameObject.SetActive(false);
        }

        private static GameObject BuildSplashPage(RectTransform content, TMP_FontAsset font, GameObject controllerModel)
        {
            RectTransform page = CreatePage(content, "Page_Splash");

            TextMeshProUGUI title = AddText(page, "Title", font, "Mi Primer Volante", 150f, Color.white, TextAlignmentOptions.Center);
            AnchorTop(title.rectTransform, 0f, 230f);

            RectTransform cards = CreateRect("Cards", page);
            cards.anchorMin = new Vector2(0f, 0f);
            cards.anchorMax = new Vector2(1f, 1f);
            cards.offsetMin = new Vector2(0f, 150f);
            cards.offsetMax = new Vector2(0f, -250f);
            var layout = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 40f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            BuildCard(cards, font, controllerModel, "Card_Grab", "Agarrar", "Bumper", false, string.Empty, 3600f, new Vector3(20f, -40f, 0f));
            BuildCard(cards, font, controllerModel, "Card_Press", "Pulsar", string.Empty, true, string.Empty, 2600f, new Vector3(15f, -50f, 0f));

            TextMeshProUGUI footer = AddText(page, "Footer", font, "Pulsá el botón de balizas para comenzar", 60f, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Center);
            var footerRect = footer.rectTransform;
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, 120f);
            footerRect.anchoredPosition = Vector2.zero;

            return page.gameObject;
        }

        private static GameObject BuildLessonsPage(RectTransform content, TMP_FontAsset font, List<LessonDefinitionSO> lessons, out LessonListPanel panel)
        {
            RectTransform page = CreatePage(content, "Page_Lessons");

            TextMeshProUGUI title = AddText(page, "Title", font, "Lecciones", 90f, Color.white, TextAlignmentOptions.Center);
            AnchorTop(title.rectTransform, 0f, 130f);

            RectTransform list = CreateRect("LessonList", page);
            list.anchorMin = Vector2.zero;
            list.anchorMax = Vector2.one;
            list.offsetMin = new Vector2(0f, 0f);
            list.offsetMax = new Vector2(0f, -150f);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            panel = list.gameObject.AddComponent<LessonListPanel>();
            var so = new SerializedObject(panel);
            so.Update();
            so.FindProperty("m_Container").objectReferenceValue = list;
            so.FindProperty("m_Font").objectReferenceValue = font;
            so.FindProperty("m_ItemHeight").floatValue = 170f;
            SerializedProperty lessonsProp = so.FindProperty("m_Lessons");
            lessonsProp.arraySize = lessons.Count;
            for (int i = 0; i < lessons.Count; i++)
            {
                lessonsProp.GetArrayElementAtIndex(i).objectReferenceValue = lessons[i];
            }
            so.ApplyModifiedProperties();

            return page.gameObject;
        }

        private static GameObject BuildControlsPage(RectTransform content, TMP_FontAsset font, GameObject controllerModel)
        {
            RectTransform page = CreatePage(content, "Page_Controls");

            TextMeshProUGUI title = AddText(page, "Title", font, "Controles", 90f, Color.white, TextAlignmentOptions.Center);
            AnchorTop(title.rectTransform, 0f, 130f);

            RectTransform cards = CreateRect("Cards", page);
            cards.anchorMin = Vector2.zero;
            cards.anchorMax = Vector2.one;
            cards.offsetMin = Vector2.zero;
            cards.offsetMax = new Vector2(0f, -150f);
            var layout = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            BuildCard(cards, font, controllerModel, "Card_Grip", "Grip", "Bumper", false,
                "Agarrar: volante, palanca, freno de mano, guiños y espejos", 3000f, new Vector3(20f, -40f, 0f));
            BuildCard(cards, font, controllerModel, "Card_Trigger", "Gatillo", "Trigger", false,
                "Usar el rayo en los menús (botones y sliders)", 3000f, new Vector3(20f, 40f, 0f));
            BuildCard(cards, font, controllerModel, "Card_Menu", "Menú", "Button_Home", false,
                "Abrir el menú de pausa y calibración durante las lecciones", 3000f, new Vector3(-65f, 20f, 0f));

            return page.gameObject;
        }

        private static GameObject BuildSimplePage(RectTransform content, string name, TMP_FontAsset font, string title, string body, float bodySize)
        {
            RectTransform page = CreatePage(content, name);

            TextMeshProUGUI titleText = AddText(page, "Title", font, title, 120f, Color.white, TextAlignmentOptions.Center);
            titleText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            titleText.rectTransform.sizeDelta = new Vector2(0f, 160f);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, 120f);

            if (!string.IsNullOrEmpty(body))
            {
                TextMeshProUGUI bodyText = AddText(page, "Body", font, body, bodySize, new Color(0.85f, 0.9f, 1f), TextAlignmentOptions.Center);
                bodyText.textWrappingMode = TextWrappingModes.Normal;
                bodyText.rectTransform.anchorMin = new Vector2(0.05f, 0.5f);
                bodyText.rectTransform.anchorMax = new Vector2(0.95f, 0.5f);
                bodyText.rectTransform.sizeDelta = new Vector2(0f, 300f);
                bodyText.rectTransform.anchoredPosition = new Vector2(0f, -100f);
            }

            return page.gameObject;
        }

        // ------------------------------------------------------------------ tarjetas

        private static void BuildCard(RectTransform parent, TMP_FontAsset font, GameObject controllerModel, string name,
            string caption, string pieceName, bool pressAnimation, string description, float modelScale, Vector3 modelEuler)
        {
            RectTransform card = CreateRect(name, parent);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = s_CardColor;

            TextMeshProUGUI captionText = AddText(card, "Caption", font, caption, 72f, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Center);
            AnchorTop(captionText.rectTransform, 20f, 110f);

            if (!string.IsNullOrEmpty(description))
            {
                TextMeshProUGUI desc = AddText(card, "Description", font, description, 44f, Color.white, TextAlignmentOptions.Top);
                desc.textWrappingMode = TextWrappingModes.Normal;
                var descRect = desc.rectTransform;
                descRect.anchorMin = new Vector2(0f, 0f);
                descRect.anchorMax = new Vector2(1f, 0f);
                descRect.pivot = new Vector2(0.5f, 0f);
                descRect.sizeDelta = new Vector2(-40f, 260f);
                descRect.anchoredPosition = new Vector2(0f, 20f);
            }

            // Soporte de la ilustración 3D: hacia el espectador (z negativo) para que el modelo no quede detrás del fondo.
            var holder = CreateRect("Illustration", card);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, string.IsNullOrEmpty(description) ? 0.45f : 0.55f);
            holder.sizeDelta = new Vector2(100f, 100f);
            holder.anchoredPosition3D = new Vector3(0f, 0f, -150f);

            var illustration = holder.gameObject.AddComponent<ControllerIllustration>();
            var so = new SerializedObject(illustration);
            so.Update();
            so.FindProperty("m_ControllerModel").objectReferenceValue = controllerModel;
            so.FindProperty("m_PieceName").stringValue = pieceName;
            so.FindProperty("m_PressAnimation").boolValue = pressAnimation;
            so.FindProperty("m_ModelScale").floatValue = modelScale;
            so.FindProperty("m_ModelEuler").vector3Value = modelEuler;
            so.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ helpers de UI

        private static RectTransform CreatePage(RectTransform content, string name)
        {
            RectTransform page = CreateRect(name, content);
            Stretch(page, 0f, 0f, 0f, 0f);
            return page;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image AddImage(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorTop(RectTransform rect, float topOffset, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, -topOffset);
        }

        // ------------------------------------------------------------------ SerializedObject

        private static void SetBool(Object target, string property, bool value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            so.Update();
            SerializedProperty prop = so.FindProperty(property);
            if (prop == null) { Debug.LogError($"[MainMenuSetup] No existe la propiedad {property} en {target.GetType().Name}."); return; }
            prop.boolValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetObject(Object target, string property, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            so.Update();
            SerializedProperty prop = so.FindProperty(property);
            if (prop == null) { Debug.LogError($"[MainMenuSetup] No existe la propiedad {property} en {target.GetType().Name}."); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetObjectArray(SerializedProperty prop, Object[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
