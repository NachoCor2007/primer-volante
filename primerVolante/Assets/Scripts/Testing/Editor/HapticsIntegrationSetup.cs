using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Feedback;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor que integra las vibraciones hápticas del habitáculo y la UI:
    /// crea el <see cref="CabinHapticProfile"/>, agrega <see cref="CabinHapticTarget"/> a los 15 interactables de
    /// Car08, agrega <see cref="VehicleCabinControlsHaptics"/> y ajusta el rig de SoundIntegrationScene con overrides
    /// de instancia (sin tocar los prefabs de Samples). Es idempotente: se puede re-ejecutar sin duplicar nada
    /// ni pisar los valores del perfil que se hayan ajustado a mano.
    /// </summary>
    public static class HapticsIntegrationSetup
    {
        public const string PROFILE_FOLDER = "Assets/Haptics";
        public const string PROFILE_PATH = PROFILE_FOLDER + "/CabinHapticProfile_Car08.asset";

        /// <summary>Un interactable de Car08 con su categoría háptica.</summary>
        public readonly struct TargetSpec
        {
            public readonly string Name;
            public readonly CabinHapticCategory Category;
            public readonly bool PlayReach;

            public TargetSpec(string name, CabinHapticCategory category, bool playReach)
            {
                Name = name;
                Category = category;
                PlayReach = playReach;
            }
        }

        /// <summary>Los 15 interactables del habitáculo con su categoría y si vibran al alcanzarlos.</summary>
        public static readonly TargetSpec[] Targets =
        {
            new TargetSpec("TurnSignal", CabinHapticCategory.Levers, true),
            new TargetSpec("GearShifter", CabinHapticCategory.Levers, true),
            new TargetSpec("mdl_car02_brake", CabinHapticCategory.Levers, true),
            new TargetSpec("SteeringWheel_Pivot", CabinHapticCategory.Wheel, true),
            new TargetSpec("StartButton", CabinHapticCategory.ButtonsAndKnobs, true),
            new TargetSpec("HazardButton", CabinHapticCategory.ButtonsAndKnobs, true),
            new TargetSpec("mdl_car02_lights_knob", CabinHapticCategory.ButtonsAndKnobs, true),
            new TargetSpec("RearviewMirror_Casing", CabinHapticCategory.ButtonsAndKnobs, true),
            // Panel de espejos laterales: botones de ~2 cm muy juntos, solo háptica de agarre.
            new TargetSpec("Btn_Select_Left", CabinHapticCategory.ButtonsAndKnobs, false),
            new TargetSpec("Btn_Select_Off", CabinHapticCategory.ButtonsAndKnobs, false),
            new TargetSpec("Btn_Select_Right", CabinHapticCategory.ButtonsAndKnobs, false),
            new TargetSpec("Btn_DPad_Up", CabinHapticCategory.ButtonsAndKnobs, false),
            new TargetSpec("Btn_DPad_Down", CabinHapticCategory.ButtonsAndKnobs, false),
            new TargetSpec("Btn_DPad_Left", CabinHapticCategory.ButtonsAndKnobs, false),
            new TargetSpec("Btn_DPad_Right", CabinHapticCategory.ButtonsAndKnobs, false),
        };

        [MenuItem("Tools/Primer Volante/Integrate Cabin Haptics (Car08 Prefab and Scene)")]
        public static void IntegrateAll()
        {
            CreateProfile();
            SetupCar08Prefab();
            SetupHapticsScene();
        }

        // ------------------------------------------------------------------ Profile

        /// <summary>
        /// Crea el perfil con los valores de la tabla acordada. Si ya existe no lo toca, para no pisar
        /// los ajustes hechos en el visor.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Create Cabin Haptic Profile (Car08)")]
        public static CabinHapticProfile CreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CabinHapticProfile>(PROFILE_PATH);
            if (profile != null) return profile;

            if (!AssetDatabase.IsValidFolder(PROFILE_FOLDER))
                AssetDatabase.CreateFolder("Assets", "Haptics");

            profile = ScriptableObject.CreateInstance<CabinHapticProfile>();
            AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HapticsIntegrationSetup] 📳 CabinHapticProfile creado en {PROFILE_PATH}");
            return profile;
        }

        /// <summary>Restablece el perfil a los valores de la tabla acordada (descarta los ajustes manuales).</summary>
        [MenuItem("Tools/Primer Volante/Reset Cabin Haptic Profile to Defaults")]
        public static void ResetProfileToDefaults()
        {
            var profile = CreateProfile();
            var defaults = ScriptableObject.CreateInstance<CabinHapticProfile>();
            try
            {
                EditorUtility.CopySerialized(defaults, profile);
            }
            finally
            {
                Object.DestroyImmediate(defaults);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[HapticsIntegrationSetup] 📳 CabinHapticProfile restablecido a los valores por defecto.");
        }

        // ------------------------------------------------------------------ Car08

        [MenuItem("Tools/Primer Volante/Setup Car08 Haptics")]
        public static void SetupCar08Prefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SoundIntegrationSetup.PREFAB_CAR08_PATH) == null)
            {
                Debug.LogError($"[HapticsIntegrationSetup] ❌ {SoundIntegrationSetup.PREFAB_CAR08_PATH} no existe.");
                return;
            }

            var profile = CreateProfile();
            GameObject root = PrefabUtility.LoadPrefabContents(SoundIntegrationSetup.PREFAB_CAR08_PATH);
            try
            {
                ConfigureCar08(root, profile);
                PrefabUtility.SaveAsPrefabAsset(root, SoundIntegrationSetup.PREFAB_CAR08_PATH, out bool ok);
                if (ok) Debug.Log($"[HapticsIntegrationSetup] ✅ {SoundIntegrationSetup.PREFAB_CAR08_PATH} configurado con hápticos.");
                else Debug.LogError("[HapticsIntegrationSetup] ❌ Falló el guardado de Car08.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Agrega los CabinHapticTarget y el VehicleCabinControlsHaptics a la raíz de Car08.</summary>
        public static void ConfigureCar08(GameObject root, CabinHapticProfile profile)
        {
            foreach (TargetSpec spec in Targets)
            {
                XRBaseInteractable interactable = FindInteractable(root.transform, spec.Name);
                if (interactable == null)
                {
                    Debug.LogError($"[HapticsIntegrationSetup] ❌ No se encontró el interactable '{spec.Name}' en Car08.");
                    continue;
                }

                var target = GetOrAdd<CabinHapticTarget>(interactable.gameObject);
                SetObj(target, "m_Profile", profile);
                SetEnum(target, "m_Category", (int)spec.Category);
                SetBool(target, "m_PlayReach", spec.PlayReach);
            }

            // Confirmaciones: hermano de VehicleAudio, en la raíz del auto.
            Transform hapticsRoot = EnsureChild(root.transform, "VehicleHaptics");
            var vehicle = root.GetComponent<VehicleController>();
            var cabin = GetOrAdd<VehicleCabinControlsHaptics>(hapticsRoot.gameObject);
            SetObj(cabin, "m_VehicleController", vehicle);
            SetObj(cabin, "m_TurnSignal", root.GetComponentInChildren<VRTurnSignal>(true));
            SetObj(cabin, "m_GearShifter", root.GetComponentInChildren<VRGearShifter>(true));
            SetObj(cabin, "m_HeadlightKnob", root.GetComponentInChildren<VRHeadlightKnob>(true));
            SetObj(cabin, "m_Handbrake", root.GetComponentInChildren<VRHandbrake>(true));
            SetObj(cabin, "m_StartInteractable", FindInteractable(root.transform, "StartButton"));
            SetObj(cabin, "m_HazardInteractable", FindInteractable(root.transform, "HazardButton"));
            SetObj(cabin, "m_Profile", profile);
            SetObj(cabin, "m_AudioProfile", AssetDatabase.LoadAssetAtPath<VehicleAudioProfile>(SoundIntegrationSetup.PROFILE_PATH));
        }

        // ------------------------------------------------------------------ Escena

        [MenuItem("Tools/Primer Volante/Setup Haptics Scene")]
        public static void SetupHapticsScene()
        {
            var profile = CreateProfile();

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SoundIntegrationSetup.SCENE_SOUND_PATH)
                scene = EditorSceneManager.OpenScene(SoundIntegrationSetup.SCENE_SOUND_PATH, OpenSceneMode.Single);

            int feedbacks = 0, casters = 0;

            // Los hover/select genéricos de XRI se apagan en Near-Far y Poke (el Teleport no se toca).
            // Se edita la instancia en la escena: queda como override, sin tocar los prefabs de Samples.
            foreach (var haptic in Object.FindObjectsByType<SimpleHapticFeedback>(FindObjectsInactive.Include))
            {
                bool isNearFarOrPoke = haptic.GetComponent<NearFarInteractor>() != null ||
                                       haptic.GetComponent<XRPokeInteractor>() != null;
                if (!isNearFarOrPoke) continue;

                SetBool(haptic, "m_PlayHoverEntered", false);
                SetBool(haptic, "m_PlaySelectEntered", false);
                feedbacks++;
            }

            // Con el sorting por distancia al punto más cercano del collider, el volante gana al guiño
            // (cuyo attach está a ~10 cm del centro del volante) cuando la mano está cerca del aro.
            foreach (var nearFar in Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include))
            {
                SetEnum(nearFar, "m_NearCasterSortingStrategy", (int)NearFarInteractor.NearCasterSortingStrategy.ClosestPointOnCollider);
                casters++;
            }

            var inputModule = Object.FindAnyObjectByType<XRUIInputModule>(FindObjectsInactive.Include);
            if (inputModule == null)
            {
                Debug.LogError("[HapticsIntegrationSetup] ❌ No hay XRUIInputModule en la escena.");
            }
            else
            {
                var uiHaptics = GetOrAdd<UIHapticFeedback>(inputModule.gameObject);
                SetObj(uiHaptics, "m_Profile", profile);
                SetObj(uiHaptics, "m_InputModule", inputModule);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[HapticsIntegrationSetup] ✅ Escena {scene.path}: {feedbacks} SimpleHapticFeedback sin hover/select, " +
                      $"{casters} NearFarInteractor con ClosestPointOnCollider, UIHapticFeedback presente.");
        }

        // ------------------------------------------------------------------ Helpers

        private static XRBaseInteractable FindInteractable(Transform root, string name)
        {
            foreach (var interactable in root.GetComponentsInChildren<XRBaseInteractable>(true))
                if (interactable.name == name) return interactable;
            return null;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static SerializedProperty FindProperty(SerializedObject so, Object target, string property)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
                Debug.LogWarning($"[HapticsIntegrationSetup] ⚠️ Propiedad '{property}' no encontrada en {target.GetType().Name}.");
            return p;
        }

        private static void Apply(SerializedObject so, Object target)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            // En instancias de prefab asegura que el cambio quede registrado como override.
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static void SetObj(Object target, string property, Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            SerializedProperty p = FindProperty(so, target, property);
            if (p == null) return;
            p.objectReferenceValue = value;
            Apply(so, target);
        }

        private static void SetBool(Object target, string property, bool value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            SerializedProperty p = FindProperty(so, target, property);
            if (p == null) return;
            p.boolValue = value;
            Apply(so, target);
        }

        private static void SetEnum(Object target, string property, int value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            SerializedProperty p = FindProperty(so, target, property);
            if (p == null) return;
            p.intValue = value;
            Apply(so, target);
        }
    }
}
