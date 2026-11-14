using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using PrimerVolante.Lessons;
using PrimerVolante.VR;
using Object = UnityEngine.Object;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de los botones del selector de espejos laterales (Btn_Select_Left/Off/Right) de Car09,
    /// que pasan de accionarse con el grip a accionarse solo con el dedo (poke), igual que StartButton y HazardButton:
    /// 1. Estructura del prefab: raíz con collider + XRSimpleInteractable (solo capa Poke) + XRPokeFilter +
    ///    XRPokeFollowAffordance; hijo Visual con el mesh y la etiqueta; sin overrides en una instancia limpia.
    /// 2. Referencias del VRSideMirrorControlPanel (botones de la raíz, indicadores en Visual).
    /// 3. Lógica real del XRPokeFilter (dirección, profundidad): entra por +Y sí, por debajo o lateral no.
    /// 4. Capas: el NearFarInteractor (Default) no puede seleccionarlos y el XRPokeInteractor (Default|Poke) sí.
    /// 5. El bloqueo por lección sigue funcionando con la capa Poke.
    /// 6. VehicleMirrorSetupEditor.CreateButton genera la misma estructura y rechaza escalas distintas de 1.
    /// Todo se instancia en una escena aditiva temporal que se cierra al terminar. El hundido real y la selección con
    /// un poke simulado se prueban en Play Mode (ver prompt de la feature).
    /// </summary>
    public static class MirrorSelectorPokeVerification
    {
        private const string CAR09_PREFAB_PATH =
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";
        private static readonly string[] SELECTORS = { "Btn_Select_Left", "Btn_Select_Off", "Btn_Select_Right" };
        private static readonly float[] REST_X = { -VehicleMirrorSetupEditor.SELECTOR_SPACING_X, 0f, VehicleMirrorSetupEditor.SELECTOR_SPACING_X };
        private static readonly string[] BTN_FIELDS = { "m_BtnSelectLeft", "m_BtnSelectOff", "m_BtnSelectRight" };
        private static readonly string[] IND_FIELDS = { "m_IndicatorLeft", "m_IndicatorOff", "m_IndicatorRight" };
        private const BindingFlags INSTANCE_ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static int s_Total;
        private static int s_Passed;

        [MenuItem("Tools/Primer Volante/Run Mirror Selector Poke Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL SELECTOR DE ESPEJOS CON POKE");
            Debug.Log("=========================================================");

            s_Total = 0;
            s_Passed = 0;

            bool dirtyBefore = AnySceneDirty();
            Scene original = SceneManager.GetActiveScene();
            Scene tmp = default;
            GameObject car = null;
            try
            {
                tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (original.IsValid()) SceneManager.SetActiveScene(original);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CAR09_PREFAB_PATH);
                Assert(prefab != null, "Car09.prefab existe");
                if (prefab != null)
                {
                    // Una sola instancia compartida por todas las comprobaciones
                    car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tmp);
                    VerifyPrefabStructure(car);
                    VerifyPanelReferences(car);
                    VerifyFilterLogic(car);
                    VerifyLayerBehavior(car, tmp);
                    VerifyLessonLock(car);
                }
                VerifyCreateButton(tmp);
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
            {
                Debug.Log($"🎉 VERIFICACIÓN DEL SELECTOR DE ESPEJOS CON POKE EXITOSA: {s_Passed}/{s_Total} pruebas pasadas.");
            }
            else
            {
                Debug.LogError($"❌ [FAIL] VERIFICACIÓN DEL SELECTOR DE ESPEJOS CON POKE CON FALLOS: {s_Passed}/{s_Total} pruebas pasadas.");
            }
            Debug.Log("=========================================================");
        }

        /// <summary>Solo las comprobaciones de la lógica del filtro sobre una instancia dada (para probar que fallan con datos erróneos).</summary>
        internal static (int total, int passed) RunFilterLogicOnly(GameObject car)
        {
            s_Total = 0;
            s_Passed = 0;
            VerifyFilterLogic(car);
            return (s_Total, s_Passed);
        }

        // =====================================================================
        // 1. Estructura del prefab
        // =====================================================================
        private static void VerifyPrefabStructure(GameObject car)
        {
            var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
            Assert(panel != null, "Car09.prefab tiene VRSideMirrorControlPanel");
            if (panel == null) return;

            // Máscara de referencia: la de las balizas (solo Poke)
            Transform hazard = car.transform.Find("HazardButton");
            Assert(hazard != null, "Car09.prefab tiene HazardButton (referencia de capa)");
            if (hazard == null) return;
            int hazardMask = hazard.GetComponent<XRSimpleInteractable>().interactionLayers.value;
            int pokeMask = InteractionLayerMask.GetMask("Poke");
            Assert(hazardMask == pokeMask && pokeMask != 0, $"HazardButton usa solo la capa Poke (máscara {hazardMask})");

            // Una instancia limpia no debe traer overrides en los botones: si OnValidate de la affordance moviera el Visual
            // (escala != 1), quedaría guardado como override.
            int overrides = 0;
            foreach (var o in PrefabUtility.GetObjectOverrides(car, false))
            {
                if (o.instanceObject is Component c && c != null && IsUnderSelector(c.transform, panel.transform)) overrides++;
            }
            Assert(overrides == 0, $"Los botones del selector no tienen overrides en una instancia limpia (encontrados {overrides})");

            for (int i = 0; i < SELECTORS.Length; i++)
            {
                string name = SELECTORS[i];
                Transform btn = panel.transform.Find(name);
                Assert(btn != null, $"{name}: existe bajo SideMirror_ControlPanel");
                if (btn == null) continue;

                // Interactable
                var interactable = btn.GetComponent<XRSimpleInteractable>();
                Assert(interactable != null, $"{name}: raíz con XRSimpleInteractable");
                if (interactable == null) continue;
                int mask = interactable.interactionLayers.value;
                Assert(mask == hazardMask, $"{name}: interactionLayers == máscara de HazardButton (actual {mask})");
                Assert((mask & 1) == 0, $"{name}: sin el bit Default (el grip no lo selecciona)");

                // Filtro
                var filter = btn.GetComponent<XRPokeFilter>();
                Assert(filter != null && filter.enabled, $"{name}: raíz con XRPokeFilter habilitado");
                var collider = btn.GetComponent<BoxCollider>();
                Assert(collider != null, $"{name}: raíz con BoxCollider");
                if (filter != null)
                {
                    var so = new SerializedObject(filter);
                    Assert(so.FindProperty("m_Interactable").objectReferenceValue == interactable,
                        $"{name}: XRPokeFilter.m_Interactable es el interactable propio");
                    Assert(so.FindProperty("m_PokeCollider").objectReferenceValue == collider && collider != null,
                        $"{name}: XRPokeFilter.m_PokeCollider es el collider propio");
                    Assert(so.FindProperty("m_PokeConfiguration.m_UseConstant").boolValue,
                        $"{name}: configuración de poke constante");
                    // NegativeY: el dedo entra por la cara +Y (la de la etiqueta) y se mueve hacia -Y
                    Assert(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeDirection").enumValueIndex == (int)PokeAxis.NegativeY,
                        $"{name}: dirección de poke NegativeY (dedo entrando por la cara +Y)");
                    Assert(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_EnablePokeAngleThreshold").boolValue
                           && Mathf.Approximately(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeAngleThreshold").floatValue, 45f),
                        $"{name}: umbral de ángulo habilitado en 45");
                }

                // Transform y collider
                Assert(btn.localScale == Vector3.one, $"{name}: raíz con escala (1,1,1) (actual {btn.localScale})");
                Assert(btn.GetComponent<MeshRenderer>() == null && btn.GetComponent<MeshFilter>() == null,
                    $"{name}: la raíz ya no lleva MeshFilter/MeshRenderer");
                Assert(collider != null && Approx(collider.size, VehicleMirrorSetupEditor.SELECTOR_SIZE),
                    $"{name}: BoxCollider de size {VehicleMirrorSetupEditor.SELECTOR_SIZE} (actual {(collider != null ? collider.size.ToString() : "sin collider")})");

                // Visual y etiqueta
                Transform visual = btn.Find("Visual");
                Assert(visual != null, $"{name}: hijo Visual");
                if (visual == null) continue;
                Assert(visual.GetComponent<MeshFilter>() != null && visual.GetComponent<MeshRenderer>() != null
                       && visual.GetComponent<MeshFilter>().sharedMesh != null && visual.GetComponent<MeshRenderer>().sharedMaterial != null,
                    $"{name}: Visual con MeshFilter (mesh) y MeshRenderer (material)");
                Assert(Approx(visual.localScale, VehicleMirrorSetupEditor.SELECTOR_SIZE),
                    $"{name}: Visual con escala {VehicleMirrorSetupEditor.SELECTOR_SIZE} (actual {visual.localScale})");
                Transform label = visual.Find("Label");
                Assert(label != null && label.GetComponent<TextMeshPro>() != null,
                    $"{name}: Label (TextMeshPro) es hijo de Visual");
                Assert(btn.Find("Label") == null, $"{name}: Label ya no es hijo directo de la raíz");

                // Affordance
                var follow = btn.GetComponent<XRPokeFollowAffordance>();
                Assert(follow != null, $"{name}: raíz con XRPokeFollowAffordance");
                if (follow == null) continue;
                Assert(follow.pokeFollowTransform == visual, $"{name}: affordance sigue a Visual");
                Assert(follow.clampToMaxDistance && follow.maxDistance > 0f,
                    $"{name}: clamp activo con maxDistance > 0 (actual {follow.maxDistance})");
                Assert(Mathf.Approximately(follow.smoothingSpeed, 16f) && follow.returnToInitialPosition && follow.applyIfChildIsTarget,
                    $"{name}: smoothing 16, vuelve a la posición inicial, aplica si el target es hijo");
                var soFollow = new SerializedObject(follow);
                Assert(soFollow.FindProperty("m_PokeFilter").objectReferenceValue == filter && filter != null,
                    $"{name}: affordance con m_PokeFilter asignado explícitamente al filtro de la raíz");
                float travel = follow.maxDistance * btn.lossyScale.y;
                Assert(Mathf.Abs(travel - VehicleMirrorSetupEditor.SELECTOR_PRESS_TRAVEL) < 1e-6f,
                    $"{name}: recorrido ≈ 3 mm en mundo (actual {travel * 1000f:F2} mm)");

                // El pivote de la raíz es la posición hundida; el Visual (y el collider) reposan maxDistance por encima.
                Vector3 rest = Vector3.up * follow.maxDistance;
                Assert(Approx(visual.localPosition, rest), $"{name}: Visual en reposo a +maxDistance sobre +Y (actual {visual.localPosition})");
                Assert(collider != null && Approx(collider.center, rest), $"{name}: BoxCollider centrado sobre el Visual en reposo");
                // La posición de reposo del Visual en el panel no cambió respecto del botón original
                Vector3 expectedRest = new Vector3(REST_X[i], VehicleMirrorSetupEditor.SELECTOR_REST_Y, VehicleMirrorSetupEditor.SELECTOR_REST_Z);
                Vector3 restInPanel = btn.localPosition + visual.localPosition;
                Assert(Approx(restInPanel, expectedRest),
                    $"{name}: el Visual reposa en la misma posición del panel que el botón original (actual {restInPanel}, esperado {expectedRest})");

                // Háptica conservada
                var haptic = btn.GetComponent<CabinHapticTarget>();
                Assert(haptic != null && haptic.Category == CabinHapticCategory.ButtonsAndKnobs && !haptic.PlayReach,
                    $"{name}: CabinHapticTarget conservado (ButtonsAndKnobs, sin PlayReach)");
            }
        }

        private static bool IsUnderSelector(Transform t, Transform panel)
        {
            foreach (string s in SELECTORS)
            {
                Transform btn = panel.Find(s);
                if (btn != null && t.IsChildOf(btn)) return true;
            }
            return false;
        }

        // =====================================================================
        // 2. Referencias del panel
        // =====================================================================
        private static void VerifyPanelReferences(GameObject car)
        {
            var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
            if (panel == null)
            {
                Assert(false, "Car09.prefab tiene VRSideMirrorControlPanel (referencias)");
                return;
            }
            var so = new SerializedObject(panel);
            for (int i = 0; i < SELECTORS.Length; i++)
            {
                Transform btn = panel.transform.Find(SELECTORS[i]);
                Object btnRef = so.FindProperty(BTN_FIELDS[i]).objectReferenceValue;
                Assert(btn != null && btnRef == btn.GetComponent<XRSimpleInteractable>(),
                    $"Panel: {BTN_FIELDS[i]} -> XRSimpleInteractable de la raíz {SELECTORS[i]}");

                Object indRef = so.FindProperty(IND_FIELDS[i]).objectReferenceValue;
                Transform visual = btn != null ? btn.Find("Visual") : null;
                Assert(visual != null && indRef == visual.GetComponent<MeshRenderer>(),
                    $"Panel: {IND_FIELDS[i]} -> MeshRenderer de {SELECTORS[i]}/Visual");
            }
        }

        // =====================================================================
        // 3. Lógica real del XRPokeFilter con la dirección configurada
        // =====================================================================
        // En Edit Mode XRPokeFilter no se registra como select filter (eso pasa en Start, solo en Play), pero su lógica sí se
        // inicializa llamando a Setup() (la dispara el setter pokeCollider). Se evalúa XRPokeLogic con un "interactor" que no es
        // IAttachPointVelocityProvider, así que el umbral de ángulo por velocidad (45) no interviene: aquí se prueba la
        // dirección y la profundidad; el rechazo del ángulo se prueba en Play Mode con un poke real.
        private static void VerifyFilterLogic(GameObject car)
        {
            var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
            if (panel == null)
            {
                Assert(false, "Filtro: Car09 tiene VRSideMirrorControlPanel");
                return;
            }

            foreach (string name in SELECTORS)
            {
                Transform btn = panel.transform.Find(name);
                var filter = btn != null ? btn.GetComponent<XRPokeFilter>() : null;
                var interactable = btn != null ? btn.GetComponent<XRSimpleInteractable>() : null;
                var collider = btn != null ? btn.GetComponent<BoxCollider>() : null;
                Assert(filter != null && interactable != null && collider != null, $"{name}: tiene filtro, interactable y collider");
                if (filter == null || interactable == null || collider == null) continue;

                Assert(filter.isActiveAndEnabled && filter.pokeInteractable == interactable && filter.pokeCollider == collider,
                    $"{name}: filtro activo y referenciado al interactable y collider propios");

                filter.pokeCollider = collider; // dispara Setup(): inicializa XRPokeLogic con la configuración real
                FieldInfo logicField = typeof(XRPokeFilter).GetField("m_PokeLogic", INSTANCE_ANY);
                object logic = logicField?.GetValue(filter);
                MethodInfo meets = logic?.GetType().GetMethod("MeetsRequirementsForSelectAction");
                Assert(meets != null, $"{name}: se pudo acceder a la lógica de poke del filtro (XRPokeLogic)");
                if (meets == null) continue;

                Vector3 pivot = btn.position;
                bool Poke(Vector3 tip) => (bool)meets.Invoke(logic, new object[] { new object(), pivot, tip, 0f, btn });

                Assert(Poke(pivot + btn.up * 0.00005f),
                    $"{name}: un dedo que entra por la cara +Y y llega al pivote selecciona");
                Assert(!Poke(pivot + btn.up * 0.010f),
                    $"{name}: un dedo 10 mm por encima de la cara hundida todavía no selecciona");
                Assert(!Poke(pivot - btn.up * 0.00005f),
                    $"{name}: un dedo que llega desde abajo (-Y) no selecciona");
                Assert(!Poke(pivot + btn.right * 0.012f - btn.up * 0.0005f),
                    $"{name}: un dedo lateral (junto al botón, a la altura del pivote) no selecciona");
                Assert(!Poke(pivot + btn.forward * 0.012f - btn.up * 0.0005f),
                    $"{name}: un dedo que llega por el frente (+Z, junto al botón) no selecciona");
            }
        }

        // =====================================================================
        // 4. Capas: grip (NearFar, Default) vs dedo (XRPokeInteractor, Default|Poke)
        // =====================================================================
        private static void VerifyLayerBehavior(GameObject car, Scene scene)
        {
            GameObject managerObj = null, nearFarObj = null, pokeObj = null;
            try
            {
                managerObj = new GameObject("MirrorSelectorPoke_Manager");
                SceneManager.MoveGameObjectToScene(managerObj, scene);
                var manager = managerObj.AddComponent<XRInteractionManager>();

                nearFarObj = new GameObject("MirrorSelectorPoke_NearFar");
                SceneManager.MoveGameObjectToScene(nearFarObj, scene);
                var nearFar = nearFarObj.AddComponent<NearFarInteractor>();
                nearFar.interactionLayers = InteractionLayerMask.GetMask("Default");

                pokeObj = new GameObject("MirrorSelectorPoke_Poke");
                SceneManager.MoveGameObjectToScene(pokeObj, scene);
                var poke = pokeObj.AddComponent<XRPokeInteractor>();
                poke.interactionLayers = InteractionLayerMask.GetMask("Default", "Poke");

                var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
                Assert(panel != null, "Capas: Car09 tiene VRSideMirrorControlPanel");
                if (panel == null) return;
                foreach (string name in SELECTORS)
                {
                    Transform btn = panel.transform.Find(name);
                    var interactable = btn != null ? btn.GetComponent<XRSimpleInteractable>() : null;
                    Assert(interactable != null, $"Capas: {name} existe con XRSimpleInteractable");
                    if (interactable == null) continue;
                    Assert(!manager.IsSelectPossible(nearFar, interactable),
                        $"{name}: el NearFarInteractor (capa Default, grip) NO puede seleccionarlo");
                    Assert(manager.IsSelectPossible(poke, interactable),
                        $"{name}: el XRPokeInteractor (capa Poke) pasa el filtro de capa");
                }
            }
            finally
            {
                if (pokeObj != null) Object.DestroyImmediate(pokeObj);
                if (nearFarObj != null) Object.DestroyImmediate(nearFarObj);
                if (managerObj != null) Object.DestroyImmediate(managerObj);
            }
        }

        // =====================================================================
        // 5. Bloqueo por lección con la capa Poke
        // =====================================================================
        private static void VerifyLessonLock(GameObject car)
        {
            var panel = car.GetComponentInChildren<VRSideMirrorControlPanel>(true);
            Assert(panel != null, "Bloqueo: Car09 tiene VRSideMirrorControlPanel");
            if (panel == null) return;

            var selectors = new XRSimpleInteractable[SELECTORS.Length];
            for (int i = 0; i < SELECTORS.Length; i++)
            {
                Transform btn = panel.transform.Find(SELECTORS[i]);
                selectors[i] = btn != null ? btn.GetComponent<XRSimpleInteractable>() : null;
                Assert(selectors[i] != null, $"Bloqueo: {SELECTORS[i]} existe con XRSimpleInteractable");
            }
            if (selectors[0] == null || selectors[1] == null || selectors[2] == null) return;

            var lockManager = car.AddComponent<CockpitControlLockManager>();
            try
            {
                var soLock = new SerializedObject(lockManager);
                soLock.FindProperty("m_SideMirrorPanel").objectReferenceValue = panel;
                soLock.ApplyModifiedPropertiesWithoutUndo();

                int poke = InteractionLayerMask.GetMask("Poke");
                Assert(!panel.IsLockedByLesson, "Sin bloqueo, IsLockedByLesson = false");

                lockManager.SetMirrorsLock(true);
                bool allZero = true;
                foreach (var s in selectors) allZero &= s.interactionLayers.value == 0;
                Assert(allZero, "SetMirrorsLock(true) deja la máscara de los 3 selectores en 0");
                Assert(panel.IsLockedByLesson, "SetMirrorsLock(true) -> IsLockedByLesson = true");

                lockManager.SetMirrorsLock(false);
                bool allPoke = true;
                foreach (var s in selectors) allPoke &= s.interactionLayers.value == poke;
                Assert(allPoke, "SetMirrorsLock(false) restaura la máscara de los 3 selectores a Poke");
                Assert(!panel.IsLockedByLesson, "SetMirrorsLock(false) -> IsLockedByLesson = false");
            }
            finally
            {
                Object.DestroyImmediate(lockManager);
            }
        }

        // =====================================================================
        // 6. VehicleMirrorSetupEditor.CreateButton genera la misma estructura
        // =====================================================================
        private static void VerifyCreateButton(Scene scene)
        {
            var parent = new GameObject("MirrorSelectorPoke_Panel");
            var scaledParent = new GameObject("MirrorSelectorPoke_ScaledPanel");
            try
            {
                SceneManager.MoveGameObjectToScene(parent, scene);
                SceneManager.MoveGameObjectToScene(scaledParent, scene);

                Vector3 size = VehicleMirrorSetupEditor.SELECTOR_SIZE;
                Vector3 restPos = new Vector3(-VehicleMirrorSetupEditor.SELECTOR_SPACING_X, VehicleMirrorSetupEditor.SELECTOR_REST_Y, VehicleMirrorSetupEditor.SELECTOR_REST_Z);
                var (interactable, renderer) = VehicleMirrorSetupEditor.CreateButton(parent.transform, "Btn_Select_Test", restPos, size, null, "L", null);

                Transform btn = parent.transform.Find("Btn_Select_Test");
                Assert(btn != null && interactable != null && interactable.transform == btn, "CreateButton: devuelve el interactable de la raíz");
                if (btn == null || interactable == null) return;

                Transform visual = btn.Find("Visual");
                Assert(visual != null && renderer != null && renderer.transform == visual,
                    "CreateButton: devuelve el renderer del hijo Visual");
                Assert(btn.localScale == Vector3.one && btn.GetComponent<Renderer>() == null,
                    "CreateButton: raíz con escala 1 y sin renderer");
                var collider = btn.GetComponent<BoxCollider>();
                Assert(collider != null && Approx(collider.size, size), "CreateButton: BoxCollider de size (0.018, 0.010, 0.018)");
                Assert(interactable.interactionLayers.value == InteractionLayerMask.GetMask("Poke"),
                    "CreateButton: interactionLayers = solo Poke");

                var filter = btn.GetComponent<XRPokeFilter>();
                Assert(filter != null, "CreateButton: XRPokeFilter en la raíz");
                if (filter != null)
                {
                    var so = new SerializedObject(filter);
                    Assert(so.FindProperty("m_Interactable").objectReferenceValue == interactable
                           && so.FindProperty("m_PokeCollider").objectReferenceValue == collider
                           && so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeDirection").enumValueIndex == (int)PokeAxis.NegativeY
                           && so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_EnablePokeAngleThreshold").boolValue
                           && Mathf.Approximately(so.FindProperty("m_PokeConfiguration.m_ConstantValue.m_PokeAngleThreshold").floatValue, 45f),
                        "CreateButton: XRPokeFilter con interactable y collider propios, NegativeY y umbral 45");
                }

                var follow = btn.GetComponent<XRPokeFollowAffordance>();
                Assert(follow != null && follow.pokeFollowTransform == visual && follow.clampToMaxDistance
                       && Mathf.Abs(follow.maxDistance * btn.lossyScale.y - VehicleMirrorSetupEditor.SELECTOR_PRESS_TRAVEL) < 1e-6f,
                    "CreateButton: XRPokeFollowAffordance sobre Visual con el recorrido de 3 mm");
                Assert(follow != null && filter != null && new SerializedObject(follow).FindProperty("m_PokeFilter").objectReferenceValue == filter,
                    "CreateButton: affordance con m_PokeFilter asignado explícitamente");

                Assert(visual != null && follow != null && Approx(visual.localPosition, Vector3.up * follow.maxDistance)
                       && Approx(btn.localPosition + visual.localPosition, restPos),
                    "CreateButton: el Visual reposa en la posición pedida y la raíz queda 3 mm por debajo");
                Assert(visual != null && visual.Find("Label") != null && visual.Find("Label").GetComponent<TextMeshPro>() != null,
                    "CreateButton: Label (TextMeshPro) bajo Visual");

                // Validaciones: capa Poke inexistente (máscara 0) y escala distinta de 1
                Assert(!VehicleMirrorSetupEditor.TryValidateSelectorPoke(Vector3.one, 0, out string errNoLayer) && !string.IsNullOrEmpty(errNoLayer),
                    "Validación: sin capa Poke (máscara 0) se rechaza con un error");
                Assert(!VehicleMirrorSetupEditor.TryValidateSelectorPoke(Vector3.one * 2f, 2, out string errScale) && !string.IsNullOrEmpty(errScale),
                    "Validación: escala acumulada 2 se rechaza con un error");
                Assert(VehicleMirrorSetupEditor.TryValidateSelectorPoke(Vector3.one, InteractionLayerMask.GetMask("Poke"), out _),
                    "Validación: escala 1 y capa Poke existente se aceptan");

                scaledParent.transform.localScale = Vector3.one * 2f;
                bool threw = false;
                try
                {
                    VehicleMirrorSetupEditor.CreateButton(scaledParent.transform, "Btn_Select_Scaled", restPos, size, null, "L", null);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                Assert(threw && scaledParent.transform.childCount == 0,
                    "CreateButton con un padre escalado lanza InvalidOperationException sin crear nada");

                var manual = new GameObject("Btn_Manual");
                manual.transform.SetParent(scaledParent.transform, false);
                manual.AddComponent<BoxCollider>();
                var manualInteractable = manual.AddComponent<XRSimpleInteractable>();
                var manualVisual = new GameObject("Visual");
                manualVisual.transform.SetParent(manual.transform, false);
                int layersBefore = manualInteractable.interactionLayers.value;
                bool configured = VehicleMirrorSetupEditor.TryConfigureSelectorPoke(manual, manualVisual.transform, out string errConfigure);
                Assert(!configured && !string.IsNullOrEmpty(errConfigure) && manual.GetComponent<XRPokeFilter>() == null
                       && manual.GetComponent<XRPokeFollowAffordance>() == null && manualInteractable.interactionLayers.value == layersBefore,
                    "ConfigureSelectorPoke con escala != 1 devuelve false y no toca el botón");
            }
            finally
            {
                Object.DestroyImmediate(scaledParent);
                Object.DestroyImmediate(parent);
            }
        }

        // =====================================================================
        // Utilidades
        // =====================================================================
        private static bool AnySceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            }
            return false;
        }

        private static bool Approx(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;

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
