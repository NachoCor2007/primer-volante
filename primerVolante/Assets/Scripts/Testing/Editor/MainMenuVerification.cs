using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PrimerVolante.Lessons;
using PrimerVolante.MainMenu;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private const int LESSON_COUNT = 6; // LessonData_01..06 (lecciones del menú)
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

            // Los objetos temporales de las pruebas se crean en una escena de trabajo aditiva y vacía (activa mientras
            // corre la verificación): así no se ensucia la escena que tenga abierta el usuario, p. ej. MainMenu.
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);
            try
            {
                VerifyScrollMath();
                VerifyAccumulator();
                VerifyLessonList();
                VerifyPageMapping();
                VerifyIllustration();
                VerifyCarPrefabFlags();
                VerifyCarInstanceFlags();
                // Solo lectura: puede usar la escena abierta. Lo que muta objetos corre sobre una copia temporal.
                VerificationSceneUtil.WithScene(MainMenuSetup.SCENE_PATH, VerifyScene);
                VerificationSceneUtil.WithSceneCopy(MainMenuSetup.SCENE_PATH, VerifyMutatingScene);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (scratch.IsValid()) EditorSceneManager.CloseScene(scratch, true);
            }

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
            Check(!acc.Armed && acc.Step(4f, 0.1f) == 0, "Acumulador: recién creado está desarmado y no da pasos fuera de la zona muerta");
            Check(acc.Step(0f, 0.1f) == 0 && acc.Armed, "Acumulador: en la zona muerta no da pasos y queda armado");

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
            frac.Step(0f, 0.25f);
            frac.Step(2f, 0.25f);
            Check(frac.Step(2f, 0.25f) == 0 && frac.Step(2f, 0.25f) == 1, "Acumulador: rate fraccionario conserva el resto entre frames");

            const float DEAD = MenuScrollMath.DefaultDeadZoneDegrees;
            const float REL = MenuScrollMath.DefaultReleaseDegrees;
            const float FULL = MenuScrollMath.DefaultFullAngleDegrees;
            const float MAX = MenuScrollMath.DefaultMaxItemsPerSecond;
            Check(Approx(REL, 7f), "Histéresis: el ángulo de liberación por defecto es 7°");

            // Histéresis: un temblor 9,5° <-> 10,5° da un solo paso.
            var jitter = new MenuScrollAccumulator();
            int jitterSteps = jitter.StepAngle(0f, 0.016f, DEAD, REL, FULL, MAX);
            for (int i = 0; i < 40; i++)
            {
                jitterSteps += jitter.StepAngle(i % 2 == 0 ? 10.5f : 9.5f, 0.016f, DEAD, REL, FULL, MAX);
            }
            Check(jitterSteps == 1, $"Histéresis: temblor 9,5°<->10,5° da un solo paso (dio {jitterSteps})");

            // Entre 7° y 10° ya activo: ni avanza ni reinicia. Bajo 7° libera y el siguiente cruce vuelve a dar el paso inmediato.
            var hyst = new MenuScrollAccumulator();
            hyst.StepAngle(0f, 0.016f, DEAD, REL, FULL, MAX);
            Check(hyst.StepAngle(12f, 0.016f, DEAD, REL, FULL, MAX) == 1, "Histéresis: al superar 10° da el paso inmediato");
            Check(hyst.StepAngle(8f, 0.016f, DEAD, REL, FULL, MAX) == 0 && hyst.Sign == 1, "Histéresis: entre 7° y 10° ya activo se mantiene sin reiniciar");
            Check(hyst.StepAngle(12f, 0.016f, DEAD, REL, FULL, MAX) == 0, "Histéresis: volver a >10° sin haber bajado de 7° no repite el paso inmediato");
            hyst.StepAngle(6f, 0.016f, DEAD, REL, FULL, MAX);
            Check(hyst.Sign == 0, "Histéresis: bajo 7° vuelve a la zona muerta");
            Check(hyst.StepAngle(12f, 0.016f, DEAD, REL, FULL, MAX) == 1, "Histéresis: tras liberar, el siguiente cruce de 10° da un paso inmediato");

            // Inactivo, entre 7° y 10° es zona muerta (no hay pasos): la histéresis solo aplica una vez activo.
            var inactive = new MenuScrollAccumulator();
            inactive.StepAngle(0f, 0.016f, DEAD, REL, FULL, MAX);
            Check(inactive.StepAngle(8.5f, 0.016f, DEAD, REL, FULL, MAX) == 0 && inactive.Sign == 0, "Histéresis: sin estar activo, 8,5° no da pasos");

            // Rearme: entrar con el volante en 30° da 0 pasos hasta volver al centro.
            var rearm = new MenuScrollAccumulator();
            int rearmSteps = 0;
            for (int i = 0; i < 10; i++) rearmSteps += rearm.StepAngle(30f, 0.1f, DEAD, REL, FULL, MAX);
            Check(rearmSteps == 0 && !rearm.Armed, "Rearme: con el volante ya en 30° no da ningún paso");
            rearm.StepAngle(0f, 0.016f, DEAD, REL, FULL, MAX);
            Check(rearm.Armed && rearm.StepAngle(30f, 0.1f, DEAD, REL, FULL, MAX) == 1, "Rearme: tras pasar por el centro vuelve a dar pasos");
            rearm.Reset();
            Check(!rearm.Armed && rearm.StepAngle(30f, 0.1f, DEAD, REL, FULL, MAX) == 0, "Rearme: Reset (cambio de página) desarma de nuevo");
        }

        // ------------------------------------------------------------------ 3. LessonListPanel

        private static void VerifyLessonList()
        {
            var lessons = LoadLessons();
            Check(lessons.Count == LESSON_COUNT, $"Existen LessonData_01..06 (hay {lessons.Count})");

            var canvasGo = new GameObject("TMP_MainMenuVerification_Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var listGo = new GameObject("List", typeof(RectTransform));
                listGo.transform.SetParent(canvasGo.transform, false);
                var panel = listGo.AddComponent<LessonListPanel>();
                SetLessons(panel, lessons);

                Check(panel.Count == LESSON_COUNT, "LessonListPanel: arma 6 ítems");
                bool titlesOk = true;
                for (int i = 0; i < lessons.Count; i++)
                {
                    titlesOk &= panel.GetItemTitle(i) == lessons[i].lessonTitle;
                }
                Check(titlesOk, "LessonListPanel: cada ítem muestra el lessonTitle de LessonData_01..06 en orden");
                Check(panel.CursorIndex == 0, "LessonListPanel: el cursor empieza en 0");

                Check(panel.MoveCursor(1) && panel.CursorIndex == 1, "LessonListPanel: MoveCursor(+1) baja");
                panel.MoveCursor(10);
                Check(panel.CursorIndex == LESSON_COUNT - 1, "LessonListPanel: se frena en Count-1 (no da la vuelta)");
                Check(!panel.MoveCursor(1) && panel.CursorIndex == LESSON_COUNT - 1, "LessonListPanel: MoveCursor(+1) en el último no se mueve");
                panel.MoveCursor(-10);
                Check(panel.CursorIndex == 0, "LessonListPanel: se frena en 0 (no da la vuelta)");
                Check(!panel.MoveCursor(-1) && panel.CursorIndex == 0, "LessonListPanel: MoveCursor(-1) en el primero no se mueve");

                panel.MarkSelected(2);
                Check(panel.IsItemMarked(2) && !panel.IsItemMarked(0) && !panel.IsItemMarked(1) && !panel.IsItemMarked(3) && panel.SelectedIndex == 2,
                    "LessonListPanel: MarkSelected(2) marca solo el ítem 2");

                // Deslizamiento: si la lista no entra en el contenedor, el contenido se corre para mantener visible el cursor.
                // Contenedor de 340 px con filas de 150 y espacio 24: entran 2 filas enteras y asoman 16 px de la tercera.
                var layout = listGo.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                layout.spacing = 24f;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
                ((RectTransform)listGo.transform).sizeDelta = new Vector2(800f, 340f);
                panel.Rebuild();
                Check(Approx(panel.TargetScrollOffset, 0f) && panel.IsItemVisible(0) && panel.IsItemVisible(1) && !panel.IsItemVisible(2),
                    "LessonListPanel: en un contenedor de 2 filas, arriba se ven enteras la 0 y la 1 (la 2 asoma)");
                panel.SetCursor(3);
                Check(panel.IsItemVisible(3) && !panel.IsItemVisible(0) && panel.FirstVisibleIndex == 2,
                    "LessonListPanel: el cursor en 3 desliza el contenido y queda visible (primera entera: 2)");
                Check(Approx(panel.TargetScrollOffset, 3 * 174f + 150f - 340f) && layout.padding.top == -Mathf.RoundToInt(panel.TargetScrollOffset),
                    $"LessonListPanel: desplazamiento mínimo para ver el cursor (padding.top = {layout.padding.top})");
                panel.SetCursor(LESSON_COUNT - 1);
                float maxOffset = LESSON_COUNT * 150f + (LESSON_COUNT - 1) * 24f - 340f;
                Check(Approx(panel.TargetScrollOffset, maxOffset) && panel.IsItemVisible(LESSON_COUNT - 1),
                    "LessonListPanel: en la última lección el desplazamiento se frena al final del contenido");
                panel.SetCursor(0);
                Check(panel.IsItemVisible(0) && !panel.IsItemVisible(3) && Approx(panel.TargetScrollOffset, 0f) && layout.padding.top == 0,
                    "LessonListPanel: al volver al 0 el contenido sube hasta arriba");

                panel.Rebuild();
                Check(panel.Count == LESSON_COUNT && listGo.transform.childCount == LESSON_COUNT, "LessonListPanel: Rebuild es idempotente (no duplica ítems)");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }

        // La lista de la escena: las 6 lecciones, cada una con escena en el build, y todas las filas dentro del contenedor.
        private static void VerifySceneLessonList(Scene scene)
        {
            var panel = FindFirst<LessonListPanel>(scene);
            Check(panel != null, "MainMenu.unity: hay un LessonListPanel");
            if (panel == null) return;

            var expected = LoadLessons();
            bool sameLessons = panel.Lessons.Count == expected.Count;
            for (int i = 0; sameLessons && i < expected.Count; i++) sameLessons = panel.Lessons[i] == expected[i];
            Check(sameLessons, $"MainMenu.unity: la lista tiene LessonData_01..06 en orden (hay {panel.Lessons.Count})");

            var buildScenes = new HashSet<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.enabled) buildScenes.Add(System.IO.Path.GetFileNameWithoutExtension(s.path));
            }
            bool allMapped = true;
            string missing = string.Empty;
            foreach (LessonDefinitionSO lesson in panel.Lessons)
            {
                bool ok = lesson != null && LessonSceneFlowManager.LessonScenes.TryGetValue(lesson.lessonId, out string sceneName) && buildScenes.Contains(sceneName);
                if (!ok) { allMapped = false; missing += (lesson != null ? lesson.lessonId : "null") + " "; }
            }
            Check(allMapped, $"Cada lección de la lista tiene escena mapeada y habilitada en Build Settings {missing}");

            var so = new SerializedObject(panel);
            float itemHeight = so.FindProperty("m_ItemHeight").floatValue;
            var container = so.FindProperty("m_Container").objectReferenceValue as RectTransform;
            if (container == null) container = (RectTransform)panel.transform;
            Check(Approx(itemHeight, 170f), $"Lista: las filas mantienen su alto de 170 px (hay {itemHeight:F0})");
            Check(container.GetComponent<VerticalLayoutGroup>() != null, "Lista: el contenedor tiene VerticalLayoutGroup (el deslizamiento corre su padding)");
            Check(container.GetComponent<RectMask2D>() != null, "Lista: el contenedor tiene RectMask2D (recorta las filas fuera de la vista)");
            Canvas.ForceUpdateCanvases();
            Check(container.rect.height >= itemHeight, $"Lista: entra al menos una fila entera ({container.rect.height:F0} >= {itemHeight:F0} px)");
        }

        // ------------------------------------------------------------------ 4. Mapeo marcha → página

        private static void VerifyPageMapping()
        {
            Check(MainMenuController.PageForGear(GearState.P) == MainMenuPage.Lessons, "Mapeo: P -> Lecciones");
            Check(MainMenuController.PageForGear(GearState.R) == MainMenuPage.Controls, "Mapeo: R -> Controles");
            Check(MainMenuController.PageForGear(GearState.N) == MainMenuPage.Calibration, "Mapeo: N -> Calibración");
            Check(MainMenuController.PageForGear(GearState.D) == MainMenuPage.Credits, "Mapeo: D -> Créditos");

            // Una sola tabla para los dos sentidos: ida y vuelta.
            bool roundTrip = true;
            foreach (GearState gear in new[] { GearState.P, GearState.R, GearState.N, GearState.D })
            {
                roundTrip &= MainMenuController.GearForPage(MainMenuController.PageForGear(gear)) == gear;
            }
            Check(roundTrip, "Mapeo ida y vuelta: GearForPage(PageForGear(g)) == g para P, R, N y D");
            bool roundTripPages = true;
            foreach (MainMenuPage page in new[] { MainMenuPage.Lessons, MainMenuPage.Controls, MainMenuPage.Calibration, MainMenuPage.Credits })
            {
                roundTripPages &= MainMenuController.PageForGear(MainMenuController.GearForPage(page)) == page;
            }
            Check(roundTripPages, "Mapeo ida y vuelta: PageForGear(GearForPage(p)) == p para las 4 páginas");
            Check(MainMenuController.GearForPage(MainMenuPage.Splash) == GearState.P, "Mapeo: el Splash cuenta como P");
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
                    SetField(ill, "m_ControllerModel", model);
                    SetField(ill, "m_PieceName", piece);
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
                SetField(ill, "m_ControllerModel", model);
                SetField(ill, "m_PieceName", string.Empty);
                SetField(ill, "m_PressAnimation", true);
                SetField(ill, "m_CycleSeconds", 4f);
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
                int rejectedEvents = 0;
                int engineEvents = 0;
                vehicle.OnEngineToggleRequested += () => toggleRequests++;
                vehicle.OnEngineStartRejected += reason => rejectedEvents++;
                vehicle.OnEngineStateChanged += state => engineEvents++;
                button.Pressed += () => pressedEvents++;

                // m_TogglesEngine=false: no toca el motor, pero da el feedback de "aceptado" (OnEngineToggleRequested: clic + háptico), sin rechazo.
                SetBoolField(button, "m_TogglesEngine", false);
                button.Press();
                Check(pressedEvents == 1, "StartButton (m_TogglesEngine=false): Press() dispara Pressed");
                Check(!vehicle.IsEngineRunning && engineEvents == 0, "StartButton (m_TogglesEngine=false): el motor no se toca");
                Check(toggleRequests == 1 && rejectedEvents == 0, "StartButton (m_TogglesEngine=false): da el feedback de aceptado una vez y nunca el de rechazado");

                // m_TogglesEngine=true: TryToggleEngine ya dispara el feedback; no se duplica. Sin freno lo rechaza (regresión).
                toggleRequests = 0;
                SetBoolField(button, "m_TogglesEngine", true);
                button.Press();
                Check(toggleRequests == 1, "StartButton (m_TogglesEngine=true): Press() llama a TryToggleEngine y el feedback suena una sola vez (regresión)");
                Check(pressedEvents == 2, "StartButton (m_TogglesEngine=true): Pressed también se dispara");
                Check(rejectedEvents == 1, "StartButton (m_TogglesEngine=true): sin freno a fondo el arranque se rechaza como siempre");

                // --- Bloqueo del motor (VehicleController)
                Check(!vehicle.IsEngineLocked, "VehicleController: el motor no está bloqueado por defecto");
                var states = new List<EngineState>();
                vehicle.OnEngineStateChanged += s => states.Add(s);
                states.Clear();
                vehicle.SetEngineRunning(true);
                Check(vehicle.IsEngineRunning && states.Count == 1 && states[0] == EngineState.Running, "VehicleController sin bloqueo: SetEngineRunning(true) enciende (regresión)");

                states.Clear();
                vehicle.SetEngineLocked(true);
                Check(vehicle.IsEngineLocked && !vehicle.IsEngineRunning && states.Count == 1 && states[0] == EngineState.Off,
                    "VehicleController: bloquear con el motor encendido lo apaga una sola vez, de forma normal");

                states.Clear();
                rejectedEvents = 0;
                vehicle.SetEngineRunning(true);
                bool toggled = vehicle.TryToggleEngine();
                Check(!vehicle.IsEngineRunning && !toggled && states.Count == 0, "VehicleController bloqueado: SetEngineRunning(true) y TryToggleEngine no encienden ni emiten OnEngineStateChanged");
                Check(rejectedEvents == 1, "VehicleController bloqueado: TryToggleEngine rechaza con OnEngineStartRejected");

                vehicle.SetEngineLocked(false);
                vehicle.SetEngineRunning(true);
                Check(vehicle.IsEngineRunning, "VehicleController: al desbloquear vuelve a poder encenderse");
                vehicle.SetEngineRunning(false);
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

            VerifySceneLessonList(scene);

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

        }

        // Todo lo que modifica objetos (Initialize/Restart, Rigidbody, menús, motor) corre sobre una copia temporal de la escena.
        private static void VerifyMutatingScene(Scene scene)
        {
            var controller = FindFirst<MainMenuController>(scene);
            var car = FindFirst<VehicleController>(scene);
            var menu = FindFirst<InGameMenuController>(scene);
            var shifter = FindFirst<VRGearShifter>(scene);
            var startButton = FindFirst<StartButton>(scene);
            Check(controller != null && car != null && menu != null && shifter != null && startButton != null,
                "Copia de MainMenu.unity: controlador, auto, menú, palanca y botón presentes");
            if (controller == null || car == null || menu == null || shifter == null || startButton == null) return;

            try
            {
                VerifyMenuController(scene, controller, car, menu, shifter, startButton);
                VerifyGearSettle(controller, menu);
                VerifyExitOverlay(scene, controller, menu, startButton);
                VerifyExitOverlayModels(scene, controller, startButton);
                VerifyInGameMenu(scene, menu);
            }
            finally
            {
                InteractableHighlighter.RestoreForcedEmissionKeywords();
            }
        }

        // La palanca sostenida dispara OnGearChanged en cada marcha que cruza: no se deben abrir las páginas intermedias.
        private static void VerifyGearSettle(MainMenuController controller, InGameMenuController menu)
        {
            int opened = 0;
            System.Action onOpened = () => opened++;
            menu.MenuOpened += onOpened;
            try
            {
                controller.Restart();
                Check(controller.ActiveGearTabIndex == -1 && !StripVisible(controller), "Splash: la tira de marchas está oculta");
                controller.HandleGearChanged(GearState.R);
                Check(controller.CurrentPage == MainMenuPage.Splash, "Palanca: en el Splash no queda ningún cambio pendiente");
                Check(!controller.HasPendingGear, "Splash: HandleGearChanged no deja marcha pendiente");
                Check(!StripVisible(controller), "Splash: mover la palanca no muestra la tira");
                controller.HandleGearChanged(GearState.P);
                controller.HandleHazardChanged(true);
                Check(StripVisible(controller) && ActiveTab(controller) == 0 && controller.CurrentPage == MainMenuPage.Lessons,
                    "Al salir del Splash la tira y la página quedan en la marcha actual (P)");

                // Barrido P -> R -> N -> D en menos de 0,25 s: la pestaña sigue a la palanca en cada notificación,
                // pero la página (y la calibración) esperan la estabilidad.
                opened = 0;
                controller.HandleGearChanged(GearState.R);
                Check(ActiveTab(controller) == 1 && controller.CurrentPage == MainMenuPage.Lessons, "Barrido: la pestaña pasa a R al instante y la página no cambia");
                controller.Tick(0.05f);
                controller.HandleGearChanged(GearState.N);
                Check(ActiveTab(controller) == 2 && controller.CurrentPage == MainMenuPage.Lessons, "Barrido: la pestaña pasa a N al instante y la página no cambia");
                controller.Tick(0.05f);
                controller.HandleGearChanged(GearState.D);
                Check(ActiveTab(controller) == 3 && controller.CurrentPage == MainMenuPage.Lessons, "Barrido: la pestaña pasa a D al instante y la página no cambia");
                controller.Tick(0.10f);
                Check(controller.CurrentPage == MainMenuPage.Lessons && controller.HasPendingGear,
                    "Barrido P->R->N->D rápido: mientras no se estabiliza no cambia de página");
                Check(controller.LegendString.Contains("Volante") && PageRootActive(controller, MainMenuPage.Lessons) && !PageRootActive(controller, MainMenuPage.Credits),
                    "Barrido: la leyenda y la página visible siguen en Lecciones hasta estabilizarse");
                controller.Tick(0.20f);
                Check(controller.CurrentPage == MainMenuPage.Credits && !controller.HasPendingGear, "Barrido P->R->N->D rápido: termina solo en Créditos");
                Check(ActiveTab(controller) == 3 && PageRootActive(controller, MainMenuPage.Credits), "Marcha estable en D: la tira y la página coinciden");
                Check(opened == 0 && !menu.IsOpen, $"Barrido P->R->N->D rápido: la calibración nunca se abre (MenuOpened = {opened})");

                // Una marcha estable más de 0,25 s sí cambia de página.
                controller.HandleGearChanged(GearState.N);
                controller.Tick(0.20f);
                Check(controller.CurrentPage == MainMenuPage.Credits, "Marcha estable 0,20 s (< 0,25 s): todavía no cambia de página");
                controller.Tick(0.10f);
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen && opened == 1, "Marcha N estable 0,30 s: cambia a Calibración y abre la calibración una vez");
                Check(ActiveTab(controller) == 2 && PageRootActive(controller, MainMenuPage.Calibration), "Marcha estable en N: la tira y la página coinciden");

                // Un rebote N -> D -> N dentro de la espera no cierra ni reabre la calibración.
                opened = 0;
                controller.HandleGearChanged(GearState.D);
                controller.Tick(0.10f);
                controller.HandleGearChanged(GearState.N);
                controller.Tick(0.40f);
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen && opened == 0, "Rebote N->D->N rápido: la calibración sigue abierta sin reabrirse");
                controller.HandleGearChanged(GearState.P);
                controller.Tick(0.30f);
                Check(controller.CurrentPage == MainMenuPage.Lessons && !menu.IsOpen, "Volver a P estable cierra la calibración");
            }
            finally
            {
                menu.MenuOpened -= onOpened;
                if (menu.IsOpen) menu.CloseMenu();
            }
        }

        // Freno de mano: tirado del todo pide salir (overlay), bajarlo cancela, la ignición confirma.
        private static void VerifyExitOverlay(Scene scene, MainMenuController controller, InGameMenuController menu, StartButton startButton)
        {
            var handbrake = FindFirst<VRHandbrake>(scene);
            var highlighter = FindFirst<InteractableHighlighter>(scene);
            Check(handbrake != null, "Escena: hay un VRHandbrake");
            if (handbrake == null) return;

            var logs = new List<string>();
            Application.LogCallback capture = (msg, stack, type) => { if (type == LogType.Log) logs.Add(msg); };
            Application.logMessageReceived += capture;
            int exits = 0;
            System.Action onExit = () => exits++;
            controller.ExitRequested += onExit;
            controller.SetPerformQuit(false); // los tests solo verifican el evento: nunca cierran nada
            try
            {
                controller.Restart();
                Check(Approx(handbrake.Engagement, 0f), $"Freno de mano: arranca abajo (Engagement = {handbrake.Engagement:F2})");
                Check(!controller.ExitOverlayVisible && !OverlayActive(controller), "Arranque: el overlay de salida está oculto");
                Check(controller.LegendString.Contains("Freno de mano: tirar para salir"), "Splash: la leyenda incluye 'Freno de mano: tirar para salir'");
                Check(highlighter == null || !highlighter.CurrentTargets.Contains(handbrake.gameObject), "Splash: el freno de mano no se resalta");

                // Histéresis: 0,96 muestra, 0,9 sigue, 0,8 oculta.
                handbrake.SetEngagementImmediate(0.96f);
                controller.Tick(0.016f);
                Check(controller.ExitOverlayVisible && OverlayActive(controller), "Freno 0,96: se muestra el overlay de salida");
                Check(logs.Contains("[MainMenu] Salida solicitada"), "Freno 0,96: loguea 'Salida solicitada'");
                Check(controller.LegendString.Contains("Botón de encendido: confirmar salida") && controller.LegendString.Contains("Freno de mano: bajar para cancelar")
                    && !controller.LegendString.Contains("tirar para salir"), "Overlay: la leyenda pasa a confirmar salida / bajar para cancelar");
                Check(highlighter == null || (highlighter.CurrentTargets.Count == 1 && highlighter.CurrentTargets[0] == startButton.gameObject), "Overlay: solo se resalta la ignición");
                handbrake.SetEngagementImmediate(0.9f);
                controller.Tick(0.016f);
                Check(controller.ExitOverlayVisible, "Freno 0,9: el overlay sigue visible (histéresis)");
                handbrake.SetEngagementImmediate(0.8f);
                controller.Tick(0.016f);
                Check(!controller.ExitOverlayVisible && !OverlayActive(controller), "Freno 0,8: el overlay se oculta (cancelación)");
                Check(logs.Contains("[MainMenu] Salida cancelada"), "Freno 0,8: loguea 'Salida cancelada'");
                Check(controller.LegendString.Contains("comenzar") && controller.LegendString.Contains("Freno de mano: tirar para salir"),
                    "Cancelar: la leyenda vuelve a la del Splash");
                Check(highlighter == null || (highlighter.CurrentTargets.Count > 0 && !highlighter.CurrentTargets.Contains(startButton.gameObject)),
                    "Cancelar: vuelve el resaltado del Splash (balizas, no la ignición)");

                // Splash: la ignición confirma la salida y NO sale del Splash.
                handbrake.SetEngagementImmediate(1f);
                controller.Tick(0.016f);
                logs.Clear();
                startButton.Press();
                Check(exits == 1 && controller.CurrentPage == MainMenuPage.Splash, "Splash + overlay: Press() dispara ExitRequested y no sale del Splash");
                Check(logs.Contains("[MainMenu] Saliendo del simulador"), "Confirmar: loguea 'Saliendo del simulador'");

                // Splash + overlay: las balizas tampoco salen del Splash.
                controller.HandleHazardChanged(true);
                Check(controller.CurrentPage == MainMenuPage.Splash, "Splash + overlay: las balizas no salen del Splash");

                // Sin overlay, la ignición ya no sale del Splash: se sale con las balizas.
                handbrake.SetEngagementImmediate(0f);
                controller.Tick(0.016f);
                startButton.Press();
                Check(exits == 1 && controller.CurrentPage == MainMenuPage.Splash, "Sin overlay: Press() en el Splash no sale ni dispara ExitRequested");
                controller.HandleHazardChanged(true);
                Check(exits == 1 && controller.CurrentPage == MainMenuPage.Lessons, "Sin overlay: las balizas salen del Splash y no disparan ExitRequested");

                // Lecciones: con overlay no selecciona; sin overlay sí.
                handbrake.SetEngagementImmediate(0.97f);
                controller.Tick(0.016f);
                startButton.Press();
                Check(exits == 2 && controller.SelectedLessonIndex == -1, "Lecciones + overlay: Press() dispara ExitRequested y no selecciona lección");
                handbrake.SetEngagementImmediate(0f);
                controller.Tick(0.016f);
                startButton.Press();
                Check(exits == 2 && controller.SelectedLessonIndex == controller.CursorIndex, "Lecciones sin overlay: Press() selecciona la lección como antes");

                // La línea del freno de mano está en la leyenda de todas las pantallas.
                bool legendEverywhere = controller.LegendString.Contains("Freno de mano: tirar para salir");
                foreach (GearState gear in new[] { GearState.R, GearState.N, GearState.D, GearState.P })
                {
                    controller.HandleGearChanged(gear);
                    controller.Tick(0.3f);
                    legendEverywhere &= controller.LegendString.Contains("Freno de mano: tirar para salir");
                }
                Check(legendEverywhere, "La leyenda de las 4 páginas y del Splash incluye 'Freno de mano: tirar para salir'");
                Check(highlighter == null || !highlighter.CurrentTargets.Contains(handbrake.gameObject), "Ninguna pantalla resalta el freno de mano");

                // Calibración abierta: el overlay aparece, la calibración sigue abierta y la salida se confirma igual.
                controller.HandleGearChanged(GearState.N);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen, "Calibración abierta (precondición del overlay)");
                handbrake.SetEngagementImmediate(1f);
                controller.Tick(0.016f);
                Check(controller.ExitOverlayVisible && menu.IsOpen, "Calibración + freno a fondo: el overlay aparece y la calibración sigue abierta");
                startButton.Press();
                Check(exits == 3 && menu.IsOpen && controller.CurrentPage == MainMenuPage.Calibration, "Calibración + overlay: Press() dispara ExitRequested sin cerrar la calibración");
                handbrake.SetEngagementImmediate(0f);
                controller.Tick(0.016f);
                Check(!controller.ExitOverlayVisible && menu.IsOpen, "Calibración: al cancelar el overlay, la calibración sigue abierta");
                controller.HandleGearChanged(GearState.P);
                controller.Tick(0.3f);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                controller.ExitRequested -= onExit;
                controller.SetPerformQuit(true);
                handbrake.SetEngagementImmediate(0f);
                if (menu.IsOpen) menu.CloseMenu();
            }
        }

        // Los modelos 3D de los mandos se dibujan sobre la UI: bajo el overlay de salida no deben verse.
        private static void VerifyExitOverlayModels(Scene scene, MainMenuController controller, StartButton startButton)
        {
            var handbrake = FindFirst<VRHandbrake>(scene);
            List<ControllerIllustration> illustrations = FindAll<ControllerIllustration>(scene);
            Check(illustrations.Count == 5, $"Escena: 5 ControllerIllustration (2 del Splash y 3 de Controles) (hay {illustrations.Count})");
            if (handbrake == null || illustrations.Count == 0) return;

            // Awake no corre en Edit Mode: se arman los modelos a mano.
            foreach (ControllerIllustration ill in illustrations) ill.Build();
            Check(illustrations.TrueForAll(i => i.ControllerTransform != null), "ControllerIllustration: los 5 modelos se armaron");
            var press = illustrations.Find(i => i.PressAnimation);
            Check(press != null && press.ButtonTransform != null, "ControllerIllustration: el Splash tiene la ilustración Pulsar con su botón 3D");

            controller.SetPerformQuit(false);
            try
            {
                controller.Restart();
                Check(VisibleModelRenderers(controller, MainMenuPage.Splash) > 0 && VisibleModelRenderers(controller, MainMenuPage.Controls) == 0,
                    "Splash sin overlay: se ven los modelos del Splash y no los de Controles");

                // Splash + overlay: ningún renderer de ControllerIllustration visible (incluido el botón 3D de Pulsar).
                handbrake.SetEngagementImmediate(1f);
                controller.Tick(0.016f);
                Check(controller.ExitOverlayVisible && TotalVisibleModelRenderers(controller) == 0,
                    $"Splash + overlay: ningún renderer de ControllerIllustration visible ({TotalVisibleModelRenderers(controller)})");
                Check(press.ButtonTransform.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), "Splash + overlay: el botón 3D de Pulsar también está oculto");

                // Cancelar: vuelven los de la página actual y no los de las otras.
                handbrake.SetEngagementImmediate(0f);
                controller.Tick(0.016f);
                Check(!controller.ExitOverlayVisible && VisibleModelRenderers(controller, MainMenuPage.Splash) > 0 && VisibleModelRenderers(controller, MainMenuPage.Controls) == 0,
                    "Cancelar en el Splash: vuelven los modelos del Splash y los de Controles siguen ocultos");

                // La animación Pulsar no queda rota tras ocultarse y volver a mostrarse.
                press.EvaluateAt(0f);
                float restZ = press.ButtonTransform.localPosition.z;
                press.EvaluateAt(0.3f * 3.2f);
                Check(Mathf.Abs(press.ButtonTransform.localPosition.z - restZ) > 0.005f, "Pulsar tras volver a mostrarse: el botón se sigue hundiendo");
                press.EvaluateAt(0.9f * 3.2f);
                Check(Mathf.Abs(press.ButtonTransform.localPosition.z - restZ) < 0.0001f, "Pulsar tras volver a mostrarse: el botón vuelve al reposo (no queda hundido)");

                // Controles + overlay.
                controller.HandleHazardChanged(true);
                controller.HandleGearChanged(GearState.R);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Controls && VisibleModelRenderers(controller, MainMenuPage.Controls) > 0 && VisibleModelRenderers(controller, MainMenuPage.Splash) == 0,
                    "Controles sin overlay: se ven los modelos de Controles");
                handbrake.SetEngagementImmediate(1f);
                controller.Tick(0.016f);
                Check(controller.ExitOverlayVisible && TotalVisibleModelRenderers(controller) == 0, "Controles + overlay: ningún renderer de ControllerIllustration visible");
                handbrake.SetEngagementImmediate(0f);
                controller.Tick(0.016f);
                Check(VisibleModelRenderers(controller, MainMenuPage.Controls) > 0 && VisibleModelRenderers(controller, MainMenuPage.Splash) == 0,
                    "Cancelar en Controles: vuelven los modelos de Controles y no los del Splash");

                // Cambio de página con el overlay visible: los modelos de la página nueva tampoco aparecen hasta cancelar.
                controller.HandleGearChanged(GearState.P);
                controller.Tick(0.3f);
                handbrake.SetEngagementImmediate(1f);
                controller.Tick(0.016f);
                controller.HandleGearChanged(GearState.R);
                controller.Tick(0.3f);
                Check(controller.ExitOverlayVisible && controller.CurrentPage == MainMenuPage.Controls && TotalVisibleModelRenderers(controller) == 0,
                    "Overlay visible + cambio de página a Controles: los modelos de la página nueva siguen sin mostrarse");
                handbrake.SetEngagementImmediate(0f);
                controller.Tick(0.016f);
                Check(VisibleModelRenderers(controller, MainMenuPage.Controls) > 0, "Al cancelar, aparecen los modelos de la página nueva (Controles)");
            }
            finally
            {
                controller.SetPerformQuit(true);
                handbrake.SetEngagementImmediate(0f);
            }
        }

        // Renderers de modelos 3D realmente visibles (habilitados y con la página activa) dentro de una página.
        private static int VisibleModelRenderers(MainMenuController c, MainMenuPage page)
        {
            SerializedProperty roots = new SerializedObject(c).FindProperty("m_PageRoots");
            var root = roots.GetArrayElementAtIndex((int)page).objectReferenceValue as GameObject;
            if (root == null) return 0;

            int count = 0;
            foreach (ControllerIllustration ill in root.GetComponentsInChildren<ControllerIllustration>(true))
            {
                foreach (Renderer r in ill.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.enabled && r.gameObject.activeInHierarchy) count++;
                }
            }
            return count;
        }

        private static int TotalVisibleModelRenderers(MainMenuController c)
        {
            int total = 0;
            foreach (MainMenuPage page in System.Enum.GetValues(typeof(MainMenuPage))) total += VisibleModelRenderers(c, page);
            return total;
        }

        private static bool StripVisible(MainMenuController c)
        {
            var go = new SerializedObject(c).FindProperty("m_GearStripRoot").objectReferenceValue as GameObject;
            return go != null && go.activeSelf;
        }

        private static bool OverlayActive(MainMenuController c)
        {
            var go = new SerializedObject(c).FindProperty("m_ExitOverlay").objectReferenceValue as GameObject;
            return go != null && go.activeSelf;
        }

        private static bool PageRootActive(MainMenuController c, MainMenuPage page)
        {
            SerializedProperty roots = new SerializedObject(c).FindProperty("m_PageRoots");
            var go = roots.GetArrayElementAtIndex((int)page).objectReferenceValue as GameObject;
            return go != null && go.activeSelf;
        }

        // Pestaña realmente resaltada (color activo) en la tira; -1 si ninguna o si la tira está oculta.
        private static int ActiveTab(MainMenuController c)
        {
            var so = new SerializedObject(c);
            Color active = so.FindProperty("m_ActiveTabColor").colorValue;
            SerializedProperty tabs = so.FindProperty("m_GearTabs");
            int found = -1;
            for (int i = 0; i < tabs.arraySize; i++)
            {
                var tab = tabs.GetArrayElementAtIndex(i).objectReferenceValue as TMPro.TextMeshProUGUI;
                if (tab != null && tab.color == active)
                {
                    if (found != -1) return -2; // más de una resaltada
                    found = i;
                }
            }
            return StripVisible(c) ? found : -1;
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
                var brake = FindFirst<VRHandbrake>(scene);
                Check(brake != null && Approx(brake.Engagement, 0f), "MainMenuController: el freno de mano arranca abajo (Engagement 0)");

                // Durante el Splash la palanca no cambia de página: se registra y se aplica al salir.
                controller.HandleGearChanged(GearState.N);
                Check(controller.CurrentPage == MainMenuPage.Splash && controller.CurrentGear == GearState.N, "Splash: la palanca no cambia de página, solo se registra la marcha");
                controller.HandleGearChanged(GearState.P);
                Check(controller.CurrentPage == MainMenuPage.Splash, "Splash: volver a P tampoco cambia de página");

                // La ignición ya no sale del Splash (evita elegir una lección con la misma pulsación repetida).
                startButton.Press();
                Check(controller.CurrentPage == MainMenuPage.Splash && controller.SelectedLessonIndex == -1, "Press() en Splash: no sale del Splash ni marca lección");

                // Las balizas salen del Splash hacia la marcha actual y se vuelven a apagar.
                car.SetHazardActive(true);
                Check(controller.CurrentPage == MainMenuPage.Lessons, "Balizas en Splash -> página Lecciones (marcha P)");
                Check(!car.IsHazardActive, "Balizas en Splash: quedan apagadas al salir");
                Check(controller.SelectedLessonIndex == -1, "Balizas en Splash: no marcan ninguna lección");

                // Fuera del Splash las balizas no cambian de página.
                car.SetHazardActive(true);
                Check(controller.CurrentPage == MainMenuPage.Lessons && car.IsHazardActive, "Balizas en Lecciones: no cambian de página");
                car.SetHazardActive(false);

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
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Controls, "Palanca en R (estable) -> página Controles");
                logs.Clear();
                startButton.Press();
                Check(!logs.Exists(l => l.Contains("Lección seleccionada")) && controller.CurrentPage == MainMenuPage.Controls, "Press() en Controles no hace nada");

                // Calibración
                controller.HandleGearChanged(GearState.N);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen, "Palanca en N (estable) -> Calibración abre la calibración standalone");
                Check(Time.timeScale == timeScaleBefore && AudioListener.pause == audioPauseBefore, "Calibración standalone no pausa el tiempo ni el audio");
                controller.HandleGearChanged(GearState.D);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Credits && !menu.IsOpen, "Salir de N -> Créditos cierra la calibración");
                controller.HandleGearChanged(GearState.N);
                controller.Tick(0.3f);
                controller.HandleGearChanged(GearState.P);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Lessons && !menu.IsOpen, "Volver a P cierra la calibración y muestra Lecciones");

                // Motor bloqueado: no se enciende por ninguna vía (sin apagarlo desde adentro de OnEngineStateChanged).
                Check(car.IsEngineLocked, "MainMenuController: el motor queda bloqueado");
                int nonOffEvents = 0;
                int rejected = 0;
                System.Action<EngineState> onState = s => { if (s != EngineState.Off) nonOffEvents++; };
                System.Action<string> onRejected = r => rejected++;
                car.OnEngineStateChanged += onState;
                car.OnEngineStartRejected += onRejected;
                try
                {
                    car.SetEngineRunning(true);
                    bool accepted = car.TryToggleEngine();
                    Check(!car.IsEngineRunning && !accepted && nonOffEvents == 0, "Motor bloqueado en la escena: SetEngineRunning(true) y TryToggleEngine no lo encienden");
                    Check(rejected == 1, "Motor bloqueado en la escena: TryToggleEngine emite el rechazo");
                }
                finally
                {
                    car.OnEngineStateChanged -= onState;
                    car.OnEngineStartRejected -= onRejected;
                }
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

            // El frente de referencia de los 45° es el del AUTO (m_StandaloneForwardReference), no el de la cabeza.
            var forwardRef = so.FindProperty("m_StandaloneForwardReference").objectReferenceValue as Transform;
            Check(forwardRef != null, "InGameMenuController (escena): m_StandaloneForwardReference asignado (frente del auto)");
            if (forwardRef == null) return;
            Quaternion headRotation = head.rotation;
            try
            {
                CheckStandalonePlacement(menu, head, forwardRef, distance, "cabeza al frente");

                // Con la cabeza girada a la derecha, a la izquierda y hacia atrás, el panel sigue al noreste del auto.
                foreach (float turn in new[] { 90f, -90f, 180f })
                {
                    head.rotation = Quaternion.Euler(0f, forwardRef.eulerAngles.y + turn, 0f);
                    menu.CloseMenu();
                    menu.OpenCalibrationStandalone();
                    CheckStandalonePlacement(menu, head, forwardRef, distance, $"cabeza girada {turn:F0}°");
                }

                // Sin referencia: comportamiento anterior (45° a la derecha del frente de la CABEZA).
                SetField(menu, "m_StandaloneForwardReference", null);
                head.rotation = Quaternion.Euler(0f, forwardRef.eulerAngles.y + 90f, 0f);
                menu.CloseMenu();
                menu.OpenCalibrationStandalone();
                CheckStandalonePlacement(menu, head, head, distance, "sin referencia (comportamiento anterior, cabeza girada 90°)");
                Vector3 headFwd = head.forward; headFwd.y = 0f;
                Vector3 carFwd = forwardRef.forward; carFwd.y = 0f;
                Check(Mathf.Abs(Vector3.SignedAngle(carFwd, headFwd, Vector3.up) - 90f) < 1f, "Sin referencia: el panel acompaña a la cabeza, no al auto");
                SetField(menu, "m_StandaloneForwardReference", forwardRef);
            }
            finally
            {
                head.rotation = headRotation;
                menu.CloseMenu();
                menu.OpenCalibrationStandalone();
            }

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

        // Panel de calibración standalone: a 45° (±2°) a la derecha del frente horizontal de frontSource, a la distancia
        // pedida de los ojos, a su altura y mirando hacia la cabeza.
        private static void CheckStandalonePlacement(InGameMenuController menu, Transform head, Transform frontSource, float distance, string label)
        {
            Vector3 toMenu = menu.MenuRoot.transform.position - head.position;
            Vector3 front = frontSource.forward; front.y = 0f;
            Vector3 flat = toMenu; flat.y = 0f;
            float yaw = Vector3.SignedAngle(front, flat, Vector3.up);
            Check(Mathf.Abs(yaw - 45f) <= 2f, $"OpenCalibrationStandalone ({label}): a 45° (±2°) a la derecha del frente de {(frontSource == head ? "la cabeza" : "auto")} (yaw = {yaw:F1}°)");
            Check(Mathf.Abs(toMenu.magnitude - distance) < 0.01f, $"OpenCalibrationStandalone ({label}): a {distance:F1} m de los ojos ({toMenu.magnitude:F2} m)");
            Check(Mathf.Abs(toMenu.y) < 0.001f, $"OpenCalibrationStandalone ({label}): a la altura de los ojos");
            Check(Vector3.Dot(menu.MenuRoot.transform.forward, toMenu.normalized) > 0.99f, $"OpenCalibrationStandalone ({label}): el panel mira a la cabeza (misma convención que OpenMenu)");
        }

        // ------------------------------------------------------------------ helpers

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

        // Los objetos temporales se configuran por reflexión: SerializedObject.ApplyModifiedProperties sobre objetos de la
        // escena de trabajo ensucia (marca dirty) la escena que el usuario tenga abierta, p. ej. MainMenu.
        private static void SetField(object target, string field, object value)
        {
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo info = type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (info != null)
                {
                    info.SetValue(target, value);
                    return;
                }
            }
            throw new System.MissingFieldException(target.GetType().Name, field);
        }

        private static void SetLessons(LessonListPanel panel, List<LessonDefinitionSO> lessons)
        {
            SetField(panel, "m_Lessons", lessons);
        }

        private static void SetObjectField(Object target, string field, Object value)
        {
            SetField(target, field, value);
        }

        private static void SetBoolField(Object target, string field, bool value)
        {
            SetField(target, field, value);
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
