using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Suite de verificación en Editor de la infracción de giro sin señalizar (<c>TURN_SIGNAL</c>): lógica pura del
    /// <see cref="TurnSignalTracker"/> con pasos sintéticos, definición y catálogo, zonas exentas en los prefabs y en
    /// la escena <c>GPSTurnSignalDemo</c>, y el detector en el auto. La detección en movimiento se prueba en Play Mode.
    /// No confundir con <c>TurnSignalVerification</c>, que verifica la palanca de guiño.
    /// </summary>
    public static class TurnSignalInfractionVerification
    {
        private const float k_StepM = 0.25f;

        // Simulador de pasos sintéticos: avanza en pasos de k_StepM y recoge las faltas del tracker.
        private sealed class Sim
        {
            public readonly TurnSignalTracker Tracker = new TurnSignalTracker();
            public readonly List<TurnSignalFault> Faults = new List<TurnSignalFault>();
            public float Heading;

            // Avanza 'meters' en línea recta con el guiño y la exención dados.
            public void Drive(float meters, TurnSignalState signal, bool exempt = false)
            {
                int steps = Mathf.RoundToInt(meters / k_StepM);
                for (int i = 0; i < steps; i++) Step(signal, exempt);
            }

            // Avanza 'meters' girando 'degrees' en total (lineal). El guiño es 'signal' hasta 'afterFraction' del giro y
            // 'afterSignal' después (modela el auto-cancelado del VRTurnSignal, que ocurre bien avanzado el giro).
            // La exención empieza al 'exemptFromFraction' del giro.
            public void Turn(float degrees, float meters, TurnSignalState signal, bool exempt = false, float exemptFromFraction = 0f,
                             TurnSignalState? afterSignal = null, float afterFraction = 0.4f)
            {
                int steps = Mathf.RoundToInt(meters / k_StepM);
                float perStep = degrees / steps;
                for (int i = 0; i < steps; i++)
                {
                    Heading += perStep;
                    float progress = (i + 1f) / steps;
                    bool isExempt = exempt && progress >= exemptFromFraction;
                    TurnSignalState current = afterSignal.HasValue && progress > afterFraction ? afterSignal.Value : signal;
                    Step(current, isExempt);
                }
            }

            // Marcha atrás: el detector no evalúa y pone el tracker en pausa. 'degrees' es lo que gira el auto en reversa.
            public void Reverse(float degrees)
            {
                Tracker.Pause(true);
                Heading += degrees;
                Tracker.Pause(true);
            }

            // Frenada casi total (menos de la velocidad mínima), sin marcha atrás y sin cambiar el rumbo.
            public void Pause()
            {
                Tracker.Pause(false);
            }

            // Avanza 'meters' en zigzag: el rumbo oscila ±amplitude alrededor del actual con el período dado (m).
            public void Wobble(float amplitudeDeg, float periodM, float meters, TurnSignalState signal)
            {
                float baseHeading = Heading;
                int steps = Mathf.RoundToInt(meters / k_StepM);
                for (int i = 0; i < steps; i++)
                {
                    Heading = baseHeading + amplitudeDeg * Mathf.Sin(2f * Mathf.PI * (i + 1) * k_StepM / periodM);
                    Step(signal, false);
                }

                Heading = baseHeading;
            }

            private void Step(TurnSignalState signal, bool exempt)
            {
                TurnSignalFault? fault = Tracker.Step(Heading, k_StepM, signal, exempt);
                if (fault.HasValue) Faults.Add(fault.Value);
            }
        }

        private const TurnSignalState Off = TurnSignalState.Off;
        private const TurnSignalState Left = TurnSignalState.Left;
        private const TurnSignalState Right = TurnSignalState.Right;

        private const string TMP_FOLDER = "Assets/Scenes/TestingScenes/_TmpTurnSignal";
        private const string TMP_SCENE_SOURCE = "Assets/Scenes/car09/CityMerged3.unity";
        private const string GUIDE_FILE = "ACTIVAR_GUINO_EN_ESCENAS.md";
        private const string NEW_TEMPLATE = "Doblaste {direction} {reason}. Poné el guiño con anticipación antes de doblar.";

        private static readonly string[] s_SourceFiles =
        {
            "Assets/Scripts/Testing/Editor/TurnSignalSceneTool.cs",
            "Assets/Scripts/Environment/TurnSignalDetector.cs",
            "Assets/Scripts/Environment/TurnSignalTracker.cs",
            "Assets/Scripts/Environment/TurnSignalMath.cs",
            "Assets/Scripts/Environment/TurnSignalExemptZone.cs",
        };

        // APIs de Unity obsoletas que el proyecto prohíbe (ver AGENTS.md).
        private static readonly string[] s_ObsoleteApis =
        {
            "FindFirstObjectByType", "FindObjectOfType", "FindObjectsOfType", "FindObjectsSortMode",
            "Rigidbody.velocity", ".velocity ", ".angularDrag", ".drag ",
        };

        [MenuItem("Tools/Primer Volante/Run Turn Signal Infraction Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE LA INFRACCIÓN GIRO SIN SEÑALIZAR (TURN_SIGNAL)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyTracker(ref total, ref passed);
            VerifyTrackerEdgeCases(ref total, ref passed);
            VerifyTrackerFixes(ref total, ref passed);
            VerifyDetectorComponent(ref total, ref passed);
            VerifyData(ref total, ref passed);
            VerifyPrefabs(ref total, ref passed);
            VerifyScenes(ref total, ref passed);
            VerifySceneTool(ref total, ref passed);
            VerifyApi(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 GIRO SIN SEÑALIZAR (TURN_SIGNAL): {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Tracker: casos pedidos

        private static void VerifyTracker(ref int total, ref int passed)
        {
            // Recta sin cambios de rumbo.
            var sim = new Sim();
            sim.Drive(100f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.None, "Recta sin cambios de rumbo: sin falta ni giro", ref total, ref passed);

            // Derecha 90° con el guiño Right los 25 m previos (se auto-cancela en el giro): sin falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.Signaled && sim.Tracker.LastTurnDirection == Right,
                "Derecha 90° con guiño Right 25 m antes (apagado en el giro): sin falta, giro bien señalizado", ref total, ref passed);

            // Justo 20 m de anticipación: alcanza.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(21f, Right);
            sim.Turn(90f, 15f, Right);
            sim.Drive(10f, Off);
            Assert(sim.Faults.Count == 0, "Derecha con 21 m de guiño previo: sin falta", ref total, ref passed);

            // Derecha sin guiño.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Direction == Right && sim.Faults[0].Reason == TurnSignalFaultReason.NoSignal &&
                   sim.Faults[0].DirectionText == "a la derecha" && sim.Faults[0].ReasonText == "sin poner el guiño",
                $"Derecha sin guiño: 1 falta 'a la derecha' / 'sin poner el guiño' ({Describe(sim)})", ref total, ref passed);

            // Izquierda con el guiño Right.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Right);
            sim.Turn(-90f, 15f, Right);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Direction == Left && sim.Faults[0].Reason == TurnSignalFaultReason.WrongSide &&
                   sim.Faults[0].DirectionText == "a la izquierda" && sim.Faults[0].ReasonText == "con el guiño hacia el lado contrario",
                $"Izquierda con guiño Right: 1 falta 'a la izquierda' / 'con el guiño hacia el lado contrario' ({Describe(sim)})", ref total, ref passed);

            // Guiño correcto puesto 8 m antes del inicio (con más de 20 m de historial).
            sim = new Sim();
            sim.Drive(40f, Off);
            sim.Drive(8f, Right);
            sim.Turn(90f, 15f, Right);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Direction == Right && sim.Faults[0].Reason == TurnSignalFaultReason.TooLate &&
                   sim.Faults[0].ReasonText == "sin señalizar con suficiente anticipación",
                $"Guiño puesto 8 m antes: 1 falta por anticipación ({Describe(sim)})", ref total, ref passed);

            // Guiño correcto con un corte de 2 m dentro de la ventana.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(10f, Right);
            sim.Drive(2f, Off);
            sim.Drive(10f, Right);
            sim.Turn(90f, 15f, Right);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Reason == TurnSignalFaultReason.TooLate,
                $"Guiño con un corte de 2 m en la ventana: 1 falta por anticipación ({Describe(sim)})", ref total, ref passed);

            // Guiño apagado durante el giro (auto-cancelado) después de 20 m correctos.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(22f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0, "Guiño auto-cancelado durante el giro tras 22 m correctos: sin falta", ref total, ref passed);

            // Cambio de carril (±15° y vuelta): sin giro.
            sim = new Sim();
            sim.Drive(20f, Off);
            sim.Turn(15f, 4f, Off);
            sim.Drive(8f, Off);
            sim.Turn(-15f, 4f, Off);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.None && !sim.Tracker.IsTurning,
                "Cambio de carril (+15° y vuelta): sin giro ni falta", ref total, ref passed);

            // Vuelta en U sin guiño: una sola falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(180f, 20f, Off);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Reason == TurnSignalFaultReason.NoSignal,
                $"Vuelta en U (180°) sin guiño: exactamente 1 falta ({Describe(sim)})", ref total, ref passed);

            // Vuelta en U con el guiño correcto: sin falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Left);
            sim.Turn(-180f, 20f, Left);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 0, "Vuelta en U (180°) con guiño Left: sin falta", ref total, ref passed);

            // Dos giros seguidos, el guiño puesto apenas terminado el primero: la ventana se acorta, sin falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(10f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.Signaled,
                $"Dos giros seguidos con el guiño puesto al terminar el primero: sin falta ({Describe(sim)})", ref total, ref passed);

            // El mismo caso con el guiño puesto recién al inicio del segundo giro.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(10f, Off);
            sim.Turn(90f, 15f, Right);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Direction == Right && sim.Faults[0].Reason == TurnSignalFaultReason.TooLate,
                $"Dos giros seguidos con el guiño recién en el segundo: 1 falta por anticipación ({Describe(sim)})", ref total, ref passed);

            // Giro dentro de una zona exenta.
            sim = new Sim();
            sim.Drive(30f, Off, exempt: true);
            sim.Turn(90f, 15f, Off, exempt: true);
            sim.Drive(20f, Off, exempt: true);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.Exempt, "Giro dentro de una zona exenta: sin falta", ref total, ref passed);

            // Empieza fuera de la zona y llega a 45° adentro (entrando a la punta sin salida).
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off, exempt: true, exemptFromFraction: 0.3f);
            sim.Drive(20f, Off, exempt: true);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.Exempt,
                "Giro que empieza afuera y llega a 45° adentro de la zona exenta: sin falta", ref total, ref passed);

            // Vuelta en U de 3 puntos dentro de una zona exenta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(10f, Off, exempt: true);
            sim.Turn(90f, 8f, Off, exempt: true);
            sim.Reverse(-90f);
            sim.Turn(90f, 8f, Off, exempt: true);
            sim.Drive(20f, Off, exempt: true);
            Assert(sim.Faults.Count == 0, $"Vuelta en U de 3 puntos en zona exenta: ninguna falta ({Describe(sim)})", ref total, ref passed);

            // Reset tras marcha atrás: lo girado en reversa no cuenta (y sin el reset sí daría falta).
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Reverse(90f);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0, "Reset tras marcha atrás: lo girado en reversa no cuenta", ref total, ref passed);

            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Heading += 90f; // mismo giro sin reset
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1, "Control: el mismo cambio de rumbo sin reset sí da falta", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Tracker: bordes y precedencia

        private static void VerifyTrackerEdgeCases(ref int total, ref int passed)
        {
            // Una sola evaluación por giro, aunque siga girando.
            var sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(135f, 30f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1, $"Un giro largo (135°) da una sola falta ({sim.Faults.Count})", ref total, ref passed);

            // Después de terminar el giro vuelve a evaluarse: segundo giro sin guiño, bien separado, segunda falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Drive(40f, Off);
            sim.Turn(-90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 2 && sim.Faults[0].Direction == Right && sim.Faults[1].Direction == Left,
                $"Dos giros separados sin guiño: dos faltas (derecha e izquierda) ({sim.Faults.Count})", ref total, ref passed);

            // Precedencia: apagado al inicio pero con guiño correcto antes dentro de la ventana => anticipación.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(10f, Right);
            sim.Drive(5f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Reason == TurnSignalFaultReason.TooLate,
                $"Precedencia: apagado al inicio pero correcto antes en la ventana => anticipación ({Describe(sim)})", ref total, ref passed);

            // Precedencia: guiño contrario al inicio gana aunque antes estuviera bien.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(10f, Right);
            sim.Drive(5f, Left);
            sim.Turn(90f, 15f, Left);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Reason == TurnSignalFaultReason.WrongSide,
                $"Precedencia: guiño contrario al inicio => lado contrario ({Describe(sim)})", ref total, ref passed);

            // Guiño puesto recién durante el giro (después del inicio): cuenta como sin guiño.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off, afterSignal: Right, afterFraction: 0.3f);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1, "Guiño puesto recién al empezar a girar: falta", ref total, ref passed);

            // Reset reciente: historial corto pero con el guiño puesto desde el reset => sin falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Tracker.Reset();
            sim.Drive(4f, Right);
            sim.Turn(90f, 15f, Right);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0, "Giro a 4 m de un reset con el guiño puesto desde el reset: sin falta (ventana acortada)", ref total, ref passed);

            // Más historial que el buffer (300 m) y giro bien señalizado.
            sim = new Sim();
            sim.Drive(300f, Off);
            sim.Drive(40f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(10f, Off);
            Assert(sim.Faults.Count == 0, "300 m de historial (buffer circular desbordado) y giro bien señalizado: sin falta", ref total, ref passed);

            // Reset limpia el giro en curso.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(60f, 8f, Off);
            bool turningBefore = sim.Tracker.IsTurning;
            sim.Tracker.Reset();
            Assert(turningBefore && !sim.Tracker.IsTurning && sim.Tracker.LastOutcome == TurnSignalOutcome.None, "Reset descarta el giro en curso", ref total, ref passed);

            // δ positivo a la derecha y negativo a la izquierda.
            sim = new Sim();
            sim.Drive(10f, Off);
            sim.Turn(20f, 4f, Off);
            float right = sim.Tracker.LastDeltaDeg;
            sim = new Sim();
            sim.Drive(10f, Off);
            sim.Turn(-20f, 4f, Off);
            Assert(right > 10f && sim.Tracker.LastDeltaDeg < -10f, $"El desvío es positivo a la derecha ({right:F0}°) y negativo a la izquierda ({sim.Tracker.LastDeltaDeg:F0}°)", ref total, ref passed);

            // Cruce del límite ±180° de DeltaAngle (rumbo 170° girando a la derecha hasta -100°).
            sim = new Sim { Heading = 170f };
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Direction == Right, "Giro que cruza el límite de ±180° del rumbo: una falta a la derecha", ref total, ref passed);

            // Sin allocations por paso.
            sim = new Sim();
            sim.Drive(20f, Off);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) sim.Tracker.Step(sim.Heading + (i % 7), k_StepM, i % 3 == 0 ? Right : Off, i % 5 == 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert(allocated == 0, $"El tracker no genera allocations por paso ({allocated} bytes en 2000 pasos)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Tracker: correcciones C1 a C4

        private static void VerifyTrackerFixes(ref int total, ref int passed)
        {
            // C1: el giro "termina" a 78°, el conductor endereza hasta 90° y sigue derecho; el próximo giro bien señalizado.
            var sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Right);
            sim.Turn(78f, 13f, Right, afterSignal: Off);
            sim.Drive(6f, Off);
            sim.Turn(12f, 3f, Off);
            sim.Drive(40f, Off);
            sim.Drive(25f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Drive(15f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.Signaled,
                $"C1: giro que termina a 78° y endereza a 90°, luego giro bien señalizado: sin falta ({Describe(sim)})", ref total, ref passed);

            // C1: deriva lenta de 12° a lo largo de una cuadra.
            sim = new Sim();
            sim.Drive(10f, Off);
            sim.Turn(12f, 80f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.None && !sim.Tracker.IsTurning,
                "C1: deriva lenta de 12° en una cuadra: sin falta y sin giro detectado", ref total, ref passed);

            // C1: un giro real no cumple el criterio de estabilidad mientras ocurre (si lo cumpliera, la referencia lo
            // seguiría y nunca se detectaría).
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off);
            Assert(sim.Faults.Count == 1, $"C1: un giro de 6°/m sigue siendo un giro con la referencia que sigue al rumbo ({Describe(sim)})", ref total, ref passed);

            // C2: U lenta con una pausa (velocidad menor a la mínima) a los 90°: una sola falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 10f, Off);
            sim.Pause();
            sim.Pause();
            sim.Turn(90f, 10f, Off);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 1, $"C2: U lenta sin guiño con una pausa a los 90°: exactamente 1 falta ({Describe(sim)})", ref total, ref passed);

            // C2: maniobra de 3 puntos a mitad de cuadra sin guiño.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(60f, 8f, Off);
            sim.Reverse(40f);
            sim.Turn(80f, 8f, Off);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 1, $"C2: maniobra de 3 puntos sin guiño (adelante, reversa, adelante hasta 180°): exactamente 1 falta ({Describe(sim)})", ref total, ref passed);

            // C2: la primera pasada no llega a 45°: se evalúa una sola vez contra el inicio original.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(30f, 5f, Off);
            sim.Reverse(30f);
            sim.Turn(120f, 12f, Off);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 1, $"C2: 3 puntos con la primera pasada a 30°: exactamente 1 falta ({Describe(sim)})", ref total, ref passed);

            // C2: el mismo caso con el guiño Left correcto.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Left);
            sim.Turn(-60f, 8f, Left);
            sim.Reverse(-40f);
            sim.Turn(-80f, 8f, Left);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.Signaled,
                $"C2: maniobra de 3 puntos con guiño Left correcto: 0 faltas ({Describe(sim)})", ref total, ref passed);

            // C2: marcha atrás girando sin un giro en curso y después avanzar derecho.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Reverse(120f);
            sim.Reverse(40f);
            sim.Drive(30f, Off);
            Assert(sim.Faults.Count == 0, "C2: marcha atrás girando sin giro en curso y después avanzar derecho: 0 faltas", ref total, ref passed);

            // C2: Pause reinicia sin giro en curso y conserva el estado con uno.
            sim = new Sim();
            sim.Drive(20f, Off);
            sim.Pause();
            bool resetWithoutTurn = sim.Tracker.CumulativeDistanceM == 0f;
            sim = new Sim();
            sim.Drive(20f, Off);
            sim.Turn(60f, 8f, Off);
            float cumulative = sim.Tracker.CumulativeDistanceM;
            sim.Pause();
            Assert(resetWithoutTurn && sim.Tracker.IsTurning && sim.Tracker.CumulativeDistanceM == cumulative,
                "C2: Pause reinicia sin giro en curso y conserva el estado con un giro en curso", ref total, ref passed);

            // C3: zigzag de ±4° después de un giro: el giro termina y el siguiente se detecta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Drive(25f, Right);
            sim.Turn(90f, 15f, Right, afterSignal: Off);
            sim.Wobble(4f, 6f, 20f, Off);
            bool ended = !sim.Tracker.IsTurning;
            sim.Drive(5f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(ended && sim.Faults.Count == 1 && sim.Faults[0].Direction == Right && sim.Faults[0].Reason == TurnSignalFaultReason.NoSignal,
                $"C3: zigzag de ±4° durante 20 m termina el giro y el segundo giro sin guiño se detecta: 1 falta ({Describe(sim)}, giro terminado: {ended})", ref total, ref passed);

            // C3 + R3: el tope de 30 m cierra el giro aunque el rumbo siga inestable (±8° de zigzag nunca es estable),
            // pero sin fijar referencia: queda esperando estabilidad y recién al estabilizarse se detecta el siguiente.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Wobble(8f, 4f, 10f, Off);
            bool stillTurning = sim.Tracker.IsTurning;
            sim.Wobble(8f, 4f, 15f, Off);
            bool cappedOut = !sim.Tracker.IsTurning && sim.Tracker.IsAwaitingStability;
            sim.Drive(10f, Off);
            bool stabilized = !sim.Tracker.IsAwaitingStability && !sim.Tracker.IsTurning;
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(stillTurning && cappedOut && stabilized && sim.Faults.Count == 2,
                $"C3/R3: tope de 30 m (sigue girando a ~17 m: {stillTurning}; cerrado y esperando estabilidad a ~32 m: {cappedOut}; estabilizado después: {stabilized}); el giro siguiente da la 2.ª falta ({Describe(sim)})", ref total, ref passed);

            // R3: vuelta amplia / círculo de radio 10 m (~360°, 63 m) sin guiño: una sola falta.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(360f, 2f * Mathf.PI * 10f, Off);
            bool waitedInCircle = sim.Tracker.IsAwaitingStability || sim.Tracker.IsTurning;
            sim.Drive(25f, Off);
            Assert(sim.Faults.Count == 1 && waitedInCircle && !sim.Tracker.IsAwaitingStability && !sim.Tracker.IsTurning,
                $"R3: círculo de radio 10 m (360°) sin guiño: exactamente 1 falta ({Describe(sim)})", ref total, ref passed);

            // R3: después de estabilizar el círculo, un giro nuevo sin guiño sí da falta.
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 2, $"R3: tras estabilizar, un giro nuevo sin guiño da falta ({Describe(sim)})", ref total, ref passed);

            // R1: estacionar: avanza girando 20°, marcha atrás girando 30°, vuelve a avanzar: lo rotado en reversa no suma.
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(20f, 4f, Off);
            sim.Reverse(30f);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 0 && sim.Tracker.LastOutcome == TurnSignalOutcome.None,
                $"R1: avanzar 20°, reversa girando 30° y avanzar (estacionar): 0 faltas ({Describe(sim)})", ref total, ref passed);

            // R1: la pausa por baja velocidad SIN reversa conserva el giro parcial (comportamiento sin cambios).
            sim = new Sim();
            sim.Drive(30f, Off);
            sim.Turn(20f, 4f, Off);
            sim.Pause();
            sim.Turn(40f, 5f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1, $"R1: giro parcial, pausa sin reversa y seguir girando hasta 60°: 1 falta ({Describe(sim)})", ref total, ref passed);

            // C4: sale de la zona exenta 10 m antes del inicio del giro y dobla sin guiño.
            sim = new Sim();
            sim.Drive(5f, Off, exempt: true);
            sim.Drive(25f, Off);
            sim.Turn(90f, 15f, Off);
            sim.Drive(20f, Off);
            Assert(sim.Faults.Count == 1 && sim.Faults[0].Reason == TurnSignalFaultReason.NoSignal,
                $"C4: en zona los primeros metros, fuera 10+ m antes del giro, sin guiño: 1 falta ({Describe(sim)})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Detector (componente)

        private static void VerifyDetectorComponent(ref int total, ref int passed)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            var go = new GameObject("TurnSignalDetector_Tmp");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                body.useGravity = false;
                var detector = go.AddComponent<TurnSignalDetector>();
                typeof(TurnSignalDetector).GetField("m_WarnedNoVehicle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(detector, true);

                // C5: sin pasar por Reset() (AddComponent en runtime), Awake deja la máscara de LaneZone.
                detector.ZoneMask = 0;
                Invoke(detector, "Awake");
                Assert(layer >= 0 && detector.ZoneMask.value == 1 << layer, $"C5: Awake asigna la máscara LaneZone si estaba vacía ({detector.ZoneMask.value})", ref total, ref passed);

                // ResetTracking no falla y deja el rastreo en blanco.
                detector.ResetTracking();
                Assert(detector.LastOutcome == TurnSignalOutcome.None && !detector.IsTurning, "ResetTracking público descarta el rastreo", ref total, ref passed);

                // Con una máscara ya asignada no la pisa.
                detector.ZoneMask = 1 << 3;
                Invoke(detector, "Awake");
                Assert(detector.ZoneMask.value == 1 << 3, "C5: Awake no pisa una máscara ya asignada", ref total, ref passed);

                // Sin allocations por paso del detector (en movimiento, con consulta de zonas).
                detector.ZoneMask = 1 << layer;
                Invoke(detector, "Awake");
                Invoke(detector, "OnEnable");
                body.linearVelocity = go.transform.forward * 10f;
                for (int i = 0; i < 50; i++) Invoke(detector, "FixedUpdate");
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 500; i++) Invoke(detector, "FixedUpdate");
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert(allocated <= 500 * 32, $"El detector no genera allocations propias por paso ({allocated} bytes en 500 pasos, incluye el costo de la reflection)", ref total, ref passed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static readonly Dictionary<string, System.Reflection.MethodInfo> s_Methods = new Dictionary<string, System.Reflection.MethodInfo>();

        private static void Invoke(TurnSignalDetector detector, string method)
        {
            if (!s_Methods.TryGetValue(method, out System.Reflection.MethodInfo info))
            {
                info = typeof(TurnSignalDetector).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                s_Methods[method] = info;
            }

            info.Invoke(detector, null);
        }

        // ---------------------------------------------------------------- Datos

        private static void VerifyData(ref int total, ref int passed)
        {
            Assert(InfractionIds.TurnSignal == "TURN_SIGNAL", "InfractionIds.TurnSignal = \"TURN_SIGNAL\"", ref total, ref passed);

            var asset = AssetDatabase.LoadAssetAtPath<InfractionDefinitionSO>(InfractionManagerSetup.DefinitionPath(InfractionIds.TurnSignal));
            Assert(asset != null, "Existe Infraction_TURN_SIGNAL.asset", ref total, ref passed);

            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            bool inCatalog = catalog != null && catalog.TryGet(InfractionIds.TurnSignal, out InfractionDefinitionSO fromCatalog) && fromCatalog == asset && asset != null;
            Assert(inCatalog, "TURN_SIGNAL está en el InfractionCatalog", ref total, ref passed);
            if (asset == null) return;

            Assert(asset.Severity == InfractionSeverity.Minor, $"TURN_SIGNAL es leve (Minor) ({asset.Severity})", ref total, ref passed);
            Assert(asset.ShortTitle == "Giro sin señalizar", $"ShortTitle exacto \"Giro sin señalizar\" ('{asset.ShortTitle}')", ref total, ref passed);

            var placeholders = new HashSet<string>(
                Regex.Matches(asset.DescriptionTemplate ?? string.Empty, @"\{([^{}]+)\}").Cast<Match>().Select(m => m.Groups[1].Value));
            Assert(placeholders.SetEquals(new[] { "direction", "reason" }) && asset.DescriptionTemplate == NEW_TEMPLATE,
                $"C7: la plantilla es la acordada, en voseo, con {{direction}} y {{reason}}: \"{asset.DescriptionTemplate}\"", ref total, ref passed);

            string text = InfractionTextFormatter.Format(asset.DescriptionTemplate, new Dictionary<string, string>
            {
                { "direction", TurnSignalMath.DirectionRight },
                { "reason", TurnSignalMath.ReasonNoSignal },
            });
            Assert(text == "Doblaste a la derecha sin poner el guiño. Poné el guiño con anticipación antes de doblar.", $"La descripción formatea bien: \"{text}\"", ref total, ref passed);

            Assert(TurnSignalMath.DirectionLeft == "a la izquierda" && TurnSignalMath.ReasonWrongSide == "con el guiño hacia el lado contrario" &&
                   TurnSignalMath.ReasonTooLate == "sin señalizar con suficiente anticipación",
                "Los textos de dirección y motivo son los acordados", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Prefabs

        private static void VerifyPrefabs(ref int total, ref int passed)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            Assert(layer == 15, $"La capa LaneZone es el índice 15 ({layer})", ref total, ref passed);

            foreach (string path in TurnSignalSetup.ExemptPrefabPaths)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert(prefab != null, $"Existe el prefab {name}", ref total, ref passed);
                if (prefab == null) continue;

                TurnSignalExemptZone[] zones = prefab.GetComponentsInChildren<TurnSignalExemptZone>(true);
                Assert(zones.Length == 1, $"{name}: exactamente una TurnSignalExemptZone ({zones.Length})", ref total, ref passed);
                if (zones.Length != 1) continue;

                var box = zones[0].GetComponent<BoxCollider>();
                Assert(box != null && box.isTrigger && zones[0].gameObject.layer == layer,
                    $"{name}: la zona es un BoxCollider trigger en la capa LaneZone", ref total, ref passed);
                if (box == null) continue;

                var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                if (path == TurnSignalSetup.DEAD_END_PREFAB_PATH)
                {
                    // C4: z local de -6 a +10 (extremo cerrado más 6 m de la mitad abierta).
                    bool deadEndZone = (box.center - new Vector3(0f, 1.5f, 2f)).magnitude < 0.05f && (box.size - new Vector3(20f, 3f, 16f)).magnitude < 0.05f;
                    Assert(deadEndZone, $"C4: {name}: la zona va de z local -6 a +10, centro {box.center} y tamaño {box.size} (esperado (0,1.5,2) y (20,3,16))", ref total, ref passed);
                }
                else
                {
                    bool coversPiece = Mathf.Abs(box.size.x - mesh.bounds.size.x) < 0.05f && Mathf.Abs(box.size.z - mesh.bounds.size.z) < 0.05f &&
                                       Mathf.Abs(box.size.y - TurnSignalSetup.ZONE_HEIGHT) < 0.05f &&
                                       Mathf.Abs(box.size.x - 20f) < 0.05f && Mathf.Abs(box.size.z - 20f) < 0.05f;
                    Assert(coversPiece, $"{name}: la zona cubre toda la pieza ({box.size.x:F1} x {box.size.z:F1} m, {box.size.y:F1} m de alto)", ref total, ref passed);
                }

                Assert(zones[0].GetComponent<RoadLaneZone>() == null && zones[0].GetComponent<WrongWayExemptZone>() == null,
                    $"{name}: la zona exenta de giro no es una RoadLaneZone ni una WrongWayExemptZone", ref total, ref passed);
            }

            // R6: Road Corner_02 también lleva la zona exenta.
            Assert(TurnSignalSetup.ExemptPrefabPaths.Contains(TurnSignalSetup.CORNER_02_PREFAB_PATH) &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(TurnSignalSetup.CORNER_02_PREFAB_PATH)?.GetComponentInChildren<TurnSignalExemptZone>(true) != null,
                "R6: Road Corner_02 está en la lista de prefabs exentos y tiene la TurnSignalExemptZone", ref total, ref passed);

            // R7: EnsureAssets guarda los prefabs solo si cambió algo: la segunda corrida no los toca.
            TurnSignalSetup.EnsureAssets(out _, out _);
            DateTime[] stamps = TurnSignalSetup.ExemptPrefabPaths.Select(pp => System.IO.File.GetLastWriteTimeUtc(pp)).ToArray();
            TurnSignalSetup.EnsureAssets(out _, out _);
            DateTime[] stampsAfter = TurnSignalSetup.ExemptPrefabPaths.Select(pp => System.IO.File.GetLastWriteTimeUtc(pp)).ToArray();
            Assert(TurnSignalSetup.LastPrefabsSaved == 0 && stamps.SequenceEqual(stampsAfter),
                $"R7: la segunda corrida de EnsureAssets no guarda prefabs ({TurnSignalSetup.LastPrefabsSaved} guardados, fechas iguales: {stamps.SequenceEqual(stampsAfter)})", ref total, ref passed);

            // La zona nueva no puede afectar a la detección de contramano: el WrongWayDetector solo mira otras clases.
            string detectorSource = System.IO.File.ReadAllText("Assets/Scripts/Environment/WrongWayDetector.cs");
            Assert(!detectorSource.Contains("TurnSignalExemptZone"), "WrongWayDetector no menciona TurnSignalExemptZone", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Escenas

        private static void VerifyScenes(ref int total, ref int passed)
        {
            bool exists = System.IO.File.Exists(TurnSignalSetup.SCENE_PATH);
            Assert(exists, "Existe GPSTurnSignalDemo.unity", ref total, ref passed);

            if (exists)
            {
                var checks = new List<(bool ok, string name)>();
                VerificationSceneUtil.WithScene(TurnSignalSetup.SCENE_PATH, scene => VerifyDemoScene(scene, checks));
                foreach ((bool ok, string name) in checks) Assert(ok, name, ref total, ref passed);
            }

            if (System.IO.File.Exists(GPSNavigationSceneSetup.WRONG_WAY_DEMO_SCENE_PATH))
            {
                bool clean = true;
                VerificationSceneUtil.WithScene(GPSNavigationSceneSetup.WRONG_WAY_DEMO_SCENE_PATH, scene => clean = AllInScene<TurnSignalDetector>(scene).Length == 0);
                Assert(clean, "GPSWrongWayDemo no tiene TurnSignalDetector", ref total, ref passed);
            }
        }

        private static void VerifyDemoScene(Scene scene, List<(bool ok, string name)> checks)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);

            TurnSignalDetector[] detectors = AllInScene<TurnSignalDetector>(scene);
            checks.Add((detectors.Length == 1, $"GPSTurnSignalDemo tiene exactamente un TurnSignalDetector ({detectors.Length})"));
            if (detectors.Length == 1)
            {
                TurnSignalDetector detector = detectors[0];
                checks.Add((detector.gameObject.name == TurnSignalSetup.CAR_NAME && detector.GetComponent<VehicleController>() != null &&
                            detector.GetComponent<Rigidbody>() != null, "El detector está en la raíz de Car09, junto al VehicleController y el Rigidbody"));
                checks.Add((layer >= 0 && detector.ZoneMask.value == 1 << layer && detector.ZoneMask.value == 1 << 15, $"m_ZoneMask es solo la capa LaneZone ({detector.ZoneMask.value})"));
                checks.Add((detector.GetComponent<WrongWayDetector>() != null, "Car09 conserva su WrongWayDetector"));
            }

            AddInstanceChecks(scene, checks, TurnSignalSetup.CORNER_PREFAB_PATH, 2, "curvas Road Corner_01");
            AddInstanceChecks(scene, checks, TurnSignalSetup.DEAD_END_PREFAB_PATH, 3, "puntas sin salida Road Lane_04");
        }

        // Cada instancia del prefab tiene la zona exenta (sin overrides que la quiten) y el conteo es el esperado.
        private static void AddInstanceChecks(Scene scene, List<(bool ok, string name)> checks, string prefabPath, int expected, string label)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            var instances = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                    if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go)) == prefabPath) instances.Add(go);
                }
            }

            checks.Add((instances.Count == expected, $"GPSTurnSignalDemo tiene {expected} {label} ({instances.Count})"));

            int withZone = instances.Count(go =>
            {
                TurnSignalExemptZone zone = go.GetComponentInChildren<TurnSignalExemptZone>(false);
                var box = zone != null ? zone.GetComponent<BoxCollider>() : null;
                return box != null && box.enabled && box.isTrigger && zone.gameObject.layer == layer;
            });
            checks.Add((instances.Count > 0 && withZone == instances.Count, $"Las {label} de la escena reciben la zona exenta ({withZone}/{instances.Count})"));

            int overrides = instances.Sum(go => PrefabUtility.GetRemovedGameObjects(go).Count + PrefabUtility.GetRemovedComponents(go).Count);
            checks.Add((overrides == 0, $"Las {label} no tienen overrides que quiten objetos o componentes ({overrides})"));
        }

        // ---------------------------------------------------------------- C6: herramienta de escenas y guía

        private static void VerifySceneTool(ref int total, ref int passed)
        {
            string repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ".."));
            string guidePath = System.IO.Path.Combine(repoRoot, GUIDE_FILE);
            bool guideExists = System.IO.File.Exists(guidePath);
            string guide = guideExists ? System.IO.File.ReadAllText(guidePath) : string.Empty;
            Assert(guideExists, $"C6: existe {GUIDE_FILE} en la raíz del repositorio", ref total, ref passed);
            Assert(guide.Contains("Road Corner_02") && guide.Contains("ResetTracking") && guide.Contains("Observaciones y decisiones para el futuro"),
                "R6/doc: la guía menciona Road Corner_02, ResetTracking y la sección de observaciones", ref total, ref passed);
            Assert(guide.Contains("Tools/Primer Volante/Turn Signal/Apply To Open Scene") && guide.Contains("Tools/Primer Volante/Turn Signal/Apply To Selected Scenes") &&
                   guide.Contains("Tools/Primer Volante/Turn Signal/Remove From Open Scene"),
                "C6: la guía menciona los tres MenuItems de la herramienta", ref total, ref passed);

            string infracciones = System.IO.File.ReadAllText(System.IO.Path.Combine(repoRoot, "INFRACCIONES.md"));
            Assert(infracciones.Contains("TURN_SIGNAL") && infracciones.Contains(GUIDE_FILE), "C6: INFRACCIONES.md lista TURN_SIGNAL y enlaza a la guía", ref total, ref passed);

            if (!System.IO.File.Exists(TMP_SCENE_SOURCE))
            {
                Assert(false, $"C6: existe la escena de ciudad para la prueba ({TMP_SCENE_SOURCE})", ref total, ref passed);
                return;
            }

            string tmpScene = TMP_FOLDER + "/CityTmp.unity";
            string originalScene = SceneManager.GetActiveScene().path;
            AssetDatabase.DeleteAsset(TMP_FOLDER);
            AssetDatabase.CreateFolder("Assets/Scenes/TestingScenes", "_TmpTurnSignal");
            try
            {
                if (!AssetDatabase.CopyAsset(TMP_SCENE_SOURCE, tmpScene))
                {
                    Assert(false, "C6: se pudo copiar la escena de ciudad a la carpeta temporal", ref total, ref passed);
                    return;
                }

                int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);

                // R7: con la escena dirty, Apply y Remove abortan sin guardar ni agregar el detector (escena aditiva, sin tocar la abierta).
                DateTime stampBefore = System.IO.File.GetLastWriteTimeUtc(tmpScene);
                int applyResult = 0, removeResult = 0, detectorsInMemory = -1;
                bool stillDirty = false;
                LogType previousFilter = Debug.unityLogger.filterLogType;
                Debug.unityLogger.filterLogType = LogType.Exception;
                try
                {
                    VerificationSceneUtil.WithScene(tmpScene, scene =>
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        applyResult = TurnSignalSceneTool.ApplyToLoadedScene(scene);
                        removeResult = TurnSignalSceneTool.RemoveFromLoadedScene(scene);
                        detectorsInMemory = AllInScene<TurnSignalDetector>(scene).Length;
                        stillDirty = scene.isDirty;
                    });
                }
                finally
                {
                    Debug.unityLogger.filterLogType = previousFilter;
                }

                int carsD = 0, detectorsD = 0, goodMaskD = 0;
                CountDetectors(tmpScene, layer, ref carsD, ref detectorsD, ref goodMaskD);
                Assert(applyResult == -1 && removeResult == -1 && detectorsInMemory == 0 && stillDirty && detectorsD == 0 &&
                       System.IO.File.GetLastWriteTimeUtc(tmpScene) == stampBefore,
                    $"R7: Apply/Remove sobre una escena dirty abortan sin guardar ni agregar el detector (apply {applyResult}, remove {removeResult}, detectores {detectorsInMemory}/{detectorsD})", ref total, ref passed);

                bool applied = TurnSignalSceneTool.ApplyToScenes(new[] { tmpScene });
                int cars = 0, detectors = 0, goodMask = 0;
                CountDetectors(tmpScene, layer, ref cars, ref detectors, ref goodMask);
                Assert(applied && cars >= 1 && detectors == cars && goodMask == cars,
                    $"C6: ApplyToScenes deja un único TurnSignalDetector por auto con la máscara correcta (autos {cars}, detectores {detectors}, máscara ok {goodMask})", ref total, ref passed);

                bool appliedAgain = TurnSignalSceneTool.ApplyToScenes(new[] { tmpScene });
                int cars2 = 0, detectors2 = 0, goodMask2 = 0;
                CountDetectors(tmpScene, layer, ref cars2, ref detectors2, ref goodMask2);
                Assert(appliedAgain && detectors2 == detectors && goodMask2 == cars2, $"C6: correrla dos veces no duplica el detector ({detectors2})", ref total, ref passed);

                // Máscara rota a mano: la herramienta la corrige.
                VerificationSceneUtil.WithScene(tmpScene, scene =>
                {
                    foreach (TurnSignalDetector d in AllInScene<TurnSignalDetector>(scene)) d.ZoneMask = 0;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                });
                bool fixedMask = TurnSignalSceneTool.ApplyToScenes(new[] { tmpScene });
                int cars3 = 0, detectors3 = 0, goodMask3 = 0;
                CountDetectors(tmpScene, layer, ref cars3, ref detectors3, ref goodMask3);
                Assert(fixedMask && goodMask3 == cars3 && detectors3 == detectors, "C6: si ya lo tiene, la herramienta corrige la máscara sin duplicar", ref total, ref passed);

                bool removed = TurnSignalSceneTool.RemoveFromScenes(new[] { tmpScene });
                int cars4 = 0, detectors4 = 0, goodMask4 = 0;
                CountDetectors(tmpScene, layer, ref cars4, ref detectors4, ref goodMask4);
                Assert(removed && cars4 == cars && detectors4 == 0, $"C6: Remove saca el detector ({detectors4} restantes)", ref total, ref passed);
            }
            finally
            {
                if (SceneManager.GetActiveScene().path == tmpScene)
                {
                    if (!string.IsNullOrEmpty(originalScene) && System.IO.File.Exists(originalScene)) EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }

                AssetDatabase.DeleteAsset(TMP_FOLDER);
                AssetDatabase.Refresh();
            }

            Assert(!AssetDatabase.IsValidFolder(TMP_FOLDER), "C6: la carpeta temporal se borró", ref total, ref passed);
        }

        // Cuenta los autos del jugador, los detectores y los que tienen la máscara correcta en la escena (abierta de forma aditiva).
        private static void CountDetectors(string scenePath, int layer, ref int cars, ref int detectors, ref int goodMask)
        {
            int c = 0, d = 0, g = 0;
            VerificationSceneUtil.WithScene(scenePath, scene =>
            {
                List<VehicleController> found = TurnSignalSceneTool.FindPlayerCars(scene);
                c = found.Count;
                d = AllInScene<TurnSignalDetector>(scene).Length;
                g = found.Count(v => v.GetComponents<TurnSignalDetector>().Length == 1 && v.GetComponent<TurnSignalDetector>().ZoneMask.value == 1 << layer);
            });
            cars = c;
            detectors = d;
            goodMask = g;
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
            return string.Join(", ", sim.Faults.Select(f => $"{f.Direction}/{f.Reason}"));
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
