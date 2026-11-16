using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PrimerVolante.Lessons;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Agrega al HUD de <c>Circuit_01</c> la página "Resumen de faltas" (<c>InfractionSummaryPanel</c>, hija de
    /// <c>LessonHUD_Canvas</c>, inactiva), cablea sus referencias en <see cref="LessonHUDPanel"/> y activa
    /// <c>showInfractionSummary</c> en <c>LessonData_Circuit_01</c>. Es idempotente: reutiliza los objetos si ya existen.
    /// El constructor de la página (<see cref="BuildInfractionSummary"/>) lo reutiliza la verificación sobre un HUD de prueba.
    /// </summary>
    public static class InfractionSummarySetup
    {
        public const string CIRCUIT_SCENE_PATH = "Assets/Scenes/Lessons/Circuit_01.unity";
        public const string PANEL_NAME = "InfractionSummaryPanel";

        private static readonly Color s_TitleColor = new Color(0.35f, 0.75f, 1f, 1f);
        private static readonly Color s_BodyColor = new Color(0.85f, 0.85f, 0.90f, 1f);

        [MenuItem("Tools/Primer Volante/Lessons/Setup Infraction Summary")]
        public static void SetupCircuitScene()
        {
            // La escena se abre en modo Single: pedir guardar las que estén sucias y abortar si el usuario cancela
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[InfractionSummarySetup] Cancelado por el usuario: hay escenas sin guardar.");
                return;
            }

            // Escenas abiertas al empezar (y cuál era la activa) para volver a dejarlas como estaban
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

            bool ok;
            try
            {
                ok = SetupScene(CIRCUIT_SCENE_PATH);
            }
            finally
            {
                RestoreOpenScenes(setup);
            }

            if (ok) Debug.Log("[InfractionSummarySetup] Resumen de faltas configurado en Circuit_01 (1/1).");
            else Debug.LogError("[InfractionSummarySetup] [FAIL] No se pudo configurar el resumen de faltas en Circuit_01.");
        }

        private static void RestoreOpenScenes(SceneSetup[] setup)
        {
            // Una escena sin guardar (path vacío) no se puede restaurar: se deja el setup por defecto de Unity
            if (setup.Length == 0 || System.Array.Exists(setup, s => string.IsNullOrEmpty(s.path)))
            {
                Debug.LogWarning("[InfractionSummarySetup] Había una escena sin guardar abierta: no se puede restaurar. " +
                                 "Se deja una escena nueva por defecto.");
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }

            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        /// <summary>Abre la escena, crea/reconfigura la página en su LessonHUD_Canvas, activa el flag de la lección y guarda.</summary>
        public static bool SetupScene(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[InfractionSummarySetup] [FAIL] No se pudo abrir {scenePath}");
                return false;
            }

            LessonHUDPanel hud = Object.FindAnyObjectByType<LessonHUDPanel>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError($"[InfractionSummarySetup] [FAIL] {scenePath} no tiene LessonHUDPanel (LessonHUD_Canvas).");
                return false;
            }

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Lesson01SceneSetup.FONT_PATH);
            BuildInfractionSummary(hud, font);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved) Debug.LogError($"[InfractionSummarySetup] [FAIL] No se pudo guardar {scenePath}");

            return saved && EnableFlagOnCircuitLesson();
        }

        private static bool EnableFlagOnCircuitLesson()
        {
            var lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Circuit01LessonSetup.LESSON_PATH);
            if (lesson == null)
            {
                Debug.LogError($"[InfractionSummarySetup] [FAIL] No existe {Circuit01LessonSetup.LESSON_PATH}");
                return false;
            }

            if (!lesson.showInfractionSummary)
            {
                lesson.showInfractionSummary = true;
                EditorUtility.SetDirty(lesson);
                AssetDatabase.SaveAssets();
            }
            return true;
        }

        /// <summary>
        /// Crea (o reutiliza) <c>InfractionSummaryPanel</c> bajo el canvas del HUD y asigna las referencias nuevas de
        /// <see cref="LessonHUDPanel"/>. Layout para un canvas de 600x440: título, conteos, lista con scroll, botón Continuar.
        /// </summary>
        public static void BuildInfractionSummary(LessonHUDPanel hud, TMP_FontAsset font)
        {
            if (hud == null) return;
            Transform canvasTr = hud.transform;

            GameObject panel = GetOrCreateChild(canvasTr, PANEL_NAME);
            Stretch(panel.GetComponent<RectTransform>());

            TextMeshProUGUI title = GetOrCreateText(panel.transform, "InfractionSummaryTitle", font, "Resumen de faltas", 26f,
                FontStyles.Bold, new Vector2(0f, -14f), new Vector2(560f, 36f), s_TitleColor);
            TextMeshProUGUI counts = GetOrCreateText(panel.transform, "InfractionSummaryCounts", font, "Graves: 0 · Leves: 0", 17f,
                FontStyles.Normal, new Vector2(0f, -52f), new Vector2(560f, 24f), s_BodyColor);

            // ScrollView: 570x280 centrado, con la barra de scroll a la derecha (20 de ancho, fácil de agarrar con el rayo)
            GameObject scrollObj = GetOrCreateChild(panel.transform, "InfractionSummaryScroll");
            SetRect(scrollObj.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -84f), new Vector2(570f, 280f));
            Image scrollBg = GetOrAdd<Image>(scrollObj);
            scrollBg.color = new Color(1f, 1f, 1f, 0.05f);
            ScrollRect scroll = GetOrAdd<ScrollRect>(scrollObj);

            GameObject viewport = GetOrCreateChild(scrollObj.transform, "Viewport");
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportRect.offsetMax = new Vector2(-24f, 0f);
            GetOrAdd<RectMask2D>(viewport);
            Image viewportImage = GetOrAdd<Image>(viewport);
            viewportImage.color = new Color(0f, 0f, 0f, 0f); // transparente pero rayable: se puede arrastrar desde cualquier punto
            viewportImage.raycastTarget = true;

            GameObject content = GetOrCreateChild(viewport.transform, "Content");
            SetRect(content.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 0f));
            VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(content);
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(content);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI list = GetOrCreateText(content.transform, "InfractionSummaryList", font, string.Empty, 15f,
                FontStyles.Normal, Vector2.zero, new Vector2(0f, 0f), Color.white);
            RectTransform listRect = list.rectTransform;
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.pivot = new Vector2(0.5f, 1f);
            list.alignment = TextAlignmentOptions.TopLeft;
            list.textWrappingMode = TextWrappingModes.Normal;
            list.overflowMode = TextOverflowModes.Overflow;
            list.richText = true;
            list.raycastTarget = false;

            // Barra de scroll vertical, siempre visible
            GameObject barObj = GetOrCreateChild(scrollObj.transform, "Scrollbar_Vertical");
            SetRect(barObj.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(20f, 0f));
            Image barImage = GetOrAdd<Image>(barObj);
            barImage.color = new Color(0.15f, 0.18f, 0.24f, 1f);
            Scrollbar bar = GetOrAdd<Scrollbar>(barObj);

            GameObject slidingArea = GetOrCreateChild(barObj.transform, "SlidingArea");
            Stretch(slidingArea.GetComponent<RectTransform>());
            GameObject handle = GetOrCreateChild(slidingArea.transform, "Handle");
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.offsetMin = new Vector2(2f, 0f);
            handleRect.offsetMax = new Vector2(-2f, 0f);
            Image handleImage = GetOrAdd<Image>(handle);
            handleImage.color = new Color(0.35f, 0.60f, 0.90f, 1f);

            bar.handleRect = handleRect;
            bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.BottomToTop;
            ColorBlock barColors = bar.colors;
            barColors.normalColor = handleImage.color;
            barColors.highlightedColor = handleImage.color * 1.25f;
            barColors.pressedColor = handleImage.color * 0.8f;
            barColors.selectedColor = handleImage.color * 1.15f;
            bar.colors = barColors;

            scroll.viewport = viewportRect;
            scroll.content = content.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 25f;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalScrollbar = null;

            // Botón Continuar abajo (mismo estilo que Button_Continue del paso)
            Transform existingButton = panel.transform.Find("Button_Continue");
            Button continueBtn = existingButton != null ? existingButton.GetComponent<Button>() : null;
            if (continueBtn == null)
            {
                if (existingButton != null) Object.DestroyImmediate(existingButton.gameObject);
                continueBtn = Lesson01SceneSetup.CreateButton("Button_Continue", panel.transform, font, "Continuar",
                    new Vector2(0f, -376f), new Vector2(280f, 46f), new Color(0.12f, 0.55f, 0.40f, 1f));
            }
            RectTransform btnRect = continueBtn.GetComponent<RectTransform>();
            btnRect.anchoredPosition = new Vector2(0f, -376f);
            btnRect.sizeDelta = new Vector2(280f, 46f);

            // Capa UI (el rayo del HUD está restringido a esa capa)
            int uiLayer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 5;
            Lesson01SceneSetup.SetLayerRecursively(panel, uiLayer);

            // Después de CompletionPanel y antes de MinimizedBar
            Transform completion = hud.CompletionPanel != null ? hud.CompletionPanel.transform : canvasTr.Find("CompletionPanel");
            if (completion != null)
            {
                int target = completion.GetSiblingIndex() + 1;
                if (panel.transform.GetSiblingIndex() < target) target--;
                panel.transform.SetSiblingIndex(Mathf.Max(0, target));
            }

            panel.SetActive(false);

            var so = new SerializedObject(hud);
            so.FindProperty("m_InfractionSummaryPanel").objectReferenceValue = panel;
            so.FindProperty("m_InfractionSummaryTitleText").objectReferenceValue = title;
            so.FindProperty("m_InfractionSummaryCountsText").objectReferenceValue = counts;
            so.FindProperty("m_InfractionSummaryListText").objectReferenceValue = list;
            so.FindProperty("m_InfractionSummaryScrollRect").objectReferenceValue = scroll;
            so.FindProperty("m_InfractionSummaryContinueButton").objectReferenceValue = continueBtn;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);
        }

        // ---------------------------------------------------------------------
        // Helpers de construcción (idempotentes)
        // ---------------------------------------------------------------------
        private static GameObject GetOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                if (existing.GetComponent<RectTransform>() == null) existing.gameObject.AddComponent<RectTransform>();
                return existing.gameObject;
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static TextMeshProUGUI GetOrCreateText(Transform parent, string name, TMP_FontAsset font, string content,
            float fontSize, FontStyles style, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            Transform existing = parent.Find(name);
            TextMeshProUGUI tmp = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null)
            {
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                tmp = Lesson01SceneSetup.CreateText(name, parent, font, content, fontSize, style, anchoredPosition, size, color);
            }

            tmp.rectTransform.anchoredPosition = anchoredPosition;
            tmp.rectTransform.sizeDelta = size;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            if (font != null) tmp.font = font;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }
    }
}
