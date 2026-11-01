using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.Lessons;

namespace PrimerVolante.MainMenu
{
    /// <summary>
    /// Lista de lecciones (uGUI, World Space) del menú principal. Arma un ítem por cada <see cref="LessonDefinitionSO"/>
    /// mostrando su <c>lessonTitle</c>, con un cursor discreto resaltado que se frena en los extremos (no da la vuelta).
    /// Si la lista no entra en el contenedor, el contenido se desliza (suavizado) para mantener visible la fila del cursor;
    /// un <see cref="RectMask2D"/> en el contenedor recorta lo que queda afuera, así asoma el borde de la fila siguiente.
    /// <see cref="MarkSelected"/> muestra la marca "Seleccionada" en un ítem.
    /// </summary>
    public class LessonListPanel : MonoBehaviour
    {
        [Header("Contenido")]
        [Tooltip("Lecciones a listar, en orden (LessonData_01..06).")]
        [SerializeField] private List<LessonDefinitionSO> m_Lessons = new List<LessonDefinitionSO>();

        [Tooltip("Contenedor de los ítems (con VerticalLayoutGroup). Si se deja vacío se usa este mismo transform.")]
        [SerializeField] private RectTransform m_Container;

        [Tooltip("Fuente TMP de los ítems. Si se deja vacía se usa la fuente por defecto de TMP.")]
        [SerializeField] private TMP_FontAsset m_Font;

        [Header("Aspecto")]
        [SerializeField] private float m_ItemHeight = 150f;
        [SerializeField] private float m_FontSize = 48f;
        [SerializeField] private float m_SelectedMarkFontSize = 36f;
        [SerializeField] private Color m_NormalColor = new Color(0.16f, 0.17f, 0.22f, 1f);
        [SerializeField] private Color m_CursorColor = new Color(0.2f, 0.5f, 0.95f, 1f);
        [SerializeField] private Color m_TextColor = Color.white;
        [SerializeField] private Color m_SelectedMarkColor = new Color(0.5f, 1f, 0.6f, 1f);

        [Header("Desplazamiento")]
        [Tooltip("Tiempo de suavizado (s) del deslizamiento hacia la fila del cursor. 0 = salto inmediato.")]
        [SerializeField] private float m_ScrollSmoothTime = 0.12f;

        private class Item
        {
            public GameObject root;
            public Image background;
            public TextMeshProUGUI title;
            public TextMeshProUGUI selectedMark;
        }

        private readonly List<Item> m_Items = new List<Item>();
        private int m_CursorIndex;
        private int m_SelectedIndex = -1;
        private bool m_Built;
        private float m_ScrollOffset;       // desplazamiento aplicado (px, 0 = arriba)
        private float m_TargetScrollOffset; // desplazamiento al que se desliza
        private float m_ScrollVelocity;
        private int m_BasePaddingTop;
        private bool m_BasePaddingCached;

        /// <summary>Cantidad de ítems armados.</summary>
        public int Count { get { EnsureBuilt(); return m_Items.Count; } }

        /// <summary>Índice del cursor.</summary>
        public int CursorIndex { get { EnsureBuilt(); return m_CursorIndex; } }

        /// <summary>Índice marcado como "Seleccionada", o -1 si ninguno.</summary>
        public int SelectedIndex => m_SelectedIndex;

        /// <summary>Índice del primer ítem completamente visible (con el desplazamiento de destino).</summary>
        public int FirstVisibleIndex
        {
            get
            {
                EnsureBuilt();
                for (int i = 0; i < m_Items.Count; i++)
                {
                    if (IsItemVisible(i)) return i;
                }
                return 0;
            }
        }

        /// <summary>Desplazamiento de destino del contenido (px desde arriba).</summary>
        public float TargetScrollOffset { get { EnsureBuilt(); return m_TargetScrollOffset; } }

        /// <summary>Desplazamiento aplicado ahora (px desde arriba); se acerca a <see cref="TargetScrollOffset"/> en Play Mode.</summary>
        public float ScrollOffset => m_ScrollOffset;

        public IReadOnlyList<LessonDefinitionSO> Lessons => m_Lessons;

        private void Awake()
        {
            // En Play Mode se asegura el recorte; en Edit Mode no se agrega nada para no ensuciar la escena.
            if (Application.isPlaying && Container.GetComponent<RectMask2D>() == null)
            {
                Container.gameObject.AddComponent<RectMask2D>();
            }
            EnsureBuilt();
        }

