using System;
using System.Linq;
using System.Reflection;
using PrimerVolante.MainMenu;
using PrimerVolante.VR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la compensación de la pose de la cabeza en el menú principal (NewMainMenu): el XR Origin tiene un
    /// <see cref="DriverSeatFollower"/> anclado a MenuViewpoint, y <see cref="NewMainMenuController"/> no fija el rig en cero cuando
    /// hay follower (sí lo hace sin follower, como fallback). Con VR forzado (seams de DriverSeatFollower), tras recapturar la pose de
    /// la cabeza la cámara queda sobre MenuViewpoint mirando hacia +Z (el canvas). Corre sobre una copia temporal de la escena guardada.
    /// </summary>
    public static class MenuHeadPoseVerification
    {
        private const float POSITION_TOLERANCE = 0.01f; // 1 cm
        private const float ANGLE_TOLERANCE = 0.5f;     // grados

        private static int s_Passed;
        private static int s_Total;

        [MenuItem("Tools/Primer Volante/Verify Menu Head Pose")]
        public static void RunVerification()
        {
            s_Passed = 0;
            s_Total = 0;
            HeadPoseCompensationSession.ResetForTests(); // la compensación de la sesión es estática: que no se filtre desde otra suite

            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE LA POSE DE CABEZA EN EL MENU PRINCIPAL (NewMainMenu + DriverSeatFollower)");
            Debug.Log("=========================================================");

            SceneSetup[] sceneSetupBefore = null;
            try
            {
                bool anyDirty = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).isDirty) anyDirty = true;
                }

                if (anyDirty)
                {
                    Check(false, "Hay escenas abiertas con cambios sin guardar: guardalas antes de verificar (se aísla una copia de NewMainMenu)");
                }
                else
                {
                    sceneSetupBefore = EditorSceneManager.GetSceneManagerSetup();
                    VerificationSceneUtil.WithSceneCopy(NewMainMenuSetup.SCENE_PATH, VerifyOnCopy);
                }
            }
            catch (Exception ex)
            {
                Check(false, $"Excepción no controlada durante las pruebas: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                ResetOverrides();
                RestoreScenes(sceneSetupBefore);
            }

            Debug.Log("=========================================================");
            if (s_Passed == s_Total) Debug.Log($"RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            else Debug.LogError($"[FAIL] RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas");
            Debug.Log("=========================================================");
        }

        private static void ResetOverrides()
        {
            HeadPoseCompensationSession.ResetForTests();
            DriverSeatFollower.SetVRActiveOverrideForTests(null);
            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(null);
            DriverSeatFollower.SetAppFocusedOverrideForTests(null);
            DriverSeatFollower.SetLegacyHeadPoseOverrideForTests(null);
        }

        // Una escena Untitled sin modificar se captura sin path y RestoreSceneManagerSetup la rechaza: se descartan esas entradas.
        private static void RestoreScenes(SceneSetup[] setup)
        {
            if (setup == null) return;

            try
            {
                SceneSetup[] restorable = setup.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
                if (restorable.Length == 0)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    return;
                }
                if (!restorable.Any(s => s.isActive)) restorable[0].isActive = true;
                EditorSceneManager.RestoreSceneManagerSetup(restorable);
            }
            catch (Exception ex)
            {
                Check(false, $"No se pudieron restaurar las escenas abiertas antes de la verificación: {ex.Message}");
            }
        }

        private sealed class MenuRig
        {
            public Scene Scene;
            public GameObject OriginObject;
            public Transform Origin;
            public Transform CameraOffset;
            public Transform Camera;
            public DriverSeatFollower Follower;
            public Transform Viewpoint;
            public NewMainMenuController Controller;
            public InGameMenuController PauseMenu;
        }

        private static void VerifyOnCopy(Scene scene)
        {
            // Aislar la copia (el setup usa búsquedas globales y borra cámaras ajenas al XR Origin): se cierran las demás escenas y
            // se agrega una escena vacía para que cerrar la copia al final nunca sea cerrar la última. RestoreScenes la descarta.
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open != scene) EditorSceneManager.CloseScene(open, true);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            MenuRig rig = FindRig(scene);
            if (rig == null) return;

            VerifySceneConfiguration(rig);
            VerifyPauseMenuConfiguration(rig);
            VerifyVRHeadPoseCompensation(rig);
            VerifyLockPlayerStationaryWithFollower(rig);
            VerifyFallbackWhenFollowerCannotMoveRig(rig);
            VerifyFallbackWithoutFollower();
            VerifyDesktopMode(rig);
            VerifySetupIsIdempotent(scene);
        }

        private static MenuRig FindRig(Scene scene)
        {
            var rig = new MenuRig { Scene = scene };
            rig.OriginObject = GameObject.Find("XR Origin (XR Rig)");
            GameObject canvasObj = GameObject.Find(NewMainMenuSetup.CANVAS_NAME);
            Check(rig.OriginObject != null && canvasObj != null, "La escena tiene XR Origin (XR Rig) y NewMainMenu_Canvas");
            if (rig.OriginObject == null || canvasObj == null) return null;

            rig.Origin = rig.OriginObject.transform;
            rig.Follower = rig.OriginObject.GetComponent<DriverSeatFollower>();
            rig.Controller = canvasObj.GetComponent<NewMainMenuController>();
            XROrigin xrOrigin = rig.OriginObject.GetComponent<XROrigin>();
            rig.Camera = xrOrigin != null && xrOrigin.Camera != null ? xrOrigin.Camera.transform : null;
            rig.CameraOffset = xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.transform : null;
            rig.Viewpoint = rig.Follower != null ? rig.Follower.DriverSeat : null;
            rig.PauseMenu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).FirstOrDefault();

            bool complete = rig.Follower != null && rig.Controller != null && rig.Camera != null && rig.CameraOffset != null && rig.Viewpoint != null;
            Check(complete, "El XR Origin tiene follower, cámara y Camera Offset, y el canvas tiene NewMainMenuController");
            return complete ? rig : null;
        }

        // ------------------------------------------------------------------ Escenarios

        private static void VerifySceneConfiguration(MenuRig rig)
        {
            Check(rig.Follower.enabled && rig.Follower.FollowSeat, "El DriverSeatFollower del XR Origin está habilitado y con FollowSeat = true");
            Check(rig.Viewpoint.name == NewMainMenuSetup.MENU_VIEWPOINT_NAME, "El asiento del follower es MenuViewpoint");
            Check(rig.Viewpoint.gameObject.scene == rig.Scene && rig.Viewpoint.parent == null,
                "MenuViewpoint es de la misma escena y está en la raíz (no es hijo del XR Origin)");

            GameObject ground = GameObject.Find("Ground_MainMenu");
            BoxCollider box = ground != null ? ground.GetComponent<BoxCollider>() : null;
            float groundTop = box != null
                ? box.transform.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, 0f)).y
                : float.NaN;
            Check(box != null && Mathf.Abs(groundTop - NewMainMenuSetup.MENU_FLOOR_Y) < 1e-4f,
                $"La cara superior de Ground_MainMenu está en y = {NewMainMenuSetup.MENU_FLOOR_Y} (actual: {groundTop})");

            Vector3 expected = new Vector3(0f, groundTop, 0f);
            Check(Vector3.Distance(rig.Viewpoint.position, expected) < 1e-4f && Quaternion.Angle(rig.Viewpoint.rotation, Quaternion.identity) < 1e-3f,
                $"MenuViewpoint está en (0, superficie del suelo, 0) con rotación identidad (posición {rig.Viewpoint.position})");
            Check(rig.Follower.CalibrationOffset == Vector3.zero, "La calibración del follower vale cero");
        }

        private static void VerifyPauseMenuConfiguration(MenuRig rig)
        {
            Check(rig.PauseMenu != null, "NewMainMenu tiene un InGameMenuController (menú de pausa)");
            if (rig.PauseMenu == null) return;

            Check(!rig.PauseMenu.ShowCalibrationButton, "El menú de pausa de NewMainMenu oculta el botón Calibrar (ShowCalibrationButton = false)");
            Check(rig.PauseMenu.ForwardReference == rig.Viewpoint,
                "La referencia de frente del menú de pausa es MenuViewpoint (la pausa queda a 45 grados a la derecha del frente del menú)");
        }

        // VR forzado, modo Floor y una cabeza desplazada y girada: el arranque real del Quest.
        private static void VerifyVRHeadPoseCompensation(MenuRig rig)
        {
            EnterVR();
            rig.Controller.LockPlayerStationary(); // cachea el follower (Awake no corre en Edit Mode)

            Vector3 headLocalPosition = new Vector3(0.3f, 1.6f, -0.4f);
            SetHeadPose(rig, headLocalPosition, Quaternion.Euler(0f, 30f, 0f));
            Check(rig.Follower.RecenterHeadPose(), "RecenterHeadPose devuelve true con la cabeza desplazada y girada");
            InvokeLateUpdate(rig.Controller);

            float horizontalError = HorizontalDistance(rig.Camera.position, rig.Viewpoint.position);
            Check(horizontalError < POSITION_TOLERANCE,
                $"VR: la cámara queda horizontalmente sobre MenuViewpoint tras recapturar y el LateUpdate del controlador (error {horizontalError * 1000f:F2} mm)");

            float yawError = Vector3.Angle(HorizontalForward(rig.Camera), Vector3.forward);
            Check(yawError < ANGLE_TOLERANCE, $"VR: el frente horizontal de la cámara coincide con +Z, hacia el canvas (error {yawError:F3} grados)");

            Check(rig.Origin.position.sqrMagnitude > 1e-4f, $"VR: el LateUpdate del controlador no dejó el rig en (0, 0, 0) (rig en {rig.Origin.position})");
            Check(Mathf.Abs(rig.CameraOffset.localPosition.y) < 1e-4f && Mathf.Abs(rig.Camera.position.y - (rig.Viewpoint.position.y + headLocalPosition.y)) < POSITION_TOLERANCE,
                "VR Floor: el Camera Offset queda en y = 0 y la altura de los ojos la da la cabeza (altura real del usuario)");

            // Un recentrado del runtime (otra pose de cabeza) se recaptura y la cámara vuelve sobre el anclaje.
            SetHeadPose(rig, new Vector3(-0.5f, 1.7f, 0.2f), Quaternion.Euler(0f, -75f, 0f));
            rig.Follower.RecenterHeadPose();
            InvokeLateUpdate(rig.Controller);
            Check(HorizontalDistance(rig.Camera.position, rig.Viewpoint.position) < POSITION_TOLERANCE
                    && Vector3.Angle(HorizontalForward(rig.Camera), Vector3.forward) < ANGLE_TOLERANCE,
                "VR: tras recentrar (otra pose de cabeza) la cámara vuelve a quedar sobre MenuViewpoint mirando a +Z");
        }

        private static void VerifyLockPlayerStationaryWithFollower(MenuRig rig)
        {
            EnterVR();
            Vector3 headLocalPosition = new Vector3(0.3f, 1.6f, -0.4f);
            SetHeadPose(rig, headLocalPosition, Quaternion.Euler(0f, 30f, 0f));
            rig.Follower.RecenterHeadPose();

            CharacterController characterController = rig.OriginObject.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = true;
            rig.Origin.SetPositionAndRotation(new Vector3(7f, 3f, -5f), Quaternion.Euler(0f, 120f, 0f)); // un rig corrido, como tras caer o derivar

            rig.Controller.LockPlayerStationary();

            Check(characterController == null || !characterController.enabled, "LockPlayerStationary con follower: el CharacterController queda desactivado");

            // Pose esperada del follower: yaw -30 y la cabeza (0,3, -0,4) en el espacio del rig cae sobre el anclaje.
            Quaternion expectedRotation = rig.Viewpoint.rotation * Quaternion.Euler(0f, -30f, 0f);
            Vector3 expectedPosition = rig.Viewpoint.position - expectedRotation * new Vector3(0.3f, 0f, -0.4f);
            Check(Vector3.Distance(rig.Origin.position, expectedPosition) < 1e-3f && Quaternion.Angle(rig.Origin.rotation, expectedRotation) < 0.1f,
                $"LockPlayerStationary con follower: el rig queda en la pose del follower, no en cero (rig en {rig.Origin.position})");
        }

        // Sin DriverSeatFollower se conserva el comportamiento anterior: el controlador fija el rig en (0, 0, 0).
        private static void VerifyFallbackWithoutFollower()
        {
            DriverSeatFollower.SetVRActiveOverrideForTests(null);
            var rigObject = new GameObject("TMP_RigWithoutFollower");
            var controllerObject = new GameObject("TMP_ControllerWithoutFollower");
            try
            {
                rigObject.transform.SetPositionAndRotation(new Vector3(3f, 2f, 4f), Quaternion.Euler(0f, 40f, 0f));
                NewMainMenuController controller = controllerObject.AddComponent<NewMainMenuController>();
                var so = new SerializedObject(controller);
                so.FindProperty("m_XROriginTransform").objectReferenceValue = rigObject.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                InvokeLateUpdate(controller);
                Check(rigObject.transform.position == Vector3.zero && rigObject.transform.rotation == Quaternion.identity,
                    "Sin follower: el LateUpdate del controlador deja el rig en (0, 0, 0) con rotación identidad");

                rigObject.transform.SetPositionAndRotation(new Vector3(-6f, 1f, 9f), Quaternion.Euler(0f, 200f, 0f));
                controller.LockPlayerStationary();
                Check(rigObject.transform.position == Vector3.zero && rigObject.transform.rotation == Quaternion.identity,
                    "Sin follower: LockPlayerStationary deja el rig en (0, 0, 0) con rotación identidad");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(rigObject);
            }
        }

        // Un follower que no puede mover el rig (deshabilitado, sin seguir el asiento o sin anclaje) no debe dejar el rig suelto:
        // el controlador vuelve a fijarlo en (0, 0, 0).
        private static void VerifyFallbackWhenFollowerCannotMoveRig(MenuRig rig)
        {
            DriverSeatFollower.SetVRActiveOverrideForTests(false);
            rig.Controller.LockPlayerStationary(); // cachea el follower

            void ExpectRigAtZero(string label)
            {
                rig.Origin.SetPositionAndRotation(new Vector3(3f, 1f, -2f), Quaternion.Euler(0f, 50f, 0f));
                InvokeLateUpdate(rig.Controller);
                Check(rig.Origin.position == Vector3.zero && rig.Origin.rotation == Quaternion.identity,
                    $"{label}: el LateUpdate del controlador deja el rig en (0, 0, 0) con rotación identidad");

                rig.Origin.SetPositionAndRotation(new Vector3(-4f, 2f, 5f), Quaternion.Euler(0f, 160f, 0f));
                rig.Controller.LockPlayerStationary();
                Check(rig.Origin.position == Vector3.zero && rig.Origin.rotation == Quaternion.identity,
                    $"{label}: LockPlayerStationary deja el rig en (0, 0, 0) con rotación identidad");
            }

            rig.Follower.enabled = false;
            ExpectRigAtZero("Follower deshabilitado");
            rig.Follower.enabled = true;

            rig.Follower.FollowSeat = false;
            ExpectRigAtZero("Follower con FollowSeat = false");
            rig.Follower.FollowSeat = true;

            rig.Follower.SetDriverSeat(null, false);
            ExpectRigAtZero("Follower sin DriverSeat (falta MenuViewpoint)");
            rig.Follower.SetDriverSeat(rig.Viewpoint, true);

            // Con el follower restablecido, el controlador vuelve a dejarle el rig.
            rig.Origin.SetPositionAndRotation(new Vector3(8f, 0f, 8f), Quaternion.identity);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
            SetHeadPose(rig, new Vector3(0.3f, 1.6f, -0.4f), Quaternion.Euler(0f, 30f, 0f));
            rig.Follower.RecenterHeadPose();
            InvokeLateUpdate(rig.Controller);
            Check(rig.Origin.position.sqrMagnitude > 1e-4f && HorizontalDistance(rig.Camera.position, rig.Viewpoint.position) < POSITION_TOLERANCE,
                "Con el follower restablecido, el LateUpdate del controlador vuelve a no fijar el rig");
        }

        private static void VerifyDesktopMode(MenuRig rig)
        {
            DriverSeatFollower.SetVRActiveOverrideForTests(false);
            rig.Camera.localPosition = Vector3.zero;
            rig.Camera.localRotation = Quaternion.identity;
            rig.Origin.SetPositionAndRotation(new Vector3(2f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f));
            rig.Follower.SnapToSeat();
            InvokeLateUpdate(rig.Controller);

            Check(Vector3.Distance(rig.Origin.position, rig.Viewpoint.position) < 1e-4f && Quaternion.Angle(rig.Origin.rotation, rig.Viewpoint.rotation) < 1e-3f,
                "Escritorio: el rig queda en MenuViewpoint con su rotación, sin compensación");

            // Misma regla que el follower: CameraYOffset, o 1,36144 si vale menos de 0,001.
            float cameraYOffset = rig.OriginObject.GetComponent<XROrigin>().CameraYOffset;
            float expectedHeight = cameraYOffset > 0.001f ? cameraYOffset : 1.36144f;
            Check(Mathf.Abs(rig.CameraOffset.localPosition.y - expectedHeight) < 1e-3f,
                $"Escritorio: el Camera Offset queda en la altura de los ojos {expectedHeight:F4} (actual: {rig.CameraOffset.localPosition.y:F4})");

            // El mirar con mouse o gamepad rota el Camera Offset: se descarta esa rotación para no depender de la entrada.
            rig.CameraOffset.localRotation = Quaternion.identity;
            Check(Vector3.Distance(rig.Camera.position, new Vector3(0f, expectedHeight, 0f)) < 1e-3f
                    && Vector3.Angle(HorizontalForward(rig.Camera), Vector3.forward) < ANGLE_TOLERANCE,
                $"Escritorio: la cámara queda en (0, {expectedHeight:F2}, 0) mirando a +Z, como antes");
        }

        // Correr el setup dos veces sobre la copia no duplica MenuViewpoint ni el follower, y deja el menú de pausa enlazado.
        private static void VerifySetupIsIdempotent(Scene scene)
        {
            DriverSeatFollower.SetVRActiveOverrideForTests(null);
            SceneManager.SetActiveScene(scene);
            string path = scene.path;

            for (int run = 1; run <= 2; run++)
            {
                NewMainMenuController built = NewMainMenuSetup.BuildInActiveScene(path);
                Check(built != null, $"Idempotencia: BuildInActiveScene #{run} construye el menú");

                int viewpoints = scene.GetRootGameObjects().Count(r => r.name == NewMainMenuSetup.MENU_VIEWPOINT_NAME);
                DriverSeatFollower[] followers = VerificationSceneUtil.AllInScene<DriverSeatFollower>(scene);
                Check(viewpoints == 1 && followers.Length == 1,
                    $"Idempotencia: tras la corrida #{run} hay 1 MenuViewpoint ({viewpoints}) y 1 DriverSeatFollower ({followers.Length})");

                InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).FirstOrDefault();
                Check(followers.Length == 1 && followers[0].DriverSeat != null && followers[0].DriverSeat.name == NewMainMenuSetup.MENU_VIEWPOINT_NAME
                        && menu != null && menu.ForwardReference == followers[0].DriverSeat && !menu.ShowCalibrationButton,
                    $"Idempotencia: tras la corrida #{run} el follower sigue anclado a MenuViewpoint y el menú de pausa lo usa de referencia");
            }
        }

        // ------------------------------------------------------------------ Utilidades

        private static void EnterVR()
        {
            DriverSeatFollower.SetTrackingOriginModeOverrideForTests(TrackingOriginModeFlags.Floor);
            DriverSeatFollower.SetAppFocusedOverrideForTests(true);
            DriverSeatFollower.SetVRActiveOverrideForTests(true);
        }

        // Pose de la cabeza relativa al Camera Offset (lo que entrega el TrackedPoseDriver).
        private static void SetHeadPose(MenuRig rig, Vector3 localPosition, Quaternion localRotation)
        {
            rig.Camera.localPosition = localPosition;
            rig.Camera.localRotation = localRotation;
            rig.Follower.SnapToSeat();
        }

        private static void InvokeLateUpdate(NewMainMenuController controller)
        {
            MethodInfo lateUpdate = typeof(NewMainMenuController).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            lateUpdate.Invoke(controller, null);
        }

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
