using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de US22 (#15): catálogo de infracciones, funciones puras, comportamiento
    /// del <see cref="InfractionManager"/> en memoria, contexto de lección, escena <c>CityWithInfractions</c> y
    /// suites existentes. La detección en movimiento (los cuatro detectores reales) se prueba en Play Mode.
    /// </summary>
    public static class InfractionManagerVerification
    {
        // Gravedades acordadas por el usuario (prompt §3.5). Es la fuente de verdad independiente del setup.
        private static readonly Dictionary<string, InfractionSeverity> s_ExpectedSeverity = new Dictionary<string, InfractionSeverity>
        {
            { "RED_LIGHT", InfractionSeverity.Major },
            { "SPEEDING", InfractionSeverity.Minor },
            { "LESSON_BOUNDARY", InfractionSeverity.Major },
            { "CONE_GRAZE", InfractionSeverity.Minor },
            { "CONE_CRASH", InfractionSeverity.Major },
            { "VEHICLE_GRAZE", InfractionSeverity.Minor },
            { "VEHICLE_CRASH", InfractionSeverity.Major },
            { "PEDESTRIAN_HIT", InfractionSeverity.Major },
        };

        // Valores de ejemplo con los que se formatean todas las plantillas.
        private static readonly Dictionary<string, string> s_SampleParameters = new Dictionary<string, string>
        {
            { "limit", "40" },
            { "speed", "52" },
        };

        private const BindingFlags k_Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [MenuItem("Tools/Primer Volante/Run Infraction Manager Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL GESTOR DE INFRACCIONES (US22 #15)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyCatalog(ref total, ref passed);
            VerifyPureFunctions(ref total, ref passed);
            VerifyManagerInMemory(ref total, ref passed);
            VerifyLessonContext(ref total, ref passed);
            VerifyScenes(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");

            // Informativo: no suma al resultado. Se corre al final porque algunas suites abren otras escenas.
            ReportExistingSuites();

            // Las suites existentes imprimen mucho: se repite el resultado propio al final para encontrarlo fácil.
            Debug.Log($"🏁 GESTOR DE INFRACCIONES (US22): {passed}/{total} pruebas pasadas con éxito (sin contar las suites existentes).");
        }

        // ---------------------------------------------------------------- Catálogo

        private static void VerifyCatalog(ref int total, ref int passed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            Assert(catalog != null, "Existe el catálogo InfractionCatalog.asset", ref total, ref passed);
            if (catalog == null) return;

            List<string> ids = typeof(InfractionIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();
            Assert(ids.Count == 8, $"InfractionIds tiene 8 constantes ({ids.Count})", ref total, ref passed);

            Assert(catalog.Definitions.All(d => d != null && !string.IsNullOrEmpty(d.Id)),
                "El catálogo no tiene entradas vacías ni ids vacíos", ref total, ref passed);
            Assert(catalog.Definitions.Select(d => d.Id).Distinct().Count() == catalog.Definitions.Count,
                "El catálogo no tiene ids repetidos", ref total, ref passed);

            foreach (string id in ids)
            {
                bool found = catalog.TryGet(id, out InfractionDefinitionSO definition);
                Assert(found, $"{id}: tiene definición en el catálogo", ref total, ref passed);
                if (!found) continue;

                Assert(AssetDatabase.GetAssetPath(definition) == InfractionManagerSetup.DefinitionPath(id),
                    $"{id}: el asset se llama Infraction_{id}.asset", ref total, ref passed);

                bool hasExpected = s_ExpectedSeverity.TryGetValue(id, out InfractionSeverity expected);
                Assert(hasExpected && definition.Severity == expected,
                    $"{id}: gravedad {(hasExpected ? expected.ToString() : "?")} (es {definition.Severity})", ref total, ref passed);

                Assert(!string.IsNullOrWhiteSpace(definition.ShortTitle) && !definition.ShortTitle.Contains("{"),
                    $"{id}: título corto no vacío y sin marcadores ('{definition.ShortTitle}')", ref total, ref passed);

                string text = InfractionTextFormatter.Format(definition.DescriptionTemplate, s_SampleParameters);
                Assert(!string.IsNullOrWhiteSpace(text) && !text.Contains("{") && !text.Contains("}"),
                    $"{id}: la plantilla formateada no deja marcadores sin reemplazar", ref total, ref passed);
            }

            // Los marcadores de cada plantilla del catálogo deben ser exactamente los parámetros declarados en el setup.
            foreach (InfractionManagerSetup.Spec spec in InfractionManagerSetup.Specs)
            {
                if (!catalog.TryGet(spec.Id, out InfractionDefinitionSO definition)) continue;
                var placeholders = new HashSet<string>(
                    Regex.Matches(definition.DescriptionTemplate ?? string.Empty, @"\{([^{}]+)\}").Cast<Match>().Select(m => m.Groups[1].Value));
                Assert(placeholders.SetEquals(spec.ParameterKeys),
                    $"{spec.Id}: los marcadores de la plantilla ({string.Join(", ", placeholders)}) coinciden con los parámetros declarados", ref total, ref passed);
            }

            Assert(!catalog.TryGet("NO_SUCH_ID", out _) && !catalog.TryGet(null, out _) && !catalog.TryGet("", out _),
                "TryGet devuelve false para ids desconocidos, null y vacío", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Funciones puras

        private static void VerifyPureFunctions(ref int total, ref int passed)
        {
            var parameters = new Dictionary<string, string> { { "a", "1" }, { "b", "dos" } };

            Assert(InfractionTextFormatter.Format("x {a} y {b}", parameters) == "x 1 y dos",
                "Format reemplaza cada marcador", ref total, ref passed);
            Assert(InfractionTextFormatter.Format("{a}{a}", parameters) == "11",
                "Format reemplaza marcadores repetidos y contiguos", ref total, ref passed);
            Assert(InfractionTextFormatter.Format("x {a} y {c}", parameters) == "x 1 y {c}",
                "Format deja tal cual el marcador sin valor", ref total, ref passed);
            Assert(InfractionTextFormatter.Format("sin marcadores", parameters) == "sin marcadores",
                "Format deja intacta una plantilla sin marcadores", ref total, ref passed);
            Assert(InfractionTextFormatter.Format("x {a}", null) == "x {a}",
                "Format con parámetros null deja la plantilla", ref total, ref passed);
            Assert(InfractionTextFormatter.Format(null, parameters) == string.Empty,
                "Format con plantilla null devuelve vacío", ref total, ref passed);
            Assert(InfractionTextFormatter.Format("{ {a} } {}", parameters) == "{ 1 } {}",
                "Format tolera llaves sueltas y vacías", ref total, ref passed);

            Assert(ObstacleImpactMath.InfractionIdFor(ObstacleKind.Cone, ObstacleImpactSeverity.Graze) == InfractionIds.ConeGraze,
                "InfractionIdFor(Cone, Graze) = CONE_GRAZE", ref total, ref passed);
            Assert(ObstacleImpactMath.InfractionIdFor(ObstacleKind.Cone, ObstacleImpactSeverity.Crash) == InfractionIds.ConeCrash,
                "InfractionIdFor(Cone, Crash) = CONE_CRASH", ref total, ref passed);
            Assert(ObstacleImpactMath.InfractionIdFor(ObstacleKind.Vehicle, ObstacleImpactSeverity.Graze) == InfractionIds.VehicleGraze,
                "InfractionIdFor(Vehicle, Graze) = VEHICLE_GRAZE", ref total, ref passed);
            Assert(ObstacleImpactMath.InfractionIdFor(ObstacleKind.Vehicle, ObstacleImpactSeverity.Crash) == InfractionIds.VehicleCrash,
                "InfractionIdFor(Vehicle, Crash) = VEHICLE_CRASH", ref total, ref passed);
            Assert(ObstacleImpactMath.InfractionIdFor(ObstacleKind.Pedestrian, ObstacleImpactSeverity.Graze) == InfractionIds.PedestrianHit,
                "InfractionIdFor(Pedestrian, Graze) = PEDESTRIAN_HIT", ref total, ref passed);
            Assert(ObstacleImpactMath.InfractionIdFor(ObstacleKind.Pedestrian, ObstacleImpactSeverity.Crash) == InfractionIds.PedestrianHit,
                "InfractionIdFor(Pedestrian, Crash) = PEDESTRIAN_HIT", ref total, ref passed);

            Assert(ObstacleImpactMath.InfractionIdFor((ObstacleKind)99, ObstacleImpactSeverity.Crash) == null,
                "InfractionIdFor con un tipo desconocido devuelve null (no se disfraza de atropello)", ref total, ref passed);

            InfractionReport report = InfractionReport.Create("X", null, Vector3.zero, ("k", "v"), ("k2", "v2"));
            Assert(report.Parameters != null && report.Parameters.Count == 2 && report.Parameters["k2"] == "v2",
                "InfractionReport.Create arma los parámetros", ref total, ref passed);
            Assert(InfractionReport.Create("X", null, Vector3.zero).Parameters == null,
                "InfractionReport.Create sin parámetros deja Parameters en null", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Manager en memoria

        private static void VerifyManagerInMemory(ref int total, ref int passed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            if (catalog == null) return;

            var created = new List<GameObject>();
            try
            {
                InfractionManager first = CreateManager(catalog, created, "TestInfractionManager_A");
                InvokeLifecycle(first, "Awake");
                first.BindLessonManager(null);
                InvokeLifecycle(first, "OnEnable");
                first.BindLessonManager(null);

                Assert(InfractionReporting.Current == (IInfractionReporter)first && InfractionReporting.Log == (IInfractionLog)first,
                    "El manager se registra como Current y Log al habilitarse", ref total, ref passed);

                var recorded = new List<InfractionRecord>();
                int cleared = 0;
                first.InfractionRecorded += recorded.Add;
                first.Cleared += () => cleared++;

                var sourceObject = new GameObject("TestSource_Detector");
                created.Add(sourceObject);

                // Falta leve con parámetros, por el camino real de los detectores (holder estático).
                InfractionReporting.Report(InfractionReport.Create(InfractionIds.Speeding, sourceObject.transform,
                    new Vector3(1f, 2f, 3f), ("limit", "40"), ("speed", "52")));

                Assert(recorded.Count == 1 && first.Records.Count == 1, "Un Report válido crea un registro y dispara el evento una vez", ref total, ref passed);
                InfractionRecord r1 = first.Records.Count > 0 ? first.Records[0] : null;
                Assert(r1 != null && r1.Id == InfractionIds.Speeding && r1.Severity == InfractionSeverity.Minor && r1.Sequence == 1,
                    "El registro tiene id SPEEDING, gravedad leve y Sequence 1", ref total, ref passed);
                Assert(r1 != null && r1.ShortTitle == "Exceso de velocidad" &&
                       r1.Description.Contains("límite de 40 km/h") && r1.Description.Contains("ibas a 52 km/h") && !r1.Description.Contains("{"),
                    "La descripción sale formateada con los parámetros", ref total, ref passed);
                Assert(r1 != null && r1.SourceName == "TestSource_Detector" && r1.Position == new Vector3(1f, 2f, 3f) &&
                       r1.Parameters["speed"] == "52" && r1.LessonId == null && r1.StepIndex == -1,
                    "El registro guarda fuente (nombre), posición, parámetros y sin contexto de lección", ref total, ref passed);
                Assert(first.MinorCount == 1 && first.MajorCount == 0 && first.TotalCount == 1,
                    "Contadores: 1 leve, 0 graves, 1 total", ref total, ref passed);

                UnityEngine.Object.DestroyImmediate(sourceObject);
                Assert(r1 != null && r1.SourceName == "TestSource_Detector",
                    "El registro sigue válido aunque se destruya el objeto fuente", ref total, ref passed);

                // Falta grave.
                InfractionReporting.Report(InfractionReport.Create(InfractionIds.ConeCrash, null, Vector3.zero, ("speed", "12")));
                Assert(first.MinorCount == 1 && first.MajorCount == 1 && first.TotalCount == 2 &&
                       first.Records.Count == 2 && first.Records[1].Sequence == 2 && first.Records[1].SourceName == null,
                    "Una falta grave suma al contador grave y Sequence aumenta (fuente null permitida)", ref total, ref passed);

                // Id desconocido: no registra y avisa.
                int warnings = 0;
                void OnLog(string message, string stack, LogType type)
                {
                    if (type == LogType.Warning && message.Contains("NO_SUCH_ID")) warnings++;
                }

                Application.logMessageReceived += OnLog;
                try
                {
                    InfractionReporting.Report(InfractionReport.Create("NO_SUCH_ID", null, Vector3.zero));
                }
                finally
                {
                    Application.logMessageReceived -= OnLog;
                }

                Assert(first.TotalCount == 2 && recorded.Count == 2 && warnings == 1,
                    "Un id desconocido no registra y escribe un LogWarning", ref total, ref passed);

                // Un suscriptor roto no impide que reciban el evento los demás.
                int received = 0;
                Action<InfractionRecord> broken = _ => throw new InvalidOperationException("suscriptor roto (esperado en la verificación)");
                Action<InfractionRecord> healthy = _ => received++;
                first.InfractionRecorded += broken;
                first.InfractionRecorded += healthy;
                bool threw = false;
                try
                {
                    InfractionReporting.Report(InfractionReport.Create(InfractionIds.RedLight, null, Vector3.zero));
                }
                catch (Exception)
                {
                    threw = true;
                }

                first.InfractionRecorded -= broken;
                first.InfractionRecorded -= healthy;
                Assert(!threw && received == 1 && first.TotalCount == 3,
                    "Un suscriptor que lanza no rompe al detector ni a los demás suscriptores", ref total, ref passed);

                // Un segundo manager no reemplaza al primero.
                InfractionManager second = CreateManager(catalog, created, "TestInfractionManager_B");
                InvokeLifecycle(second, "Awake");
                second.BindLessonManager(null);
                InvokeLifecycle(second, "OnEnable");
                second.BindLessonManager(null);
                Assert(InfractionReporting.Current == (IInfractionReporter)first,
                    "Un segundo manager no reemplaza al primero", ref total, ref passed);
                InvokeLifecycle(second, "OnDisable");
                Assert(InfractionReporting.Current == (IInfractionReporter)first,
                    "Deshabilitar el segundo manager no desregistra al primero", ref total, ref passed);

                // Clear.
                first.Clear();
                Assert(first.Records.Count == 0 && first.MinorCount == 0 && first.MajorCount == 0 && first.TotalCount == 0 && cleared == 1,
                    "Clear() vacía registros, pone contadores en 0 y dispara Cleared", ref total, ref passed);
                InfractionReporting.Report(InfractionReport.Create(InfractionIds.ConeGraze, null, Vector3.zero, ("speed", "3")));
                Assert(first.Records.Count == 1 && first.Records[0].Sequence == 1 && first.MinorCount == 1,
                    "Después de Clear la secuencia vuelve a 1", ref total, ref passed);

                // Sin manager: respaldo de consola.
                InvokeLifecycle(first, "OnDisable");
                Assert(InfractionReporting.Log == null && InfractionReporting.Current != null && InfractionReporting.Current != (IInfractionReporter)first,
                    "Sin manager, Current es el respaldo (no null) y Log es null", ref total, ref passed);

                bool fallbackThrew = false;
                int fallbackWarnings = 0;
                void OnFallbackLog(string message, string stack, LogType type)
                {
                    if (type == LogType.Warning && message.StartsWith("[Infraction]") && message.Contains("SPEEDING")) fallbackWarnings++;
                }

                Application.logMessageReceived += OnFallbackLog;
                try
                {
                    InfractionReporting.Report(InfractionReport.Create(InfractionIds.Speeding, null, Vector3.zero, ("limit", "40"), ("speed", "55")));
                }
                catch (Exception)
                {
                    fallbackThrew = true;
                }
                finally
                {
                    Application.logMessageReceived -= OnFallbackLog;
                }

                Assert(!fallbackThrew && fallbackWarnings == 1 && first.TotalCount == 1,
                    "Sin manager, Report no lanza y el respaldo escribe '[Infraction]' en consola", ref total, ref passed);

                // Un reporter que lanza no rompe a quien reporta.
                var throwing = new ThrowingReporter();
                InfractionReporting.Register(throwing);
                bool reporterThrew = false;
                try
                {
                    InfractionReporting.Report(InfractionReport.Create(InfractionIds.Speeding, null, Vector3.zero));
                }
                catch (Exception)
                {
                    reporterThrew = true;
                }

                InfractionReporting.Unregister(throwing);
                Assert(!reporterThrew && throwing.Calls == 1, "InfractionReporting.Report atrapa las excepciones del reporter", ref total, ref passed);
            }
            finally
            {
                // DestroyImmediate no llama a OnDisable fuera de Play Mode: se desregistra a mano aunque falle algo.
                foreach (GameObject go in created)
                {
                    var leftover = go != null ? go.GetComponent<InfractionManager>() : null;
                    if (leftover != null) InfractionReporting.Unregister(leftover);
                }

                foreach (GameObject go in created)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        private sealed class ThrowingReporter : IInfractionReporter
        {
            public int Calls;

            public void Report(in InfractionReport report)
            {
                Calls++;
                throw new InvalidOperationException("reporter roto (esperado en la verificación)");
            }
        }

        // ---------------------------------------------------------------- Contexto de lección

        private static void VerifyLessonContext(ref int total, ref int passed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            if (catalog == null) return;

            var created = new List<GameObject>();
            var temporaryAssets = new List<UnityEngine.Object>();
            try
            {
                var lessonObject = new GameObject("TestLessonManager");
                created.Add(lessonObject);
                var lessonManager = lessonObject.AddComponent<LessonManager>();

                var lesson = ScriptableObject.CreateInstance<LessonDefinitionSO>();
                lesson.lessonId = "TEST_LESSON";
                var step = ScriptableObject.CreateInstance<LessonStepSO>();
                lesson.steps.Add(step);
                temporaryAssets.Add(lesson);
                temporaryAssets.Add(step);

                InfractionManager manager = CreateManager(catalog, created, "TestInfractionManager_Lesson");
                InvokeLifecycle(manager, "Awake");
                InvokeLifecycle(manager, "OnEnable");
                manager.BindLessonManager(lessonManager);

                int cleared = 0;
                manager.Cleared += () => cleared++;

                InfractionReporting.Report(InfractionReport.Create(InfractionIds.ConeGraze, null, Vector3.zero, ("speed", "2")));
                Assert(manager.TotalCount == 1 && manager.Records[0].LessonId == null && manager.Records[0].StepIndex == -1,
                    "Con LessonManager pero sin lección iniciada, el registro no tiene contexto", ref total, ref passed);

                lessonManager.StartLesson(lesson);
                Assert(manager.TotalCount == 0 && manager.Records.Count == 0 && cleared == 1 && manager.LessonId == "TEST_LESSON",
                    "LessonStarted limpia el registro, dispara Cleared y guarda el lessonId", ref total, ref passed);

                InfractionReporting.Report(InfractionReport.Create(InfractionIds.VehicleCrash, null, Vector3.zero, ("speed", "20")));
                InfractionRecord record = manager.Records.Count > 0 ? manager.Records[0] : null;
                Assert(record != null && record.LessonId == "TEST_LESSON" && record.StepIndex == 0,
                    "El registro guarda el lessonId y el paso actual de la lección", ref total, ref passed);

                InvokeLifecycle(manager, "OnDisable");
                lessonManager.StartLesson(lesson);
                Assert(cleared == 1, "Al deshabilitar el manager se desuscribe de LessonStarted", ref total, ref passed);
            }
            finally
            {
                foreach (GameObject go in created)
                {
                    var leftover = go != null ? go.GetComponent<InfractionManager>() : null;
                    if (leftover != null) InfractionReporting.Unregister(leftover);
                }

                foreach (GameObject go in created)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }

                foreach (UnityEngine.Object asset in temporaryAssets)
                {
                    if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

        // ---------------------------------------------------------------- Escenas

        private static void VerifyScenes(ref int total, ref int passed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);

            // Los chequeos que dependen de la escena se juntan dentro de la lambda (no puede usar ref) y se evalúan después.
            var checks = new List<(bool ok, string name)>();

            bool hasInfractionsScene = System.IO.File.Exists(InfractionManagerSetup.SCENE_PATH);
            Assert(hasInfractionsScene, "Existe CityWithInfractions.unity", ref total, ref passed);
            if (hasInfractionsScene)
            {
                WithScene(InfractionManagerSetup.SCENE_PATH, scene =>
                {
                    InfractionManager[] managers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InfractionManager>(true)).ToArray();
                    checks.Add((managers.Length == 1, $"CityWithInfractions tiene exactamente un InfractionManager ({managers.Length})"));

                    InfractionCatalogSO assigned = managers.Length == 1 ? managers[0].Catalog : null;
                    checks.Add((assigned != null && assigned == catalog, "El InfractionManager de la escena tiene el catálogo asignado"));

                    checks.Add((scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LessonManager>(true)).Count() == 0,
                        "CityWithInfractions no tiene LessonManager"));
                    checks.Add((scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpeedLimitEvaluator>(true)).Count() > 0 &&
                                scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RedLightViolationDetector>(true)).Count() > 0 &&
                                scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ObstacleCollisionDetector>(true)).Count() > 0,
                        "CityWithInfractions conserva semáforos, tramos de velocidad y obstáculos"));
                });
            }

            bool hasSourceScene = System.IO.File.Exists(InfractionManagerSetup.SOURCE_SCENE_PATH);
            Assert(hasSourceScene, "Existe CityMerged3.unity", ref total, ref passed);
            if (hasSourceScene)
            {
                WithScene(InfractionManagerSetup.SOURCE_SCENE_PATH, scene =>
                {
                    int managers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<InfractionManager>(true)).Count();
                    checks.Add((managers == 0, $"CityMerged3 no tiene InfractionManager ({managers})"));
                });

                bool? clean = IsGitClean(InfractionManagerSetup.SOURCE_SCENE_PATH);
                if (clean.HasValue)
                {
                    checks.Add((clean.Value, "CityMerged3.unity no tiene cambios respecto de git"));
                }
                else
                {
                    Debug.LogWarning("[InfractionManagerVerification] ⚠️ No se pudo consultar git: se omite el chequeo de CityMerged3 sin cambios.");
                }
            }

            foreach ((bool ok, string name) in checks) Assert(ok, name, ref total, ref passed);
        }

        // Ejecuta el chequeo con la escena cargada: si no estaba abierta, la abre de forma aditiva y la cierra al final.
        private static void WithScene(string path, Action<Scene> check)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                check(scene);
            }
            finally
            {
                if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---------------------------------------------------------------- Suites existentes

        // Informativo: lee la línea "N/M pruebas pasadas" de cada suite. Se saltan si hay escenas sin guardar porque
        // algunas guardan o reemplazan la escena abierta. Algunas suites anidan VehicleLightingVerification, que
        // espera "1 fallo preexistente" desactualizado: se documenta y no se arregla.
        private static void ReportExistingSuites()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogWarning($"[InfractionManagerVerification] ⚠️ {open.name} tiene cambios sin guardar: no se corren las suites existentes.");
                    return;
                }
            }

            RunSuite("Tools/Primer Volante/Run Obstacle Collision Verification", "ObstacleCollisionVerification");
            RunSuite("Tools/Primer Volante/Run Lesson Boundary Verification", "LessonBoundaryVerification");
            RunSuite("Tools/Primer Volante/Run Traffic Light Tests", "TrafficLightTests");
        }

        private static void RunSuite(string menuPath, string name)
        {
            int suiteTotal = 0, suitePassed = 0;
            void OnLog(string message, string stack, LogType type)
            {
                // La última línea de resultado es la de la suite externa (las anidadas imprimen antes).
                Match match = Regex.Match(message, @"(\d+)/(\d+) pruebas pasadas");
                if (match.Success)
                {
                    suitePassed = int.Parse(match.Groups[1].Value);
                    suiteTotal = int.Parse(match.Groups[2].Value);
                }
            }

            bool menuFound;
            Application.logMessageReceived += OnLog;
            try
            {
                menuFound = EditorApplication.ExecuteMenuItem(menuPath);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            if (!menuFound)
            {
                Debug.LogWarning($"[InfractionManagerVerification] ⚠️ Suite existente {name}: no existe el MenuItem '{menuPath}'.");
            }
            else if (suiteTotal > 0 && suitePassed == suiteTotal)
            {
                Debug.Log($"[InfractionManagerVerification] ℹ️ Suite existente {name}: {suitePassed}/{suiteTotal} (pasa).");
            }
            else
            {
                Debug.LogWarning($"[InfractionManagerVerification] ⚠️ Suite existente {name}: {suitePassed}/{suiteTotal}. " +
                                 "Revisá si el fallo es preexistente (p. ej. el conteo de VehicleLightingVerification).");
            }
        }

        // ---------------------------------------------------------------- Helpers

        private static InfractionManager CreateManager(InfractionCatalogSO catalog, List<GameObject> created, string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            var manager = go.AddComponent<InfractionManager>();
            manager.SetCatalog(catalog);

            // Los logs de cada registro no aportan en la verificación.
            var so = new SerializedObject(manager);
            so.FindProperty("m_LogEvents").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        // Fuera de Play Mode Unity no llama a Awake/OnEnable/OnDisable de un componente sin [ExecuteAlways]: se invocan a mano.
        private static void InvokeLifecycle(InfractionManager manager, string method)
        {
            MethodInfo info = typeof(InfractionManager).GetMethod(method, k_Instance);
            info?.Invoke(manager, null);
        }

        // true si git no ve diferencias del archivo contra HEAD; null si no se pudo correr git.
        private static bool? IsGitClean(string assetPath)
        {
            try
            {
                string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                var info = new ProcessStartInfo("git", $"diff --quiet -- \"{assetPath}\"")
                {
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null) return null;
                    process.WaitForExit(15000);
                    if (!process.HasExited) return null;

                    // 0 = sin diferencias, 1 = hay diferencias, otro = git falló.
                    if (process.ExitCode == 0) return true;
                    if (process.ExitCode == 1) return false;
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
