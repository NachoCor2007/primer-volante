using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente para configurar y ensamblar la Lección 6:
    /// "Maniobras de Estacionamiento" (TASK-301 / Issue #10 / PRI-46).
    /// Clona de forma segura Lesson_01_BasicDriving.unity a Assets/Scenes/Lessons/Lesson_06_ParkingManeuvers.unity,
    /// crea los ScriptableObjects correspondientes y construye el área de dársenas en el playón (X=120, Z=60 a 120)
    /// con TurnSignalExemptZone, recuadros amarillos demarcados en el asfalto, conos de tránsito y vehículos
    /// estáticos equipados con ObstacleCollisionDetector, limpiando vehículos residuales de la calle.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Lesson06ParkingSetup
    {
        public const string ORIGINAL_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_06_ParkingManeuvers.unity";

        public const string CURRICULUM_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps/Lesson06";
        public const string LESSON_06_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_06.asset";

        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";
        public const string STATIC_CAR_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels/Vehicle_Car_color01.prefab";
        public const string YELLOW_MATERIAL_PATH = "Assets/Environment/Materials/M_Parking_YellowZone.mat";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 6 Scene")]
        public static void SetupLesson06()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [Lesson06ParkingSetup] Iniciando configuración de Lección 6...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Crear o actualizar los ScriptableObjects de la Lección 6 con textos claros y concisos
            LessonDefinitionSO lessonData = CreateOrUpdateLesson06Assets();

            // 2. Asegurar que la escena clonada exista
            EnsureClonedSceneExists();

            // 3. Abrir la escena de la Lección 6 si no está abierta
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != TARGET_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson06ParkingSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 4. Asegurar Car09 y sus componentes de cabina
            GameObject car09 = EnsureCar09InScene(scene);
            if (car09 == null)
            {
                Debug.LogError("[Lesson06ParkingSetup] ❌ No se pudo encontrar o configurar Car09 en la escena.");
                return;
            }

            // 5. Limpiar vehículos decorativos residuales de la calle X=120 para despejar el área de maniobras
            CleanResidualVehiclesInStreet(scene, car09);

            // 6. Configurar detector de guiños en Car09
            TurnSignalDetector turnDetector = car09.GetComponent<TurnSignalDetector>();
            if (turnDetector == null)
            {
                turnDetector = car09.AddComponent<TurnSignalDetector>();
            }
            int laneZoneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (laneZoneLayer >= 0)
            {
                turnDetector.ZoneMask = 1 << laneZoneLayer;
            }
            EditorUtility.SetDirty(turnDetector);

            // 7. Asegurar VRScreenFader en la escena
            VRScreenFader screenFader = VRScreenFader.EnsureScreenFader();
            EditorUtility.SetDirty(screenFader);

            // 8. Configurar la TurnSignalExemptZone en el playón (X=120, Z=60 a 120)
            TurnSignalExemptZone exemptZone = SetupTurnSignalExemptZone(scene);

            // 9. Construir las tres estaciones de estacionamiento con recuadros amarillos en el piso, conos y autos
            ParkingZoneTrigger trigger45, trigger90, triggerParallel;
            GameObject hl45, hl90, hlParallel;
            BuildParkingStations(scene, out trigger45, out trigger90, out triggerParallel, out hl45, out hl90, out hlParallel);

            // 10. Configurar puntos de spawn para cada paso
            Transform[] spawnPoints = EnsureSpawnPoints(scene);

            // 11. Posicionar Car09 inicialmente en la aproximación al área de maniobras
            if (spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[0] != null)
            {
                car09.transform.position = spawnPoints[0].position;
                car09.transform.rotation = spawnPoints[0].rotation;

                Rigidbody rb = car09.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = spawnPoints[0].position;
                    rb.rotation = spawnPoints[0].rotation;
                }
            }

            // 12. Configurar LessonManager, HUD y Lesson06ParkingController
            SetupLessonControllers(scene, car09, lessonData, spawnPoints, exemptZone,
                trigger45, trigger90, triggerParallel, hl45, hl90, hlParallel, screenFader);

            // 13. Guardar escena y assets
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("=================================================================");
            Debug.Log("✅ [Lesson06ParkingSetup] ¡Lección 6 configurada exitosamente!");
            Debug.Log($"   Escena: {TARGET_SCENE_PATH}");
            Debug.Log($"   Asset:  {LESSON_06_PATH}");
            Debug.Log("=================================================================");
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
            if (!AssetDatabase.IsValidFolder(CURRICULUM_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Curriculum");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Scripts/Lessons/Steps"))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons", "Steps");
            }
            if (!AssetDatabase.IsValidFolder(STEPS_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Scripts/Lessons/Steps", "Lesson06");
            }
        }

        public static LessonDefinitionSO CreateOrUpdateLesson06Assets()
        {
            EnsureDirectories();

            LessonStepSO step1 = CreateOrUpdateStep("Lesson06_Step01_HazardLightsApproach",
                "Luces de Emergencia",
                "Presiona el botón rojo de balizas (triángulo) para advertir tu maniobra.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ActivateHazardLights,
                0f,
                CockpitControlFlags.All,
                "HazardButton");

            LessonStepSO step2 = CreateOrUpdateStep("Lesson06_Step02_DiagonalParking45",
                "Maniobra a 45° (Espiga)",
                "Avanza y entra de frente centrado en el recuadro amarillo hasta detenerte.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ParkDiagonal45,
                0f,
                CockpitControlFlags.All,
                "");

            LessonStepSO step3 = CreateOrUpdateStep("Lesson06_Step03_SecureDiagonal45",
                "Asegurar en Dársena 45°",
                "Coloca la palanca en Parking (P) y sube firmemente el freno de mano.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SecureDiagonal45,
                0f,
                CockpitControlFlags.All,
                "VRGearShifter, VRHandbrake");

            LessonStepSO step4 = CreateOrUpdateStep("Lesson06_Step04_BayParking90Reverse",
                "Maniobra a 90° (Culata)",
                "Pasa la dársena, pon Reversa (R) y estaciona de culata dentro del recuadro amarillo.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ParkBay90Reverse,
                0f,
                CockpitControlFlags.All,
                "");

            LessonStepSO step5 = CreateOrUpdateStep("Lesson06_Step05_SecureBay90",
                "Asegurar en Batería 90°",
                "Coloca la palanca en Parking (P) y sube firmemente el freno de mano.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SecureBay90,
                0f,
                CockpitControlFlags.All,
                "VRGearShifter, VRHandbrake");

            LessonStepSO step6 = CreateOrUpdateStep("Lesson06_Step06_ParallelParkingRight",
                "Maniobra en Paralelo",
                "Avanza por la calle, alinéate al auto delantero y maniobra marcha atrás hasta el recuadro amarillo.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.ParkParallelRight,
                0f,
                CockpitControlFlags.All,
                "");

            LessonStepSO step7 = CreateOrUpdateStep("Lesson06_Step07_SecurePark",
                "Asegurar y Finalizar",
                "Apaga las balizas, coloca Parking (P) y sube firmemente el freno de mano.",
                LessonStepCompletionMode.ActionRequired,
                LessonActionType.SecureParkDeactivateHazards,
                0f,
                CockpitControlFlags.All,
                "HazardButton, VRGearShifter, VRHandbrake");

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_06_PATH);
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                AssetDatabase.CreateAsset(lesson, LESSON_06_PATH);
            }

            lesson.lessonId = "LESSON_06";
            lesson.lessonTitle = "Lección 6: Maniobras de Estacionamiento";
            lesson.steps = new List<LessonStepSO> { step1, step2, step3, step4, step5, step6, step7 };

            EditorUtility.SetDirty(lesson);
            AssetDatabase.SaveAssets();
            return lesson;
        }

        private static LessonStepSO CreateOrUpdateStep(string fileName, string title, string instruction,
            LessonStepCompletionMode mode, LessonActionType requiredAction, float actionNumericParameter,
            CockpitControlFlags allowedControls, string highlightTarget)
        {
            string path = $"{STEPS_DIR}/{fileName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                AssetDatabase.CreateAsset(step, path);
            }

            step.stepTitle = title;
            step.instructionText = instruction;
            step.completionMode = mode;
            step.requiredAction = requiredAction;
            step.actionNumericParameter = actionNumericParameter;
            step.allowedControls = allowedControls;
            step.highlightTargetName = highlightTarget;

            EditorUtility.SetDirty(step);
            return step;
        }

        private static void EnsureClonedSceneExists()
        {
            if (!File.Exists(TARGET_SCENE_PATH))
            {
                string source = File.Exists(ORIGINAL_SCENE_PATH) ? ORIGINAL_SCENE_PATH : Lesson01SceneSetup.ORIGINAL_SCENE_PATH;
                Debug.Log($"[Lesson06ParkingSetup] 📋 Clonando escena base desde {source} hacia {TARGET_SCENE_PATH}...");
                File.Copy(source, TARGET_SCENE_PATH, true);
                AssetDatabase.ImportAsset(TARGET_SCENE_PATH, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static GameObject EnsureCar09InScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.Contains("Car09") || root.GetComponent<VehicleController>() != null)
                {
                    return root;
                }
            }
            return UnityEngine.Object.FindAnyObjectByType<VehicleController>()?.gameObject;
        }

        /// <summary>
        /// Elimina vehículos decorativos residuales de la ciudad en la calle X=120 para despejar el área de maniobras.
        /// </summary>
        private static void CleanResidualVehiclesInStreet(Scene scene, GameObject car09)
        {
            var toDestroy = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                FindResidualVehiclesRecursive(root, car09, toDestroy);
            }

            int count = toDestroy.Count;
            foreach (var go in toDestroy)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            if (count > 0)
            {
                Debug.Log($"[Lesson06ParkingSetup] 🧹 Se eliminaron {count} vehículos residuales para despejar la calle X=120.");
            }
        }

        private static void FindResidualVehiclesRecursive(GameObject obj, GameObject car09, List<GameObject> toDestroy)
        {
            if (obj == null || obj == car09) return;
            if (obj.name == "Parking_Stations_Container" || obj.transform.IsChildOf(car09.transform)) return;

            if (obj.name.StartsWith("Vehicle_") || obj.name.StartsWith("Vehicle") || obj.name.Contains("Car08") || obj.name.Contains("Car07"))
            {
                Vector3 pos = obj.transform.position;
                if (pos.x >= 110f && pos.x <= 130f && pos.z >= 45f && pos.z <= 130f)
                {
                    toDestroy.Add(obj);
                    return;
                }
            }

            foreach (Transform child in obj.transform)
            {
                FindResidualVehiclesRecursive(child.gameObject, car09, toDestroy);
            }
        }

        private static TurnSignalExemptZone SetupTurnSignalExemptZone(Scene scene)
        {
            GameObject exemptObj = GameObject.Find("TurnSignalExempt_ParkingZone");
            if (exemptObj == null)
            {
                exemptObj = new GameObject("TurnSignalExempt_ParkingZone");
                SceneManager.MoveGameObjectToScene(exemptObj, scene);
            }

            int laneZoneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (laneZoneLayer >= 0)
            {
                exemptObj.layer = laneZoneLayer;
            }

            // Ubicada cubriendo la playa de estacionamiento y el tramo de calle exterior (X=190 a 250, Z=60 a 120)
            exemptObj.transform.position = new Vector3(220f, 1.5f, 90f);

            BoxCollider box = exemptObj.GetComponent<BoxCollider>();
            if (box == null) box = exemptObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(60f, 5f, 60f);

            TurnSignalExemptZone zone = exemptObj.GetComponent<TurnSignalExemptZone>();
            if (zone == null) zone = exemptObj.AddComponent<TurnSignalExemptZone>();

            EditorUtility.SetDirty(exemptObj);
            return zone;
        }

        private static Material EnsureYellowZoneMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(YELLOW_MATERIAL_PATH);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = "M_Parking_YellowZone";
                AssetDatabase.CreateAsset(mat, YELLOW_MATERIAL_PATH);
            }

            Color baseCol = new Color(1.0f, 0.85f, 0.0f, 0.82f);
            mat.color = baseCol;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1.0f, 0.85f, 0.1f) * 1.8f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildParkingStations(Scene scene,
            out ParkingZoneTrigger trigger45, out ParkingZoneTrigger trigger90, out ParkingZoneTrigger triggerParallel,
            out GameObject hl45, out GameObject hl90, out GameObject hlParallel)
        {
            GameObject stationsContainer = GameObject.Find("Parking_Stations_Container");
            if (stationsContainer == null)
            {
                stationsContainer = new GameObject("Parking_Stations_Container");
                SceneManager.MoveGameObjectToScene(stationsContainer, scene);
            }

            GameObject conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);
            GameObject carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(STATIC_CAR_PREFAB_PATH);
            Material yellowMat = EnsureYellowZoneMaterial();
            Material auraMat = EnsureAuraMaterial();

            // -------------------------------------------------------------
            // Estación 1: Dársena a 45° (Espiga)
            // -------------------------------------------------------------
            GameObject station45Obj = EnsureChild(stationsContainer.transform, "Station_01_Diagonal45", new Vector3(123.5f, 0f, 105f), Quaternion.Euler(0f, 45f, 0f));
            trigger45 = station45Obj.GetComponent<ParkingZoneTrigger>();
            if (trigger45 == null) trigger45 = station45Obj.AddComponent<ParkingZoneTrigger>();
            trigger45.StationType = ParkingStationType.Diagonal45;
            BoxCollider col45 = station45Obj.GetComponent<BoxCollider>();
            col45.isTrigger = true;
            col45.center = new Vector3(0f, 1f, 0f);
            col45.size = new Vector3(3.2f, 2.5f, 6.0f);

            hl45 = EnsureGroundQuad(station45Obj.transform, "GroundVisual_YellowZone_45", new Vector2(3.0f, 5.5f), yellowMat);
            EnsureDirectionArrowOnGround(station45Obj.transform, "GroundVisual_Arrow_45", ParkingStationType.Diagonal45);
            EnsureAuraPerimeter(station45Obj.transform, "GroundVisual_Aura_45", new Vector2(3.0f, 5.5f), auraMat, hl45.transform.localPosition);
            SpawnConesAround(station45Obj.transform, conePrefab, new Vector2(3.2f, 6.0f));
            SpawnStaticCar(station45Obj.transform, carPrefab, new Vector3(-3.2f, 0f, 0f), Quaternion.identity);
            SpawnStaticCar(station45Obj.transform, carPrefab, new Vector3(3.2f, 0f, 0f), Quaternion.identity);

            // -------------------------------------------------------------
            // Estación 2: Dársena a 90° de Culata (Perpendicular)
            // -------------------------------------------------------------
            GameObject station90Obj = EnsureChild(stationsContainer.transform, "Station_02_Bay90Reverse", new Vector3(123.5f, 0f, 88f), Quaternion.Euler(0f, 90f, 0f));
            trigger90 = station90Obj.GetComponent<ParkingZoneTrigger>();
            if (trigger90 == null) trigger90 = station90Obj.AddComponent<ParkingZoneTrigger>();
            trigger90.StationType = ParkingStationType.Bay90Reverse;
            BoxCollider col90 = station90Obj.GetComponent<BoxCollider>();
            col90.isTrigger = true;
            col90.center = new Vector3(0f, 1f, 0f);
            col90.size = new Vector3(3.2f, 2.5f, 6.0f);

            hl90 = EnsureGroundQuad(station90Obj.transform, "GroundVisual_YellowZone_90", new Vector2(3.0f, 5.5f), yellowMat);
            EnsureDirectionArrowOnGround(station90Obj.transform, "GroundVisual_Arrow_90", ParkingStationType.Bay90Reverse);
            EnsureAuraPerimeter(station90Obj.transform, "GroundVisual_Aura_90", new Vector2(3.0f, 5.5f), auraMat, hl90.transform.localPosition);
            SpawnConesAround(station90Obj.transform, conePrefab, new Vector2(3.2f, 6.0f));
            SpawnStaticCar(station90Obj.transform, carPrefab, new Vector3(-3.2f, 0f, 0f), Quaternion.identity);
            SpawnStaticCar(station90Obj.transform, carPrefab, new Vector3(3.2f, 0f, 0f), Quaternion.identity);

            // -------------------------------------------------------------
            // Estación 3: Dársena en Paralelo al Cordón Derecho
            // -------------------------------------------------------------
            GameObject stationParallelObj = EnsureChild(stationsContainer.transform, "Station_03_ParallelRight", new Vector3(122.5f, 0f, 72f), Quaternion.Euler(0f, 180f, 0f));
            triggerParallel = stationParallelObj.GetComponent<ParkingZoneTrigger>();
            if (triggerParallel == null) triggerParallel = stationParallelObj.AddComponent<ParkingZoneTrigger>();
            triggerParallel.StationType = ParkingStationType.ParallelRight;
            BoxCollider colParallel = stationParallelObj.GetComponent<BoxCollider>();
            colParallel.isTrigger = true;
            colParallel.center = new Vector3(0f, 1f, 0f);
            colParallel.size = new Vector3(3.0f, 2.5f, 7.5f);

            hlParallel = EnsureGroundQuad(stationParallelObj.transform, "GroundVisual_YellowZone_Parallel", new Vector2(2.8f, 6.5f), yellowMat);
            EnsureDirectionArrowOnGround(stationParallelObj.transform, "GroundVisual_Arrow_Parallel", ParkingStationType.ParallelRight);
            EnsureAuraPerimeter(stationParallelObj.transform, "GroundVisual_Aura_Parallel", new Vector2(2.8f, 6.5f), auraMat, hlParallel.transform.localPosition);
            // Autos estáticos delimitando el cajón paralelo: delantero y trasero
            SpawnStaticCar(stationParallelObj.transform, carPrefab, new Vector3(0f, 0f, 6.5f), Quaternion.identity);
            SpawnStaticCar(stationParallelObj.transform, carPrefab, new Vector3(0f, 0f, -6.5f), Quaternion.identity);
            SpawnConesAlongLine(stationParallelObj.transform, conePrefab, new Vector3(1.6f, 0f, -4.5f), new Vector3(1.6f, 0f, 4.5f), 3);
        }

        private static GameObject EnsureGroundQuad(Transform parent, string name, Vector2 size, Material mat)
        {
            Transform existing = parent.Find(name);
            GameObject quad;
            if (existing != null)
            {
                quad = existing.gameObject;
            }
            else
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                quad.transform.SetParent(parent, false);

                Collider col = quad.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }

            quad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
            }

            // Luz suave para emitir iluminación visible sobre el asfalto en VR
            EnsureGroundZoneLight(quad.transform);

            EditorUtility.SetDirty(quad);
            return quad;
        }

        private static void EnsureGroundZoneLight(Transform quadTransform)
        {
            Transform lightTr = quadTransform.Find("GroundVisual_Light");
            GameObject lightObj;
            if (lightTr == null)
            {
                lightObj = new GameObject("GroundVisual_Light");
                lightObj.transform.SetParent(quadTransform, false);
            }
            else
            {
                lightObj = lightTr.gameObject;
            }

            // En un Quad rotado a 90° en X, -Z local apunta hacia arriba (+Y en espacio mundial)
            lightObj.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            lightObj.transform.localRotation = Quaternion.identity;

            Light l = lightObj.GetComponent<Light>();
            if (l == null) l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 7.0f;
            l.color = new Color(1.0f, 0.85f, 0.2f);
            l.intensity = 2.0f;
            l.shadows = LightShadows.None;
        }

        private static Material EnsureAuraMaterial()
        {
            const string matPath = "Assets/Environment/Materials/M_Parking_AuraPerimeter.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Custom/ParkingAura") ?? Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(shader);
                mat.name = "M_Parking_AuraPerimeter";
                mat.SetColor("_BaseColor", new Color(1.0f, 0.82f, 0.08f, 1.0f));
                if (mat.HasProperty("_Intensity")) mat.SetFloat("_Intensity", 2.6f);
                if (mat.HasProperty("_FadePower")) mat.SetFloat("_FadePower", 1.5f);
                if (mat.HasProperty("_GroundGlowBoost")) mat.SetFloat("_GroundGlowBoost", 3.0f);
                if (mat.HasProperty("_WaveSpeed")) mat.SetFloat("_WaveSpeed", 3.0f);
                if (mat.HasProperty("_WaveFreq")) mat.SetFloat("_WaveFreq", 10.0f);
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static GameObject EnsureAuraPerimeter(Transform parent, string name, Vector2 size, Material mat, Vector3 localPos)
        {
            Transform existing = parent.Find(name);
            GameObject auraObj;
            if (existing != null)
            {
                auraObj = existing.gameObject;
            }
            else
            {
                auraObj = new GameObject(name);
                auraObj.transform.SetParent(parent, false);
            }

            auraObj.transform.localPosition = new Vector3(localPos.x, 0f, localPos.z);
            auraObj.transform.localRotation = Quaternion.identity;
            auraObj.transform.localScale = Vector3.one;

            ParkingAuraPerimeter aura = auraObj.GetComponent<ParkingAuraPerimeter>();
            if (aura == null) aura = auraObj.AddComponent<ParkingAuraPerimeter>();
            aura.Size = size;
            aura.Height = 0.60f;
            aura.AuraMaterial = mat;
            aura.BuildAuraMesh();

            EditorUtility.SetDirty(auraObj);
            return auraObj;
        }

        private static Material EnsureArrowMaterial()
        {
            const string matPath = "Assets/Environment/Materials/M_Parking_ArrowBlack.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = "M_Parking_ArrowBlack";
                // Color carbón vial oscuro para máximo contraste sobre el asfalto y el cuadro amarillo
                Color darkColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
                mat.color = darkColor;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", darkColor);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.2f);
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static GameObject EnsureDirectionArrowOnGround(Transform parent, string name, ParkingStationType stationType)
        {
            Transform existing = parent.Find(name);
            GameObject arrowObj;
            if (existing != null)
            {
                arrowObj = existing.gameObject;
            }
            else
            {
                arrowObj = new GameObject(name);
                arrowObj.transform.SetParent(parent, false);
            }

            // Ubicado a 1 cm sobre el cuadro amarillo (Y=0.04)
            arrowObj.transform.localPosition = new Vector3(0f, 0.04f, 0f);

            // Para Bay90Reverse la flecha apunta hacia la calle (-Z local, hacia la salida del cajón)
            arrowObj.transform.localRotation = (stationType == ParkingStationType.Bay90Reverse)
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;
            arrowObj.transform.localScale = Vector3.one;

            MeshFilter mf = arrowObj.GetComponent<MeshFilter>();
            if (mf == null) mf = arrowObj.AddComponent<MeshFilter>();

            MeshRenderer mr = arrowObj.GetComponent<MeshRenderer>();
            if (mr == null) mr = arrowObj.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh();
            mesh.name = "M_ParkingArrowMesh";

            float shaftW = 0.38f;
            float shaftL = 1.4f;
            float headW = 1.0f;
            float headL = 0.9f;
            float totalL = shaftL + headL;
            float zStart = -totalL * 0.5f;
            float zJoint = zStart + shaftL;
            float zTip = zStart + totalL;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-shaftW * 0.5f, 0f, zStart),
                new Vector3(shaftW * 0.5f, 0f, zStart),
                new Vector3(-shaftW * 0.5f, 0f, zJoint),
                new Vector3(shaftW * 0.5f, 0f, zJoint),
                new Vector3(-headW * 0.5f, 0f, zJoint),
                new Vector3(headW * 0.5f, 0f, zJoint),
                new Vector3(0f, 0f, zTip)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,
                1, 2, 3,
                4, 6, 2,
                2, 6, 3,
                3, 6, 5
            };

            Vector2[] uvs = new Vector2[7];
            for (int i = 0; i < 7; i++)
            {
                uvs[i] = new Vector2(vertices[i].x, vertices[i].z);
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;

            Material arrowMat = EnsureArrowMaterial();
            if (arrowMat != null)
            {
                mr.sharedMaterial = arrowMat;
            }

            return arrowObj;
        }

        private static void SpawnConesAround(Transform parent, GameObject conePrefab, Vector2 size)
        {
            if (conePrefab == null) return;
            Vector3[] offsets = new Vector3[]
            {
                new Vector3(-size.x * 0.5f, 0f, -size.y * 0.5f),
                new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f),
                new Vector3(-size.x * 0.5f, 0f, size.y * 0.5f),
                new Vector3(size.x * 0.5f, 0f, size.y * 0.5f)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                string cName = $"Cone_{i}";
                Transform existing = parent.Find(cName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, parent);
                cone.name = cName;
                cone.transform.localPosition = offsets[i];
                cone.transform.localRotation = Quaternion.identity;

                EnsureObstacleDetector(cone, ObstacleKind.Cone);
            }
        }

        private static void SpawnConesAlongLine(Transform parent, GameObject conePrefab, Vector3 start, Vector3 end, int count)
        {
            if (conePrefab == null || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : (float)i / (count - 1);
                Vector3 pos = Vector3.Lerp(start, end, t);

                string cName = $"CurbCone_{i}";
                Transform existing = parent.Find(cName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, parent);
                cone.name = cName;
                cone.transform.localPosition = pos;
                cone.transform.localRotation = Quaternion.identity;

                EnsureObstacleDetector(cone, ObstacleKind.Cone);
            }
        }

        private static void SpawnStaticCar(Transform parent, GameObject carPrefab, Vector3 localPos, Quaternion localRot)
        {
            if (carPrefab == null) return;

            string carName = $"StaticCar_{localPos.x:F0}_{localPos.z:F0}";
            Transform existing = parent.Find(carName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            GameObject car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, parent);
            car.name = carName;
            car.transform.localPosition = localPos;
            car.transform.localRotation = localRot;

            EnsureObstacleDetector(car, ObstacleKind.Vehicle);
        }

        private static void EnsureObstacleDetector(GameObject go, ObstacleKind kind)
        {
            Collider col = go.GetComponent<Collider>() ?? go.GetComponentInChildren<Collider>();
            if (col == null)
            {
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.size = (kind == ObstacleKind.Cone) ? new Vector3(0.4f, 0.7f, 0.4f) : new Vector3(2f, 1.5f, 4.5f);
                box.center = (kind == ObstacleKind.Cone) ? new Vector3(0f, 0.35f, 0f) : new Vector3(0f, 0.75f, 0f);
            }

            ObstacleCollisionDetector detector = go.GetComponent<ObstacleCollisionDetector>();
            if (detector == null) detector = go.AddComponent<ObstacleCollisionDetector>();

            var so = new SerializedObject(detector);
            SerializedProperty kindProp = so.FindProperty("m_Kind");
            if (kindProp != null) kindProp.enumValueIndex = (int)kind;
            so.ApplyModifiedProperties();
        }

        private static GameObject EnsureChild(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }
            child.position = pos;
            child.rotation = rot;
            return child.gameObject;
        }

        private static Transform[] EnsureSpawnPoints(Scene scene)
        {
            GameObject container = GameObject.Find("Lesson06_SpawnPoints");
            if (container == null)
            {
                container = new GameObject("Lesson06_SpawnPoints");
                SceneManager.MoveGameObjectToScene(container, scene);
            }

            Transform[] spawns = new Transform[5];

            // Paso 1: Entrada a la playa y balizas
            spawns[0] = EnsureChildTransform(container.transform, "SpawnPoint_01_HazardApproach",
                new Vector3(230.30f, 0.02f, 89.40f), Quaternion.Euler(0f, 270f, 0f));

            // Paso 2: Entrada a dársena de 45°
            spawns[1] = EnsureChildTransform(container.transform, "SpawnPoint_02_Diagonal45",
                new Vector3(230.30f, 0.02f, 89.40f), Quaternion.Euler(0f, 270f, 0f));

            // Paso 3: Inicio de pasillo para ir a dársena de 90° de culata
            spawns[2] = EnsureChildTransform(container.transform, "SpawnPoint_03_Bay90Reverse",
                new Vector3(228.00f, 0.02f, 91.50f), Quaternion.Euler(0f, 270f, 0f));

            // Paso 4: En la calle exterior en dirección a la dársena en paralelo (posición manual del usuario)
            spawns[3] = EnsureChildTransform(container.transform, "SpawnPoint_04_ParallelApproach",
                new Vector3(241.20f, 0.02f, 71.60f), Quaternion.Euler(0f, 0f, 0f));

            // Paso 5: Auto estacionado listo para asegurar
            spawns[4] = EnsureChildTransform(container.transform, "SpawnPoint_05_SecurePark",
                new Vector3(244.89f, 0.02f, 90.17f), Quaternion.Euler(0f, 0f, 0f));

            return spawns;
        }

        private static Transform EnsureChildTransform(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }
            child.position = pos;
            child.rotation = rot;
            return child;
        }

        private static void SetupLessonControllers(Scene scene, GameObject car09, LessonDefinitionSO lessonData,
            Transform[] spawnPoints, TurnSignalExemptZone exemptZone,
            ParkingZoneTrigger trigger45, ParkingZoneTrigger trigger90, ParkingZoneTrigger triggerParallel,
            GameObject hl45, GameObject hl90, GameObject hlParallel,
            VRScreenFader screenFader)
        {
            GameObject managerObj = GameObject.Find("Lesson_Manager");
            if (managerObj == null)
            {
                managerObj = new GameObject("Lesson_Manager");
                SceneManager.MoveGameObjectToScene(managerObj, scene);
            }

            LessonManager manager = managerObj.GetComponent<LessonManager>();
            if (manager == null) manager = managerObj.AddComponent<LessonManager>();

            LessonActionMonitor actionMonitor = managerObj.GetComponent<LessonActionMonitor>();
            if (actionMonitor == null) actionMonitor = managerObj.AddComponent<LessonActionMonitor>();

            VehicleController vc = car09.GetComponent<VehicleController>();
            LessonHUDPanel hud = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            DriverSeatFollower seatFollower = UnityEngine.Object.FindAnyObjectByType<DriverSeatFollower>();

            lessonData = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_06_PATH);

            // Configurar LessonManager
            var so = new SerializedObject(manager);
            so.FindProperty("m_ActiveLesson").objectReferenceValue = lessonData;
            so.FindProperty("m_AutoStartOnStart").boolValue = true;
            so.FindProperty("m_VehicleRoot").objectReferenceValue = car09.transform;
            so.FindProperty("m_VehicleController").objectReferenceValue = vc;
            so.FindProperty("m_HUDPanel").objectReferenceValue = hud;
            so.FindProperty("m_ActionMonitor").objectReferenceValue = actionMonitor;

            SerializedProperty currProp = so.FindProperty("m_Curriculum");
            if (currProp != null)
            {
                var list = new List<LessonDefinitionSO>();
                for (int l = 1; l <= 6; l++)
                {
                    string path = $"Assets/Scripts/Lessons/Curriculum/LessonData_0{l}.asset";
                    LessonDefinitionSO def = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(path);
                    if (def != null) list.Add(def);
                }
                currProp.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    currProp.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
                }
            }
            so.ApplyModifiedProperties();

            // Configurar Lesson06ParkingController
            Lesson06ParkingController ctrl = managerObj.GetComponent<Lesson06ParkingController>();
            if (ctrl == null) ctrl = managerObj.AddComponent<Lesson06ParkingController>();

            var soCtrl = new SerializedObject(ctrl);
            soCtrl.FindProperty("m_LessonManager").objectReferenceValue = manager;
            soCtrl.FindProperty("m_VehicleController").objectReferenceValue = vc;
            soCtrl.FindProperty("m_HUDPanel").objectReferenceValue = hud;
            soCtrl.FindProperty("m_ScreenFader").objectReferenceValue = screenFader;
            soCtrl.FindProperty("m_SeatFollower").objectReferenceValue = seatFollower;
            soCtrl.FindProperty("m_ExemptZone").objectReferenceValue = exemptZone;
            soCtrl.FindProperty("m_Diagonal45Zone").objectReferenceValue = trigger45;
            soCtrl.FindProperty("m_Bay90Zone").objectReferenceValue = trigger90;
            soCtrl.FindProperty("m_ParallelZone").objectReferenceValue = triggerParallel;
            soCtrl.FindProperty("m_GroundHighlight45").objectReferenceValue = hl45;
            soCtrl.FindProperty("m_GroundHighlight90").objectReferenceValue = hl90;
            soCtrl.FindProperty("m_GroundHighlightParallel").objectReferenceValue = hlParallel;

            SerializedProperty spawnsProp = soCtrl.FindProperty("m_StepSpawnPoints");
            if (spawnsProp != null && spawnPoints != null)
            {
                spawnsProp.arraySize = spawnPoints.Length;
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    spawnsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
                }
            }
            soCtrl.ApplyModifiedProperties();

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(ctrl);
        }
    }
}
