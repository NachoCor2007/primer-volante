using System;
using System.Collections.Generic;
using System.Reflection;
using PrimerVolante.VR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la persistencia de la calibración de vista (<see cref="DriverViewCalibrationStore"/> +
    /// <see cref="DriverSeatFollower"/> + <see cref="DriverViewCalibrationUI"/>): carga, guardado, recorte a los límites,
    /// valores inválidos, "Restablecer", el flag del menú principal, los volcados a disco y que en Edit Mode real no se
    /// toquen los PlayerPrefs. Todo corre con el backend en memoria del store, salvo la prueba de los PlayerPrefs reales.
    /// En Edit Mode los componentes no reciben Awake/OnDisable/OnApplicationPause: se invocan por reflexión.
    /// </summary>
    public static class PersistentCalibrationVerification
    {
        private const float EPSILON = 1e-5f;
        private const string LESSON_01_SCENE_PATH = "Assets/Scenes/Lessons/Lesson_01_BasicDriving.unity";

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Verify Persistent Calibration")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DE LA PERSISTENCIA DE LA CALIBRACION DE VISTA (DriverViewCalibrationStore)");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;
            var created = new List<GameObject>();
            try
            {
                DriverViewCalibrationStore.UseInMemoryBackendForTests(true);
                VerifyNothingSaved(created);
                VerifySaveAndReload(created);
                VerifyLoadClampsToLimits(created);
                VerifyInvalidValuesIgnored(created);
                VerifyResetPersistsZero(created);
                VerifyFlagOffIgnoresStore(created);
                VerifyLoadDoesNotRaiseEvent(created);
                VerifyFlushBehavior(created);
                VerifyUiDisableFlushes(created);
                VerifyClear(created);
                VerifySceneFlags();
                VerifyEditModeDoesNotTouchPlayerPrefs(created);
            }
            catch (Exception ex)
            {
                Check(false, $"Excepción no controlada durante las pruebas: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                DriverViewCalibrationStore.UseInMemoryBackendForTests(false);
                foreach (GameObject go in created)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
            }

            Debug.Log("=========================================================");
            if (s_Passed == s_Total) Debug.Log($"RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas con exito!");
            else Debug.LogError($"[FAIL] RESULTADO FINAL: {s_Passed}/{s_Total} pruebas pasadas");
            Debug.Log("=========================================================");
        }

        // ------------------------------------------------------------------ Escenarios

        private static void VerifyNothingSaved(List<GameObject> created)
        {
            ResetStore();
            Check(!DriverViewCalibrationStore.TryLoad(out _), "Sin nada guardado, TryLoad es false");
            Check(!DriverViewCalibrationStore.HasSaved, "Sin nada guardado, HasSaved es false");

            DriverSeatFollower follower = BuildFollower(created);
            Check(!follower.LoadPersistentCalibration(), "Sin nada guardado, LoadPersistentCalibration devuelve false");
            Check(follower.CalibrationOffset == Vector3.zero, "Sin nada guardado, el offset queda en 0");
        }

        private static void VerifySaveAndReload(List<GameObject> created)
        {
            ResetStore();
            DriverSeatFollower first = BuildFollower(created);
            first.SetCalibrationAxis(1, 0.1f);
            first.SetCalibrationAxis(2, -0.05f);

            Check(DriverViewCalibrationStore.HasSaved, "SetCalibrationAxis en un follower con persistencia guarda la calibración");

            DriverSeatFollower second = BuildFollower(created);
            Check(second.LoadPersistentCalibration(), "Un follower nuevo carga la calibración guardada");
            Check(Approximately(second.CalibrationOffset, new Vector3(0f, 0.1f, -0.05f)),
                $"El follower nuevo carga el mismo offset (obtuvo {second.CalibrationOffset})");

            // Un cambio posterior sobrescribe lo guardado.
            second.SetCalibrationAxis(0, 0.2f);
            DriverViewCalibrationStore.InvalidateCacheForTests();
            DriverViewCalibrationStore.TryLoad(out Vector3 reloaded);
            Check(Approximately(reloaded, new Vector3(0.2f, 0.1f, -0.05f)), "Lo guardado se actualiza y se relee del backend");
        }

        private static void VerifyLoadClampsToLimits(List<GameObject> created)
        {
            ResetStore();
            DriverViewCalibrationStore.Save(new Vector3(1f, -1f, 1f));

            DriverSeatFollower follower = BuildFollower(created);
            Check(follower.LoadPersistentCalibration(), "Un valor fuera de límites guardado se carga");
            Vector3 expected = new Vector3(follower.CalibrationLimitsMax.x, follower.CalibrationLimitsMin.y, follower.CalibrationLimitsMax.z);
            Check(Approximately(follower.CalibrationOffset, expected),
                $"Lo guardado fuera de los límites se recorta al cargar (obtuvo {follower.CalibrationOffset})");
        }

        private static void VerifyInvalidValuesIgnored(List<GameObject> created)
        {
            ResetStore();
            DriverViewCalibrationStore.Save(new Vector3(0.1f, float.NaN, 0.1f));
            DriverViewCalibrationStore.InvalidateCacheForTests();
            Check(!DriverViewCalibrationStore.TryLoad(out _), "Un componente NaN guardado se ignora (TryLoad es false)");

            DriverSeatFollower follower = BuildFollower(created);
            Check(!follower.LoadPersistentCalibration() && follower.CalibrationOffset == Vector3.zero,
                "Con NaN guardado el follower queda en offset 0");

            DriverViewCalibrationStore.Save(new Vector3(float.PositiveInfinity, 0f, 0f));
            DriverViewCalibrationStore.InvalidateCacheForTests();
            Check(!DriverViewCalibrationStore.TryLoad(out _), "Un componente infinito guardado se ignora");
        }

        private static void VerifyResetPersistsZero(List<GameObject> created)
        {
            ResetStore();
            DriverSeatFollower first = BuildFollower(created);
            first.SetCalibrationAxis(0, 0.12f);
            first.SetCalibrationAxis(1, 0.08f);
            first.ResetCalibration();

            Check(DriverViewCalibrationStore.TryLoad(out Vector3 saved) && saved == Vector3.zero,
                "ResetCalibration guarda (0, 0, 0)");

            DriverSeatFollower second = BuildFollower(created);
            Check(second.LoadPersistentCalibration() && second.CalibrationOffset == Vector3.zero,
                "Un follower nuevo carga 0 aunque antes hubiera otro valor");
        }

        private static void VerifyFlagOffIgnoresStore(List<GameObject> created)
        {
            ResetStore();
            DriverViewCalibrationStore.Save(new Vector3(0.1f, 0.1f, 0.1f));

            DriverSeatFollower follower = BuildFollower(created);
            follower.UsePersistentCalibration = false;
            Check(!follower.IsPersistentCalibrationActive, "Con UsePersistentCalibration = false la persistencia no está activa");
            Check(!follower.LoadPersistentCalibration() && follower.CalibrationOffset == Vector3.zero,
                "Con el flag apagado el follower no carga lo guardado");

            follower.SetCalibrationAxis(0, -0.2f);
            follower.ResetCalibration();
            follower.SetCalibrationAxis(2, 0.15f);
            DriverViewCalibrationStore.TryLoad(out Vector3 saved);
            Check(Approximately(saved, new Vector3(0.1f, 0.1f, 0.1f)), "Con el flag apagado, los cambios del follower no pisan lo guardado");
        }

        private static void VerifyLoadDoesNotRaiseEvent(List<GameObject> created)
        {
            ResetStore();
            DriverViewCalibrationStore.Save(new Vector3(0.1f, 0.05f, -0.1f));

            DriverSeatFollower follower = BuildFollower(created);
            int events = 0;
            follower.CalibrationOffsetChanged += _ => events++;
            bool loaded = follower.LoadPersistentCalibration();
            Check(loaded && Approximately(follower.CalibrationOffset, new Vector3(0.1f, 0.05f, -0.1f)), "La carga aplica el offset guardado");
            Check(events == 0, "Cargar no dispara CalibrationOffsetChanged");

            follower.SetCalibrationAxis(0, 0.2f);
            Check(events == 1, "Un cambio posterior sí dispara CalibrationOffsetChanged");
        }

        private static void VerifyFlushBehavior(List<GameObject> created)
        {
            ResetStore();
            DriverSeatFollower follower = BuildFollower(created);

            int baseline = DriverViewCalibrationStore.FlushCountForTests;
            for (int i = 1; i <= 10; i++) follower.SetCalibrationAxis(0, i * 0.01f); // un slider moviéndose
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline, "Save (sliders cambiando) no hace flush a disco");

            DriverViewCalibrationStore.Flush();
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 1, "Flush con cambios pendientes escribe a disco");
            DriverViewCalibrationStore.Flush();
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 1, "Flush sin cambios pendientes no escribe");

            follower.SetCalibrationAxis(1, 0.05f);
            Invoke(follower, "OnDisable");
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 2, "OnDisable del follower hace flush");

            follower.SetCalibrationAxis(1, 0.06f);
            Invoke(follower, "OnApplicationPause", true);
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 3, "OnApplicationPause(true) hace flush");

            follower.SetCalibrationAxis(1, 0.07f);
            Invoke(follower, "OnApplicationPause", false);
            Invoke(follower, "OnApplicationFocus", true);
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 3, "Reanudar o ganar foco no hace flush");
            Invoke(follower, "OnApplicationFocus", false);
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 4, "OnApplicationFocus(false) hace flush");

            // Con el flag apagado, el follower no vuelca (el menú principal no toca lo guardado).
            DriverViewCalibrationStore.Save(new Vector3(0.1f, 0f, 0f));
            DriverSeatFollower menuFollower = BuildFollower(created);
            menuFollower.UsePersistentCalibration = false;
            Invoke(menuFollower, "OnDisable");
            Invoke(menuFollower, "OnApplicationPause", true);
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 4, "Con el flag apagado, el follower no hace flush");
            DriverViewCalibrationStore.Flush();
        }

        private static void VerifyUiDisableFlushes(List<GameObject> created)
        {
            ResetStore();
            DriverSeatFollower follower = BuildFollower(created);
            DriverViewCalibrationUI ui = BuildUi(created, follower);

            follower.SetCalibrationAxis(2, 0.1f);
            int baseline = DriverViewCalibrationStore.FlushCountForTests;
            Invoke(ui, "OnDisable");
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 1, "DriverViewCalibrationUI.OnDisable hace flush");

            Invoke(ui, "OnDisable");
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline + 1, "DriverViewCalibrationUI.OnDisable sin cambios pendientes no escribe");

            // Si el follower no usa persistencia, el panel no vuelca.
            DriverSeatFollower menuFollower = BuildFollower(created);
            menuFollower.UsePersistentCalibration = false;
            DriverViewCalibrationUI menuUi = BuildUi(created, menuFollower);
            DriverViewCalibrationStore.Save(new Vector3(0f, 0.1f, 0f));
            baseline = DriverViewCalibrationStore.FlushCountForTests;
            Invoke(menuUi, "OnDisable");
            Check(DriverViewCalibrationStore.FlushCountForTests == baseline, "Con el flag apagado, el panel no hace flush");
            DriverViewCalibrationStore.Flush();
        }

        private static void VerifyClear(List<GameObject> created)
        {
            ResetStore();
            DriverViewCalibrationStore.Save(new Vector3(0.1f, 0.1f, 0.1f));
            Check(DriverViewCalibrationStore.HasSaved, "Tras Save, HasSaved es true");

            DriverViewCalibrationStore.Clear();
            Check(!DriverViewCalibrationStore.HasSaved, "Clear deja HasSaved en false");

            DriverViewCalibrationStore.InvalidateCacheForTests();
            Check(!DriverViewCalibrationStore.TryLoad(out _), "Tras Clear y recargar del backend, no hay nada guardado");

            DriverSeatFollower follower = BuildFollower(created);
            Check(!follower.LoadPersistentCalibration() && follower.CalibrationOffset == Vector3.zero, "Tras Clear, un follower nuevo queda en 0");
        }

        private static void VerifySceneFlags()
        {
            VerificationSceneUtil.WithSceneCopy(NewMainMenuSetup.SCENE_PATH, scene =>
            {
                DriverSeatFollower[] followers = VerificationSceneUtil.AllInScene<DriverSeatFollower>(scene);
                Check(followers.Length == 1, $"NewMainMenu tiene un DriverSeatFollower (encontró {followers.Length})");
                foreach (DriverSeatFollower follower in followers)
                {
                    Check(!follower.UsePersistentCalibration, "NewMainMenu: su follower tiene UsePersistentCalibration == false");
                }
            });

            VerificationSceneUtil.WithScene(LESSON_01_SCENE_PATH, scene =>
            {
                DriverSeatFollower[] followers = VerificationSceneUtil.AllInScene<DriverSeatFollower>(scene);
                bool allOn = followers.Length > 0;
                foreach (DriverSeatFollower follower in followers) allOn &= follower.UsePersistentCalibration;
                Check(allOn, "Lesson_01: el follower de la lección usa la persistencia (valor por defecto)");
            });
        }

        private static void VerifyEditModeDoesNotTouchPlayerPrefs(List<GameObject> created)
        {
            if (Application.isPlaying)
            {
                Debug.Log("[SKIP] La prueba de PlayerPrefs reales solo aplica en Edit Mode.");
                return;
            }

            DriverViewCalibrationStore.UseInMemoryBackendForTests(false);
            string[] keys =
            {
                DriverViewCalibrationStore.KEY_X, DriverViewCalibrationStore.KEY_Y,
                DriverViewCalibrationStore.KEY_Z, DriverViewCalibrationStore.KEY_HAS
            };
            string before = SnapshotPlayerPrefs(keys);

            DriverSeatFollower follower = BuildFollower(created);
            Check(!follower.IsPersistentCalibrationActive, "En Edit Mode sin backend en memoria, la persistencia no está activa");
            follower.LoadPersistentCalibration();
            follower.SetCalibrationAxis(0, 0.123f);
            follower.SetCalibrationAxis(1, 0.045f);
            follower.ResetCalibration();
            follower.SetCalibrationAxis(2, -0.2f);
            Invoke(follower, "OnDisable");
            Invoke(follower, "OnApplicationPause", true);
            Invoke(follower, "OnApplicationFocus", false);

            string after = SnapshotPlayerPrefs(keys);
            Check(before == after, $"En Edit Mode, cambiar el offset no escribe en PlayerPrefs (antes: {before} / después: {after})");
        }

        // ------------------------------------------------------------------ Utilidades

        private static void ResetStore()
        {
            // Reactivar el backend en memoria descarta valores, caché, dirty y contador.
            DriverViewCalibrationStore.UseInMemoryBackendForTests(true);
        }

        private static string SnapshotPlayerPrefs(string[] keys)
        {
            var parts = new List<string>();
            foreach (string key in keys)
            {
                parts.Add(PlayerPrefs.HasKey(key) ? $"{key.Substring(key.LastIndexOf('.') + 1)}={PlayerPrefs.GetFloat(key, float.NaN):R}/{PlayerPrefs.GetInt(key, -1)}" : "-");
            }
            return string.Join("|", parts);
        }

        private static DriverSeatFollower BuildFollower(List<GameObject> created)
        {
            var seatObject = new GameObject("DriverSeat");
            var originObject = new GameObject("XR Origin");
            var offsetObject = new GameObject("Camera Offset");
            var cameraObject = new GameObject("Main Camera");
            offsetObject.transform.SetParent(originObject.transform, false);
            cameraObject.transform.SetParent(offsetObject.transform, false);
            var camera = cameraObject.AddComponent<Camera>();

            var origin = originObject.AddComponent<XROrigin>();
            origin.CameraFloorOffsetObject = offsetObject;
            origin.Camera = camera;

            var follower = originObject.AddComponent<DriverSeatFollower>();
            follower.SetDriverSeat(seatObject.transform);

            created.Add(seatObject);
            created.Add(originObject);
            return follower;
        }

        private static DriverViewCalibrationUI BuildUi(List<GameObject> created, DriverSeatFollower follower)
        {
            var uiObject = new GameObject("CalibrationUI");
            var ui = uiObject.AddComponent<DriverViewCalibrationUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("m_DriverSeatFollower").objectReferenceValue = follower;
            so.ApplyModifiedPropertiesWithoutUndo();
            created.Add(uiObject);
            return ui;
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            info.Invoke(target, args);
        }

        private static bool Approximately(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < EPSILON * EPSILON;

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
