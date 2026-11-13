using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.Lessons;
using PrimerVolante.MainMenu;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para la construcción automatizada, limpia e idempotente de la escena NewMainMenu:
    /// - Duplica la base de MainMenu o Lesson_01 a NewMainMenu.unity (si no existe).
    /// - Elimina completamente Car09, vehículos y controladores obsoletos de la cabina.
    /// - Configura XR Origin (XR Rig) en (0,0,0) con DriverSeatFollower anclado a MenuViewpoint y con InputActionManager activo.
    /// - Construye NewMainMenu_Canvas en World Space con TrackedDeviceGraphicRaycaster calibrado y sin GraphicRaycaster conflictivo.
    /// - Implementa el diseño ortogonal por solapas con tarjetas de controles 3D (ControllerIllustration),
    ///   currículum de lecciones con botón "Comenzar Lección", circuitos integradores con botón "Iniciar Recorrido",
    ///   panel de calibración/créditos y modal de confirmación de salida centrado y sin errores visuales.
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
        private const string CONTROLLER_MODEL_PATH = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Models/UniversalController.fbx";
        private const string INPUT_ACTIONS_PATH = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        public const string LEGACY_PLATFORM_NAME = "Menu_FloorPlatform";
        public const string MENU_VIEWPOINT_NAME = "MenuViewpoint";
        // Superficie del suelo del menú (cara superior de Ground_MainMenu): donde quedan los pies del jugador con tracking Floor.
        public const float MENU_FLOOR_Y = 0f;
        private const string CURRICULUM_FOLDER = "Assets/Scripts/Lessons/Curriculum";
        private const int LESSON_COUNT = 6;

        // Dimensiones del Canvas en World Space (scale 0.001f -> 2.3m x 1.35m)
        private const float CANVAS_W = 2300f;
        private const float CANVAS_H = 1350f;
        // Posición revisada en el visor (commits dc9c19e, fe2d5ec y 97a6fa2): más cerca y más baja que la original (0, 1.35, 2.3).
        public static readonly Vector3 CanvasWorldPosition = new Vector3(0f, 0.95f, 1.264f);

        // Paleta de Colores estandarizada (basada en MainMenuSetup original)
        private static readonly Color s_BgCanvasColor = new Color(0.04f, 0.05f, 0.09f, 0.98f);     // #0a0d17
        private static readonly Color s_SidebarBgColor = new Color(0.09f, 0.11f, 0.17f, 1f);       // #171c2b
        private static readonly Color s_ContentAreaBgColor = new Color(0.07f, 0.09f, 0.14f, 1f);   // #121724
        private static readonly Color s_CardBgColor = new Color(0.13f, 0.16f, 0.24f, 1f);          // #21293d
        private static readonly Color s_TabActiveColor = new Color(0.15f, 0.42f, 0.90f, 1f);       // #266be6
        private static readonly Color s_TabInactiveColor = new Color(0.10f, 0.12f, 0.18f, 1f);     // #1a1f2e
        private static readonly Color s_GreenActionColor = new Color(0.08f, 0.68f, 0.45f, 1f);     // #14ad73
        private static readonly Color s_ExitRedColor = new Color(0.85f, 0.16f, 0.16f, 1f);         // #d92929

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

            Debug.Log("[NewMainMenuSetup] ¡Nueva escena NewMainMenu configurada y guardada con éxito!");
        }

        public static NewMainMenuController BuildInActiveScene() => BuildInActiveScene(SCENE_PATH);

        /// <summary>
        /// Construye el menú en la escena activa, que tiene que estar en <paramref name="expectedScenePath"/>. Las verificaciones
        /// pasan la ruta de una copia temporal para probar la construcción sin tocar la escena real.
        /// </summary>
        public static NewMainMenuController BuildInActiveScene(string expectedScenePath)
        {
            string activePath = EditorSceneManager.GetActiveScene().path;
            if (activePath != expectedScenePath)
            {
                Debug.LogError($"[NewMainMenuSetup] La escena activa es '{activePath}', no {expectedScenePath}. Operación cancelada.");
                return null;
            }

            // 1. Eliminar completamente el vehículo Car09 y controladores de conducción
            RemoveCarAndCabinObjects();

            // 2. Eliminar menús legados de la escena
            RemoveLegacyMenuObjects();

            // 3. Configurar XR Origin (XR Rig) y anclaje estático (desactivando gravedad y locomoción)
            GameObject xrOriginObj = SetupXROrigin();

            // 4. Garantizar piso físico y plataforma de menú en el origen (0, 0, 0)
            EnsureGroundPlatform();

            // 5. Garantizar EventSystem con XRUIInputModule
            InGameMenuSetup.EnsureEventSystem();

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            GameObject controllerModel = AssetDatabase.LoadAssetAtPath<GameObject>(CONTROLLER_MODEL_PATH);
            List<LessonDefinitionSO> lessons = LoadLessons();

            // 5. Construir Canvas y Jerarquía Visual
            GameObject canvasObj = BuildCanvas(font, controllerModel, lessons, xrOriginObj, out NewMainMenuController controller);

            // 5.b. Menú de pausa por desconexión VR, enlazado al controlador y al CanvasGroup del canvas
            EnsurePauseMenu(canvasObj, controller);

            // 6. Asignar capa UI estrictamente en toda la jerarquía para compatibilidad con UIOnlyFarCasting
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

        // ====================================================================
        // LIMPIEZA DE VEHÍCULO Y OBJETOS DE CABINA
        // ====================================================================
        private static void RemoveCarAndCabinObjects()
        {
            // Destruir todas las instancias de VehicleController en la escena
            var cars = Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include);
            foreach (var car in cars)
            {
                if (car != null && car.gameObject != null)
                {
                    Object.DestroyImmediate(car.gameObject);
                }
            }

            // Destruir cualquier GameObject que se llame Car09 o Car08
            GameObject carObj = GameObject.Find("Car09");
            if (carObj != null) Object.DestroyImmediate(carObj);

            GameObject car08Obj = GameObject.Find("Car08");
            if (car08Obj != null) Object.DestroyImmediate(car08Obj);

            // Destruir controladores de espejo o cabina si quedaron sueltos
            foreach (var mirror in Object.FindObjectsByType<VRRearviewMirror>(FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(mirror.gameObject);
            }
        }

        private static void RemoveLegacyMenuObjects()
        {
            GameObject oldCanvas = GameObject.Find("MainMenu_Canvas");
            if (oldCanvas != null) Object.DestroyImmediate(oldCanvas);

            GameObject oldController = GameObject.Find("MainMenuController");
            if (oldController != null) Object.DestroyImmediate(oldController);

            GameObject oldHighlighter = GameObject.Find("MainMenu_Highlighter");
            if (oldHighlighter != null) Object.DestroyImmediate(oldHighlighter);

            // InGameMenu_Canvas e InGameMenuController se conservan: son el menú de la pausa por desconexión VR
            // (ver EnsurePauseMenu), no del MainMenu viejo.
        }

        /// <summary>
        /// Garantiza (idempotente) el menú de pausa por desconexión VR en la escena y lo enlaza al controlador del menú nuevo:
        /// el botón Menu y Escape no lo abren (<c>m_MenuButtonEnabled = false</c>), "Calibrar posición" queda oculto
        /// (<c>m_ShowCalibrationButton = false</c>, no hay auto ni asiento) y el canvas recibe un CanvasGroup en la raíz que el controlador
        /// pone no interactuable mientras dura la pausa.
        /// </summary>
        private static void EnsurePauseMenu(GameObject canvasObj, NewMainMenuController controller)
        {
            InGameMenuController pauseMenu = InGameMenuSetup.EnsureInGameMenuInActiveScene();

            var soMenu = new SerializedObject(pauseMenu);
            soMenu.Update();
            soMenu.FindProperty("m_MenuButtonEnabled").boolValue = false;
            soMenu.FindProperty("m_ShowCalibrationButton").boolValue = false;
            soMenu.ApplyModifiedProperties();
            EditorUtility.SetDirty(pauseMenu);

            // CanvasGroup en la raíz del canvas (el controlador lo resuelve solo desde ahí; el setup lo deja en la escena).
            if (canvasObj.GetComponent<CanvasGroup>() == null) canvasObj.AddComponent<CanvasGroup>();

            var soController = new SerializedObject(controller);
            soController.Update();
            soController.FindProperty("m_InGameMenu").objectReferenceValue = pauseMenu;
            soController.ApplyModifiedProperties();
        }

        // ====================================================================
        // CONFIGURACIÓN DE XR ORIGIN
        // ====================================================================
        private static GameObject SetupXROrigin()
        {
            GameObject xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
            if (xrOriginObj == null)
            {
                var origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
                if (origin != null) xrOriginObj = origin.gameObject;
            }

            if (xrOriginObj != null)
            {
                // DriverSeatFollower con MenuViewpoint como anclaje fijo (no hay auto ni asiento): compensa la pose de la cabeza
                // para que el jugador aparezca sobre el menú sin importar el origen de tracking del visor.
                EnsureMenuViewpointAndFollower(xrOriginObj);

                // Centrar el Rig en el origen del mundo
                xrOriginObj.transform.position = Vector3.zero;
                xrOriginObj.transform.rotation = Quaternion.identity;

                // Desactivar CharacterController en el rig: evita caída al vacío y colisiones físicas en menú
                CharacterController cc = xrOriginObj.GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.enabled = false;
                }

                // Desactivar sub-objeto Locomotion (contiene GravityProvider, ContinuousMove, SnapTurn, etc.)
                Transform locomotion = xrOriginObj.transform.Find("Locomotion");
                if (locomotion != null)
                {
                    locomotion.gameObject.SetActive(false);
                }

                // Desactivar cualquier componente de locomoción o física en el rig
                foreach (var mb in xrOriginObj.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb != null && mb.GetType().Namespace != null && mb.GetType().Namespace.Contains("Locomotion"))
                    {
                        mb.enabled = false;
                    }
                }

                // Configurar XROrigin con Tracking Origin Floor y CameraYOffset estándar (1.36m)
                var originComp = xrOriginObj.GetComponent<Unity.XR.CoreUtils.XROrigin>();
                if (originComp != null)
                {
                    originComp.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
                    originComp.CameraYOffset = 1.36f;
                }

                // Eliminar cámaras huérfanas que no pertenezcan al XR Origin
                Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
                foreach (Camera cam in cameras)
                {
                    if (cam != null && !cam.transform.IsChildOf(xrOriginObj.transform))
                    {
                        Object.DestroyImmediate(cam.gameObject);
                    }
                }

                // Asegurar que la cámara principal de XR Origin esté activa y con tag MainCamera
                Camera headCam = xrOriginObj.GetComponentInChildren<Camera>(true);
                if (headCam != null)
                {
                    headCam.gameObject.SetActive(true);
                    headCam.tag = "MainCamera";
                }

                // Garantizar InputActionManager con XRI Default Input Actions activo
                var inputMgr = xrOriginObj.GetComponent<InputActionManager>();
                if (inputMgr == null) inputMgr = xrOriginObj.AddComponent<InputActionManager>();

                var inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(INPUT_ACTIONS_PATH);
                if (inputAsset != null)
                {
                    inputMgr.actionAssets = new List<InputActionAsset> { inputAsset };
                }
            }

            return xrOriginObj;
        }

        /// <summary>
        /// Garantiza (idempotente) el anclaje <see cref="MENU_VIEWPOINT_NAME"/> en la raíz de la escena, a la altura del suelo del menú
        /// (<see cref="MENU_FLOOR_Y"/>) y mirando a +Z hacia el canvas, y el <see cref="DriverSeatFollower"/> del XR Origin con ese anclaje
        /// como asiento, siguiéndolo y con la calibración en cero. No duplica ninguno de los dos si ya existen.
        /// </summary>
        public static DriverSeatFollower EnsureMenuViewpointAndFollower(GameObject xrOriginObj)
        {
            GameObject viewpoint = null;
            foreach (GameObject root in xrOriginObj.scene.GetRootGameObjects())
            {
                if (root.name == MENU_VIEWPOINT_NAME)
                {
                    viewpoint = root;
                    break;
                }
            }
            if (viewpoint == null)
            {
                viewpoint = new GameObject(MENU_VIEWPOINT_NAME);
                SceneManager.MoveGameObjectToScene(viewpoint, xrOriginObj.scene);
            }
            viewpoint.transform.SetPositionAndRotation(new Vector3(0f, MENU_FLOOR_Y, 0f), Quaternion.identity);
            viewpoint.transform.localScale = Vector3.one;

            DriverSeatFollower follower = xrOriginObj.GetComponent<DriverSeatFollower>();
            if (follower == null) follower = xrOriginObj.AddComponent<DriverSeatFollower>();

            var so = new SerializedObject(follower);
            so.Update();
            so.FindProperty("m_DriverSeat").objectReferenceValue = viewpoint.transform;
            so.FindProperty("m_FollowSeat").boolValue = true;
            so.FindProperty("m_CalibrationOffset").vector3Value = Vector3.zero;
            // El menú no tiene asiento: el anclaje es fijo y lo guardado de la calibración no debe mover la vista.
            so.FindProperty("m_UsePersistentCalibration").boolValue = false;
            so.ApplyModifiedProperties();
            follower.enabled = true;
            EditorUtility.SetDirty(follower);
            return follower;
        }

        // ====================================================================
        // PISO FÍSICO Y PLATAFORMA DEL MENÚ PRINCIPAL
        // ====================================================================
        private static void EnsureGroundPlatform()
        {
            // 1. Collider de piso físico centrado en el origen (superficie superior en y = MENU_FLOOR_Y)
            GameObject ground = GameObject.Find("Ground_FallbackCollider");
            if (ground == null) ground = GameObject.Find("Ground_MainMenu");
            if (ground == null) ground = new GameObject("Ground_MainMenu");
            ground.name = "Ground_MainMenu";
            ground.transform.rotation = Quaternion.identity;
            ground.transform.localScale = Vector3.one;

            BoxCollider box = ground.GetComponent<BoxCollider>();
            if (box == null) box = ground.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(200f, 1f, 200f);
            // La cara superior del collider queda en MENU_FLOOR_Y (el centro del collider está en el origen del objeto).
            ground.transform.position = new Vector3(0f, MENU_FLOOR_Y - box.size.y * 0.5f, 0f);
            box.isTrigger = false;
            box.enabled = true;
            ground.layer = 0; // Default

            // 2. Sin plataforma visual: el disco azul de 14 m se quitó a mano de la escena (commit dc9c19e).
            // Se borra si quedó de una versión anterior del setup, para no volver a crearlo al regenerar.
            GameObject platform = GameObject.Find(LEGACY_PLATFORM_NAME);
            if (platform != null) Object.DestroyImmediate(platform);
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

        // ====================================================================
        // CONSTRUCCIÓN DEL CANVAS WORLD SPACE
        // ====================================================================
        private static GameObject BuildCanvas(TMP_FontAsset font, GameObject controllerModel, List<LessonDefinitionSO> lessons,
            GameObject xrOriginObj, out NewMainMenuController controller)
        {
            GameObject existing = GameObject.Find(CANVAS_NAME);
            if (existing != null) Object.DestroyImmediate(existing);

            var canvasObj = new GameObject(CANVAS_NAME, typeof(RectTransform));
            RectTransform canvasRect = (RectTransform)canvasObj.transform;
            canvasRect.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);
            canvasObj.transform.localScale = Vector3.one * 0.001f;
            canvasObj.transform.position = CanvasWorldPosition;
            canvasObj.transform.rotation = Quaternion.identity; // Mirando hacia el jugador en (0,0,0)

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            Camera headCam = xrOriginObj != null ? xrOriginObj.GetComponentInChildren<Camera>(true) : Camera.main;
            canvas.worldCamera = headCam;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;

            // ÚNICAMENTE TrackedDeviceGraphicRaycaster (sin GraphicRaycaster redundante que causa conflicto)
            GraphicRaycaster oldRaycaster = canvasObj.GetComponent<GraphicRaycaster>();
            if (oldRaycaster != null) Object.DestroyImmediate(oldRaycaster);

            var trackedRaycaster = canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();
            trackedRaycaster.checkFor2DOcclusion = false;
            trackedRaycaster.checkFor3DOcclusion = false;

            // Fondo general (raycastTarget = false para no bloquear interactores)
            Image bg = AddImage(canvasObj.transform, "Background", s_BgCanvasColor, false);
            Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            // ================================================================
            // HEADER SUPERIOR
            // ================================================================
            RectTransform header = CreateRect("Header", canvasObj.transform);
            AnchorTop(header, 0f, 110f);
            Image headerBg = header.gameObject.AddComponent<Image>();
            headerBg.color = new Color(0.06f, 0.08f, 0.13f, 1f);
            headerBg.raycastTarget = false;

            TextMeshProUGUI headerTitle = AddText(header, "Title", font, "PRIMER VOLANTE", 50f, Color.white, TextAlignmentOptions.MidlineLeft);
            headerTitle.fontStyle = FontStyles.Bold;
            headerTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
            headerTitle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            headerTitle.rectTransform.offsetMin = new Vector2(40f, 0f);
            headerTitle.rectTransform.offsetMax = new Vector2(-10f, 0f);

            TextMeshProUGUI headerSub = AddText(header, "Subtitle", font, "Simulador de Aprendizaje y Conducción Segura en RV", 30f, new Color(0.60f, 0.68f, 0.82f, 1f), TextAlignmentOptions.MidlineRight);
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
            sidebar.sizeDelta = new Vector2(500f, -110f);
            sidebar.anchoredPosition = new Vector2(0f, -55f);
            Image sidebarBg = sidebar.gameObject.AddComponent<Image>();
            sidebarBg.color = s_SidebarBgColor;
            sidebarBg.raycastTarget = false;

            // Grupo de botones de navegación (solapas)
            RectTransform navGroup = CreateRect("NavButtons", sidebar);
            navGroup.anchorMin = new Vector2(0f, 0f);
            navGroup.anchorMax = new Vector2(1f, 1f);
            navGroup.offsetMin = new Vector2(25f, 135f);
            navGroup.offsetMax = new Vector2(-25f, -25f);
            var navLayout = navGroup.gameObject.AddComponent<VerticalLayoutGroup>();
            navLayout.spacing = 16f;
            navLayout.childControlWidth = true;
            navLayout.childControlHeight = false;
            navLayout.childForceExpandWidth = true;
            navLayout.childForceExpandHeight = false;

            Button btnTabControls = CreateMenuButton("Tab_Controles", navGroup, font, "Controles y Mandos", s_TabActiveColor, 90f);
            Button btnTabLessons = CreateMenuButton("Tab_Lecciones", navGroup, font, "Lecciones Formativas", s_TabInactiveColor, 90f);
            Button btnTabCircuits = CreateMenuButton("Tab_Circuitos", navGroup, font, "Circuitos / Integradores", s_TabInactiveColor, 90f);
            Button btnTabCalib = CreateMenuButton("Tab_Calibracion", navGroup, font, "Calibración de Asiento", s_TabInactiveColor, 90f);
            Button btnTabCredits = CreateMenuButton("Tab_Creditos", navGroup, font, "Créditos del Proyecto", s_TabInactiveColor, 90f);

            // Botón Salir (parte inferior de sidebar: botón rojo visible a todo lo ancho de la columna izquierda)
            Button btnExit = CreateExitButton("Btn_Exit", sidebar, font, "SALIR DEL SIMULADOR", s_ExitRedColor, 85f);

            // ================================================================
            // ÁREA DE CONTENIDO DERECHA
            // ================================================================
            RectTransform contentArea = CreateRect("ContentArea", canvasObj.transform);
            contentArea.anchorMin = new Vector2(0f, 0f);
            contentArea.anchorMax = new Vector2(1f, 1f);
            contentArea.offsetMin = new Vector2(530f, 25f);
            contentArea.offsetMax = new Vector2(-30f, -135f);
            Image contentBg = contentArea.gameObject.AddComponent<Image>();
            contentBg.color = s_ContentAreaBgColor;
            contentBg.raycastTarget = false;

            // Paneles de Contenido
            GameObject panelControls = BuildControlsPanel(contentArea, font, controllerModel);
            GameObject panelLessons = BuildLessonsPanel(contentArea, font, lessons,
                out Transform lessonItemsContainer, out TextMeshProUGUI lTitle, out TextMeshProUGUI lDesc,
                out Button btnStartLesson, out TextMeshProUGUI btnStartLessonText);

            GameObject panelCircuits = BuildCircuitsPanel(contentArea, font,
                out Transform circuitItemsContainer, out TextMeshProUGUI cTitle, out TextMeshProUGUI cDesc,
                out Button btnStartCircuit, out TextMeshProUGUI btnStartCircuitText);

            GameObject panelCalib = BuildCalibrationPanel(contentArea, font);
            GameObject panelCredits = BuildCreditsPanel(contentArea, font);

            // Modal de Salida
            GameObject exitModal = BuildExitModal(canvasObj.transform, font, out Button btnConfirmExit, out Button btnCancelExit);

            // ================================================================
            // CONTROLADOR DEL MENÚ
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
            so.FindProperty("m_BtnStartLesson").objectReferenceValue = btnStartLesson;
            so.FindProperty("m_BtnStartLessonText").objectReferenceValue = btnStartLessonText;

            // Circuitos
            so.FindProperty("m_CircuitItemsContainer").objectReferenceValue = circuitItemsContainer;
            so.FindProperty("m_CircuitDetailTitle").objectReferenceValue = cTitle;
            so.FindProperty("m_CircuitDetailDescription").objectReferenceValue = cDesc;
            so.FindProperty("m_BtnStartCircuit").objectReferenceValue = btnStartCircuit;
            so.FindProperty("m_BtnStartCircuitText").objectReferenceValue = btnStartCircuitText;

            // Modal Salida
            so.FindProperty("m_ExitModal").objectReferenceValue = exitModal;
            so.FindProperty("m_BtnConfirmExit").objectReferenceValue = btnConfirmExit;
            so.FindProperty("m_BtnCancelExit").objectReferenceValue = btnCancelExit;

            // Anclaje Estático del Jugador
            so.FindProperty("m_XROriginTransform").objectReferenceValue = xrOriginObj != null ? xrOriginObj.transform : null;

            // Interactores VR
            NearFarInteractor[] interactors = xrOriginObj != null
                ? xrOriginObj.GetComponentsInChildren<NearFarInteractor>(true)
                : Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);

            SerializedProperty farCastProp = so.FindProperty("m_FarCastInteractors");
            farCastProp.arraySize = interactors.Length;
            for (int i = 0; i < interactors.Length; i++)
            {
                farCastProp.GetArrayElementAtIndex(i).objectReferenceValue = interactors[i];
            }

            so.ApplyModifiedProperties();
            return canvasObj;
        }

        // ====================================================================
        // PANEL CONTROLES (CON ILUSTRACIONES 3D INTERACTIVAS)
        // ====================================================================
        private static GameObject BuildControlsPanel(Transform parent, TMP_FontAsset font, GameObject controllerModel)
        {
            RectTransform panel = CreateRect("Panel_Controls", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            TextMeshProUGUI title = AddText(panel, "Title", font, "Mandos y Controles de Conducción", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 0f, 60f);

            RectTransform cards = CreateRect("Cards", panel);
            cards.anchorMin = Vector2.zero;
            cards.anchorMax = Vector2.one;
            cards.offsetMin = Vector2.zero;
            cards.offsetMax = new Vector2(0f, -80f);
            var layout = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            BuildCard(cards, font, controllerModel, "Card_Grip", "Grip / Agarre", "Bumper", false,
                "Agarrar volante, palanca de cambios, freno de mano, guiños y espejos retrovisores.", 2800f, new Vector3(20f, -40f, 0f));
            BuildCard(cards, font, controllerModel, "Card_Trigger", "Gatillo / Trigger", "Trigger", false,
                "Acelerar y frenar el auto progresivamente. En menús, apuntar y presionar para seleccionar.", 2800f, new Vector3(20f, 40f, 0f));
            BuildCard(cards, font, controllerModel, "Card_Menu", "Menú / Pausa", "Button_Home", false,
                "Pausar la simulación y calibrar asiento en cualquier momento. En las lecciones, el botón A del control derecho oculta o muestra el panel del paso.", 2800f, new Vector3(-65f, 20f, 0f));

            return panel.gameObject;
        }

        private static void BuildCard(RectTransform parent, TMP_FontAsset font, GameObject controllerModel, string name,
            string caption, string pieceName, bool pressAnimation, string description, float modelScale, Vector3 modelEuler)
        {
            RectTransform card = CreateRect(name, parent);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = s_CardBgColor;
            bg.raycastTarget = false;

            TextMeshProUGUI captionText = AddText(card, "Caption", font, caption, 44f, new Color(1f, 0.85f, 0.2f), TextAlignmentOptions.Center);
            captionText.fontStyle = FontStyles.Bold;
            AnchorTop(captionText.rectTransform, 20f, 60f);

            if (!string.IsNullOrEmpty(description))
            {
                TextMeshProUGUI desc = AddText(card, "Description", font, description, 28f, Color.white, TextAlignmentOptions.Top);
                desc.textWrappingMode = TextWrappingModes.Normal;
                var descRect = desc.rectTransform;
                descRect.anchorMin = new Vector2(0f, 0f);
                descRect.anchorMax = new Vector2(1f, 0f);
                descRect.pivot = new Vector2(0.5f, 0f);
                descRect.sizeDelta = new Vector2(-40f, 140f);
                descRect.anchoredPosition = new Vector2(0f, 20f);
            }

            // Soporte de la ilustración 3D hacia el frente (z negativo) para que no quede detrás del fondo
            var holder = CreateRect("Illustration", card);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.55f);
            holder.sizeDelta = new Vector2(100f, 100f);
            holder.anchoredPosition3D = new Vector3(0f, 0f, -120f);

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

        // ====================================================================
        // PANEL LECCIONES
        // ====================================================================
        private static GameObject BuildLessonsPanel(Transform parent, TMP_FontAsset font, List<LessonDefinitionSO> lessons,
            out Transform container, out TextMeshProUGUI title, out TextMeshProUGUI desc,
            out Button btnStart, out TextMeshProUGUI btnStartText)
        {
            RectTransform panel = CreateRect("Panel_Lessons", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            // Columna Izquierda: Lista de lecciones
            RectTransform listCol = CreateRect("LessonListColumn", panel);
            listCol.anchorMin = new Vector2(0f, 0f);
            listCol.anchorMax = new Vector2(0.46f, 1f);
            listCol.offsetMin = Vector2.zero;
            listCol.offsetMax = new Vector2(-15f, 0f);

            RectTransform itemsBox = CreateRect("ItemsBox", listCol);
            Stretch(itemsBox, 0f, 0f, 0f, 0f);
            var layout = itemsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            container = itemsBox;

            for (int i = 0; i < lessons.Count; i++)
            {
                var l = lessons[i];
                CreateLessonCardButton($"LessonItem_{i}", itemsBox, font, l.lessonTitle, 110f);
            }

            // Columna Derecha: Detalle de la lección seleccionada
            RectTransform detailCol = CreateRect("LessonDetailColumn", panel);
            detailCol.anchorMin = new Vector2(0.48f, 0f);
            detailCol.anchorMax = new Vector2(1f, 1f);
            detailCol.offsetMin = new Vector2(15f, 0f);
            detailCol.offsetMax = Vector2.zero;

            RectTransform cardBox = CreateRect("DetailCard", detailCol);
            cardBox.anchorMin = new Vector2(0f, 0f);
            cardBox.anchorMax = new Vector2(1f, 1f);
            cardBox.offsetMin = new Vector2(0f, 130f);
            cardBox.offsetMax = Vector2.zero;
            Image cardBg = cardBox.gameObject.AddComponent<Image>();
            cardBg.color = s_CardBgColor;
            cardBg.raycastTarget = false;

            AddDetailStackLayout(cardBox);

            title = AddText(cardBox, "DetailTitle", font, "Calibración y Asiento", 42f, Color.white, TextAlignmentOptions.TopLeft);
            ConfigureDetailTitle(title);

            desc = AddText(cardBox, "DetailDesc", font, "Descripción de la lección formativa...", 30f, new Color(0.85f, 0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            ConfigureDetailDescription(desc);

            // Botón verde destacado inferior
            RectTransform actionBox = CreateRect("ActionBox", detailCol);
            actionBox.anchorMin = new Vector2(0f, 0f);
            actionBox.anchorMax = new Vector2(1f, 0f);
            actionBox.sizeDelta = new Vector2(0f, 105f);
            actionBox.anchoredPosition = new Vector2(0f, 52f);

            btnStart = CreateActionButton("Btn_StartLesson", actionBox, font, "COMENZAR LECCIÓN", s_GreenActionColor);
            btnStartText = btnStart.GetComponentInChildren<TextMeshProUGUI>();

            return panel.gameObject;
        }

        // ====================================================================
        // PANEL CIRCUITOS / INTEGRADORES
        // ====================================================================
        private static GameObject BuildCircuitsPanel(Transform parent, TMP_FontAsset font,
            out Transform container, out TextMeshProUGUI title, out TextMeshProUGUI desc,
            out Button btnStart, out TextMeshProUGUI btnStartText)
        {
            RectTransform panel = CreateRect("Panel_Circuits", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            // Columna Izquierda: Lista de circuitos
            RectTransform listCol = CreateRect("CircuitListColumn", panel);
            listCol.anchorMin = new Vector2(0f, 0f);
            listCol.anchorMax = new Vector2(0.46f, 1f);
            listCol.offsetMin = Vector2.zero;
            listCol.offsetMax = new Vector2(-15f, 0f);

            RectTransform itemsBox = CreateRect("ItemsBox", listCol);
            Stretch(itemsBox, 0f, 0f, 0f, 0f);
            var layout = itemsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            container = itemsBox;

            CreateLessonCardButton("CircuitItem_0", itemsBox, font, "Circuito Integrador 1: Tráfico y Señales", 125f);
            CreateLessonCardButton("CircuitItem_1", itemsBox, font, "Circuito Integrador 2: Maniobras y Parking", 125f);
            CreateLessonCardButton("CircuitItem_2", itemsBox, font, "Práctica Libre: Ciudad Completa", 125f);

            // Columna Derecha: Detalle del circuito
            RectTransform detailCol = CreateRect("CircuitDetailColumn", panel);
            detailCol.anchorMin = new Vector2(0.48f, 0f);
            detailCol.anchorMax = new Vector2(1f, 1f);
            detailCol.offsetMin = new Vector2(15f, 0f);
            detailCol.offsetMax = Vector2.zero;

            RectTransform cardBox = CreateRect("DetailCard", detailCol);
            cardBox.anchorMin = new Vector2(0f, 0f);
            cardBox.anchorMax = new Vector2(1f, 1f);
            cardBox.offsetMin = new Vector2(0f, 130f);
            cardBox.offsetMax = Vector2.zero;
            Image cardBg = cardBox.gameObject.AddComponent<Image>();
            cardBg.color = s_CardBgColor;
            cardBg.raycastTarget = false;

            AddDetailStackLayout(cardBox);

            title = AddText(cardBox, "DetailTitle", font, "Circuito Integrador", 42f, Color.white, TextAlignmentOptions.TopLeft);
            ConfigureDetailTitle(title);

            desc = AddText(cardBox, "DetailDesc", font, "Descripción del circuito de práctica...", 30f, new Color(0.85f, 0.88f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            ConfigureDetailDescription(desc);

            // Botón verde destacado inferior
            RectTransform actionBox = CreateRect("ActionBox", detailCol);
            actionBox.anchorMin = new Vector2(0f, 0f);
            actionBox.anchorMax = new Vector2(1f, 0f);
            actionBox.sizeDelta = new Vector2(0f, 105f);
            actionBox.anchoredPosition = new Vector2(0f, 52f);

            btnStart = CreateActionButton("Btn_StartCircuit", actionBox, font, "INICIAR RECORRIDO", s_GreenActionColor);
            btnStartText = btnStart.GetComponentInChildren<TextMeshProUGUI>();

            return panel.gameObject;
        }

        // ====================================================================
        // PANEL CALIBRACIÓN
        // ====================================================================
        private static GameObject BuildCalibrationPanel(Transform parent, TMP_FontAsset font)
        {
            RectTransform panel = CreateRect("Panel_Calibration", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            TextMeshProUGUI title = AddText(panel, "Title", font, "Calibración del Puesto de Conducción", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 0f, 60f);

            RectTransform card = CreateRect("Card_CalibInfo", panel);
            card.anchorMin = Vector2.zero;
            card.anchorMax = Vector2.one;
            card.offsetMin = Vector2.zero;
            card.offsetMax = new Vector2(0f, -80f);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = s_CardBgColor;
            bg.raycastTarget = false;

            string infoText = "<b>Ajuste de Postura y Altura del Asiento:</b>\n\n" +
                              "Para garantizar que alcances el volante, palanca y pedales con máxima comodidad y ergonomía:\n\n" +
                              "• <b>Durante las lecciones:</b> Podés presionar en cualquier momento el botón <b>Menú</b> de tu control izquierdo " +
                              "(o tecla <b>Escape</b>) para abrir el menú de pausa in-game y calibrar en vivo tu altura, distancia y alineación con respecto al volante.\n\n" +
                              "• <b>Recentrado de visor:</b> Si sentís que la orientación principal está desplazada, mantené pulsado el botón Oculus/Meta en el control derecho para recentrar el campo visual.\n\n" +
                              "• <b>Configuración guardada:</b> Tu calibración se guarda automáticamente entre lecciones y se aplica al subirte al vehículo.";

            TextMeshProUGUI bodyText = AddText(card, "Body", font, infoText, 32f, new Color(0.88f, 0.90f, 0.96f, 1f), TextAlignmentOptions.TopLeft);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            Stretch(bodyText.rectTransform, 40f, 40f, 40f, 40f);

            return panel.gameObject;
        }

        // ====================================================================
        // PANEL CRÉDITOS
        // ====================================================================
        private static GameObject BuildCreditsPanel(Transform parent, TMP_FontAsset font)
        {
            RectTransform panel = CreateRect("Panel_Credits", parent);
            Stretch(panel, 25f, 25f, 25f, 25f);

            TextMeshProUGUI title = AddText(panel, "Title", font, "Acerca de Primer Volante", 42f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 0f, 60f);

            RectTransform card = CreateRect("Card_Credits", panel);
            card.anchorMin = Vector2.zero;
            card.anchorMax = Vector2.one;
            card.offsetMin = Vector2.zero;
            card.offsetMax = new Vector2(0f, -80f);
            Image bg = card.gameObject.AddComponent<Image>();
            bg.color = s_CardBgColor;
            bg.raycastTarget = false;

            string creditBody = "<b>Primer Volante</b> es una plataforma inmersiva de entrenamiento para nuevos conductores basada en Realidad Virtual.\n\n" +
                                "<b>Objetivo Pedagógico:</b>\n" +
                                "Proveer un entorno seguro, progresivo y realista para que los estudiantes adquieran reflejos, automatismos en los mandos del vehículo, nociones de prioridad de paso, lectura de señalización vial y respeto por las normas de tránsito.\n\n" +
                                "<b>Características del Simulador:</b>\n" +
                                "• Física de vehículo calibrada y mandos de cabina bimanuales con agarre VR.\n" +
                                "• Sistema de detección y penalización de infracciones viales en tiempo real.\n" +
                                "• Currículum de 6 lecciones guiadas por voz y HUD visual.\n" +
                                "• Sistema de navegación GPS dinámico en entorno urbano completo.\n\n" +
                                "<i>Versión: 2.1 (Menú VR Unificado y Accesible)</i>";

            TextMeshProUGUI bodyText = AddText(card, "Body", font, creditBody, 30f, new Color(0.88f, 0.90f, 0.96f, 1f), TextAlignmentOptions.TopLeft);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            Stretch(bodyText.rectTransform, 40f, 40f, 40f, 40f);

            return panel.gameObject;
        }

        // ====================================================================
        // MODAL DE SALIDA (CENTRO, ELEGANTE, SIN BUGS VISUALES)
        // ====================================================================
        private static GameObject BuildExitModal(Transform parent, TMP_FontAsset font, out Button btnConfirm, out Button btnCancel)
        {
            RectTransform modalRoot = CreateRect("Modal_ExitConfirmation", parent);
            Stretch(modalRoot, 0f, 0f, 0f, 0f);
            Image backdrop = modalRoot.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.03f, 0.04f, 0.07f, 0.88f);
            backdrop.raycastTarget = true; // El backdrop bloquea clicks hacia el fondo mientras el modal está abierto

            RectTransform box = CreateRect("ConfirmBox", modalRoot);
            box.anchorMin = new Vector2(0.5f, 0.5f);
            box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(850f, 440f);
            box.anchoredPosition = Vector2.zero;
            Image boxBg = box.gameObject.AddComponent<Image>();
            boxBg.color = new Color(0.12f, 0.15f, 0.22f, 1f);
            boxBg.raycastTarget = false;

            TextMeshProUGUI title = AddText(box, "Title", font, "¿Salir del Simulador?", 46f, Color.white, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            AnchorTop(title.rectTransform, 45f, 65f);

            TextMeshProUGUI sub = AddText(box, "Subtitle", font, "Se cerrará la aplicación y volverás a la pantalla principal de tu visor.", 28f, new Color(0.75f, 0.80f, 0.90f, 1f), TextAlignmentOptions.Center);
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.rectTransform.anchorMin = new Vector2(0.08f, 0.5f);
            sub.rectTransform.anchorMax = new Vector2(0.92f, 0.5f);
            sub.rectTransform.sizeDelta = new Vector2(0f, 60f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, 15f);

            RectTransform btnBox = CreateRect("ButtonRow", box);
            btnBox.anchorMin = new Vector2(0f, 0f);
            btnBox.anchorMax = new Vector2(1f, 0f);
            btnBox.pivot = new Vector2(0.5f, 0f);
            btnBox.sizeDelta = new Vector2(0f, 110f);
            btnBox.anchoredPosition = new Vector2(0f, 35f);
            var layout = btnBox.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 35f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            btnCancel = CreateModalButton("Btn_Cancel", btnBox, font, "CANCELAR", new Color(0.22f, 0.26f, 0.36f, 1f), 300f, 75f);
            btnConfirm = CreateModalButton("Btn_Confirm", btnBox, font, "SALIR", s_ExitRedColor, 300f, 75f);

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

        private static Image AddImage(Transform parent, string name, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        /// <summary>
        /// Apila título y descripción en la tarjeta de detalle: el título hace wrap y empuja la descripción hacia abajo
        /// en vez de tener una altura fija que, con títulos largos, desbordaba sobre el texto inferior.
        /// </summary>
        private static void AddDetailStackLayout(RectTransform cardBox)
        {
            var layout = cardBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(35, 35, 25, 30);
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void ConfigureDetailTitle(TextMeshProUGUI title)
        {
            title.fontStyle = FontStyles.Bold;
            title.textWrappingMode = TextWrappingModes.Normal;
            title.overflowMode = TextOverflowModes.Overflow;
            title.gameObject.AddComponent<LayoutElement>().flexibleHeight = 0f;
        }

        private static void ConfigureDetailDescription(TextMeshProUGUI desc)
        {
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
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
            tmp.raycastTarget = false; // Siempre false para no absorber eventos del botón
            return tmp;
        }

        private static Button CreateMenuButton(string name, Transform parent, TMP_FontAsset font, string text, Color bgColor, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(0f, height);

            var img = go.GetComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = new Color(0.25f, 0.48f, 0.95f, 1f);
            colors.pressedColor = new Color(0.18f, 0.35f, 0.80f, 1f);
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 30f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform, 30f, 20f, 10f, 10f);

            return btn;
        }

        private static Button CreateExitButton(string name, Transform parent, TMP_FontAsset font, string text, Color bgColor, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(25f, 25f);
            rect.offsetMax = new Vector2(-25f, 25f + height);

            var img = go.GetComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = new Color(0.98f, 0.28f, 0.28f, 1f);
            colors.pressedColor = new Color(0.65f, 0.10f, 0.10f, 1f);
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 32f, Color.white, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(label.rectTransform, 15f, 15f, 5f, 5f);

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
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = s_CardBgColor;
            colors.highlightedColor = new Color(0.18f, 0.32f, 0.58f, 1f);
            colors.pressedColor = new Color(0.12f, 0.22f, 0.42f, 1f);
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
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.20f;
            colors.pressedColor = bgColor * 0.80f;
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 36f, Color.white, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform, 10f, 10f, 5f, 5f);

            return btn;
        }

        private static Button CreateModalButton(string name, Transform parent, TMP_FontAsset font, string text, Color bgColor, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);

            var img = go.GetComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.80f;
            btn.colors = colors;

            var label = AddText(go.transform, "Text", font, text, 32f, Color.white, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform, 10f, 10f, 5f, 5f);

            return btn;
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
