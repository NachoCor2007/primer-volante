using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Luz de guiño individual. Expone un simple SetOn(bool) sobre un Renderer,
    /// de forma que TurnSignalLightController no dependa de si la luz es un
    /// placeholder o un submesh real separado del modelo del vehículo.
    /// </summary>
    public class BlinkerLight : MonoBehaviour
    {
        [Tooltip("Renderer de la luz. Si se deja vacío, se busca en este mismo objeto.")]
        [SerializeField] private Renderer m_Renderer;

        [Tooltip("Color del material cuando la luz está apagada.")]
        [SerializeField] private Color m_OffColor = new Color(0.35f, 0.28f, 0f);

        [Tooltip("Color emisivo del material cuando la luz está encendida.")]
        [SerializeField] private Color m_OnColor = new Color(1f, 0.55f, 0f);

        [Tooltip("Intensidad del color emisivo al encender.")]
        [SerializeField] private float m_EmissionIntensity = 2f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Material m_InstancedMaterial;

        private void Awake()
        {
            if (m_Renderer == null)
                m_Renderer = GetComponent<Renderer>();

            if (m_Renderer != null)
            {
                m_InstancedMaterial = m_Renderer.material;
                ApplyState(false);
            }
        }

        private void OnDestroy()
        {
            if (m_InstancedMaterial != null)
                Destroy(m_InstancedMaterial);
        }

        public void SetOn(bool on)
        {
            ApplyState(on);
        }

        private void ApplyState(bool on)
        {
            if (m_InstancedMaterial == null) return;

            if (on)
            {
                m_InstancedMaterial.color = m_OnColor;
                m_InstancedMaterial.EnableKeyword("_EMISSION");
                m_InstancedMaterial.SetColor(EmissionColorId, m_OnColor * m_EmissionIntensity);
            }
            else
            {
                m_InstancedMaterial.color = m_OffColor;
                m_InstancedMaterial.DisableKeyword("_EMISSION");
                m_InstancedMaterial.SetColor(EmissionColorId, Color.black);
            }
        }
    }
}
