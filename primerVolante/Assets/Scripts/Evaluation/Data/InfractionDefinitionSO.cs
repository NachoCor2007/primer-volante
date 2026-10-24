using UnityEngine;

namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Definición de un tipo de infracción: id estable, título corto para el tablero (US24), plantilla de
    /// descripción para el reporte final (US26) y gravedad. Es solo dato de autoría: el asset no guarda estado
    /// de runtime (los registros viven en <see cref="InfractionManager"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "Infraction_NEW", menuName = "Primer Volante/Evaluation/Infraction Definition", order = 0)]
    public class InfractionDefinitionSO : ScriptableObject
    {
        [Tooltip("Id estable de la infracción: uno de InfractionIds (ej. SPEED_LIMIT).")]
        [SerializeField] private string m_Id;

        [Tooltip("Título corto que muestra el tablero. Pocas palabras y sin parámetros.")]
        [SerializeField] private string m_ShortTitle;

        [Tooltip("Descripción completa para el reporte final, con marcadores {clave} que se reemplazan por los parámetros del reporte.")]
        [TextArea(2, 5)]
        [SerializeField] private string m_DescriptionTemplate;

        [Tooltip("Falta leve (Minor) o grave (Major).")]
        [SerializeField] private InfractionSeverity m_Severity = InfractionSeverity.Minor;

        /// <summary>Id estable de la infracción.</summary>
        public string Id => m_Id;

        /// <summary>Título corto para el tablero.</summary>
        public string ShortTitle => m_ShortTitle;

        /// <summary>Plantilla de la descripción completa, con marcadores <c>{clave}</c>.</summary>
        public string DescriptionTemplate => m_DescriptionTemplate;

        /// <summary>Gravedad de la infracción.</summary>
        public InfractionSeverity Severity => m_Severity;
    }
}
