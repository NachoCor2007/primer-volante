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
    /// Si la lista no entra en <c>m_MaxVisibleItems</c> filas, desplaza la ventana para mantener el cursor visible.
    /// <see cref="MarkSelected"/> muestra la marca "Seleccionada" en un ítem.
    /// </summary>
    public class LessonListPanel : MonoBehaviour
    {
        [Header("Contenido")]
        [Tooltip("Lecciones a listar, en orden (LessonData_01..04).")]
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

        [Tooltip("Máximo de filas visibles a la vez. Si hay más lecciones, la ventana sigue al cursor.")]
        [SerializeField] private int m_MaxVisibleItems = 6;

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
        private int m_FirstVisible;
        private bool m_Built;

        /// <summary>Cantidad de ítems armados.</summary>
        public int Count { get { EnsureBuilt(); return m_Items.Count; } }

        /// <summary>Índice del cursor.</summary>
        public int CursorIndex { get { EnsureBuilt(); return m_CursorIndex; } }

        /// <summary>Índice marcado como "Seleccionada", o -1 si ninguno.</summary>
        public int SelectedIndex => m_SelectedIndex;

        /// <summary>Índice del primer ítem visible de la ventana.</summary>
        public int FirstVisibleIndex { get { EnsureBuilt(); return m_FirstVisible; } }

        public IReadOnlyList<LessonDefinitionSO> Lessons => m_Lessons;

        private void Awake()
        {
            EnsureBuilt();
        }

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

        /// <summary>true si el ítem está dentro de la ventana visible.</summary>
        public bool IsItemVisible(int index)
        {
            EnsureBuilt();
            return index >= 0 && index < m_Items.Count && m_Items[index].root.activeSelf;
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
            Transform container = m_Container != null ? m_Container : transform;
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
            int visible = Mathf.Max(1, m_MaxVisibleItems);
            if (m_CursorIndex < m_FirstVisible) m_FirstVisible = m_CursorIndex;
            else if (m_CursorIndex >= m_FirstVisible + visible) m_FirstVisible = m_CursorIndex - visible + 1;
            m_FirstVisible = Mathf.Clamp(m_FirstVisible, 0, Mathf.Max(0, m_Items.Count - visible));

            for (int i = 0; i < m_Items.Count; i++)
            {
                Item item = m_Items[i];
                item.root.SetActive(i >= m_FirstVisible && i < m_FirstVisible + visible);
                item.background.color = i == m_CursorIndex ? m_CursorColor : m_NormalColor;
                item.selectedMark.gameObject.SetActive(i == m_SelectedIndex);
            }
        }
    }
}
