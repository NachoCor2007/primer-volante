using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>Utilidades compartidas por las verificaciones de Editor que inspeccionan escenas guardadas.</summary>
    public static class VerificationSceneUtil
    {
        private const string TMP_COPY_FOLDER_PARENT = "Assets/Scenes/TestingScenes";
        private const string TMP_COPY_FOLDER_NAME = "_TmpSceneCopy";

        /// <summary>
        /// Ejecuta <paramref name="check"/> con la escena de <paramref name="path"/> cargada. Si la escena ya estaba
        /// abierta, la usa tal cual: no la cierra ni toca su estado dirty. Si no estaba abierta, la abre en modo
        /// aditivo y, al terminar, la cierra descartando cualquier cambio en memoria (no se guarda nada).
        /// Solo para checks de SOLO LECTURA: si el check modifica objetos, usar <see cref="WithSceneCopy"/>.
        /// </summary>
        public static void WithScene(string path, Action<Scene> check)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                check(scene);
            }
            finally
            {
                if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Ejecuta <paramref name="check"/> sobre una COPIA temporal de la escena guardada en <paramref name="path"/>,
        /// abierta en modo aditivo, para checks que modifican objetos (Initialize, abrir menús, congelar el Rigidbody...).
        /// Nunca toca la escena original, esté o no abierta. Al terminar cierra la copia sin guardar y borra el asset
        /// temporal. La copia sale del archivo en disco: los cambios sin guardar de la escena abierta no se reflejan.
        /// </summary>
        public static void WithSceneCopy(string path, Action<Scene> check)
        {
            string folder = TMP_COPY_FOLDER_PARENT + "/" + TMP_COPY_FOLDER_NAME;
            string copyPath = folder + "/" + System.IO.Path.GetFileName(path);

            AssetDatabase.DeleteAsset(folder);
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(TMP_COPY_FOLDER_PARENT, TMP_COPY_FOLDER_NAME);

            Scene scene = default;
            try
            {
                if (!AssetDatabase.CopyAsset(path, copyPath)) throw new InvalidOperationException($"No se pudo copiar {path} a {copyPath}");
                scene = EditorSceneManager.OpenScene(copyPath, OpenSceneMode.Additive);
                check(scene);
            }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
