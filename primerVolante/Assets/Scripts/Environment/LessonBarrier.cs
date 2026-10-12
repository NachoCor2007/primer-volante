using System;
using PrimerVolante.VR;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Datos de un contacto entre el auto del jugador y una valla de lección.
    /// </summary>
    public struct LessonBarrierContact
    {
        /// <summary>Valla que avisó.</summary>
        public LessonBarrier Barrier;

        /// <summary>Auto del jugador que la alcanzó.</summary>
        public VehicleController Vehicle;

        /// <summary>Posición del auto en el momento del aviso.</summary>
        public Vector3 Position;

        /// <summary>Velocidad del auto en km/h en el momento del aviso.</summary>
        public float SpeedKmh;
    }

    /// <summary>
    /// Valla de control de una línea de barreras de lección (US30). El prefab tiene un collider sólido que
    /// frena físicamente al auto y un hijo trigger (<c>AlertTrigger</c>) más ancho que lo rodea; este
    /// componente va en el hijo trigger y avisa cuando el auto del jugador lo alcanza.
    /// Solo emite el evento: la falta o el reinicio de la prueba son responsabilidad de quien se suscriba.
    /// </summary>
    [DisallowMultipleComponent]
    public class LessonBarrier : MonoBehaviour
    {
        [Header("Depuración")]
        [Tooltip("Si se activa, escribe un log cada vez que el auto alcanza la valla.")]
        [SerializeField] private bool m_LogEvents = false;

        /// <summary>
        /// Se dispara cuando un collider del auto del jugador entra en la zona de aviso. Un mismo paso puede
        /// disparar varias veces (el auto tiene varios colliders y puede tocar vallas contiguas): quien necesite
        /// un único aviso por pasada debe deduplicar, como hace <see cref="LessonBoundary"/>.
        /// </summary>
        public event Action<LessonBarrierContact> OnVehicleReached;

        // En un collider estático el mensaje de trigger llega al GameObject del collider que es trigger, no a su
        // padre: por eso este componente vive en el hijo AlertTrigger.
        private void OnTriggerEnter(Collider other)
        {
            // Solo el auto del jugador: los NPC no tienen VehicleController y las manos/XR tampoco.
            VehicleController vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle == null) return;

            var contact = new LessonBarrierContact
            {
                Barrier = this,
                Vehicle = vehicle,
                Position = vehicle.transform.position,
                SpeedKmh = vehicle.CurrentSpeedKmh,
            };

            if (m_LogEvents)
            {
                Debug.Log($"[LessonBarrier] 🚧 {name}: el auto alcanzó la valla a {contact.SpeedKmh:F1} km/h en {contact.Position}");
            }

            OnVehicleReached?.Invoke(contact);
        }
    }
}
