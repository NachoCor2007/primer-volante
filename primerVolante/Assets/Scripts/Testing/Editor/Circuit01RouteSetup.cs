using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using PrimerVolante.UI;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Observador de Editor que regenera automáticamente el LineRenderer azul del GPS
    /// en tiempo real cada vez que se agrega, mueve o edita un nudo de la Spline en el Editor,
    /// sin necesidad de entrar a Play Mode.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [InitializeOnLoad]
    public static class SplineRouteEditorAutoUpdater
    {
        static SplineRouteEditorAutoUpdater()
        {
            Spline.Changed -= OnSplineChanged;
            Spline.Changed += OnSplineChanged;
        }

        private static void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
        {
            if (Application.isPlaying) return;

            GPSRouteRenderer routeRenderer = UnityEngine.Object.FindAnyObjectByType<GPSRouteRenderer>();
            if (routeRenderer != null && routeRenderer.RouteContainer != null && routeRenderer.RouteContainer.Spline == spline)
            {
                routeRenderer.InitializeRoute();
            }
        }
    }

    /// <summary>
    /// Configura y rectifica el circuito de navegación de la escena Circuit_01:
    /// - Corrige tramos de calles para que sean 100% rectos sobre sus carriles reglamentarios.
    /// - Genera arcos Bézier suaves y redondeados en las esquinas e intersecciones.
    /// - Respeta mano derecha, altura de asfalto y conecta Car09 al GPS.
    /// </summary>
    public static class Circuit01RouteSetup
    {
        public const string ROUTE_NAME = "GPS_Circuit_Route";

        [MenuItem("Tools/Primer Volante/GPS/Rectify Circuit 01 Route")]
        public static void RectifyCircuit01Route()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.path.Contains("Circuit_01"))
            {
                Debug.LogWarning("[Circuit01RouteSetup] Nota: La escena activa no es Circuit_01.");
            }

            GameObject routeObj = GameObject.Find(ROUTE_NAME);
            if (routeObj == null)
            {
                routeObj = new GameObject(ROUTE_NAME);
                Undo.RegisterCreatedObjectUndo(routeObj, "Create GPS Route");
                SceneManager.MoveGameObjectToScene(routeObj, scene);
            }

            routeObj.transform.position = Vector3.zero;
            routeObj.transform.rotation = Quaternion.identity;
            routeObj.transform.localScale = Vector3.one;

            SplineContainer container = routeObj.GetComponent<SplineContainer>();
            if (container == null) container = routeObj.AddComponent<SplineContainer>();

            Spline spline = new Spline();
            spline.Closed = false;

            const float Y = 0.02f;
            const float R = 6.0f;
            const float K = 0.55228475f * R; // ~3.3137m

            var knots = new List<BezierKnot>
            {
                // --- TRAMO 1: SALIDA DEL ESTACIONAMIENTO ---
                // 0: En el auto
                new BezierKnot(new float3(208.19f, Y, 78.22f), float3.zero, new float3(1.06f, 0f, 1.70f), quaternion.identity),
                // 1: Saliendo del cajón al playón
                new BezierKnot(new float3(211.38f, Y, 83.30f), new float3(-1.06f, 0f, -1.70f), new float3(1.06f, 0f, 1.70f), quaternion.identity),
                // 2: Curva suave derecha en el playón
                new BezierKnot(new float3(216.50f, Y, 88.50f), new float3(-2.5f, 0f, -1.8f), new float3(3.5f, 0f, 0f), quaternion.identity),
                // 3: Recto por el playón
                new BezierKnot(new float3(226.50f, Y, 88.50f), new float3(-3.5f, 0f, 0f), new float3(3.5f, 0f, 0f), quaternion.identity),
                // 4: Boca de salida
                new BezierKnot(new float3(233.00f, Y, 88.50f), new float3(-3.0f, 0f, 0f), new float3(2.5f, 0f, 0f), quaternion.identity),
                // 5: Incorporación a la avenida X = 240 hacia el Sur (-Z), carril X = 236.5
                new BezierKnot(new float3(236.50f, Y, 78.00f), new float3(0f, 0f, 3.5f), new float3(0f, 0f, -5.0f), quaternion.identity),

                // --- TRAMO 2: AVENIDA X = 240 HACIA EL SUR (-Z) ---
                // 6: Fin de recta en avenida X = 240 (antes de esquina Z = 0)
                new BezierKnot(new float3(236.50f, Y, 6.00f), new float3(0f, 0f, 5.0f), new float3(0f, 0f, -K), quaternion.identity),

                // --- CURVA 1: ESQUINA (240, 0) - GIRO A LA DERECHA (HACIA EL OESTE -X) ---
                // 7: Inicio de recta en calle Z = 0, carril Z = 3.5
                new BezierKnot(new float3(234.00f, Y, 3.50f), new float3(K, 0f, 0f), new float3(-5.0f, 0f, 0f), quaternion.identity),

                // --- TRAMO 3: CALLE Z = 0 HACIA EL OESTE (-X) ---
                // 8: Fin de recta en calle Z = 0 (antes de esquina X = 120)
                new BezierKnot(new float3(126.00f, Y, 3.50f), new float3(5.0f, 0f, 0f), new float3(-K, 0f, 0f), quaternion.identity),

                // --- CURVA 2: ESQUINA (120, 0) - GIRO A LA DERECHA (HACIA EL NORTE +Z) ---
                // 9: Inicio de recta en calle X = 120, carril X = 123.5
                new BezierKnot(new float3(123.50f, Y, 6.00f), new float3(0f, 0f, -K), new float3(0f, 0f, 5.0f), quaternion.identity),

                // --- TRAMO 4: CALLE X = 120 HACIA EL NORTE (+Z) ---
                // 10: Fin de recta en calle X = 120 (antes del cruce Z = 60)
                new BezierKnot(new float3(123.50f, Y, 52.00f), new float3(0f, 0f, -5.0f), new float3(0f, 0f, 4.0f), quaternion.identity),

                // --- CURVA 3: CRUCE (120, 60) - GIRO A LA IZQUIERDA (HACIA EL OESTE -X) ---
                // 11: Centro del cruce para giro amplio a la izquierda
                new BezierKnot(new float3(121.00f, Y, 61.00f), new float3(2.5f, 0f, -2.5f), new float3(-3.5f, 0f, 2.0f), quaternion.identity),
                // 12: Inicio de recta en calle Z = 60, carril Z = 63.5
                new BezierKnot(new float3(112.00f, Y, 63.50f), new float3(4.0f, 0f, 0f), new float3(-5.0f, 0f, 0f), quaternion.identity),

                // --- TRAMO 5: CALLE Z = 60 HACIA EL OESTE (-X) ---
                // 13: Fin de recta en calle Z = 60 (antes de esquina X = 0)
                new BezierKnot(new float3(6.00f, Y, 63.50f), new float3(5.0f, 0f, 0f), new float3(-K, 0f, 0f), quaternion.identity),

                // --- CURVA 4: ESQUINA (0, 60) - GIRO A LA DERECHA (HACIA EL NORTE +Z) ---
                // 14: Inicio de recta en avenida X = 0, carril X = 3.5
                new BezierKnot(new float3(3.50f, Y, 66.00f), new float3(0f, 0f, -K), new float3(0f, 0f, 5.0f), quaternion.identity),

                // --- TRAMO 6: AVENIDA X = 0 HACIA EL NORTE (+Z) ---
                // 15: Fin de recta en avenida X = 0 (antes de esquina Z = 160)
                new BezierKnot(new float3(3.50f, Y, 154.00f), new float3(0f, 0f, -5.0f), new float3(0f, 0f, K), quaternion.identity),

                // --- CURVA 5: ESQUINA (0, 160) - GIRO A LA DERECHA (HACIA EL ESTE +X) ---
                // 16: Inicio de recta en calle Z = 160, carril Z = 156.5
                new BezierKnot(new float3(6.00f, Y, 156.50f), new float3(-K, 0f, 0f), new float3(5.0f, 0f, 0f), quaternion.identity),

                // --- TRAMO 7: CALLE Z = 160 HACIA EL ESTE (+X) ---
                // 17: Fin de recta en calle Z = 160 (antes de intersección X = 120)
                new BezierKnot(new float3(112.00f, Y, 156.50f), new float3(-5.0f, 0f, 0f), new float3(4.0f, 0f, 0f), quaternion.identity),

                // --- CURVA 6: CRUCE (120, 160) - GIRO A LA IZQUIERDA (HACIA EL NORTE +Z) ---
                // 18: Centro del cruce para giro amplio a la izquierda
                new BezierKnot(new float3(121.00f, Y, 159.00f), new float3(-2.5f, 0f, -2.5f), new float3(2.0f, 0f, 3.5f), quaternion.identity),
                // 19: Inicio de recta en calle X = 120, carril X = 123.5
                new BezierKnot(new float3(123.50f, Y, 168.00f), new float3(0f, 0f, -4.0f), new float3(0f, 0f, 5.0f), quaternion.identity),

                // --- TRAMO 8: CALLE X = 120 HACIA EL NORTE (+Z) HASTA LA META ---
                // 20: Meta / Llegada
                new BezierKnot(new float3(123.50f, Y, 200.00f), new float3(0f, 0f, -4.0f), float3.zero, quaternion.identity)
            };

            foreach (var knot in knots)
            {
                spline.Add(knot, TangentMode.Broken);
            }

            container.Spline = spline;
            EditorUtility.SetDirty(container);

            GameObject carObj = GameObject.Find("Car09");
            if (carObj != null)
            {
                GPSRouteRenderer routeRenderer = carObj.GetComponent<GPSRouteRenderer>() ?? carObj.GetComponentInChildren<GPSRouteRenderer>(true);
                if (routeRenderer != null)
                {
                    routeRenderer.RouteContainer = container;
                    routeRenderer.InitializeRoute();
                    EditorUtility.SetDirty(routeRenderer);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = routeObj;
            Debug.Log($"[Circuit01RouteSetup] 🏁 Circuito rectificado con éxito: {knots.Count} nudos ({container.CalculateLength(0):F1} m). Calles 100% rectas y curvas limpias en cruces.");
        }
    }
}
