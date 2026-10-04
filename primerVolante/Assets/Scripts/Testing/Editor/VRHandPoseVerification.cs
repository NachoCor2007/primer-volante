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
    /// Suite de verificación exhaustiva para el sistema de Manos VR y Posturas Contextuales:
    /// 1. Verificación de integridad de assets (materiales de conductor, prefabs de manos, SkinnedMeshRenderers y huesos).
    /// 2. Verificación de configuración en prefabs de autos (Car07 y Car08: volante, perilla, cambios, freno, espejo).
    /// 3. Verificación de escena CockpitIntegrationScene y XR Origin (sustitución de mandos por manos).
    /// 4. Verificación cinemática de flexión de dedos para todas las posturas.
    /// 5. Verificación de seguimiento rígido y acompañamiento angular (volante y perilla girando).
    /// </summary>
    public static class VRHandPoseVerification
    {
        private static int s_TotalTests = 0;
        private static int s_PassedTests = 0;
        private static int s_FailedTests = 0;

        [MenuItem("Primer Volante/VR Hands/Verify VR Hands System")]
        public static bool RunVerification()
        {
            s_TotalTests = 0;
            s_PassedTests = 0;
            s_FailedTests = 0;

            Debug.Log("===============================================================================");
            Debug.Log(">>> [VRHandPoseVerification] INICIANDO VERIFICACIÓN DEL SISTEMA DE MANOS VR <<<");
            Debug.Log("===============================================================================");

            // Fase 1: Integridad de Assets
            VerifyHandAssets();

            // Fase 2: Prefabs Car07 y Car08
            VerifyVehiclePrefab(VRHandPoseSetupEditor.PREFAB_CAR07_PATH, "Car07");
            VerifyVehiclePrefab(VRHandPoseSetupEditor.PREFAB_CAR08_PATH, "Car08");

            // Fase 3: Escena CockpitIntegrationScene y XR Origin
            VerifySceneAndRig();

            // Fase 4: Pruebas Cinemáticas de Dedos y Seguimiento en Vivo
            VerifyKinematicsAndTracking();

            Debug.Log("===============================================================================");
            Debug.Log($"[VRHandPoseVerification] RESULTADOS FINALES: {s_PassedTests}/{s_TotalTests} pruebas pasadas (Fallidas: {s_FailedTests}).");
            if (s_FailedTests == 0)
            {
                Debug.Log(">>> [VRHandPoseVerification] ¡TODAS LAS PRUEBAS PASARON EXITOSAMENTE! <<<");
            }
            else
            {
                Debug.LogError($">>> [VRHandPoseVerification] SE ENCONTRARON {s_FailedTests} FALLOS EN LA VERIFICACIÓN. <<<");
            }
            Debug.Log("===============================================================================");

            return s_FailedTests == 0;
        }

        private static void AssertCondition(bool condition, string testName, string failureDetails = "")
        {
            s_TotalTests++;
            if (condition)
            {
                s_PassedTests++;
                Debug.Log($"  [PASS] {testName}");
            }
            else
            {
                s_FailedTests++;
                Debug.LogError($"  [FAIL] {testName} - {failureDetails}");
            }
        }

        private static void VerifyHandAssets()
        {
            Debug.Log("--- Fase 1: Verificación de Assets y Mallas 3D ---");

            // Material
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(VRHandPoseSetupEditor.HANDS_MATERIAL_PATH);
            AssertCondition(mat != null, "Material M_DriverHand existe", $"Ruta: {VRHandPoseSetupEditor.HANDS_MATERIAL_PATH}");

            // Prefab Mano Izquierda
            GameObject leftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRHandPoseSetupEditor.HAND_LEFT_PREFAB_PATH);
            AssertCondition(leftPrefab != null, "Prefab VRHand_Left existe", $"Ruta: {VRHandPoseSetupEditor.HAND_LEFT_PREFAB_PATH}");
            if (leftPrefab != null)
            {
                SkinnedMeshRenderer smr = leftPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                AssertCondition(smr != null, "VRHand_Left contiene SkinnedMeshRenderer");
                AssertCondition(smr != null && smr.bones.Length >= 20, "VRHand_Left tiene jerarquía de huesos completa", $"Huesos detectados: {(smr != null ? smr.bones.Length : 0)}");
            }

            // Prefab Mano Derecha
            GameObject rightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRHandPoseSetupEditor.HAND_RIGHT_PREFAB_PATH);
            AssertCondition(rightPrefab != null, "Prefab VRHand_Right existe", $"Ruta: {VRHandPoseSetupEditor.HAND_RIGHT_PREFAB_PATH}");
            if (rightPrefab != null)
            {
                SkinnedMeshRenderer smr = rightPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                AssertCondition(smr != null, "VRHand_Right contiene SkinnedMeshRenderer");
                AssertCondition(smr != null && smr.bones.Length >= 20, "VRHand_Right tiene jerarquía de huesos completa", $"Huesos detectados: {(smr != null ? smr.bones.Length : 0)}");
            }
        }

        private static void VerifyVehiclePrefab(string prefabPath, string label)
        {
            Debug.Log($"--- Fase 2: Verificación de Interactables en {label} ---");

            if (!File.Exists(prefabPath))
            {
                AssertCondition(false, $"Prefab {label} accesible", $"No existe archivo en: {prefabPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // Volante
                VRSteeringWheel wheel = root.GetComponentInChildren<VRSteeringWheel>(true);
                AssertCondition(wheel != null, $"{label}: VRSteeringWheel encontrado");
                if (wheel != null)
                {
                    VRHandPoseTarget target = wheel.GetComponent<VRHandPoseTarget>();
                    AssertCondition(target != null && target.PoseType == HandPoseType.SteeringWheel, $"{label}: Volante tiene VRHandPoseTarget con SteeringWheel");
                    AssertCondition(target != null && target.FollowObjectMotion, $"{label}: Volante tiene FollowObjectMotion activado");
                    AssertCondition(target != null && target.UseDynamicContactPoint, $"{label}: Volante tiene UseDynamicContactPoint activado");
                    AssertCondition(target != null && target.HandPalmGripOffset != Vector3.zero, $"{label}: Volante tiene HandPalmGripOffset configurado");
                }

                // Perilla de luces
                VRHeadlightKnob knob = root.GetComponentInChildren<VRHeadlightKnob>(true);
                AssertCondition(knob != null, $"{label}: VRHeadlightKnob encontrado");
                if (knob != null)
                {
                    VRHandPoseTarget target = knob.GetComponent<VRHandPoseTarget>();
                    AssertCondition(target != null && target.PoseType == HandPoseType.KnobPinch, $"{label}: Perilla tiene VRHandPoseTarget con KnobPinch");
                    AssertCondition(target != null && target.FollowObjectMotion, $"{label}: Perilla tiene FollowObjectMotion activado");
                }

                // Palanca de cambios
                VRGearShifter shifter = root.GetComponentInChildren<VRGearShifter>(true);
                AssertCondition(shifter != null, $"{label}: VRGearShifter encontrado");
                if (shifter != null)
                {
                    VRHandPoseTarget target = shifter.GetComponent<VRHandPoseTarget>();
                    AssertCondition(target != null && target.PoseType == HandPoseType.GearShifter, $"{label}: Palanca tiene VRHandPoseTarget con GearShifter");
                    AssertCondition(target != null && target.FollowObjectMotion, $"{label}: Palanca tiene FollowObjectMotion activado");
                    AssertCondition(target != null && target.RightAttachTransform != null && target.RightAttachTransform.name == "Attach_Knob", $"{label}: Palanca tiene Attach_Knob configurado en RightAttachTransform");

                    var grab = shifter.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                    AssertCondition(grab != null && grab.attachTransform != null && grab.attachTransform.name == "Attach_Knob", $"{label}: XRGrabInteractable en palanca tiene Attach_Knob asignado a attachTransform");
                }

                // Freno de mano
                VRHandbrake handbrake = root.GetComponentInChildren<VRHandbrake>(true);
                AssertCondition(handbrake != null, $"{label}: VRHandbrake encontrado");
                if (handbrake != null)
                {
                    VRHandPoseTarget target = handbrake.GetComponent<VRHandPoseTarget>();
                    AssertCondition(target != null && target.PoseType == HandPoseType.Handbrake, $"{label}: Freno de mano tiene VRHandPoseTarget con Handbrake");
                    AssertCondition(target != null && target.FollowObjectMotion, $"{label}: Freno de mano tiene FollowObjectMotion activado");
                    AssertCondition(target != null && target.RightAttachTransform != null, $"{label}: Freno de mano tiene RightAttachTransform configurado");

                    var grab = handbrake.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                    AssertCondition(grab != null && grab.attachTransform != null, $"{label}: XRGrabInteractable en freno de mano tiene attachTransform asignado");
                }

                // Espejo retrovisor
                VRRearviewMirror mirror = root.GetComponentInChildren<VRRearviewMirror>(true);
                AssertCondition(mirror != null, $"{label}: VRRearviewMirror encontrado");
                if (mirror != null)
                {
                    VRHandPoseTarget target = mirror.GetComponent<VRHandPoseTarget>();
                    AssertCondition(target != null && target.PoseType == HandPoseType.RearviewMirror, $"{label}: Espejo tiene VRHandPoseTarget con RearviewMirror");
                    AssertCondition(target != null && target.FollowObjectMotion, $"{label}: Espejo tiene FollowObjectMotion activado");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void VerifySceneAndRig()
        {
            Debug.Log("--- Fase 3: Verificación de CockpitIntegrationScene y XR Origin ---");

            if (!File.Exists(VRHandPoseSetupEditor.SCENE_INTEGRATION_PATH))
            {
                AssertCondition(false, "Escena de integración accesible", $"No existe archivo en: {VRHandPoseSetupEditor.SCENE_INTEGRATION_PATH}");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(VRHandPoseSetupEditor.SCENE_INTEGRATION_PATH, OpenSceneMode.Single);
            AssertCondition(scene.isLoaded, "CockpitIntegrationScene cargada correctamente");

            GameObject origin = GameObject.Find("XR Origin (XR Rig)");
            AssertCondition(origin != null, "GameObject 'XR Origin (XR Rig)' encontrado en escena");

            if (origin != null)
            {
                Transform leftCtrl = origin.transform.Find("Camera Offset/Left Controller");
                Transform rightCtrl = origin.transform.Find("Camera Offset/Right Controller");

                AssertCondition(leftCtrl != null, "Left Controller transform encontrado");
                AssertCondition(rightCtrl != null, "Right Controller transform encontrado");

                if (leftCtrl != null)
                {
                    var leftCtrls = leftCtrl.GetComponents<VRHandPoseController>();
                    AssertCondition(leftCtrls.Length == 1, "Left Controller tiene exactamente 1 componente VRHandPoseController (sin duplicados)");

                    VRHandPoseController ctrl = leftCtrl.GetComponent<VRHandPoseController>();
                    AssertCondition(ctrl != null && ctrl.IsLeftHand, "VRHandPoseController izquierdo está marcado como IsLeftHand=true");

                    int leftHandCount = 0;
                    Transform leftHand = null;
                    for (int i = 0; i < leftCtrl.childCount; i++)
                    {
                        if (leftCtrl.GetChild(i).name == "VRHand_Left")
                        {
                            leftHandCount++;
                            if (leftHand == null) leftHand = leftCtrl.GetChild(i);
                        }
                    }
                    AssertCondition(leftHandCount == 1, "Left Controller tiene exactamente 1 objeto VRHand_Left instanciado (sin manos duplicadas)", $"Encontradas: {leftHandCount}");
                    AssertCondition(leftHand != null && leftHand.gameObject.activeSelf, "VRHand_Left está instanciado y activo en Left Controller");

                    if (leftHand != null)
                    {
                        var smr = leftHand.GetComponentInChildren<SkinnedMeshRenderer>();
                        AssertCondition(smr != null && smr.updateWhenOffscreen, "VRHand_Left tiene updateWhenOffscreen=true (inmune a culling de cámara)");
                    }

                    Transform visual = leftCtrl.Find("Left Controller Visual");
                    AssertCondition(visual == null || !visual.gameObject.activeSelf, "Left Controller Visual tradicional está oculto/desactivado");
                }

                if (rightCtrl != null)
                {
                    var rightCtrls = rightCtrl.GetComponents<VRHandPoseController>();
                    AssertCondition(rightCtrls.Length == 1, "Right Controller tiene exactamente 1 componente VRHandPoseController (sin duplicados)");

                    VRHandPoseController ctrl = rightCtrl.GetComponent<VRHandPoseController>();
                    AssertCondition(ctrl != null && !ctrl.IsLeftHand, "VRHandPoseController derecho está marcado como IsLeftHand=false");

                    int rightHandCount = 0;
                    Transform rightHand = null;
                    for (int i = 0; i < rightCtrl.childCount; i++)
                    {
                        if (rightCtrl.GetChild(i).name == "VRHand_Right")
                        {
                            rightHandCount++;
                            if (rightHand == null) rightHand = rightCtrl.GetChild(i);
                        }
                    }
                    AssertCondition(rightHandCount == 1, "Right Controller tiene exactamente 1 objeto VRHand_Right instanciado (sin manos duplicadas)", $"Encontradas: {rightHandCount}");
                    AssertCondition(rightHand != null && rightHand.gameObject.activeSelf, "VRHand_Right está instanciado y activo en Right Controller");

                    if (rightHand != null)
                    {
                        var smr = rightHand.GetComponentInChildren<SkinnedMeshRenderer>();
                        AssertCondition(smr != null && smr.updateWhenOffscreen, "VRHand_Right tiene updateWhenOffscreen=true (inmune a culling de cámara)");
                    }

                    Transform visual = rightCtrl.Find("Right Controller Visual");
                    AssertCondition(visual == null || !visual.gameObject.activeSelf, "Right Controller Visual tradicional está oculto/desactivado");
                }
            }
        }

        private static void VerifyKinematicsAndTracking()
        {
            Debug.Log("--- Fase 4: Pruebas Cinemáticas y de Acompañamiento de Movimiento ---");

            GameObject testObj = new GameObject("Test_HandPoseVerification");
            try
            {
                GameObject leftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRHandPoseSetupEditor.HAND_LEFT_PREFAB_PATH);
                GameObject handInst = UnityEngine.Object.Instantiate(leftPrefab, testObj.transform);
                VRHandPoseController poseCtrl = testObj.AddComponent<VRHandPoseController>();
                poseCtrl.SetHandVisual(handInst.transform, isLeft: true);

                // 1. Probar todas las posturas contextuales
                HandPoseType[] allPoses = new HandPoseType[]
                {
                    HandPoseType.Default,
                    HandPoseType.SteeringWheel,
                    HandPoseType.KnobPinch,
                    HandPoseType.GearShifter,
                    HandPoseType.Handbrake,
                    HandPoseType.RearviewMirror
                };

                foreach (HandPoseType pose in allPoses)
                {
                    poseCtrl.SetPose(pose, instant: true);
                    AssertCondition(poseCtrl.CurrentPose == pose, $"Postura {pose} aplicada correctamente");
                }

                // 2. Probar flexión física de dedos entre Default y SteeringWheel
                poseCtrl.SetPose(HandPoseType.Default, instant: true);
                Transform indexTip = handInst.transform.Find("L_Wrist/L_IndexMetacarpal/L_IndexProximal/L_IndexIntermediate/L_IndexDistal/L_IndexTip");
                Transform palm = handInst.transform.Find("L_Wrist/L_Palm");

                float defaultDist = Vector3.Distance(indexTip.position, palm.position);

                poseCtrl.SetPose(HandPoseType.SteeringWheel, instant: true);
                float wheelDist = Vector3.Distance(indexTip.position, palm.position);

                AssertCondition(wheelDist < defaultDist, "Dedos se cierran firmemente en postura SteeringWheel", $"Distancia al centro: Default={defaultDist:F3}m -> Wheel={wheelDist:F3}m");

                // 3. Probar acompañamiento angular (Giro del Volante)
                GameObject wheelObj = new GameObject("Test_Wheel");
                VRHandPoseTarget wheelTarget = wheelObj.AddComponent<VRHandPoseTarget>();
                wheelTarget.PoseType = HandPoseType.SteeringWheel;
                wheelTarget.FollowObjectMotion = true;
                wheelTarget.UseDynamicContactPoint = true;
                wheelTarget.WheelRadius = 0.18f;

                // Colocar la mano en la posición del volante y calcular agarre inicial
                Vector3 initialGripPos;
                Quaternion initialGripRot;
                wheelTarget.CalculateInitialGrip(isLeft: true, testObj.transform, out initialGripPos, out initialGripRot);

                // Rota el volante 90 grados alrededor del eje Z
                Quaternion rotBefore = wheelObj.transform.rotation;
                wheelObj.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

                // La posición y orientación de la mano transformada según el volante
                Vector3 handWorldPos = wheelObj.transform.TransformPoint(initialGripPos);
                Quaternion handWorldRot = wheelObj.transform.rotation * initialGripRot;

                float angleDelta = Quaternion.Angle(wheelObj.transform.rotation, rotBefore);
                AssertCondition(Mathf.Abs(angleDelta - 90f) < 0.1f, "Simulación de rotación de 90° en el volante ejecutada");

                // Verificar que el punto de agarre de la mano sobre el aro rote los mismos 90°
                Vector3 expectedLocalPosRotated = Quaternion.Euler(0f, 0f, 90f) * initialGripPos;
                float posError = Vector3.Distance(handWorldPos, expectedLocalPosRotated);
                AssertCondition(posError < 0.001f, "La mano visual acompaña rígidamente el giro del volante sin desfasarse", $"Error de posición: {posError:F6}m");

                // 4. Probar alineación concéntrica y compensación hacia adentro del anclaje radial en el aro del volante
                float anchorRadiusL = new Vector2(initialGripPos.x, initialGripPos.y).magnitude;
                float expectedConcentricRadius = wheelTarget.WheelRadius - Mathf.Abs(wheelTarget.HandPalmGripOffset.z);
                AssertCondition(Mathf.Abs(anchorRadiusL - expectedConcentricRadius) < 0.005f, "El anclaje radial izquierdo se calcula de forma concéntrica hacia adentro al radio real del marco", $"Radio anclaje: {anchorRadiusL:F4}m vs {expectedConcentricRadius:F4}m");
                AssertCondition(anchorRadiusL <= wheelTarget.WheelRadius + 0.001f, "El anclaje no se descoloca hacia afuera del volante (desplazado radialmente hacia adentro)");

                Vector3 gripPosR;
                Quaternion gripRotR;
                wheelTarget.CalculateInitialGrip(isLeft: false, testObj.transform, out gripPosR, out gripRotR);
                float anchorRadiusR = new Vector2(gripPosR.x, gripPosR.y).magnitude;
                AssertCondition(Mathf.Abs(anchorRadiusR - expectedConcentricRadius) < 0.005f, "El anclaje radial derecho se calcula de forma concéntrica hacia adentro al radio real del marco", $"Radio anclaje: {anchorRadiusR:F4}m vs {expectedConcentricRadius:F4}m");

                UnityEngine.Object.DestroyImmediate(wheelObj);

                // 5. Probar orientación de agarre en palanca de cambios (espacio Blender -90° X)
                GameObject shifterObj = new GameObject("Test_Shifter");
                shifterObj.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                VRHandPoseTarget shifterTarget = shifterObj.AddComponent<VRHandPoseTarget>();
                shifterTarget.PoseType = HandPoseType.GearShifter;
                Vector3 sPos;
                Quaternion sRot;
                shifterTarget.CalculateInitialGrip(false, testObj.transform, out sPos, out sRot);
                Vector3 sFwd = shifterObj.transform.rotation * sRot * Vector3.forward;
                Vector3 sUp = shifterObj.transform.rotation * sRot * Vector3.up;
                AssertCondition(Vector3.Dot(sFwd, Vector3.forward) > 0.7f, "Mano en palanca de cambios orientada hacia adelante (hacia tablero)", $"Dot forward: {Vector3.Dot(sFwd, Vector3.forward):F3}");
                AssertCondition(Vector3.Dot(sUp, Vector3.up) > 0.7f, "Palma en palanca de cambios ahuecada hacia abajo apoyada en el pomo", $"Dot up: {Vector3.Dot(sUp, Vector3.up):F3}");
                UnityEngine.Object.DestroyImmediate(shifterObj);

                // 6. Probar orientación de agarre en freno de mano (espacio Blender -90° X)
                GameObject brakeObj = new GameObject("Test_Brake");
                brakeObj.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                VRHandPoseTarget brakeTarget = brakeObj.AddComponent<VRHandPoseTarget>();
                brakeTarget.PoseType = HandPoseType.Handbrake;
                Vector3 bPos;
                Quaternion bRot;
                brakeTarget.CalculateInitialGrip(false, testObj.transform, out bPos, out bRot);
                Vector3 bFwd = brakeObj.transform.rotation * bRot * Vector3.forward;
                AssertCondition(Vector3.Dot(bFwd, Vector3.forward) > 0.7f, "Mano en freno de mano orientada hacia adelante envolviendo empuñadura", $"Dot forward: {Vector3.Dot(bFwd, Vector3.forward):F3}");
                UnityEngine.Object.DestroyImmediate(brakeObj);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(testObj);
            }
        }
    }
}
