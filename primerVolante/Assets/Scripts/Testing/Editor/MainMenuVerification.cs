using System.Collections.Generic;
using System.Reflection;
using PrimerVolante.Lessons;
using PrimerVolante.MainMenu;
using PrimerVolante.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor del menú principal en cabina (issue #7): matemática del volante, acumulador de pasos,
    /// cursor de la lista de lecciones, mapeo marcha→página, flags opt-in de VRGearShifter / StartButton /
    /// InGameMenuController, controlador del menú, ilustraciones y la escena MainMenu.unity.
    /// </summary>
    public static class MainMenuVerification
    {
        private const string CAR_PREFAB = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";
        private const string CONTROLLER_MODEL = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Models/UniversalController.fbx";
        private const string CURRICULUM_FOLDER = "Assets/Scripts/Lessons/Curriculum";
        private const float EPS = 0.0001f;

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Run Main Menu Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DEL MENU PRINCIPAL EN CABINA");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;

            VerifyScrollMath();
            VerifyAccumulator();
            VerifyLessonList();
            VerifyPageMapping();
            VerifyIllustration();
            VerifyCarPrefabFlags();
            VerifyCarInstanceFlags();
            VerificationSceneUtil.WithScene(MainMenuSetup.SCENE_PATH, VerifyScene);

            Debug.Log("=========================================================");
            Debug.Log($"RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            Debug.Log("=========================================================");
        }

        // ------------------------------------------------------------------ 1. MenuScrollMath

        private static void VerifyScrollMath()
        {
            Check(Approx(MenuScrollMath.ItemsPerSecond(0f), 0f), "ItemsPerSecond(0°) == 0");
            Check(Approx(MenuScrollMath.ItemsPerSecond(9f), 0f), "ItemsPerSecond(9°) == 0 (zona muerta)");
            Check(Approx(MenuScrollMath.ItemsPerSecond(10f), 0f), "ItemsPerSecond(10°) == 0 (borde de la zona muerta)");
            Check(Approx(MenuScrollMath.ItemsPerSecond(50f), 2f), "ItemsPerSecond(50°) == 2 (lineal entre 10° y 90°)");
            Check(Approx(MenuScrollMath.ItemsPerSecond(90f), 4f), "ItemsPerSecond(90°) == 4");
            Check(Approx(MenuScrollMath.ItemsPerSecond(300f), 4f), "ItemsPerSecond(300°) == 4 (saturado)");
            Check(Approx(MenuScrollMath.ItemsPerSecond(450f), 4f), "ItemsPerSecond(450°) == 4 (tope del volante)");
            Check(Approx(MenuScrollMath.ItemsPerSecond(-90f), -4f), "ItemsPerSecond(-90°) == -4");
            Check(Approx(MenuScrollMath.ItemsPerSecond(-50f), -2f), "ItemsPerSecond(-50°) == -2");
            Check(MenuScrollMath.ItemsPerSecond(30f) > 0f, "Derecha (+) = bajar (rate positivo)");
            Check(MenuScrollMath.ItemsPerSecond(-30f) < 0f, "Izquierda (-) = subir (rate negativo)");
            Check(Approx(MenuScrollMath.ItemsPerSecond(60f, 20f, 100f, 8f), 4f), "Parámetros configurables: (60°, zona 20°, tope 100°, 8/s) == 4");
        }

        // ------------------------------------------------------------------ 2. Acumulador

        private static void VerifyAccumulator()
        {
            var acc = new MenuScrollAccumulator();
            Check(acc.Step(0f, 0.1f) == 0, "Acumulador: en la zona muerta no da pasos");

            Check(acc.Step(4f, 0.125f) == 1, "Acumulador: paso inmediato al salir de la zona muerta (derecha)");
            Check(Approx(acc.Remainder, 0f), "Acumulador: el dt del frame inicial no se acumula");

            // Regla: 1 paso inmediato + 4 acumulados a 4 ítems/s durante 1 s (8 frames de 0,125 s) = 5 pasos.
            int total = 1;
            for (int i = 0; i < 8; i++) total += acc.Step(4f, 0.125f);
            Check(total == 5, $"Acumulador: a 4 ítems/s durante 1 s da 1 inmediato + 4 acumulados = 5 (dio {total})");

            // Volver a la zona muerta reinicia.
            acc.Step(4f, 0.125f);              // acumula 0,5
            Check(acc.Step(0f, 0.125f) == 0 && acc.Sign == 0 && Approx(acc.Remainder, 0f), "Acumulador: volver a la zona muerta reinicia signo y resto");
            Check(acc.Step(4f, 0.125f) == 1, "Acumulador: tras reiniciar, vuelve a dar el paso inmediato");

            // Cambiar de signo reinicia: paso inmediato en la nueva dirección, sin arrastrar el resto.
            acc.Step(4f, 0.125f);              // resto 0,5 hacia abajo
            Check(acc.Step(-4f, 0.125f) == -1, "Acumulador: cambiar de signo da un paso inmediato hacia el otro lado");
            Check(Approx(acc.Remainder, 0f) && acc.Sign == -1, "Acumulador: cambiar de signo descarta el resto anterior");
            Check(acc.Step(-4f, 0.25f) == -1, "Acumulador: hacia la izquierda acumula pasos negativos (4/s * 0,25 s = 1)");

            // Rate fraccionario: 2 ítems/s con dt 0,25 → 0,5 por frame: un paso cada dos frames.
            var frac = new MenuScrollAccumulator();
            frac.Step(2f, 0.25f);
            Check(frac.Step(2f, 0.25f) == 0 && frac.Step(2f, 0.25f) == 1, "Acumulador: rate fraccionario conserva el resto entre frames");
        }

        // ------------------------------------------------------------------ 3. LessonListPanel

        private static void VerifyLessonList()
        {
            var lessons = LoadLessons();
            Check(lessons.Count == 4, $"Existen LessonData_01..04 (hay {lessons.Count})");

            var canvasGo = new GameObject("TMP_MainMenuVerification_Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var listGo = new GameObject("List", typeof(RectTransform));
                listGo.transform.SetParent(canvasGo.transform, false);
                var panel = listGo.AddComponent<LessonListPanel>();
                SetLessons(panel, lessons);

                Check(panel.Count == 4, "LessonListPanel: arma 4 ítems");
                bool titlesOk = true;
                for (int i = 0; i < lessons.Count; i++)
                {
                    titlesOk &= panel.GetItemTitle(i) == lessons[i].lessonTitle;
                }
                Check(titlesOk, "LessonListPanel: cada ítem muestra el lessonTitle de LessonData_01..04 en orden");
                Check(panel.CursorIndex == 0, "LessonListPanel: el cursor empieza en 0");

                Check(panel.MoveCursor(1) && panel.CursorIndex == 1, "LessonListPanel: MoveCursor(+1) baja");
                panel.MoveCursor(10);
                Check(panel.CursorIndex == 3, "LessonListPanel: se frena en Count-1 (no da la vuelta)");
                Check(!panel.MoveCursor(1) && panel.CursorIndex == 3, "LessonListPanel: MoveCursor(+1) en el último no se mueve");
                panel.MoveCursor(-10);
                Check(panel.CursorIndex == 0, "LessonListPanel: se frena en 0 (no da la vuelta)");
                Check(!panel.MoveCursor(-1) && panel.CursorIndex == 0, "LessonListPanel: MoveCursor(-1) en el primero no se mueve");

                panel.MarkSelected(2);
                Check(panel.IsItemMarked(2) && !panel.IsItemMarked(0) && !panel.IsItemMarked(1) && !panel.IsItemMarked(3) && panel.SelectedIndex == 2,
                    "LessonListPanel: MarkSelected(2) marca solo el ítem 2");

                // Ventana: si la lista no entra, el cursor se mantiene visible.
                var so = new SerializedObject(panel);
                so.FindProperty("m_MaxVisibleItems").intValue = 2;
                so.ApplyModifiedProperties();
                panel.Rebuild();
                panel.SetCursor(3);
                Check(panel.IsItemVisible(3) && !panel.IsItemVisible(0) && panel.FirstVisibleIndex == 2,
                    "LessonListPanel: con 2 filas visibles, el cursor en 3 desplaza la ventana y queda visible");
                panel.SetCursor(0);
                Check(panel.IsItemVisible(0) && !panel.IsItemVisible(3), "LessonListPanel: al volver al 0 la ventana sube");

                panel.Rebuild();
                Check(panel.Count == 4 && listGo.transform.childCount == 4, "LessonListPanel: Rebuild es idempotente (no duplica ítems)");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        // ------------------------------------------------------------------ 4. Mapeo marcha → página

        private static void VerifyPageMapping()
        {
            Check(MainMenuController.PageForGear(GearState.P) == MainMenuPage.Lessons, "Mapeo: P -> Lecciones");
            Check(MainMenuController.PageForGear(GearState.R) == MainMenuPage.Controls, "Mapeo: R -> Controles");
            Check(MainMenuController.PageForGear(GearState.N) == MainMenuPage.Calibration, "Mapeo: N -> Calibración");
            Check(MainMenuController.PageForGear(GearState.D) == MainMenuPage.Credits, "Mapeo: D -> Créditos");
        }

        // ------------------------------------------------------------------ ControllerIllustration

        private static void VerifyIllustration()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CONTROLLER_MODEL);
            Check(model != null, "Existe UniversalController.fbx");
            if (model == null) return;

            // Material compartido del asset (Bumper): no debe cambiar.
            Material sharedBumper = null;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Bumper") sharedBumper = r.sharedMaterial;
            }
            Check(sharedBumper != null, "El modelo tiene la pieza Bumper (grip)");
            Color sharedColorBefore = sharedBumper != null ? sharedBumper.GetColor("_BaseColor") : Color.clear;

            foreach (string piece in new[] { "Bumper", "Trigger", "Button_Home" })
            {
                var go = new GameObject("TMP_Illustration_" + piece, typeof(RectTransform));
                try
                {
                    var ill = go.AddComponent<ControllerIllustration>();
                    var so = new SerializedObject(ill);
                    so.FindProperty("m_ControllerModel").objectReferenceValue = model;
                    so.FindProperty("m_PieceName").stringValue = piece;
                    so.ApplyModifiedProperties();
                    ill.Build();

                    Check(ill.ControllerTransform != null, $"Ilustración {piece}: instancia el modelo del control");
                    Check(ill.HighlightedRenderer != null && ill.HighlightedRenderer.name == piece, $"Ilustración {piece}: encuentra la pieza");
                    Material mat = ill.HighlightedMaterial;
                    Check(mat != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)) && mat != sharedBumper,
                        $"Ilustración {piece}: pinta con un material instanciado (no el del asset)");
                    Check(mat != null && mat.IsKeywordEnabled("_EMISSION") && mat.GetColor("_EmissionColor").maxColorComponent > 0.1f,
                        $"Ilustración {piece}: la pieza tiene emisión de resaltado");
                    Check(!ill.PressAnimation && ill.ButtonTransform == null, $"Ilustración {piece}: sin modo Pulsar no hay botón 3D");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            Check(sharedBumper == null || sharedBumper.GetColor("_BaseColor") == sharedColorBefore, "Los materiales del asset del control no se modificaron");

            // Modo Pulsar
            var pressGo = new GameObject("TMP_Illustration_Press", typeof(RectTransform));
            try
            {
                var ill = pressGo.AddComponent<ControllerIllustration>();
                var so = new SerializedObject(ill);
                so.FindProperty("m_ControllerModel").objectReferenceValue = model;
                so.FindProperty("m_PieceName").stringValue = string.Empty;
                so.FindProperty("m_PressAnimation").boolValue = true;
                so.FindProperty("m_CycleSeconds").floatValue = 4f;
                so.ApplyModifiedProperties();
                ill.Build();

                Check(ill.ButtonTransform != null && ill.ControllerTransform != null, "Pulsar: hay control y botón 3D (cilindro)");
                Check(ill.HighlightedRenderer == null, "Pulsar: sin pieza configurada no pinta ninguna");

                ill.EvaluateAt(0f);
                float restButtonZ = ill.ButtonTransform.localPosition.z;
                float restControllerZ = ill.ControllerTransform.localPosition.z;
                ill.EvaluateAt(0.30f * 4f); // en pleno hundimiento
                float pressedButtonZ = ill.ButtonTransform.localPosition.z;
                float pressedControllerZ = ill.ControllerTransform.localPosition.z;
                ill.EvaluateAt(0.9f * 4f);  // esperando
                float waitButtonZ = ill.ButtonTransform.localPosition.z;
                float waitControllerZ = ill.ControllerTransform.localPosition.z;

                Check(Mathf.Abs(pressedButtonZ - restButtonZ) > 0.005f, "Pulsar: el botón se hunde visiblemente (>5 mm del modelo)");
                Check(Mathf.Abs(pressedControllerZ - restControllerZ) > 0.05f, "Pulsar: el control se acerca al botón");
                Check(Mathf.Abs(waitButtonZ - restButtonZ) < EPS && Mathf.Abs(waitControllerZ - restControllerZ) < EPS,
                    "Pulsar: tras soltar vuelve al reposo y espera");
            }
            finally
            {
                Object.DestroyImmediate(pressGo);
            }
        }

        // ------------------------------------------------------------------ Car09: prefab

        private static void VerifyCarPrefabFlags()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CAR_PREFAB);
            Check(prefab != null, "Existe Car09.prefab");
            if (prefab == null) return;

            VRGearShifter shifter = prefab.GetComponentInChildren<VRGearShifter>(true);
            StartButton button = prefab.GetComponentInChildren<StartButton>(true);
            Check(shifter != null && !shifter.IgnoreDrivingRestrictions, "Prefab Car09: VRGearShifter.m_IgnoreDrivingRestrictions == false (sin cambios)");
            Check(button != null && button.TogglesEngine, "Prefab Car09: StartButton.m_TogglesEngine == true (sin cambios)");
        }

        // ------------------------------------------------------------------ 5 y 6. VRGearShifter / StartButton

        private static void VerifyCarInstanceFlags()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CAR_PREFAB);
            if (prefab == null) return;

            GameObject instance = Object.Instantiate(prefab);
            instance.name = "Car09_MainMenuVerification";
            try
            {
                var vehicle = instance.GetComponent<VehicleController>();
                var shifter = instance.GetComponentInChildren<VRGearShifter>(true);
                var button = instance.GetComponentInChildren<StartButton>(true);
                Check(vehicle != null && shifter != null && button != null, "Instancia Car09: VehicleController, VRGearShifter y StartButton presentes");
                if (vehicle == null || shifter == null || button == null) return;

                // Awake no corre en Edit Mode: se cablean las referencias a mano.
                SetObjectField(shifter, "m_VehicleController", vehicle);
                SetObjectField(button, "m_VehicleController", vehicle);
                Check(!vehicle.IsEngineRunning, "Instancia Car09: motor apagado al empezar");

                // --- VRGearShifter
                SetBoolField(shifter, "m_IgnoreDrivingRestrictions", false);
                (float min, float max) restricted = InvokeAllowedRange(shifter);
                float angleP = shifter.GetAngleForGear(GearState.P);
                Check(Approx(restricted.min, angleP) && Approx(restricted.max, angleP), "VRGearShifter (false): con el motor apagado queda clavado en P");
                Check(!InvokeCanShiftTo(shifter, GearState.R) && !InvokeCanShiftTo(shifter, GearState.N) && !InvokeCanShiftTo(shifter, GearState.D),
                    "VRGearShifter (false): CanShiftTo rechaza R, N y D con el motor apagado");

                SetBoolField(shifter, "m_IgnoreDrivingRestrictions", true);
                (float min, float max) free = InvokeAllowedRange(shifter);
                float lo = Mathf.Min(shifter.forwardLimit, shifter.backwardLimit);
                float hi = Mathf.Max(shifter.forwardLimit, shifter.backwardLimit);
                Check(shifter.IgnoreDrivingRestrictions, "VRGearShifter: la propiedad pública refleja el flag");
                Check(Approx(free.min, lo) && Approx(free.max, hi), "VRGearShifter (true): rango completo con el motor apagado");
                Check(InvokeCanShiftTo(shifter, GearState.R) && InvokeCanShiftTo(shifter, GearState.N) && InvokeCanShiftTo(shifter, GearState.D),
                    "VRGearShifter (true): CanShiftTo acepta R, N y D con el motor apagado");

                // --- StartButton
                int toggleRequests = 0;
                int pressedEvents = 0;
                vehicle.OnEngineToggleRequested += () => toggleRequests++;
                button.Pressed += () => pressedEvents++;

                SetBoolField(button, "m_TogglesEngine", false);
                button.Press();
                Check(pressedEvents == 1, "StartButton (m_TogglesEngine=false): Press() dispara Pressed");
                Check(toggleRequests == 0 && !vehicle.IsEngineRunning, "StartButton (m_TogglesEngine=false): el motor no se toca");

                SetBoolField(button, "m_TogglesEngine", true);
                button.Press();
                Check(toggleRequests == 1, "StartButton (m_TogglesEngine=true): Press() sigue llamando a TryToggleEngine (regresión)");
                Check(pressedEvents == 2, "StartButton (m_TogglesEngine=true): Pressed también se dispara");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ------------------------------------------------------------------ 7, 8 y 9. Escena MainMenu

        private static void VerifyScene(Scene scene)
        {
            Check(scene.IsValid() && scene.isLoaded, $"Existe y carga {MainMenuSetup.SCENE_PATH}");
            if (!scene.IsValid() || !scene.isLoaded) return;

            Check(FindAll<LessonManager>(scene).Count == 0, "MainMenu.unity: no tiene LessonManager");
            Check(FindAll<LessonHUDPanel>(scene).TrueForAll(h => !h.gameObject.activeInHierarchy), "MainMenu.unity: el HUD de lección no está activo");

            var controller = FindFirst<MainMenuController>(scene);
            var car = FindFirst<VehicleController>(scene);
            var menu = FindFirst<InGameMenuController>(scene);
            var shifter = FindFirst<VRGearShifter>(scene);
            var startButton = FindFirst<StartButton>(scene);
            var highlighter = FindFirst<InteractableHighlighter>(scene);
            Check(controller != null && car != null && menu != null && shifter != null && startButton != null,
                "MainMenu.unity: MainMenuController, Car09, InGameMenuController, VRGearShifter y StartButton presentes");
            Check(highlighter != null, "MainMenu.unity: hay un InteractableHighlighter");
            if (controller == null || car == null || menu == null || shifter == null || startButton == null) return;

            // Referencias del MainMenuController
            var so = new SerializedObject(controller);
            var missing = new List<string>();
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (!it.name.StartsWith("m_") || it.name == "m_Script") continue;
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null) missing.Add(it.name);
                if (it.isArray && it.propertyType == SerializedPropertyType.Generic)
                {
                    for (int i = 0; i < it.arraySize; i++)
                    {
                        if (it.GetArrayElementAtIndex(i).propertyType == SerializedPropertyType.ObjectReference &&
                            it.GetArrayElementAtIndex(i).objectReferenceValue == null) missing.Add($"{it.name}[{i}]");
                    }
                }
            }
            Check(missing.Count == 0, "MainMenuController: todas las referencias asignadas" + (missing.Count > 0 ? " (faltan: " + string.Join(", ", missing) + ")" : string.Empty));

            // Canvas
            GameObject canvasGo = FindByName(scene, MainMenuSetup.CANVAS_NAME);
            Check(canvasGo != null, "MainMenu.unity: existe MainMenu_Canvas");
            Transform head = FindHead(scene);
            if (canvasGo != null && head != null)
            {
                Check(canvasGo.layer == LayerMask.NameToLayer("UI"), "MainMenu_Canvas está en la capa UI");
                Check(canvasGo.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace, "MainMenu_Canvas es World Space");
                float forwardDot = Vector3.Dot(canvasGo.transform.position - car.transform.position, car.GetForwardVector().normalized);
                float distance = Vector3.Distance(canvasGo.transform.position, head.position);
                Check(forwardDot > 0f, $"MainMenu_Canvas está delante del auto (dot con el forward = {forwardDot:F2})");
                Check(distance >= 2f && distance <= 4f, $"MainMenu_Canvas está a 2-4 m de los ojos del conductor ({distance:F2} m)");
                Vector3 toCanvas = canvasGo.transform.position - head.position;
                Check(Mathf.Abs(toCanvas.y) < 0.3f, $"MainMenu_Canvas está a la altura de los ojos (dy = {toCanvas.y:F2} m)");
                Check(Vector3.Dot(canvasGo.transform.forward, toCanvas.normalized) > 0.9f, "MainMenu_Canvas mira hacia el conductor (su cara legible apunta a la cabeza)");
            }

            // Overrides de la instancia de Car09
            Check(shifter.IgnoreDrivingRestrictions, "Escena: VRGearShifter.m_IgnoreDrivingRestrictions == true");
            Check(!startButton.TogglesEngine, "Escena: StartButton.m_TogglesEngine == false");
            Check(!menu.MenuButtonEnabled, "Escena: InGameMenuController.m_MenuButtonEnabled == false");
            var soMenu = new SerializedObject(menu);
            Check(soMenu.FindProperty("m_CalibrationBackButton").objectReferenceValue != null, "Escena: InGameMenuController.m_CalibrationBackButton asignado");

            // Controlador (solo cambios en memoria: la escena se cierra sin guardar)
            try
            {
                VerifyMenuController(scene, controller, car, menu, shifter, startButton);
                VerifyInGameMenu(scene, menu);
            }
            finally
            {
                InteractableHighlighter.RestoreForcedEmissionKeywords();
            }
        }

        private static void VerifyMenuController(Scene scene, MainMenuController controller, VehicleController car, InGameMenuController menu,
            VRGearShifter shifter, StartButton startButton)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (msg, stack, type) => { if (type == LogType.Log) logs.Add(msg); };
            Application.logMessageReceived += capture;
            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;
            try
            {
                controller.Restart();
                var list = FindFirst<LessonListPanel>(scene);

                Check(controller.CurrentPage == MainMenuPage.Splash, "MainMenuController: arranca en Splash");
                Check(!car.IsEngineRunning, "MainMenuController: motor apagado al arrancar");
                var body = car.GetComponent<Rigidbody>();
                Check(body != null && body.constraints == RigidbodyConstraints.FreezeAll, "MainMenuController: Rigidbody del auto en FreezeAll");

                // Durante el Splash la palanca no cambia de página: se registra y se aplica al salir.
                controller.HandleGearChanged(GearState.N);
                Check(controller.CurrentPage == MainMenuPage.Splash && controller.CurrentGear == GearState.N, "Splash: la palanca no cambia de página, solo se registra la marcha");
                controller.HandleGearChanged(GearState.P);
                Check(controller.CurrentPage == MainMenuPage.Splash, "Splash: volver a P tampoco cambia de página");

                // La ignición (Press) sale del Splash hacia la marcha actual.
                startButton.Press();
                Check(controller.CurrentPage == MainMenuPage.Lessons, "Press() en Splash -> página Lecciones (marcha P)");

                // Scroll con el volante
                controller.ApplyScroll(0f, 0.016f);
                controller.ApplyScroll(90f, 0.25f);
                Check(controller.CursorIndex == 1, "Volante +90°: paso inmediato, el cursor baja a 1");
                controller.ApplyScroll(90f, 0.25f);
                Check(controller.CursorIndex == 2, "Volante +90° 0,25 s: 4 ítems/s * 0,25 s = 1 paso más (cursor 2)");
                controller.ApplyScroll(0f, 0.016f);
                controller.ApplyScroll(5f, 1f);
                Check(controller.CursorIndex == 2, "Volante dentro de la zona muerta: el cursor no se mueve");
                controller.ApplyScroll(-90f, 0.25f);
                Check(controller.CursorIndex == 1, "Volante -90°: el cursor sube");
                controller.ApplyScroll(0f, 0.016f);

                // Ignición en Lecciones = selección placeholder
                int cursor = controller.CursorIndex;
                string expectedId = list != null ? list.GetLesson(cursor).lessonId : "?";
                logs.Clear();
                startButton.Press();
                Check(logs.Exists(l => l.Contains("[MainMenu] Lección seleccionada: " + expectedId)), $"Press() en Lecciones loguea la selección del ítem {cursor} ({expectedId})");
                Check(controller.SelectedLessonIndex == cursor && list.IsItemMarked(cursor), "Press() en Lecciones marca el ítem como Seleccionada");
                Check(!car.IsEngineRunning, "Press() en Lecciones no enciende el motor");

                // Controles: la ignición no hace nada
                controller.HandleGearChanged(GearState.R);
                Check(controller.CurrentPage == MainMenuPage.Controls, "Palanca en R -> página Controles");
                logs.Clear();
                startButton.Press();
                Check(!logs.Exists(l => l.Contains("Lección seleccionada")) && controller.CurrentPage == MainMenuPage.Controls, "Press() en Controles no hace nada");

                // Calibración
                controller.HandleGearChanged(GearState.N);
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen, "Palanca en N -> Calibración abre la calibración standalone");
                Check(Time.timeScale == timeScaleBefore && AudioListener.pause == audioPauseBefore, "Calibración standalone no pausa el tiempo ni el audio");
                controller.HandleGearChanged(GearState.D);
                Check(controller.CurrentPage == MainMenuPage.Credits && !menu.IsOpen, "Salir de N -> Créditos cierra la calibración");
                controller.HandleGearChanged(GearState.N);
                controller.HandleGearChanged(GearState.P);
                Check(controller.CurrentPage == MainMenuPage.Lessons && !menu.IsOpen, "Volver a P cierra la calibración y muestra Lecciones");

                // Motor encendido por otra vía → se vuelve a apagar enseguida
                car.SetEngineRunning(true);
                Check(!car.IsEngineRunning, "Si el motor llega a encenderse por otra vía, se vuelve a apagar");
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
        }

        private static void VerifyInGameMenu(Scene scene, InGameMenuController menu)
        {
            var so = new SerializedObject(menu);
            var mainPanel = so.FindProperty("m_MainPanel").objectReferenceValue as GameObject;
            var calibrationPanel = so.FindProperty("m_CalibrationPanel").objectReferenceValue as GameObject;
            var backButton = so.FindProperty("m_CalibrationBackButton").objectReferenceValue as GameObject;
            var head = so.FindProperty("m_HeadTransform").objectReferenceValue as Transform;
            float distance = so.FindProperty("m_DistanceFromHead").floatValue;
            if (head == null) head = FindHead(scene);
            Check(mainPanel != null && calibrationPanel != null && backButton != null && head != null,
                "InGameMenuController: referencias de paneles, botón Volver y cabeza disponibles");
            if (mainPanel == null || calibrationPanel == null || backButton == null || head == null) return;

            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;

            menu.CloseMenu();
            menu.OpenCalibrationStandalone();
            Check(menu.IsOpen, "OpenCalibrationStandalone: abre el menú");
            Check(Time.timeScale == timeScaleBefore && AudioListener.pause == audioPauseBefore && !menu.IsSimulationPaused,
                "OpenCalibrationStandalone: Time.timeScale y AudioListener.pause no cambian");
            Check(calibrationPanel.activeSelf && !mainPanel.activeSelf, "OpenCalibrationStandalone: solo el panel de calibración está activo");
            Check(!backButton.activeSelf, "OpenCalibrationStandalone: el botón Volver está oculto");

            Vector3 toMenu = menu.MenuRoot.transform.position - head.position;
            Vector3 headForward = head.forward; headForward.y = 0f;
            Vector3 flat = toMenu; flat.y = 0f;
            float yaw = Vector3.SignedAngle(headForward, flat, Vector3.up);
            Check(Mathf.Abs(yaw - 45f) <= 2f, $"OpenCalibrationStandalone: a 45° (±2°) a la derecha del frente de la cabeza (yaw = {yaw:F1}°)");
            Check(Mathf.Abs(toMenu.magnitude - distance) < 0.01f, $"OpenCalibrationStandalone: a {distance:F1} m de la cabeza ({toMenu.magnitude:F2} m)");
            Check(Mathf.Abs(toMenu.y) < 0.001f, "OpenCalibrationStandalone: a la altura de los ojos");
            Vector3 faceDir = menu.MenuRoot.transform.forward;
            Check(Vector3.Dot(faceDir, toMenu.normalized) > 0.99f, "OpenCalibrationStandalone: el panel mira a la cabeza (misma convención que OpenMenu)");

            menu.CloseMenu();
            Check(!menu.IsOpen && backButton.activeSelf, "CloseMenu: cierra y restaura el botón Volver");
            Check(Time.timeScale == timeScaleBefore && AudioListener.pause == audioPauseBefore, "CloseMenu: no deja el tiempo/audio alterados");

            // m_MenuButtonEnabled: opt-in. En la escena está apagado; por defecto (otras escenas) sigue encendido.
            Check(!menu.MenuButtonEnabled, "InGameMenuController (escena): m_MenuButtonEnabled == false");
            var defaultGo = new GameObject("TMP_DefaultInGameMenu");
            try
            {
                Check(defaultGo.AddComponent<InGameMenuController>().MenuButtonEnabled, "InGameMenuController (por defecto): m_MenuButtonEnabled == true (regresión)");
            }
            finally
            {
                Object.DestroyImmediate(defaultGo);
            }
            // Que Escape y el botón Menu no abren nada se prueba en Play Mode (wasPressedThisFrame no funciona en Edit Mode).
        }

        // ------------------------------------------------------------------ helpers

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

        private static void SetLessons(LessonListPanel panel, List<LessonDefinitionSO> lessons)
        {
            var so = new SerializedObject(panel);
            SerializedProperty prop = so.FindProperty("m_Lessons");
            prop.arraySize = lessons.Count;
            for (int i = 0; i < lessons.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = lessons[i];
            so.ApplyModifiedProperties();
        }

        private static void SetObjectField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static void SetBoolField(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedProperties();
        }

        private static (float min, float max) InvokeAllowedRange(VRGearShifter shifter)
        {
            MethodInfo m = typeof(VRGearShifter).GetMethod("ComputeAllowedAngleRange", BindingFlags.Instance | BindingFlags.NonPublic);
            return ((float, float))m.Invoke(shifter, null);
        }

        private static bool InvokeCanShiftTo(VRGearShifter shifter, GearState target)
        {
            MethodInfo m = typeof(VRGearShifter).GetMethod("CanShiftTo", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)m.Invoke(shifter, new object[] { target });
        }

        private static List<T> FindAll<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return result;
        }

        private static T FindFirst<T>(Scene scene) where T : Component
        {
            List<T> all = FindAll<T>(scene);
            return all.Count > 0 ? all[0] : null;
        }

        private static GameObject FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            return null;
        }

        private static Transform FindHead(Scene scene)
        {
            foreach (Camera cam in FindAll<Camera>(scene))
            {
                if (cam.CompareTag("MainCamera") && cam.transform.parent != null) return cam.transform;
            }
            return null;
        }

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < EPS;

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
                Debug.LogError($"  [FAIL] {testName}");
            }
        }
    }
}
