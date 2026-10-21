using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación en Editor del volante agarrable con las dos manos:
    /// serialización de Car07/Car08/Car09, configuración en runtime (Awake) y matemática del
    /// promedio de deltas angulares ponderado por radio.
    /// </summary>
    public static class SteeringWheelTwoHandsVerification
    {
        private const string CAR_FOLDER = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs";
        private const float EPS = 0.0001f;

        [MenuItem("Tools/Primer Volante/Run Steering Wheel Two Hands Verification")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("INICIANDO VERIFICACION DEL VOLANTE CON DOS MANOS");
            Debug.Log("=========================================================");

            int total = 0;
            int passed = 0;

            // 1. Prefabs serializados
            foreach (string carName in new[] { "Car07", "Car08", "Car09" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CAR_FOLDER}/{carName}.prefab");
                Assert(prefab != null, $"{carName}.prefab debe existir", ref total, ref passed);
                if (prefab == null) continue;

                VRSteeringWheel wheel = prefab.GetComponentInChildren<VRSteeringWheel>(true);
                Assert(wheel != null, $"{carName}: debe tener VRSteeringWheel", ref total, ref passed);
                if (wheel == null) continue;

                XRGrabInteractable grab = wheel.GetComponent<XRGrabInteractable>();
                Assert(grab != null, $"{carName}: el volante debe tener XRGrabInteractable", ref total, ref passed);
                if (grab == null) continue;

                Assert(grab.selectMode == InteractableSelectMode.Multiple, $"{carName}: selectMode == Multiple", ref total, ref passed);
                Assert(!grab.trackPosition, $"{carName}: trackPosition == false", ref total, ref passed);
                Assert(!grab.trackRotation, $"{carName}: trackRotation == false", ref total, ref passed);
                Assert(!grab.trackScale, $"{carName}: trackScale == false", ref total, ref passed);
                Assert(!grab.addDefaultGrabTransformers, $"{carName}: addDefaultGrabTransformers == false", ref total, ref passed);
                Assert(grab.startingSingleGrabTransformers == null || grab.startingSingleGrabTransformers.Count == 0,
                    $"{carName}: sin single grab transformers", ref total, ref passed);
                Assert(grab.startingMultipleGrabTransformers == null || grab.startingMultipleGrabTransformers.Count == 0,
                    $"{carName}: sin multiple grab transformers", ref total, ref passed);
            }

            // 2. Instancia de Car08 tras Awake
            GameObject car08 = AssetDatabase.LoadAssetAtPath<GameObject>($"{CAR_FOLDER}/Car08.prefab");
            if (car08 != null)
            {
                GameObject instance = Object.Instantiate(car08);
                instance.name = "Car08_TwoHandsTest";
                instance.BroadcastMessage("Awake", SendMessageOptions.DontRequireReceiver);
                try
                {
                    VRSteeringWheel wheel = instance.GetComponentInChildren<VRSteeringWheel>(true);
                    Assert(wheel != null, "Instancia Car08: VRSteeringWheel presente", ref total, ref passed);
                    if (wheel != null)
                    {
                        XRGrabInteractable grab = wheel.GetComponent<XRGrabInteractable>();
                        Assert(grab != null && grab.selectMode == InteractableSelectMode.Multiple,
                            "Instancia Car08: selectMode == Multiple tras Awake", ref total, ref passed);
                        Assert(wheel.GrabbingHandCount == 0, "Instancia Car08: GrabbingHandCount == 0", ref total, ref passed);
                        Assert(!wheel.IsGrabbed, "Instancia Car08: IsGrabbed == false", ref total, ref passed);
                        Assert(!wheel.IsHeldWithBothHands, "Instancia Car08: IsHeldWithBothHands == false", ref total, ref passed);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            // 3. Matemática del promedio ponderado
            // Una mano: delta exacto (sum = delta * r, weight = r)
            Assert(Approx(VRSteeringWheel.WeightedAverageDelta(7.3f * 0.18f, 0.18f), 7.3f),
                "Una mano: devuelve su delta exacto", ref total, ref passed);
            // Dos manos, igual delta, radios distintos
            Assert(Approx(VRSteeringWheel.WeightedAverageDelta(12f * 0.18f + 12f * 0.05f, 0.18f + 0.05f), 12f),
                "Dos manos con igual delta y radios distintos: el mismo delta", ref total, ref passed);
            // Una quieta y otra +10 con radios iguales
            Assert(Approx(VRSteeringWheel.WeightedAverageDelta(0f * 0.18f + 10f * 0.18f, 0.36f), 5f),
                "Una quieta y otra +10 (radios iguales): +5", ref total, ref passed);
            // Opuestas con radios iguales
            Assert(Approx(VRSteeringWheel.WeightedAverageDelta(10f * 0.18f - 10f * 0.18f, 0.36f), 0f),
                "Manos opuestas con radios iguales: 0", ref total, ref passed);
            // Radios 0.18 y 0.02: domina la del aro (+10 en el aro, 0 en la cercana al centro)
            float dominated = VRSteeringWheel.WeightedAverageDelta(10f * 0.18f + 0f * 0.02f, 0.20f);
            Assert(Approx(dominated, 9f) && dominated > 5f,
                "Radios 0.18 y 0.02: domina la mano del aro", ref total, ref passed);
            // Peso cero
            Assert(VRSteeringWheel.WeightedAverageDelta(5f, 0f) == 0f, "weightSum = 0: devuelve 0", ref total, ref passed);
            Assert(VRSteeringWheel.WeightedAverageDelta(5f, -1f) == 0f, "weightSum < 0: devuelve 0", ref total, ref passed);

            Debug.Log("=========================================================");
            Debug.Log($"RESULTADO FINAL: {passed}/{total} pruebas pasadas con exito!");
            Debug.Log("=========================================================");
        }

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < EPS;

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
