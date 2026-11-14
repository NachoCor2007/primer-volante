using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using PrimerVolante.VR;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>
    /// Configura en Car09 la perilla de luces:
    /// - Reemplaza la SphereCollider (r = 4 cm) por una BoxCollider que cubre el bisel (~7 x 7 cm, ~3 cm de profundidad hacia el conductor).
    /// - Crea el Canvas WorldSpace <c>HeadlightKnob_ModeIcons</c> (hijo de Car09, no de la perilla, para que no gire) con un ícono
    ///   por modo, sobre la cara del bisel, derechos y en la dirección del puntero de cada ángulo de snap.
    /// Idempotente: el canvas y los íconos se actualizan en el lugar (se crean solo si faltan) y el collider solo se reemplaza si hace falta.
    /// Las posiciones de los íconos salen de los ángulos reales de la perilla; no hay coordenadas hardcodeadas.
    /// </summary>
    public static class HeadlightKnobIconsSetup
    {
        public const string CAR09_PREFAB_PATH =
            "Assets/MadTroll_Studio/Low Poly 1970s Family Sedan 3D Model Free Download Car02/Prefabs/Car09.prefab";

        public const string KNOB_NAME = "mdl_car02_lights_knob";
        public const string BEZEL_NAME = "mdl_car02_lights";
        public const string CANVAS_NAME = "HeadlightKnob_ModeIcons";

        public static readonly string[] ICON_NAMES = { "Icon_Off", "Icon_Position", "Icon_LowBeam", "Icon_HighBeam" };

        public static readonly string[] SPRITE_PATHS =
        {
            "Assets/Textures/UI/Dashboard/icon_lights_off.png",
            "Assets/Textures/UI/Dashboard/icon_lights_position.png",
            "Assets/Textures/UI/Dashboard/icon_lights_lowbeam.png",
            "Assets/Textures/UI/Dashboard/icon_lights_highbeam.png"
        };

        // Medidas en metros de mundo (Car09 tiene escala 1; la perilla tiene escala 100).
        public const float COLLIDER_FACE_SIZE = 0.07f;          // lado del collider (el bisel mide ~6.5 cm de diámetro)
        public const float COLLIDER_DEPTH = 0.03f;              // profundidad total hacia el conductor
        public const float COLLIDER_BEHIND_FACE = 0.005f;       // cuánto queda por detrás de la cara del bisel
        public const float ICON_SIZE = 0.010f;                  // lado de cada ícono
        public const float ICON_RADIUS = 0.0245f;               // distancia del eje a cada ícono
        public const float ICON_FACE_OFFSET = 0.0015f;          // separación de la cara del bisel hacia el conductor (anti z-fighting)
        public const float CANVAS_PIXEL_SCALE = 0.0001f;        // 1 px = 0.1 mm

        [MenuItem("Tools/Primer Volante/Setup Headlight Knob Icons in Car09")]
        public static void SetupCar09()
        {
            // Los sprites se importan antes de abrir el prefab
            Sprite[] sprites = new Sprite[SPRITE_PATHS.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                sprites[i] = LoadSprite(SPRITE_PATHS[i]);
                if (sprites[i] == null)
                {
                    Debug.LogError($"[HeadlightKnobIconsSetup] ❌ Falta el sprite {SPRITE_PATHS[i]}.");
                    return;
                }
            }

            GameObject root = PrefabUtility.LoadPrefabContents(CAR09_PREFAB_PATH);
            try
            {
                string error = Apply(root, sprites);
                if (error != null)
                {
                    Debug.LogError($"[HeadlightKnobIconsSetup] ❌ {error}");
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, CAR09_PREFAB_PATH, out bool ok);
                if (ok) Debug.Log("[HeadlightKnobIconsSetup] ✅ Collider de la perilla e íconos de modo configurados en Car09.");
                else Debug.LogError("[HeadlightKnobIconsSetup] ❌ No se pudo guardar Car09.prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Aplica collider e íconos sobre la raíz de un Car09 (instancia o contenido de prefab). Devuelve un mensaje de error o null.
        /// </summary>
        public static string Apply(GameObject car, Sprite[] sprites)
        {
            Transform knob = car.transform.Find(KNOB_NAME);
            Transform bezel = car.transform.Find(BEZEL_NAME);
            if (knob == null || bezel == null) return $"Car09 no tiene {KNOB_NAME} o {BEZEL_NAME}.";

            var knobScript = knob.GetComponent<VRHeadlightKnob>();
            if (knobScript == null) return "La perilla no tiene VRHeadlightKnob.";

            Transform rotor = GetRotor(knobScript, knob);
            Vector3 axisLocal = GetRotationAxis(knobScript).normalized;

            // Eje de giro (hacia el conductor) y centro de la perilla en coordenadas de Car09
            Vector3 axisCar = car.transform.InverseTransformDirection(rotor.TransformDirection(axisLocal)).normalized;
            Vector3 knobCenter = car.transform.InverseTransformPoint(knob.position);

            // Cara del bisel: el punto más cercano al conductor (máximo a lo largo del eje) entre los vértices del bisel
            float faceOffset = BezelFaceOffset(car.transform, bezel, knobCenter, axisCar);

            ReplaceCollider(knob, faceOffset);
            BuildIconCanvas(car.transform, knobScript, knobCenter, axisCar, faceOffset, sprites);
            return null;
        }

        /// <summary>
        /// Distancia (en metros, sobre el eje de la perilla hacia el conductor) del centro de la perilla a la cara del bisel.
        /// </summary>
        public static float BezelFaceOffset(Transform car, Transform bezel, Vector3 knobCenterCar, Vector3 axisCar)
        {
            Mesh mesh = bezel.GetComponent<MeshFilter>().sharedMesh;
            float max = float.NegativeInfinity;
            foreach (Vector3 v in mesh.vertices)
            {
                Vector3 p = car.InverseTransformPoint(bezel.TransformPoint(v));
                max = Mathf.Max(max, Vector3.Dot(p - knobCenterCar, axisCar));
            }
            return max;
        }

        /// <summary>
        /// Dirección (en el espacio de Car09) a la que apunta el eje largo del mesh (+Z local) con la perilla en <paramref name="angle"/>,
        /// con la misma fórmula que <c>VRHeadlightKnob.ApplyRotation</c> (rotación inicial * AngleAxis(angle, eje)).
        /// </summary>
        public static Vector3 PointerDirectionCar(Transform car, Transform rotor, Quaternion initialLocalRotation, Vector3 axisLocal, float angle)
        {
            Quaternion rotated = initialLocalRotation * Quaternion.AngleAxis(angle, axisLocal);
            Transform parent = rotor.parent;
            Quaternion world = parent != null ? parent.rotation * rotated : rotated;
            return car.InverseTransformDirection(world * Vector3.forward);
        }

        public static Transform GetRotor(VRHeadlightKnob script, Transform fallback)
        {
            var so = new SerializedObject(script);
            var rotor = so.FindProperty("m_RotorTransform").objectReferenceValue as Transform;
            return rotor != null ? rotor : fallback;
        }

        public static Vector3 GetRotationAxis(VRHeadlightKnob script)
        {
            return new SerializedObject(script).FindProperty("m_RotationAxis").vector3Value;
        }

        private static void ReplaceCollider(Transform knob, float faceOffset)
        {
            // Primero el BoxCollider: VRHeadlightKnob exige un Collider y Unity no deja quitar el último
            var box = knob.GetComponent<BoxCollider>();
            if (box == null) box = knob.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false;

            foreach (var sphere in knob.GetComponents<SphereCollider>())
                Object.DestroyImmediate(sphere);

            // Tamaño y centro en unidades locales (la perilla tiene escala 100). El eje de giro es el eje local Y del mesh.
            Vector3 s = knob.lossyScale;
            box.size = new Vector3(COLLIDER_FACE_SIZE / s.x, COLLIDER_DEPTH / s.y, COLLIDER_FACE_SIZE / s.z);
            // Desde COLLIDER_BEHIND_FACE detrás de la cara hasta COLLIDER_DEPTH - COLLIDER_BEHIND_FACE por delante
            float centerWorld = faceOffset - COLLIDER_BEHIND_FACE + COLLIDER_DEPTH * 0.5f;
            box.center = new Vector3(0f, centerWorld / s.y, 0f);
        }

        private static void BuildIconCanvas(Transform car, VRHeadlightKnob knobScript, Vector3 knobCenter, Vector3 axisCar,
            float faceOffset, Sprite[] sprites)
        {
            Transform rotor = GetRotor(knobScript, knobScript.transform);
            Vector3 axisLocal = GetRotationAxis(knobScript).normalized;
            Quaternion initialRotation = rotor.localRotation;

            // Get-or-create: una segunda corrida actualiza en lugar de recrear (IDs estables, diffs limpios)
            Transform existing = car.Find(CANVAS_NAME);
            GameObject canvasObj = existing != null ? existing.gameObject : new GameObject(CANVAS_NAME, typeof(RectTransform));
            canvasObj.transform.SetParent(car, false);
            Transform dash = car.Find("Dashboard_Cluster_Canvas");
            if (dash != null) canvasObj.layer = dash.gameObject.layer;

            // Centrado en el eje de la perilla, sobre la cara del bisel + separación hacia el conductor.
            // Sin rotación respecto de Car09: el conductor mira hacia +Z de Car09, así que el canvas (visible desde -Z) lee derecho.
            canvasObj.transform.localPosition = knobCenter + axisCar * (faceOffset + ICON_FACE_OFFSET);
            canvasObj.transform.localRotation = Quaternion.identity;
            canvasObj.transform.localScale = Vector3.one * CANVAS_PIXEL_SCALE;

            var canvas = GetOrAdd<Canvas>(canvasObj);
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasObj.transform;
            float canvasPx = COLLIDER_FACE_SIZE / CANVAS_PIXEL_SCALE;
            rect.sizeDelta = new Vector2(canvasPx, canvasPx);
            // Sin GraphicRaycaster: los íconos no tienen que interferir con poke ni rayos.

            var icons = new Graphic[ICON_NAMES.Length];
            for (int i = 0; i < ICON_NAMES.Length; i++)
            {
                float angle = knobScript.GetAngleForMode((HeadlightMode)i);
                Vector3 dirCar = PointerDirectionCar(car, rotor, initialRotation, axisLocal, angle);
                // Componentes sobre la derecha (X) y arriba (Y) de Car09 = ejes del canvas
                Vector2 dir = new Vector2(dirCar.x, dirCar.y).normalized;

                Transform existingIcon = canvasObj.transform.Find(ICON_NAMES[i]);
                GameObject go = existingIcon != null ? existingIcon.gameObject : new GameObject(ICON_NAMES[i], typeof(RectTransform));
                go.transform.SetParent(canvasObj.transform, false);
                go.layer = canvasObj.layer;
                var r = (RectTransform)go.transform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = Vector2.one * (ICON_SIZE / CANVAS_PIXEL_SCALE);
                r.anchoredPosition = dir * (ICON_RADIUS / CANVAS_PIXEL_SCALE);
                r.localRotation = Quaternion.identity; // derechos, no rotados con el arco

                var img = GetOrAdd<Image>(go);
                img.sprite = sprites[i];
                img.preserveAspect = true;
                img.raycastTarget = false;
                icons[i] = img;
            }

            var comp = GetOrAdd<HeadlightKnobModeIcons>(canvasObj);
            var so = new SerializedObject(comp);
            so.FindProperty("m_Knob").objectReferenceValue = knobScript;
            SerializedProperty arr = so.FindProperty("m_Icons");
            arr.arraySize = icons.Length;
            for (int i = 0; i < icons.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            comp.Refresh(knobScript.CurrentMode);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        /// <summary>Importa el PNG como Sprite único, sin mipmaps, y lo devuelve.</summary>
        public static Sprite LoadSprite(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                || importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
