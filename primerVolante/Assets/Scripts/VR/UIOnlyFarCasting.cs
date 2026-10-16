using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Habilita el cast a distancia de un <see cref="NearFarInteractor"/> restringido a la capa UI.
    /// Se agrega en runtime al GameObject del interactor. Mientras haya al menos un solicitante,
    /// el rayo a distancia queda activo pero su <c>raycastMask</c> solo incluye la capa "UI", de modo que
    /// funciona con los botones/sliders de los canvas (que deben estar en esa capa: la máscara también filtra la UI)
    /// e ignora los colliders de la cabina (volante, espejos, palanca, etc.). El near caster no se toca.
    /// Varios solicitantes (HUD de la lección, menú in-game) pueden convivir: el estado original se guarda
    /// al entrar el primero y se restaura al salir el último.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIOnlyFarCasting : MonoBehaviour
    {
        private const string k_UILayerName = "UI";

        private readonly HashSet<Object> m_Requesters = new HashSet<Object>();
        private readonly List<Object> m_PruneBuffer = new List<Object>();

        private NearFarInteractor m_Interactor;
        private CurveInteractionCaster m_Caster;
        private bool m_Applied;
        private bool m_OriginalEnableFarCasting;
        private LayerMask m_OriginalRaycastMask;

        /// <summary>true mientras hay al menos un solicitante y el rayo está restringido a la UI.</summary>
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

        /// <summary>
        /// Devuelve el componente del interactor indicado (null si no se le solicitó nunca).
        /// </summary>
        public static UIOnlyFarCasting Get(NearFarInteractor interactor)
        {
            return interactor != null ? interactor.GetComponent<UIOnlyFarCasting>() : null;
        }

        /// <summary>
        /// Solicita el rayo a distancia restringido a la UI para el interactor. Es idempotente por solicitante.
        /// </summary>
        public static void Request(NearFarInteractor interactor, Object requester)
        {
            if (interactor == null || requester == null) return;

            UIOnlyFarCasting component = interactor.GetComponent<UIOnlyFarCasting>();
            if (component == null)
            {
                component = interactor.gameObject.AddComponent<UIOnlyFarCasting>();
                // Estado puramente de runtime: no debe serializarse en la escena.
                component.hideFlags = HideFlags.DontSave;
            }

            component.AddRequester(interactor, requester);
        }

        /// <summary>
        /// Libera la solicitud del interactor. Al salir el último solicitante se restauran los valores originales.
        /// </summary>
        public static void Release(NearFarInteractor interactor, Object requester)
        {
            if (interactor == null || requester == null) return;

            UIOnlyFarCasting component = interactor.GetComponent<UIOnlyFarCasting>();
            if (component != null)
            {
                component.RemoveRequester(requester);
            }
        }

        private void AddRequester(NearFarInteractor interactor, Object requester)
        {
            m_Requesters.Add(requester);
            if (m_Applied) return;

            m_Interactor = interactor;
            m_Caster = interactor.farInteractionCaster as CurveInteractionCaster;
            m_OriginalEnableFarCasting = interactor.enableFarCasting;

            if (m_Caster != null)
            {
                m_OriginalRaycastMask = m_Caster.raycastMask;
                m_Caster.raycastMask = LayerMask.GetMask(k_UILayerName);
            }
            else
            {
                Debug.LogWarning($"[UIOnlyFarCasting] El far caster de '{interactor.name}' no es un CurveInteractionCaster: " +
                                 "se habilita el cast a distancia sin restringirlo a la capa UI.", interactor);
            }

            interactor.enableFarCasting = true;
            m_Applied = true;
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

            if (m_Interactor != null) m_Interactor.enableFarCasting = m_OriginalEnableFarCasting;
            if (m_Caster != null) m_Caster.raycastMask = m_OriginalRaycastMask;
            m_Applied = false;
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
            // Si un solicitante fue destruido sin liberar, no se deja el rayo trabado en modo UI.
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
