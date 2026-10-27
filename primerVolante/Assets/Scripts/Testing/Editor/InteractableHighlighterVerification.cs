using System;
using System.Linq;
using PrimerVolante.Lessons;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de que el <see cref="InteractableHighlighter"/> no deja materiales de la cabina sucios en
    /// disco: el keyword _EMISSION que fuerza sobre el asset se revierte con <c>RestoreForcedEmissionKeywords</c>.
    /// </summary>
    public static class InteractableHighlighterVerification
    {
        private const string KNOB_MATERIAL = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mat_LightsKnob_Blue.mat";

        private static readonly string[] s_GuardedMaterials =
        {
            KNOB_MATERIAL,
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mirrors/Mat_DoorPanel_Base.mat",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mirrors/Mat_DoorPanel_Button.mat",
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mirrors/Mat_Mirror_Casing.mat",
        };

        [MenuItem("Tools/Primer Volante/Verify Interactable Highlighter Emission Restore")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DEL HIGHLIGHTER (RESTAURACION DE _EMISSION)");
            Debug.Log("=========================================================");

            int total = 0, passed = 0;
            VerifyRestore(ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"HIGHLIGHTER _EMISSION: {passed}/{total} pruebas pasadas con exito!");
            Debug.Log("=========================================================");
        }

        private static void VerifyRestore(ref int total, ref int passed)
        {
            AssetDatabase.SaveAssets();
            var material = AssetDatabase.LoadAssetAtPath<Material>(KNOB_MATERIAL);
            Assert(material != null, "El material de la perilla de luces existe", ref total, ref passed);
            if (material == null) return;

            string[] hashesBefore = s_GuardedMaterials.Select(Sha1).ToArray();
            bool hadEmissionBefore = material.IsKeywordEnabled("_EMISSION");
            Assert(!hadEmissionBefore, "Precondicion: el material no tiene _EMISSION al empezar", ref total, ref passed);

            var go = new GameObject("TMP_HighlighterVerification");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var highlighter = go.AddComponent<InteractableHighlighter>();

                highlighter.HighlightTargets(new[] { go });
                Assert(material.IsKeywordEnabled("_EMISSION"), "HighlightTargets activa _EMISSION en memoria", ref total, ref passed);

                // Segunda llamada: el material ya lo tiene, no debe romper el registro.
                highlighter.HighlightTargets(new[] { go });

                InteractableHighlighter.RestoreForcedEmissionKeywords();
                Assert(!material.IsKeywordEnabled("_EMISSION"), "RestoreForcedEmissionKeywords quita _EMISSION", ref total, ref passed);

                // Un material que ya tenia _EMISSION antes del highlighter no debe tocarse.
                var own = new Material(material);
                own.EnableKeyword("_EMISSION");
                renderer.sharedMaterial = own;
                highlighter.HighlightTargets(new[] { go });
                InteractableHighlighter.RestoreForcedEmissionKeywords();
                Assert(own.IsKeywordEnabled("_EMISSION"), "Un material que ya tenia _EMISSION no se revierte", ref total, ref passed);
                UnityEngine.Object.DestroyImmediate(own);

                // Material destruido en el registro: no debe lanzar.
                var temp = new Material(material);
                renderer.sharedMaterial = temp;
                highlighter.HighlightTargets(new[] { go });
                UnityEngine.Object.DestroyImmediate(temp);
                bool threw = false;
                try { InteractableHighlighter.RestoreForcedEmissionKeywords(); } catch (Exception) { threw = true; }
                Assert(!threw, "Restaurar con un material ya destruido en el registro no lanza", ref total, ref passed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                InteractableHighlighter.RestoreForcedEmissionKeywords();
            }

            AssetDatabase.SaveAssets();
            string[] hashesAfter = s_GuardedMaterials.Select(Sha1).ToArray();
            var changed = s_GuardedMaterials.Where((p, i) => hashesBefore[i] != hashesAfter[i]).Select(System.IO.Path.GetFileName).ToList();
            Assert(changed.Count == 0, $"Tras SaveAssets los 4 .mat no cambiaron en disco ({(changed.Count == 0 ? "sin cambios" : string.Join(", ", changed))})", ref total, ref passed);
            Assert(!material.IsKeywordEnabled("_EMISSION"), "El material queda sin _EMISSION al final", ref total, ref passed);
        }

        private static string Sha1(string path)
        {
            if (!System.IO.File.Exists(path)) return "missing";
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(System.IO.File.ReadAllBytes(path)));
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
