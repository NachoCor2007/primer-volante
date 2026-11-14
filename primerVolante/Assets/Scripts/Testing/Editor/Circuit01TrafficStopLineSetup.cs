using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.Environment;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor y verificación para configurar las líneas de detención
    /// y detectores de semáforo en rojo (RedLightViolationDetector) en Circuit_01:
    /// - Crea colliders de tipo Trigger en cada cruce con semáforo a lo largo del circuito.
    /// - Cada trigger contiene un BoxCollider traspasable (isTrigger = true) y un RedLightViolationDetector.
    /// - Configura el enlace dinámico y espacial con el TrafficLightController correspondiente de City_Master.
    /// - Mantiene todos los detectores estrictamente dentro de la escena Circuit_01 (sin alterar City_Master).
    /// - Es 100% idempotente y cumple con la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public static class Circuit01TrafficStopLineSetup
    {
        public const string TARGET_SCENE_PATH = "Assets/Scenes/Lessons/Circuit_01.unity";
        public const string STOP_LINES_ROOT_NAME = "Traffic_StopLines";

        public struct StopLineSpec
        {
            public string Name;
            public string TrafficLightName;
            public Vector3 Position;
            public Vector3 BoxSize;
            public string Description;

            public StopLineSpec(string name, string trafficLightName, Vector3 pos, Vector3 size, string desc)
            {
                Name = name;
                TrafficLightName = trafficLightName;
                Position = pos;
                BoxSize = size;
                Description = desc;
            }
        }

        /// <summary>
        /// Especificación de las líneas de detención en cada intersección controlada del circuito.
        /// Ancho de carril ~7.5m, altura de trigger 2.0m para interceptar el vehículo con holgura.
        /// </summary>
        public static readonly StopLineSpec[] Specs = new[]
        {
            // 1. Cruce Avenida 240 y Calle 60 (el jugador desciende hacia el Sur -Z)
            new StopLineSpec(
                "StopLine_Avenue240_Z60",
                "Props_Traffic Signal_big (16)",
                new Vector3(236.25f, 1.0f, 67.5f),
                new Vector3(7.5f, 2.0f, 0.8f),
                "Avenida X=240 hacia el Sur (-Z) antes de cruce Z=60"
            ),

            // 2. Cruce Calle 0 e Intersección X=180 (el jugador avanza hacia el Oeste -X)
            new StopLineSpec(
                "StopLine_Street0_X180",
                "Props_Traffic Signal_small (8)",
                new Vector3(187.5f, 1.0f, 3.75f),
                new Vector3(0.8f, 2.0f, 7.5f),
                "Calle Z=0 hacia el Oeste (-X) antes de cruce X=180"
            ),

            // 3. Cruce Calle 0 y Calle 120 (el jugador llega a X=120 para doblar al Norte +Z)
            new StopLineSpec(
                "StopLine_Street0_X120",
                "Props_Traffic Signal_small (3)",
                new Vector3(127.5f, 1.0f, 3.75f),
                new Vector3(0.8f, 2.0f, 7.5f),
                "Calle Z=0 hacia el Oeste (-X) antes de girar al Norte en X=120"
            ),

            // 4. Cruce Calle 120 y Calle 60 (Cruce con Peatón y giro a la izquierda -X)
            new StopLineSpec(
                "StopLine_Crosswalk_120_60",
                "Props_Traffic Signal_big (8)",
                new Vector3(123.75f, 1.0f, 51.5f),
                new Vector3(7.5f, 2.0f, 0.8f),
                "Calle X=120 hacia el Norte (+Z) antes de senda peatonal Z=54"
            ),

            // 5. Cruce Calle 60 e Intersección X=60 (el jugador avanza hacia el Oeste -X)
            new StopLineSpec(
                "StopLine_Street60_X60",
                "Props_Traffic Signal_big (4)",
                new Vector3(67.5f, 1.0f, 63.75f),
                new Vector3(0.8f, 2.0f, 7.5f),
                "Calle Z=60 hacia el Oeste (-X) antes de cruce X=60"
            ),

            // 6. Cruce Calle 60 y Avenida 0 (el jugador llega a X=0 para girar al Norte +Z)
            new StopLineSpec(
                "StopLine_Street60_X0",
                "Props_Traffic Signal_big (1)",
                new Vector3(7.5f, 1.0f, 63.75f),
                new Vector3(0.8f, 2.0f, 7.5f),
                "Calle Z=60 hacia el Oeste (-X) antes de girar al Norte en Avenida 0"
            ),

            // 7. Cruce Avenida 0 e Intersección Z=120 (el jugador avanza hacia el Norte +Z)
            new StopLineSpec(
                "StopLine_Avenue0_Z120",
                "Props_Traffic Signal_big (37)",
                new Vector3(3.75f, 1.0f, 112.5f),
                new Vector3(7.5f, 2.0f, 0.8f),
                "Avenida X=0 hacia el Norte (+Z) antes de cruce Z=120"
            ),

            // 8. Cruce Avenida 0 y Calle 160 (el jugador llega a Z=160 para girar al Este +X)
            new StopLineSpec(
                "StopLine_Avenue0_Z160",
                "Props_Traffic Signal_big (40)",
                new Vector3(3.75f, 1.0f, 152.5f),
                new Vector3(7.5f, 2.0f, 0.8f),
                "Avenida X=0 hacia el Norte (+Z) antes de girar al Este en Calle 160"
            ),

            // 9. Cruce Calle 160 y Calle 120 (el jugador llega a X=120 para girar al Norte hacia la meta)
            new StopLineSpec(
                "StopLine_Street160_X120",
                "Props_Traffic Signal_big (46)",
                new Vector3(112.5f, 1.0f, 156.25f),
                new Vector3(0.8f, 2.0f, 7.5f),
                "Calle Z=160 hacia el Este (+X) antes de girar al Norte en Calle 120"
            )
        };

        [MenuItem("Tools/Primer Volante/Circuit 01/Setup Red Light Stop Lines")]
        public static bool SetupStopLines()
        {
            Debug.Log("=================================================================");
            Debug.Log("🚦 [Circuit01TrafficStopLineSetup] Configurando Líneas de Detención y Detectores en Circuit_01...");
            Debug.Log("=================================================================");

            Scene scene = SceneManager.GetSceneByName("Circuit_01");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[Circuit01TrafficStopLineSetup] ❌ No se pudo abrir la escena {TARGET_SCENE_PATH}");
                return false;
            }

            // Contenedor en Circuit_01
            GameObject root = GameObject.Find(STOP_LINES_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(STOP_LINES_ROOT_NAME);
                Undo.RegisterCreatedObjectUndo(root, "Create Traffic Stop Lines Root");
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            int configuredCount = 0;

            foreach (var spec in Specs)
            {
                // Si el objeto ya existe en la escena, buscarlo o crearlo
                GameObject stopLineObj = GameObject.Find(spec.Name);
                if (stopLineObj == null)
                {
                    stopLineObj = new GameObject(spec.Name);
                    Undo.RegisterCreatedObjectUndo(stopLineObj, "Create Stop Line " + spec.Name);
                    stopLineObj.transform.SetParent(root.transform, false);
                }
                else
                {
                    stopLineObj.transform.SetParent(root.transform, false);
                }

                stopLineObj.transform.position = spec.Position;
                stopLineObj.transform.rotation = Quaternion.identity;

                // BoxCollider tipo Trigger
                BoxCollider box = stopLineObj.GetComponent<BoxCollider>();
                if (box == null) box = stopLineObj.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = Vector3.zero;
                box.size = spec.BoxSize;

                // RedLightViolationDetector
                RedLightViolationDetector detector = stopLineObj.GetComponent<RedLightViolationDetector>();
                if (detector == null) detector = stopLineObj.AddComponent<RedLightViolationDetector>();

                var so = new SerializedObject(detector);
                so.Update();

                SerializedProperty nameProp = so.FindProperty("m_TrafficLightName");
                if (nameProp != null) nameProp.stringValue = spec.TrafficLightName;

                SerializedProperty cooldownProp = so.FindProperty("m_CooldownDuration");
                if (cooldownProp != null) cooldownProp.floatValue = 3.0f;

                SerializedProperty checkVehicleProp = so.FindProperty("m_CheckVehicleController");
                if (checkVehicleProp != null) checkVehicleProp.boolValue = true;

                so.ApplyModifiedProperties();

                // Intentar enlazar el semáforo si City_Master está abierta
                detector.TrafficLightName = spec.TrafficLightName;
                detector.EnsureTrafficLightReference();

                EditorUtility.SetDirty(detector);
                EditorUtility.SetDirty(stopLineObj);
                configuredCount++;

                Debug.Log($"[Circuit01TrafficStopLineSetup] ✅ StopLine '{spec.Name}' -> Semáforo: '{spec.TrafficLightName}' ({spec.Description})");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Circuit01TrafficStopLineSetup] 🏁 ¡Configuración completada! {configuredCount} líneas de detención listas en Circuit_01.");
            return true;
        }

        [MenuItem("Tools/Primer Volante/Circuit 01/Verify Red Light Stop Lines")]
        public static bool VerifyStopLines()
        {
            Debug.Log("=================================================================");
            Debug.Log("🔍 [Circuit01TrafficStopLineSetup] Verificando Detectores de Luz Roja en Circuit_01...");
            Debug.Log("=================================================================");

            Scene scene = SceneManager.GetSceneByName("Circuit_01");
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[Circuit01TrafficStopLineSetup] ❌ La escena Circuit_01 no está cargada.");
                return false;
            }

            int passed = 0;
            foreach (var spec in Specs)
            {
                GameObject obj = GameObject.Find(spec.Name);
                if (obj == null)
                {
                    Debug.LogError($"[Circuit01TrafficStopLineSetup] ❌ No se encontró GameObject: {spec.Name}");
                    continue;
                }

                if (obj.scene != scene)
                {
                    Debug.LogError($"[Circuit01TrafficStopLineSetup] ❌ {spec.Name} no está en Circuit_01, está en {obj.scene.name}");
                    continue;
                }

                BoxCollider box = obj.GetComponent<BoxCollider>();
                if (box == null || !box.isTrigger)
                {
                    Debug.LogError($"[Circuit01TrafficStopLineSetup] ❌ {spec.Name} no tiene BoxCollider con isTrigger=true.");
                    continue;
                }

                RedLightViolationDetector detector = obj.GetComponent<RedLightViolationDetector>();
                if (detector == null)
                {
                    Debug.LogError($"[Circuit01TrafficStopLineSetup] ❌ {spec.Name} no tiene componente RedLightViolationDetector.");
                    continue;
                }

                detector.EnsureTrafficLightReference();
                if (detector.TrafficLight == null)
                {
                    Debug.LogWarning($"[Circuit01TrafficStopLineSetup] ⚠️ {spec.Name}: TrafficLight está null en Edit Mode (requiere City_Master cargada o se resolverá en Play Mode con {detector.TrafficLightName}).");
                }
                else
                {
                    Debug.Log($"[Circuit01TrafficStopLineSetup] ✔️ {spec.Name}: Enlazado con éxito a '{detector.TrafficLight.name}'");
                }

                passed++;
            }

            bool allPassed = (passed == Specs.Length);
            if (allPassed)
            {
                Debug.Log($"[Circuit01TrafficStopLineSetup] 🏆 ¡VERIFICACIÓN EXITOSA! {passed}/{Specs.Length} líneas de detención verificadas correctamente.");
            }
            else
            {
                Debug.LogError($"[Circuit01TrafficStopLineSetup] ❌ Fallaron validaciones: {passed}/{Specs.Length} aprobadas.");
            }

            return allPassed;
        }
    }
}
