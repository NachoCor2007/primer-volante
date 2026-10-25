namespace PrimerVolante.Evaluation
{
    /// <summary>
    /// Interfaz de escritura: por aquí cualquier sistema informa una falta sin saber quién la guarda. Los
    /// detectores no la usan directamente sino a través de <see cref="InfractionReporting"/>.
    /// </summary>
    public interface IInfractionReporter
    {
        /// <summary>Informa una infracción. No debe interrumpir la simulación.</summary>
        void Report(in InfractionReport report);
    }
}
