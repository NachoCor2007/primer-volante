using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de la infracción de circular sin luces bajas (<c>LOW_BEAM</c>): lógica pura del
    /// <see cref="LowBeamTracker"/> con pasos sintéticos, textos, definición y catálogo, la escena
    /// <c>GPSLowBeamDemo</c> y el comportamiento básico del <see cref="LowBeamDetector"/>. La detección en movimiento
    /// se prueba en Play Mode.
    /// </summary>
    public static class LowBeamInfractionVerification
    {
        private const float k_StepM = 0.25f;
        private const string TEMPLATE = "{when} {lights}. Encendé las luces bajas antes de empezar a circular y mantenelas puestas.";

        private const HeadlightMode Off = HeadlightMode.Off;
        private const HeadlightMode Position = HeadlightMode.Position;
        private const HeadlightMode Low = HeadlightMode.LowBeam;
        private const HeadlightMode High = HeadlightMode.HighBeam;

        private static readonly string[] s_SourceFiles =
        {
            "Assets/Scripts/Testing/Editor/LowBeamSetup.cs",
            "Assets/Scripts/Testing/Editor/DemoSceneSetup.cs",
            "Assets/Scripts/Environment/SuppressionSet.cs",
            "Assets/Scripts/Environment/LowBeamDetector.cs",
            "Assets/Scripts/Environment/LowBeamTracker.cs",
        };

        // APIs de Unity obsoletas que el proyecto prohíbe (ver AGENTS.md).
        private static readonly string[] s_ObsoleteApis =
        {
            "FindFirstObjectByType", "FindObjectOfType", "FindObjectsOfType", "FindObjectsSortMode",
            "Rigidbody.velocity", ".velocity ", ".angularDrag", ".drag ",
        };

        private const float k_Dt = 0.02f;

        // Simulador de pasos sintéticos de k_Dt segundos: recoge las faltas del tracker.
        private sealed class Sim
        {
            public readonly LowBeamTracker Tracker = new LowBeamTracker();
            public readonly List<LowBeamFault> Faults = new List<LowBeamFault>();

            // Recorre 'meters' a 'speed' m/s con el modo dado.
            public void Drive(float meters, HeadlightMode mode, float speed = 2f) => DriveTime(meters / speed, mode, speed);

            // Circula 'seconds' a 'speed' m/s con el modo dado.
            public void DriveTime(float seconds, HeadlightMode mode, float speed = 2f)
            {
                float remaining = seconds;
                while (remaining > 1e-5f)
                {
                    float dt = Mathf.Min(k_Dt, remaining);
                    Step(mode, speed * dt, dt);
                    remaining -= dt;
                }
            }

            // Auto detenido 'seconds' segundos: pasos con distancia 0 (el tiempo corre).
            public void Stop(HeadlightMode mode, float seconds = 0.4f) => DriveTime(seconds, mode, 0f);

            public void Step(HeadlightMode mode, float distance, float dt)
            {
                LowBeamFault? fault = Tracker.Step(mode, distance, dt);
                if (fault.HasValue) Faults.Add(fault.Value);
            }
        }

        [MenuItem("Tools/Primer Volante/Run Low Beam Infraction Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE LA INFRACCIÓN SIN LUCES BAJAS (LOW_BEAM)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;
            CockpitSnapshot cockpitBefore = CockpitSnapshot.Take(saveAssetsFirst: false);

            VerifyTracker(ref total, ref passed);
            VerifyTexts(ref total, ref passed);
            VerifyData(ref total, ref passed);
            VerifyScene(ref total, ref passed);
            VerifyDetectorComponent(ref total, ref passed);
            VerifySuppressionSet(ref total, ref passed);
            VerifyApi(ref total, ref passed);
            VerifyCockpitUntouched(cockpitBefore, ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 SIN LUCES BAJAS (LOW_BEAM): {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Tracker

        private static void VerifyTracker(ref int total, ref int passed)
        {
            // 1. Motor apagado: nada.
            var sim = new Sim();
            sim.Drive(10f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.AccumulatedM == 0f && sim.Tracker.AccumulatedS == 0f,
                "1. Motor apagado: 10 m sin luces no dan falta ni acumulan", ref total, ref passed);

            // 2. Encender, 1.9 m sin luces (2 m/s): nada; al llegar a 2 m (y 1 s) una falta AtStart/Off.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(1.9f, Off);
            bool noneAt19 = sim.Faults.Count == 0;
            sim.Drive(0.1f, Off);
            Assert(noneAt19 && sim.Faults.Count == 1 && sim.Faults[0].AtStart && sim.Faults[0].Mode == Off,
                $"2. Sin luces: 1.9 m no dan falta; a los 2 m una falta AtStart/Off ({Describe(sim)})", ref total, ref passed);

            // 3. Bloqueado.
            sim.Drive(50f, Off, 5f);
            Assert(sim.Faults.Count == 1 && sim.Tracker.IsLatched, $"3. Bloqueado: 50 m más sin luces no dan más faltas ({Describe(sim)})", ref total, ref passed);

            // 4. Con bajas 30 m, detenerse, 30 m más: 0 faltas.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(30f, Low, 5f);
            sim.Stop(Low);
            sim.Drive(30f, Low, 5f);
            Assert(sim.Faults.Count == 0, $"4. Con bajas, detenerse y seguir: sin faltas ({Describe(sim)})", ref total, ref passed);

            // 5. Con bajas 10 m, Off, 2 m: falta "Circulaste".
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(10f, Low, 5f);
            sim.Drive(2f, Off);
            Assert(sim.Faults.Count == 1 && !sim.Faults[0].AtStart && sim.Faults[0].Mode == Off,
                $"5. Bajas 10 m y luego Off 2 m: una falta AtStart=false ({Describe(sim)})", ref total, ref passed);

            // 6. Rearma al poner las bajas.
            sim.Drive(1f, Low);
            sim.Drive(2f, Off);
            Assert(sim.Faults.Count == 2 && !sim.Faults[1].AtStart, $"6. Poner las bajas rearma: segunda falta al volver a Off ({Describe(sim)})", ref total, ref passed);

            // 7. HighBeam y Position.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 5f);
            sim.Drive(2f, High);
            bool highFault = sim.Faults.Count == 1 && sim.Faults[0].Mode == High;
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 5f);
            sim.Drive(2f, Position);
            bool positionFault = sim.Faults.Count == 1 && sim.Faults[0].Mode == Position;
            Assert(highFault && positionFault, $"7. HighBeam y Position 2 m dan falta con su modo (alta: {highFault}, posición: {positionFault})", ref total, ref passed);

            // 8. Parpadeo: LowBeam -> HighBeam 1 m -> LowBeam -> HighBeam 1 m: sin falta.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 5f);
            sim.Drive(1f, High);
            sim.Drive(1f, Low);
            sim.Drive(1f, High);
            Assert(sim.Faults.Count == 0, $"8. Parpadeo de altas (1 m + 1 m separados por bajas): sin falta ({Describe(sim)})", ref total, ref passed);

            // 9. Motor: falta; apagar; encender; sin luces 2 m: otra falta AtStart.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(2f, Off);
            sim.Tracker.EngineStopped();
            sim.Tracker.EngineStarted();
            sim.Drive(2f, Off);
            Assert(sim.Faults.Count == 2 && sim.Faults[0].AtStart && sim.Faults[1].AtStart,
                $"9. Apagar y volver a encender sin luces: otra falta AtStart ({Describe(sim)})", ref total, ref passed);

            // 10. Arranca con bajas, circula, se detiene, Off detenido, 2 m: Circulaste.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(10f, Low, 5f);
            sim.Stop(Low);
            sim.Stop(Off);
            sim.Drive(2f, Off);
            Assert(sim.Faults.Count == 1 && !sim.Faults[0].AtStart, $"10. Bajas, detenerse, apagar las luces y recorrer 2 m: AtStart=false ({Describe(sim)})", ref total, ref passed);

            // 11. EngineStopped reinicia todo.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 5f);
            sim.Drive(2f, Off);
            bool dirty = sim.Tracker.AccumulatedM > 0f && sim.Tracker.AccumulatedS > 0f && sim.Tracker.IsLatched && sim.Tracker.HasDrivenWithLowBeam;
            sim.Tracker.EngineStopped();
            Assert(dirty && sim.Tracker.AccumulatedM == 0f && sim.Tracker.AccumulatedS == 0f && !sim.Tracker.IsLatched &&
                   !sim.Tracker.HasDrivenWithLowBeam && !sim.Tracker.IsEngineRunning,
                "11. EngineStopped reinicia distancia, tiempo, bloqueo y el estado 'ya circuló con bajas'", ref total, ref passed);

            // C2: distancia Y tiempo.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 14f);
            sim.DriveTime(0.3f, High, 14f);
            bool noFault = sim.Faults.Count == 0 && sim.Tracker.AccumulatedM > 4.1f;
            sim.DriveTime(0.1f, Low, 14f);
            Assert(noFault && sim.Tracker.AccumulatedM == 0f && sim.Tracker.AccumulatedS == 0f && !sim.Tracker.IsLatched,
                $"C2: HighBeam 0,3 s a 14 m/s (4,2 m) no da falta y al volver a LowBeam se reinicia todo ({Describe(sim)})", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 14f);
            sim.DriveTime(1.0f, High, 14f);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Mode == High && !sim.Faults[0].AtStart,
                $"C2: HighBeam 1,0 s a 14 m/s da 1 falta ({Describe(sim)})", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(1.9f, Off, 1f);
            bool none19 = sim.Faults.Count == 0;
            sim.Drive(0.1f, Off, 1f);
            Assert(none19 && sim.Faults.Count == 1 && sim.Faults[0].AtStart,
                $"C2: arrancar sin luces a 1 m/s: falta al llegar a los 2 m (2 s) ({Describe(sim)})", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Stop(Off, 5f);
            bool waited = sim.Faults.Count == 0 && sim.Tracker.AccumulatedS > 4.9f && sim.Tracker.AccumulatedM == 0f;
            sim.Drive(1.9f, Off, 5f);
            bool none = sim.Faults.Count == 0;
            sim.Drive(0.1f, Off, 5f);
            Assert(waited && none && sim.Faults.Count == 1, $"C2: detenido sin luces 5 s no da falta; la da al completar los 2 m ({Describe(sim)})", ref total, ref passed);

            // C4: observar el modo sin acumular (detector suspendido): pasar a bajas rearma igual.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 5f);
            sim.Drive(2f, Off);
            bool latched = sim.Tracker.IsLatched;
            LowBeamFault? ignored = sim.Tracker.Step(Low, 0f, 0f);
            Assert(latched && !ignored.HasValue && !sim.Tracker.IsLatched && sim.Tracker.AccumulatedM == 0f && sim.Tracker.AccumulatedS == 0f,
                "C4: Step(LowBeam, 0, 0) desbloquea y reinicia sin acumular", ref total, ref passed);
            sim.Tracker.Step(Off, 0f, 0f);
            Assert(sim.Tracker.AccumulatedM == 0f && sim.Tracker.AccumulatedS == 0f, "C4: Step(Off, 0, 0) no acumula nada", ref total, ref passed);

            // Extras de borde.
            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(1f, Off);
            sim.Tracker.EngineStarted(); // idempotente: no reinicia
            Assert(Mathf.Abs(sim.Tracker.AccumulatedM - 1f) < 1e-3f && sim.Tracker.AccumulatedS > 0.49f, "EngineStarted con el motor ya en marcha no reinicia los acumulados", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Off, 5f);
            bool latchedBefore = sim.Tracker.IsLatched;
            sim.Tracker.EngineStarted();
            Assert(latchedBefore && sim.Tracker.IsLatched, "EngineStarted con el motor ya en marcha no desbloquea una falta", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(1f, Off);
            sim.Stop(Off);
            sim.Drive(1f, Off);
            Assert(sim.Faults.Count == 1, "Detenerse no reinicia el acumulado: 1 m + parado + 1 m sin luces da falta", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Stop(Low);
            sim.Drive(2f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].AtStart, "Encender con bajas sin moverse y apagarlas antes de salir: 'Arrancaste'", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Tracker.ThresholdM = 5f;
            sim.Tracker.MinTimeS = 0f;
            sim.Drive(4.5f, Off, 5f);
            bool noneYet = sim.Faults.Count == 0;
            sim.Drive(0.5f, Off, 5f);
            Assert(noneYet && sim.Faults.Count == 1, "ThresholdM configurable (5 m) y MinTimeS en 0", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Off, 5f);
            sim.Tracker.Reset();
            sim.Drive(5f, Off, 5f);
            Assert(sim.Faults.Count == 1 && !sim.Tracker.IsEngineRunning, "Reset deja el motor como apagado: no acumula hasta el próximo EngineStarted", ref total, ref passed);

            sim = new Sim();
            sim.Tracker.EngineStarted();
            sim.Drive(5f, Low, 5f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) sim.Tracker.Step(i % 4 == 0 ? Low : Off, 0.1f, k_Dt);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert(allocated == 0, $"El tracker no genera allocations por paso ({allocated} bytes en 2000 pasos)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Textos

        private static void VerifyTexts(ref int total, ref int passed)
        {
            Assert(LowBeamTracker.WhenText(true) == "Arrancaste" && LowBeamTracker.WhenText(false) == "Circulaste",
                "12. WhenText: true = \"Arrancaste\", false = \"Circulaste\"", ref total, ref passed);
            Assert(LowBeamTracker.LightsText(Off) == "con las luces apagadas" &&
                   LowBeamTracker.LightsText(Position) == "solo con las luces de posición" &&
                   LowBeamTracker.LightsText(High) == "con las luces altas",
                "12. LightsText: Off, Position y HighBeam devuelven las cadenas acordadas", ref total, ref passed);
            var fault = new LowBeamFault(High, false);
            Assert(fault.WhenText == "Circulaste" && fault.LightsText == "con las luces altas", "12. LowBeamFault expone los textos de when y lights", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Datos

        private static void VerifyData(ref int total, ref int passed)
        {
            Assert(InfractionIds.LowBeam == "LOW_BEAM", "InfractionIds.LowBeam = \"LOW_BEAM\"", ref total, ref passed);

            var asset = AssetDatabase.LoadAssetAtPath<InfractionDefinitionSO>(InfractionManagerSetup.DefinitionPath(InfractionIds.LowBeam));
            Assert(asset != null, "13. Existe Infraction_LOW_BEAM.asset", ref total, ref passed);

            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            bool inCatalog = catalog != null && asset != null && catalog.TryGet(InfractionIds.LowBeam, out InfractionDefinitionSO fromCatalog) && fromCatalog == asset;
            Assert(inCatalog, "13. LOW_BEAM está en el InfractionCatalog", ref total, ref passed);
            if (asset == null) return;

            Assert(asset.Severity == InfractionSeverity.Minor, $"13. LOW_BEAM es leve (Minor) ({asset.Severity})", ref total, ref passed);
            Assert(asset.ShortTitle == "Sin luces bajas", $"13. ShortTitle exacto \"Sin luces bajas\" ('{asset.ShortTitle}')", ref total, ref passed);
            Assert(asset.DescriptionTemplate == TEMPLATE, $"13. Plantilla exacta: \"{asset.DescriptionTemplate}\"", ref total, ref passed);

            string text = InfractionTextFormatter.Format(asset.DescriptionTemplate, new Dictionary<string, string>
            {
                { "when", LowBeamTracker.WhenText(true) },
                { "lights", LowBeamTracker.LightsText(Off) },
            });
            Assert(text == "Arrancaste con las luces apagadas. Encendé las luces bajas antes de empezar a circular y mantenelas puestas.",
                $"13. La descripción formatea bien: \"{text}\"", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Escena

        private static void VerifyScene(ref int total, ref int passed)
        {
            bool exists = System.IO.File.Exists(LowBeamSetup.SCENE_PATH);
            Assert(exists, "14. Existe GPSLowBeamDemo.unity", ref total, ref passed);

            if (exists)
            {
                var checks = new List<(bool ok, string name)>();
                VerificationSceneUtil.WithScene(LowBeamSetup.SCENE_PATH, scene => VerifyDemoScene(scene, checks));
                foreach ((bool ok, string name) in checks) Assert(ok, name, ref total, ref passed);
            }

            foreach (string other in new[] { LowBeamSetup.SOURCE_SCENE_PATH })
            {
                if (!System.IO.File.Exists(other)) continue;
                bool clean = true;
                VerificationSceneUtil.WithScene(other, scene => clean = AllInScene<LowBeamDetector>(scene).Length == 0);
                Assert(clean, $"14. {System.IO.Path.GetFileNameWithoutExtension(other)} no tiene LowBeamDetector", ref total, ref passed);
            }

            // C6/R2: una escena destino abierta y sucia se detecta (y es esa escena la que se detecta).
            VerifyDirtyDetection(ref total, ref passed);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab");
            Assert(prefab != null && prefab.GetComponentsInChildren<LowBeamDetector>(true).Length == 0, "14. El prefab Car09 no tiene LowBeamDetector", ref total, ref passed);
        }

        private static void VerifyDemoScene(Scene scene, List<(bool ok, string name)> checks)
        {
            LowBeamDetector[] detectors = AllInScene<LowBeamDetector>(scene);
            checks.Add((detectors.Length == 1, $"14. GPSLowBeamDemo tiene exactamente un LowBeamDetector ({detectors.Length})"));
            if (detectors.Length != 1) return;

            LowBeamDetector detector = detectors[0];
            checks.Add((detector.gameObject.name == LowBeamSetup.CAR_NAME && detector.GetComponent<VehicleController>() != null && detector.GetComponent<Rigidbody>() != null,
                "14. El detector está en la raíz de Car09, junto al VehicleController y el Rigidbody"));

            bool isAdded = PrefabUtility.GetAddedComponents(detector.gameObject).Any(c => c.instanceComponent == detector);
            checks.Add((isAdded, "14. El detector es un override de escena (componente agregado a la instancia del prefab)"));
            checks.Add((detector.ThresholdM == 2f && detector.MinTimeS == 1f, $"14. m_ThresholdM = 2 y m_MinTimeS = 1 ({detector.ThresholdM}, {detector.MinTimeS})"));

            var so = new SerializedObject(detector);
            checks.Add((so.FindProperty("m_LogEvents").boolValue, "14. m_LogEvents = true en la escena demo"));
        }

        // ---------------------------------------------------------------- Detector (componente)

        private static readonly Dictionary<string, MethodInfo> s_Methods = new Dictionary<string, MethodInfo>();

        private static void Invoke(LowBeamDetector detector, string method)
        {
            if (!s_Methods.TryGetValue(method, out MethodInfo info))
            {
                info = typeof(LowBeamDetector).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                s_Methods[method] = info;
            }

            info.Invoke(detector, null);
        }

        // Reporter de prueba: cuenta las faltas LOW_BEAM y guarda sus parámetros.
        private sealed class CountingReporter : IInfractionReporter
        {
            public readonly List<(string id, string when, string lights)> Reports = new List<(string, string, string)>();

            public void Report(in InfractionReport report)
            {
                string when = null, lights = null;
                if (report.Parameters != null)
                {
                    report.Parameters.TryGetValue("when", out when);
                    report.Parameters.TryGetValue("lights", out lights);
                }

                Reports.Add((report.InfractionId, when, lights));
            }
        }

        // Modo de luces que lee el detector bajo prueba (HeadlightSource): no hay VehicleLightingController, que se
        // engancharía a la cabina de la escena abierta.
        private static HeadlightMode s_TestMode = Off;

        // Mueve el auto 'seconds' segundos a 'speed' m/s hacia adelante, un FixedUpdate por paso de física.
        private static void Advance(LowBeamDetector detector, Rigidbody body, float seconds, float speed)
        {
            int steps = Mathf.RoundToInt(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < steps; i++)
            {
                body.position += Vector3.forward * (speed * Time.fixedDeltaTime);
                Invoke(detector, "FixedUpdate");
            }
        }

        private static void VerifyDetectorComponent(ref int total, ref int passed)
        {
            // C7: los objetos temporales viven en una preview scene, así no ensucian la escena abierta.
            bool dirtyBefore = SceneManager.GetActiveScene().isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            var reporter = new CountingReporter();
            InfractionReporting.Register(reporter);
            LowBeamDetector detector = null;
            try
            {
                var go = new GameObject("LowBeamDetector_Tmp");
                SceneManager.MoveGameObjectToScene(go, preview);
                var body = go.AddComponent<Rigidbody>();
                body.useGravity = false;
                var vehicle = go.AddComponent<VehicleController>();
                detector = go.AddComponent<LowBeamDetector>();
                detector.HeadlightSource = () => s_TestMode;
                s_TestMode = Off;
                Invoke(detector, "Awake");
                Invoke(detector, "OnEnable");

                // SetSuppressed con dueño destruido se descarta.
                var owner = new GameObject("LowBeamOwner_Tmp");
                SceneManager.MoveGameObjectToScene(owner, preview);
                detector.SetSuppressed(owner, true);
                bool suppressed = detector.IsSuppressed;
                UnityEngine.Object.DestroyImmediate(owner);
                bool released = !detector.IsSuppressed;
                Assert(suppressed && released, $"15. SetSuppressed con dueño destruido se descarta (suprimido {suppressed}, liberado {released})", ref total, ref passed);

                var plainOwner = new object();
                detector.SetSuppressed(plainOwner, true);
                bool plainSuppressed = detector.IsSuppressed;
                detector.SetSuppressed(plainOwner, false);
                Assert(plainSuppressed && !detector.IsSuppressed, "15. SetSuppressed(owner, true/false) suspende y reanuda", ref total, ref passed);

                // Motor en marcha, sin luces, 5 m/s durante 0,2 s (1 m): acumula distancia y tiempo.
                vehicle.SetEngineRunning(true);
                Invoke(detector, "FixedUpdate");
                Advance(detector, body, 0.2f, 5f);
                float accumulated = detector.AccumulatedM;
                Assert(accumulated > 0.7f && accumulated < 1.1f && detector.AccumulatedS > 0.19f,
                    $"15. Con el motor en marcha y sin luces acumula distancia y tiempo ({accumulated:F2} m, {detector.AccumulatedS:F2} s)", ref total, ref passed);

                // Suspendido: no acumula, pero el ancla sigue al auto: al reanudar no se cuenta lo recorrido.
                detector.SetSuppressed(plainOwner, true);
                Advance(detector, body, 0.5f, 5f);
                bool frozen = Mathf.Approximately(detector.AccumulatedM, accumulated) && !detector.IsLatched;
                detector.SetSuppressed(plainOwner, false);
                Advance(detector, body, 0.02f, 5f);
                Assert(frozen && detector.AccumulatedM < accumulated + 0.3f,
                    $"15. Suspendido no acumula y al reanudar no cuenta lo recorrido durante la suspensión ({detector.AccumulatedM:F2} m)", ref total, ref passed);

                detector.ResetTracking();
                Assert(detector.AccumulatedM == 0f && detector.AccumulatedS == 0f && !detector.IsLatched, "15. ResetTracking() vuelve los acumulados a 0", ref total, ref passed);

                // C1: la velocidad comandada sin desplazamiento real (auto contra una valla) no suma.
                body.linearVelocity = Vector3.forward * 5f;
                for (int i = 0; i < 100; i++) Invoke(detector, "FixedUpdate");
                body.linearVelocity = Vector3.zero;
                Assert(detector.AccumulatedM == 0f && detector.AccumulatedS > 1.9f && !detector.IsLatched,
                    $"C1: velocidad comandada sin desplazamiento real no suma distancia ({detector.AccumulatedM:F2} m, {detector.AccumulatedS:F1} s)", ref total, ref passed);

                // R6: vibración milimétrica de ida y vuelta (±5 mm) contra una valla: no suma.
                detector.ResetTracking();
                Vector3 basePosition = body.position;
                for (int i = 0; i < 2000; i++)
                {
                    body.position = basePosition + Vector3.forward * (i % 2 == 0 ? 0.005f : -0.005f);
                    Invoke(detector, "FixedUpdate");
                }

                body.position = basePosition;
                Assert(detector.AccumulatedM == 0f && !detector.IsLatched, $"R6: 2000 pasos de vibración de ±5 mm no suman distancia ({detector.AccumulatedM:F3} m)", ref total, ref passed);

                // R6: avance real de 2 m en pasos de 0,1 m acumula ~2 m (tolerancia 0,25).
                detector.ResetTracking();
                Advance(detector, body, 0.4f, 5f);
                Assert(Mathf.Abs(detector.AccumulatedM - 2f) <= 0.25f && !detector.IsLatched,
                    $"R6: avanzar 2 m en pasos de 0,1 m acumula ~2 m ({detector.AccumulatedM:F2} m)", ref total, ref passed);

                // R6: un salto de posición en un solo paso (teletransporte) no suma.
                detector.ResetTracking();
                body.position += Vector3.forward * 50f;
                Invoke(detector, "FixedUpdate");
                Assert(detector.AccumulatedM == 0f, $"R6: un teletransporte de 50 m no suma distancia ({detector.AccumulatedM:F2} m)", ref total, ref passed);

                // R6: un avance lento (0,05 m/s durante 1 s = 5 cm) tampoco.
                detector.ResetTracking();
                Advance(detector, body, 1f, 0.05f);
                Assert(detector.AccumulatedM == 0f, $"R6: 5 cm de avance lento no suman ({detector.AccumulatedM:F2} m)", ref total, ref passed);

                // Motor apagado: no acumula.
                vehicle.SetEngineRunning(false);
                Advance(detector, body, 0.5f, 5f);
                Assert(detector.AccumulatedM == 0f, "15. Con el motor apagado no acumula", ref total, ref passed);

                // R3: los eventos del motor llegan aunque el detector esté deshabilitado (suscrito desde Awake).
                vehicle.SetEngineRunning(true);
                detector.ResetTracking();
                reporter.Reports.Clear();
                Advance(detector, body, 1.2f, 2.5f);
                bool latchedFirst = detector.IsLatched && reporter.Reports.Count == 1;
                // "Deshabilitado": no corre FixedUpdate; el motor se apaga y se vuelve a prender.
                vehicle.SetEngineRunning(false);
                vehicle.SetEngineRunning(true);
                Invoke(detector, "OnEnable");
                bool unlocked = !detector.IsLatched && detector.AccumulatedM == 0f;
                Advance(detector, body, 1.2f, 2.5f);
                Assert(latchedFirst && unlocked && reporter.Reports.Count == 2 && reporter.Reports[1].when == "Arrancaste",
                    $"R3: un ciclo de motor completo con el detector deshabilitado se detecta: la falta del encendido nuevo es 'Arrancaste' ({reporter.Reports.Count} faltas)", ref total, ref passed);

                // C3: sin ciclo de motor, deshabilitar y habilitar conserva el bloqueo.
                int reportsBefore = reporter.Reports.Count;
                Invoke(detector, "OnEnable");
                bool stillLatched = detector.IsLatched;
                Advance(detector, body, 1.2f, 2.5f);
                Assert(stillLatched && detector.IsLatched && reporter.Reports.Count == reportsBefore,
                    $"C3: habilitar de nuevo con el motor en marcha conserva el bloqueo: 2 m más no dan otra falta ({reporter.Reports.Count} faltas)", ref total, ref passed);

                // C3: OnEnable con el motor apagado deja el tracker apagado.
                vehicle.SetEngineRunning(false);
                Invoke(detector, "OnEnable");
                Advance(detector, body, 1.2f, 2.5f);
                Assert(detector.AccumulatedM == 0f && !detector.IsLatched, "C3: OnEnable con el motor apagado no acumula", ref total, ref passed);

                // C4 (bloqueado -> suspendido -> LowBeam -> Off -> fin): falta "Circulaste".
                vehicle.SetEngineRunning(true);
                detector.ResetTracking();
                reporter.Reports.Clear();
                s_TestMode = Low;
                Advance(detector, body, 1f, 5f);
                s_TestMode = Off;
                Advance(detector, body, 1.2f, 2.5f);
                bool firstFault = detector.IsLatched && reporter.Reports.Count == 1 && reporter.Reports[0].when == "Circulaste";
                detector.SetSuppressed(plainOwner, true);
                s_TestMode = Low;
                Advance(detector, body, 0.1f, 5f);
                s_TestMode = Off;
                Advance(detector, body, 0.5f, 5f);
                bool rearmed = !detector.IsLatched && detector.AccumulatedM == 0f && detector.AccumulatedS == 0f;
                detector.SetSuppressed(plainOwner, false);
                Advance(detector, body, 1.1f, 2.5f);
                Assert(firstFault && rearmed && reporter.Reports.Count == 2 && reporter.Reports[1].when == "Circulaste" && reporter.Reports[1].lights == "con las luces apagadas",
                    $"C4: suspendido, poner LowBeam desbloquea: tras la suspensión 2 m y 1 s dan otra falta 'Circulaste' ({reporter.Reports.Count} faltas)", ref total, ref passed);

                // R4: suspendido, circular 10 m con bajas marca que ya circuló: al terminar, Off + 2 m y 1 s = "Circulaste".
                detector.ResetTracking();
                reporter.Reports.Clear();
                detector.SetSuppressed(plainOwner, true);
                s_TestMode = Low;
                Advance(detector, body, 2f, 5f);
                bool drivenMarked = detector.HasDrivenWithLowBeam;
                detector.SetSuppressed(plainOwner, false);
                s_TestMode = Off;
                Advance(detector, body, 1.1f, 2.5f);
                Assert(drivenMarked && reporter.Reports.Count == 1 && reporter.Reports[0].when == "Circulaste",
                    $"R4: circular 10 m con bajas estando suspendido marca 'ya circuló': la falta posterior es 'Circulaste' ({(reporter.Reports.Count > 0 ? reporter.Reports[0].when : "sin falta")})", ref total, ref passed);

                // R5: suspendido, sin bajas, no se llama a Step: una falta que ya correspondía no se descarta ni bloquea.
                detector.ResetTracking();
                reporter.Reports.Clear();
                s_TestMode = Off;
                Advance(detector, body, 3f, 0.5f);
                bool prepared = Mathf.Abs(detector.AccumulatedM - 1.5f) <= 0.3f && detector.AccumulatedS > 2.9f && !detector.IsLatched;
                detector.SetSuppressed(plainOwner, true);
                detector.ThresholdM = 1f;
                Advance(detector, body, 0.1f, 5f);
                bool notLatched = !detector.IsLatched && reporter.Reports.Count == 0;
                detector.SetSuppressed(plainOwner, false);
                Advance(detector, body, 0.1f, 5f);
                detector.ThresholdM = 2f;
                Assert(prepared && notLatched && detector.IsLatched && reporter.Reports.Count == 1,
                    $"R5: con el detector suspendido no se bloquea sin informar; al reanudar el siguiente paso da la falta (preparado {prepared}, sin bloqueo {notLatched}, faltas {reporter.Reports.Count})", ref total, ref passed);

                // Parpadeo de altas a 50 km/h (14 m/s) y altas sostenidas.
                detector.ResetTracking();
                reporter.Reports.Clear();
                s_TestMode = Low;
                Advance(detector, body, 1f, 14f);
                s_TestMode = High;
                Advance(detector, body, 0.3f, 14f);
                s_TestMode = Low;
                Advance(detector, body, 0.5f, 14f);
                Assert(reporter.Reports.Count == 0, $"C2: parpadeo de altas de 0,3 s a 50 km/h no es falta ({reporter.Reports.Count} faltas)", ref total, ref passed);

                s_TestMode = High;
                Advance(detector, body, 1.1f, 14f);
                Assert(reporter.Reports.Count == 1 && reporter.Reports[0].id == "LOW_BEAM" && reporter.Reports[0].when == "Circulaste" && reporter.Reports[0].lights == "con las luces altas",
                    "C2: altas sostenidas 1 s a 50 km/h dan 1 falta LOW_BEAM 'Circulaste con las luces altas'", ref total, ref passed);

                // C5: límites del Inspector.
                detector.ThresholdM = -3f;
                detector.MinTimeS = -3f;
                bool clamped = detector.ThresholdM >= 0.1f && detector.MinTimeS == 0f;
                detector.ThresholdM = 2f;
                detector.MinTimeS = 1f;
                Assert(clamped, "C5: ThresholdM tiene mínimo 0,1 y MinTimeS mínimo 0", ref total, ref passed);
            }
            finally
            {
                if (detector != null) Invoke(detector, "OnDestroy");
                s_TestMode = Off;
                InfractionReporting.Unregister(reporter);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            bool dirtyAfter = SceneManager.GetActiveScene().isDirty;
            Assert(!(dirtyAfter && !dirtyBefore), $"C7: la verificación no ensucia la escena activa (antes {dirtyBefore}, después {dirtyAfter})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- SuppressionSet

        private static void VerifySuppressionSet(ref int total, ref int passed)
        {
            var set = new SuppressionSet("[Test]");
            Assert(!set.IsActive, "C9: SuppressionSet vacío no está activo", ref total, ref passed);

            var a = new object();
            var b = new object();
            Assert(set.Set(a, true) && set.Set(b, true) && set.IsActive, "C9: con dos dueños está activo", ref total, ref passed);
            set.Set(a, false);
            bool stillActive = set.IsActive;
            set.Set(b, false);
            Assert(stillActive && !set.IsActive, "C9: con dos dueños, liberar uno sigue activo; liberar el segundo lo desactiva", ref total, ref passed);

            // Dueño destruido: se descarta solo.
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("SuppressionOwner_Tmp");
                SceneManager.MoveGameObjectToScene(go, preview);
                set.Set(go, true);
                bool active = set.IsActive;
                UnityEngine.Object.DestroyImmediate(go);
                Assert(active && !set.IsActive, "C9: un dueño UnityEngine.Object destruido se descarta", ref total, ref passed);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            // Dueño null: se ignora (y avisa con el prefijo).
            LogType previousFilter = Debug.unityLogger.filterLogType;
            bool accepted;
            Debug.unityLogger.filterLogType = LogType.Error;
            try
            {
                accepted = set.Set(null, true);
            }
            finally
            {
                Debug.unityLogger.filterLogType = previousFilter;
            }

            Assert(!accepted && !set.IsActive, "C9: un dueño null se ignora", ref total, ref passed);

            // Los tres detectores delegan en SuppressionSet.
            string[] detectors = { "WrongWayDetector", "TurnSignalDetector", "LowBeamDetector" };
            bool allDelegate = detectors.All(name =>
            {
                string source = System.IO.File.ReadAllText($"Assets/Scripts/Environment/{name}.cs");
                return source.Contains("SuppressionSet") && !source.Contains("HashSet<object>");
            });
            Assert(allDelegate, "C9: WrongWayDetector, TurnSignalDetector y LowBeamDetector delegan en SuppressionSet", ref total, ref passed);
        }

        // ---------------------------------------------------------------- C6 / R2: escena destino sucia

        private const string TMP_FOLDER = "Assets/Scenes/TestingScenes/_TmpLowBeam";

        private static bool AnySceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            }

            return false;
        }

        // Con una escena temporal propia (abierta de forma aditiva y sucia) se comprueba que el helper de los setups la
        // detecta como la escena destino. No toca las escenas abiertas del usuario.
        private static void VerifyDirtyDetection(ref int total, ref int passed)
        {
            string tmpPath = TMP_FOLDER + "/DirtyProbe.unity";
            bool dirtyBefore = AnySceneDirty();
            Scene original = SceneManager.GetActiveScene();
            bool detected = false, specific = false;
            Scene tmp = default;
            LogType previousFilter = Debug.unityLogger.filterLogType;
            AssetDatabase.DeleteAsset(TMP_FOLDER);
            AssetDatabase.CreateFolder("Assets/Scenes/TestingScenes", "_TmpLowBeam");
            try
            {
                tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(tmp, tmpPath);
                EditorSceneManager.MarkSceneDirty(tmp);
                if (original.IsValid()) SceneManager.SetActiveScene(original);

                Debug.unityLogger.filterLogType = LogType.Exception;
                detected = DemoSceneSetup.HasDirtyScenes("[LowBeamSetup]", tmpPath, out Scene found);
                specific = found.IsValid() && found.path == tmpPath;
            }
            finally
            {
                Debug.unityLogger.filterLogType = previousFilter;
                if (original.IsValid() && SceneManager.GetActiveScene() != original) SceneManager.SetActiveScene(original);
                if (tmp.IsValid()) EditorSceneManager.CloseScene(tmp, true);
                AssetDatabase.DeleteAsset(TMP_FOLDER);
            }

            Assert(detected && specific, $"C6/R2: la escena destino abierta y sucia se detecta como tal (detectada {detected}, es la destino {specific})", ref total, ref passed);
            Assert(AnySceneDirty() == dirtyBefore && !AssetDatabase.IsValidFolder(TMP_FOLDER),
                $"R2: la prueba no deja ninguna escena sucia ni la carpeta temporal (antes {dirtyBefore}, después {AnySceneDirty()})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- R1: la verificación no toca la cabina ni el disco

        private static readonly string[] s_GuardedMaterials =
        {
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mat_LightsKnob_Blue.mat",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mirrors/Mat_DoorPanel_Button.mat",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mirrors/Mat_Mirror_Casing.mat",
        };

        // Estado de la cabina y de los materiales del disco antes y después de la verificación.
        private sealed class CockpitSnapshot
        {
            public readonly Dictionary<string, string> FileHashes = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Keywords = new Dictionary<string, string>();
            public readonly Dictionary<VRHeadlightKnob, HeadlightMode> KnobModes = new Dictionary<VRHeadlightKnob, HeadlightMode>();

            public static CockpitSnapshot Take(bool saveAssetsFirst)
            {
                // Persiste lo que haya en memoria: si algo ensució un material, el hash del disco lo delata.
                if (saveAssetsFirst) AssetDatabase.SaveAssets();

                var snapshot = new CockpitSnapshot();
                foreach (string path in s_GuardedMaterials)
                {
                    snapshot.FileHashes[path] = System.IO.File.Exists(path)
                        ? Convert.ToBase64String(System.Security.Cryptography.SHA1.Create().ComputeHash(System.IO.File.ReadAllBytes(path)))
                        : "missing";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    snapshot.Keywords[path] = material != null ? string.Join(",", material.shaderKeywords.OrderBy(k => k)) : "missing";
                }

                foreach (VRHeadlightKnob knob in UnityEngine.Object.FindObjectsByType<VRHeadlightKnob>(FindObjectsInactive.Include))
                {
                    snapshot.KnobModes[knob] = knob.CurrentMode;
                }

                return snapshot;
            }
        }

        private static void VerifyCockpitUntouched(CockpitSnapshot before, ref int total, ref int passed)
        {
            CockpitSnapshot after = CockpitSnapshot.Take(saveAssetsFirst: true);

            var changedFiles = s_GuardedMaterials.Where(p => before.FileHashes[p] != after.FileHashes[p]).Select(System.IO.Path.GetFileName).ToList();
            Assert(changedFiles.Count == 0, $"R1: los 3 materiales de la cabina no cambiaron en el disco ({(changedFiles.Count == 0 ? "sin cambios" : string.Join(", ", changedFiles))})", ref total, ref passed);

            var changedKeywords = s_GuardedMaterials.Where(p => before.Keywords[p] != after.Keywords[p]).Select(System.IO.Path.GetFileName).ToList();
            Assert(changedKeywords.Count == 0, $"R1: los keywords de esos materiales no cambiaron ({(changedKeywords.Count == 0 ? "sin cambios" : string.Join(", ", changedKeywords))})", ref total, ref passed);

            int knobsChanged = before.KnobModes.Count(kv => kv.Key != null && after.KnobModes.TryGetValue(kv.Key, out HeadlightMode mode) && mode != kv.Value);
            Assert(knobsChanged == 0, $"R1: el modo de las {before.KnobModes.Count} perillas de luces de las escenas abiertas no cambió ({knobsChanged} cambiaron)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- API

        private static void VerifyApi(ref int total, ref int passed)
        {
            var offenders = new List<string>();
            foreach (string file in s_SourceFiles)
            {
                string source = System.IO.File.ReadAllText(file);
                foreach (string api in s_ObsoleteApis)
                {
                    if (source.Contains(api)) offenders.Add($"{System.IO.Path.GetFileName(file)}: {api}");
                }
            }

            Assert(offenders.Count == 0, $"Los archivos del detector no usan APIs obsoletas conocidas ({(offenders.Count == 0 ? "ninguna" : string.Join("; ", offenders))})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Helpers

        private static string Describe(Sim sim)
        {
            if (sim.Faults.Count == 0) return "0 faltas";
            return string.Join(", ", sim.Faults.Select(f => $"{(f.AtStart ? "Arrancaste" : "Circulaste")}/{f.Mode}"));
        }

        private static T[] AllInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
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
