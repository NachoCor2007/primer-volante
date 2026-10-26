using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.UI;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente que agrega el aviso de infracción (US24) a la pantalla del GPS de Car09:
    /// un overlay rojo <c>Infraction_Alert</c> dentro de <c>Header_ManeuverBar</c> (ícono + título corto + barra de
    /// tiempo) y el <see cref="ConsoleAlertDisplay"/> en <c>GPS_Display_Canvas</c> que lo maneja.
    /// <see cref="GPSNavigationSceneSetup.ConfigureGPSInVehicle"/> la llama al final, porque reconstruye el canvas
    /// del GPS desde cero y, sin esto, un rebuild borraría el aviso.
    /// </summary>
    public static class ConsoleAlertSetup
    {
        public const string CANVAS_NAME = "GPS_Display_Canvas";
        public const string HEADER_PATH = "Screen_Panel/Header_ManeuverBar";
        public const string ALERT_NAME = "Infraction_Alert";
        public const string ICON_NAME = "Infraction_Alert_Icon";
        public const string TEXT_NAME = "Infraction_Alert_Text";
        public const string PROGRESS_NAME = "Infraction_Alert_Progress";
        public const string FILL_NAME = "Infraction_Alert_Progress_Fill";

        public static readonly Color ALERT_COLOR = new Color(0.75f, 0.08f, 0.08f, 1f);
        public static readonly Color PROGRESS_TRACK_COLOR = new Color(0f, 0f, 0f, 0.35f);
        public static readonly Color PROGRESS_FILL_COLOR = new Color(1f, 1f, 1f, 0.9f);
        public const float PROGRESS_HEIGHT = 4f;
        public const float ICON_SIZE = 36f;
        public const float ICON_X = 30f;
        public const float DISPLAY_DURATION = 3f;

        [MenuItem("Tools/Primer Volante/GPS/Add Infraction Alert to Car09 Prefab")]
        public static void AddInfractionAlertToPrefab()
        {
            string prefabPath = GPSNavigationSceneSetup.PREFAB_PATH;
            if (!File.Exists(prefabPath))
            {
                Debug.LogWarning($"[ConsoleAlertSetup] No se encontró el prefab en {prefabPath}");
                return;
            }

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GPSNavigationSceneSetup.FONT_PATH);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null) return;

            try
            {
                if (!EnsureConsoleAlert(prefabRoot, fontAsset)) return;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Debug.Log($"[ConsoleAlertSetup] ✅ Prefab {Path.GetFileName(prefabPath)} actualizado con el aviso de infracción del GPS.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        /// <summary>
        /// Crea o actualiza el aviso bajo <c>GPS_Display_Canvas/Screen_Panel/Header_ManeuverBar</c> y cablea el
        /// <see cref="ConsoleAlertDisplay"/>. Idempotente: correrlo dos veces no duplica objetos ni componentes.
        /// Devuelve false (con un warning) si el auto no tiene GPS.
        /// </summary>
        public static bool EnsureConsoleAlert(GameObject carRoot, TMP_FontAsset font)
        {
            Transform canvas = carRoot != null ? carRoot.transform.Find(CANVAS_NAME) : null;
            Transform header = canvas != null ? canvas.Find(HEADER_PATH) : null;
            if (header == null)
            {
                Debug.LogWarning($"[ConsoleAlertSetup] ⚠️ {(carRoot != null ? carRoot.name : "(null)")} no tiene {CANVAS_NAME}/{HEADER_PATH}: no hay GPS donde agregar el aviso.");
                return false;
            }

            Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DashboardClusterSetup.ICON_INFRACTION_PATH);
            if (iconSprite == null)
                Debug.LogWarning($"[ConsoleAlertSetup] ⚠️ No se encontró el sprite {DashboardClusterSetup.ICON_INFRACTION_PATH}: el ícono quedará sin imagen.");

            // Overlay: stretch completo, opaco, encima de Maneuver_Icon y Maneuver_Text (último hermano).
            RectTransform alert = GetOrCreateRect(header, ALERT_NAME);
            Stretch(alert);
            Image alertImage = GetOrAdd<Image>(alert.gameObject);
            alertImage.color = ALERT_COLOR;
            alertImage.raycastTarget = false;
            alert.SetAsLastSibling();

            // Ícono de advertencia a la izquierda, donde está Maneuver_Icon.
            RectTransform icon = GetOrCreateRect(alert, ICON_NAME);
            icon.anchorMin = new Vector2(0f, 0.5f);
            icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = new Vector2(ICON_X, 0f);
            icon.sizeDelta = new Vector2(ICON_SIZE, ICON_SIZE);
            Image iconImage = GetOrAdd<Image>(icon.gameObject);
            iconImage.sprite = iconSprite;
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            // Título corto: mismos offsets que Maneuver_Text; se achica antes de cortarse.
            RectTransform text = GetOrCreateRect(alert, TEXT_NAME);
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.pivot = new Vector2(0.5f, 0.5f);
            text.offsetMin = new Vector2(58f, 0f);
            text.offsetMax = new Vector2(-15f, 0f);
            TextMeshProUGUI title = GetOrAdd<TextMeshProUGUI>(text.gameObject);
            if (font != null) title.font = font;
            title.text = "Contramano";
            title.fontSize = 20f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Left;
            title.color = Color.white;
            title.enableAutoSizing = true;
            title.fontSizeMin = 14f;
            title.fontSizeMax = 20f;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.raycastTarget = false;

            // Barra de tiempo: pista de 4 px pegada al borde inferior; el relleno se acorta con anchorMax.x.
            RectTransform track = GetOrCreateRect(alert, PROGRESS_NAME);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.anchoredPosition = Vector2.zero;
            track.sizeDelta = new Vector2(0f, PROGRESS_HEIGHT);
            Image trackImage = GetOrAdd<Image>(track.gameObject);
            trackImage.color = PROGRESS_TRACK_COLOR;
            trackImage.raycastTarget = false;

            RectTransform fill = GetOrCreateRect(track, FILL_NAME);
            Stretch(fill);
            Image fillImage = GetOrAdd<Image>(fill.gameObject);
            fillImage.color = PROGRESS_FILL_COLOR;
            fillImage.raycastTarget = false;

            // Orden estable de los hijos: ícono, texto, barra.
            icon.SetSiblingIndex(0);
            text.SetSiblingIndex(1);
            track.SetSiblingIndex(2);

            alert.gameObject.SetActive(false);

            ConsoleAlertDisplay display = GetOrAdd<ConsoleAlertDisplay>(canvas.gameObject);
            var so = new SerializedObject(display);
            so.Update();
            so.FindProperty("m_AlertRoot").objectReferenceValue = alert.gameObject;
            so.FindProperty("m_TitleText").objectReferenceValue = title;
            so.FindProperty("m_ProgressFill").objectReferenceValue = fill;
            so.FindProperty("m_DisplayDuration").floatValue = DISPLAY_DURATION;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(display);
            return true;
        }

        private static RectTransform GetOrCreateRect(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return (RectTransform)existing;

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
