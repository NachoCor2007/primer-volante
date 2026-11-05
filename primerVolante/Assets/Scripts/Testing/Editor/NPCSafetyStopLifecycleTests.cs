using PrimerVolante.Environment;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Pruebas en Editor para la integración y gestión del ciclo de vida de NPCSafetyStop sincronizado con NPCPathFollower:
    /// 1. NPC sin ruta -> NPCSafetyStop.enabled == false.
    /// 2. Play() con ruta -> NPCSafetyStop.enabled == true.
    /// 3. Frenado de emergencia por obstáculo -> se mantiene enabled == true.
    /// 4. Fin de ruta en modo Stop -> se apaga enabled == false.
    /// 5. Ruta en modo Loop -> no se apaga a lo largo de las vueltas.
    /// 6. Vehículos estacionados / decorativos sin ruta -> sensor desactivado sin consumo de CPU física.
    /// </summary>
    public static class NPCSafetyStopLifecycleTests
    {
        [MenuItem("Tools/Primer Volante/Run NPC Safety Stop Tests")]
        public static void RunAllTests()
        {
            Debug.Log("=========================================================");
            Debug.Log("🛡️ INICIANDO PRUEBAS DE CICLO DE VIDA DE NPCSAFETYSTOP");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            TestWithoutPathSafetyStopDisabled(ref total, ref passed);
            TestPlayWithPathEnablesSafetyStop(ref total, ref passed);
            TestEmergencyBrakingStaysEnabled(ref total, ref passed);
            TestEndOfRouteStopDisablesSafetyStop(ref total, ref passed);
            TestLoopRouteStaysEnabled(ref total, ref passed);
            TestParkedAndDecorativeVehicles(ref total, ref passed);
            TestMutualDetectionUnderSharedParent(ref total, ref passed);
            TestCurveSensorAdaptation(ref total, ref passed);
            TestCurveSensorSmoothing(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas de NPCSafetyStop pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        public static void RunTests(ref int total, ref int passed)
        {
            TestWithoutPathSafetyStopDisabled(ref total, ref passed);
            TestPlayWithPathEnablesSafetyStop(ref total, ref passed);
            TestEmergencyBrakingStaysEnabled(ref total, ref passed);
            TestEndOfRouteStopDisablesSafetyStop(ref total, ref passed);
            TestLoopRouteStaysEnabled(ref total, ref passed);
            TestParkedAndDecorativeVehicles(ref total, ref passed);
        }

        // ---------------------------------------------------------------- 1. Sin ruta
        private static void TestWithoutPathSafetyStopDisabled(ref int total, ref int passed)
        {
            GameObject npcObj = new GameObject("TestNPC_NoPath");
            try
            {
                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();

                Assert(!follower.HasPath, "NPC nuevo no tiene ruta válida asignada (!HasPath)", ref total, ref passed);
                Assert(!safety.enabled, "NPC sin ruta tiene NPCSafetyStop deshabilitado (enabled == false)", ref total, ref passed);

                // Intentar iniciar la marcha sin ruta
                follower.Play();
                Assert(!follower.IsPlaying, "Play() sin ruta no inicia la marcha (!IsPlaying)", ref total, ref passed);
                Assert(!safety.enabled, "NPCSafetyStop permanece deshabilitado tras Play() sin ruta", ref total, ref passed);

                // Sincronización explícita de ciclo de vida
                safety.SyncLifecycle();
                Assert(!safety.enabled, "SyncLifecycle() mantiene deshabilitado el sensor sin ruta", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(npcObj);
            }
        }

        // ---------------------------------------------------------------- 2. Play con ruta
        private static void TestPlayWithPathEnablesSafetyStop(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline_Play");
            GameObject npcObj = new GameObject("TestNPC_Play");
            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 50f)));
                splineContainer.Spline = spline;

                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();

                follower.SetPath(splineContainer, 0);
                Assert(follower.HasPath, "Ruta asignada correctamente (HasPath == true)", ref total, ref passed);
                Assert(!follower.IsPlaying, "NPC aún no inició la marcha (!IsPlaying)", ref total, ref passed);
                Assert(!safety.enabled, "Sensor deshabilitado antes de iniciar la marcha", ref total, ref passed);

                // Iniciar marcha
                follower.Play();
                Assert(follower.IsPlaying, "Follower inicia la marcha (IsPlaying == true)", ref total, ref passed);
                Assert(safety.enabled, "NPCSafetyStop se activa automáticamente al llamar a Play()", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        // ---------------------------------------------------------------- 3. Frenado de emergencia
        private static void TestEmergencyBrakingStaysEnabled(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline_Braking");
            GameObject npcObj = new GameObject("TestNPC_Braking");
            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 60f)));
                splineContainer.Spline = spline;

                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();
                safety.ResumeDelay = 0.3f;
                safety.DetectionDistance = 5.0f;

                follower.SetPath(splineContainer, 0);
                follower.Play();
                Assert(safety.enabled, "Sensor habilitado al comenzar marcha", ref total, ref passed);

                // Obstáculo detectado en la trayectoria
                safety.SetSimulatedObstacle(true, 3.0f);
                safety.Tick(0.02f);

                Assert(safety.IsEmergencyBraking, "Frenado de emergencia activo (IsEmergencyBraking == true)", ref total, ref passed);
                Assert(follower.IsEmergencyStopped, "Follower frenado en seco (IsEmergencyStopped == true)", ref total, ref passed);
                Assert(safety.enabled, "NPCSafetyStop PERMANECE HABILITADO durante frenado de emergencia para censar despeje", ref total, ref passed);

                // Varios ticks con obstáculo presente
                for (int i = 0; i < 5; i++)
                {
                    safety.Tick(0.02f);
                }
                Assert(safety.IsEmergencyBraking, "Permanece frenado mientras obstáculo persiste", ref total, ref passed);
                Assert(safety.enabled, "NPCSafetyStop sigue habilitado tras múltiples ticks frenado", ref total, ref passed);

                // Despeje de vía y reanudación
                safety.SetSimulatedObstacle(false);
                safety.Tick(0.35f); // Supera ResumeDelay (0.3s)

                Assert(!safety.IsEmergencyBraking, "Frenado de emergencia liberado tras despejarse la vía", ref total, ref passed);
                Assert(!follower.IsEmergencyStopped, "Follower ya no está frenado por emergencia", ref total, ref passed);
                Assert(follower.IsPlaying, "Follower reanudó marcha", ref total, ref passed);
                Assert(safety.enabled, "NPCSafetyStop sigue habilitado tras reanudar marcha", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        // ---------------------------------------------------------------- 4. Fin de ruta en modo Stop
        private static void TestEndOfRouteStopDisablesSafetyStop(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline_StopEnd");
            GameObject npcObj = new GameObject("TestNPC_StopEnd");
            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 20f)));
                splineContainer.Spline = spline;

                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();

                follower.EndBehavior = NPCPathEndBehavior.Stop;
                follower.SetPath(splineContainer, 0);
                follower.Play();
                Assert(safety.enabled, "Sensor habilitado durante marcha hacia el final", ref total, ref passed);

                // Teletransportar a 5 cm antes del final y avanzar
                follower.TeleportToDistance(follower.PathLength - 0.05f);
                follower.Play();
                follower.Tick(0.2f); // Avanza y supera el final de ruta

                Assert(!follower.IsPlaying, "Follower se detuvo al llegar al final en modo Stop", ref total, ref passed);
                Assert(!safety.enabled, "NPCSafetyStop se DESHABILITA (enabled == false) al llegar al final de ruta en Stop", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        // ---------------------------------------------------------------- 5. Ruta en modo Loop
        private static void TestLoopRouteStaysEnabled(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline_Loop");
            GameObject npcObj = new GameObject("TestNPC_Loop");
            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 20f)));
                spline.Add(new BezierKnot(new float3(20f, 0f, 20f)));
                spline.Add(new BezierKnot(new float3(20f, 0f, 0f)));
                spline.Closed = true;
                splineContainer.Spline = spline;

                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();

                follower.EndBehavior = NPCPathEndBehavior.Loop;
                follower.SetPath(splineContainer, 0);
                follower.Play();

                Assert(follower.EndBehavior == NPCPathEndBehavior.Loop, "Modo Loop activo en spline cerrada", ref total, ref passed);
                Assert(safety.enabled, "Sensor habilitado al iniciar recorrido en loop", ref total, ref passed);

                // Teletransportar justo antes del cierre del loop y avanzar para cruzar a la vuelta siguiente
                follower.TeleportToDistance(follower.PathLength - 0.1f);
                follower.Play();
                follower.Tick(0.5f); // Cruza el cierre y da la vuelta

                Assert(follower.IsPlaying, "Follower continúa en marcha tras dar la vuelta en Loop", ref total, ref passed);
                Assert(follower.DistanceTravelled < follower.PathLength, "Distancia da la vuelta correctamente", ref total, ref passed);
                Assert(safety.enabled, "NPCSafetyStop PERMANECE HABILITADO indefinidamente a lo largo de las vueltas en Loop", ref total, ref passed);

                // Avanzar varios pasos adicionales de simulación
                for (int i = 0; i < 10; i++)
                {
                    follower.Tick(0.1f);
                }
                Assert(safety.enabled, "NPCSafetyStop sigue habilitado tras múltiples pasos en Loop", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        // ---------------------------------------------------------------- 6. Autos estacionados / decorativos
        private static void TestParkedAndDecorativeVehicles(ref int total, ref int passed)
        {
            GameObject parkedObj = new GameObject("TestVehicle_Parked");
            GameObject decorativeObj = new GameObject("TestVehicle_Decorative");
            try
            {
                // Auto estacionado: tiene NPCPathFollower pero sin ruta asignada y PlayOnStart apagado
                var parkedFollower = parkedObj.AddComponent<NPCPathFollower>();
                parkedFollower.PlayOnStart = false;
                var parkedSafety = parkedObj.AddComponent<NPCSafetyStop>();

                Assert(!parkedFollower.HasPath, "Auto estacionado no tiene ruta", ref total, ref passed);
                Assert(!parkedSafety.enabled, "Auto estacionado tiene sensor deshabilitado", ref total, ref passed);

                // Objeto decorativo con NPCSafetyStop pero sin seguidor o con seguidor estático
                var decorativeSafety = decorativeObj.AddComponent<NPCSafetyStop>();
                decorativeSafety.SyncLifecycle();
                Assert(!decorativeSafety.enabled, "Objeto decorativo sin ruta mantiene sensor deshabilitado", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(parkedObj);
                Object.DestroyImmediate(decorativeObj);
            }
        }

        // ---------------------------------------------------------------- 7. Detección mutua entre NPCs en mismo contenedor
        private static void TestMutualDetectionUnderSharedParent(ref int total, ref int passed)
        {
            GameObject parentGroup = new GameObject("NPC_Demo_Group");
            try
            {
                // NPC Vehículo A
                GameObject npcA = new GameObject("NPC_Vehicle_A");
                npcA.transform.SetParent(parentGroup.transform);
                var rbA = npcA.AddComponent<Rigidbody>();
                rbA.isKinematic = true;
                var colA = npcA.AddComponent<BoxCollider>();
                colA.size = new Vector3(2f, 1.5f, 4f);
                var followerA = npcA.AddComponent<NPCPathFollower>();
                var safetyA = npcA.AddComponent<NPCSafetyStop>();

                // Hijo de NPC A (ej. rueda o accesorio)
                GameObject childA = new GameObject("Wheel_FL");
                childA.transform.SetParent(npcA.transform);
                var childColA = childA.AddComponent<BoxCollider>();

                // NPC Vehículo B (hermano bajo el mismo grupo)
                GameObject npcB = new GameObject("NPC_Vehicle_B");
                npcB.transform.SetParent(parentGroup.transform);
                var rbB = npcB.AddComponent<Rigidbody>();
                rbB.isKinematic = true;
                var colB = npcB.AddComponent<BoxCollider>();
                colB.size = new Vector3(2f, 1.5f, 4f);
                var followerB = npcB.AddComponent<NPCPathFollower>();
                var safetyB = npcB.AddComponent<NPCSafetyStop>();

                // NPC Peatón C (hermano bajo el mismo grupo)
                GameObject npcPed = new GameObject("NPC_Pedestrian_C");
                npcPed.transform.SetParent(parentGroup.transform);
                var rbPed = npcPed.AddComponent<Rigidbody>();
                rbPed.isKinematic = true;
                var colPed = npcPed.AddComponent<BoxCollider>();
                colPed.size = new Vector3(0.5f, 1.8f, 0.5f);
                var followerPed = npcPed.AddComponent<NPCPathFollower>();
                var safetyPed = npcPed.AddComponent<NPCSafetyStop>();

                // 7.1 Confirmar que comparten el mismo transform.root (escenario del bug)
                Assert(npcA.transform.root == npcB.transform.root && npcA.transform.root == parentGroup.transform,
                    "NPCs comparten el mismo transform.root contenedor (grupo común)", ref total, ref passed);

                // 7.2 Validación local: NPC A ignora sus propios colliders e hijos
                Assert(!safetyA.IsValidObstacleCollider(colA),
                    "NPC A ignora su propio BoxCollider", ref total, ref passed);
                Assert(!safetyA.IsValidObstacleCollider(childColA),
                    "NPC A ignora el BoxCollider de su propio hijo (Wheel_FL)", ref total, ref passed);

                // 7.3 Detección mutua: NPC A detecta a NPC B y al Peatón a pesar de compartir root
                Assert(safetyA.IsValidObstacleCollider(colB),
                    "NPC A DETECTA a NPC B en el mismo grupo contenedor como obstáculo válido", ref total, ref passed);
                Assert(safetyA.IsValidObstacleCollider(colPed),
                    "NPC A DETECTA al Peatón en el mismo grupo contenedor como obstáculo válido", ref total, ref passed);

                // 7.4 Validación simétrica en NPC B
                Assert(!safetyB.IsValidObstacleCollider(colB),
                    "NPC B ignora su propio BoxCollider", ref total, ref passed);
                Assert(safetyB.IsValidObstacleCollider(colA),
                    "NPC B DETECTA a NPC A en el mismo grupo contenedor como obstáculo válido", ref total, ref passed);
                Assert(safetyB.IsValidObstacleCollider(colPed),
                    "NPC B DETECTA al Peatón en el mismo grupo contenedor como obstáculo válido", ref total, ref passed);

                // 7.5 Validación simétrica en Peatón
                Assert(!safetyPed.IsValidObstacleCollider(colPed),
                    "Peatón ignora su propio BoxCollider", ref total, ref passed);
                Assert(safetyPed.IsValidObstacleCollider(colA),
                    "Peatón DETECTA a NPC A en el mismo grupo contenedor como obstáculo válido", ref total, ref passed);

                // 7.6 ForceEmergencyBrake respeta filtros locales
                safetyA.ForceEmergencyBrake(childColA);
                Assert(!safetyA.IsEmergencyBraking,
                    "ForceEmergencyBrake NO se activa con collider propio o de hijo", ref total, ref passed);

                safetyA.ForceEmergencyBrake(colB);
                Assert(safetyA.IsEmergencyBraking,
                    "ForceEmergencyBrake SÍ se activa ante colisión con otro NPC del mismo grupo", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(parentGroup);
            }
        }

        // ---------------------------------------------------------------- 8. Adaptación en curvas
        private static void TestCurveSensorAdaptation(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline_Curve");
            GameObject npcObj = new GameObject("TestNPC_Curve");
            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                // Recta inicial de 10m y luego curva cerrada de 90° hacia la derecha
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f), new float3(0f, 0f, -3f), new float3(0f, 0f, 3f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 10f), new float3(0f, 0f, -3f), new float3(0f, 0f, 3f)));
                spline.Add(new BezierKnot(new float3(15f, 0f, 25f), new float3(-3f, 0f, 0f), new float3(3f, 0f, 0f)));
                splineContainer.Spline = spline;

                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();
                safety.DetectionDistance = 8.0f;
                safety.MinCurveDetectionDistance = 3.5f;
                safety.CurveAngleThreshold = 8.0f;
                safety.MaxCurveAngle = 45.0f;
                safety.AdaptSensorInCurves = true;

                follower.SetPath(splineContainer, 0);

                // 8.1 En recta: sensor recto y distancia base completa
                follower.TeleportToDistance(0f);
                safety.GetSensorParameters(out Vector3 originStraight, out Vector3 dirStraight, out Quaternion rotStraight, out float distStraight);
                Assert(Mathf.Abs(distStraight - 8.0f) < 0.1f, "En tramo recto la distancia del sensor se mantiene completa (~8.0m)", ref total, ref passed);
                Assert(Vector3.Angle(dirStraight, npcObj.transform.forward) < 1.0f, "En tramo recto la dirección del sensor coincide con transform.forward", ref total, ref passed);

                // 8.2 Acercándose / entrando a la curva (10m): la distancia se reduce y la dirección gira hacia X positivo
                follower.TeleportToDistance(9.0f);
                safety.GetSensorParameters(out Vector3 originCurve, out Vector3 dirCurve, out Quaternion rotCurve, out float distCurve);
                Assert(distCurve < distStraight - 1.0f, "Al entrar en curva la distancia del sensor se reduce significativamente", ref total, ref passed);
                Assert(dirCurve.x > 0.1f, "La dirección del sensor gira hacia la curva (X > 0)", ref total, ref passed);

                // 8.3 Con AdaptSensorInCurves = false: no reduce ni orienta
                safety.AdaptSensorInCurves = false;
                safety.GetSensorParameters(out Vector3 originOff, out Vector3 dirOff, out Quaternion rotOff, out float distOff);
                Assert(Mathf.Abs(distOff - 8.0f) < 0.1f, "Con AdaptSensorInCurves=false la distancia permanece en el valor base", ref total, ref passed);
                Assert(Vector3.Angle(dirOff, npcObj.transform.forward) < 1.0f, "Con AdaptSensorInCurves=false la dirección sigue en transform.forward", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        // ---------------------------------------------------------------- 9. Suavizado del sensor en curvas
        private const float k_SmoothStep = 0.02f;

        private static void TestCurveSensorSmoothing(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline_CurveSmoothing");
            GameObject npcObj = new GameObject("TestNPC_CurveSmoothing");
            GameObject loopSplineObj = null;
            GameObject loopNpcObj = null;
            GameObject circleSplineObj = null;
            GameObject circleNpcObj = null;
            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                // Misma spline que el test 8: recta de 10m y curva de 90° a la derecha
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f), new float3(0f, 0f, -3f), new float3(0f, 0f, 3f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 10f), new float3(0f, 0f, -3f), new float3(0f, 0f, 3f)));
                spline.Add(new BezierKnot(new float3(15f, 0f, 25f), new float3(-3f, 0f, 0f), new float3(3f, 0f, 0f)));
                splineContainer.Spline = spline;

                var follower = npcObj.AddComponent<NPCPathFollower>();
                var safety = npcObj.AddComponent<NPCSafetyStop>();
                safety.DetectionDistance = 8.0f;
                safety.MinCurveDetectionDistance = 3.5f;
                safety.CurveAngleThreshold = 8.0f;
                safety.MaxCurveAngle = 45.0f;
                safety.AdaptSensorInCurves = true;
                safety.SensorSmoothingTime = 0.15f;

                follower.SetPath(splineContainer, 0);
                follower.Play();

                // 9.1 / 9.2 / 9.7: vuelta redondeada realista (cuadrado de 60x60 m, esquinas de R=6 m, Loop, como Route_Vehicle_Block).
                // La spline del test 8 (radio ~1.4 m) no es representativa de las curvas reales.
                loopSplineObj = new GameObject("TestSpline_CurveSmoothingLoop");
                loopNpcObj = new GameObject("TestNPC_CurveSmoothingLoop");
                SplineContainer loopContainer = loopSplineObj.AddComponent<SplineContainer>();
                loopContainer.Spline = NPCDemoRoutesSetup.BuildRoundedLoop(new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 60f), new Vector3(60f, 0f, 60f), new Vector3(60f, 0f, 0f),
                }, 6.0f, null);

                var loopFollower = loopNpcObj.AddComponent<NPCPathFollower>();
                var loopSafety = loopNpcObj.AddComponent<NPCSafetyStop>();
                loopSafety.DetectionDistance = 5.0f;
                loopSafety.MinCurveDetectionDistance = 3.5f;
                loopSafety.CurveAngleThreshold = 8.0f;
                loopSafety.MaxCurveAngle = 45.0f;
                loopSafety.AdaptSensorInCurves = true;
                loopSafety.SensorSmoothingTime = 0.15f;
                loopFollower.EndBehavior = NPCPathEndBehavior.Loop;
                loopFollower.SetPath(loopContainer, 0);
                loopFollower.Play();

                SensorTrace trace = RecordSensorTrace(loopFollower, loopSafety, 8f, loopFollower.PathLength);
                Assert(loopFollower.EndBehavior == NPCPathEndBehavior.Loop && trace.Steps > 500,
                    $"[9.0] La vuelta de prueba (Loop) avanza una vuelta completa ({trace.Steps} pasos, {loopFollower.PathLength:F1}m)", ref total, ref passed);
                Assert(trace.MaxInstantYawStep < 3.0f,
                    $"[9.1] Sin salto en el umbral: |Δyaw| instantáneo máx por paso < 3° (medido {trace.MaxInstantYawStep:F2}°)", ref total, ref passed);
                Assert(trace.MaxSmoothRelStep < trace.MaxInstantRelStep,
                    $"[9.2a] El offset de yaw relativo suavizado cambia menos por paso que el instantáneo ({trace.MaxSmoothRelStep:F3}° < {trace.MaxInstantRelStep:F3}°; yaw de mundo {trace.MaxSmoothYawStep:F3}° vs {trace.MaxInstantYawStep:F3}°)", ref total, ref passed);
                Assert(trace.MaxSmoothDistStep < trace.MaxInstantDistStep,
                    $"[9.2b] La distancia suavizada cambia menos por paso que la instantánea ({trace.MaxSmoothDistStep:F4}m < {trace.MaxInstantDistStep:F4}m)", ref total, ref passed);
                Assert(trace.MinInstantDist < 5.0f - 1.0f,
                    $"[9.2c] La adaptación en curva se mantiene: el alcance baja en la curva (mín {trace.MinInstantDist:F2}m)", ref total, ref passed);
                // 9.7 Curva sostenida: círculo de R=6 m a 8 m/s (arco largo). En la esquina de 90° de la vuelta el arco dura
                // ~1 s y su salida cambia el objetivo, así que no sirve para medir el régimen permanente del filtro.
                circleSplineObj = new GameObject("TestSpline_CurveSmoothingCircle");
                circleNpcObj = new GameObject("TestNPC_CurveSmoothingCircle");
                SplineContainer circleContainer = circleSplineObj.AddComponent<SplineContainer>();
                const float radius = 6f;
                float k = 0.5523f * radius;
                Spline circle = new Spline();
                circle.Add(new BezierKnot(new float3(radius, 0f, 0f), new float3(0f, 0f, -k), new float3(0f, 0f, k)));
                circle.Add(new BezierKnot(new float3(0f, 0f, radius), new float3(k, 0f, 0f), new float3(-k, 0f, 0f)));
                circle.Add(new BezierKnot(new float3(-radius, 0f, 0f), new float3(0f, 0f, k), new float3(0f, 0f, -k)));
                circle.Add(new BezierKnot(new float3(0f, 0f, -radius), new float3(-k, 0f, 0f), new float3(k, 0f, 0f)));
                circle.Closed = true;
                circleContainer.Spline = circle;

                var circleFollower = circleNpcObj.AddComponent<NPCPathFollower>();
                var circleSafety = circleNpcObj.AddComponent<NPCSafetyStop>();
                circleSafety.DetectionDistance = 5.0f;
                circleSafety.SensorSmoothingTime = 0.15f;
                circleFollower.EndBehavior = NPCPathEndBehavior.Loop;
                circleFollower.SetPath(circleContainer, 0);
                circleFollower.Play();

                SensorTrace circleTrace = RecordSensorTrace(circleFollower, circleSafety, 8f, circleFollower.PathLength * 2f);
                Assert(circleTrace.ArcSteps > 100 && circleTrace.MaxArcLag < 1.0f,
                    $"[9.7] Sin desfase en curva sostenida (R=6 m): tras 0.5 s en el arco el yaw de mundo suavizado queda a < 1° del instantáneo (máx {circleTrace.MaxArcLag:F3}° en {circleTrace.ArcSteps} pasos)", ref total, ref passed);

                // Para los casos siguientes se vuelve a la spline del test 8 (recta + curva)
                follower.SetPath(splineContainer, 0);
                follower.Play();

                // 9.3 Convergencia con la pose fija (a mitad de ruta, ya en curva): 1 s de Tick
                follower.TeleportToDistance(follower.PathLength * 0.55f);
                safety.GetSensorParameters(out _, out Vector3 dirInstant, out _, out float distInstant);
                for (int i = 0; i < 50; i++) safety.Tick(k_SmoothStep);
                safety.GetSmoothedSensorParameters(out _, out Vector3 dirSmooth, out _, out float distSmooth);
                float convAngle = Vector3.Angle(dirInstant, dirSmooth);
                float convDist = Mathf.Abs(distInstant - distSmooth);
                Assert(convAngle < 0.5f && convDist < 0.05f,
                    $"[9.3] Con la pose fija, tras 1 s el suavizado converge al instantáneo (Δ{convAngle:F3}° / Δ{convDist:F4}m)", ref total, ref passed);

                // 9.4 Snap inicial tras ResetSafetyState
                safety.ResetSafetyState();
                safety.Tick(k_SmoothStep);
                safety.GetSensorParameters(out _, out Vector3 dirI4, out _, out float distI4);
                safety.GetSmoothedSensorParameters(out _, out Vector3 dirS4, out _, out float distS4);
                Assert(Vector3.Angle(dirI4, dirS4) < 0.01f && Mathf.Abs(distI4 - distS4) < 0.001f,
                    "[9.4] Tras ResetSafetyState el primer Tick deja el suavizado igual al instantáneo", ref total, ref passed);

                // 9.5 SensorSmoothingTime = 0: suavizado == instantáneo en cada paso
                safety.SensorSmoothingTime = 0f;
                SensorTrace noSmooth = RecordSensorTrace(follower, safety, 6f, follower.PathLength - 1f);
                Assert(noSmooth.MaxYawDiff < 0.01f && noSmooth.MaxDistDiff < 0.001f,
                    $"[9.5] Con SensorSmoothingTime=0 el suavizado coincide con el instantáneo en cada paso (Δ{noSmooth.MaxYawDiff:F4}° / Δ{noSmooth.MaxDistDiff:F5}m)", ref total, ref passed);
                safety.SensorSmoothingTime = 0.15f;

                // 9.6 En recta: sensor alineado con el frente y distancia completa
                follower.TeleportToDistance(0f);
                safety.ResetSafetyState();
                for (int i = 0; i < 10; i++) safety.Tick(k_SmoothStep);
                safety.GetSmoothedSensorParameters(out _, out Vector3 dirStraight, out _, out float distStraight);
                Assert(Vector3.Angle(dirStraight, npcObj.transform.forward) < 1.0f && Mathf.Abs(distStraight - 8.0f) < 0.1f,
                    $"[9.6] En recta el sensor suavizado queda alineado con transform.forward y con la distancia completa ({Vector3.Angle(dirStraight, npcObj.transform.forward):F3}° / {distStraight:F2}m)", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
                if (loopSplineObj != null) Object.DestroyImmediate(loopSplineObj);
                if (loopNpcObj != null) Object.DestroyImmediate(loopNpcObj);
                if (circleSplineObj != null) Object.DestroyImmediate(circleSplineObj);
                if (circleNpcObj != null) Object.DestroyImmediate(circleNpcObj);
            }
        }

        private struct SensorTrace
        {
            public int Steps;
            public float MaxInstantYawStep;
            public float MaxSmoothYawStep;
            public float MaxInstantRelStep;
            public float MaxSmoothRelStep;
            public float MaxInstantDistStep;
            public float MaxSmoothDistStep;
            public float MinInstantDist;
            public float MaxYawDiff;
            public float MaxDistDiff;
            public int ArcSteps;
            public float MaxArcLag;
        }

        /// <summary>
        /// Recorre la ruta desde el inicio a velocidad fija llamando a Tick de a pasos y mide los cambios por paso del
        /// yaw de mundo, del offset de yaw relativo al vehículo y de la distancia del sensor, instantáneo vs suavizado.
        /// Termina al recorrer <paramref name="travel"/> metros. También mide el desfase del yaw de mundo suavizado
        /// respecto del instantáneo mientras el vehículo lleva al menos 0.5 s dentro de una curva sostenida.
        /// </summary>
        private static SensorTrace RecordSensorTrace(NPCPathFollower follower, NPCSafetyStop safety, float speed, float travel)
        {
            follower.TeleportToDistance(0f);
            follower.Play();
            safety.ResetSafetyState();

            SensorTrace trace = default;
            trace.MinInstantDist = float.MaxValue;
            bool hasPrev = false;
            float prevInstYaw = 0f, prevSmoothYaw = 0f, prevInstDist = 0f, prevSmoothDist = 0f;
            float prevInstRel = 0f, prevSmoothRel = 0f, prevVehicleYaw = 0f, prevPathDistance = 0f, travelled = 0f;
            int stepsInArc = 0;

            for (int step = 0; step < 6000 && travelled < travel; step++)
            {
                follower.SetSpeedForTesting(speed);
                follower.Tick(k_SmoothStep);
                safety.Tick(k_SmoothStep);

                float pathDistance = follower.DistanceTravelled;
                if (step > 0)
                {
                    float delta = pathDistance - prevPathDistance;
                    if (delta < -follower.PathLength * 0.5f) delta += follower.PathLength;
                    travelled += Mathf.Max(0f, delta);
                }
                prevPathDistance = pathDistance;

                safety.GetSensorParameters(out _, out Vector3 dirI, out _, out float distI);
                safety.GetSmoothedSensorParameters(out _, out Vector3 dirS, out _, out float distS);
                float yawI = Mathf.Atan2(dirI.x, dirI.z) * Mathf.Rad2Deg;
                float yawS = Mathf.Atan2(dirS.x, dirS.z) * Mathf.Rad2Deg;
                float vehicleYaw = safety.transform.eulerAngles.y;
                float relI = Mathf.DeltaAngle(vehicleYaw, yawI);
                float relS = Mathf.DeltaAngle(vehicleYaw, yawS);

                trace.MinInstantDist = Mathf.Min(trace.MinInstantDist, distI);
                trace.MaxYawDiff = Mathf.Max(trace.MaxYawDiff, Mathf.Abs(Mathf.DeltaAngle(yawI, yawS)));
                trace.MaxDistDiff = Mathf.Max(trace.MaxDistDiff, Mathf.Abs(distI - distS));

                if (hasPrev)
                {
                    trace.MaxInstantYawStep = Mathf.Max(trace.MaxInstantYawStep, Mathf.Abs(Mathf.DeltaAngle(prevInstYaw, yawI)));
                    trace.MaxSmoothYawStep = Mathf.Max(trace.MaxSmoothYawStep, Mathf.Abs(Mathf.DeltaAngle(prevSmoothYaw, yawS)));
                    trace.MaxInstantRelStep = Mathf.Max(trace.MaxInstantRelStep, Mathf.Abs(Mathf.DeltaAngle(prevInstRel, relI)));
                    trace.MaxSmoothRelStep = Mathf.Max(trace.MaxSmoothRelStep, Mathf.Abs(Mathf.DeltaAngle(prevSmoothRel, relS)));
                    trace.MaxInstantDistStep = Mathf.Max(trace.MaxInstantDistStep, Mathf.Abs(distI - prevInstDist));
                    trace.MaxSmoothDistStep = Mathf.Max(trace.MaxSmoothDistStep, Mathf.Abs(distS - prevSmoothDist));

                    // Curva sostenida: el vehículo gira más de 0.5° por paso; se mide pasados 0.5 s (25 pasos) dentro del arco
                    bool turning = Mathf.Abs(Mathf.DeltaAngle(prevVehicleYaw, vehicleYaw)) > 0.5f;
                    stepsInArc = turning ? stepsInArc + 1 : 0;
                    if (stepsInArc >= 25)
                    {
                        trace.ArcSteps++;
                        trace.MaxArcLag = Mathf.Max(trace.MaxArcLag, Mathf.Abs(Mathf.DeltaAngle(yawI, yawS)));
                    }
                }

                prevInstYaw = yawI; prevSmoothYaw = yawS; prevInstDist = distI; prevSmoothDist = distS;
                prevInstRel = relI; prevSmoothRel = relS; prevVehicleYaw = vehicleYaw;
                hasPrev = true;
                trace.Steps++;
            }

            return trace;
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
