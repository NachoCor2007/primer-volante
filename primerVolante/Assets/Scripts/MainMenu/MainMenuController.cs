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

        [Header("Capa de pruebas (Editor)")]
        [Tooltip("Tecla que simula pulsar el botón de ignición. (South y la tecla E las usa VehicleController para el motor.)")]
        [SerializeField] private Key m_IgnitionKey = Key.Enter;

        private MainMenuPage m_Page = MainMenuPage.Splash;
        private GearState m_CurrentGear = GearState.P;
        private MenuScrollAccumulator m_Scroll;
        private bool m_Initialized;
        private bool m_CalibrationOpen;
        private UnityEngine.Events.UnityAction<GearState> m_GearListener;

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
            switch (gear)
            {
                case GearState.R: return MainMenuPage.Controls;
                case GearState.N: return MainMenuPage.Calibration;
                case GearState.D: return MainMenuPage.Credits;
                default: return MainMenuPage.Lessons;
            }
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

            Subscribe();
            m_Page = MainMenuPage.Splash;
            ApplyPage();
        }

        /// <summary>Reinicia el menú: vuelve al Splash con el cursor en 0 y sin lección marcada. Útil para tests.</summary>
        public void Restart()
        {
            Unsubscribe();
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

        /// <summary>Motor apagado, freno de mano puesto y Rigidbody congelado: el auto no se mueve ni hace ruido.</summary>
        private void ImmobilizeVehicle()
        {
            if (m_Vehicle == null) return;

            m_Vehicle.SetEngineRunning(false);
            if (m_Handbrake != null) m_Handbrake.SetEngagementImmediate(1f);

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
            if (m_Vehicle != null) m_Vehicle.OnEngineStateChanged += HandleEngineStateChanged;
        }

        private void Unsubscribe()
        {
            if (m_GearShifter != null && m_GearListener != null) m_GearShifter.OnGearChanged.RemoveListener(m_GearListener);
            m_GearListener = null;
            if (m_StartButton != null) m_StartButton.Pressed -= HandleIgnitionPressed;
            if (m_Vehicle != null) m_Vehicle.OnEngineStateChanged -= HandleEngineStateChanged;
        }

        // Si el motor llegara a encenderse por otra vía (p. ej. la capa de pruebas de VehicleController), se apaga enseguida.
        private void HandleEngineStateChanged(EngineState state)
        {
            if (state != EngineState.Off && m_Vehicle != null) m_Vehicle.SetEngineRunning(false);
        }

        /// <summary>Cambio de marcha: durante el Splash solo se registra; en el resto cambia la página.</summary>
        public void HandleGearChanged(GearState gear)
        {
            m_CurrentGear = gear;
            if (m_Page == MainMenuPage.Splash) return;
            SetPage(PageForGear(gear));
        }

        /// <summary>Pulsación de la ignición: sale del Splash, o selecciona la lección bajo el cursor en Lecciones.</summary>
        public void HandleIgnitionPressed()
        {
            switch (m_Page)
            {
                case MainMenuPage.Splash:
                    SetPage(PageForGear(m_CurrentGear));
                    break;
                case MainMenuPage.Lessons:
                    SelectCurrentLesson();
                    break;
            }
        }

        private void SelectCurrentLesson()
        {
            if (m_LessonList == null || m_LessonList.Count == 0) return;

            int index = m_LessonList.CursorIndex;
            LessonDefinitionSO lesson = m_LessonList.GetLesson(index);
            // Placeholder: por ahora no carga ninguna escena.
            Debug.Log("[MainMenu] Lección seleccionada: " + (lesson != null ? lesson.lessonId : "(sin definir)"));
            m_LessonList.MarkSelected(index);
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

            bool splash = m_Page == MainMenuPage.Splash;
            if (m_GearStripRoot != null) m_GearStripRoot.SetActive(!splash);
            for (int i = 0; i < m_GearTabs.Length; i++)
            {
                if (m_GearTabs[i] == null) continue;
                bool active = !splash && i == (int)PageGear(m_Page);
                m_GearTabs[i].color = active ? m_ActiveTabColor : m_InactiveTabColor;
                m_GearTabs[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }

            UpdateLegendAndHighlight();
        }

        private static GearState PageGear(MainMenuPage page)
        {
            switch (page)
            {
                case MainMenuPage.Controls: return GearState.R;
                case MainMenuPage.Calibration: return GearState.N;
                case MainMenuPage.Credits: return GearState.D;
                default: return GearState.P;
            }
        }

        private void UpdateLegendAndHighlight()
        {
            var lines = new List<string>();
            var targets = new List<GameObject>();

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

            if (m_Page == MainMenuPage.Lessons && m_SteeringWheel != null)
                ApplyScroll(m_SteeringWheel.CurrentAngle, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Convierte el ángulo del volante en pasos de cursor (ver <see cref="MenuScrollMath"/> y
        /// <see cref="MenuScrollAccumulator"/>) y los aplica a la lista. Solo actúa en la página Lecciones.
        /// </summary>
        public void ApplyScroll(float wheelAngleDeg, float dt)
        {
            if (m_Page != MainMenuPage.Lessons || m_LessonList == null) return;

            float rate = MenuScrollMath.ItemsPerSecond(wheelAngleDeg, m_DeadZoneDegrees, m_FullAngleDegrees, m_MaxItemsPerSecond);
            int steps = m_Scroll.Step(rate, dt);
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
