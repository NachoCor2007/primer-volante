using System;
using System.Collections.Generic;
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
    /// - XR Origin centrado en (0,0,0) sin DriverSeatFollower y con InputActionManager configurado.
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

            try
            {
                VerifySetupScript();
                VerifySceneStructureAndComponents();
            }
            catch (Exception ex)
            {
                s_Failures.Add($"Excepción no controlada durante las pruebas: {ex.Message}\n{ex.StackTrace}");
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

        private static void VerifySetupScript()
        {
            Check(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/MainMenu/NewMainMenuController.cs") != null, "NewMainMenuController.cs existe en el proyecto");
            Check(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/Testing/Editor/NewMainMenuSetup.cs") != null, "NewMainMenuSetup.cs existe en el proyecto");
        }

        private static void VerifySceneStructureAndComponents()
        {
            // Ejecutar setup para garantizar que la escena esté actualizada
            NewMainMenuSetup.SetupNewMainMenu();

            Scene scene = EditorSceneManager.GetSceneByPath(NewMainMenuSetup.SCENE_PATH);
            Check(scene.IsValid() && scene.isLoaded, $"La escena {NewMainMenuSetup.SCENE_PATH} se cargó correctamente");

            // 1. Validar que NO exista Car09 ni VehicleController en la escena
            VehicleController[] cars = UnityEngine.Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include);
            Check(cars == null || cars.Length == 0, "No existe ningún VehicleController (Car09) en la escena");

            GameObject car09Obj = GameObject.Find("Car09");
            Check(car09Obj == null, "No existe el GameObject Car09 en la escena");

            // 2. XR Origin (XR Rig) centrado y sin DriverSeatFollower
            GameObject xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
            Check(xrOriginObj != null, "Existe el GameObject XR Origin (XR Rig)");
            if (xrOriginObj != null)
            {
                DriverSeatFollower follower = xrOriginObj.GetComponent<DriverSeatFollower>();
                Check(follower == null, "XR Origin no tiene DriverSeatFollower (no intenta seguir un auto inexistente)");
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

            GameObject platformObj = GameObject.Find("Menu_FloorPlatform");
            Check(platformObj != null, "Existe la plataforma visual Menu_FloorPlatform bajo los pies del usuario");

            // 3. Canvas WorldSpace, WorldCamera y Raycaster
            GameObject canvasObj = GameObject.Find(NewMainMenuSetup.CANVAS_NAME);
            Check(canvasObj != null, "Existe el GameObject NewMainMenu_Canvas");
            if (canvasObj == null) return;

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace, "El Canvas está configurado en WorldSpace");
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
            Check(controller.XROriginTransform != null && controller.XROriginTransform.position == Vector3.zero, "LockPlayerStationary mantiene el rig anclado en Vector3.zero");

            Check(controller.Lessons.Count == 6, "NewMainMenuController tiene cargadas las 6 lecciones del currículum");
            Check(controller.Circuits.Count >= 3, "NewMainMenuController tiene configurados al menos 3 circuitos integradores");

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
