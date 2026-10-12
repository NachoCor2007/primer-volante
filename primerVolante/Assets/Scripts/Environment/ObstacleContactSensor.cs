using System.Collections.Generic;
using PrimerVolante.VR;
using UnityEngine;
using UnityEngine.Audio;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Sensor de contactos del auto del jugador contra obstáculos (US33). Va en la raíz del auto, junto al
    /// Rigidbody dinámico, que es el que recibe los mensajes de colisión de los obstáculos estáticos (conos, autos
    /// estacionados) y de los cinemáticos (NPC). Informa cada contacto al <see cref="ObstacleCollisionDetector"/>
    /// del obstáculo, reproduce el sonido del impacto y frena la velocidad interna del
    /// <see cref="VehicleController"/> para que el auto no siga "empujando" contra el obstáculo.
    /// </summary>
    /// <remarks>
    /// Los controles de cabina (volante, palancas, botones) tienen su propio Rigidbody cinemático, así que sus
    /// colisiones no llegan a este componente. Los contactos con calle, cordones, vallas de lección y edificios no
    /// tienen <see cref="ObstacleCollisionDetector"/> y se ignoran.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    public class ObstacleContactSensor : MonoBehaviour
    {
        // Prioridad de la fuente: más alta (número menor) que la rodadura, que suena todo el tiempo.
        private const int k_SourcePriority = 64;

        [Header("Audio")]
        [Tooltip("Clips y volúmenes de impacto por tipo y gravedad. Sin perfil el evento se emite igual y no suena.")]
        [SerializeField] private ObstacleImpactAudioProfile m_AudioProfile;

        [Tooltip("Fuente 3D del impacto. Se mueve al punto de contacto en cada impacto.")]
        [SerializeField] private AudioSource m_ImpactSource;

        [Tooltip("Grupo del mixer de la fuente (sonidos exteriores: Road).")]
        [SerializeField] private AudioMixerGroup m_MixerGroup;

        private VehicleController m_Vehicle;

        // Obstáculos tocados ahora (una entrada por contacto abierto), para cerrarlos si el sensor se apaga.
        private readonly List<ObstacleCollisionDetector> m_Touching = new List<ObstacleCollisionDetector>();

        public ObstacleImpactAudioProfile AudioProfile => m_AudioProfile;
        public AudioSource ImpactSource => m_ImpactSource;
        public AudioMixerGroup MixerGroup => m_MixerGroup;

        private void Awake()
        {
            m_Vehicle = GetComponent<VehicleController>();

            if (m_ImpactSource != null)
            {
                float min = m_AudioProfile != null ? m_AudioProfile.MinDistance : 1f;
                float max = m_AudioProfile != null ? m_AudioProfile.MaxDistance : 30f;
                VehicleAudioUtil.ConfigureSource(m_ImpactSource, false, min, max, m_MixerGroup, k_SourcePriority);
            }
        }

        private void OnDisable()
        {
            // Si el auto se apaga, OnCollisionExit puede no llegar: los obstáculos no pueden quedar "tocados".
            foreach (ObstacleCollisionDetector detector in m_Touching)
            {
                if (detector != null) detector.EndContact(m_Vehicle);
            }

            m_Touching.Clear();
        }

        private void OnCollisionEnter(Collision collision)
        {
            ObstacleCollisionDetector detector = FindDetector(collision);
            if (detector == null) return;

            // Un obstáculo desactivado o destruido en pleno contacto ya reseteó su contador y OnCollisionExit puede
            // no haber llegado: se descartan sus entradas para que no desbalanceen el contador al reactivarse.
            m_Touching.RemoveAll(touching => touching == null || !touching.isActiveAndEnabled);
            m_Touching.Add(detector);

            // La gravedad se decide con la velocidad del jugador antes de que el ajuste de abajo la baje.
            ObstacleImpact? impact = detector.BeginContact(m_Vehicle, collision);
            if (impact.HasValue) PlayImpactSound(impact.Value);

            LimitSpeedIfPressing(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (FindDetector(collision) == null) return;

            LimitSpeedIfPressing(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            ObstacleCollisionDetector detector = FindDetector(collision);
            if (detector == null) return;

            // Solo cierra los contactos que este sensor abrió, así el contador del obstáculo no se desbalancea.
            if (m_Touching.Remove(detector)) detector.EndContact(m_Vehicle);
        }

        // Solo se limita la velocidad si el auto empuja contra la superficie. Al separarse o rozar de costado se deja
        // que avance libre: la velocidad medida ya descuenta la fricción del suelo y, si se la usara como tope en
        // esos casos, la velocidad interna se iría a 0 en cada paso y el auto quedaría clavado al obstáculo.
        private void LimitSpeedIfPressing(Collision collision)
        {
            Vector3 travel = m_Vehicle.GetTravelDirection();
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (ObstacleImpactMath.IsPressingInto(travel, collision.GetContact(i).normal))
                {
                    m_Vehicle.LimitSpeedToActualMotion();
                    return;
                }
            }
        }

        private static ObstacleCollisionDetector FindDetector(Collision collision)
        {
            Collider other = collision.collider;
            return other != null ? other.GetComponentInParent<ObstacleCollisionDetector>() : null;
        }

        private void PlayImpactSound(ObstacleImpact impact)
        {
            if (m_AudioProfile == null || m_ImpactSource == null) return;

            AudioClip clip = m_AudioProfile.GetClip(impact.Kind, impact.Severity, out float volume);
            if (clip == null) return;

            m_ImpactSource.transform.position = impact.Point;
            VehicleAudioUtil.PlayOneShot(m_ImpactSource, clip, m_AudioProfile.ScaleVolumeBySpeed(volume, impact.PlayerSpeedKmh));
        }
    }
}
