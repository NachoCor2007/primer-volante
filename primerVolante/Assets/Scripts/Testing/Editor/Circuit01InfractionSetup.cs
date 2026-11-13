using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using PrimerVolante.Lessons;
using PrimerVolante.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor y verificación para configurar el sistema de infracciones en Circuit_01:
    /// - Asegura que el catálogo y las definiciones de infracción existan.
    /// - Configura la capa LaneZone y las zonas exentas en prefabs viales.
    /// - Instancia y configura InfractionManager vinculado a LessonManager.
    /// - Agrega y calibra WrongWayDetector, TurnSignalDetector y LowBeamDetector en Car09.
    /// - Configura zonas exentas (WrongWayExemptZone y TurnSignalExemptZone) en los estacionamientos de inicio y llegada.
    /// - Verifica la cadena de alertas diegéticas (DashboardUIController en tablero y ConsoleAlertDisplay en GPS).
    /// - Respeta estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Circuit01InfractionSetup
    {
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Circuit_01.unity";
        public const string INFRACTION_MANAGER_NAME = "InfractionManager";
        public const string START_EXEMPT_NAME = "ExemptZone_StartParkingLot";
        public const string FINISH_EXEMPT_NAME = "ExemptZone_FinishParking";

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Infractions in Circuit 01")]
        public static bool SetupInfractionsInCircuit01()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚨 [Circuit01InfractionSetup] Configurando Sistema de Infracciones en Circuit_01...");
            Debug.Log("=================================================================");

            // 1. Cargar catálogo de infracciones y capa LaneZone directamente
            InfractionCatalogSO catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>("Assets/Settings/Evaluation/InfractionCatalog.asset");
            if (catalog == null)
            {
                catalog = InfractionManagerSetup.EnsureDefinitionsAndCatalog();
            }

            int laneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            if (laneLayer < 0)
            {
                laneLayer = WrongWaySetup.EnsureLaneZoneLayer();
            }

            if (laneLayer < 0 || catalog == null)
            {
                Debug.LogError("[Circuit01InfractionSetup] ❌ No se pudo inicializar la capa LaneZone o el catálogo de infracciones.");
                return false;
            }

            // 2. Abrir la escena Circuit_01 si no está activa
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "Circuit_01" && !scene.path.Contains("Circuit_01"))
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Circuit01InfractionSetup] ❌ No se pudo abrir la escena objetivo: {TARGET_SCENE_PATH}");
                return false;
            }

            // 3. Configurar GameObject InfractionManager en la escena
            GameObject mgrObj = GameObject.Find(INFRACTION_MANAGER_NAME);
            if (mgrObj == null)
            {
                mgrObj = new GameObject(INFRACTION_MANAGER_NAME);
                Undo.RegisterCreatedObjectUndo(mgrObj, "Create InfractionManager");
                SceneManager.MoveGameObjectToScene(mgrObj, scene);
            }

            InfractionManager infractionMgr = mgrObj.GetComponent<InfractionManager>();
            if (infractionMgr == null) infractionMgr = mgrObj.AddComponent<InfractionManager>();

            LessonManager lessonMgr = UnityEngine.Object.FindAnyObjectByType<LessonManager>();

            var mgrSo = new SerializedObject(infractionMgr);
            mgrSo.FindProperty("m_Catalog").objectReferenceValue = catalog;
            if (lessonMgr != null)
            {
                mgrSo.FindProperty("m_LessonManager").objectReferenceValue = lessonMgr;
            }
            mgrSo.FindProperty("m_LogEvents").boolValue = true;
            mgrSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(infractionMgr);

            // 4. Configurar detectores en el auto del jugador (Car09)
            GameObject car09 = GameObject.Find("Car09");
            if (car09 == null)
            {
                Debug.LogError("[Circuit01InfractionSetup] ❌ No se encontró el GameObject 'Car09' en la escena.");
                return false;
            }

            // 4.1 WrongWayDetector (Contramano)
            WrongWayDetector wrongWay = car09.GetComponent<WrongWayDetector>();
            if (wrongWay == null)
            {
                wrongWay = car09.AddComponent<WrongWayDetector>();
                Debug.Log("[Circuit01InfractionSetup] ➕ WrongWayDetector agregado a Car09.");
            }
            var wwSo = new SerializedObject(wrongWay);
            wwSo.FindProperty("m_LogEvents").boolValue = true;
            wwSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(wrongWay);

            // 4.2 TurnSignalDetector (Guiño / Giro sin señalizar)
            TurnSignalDetector turnDetector = car09.GetComponent<TurnSignalDetector>();
            if (turnDetector == null)
            {
                turnDetector = car09.AddComponent<TurnSignalDetector>();
                Debug.Log("[Circuit01InfractionSetup] ➕ TurnSignalDetector agregado a Car09.");
            }
            turnDetector.ZoneMask = 1 << laneLayer;
            var tsSo = new SerializedObject(turnDetector);
            tsSo.FindProperty("m_LogEvents").boolValue = true;
            tsSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(turnDetector);

            // 4.3 LowBeamDetector (Luces bajas)
            LowBeamDetector lowBeam = car09.GetComponent<LowBeamDetector>();
            if (lowBeam == null)
            {
                lowBeam = car09.AddComponent<LowBeamDetector>();
                Debug.Log("[Circuit01InfractionSetup] ➕ LowBeamDetector agregado a Car09.");
            }
            var lbSo = new SerializedObject(lowBeam);
            lbSo.FindProperty("m_LogEvents").boolValue = true;
            lbSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(lowBeam);

            // 5. Configurar Zonas Exentas para maniobras en estacionamiento
            EnsureStartParkingExemptZone(scene, laneLayer);
            EnsureFinishParkingExemptZone(scene, laneLayer);

            // 6. Verificar y asegurar feedback diegético en Car09
            DashboardUIController dash = car09.GetComponentInChildren<DashboardUIController>(true);
            ConsoleAlertDisplay alertDisplay = car09.GetComponentInChildren<ConsoleAlertDisplay>(true);
            VehicleInfractionAudio audio = car09.GetComponentInChildren<VehicleInfractionAudio>(true);

            Debug.Log($"[Circuit01InfractionSetup] ℹ️ Feedback Diegético: DashboardUIController={dash != null}, ConsoleAlertDisplay={alertDisplay != null}, VehicleInfractionAudio={audio != null}");

            // 7. Guardar cambios
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("=================================================================");
            Debug.Log("✅ [Circuit01InfractionSetup] Sistema de Infracciones configurado con éxito en Circuit_01!");
            Debug.Log("=================================================================");
            return true;
        }

        private static void EnsureStartParkingExemptZone(Scene scene, int laneLayer)
        {
            GameObject zoneObj = GameObject.Find(START_EXEMPT_NAME);
            if (zoneObj == null)
            {
                zoneObj = new GameObject(START_EXEMPT_NAME);
                Undo.RegisterCreatedObjectUndo(zoneObj, "Create Start Exempt Zone");
                SceneManager.MoveGameObjectToScene(zoneObj, scene);
            }

            zoneObj.layer = laneLayer;
            zoneObj.transform.position = new Vector3(218f, 1.5f, 84f);
            zoneObj.transform.rotation = Quaternion.identity;
            zoneObj.transform.localScale = Vector3.one;

            BoxCollider box = zoneObj.GetComponent<BoxCollider>();
            if (box == null) box = zoneObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(35f, 3f, 25f);

            WrongWayExemptZone wwExempt = zoneObj.GetComponent<WrongWayExemptZone>();
            if (wwExempt == null) wwExempt = zoneObj.AddComponent<WrongWayExemptZone>();

            TurnSignalExemptZone tsExempt = zoneObj.GetComponent<TurnSignalExemptZone>();
            if (tsExempt == null) tsExempt = zoneObj.AddComponent<TurnSignalExemptZone>();

            EditorUtility.SetDirty(zoneObj);
        }

        private static void EnsureFinishParkingExemptZone(Scene scene, int laneLayer)
        {
            GameObject zoneObj = GameObject.Find(FINISH_EXEMPT_NAME);
            if (zoneObj == null)
            {
                zoneObj = new GameObject(FINISH_EXEMPT_NAME);
                Undo.RegisterCreatedObjectUndo(zoneObj, "Create Finish Exempt Zone");
                SceneManager.MoveGameObjectToScene(zoneObj, scene);
            }

            zoneObj.layer = laneLayer;
            zoneObj.transform.position = new Vector3(123.5f, 1.5f, 200f);
            zoneObj.transform.rotation = Quaternion.identity;
            zoneObj.transform.localScale = Vector3.one;

            BoxCollider box = zoneObj.GetComponent<BoxCollider>();
            if (box == null) box = zoneObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(12f, 3f, 14f);

            WrongWayExemptZone wwExempt = zoneObj.GetComponent<WrongWayExemptZone>();
            if (wwExempt == null) wwExempt = zoneObj.AddComponent<WrongWayExemptZone>();

            TurnSignalExemptZone tsExempt = zoneObj.GetComponent<TurnSignalExemptZone>();
            if (tsExempt == null) tsExempt = zoneObj.AddComponent<TurnSignalExemptZone>();

            EditorUtility.SetDirty(zoneObj);
        }

        [MenuItem("Tools/Primer Volante/Circuit 01/Verify Infractions in Circuit 01")]
        public static bool RunVerification()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "Circuit_01" && !scene.path.Contains("Circuit_01"))
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            var failures = new List<string>();

            // 1. InfractionManager
            InfractionManager mgr = UnityEngine.Object.FindAnyObjectByType<InfractionManager>();
            if (mgr == null)
            {
                failures.Add("Falta InfractionManager en la escena.");
            }
            else
            {
                if (mgr.Catalog == null) failures.Add("InfractionManager no tiene el catálogo asignado.");
                else if (mgr.Catalog.Definitions.Count < 5) failures.Add($"Catálogo incompleto ({mgr.Catalog.Definitions.Count} definiciones).");
            }

            // 2. Car09 y sus detectores
            GameObject car = GameObject.Find("Car09");
            if (car == null)
            {
                failures.Add("No se encontró Car09 en la escena.");
            }
            else
            {
                WrongWayDetector ww = car.GetComponent<WrongWayDetector>();
                if (ww == null) failures.Add("Car09 no tiene WrongWayDetector.");

                TurnSignalDetector ts = car.GetComponent<TurnSignalDetector>();
                if (ts == null) failures.Add("Car09 no tiene TurnSignalDetector.");
                else
                {
                    int laneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
                    if (ts.ZoneMask != (1 << laneLayer))
                    {
                        failures.Add($"TurnSignalDetector.ZoneMask incorrecta ({ts.ZoneMask.value} != {1 << laneLayer}).");
                    }
                }

                LowBeamDetector lb = car.GetComponent<LowBeamDetector>();
                if (lb == null) failures.Add("Car09 no tiene LowBeamDetector.");

                DashboardUIController dash = car.GetComponentInChildren<DashboardUIController>(true);
                if (dash == null) failures.Add("Car09 no tiene DashboardUIController en su jerarquía.");

                ConsoleAlertDisplay alert = car.GetComponentInChildren<ConsoleAlertDisplay>(true);
                if (alert == null) failures.Add("Car09 no tiene ConsoleAlertDisplay en su jerarquía.");

                VehicleInfractionAudio audio = car.GetComponentInChildren<VehicleInfractionAudio>(true);
                if (audio == null) failures.Add("Car09 no tiene VehicleInfractionAudio en su jerarquía.");
            }

            // 3. Zonas exentas
            GameObject startExempt = GameObject.Find(START_EXEMPT_NAME);
            if (startExempt == null) failures.Add($"No se encontró la zona exenta de inicio {START_EXEMPT_NAME}.");
            else
            {
                if (startExempt.GetComponent<WrongWayExemptZone>() == null) failures.Add("Zona exenta de inicio sin WrongWayExemptZone.");
                if (startExempt.GetComponent<TurnSignalExemptZone>() == null) failures.Add("Zona exenta de inicio sin TurnSignalExemptZone.");
            }

            GameObject finishExempt = GameObject.Find(FINISH_EXEMPT_NAME);
            if (finishExempt == null) failures.Add($"No se encontró la zona exenta de llegada {FINISH_EXEMPT_NAME}.");
            else
            {
                if (finishExempt.GetComponent<WrongWayExemptZone>() == null) failures.Add("Zona exenta de llegada sin WrongWayExemptZone.");
                if (finishExempt.GetComponent<TurnSignalExemptZone>() == null) failures.Add("Zona exenta de llegada sin TurnSignalExemptZone.");
            }

            // 4. Reporte
            if (failures.Count > 0)
            {
                Debug.LogError($"[Circuit01InfractionSetup] ❌ Verificación falló con {failures.Count} errores:\n - " + string.Join("\n - ", failures));
                return false;
            }

            Debug.Log("[Circuit01InfractionSetup] ✅ ¡Verificación de Infracciones en Circuit_01 PASADA EXITOSAMENTE! Todos los gestores, detectores, zonas exentas y alertas diegéticas están correctamente configurados.");
            return true;
        }
    }
}
