using System.Collections.Generic;
using System.Reflection;
using PrimerVolante.VR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la compensación de la pose de la cabeza de <see cref="DriverSeatFollower"/> (rama VR):
    /// tras <see cref="DriverSeatFollower.RecenterHeadPose"/> la cámara queda horizontalmente en el punto de vista del asiento
    /// y mirando hacia el frente del asiento, una recaptura converge sin acumular error, el modo escritorio no cambia,
    /// el tracking real de la cabeza no se anula, y la compensación sobrevive al cambio de asiento.
    /// La rama VR se fuerza con <see cref="DriverSeatFollower.SetVRActiveOverrideForTests"/>.
    /// </summary>
    public static class DriverSeatHeadCompensationVerification
    {
        private const float POSITION_TOLERANCE = 0.001f; // 1 mm
        private const float ANGLE_TOLERANCE = 0.5f;      // grados
        private const float CAMERA_OFFSET_Y = 1.3f;

        private static int s_Total;
        private static int s_Passed;

        private sealed class Rig
        {
            public Transform Seat;
            public Transform Origin;
            public Transform CameraOffset;
            public Transform Camera;
            public DriverSeatFollower Follower;
        }

        [MenuItem("Tools/Primer Volante/Run Driver Seat Head Compensation Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE LA COMPENSACION DE POSE DE CABEZA (DriverSeatFollower)");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;
            var created = new List<GameObject>();
            try
            {
                HeadPoseCompensationSession.ResetForTests();
                VerifyCaptureAlignsCameraToSeat(created);
                VerifyRecaptureConverges(created);
                VerifyDesktopModeUnchanged(created);
                VerifyHeadTrackingStillMovesCamera(created);
                VerifySeatChangeKeepsCompensation(created);
                VerifyTiltedSeat(created);
                VerifyDegenerateAndInvalidCases(created);
                VerifyPassthroughResumeRestoresOffset(created);
                VerifySystemMenuHoldsHeadPose(created);
                VerifySystemMenuFollowsLegacyHeadPose(created);
            }
            finally
            {
                HeadPoseCompensationSession.ResetForTests();
                DriverSeatFollower.SetVRActiveOverrideForTests(null);
                DriverSeatFollower.SetTrackingOriginModeOverrideForTests(null);
                DriverSeatFollower.SetAppFocusedOverrideForTests(null);
                DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(null);
                foreach (GameObject go in created)
                {
                    if (go != null) Object.DestroyImmediate(go);
                }
            }

            Debug.Log("=========================================================");
            if (s_Passed == s_Total) Debug.Log($"RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            else Debug.LogError($"[FAIL] RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas");
            Debug.Log("=========================================================");
        }

        // ------------------------------------------------------------------ Escenarios

        private static void VerifyCaptureAlignsCameraToSeat(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(12.3f, 0.7f, -5.1f), Quaternion.Euler(0f, 137f, 0f));
            rig.Follower.CalibrationOffset = new Vector3(0.1f, 0.05f, -0.08f);
            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 5f));
            DriverSeatFollower.SetVRActiveOverrideForTests(true);

            rig.Follower.SnapToSeat();
            Check(HorizontalDistance(rig.Camera.position, ViewPoint(rig)) > 0.3f,
                "Sin captura, la cámara está desplazada del punto de vista del asiento (el problema que se corrige)");

            Check(rig.Follower.RecenterHeadPose(), "RecenterHeadPose devuelve true con una cabeza válida");
            AssertAlignedToSeat(rig, "Tras capturar");
        }

        private static void VerifyRecaptureConverges(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(-3f, 1.1f, 40f), Quaternion.Euler(0f, -63f, 0f));
            rig.Follower.CalibrationOffset = new Vector3(-0.07f, 0f, 0.1f);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);

            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 5f));
            rig.Follower.RecenterHeadPose();
            AssertAlignedToSeat(rig, "Primera captura");

            // Con la compensación anterior ya aplicada, el runtime recentra y la cabeza queda en otra pose local.
            SetCameraPose(rig, new Vector3(-0.35f, -0.05f, 0.15f), Quaternion.Euler(-5f, -40f, 0f));
            rig.Follower.RecenterHeadPose();
            AssertAlignedToSeat(rig, "Segunda captura con otra pose (reemplaza, no acumula)");

            rig.Follower.RecenterHeadPose();
            AssertAlignedToSeat(rig, "Capturar de nuevo con la misma pose es idempotente");

            SetCameraPose(rig, new Vector3(1.2f, 0f, -0.9f), Quaternion.Euler(0f, 175f, 0f));
            rig.Follower.RecenterHeadPose();
            AssertAlignedToSeat(rig, "Tercera captura con yaw cercano a 180 grados");
        }

        private static void VerifyDesktopModeUnchanged(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(5f, 0.2f, 7f), Quaternion.Euler(0f, 30f, 0f));
            rig.Follower.CalibrationOffset = new Vector3(0.1f, 0.05f, 0.05f);
            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));

            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            rig.Follower.RecenterHeadPose(); // hay una compensación guardada...

            DriverSeatFollower.SetVRActiveOverrideForTests(false); // ...pero en escritorio no se aplica
            rig.Follower.SnapToSeat();
            Vector3 expectedPosition = rig.Seat.TransformPoint(rig.Follower.CalibrationOffset);
            Check(Vector3.Distance(rig.Origin.position, expectedPosition) < 1e-5f,
                "Escritorio: la posición del rig es exactamente asiento + calibración");
            Check(Quaternion.Angle(rig.Origin.rotation, rig.Seat.rotation) < 1e-3f,
                "Escritorio: la rotación del rig es exactamente la del asiento");
        }

        private static void VerifyHeadTrackingStillMovesCamera(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(2f, 0f, 2f), Quaternion.Euler(0f, 210f, 0f));
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            rig.Follower.RecenterHeadPose();
            Vector3 viewPoint = ViewPoint(rig);

            // El usuario se inclina 0.1 m a un costado y 0.2 m adelante (en el espacio del rig) y gira 20 grados.
            Vector3 lean = new Vector3(0.1f, 0f, 0.2f);
            SetCameraPose(rig, new Vector3(0.3f, 0.1f, 0.6f), Quaternion.Euler(10f, 90f, 0f));
            rig.Follower.SnapToSeat();

            Vector3 expected = viewPoint + rig.Origin.rotation * lean + Vector3.up * (rig.Camera.position.y - viewPoint.y);
            Check(HorizontalDistance(rig.Camera.position, expected) < POSITION_TOLERANCE,
                $"Mover la cabeza tras la captura mueve la cámara (error {HorizontalDistance(rig.Camera.position, expected) * 1000f:F2} mm)");

            float yawFromSeat = Vector3.SignedAngle(rig.Seat.forward, HorizontalForward(rig.Camera), Vector3.up);
            Check(Mathf.Abs(yawFromSeat - 20f) < ANGLE_TOLERANCE,
                $"Girar la cabeza tras la captura gira la vista (esperado 20, obtenido {yawFromSeat:F2})");

            // Sin recaptura, el rig sigue al auto: el punto de vista acompaña al asiento.
            rig.Seat.SetPositionAndRotation(new Vector3(8f, 0.3f, -4f), Quaternion.Euler(0f, 15f, 0f));
            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            rig.Follower.SnapToSeat();
            AssertAlignedToSeat(rig, "Al moverse y girar el asiento, la cámara lo acompaña sin recapturar");
        }

        private static void VerifySeatChangeKeepsCompensation(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(0f, 0f, 0f), Quaternion.identity);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            SetCameraPose(rig, new Vector3(0.25f, 0.1f, 0.3f), Quaternion.Euler(0f, 55f, 0f));
            rig.Follower.RecenterHeadPose();

            var otherSeatObject = new GameObject("OtherDriverSeat");
            created.Add(otherSeatObject);
            otherSeatObject.transform.SetPositionAndRotation(new Vector3(-20f, 0.5f, 33f), Quaternion.Euler(0f, 250f, 0f));
            rig.Follower.SetDriverSeat(otherSeatObject.transform);

            Check(HorizontalDistance(rig.Camera.position, otherSeatObject.transform.TransformPoint(rig.Follower.CalibrationOffset)) < POSITION_TOLERANCE &&
                  Mathf.Abs(Vector3.SignedAngle(otherSeatObject.transform.forward, HorizontalForward(rig.Camera), Vector3.up)) < ANGLE_TOLERANCE,
                "SetDriverSeat conserva la compensación: con el asiento nuevo la cámara queda alineada sin recapturar");

            // El delta mundial de CalibrationOffsetChanged no cambia por la compensación.
            Vector3 reportedDelta = Vector3.zero;
            void OnChanged(Vector3 delta) => reportedDelta = delta;
            rig.Follower.CalibrationOffsetChanged += OnChanged;
            Vector3 before = rig.Camera.position;
            rig.Follower.SetCalibrationAxis(2, 0.1f);
            rig.Follower.SnapToSeat();
            rig.Follower.CalibrationOffsetChanged -= OnChanged;
            Vector3 expectedDelta = otherSeatObject.transform.TransformPoint(new Vector3(0f, 0f, 0.1f)) - otherSeatObject.transform.position;
            Check(Vector3.Distance(reportedDelta, expectedDelta) < 1e-5f &&
                  Vector3.Distance(rig.Camera.position - before, expectedDelta) < POSITION_TOLERANCE,
                "CalibrationOffsetChanged reporta el delta mundial y la cámara se mueve exactamente ese delta");
        }

        private static void VerifyTiltedSeat(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(4f, 2f, -9f), Quaternion.Euler(5f, 137f, 3f));
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            rig.Follower.RecenterHeadPose();

            // Con el asiento inclinado, la cabeza queda sobre el punto de vista a lo largo de la vertical del rig.
            Vector3 expected = ViewPoint(rig) + rig.Origin.rotation * new Vector3(0f, rig.Origin.InverseTransformPoint(rig.Camera.position).y, 0f);
            Check(Vector3.Distance(rig.Camera.position, expected) < POSITION_TOLERANCE,
                "Asiento inclinado: la cámara cae sobre el punto de vista del asiento a lo largo de la vertical del rig");
        }

        private static void VerifyDegenerateAndInvalidCases(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(1f, 1f, 1f), Quaternion.Euler(0f, 90f, 0f));
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            rig.Follower.RecenterHeadPose();
            Vector3 posBefore = rig.Origin.position;
            Quaternion rotBefore = rig.Origin.rotation;

            SetCameraPose(rig, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(90f, 70f, 0f)); // mirando recto hacia abajo
            bool captured = rig.Follower.RecenterHeadPose();
            Check(!captured && Vector3.Distance(rig.Origin.position, posBefore) < 1e-6f && Quaternion.Angle(rig.Origin.rotation, rotBefore) < 1e-4f,
                "Mirando recto hacia abajo (yaw indeterminado) no captura y no cambia la compensación");

            // La sesión (HeadPoseCompensationSession) ya guarda la captura de arriba y un follower nuevo la reutilizaría: se la descarta
            // para verificar solo el caso sin sesión previa.
            HeadPoseCompensationSession.ResetForTests();
            // Update con el visor "activo" pero sin dispositivo de cabeza válido no debe capturar (pose identidad / sin tracking).
            Rig second = BuildRig(created, new Vector3(0f, 0f, 0f), Quaternion.identity);
            SetCameraPose(second, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            typeof(DriverSeatFollower).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(second.Follower, null);
            Check(HorizontalDistance(second.Camera.position, ViewPoint(second)) > 0.3f,
                "Update sin un HMD trackeando (dispositivo inválido) no captura la compensación");

            var noCameraObject = new GameObject("NoCameraRig");
            created.Add(noCameraObject);
            noCameraObject.AddComponent<XROrigin>();
            var noCameraFollower = noCameraObject.AddComponent<DriverSeatFollower>();
            Check(!noCameraFollower.RecenterHeadPose(), "Sin cámara en el XROrigin, RecenterHeadPose devuelve false sin lanzar");
        }

        // Al volver de passthrough en el Quest el display figura detenido un frame: corre la rama de escritorio, que sube el
        // Camera Offset a la altura de los ojos. De vuelta en VR el offset tiene que volver a lo que fija el modo de tracking.
        private static void VerifyPassthroughResumeRestoresOffset(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(8f, 0.4f, -2f), Quaternion.Euler(0f, 95f, 0f));
            SetCameraPose(rig, new Vector3(0.05f, 1.05f, 0.25f), Quaternion.Euler(0f, 15f, 0f));

            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(UnityEngine.XR.TrackingOriginModeFlags.Floor);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            rig.Follower.RecenterHeadPose();
            rig.Follower.SnapToSeat();
            Check(Mathf.Abs(rig.CameraOffset.localPosition.y) < 1e-5f, "VR Floor: la altura del Camera Offset es 0");
            Vector3 cameraBefore = rig.Camera.position;

            DriverSeatFollower.SetVRActiveOverrideForTests(false); // frame de "display detenido" al reanudar
            rig.Follower.SnapToSeat();
            rig.CameraOffset.localRotation = Quaternion.Euler(0f, 30f, 0f); // ej. vista de escritorio girada
            Check(rig.CameraOffset.localPosition.y > 0.5f, "Escritorio: el Camera Offset sube a la altura de los ojos (reproduce el bug)");

            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            rig.Follower.SnapToSeat();
            Check(rig.CameraOffset.localPosition.sqrMagnitude < 1e-8f, "VR Floor tras el frame de escritorio: el Camera Offset vuelve a (0, 0, 0)");
            Check(Quaternion.Angle(rig.CameraOffset.localRotation, Quaternion.identity) < 1e-3f, "VR tras el frame de escritorio: el Camera Offset vuelve a no tener rotación");
            Check(Vector3.Distance(rig.Camera.position, cameraBefore) < POSITION_TOLERANCE,
                "VR tras el frame de escritorio: la cámara vuelve exactamente a donde estaba (altura incluida)");
            AssertAlignedToSeat(rig, "VR tras el frame de escritorio");

            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(UnityEngine.XR.TrackingOriginModeFlags.Device);
            rig.Follower.SnapToSeat();
            Check(Mathf.Abs(rig.CameraOffset.localPosition.y - rig.Origin.GetComponent<XROrigin>().CameraYOffset) < 1e-5f,
                "VR Device: la altura del Camera Offset es CameraYOffset");

            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(UnityEngine.XR.TrackingOriginModeFlags.Unknown);
            rig.CameraOffset.localPosition = new Vector3(0.2f, 0.42f, -0.1f);
            rig.Follower.SnapToSeat();
            Check(Vector3.Distance(rig.CameraOffset.localPosition, new Vector3(0f, 0.42f, 0f)) < 1e-5f,
                "VR con modo desconocido: se corrigen X/Z pero no se toca la altura");

            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(null);
        }

        // Con el menú del botón Meta la app pierde el foco y la pose local de la cámara cae a (0, 0, 0): la vista de fondo
        // quedaba al ras del asiento. El Camera Offset tiene que sostener la última pose y soltarla al recuperar el foco.
        private static void VerifySystemMenuHoldsHeadPose(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(-6f, 0.3f, 11f), Quaternion.Euler(0f, -40f, 0f));
            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(UnityEngine.XR.TrackingOriginModeFlags.Floor);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetAppFocusedOverrideForTests(true);
            // Sin pose XR heredada válida: se congela la última pose conocida.
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(Pose.identity, valid: false);

            Quaternion headRotation = Quaternion.Euler(8f, 20f, 0f);
            SetCameraPose(rig, new Vector3(0.04f, 1.08f, 0.2f), headRotation);
            rig.Follower.RecenterHeadPose();
            rig.Follower.SnapToSeat();
            Vector3 cameraBefore = rig.Camera.position;
            Quaternion cameraRotationBefore = rig.Camera.rotation;

            // Se abre el menú del sistema: sin foco, la pose cae a cero (posición y rotación).
            DriverSeatFollower.SetAppFocusedOverrideForTests(false);
            SetCameraPose(rig, Vector3.zero, Quaternion.identity);
            Check(Vector3.Distance(rig.Camera.position, cameraBefore) < POSITION_TOLERANCE,
                "Menú del sistema: la cámara se mantiene donde estaba aunque la pose caiga a cero");
            Check(Quaternion.Angle(rig.Camera.rotation, cameraRotationBefore) < ANGLE_TOLERANCE,
                "Menú del sistema: la cámara mantiene la orientación previa");

            rig.Follower.SnapToSeat();
            Check(Vector3.Distance(rig.Camera.position, cameraBefore) < POSITION_TOLERANCE,
                "Menú del sistema: frames siguientes no acumulan desplazamiento");

            // Sin foco pero con rotación todavía viva: se sostiene la posición y la rotación sigue a la cabeza.
            SetCameraPose(rig, Vector3.zero, Quaternion.Euler(0f, -30f, 0f));
            Check(Vector3.Distance(rig.Camera.position, cameraBefore) < POSITION_TOLERANCE,
                "Menú del sistema con rotación viva: la posición se sostiene");
            Check(Quaternion.Angle(rig.CameraOffset.localRotation, Quaternion.identity) < 1e-3f,
                "Menú del sistema con rotación viva: el Camera Offset no agrega rotación");

            // Vuelve el foco y la pose real: el Camera Offset vuelve a (0, 0, 0) sin rotación.
            DriverSeatFollower.SetAppFocusedOverrideForTests(true);
            SetCameraPose(rig, new Vector3(0.04f, 1.08f, 0.2f), headRotation);
            Check(rig.CameraOffset.localPosition.sqrMagnitude < 1e-8f
                    && Quaternion.Angle(rig.CameraOffset.localRotation, Quaternion.identity) < 1e-3f,
                "Al recuperar el foco el Camera Offset vuelve a (0, 0, 0) sin rotación");
            Check(Vector3.Distance(rig.Camera.position, cameraBefore) < POSITION_TOLERANCE,
                "Al recuperar el foco la cámara sigue en el mismo lugar");

            // Con foco, una pose en cero no se sostiene (no es el caso del menú del sistema).
            SetCameraPose(rig, Vector3.zero, Quaternion.identity);
            Check(rig.CameraOffset.localPosition.sqrMagnitude < 1e-8f, "Con foco, una pose en cero no activa el sostén");

            DriverSeatFollower.SetAppFocusedOverrideForTests(null);
            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(null);
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(null);
        }

        // Con pose XR heredada viva durante el menú del sistema, el fondo tiene que seguir la cabeza (no quedar congelado),
        // tanto en el Update como justo antes de dibujar.
        private static void VerifySystemMenuFollowsLegacyHeadPose(List<GameObject> created)
        {
            Rig rig = BuildRig(created, new Vector3(3f, 0.5f, -9f), Quaternion.Euler(0f, 160f, 0f));
            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(UnityEngine.XR.TrackingOriginModeFlags.Floor);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetAppFocusedOverrideForTests(true);

            Vector3 focusedPosition = new Vector3(-0.02f, 1.1f, 0.15f);
            Quaternion focusedRotation = Quaternion.Euler(0f, 10f, 0f);
            SetCameraPose(rig, focusedPosition, focusedRotation);
            rig.Follower.RecenterHeadPose();
            rig.Follower.SnapToSeat();

            // Pose esperada en el mundo para una pose local de cabeza dada (como si la cámara tuviera esa pose con foco).
            Vector3 ExpectedWorldPosition(Vector3 local) => rig.CameraOffset.parent.TransformPoint(local);
            Quaternion ExpectedWorldRotation(Quaternion local) => rig.CameraOffset.parent.rotation * local;

            DriverSeatFollower.SetAppFocusedOverrideForTests(false);
            Vector3 menuPosition1 = new Vector3(0.05f, 1.12f, 0.1f);
            Quaternion menuRotation1 = Quaternion.Euler(-5f, 45f, 0f);
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(new Pose(menuPosition1, menuRotation1));
            SetCameraPose(rig, Vector3.zero, Quaternion.identity);
            Check(Vector3.Distance(rig.Camera.position, ExpectedWorldPosition(menuPosition1)) < POSITION_TOLERANCE
                    && Quaternion.Angle(rig.Camera.rotation, ExpectedWorldRotation(menuRotation1)) < ANGLE_TOLERANCE,
                "Menú del sistema con pose XR heredada: la cámara toma la pose viva de la cabeza");

            // La cabeza se mueve: antes de dibujar se actualiza sin esperar al Update siguiente.
            Vector3 menuPosition2 = new Vector3(-0.1f, 1.05f, 0.18f);
            Quaternion menuRotation2 = Quaternion.Euler(10f, -60f, 5f);
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(new Pose(menuPosition2, menuRotation2));
            rig.Follower.ApplyBeforeRenderForTests();
            Check(Vector3.Distance(rig.Camera.position, ExpectedWorldPosition(menuPosition2)) < POSITION_TOLERANCE
                    && Quaternion.Angle(rig.Camera.rotation, ExpectedWorldRotation(menuRotation2)) < ANGLE_TOLERANCE,
                "Menú del sistema: justo antes de dibujar el fondo sigue el movimiento de la cabeza");

            // Si la pose heredada deja de ser válida, se usa la última pose con foco.
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(Pose.identity, valid: false);
            rig.Follower.SnapToSeat();
            Check(Vector3.Distance(rig.Camera.position, ExpectedWorldPosition(focusedPosition)) < POSITION_TOLERANCE,
                "Menú del sistema sin pose heredada válida: se vuelve a la última pose con foco");

            // Vuelve el foco: el Camera Offset vuelve a (0, 0, 0) y antes de dibujar no se toca nada.
            DriverSeatFollower.SetAppFocusedOverrideForTests(true);
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(new Pose(menuPosition1, menuRotation1));
            SetCameraPose(rig, focusedPosition, focusedRotation);
            rig.Follower.ApplyBeforeRenderForTests();
            Check(rig.CameraOffset.localPosition.sqrMagnitude < 1e-8f
                    && Quaternion.Angle(rig.CameraOffset.localRotation, Quaternion.identity) < 1e-3f,
                "Con foco: el Camera Offset queda en (0, 0, 0) sin rotación, también antes de dibujar");

            DriverSeatFollower.SetAppFocusedOverrideForTests(null);
            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(null);
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(null);
        }

        // ------------------------------------------------------------------ Utilidades

        private static Rig BuildRig(List<GameObject> created, Vector3 seatPosition, Quaternion seatRotation)
        {
            var seatObject = new GameObject("DriverSeat");
            seatObject.transform.SetPositionAndRotation(seatPosition, seatRotation);

            var originObject = new GameObject("XR Origin");
            var offsetObject = new GameObject("Camera Offset");
            var cameraObject = new GameObject("Main Camera");
            offsetObject.transform.SetParent(originObject.transform, false);
            offsetObject.transform.localPosition = new Vector3(0f, CAMERA_OFFSET_Y, 0f);
            cameraObject.transform.SetParent(offsetObject.transform, false);
            var camera = cameraObject.AddComponent<Camera>();

            var origin = originObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;

            var follower = originObject.AddComponent<DriverSeatFollower>();
            follower.SetDriverSeat(seatObject.transform);

            created.Add(seatObject);
            created.Add(originObject);
            return new Rig
            {
                Seat = seatObject.transform,
                Origin = originObject.transform,
                CameraOffset = offsetObject.transform,
                Camera = cameraObject.transform,
                Follower = follower
            };
        }

        // Pose de la cabeza relativa al Camera Offset (lo que entrega el TrackedPoseDriver).
        private static void SetCameraPose(Rig rig, Vector3 localPosition, Quaternion localRotation)
        {
            rig.Camera.localPosition = localPosition;
            rig.Camera.localRotation = localRotation;
            rig.Follower.SnapToSeat();
        }

        private static Vector3 ViewPoint(Rig rig) => rig.Seat.TransformPoint(rig.Follower.CalibrationOffset);

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static Vector3 HorizontalForward(Transform t)
        {
            Vector3 f = t.forward;
            f.y = 0f;
            return f.normalized;
        }

        private static void AssertAlignedToSeat(Rig rig, string label)
        {
            float distance = HorizontalDistance(rig.Camera.position, ViewPoint(rig));
            float angle = Vector3.Angle(HorizontalForward(rig.Camera), HorizontalForward(rig.Seat));
            Check(distance < POSITION_TOLERANCE && angle < ANGLE_TOLERANCE,
                $"{label}: cámara en el punto de vista del asiento (error {distance * 1000f:F3} mm) y mirando al frente del asiento (error {angle:F3} grados)");
        }

        private static void Check(bool condition, string description)
        {
            s_Total++;
            if (condition)
            {
                s_Passed++;
                Debug.Log($"[PASS] {description}");
            }
            else
            {
                Debug.LogError($"[FAIL] {description}");
            }
        }
    }
}
