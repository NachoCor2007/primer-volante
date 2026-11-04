using System;
using System.Linq;
using PrimerVolante.Lessons;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor de que el <see cref="InteractableHighlighter"/> no deja materiales de la cabina sucios en
    /// disco: el keyword _EMISSION nunca se activa sobre el asset (en Edit Mode usa clones temporales que ClearHighlight
    /// descarta), así que ni guardar con el resaltado activo modifica los .mat.
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
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Materials/Mat_HazardButton_Red.mat",
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
            Assert(!material.IsKeywordEnabled("_EMISSION"), "Precondicion: el material no tiene _EMISSION al empezar", ref total, ref passed);

            var go = new GameObject("TMP_HighlighterVerification");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var highlighter = go.AddComponent<InteractableHighlighter>();

                highlighter.HighlightTargets(new[] { go });
                Material highlighted = renderer.sharedMaterial;
                Assert(!material.IsKeywordEnabled("_EMISSION"), "HighlightTargets NO activa _EMISSION en el asset", ref total, ref passed);
                Assert(highlighted != null && highlighted != material && !EditorUtility.IsPersistent(highlighted),
                    "Durante el resaltado el renderer usa un clone temporal (no el asset)", ref total, ref passed);
                Assert(highlighted != null && highlighted.IsKeywordEnabled("_EMISSION"), "El clone temporal tiene _EMISSION", ref total, ref passed);

                // Guardar con el resaltado activo no debe persistir nada en los .mat.
                AssetDatabase.SaveAssets();
                string[] hashesDuring = s_GuardedMaterials.Select(Sha1).ToArray();
                Assert(hashesBefore.SequenceEqual(hashesDuring), "SaveAssets con el resaltado activo no modifica los .mat", ref total, ref passed);

                // Segunda llamada: restaura y vuelve a clonar desde el original, sin anidar clones.
                highlighter.HighlightTargets(new[] { go });
                Assert(highlighted == null, "Re-resaltar destruye el clone anterior", ref total, ref passed);
                Material second = renderer.sharedMaterial;
                Assert(second != null && second != material && second.name == material.name + " (Highlight)",
                    "Re-resaltar clona desde el material original", ref total, ref passed);

                highlighter.ClearHighlight();
                Assert(renderer.sharedMaterial == material, "ClearHighlight devuelve el material original al renderer", ref total, ref passed);
                Assert(second == null, "ClearHighlight destruye el clone temporal", ref total, ref passed);

                // Un material que ya tenia _EMISSION no se clona ni se toca.
                var own = new Material(material);
                own.EnableKeyword("_EMISSION");
                renderer.sharedMaterial = own;
                highlighter.HighlightTargets(new[] { go });
                Assert(renderer.sharedMaterial == own, "Un material que ya tenia _EMISSION no se reemplaza", ref total, ref passed);
                highlighter.ClearHighlight();
                Assert(own.IsKeywordEnabled("_EMISSION"), "Un material que ya tenia _EMISSION lo conserva", ref total, ref passed);
                UnityEngine.Object.DestroyImmediate(own);

                // Renderer destruido durante el resaltado: limpiar no debe lanzar.
                renderer.sharedMaterial = material;
                highlighter.HighlightTargets(new[] { go });
                UnityEngine.Object.DestroyImmediate(renderer);
                bool threw = false;
                try { highlighter.ClearHighlight(); } catch (Exception) { threw = true; }
                Assert(!threw, "ClearHighlight con un renderer ya destruido no lanza", ref total, ref passed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            AssetDatabase.SaveAssets();
            string[] hashesAfter = s_GuardedMaterials.Select(Sha1).ToArray();
            var changed = s_GuardedMaterials.Where((p, i) => hashesBefore[i] != hashesAfter[i]).Select(System.IO.Path.GetFileName).ToList();
            Assert(changed.Count == 0, $"Tras SaveAssets los .mat vigilados no cambiaron en disco ({(changed.Count == 0 ? "sin cambios" : string.Join(", ", changed))})", ref total, ref passed);
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
