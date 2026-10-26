using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.UI;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación automatizada en Editor del aviso de infracción de la pantalla del GPS (US24): jerarquía y valores
    /// de <c>Infraction_Alert</c> en Car09.prefab, cableado de <see cref="ConsoleAlertDisplay"/>, idempotencia de
    /// <see cref="ConsoleAlertSetup.EnsureConsoleAlert"/>, que un rebuild del GPS conserve el aviso y que ninguna escena
    /// que use Car09 tenga overrides sobre el aviso. No modifica ningún asset: trabaja sobre copias sin guardar.
    /// </summary>
    public static class ConsoleAlertVerification
    {
        private static readonly string[] s_ScenePaths =
        {
            "Assets/Scenes/car09/CityMerged3.unity",
            "Assets/Scenes/car09/CityWithInfractions.unity",
            "Assets/Scenes/car09/CityWithWrongWay.unity",
            "Assets/Scenes/car09/Lesson_01_BasicDriving2.unity",
            "Assets/Scenes/TestingScenes/GPSNavigationDemo.unity",
            "Assets/Scenes/TestingScenes/GPSWrongWayDemo.unity",
        };

        [MenuItem("Tools/Primer Volante/GPS/Verify Infraction Alert")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL AVISO DE INFRACCIÓN EN EL GPS");
            Debug.Log("=========================================================");

            int total = 0;
            int passed = 0;

            string prefabPath = GPSNavigationSceneSetup.PREFAB_PATH;
            Check(File.Exists(prefabPath), "Car09.prefab debe existir", ref total, ref passed);
            if (File.Exists(prefabPath))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    VerifyAlert(root, "Car09.prefab", ref total, ref passed);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                VerifyIdempotent(prefabPath, ref total, ref passed);

                VerifyRebuildKeepsAlert(prefabPath, ref total, ref passed);
                VerifyScenesHaveNoOverrides(prefabPath, ref total, ref passed);
            }

            Debug.Log("=========================================================");
            if (passed == total)
                Debug.Log($"<color=green>✅ VERIFICACIÓN DEL AVISO DE INFRACCIÓN EN EL GPS COMPLETADA CON ÉXITO: {passed}/{total} pruebas pasadas.</color>");
            else
                Debug.LogError($"<color=red>❌ VERIFICACIÓN DEL AVISO DE INFRACCIÓN EN EL GPS FALLÓ: {passed}/{total} pruebas pasadas.</color>");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------------------------------------------
        // Jerarquía, valores y cableado
        // ---------------------------------------------------------------------------------------------------

        private static void VerifyAlert(GameObject root, string label, ref int total, ref int passed)
        {
            Transform canvas = root.transform.Find(ConsoleAlertSetup.CANVAS_NAME);
            Transform header = canvas != null ? canvas.Find(ConsoleAlertSetup.HEADER_PATH) : null;
            Check(header != null, $"{label}: debe existir {ConsoleAlertSetup.CANVAS_NAME}/{ConsoleAlertSetup.HEADER_PATH}", ref total, ref passed);
            if (header == null) return;

            Transform alertT = header.Find(ConsoleAlertSetup.ALERT_NAME);
            Check(alertT != null, $"{label}: {ConsoleAlertSetup.ALERT_NAME} debe existir bajo Header_ManeuverBar", ref total, ref passed);
            if (alertT == null) return;

            var alert = (RectTransform)alertT;
            Check(alert.GetSiblingIndex() == header.childCount - 1, $"{label}: {ConsoleAlertSetup.ALERT_NAME} debe ser el último hermano (se dibuja encima de la maniobra)", ref total, ref passed);
            Check(!alert.gameObject.activeSelf, $"{label}: {ConsoleAlertSetup.ALERT_NAME} debe arrancar inactivo", ref total, ref passed);

            Transform mIcon = header.Find("Maneuver_Icon");
            Transform mText = header.Find("Maneuver_Text");
            Check(mIcon != null && mText != null && mIcon.GetSiblingIndex() < alert.GetSiblingIndex() && mText.GetSiblingIndex() < alert.GetSiblingIndex(),
                $"{label}: Maneuver_Icon y Maneuver_Text deben seguir existiendo, debajo del aviso", ref total, ref passed);

            Check(alert.anchorMin == Vector2.zero && alert.anchorMax == Vector2.one && alert.offsetMin == Vector2.zero && alert.offsetMax == Vector2.zero,
                $"{label}: el layout del aviso debe ser stretch completo (anchors 0..1, offsets 0)", ref total, ref passed);
            Image alertImage = alert.GetComponent<Image>();
            Check(alertImage != null && alertImage.color == ConsoleAlertSetup.ALERT_COLOR,
                $"{label}: el color del aviso debe ser (0.75, 0.08, 0.08, 1) y opaco", ref total, ref passed);

            // Ícono
            var iconT = alert.Find(ConsoleAlertSetup.ICON_NAME) as RectTransform;
            Check(iconT != null, $"{label}: debe existir {ConsoleAlertSetup.ICON_NAME}", ref total, ref passed);
            if (iconT != null)
            {
                Image icon = iconT.GetComponent<Image>();
                string spritePath = icon != null && icon.sprite != null ? AssetDatabase.GetAssetPath(icon.sprite) : "";
                Check(spritePath == DashboardClusterSetup.ICON_INFRACTION_PATH, $"{label}: el ícono debe usar icon_infraction_warning (actual: '{spritePath}')", ref total, ref passed);
                Check(icon != null && icon.color == Color.white && icon.preserveAspect, $"{label}: el ícono debe ser blanco y tener preserveAspect", ref total, ref passed);
                Check(iconT.sizeDelta == new Vector2(36f, 36f) && iconT.anchorMin == new Vector2(0f, 0.5f) && iconT.anchorMax == new Vector2(0f, 0.5f)
                      && iconT.anchoredPosition == new Vector2(30f, 0f),
                    $"{label}: el ícono debe medir 36x36 y estar anclado a la izquierda en x = 30", ref total, ref passed);
            }

            // Texto
            var textT = alert.Find(ConsoleAlertSetup.TEXT_NAME) as RectTransform;
            Check(textT != null, $"{label}: debe existir {ConsoleAlertSetup.TEXT_NAME}", ref total, ref passed);
            TextMeshProUGUI title = textT != null ? textT.GetComponent<TextMeshProUGUI>() : null;
            if (title != null)
            {
                string fontPath = title.font != null ? AssetDatabase.GetAssetPath(title.font) : "";
                Check(fontPath == GPSNavigationSceneSetup.FONT_PATH, $"{label}: el texto debe usar la fuente de FONT_PATH (actual: '{fontPath}')", ref total, ref passed);
                Check(Mathf.Approximately(title.fontSize, 20f) && title.fontStyle == FontStyles.Bold && title.color == Color.white
                      && title.alignment == TextAlignmentOptions.Left,
                    $"{label}: el texto debe ser blanco, Bold, tamaño 20, alineado a la izquierda", ref total, ref passed);
                Check(title.enableAutoSizing && Mathf.Approximately(title.fontSizeMin, 14f), $"{label}: el texto debe achicarse (autosize, mínimo 14)", ref total, ref passed);
                Check(textT.offsetMin == new Vector2(58f, 0f) && textT.offsetMax == new Vector2(-15f, 0f) && textT.anchorMin == Vector2.zero && textT.anchorMax == Vector2.one,
                    $"{label}: el texto debe tener los offsets de Maneuver_Text (izquierda 58, derecha -15)", ref total, ref passed);
            }
            else
            {
                Check(false, $"{label}: {ConsoleAlertSetup.TEXT_NAME} debe tener TextMeshProUGUI", ref total, ref passed);
            }

            // Barra de tiempo
            var trackT = alert.Find(ConsoleAlertSetup.PROGRESS_NAME) as RectTransform;
            Check(trackT != null, $"{label}: debe existir {ConsoleAlertSetup.PROGRESS_NAME}", ref total, ref passed);
            RectTransform fillT = null;
            if (trackT != null)
            {
                Check(Mathf.Approximately(trackT.sizeDelta.y, 4f) && trackT.anchorMin == Vector2.zero && trackT.anchorMax == new Vector2(1f, 0f)
                      && trackT.anchoredPosition == Vector2.zero && trackT.sizeDelta.x == 0f,
                    $"{label}: la pista debe medir 4 px de alto, pegada al borde inferior", ref total, ref passed);
                Image track = trackT.GetComponent<Image>();
                Check(track != null && track.color == ConsoleAlertSetup.PROGRESS_TRACK_COLOR, $"{label}: la pista debe ser (0, 0, 0, 0.35)", ref total, ref passed);

                fillT = trackT.Find(ConsoleAlertSetup.FILL_NAME) as RectTransform;
                Check(fillT != null, $"{label}: debe existir {ConsoleAlertSetup.FILL_NAME} dentro de la pista", ref total, ref passed);
                if (fillT != null)
                {
                    Check(fillT.anchorMin == Vector2.zero && fillT.anchorMax == Vector2.one && fillT.offsetMin == Vector2.zero && fillT.offsetMax == Vector2.zero,
                        $"{label}: el relleno debe tener anchors (0,0)-(1,1) y offsets 0", ref total, ref passed);
                    Image fill = fillT.GetComponent<Image>();
                    Check(fill != null && fill.color == ConsoleAlertSetup.PROGRESS_FILL_COLOR, $"{label}: el relleno debe ser (1, 1, 1, 0.9)", ref total, ref passed);
                }
            }

            // Ningún elemento es raycastTarget
            int raycastTargets = 0;
            foreach (Graphic g in alert.GetComponentsInChildren<Graphic>(true))
            {
                if (g.raycastTarget) raycastTargets++;
            }
            Check(raycastTargets == 0, $"{label}: ningún elemento del aviso debe ser raycastTarget ({raycastTargets} lo son)", ref total, ref passed);

            // Componente
            ConsoleAlertDisplay[] displays = canvas.GetComponents<ConsoleAlertDisplay>();
            Check(displays.Length == 1, $"{label}: GPS_Display_Canvas debe tener exactamente un ConsoleAlertDisplay (hay {displays.Length})", ref total, ref passed);
            if (displays.Length == 0) return;

            var so = new SerializedObject(displays[0]);
            Check(so.FindProperty("m_AlertRoot").objectReferenceValue == alert.gameObject, $"{label}: m_AlertRoot debe apuntar a {ConsoleAlertSetup.ALERT_NAME}", ref total, ref passed);
            Check(so.FindProperty("m_TitleText").objectReferenceValue == title && title != null, $"{label}: m_TitleText debe apuntar al texto del aviso", ref total, ref passed);
            Check(so.FindProperty("m_ProgressFill").objectReferenceValue == fillT && fillT != null, $"{label}: m_ProgressFill debe apuntar al relleno de la barra", ref total, ref passed);
            Check(Mathf.Approximately(so.FindProperty("m_DisplayDuration").floatValue, 3f), $"{label}: m_DisplayDuration debe ser 3 s", ref total, ref passed);
        }

        // ---------------------------------------------------------------------------------------------------
        // Idempotencia
        // ---------------------------------------------------------------------------------------------------

        private static void VerifyIdempotent(string prefabPath, ref int total, ref int passed)
        {
            // Copia en memoria del prefab (no se guarda): se corre Ensure dos veces y se compara el conteo.
            GameObject copy = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GPSNavigationSceneSetup.FONT_PATH);
                int transformsBefore = copy.GetComponentsInChildren<Transform>(true).Length;
                int componentsBefore = CountComponents(copy);

                bool first = ConsoleAlertSetup.EnsureConsoleAlert(copy, font);
                bool second = ConsoleAlertSetup.EnsureConsoleAlert(copy, font);

                Check(first && second, "Idempotencia: EnsureConsoleAlert debe devolver true las dos veces", ref total, ref passed);
                Check(copy.GetComponentsInChildren<Transform>(true).Length == transformsBefore,
                    "Idempotencia: correr EnsureConsoleAlert dos veces no debe crear objetos nuevos", ref total, ref passed);
                Check(CountComponents(copy) == componentsBefore,
                    "Idempotencia: correr EnsureConsoleAlert dos veces no debe crear componentes nuevos", ref total, ref passed);
                Check(CountNamed(copy, ConsoleAlertSetup.ALERT_NAME) == 1, "Idempotencia: debe haber un único Infraction_Alert", ref total, ref passed);
                Check(copy.GetComponentsInChildren<ConsoleAlertDisplay>(true).Length == 1, "Idempotencia: debe haber un único ConsoleAlertDisplay", ref total, ref passed);

                // Sobre un auto sin GPS debe salir con warning y false, sin lanzar.
                var noGps = new GameObject("NoGPS");
                bool noGpsResult = ConsoleAlertSetup.EnsureConsoleAlert(noGps, font);
                Object.DestroyImmediate(noGps);
                Check(!noGpsResult, "Idempotencia: sin GPS, EnsureConsoleAlert debe devolver false (con LogWarning)", ref total, ref passed);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(copy);
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // ConfigureGPSInVehicle conserva el aviso
        // ---------------------------------------------------------------------------------------------------

        private static void VerifyRebuildKeepsAlert(string prefabPath, ref int total, ref int passed)
        {
            // Se reconstruye el GPS sobre el contenido del prefab SIN guardarlo: el asset en disco no cambia.
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(GPSNavigationSceneSetup.RT_PATH);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(GPSNavigationSceneSetup.MAT_PATH);
                var arrow = AssetDatabase.LoadAssetAtPath<Sprite>(GPSNavigationSceneSetup.ARROW_ICON_PATH);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GPSNavigationSceneSetup.FONT_PATH);

                GPSNavigationSceneSetup.ConfigureGPSInVehicle(root, rt, mat, arrow, font,
                    LayerMask.NameToLayer(GPSNavigationSceneSetup.ROUTE_LAYER_NAME),
                    LayerMask.NameToLayer(GPSNavigationSceneSetup.VEHICLE_LAYER_NAME),
                    LayerMask.NameToLayer(GPSNavigationSceneSetup.NPC_LAYER_NAME),
                    LayerMask.NameToLayer(GPSNavigationSceneSetup.ROADS_LAYER_NAME),
                    LayerMask.NameToLayer(GPSNavigationSceneSetup.BUILDING_LAYER_NAME));

                VerifyAlert(root, "Rebuild del GPS (ConfigureGPSInVehicle sin guardar)", ref total, ref passed);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Escenas sin overrides sobre el aviso
        // ---------------------------------------------------------------------------------------------------

        private static void VerifyScenesHaveNoOverrides(string prefabPath, ref int total, ref int passed)
        {
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);

            // fileIDs del subárbol del aviso y del ConsoleAlertDisplay dentro del prefab.
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Transform canvas = prefabAsset != null ? prefabAsset.transform.Find(ConsoleAlertSetup.CANVAS_NAME) : null;
            Transform alert = canvas != null ? canvas.Find(ConsoleAlertSetup.HEADER_PATH + "/" + ConsoleAlertSetup.ALERT_NAME) : null;
            Check(alert != null, "Escenas: Car09.prefab debe tener el aviso para poder buscar overrides", ref total, ref passed);
            if (alert == null) return;

            var ids = new Dictionary<long, string>();
            var objects = new List<Object>();
            foreach (Transform t in alert.GetComponentsInChildren<Transform>(true))
            {
                objects.Add(t.gameObject);
                objects.AddRange(t.GetComponents<Component>());
            }
            objects.Add(canvas.GetComponent<ConsoleAlertDisplay>());

            foreach (Object obj in objects)
            {
                if (obj == null) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string _, out long localId))
                    ids[localId] = obj.name + " (" + obj.GetType().Name + ")";
            }
            Check(ids.Count > 0, $"Escenas: se obtuvieron {ids.Count} fileIDs del aviso en el prefab", ref total, ref passed);

            foreach (string scenePath in s_ScenePaths)
            {
                if (!File.Exists(scenePath))
                {
                    Check(false, $"Escenas: {scenePath} debe existir", ref total, ref passed);
                    continue;
                }

                string yaml = File.ReadAllText(scenePath);
                bool usesCar09 = yaml.Contains("guid: " + prefabGuid);
                Check(usesCar09, $"Escenas: {Path.GetFileName(scenePath)} debe instanciar Car09.prefab", ref total, ref passed);

                // Cualquier referencia {fileID: <id del aviso>, guid: <Car09>} en la escena es un override sobre el
                // aviso (modificación, objeto/componente quitado o agregado). Debe haber cero.
                var offenders = new List<string>();
                foreach (KeyValuePair<long, string> pair in ids)
                {
                    if (Regex.IsMatch(yaml, @"fileID:\s*" + pair.Key + @",\s*guid:\s*" + prefabGuid))
                        offenders.Add(pair.Value);
                }

                Check(offenders.Count == 0,
                    $"Escenas: {Path.GetFileName(scenePath)} no debe tener overrides sobre el aviso del GPS" +
                    (offenders.Count > 0 ? $" (encontrados: {string.Join(", ", offenders)})" : ""), ref total, ref passed);
            }
        }

        private static int CountComponents(GameObject root)
        {
            int count = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                count += t.GetComponents<Component>().Length;
            return count;
        }

        private static int CountNamed(GameObject root, string name)
        {
            int count = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) count++;
            }
            return count;
        }

        private static void Check(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"✅ {message}");
            }
            else
            {
                Debug.LogError($"❌ {message}");
            }
        }
    }
}
