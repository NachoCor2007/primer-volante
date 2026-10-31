using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.MainMenu
{
    /// <summary>Página visible del menú principal.</summary>
    public enum MainMenuPage
    {
        Splash = 0,
        Lessons = 1,
        Controls = 2,
        Calibration = 3,
        Credits = 4
    }

    /// <summary>
    /// Menú principal en cabina (issue #7). Se maneja con los mandos del propio vehículo:
    /// la palanca elige la página (P = Lecciones, R = Controles, N = Calibración, D = Créditos), el volante desplaza el
    /// cursor de la lista de lecciones (<see cref="MenuScrollMath"/>) y el botón de ignición confirma.
    /// Empieza en una pantalla de bienvenida (Splash) que termina al pulsar la ignición. El auto queda inmovilizado
    /// (motor apagado, freno de mano puesto, Rigidbody congelado) para que interactuar no genere ruido.
    /// La selección de lección es un placeholder: loguea y marca el ítem, no carga ninguna escena.
    /// Para que la palanca y el botón funcionen sin motor, la escena debe tener <c>m_IgnoreDrivingRestrictions</c> en el
    /// VRGearShifter y <c>m_TogglesEngine</c> apagado en el StartButton.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Mandos del vehículo")]
        [SerializeField] private VehicleController m_Vehicle;
        [SerializeField] private VRGearShifter m_GearShifter;
        [SerializeField] private VRSteeringWheel m_SteeringWheel;
        [SerializeField] private StartButton m_StartButton;
        [SerializeField] private VRHandbrake m_Handbrake;

        [Header("Menús y resaltado")]
        [Tooltip("Menú in-game, para abrir/cerrar la calibración standalone en la página Calibración.")]
        [SerializeField] private InGameMenuController m_InGameMenu;

        [Tooltip("Resalta los mandos nombrados en la leyenda. Si se deja vacío se autodetecta.")]
        [SerializeField] private InteractableHighlighter m_Highlighter;

        [Header("UI")]
        [SerializeField] private LessonListPanel m_LessonList;

        [Tooltip("Una raíz por página, en el orden del enum MainMenuPage: Splash, Lessons, Controls, Calibration, Credits.")]
        [SerializeField] private GameObject[] m_PageRoots = new GameObject[5];

        [Tooltip("Tira de marchas de arriba, en el orden P, R, N, D (el texto de cada tab).")]
        [SerializeField] private TextMeshProUGUI[] m_GearTabs = new TextMeshProUGUI[4];

        [Tooltip("Raíz de la tira de marchas (se oculta durante el splash).")]
        [SerializeField] private GameObject m_GearStripRoot;

        [Tooltip("Raíz del panel de leyenda lateral (se oculta si no tiene texto).")]
        [SerializeField] private GameObject m_LegendRoot;

        [Tooltip("Texto de la leyenda lateral.")]
        [SerializeField] private TextMeshProUGUI m_LegendText;

        [SerializeField] private Color m_ActiveTabColor = new Color(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private Color m_InactiveTabColor = new Color(0.65f, 0.67f, 0.72f, 1f);

        [Header("Volante")]
        [Tooltip("Zona muerta del volante (grados).")]
        [SerializeField] private float m_DeadZoneDegrees = MenuScrollMath.DefaultDeadZoneDegrees;

        [Tooltip("Ángulo al que se alcanza la velocidad máxima de desplazamiento (grados).")]
        [SerializeField] private float m_FullAngleDegrees = MenuScrollMath.DefaultFullAngleDegrees;

        [Tooltip("Ítems por segundo a ese ángulo.")]
        [SerializeField] private float m_MaxItemsPerSecond = MenuScrollMath.DefaultMaxItemsPerSecond;

        [Tooltip("Histéresis: ya en movimiento, el cursor se detiene recién al bajar de este ángulo (grados). Debe ser menor a la zona muerta.")]
        [SerializeField] private float m_ReleaseDegrees = MenuScrollMath.DefaultReleaseDegrees;

        [Header("Palanca")]
        [Tooltip("Segundos que una marcha debe estar estable para cambiar de página. Al sostener la palanca, el imán " +
                 "dispara OnGearChanged en cada marcha que cruza (P a D pasa por R y N): así no se abren páginas de paso.")]
        [SerializeField] private float m_GearSettleSeconds = 0.25f;

        [Header("Salida (freno de mano)")]
        [Tooltip("Overlay de confirmación de salida sobre el canvas principal. Se muestra con el freno de mano tirado del todo.")]
        [SerializeField] private GameObject m_ExitOverlay;

        [Tooltip("Engagement del freno de mano (0..1) desde el cual se muestra el overlay de salida.")]
        [SerializeField] private float m_ExitRaiseThreshold = 0.95f;

        [Tooltip("Histéresis: el overlay se oculta (cancelación) recién cuando el freno baja de este valor. Debe ser menor al umbral de salida.")]
        [SerializeField] private float m_ExitCancelThreshold = 0.85f;

        [Tooltip("Si está activo, confirmar la salida cierra la aplicación (Application.Quit; en el Editor, sale de Play Mode). " +
                 "Los tests y el Play Mode de prueba lo apagan para verificar solo el evento ExitRequested.")]
        [SerializeField] private bool m_PerformQuit = true;

        [Header("Capa de pruebas (Editor)")]
        [Tooltip("Tecla que simula pulsar el botón de ignición. (South y la tecla E las usa VehicleController para el motor.)")]
        [SerializeField] private Key m_IgnitionKey = Key.Enter;

        private MainMenuPage m_Page = MainMenuPage.Splash;
        private GearState m_CurrentGear = GearState.P;
        private MenuScrollAccumulator m_Scroll;
        private bool m_Initialized;
        private bool m_CalibrationOpen;
        private UnityEngine.Events.UnityAction<GearState> m_GearListener;
        private bool m_HasPendingGear;
        private GearState m_PendingGear;
        private float m_PendingGearTimer;
        private bool m_ExitOverlayVisible;

        /// <summary>Se dispara al confirmar la salida con la ignición (antes de cerrar la aplicación si <c>m_PerformQuit</c>).</summary>
        public event System.Action ExitRequested;

        // Única fuente de verdad del mapeo página <-> marcha, indexada por MainMenuPage (Splash no tiene marcha propia).
        private static readonly GearState[] s_GearByPage =
        {
            GearState.P, // Splash
            GearState.P, // Lessons
            GearState.R, // Controls
            GearState.N, // Calibration
            GearState.D  // Credits
        };

        /// <summary>Página actual.</summary>
        public MainMenuPage CurrentPage => m_Page;

        /// <summary>Marcha registrada (la palanca puede haberse movido durante el splash).</summary>
        public GearState CurrentGear => m_CurrentGear;

        /// <summary>Índice del cursor de la lista de lecciones.</summary>
        public int CursorIndex => m_LessonList != null ? m_LessonList.CursorIndex : 0;

        /// <summary>Índice de la última lección marcada, o -1.</summary>
        public int SelectedLessonIndex => m_LessonList != null ? m_LessonList.SelectedIndex : -1;

        /// <summary>Texto actual de la leyenda lateral.</summary>
        public string LegendString => m_LegendText != null ? m_LegendText.text : string.Empty;

        /// <summary>Mapeo marcha → página: P = Lecciones, R = Controles, N = Calibración, D = Créditos.</summary>
        public static MainMenuPage PageForGear(GearState gear)
        {
            for (int page = (int)MainMenuPage.Lessons; page < s_GearByPage.Length; page++)
            {
                if (s_GearByPage[page] == gear) return (MainMenuPage)page;
            }
            return MainMenuPage.Lessons;
        }

        /// <summary>Marcha que corresponde a una página (inversa de <see cref="PageForGear"/>; el Splash cuenta como P).</summary>
        public static GearState GearForPage(MainMenuPage page)
        {
            return s_GearByPage[(int)page];
        }

        /// <summary>true mientras hay un cambio de marcha esperando estabilizarse (<c>m_GearSettleSeconds</c>).</summary>
        public bool HasPendingGear => m_HasPendingGear;

        /// <summary>true mientras se muestra el overlay de confirmación de salida.</summary>
        public bool ExitOverlayVisible => m_ExitOverlayVisible;

        /// <summary>Índice (0..3 = P, R, N, D) de la pestaña resaltada en la tira de marchas, o -1 si está oculta (Splash).</summary>
        public int ActiveGearTabIndex => m_Page == MainMenuPage.Splash ? -1 : (int)PageForGear(m_CurrentGear) - (int)MainMenuPage.Lessons;

        /// <summary>Enciende o apaga el cierre real de la aplicación al confirmar la salida (para tests y Play Mode de prueba).</summary>
        public void SetPerformQuit(bool performQuit)
        {
            m_PerformQuit = performQuit;
        }

        private void Start()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (m_Initialized) Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (m_Highlighter != null) m_Highlighter.ClearHighlight();
        }

        /// <summary>
        /// Autodetecta referencias vacías, inmoviliza el auto, se suscribe a los mandos y muestra el Splash.
        /// Idempotente; la llama Start y los tests.
        /// </summary>
        public void Initialize()
        {
            if (m_Initialized) return;
            m_Initialized = true;

            AutoDetectReferences();
            ImmobilizeVehicle();

            if (m_GearShifter != null) m_CurrentGear = m_GearShifter.CurrentGear;
            m_Scroll.Reset();
            m_ExitOverlayVisible = false;
            if (m_ExitOverlay != null) m_ExitOverlay.SetActive(false);
            ApplyIllustrationVisibility();

            Subscribe();
            m_Page = MainMenuPage.Splash;
            ApplyPage();
        }

        /// <summary>Reinicia el menú: vuelve al Splash con el cursor en 0 y sin lección marcada. Útil para tests.</summary>
        public void Restart()
        {
            Unsubscribe();
            m_HasPendingGear = false;
            m_Initialized = false;
            if (m_LessonList != null)
            {
                m_LessonList.SetCursor(0);
                m_LessonList.MarkSelected(-1);
            }
            Initialize();
        }

        private void AutoDetectReferences()
        {
            if (m_Vehicle == null) m_Vehicle = Object.FindAnyObjectByType<VehicleController>(FindObjectsInactive.Include);
            if (m_Vehicle != null)
            {
                if (m_GearShifter == null) m_GearShifter = m_Vehicle.GetComponentInChildren<VRGearShifter>(true);
                if (m_SteeringWheel == null) m_SteeringWheel = m_Vehicle.GetComponentInChildren<VRSteeringWheel>(true);
                if (m_StartButton == null) m_StartButton = m_Vehicle.GetComponentInChildren<StartButton>(true);
                if (m_Handbrake == null) m_Handbrake = m_Vehicle.GetComponentInChildren<VRHandbrake>(true);
            }
            if (m_GearShifter == null) m_GearShifter = Object.FindAnyObjectByType<VRGearShifter>(FindObjectsInactive.Include);
            if (m_SteeringWheel == null) m_SteeringWheel = Object.FindAnyObjectByType<VRSteeringWheel>(FindObjectsInactive.Include);
            if (m_StartButton == null) m_StartButton = Object.FindAnyObjectByType<StartButton>(FindObjectsInactive.Include);
            if (m_InGameMenu == null) m_InGameMenu = Object.FindAnyObjectByType<InGameMenuController>(FindObjectsInactive.Include);
            if (m_Highlighter == null) m_Highlighter = Object.FindAnyObjectByType<InteractableHighlighter>(FindObjectsInactive.Include);
            if (m_LessonList == null) m_LessonList = Object.FindAnyObjectByType<LessonListPanel>(FindObjectsInactive.Include);
        }

        /// <summary>
        /// Motor apagado y bloqueado y Rigidbody congelado: el auto no se mueve ni hace ruido. El freno de mano arranca
        /// ABAJO (Engagement 0): tirarlo del todo pide salir (overlay). Este ajuste inicial ocurre en el primer frame, dentro
        /// del período de gracia de los scripts de audio y hápticos del freno, así que no suena ni vibra.
        /// </summary>
        private void ImmobilizeVehicle()
        {
            if (m_Vehicle == null) return;

            m_Vehicle.SetEngineRunning(false);
            m_Vehicle.SetEngineLocked(true);
            if (m_Handbrake != null) m_Handbrake.SetEngagementImmediate(0f);

            var body = m_Vehicle.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.constraints = RigidbodyConstraints.FreezeAll;
            }
        }

        private void Subscribe()
        {
            Unsubscribe();

            if (m_GearShifter != null)
            {
                m_GearListener = HandleGearChanged;
                m_GearShifter.OnGearChanged.AddListener(m_GearListener);
            }
            if (m_StartButton != null) m_StartButton.Pressed += HandleIgnitionPressed;
        }

        private void Unsubscribe()
        {
            if (m_GearShifter != null && m_GearListener != null) m_GearShifter.OnGearChanged.RemoveListener(m_GearListener);
            m_GearListener = null;
            if (m_StartButton != null) m_StartButton.Pressed -= HandleIgnitionPressed;
        }

        /// <summary>
        /// Cambio de marcha. Durante el Splash solo se registra. Fuera de él NO cambia de página directo: queda
        /// pendiente y <see cref="Tick"/> lo aplica cuando lleva estable <c>m_GearSettleSeconds</c> (cada nueva
        /// notificación reinicia la espera), para no abrir las páginas intermedias al barrer la palanca.
        /// </summary>
        public void HandleGearChanged(GearState gear)
        {
            m_CurrentGear = gear;
            if (m_Page == MainMenuPage.Splash) return;

            // La tira de marchas sigue a la palanca en el momento (también las marchas de paso); lo demás espera.
            UpdateGearStrip();

            m_PendingGear = gear;
            m_PendingGearTimer = 0f;
            m_HasPendingGear = true;
        }

        /// <summary>
        /// Pulsación de la ignición. Con el overlay de salida visible confirma la salida (tiene prioridad sobre la página).
        /// Si no: sale del Splash, o selecciona la lección bajo el cursor en Lecciones.
        /// </summary>
        public void HandleIgnitionPressed()
        {
            if (m_ExitOverlayVisible)
            {
                ConfirmExit();
                return;
            }

            switch (m_Page)
            {
                case MainMenuPage.Splash:
                    m_HasPendingGear = false;
                    SetPage(PageForGear(m_CurrentGear));
                    break;
                case MainMenuPage.Lessons:
                    SelectCurrentLesson();
                    break;
            }
        }

        private void ConfirmExit()
        {
            Debug.Log("[MainMenu] Saliendo del simulador");
            ExitRequested?.Invoke();

            if (!m_PerformQuit) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Freno de mano: tirado del todo (>= umbral) pide salir; bajarlo de la histéresis cancela.
        private void UpdateExitOverlay()
        {
            if (m_Handbrake == null) return;

            float engagement = m_Handbrake.Engagement;
            if (!m_ExitOverlayVisible && engagement >= m_ExitRaiseThreshold)
            {
                SetExitOverlay(true);
                Debug.Log("[MainMenu] Salida solicitada");
            }
            else if (m_ExitOverlayVisible && engagement < m_ExitCancelThreshold)
            {
                SetExitOverlay(false);
                Debug.Log("[MainMenu] Salida cancelada");
            }
        }

        private void SetExitOverlay(bool visible)
        {
            m_ExitOverlayVisible = visible;
            if (m_ExitOverlay != null) m_ExitOverlay.SetActive(visible);
            ApplyIllustrationVisibility();
            UpdateLegendAndHighlight();
        }

        // Los modelos 3D de los mandos se dibujan sobre la UI del canvas: con el overlay de salida visible se ocultan todos
        // (también los de las páginas que se activen mientras tanto); al cancelar vuelven, y las páginas inactivas siguen ocultas.
        private void ApplyIllustrationVisibility()
        {
            for (int i = 0; i < m_PageRoots.Length; i++)
            {
                if (m_PageRoots[i] == null) continue;
                foreach (ControllerIllustration illustration in m_PageRoots[i].GetComponentsInChildren<ControllerIllustration>(true))
                {
                    illustration.SetModelVisible(!m_ExitOverlayVisible);
                }
            }
        }

        private void SelectCurrentLesson()
        {
            if (m_LessonList == null || m_LessonList.Count == 0) return;

            int index = m_LessonList.CursorIndex;
            LessonDefinitionSO lesson = m_LessonList.GetLesson(index);
            Debug.Log("[MainMenu] Lección seleccionada: " + (lesson != null ? lesson.lessonId : "(sin definir)"));
            m_LessonList.MarkSelected(index);

            if (lesson != null && Application.isPlaying && LessonSceneFlowManager.Instance != null)
            {
                LessonSceneFlowManager.Instance.LoadLesson(lesson);
            }
        }

        private void SetPage(MainMenuPage page)
        {
            if (page == m_Page) return;
            m_Page = page;
            m_Scroll.Reset();
            ApplyPage();
        }

        private void ApplyPage()
        {
            // La calibración standalone solo vive en su página.
            bool wantCalibration = m_Page == MainMenuPage.Calibration;
            if (m_InGameMenu != null)
            {
                if (wantCalibration && !m_CalibrationOpen)
                {
                    m_InGameMenu.OpenCalibrationStandalone();
                    m_CalibrationOpen = true;
                }
                else if (!wantCalibration && m_CalibrationOpen)
                {
                    m_InGameMenu.CloseMenu();
                    m_CalibrationOpen = false;
                }
            }

            for (int i = 0; i < m_PageRoots.Length; i++)
            {
                if (m_PageRoots[i] != null) m_PageRoots[i].SetActive(i == (int)m_Page);
            }

            UpdateGearStrip();
            UpdateLegendAndHighlight();
        }

        // La pestaña activa sigue a la marcha registrada (m_CurrentGear), no a la página: se mueve al instante.
        // Durante el Splash la tira está oculta.
        private void UpdateGearStrip()
        {
            bool splash = m_Page == MainMenuPage.Splash;
            if (m_GearStripRoot != null) m_GearStripRoot.SetActive(!splash);

            int activeTab = ActiveGearTabIndex;
            for (int i = 0; i < m_GearTabs.Length; i++)
            {
                if (m_GearTabs[i] == null) continue;
                bool active = i == activeTab;
                m_GearTabs[i].color = active ? m_ActiveTabColor : m_InactiveTabColor;
                m_GearTabs[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private void UpdateLegendAndHighlight()
        {
            var lines = new List<string>();
            var targets = new List<GameObject>();

            if (m_ExitOverlayVisible)
            {
                // Con el overlay de salida: solo se confirma o se cancela; se resalta la ignición.
                lines.Add("Botón de encendido: confirmar salida");
                lines.Add("Freno de mano: bajar para cancelar");
                AddTarget(targets, m_StartButton != null ? m_StartButton.gameObject : null);
                ApplyLegend(lines, targets);
                return;
            }

            switch (m_Page)
            {
                case MainMenuPage.Splash:
                    lines.Add("Botón de encendido: comenzar");
                    AddTarget(targets, m_StartButton != null ? m_StartButton.gameObject : null);
                    break;
                case MainMenuPage.Lessons:
                    lines.Add("Volante: ← subir / → bajar (más giro, más rápido)");
                    lines.Add("Botón de encendido: seleccionar lección");
                    lines.Add("Palanca: cambiar de menú");
                    AddTarget(targets, m_SteeringWheel != null ? m_SteeringWheel.gameObject : null);
                    AddTarget(targets, m_StartButton != null ? m_StartButton.gameObject : null);
                    AddTarget(targets, m_GearShifter != null ? m_GearShifter.gameObject : null);
                    break;
                case MainMenuPage.Calibration:
                    lines.Add("Gatillo + rayo: mover los sliders");
                    lines.Add("Palanca: cambiar de menú (cierra la calibración)");
                    AddTarget(targets, m_GearShifter != null ? m_GearShifter.gameObject : null);
                    break;
                default: // Controles / Créditos
                    lines.Add("Palanca: cambiar de menú");
                    AddTarget(targets, m_GearShifter != null ? m_GearShifter.gameObject : null);
                    break;
            }

            // En todas las pantallas: cómo salir. Sin resaltar el freno de mano.
            lines.Add("Freno de mano: tirar para salir");
            ApplyLegend(lines, targets);
        }

        private void ApplyLegend(List<string> lines, List<GameObject> targets)
        {
            if (m_LegendText != null) m_LegendText.text = string.Join("\n\n", lines);
            if (m_LegendRoot != null) m_LegendRoot.SetActive(lines.Count > 0);
            if (m_Highlighter != null) m_Highlighter.HighlightTargets(targets);
        }

        private static void AddTarget(List<GameObject> list, GameObject go)
        {
            if (go != null) list.Add(go);
        }

        private void Update()
        {
            ReadIgnitionTestLayer();
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Avanza el menú <paramref name="dt"/> segundos: revisa el freno de mano (overlay de salida), aplica la marcha pendiente si lleva estable
        /// <c>m_GearSettleSeconds</c> y, en la página Lecciones, mueve el cursor con el ángulo actual del volante.
        /// La llama Update; los tests la llaman con dt inyectado.
        /// </summary>
        public void Tick(float dt)
        {
            UpdateExitOverlay();

            if (m_HasPendingGear)
            {
                m_PendingGearTimer += dt;
                if (m_PendingGearTimer >= m_GearSettleSeconds)
                {
                    m_HasPendingGear = false;
                    SetPage(PageForGear(m_PendingGear));
                }
            }

            if (m_Page == MainMenuPage.Lessons && m_SteeringWheel != null)
                ApplyScroll(m_SteeringWheel.CurrentAngle, dt);
        }

        /// <summary>
        /// Convierte el ángulo del volante en pasos de cursor (ver <see cref="MenuScrollMath"/> y
        /// <see cref="MenuScrollAccumulator"/>) y los aplica a la lista. Solo actúa en la página Lecciones.
        /// </summary>
        public void ApplyScroll(float wheelAngleDeg, float dt)
        {
            if (m_Page != MainMenuPage.Lessons || m_LessonList == null) return;

            int steps = m_Scroll.StepAngle(wheelAngleDeg, dt, m_DeadZoneDegrees, m_ReleaseDegrees, m_FullAngleDegrees, m_MaxItemsPerSecond);
            if (steps != 0) m_LessonList.MoveCursor(steps);
        }

        // Capa de pruebas: Enter y Gamepad buttonEast pulsan la ignición (South y E los usa VehicleController).
        private void ReadIgnitionTestLayer()
        {
            if (m_StartButton == null) return;

            bool pressed = false;
            if (Keyboard.current != null && Keyboard.current[m_IgnitionKey].wasPressedThisFrame) pressed = true;
            if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) pressed = true;

            if (pressed) m_StartButton.Press();
        }
    }
}
