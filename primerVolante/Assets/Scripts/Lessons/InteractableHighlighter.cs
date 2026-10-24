using System;
using System.Collections.Generic;
using UnityEngine;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Resalta visualmente mandos u objetos interactuables objetivo mediante un glow/brillo amarillo pulsante.
    /// Soporta resaltado individual o multiobjetivo simultáneo (ej. los soportes y carcasas de espejos retrovisores).
    /// Utiliza MaterialPropertyBlock para no instanciar materiales ni ensuciar assets.
    /// Modula _EmissionColor sobre materiales Lit (asegurando keyword _EMISSION).
    /// Excluye explícitamente cualquier cristal reflectante (Mirror_Glass) y cámaras de retrovisores.
    /// </summary>
    public class InteractableHighlighter : MonoBehaviour
    {
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private static readonly Color WarmYellowTint = new Color(1f, 0.95f, 0.3f, 1f);

        [Header("Configuración de Resaltado")]
        [Tooltip("Color base de emisión o realce del interactuable a resaltar.")]
        [SerializeField] private Color m_BaseHighlightColor = Color.yellow;

        [Tooltip("Velocidad de oscilación del brillo pulsante.")]
        [SerializeField] private float m_PulseFrequency = 2f;

        private readonly List<GameObject> m_CurrentTargets = new List<GameObject>();
        private readonly List<Renderer> m_TargetRenderers = new List<Renderer>();

        private struct RendererHighlightInfo
        {
            public Renderer renderer;
            public bool isLit;
            public bool isUnlit;
        }

        private readonly List<RendererHighlightInfo> m_TargetRendererInfos = new List<RendererHighlightInfo>();

        private MaterialPropertyBlock m_PropertyBlock;
        private MaterialPropertyBlock PropertyBlock => m_PropertyBlock ??= new MaterialPropertyBlock();
        private bool m_IsHighlighting = false;

        public bool IsHighlighting => m_IsHighlighting;
        public GameObject CurrentTarget => m_CurrentTargets.Count > 0 ? m_CurrentTargets[0] : null;
        public IReadOnlyList<GameObject> CurrentTargets => m_CurrentTargets;
        public IReadOnlyList<Renderer> TargetRenderers => m_TargetRenderers;

        private void Awake()
        {
            if (m_PropertyBlock == null)
            {
                m_PropertyBlock = new MaterialPropertyBlock();
            }
        }

        private void Update()
        {
            if (!m_IsHighlighting || m_TargetRendererInfos.Count == 0) return;

            // Oscilación suave entre 0.0 y 1.0 utilizando tiempo no escalado
            float pulse = Mathf.PingPong(Time.unscaledTime * m_PulseFrequency, 1f);
            Color pulseEmissionColor = m_BaseHighlightColor * pulse;
            Color unlitHighlightColor = Color.Lerp(Color.white, WarmYellowTint, pulse);

            for (int i = 0; i < m_TargetRendererInfos.Count; i++)
            {
                RendererHighlightInfo info = m_TargetRendererInfos[i];
                Renderer r = info.renderer;
                if (r == null) continue;

                r.GetPropertyBlock(PropertyBlock);

                if (info.isLit)
                {
                    PropertyBlock.SetColor(EmissionColorPropertyId, pulseEmissionColor);
                }

                if (info.isUnlit)
                {
                    PropertyBlock.SetColor(BaseColorPropertyId, unlitHighlightColor);
                    PropertyBlock.SetColor(ColorPropertyId, unlitHighlightColor);
                }

                r.SetPropertyBlock(PropertyBlock);
            }
        }

        /// <summary>
        /// Activa el resaltado pulsante sobre un único GameObject objetivo y todos sus renderers hijos
        /// (excluyendo cristales de espejos y cámaras).
        /// </summary>
        public void Highlight(GameObject target)
        {
            if (target == null)
            {
                ClearHighlight();
                return;
            }

            if (m_CurrentTargets.Count == 1 && m_CurrentTargets[0] == target && m_IsHighlighting)
            {
                return;
            }

            HighlightTargets(new[] { target });
        }

        /// <summary>
        /// Activa el resaltado simultáneo sobre una colección de GameObjects objetivos y sus renderers hijos.
        /// Excluye explícitamente cualquier Renderer asociado a Mirror_Glass o VehicleMirrorCamera.
        /// </summary>
        public void HighlightTargets(IEnumerable<GameObject> targets)
        {
            ClearHighlight();

            if (targets == null) return;

            var tempRenderers = new List<Renderer>();

            foreach (GameObject target in targets)
            {
                if (target == null) continue;

                if (!m_CurrentTargets.Contains(target))
                {
                    m_CurrentTargets.Add(target);
                }

                tempRenderers.Clear();
                target.GetComponentsInChildren(true, tempRenderers);

                for (int i = 0; i < tempRenderers.Count; i++)
                {
                    Renderer r = tempRenderers[i];
                    if (r == null || m_TargetRenderers.Contains(r)) continue;

                    // EXCLUSIÓN EXPLÍCITA: Cristales reflectantes y cámaras de espejos retrovisores
                    if (r.gameObject.name.Equals("Mirror_Glass", StringComparison.OrdinalIgnoreCase) ||
                        r.GetComponent<VehicleMirrorCamera>() != null ||
                        r.GetComponentInParent<VehicleMirrorCamera>() != null ||
                        r.GetComponent<Camera>() != null ||
                        r.GetComponentInParent<Camera>() != null)
                    {
                        continue;
                    }

                    m_TargetRenderers.Add(r);

                    bool isLit = false;
                    bool isUnlit = false;

                    Material[] sharedMats = r.sharedMaterials;
                    if (sharedMats != null)
                    {
                        for (int m = 0; m < sharedMats.Length; m++)
                        {
                            Material mat = sharedMats[m];
                            if (mat == null) continue;

                            if (mat.HasProperty(EmissionColorPropertyId))
                            {
                                isLit = true;
                                if (!mat.IsKeywordEnabled("_EMISSION"))
                                {
                                    mat.EnableKeyword("_EMISSION");
                                }
                            }
                            else if (mat.HasProperty(BaseColorPropertyId) || mat.HasProperty(ColorPropertyId))
                            {
                                isUnlit = true;
                            }
                        }
                    }

                    // Fallback por defecto si no detectó propiedades
                    if (!isLit && !isUnlit)
                    {
                        isLit = true;
                    }

                    m_TargetRendererInfos.Add(new RendererHighlightInfo
                    {
                        renderer = r,
                        isLit = isLit,
                        isUnlit = isUnlit
                    });
                }
            }

            m_IsHighlighting = m_TargetRendererInfos.Count > 0;
        }

        /// <summary>
        /// Busca interactuables por nombre, lista separada por comas, o palabras clave ("Mirrors" / "AllMirrors") y los resalta.
        /// </summary>
        public void HighlightByName(string targetName, Transform searchRoot = null)
        {
            ClearHighlight();

            if (string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            string trimmedTarget = targetName.Trim();

            // 1. Caso especial: Todos los espejos retrovisores ("Mirrors" o "AllMirrors")
            if (trimmedTarget.Equals("Mirrors", StringComparison.OrdinalIgnoreCase) ||
                trimmedTarget.Equals("AllMirrors", StringComparison.OrdinalIgnoreCase))
            {
                HighlightAllMirrors(searchRoot);
                return;
            }

            // 2. Múltiples interactuables separados por coma, punto y coma o barra vertical
            if (trimmedTarget.IndexOfAny(new[] { ',', ';', '|' }) >= 0)
            {
                string[] tokens = trimmedTarget.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                var foundList = new List<GameObject>();

                for (int i = 0; i < tokens.Length; i++)
                {
                    string token = tokens[i].Trim();
                    if (string.IsNullOrEmpty(token)) continue;

                    GameObject obj = FindSingleTarget(token, searchRoot);
                    if (obj != null && !foundList.Contains(obj))
                    {
                        foundList.Add(obj);
                    }
                }

                if (foundList.Count > 0)
                {
                    HighlightTargets(foundList);
                }
                else
                {
                    ClearHighlight();
                    Debug.LogWarning($"[InteractableHighlighter] No se encontró ninguno de los objetos especificados en '{targetName}'.");
                }
                return;
            }

            // 3. Objetivo único estándar
            GameObject found = FindSingleTarget(trimmedTarget, searchRoot);
            if (found != null)
            {
                Highlight(found);
            }
            else
            {
                ClearHighlight();
                Debug.LogWarning($"[InteractableHighlighter] No se encontró el objeto o componente '{targetName}' para resaltar.");
            }
        }

        /// <summary>
        /// Resalta simultáneamente el soporte/carcasa del retrovisor interior central (Casing_Mesh / RearviewMirror_Casing)
        /// y de los dos espejos laterales (Mirror_Housing), excluyendo terminantemente cristales y cámaras.
        /// </summary>
        private void HighlightAllMirrors(Transform searchRoot)
        {
            var mirrorTargets = new List<GameObject>();

            // 1. Retrovisor central interior: Casing_Mesh / RearviewMirror_Casing
            GameObject centralMirror = null;
            if (searchRoot != null)
            {
                Transform casingMesh = FindDeep(searchRoot, "Casing_Mesh");
                if (casingMesh != null)
                {
                    centralMirror = casingMesh.gameObject;
                }
                else
                {
                    Transform casing = FindDeep(searchRoot, "RearviewMirror_Casing");
                    if (casing != null)
                    {
                        centralMirror = casing.gameObject;
                    }
                    else
                    {
                        VRRearviewMirror comp = searchRoot.GetComponentInChildren<VRRearviewMirror>(true);
                        if (comp != null) centralMirror = comp.gameObject;
                    }
                }
            }

            if (centralMirror == null)
            {
                GameObject casingMesh = GameObject.Find("Casing_Mesh");
                if (casingMesh != null)
                {
                    centralMirror = casingMesh;
                }
                else
                {
                    GameObject casing = GameObject.Find("RearviewMirror_Casing");
                    if (casing != null)
                    {
                        centralMirror = casing;
                    }
                    else
                    {
                        VRRearviewMirror comp = UnityEngine.Object.FindAnyObjectByType<VRRearviewMirror>();
                        if (comp != null) centralMirror = comp.gameObject;
                    }
                }
            }

            if (centralMirror != null)
            {
                mirrorTargets.Add(centralMirror);
            }

            // 2. Espejo lateral izquierdo: Buscar específicamente Mirror_Housing bajo SideMirror_Left
            GameObject leftMirror = null;
            if (searchRoot != null)
            {
                Transform leftRoot = FindDeep(searchRoot, "SideMirror_Left");
                if (leftRoot != null)
                {
                    Transform housing = leftRoot.Find("Mirror_Housing") ?? FindDeep(leftRoot, "Mirror_Housing");
                    leftMirror = housing != null ? housing.gameObject : leftRoot.gameObject;
                }
                else
                {
                    VRSideMirror[] sideMirrors = searchRoot.GetComponentsInChildren<VRSideMirror>(true);
                    for (int i = 0; i < sideMirrors.Length; i++)
                    {
                        if (sideMirrors[i].Side == VRSideMirror.MirrorSide.Left)
                        {
                            Transform housing = sideMirrors[i].transform.Find("Mirror_Housing") ?? FindDeep(sideMirrors[i].transform, "Mirror_Housing");
                            leftMirror = housing != null ? housing.gameObject : sideMirrors[i].gameObject;
                            break;
                        }
                    }
                }
            }

            if (leftMirror == null)
            {
                GameObject go = GameObject.Find("SideMirror_Left");
                if (go != null)
                {
                    Transform housing = go.transform.Find("Mirror_Housing") ?? FindDeep(go.transform, "Mirror_Housing");
                    leftMirror = housing != null ? housing.gameObject : go;
                }
                else
                {
                    VRSideMirror[] sideMirrors = UnityEngine.Object.FindObjectsByType<VRSideMirror>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (int i = 0; i < sideMirrors.Length; i++)
                    {
                        if (sideMirrors[i].Side == VRSideMirror.MirrorSide.Left)
                        {
                            Transform housing = sideMirrors[i].transform.Find("Mirror_Housing") ?? FindDeep(sideMirrors[i].transform, "Mirror_Housing");
                            leftMirror = housing != null ? housing.gameObject : sideMirrors[i].gameObject;
                            break;
                        }
                    }
                }
            }

            if (leftMirror != null)
            {
                mirrorTargets.Add(leftMirror);
            }

            // 3. Espejo lateral derecho: Buscar específicamente Mirror_Housing bajo SideMirror_Right
            GameObject rightMirror = null;
            if (searchRoot != null)
            {
                Transform rightRoot = FindDeep(searchRoot, "SideMirror_Right");
                if (rightRoot != null)
                {
                    Transform housing = rightRoot.Find("Mirror_Housing") ?? FindDeep(rightRoot, "Mirror_Housing");
                    rightMirror = housing != null ? housing.gameObject : rightRoot.gameObject;
                }
                else
                {
                    VRSideMirror[] sideMirrors = searchRoot.GetComponentsInChildren<VRSideMirror>(true);
                    for (int i = 0; i < sideMirrors.Length; i++)
                    {
                        if (sideMirrors[i].Side == VRSideMirror.MirrorSide.Right)
                        {
                            Transform housing = sideMirrors[i].transform.Find("Mirror_Housing") ?? FindDeep(sideMirrors[i].transform, "Mirror_Housing");
                            rightMirror = housing != null ? housing.gameObject : sideMirrors[i].gameObject;
                            break;
                        }
                    }
                }
            }

            if (rightMirror == null)
            {
                GameObject go = GameObject.Find("SideMirror_Right");
                if (go != null)
                {
                    Transform housing = go.transform.Find("Mirror_Housing") ?? FindDeep(go.transform, "Mirror_Housing");
                    rightMirror = housing != null ? housing.gameObject : go;
                }
                else
                {
                    VRSideMirror[] sideMirrors = UnityEngine.Object.FindObjectsByType<VRSideMirror>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (int i = 0; i < sideMirrors.Length; i++)
                    {
                        if (sideMirrors[i].Side == VRSideMirror.MirrorSide.Right)
                        {
                            Transform housing = sideMirrors[i].transform.Find("Mirror_Housing") ?? FindDeep(sideMirrors[i].transform, "Mirror_Housing");
                            rightMirror = housing != null ? housing.gameObject : sideMirrors[i].gameObject;
                            break;
                        }
                    }
                }
            }

            if (rightMirror != null)
            {
                mirrorTargets.Add(rightMirror);
            }

            if (mirrorTargets.Count > 0)
            {
                HighlightTargets(mirrorTargets);
            }
            else
            {
                ClearHighlight();
                Debug.LogWarning("[InteractableHighlighter] No se encontraron espejos retrovisores para resaltar.");
            }
        }

        /// <summary>
        /// Busca un interactuable individual por nombre de Transform, tag o tipo de componente.
        /// </summary>
        private GameObject FindSingleTarget(string targetName, Transform searchRoot)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return null;

            GameObject found = null;

            if (searchRoot != null)
            {
                // 1. Búsqueda exacta directa por jerarquía
                Transform exact = searchRoot.Find(targetName);
                if (exact != null) return exact.gameObject;

                // 2. Búsqueda recursiva por nombre de GameObject o Tag en searchRoot
                Transform deep = FindDeep(searchRoot, targetName);
                if (deep != null) return deep.gameObject;

                // 3. Aliases de cabina comunes
                if (targetName.Equals("VRHandbrake", StringComparison.OrdinalIgnoreCase) || targetName.Equals("Handbrake", StringComparison.OrdinalIgnoreCase))
                {
                    Transform hb = FindDeep(searchRoot, "VRHandbrake") ?? FindDeep(searchRoot, "mdl_car02_brake");
                    if (hb != null) return hb.gameObject;
                }

                if (targetName.Equals("VRGearShifter", StringComparison.OrdinalIgnoreCase) || targetName.Equals("GearShifter", StringComparison.OrdinalIgnoreCase))
                {
                    Transform gs = FindDeep(searchRoot, "VRGearShifter") ?? FindDeep(searchRoot, "GearShifter");
                    if (gs != null) return gs.gameObject;
                }

                if (targetName.Equals("VRTurnSignal", StringComparison.OrdinalIgnoreCase) || targetName.Equals("TurnSignal", StringComparison.OrdinalIgnoreCase))
                {
                    Transform ts = FindDeep(searchRoot, "VRTurnSignal") ?? FindDeep(searchRoot, "TurnSignal");
                    if (ts != null) return ts.gameObject;
                }

                if (targetName.Equals("VRHeadlightKnob", StringComparison.OrdinalIgnoreCase) || targetName.Equals("HeadlightKnob", StringComparison.OrdinalIgnoreCase))
                {
                    Transform hk = FindDeep(searchRoot, "VRHeadlightKnob") ?? FindDeep(searchRoot, "mdl_car02_lights_knob");
                    if (hk != null) return hk.gameObject;
                }

                if (targetName.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Transform casing = FindDeep(searchRoot, "Casing_Mesh") ?? FindDeep(searchRoot, "RearviewMirror_Casing") ?? FindDeep(searchRoot, "RearviewMirror_Mount");
                    if (casing != null) return casing.gameObject;
                }

                // 4. Búsqueda recursiva por tipo de Componente en searchRoot
                foreach (Component comp in searchRoot.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && (comp.GetType().Name == targetName ||
                                         comp.GetType().Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return comp.gameObject;
                    }
                }
            }

            // 5. Búsqueda global en escena como fallback
            found = GameObject.Find(targetName);
            if (found != null) return found;

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
            {
                foreach (GameObject root in activeScene.GetRootGameObjects())
                {
                    Transform deep = FindDeep(root.transform, targetName);
                    if (deep != null) return deep.gameObject;

                    foreach (Component comp in root.GetComponentsInChildren<Component>(true))
                    {
                        if (comp != null && (comp.GetType().Name == targetName ||
                                             comp.GetType().Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                        {
                            return comp.gameObject;
                        }
                    }
                }
            }

            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name == name || parent.name.Equals(name, StringComparison.OrdinalIgnoreCase) || parent.CompareTag(name))
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform res = FindDeep(parent.GetChild(i), name);
                if (res != null) return res;
            }

            return null;
        }

        /// <summary>
        /// Desactiva el resaltado actual, limpia los MaterialPropertyBlock de todos los renderers
        /// y vacía la lista de objetivos activos.
        /// </summary>
        public void ClearHighlight()
        {
            if (m_TargetRenderers.Count > 0)
            {
                for (int i = 0; i < m_TargetRenderers.Count; i++)
                {
                    Renderer r = m_TargetRenderers[i];
                    if (r == null) continue;

                    r.GetPropertyBlock(PropertyBlock);
                    PropertyBlock.SetColor(EmissionColorPropertyId, Color.black);
                    PropertyBlock.SetColor(BaseColorPropertyId, Color.white);
                    PropertyBlock.SetColor(ColorPropertyId, Color.white);
                    r.SetPropertyBlock(PropertyBlock);
                    r.SetPropertyBlock(null);
                }

                m_TargetRenderers.Clear();
                m_TargetRendererInfos.Clear();
            }

            m_CurrentTargets.Clear();
            m_IsHighlighting = false;
        }

        private void OnDisable()
        {
            ClearHighlight();
        }
    }
}
