using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para configurar la arquitectura de lecciones
    /// y la Lección 1 ("Calibración del ambiente de manejo") para el proyecto "Primer Volante" (TASK-204 / Issue #19).
    /// Clona de forma segura CityMerged_ScriptedEvents.unity a Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity,
    /// genera el asset LessonData_01.asset y ensambla el panel HUD sobre el volante de Car09.
    /// </summary>
    public static class Lesson01SceneSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged_ScriptedEvents.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string LESSON_DATA_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DATA_DIR = "Assets/Scripts/Lessons/Steps";
        public const string LESSON_DATA_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_01.asset";
        public const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        public const string PREFAB_CAR09_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";
        public const string LESSON_AUDIO_DIR = "Assets/Audio/Lessons";
        public const string STEP_CHIME_PATH = "Assets/Audio/Lessons/step_notification_chime.wav";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 1 Scene")]
        public static void SetupLesson01()
        {
            Debug.Log("=========================================================");
            Debug.Log("🚀 [Lesson01SceneSetup] Iniciando configuración de Lección 1...");
            Debug.Log("=========================================================");

            // 1. Asegurar directorios
            EnsureDirectories();

            // 2. Crear o actualizar el asset ScriptableObject de la Lección 1
            LessonDefinitionSO lessonData = CreateOrUpdateLessonData();

            // 3. Asegurar la escena clonada
            EnsureClonedSceneExists();

            // 4. Abrir la escena de lección
            Scene scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson01SceneSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 5. Configurar Car09 y sus componentes de cabina (reemplazando Car08 si existiera)
            GameObject car09 = EnsurePlayerVehicleCar09(scene);
            if (car09 == null)
            {
                Debug.LogError("[Lesson01SceneSetup] ❌ No se pudo configurar el vehículo Car09 en la escena.");
                return;
            }

            // Revertir overrides indeseados en mandos de Car09 para que coincidan 100% con Car09.prefab
            RevertCockpitControlOverrides(car09);

            // Asegurar que DriverSeatFollower en XR Origin esté vinculado al asiento de Car09
            Transform driverSeat = car09.transform.Find("DriverSeat");
            if (driverSeat == null)
            {
                foreach (Transform t in car09.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(t.name, "DriverSeat", System.StringComparison.OrdinalIgnoreCase))
                    {
                        driverSeat = t;
                        break;
                    }
                }
            }

            DriverSeatFollower seatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();
            if (seatFollower != null && driverSeat != null)
            {
                seatFollower.DriverSeat = driverSeat;
                seatFollower.FollowSeat = true;
                seatFollower.SnapToSeat();

                var soFollower = new SerializedObject(seatFollower);
                soFollower.FindProperty("m_DriverSeat").objectReferenceValue = driverSeat;
                soFollower.FindProperty("m_FollowSeat").boolValue = true;
                soFollower.ApplyModifiedProperties();

                EditorUtility.SetDirty(seatFollower);
                Debug.Log($"[Lesson01SceneSetup] 💺 DriverSeatFollower vinculado exitosamente a '{driverSeat.name}' en Car09.");
            }

            // Asegurar que Car09 tenga el ensamble de retrovisores si hiciera falta
            VRRearviewMirror mirror = car09.GetComponentInChildren<VRRearviewMirror>(true);
            if (mirror == null)
            {
                Debug.Log("[Lesson01SceneSetup] 🪞 Ensamblando retrovisores en Car09 con VehicleMirrorSetupEditor...");
                VehicleMirrorSetupEditor.ConfigureMirrorsOnCar(car09);
            }

            CockpitControlLockManager lockManager = car09.GetComponent<CockpitControlLockManager>();
            if (lockManager == null)
            {
                lockManager = car09.AddComponent<CockpitControlLockManager>();
            }
            lockManager.AutoDiscoverControls(car09.transform);
            EditorUtility.SetDirty(lockManager);

            InteractableHighlighter highlighter = car09.GetComponent<InteractableHighlighter>();
            if (highlighter == null)
            {
                highlighter = car09.AddComponent<InteractableHighlighter>();
            }
            EditorUtility.SetDirty(highlighter);

            // 6. Asegurar InGameMenu y EventSystem con XRUIInputModule en la escena
            InGameMenuSetup.EnsureInGameMenuInActiveScene();

            // 7. Configurar o crear el LessonHUDPanel sobre el volante de Car09
            LessonHUDPanel hudPanel = SetupLessonHUD(car09);

            // 8. Configurar el LessonManager en la escena
            SetupLessonManager(scene, car09, lessonData, lockManager, highlighter, hudPanel);

            // Revertir overrides indeseados en mandos de Car09 antes de guardar
            RevertCockpitControlOverrides(car09);

            // 9. Guardar escena y assets
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("=========================================================");
            Debug.Log("✅ [Lesson01SceneSetup] ¡Escena y componentes de Lección 1 configurados exitosamente!");
            Debug.Log($"   Escena: {TARGET_SCENE_PATH}");
            Debug.Log($"   Asset:  {LESSON_DATA_PATH}");
            Debug.Log("=========================================================");
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
            if (!AssetDatabase.IsValidFolder(TARGET_SCENE_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scenes", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Scripts/Lessons"))
            {
                AssetDatabase.CreateFolder("Assets/Scripts", "Lessons");
            }
            if (!AssetDatabase.IsValidFolder(LESSON_DATA_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Curriculum");
            }
            if (!AssetDatabase.IsValidFolder(STEPS_DATA_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Steps");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Audio"))
            {
                AssetDatabase.CreateFolder("Assets", "Audio");
            }
            if (!AssetDatabase.IsValidFolder(LESSON_AUDIO_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Audio", "Lessons");
            }
        }

        /// <summary>
        /// Asegura que el asset físico de audio 'step_notification_chime.wav' exista en Assets/Audio/Lessons/,
        /// generándolo con dos tonos armónicos ascendentes (G5 a C6) y configurando el AudioImporter en modo DecompressOnLoad.
        /// </summary>
        public static AudioClip EnsureStepNotificationChimeAsset(bool force = false)
        {
            EnsureDirectories();
            string path = STEP_CHIME_PATH;

            if (force || !File.Exists(path) || new FileInfo(path).Length == 0)
            {
                GenerateStepChimeWav(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer != null)
                {
                    importer.forceToMono = true;
                    importer.ambisonic = false;
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.preloadAudioData = true;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
                Debug.Log($"[Lesson01SceneSetup] 🔔 Archivo de audio físico generado e importado en {path}");
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        /// <summary>
        /// Genera un clip WAV 16-bit PCM de ~0.35s con dos tonos armónicos ascendentes (G5 784 Hz a C6 1046 Hz)
        /// con envolvente exponencial suave, normalizado a 0.90 de pico para excelente presencia y claridad.
        /// </summary>
        public static void GenerateStepChimeWav(string path)
        {
            const int sampleRate = 44100;
            const float duration = 0.35f;
            int totalSamples = Mathf.RoundToInt(duration * sampleRate);
            float[] samples = new float[totalSamples];

            float freq1 = 783.99f; // G5
            float freq2 = 1046.50f; // C6
            float tone2Delay = 0.07f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;

                // Tono 1 (G5) con ataque de 5ms y decaimiento exponencial
                float attack1 = Mathf.Clamp01(t / 0.005f);
                float env1 = attack1 * Mathf.Exp(-11f * t);
                float wave1 = Mathf.Sin(2f * Mathf.PI * freq1 * t) * 0.7f +
                              Mathf.Sin(2f * Mathf.PI * freq1 * 2f * t) * 0.25f;
                float tone1 = wave1 * env1;

                // Tono 2 (C6) iniciado a 70ms con ataque de 5ms y decaimiento suave
                float tone2 = 0f;
                if (t >= tone2Delay)
                {
                    float t2 = t - tone2Delay;
                    float attack2 = Mathf.Clamp01(t2 / 0.005f);
                    float env2 = attack2 * Mathf.Exp(-8.5f * t2);
                    float wave2 = Mathf.Sin(2f * Mathf.PI * freq2 * t2) * 0.8f +
                                  Mathf.Sin(2f * Mathf.PI * freq2 * 2f * t2) * 0.2f;
                    tone2 = wave2 * env2;
                }

                samples[i] = tone1 * 0.65f + tone2 * 0.85f;
            }

            VehicleAudioPlaceholderGenerator.Normalize(samples, 0.90f);
            VehicleAudioPlaceholderGenerator.WriteWav16(path, samples);
        }

        private static void EnsureClonedSceneExists()
        {
            var targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TARGET_SCENE_PATH);
            if (targetScene == null)
            {
                var originalScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ORIGINAL_SCENE_PATH);
                if (originalScene == null)
                {
                    Debug.LogError($"[Lesson01SceneSetup] ❌ No se encontró la escena original en {ORIGINAL_SCENE_PATH}");
                    return;
                }

                bool copied = AssetDatabase.CopyAsset(ORIGINAL_SCENE_PATH, TARGET_SCENE_PATH);
                AssetDatabase.Refresh();

                if (copied)
                {
                    Debug.Log($"[Lesson01SceneSetup] 📋 Escena clonada exitosamente desde {ORIGINAL_SCENE_PATH} a {TARGET_SCENE_PATH}");
                }
                else
                {
                    Debug.LogError($"[Lesson01SceneSetup] ❌ Falló la clonación de la escena a {TARGET_SCENE_PATH}");
                }
            }
        }

        private static LessonStepSO CreateOrUpdateStep(string fileName, string title, string instruction,
            LessonStepCompletionMode mode, CockpitControlFlags allowedControls, string highlightTarget)
        {
            string subFolder = fileName.Length >= 8 && fileName.StartsWith("Lesson") ? fileName.Substring(0, 8) : "";
            string dir = string.IsNullOrEmpty(subFolder) ? STEPS_DATA_DIR : $"{STEPS_DATA_DIR}/{subFolder}";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder(STEPS_DATA_DIR, subFolder);
            }

            string path = $"{dir}/{fileName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            bool isNew = false;
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                isNew = true;
            }

            if (isNew)
            {
                step.stepTitle = title;
                step.instructionText = instruction;
                step.completionMode = mode;
                step.allowedControls = allowedControls;
                step.highlightTargetName = highlightTarget;
                AssetDatabase.CreateAsset(step, path);
            }
            else
            {
                // Preservar textos existentes actualizados en commit 80c0d1b
                if (string.IsNullOrEmpty(step.stepTitle))
                {
                    step.stepTitle = title;
                }
                if (string.IsNullOrEmpty(step.instructionText) || step.instructionText.Contains("con ambas manos"))
                {
                    step.instructionText = instruction;
                }
                step.completionMode = mode;
                step.allowedControls = allowedControls;
                step.highlightTargetName = highlightTarget;
                EditorUtility.SetDirty(step);
            }

            return step;
        }

        public static LessonDefinitionSO CreateOrUpdateLessonData()
        {
            EnsureDirectories();
            LessonDefinitionSO lesson = LessonsCurriculumSetup.SetupLesson01();
            Debug.Log($"[Lesson01SceneSetup] 📄 Asset de lección generado en {LESSON_DATA_PATH} con {lesson.steps.Count} pasos Action-Driven.");
            return lesson;
        }

        public static GameObject EnsurePlayerVehicleCar09(Scene scene)
        {
            Vector3 spawnPos = new Vector3(63.5f, 0.02f, 95f);
            Quaternion spawnRot = Quaternion.identity;
            bool posCaptured = false;

            // 1. Si existe Car08 en la escena, capturar su transformación y destruirlo
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Car08")
                {
                    spawnPos = root.transform.position;
                    spawnRot = root.transform.rotation;
                    posCaptured = true;
                    Debug.Log($"[Lesson01SceneSetup] 🔄 Reemplazando 'Car08' por 'Car09' en posición {spawnPos}...");
                    UnityEngine.Object.DestroyImmediate(root);
                    break;
                }
            }

            // 2. Buscar si ya existe Car09 en la escena
            GameObject car09 = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Car09")
                {
                    car09 = root;
                    break;
                }
                if (root.name.Contains("Car09") && root.GetComponentInChildren<VehicleController>(true) != null)
                {
                    car09 = root;
                    car09.name = "Car09";
                    break;
                }
            }

            // 3. Instanciar desde el prefab Car09.prefab si no existe
            if (car09 == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_CAR09_PATH);
                if (prefab == null)
                {
                    Debug.LogError($"[Lesson01SceneSetup] ❌ No se encontró el prefab de Car09 en {PREFAB_CAR09_PATH}");
                    return null;
                }

                car09 = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                car09.name = "Car09";
                car09.transform.position = spawnPos;
                car09.transform.rotation = spawnRot;
                Debug.Log($"[Lesson01SceneSetup] 🚗 Car09 instanciado exitosamente en {spawnPos}.");
            }
            else if (posCaptured)
            {
                car09.transform.position = spawnPos;
                car09.transform.rotation = spawnRot;
            }

            return car09;
        }

        private static GameObject FindPlayerVehicle(Scene scene)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            foreach (GameObject root in rootObjects)
            {
                if (root.name == "Car09") return root;
            }
            foreach (GameObject root in rootObjects)
            {
                VehicleController ctrl = root.GetComponentInChildren<VehicleController>(true);
                if (ctrl != null) return root;
            }
            return null;
        }

        /// <summary>
        /// Revierte cualquier override indebido en los mandos de Car09 para que coincidan 100%
        /// con los valores del prefab Car09.prefab (y los cambios de Nacho en main).
        /// Mantiene únicamente la posición/rotación raíz de Car09 en la escena y los componentes exclusivos de lecciones.
        /// </summary>
        public static void RevertCockpitControlOverrides(GameObject car09)
        {
            if (car09 == null || !PrefabUtility.IsPartOfPrefabInstance(car09)) return;

            // 1. Sincronizar en memoria y revertir Object Overrides en todos los transforms y componentes de Car09
            // que provienen del prefab (excluyendo la raíz Car09 y adiciones exclusivas de lecciones).
            foreach (Transform t in car09.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == car09.transform) continue;
                if (!PrefabUtility.IsPartOfPrefabInstance(t)) continue;

                Transform prefabT = PrefabUtility.GetCorrespondingObjectFromSource(t);
                if (prefabT != null)
                {
                    // Sincronizar bit a bit en memoria con el prefab para evitar que Unity
                    // auto-regenere deltas de precisión de coma flotante al guardar la escena.
                    t.localPosition = prefabT.localPosition;
                    t.localRotation = prefabT.localRotation;
                    t.localScale = prefabT.localScale;
                    PrefabUtility.RevertObjectOverride(t, InteractionMode.AutomatedAction);
                }

                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
                {
                    PrefabUtility.RevertObjectOverride(t.gameObject, InteractionMode.AutomatedAction);
                }

                foreach (Component comp in t.GetComponents<Component>())
                {
                    if (comp != null &&
                        !(comp is CockpitControlLockManager) &&
                        !(comp is InteractableHighlighter) &&
                        !(comp is LessonHUDPanel) &&
                        PrefabUtility.IsPartOfPrefabInstance(comp))
                    {
                        PrefabUtility.RevertObjectOverride(comp, InteractionMode.AutomatedAction);
                    }
                }
            }

            // 2. Limpiar las PropertyModifications: conservar ÚNICAMENTE las modificaciones de la raíz Car09 en la escena
            var mods = PrefabUtility.GetPropertyModifications(car09);
            if (mods != null)
            {
                var rootAssetGo = PrefabUtility.GetCorrespondingObjectFromSource(car09);
                var rootAssetTransform = PrefabUtility.GetCorrespondingObjectFromSource(car09.transform);

                var keptMods = new List<PropertyModification>();
                for (int i = 0; i < mods.Length; i++)
                {
                    var mod = mods[i];
                    if (mod.target == null) continue;

                    // Descartar explícitamente cualquier modificación en objetos llamados "Visual" o componentes hijos
                    if (mod.target.name == "Visual") continue;

                    bool isRootMod = (mod.target == (UnityEngine.Object)rootAssetGo || mod.target == (UnityEngine.Object)rootAssetTransform);
                    if (isRootMod)
                    {
                        keptMods.Add(mod);
                    }
                }

                PrefabUtility.SetPropertyModifications(car09, keptMods.ToArray());
            }

            EditorUtility.SetDirty(car09);
            Debug.Log("[Lesson01SceneSetup] 🛠️ Overrides indebidos de mandos en Car09 revertidos al prefab original y transforms sincronizados.");
        }

        private static LessonHUDPanel SetupLessonHUD(GameObject car09)
        {
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            // Buscar volante para posicionar el HUD relativo a él
            VRSteeringWheel wheel = car09.GetComponentInChildren<VRSteeringWheel>(true);
            Transform wheelPivot = wheel != null ? wheel.transform : car09.transform.Find("mdl_car02_body/SteeringWheel_Pivot");
            if (wheelPivot == null)
            {
                foreach (Transform t in car09.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "SteeringWheel_Pivot") { wheelPivot = t; break; }
                }
            }

            if (wheelPivot != null && PrefabUtility.IsPartOfPrefabInstance(wheelPivot))
            {
                PrefabUtility.RevertObjectOverride(wheelPivot, InteractionMode.AutomatedAction);
            }

            Transform parentBody = (wheelPivot != null && wheelPivot.parent != null) ? wheelPivot.parent : car09.transform;

            // Verificar si ya existe el HUD y destruirlo limpiamente (incluso si quedó en otra rama de la jerarquía de Car09)
            foreach (Transform t in car09.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "LessonHUD_Canvas")
                {
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                }
            }

            // 1. Crear Canvas raíz
            GameObject canvasObj = new GameObject("LessonHUD_Canvas");
            canvasObj.transform.SetParent(parentBody, false);

            Vector3 wheelLocalPos = wheelPivot != null ? wheelPivot.localPosition : new Vector3(-0.2975659f, 0.9444506f, 0.1298789f);
            // Posicionado directamente arriba del aro del volante (aprox Y: +0.22m por encima del centro, +0.08m adelantado en Z)
            canvasObj.transform.localPosition = new Vector3(wheelLocalPos.x, wheelLocalPos.y + 0.22f, wheelLocalPos.z + 0.08f);
            // Ligera inclinación hacia la vista del conductor
            canvasObj.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            canvasObj.transform.localScale = Vector3.one * 0.00075f;

            RectTransform canvasRect = canvasObj.AddComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(600f, 260f);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            GameObject xrOriginObj = GameObject.Find("XR Origin (XR Rig)");
            Transform headTransform = FindHeadTransform(xrOriginObj);
            Camera headCam = headTransform != null ? headTransform.GetComponent<Camera>() : Camera.main;
            if (headCam != null)
            {
                canvas.worldCamera = headCam;
            }

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();

            LessonHUDPanel hudPanel = canvasObj.AddComponent<LessonHUDPanel>();

            // 2. Fondo general
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.08f, 0.10f, 0.14f, 0.94f);

            // Borde superior decorativo
            GameObject headerBar = new GameObject("HeaderBar");
            headerBar.transform.SetParent(bgObj.transform, false);
            RectTransform hbRect = headerBar.AddComponent<RectTransform>();
            hbRect.anchorMin = new Vector2(0f, 1f);
            hbRect.anchorMax = new Vector2(1f, 1f);
            hbRect.pivot = new Vector2(0.5f, 1f);
            hbRect.anchoredPosition = Vector2.zero;
            hbRect.sizeDelta = new Vector2(0f, 6f);
            Image hbImage = headerBar.AddComponent<Image>();
            hbImage.color = new Color(0.2f, 0.65f, 1f, 1f);

            // 3. Panel de Contenido de Pasos
            GameObject stepContent = new GameObject("StepContentPanel");
            stepContent.transform.SetParent(canvasObj.transform, false);
            RectTransform scRect = stepContent.AddComponent<RectTransform>();
            scRect.anchorMin = Vector2.zero;
            scRect.anchorMax = Vector2.one;
            scRect.offsetMin = Vector2.zero;
            scRect.offsetMax = Vector2.zero;

            // Título de la lección
            TextMeshProUGUI titleText = CreateText("LessonTitle", stepContent.transform, fontAsset,
                "Lección 1: Calibración del Ambiente de Manejo", 22f, FontStyles.Bold,
                new Vector2(0f, -24f), new Vector2(560f, 30f), new Color(0.35f, 0.75f, 1f, 1f));

            // Indicador de progreso del paso (Lección 1: 5 pasos)
            TextMeshProUGUI progressText = CreateText("StepProgress", stepContent.transform, fontAsset,
                "Paso 1 de 5: Ajuste de Asiento y Postura", 17f, FontStyles.Normal,
                new Vector2(0f, -54f), new Vector2(560f, 24f), new Color(0.85f, 0.85f, 0.90f, 1f));

            // Instrucción detallada
            TextMeshProUGUI instructionText = CreateText("InstructionText", stepContent.transform, fontAsset,
                "Para ajustar tu postura, altura y distancia del asiento, presiona el botón Menu en tu control izquierdo (o tecla Escape) para abrir el menú de calibración.\n\nAjusta tu posición y cierra el menú al finalizar para avanzar.",
                15f, FontStyles.Normal, new Vector2(0f, -118f), new Vector2(560f, 85f), Color.white);
            instructionText.textWrappingMode = TextWrappingModes.Normal;

            // Botón Continuar / Confirmar Posición (Action-Driven: inactivo mientras espera acción)
            Button continueBtn = CreateButton("Button_Continue", stepContent.transform, fontAsset, "Esperando acción...",
                new Vector2(0f, -198f), new Vector2(280f, 46f), new Color(0.12f, 0.55f, 0.40f, 1f));
            continueBtn.interactable = false;
            TextMeshProUGUI continueBtnText = continueBtn.GetComponentInChildren<TextMeshProUGUI>();

            // TAREA 2: Destruir cualquier residuo de SuccessBadge si existiera
            Transform residualBadge = stepContent.transform.Find("SuccessBadge");
            if (residualBadge != null)
            {
                UnityEngine.Object.DestroyImmediate(residualBadge.gameObject);
            }

            // Borde brillante de feedback sensorial perimetral (marco hueco 9-sliced, oculto por defecto)
            GameObject glowObj = new GameObject("SuccessBorderGlow");
            glowObj.transform.SetParent(canvasObj.transform, false);
            RectTransform glowRect = glowObj.AddComponent<RectTransform>();
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = Vector2.zero;
            glowRect.offsetMax = Vector2.zero;
            Image glowImage = glowObj.AddComponent<Image>();
            glowImage.sprite = LessonHUDPanel.GetOrCreateHollowBorderSprite();
            glowImage.type = Image.Type.Sliced;
            glowImage.fillCenter = false;
            glowImage.color = new Color(0f, 0.902f, 0.463f, 0f);
            glowImage.raycastTarget = false;
            glowObj.SetActive(false);

            // AudioSource para feedback sonoro (chime)
            AudioSource hudAudio = canvasObj.GetComponent<AudioSource>();
            if (hudAudio == null)
            {
                hudAudio = canvasObj.AddComponent<AudioSource>();
            }
            hudAudio.playOnAwake = false;
            hudAudio.spatialBlend = 0f;
            hudAudio.volume = 1f;

            AudioClip chimeClip = EnsureStepNotificationChimeAsset();
            hudPanel.SuccessChimeClip = chimeClip;

            // 4. Subpanel de Felicitaciones / Fin de Lección (oculto inicialmente)
            GameObject completionPanel = new GameObject("CompletionPanel");
            completionPanel.transform.SetParent(canvasObj.transform, false);
            RectTransform cpRect = completionPanel.AddComponent<RectTransform>();
            cpRect.anchorMin = Vector2.zero;
            cpRect.anchorMax = Vector2.one;
            cpRect.offsetMin = Vector2.zero;
            cpRect.offsetMax = Vector2.zero;

            TextMeshProUGUI compTitle = CreateText("CompletionTitle", completionPanel.transform, fontAsset,
                "¡Excelente Trabajo!", 26f, FontStyles.Bold,
                new Vector2(0f, -50f), new Vector2(560f, 40f), new Color(0.3f, 0.95f, 0.55f, 1f));

            TextMeshProUGUI compDesc = CreateText("CompletionDesc", completionPanel.transform, fontAsset,
                "Has completado con éxito la Lección 1.\nTu ambiente de manejo ha quedado calibrado correctamente.",
                15f, FontStyles.Normal,
                new Vector2(0f, -115f), new Vector2(540f, 70f), Color.white);
            compDesc.textWrappingMode = TextWrappingModes.Normal;

            Button restartBtn = CreateButton("Button_Restart", completionPanel.transform, fontAsset, "Repetir Lección",
                new Vector2(-135f, -195f), new Vector2(240f, 46f), new Color(0.25f, 0.45f, 0.70f, 1f));

            Button nextBtn = CreateButton("Button_NextLesson", completionPanel.transform, fontAsset, "Siguiente Lección",
                new Vector2(135f, -195f), new Vector2(240f, 46f), new Color(0.15f, 0.65f, 0.35f, 1f));
            TextMeshProUGUI nextBtnText = nextBtn.GetComponentInChildren<TextMeshProUGUI>();

            completionPanel.SetActive(false);

            // 5. Asignar capa UI (Layer 5) a todos los elementos del HUD
            int uiLayer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 5;
            SetLayerRecursively(canvasObj, uiLayer);

            // 6. Detectar NearFarInteractors del jugador
            NearFarInteractor[] farCastInteractors = xrOriginObj != null
                ? xrOriginObj.GetComponentsInChildren<NearFarInteractor>(true)
                : UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include);

            // 7. Cablear referencias en LessonHUDPanel mediante SerializedObject
            var so = new SerializedObject(hudPanel);
            so.FindProperty("m_LessonTitleText").objectReferenceValue = titleText;
            so.FindProperty("m_StepProgressText").objectReferenceValue = progressText;
            so.FindProperty("m_InstructionText").objectReferenceValue = instructionText;
            so.FindProperty("m_StepContentPanel").objectReferenceValue = stepContent;
            so.FindProperty("m_ContinueButton").objectReferenceValue = continueBtn;
            so.FindProperty("m_ContinueButtonText").objectReferenceValue = continueBtnText;
            so.FindProperty("m_CompletionPanel").objectReferenceValue = completionPanel;
            so.FindProperty("m_CompletionTitleText").objectReferenceValue = compTitle;
            so.FindProperty("m_CompletionDescriptionText").objectReferenceValue = compDesc;
            so.FindProperty("m_RestartButton").objectReferenceValue = restartBtn;
            so.FindProperty("m_NextLessonButton").objectReferenceValue = nextBtn;
            so.FindProperty("m_NextLessonButtonText").objectReferenceValue = nextBtnText;
            so.FindProperty("m_SuccessBorderGlow").objectReferenceValue = glowImage;
            so.FindProperty("m_AudioSource").objectReferenceValue = hudAudio;
            so.FindProperty("m_SuccessChimeClip").objectReferenceValue = chimeClip;

            InGameMenuController menuCtrl = UnityEngine.Object.FindAnyObjectByType<InGameMenuController>();
            if (menuCtrl != null)
            {
                so.FindProperty("m_InGameMenuController").objectReferenceValue = menuCtrl;
            }

            SerializedProperty farCastProp = so.FindProperty("m_FarCastInteractors");
            if (farCastProp != null)
            {
                farCastProp.arraySize = farCastInteractors.Length;
                for (int i = 0; i < farCastInteractors.Length; i++)
                {
                    farCastProp.GetArrayElementAtIndex(i).objectReferenceValue = farCastInteractors[i];
                }
            }

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(hudPanel);
            return hudPanel;
        }

        private static void SetupLessonManager(Scene scene, GameObject car09, LessonDefinitionSO lessonData,
            CockpitControlLockManager lockManager, InteractableHighlighter highlighter, LessonHUDPanel hudPanel)
        {
            // Buscar o crear GameObject raíz del LessonManager
            GameObject managerObj = GameObject.Find("Lesson_Manager");
            if (managerObj == null)
            {
                managerObj = new GameObject("Lesson_Manager");
                SceneManager.MoveGameObjectToScene(managerObj, scene);
            }

            LessonManager manager = managerObj.GetComponent<LessonManager>();
            if (manager == null)
            {
                manager = managerObj.AddComponent<LessonManager>();
            }

            LessonActionMonitor actionMonitor = managerObj.GetComponent<LessonActionMonitor>();
            if (actionMonitor == null)
            {
                actionMonitor = managerObj.AddComponent<LessonActionMonitor>();
            }

            VehicleController vehicleController = car09 != null ? car09.GetComponent<VehicleController>() : null;
            if (vehicleController == null)
            {
                vehicleController = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            }

            LessonDefinitionSO actualLessonData = lessonData != null ? lessonData : AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_DATA_PATH);

            var so = new SerializedObject(manager);
            so.FindProperty("m_ActiveLesson").objectReferenceValue = actualLessonData;
            so.FindProperty("m_AutoStartOnStart").boolValue = true;
            so.FindProperty("m_LockManager").objectReferenceValue = lockManager;
            so.FindProperty("m_Highlighter").objectReferenceValue = highlighter;
            so.FindProperty("m_HUDPanel").objectReferenceValue = hudPanel;
            so.FindProperty("m_VehicleRoot").objectReferenceValue = car09.transform;
            so.FindProperty("m_VehicleController").objectReferenceValue = vehicleController;
            so.FindProperty("m_ActionMonitor").objectReferenceValue = actionMonitor;

            SerializedProperty currProp = so.FindProperty("m_Curriculum");
            if (currProp != null)
            {
                var curriculumLessons = new List<LessonDefinitionSO>();
                for (int l = 1; l <= 4; l++)
                {
                    string lPath = $"Assets/Scripts/Lessons/Curriculum/LessonData_0{l}.asset";
                    LessonDefinitionSO lDef = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(lPath);
                    if (lDef != null) curriculumLessons.Add(lDef);
                }
                currProp.arraySize = curriculumLessons.Count;
                for (int i = 0; i < curriculumLessons.Count; i++)
                {
                    currProp.GetArrayElementAtIndex(i).objectReferenceValue = curriculumLessons[i];
                }
            }
            so.ApplyModifiedProperties();

            var activeLessonField = typeof(LessonManager).GetField("m_ActiveLesson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (activeLessonField != null)
            {
                activeLessonField.SetValue(manager, actualLessonData);
            }

            var hudSo = new SerializedObject(hudPanel);
            hudSo.FindProperty("m_LessonManager").objectReferenceValue = manager;
            hudSo.ApplyModifiedProperties();

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(hudPanel);
        }

        private static Transform FindHeadTransform(GameObject xrOriginObj)
        {
            if (xrOriginObj == null) return null;

            Transform cameraOffset = xrOriginObj.transform.Find("Camera Offset");
            if (cameraOffset != null)
            {
                Transform mainCamera = cameraOffset.Find("Main Camera");
                if (mainCamera != null) return mainCamera;
            }

            Camera cam = xrOriginObj.GetComponentInChildren<Camera>();
            return cam != null ? cam.transform : null;
        }

        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }

        // =========================================================================
        // Helpers de Construcción de UI
        // =========================================================================

        private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset fontAsset,
            string content, float fontSize, FontStyles style, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;

            return tmp;
        }

        private static Button CreateButton(string name, Transform parent, TMP_FontAsset fontAsset,
            string labelText, Vector2 anchoredPosition, Vector2 sizeDelta, Color bgColor)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image bgImage = btnObj.AddComponent<Image>();
            bgImage.color = bgColor;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = bgImage;

            ColorBlock colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.8f;
            colors.selectedColor = bgColor * 1.15f;
            btn.colors = colors;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.text = labelText;
            tmp.fontSize = 17f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }
    }
}
