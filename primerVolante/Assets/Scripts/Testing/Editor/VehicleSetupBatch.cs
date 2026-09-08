using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de automatización para CLI y Editor de Unity.
    /// Permite asociar componentes (VehicleController, Rigidbody, BoxCollider)
    /// a Prefabs y a la Escena Activa mediante el menú de Unity.
    /// </summary>
    public static class VehicleSetupBatch
    {
        private const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car03.prefab";

        [MenuItem("Tools/Primer Volante/Setup Vehicle Prefab")]
        public static void SetupVehicleComponent()
        {
            Debug.Log($"[VehicleSetupBatch] 📦 Cargando contenidos del Prefab en: {PREFAB_PATH}...");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);

            if (prefabRoot == null)
            {
                Debug.LogError($"[VehicleSetupBatch] ❌ No se pudo cargar el Prefab en {PREFAB_PATH}");
                return;
            }

            try
            {
                ConfigureVehicleGameObject(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
                Debug.Log("[VehicleSetupBatch] ✅ ¡Prefab guardado exitosamente con VehicleController, Rigidbody y BoxCollider!");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Tools/Primer Volante/Setup Vehicle in Active Scene")]
        public static void SetupActiveSceneVehicle()
        {
            Debug.Log("[VehicleSetupBatch] 🔍 Buscando vehículo en la escena activa...");
            
            // Buscar vehículo en la escena por VehicleController o por VRSteeringWheel o por nombre
            VehicleController existingCtrl = Object.FindFirstObjectByType<VehicleController>();
            GameObject vehicleTarget = existingCtrl != null ? existingCtrl.gameObject : null;

            if (vehicleTarget == null)
            {
                VRSteeringWheel steeringWheel = Object.FindFirstObjectByType<VRSteeringWheel>();
                if (steeringWheel != null)
                {
                    Transform curr = steeringWheel.transform;
                    while (curr.parent != null && !curr.name.StartsWith("Demo") && !curr.name.StartsWith("XR") && !curr.name.Contains("Scene"))
                    {
                        curr = curr.parent;
                    }
                    vehicleTarget = curr.gameObject;
                }
            }

            if (vehicleTarget == null)
            {
                vehicleTarget = GameObject.Find("Car03") ?? GameObject.Find("mdl_car02_body");
            }

            if (vehicleTarget == null && Selection.activeGameObject != null)
            {
                vehicleTarget = Selection.activeGameObject;
            }

            if (vehicleTarget == null)
            {
                Debug.LogError("[VehicleSetupBatch] ❌ No se encontró ningún vehículo automáticamente en la escena activa. Selecciona el objeto del coche en la jerarquía e intenta de nuevo.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(vehicleTarget, "Setup Vehicle Controller");
            ConfigureVehicleGameObject(vehicleTarget);
            EditorSceneManager.MarkSceneDirty(vehicleTarget.scene);
            Debug.Log($"[VehicleSetupBatch] ✅ ¡Vehículo '{vehicleTarget.name}' configurado exitosamente en la escena activa! (VehicleController, Rigidbody y BoxCollider listos)");
        }

        private static void ConfigureVehicleGameObject(GameObject root)
        {
            // 1. Configurar o agregar Rigidbody
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.Log($"[VehicleSetupBatch] ➕ Agregando Rigidbody a {root.name}...");
                rb = Undo.AddComponent<Rigidbody>(root);
            }
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // 2. Configurar o agregar BoxCollider encuadrado
            BoxCollider boxCol = root.GetComponent<BoxCollider>();
            if (boxCol == null)
            {
                Debug.Log($"[VehicleSetupBatch] ➕ Agregando y encuadrando BoxCollider a {root.name}...");
                boxCol = Undo.AddComponent<BoxCollider>(root);
            }
            
            VehicleController.SetupBoxColliderBounds(root, boxCol);

            // 3. Configurar o agregar VehicleController
            VehicleController vehicleCtrl = root.GetComponent<VehicleController>();
            if (vehicleCtrl == null)
            {
                Debug.Log($"[VehicleSetupBatch] ➕ Agregando componente VehicleController a {root.name}...");
                vehicleCtrl = Undo.AddComponent<VehicleController>(root);
            }

            vehicleCtrl.ConfigurePhysicsAndColliders();

            // 4. Enganchar el VRSteeringWheel si existe en los hijos
            VRSteeringWheel wheel = root.GetComponentInChildren<VRSteeringWheel>();
            if (wheel != null && vehicleCtrl != null)
            {
                vehicleCtrl.SteeringWheel = wheel;
                Debug.Log($"[VehicleSetupBatch] 🔗 Volante VR '{wheel.name}' vinculado a VehicleController en {root.name}.");
            }
        }
    }
}
