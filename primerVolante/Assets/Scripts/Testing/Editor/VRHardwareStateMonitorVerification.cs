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

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la pausa por desconexión VR (issue #5, US40-US41): la lógica de decisión de
    /// <see cref="VRHardwareStateMonitor"/> con estados inyectados (sesión, presencia, mandos), el camino de
    /// <see cref="VRSessionStateFeature"/>, la reacción del menú in-game (aviso, bloqueo de Reanudar, pausa de tiempo y audio),
    /// los visuales de los controles (focus aware), el ajuste del menú principal y del paso OpenCalibrationMenu de la lección 1.
    /// Las escenas se verifican sobre copias temporales (<see cref="VerificationSceneUtil.WithSceneCopy"/>).
    /// </summary>
    public static class VRHardwareStateMonitorVerification
    {
        private const string LESSON_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        private const float BASELINE_TIME_SCALE = 0.75f;

        private static Scene s_Scratch; // escena vacía de trabajo: no tiene menú ni rig
        private static Scene s_Scene; // escena (copia) que se está verificando: el monitor solo busca ahí
        private static int s_Total;
        private static int s_Passed;
        private static readonly List<string> s_Logs = new List<string>();

        [MenuItem("Tools/Primer Volante/Run VR Hardware State Monitor Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE LA PAUSA POR DESCONEXION VR");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;

            // Los objetos temporales se crean en una escena de trabajo aditiva y vacía, así no se ensucia la escena abierta.
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);
            s_Scratch = scratch;
            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;
            Application.logMessageReceived += OnLog;
            try
            {
                VerifyPureLogic();
                VerifyFeaturePath();
                VerifySimulationPause();
                VerificationSceneUtil.WithSceneCopy(LESSON_SCENE_PATH, VerifyLessonScene);
                VerificationSceneUtil.WithSceneCopy(MainMenuSetup.SCENE_PATH, VerifyMainMenuScene);
                Check(!SimulationPause.IsPaused, "Al terminar todos los escenarios no queda ningún token de pausa (nada se filtra)");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                VRSessionStateFeature.ResetForTests();
                SimulationPause.ResetForTests();
                Time.timeScale = timeScaleBefore;
                AudioListener.pause = audioPauseBefore;
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (scratch.IsValid()) EditorSceneManager.CloseScene(scratch, true);
            }

            Debug.Log("=========================================================");
            if (s_Passed == s_Total) Debug.Log($"RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            else Debug.LogError($"[FAIL] RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas");
            Debug.Log("=========================================================");
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith(VRHardwareStateMonitor.LOG_PREFIX)) s_Logs.Add(message);
        }

        // ------------------------------------------------------------------ Lógica pura

        private static void VerifyPureLogic()
        {
            const int FOCUSED = VRSessionStateFeature.STATE_FOCUSED;
            const int VISIBLE = VRSessionStateFeature.STATE_VISIBLE;
            const int LOSS = VRSessionStateFeature.STATE_LOSS_PENDING;

            Check(VRHardwareStateMonitor.ComputeReasons(false, VISIBLE, false, true, false, false, false, false) == VRPauseReason.None,
                "ComputeReasons: sin armar, la sesión en VISIBLE no pausa");
            Check(VRHardwareStateMonitor.ComputeReasons(true, VISIBLE, false, true, false, false, false, false) == VRPauseReason.SessionLostFocus,
                "ComputeReasons: armado, VISIBLE = SessionLostFocus");
            Check(VRHardwareStateMonitor.ComputeReasons(true, LOSS, true, true, false, false, false, false) == VRPauseReason.SessionLost,
                "ComputeReasons: armado, LOSS_PENDING = SessionLost");
            Check(VRHardwareStateMonitor.ComputeReasons(false, VISIBLE, false, false, false, false, false, false) == VRPauseReason.None,
                "ComputeReasons: sin armar, la presencia en false no pausa");
            Check(VRHardwareStateMonitor.ComputeReasons(true, FOCUSED, false, false, false, false, false, false) == VRPauseReason.UserNotPresent,
                "ComputeReasons: armado y FOCUSED, presencia false = UserNotPresent");
            Check(VRHardwareStateMonitor.ComputeReasons(false, 0, false, true, false, false, false, false) == VRPauseReason.None,
                "ComputeReasons: mandos que nunca se conectaron no cuentan");
            Check(VRHardwareStateMonitor.ComputeReasons(false, 0, false, true, true, false, true, true) == VRPauseReason.LeftControllerDisconnected,
                "ComputeReasons: control izquierdo conectado alguna vez y ahora no = LeftControllerDisconnected (sin sesión)");

            Check(VRHardwareStateMonitor.BuildNotice(VRPauseReason.SessionLostFocus, false) == VRHardwareStateMonitor.NOTICE_HEADSET,
                "BuildNotice: sin foco = texto del visor");
            Check(VRHardwareStateMonitor.BuildNotice(VRPauseReason.LeftControllerDisconnected, false) == VRHardwareStateMonitor.NOTICE_LEFT_CONTROLLER,
                "BuildNotice: control izquierdo = texto del control izquierdo");
            Check(VRHardwareStateMonitor.BuildNotice(VRPauseReason.RightControllerDisconnected, false) == VRHardwareStateMonitor.NOTICE_RIGHT_CONTROLLER,
                "BuildNotice: control derecho = texto del control derecho");
            Check(VRHardwareStateMonitor.BuildNotice(VRPauseReason.None, true) == VRHardwareStateMonitor.NOTICE_RESTORED,
                "BuildNotice: CanResume = 'Conexión restablecida...'");
            Check(VRHardwareStateMonitor.NOTICE_HEADSET == "Simulación pausada: se perdió la conexión con el visor." &&
                  VRHardwareStateMonitor.NOTICE_LEFT_CONTROLLER == "Simulación pausada: reconectá el control izquierdo para continuar." &&
                  VRHardwareStateMonitor.NOTICE_RESTORED == "Conexión restablecida. Pulsá Reanudar para continuar.",
                "Los textos del aviso son los acordados");
        }

        // ------------------------------------------------------------------ Feature de OpenXR

        private static void VerifyFeaturePath()
        {
            VRSessionStateFeature.ResetForTests();
            Check(!VRSessionStateFeature.TryDequeueTransition(out _), "VRSessionStateFeature: arranca sin transiciones");

            VRSessionStateFeature.SimulateStateChange(4, 5);
            VRSessionStateFeature.SimulateStateChange(5, 4);
            VRSessionStateFeature.SimulateLossPending();
            VRSessionStateFeature.SimulateLossPending();
            VRSessionStateFeature.SimulateExiting();
            VRSessionStateFeature.SimulateExiting();

            var seen = new List<string>();
            while (VRSessionStateFeature.TryDequeueTransition(out VRSessionStateFeature.Transition t)) seen.Add($"{t.OldState}>{t.NewState}");
            Check(string.Join(",", seen) == "4>5,5>4,4>7,7>8",
                $"VRSessionStateFeature: la cola conserva el orden y no duplica LOSS_PENDING ni EXITING ({string.Join(",", seen)})");

            VRSessionStateFeature.SimulateStateChange(1, 2);
            VRSessionStateFeature.ResetForTests();
            Check(!VRSessionStateFeature.TryDequeueTransition(out _), "VRSessionStateFeature: ResetForTests vacía la cola");
        }

        // ------------------------------------------------------------------ SimulationPause (tokens de pausa)

        private static void VerifySimulationPause()
        {
            SimulationPause.ResetForTests();
            float timeBefore = Time.timeScale;
            bool audioBefore = AudioListener.pause;
            Time.timeScale = 0.6f;
            AudioListener.pause = false;
            var a = new object();
            var b = new object();
            try
            {
                // Dos dueños solapados, en los dos órdenes de salida: se restauran los valores originales exactos.
                SimulationPause.Acquire(a, false);
                Check(Time.timeScale == 0f && !AudioListener.pause && SimulationPause.IsPaused && !SimulationPause.IsAudioPaused,
                    "SimulationPause: el primer Acquire congela el tiempo y deja el audio (sin pedirlo)");
                SimulationPause.Acquire(b, true);
                Check(AudioListener.pause && SimulationPause.IsAudioPaused && SimulationPause.OwnerCount == 2,
                    "SimulationPause: el audio queda pausado mientras algún token lo pida");
                SimulationPause.Release(a);
                Check(Time.timeScale == 0f && AudioListener.pause && SimulationPause.Holds(b) && !SimulationPause.Holds(a),
                    "SimulationPause: al soltar el primero sigue congelado por el segundo");
                SimulationPause.Release(b);
                Check(Time.timeScale == 0.6f && !AudioListener.pause && !SimulationPause.IsPaused,
                    "SimulationPause: dueños solapados (A, B, suelta A, suelta B) restauran los valores originales exactos");

                SimulationPause.Acquire(a, true);
                SimulationPause.Acquire(b, false);
                SimulationPause.Release(b);
                Check(Time.timeScale == 0f && AudioListener.pause, "SimulationPause: soltar el segundo no restaura mientras el primero tiene token");
                SimulationPause.Release(a);
                Check(Time.timeScale == 0.6f && !AudioListener.pause,
                    "SimulationPause: dueños solapados (A, B, suelta B, suelta A) restauran los valores originales exactos");

                // Audio que ya estaba pausado antes: se conserva al soltar.
                AudioListener.pause = true;
                SimulationPause.Acquire(a, false);
                SimulationPause.Release(a);
                Check(AudioListener.pause && Time.timeScale == 0.6f, "SimulationPause: el audio que ya estaba pausado vuelve pausado");
                AudioListener.pause = false;

                // Release doble, de un dueño ajeno y Acquire repetido no rompen nada.
                SimulationPause.Acquire(a, false);
                SimulationPause.Acquire(a, false);
                Check(SimulationPause.OwnerCount == 1, "SimulationPause: Acquire repetido del mismo dueño es idempotente");
                SimulationPause.Release(b);
                Check(SimulationPause.IsPaused && Time.timeScale == 0f, "SimulationPause: soltar un dueño sin token no hace nada");
                SimulationPause.Release(a);
                SimulationPause.Release(a);
                Check(!SimulationPause.IsPaused && Time.timeScale == 0.6f, "SimulationPause: Release doble no rompe nada ni vuelve a restaurar");
                Time.timeScale = 0.3f;
                SimulationPause.Release(a);
                Check(Time.timeScale == 0.3f, "SimulationPause: un Release sin token no pisa un timeScale ajeno");
                Time.timeScale = 0.6f;

                // Re-aplicación: algo pisa timeScale con tokens activos; Acquire lo vuelve a 0 sin perder el valor original.
                SimulationPause.Acquire(a, false);
                Time.timeScale = 1f;
                SimulationPause.Acquire(a, false);
                Check(Time.timeScale == 0f, "SimulationPause: Acquire reaplica el congelamiento si algo pisó timeScale");
                SimulationPause.Release(a);
                Check(Time.timeScale == 0.6f, "SimulationPause: tras la re-aplicación se restaura el valor original (no el que se pisó)");

                // Un dueño destruido que no soltó su token se descarta solo.
                var go = new GameObject("TMP_SimulationPauseOwner");
                SimulationPause.Acquire(go, true);
                SimulationPause.Acquire(a, false);
                Object.DestroyImmediate(go);
                Check(SimulationPause.OwnerCount == 1 && Time.timeScale == 0f, "SimulationPause: el dueño destruido se descarta pero el resto sigue congelando");
                SimulationPause.Release(a);
                Check(Time.timeScale == 0.6f && !AudioListener.pause, "SimulationPause: tras descartar al destruido y soltar al último se restauran los valores originales");
                var go2 = new GameObject("TMP_SimulationPauseOwner2");
                SimulationPause.Acquire(go2, false);
                Object.DestroyImmediate(go2);
                Check(!SimulationPause.IsPaused && Time.timeScale == 0.6f, "SimulationPause: si el único dueño se destruye, los valores se restauran solos");
            }
            finally
            {
                SimulationPause.ResetForTests();
                Time.timeScale = timeBefore;
                AudioListener.pause = audioBefore;
            }
        }

        // ------------------------------------------------------------------ Lección 1 (copia de escena)

        private static void VerifyLessonScene(Scene scene)
        {
            s_Scene = scene;
            InGameMenuController menu = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<InGameMenuController>(true)).FirstOrDefault();
            Check(menu != null && menu.MenuRoot != null, "Lesson_01 (copia): hay un InGameMenuController con su Canvas");
            if (menu == null || menu.MenuRoot == null) return;
            PrepareMenu(menu);

            Check(menu.ResumeButton != null, "Lesson_01 (copia): el panel principal tiene Button_Resume");

            VerifyStartupDoesNotPause(menu);
            VerifyNoSessionEditor(menu);
            VerifySessionLostFocus(menu);
            VerifyResumeRestoresState(menu);
            VerifyLossPending(menu);
            VerifyPresence(menu);
            VerifyControllers(menu);
            VerifyDoubleFailure(menu);
            VerifyControllerVisuals(scene, menu);
            VerifyNormalOpen(menu);
            VerifyPauseOverOpenedMenu(menu);
            VerifyViaFeatureQueue(menu);
            VerifyCalibrationStepNotCompletedByPause(menu);
            VerifyShutdownReleasesPause(menu);
            VerifyQuittingAndExiting(menu);
            VerifyRecenterOnRecovery(menu);
            VerifyPauseForcedAndReapplied(menu);
            VerifyNoMenuAvailable();
            VerifyInactiveMenuIsNotUsable(menu);
            VerifySceneChangeWithPendingPause(menu);
        }

        // Awake no corre en Edit Mode: se deja el menú cerrado y el mundo en el estado base de las pruebas.
        private static void PrepareMenu(InGameMenuController menu)
        {
            menu.MenuRoot.SetActive(false);
            Time.timeScale = BASELINE_TIME_SCALE;
            AudioListener.pause = false;
        }

        private static VRHardwareStateMonitor NewMonitor()
        {
            VRSessionStateFeature.ResetForTests();
            var go = new GameObject("TMP_VRHardwareStateMonitor");
            var monitor = go.AddComponent<VRHardwareStateMonitor>();
            monitor.Initialize(false);
            monitor.RestrictToScene(s_Scene);
            s_Logs.Clear();
            return monitor;
        }

        // Las entradas del monitor solo actualizan el estado; el menú y los visuales se refrescan en Tick (una vez por frame).
        private static void Tr(VRHardwareStateMonitor monitor, int oldState, int newState)
        {
            monitor.ProcessSessionTransition(oldState, newState);
            monitor.Tick();
        }

        private static void Pr(VRHardwareStateMonitor monitor, bool present)
        {
            monitor.SetUserPresent(present);
            monitor.Tick();
        }

        private static void Ctl(VRHardwareStateMonitor monitor, bool left, bool connected)
        {
            monitor.SetControllerConnected(left, connected);
            monitor.Tick();
        }

        // Apaga el monitor (como OnDestroy) sin ninguna ayuda manual: el menú y el tiempo tienen que quedar libres solos.
        // Solo si la prueba abrió el menú a propósito (modo del jugador) se lo cierra.
        private static void EndScenario(VRHardwareStateMonitor monitor, InGameMenuController menu)
        {
            monitor.Shutdown();
            Object.DestroyImmediate(monitor.gameObject);
            if (menu != null && menu.IsOpen && !menu.IsBlockingGameplayInput) menu.CloseMenu();
            Time.timeScale = BASELINE_TIME_SCALE;
            AudioListener.pause = false;
        }

        // Llega a FOCUSED por la secuencia normal de arranque (queda armado).
        private static void Boot(VRHardwareStateMonitor monitor)
        {
            Tr(monitor, 0, 1);
            Tr(monitor, 1, 2);
            Tr(monitor, 2, 3);
            Tr(monitor, 3, 4);
            Tr(monitor, 4, 5);
        }

        private static bool ResumeInteractable(InGameMenuController menu) => menu.ResumeButton != null && menu.ResumeButton.interactable;

        private static string NoticeText(InGameMenuController menu) => menu.NoticeLabel != null ? menu.NoticeLabel.text : null;

        // 1. Arranque sin haber estado FOCUSED
        private static void VerifyStartupDoesNotPause(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Tr(monitor, 0, 1);
                Tr(monitor, 1, 2);
                Tr(monitor, 2, 3);
                Tr(monitor, 3, 4);
                monitor.Tick();
                Check(!monitor.IsArmed && !VRHardwareStateMonitor.IsConnectionPaused && !menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE,
                    "Arranque IDLE>READY>SYNCHRONIZED>VISIBLE sin haber estado FOCUSED: no pausa, el menú sigue cerrado y el tiempo no cambia");
                Check(!monitor.ControllerVisualsHidden, "Arranque en VISIBLE sin armar: los visuales de los controles no se ocultan");

                Tr(monitor, 4, 5);
                Check(monitor.IsArmed && !VRHardwareStateMonitor.IsConnectionPaused, "Al llegar a FOCUSED se arma y no pausa");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 2. Editor sin casco: ni sesión ni mandos que alguna vez se hayan conectado
        private static void VerifyNoSessionEditor(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                monitor.Tick();
                Ctl(monitor, true, false);
                Ctl(monitor, false, false);
                Pr(monitor, false);
                monitor.Tick();
                Check(!VRHardwareStateMonitor.IsConnectionPaused && monitor.Reason == VRPauseReason.None && !menu.IsOpen,
                    "Sin sesión (Editor sin casco): controles que nunca se conectaron y presencia en false no pausan");
                Pr(monitor, true);
            }
            finally { EndScenario(monitor, menu); }
        }

        // 3. FOCUSED -> VISIBLE
        private static void VerifySessionLostFocus(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                int opened = 0;
                System.Action onOpened = () => opened++;
                menu.MenuOpened += onOpened;
                Boot(monitor);
                // Las entradas solo marcan el estado: el menú se abre en el Tick del frame.
                monitor.ProcessSessionTransition(5, 4);
                Check(VRHardwareStateMonitor.IsConnectionPaused && !menu.IsOpen, "FOCUSED>VISIBLE: la entrada solo actualiza el estado, el menú se abre en el Tick");
                monitor.Tick();
                menu.MenuOpened -= onOpened;

                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.Reason == VRPauseReason.SessionLostFocus && !monitor.CanResume,
                    "FOCUSED>VISIBLE: pausa con motivo SessionLostFocus y sin poder reanudar");
                Check(menu.IsOpen && menu.IsBlockingGameplayInput && opened == 1, "FOCUSED>VISIBLE: el menú se abre una vez");
                Check(Time.timeScale == 0f && AudioListener.pause, "FOCUSED>VISIBLE: Time.timeScale == 0 y AudioListener.pause");
                Check(menu.IsNoticeVisible && NoticeText(menu) == VRHardwareStateMonitor.NOTICE_HEADSET, $"FOCUSED>VISIBLE: aviso del visor visible ('{NoticeText(menu)}')");
                Check(!ResumeInteractable(menu) && menu.ResumeBlocked, "FOCUSED>VISIBLE: Reanudar no interactuable");
                menu.ToggleMenu();
                Check(menu.IsOpen && Time.timeScale == 0f, "FOCUSED>VISIBLE: ToggleMenu (botón Menu / Escape) no cierra el menú");
                menu.CloseMenu();
                Check(menu.IsOpen && VRHardwareStateMonitor.IsConnectionPaused, "FOCUSED>VISIBLE: CloseMenu (Reanudar) no cierra el menú");

                Check(s_Logs.Any(l => l.Contains("Session FOCUSED -> VISIBLE")) && s_Logs.Any(l => l.Contains("Pause (reason: SessionLostFocus)")),
                    "Log: 'Session FOCUSED -> VISIBLE' y 'Pause (reason: SessionLostFocus)'");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 4. VISIBLE -> FOCUSED: se puede reanudar, pero no solo
        private static void VerifyResumeRestoresState(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                bool audioBefore = AudioListener.pause;
                Boot(monitor);
                Tr(monitor, 5, 4);
                Tr(monitor, 4, 5);

                Check(monitor.CanResume && monitor.Reason == VRPauseReason.None && VRHardwareStateMonitor.IsConnectionPaused,
                    "VISIBLE>FOCUSED: CanResume, todavía pausado (no se reanuda solo)");
                Check(NoticeText(menu) == VRHardwareStateMonitor.NOTICE_RESTORED && menu.IsNoticeVisible,
                    $"VISIBLE>FOCUSED: texto 'Conexión restablecida...' ('{NoticeText(menu)}')");
                Check(ResumeInteractable(menu) && !menu.ResumeBlocked, "VISIBLE>FOCUSED: Reanudar interactuable");
                Check(menu.IsOpen && Time.timeScale == 0f && AudioListener.pause, "VISIBLE>FOCUSED: el menú sigue abierto y el tiempo sigue en 0");
                Check(s_Logs.Any(l => l.Contains("Resume available")), "Log: 'Resume available'");

                menu.CloseMenu();
                Check(!menu.IsOpen && !VRHardwareStateMonitor.IsConnectionPaused, "Reanudar (CloseMenu): cierra el menú y sale de la pausa");
                Check(Time.timeScale == BASELINE_TIME_SCALE && AudioListener.pause == audioBefore, "Reanudar: se restauran el timeScale y el audio previos");
                Check(!menu.IsNoticeVisible && !menu.IsBlockingGameplayInput, "Reanudar: el aviso se oculta");
                Check(s_Logs.Any(l => l.Contains("Resumed by user")), "Log: 'Resumed by user'");

                // Una falla posterior vuelve a pausar.
                Tr(monitor, 5, 4);
                Check(VRHardwareStateMonitor.IsConnectionPaused && menu.IsOpen, "Tras reanudar, una nueva falla vuelve a pausar");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 5. LOSS_PENDING
        private static void VerifyLossPending(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 7);
                Check(VRHardwareStateMonitor.IsConnectionPaused && (monitor.Reason & VRPauseReason.SessionLost) != 0 && !monitor.CanResume,
                    "LOSS_PENDING: pausa con motivo de visor desconectado (SessionLost)");
                Check(NoticeText(menu) == VRHardwareStateMonitor.NOTICE_HEADSET, "LOSS_PENDING: aviso del visor");
                Check(s_Logs.Any(l => l.Contains("Session LOSS_PENDING")), "Log: 'Session LOSS_PENDING'");
                Tr(monitor, 7, 8);
                Check(!monitor.CanResume && (monitor.Reason & VRPauseReason.SessionLost) != 0, "EXITING tras LOSS_PENDING: se loguea pero no cambia nada, sigue sin poder reanudar");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 6. Presencia
        private static void VerifyPresence(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Pr(monitor, false);
                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.Reason == VRPauseReason.UserNotPresent && menu.IsOpen,
                    "Presencia true>false estando FOCUSED: pausa (visor quitado)");
                Check(s_Logs.Any(l => l.Contains("User presence false")), "Log: 'User presence false'");
                Check(!ResumeInteractable(menu), "Presencia false: Reanudar no interactuable");
                Pr(monitor, true);
                Check(monitor.CanResume && ResumeInteractable(menu), "Presencia true de nuevo: CanResume y Reanudar interactuable");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 7. Mandos
        private static void VerifyControllers(InGameMenuController menu)
        {
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "izquierdo" : "derecho";
                VRHardwareStateMonitor monitor = NewMonitor();
                try
                {
                    Boot(monitor);
                    Ctl(monitor, left, true);
                    Check(!VRHardwareStateMonitor.IsConnectionPaused, $"Control {side} conectado: no pausa");
                    Ctl(monitor, left, false);
                    VRPauseReason expected = left ? VRPauseReason.LeftControllerDisconnected : VRPauseReason.RightControllerDisconnected;
                    string expectedText = left ? VRHardwareStateMonitor.NOTICE_LEFT_CONTROLLER : VRHardwareStateMonitor.NOTICE_RIGHT_CONTROLLER;
                    Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.Reason == expected && menu.IsOpen, $"Control {side} desconectado: pausa");
                    Check(NoticeText(menu) == expectedText, $"Control {side} desconectado: aviso del control ('{NoticeText(menu)}')");
                    Check(s_Logs.Any(l => l.Contains($"Controller {(left ? "Left" : "Right")} disconnected")), $"Log: 'Controller {(left ? "Left" : "Right")} disconnected'");
                    Ctl(monitor, left, true);
                    Check(monitor.CanResume && ResumeInteractable(menu), $"Control {side} reconectado: CanResume");
                    Check(s_Logs.Any(l => l.Contains($"Controller {(left ? "Left" : "Right")} connected")), $"Log: 'Controller {(left ? "Left" : "Right")} connected'");
                }
                finally { EndScenario(monitor, menu); }
            }

            // Un control que nunca se conectó no pausa, aunque el otro sí se desconecte (y solo cuenta el que ya estuvo).
            VRHardwareStateMonitor only = NewMonitor();
            try
            {
                Boot(only);
                Ctl(only, true, true);
                Ctl(only, false, false);
                Check(!VRHardwareStateMonitor.IsConnectionPaused, "Control derecho que nunca se conectó: no pausa");
                Ctl(only, true, false);
                Check(only.Reason == VRPauseReason.LeftControllerDisconnected, "Solo cuenta el control que ya había estado conectado");
            }
            finally { EndScenario(only, menu); }
        }

        // 8. Doble falla y recuperación parcial
        private static void VerifyDoubleFailure(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                int opened = 0;
                System.Action onOpened = () => opened++;
                menu.MenuOpened += onOpened;
                Boot(monitor);
                Ctl(monitor, true, true);
                Ctl(monitor, false, true);

                Tr(monitor, 5, 4);
                Ctl(monitor, true, false);
                menu.MenuOpened -= onOpened;
                Check(monitor.Reason == (VRPauseReason.SessionLostFocus | VRPauseReason.LeftControllerDisconnected) && !monitor.CanResume,
                    "Doble falla (visor + control izquierdo): ambos motivos");
                Check(NoticeText(menu) == VRHardwareStateMonitor.NOTICE_HEADSET, "Doble falla: se muestra el aviso del visor");

                Tr(monitor, 4, 5);
                Check(!monitor.CanResume && monitor.Reason == VRPauseReason.LeftControllerDisconnected && !ResumeInteractable(menu),
                    "Recuperación parcial (vuelve el visor, falta el control): no se puede reanudar");
                Check(NoticeText(menu) == VRHardwareStateMonitor.NOTICE_LEFT_CONTROLLER, $"Recuperación parcial: el aviso pasa al del control izquierdo ('{NoticeText(menu)}')");

                int notices = menu.MenuRoot.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Count(t => t.gameObject.name == "Notice");
                Check(notices == 1 && opened == 1, $"El aviso no se duplica ni se reabre el menú (avisos = {notices}, aperturas = {opened})");

                menu.CloseMenu();
                Check(menu.IsOpen, "Recuperación parcial: Reanudar sigue sin cerrar el menú");

                Ctl(monitor, true, true);
                Check(monitor.CanResume && ResumeInteractable(menu) && NoticeText(menu) == VRHardwareStateMonitor.NOTICE_RESTORED,
                    "Vuelve el segundo elemento: CanResume y texto de conexión restablecida");

                // Si vuelve a fallar estando en CanResume, se vuelve a bloquear sin reabrir.
                Ctl(monitor, false, false);
                Check(!monitor.CanResume && !ResumeInteractable(menu) && NoticeText(menu) == VRHardwareStateMonitor.NOTICE_RIGHT_CONTROLLER && menu.IsOpen,
                    "Nueva falla estando en CanResume: se bloquea Reanudar y el aviso se actualiza");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 9. Focus aware
        private static void VerifyControllerVisuals(Scene scene, InGameMenuController menu)
        {
            List<Renderer> renderers = new List<Renderer>();
            foreach (Unity.XR.CoreUtils.XROrigin origin in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Unity.XR.CoreUtils.XROrigin>(true)))
            {
                foreach (Transform t in origin.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Left Controller" && t.name != "Right Controller") continue;
                    renderers.AddRange(t.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer || r is LineRenderer));
                }
            }
            Check(renderers.Count >= 2, $"Lesson_01 (copia): el rig tiene renderers bajo Left/Right Controller ({renderers.Count})");
            if (renderers.Count < 2) return;
            Check(renderers.Any(r => r is LineRenderer) && renderers.Any(r => r is MeshRenderer || r is SkinnedMeshRenderer),
                "Los renderers de los controles incluyen LineRenderer y MeshRenderer/SkinnedMeshRenderer");

            // Estado previo mixto: uno apagado (enabled = false) y otro ya oculto.
            bool[] enabledBefore = renderers.Select(r => r.enabled).ToArray();
            renderers[0].enabled = false;
            enabledBefore[0] = false;
            bool alreadyForcedOff = renderers[1].forceRenderingOff;
            renderers[1].forceRenderingOff = true;

            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Tr(monitor, 0, 1);
                Tr(monitor, 3, 4);
                Check(!monitor.ControllerVisualsHidden && renderers.Skip(2).All(r => !r.forceRenderingOff), "Arranque en VISIBLE: los controles se ven");
                Tr(monitor, 4, 5);
                Check(!monitor.ControllerVisualsHidden, "FOCUSED: los controles se ven");

                Tr(monitor, 5, 4);
                Check(monitor.ControllerVisualsHidden && renderers.All(r => r.forceRenderingOff), "FOCUSED>VISIBLE: todos los visuales de los controles ocultos");
                Check(renderers.Select((r, i) => r.enabled == enabledBefore[i]).All(x => x), "Ocultar no modifica Renderer.enabled (XRI y UIOnlyFarCasting lo administran)");
                Check(menu.IsOpen, "El menú de la pausa está abierto mientras los controles están ocultos");

                Tr(monitor, 4, 5);
                Check(menu.IsOpen && !monitor.ControllerVisualsHidden, "VISIBLE>FOCUSED: el menú sigue abierto y los visuales se restauran");
                Check(!renderers[0].forceRenderingOff && renderers[1].forceRenderingOff && renderers.Skip(2).All(r => !r.forceRenderingOff),
                    "Restauración: cada renderer vuelve a su estado previo (el que ya estaba oculto sigue oculto)");
                Check(renderers.Select((r, i) => r.enabled == enabledBefore[i]).All(x => x) && !renderers[0].enabled,
                    "Restauración: respeta los renderers que ya estaban apagados");

                // Un control desconectado con la sesión FOCUSED no oculta nada.
                Ctl(monitor, true, true);
                Ctl(monitor, true, false);
                Check(!monitor.ControllerVisualsHidden, "Control desconectado con la sesión FOCUSED: no se tocan los visuales");
            }
            finally
            {
                EndScenario(monitor, menu);
                for (int i = 0; i < renderers.Count; i++) renderers[i].forceRenderingOff = false;
                renderers[1].forceRenderingOff = alreadyForcedOff;
            }
        }

        // 10. Apertura normal
        private static void VerifyNormalOpen(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                menu.OpenMenu();
                Check(menu.IsOpen && !menu.IsNoticeVisible && !menu.IsBlockingGameplayInput, "Apertura normal: el aviso está oculto");
                Check(ResumeInteractable(menu) && !menu.ResumeBlocked, "Apertura normal: Reanudar interactuable");
                Check(Time.timeScale == 0f && AudioListener.pause, "Apertura normal: pausa el tiempo y el audio como siempre");
                menu.ToggleMenu();
                Check(!menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause, "Apertura normal: ToggleMenu cierra y restaura el tiempo");
                Check(!VRHardwareStateMonitor.IsConnectionPaused, "Apertura normal: no activa la pausa de conexión");
            }
            finally { EndScenario(monitor, menu); }
        }

        // Pausa de conexión con el menú ya abierto por el jugador: al reanudar se conserva su menú (y su panel).
        private static void VerifyPauseOverOpenedMenu(InGameMenuController menu)
        {
            Transform calibrationPanel = menu.MenuRoot.transform.Find("CalibrationPanel");
            Transform mainPanel = menu.MenuRoot.transform.Find("MainPanel");

            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                int opened = 0;
                System.Action onOpened = () => opened++;
                Boot(monitor);
                menu.OpenMenu();
                menu.MenuOpened += onOpened;
                Tr(monitor, 5, 4);
                menu.MenuOpened -= onOpened;
                Check(menu.IsOpen && opened == 0 && menu.IsBlockingGameplayInput && menu.IsNoticeVisible,
                    "Menú ya abierto por el jugador: la pausa solo agrega el aviso (no reabre)");
                menu.ToggleMenu();
                Check(menu.IsOpen, "Menú ya abierto: queda bloqueado hasta que vuelva la conexión");
                Tr(monitor, 4, 5);
                menu.ToggleMenu();
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !menu.IsBlockingGameplayInput && !menu.IsNoticeVisible && !menu.ResumeBlocked,
                    "Con la conexión de vuelta, el botón Menu (ToggleMenu) termina la pausa");
                Check(menu.IsOpen && mainPanel.gameObject.activeSelf && Time.timeScale == 0f,
                    "Reanudar no cierra el menú del jugador: sigue abierto, en su panel y con el tiempo pausado como estaba");
                menu.ToggleMenu();
                Check(!menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE, "El jugador cierra su menú después: se restaura el tiempo original");
            }
            finally { EndScenario(monitor, menu); }

            // El jugador estaba en "Calibrar posición": al reanudar vuelve a ese panel.
            monitor = NewMonitor();
            try
            {
                Boot(monitor);
                menu.OpenMenu();
                menu.ShowCalibrationPanel();
                Tr(monitor, 5, 4);
                Check(menu.IsBlockingGameplayInput && mainPanel.gameObject.activeSelf && !calibrationPanel.gameObject.activeSelf && menu.IsNoticeVisible,
                    "Menú del jugador en Calibrar posición: la pausa muestra el panel principal con el aviso");
                Tr(monitor, 4, 5);
                menu.CloseMenu();
                Check(menu.IsOpen && calibrationPanel.gameObject.activeSelf && !mainPanel.gameObject.activeSelf && !menu.IsBlockingGameplayInput,
                    "Al reanudar vuelve el panel Calibrar posición del jugador (no se cierra su menú)");
                Check(Time.timeScale == 0f && !VRHardwareStateMonitor.IsConnectionPaused, "Calibrar posición del jugador: el tiempo sigue pausado como estaba");
                menu.ShowMainPanel();
                menu.CloseMenu();
                Check(!menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE, "El jugador cierra su menú: se restaura el tiempo");
            }
            finally { EndScenario(monitor, menu); }
        }

        // Camino completo de la feature: cola estática -> Tick del monitor
        private static void VerifyViaFeatureQueue(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                VRSessionStateFeature.SimulateStateChange(4, 5);
                VRSessionStateFeature.SimulateStateChange(5, 4);
                monitor.Tick();
                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.Reason == VRPauseReason.SessionLostFocus && menu.IsOpen,
                    "VRSessionStateFeature.SimulateStateChange + Tick: llega FOCUSED y VISIBLE en el mismo frame y igual pausa");
                VRSessionStateFeature.SimulateStateChange(4, 5);
                monitor.Tick();
                Check(monitor.CanResume, "Feature: volver a FOCUSED por la cola habilita CanResume");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 12. Lección 1: el paso OpenCalibrationMenu
        private static void VerifyCalibrationStepNotCompletedByPause(InGameMenuController menu)
        {
            var monitorGo = new GameObject("TMP_LessonActionMonitor");
            var lessonMonitor = monitorGo.AddComponent<LessonActionMonitor>();
            lessonMonitor.MenuController = menu;
            var step = ScriptableObject.CreateInstance<LessonStepSO>();
            step.requiredAction = LessonActionType.OpenCalibrationMenu;

            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Check(!lessonMonitor.IsConditionSatisfied(step), "OpenCalibrationMenu: menú cerrado, no se cumple");
                Tr(monitor, 5, 4);
                Check(menu.IsOpen && !lessonMonitor.IsConditionSatisfied(step), "OpenCalibrationMenu: abierto por la pausa de conexión no cuenta como abierto");

                // Escape durante la pausa tampoco cuenta como abrir el menú.
                LessonActionMonitor.EscapePressedOverrideForTests = () => true;
                Check(!lessonMonitor.IsConditionSatisfied(step), "OpenCalibrationMenu: Escape durante la pausa de conexión no marca el menú como abierto");
                LessonActionMonitor.EscapePressedOverrideForTests = null; // se soltó Escape

                Tr(monitor, 4, 5);
                menu.CloseMenu();
                Check(!menu.IsOpen && !lessonMonitor.IsConditionSatisfied(step), "OpenCalibrationMenu: abrir y cerrar por la pausa de conexión no cumple el paso (Escape incluido)");
                // Control: abrirlo a propósito sí lo cumple al cerrarlo.
                menu.OpenMenu();
                Check(!lessonMonitor.IsConditionSatisfied(step), "OpenCalibrationMenu: abierto a propósito, todavía no se cumple");
                menu.CloseMenu();
                Check(lessonMonitor.IsConditionSatisfied(step), "OpenCalibrationMenu: abierto a propósito y cerrado, se cumple (comportamiento previo)");

                // Control: Escape fuera de la pausa sigue contando.
                var control = new GameObject("TMP_LessonActionMonitor_Control").AddComponent<LessonActionMonitor>();
                control.MenuController = menu;
                LessonActionMonitor.EscapePressedOverrideForTests = () => true;
                Check(control.IsConditionSatisfied(step), "OpenCalibrationMenu: Escape fuera de la pausa de conexión sigue contando (comportamiento previo)");
                Object.DestroyImmediate(control.gameObject);
            }
            finally
            {
                LessonActionMonitor.EscapePressedOverrideForTests = null;
                EndScenario(monitor, menu);
                Object.DestroyImmediate(monitorGo);
                Object.DestroyImmediate(step);
            }
        }

        // 2. Apagar el monitor con una pausa en curso libera el menú y el tiempo (sin ayuda manual)
        private static void VerifyShutdownReleasesPause(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 4);
                Check(menu.IsOpen && menu.ResumeBlocked && Time.timeScale == 0f, "Shutdown: antes de apagar, el menú está abierto, bloqueado y el tiempo en 0");
                monitor.Shutdown();
                Check(!menu.ResumeBlocked && !menu.IsBlockingGameplayInput && !menu.IsNoticeVisible && ResumeInteractable(menu),
                    "Shutdown con pausa en curso: se libera el bloqueo y se limpia el aviso");
                Check(!menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause,
                    "Shutdown: el menú vuelve a su modo previo (cerrado) y el tiempo y el audio se restauran");
                menu.OpenMenu();
                menu.CloseMenu();
                Check(!menu.IsOpen, "Shutdown: después, el menú se abre y se cierra con normalidad");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 5. Cierre normal de la aplicación: ni STOPPING ni EXITING ni la desconexión de mandos pausan
        private static void VerifyQuittingAndExiting(InGameMenuController menu)
        {
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 8);
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !menu.IsOpen, "EXITING estando FOCUSED: no pausa (solo se loguea)");
                Check(s_Logs.Any(l => l.Contains("Session FOCUSED -> EXITING")), "Log: 'Session FOCUSED -> EXITING'");
            }
            finally { EndScenario(monitor, menu); }

            monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 6);
                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.Reason == VRPauseReason.SessionLostFocus,
                    "STOPPING fuera del cierre de la aplicación: sigue pausando (algunas plataformas lo emiten al quitarse el visor)");
            }
            finally { EndScenario(monitor, menu); }

            monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Ctl(monitor, true, true);
                monitor.SimulateQuitting();
                Tr(monitor, 5, 6);
                Tr(monitor, 6, 8);
                Ctl(monitor, true, false);
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE,
                    "Durante el apagado de la aplicación: STOPPING, EXITING y la desconexión de mandos no pausan ni abren el aviso");
                Check(!monitor.ControllerVisualsHidden, "Durante el apagado: tampoco se ocultan los visuales de los controles");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 1. Al recuperar el foco el menú vuelve a ubicarse frente a la cabeza
        private static void VerifyRecenterOnRecovery(InGameMenuController menu)
        {
            var head = new SerializedObject(menu).FindProperty("m_HeadTransform").objectReferenceValue as Transform;
            float distance = new SerializedObject(menu).FindProperty("m_DistanceFromHead").floatValue;
            Check(head != null, "Recentrado: el menú tiene referencia a la cabeza");
            if (head == null) return;
            Vector3 headPosition = head.position;

            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                head.position = headPosition + new Vector3(10f, 0f, 0f);
                Tr(monitor, 5, 4);
                Vector3 openedAt = menu.MenuRoot.transform.position;

                // Con el visor apoyado en la mesa, la cabeza queda lejos de donde se cortó la conexión.
                head.position = headPosition + new Vector3(-6f, 0.5f, 25f);
                Tr(monitor, 4, 5);
                Vector3 flat = head.forward; flat.y = 0f; flat.Normalize();
                Vector3 expected = head.position + flat * distance;
                Check(Vector3.Distance(openedAt, expected) > 5f, "Recentrado: la posición del corte quedó lejos de la cabeza actual (precondición)");
                Check(Vector3.Distance(menu.MenuRoot.transform.position, expected) < 0.01f,
                    "Al volver a FOCUSED el menú se ubica de nuevo frente a la cabeza");

                // También al llegar a CanResume por otro motivo (control reconectado).
                Ctl(monitor, true, true);
                Ctl(monitor, true, false);
                head.position = headPosition + new Vector3(3f, 0f, -8f);
                Ctl(monitor, true, true);
                flat = head.forward; flat.y = 0f; flat.Normalize();
                Check(monitor.CanResume && Vector3.Distance(menu.MenuRoot.transform.position, head.position + flat * distance) < 0.01f,
                    "Al llegar a CanResume el menú se vuelve a ubicar frente a la cabeza");
            }
            finally
            {
                head.position = headPosition;
                EndScenario(monitor, menu);
            }
        }

        // 7. La pausa por conexión congela siempre y se reaplica si algo la levanta
        private static void VerifyPauseForcedAndReapplied(InGameMenuController menu)
        {
            var so = new SerializedObject(menu);
            SerializedProperty pauseSim = so.FindProperty("m_PauseSimulation");
            bool original = pauseSim.boolValue;
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                pauseSim.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                Boot(monitor);
                Tr(monitor, 5, 4);
                Check(menu.IsBlockingGameplayInput && Time.timeScale == 0f && menu.IsSimulationPaused,
                    "m_PauseSimulation apagado en la escena: la pausa por conexión igual congela el tiempo");
                Tr(monitor, 4, 5);
                menu.CloseMenu();
                Check(!menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE, "m_PauseSimulation apagado: al reanudar se restaura el tiempo");
            }
            finally
            {
                pauseSim.boolValue = original;
                so.ApplyModifiedPropertiesWithoutUndo();
                EndScenario(monitor, menu);
            }

            monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 4);
                // El menú se deshabilita y vuelve a habilitar sin destruirse: OnDisable despausa.
                typeof(InGameMenuController).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
                Check(!menu.IsSimulationPaused && Time.timeScale == BASELINE_TIME_SCALE, "Precondición: OnDisable del menú levantó la pausa");
                monitor.Tick();
                Check(menu.IsSimulationPaused && Time.timeScale == 0f && AudioListener.pause, "Menú deshabilitado y rehabilitado: el monitor reaplica la pausa");

                // Algo cambia el tiempo mientras el aviso sigue visible.
                Time.timeScale = 1f;
                monitor.Tick();
                Check(Time.timeScale == 0f, "Time.timeScale alterado durante la pausa: el monitor lo vuelve a congelar");

                Tr(monitor, 4, 5);
                menu.CloseMenu();
                Check(!menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause,
                    "Tras reaplicar la pausa, al reanudar se restaura el tiempo previo (no el que se cambió a mano)");
            }
            finally { EndScenario(monitor, menu); }
        }

        // 3. Sin menú disponible el monitor congela el tiempo por su cuenta y reanuda solo
        private static void VerifyNoMenuAvailable()
        {
            Scene previous = s_Scene;
            s_Scene = s_Scratch; // la escena vacía de trabajo no tiene InGameMenuController
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Time.timeScale = BASELINE_TIME_SCALE;
                AudioListener.pause = false;
                Boot(monitor);
                Tr(monitor, 5, 4);
                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.OwnsTimeFreeze && Time.timeScale == 0f && AudioListener.pause && monitor.PauseMenu == null,
                    "Sin menú: el monitor congela el tiempo y el audio por su cuenta");
                Tr(monitor, 4, 5);
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !monitor.OwnsTimeFreeze, "Sin menú y con la conexión de vuelta: reanuda sola");
                Check(Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause, "Sin menú: al reanudar se restauran el timeScale y el audio previos");
                Check(s_Logs.Any(l => l.Contains("Resumed (no menu)")), "Log: 'Resumed (no menu)'");

                // Una segunda falla vuelve a pausar (sin quedar trabado).
                Tr(monitor, 5, 4);
                Check(VRHardwareStateMonitor.IsConnectionPaused && Time.timeScale == 0f, "Sin menú: una nueva falla vuelve a pausar");
                monitor.Shutdown();
                Check(Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause, "Sin menú: apagar el monitor con la pausa en curso restaura el tiempo");
            }
            finally
            {
                EndScenario(monitor, null);
                s_Scene = previous;
            }
        }

        // Un menú en un GameObject inactivo (o deshabilitado) no sirve para la pausa: se congela por cuenta del monitor
        private static void VerifyInactiveMenuIsNotUsable(InGameMenuController menu)
        {
            // a) El menú ya estaba inactivo cuando se pausa: no se engancha, el monitor congela solo y reanuda solo.
            menu.gameObject.SetActive(false);
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 4);
                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.PauseMenu == null && monitor.OwnsTimeFreeze && Time.timeScale == 0f && !menu.IsOpen,
                    "Menú en un objeto inactivo: no se engancha, no se abre y el monitor congela por su cuenta");
                Tr(monitor, 4, 5);
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !monitor.OwnsTimeFreeze && Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause,
                    "Menú inactivo: al volver todo reanuda solo y restaura el tiempo");
                Check(s_Logs.Any(l => l.Contains("Resumed (no menu)")), "Menú inactivo: log 'Resumed (no menu)'");
            }
            finally
            {
                menu.gameObject.SetActive(true);
                EndScenario(monitor, menu);
            }

            // b) El menú enganchado se desactiva en medio de la pausa: pasa a "sin menú" y reanuda solo.
            monitor = NewMonitor();
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 4);
                Check(monitor.PauseMenu == menu && menu.IsBlockingGameplayInput, "Menú activo: se engancha y abre el aviso");
                menu.gameObject.SetActive(false);
                monitor.Tick();
                Check(monitor.PauseMenu == null && monitor.OwnsTimeFreeze && Time.timeScale == 0f && VRHardwareStateMonitor.IsConnectionPaused,
                    "Menú que se desactiva durante la pausa: pasa a 'sin menú', el monitor sigue congelando");
                Check(!menu.ResumeBlocked && !menu.IsBlockingGameplayInput && !menu.IsNoticeVisible && !menu.IsOpen,
                    "Menú desactivado: se le quitan el bloqueo y el aviso y se restaura su modo previo (cerrado)");
                Tr(monitor, 4, 5);
                Check(!VRHardwareStateMonitor.IsConnectionPaused && Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause,
                    "Menú desactivado: con la conexión de vuelta reanuda solo y restaura el tiempo original");

                // Se reactiva: la siguiente falla lo vuelve a usar.
                menu.gameObject.SetActive(true);
                Tr(monitor, 5, 4);
                Check(monitor.PauseMenu == menu && menu.IsBlockingGameplayInput && !monitor.OwnsTimeFreeze && Time.timeScale == 0f,
                    "Menú reactivado: la siguiente pausa vuelve a usarlo y no queda congelamiento propio");
            }
            finally
            {
                menu.gameObject.SetActive(true);
                EndScenario(monitor, menu);
            }
        }

        // 13. Cambio de escena con pausa pendiente
        private static void VerifySceneChangeWithPendingPause(InGameMenuController menu)
        {
            // Se guarda lo necesario para armar un segundo menú idéntico (el "de la escena nueva") cuando el primero se descargue.
            var oldSo = new SerializedObject(menu);
            Object head = oldSo.FindProperty("m_HeadTransform").objectReferenceValue;
            Object follower = oldSo.FindProperty("m_DriverSeatFollower").objectReferenceValue;
            GameObject newCanvas = Object.Instantiate(menu.MenuRoot);
            newCanvas.name = menu.MenuRoot.name + "_New";
            newCanvas.SetActive(false);
            if (newCanvas.scene != s_Scene) SceneManager.MoveGameObjectToScene(newCanvas, s_Scene);
            GameObject newControllerGo = null;

            VRHardwareStateMonitor monitor = NewMonitor();
            InGameMenuController newMenu = null;
            try
            {
                Boot(monitor);
                Tr(monitor, 5, 4);
                Check(menu.IsOpen && monitor.PauseMenu == menu, "Cambio de escena: la pausa abrió el menú de la escena actual");

                // La escena se descarga: el menú viejo se destruye. En Edit Mode no corre OnDisable (que soltaría su token): el token
                // del menú destruido lo descarta SimulationPause solo, sin ayuda manual.
                GameObject oldCanvas = menu.MenuRoot;
                Object.DestroyImmediate(menu.gameObject);
                Object.DestroyImmediate(oldCanvas);

                monitor.Tick();
                Check(VRHardwareStateMonitor.IsConnectionPaused && monitor.PauseMenu == null && monitor.OwnsTimeFreeze && Time.timeScale == 0f,
                    "Cambio de escena: sin menú la pausa sigue vigente y el monitor congela el tiempo por su cuenta");

                // Se carga la escena nueva: aparece su menú.
                newControllerGo = new GameObject("TMP_InGameMenuController_New");
                SceneManager.MoveGameObjectToScene(newControllerGo, s_Scene);
                newMenu = newControllerGo.AddComponent<InGameMenuController>();
                var so = new SerializedObject(newMenu);
                so.FindProperty("m_MenuRoot").objectReferenceValue = newCanvas;
                so.FindProperty("m_MainPanel").objectReferenceValue = newCanvas.transform.Find("MainPanel").gameObject;
                so.FindProperty("m_CalibrationPanel").objectReferenceValue = newCanvas.transform.Find("CalibrationPanel").gameObject;
                Transform back = newCanvas.transform.Find("CalibrationPanel/Button_Back");
                so.FindProperty("m_CalibrationBackButton").objectReferenceValue = back != null ? back.gameObject : null;
                so.FindProperty("m_HeadTransform").objectReferenceValue = head;
                so.FindProperty("m_DriverSeatFollower").objectReferenceValue = follower;
                so.ApplyModifiedPropertiesWithoutUndo();

                monitor.Tick();
                Check(newMenu.IsOpen && newMenu.IsBlockingGameplayInput && monitor.PauseMenu == newMenu && !monitor.OwnsTimeFreeze,
                    "Cambio de escena: con el menú nuevo el monitor vuelve a abrir el aviso y le transfiere la pausa");
                Check(newMenu.IsNoticeVisible && NoticeText(newMenu) == VRHardwareStateMonitor.NOTICE_HEADSET && !ResumeInteractable(newMenu) && Time.timeScale == 0f,
                    "Cambio de escena: el aviso, el bloqueo de Reanudar y la pausa de tiempo se reaplican");

                Tr(monitor, 4, 5);
                newMenu.CloseMenu();
                Check(!newMenu.IsOpen && !VRHardwareStateMonitor.IsConnectionPaused && Time.timeScale == BASELINE_TIME_SCALE && !AudioListener.pause,
                    "Cambio de escena: se reanuda desde el menú nuevo y el tiempo vuelve al valor previo (no queda en 0)");
            }
            finally
            {
                monitor.Shutdown();
                Object.DestroyImmediate(monitor.gameObject);
                Time.timeScale = BASELINE_TIME_SCALE;
                AudioListener.pause = false;
                if (newControllerGo != null) Object.DestroyImmediate(newControllerGo);
                Object.DestroyImmediate(newCanvas);
            }
        }

        // ------------------------------------------------------------------ MainMenu (copia de escena)

        private static void VerifyMainMenuScene(Scene scene)
        {
            s_Scene = scene;
            var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainMenuController>(true)).FirstOrDefault();
            var menu = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InGameMenuController>(true)).FirstOrDefault();
            Check(controller != null && menu != null && menu.MenuRoot != null, "MainMenu (copia): hay MainMenuController e InGameMenuController");
            if (controller == null || menu == null || menu.MenuRoot == null) return;
            PrepareMenu(menu);
            Transform calibrationPanel = menu.MenuRoot.transform.Find("CalibrationPanel");
            Transform backButton = menu.MenuRoot.transform.Find("CalibrationPanel/Button_Back");

            bool lessonActivatedBefore = LessonSceneFlowManager.HasActivatedLesson;
            LessonSceneFlowManager.HasActivatedLesson = false;
            VRHardwareStateMonitor monitor = NewMonitor();
            try
            {
                controller.Restart();
                controller.HandleHazardChanged(true); // sale del Splash a Lecciones
                Boot(monitor);
                Check(controller.CurrentPage == MainMenuPage.Lessons && !VRHardwareStateMonitor.IsConnectionPaused, "MainMenu: arranca en Lecciones, sin pausa");
                int tabBefore = controller.ActiveGearTabIndex;

                Tr(monitor, 5, 4);
                Check(VRHardwareStateMonitor.IsConnectionPaused && menu.IsOpen && menu.IsBlockingGameplayInput, "MainMenu: la pausa abre el menú con aviso");
                Check(menu.IsNoticeVisible && NoticeText(menu) == VRHardwareStateMonitor.NOTICE_HEADSET && !ResumeInteractable(menu), "MainMenu: aviso del visor y Reanudar bloqueado");
                Check(Time.timeScale == 0f && AudioListener.pause, "MainMenu: la pausa detiene el tiempo y el audio");

                // MainMenuController no reacciona a nada mientras dura la pausa (un solo guard: InGameMenuController.IsBlockingGameplayInput).
                controller.HandleIgnitionPressed();
                Check(controller.SelectedLessonIndex == -1, "MainMenu: la ignición se ignora durante la pausa (no marca ninguna lección)");
                controller.HandleGearChanged(GearState.N);
                Check(!controller.HasPendingGear && controller.ActiveGearTabIndex == tabBefore && controller.CurrentGear != GearState.N,
                    "MainMenu: el cambio de marcha se ignora durante la pausa (no actualiza la tira ni encola la marcha pendiente)");
                controller.Tick(1f);
                Check(controller.CurrentPage == MainMenuPage.Lessons, "MainMenu: Tick no avanza la página durante la pausa");

                Tr(monitor, 4, 5);
                menu.CloseMenu();
                Check(!VRHardwareStateMonitor.IsConnectionPaused && !menu.IsOpen && Time.timeScale == BASELINE_TIME_SCALE, "MainMenu: Reanudar cierra el menú y restaura el tiempo");

                controller.Tick(0f); // primer Tick tras la pausa: la palanca vuelve a mandar (la marcha real sigue en P)
                // Tras reanudar, la entrada vuelve a funcionar: la marcha N lleva a Calibración (calibración standalone abierta).
                controller.HandleGearChanged(GearState.N);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen && !menu.IsBlockingGameplayInput && Time.timeScale == BASELINE_TIME_SCALE,
                    "MainMenu: tras reanudar, la marcha y el Tick vuelven a funcionar (página Calibración con la calibración standalone abierta)");
                Check(calibrationPanel.gameObject.activeSelf && !backButton.gameObject.activeSelf, "MainMenu: la calibración standalone muestra el panel de calibración sin botón Volver");

                // Pausa en la página Calibración: el monitor toma el menú; al reanudar se reabre la calibración standalone.
                Tr(monitor, 5, 4);
                Check(menu.IsOpen && menu.IsBlockingGameplayInput && menu.IsNoticeVisible && Time.timeScale == 0f,
                    "MainMenu (Calibración): la pausa convierte la calibración standalone en el menú con aviso y pausa el tiempo");
                Check(menu.ResumeButton != null && menu.ResumeButton.gameObject.activeInHierarchy && !calibrationPanel.gameObject.activeSelf && backButton.gameObject.activeSelf,
                    "MainMenu (Calibración): se ve el panel principal con Reanudar");
                Tr(monitor, 4, 5);
                menu.CloseMenu();
                Check(controller.CurrentPage == MainMenuPage.Calibration && menu.IsOpen && !menu.IsBlockingGameplayInput && Time.timeScale == BASELINE_TIME_SCALE,
                    "MainMenu (Calibración): al reanudar se vuelve a abrir la calibración standalone, sin pausar el tiempo");
                Check(calibrationPanel.gameObject.activeSelf && !backButton.gameObject.activeSelf && !menu.IsNoticeVisible && !menu.ResumeBlocked,
                    "MainMenu (Calibración): panel de calibración sin botón Volver, sin aviso ni bloqueo tras reanudar");

                // Sin que MainMenuController conozca al monitor: cambiar de página cierra la calibración y la entrada funciona.
                controller.HandleGearChanged(GearState.P);
                controller.Tick(0.3f);
                Check(controller.CurrentPage == MainMenuPage.Lessons && !menu.IsOpen, "MainMenu: tras reanudar, volver a P cierra la calibración (el menú principal sigue funcionando)");
                controller.HandleIgnitionPressed();
                Check(controller.SelectedLessonIndex == controller.CursorIndex, "MainMenu: tras reanudar la ignición vuelve a seleccionar");
            }
            finally
            {
                EndScenario(monitor, menu);
                controller.Restart();
                if (menu.IsOpen) menu.CloseMenu();
                LessonSceneFlowManager.HasActivatedLesson = lessonActivatedBefore;
                var highlighter = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InteractableHighlighter>(true)).FirstOrDefault();
                if (highlighter != null) highlighter.ClearHighlight();
            }
        }

        // ------------------------------------------------------------------ helpers

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
