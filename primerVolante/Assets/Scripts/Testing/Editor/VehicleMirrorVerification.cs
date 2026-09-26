using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación automatizada en Editor para el sistema completo de espejos vehiculares (Car07):
    /// Comprueba:
    /// 1. Limpieza de duplicados: exactamente una sola instancia de cada componente.
    /// 2. Posicionamiento exacto sobre la malla 3D original:
    ///    - Retrovisor interior: X: -0.075, Y: 1.31, Z: 0.00
    ///    - Espejo izquierdo:    X: -0.88,  Y: 1.03, Z: 0.22
    ///    - Espejo derecho:      X:  0.90,  Y: 1.03, Z: 0.22
    /// 3. Renderizado garantizado hacia atrás (rotación Y ~ 180°, RenderTexture creada en GPU, inversión UV).
    /// 4. Angle Culling relajado por defecto (no pantallas negras sin casco).
    /// 5. Espejos laterales motorizados y mini-panel de control.
    /// </summary>
    public static class VehicleMirrorVerification
    {
        [MenuItem("Tools/Primer Volante/Run Vehicle Mirror Verification")]
        [MenuItem("Primer Volante/Espejos/Ejecutar Verificación de Espejos")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL SISTEMA DE ESPEJOS (Car07)");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            // 1. Cargar Prefab Car07
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CockpitIntegrationSetup.PREFAB_CAR07_PATH);
            Assert(prefab != null, $"Car07.prefab debe existir en {CockpitIntegrationSetup.PREFAB_CAR07_PATH}", ref totalTests, ref passedTests);

            GameObject carInstance = UnityEngine.Object.Instantiate(prefab);
            carInstance.name = "Car07_MirrorVerification";

            try
            {
                // Asegurar configuración de espejos sobre la instancia
                VehicleMirrorSetupEditor.ConfigureMirrorsOnCar(carInstance);

                carInstance.BroadcastMessage("Awake", SendMessageOptions.DontRequireReceiver);
                carInstance.BroadcastMessage("Start", SendMessageOptions.DontRequireReceiver);

                // =========================================================================
                // 1. Verificación de No Duplicados (Exactamente 1 instancia de cada uno)
                // =========================================================================
                int countMount = CountChildrenNamed(carInstance.transform, "RearviewMirror_Mount");
                Assert(countMount == 1, $"Debe existir exactamente 1 RearviewMirror_Mount (encontrados: {countMount})", ref totalTests, ref passedTests);

                int countLeft = CountChildrenNamed(carInstance.transform, "SideMirror_Left");
                Assert(countLeft == 1, $"Debe existir exactamente 1 SideMirror_Left (encontrados: {countLeft})", ref totalTests, ref passedTests);

                int countRight = CountChildrenNamed(carInstance.transform, "SideMirror_Right");
                Assert(countRight == 1, $"Debe existir exactamente 1 SideMirror_Right (encontrados: {countRight})", ref totalTests, ref passedTests);

                int countPanel = CountChildrenNamed(carInstance.transform, "SideMirror_ControlPanel");
                Assert(countPanel == 1, $"Debe existir exactamente 1 SideMirror_ControlPanel (encontrados: {countPanel})", ref totalTests, ref passedTests);

                // =========================================================================
                // 2. Espejo Retrovisor Central (VRRearviewMirror) - Posición Exacta
                // =========================================================================
                Transform mountTr = carInstance.transform.Find("RearviewMirror_Mount");
                Assert(mountTr != null, "RearviewMirror_Mount debe existir en Car07", ref totalTests, ref passedTests);

                if (mountTr != null)
                {
                    Assert(Mathf.Abs(mountTr.localPosition.x - (-0.075f)) < 0.02f, $"RearviewMirror_Mount X debe ser aprox -0.075m (actual: {mountTr.localPosition.x})", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(mountTr.localPosition.y - 1.31f) < 0.03f, $"RearviewMirror_Mount Y debe ser aprox 1.31m (actual: {mountTr.localPosition.y})", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(mountTr.localPosition.z - 0.00f) < 0.03f, $"RearviewMirror_Mount Z debe ser aprox 0.00m (actual: {mountTr.localPosition.z})", ref totalTests, ref passedTests);
                }

                VRRearviewMirror rearview = carInstance.GetComponentInChildren<VRRearviewMirror>(true);
                Assert(rearview != null, "Componente VRRearviewMirror debe existir en Car07", ref totalTests, ref passedTests);

                if (rearview != null)
                {
                    Rigidbody rb = rearview.GetComponent<Rigidbody>();
                    Assert(rb != null && rb.isKinematic && !rb.useGravity, "VRRearviewMirror debe tener Rigidbody cinemático sin gravedad", ref totalTests, ref passedTests);

                    var grab = rearview.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                    Assert(grab != null, "VRRearviewMirror debe tener XRGrabInteractable", ref totalTests, ref passedTests);
                    if (grab != null)
                    {
                        Assert(!grab.trackPosition, "XRGrabInteractable.trackPosition debe ser false", ref totalTests, ref passedTests);
                        Assert(!grab.trackRotation, "XRGrabInteractable.trackRotation debe ser false", ref totalTests, ref passedTests);
                    }

                    // Prueba de límites angulares: Yaw [-30, 30], Pitch [-20, 20], Roll = 0
                    rearview.SetAngles(50f, 60f);
                    Assert(Mathf.Approximately(rearview.CurrentPitch, 20f), "Pitch debe estar clampeado a +20°", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(rearview.CurrentYaw, 30f), "Yaw debe estar clampeado a +30°", ref totalTests, ref passedTests);

                    rearview.SetAngles(-50f, -60f);
                    Assert(Mathf.Approximately(rearview.CurrentPitch, -20f), "Pitch debe estar clampeado a -20°", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(rearview.CurrentYaw, -30f), "Yaw debe estar clampeado a -30°", ref totalTests, ref passedTests);

                    rearview.ResetToCenter();
                    Assert(Mathf.Approximately(rearview.CurrentPitch, 0f), "ResetToCenter debe devolver Pitch a 0°", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(rearview.CurrentYaw, 0f), "ResetToCenter debe devolver Yaw a 0°", ref totalTests, ref passedTests);

                    // Vidrio reflectante del retrovisor central
                    Transform rvGlass = carInstance.transform.Find("RearviewMirror_Mount/RearviewMirror_Casing/Mirror_Glass");
                    Assert(rvGlass != null, "Mirror_Glass debe existir en el retrovisor central", ref totalTests, ref passedTests);
                    if (rvGlass != null)
                    {
                        Assert(rvGlass.localRotation == Quaternion.identity, "Mirror_Glass central debe tener localRotation == Quaternion.identity (cara visible hacia -Z)", ref totalTests, ref passedTests);
                        Assert(Mathf.Abs(rvGlass.localPosition.z - (-0.0135f)) < 0.005f, $"Mirror_Glass central localPosition.z debe ser aprox -0.0135m (actual: {rvGlass.localPosition.z})", ref totalTests, ref passedTests);
                    }

                    // Cámara del retrovisor central
                    Transform rvCam = carInstance.transform.Find("RearviewMirror_Mount/RearviewMirror_Casing/RearviewMirror_Camera");
                    Assert(rvCam != null, "RearviewMirror_Camera debe existir", ref totalTests, ref passedTests);
                    if (rvCam != null)
                    {
                        Assert(Mathf.Abs(rvCam.localPosition.z - (-0.03f)) < 0.01f, $"RearviewMirror_Camera localPosition.z debe ser aprox -0.03m por delante del vidrio (actual: {rvCam.localPosition.z})", ref totalTests, ref passedTests);
                    }
                }

                // =========================================================================
                // 3. Cámaras de Espejos (VehicleMirrorCamera), RenderTextures y Orientación hacia atrás
                // =========================================================================
                VehicleMirrorCamera[] mirrorCameras = carInstance.GetComponentsInChildren<VehicleMirrorCamera>(true);
                Assert(mirrorCameras.Length == 3, $"Deben existir exactamente 3 VehicleMirrorCamera (encontradas {mirrorCameras.Length})", ref totalTests, ref passedTests);

                foreach (var mc in mirrorCameras)
                {
                    Camera cam = mc.MirrorCamera;
                    Assert(cam != null, $"MirrorCamera en {mc.name} no debe ser null", ref totalTests, ref passedTests);

                    if (cam != null)
                    {
                        Assert(cam.targetTexture != null, $"targetTexture en {mc.name} debe estar asignada", ref totalTests, ref passedTests);
                        Assert(cam.targetTexture.IsCreated(), $"RenderTexture en {mc.name} debe estar creada en GPU", ref totalTests, ref passedTests);
                        Assert(cam.farClipPlane >= 100f && cam.farClipPlane <= 125f, $"farClipPlane en {mc.name} debe estar acotado entre 100 y 120m (actual: {cam.farClipPlane})", ref totalTests, ref passedTests);

                        AudioListener listener = mc.GetComponentInChildren<AudioListener>(true);
                        Assert(listener == null, $"La cámara de espejo {mc.name} no debe tener ningún AudioListener", ref totalTests, ref passedTests);

                        // Orientación de la cámara: debe mirar hacia atrás (-Z del auto, rotación Y aprox 180°)
                        float angleToBack = Vector3.Angle(mc.transform.forward, -carInstance.transform.forward);
                        Assert(angleToBack < 25f, $"Cámara {mc.name} debe apuntar hacia atrás del vehículo (ángulo actual respecto a -Z: {angleToBack:F1}°)", ref totalTests, ref passedTests);
                    }

                    // Angle Culling relajado por defecto
                    Assert(!mc.EnableAngleCulling, $"Angle Culling en {mc.name} debe estar desactivado por defecto para evitar pantallas negras", ref totalTests, ref passedTests);

                    // Comprobar inversión óptica horizontal en el material (Tiling X = -1, Offset X = 1) y doble cara (Cull Off)
                    Material mat = mc.GetMirrorMaterial();
                    Assert(mat != null, $"Material de espejo en {mc.name} debe existir", ref totalTests, ref passedTests);
                    if (mat != null)
                    {
                        mc.ApplyOpticalInversion();
                        Assert(Mathf.Approximately(mat.mainTextureScale.x, -1f), $"Material {mat.name} mainTextureScale.x debe ser -1 (actual: {mat.mainTextureScale.x})", ref totalTests, ref passedTests);
                        Assert(Mathf.Approximately(mat.mainTextureOffset.x, 1f), $"Material {mat.name} mainTextureOffset.x debe ser 1 (actual: {mat.mainTextureOffset.x})", ref totalTests, ref passedTests);

                        if (mat.HasProperty("_Cull"))
                        {
                            Assert(Mathf.Approximately(mat.GetFloat("_Cull"), 0f), $"Material {mat.name} _Cull debe ser 0 (Off / Doble Cara)", ref totalTests, ref passedTests);
                        }
                    }
                }

                // =========================================================================
                // 4. Espejos Laterales Motorizados (VRSideMirror) - Posicionamiento Exacto
                // =========================================================================
                Transform sideLeftTr = carInstance.transform.Find("SideMirror_Left");
                Assert(sideLeftTr != null, "SideMirror_Left debe existir", ref totalTests, ref passedTests);
                if (sideLeftTr != null)
                {
                    Assert(Mathf.Abs(sideLeftTr.localPosition.x - (-0.88f)) < 0.03f, $"SideMirror_Left X debe ser aprox -0.88m (actual: {sideLeftTr.localPosition.x})", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(sideLeftTr.localPosition.y - 1.03f) < 0.03f, $"SideMirror_Left Y debe ser aprox 1.03m (actual: {sideLeftTr.localPosition.y})", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(sideLeftTr.localPosition.z - 0.22f) < 0.03f, $"SideMirror_Left Z debe ser aprox 0.22m (actual: {sideLeftTr.localPosition.z})", ref totalTests, ref passedTests);
                }

                Transform sideRightTr = carInstance.transform.Find("SideMirror_Right");
                Assert(sideRightTr != null, "SideMirror_Right debe existir", ref totalTests, ref passedTests);
                if (sideRightTr != null)
                {
                    Assert(Mathf.Abs(sideRightTr.localPosition.x - 0.90f) < 0.03f, $"SideMirror_Right X debe ser aprox 0.90m (actual: {sideRightTr.localPosition.x})", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(sideRightTr.localPosition.y - 1.03f) < 0.03f, $"SideMirror_Right Y debe ser aprox 1.03m (actual: {sideRightTr.localPosition.y})", ref totalTests, ref passedTests);
                    Assert(Mathf.Abs(sideRightTr.localPosition.z - 0.22f) < 0.03f, $"SideMirror_Right Z debe ser aprox 0.22m (actual: {sideRightTr.localPosition.z})", ref totalTests, ref passedTests);
                }

                // Vidrios y pivotes de espejos laterales
                Transform leftGlass = carInstance.transform.Find("SideMirror_Left/Glass_Pivot/Mirror_Glass");
                Assert(leftGlass != null, "Mirror_Glass debe existir en SideMirror_Left", ref totalTests, ref passedTests);
                if (leftGlass != null)
                {
                    Assert(leftGlass.localRotation == Quaternion.identity, "Mirror_Glass izquierdo debe tener localRotation == Quaternion.identity (cara visible hacia -Z)", ref totalTests, ref passedTests);
                }

                Transform rightGlass = carInstance.transform.Find("SideMirror_Right/Glass_Pivot/Mirror_Glass");
                Assert(rightGlass != null, "Mirror_Glass debe existir en SideMirror_Right", ref totalTests, ref passedTests);
                if (rightGlass != null)
                {
                    Assert(rightGlass.localRotation == Quaternion.identity, "Mirror_Glass derecho debe tener localRotation == Quaternion.identity (cara visible hacia -Z)", ref totalTests, ref passedTests);
                }

                Transform leftPivot = carInstance.transform.Find("SideMirror_Left/Glass_Pivot");
                if (leftPivot != null)
                {
                    float yRot = leftPivot.localEulerAngles.y;
                    if (yRot > 180f) yRot -= 360f;
                    Assert(Mathf.Abs(yRot - (-22f)) < 1.5f, $"Glass_Pivot izquierdo debe converger hacia conductor con Y aprox -22° (actual: {yRot:F1}°)", ref totalTests, ref passedTests);
                }

                Transform rightPivot = carInstance.transform.Find("SideMirror_Right/Glass_Pivot");
                if (rightPivot != null)
                {
                    float yRot = rightPivot.localEulerAngles.y;
                    if (yRot > 180f) yRot -= 360f;
                    Assert(Mathf.Abs(yRot - 22f) < 1.5f, $"Glass_Pivot derecho debe converger hacia conductor con Y aprox 22° (actual: {yRot:F1}°)", ref totalTests, ref passedTests);
                }

                // Cámaras de espejos laterales
                Transform leftCamTr = carInstance.transform.Find("SideMirror_Left/Glass_Pivot/SideMirror_Camera_Left");
                Assert(leftCamTr != null, "SideMirror_Camera_Left debe existir", ref totalTests, ref passedTests);
                if (leftCamTr != null)
                {
                    Assert(Mathf.Abs(leftCamTr.localPosition.z - (-0.02f)) < 0.01f, $"SideMirror_Camera_Left localPosition.z debe ser aprox -0.02m (actual: {leftCamTr.localPosition.z})", ref totalTests, ref passedTests);
                }

                Transform rightCamTr = carInstance.transform.Find("SideMirror_Right/Glass_Pivot/SideMirror_Camera_Right");
                Assert(rightCamTr != null, "SideMirror_Camera_Right debe existir", ref totalTests, ref passedTests);
                if (rightCamTr != null)
                {
                    Assert(Mathf.Abs(rightCamTr.localPosition.z - (-0.02f)) < 0.01f, $"SideMirror_Camera_Right localPosition.z debe ser aprox -0.02m (actual: {rightCamTr.localPosition.z})", ref totalTests, ref passedTests);
                }

                VRSideMirror leftMirror = sideLeftTr != null ? sideLeftTr.GetComponent<VRSideMirror>() : null;
                VRSideMirror rightMirror = sideRightTr != null ? sideRightTr.GetComponent<VRSideMirror>() : null;

                Assert(leftMirror != null && leftMirror.Side == VRSideMirror.MirrorSide.Left, "VRSideMirror izquierdo debe estar configurado como Left", ref totalTests, ref passedTests);
                Assert(rightMirror != null && rightMirror.Side == VRSideMirror.MirrorSide.Right, "VRSideMirror derecho debe estar configurado como Right", ref totalTests, ref passedTests);

                if (leftMirror != null)
                {
                    Assert(Mathf.Approximately(leftMirror.MaxYaw, 20f), "VRSideMirror MaxYaw debe ser 20°", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(leftMirror.MaxPitch, 15f), "VRSideMirror MaxPitch debe ser 15°", ref totalTests, ref passedTests);

                    leftMirror.SetAngles(30f, 40f);
                    Assert(Mathf.Approximately(leftMirror.CurrentPitch, 15f), "VRSideMirror Pitch debe clampear a 15°", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(leftMirror.CurrentYaw, 20f), "VRSideMirror Yaw debe clampear a 20°", ref totalTests, ref passedTests);

                    leftMirror.ResetToCenter();
                    Assert(Mathf.Approximately(leftMirror.CurrentPitch, 0f), "VRSideMirror ResetToCenter debe poner Pitch en 0°", ref totalTests, ref passedTests);
                    Assert(Mathf.Approximately(leftMirror.CurrentYaw, 0f), "VRSideMirror ResetToCenter debe poner Yaw en 0°", ref totalTests, ref passedTests);

                    leftMirror.AdjustPulse(new Vector2(1f, 0f), 2f);
                    Assert(Mathf.Approximately(leftMirror.CurrentYaw, 2f), "VRSideMirror AdjustPulse X debe avanzar 2°", ref totalTests, ref passedTests);
                    leftMirror.ResetToCenter();
                }

                // =========================================================================
                // 5. Mini-Panel de Control Eléctrico en Puerta (VRSideMirrorControlPanel)
                // =========================================================================
                VRSideMirrorControlPanel panel = carInstance.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                Assert(panel != null, "VRSideMirrorControlPanel debe existir en Car07", ref totalTests, ref passedTests);

                if (panel != null)
                {
                    Rigidbody panelRb = panel.GetComponent<Rigidbody>();
                    Assert(panelRb != null && panelRb.isKinematic && !panelRb.useGravity, "SideMirror_ControlPanel debe tener Rigidbody cinemático", ref totalTests, ref passedTests);

                    Assert(panel.transform.localPosition.x <= -0.45f, "El panel debe estar situado en el lateral izquierdo del conductor (X <= -0.45m)", ref totalTests, ref passedTests);
                    Assert(panel.transform.localPosition.y >= 0.55f && panel.transform.localPosition.y <= 0.85f, "El panel debe estar a la altura del apoyabrazos (Y entre 0.55m y 0.85m)", ref totalTests, ref passedTests);

                    // Comprobar bloqueo en OFF
                    panel.SetSelection(VRSideMirrorControlPanel.MirrorSelection.Off);
                    Assert(panel.CurrentSelection == VRSideMirrorControlPanel.MirrorSelection.Off, "El panel debe estar en OFF", ref totalTests, ref passedTests);

                    if (leftMirror != null && rightMirror != null)
                    {
                        leftMirror.ResetToCenter();
                        rightMirror.ResetToCenter();

                        panel.AdjustPulse(new Vector2(1f, 0f), 5f);
                        Assert(Mathf.Approximately(leftMirror.CurrentYaw, 0f), "En posición OFF, el espejo izquierdo no debe moverse", ref totalTests, ref passedTests);
                        Assert(Mathf.Approximately(rightMirror.CurrentYaw, 0f), "En posición OFF, el espejo derecho no debe moverse", ref totalTests, ref passedTests);

                        panel.SetSelection(VRSideMirrorControlPanel.MirrorSelection.Left);
                        panel.AdjustPulse(new Vector2(1f, 0f), 3f);
                        Assert(Mathf.Approximately(leftMirror.CurrentYaw, 3f), "Con Left seleccionado, el espejo izquierdo debe moverse 3°", ref totalTests, ref passedTests);
                        Assert(Mathf.Approximately(rightMirror.CurrentYaw, 0f), "Con Left seleccionado, el espejo derecho debe permanecer en 0°", ref totalTests, ref passedTests);

                        panel.SetSelection(VRSideMirrorControlPanel.MirrorSelection.Right);
                        panel.AdjustPulse(new Vector2(0f, 1f), 4f);
                        Assert(Mathf.Approximately(rightMirror.CurrentPitch, 4f), "Con Right seleccionado, el espejo derecho debe moverse 4° en Pitch", ref totalTests, ref passedTests);
                        Assert(Mathf.Approximately(leftMirror.CurrentPitch, 0f), "Con Right seleccionado, el espejo izquierdo no debe alterar su Pitch", ref totalTests, ref passedTests);

                        panel.CycleSelection(-1);
                        Assert(panel.CurrentSelection == VRSideMirrorControlPanel.MirrorSelection.Off, "CycleSelection(-1) desde Right debe conmutar a Off", ref totalTests, ref passedTests);
                        panel.CycleSelection(-1);
                        Assert(panel.CurrentSelection == VRSideMirrorControlPanel.MirrorSelection.Left, "CycleSelection(-1) desde Off debe conmutar a Left", ref totalTests, ref passedTests);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(carInstance);
            }

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO DE VERIFICACIÓN: {passedTests}/{totalTests} pruebas superadas.");
            Debug.Log("=========================================================");

            if (passedTests == totalTests)
            {
                Debug.Log("🎉 ¡TODAS LAS PRUEBAS DE ESPEJOS PASARON SATISFACTORIAMENTE!");
            }
            else
            {
                Debug.LogError($"❌ {totalTests - passedTests} pruebas fallaron. Revisa los mensajes anteriores.");
            }
        }

        private static int CountChildrenNamed(Transform root, string name)
        {
            int count = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t != root && t.name == name)
                {
                    count++;
                }
            }
            return count;
        }

        private static void Assert(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {message}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {message}");
            }
        }
    }
}
