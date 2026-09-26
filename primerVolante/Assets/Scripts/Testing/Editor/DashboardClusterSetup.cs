using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para construir y configurar el tablero diegético (World Space Canvas)
    /// en el vehículo Car06 y en la escena DashboardTestingScene.
    /// </summary>
    public static class DashboardClusterSetup
    {
        private const string SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/DashboardTestingScene.unity";
        private const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car06.prefab";
        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Tools/Primer Volante/Setup Dashboard in Car06 Prefab and Scene")]
        public static void SetupDashboardAll()
        {
            SetupDashboardInPrefab();
            SetupDashboardInScene();
        }

        [MenuItem("Tools/Primer Volante/Setup Dashboard in Car06 Prefab")]
        public static void SetupDashboardInPrefab()
        {
            Debug.Log($"[DashboardClusterSetup] 📦 Configurando tablero en ASSET Prefab: {PREFAB_PATH}...");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabRoot == null)
            {
                Debug.LogError($"[DashboardClusterSetup] ❌ No se pudo cargar el Prefab en {PREFAB_PATH}");
                return;
            }

            try
            {
                ConfigureDashboardCluster(prefabRoot);
                VehicleLightingSetup.ConfigureVehicleLighting(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
                Debug.Log("[DashboardClusterSetup] ✅ ¡Car06.prefab actualizado y guardado con el tablero diegético e iluminación!");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Tools/Primer Volante/Setup Dashboard in Car06 Scene")]
        public static void SetupDashboardInScene()
        {
            Debug.Log($"[DashboardClusterSetup] 🎬 Configurando escena: {SCENE_PATH}...");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }

            // Buscar Car06 en la escena
            GameObject car = GameObject.Find("Car06");
            if (car == null)
            {
                var vehicle = Object.FindAnyObjectByType<VehicleController>();
                if (vehicle != null) car = vehicle.gameObject;
            }

            if (car == null)
            {
                Debug.LogError("[DashboardClusterSetup] ❌ No se encontró Car06 ni VehicleController en la escena.");
                return;
            }

            ConfigureDashboardCluster(car);
            VehicleLightingSetup.ConfigureVehicleLighting(car);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DashboardClusterSetup] ✅ ¡Escena {SCENE_PATH} guardada exitosamente con el cluster e iluminación configurados!");
        }

        public static GameObject ConfigureDashboardCluster(GameObject carRoot)
        {
            VehicleController vehicleController = carRoot.GetComponent<VehicleController>();
            if (vehicleController == null)
            {
                vehicleController = Object.FindAnyObjectByType<VehicleController>();
            }

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            // 1. Buscar o crear Dashboard_Cluster_Canvas
            Transform existingCanvas = carRoot.transform.Find("Dashboard_Cluster_Canvas");
            GameObject canvasObj;
            if (existingCanvas != null)
            {
                canvasObj = existingCanvas.gameObject;
                // Limpiar hijos existentes si ya existían para regenerar limpiamente
                for (int i = canvasObj.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(canvasObj.transform.GetChild(i).gameObject);
                }
            }
            else
            {
                canvasObj = new GameObject("Dashboard_Cluster_Canvas");
                canvasObj.transform.SetParent(carRoot.transform, false);
            }

            // 2. Setup RectTransform y Canvas
            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            if (canvasRect == null) canvasRect = canvasObj.AddComponent<RectTransform>();

            canvasRect.localPosition = new Vector3(-0.30f, 0.97f, 0.295f);
            canvasRect.localRotation = Quaternion.Euler(0f, 0f, 0f);
            canvasRect.localScale = new Vector3(0.0005f, 0.0005f, 0.0005f);
            canvasRect.sizeDelta = new Vector2(500f, 200f);

            Canvas canvas = canvasObj.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // 3. Panel de fondo (Image)
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

            // 4. Guiños / Flechas
            // Izquierda
            GameObject turnLeftObj = new GameObject("TurnSignal_Left");
            turnLeftObj.transform.SetParent(canvasObj.transform, false);
            RectTransform tlRect = turnLeftObj.AddComponent<RectTransform>();
            tlRect.anchorMin = new Vector2(0f, 1f);
            tlRect.anchorMax = new Vector2(0f, 1f);
            tlRect.anchoredPosition = new Vector2(45f, -35f);
            tlRect.sizeDelta = new Vector2(50f, 40f);
            TextMeshProUGUI tlText = turnLeftObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tlText.font = fontAsset;
            tlText.text = "<";
            tlText.fontSize = 32f;
            tlText.fontStyle = FontStyles.Bold;
            tlText.alignment = TextAlignmentOptions.Center;
            tlText.color = new Color(0.15f, 0.15f, 0.15f, 0.25f);

            // Derecha
            GameObject turnRightObj = new GameObject("TurnSignal_Right");
            turnRightObj.transform.SetParent(canvasObj.transform, false);
            RectTransform trRect = turnRightObj.AddComponent<RectTransform>();
            trRect.anchorMin = new Vector2(1f, 1f);
            trRect.anchorMax = new Vector2(1f, 1f);
            trRect.anchoredPosition = new Vector2(-45f, -35f);
            trRect.sizeDelta = new Vector2(50f, 40f);
            TextMeshProUGUI trText = turnRightObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) trText.font = fontAsset;
            trText.text = ">";
            trText.fontSize = 32f;
            trText.fontStyle = FontStyles.Bold;
            trText.alignment = TextAlignmentOptions.Center;
            trText.color = new Color(0.15f, 0.15f, 0.15f, 0.25f);

            // 5. Velocímetro
            // Valor numérico
            GameObject speedObj = new GameObject("Speed_Value");
            speedObj.transform.SetParent(canvasObj.transform, false);
            RectTransform speedRect = speedObj.AddComponent<RectTransform>();
            speedRect.anchorMin = new Vector2(0.5f, 0.5f);
            speedRect.anchorMax = new Vector2(0.5f, 0.5f);
            speedRect.anchoredPosition = new Vector2(0f, 18f);
            speedRect.sizeDelta = new Vector2(220f, 90f);
            TextMeshProUGUI speedText = speedObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) speedText.font = fontAsset;
            speedText.text = "0";
            speedText.fontSize = 72f;
            speedText.fontStyle = FontStyles.Bold;
            speedText.alignment = TextAlignmentOptions.Center;
            speedText.color = Color.white;

            // Etiqueta KM/H
            GameObject unitObj = new GameObject("Speed_Unit");
            unitObj.transform.SetParent(canvasObj.transform, false);
            RectTransform unitRect = unitObj.AddComponent<RectTransform>();
            unitRect.anchorMin = new Vector2(0.5f, 0.5f);
            unitRect.anchorMax = new Vector2(0.5f, 0.5f);
            unitRect.anchoredPosition = new Vector2(0f, -30f);
            unitRect.sizeDelta = new Vector2(120f, 25f);
            TextMeshProUGUI unitText = unitObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) unitText.font = fontAsset;
            unitText.text = "KM/H";
            unitText.fontSize = 20f;
            unitText.fontStyle = FontStyles.Bold;
            unitText.alignment = TextAlignmentOptions.Center;
            unitText.color = new Color(0.65f, 0.65f, 0.65f, 0.85f);

            // 6. Testigo de Freno de Mano (Izquierda inferior)
            GameObject handbrakeObj = new GameObject("Indicator_Handbrake");
            handbrakeObj.transform.SetParent(canvasObj.transform, false);
            RectTransform hbRect = handbrakeObj.AddComponent<RectTransform>();
            hbRect.anchorMin = new Vector2(0.5f, 0f);
            hbRect.anchorMax = new Vector2(0.5f, 0f);
            hbRect.anchoredPosition = new Vector2(-155f, 30f);
            hbRect.sizeDelta = new Vector2(65f, 35f);
            TextMeshProUGUI hbText = handbrakeObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) hbText.font = fontAsset;
            hbText.text = "(P)";
            hbText.fontSize = 26f;
            hbText.fontStyle = FontStyles.Bold;
            hbText.alignment = TextAlignmentOptions.Center;
            hbText.color = new Color(1f, 0.15f, 0.15f, 1f); // Activo por defecto

            // 7. Testigos de Luces Frontales: Bajas y Altas (Derecha inferior)
            // 7.A. Luces Bajas (#1AF2A0)
            GameObject headlightsObj = new GameObject("Indicator_Headlights");
            headlightsObj.transform.SetParent(canvasObj.transform, false);
            RectTransform hlRect = headlightsObj.AddComponent<RectTransform>();
            hlRect.anchorMin = new Vector2(0.5f, 0f);
            hlRect.anchorMax = new Vector2(0.5f, 0f);
            hlRect.anchoredPosition = new Vector2(130f, 30f);
            hlRect.sizeDelta = new Vector2(50f, 35f);
            TextMeshProUGUI hlText = headlightsObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) hlText.font = fontAsset;
            hlText.text = "[ ≡ ]";
            hlText.fontSize = 22f;
            hlText.fontStyle = FontStyles.Bold;
            hlText.alignment = TextAlignmentOptions.Center;
            hlText.color = new Color(0.102f, 0.949f, 0.627f, 0.2f); // Inactivo por defecto

            // 7.B. Luces Altas (#1A7BFF con símbolo [ D ])
            GameObject highBeamsObj = new GameObject("Indicator_HighBeams");
            highBeamsObj.transform.SetParent(canvasObj.transform, false);
            RectTransform hbAltasRect = highBeamsObj.AddComponent<RectTransform>();
            hbAltasRect.anchorMin = new Vector2(0.5f, 0f);
            hbAltasRect.anchorMax = new Vector2(0.5f, 0f);
            hbAltasRect.anchoredPosition = new Vector2(185f, 30f);
            hbAltasRect.sizeDelta = new Vector2(50f, 35f);
            TextMeshProUGUI hbAltasText = highBeamsObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) hbAltasText.font = fontAsset;
            hbAltasText.text = "[ D ]";
            hbAltasText.fontSize = 22f;
            hbAltasText.fontStyle = FontStyles.Bold;
            hbAltasText.alignment = TextAlignmentOptions.Center;
            hbAltasText.color = new Color(0.102f, 0.482f, 1f, 0.2f); // Inactivo por defecto

            // 8. Tira de Marchas P R N D (Centro inferior)
            GameObject gearStripObj = new GameObject("Gear_Strip");
            gearStripObj.transform.SetParent(canvasObj.transform, false);
            RectTransform gsRect = gearStripObj.AddComponent<RectTransform>();
            gsRect.anchorMin = new Vector2(0.5f, 0f);
            gsRect.anchorMax = new Vector2(0.5f, 0f);
            gsRect.anchoredPosition = new Vector2(0f, 30f);
            gsRect.sizeDelta = new Vector2(200f, 35f);

            string[] gearNames = { "P", "R", "N", "D" };
            float[] gearOffsets = { -75f, -25f, 25f, 75f };
            TMP_Text[] gearLabels = new TMP_Text[4];

            for (int i = 0; i < 4; i++)
            {
                GameObject gObj = new GameObject($"Gear_{gearNames[i]}");
                gObj.transform.SetParent(gearStripObj.transform, false);
                RectTransform gRect = gObj.AddComponent<RectTransform>();
                gRect.anchorMin = new Vector2(0.5f, 0.5f);
                gRect.anchorMax = new Vector2(0.5f, 0.5f);
                gRect.anchoredPosition = new Vector2(gearOffsets[i], 0f);
                gRect.sizeDelta = new Vector2(40f, 35f);

                TextMeshProUGUI gText = gObj.AddComponent<TextMeshProUGUI>();
                if (fontAsset != null) gText.font = fontAsset;
                gText.text = gearNames[i];
                gText.fontSize = 24f;
                gText.fontStyle = FontStyles.Bold;
                gText.alignment = TextAlignmentOptions.Center;
                gText.color = (i == 0)
                    ? new Color(1f, 0.85f, 0.2f, 1f) // P seleccionada en amarillo
                    : new Color(0.25f, 0.25f, 0.25f, 0.4f);

                gearLabels[i] = gText;
            }

            // 9. Componente DashboardUIController
            DashboardUIController controller = canvasObj.GetComponent<DashboardUIController>();
            if (controller == null) controller = canvasObj.AddComponent<DashboardUIController>();

            // Cablear serialized properties a través de SerializedObject
            SerializedObject so = new SerializedObject(controller);
            so.Update();

            if (vehicleController != null)
            {
                so.FindProperty("m_VehicleController").objectReferenceValue = vehicleController;
            }
            so.FindProperty("m_SpeedText").objectReferenceValue = speedText;
            so.FindProperty("m_UnitText").objectReferenceValue = unitText;
            so.FindProperty("m_TurnLeftIndicator").objectReferenceValue = tlText;
            so.FindProperty("m_TurnRightIndicator").objectReferenceValue = trText;
            so.FindProperty("m_HandbrakeIndicator").objectReferenceValue = hbText;
            so.FindProperty("m_HeadlightsIndicator").objectReferenceValue = hlText;
            so.FindProperty("m_HighBeamsIndicator").objectReferenceValue = hbAltasText;
            so.FindProperty("m_LowBeamActiveColor").colorValue = new Color(0.102f, 0.949f, 0.627f, 1f);
            so.FindProperty("m_LowBeamInactiveColor").colorValue = new Color(0.102f, 0.949f, 0.627f, 0.2f);
            so.FindProperty("m_HighBeamActiveColor").colorValue = new Color(0.102f, 0.482f, 1f, 1f);
            so.FindProperty("m_HighBeamInactiveColor").colorValue = new Color(0.102f, 0.482f, 1f, 0.2f);

            SerializedProperty gearArrayProp = so.FindProperty("m_GearLabels");
            gearArrayProp.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                gearArrayProp.GetArrayElementAtIndex(i).objectReferenceValue = gearLabels[i];
            }

            so.ApplyModifiedProperties();

            // 10. Cablear botón de balizas (HazardButton) con VehicleController
            if (vehicleController != null)
            {
                VRToggleButton[] toggles = carRoot.GetComponentsInChildren<VRToggleButton>(true);
                foreach (var toggle in toggles)
                {
                    if (toggle.gameObject.name.Contains("Hazard"))
                    {
                        for (int i = toggle.OnToggledOn.GetPersistentEventCount() - 1; i >= 0; i--)
                        {
                            UnityEditor.Events.UnityEventTools.RemovePersistentListener(toggle.OnToggledOn, i);
                        }
                        for (int i = toggle.OnToggledOff.GetPersistentEventCount() - 1; i >= 0; i--)
                        {
                            UnityEditor.Events.UnityEventTools.RemovePersistentListener(toggle.OnToggledOff, i);
                        }

                        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(
                            toggle.OnToggledOn,
                            vehicleController.SetHazardActive,
                            true
                        );

                        UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(
                            toggle.OnToggledOff,
                            vehicleController.SetHazardActive,
                            false
                        );

                        EditorUtility.SetDirty(toggle);
                        Debug.Log($"[DashboardClusterSetup] 🔘 Conectados eventos OnToggledOn/Off de {toggle.gameObject.name} a VehicleController.SetHazardActive.");
                        break;
                    }
                }
            }

            Debug.Log($"[DashboardClusterSetup] 🚀 Tablero diegético configurado y cableado exitosamente en {carRoot.name}!");
            return canvasObj;
        }
    }
}
