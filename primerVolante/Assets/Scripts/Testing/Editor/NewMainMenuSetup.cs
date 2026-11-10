using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.Lessons;
using PrimerVolante.MainMenu;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para la construcción automatizada e idempotente de la escena NewMainMenu:
    /// - Duplica la base de MainMenu a NewMainMenu.unity (si no existe).
    /// - Remueve controladores antiguos (MainMenuController, MainMenu_Canvas).
    /// - Construye NewMainMenu_Canvas (World Space) con TrackedDeviceGraphicRaycaster, diseño ortogonal por solapas,
    ///   lista de lecciones data-driven, circuitos integradores, controles, calibración integrada y modal de confirmación de salida.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class NewMainMenuSetup
    {
        public const string SCENE_PATH = "Assets/Scenes/car09/NewMainMenu.unity";
        public const string SOURCE_SCENE_PATH = "Assets/Scenes/car09/MainMenu.unity";
        public const string FALLBACK_SOURCE_SCENE_PATH = "Assets/Scenes/car09/Lesson_01_BasicDriving2.unity";
        public const string CANVAS_NAME = "NewMainMenu_Canvas";
        public const string CONTROLLER_NAME = "NewMainMenuController";

        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string CURRICULUM_FOLDER = "Assets/Scripts/Lessons/Curriculum";
        private const int LESSON_COUNT = 6;

        // Dimensiones del Canvas en World Space
        private const float CANVAS_W = 2300f;
        private const float CANVAS_H = 1350f;
        private static readonly Vector3 s_CanvasOffsetFromCar = new Vector3(0f, 1.25f, 2.3f);

        // Paleta de Colores
        private static readonly Color s_BgCanvasColor = new Color(0.05f, 0.07f, 0.11f, 0.98f);     // #0d121c
        private static readonly Color s_SidebarBgColor = new Color(0.08f, 0.10f, 0.16f, 1f);       // #141a29
        private static readonly Color s_ContentAreaBgColor = new Color(0.10f, 0.12f, 0.19f, 1f);   // #1a1f30
        private static readonly Color s_CardBgColor = new Color(0.13f, 0.16f, 0.24f, 1f);          // #21293d
        private static readonly Color s_TabActiveColor = new Color(0.15f, 0.39f, 0.92f, 1f);       // #2563eb
        private static readonly Color s_TabInactiveColor = new Color(0.11f, 0.14f, 0.22f, 1f);     // #1c2438
        private static readonly Color s_GreenActionColor = new Color(0.06f, 0.73f, 0.51f, 1f);     // #10b981
        private static readonly Color s_ExitRedColor = new Color(0.86f, 0.15f, 0.15f, 1f);         // #dc2626

        [MenuItem("Tools/Primer Volante/Setup New Main Menu Scene")]
        public static void SetupNewMainMenu()
        {
            Debug.Log($"[NewMainMenuSetup] Configurando la nueva escena de menú en: {SCENE_PATH}...");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) == null)
            {
                string source = AssetDatabase.LoadAssetAtPath<SceneAsset>(SOURCE_SCENE_PATH) != null
                    ? SOURCE_SCENE_PATH
                    : FALLBACK_SOURCE_SCENE_PATH;

                if (!AssetDatabase.CopyAsset(source, SCENE_PATH))
                {
                    Debug.LogError($"[NewMainMenuSetup] No se pudo duplicar {source} a {SCENE_PATH}.");
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

            Debug.Log("[NewMainMenuSetup] Nueva escena de menú NewMainMenu configurada y guardada con éxito.");
        }

        public static NewMainMenuController BuildInActiveScene()
        {
            string activePath = EditorSceneManager.GetActiveScene().path;
            if (activePath != SCENE_PATH)
            {
                Debug.LogError($"[NewMainMenuSetup] La escena activa es '{activePath}', no {SCENE_PATH}. Operación cancelada.");
                return null;
            }

            VehicleController car = UnityEngine.Object.FindAnyObjectByType<VehicleController>(FindObjectsInactive.Include);
            if (car == null)
            {
                Debug.LogError("[NewMainMenuSetup] No se encontró ningún VehicleController (Car09) en la escena.");
                return null;
            }

            // Eliminar objetos de menús legados si existen en la escena duplicada
            RemoveLegacyMenuObjects();

            // Garantizar EventSystem con XRUIInputModule
            EnsureEventSystem();

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            List<LessonDefinitionSO> lessons = LoadLessons();

            // Construir Canvas y Jerarquía Visual
            GameObject canvasObj = BuildCanvas(car, font, lessons, out NewMainMenuController controller);

            // Capa UI en toda la jerarquía para compatibilidad estricta con UIOnlyFarCasting
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                foreach (Transform t in canvasObj.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = uiLayer;
                }
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void RemoveLegacyMenuObjects()
        {
            GameObject oldCanvas = GameObject.Find("MainMenu_Canvas");
            if (oldCanvas != null) UnityEngine.Object.DestroyImmediate(oldCanvas);

            GameObject oldController = GameObject.Find("MainMenuController");
            if (oldController != null) UnityEngine.Object.DestroyImmediate(oldController);

            GameObject oldHighlighter = GameObject.Find("MainMenu_Highlighter");
            if (oldHighlighter != null) UnityEngine.Object.DestroyImmediate(oldHighlighter);
        }

        public static void EnsureEventSystem()
        {
            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            GameObject eventSystemObj;
            if (eventSystem == null)
            {
                eventSystemObj = new GameObject("EventSystem");
                eventSystem = eventSystemObj.AddComponent<EventSystem>();
            }
            else
            {
                eventSystemObj = eventSystem.gameObject;
            }

            if (eventSystemObj.GetComponent<XRUIInputModule>() == null)
            {
                var inputModule = eventSystemObj.GetComponent<StandaloneInputModule>();
                if (inputModule != null) UnityEngine.Object.DestroyImmediate(inputModule);

                eventSystemObj.AddComponent<XRUIInputModule>();
            }
        }

        private static List<LessonDefinitionSO> LoadLessons()
        {
            var list = new List<LessonDefinitionSO>();
            for (int i = 1; i <= LESSON_COUNT; i++)
            {
                var lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>($"{CURRICULUM_FOLDER}/LessonData_{i:00}.asset");
                if (lesson != null) list.Add(lesson);
            }
            return list;
        }

        private static GameObject BuildCanvas(VehicleController car, TMP_FontAsset font, List<LessonDefinitionSO> lessons, out NewMainMenuController controller)
        {
            GameObject existing = GameObject.Find(CANVAS_NAME);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var canvasObj = new GameObject(CANVAS_NAME, typeof(RectTransform));
            RectTransform canvasRect = (RectTransform)canvasObj.transform;
            canvasRect.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);
            canvasObj.transform.localScale = Vector3.one * 0.001f;
            canvasObj.transform.position = car.transform.position + car.transform.TransformDirection(s_CanvasOffsetFromCar);
            canvasObj.transform.rotation = Quaternion.LookRotation(car.transform.forward, Vector3.up);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;

            // Soporte de Raycasting VR y mouse/desktop
            canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Fondo general
            Image bg = AddImage(canvasObj.transform, "Background", s_BgCanvasColor);
            Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            // ================================================================
            // HEADER SUPERIOR
            // ================================================================
            RectTransform header = CreateRect("Header", canvasObj.transform);
            AnchorTop(header, 0f, 110f);
            Image headerBg = header.gameObject.AddComponent<Image>();
            headerBg.color = new Color(0.07f, 0.09f, 0.14f, 1f);

            TextMeshProUGUI headerTitle = AddText(header, "Title", font, "PRIMER VOLANTE", 52f, Color.white, TextAlignmentOptions.MidlineLeft);
            headerTitle.fontStyle = FontStyles.Bold;
            headerTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
            headerTitle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            headerTitle.rectTransform.offsetMin = new Vector2(40f, 0f);
            headerTitle.rectTransform.offsetMax = new Vector2(-10f, 0f);

            TextMeshProUGUI headerSub = AddText(header, "Subtitle", font, "Simulador de Aprendizaje y Conducción Segura en RV", 32f, new Color(0.6f, 0.68f, 0.82f, 1f), TextAlignmentOptions.MidlineRight);
            headerSub.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            headerSub.rectTransform.anchorMax = new Vector2(1f, 1f);
            headerSub.rectTransform.offsetMin = new Vector2(10f, 0f);
            headerSub.rectTransform.offsetMax = new Vector2(-40f, 0f);

            // ================================================================
            // SIDEBAR LATERAL IZQUIERDA
            // ================================================================
            RectTransform sidebar = CreateRect("Sidebar", canvasObj.transform);
            sidebar.anchorMin = new Vector2(0f, 0f);
            sidebar.anchorMax = new Vector2(0f, 1f);
            sidebar.pivot = new Vector2(0f, 0.5f);
            sidebar.sizeDelta = new Vector2(520f, -110f);
            sidebar.anchoredPosition = new Vector2(0f, -55f);
            Image sidebarBg = sidebar.gameObject.AddComponent<Image>();
            sidebarBg.color = s_SidebarBgColor;

            // Grupo de botones de navegación
            RectTransform navGroup = CreateRect("NavButtons", sidebar);
            navGroup.anchorMin = new Vector2(0f, 0f);
            navGroup.anchorMax = new Vector2(1f, 1f);
            navGroup.offsetMin = new Vector2(25f, 130f);
            navGroup.offsetMax = new Vector2(-25f, -30f);
            var navLayout = navGroup.gameObject.AddComponent<VerticalLayoutGroup>();
            navLayout.spacing = 18f;
            navLayout.childControlWidth = true;
            navLayout.childControlHeight = false;
            navLayout.childForceExpandWidth = true;
            navLayout.childForceExpandHeight = false;

            Button btnTabLessons = CreateMenuButton("Tab_Lecciones", navGroup, font, "Lecciones Formativas", s_TabActiveColor, 95f);
            Button btnTabCircuits = CreateMenuButton("Tab_Circuitos", navGroup, font, "Circuitos / Integradores", s_TabInactiveColor, 95f);
            Button btnTabControls = CreateMenuButton("Tab_Controles", navGroup, font, "Controles y Mandos", s_TabInactiveColor, 95f);
            Button btnTabCalib = CreateMenuButton("Tab_Calibracion", navGroup, font, "Calibración de Asiento", s_TabInactiveColor, 95f);
            Button btnTabCredits = CreateMenuButton("Tab_Creditos", navGroup, font, "Créditos del Proyecto", s_TabInactiveColor, 95f);

            // Botón Salir (parte inferior de sidebar)
            RectTransform exitBox = CreateRect("ExitBox", sidebar);
            exitBox.anchorMin = new Vector2(0f, 0f);
            exitBox.anchorMax = new Vector2(1f, 0f);
            exitBox.sizeDelta = new Vector2(-50f, 90f);
            exitBox.anchoredPosition = new Vector2(0f, 65f);
            Button btnExit = CreateMenuButton("Btn_Exit", exitBox, font, "Salir del Simulador", new Color(0.24f, 0.10f, 0.12f, 1f), 85f);
            var exitText = btnExit.GetComponentInChildren<TextMeshProUGUI>();
            if (exitText != null) exitText.color = new Color(0.96f, 0.44f, 0.44f, 1f);

            // ================================================================
            // ÁREA DE CONTENIDO DERECHA
            // ================================================================
            RectTransform contentArea = CreateRect("ContentArea", canvasObj.transform);
            contentArea.anchorMin = new Vector2(0f, 0f);
            contentArea.anchorMax = new Vector2(1f, 1f);
            contentArea.offsetMin = new Vector2(550f, 25f);
            contentArea.offsetMax = new Vector2(-30f, -135f);
            Image contentBg = contentArea.gameObject.AddComponent<Image>();
            contentBg.color = s_ContentAreaBgColor;

            // Paneles
            GameObject panelLessons = BuildLessonsPanel(contentArea, font, lessons,
                out Transform lessonItemsContainer, out TextMeshProUGUI lTitle, out TextMeshProUGUI lDesc, out TextMeshProUGUI lSteps,
                out Button btnStartLesson, out TextMeshProUGUI btnStartLessonText);

            GameObject panelCircuits = BuildCircuitsPanel(contentArea, font,
                out Transform circuitItemsContainer, out TextMeshProUGUI cTitle, out TextMeshProUGUI cDesc, out TextMeshProUGUI cInfo,
                out Button btnStartCircuit, out TextMeshProUGUI btnStartCircuitText);

            GameObject panelControls = BuildControlsPanel(contentArea, font);
            GameObject panelCalib = BuildCalibrationPanel(contentArea, font,
                out Slider sLateral, out Slider sHeight, out Slider sDepth, out Button btnResetCalib);
            GameObject panelCredits = BuildCreditsPanel(contentArea, font);

            // Modal de Salida
            GameObject exitModal = BuildExitModal(canvasObj.transform, font, out Button btnConfirmExit, out Button btnCancelExit);

            // ================================================================
            // CONTROLADOR
            // ================================================================
            controller = canvasObj.AddComponent<NewMainMenuController>();
            List<CircuitDefinition> defaultCircuits = NewMainMenuController.GetDefaultCircuits();
            controller.SetLessons(lessons);
            controller.SetCircuits(defaultCircuits);

            var so = new SerializedObject(controller);
            so.Update();

            // Tabs
            so.FindProperty("m_TabBtnControls").objectReferenceValue = btnTabControls;
            so.FindProperty("m_TabBtnLessons").objectReferenceValue = btnTabLessons;
            so.FindProperty("m_TabBtnCircuits").objectReferenceValue = btnTabCircuits;
            so.FindProperty("m_TabBtnCalibration").objectReferenceValue = btnTabCalib;
            so.FindProperty("m_TabBtnCredits").objectReferenceValue = btnTabCredits;
            so.FindProperty("m_BtnExit").objectReferenceValue = btnExit;

            // Paneles
            so.FindProperty("m_PanelControls").objectReferenceValue = panelControls;
            so.FindProperty("m_PanelLessons").objectReferenceValue = panelLessons;
            so.FindProperty("m_PanelCircuits").objectReferenceValue = panelCircuits;
            so.FindProperty("m_PanelCalibration").objectReferenceValue = panelCalib;
            so.FindProperty("m_PanelCredits").objectReferenceValue = panelCredits;

            // Lecciones
            so.FindProperty("m_LessonItemsContainer").objectReferenceValue = lessonItemsContainer;
            so.FindProperty("m_LessonDetailTitle").objectReferenceValue = lTitle;
            so.FindProperty("m_LessonDetailDescription").objectReferenceValue = lDesc;
            so.FindProperty("m_LessonDetailSteps").objectReferenceValue = lSteps;
            so.FindProperty("m_BtnStartLesson").objectReferenceValue = btnStartLesson;
            so.FindProperty("m_BtnStartLessonText").objectReferenceValue = btnStartLessonText;

            SerializedProperty lessonsProp = so.FindProperty("m_Lessons");
            lessonsProp.arraySize = lessons.Count;
            for (int i = 0; i < lessons.Count; i++)
            {
                lessonsProp.GetArrayElementAtIndex(i).objectReferenceValue = lessons[i];
            }

            // Circuitos
            controller.InitializeDefaultCircuitsIfEmpty();
            so.Update();
            so.FindProperty("m_CircuitItemsContainer").objectReferenceValue = circuitItemsContainer;
            so.FindProperty("m_CircuitDetailTitle").objectReferenceValue = cTitle;
            so.FindProperty("m_CircuitDetailDescription").objectReferenceValue = cDesc;
            so.FindProperty("m_CircuitDetailInfo").objectReferenceValue = cInfo;
            so.FindProperty("m_BtnStartCircuit").objectReferenceValue = btnStartCircuit;
            so.FindProperty("m_BtnStartCircuitText").objectReferenceValue = btnStartCircuitText;

            SerializedProperty circuitsProp = so.FindProperty("m_Circuits");
            circuitsProp.arraySize = defaultCircuits.Count;
            for (int i = 0; i < defaultCircuits.Count; i++)
            {
                SerializedProperty elem = circuitsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("circuitId").stringValue = defaultCircuits[i].circuitId;
                elem.FindPropertyRelative("title").stringValue = defaultCircuits[i].title;
                elem.FindPropertyRelative("description").stringValue = defaultCircuits[i].description;
                elem.FindPropertyRelative("sceneName").stringValue = defaultCircuits[i].sceneName;
                elem.FindPropertyRelative("difficulty").stringValue = defaultCircuits[i].difficulty;
                elem.FindPropertyRelative("estimatedTime").stringValue = defaultCircuits[i].estimatedTime;
            }

            // Calibración
            so.FindProperty("m_CalibLateralSlider").objectReferenceValue = sLateral;
            so.FindProperty("m_CalibHeightSlider").objectReferenceValue = sHeight;
            so.FindProperty("m_CalibDepthSlider").objectReferenceValue = sDepth;
            so.FindProperty("m_BtnResetCalibration").objectReferenceValue = btnResetCalib;
            DriverSeatFollower follower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>(FindObjectsInactive.Include);
            so.FindProperty("m_DriverSeatFollower").objectReferenceValue = follower;

            // Modal
            so.FindProperty("m_ExitModal").objectReferenceValue = exitModal;
            so.FindProperty("m_BtnConfirmExit").objectReferenceValue = btnConfirmExit;
            so.FindProperty("m_BtnCancelExit").objectReferenceValue = btnCancelExit;

            // Interactors
            NearFarInteractor[] interactors = UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);
            SerializedProperty interactorsProp = so.FindProperty("m_FarCastInteractors");
            interactorsProp.arraySize = interactors.Length;
            for (int i = 0; i < interactors.Length; i++)
            {
                interactorsProp.GetArrayElementAtIndex(i).objectReferenceValue = interactors[i];
            }

            so.ApplyModifiedProperties();
            return canvasObj;
        }

        // ====================================================================
        // CONSTRUCCIÓN DE PANELES
        // ====================================================================
        private static GameObject BuildLessonsPanel(
            Transform parent, TMP_FontAsset font, List<LessonDefinitionSO> lessons,
            out Transform container, out TextMeshProUGUI title, out TextMeshProUGUI desc, out TextMeshProUGUI steps,
            out Button btnStart, out TextMeshProUGUI btnStartText)
        {
            RectTransform panel = CreateRect("Panel_Lessons", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            // Subcolumna izquierda: Lista de lecciones
            RectTransform listCol = CreateRect("LessonsListColumn", panel);
            listCol.anchorMin = new Vector2(0f, 0f);
            listCol.anchorMax = new Vector2(0.48f, 1f);
            listCol.offsetMin = Vector2.zero;
            listCol.offsetMax = new Vector2(-15f, 0f);

            TextMeshProUGUI listTitle = AddText(listCol, "Title", font, "Lecciones del Currículum", 38f, Color.white, TextAlignmentOptions.TopLeft);
            listTitle.fontStyle = FontStyles.Bold;
            listTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            listTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            listTitle.rectTransform.sizeDelta = new Vector2(0f, 50f);
            listTitle.rectTransform.anchoredPosition = new Vector2(0f, -25f);

            RectTransform itemsBox = CreateRect("ItemsBox", listCol);
            itemsBox.anchorMin = new Vector2(0f, 0f);
            itemsBox.anchorMax = new Vector2(1f, 1f);
            itemsBox.offsetMin = Vector2.zero;
            itemsBox.offsetMax = new Vector2(0f, -65f);
            var layout = itemsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            container = itemsBox;

            for (int i = 0; i < lessons.Count; i++)
            {
                var lesson = lessons[i];
                string label = $"{i + 1:D2}  {lesson.lessonTitle}";
                CreateLessonCardButton($"LessonItem_{i + 1}", itemsBox, font, label, 110f);
            }

            // Subcolumna derecha: Detalle de la lección seleccionada
            RectTransform detailCol = CreateRect("LessonDetailColumn", panel);
            detailCol.anchorMin = new Vector2(0.48f, 0f);
            detailCol.anchorMax = new Vector2(1f, 1f);
            detailCol.offsetMin = new Vector2(15f, 0f);
            detailCol.offsetMax = Vector2.zero;

            RectTransform cardBox = CreateRect("DetailCard", detailCol);
            cardBox.anchorMin = new Vector2(0f, 0f);
            cardBox.anchorMax = new Vector2(1f, 1f);
            cardBox.offsetMin = new Vector2(0f, 120f);
            cardBox.offsetMax = Vector2.zero;
            Image cardBg = cardBox.gameObject.AddComponent<Image>();
            cardBg.color = s_CardBgColor;

            title = AddText(cardBox, "DetailTitle", font, "Título de la Lección", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(30f, -70f);
            title.rectTransform.offsetMax = new Vector2(-30f, -25f);

            steps = AddText(cardBox, "DetailSteps", font, "Pasos formativos a cumplir: 5", 30f, new Color(0.22f, 0.74f, 0.97f, 1f), TextAlignmentOptions.TopLeft);
            steps.rectTransform.anchorMin = new Vector2(0f, 1f);
            steps.rectTransform.anchorMax = new Vector2(1f, 1f);
            steps.rectTransform.offsetMin = new Vector2(30f, -120f);
            steps.rectTransform.offsetMax = new Vector2(-30f, -75f);

            desc = AddText(cardBox, "DetailDesc", font, "Descripción detallada...", 28f, new Color(0.85f, 0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.rectTransform.anchorMin = new Vector2(0f, 0f);
            desc.rectTransform.anchorMax = new Vector2(1f, 1f);
            desc.rectTransform.offsetMin = new Vector2(30f, 25f);
            desc.rectTransform.offsetMax = new Vector2(-30f, -135f);

            // Botón verde de acción en la parte inferior
            RectTransform actionBox = CreateRect("ActionBox", detailCol);
            actionBox.anchorMin = new Vector2(0f, 0f);
            actionBox.anchorMax = new Vector2(1f, 0f);
            actionBox.sizeDelta = new Vector2(0f, 100f);
            actionBox.anchoredPosition = new Vector2(0f, 50f);

            btnStart = CreateActionButton("Btn_StartLesson", actionBox, font, "COMENZAR LECCIÓN", s_GreenActionColor);
            btnStartText = btnStart.GetComponentInChildren<TextMeshProUGUI>();

            return panel.gameObject;
        }

        private static GameObject BuildCircuitsPanel(
            Transform parent, TMP_FontAsset font,
            out Transform container, out TextMeshProUGUI title, out TextMeshProUGUI desc, out TextMeshProUGUI info,
            out Button btnStart, out TextMeshProUGUI btnStartText)
        {
            RectTransform panel = CreateRect("Panel_Circuits", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            RectTransform listCol = CreateRect("CircuitsListColumn", panel);
            listCol.anchorMin = new Vector2(0f, 0f);
            listCol.anchorMax = new Vector2(0.48f, 1f);
            listCol.offsetMin = Vector2.zero;
            listCol.offsetMax = new Vector2(-15f, 0f);

            TextMeshProUGUI listTitle = AddText(listCol, "Title", font, "Circuitos Integradores", 38f, Color.white, TextAlignmentOptions.TopLeft);
            listTitle.fontStyle = FontStyles.Bold;
            listTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            listTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            listTitle.rectTransform.sizeDelta = new Vector2(0f, 50f);
            listTitle.rectTransform.anchoredPosition = new Vector2(0f, -25f);

            RectTransform itemsBox = CreateRect("ItemsBox", listCol);
            itemsBox.anchorMin = new Vector2(0f, 0f);
            itemsBox.anchorMax = new Vector2(1f, 1f);
            itemsBox.offsetMin = Vector2.zero;
            itemsBox.offsetMax = new Vector2(0f, -65f);
            var layout = itemsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            container = itemsBox;

            CreateLessonCardButton("CircuitItem_1", itemsBox, font, "Circuito Integrador 1: Tráfico y Señales", 125f);
            CreateLessonCardButton("CircuitItem_2", itemsBox, font, "Circuito Integrador 2: Maniobras y Parking", 125f);
            CreateLessonCardButton("CircuitItem_3", itemsBox, font, "Práctica Libre: Ciudad Completa", 125f);

            RectTransform detailCol = CreateRect("CircuitDetailColumn", panel);
            detailCol.anchorMin = new Vector2(0.48f, 0f);
            detailCol.anchorMax = new Vector2(1f, 1f);
            detailCol.offsetMin = new Vector2(15f, 0f);
            detailCol.offsetMax = Vector2.zero;

            RectTransform cardBox = CreateRect("DetailCard", detailCol);
            cardBox.anchorMin = new Vector2(0f, 0f);
            cardBox.anchorMax = new Vector2(1f, 1f);
            cardBox.offsetMin = new Vector2(0f, 120f);
            cardBox.offsetMax = Vector2.zero;
            Image cardBg = cardBox.gameObject.AddComponent<Image>();
            cardBg.color = s_CardBgColor;

            title = AddText(cardBox, "DetailTitle", font, "Circuito Integrador", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(30f, -70f);
            title.rectTransform.offsetMax = new Vector2(-30f, -25f);

            info = AddText(cardBox, "DetailInfo", font, "Dificultad: Intermedio  |  Tiempo: 15 min", 30f, new Color(0.22f, 0.74f, 0.97f, 1f), TextAlignmentOptions.TopLeft);
            info.rectTransform.anchorMin = new Vector2(0f, 1f);
            info.rectTransform.anchorMax = new Vector2(1f, 1f);
            info.rectTransform.offsetMin = new Vector2(30f, -120f);
            info.rectTransform.offsetMax = new Vector2(-30f, -75f);

            desc = AddText(cardBox, "DetailDesc", font, "Descripción del circuito...", 28f, new Color(0.85f, 0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.rectTransform.anchorMin = new Vector2(0f, 0f);
            desc.rectTransform.anchorMax = new Vector2(1f, 1f);
            desc.rectTransform.offsetMin = new Vector2(30f, 25f);
            desc.rectTransform.offsetMax = new Vector2(-30f, -135f);

            RectTransform actionBox = CreateRect("ActionBox", detailCol);
            actionBox.anchorMin = new Vector2(0f, 0f);
            actionBox.anchorMax = new Vector2(1f, 0f);
            actionBox.sizeDelta = new Vector2(0f, 100f);
            actionBox.anchoredPosition = new Vector2(0f, 50f);

            btnStart = CreateActionButton("Btn_StartCircuit", actionBox, font, "INICIAR RECORRIDO", s_GreenActionColor);
            btnStartText = btnStart.GetComponentInChildren<TextMeshProUGUI>();

            return panel.gameObject;
        }

        private static GameObject BuildControlsPanel(Transform parent, TMP_FontAsset font)
        {
            RectTransform panel = CreateRect("Panel_Controls", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            TextMeshProUGUI title = AddText(panel, "Title", font, "Mandos y Controles de Manejo", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 0f, 60f);

            RectTransform grid = CreateRect("CardsGrid", panel);
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.offsetMin = Vector2.zero;
            grid.offsetMax = new Vector2(0f, -75f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(530f, 220f);
            layout.spacing = new Vector2(25f, 25f);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;

            CreateControlCard(grid, font, "Volante de Dirección", "Grip de cualquier mano para agarrar y girar el volante de forma natural.");
            CreateControlCard(grid, font, "Palanca de Cambios", "Grip para mover la palanca entre las marchas P (Parking), R (Reversa), N (Neutro) y D (Directa).");
            CreateControlCard(grid, font, "Acelerador y Freno", "Gatillo derecho para acelerar progresivamente. Gatillo izquierdo para accionar el pedal de freno.");
            CreateControlCard(grid, font, "Freno de Mano", "Grip para subir la palanca e inmovilizar el coche, o bajarla para liberar las ruedas.");
            CreateControlCard(grid, font, "Guiños y Luces", "Palanca izquierda en la columna para guiños. Perilla selectora en el tablero para luces reglamentarias.");
            CreateControlCard(grid, font, "Interacción en Menús", "Apuntá con el rayo de tu mano hacia los botones y presioná el Gatillo (Trigger) para confirmar.");

            return panel.gameObject;
        }

        private static void CreateControlCard(Transform parent, TMP_FontAsset font, string title, string description)
        {
            RectTransform card = CreateRect("Card", parent);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = s_CardBgColor;

            TextMeshProUGUI t = AddText(card, "CardTitle", font, title, 32f, new Color(0.22f, 0.74f, 0.97f, 1f), TextAlignmentOptions.TopLeft);
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.anchorMin = new Vector2(0f, 1f);
            t.rectTransform.anchorMax = new Vector2(1f, 1f);
            t.rectTransform.offsetMin = new Vector2(20f, -50f);
            t.rectTransform.offsetMax = new Vector2(-20f, -15f);

            TextMeshProUGUI d = AddText(card, "CardDesc", font, description, 26f, new Color(0.82f, 0.86f, 0.92f, 1f), TextAlignmentOptions.TopLeft);
            d.textWrappingMode = TextWrappingModes.Normal;
            d.rectTransform.anchorMin = new Vector2(0f, 0f);
            d.rectTransform.anchorMax = new Vector2(1f, 1f);
            d.rectTransform.offsetMin = new Vector2(20f, 15f);
            d.rectTransform.offsetMax = new Vector2(-20f, -60f);
        }

        private static GameObject BuildCalibrationPanel(Transform parent, TMP_FontAsset font,
            out Slider sLateral, out Slider sHeight, out Slider sDepth, out Button btnReset)
        {
            RectTransform panel = CreateRect("Panel_Calibration", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            TextMeshProUGUI title = AddText(panel, "Title", font, "Calibración del Puesto de Conducción", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 0f, 60f);

            TextMeshProUGUI subtitle = AddText(panel, "Subtitle", font, "Ajustá la altura y distancia de la vista para alcanzar el volante y pedales con comodidad.", 30f, new Color(0.65f, 0.72f, 0.85f, 1f), TextAlignmentOptions.TopLeft);
            subtitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            subtitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            subtitle.rectTransform.offsetMin = new Vector2(0f, -110f);
            subtitle.rectTransform.offsetMax = new Vector2(0f, -65f);

            RectTransform slidersBox = CreateRect("SlidersBox", panel);
            slidersBox.anchorMin = new Vector2(0f, 0f);
            slidersBox.anchorMax = new Vector2(1f, 1f);
            slidersBox.offsetMin = new Vector2(100f, 150f);
            slidersBox.offsetMax = new Vector2(-100f, -130f);

            CreateLabel("Lbl_Lateral", slidersBox, font, "Posición Lateral (Izquierda / Derecha)", 28f, new Vector2(0f, 120f), new Vector2(900f, 35f));
            sLateral = CreateModernSlider("Slider_Lateral", slidersBox, new Vector2(0f, 80f), new Vector2(900f, 32f));

            CreateLabel("Lbl_Height", slidersBox, font, "Altura del Asiento (Arriba / Abajo)", 28f, new Vector2(0f, 0f), new Vector2(900f, 35f));
            sHeight = CreateModernSlider("Slider_Height", slidersBox, new Vector2(0f, -40f), new Vector2(900f, 32f));

            CreateLabel("Lbl_Depth", slidersBox, font, "Distancia Longitudinal (Adelante / Atrás)", 28f, new Vector2(0f, -120f), new Vector2(900f, 35f));
            sDepth = CreateModernSlider("Slider_Depth", slidersBox, new Vector2(0f, -160f), new Vector2(900f, 32f));

            RectTransform resetBox = CreateRect("ResetBox", panel);
            resetBox.anchorMin = new Vector2(0.5f, 0f);
            resetBox.anchorMax = new Vector2(0.5f, 0f);
            resetBox.sizeDelta = new Vector2(500f, 85f);
            resetBox.anchoredPosition = new Vector2(0f, 60f);

            btnReset = CreateActionButton("Btn_ResetCalib", resetBox, font, "RESTABLECER CALIBRACIÓN", new Color(0.25f, 0.32f, 0.45f, 1f));

            return panel.gameObject;
        }

        private static GameObject BuildCreditsPanel(Transform parent, TMP_FontAsset font)
        {
            RectTransform panel = CreateRect("Panel_Credits", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            TextMeshProUGUI title = AddText(panel, "Title", font, "Acerca de Primer Volante", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 0f, 60f);

            RectTransform card = CreateRect("Card_Credits", panel);
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 1f);
            card.offsetMin = Vector2.zero;
            card.offsetMax = new Vector2(0f, -75f);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = s_CardBgColor;

            string creditBody = "<b>Primer Volante</b> es una plataforma inmersiva de entrenamiento para nuevos conductores basada en Realidad Virtual.\n\n" +
                                "<b>Objetivo Pedagógico:</b>\n" +
                                "Proveer un entorno seguro, progresivo y realista para que los estudiantes adquieran reflejos, automatismos en los mandos del vehículo, nociones de prioridad de paso, lectura de señalización vial y respeto por las normas de tránsito.\n\n" +
                                "<b>Características del Simulador:</b>\n" +
                                "• Física de vehículo calibrada y mandos de cabina bimanuales con agarre VR.\n" +
                                "• Sistema de detección y penalización de infracciones viales en tiempo real.\n" +
                                "• Currículum de 6 lecciones guiadas por voz y HUD visual.\n" +
                                "• Sistema de navegación GPS dinámico en entorno urbano completo.\n\n" +
                                "<i>Versión: 2.0 (Menú VR Unificado)</i>";

            TextMeshProUGUI bodyText = AddText(card, "Body", font, creditBody, 28f, new Color(0.88f, 0.90f, 0.96f, 1f), TextAlignmentOptions.TopLeft);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            Stretch(bodyText.rectTransform, 40f, 40f, 40f, 40f);

            return panel.gameObject;
        }

        // ====================================================================
        // MODAL DE SALIDA
        // ====================================================================
        private static GameObject BuildExitModal(Transform parent, TMP_FontAsset font, out Button btnConfirm, out Button btnCancel)
        {
            RectTransform modalRoot = CreateRect("Modal_ExitConfirmation", parent);
            Stretch(modalRoot, 0f, 0f, 0f, 0f);
            Image backdrop = modalRoot.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.03f, 0.06f, 0.85f);

            RectTransform box = CreateRect("ConfirmBox", modalRoot);
            box.anchorMin = new Vector2(0.5f, 0.5f);
            box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(900f, 480f);
            Image boxBg = box.gameObject.AddComponent<Image>();
            boxBg.color = new Color(0.14f, 0.08f, 0.10f, 1f);

            TextMeshProUGUI title = AddText(box, "Title", font, "¿Desea salir del simulador?", 48f, Color.white, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 35f, 70f);

            TextMeshProUGUI sub = AddText(box, "Subtitle", font, "Se cerrará la aplicación y volverá al sistema operativo.", 30f, new Color(0.8f, 0.84f, 0.9f, 1f), TextAlignmentOptions.Center);
            sub.rectTransform.anchorMin = new Vector2(0.05f, 0.5f);
            sub.rectTransform.anchorMax = new Vector2(0.95f, 0.5f);
            sub.rectTransform.sizeDelta = new Vector2(0f, 60f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, 20f);

            RectTransform btnBox = CreateRect("ButtonRow", box);
            btnBox.anchorMin = new Vector2(0f, 0f);
            btnBox.anchorMax = new Vector2(1f, 0f);
            btnBox.sizeDelta = new Vector2(0f, 110f);
            btnBox.anchoredPosition = new Vector2(0f, 75f);
            var layout = btnBox.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.padding = new RectOffset(60, 60, 10, 10);

            btnCancel = CreateActionButton("Btn_Cancel", btnBox, font, "CANCELAR", new Color(0.24f, 0.28f, 0.38f, 1f));
            btnConfirm = CreateActionButton("Btn_Confirm", btnBox, font, "CONFIRMAR SALIDA", s_ExitRedColor);

            modalRoot.gameObject.SetActive(false);
            return modalRoot.gameObject;
        }

        // ====================================================================
        // HELPERS DE UI
        // ====================================================================
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image AddImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            return tmp;
        }

        private static void CreateLabel(string name, Transform parent, TMP_FontAsset font, string text, float size, Vector2 pos, Vector2 delta)
        {
            TextMeshProUGUI t = AddText(parent, name, font, text, size, Color.white, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.anchoredPosition = pos;
            t.rectTransform.sizeDelta = delta;
        }

        private static Button CreateMenuButton(string name, Transform parent, TMP_FontAsset font, string text, Color bgColor, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(0f, height);

            var img = go.GetComponent<Image>();
            img.color = bgColor;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.28f, 0.48f, 0.95f, 1f);
            colors.pressedColor = new Color(0.18f, 0.35f, 0.8f, 1f);
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 30f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform, 30f, 20f, 10f, 10f);

            return btn;
        }

        private static Button CreateLessonCardButton(string name, Transform parent, TMP_FontAsset font, string text, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(0f, height);

            var img = go.GetComponent<Image>();
            img.color = s_CardBgColor;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.20f, 0.30f, 0.50f, 1f);
            colors.pressedColor = new Color(0.14f, 0.22f, 0.38f, 1f);
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 28f, Color.white, TextAlignmentOptions.MidlineLeft);
            label.textWrappingMode = TextWrappingModes.Normal;
            Stretch(label.rectTransform, 25f, 20f, 10f, 10f);

            return btn;
        }

        private static Button CreateActionButton(string name, Transform parent, TMP_FontAsset font, string text, Color bgColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect, 0f, 0f, 0f, 0f);

            var img = go.GetComponent<Image>();
            img.color = bgColor;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.15f;
            colors.pressedColor = bgColor * 0.85f;
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 36f, Color.white, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform, 10f, 10f, 5f, 5f);

            return btn;
        }

        private static Slider CreateModernSlider(string name, Transform parent, Vector2 pos, Vector2 delta)
        {
            var sliderObj = new GameObject(name, typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(parent, false);
            var rect = (RectTransform)sliderObj.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = delta;

            var bg = AddImage(sliderObj.transform, "Background", new Color(0.14f, 0.17f, 0.25f, 1f));
            Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            var fillArea = CreateRect("Fill Area", sliderObj.transform);
            Stretch(fillArea, 5f, 5f, 4f, 4f);

            var fill = AddImage(fillArea, "Fill", new Color(0.15f, 0.39f, 0.92f, 1f));
            Stretch(fill.rectTransform, 0f, 0f, 0f, 0f);

            var handleArea = CreateRect("Handle Slide Area", sliderObj.transform);
            Stretch(handleArea, 10f, 10f, 0f, 0f);

            var handle = AddImage(handleArea, "Handle", Color.white);
            handle.rectTransform.sizeDelta = new Vector2(25f, 40f);

            var slider = sliderObj.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private static void Stretch(RectTransform rt, float left, float right, float bottom, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorTop(RectTransform rt, float topMargin, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -topMargin);
        }
    }
}
