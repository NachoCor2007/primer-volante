using System;
using System.Collections.Generic;
using System.IO;
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
    /// Herramienta de Editor para la configuración y calibración exacta del sistema de espejos en Car07:
    /// - Posicionamiento exacto sobre la malla 3D original (retrovisor en -0.075, 1.31, 0.00; laterales en -0.88, 1.03, 0.22 y 0.90, 1.03, 0.22).
    /// - Eliminación rigurosa de duplicados tanto en el Prefab Car07 como en la escena CockpitIntegrationScene.
    /// - Renderizado garantizado hacia atrás (Quaternion.Euler(0, 180, 0)) sobre RenderTextures creadas en GPU.
    /// - Mini-panel eléctrico en la puerta del conductor.
    /// </summary>
    public static class VehicleMirrorSetupEditor
    {
        public const string PREFAB_CAR07_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car07.prefab";
        public const string SCENE_INTEGRATION_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/CockpitIntegrationScene.unity";

        private const string MIRRORS_DIR = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mirrors";
        private const string FONT_PATH = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string AUDIO_CLICK_PATH = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/DemoAssets/Audio/Button Pop.wav";

        [MenuItem("Primer Volante/Espejos/Configurar Espejos en Car07")]
        [MenuItem("Tools/Primer Volante/Setup Mirrors in Car07 (Prefab and Scene)")]
        public static void SetupMirrorsAll()
        {
            SetupMirrorsInPrefab();
            SetupMirrorsInScene();
        }

        [MenuItem("Tools/Primer Volante/Setup Mirrors in Car07 Prefab")]
        public static void SetupMirrorsInPrefab()
        {
            Debug.Log($"[VehicleMirrorSetupEditor] 📦 Configurando sistema de espejos en Prefab: {PREFAB_CAR07_PATH}...");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_CAR07_PATH);
            if (prefabRoot == null)
            {
                Debug.LogError($"[VehicleMirrorSetupEditor] ❌ No se pudo cargar el Prefab en {PREFAB_CAR07_PATH}");
                return;
            }

            try
            {
                // Limpieza total de cualquier duplicado previo en el prefab
                PurgeDuplicateMirrorsInHierarchy(prefabRoot.transform);

                ConfigureMirrorsOnCar(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_CAR07_PATH, out bool success);
                if (success)
                {
                    Debug.Log($"[VehicleMirrorSetupEditor] ✅ ¡Sistema de espejos guardado exitosamente en {PREFAB_CAR07_PATH}!");
                }
                else
                {
                    Debug.LogError($"[VehicleMirrorSetupEditor] ❌ Falló el guardado del Prefab en {PREFAB_CAR07_PATH}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Tools/Primer Volante/Setup Mirrors in Car07 Scene")]
        public static void SetupMirrorsInScene()
        {
            Debug.Log($"[VehicleMirrorSetupEditor] 🎬 Configurando sistema de espejos en escena: {SCENE_INTEGRATION_PATH}...");

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != SCENE_INTEGRATION_PATH && File.Exists(SCENE_INTEGRATION_PATH))
            {
                scene = EditorSceneManager.OpenScene(SCENE_INTEGRATION_PATH, OpenSceneMode.Single);
            }

            GameObject car = GameObject.Find("Car07");
            if (car == null)
            {
                var vehicle = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
                if (vehicle != null) car = vehicle.gameObject;
            }

            if (car == null)
            {
                Debug.LogError("[VehicleMirrorSetupEditor] ❌ No se encontró Car07 ni VehicleController en la escena.");
                return;
            }

            // 1. Limpieza de duplicados sueltos en la escena y en Car07
            PurgeSceneOrphanMirrors(scene, car);
            PurgeDuplicateMirrorsInHierarchy(car.transform);

            // 2. Configurar exactamente una instancia de cada espejo
            ConfigureMirrorsOnCar(car);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[VehicleMirrorSetupEditor] ✅ ¡Escena {SCENE_INTEGRATION_PATH} actualizada con sistema de espejos calibrado sin duplicados!");
        }

        /// <summary>
        /// Elimina todas las instancias duplicadas de espejos en la jerarquía dejando el terreno limpio para reconstruir exactamente 1 de cada una.
        /// </summary>
        public static void PurgeDuplicateMirrorsInHierarchy(Transform root)
        {
            string[] mirrorNames = new string[]
            {
                "RearviewMirror_Mount",
                "SideMirror_Left",
                "SideMirror_Right",
                "SideMirror_ControlPanel"
            };

            foreach (string mName in mirrorNames)
            {
                List<GameObject> toDestroy = new List<GameObject>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t != root && t.name == mName)
                    {
                        toDestroy.Add(t.gameObject);
                    }
                }

                for (int i = toDestroy.Count - 1; i >= 0; i--)
                {
                    if (toDestroy[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(toDestroy[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Elimina espejos huérfanos que pudieran existir a nivel raíz de la escena.
        /// </summary>
        public static void PurgeSceneOrphanMirrors(UnityEngine.SceneManagement.Scene scene, GameObject carRoot)
        {
            string[] mirrorNames = new string[]
            {
                "RearviewMirror_Mount",
                "SideMirror_Left",
                "SideMirror_Right",
                "SideMirror_ControlPanel"
            };

            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                if (rootObj == carRoot || rootObj == null) continue;

                foreach (string mName in mirrorNames)
                {
                    if (rootObj.name == mName)
                    {
                        UnityEngine.Object.DestroyImmediate(rootObj);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Configura el sistema completo de espejos sobre la raíz del vehículo con posicionamiento exacto sobre la malla 3D original.
        /// </summary>
        public static void ConfigureMirrorsOnCar(GameObject carRoot)
        {
            EnsureAssetDirectories();

            // 1. Shaders y Materiales
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Texture");

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Material casingMat = GetOrCreateMaterial($"{MIRRORS_DIR}/Mat_Mirror_Casing.mat", litShader, new Color(0.12f, 0.12f, 0.14f));
            Material panelBaseMat = GetOrCreateMaterial($"{MIRRORS_DIR}/Mat_DoorPanel_Base.mat", litShader, new Color(0.15f, 0.15f, 0.16f));
            Material panelBtnMat = GetOrCreateMaterial($"{MIRRORS_DIR}/Mat_DoorPanel_Button.mat", litShader, new Color(0.28f, 0.28f, 0.30f));

            // 2. RenderTextures dedicadas para cada espejo (creadas en GPU)
            RenderTexture rtRearview = GetOrCreateRenderTexture($"{MIRRORS_DIR}/RT_RearviewMirror.renderTexture", 512, 256);
            RenderTexture rtSideLeft = GetOrCreateRenderTexture($"{MIRRORS_DIR}/RT_SideMirror_Left.renderTexture", 256, 256);
            RenderTexture rtSideRight = GetOrCreateRenderTexture($"{MIRRORS_DIR}/RT_SideMirror_Right.renderTexture", 256, 256);

            // 3. Materiales reflectantes con inversión óptica horizontal (Tiling X = -1, Offset X = 1)
            Material matRearview = GetOrCreateMirrorMaterial($"{MIRRORS_DIR}/Mat_RearviewMirror.mat", unlitShader, rtRearview);
            Material matSideLeft = GetOrCreateMirrorMaterial($"{MIRRORS_DIR}/Mat_SideMirror_Left.mat", unlitShader, rtSideLeft);
            Material matSideRight = GetOrCreateMirrorMaterial($"{MIRRORS_DIR}/Mat_SideMirror_Right.mat", unlitShader, rtSideRight);

            // Asegurar que no queden duplicados previos en la jerarquía
            PurgeDuplicateMirrorsInHierarchy(carRoot.transform);

            // =========================================================================
            // 4. Espejo Retrovisor Central (Posición exacta: X: -0.075, Y: 1.31, Z: 0.00)
            // =========================================================================
            VRRearviewMirror rearview = CreateRearviewMirror(carRoot.transform, casingMat, matRearview, rtRearview);

            // =========================================================================
            // 5. Espejos Laterales Motorizados
            //    Izquierdo: X: -0.88, Y: 1.03, Z: 0.22 (sin brazos redundantes flotando)
            //    Derecho:   X:  0.90, Y: 1.03, Z: 0.22
            // =========================================================================
            VRSideMirror mirrorLeft = CreateSideMirror(carRoot.transform, VRSideMirror.MirrorSide.Left,
                                                      new Vector3(-0.88f, 1.03f, 0.22f), matSideLeft, rtSideLeft);
            VRSideMirror mirrorRight = CreateSideMirror(carRoot.transform, VRSideMirror.MirrorSide.Right,
                                                       new Vector3(0.90f, 1.03f, 0.22f), matSideRight, rtSideRight);

            // =========================================================================
            // 6. Mini-Panel de Control Eléctrico en la Puerta del Conductor
            // =========================================================================
            VRSideMirrorControlPanel panel = CreateDoorControlPanel(carRoot, mirrorLeft, mirrorRight, panelBaseMat, panelBtnMat);

            // =========================================================================
            // 7. Blindaje Físico: asegurar Rigidbodies hijos cinemáticos
            // =========================================================================
            foreach (var rb in carRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb.gameObject != carRoot)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }

            Debug.Log($"[VehicleMirrorSetupEditor] ✨ Espejos calibrados exitosamente en {carRoot.name}:");
            Debug.Log($"   • Retrovisor Central: {rearview.transform.parent.localPosition}");
            Debug.Log($"   • Espejo Izquierdo:   {mirrorLeft.transform.localPosition}");
            Debug.Log($"   • Espejo Derecho:     {mirrorRight.transform.localPosition}");
            Debug.Log($"   • Panel Puerta:       {panel.transform.localPosition}");
        }

        /// <summary>
        /// Crea el espejo retrovisor interior central en la posición exacta del modelo:
        /// X: -0.075, Y: 1.31, Z: 0.00 (cubriendo el retrovisor original que cuelga del techo).
        /// </summary>
        private static VRRearviewMirror CreateRearviewMirror(Transform carTransform, Material casingMat, Material mirrorMat, RenderTexture rt)
        {
            // 1. Soporte fijo en la posición exacta del retrovisor original
            GameObject mountObj = new GameObject("RearviewMirror_Mount");
            mountObj.transform.SetParent(carTransform, false);
            mountObj.transform.localPosition = new Vector3(-0.075f, 1.31f, 0.00f);
            mountObj.transform.localRotation = Quaternion.identity;

            // 2. Carcasa y rótula basculante interactuable (VRRearviewMirror)
            GameObject casingObj = new GameObject("RearviewMirror_Casing");
            casingObj.transform.SetParent(mountObj.transform, false);
            casingObj.transform.localPosition = Vector3.zero;
            casingObj.transform.localRotation = Quaternion.identity;

            // Rigidbody cinemático
            Rigidbody rb = casingObj.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // BoxCollider para agarre en VR
            BoxCollider grabCol = casingObj.AddComponent<BoxCollider>();
            grabCol.center = Vector3.zero;
            grabCol.size = new Vector3(0.22f, 0.065f, 0.04f);

            // XRGrabInteractable sin traslación ni rotación física libre
            XRGrabInteractable grab = casingObj.AddComponent<XRGrabInteractable>();
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.forceGravityOnDetach = false;
            grab.retainTransformParent = true;

            // Malla visual de la carcasa que cubre con precisión el retrovisor estático original
            GameObject casingMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            casingMesh.name = "Casing_Mesh";
            casingMesh.transform.SetParent(casingObj.transform, false);
            casingMesh.transform.localPosition = Vector3.zero;
            casingMesh.transform.localScale = new Vector3(0.20f, 0.055f, 0.025f);
            casingMesh.GetComponent<Renderer>().sharedMaterial = casingMat;
            UnityEngine.Object.DestroyImmediate(casingMesh.GetComponent<Collider>());

            // Vidrio reflectante (Quad con cara visible hacia -Z mirando directamente a los ojos del conductor)
            GameObject glassObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glassObj.name = "Mirror_Glass";
            glassObj.transform.SetParent(casingObj.transform, false);
            glassObj.transform.localPosition = new Vector3(0f, 0f, -0.0135f);
            glassObj.transform.localRotation = Quaternion.identity; // SIN rotación de 180°: cara visible hacia -Z
            glassObj.transform.localScale = new Vector3(0.19f, 0.048f, 1f);
            Renderer glassRenderer = glassObj.GetComponent<Renderer>();
            glassRenderer.sharedMaterial = mirrorMat;
            UnityEngine.Object.DestroyImmediate(glassObj.GetComponent<Collider>());

            // 3. Cámara dedicada mirando hacia atrás emparentada a la carcasa
            GameObject camObj = new GameObject("RearviewMirror_Camera");
            camObj.transform.SetParent(casingObj.transform, false);
            camObj.transform.localPosition = new Vector3(0f, -0.01f, -0.03f); // Por delante del vidrio en el habitáculo
            camObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Mira hacia atrás (-Z del auto)

            Camera cam = camObj.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = 65f;
            cam.farClipPlane = 120f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.Skybox;

            // Componente VehicleMirrorCamera para renderizado garantizado y optimización
            VehicleMirrorCamera mirrorCam = camObj.AddComponent<VehicleMirrorCamera>();
            SerializedObject soCam = new SerializedObject(mirrorCam);
            soCam.Update();
            soCam.FindProperty("m_MirrorCamera").objectReferenceValue = cam;
            soCam.FindProperty("m_RenderTexture").objectReferenceValue = rt;
            soCam.FindProperty("m_MirrorSurfaceRenderer").objectReferenceValue = glassRenderer;
            soCam.FindProperty("m_FarClipPlane").floatValue = 120f;
            soCam.FindProperty("m_FieldOfView").floatValue = 65f;
            soCam.FindProperty("m_EnableAngleCulling").boolValue = false; // Desactivado por defecto
            soCam.FindProperty("m_EnableTimeSlicing").boolValue = true;
            soCam.ApplyModifiedProperties();
            mirrorCam.ConfigureCamera();
            mirrorCam.EnsureNoAudioListener();
            mirrorCam.EnsureRenderTextureCreated();
            mirrorCam.ApplyOpticalInversion();

            // 4. Componente VRRearviewMirror en la carcasa
            VRRearviewMirror rearviewComp = casingObj.AddComponent<VRRearviewMirror>();
            SerializedObject soMirror = new SerializedObject(rearviewComp);
            soMirror.Update();
            soMirror.FindProperty("m_PivotTransform").objectReferenceValue = casingObj.transform;
            soMirror.FindProperty("m_MountTransform").objectReferenceValue = mountObj.transform;
            soMirror.FindProperty("m_RearviewCameraTransform").objectReferenceValue = camObj.transform;
            soMirror.FindProperty("m_MinYaw").floatValue = -30f;
            soMirror.FindProperty("m_MaxYaw").floatValue = 30f;
            soMirror.FindProperty("m_MinPitch").floatValue = -20f;
            soMirror.FindProperty("m_MaxPitch").floatValue = 20f;
            soMirror.FindProperty("m_HandLeverArm").floatValue = 0.15f;
            soMirror.FindProperty("m_EnableKeyboardDebug").boolValue = true;
            soMirror.ApplyModifiedProperties();
            rearviewComp.ConfigureInteractableAndPhysics();

            return rearviewComp;
        }

        /// <summary>
        /// Crea un espejo lateral exterior directamente sobre la carcasa original del modelo 3D
        /// (sin brazos flotantes redundantes), con pivote basculante interno, vidrio y cámara hacia atrás.
        /// </summary>
        private static VRSideMirror CreateSideMirror(Transform carTransform, VRSideMirror.MirrorSide side,
                                                    Vector3 exactLocalPos, Material mirrorMat, RenderTexture rt)
        {
            bool isLeft = (side == VRSideMirror.MirrorSide.Left);
            string mirrorName = isLeft ? "SideMirror_Left" : "SideMirror_Right";

            GameObject rootObj = new GameObject(mirrorName);
            rootObj.transform.SetParent(carTransform, false);
            rootObj.transform.localPosition = exactLocalPos;
            rootObj.transform.localRotation = Quaternion.identity;

            // Pivote basculante interno para el vidrio reflectante
            GameObject glassPivot = new GameObject("Glass_Pivot");
            glassPivot.transform.SetParent(rootObj.transform, false);
            glassPivot.transform.localPosition = Vector3.zero;
            // Orientación angular convergente hacia el conductor (aproximadamente -22° en Y para izq, 22° para der)
            float initialYaw = isLeft ? -22f : 22f;
            glassPivot.transform.localRotation = Quaternion.Euler(0f, initialYaw, 0f);

            // Vidrio reflectante (Quad orientado hacia atrás sobre la cara del espejo de la puerta)
            GameObject glassObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glassObj.name = "Mirror_Glass";
            glassObj.transform.SetParent(glassPivot.transform, false);
            glassObj.transform.localPosition = new Vector3(0f, 0f, -0.005f);
            glassObj.transform.localRotation = Quaternion.identity; // SIN rotación de 180°: cara visible mira hacia -Z
            glassObj.transform.localScale = new Vector3(0.08f, 0.11f, 1f);
            Renderer glassRenderer = glassObj.GetComponent<Renderer>();
            glassRenderer.sharedMaterial = mirrorMat;
            UnityEngine.Object.DestroyImmediate(glassObj.GetComponent<Collider>());

            // Cámara del espejo lateral emparentada al pivote para seguir el ajuste motorizado
            // Se posiciona ligeramente por delante del vidrio hacia atrás (-Z) para no ser ocluida
            GameObject camObj = new GameObject(isLeft ? "SideMirror_Camera_Left" : "SideMirror_Camera_Right");
            camObj.transform.SetParent(glassPivot.transform, false);
            camObj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            camObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Mira hacia atrás

            Camera cam = camObj.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = 55f;
            cam.farClipPlane = 120f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.Skybox;

            VehicleMirrorCamera mirrorCam = camObj.AddComponent<VehicleMirrorCamera>();
            SerializedObject soCam = new SerializedObject(mirrorCam);
            soCam.Update();
            soCam.FindProperty("m_MirrorCamera").objectReferenceValue = cam;
            soCam.FindProperty("m_RenderTexture").objectReferenceValue = rt;
            soCam.FindProperty("m_MirrorSurfaceRenderer").objectReferenceValue = glassRenderer;
            soCam.FindProperty("m_FarClipPlane").floatValue = 120f;
            soCam.FindProperty("m_FieldOfView").floatValue = 55f;
            soCam.FindProperty("m_EnableAngleCulling").boolValue = false; // Desactivado por defecto
            soCam.FindProperty("m_EnableTimeSlicing").boolValue = true;
            soCam.ApplyModifiedProperties();
            mirrorCam.ConfigureCamera();
            mirrorCam.EnsureNoAudioListener();
            mirrorCam.EnsureRenderTextureCreated();
            mirrorCam.ApplyOpticalInversion();

            // Componente VRSideMirror
            VRSideMirror sideMirror = rootObj.AddComponent<VRSideMirror>();
            SerializedObject soSide = new SerializedObject(sideMirror);
            soSide.Update();
            soSide.FindProperty("m_Side").enumValueIndex = (int)side;
            soSide.FindProperty("m_GlassPivot").objectReferenceValue = glassPivot.transform;
            soSide.FindProperty("m_MaxYaw").floatValue = 20f;
            soSide.FindProperty("m_MaxPitch").floatValue = 15f;
            soSide.FindProperty("m_DefaultSpeed").floatValue = 16f;
            soSide.ApplyModifiedProperties();
            sideMirror.InitializePivot();

            return sideMirror;
        }

        /// <summary>
        /// Crea el mini-panel eléctrico en el apoyabrazos de la puerta del conductor con selector [Left | OFF | Right]
        /// y mini D-Pad interactivo.
        /// </summary>
        private static VRSideMirrorControlPanel CreateDoorControlPanel(GameObject carRoot,
                                                                       VRSideMirror leftMirror,
                                                                       VRSideMirror rightMirror,
                                                                       Material panelBaseMat,
                                                                       Material btnMat)
        {
            GameObject panelObj = new GameObject("SideMirror_ControlPanel");
            panelObj.transform.SetParent(carRoot.transform, false);

            // Posición ergonómica en el apoyabrazos de la puerta del conductor (mdl_car02_door_FL)
            panelObj.transform.localPosition = new Vector3(-0.54f, 0.68f, 0.12f);
            panelObj.transform.localRotation = Quaternion.Euler(15f, 0f, -12f);

            Rigidbody panelRb = panelObj.AddComponent<Rigidbody>();
            panelRb.isKinematic = true;
            panelRb.useGravity = false;

            // Placa base del panel
            GameObject baseMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMesh.name = "Panel_Base";
            baseMesh.transform.SetParent(panelObj.transform, false);
            baseMesh.transform.localPosition = Vector3.zero;
            baseMesh.transform.localScale = new Vector3(0.075f, 0.015f, 0.13f);
            baseMesh.GetComponent<Renderer>().sharedMaterial = panelBaseMat;
            UnityEngine.Object.DestroyImmediate(baseMesh.GetComponent<Collider>());

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            // 1. Selector de 3 posiciones: [ Left | OFF | Right ]
            float selZ = 0.035f;
            float btnY = 0.011f;
            Vector3 selSize = new Vector3(0.018f, 0.010f, 0.018f);

            var (btnLeft, rendLeft) = CreateButton(panelObj.transform, "Btn_Select_Left", new Vector3(-0.022f, btnY, selZ), selSize, btnMat, "L", font);
            var (btnOff, rendOff) = CreateButton(panelObj.transform, "Btn_Select_Off", new Vector3(0f, btnY, selZ), selSize, btnMat, "OFF", font, fontSize: 0.18f);
            var (btnRight, rendRight) = CreateButton(panelObj.transform, "Btn_Select_Right", new Vector3(0.022f, btnY, selZ), selSize, btnMat, "R", font);

            // 2. Mini D-Pad (4 direcciones)
            float dpadZ = -0.025f;
            Vector3 dpadSize = new Vector3(0.016f, 0.010f, 0.016f);

            var (btnUp, _) = CreateButton(panelObj.transform, "Btn_DPad_Up", new Vector3(0f, btnY, dpadZ + 0.018f), dpadSize, btnMat, "▲", font);
            var (btnDown, _) = CreateButton(panelObj.transform, "Btn_DPad_Down", new Vector3(0f, btnY, dpadZ - 0.018f), dpadSize, btnMat, "▼", font);
            var (btnDLeft, _) = CreateButton(panelObj.transform, "Btn_DPad_Left", new Vector3(-0.018f, btnY, dpadZ), dpadSize, btnMat, "◄", font);
            var (btnDRight, _) = CreateButton(panelObj.transform, "Btn_DPad_Right", new Vector3(0.018f, btnY, dpadZ), dpadSize, btnMat, "►", font);

            AudioSource audio = panelObj.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 0.1f;
            audio.maxDistance = 2.5f;

            AudioClip clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AUDIO_CLICK_PATH);

            VRSideMirrorControlPanel panelComp = panelObj.AddComponent<VRSideMirrorControlPanel>();
            SerializedObject soPanel = new SerializedObject(panelComp);
            soPanel.Update();
            soPanel.FindProperty("m_LeftMirror").objectReferenceValue = leftMirror;
            soPanel.FindProperty("m_RightMirror").objectReferenceValue = rightMirror;
            soPanel.FindProperty("m_CurrentSelection").enumValueIndex = (int)VRSideMirrorControlPanel.MirrorSelection.Off;
            soPanel.FindProperty("m_AdjustmentSpeed").floatValue = 16f;

            soPanel.FindProperty("m_BtnSelectLeft").objectReferenceValue = btnLeft;
            soPanel.FindProperty("m_BtnSelectOff").objectReferenceValue = btnOff;
            soPanel.FindProperty("m_BtnSelectRight").objectReferenceValue = btnRight;

            soPanel.FindProperty("m_BtnUp").objectReferenceValue = btnUp;
            soPanel.FindProperty("m_BtnDown").objectReferenceValue = btnDown;
            soPanel.FindProperty("m_BtnLeft").objectReferenceValue = btnDLeft;
            soPanel.FindProperty("m_BtnRight").objectReferenceValue = btnDRight;

            soPanel.FindProperty("m_IndicatorLeft").objectReferenceValue = rendLeft;
            soPanel.FindProperty("m_IndicatorOff").objectReferenceValue = rendOff;
            soPanel.FindProperty("m_IndicatorRight").objectReferenceValue = rendRight;

            soPanel.FindProperty("m_AudioSource").objectReferenceValue = audio;
            if (clickClip != null)
            {
                soPanel.FindProperty("m_ClickSound").objectReferenceValue = clickClip;
            }

            soPanel.ApplyModifiedProperties();

            return panelComp;
        }

        private static (XRSimpleInteractable interactable, Renderer renderer) CreateButton(
            Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, string label, TMP_FontAsset font, float fontSize = 0.25f)
        {
            GameObject btnObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btnObj.name = name;
            btnObj.transform.SetParent(parent, false);
            btnObj.transform.localPosition = localPos;
            btnObj.transform.localScale = size;

            Renderer rend = btnObj.GetComponent<Renderer>();
            rend.sharedMaterial = mat;

            BoxCollider col = btnObj.GetComponent<BoxCollider>();
            if (col == null) col = btnObj.AddComponent<BoxCollider>();
            col.isTrigger = false;

            XRSimpleInteractable interactable = btnObj.AddComponent<XRSimpleInteractable>();

            if (!string.IsNullOrEmpty(label))
            {
                GameObject textObj = new GameObject("Label");
                textObj.transform.SetParent(btnObj.transform, false);
                textObj.transform.localPosition = new Vector3(0f, 0.52f, 0f);
                textObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                textObj.transform.localScale = new Vector3(1f / size.x, 1f / size.z, 1f);

                TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
                if (font != null) tmp.font = font;
                tmp.text = label;
                tmp.fontSize = fontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.rectTransform.sizeDelta = new Vector2(size.x * 20f, size.z * 20f);
            }

            return (interactable, rend);
        }

        private static void EnsureAssetDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02", "Materials");
            }
            if (!AssetDatabase.IsValidFolder(MIRRORS_DIR))
            {
                AssetDatabase.CreateFolder("Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials", "Mirrors");
            }
        }

        private static RenderTexture GetOrCreateRenderTexture(string assetPath, int width, int height)
        {
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(assetPath);
            if (rt == null)
            {
                rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
                rt.name = Path.GetFileNameWithoutExtension(assetPath);
                rt.filterMode = FilterMode.Bilinear;
                rt.wrapMode = TextureWrapMode.Clamp;
                AssetDatabase.CreateAsset(rt, assetPath);
                AssetDatabase.Refresh();
            }

            if (!rt.IsCreated())
            {
                rt.Create();
            }

            return rt;
        }

        private static Material GetOrCreateMaterial(string assetPath, Shader shader, Color color)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = Path.GetFileNameWithoutExtension(assetPath);
                mat.color = color;
                AssetDatabase.CreateAsset(mat, assetPath);
                AssetDatabase.Refresh();
            }
            return mat;
        }

        private static Material GetOrCreateMirrorMaterial(string assetPath, Shader shader, RenderTexture rt)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(mat, assetPath);
                AssetDatabase.Refresh();
            }

            if (rt != null && !rt.IsCreated())
            {
                rt.Create();
            }

            mat.mainTexture = rt;
            mat.mainTextureScale = new Vector2(-1f, 1f);
            mat.mainTextureOffset = new Vector2(1f, 0f);

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", rt);
                mat.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
                mat.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
            }

            if (mat.HasProperty("_Cull"))
            {
                mat.SetFloat("_Cull", 0f);
            }
            mat.doubleSidedGI = true;

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
