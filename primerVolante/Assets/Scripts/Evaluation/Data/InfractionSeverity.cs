namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Gravedad de una infracción. El gestor lleva un contador independiente para cada una.
    /// </summary>
    public enum InfractionSeverity
    {
        /// <summary>Falta leve.</summary>
        Minor = 0,

        /// <summary>Falta grave.</summary>
        Major = 1
    }
}
