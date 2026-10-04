using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para la configuración automatizada del sistema de manos VR:
    /// - Creación de materiales de conductor y prefabs de manos (izq / der).
    /// - Reemplazo de los modelos de mandos tradicionales (UniversalController / XR Controller Visual)
    ///   por manos articuladas en XR Origin.
    /// - Asignación de componentes VRHandPoseTarget en Volante, Perilla de Luces, Palanca de Cambios,
    ///   Freno de Mano y Espejo Retrovisor en Car07, Car08 y en la escena CockpitIntegrationScene.
    /// - Configuración de sincronización de movimiento y posturas contextuales (SteeringWheel, KnobPinch, etc.).
    /// </summary>
    public static class VRHandPoseSetupEditor
    {
        public const string PREFAB_CAR07_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car07.prefab";
        public const string PREFAB_CAR08_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car08.prefab";
        public const string SCENE_INTEGRATION_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene/CockpitIntegrationScene.unity";

        public const string HANDS_MATERIAL_DIR = "Assets/Art/VRHands/Materials";
        public const string HANDS_MATERIAL_PATH = "Assets/Art/VRHands/Materials/M_DriverHand.mat";
        public const string HANDS_PREFAB_DIR = "Assets/Prefabs/VR";
        public const string HAND_LEFT_PREFAB_PATH = "Assets/Prefabs/VR/VRHand_Left.prefab";
        public const string HAND_RIGHT_PREFAB_PATH = "Assets/Prefabs/VR/VRHand_Right.prefab";

        public const string FBX_LEFT_PATH = "Assets/Art/VRHands/LeftHand.fbx";
        public const string FBX_RIGHT_PATH = "Assets/Art/VRHands/RightHand.fbx";
        public const string PREFAB_XR_ORIGIN_PATH = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        [MenuItem("Primer Volante/VR Hands/Dump Diagnostics")]
        public static void DumpDiagnostics()
        {
            Debug.Log("=== [VRHandPoseSetupEditor] DIAGNOSTICO DETALLADO DE MANOS VR ===");
            Scene scene = EditorSceneManager.OpenScene(SCENE_INTEGRATION_PATH, OpenSceneMode.Single);
            GameObject origin = GameObject.Find("XR Origin (XR Rig)");
            if (origin == null)
            {
                Debug.LogError("No se encontró XR Origin (XR Rig) en la escena.");
                return;
            }

            // 1. Renderers
            Renderer[] renderers = origin.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[DIAGNOSTICO] Total Renderers en XR Origin: {renderers.Length}");
            foreach (var r in renderers)
            {
                string path = AnimationUtility.CalculateTransformPath(r.transform, origin.transform);
                string extra = "";
                if (r is SkinnedMeshRenderer smr)
                {
                    extra = $" | updateWhenOffscreen: {smr.updateWhenOffscreen} | rootBone: {(smr.rootBone != null ? smr.rootBone.name : "null")} | localBounds: {smr.localBounds}";
                }
                Debug.Log($"[RENDERER] {path} | Tipo: {r.GetType().Name} | enabled: {r.enabled} | go.active: {r.gameObject.activeInHierarchy} | mat: {(r.sharedMaterial != null ? r.sharedMaterial.name : "null")}{extra}");
            }

            // 2. Controladores
            VRHandPoseController[] controllers = origin.GetComponentsInChildren<VRHandPoseController>(true);
            Debug.Log($"[DIAGNOSTICO] Total VRHandPoseController en XR Origin: {controllers.Length}");
            for (int i = 0; i < controllers.Length; i++)
            {
                var c = controllers[i];
                string path = AnimationUtility.CalculateTransformPath(c.transform, origin.transform);
                var so = new SerializedObject(c);
                var visualToHideProp = so.FindProperty("m_ControllerVisualToHide");
                string toHide = visualToHideProp.objectReferenceValue != null ? visualToHideProp.objectReferenceValue.name : "null";
                Debug.Log($"[CONTROLLER #{i}] {path} | isLeft: {c.IsLeftHand} | handVisual: {(c.HandVisual != null ? c.HandVisual.name : "null")} | visualToHide: {toHide}");
            }

            // 3. Hijos directos de Left y Right Controller
            Transform leftTr = origin.transform.Find("Camera Offset/Left Controller");
            if (leftTr != null)
            {
                Debug.Log($"[HIJOS LEFT] childCount: {leftTr.childCount}");
                for (int i = 0; i < leftTr.childCount; i++)
                {
                    var child = leftTr.GetChild(i);
                    Debug.Log($"  - Left Child #{i}: {child.name} (activeSelf: {child.gameObject.activeSelf}, isPrefab: {PrefabUtility.IsPartOfAnyPrefab(child)})");
                }
            }

            Transform rightTr = origin.transform.Find("Camera Offset/Right Controller");
            if (rightTr != null)
            {
                Debug.Log($"[HIJOS RIGHT] childCount: {rightTr.childCount}");
                for (int i = 0; i < rightTr.childCount; i++)
                {
                    var child = rightTr.GetChild(i);
                    Debug.Log($"  - Right Child #{i}: {child.name} (activeSelf: {child.gameObject.activeSelf}, isPrefab: {PrefabUtility.IsPartOfAnyPrefab(child)})");
                }
            }

            // 4. Test agarre volante (izq y der)
            VRSteeringWheel wheel = UnityEngine.Object.FindAnyObjectByType<VRSteeringWheel>();
            Camera cam = Camera.main;
            if (wheel != null && leftTr != null)
            {
                VRHandPoseTarget target = wheel.GetComponent<VRHandPoseTarget>();
                if (target != null)
                {
                    target.CalculateInitialGrip(true, leftTr, out Vector3 posL, out Quaternion rotL);
                    Transform attachTr = target.GetAttachTransform(true);
                    Vector3 worldPosL = attachTr.position + attachTr.rotation * posL;
                    Quaternion worldRotL = attachTr.rotation * rotL;
                    Vector3 palmWorldL = worldPosL + worldRotL * (target.HandPalmGripOffset);
                    float palmRadiusL = Vector3.Distance(palmWorldL, attachTr.position);
                    Debug.Log($"[VOLANTE GRIP LEFT] Wrist worldPos: {worldPosL.ToString("F3")}, Palm worldPos: {palmWorldL.ToString("F3")}, Palm Radius: {palmRadiusL:F4}m (Target: {target.WheelRadius:F4}m)");
                    if (cam != null)
                    {
                        Vector3 vp = cam.WorldToViewportPoint(palmWorldL);
                        Debug.Log($"[VOLANTE GRIP LEFT] Viewport Pos: {vp} (visible: {vp.z > 0 && vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1})");
                    }

                    if (rightTr != null)
                    {
                        target.CalculateInitialGrip(false, rightTr, out Vector3 posR, out Quaternion rotR);
                        Transform attachTrR = target.GetAttachTransform(false);
                        Vector3 worldPosR = attachTrR.position + attachTrR.rotation * posR;
                        Quaternion worldRotR = attachTrR.rotation * rotR;
                        Vector3 palmWorldR = worldPosR + worldRotR * (target.HandPalmGripOffset);
                        float palmRadiusR = Vector3.Distance(palmWorldR, attachTrR.position);
                        Debug.Log($"[VOLANTE GRIP RIGHT] Wrist worldPos: {worldPosR.ToString("F3")}, Palm worldPos: {palmWorldR.ToString("F3")}, Palm Radius: {palmRadiusR:F4}m (Target: {target.WheelRadius:F4}m)");
                    }
                }
            }

            VRGearShifter diagShifter = UnityEngine.Object.FindAnyObjectByType<VRGearShifter>();
            if (diagShifter != null && rightTr != null)
            {
                VRHandPoseTarget target = diagShifter.GetComponent<VRHandPoseTarget>();
                if (target != null)
                {
                    target.CalculateInitialGrip(false, rightTr, out Vector3 posR, out Quaternion rotR);
                    Transform attachTr = target.GetAttachTransform(false);
                    Vector3 worldPosR = attachTr.position + attachTr.rotation * posR;
                    Quaternion worldRotR = attachTr.rotation * rotR;
                    Debug.Log($"[SHIFTER GRIP TEST] Shifter attachTr: {attachTr.name} ({attachTr.position.ToString("F3")}), worldPosR: {worldPosR.ToString("F3")}, Hand fwd: {worldRotR * Vector3.forward}, up: {worldRotR * Vector3.up}");
                }
            }

            VRHandbrake diagHandbrake = UnityEngine.Object.FindAnyObjectByType<VRHandbrake>();
            if (diagHandbrake != null && rightTr != null)
            {
                VRHandPoseTarget target = diagHandbrake.GetComponent<VRHandPoseTarget>();
                if (target != null)
                {
                    target.CalculateInitialGrip(false, rightTr, out Vector3 posR, out Quaternion rotR);
                    Transform attachTr = target.GetAttachTransform(false);
                    Vector3 worldPosR = attachTr.position + attachTr.rotation * posR;
                    Quaternion worldRotR = attachTr.rotation * rotR;
                    Debug.Log($"[HANDBRAKE GRIP TEST] Handbrake attachTr: {attachTr.name} ({attachTr.position.ToString("F3")}), worldPosR: {worldPosR.ToString("F3")}, Hand fwd: {worldRotR * Vector3.forward}, up: {worldRotR * Vector3.up}");
                }
            }

            // 5. Listar todas las instancias de Volantes, Cambios y Frenos en la escena
            var allWheels = UnityEngine.Object.FindObjectsByType<VRSteeringWheel>(FindObjectsSortMode.None);
            Debug.Log($"[INSTANCIAS] Total VRSteeringWheel: {allWheels.Length}");
            foreach (var w in allWheels)
            {
                var r = w.GetComponentInChildren<Renderer>();
                string boundsStr = r != null ? $"bounds center: {r.bounds.center.ToString("F4")}, extents: {r.bounds.extents.ToString("F4")}" : "no renderer";
                Debug.Log($"  - Wheel: {w.gameObject.name} | pos: {w.transform.position.ToString("F4")} | fwd: {w.transform.forward.ToString("F4")} | up: {w.transform.up.ToString("F4")} | rgt: {w.transform.right.ToString("F4")} | localRot: {w.transform.localEulerAngles} | {boundsStr}");
                
                MeshFilter mf = w.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Vector3 meshCenterInWheel = w.transform.InverseTransformPoint(mf.transform.TransformPoint(mf.sharedMesh.bounds.center));
                    Debug.Log($"  - Wheel Mesh Center in Wheel Local: {meshCenterInWheel.ToString("F4")} (Offset from Pivot: {meshCenterInWheel.magnitude:F4}m)");
                    
                    // Calcular radio medio de los vértices del aro respecto al pivote
                    Vector3[] verts = mf.sharedMesh.vertices;
                    float minRad = float.MaxValue, maxRad = float.MinValue, sumRad = 0f;
                    int count = 0;
                    for (int vi = 0; vi < verts.Length; vi++)
                    {
                        Vector3 vInWheel = w.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[vi]));
                        float rad = new Vector2(vInWheel.x, vInWheel.y).magnitude;
                        if (rad > 0.10f) // Solo vértices del aro exterior
                        {
                            minRad = Mathf.Min(minRad, rad);
                            maxRad = Mathf.Max(maxRad, rad);
                            sumRad += rad;
                            count++;
                        }
                    }
                    if (count > 0)
                    {
                        Debug.Log($"  - Wheel Rim Vertices: count={count}, minRad={minRad:F4}m, maxRad={maxRad:F4}m, avgRad={(sumRad/count):F4}m");
                    }
                }
            }

            var allShifters = UnityEngine.Object.FindObjectsByType<VRGearShifter>(FindObjectsSortMode.None);
            Debug.Log($"[INSTANCIAS] Total VRGearShifter: {allShifters.Length}");
            foreach (var s in allShifters)
            {
                var r = s.GetComponentInChildren<Renderer>();
                string boundsStr = r != null ? $"bounds center: {r.bounds.center}, extents: {r.bounds.extents}" : "no renderer";
                Debug.Log($"  - Shifter: {s.gameObject.name} | pos: {s.transform.position} | fwd: {s.transform.forward} | up: {s.transform.up} | rgt: {s.transform.right} | localRot: {s.transform.localEulerAngles} | {boundsStr}");
            }

            var allHandbrakes = UnityEngine.Object.FindObjectsByType<VRHandbrake>(FindObjectsSortMode.None);
            Debug.Log($"[INSTANCIAS] Total VRHandbrake: {allHandbrakes.Length}");
            foreach (var h in allHandbrakes)
            {
                var r = h.GetComponentInChildren<Renderer>();
                string boundsStr = r != null ? $"bounds center: {r.bounds.center}, extents: {r.bounds.extents}" : "no renderer";
                Debug.Log($"  - Handbrake: {h.gameObject.name} | pos: {h.transform.position} | fwd: {h.transform.forward} | up: {h.transform.up} | rgt: {h.transform.right} | localRot: {h.transform.localEulerAngles} | {boundsStr}");
            }

            // 6. Diagnóstico de huesos de las manos (muñeca y palma)
            GameObject leftPre = AssetDatabase.LoadAssetAtPath<GameObject>(HAND_LEFT_PREFAB_PATH);
            if (leftPre != null)
            {
                Transform lWrist = leftPre.transform.Find("L_Wrist");
                Transform lPalm = leftPre.transform.Find("L_Wrist/L_Palm");
                Transform lMid = leftPre.transform.Find("L_Wrist/L_MiddleMetacarpal/L_MiddleProximal/L_MiddleIntermediate/L_MiddleDistal/L_MiddleTip");
                Transform lIdx = leftPre.transform.Find("L_Wrist/L_IndexMetacarpal/L_IndexProximal/L_IndexIntermediate/L_IndexDistal/L_IndexTip");
                Vector3 pWrist = lWrist != null ? lWrist.position : Vector3.zero;
                Vector3 pPalm = lPalm != null ? lPalm.position : Vector3.zero;
                Vector3 pMid = lMid != null ? lMid.position : Vector3.zero;
                Debug.Log($"[HAND BONES LEFT] Wrist: {pWrist.ToString("F4")}, Palm: {pPalm.ToString("F4")}, MidTip: {pMid.ToString("F4")}");
                Debug.Log($"[HAND BONES LEFT] Wrist->Palm vec: {(pPalm - pWrist).ToString("F4")}, Palm->MidTip vec: {(pMid - pPalm).ToString("F4")}");
                if (lPalm != null)
                {
                    Debug.Log($"[HAND BONES LEFT] Palm forward: {lPalm.forward.ToString("F4")}, up: {lPalm.up.ToString("F4")}, right: {lPalm.right.ToString("F4")}");
                }
            }

            // 7. Diagnóstico de mano cerrada en SteeringWheel pose
            GameObject testObj = new GameObject("Diag_Hand_Test");
            try
            {
                if (leftPre != null)
                {
                    GameObject hInst = UnityEngine.Object.Instantiate(leftPre, testObj.transform);
                    VRHandPoseController ctrl = testObj.AddComponent<VRHandPoseController>();
                    ctrl.SetHandVisual(hInst.transform, isLeft: true);
                    ctrl.SetPose(HandPoseType.SteeringWheel, instant: true);

                    Transform palm = hInst.transform.Find("L_Wrist/L_Palm");
                    Transform idxProx = hInst.transform.Find("L_Wrist/L_IndexMetacarpal/L_IndexProximal");
                    Transform idxTip = hInst.transform.Find("L_Wrist/L_IndexMetacarpal/L_IndexProximal/L_IndexIntermediate/L_IndexDistal/L_IndexTip");
                    Transform midProx = hInst.transform.Find("L_Wrist/L_MiddleMetacarpal/L_MiddleProximal");
                    Transform midTip = hInst.transform.Find("L_Wrist/L_MiddleMetacarpal/L_MiddleProximal/L_MiddleIntermediate/L_MiddleDistal/L_MiddleTip");
                    Transform thmTip = hInst.transform.Find("L_Wrist/L_ThumbMetacarpal/L_ThumbProximal/L_ThumbDistal/L_ThumbTip");

                    Vector3 pPalmLocal = hInst.transform.InverseTransformPoint(palm.position);
                    Vector3 pMidProxLocal = hInst.transform.InverseTransformPoint(midProx.position);
                    Vector3 pMidTipLocal = hInst.transform.InverseTransformPoint(midTip.position);
                    Vector3 pThmTipLocal = hInst.transform.InverseTransformPoint(thmTip.position);

                    Debug.Log($"[STEERING POSE LEFT] Palm: {pPalmLocal.ToString("F4")}, MidProx: {pMidProxLocal.ToString("F4")}, MidTip: {pMidTipLocal.ToString("F4")}, ThmTip: {pThmTipLocal.ToString("F4")}");
                    Vector3 gripCenter = (pPalmLocal + pMidProxLocal + pMidTipLocal) / 3f;
                    Debug.Log($"[STEERING POSE LEFT] Estimated Grip Cavity Center: {gripCenter.ToString("F4")}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testObj);
            }
        }

        [MenuItem("Primer Volante/VR Hands/Setup VR Hands System")]
        public static void SetupVRHandsSystem()
        {
            Debug.Log("=== [VRHandPoseSetupEditor] Iniciando configuración de Manos VR ===");

            // 1. Asegurar material de mano/guante
            Material handMat = EnsureHandMaterial();

            // 2. Asegurar prefabs de manos izquierda y derecha
            GameObject leftPrefab = EnsureHandPrefab(isLeft: true, handMat);
            GameObject rightPrefab = EnsureHandPrefab(isLeft: false, handMat);

            // 3. Configurar interactables en Prefab Car07
            SetupVehiclePrefab(PREFAB_CAR07_PATH);

            // 4. Configurar interactables en Prefab Car08
            SetupVehiclePrefab(PREFAB_CAR08_PATH);

            // 5. Configurar Prefab XR Origin
            SetupXROriginPrefab(leftPrefab, rightPrefab, handMat);

            // 6. Configurar escena de integración y XR Origin en escena
            SetupIntegrationScene(leftPrefab, rightPrefab, handMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("=== [VRHandPoseSetupEditor] Configuración de Manos VR completada con éxito ===");
        }

        public static Material EnsureHandMaterial()
        {
            if (!Directory.Exists(HANDS_MATERIAL_DIR))
            {
                Directory.CreateDirectory(HANDS_MATERIAL_DIR);
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(HANDS_MATERIAL_PATH);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");

                mat = new Material(shader);
                mat.name = "M_DriverHand";
                // Color elegante de guante de cuero de conductor (carbón oscuro satinado)
                mat.color = new Color(0.18f, 0.18f, 0.20f, 1f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);

                AssetDatabase.CreateAsset(mat, HANDS_MATERIAL_PATH);
                EditorUtility.SetDirty(mat);
                Debug.Log($"[VRHandPoseSetupEditor] Material de mano creado en: {HANDS_MATERIAL_PATH}");
            }

            return mat;
        }

        public static GameObject EnsureHandPrefab(bool isLeft, Material handMat)
        {
            if (!Directory.Exists(HANDS_PREFAB_DIR))
            {
                Directory.CreateDirectory(HANDS_PREFAB_DIR);
            }

            string prefabPath = isLeft ? HAND_LEFT_PREFAB_PATH : HAND_RIGHT_PREFAB_PATH;
            string fbxPath = isLeft ? FBX_LEFT_PATH : FBX_RIGHT_PATH;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                GameObject fbxObj = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbxObj == null)
                {
                    Debug.LogError($"[VRHandPoseSetupEditor] No se encontró el modelo FBX en: {fbxPath}");
                    return null;
                }

                GameObject instance = UnityEngine.Object.Instantiate(fbxObj);
                instance.name = isLeft ? "VRHand_Left" : "VRHand_Right";

                SkinnedMeshRenderer smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    if (handMat != null) smr.sharedMaterial = handMat;
                    smr.updateWhenOffscreen = true;
                }

                prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                UnityEngine.Object.DestroyImmediate(instance);

                Debug.Log($"[VRHandPoseSetupEditor] Prefab de mano creado en: {prefabPath}");
            }
            else
            {
                // Asegurar que el prefab existente tenga updateWhenOffscreen activado
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    SkinnedMeshRenderer smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        bool modified = false;
                        if (!smr.updateWhenOffscreen)
                        {
                            smr.updateWhenOffscreen = true;
                            modified = true;
                        }
                        if (handMat != null && smr.sharedMaterial != handMat)
                        {
                            smr.sharedMaterial = handMat;
                            modified = true;
                        }
                        if (modified)
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                            Debug.Log($"[VRHandPoseSetupEditor] Prefab de mano actualizado (updateWhenOffscreen=true): {prefabPath}");
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return prefab;
        }

        public static void ConfigureSteeringWheelTarget(VRSteeringWheel wheel)
        {
            if (wheel == null) return;
            VRHandPoseTarget target = wheel.GetComponent<VRHandPoseTarget>();
            if (target == null) target = wheel.gameObject.AddComponent<VRHandPoseTarget>();

            target.PoseType = HandPoseType.SteeringWheel;
            target.FollowObjectMotion = true;
            target.UseDynamicContactPoint = true;
            target.WheelRadius = 0.165f;
            target.HandPalmGripOffset = new Vector3(0f, -0.015f, 0f);
            target.LeftAttachTransform = null;
            target.RightAttachTransform = null;
            EditorUtility.SetDirty(wheel.gameObject);
        }

        public static void ConfigureShifterTarget(VRGearShifter shifter)
        {
            if (shifter == null) return;
            VRHandPoseTarget target = shifter.GetComponent<VRHandPoseTarget>();
            if (target == null) target = shifter.gameObject.AddComponent<VRHandPoseTarget>();

            target.PoseType = HandPoseType.GearShifter;
            target.FollowObjectMotion = true;
            target.UseDynamicContactPoint = false;

            Transform rootTr = shifter.transform.root;
            Vector3 cabinUp = rootTr != null ? rootTr.up : Vector3.up;
            Quaternion cabinRot = rootTr != null ? rootTr.rotation : Quaternion.identity;

            Transform oldAttach = shifter.transform.Find("HandAttach_Shifter");
            if (oldAttach != null)
            {
                UnityEngine.Object.DestroyImmediate(oldAttach.gameObject);
            }

            Transform attachKnob = shifter.transform.Find("Attach_Knob");
            if (attachKnob == null)
            {
                GameObject go = new GameObject("Attach_Knob");
                go.transform.SetParent(shifter.transform, false);
                attachKnob = go.transform;
            }

            // Ubicar el anclaje exactamente en el pomo superior de la palanca (0.18m arriba del pivote)
            // Asignar en world space permite que Unity maneje automáticamente las escalas de modelos FBX (lossyScale=100)
            attachKnob.position = shifter.transform.position + cabinUp * 0.18f;
            // Palma ahuecada orientada hacia abajo, dedos apuntando hacia el frente de la cabina
            attachKnob.rotation = cabinRot * Quaternion.Euler(15f, 0f, 0f);

            var grab = shifter.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null)
            {
                grab.attachTransform = attachKnob;
                EditorUtility.SetDirty(grab);
            }

            target.LeftAttachTransform = attachKnob;
            target.RightAttachTransform = attachKnob;
            EditorUtility.SetDirty(shifter.gameObject);
        }

        public static void ConfigureHandbrakeTarget(VRHandbrake handbrake)
        {
            if (handbrake == null) return;
            VRHandPoseTarget target = handbrake.GetComponent<VRHandPoseTarget>();
            if (target == null) target = handbrake.gameObject.AddComponent<VRHandPoseTarget>();

            target.PoseType = HandPoseType.Handbrake;
            target.FollowObjectMotion = true;
            target.UseDynamicContactPoint = false;

            Transform rootTr = handbrake.transform.root;
            Vector3 cabinUp = rootTr != null ? rootTr.up : Vector3.up;
            Vector3 cabinFwd = rootTr != null ? rootTr.forward : Vector3.forward;
            Quaternion cabinRot = rootTr != null ? rootTr.rotation : Quaternion.identity;

            // Empuñadura del freno de mano: 18cm hacia adelante y 12cm hacia arriba respecto al pivote
            Vector3 gripWorldPos = handbrake.transform.position + cabinUp * 0.12f + cabinFwd * 0.18f;

            Transform attachRight = handbrake.transform.Find("HandAttach_Right");
            if (attachRight == null)
            {
                GameObject go = new GameObject("HandAttach_Right");
                go.transform.SetParent(handbrake.transform, false);
                attachRight = go.transform;
            }
            attachRight.position = gripWorldPos;
            attachRight.rotation = cabinRot * Quaternion.Euler(20f, -10f, 35f);

            Transform attachLeft = handbrake.transform.Find("HandAttach_Left");
            if (attachLeft == null)
            {
                GameObject go = new GameObject("HandAttach_Left");
                go.transform.SetParent(handbrake.transform, false);
                attachLeft = go.transform;
            }
            attachLeft.position = gripWorldPos;
            attachLeft.rotation = cabinRot * Quaternion.Euler(20f, 10f, -35f);

            var grab = handbrake.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null)
            {
                grab.attachTransform = attachRight;
                EditorUtility.SetDirty(grab);
            }

            target.RightAttachTransform = attachRight;
            target.LeftAttachTransform = attachLeft;
            EditorUtility.SetDirty(handbrake.gameObject);
        }

        public static void SetupVehiclePrefab(string prefabPath)
        {
            if (!File.Exists(prefabPath))
            {
                Debug.LogWarning($"[VRHandPoseSetupEditor] Prefab no encontrado: {prefabPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool modified = false;

            try
            {
                // Volante
                VRSteeringWheel wheel = root.GetComponentInChildren<VRSteeringWheel>(true);
                if (wheel != null)
                {
                    ConfigureSteeringWheelTarget(wheel);
                    modified = true;
                }

                // Perilla de luces
                VRHeadlightKnob knob = root.GetComponentInChildren<VRHeadlightKnob>(true);
                if (knob != null)
                {
                    VRHandPoseTarget target = knob.GetComponent<VRHandPoseTarget>();
                    if (target == null) target = knob.gameObject.AddComponent<VRHandPoseTarget>();

                    target.PoseType = HandPoseType.KnobPinch;
                    target.FollowObjectMotion = true;
                    target.UseDynamicContactPoint = false;
                    modified = true;
                }

                // Palanca de cambios con anclaje dedicado
                VRGearShifter shifter = root.GetComponentInChildren<VRGearShifter>(true);
                if (shifter != null)
                {
                    ConfigureShifterTarget(shifter);
                    modified = true;
                }

                // Freno de mano con anclajes dedicados
                VRHandbrake handbrake = root.GetComponentInChildren<VRHandbrake>(true);
                if (handbrake != null)
                {
                    ConfigureHandbrakeTarget(handbrake);
                    modified = true;
                }

                // Espejo retrovisor
                VRRearviewMirror mirror = root.GetComponentInChildren<VRRearviewMirror>(true);
                if (mirror != null)
                {
                    VRHandPoseTarget target = mirror.GetComponent<VRHandPoseTarget>();
                    if (target == null) target = mirror.gameObject.AddComponent<VRHandPoseTarget>();

                    target.PoseType = HandPoseType.RearviewMirror;
                    target.FollowObjectMotion = true;
                    target.UseDynamicContactPoint = false;
                    modified = true;
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"[VRHandPoseSetupEditor] VRHandPoseTarget configurado con éxito en prefab: {prefabPath}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void SetupIntegrationScene(GameObject leftPrefab, GameObject rightPrefab, Material handMat)
        {
            if (!File.Exists(SCENE_INTEGRATION_PATH))
            {
                Debug.LogWarning($"[VRHandPoseSetupEditor] Escena no encontrada: {SCENE_INTEGRATION_PATH}");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(SCENE_INTEGRATION_PATH, OpenSceneMode.Single);
            bool modified = false;

            // 1. Configurar VRHandPoseTarget en objetos de la escena
            VRSteeringWheel wheel = UnityEngine.Object.FindAnyObjectByType<VRSteeringWheel>();
            if (wheel != null)
            {
                ConfigureSteeringWheelTarget(wheel);
                modified = true;
            }

            VRHeadlightKnob knob = UnityEngine.Object.FindAnyObjectByType<VRHeadlightKnob>();
            if (knob != null)
            {
                VRHandPoseTarget target = knob.GetComponent<VRHandPoseTarget>();
                if (target == null) target = knob.gameObject.AddComponent<VRHandPoseTarget>();

                target.PoseType = HandPoseType.KnobPinch;
                target.FollowObjectMotion = true;
                target.UseDynamicContactPoint = false;
                EditorUtility.SetDirty(knob.gameObject);
                modified = true;
            }

            VRGearShifter shifter = UnityEngine.Object.FindAnyObjectByType<VRGearShifter>();
            if (shifter != null)
            {
                ConfigureShifterTarget(shifter);
                modified = true;
            }

            VRHandbrake handbrake = UnityEngine.Object.FindAnyObjectByType<VRHandbrake>();
            if (handbrake != null)
            {
                ConfigureHandbrakeTarget(handbrake);
                modified = true;
            }

            VRRearviewMirror mirror = UnityEngine.Object.FindAnyObjectByType<VRRearviewMirror>();
            if (mirror != null)
            {
                VRHandPoseTarget target = mirror.GetComponent<VRHandPoseTarget>();
                if (target == null) target = mirror.gameObject.AddComponent<VRHandPoseTarget>();

                target.PoseType = HandPoseType.RearviewMirror;
                target.FollowObjectMotion = true;
                target.UseDynamicContactPoint = false;
                EditorUtility.SetDirty(mirror.gameObject);
                modified = true;
            }

            // 2. Configurar mandos en XR Origin (XR Rig)
            GameObject origin = GameObject.Find("XR Origin (XR Rig)");
            if (origin != null)
            {
                Transform leftCtrl = origin.transform.Find("Camera Offset/Left Controller");
                Transform rightCtrl = origin.transform.Find("Camera Offset/Right Controller");

                if (leftCtrl != null && leftPrefab != null)
                {
                    ConfigureControllerHand(leftCtrl, leftPrefab, isLeft: true, handMat);
                    modified = true;
                }

                if (rightCtrl != null && rightPrefab != null)
                {
                    ConfigureControllerHand(rightCtrl, rightPrefab, isLeft: false, handMat);
                    modified = true;
                }
            }

            if (modified)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[VRHandPoseSetupEditor] Escena guardada exitosamente: {SCENE_INTEGRATION_PATH}");
            }
        }

        public static void SetupXROriginPrefab(GameObject leftPrefab, GameObject rightPrefab, Material handMat)
        {
            if (!File.Exists(PREFAB_XR_ORIGIN_PATH))
            {
                Debug.LogWarning($"[VRHandPoseSetupEditor] Prefab XR Origin no encontrado en: {PREFAB_XR_ORIGIN_PATH}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_XR_ORIGIN_PATH);
            try
            {
                Transform leftCtrl = root.transform.Find("Camera Offset/Left Controller");
                Transform rightCtrl = root.transform.Find("Camera Offset/Right Controller");

                if (leftCtrl != null && leftPrefab != null)
                {
                    ConfigureControllerHand(leftCtrl, leftPrefab, isLeft: true, handMat);
                }

                if (rightCtrl != null && rightPrefab != null)
                {
                    ConfigureControllerHand(rightCtrl, rightPrefab, isLeft: false, handMat);
                }

                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_XR_ORIGIN_PATH);
                Debug.Log($"[VRHandPoseSetupEditor] Prefab XR Origin guardado exitosamente con Manos VR: {PREFAB_XR_ORIGIN_PATH}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureControllerHand(Transform controllerTr, GameObject handPrefab, bool isLeft, Material handMat)
        {
            // 1. Deduplicar componentes VRHandPoseController en el controlador
            VRHandPoseController[] existingControllers = controllerTr.GetComponents<VRHandPoseController>();
            VRHandPoseController poseCtrl = null;
            if (existingControllers.Length > 0)
            {
                poseCtrl = existingControllers[0];
                for (int i = 1; i < existingControllers.Length; i++)
                {
                    UnityEngine.Object.DestroyImmediate(existingControllers[i]);
                }
            }
            else
            {
                poseCtrl = controllerTr.gameObject.AddComponent<VRHandPoseController>();
            }

            // 2. Ocultar y registrar modelo tradicional del mando
            string visualName = isLeft ? "Left Controller Visual" : "Right Controller Visual";
            Transform visualTr = controllerTr.Find(visualName);
            if (visualTr != null)
            {
                visualTr.gameObject.SetActive(false);
                poseCtrl.SetControllerVisualToHide(visualTr.gameObject);
            }

            // 3. Deduplicar instancias de manos hijas (eliminar duplicados y manos del lado contrario)
            string targetHandName = isLeft ? "VRHand_Left" : "VRHand_Right";
            string oppositeHandName = isLeft ? "VRHand_Right" : "VRHand_Left";

            System.Collections.Generic.List<GameObject> toDestroy = new System.Collections.Generic.List<GameObject>();
            GameObject handObj = null;

            for (int i = 0; i < controllerTr.childCount; i++)
            {
                Transform child = controllerTr.GetChild(i);
                if (child.name == targetHandName)
                {
                    if (handObj == null)
                    {
                        handObj = child.gameObject;
                    }
                    else
                    {
                        toDestroy.Add(child.gameObject);
                    }
                }
                else if (child.name == oppositeHandName)
                {
                    toDestroy.Add(child.gameObject);
                }
            }

            foreach (var extra in toDestroy)
            {
                UnityEngine.Object.DestroyImmediate(extra);
            }

            if (handObj == null)
            {
                handObj = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, controllerTr);
                handObj.name = targetHandName;
            }

            if (handObj != null)
            {
                handObj.transform.localPosition = Vector3.zero;
                handObj.transform.localRotation = Quaternion.identity;
                handObj.transform.localScale = Vector3.one;

                SkinnedMeshRenderer smr = handObj.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null)
                {
                    if (handMat != null) smr.sharedMaterial = handMat;
                    smr.updateWhenOffscreen = true;
                }

                poseCtrl.SetHandVisual(handObj.transform, isLeft);
            }

            EditorUtility.SetDirty(controllerTr.gameObject);
            Debug.Log($"[VRHandPoseSetupEditor] Mano {(isLeft ? "Izquierda" : "Derecha")} configurada limpiamente en: {controllerTr.name}");
        }
    }
}
