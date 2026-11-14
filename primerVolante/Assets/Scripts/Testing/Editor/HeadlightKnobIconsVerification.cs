using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;
using Object = UnityEngine.Object;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de la perilla de luces de Car09:
    /// 1. Giro por muñeca: twist puro (ComputeTwistDegrees) independiente de la orientación inicial, signo (horario visto por el
    ///    conductor = hacia Altas) y cadena completa (UpdateRotationFromHand con rotaciones sintéticas, sin depender de la posición).
    /// 2. Collider: BoxCollider que cubre el bisel en los 4 ángulos, profundidad, sin solaparse con la palanca de giro ni otros colliders.
    /// 3. Íconos de modo: canvas hermano de la perilla (no gira), sin raycaster, sprites, posición/dirección según el puntero,
    ///    sin solaparse ni salirse del bisel, y alfa activo/tenue con los colores del tablero.
    /// Todo se instancia en una escena aditiva temporal que se cierra al terminar. El toque rápido (CycleMode al soltar sin giro)
    /// no cambió de lógica y requiere XR: se prueba en el visor.
    /// </summary>
    public static class HeadlightKnobIconsVerification
    {
        private const BindingFlags INSTANCE_ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly HeadlightMode[] MODES =
            { HeadlightMode.Off, HeadlightMode.Position, HeadlightMode.LowBeam, HeadlightMode.HighBeam };

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Verify Headlight Knob Icons")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE LA PERILLA DE LUCES (GIRO, COLLIDER E ÍCONOS)");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;

            bool dirtyBefore = AnySceneDirty();
            Scene original = SceneManager.GetActiveScene();
            Scene tmp = default;
            GameObject car = null;
            try
            {
                VerifyTwistMath();

                tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (original.IsValid()) SceneManager.SetActiveScene(original);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeadlightKnobIconsSetup.CAR09_PREFAB_PATH);
                Assert(prefab != null, "Car09.prefab existe");
                if (prefab != null)
                {
                    car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tmp);
                    Transform knob = car.transform.Find(HeadlightKnobIconsSetup.KNOB_NAME);
                    Transform bezel = car.transform.Find(HeadlightKnobIconsSetup.BEZEL_NAME);
                    Assert(knob != null && bezel != null, "Car09 tiene la perilla y el bisel");
                    if (knob != null && bezel != null)
                    {
                        var script = knob.GetComponent<VRHeadlightKnob>();
                        Assert(script != null, "La perilla tiene VRHeadlightKnob");
                        if (script != null)
                        {
                            VerifyKnobTwistSign(car.transform, knob, script);
                            VerifyWristChain(car.transform, knob, script);
                            VerifyCollider(car.transform, knob, bezel, script);
                            VerifyIcons(car.transform, knob, bezel, script);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Assert(false, $"La verificación lanzó una excepción: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                if (car != null) Object.DestroyImmediate(car);
                if (original.IsValid() && SceneManager.GetActiveScene() != original) SceneManager.SetActiveScene(original);
                if (tmp.IsValid()) EditorSceneManager.CloseScene(tmp, true);
            }
            Assert(AnySceneDirty() == dirtyBefore, "La verificación no deja ninguna escena abierta sucia");

            Debug.Log("=========================================================");
            if (s_Passed == s_Total)
                Debug.Log($"🎉 VERIFICACIÓN DE LA PERILLA DE LUCES EXITOSA: {s_Passed}/{s_Total} pruebas pasadas.");
            else
                Debug.LogError($"❌ [FAIL] VERIFICACIÓN DE LA PERILLA DE LUCES CON FALLOS: {s_Passed}/{s_Total} pruebas pasadas.");
            Debug.Log("=========================================================");
        }

        // =====================================================================
        // 1a. Matemática pura del twist
        // =====================================================================
        private static void VerifyTwistMath()
        {
            Quaternion id = Quaternion.identity;
            Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(id, id), 0f), "Twist: identidad => 0");

            Quaternion[] starts =
            {
                Quaternion.identity,
                Quaternion.Euler(45f, 0f, 0f),   // control inclinado 45 grados
                Quaternion.Euler(0f, 90f, 0f),   // control girado a un costado
                Quaternion.Euler(20f, -70f, 35f) // orientación arbitraria
            };
            string[] names = { "apuntando adelante", "inclinado 45", "girado a un costado", "arbitraria" };

            for (int i = 0; i < starts.Length; i++)
            {
                Quaternion s = starts[i];
                Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(s, s * Quaternion.AngleAxis(30f, Vector3.forward)), 30f),
                    $"Twist +30 alrededor del forward del control ({names[i]}) => +30");
                Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(s, s * Quaternion.AngleAxis(-30f, Vector3.forward)), -30f),
                    $"Twist -30 alrededor del forward del control ({names[i]}) => -30");
            }

            Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(id, Quaternion.Euler(40f, 0f, 0f)), 0f, 0.05f), "Swing puro (pitch 40) => ~0");
            Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(id, Quaternion.Euler(0f, 40f, 0f)), 0f, 0.05f), "Swing puro (yaw 40) => ~0");
            Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(id, Quaternion.Euler(40f, 0f, 0f) * Quaternion.AngleAxis(30f, Vector3.forward)), 30f, 0.05f),
                "Twist 30 combinado con swing (pitch 40) => ~30");
            Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(id, Quaternion.Euler(0f, 40f, 0f) * Quaternion.AngleAxis(-30f, Vector3.forward)), -30f, 0.05f),
                "Twist -30 combinado con swing (yaw 40) => ~-30");
            Assert(Near(VRHeadlightKnob.ComputeTwistDegrees(id, Quaternion.AngleAxis(200f, Vector3.forward)), -160f),
                "Twist de 200 grados se normaliza a -160");

            // Mismo twist con swings distintos en la rotación inicial y final => mismo delta
            Quaternion a = Quaternion.Euler(0f, 90f, 0f);
            float d1 = VRHeadlightKnob.ComputeTwistDegrees(a, a * Quaternion.AngleAxis(25f, Vector3.forward));
            float d2 = VRHeadlightKnob.ComputeTwistDegrees(Quaternion.Euler(45f, 0f, 0f),
                Quaternion.Euler(45f, 0f, 0f) * Quaternion.AngleAxis(25f, Vector3.forward));
            Assert(Near(d1, d2), "Mismo twist con distintas orientaciones iniciales => mismo delta");
        }

        // =====================================================================
        // 1b. Signo: horario visto por el conductor => Off hacia Altas
        // =====================================================================
        private static void VerifyKnobTwistSign(Transform car, Transform knob, VRHeadlightKnob script)
        {
            // El conductor mira hacia +forward de Car09 con la perilla delante: una rotación positiva de Unity alrededor de
            // su dirección de mirada es antihoraria para él, así que "horario" = ángulo negativo alrededor de car.forward.
            Vector3 f = car.forward;
            Quaternion start = Quaternion.LookRotation(f, car.up);
            Quaternion clockwiseWrist = Quaternion.AngleAxis(-30f, f) * start;
            float twist = VRHeadlightKnob.ComputeTwistDegrees(start, clockwiseWrist);
            Assert(Near(twist, -30f), $"Muñeca 30 grados en sentido horario visto por el conductor => twist -30 (obtenido {twist:F2})");

            float fromOff = script.GetAngleForMode(HeadlightMode.Off);
            float fromHigh = script.GetAngleForMode(HeadlightMode.HighBeam);
            Assert(VRHeadlightKnob.KnobAngleFromTwist(fromOff, twist) > fromOff,
                "Muñeca en sentido horario aumenta el ángulo de la perilla (de Off hacia Altas)");
            Quaternion ccwWrist = Quaternion.AngleAxis(30f, f) * start;
            Assert(VRHeadlightKnob.KnobAngleFromTwist(fromHigh, VRHeadlightKnob.ComputeTwistDegrees(start, ccwWrist)) < fromHigh,
                "Muñeca en sentido antihorario reduce el ángulo (de Altas hacia Off)");
            Assert(Near(VRHeadlightKnob.KnobAngleFromTwist(-45f, twist), -15f), "Relación 1:1 entre twist y ángulo de la perilla");

            // Con el mesh real, aumentar el ángulo mueve el puntero en sentido horario visto desde el asiento
            script.SetMode(HeadlightMode.Off, false);
            Vector2 pOff = DriverViewDirection(car, knob);
            script.SetMode(HeadlightMode.HighBeam, false);
            Vector2 pHigh = DriverViewDirection(car, knob);
            script.SetMode(HeadlightMode.Off, false);
            float cross = pOff.x * pHigh.y - pOff.y * pHigh.x;
            Assert(cross < -0.1f, $"El puntero va de Off a Altas en sentido horario visto por el conductor (cruz {cross:F2})");
        }

        // =====================================================================
        // 1c. Cadena completa: UpdateRotationFromHand con rotaciones sintéticas
        // =====================================================================
        private static void VerifyWristChain(Transform car, Transform knob, VRHeadlightKnob script)
        {
            MethodInfo update = typeof(VRHeadlightKnob).GetMethod("UpdateRotationFromHand", INSTANCE_ANY);
            FieldInfo startRot = typeof(VRHeadlightKnob).GetField("m_GrabStartHandRotation", INSTANCE_ANY);
            FieldInfo startAngle = typeof(VRHeadlightKnob).GetField("m_GrabStartAngle", INSTANCE_ANY);
            FieldInfo curAngle = typeof(VRHeadlightKnob).GetField("m_CurrentAngle", INSTANCE_ANY);
            Assert(update != null && startRot != null && startAngle != null && curAngle != null,
                "VRHeadlightKnob expone la lógica de agarre (UpdateRotationFromHand, m_GrabStartHandRotation)");
            if (update == null || startRot == null || startAngle == null || curAngle == null) return;

            var hand = new GameObject("FakeHand");
            try
            {
                Vector3 f = car.forward;
                Quaternion start = Quaternion.LookRotation(f, car.up);

                int events = 0;
                HeadlightMode last = HeadlightMode.Off;
                Action<HeadlightMode> handler = m => { events++; last = m; };
                script.ModeChanged += handler;

                // m_GrabStartHandRotation se guarda en el marco del auto (como lo hace OnGrab).
                Quaternion startInCar = Quaternion.Inverse(car.rotation) * start;

                script.SetMode(HeadlightMode.Off, false);
                startRot.SetValue(script, startInCar);
                startAngle.SetValue(script, script.GetAngleForMode(HeadlightMode.Off));

                // Muñeca horaria 90 grados, mano en una posición cualquiera => Altas
                hand.transform.SetPositionAndRotation(car.position + new Vector3(0.1f, 0.2f, -0.3f), Quaternion.AngleAxis(-90f, f) * start);
                update.Invoke(script, new object[] { hand.transform });
                Assert(script.CurrentMode == HeadlightMode.HighBeam && events >= 1 && last == HeadlightMode.HighBeam,
                    $"Muñeca horaria 90 grados lleva Off a Altas y emite ModeChanged (modo {script.CurrentMode}, eventos {events})");
                float angleA = (float)curAngle.GetValue(script);

                // Mismo giro, mano en otro lugar y lejos del centro => mismo ángulo (no depende de la posición)
                hand.transform.SetPositionAndRotation(car.position + new Vector3(-0.5f, -0.3f, 0.6f), Quaternion.AngleAxis(-90f, f) * start);
                update.Invoke(script, new object[] { hand.transform });
                Assert(Near((float)curAngle.GetValue(script), angleA), "El ángulo no depende de la posición de la mano, solo de la rotación de la muñeca");

                // Traslación pura sin rotar => sin cambio de ángulo
                startRot.SetValue(script, startInCar);
                startAngle.SetValue(script, script.GetAngleForMode(HeadlightMode.Position));
                script.SetMode(HeadlightMode.Position, false);
                hand.transform.SetPositionAndRotation(car.position + new Vector3(0.4f, 0.4f, 0.4f), start);
                update.Invoke(script, new object[] { hand.transform });
                Assert(Near((float)curAngle.GetValue(script), script.GetAngleForMode(HeadlightMode.Position)),
                    "Mover la mano sin rotar la muñeca no cambia el ángulo");

                // Antihorario 150 grados desde Posición (-15) => -165 sin clamp: se limita a min - 10 = -55
                hand.transform.SetPositionAndRotation(car.position, Quaternion.AngleAxis(150f, f) * start);
                update.Invoke(script, new object[] { hand.transform });
                float clampedLow = (float)curAngle.GetValue(script);
                Assert(Near(clampedLow, -55f), $"El ángulo se limita a min - 10 grados (obtenido {clampedLow:F1})");

                hand.transform.SetPositionAndRotation(car.position, Quaternion.AngleAxis(-170f, f) * start);
                update.Invoke(script, new object[] { hand.transform });
                float clampedHigh = (float)curAngle.GetValue(script);
                Assert(Near(clampedHigh, 55f), $"El ángulo se limita a max + 10 grados (obtenido {clampedHigh:F1})");

                // El auto gira mientras la perilla está agarrada con el control inclinado: la mano acompaña al auto
                // sin girar la muñeca => el ángulo no cambia (la rotación se mide en el marco del auto).
                Quaternion carRotBefore = car.rotation;
                try
                {
                    Quaternion pitched = Quaternion.LookRotation(Quaternion.AngleAxis(35f, car.right) * f, car.up);
                    script.SetMode(HeadlightMode.Position, false);
                    startRot.SetValue(script, Quaternion.Inverse(car.rotation) * pitched);
                    startAngle.SetValue(script, script.GetAngleForMode(HeadlightMode.Position));
                    events = 0;

                    foreach (Quaternion carTurn in new[] { Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(8f, -60f, 12f) })
                    {
                        car.rotation = carTurn * carRotBefore;
                        hand.transform.rotation = carTurn * pitched;
                        update.Invoke(script, new object[] { hand.transform });
                        Assert(Near((float)curAngle.GetValue(script), script.GetAngleForMode(HeadlightMode.Position)) && script.CurrentMode == HeadlightMode.Position,
                            $"Girar o inclinar el auto con la perilla agarrada (control inclinado) no cambia el ángulo (obtenido {(float)curAngle.GetValue(script):F2})");
                    }
                    Assert(events == 0, "Girar el auto con la perilla agarrada no emite ModeChanged");

                    // Con el auto girado, un giro real de muñeca sigue funcionando
                    Quaternion carTurned = Quaternion.Euler(0f, 90f, 0f);
                    car.rotation = carTurned * carRotBefore;
                    Vector3 fTurned = car.forward;
                    hand.transform.rotation = Quaternion.AngleAxis(-30f, fTurned) * (carTurned * pitched);
                    update.Invoke(script, new object[] { hand.transform });
                    float turnedAngle = (float)curAngle.GetValue(script);
                    Assert(turnedAngle > script.GetAngleForMode(HeadlightMode.Position) + 15f,
                        $"Con el auto girado, la muñeca en sentido horario sigue llevando hacia Altas (obtenido {turnedAngle:F2})");
                }
                finally
                {
                    car.rotation = carRotBefore;
                }

                script.ModeChanged -= handler;
                script.SetMode(HeadlightMode.Off, false);
            }
            finally
            {
                Object.DestroyImmediate(hand);
            }
        }

        // =====================================================================
        // 2. Collider sobre el bisel
        // =====================================================================
        private static void VerifyCollider(Transform car, Transform knob, Transform bezel, VRHeadlightKnob script)
        {
            var box = knob.GetComponent<BoxCollider>();
            Assert(box != null, "La perilla tiene BoxCollider");
            Assert(knob.GetComponent<SphereCollider>() == null, "La perilla ya no tiene SphereCollider");
            Assert(knob.GetComponent<XRGrabInteractable>() != null, "La perilla conserva XRGrabInteractable");
            if (box == null) return;

            Assert(!box.isTrigger && box.enabled, "El BoxCollider no es trigger y está habilitado");

            var grab = knob.GetComponent<XRGrabInteractable>();
            bool collidersOk = grab != null && (grab.colliders.Count == 0 || (grab.colliders.Count == 1 && grab.colliders[0] == box));
            Assert(collidersOk, "XRGrabInteractable.colliders resuelve al BoxCollider (vacío => Awake lo toma, o asignado)");

            Vector3 axisCar = car.InverseTransformDirection(knob.TransformDirection(Vector3.up)).normalized;
            Vector3 center = car.InverseTransformPoint(knob.position);
            float face = HeadlightKnobIconsSetup.BezelFaceOffset(car, bezel, center, axisCar);
            float bezelRadius = BezelRadius(car, bezel, center, axisCar);

            // Profundidad hacia el conductor (desde la cara del bisel) y espesor total
            Vector3 s = knob.lossyScale;
            float top = (box.center.y + box.size.y * 0.5f) * s.y;
            float bottom = (box.center.y - box.size.y * 0.5f) * s.y;
            Assert(top - face >= 0.02f, $"El collider se extiende >= 2 cm hacia el conductor desde la cara del bisel ({(top - face) * 100f:F1} cm)");
            Assert(bottom <= face && face - bottom >= 0.003f, $"El collider arranca por detrás de la cara del bisel ({(face - bottom) * 100f:F1} cm)");
            Assert(box.size.x * s.x >= 0.065f && box.size.z * s.z >= 0.065f,
                $"El collider mide >= 6.5 cm en el plano de la cara ({box.size.x * s.x * 100f:F1} x {box.size.z * s.z * 100f:F1} cm)");

            // Cubre el disco del bisel (borde y cara) en los 4 ángulos de snap
            Vector3 u = Vector3.Cross(axisCar, Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axisCar, u).normalized;
            foreach (HeadlightMode mode in MODES)
            {
                script.SetMode(mode, false);
                bool covered = true;
                for (int i = 0; i < 48 && covered; i++)
                {
                    float t = i / 48f * Mathf.PI * 2f;
                    Vector3 edge = center + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * bezelRadius;
                    foreach (float depth in new[] { 0f, face })
                    {
                        Vector3 p = edge + axisCar * depth;
                        Vector3 local = box.transform.InverseTransformPoint(car.TransformPoint(p)) - box.center;
                        covered &= Mathf.Abs(local.x) <= box.size.x * 0.5f && Mathf.Abs(local.z) <= box.size.z * 0.5f
                                   && Mathf.Abs(local.y) <= box.size.y * 0.5f;
                    }
                }
                Assert(covered, $"El BoxCollider cubre el borde del bisel con la perilla en {mode}");
            }

            // No se solapa con la palanca de giro (ni con ningún otro collider) en ningún ángulo
            Collider turnSignal = null;
            Transform ts = car.Find("TurnSignal");
            if (ts != null) turnSignal = ts.GetComponent<Collider>();
            Assert(turnSignal != null, "Car09 tiene el collider de TurnSignal");

            foreach (HeadlightMode mode in MODES)
            {
                script.SetMode(mode, false);
                string overlaps = "";
                foreach (Collider other in car.GetComponentsInChildren<Collider>())
                {
                    if (other == box || !other.enabled || other.isTrigger) continue;
                    if (other.transform.IsChildOf(knob)) continue;
                    if (other.gameObject == car.gameObject) continue; // collider de carrocería en la raíz: envuelve todo el tablero
                    if (Physics.ComputePenetration(box, box.transform.position, box.transform.rotation,
                            other, other.transform.position, other.transform.rotation, out _, out _))
                        overlaps += other.name + " ";
                }
                Assert(overlaps.Length == 0, $"El BoxCollider no se solapa con otros colliders de Car09 con la perilla en {mode} ({overlaps.Trim()})");
            }
            script.SetMode(HeadlightMode.Off, false);
        }

        // =====================================================================
        // 3. Íconos de modo
        // =====================================================================
        private static void VerifyIcons(Transform car, Transform knob, Transform bezel, VRHeadlightKnob script)
        {
            Transform canvasT = car.Find(HeadlightKnobIconsSetup.CANVAS_NAME);
            Assert(canvasT != null, $"Car09 tiene {HeadlightKnobIconsSetup.CANVAS_NAME}");
            if (canvasT == null) return;

            Assert(!canvasT.IsChildOf(knob), "El canvas de íconos no es descendiente de la perilla (no gira)");
            Assert(canvasT.parent == car, "El canvas de íconos es hijo directo de Car09");
            Assert(canvasT.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0, "El canvas de íconos no tiene GraphicRaycaster");
            Assert(canvasT.GetComponentsInChildren<Collider>(true).Length == 0, "El canvas de íconos no tiene colliders");
            var canvas = canvasT.GetComponent<Canvas>();
            Assert(canvas != null && canvas.renderMode == RenderMode.WorldSpace, "El canvas de íconos es WorldSpace");

            // Sprite nuevo del modo Off
            string offPath = HeadlightKnobIconsSetup.SPRITE_PATHS[0];
            var importer = AssetImporter.GetAtPath(offPath) as TextureImporter;
            var offSprite = AssetDatabase.LoadAssetAtPath<Sprite>(offPath);
            Assert(importer != null && importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled && offSprite != null,
                "icon_lights_off.png existe importado como Sprite sin mipmaps");
            Assert(offSprite != null && offSprite.texture.width == 256 && offSprite.texture.height == 256,
                "icon_lights_off.png mide 256x256");

            // Estructura y sprites
            var images = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                Transform t = canvasT.Find(HeadlightKnobIconsSetup.ICON_NAMES[i]);
                images[i] = t != null ? t.GetComponent<Image>() : null;
                Assert(images[i] != null, $"Existe {HeadlightKnobIconsSetup.ICON_NAMES[i]} con Image");
                if (images[i] == null) return;
                Assert(AssetDatabase.GetAssetPath(images[i].sprite) == HeadlightKnobIconsSetup.SPRITE_PATHS[i],
                    $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} usa {HeadlightKnobIconsSetup.SPRITE_PATHS[i]}");
                Assert(!images[i].raycastTarget, $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} tiene raycastTarget desactivado");
            }

            Vector3 axisCar = car.InverseTransformDirection(knob.TransformDirection(Vector3.up)).normalized;
            Vector3 center = car.InverseTransformPoint(knob.position);
            float face = HeadlightKnobIconsSetup.BezelFaceOffset(car, bezel, center, axisCar);
            float bezelRadius = BezelRadius(car, bezel, center, axisCar);

            // Posición de cada ícono: radio, dirección del puntero, desplazamiento hacia el conductor, derechos
            Vector3[] initialPositions = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = car.InverseTransformPoint(images[i].transform.position);
                initialPositions[i] = images[i].transform.position;
                Vector3 rel = p - center;
                float along = Vector3.Dot(rel, axisCar);
                Vector3 inPlane = rel - axisCar * along;

                Assert(Mathf.Abs(inPlane.magnitude - 0.026f) <= 0.003f,
                    $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} está a radio 2.6 +/- 0.3 cm del eje ({inPlane.magnitude * 100f:F2} cm)");

                script.SetMode(MODES[i], false);
                Vector3 pointer = car.InverseTransformDirection(knob.forward);
                pointer -= axisCar * Vector3.Dot(pointer, axisCar);
                float err = Vector3.Angle(inPlane, pointer);
                Assert(err < 3f, $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} está en la dirección del puntero de {MODES[i]} (error {err:F2} grados)");

                Assert(along - face >= 0.001f, $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} está desplazado hacia el conductor respecto de la cara del bisel ({(along - face) * 1000f:F1} mm)");
                Assert(Vector3.Dot(images[i].transform.up, car.up) > 0.999f && Vector3.Dot(images[i].transform.forward, car.forward) > 0.999f,
                    $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} está derecho visto desde el asiento");
            }
            script.SetMode(HeadlightMode.Off, false);

            // Sin solapes y dentro del bisel (esquinas del rectángulo del ícono)
            for (int i = 0; i < 4; i++)
            {
                Vector3[] ci = new Vector3[4];
                ((RectTransform)images[i].transform).GetWorldCorners(ci);
                float maxR = 0f;
                foreach (Vector3 c in ci)
                {
                    Vector3 rel = car.InverseTransformPoint(c) - center;
                    rel -= axisCar * Vector3.Dot(rel, axisCar);
                    maxR = Mathf.Max(maxR, rel.magnitude);
                }
                Assert(maxR <= bezelRadius, $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} no se sale del bisel (esquina a {maxR * 100f:F2} cm, bisel {bezelRadius * 100f:F2} cm)");

                for (int j = i + 1; j < 4; j++)
                {
                    Rect a = PlaneRect(car, (RectTransform)images[i].transform);
                    Rect b = PlaneRect(car, (RectTransform)images[j].transform);
                    Assert(!a.Overlaps(b), $"{HeadlightKnobIconsSetup.ICON_NAMES[i]} y {HeadlightKnobIconsSetup.ICON_NAMES[j]} no se solapan");
                }
            }

            // Tamaño ~1.0-1.1 cm
            float size = ((RectTransform)images[0].transform).rect.width * images[0].transform.lossyScale.x;
            Assert(size >= 0.0095f && size <= 0.0115f, $"Los íconos miden ~1 cm ({size * 100f:F2} cm)");

            VerifyRefresh(canvasT, images, script, car);

            // Rotar la perilla no mueve los íconos
            bool still = true;
            foreach (HeadlightMode mode in MODES)
            {
                script.SetMode(mode, false);
                for (int i = 0; i < 4; i++)
                    still &= Vector3.Distance(images[i].transform.position, initialPositions[i]) < 1e-6f;
            }
            script.SetMode(HeadlightMode.Off, false);
            Assert(still, "Al rotar la perilla (SetMode animated:false) los íconos no se mueven");
        }

        private static void VerifyRefresh(Transform canvasT, Image[] images, VRHeadlightKnob script, Transform car)
        {
            var comp = canvasT.GetComponent<HeadlightKnobModeIcons>();
            Assert(comp != null, "El canvas de íconos tiene HeadlightKnobModeIcons");
            if (comp == null) return;

            var so = new SerializedObject(comp);
            Assert(so.FindProperty("m_Knob").objectReferenceValue == script, "HeadlightKnobModeIcons apunta a la perilla de Car09");
            SerializedProperty arr = so.FindProperty("m_Icons");
            bool wired = arr.arraySize == 4;
            for (int i = 0; wired && i < 4; i++) wired &= arr.GetArrayElementAtIndex(i).objectReferenceValue == images[i];
            Assert(wired, "HeadlightKnobModeIcons tiene los 4 íconos en orden de HeadlightMode");

            float inactive = so.FindProperty("m_InactiveAlpha").floatValue;
            Assert(Near(inactive, 0.45f, 0.1f), $"Alfa de íconos inactivos ~0.45 ({inactive:F2})");

            // Colores del tablero: se leen del DashboardUIController del mismo auto
            Color[] expected =
            {
                new Color(0.9f, 0.9f, 0.9f), new Color(0.2f, 0.85f, 0.3f), new Color(0.102f, 0.949f, 0.627f), new Color(0.102f, 0.482f, 1f)
            };
            var dash = car.GetComponentInChildren<DashboardUIController>(true);
            if (dash != null)
            {
                var dso = new SerializedObject(dash);
                expected[1] = dso.FindProperty("m_PositionActiveColor").colorValue;
                expected[2] = dso.FindProperty("m_LowBeamActiveColor").colorValue;
                expected[3] = dso.FindProperty("m_HighBeamActiveColor").colorValue;
            }

            foreach (HeadlightMode mode in MODES)
            {
                comp.Refresh(mode);
                bool ok = true;
                for (int i = 0; i < 4; i++)
                {
                    Color c = images[i].color;
                    float wantAlpha = i == (int)mode ? 1f : inactive;
                    ok &= Mathf.Abs(c.a - wantAlpha) < 0.001f
                          && Mathf.Abs(c.r - expected[i].r) < 0.001f && Mathf.Abs(c.g - expected[i].g) < 0.001f && Mathf.Abs(c.b - expected[i].b) < 0.001f;
                }
                Assert(ok, $"Refresh({mode}): el ícono activo a alfa 1, los demás tenues, con los colores del tablero");
            }

            // Suscripción a ModeChanged (OnEnable/OnDisable no corren en edit mode: se invocan por reflexión)
            MethodInfo onEnable = typeof(HeadlightKnobModeIcons).GetMethod("OnEnable", INSTANCE_ANY);
            MethodInfo onDisable = typeof(HeadlightKnobModeIcons).GetMethod("OnDisable", INSTANCE_ANY);
            script.SetMode(HeadlightMode.Off, false);
            comp.Refresh(HeadlightMode.Off);
            onEnable.Invoke(comp, null);
            script.SetMode(HeadlightMode.HighBeam, false);
            Assert(images[3].color.a > 0.99f && images[0].color.a < 0.99f, "Al cambiar el modo de la perilla, los íconos se actualizan solos");
            onDisable.Invoke(comp, null);
            script.SetMode(HeadlightMode.LowBeam, false);
            Assert(images[3].color.a > 0.99f, "Tras OnDisable ya no reacciona a ModeChanged (sin fugas de suscripción)");
            onEnable.Invoke(comp, null);
            Assert(images[2].color.a > 0.99f && images[3].color.a < 0.99f,
                "Al rehabilitarse, los íconos se resincronizan con el modo que cambió mientras estaban deshabilitados");
            onDisable.Invoke(comp, null);
            script.SetMode(HeadlightMode.Off, false);
            comp.Refresh(HeadlightMode.Off);
        }

        // =====================================================================
        // Utilidades
        // =====================================================================

        /// <summary>Dirección del puntero (eje local +Z del mesh) en el plano que ve el conductor: (derecha, arriba) de Car09.</summary>
        private static Vector2 DriverViewDirection(Transform car, Transform knob)
        {
            Vector3 d = car.InverseTransformDirection(knob.forward);
            return new Vector2(d.x, d.y).normalized;
        }

        /// <summary>Radio máximo del bisel respecto del eje de la perilla (en el plano de la cara).</summary>
        private static float BezelRadius(Transform car, Transform bezel, Vector3 center, Vector3 axisCar)
        {
            float max = 0f;
            foreach (Vector3 vtx in bezel.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                Vector3 rel = car.InverseTransformPoint(bezel.TransformPoint(vtx)) - center;
                rel -= axisCar * Vector3.Dot(rel, axisCar);
                max = Mathf.Max(max, rel.magnitude);
            }
            return max;
        }

        /// <summary>Rectángulo del ícono en el plano (derecha, arriba) de Car09.</summary>
        private static Rect PlaneRect(Transform car, RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3 a = car.InverseTransformPoint(corners[0]);
            Vector3 c = car.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }

        private static bool Near(float a, float b, float tol = 0.01f) => Mathf.Abs(a - b) <= tol;

        private static bool AnySceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }

        private static void Assert(bool condition, string message)
        {
            s_Total++;
            if (condition)
            {
                s_Passed++;
                Debug.Log($"  ✅ [PASS] {message}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {message}");
            }
        }
    }
}
