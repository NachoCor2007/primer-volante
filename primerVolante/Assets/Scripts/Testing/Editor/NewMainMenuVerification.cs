using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// Batería de verificación automatizada para el nuevo menú principal NewMainMenu.
    /// Valida:
    /// - Ausencia total de Car09 o vehículos en la escena (menú limpio sin obstrucciones de cabina).
    /// - XR Origin en (0,0,0) con DriverSeatFollower anclado a MenuViewpoint y con InputActionManager configurado.
    /// - Canvas en World Space con worldCamera asignada y TrackedDeviceGraphicRaycaster único.
    /// - RaycastTargets disciplinados (fondos y textos en false, botones en true).
    /// - Navegación entre solapas, currículum de 6 lecciones con botón "COMENZAR LECCIÓN",
    ///   circuitos con "INICIAR RECORRIDO", ilustraciones 3D en controles y modal de salida centrado.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class NewMainMenuVerification
    {
        private static int s_Passed = 0;
        private static int s_Total = 0;
        private static readonly List<string> s_Failures = new List<string>();

        [MenuItem("Tools/Primer Volante/Verify New Main Menu Scene")]
        public static void RunVerification()
        {
            s_Passed = 0;
            s_Total = 0;
            s_Failures.Clear();

            Debug.Log("==================================================================");
            Debug.Log("[NewMainMenuVerification] Iniciando suite de verificación exhaustiva...");

            SceneSetup[] sceneSetupBefore = null;
            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;
            try
            {
                VerifySetupScript();

                // Todo lo que muta la escena corre sobre una copia temporal, aislada: se cierran las demás escenas abiertas
                // (el setup usa búsquedas globales y borra cámaras que no son del XR Origin) y se restauran al terminar.
                bool anyDirty = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).isDirty) anyDirty = true;
                }
                if (anyDirty)
                {
                    Check(false, "Hay escenas abiertas con cambios sin guardar: guardalas antes de verificar (la verificación necesita aislar la copia de NewMainMenu)");
                }
                else
                {
                    sceneSetupBefore = EditorSceneManager.GetSceneManagerSetup();
                    VerificationSceneUtil.WithSceneCopy(NewMainMenuSetup.SCENE_PATH, VerifyOnCopy);
                }
            }
            catch (Exception ex)
            {
                s_Failures.Add($"Excepción no controlada durante las pruebas: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                VRSessionStateFeature.ResetForTests();
                SimulationPause.ResetForTests();
                Time.timeScale = timeScaleBefore;
                AudioListener.pause = audioPauseBefore;
                RestoreScenes(sceneSetupBefore);
            }

            Debug.Log("==================================================================");
            if (s_Failures.Count == 0)
            {
                Debug.Log($"<color=#10b981><b>[NewMainMenuVerification] ¡TODOS LOS TESTS PASARON EXITOSAMENTE! ({s_Passed}/{s_Total})</b></color>");
            }
            else
            {
                Debug.LogError($"<color=#ef4444><b>[NewMainMenuVerification] FALLARON {s_Failures.Count} de {s_Total} validaciones:</b></color>");
                for (int i = 0; i < s_Failures.Count; i++)
                {
                    Debug.LogError($"  [{i + 1}] {s_Failures[i]}");
                }
            }
            Debug.Log("==================================================================");
        }

        // Restaura las escenas que había abiertas. Una escena Untitled sin modificar se captura sin path y RestoreSceneManagerSetup
        // la rechaza: se descartan esas entradas. Si no queda ninguna se deja una escena vacía. Un error se registra como falla
        // (no se escapa de RunVerification para no perder el resumen).
        private static void RestoreScenes(SceneSetup[] setup)
        {
            if (setup == null) return;

            try
            {
                SceneSetup[] restorable = setup.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
                if (restorable.Length == 0)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    return;
                }
                if (!restorable.Any(s => s.isActive)) restorable[0].isActive = true;
                EditorSceneManager.RestoreSceneManagerSetup(restorable);
            }
            catch (Exception ex)
            {
                Check(false, $"No se pudieron restaurar las escenas abiertas antes de la verificación: {ex.Message}");
            }
        }

        private static void VerifySetupScript()
        {
            Check(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/MainMenu/NewMainMenuController.cs") != null, "NewMainMenuController.cs existe en el proyecto");
            Check(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/Testing/Editor/NewMainMenuSetup.cs") != null, "NewMainMenuSetup.cs existe en el proyecto");
        }

        // Corre sobre la copia temporal de la escena guardada (la original nunca se toca).
        private static void VerifyOnCopy(Scene scene)
        {
            // Aislar la copia: el setup usa búsquedas globales y borra cámaras que no son del XR Origin. Primero se cierran las
            // demás escenas (también una Untitled, que impediría crear la escena aditiva) y después se agrega una escena vacía
            // (scratch) para que cerrar la copia al final nunca sea cerrar la última escena; RestoreScenes la descarta.
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open != scene) EditorSceneManager.CloseScene(open, true);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            VerifySceneStructureAndComponents(scene);
            VerifyPauseMenuIntegration(scene);
            VerifyCalibrationButtonFlag(scene);
            VerifyBuildIsIdempotent(scene);
            VerifyDisconnectionPause(scene);
        }

        private static void VerifySceneStructureAndComponents(Scene scene)
        {
            Check(scene.IsValid() && scene.isLoaded, $"La copia de {NewMainMenuSetup.SCENE_PATH} se cargó correctamente");

            // 1. Validar que NO exista Car09 ni VehicleController en la escena
            VehicleController[] cars = UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include);
            Check(cars == null || cars.Length == 0, "No existe ningún VehicleController (Car09) en la escena");

            GameObject car09Obj = GameObject.Find("Car09");
            Check(car09Obj == null, "No existe el GameObject Car09 en la escena");

            // 2. XR Origin (XR Rig) centrado y con DriverSeatFollower anclado a MenuViewpoint (detalle en MenuHeadPoseVerification)
            GameObject xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
            Check(xrOriginObj != null, "Existe el GameObject XR Origin (XR Rig)");
            if (xrOriginObj != null)
            {
                DriverSeatFollower follower = xrOriginObj.GetComponent<DriverSeatFollower>();
                Check(follower != null && follower.enabled && follower.DriverSeat != null && follower.DriverSeat.name == NewMainMenuSetup.MENU_VIEWPOINT_NAME,
                    "XR Origin tiene DriverSeatFollower anclado a MenuViewpoint (compensa la pose de la cabeza, no sigue un auto)");
                Check(xrOriginObj.transform.position == Vector3.zero, "XR Origin está centrado en el origen Vector3.zero");

                var inputMgr = xrOriginObj.GetComponent<InputActionManager>();
                Check(inputMgr != null && inputMgr.actionAssets != null && inputMgr.actionAssets.Count > 0, "XR Origin tiene InputActionManager con acciones XRI configuradas");

                Camera headCam = xrOriginObj.GetComponentInChildren<Camera>(true);
                Check(headCam != null && headCam.gameObject.activeSelf, "XR Origin posee una cámara activa para el visor VR");

                // Verificaciones de anclaje estático y prevención de caída por gravedad física
                CharacterController cc = xrOriginObj.GetComponent<CharacterController>();
                Check(cc == null || !cc.enabled, "CharacterController está desactivado (evita que la física acelere el rig hacia abajo)");

                Transform locomotion = xrOriginObj.transform.Find("Locomotion");
                Check(locomotion != null && !locomotion.gameObject.activeSelf, "GameObject Locomotion está desactivado (elimina GravityProvider y traslación)");

                bool anyLocoProviderEnabled = false;
                foreach (var provider in xrOriginObj.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true))
                {
                    if (provider.enabled) anyLocoProviderEnabled = true;
                }
                Check(!anyLocoProviderEnabled, "Todos los proveedores de locomoción están desactivados");
            }

            // 2.b. Piso físico y plataforma de menú
            GameObject groundObj = GameObject.Find("Ground_MainMenu");
            Check(groundObj != null, "Existe el GameObject Ground_MainMenu en la escena");
            if (groundObj != null)
            {
                BoxCollider groundBox = groundObj.GetComponent<BoxCollider>();
                Check(groundBox != null && groundBox.enabled && !groundBox.isTrigger, "Ground_MainMenu posee un BoxCollider físico activo");
            }

            GameObject platformObj = GameObject.Find(NewMainMenuSetup.LEGACY_PLATFORM_NAME);
            Check(platformObj == null, "No existe la plataforma visual Menu_FloorPlatform (se quitó a propósito en dc9c19e)");

            // 3. Canvas WorldSpace, WorldCamera y Raycaster
            GameObject canvasObj = GameObject.Find(NewMainMenuSetup.CANVAS_NAME);
            Check(canvasObj != null, "Existe el GameObject NewMainMenu_Canvas");
            if (canvasObj == null) return;

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace, "El Canvas está configurado en WorldSpace");
            Check(Vector3.Distance(canvasObj.transform.position, NewMainMenuSetup.CanvasWorldPosition) < 0.001f,
                $"El Canvas está en la posición revisada {NewMainMenuSetup.CanvasWorldPosition} (actual: {canvasObj.transform.position})");
            Check(canvas != null && canvas.worldCamera != null, "El Canvas tiene asignada la worldCamera de XR Origin");

            TrackedDeviceGraphicRaycaster trackedRaycaster = canvasObj.GetComponent<TrackedDeviceGraphicRaycaster>();
            Check(trackedRaycaster != null, "El Canvas posee TrackedDeviceGraphicRaycaster para interacción VR");

            GraphicRaycaster legacyRaycaster = canvasObj.GetComponent<GraphicRaycaster>();
            Check(legacyRaycaster == null, "El Canvas NO posee GraphicRaycaster redundante (evita conflictos de raycast)");

            // 4. Disciplina de RaycastTargets
            Image bg = canvasObj.transform.Find("Background")?.GetComponent<Image>();
            Check(bg != null && !bg.raycastTarget, "El fondo del Canvas tiene raycastTarget = false (no absorbe eventos de puntero)");

            // 5. Capa UI estricta
            int uiLayer = LayerMask.NameToLayer("UI");
            bool allInUILayer = true;
            foreach (Transform t in canvasObj.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer != uiLayer)
                {
                    allInUILayer = false;
                    break;
                }
            }
            Check(allInUILayer, "Todos los elementos del Canvas pertenecen estrictamente a la capa UI");

            // 6. EventSystem con XRUIInputModule
            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            Check(eventSystem != null, "Existe EventSystem en la escena");
            if (eventSystem != null)
            {
                XRUIInputModule xrModule = eventSystem.GetComponent<XRUIInputModule>();
                Check(xrModule != null, "EventSystem tiene agregado XRUIInputModule");
            }

            // 7. Controlador y Solapas
            NewMainMenuController controller = canvasObj.GetComponent<NewMainMenuController>();
            Check(controller != null, "NewMainMenuController está adjunto al Canvas");
            if (controller == null) return;

            Check(controller.XROriginTransform != null, "NewMainMenuController tiene asignado m_XROriginTransform para anclaje estático");
            controller.LockPlayerStationary();
            Check(controller.XROriginTransform != null && controller.XROriginTransform.position == Vector3.zero, "LockPlayerStationary deja el rig en MenuViewpoint = Vector3.zero (sin VR)");

            Check(controller.Lessons.Count == 6, "NewMainMenuController tiene cargadas las 6 lecciones del currículum");
            Check(controller.Circuits.Count >= 1, "NewMainMenuController tiene configurado el circuito integrador");
            Check(controller.CurrentTab == NewMenuTab.Controles, "La solapa inicial por defecto es 'Controles y Mandos'");

            // 7.b. Botón Salir en columna izquierda (Sidebar)
            Button btnExit = canvasObj.transform.Find("Sidebar/Btn_Exit")?.GetComponent<Button>();
            Check(btnExit != null, "Existe el botón rojo Btn_Exit en la columna izquierda");
            if (btnExit != null)
            {
                RectTransform rt = (RectTransform)btnExit.transform;
                Check(rt.rect.width > 200f || (rt.anchorMax.x - rt.anchorMin.x > 0.5f), "Btn_Exit tiene ancho completo en la columna (no se comprime verticalmente)");
                TextMeshProUGUI txt = btnExit.GetComponentInChildren<TextMeshProUGUI>();
                Check(txt != null && txt.text.Contains("SALIR"), "Btn_Exit tiene texto legible 'SALIR DEL SIMULADOR'");
            }

            // 8. Test de Navegación por Solapas
            controller.SelectTab(NewMenuTab.Controles);
            Check(controller.CurrentTab == NewMenuTab.Controles, "Navegación: Activar solapa Controles");

            controller.SelectTab(NewMenuTab.Circuitos);
            Check(controller.CurrentTab == NewMenuTab.Circuitos, "Navegación: Activar solapa Circuitos");

            controller.SelectTab(NewMenuTab.Calibracion);
            Check(controller.CurrentTab == NewMenuTab.Calibracion, "Navegación: Activar solapa Calibración");

            controller.SelectTab(NewMenuTab.Creditos);
            Check(controller.CurrentTab == NewMenuTab.Creditos, "Navegación: Activar solapa Créditos");

            controller.SelectTab(NewMenuTab.Lecciones);
            Check(controller.CurrentTab == NewMenuTab.Lecciones, "Navegación: Activar solapa Lecciones");

            // 9. Ilustraciones 3D de Controles
            ControllerIllustration[] illustrations = canvasObj.GetComponentsInChildren<ControllerIllustration>(true);
            Check(illustrations != null && illustrations.Length >= 3, "El panel de Controles contiene al menos 3 ilustraciones 3D interactivas de mando VR");

            // 10. Botones de acción formativos verdes
            Button btnStartLesson = canvasObj.transform.Find("ContentArea/Panel_Lessons/LessonDetailColumn/ActionBox/Btn_StartLesson")?.GetComponent<Button>();
            Check(btnStartLesson != null, "Existe el botón de acción formativa para Lecciones");
            if (btnStartLesson != null)
            {
                TextMeshProUGUI txt = btnStartLesson.GetComponentInChildren<TextMeshProUGUI>();
                Check(txt != null && txt.text.Contains("COMENZAR"), "El botón de lección tiene mensaje formativo adecuado ('COMENZAR LECCIÓN')");
            }

            Button btnStartCircuit = canvasObj.transform.Find("ContentArea/Panel_Circuits/CircuitDetailColumn/ActionBox/Btn_StartCircuit")?.GetComponent<Button>();
            Check(btnStartCircuit != null, "Existe el botón de acción para Circuitos");
            if (btnStartCircuit != null)
            {
                TextMeshProUGUI txt = btnStartCircuit.GetComponentInChildren<TextMeshProUGUI>();
                Check(txt != null && txt.text.Contains("INICIAR"), "El botón de circuito tiene mensaje adecuado ('INICIAR RECORRIDO')");
            }

            // 11. Modal de Salida
            Check(!controller.IsExitModalOpen, "El modal de salida está inicialmente cerrado");
            controller.OpenExitModal();
            Check(controller.IsExitModalOpen, "OpenExitModal abre el modal de confirmación");

            GameObject exitModalObj = canvasObj.transform.Find("Modal_ExitConfirmation")?.gameObject;
            Check(exitModalObj != null && exitModalObj.activeSelf, "Modal_ExitConfirmation se encuentra activo visualmente");

            Button btnConfirmExit = exitModalObj?.transform.Find("ConfirmBox/ButtonRow/Btn_Confirm")?.GetComponent<Button>();
            Button btnCancelExit = exitModalObj?.transform.Find("ConfirmBox/ButtonRow/Btn_Cancel")?.GetComponent<Button>();
            Check(btnConfirmExit != null && btnCancelExit != null, "El modal de salida contiene botones de Cancelar y Confirmar Salida");

            controller.CloseExitModal();
            Check(!controller.IsExitModalOpen, "CloseExitModal cierra el modal de confirmación");
        }

        // ====================================================================
        // PAUSA POR DESCONEXIÓN VR (integración del menú de pausa en NewMainMenu)
        // ====================================================================
        private const float BASELINE_TIME_SCALE = 0.75f;
        private const string PAUSE_CANVAS_NAME = "InGameMenu_Canvas";

        private static int CountNamed(Scene scene, string name)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name) count++;
                }
            }
            return count;
        }

        private static void VerifyPauseMenuIntegration(Scene scene)
        {
            InGameMenuController[] menus = VerificationSceneUtil.AllInScene<InGameMenuController>(scene);
            Check(CountNamed(scene, PAUSE_CANVAS_NAME) == 1, "Pausa VR: la escena tiene exactamente un InGameMenu_Canvas");
            Check(menus.Length == 1, $"Pausa VR: la escena tiene exactamente un InGameMenuController (hay {menus.Length})");
            if (menus.Length != 1) return;

            InGameMenuController menu = menus[0];
            Check(!menu.MenuButtonEnabled, "Pausa VR: el botón Menu y Escape no abren el menú de pausa (MenuButtonEnabled == false)");
            Check(!menu.ShowCalibrationButton, "Pausa VR: el menú de pausa no muestra 'Calibrar posición' (ShowCalibrationButton == false)");
            Check(menu.MenuRoot != null && menu.MenuRoot.transform.Find("MainPanel/Button_Calibrate") != null && menu.ResumeButton != null,
                "Pausa VR: el menú de pausa tiene Reanudar y Calibrar en el panel principal");

            GameObject canvasObj = GameObject.Find(NewMainMenuSetup.CANVAS_NAME);
            NewMainMenuController controller = canvasObj != null ? canvasObj.GetComponent<NewMainMenuController>() : null;
            CanvasGroup group = canvasObj != null ? canvasObj.GetComponent<CanvasGroup>() : null;
            Check(group != null && group.interactable && group.alpha == 1f && group.blocksRaycasts,
                "Pausa VR: NewMainMenu_Canvas tiene un CanvasGroup interactuable y opaco por defecto");

            var so = controller != null ? new SerializedObject(controller) : null;
            Check(so != null && so.FindProperty("m_InGameMenu").objectReferenceValue == menu,
                "Pausa VR: NewMainMenuController tiene m_InGameMenu asignado al menú de pausa de la escena");
            Check(canvasObj != null && canvasObj.GetComponentsInChildren<CanvasGroup>(true).Length == 1 && group != null && group.gameObject == canvasObj,
                "Pausa VR: el único CanvasGroup del menú está en la raíz del canvas (ninguno anidado)");
            Check(controller != null && controller.InteractionGroup == group,
                "Pausa VR: InteractionGroup del controlador es el CanvasGroup de la raíz del canvas");
            Transform calibrateTransform = menu.MenuRoot != null ? menu.MenuRoot.transform.Find("MainPanel/Button_Calibrate") : null;
            var soMenu = new SerializedObject(menu);
            Check(calibrateTransform != null && soMenu.FindProperty("m_CalibrateButton").objectReferenceValue == calibrateTransform.gameObject,
                "Pausa VR: el menú de pausa tiene m_CalibrateButton serializado (cableado por el setup)");

            EventSystem[] eventSystems = VerificationSceneUtil.AllInScene<EventSystem>(scene);
            Check(eventSystems.Length == 1, $"Pausa VR: existe un solo EventSystem (hay {eventSystems.Length})");
            Check(eventSystems.Length == 1 && eventSystems[0].GetComponent<XRUIInputModule>() != null &&
                  eventSystems[0].GetComponent<StandaloneInputModule>() == null &&
                  eventSystems[0].GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null,
                "Pausa VR: el EventSystem conserva su XRUIInputModule (sin módulos de entrada incompatibles)");
        }

        // El flag nuevo no cambia las lecciones (por defecto true) y, apagado, oculta Calibrar en OpenMenu y OpenMenuWithNotice.
        private static void VerifyCalibrationButtonFlag(Scene scene)
        {
            var fresh = new GameObject("TMP_DefaultInGameMenu").AddComponent<InGameMenuController>();
            Check(fresh.ShowCalibrationButton, "InGameMenuController nuevo: ShowCalibrationButton == true por defecto (las lecciones no cambian)");
            UnityEngine.Object.DestroyImmediate(fresh.gameObject);

            InGameMenuController[] menus = VerificationSceneUtil.AllInScene<InGameMenuController>(scene);
            if (menus.Length != 1) return;
            InGameMenuController menu = menus[0];
            Transform calibrate = menu.MenuRoot.transform.Find("MainPanel/Button_Calibrate");
            if (calibrate == null) { Check(false, "Existe MainPanel/Button_Calibrate"); return; }

            float timeBefore = Time.timeScale;
            var so = new SerializedObject(menu);

            // Con el flag en true (lecciones) el botón vuelve a mostrarse al abrir, aunque estuviera inactivo.
            so.FindProperty("m_ShowCalibrationButton").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            calibrate.gameObject.SetActive(false);
            menu.OpenMenu();
            Check(calibrate.gameObject.activeSelf, "ShowCalibrationButton == true: OpenMenu vuelve a mostrar 'Calibrar posición' (estaba inactivo)");
            menu.CloseMenu();

            calibrate.gameObject.SetActive(false);
            menu.OpenMenuWithNotice("aviso de prueba");
            Check(calibrate.gameObject.activeSelf, "ShowCalibrationButton == true: OpenMenuWithNotice vuelve a mostrar 'Calibrar posición'");
            menu.EndConnectionPause();

            // Escenas con el menú serializado sin m_CalibrateButton: se busca una sola vez por nombre y funciona en las dos direcciones.
            so.FindProperty("m_CalibrateButton").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            calibrate.gameObject.SetActive(false);
            menu.OpenMenu();
            Check(calibrate.gameObject.activeSelf && menu.CalibrateButton == calibrate.gameObject,
                "Sin m_CalibrateButton serializado: el fallback por nombre encuentra el botón y lo muestra (flag en true)");
            menu.CloseMenu();

            // Con el flag en false queda inactivo con OpenMenu, con OpenMenuWithNotice y al restaurar el modo previo.
            so.Update(); // el fallback escribió m_CalibrateButton en el objeto vivo: no pisarlo con el valor viejo
            so.FindProperty("m_ShowCalibrationButton").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            calibrate.gameObject.SetActive(true);
            menu.OpenMenu();
            Check(!calibrate.gameObject.activeSelf, "ShowCalibrationButton == false: OpenMenu oculta 'Calibrar posición'");
            menu.CloseMenu();

            calibrate.gameObject.SetActive(true);
            menu.OpenMenuWithNotice("aviso de prueba");
            Check(!calibrate.gameObject.activeSelf, "ShowCalibrationButton == false: OpenMenuWithNotice oculta 'Calibrar posición'");
            menu.EndConnectionPause();
            Check(!menu.IsOpen && Time.timeScale == timeBefore, "EndConnectionPause cierra el menú de prueba y restaura el tiempo");

            // Restaurar el modo previo "menú del jugador abierto" también lo oculta.
            menu.OpenMenu();
            calibrate.gameObject.SetActive(true);
            menu.OpenMenuWithNotice("aviso de prueba");
            calibrate.gameObject.SetActive(true);
            menu.EndConnectionPause();
            Check(menu.IsOpen && !calibrate.gameObject.activeSelf, "ShowCalibrationButton == false: restaurar el modo previo oculta 'Calibrar posición'");
            menu.CloseMenu();

            // CanvasGroup anidado: nunca se usa uno que no sea el de la raíz del canvas.
            var rootObj = new GameObject("TMP_NestedGroupCanvas", typeof(RectTransform), typeof(Canvas));
            var child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(rootObj.transform, false);
            CanvasGroup nested = child.AddComponent<CanvasGroup>();
            NewMainMenuController nestedController = rootObj.AddComponent<NewMainMenuController>();
            CanvasGroup resolved = nestedController.InteractionGroup;
            Check(resolved != null && resolved != nested && resolved.gameObject == rootObj,
                "CanvasGroup: un grupo anidado nunca se usa; se toma (o agrega) el de la raíz del canvas");
            UnityEngine.Object.DestroyImmediate(rootObj);
            Time.timeScale = timeBefore;
            AudioListener.pause = false;
            SimulationPause.ResetForTests();
        }

        // Correr el setup dos veces (sobre la copia) no duplica el menú de pausa, el EventSystem ni el canvas.
        private static void VerifyBuildIsIdempotent(Scene scene)
        {
            string path = scene.path;
            SceneManager.SetActiveScene(scene);

            for (int run = 1; run <= 2; run++)
            {
                NewMainMenuController built = NewMainMenuSetup.BuildInActiveScene(path);
                Check(built != null, $"Idempotencia: BuildInActiveScene #{run} construye el menú");

                int pauseCanvases = CountNamed(scene, PAUSE_CANVAS_NAME);
                int pauseControllers = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).Length;
                int eventSystems = VerificationSceneUtil.AllInScene<EventSystem>(scene).Length;
                int xrModules = VerificationSceneUtil.AllInScene<XRUIInputModule>(scene).Length;
                int canvases = CountNamed(scene, NewMainMenuSetup.CANVAS_NAME);
                int groups = built != null ? built.GetComponents<CanvasGroup>().Length : -1;
                Check(pauseCanvases == 1 && pauseControllers == 1 && eventSystems == 1 && xrModules == 1 && canvases == 1 && groups == 1,
                    $"Idempotencia: tras la corrida #{run} hay 1 InGameMenu_Canvas ({pauseCanvases}), 1 InGameMenuController ({pauseControllers}), " +
                    $"1 EventSystem ({eventSystems}), 1 XRUIInputModule ({xrModules}), 1 NewMainMenu_Canvas ({canvases}) y 1 CanvasGroup ({groups})");
            }

            VerifyPauseMenuIntegration(scene);
        }

        // Estado, controles y transiciones se inyectan al monitor (sin runtime XR). Update no corre en Edit Mode, pero el bloqueo
        // del controlador no depende de él: se aplica por el evento GameplayInputBlockingChanged del menú de pausa, así que todos
        // los checks se hacen justo después de la acción, sin esperar ningún "frame". Los clics "de XRI" se simulan ejecutando el
        // evento pointerClick sobre el botón (respeta IsInteractable); el fallback de gatillo es TryClickButton.
        private static void VerifyDisconnectionPause(Scene scene)
        {
            InGameMenuController[] menus = VerificationSceneUtil.AllInScene<InGameMenuController>(scene);
            GameObject canvasObj = GameObject.Find(NewMainMenuSetup.CANVAS_NAME);
            NewMainMenuController controller = canvasObj != null ? canvasObj.GetComponent<NewMainMenuController>() : null;
            CanvasGroup group = canvasObj != null ? canvasObj.GetComponent<CanvasGroup>() : null;
            if (menus.Length != 1 || controller == null || group == null)
            {
                Check(false, "Pausa VR: la escena tiene menú de pausa, controlador y CanvasGroup para probar la desconexión");
                return;
            }

            InGameMenuController menu = menus[0];
            // Botones sondeados: uno del NewMainMenu y Reanudar. Los contadores cuentan los onClick realmente invocados
            // (los listeners persistentes del setup son RuntimeOnly y no corren en Edit Mode).
            Button tabButton = canvasObj.transform.Find("Sidebar/NavButtons/Tab_Lecciones")?.GetComponent<Button>();
            Button resumeButton = menu.ResumeButton;
            if (tabButton == null || resumeButton == null)
            {
                Check(false, "Pausa VR: existen Tab_Lecciones y Reanudar para probar los clics");
                return;
            }
            int tabClicks = 0, resumeClicks = 0;
            UnityEngine.Events.UnityAction onTab = () => tabClicks++;
            UnityEngine.Events.UnityAction onResume = () => resumeClicks++;
            tabButton.onClick.AddListener(onTab);
            resumeButton.onClick.AddListener(onResume);

            menu.MenuRoot.SetActive(false);
            Time.timeScale = BASELINE_TIME_SCALE;
            AudioListener.pause = false;
            SimulationPause.ResetForTests();
            VRSessionStateFeature.ResetForTests();
            group.interactable = true;
            controller.ConnectPauseMenu(); // OnEnable no corre en Edit Mode

            var monitorObj = new GameObject("TMP_VRHardwareStateMonitor");
            VRHardwareStateMonitor monitor = monitorObj.AddComponent<VRHardwareStateMonitor>();
            try
            {
                monitor.Initialize(false);
                monitor.RestrictToScene(scene);

                // Arranque normal hasta FOCUSED y ambos mandos conectados.
                for (int s = 0; s < 5; s++) Transition(monitor, s, s + 1);
                monitor.SetControllerConnected(true, true);
                monitor.SetControllerConnected(false, true);
                monitor.Tick();
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !controller.InputBlocked && group.interactable,
                    "Desconexión: con todo conectado no hay pausa y NewMainMenu es interactuable");

                // Fuera de la pausa se puede hacer clic en el NewMainMenu, por XRI y por el fallback de gatillo.
                ClickLikeXri(tabButton);
                Check(tabClicks == 1, "Sin pausa: un clic de XRI llega a un botón del NewMainMenu");
                Check(controller.TryClickButton(tabButton) && tabClicks == 2, "Sin pausa: el fallback de gatillo hace clic en un botón del NewMainMenu");
                tabClicks = 0;

                // Se desconecta el control izquierdo: el bloqueo se aplica en ese mismo momento, sin esperar a ningún Update.
                monitor.SetControllerConnected(true, false);
                monitor.Tick();
                Check(VRHardwareStateMonitor.IsConnectionPaused && menu.IsOpen && menu.IsBlockingGameplayInput,
                    "Desconexión: el control izquierdo abre el menú de pausa");
                Check(NoticeOf(menu) == VRHardwareStateMonitor.NOTICE_LEFT_CONTROLLER,
                    "Desconexión: el aviso es NOTICE_LEFT_CONTROLLER");
                Check(controller.InputBlocked, "Desconexión: NewMainMenuController.InputBlocked == true");
                Check(!group.interactable, "Desconexión: el CanvasGroup de NewMainMenu queda no interactuable en el mismo momento (sin Update)");
                Check(!group.blocksRaycasts, "Desconexión: el CanvasGroup de NewMainMenu deja de bloquear raycasts (no tapa el rayo hacia Reanudar)");
                Check(canvasObj.activeSelf && group.alpha == 1f, "Desconexión: NewMainMenu sigue visible (ni alpha ni desactivado)");
                Check(menu.CalibrateButton != null && !menu.CalibrateButton.activeSelf, "Desconexión: 'Calibrar posición' está inactivo en el menú de pausa");
                Check(menu.ResumeButton != null && !menu.ResumeButton.interactable && menu.ResumeBlocked, "Desconexión: Reanudar no es interactuable");
                Check(Time.timeScale == 0f, "Desconexión: Time.timeScale == 0");

                // Un clic de XRI en el frame de la pausa ya no llega: ni al NewMainMenu ni a Reanudar (aún bloqueado).
                ClickLikeXri(tabButton);
                ClickLikeXri(resumeButton);
                Check(tabClicks == 0, "Bloqueo: un clic de XRI no llega a un botón del NewMainMenu");
                Check(resumeClicks == 0, "Bloqueo: Reanudar sigue bloqueado para XRI mientras no vuelva la conexión");

                // Fallback de gatillo: no hace clic en el NewMainMenu ni en Reanudar (bloqueado).
                Check(!controller.TryClickButton(tabButton) && tabClicks == 0, "Bloqueo: el fallback de gatillo no hace clic en un botón del NewMainMenu");
                Check(!controller.TryClickButton(resumeButton) && resumeClicks == 0, "Bloqueo: Reanudar sigue bloqueado para el fallback mientras no vuelva la conexión");

                // Escape durante el bloqueo no abre el modal de salida.
                controller.HandleEscapePressed();
                Check(!controller.IsExitModalOpen, "Bloqueo: Escape no abre el modal de salida");

                // Un gatillo apretado durante el bloqueo actualiza wasPressed (el flanco se informa; el botón lo decide TryClickButton).
                bool wasPressed = false;
                bool click = controller.ProcessTriggerEdge(true, ref wasPressed);
                Check(click && wasPressed, "Bloqueo: el flanco del gatillo se informa y se registra como apretado");
                click = controller.ProcessTriggerEdge(true, ref wasPressed);
                Check(!click && wasPressed, "Bloqueo: mantener apretado el gatillo no es un flanco nuevo");

                // Reconexión: aviso de restablecida, Reanudar habilitado y el bloqueo sigue hasta reanudar.
                monitor.SetControllerConnected(true, true);
                monitor.Tick();
                Check(NoticeOf(menu) == VRHardwareStateMonitor.NOTICE_RESTORED, "Reconexión: el aviso es NOTICE_RESTORED");
                Check(menu.ResumeButton.interactable && !menu.ResumeBlocked, "Reconexión: Reanudar se habilita");
                Check(controller.InputBlocked && !group.interactable, "Reconexión: NewMainMenu sigue bloqueado hasta reanudar");
                ClickLikeXri(resumeButton);
                Check(resumeClicks == 1, "Reconexión: un clic de XRI llega a Reanudar durante la pausa");
                Check(controller.TryClickButton(resumeButton) && resumeClicks == 2, "Reconexión: el fallback de gatillo sí hace clic en Reanudar durante la pausa");
                Check(!controller.TryClickButton(tabButton) && tabClicks == 0, "Reconexión: el fallback de gatillo sigue sin hacer clic en el NewMainMenu");

                // Reanudar (CloseMenu): se desbloquea, se restaura interactable (en el momento) y el tiempo. El gatillo sigue apretado.
                menu.CloseMenu();
                Check(!menu.IsOpen && !VRHardwareStateMonitor.IsConnectionPaused, "Reanudar: cierra el menú y termina la pausa");
                Check(!controller.InputBlocked, "Reanudar: InputBlocked == false");
                Check(Time.timeScale == BASELINE_TIME_SCALE, "Reanudar: Time.timeScale vuelve a su valor previo");
                Check(group.interactable, "Reanudar: se restaura interactable == true en el mismo momento");
                ClickLikeXri(tabButton);
                Check(tabClicks == 1, "Reanudar: un clic de XRI vuelve a llegar al NewMainMenu");
                tabClicks = 0;

                // El gatillo que tocó Reanudar sigue apretado: no hay flanco nuevo, así que no hace clic en el canvas de atrás.
                click = controller.ProcessTriggerEdge(true, ref wasPressed);
                Check(!click, "Sin clics fantasma: el gatillo que sigue apretado tras la pausa no hace clic");
                click = controller.ProcessTriggerEdge(false, ref wasPressed);
                Check(!click && !wasPressed, "Sin clics fantasma: soltar el gatillo no hace clic");
                click = controller.ProcessTriggerEdge(true, ref wasPressed);
                Check(click && wasPressed, "Tras soltar y volver a apretar, el gatillo vuelve a hacer clic");

                // Escape vuelve a funcionar tras reanudar.
                controller.HandleEscapePressed();
                Check(controller.IsExitModalOpen, "Tras reanudar, Escape vuelve a abrir el modal de salida");
                controller.CloseExitModal();

                // Control caído o parpadeando con el gatillo apretado: mientras el dispositivo es inválido se lo trata como apretado,
                // así que al volver hay que soltar y apretar de nuevo; un parpadeo nunca produce un clic sin pulsación nueva.
                bool held = true;
                click = controller.ProcessTriggerSample(false, false, ref held);
                Check(!click && held, "Control caído: con el dispositivo inválido el gatillo se trata como apretado (wasPressed == true)");
                click = controller.ProcessTriggerSample(true, true, ref held);
                Check(!click && held, "Control reconectado con el gatillo todavía apretado (parpadeo): no hay flanco ni clic");
                click = controller.ProcessTriggerSample(true, false, ref held);
                Check(!click && !held, "Control reconectado: soltar el gatillo no hace clic");
                click = controller.ProcessTriggerSample(true, true, ref held);
                Check(click && held, "Control reconectado: soltar y volver a apretar sí es un flanco y hace clic");
                held = false;
                controller.ProcessTriggerSample(false, false, ref held);
                click = controller.ProcessTriggerSample(true, false, ref held);
                Check(!click && !held, "Control reconectado sin gatillo apretado: no hay flanco hasta que se apriete");
                click = controller.ProcessTriggerSample(true, true, ref held);
                Check(click, "Control reconectado sin gatillo apretado: la primera pulsación hace clic");
                held = true;
                click = controller.ProcessTriggerSample(true, true, ref held);
                Check(!click, "Control válido con el gatillo mantenido: sigue sin ser un flanco nuevo");

                // Durante la pausa: el control vuelve con el gatillo apretado, hay que soltar y apretar para tocar Reanudar.
                monitor.SetControllerConnected(true, false);
                monitor.Tick();
                Check(!group.interactable && !group.blocksRaycasts, "Segunda pausa: NewMainMenu no interactuable y sin bloquear raycasts");
                monitor.SetControllerConnected(true, true);
                monitor.Tick();
                held = true;
                controller.ProcessTriggerSample(false, false, ref held);
                click = controller.ProcessTriggerSample(true, true, ref held);
                Check(!click, "Reconexión con el gatillo apretado: el gatillo que volvió apretado no hace clic en Reanudar");
                controller.ProcessTriggerSample(true, false, ref held);
                click = controller.ProcessTriggerSample(true, true, ref held);
                Check(click && controller.TryClickButton(resumeButton) && resumeClicks == 3,
                    "Reconexión: soltar y volver a apretar hace clic en Reanudar");
                menu.CloseMenu();
                Check(!menu.IsOpen && group.interactable && group.blocksRaycasts, "Segunda pausa: Reanudar cierra el menú y se restauran interactable y blocksRaycasts");

                // Se restauran los valores previos de interactable y blocksRaycasts, no un true fijo.
                group.interactable = false;
                group.blocksRaycasts = false;
                monitor.SetControllerConnected(true, false);
                monitor.Tick();
                monitor.SetControllerConnected(true, true);
                monitor.Tick();
                menu.CloseMenu();
                Check(!group.interactable && !group.blocksRaycasts, "Se restauran los valores previos de interactable y blocksRaycasts (false) y no un true fijo");
                group.interactable = true;
                group.blocksRaycasts = true;

                // blocksRaycasts durante la pausa: el canvas bloqueado no captura el rayo hacia Reanudar.
                monitor.SetControllerConnected(true, false);
                monitor.Tick();
                Check(!group.blocksRaycasts && !group.interactable && canvasObj.activeSelf && group.alpha == 1f,
                    "Pausa: blocksRaycasts == false y NewMainMenu sigue visible (alpha y activo intactos)");

                // Desactivar el menú de pausa con la pausa vigente deja el NewMainMenu usable (OnDisable); al reactivarlo vuelve a bloquear.
                InvokeUnityMessage(menu, "OnDisable");
                Check(group.interactable && group.blocksRaycasts,
                    "Menú de pausa desactivado con la pausa vigente: NewMainMenu vuelve a ser interactuable y bloquea raycasts");
                InvokeUnityMessage(menu, "OnDisable");
                Check(group.interactable && group.blocksRaycasts, "Un segundo OnDisable/OnDestroy no duplica ni rompe la notificación");
                InvokeUnityMessage(menu, "OnEnable");
                Check(!group.interactable && !group.blocksRaycasts, "Menú de pausa reactivado con la pausa vigente: NewMainMenu vuelve a bloquearse");

                // MenuRoot desactivado desde afuera con la pausa vigente: la siguiente evaluación (Update) levanta el bloqueo.
                menu.MenuRoot.SetActive(false);
                InvokeUnityMessage(menu, "Update");
                Check(group.interactable && group.blocksRaycasts, "MenuRoot desactivado desde afuera: el bloqueo se levanta en el siguiente Update");
                monitor.Tick(); // el monitor reabre el menú de pausa
                Check(menu.IsBlockingGameplayInput && !group.interactable && !group.blocksRaycasts,
                    "El monitor reabre el menú de pausa y NewMainMenu vuelve a bloquearse");
                monitor.SetControllerConnected(true, true);
                monitor.Tick();
                menu.CloseMenu();
                Check(group.interactable && group.blocksRaycasts, "Tras reanudar NewMainMenu es interactuable y bloquea raycasts");

                // Pausa ya activa al enganchar el controlador (p. ej. al cargar la escena): se bloquea de inmediato.
                controller.DisconnectPauseMenu();
                monitor.SetControllerConnected(true, false);
                monitor.Tick();
                Check(menu.IsBlockingGameplayInput && group.interactable, "Carga con la pausa activa: sin enganchar, el controlador todavía no bloqueó");
                controller.ConnectPauseMenu();
                Check(!group.interactable, "Carga con la pausa activa: al engancharse (OnEnable) el CanvasGroup queda no interactuable");
                controller.DisconnectPauseMenu();
                Check(group.interactable, "OnDisable con la pausa vigente: el canvas no queda no interactuable");
                controller.ConnectPauseMenu();
                monitor.SetControllerConnected(true, true);
                monitor.Tick();
                menu.CloseMenu();
                Check(group.interactable && !controller.InputBlocked, "Tras reanudar el controlador vuelve a ser interactuable");
            }
            finally
            {
                tabButton.onClick.RemoveListener(onTab);
                resumeButton.onClick.RemoveListener(onResume);
                controller.DisconnectPauseMenu();
                monitor.Shutdown();
                UnityEngine.Object.DestroyImmediate(monitorObj);
                if (menu.IsOpen) menu.EndConnectionPause();
                menu.MenuRoot.SetActive(false);
                group.interactable = true;
                SimulationPause.ResetForTests();
                VRSessionStateFeature.ResetForTests();
                Time.timeScale = BASELINE_TIME_SCALE;
                AudioListener.pause = false;
            }
        }

        // Edit Mode no ejecuta OnEnable, OnDisable ni Update de un MonoBehaviour normal (SendMessage registra un error): se invocan
        // por reflection, con los nombres de los mensajes de Unity.
        private static void InvokeUnityMessage(MonoBehaviour behaviour, string message)
        {
            System.Reflection.MethodInfo method = behaviour.GetType().GetMethod(message,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Check(method != null, $"{behaviour.GetType().Name} tiene el mensaje de Unity {message}");
            method?.Invoke(behaviour, null);
        }

        // Un clic como el que entrega XRUIInputModule: el evento pointerClick sobre el botón, que respeta IsInteractable().
        private static void ClickLikeXri(Button button)
        {
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(null), ExecuteEvents.pointerClickHandler);
        }

        private static void Transition(VRHardwareStateMonitor monitor, int oldState, int newState)
        {
            monitor.ProcessSessionTransition(oldState, newState);
            monitor.Tick();
        }

        private static string NoticeOf(InGameMenuController menu) => menu.NoticeLabel != null ? menu.NoticeLabel.text : null;

        private static void Check(bool condition, string testName)
        {
            s_Total++;
            if (condition)
            {
                s_Passed++;
                Debug.Log($"  [OK] {testName}");
            }
            else
            {
                s_Failures.Add(testName);
                Debug.LogError($"  [FAIL] {testName}");
            }
        }
    }
}
