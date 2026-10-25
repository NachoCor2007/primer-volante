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
    /// en el vehículo Car06 y en la escena DashboardTestingScene. También agrega (sin regenerar el tablero)
    /// los testigos de infracción y su chime a los prefabs de Car06 a Car09.
    /// </summary>
    public static class DashboardClusterSetup
    {
        private const string SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/DashboardTestingScene.unity";
        private const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car06.prefab";
        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string ICON_POSITION_PATH = "Assets/Textures/UI/Dashboard/icon_lights_position.png";
        private const string ICON_LOWBEAM_PATH = "Assets/Textures/UI/Dashboard/icon_lights_lowbeam.png";
        private const string ICON_HIGHBEAM_PATH = "Assets/Textures/UI/Dashboard/icon_lights_highbeam.png";
        public const string ICON_INFRACTION_PATH = "Assets/Textures/UI/Dashboard/icon_infraction_warning.png";

        public const string INFRACTION_LEFT_NAME = "Indicator_Infraction_Left";
        public const string INFRACTION_RIGHT_NAME = "Indicator_Infraction_Right";

        /// <summary>Posición X (respecto del centro del canvas) de los testigos de infracción; el izquierdo usa -X.</summary>
        public const float INFRACTION_INDICATOR_X = 150f;
        public const float INFRACTION_INDICATOR_Y = -35f;
        public const float INFRACTION_INDICATOR_SIZE = 40f;

        private static readonly Color s_InfractionActiveColor = new Color(1f, 0.15f, 0.15f, 1f);
        private static readonly Color s_InfractionInactiveColor = new Color(1f, 0.15f, 0.15f, 0.2f);
        private const float INFRACTION_WARNING_DURATION = 3f;

        private static readonly string[] s_VehiclePrefabsWithDashboard =
        {
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car06.prefab",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car07.prefab",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car08.prefab",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab"
        };

        private static Sprite LoadSprite(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        [MenuItem("Tools/Primer Volante/Setup Dashboard in Car06 Prefab and Scene")]
        public static void SetupDashboardAll()
        {
            SetupDashboardInPrefab();
            SetupDashboardInScene();
        }

        [MenuItem("Tools/Primer Volante/Setup Dashboard in All Vehicle Prefabs (Car06, Car07, Car08)")]
        public static void SetupDashboardInAllPrefabs()
        {
            string[] prefabs = {
                "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car06.prefab",
                "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car07.prefab",
                "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car08.prefab"
            };

            foreach (var path in prefabs)
            {
                if (!System.IO.File.Exists(path)) continue;
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null) continue;
                try
                {
                    ConfigureDashboardCluster(prefabRoot);
                    VehicleLightingSetup.ConfigureVehicleLighting(prefabRoot);
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    Debug.Log($"[DashboardClusterSetup] ✅ {System.IO.Path.GetFileName(path)} actualizado con el nuevo tablero diegético e íconos!");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        /// <summary>
        /// Agrega a Car06-Car09 los testigos de infracción del tablero y el componente de audio del chime,
        /// sin regenerar el tablero (no toca ningún otro hijo del canvas). Idempotente.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Add Infraction Warning to Vehicle Prefabs (Car06-Car09)")]
        public static void AddInfractionWarningToVehiclePrefabs()
        {
            foreach (var path in s_VehiclePrefabsWithDashboard)
            {
                if (!System.IO.File.Exists(path)) continue;
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                if (prefabRoot == null) continue;
                try
                {
                    Transform canvas = prefabRoot.transform.Find("Dashboard_Cluster_Canvas");
                    if (canvas == null)
                    {
                        Debug.LogWarning($"[DashboardClusterSetup] ⚠ {System.IO.Path.GetFileName(path)} no tiene Dashboard_Cluster_Canvas: se omite.");
                        continue;
                    }

                    EnsureInfractionIndicators(canvas.gameObject);
                    EnsureInfractionAudio(prefabRoot, canvas.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    Debug.Log($"[DashboardClusterSetup] ✅ {System.IO.Path.GetFileName(path)}: testigos de infracción y chime configurados.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
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
            hbText.text = "(!)";
            hbText.fontSize = 26f;
            hbText.fontStyle = FontStyles.Bold;
            hbText.alignment = TextAlignmentOptions.Center;
            hbText.color = new Color(1f, 0.15f, 0.15f, 1f); // Activo por defecto

            // 7. Testigos de Luces Frontales: Posición, Bajas y Altas (Derecha inferior)
            Sprite posSprite = LoadSprite(ICON_POSITION_PATH);
            Sprite lowBeamSprite = LoadSprite(ICON_LOWBEAM_PATH);
            Sprite highBeamSprite = LoadSprite(ICON_HIGHBEAM_PATH);

            // 7.A. Luces de Posición (verde)
            GameObject positionObj = new GameObject("Indicator_PositionLights");
            positionObj.transform.SetParent(canvasObj.transform, false);
            RectTransform posRect = positionObj.AddComponent<RectTransform>();
            posRect.anchorMin = new Vector2(0.5f, 0f);
            posRect.anchorMax = new Vector2(0.5f, 0f);
            posRect.anchoredPosition = new Vector2(128f, 30f);
            posRect.sizeDelta = new Vector2(40f, 40f);
            Image posImage = positionObj.AddComponent<Image>();
            posImage.sprite = posSprite;
            posImage.preserveAspect = true;
            posImage.color = new Color(0.2f, 0.85f, 0.3f, 0.2f); // Inactivo por defecto

            // 7.B. Luces Bajas (#1AF2A0)
            GameObject headlightsObj = new GameObject("Indicator_Headlights");
            headlightsObj.transform.SetParent(canvasObj.transform, false);
            RectTransform hlRect = headlightsObj.AddComponent<RectTransform>();
            hlRect.anchorMin = new Vector2(0.5f, 0f);
            hlRect.anchorMax = new Vector2(0.5f, 0f);
            hlRect.anchoredPosition = new Vector2(176f, 30f);
            hlRect.sizeDelta = new Vector2(40f, 40f);
            Image hlImage = headlightsObj.AddComponent<Image>();
            hlImage.sprite = lowBeamSprite;
            hlImage.preserveAspect = true;
            hlImage.color = new Color(0.102f, 0.949f, 0.627f, 0.2f); // Inactivo por defecto

            // 7.C. Luces Altas (#1A7BFF con ícono real)
            GameObject highBeamsObj = new GameObject("Indicator_HighBeams");
            highBeamsObj.transform.SetParent(canvasObj.transform, false);
            RectTransform hbAltasRect = highBeamsObj.AddComponent<RectTransform>();
            hbAltasRect.anchorMin = new Vector2(0.5f, 0f);
            hbAltasRect.anchorMax = new Vector2(0.5f, 0f);
            hbAltasRect.anchoredPosition = new Vector2(224f, 30f);
            hbAltasRect.sizeDelta = new Vector2(40f, 40f);
            Image hbAltasImage = highBeamsObj.AddComponent<Image>();
            hbAltasImage.sprite = highBeamSprite;
            hbAltasImage.preserveAspect = true;
            hbAltasImage.color = new Color(0.102f, 0.482f, 1f, 0.2f); // Inactivo por defecto

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
            so.FindProperty("m_PositionLightsIndicator").objectReferenceValue = posImage;
            so.FindProperty("m_PositionActiveColor").colorValue = new Color(0.2f, 0.85f, 0.3f, 1f);
            so.FindProperty("m_PositionInactiveColor").colorValue = new Color(0.2f, 0.85f, 0.3f, 0.2f);
            so.FindProperty("m_HeadlightsIndicator").objectReferenceValue = hlImage;
            so.FindProperty("m_HighBeamsIndicator").objectReferenceValue = hbAltasImage;
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

            // 9.b. Testigos de infracción (triángulos rojos a cada lado de la velocidad)
            EnsureInfractionIndicators(canvasObj);
            EnsureInfractionAudio(carRoot, canvasObj);

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

        // ---------------------------------------------------------------- testigos de infracción

        /// <summary>
        /// Busca o crea <c>Indicator_Infraction_Left</c> e <c>Indicator_Infraction_Right</c> como hijos directos
        /// del canvas (triángulo de advertencia, 40x40, arriba al centro a x = -150 / +150), y los cablea en el
        /// <see cref="DashboardUIController"/> junto con colores y duración. Idempotente: si ya existen solo
        /// reajusta layout, sprite y color; no duplica ni toca ningún otro hijo.
        /// </summary>
        public static void EnsureInfractionIndicators(GameObject canvasObj)
        {
            Sprite sprite = EnsureInfractionWarningIcon();

            Image left = EnsureInfractionIndicator(canvasObj.transform, INFRACTION_LEFT_NAME, -INFRACTION_INDICATOR_X, sprite);
            Image right = EnsureInfractionIndicator(canvasObj.transform, INFRACTION_RIGHT_NAME, INFRACTION_INDICATOR_X, sprite);

            DashboardUIController controller = canvasObj.GetComponent<DashboardUIController>();
            if (controller == null) controller = canvasObj.AddComponent<DashboardUIController>();

            SerializedObject so = new SerializedObject(controller);
            so.Update();
            so.FindProperty("m_InfractionLeftIndicator").objectReferenceValue = left;
            so.FindProperty("m_InfractionRightIndicator").objectReferenceValue = right;
            so.FindProperty("m_InfractionActiveColor").colorValue = s_InfractionActiveColor;
            so.FindProperty("m_InfractionInactiveColor").colorValue = s_InfractionInactiveColor;
            so.FindProperty("m_InfractionWarningDuration").floatValue = INFRACTION_WARNING_DURATION;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }

        private static Image EnsureInfractionIndicator(Transform canvas, string name, float x, Sprite sprite)
        {
            Transform existing = canvas.Find(name);
            GameObject obj;
            if (existing != null)
            {
                obj = existing.gameObject;
            }
            else
            {
                obj = new GameObject(name, typeof(RectTransform));
                obj.transform.SetParent(canvas, false);
            }

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, INFRACTION_INDICATOR_Y);
            rect.sizeDelta = new Vector2(INFRACTION_INDICATOR_SIZE, INFRACTION_INDICATOR_SIZE);

            Image image = obj.GetComponent<Image>();
            if (image == null) image = obj.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = s_InfractionInactiveColor;
            return image;
        }

        /// <summary>
        /// Agrega (o completa) el <see cref="VehicleInfractionAudio"/> en el GameObject del canvas, con su
        /// AudioSource, el perfil y el AudioMixerGroup del <see cref="VehicleCabinControlsAudio"/> del mismo
        /// prefab. Si el prefab no tiene ese componente deja perfil y mixer sin tocar y avisa. Solo completa perfil y mixer vacíos.
        /// </summary>
        public static void EnsureInfractionAudio(GameObject carRoot, GameObject canvasObj)
        {
            AudioSource source = canvasObj.GetComponent<AudioSource>();
            if (source == null) source = canvasObj.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;

            VehicleInfractionAudio audio = canvasObj.GetComponent<VehicleInfractionAudio>();
            if (audio == null) audio = canvasObj.AddComponent<VehicleInfractionAudio>();

            SerializedObject so = new SerializedObject(audio);
            so.Update();
            so.FindProperty("m_Dashboard").objectReferenceValue = canvasObj.GetComponent<DashboardUIController>();
            so.FindProperty("m_Source").objectReferenceValue = source;

            VehicleCabinControlsAudio cabin = carRoot.GetComponentInChildren<VehicleCabinControlsAudio>(true);
            if (cabin != null)
            {
                // Solo completa lo que falta: no pisa un perfil o mixer elegido a mano.
                SerializedObject cabinSo = new SerializedObject(cabin);
                SerializedProperty profileProp = so.FindProperty("m_Profile");
                SerializedProperty mixerProp = so.FindProperty("m_MixerGroup");
                if (profileProp.objectReferenceValue == null)
                    profileProp.objectReferenceValue = cabinSo.FindProperty("m_Profile").objectReferenceValue;
                if (mixerProp.objectReferenceValue == null)
                    mixerProp.objectReferenceValue = cabinSo.FindProperty("m_MixerGroup").objectReferenceValue;
            }
            else
            {
                Debug.LogWarning($"[DashboardClusterSetup] ⚠ {carRoot.name} no tiene VehicleCabinControlsAudio: VehicleInfractionAudio queda sin perfil ni mixer (no sonará).");
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(audio);
            EditorUtility.SetDirty(source);
        }

        /// <summary>
        /// Genera <c>icon_infraction_warning.png</c> si no existe (nunca lo pisa) y devuelve su sprite.
        /// </summary>
        public static Sprite EnsureInfractionWarningIcon()
        {
            if (!System.IO.File.Exists(ICON_INFRACTION_PATH))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ICON_INFRACTION_PATH));
                Texture2D tex = GenerateInfractionWarningTexture(128);
                System.IO.File.WriteAllBytes(ICON_INFRACTION_PATH, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(ICON_INFRACTION_PATH, ImportAssetOptions.ForceSynchronousImport);
            }
            return LoadSprite(ICON_INFRACTION_PATH);
        }

        /// <summary>
        /// Triángulo equilátero blanco (esquinas redondeadas, márgenes de 6 px, borde suavizado ~1.5 px) con un "!"
        /// recortado en transparente (barra + punto), sobre fondo transparente.
        /// </summary>
        private static Texture2D GenerateInfractionWarningTexture(int size)
        {
            const float margin = 6f;
            const float edgeSoftness = 1.5f;
            const float cornerRadius = 7f;

            float width = size - 2f * margin;
            float height = width * 0.8660254f;
            float cx = size * 0.5f;
            float bottom = (size - height) * 0.5f;
            float top = bottom + height;

            // Vértices del triángulo "encogido" por el radio de las esquinas (la distancia a él, menos el radio,
            // da el triángulo redondeado exacto con el mismo contorno exterior).
            Vector2 centroid = new Vector2(cx, bottom + height / 3f);
            Vector2[] outer =
            {
                new Vector2(cx - width * 0.5f, bottom),
                new Vector2(cx + width * 0.5f, bottom),
                new Vector2(cx, top)
            };
            // Escala hacia el centroide: inradio = height / 3.
            float inradius = height / 3f;
            float shrink = (inradius - cornerRadius) / inradius;
            Vector2[] tri = new Vector2[3];
            for (int i = 0; i < 3; i++) tri[i] = centroid + (outer[i] - centroid) * shrink;

            // "!": barra con extremos redondeados y punto, centrados en x.
            float barHalfWidth = 7f;
            float barBottom = centroid.y + 3f;
            float barTop = centroid.y + 41f;
            Vector2 dotCenter = new Vector2(cx, bottom + 19f);
            float dotRadius = 8f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                    float triSdf = SignedDistanceToTriangle(p, tri[0], tri[1], tri[2]) - cornerRadius;
                    float alpha = Mathf.Clamp01(0.5f - triSdf / edgeSoftness);

                    // Barra: segmento vertical con radio = mitad del ancho.
                    float barSdf = DistanceToSegment(p, new Vector2(cx, barBottom + barHalfWidth * 0.5f),
                        new Vector2(cx, barTop - barHalfWidth * 0.5f)) - barHalfWidth * 0.5f - 1.5f;
                    float dotSdf = Vector2.Distance(p, dotCenter) - dotRadius;
                    float cutSdf = Mathf.Min(barSdf, dotSdf);
                    alpha *= Mathf.Clamp01(0.5f + cutSdf / edgeSoftness);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distancia con signo (negativa adentro) de un punto a un triángulo.</summary>
        private static float SignedDistanceToTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d = Mathf.Min(DistanceToSegment(p, a, b), Mathf.Min(DistanceToSegment(p, b, c), DistanceToSegment(p, c, a)));
            float s1 = Cross(b - a, p - a);
            float s2 = Cross(c - b, p - b);
            float s3 = Cross(a - c, p - c);
            bool inside = (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
            return inside ? -d : d;
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
    }
}
