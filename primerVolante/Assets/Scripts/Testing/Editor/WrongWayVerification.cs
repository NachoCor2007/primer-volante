using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor de la detección de contramano (<c>WRONG_WAY</c>): funciones puras, capa
    /// <c>LaneZone</c>, zonas en los prefabs de calle, detector en los autos, catálogo y escenas. La detección en
    /// movimiento se prueba en Play Mode (ver <c>INFRACCIONES.md</c>).
    /// </summary>
    public static class WrongWayVerification
    {
        private const float k_Tolerance = 0.05f;

        // Resumen de instancias de calle por escena, para repetirlo al final (las suites existentes imprimen mucho).
        private static readonly List<string> s_SceneSummaries = new List<string>();

        /// <summary>Escenas de ciudad: todas heredan las zonas por los prefabs de calle.</summary>
        private static readonly string[] s_CityScenes =
        {
            "Assets/Scenes/car09/CityMerged3.unity",
            "Assets/Scenes/car09/CityWithInfractions.unity",
            "Assets/Scenes/car09/Lesson_01_BasicDriving2.unity",
            // Las lecciones son escenas overlay sin calles: la ciudad compartida (con las zonas) vive en City_Master.
            "Assets/Scenes/City_Master.unity",
            WrongWaySetup.SCENE_PATH,
            GPSNavigationSceneSetup.WRONG_WAY_DEMO_SCENE_PATH,
        };

        // Rangos esperados de cada prefab (espacio local de la pieza), independientes del setup: son los de la tabla
        // del prompt. {xMinNeg, xMaxPos, zMin, zMax}.
        private static readonly Dictionary<string, float[]> s_ExpectedRanges = new Dictionary<string, float[]>
        {
            { "Road Lane_01", new[] { -7.15f, 7.15f, -10f, 10f } },
            { "Road Lane Half", new[] { -7.15f, 7.15f, -5f, 5f } },
            { "Road Lane_02", new[] { -7.15f, 7.15f, -10f, 10f } },
            { "Road Lane_04", new[] { -7.15f, 7.15f, -10f, 7.15f } },
            { "Road Lane Bus Stop", new[] { -14.15f, 7.15f, -20f, 20f } },
            { "Road Lane_03", new[] { -7.15f, 7.15f, -10f, 0f } },
        };

        [MenuItem("Tools/Primer Volante/Run Wrong Way Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE LA DETECCIÓN DE CONTRAMANO (WRONG_WAY)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;
            s_SceneSummaries.Clear();

            VerifyClassify(ref total, ref passed);
            VerifyEpisode(ref total, ref passed);
            VerifyDirectionRules(ref total, ref passed);
            VerifyLayer(ref total, ref passed);
            VerifyPrefabs(ref total, ref passed);
            VerifyCars(ref total, ref passed);
            VerifyCatalog(ref total, ref passed);
            VerifyScenes(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");

            // Informativo: no suma al resultado. Se corre al final porque abre otras escenas.
            string initialScenePath = SceneManager.GetActiveScene().path;
            bool initiallyClean = Enumerable.Range(0, SceneManager.sceneCount).All(i => !SceneManager.GetSceneAt(i).isDirty);
            ReportExistingSuites();
            if (initiallyClean) RestoreScene(initialScenePath);

            foreach (string line in s_SceneSummaries) Debug.Log($"[WrongWayVerification] 🛣️ {line}");
            Debug.Log($"🏁 CONTRAMANO (WRONG_WAY): {passed}/{total} pruebas pasadas con éxito (sin contar las suites existentes).");
        }

        // ---------------------------------------------------------------- WrongWayMath.Classify

        private static void VerifyClassify(ref int total, ref int passed)
        {
            Vector3 allowed = Vector3.forward;

            // Velocidad con un ángulo dado (en grados) respecto del sentido permitido, en el plano XZ.
            Vector3 At(float degrees) => Quaternion.Euler(0f, degrees, 0f) * allowed * 10f;
            WrongWayClassification C(Vector3 v) => WrongWayMath.Classify(v, allowed, 60f, 120f);

            Assert(C(At(0f)) == WrongWayClassification.Correct, "Classify: 0° es Correct", ref total, ref passed);
            Assert(C(At(59f)) == WrongWayClassification.Correct, "Classify: 59° es Correct", ref total, ref passed);
            Assert(C(At(61f)) == WrongWayClassification.Neutral, "Classify: 61° es Neutral", ref total, ref passed);
            Assert(C(At(90f)) == WrongWayClassification.Neutral, "Classify: 90° es Neutral", ref total, ref passed);
            Assert(C(At(119f)) == WrongWayClassification.Neutral, "Classify: 119° es Neutral", ref total, ref passed);
            Assert(C(At(121f)) == WrongWayClassification.Wrong, "Classify: 121° es Wrong", ref total, ref passed);
            Assert(C(At(180f)) == WrongWayClassification.Wrong, "Classify: 180° es Wrong", ref total, ref passed);
            Assert(C(At(-179f)) == WrongWayClassification.Wrong, "Classify: -179° (lado opuesto) es Wrong", ref total, ref passed);
            Assert(C(Vector3.zero) == WrongWayClassification.Neutral, "Classify: velocidad nula es Neutral", ref total, ref passed);
            Assert(C(new Vector3(0f, 25f, 0f)) == WrongWayClassification.Neutral, "Classify: velocidad solo vertical es Neutral", ref total, ref passed);
            Assert(C(new Vector3(0f, 25f, 10f)) == WrongWayClassification.Correct, "Classify: la componente vertical se ignora (a favor)", ref total, ref passed);
            Assert(C(new Vector3(0f, -25f, -10f)) == WrongWayClassification.Wrong, "Classify: la componente vertical se ignora (en contra)", ref total, ref passed);
            Assert(WrongWayMath.Classify(Vector3.forward, Vector3.zero, 60f, 120f) == WrongWayClassification.Neutral,
                "Classify: sentido permitido nulo es Neutral", ref total, ref passed);
        }

        // ---------------------------------------------------------------- WrongWayEpisode

        private static void VerifyEpisode(ref int total, ref int passed)
        {
            const WrongWayClassification wrong = WrongWayClassification.Wrong;
            const WrongWayClassification correct = WrongWayClassification.Correct;
            const WrongWayClassification neutral = WrongWayClassification.Neutral;

            var episode = new WrongWayEpisode(10f, 10f);
            Assert(!episode.Step(wrong, 9.9f), "Episode: 9,9 m en contramano no reporta", ref total, ref passed);
            Assert(episode.Step(wrong, 0.2f), "Episode: 10,1 m en contramano reportan", ref total, ref passed);
            Assert(!episode.Step(wrong, 5f) && !episode.Step(wrong, 50f), "Episode: seguir en contramano no vuelve a reportar", ref total, ref passed);

            episode = new WrongWayEpisode(10f, 10f);
            episode.Step(wrong, 10.1f);
            episode.Step(correct, 9f);
            Assert(!episode.Step(wrong, 1f), "Episode: 9 m a mano no rearma (no vuelve a reportar)", ref total, ref passed);

            episode = new WrongWayEpisode(10f, 10f);
            bool first = episode.Step(wrong, 10.1f);
            episode.Step(correct, 10.1f);
            Assert(first && !episode.Reported && episode.WrongDistanceM == 0f, "Episode: 10,1 m a mano rearman (cierran el episodio)", ref total, ref passed);
            Assert(!episode.Step(wrong, 9.9f) && episode.Step(wrong, 0.2f), "Episode: después de rearmar, otros 10,1 m reportan de nuevo", ref total, ref passed);

            episode = new WrongWayEpisode(10f, 10f);
            episode.Step(wrong, 4f);
            episode.Step(neutral, 100f);
            Assert(episode.WrongDistanceM == 4f && episode.CorrectDistanceM == 0f, "Episode: Neutral no cambia nada", ref total, ref passed);

            // Dos esquivas cortas separadas por menos de la distancia de rearme se suman (intencional).
            episode = new WrongWayEpisode(10f, 10f);
            episode.Step(wrong, 6f);
            episode.Step(correct, 5f);
            Assert(episode.Step(wrong, 4.5f), "Episode: dos esquivas cercanas (6 m + 5 m a mano + 4,5 m) se suman", ref total, ref passed);

            // Una esquiva de 6 m y la vuelta a la derecha de más de 10 m no reportan nunca.
            episode = new WrongWayEpisode(10f, 10f);
            bool reported = episode.Step(wrong, 6f);
            reported |= episode.Step(correct, 12f);
            reported |= episode.Step(wrong, 6f);
            Assert(!reported, "Episode: esquivas de 6 m separadas por más de 10 m a mano no reportan", ref total, ref passed);

            episode = new WrongWayEpisode(10f, 10f);
            episode.Step(wrong, 10.1f);
            episode.Reset();
            Assert(!episode.Reported && episode.WrongDistanceM == 0f && episode.CorrectDistanceM == 0f, "Episode: Reset descarta el episodio", ref total, ref passed);
            Assert(episode.Step(wrong, 10.1f), "Episode: después de Reset se puede reportar de nuevo", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Reglas de RoadLaneDirection

        private static void VerifyDirectionRules(ref int total, ref int passed)
        {
            var go = new GameObject("WrongWayVerification_Direction");
            try
            {
                var direction = go.AddComponent<RoadLaneDirection>();

                direction.Flow = TrafficFlow.TwoWay;
                Assert(direction.AllowedLocalDirection(LaneSide.PositiveX) == Vector3.forward &&
                       direction.AllowedLocalDirection(LaneSide.NegativeX) == Vector3.back,
                    "TwoWay: PositiveX va a +Z y NegativeX a −Z (mano derecha)", ref total, ref passed);

                direction.Flow = TrafficFlow.OneWayPositiveZ;
                Assert(direction.AllowedLocalDirection(LaneSide.PositiveX) == Vector3.forward &&
                       direction.AllowedLocalDirection(LaneSide.NegativeX) == Vector3.forward,
                    "OneWayPositiveZ: los dos carriles van a +Z", ref total, ref passed);

                direction.Flow = TrafficFlow.OneWayNegativeZ;
                Assert(direction.AllowedLocalDirection(LaneSide.PositiveX) == Vector3.back &&
                       direction.AllowedLocalDirection(LaneSide.NegativeX) == Vector3.back,
                    "OneWayNegativeZ: los dos carriles van a −Z", ref total, ref passed);

                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                direction.Flow = TrafficFlow.TwoWay;
                Assert(Vector3.Dot(direction.AllowedWorldDirection(LaneSide.PositiveX), Vector3.right) > 0.999f,
                    "AllowedWorldDirection respeta la rotación de la pieza", ref total, ref passed);

                // Herramienta de la Scene view: la pieza elige el sentido según hacia dónde mira el autor.
                TrafficFlow flow = RoadLaneDirectionTools.FlowFromDirection(Vector3.forward, new Vector3(0.1f, -0.5f, 1f), out bool ambiguous);
                Assert(flow == TrafficFlow.OneWayPositiveZ && !ambiguous, "FlowFromDirection: misma dirección (con pitch) => +Z", ref total, ref passed);
                flow = RoadLaneDirectionTools.FlowFromDirection(Vector3.forward, Vector3.back, out ambiguous);
                Assert(flow == TrafficFlow.OneWayNegativeZ && !ambiguous, "FlowFromDirection: dirección opuesta => −Z", ref total, ref passed);
                RoadLaneDirectionTools.FlowFromDirection(Vector3.forward, new Vector3(1f, 0f, 0.3f), out ambiguous);
                Assert(ambiguous, "FlowFromDirection: casi perpendicular (|dot| < 0,5) es ambiguo", ref total, ref passed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- Capa

        private static void VerifyLayer(ref int total, ref int passed)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            Assert(layer >= 0, $"Existe la capa LaneZone (índice {layer})", ref total, ref passed);
            if (layer < 0) return;

            bool ignoresAll = true;
            for (int other = 0; other < 32; other++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, other)) ignoresAll = false;
            }

            Assert(ignoresAll, "LaneZone no colisiona con ninguna capa (ni consigo misma)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Prefabs de calle

        private static void VerifyPrefabs(ref int total, ref int passed)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);

            foreach (string path in LessonBoundarySetup.FindRoadPrefabPaths())
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                RoadLaneZone[] zones = prefab.GetComponentsInChildren<RoadLaneZone>(true);
                var direction = prefab.GetComponent<RoadLaneDirection>();

                if (!s_ExpectedRanges.TryGetValue(name, out float[] range))
                {
                    Assert(zones.Length == 0 && direction == null && prefab.transform.Find(WrongWaySetup.LANE_ZONES_NAME) == null,
                        $"{name}: pieza excluida, sin zonas ni RoadLaneDirection", ref total, ref passed);
                    continue;
                }

                Assert(direction != null && direction.Flow == TrafficFlow.TwoWay, $"{name}: RoadLaneDirection en la raíz, en TwoWay", ref total, ref passed);
                Assert(zones.Length == 2 && zones.Count(z => z.Side == LaneSide.PositiveX) == 1 && zones.Count(z => z.Side == LaneSide.NegativeX) == 1,
                    $"{name}: exactamente dos RoadLaneZone (una por lado) ({zones.Length})", ref total, ref passed);
                if (zones.Length != 2) continue;

                foreach (RoadLaneZone zone in zones)
                {
                    var box = zone.GetComponent<BoxCollider>();
                    Assert(box != null && box.isTrigger && zone.gameObject.layer == layer,
                        $"{name}/{zone.Side}: BoxCollider trigger en la capa LaneZone", ref total, ref passed);
                    if (box == null) continue;

                    Vector3 min = zone.transform.localPosition + box.center - box.size * 0.5f;
                    Vector3 max = zone.transform.localPosition + box.center + box.size * 0.5f;
                    float xA = zone.Side == LaneSide.PositiveX ? 0f : range[0];
                    float xB = zone.Side == LaneSide.PositiveX ? range[1] : 0f;
                    bool ok = Near(min.x, xA) && Near(max.x, xB) && Near(min.z, range[2]) && Near(max.z, range[3]) &&
                              Near(min.y, 0f) && Near(max.y, WrongWaySetup.ZONE_HEIGHT);
                    Assert(ok, $"{name}/{zone.Side}: rangos X[{min.x:F2},{max.x:F2}] Z[{min.z:F2},{max.z:F2}] Y[{min.y:F2},{max.y:F2}] según la tabla", ref total, ref passed);
                }

                if (name == "Road Lane_03")
                {
                    bool coversCrosswalk = zones.Any(z =>
                    {
                        var b = z.GetComponent<BoxCollider>();
                        return z.transform.localPosition.z + b.center.z + b.size.z * 0.5f > 0.01f;
                    });
                    Assert(!coversCrosswalk, "Road Lane_03: las zonas no cubren z > 0 (la senda peatonal)", ref total, ref passed);
                }
            }
        }

        // ---------------------------------------------------------------- Autos

        private static void VerifyCars(ref int total, ref int passed)
        {
            int layer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            foreach (string path in WrongWaySetup.CarPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var detector = prefab != null ? prefab.GetComponent<WrongWayDetector>() : null;
                string car = System.IO.Path.GetFileNameWithoutExtension(path);
                Assert(detector != null, $"{car}: tiene WrongWayDetector en la raíz", ref total, ref passed);
                if (detector == null) continue;

                Assert(layer >= 0 && detector.ZoneMask.value == 1 << layer, $"{car}: m_ZoneMask es solo la capa LaneZone", ref total, ref passed);
                Assert(prefab.GetComponent<PrimerVolante.VR.VehicleController>() != null && prefab.GetComponent<Rigidbody>() != null,
                    $"{car}: el detector está junto al VehicleController y el Rigidbody", ref total, ref passed);
            }

            string[] npcPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/NPCs" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            int withDetector = npcPaths.Count(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponentInChildren<WrongWayDetector>(true) != null);
            Assert(npcPaths.Length > 0 && withDetector == 0, $"Los prefabs de NPC no tienen WrongWayDetector ({npcPaths.Length} revisados)", ref total, ref passed);
        }

        // ---------------------------------------------------------------- Catálogo

        private static void VerifyCatalog(ref int total, ref int passed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
            bool found = catalog != null && catalog.TryGet(InfractionIds.WrongWay, out _);
            Assert(found, "El catálogo tiene la definición WRONG_WAY", ref total, ref passed);
            if (!found) return;

            catalog.TryGet(InfractionIds.WrongWay, out InfractionDefinitionSO definition);
            Assert(definition.Severity == InfractionSeverity.Major, "WRONG_WAY es grave (Major)", ref total, ref passed);

            var placeholders = new HashSet<string>(
                Regex.Matches(definition.DescriptionTemplate ?? string.Empty, @"\{([^{}]+)\}").Cast<Match>().Select(m => m.Groups[1].Value));
            Assert(placeholders.SetEquals(new[] { "where" }), $"La plantilla de WRONG_WAY usa solo 'where' ({string.Join(", ", placeholders)})", ref total, ref passed);

            foreach (string where in new[] { WrongWayMath.WhereTwoWay, WrongWayMath.WhereOneWay })
            {
                string text = InfractionTextFormatter.Format(definition.DescriptionTemplate, new Dictionary<string, string> { { "where", where } });
                Assert(!text.Contains("{") && text.Contains(where), $"La descripción formatea 'where' = \"{where}\"", ref total, ref passed);
            }
        }

        // ---------------------------------------------------------------- Escenas

        private static void VerifyScenes(ref int total, ref int passed)
        {
            var checks = new List<(bool ok, string name)>();

            foreach (string path in s_CityScenes)
            {
                bool exists = System.IO.File.Exists(path);
                checks.Add((exists, $"Existe {System.IO.Path.GetFileName(path)}"));
                if (!exists) continue;

                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                WithScene(path, scene => VerifySceneRoads(scene, sceneName, checks));
            }

            // CityWithWrongWay: gestor y calle de demostración.
            if (System.IO.File.Exists(WrongWaySetup.SCENE_PATH))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<InfractionCatalogSO>(InfractionManagerSetup.CATALOG_PATH);
                WithScene(WrongWaySetup.SCENE_PATH, scene =>
                {
                    InfractionManager[] managers = AllInScene<InfractionManager>(scene);
                    checks.Add((managers.Length == 1, $"CityWithWrongWay tiene exactamente un InfractionManager ({managers.Length})"));
                    checks.Add((managers.Length == 1 && managers[0].Catalog != null && managers[0].Catalog == catalog,
                        "El InfractionManager de CityWithWrongWay tiene el catálogo asignado"));

                    RoadLaneDirection[] oneWay = AllInScene<RoadLaneDirection>(scene).Where(d => d.IsOneWay).ToArray();
                    checks.Add((oneWay.Length == WrongWaySetup.DemoPiecePositions.Length,
                        $"CityWithWrongWay tiene exactamente {WrongWaySetup.DemoPiecePositions.Length} piezas en mano única ({oneWay.Length})"));

                    bool demoPieces = oneWay.Length > 0 && oneWay.All(d =>
                    {
                        Vector3 p = d.transform.position;
                        bool atDemo = WrongWaySetup.DemoPiecePositions.Any(v => Mathf.Abs(v.x - p.x) < 0.5f && Mathf.Abs(v.y - p.z) < 0.5f);
                        bool direction = Vector3.Dot(d.AllowedWorldDirection(LaneSide.PositiveX), WrongWaySetup.DemoTravelDirection) > 0.99f &&
                                         Vector3.Dot(d.AllowedWorldDirection(LaneSide.NegativeX), WrongWaySetup.DemoTravelDirection) > 0.99f;
                        return atDemo && direction;
                    });
                    checks.Add((demoPieces, "Las piezas en mano única son las de la calle de demostración y los dos carriles van hacia +X"));

                    GameObject root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == WrongWaySetup.DEMO_ROOT_NAME);
                    checks.Add((root != null, "CityWithWrongWay tiene el grupo OneWayDemo"));
                    if (root != null)
                    {
                        string Source(GameObject go) => AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go));
                        var signs = root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject)
                            .Where(PrefabUtility.IsAnyPrefabInstanceRoot).ToArray();
                        checks.Add((signs.Count(s => Source(s) == WrongWaySetup.PROHIBITED_PREFAB_PATH) == 1 &&
                                    signs.Count(s => Source(s) == WrongWaySetup.DIRECTION_PREFAB_PATH) == 1,
                            "OneWayDemo tiene un cartel de contramano y uno de sentido de circulación"));
                        checks.Add((signs.All(s => s.GetComponentsInChildren<Collider>(true).All(c => !c.isTrigger)),
                            "Los carteles no tienen colliders trigger (son solo visuales)"));
                        checks.Add((root.GetComponentsInChildren<WrongWayExemptZone>(true).Length == 0 &&
                                    AllInScene<WrongWayExemptZone>(scene).Length == 0,
                            "CityWithWrongWay no tiene WrongWayExemptZone guardadas"));
                    }
                });
            }

            // CityMerged3 y CityWithInfractions: sin mano única.
            foreach (string path in new[] { s_CityScenes[0], s_CityScenes[1] })
            {
                if (!System.IO.File.Exists(path)) continue;
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                WithScene(path, scene =>
                {
                    int oneWay = AllInScene<RoadLaneDirection>(scene).Count(d => d.IsOneWay);
                    bool hasDemo = scene.GetRootGameObjects().Any(go => go.name == WrongWaySetup.DEMO_ROOT_NAME);
                    checks.Add((oneWay == 0 && !hasDemo, $"{sceneName} no tiene piezas en mano única ni OneWayDemo ({oneWay})"));
                });
            }

            foreach ((bool ok, string name) in checks) Assert(ok, name, ref total, ref passed);
        }

        // Cada instancia de las piezas con zonas las tiene (sin overrides que las eliminen) y no hay piezas desempaquetadas.
        private static void VerifySceneRoads(Scene scene, string sceneName, List<(bool ok, string name)> checks)
        {
            var counts = new Dictionary<string, int>();
            int instances = 0, withZones = 0, unpacked = 0, overridesRemoved = 0, wrongLayer = 0;
            int laneZoneLayer = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            var odd = new List<string>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;

                    if (!PrefabUtility.IsPartOfAnyPrefab(go) && WrongWaySetup.ZonePrefabNames.Any(n => go.name.StartsWith(n)) &&
                        go.GetComponent<MeshRenderer>() != null)
                    {
                        unpacked++;
                        odd.Add($"{go.name} desempaquetada en {Path(t)}");
                        continue;
                    }

                    if (!LessonBoundarySetup.IsRoadInstanceRoot(go)) continue;
                    string prefabName = PrefabUtility.GetCorrespondingObjectFromSource(go).name;
                    if (!WrongWaySetup.HasLaneZones(prefabName)) continue;

                    instances++;
                    counts[prefabName] = counts.TryGetValue(prefabName, out int c) ? c + 1 : 1;

                    RoadLaneZone[] zones = go.GetComponentsInChildren<RoadLaneZone>(false)
                        .Where(z => z.GetComponent<BoxCollider>() != null && z.GetComponent<BoxCollider>().enabled).ToArray();
                    bool ok = go.GetComponent<RoadLaneDirection>() != null && zones.Length == 2 &&
                              zones.Select(z => z.Side).Distinct().Count() == 2;
                    if (ok) withZones++;
                    else odd.Add($"{go.name} sin zonas completas en {Path(t)}");

                    // Otras herramientas cambian capas en forma recursiva (p. ej. Roads del GPS): las zonas deben seguir en LaneZone.
                    if (zones.Any(z => z.gameObject.layer != laneZoneLayer))
                    {
                        wrongLayer++;
                        odd.Add($"{go.name} con zonas fuera de la capa {RoadLaneZone.LayerName} en {Path(t)}");
                    }

                    int removed = PrefabUtility.GetRemovedGameObjects(go).Count + PrefabUtility.GetRemovedComponents(go).Count;
                    if (removed > 0)
                    {
                        overridesRemoved++;
                        odd.Add($"{go.name} con {removed} overrides que quitan objetos/componentes en {Path(t)}");
                    }
                }
            }

            string summary = string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} x{kv.Value}"));
            string sceneLine = $"{sceneName}: {instances} instancias con zonas ({summary}); raras: {(odd.Count == 0 ? "ninguna" : string.Join("; ", odd))}";
            s_SceneSummaries.Add(sceneLine);
            Debug.Log($"[WrongWayVerification] ℹ️ {sceneLine}");

            checks.Add((instances > 0 && withZones == instances, $"{sceneName}: cada instancia de pieza con zonas las tiene ({withZones}/{instances})"));
            checks.Add((unpacked == 0, $"{sceneName}: no hay piezas desempaquetadas ({unpacked})"));
            checks.Add((overridesRemoved == 0, $"{sceneName}: ninguna instancia tiene overrides que eliminen las zonas ({overridesRemoved})"));
            checks.Add((wrongLayer == 0, $"{sceneName}: todas las zonas están en la capa {RoadLaneZone.LayerName} ({wrongLayer} instancias fuera)"));
        }

        // ---------------------------------------------------------------- Suites existentes

        // Informativo: lee la línea "N/M pruebas pasadas" de la verificación del gestor (que a su vez corre las demás).
        private static void ReportExistingSuites()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Debug.LogWarning($"[WrongWayVerification] ⚠️ {open.name} tiene cambios sin guardar: no se corren las suites existentes.");
                    return;
                }
            }

            RunSuite("Tools/Primer Volante/Run Infraction Manager Verification", "InfractionManagerVerification");
        }

        private static void RunSuite(string menuPath, string name)
        {
            int suiteTotal = 0, suitePassed = 0;
            void OnLog(string message, string stack, LogType type)
            {
                Match match = Regex.Match(message, @"(\d+)/(\d+) pruebas pasadas");
                if (match.Success)
                {
                    suitePassed = int.Parse(match.Groups[1].Value);
                    suiteTotal = int.Parse(match.Groups[2].Value);
                }
            }

            bool menuFound;
            Application.logMessageReceived += OnLog;
            try
            {
                menuFound = EditorApplication.ExecuteMenuItem(menuPath);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            if (!menuFound) Debug.LogWarning($"[WrongWayVerification] ⚠️ No existe el MenuItem '{menuPath}'.");
            else if (suiteTotal > 0 && suitePassed == suiteTotal) Debug.Log($"[WrongWayVerification] ℹ️ Suite existente {name}: {suitePassed}/{suiteTotal} (pasa).");
            else Debug.LogWarning($"[WrongWayVerification] ⚠️ Suite existente {name}: {suitePassed}/{suiteTotal}. Revisá los fallos conocidos en INFRACCIONES.md.");
        }

        // ---------------------------------------------------------------- Helpers

        // Las suites existentes dejan abierta otra escena (con cambios por reserialización): se vuelve a la inicial y
        // se descartan esos cambios. Solo se llama si todo estaba limpio al empezar.
        private static void RestoreScene(string path)
        {
            if (string.IsNullOrEmpty(path) || SceneManager.GetActiveScene().path == path) return;

            Scene restored = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(restored);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene other = SceneManager.GetSceneAt(i);
                if (other != restored) EditorSceneManager.CloseScene(other, true);
            }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= k_Tolerance;

        private static T[] AllInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static string Path(Transform t)
        {
            var names = new List<string>();
            for (Transform current = t; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // Ejecuta el chequeo con la escena cargada: si no estaba abierta, la abre de forma aditiva y la cierra al final.
        private static void WithScene(string path, Action<Scene> check)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                check(scene);
            }
            finally
            {
                if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {testName}");
            }
        }
    }
}
