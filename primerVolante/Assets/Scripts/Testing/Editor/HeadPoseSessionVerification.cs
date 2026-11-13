using System.Collections.Generic;
using PrimerVolante.VR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la compensación de pose de cabeza por sesión (<see cref="HeadPoseCompensationSession"/>): el primer
    /// <see cref="DriverSeatFollower"/> captura, los followers de las escenas siguientes (lección nueva, Reiniciar, menú) reutilizan esa
    /// compensación sin recapturar aunque el usuario esté mirando hacia otro lado, y solo se recaptura ante un recentrado del runtime,
    /// un subsistema nuevo o <see cref="DriverSeatFollower.RequestHeadPoseRecenter"/>. La rama VR se fuerza con los seams de
    /// <see cref="DriverSeatFollower"/>; "habilitar" un follower en Edit Mode se simula con <c>RunEnableLogicForTests</c>.
    /// </summary>
    public static class HeadPoseSessionVerification
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
            public Transform Camera;
            public DriverSeatFollower Follower;
        }

        [MenuItem("Tools/Primer Volante/Run Head Pose Session Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE LA COMPENSACION DE POSE DE CABEZA POR SESION (HeadPoseCompensationSession)");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;
            var created = new List<GameObject>();
            try
            {
                HeadPoseCompensationSession.ResetForTests();
                VerifyFirstCaptureStoresCompensation(created);
                VerifyNewSceneReusesWithoutRecapturing(created, 25f, "Escena nueva con la cabeza 25 grados a la derecha (Btn_StartLesson)");
                VerifyNewSceneReusesWithoutRecapturing(created, 45f, "Reiniciar con la cabeza 45 grados a la derecha (menú de pausa)");
                VerifyKnownSubsystemDoesNotInvalidate(created);
                VerifyTrackingOriginUpdateRecaptures(created);
                VerifyRequestHeadPoseRecenter(created);
                VerifyEmptySessionCapturesAsBefore(created);
                VerifyInvalidCaptureLeavesStoreUntouched(created);
                VerifyResetEmptiesStore();
                VerifyDesktopBranchIgnoresStore(created);
                VerifyInvalidationBetweenScenesIsNotLost(created);
                VerifyInvalidationDuringCaptureIsNotStored(created);
                VerifyOpenXRSessionCreateInvalidates(created);
                VerifyRefreshThrottleIsSessionWide(created);
                VerifyAutoCaptureStoresWithObservedEpoch(created);
                VerifyEnableWithNewSubsystemDoesNotReuse(created);
            }
            finally
            {
                HeadPoseCompensationSession.ResetForTests();
                DriverSeatFollower.SetVRActiveOverrideForTests(null);
                DriverSeatFollower.SetHeadPoseValidOverrideForTests(null);
                DriverSeatFollower.SetFrameCountOverrideForTests(null);
                DriverSeatFollower.SetAfterEpochReadHookForTests(null);
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

        // 1. El rig A captura con la cabeza girada 30 grados a la derecha y desplazada: el store guarda eso y la cámara mira al frente.
        private static void VerifyFirstCaptureStoresCompensation(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            Rig a = BuildRig(created, new Vector3(12.3f, 0.7f, -5.1f), Quaternion.Euler(0f, 137f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));

            Check(!HeadPoseCompensationSession.HasCompensation, "Antes de capturar, la sesión no tiene compensación");
            Check(a.Follower.RecenterHeadPose(), "El rig A captura con la cabeza girada y desplazada");

            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset);
            Check(valid && Mathf.Abs(yaw - 30f) < ANGLE_TOLERANCE,
                $"El store guarda el yaw de la captura (esperado 30, obtenido {yaw:F2})");
            Check(Mathf.Abs(offset.x - 0.2f) < 1e-4f && Mathf.Abs(offset.z - 0.4f) < 1e-4f && offset.y == 0f,
                $"El store guarda el desplazamiento horizontal de la cabeza ({offset.x:F3}, {offset.z:F3})");
            AssertLooksForward(a, 0f, "Rig A tras capturar");
        }

        // 2 y 3. Un follower de una escena nueva aplica la compensación del store sin recapturar, aunque el usuario mire a otro lado.
        private static void VerifyNewSceneReusesWithoutRecapturing(List<GameObject> created, float userYawDegrees, string label)
        {
            // Estado de partida: la sesión tiene la compensación de A (cabeza a 30 grados, desplazada).
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            Rig a = BuildRig(created, new Vector3(12.3f, 0.7f, -5.1f), Quaternion.Euler(0f, 137f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RecenterHeadPose();
            HeadPoseCompensationSession.TryGetValid(out float storedYaw, out Vector3 storedOffset);

            // Escena nueva: otro asiento y otra orientación; el usuario mira userYawDegrees más a la derecha que cuando capturó.
            Rig b = BuildRig(created, new Vector3(-40f, 1.2f, 22f), Quaternion.Euler(0f, -75f, 0f));
            SetHeadPose(b, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f + userYawDegrees, 0f));
            b.Follower.RunEnableLogicForTests();
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            b.Follower.RunHeadPoseCaptureUpdateForTests();

            AssertLooksForward(b, userYawDegrees, $"{label}: la cámara de B mira {userYawDegrees} grados a la derecha del frente del asiento (no al frente)");
            Check(HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset)
                    && Mathf.Approximately(yaw, storedYaw) && offset == storedOffset,
                $"{label}: el store no cambió (no hubo recaptura)");
            Check(HorizontalDistance(b.Camera.position, ViewPoint(b)) < POSITION_TOLERANCE,
                $"{label}: la cámara de B queda en el punto de vista de su asiento");
        }

        // 4. Que B vea por primera vez un subsistema que la sesión ya conocía no invalida.
        private static void VerifyKnownSubsystemDoesNotInvalidate(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(1000);
            var subsystem = new object();
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { subsystem });

            Rig a = BuildRig(created, new Vector3(1f, 0f, 1f), Quaternion.identity);
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests();
            a.Follower.RunHeadPoseCaptureUpdateForTests(); // ve el subsistema (sin compensación todavía: nada que invalidar) y captura
            Check(HeadPoseCompensationSession.HasCompensation, "A captura con el subsistema ya visible");
            int epochAfterA = HeadPoseCompensationSession.Epoch;
            HeadPoseCompensationSession.TryGetValid(out float storedYaw, out Vector3 storedOffset);

            Rig b = BuildRig(created, new Vector3(30f, 0f, -8f), Quaternion.Euler(0f, 90f, 0f));
            SetHeadPose(b, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 55f, 0f));
            b.Follower.RunEnableLogicForTests();
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(1010);
            b.Follower.RunHeadPoseCaptureUpdateForTests();

            Check(HeadPoseCompensationSession.Epoch == epochAfterA, "Un subsistema ya conocido por la sesión no sube la época de recaptura");
            Check(HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset) && yaw == storedYaw && offset == storedOffset,
                "Un subsistema ya conocido no invalida ni recaptura: el store queda igual");
            AssertLooksForward(b, 25f, "B con el subsistema ya conocido sigue con la compensación de la sesión");

            // Un subsistema distinto, en cambio, sí es nuevo para la sesión.
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { subsystem, new object() });
            HeadPoseCompensationSession.RefreshSubsystems();
            Check(HeadPoseCompensationSession.Epoch == epochAfterA + 1 && !HeadPoseCompensationSession.TryGetValid(out _, out _),
                "Un subsistema nuevo para la sesión (con compensación vigente) la invalida");
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(null);
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // 5. trackingOriginUpdated: el follower vivo recaptura tras RECAPTURE_DELAY_FRAMES con la pose actual y el store se actualiza.
        private static void VerifyTrackingOriginUpdateRecaptures(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(2000);

            Rig a = BuildRig(created, new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 20f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RecenterHeadPose();

            Rig b = BuildRig(created, new Vector3(-9f, 0f, 3f), Quaternion.Euler(0f, 200f, 0f));
            SetHeadPose(b, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            b.Follower.RunEnableLogicForTests();
            AssertLooksForward(b, 0f, "B reutiliza la compensación antes del recentrado");

            // El runtime recentra: la cabeza queda en otra pose local.
            HeadPoseCompensationSession.SimulateTrackingOriginUpdatedForTests();
            SetHeadPose(b, new Vector3(-0.3f, 0.05f, 0.1f), Quaternion.Euler(0f, -50f, 0f));
            b.Follower.RunHeadPoseCaptureUpdateForTests(); // detecta la invalidación, espera
            Check(HeadPoseCompensationSession.HasCompensation && !HeadPoseCompensationSession.TryGetValid(out _, out _),
                "Tras el recentrado la compensación del store queda invalidada");

            DriverSeatFollower.SetFrameCountOverrideForTests(2001);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Un frame después del recentrado, B sigue esperando (RECAPTURE_DELAY_FRAMES = 2)");

            DriverSeatFollower.SetFrameCountOverrideForTests(2002);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset);
            Check(valid && Mathf.Abs(yaw - (-50f)) < ANGLE_TOLERANCE && Mathf.Abs(offset.x - (-0.3f)) < 1e-4f && Mathf.Abs(offset.z - 0.1f) < 1e-4f,
                $"Tras el retardo B recaptura con la pose actual y el store se actualiza (yaw {yaw:F2})");
            AssertLooksForward(b, 0f, "Tras recapturar, B mira al frente de su asiento");

            // La recaptura vale para las escenas siguientes.
            Rig c = BuildRig(created, new Vector3(0f, 0f, 0f), Quaternion.Euler(0f, 10f, 0f));
            SetHeadPose(c, new Vector3(-0.3f, 0.05f, 0.1f), Quaternion.Euler(0f, -50f + 12f, 0f));
            c.Follower.RunEnableLogicForTests();
            AssertLooksForward(c, 12f, "Un follower posterior reutiliza la compensación recapturada");
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // 6. RequestHeadPoseRecenter() recaptura y actualiza el store.
        private static void VerifyRequestHeadPoseRecenter(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(3000);

            Rig a = BuildRig(created, new Vector3(2f, 0f, -3f), Quaternion.Euler(0f, 300f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests();
            a.Follower.RunHeadPoseCaptureUpdateForTests(); // captura inicial automática
            Check(HeadPoseCompensationSession.HasCompensation, "El follower captura automáticamente en el primer frame válido");

            SetHeadPose(a, new Vector3(0.1f, 0.1f, 0.2f), Quaternion.Euler(10f, 80f, 0f));
            a.Follower.RequestHeadPoseRecenter();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "RequestHeadPoseRecenter invalida la compensación de la sesión");
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(3002);
            a.Follower.RunHeadPoseCaptureUpdateForTests();

            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset);
            Check(valid && Mathf.Abs(yaw - 80f) < ANGLE_TOLERANCE && Mathf.Abs(offset.x - 0.1f) < 1e-4f && Mathf.Abs(offset.z - 0.2f) < 1e-4f,
                $"RequestHeadPoseRecenter recaptura con la pose actual y actualiza el store (yaw {yaw:F2})");
            AssertLooksForward(a, 0f, "Tras RequestHeadPoseRecenter la cámara mira al frente del asiento");
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // 7. Sin compensación en el store (sesión nueva): el follower captura como antes, en el primer frame válido.
        private static void VerifyEmptySessionCapturesAsBefore(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(4000);

            Rig a = BuildRig(created, new Vector3(7f, 0.4f, 7f), Quaternion.Euler(0f, 45f, 0f));
            SetHeadPose(a, new Vector3(0.25f, 0.1f, 0.3f), Quaternion.Euler(10f, 60f, 0f));
            a.Follower.RunEnableLogicForTests();
            Check(!HeadPoseCompensationSession.HasCompensation, "Habilitar un follower con la sesión vacía no inventa una compensación");

            DriverSeatFollower.SetHeadPoseValidOverrideForTests(false);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.HasCompensation, "Con la sesión vacía y la pose todavía inválida, no captura");

            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.HasCompensation, "Con la sesión vacía captura en el primer frame válido");
            AssertLooksForward(a, 0f, "Sesión vacía: la cámara queda mirando al frente del asiento");

            // Un segundo follower con la sesión ya cargada no vuelve a capturar aunque la pose sea válida.
            HeadPoseCompensationSession.TryGetValid(out float storedYaw, out _);
            Rig b = BuildRig(created, new Vector3(0f, 0f, 0f), Quaternion.identity);
            SetHeadPose(b, new Vector3(0.25f, 0.1f, 0.3f), Quaternion.Euler(10f, 120f, 0f));
            b.Follower.RunEnableLogicForTests();
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            HeadPoseCompensationSession.TryGetValid(out float yawAfter, out _);
            Check(Mathf.Approximately(storedYaw, yawAfter), "Con la sesión ya cargada el segundo follower no recaptura aunque la pose sea válida");
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // 8. Una captura inválida (cabeza vertical o sin cámara) no toca el store.
        private static void VerifyInvalidCaptureLeavesStoreUntouched(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);

            Rig a = BuildRig(created, new Vector3(1f, 1f, 1f), Quaternion.Euler(0f, 90f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            a.Follower.RecenterHeadPose();
            HeadPoseCompensationSession.TryGetValid(out float yawBefore, out Vector3 offsetBefore);

            SetHeadPose(a, new Vector3(0.9f, 0.1f, -0.5f), Quaternion.Euler(90f, 70f, 0f)); // mirando recto hacia abajo
            bool captured = a.Follower.RecenterHeadPose();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yawAfter, out Vector3 offsetAfter);
            Check(!captured && valid && yawAfter == yawBefore && offsetAfter == offsetBefore,
                "Cabeza vertical: la captura falla y el store queda igual");

            HeadPoseCompensationSession.ResetForTests();
            var noCameraObject = new GameObject("NoCameraRig");
            created.Add(noCameraObject);
            noCameraObject.AddComponent<XROrigin>();
            var noCameraFollower = noCameraObject.AddComponent<DriverSeatFollower>();
            Check(!noCameraFollower.RecenterHeadPose() && !HeadPoseCompensationSession.HasCompensation,
                "Sin cámara: la captura falla y el store sigue vacío");
        }

        // 9. ResetForTests (el mismo reset de estáticos del arranque) deja el store vacío.
        private static void VerifyResetEmptiesStore()
        {
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            var created = new List<GameObject>();
            try
            {
                Rig a = BuildRig(created, new Vector3(3f, 0f, 3f), Quaternion.identity);
                SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
                a.Follower.RecenterHeadPose();
                HeadPoseCompensationSession.SimulateTrackingOriginUpdatedForTests();
                Check(HeadPoseCompensationSession.HasCompensation && HeadPoseCompensationSession.Epoch > 0, "Con una captura e invalidación la sesión tiene estado");

                HeadPoseCompensationSession.ResetForTests();
                Check(!HeadPoseCompensationSession.HasCompensation
                        && !HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset)
                        && yaw == 0f && offset == Vector3.zero
                        && HeadPoseCompensationSession.Epoch == 0,
                    "El reset de estáticos deja el store vacío (sin compensación, época 0)");
            }
            finally
            {
                foreach (GameObject go in created) Object.DestroyImmediate(go);
            }
        }

        // 10. Rama de escritorio: el store no se usa y la posición/rotación del rig queda igual que antes.
        private static void VerifyDesktopBranchIgnoresStore(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            Rig seed = BuildRig(created, new Vector3(1f, 0f, 1f), Quaternion.identity);
            SetHeadPose(seed, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            seed.Follower.RecenterHeadPose();
            int epochBefore = HeadPoseCompensationSession.Epoch;
            HeadPoseCompensationSession.TryGetValid(out float yawBefore, out Vector3 offsetBefore);

            DriverSeatFollower.SetVRActiveOverrideForTests(false);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            Rig d = BuildRig(created, new Vector3(5f, 0.2f, 7f), Quaternion.Euler(0f, 30f, 0f));
            d.Follower.CalibrationOffset = new Vector3(0.1f, 0.05f, 0.05f);
            SetHeadPose(d, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 70f, 0f));
            d.Follower.RunEnableLogicForTests();
            d.Follower.RunHeadPoseCaptureUpdateForTests();
            d.Follower.SnapToSeat();

            Vector3 expectedPosition = d.Seat.TransformPoint(d.Follower.CalibrationOffset);
            Check(Vector3.Distance(d.Origin.position, expectedPosition) < 1e-5f
                    && Quaternion.Angle(d.Origin.rotation, d.Seat.rotation) < 1e-3f,
                "Escritorio: la posición y rotación del rig son exactamente asiento + calibración (la compensación de la sesión no se aplica)");
            Check(HeadPoseCompensationSession.Epoch == epochBefore
                    && HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset) && yaw == yawBefore && offset == offsetBefore,
                "Escritorio: el follower no lee ni escribe el store");
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(null);
        }

        // Un recentrado que llega entre dos escenas (sin follower vivo) no se pierde: el follower nuevo no reutiliza la compensación
        // vieja y, como la invalidación es reciente, espera RECAPTURE_DELAY_FRAMES antes de capturar (la pose podría ser anterior).
        private static void VerifyInvalidationBetweenScenesIsNotLost(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(5000);

            Rig a = BuildRig(created, new Vector3(0f, 0f, 0f), Quaternion.identity);
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RecenterHeadPose();
            Object.DestroyImmediate(a.Origin.gameObject); // el overlay viejo se descarga

            HeadPoseCompensationSession.SimulateTrackingOriginUpdatedForTests(); // el usuario recentra en la transición

            DriverSeatFollower.SetFrameCountOverrideForTests(5010);
            Rig b = BuildRig(created, new Vector3(9f, 0f, 9f), Quaternion.Euler(0f, 60f, 0f));
            SetHeadPose(b, new Vector3(-0.1f, 0.1f, 0.3f), Quaternion.Euler(10f, -20f, 0f));
            b.Follower.RunEnableLogicForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Un recentrado sin follower vivo deja la compensación invalidada para el follower nuevo");

            b.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _),
                "El follower habilitado justo después de la invalidación no captura en el mismo frame (la pose podría ser anterior al recentrado)");
            DriverSeatFollower.SetFrameCountOverrideForTests(5011);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Un frame después sigue esperando (RECAPTURE_DELAY_FRAMES = 2)");

            DriverSeatFollower.SetFrameCountOverrideForTests(5012);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out _);
            Check(valid && Mathf.Abs(yaw - (-20f)) < ANGLE_TOLERANCE, $"Cumplido el retardo, el follower nuevo captura con la pose actual en lugar de reutilizar la vieja (yaw {yaw:F2})");
            AssertLooksForward(b, 0f, "Tras capturar, el follower nuevo mira al frente del asiento");
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // Si llega una invalidación después de leer la época y antes de guardar, la captura pudo ser anterior al recentrado: no se guarda,
        // la recaptura sigue pendiente y no se reutiliza la compensación vieja ni la captura descartada.
        private static void VerifyInvalidationDuringCaptureIsNotStored(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(6000);

            Rig a = BuildRig(created, new Vector3(2f, 0f, 2f), Quaternion.Euler(0f, 10f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests();
            a.Follower.RunHeadPoseCaptureUpdateForTests(); // captura inicial en la época 0
            Check(HeadPoseCompensationSession.TryGetValid(out float firstYaw, out Vector3 firstOffset), "Captura inicial vigente en la sesión");

            // Nueva captura (otra pose) durante la cual llega un recentrado.
            SetHeadPose(a, new Vector3(-0.3f, 0.1f, 0.1f), Quaternion.Euler(0f, -50f, 0f));
            DriverSeatFollower.SetAfterEpochReadHookForTests(() =>
            {
                DriverSeatFollower.SetAfterEpochReadHookForTests(null);
                HeadPoseCompensationSession.SimulateTrackingOriginUpdatedForTests();
            });
            int epochBefore = HeadPoseCompensationSession.Epoch;
            bool captured = a.Follower.RecenterHeadPose();

            Check(captured && HeadPoseCompensationSession.Epoch == epochBefore + 1, "La invalidación llegó durante la captura (la época avanzó)");
            Check(HeadPoseCompensationSession.HasCompensation && !HeadPoseCompensationSession.TryGetValid(out _, out _),
                "La captura tomada durante la invalidación no se marca como válida: la compensación sigue invalidada");

            // Un follower nuevo no reutiliza ni la compensación vieja ni la captura descartada.
            Rig b = BuildRig(created, new Vector3(9f, 0f, 9f), Quaternion.Euler(0f, 60f, 0f));
            SetHeadPose(b, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 47f, 0f));
            b.Follower.RunEnableLogicForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Un follower nuevo no reutiliza la captura descartada");

            // El follower vivo atiende el cambio de época y recaptura tras el retardo; el store pasa a la captura buena.
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(6002);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out Vector3 offset);
            Check(valid && Mathf.Abs(yaw - (-50f)) < ANGLE_TOLERANCE && Mathf.Abs(offset.x - (-0.3f)) < 1e-4f && yaw != firstYaw && offset != firstOffset,
                $"Tras el retardo el follower vivo recaptura y recién ahí el store queda vigente (yaw {yaw:F2})");

            // Store directo: una captura con una época vieja se rechaza y no pisa lo guardado.
            HeadPoseCompensationSession.TryGetValid(out float goodYaw, out Vector3 goodOffset);
            int staleEpoch = HeadPoseCompensationSession.Epoch;
            HeadPoseCompensationSession.Invalidate("test");
            bool storedStale = HeadPoseCompensationSession.Store(staleEpoch, 123f, new Vector3(9f, 0f, 9f));
            Check(!storedStale && HeadPoseCompensationSession.HasCompensation && !HeadPoseCompensationSession.TryGetValid(out _, out _),
                "Store con una época vieja devuelve false y la compensación sigue invalidada");
            bool storedFresh = HeadPoseCompensationSession.Store(HeadPoseCompensationSession.Epoch, goodYaw, goodOffset);
            Check(storedFresh && HeadPoseCompensationSession.TryGetValid(out float freshYaw, out _) && freshYaw == goodYaw,
                "Store con la época actual guarda y la compensación vuelve a ser válida");
            DriverSeatFollower.SetAfterEpochReadHookForTests(null);
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // Una sesión OpenXR nueva (reconexión, LOSS_PENDING y sesión nueva) invalida aunque el subsistema de entrada sea el mismo.
        private static void VerifyOpenXRSessionCreateInvalidates(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            VRSessionStateFeature.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(7000);

            Rig a = BuildRig(created, new Vector3(1f, 0f, 1f), Quaternion.identity);
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests();
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.TryGetValid(out _, out _), "Compensación vigente antes de la sesión OpenXR nueva");

            int epochBefore = HeadPoseCompensationSession.Epoch;
            VRSessionStateFeature.SimulateSessionCreate(); // mismo camino que OnSessionCreate
            Check(HeadPoseCompensationSession.Epoch == epochBefore + 1 && !HeadPoseCompensationSession.TryGetValid(out _, out _),
                "Crear una sesión OpenXR nueva invalida la compensación (sube la época)");

            // Se recaptura con la pose nueva tras el retardo.
            SetHeadPose(a, new Vector3(0.0f, 0.1f, 0.2f), Quaternion.Euler(0f, 100f, 0f));
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(7002);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out _);
            Check(valid && Mathf.Abs(yaw - 100f) < ANGLE_TOLERANCE, $"Tras la sesión nueva el follower recaptura con la pose actual (yaw {yaw:F2})");

            VRSessionStateFeature.ResetForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // Un solo throttle para toda la sesión: varios followers no multiplican las revisiones de subsistemas.
        private static void VerifyRefreshThrottleIsSessionWide(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(8000);
            HeadPoseCompensationSession.SetTimeOverrideForTests(100f);
            var first = new object();
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { first });

            Rig a = BuildRig(created, new Vector3(1f, 0f, 1f), Quaternion.identity);
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests(); // revisa al habilitarse (la ventana de 2 s arranca en t = 100)
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.TryGetValid(out _, out _), "Con la sesión recién revisada hay una compensación válida");

            Rig b = BuildRig(created, new Vector3(5f, 0f, 5f), Quaternion.identity);
            SetHeadPose(b, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            b.Follower.RunEnableLogicForTests();

            // Aparece un subsistema nuevo, pero dentro de la ventana ningún follower (ni B, que todavía no hizo su primer Update) revisa.
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { first, new object() });
            HeadPoseCompensationSession.SetTimeOverrideForTests(101f);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.TryGetValid(out _, out _), "Dentro de la ventana de 2 s ningún follower vuelve a revisar los subsistemas (un solo throttle)");

            HeadPoseCompensationSession.SetTimeOverrideForTests(102.5f);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Pasada la ventana la revisión corre y el subsistema nuevo invalida la compensación");

            HeadPoseCompensationSession.SetTimeOverrideForTests(null);
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(null);
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // La captura automática guarda con la época cuyo retardo esperó. Si la época avanza entre la observación y la captura (incluso
        // después de cumplido el retardo), la captura no queda válida y la invalidación nueva no saltea su propio retardo.
        private static void VerifyAutoCaptureStoresWithObservedEpoch(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(9000);

            Rig a = BuildRig(created, new Vector3(3f, 0f, 3f), Quaternion.Euler(0f, 15f, 0f));
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests();
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.TryGetValid(out _, out _), "Captura automática inicial válida (época 0)");

            // Un recentrado se observa en el frame 9010: la recaptura corresponde a la época 1 y toca en el 9012.
            DriverSeatFollower.SetFrameCountOverrideForTests(9010);
            HeadPoseCompensationSession.SimulateTrackingOriginUpdatedForTests();
            SetHeadPose(a, new Vector3(-0.3f, 0.1f, 0.1f), Quaternion.Euler(0f, -50f, 0f));
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "En el frame de la invalidación el follower espera el retardo");

            // Cumplido el retardo, justo entre la observación y la captura llega otra invalidación (época 2).
            DriverSeatFollower.SetFrameCountOverrideForTests(9012);
            DriverSeatFollower.SetAfterEpochReadHookForTests(() =>
            {
                DriverSeatFollower.SetAfterEpochReadHookForTests(null);
                HeadPoseCompensationSession.SimulateTrackingOriginUpdatedForTests();
            });
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.Epoch == 2, "Llegó una segunda invalidación después de cumplir el retardo (época 2)");
            Check(HeadPoseCompensationSession.HasCompensation && !HeadPoseCompensationSession.TryGetValid(out _, out _),
                "La captura automática se guarda con la época esperada (1): como la época avanzó no queda válida");

            // La invalidación nueva tampoco saltea su retardo: se observa en el 9013 y recién toca en el 9015.
            DriverSeatFollower.SetFrameCountOverrideForTests(9013);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(9014);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "La invalidación que llegó entre la observación y la captura no saltea su retardo");

            DriverSeatFollower.SetFrameCountOverrideForTests(9015);
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out _);
            Check(valid && Mathf.Abs(yaw - (-50f)) < ANGLE_TOLERANCE, $"Cumplido el retardo de la época 2 la captura queda válida (yaw {yaw:F2})");
            DriverSeatFollower.SetAfterEpochReadHookForTests(null);
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
        }

        // Un follower que se habilita con un subsistema de entrada nuevo para la sesión no reutiliza la compensación vieja: la revisión
        // de subsistemas corre antes de decidir (en la habilitación con visor activo, o en el primer Update si todavía no había visor).
        private static void VerifyEnableWithNewSubsystemDoesNotReuse(List<GameObject> created)
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            DriverSeatFollower.SetHeadPoseValidOverrideForTests(true);
            DriverSeatFollower.SetFrameCountOverrideForTests(9500);
            var known = new object();
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { known });

            Rig a = BuildRig(created, new Vector3(1f, 0f, 1f), Quaternion.identity);
            SetHeadPose(a, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            a.Follower.RunEnableLogicForTests();
            a.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(HeadPoseCompensationSession.TryGetValid(out _, out _), "Compensación válida con un solo subsistema conocido");

            // Aparece un subsistema nuevo mientras no hay follower: el follower que se habilita lo ve antes de reutilizar.
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { known, new object() });
            Rig b = BuildRig(created, new Vector3(30f, 0f, -8f), Quaternion.Euler(0f, 90f, 0f));
            SetHeadPose(b, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 55f, 0f));
            b.Follower.RunEnableLogicForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Un subsistema nuevo visto al habilitar invalida antes de reutilizar");
            AssertYawOnly(b, 55f, "El follower no aplicó la compensación vieja (la cámara mira donde mira la cabeza)");

            b.Follower.RunHeadPoseCaptureUpdateForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(9501);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Hasta cumplir el retardo no se recaptura");
            DriverSeatFollower.SetFrameCountOverrideForTests(9502);
            b.Follower.RunHeadPoseCaptureUpdateForTests();
            bool valid = HeadPoseCompensationSession.TryGetValid(out float yaw, out _);
            Check(valid && Mathf.Abs(yaw - 55f) < ANGLE_TOLERANCE, $"Cumplido el retardo captura con su pose (yaw {yaw:F2})");
            AssertLooksForward(b, 0f, "Tras capturar, B mira al frente de su asiento");

            // Sin visor al habilitarse: la revisión se fuerza en el primer Update y corre antes de reutilizar.
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetFrameCountOverrideForTests(9600);
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { known });
            Rig c = BuildRig(created, new Vector3(0f, 0f, 0f), Quaternion.identity);
            SetHeadPose(c, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 30f, 0f));
            c.Follower.RunEnableLogicForTests();
            c.Follower.RunHeadPoseCaptureUpdateForTests(); // captura (época 0)
            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(new[] { known, new object() });

            DriverSeatFollower.SetVRActiveOverrideForTests(false);
            Rig d = BuildRig(created, new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 40f, 0f));
            SetHeadPose(d, new Vector3(0.2f, 0.1f, 0.4f), Quaternion.Euler(10f, 77f, 0f));
            d.Follower.RunEnableLogicForTests(); // sin visor: no revisa ni reutiliza
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            d.Follower.RunHeadPoseCaptureUpdateForTests();
            Check(!HeadPoseCompensationSession.TryGetValid(out _, out _), "Con el visor resuelto después, el primer Update revisa los subsistemas antes de reutilizar y invalida");
            AssertYawOnly(d, 77f, "El follower no reutilizó la compensación vieja");

            HeadPoseCompensationSession.SetSubsystemKeysOverrideForTests(null);
            DriverSeatFollower.SetFrameCountOverrideForTests(null);
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
            follower.UsePersistentCalibration = false;
            follower.SetDriverSeat(seatObject.transform);

            created.Add(seatObject);
            created.Add(originObject);
            return new Rig
            {
                Seat = seatObject.transform,
                Origin = originObject.transform,
                Camera = cameraObject.transform,
                Follower = follower
            };
        }

        // Pose de la cabeza relativa al Camera Offset (lo que entrega el TrackedPoseDriver).
        private static void SetHeadPose(Rig rig, Vector3 localPosition, Quaternion localRotation)
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

        // Solo el yaw de la cámara respecto del frente del asiento (sin exigir posición): sirve para ver que NO hay compensación aplicada.
        private static void AssertYawOnly(Rig rig, float expectedYaw, string label)
        {
            float yaw = Vector3.SignedAngle(HorizontalForward(rig.Seat), HorizontalForward(rig.Camera), Vector3.up);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw, expectedYaw)) < ANGLE_TOLERANCE, $"{label} (yaw {yaw:F2}, esperado {expectedYaw:F2})");
        }

        // La cámara mira expectedYaw grados a la derecha del frente del asiento (0 = al frente) y está en el punto de vista.
        private static void AssertLooksForward(Rig rig, float expectedYaw, string label)
        {
            float yaw = Vector3.SignedAngle(HorizontalForward(rig.Seat), HorizontalForward(rig.Camera), Vector3.up);
            float distance = HorizontalDistance(rig.Camera.position, ViewPoint(rig));
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw, expectedYaw)) < ANGLE_TOLERANCE && distance < POSITION_TOLERANCE,
                $"{label} (yaw {yaw:F2}, esperado {expectedYaw:F2}; error de posición {distance * 1000f:F3} mm)");
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
