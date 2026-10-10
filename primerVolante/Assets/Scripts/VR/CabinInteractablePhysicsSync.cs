using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Mantiene la pose física (PhysX) de los interactuables de cabina alineada con su pose visual.
    /// Con el Rigidbody del vehículo en modo Interpolate, los transforms se mueven suave a la tasa de render,
    /// pero los colliders en PhysX solo avanzan en cada paso de física (50 Hz). A velocidad, el collider queda
    /// adelantado respecto de lo que se ve (hasta ~20 cm a 40 km/h) y los interactores de las manos no encuentran
    /// interactuables chicos como el retrovisor o el botón de balizas.
    /// La interpolación mueve los transforms sin marcarlos como modificados, así que la sincronización automática
    /// de transforms no los envía a PhysX. Antes del XRInteractionManager (-105) este componente marca como
    /// modificado el transform de cada Rigidbody cinemático de cabina (sin alterar su valor), para que la
    /// detección de hover/select use la misma pose que se renderiza. No usa Rigidbody.position: eso haría que el
    /// paso de física siguiente copie la pose al transform y pise cambios legítimos de los controles.
    /// </summary>
    [DefaultExecutionOrder(-290)]
    public class CabinInteractablePhysicsSync : MonoBehaviour
    {
        // Desplazamiento mínimo usado para marcar el transform como modificado; se revierte en el acto.
        private static readonly Vector3 k_Nudge = new Vector3(0f, 1e-4f, 0f);

        private readonly List<Rigidbody> m_Bodies = new List<Rigidbody>();

        /// <summary>
        /// Rigidbodies cinemáticos de cabina que se sincronizan cada frame.
        /// </summary>
        public IReadOnlyList<Rigidbody> Bodies => m_Bodies;

        private void Start()
        {
            CollectBodies();
        }

        /// <summary>
        /// Recolecta los Rigidbody cinemáticos de los interactuables XR hijos de este objeto.
        /// </summary>
        public void CollectBodies()
        {
            m_Bodies.Clear();
            foreach (var interactable in GetComponentsInChildren<XRBaseInteractable>(true))
            {
                Rigidbody rb = interactable.GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic && !m_Bodies.Contains(rb))
                {
                    m_Bodies.Add(rb);
                }
            }
        }

        private void Update()
        {
            foreach (Rigidbody rb in m_Bodies)
            {
                if (rb == null || !rb.gameObject.activeInHierarchy) continue;

                Transform t = rb.transform;
                Vector3 localPosition = t.localPosition;
                t.localPosition = localPosition + k_Nudge;
                t.localPosition = localPosition;
            }
        }
    }
}
