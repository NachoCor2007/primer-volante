using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using PrimerVolante.Lessons;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace PrimerVolante.VR
{
    /// <summary>Motivos por los que la simulación está (o estaría) pausada por la conexión VR. Se combinan como flags.</summary>
    [Flags]
    public enum VRPauseReason
    {
        None = 0,
        /// <summary>La sesión salió de FOCUSED (se quitó el visor, menú del sistema, Link cortado...).</summary>
        SessionLostFocus = 1,
        /// <summary>La sesión se perdió (LOSS_PENDING): el visor se desconectó.</summary>
        SessionLost = 2,
        /// <summary>XR_EXT_user_presence informa que el usuario ya no tiene el visor puesto.</summary>
        UserNotPresent = 4,
        LeftControllerDisconnected = 8,
        RightControllerDisconnected = 16
    }

    /// <summary>
    /// Pausa de emergencia por desconexión VR (issue #5, US40-US41; requisitos Meta VRC.Quest.Functional.2, VRC.Quest.Input.4 y
    /// VRC.PC.Input.1). Singleton persistente auto-creado (no se agrega a ninguna escena) que vigila:
    ///  - la sesión OpenXR (<see cref="VRSessionStateFeature"/>): se arma recién cuando la sesión llegó a FOCUSED una vez; ya
    ///    armado, salir de FOCUSED o perder la sesión pausa. Antes de eso (arranque en VISIBLE, Editor sin casco) nunca pausa.
    ///    EXITING nunca pausa (solo se loguea) y nada pausa durante el apagado de la aplicación;
    ///  - la presencia del usuario (<c>OpenXRUtility.IsUserPresent</c>), para runtimes que se quedan en FOCUSED sin casco;
    ///  - los mandos (API legada <c>InputDevices</c>, la única que en este proyecto refleja los controles OpenXR): solo cuenta
    ///    como desconexión un mando que ya había estado conectado. No se usa <c>isTracked</c>: las pérdidas breves de tracking
    ///    (mano fuera de las cámaras) no pausan.
    /// Al pausar abre el menú in-game de la escena overlay con un aviso (<see cref="InGameMenuController.OpenMenuWithNotice"/>) y
    /// bloquea Reanudar hasta que todo vuelve: no hay reanudación automática. Sin menú disponible congela el tiempo por su
    /// cuenta (y reanuda solo al recuperarse la conexión, porque no hay UI que pulsar). Mientras la sesión no esté en FOCUSED
    /// oculta los visuales de los controles del rig (focus aware). La lógica de decisión es independiente del hardware
    /// (<see cref="ProcessSessionTransition"/>, <see cref="SetUserPresent"/>, <see cref="SetControllerConnected"/>) para poder
    /// verificarla sin casco. Cada transición se loguea con el prefijo <c>[VRHardware]</c>.
    /// Las entradas solo actualizan el estado; el menú y los visuales se refrescan una vez por frame en <see cref="Tick"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class VRHardwareStateMonitor : MonoBehaviour
    {
        public const string LOG_PREFIX = "[VRHardware]";

        public const string NOTICE_HEADSET = "Simulación pausada: se perdió la conexión con el visor.";
        public const string NOTICE_LEFT_CONTROLLER = "Simulación pausada: reconectá el control izquierdo para continuar.";
        public const string NOTICE_RIGHT_CONTROLLER = "Simulación pausada: reconectá el control derecho para continuar.";
        public const string NOTICE_BOTH_CONTROLLERS = "Simulación pausada: reconectá los controles para continuar.";
        public const string NOTICE_RESTORED = "Conexión restablecida. Pulsá Reanudar para continuar.";

        private const float MENU_SEARCH_INTERVAL = 0.25f;

        private static VRHardwareStateMonitor s_Instance;

        // Sesión
        private int m_SessionState = VRSessionStateFeature.STATE_UNKNOWN;
        private bool m_Armed;
        private bool m_SessionLossPending;

        // Presencia
        private bool m_UserPresent = true;
        private bool m_PollPresence;
        private bool m_PresenceUnavailable;
        private bool m_PresenceSeenTrue;
        private bool m_PresenceIgnoreLogged;

        // Mandos
        private bool m_LeftEverConnected;
        private bool m_LeftConnected;
        private bool m_RightEverConnected;
        private bool m_RightConnected;
        private readonly List<XRInputDevice> m_LeftDevices = new List<XRInputDevice>();
        private readonly List<XRInputDevice> m_RightDevices = new List<XRInputDevice>();
        private bool m_SubscribedToDevices;

        // Apagado de la aplicación: desde ahí nada pausa (STOPPING/EXITING y la desconexión de mandos son del cierre normal).
        private bool m_Quitting;
        private bool m_SubscribedToLifecycle;

        // Estado de pausa
        private VRPauseReason m_Reasons;
        private bool m_IsPaused;
        private bool m_CanResume;
        private bool m_RecenterPending;

        // Menú
        private InGameMenuController m_PauseMenu;
        private string m_AppliedNotice;
        private float m_NextMenuSearchTime;
        private LessonSceneFlowManager m_FlowManager;

        // Congelamiento propio (token de SimulationPause), mientras no hay un menú usable que lo haga.
        private bool m_OwnsFreeze;

        // Focus aware: visuales de los controles (renderer -> forceRenderingOff previo), cacheados una vez por rig.
        private readonly Dictionary<Renderer, bool> m_HiddenRenderers = new Dictionary<Renderer, bool>();
        private readonly HashSet<Unity.XR.CoreUtils.XROrigin> m_ScannedOrigins = new HashSet<Unity.XR.CoreUtils.XROrigin>();
        private readonly List<Transform> m_TransformBuffer = new List<Transform>();
        private readonly List<Renderer> m_RendererBuffer = new List<Renderer>();
        private readonly List<InGameMenuController> m_MenuBuffer = new List<InGameMenuController>();
        private bool m_VisualsHidden;
        private bool m_RigsDirty;

        private bool m_Initialized;

        // Solo tests: limita la búsqueda del menú y de los rigs a una escena (en producción se busca en todas las cargadas).
        private Scene m_RestrictedScene;
        private bool m_HasRestrictedScene;

        /// <summary>Instancia vigente (null si todavía no se creó, p. ej. en Edit Mode fuera de los tests).</summary>
        public static VRHardwareStateMonitor Instance => s_Instance;

        /// <summary>
        /// true mientras la simulación está pausada por una desconexión VR (hasta que el jugador reanuda). Estado de solo lectura
        /// del monitor, para diagnóstico y tests: el menú principal y los pasos de lección no lo consultan, usan
        /// <see cref="InGameMenuController.IsBlockingGameplayInput"/>.
        /// </summary>
        public static bool IsConnectionPaused => s_Instance != null && s_Instance.m_IsPaused;

        /// <summary>true si en pausa ya volvió todo lo que faltaba (sesión FOCUSED, presencia y mandos): el jugador puede reanudar.</summary>
        public bool CanResume => m_IsPaused && m_CanResume;

        /// <summary>Motivos vigentes (None cuando todo volvió, aunque el jugador todavía no reanude).</summary>
        public VRPauseReason Reason => m_Reasons;

        /// <summary>true cuando la sesión llegó a FOCUSED al menos una vez: recién ahí pueden pausar la sesión y la presencia.</summary>
        public bool IsArmed => m_Armed;

        /// <summary>true mientras los visuales de los controles están ocultos por no tener foco.</summary>
        public bool ControllerVisualsHidden => m_VisualsHidden;

        /// <summary>Cantidad de renderers de controles ocultos ahora (para tests).</summary>
        public int HiddenRendererCount => m_HiddenRenderers.Count;

        /// <summary>Menú que tiene el aviso de la pausa vigente (null si todavía no se encontró).</summary>
        public InGameMenuController PauseMenu => m_PauseMenu;

        /// <summary>true mientras el monitor congela el tiempo por su cuenta porque no hay menú que lo haga.</summary>
        public bool OwnsTimeFreeze => m_OwnsFreeze;

        /// <summary>Texto del aviso que corresponde al estado actual.</summary>
        public string NoticeText => BuildNotice(m_Reasons, m_CanResume);

        // Con "Enter Play Mode Options" sin recarga de dominio los estáticos sobreviven entre Play: se reinician aquí.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (s_Instance != null) return;
            var go = new GameObject("VRHardwareStateMonitor");
            DontDestroyOnLoad(go);
            go.AddComponent<VRHardwareStateMonitor>();
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Initialize(true);
        }

        /// <summary>
        /// Deja el monitor listo. <paramref name="useHardware"/> suscribe los eventos de dispositivos XR, de escenas y de apagado,
        /// y sondea la presencia del usuario; los tests lo llaman con false e inyectan los estados. Idempotente.
        /// </summary>
        public void Initialize(bool useHardware)
        {
            if (m_Initialized) return;
            m_Initialized = true;
            s_Instance = this;
            m_PollPresence = useHardware;
            if (useHardware)
            {
                SubscribeToDevices();
                SubscribeToLifecycle();
            }
        }

        private void OnDestroy() => Shutdown();

        /// <summary>
        /// Suelta eventos, menú, congelamiento y visuales, y deja de ser la instancia vigente. Si hay una pausa en curso la termina
        /// (el menú vuelve a su modo previo, sin bloqueo ni aviso, y el tiempo se restaura). Lo llama OnDestroy; los tests lo
        /// llaman a mano porque en Edit Mode Unity no ejecuta OnDestroy.
        /// </summary>
        public void Shutdown()
        {
            UnsubscribeFromDevices();
            UnsubscribeFromLifecycle();

            InGameMenuController menu = m_PauseMenu;
            UnsubscribeFromMenu();
            if (menu != null) menu.EndConnectionPause();

            ReleaseOwnFreeze();
            RestoreControllerVisuals();
            m_IsPaused = false;
            if (s_Instance == this) s_Instance = null;
        }

#if UNITY_EDITOR
        /// <summary>Solo Editor (tests): busca el menú y los rigs solo en <paramref name="scene"/> (la copia temporal que se verifica).</summary>
        public void RestrictToScene(Scene scene)
        {
            m_RestrictedScene = scene;
            m_HasRestrictedScene = true;
        }

        /// <summary>Solo Editor (tests): el mismo efecto que el aviso de apagado de la aplicación.</summary>
        public void SimulateQuitting() => m_Quitting = true;
#endif

        private void Update() => Tick();

        /// <summary>
        /// Un paso del monitor en el hilo principal: procesa las transiciones de sesión que encoló la feature, sondea la
        /// presencia y refresca el menú y los visuales (una vez por frame). La llama Update; los tests la llaman a mano.
        /// </summary>
        public void Tick()
        {
            DrainSessionTransitions();
            PollUserPresence();
            RefreshMenu();
            RefreshControllerVisuals();
        }

        // ------------------------------------------------------------------ Entradas (puras, inyectables)

        /// <summary>
        /// Procesa un cambio de estado de sesión (XrSessionState). Se arma al llegar a FOCUSED; ya armado, cualquier otro
        /// estado pausa. LOSS_PENDING se informa como visor desconectado. EXITING solo se loguea.
        /// </summary>
        public void ProcessSessionTransition(int oldState, int newState)
        {
            string note = newState == VRSessionStateFeature.STATE_EXITING ? " (never pauses)" : m_Quitting ? " (ignored: shutting down)" : string.Empty;
            if (newState == VRSessionStateFeature.STATE_LOSS_PENDING)
            {
                Log($"Session LOSS_PENDING (was {VRSessionStateFeature.StateName(oldState)}){note}");
            }
            else
            {
                Log($"Session {VRSessionStateFeature.StateName(oldState)} -> {VRSessionStateFeature.StateName(newState)}{note}");
            }

            if (newState == VRSessionStateFeature.STATE_EXITING) return;

            m_SessionState = newState;
            if (newState == VRSessionStateFeature.STATE_FOCUSED)
            {
                m_Armed = true;
                m_SessionLossPending = false;
                // La cabeza pudo quedar en cualquier lado mientras no había foco: Reanudar vuelve a ubicarse frente a ella.
                if (m_IsPaused) m_RecenterPending = true;
            }
            else if (newState == VRSessionStateFeature.STATE_LOSS_PENDING)
            {
                m_SessionLossPending = true;
            }

            Evaluate();
        }

        /// <summary>Informa la presencia del usuario (<c>OpenXRUtility.IsUserPresent</c>). Solo pausa estando armado.</summary>
        public void SetUserPresent(bool present)
        {
            if (m_UserPresent == present) return;
            m_UserPresent = present;
            Log("User presence " + (present ? "true" : "false"));
            Evaluate();
        }

        /// <summary>
        /// Informa la conexión de un mando. Solo cuenta como desconexión un mando que ya había estado conectado (al arrancar los
        /// controles aparecen tarde); también pasar a hand tracking.
        /// </summary>
        public void SetControllerConnected(bool left, bool connected)
        {
            bool previous = left ? m_LeftConnected : m_RightConnected;
            if (left)
            {
                m_LeftConnected = connected;
                if (connected) m_LeftEverConnected = true;
            }
            else
            {
                m_RightConnected = connected;
                if (connected) m_RightEverConnected = true;
            }

            if (previous != connected) Log($"Controller {(left ? "Left" : "Right")} {(connected ? "connected" : "disconnected")}");
            Evaluate();
        }

        // ------------------------------------------------------------------ Decisión

        /// <summary>Motivos de pausa para un estado dado (función pura).</summary>
        public static VRPauseReason ComputeReasons(bool armed, int sessionState, bool sessionLossPending, bool userPresent,
            bool leftEverConnected, bool leftConnected, bool rightEverConnected, bool rightConnected)
        {
            VRPauseReason reasons = VRPauseReason.None;
            if (armed && sessionState != VRSessionStateFeature.STATE_FOCUSED)
            {
                reasons |= sessionLossPending || sessionState == VRSessionStateFeature.STATE_LOSS_PENDING
                    ? VRPauseReason.SessionLost
                    : VRPauseReason.SessionLostFocus;
            }
            if (armed && !userPresent) reasons |= VRPauseReason.UserNotPresent;
            if (leftEverConnected && !leftConnected) reasons |= VRPauseReason.LeftControllerDisconnected;
            if (rightEverConnected && !rightConnected) reasons |= VRPauseReason.RightControllerDisconnected;
            return reasons;
        }

        /// <summary>Texto del aviso del menú para unos motivos dados (función pura).</summary>
        public static string BuildNotice(VRPauseReason reasons, bool canResume)
        {
            if (canResume || reasons == VRPauseReason.None) return NOTICE_RESTORED;

            const VRPauseReason headsetMask = VRPauseReason.SessionLostFocus | VRPauseReason.SessionLost | VRPauseReason.UserNotPresent;
            if ((reasons & headsetMask) != 0) return NOTICE_HEADSET;

            bool left = (reasons & VRPauseReason.LeftControllerDisconnected) != 0;
            bool right = (reasons & VRPauseReason.RightControllerDisconnected) != 0;
            if (left && right) return NOTICE_BOTH_CONTROLLERS;
            return left ? NOTICE_LEFT_CONTROLLER : NOTICE_RIGHT_CONTROLLER;
        }

        // Solo recalcula el estado de pausa y avisa; el menú y los visuales se refrescan en Tick.
        private void Evaluate()
        {
            if (m_Quitting) return;

            VRPauseReason reasons = ComputeReasons(m_Armed, m_SessionState, m_SessionLossPending, m_UserPresent,
                m_LeftEverConnected, m_LeftConnected, m_RightEverConnected, m_RightConnected);
            bool reasonsChanged = reasons != m_Reasons;
            m_Reasons = reasons;

            if (!m_IsPaused)
            {
                if (reasons != VRPauseReason.None)
                {
                    m_IsPaused = true;
                    m_CanResume = false;
                    m_RecenterPending = false;
                    Log($"Pause (reason: {reasons})");
                }
                return;
            }

            bool canResume = reasons == VRPauseReason.None;
            if (canResume != m_CanResume)
            {
                m_CanResume = canResume;
                if (canResume) m_RecenterPending = true;
                Log(canResume ? "Resume available" : $"Pause (reason: {reasons})");
            }
            else if (reasonsChanged && !canResume)
            {
                Log($"Pause (reason: {reasons})");
            }
        }

        // ------------------------------------------------------------------ Hardware real

        private void DrainSessionTransitions()
        {
            while (VRSessionStateFeature.TryDequeueTransition(out VRSessionStateFeature.Transition t))
            {
                ProcessSessionTransition(t.OldState, t.NewState);
            }
        }

        private void ReadUserPresence()
        {
            try
            {
                bool present = UnityEngine.XR.OpenXR.OpenXRUtility.IsUserPresent;
                // El runtime tiene que informar presencia (true) al menos una vez antes de confiar en un false: si el valor
                // nativo arrancara en false y nunca se actualizara, el simulador quedaría pausado sin motivo.
                if (!m_PresenceSeenTrue)
                {
                    if (!present)
                    {
                        if (!m_PresenceIgnoreLogged)
                        {
                            m_PresenceIgnoreLogged = true;
                            Log("User presence reported false before ever being true: ignored until the runtime reports true");
                        }
                        return;
                    }
                    m_PresenceSeenTrue = true;
                }
                SetUserPresent(present);
            }
            catch (Exception e)
            {
                // Sin OpenXR cargado la consulta nativa puede fallar: se deja de sondear (nunca pausa por presencia).
                m_PresenceUnavailable = true;
                Debug.LogWarning($"{LOG_PREFIX} No se pudo leer la presencia del usuario: {e.Message}");
            }
        }

        private void PollUserPresence()
        {
            if (!m_PollPresence || m_PresenceUnavailable || !m_Armed) return;
            ReadUserPresence();
        }

        private void SubscribeToDevices()
        {
            if (m_SubscribedToDevices) return;
            m_SubscribedToDevices = true;
            InputDevices.deviceConnected += HandleDeviceConnected;
            InputDevices.deviceDisconnected += HandleDeviceDisconnected;

            // Los mandos que ya estaban conectados antes de que existiera el monitor.
            var existing = new List<XRInputDevice>();
            InputDevices.GetDevices(existing);
            foreach (XRInputDevice device in existing) HandleDeviceConnected(device);
        }

        private void UnsubscribeFromDevices()
        {
            if (!m_SubscribedToDevices) return;
            m_SubscribedToDevices = false;
            InputDevices.deviceConnected -= HandleDeviceConnected;
            InputDevices.deviceDisconnected -= HandleDeviceDisconnected;
        }

        private void SubscribeToLifecycle()
        {
            if (m_SubscribedToLifecycle) return;
            m_SubscribedToLifecycle = true;
            Application.quitting += HandleQuitting;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif
        }

        private void UnsubscribeFromLifecycle()
        {
            if (!m_SubscribedToLifecycle) return;
            m_SubscribedToLifecycle = false;
            Application.quitting -= HandleQuitting;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif
        }

        private void HandleQuitting() => m_Quitting = true;

        // El rig de los controles puede ser otro: se vuelve a escanear la próxima vez que haya que ocultarlos.
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => m_RigsDirty = true;

        private void HandleSceneUnloaded(Scene scene) => m_RigsDirty = true;

#if UNITY_EDITOR
        private void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) m_Quitting = true;
        }
