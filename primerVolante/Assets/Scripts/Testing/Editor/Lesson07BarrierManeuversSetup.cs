using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor automatizada e idempotente para configurar la Lección 7:
    /// "Dominio del Volante y Slalom entre Conos (Slalom y Reversa con Espejos)".
    /// - Clona de forma segura Lesson_01_BasicDriving.unity a Assets/Scenes/Lessons/Lesson_07_BarrierManeuvers.unity.
    /// - Genera los ScriptableObjects didácticos (LessonData_07.asset y los 7 pasos).
    /// - Construye la pista: fila recta de 6 conos (X=60), vallas transversales alternadas que obligan el zigzag y un
    ///   corredor de vallas (LessonBoundary) alrededor; todo guardado en la escena (nada se arma en runtime).
    /// - Configura los recuadros amarillos de inicio (Z = 14.3) y de llegada (Z = 84), de 3.6 x 7 m.
    /// - Agrega las zonas de exención de contramano y de giro sin señalizar sobre toda la pista.
    /// - Ensambla el Lesson07BarrierManeuversController en la escena.
    /// - Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [InitializeOnLoad]
    public static class Lesson07BarrierManeuversSetup
    {
        static Lesson07BarrierManeuversSetup()
        {
            EditorApplication.delayCall += AutoSetupIfMissing;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += AutoSetupIfMissing;
            }
        }

        private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.IsValid() && scene.path == TARGET_SCENE_PATH)
            {
                AutoSetupIfMissing();
            }
        }

        private static void AutoSetupIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.path == TARGET_SCENE_PATH)
            {
                bool needsUpdate = GameObject.Find(START_ZONE_NAME) == null || GameObject.Find(SLALOM_ROOT_NAME) == null;
                if (needsUpdate)
                {
                    Debug.Log("[Lesson07BarrierManeuversSetup] 🔄 Auto-ejecutando SetupLesson07() en escena activa...");
                    SetupLesson07();
                }
            }
        }
        public const string ORIGINAL_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        public const string TARGET_SCENE_DIR = "Assets/Scenes/Lessons";
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_07_BarrierManeuvers.unity";

        public const string CURRICULUM_DIR = "Assets/Scripts/Lessons/Curriculum";
        public const string STEPS_DIR = "Assets/Scripts/Lessons/Steps/Lesson07";
        public const string LESSON_07_PATH = "Assets/Scripts/Lessons/Curriculum/LessonData_07.asset";

        public const string BARRIER_PREFAB_PATH = "Assets/Prefabs/Environment/LessonBarrier.prefab";
        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";
        public const string YELLOW_MATERIAL_PATH = "Assets/Environment/Materials/M_Parking_YellowZone.mat";

        public const string SLALOM_ROOT_NAME = "Slalom_Course";
        public const string LEGACY_SLALOM_ROOT_NAME = "Slalom_Barriers";
        public const string EXEMPT_ZONE_NAME = "Slalom_ExemptZone";
        public const string CORRIDOR_NAME = "Corridor";

        // Geometría de la pista (mundo). La calzada va de X 53.05 a 66.95 con el eje en X=60.
        public const float CONE_X = 60f;
        public static readonly float[] ConeZs = { 24f, 34f, 44f, 54f, 64f, 74f };
        public const float CORRIDOR_LEFT_X = 55f;
        public const float CORRIDOR_RIGHT_X = 65f;
        public const float CORRIDOR_START_Z = 8f;
        public const float CORRIDOR_END_Z = 90f;
        public const float CONE_CLEARANCE = 0.3f;     // hueco entre el extremo de la valla transversal y el eje del cono
        public static readonly Vector3 START_ZONE_CENTER = new Vector3(62.4f, 0.05f, 14.3f);
        public static readonly Vector3 FORWARD_ZONE_CENTER = new Vector3(57.6f, 0.05f, 84f);
        public static readonly Vector3 ZONE_SIZE = new Vector3(3.6f, 2f, 7f);
        public const string START_ZONE_NAME = "ParkingZone_Start";
        public const string FORWARD_FINISH_ZONE_NAME = "ParkingZone_ForwardFinish";

        [MenuItem("Tools/Primer Volante/Lessons/Setup Lesson 7 Scene")]
        public static void SetupLesson07()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚀 [Lesson07BarrierManeuversSetup] Iniciando configuración de Lección 7...");
            Debug.Log("=================================================================");

            EnsureDirectories();

            // 1. Crear o actualizar los ScriptableObjects de la Lección 7
            LessonDefinitionSO lessonData = CreateOrUpdateLesson07Assets();

            // 2. Asegurar que la escena clonada exista
            EnsureClonedSceneExists();

            // 3. Abrir la escena de la Lección 7
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != TARGET_SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ No se pudo abrir la escena: {TARGET_SCENE_PATH}");
                return;
            }

            // 4. Asegurar Car09 y sus componentes de cabina (posición inicial al inicio de la calle)
            GameObject car09 = GameObject.Find("Car09");
            if (car09 != null)
            {
                car09.transform.position = new Vector3(START_ZONE_CENTER.x, car09.transform.position.y, START_ZONE_CENTER.z);
                car09.transform.rotation = Quaternion.identity;
                EditorUtility.SetDirty(car09);
            }

            // 5. Configurar Lesson_Manager con LessonData_07
            GameObject mgrObj = GameObject.Find("Lesson_Manager");
            if (mgrObj != null)
            {
                LessonManager lessonMgr = mgrObj.GetComponent<LessonManager>();
                if (lessonMgr != null)
                {
                    var soMgr = new SerializedObject(lessonMgr);
                    SerializedProperty activeProp = soMgr.FindProperty("m_ActiveLesson");
                    if (activeProp != null) activeProp.objectReferenceValue = lessonData;
                    soMgr.ApplyModifiedProperties();
                    EditorUtility.SetDirty(lessonMgr);
                }
            }

            // 6. Limpiar elementos residuales de Lección 1 (como StopLine_Trigger viejo)
            CleanResidualObjects(scene);

            // 7. Construir Dársenas de Inicio y Llegada (Recuadros amarillos con ParkingZoneTrigger)
            ParkingZoneTrigger startZone, forwardZone;
            GameObject startHighlight, forwardHighlight;
            BuildParkingZones(scene, out startZone, out forwardZone, out startHighlight, out forwardHighlight);

            // 8. Construir la pista: conos, vallas transversales y corredor
            BuildSlalomCourse(scene, out List<ObstacleCollisionDetector> cones, out List<LessonBarrier> transverse, out List<LessonBoundary> boundaries);

            // 8b. Zonas de exención de contramano / giro sin señalizar sobre toda la pista
            BuildExemptZone(scene);

            // 9. Configurar Lesson07BarrierManeuversController
            SetupController(scene, startZone, forwardZone, startHighlight, forwardHighlight, cones, transverse, boundaries);

            // 10. Asegurar escena en EditorBuildSettings
            EnsureSceneInBuildSettings();

            // Las operaciones de Undo pendientes volverían a marcar la escena como sucia después de guardarla.
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("=================================================================");
            Debug.Log("✅ [Lesson07BarrierManeuversSetup] ¡Lección 7 configurada y guardada con éxito!");
            Debug.Log("=================================================================");
        }

        private static void EnsureDirectories()
        {
            if (!Directory.Exists(CURRICULUM_DIR)) Directory.CreateDirectory(CURRICULUM_DIR);
            if (!Directory.Exists(STEPS_DIR)) Directory.CreateDirectory(STEPS_DIR);
            if (!Directory.Exists(TARGET_SCENE_DIR)) Directory.CreateDirectory(TARGET_SCENE_DIR);
        }

        public static LessonDefinitionSO CreateOrUpdateLesson07Assets()
        {
            EnsureDirectories();

            var steps = new List<LessonStepSO>
            {
                // Paso 1: Comprobación y Giro del Volante
                CreateOrUpdateStep("Lesson07_Step01_SteeringGripTechnique",
                    "Comprobación y Giro del Volante",
                    "Gira el volante con fluidez hacia ambos lados (derecha e izquierda) para comprobar su recorrido y la respuesta del vehículo antes de iniciar la marcha.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.RotateSteeringWheel,
                    CockpitControlFlags.SteeringWheel,
                    "SteeringWheel_Pivot",
                    20f),

                // Paso 2: Encendido del Motor
                CreateOrUpdateStep("Lesson07_Step02_StartAndDrive",
                    "Encendido del Motor",
                    "Presiona el botón START para encender el motor del vehículo.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.StartEngine,
                    CockpitControlFlags.All,
                    "StartButton"),

                // Paso 3: Puesta en Marcha (Drive)
                CreateOrUpdateStep("Lesson07_Step03_ForwardSlalom",
                    "Puesta en Marcha (Drive)",
                    "Pisa y mantén presionado el pedal de freno, coloca la palanca de cambios en Drive (D) y baja el freno de mano.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.ShiftToDrive,
                    CockpitControlFlags.All,
                    "VRGearShifter"),

                // Paso 4: Slalom hacia Adelante entre Conos
                CreateOrUpdateStep("Lesson07_Step04_StopAndShiftReverse",
                    "Slalom hacia Adelante entre Conos",
                    "Avanza despacio (5-10 km/h) haciendo zigzag entre los conos: pasa cada uno por el lado libre de cada valla, sin tocar nada. Al final, detén el auto con el auto completo dentro del recuadro amarillo.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.SlalomForwardComplete,
                    CockpitControlFlags.All,
                    ""),

                // Paso 5: Detención y Cambio a Reversa
                CreateOrUpdateStep("Lesson07_Step05_MirrorHalfCarRule",
                    "Detención y Cambio a Reversa",
                    "¡Excelente slalom hacia adelante! Detén el auto por completo en la dársena. Pisa el freno y coloca la palanca de cambios en Reversa (R).",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.ShiftToReverse,
                    CockpitControlFlags.All,
                    "VRGearShifter"),

                // Paso 6: Slalom en Reversa con Espejos
                CreateOrUpdateStep("Lesson07_Step06_ReverseSlalom",
                    "Slalom en Reversa con Espejos",
                    "Retrocede despacio (5-10 km/h) aplicando la regla del medio auto: observa con atención el espejo del lado del cono y, cuando veas que pasó la mitad del auto, invierte el volante para esquivar el siguiente por el lado libre de cada valla. Detén el auto con el auto completo dentro del recuadro amarillo de inicio.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.SlalomReverseComplete,
                    CockpitControlFlags.All,
                    ""),

                // Paso 7: Asegurar el Vehículo
                CreateOrUpdateStep("Lesson07_Step07_SecureVehicle",
                    "Asegurar el Vehículo",
                    "Detén la marcha, coloca la palanca de cambios en Parking (P) y acciona el freno de mano hacia arriba para finalizar la lección exitosamente.",
                    LessonStepCompletionMode.ActionRequired,
                    LessonActionType.SecureStopAndPark,
                    CockpitControlFlags.All,
                    "VRGearShifter")
            };

            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_07_PATH);
            if (lesson == null)
            {
                lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                AssetDatabase.CreateAsset(lesson, LESSON_07_PATH);
            }

            lesson.lessonId = "LESSON_07";
            lesson.lessonTitle = "Lección 7: Dominio del Volante y Slalom entre Conos";
            lesson.steps = steps;

            EditorUtility.SetDirty(lesson);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Lesson07BarrierManeuversSetup] 📋 LessonData_07 actualizado con {steps.Count} pasos didácticos automáticos.");
            return lesson;
        }

        private static LessonStepSO CreateOrUpdateStep(string assetName, string title, string instruction,
            LessonStepCompletionMode mode, LessonActionType action, CockpitControlFlags controls,
            string highlightTarget = "", float numericParam = 0f)
        {
            string path = $"{STEPS_DIR}/{assetName}.asset";
            LessonStepSO step = AssetDatabase.LoadAssetAtPath<LessonStepSO>(path);
            if (step == null)
            {
                step = ScriptableObject.CreateInstance<LessonStepSO>();
                AssetDatabase.CreateAsset(step, path);
            }

            step.stepTitle = title;
            step.instructionText = instruction;
            step.completionMode = mode;
            step.requiredAction = action;
            step.allowedControls = controls;
            step.highlightTargetName = highlightTarget;
            step.actionNumericParameter = numericParam;
            // Las lecciones básicas mantienen el ambiente calmo de la escena (EnvironmentAudioVerification).
            step.ambienceIntensity = AmbienceIntensityOverride.Unchanged;

            EditorUtility.SetDirty(step);
            return step;
        }

        private static void EnsureClonedSceneExists()
        {
            if (File.Exists(TARGET_SCENE_PATH))
            {
                return;
            }

            Debug.Log($"[Lesson07BarrierManeuversSetup] 📋 Clonando escena {ORIGINAL_SCENE_PATH} -> {TARGET_SCENE_PATH}...");
            bool ok = AssetDatabase.CopyAsset(ORIGINAL_SCENE_PATH, TARGET_SCENE_PATH);
            if (!ok)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ Falló la clonación de la escena a {TARGET_SCENE_PATH}");
            }
        }

        private static void CleanResidualObjects(Scene scene)
        {
            string[] objectsToRemove = { "StopLine_Trigger", "Controlled_Intersection_T" };
            foreach (var name in objectsToRemove)
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null)
                {
                    Undo.DestroyObjectImmediate(obj);
                }
            }
        }

        private static void BuildParkingZones(Scene scene,
            out ParkingZoneTrigger startZone, out ParkingZoneTrigger forwardZone,
            out GameObject startHighlight, out GameObject forwardHighlight)
        {
            Material yellowMat = AssetDatabase.LoadAssetAtPath<Material>(YELLOW_MATERIAL_PATH);

            startZone = BuildZone(scene, START_ZONE_NAME, START_ZONE_CENTER, 30f, yellowMat, "GroundVisual_StartYellow", out startHighlight);
            forwardZone = BuildZone(scene, FORWARD_FINISH_ZONE_NAME, FORWARD_ZONE_CENTER, 35f, yellowMat, "GroundVisual_ForwardYellow", out forwardHighlight);
        }

        // Recuadro de 3.6 x 7 m: BoxCollider trigger + ParkingZoneTrigger + quad amarillo del mismo tamaño que el collider.
        private static ParkingZoneTrigger BuildZone(Scene scene, string zoneName, Vector3 center, float maxAlignmentDeg,
            Material yellowMat, string visualName, out GameObject highlight)
        {
            GameObject obj = GameObject.Find(zoneName);
            if (obj == null)
            {
                obj = new GameObject(zoneName);
                Undo.RegisterCreatedObjectUndo(obj, "Create " + zoneName);
                SceneManager.MoveGameObjectToScene(obj, scene);
            }
            obj.transform.SetPositionAndRotation(center, Quaternion.identity);

            BoxCollider box = obj.GetComponent<BoxCollider>();
            if (box == null) box = obj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = ZONE_SIZE;

            ParkingZoneTrigger zone = obj.GetComponent<ParkingZoneTrigger>();
            if (zone == null) zone = obj.AddComponent<ParkingZoneTrigger>();
            var so = new SerializedObject(zone);
            SerializedProperty type = so.FindProperty("m_StationType");
            if (type != null) type.enumValueIndex = (int)ParkingStationType.ParallelRight;
            SerializedProperty speed = so.FindProperty("m_MaxStopSpeedKmh");
            if (speed != null) speed.floatValue = 1.5f;
            SerializedProperty angle = so.FindProperty("m_MaxAlignmentAngleDeg");
            if (angle != null) angle.floatValue = maxAlignmentDeg;
            so.ApplyModifiedProperties();

            highlight = EnsureGroundVisual(obj.transform, visualName, new Vector3(ZONE_SIZE.x, ZONE_SIZE.z, 1f), yellowMat);
            EditorUtility.SetDirty(zone);
            return zone;
        }

        private static GameObject EnsureGroundVisual(Transform parent, string name, Vector3 scale, Material mat)
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
                Collider c = quad.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }

            quad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = scale;

            if (mat != null)
            {
                MeshRenderer mr = quad.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            return quad;
        }

        private static void BuildSlalomCourse(Scene scene,
            out List<ObstacleCollisionDetector> cones, out List<LessonBarrier> transverse, out List<LessonBoundary> boundaries)
        {
            cones = new List<ObstacleCollisionDetector>();
            transverse = new List<LessonBarrier>();
            boundaries = new List<LessonBoundary>();

            GameObject barrierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BARRIER_PREFAB_PATH);
            GameObject conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);
            if (barrierPrefab == null || conePrefab == null)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ Faltan prefabs: valla={barrierPrefab != null} cono={conePrefab != null}");
                return;
            }

            // Idempotente: se borra la pista vieja (y la de vallas alternadas anterior) y se recrea.
            foreach (string oldName in new[] { SLALOM_ROOT_NAME, LEGACY_SLALOM_ROOT_NAME })
            {
                GameObject old = GameObject.Find(oldName);
                if (old != null) Undo.DestroyObjectImmediate(old);
            }

            var root = new GameObject(SLALOM_ROOT_NAME);
            Undo.RegisterCreatedObjectUndo(root, "Create Slalom Course");
            SceneManager.MoveGameObjectToScene(root, scene);

            // Corredor: cuatro tramos (dos paredes y dos tapas) con LessonBoundary, cada uno con su spline recta.
            var corridorGo = new GameObject(CORRIDOR_NAME);
            corridorGo.transform.SetParent(root.transform, false);
            Transform corridor = corridorGo.transform;
            AddBoundary(corridor, "Wall_Left", barrierPrefab, new Vector3(CORRIDOR_LEFT_X, 0f, CORRIDOR_START_Z), new Vector3(CORRIDOR_LEFT_X, 0f, CORRIDOR_END_Z), boundaries);
            AddBoundary(corridor, "Wall_Right", barrierPrefab, new Vector3(CORRIDOR_RIGHT_X, 0f, CORRIDOR_START_Z), new Vector3(CORRIDOR_RIGHT_X, 0f, CORRIDOR_END_Z), boundaries);
            AddBoundary(corridor, "Cap_South", barrierPrefab, new Vector3(CORRIDOR_LEFT_X, 0f, CORRIDOR_START_Z), new Vector3(CORRIDOR_RIGHT_X, 0f, CORRIDOR_START_Z), boundaries);
            AddBoundary(corridor, "Cap_North", barrierPrefab, new Vector3(CORRIDOR_LEFT_X, 0f, CORRIDOR_END_Z), new Vector3(CORRIDOR_RIGHT_X, 0f, CORRIDOR_END_Z), boundaries);

            // Las piezas transversales arrancan donde termina el AlertTrigger de la pared, para que ni sus sólidos ni sus
            // AlertTrigger se superpongan con los de la pared (un roce cerca de la pared no dispara las dos a la vez).
            float wallTriggerHalf = MeasureAlertTriggerHalfThickness(corridor.Find("Wall_Left"));

            // Conos sobre el eje de la calzada y vallas transversales que cierran el lado opuesto al de paso:
            // cono 1 se pasa por la derecha (valla desde la pared izquierda), cono 2 por la izquierda, y así.
            for (int i = 0; i < ConeZs.Length; i++)
            {
                float z = ConeZs[i];
                GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, root.transform);
                cone.name = $"Cone_{i + 1:00}";
                cone.transform.SetPositionAndRotation(new Vector3(CONE_X, 0f, z), Quaternion.identity);
                ObstacleCollisionDetector detector = cone.GetComponentInChildren<ObstacleCollisionDetector>(true);
                if (detector != null) cones.Add(detector);
                else Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ {cone.name} no tiene ObstacleCollisionDetector");

                bool passRight = i % 2 == 0;
                string side = passRight ? "Left" : "Right";
                float wallX = passRight ? CORRIDOR_LEFT_X + wallTriggerHalf + 0.01f : CORRIDOR_RIGHT_X - wallTriggerHalf - 0.01f;
                float endX = passRight ? CONE_X - CONE_CLEARANCE : CONE_X + CONE_CLEARANCE;

                var group = new GameObject($"Transverse_{i + 1:00}_{side}");
                group.transform.SetParent(root.transform, false);

                // Dos piezas que cubren de la pared al extremo, sin superponerse entre sí ni con el cono.
                float span = Mathf.Abs(endX - wallX);
                float pieceWidth = span * 0.5f;
                float dir = passRight ? 1f : -1f;
                for (int p = 0; p < 2; p++)
                {
                    GameObject piece = (GameObject)PrefabUtility.InstantiatePrefab(barrierPrefab, group.transform);
                    piece.name = p == 0 ? "Piece_A" : "Piece_B";
                    float centerX = wallX + dir * (pieceWidth * (p + 0.5f));
                    piece.transform.SetPositionAndRotation(new Vector3(centerX, 0f, z), Quaternion.identity);

                    // Largo natural del sólido en X (mundo): se ajusta la escala X para que mida exactamente pieceWidth.
                    BoxCollider solid = piece.GetComponent<BoxCollider>();
                    float natural = solid != null ? solid.bounds.size.x : 0f;
                    if (natural > 0.01f)
                    {
                        Vector3 scale = piece.transform.localScale;
                        scale.x *= pieceWidth / natural;
                        piece.transform.localScale = scale;
                    }

                    LessonBarrier lb = piece.GetComponentInChildren<LessonBarrier>(true);
                    if (lb != null) transverse.Add(lb);
                }
            }

            Debug.Log($"[Lesson07BarrierManeuversSetup] 🚧 Pista: {cones.Count} conos, {transverse.Count} piezas transversales, {boundaries.Count} tramos de corredor.");
        }

        // Mitad del espesor (en X) del AlertTrigger de las vallas de una pared recta a lo largo de Z.
        private static float MeasureAlertTriggerHalfThickness(Transform wall)
        {
            if (wall != null)
            {
                foreach (BoxCollider b in wall.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (b.isTrigger) return b.bounds.size.x * 0.5f;
                }
            }

            Debug.LogError("[Lesson07BarrierManeuversSetup] ❌ No se pudo medir el AlertTrigger de la pared");
            return 0.4f;
        }

        private static void AddBoundary(Transform parent, string boundaryName, GameObject barrierPrefab, Vector3 from, Vector3 to, List<LessonBoundary> boundaries)
        {
            var go = new GameObject(boundaryName);
            go.transform.SetParent(parent, false);

            var container = go.AddComponent<SplineContainer>();
            var spline = new Spline(new[] { new BezierKnot(new float3(from.x, from.y, from.z)), new BezierKnot(new float3(to.x, to.y, to.z)) }, false);
            spline.SetTangentMode(TangentMode.Linear);
            container.Splines = new List<Spline> { spline };

            var boundary = go.AddComponent<LessonBoundary>();
            var so = new SerializedObject(boundary);
            so.FindProperty("m_BarrierPrefab").objectReferenceValue = barrierPrefab;
            so.FindProperty("m_SplineIndex").intValue = 0;
            so.ApplyModifiedProperties();

            if (!boundary.TryRebuildBarriers()) Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ No se pudieron generar las vallas de {boundaryName}");
            boundaries.Add(boundary);
        }

        // Una sola zona (capa LaneZone, trigger) con ambas exenciones: sin control de contramano ni de giro sin señalizar.
        private static void BuildExemptZone(Scene scene)
        {
            GameObject old = GameObject.Find(EXEMPT_ZONE_NAME);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var go = new GameObject(EXEMPT_ZONE_NAME);
            Undo.RegisterCreatedObjectUndo(go, "Create Slalom Exempt Zone");
            SceneManager.MoveGameObjectToScene(go, scene);

            float minX = 53f, maxX = 67f, minZ = 5f, maxZ = 93f;
            go.transform.position = new Vector3((minX + maxX) * 0.5f, 1f, (minZ + maxZ) * 0.5f);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(maxX - minX, 4f, maxZ - minZ);

            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (layer >= 0) go.layer = layer;
            else Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ No existe la capa {RoadLaneZone.LayerName}");

            go.AddComponent<WrongWayExemptZone>();
            go.AddComponent<TurnSignalExemptZone>();
        }

        private static void SetupController(Scene scene,
            ParkingZoneTrigger startZone, ParkingZoneTrigger forwardZone,
            GameObject startHighlight, GameObject forwardHighlight,
            List<ObstacleCollisionDetector> cones, List<LessonBarrier> transverse, List<LessonBoundary> boundaries)
        {
            GameObject ctrlObj = GameObject.Find("Lesson07_Controller");
            if (ctrlObj == null)
            {
                ctrlObj = new GameObject("Lesson07_Controller");
                Undo.RegisterCreatedObjectUndo(ctrlObj, "Create Lesson07 Controller");
                SceneManager.MoveGameObjectToScene(ctrlObj, scene);
            }

            Lesson07BarrierManeuversController ctrl = ctrlObj.GetComponent<Lesson07BarrierManeuversController>();
            if (ctrl == null) ctrl = ctrlObj.AddComponent<Lesson07BarrierManeuversController>();

            var so = new SerializedObject(ctrl);
            so.FindProperty("m_LessonManager").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<LessonManager>();
            so.FindProperty("m_VehicleController").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            so.FindProperty("m_HUDPanel").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<LessonHUDPanel>();
            so.FindProperty("m_ScreenFader").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            so.FindProperty("m_StartZone").objectReferenceValue = startZone;
            so.FindProperty("m_ForwardFinishZone").objectReferenceValue = forwardZone;
            so.FindProperty("m_StartZoneHighlight").objectReferenceValue = startHighlight;
            so.FindProperty("m_ForwardZoneHighlight").objectReferenceValue = forwardHighlight;

            AssignList(so.FindProperty("m_Cones"), cones);
            AssignList(so.FindProperty("m_TransverseBarriers"), transverse);
            AssignList(so.FindProperty("m_Boundaries"), boundaries);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ctrl);
        }

        private static void AssignList<T>(SerializedProperty prop, List<T> items) where T : UnityEngine.Object
        {
            prop.ClearArray();
            for (int i = 0; i < items.Count; i++)
            {
                prop.InsertArrayElementAtIndex(i);
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }

        private static void EnsureSceneInBuildSettings()
        {
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == TARGET_SCENE_PATH))
            {
                buildScenes.Add(new EditorBuildSettingsScene(TARGET_SCENE_PATH, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
                Debug.Log($"[Lesson07BarrierManeuversSetup] ➕ Escena {TARGET_SCENE_PATH} agregada a EditorBuildSettings.");
            }
        }

        [MenuItem("Tools/Primer Volante/Lessons/Verify Lesson 7")]
        public static bool VerifyLesson07()
        {
            Debug.Log("=================================================================");
            Debug.Log("🔍 [Lesson07BarrierManeuversSetup] Verificando Lección 7...");
            Debug.Log("=================================================================");

            var failures = new List<string>();

            // 1. Verificar ScriptableObjects
            LessonDefinitionSO lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(LESSON_07_PATH);
            if (lesson == null) failures.Add($"No se encontró {LESSON_07_PATH}");
            else
            {
                if (lesson.steps == null || lesson.steps.Count != 7)
                {
                    failures.Add($"LessonData_07 debe tener 7 pasos (tiene {lesson.steps?.Count ?? 0}).");
                }
                else
                {
                    // Verificar mención a la regla del medio auto
                    bool hasHalfCarRule = false;
                    foreach (var s in lesson.steps)
                    {
                        if (s != null && s.instructionText.ToLower().Contains("medio auto"))
                        {
                            hasHalfCarRule = true;
                            break;
                        }
                    }
                    if (!hasHalfCarRule) failures.Add("Falta la explicación de la regla del 'medio auto' en los pasos de reversa.");
                }
            }

            // 2. Verificar Escena
            Scene scene = SceneManager.GetSceneByName("Lesson_07_BarrierManeuvers");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                failures.Add($"No se pudo abrir la escena {TARGET_SCENE_PATH}");
            }
            else
            {
                // Car09
                GameObject car09 = GameObject.Find("Car09");
                if (car09 == null) failures.Add("Falta Car09 en la escena.");

                // Lesson_Manager
                GameObject mgr = GameObject.Find("Lesson_Manager");
                if (mgr == null) failures.Add("Falta Lesson_Manager en la escena.");

                // Controller
                Lesson07BarrierManeuversController ctrl = UnityEngine.Object.FindAnyObjectByType<Lesson07BarrierManeuversController>();
                if (ctrl == null) failures.Add("Falta Lesson07BarrierManeuversController en la escena.");
                else
                {
                    if (ctrl.StartZone == null) failures.Add("Controller: StartZone está null.");
                    if (ctrl.ForwardFinishZone == null) failures.Add("Controller: ForwardFinishZone está null.");
                }

                // Vallas
                GameObject slalomRoot = GameObject.Find(SLALOM_ROOT_NAME);
                if (slalomRoot == null) failures.Add($"Falta el contenedor {SLALOM_ROOT_NAME}.");
                else
                {
                    int count = slalomRoot.GetComponentsInChildren<ObstacleCollisionDetector>(true).Length;
                    if (count != ConeZs.Length) failures.Add($"Se esperaban {ConeZs.Length} conos en el slalom (hay {count}).");
                }
            }

            if (failures.Count > 0)
            {
                Debug.LogError($"[Lesson07BarrierManeuversSetup] ❌ Verificación falló con {failures.Count} errores:\n - " + string.Join("\n - ", failures));
                return false;
            }

            Debug.Log("[Lesson07BarrierManeuversSetup] 🏆 ¡VERIFICACIÓN DE LECCIÓN 7 EXITOSA! Todos los assets, escena, vallas y controlador están en orden.");
            return true;
        }
    }
}
