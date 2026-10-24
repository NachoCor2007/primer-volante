using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrimerVolante.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Escena <c>CityWithWrongWay</c>: copia de <c>CityWithInfractions</c> con una calle de mano única de
    /// demostración y sus carteles bajo <c>OneWayDemo</c>. Es la única escena que guarda esta herramienta.
    /// </summary>
    public static partial class WrongWaySetup
    {
        public const string SOURCE_SCENE_PATH = "Assets/Scenes/car09/CityWithInfractions.unity";
        public const string SCENE_PATH = "Assets/Scenes/car09/CityWithWrongWay.unity";
        public const string DEMO_ROOT_NAME = "OneWayDemo";
        public const string DEMO_INFO_PREFIX = "RoadPieces";
        public const string DEMO_EXIT_SIGN_NAME = "OneWayProhibitedSign_Exit";
        public const string DEMO_ENTRY_SIGN_NAME = "OneWayDirectionSign_Entry";

        /// <summary>
        /// Calle de demostración: 5 piezas <c>Road Lane_01</c> seguidas sobre z = 160, de x = 140 a x = 220 (de
        /// una <c>Road Intersection_01</c> en x = 120 a una <c>Road T_Intersection_01</c> en x = 240), con el
        /// tránsito permitido hacia +X (este). Ninguna ruta de NPC pasa por ahí (las rutas de vehículos están en
        /// x 45..117 y z 3..124).
        /// </summary>
        public static readonly Vector2[] DemoPiecePositions =
        {
            new Vector2(140f, 160f), new Vector2(160f, 160f), new Vector2(180f, 160f),
            new Vector2(200f, 160f), new Vector2(220f, 160f),
        };

        /// <summary>Hacia dónde se permite circular en la calle de demostración (mundo).</summary>
        public static readonly Vector3 DemoTravelDirection = Vector3.right;

        // Distancia de los carteles al eje de la calle (cordón en 7.15, vereda de 7.15 a 10) y al borde de la pieza.
        private const float k_SignOffsetFromAxis = 8.5f;
        private const float k_SignOffsetFromEnd = 3f;
        private const float k_SidewalkHeight = 0.2f;

        // Devuelve una descripción de lo hecho (vacía si falló).
        private static string SetupDemoScene()
        {
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            string status;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene open = SceneManager.GetSceneAt(i);
                    if (open.isDirty)
                    {
                        Debug.LogError($"[WrongWaySetup] ❌ {open.name} tiene cambios sin guardar: guardalos antes de abrir {SCENE_PATH}.");
                        return "Escena de demostración: no se pudo abrir.";
                    }
                }

                if (!File.Exists(SCENE_PATH))
                {
                    if (!AssetDatabase.CopyAsset(SOURCE_SCENE_PATH, SCENE_PATH))
                    {
                        Debug.LogError($"[WrongWaySetup] ❌ No se pudo copiar {SOURCE_SCENE_PATH} a {SCENE_PATH}.");
                        return "Escena de demostración: no se pudo copiar.";
                    }

                    status = "copiada de CityWithInfractions";
                }
                else
                {
                    status = "reutilizada";
                }

                scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            }
            else
            {
                status = "ya estaba abierta";
            }

            if (!SetupDemoStreet(scene, out string summary)) return "Escena de demostración: falló (ver errores).";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Escena {SCENE_PATH} ({status}): {summary}";
        }

        private static bool SetupDemoStreet(Scene scene, out string summary)
        {
            summary = string.Empty;

            var prohibited = AssetDatabase.LoadAssetAtPath<GameObject>(PROHIBITED_PREFAB_PATH);
            var direction = AssetDatabase.LoadAssetAtPath<GameObject>(DIRECTION_PREFAB_PATH);
            if (prohibited == null || direction == null)
            {
                Debug.LogError("[WrongWaySetup] ❌ Faltan los prefabs de los carteles.");
                return false;
            }

            // 1. Piezas de la calle de demostración.
            var pieces = new List<RoadLaneDirection>();
            foreach (Vector2 position in DemoPiecePositions)
            {
                RoadLaneDirection piece = FindLanePiece(scene, position);
                if (piece == null)
                {
                    Debug.LogError($"[WrongWaySetup] ❌ No se encontró una instancia de Road Lane_01 con RoadLaneDirection en ({position.x}, {position.y}).");
                    return false;
                }

                pieces.Add(piece);
            }

            // 2. Mano única hacia DemoTravelDirection, con la misma lógica del MenuItem de la Scene view.
            foreach (RoadLaneDirection piece in pieces)
            {
                TrafficFlow flow = RoadLaneDirectionTools.FlowFromDirection(piece.transform.forward, DemoTravelDirection, out bool ambiguous);
                if (ambiguous)
                {
                    Debug.LogError($"[WrongWaySetup] ❌ {piece.name} es perpendicular a la calle de demostración.");
                    return false;
                }

                RoadLaneDirectionTools.SetFlow(piece, flow);
            }

            // 3. Extremos de la calle a lo largo del sentido permitido (cada Road Lane_01 mide 20 m).
            Vector3 along = DemoTravelDirection.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, along);
            RoadLaneDirection first = pieces.OrderBy(p => Vector3.Dot(p.transform.position, along)).First();
            RoadLaneDirection last = pieces.OrderByDescending(p => Vector3.Dot(p.transform.position, along)).First();
            Vector3 start = first.transform.position - along * 10f;
            Vector3 end = last.transform.position + along * 10f;

            // 4. Carteles bajo OneWayDemo.
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == DEMO_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(DEMO_ROOT_NAME);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            // Entrada: a la derecha de quien entra a favor del sentido, mirando hacia él (contra el sentido).
            PlaceSign(scene, root.transform, direction, DEMO_ENTRY_SIGN_NAME,
                start + along * k_SignOffsetFromEnd + right * k_SignOffsetFromAxis, Quaternion.LookRotation(-along));

            // Salida: a la derecha de quien entraría en contra (o sea, a la izquierda del sentido), mirando hacia él.
            PlaceSign(scene, root.transform, prohibited, DEMO_EXIT_SIGN_NAME,
                end - along * k_SignOffsetFromEnd - right * k_SignOffsetFromAxis, Quaternion.LookRotation(along));

            // Comentario: qué piezas se cambiaron (un hijo vacío con la descripción en el nombre).
            string info = $"{DEMO_INFO_PREFIX}: {pieces.Count} x Road Lane_01 de ({first.transform.position.x:F0},{first.transform.position.z:F0}) " +
                          $"a ({last.transform.position.x:F0},{last.transform.position.z:F0}), mano única hacia +X";
            Transform infoChild = root.transform.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith(DEMO_INFO_PREFIX));
            if (infoChild == null)
            {
                infoChild = new GameObject(info).transform;
                infoChild.SetParent(root.transform, false);
            }

            infoChild.name = info;

            summary = $"calle de demostración con {pieces.Count} piezas en mano única hacia +X, de ({first.transform.position.x:F0}, {first.transform.position.z:F0}) " +
                      $"a ({last.transform.position.x:F0}, {last.transform.position.z:F0}).";
            return true;
        }

        // Instancia de Road Lane_01 con RoadLaneDirection más cercana (< 0.5 m) a la posición XZ pedida.
        private static RoadLaneDirection FindLanePiece(Scene scene, Vector2 xz)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (RoadLaneDirection candidate in root.GetComponentsInChildren<RoadLaneDirection>(true))
                {
                    if (!LessonBoundarySetup.IsRoadInstanceRoot(candidate.gameObject)) continue;
                    if (PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject).name != "Road Lane_01") continue;

                    Vector3 p = candidate.transform.position;
                    if (new Vector2(p.x - xz.x, p.z - xz.y).sqrMagnitude < 0.25f) return candidate;
                }
            }

            return null;
        }

        private static void PlaceSign(Scene scene, Transform parent, GameObject prefab, string name, Vector3 position, Quaternion rotation)
        {
            Transform sign = parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
            if (sign == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = name;
                instance.transform.SetParent(parent, false);
                sign = instance.transform;
            }

            position.y = k_SidewalkHeight;
            sign.position = position;
            sign.rotation = rotation;
            sign.localScale = Vector3.one;
        }
    }
}
