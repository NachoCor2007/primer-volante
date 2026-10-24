using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Catálogo de todas las definiciones de infracción disponibles. El gestor lo consulta por id.
    /// </summary>
    [CreateAssetMenu(fileName = "InfractionCatalog", menuName = "Primer Volante/Evaluation/Infraction Catalog", order = 1)]
    public class InfractionCatalogSO : ScriptableObject
    {
        [Tooltip("Definiciones de infracción. Los ids deben ser únicos y no vacíos.")]
        [SerializeField] private List<InfractionDefinitionSO> m_Definitions = new List<InfractionDefinitionSO>();

        // Índice por id, armado de forma perezosa. No se serializa.
        [System.NonSerialized] private Dictionary<string, InfractionDefinitionSO> m_ById;

        /// <summary>Definiciones del catálogo, en el orden del asset.</summary>
        public IReadOnlyList<InfractionDefinitionSO> Definitions => m_Definitions;

        /// <summary>
        /// Busca la definición con ese id. Devuelve false si el id es null/vacío o no está en el catálogo.
        /// </summary>
        public bool TryGet(string id, out InfractionDefinitionSO definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(id)) return false;

            if (m_ById == null) BuildIndex();
            return m_ById.TryGetValue(id, out definition);
        }

        private void BuildIndex()
        {
            m_ById = new Dictionary<string, InfractionDefinitionSO>(m_Definitions.Count);
            foreach (InfractionDefinitionSO definition in m_Definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id)) continue;

                // Ante un id repetido gana la primera definición; OnValidate ya avisó.
                if (!m_ById.ContainsKey(definition.Id)) m_ById.Add(definition.Id, definition);
            }
        }

        private void OnEnable()
        {
            m_ById = null;
        }

        private void OnValidate()
        {
            m_ById = null;

            var seen = new HashSet<string>();
            foreach (InfractionDefinitionSO definition in m_Definitions)
            {
                if (definition == null)
                {
                    Debug.LogWarning($"[InfractionCatalogSO] ⚠️ {name}: hay una entrada vacía en la lista.", this);
                    continue;
                }

                if (string.IsNullOrEmpty(definition.Id))
                {
                    Debug.LogWarning($"[InfractionCatalogSO] ⚠️ {name}: la definición '{definition.name}' no tiene id.", this);
                }
                else if (!seen.Add(definition.Id))
                {
                    Debug.LogWarning($"[InfractionCatalogSO] ⚠️ {name}: el id '{definition.Id}' está repetido ('{definition.name}').", this);
                }
            }
        }
    }
}
