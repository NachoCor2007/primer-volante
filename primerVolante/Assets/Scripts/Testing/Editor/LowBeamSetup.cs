using PrimerVolante.Environment;
using PrimerVolante.Evaluation;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Herramienta de Editor idempotente de la infracción de circular sin luces bajas (<c>LOW_BEAM</c>): definición
    /// y entrada de catálogo de la infracción, la escena <c>GPSLowBeamDemo</c> (copia de <c>GPSTurnSignalDemo</c>) y
    /// el <see cref="LowBeamDetector"/> en el <c>Car09</c> de esa escena, como override de escena (no se toca el prefab).
    /// </summary>
    public static class LowBeamSetup
    {
        public const string SOURCE_SCENE_PATH = "Assets/Scenes/TestingScenes/GPSTurnSignalDemo.unity";
        public const string SCENE_PATH = "Assets/Scenes/TestingScenes/GPSLowBeamDemo.unity";
        public const string CAR_NAME = "Car09";

        [MenuItem("Tools/Primer Volante/Setup Low Beam Detection")]
        public static void SetupAll()
        {
            Debug.Log("[LowBeamSetup] 🔧 Configurando la detección de circular sin luces bajas (LOW_BEAM)...");

            // Se chequea antes de tocar nada: si hay una escena sucia (también la demo, si ya está abierta), se aborta sin cambios.
            if (DemoSceneSetup.HasDirtyScenes("[LowBeamSetup]", SCENE_PATH)) return;

            InfractionCatalogSO catalog = InfractionManagerSetup.EnsureDefinitionsAndCatalog();
            if (catalog == null || !catalog.TryGet(InfractionIds.LowBeam, out _))
            {
                Debug.LogError("[LowBeamSetup] ❌ No se pudo crear la definición LOW_BEAM ni el catálogo de infracciones.");
                return;
            }

            string sceneStatus = SetupScene();
            if (sceneStatus == null) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[LowBeamSetup] ✅ Listo. Catálogo con {catalog.Definitions.Count} definiciones. Escena {SCENE_PATH}: {sceneStatus}.");
        }

        // Devuelve una descripción de lo hecho o null si falló.
        private static string SetupScene()
        {
            return DemoSceneSetup.EnsureCarComponent<LowBeamDetector>(SOURCE_SCENE_PATH, SCENE_PATH, CAR_NAME, "GPSTurnSignalDemo", "[LowBeamSetup]", configure: so =>
            {
                so.FindProperty("m_ThresholdM").floatValue = LowBeamTracker.DefaultThresholdM;
                so.FindProperty("m_MinTimeS").floatValue = LowBeamTracker.DefaultMinTimeS;
                so.FindProperty("m_LogEvents").boolValue = true;
            }, skipDirtyCheck: true);
        }
    }
}
