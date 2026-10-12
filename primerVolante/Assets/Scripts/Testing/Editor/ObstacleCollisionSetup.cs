using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de US33 (#21): sensor de contactos en el auto del jugador, detectores en
    /// el cono, los NPC y los autos estacionados, y la zona de práctica de la escena
    /// <c>CityWithCollisionDetectors</c>. Cada paso se puede volver a correr sin duplicar nada.
    /// </summary>
    public static class ObstacleCollisionSetup
    {
        private const string CAR_FOLDER = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs";
        public const string CAR08_PATH = CAR_FOLDER + "/Car08.prefab";
        public const string CAR07_PATH = CAR_FOLDER + "/Car07.prefab";

        /// <summary>Nombre del hijo con la <c>AudioSource</c> del impacto.</summary>
        public const string IMPACT_SOURCE_NAME = "Audio_ObstacleImpact";

        // Posición local de la fuente: aproximadamente el centro de la carrocería. En cada impacto se mueve al punto de contacto.
        private static readonly Vector3 s_ImpactSourceLocalPosition = new Vector3(0f, 0.7f, 0f);

        // ------------------------------------------------------------------ Auto del jugador

        /// <summary>
        /// Agrega o actualiza <see cref="ObstacleContactSensor"/> en <c>Car08</c> y <c>Car07</c>, con la
        /// <c>AudioSource</c> hija, el grupo <c>Road</c> del mixer y el perfil de audio asignados.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Car Obstacle Sensor")]
        public static void SetupCarObstacleSensor()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ObstacleImpactAudioProfile>(ObstacleImpactAudioPlaceholderGenerator.PROFILE_PATH);
            if (profile == null)
            {
                Debug.LogError($"[ObstacleCollisionSetup] ❌ Falta el perfil {ObstacleImpactAudioPlaceholderGenerator.PROFILE_PATH}. " +
                               "Corré 'Generate Obstacle Impact Audio Placeholders' primero.");
                return;
            }

            AudioMixerGroup roadGroup = FindRoadGroup();
            if (roadGroup == null)
            {
                Debug.LogError("[ObstacleCollisionSetup] ❌ El mixer no tiene el grupo 'Road'; no se configura la salida de audio.");
                return;
            }

            foreach (string path in new[] { CAR08_PATH, CAR07_PATH })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"[ObstacleCollisionSetup] ⚠️ No existe {path}; se omite.");
                    continue;
                }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureCar(root, profile, roadGroup);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                Debug.Log($"[ObstacleCollisionSetup] ✅ ObstacleContactSensor listo en {path}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void ConfigureCar(GameObject root, ObstacleImpactAudioProfile profile, AudioMixerGroup roadGroup)
        {
            var sensor = GetOrAdd<ObstacleContactSensor>(root);

            Transform child = root.transform.Find(IMPACT_SOURCE_NAME);
            if (child == null)
            {
                var go = new GameObject(IMPACT_SOURCE_NAME);
                go.transform.SetParent(root.transform, false);
                child = go.transform;
            }

            child.localPosition = s_ImpactSourceLocalPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;

            var source = GetOrAdd<AudioSource>(child.gameObject);
            VehicleAudioUtil.ConfigureSource(source, false, profile.MinDistance, profile.MaxDistance, roadGroup, 64);
            source.clip = null;

            var so = new SerializedObject(sensor);
            so.FindProperty("m_AudioProfile").objectReferenceValue = profile;
            so.FindProperty("m_ImpactSource").objectReferenceValue = source;
            so.FindProperty("m_MixerGroup").objectReferenceValue = roadGroup;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Grupo <c>Road</c> de <c>VehicleAudioMixer</c> (no se crean grupos nuevos).</summary>
        public static AudioMixerGroup FindRoadGroup()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(SoundIntegrationSetup.MIXER_PATH);
            if (mixer == null) return null;

            return mixer.FindMatchingGroups("Road").FirstOrDefault(group => group.name == "Road");
        }

        // ------------------------------------------------------------------ Cono (prefab original)

        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";

        /// <summary>
        /// Deja el prefab original del cono con un <c>MeshCollider</c> convexo (material sin fricción) y un
        /// <see cref="ObstacleCollisionDetector"/> de tipo <see cref="ObstacleKind.Cone"/>. Sin Rigidbody: el cono
        /// es fijo. No toca la malla, el material visual ni la escala.
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup Obstacle Cone Prefab")]
        public static void SetupObstacleConePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH) == null)
            {
                Debug.LogError($"[ObstacleCollisionSetup] ❌ No existe el prefab del cono en {CONE_PREFAB_PATH}.");
                return;
            }

            PhysicsMaterial noFriction = LessonBoundarySetup.EnsureNoFrictionMaterial();

            GameObject root = PrefabUtility.LoadPrefabContents(CONE_PREFAB_PATH);
            try
            {
                var filter = root.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    Debug.LogError("[ObstacleCollisionSetup] ❌ El cono no tiene MeshFilter con malla; no se puede crear el collider.");
                    return;
                }

                var collider = GetOrAdd<MeshCollider>(root);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
                collider.isTrigger = false;
                collider.sharedMaterial = noFriction;

                // El cono es fijo: un Rigidbody lo haría derribable y la decisión es que no lo sea.
                var body = root.GetComponent<Rigidbody>();
                if (body != null) Object.DestroyImmediate(body);

                SetKind(GetOrAdd<ObstacleCollisionDetector>(root), ObstacleKind.Cone);
                PrefabUtility.SaveAsPrefabAsset(root, CONE_PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ObstacleCollisionSetup] ✅ Cono listo en {CONE_PREFAB_PATH} (MeshCollider convexo + detector, sin Rigidbody).");
        }

        // ------------------------------------------------------------------ Prefabs NPC

        /// <summary>Rutas de los prefabs de vehículo NPC (<c>NPC_*.prefab</c>), ordenadas por nombre.</summary>
        public static string[] FindNpcVehiclePaths() => FindPrefabPaths(NPCPrefabSetup.VEHICLE_OUTPUT_FOLDER, "NPC_");

        /// <summary>Rutas de los prefabs de peatón NPC (<c>NPC_Pedestrian_*.prefab</c>), ordenadas por nombre.</summary>
        public static string[] FindNpcPedestrianPaths() => FindPrefabPaths(NPCPrefabSetup.PEDESTRIAN_OUTPUT_FOLDER, "NPC_Pedestrian_");

        private static string[] FindPrefabPaths(string folder, string namePrefix)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return new string[0];

            return AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetFileName(path).StartsWith(namePrefix))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Agrega <see cref="ObstacleCollisionDetector"/> en la raíz (donde están el Rigidbody y el BoxCollider) de
        /// los vehículos NPC (<see cref="ObstacleKind.Vehicle"/>) y los peatones NPC (<see cref="ObstacleKind.Pedestrian"/>).
        /// </summary>
        [MenuItem("Tools/Primer Volante/Setup NPC Obstacle Detectors")]
        public static void SetupNpcObstacleDetectors()
        {
            int vehicles = SetupDetectorOnPrefabs(FindNpcVehiclePaths(), ObstacleKind.Vehicle);
            int pedestrians = SetupDetectorOnPrefabs(FindNpcPedestrianPaths(), ObstacleKind.Pedestrian);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ObstacleCollisionSetup] ✅ Detectores en {vehicles} vehículos NPC y {pedestrians} peatones NPC.");
        }

        private static int SetupDetectorOnPrefabs(string[] paths, ObstacleKind kind)
        {
            int count = 0;
            foreach (string path in paths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    SetKind(GetOrAdd<ObstacleCollisionDetector>(root), kind);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ Helpers

        private static void SetKind(ObstacleCollisionDetector detector, ObstacleKind kind)
        {
            var so = new SerializedObject(detector);
            so.FindProperty("m_Kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
