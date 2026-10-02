using System.IO;
using UnityEditor;
using UnityEngine;
using PrimerVolante.Characters;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor que crea el Prefab Variant UrbanManCustomizable a partir de UrbanManV1.prefab
    /// (ALSTRA INFINITE), con UrbanManCustomizer ya cableado: torso Hoodie Up, accesorios Cap/Glasses/Sunglasses y tonos de piel.
    /// Se trabaja sobre un Variant para no modificar el prefab original del asset.
    /// </summary>
    public static class UrbanManCustomizerSetup
    {
        private const string ASSET_ROOT = "Assets/ALSTRA INFINITE/PolyMate Starter/Urban Man/Prefabs/";
        public const string BASE_PREFAB_PATH = ASSET_ROOT + "UrbanManV1.prefab";
        public const string VARIANT_PREFAB_PATH = "Assets/Characters/UrbanManCustomizable.prefab";

        [MenuItem("Tools/Primer Volante/Setup Urban Man Customizable Prefab")]
        public static void CreateVariant()
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BASE_PREFAB_PATH);
            if (basePrefab == null)
            {
                Debug.LogError($"[UrbanManCustomizerSetup] ❌ No se encontró el prefab base en {BASE_PREFAB_PATH}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(VARIANT_PREFAB_PATH));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                var customizer = instance.GetComponent<UrbanManCustomizer>();
                if (customizer == null)
                {
                    customizer = instance.AddComponent<UrbanManCustomizer>();
                }

                var serialized = new SerializedObject(customizer);
                serialized.FindProperty("m_RootBone").objectReferenceValue = instance.transform.Find("root");

                SerializedProperty defaults = serialized.FindProperty("m_DefaultTorsoRenderers");
                defaults.arraySize = 1;
                defaults.GetArrayElementAtIndex(0).objectReferenceValue =
                    instance.transform.Find("Torso_HoodieV1_Down").GetComponent<SkinnedMeshRenderer>();

                SetTorsoOptions(serialized, ("HoodieUp", "Separated/Torso_HoodieV1_Up"));
                // Posiciones tomadas de la escena de demo del asset (personaje en el origen, rotación identidad).
                SetAccessoryOptions(serialized,
                    ("Cap", "Hat", "Accessories/CapV1", new Vector3(0f, 1.775f, -0.0024f)),
                    ("Glasses", "Eyewear", "Accessories/GlassesV1", new Vector3(0f, 1.7298f, 0.0889f)),
                    ("Sunglasses", "Eyewear", "Accessories/SunglassesV1", new Vector3(0f, 1.7298f, 0.0889f)));

                SetSkinRenderers(serialized, instance.transform, "Head_MV1", "Hands_MV1");
                SetSkinTones(serialized,
                    ("Light", new Color(0.96f, 0.80f, 0.69f)),
                    ("Fair", new Color(0.89f, 0.69f, 0.55f)),
                    ("Medium", new Color(0.78f, 0.57f, 0.42f)),
                    ("Olive", new Color(0.66f, 0.47f, 0.33f)),
                    ("Brown", new Color(0.50f, 0.34f, 0.24f)),
                    ("Dark", new Color(0.33f, 0.21f, 0.15f)));

                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(instance, VARIANT_PREFAB_PATH);
                Debug.Log($"[UrbanManCustomizerSetup] ✅ Prefab Variant creado en {VARIANT_PREFAB_PATH}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void SetTorsoOptions(SerializedObject serialized, params (string id, string prefab)[] options)
        {
            SerializedProperty list = serialized.FindProperty("m_TorsoOptions");
            list.arraySize = options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("m_Id").stringValue = options[i].id;
                element.FindPropertyRelative("m_Prefab").objectReferenceValue = LoadPart(options[i].prefab);
            }
        }

        private static void SetAccessoryOptions(SerializedObject serialized, params (string id, string slot, string prefab, Vector3 restPosition)[] options)
        {
            SerializedProperty list = serialized.FindProperty("m_AccessoryOptions");
            list.arraySize = options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("m_Id").stringValue = options[i].id;
                element.FindPropertyRelative("m_Slot").stringValue = options[i].slot;
                element.FindPropertyRelative("m_RestPosition").vector3Value = options[i].restPosition;
                element.FindPropertyRelative("m_Prefab").objectReferenceValue = LoadPart(options[i].prefab);
            }
        }

        private static void SetSkinRenderers(SerializedObject serialized, Transform root, params string[] names)
        {
            SerializedProperty list = serialized.FindProperty("m_SkinRenderers");
            list.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = root.Find(names[i]).GetComponent<SkinnedMeshRenderer>();
            }
        }

        private static void SetSkinTones(SerializedObject serialized, params (string id, Color color)[] tones)
        {
            SerializedProperty list = serialized.FindProperty("m_SkinTones");
            list.arraySize = tones.Length;
            for (int i = 0; i < tones.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("m_Id").stringValue = tones[i].id;
                element.FindPropertyRelative("m_Color").colorValue = tones[i].color;
            }
        }

        private static GameObject LoadPart(string relativePath)
        {
            string path = $"{ASSET_ROOT}{relativePath}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[UrbanManCustomizerSetup] ❌ No se encontró {path}");
            }
            return prefab;
        }
    }
}
