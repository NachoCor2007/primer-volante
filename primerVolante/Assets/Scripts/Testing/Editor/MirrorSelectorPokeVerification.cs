using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using PrimerVolante.Lessons;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de los botones del selector de espejos laterales (Btn_Select_Left/Off/Right) de Car09,
    /// que pasan de accionarse con el grip a accionarse solo con el dedo (poke), igual que StartButton y HazardButton:
    /// 1. Estructura del prefab: raíz con collider + XRSimpleInteractable (solo capa Poke) + XRPokeFilter +
    ///    XRPokeFollowAffordance; hijo Visual con el mesh y la etiqueta.
    /// 2. Referencias del VRSideMirrorControlPanel (botones de la raíz, indicadores en Visual).
    /// 3. Capas: el NearFarInteractor (Default) no puede seleccionarlos y el XRPokeInteractor (Default|Poke) sí.
    /// 4. El bloqueo por lección sigue funcionando con la capa Poke.
    /// 5. VehicleMirrorSetupEditor.CreateButton genera la misma estructura.
    /// El hundido real y la selección con un poke simulado se prueban en Play Mode (ver prompt de la feature).
    /// </summary>
    public static class MirrorSelectorPokeVerification
    {
        private const string CAR09_PREFAB_PATH =
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";
        private static readonly string[] SELECTORS = { "Btn_Select_Left", "Btn_Select_Off", "Btn_Select_Right" };
        private static readonly float[] REST_X = { -0.022f, 0f, 0.022f };
        private static readonly string[] BTN_FIELDS = { "m_BtnSelectLeft", "m_BtnSelectOff", "m_BtnSelectRight" };
        private static readonly string[] IND_FIELDS = { "m_IndicatorLeft", "m_IndicatorOff", "m_IndicatorRight" };
        private const BindingFlags STATIC_ANY = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Tools/Primer Volante/Run Mirror Selector Poke Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL SELECTOR DE ESPEJOS CON POKE");
            Debug.Log("=========================================================");

            int totalTests = 0;
            int passedTests = 0;

            VerifyPrefabStructure(ref totalTests, ref passedTests);
            VerifyPanelReferences(ref totalTests, ref passedTests);
            VerifyLayerBehavior(ref totalTests, ref passedTests);
            VerifyLessonLock(ref totalTests, ref passedTests);
            VerifyCreateButton(ref totalTests, ref passedTests);

            Debug.Log("=========================================================");
            if (passedTests == totalTests)
            {
                Debug.Log($"🎉 VERIFICACIÓN DEL SELECTOR DE ESPEJOS CON POKE EXITOSA: {passedTests}/{totalTests} pruebas pasadas.");
            }
            else
            {
                Debug.LogError($"❌ [FAIL] VERIFICACIÓN DEL SELECTOR DE ESPEJOS CON POKE CON FALLOS: {passedTests}/{totalTests} pruebas pasadas.");
            }
            Debug.Log("=========================================================");
        }

        // =====================================================================
        // 1. Estructura del prefab
        // =====================================================================
        private static void VerifyPrefabStructure(ref int total, ref int passed)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CAR09_PREFAB_PATH);
            try
            {
                var panel = root.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                Assert(panel != null, "Car09.prefab tiene VRSideMirrorControlPanel", ref total, ref passed);
                if (panel == null) return;

                // Máscara de referencia: la de las balizas (solo Poke)
                Transform hazard = root.transform.Find("HazardButton");
                Assert(hazard != null, "Car09.prefab tiene HazardButton (referencia de capa)", ref total, ref passed);
                if (hazard == null) return;
                int hazardMask = hazard.GetComponent<XRSimpleInteractable>().interactionLayers.value;
                int pokeMask = InteractionLayerMask.GetMask("Poke");
                Assert(hazardMask == pokeMask && pokeMask != 0, $"HazardButton usa solo la capa Poke (máscara {hazardMask})", ref total, ref passed);

                for (int i = 0; i < SELECTORS.Length; i++)
                {
                    string name = SELECTORS[i];
                    Transform btn = panel.transform.Find(name);
                    Assert(btn != null, $"{name}: existe bajo SideMirror_ControlPanel", ref total, ref passed);
                    if (btn == null) continue;

                    // Interactable
                    var interactable = btn.GetComponent<XRSimpleInteractable>();
                    Assert(interactable != null, $"{name}: raíz con XRSimpleInteractable", ref total, ref passed);
                    if (interactable == null) continue;
                    int mask = interactable.interactionLayers.value;
                    Assert(mask == hazardMask, $"{name}: interactionLayers == máscara de HazardButton (actual {mask})", ref total, ref passed);
                    Assert((mask & 1) == 0, $"{name}: sin el bit Default (el grip no lo selecciona)", ref total, ref passed);

                    // Filtro
                    var filter = btn.GetComponent<XRPokeFilter>();
                    Assert(filter != null, $"{name}: raíz con XRPokeFilter", ref total, ref passed);
                    var collider = btn.GetComponent<BoxCollider>();
                    Assert(collider != null, $"{name}: raíz con BoxCollider", ref total, ref passed);
                    if (filter != null)
                    {
                        var so = new SerializedObject(filter);
                        Assert(so.FindProperty("m_Interactable").objectReferenceValue == interactable,
                            $"{name}: XRPokeFilter.m_Interactable es el interactable propio", ref total, ref passed);
                        Assert(so.FindProperty("m_PokeCollider").objectReferenceValue == collider && collider != null,
                            $"{name}: XRPokeFilter.m_PokeCollider es el collider propio", ref total, ref passed);
                        Assert(so.FindProperty("m_PokeConfiguration.m_UseConstant").boolValue,
                            $"{name}: configuración de poke constante", ref total, ref passed);
                        // NegativeY: el dedo entra por la cara +Y (la de la etiqueta) y se mueve hacia -Y
                        Assert(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeDirection").enumValueIndex == (int)PokeAxis.NegativeY,
                            $"{name}: dirección de poke NegativeY (dedo entrando por la cara +Y)", ref total, ref passed);
                        Assert(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_EnablePokeAngleThreshold").boolValue
                               && Mathf.Approximately(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeAngleThreshold").floatValue, 45f),
                            $"{name}: umbral de ángulo habilitado en 45", ref total, ref passed);
                    }

                    // Transform y collider
                    Assert(btn.localScale == Vector3.one, $"{name}: raíz con escala (1,1,1) (actual {btn.localScale})", ref total, ref passed);
                    Assert(btn.GetComponent<MeshRenderer>() == null && btn.GetComponent<MeshFilter>() == null,
                        $"{name}: la raíz ya no lleva MeshFilter/MeshRenderer", ref total, ref passed);
                    Assert(collider != null && Approx(collider.size, new Vector3(0.018f, 0.010f, 0.018f)),
                        $"{name}: BoxCollider de size (0.018, 0.010, 0.018) (actual {(collider != null ? collider.size.ToString() : "sin collider")})", ref total, ref passed);

                    // Visual y etiqueta
                    Transform visual = btn.Find("Visual");
                    Assert(visual != null, $"{name}: hijo Visual", ref total, ref passed);
                    if (visual == null) continue;
                    Assert(visual.GetComponent<MeshFilter>() != null && visual.GetComponent<MeshRenderer>() != null
                           && visual.GetComponent<MeshFilter>().sharedMesh != null && visual.GetComponent<MeshRenderer>().sharedMaterial != null,
                        $"{name}: Visual con MeshFilter (mesh) y MeshRenderer (material)", ref total, ref passed);
                    Assert(Approx(visual.localScale, new Vector3(0.018f, 0.010f, 0.018f)),
                        $"{name}: Visual con escala (0.018, 0.010, 0.018) (actual {visual.localScale})", ref total, ref passed);
                    Transform label = visual.Find("Label");
                    Assert(label != null && label.GetComponent<TextMeshPro>() != null,
                        $"{name}: Label (TextMeshPro) es hijo de Visual", ref total, ref passed);
                    Assert(btn.Find("Label") == null, $"{name}: Label ya no es hijo directo de la raíz", ref total, ref passed);

                    // Affordance
                    var follow = btn.GetComponent<XRPokeFollowAffordance>();
                    Assert(follow != null, $"{name}: raíz con XRPokeFollowAffordance", ref total, ref passed);
                    if (follow != null)
                    {
                        Assert(follow.pokeFollowTransform == visual, $"{name}: affordance sigue a Visual", ref total, ref passed);
                        Assert(follow.clampToMaxDistance && follow.maxDistance > 0f,
                            $"{name}: clamp activo con maxDistance > 0 (actual {follow.maxDistance})", ref total, ref passed);
                        Assert(Mathf.Approximately(follow.smoothingSpeed, 16f) && follow.returnToInitialPosition && follow.applyIfChildIsTarget,
                            $"{name}: smoothing 16, vuelve a la posición inicial, aplica si el target es hijo", ref total, ref passed);
                        float travel = follow.maxDistance * btn.lossyScale.y;
                        Assert(Mathf.Abs(travel - 0.003f) < 1e-5f, $"{name}: recorrido ≈ 3 mm en mundo (actual {travel * 1000f:F2} mm)", ref total, ref passed);

                        // El pivote de la raíz es la posición hundida; el Visual (y el collider) reposan maxDistance por encima.
                        Vector3 rest = Vector3.up * follow.maxDistance;
                        Assert(Approx(visual.localPosition, rest), $"{name}: Visual en reposo a +maxDistance sobre +Y (actual {visual.localPosition})", ref total, ref passed);
                        Assert(collider != null && Approx(collider.center, rest), $"{name}: BoxCollider centrado sobre el Visual en reposo", ref total, ref passed);
                        // La posición de reposo del Visual en el panel no cambió respecto del botón original
                        Vector3 restInPanel = btn.localPosition + visual.localPosition;
                        Assert(Approx(restInPanel, new Vector3(REST_X[i], 0.011f, 0.035f)),
                            $"{name}: el Visual reposa en la misma posición del panel que el botón original (actual {restInPanel})", ref total, ref passed);
                    }

                    // Háptica conservada
                    var haptic = btn.GetComponent<CabinHapticTarget>();
                    Assert(haptic != null && haptic.Category == CabinHapticCategory.ButtonsAndKnobs && !haptic.PlayReach,
                        $"{name}: CabinHapticTarget conservado (ButtonsAndKnobs, sin PlayReach)", ref total, ref passed);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // =====================================================================
        // 2. Referencias del panel
        // =====================================================================
        private static void VerifyPanelReferences(ref int total, ref int passed)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CAR09_PREFAB_PATH);
            try
            {
                var panel = root.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                if (panel == null)
                {
                    Assert(false, "Car09.prefab tiene VRSideMirrorControlPanel", ref total, ref passed);
                    return;
                }
                var so = new SerializedObject(panel);
                for (int i = 0; i < SELECTORS.Length; i++)
                {
                    Transform btn = panel.transform.Find(SELECTORS[i]);
                    Object btnRef = so.FindProperty(BTN_FIELDS[i]).objectReferenceValue;
                    Assert(btn != null && btnRef == btn.GetComponent<XRSimpleInteractable>(),
                        $"Panel: {BTN_FIELDS[i]} -> XRSimpleInteractable de la raíz {SELECTORS[i]}", ref total, ref passed);

                    Object indRef = so.FindProperty(IND_FIELDS[i]).objectReferenceValue;
                    Transform visual = btn != null ? btn.Find("Visual") : null;
                    Assert(visual != null && indRef == visual.GetComponent<MeshRenderer>(),
                        $"Panel: {IND_FIELDS[i]} -> MeshRenderer de {SELECTORS[i]}/Visual", ref total, ref passed);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // =====================================================================
        // 3. Capas: grip (NearFar, Default) vs dedo (XRPokeInteractor, Default|Poke)
        // =====================================================================
        private static void VerifyLayerBehavior(ref int total, ref int passed)
        {
            GameObject managerObj = null, nearFarObj = null, pokeObj = null;
            GameObject car = InstantiateCar09();
            try
            {
                managerObj = new GameObject("MirrorSelectorPoke_Manager");
                var manager = managerObj.AddComponent<XRInteractionManager>();

                nearFarObj = new GameObject("MirrorSelectorPoke_NearFar");
                var nearFar = nearFarObj.AddComponent<NearFarInteractor>();
                nearFar.interactionLayers = InteractionLayerMask.GetMask("Default");

                pokeObj = new GameObject("MirrorSelectorPoke_Poke");
                var poke = pokeObj.AddComponent<XRPokeInteractor>();
                poke.interactionLayers = InteractionLayerMask.GetMask("Default", "Poke");

                var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                foreach (string name in SELECTORS)
                {
                    var interactable = panel.transform.Find(name).GetComponent<XRSimpleInteractable>();
                    Assert(!manager.IsSelectPossible(nearFar, interactable),
                        $"{name}: el NearFarInteractor (capa Default, grip) NO puede seleccionarlo", ref total, ref passed);
                    Assert(manager.IsSelectPossible(poke, interactable),
                        $"{name}: el XRPokeInteractor (capa Poke) pasa el filtro de capa", ref total, ref passed);
                }
            }
            finally
            {
                if (car != null) Object.DestroyImmediate(car);
                if (pokeObj != null) Object.DestroyImmediate(pokeObj);
                if (nearFarObj != null) Object.DestroyImmediate(nearFarObj);
                if (managerObj != null) Object.DestroyImmediate(managerObj);
            }
        }

        // =====================================================================
        // 4. Bloqueo por lección con la capa Poke
        // =====================================================================
        private static void VerifyLessonLock(ref int total, ref int passed)
        {
            GameObject car = InstantiateCar09();
            try
            {
                var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                var lockManager = car.AddComponent<CockpitControlLockManager>();
                var soLock = new SerializedObject(lockManager);
                soLock.FindProperty("m_SideMirrorPanel").objectReferenceValue = panel;
                soLock.ApplyModifiedPropertiesWithoutUndo();

                int poke = InteractionLayerMask.GetMask("Poke");
                var selectors = new XRSimpleInteractable[SELECTORS.Length];
                for (int i = 0; i < SELECTORS.Length; i++) selectors[i] = panel.transform.Find(SELECTORS[i]).GetComponent<XRSimpleInteractable>();

                Assert(!panel.IsLockedByLesson, "Sin bloqueo, IsLockedByLesson = false", ref total, ref passed);

                lockManager.SetMirrorsLock(true);
                bool allZero = true;
                foreach (var s in selectors) allZero &= s.interactionLayers.value == 0;
                Assert(allZero, "SetMirrorsLock(true) deja la máscara de los 3 selectores en 0", ref total, ref passed);
                Assert(panel.IsLockedByLesson, "SetMirrorsLock(true) -> IsLockedByLesson = true", ref total, ref passed);

                lockManager.SetMirrorsLock(false);
                bool allPoke = true;
                foreach (var s in selectors) allPoke &= s.interactionLayers.value == poke;
                Assert(allPoke, "SetMirrorsLock(false) restaura la máscara de los 3 selectores a Poke", ref total, ref passed);
                Assert(!panel.IsLockedByLesson, "SetMirrorsLock(false) -> IsLockedByLesson = false", ref total, ref passed);
            }
            finally
            {
                if (car != null) Object.DestroyImmediate(car);
            }
        }

        // =====================================================================
        // 5. VehicleMirrorSetupEditor.CreateButton genera la misma estructura
        // =====================================================================
        private static void VerifyCreateButton(ref int total, ref int passed)
        {
            MethodInfo create = typeof(VehicleMirrorSetupEditor).GetMethod("CreateButton", STATIC_ANY);
            Assert(create != null, "VehicleMirrorSetupEditor.CreateButton existe", ref total, ref passed);
            if (create == null) return;

            var parent = new GameObject("MirrorSelectorPoke_Panel");
            try
            {
                Vector3 size = new Vector3(0.018f, 0.010f, 0.018f);
                Vector3 restPos = new Vector3(-0.022f, 0.011f, 0.035f);
                object result = create.Invoke(null, new object[] { parent.transform, "Btn_Select_Test", restPos, size, null, "L", null, 0.25f });
                var interactable = (XRSimpleInteractable)result.GetType().GetField("Item1").GetValue(result);
                var renderer = (Renderer)result.GetType().GetField("Item2").GetValue(result);

                Transform btn = parent.transform.Find("Btn_Select_Test");
                Assert(btn != null && interactable != null && interactable.transform == btn, "CreateButton: devuelve el interactable de la raíz", ref total, ref passed);
                if (btn == null || interactable == null) return;

                Transform visual = btn.Find("Visual");
                Assert(visual != null && renderer != null && renderer.transform == visual,
                    "CreateButton: devuelve el renderer del hijo Visual", ref total, ref passed);
                Assert(btn.localScale == Vector3.one && btn.GetComponent<Renderer>() == null,
                    "CreateButton: raíz con escala 1 y sin renderer", ref total, ref passed);
                var collider = btn.GetComponent<BoxCollider>();
                Assert(collider != null && Approx(collider.size, size), "CreateButton: BoxCollider de size (0.018, 0.010, 0.018)", ref total, ref passed);
                Assert(interactable.interactionLayers.value == InteractionLayerMask.GetMask("Poke"),
                    "CreateButton: interactionLayers = solo Poke", ref total, ref passed);

                var filter = btn.GetComponent<XRPokeFilter>();
                Assert(filter != null, "CreateButton: XRPokeFilter en la raíz", ref total, ref passed);
                if (filter != null)
                {
                    var so = new SerializedObject(filter);
                    Assert(so.FindProperty("m_Interactable").objectReferenceValue == interactable
                           && so.FindProperty("m_PokeCollider").objectReferenceValue == collider
                           && so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeDirection").enumValueIndex == (int)PokeAxis.NegativeY
                           && so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_EnablePokeAngleThreshold").boolValue
                           && Mathf.Approximately(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeAngleThreshold").floatValue, 45f),
                        "CreateButton: XRPokeFilter con interactable y collider propios, NegativeY y umbral 45", ref total, ref passed);
                }

                var follow = btn.GetComponent<XRPokeFollowAffordance>();
                Assert(follow != null && follow.pokeFollowTransform == visual && follow.clampToMaxDistance
                       && Mathf.Abs(follow.maxDistance * btn.lossyScale.y - VehicleMirrorSetupEditor.SELECTOR_PRESS_TRAVEL) < 1e-6f,
                    "CreateButton: XRPokeFollowAffordance sobre Visual con el recorrido de 3 mm", ref total, ref passed);

                Assert(visual != null && follow != null && Approx(visual.localPosition, Vector3.up * follow.maxDistance)
                       && Approx(btn.localPosition + visual.localPosition, restPos),
                    "CreateButton: el Visual reposa en la posición pedida y la raíz queda 3 mm por debajo", ref total, ref passed);
                Assert(visual != null && visual.Find("Label") != null && visual.Find("Label").GetComponent<TextMeshPro>() != null,
                    "CreateButton: Label (TextMeshPro) bajo Visual", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        // =====================================================================
        // Utilidades
        // =====================================================================
        private static GameObject InstantiateCar09()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CAR09_PREFAB_PATH);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        private static bool Approx(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;

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
