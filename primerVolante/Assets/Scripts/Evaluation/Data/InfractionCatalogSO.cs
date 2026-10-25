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

        /// <summary>Definiciones del catálogo, en el orden del asset.</summary>
        public IReadOnlyList<InfractionDefinitionSO> Definitions => m_Definitions;

        /// <summary>
        /// Busca la definición con ese id. Devuelve false si el id es null/vacío o no está en el catálogo. Recorre
        /// la lista en cada consulta (son pocas definiciones y pocas faltas): así un id editado en un asset de
        /// definición se ve al instante, sin índices que invalidar.
        /// </summary>
        public bool TryGet(string id, out InfractionDefinitionSO definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(id)) return false;

            // Ante un id repetido gana la primera definición; OnValidate ya avisó.
            foreach (InfractionDefinitionSO candidate in m_Definitions)
            {
                if (candidate != null && candidate.Id == id)
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
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