        private void Update()
        {
            if (Mathf.Approximately(m_ScrollOffset, m_TargetScrollOffset)) return;

            if (m_ScrollSmoothTime <= 0f)
            {
                ApplyScroll(m_TargetScrollOffset);
                return;
            }

            float next = Mathf.SmoothDamp(m_ScrollOffset, m_TargetScrollOffset, ref m_ScrollVelocity, m_ScrollSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(next - m_TargetScrollOffset) < 0.5f)
            {
                next = m_TargetScrollOffset;
                m_ScrollVelocity = 0f;
            }
            ApplyScroll(next);
        }

        private RectTransform Container => m_Container != null ? m_Container : (RectTransform)transform;

        /// <summary>Lección del índice dado, o null.</summary>
        public LessonDefinitionSO GetLesson(int index)
        {
            return index >= 0 && index < m_Lessons.Count ? m_Lessons[index] : null;
        }

        /// <summary>Título mostrado en el ítem <paramref name="index"/> (vacío si no existe).</summary>
        public string GetItemTitle(int index)
        {
            EnsureBuilt();
            return index >= 0 && index < m_Items.Count ? m_Items[index].title.text : string.Empty;
        }

        /// <summary>true si el ítem queda completamente dentro del contenedor con el desplazamiento de destino.</summary>
        public bool IsItemVisible(int index)
        {
            EnsureBuilt();
            if (index < 0 || index >= m_Items.Count) return false;

            float view = ViewHeight();
            if (view <= 0f) return true; // sin tamaño conocido: no hay recorte
            float top = ItemTop(index) - m_TargetScrollOffset;
            return top >= -0.5f && top + m_ItemHeight <= view + 0.5f;
        }

        /// <summary>Lleva el desplazamiento aplicado directo al de destino (sin animación).</summary>
        public void SnapScroll()
        {
            EnsureBuilt();
            m_ScrollVelocity = 0f;
            ApplyScroll(m_TargetScrollOffset);
        }

        /// <summary>true si el ítem muestra la marca "Seleccionada".</summary>
        public bool IsItemMarked(int index)
        {
            EnsureBuilt();
            return index >= 0 && index < m_Items.Count && m_Items[index].selectedMark.gameObject.activeSelf;
        }

        /// <summary>Mueve el cursor <paramref name="delta"/> ítems (+ = bajar), frenando en los extremos. Devuelve true si se movió.</summary>
        public bool MoveCursor(int delta)
        {
            EnsureBuilt();
            int before = m_CursorIndex;
            SetCursor(m_CursorIndex + delta);
            return m_CursorIndex != before;
        }

        /// <summary>Ubica el cursor en <paramref name="index"/> (acotado a [0, Count-1]).</summary>
        public void SetCursor(int index)
        {
            EnsureBuilt();
            if (m_Items.Count == 0)
            {
                m_CursorIndex = 0;
                return;
            }

            m_CursorIndex = Mathf.Clamp(index, 0, m_Items.Count - 1);
            Refresh();
        }

        /// <summary>Muestra la marca "Seleccionada" solo en el ítem <paramref name="index"/> (-1 la quita de todos).</summary>
        public void MarkSelected(int index)
        {
            EnsureBuilt();
            m_SelectedIndex = index >= 0 && index < m_Items.Count ? index : -1;
            Refresh();
        }

        /// <summary>(Re)arma los ítems a partir de <c>m_Lessons</c>. Idempotente.</summary>
        public void Rebuild()
        {
            Transform container = Container;
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (m_Items[i].root != null) DestroyUnityObject(m_Items[i].root);
            }
            m_Items.Clear();

            // Limpia ítems huérfanos (p. ej. armados en Edit Mode y guardados con la escena) para no duplicarlos.
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Transform child = container.GetChild(i);
                if (child.name.StartsWith("LessonItem_")) DestroyUnityObject(child.gameObject);
            }

            for (int i = 0; i < m_Lessons.Count; i++)
            {
                m_Items.Add(CreateItem(container, i, m_Lessons[i]));
            }

