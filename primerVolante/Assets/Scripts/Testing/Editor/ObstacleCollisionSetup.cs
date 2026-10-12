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

        // ------------------------------------------------------------------ Helpers

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
