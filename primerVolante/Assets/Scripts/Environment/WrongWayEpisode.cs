namespace PrimerVolante.Environment
{
    /// <summary>
    /// Estado puro de un episodio de contramano (sin escena, testeable). Lo usa <see cref="WrongWayDetector"/>:
    /// acumula la distancia recorrida en contramano y decide cuándo informar la falta, una sola vez por episodio.
    /// </summary>
    /// <remarks>
    /// El episodio se cierra (y se rearma) solo después de <see cref="RearmDistanceM"/> metros seguidos a mano.
    /// Por eso dos esquivas cortas separadas por menos que esa distancia de manejo a mano <b>se suman</b>: es
    /// intencional, para que esquivar un auto estacionado dos veces seguidas en la misma cuadra cuente como un
    /// solo episodio largo en contramano y no como dos maniobras inocentes.
    /// </remarks>
    public sealed class WrongWayEpisode
    {
        private float m_WrongDistanceM;
        private float m_CorrectDistanceM;
        private bool m_Reported;

        /// <summary>Metros en contramano antes de informar la falta.</summary>
        public float WrongWayDistanceM { get; set; }

        /// <summary>Metros a mano necesarios para cerrar el episodio.</summary>
        public float RearmDistanceM { get; set; }

        /// <summary>Metros acumulados en contramano en el episodio en curso.</summary>
        public float WrongDistanceM => m_WrongDistanceM;

        /// <summary>Metros a mano acumulados desde la última vez en contramano.</summary>
        public float CorrectDistanceM => m_CorrectDistanceM;

        /// <summary>true si el episodio en curso ya informó su falta.</summary>
        public bool Reported => m_Reported;

        public WrongWayEpisode(float wrongWayDistanceM, float rearmDistanceM)
        {
            WrongWayDistanceM = wrongWayDistanceM;
            RearmDistanceM = rearmDistanceM;
        }

        /// <summary>
        /// Avanza el episodio con la clasificación de este paso y la distancia recorrida en él. Devuelve
        /// <c>true</c> exactamente una vez por episodio, cuando la distancia en contramano supera
        /// <see cref="WrongWayDistanceM"/>.
        /// </summary>
        public bool Step(WrongWayClassification classification, float distanceM)
        {
            if (distanceM < 0f) distanceM = 0f;

            switch (classification)
            {
                case WrongWayClassification.Wrong:
                    m_WrongDistanceM += distanceM;
                    m_CorrectDistanceM = 0f;
                    if (!m_Reported && m_WrongDistanceM > WrongWayDistanceM)
                    {
                        m_Reported = true;
                        return true;
                    }

                    return false;

                case WrongWayClassification.Correct:
                    m_CorrectDistanceM += distanceM;
                    if (m_CorrectDistanceM > RearmDistanceM)
                    {
                        // Cierra el episodio: el próximo tramo en contramano vuelve a contar desde cero.
                        m_WrongDistanceM = 0f;
                        m_CorrectDistanceM = 0f;
                        m_Reported = false;
                    }

                    return false;

                default:
                    return false;
            }
        }

        /// <summary>Descarta el episodio en curso (suspensión, exención o apagado del detector).</summary>
        public void Reset()
        {
            m_WrongDistanceM = 0f;
            m_CorrectDistanceM = 0f;
            m_Reported = false;
        }
    }
}
