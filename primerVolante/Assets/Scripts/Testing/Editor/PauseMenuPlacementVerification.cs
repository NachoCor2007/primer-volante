using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PrimerVolante.Lessons;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la pose estándar del menú de pausa y del aviso de desconexión: siempre a 45° a la derecha del
    /// frente actual del auto, medido desde la posición actual de los ojos (la orientación de la cabeza se ignora).
    /// 1.  La referencia de frente se resuelve de forma perezosa en la escena del menú (sin Awake), sin tomar un auto de otra escena.
    /// 2.  OpenMenu con la cabeza al frente, girada y mirando arriba o abajo.
    /// 3.  Auto rotado antes de abrir: el menú acompaña el frente del auto, no el del mundo.
    /// 4.  Cabeza desplazada: el menú se traslada con la cabeza y mantiene el ángulo.
    /// 5.  OpenMenuWithNotice (cerrado, menú del jugador abierto, calibración standalone abierta).
    /// 6.  RecenterConnectionPauseMenu durante y fuera de la pausa.
    /// 7.  La calibración con el menú abierto traslada el canvas el mismo delta de mundo que la vista.
    /// 8.  Sin referencia (NewMainMenu): frente de la cabeza a 0°, como antes.
    /// 9.  Campos renombrados (FormerlySerializedAs): el valor se preserva desde las escenas.
    /// Todo sobre copias temporales de las escenas (<see cref="VerificationSceneUtil.WithSceneCopy"/>).
    /// </summary>
    public static class PauseMenuPlacementVerification
    {
        private const string LESSON_SCENE = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";
        private const string NEW_MAIN_MENU_SCENE = "Assets/Scenes/car09/NewMainMenu.unity";
        private const string MAIN_MENU_SCENE = "Assets/Scenes/car09/MainMenu.unity";
        private const float YAW_TOLERANCE = 2f;
        private const float DISTANCE_TOLERANCE = 0.01f;
        private const BindingFlags k_Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Verify Pause Menu Placement")]
        public static void RunVerification()
        {
            s_Total = 0;
            s_Passed = 0;
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE LA POSE ESTANDAR DEL MENU DE PAUSA (RESPECTO DEL AUTO)");
            Debug.Log("=========================================================");

            float timeScaleBefore = Time.timeScale;
            bool audioPauseBefore = AudioListener.pause;
            Scene previousActive = SceneManager.GetActiveScene();
            try
            {
                VerificationSceneUtil.WithSceneCopy(LESSON_SCENE, VerifyLazyReference);
                VerificationSceneUtil.WithSceneCopy(LESSON_SCENE, VerifyOpenMenuPlacement);
                VerificationSceneUtil.WithSceneCopy(LESSON_SCENE, VerifyOpenMenuWithNotice);
                VerificationSceneUtil.WithSceneCopy(LESSON_SCENE, VerifyRecenter);
                VerificationSceneUtil.WithSceneCopy(LESSON_SCENE, VerifyCalibrationFollowsView);
                VerificationSceneUtil.WithSceneCopy(NEW_MAIN_MENU_SCENE, VerifyWithoutReference);
                VerifyRenamedFields();
                VerificationSceneUtil.WithSceneCopy(MAIN_MENU_SCENE, VerifyFormerlySerializedValue);
                Check(!SimulationPause.IsPaused, "Al terminar no queda ningún dueño de la pausa de simulación");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FAIL] PauseMenuPlacementVerification: excepción no controlada: {ex}");
            }
            finally
            {
                SimulationPause.ResetForTests();
                Time.timeScale = timeScaleBefore;
                AudioListener.pause = audioPauseBefore;
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }

            Debug.Log("=========================================================");
            if (s_Total > 0 && s_Passed == s_Total) Debug.Log($"PAUSE MENU PLACEMENT: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            else Debug.LogError($"[FAIL] PAUSE MENU PLACEMENT: {s_Passed}/{s_Total} pruebas pasadas");
            Debug.Log("=========================================================");
        }

        // ------------------------------------------------------------------ contexto

        private sealed class Rig
        {
            public InGameMenuController Menu;
            public Transform Head;
            public Transform Car;
            public float Distance;
            public float YawOffset;
            public Vector3 HeadPosition;
            public Quaternion HeadRotation;
            public Quaternion CarRotation;
        }

        private static Rig BuildRig(Scene scene, string label)
        {
            InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).FirstOrDefault();
            Check(menu != null && menu.MenuRoot != null, $"{label}: hay un InGameMenuController con su Canvas");
            if (menu == null || menu.MenuRoot == null) return null;

            var so = new SerializedObject(menu);
            var head = so.FindProperty("m_HeadTransform").objectReferenceValue as Transform;
            Check(head != null, $"{label}: el menú tiene referencia a la cabeza");
            if (head == null) return null;

            menu.MenuRoot.SetActive(false);
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SimulationPause.ResetForTests();

            Transform car = menu.ForwardReference;
            return new Rig
            {
                Menu = menu,
                Head = head,
                Car = car,
                Distance = so.FindProperty("m_DistanceFromHead").floatValue,
                YawOffset = so.FindProperty("m_YawOffsetDegrees").floatValue,
                HeadPosition = head.position,
                HeadRotation = head.rotation,
                CarRotation = car != null ? car.rotation : Quaternion.identity
            };
        }

        // Deja el rig como lo encontró y el menú cerrado y sin pausa (un caso no contamina al siguiente).
        private static void Reset(Rig rig)
        {
            rig.Head.position = rig.HeadPosition;
            rig.Head.rotation = rig.HeadRotation;
            if (rig.Car != null) rig.Car.rotation = rig.CarRotation;
            rig.Menu.MenuRoot.SetActive(false);
            SimulationPause.ResetForTests();
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        // Posición esperada de la pose estándar: cabeza actual + frente horizontal actual del auto rotado yawOffset.
        private static Vector3 ExpectedPosition(Rig rig)
        {
            return rig.Head.position + Quaternion.AngleAxis(rig.YawOffset, Vector3.up) * Flat(rig.Car.forward) * rig.Distance;
        }

        private static void CheckStandardPose(Rig rig, string label)
        {
            Transform menuRoot = rig.Menu.MenuRoot.transform;
            Vector3 toMenu = menuRoot.position - rig.Head.position;
            float yaw = Vector3.SignedAngle(Flat(rig.Car.forward), Flat(toMenu), Vector3.up);
            Check(Mathf.Abs(yaw - 45f) <= YAW_TOLERANCE, $"{label}: a 45° (±{YAW_TOLERANCE:F0}°) a la derecha del frente del auto (yaw = {yaw:F1}°)");
            Check(Mathf.Abs(toMenu.magnitude - rig.Distance) < DISTANCE_TOLERANCE, $"{label}: a {rig.Distance:F1} m de los ojos ({toMenu.magnitude:F3} m)");
            Check(Mathf.Abs(toMenu.y) < 0.001f, $"{label}: a la altura de los ojos (dy = {toMenu.y:F4})");
            Check(Vector3.Dot(menuRoot.forward, toMenu.normalized) > 0.99f, $"{label}: el panel mira a la cabeza (misma convención que antes)");
        }

        private static void SetHead(Rig rig, float pitch, float yaw)
        {
            rig.Head.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // ------------------------------------------------------------------ 1. referencia perezosa

        private static void VerifyLazyReference(Scene scene)
        {
            // Antes de BuildRig: leer ForwardReference cachea el resultado en el campo.
            InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).FirstOrDefault();
            Check(menu != null, "Lesson_01 (copia): hay un InGameMenuController");
            if (menu == null) return;
            var field = typeof(InGameMenuController).GetField("m_ForwardReference", k_Instance);
            Check(field != null && field.GetValue(menu) as Transform == null,
                "Precondición: sin Awake (Edit Mode) m_ForwardReference está vacío en la escena");

            VehicleController vehicle = VerificationSceneUtil.AllInScene<VehicleController>(scene).FirstOrDefault();
            Check(vehicle != null, "La escena de la lección tiene un VehicleController");
            if (vehicle == null) return;

            Transform resolved = menu.ForwardReference;
            Check(resolved == vehicle.transform, "ForwardReference resuelve el VehicleController de la escena de la lección sin que haya corrido Awake");

            // Un auto de otra escena cargada no se toma: se borra el de la lección y se crea uno señuelo en una escena aparte.
            Scene other = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var decoy = new GameObject("TMP_DecoyVehicle");
                SceneManager.MoveGameObjectToScene(decoy, other);
                decoy.AddComponent<VehicleController>();
                Transform seat = (new SerializedObject(menu).FindProperty("m_DriverSeatFollower").objectReferenceValue as DriverSeatFollower)?.DriverSeat;

                field.SetValue(menu, null);
                UnityEngine.Object.DestroyImmediate(vehicle.gameObject);
                Transform afterDelete = menu.ForwardReference;
                Check(afterDelete != decoy.transform, "Sin auto en la escena del menú no se toma el VehicleController de otra escena cargada");
                Check(afterDelete == seat || afterDelete == null,
                    $"Sin auto en la escena: resuelve al DriverSeat del DriverSeatFollower o queda en null ({(afterDelete != null ? afterDelete.name : "null")})");

                // Referencia cacheada y luego destruida: se vuelve a resolver (no queda un objeto muerto).
                field.SetValue(menu, decoy.transform);
                UnityEngine.Object.DestroyImmediate(decoy);
                Check(menu.ForwardReference != decoy, "Con la referencia destruida la propiedad no devuelve el objeto muerto");
            }
            finally
            {
                EditorSceneManager.CloseScene(other, true);
            }
        }

        // ------------------------------------------------------------------ 2-4. OpenMenu

        private static void VerifyOpenMenuPlacement(Scene scene)
        {
            Rig rig = BuildRig(scene, "OpenMenu");
            if (rig == null) return;
            Check(rig.Car != null, "OpenMenu: el menú tiene el auto como referencia de frente");
            if (rig.Car == null) return;
            InGameMenuController menu = rig.Menu;
            Quaternion carYaw0 = Quaternion.Euler(0f, rig.Car.eulerAngles.y, 0f);

            try
            {
                // 2. La mirada no cambia la pose.
                float carY = rig.Car.eulerAngles.y;
                foreach ((float pitch, float yaw, string name) in new[]
                {
                    (0f, carY, "cabeza al frente"), (0f, carY + 90f, "cabeza girada +90°"), (0f, carY - 90f, "cabeza girada -90°"),
                    (0f, carY + 180f, "cabeza girada 180°"), (60f, carY, "cabeza mirando abajo 60°"), (-60f, carY + 90f, "cabeza mirando arriba 60° y girada")
                })
                {
                    SetHead(rig, pitch, yaw);
                    menu.CloseMenu();
                    menu.OpenMenu();
                    CheckStandardPose(rig, $"OpenMenu ({name})");
                }

                // 3. Auto rotado: el menú acompaña el nuevo frente, no el de la escena ni el del mundo.
                SetHead(rig, 0f, carY);
                foreach (float turn in new[] { 90f, 137f })
                {
                    menu.CloseMenu();
                    rig.Car.rotation = Quaternion.Euler(0f, carY + turn, 0f);
                    menu.OpenMenu();
                    CheckStandardPose(rig, $"OpenMenu (auto rotado {turn:F0}°)");

                    Vector3 toMenu = menu.MenuRoot.transform.position - rig.Head.position;
                    float yawVsInitial = Vector3.SignedAngle(Flat(carYaw0 * Vector3.forward), Flat(toMenu), Vector3.up);
                    float expected = Mathf.DeltaAngle(0f, 45f + turn);
                    Check(Mathf.Abs(Mathf.DeltaAngle(yawVsInitial, expected)) <= YAW_TOLERANCE,
                        $"OpenMenu (auto rotado {turn:F0}°): respecto del frente inicial del auto está a {expected:F0}°, no a 45° (yaw = {yawVsInitial:F1}°)");
                }

                // 4. Cabeza desplazada: el menú se traslada con ella y mantiene el ángulo.
                rig.Car.rotation = rig.CarRotation;
                menu.CloseMenu();
                SetHead(rig, 0f, carY);
                menu.OpenMenu();
                Vector3 relative = menu.MenuRoot.transform.position - rig.Head.position;
                foreach ((Vector3 delta, string name) in new[]
                {
                    (rig.Head.right * 0.3f, "0,3 m a un costado"), (Flat(rig.Head.forward) * 0.3f, "0,3 m hacia adelante"), (new Vector3(2f, 0.2f, -3f), "2 m y 3 m")
                })
                {
                    menu.CloseMenu();
                    rig.Head.position = rig.HeadPosition + delta;
                    menu.OpenMenu();
                    CheckStandardPose(rig, $"OpenMenu (cabeza desplazada {name})");
                    Check(Vector3.Distance(menu.MenuRoot.transform.position - rig.Head.position, relative) < DISTANCE_TOLERANCE,
                        $"OpenMenu (cabeza desplazada {name}): la posición relativa a la cabeza es la misma");
                }
            }
            finally
            {
                menu.CloseMenu();
                Reset(rig);
            }
        }

        // ------------------------------------------------------------------ 5. OpenMenuWithNotice

        private static void VerifyOpenMenuWithNotice(Scene scene)
        {
            Rig rig = BuildRig(scene, "OpenMenuWithNotice");
            if (rig == null || rig.Car == null) return;
            InGameMenuController menu = rig.Menu;
            float carY = rig.Car.eulerAngles.y;

            try
            {
                // Cerrado -> pose estándar.
                SetHead(rig, 20f, carY + 120f);
                rig.Head.position = rig.HeadPosition + new Vector3(1f, 0f, 1f);
                menu.OpenMenuWithNotice("test");
                Check(menu.IsOpen && menu.IsBlockingGameplayInput, "OpenMenuWithNotice (cerrado): abre el menú con la pausa de conexión");
                CheckStandardPose(rig, "OpenMenuWithNotice (menú cerrado, cabeza girada 120°)");
                menu.EndConnectionPause();
                menu.CloseMenu();
                Reset(rig);

                // Menú del jugador abierto (modo Normal) -> no se mueve.
                menu.OpenMenu();
                Vector3 position = menu.MenuRoot.transform.position;
                Quaternion rotation = menu.MenuRoot.transform.rotation;
                rig.Head.position = rig.HeadPosition + new Vector3(0f, 0f, 5f);
                SetHead(rig, 0f, carY - 90f);
                rig.Car.rotation = Quaternion.Euler(0f, carY + 60f, 0f);
                menu.OpenMenuWithNotice("test");
                Check(menu.MenuRoot.transform.position == position && menu.MenuRoot.transform.rotation == rotation,
                    "OpenMenuWithNotice (menú del jugador abierto): el menú queda donde estaba");
                menu.EndConnectionPause();
                menu.CloseMenu();
                Reset(rig);

                // Calibración standalone abierta -> pose estándar.
                menu.OpenCalibrationStandalone();
                rig.Head.position = rig.HeadPosition + new Vector3(-2f, 0.3f, 4f);
                SetHead(rig, 0f, carY + 200f);
                rig.Car.rotation = Quaternion.Euler(0f, carY - 40f, 0f);
                menu.OpenMenuWithNotice("test");
                CheckStandardPose(rig, "OpenMenuWithNotice (calibración standalone abierta, cabeza y auto movidos)");
                menu.EndConnectionPause();
                Check(menu.IsOpen, "OpenMenuWithNotice (standalone): al reanudar vuelve la calibración standalone");
                CheckStandardPose(rig, "Restaurar standalone tras la pausa");
            }
            finally
            {
                menu.EndConnectionPause();
                menu.CloseMenu();
                Reset(rig);
            }
        }

        // ------------------------------------------------------------------ 6. RecenterConnectionPauseMenu

        private static void VerifyRecenter(Scene scene)
        {
            Rig rig = BuildRig(scene, "RecenterConnectionPauseMenu");
            if (rig == null || rig.Car == null) return;
            InGameMenuController menu = rig.Menu;
            float carY = rig.Car.eulerAngles.y;

            try
            {
                menu.OpenMenuWithNotice("test");
                rig.Head.position = rig.HeadPosition + new Vector3(-6f, 0.5f, 25f);
                SetHead(rig, 10f, carY + 250f);
                rig.Car.rotation = Quaternion.Euler(0f, carY + 33f, 0f);
                Check(Vector3.Distance(menu.MenuRoot.transform.position, ExpectedPosition(rig)) > 5f,
                    "Recentrado: la pose del corte quedó lejos de la actual (precondición)");
                menu.RecenterConnectionPauseMenu();
                CheckStandardPose(rig, "RecenterConnectionPauseMenu (durante la pausa, cabeza y auto movidos)");

                menu.EndConnectionPause();
                menu.CloseMenu();
                Reset(rig);

                // Fuera de la pausa no hace nada.
                menu.OpenMenu();
                Vector3 position = menu.MenuRoot.transform.position;
                rig.Head.position = rig.HeadPosition + new Vector3(3f, 0f, 3f);
                rig.Car.rotation = Quaternion.Euler(0f, carY + 90f, 0f);
                menu.RecenterConnectionPauseMenu();
                Check(menu.MenuRoot.transform.position == position, "RecenterConnectionPauseMenu fuera de la pausa por conexión no mueve el menú");
                menu.CloseMenu();
                menu.RecenterConnectionPauseMenu();
                Check(!menu.IsOpen, "RecenterConnectionPauseMenu con el menú cerrado no lo abre");
            }
            finally
            {
                menu.EndConnectionPause();
                menu.CloseMenu();
                Reset(rig);
            }
        }

        // ------------------------------------------------------------------ 7. calibración

        private static void VerifyCalibrationFollowsView(Scene scene)
        {
            Rig rig = BuildRig(scene, "Calibración");
            if (rig == null) return;
            InGameMenuController menu = rig.Menu;
            var follower = new SerializedObject(menu).FindProperty("m_DriverSeatFollower").objectReferenceValue as DriverSeatFollower;
            Check(follower != null && follower.DriverSeat != null, "Calibración: el menú tiene DriverSeatFollower con asiento");
            if (follower == null || follower.DriverSeat == null) return;

            // OnEnable (suscripción al evento) no corre en Edit Mode.
            MethodInfo onEnable = typeof(InGameMenuController).GetMethod("OnEnable", k_Instance);
            MethodInfo onDisable = typeof(InGameMenuController).GetMethod("OnDisable", k_Instance);
            onEnable.Invoke(menu, null);
            try
            {
                menu.OpenMenu();
                menu.ShowCalibrationPanel();
                Transform menuRoot = menu.MenuRoot.transform;
                Vector3 start = menuRoot.position;
                Vector3 viewStart = follower.DriverSeat.TransformPoint(follower.CalibrationOffset);
                Vector3 relativeStart = menuRoot.position - viewStart;

                for (int axis = 0; axis < 3; axis++)
                {
                    Vector3 before = menuRoot.position;
                    Vector3 offsetBefore = follower.CalibrationOffset;
                    Vector3 viewBefore = follower.DriverSeat.TransformPoint(offsetBefore);
                    float value = Mathf.Clamp(offsetBefore[axis] + 0.06f, follower.CalibrationLimitsMin[axis], follower.CalibrationLimitsMax[axis]);
                    follower.SetCalibrationAxis(axis, value);
                    Vector3 viewDelta = follower.DriverSeat.TransformPoint(follower.CalibrationOffset) - viewBefore;
                    Check(viewDelta.magnitude > 0.001f, $"Calibración eje {axis}: la vista se desplazó ({viewDelta.magnitude:F3} m)");
                    Check(Vector3.Distance(menuRoot.position - before, viewDelta) < 1e-4f,
                        $"Calibración eje {axis}: el canvas se trasladó el mismo delta de mundo que la vista");
                }

                Vector3 viewNow = follower.DriverSeat.TransformPoint(follower.CalibrationOffset);
                Check(Vector3.Distance(menuRoot.position - viewNow, relativeStart) < 1e-4f,
                    "Calibración: tras mover los 3 ejes la pose del menú relativa a la vista se conserva");

                follower.ResetCalibration();
                Check(Vector3.Distance(menuRoot.position, start) < 1e-4f, "ResetCalibration: el canvas vuelve a su posición inicial junto con la vista");
            }
            finally
            {
                follower.ResetCalibration();
                onDisable.Invoke(menu, null);
                menu.CloseMenu();
                Reset(rig);
            }
        }

        // ------------------------------------------------------------------ 8. sin referencia

        private static void VerifyWithoutReference(Scene scene)
        {
            Rig rig = BuildRig(scene, "NewMainMenu (copia)");
            if (rig == null) return;
            InGameMenuController menu = rig.Menu;
            Check(rig.Car == null, "NewMainMenu: no hay auto ni asiento, la referencia de frente queda en null");

            try
            {
                foreach (float yaw in new[] { 0f, 90f, -130f })
                {
                    menu.CloseMenu();
                    rig.Head.rotation = Quaternion.Euler(15f, yaw, 0f);
                    rig.Head.position = rig.HeadPosition + new Vector3(yaw * 0.01f, 0f, 0.4f);
                    menu.OpenMenuWithNotice("test");
                    Vector3 toMenu = menu.MenuRoot.transform.position - rig.Head.position;
                    float angle = Vector3.SignedAngle(Flat(rig.Head.forward), Flat(toMenu), Vector3.up);
                    Check(Mathf.Abs(angle) <= YAW_TOLERANCE, $"Sin referencia (cabeza girada {yaw:F0}°): el menú queda al frente de la cabeza, a 0° (yaw = {angle:F1}°)");
                    Check(Mathf.Abs(toMenu.magnitude - rig.Distance) < DISTANCE_TOLERANCE && Mathf.Abs(toMenu.y) < 0.001f,
                        $"Sin referencia (cabeza girada {yaw:F0}°): a {rig.Distance:F1} m y a la altura de los ojos");
                    menu.EndConnectionPause();
                }
            }
            finally
            {
                menu.EndConnectionPause();
                menu.CloseMenu();
                Reset(rig);
            }
        }

        // ------------------------------------------------------------------ 9. campos renombrados

        private static void VerifyRenamedFields()
        {
            Type type = typeof(InGameMenuController);
            FieldInfo reference = type.GetField("m_ForwardReference", k_Instance);
            FieldInfo yaw = type.GetField("m_YawOffsetDegrees", k_Instance);
            Check(reference != null && reference.FieldType == typeof(Transform), "Campo m_ForwardReference existe");
            Check(yaw != null && yaw.FieldType == typeof(float), "Campo m_YawOffsetDegrees existe");
            Check(type.GetField("m_StandaloneForwardReference", k_Instance) == null, "El nombre viejo m_StandaloneForwardReference ya no existe");
            Check(type.GetField("m_StandaloneYawOffsetDegrees", k_Instance) == null, "El nombre viejo m_StandaloneYawOffsetDegrees ya no existe");
            Check(reference != null && reference.GetCustomAttribute<FormerlySerializedAsAttribute>()?.oldName == "m_StandaloneForwardReference",
                "m_ForwardReference conserva el nombre viejo con FormerlySerializedAs");
            Check(yaw != null && yaw.GetCustomAttribute<FormerlySerializedAsAttribute>()?.oldName == "m_StandaloneYawOffsetDegrees",
                "m_YawOffsetDegrees conserva el nombre viejo con FormerlySerializedAs");

            foreach (string path in LessonHUDMinimizeSetup.LESSON_SCENE_PATHS)
            {
                string label = Path.GetFileNameWithoutExtension(path);
                VerificationSceneUtil.WithScene(path, s =>
                {
                    InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(s).FirstOrDefault();
                    Check(menu != null && Mathf.Approximately(new SerializedObject(menu).FindProperty("m_YawOffsetDegrees").floatValue, 45f),
                        $"{label}: m_YawOffsetDegrees conserva 45 desde la escena");
                });
            }
        }

        // MainMenu.unity guarda la referencia con el nombre viejo: si se carga, FormerlySerializedAs la migró.
        private static void VerifyFormerlySerializedValue(Scene scene)
        {
            string yamlPath = Path.Combine(Directory.GetCurrentDirectory(), MAIN_MENU_SCENE);
            bool oldNameOnDisk = File.Exists(yamlPath) && File.ReadAllText(yamlPath).Contains("m_StandaloneForwardReference: {fileID:");
            InGameMenuController menu = VerificationSceneUtil.AllInScene<InGameMenuController>(scene).FirstOrDefault();
            Check(menu != null, "MainMenu (copia): hay un InGameMenuController");
            if (menu == null) return;

            var reference = new SerializedObject(menu).FindProperty("m_ForwardReference").objectReferenceValue as Transform;
            if (oldNameOnDisk)
                Check(reference != null, "MainMenu: la referencia guardada como m_StandaloneForwardReference se carga en m_ForwardReference (FormerlySerializedAs)");
            else
                Debug.Log("  [INFO] MainMenu.unity ya no guarda el nombre viejo: se omite la comprobación de migración de la referencia");
        }

        private static void Check(bool condition, string testName)
        {
            s_Total++;
            if (condition)
            {
                s_Passed++;
                Debug.Log($"  [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"[FAIL] {testName}");
            }
        }
    }
}
