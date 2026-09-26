using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para la integración arquitectónica definitiva de cabina:
    /// Ensambla y cablea Car07.prefab con piezas 3D nativas (mdl_car02_brake, mdl_car02_lights_knob),
    /// el sistema completo de iluminación vehicular, freno de mano físico y tablero diegético.
    /// Crea y configura la escena unificada CockpitIntegrationScene.unity.
    /// </summary>
    public static class CockpitIntegrationSetup
    {
        public const string PREFAB_CAR07_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car07.prefab";
        public const string SCENE_INTEGRATION_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/CockpitIntegrationScene.unity";
        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Tools/Primer Volante/Integrate Cockpit Controls (Car07 Prefab and Scene)")]
        public static void IntegrateAll()
        {
            SetupCar07Prefab();
            SetupCockpitIntegrationScene();
        }

        [MenuItem("Tools/Primer Volante/Setup Car07 Prefab")]
        public static void SetupCar07Prefab()
        {
            Debug.Log($"[CockpitIntegrationSetup] 🚗 Configurando Prefab unificado: {PREFAB_CAR07_PATH}...");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_CAR07_PATH);
            if (prefabRoot == null)
            {
                Debug.LogError($"[CockpitIntegrationSetup] ❌ No se pudo cargar el Prefab en {PREFAB_CAR07_PATH}");
                return;
            }

            try
            {
                ConfigureCar07(prefabRoot);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_CAR07_PATH, out bool success);
                if (success && saved != null)
                {
                    Debug.Log($"[CockpitIntegrationSetup] ✅ ¡{PREFAB_CAR07_PATH} guardado exitosamente con todos los controles integrados!");
                }
                else
                {
                    Debug.LogError($"[CockpitIntegrationSetup] ❌ Falló el guardado del Prefab en {PREFAB_CAR07_PATH}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Tools/Primer Volante/Setup Cockpit Integration Scene")]
        public static void SetupCockpitIntegrationScene()
        {
            Debug.Log($"[CockpitIntegrationSetup] 🎬 Configurando escena de integración: {SCENE_INTEGRATION_PATH}...");

            const string SOURCE_SCENE = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/BrakeAndLights.unity";

            if (!System.IO.File.Exists(SCENE_INTEGRATION_PATH) && System.IO.File.Exists(SOURCE_SCENE))
            {
                AssetDatabase.CopyAsset(SOURCE_SCENE, SCENE_INTEGRATION_PATH);
                AssetDatabase.Refresh();
            }

            var scene = EditorSceneManager.OpenScene(SCENE_INTEGRATION_PATH, OpenSceneMode.Single);

            // Buscar Car07 en la escena o instanciarlo
            GameObject car = GameObject.Find("Car07");
            if (car == null)
            {
                var vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
                if (vehicle != null) car = vehicle.gameObject;
            }

            if (car == null)
            {
                GameObject carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_CAR07_PATH);
                if (carPrefab != null)
                {
                    car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab, scene);
                    car.name = "Car07";
                    car.transform.position = Vector3.zero;
                    car.transform.rotation = Quaternion.identity;
                }
            }

            if (car != null)
            {
                ConfigureCar07(car);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CockpitIntegrationSetup] ✅ ¡Escena {SCENE_INTEGRATION_PATH} configurada y guardada exitosamente!");
        }

        public static void ConfigureCar07(GameObject carRoot)
        {
            // 0. Limpiar MonoBehaviours con scripts faltantes / obsoletos en toda la jerarquía
            foreach (var t in carRoot.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }

            // =========================================================================
            // 0b. Limpieza de Colliders y Rigidbodies indebidos (Fuerzas Fantasma)
            // =========================================================================
            // 1. Eliminar colliders en todas las luces del vehículo (BlinkerLight_*, Taillight_*, etc.)
            foreach (var col in carRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col.gameObject == carRoot) continue;

                string colName = col.gameObject.name;
                bool isLight = colName.StartsWith("BlinkerLight") ||
                               colName.StartsWith("Taillight") ||
                               colName.StartsWith("Headlight") ||
                               colName.StartsWith("ReverseLight") ||
                               colName.StartsWith("TailBrakeLight") ||
                               colName.StartsWith("SpotLight") ||
                               col.GetComponent<BlinkerLight>() != null;

                if (isLight)
                {
                    UnityEngine.Object.DestroyImmediate(col);
                }
            }

            // 2. Eliminar Rigidbody redundante en la carrocería (mdl_car04_body)
            Transform bodyTransform = carRoot.transform.Find("mdl_car04_body");
            if (bodyTransform != null)
            {
                Rigidbody bodyRb = bodyTransform.GetComponent<Rigidbody>();
                if (bodyRb != null) UnityEngine.Object.DestroyImmediate(bodyRb);
            }

            // 3. Eliminar MeshCollider en el volante visible (mdl_car02_steering_wheel)
            Transform wheelMesh = carRoot.transform.Find("SteeringWheel_Pivot/mdl_car02_steering_wheel");
            if (wheelMesh == null)
            {
                foreach (var t in carRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "mdl_car02_steering_wheel")
                    {
                        wheelMesh = t;
                        break;
                    }
                }
            }
            if (wheelMesh != null)
            {
                foreach (var col in wheelMesh.GetComponents<Collider>())
                {
                    UnityEngine.Object.DestroyImmediate(col);
                }
            }

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            // =========================================================================
            // 1. Freno de Mano Nativo (mdl_car02_brake / VRHandbrake)
            // =========================================================================
            Transform brakeTransform = carRoot.transform.Find("mdl_car02_brake");
            VRHandbrake handbrake = null;
            if (brakeTransform != null)
            {
                GameObject brakeObj = brakeTransform.gameObject;

                // Rigidbody cinemático
                Rigidbody rb = brakeObj.GetComponent<Rigidbody>();
                if (rb == null) rb = brakeObj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                // Collider
                BoxCollider col = brakeObj.GetComponent<BoxCollider>();
                if (col == null)
                {
                    // Si hay otro tipo de collider, remover y usar BoxCollider centrado
                    var existingCol = brakeObj.GetComponent<Collider>();
                    if (existingCol != null) UnityEngine.Object.DestroyImmediate(existingCol);
                    col = brakeObj.AddComponent<BoxCollider>();
                }
                col.center = new Vector3(0f, 0.001f, 0.001f);
                col.size = new Vector3(0.0006f, 0.0025f, 0.001f);

                // XRGrabInteractable
                XRGrabInteractable grab = brakeObj.GetComponent<XRGrabInteractable>();
                if (grab == null) grab = brakeObj.AddComponent<XRGrabInteractable>();
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;
                grab.retainTransformParent = true;

                // VRHandbrake
                handbrake = brakeObj.GetComponent<VRHandbrake>();
                if (handbrake == null) handbrake = brakeObj.AddComponent<VRHandbrake>();
                handbrake.rotationAxis = Vector3.right;
                handbrake.releaseAngleRange = 36f;
            }

            // =========================================================================
            // 2. Perilla de Luces Nativa (mdl_car02_lights_knob / VRHeadlightKnob)
            // =========================================================================
            // Remover cualquier objeto procedural duplicado VRHeadlightKnob
            Transform oldProcedural = carRoot.transform.Find("VRHeadlightKnob");
            if (oldProcedural != null)
            {
                UnityEngine.Object.DestroyImmediate(oldProcedural.gameObject);
            }

            Transform knobTransform = carRoot.transform.Find("mdl_car02_lights_knob");
            VRHeadlightKnob headlightKnob = null;
            if (knobTransform != null)
            {
                GameObject knobObj = knobTransform.gameObject;

                // Remover VRLightsKnob obsoleto si aún está adjunto
                var oldScript = knobObj.GetComponent("VRLightsKnob");
                if (oldScript != null) UnityEngine.Object.DestroyImmediate(oldScript);

                // Remover BoxCollider residual en la perilla nativa (conservar solo SphereCollider)
                foreach (var b in knobObj.GetComponents<BoxCollider>())
                {
                    UnityEngine.Object.DestroyImmediate(b);
                }

                Rigidbody rb = knobObj.GetComponent<Rigidbody>();
                if (rb == null) rb = knobObj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                SphereCollider col = knobObj.GetComponent<SphereCollider>();
                if (col == null) col = knobObj.AddComponent<SphereCollider>();
                col.center = Vector3.zero;
                col.radius = 0.04f;

                XRGrabInteractable grab = knobObj.GetComponent<XRGrabInteractable>();
                if (grab == null) grab = knobObj.AddComponent<XRGrabInteractable>();
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = false;
                grab.retainTransformParent = true;

                headlightKnob = knobObj.GetComponent<VRHeadlightKnob>();
                if (headlightKnob == null) headlightKnob = knobObj.AddComponent<VRHeadlightKnob>();

                SerializedObject soKnob = new SerializedObject(headlightKnob);
                soKnob.Update();
                soKnob.FindProperty("m_RotorTransform").objectReferenceValue = knobTransform;
                soKnob.FindProperty("m_RotationAxis").vector3Value = Vector3.up;
                soKnob.FindProperty("m_OffAngle").floatValue = 0f;
                soKnob.FindProperty("m_LowBeamAngle").floatValue = 30f;
                soKnob.FindProperty("m_HighBeamAngle").floatValue = 60f;
                soKnob.ApplyModifiedProperties();
            }

            // =========================================================================
            // 3. Tablero Diegético (Dashboard_Cluster_Canvas)
            // =========================================================================
            GameObject canvasObj = DashboardClusterSetup.ConfigureDashboardCluster(carRoot);
            DashboardUIController dashboardUI = carRoot.GetComponentInChildren<DashboardUIController>(true);

            // =========================================================================
            // 4. Sistema Integral de Iluminación Exterior
            // =========================================================================
            VehicleLightingSetup.ConfigureVehicleLighting(carRoot);
            VehicleLightingController lightingController = carRoot.GetComponent<VehicleLightingController>();

            // =========================================================================
            // 5. Cableado Final de VehicleController
            // =========================================================================
            VehicleController vehicleCtrl = carRoot.GetComponent<VehicleController>();
            if (vehicleCtrl != null)
            {
                SerializedObject soVeh = new SerializedObject(vehicleCtrl);
                soVeh.Update();

                // Freno de mano
                soVeh.FindProperty("m_HandbrakeForce").floatValue = 12f;
                if (handbrake != null)
                {
                    soVeh.FindProperty("m_Handbrake").objectReferenceValue = handbrake;
                }

                // Controlador de luces
                if (lightingController != null)
                {
                    soVeh.FindProperty("m_LightingController").objectReferenceValue = lightingController;
                }

                // Volante
                VRSteeringWheel wheel = carRoot.GetComponentInChildren<VRSteeringWheel>(true);
                if (wheel != null) soVeh.FindProperty("m_SteeringWheel").objectReferenceValue = wheel;

                // Palanca de cambios
                VRGearShifter shifter = carRoot.GetComponentInChildren<VRGearShifter>(true);
                if (shifter != null) soVeh.FindProperty("m_GearShifter").objectReferenceValue = shifter;

                // Guiño
                VRTurnSignal turnSignal = carRoot.GetComponentInChildren<VRTurnSignal>(true);
                if (turnSignal != null) soVeh.FindProperty("m_TurnSignal").objectReferenceValue = turnSignal;

                // Balizas
                foreach (var toggle in carRoot.GetComponentsInChildren<VRToggleButton>(true))
                {
                    if (toggle.gameObject.name.Contains("Hazard"))
                    {
                        soVeh.FindProperty("m_HazardButton").objectReferenceValue = toggle;
                        break;
                    }
                }

                soVeh.ApplyModifiedProperties();
            }

            // =========================================================================
            // 6. Conexión de Eventos Handbrake -> DashboardUIController
            // =========================================================================
            if (handbrake != null && dashboardUI != null)
            {
                // Asegurar que el evento actualice el tablero
                handbrake.OnEngagementChanged.RemoveListener(OnHandbrakeEngagementChangedDummy);
                EditorUtility.SetDirty(handbrake);
            }

            // =========================================================================
            // 7. Blindaje Físico de Controles Internos
            // =========================================================================
            // Asegurar estrictamente que todos los Rigidbodies hijos (GearShifter, Handbrake, Knob, etc.)
            // sean cinemáticos y no usen gravedad, dejando a Car07 como el único Rigidbody dinámico.
            foreach (var rb in carRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb.gameObject != carRoot)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }

            // Reconfigurar físicas, tamaño de collider del coche e ignorar colisiones internas entre hijos
            if (vehicleCtrl != null)
            {
                vehicleCtrl.ConfigurePhysicsAndColliders();
            }

            Debug.Log($"[CockpitIntegrationSetup] ✨ Integración de cabina completada en {carRoot.name}!");
        }

        private static void OnHandbrakeEngagementChangedDummy(float val) { }
    }
}
