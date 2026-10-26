using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Verificación automatizada en Editor del testigo de infracción del tablero: testigos espejados en
    /// <c>Dashboard_Cluster_Canvas</c>, cableado en el <see cref="DashboardUIController"/> y chime
    /// (<see cref="VehicleInfractionAudio"/> + <see cref="VehicleAudioProfile.InfractionChime"/>) de Car06 a Car09.
    /// </summary>
    public static class InfractionWarningVerification
    {
        private const string BASE_PATH = "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/";
        private static readonly string[] s_Cars = { "Car06", "Car07", "Car08", "Car09" };

        [MenuItem("Tools/Primer Volante/Verify Infraction Warning")]
        public static void RunVerification()
        {
            Debug.Log("=========================================================");
            Debug.Log("🔍 INICIANDO VERIFICACIÓN DEL TESTIGO DE INFRACCIÓN");
            Debug.Log("=========================================================");

            int total = 0;
            int passed = 0;

            foreach (string car in s_Cars)
                VerifyCar(car, ref total, ref passed);

            Debug.Log("=========================================================");
            if (passed == total)
                Debug.Log($"<color=green>✅ VERIFICACIÓN DEL TESTIGO DE INFRACCIÓN COMPLETADA CON ÉXITO: {passed}/{total} pruebas pasadas.</color>");
            else
                Debug.LogError($"<color=red>❌ VERIFICACIÓN DEL TESTIGO DE INFRACCIÓN FALLÓ: {passed}/{total} pruebas pasadas.</color>");
            Debug.Log("=========================================================");
        }

        private static void VerifyCar(string car, ref int total, ref int passed)
        {
            string path = $"{BASE_PATH}{car}.prefab";
            if (!System.IO.File.Exists(path))
            {
                Check(false, $"{car}.prefab debe existir", ref total, ref passed);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform canvas = root.transform.Find("Dashboard_Cluster_Canvas");
                Check(canvas != null, $"{car}: debe tener Dashboard_Cluster_Canvas", ref total, ref passed);
                if (canvas == null) return;

                VerifyIndicators(car, canvas, ref total, ref passed);
                VerifyController(car, canvas, ref total, ref passed);
                VerifyAudio(car, canvas, ref total, ref passed);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void VerifyIndicators(string car, Transform canvas, ref int total, ref int passed)
        {
            Transform leftT = canvas.Find(DashboardClusterSetup.INFRACTION_LEFT_NAME);
            Transform rightT = canvas.Find(DashboardClusterSetup.INFRACTION_RIGHT_NAME);
            Check(leftT != null, $"{car}: {DashboardClusterSetup.INFRACTION_LEFT_NAME} debe ser hijo directo del canvas", ref total, ref passed);
            Check(rightT != null, $"{car}: {DashboardClusterSetup.INFRACTION_RIGHT_NAME} debe ser hijo directo del canvas", ref total, ref passed);
            if (leftT == null || rightT == null) return;

            foreach (Transform t in new[] { leftT, rightT })
            {
                Image image = t.GetComponent<Image>();
                Check(image != null, $"{car}: {t.name} debe tener Image", ref total, ref passed);
                if (image == null) continue;

                string spritePath = image.sprite != null ? AssetDatabase.GetAssetPath(image.sprite) : "";
                Check(spritePath == DashboardClusterSetup.ICON_INFRACTION_PATH,
                    $"{car}: {t.name} debe usar el sprite icon_infraction_warning (actual: '{spritePath}')", ref total, ref passed);
                Check(image.preserveAspect, $"{car}: {t.name} debe tener preserveAspect", ref total, ref passed);
                Check(!image.raycastTarget, $"{car}: {t.name} no debe tener raycastTarget", ref total, ref passed);
            }

            RectTransform left = (RectTransform)leftT;
            RectTransform right = (RectTransform)rightT;
            Check(Mathf.Approximately(left.anchoredPosition.x, -right.anchoredPosition.x)
                  && Mathf.Approximately(left.anchoredPosition.y, right.anchoredPosition.y),
                $"{car}: los testigos deben ser simétricos (x opuestas, misma y): {left.anchoredPosition} / {right.anchoredPosition}", ref total, ref passed);

            // Sin superposición con la velocidad ni con los guiños (rects en el espacio local del canvas).
            Rect leftRect = LocalRect(canvas, left);
            Rect rightRect = LocalRect(canvas, right);
            foreach (string other in new[] { "Speed_Value", "TurnSignal_Left", "TurnSignal_Right" })
            {
                Transform o = canvas.Find(other);
                if (o == null)
                {
                    Check(false, $"{car}: debe existir {other} para comprobar la superposición", ref total, ref passed);
                    continue;
                }
                Rect otherRect = LocalRect(canvas, (RectTransform)o);
                Check(!leftRect.Overlaps(otherRect) && !rightRect.Overlaps(otherRect),
                    $"{car}: los testigos no deben superponerse con {other}", ref total, ref passed);
            }
        }

        private static void VerifyController(string car, Transform canvas, ref int total, ref int passed)
        {
            DashboardUIController controller = canvas.GetComponent<DashboardUIController>();
            Check(controller != null, $"{car}: el canvas debe tener DashboardUIController", ref total, ref passed);
            if (controller == null) return;

            var so = new SerializedObject(controller);
            Check(so.FindProperty("m_InfractionLeftIndicator").objectReferenceValue == canvas.Find(DashboardClusterSetup.INFRACTION_LEFT_NAME)?.GetComponent<Image>(),
                $"{car}: m_InfractionLeftIndicator debe apuntar al Image izquierdo", ref total, ref passed);
            Check(so.FindProperty("m_InfractionRightIndicator").objectReferenceValue == canvas.Find(DashboardClusterSetup.INFRACTION_RIGHT_NAME)?.GetComponent<Image>(),
                $"{car}: m_InfractionRightIndicator debe apuntar al Image derecho", ref total, ref passed);
            Check(Mathf.Approximately(so.FindProperty("m_InfractionWarningDuration").floatValue, 3f),
                $"{car}: m_InfractionWarningDuration debe ser 3 s", ref total, ref passed);
            Check(so.FindProperty("m_InfractionActiveColor").colorValue == new Color(1f, 0.15f, 0.15f, 1f),
                $"{car}: color activo debe ser (1, 0.15, 0.15, 1)", ref total, ref passed);
            Check(so.FindProperty("m_InfractionInactiveColor").colorValue == new Color(0.15f, 0.15f, 0.15f, 0.25f),
                $"{car}: color inactivo debe ser (0.15, 0.15, 0.15, 0.25)", ref total, ref passed);
        }

        private static void VerifyAudio(string car, Transform canvas, ref int total, ref int passed)
        {
            bool mandatory = car == "Car09";

            VehicleInfractionAudio audio = canvas.GetComponent<VehicleInfractionAudio>();
            Check(audio != null, $"{car}: el canvas debe tener VehicleInfractionAudio", ref total, ref passed);
            if (audio == null) return;

            var so = new SerializedObject(audio);
            var source = so.FindProperty("m_Source").objectReferenceValue as AudioSource;
            var profile = so.FindProperty("m_Profile").objectReferenceValue as VehicleAudioProfile;
            var mixer = so.FindProperty("m_MixerGroup").objectReferenceValue;

            Check(source != null, $"{car}: VehicleInfractionAudio debe tener AudioSource", ref total, ref passed);
            Check(so.FindProperty("m_Dashboard").objectReferenceValue == canvas.GetComponent<DashboardUIController>(),
                $"{car}: VehicleInfractionAudio.m_Dashboard debe apuntar al DashboardUIController", ref total, ref passed);
            if (source != null)
                Check(!source.loop && !source.playOnAwake, $"{car}: el AudioSource del chime no debe ser loop ni playOnAwake", ref total, ref passed);

            if (mandatory)
            {
                Check(profile != null, $"{car}: VehicleInfractionAudio debe tener perfil", ref total, ref passed);
                Check(mixer != null, $"{car}: VehicleInfractionAudio debe tener AudioMixerGroup", ref total, ref passed);
            }
            else if (profile == null || mixer == null)
            {
                Debug.LogWarning($"⚠ {car}: VehicleInfractionAudio sin perfil o mixer (el prefab no tiene VehicleCabinControlsAudio); no sonará.");
            }

            if (profile != null)
            {
                Check(profile.InfractionChime != null, $"{car}: el perfil '{profile.name}' debe tener InfractionChime", ref total, ref passed);
                if (profile.InfractionChime != null)
                {
                    float length = profile.InfractionChime.length;
                    Check(length >= 0.5f && length <= 1f, $"{car}: el chime debe durar entre 0.5 y 1 s (actual: {length:F2} s)", ref total, ref passed);
                }
            }
        }

        private static Rect LocalRect(Transform canvas, RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector3 a = canvas.InverseTransformPoint(corners[0]);
            Vector3 b = canvas.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static void Check(bool condition, string message, ref int total, ref int passed)
        {
            total++;
            if (condition)
            {
                passed++;
                Debug.Log($"✅ {message}");
            }
            else
            {
                Debug.LogError($"❌ {message}");
            }
        }
    }
}