#endif

        // Un control es un dispositivo Controller (sin HandTracking) de la mano izquierda o derecha. Puede haber más de un
        // dispositivo por mano; la mano cuenta como conectada mientras quede alguno.
        private static bool TryGetHand(XRInputDevice device, out bool left)
        {
            InputDeviceCharacteristics c = device.characteristics;
            left = (c & InputDeviceCharacteristics.Left) != 0;
            bool right = (c & InputDeviceCharacteristics.Right) != 0;
            bool isController = (c & InputDeviceCharacteristics.Controller) != 0 && (c & InputDeviceCharacteristics.HandTracking) == 0;
            return isController && (left ^ right);
        }

        private void HandleDeviceConnected(XRInputDevice device)
        {
            if (!TryGetHand(device, out bool left)) return;
            List<XRInputDevice> list = left ? m_LeftDevices : m_RightDevices;
            if (!list.Contains(device)) list.Add(device);
            SetControllerConnected(left, true);
        }

        private void HandleDeviceDisconnected(XRInputDevice device)
        {
            // Al desconectarse puede que ya no informe sus características: se la busca en las listas.
            bool left;
            if (m_LeftDevices.Remove(device)) left = true;
            else if (m_RightDevices.Remove(device)) left = false;
            else return;

            List<XRInputDevice> list = left ? m_LeftDevices : m_RightDevices;
            SetControllerConnected(left, list.Count > 0);
        }

        // ------------------------------------------------------------------ Menú

        private bool IsFlowTransitioning()
        {
            if (!Application.isPlaying) return false;
            if (m_FlowManager == null) m_FlowManager = UnityEngine.Object.FindAnyObjectByType<LessonSceneFlowManager>();
            return m_FlowManager != null && m_FlowManager.IsTransitioning;
        }

        // Un menú sirve para la pausa solo si su componente está habilitado y su GameObject activo en la jerarquía: uno inactivo
        // sería invisible e impulsable, y como "hay menú" no reanudaría solo (congelamiento permanente).
        private static bool IsUsable(InGameMenuController menu) => menu != null && menu.enabled && menu.gameObject.activeInHierarchy;

        private InGameMenuController FindMenu()
        {
            if (!m_HasRestrictedScene)
            {
                foreach (InGameMenuController menu in UnityEngine.Object.FindObjectsByType<InGameMenuController>(FindObjectsInactive.Exclude))
                {
                    if (IsUsable(menu)) return menu;
                }
                return null;
            }

            foreach (GameObject root in m_RestrictedScene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(false, m_MenuBuffer);
                foreach (InGameMenuController menu in m_MenuBuffer)
                {
                    if (IsUsable(menu)) return menu;
                }
            }
            return null;
        }

        /// <summary>
        /// Mantiene la pausa: busca un menú usable en la escena overlay activa y le abre el aviso, o congela el tiempo por su
        /// cuenta mientras no haya (y reanuda sola al recuperarse la conexión). Si el menú se destruyó o dejó de estar activo
        /// (cambio de escena) lo trata como "sin menú" hasta encontrar el de la escena nueva; mientras hay una transición de
        /// escena en curso espera a que termine.
        /// </summary>
        private void RefreshMenu()
        {
            if (!m_IsPaused || m_Quitting) return;

            if (m_PauseMenu != null && !IsUsable(m_PauseMenu)) DetachUnusableMenu();

            if (m_PauseMenu == null)
            {
                // Sin menú y con la conexión de vuelta no hay UI que pulsar: se reanuda sola.
                if (m_OwnsFreeze && m_CanResume)
                {
                    ResumeWithoutMenu();
                    return;
                }
                if (m_OwnsFreeze && Time.timeScale != 0f) FreezeOnOwn();

                if (Application.isPlaying)
                {
                    if (Time.unscaledTime < m_NextMenuSearchTime) return;
                    m_NextMenuSearchTime = Time.unscaledTime + MENU_SEARCH_INTERVAL;
                }
                if (IsFlowTransitioning()) return;

                InGameMenuController found = FindMenu();
                if (found == null)
                {
                    if (m_CanResume) ResumeWithoutMenu();
                    else FreezeOnOwn();
                    return;
                }

                m_PauseMenu = found;
                m_PauseMenu.ConnectionPauseEnded += HandlePauseEnded;
                m_AppliedNotice = null;
                m_RecenterPending = false;
            }

            string notice = NoticeText;
            bool frozen = m_PauseMenu.IsSimulationPaused && Time.timeScale == 0f;
            // Idempotente: abre la primera vez, actualiza el aviso y reaplica la pausa si algo la levantó (menú deshabilitado).
            if (!m_PauseMenu.IsBlockingGameplayInput || notice != m_AppliedNotice || !frozen)
            {
                m_PauseMenu.OpenMenuWithNotice(notice);
                m_AppliedNotice = notice;
            }
            if (m_RecenterPending)
            {
                m_RecenterPending = false;
                m_PauseMenu.RecenterConnectionPauseMenu();
            }
            if (m_PauseMenu.ResumeBlocked != !m_CanResume) m_PauseMenu.SetResumeBlocked(!m_CanResume);

            // El menú ya tiene su token de SimulationPause: el congelamiento propio, si lo había, sobra.
            ReleaseOwnFreeze();
        }

        // El menú enganchado se desactivó o deshabilitó sin destruirse: se lo suelta (restaurando su UI) y la pausa sigue por
        // cuenta del monitor hasta que haya un menú usable.
        private void DetachUnusableMenu()
        {
            InGameMenuController menu = m_PauseMenu;
            UnsubscribeFromMenu();
            FreezeOnOwn();
            menu.EndConnectionPause();
            m_NextMenuSearchTime = 0f;
        }

        private void UnsubscribeFromMenu()
        {
            if (m_PauseMenu != null) m_PauseMenu.ConnectionPauseEnded -= HandlePauseEnded;
            m_PauseMenu = null;
            m_AppliedNotice = null;
        }

        // El jugador reanudó (Reanudar o botón Menu): el menú ya restauró su modo previo.
        private void HandlePauseEnded()
        {
            if (!m_IsPaused || !m_CanResume) return;

            UnsubscribeFromMenu();
            EndPause("Resumed by user");
        }

        private void ResumeWithoutMenu()
        {
            ReleaseOwnFreeze();
            EndPause("Resumed (no menu)");
        }

        private void EndPause(string logMessage)
        {
            m_IsPaused = false;
            m_CanResume = false;
            Log(logMessage);
        }

        // Congelamiento propio (sin menú usable): token de SimulationPause, que guarda y restaura el tiempo y el audio previos.
        private void FreezeOnOwn()
        {
            if (!m_OwnsFreeze)
            {
                m_OwnsFreeze = true;
                Log("No usable menu: freezing time on its own");
            }
            SimulationPause.Acquire(this, true);
        }

        private void ReleaseOwnFreeze()
        {
            if (!m_OwnsFreeze) return;
            m_OwnsFreeze = false;
            SimulationPause.Release(this);
        }

        // ------------------------------------------------------------------ Focus aware

        /// <summary>
        /// Mientras la sesión esté armada y fuera de FOCUSED oculta los renderers (MeshRenderer, SkinnedMeshRenderer,
        /// LineRenderer) bajo "Left Controller" y "Right Controller" de los XR Origin cargados; al volver a FOCUSED los
        /// restaura, aunque el menú siga abierto. Se oculta con <c>Renderer.forceRenderingOff</c> y no con <c>enabled</c> (ni se
        /// desactivan GameObjects ni interactors): XRI y UIOnlyFarCasting administran <c>enabled</c> y lo capturan al aplicarse,
        /// así que tocarlo corrompería su estado original. Los renderers de cada rig se cachean al ocultar; solo se vuelve a
        /// escanear cuando se carga o descarga una escena.
        /// </summary>
        public void RefreshControllerVisuals()
        {
            bool shouldHide = !m_Quitting && m_Armed && m_SessionState != VRSessionStateFeature.STATE_FOCUSED;
            if (shouldHide)
            {
                if (!m_VisualsHidden || m_RigsDirty) HideControllerVisuals();
            }
            else if (m_VisualsHidden)
            {
                RestoreControllerVisuals();
            }
        }

        private void HideControllerVisuals()
        {
            m_VisualsHidden = true;
            m_RigsDirty = false;

            // Se descartan los rigs y renderers destruidos (cambio de escena) y se toman los de los rigs nuevos.
            m_ScannedOrigins.RemoveWhere(o => o == null);
            m_RendererBuffer.Clear();
            foreach (KeyValuePair<Renderer, bool> pair in m_HiddenRenderers)
            {
                if (pair.Key == null) m_RendererBuffer.Add(pair.Key);
            }
            foreach (Renderer dead in m_RendererBuffer) m_HiddenRenderers.Remove(dead);

            if (!m_HasRestrictedScene)
            {
                foreach (Unity.XR.CoreUtils.XROrigin origin in UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include))
                {
                    HideRigRenderers(origin);
                }
                return;
            }

            foreach (GameObject root in m_RestrictedScene.GetRootGameObjects())
            {
                foreach (Unity.XR.CoreUtils.XROrigin origin in root.GetComponentsInChildren<Unity.XR.CoreUtils.XROrigin>(true))
                {
                    HideRigRenderers(origin);
                }
            }
        }

        private void HideRigRenderers(Unity.XR.CoreUtils.XROrigin origin)
        {
            if (!m_ScannedOrigins.Add(origin)) return;

            origin.GetComponentsInChildren(true, m_TransformBuffer);
            for (int i = 0; i < m_TransformBuffer.Count; i++)
            {
                Transform t = m_TransformBuffer[i];
                if (t.name != "Left Controller" && t.name != "Right Controller") continue;

                t.GetComponentsInChildren(true, m_RendererBuffer);
                foreach (Renderer r in m_RendererBuffer)
                {
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer) && !(r is LineRenderer)) continue;
                    if (m_HiddenRenderers.ContainsKey(r)) continue;
                    m_HiddenRenderers[r] = r.forceRenderingOff;
                    r.forceRenderingOff = true;
                }
            }
        }

        private void RestoreControllerVisuals()
        {
            foreach (KeyValuePair<Renderer, bool> pair in m_HiddenRenderers)
            {
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            }
            m_HiddenRenderers.Clear();
            m_ScannedOrigins.Clear();
            m_VisualsHidden = false;
        }

        // ------------------------------------------------------------------ Log

        private static void Log(string message) => Debug.Log($"{LOG_PREFIX} {message}");
    }
}
