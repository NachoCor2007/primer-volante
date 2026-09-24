using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    [InitializeOnLoad]
    public static class DemoComponentsSetup
    {
        private const string SCENE_DIR = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Scene";
        private const string SOURCE_SCENE_PATH = SCENE_DIR + "/DemoWithXR.unity";
        private const string TARGET_SCENE_PATH = SCENE_DIR + "/DemoComponents.unity";
        private const string PREFAB_DIR = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs";
        private const string CAR04_PREFAB_PATH_1 = PREFAB_DIR + "/Car04.prefab";
        private const string CAR04_PREFAB_PATH_2 = PREFAB_DIR + "/car04.prefab";
        private static readonly string FLAG_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "DemoComponentsSetup_Executed.flag");
        private static readonly string RESULT_FILE = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "DemoComponentsSetup_Result.txt");

        static DemoComponentsSetup()
        {
            EditorApplication.delayCall += OnEditorLoaded;
        }

        private static void OnEditorLoaded()
        {
            if (File.Exists(FLAG_FILE))
            {
                return;
            }

            Debug.Log("[DemoComponentsSetup] 🚀 Triggering automatic execution via InitializeOnLoad...");
            ExecuteSetup();
        }

        [MenuItem("Tools/Primer Volante/Duplicate And Setup DemoComponents Scene")]
        public static void MenuExecuteSetup()
        {
            Debug.Log("[DemoComponentsSetup] 🔘 Manual menu trigger activated.");
            ExecuteSetup();
        }

        public static void ExecuteSetup()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== DemoComponents Setup Execution Log ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // Paso 1 & 2: Duplicar la escena DemoWithXR.unity como DemoComponents.unity
                sb.AppendLine("\n--- Paso 1 y 2: Duplicando escena ---");
                if (!AssetDatabase.CopyAsset(SOURCE_SCENE_PATH, TARGET_SCENE_PATH))
                {
                    sb.AppendLine($"AssetDatabase.CopyAsset fallo o el destino ya existe. Intentando File.Copy...");
                    string fullSource = Path.Combine(Directory.GetCurrentDirectory(), SOURCE_SCENE_PATH);
                    string fullTarget = Path.Combine(Directory.GetCurrentDirectory(), TARGET_SCENE_PATH);
                    File.Copy(fullSource, fullTarget, true);
                    AssetDatabase.ImportAsset(TARGET_SCENE_PATH, ImportAssetOptions.ForceUpdate);
                }
                AssetDatabase.Refresh();
                sb.AppendLine($"Escena duplicada exitosamente en: {TARGET_SCENE_PATH}");

                // Paso 3: Abrir la escena recién creada DemoComponents.unity
                sb.AppendLine("\n--- Paso 3: Abriendo escena DemoComponents.unity ---");
                Scene targetScene = EditorSceneManager.OpenScene(TARGET_SCENE_PATH, OpenSceneMode.Single);
                if (!targetScene.IsValid())
                {
                    throw new Exception($"No se pudo abrir la escena en {TARGET_SCENE_PATH}");
                }
                sb.AppendLine($"Escena abierta: {targetScene.name} (Ruta: {targetScene.path})");

                // Paso 4: Localizar el GameObject car03 en la jerarquia
                sb.AppendLine("\n--- Paso 4: Localizando GameObject car03 ---");
                GameObject car03 = null;
                GameObject[] roots = targetScene.GetRootGameObjects();

                foreach (var r in roots)
                {
                    if (r.name.Equals("Car03", StringComparison.OrdinalIgnoreCase) ||
                        r.name.StartsWith("Car03", StringComparison.OrdinalIgnoreCase))
                    {
                        car03 = r;
                        break;
                    }
                }

                if (car03 == null)
                {
                    // Fallback: search all gameobjects
                    foreach (var r in roots)
                    {
                        foreach (var t in r.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.name.Equals("Car03", StringComparison.OrdinalIgnoreCase) ||
                                t.name.StartsWith("Car03", StringComparison.OrdinalIgnoreCase))
                            {
                                car03 = t.gameObject;
                                break;
                            }
                        }
                        if (car03 != null) break;
                    }
                }

                if (car03 == null)
                {
                    throw new Exception("No se encontró el GameObject correspondiente a car03 en la escena.");
                }

                sb.AppendLine($"GameObject car03 encontrado: '{car03.name}'");
                sb.AppendLine($"Posicion Car03: {car03.transform.localPosition}");
                sb.AppendLine($"Rotacion Car03: {car03.transform.localRotation.eulerAngles}");
                sb.AppendLine($"Escala Car03: {car03.transform.localScale}");

                // Paso 5: Instanciar prefab car04
                sb.AppendLine("\n--- Paso 5: Instanciando prefab car04 ---");
                string car04Path = File.Exists(CAR04_PREFAB_PATH_1) ? CAR04_PREFAB_PATH_1 : CAR04_PREFAB_PATH_2;
                GameObject car04Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(car04Path);
                if (car04Prefab == null)
                {
                    throw new Exception($"No se pudo cargar el prefab car04 en {car04Path}");
                }

                GameObject car04 = (GameObject)PrefabUtility.InstantiatePrefab(car04Prefab, targetScene);
                car04.name = "Car04";
                sb.AppendLine($"Prefab car04 instanciado como '{car04.name}'");

                // Paso 6: Copiar exactamente los valores del Transform de car03 a car04
                sb.AppendLine("\n--- Paso 6: Copiando valores del Transform ---");
                car04.transform.SetParent(car03.transform.parent, false);
                car04.transform.localPosition = car03.transform.localPosition;
                car04.transform.localRotation = car03.transform.localRotation;
                car04.transform.localScale = car03.transform.localScale;
                car04.transform.SetSiblingIndex(car03.transform.GetSiblingIndex());

                sb.AppendLine($"Posicion Car04 aplicada: {car04.transform.localPosition}");
                sb.AppendLine($"Rotacion Car04 aplicada: {car04.transform.localRotation.eulerAngles}");
                sb.AppendLine($"Escala Car04 aplicada: {car04.transform.localScale}");

                // Paso 7 & 8: Escanear y mapear todas las referencias de car03 y sus hijos a car04 y sus componentes
                sb.AppendLine("\n--- Paso 7 y 8: Escaneando y reemplazando referencias ---");
                Dictionary<UnityEngine.Object, UnityEngine.Object> objectMap = BuildObjectMap(car03, car04, sb);

                sb.AppendLine($"\nMapeo completado con {objectMap.Count} equivalencias.");

                int replacedCount = 0;
                List<GameObject> allSceneObjects = new List<GameObject>();
                foreach (var r in targetScene.GetRootGameObjects())
                {
                    allSceneObjects.AddRange(r.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject));
                }

                foreach (var go in allSceneObjects)
                {
                    // No tocar car03 (que se eliminara) ni car04 (que es el nuevo)
                    if (go == car03 || go.transform.IsChildOf(car03.transform)) continue;
                    if (go == car04 || go.transform.IsChildOf(car04.transform)) continue;

                    Component[] components = go.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;

                        SerializedObject so = new SerializedObject(comp);
                        SerializedProperty prop = so.GetIterator();
                        bool soModified = false;

                        while (prop.Next(true))
                        {
                            if (prop.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                UnityEngine.Object currentVal = prop.objectReferenceValue;
                                if (currentVal != null && objectMap.TryGetValue(currentVal, out UnityEngine.Object newVal))
                                {
                                    sb.AppendLine($"[REEMPLAZO SERIALIZADO] GameObject: '{go.name}', Componente: '{comp.GetType().Name}', Propiedad: '{prop.propertyPath}', De: '{currentVal.name}' ({currentVal.GetType().Name}) -> A: '{newVal.name}' ({newVal.GetType().Name})");
                                    prop.objectReferenceValue = newVal;
                                    soModified = true;
                                    replacedCount++;
                                }
                            }
                        }

                        if (soModified)
                        {
                            so.ApplyModifiedProperties();
                            EditorUtility.SetDirty(comp);
                        }

                        // Tambien verificar campos publicos por reflection
                        FieldInfo[] fields = comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                        foreach (var f in fields)
                        {
                            if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                            {
                                var currentVal = f.GetValue(comp) as UnityEngine.Object;
                                if (currentVal != null && objectMap.TryGetValue(currentVal, out UnityEngine.Object newVal))
                                {
                                    sb.AppendLine($"[REEMPLAZO REFLECTION] GameObject: '{go.name}', Componente: '{comp.GetType().Name}', Campo: '{f.Name}', De: '{currentVal.name}' -> A: '{newVal.name}'");
                                    f.SetValue(comp, newVal);
                                    EditorUtility.SetDirty(comp);
                                }
                            }
                        }
                    }
                }

                sb.AppendLine($"Total de referencias reasignadas: {replacedCount}");

                // Paso 9: Eliminar car03 original de la jerarquia
                sb.AppendLine("\n--- Paso 9: Eliminando GameObject car03 original ---");
                UnityEngine.Object.DestroyImmediate(car03);
                sb.AppendLine("car03 eliminado correctamente de la jerarquia.");

                // Paso 10: Guardar los cambios en la escena DemoComponents
                sb.AppendLine("\n--- Paso 10: Guardando escena DemoComponents ---");
                EditorSceneManager.MarkSceneDirty(targetScene);
                bool saved = EditorSceneManager.SaveScene(targetScene);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                sb.AppendLine($"Escena guardada: {saved}");
                sb.AppendLine("\n✅ PROCESO COMPLETADO CON ÉXITO.");
                Debug.Log(sb.ToString());

                // Escribir archivo flag y archivo de resultado
                File.WriteAllText(FLAG_FILE, DateTime.Now.ToString("o"));
                File.WriteAllText(RESULT_FILE, sb.ToString());
            }
            catch (Exception ex)
            {
                sb.AppendLine($"\n❌ ERROR DURANTE LA EJECUCION: {ex.Message}\n{ex.StackTrace}");
                Debug.LogError(sb.ToString());
                File.WriteAllText(RESULT_FILE, sb.ToString());
            }
        }

        private static Dictionary<UnityEngine.Object, UnityEngine.Object> BuildObjectMap(GameObject sourceRoot, GameObject targetRoot, StringBuilder sb)
        {
            var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();

            // 1. Mapear root GameObject
            map[sourceRoot] = targetRoot;

            // 2. Mapear componentes en el root
            Component[] sourceRootComponents = sourceRoot.GetComponents<Component>();
            foreach (var sc in sourceRootComponents)
            {
                if (sc == null) continue;
                Component tc = targetRoot.GetComponent(sc.GetType());
                if (tc != null)
                {
                    map[sc] = tc;
                    sb.AppendLine($"Mapeado Componente Root: {sc.GetType().Name} -> {tc.GetType().Name}");
                }
            }

            // 3. Mapear todos los Transforms e hijos recursivamente
            Transform[] sourceChildren = sourceRoot.GetComponentsInChildren<Transform>(true);
            Transform[] targetChildren = targetRoot.GetComponentsInChildren<Transform>(true);

            foreach (var st in sourceChildren)
            {
                if (st == sourceRoot.transform)
                {
                    map[st] = targetRoot.transform;
                    continue;
                }

                string relativePath = AnimationUtility.CalculateTransformPath(st, sourceRoot.transform);
                Transform targetChild = targetRoot.transform.Find(relativePath);

                if (targetChild == null)
                {
                    // Fallback por nombre
                    targetChild = targetChildren.FirstOrDefault(t => t.name.Equals(st.name, StringComparison.OrdinalIgnoreCase));
                }

                if (targetChild != null)
                {
                    map[st.gameObject] = targetChild.gameObject;
                    map[st] = targetChild;
                    sb.AppendLine($"Mapeado GameObject Hijo: '{st.name}' (Ruta: {relativePath}) -> '{targetChild.name}'");

                    // Mapear componentes de los hijos
                    Component[] childSourceComps = st.GetComponents<Component>();
                    foreach (var csc in childSourceComps)
                    {
                        if (csc == null) continue;
                        Component ctc = targetChild.GetComponent(csc.GetType());
                        if (ctc != null)
                        {
                            map[csc] = ctc;
                            sb.AppendLine($"  Mapeado Componente Hijo: {csc.GetType().Name} en {st.name} -> {ctc.GetType().Name} en {targetChild.name}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"[AVISO] No se encontro equivalente en target para hijo: '{st.name}' (Ruta: {relativePath})");
                }
            }

            return map;
        }
    }
}
