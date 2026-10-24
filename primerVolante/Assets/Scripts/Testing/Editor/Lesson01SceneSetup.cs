using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para configurar la arquitectura de lecciones
    /// y la Lección 1 ("Calibración del ambiente de manejo") para el proyecto "Primer Volante" (TASK-204 / Issue #19).
    /// Clona de forma segura CityMerged_ScriptedEvents.unity a Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity,
    /// genera el asset LessonData_01.asset y ensambla el panel HUD sobre el volante de Car08.
    /// </summary>
    public static class Lesson01SceneSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged_ScriptedEvents.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string LESSON_DATA_DIR = "Assets/Scripts/Lessons";
        public const string STEPS_DATA_DIR = "Assets/Scripts/Lessons/Steps";
        public const string LESSON_DATA_PATH = "Assets/Scripts/Lessons/LessonData_01.asset";
        public const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 1 Scene")]
        public static void SetupLesson01()
        {
            Debug.Log("=========================================================");
            Debug.Log("🚀 [Lesson01SceneSetup] Iniciando configuración de Lección 1...");
            Debug.Log("=========================================================");

            // 1. Asegurar directorios
            EnsureDirectories();

            // 2. Crear o actualizar el asset ScriptableObject de la Lección 1
            LessonDefinitionSO lessonData = CreateOrUpdateLessonData();

            // 3. Asegurar la escena clonada
            EnsureClonedSceneExists();

            // 4. Abrir la escena de lección
            Scene scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson01SceneSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 5. Configurar Car08 y sus componentes de cabina
            GameObject car08 = FindPlayerVehicle(scene);
            if (car08 == null)
            {
                Debug.LogError("[Lesson01SceneSetup] ❌ No se encontró el vehículo Car08 en la escena.");
                return;
            }

            // Asegurar que Car08 tenga el ensamble de retrovisores
            VRRearviewMirror mirror = car08.GetComponentInChildren<VRRearviewMirror>(true);
            if (mirror == null)
            {
                Debug.Log("[Lesson01SceneSetup] 🪞 Ensamblando retrovisores en Car08 con VehicleMirrorSetupEditor...");
                VehicleMirrorSetupEditor.ConfigureMirrorsOnCar(car08);
            }

            CockpitControlLockManager lockManager = car08.GetComponent<CockpitControlLockManager>();
            if (lockManager == null)
            {
                lockManager = car08.AddComponent<CockpitControlLockManager>();
            }
            lockManager.AutoDiscoverControls(car08.transform);
            EditorUtility.SetDirty(lockManager);

            InteractableHighlighter highlighter = car08.GetComponent<InteractableHighlighter>();
            if (highlighter == null)
            {
                highlighter = car08.AddComponent<InteractableHighlighter>();
            }
            EditorUtility.SetDirty(highlighter);

            // 6. Asegurar InGameMenu y EventSystem con XRUIInputModule en la escena
            InGameMenuSetup.EnsureInGameMenuInActiveScene();

            // 7. Configurar o crear el LessonHUDPanel sobre el volante
            LessonHUDPanel hudPanel = SetupLessonHUD(car08);

            // 8. Configurar el LessonManager en la escena
            SetupLessonManager(scene, car08, lessonData, lockManager, highlighter, hudPanel);

            // 9. Guardar escena y assets
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("=========================================================");
            Debug.Log("✅ [Lesson01SceneSetup] ¡Escena y componentes de Lección 1 configurados exitosamente!");
            Debug.Log($"   Escena: {TARGET_SCENE_PATH}");
            Debug.Log($"   Asset:  {LESSON_DATA_PATH}");
            Debug.Log("=========================================================");
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
            if (!AssetDatabase.IsValidFolder(TARGET_SCENE_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scenes", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder(LESSON_DATA_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder(STEPS_DATA_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Steps");
            }
        }

        private static void EnsureClonedSceneExists()
        {
            var targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TARGET_SCENE_PATH);
            if (targetScene == null)
            {
                var originalScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ORIGINAL_SCENE_PATH);
                if (originalScene == null)
                {
                    Debug.LogError($"[Lesson01SceneSetup] ❌ No se encontró la escena original en {ORIGINAL_SCENE_PATH}");
                    return;
                }

                bool copied = AssetDatabase.CopyAsset(ORIGINAL_SCENE_PATH, TARGET_SCENE_PATH);
                AssetDatabase.Refresh();

                if (copied)
                {
                    Debug.Log($"[Lesson01SceneSetup] 📋 Escena clonada exitosamente desde {ORIGINAL_SCENE_PATH} a {TARGET_SCENE_PATH}");
                }
                else
                {
                    Debug.LogError($"[Lesson01SceneSetup] ❌ Falló la clonación de la escena a {TARGET_SCENE_PATH}");
                }
            }
        }

        private static LessonStepSO CreateOrUpdateStep(string fileName, string title, string instruction,
            LessonStepCompletionMode mode, CockpitControlFlags allowedControls, string highlightTarget)
        {
            string path = $"{STEPS_DATA_DIR}/{fileName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            bool isNew = false;
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                isNew = true;
            }

            if (isNew)
            {
                step.stepTitle = title;
                step.instructionText = instruction;
                step.completionMode = mode;
                step.allowedControls = allowedControls;
                step.highlightTargetName = highlightTarget;
                AssetDatabase.CreateAsset(step, path);
            }
            else
            {
                // Preservar textos existentes actualizados en commit 80c0d1b
                if (string.IsNullOrEmpty(step.stepTitle))
                {
                    step.stepTitle = title;
                }
                if (string.IsNullOrEmpty(step.instructionText) || step.instructionText.Contains("con ambas manos"))
                {
                    step.instructionText = instruction;
                }
                step.completionMode = mode;
                step.allowedControls = allowedControls;
                step.highlightTargetName = highlightTarget;
                EditorUtility.SetDirty(step);
            }

            return step;
        }

        public static LessonDefinitionSO CreateOrUpdateLessonData()
        {
            EnsureDirectories();

            // Paso 1: Asiento y Postura (commit 80c0d1b)
            LessonStepSO step1 = CreateOrUpdateStep("Lesson01_Step01_SeatCalibration",
                "Ajuste de Asiento y Postura",
                "Para ajustar tu postura, altura y distancia del asiento, presiona el botón Menu en tu control izquierdo para abrir el menú de calibración.\nUna vez ajustado a tu comodidad, o si la posición inicial ya es óptima, apunta con el rayo y presiona 'Continuar'.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.SteeringWheel,
                "");

            // Paso 2: Volante (commit 80c0d1b)
            LessonStepSO step2 = CreateOrUpdateStep("Lesson01_Step02_SteeringWheelCheck",
                "Comprobación del Volante",
                "Toma el volante presionando el grip de cualquier controlador y gíralo suavemente hacia ambos lados para familiarizarte con su recorrido y verificar que tus brazos tengan la flexión adecuada.\nPresiona 'Continuar' cuando estés listo.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.SteeringWheel,
                "SteeringWheel_Pivot");

            // Paso 3: Espejos (resaltado multiobjetivo de 3 espejos)
            LessonStepSO step3 = CreateOrUpdateStep("Lesson01_Step03_MirrorsCheck",
                "Visibilidad de Espejos y Entorno",
                "Comprueba los 3 espejos retrovisores resaltados en amarillo (central y laterales). Debes observar el tráfico posterior sin desviar en exceso la cabeza.\nPresiona 'Finalizar Calibración' para concluir la lección.",
                LessonStepCompletionMode.ManualContinue,
                CockpitControlFlags.SteeringWheel,
                "Mirrors");

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_DATA_PATH);
            bool isNew = false;

            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                isNew = true;
            }

            lesson.lessonId = "LESSON_01";
            lesson.lessonTitle = "Lección 1: Calibración del Ambiente de Manejo";
            lesson.steps.Clear();
            lesson.steps.Add(step1);
            lesson.steps.Add(step2);
            lesson.steps.Add(step3);

            if (isNew)
            {
                AssetDatabase.CreateAsset(lesson, LESSON_DATA_PATH);
            }
            else
            {
                EditorUtility.SetDirty(lesson);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Lesson01SceneSetup] 📄 Asset de lección generado en {LESSON_DATA_PATH} con {lesson.steps.Count} pasos.");
            return lesson;
        }

        private static GameObject FindPlayerVehicle(Scene scene)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            foreach (GameObject root in rootObjects)
            {
                if (root.name == "Car08") return root;
                VehicleController ctrl = root.GetComponentInChildren<VehicleController>(true);
                if (ctrl != null) return ctrl.gameObject;
            }
            return null;
        }

        private static LessonHUDPanel SetupLessonHUD(GameObject car08)
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            // Buscar volante para posicionar el HUD relativo a él
            VRSteeringWheel wheel = car08.GetComponentInChildren<VRSteeringWheel>(true);
            Transform wheelPivot = wheel != null ? wheel.transform : car08.transform.Find("mdl_car04_body/SteeringWheel_Pivot");

            Transform parentBody = (wheelPivot != null && wheelPivot.parent != null) ? wheelPivot.parent : car08.transform;

            // Verificar si ya existe el HUD
            Transform existingHUD = parentBody.Find("LessonHUD_Canvas");
            if (existingHUD != null)
            {
                UnityEngine.Object.DestroyImmediate(existingHUD.gameObject);
            }

            // 1. Crear Canvas raíz
            GameObject canvasObj = new GameObject("LessonHUD_Canvas");
            canvasObj.transform.SetParent(parentBody, false);

            Vector3 wheelLocalPos = wheelPivot != null ? wheelPivot.localPosition : new Vector3(-0.298f, 0.944f, 0.13f);
            // Posicionado directamente arriba del aro del volante (aprox Y: +0.22m por encima del centro, +0.08m adelantado en Z)
            canvasObj.transform.localPosition = new Vector3(wheelLocalPos.x, wheelLocalPos.y + 0.22f, wheelLocalPos.z + 0.08f);
            // Ligera inclinación hacia la vista del conductor
            canvasObj.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            canvasObj.transform.localScale = Vector3.one * 0.00075f;

            RectTransform canvasRect = canvasObj.AddComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(600f, 260f);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            GameObject xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
            Transform headTransform = FindHeadTransform(xrOriginObj);
            Camera headCam = headTransform != null ? headTransform.GetComponent<Camera>() : Camera.main;
            if (headCam != null)
            {
                canvas.worldCamera = headCam;
            }

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();

            LessonHUDPanel hudPanel = canvasObj.AddComponent<LessonHUDPanel>();

            // 2. Fondo general
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.08f, 0.10f, 0.14f, 0.94f);

            // Borde superior decorativo
            GameObject headerBar = new GameObject("HeaderBar");
            headerBar.transform.SetParent(bgObj.transform, false);
            RectTransform hbRect = headerBar.AddComponent<RectTransform>();
            hbRect.anchorMin = new Vector2(0f, 1f);
            hbRect.anchorMax = new Vector2(1f, 1f);
            hbRect.pivot = new Vector2(0.5f, 1f);
            hbRect.anchoredPosition = Vector2.zero;
            hbRect.sizeDelta = new Vector2(0f, 6f);
            Image hbImage = headerBar.AddComponent<Image>();
            hbImage.color = new Color(0.2f, 0.65f, 1f, 1f);

            // 3. Panel de Contenido de Pasos
            GameObject stepContent = new GameObject("StepContentPanel");
            stepContent.transform.SetParent(canvasObj.transform, false);
            RectTransform scRect = stepContent.AddComponent<RectTransform>();
            scRect.anchorMin = Vector2.zero;
            scRect.anchorMax = Vector2.one;
            scRect.offsetMin = Vector2.zero;
            scRect.offsetMax = Vector2.zero;

            // Título de la lección
            TextMeshProUGUI titleText = CreateText("LessonTitle", stepContent.transform, fontAsset,
                "Lección 1: Calibración del Ambiente de Manejo", 22f, FontStyles.Bold,
                new Vector2(0f, -24f), new Vector2(560f, 30f), new Color(0.35f, 0.75f, 1f, 1f));

            // Indicador de progreso del paso
            TextMeshProUGUI progressText = CreateText("StepProgress", stepContent.transform, fontAsset,
                "Paso 1 de 3: Ajuste de Asiento y Postura", 17f, FontStyles.Normal,
                new Vector2(0f, -54f), new Vector2(560f, 24f), new Color(0.85f, 0.85f, 0.90f, 1f));

            // Instrucción detallada
            TextMeshProUGUI instructionText = CreateText("InstructionText", stepContent.transform, fontAsset,
                "Para ajustar tu postura, altura y distancia del asiento, presiona el botón Menu en tu control izquierdo (o tecla Escape) para abrir el menú de calibración.\n\nUna vez ajustado a tu comodidad, o si la posición inicial ya es óptima, apunta con el rayo y presiona 'Continuar'.",
                15f, FontStyles.Normal, new Vector2(0f, -118f), new Vector2(560f, 85f), Color.white);
            instructionText.textWrappingMode = TextWrappingModes.Normal;

            // Botón Continuar / Confirmar Posición
            Button continueBtn = CreateButton("Button_Continue", stepContent.transform, fontAsset, "Continuar",
                new Vector2(0f, -198f), new Vector2(280f, 46f), new Color(0.12f, 0.55f, 0.40f, 1f));
            TextMeshProUGUI continueBtnText = continueBtn.GetComponentInChildren<TextMeshProUGUI>();

            // 4. Subpanel de Felicitaciones / Fin de Lección (oculto inicialmente)
            GameObject completionPanel = new GameObject("CompletionPanel");
            completionPanel.transform.SetParent(canvasObj.transform, false);
            RectTransform cpRect = completionPanel.AddComponent<RectTransform>();
            cpRect.anchorMin = Vector2.zero;
            cpRect.anchorMax = Vector2.one;
            cpRect.offsetMin = Vector2.zero;
            cpRect.offsetMax = Vector2.zero;

            TextMeshProUGUI compTitle = CreateText("CompletionTitle", completionPanel.transform, fontAsset,
                "¡Excelente Trabajo!", 26f, FontStyles.Bold,
                new Vector2(0f, -50f), new Vector2(560f, 40f), new Color(0.3f, 0.95f, 0.55f, 1f));

            TextMeshProUGUI compDesc = CreateText("CompletionDesc", completionPanel.transform, fontAsset,
                "Has completado con éxito la Lección 1.\nTu ambiente de manejo ha quedado calibrado correctamente.",
                15f, FontStyles.Normal,
                new Vector2(0f, -115f), new Vector2(540f, 70f), Color.white);
            compDesc.textWrappingMode = TextWrappingModes.Normal;

            Button restartBtn = CreateButton("Button_Restart", completionPanel.transform, fontAsset, "Repetir Lección",
                new Vector2(-135f, -195f), new Vector2(240f, 46f), new Color(0.25f, 0.45f, 0.70f, 1f));

            Button nextBtn = CreateButton("Button_NextLesson", completionPanel.transform, fontAsset, "Siguiente Lección",
                new Vector2(135f, -195f), new Vector2(240f, 46f), new Color(0.15f, 0.65f, 0.35f, 1f));
            TextMeshProUGUI nextBtnText = nextBtn.GetComponentInChildren<TextMeshProUGUI>();

            completionPanel.SetActive(false);

            // 5. Asignar capa UI (Layer 5) a todos los elementos del HUD
            int uiLayer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 5;
            SetLayerRecursively(canvasObj, uiLayer);

            // 6. Detectar NearFarInteractors del jugador
            NearFarInteractor[] farCastInteractors = xrOriginObj != null
                ? xrOriginObj.GetComponentsInChildren<NearFarInteractor>(true)
                : UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);

            // 7. Cablear referencias en LessonHUDPanel mediante SerializedObject
            var so = new SerializedObject(hudPanel);
            so.FindProperty("m_LessonTitleText").objectReferenceValue = titleText;
            so.FindProperty("m_StepProgressText").objectReferenceValue = progressText;
            so.FindProperty("m_InstructionText").objectReferenceValue = instructionText;
            so.FindProperty("m_StepContentPanel").objectReferenceValue = stepContent;
            so.FindProperty("m_ContinueButton").objectReferenceValue = continueBtn;
            so.FindProperty("m_ContinueButtonText").objectReferenceValue = continueBtnText;
            so.FindProperty("m_CompletionPanel").objectReferenceValue = completionPanel;
            so.FindProperty("m_CompletionTitleText").objectReferenceValue = compTitle;
            so.FindProperty("m_CompletionDescriptionText").objectReferenceValue = compDesc;
            so.FindProperty("m_RestartButton").objectReferenceValue = restartBtn;
            so.FindProperty("m_NextLessonButton").objectReferenceValue = nextBtn;
            so.FindProperty("m_NextLessonButtonText").objectReferenceValue = nextBtnText;

            SerializedProperty farCastProp = so.FindProperty("m_FarCastInteractors");
            if (farCastProp != null)
            {
                farCastProp.arraySize = farCastInteractors.Length;
                for (int i = 0; i < farCastInteractors.Length; i++)
                {
                    farCastProp.GetArrayElementAtIndex(i).objectReferenceValue = farCastInteractors[i];
                }
            }

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(hudPanel);
            return hudPanel;
        }

        private static void SetupLessonManager(Scene scene, GameObject car08, LessonDefinitionSO lessonData,
            CockpitControlLockManager lockManager, InteractableHighlighter highlighter, LessonHUDPanel hudPanel)
        {
            // Buscar o crear GameObject raíz del LessonManager
            GameObject managerObj = GameObject.Find("Lesson_Manager");
            if (managerObj == null)
            {
                managerObj = new GameObject("Lesson_Manager");
                SceneManager.MoveGameObjectToScene(managerObj, scene);
            }

            LessonManager manager = managerObj.GetComponent<LessonManager>();
            if (manager == null)
            {
                manager = managerObj.AddComponent<LessonManager>();
            }

            LessonDefinitionSO actualLessonData = lessonData != null ? lessonData : AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_DATA_PATH);

            var so = new SerializedObject(manager);
            so.FindProperty("m_ActiveLesson").objectReferenceValue = actualLessonData;
            so.FindProperty("m_AutoStartOnStart").boolValue = true;
            so.FindProperty("m_LockManager").objectReferenceValue = lockManager;
            so.FindProperty("m_Highlighter").objectReferenceValue = highlighter;
            so.FindProperty("m_HUDPanel").objectReferenceValue = hudPanel;
            so.FindProperty("m_VehicleRoot").objectReferenceValue = car08.transform;

            SerializedProperty currProp = so.FindProperty("m_Curriculum");
            if (currProp != null)
            {
                var curriculumLessons = new List<LessonDefinitionSO>();
                for (int l = 1; l <= 4; l++)
                {
                    string lPath = $"Assets/Scripts/Lessons/LessonData_0{l}.asset";
                    LessonDefinitionSO lDef = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(lPath);
                    if (lDef != null) curriculumLessons.Add(lDef);
                }
                currProp.arraySize = curriculumLessons.Count;
                for (int i = 0; i < curriculumLessons.Count; i++)
                {
                    currProp.GetArrayElementAtIndex(i).objectReferenceValue = curriculumLessons[i];
                }
            }
            so.ApplyModifiedProperties();

            var activeLessonField = typeof(LessonManager).GetField("m_ActiveLesson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (activeLessonField != null)
            {
                activeLessonField.SetValue(manager, actualLessonData);
            }

            var hudSo = new SerializedObject(hudPanel);
            hudSo.FindProperty("m_LessonManager").objectReferenceValue = manager;
            hudSo.ApplyModifiedProperties();

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(hudPanel);
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

        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }

        // =========================================================================
        // Helpers de Construcción de UI
        // =========================================================================

        private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset fontAsset,
            string content, float fontSize, FontStyles style, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;

            return tmp;
        }

        private static Button CreateButton(string name, Transform parent, TMP_FontAsset fontAsset,
            string labelText, Vector2 anchoredPosition, Vector2 sizeDelta, Color bgColor)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image bgImage = btnObj.AddComponent<Image>();
            bgImage.color = bgColor;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bgImage;

            ColorBlock colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.8f;
            colors.selectedColor = bgColor * 1.15f;
            btn.colors = colors;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = labelText;
            tmp.fontSize = 17f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }
    }
}
