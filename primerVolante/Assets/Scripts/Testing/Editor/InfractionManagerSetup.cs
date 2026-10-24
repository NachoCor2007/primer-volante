using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Evaluation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de US22 (#15): crea o actualiza las definiciones de infracción y el
    /// catálogo en <c>Assets/Settings/Evaluation/</c>, copia <c>CityMerged3</c> a <c>CityWithInfractions</c> (si
    /// no existe) y deja un <see cref="InfractionManager"/> con el catálogo asignado en esa escena.
    /// </summary>
    public static class InfractionManagerSetup
    {
        public const string FOLDER = "Assets/Settings/Evaluation";
        public const string CATALOG_PATH = FOLDER + "/InfractionCatalog.asset";
        public const string SOURCE_SCENE_PATH = "Assets/Scenes/car09/CityMerged3.unity";
        public const string SCENE_PATH = "Assets/Scenes/car09/CityWithInfractions.unity";
        public const string MANAGER_OBJECT_NAME = "InfractionManager";

        /// <summary>Datos de autoría de una infracción: lo que va en cada <see cref="InfractionDefinitionSO"/>.</summary>
        public readonly struct Spec
        {
            public readonly string Id;
            public readonly string ShortTitle;
            public readonly string Template;
            public readonly InfractionSeverity Severity;
            public readonly string[] ParameterKeys;

            public Spec(string id, string shortTitle, string template, InfractionSeverity severity, params string[] parameterKeys)
            {
                Id = id;
                ShortTitle = shortTitle;
                Template = template;
                Severity = severity;
                ParameterKeys = parameterKeys;
            }
        }

        /// <summary>Las 4 infracciones actuales, en el orden del catálogo.</summary>
        public static readonly Spec[] Specs =
        {
            new Spec(InfractionIds.RedLight, "Semáforo en rojo",
                "Cruzaste la línea de detención con el semáforo en rojo. Frená antes de la línea y esperá la luz verde.",
                InfractionSeverity.Major),
            new Spec(InfractionIds.SpeedLimit, "Exceso de velocidad",
                "Superaste el límite de {limit} km/h en este tramo. Respetá la velocidad máxima señalizada.",
                InfractionSeverity.Minor, "limit"),
            new Spec(InfractionIds.Crash, "Choque",
                "Chocaste con {object} a {speed} km/h. Reducí la velocidad y mantené distancia con los obstáculos.",
                InfractionSeverity.Major, "object", "speed"),
            new Spec(InfractionIds.Graze, "Roce",
                "Rozaste {object} a {speed} km/h. Calculá mejor las distancias al maniobrar.",
                InfractionSeverity.Minor, "object", "speed"),
        };

        /// <summary>Ruta del asset de una definición: <c>Infraction_&lt;Id&gt;.asset</c>.</summary>
        public static string DefinitionPath(string id) => $"{FOLDER}/Infraction_{id}.asset";

        [MenuItem("Tools/Primer Volante/Setup Infraction Manager")]
        public static void Setup()
        {
            Debug.Log("[InfractionManagerSetup] 🔧 Configurando el gestor de infracciones (US22)...");

            int created = 0, updated = 0;
            InfractionCatalogSO catalog = EnsureCatalog(ref created, ref updated);
            if (catalog == null) return;

            string sceneStatus = EnsureScene(catalog);
            if (sceneStatus == null) return;

            Debug.Log($"[InfractionManagerSetup] ✅ Listo. Assets creados: {created}, actualizados: {updated}. " +
                      $"Catálogo con {catalog.Definitions.Count} definiciones. Escena {SCENE_PATH}: {sceneStatus}.");
        }

        // ------------------------------------------------------------------ Catálogo

        private static InfractionCatalogSO EnsureCatalog(ref int created, ref int updated)
        {
            EnsureFolder("Assets/Settings");
            EnsureFolder(FOLDER);

            var definitions = new List<InfractionDefinitionSO>();
            foreach (Spec spec in Specs)
            {
                string path = DefinitionPath(spec.Id);
                var definition = AssetDatabase.LoadAssetAtPath<InfractionDefinitionSO>(path);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<InfractionDefinitionSO>();
                    AssetDatabase.CreateAsset(definition, path);
                    created++;
                }
                else
                {
                    updated++;
                }

                var so = new SerializedObject(definition);
                so.FindProperty("m_Id").stringValue = spec.Id;
                so.FindProperty("m_ShortTitle").stringValue = spec.ShortTitle;
                so.FindProperty("m_DescriptionTemplate").stringValue = spec.Template;
                so.FindProperty("m_Severity").enumValueIndex = (int)spec.Severity;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);
                definitions.Add(definition);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<InfractionCatalogSO>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
                created++;
            }
            else
            {
                updated++;
            }

            // Garantiza que estén las 4 definiciones (en orden) sin quitar otras que alguien haya sumado después.
            var catalogSo = new SerializedObject(catalog);
            SerializedProperty list = catalogSo.FindProperty("m_Definitions");
            var current = new List<InfractionDefinitionSO>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i).objectReferenceValue as InfractionDefinitionSO;
                if (item != null && !definitions.Contains(item)) current.Add(item);
            }

            var finalList = definitions.Concat(current).ToList();
            list.arraySize = finalList.Count;
            for (int i = 0; i < finalList.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = finalList[i];
            }

            catalogSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return catalog;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ Escena

        // Devuelve una descripción de lo hecho o null si falló.
        private static string EnsureScene(InfractionCatalogSO catalog)
        {
            var status = new List<string>();

            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene open = SceneManager.GetSceneAt(i);
                    if (open.isDirty)
                    {
                        Debug.LogError($"[InfractionManagerSetup] ❌ {open.name} tiene cambios sin guardar: guardalos antes de abrir {SCENE_PATH}.");
                        return null;
                    }
                }

                if (!System.IO.File.Exists(SCENE_PATH))
                {
                    if (!AssetDatabase.CopyAsset(SOURCE_SCENE_PATH, SCENE_PATH))
                    {
                        Debug.LogError($"[InfractionManagerSetup] ❌ No se pudo copiar {SOURCE_SCENE_PATH} a {SCENE_PATH}.");
                        return null;
                    }

                    status.Add("copiada de CityMerged3");
                }
                else
                {
                    status.Add("reutilizada");
                }

                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }
            else
            {
                status.Add("ya estaba abierta");
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[InfractionManagerSetup] ❌ No se pudo abrir {SCENE_PATH}.");
                return null;
            }

            GameObject root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == MANAGER_OBJECT_NAME);
            if (root == null)
            {
                root = new GameObject(MANAGER_OBJECT_NAME);
                SceneManager.MoveGameObjectToScene(root, scene);
                status.Add("GameObject InfractionManager creado");
            }

            var manager = root.GetComponent<InfractionManager>();
            if (manager == null) manager = root.AddComponent<InfractionManager>();

            var so = new SerializedObject(manager);
            so.FindProperty("m_Catalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            status.Add("catálogo asignado y escena guardada");
            return string.Join(", ", status);
        }
    }
}
