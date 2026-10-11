using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de los NPC de TASK-202: paquete Splines, prefabs de vehículos y
    /// peatones, clips de Mixamo, matemática pura del seguimiento de ruta, giro del auto del jugador,
    /// luces de Car07/Car08, escena de demo y suites existentes.
    /// </summary>
    public static class NPCVerification
    {
        [MenuItem("Tools/Primer Volante/Run NPC Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE NPC (TASK-202)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyPackage(ref total, ref passed);
            VerifyVehiclePrefabs(ref total, ref passed);
            VerifyPedestrianPrefabs(ref total, ref passed);
            VerifyMixamoClips(ref total, ref passed);
            VerifyPathMath(ref total, ref passed);
            VerifyVehicleVisualMath(ref total, ref passed);
            VerifyPlayerSteering(ref total, ref passed);
            VerifyPlayerCarLights(ref total, ref passed);
            VerifyDemoScene(ref total, ref passed);
            VerifyExistingSuites(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Paquete

        private static void VerifyPackage(ref int total, ref int passed)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SplineContainer).Assembly);
            Assert(info != null && info.name == "com.unity.splines", "Paquete com.unity.splines debe estar instalado", ref total, ref passed);
            Assert(info != null && info.version == "2.9.1", $"com.unity.splines debe estar en 2.9.1 (actual: {info?.version})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Vehículos

        private static void VerifyVehiclePrefabs(ref int total, ref int passed)
        {
            string[] sources = NPCPrefabSetup.FindVehicleSourcePaths();
            Assert(sources.Length == 21, $"Deben existir 21 vehículos _separate originales (encontrados {sources.Length})", ref total, ref passed);

            foreach (string sourcePath in sources)
            {
                string outputPath = NPCPrefabSetup.VehicleOutputPath(sourcePath);
                string label = System.IO.Path.GetFileNameWithoutExtension(outputPath);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
                Assert(prefab != null, $"{label}: prefab debe existir", ref total, ref passed);
                if (prefab == null) continue;

                var source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                Assert(PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant &&
                       source != null && AssetDatabase.GetAssetPath(source) == sourcePath,
                       $"{label}: debe ser variante de {System.IO.Path.GetFileName(sourcePath)}", ref total, ref passed);

                Assert(Mathf.Approximately(prefab.transform.localScale.x, NPCPrefabSetup.VEHICLE_SCALE) &&
                       Mathf.Approximately(prefab.transform.localScale.y, NPCPrefabSetup.VEHICLE_SCALE) &&
                       Mathf.Approximately(prefab.transform.localScale.z, NPCPrefabSetup.VEHICLE_SCALE),
                       $"{label}: escala debe ser 0.69", ref total, ref passed);

                var rb = prefab.GetComponent<Rigidbody>();
                Assert(rb != null && rb.isKinematic && rb.interpolation == RigidbodyInterpolation.Interpolate && !rb.useGravity,
                       $"{label}: Rigidbody cinemático, interpolado y sin gravedad", ref total, ref passed);

                Assert(prefab.GetComponent<BoxCollider>() != null, $"{label}: debe tener BoxCollider", ref total, ref passed);
                Assert(prefab.GetComponent<NPCPathFollower>() != null, $"{label}: debe tener NPCPathFollower", ref total, ref passed);
                Assert(prefab.GetComponent<VehicleController>() == null, $"{label}: no debe tener VehicleController", ref total, ref passed);

                var controller = prefab.GetComponent<NPCVehicleController>();
                int wheelsInHierarchy = prefab.GetComponentsInChildren<Transform>(true)
                    .Count(t => t != prefab.transform && t.name.StartsWith("Wheel_"));
                int assigned = controller == null ? 0 : controller.FrontWheels.Count(w => w != null) + controller.RearWheels.Count(w => w != null);
                Assert(controller != null && controller.FrontWheels.Count == 2 && wheelsInHierarchy >= 4 && assigned == wheelsInHierarchy,
                       $"{label}: NPCVehicleController con todas las ruedas asignadas ({assigned}/{wheelsInHierarchy})", ref total, ref passed);
            }
        }

        // ---------------------------------------------------------------- Peatones

        private static void VerifyPedestrianPrefabs(ref int total, ref int passed)
        {
            var controllerAsset = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(NPCPrefabSetup.CONTROLLER_PATH);
            Assert(controllerAsset != null, "NPC_Pedestrian.controller debe existir", ref total, ref passed);

            var looks = new HashSet<string>();
            for (int i = 1; i <= NPCPrefabSetup.PEDESTRIAN_COUNT; i++)
            {
                string path = $"{NPCPrefabSetup.PEDESTRIAN_OUTPUT_FOLDER}/NPC_Pedestrian_{i:00}.prefab";
                string label = $"NPC_Pedestrian_{i:00}";

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert(prefab != null, $"{label}: prefab debe existir", ref total, ref passed);
                if (prefab == null) continue;

                var source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                Assert(PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant &&
                       source != null && AssetDatabase.GetAssetPath(source) == NPCPrefabSetup.URBAN_MAN_PREFAB_PATH,
                       $"{label}: debe ser variante de UrbanManCustomizable", ref total, ref passed);

                var animator = prefab.GetComponent<Animator>();
                Assert(animator != null && animator.runtimeAnimatorController == controllerAsset,
                       $"{label}: Animator con NPC_Pedestrian.controller", ref total, ref passed);
                Assert(animator != null && !animator.applyRootMotion, $"{label}: applyRootMotion debe ser false", ref total, ref passed);
                Assert(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman,
                       $"{label}: avatar humanoide válido", ref total, ref passed);

                var rb = prefab.GetComponent<Rigidbody>();
                Assert(rb != null && rb.isKinematic && rb.interpolation == RigidbodyInterpolation.Interpolate && !rb.useGravity,
                       $"{label}: Rigidbody cinemático, interpolado y sin gravedad", ref total, ref passed);

                var box = prefab.GetComponent<BoxCollider>();
                Assert(box != null && box.enabled && box.size.y > 1.5f, $"{label}: BoxCollider raíz que cubre el cuerpo (altura > 1.5 m)", ref total, ref passed);
                int enabledColliders = prefab.GetComponentsInChildren<Collider>(true).Count(c => c.enabled);
                Assert(enabledColliders == 1, $"{label}: un único collider activo (encontrados {enabledColliders})", ref total, ref passed);

                Assert(prefab.GetComponent<NPCPathFollower>() != null, $"{label}: debe tener NPCPathFollower", ref total, ref passed);
                var pedestrian = prefab.GetComponent<NPCPedestrianController>();
                Assert(pedestrian != null && pedestrian.WalkReferenceSpeed > 0f,
                       $"{label}: NPCPedestrianController con velocidad de referencia de Walking > 0", ref total, ref passed);

                var customizer = prefab.GetComponent<PrimerVolante.Characters.UrbanManCustomizer>();
                if (customizer != null)
                {
                    var so = new SerializedObject(customizer);
                    SerializedProperty accessories = so.FindProperty("m_InitialAccessoryIds");
                    var ids = new List<string>();
                    for (int a = 0; a < accessories.arraySize; a++) ids.Add(accessories.GetArrayElementAtIndex(a).stringValue);
                    looks.Add(so.FindProperty("m_InitialTorsoId").stringValue + "|" + string.Join(",", ids) + "|" +
                              so.FindProperty("m_InitialSkinToneId").stringValue);
                }
            }

            Assert(looks.Count == NPCPrefabSetup.PEDESTRIAN_COUNT, $"Los peatones deben tener {NPCPrefabSetup.PEDESTRIAN_COUNT} apariencias distintas (encontradas {looks.Count})", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Mixamo

        private static void VerifyMixamoClips(ref int total, ref int passed)
        {
            foreach (string path in new[] { NPCPrefabSetup.IDLE_FBX_PATH, NPCPrefabSetup.WALKING_FBX_PATH })
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert(importer != null, $"{label}: debe existir y ser un modelo", ref total, ref passed);
                if (importer == null) continue;

                Assert(importer.animationType == ModelImporterAnimationType.Human, $"{label}: animationType debe ser Human", ref total, ref passed);
                Assert(NPCPrefabSetup.IsHumanWithValidAvatar(path), $"{label}: avatar humanoide válido", ref total, ref passed);

                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                Assert(clips != null && clips.Length > 0, $"{label}: debe tener clips configurados", ref total, ref passed);
                if (clips == null || clips.Length == 0) continue;

                Assert(clips.All(c => c.loopTime), $"{label}: loopTime activo", ref total, ref passed);
                Assert(clips.All(c => c.lockRootRotation), $"{label}: bake into pose de rotación", ref total, ref passed);
                Assert(clips.All(c => c.lockRootHeightY), $"{label}: bake into pose de altura", ref total, ref passed);
                Assert(clips.All(c => c.lockRootPositionXZ), $"{label}: bake into pose de posición XZ", ref total, ref passed);
            }

            AnimationClip walking = NPCPrefabSetup.LoadClip(NPCPrefabSetup.WALKING_FBX_PATH);
            Assert(walking != null && walking.length > 0.9f && walking.length < 1.2f,
                $"Clip Walking debe durar ~1.03 s (actual: {(walking != null ? walking.length : 0f):F2})", ref total, ref passed);
            AnimationClip idle = NPCPrefabSetup.LoadClip(NPCPrefabSetup.IDLE_FBX_PATH);
            Assert(idle != null && idle.length > 8f && idle.length < 8.7f,
                $"Clip Idle debe durar ~8.33 s (actual: {(idle != null ? idle.length : 0f):F2})", ref total, ref passed);

            float reference = NPCPrefabSetup.ComputeUrbanManWalkReferenceSpeed();
            Assert(reference > 1f && reference < 3f, $"Velocidad de referencia de Walking del UrbanMan en rango (actual: {reference:F2} m/s)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Matemática pura del follower

        private struct RunResult
        {
            public float Distance;
            public float Speed;
            public int Steps;
            public bool Finished;
            public float MaxStepAcceleration;
            public float MaxStepDeceleration;
            public float TravelledTotal;
        }

        private static RunResult Simulate(float cruise, float accel, float decel, float length, NPCPathEndBehavior behavior, int maxSteps, float dt)
        {
            var state = new NPCPathState();
            var result = new RunResult();
            float previousDistance = 0f;
            int steps = 0;
            for (; steps < maxSteps && !state.Finished; steps++)
            {
                float target = cruise;
                if (behavior == NPCPathEndBehavior.Stop)
                {
                    target = Mathf.Min(target, NPCPathMath.StopSpeedLimit(length - state.Distance, decel));
                }

                float before = state.Speed;
                state = NPCPathMath.Step(state, target, accel, decel, length, behavior, dt);
                float dv = (state.Speed - before) / dt;
                result.MaxStepAcceleration = Mathf.Max(result.MaxStepAcceleration, dv);
                result.MaxStepDeceleration = Mathf.Max(result.MaxStepDeceleration, -dv);

                float travelled = state.Distance - previousDistance;
                if (behavior == NPCPathEndBehavior.Loop && travelled < 0f) travelled += length;
                result.TravelledTotal += travelled;
                previousDistance = state.Distance;
            }

            result.Distance = state.Distance;
            result.Speed = state.Speed;
            result.Steps = steps;
            result.Finished = state.Finished;
            return result;
        }

        private static void VerifyPathMath(ref int total, ref int passed)
        {
            const float dt = 0.02f;

            // Determinismo: dos corridas idénticas dan exactamente el mismo resultado.
            RunResult a = Simulate(8f, 2.5f, 4f, 120f, NPCPathEndBehavior.Loop, 3000, dt);
            RunResult b = Simulate(8f, 2.5f, 4f, 120f, NPCPathEndBehavior.Loop, 3000, dt);
            Assert(a.Distance == b.Distance && a.Speed == b.Speed && a.TravelledTotal == b.TravelledTotal,
                "Misma secuencia de pasos fijos produce exactamente el mismo resultado", ref total, ref passed);

            // Límites de aceleración y frenado.
            RunResult stop = Simulate(10f, 2f, 3f, 150f, NPCPathEndBehavior.Stop, 20000, dt);
            Assert(a.MaxStepAcceleration <= 2.5f + 1e-3f && stop.MaxStepAcceleration <= 2f + 1e-3f,
                $"La aceleración por paso respeta el límite (máx {Mathf.Max(a.MaxStepAcceleration, stop.MaxStepAcceleration):F3} m/s²)", ref total, ref passed);
            Assert(a.MaxStepDeceleration <= 4f + 1e-3f && stop.MaxStepDeceleration <= 3f + 1e-3f,
                $"El frenado por paso respeta el límite (máx {Mathf.Max(a.MaxStepDeceleration, stop.MaxStepDeceleration):F3} m/s²)", ref total, ref passed);

            // Stop: termina exactamente en el final, sin pasarse y detenido.
            Assert(stop.Finished && Mathf.Approximately(stop.Distance, 150f) && stop.Speed == 0f,
                $"En Stop se detiene en el final de la ruta (dist {stop.Distance:F3} de 150, vel {stop.Speed:F3})", ref total, ref passed);

            // Loop: la distancia da la vuelta y nunca llega a Finished.
            Assert(!a.Finished && a.Distance >= 0f && a.Distance < 120f && a.TravelledTotal > 120f,
                $"En Loop la distancia da la vuelta (recorrido total {a.TravelledTotal:F1} m en una ruta de 120 m)", ref total, ref passed);

            // Curvas: la velocidad objetivo queda por debajo de sqrt(aLat · R).
            bool allBelow = true;
            foreach (float radius in new[] { 3f, 6f, 12f, 30f })
            {
                foreach (float aLat in new[] { 1.5f, 3f })
                {
                    float limit = NPCPathMath.CurveSpeedLimit(aLat, radius);
                    float atCurve = NPCPathMath.ApproachSpeedLimit(limit, 0f, 4f);
                    if (atCurve > Mathf.Sqrt(aLat * radius) + 1e-4f) allBelow = false;
                }
            }

            Assert(allBelow, "La velocidad en curva queda por debajo de sqrt(aLat · R)", ref total, ref passed);
            Assert(float.IsPositiveInfinity(NPCPathMath.CurveSpeedLimit(3f, float.PositiveInfinity)),
                "Una recta (radio infinito) no limita la velocidad", ref total, ref passed);
        }

        private static void VerifyVehicleVisualMath(ref int total, ref int passed)
        {
            float straight = NPCVehicleController.ComputeSteerAngle(2.44f, 0f, 8f, 35f);
            float turning = NPCVehicleController.ComputeSteerAngle(2.44f, 1f, 4f, 35f);
            float limited = NPCVehicleController.ComputeSteerAngle(2.44f, 10f, 1f, 35f);
            Assert(Mathf.Abs(straight) < 1e-4f, "Sin giro (ω = 0) las ruedas quedan rectas", ref total, ref passed);
            Assert(turning > 0f && Mathf.Approximately(turning, Mathf.Atan(2.44f * 1f / 4f) * Mathf.Rad2Deg),
                "Ángulo de dirección = atan(L·ω/v)", ref total, ref passed);
            Assert(Mathf.Approximately(limited, 35f), "Ángulo de dirección limitado a ±35°", ref total, ref passed);
            Assert(NPCVehicleController.ComputeSteerAngle(2.44f, 1f, 0f, 35f) == 0f, "Con v = 0 el ángulo inverso no está definido (se conserva el último)", ref total, ref passed);

            // Por debajo de la referencia el blend tree ya acorta el paso: el Animator va a 1 (sin doble compensación).
            float idleSpeed = NPCPedestrianController.ComputeAnimatorSpeed(0f, 1.89f, 2f);
            float slowSpeed = NPCPedestrianController.ComputeAnimatorSpeed(1.4f, 1.89f, 2f);
            float walkSpeed = NPCPedestrianController.ComputeAnimatorSpeed(1.89f, 1.89f, 2f);
            float brisk = NPCPedestrianController.ComputeAnimatorSpeed(2.84f, 1.89f, 2f);
            float fastSpeed = NPCPedestrianController.ComputeAnimatorSpeed(5f, 1.89f, 2f);
            Assert(Mathf.Approximately(idleSpeed, 1f) && Mathf.Approximately(slowSpeed, 1f) && Mathf.Approximately(walkSpeed, 1f),
                "Por debajo de la referencia el Animator va a velocidad 1 (el blend tree ya acorta el paso)", ref total, ref passed);
            Assert(Mathf.Abs(brisk - 2.84f / 1.89f) < 1e-4f && Mathf.Approximately(fastSpeed, 2f),
                "Por encima de la referencia el Animator sigue a v / referencia y queda acotado", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Auto del jugador

        private static void VerifyPlayerSteering(ref int total, ref int passed)
        {
            // Valores serializados en Car08 (los defaults del script: 2.86 m, 30°, 6 m/s²).
            float wheelBase = 2.86f, steerAngle = 30f, maxLateral = 6f;
            var car08 = AssetDatabase.LoadAssetAtPath<GameObject>(SoundIntegrationSetup.PREFAB_CAR08_PATH);
            var car08Vehicle = car08 != null ? car08.GetComponent<VehicleController>() : null;
            if (car08Vehicle != null)
            {
                var so = new SerializedObject(car08Vehicle);
                wheelBase = so.FindProperty("m_WheelBase").floatValue;
                steerAngle = so.FindProperty("m_MaxSteerAngle").floatValue;
                maxLateral = so.FindProperty("m_MaxLateralAcceleration").floatValue;
            }

            Assert(Mathf.Approximately(wheelBase, 2.86f) && Mathf.Approximately(steerAngle, 30f) && maxLateral > 0f,
                $"Car08: batalla 2.86 m, ángulo máximo 30° y aceleración lateral > 0 (actual {wheelBase} m, {steerAngle}°, {maxLateral} m/s²)", ref total, ref passed);

            float lowSpeed = 2f;
            float omega = VehicleController.ComputeYawRate(lowSpeed, 1f, steerAngle, wheelBase, maxLateral);
            float minRadius = omega > 0f ? lowSpeed / omega : float.PositiveInfinity;
            Assert(minRadius >= 4.5f && minRadius <= 6f, $"Radio de giro mínimo a baja velocidad entre 4.5 y 6 m (actual {minRadius:F2} m)", ref total, ref passed);

            float highSpeed = 60f / 3.6f;
            float omegaHigh = VehicleController.ComputeYawRate(highSpeed, 1f, steerAngle, wheelBase, maxLateral);
            float lateral = omegaHigh * highSpeed;
            Assert(lateral <= maxLateral + 1e-3f, $"A 60 km/h la aceleración lateral no supera el tope (actual {lateral:F2} m/s²)", ref total, ref passed);

            Assert(VehicleController.ComputeYawRate(0f, 1f, steerAngle, wheelBase, maxLateral) == 0f, "Detenido no gira", ref total, ref passed);
            Assert(VehicleController.ComputeYawRate(5f, -1f, steerAngle, wheelBase, maxLateral) < 0f, "Volante a la izquierda gira a la izquierda", ref total, ref passed);

            // El radio ya no es constante con la velocidad: a media velocidad es mayor que a baja.
            float midSpeed = 8f;
            float omegaMid = VehicleController.ComputeYawRate(midSpeed, 1f, steerAngle, wheelBase, maxLateral);
            Assert(omegaMid > 0f && midSpeed / omegaMid >= minRadius - 1e-3f, "El radio mínimo no baja al aumentar la velocidad", ref total, ref passed);
        }

        private static void VerifyPlayerCarLights(ref int total, ref int passed)
        {
            foreach (string path in new[] { CockpitIntegrationSetup.PREFAB_CAR07_PATH, SoundIntegrationSetup.PREFAB_CAR08_PATH })
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert(prefab != null, $"{label}: prefab debe existir", ref total, ref passed);
                if (prefab == null) continue;

                var controllers = prefab.GetComponents<VehicleLightingController>();
                Assert(controllers.Length == 1, $"{label}: exactamente 1 VehicleLightingController (encontrados {controllers.Length})", ref total, ref passed);

                int headlights = prefab.transform.Cast<Transform>().Count(t => t.name == "Headlights_Front");
                int taillights = prefab.transform.Cast<Transform>().Count(t => t.name == "Taillights_Rear");
                Assert(headlights == 1, $"{label}: exactamente 1 Headlights_Front (encontrados {headlights})", ref total, ref passed);
                Assert(taillights == 1, $"{label}: exactamente 1 Taillights_Rear (encontrados {taillights})", ref total, ref passed);

                if (controllers.Length != 1) continue;
                VehicleLightingController lighting = controllers[0];

                var vehicle = prefab.GetComponent<VehicleController>();
                Assert(vehicle != null, $"{label}: debe tener VehicleController", ref total, ref passed);
                if (vehicle == null) continue;

                var vehicleSo = new SerializedObject(vehicle);
                Assert(vehicleSo.FindProperty("m_LightingController").objectReferenceValue == lighting,
                    $"{label}: VehicleController.m_LightingController apunta al único controlador", ref total, ref passed);

                var knob = prefab.GetComponentInChildren<VRHeadlightKnob>(true);
                Assert(knob != null && knob.OnModeChanged.GetPersistentEventCount() == 1 &&
                       knob.OnModeChanged.GetPersistentTarget(0) == lighting,
                    $"{label}: VRHeadlightKnob.OnModeChanged conserva su evento hacia el controlador", ref total, ref passed);

                var lightingSo = new SerializedObject(lighting);
                SerializedProperty outer = lightingSo.FindProperty("m_FrontOuterHeadlights");
                var firstOuter = outer.arraySize > 0 ? outer.GetArrayElementAtIndex(0).objectReferenceValue as Component : null;
                Transform frontGroup = prefab.transform.Find("Headlights_Front");
                Assert(firstOuter != null && frontGroup != null && firstOuter.transform.IsChildOf(frontGroup),
                    $"{label}: las ópticas delanteras del controlador están bajo el Headlights_Front conservado", ref total, ref passed);

                SerializedProperty tail = lightingSo.FindProperty("m_RearTailBrakeLights");
                var firstTail = tail.arraySize > 0 ? tail.GetArrayElementAtIndex(0).objectReferenceValue as Component : null;
                Transform rearGroup = prefab.transform.Find("Taillights_Rear");
                Assert(firstTail != null && rearGroup != null && firstTail.transform.IsChildOf(rearGroup),
                    $"{label}: las luces traseras del controlador están bajo el Taillights_Rear conservado", ref total, ref passed);
            }
        }

        // ---------------------------------------------------------------- Escena de demo

        private static void VerifyDemoScene(ref int total, ref int passed)
        {
            Scene scene = SceneManager.GetSceneByPath(NPCDemoRoutesSetup.SCENE_PATH);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!System.IO.File.Exists(NPCDemoRoutesSetup.SCENE_PATH))
                {
                    Assert(false, $"La escena {NPCDemoRoutesSetup.SCENE_PATH} debe existir", ref total, ref passed);
                    return;
                }

                // Aditiva: nunca se reemplaza una escena abierta.
                scene = EditorSceneManager.OpenScene(NPCDemoRoutesSetup.SCENE_PATH, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                Transform group = NPCDemoRoutesSetup.FindGroup(scene);
                Assert(group != null, $"CityWithCar debe tener el grupo {NPCDemoRoutesSetup.GROUP_NAME}", ref total, ref passed);
                if (group == null) return;

                var followers = group.GetComponentsInChildren<NPCPathFollower>(true);
                int vehicles = followers.Count(f => f.GetComponent<NPCVehicleController>() != null);
                int pedestrians = followers.Count(f => f.GetComponent<NPCPedestrianController>() != null);
                Assert(vehicles == 2 && pedestrians == 2, $"NPC_Demo debe tener 2 vehículos y 2 peatones (hay {vehicles} y {pedestrians})", ref total, ref passed);

                foreach (NPCPathFollower follower in followers)
                {
                    follower.RefreshPath();
                    Assert(follower.HasPath && follower.Path != null && follower.PathLength > 5f,
                        $"{follower.name}: debe tener ruta asignada (largo {follower.PathLength:F1} m)", ref total, ref passed);
                }

                foreach (NPCPathFollower follower in followers.Where(f => f.GetComponent<NPCVehicleController>() != null))
                {
                    if (follower.Path == null) continue;
                    List<GameObject> obstacles = NPCDemoRoutesSetup.FindRouteObstacles(follower.Path);
                    Assert(obstacles.Count == 0,
                        $"{follower.name}: la ruta no atraviesa vehículos estacionados (obstáculos: {string.Join(", ", obstacles.Select(o => o.name))})", ref total, ref passed);

                    var spline = follower.Path.Splines[0];
                    Assert(spline.Closed, $"{follower.name}: la ruta de la manzana debe ser una spline cerrada", ref total, ref passed);
                }

                // Peatones: la ruta no atraviesa colliders estáticos (farolas, bancos, árboles, vehículos).
                var pedestrianProbe = new Vector3(0.3f, 0.75f, 0.3f);
                foreach (NPCPathFollower follower in followers.Where(f => f.GetComponent<NPCPedestrianController>() != null))
                {
                    if (follower.Path == null) continue;
                    List<GameObject> obstacles = NPCDemoRoutesSetup.FindRouteObstacles(follower.Path, pedestrianProbe, 1.1f);
                    Assert(obstacles.Count == 0,
                        $"{follower.name}: la ruta peatonal no atraviesa colliders (obstáculos: {string.Join(", ", obstacles.Select(o => o.name))})", ref total, ref passed);
                }
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---------------------------------------------------------------- Suites existentes

        private static void VerifyExistingSuites(ref int total, ref int passed)
        {
            RunSuite("Tools/Primer Volante/Run Cockpit Integration Verification", "CockpitIntegrationVerification", ref total, ref passed);
            // "Indicator_PositionLights debe existir en el cluster" es un fallo preexistente de Car06, ajeno a los NPC.
            RunSuite("Tools/Primer Volante/Run Vehicle Lighting Verification", "VehicleLightingVerification", ref total, ref passed,
                "Indicator_PositionLights debe existir en el cluster", expectedKnownFailures: 1);
            RunSuite("Tools/Primer Volante/Run Sound Integration Verification", "SoundIntegrationVerification", ref total, ref passed,
                "Indicator_PositionLights debe existir en el cluster", expectedKnownFailures: 0);
            RunSuite("Tools/Primer Volante/Run Turn Signal Verification", "TurnSignalVerification", ref total, ref passed);
        }

        private static void RunSuite(string menuPath, string name, ref int total, ref int passed,
            string knownFailure = null, int expectedKnownFailures = 0)
        {
            int fails = 0;
            int suiteTotal = 0, suitePassed = 0;
            bool sawResultLine = false;
            void OnLog(string message, string stack, LogType type)
            {
                if (message.Contains("[FAIL]") && (knownFailure == null || !message.Contains(knownFailure))) fails++;

                Match m = Regex.Match(message, @"RESULTADO FINAL:\s*(\d+)/(\d+)");
                if (m.Success)
                {
                    suitePassed = int.Parse(m.Groups[1].Value);
                    suiteTotal = int.Parse(m.Groups[2].Value);
                    sawResultLine = true;
                }

                // TurnSignalVerification no usa "RESULTADO FINAL": "COMPLETADA CON ÉXITO: 45/45 pruebas pasadas".
                Match turn = Regex.Match(message, @"(\d+)/(\d+) pruebas pasadas");
                if (turn.Success && !sawResultLine)
                {
                    suitePassed = int.Parse(turn.Groups[1].Value);
                    suiteTotal = int.Parse(turn.Groups[2].Value);
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                EditorApplication.ExecuteMenuItem(menuPath);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert(suiteTotal > 0 && fails == 0 && suitePassed == suiteTotal - expectedKnownFailures,
                $"{name} sigue pasando ({suitePassed}/{suiteTotal}, {fails} fallos nuevos, {expectedKnownFailures} preexistente)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Helpers

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