            m_Built = true;
            m_CursorIndex = Mathf.Clamp(m_CursorIndex, 0, Mathf.Max(0, m_Items.Count - 1));
            if (m_SelectedIndex >= m_Items.Count) m_SelectedIndex = -1;
            Refresh();
            m_ScrollVelocity = 0f;
            ApplyScroll(m_TargetScrollOffset);
        }

        private void EnsureBuilt()
        {
            // Además del flag, se valida el estado real: tras un reload de dominio en el Editor los ítems (no serializados) se pierden.
            bool stale = m_Items.Count != m_Lessons.Count || (m_Items.Count > 0 && m_Items[0].root == null);
            if (!m_Built || stale) Rebuild();
        }

        private static void DestroyUnityObject(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private Item CreateItem(Transform parent, int index, LessonDefinitionSO lesson)
        {
            var item = new Item();

            item.root = new GameObject($"LessonItem_{index + 1:00}", typeof(RectTransform));
            item.root.layer = gameObject.layer;
            item.root.transform.SetParent(parent, false);

            var rootRect = (RectTransform)item.root.transform;
            rootRect.sizeDelta = new Vector2(0f, m_ItemHeight);
            var layout = item.root.AddComponent<LayoutElement>();
            layout.minHeight = m_ItemHeight;
            layout.preferredHeight = m_ItemHeight;

            item.background = item.root.AddComponent<Image>();
            item.background.color = m_NormalColor;
            item.background.raycastTarget = false;

            item.title = CreateText("Title", item.root.transform, m_FontSize, TextAlignmentOptions.MidlineLeft);
            item.title.text = lesson != null ? lesson.lessonTitle : "(Lección sin definir)";
            var titleRect = item.title.rectTransform;
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(40f, 0f);
            titleRect.offsetMax = new Vector2(-330f, 0f);

            item.selectedMark = CreateText("SelectedMark", item.root.transform, m_SelectedMarkFontSize, TextAlignmentOptions.MidlineRight);
            item.selectedMark.text = "Seleccionada";
            item.selectedMark.color = m_SelectedMarkColor;
            var markRect = item.selectedMark.rectTransform;
            markRect.anchorMin = new Vector2(1f, 0f);
            markRect.anchorMax = new Vector2(1f, 1f);
            markRect.pivot = new Vector2(1f, 0.5f);
            markRect.sizeDelta = new Vector2(300f, 0f);
            markRect.anchoredPosition = new Vector2(-30f, 0f);
            item.selectedMark.gameObject.SetActive(false);

            return item;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (m_Font != null) text.font = m_Font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = m_TextColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private void Refresh()
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                Item item = m_Items[i];
                item.background.color = i == m_CursorIndex ? m_CursorColor : m_NormalColor;
                item.selectedMark.gameObject.SetActive(i == m_SelectedIndex);
            }

            UpdateScrollTarget();
            // Fuera de Play Mode no corre Update: se aplica directo.
            if (!Application.isPlaying)
            {
                m_ScrollVelocity = 0f;
                ApplyScroll(m_TargetScrollOffset);
            }
        }

        // ------------------------------------------------------------------ desplazamiento

        private VerticalLayoutGroup Layout => Container.GetComponent<VerticalLayoutGroup>();

        private float Spacing
        {
            get
            {
                VerticalLayoutGroup layout = Layout;
                return layout != null ? layout.spacing : 0f;
            }
        }

        private float ItemTop(int index)
        {
            return index * (m_ItemHeight + Spacing);
        }

        private float ContentHeight()
        {
            int n = m_Items.Count;
            return n == 0 ? 0f : n * m_ItemHeight + (n - 1) * Spacing;
        }

        private float ViewHeight()
        {
            CacheBasePadding();
            VerticalLayoutGroup layout = Layout;
            float padding = layout != null ? m_BasePaddingTop + layout.padding.bottom : 0f;
            return Container.rect.height - padding;
        }

        private void CacheBasePadding()
        {
            if (m_BasePaddingCached) return;
            VerticalLayoutGroup layout = Layout;
            m_BasePaddingTop = layout != null ? layout.padding.top : 0;
            m_BasePaddingCached = true;
        }

        // Mínimo desplazamiento que deja la fila del cursor entera a la vista, acotado al contenido.
        private void UpdateScrollTarget()
        {
            float view = ViewHeight();
            if (view <= 0f || m_Items.Count == 0)
            {
                m_TargetScrollOffset = 0f;
                return;
            }

            float top = ItemTop(m_CursorIndex);
            float bottom = top + m_ItemHeight;
            float target = m_TargetScrollOffset;
            if (top < target) target = top;
            else if (bottom > target + view) target = bottom - view;

            m_TargetScrollOffset = Mathf.Clamp(target, 0f, Mathf.Max(0f, ContentHeight() - view));
        }

        // El VerticalLayoutGroup acomoda las filas; el desplazamiento se aplica corriendo su padding superior.
        private void ApplyScroll(float offset)
        {
            m_ScrollOffset = offset;
            VerticalLayoutGroup layout = Layout;
            if (layout == null) return;

            CacheBasePadding();
            int padTop = m_BasePaddingTop - Mathf.RoundToInt(offset);
            if (layout.padding.top == padTop) return;
            layout.padding.top = padTop;
            LayoutRebuilder.MarkLayoutForRebuild(Container);
        }
    }
}
