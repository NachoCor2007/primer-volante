using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Impide que un interactor de las manos agarre o toque algún control del habitáculo mientras la simulación está pausada,
    /// y suelta cualquier selección en curso del interactor (con la selección activa el interactor conserva el objeto aunque ya
    /// no lo detecte). Lo gobierna <see cref="CabinPauseInteractionBlock"/> según <see cref="SimulationPause"/>.
    /// <list type="bullet">
    /// <item><description><see cref="NearFarInteractor"/>: apaga <c>enableNearCasting</c>. El rayo a UI del menú sigue siendo de <see cref="UIOnlyFarCasting"/>.</description></item>
    /// <item><description>Cualquier otro (<c>XRPokeInteractor</c>, <c>XRDirectInteractor</c>, interactores de hand tracking): pone sus <c>interactionLayers</c> en 0, así no puede
    /// hover ni select sobre ningún interactable, y el poke sobre la UI (que no pasa por las capas) sigue funcionando.</description></item>
    /// </list>
    /// No toca las <c>interactionLayers</c> de los interactables (esas las gobierna <c>CockpitControlLockManager</c> por paso).
    /// Mismo patrón que <see cref="UIOnlyFarCasting"/>: componente runtime en el GameObject del interactor con conteo de
    /// solicitantes; el estado original se guarda al entrar el primero y se restaura al salir el último.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractorPauseBlock : MonoBehaviour
    {
        /// <summary>
        /// true mientras el bloqueo suelta las selecciones de un interactor por la pausa. Los controles que encastran al soltarse
        /// (p. ej. <see cref="VRGearShifter"/>) lo consultan para volver a la posición que tenían en vez de encastrar donde los
        /// dejó la mano.
        /// </summary>
        public static bool IsReleasingForPause { get; private set; }

        private readonly HashSet<Object> m_Requesters = new HashSet<Object>();
        private readonly List<Object> m_PruneBuffer = new List<Object>();
        private readonly List<IXRSelectInteractable> m_SelectedBuffer = new List<IXRSelectInteractable>();

        private XRBaseInteractor m_Interactor;
        private bool m_Applied;
        private bool m_OriginalEnableNearCasting;
        private InteractionLayerMask m_OriginalInteractionLayers;

        /// <summary>true mientras hay al menos un solicitante y el interactor está bloqueado por este componente.</summary>
        public bool IsActive => m_Applied;

        /// <summary>Cantidad de solicitantes vigentes (sin contar los destruidos).</summary>
        public int ActiveRequesterCount
        {
            get
            {
                PruneDestroyedRequesters();
                return m_Requesters.Count;
            }
        }

        /// <summary>Devuelve el componente del interactor indicado (null si no se le solicitó nunca).</summary>
        public static InteractorPauseBlock Get(XRBaseInteractor interactor)
        {
            return interactor != null ? interactor.GetComponent<InteractorPauseBlock>() : null;
        }

        /// <summary>
        /// Solicita bloquear el interactor y suelta su agarre en curso. Es idempotente por solicitante.
        /// </summary>
        public static void Request(XRBaseInteractor interactor, Object requester)
        {
            if (interactor == null || requester == null) return;

            InteractorPauseBlock component = interactor.GetComponent<InteractorPauseBlock>();
            if (component == null)
            {
                component = interactor.gameObject.AddComponent<InteractorPauseBlock>();
                // Estado puramente de runtime: no debe serializarse en la escena.
                component.hideFlags = HideFlags.DontSave;
            }

            component.AddRequester(interactor, requester);
        }

        /// <summary>
        /// Libera la solicitud del interactor. Al salir el último solicitante se restaura el estado original.
        /// </summary>
        public static void Release(XRBaseInteractor interactor, Object requester)
        {
            if (interactor == null || requester == null) return;

            InteractorPauseBlock component = interactor.GetComponent<InteractorPauseBlock>();
            if (component != null) component.RemoveRequester(requester);
        }

        private void AddRequester(XRBaseInteractor interactor, Object requester)
        {
            m_Requesters.Add(requester);

            if (!m_Applied)
            {
                m_Interactor = interactor;
                if (interactor is NearFarInteractor nearFar)
                {
                    m_OriginalEnableNearCasting = nearFar.enableNearCasting;
                    nearFar.enableNearCasting = false;
                }
                else
                {
                    m_OriginalInteractionLayers = interactor.interactionLayers;
                    interactor.interactionLayers = 0;
                }
                m_Applied = true;
            }

            // Un agarre que empezó antes de la pausa se suelta en cada solicitud (idempotente: sin selección no hace nada).
            DropCurrentSelection();
        }

        private void RemoveRequester(Object requester)
        {
            m_Requesters.Remove(requester);
            PruneDestroyedRequesters();
            if (m_Requesters.Count == 0) Restore();
        }

        private void Restore()
        {
            if (!m_Applied) return;

            if (m_Interactor != null)
            {
                if (m_Interactor is NearFarInteractor nearFar) nearFar.enableNearCasting = m_OriginalEnableNearCasting;
                else m_Interactor.interactionLayers = m_OriginalInteractionLayers;
            }
            m_Applied = false;
        }

        // Suelta lo que el interactor tiene agarrado con la API pública del manager (SelectExit respeta el ciclo de eventos normal).
        private void DropCurrentSelection()
        {
            if (m_Interactor == null) return;

            XRInteractionManager manager = m_Interactor.interactionManager;
            if (manager == null) return;

            IXRSelectInteractor selector = m_Interactor;
            m_SelectedBuffer.Clear();
            m_SelectedBuffer.AddRange(selector.interactablesSelected);
            if (m_SelectedBuffer.Count == 0) return;

            IsReleasingForPause = true;
            try
            {
                for (int i = 0; i < m_SelectedBuffer.Count; i++)
                {
                    manager.SelectExit(selector, m_SelectedBuffer[i]);
                }
            }
            finally
            {
                IsReleasingForPause = false;
                m_SelectedBuffer.Clear();
            }
        }

        private void PruneDestroyedRequesters()
        {
            if (m_Requesters.Count == 0) return;

            m_PruneBuffer.Clear();
            foreach (Object requester in m_Requesters)
            {
                if (requester == null) m_PruneBuffer.Add(requester);
            }

            for (int i = 0; i < m_PruneBuffer.Count; i++)
            {
                m_Requesters.Remove(m_PruneBuffer[i]);
            }
        }

        private void Update()
        {
            // Si un solicitante fue destruido sin liberar, no se deja el interactor trabado en bloqueado.
            if (!m_Applied) return;

            PruneDestroyedRequesters();
            if (m_Requesters.Count == 0) Restore();
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
