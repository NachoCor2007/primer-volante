using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de automatización para CLI y Editor de Unity.
    /// Permite asociar componentes (VehicleController, Rigidbody, BoxCollider),
    /// configurar el asiento del conductor (DriverSeat), reemplazar MeshCollider del volante por BoxCollider simplificado
    /// y ubicar al jugador (XROrigin) mediante DriverSeatFollower.
    /// </summary>
    public static class VehicleSetupBatch
    {
        private const string PREFAB_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car03.prefab";

        [MenuItem("Tools/Primer Volante/Setup Vehicle Prefab")]
        public static void SetupVehicleComponent()
        {
            Debug.Log($"[VehicleSetupBatch] 📦 Cargando contenidos del ASSET Prefab en: {PREFAB_PATH}...");
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
                Debug.Log("[VehicleSetupBatch] ✅ ¡ASSET Car03.prefab actualizado y guardado PERMANENTEMENTE!");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Tools/Primer Volante/Setup Vehicle in Active Scene")]
        public static void SetupActiveSceneVehicle()
        {
            Debug.Log("[VehicleSetupBatch] 🔍 Configurando vehículo en el asset Prefab y en la escena activa...");
            
            SetupVehicleComponent();

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
                Debug.LogError("[VehicleSetupBatch] ❌ No se encontró ningún vehículo automáticamente en la escena activa.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(vehicleTarget, "Setup Vehicle Controller");
            ConfigureVehicleGameObject(vehicleTarget);

            SetupPlayerInDriverSeat(vehicleTarget);

            EditorSceneManager.MarkSceneDirty(vehicleTarget.scene);
            Debug.Log($"[VehicleSetupBatch] ✅ ¡Vehículo '{vehicleTarget.name}' y Jugador (XROrigin) configurados permanentemente!");
        }

        private static void ConfigureVehicleGameObject(GameObject root)
        {
            // 1. Configurar o agregar Rigidbody con Interpolación en el Asset
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.Log($"[VehicleSetupBatch] ➕ Agregando Rigidbody permanente a {root.name}...");
                rb = root.AddComponent<Rigidbody>();
            }
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // 2. Configurar o agregar BoxCollider encuadrado permanente
            BoxCollider boxCol = root.GetComponent<BoxCollider>();
            if (boxCol == null)
            {
                Debug.Log($"[VehicleSetupBatch] ➕ Agregando y encuadrando BoxCollider permanente a {root.name}...");
                boxCol = root.AddComponent<BoxCollider>();
            }
            
            VehicleController.SetupBoxColliderBounds(root, boxCol);

            // 3. Crear o verificar ancla DriverSeat (Asiento del Conductor) en el Asset
            Transform driverSeat = root.transform.Find("DriverSeat");
            if (driverSeat == null)
            {
                Debug.Log($"[VehicleSetupBatch] 🪑 Creando ancla DriverSeat permanente en {root.name}...");
                GameObject seatObj = new GameObject("DriverSeat");
                seatObj.transform.SetParent(root.transform, false);
                seatObj.transform.localPosition = new Vector3(-0.35f, 0.40f, -0.1f);
                seatObj.transform.localRotation = Quaternion.identity;
                driverSeat = seatObj.transform;
            }

            // 4. Configurar o agregar VehicleController permanente
            VehicleController vehicleCtrl = root.GetComponent<VehicleController>();
            if (vehicleCtrl == null)
            {
                Debug.Log($"[VehicleSetupBatch] ➕ Agregando componente VehicleController permanente a {root.name}...");
                vehicleCtrl = root.AddComponent<VehicleController>();
            }

            vehicleCtrl.ConfigurePhysicsAndColliders();

            // 5. Configurar el Volante con Colisionador Simplificado (BoxCollider de Agarre Indulgente)
            VRSteeringWheel wheel = root.GetComponentInChildren<VRSteeringWheel>();
            if (wheel != null)
            {
                if (vehicleCtrl != null) vehicleCtrl.SteeringWheel = wheel;

                wheel.ConfigureGrabInteractable();
                wheel.EnsureSimplifiedGrabCollider();

                XRGrabInteractable grabInteractable = wheel.GetComponent<XRGrabInteractable>();
                if (grabInteractable != null)
                {
                    grabInteractable.trackPosition = false;
                    grabInteractable.trackRotation = false;
                    grabInteractable.throwOnDetach = false;
                    grabInteractable.useDynamicAttach = false;
                    grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                    Debug.Log($"[VehicleSetupBatch] 🔒 Colisionador de agarre simplificado y XRGrabInteractable configurados en '{wheel.name}'.");
                }
            }
        }

        private static void SetupPlayerInDriverSeat(GameObject vehicleTarget)
        {
            Transform driverSeat = vehicleTarget.transform.Find("DriverSeat");
            if (driverSeat == null) return;

            GameObject xrOrigin = GameObject.Find("XR Origin (XR Rig)") ?? GameObject.Find("XR Origin") ?? GameObject.Find("XROrigin") ?? GameObject.Find("XR Rig");
            if (xrOrigin == null)
            {
                var originComponent = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (originComponent != null)
                {
                    xrOrigin = originComponent.gameObject;
                }
            }

            if (xrOrigin != null)
            {
                if (xrOrigin.transform.parent != null && xrOrigin.transform.parent.IsChildOf(vehicleTarget.transform))
                {
                    Undo.SetTransformParent(xrOrigin.transform, null, "Unparent XROrigin to Scene Root");
                }

                DriverSeatFollower follower = xrOrigin.GetComponent<DriverSeatFollower>();
                if (follower == null)
                {
                    follower = Undo.AddComponent<DriverSeatFollower>(xrOrigin);
                }

                follower.DriverSeat = driverSeat;
                follower.FollowSeat = true;
                follower.SnapToSeat();

                Debug.Log($"[VehicleSetupBatch] 🚗 Jugador '{xrOrigin.name}' configurado con DriverSeatFollower permanente.");

                CharacterController charCtrl = xrOrigin.GetComponentInChildren<CharacterController>(true);
                if (charCtrl != null)
                {
                    charCtrl.enabled = false;
                }

                Rigidbody playerRb = xrOrigin.GetComponent<Rigidbody>();
                if (playerRb != null && playerRb.gameObject != vehicleTarget)
                {
                    playerRb.isKinematic = true;
                    playerRb.useGravity = false;
                }

                MonoBehaviour[] allComponents = xrOrigin.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var comp in allComponents)
                {
                    if (comp == null) continue;
                    string name = comp.GetType().Name;
                    if (name.Contains("Teleport") || name.Contains("ContinuousMove") || name.Contains("DynamicMove") || name.Contains("SnapTurn"))
                    {
                        comp.enabled = false;
                    }
                }
            }
        }
    }
}
