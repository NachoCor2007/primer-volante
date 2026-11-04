using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor del ajuste de espejos laterales con el joystick del mando izquierdo:
    /// 1. Zona muerta radial proporcional (ApplyRadialDeadzone).
    /// 2. Car09.prefab sin D-Pad (solo los 3 selectores) y con el panel.
    /// 3. VRSideMirrorControlPanel sin campos del D-Pad y con la acción y la zona muerta del joystick.
    /// 4. CanUseJoystick: selector Off, bloqueo por lección y menú in-game abierto.
    /// 5. Texto del paso 4 de la Lección 1.
    /// El comportamiento con input real se prueba en Play Mode (ver prompt de la feature).
    /// </summary>
    public static class SideMirrorJoystickVerification
    {
        private const string CAR09_PREFAB_PATH =
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";
        private const string STEP04_PATH = "Assets/Scripts/Lessons/Steps/Lesson01/Lesson01_Step04_LeftMirror.asset";
        private const BindingFlags INSTANCE_ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/Primer Volante/Run Side Mirror Joystick Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL JOYSTICK DE ESPEJOS LATERALES");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            VerifyDeadzone(ref totalTests, ref passedTests);
            VerifyPrefab(ref totalTests, ref passedTests);
            VerifyFields(ref totalTests, ref passedTests);
            VerifyCanUseJoystick(ref totalTests, ref passedTests);
            VerifyLessonText(ref totalTests, ref passedTests);

            Debug.Log("=========================================================");
            if (passedTests == totalTests)
            {
                Debug.Log($"🎉 VERIFICACIÓN DEL JOYSTICK DE ESPEJOS EXITOSA: {passedTests}/{totalTests} pruebas pasadas.");
            }
            else
            {
                Debug.LogError($"❌ [FAIL] VERIFICACIÓN DEL JOYSTICK DE ESPEJOS CON FALLOS: {passedTests}/{totalTests} pruebas pasadas.");
            }
            Debug.Log("=========================================================");
        }

        // =====================================================================
        // 1. Zona muerta radial
        // =====================================================================
        private static void VerifyDeadzone(ref int total, ref int passed)
        {
            const float dz = 0.2f;

            Vector2 r = VRSideMirrorControlPanel.ApplyRadialDeadzone(new Vector2(0.15f, 0f), dz);
            Assert(r == Vector2.zero, "Zona muerta: (0.15, 0) -> cero", ref total, ref passed);

            r = VRSideMirrorControlPanel.ApplyRadialDeadzone(new Vector2(0.2f, 0f), dz);
            Assert(r == Vector2.zero, "Zona muerta: (0.2, 0) en el borde -> cero", ref total, ref passed);

            r = VRSideMirrorControlPanel.ApplyRadialDeadzone(new Vector2(1f, 0f), dz);
            Assert(Vector2.Distance(r, new Vector2(1f, 0f)) < 1e-4f, $"Zona muerta: (1, 0) -> (1, 0) (obtenido {r})", ref total, ref passed);

            r = VRSideMirrorControlPanel.ApplyRadialDeadzone(new Vector2(0.6f, 0f), dz);
            Assert(Mathf.Abs(r.magnitude - 0.5f) < 1e-4f, $"Zona muerta: (0.6, 0) -> magnitud 0.5 (obtenido {r.magnitude})", ref total, ref passed);

            r = VRSideMirrorControlPanel.ApplyRadialDeadzone(new Vector2(0.7071f, 0.7071f), dz);
            Assert(Mathf.Abs(r.magnitude - 1f) < 1e-3f && Mathf.Abs(r.x - r.y) < 1e-4f && r.x > 0f,
                $"Zona muerta: diagonal (0.7071, 0.7071) -> magnitud ~1 con dirección conservada (obtenido {r})", ref total, ref passed);

            r = VRSideMirrorControlPanel.ApplyRadialDeadzone(new Vector2(0f, -1f), dz);
            Assert(Vector2.Distance(r, new Vector2(0f, -1f)) < 1e-4f, $"Zona muerta: (0, -1) -> (0, -1) (obtenido {r})", ref total, ref passed);
        }

        // =====================================================================
        // 2. Prefab Car09 sin D-Pad
        // =====================================================================
        private static void VerifyPrefab(ref int total, ref int passed)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CAR09_PREFAB_PATH);
            try
            {
                var panel = root.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                Assert(panel != null, "Car09.prefab tiene VRSideMirrorControlPanel", ref total, ref passed);
                if (panel == null) return;

                foreach (string name in new[] { "Btn_Select_Left", "Btn_Select_Off", "Btn_Select_Right" })
                {
                    Assert(panel.transform.Find(name) != null, $"Car09.prefab: el panel conserva {name}", ref total, ref passed);
                }

                int dpad = 0;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("Btn_DPad")) dpad++;
                }
                Assert(dpad == 0, $"Car09.prefab no debe tener ningún Btn_DPad_* (encontrados: {dpad})", ref total, ref passed);

                // Zona muerta serializada en el prefab
                var so = new SerializedObject(panel);
                SerializedProperty dz = so.FindProperty("m_JoystickDeadzone");
                Assert(dz != null && Mathf.Abs(dz.floatValue - 0.2f) < 1e-5f,
                    $"Car09.prefab: m_JoystickDeadzone = 0.2 (actual: {(dz != null ? dz.floatValue.ToString() : "sin campo")})", ref total, ref passed);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // =====================================================================
        // 3. Campos del componente
        // =====================================================================
        private static void VerifyFields(ref int total, ref int passed)
        {
            System.Type t = typeof(VRSideMirrorControlPanel);
            foreach (string name in new[] { "m_BtnUp", "m_BtnDown", "m_BtnLeft", "m_BtnRight" })
            {
                Assert(t.GetField(name, INSTANCE_ANY) == null, $"VRSideMirrorControlPanel ya no tiene el campo {name}", ref total, ref passed);
            }
            Assert(t.GetField("m_LeftJoystickAction", INSTANCE_ANY) != null, "VRSideMirrorControlPanel tiene m_LeftJoystickAction", ref total, ref passed);
            Assert(t.GetField("m_JoystickDeadzone", INSTANCE_ANY) != null, "VRSideMirrorControlPanel tiene m_JoystickDeadzone", ref total, ref passed);

            // Acción por defecto: bindings sobre el mando izquierdo
            var go = new GameObject("SideMirrorJoystick_Fields");
            try
            {
                var panel = go.AddComponent<VRSideMirrorControlPanel>();
                t.GetMethod("SetupDefaultActionsIfNeeded", INSTANCE_ANY).Invoke(panel, null);
                var action = (InputAction)t.GetField("m_DefaultJoystickAction", INSTANCE_ANY).GetValue(panel);
                Assert(action != null && action.bindings.Count == 2
                       && action.bindings[0].path == "<XRController>{LeftHand}/primary2DAxis"
                       && action.bindings[1].path == "<XRController>{LeftHand}/thumbstick"
                       && action.expectedControlType == "Vector2",
                    "Acción propia del joystick: Vector2 con bindings primary2DAxis y thumbstick del mando izquierdo", ref total, ref passed);
                action?.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        // 4. CanUseJoystick
        // =====================================================================
        private static void VerifyCanUseJoystick(ref int total, ref int passed)
        {
            var root = new GameObject("SideMirrorJoystick_Root");
            GameObject menuObj = null;
            try
            {
                var panel = root.AddComponent<VRSideMirrorControlPanel>();
                XRSimpleInteractable selLeft = CreateSelector(root.transform, "Btn_Select_Left");
                XRSimpleInteractable selOff = CreateSelector(root.transform, "Btn_Select_Off");
                XRSimpleInteractable selRight = CreateSelector(root.transform, "Btn_Select_Right");
                var so = new SerializedObject(panel);
                so.FindProperty("m_BtnSelectLeft").objectReferenceValue = selLeft;
                so.FindProperty("m_BtnSelectOff").objectReferenceValue = selOff;
                so.FindProperty("m_BtnSelectRight").objectReferenceValue = selRight;
                so.ApplyModifiedPropertiesWithoutUndo();

                var lockManager = root.AddComponent<CockpitControlLockManager>();
                var soLock = new SerializedObject(lockManager);
                soLock.FindProperty("m_SideMirrorPanel").objectReferenceValue = panel;
                soLock.ApplyModifiedPropertiesWithoutUndo();

                // Menú in-game temporal (cerrado). Se inyecta en el caché del panel para que el resultado no dependa
                // de un InGameMenuController que ya esté en la escena abierta (en Edit Mode su menú puede estar visible).
                menuObj = new GameObject("SideMirrorJoystick_Menu");
                var menu = menuObj.AddComponent<InGameMenuController>();
                var menuRoot = new GameObject("MenuRoot");
                menuRoot.transform.SetParent(menuObj.transform, false);
                var soMenu = new SerializedObject(menu);
                soMenu.FindProperty("m_MenuRoot").objectReferenceValue = menuRoot;
                soMenu.ApplyModifiedPropertiesWithoutUndo();
                menuRoot.SetActive(false);
                typeof(VRSideMirrorControlPanel).GetField("m_MenuController", INSTANCE_ANY).SetValue(panel, menu);

                Assert(panel.CurrentSelection == VRSideMirrorControlPanel.MirrorSelection.Off, "Panel nuevo arranca con el selector en Off", ref total, ref passed);
                Assert(!panel.CanUseJoystick(), "CanUseJoystick = false con el selector en Off", ref total, ref passed);

                panel.SetSelection(VRSideMirrorControlPanel.MirrorSelection.Left);
                Assert(panel.CanUseJoystick(), "CanUseJoystick = true con selector Left sin bloqueo ni menú", ref total, ref passed);
                panel.SetSelection(VRSideMirrorControlPanel.MirrorSelection.Right);
                Assert(panel.CanUseJoystick(), "CanUseJoystick = true con selector Right sin bloqueo ni menú", ref total, ref passed);
                Assert(!panel.IsLockedByLesson, "IsLockedByLesson = false sin bloqueo", ref total, ref passed);

                lockManager.SetMirrorsLock(true);
                Assert(panel.IsLockedByLesson, "IsLockedByLesson = true tras SetMirrorsLock(true)", ref total, ref passed);
                Assert(!panel.CanUseJoystick(), "CanUseJoystick = false con los espejos bloqueados por la lección", ref total, ref passed);

                lockManager.SetMirrorsLock(false);
                Assert(!panel.IsLockedByLesson && panel.CanUseJoystick(), "Al desbloquear, CanUseJoystick vuelve a true", ref total, ref passed);

                // Menú in-game abierto
                menuRoot.SetActive(false);
                Assert(!menu.IsOpen && panel.CanUseJoystick(), "Con el menú in-game cerrado, CanUseJoystick = true", ref total, ref passed);
                menuRoot.SetActive(true);
                Assert(menu.IsOpen && !panel.CanUseJoystick(), "Con el menú in-game abierto, CanUseJoystick = false", ref total, ref passed);
                menuRoot.SetActive(false);
                Assert(panel.CanUseJoystick(), "Al cerrar el menú, CanUseJoystick vuelve a true", ref total, ref passed);
            }
            finally
            {
                if (menuObj != null) Object.DestroyImmediate(menuObj);
                Object.DestroyImmediate(root);
            }
        }

        private static XRSimpleInteractable CreateSelector(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<BoxCollider>();
            return go.AddComponent<XRSimpleInteractable>();
        }

        // =====================================================================
        // 5. Texto de la lección
        // =====================================================================
        private static void VerifyLessonText(ref int total, ref int passed)
        {
            var step = AssetDatabase.LoadAssetAtPath<ScriptableObject>(STEP04_PATH);
            Assert(step != null, "Lesson01_Step04_LeftMirror.asset existe", ref total, ref passed);
            if (step == null) return;

            string text = new SerializedObject(step).FindProperty("instructionText").stringValue;
            Assert(text.Contains("joystick del mando izquierdo"), "Paso 4: el texto menciona 'joystick del mando izquierdo'", ref total, ref passed);
            Assert(!text.Contains("Stick derecho"), "Paso 4: el texto ya no menciona 'Stick derecho'", ref total, ref passed);
        }

        private static void Assert(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  ✅ [PASS] {message}");
            }
            else
            {
                Debug.LogError($"  ❌ [FAIL] {message}");
            }
        }
    }
}
