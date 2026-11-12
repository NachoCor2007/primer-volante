using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Apaga el near casting de un <see cref="NearFarInteractor"/> mientras la simulación está pausada, de modo que las manos no
    /// puedan agarrar ni tocar ningún control del habitáculo (volante, palanca, guiños, perilla, espejos, botones). Al pedirlo
    /// además suelta cualquier agarre en curso del interactor, porque con la selección activa el interactor conserva el objeto
    /// aunque ya no lo detecte. No toca las <c>interactionLayers</c> de los interactables (esas las gobierna
    /// <c>CockpitControlLockManager</c> por paso) ni el rayo a UI, que sigue siendo de <see cref="UIOnlyFarCasting"/>.
    /// Mismo patrón que <see cref="UIOnlyFarCasting"/>: componente runtime en el GameObject del interactor con conteo de
    /// solicitantes; el valor original de <c>enableNearCasting</c> se guarda al entrar el primero y se restaura al salir el último.
    /// </summary>
    [DisallowMultipleComponent]
    public class NearCastingPauseBlock : MonoBehaviour
    {
        private readonly HashSet<Object> m_Requesters = new HashSet<Object>();
        private readonly List<Object> m_PruneBuffer = new List<Object>();
        private readonly List<IXRSelectInteractable> m_SelectedBuffer = new List<IXRSelectInteractable>();

        private NearFarInteractor m_Interactor;
        private bool m_Applied;
        private bool m_OriginalEnableNearCasting;

        /// <summary>true mientras hay al menos un solicitante y el near casting está apagado por este componente.</summary>
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
        public static NearCastingPauseBlock Get(NearFarInteractor interactor)
        {
            return interactor != null ? interactor.GetComponent<NearCastingPauseBlock>() : null;
        }

        /// <summary>
        /// Solicita apagar el near casting del interactor y suelta su agarre en curso. Es idempotente por solicitante.
        /// </summary>
        public static void Request(NearFarInteractor interactor, Object requester)
        {
            if (interactor == null || requester == null) return;

            NearCastingPauseBlock component = interactor.GetComponent<NearCastingPauseBlock>();
            if (component == null)
            {
                component = interactor.gameObject.AddComponent<NearCastingPauseBlock>();
                // Estado puramente de runtime: no debe serializarse en la escena.
                component.hideFlags = HideFlags.DontSave;
            }

            component.AddRequester(interactor, requester);
        }

        /// <summary>
        /// Libera la solicitud del interactor. Al salir el último solicitante se restaura el near casting original.
        /// </summary>
        public static void Release(NearFarInteractor interactor, Object requester)
        {
            if (interactor == null || requester == null) return;

            NearCastingPauseBlock component = interactor.GetComponent<NearCastingPauseBlock>();
            if (component != null) component.RemoveRequester(requester);
        }

        private void AddRequester(NearFarInteractor interactor, Object requester)
        {
            m_Requesters.Add(requester);

            if (!m_Applied)
            {
                m_Interactor = interactor;
                m_OriginalEnableNearCasting = interactor.enableNearCasting;
                interactor.enableNearCasting = false;
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

            if (m_Interactor != null) m_Interactor.enableNearCasting = m_OriginalEnableNearCasting;
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
            for (int i = 0; i < m_SelectedBuffer.Count; i++)
            {
                manager.SelectExit(selector, m_SelectedBuffer[i]);
            }
            m_SelectedBuffer.Clear();
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
            // Si un solicitante fue destruido sin liberar, no se deja el near casting trabado en apagado.
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
