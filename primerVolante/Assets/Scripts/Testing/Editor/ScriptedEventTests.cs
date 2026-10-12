using System.Collections.Generic;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Splines;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de pruebas automatizadas en Editor para el Sistema de Eventos y Tráfico Guionado por Triggers
    /// (US31 / Issue #23).
    /// Valida exhaustivamente:
    /// 1. Activación de TriggerScriptedEvent con vehículo y descarte de otros objetos no-vehículo.
    /// 2. Activación de movimiento en NPCPathFollower al disparar el trigger.
    /// 3. Comportamiento de m_TriggerOnce y retardo m_Delay determinista.
    /// 4. NPCSafetyStop: Detección frontal de riesgo inminente, detención de emergencia y reanudación con retardo de seguridad.
    /// 5. Integración y sincronización con semáforos (TrafficLightController).
    /// </summary>
    public static class ScriptedEventTests
    {
        [MenuItem("Tools/Primer Volante/Run Scripted Event Tests")]
        public static void RunAllTests()
        {
            Debug.Log("=========================================================");
            Debug.Log("🚦 INICIANDO PRUEBAS DE EVENTOS Y TRÁFICO GUIONADO (US31)");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            TestTriggerActivationAndFiltering(ref totalTests, ref passedTests);
            TestTargetNPCActivation(ref totalTests, ref passedTests);
            TestTriggerOnceAndDelay(ref totalTests, ref passedTests);
            TestNPCSafetyStopDetectionAndBraking(ref totalTests, ref passedTests);
            TestNPCSafetyStopDynamicDistanceAndFiltering(ref totalTests, ref passedTests);
            TestTrafficLightIntegration(ref totalTests, ref passedTests);
            TestSceneSetupIntegrity(ref totalTests, ref passedTests);

            Debug.Log("=========================================================");
            if (passedTests == totalTests)
            {
                Debug.Log($"<color=green>✅ PRUEBAS DE EVENTOS GUIONADOS COMPLETADAS CON ÉXITO: {passedTests}/{totalTests} pruebas pasadas.</color>");
            }
            else
            {
                Debug.LogError($"<color=red>❌ PRUEBAS DE EVENTOS GUIONADOS FALLIDAS: {passedTests}/{totalTests} pruebas pasadas.</color>");
            }
            Debug.Log("=========================================================");
        }

        public static string RunAllTestsSummary()
        {
            int totalTests = 0;
            int passedTests = 0;

            TestTriggerActivationAndFiltering(ref totalTests, ref passedTests);
            TestTargetNPCActivation(ref totalTests, ref passedTests);
            TestTriggerOnceAndDelay(ref totalTests, ref passedTests);
            TestNPCSafetyStopDetectionAndBraking(ref totalTests, ref passedTests);
            TestNPCSafetyStopDynamicDistanceAndFiltering(ref totalTests, ref passedTests);
            TestTrafficLightIntegration(ref totalTests, ref passedTests);
            TestSceneSetupIntegrity(ref totalTests, ref passedTests);

            return $"{passedTests}/{totalTests} passed (Failures: {totalTests - passedTests})";
        }

        #region 1. Activación de Trigger y Filtros de Detección

        private static void TestTriggerActivationAndFiltering(ref int total, ref int passed)
        {
            GameObject triggerObj = new GameObject("TestTrigger_Activation");
            GameObject vehicleObj = new GameObject("TestVehicle_Player");
            GameObject nonVehicleObj = new GameObject("TestNonVehicle_Prop");

            try
            {
                BoxCollider triggerCol = triggerObj.AddComponent<BoxCollider>();
                triggerCol.isTrigger = true;
                TriggerScriptedEvent trigger = triggerObj.AddComponent<TriggerScriptedEvent>();

                // 1.1 Configurar vehículo con VehicleController y Collider
                vehicleObj.AddComponent<Rigidbody>();
                vehicleObj.AddComponent<VehicleController>();
                BoxCollider vehicleCol = vehicleObj.AddComponent<BoxCollider>();

                // 1.2 Configurar objeto no vehículo sin VehicleController
                BoxCollider propCol = nonVehicleObj.AddComponent<BoxCollider>();

                // Validar que trigger se auto-configura como isTrigger
                Assert(triggerCol.isTrigger, "TriggerScriptedEvent asegura isTrigger = true en collider", ref total, ref passed);

                // Validar eventos
                bool csharpEventFired = false;
                Collider capturedCollider = null;
                trigger.OnTriggerActivated += col =>
                {
                    csharpEventFired = true;
                    capturedCollider = col;
                };

                bool unityEventFired = false;
                trigger.OnTriggerActivatedEvent.AddListener(() => unityEventFired = true);

                // 1.3 Intento con objeto no-vehículo -> debe ser rechazado
                bool nonVehicleResult = trigger.TryTrigger(propCol);
                Assert(!nonVehicleResult, "Objeto sin VehicleController ni Tag es rechazado por el trigger", ref total, ref passed);
                Assert(!trigger.HasTriggered, "Trigger permanece inactivo tras rechazar no-vehículo", ref total, ref passed);
                Assert(!csharpEventFired, "Evento C# no se dispara para objetos no permitidos", ref total, ref passed);
                Assert(!unityEventFired, "UnityEvent no se dispara para objetos no permitidos", ref total, ref passed);

                // 1.4 Intento con vehículo -> debe ser aceptado
                bool vehicleResult = trigger.TryTrigger(vehicleCol);
                Assert(vehicleResult, "Vehículo con VehicleController activa exitosamente el trigger", ref total, ref passed);
                Assert(trigger.HasTriggered, "HasTriggered es true tras la activación", ref total, ref passed);
                Assert(csharpEventFired && capturedCollider == vehicleCol, "Evento C# OnTriggerActivated pasa el collider del vehículo", ref total, ref passed);
                Assert(unityEventFired, "onTriggerActivatedEvent de Unity se dispara correctamente", ref total, ref passed);

                // 1.5 Filtro por Tag
                trigger.ResetTrigger();
                trigger.CheckVehicleController = false;
                trigger.RequiredTag = "Player";

                // NonVehicleObj no tiene tag "Player"
                Assert(!trigger.TryTrigger(propCol), "Objeto sin tag 'Player' es rechazado cuando RequiredTag = 'Player'", ref total, ref passed);

                // Si se le asigna el tag Player (o mediante hijo con VehicleController)
                trigger.CheckVehicleController = true;
                trigger.RequiredTag = "";
                Assert(trigger.TryTrigger(vehicleCol), "Reactivación tras ResetTrigger funciona correctamente", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(triggerObj);
                Object.DestroyImmediate(vehicleObj);
                Object.DestroyImmediate(nonVehicleObj);
            }
        }

        #endregion

        #region 2. Activación de Movimiento en NPCPathFollower

        private static void TestTargetNPCActivation(ref int total, ref int passed)
        {
            GameObject triggerObj = new GameObject("TestTrigger_NPC");
            GameObject vehicleObj = new GameObject("TestVehicle");
            GameObject splineObj = new GameObject("TestSplineContainer");
            GameObject npc1Obj = new GameObject("TestNPC_Pedestrian");
            GameObject npc2Obj = new GameObject("TestNPC_Vehicle");

            try
            {
                // Configurar ruta Spline dummy de 50 metros
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 50f)));
                splineContainer.Spline = spline;

                // Configurar NPCs con ruta pero sin auto-play
                NPCPathFollower npc1 = npc1Obj.AddComponent<NPCPathFollower>();
                npc1.SetPath(splineContainer, 0);
                Assert(!npc1.IsPlaying, "NPC1 arranca detenido (IsPlaying == false)", ref total, ref passed);

                NPCPathFollower npc2 = npc2Obj.AddComponent<NPCPathFollower>();
                npc2.SetPath(splineContainer, 0);
                Assert(!npc2.IsPlaying, "NPC2 arranca detenido (IsPlaying == false)", ref total, ref passed);

                // Configurar Trigger con ambos NPCs
                triggerObj.AddComponent<BoxCollider>().isTrigger = true;
                TriggerScriptedEvent trigger = triggerObj.AddComponent<TriggerScriptedEvent>();
                trigger.TargetNPCs.Add(npc1);
                trigger.TargetNPCs.Add(npc2);

                vehicleObj.AddComponent<VehicleController>();
                BoxCollider vehicleCol = vehicleObj.AddComponent<BoxCollider>();

                // Disparar trigger
                bool success = trigger.TryTrigger(vehicleCol);
                Assert(success, "Trigger dispara con éxito hacia objetivos", ref total, ref passed);

                // Validar que ambos NPCs iniciaron marcha (Play)
                Assert(npc1.IsPlaying, "NPC1 recibió Play() y ahora IsPlaying == true", ref total, ref passed);
                Assert(!npc1.IsStopRequested, "NPC1 no tiene parada solicitada", ref total, ref passed);
                Assert(npc2.IsPlaying, "NPC2 recibió Play() y ahora IsPlaying == true", ref total, ref passed);
                Assert(!npc2.IsStopRequested, "NPC2 no tiene parada solicitada", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(triggerObj);
                Object.DestroyImmediate(vehicleObj);
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npc1Obj);
                Object.DestroyImmediate(npc2Obj);
            }
        }

        #endregion

        #region 3. Comportamiento de m_TriggerOnce y Retardo m_Delay

        private static void TestTriggerOnceAndDelay(ref int total, ref int passed)
        {
            GameObject triggerObj = new GameObject("TestTrigger_OnceDelay");
            GameObject vehicleObj = new GameObject("TestVehicle");
            GameObject splineObj = new GameObject("TestSpline");
            GameObject npcObj = new GameObject("TestNPC");

            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 40f)));
                splineContainer.Spline = spline;

                NPCPathFollower npc = npcObj.AddComponent<NPCPathFollower>();
                npc.SetPath(splineContainer, 0);

                triggerObj.AddComponent<BoxCollider>().isTrigger = true;
                TriggerScriptedEvent trigger = triggerObj.AddComponent<TriggerScriptedEvent>();
                trigger.TargetNPCs.Add(npc);
                trigger.TriggerOnce = true;
                trigger.Delay = 1.0f; // 1 segundo de retardo

                vehicleObj.AddComponent<VehicleController>();
                BoxCollider vehicleCol = vehicleObj.AddComponent<BoxCollider>();

                // 3.1 Primer disparo con retardo
                bool firstTrigger = trigger.TryTrigger(vehicleCol);
                Assert(firstTrigger, "Primer disparo con retardo es aceptado", ref total, ref passed);
                Assert(trigger.HasTriggered, "HasTriggered es true", ref total, ref passed);
                Assert(trigger.IsDelayPending, "IsDelayPending es true durante el retardo", ref total, ref passed);
                Assert(Mathf.Approximately(trigger.RemainingDelay, 1.0f), "RemainingDelay arranca en 1.0s", ref total, ref passed);
                Assert(!npc.IsPlaying, "NPC permanece detenido mientras el retardo está activo", ref total, ref passed);

                // 3.2 Segundo disparo ignorado por TriggerOnce
                bool secondTrigger = trigger.TryTrigger(vehicleCol);
                Assert(!secondTrigger, "Segundo disparo es rechazado debido a TriggerOnce = true", ref total, ref passed);

                // 3.3 Avance parcial de tiempo (0.6s)
                trigger.Tick(0.6f);
                Assert(trigger.IsDelayPending, "Retardo sigue pendiente a los 0.6s", ref total, ref passed);
                Assert(Mathf.Approximately(trigger.RemainingDelay, 0.4f), "Tiempo restante es 0.4s", ref total, ref passed);
                Assert(!npc.IsPlaying, "NPC sigue detenido a los 0.6s", ref total, ref passed);

                // 3.4 Completar retardo (0.4s restantes)
                trigger.Tick(0.4f);
                Assert(!trigger.IsDelayPending, "IsDelayPending pasa a false al cumplirse el retardo", ref total, ref passed);
                Assert(npc.IsPlaying, "NPC arranca inmediatamente al cumplirse el retardo", ref total, ref passed);

                // 3.5 Modo TriggerOnce = false
                trigger.ResetTrigger();
                trigger.TriggerOnce = false;
                trigger.Delay = 0f;
                Assert(trigger.TryTrigger(vehicleCol), "Disparo 1 con TriggerOnce=false funciona", ref total, ref passed);
                trigger.ResetTrigger();
                Assert(trigger.TryTrigger(vehicleCol), "Disparo 2 con TriggerOnce=false funciona", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(triggerObj);
                Object.DestroyImmediate(vehicleObj);
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        #endregion

        #region 4. NPCSafetyStop: Detección Frontal y Frenado de Emergencia

        private static void TestNPCSafetyStopDetectionAndBraking(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline");
            GameObject npcObj = new GameObject("TestNPC_Safety");

            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 60f)));
                splineContainer.Spline = spline;

                NPCPathFollower npc = npcObj.AddComponent<NPCPathFollower>();
                npc.SetPath(splineContainer, 0);

                NPCSafetyStop safety = npcObj.AddComponent<NPCSafetyStop>();
                safety.ResumeDelay = 0.5f;
                safety.DetectionDistance = 4.0f;

                // Eventos
                bool emergencyBrakeFired = false;
                safety.OnEmergencyBrake += () => emergencyBrakeFired = true;

                bool resumeFired = false;
                safety.OnResumeMovement += () => resumeFired = true;

                // 4.1 NPC comienza su marcha
                npc.Play();
                Assert(npc.IsPlaying && !npc.IsStopRequested, "NPC está en marcha activa", ref total, ref passed);
                Assert(!safety.IsEmergencyBraking, "NPCSafetyStop arranca sin frenado", ref total, ref passed);

                // 4.2 Detección de obstáculo simulado a 3 metros
                safety.SetSimulatedObstacle(true, 3.0f);
                safety.Tick(0.02f);

                Assert(safety.IsEmergencyBraking, "Frenado de emergencia activado (IsEmergencyBraking == true)", ref total, ref passed);
                Assert(npc.IsStopRequested, "NPCPathFollower tiene Stop solicitado", ref total, ref passed);
                Assert(emergencyBrakeFired, "Evento OnEmergencyBrake disparado", ref total, ref passed);
                Assert(Mathf.Approximately(safety.LastObstacleHit.distance, 3.0f), "Distancia registrada del obstáculo es 3.0m", ref total, ref passed);

                // 4.3 Obstáculo sigue presente -> no reanuda
                safety.Tick(1.0f);
                Assert(safety.IsEmergencyBraking, "Sigue en frenado de emergencia mientras obstáculo permanece", ref total, ref passed);
                Assert(!resumeFired, "No se dispara reanudación mientras el obstáculo está presente", ref total, ref passed);

                // 4.4 Vía despejada: comienza cuenta regresiva de ResumeDelay (0.5s)
                safety.SetSimulatedObstacle(false);
                safety.Tick(0.2f);
                Assert(safety.IsEmergencyBraking, "Continúa en frenado durante el tiempo de seguridad (0.2s < 0.5s)", ref total, ref passed);
                Assert(Mathf.Approximately(safety.ClearTimer, 0.2f), "ClearTimer acumula 0.2s", ref total, ref passed);

                // 4.5 Transcurre el tiempo restante (0.35s más -> total 0.55s >= 0.5s)
                safety.Tick(0.35f);
                Assert(!safety.IsEmergencyBraking, "Frenado de emergencia liberado tras despejarse la vía", ref total, ref passed);
                Assert(npc.IsPlaying, "NPC reanudó la marcha automáticamente llamando a Play()", ref total, ref passed);
                Assert(!npc.IsStopRequested, "Stop solicitado fue limpiado", ref total, ref passed);
                Assert(resumeFired, "Evento OnResumeMovement disparado al reanudar", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
            }
        }

        #endregion

        #region 5. NPCSafetyStop: Distancia Dinámica y Filtros de Obstáculo

        private static void TestNPCSafetyStopDynamicDistanceAndFiltering(ref int total, ref int passed)
        {
            GameObject splineObj = new GameObject("TestSpline");
            GameObject npcObj = new GameObject("TestNPC_Dynamic");
            GameObject selfChildObj = new GameObject("NPC_ChildPart");
            GameObject externalObstacleObj = new GameObject("ExternalObstacle");

            try
            {
                SplineContainer splineContainer = splineObj.AddComponent<SplineContainer>();
                Spline spline = new Spline();
                spline.Add(new BezierKnot(new float3(0f, 0f, 0f)));
                spline.Add(new BezierKnot(new float3(0f, 0f, 50f)));
                splineContainer.Spline = spline;

                NPCPathFollower npc = npcObj.AddComponent<NPCPathFollower>();
                npc.SetPath(splineContainer, 0);

                NPCSafetyStop safety = npcObj.AddComponent<NPCSafetyStop>();
                safety.DetectionDistance = 5.0f;
                safety.DynamicDistanceWithSpeed = true;
                safety.SpeedLookaheadFactor = 1.0f;

                // 5.1 Distancia efectiva con velocidad cero
                float distBase = safety.GetEffectiveDetectionDistance();
                Assert(Mathf.Approximately(distBase, 5.0f), "Distancia efectiva base es 5.0m con v=0", ref total, ref passed);

                // 5.2 Filtro de colisiones propias: colliders del propio NPC o sus hijos
                selfChildObj.transform.SetParent(npcObj.transform, false);
                BoxCollider selfChildCol = selfChildObj.AddComponent<BoxCollider>();

                Assert(!safety.IsValidObstacleCollider(selfChildCol), "Collider hijo del propio NPC es ignorado por IsValidObstacleCollider", ref total, ref passed);

                // 5.3 Collider de un obstáculo externo
                BoxCollider extCol = externalObstacleObj.AddComponent<BoxCollider>();
                Assert(safety.IsValidObstacleCollider(extCol), "Collider externo ajeno es aceptado como obstáculo válido", ref total, ref passed);

                // 5.4 Filtro por ObstacleTag
                safety.ObstacleTag = "Player";
                Assert(!safety.IsValidObstacleCollider(extCol), "Obstáculo externo sin tag 'Player' es rechazado cuando ObstacleTag='Player'", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(splineObj);
                Object.DestroyImmediate(npcObj);
                Object.DestroyImmediate(externalObstacleObj);
            }
        }

        #endregion

        #region 6. Integración con Semáforos (TrafficLightController)

        private static void TestTrafficLightIntegration(ref int total, ref int passed)
        {
            GameObject triggerObj = new GameObject("TestTrigger_Light");
            GameObject vehicleObj = new GameObject("TestVehicle");
            GameObject lightObj = new GameObject("TestTrafficLight");

            try
            {
                triggerObj.AddComponent<BoxCollider>().isTrigger = true;
                TriggerScriptedEvent trigger = triggerObj.AddComponent<TriggerScriptedEvent>();

                TrafficLightController light = lightObj.AddComponent<TrafficLightController>();
                light.AutoStart = false;
                light.ResetCycle();
                Assert(!light.IsCycleActive, "Semáforo arranca inactivo (AutoStart = false)", ref total, ref passed);

                // Configurar en el trigger para forzar inicio en Verde
                trigger.TargetTrafficLights.Add(light);
                trigger.TrafficLightInitialState = TrafficLightState.Green;
                trigger.StartTrafficLightCycle = true;

                vehicleObj.AddComponent<VehicleController>();
                BoxCollider vehicleCol = vehicleObj.AddComponent<BoxCollider>();

                // Disparar trigger
                bool success = trigger.TryTrigger(vehicleCol);
                Assert(success, "Trigger dispara hacia semáforo", ref total, ref passed);
                Assert(light.IsCycleActive, "Semáforo inició su ciclo (IsCycleActive == true)", ref total, ref passed);
                Assert(light.IsGreen && light.CurrentState == TrafficLightState.Green, "Semáforo inició en fase Green como se configuró", ref total, ref passed);

                // Forzar fase Roja con SetState vía trigger
                trigger.ResetTrigger();
                trigger.TrafficLightInitialState = TrafficLightState.Red;
                trigger.StartTrafficLightCycle = false;
                trigger.TryTrigger(vehicleCol);
                Assert(light.IsRed && light.CurrentState == TrafficLightState.Red, "Trigger con SetState forza semáforo a fase Red", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(triggerObj);
                Object.DestroyImmediate(vehicleObj);
            }
        }

        #endregion

        #region 7. Validación de Integridad de Escena y Prefabs

        private static void TestSceneSetupIntegrity(ref int total, ref int passed)
        {
            Assert(System.IO.File.Exists(ScriptedEventSceneSetup.ORIGINAL_SCENE_PATH),
                $"Escena original existe intacta en '{ScriptedEventSceneSetup.ORIGINAL_SCENE_PATH}'", ref total, ref passed);

            Assert(ScriptedEventSceneSetup.TARGET_SCENE_PATH.EndsWith("CityMerged_ScriptedEvents.unity"),
                "Ruta de escena clonada termina en 'CityMerged_ScriptedEvents.unity'", ref total, ref passed);

            GameObject pedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScriptedEventSceneSetup.PEDESTRIAN_PREFAB_PATH);
            Assert(pedPrefab != null && pedPrefab.GetComponent<NPCPathFollower>() != null,
                $"Prefab de peatón existe y tiene NPCPathFollower en '{ScriptedEventSceneSetup.PEDESTRIAN_PREFAB_PATH}'", ref total, ref passed);

            GameObject vehPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScriptedEventSceneSetup.VEHICLE_PREFAB_PATH);
            Assert(vehPrefab != null && vehPrefab.GetComponent<NPCPathFollower>() != null && vehPrefab.GetComponent<NPCVehicleController>() != null,
                $"Prefab de vehículo existe con NPCPathFollower y NPCVehicleController en '{ScriptedEventSceneSetup.VEHICLE_PREFAB_PATH}'", ref total, ref passed);
        }

        #endregion

        #region Helpers

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

        #endregion
    }
}
