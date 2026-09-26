using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para construir, posicionar y cablear el sistema completo de iluminación vehicular
    /// (delantera, trasera, reversa, perilla VR y dashboard) en Car06.prefab y en DashboardTestingScene.
    /// </summary>
    public static class VehicleLightingSetup
    {
        private const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car06.prefab";
        private const string SCENE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/DashboardTestingScene.unity";

        [MenuItem("Tools/Primer Volante/Setup Vehicle Lighting in Car06 Prefab and Scene")]
        public static void SetupLightingAll()
        {
            SetupLightingInPrefab();
            SetupLightingInScene();
        }

        [MenuItem("Tools/Primer Volante/Setup Vehicle Lighting in Car06 Prefab")]
        public static void SetupLightingInPrefab()
        {
            Debug.Log($"[VehicleLightingSetup] 📦 Configurando iluminación en Prefab: {PREFAB_PATH}...");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            if (prefabRoot == null)
            {
                Debug.LogError($"[VehicleLightingSetup] ❌ No se pudo cargar el Prefab en {PREFAB_PATH}");
                return;
            }

            try
            {
                // Primero asegura el tablero diegético con los testigos
                DashboardClusterSetup.ConfigureDashboardCluster(prefabRoot);

                // Configura iluminación exterior e interior
                ConfigureVehicleLighting(prefabRoot);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
                Debug.Log("[VehicleLightingSetup] ✅ ¡Car06.prefab actualizado y guardado exitosamente con iluminación completa!");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Tools/Primer Volante/Setup Vehicle Lighting in Car06 Scene")]
        public static void SetupLightingInScene()
        {
            Debug.Log($"[VehicleLightingSetup] 🎬 Configurando escena: {SCENE_PATH}...");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != SCENE_PATH)
            {
                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }

            GameObject car = GameObject.Find("Car06");
            if (car == null)
            {
                var vehicle = Object.FindAnyObjectByType<VehicleController>();
                if (vehicle != null) car = vehicle.gameObject;
            }

            if (car == null)
            {
                Debug.LogError("[VehicleLightingSetup] ❌ No se encontró Car06 ni VehicleController en la escena.");
                return;
            }

            DashboardClusterSetup.ConfigureDashboardCluster(car);
            ConfigureVehicleLighting(car);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[VehicleLightingSetup] ✅ ¡Escena {SCENE_PATH} guardada exitosamente con iluminación completa!");
        }

        public static void ConfigureVehicleLighting(GameObject carRoot)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            VehicleController vehicleController = carRoot.GetComponent<VehicleController>();
            if (vehicleController == null)
            {
                vehicleController = Object.FindAnyObjectByType<VehicleController>();
            }

            DashboardUIController dashboardUI = carRoot.GetComponentInChildren<DashboardUIController>(true);

            // =========================================================================
            // 1. Perilla Virtual de 3 Modos (VRHeadlightKnob)
            // =========================================================================
            VRHeadlightKnob knob = SetupHeadlightKnob(carRoot, litShader);

            // =========================================================================
            // 2. Faros Delanteros Dobles (4 puntos) + 4 Spotlights
            // =========================================================================
            (Renderer[] outerHeadlights, Renderer[] innerHeadlights, Light[] lowBeamSpots, Light[] highBeamSpots) =
                SetupFrontHeadlights(carRoot, litShader);

            // =========================================================================
            // 3. Luces Traseras (Posición / Freno / Reversa) + Luz de Retroceso
            // =========================================================================
            (Renderer[] tailBrakeLights, Renderer thirdBrakeLight, Renderer[] reverseLights, Light reverseSpot) =
                SetupRearLights(carRoot, litShader);

            // =========================================================================
            // 4. VehicleLightingController
            // =========================================================================
            VehicleLightingController lightingController = carRoot.GetComponent<VehicleLightingController>();
            if (lightingController == null)
            {
                lightingController = carRoot.AddComponent<VehicleLightingController>();
            }

            SerializedObject so = new SerializedObject(lightingController);
            so.Update();

            if (vehicleController != null)
                so.FindProperty("m_VehicleController").objectReferenceValue = vehicleController;

            if (knob != null)
                so.FindProperty("m_HeadlightKnob").objectReferenceValue = knob;

            if (dashboardUI != null)
                so.FindProperty("m_DashboardUI").objectReferenceValue = dashboardUI;

            TurnSignalLightController turnSignalCtrl = carRoot.GetComponentInChildren<TurnSignalLightController>(true);
            if (turnSignalCtrl != null)
                so.FindProperty("m_TurnSignalController").objectReferenceValue = turnSignalCtrl;

            AssignRendererArrayProperty(so.FindProperty("m_FrontOuterHeadlights"), outerHeadlights);
            AssignRendererArrayProperty(so.FindProperty("m_FrontInnerHeadlights"), innerHeadlights);
            AssignLightArrayProperty(so.FindProperty("m_LowBeamSpotlights"), lowBeamSpots);
            AssignLightArrayProperty(so.FindProperty("m_HighBeamSpotlights"), highBeamSpots);

            AssignRendererArrayProperty(so.FindProperty("m_RearTailBrakeLights"), tailBrakeLights);
            if (thirdBrakeLight != null)
                so.FindProperty("m_ThirdBrakeLight").objectReferenceValue = thirdBrakeLight;

            AssignRendererArrayProperty(so.FindProperty("m_ReverseLights"), reverseLights);
            if (reverseSpot != null)
                so.FindProperty("m_ReverseSpotlight").objectReferenceValue = reverseSpot;

            so.ApplyModifiedProperties();

            // Cablear evento de perilla hacia lightingController
            if (knob != null)
            {
                for (int i = knob.OnModeChanged.GetPersistentEventCount() - 1; i >= 0; i--)
                {
                    UnityEditor.Events.UnityEventTools.RemovePersistentListener(knob.OnModeChanged, i);
                }

                UnityEditor.Events.UnityEventTools.AddPersistentListener(
                    knob.OnModeChanged,
                    lightingController.OnKnobModeChanged
                );
                EditorUtility.SetDirty(knob);
            }

            Debug.Log($"[VehicleLightingSetup] 💡 Sistema de iluminación vehicular configurado exitosamente en {carRoot.name}!");
        }

        private static VRHeadlightKnob SetupHeadlightKnob(GameObject carRoot, Shader shader)
        {
            Transform existingKnob = carRoot.transform.Find("VRHeadlightKnob");
            GameObject knobObj;
            if (existingKnob != null)
            {
                knobObj = existingKnob.gameObject;
                for (int i = knobObj.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(knobObj.transform.GetChild(i).gameObject);
                }
            }
            else
            {
                knobObj = new GameObject("VRHeadlightKnob");
                knobObj.transform.SetParent(carRoot.transform, false);
            }

            // Ubicación en el tablero a la izquierda del volante, apoyada al ras de la pared vertical del tablero
            knobObj.transform.localPosition = new Vector3(-0.595f, 0.916f, 0.285f);
            knobObj.transform.localRotation = Quaternion.identity;
            knobObj.transform.localScale = Vector3.one;

            // Rigidbody cinemático
            Rigidbody rb = knobObj.GetComponent<Rigidbody>();
            if (rb == null) rb = knobObj.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Collider para interacción VR centrado en la perilla visible
            SphereCollider collider = knobObj.GetComponent<SphereCollider>();
            if (collider == null) collider = knobObj.AddComponent<SphereCollider>();
            collider.center = new Vector3(0f, 0f, -0.012f);
            collider.radius = 0.038f;

            // Interactable de XR Interaction Toolkit
            XRGrabInteractable grab = knobObj.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = knobObj.AddComponent<XRGrabInteractable>();
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.forceGravityOnDetach = false;
            grab.retainTransformParent = true;

            // Pieza visual rotativa (Rotor)
            GameObject rotorObj = new GameObject("Knob_Rotor");
            rotorObj.transform.SetParent(knobObj.transform, false);
            rotorObj.transform.localPosition = Vector3.zero;
            rotorObj.transform.localRotation = Quaternion.identity;

            // Cilindro dial
            GameObject dialCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dialCylinder.name = "Dial_Mesh";
            dialCylinder.transform.SetParent(rotorObj.transform, false);
            dialCylinder.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            // Orientado con su tapa frontal hacia el conductor (-Z local)
            dialCylinder.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            dialCylinder.transform.localScale = new Vector3(0.042f, 0.012f, 0.042f);
            // Quitar collider del primitivo generado
            Object.DestroyImmediate(dialCylinder.GetComponent<Collider>());

            Material knobMat = new Material(shader);
            knobMat.name = "Mat_VRHeadlightKnob";
            knobMat.color = new Color(0.15f, 0.15f, 0.18f, 1f);
            dialCylinder.GetComponent<Renderer>().sharedMaterial = knobMat;

            // Marca / indicador de posición sobre la cara frontal del dial mirando al conductor
            GameObject notch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            notch.name = "Pointer_Mark";
            notch.transform.SetParent(rotorObj.transform, false);
            notch.transform.localPosition = new Vector3(0f, 0.016f, -0.025f);
            notch.transform.localRotation = Quaternion.identity;
            notch.transform.localScale = new Vector3(0.005f, 0.008f, 0.004f);
            Object.DestroyImmediate(notch.GetComponent<Collider>());

            Material notchMat = new Material(shader);
            notchMat.name = "Mat_KnobPointer";
            notchMat.color = new Color(1f, 0.6f, 0.1f, 1f);
            notch.GetComponent<Renderer>().sharedMaterial = notchMat;

            // Componente VRHeadlightKnob
            VRHeadlightKnob knobComp = knobObj.GetComponent<VRHeadlightKnob>();
            if (knobComp == null) knobComp = knobObj.AddComponent<VRHeadlightKnob>();

            SerializedObject soKnob = new SerializedObject(knobComp);
            soKnob.Update();
            soKnob.FindProperty("m_RotorTransform").objectReferenceValue = rotorObj.transform;
            soKnob.FindProperty("m_RotationAxis").vector3Value = Vector3.back;
            soKnob.FindProperty("m_OffAngle").floatValue = 0f;
            soKnob.FindProperty("m_LowBeamAngle").floatValue = 45f;
            soKnob.FindProperty("m_HighBeamAngle").floatValue = 90f;
            soKnob.ApplyModifiedProperties();

            return knobComp;
        }

        private static (Renderer[], Renderer[], Light[], Light[]) SetupFrontHeadlights(GameObject carRoot, Shader shader)
        {
            Transform existingGroup = carRoot.transform.Find("Headlights_Front");
            GameObject groupObj;
            if (existingGroup != null)
            {
                groupObj = existingGroup.gameObject;
                for (int i = groupObj.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(groupObj.transform.GetChild(i).gameObject);
                }
            }
            else
            {
                groupObj = new GameObject("Headlights_Front");
                groupObj.transform.SetParent(carRoot.transform, false);
            }

            // Material para ópticas delanteras
            Material lensMat = new Material(shader);
            lensMat.name = "Mat_Headlight_Lens";
            lensMat.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            // Posiciones simétricas de faros dobles (2 externos, 2 internos)
            // Centro del vehículo X = 0.072m
            float yPos = 0.65f;
            float zPos = 1.78f;

            // 1. Faros Externos (Luces Bajas)
            Renderer outerLeft = CreateHeadlightLens(groupObj.transform, "Headlight_Outer_Left", new Vector3(-0.58f, yPos, zPos), lensMat);
            Renderer outerRight = CreateHeadlightLens(groupObj.transform, "Headlight_Outer_Right", new Vector3(0.72f, yPos, zPos), lensMat);

            // 2. Faros Internos (Luces Altas)
            Renderer innerLeft = CreateHeadlightLens(groupObj.transform, "Headlight_Inner_Left", new Vector3(-0.42f, yPos, zPos), lensMat);
            Renderer innerRight = CreateHeadlightLens(groupObj.transform, "Headlight_Inner_Right", new Vector3(0.56f, yPos, zPos), lensMat);

            // 3. Spotlights 3D de Luces Bajas (~25m, ángulo ancho, ligera inclinación hacia abajo)
            Light spotLowLeft = CreateSpotlight(groupObj.transform, "SpotLight_LowBeam_Left",
                new Vector3(-0.58f, yPos, zPos + 0.04f),
                new Vector3(6f, 0f, 0f),
                range: 25f, spotAngle: 65f, innerSpotAngle: 35f,
                new Color(1f, 0.95f, 0.88f), intensity: 3.0f);

            Light spotLowRight = CreateSpotlight(groupObj.transform, "SpotLight_LowBeam_Right",
                new Vector3(0.72f, yPos, zPos + 0.04f),
                new Vector3(6f, 0f, 0f),
                range: 25f, spotAngle: 65f, innerSpotAngle: 35f,
                new Color(1f, 0.95f, 0.88f), intensity: 3.0f);

            // 4. Spotlights 3D de Luces Altas (~60m, ángulo concentrado, haz horizontal recto)
            Light spotHighLeft = CreateSpotlight(groupObj.transform, "SpotLight_HighBeam_Left",
                new Vector3(-0.42f, yPos, zPos + 0.04f),
                new Vector3(0f, 0f, 0f),
                range: 60f, spotAngle: 35f, innerSpotAngle: 20f,
                new Color(0.96f, 0.98f, 1f), intensity: 5.0f);

            Light spotHighRight = CreateSpotlight(groupObj.transform, "SpotLight_HighBeam_Right",
                new Vector3(0.56f, yPos, zPos + 0.04f),
                new Vector3(0f, 0f, 0f),
                range: 60f, spotAngle: 35f, innerSpotAngle: 20f,
                new Color(0.96f, 0.98f, 1f), intensity: 5.0f);

            return (
                new Renderer[] { outerLeft, outerRight },
                new Renderer[] { innerLeft, innerRight },
                new Light[] { spotLowLeft, spotLowRight },
                new Light[] { spotHighLeft, spotHighRight }
            );
        }

        private static (Renderer[], Renderer, Renderer[], Light) SetupRearLights(GameObject carRoot, Shader shader)
        {
            Transform existingGroup = carRoot.transform.Find("Taillights_Rear");
            GameObject groupObj;
            if (existingGroup != null)
            {
                groupObj = existingGroup.gameObject;
                for (int i = groupObj.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(groupObj.transform.GetChild(i).gameObject);
                }
            }
            else
            {
                groupObj = new GameObject("Taillights_Rear");
                groupObj.transform.SetParent(carRoot.transform, false);
            }

            Material redLensMat = new Material(shader);
            redLensMat.name = "Mat_Taillight_Red";
            redLensMat.color = new Color(0.3f, 0.05f, 0.05f, 1f);

            Material whiteLensMat = new Material(shader);
            whiteLensMat.name = "Mat_Taillight_Reverse";
            whiteLensMat.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            float yPos = 0.80f;
            float zPos = -2.66f;

            // Ópticas rojas de posición y freno
            Renderer tailLeft = CreateRectLight(groupObj.transform, "TailBrakeLight_Left",
                new Vector3(-0.52f, yPos, zPos), new Vector3(0.12f, 0.08f, 0.03f), redLensMat);

            Renderer tailRight = CreateRectLight(groupObj.transform, "TailBrakeLight_Right",
                new Vector3(0.66f, yPos, zPos), new Vector3(0.12f, 0.08f, 0.03f), redLensMat);

            // Tercera luz de stop central (en luneta trasera)
            Renderer thirdBrake = CreateRectLight(groupObj.transform, "TailBrakeLight_CenterThird",
                new Vector3(0.072f, 1.05f, -2.05f), new Vector3(0.16f, 0.03f, 0.02f), redLensMat);

            // Ópticas blancas de retroceso
            Renderer revLeft = CreateRectLight(groupObj.transform, "ReverseLight_Left",
                new Vector3(-0.40f, yPos, zPos), new Vector3(0.08f, 0.08f, 0.03f), whiteLensMat);

            Renderer revRight = CreateRectLight(groupObj.transform, "ReverseLight_Right",
                new Vector3(0.54f, yPos, zPos), new Vector3(0.08f, 0.08f, 0.03f), whiteLensMat);

            // Luz 3D auxiliar de retroceso orientada hacia atrás (Euler Y = 180°)
            Light revSpot = CreateSpotlight(groupObj.transform, "SpotLight_Reverse_Aux",
                new Vector3(0.072f, 0.75f, -2.68f),
                new Vector3(0f, 180f, 0f),
                range: 10f, spotAngle: 80f, innerSpotAngle: 40f,
                new Color(0.92f, 0.96f, 1f), intensity: 2.5f);

            return (
                new Renderer[] { tailLeft, tailRight },
                thirdBrake,
                new Renderer[] { revLeft, revRight },
                revSpot
            );
        }

        private static Renderer CreateHeadlightLens(Transform parent, string name, Vector3 localPos, Material mat)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            obj.transform.localScale = new Vector3(0.13f, 0.02f, 0.13f);
            Object.DestroyImmediate(obj.GetComponent<Collider>());

            Renderer r = obj.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        private static Renderer CreateRectLight(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = scale;
            Object.DestroyImmediate(obj.GetComponent<Collider>());

            Renderer r = obj.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        private static Light CreateSpotlight(Transform parent, string name, Vector3 localPos, Vector3 localEuler,
            float range, float spotAngle, float innerSpotAngle, Color color, float intensity)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.transform.localRotation = Quaternion.Euler(localEuler);

            Light light = obj.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
            light.color = color;
            light.intensity = intensity;
            light.enabled = false; // Comienza apagada

            return light;
        }

        private static void AssignRendererArrayProperty(SerializedProperty prop, Renderer[] renderers)
        {
            if (prop == null || renderers == null) return;
            prop.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }
        }

        private static void AssignLightArrayProperty(SerializedProperty prop, Light[] lights)
        {
            if (prop == null || lights == null) return;
            prop.arraySize = lights.Length;
            for (int i = 0; i < lights.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            }
        }
    }
}
