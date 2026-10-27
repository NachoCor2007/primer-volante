using System;
using System.Linq;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Pasos compartidos por los setups idempotentes de escenas demo (<c>GPSTurnSignalDemo</c>,
    /// <c>GPSLowBeamDemo</c>, <c>GPSWrongWayDemo</c>): abortar si hay escenas con cambios sin guardar (incluida la
    /// escena destino si ya está abierta), copiar la escena fuente si falta, abrirla y, para los detectores,
    /// agregar un componente al auto y configurarlo.
    /// </summary>
    public static class DemoSceneSetup
    {
        /// <summary>
        /// true si alguna escena abierta (incluida la escena destino, si ya está abierta) tiene cambios sin guardar.
        /// En ese caso loguea el error con el prefijo dado y no hay que tocar nada. Mira primero la escena destino.
        /// </summary>
        public static bool HasDirtyScenes(string logPrefix, string targetPath) => HasDirtyScenes(logPrefix, targetPath, out _);

        /// <summary>Igual que <see cref="HasDirtyScenes(string, string)"/>, y devuelve la escena sucia encontrada.</summary>
        public static bool HasDirtyScenes(string logPrefix, string targetPath, out Scene dirtyScene)
        {
            dirtyScene = default;

            Scene target = SceneManager.GetSceneByPath(targetPath);
            if (target.IsValid() && target.isLoaded && target.isDirty)
            {
                dirtyScene = target;
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene open = SceneManager.GetSceneAt(i);
                    if (!open.isDirty) continue;

                    dirtyScene = open;
                    break;
                }
            }

            if (!dirtyScene.IsValid()) return false;

            Debug.LogError($"{logPrefix} ❌ {dirtyScene.name} tiene cambios sin guardar: guardalos antes de abrir {targetPath}.");
            return true;
        }

        /// <summary>
        /// Deja abierta la escena <paramref name="targetPath"/>: si ya estaba abierta la usa; si no, la copia de
        /// <paramref name="sourcePath"/> cuando falta y la abre. Devuelve false (y loguea) si hay escenas sucias o si
        /// falla la copia. <paramref name="status"/> describe lo hecho (para el log final). Con
        /// <paramref name="skipDirtyCheck"/> no repite el chequeo de escenas sucias (si quien llama ya lo hizo).
        /// </summary>
        public static bool OpenOrCopyScene(string sourcePath, string targetPath, string sourceLabel, string logPrefix,
                                           out Scene scene, out string status, bool skipDirtyCheck = false)
        {
            status = null;
            scene = default;
            if (!skipDirtyCheck && HasDirtyScenes(logPrefix, targetPath)) return false;

            scene = SceneManager.GetSceneByPath(targetPath);
            if (scene.IsValid() && scene.isLoaded)
            {
                status = "ya estaba abierta";
                return true;
            }

            if (!System.IO.File.Exists(targetPath))
            {
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
                {
                    Debug.LogError($"{logPrefix} ❌ No se pudo copiar {sourcePath} a {targetPath}.");
                    return false;
                }

                status = $"copiada de {sourceLabel}";
            }
            else
            {
                status = "reutilizada";
            }

            scene = EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>
        /// Deja en la escena demo un componente <typeparamref name="T"/> en el auto <paramref name="carName"/> (con
        /// <see cref="VehicleController"/>), lo configura con <paramref name="configure"/> y guarda la escena. El
        /// componente se agrega como override de escena (no se toca el prefab). Idempotente. Devuelve una
        /// descripción de lo hecho o null si falló. Con <paramref name="skipDirtyCheck"/> no repite el chequeo de
        /// escenas sucias (si quien llama ya lo hizo con <see cref="HasDirtyScenes(string, string)"/>).
        /// </summary>
        public static string EnsureCarComponent<T>(string sourcePath, string targetPath, string carName, string sourceLabel,
                                                   string logPrefix, Action<SerializedObject> configure, bool skipDirtyCheck = false) where T : Component
        {
            if (!OpenOrCopyScene(sourcePath, targetPath, sourceLabel, logPrefix, out Scene scene, out string status, skipDirtyCheck)) return null;

            VehicleController car = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<VehicleController>(true))
                .FirstOrDefault(v => v.gameObject.name == carName);
            if (car == null)
            {
                Debug.LogError($"{logPrefix} ❌ {scene.name} no tiene un '{carName}' con VehicleController.");
                return null;
            }

            var component = car.GetComponent<T>();
            if (component == null)
            {
                component = Undo.AddComponent<T>(car.gameObject);
                status += ", detector agregado";
            }

            if (configure != null)
            {
                var so = new SerializedObject(component);
                configure(so);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return status;
        }
    }
}
