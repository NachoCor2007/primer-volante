using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor del bloqueo de la entrada del habitáculo mientras la simulación está pausada:
    /// 1. <see cref="SimulationPause.PausedChanged"/> se dispara solo en las transiciones.
    /// 2. <see cref="NearCastingPauseBlock"/>: apaga/restaura el near casting con conteo de solicitantes y suelta el agarre en curso.
    /// 3. <see cref="InGameMenuController"/> pide y libera el bloqueo junto con su token de pausa (OpenMenu, CloseMenu, pausa por
    ///    desconexión, OnDisable) y las 6 escenas de lección tienen los interactores cableados.
    /// 4. Pedales de <see cref="VehicleController"/>: 0 en pausa y retenidos hasta soltar el gatillo, un latch por pedal.
    /// 5. Los atajos de teclado/Gamepad de cada script revisan la pausa (reglas de código; el comportamiento real se prueba en Play Mode).
    /// 6. <see cref="LessonActionMonitor"/> no evalúa ni acumula progreso en pausa (salvo OpenCalibrationMenu).
    /// Las pruebas con escena usan una copia temporal de Lesson_03. Restaura estáticos, timeScale y escenas en finally.
    /// </summary>
    public static class PauseCabinInputBlockVerification
    {
        private const string LESSON_03_SCENE = "Assets/Scenes/Lessons/Lesson_03_LightingSignals.unity";
        private const BindingFlags k_Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Verify Pause Cabin Input Block")]
        public static void RunVerification()
        {
            s_Total = 0;
            s_Passed = 0;
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DEL BLOQUEO DE ENTRADA DEL HABITACULO EN PAUSA");
            Debug.Log("=========================================================");

            var cleanScenePaths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(open.path) && !open.isDirty) cleanScenePaths.Add(open.path);
            }

            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;
            Scene previousActive = SceneManager.GetActiveScene();
            Func<bool> escapeBefore = LessonActionMonitor.EscapePressedOverrideForTests;
            try
            {
                SimulationPause.ResetForTests();
                Time.timeScale = 1f;
                AudioListener.pause = false;

                VerifyPausedChangedEvent();
                VerifyNearCastingBlockHelper();
                VerifyLessonScenesWiring();
                VerificationSceneUtil.WithSceneCopy(LESSON_03_SCENE, VerifyMenuRequestsBlock);
                VerificationSceneUtil.WithSceneCopy(LESSON_03_SCENE, VerifyPedals);
                VerifyShortcutSourceRules();
                VerificationSceneUtil.WithSceneCopy(LESSON_03_SCENE, VerifyLessonMonitor);

                Check(!SimulationPause.IsPaused, "Al terminar no queda ningún dueño de la pausa de simulación");

                bool stillClean = true;
                foreach (string path in cleanScenePaths)
                {
                    Scene scene = SceneManager.GetSceneByPath(path);
                    if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    {
                        stillClean = false;
                        Debug.LogError($"[FAIL] La verificación dejó sucia la escena abierta {path}");
                    }
                }
                Check(stillClean, "Ninguna escena guardada del usuario quedó modificada (isDirty) tras la verificación");
            }
            catch (Exception ex)
            {
                s_Total++;
                Debug.LogError($"[FAIL] PauseCabinInputBlockVerification: excepción no controlada: {ex}");
            }
            finally
            {
                SimulationPause.ResetForTests();
                Time.timeScale = timeScaleBefore;
                AudioListener.pause = audioPauseBefore;
                LessonActionMonitor.EscapePressedOverrideForTests = escapeBefore;
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }

            Debug.Log("=========================================================");
            if (s_Total > 0 && s_Passed == s_Total)
            {
                Debug.Log($"PAUSE CABIN INPUT BLOCK: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            }
            else
            {
                Debug.LogError($"[FAIL] PAUSE CABIN INPUT BLOCK: {s_Passed}/{s_Total} pruebas pasadas");
            }
            Debug.Log("=========================================================");
        }

        // =========================================================================
        // 1. SimulationPause.PausedChanged
        // =========================================================================
        private static void VerifyPausedChangedEvent()
        {
            SimulationPause.ResetForTests();
            Time.timeScale = 1f;
            var events = new List<bool>();
            Action<bool> handler = events.Add;
            SimulationPause.PausedChanged += handler;
            var ownerA = new GameObject("TMP_PauseOwnerA");
            var ownerB = new GameObject("TMP_PauseOwnerB");
            var ghost = new GameObject("TMP_PauseOwnerGhost");
            try
            {
                SimulationPause.Acquire(ownerA, true);
                Check(events.Count == 1 && events[0], "PausedChanged(true) se dispara en el primer Acquire");

                SimulationPause.Acquire(ownerA, true);
                Check(events.Count == 1, "Acquire doble del mismo dueño no dispara el evento");

                SimulationPause.Acquire(ownerB, false);
                Check(events.Count == 1, "Un segundo dueño no dispara el evento (ya estaba pausado)");

                SimulationPause.Release(ghost);
                Check(events.Count == 1 && SimulationPause.IsPaused, "Release de un no-dueño no dispara el evento");

                SimulationPause.Release(ownerA);
                Check(events.Count == 1 && SimulationPause.IsPaused, "Soltar un dueño con otro vigente no dispara el evento");

                SimulationPause.Release(ownerB);
                Check(events.Count == 2 && !events[1] && !SimulationPause.IsPaused && Time.timeScale == 1f,
                    "PausedChanged(false) se dispara en el último Release, con el timeScale ya restaurado");

                SimulationPause.Release(ownerB);
                Check(events.Count == 2, "Release sin dueños no dispara el evento");

                events.Clear();
                SimulationPause.Acquire(ghost, true);
                Check(events.Count == 1 && events[0], "Vuelve a disparar true en un nuevo Acquire");
                UnityEngine.Object.DestroyImmediate(ghost);
                bool paused = SimulationPause.IsPaused;
                Check(!paused && events.Count == 2 && !events[1] && Time.timeScale == 1f,
                    "Prune de un dueño destruido que deja 0 dueños dispara PausedChanged(false)");
                Check(!SimulationPause.IsPaused && events.Count == 2, "Consultar IsPaused otra vez no repite el evento");

                events.Clear();
                SimulationPause.Acquire(ownerA, true);
                SimulationPause.ResetForTests();
                Check(!SimulationPause.IsPaused && SimulationPause.OwnerCount == 0, "ResetForTests deja la pausa limpia");
                events.Clear();
                Time.timeScale = 1f;
                SimulationPause.Acquire(ownerB, true);
                Check(events.Count == 1 && events[0], "Tras ResetForTests el próximo Acquire vuelve a notificar la transición");
                SimulationPause.Release(ownerB);
            }
            finally
            {
                SimulationPause.PausedChanged -= handler;
                SimulationPause.ResetForTests();
                Time.timeScale = 1f;
                AudioListener.pause = false;
                if (ownerA != null) UnityEngine.Object.DestroyImmediate(ownerA);
                if (ownerB != null) UnityEngine.Object.DestroyImmediate(ownerB);
                if (ghost != null) UnityEngine.Object.DestroyImmediate(ghost);
            }
        }

        // =========================================================================
        // 2. NearCastingPauseBlock
        // =========================================================================
        private sealed class InteractorRig
        {
            public GameObject Root;
            public XRInteractionManager Manager;
            public NearFarInteractor Interactor;
            public XRSimpleInteractable Grab;
            public GameObject GrabObject;

            public IXRSelectInteractor Selector => Interactor;

            public void Destroy()
            {
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            }
        }

        private static InteractorRig NewRig(bool nearCasting)
        {
            var rig = new InteractorRig { Root = new GameObject("TMP_NearBlockRig") };
            rig.Manager = rig.Root.AddComponent<XRInteractionManager>();

            var interactorGo = new GameObject("TMP_NearBlockInteractor");
            interactorGo.transform.SetParent(rig.Root.transform);
            rig.Interactor = interactorGo.AddComponent<NearFarInteractor>();
            rig.Interactor.interactionManager = rig.Manager;
            // En Edit Mode no corre OnEnable: el anchor del attach controller se crea a mano, o el interactor lanza al seleccionar.
            var attach = interactorGo.AddComponent<InteractionAttachController>();
            ((IInteractionAttachController)attach).GetOrCreateAnchorTransform();
            rig.Interactor.interactionAttachController = attach;
            rig.Interactor.enableNearCasting = nearCasting;

            rig.GrabObject = new GameObject("TMP_NearBlockGrab");
            rig.GrabObject.transform.SetParent(rig.Root.transform);
            rig.GrabObject.AddComponent<BoxCollider>();
            rig.Grab = rig.GrabObject.AddComponent<XRSimpleInteractable>();
            rig.Grab.interactionManager = rig.Manager;

            rig.Manager.RegisterInteractor((IXRInteractor)rig.Interactor);
            rig.Manager.RegisterInteractable((IXRInteractable)rig.Grab);
            return rig;
        }

        private static void VerifyNearCastingBlockHelper()
        {
            var requesterA = new GameObject("TMP_BlockRequesterA");
            var requesterB = new GameObject("TMP_BlockRequesterB");
            InteractorRig rig = null;
            try
            {
                rig = NewRig(true);
                NearFarInteractor interactor = rig.Interactor;

                NearCastingPauseBlock.Request(null, requesterA);
                NearCastingPauseBlock.Request(interactor, null);
                NearCastingPauseBlock.Release(null, requesterA);
                NearCastingPauseBlock.Release(interactor, requesterA);
                Check(NearCastingPauseBlock.Get(interactor) == null && interactor.enableNearCasting,
                    "Request/Release con null o sin componente previo no hacen nada ni lanzan");

                NearCastingPauseBlock.Request(interactor, requesterA);
                NearCastingPauseBlock block = NearCastingPauseBlock.Get(interactor);
                Check(block != null && block.IsActive && !interactor.enableNearCasting && block.hideFlags == HideFlags.DontSave,
                    "Request apaga el near casting (componente runtime DontSave)");

                NearCastingPauseBlock.Request(interactor, requesterA);
                Check(block.ActiveRequesterCount == 1 && !interactor.enableNearCasting, "Request repetido del mismo solicitante es idempotente");

                NearCastingPauseBlock.Request(interactor, requesterB);
                Check(block.ActiveRequesterCount == 2, "Dos solicitantes conviven");
                NearCastingPauseBlock.Release(interactor, requesterA);
                Check(block.IsActive && !interactor.enableNearCasting, "Al soltar uno de dos solicitantes el near casting sigue apagado");
                NearCastingPauseBlock.Release(interactor, requesterB);
                Check(!block.IsActive && interactor.enableNearCasting, "El último Release restaura el valor original (true)");

                // Original false: se restaura false (no se fuerza a true)
                interactor.enableNearCasting = false;
                NearCastingPauseBlock.Request(interactor, requesterA);
                NearCastingPauseBlock.Request(interactor, requesterA);
                NearCastingPauseBlock.Release(interactor, requesterA);
                Check(!interactor.enableNearCasting && !block.IsActive, "Con original false, el último Release lo deja en false");

                // Un Request no pisa el original guardado
                interactor.enableNearCasting = true;
                NearCastingPauseBlock.Request(interactor, requesterA);
                NearCastingPauseBlock.Request(interactor, requesterB);
                NearCastingPauseBlock.Release(interactor, requesterA);
                NearCastingPauseBlock.Release(interactor, requesterB);
                Check(interactor.enableNearCasting, "Solicitudes solapadas no pierden el original (true)");

                // Solicitante destruido: se poda
                NearCastingPauseBlock.Request(interactor, requesterA);
                NearCastingPauseBlock.Request(interactor, requesterB);
                UnityEngine.Object.DestroyImmediate(requesterA);
                Check(block.ActiveRequesterCount == 1, "Un solicitante destruido se poda del conteo");
                NearCastingPauseBlock.Release(interactor, requesterB);
                Check(!block.IsActive && interactor.enableNearCasting, "Con el solicitante destruido podado, el Release del otro restaura el near casting");

                // Solicitante destruido sin Release: Update lo detecta y restaura
                var lone = new GameObject("TMP_BlockRequesterLone");
                NearCastingPauseBlock.Request(interactor, lone);
                UnityEngine.Object.DestroyImmediate(lone);
                Invoke(block, "Update");
                Check(!block.IsActive && interactor.enableNearCasting, "Si el único solicitante se destruye sin liberar, Update restaura el near casting");

                // Agarre en curso: Request lo suelta
                var exits = new List<bool>();
                rig.Grab.selectExited.AddListener(args => exits.Add(args.isCanceled));
                rig.Manager.SelectEnter(rig.Selector, (IXRSelectInteractable)rig.Grab);
                Check(rig.Selector.interactablesSelected.Count == 1 && rig.Grab.isSelected, "Preparación: el interactor tiene el objeto agarrado");
                NearCastingPauseBlock.Request(interactor, requesterB);
                Check(rig.Selector.interactablesSelected.Count == 0 && !rig.Grab.isSelected && exits.Count == 1 && !exits[0],
                    "Request suelta el agarre en curso (SelectExit normal, no cancelado)");
                Check(!interactor.enableNearCasting, "Y deja el near casting apagado");
                NearCastingPauseBlock.Release(interactor, requesterB);
                Check(interactor.enableNearCasting && rig.Selector.interactablesSelected.Count == 0,
                    "Al soltar el bloqueo se restaura el near casting y no se re-agarra nada");

                // Destruir el componente restaura el near casting
                NearCastingPauseBlock.Request(interactor, requesterB);
                Invoke(NearCastingPauseBlock.Get(interactor), "OnDestroy"); // en Edit Mode Unity no lo invoca solo
                Check(interactor.enableNearCasting, "OnDestroy del componente con solicitantes restaura el near casting");
            }
            finally
            {
                if (requesterA != null) UnityEngine.Object.DestroyImmediate(requesterA);
                if (requesterB != null) UnityEngine.Object.DestroyImmediate(requesterB);
                rig?.Destroy();
            }
        }

        // =========================================================================
        // 3. InGameMenuController
        // =========================================================================
        private static void VerifyLessonScenesWiring()
        {
            foreach (string path in LessonHUDMinimizeSetup.LESSON_SCENE_PATHS)
            {
                string label = Path.GetFileNameWithoutExtension(path);
                VerificationSceneUtil.WithScene(path, scene =>
                {
                    InGameMenuController[] menus = VerificationSceneUtil.AllInScene<InGameMenuController>(scene);
                    NearFarInteractor[] interactors = menus.Length == 1 ? FarCastInteractors(menus[0]) : null;
                    int wired = 0;
                    if (interactors != null)
                    {
                        foreach (NearFarInteractor i in interactors) if (i != null) wired++;
                    }
                    Check(menus.Length == 1 && wired == 2, $"{label}: el menú tiene sus 2 NearFarInteractor cableados ({wired})");
                });
            }
        }

        private static NearFarInteractor[] FarCastInteractors(InGameMenuController menu)
        {
            FieldInfo field = typeof(InGameMenuController).GetField("m_FarCastInteractors", k_Instance);
            return field != null ? field.GetValue(menu) as NearFarInteractor[] : null;
        }

        private static bool AllNearCasting(NearFarInteractor[] interactors, bool expected)
        {
            foreach (NearFarInteractor i in interactors)
            {
                if (i.enableNearCasting != expected) return false;
            }
            return true;
        }

        private static void VerifyMenuRequestsBlock(Scene scene)
        {
            InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene)[0];
            NearFarInteractor[] interactors = FarCastInteractors(menu);
            Check(interactors != null && interactors.Length == 2 && interactors[0] != null && interactors[1] != null,
                "Lesson_03: el menú tiene los 2 interactores de las manos");
            if (interactors == null || interactors.Length != 2) return;

            menu.MenuRoot.SetActive(false);
            SimulationPause.ResetForTests();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            interactors[0].enableNearCasting = true;
            interactors[1].enableNearCasting = true;
            try
            {
                menu.OpenMenu();
                Check(SimulationPause.IsPaused && AllNearCasting(interactors, false)
                      && NearCastingPauseBlock.Get(interactors[0]).IsActive && NearCastingPauseBlock.Get(interactors[1]).IsActive,
                    "OpenMenu: near casting apagado en ambos interactores");
                menu.CloseMenu();
                Check(!SimulationPause.IsPaused && AllNearCasting(interactors, true), "CloseMenu: near casting restaurado en ambos interactores");

                // El valor original de cada mano se respeta
                interactors[1].enableNearCasting = false;
                menu.OpenMenu();
                menu.CloseMenu();
                Check(interactors[0].enableNearCasting && !interactors[1].enableNearCasting, "CloseMenu restaura el original de cada mano (true y false)");
                interactors[1].enableNearCasting = true;

                // Pausa por desconexión VR (el menú abre solo con aviso)
                menu.OpenMenuWithNotice("Aviso de prueba");
                Check(SimulationPause.IsPaused && AllNearCasting(interactors, false), "OpenMenuWithNotice (pausa por desconexión): near casting apagado");
                menu.OpenMenuWithNotice("Otro aviso");
                menu.EndConnectionPause();
                Check(!SimulationPause.IsPaused && AllNearCasting(interactors, true), "EndConnectionPause: near casting restaurado");

                // OnDisable con el menú abierto
                menu.OpenMenu();
                Check(AllNearCasting(interactors, false), "Preparación: menú abierto con el near casting apagado");
                Invoke(menu, "OnDisable");
                Check(!SimulationPause.IsPaused && AllNearCasting(interactors, true), "OnDisable con el menú abierto: near casting restaurado");
                Invoke(menu, "OnEnable");
                menu.MenuRoot.SetActive(false);

                // Pausa de simulación apagada en la escena: no hay pausa, no hay bloqueo
                FieldInfo pauseField = typeof(InGameMenuController).GetField("m_PauseSimulation", k_Instance);
                bool pauseBefore = (bool)pauseField.GetValue(menu);
                pauseField.SetValue(menu, false);
                try
                {
                    menu.OpenMenu();
                    Check(!SimulationPause.IsPaused && AllNearCasting(interactors, true), "Sin m_PauseSimulation el menú no pausa ni bloquea el near casting");
                    menu.CloseMenu();
                }
                finally
                {
                    pauseField.SetValue(menu, pauseBefore);
                }

                // Los bloqueos por paso de CockpitControlLockManager (interactionLayers) no se tocan
                CockpitControlLockManager lockManager = VerificationSceneUtil.AllInScene<CockpitControlLockManager>(scene).Length > 0
                    ? VerificationSceneUtil.AllInScene<CockpitControlLockManager>(scene)[0] : null;
                var layersBefore = new List<int>();
                XRBaseInteractable[] interactables = VerificationSceneUtil.AllInScene<XRBaseInteractable>(scene);
                foreach (XRBaseInteractable i in interactables) layersBefore.Add(i.interactionLayers.value);
                menu.OpenMenu();
                menu.CloseMenu();
                bool layersSame = true;
                for (int i = 0; i < interactables.Length; i++) layersSame &= interactables[i].interactionLayers.value == layersBefore[i];
                Check(interactables.Length > 0 && layersSame, $"Abrir y cerrar el menú no cambia las interactionLayers de los {interactables.Length} interactables{(lockManager != null ? "" : " (sin lock manager)")}");
            }
            finally
            {
                if (menu != null && menu.MenuRoot != null) menu.MenuRoot.SetActive(false);
                SimulationPause.ResetForTests();
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }

        // =========================================================================
        // 4. Pedales
        // =========================================================================
        private static void VerifyPedals(Scene scene)
        {
            VehicleController vehicle = VerificationSceneUtil.AllInScene<VehicleController>(scene)[0];

            vehicle.ApplyPedalInputForTests(0f, 0f, false);
            vehicle.ApplyPedalInputForTests(0.7f, 0.9f, false);
            Check(Mathf.Approximately(vehicle.ThrottleValue, 0.7f) && Mathf.Approximately(vehicle.BrakeValue, 0.9f), "Sin pausa, los pedales leen el valor crudo");

            vehicle.ApplyPedalInputForTests(1f, 1f, true);
            Check(vehicle.ThrottleValue == 0f && vehicle.BrakeValue == 0f, "En pausa, acelerador y freno valen 0 aunque los gatillos estén a fondo");

            vehicle.ApplyPedalInputForTests(1f, 1f, true);
            vehicle.ApplyPedalInputForTests(0f, 1f, false);
            Check(vehicle.ThrottleValue == 0f && vehicle.BrakeValue == 0f, "Tras la pausa, el freno sigue en 0 mientras el gatillo siga apretado");

            vehicle.ApplyPedalInputForTests(0.5f, 1f, false);
            Check(vehicle.BrakeValue == 0f && Mathf.Approximately(vehicle.ThrottleValue, 0.5f),
                "El latch es independiente por pedal: el acelerador ya liberado se lee mientras el freno sigue retenido");

            vehicle.ApplyPedalInputForTests(0.5f, 0.3f, false);
            Check(vehicle.BrakeValue == 0f, "Bajar el gatillo sin llegar al umbral no libera el freno");

            vehicle.ApplyPedalInputForTests(0.5f, 0.04f, false);
            Check(vehicle.BrakeValue == 0.04f, "Por debajo del umbral (0.05) se libera el latch");

            vehicle.ApplyPedalInputForTests(0.5f, 0f, false);
            vehicle.ApplyPedalInputForTests(0.5f, 0.8f, false);
            Check(Mathf.Approximately(vehicle.BrakeValue, 0.8f), "Al soltar y volver a apretar el freno se lee otra vez");

            // Acelerador retenido
            vehicle.ApplyPedalInputForTests(0f, 0f, false);
            vehicle.ApplyPedalInputForTests(0.6f, 0f, true);
            vehicle.ApplyPedalInputForTests(0.6f, 0f, false);
            Check(vehicle.ThrottleValue == 0f, "El acelerador apretado al reanudar queda retenido");
            vehicle.ApplyPedalInputForTests(0f, 0f, false);
            vehicle.ApplyPedalInputForTests(0.6f, 0f, false);
            Check(Mathf.Approximately(vehicle.ThrottleValue, 0.6f), "Soltar y apretar el acelerador lo libera");

            // Una pausa nunca deja un valor residual: pasar a pausa pone 0 enseguida
            vehicle.ApplyPedalInputForTests(0.6f, 0.6f, true);
            Check(vehicle.ThrottleValue == 0f && vehicle.BrakeValue == 0f, "Pausar con los pedales apretados los pone en 0 en el mismo frame");
            vehicle.ApplyPedalInputForTests(0f, 0f, false);
        }

        // =========================================================================
        // 5. Atajos (reglas de código; el comportamiento real se prueba en Play Mode)
        // =========================================================================
        private static void VerifyShortcutSourceRules()
        {
            (string path, string method)[] shortcuts =
            {
                ("Assets/Scripts/VR/VehicleController.cs", "private void Update()"),
                ("Assets/Scripts/VR/VRGearShifter.cs", "private void Update()"),
                ("Assets/Scripts/VR/VRHandbrake.cs", "private void Update()"),
                ("Assets/Scripts/VR/VRHeadlightKnob.cs", "private void HandleDebugInputs()"),
                ("Assets/Scripts/VR/VRRearviewMirror.cs", "private void Update()"),
                ("Assets/Scripts/VR/VRSideMirrorControlPanel.cs", "private void Update()"),
            };
            foreach ((string path, string method) in shortcuts)
            {
                string source = File.ReadAllText(path);
                string body = MethodBody(source, method);
                int check = body.IndexOf("SimulationPause.IsPaused", StringComparison.Ordinal);
                int firstInput = body.IndexOf("Keyboard.current", StringComparison.Ordinal);
                int firstGamepad = body.IndexOf("Gamepad.current", StringComparison.Ordinal);
                string name = Path.GetFileNameWithoutExtension(path);
                Check(body.Length > 0 && check >= 0, $"{name}.{method.Replace("private void ", "")} consulta SimulationPause.IsPaused");
                if (path.EndsWith("VehicleController.cs")) continue; // lee primero el input crudo de los pedales y filtra cada atajo
                bool before = (firstInput < 0 || check < firstInput) && (firstGamepad < 0 || check < firstGamepad);
                Check(before, $"{name}: el chequeo de pausa va antes de leer teclado/Gamepad");
            }

            string vehicleBody = MethodBody(File.ReadAllText("Assets/Scripts/VR/VehicleController.cs"), "private void Update()");
            Check(vehicleBody.Contains("!paused && Gamepad.current.buttonSouth") && vehicleBody.Contains("!paused && Keyboard.current.eKey")
                  && vehicleBody.Contains("!paused && Keyboard.current != null") && vehicleBody.Contains("!paused && Gamepad.current != null"),
                "VehicleController: motor (E/buttonSouth), guiños y balizas (Z/C/H/D-Pad) filtran por la pausa");

            string wheel = File.ReadAllText("Assets/Scripts/VR/VRSteeringWheel.cs");
            Check(wheel.Contains("Time.deltaTime") && !wheel.Contains("Time.unscaledDeltaTime"),
                "VRSteeringWheel: el teclado/Gamepad solo mueve el volante con Time.deltaTime (0 en pausa), no necesita filtro propio");
        }

        // =========================================================================
        // 6. LessonActionMonitor
        // =========================================================================
        private sealed class MonitorRig
        {
            public LessonActionMonitor Monitor;
            public readonly List<LessonActionType> Completed = new List<LessonActionType>();
            public LessonStepSO Step;
        }

        private static MonitorRig NewMonitor(VehicleController vehicle, InGameMenuController menu, VRSideMirrorControlPanel panel)
        {
            var go = new GameObject("TMP_PauseLessonMonitor");
            var rig = new MonitorRig { Monitor = go.AddComponent<LessonActionMonitor>() };
            rig.Monitor.VehicleController = vehicle;
            rig.Monitor.MenuController = menu;
            if (panel != null) rig.Monitor.SideMirrorPanel = panel;
            rig.Monitor.ActionCompleted += step => rig.Completed.Add(step.requiredAction);
            return rig;
        }

        private static void SetStep(MonitorRig rig, LessonActionType action, float parameter = 0f)
        {
            if (rig.Step != null) UnityEngine.Object.DestroyImmediate(rig.Step);
            rig.Step = ScriptableObject.CreateInstance<LessonStepSO>();
            rig.Step.completionMode = LessonStepCompletionMode.ActionRequired;
            rig.Step.requiredAction = action;
            rig.Step.actionNumericParameter = parameter;
            MethodInfo onStep = typeof(LessonActionMonitor).GetMethod("OnStepChanged", k_Instance);
            onStep.Invoke(rig.Monitor, new object[] { rig.Step, 0, 1 });
        }

        private static bool GetFlag(LessonActionMonitor monitor, string field)
        {
            return (bool)typeof(LessonActionMonitor).GetField(field, k_Instance).GetValue(monitor);
        }

        private static void VerifyLessonMonitor(Scene scene)
        {
            VehicleController vehicle = VerificationSceneUtil.AllInScene<VehicleController>(scene)[0];
            InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene)[0];
            VRSideMirrorControlPanel[] panels = VerificationSceneUtil.AllInScene<VRSideMirrorControlPanel>(scene);
            VRSideMirrorControlPanel panel = panels.Length > 0 ? panels[0] : null;

            menu.MenuRoot.SetActive(false);
            SimulationPause.ResetForTests();
            Time.timeScale = 1f;
            AudioListener.pause = false;
            var rigs = new List<MonitorRig>();
            var owner = new GameObject("TMP_PauseOwnerMonitor");
            try
            {
                // --- Guiño derecho: con pausa no completa; al reanudar sí, una sola vez
                vehicle.SetTurnSignal(TurnSignalState.Off);
                MonitorRig rig = NewMonitor(vehicle, menu, panel);
                rigs.Add(rig);
                SetStep(rig, LessonActionType.ActivateTurnSignalRight);

                SimulationPause.Acquire(owner, true);
                vehicle.SetTurnSignal(TurnSignalState.Right);
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 0 && !rig.Monitor.IsCompletingStep, "Con pausa activa, guiño derecho no dispara ActionCompleted");
                Check(rig.Monitor.IsConditionSatisfied(rig.Step), "IsConditionSatisfied sigue siendo una consulta pura (no depende de la pausa)");

                SimulationPause.Release(owner);
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 1 && rig.Completed[0] == LessonActionType.ActivateTurnSignalRight, "Al reanudar el paso se evalúa con el estado real y se completa");
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 1, "El paso completado no se vuelve a disparar");

                // --- Secuencia derecha -> izquierda durante la pausa: no completa dos pasos al reanudar
                vehicle.SetTurnSignal(TurnSignalState.Off);
                rig = NewMonitor(vehicle, menu, panel);
                rigs.Add(rig);
                SetStep(rig, LessonActionType.ActivateTurnSignalRight);
                SimulationPause.Acquire(owner, true);
                vehicle.SetTurnSignal(TurnSignalState.Right);
                rig.Monitor.CheckActionProgress();
                vehicle.SetTurnSignal(TurnSignalState.Left);
                rig.Monitor.CheckActionProgress();
                SimulationPause.Release(owner);
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 0, "Derecha -> izquierda en pausa y reanudar: el paso 'derecha' no se completa por un guiño que ya no está");
                SetStep(rig, LessonActionType.ActivateTurnSignalLeft);
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 1, "Y el paso 'izquierda' se completa recién en su evaluación (uno solo)");
                vehicle.SetTurnSignal(TurnSignalState.Off);

                // --- Un paso ya cumplido antes de la pausa conserva su estado de "completando"
                rig = NewMonitor(vehicle, menu, panel);
                rigs.Add(rig);
                SetStep(rig, LessonActionType.ActivateHazardLights);
                vehicle.SetHazardActive(true);
                rig.Monitor.CheckActionProgress();
                SimulationPause.Acquire(owner, true);
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 1 && rig.Monitor.IsCompletingStep, "Un paso cumplido antes de la pausa sigue 'completando' y no se re-dispara en la pausa");
                SimulationPause.Release(owner);
                vehicle.SetHazardActive(false);

                // --- Eventos del vehículo (balizas) también respetan la pausa
                rig = NewMonitor(vehicle, menu, panel);
                rigs.Add(rig);
                SetStep(rig, LessonActionType.ActivateHazardLights);
                SimulationPause.Acquire(owner, true);
                vehicle.SetHazardActive(true);
                Invoke(rig.Monitor, "OnHazardChanged", true);
                Check(rig.Completed.Count == 0, "El evento OnHazardChanged en pausa tampoco completa el paso");
                SimulationPause.Release(owner);
                Invoke(rig.Monitor, "OnHazardChanged", true);
                Check(rig.Completed.Count == 1, "El mismo evento fuera de la pausa sí lo completa");
                vehicle.SetHazardActive(false);

                // --- RotateSteeringWheel no acumula flags en pausa
                VRSteeringWheel wheel = vehicle.SteeringWheel;
                if (wheel != null)
                {
                    rig = NewMonitor(vehicle, menu, panel);
                    rigs.Add(rig);
                    SetStep(rig, LessonActionType.RotateSteeringWheel, 20f);
                    SimulationPause.Acquire(owner, true);
                    wheel.SetAngleProgrammatic(-90f);
                    rig.Monitor.CheckActionProgress();
                    wheel.SetAngleProgrammatic(90f);
                    rig.Monitor.CheckActionProgress();
                    Check(!GetFlag(rig.Monitor, "m_SteeringTurnedLeft") && !GetFlag(rig.Monitor, "m_SteeringTurnedRight") && rig.Completed.Count == 0,
                        "RotateSteeringWheel no acumula flags de giro en pausa");
                    SimulationPause.Release(owner);
                    rig.Monitor.CheckActionProgress();
                    Check(GetFlag(rig.Monitor, "m_SteeringTurnedRight") && !GetFlag(rig.Monitor, "m_SteeringTurnedLeft") && rig.Completed.Count == 0,
                        "Al reanudar acumula solo el estado real (derecha) y no completa");
                    wheel.SetAngleProgrammatic(0f);
                }
                else
                {
                    Check(false, "El auto de Lesson_03 tiene volante");
                }

                // --- Espejos laterales: el evento del panel no acumula en pausa
                if (panel != null)
                {
                    rig = NewMonitor(vehicle, menu, panel);
                    rigs.Add(rig);
                    SetStep(rig, LessonActionType.AdjustLeftMirror);
                    SimulationPause.Acquire(owner, true);
                    Invoke(rig.Monitor, "OnMirrorAdjusted", VRSideMirrorControlPanel.MirrorSelection.Left, Vector2.up);
                    Check(!GetFlag(rig.Monitor, "m_LeftMirrorAdjusted") && rig.Completed.Count == 0, "OnMirrorAdjusted en pausa no marca el espejo izquierdo ni completa el paso");
                    SimulationPause.Release(owner);
                    Invoke(rig.Monitor, "OnMirrorAdjusted", VRSideMirrorControlPanel.MirrorSelection.Left, Vector2.up);
                    Check(GetFlag(rig.Monitor, "m_LeftMirrorAdjusted") && rig.Completed.Count == 1, "Fuera de la pausa el mismo evento sí lo marca y completa el paso");
                }

                // --- OpenCalibrationMenu sigue funcionando: necesita ver el menú abierto (que pausa la simulación)
                rig = NewMonitor(vehicle, menu, panel);
                rigs.Add(rig);
                SetStep(rig, LessonActionType.OpenCalibrationMenu);
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 0, "OpenCalibrationMenu: con el menú cerrado no se cumple");
                menu.OpenMenu();
                Check(SimulationPause.IsPaused, "Preparación: el menú abierto pausa la simulación");
                rig.Monitor.CheckActionProgress();
                Check(GetFlag(rig.Monitor, "m_CalibrationMenuWasOpened") && rig.Completed.Count == 0,
                    "OpenCalibrationMenu: con el menú abierto (pausa activa) se marca como abierto y todavía no se cumple");
                menu.CloseMenu();
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 1, "OpenCalibrationMenu: al cerrar el menú se cumple (comportamiento previo)");

                // --- OpenCalibrationMenu con pausa por desconexión VR sigue sin contar (regla previa)
                rig = NewMonitor(vehicle, menu, panel);
                rigs.Add(rig);
                SetStep(rig, LessonActionType.OpenCalibrationMenu);
                menu.OpenMenuWithNotice("Aviso");
                rig.Monitor.CheckActionProgress();
                menu.EndConnectionPause();
                rig.Monitor.CheckActionProgress();
                Check(rig.Completed.Count == 0, "OpenCalibrationMenu: la pausa por desconexión sigue sin contar como abrir el menú");
            }
            finally
            {
                foreach (MonitorRig r in rigs)
                {
                    if (r.Step != null) UnityEngine.Object.DestroyImmediate(r.Step);
                    if (r.Monitor != null) UnityEngine.Object.DestroyImmediate(r.Monitor.gameObject);
                }
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
                if (menu != null && menu.MenuRoot != null) menu.MenuRoot.SetActive(false);
                SimulationPause.ResetForTests();
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }

        // =========================================================================
        // Utilidades
        // =========================================================================
        // Cuerpo de un método a partir de su firma (hasta la llave de cierre con la misma sangría).
        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return string.Empty;

            int open = source.IndexOf('{', start);
            if (open < 0) return string.Empty;

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            return string.Empty;
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(method, k_Instance);
            if (m == null) throw new MissingMethodException(target.GetType().Name, method);
            try { m.Invoke(target, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

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
                Debug.LogError($"[FAIL] {testName}");
            }
        }
    }
}
