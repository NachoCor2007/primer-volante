using System;
using System.Collections.Generic;
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

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Batería de verificación automatizada para el nuevo menú principal NewMainMenu.
    /// Valida escena, Canvas en World Space, TrackedDeviceGraphicRaycaster, EventSystem,
    /// solapas, selección de lecciones, circuitos integradores, calibración y modal de salida.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
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
            Debug.Log("[NewMainMenuVerification] Iniciando suite de verificación...");

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

            // 1. Canvas WorldSpace y Raycaster
            GameObject canvasObj = GameObject.Find(NewMainMenuSetup.CANVAS_NAME);
            Check(canvasObj != null, "Existe el GameObject NewMainMenu_Canvas");
            if (canvasObj == null) return;

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace, "El Canvas está configurado en WorldSpace");

            TrackedDeviceGraphicRaycaster raycaster = canvasObj.GetComponent<TrackedDeviceGraphicRaycaster>();
            Check(raycaster != null, "El Canvas posee TrackedDeviceGraphicRaycaster para interacción VR");

            GraphicRaycaster fallbackRaycaster = canvasObj.GetComponent<GraphicRaycaster>();
            Check(fallbackRaycaster != null, "El Canvas posee GraphicRaycaster para interacción con mouse/escritorio");

            // 2. Capa UI
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
            Check(allInUILayer, "Todos los elementos del Canvas pertenecen estrictamente a la capa UI (UIOnlyFarCasting)");

            // 3. EventSystem con XRUIInputModule
            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            Check(eventSystem != null, "Existe EventSystem en la escena");
            if (eventSystem != null)
            {
                XRUIInputModule xrModule = eventSystem.GetComponent<XRUIInputModule>();
                Check(xrModule != null, "EventSystem tiene agregado XRUIInputModule");
            }

            // 4. Controlador y Solapas
            NewMainMenuController controller = canvasObj.GetComponent<NewMainMenuController>();
            Check(controller != null, "NewMainMenuController está adjunto al Canvas");
            if (controller == null) return;

            Check(controller.Lessons.Count == 6, "NewMainMenuController tiene cargadas las 6 lecciones del currículum");
            Check(controller.Circuits.Count >= 3, "NewMainMenuController tiene configurados al menos 3 circuitos integradores");

            // 5. Test de Navegación por Solapas
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

            // 6. Test de Selección de Lecciones
            controller.SelectLesson(2);
            Check(controller.SelectedLessonIndex == 2, "Selección: Cambiar a Lección índice 2 (Lección 3)");

            controller.SelectLesson(0);
            Check(controller.SelectedLessonIndex == 0, "Selección: Volver a Lección índice 0 (Lección 1)");

            // 7. Test de Selección de Circuitos
            controller.SelectCircuit(1);
            Check(controller.SelectedCircuitIndex == 1, "Selección: Cambiar a Circuito índice 1");

            // 8. Test de Modal de Salida
            Check(!controller.IsExitModalOpen, "El modal de salida está inicialmente cerrado");
            controller.OpenExitModal();
            Check(controller.IsExitModalOpen, "OpenExitModal abre el modal de confirmación");
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

