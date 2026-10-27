using System.Collections.Generic;
using System.Linq;
using PrimerVolante.Environment;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor para llevar la detección de giro sin señalizar (<c>TURN_SIGNAL</c>) a otras escenas:
    /// agrega (o corrige) un <see cref="TurnSignalDetector"/> como componente de la <b>instancia</b> del auto del
    /// jugador en la escena, igual que en <c>GPSTurnSignalDemo</c>, sin tocar los prefabs del auto. También lo saca.
    /// Antes asegura la capa, las zonas exentas de los prefabs, el asset y el catálogo (reutiliza
    /// <see cref="TurnSignalSetup.EnsureAssets"/>). Guía: <c>ACTIVAR_GUINO_EN_ESCENAS.md</c> en la raíz del repo.
    /// </summary>
    public static class TurnSignalSceneTool
    {
        private const string MENU_ROOT = "Tools/Primer Volante/Turn Signal/";

        // ------------------------------------------------------------------ MenuItems

        [MenuItem(MENU_ROOT + "Apply To Open Scene")]
        public static void ApplyToOpenScene()
        {
            ApplyToLoadedScene(SceneManager.GetActiveScene());
        }

        [MenuItem(MENU_ROOT + "Apply To Selected Scenes")]
        public static void ApplyToSelectedScenes()
        {
            string[] paths = SelectedScenePaths();
            if (paths.Length == 0)
            {
                Debug.LogWarning("[TurnSignalSceneTool] ⚠️ No hay escenas seleccionadas en la ventana Project.");
                return;
            }

            if (!Application.isBatchMode)
            {
                string list = string.Join("\n", paths);
                bool confirmed = EditorUtility.DisplayDialog("Giro sin señalizar",
                    $"Se va a abrir, modificar y guardar {paths.Length} escena(s):\n\n{list}\n\n¿Continuar?", "Aplicar", "Cancelar");
                if (!confirmed) return;
            }

            ApplyToScenes(paths);
        }

        [MenuItem(MENU_ROOT + "Remove From Open Scene")]
        public static void RemoveFromOpenScene()
        {
            RemoveFromLoadedScene(SceneManager.GetActiveScene());
        }

        // ------------------------------------------------------------------ API pública (sin diálogos)

        /// <summary>
        /// Aplica la detección a una escena ya cargada (la activa en el MenuItem). Si la escena tenía cambios sin
        /// guardar antes de aplicar, <b>aborta</b> con un error y no guarda ni modifica nada. Devuelve la cantidad de
        /// autos procesados, o -1 si abortó.
        /// </summary>
        public static int ApplyToLoadedScene(Scene scene)
        {
            if (!CanProcess(scene)) return -1;
            if (TurnSignalSetup.EnsureAssets(out _, out _) < 0) return -1;

            return ApplyToScene(scene, LayerMask.NameToLayer(RoadLaneZone.LayerName));
        }

        /// <summary>Saca el detector de una escena ya cargada; aborta con un error si tenía cambios sin guardar. Devuelve cuántos sacó, o -1 si abortó.</summary>
        public static int RemoveFromLoadedScene(Scene scene)
        {
            if (!CanProcess(scene)) return -1;

            return RemoveFromScene(scene);
        }

        /// <summary>
        /// Aplica la detección a cada escena de <paramref name="paths"/>: la abre, agrega o corrige el detector en
        /// el auto del jugador, la guarda y, al final, reabre la escena que estaba abierta. No pide confirmación.
        /// Devuelve false si no pudo procesar alguna escena.
        /// </summary>
        public static bool ApplyToScenes(string[] paths)
        {
            return ProcessScenes(paths, applying: true);
        }

        /// <summary>Saca el detector de cada escena de <paramref name="paths"/> (misma mecánica que <see cref="ApplyToScenes"/>).</summary>
        public static bool RemoveFromScenes(string[] paths)
        {
            return ProcessScenes(paths, applying: false);
        }

        /// <summary>
        /// Agrega o corrige el detector en el auto del jugador de <paramref name="scene"/> (ya cargada) y la guarda.
        /// Devuelve la cantidad de autos procesados (0 si no encontró el auto).
        /// </summary>
        public static int ApplyToScene(Scene scene, int layer)
        {
            List<VehicleController> cars = FindPlayerCars(scene);
            if (cars.Count == 0)
            {
                Debug.LogWarning($"[TurnSignalSceneTool] ⚠️ {scene.name}: no se encontró el auto del jugador (Car07/Car08/Car09 o un VehicleController con WrongWayDetector). No se hizo nada.");
                return 0;
            }

            foreach (VehicleController car in cars)
            {
                var detector = car.GetComponent<TurnSignalDetector>();
                string status = "ya tenía el detector";
                if (detector == null)
                {
                    detector = Undo.AddComponent<TurnSignalDetector>(car.gameObject);
                    status = "detector agregado";
                }

                var so = new SerializedObject(detector);
                SerializedProperty bits = so.FindProperty("m_ZoneMask").FindPropertyRelative("m_Bits");
                if (bits.intValue != 1 << layer)
                {
                    bits.intValue = 1 << layer;
                    status += ", máscara corregida";
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(detector);
                Debug.Log($"[TurnSignalSceneTool] ✅ {scene.name}: {car.gameObject.name} ({status}).");
            }

            if (cars.Count > 1)
            {
                Debug.Log($"[TurnSignalSceneTool] ℹ️ {scene.name}: había {cars.Count} autos del jugador y se procesaron todos.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return cars.Count;
        }

        /// <summary>Saca el <see cref="TurnSignalDetector"/> de <paramref name="scene"/> (ya cargada) y la guarda. Devuelve cuántos sacó.</summary>
        public static int RemoveFromScene(Scene scene)
        {
            TurnSignalDetector[] detectors = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<TurnSignalDetector>(true))
                .ToArray();
            if (detectors.Length == 0)
            {
                Debug.LogWarning($"[TurnSignalSceneTool] ⚠️ {scene.name}: no hay ningún TurnSignalDetector que sacar.");
                return 0;
            }

            foreach (TurnSignalDetector detector in detectors)
            {
                string carName = detector.gameObject.name;
                Undo.DestroyObjectImmediate(detector);
                Debug.Log($"[TurnSignalSceneTool] ✅ {scene.name}: detector sacado de {carName}.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return detectors.Length;
        }

        /// <summary>
        /// Autos del jugador de la escena: las instancias de Car07/Car08/Car09, o cualquier objeto con
        /// <see cref="VehicleController"/> y <see cref="WrongWayDetector"/>.
        /// </summary>
        public static List<VehicleController> FindPlayerCars(Scene scene)
        {
            var cars = new List<VehicleController>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (VehicleController vehicle in root.GetComponentsInChildren<VehicleController>(true))
                {
                    if (vehicle.GetComponent<Rigidbody>() == null) continue;
                    if (vehicle.GetComponent<WrongWayDetector>() != null || IsPlayerCarInstance(vehicle.gameObject)) cars.Add(vehicle);
                }
            }

            return cars;
        }

        // ------------------------------------------------------------------ Internos

        private static bool IsPlayerCarInstance(GameObject go)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (source == null) return false;

            string path = AssetDatabase.GetAssetPath(source);
            return WrongWaySetup.CarPaths.Contains(path);
        }

        private static bool ProcessScenes(string[] paths, bool applying)
        {
            if (paths == null || paths.Length == 0) return true;

            int layer = -1;
            if (applying)
            {
                layer = TurnSignalSetup.EnsureAssets(out _, out _);
                if (layer < 0) return false;
            }

            if (HasUnsavedScene())
            {
                Debug.LogError("[TurnSignalSceneTool] ❌ Hay escenas abiertas con cambios sin guardar: guardalas antes de procesar otras escenas.");
                return false;
            }

            string original = SceneManager.GetActiveScene().path;
            bool ok = true;
            try
            {
                foreach (string path in paths)
                {
                    if (!System.IO.File.Exists(path))
                    {
                        Debug.LogError($"[TurnSignalSceneTool] ❌ No existe la escena {path}.");
                        ok = false;
                        continue;
                    }

                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (applying)
                    {
                        if (ApplyToScene(scene, layer) == 0) ok = false;
                    }
                    else
                    {
                        RemoveFromScene(scene);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(original) && System.IO.File.Exists(original))
                {
                    EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
                }
                else
                {
                    // La escena de partida no estaba guardada: no se puede reabrir; se deja una vacía.
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }

            return ok;
        }

        private static bool CanProcess(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[TurnSignalSceneTool] ❌ La escena abierta no está guardada en disco: guardala primero.");
                return false;
            }

            if (scene.isDirty)
            {
                Debug.LogError($"[TurnSignalSceneTool] ❌ {scene.name} tiene cambios sin guardar: guardalos o descartalos antes de aplicar la herramienta (no se guardó nada).");
                return false;
            }

            return true;
        }

        private static bool HasUnsavedScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            }

            return false;
        }

        private static string[] SelectedScenePaths()
        {
            return Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToArray();
        }
    }
}
