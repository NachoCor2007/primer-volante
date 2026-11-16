using System.Collections.Generic;
using System.Reflection;
using PrimerVolante.Environment;
using PrimerVolante.Lessons;
using PrimerVolante.VR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la pista de slalom de la Lección 7 sobre la escena guardada: conos, vallas
    /// transversales alternadas, corredor, recuadros amarillos, exenciones de contramano / giro, referencias del
    /// controlador, finalización geométrica de los recuadros, textos de los pasos y el arreglo de
    /// <c>InteractableHighlighter.FindDeep</c>.
    /// </summary>
    public static class Lesson07SlalomVerification
    {
        private const float k_Tol = 0.05f;

        [MenuItem("Tools/Primer Volante/Verify Lesson 7 Slalom")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DEL SLALOM DE LA LECCION 7");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            Scene scene = EditorSceneManager.OpenScene(Lesson07BarrierManeuversSetup.TARGET_SCENE_PATH, OpenSceneMode.Single);
            Assert(scene.IsValid() && scene.isLoaded, "La escena de la Lección 7 abre", ref total, ref passed);

            if (scene.IsValid() && scene.isLoaded)
            {
                VerifyCones(ref total, ref passed);
                VerifyTransverseBarriers(ref total, ref passed);
                VerifyCorridor(ref total, ref passed);
                VerifyZones(ref total, ref passed);
                VerifyExemptZones(ref total, ref passed);
                VerifyController(ref total, ref passed);
                VerifyGeometricCompletion(ref total, ref passed);
                VerifyOverlapMath(ref total, ref passed);
            }

            VerifySteps(ref total, ref passed);
            VerifyFindDeepLogsNothing(ref total, ref passed);

            if (scene.IsValid())
            {
                VerifyNoAutoCreate(ref total, ref passed);
            }

            Debug.Log("=========================================================");
            Debug.Log($"LECCION 7 SLALOM: {passed}/{total} pruebas pasadas con exito!");
            Debug.Log("=========================================================");
        }

        private static Transform Course => GameObject.Find(Lesson07BarrierManeuversSetup.SLALOM_ROOT_NAME)?.transform;

        // --- Conos ---------------------------------------------------------------------------------------------

        private static void VerifyCones(ref int total, ref int passed)
        {
            Transform course = Course;
            Assert(course != null, "Existe el contenedor Slalom_Course", ref total, ref passed);
            if (course == null) return;

            Assert(GameObject.Find(Lesson07BarrierManeuversSetup.LEGACY_SLALOM_ROOT_NAME) == null,
                "Ya no existe el contenedor viejo Slalom_Barriers", ref total, ref passed);

            float[] zs = Lesson07BarrierManeuversSetup.ConeZs;
            Assert(zs.Length == 6, "Hay 6 conos previstos", ref total, ref passed);

            for (int i = 0; i < zs.Length; i++)
            {
                Transform cone = course.Find($"Cone_{i + 1:00}");
                Assert(cone != null, $"Existe Cone_{i + 1:00}", ref total, ref passed);
                if (cone == null) continue;

                Assert(Mathf.Abs(cone.position.x - 60f) <= k_Tol && Mathf.Abs(cone.position.z - zs[i]) <= k_Tol,
                    $"Cone_{i + 1:00} en X=60, Z={zs[i]} (está en {cone.position})", ref total, ref passed);
                Assert(cone.GetComponentInChildren<ObstacleCollisionDetector>(true) != null,
                    $"Cone_{i + 1:00} tiene ObstacleCollisionDetector", ref total, ref passed);
            }

            for (int i = 1; i < zs.Length; i++)
            {
                Assert(Mathf.Abs(zs[i] - zs[i - 1] - 10f) <= k_Tol, $"Cono {i + 1} a 10 m del anterior", ref total, ref passed);
            }
        }

        // --- Vallas transversales ---------------------------------------------------------------------------------

        private static void VerifyTransverseBarriers(ref int total, ref int passed)
        {
            Transform course = Course;
            if (course == null) return;

            // Ancho libre del corredor: caras interiores de las paredes.
            float leftInner = float.NegativeInfinity, rightInner = float.PositiveInfinity;
            Transform corridor = course.Find(Lesson07BarrierManeuversSetup.CORRIDOR_NAME);
            if (corridor != null)
            {
                foreach (BoxCollider b in corridor.Find("Wall_Left")?.GetComponentsInChildren<BoxCollider>() ?? new BoxCollider[0])
                {
                    if (!b.isTrigger) leftInner = Mathf.Max(leftInner, b.bounds.max.x);
                }
                foreach (BoxCollider b in corridor.Find("Wall_Right")?.GetComponentsInChildren<BoxCollider>() ?? new BoxCollider[0])
                {
                    if (!b.isTrigger) rightInner = Mathf.Min(rightInner, b.bounds.min.x);
                }
            }

            for (int i = 0; i < 6; i++)
            {
                bool passRight = i % 2 == 0; // D, I, D, I, D, I
                string name = $"Transverse_{i + 1:00}_{(passRight ? "Left" : "Right")}";
                Transform group = course.Find(name);
                Assert(group != null, $"Existe {name} (cono {i + 1} se pasa por la {(passRight ? "derecha" : "izquierda")})", ref total, ref passed);
                if (group == null) continue;

                var solids = new List<BoxCollider>();
                foreach (BoxCollider b in group.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (!b.isTrigger) solids.Add(b);
                }
                Assert(solids.Count == 2, $"{name} tiene 2 piezas sólidas (tiene {solids.Count})", ref total, ref passed);

                bool noCross = solids.Count > 0;
                bool lateral = solids.Count > 0;
                foreach (BoxCollider b in solids)
                {
                    // Ninguna pieza cruza X=60 hacia el lado libre.
                    if (passRight && b.bounds.max.x > 60f - 0.1f) noCross = false;
                    if (!passRight && b.bounds.min.x < 60f + 0.1f) noCross = false;
                    // A lo ancho de la calle: largo en X, fino en Z.
                    if (b.bounds.size.x < b.bounds.size.z * 3f) lateral = false;
                }
                Assert(noCross, $"{name}: ninguna pieza cruza X=60 hacia el lado libre", ref total, ref passed);
                Assert(lateral, $"{name}: las piezas quedan a lo ancho de la calle", ref total, ref passed);

                float freeGap = passRight ? rightInner - 60f : 60f - leftInner;
                Assert(freeGap >= 3.5f, $"{name}: el lado libre deja >= 3.5 m hasta la pared (deja {freeGap:F2} m)", ref total, ref passed);

                // Cubre desde la pared: la pieza exterior queda pegada a la pared del lado cerrado.
                float wallX = passRight ? 55.5f : 64.5f;
                bool touchesWall = false;
                foreach (BoxCollider b in solids)
                {
                    float edge = passRight ? b.bounds.min.x : b.bounds.max.x;
                    if (Mathf.Abs(edge - wallX) <= 0.2f) touchesWall = true;
                }
                Assert(touchesWall, $"{name}: arranca en la pared del corredor (X={wallX})", ref total, ref passed);
            }
        }

        // --- Corredor ----------------------------------------------------------------------------------------------

        private static void VerifyCorridor(ref int total, ref int passed)
        {
            Transform course = Course;
            Transform corridor = course != null ? course.Find(Lesson07BarrierManeuversSetup.CORRIDOR_NAME) : null;
            Assert(corridor != null, "Existe el corredor", ref total, ref passed);
            if (corridor == null) return;

            foreach (string name in new[] { "Wall_Left", "Wall_Right", "Cap_South", "Cap_North" })
            {
                Transform t = corridor.Find(name);
                LessonBoundary boundary = t != null ? t.GetComponent<LessonBoundary>() : null;
                Assert(boundary != null, $"{name} tiene LessonBoundary", ref total, ref passed);
                if (boundary == null) continue;

                Assert(boundary.BarrierPrefab != null, $"{name} tiene el prefab de valla asignado", ref total, ref passed);

                Transform barriers = t.Find(LessonBoundary.CONTAINER_NAME);
                int count = barriers != null ? barriers.childCount : 0;
                Assert(count > 0, $"{name} tiene vallas generadas ({count})", ref total, ref passed);
                if (barriers == null || count == 0) continue;

                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                foreach (BoxCollider b in barriers.GetComponentsInChildren<BoxCollider>())
                {
                    if (b.isTrigger) continue;
                    minX = Mathf.Min(minX, b.bounds.min.x);
                    maxX = Mathf.Max(maxX, b.bounds.max.x);
                    minZ = Mathf.Min(minZ, b.bounds.min.z);
                    maxZ = Mathf.Max(maxZ, b.bounds.max.z);
                }

                switch (name)
                {
                    case "Wall_Left":
                    case "Wall_Right":
                        float wallX = name == "Wall_Left" ? 55.5f : 64.5f;
                        Assert(Mathf.Abs((minX + maxX) * 0.5f - wallX) <= 0.1f, $"{name} en X≈{wallX}", ref total, ref passed);
                        Assert(minZ <= 8.2f && minZ >= 7.8f && maxZ >= 89.8f && maxZ <= 90.2f, $"{name} cubre Z 8..90 ({minZ:F2}..{maxZ:F2})", ref total, ref passed);
                        break;
                    default:
                        float capZ = name == "Cap_South" ? 8f : 90f;
                        Assert(Mathf.Abs((minZ + maxZ) * 0.5f - capZ) <= 0.1f, $"{name} en Z≈{capZ}", ref total, ref passed);
                        Assert(minX <= 55.7f && maxX >= 64.3f, $"{name} cierra de pared a pared ({minX:F2}..{maxX:F2})", ref total, ref passed);
                        break;
                }
            }
        }

        // --- Recuadros ---------------------------------------------------------------------------------------------

        private static void VerifyZones(ref int total, ref int passed)
        {
            VerifyZone(Lesson07BarrierManeuversSetup.START_ZONE_NAME, new Vector3(62.4f, 0.05f, 14.3f), "GroundVisual_StartYellow", ref total, ref passed);
            VerifyZone(Lesson07BarrierManeuversSetup.FORWARD_FINISH_ZONE_NAME, new Vector3(57.6f, 0.05f, 84f), "GroundVisual_ForwardYellow", ref total, ref passed);

            var car = Object.FindAnyObjectByType<VehicleController>();
            var start = GameObject.Find(Lesson07BarrierManeuversSetup.START_ZONE_NAME)?.GetComponent<ParkingZoneTrigger>();
            Assert(car != null && start != null && start.IsVehicleFullyInside(car, 0.25f), "Car09 arranca completo dentro de ParkingZone_Start", ref total, ref passed);
            if (car != null)
            {
                Assert(Mathf.Abs(car.transform.position.x - 62.4f) <= k_Tol && Mathf.Abs(car.transform.position.z - 14.3f) <= k_Tol &&
                       Mathf.Abs(Quaternion.Angle(car.transform.rotation, Quaternion.identity)) <= 0.5f,
                    $"Car09 en (62.4, y, 14.3) con yaw 0 (está en {car.transform.position})", ref total, ref passed);
            }
        }

        private static void VerifyZone(string zoneName, Vector3 center, string visualName, ref int total, ref int passed)
        {
            GameObject go = GameObject.Find(zoneName);
            Assert(go != null, $"Existe {zoneName}", ref total, ref passed);
            if (go == null) return;

            BoxCollider box = go.GetComponent<BoxCollider>();
            Assert(box != null && box.isTrigger, $"{zoneName} tiene BoxCollider trigger", ref total, ref passed);
            Assert(go.GetComponent<ParkingZoneTrigger>() != null, $"{zoneName} tiene ParkingZoneTrigger", ref total, ref passed);
            if (box == null) return;

            Assert(Vector3.Distance(go.transform.position, center) <= 0.001f, $"{zoneName} centrado en {center} (está en {go.transform.position})", ref total, ref passed);
            Assert(Mathf.Approximately(box.size.x, 3.6f) && Mathf.Approximately(box.size.z, 7f) && box.center == Vector3.zero,
                $"{zoneName} mide 3.6 x 7 m (mide {box.size.x} x {box.size.z})", ref total, ref passed);

            Transform quad = go.transform.Find(visualName);
            Assert(quad != null, $"{zoneName} tiene el quad {visualName}", ref total, ref passed);
            if (quad == null) return;

            Assert(Mathf.Approximately(quad.localScale.x, box.size.x) && Mathf.Approximately(quad.localScale.y, box.size.z),
                $"{visualName} tiene la escala del collider ({quad.localScale.x} x {quad.localScale.y})", ref total, ref passed);
            Assert(quad.GetComponent<Collider>() == null, $"{visualName} no tiene collider", ref total, ref passed);
        }

        // --- Exenciones --------------------------------------------------------------------------------------------

        private static void VerifyExemptZones(ref int total, ref int passed)
        {
            int laneZone = LayerMask.NameToLayer(RoadLaneZone.LayerName);
            var wrongWay = Object.FindAnyObjectByType<WrongWayExemptZone>();
            var turnSignal = Object.FindAnyObjectByType<TurnSignalExemptZone>();
            Assert(wrongWay != null, "Hay una WrongWayExemptZone", ref total, ref passed);
            Assert(turnSignal != null, "Hay una TurnSignalExemptZone", ref total, ref passed);

            foreach (MonoBehaviour zone in new MonoBehaviour[] { wrongWay, turnSignal })
            {
                if (zone == null) continue;
                BoxCollider box = zone.GetComponent<BoxCollider>();
                Assert(box != null && box.isTrigger, $"{zone.GetType().Name} es un BoxCollider trigger", ref total, ref passed);
                Assert(laneZone >= 0 && zone.gameObject.layer == laneZone, $"{zone.GetType().Name} está en la capa LaneZone", ref total, ref passed);
                if (box == null) continue;

                Bounds b = box.bounds;
                Assert(b.min.x <= 53f + 0.01f && b.max.x >= 67f - 0.01f && b.min.z <= 5f + 0.01f && b.max.z >= 93f - 0.01f,
                    $"{zone.GetType().Name} cubre X 53–67, Z 5–93", ref total, ref passed);
                Assert(b.min.y <= 0.3f && b.max.y >= 1.5f, $"{zone.GetType().Name} cubre la altura del punto de consulta (y {b.min.y:F1}..{b.max.y:F1})", ref total, ref passed);
            }
        }

        // --- Controlador -------------------------------------------------------------------------------------------

        private static void VerifyController(ref int total, ref int passed)
        {
            var ctrl = Object.FindAnyObjectByType<Lesson07BarrierManeuversController>();
            Assert(ctrl != null, "Existe Lesson07BarrierManeuversController", ref total, ref passed);
            if (ctrl == null) return;

            var so = new SerializedObject(ctrl);
            foreach (string field in new[] { "m_LessonManager", "m_VehicleController", "m_HUDPanel", "m_StartZone", "m_ForwardFinishZone", "m_StartZoneHighlight", "m_ForwardZoneHighlight" })
            {
                Assert(so.FindProperty(field).objectReferenceValue != null, $"Controlador: {field} serializado", ref total, ref passed);
            }

            Assert(AllAssigned(so.FindProperty("m_Cones"), 6), "Controlador: 6 conos serializados", ref total, ref passed);
            Assert(AllAssigned(so.FindProperty("m_TransverseBarriers"), 12), "Controlador: 12 piezas transversales serializadas", ref total, ref passed);
            Assert(AllAssigned(so.FindProperty("m_Boundaries"), 4), "Controlador: 4 tramos de corredor serializados", ref total, ref passed);
            Assert(so.FindProperty("m_BarrierCollisions") == null, "Controlador: ya no existe m_BarrierCollisions", ref total, ref passed);
            Assert(ctrl.ConeContacts == 0 && ctrl.BarrierContacts == 0, "Controlador: contadores de toques en 0", ref total, ref passed);

            // Las vallas del corredor no están en la lista de vallas transversales.
            bool disjoint = true;
            foreach (LessonBarrier barrier in ctrl.TransverseBarriers)
            {
                if (barrier != null && barrier.GetComponentInParent<LessonBoundary>(true) != null) disjoint = false;
            }
            Assert(disjoint, "Controlador: las vallas transversales no incluyen las del corredor", ref total, ref passed);
        }

        private static bool AllAssigned(SerializedProperty list, int expected)
        {
            if (list == null || list.arraySize != expected) return false;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null) return false;
            }
            return true;
        }

        private static void VerifyNoAutoCreate(ref int total, ref int passed)
        {
            // Escena sin controlador: Instance debe devolver null y no crear un GameObject.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            typeof(Lesson07BarrierManeuversController).GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);

            Lesson07BarrierManeuversController instance = Lesson07BarrierManeuversController.Instance;
            Assert(instance == null, "Instance sin controlador en la escena devuelve null", ref total, ref passed);
            Assert(GameObject.Find("Lesson07_Controller") == null, "Instance no crea un GameObject Lesson07_Controller", ref total, ref passed);
            Assert(Object.FindAnyObjectByType<Lesson07BarrierManeuversController>() == null, "No quedó ningún controlador en la escena", ref total, ref passed);

            var go = new GameObject("TMP_Lesson07");
            var withoutZones = go.AddComponent<Lesson07BarrierManeuversController>();
            Assert(!withoutZones.IsForwardSlalomComplete && !withoutZones.IsReverseSlalomComplete,
                "Sin recuadros ni auto, el slalom no se da por completo", ref total, ref passed);
            Object.DestroyImmediate(go);

            EditorSceneManager.OpenScene(Lesson07BarrierManeuversSetup.TARGET_SCENE_PATH, OpenSceneMode.Single);
        }

        // --- Finalización geométrica ---------------------------------------------------------------------------------

        private static void VerifyGeometricCompletion(ref int total, ref int passed)
        {
            var ctrl = Object.FindAnyObjectByType<Lesson07BarrierManeuversController>();
            var car = Object.FindAnyObjectByType<VehicleController>();
            if (ctrl == null || car == null) return;

            Vector3 originalPos = car.transform.position;
            Quaternion originalRot = car.transform.rotation;
            float y = originalPos.y;

            try
            {
                Place(car, new Vector3(57.6f, y, 84f), 0f);
                Assert(ctrl.IsForwardSlalomComplete, "Completo dentro del recuadro final y detenido: IsForwardSlalomComplete", ref total, ref passed);
                Assert(!ctrl.IsReverseSlalomComplete, "Completo en el recuadro final: IsReverseSlalomComplete es false", ref total, ref passed);

                // Sin chequeo de orientación: de costado dentro del recuadro de 3.6 x 7 m no cabe, pero con 10° sí.
                Place(car, new Vector3(57.6f, y, 84f), 10f);
                Assert(ctrl.IsForwardSlalomComplete, "Con 10° de yaw dentro del recuadro igual completa (sin chequeo de orientación)", ref total, ref passed);

                // A medio entrar: la cola queda afuera.
                Place(car, new Vector3(57.6f, y, 80f), 0f);
                Assert(!ctrl.IsForwardSlalomComplete, "A medio entrar al recuadro final: IsForwardSlalomComplete es false", ref total, ref passed);
                Assert(Lesson07BarrierManeuversController.OverlapsZone(ctrl.ForwardFinishZone, car), "A medio entrar: el auto se superpone con el recuadro", ref total, ref passed);

                // Lejos del recuadro.
                Place(car, new Vector3(60f, y, 50f), 0f);
                Assert(!ctrl.IsForwardSlalomComplete && !ctrl.IsReverseSlalomComplete, "En medio de la pista no completa ningún recuadro", ref total, ref passed);
                Assert(!Lesson07BarrierManeuversController.OverlapsZone(ctrl.ForwardFinishZone, car), "En medio de la pista no se superpone con el recuadro final", ref total, ref passed);

                // Recuadro de inicio (reversa).
                Place(car, new Vector3(62.4f, y, 14.3f), 0f);
                Assert(ctrl.IsReverseSlalomComplete, "Completo dentro del recuadro de inicio y detenido: IsReverseSlalomComplete", ref total, ref passed);
                Place(car, new Vector3(62.4f, y, 18.5f), 0f);
                Assert(!ctrl.IsReverseSlalomComplete, "A medio entrar al recuadro de inicio: IsReverseSlalomComplete es false", ref total, ref passed);

                Assert(ctrl.ForwardFinishZone.IsVehicleInside == false, "El resultado no depende de IsVehicleInside (eventos de trigger)", ref total, ref passed);
            }
            finally
            {
                Place(car, originalPos, originalRot.eulerAngles.y);
            }
        }

        private static void Place(VehicleController car, Vector3 position, float yaw)
        {
            car.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Physics.SyncTransforms();
        }

        private static void VerifyOverlapMath(ref int total, ref int passed)
        {
            Vector2 r = Vector2.right, f = Vector2.up;
            Assert(Lesson07BarrierManeuversController.OverlapsOrientedRects(Vector2.zero, r, f, new Vector2(1, 2), new Vector2(1.5f, 0), r, f, new Vector2(1, 2)),
                "SAT: dos rectángulos alineados que se pisan se superponen", ref total, ref passed);
            Assert(!Lesson07BarrierManeuversController.OverlapsOrientedRects(Vector2.zero, r, f, new Vector2(1, 2), new Vector2(2.5f, 0), r, f, new Vector2(1, 2)),
                "SAT: dos rectángulos alineados separados no se superponen", ref total, ref passed);

            // Un rectángulo rotado 45° cuya esquina entra en el otro, y otro que pasa cerca sin tocar (los AABB sí se pisan).
            Vector2 r45 = new Vector2(1, 1).normalized, f45 = new Vector2(-1, 1).normalized;
            Assert(Lesson07BarrierManeuversController.OverlapsOrientedRects(Vector2.zero, r, f, new Vector2(1, 1), new Vector2(2.2f, 0), r45, f45, new Vector2(1, 1)),
                "SAT: esquina de un rectángulo rotado dentro del otro se superpone", ref total, ref passed);
            Assert(!Lesson07BarrierManeuversController.OverlapsOrientedRects(Vector2.zero, r, f, new Vector2(1, 1), new Vector2(2.6f, 1.6f), r45, f45, new Vector2(1, 1)),
                "SAT: rectángulo rotado que solo comparte AABB no se superpone", ref total, ref passed);
        }

        // --- Pasos ---------------------------------------------------------------------------------------------------

        private static void VerifySteps(ref int total, ref int passed)
        {
            var lesson = AssetDatabase.LoadAssetAtPath<LessonDefinitionSO>(Lesson07BarrierManeuversSetup.LESSON_07_PATH);
            Assert(lesson != null, "Existe LessonData_07", ref total, ref passed);
            if (lesson == null || lesson.steps == null || lesson.steps.Count != 7) return;

            Assert(lesson.lessonTitle == "Lección 7: Dominio del Volante y Slalom entre Conos", $"Título de la lección ('{lesson.lessonTitle}')", ref total, ref passed);
            Assert(!lesson.lessonTitle.ToLowerInvariant().Contains("vallas"), "El título de la lección no dice 'vallas'", ref total, ref passed);

            LessonStepSO forward = lesson.steps[Lesson07BarrierManeuversController.ForwardSlalomStepIndex];
            LessonStepSO reverse = lesson.steps[Lesson07BarrierManeuversController.ReverseSlalomStepIndex];

            Assert(forward.requiredAction == LessonActionType.SlalomForwardComplete, "Paso 3 es SlalomForwardComplete", ref total, ref passed);
            Assert(reverse.requiredAction == LessonActionType.SlalomReverseComplete, "Paso 5 es SlalomReverseComplete", ref total, ref passed);
            Assert(forward.stepTitle == "Slalom hacia Adelante entre Conos", $"Título del paso 3 ('{forward.stepTitle}')", ref total, ref passed);

            foreach (LessonStepSO step in new[] { forward, reverse })
            {
                string all = (step.stepTitle + " " + step.instructionText).ToLowerInvariant();
                Assert(!all.Contains("vallas"), $"'{step.stepTitle}' no dice 'vallas'", ref total, ref passed);
                Assert(all.Contains("cono"), $"'{step.stepTitle}' menciona los conos", ref total, ref passed);
                Assert(all.Contains("recuadro"), $"'{step.stepTitle}' menciona el recuadro", ref total, ref passed);
                Assert(string.IsNullOrEmpty(step.highlightTargetName), $"'{step.stepTitle}' tiene highlightTargetName vacío", ref total, ref passed);
            }

            Assert(reverse.instructionText.ToLowerInvariant().Contains("medio auto"), "El paso en reversa menciona la regla del 'medio auto'", ref total, ref passed);

            bool anyVallas = false;
            foreach (LessonStepSO step in lesson.steps)
            {
                if ((step.stepTitle + " " + step.instructionText).ToLowerInvariant().Contains("vallas")) anyVallas = true;
            }
            Assert(!anyVallas, "Ningún paso de la lección dice 'vallas'", ref total, ref passed);
        }

        // --- InteractableHighlighter.FindDeep ------------------------------------------------------------------------

        private static void VerifyFindDeepLogsNothing(ref int total, ref int passed)
        {
            MethodInfo findDeep = typeof(InteractableHighlighter).GetMethod("FindDeep", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(findDeep != null, "InteractableHighlighter.FindDeep existe", ref total, ref passed);
            if (findDeep == null) return;

            var root = new GameObject("TMP_FindDeepRoot");
            try
            {
                Transform parent = root.transform;
                for (int i = 0; i < 4; i++)
                {
                    var child = new GameObject($"Nodo_{i}");
                    child.transform.SetParent(parent, false);
                    parent = child.transform;
                }

                int tagErrors = 0;
                object result = null;
                bool threw = false;
                void OnLog(string message, string stack, LogType type)
                {
                    if (message.Contains("is not defined")) tagErrors++;
                }

                Application.logMessageReceived += OnLog;
                try
                {
                    result = findDeep.Invoke(null, new object[] { root.transform, "NombreQueNoExiste_Lesson07" });
                }
                catch (TargetInvocationException)
                {
                    threw = true;
                }
                finally
                {
                    Application.logMessageReceived -= OnLog;
                }

                Assert(!threw, "FindDeep con un nombre inexistente no lanza (sin CompareTag)", ref total, ref passed);
                Assert(result == null, "FindDeep con un nombre inexistente devuelve null", ref total, ref passed);
                Assert(tagErrors == 0, $"FindDeep no loguea 'Tag: ... is not defined' ({tagErrors} errores)", ref total, ref passed);

                object found = findDeep.Invoke(null, new object[] { root.transform, "nodo_3" });
                Assert(found is Transform t && t.name == "Nodo_3", "FindDeep sigue encontrando por nombre (sin distinguir mayúsculas)", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Assert(bool condition, string testName, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  [PASS] {testName}");
            }
            else
            {
                Debug.LogError($"  [FAIL] {testName}");
            }
        }
    }
}
