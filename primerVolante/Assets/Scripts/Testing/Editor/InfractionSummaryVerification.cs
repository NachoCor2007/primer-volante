using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor del resumen de faltas al final de los circuitos:
    /// 1. <see cref="InfractionSummaryBuilder"/> (agrupación, orden, contadores y texto), sin escena.
    /// 2. Comportamiento de <see cref="LessonHUDPanel"/> sobre un HUD de prueba construido con el mismo código que el setup.
    /// 3. Escena <c>Circuit_01</c> y assets de lección (flag solo en el circuito).
    /// </summary>
    public static class InfractionSummaryVerification
    {
        [MenuItem("Tools/Primer Volante/Verify Infraction Summary")]
        public static void RunVerification()
        {
            int total = 0, passed = 0;
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DEL RESUMEN DE FALTAS DE CIRCUITOS");
            Debug.Log("=========================================================");

            try
            {
                VerifyBuilder(ref total, ref passed);
                VerifyHud(ref total, ref passed);
                VerifySceneAndAssets(ref total, ref passed);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FAIL] InfractionSummaryVerification: excepción no controlada: {ex}");
            }

            Debug.Log("=========================================================");
            if (total > 0 && passed == total) Debug.Log($"INFRACTION SUMMARY: {passed}/{total} pruebas pasadas con exito!");
            else Debug.LogError($"[FAIL] INFRACTION SUMMARY: {passed}/{total} pruebas pasadas");
            Debug.Log("=========================================================");
        }

        // =========================================================================
        // 1. Builder
        // =========================================================================
        private static void VerifyBuilder(ref int total, ref int passed)
        {
            var created = new List<UnityEngine.Object>();
            try
            {
                InfractionSummary empty = InfractionSummaryBuilder.Build(null);
                Assert(empty.Groups.Count == 0 && empty.MajorCount == 0 && empty.MinorCount == 0 && empty.TotalCount == 0,
                    "Build(null): 0 grupos y contadores en 0", ref total, ref passed);
                InfractionSummary emptyList = InfractionSummaryBuilder.Build(new List<InfractionRecord>());
                Assert(emptyList.Groups.Count == 0 && emptyList.MajorCount == 0 && emptyList.MinorCount == 0,
                    "Build(lista vacía): 0 grupos y contadores en 0", ref total, ref passed);
                Assert(InfractionSummaryBuilder.BuildText(empty).Contains("No cometiste faltas")
                       && InfractionSummaryBuilder.BuildText(null).Contains("No cometiste faltas"),
                    "Sin faltas el texto es el mensaje positivo", ref total, ref passed);
                Assert(InfractionSummaryBuilder.BuildCountsText(empty) == "Graves: 0 · Leves: 0",
                    "Conteos sin faltas: 'Graves: 0 · Leves: 0'", ref total, ref passed);

                InfractionManager manager = CreateManager(created);
                // Primero aparece un tipo leve: aun así los graves van arriba. SPEED_LIMIT repetido idéntico, CRASH con 2 descripciones.
                Report(manager, InfractionIds.SpeedLimit, ("limit", "40"));            // 1 leve
                ReportCrash(manager, "23");                                             // 2 grave
                Report(manager, InfractionIds.RedLight);                                // 3 grave
                ReportCrash(manager, "31");                                             // 4 grave (otra descripción)
                Report(manager, InfractionIds.SpeedLimit, ("limit", "40"));            // 5 leve idéntica a la 1
                Report(manager, InfractionIds.Graze, ("object", "un cono"), ("speed", "9")); // 6 leve

                InfractionSummary summary = InfractionSummaryBuilder.Build(manager.Records);
                string[] ids = summary.Groups.Select(g => g.Id).ToArray();
                Assert(ids.SequenceEqual(new[] { "CRASH", "RED_LIGHT", "SPEED_LIMIT", "GRAZE" }),
                    $"Orden: graves primero y por primera aparición dentro de cada gravedad ({string.Join(", ", ids)})", ref total, ref passed);
                Assert(summary.MajorCount == 3 && summary.MinorCount == 3 && summary.TotalCount == 6,
                    $"Contadores por ocurrencia: 3 graves y 3 leves ({summary.MajorCount}/{summary.MinorCount})", ref total, ref passed);

                InfractionSummaryGroup crash = summary.Groups[0];
                Assert(crash.Count == 2 && crash.Details.Count == 2 && crash.Details.All(d => d.Count == 1)
                       && crash.Details[0].Description.Contains("23 km/h") && crash.Details[1].Description.Contains("31 km/h"),
                    "CRASH x2 con descripciones distintas: 2 detalles de Count 1, por primera aparición", ref total, ref passed);
                InfractionSummaryGroup speed = summary.Groups[2];
                Assert(speed.Count == 2 && speed.Details.Count == 1 && speed.Details[0].Count == 2,
                    "SPEED_LIMIT x2 idénticas: 1 detalle con Count 2", ref total, ref passed);
                Assert(summary.Groups[1].Count == 1 && summary.Groups[1].Details.Count == 1 && summary.Groups[1].Details[0].Count == 1,
                    "RED_LIGHT x1: un detalle de Count 1", ref total, ref passed);
                Assert(crash.Severity == InfractionSeverity.Major && speed.Severity == InfractionSeverity.Minor && crash.ShortTitle == "Choque",
                    "Cada grupo conserva gravedad y título corto del catálogo", ref total, ref passed);

                // El orden depende del Sequence, no del orden de la lista recibida
                InfractionSummary shuffled = InfractionSummaryBuilder.Build(manager.Records.Reverse().ToList());
                Assert(shuffled.Groups.Select(g => g.Id).SequenceEqual(ids),
                    "Una lista en otro orden da el mismo resultado (se ordena por Sequence)", ref total, ref passed);

                string text = InfractionSummaryBuilder.BuildText(summary);
                Assert(text.Contains("Faltas graves (3)") && text.Contains("Faltas leves (3)")
                       && text.IndexOf("Faltas graves", StringComparison.Ordinal) < text.IndexOf("Faltas leves", StringComparison.Ordinal),
                    "Texto: sección de graves con su cantidad antes de la de leves", ref total, ref passed);
                Assert(text.Contains("<b>Choque</b> ×2") && text.Contains("<b>Semáforo en rojo</b>\n") && text.Contains("<b>Exceso de velocidad</b> ×2")
                       && text.Contains("<b>Roce</b>\n"),
                    "Texto: título en negrita y ×N solo si el tipo se repite", ref total, ref passed);
                Assert(crash.Details.All(d => text.Contains("• " + d.Description + "\n")) && text.Contains("• " + speed.Details[0].Description + " (×2)\n"),
                    "Texto: una viñeta por descripción y (×N) solo en el detalle repetido", ref total, ref passed);
                Assert(!text.Contains("×1") && text.Split(new[] { "(×2)" }, StringSplitOptions.None).Length == 2,
                    "Texto: no hay marcas ×1 y (×2) aparece una sola vez", ref total, ref passed);
                Assert(InfractionSummaryBuilder.BuildText(summary, "x").Contains("<b>Choque</b> x2") && !InfractionSummaryBuilder.BuildText(summary, "x").Contains("×"),
                    "Texto: el signo de repetición es configurable (fallback 'x')", ref total, ref passed);
                Assert(InfractionSummaryBuilder.BuildCountsText(summary) == "Graves: 3 · Leves: 3", "Conteos: 'Graves: 3 · Leves: 3'", ref total, ref passed);

                manager.Clear();
                Report(manager, InfractionIds.Graze, ("object", "un cono"), ("speed", "9"));
                string onlyMinor = InfractionSummaryBuilder.BuildText(InfractionSummaryBuilder.Build(manager.Records));
                Assert(!onlyMinor.Contains("Faltas graves") && onlyMinor.Contains("Faltas leves (1)"),
                    "Solo leves: se omite la sección de graves", ref total, ref passed);
                manager.Clear();
                ReportCrash(manager, "23");
                string onlyMajor = InfractionSummaryBuilder.BuildText(InfractionSummaryBuilder.Build(manager.Records));
                Assert(!onlyMajor.Contains("Faltas leves") && onlyMajor.Contains("Faltas graves (1)"),
                    "Solo graves: se omite la sección de leves", ref total, ref passed);
            }
            finally
            {
                DestroyAll(created);
            }
        }

        // =========================================================================
        // 2. HUD
        // =========================================================================
        private static void VerifyHud(ref int total, ref int passed)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scratch = default;
            var created = new List<UnityEngine.Object>();
            LessonHUDPanel hud = null;
            try
            {
                if (!LessonHUDMinimizeVerification.TryCreateScratchScene(out scratch, out bool replacedUserScenes))
                {
                    Assert(false, "No se pudo crear la escena de trabajo: se saltea el comportamiento del HUD", ref total, ref passed);
                    return;
                }
                if (replacedUserScenes) previousActive = default;

                InfractionManager manager = CreateManager(created);
                hud = BuildTestHud(created, out Transform parent);
                Invoke(hud, "SetupButtons");
                RectTransform rect = (RectTransform)hud.transform;

                var circuit = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                circuit.lessonId = "CIRCUIT_TEST";
                circuit.lessonTitle = "Circuito de prueba";
                circuit.showInfractionSummary = true;
                var plain = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                plain.lessonId = "LESSON_TEST";
                plain.lessonTitle = "Lección de prueba";
                var step = ScriptableObject.CreateInstance<LessonStepSO>();
                step.stepTitle = "Paso";
                step.instructionText = "Instrucción";
                created.Add(circuit);
                created.Add(plain);
                created.Add(step);

                Assert(hud.InfractionSummaryPanel != null && hud.InfractionSummaryTitleText != null && hud.InfractionSummaryCountsText != null
                       && hud.InfractionSummaryListText != null && hud.InfractionSummaryScrollRect != null && hud.InfractionSummaryContinueButton != null,
                    "BuildInfractionSummary asigna las 6 referencias de LessonHUDPanel", ref total, ref passed);
                Assert(!hud.InfractionSummaryPanel.activeSelf && !hud.IsInfractionSummaryVisible,
                    "La página de resumen arranca inactiva", ref total, ref passed);
                Assert(hud.InfractionSummaryTitleText.text == "Resumen de faltas", "Título de la página: 'Resumen de faltas'", ref total, ref passed);

                InfractionSummarySetup.BuildInfractionSummary(hud, null);
                Assert(CountByName(hud.transform, InfractionSummarySetup.PANEL_NAME) == 1 && CountByName(hud.transform, "Button_Continue") == 2
                       && CountByName(hud.transform, "InfractionSummaryList") == 1 && CountByName(hud.transform, "Scrollbar_Vertical") == 1,
                    "BuildInfractionSummary es idempotente (no duplica objetos)", ref total, ref passed);

                Vector2 originalSize = rect.sizeDelta;
                Vector2 originalPivot = rect.pivot;
                Vector3 originalLocalPos = rect.localPosition;
                Vector3[] originalCorners = WorldCorners(rect);

                // --- Con faltas leves y graves
                ReportCrash(manager, "23");
                Report(manager, InfractionIds.SpeedLimit, ("limit", "40"));
                Report(manager, InfractionIds.SpeedLimit, ("limit", "40"));
                hud.SetMinimized(true);
                // Un scroll previo (de una visita anterior) no puede quedar: el resumen siempre arranca arriba
                hud.InfractionSummaryScrollRect.content.anchoredPosition = new Vector2(0f, 120f);
                hud.DisplayCompletion(circuit);

                Assert(hud.IsInfractionSummaryVisible && hud.InfractionSummaryPanel.activeSelf && !hud.CompletionPanel.activeSelf
                       && !hud.StepContentPanel.activeSelf && !hud.IsMinimized && hud.Background.activeSelf,
                    "DisplayCompletion con flag y log: resumen visible, Completion y StepContent ocultos, panel maximizado", ref total, ref passed);
                Assert(hud.FarCastingActive, "Con el resumen visible el rayo a distancia está activo", ref total, ref passed);
                Assert(Mathf.Approximately(rect.rect.width, 600f) && Mathf.Approximately(rect.rect.height, 440f),
                    $"Con el resumen el canvas mide 600x440 ({rect.rect.size})", ref total, ref passed);
                Vector3[] expanded = WorldCorners(rect);
                Assert(Vector3.Distance(expanded[0], originalCorners[0]) < 0.001f && Vector3.Distance(expanded[3], originalCorners[3]) < 0.001f,
                    $"El borde inferior queda en la misma posición mundial (tolerancia 1 mm; dif {Vector3.Distance(expanded[0], originalCorners[0]) * 1000f:F3} mm)", ref total, ref passed);
                Assert(expanded[1].y > originalCorners[1].y - 0.0001f && Vector3.Distance(expanded[1], originalCorners[1]) > 0.05f,
                    "El canvas creció hacia arriba", ref total, ref passed);
                Assert(hud.InfractionSummaryCountsText.text == "Graves: 1 · Leves: 2", $"Conteos de la página ('{hud.InfractionSummaryCountsText.text}')", ref total, ref passed);
                string listText = hud.InfractionSummaryListText.text;
                Assert(listText.Contains("<b>Choque</b>") && listText.Contains("Chocaste con un cono a 23 km/h") && listText.Contains("<b>Exceso de velocidad</b> ×2"),
                    "La lista muestra títulos y descripciones reales con ×N", ref total, ref passed);
                Assert(Mathf.Approximately(hud.InfractionSummaryScrollRect.content.anchoredPosition.y, 0f), "El scroll arranca arriba", ref total, ref passed);

                // Snapshot: lo que se registre después no cambia la página
                Report(manager, InfractionIds.RedLight);
                Assert(hud.InfractionSummaryListText.text == listText && hud.InfractionSummaryCountsText.text == "Graves: 1 · Leves: 2",
                    "Snapshot: registros agregados después de DisplayCompletion no cambian la página", ref total, ref passed);

                // Minimizar bloqueado
                int events = 0;
                Action<bool> counter = v => events++;
                hud.MinimizedChanged += counter;
                hud.ToggleMinimized();
                hud.SetMinimized(true);
                hud.MinimizeButton.onClick.Invoke();
                hud.MaximizeButton.onClick.Invoke();
                hud.MinimizedChanged -= counter;
                Assert(!hud.IsMinimized && events == 0 && hud.InfractionSummaryPanel.activeSelf && !hud.StepContentPanel.activeSelf && hud.Background.activeSelf,
                    "Con el resumen visible, ToggleMinimized/SetMinimized(true)/botones no minimizan", ref total, ref passed);

                // Continuar
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(!hud.IsInfractionSummaryVisible && !hud.InfractionSummaryPanel.activeSelf && hud.CompletionPanel.activeSelf && !hud.StepContentPanel.activeSelf,
                    "Continuar: resumen oculto y Completion visible", ref total, ref passed);
                Assert(rect.sizeDelta == originalSize && rect.pivot == originalPivot && rect.localPosition == originalLocalPos,
                    "Continuar restaura exactamente tamaño, pivot y posición originales", ref total, ref passed);
                Assert(hud.CompletionTitleText().text == "Circuito completado con faltas graves"
                       && hud.CompletionDescriptionText().text.Contains("1 falta grave y 2 leves") && hud.CompletionDescriptionText().text.Contains("Repetí el circuito"),
                    "Felicitación con una grave: título de faltas graves y descripción con singular/plural correcto (snapshot)", ref total, ref passed);
                hud.ToggleMinimized();
                Assert(!hud.IsMinimized, "Con Completion visible tampoco se minimiza", ref total, ref passed);

                // Continuar de nuevo no hace nada
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(hud.CompletionPanel.activeSelf && !hud.IsInfractionSummaryVisible, "Continuar sin resumen visible no hace nada", ref total, ref passed);

                // --- Solo leves
                manager.Clear();
                Report(manager, InfractionIds.Graze, ("object", "un cono"), ("speed", "9"));
                hud.DisplayCompletion(circuit);
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(hud.CompletionTitleText().text == "Circuito completado" && hud.CompletionDescriptionText().text.Contains("con 1 falta leve."),
                    "Solo leves: título 'Circuito completado' y '1 falta leve' en singular", ref total, ref passed);
                manager.Clear();
                Report(manager, InfractionIds.Graze, ("object", "un cono"), ("speed", "9"));
                Report(manager, InfractionIds.LowBeam, ("when", "Arrancaste"), ("lights", "con las luces apagadas"));
                hud.DisplayCompletion(circuit);
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(hud.CompletionDescriptionText().text.Contains("con 2 faltas leves."), "Solo leves: '2 faltas leves' en plural", ref total, ref passed);

                // --- Solo graves
                manager.Clear();
                ReportCrash(manager, "23");
                ReportCrash(manager, "40");
                hud.DisplayCompletion(circuit);
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(hud.CompletionTitleText().text == "Circuito completado con faltas graves"
                       && hud.CompletionDescriptionText().text.Contains("con 2 faltas graves.") && !hud.CompletionDescriptionText().text.Contains(" y 0"),
                    "Solo graves: '2 faltas graves' sin mencionar leves", ref total, ref passed);

                // --- Sin faltas
                manager.Clear();
                hud.DisplayCompletion(circuit);
                Assert(hud.IsInfractionSummaryVisible && hud.InfractionSummaryListText.text.Contains("No cometiste faltas")
                       && hud.InfractionSummaryCountsText.text == "Graves: 0 · Leves: 0",
                    "Sin faltas: el resumen muestra el mensaje positivo", ref total, ref passed);
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(hud.CompletionTitleText().text == "¡Excelente Trabajo!"
                       && hud.CompletionDescriptionText().text == "Has completado satisfactoriamente la Circuito de prueba.\n¡Felicitaciones por completar el curso de manejo en Primer Volante!",
                    "Sin faltas: la felicitación conserva el texto actual", ref total, ref passed);

                // --- Lección sin flag: comportamiento anterior intacto
                ReportCrash(manager, "23");
                hud.DisplayStep(step, 0, 1);
                hud.DisplayCompletion(plain);
                Assert(!hud.IsInfractionSummaryVisible && hud.CompletionPanel.activeSelf && !hud.InfractionSummaryPanel.activeSelf
                       && hud.CompletionTitleText().text == "¡Excelente Trabajo!"
                       && hud.CompletionDescriptionText().text == "Has completado satisfactoriamente la Lección de prueba.\n¡Felicitaciones por completar el curso de manejo en Primer Volante!"
                       && rect.sizeDelta == originalSize,
                    "Lección sin flag: va directo a las felicitaciones con el texto de siempre y sin cambiar el tamaño", ref total, ref passed);

                // --- Flag sin log
                InfractionReporting.Unregister(manager);
                Assert(InfractionReporting.Log == null, "Precondición: no hay InfractionManager registrado", ref total, ref passed);
                hud.DisplayStep(step, 0, 1);
                hud.DisplayCompletion(circuit);
                Assert(!hud.IsInfractionSummaryVisible && hud.CompletionPanel.activeSelf && hud.CompletionTitleText().text == "¡Excelente Trabajo!" && rect.sizeDelta == originalSize,
                    "Flag sin log: va directo a las felicitaciones (con advertencia)", ref total, ref passed);
                InfractionReporting.Register(manager);

                // --- DisplayStep con el resumen visible
                manager.Clear();
                ReportCrash(manager, "23");
                hud.DisplayCompletion(circuit);
                Assert(hud.IsInfractionSummaryVisible, "Precondición: resumen visible", ref total, ref passed);
                hud.DisplayStep(step, 0, 1);
                Assert(!hud.IsInfractionSummaryVisible && !hud.InfractionSummaryPanel.activeSelf && hud.StepContentPanel.activeSelf && !hud.CompletionPanel.activeSelf
                       && rect.sizeDelta == originalSize && rect.pivot == originalPivot && rect.localPosition == originalLocalPos,
                    "DisplayStep con el resumen visible lo oculta y restaura tamaño, pivot y posición", ref total, ref passed);
                hud.SetMinimized(true);
                Assert(hud.IsMinimized, "Tras DisplayStep se puede minimizar de nuevo", ref total, ref passed);
                hud.SetMinimized(false);

                // La geometría se restaura aun con el padre movido (se compara contra lo guardado al expandir)
                parent.position += new Vector3(1f, 0f, 2f);
                hud.DisplayCompletion(circuit);
                hud.InfractionSummaryContinueButton.onClick.Invoke();
                Assert(rect.sizeDelta == originalSize, "Expandir y contraer con el auto movido restaura el tamaño", ref total, ref passed);
            }
            finally
            {
                if (hud != null) hud.SetFarCasting(false);
                DestroyAll(created);

                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (scratch.IsValid()) EditorSceneManager.CloseScene(scratch, true);
            }
        }

        private static TMP_Text CompletionTitleText(this LessonHUDPanel hud) => FindDeep(hud.transform, "CompletionTitle").GetComponent<TMP_Text>();
        private static TMP_Text CompletionDescriptionText(this LessonHUDPanel hud) => FindDeep(hud.transform, "CompletionDesc").GetComponent<TMP_Text>();

        private static LessonHUDPanel BuildTestHud(List<UnityEngine.Object> created, out Transform parent)
        {
            // El canvas cuelga de un "auto" rotado y desplazado, como en Circuit_01 (posición en metros del padre)
            var carObj = new GameObject("TMP_Car");
            created.Add(carObj);
            carObj.transform.SetPositionAndRotation(new Vector3(3f, 0.2f, -4f), Quaternion.Euler(8f, 35f, 3f));
            parent = carObj.transform;

            var canvasObj = new GameObject("TMP_LessonHUD_Canvas", typeof(RectTransform));
            canvasObj.transform.SetParent(carObj.transform, false);
            RectTransform canvasRect = (RectTransform)canvasObj.transform;
            canvasRect.sizeDelta = new Vector2(600f, 260f);
            canvasRect.localScale = Vector3.one * 0.001f;
            canvasRect.localPosition = new Vector3(-0.30f, 1.16f, 0.45f);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            LessonHUDPanel hud = canvasObj.AddComponent<LessonHUDPanel>();

            NewChild("Background", canvasObj.transform, true).AddComponent<Image>();

            GameObject stepContent = NewChild("StepContentPanel", canvasObj.transform, true);
            TextMeshProUGUI progress = NewChild("StepProgress", stepContent.transform, false).AddComponent<TextMeshProUGUI>();
            TextMeshProUGUI instruction = NewChild("InstructionText", stepContent.transform, false).AddComponent<TextMeshProUGUI>();
            GameObject continueObj = NewChild("Button_Continue", stepContent.transform, false);
            Image continueImg = continueObj.AddComponent<Image>();
            Button continueBtn = continueObj.AddComponent<Button>();
            continueBtn.targetGraphic = continueImg;
            TextMeshProUGUI continueText = NewChild("Text", continueObj.transform, false).AddComponent<TextMeshProUGUI>();

            GameObject glow = NewChild("SuccessBorderGlow", canvasObj.transform, true);
            Image glowImg = glow.AddComponent<Image>();
            glow.SetActive(false);

            GameObject completion = NewChild("CompletionPanel", canvasObj.transform, true);
            TextMeshProUGUI compTitle = NewChild("CompletionTitle", completion.transform, false).AddComponent<TextMeshProUGUI>();
            TextMeshProUGUI compDesc = NewChild("CompletionDesc", completion.transform, false).AddComponent<TextMeshProUGUI>();
            completion.SetActive(false);

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

            // Interactor de prueba (en la escena de trabajo): así SetFarCasting nunca busca los de las escenas del usuario
            var interactorObj = new GameObject("TMP_NearFarInteractor");
            created.Add(interactorObj);
            hud.SetFarCastInteractors(new[] { interactorObj.AddComponent<NearFarInteractor>() });

            // Mismo código que usan los setups de las escenas
            Lesson01SceneSetup.BuildMinimizeControls(hud, null);
            InfractionSummarySetup.BuildInfractionSummary(hud, null);
            return hud;
        }

        // =========================================================================
        // 3. Escena y assets
        // =========================================================================
        private static void VerifySceneAndAssets(ref int total, ref int passed)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(InfractionSummarySetup.CIRCUIT_SCENE_PATH);
            try
            {
                LessonHUDPanel hud = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<LessonHUDPanel>(true)).FirstOrDefault(h => h != null);
                Assert(hud != null, "Circuit_01: existe LessonHUDPanel (LessonHUD_Canvas)", ref total, ref passed);
                if (hud == null) return;

                Transform panel = hud.transform.Find(InfractionSummarySetup.PANEL_NAME);
                Assert(panel != null && CountByName(hud.transform, InfractionSummarySetup.PANEL_NAME) == 1,
                    "Circuit_01: existe un único InfractionSummaryPanel hijo directo de LessonHUD_Canvas", ref total, ref passed);
                Assert(panel != null && !panel.gameObject.activeSelf, "Circuit_01: InfractionSummaryPanel arranca inactivo", ref total, ref passed);
                Assert(hud.InfractionSummaryPanel != null && hud.InfractionSummaryPanel.transform == panel && hud.InfractionSummaryTitleText != null
                       && hud.InfractionSummaryCountsText != null && hud.InfractionSummaryListText != null && hud.InfractionSummaryScrollRect != null
                       && hud.InfractionSummaryContinueButton != null,
                    "Circuit_01: LessonHUDPanel tiene las 6 referencias del resumen asignadas", ref total, ref passed);
                Assert(hud.InfractionSummaryCanvasSize == new Vector2(600f, 440f), "Circuit_01: tamaño del resumen 600x440", ref total, ref passed);

                if (panel != null && hud.CompletionPanel != null && hud.MinimizedBar != null)
                {
                    int p = panel.GetSiblingIndex();
                    Assert(p > hud.CompletionPanel.transform.GetSiblingIndex() && p < hud.MinimizedBar.transform.GetSiblingIndex(),
                        "Circuit_01: InfractionSummaryPanel va después de CompletionPanel y antes de MinimizedBar", ref total, ref passed);
                }

                ScrollRect scroll = hud.InfractionSummaryScrollRect;
                if (scroll != null)
                {
                    Assert(scroll.vertical && !scroll.horizontal && scroll.viewport != null && scroll.viewport.GetComponent<RectMask2D>() != null
                           && scroll.content != null && scroll.content.GetComponent<VerticalLayoutGroup>() != null
                           && scroll.content.GetComponent<ContentSizeFitter>() != null && scroll.verticalScrollbar != null
                           && scroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.Permanent
                           && hud.InfractionSummaryListText.transform.IsChildOf(scroll.content),
                        "Circuit_01: ScrollRect vertical con viewport RectMask2D, content con layout y scrollbar visible", ref total, ref passed);
                    Assert(scroll.viewport.rect.height >= 270f || ((RectTransform)scroll.transform).sizeDelta.y >= 270f,
                        "Circuit_01: el viewport del scroll mide ~280 de alto", ref total, ref passed);
                }

                bool fontsOk = true;
                foreach (TextMeshProUGUI t in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (t.fontSize < 14f) fontsOk = false;
                }
                Assert(fontsOk, "Circuit_01: textos del resumen >= 14", ref total, ref passed);
                Assert(panel != null && panel.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == LayerMask.NameToLayer("UI")),
                    "Circuit_01: el resumen está en la capa UI (rayo VR)", ref total, ref passed);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            string[] guids = AssetDatabase.FindAssets("t:LessonDefinitionSO");
            var withFlag = new List<string>();
            foreach (string guid in guids)
            {
                var lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (lesson != null && lesson.showInfractionSummary) withFlag.Add(lesson.lessonId);
            }
            Assert(guids.Length >= 8 && withFlag.Count == 1 && withFlag[0] == "CIRCUIT_01",
                $"Solo LessonData_Circuit_01 tiene showInfractionSummary = true ({guids.Length} lecciones; con flag: {string.Join(", ", withFlag)})", ref total, ref passed);

            bool obsolete = false;
            foreach (string path in new[] { "Assets/Scripts/Lessons/LessonHUDPanel.cs", "Assets/Scripts/Evaluation/InfractionSummaryBuilder.cs",
                         "Assets/Scripts/Testing/Editor/InfractionSummarySetup.cs", "Assets/Scripts/Testing/Editor/InfractionSummaryVerification.cs" })
            {
                if (System.IO.File.ReadAllText(path).Contains("FindFirst" + "ObjectByType")) obsolete = true;
            }
            Assert(!obsolete, "Sin la API obsoleta FindFirst..ObjectByType en los archivos de la feature", ref total, ref passed);
        }

        // =========================================================================
        // Helpers
        // =========================================================================
        private static InfractionManager CreateManager(List<UnityEngine.Object> created)
        {
            var go = new GameObject("TMP_InfractionManager");
            created.Add(go);
            var manager = go.AddComponent<InfractionManager>();
            manager.SetCatalog(AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH));
            var so = new SerializedObject(manager);
            so.FindProperty("m_LogEvents").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Fuera de Play Mode Unity no llama a OnEnable: se registra a mano (y se quita en DestroyAll)
            InfractionReporting.Register(manager);
            return manager;
        }

        private static void DestroyAll(List<UnityEngine.Object> created)
        {
            foreach (UnityEngine.Object o in created)
            {
                if (o is GameObject go && go != null)
                {
                    var manager = go.GetComponent<InfractionManager>();
                    if (manager != null) InfractionReporting.Unregister(manager);
                }
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        private static void Report(InfractionManager manager, string id, params (string key, string value)[] parameters)
        {
            manager.Report(InfractionReport.Create(id, null, Vector3.zero, parameters));
        }

        private static void ReportCrash(InfractionManager manager, string speed)
        {
            Report(manager, InfractionIds.Crash, ("object", "un cono"), ("speed", speed));
        }

        private static Vector3[] WorldCorners(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners;
        }

        private static GameObject NewChild(string name, Transform parent, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (stretch)
            {
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            return go;
        }

        private static Transform FindDeep(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static int CountByName(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).Count(t => t.name == name);

        private static void Invoke(object target, string method)
        {
            System.Reflection.MethodInfo m = target.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
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
