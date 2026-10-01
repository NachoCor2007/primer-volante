using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Qué háptico corresponde a un movimiento de slider (ver <see cref="UIHapticFeedback.ClassifySliderMove"/>).
    /// </summary>
    public enum SliderHapticEvent
    {
        None = 0,

        /// <summary>Cruzó un múltiplo del step.</summary>
        Tick = 1,

        /// <summary>Pasó por 0 (posición original del asiento).</summary>
        Zero = 2,

        /// <summary>Llegó a su valor mínimo o máximo.</summary>
        Limit = 3
    }

    /// <summary>
    /// Hápticos de la UI en world space. Uno solo por escena: escucha los eventos de puntero del
    /// <see cref="XRUIInputModule"/>, así que cubre cualquier canvas, presente o futuro, sin configurarlo uno por uno.
    /// Vibra al entrar a un <see cref="Selectable"/> interactuable, al hacer click en un <see cref="Button"/> y al
    /// arrastrar un <see cref="Slider"/> (ticks, cruce por 0 y límites). No usa <c>onValueChanged</c>: los cambios
    /// por código (abrir el menú, Restablecer) no deben vibrar.
    /// </summary>
    public class UIHapticFeedback : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Perfil con las firmas hápticas.")]
        [SerializeField] private CabinHapticProfile m_Profile;

        [Tooltip("Módulo de input de UI de XRI. Si se deja vacío se busca en este GameObject y luego en la escena.")]
        [SerializeField] private XRUIInputModule m_InputModule;

        [Header("Debugging / Logs")]
        [SerializeField] private bool m_EnableDebugLogs = false;

        private const float k_Epsilon = 1e-4f;

        private sealed class SliderDrag
        {
            public Slider Slider;
            public float LastValue;
        }

        private readonly Dictionary<int, Selectable> m_Hovered = new Dictionary<int, Selectable>();
        private readonly Dictionary<int, SliderDrag> m_Drags = new Dictionary<int, SliderDrag>();

        /// <summary>Perfil asignado.</summary>
        public CabinHapticProfile Profile => m_Profile;

        private void Awake()
        {
            ResolveInputModule();
        }

        private void OnEnable()
        {
            ResolveInputModule();
            if (m_InputModule == null)
            {
                Debug.LogWarning("[UIHapticFeedback] ⚠️ No hay XRUIInputModule en la escena: la UI no vibrará.");
                return;
            }

            m_InputModule.pointerEnter += OnPointerEnter;
            m_InputModule.pointerExit += OnPointerExit;
            m_InputModule.pointerDown += OnPointerDown;
            m_InputModule.pointerUp += OnPointerUp;
            m_InputModule.pointerClick += OnPointerClick;
            m_InputModule.beginDrag += OnDrag;
            m_InputModule.drag += OnDrag;
            m_InputModule.endDrag += OnEndDrag;
        }

        private void OnDisable()
        {
            Unsubscribe();
            m_Hovered.Clear();
            m_Drags.Clear();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (m_InputModule == null) return;

            m_InputModule.pointerEnter -= OnPointerEnter;
            m_InputModule.pointerExit -= OnPointerExit;
            m_InputModule.pointerDown -= OnPointerDown;
            m_InputModule.pointerUp -= OnPointerUp;
            m_InputModule.pointerClick -= OnPointerClick;
            m_InputModule.beginDrag -= OnDrag;
            m_InputModule.drag -= OnDrag;
            m_InputModule.endDrag -= OnEndDrag;
        }

        private void ResolveInputModule()
        {
            if (m_InputModule != null) return;
            m_InputModule = GetComponent<XRUIInputModule>();
            if (m_InputModule == null) m_InputModule = Object.FindAnyObjectByType<XRUIInputModule>();
        }

        // ------------------------------------------------------------------ Hover y click

        private void OnPointerEnter(GameObject target, PointerEventData data)
        {
            if (m_Profile == null || data == null) return;

            // El módulo dispara pointerEnter para cada GameObject de la jerarquía; se resuelve siempre desde el más
            // profundo, así pasar entre graphics hijos del mismo botón no cuenta como un Selectable nuevo.
            GameObject deepest = data.pointerEnter != null ? data.pointerEnter : target;
            Selectable selectable = deepest != null ? deepest.GetComponentInParent<Selectable>() : null;
            if (selectable != null && !selectable.IsInteractable()) selectable = null;

            int id = data.pointerId;
            m_Hovered.TryGetValue(id, out Selectable previous);
            if (previous != null && !previous.isActiveAndEnabled) previous = null;

            if (selectable == previous) return;

            if (selectable == null) m_Hovered.Remove(id);
            else m_Hovered[id] = selectable;

            if (selectable != null)
                PlayForPointer(id, m_Profile.UI.Hover, "hover");
        }

        private void OnPointerExit(GameObject target, PointerEventData data)
        {
            if (data == null) return;
            if (m_Hovered.TryGetValue(data.pointerId, out Selectable hovered) && (hovered == null || hovered.gameObject == target))
                m_Hovered.Remove(data.pointerId);
        }

        private void OnPointerClick(GameObject target, PointerEventData data)
        {
            if (m_Profile == null || data == null || target == null) return;

            Button button = target.GetComponentInParent<Button>();
            if (button == null || !button.IsInteractable()) return;

            PlayForPointer(data.pointerId, m_Profile.UI.Click, "click");
        }

        // ------------------------------------------------------------------ Sliders

        private void OnPointerDown(GameObject target, PointerEventData data)
        {
            if (data == null) return;

            // Se llama antes de que el Slider procese el press: el valor guardado es el de "antes".
            Slider slider = target != null ? target.GetComponentInParent<Slider>() : null;
            if (slider != null && slider.IsInteractable())
                m_Drags[data.pointerId] = new SliderDrag { Slider = slider, LastValue = slider.value };
            else
                m_Drags.Remove(data.pointerId);
        }

        private void OnDrag(GameObject target, PointerEventData data)
        {
            if (data == null) return;
            EvaluateSlider(data.pointerId);
        }

        private void OnPointerUp(GameObject target, PointerEventData data)
        {
            if (data == null) return;
            EvaluateSlider(data.pointerId);
            m_Drags.Remove(data.pointerId);
        }

        private void OnEndDrag(GameObject target, PointerEventData data)
        {
            if (data == null) return;
            EvaluateSlider(data.pointerId);
            m_Drags.Remove(data.pointerId);
        }

        /// <summary>
        /// Compara el valor actual del slider arrastrado con el de la última evaluación y vibra
        /// según el tipo de movimiento. Los eventos de arrastre se disparan antes de que el Slider procese
        /// el movimiento, por eso el resultado de cada paso se evalúa en el evento siguiente (y en el soltado).
        /// </summary>
        private void EvaluateSlider(int pointerId)
        {
            if (m_Profile == null) return;
            if (!m_Drags.TryGetValue(pointerId, out SliderDrag drag)) return;

            Slider slider = drag.Slider;
            if (slider == null || !slider.isActiveAndEnabled)
            {
                m_Drags.Remove(pointerId);
                return;
            }

            float current = slider.value;
            float previous = drag.LastValue;
            if (Mathf.Approximately(current, previous)) return;
            drag.LastValue = current;

            switch (ClassifySliderMove(previous, current, slider.minValue, slider.maxValue, m_Profile.SliderStep))
            {
                case SliderHapticEvent.Limit:
                    PlayForPointer(pointerId, m_Profile.UI.SliderLimit, "slider límite");
                    break;
                case SliderHapticEvent.Zero:
                    PlayForPointer(pointerId, m_Profile.UI.SliderZero, "slider cero");
                    break;
                case SliderHapticEvent.Tick:
                    PlayForPointer(pointerId, m_Profile.UI.SliderTick, "slider tick");
                    break;
            }
        }

        // ------------------------------------------------------------------ Lógica pura (testeable)

        /// <summary>
        /// Indica si al pasar de <paramref name="from"/> a <paramref name="to"/> se cruzó un múltiplo de <paramref name="step"/>
        /// (en cualquier sentido).
        /// </summary>
        public static bool CrossedSliderStep(float from, float to, float step)
        {
            if (step <= 0f) return false;
            return Mathf.FloorToInt(to / step + k_Epsilon) != Mathf.FloorToInt(from / step + k_Epsilon);
        }

        /// <summary>
        /// Indica si el movimiento pasó por 0 o aterrizó en 0. Salir de 0 no cuenta.
        /// </summary>
        public static bool CrossedSliderZero(float from, float to)
        {
            int signFrom = SignWithTolerance(from);
            int signTo = SignWithTolerance(to);
            if (signFrom == 0) return false;
            return signTo != signFrom;
        }

        /// <summary>
        /// Indica si el movimiento llegó a <paramref name="min"/> o <paramref name="max"/> (estando antes fuera del límite).
        /// </summary>
        public static bool ReachedSliderLimit(float from, float to, float min, float max)
        {
            bool atMinNow = to <= min + k_Epsilon;
            bool atMinBefore = from <= min + k_Epsilon;
            bool atMaxNow = to >= max - k_Epsilon;
            bool atMaxBefore = from >= max - k_Epsilon;
            return (atMinNow && !atMinBefore) || (atMaxNow && !atMaxBefore);
        }

        /// <summary>
        /// Clasifica un movimiento de slider con prioridad límite &gt; cruce por 0 &gt; tick, de modo que
        /// cada movimiento dispare a lo sumo un patrón.
        /// </summary>
        public static SliderHapticEvent ClassifySliderMove(float from, float to, float min, float max, float step)
        {
            if (Mathf.Approximately(from, to)) return SliderHapticEvent.None;
            if (ReachedSliderLimit(from, to, min, max)) return SliderHapticEvent.Limit;
            if (CrossedSliderZero(from, to)) return SliderHapticEvent.Zero;
            if (CrossedSliderStep(from, to, step)) return SliderHapticEvent.Tick;
            return SliderHapticEvent.None;
        }

        private static int SignWithTolerance(float value)
        {
            if (value > k_Epsilon) return 1;
            if (value < -k_Epsilon) return -1;
            return 0;
        }

        // ------------------------------------------------------------------ Reproducción

        private void PlayForPointer(int pointerId, HapticPattern pattern, string label)
        {
            if (m_InputModule == null || pattern == null) return;

            // Sin interactor (mouse del Editor) no hay mano a la que vibrar.
            IUIInteractor interactor = m_InputModule.GetInteractor(pointerId);
            if (interactor == null) return;

            if (HapticPlayback.Play(interactor, pattern, m_Profile.MasterAmplitude) && m_EnableDebugLogs)
                Debug.Log($"[UIHapticFeedback] 📳 {label} (pointer {pointerId})");
        }
    }
}
