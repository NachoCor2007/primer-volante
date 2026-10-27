using System;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>Utilidades compartidas por las verificaciones de Editor que inspeccionan escenas guardadas.</summary>
    public static class VerificationSceneUtil
    {
        /// <summary>
        /// Ejecuta <paramref name="check"/> con la escena de <paramref name="path"/> cargada. Si la escena ya estaba
        /// abierta, la usa tal cual: no la cierra ni toca su estado dirty. Si no estaba abierta, la abre en modo
        /// aditivo y, al terminar, la cierra descartando cualquier cambio en memoria (no se guarda nada).
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
    }
}
