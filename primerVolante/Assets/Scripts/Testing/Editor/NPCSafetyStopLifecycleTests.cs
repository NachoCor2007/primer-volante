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
            TestMutualDetectionUnderSharedParent(ref total, ref passed);

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
