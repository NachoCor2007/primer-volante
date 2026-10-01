using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para construir el menú in-game (Canvas World Space) con calibración
    /// de la posición de la vista del conductor, en la escena CockpitIntegrationScene.
    /// Idempotente: puede ejecutarse repetidamente sin duplicar objetos.
    /// </summary>
    public static class InGameMenuSetup
    {
        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Tools/Primer Volante/Setup In-Game Menu (Cockpit Integration Scene)")]
        public static void SetupMenu()
        {
            Debug.Log($"[InGameMenuSetup] 🧩 Configurando menú in-game en: {CockpitIntegrationSetup.SCENE_INTEGRATION_PATH}...");

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != CockpitIntegrationSetup.SCENE_INTEGRATION_PATH)
            {
                scene = EditorSceneManager.OpenScene(CockpitIntegrationSetup.SCENE_INTEGRATION_PATH, OpenSceneMode.Single);
            }

            EnsureEventSystem();

            GameObject xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
            DriverSeatFollower follower = xrOriginObj != null
                ? xrOriginObj.GetComponent<DriverSeatFollower>()
                : Object.FindAnyObjectByType<DriverSeatFollower>();
            Transform headTransform = FindHeadTransform(xrOriginObj);

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            GameObject canvasRoot = BuildMenuCanvas(
                fontAsset,
                out GameObject mainPanel,
                out GameObject calibrationPanel,
                out Slider lateralSlider,
                out Slider heightSlider,
                out Slider depthSlider,
                out Button resumeButton,
                out Button calibrateButton,
                out Button resetButton,
                out Button backButton);

            // Limpieza de una versión anterior que agregaba el componente sobre el propio Canvas.
            InGameMenuController staleController = canvasRoot.GetComponent<InGameMenuController>();
            if (staleController != null) Object.DestroyImmediate(staleController);

            // InGameMenuController: gestiona apertura/cierre y posicionamiento frente a la cabeza.
            // Vive en un GameObject SEPARADO del Canvas (que se activa/desactiva): si estuviera en
            // el propio m_MenuRoot, Awake() lo desactivaría a sí mismo y Update() dejaría de correr.
            GameObject controllerObj = GameObject.Find("InGameMenuController");
            if (controllerObj == null)
            {
                controllerObj = new GameObject("InGameMenuController");
            }
            InGameMenuController menuController = controllerObj.GetComponent<InGameMenuController>();
            if (menuController == null) menuController = controllerObj.AddComponent<InGameMenuController>();

            NearFarInteractor[] farCastInteractors = xrOriginObj != null
                ? xrOriginObj.GetComponentsInChildren<NearFarInteractor>(true)
                : System.Array.Empty<NearFarInteractor>();

            SerializedObject soMenu = new SerializedObject(menuController);
            soMenu.Update();
            soMenu.FindProperty("m_MenuRoot").objectReferenceValue = canvasRoot;
            soMenu.FindProperty("m_MainPanel").objectReferenceValue = mainPanel;
            soMenu.FindProperty("m_CalibrationPanel").objectReferenceValue = calibrationPanel;
            soMenu.FindProperty("m_HeadTransform").objectReferenceValue = headTransform;
            soMenu.FindProperty("m_DriverSeatFollower").objectReferenceValue = follower;

            SerializedProperty farCastProp = soMenu.FindProperty("m_FarCastInteractors");
            farCastProp.arraySize = farCastInteractors.Length;
            for (int i = 0; i < farCastInteractors.Length; i++)
            {
                farCastProp.GetArrayElementAtIndex(i).objectReferenceValue = farCastInteractors[i];
            }

            soMenu.ApplyModifiedProperties();

            // DriverViewCalibrationUI: conecta los sliders con DriverSeatFollower.
            DriverViewCalibrationUI calibrationUI = calibrationPanel.GetComponent<DriverViewCalibrationUI>();
            if (calibrationUI == null) calibrationUI = calibrationPanel.AddComponent<DriverViewCalibrationUI>();

            SerializedObject soCalib = new SerializedObject(calibrationUI);
            soCalib.Update();
            soCalib.FindProperty("m_DriverSeatFollower").objectReferenceValue = follower;
            soCalib.FindProperty("m_LateralSlider").objectReferenceValue = lateralSlider;
            soCalib.FindProperty("m_HeightSlider").objectReferenceValue = heightSlider;
            soCalib.FindProperty("m_DepthSlider").objectReferenceValue = depthSlider;
            soCalib.ApplyModifiedProperties();

            // Cableado de botones (idempotente: se limpian listeners previos antes de agregar).
            WireButtonClick(resumeButton, menuController, nameof(InGameMenuController.CloseMenu));
            WireButtonClick(calibrateButton, menuController, nameof(InGameMenuController.ShowCalibrationPanel));
            WireButtonClick(backButton, menuController, nameof(InGameMenuController.ShowMainPanel));
            WireButtonClick(resetButton, calibrationUI, nameof(DriverViewCalibrationUI.ResetCalibration));

            EditorUtility.SetDirty(menuController);
            EditorUtility.SetDirty(calibrationUI);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[InGameMenuSetup] ✅ ¡Menú in-game configurado y guardado exitosamente! " +
                      "El botón Menu del control izquierdo (Meta Quest Touch Plus / Quest 3S, leído vía " +
                      "UnityEngine.XR.InputDevices) y la tecla Escape ya abren/cierran el menú.");
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>();
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
                var inputSystemUIModule = eventSystemObj.GetComponent<InputSystemUIInputModule>();
                if (inputSystemUIModule != null) Object.DestroyImmediate(inputSystemUIModule);

                var standaloneModule = eventSystemObj.GetComponent<StandaloneInputModule>();
                if (standaloneModule != null) Object.DestroyImmediate(standaloneModule);

                eventSystemObj.AddComponent<XRUIInputModule>();
            }
        }

        private static Transform FindHeadTransform(GameObject xrOriginObj)
        {
            if (xrOriginObj == null) return null;

            Transform cameraOffset = xrOriginObj.transform.Find("Camera Offset");
            if (cameraOffset != null)
            {
                Transform mainCamera = cameraOffset.Find("Main Camera");
                if (mainCamera != null) return mainCamera;
            }

            Camera cam = xrOriginObj.GetComponentInChildren<Camera>();
            return cam != null ? cam.transform : null;
        }

        private static GameObject BuildMenuCanvas(
            TMP_FontAsset fontAsset,
            out GameObject mainPanel,
            out GameObject calibrationPanel,
            out Slider lateralSlider,
            out Slider heightSlider,
            out Slider depthSlider,
            out Button resumeButton,
            out Button calibrateButton,
            out Button resetButton,
            out Button backButton)
        {
            GameObject canvasRoot = GameObject.Find("InGameMenu_Canvas");
            if (canvasRoot == null)
            {
                canvasRoot = new GameObject("InGameMenu_Canvas");
            }
            else
            {
                for (int i = canvasRoot.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(canvasRoot.transform.GetChild(i).gameObject);
                }
            }

            RectTransform canvasRect = canvasRoot.GetComponent<RectTransform>();
            if (canvasRect == null) canvasRect = canvasRoot.AddComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(600f, 420f);
            canvasRoot.transform.localScale = Vector3.one * 0.001f;

            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;

            GraphicRaycaster obsoleteRaycaster = canvasRoot.GetComponent<GraphicRaycaster>();
            if (obsoleteRaycaster != null) Object.DestroyImmediate(obsoleteRaycaster);

            if (canvasRoot.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                canvasRoot.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            // Panel principal
            mainPanel = CreatePanel("MainPanel", canvasRoot.transform);
            CreateLabel("Title", mainPanel.transform, fontAsset, "Menú", 36f, new Vector2(0f, -40f), new Vector2(400f, 50f));
            resumeButton = CreateButton("Button_Resume", mainPanel.transform, fontAsset, "Reanudar", new Vector2(0f, -150f), new Vector2(300f, 60f));
            calibrateButton = CreateButton("Button_Calibrate", mainPanel.transform, fontAsset, "Calibrar posición", new Vector2(0f, -230f), new Vector2(300f, 60f));

            // Panel de calibración
            calibrationPanel = CreatePanel("CalibrationPanel", canvasRoot.transform);
            calibrationPanel.SetActive(false);
            CreateLabel("Title", calibrationPanel.transform, fontAsset, "Calibrar posición", 32f, new Vector2(0f, -30f), new Vector2(500f, 50f));

            CreateLabel("Label_Lateral", calibrationPanel.transform, fontAsset, "Izquierda / Derecha", 20f, new Vector2(0f, -90f), new Vector2(500f, 30f));
            lateralSlider = CreateSlider("Slider_Lateral", calibrationPanel.transform, new Vector2(0f, -120f), new Vector2(500f, 20f));

            CreateLabel("Label_Height", calibrationPanel.transform, fontAsset, "Altura", 20f, new Vector2(0f, -160f), new Vector2(500f, 30f));
            heightSlider = CreateSlider("Slider_Height", calibrationPanel.transform, new Vector2(0f, -190f), new Vector2(500f, 20f));

            CreateLabel("Label_Depth", calibrationPanel.transform, fontAsset, "Adelante / Atrás", 20f, new Vector2(0f, -230f), new Vector2(500f, 30f));
            depthSlider = CreateSlider("Slider_Depth", calibrationPanel.transform, new Vector2(0f, -260f), new Vector2(500f, 20f));

            resetButton = CreateButton("Button_Reset", calibrationPanel.transform, fontAsset, "Restablecer", new Vector2(-160f, -340f), new Vector2(260f, 55f));
            backButton = CreateButton("Button_Back", calibrationPanel.transform, fontAsset, "Volver", new Vector2(160f, -340f), new Vector2(260f, 55f));

            return canvasRoot;
        }

        private static GameObject CreatePanel(string name, Transform parent)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

            return panel;
        }

        private static TextMeshProUGUI CreateLabel(string name, Transform parent, TMP_FontAsset fontAsset, string text, float fontSize, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject labelObj = new GameObject(name);
            labelObj.transform.SetParent(parent, false);

            RectTransform rect = labelObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) label.font = fontAsset;
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            return label;
        }

        private static Button CreateButton(string name, Transform parent, TMP_FontAsset fontAsset, string text, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent, false);

            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image bg = buttonObj.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.25f, 1f);

            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = bg;

            TextMeshProUGUI label = CreateLabel("Label", buttonObj.transform, fontAsset, text, 24f, Vector2.zero, sizeDelta);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.anchoredPosition = Vector2.zero;

            return button;
        }

        private static Slider CreateSlider(string name, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject sliderObj = new GameObject(name);
            sliderObj.transform.SetParent(parent, false);

            RectTransform rect = sliderObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            // Background
            GameObject background = new GameObject("Background");
            background.transform.SetParent(sliderObj.transform, false);
            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(0.15f, 0.15f, 0.18f, 1f);

            // Fill Area / Fill
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRect.offsetMin = new Vector2(5f, 0f);
            fillAreaRect.offsetMax = new Vector2(-5f, 0f);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.5f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.25f, 0.65f, 1f, 1f);

            // Handle Slide Area / Handle
            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handle.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(20f, 0f);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = -1f;
            slider.maxValue = 1f;
            slider.value = 0f;

            return slider;
        }

        private static void WireButtonClick(Button button, Object target, string methodName)
        {
            if (button == null || target == null) return;

            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            var method = System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), target, methodName) as UnityEngine.Events.UnityAction;
            UnityEventTools.AddVoidPersistentListener(button.onClick, method);
            EditorUtility.SetDirty(button);
        }
    }
}
