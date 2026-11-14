using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;
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
        public const string FINISH_ZONE_NAME = "ParkingZone_Finish";
        public const string YELLOW_MATERIAL_PATH = "Assets/Environment/Materials/M_Parking_YellowZone.mat";
        public const string AURA_MATERIAL_PATH = "Assets/Environment/Materials/M_Parking_AuraPerimeter.mat";
        public const string ARROW_MATERIAL_PATH = "Assets/Environment/Materials/M_Parking_ArrowBlack.mat";
        public const string CONE_PREFAB_PATH = "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Traffic cone.prefab";

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

            // Crear o actualizar el sector de estacionamiento amarillo al final del recorrido
            CreateOrUpdateFinishParkingZone(scene, container);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = routeObj;
            Debug.Log($"[Circuit01RouteSetup] 🏁 Circuito rectificado con éxito: {knots.Count} nudos ({container.CalculateLength(0):F1} m). Calles 100% rectas y curvas limpias en cruces.");
        }

        [MenuItem("Tools/Primer Volante/Circuit 01/Add or Update Finish Parking Zone")]
        public static GameObject CreateOrUpdateFinishParkingZoneMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject routeObj = GameObject.Find(ROUTE_NAME);
            SplineContainer container = routeObj != null ? routeObj.GetComponent<SplineContainer>() : null;
            GameObject zoneObj = CreateOrUpdateFinishParkingZone(scene, container);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return zoneObj;
        }

        public static GameObject CreateOrUpdateFinishParkingZone(Scene scene, SplineContainer container = null)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = SceneManager.GetActiveScene();
            }

            Vector3 finishPos = new Vector3(123.50f, 0.0f, 200.00f);
            Quaternion finishRot = Quaternion.identity;

            if (container == null)
            {
                GameObject routeObj = GameObject.Find(ROUTE_NAME);
                if (routeObj != null) container = routeObj.GetComponent<SplineContainer>();
            }

            if (container != null && container.Spline != null && container.Spline.Count >= 2)
            {
                var spline = container.Spline;
                int lastIdx = spline.Count - 1;
                Vector3 pEnd = (Vector3)spline[lastIdx].Position;
                Vector3 pPrev = (Vector3)spline[lastIdx - 1].Position;

                finishPos = new Vector3(pEnd.x, 0.0f, pEnd.z);
                Vector3 fwd = pEnd - pPrev;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.001f)
                {
                    finishRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                }
            }

            GameObject zoneObj = GameObject.Find(FINISH_ZONE_NAME);
            if (zoneObj == null)
            {
                zoneObj = new GameObject(FINISH_ZONE_NAME);
                Undo.RegisterCreatedObjectUndo(zoneObj, "Create Finish Parking Zone");
                SceneManager.MoveGameObjectToScene(zoneObj, scene);
            }

            zoneObj.transform.position = finishPos;
            zoneObj.transform.rotation = finishRot;
            zoneObj.transform.localScale = Vector3.one;

            // Trigger volumétrico para validación de estacionamiento
            BoxCollider col = zoneObj.GetComponent<BoxCollider>();
            if (col == null) col = zoneObj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 1.25f, 0f);
            col.size = new Vector3(3.0f, 2.5f, 6.0f);

            ParkingZoneTrigger trigger = zoneObj.GetComponent<ParkingZoneTrigger>();
            if (trigger == null) trigger = zoneObj.AddComponent<ParkingZoneTrigger>();
            trigger.StationType = ParkingStationType.ParallelRight;
            trigger.MarginTolerance = 0.35f;

            // Visual: recuadro amarillo demarcado sobre el asfalto con iluminación suave
            Material yellowMat = AssetDatabase.LoadAssetAtPath<Material>(YELLOW_MATERIAL_PATH);
            EnsureGroundQuad(zoneObj.transform, "GroundVisual_YellowZone", new Vector2(3.0f, 6.0f), yellowMat);

            // Visual: flecha negra direccional sobre el recuadro
            EnsureDirectionArrowOnGround(zoneObj.transform, "GroundVisual_Arrow");

            // Visual: aura holográfica perimetral 3D pulsante
            Material auraMat = AssetDatabase.LoadAssetAtPath<Material>(AURA_MATERIAL_PATH);
            EnsureAuraPerimeter(zoneObj.transform, "GroundVisual_Aura", new Vector2(3.0f, 6.0f), auraMat);

            // Obstáculos perimetrales: conos de tránsito en las 4 esquinas
            GameObject conePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONE_PREFAB_PATH);
            SpawnCones(zoneObj.transform, conePrefab, new Vector2(3.0f, 6.0f));

            EditorUtility.SetDirty(zoneObj);
            Selection.activeGameObject = zoneObj;
            Debug.Log($"[Circuit01RouteSetup] 🟨 Sector de estacionamiento amarillo configurado en {finishPos} con orientación {finishRot.eulerAngles}.");
            return zoneObj;
        }

        private static GameObject EnsureGroundQuad(Transform parent, string name, Vector2 size, Material mat)
        {
            Transform existing = parent.Find(name);
            GameObject quad;
            if (existing != null)
            {
                quad = existing.gameObject;
            }
            else
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                quad.transform.SetParent(parent, false);

                Collider col = quad.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }

            quad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
            }

            EnsureGroundZoneLight(quad.transform);
            EditorUtility.SetDirty(quad);
            return quad;
        }

        private static void EnsureGroundZoneLight(Transform quadTransform)
        {
            Transform lightTr = quadTransform.Find("GroundVisual_Light");
            GameObject lightObj;
            if (lightTr == null)
            {
                lightObj = new GameObject("GroundVisual_Light");
                lightObj.transform.SetParent(quadTransform, false);
            }
            else
            {
                lightObj = lightTr.gameObject;
            }

            lightObj.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            lightObj.transform.localRotation = Quaternion.identity;

            Light l = lightObj.GetComponent<Light>();
            if (l == null) l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 7.0f;
            l.color = new Color(1.0f, 0.85f, 0.2f);
            l.intensity = 2.0f;
            l.shadows = LightShadows.None;
        }

        private static GameObject EnsureAuraPerimeter(Transform parent, string name, Vector2 size, Material mat)
        {
            Transform existing = parent.Find(name);
            GameObject auraObj;
            if (existing != null)
            {
                auraObj = existing.gameObject;
            }
            else
            {
                auraObj = new GameObject(name);
                auraObj.transform.SetParent(parent, false);
            }

            auraObj.transform.localPosition = Vector3.zero;
            auraObj.transform.localRotation = Quaternion.identity;
            auraObj.transform.localScale = Vector3.one;

            ParkingAuraPerimeter aura = auraObj.GetComponent<ParkingAuraPerimeter>();
            if (aura == null) aura = auraObj.AddComponent<ParkingAuraPerimeter>();
            aura.Size = size;
            aura.Height = 0.60f;
            aura.AuraMaterial = mat;
            aura.BuildAuraMesh();

            EditorUtility.SetDirty(auraObj);
            return auraObj;
        }

        private static GameObject EnsureDirectionArrowOnGround(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            GameObject arrowObj;
            if (existing != null)
            {
                arrowObj = existing.gameObject;
            }
            else
            {
                arrowObj = new GameObject(name);
                arrowObj.transform.SetParent(parent, false);
            }

            arrowObj.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            arrowObj.transform.localRotation = Quaternion.identity;
            arrowObj.transform.localScale = Vector3.one;

            MeshFilter mf = arrowObj.GetComponent<MeshFilter>();
            if (mf == null) mf = arrowObj.AddComponent<MeshFilter>();

            MeshRenderer mr = arrowObj.GetComponent<MeshRenderer>();
            if (mr == null) mr = arrowObj.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh();
            mesh.name = "M_ParkingArrowMesh_Circuit";

            float shaftW = 0.38f;
            float shaftL = 1.4f;
            float headW = 1.0f;
            float headL = 0.9f;
            float totalL = shaftL + headL;
            float zStart = -totalL * 0.5f;
            float zJoint = zStart + shaftL;
            float zTip = zStart + totalL;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-shaftW * 0.5f, 0f, zStart),
                new Vector3(shaftW * 0.5f, 0f, zStart),
                new Vector3(-shaftW * 0.5f, 0f, zJoint),
                new Vector3(shaftW * 0.5f, 0f, zJoint),
                new Vector3(-headW * 0.5f, 0f, zJoint),
                new Vector3(headW * 0.5f, 0f, zJoint),
                new Vector3(0f, 0f, zTip)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,
                1, 2, 3,
                4, 6, 2,
                2, 6, 3,
                3, 6, 5
            };

            Vector2[] uvs = new Vector2[7];
            for (int i = 0; i < 7; i++)
            {
                uvs[i] = new Vector2(vertices[i].x, vertices[i].z);
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;

            Material arrowMat = AssetDatabase.LoadAssetAtPath<Material>(ARROW_MATERIAL_PATH);
            if (arrowMat != null)
            {
                mr.sharedMaterial = arrowMat;
            }

            EditorUtility.SetDirty(arrowObj);
            return arrowObj;
        }

        private static void SpawnCones(Transform parent, GameObject conePrefab, Vector2 size)
        {
            if (conePrefab == null) return;
            Vector3[] offsets = new Vector3[]
            {
                new Vector3(-size.x * 0.5f, 0f, -size.y * 0.5f),
                new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f),
                new Vector3(-size.x * 0.5f, 0f, size.y * 0.5f),
                new Vector3(size.x * 0.5f, 0f, size.y * 0.5f)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                string cName = $"Cone_{i}";
                Transform existing = parent.Find(cName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                GameObject cone = (GameObject)PrefabUtility.InstantiatePrefab(conePrefab, parent);
                cone.name = cName;
                cone.transform.localPosition = offsets[i];
                cone.transform.localRotation = Quaternion.identity;

                ObstacleCollisionDetector detector = cone.GetComponent<ObstacleCollisionDetector>();
                if (detector == null) detector = cone.AddComponent<ObstacleCollisionDetector>();
            }
        }
    }
}
