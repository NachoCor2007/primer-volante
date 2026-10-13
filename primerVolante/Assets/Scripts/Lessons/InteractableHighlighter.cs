using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Resalta visualmente un mando interactuable objetivo mediante un glow/brillo amarillo pulsante.
    /// Utiliza MaterialPropertyBlock sobre _EmissionColor para no instanciar materiales ni ensuciar assets.
    /// </summary>
    public class InteractableHighlighter : MonoBehaviour
    {
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

        [Header("Configuración de Resaltado")]
        [Tooltip("Color base de emisión del interactuable a resaltar.")]
        [SerializeField] private Color m_BaseHighlightColor = Color.yellow;

        [Tooltip("Velocidad de oscilación del brillo pulsante.")]
        [SerializeField] private float m_PulseFrequency = 2f;

        private GameObject m_CurrentTarget;
        private readonly List<Renderer> m_TargetRenderers = new List<Renderer>();
        private MaterialPropertyBlock m_PropertyBlock;
        private MaterialPropertyBlock PropertyBlock => m_PropertyBlock ??= new MaterialPropertyBlock();
        private bool m_IsHighlighting = false;

        public bool IsHighlighting => m_IsHighlighting;
        public GameObject CurrentTarget => m_CurrentTarget;

        private void Awake()
        {
            if (m_PropertyBlock == null)
            {
                m_PropertyBlock = new MaterialPropertyBlock();
            }
        }

        private void Update()
        {
            if (!m_IsHighlighting || m_TargetRenderers.Count == 0) return;

            // Oscilación suave entre 0.0 y 1.0 utilizando tiempo no escalado
            float pulse = Mathf.PingPong(Time.unscaledTime * m_PulseFrequency, 1f);
            Color pulseColor = m_BaseHighlightColor * pulse;

            for (int i = 0; i < m_TargetRenderers.Count; i++)
            {
                Renderer r = m_TargetRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(PropertyBlock);
                PropertyBlock.SetColor(EmissionColorPropertyId, pulseColor);
                r.SetPropertyBlock(PropertyBlock);
            }
        }

        /// <summary>
        /// Activa el resaltado pulsante sobre un GameObject objetivo y todos sus renderers hijos.
        /// </summary>
        public void Highlight(GameObject target)
        {
            if (m_CurrentTarget == target && m_IsHighlighting) return;

            ClearHighlight();

            if (target == null) return;

            m_CurrentTarget = target;
            m_TargetRenderers.Clear();
            target.GetComponentsInChildren(true, m_TargetRenderers);

            // Asegurar que la keyword de emisión esté habilitada en los materiales compartidos
            for (int i = 0; i < m_TargetRenderers.Count; i++)
            {
                Renderer r = m_TargetRenderers[i];
                if (r == null) continue;

                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat != null && !mat.IsKeywordEnabled("_EMISSION"))
                    {
                        mat.EnableKeyword("_EMISSION");
                    }
                }
            }

            m_IsHighlighting = true;
        }

        /// <summary>
        /// Busca un interactuable por nombre o tipo de componente y lo resalta.
        /// Garantiza la limpieza previa de cualquier resaltado anterior.
        /// </summary>
        public void HighlightByName(string targetName, Transform searchRoot = null)
        {
            // Siempre limpiar el resaltado previo al iniciar
            ClearHighlight();

            if (string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            GameObject found = null;

            if (searchRoot != null)
            {
                // 1. Búsqueda exacta directa por jerarquía
                Transform exact = searchRoot.Find(targetName);
                if (exact != null)
                {
                    found = exact.gameObject;
                }

                // 2. Búsqueda recursiva por nombre de GameObject o Tag en searchRoot
                if (found == null)
                {
                    foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == targetName ||
                            child.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase) ||
                            child.CompareTag(targetName))
                        {
                            found = child.gameObject;
                            break;
                        }
                    }
                }

                // 3. Búsqueda recursiva por tipo de Componente en searchRoot (ej. "VRRearviewMirror")
                if (found == null)
                {
                    foreach (Component comp in searchRoot.GetComponentsInChildren<Component>(true))
                    {
                        if (comp != null && (comp.GetType().Name == targetName ||
                                             comp.GetType().Name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase)))
                        {
                            found = comp.gameObject;
                            break;
                        }
                    }
                }

                // 4. Aliases comunes de cabina si no se encontró directamente
                if (found == null && targetName.IndexOf("mirror", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == "RearviewMirror_Casing" || child.name == "RearviewMirror_Mount")
                        {
                            found = child.gameObject;
                            break;
                        }
                    }
                }
            }

            // 5. Búsqueda global en escena como fallback
            if (found == null)
            {
                found = GameObject.Find(targetName);
            }

            if (found == null)
            {
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    foreach (GameObject root in activeScene.GetRootGameObjects())
                    {
                        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        {
                            if (child.name == targetName ||
                                child.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                found = child.gameObject;
                                break;
                            }
                        }
                        if (found != null) break;

                        foreach (Component comp in root.GetComponentsInChildren<Component>(true))
                        {
                            if (comp != null && (comp.GetType().Name == targetName ||
                                                 comp.GetType().Name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase)))
                            {
                                found = comp.gameObject;
                                break;
                            }
                        }
                        if (found != null) break;
                    }
                }
            }

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
        /// Desactiva el resaltado actual y limpia los MaterialPropertyBlock.
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
                    r.SetPropertyBlock(PropertyBlock);
                }
                m_TargetRenderers.Clear();
            }

            m_CurrentTarget = null;
            m_IsHighlighting = false;
        }

        private void OnDisable()
        {
            ClearHighlight();
        }
    }
}
