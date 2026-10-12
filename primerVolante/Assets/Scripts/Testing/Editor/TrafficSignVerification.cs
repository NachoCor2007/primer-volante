using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Suite de verificación en Editor del sistema de señalización vial reglamentaria
    /// argentina (R.14 velocidad máxima) y zonas de evaluación.
    /// </summary>
    public static class TrafficSignVerification
    {
        [MenuItem("Tools/Primer Volante/Run Traffic Sign Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DE SEÑALIZACIÓN VIAL (R.14)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;

            VerifyTypographyCalibration(ref total, ref passed);
            VerifyPureEvaluationLogic(ref total, ref passed);
            VerifyAssetsAndPrefabs(ref total, ref passed);
            VerifyNoDeprecatedAPIs(ref total, ref passed);
            VerifyTestScene(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"🏁 RESULTADO FINAL: {passed}/{total} pruebas pasadas con éxito!");
            Debug.Log("=========================================================");
        }

        // ---------------------------------------------------------------- Tipografía 2 vs 3 Dígitos
        private static void VerifyTypographyCalibration(ref int total, ref int passed)
        {
            Debug.Log("--- [1/5] Verificando Calibración Tipográfica (2 vs 3 Dígitos) ---");

            var go = new GameObject("TestSignDisplay");
            var textGo = new GameObject("Text_Display");
            textGo.transform.SetParent(go.transform);
            var tmp = textGo.AddComponent<TextMeshPro>();

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TrafficSignSetup.FONT_PATH);
            if (fontAsset != null) tmp.font = fontAsset;

            var display = go.AddComponent<SpeedLimitSignDisplay>();

            try
            {
                // Test 1: Configuración estándar de 2 dígitos (40 km/h)
                display.SetSpeedLimit(40);
                Assert(display.SpeedLimitKmh == 40, "Límite configurado a 40 km/h", ref total, ref passed);
                Assert(tmp.text == "40", "Texto del display es '40'", ref total, ref passed);
                Assert(tmp.alignment == TextAlignmentOptions.Center, "Alineación estrictamente centrada", ref total, ref passed);

                float size2Digits = display.CurrentFontSize;
                float spacing2Digits = display.CurrentCharacterSpacing;
                Assert(size2Digits > 2.5f, $"Tamaño de fuente para 2 dígitos es amplio ({size2Digits:F2})", ref total, ref passed);

                // Test 2: Configuración de 3 dígitos (110 km/h)
                display.SetSpeedLimit(110);
                Assert(display.SpeedLimitKmh == 110, "Límite configurado a 110 km/h", ref total, ref passed);
                Assert(tmp.text == "110", "Texto del display es '110'", ref total, ref passed);

                float size3Digits = display.CurrentFontSize;
                float spacing3Digits = display.CurrentCharacterSpacing;

                Assert(size3Digits < size2Digits,
                    $"Tamaño de fuente para 3 dígitos ({size3Digits:F2}) se reduce respecto a 2 dígitos ({size2Digits:F2})",
                    ref total, ref passed);
                Assert(spacing3Digits < spacing2Digits,
                    $"Espaciado para 3 dígitos ({spacing3Digits:F1}) es más condensado que 2 dígitos ({spacing2Digits:F1}) para no invadir aro rojo",
                    ref total, ref passed);

                // Test 3: Cambio dinámico a 2 dígitos nuevamente (60 km/h)
                display.SetSpeedLimit(60);
                Assert(Mathf.Approximately(display.CurrentFontSize, size2Digits),
                    "Vuelve al tamaño amplio al regresar a 2 dígitos (60 km/h)", ref total, ref passed);

                // Test 4: Cambio a 3 dígitos extremo (130 km/h)
                display.SetSpeedLimit(130);
                Assert(Mathf.Approximately(display.CurrentFontSize, size3Digits),
                    "Aplica calibración reducida a 130 km/h", ref total, ref passed);

                // Test 5: Clamping de límites reglamentarios (20 a 130)
                display.SetSpeedLimit(10);
                Assert(display.SpeedLimitKmh == SpeedLimitSignDisplay.MIN_SPEED_KMH,
                    $"Velocidad menor a {SpeedLimitSignDisplay.MIN_SPEED_KMH} se clampa al mínimo reglamentario", ref total, ref passed);

                display.SetSpeedLimit(200);
                Assert(display.SpeedLimitKmh == SpeedLimitSignDisplay.MAX_SPEED_KMH,
                    $"Velocidad mayor a {SpeedLimitSignDisplay.MAX_SPEED_KMH} se clampa al máximo reglamentario", ref total, ref passed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- Lógica de Evaluación Pura
        private static void VerifyPureEvaluationLogic(ref int total, ref int passed)
        {
            Debug.Log("--- [2/5] Verificando Lógica Pura de Evaluación y Tolerancia ---");

            var go = new GameObject("TestEvaluator");
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            var evaluator = go.AddComponent<SpeedLimitEvaluator>();

            try
            {
                // Zona 40 km/h con 3 km/h de tolerancia -> límite estricto de infracción: > 43.0 km/h
                evaluator.SpeedLimitKmh = 40;
                evaluator.ToleranceKmh = 3f;

                Assert(!evaluator.IsExcessSpeed(30f), "30 km/h en zona 40: NO infracción", ref total, ref passed);
                Assert(!evaluator.IsExcessSpeed(40f), "40 km/h en zona 40: NO infracción", ref total, ref passed);
                Assert(!evaluator.IsExcessSpeed(42.9f), "42.9 km/h (dentro de tolerancia 3 km/h): NO infracción", ref total, ref passed);
                Assert(!evaluator.IsExcessSpeed(43.0f), "43.0 km/h (borde exacto de tolerancia): NO infracción", ref total, ref passed);
                Assert(evaluator.IsExcessSpeed(43.1f), "43.1 km/h (excede límite + tolerancia): INFRACCIÓN", ref total, ref passed);
                Assert(evaluator.IsExcessSpeed(55f), "55.0 km/h en zona 40: INFRACCIÓN clara", ref total, ref passed);

                // Zona 110 km/h con 5 km/h de tolerancia -> infracción si > 115.0 km/h
                evaluator.SpeedLimitKmh = 110;
                evaluator.ToleranceKmh = 5f;

                Assert(!evaluator.IsExcessSpeed(100f), "100 km/h en zona 110: NO infracción", ref total, ref passed);
                Assert(!evaluator.IsExcessSpeed(115f), "115.0 km/h (en tolerancia 5 km/h): NO infracción", ref total, ref passed);
                Assert(evaluator.IsExcessSpeed(115.1f), "115.1 km/h en zona 110: INFRACCIÓN", ref total, ref passed);
                Assert(evaluator.IsExcessSpeed(130f), "130.0 km/h en zona 110: INFRACCIÓN", ref total, ref passed);

                // Verificación de sincronización con cartel
                var signGo = new GameObject("TestSign");
                var signDisplay = signGo.AddComponent<SpeedLimitSignDisplay>();
                signDisplay.SetSpeedLimit(60);

                evaluator.SignDisplay = signDisplay;
                evaluator.SyncWithSign();
                Assert(evaluator.SpeedLimitKmh == 60, "Evaluador sincronizado con límite del cartel (60 km/h)", ref total, ref passed);
                Object.DestroyImmediate(signGo);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- Assets y Prefabs
        private static void VerifyAssetsAndPrefabs(ref int total, ref int passed)
        {
            Debug.Log("--- [3/5] Verificando Integridad de Assets y Prefabs ---");

            // Asegurar que los assets existan mediante TrafficSignSetup si faltaran
            if (!File.Exists(TrafficSignSetup.PREFAB_SIGN_PATH) || !File.Exists(TrafficSignSetup.PREFAB_ZONE_PATH))
            {
                TrafficSignSetup.SetupAll();
            }

            // 1. Textura y Material
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TrafficSignSetup.TEXTURE_PATH);
            Assert(tex != null, $"Textura R.14 debe existir en {TrafficSignSetup.TEXTURE_PATH}", ref total, ref passed);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(TrafficSignSetup.MATERIAL_PATH);
            Assert(mat != null, $"Material R.14 debe existir en {TrafficSignSetup.MATERIAL_PATH}", ref total, ref passed);
            if (mat != null)
            {
                Assert(mat.mainTexture != null, "Material tiene textura principal asignada", ref total, ref passed);
            }

            // 2. Prefab Cartel
            var signPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficSignSetup.PREFAB_SIGN_PATH);
            Assert(signPrefab != null, $"Prefab cartel debe existir en {TrafficSignSetup.PREFAB_SIGN_PATH}", ref total, ref passed);
            if (signPrefab != null)
            {
                var display = signPrefab.GetComponent<SpeedLimitSignDisplay>();
                Assert(display != null, "Prefab cartel tiene componente SpeedLimitSignDisplay", ref total, ref passed);

                var tmp = signPrefab.GetComponentInChildren<TextMeshPro>(true);
                Assert(tmp != null, "Prefab cartel tiene TextMeshPro en jerarquía", ref total, ref passed);
                if (tmp != null)
                {
                    Assert(tmp.font != null, "TextMeshPro tiene fuente asignada", ref total, ref passed);
                    Assert(tmp.font.name.Contains("LiberationSans"), "TextMeshPro usa LiberationSans SDF reglamentaria", ref total, ref passed);
                }
            }

            // 3. Prefab Cartel con Zona de Evaluación
            var zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficSignSetup.PREFAB_ZONE_PATH);
            Assert(zonePrefab != null, $"Prefab con zona debe existir en {TrafficSignSetup.PREFAB_ZONE_PATH}", ref total, ref passed);
            if (zonePrefab != null)
            {
                var display = zonePrefab.GetComponent<SpeedLimitSignDisplay>();
                Assert(display != null, "Prefab con zona tiene SpeedLimitSignDisplay", ref total, ref passed);

                var evaluator = zonePrefab.GetComponentInChildren<SpeedLimitEvaluator>(true);
                Assert(evaluator != null, "Prefab con zona tiene SpeedLimitEvaluator", ref total, ref passed);
                if (evaluator != null)
                {
                    var box = evaluator.GetComponent<BoxCollider>();
                    Assert(box != null && box.isTrigger, "SpeedLimitEvaluator tiene BoxCollider con isTrigger = true", ref total, ref passed);
                    Assert(evaluator.ToleranceKmh > 0f, "Tolerancia configurada (> 0 km/h)", ref total, ref passed);
                    Assert(evaluator.AlertCooldown > 0f, "Cooldown de alertas configurado (> 0 s)", ref total, ref passed);
                }
            }
        }

        // ---------------------------------------------------------------- Regla AGENTS.md: APIs Obsoletas
        private static void VerifyNoDeprecatedAPIs(ref int total, ref int passed)
        {
            Debug.Log("--- [4/5] Verificando Regla de APIs de Unity Obsoletas ---");

            string[] scriptPaths = new string[]
            {
                "Assets/Scripts/Environment/SpeedLimitSignDisplay.cs",
                "Assets/Scripts/Environment/SpeedLimitEvaluator.cs",
                "Assets/Scripts/Testing/Editor/TrafficSignSetup.cs",
                "Assets/Scripts/Testing/Editor/TrafficSignSceneSetup.cs"
            };

            string deprecatedTarget = "Find" + "FirstObjectByType";
            foreach (var path in scriptPaths)
            {
                string fullPath = Path.Combine(Application.dataPath, "..", path);
                if (File.Exists(fullPath))
                {
                    string content = File.ReadAllText(fullPath);
                    bool hasDeprecated = content.Contains(deprecatedTarget);
                    Assert(!hasDeprecated, $"{Path.GetFileName(path)} NO usa {deprecatedTarget} (usa FindAnyObjectByType)", ref total, ref passed);
                }
                else
                {
                    Assert(false, $"Script {path} debe existir", ref total, ref passed);
                }
            }
        }

        // ---------------------------------------------------------------- Escena de Prueba
        private static void VerifyTestScene(ref int total, ref int passed)
        {
            Debug.Log("--- [5/5] Verificando Escena de Prueba de Señalización ---");

            Assert(File.Exists(TrafficSignSetup.TEST_SCENE_PATH),
                $"Escena de prueba debe existir en {TrafficSignSetup.TEST_SCENE_PATH}", ref total, ref passed);

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(TrafficSignSetup.TEST_SCENE_PATH);
            Assert(sceneAsset != null, "SceneAsset es válido y cargable", ref total, ref passed);

            // Verificación de integridad de escenas de ciudad y TrafficSignSceneSetup
            Assert(File.Exists(TrafficSignSceneSetup.ORIGINAL_SCENE_PATH),
                $"Escena original protegida debe existir intacta en {TrafficSignSceneSetup.ORIGINAL_SCENE_PATH}", ref total, ref passed);

            Assert(File.Exists("Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityMerged.unity"),
                "Escena CityMerged.unity original existe intacta", ref total, ref passed);

            Assert(File.Exists("Assets/SimplePoly City - Low Poly Assets/Demo/TestingScenes/CityWithCar.unity"),
                "Escena CityWithCar.unity original existe intacta", ref total, ref passed);

            if (File.Exists(TrafficSignSceneSetup.TARGET_SCENE_PATH))
            {
                var targetSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(TrafficSignSceneSetup.TARGET_SCENE_PATH);
                Assert(targetSceneAsset != null, $"SceneAsset {TrafficSignSceneSetup.TARGET_SCENE_PATH} es válido y cargable", ref total, ref passed);
            }
        }

        public static string RunAllTestsSummary()
        {
            int total = 0, passed = 0;
            VerifyTypographyCalibration(ref total, ref passed);
            VerifyPureEvaluationLogic(ref total, ref passed);
            VerifyAssetsAndPrefabs(ref total, ref passed);
            VerifyNoDeprecatedAPIs(ref total, ref passed);
            VerifyTestScene(ref total, ref passed);
            return $"{passed}/{total} passed (Failures: {total - passed})";
        }

        // ---------------------------------------------------------------- Helper Assert
        private static void Assert(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"  [PASS] {message}");
            }
            else
            {
                Debug.LogError($"  [FAIL] {message}");
            }
        }
    }
}
