using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

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
    /// Además, mientras el modo está activo, el rayo visible (<see cref="CurveVisualController"/> + <c>LineRenderer</c>)
    /// solo se muestra cuando apunta a un elemento de UI; fuera de la UI se oculta tras un pequeño margen.
    /// Se resuelve en este mismo componente (y no en uno hermano) porque comparte el ciclo de vida y el conteo de
    /// solicitantes: el visual se gobierna exactamente mientras el modo UI-only está aplicado.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIOnlyFarCasting : MonoBehaviour
    {
        [Tooltip("Segundos que el rayo permanece visible tras perder el apuntado a la UI (evita parpadeo en bordes y al arrastrar sliders).")]
        [SerializeField] private float m_HideDelay = 0.12f;

        private const string k_UILayerName = "UI";

        private readonly HashSet<Object> m_Requesters = new HashSet<Object>();
        private readonly List<Object> m_PruneBuffer = new List<Object>();

        private NearFarInteractor m_Interactor;
        private CurveInteractionCaster m_Caster;
        private bool m_Applied;
        private bool m_OriginalEnableFarCasting;
        private LayerMask m_OriginalRaycastMask;

        private CurveVisualController m_Visual;
        private LineRenderer m_Line;
        private bool m_VisualLookupDone;
        private bool m_VisualCaptured;
        private bool m_OriginalVisualEnabled;
        private bool m_OriginalLineEnabled;
        private bool m_VisualShown;
        private float m_LastUITime = float.NegativeInfinity;

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
            m_LastUITime = float.NegativeInfinity;
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

            RestoreVisual();
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

        private void LateUpdate()
        {
            if (!m_Applied || m_Interactor == null) return;

            if (!m_VisualLookupDone) FindVisual();
            if (m_Visual == null || m_Line == null) return;

            if (!m_VisualCaptured)
            {
                m_OriginalVisualEnabled = m_Visual.enabled;
                m_OriginalLineEnabled = m_Line.enabled;
                m_VisualShown = true;
                m_VisualCaptured = true;
            }

            // El interactor calcula el endpoint en su Update, por lo que acá ya es el valor del frame.
            if (m_Interactor.TryGetCurveEndPoint(out _) == EndPointType.UI) m_LastUITime = Time.unscaledTime;

            bool shouldShow = Time.unscaledTime - m_LastUITime <= m_HideDelay;
            if (shouldShow == m_VisualShown) return;

            m_VisualShown = shouldShow;
            if (shouldShow)
            {
                m_Visual.enabled = m_OriginalVisualEnabled;
                m_Line.enabled = m_OriginalLineEnabled;
            }
            else
            {
                // CurveVisualController.OnDisable no apaga el LineRenderer, hay que hacerlo explícitamente.
                m_Visual.enabled = false;
                m_Line.enabled = false;
            }
        }

        private void FindVisual()
        {
            m_VisualLookupDone = true;
            CurveVisualController[] visuals = m_Interactor.GetComponentsInChildren<CurveVisualController>(true);
            for (int i = 0; i < visuals.Length; i++)
            {
                if ((Object)visuals[i].curveInteractionDataProvider == m_Interactor)
                {
                    m_Visual = visuals[i];
                    break;
                }
            }

            m_Line = m_Visual != null ? m_Visual.GetComponentInChildren<LineRenderer>(true) : null;
            if (m_Visual == null || m_Line == null)
            {
                Debug.LogWarning($"[UIOnlyFarCasting] No se encontró CurveVisualController/LineRenderer para '{m_Interactor.name}': " +
                                 "el rayo no se ocultará al apuntar fuera de la UI.", m_Interactor);
            }
        }

        /// <summary>Devuelve el CurveVisualController gobernado (null si no se encontró).</summary>
        public CurveVisualController Visual => m_Visual;

        /// <summary>true si el rayo está oculto por este componente en este momento.</summary>
        public bool VisualHidden => m_VisualCaptured && !m_VisualShown;

        private void RestoreVisual()
        {
            if (!m_VisualCaptured) return;

            if (m_Visual != null) m_Visual.enabled = m_OriginalVisualEnabled;
            if (m_Line != null) m_Line.enabled = m_OriginalLineEnabled;
            m_VisualCaptured = false;
            m_VisualShown = false;
        }

        private void OnDisable()
        {
            // Si el interactor se desactiva, no se deja el visual apagado; LateUpdate lo vuelve a gobernar al reactivarse.
            RestoreVisual();
        }

        private void OnDestroy()
        {
            Restore();
        }
    }
}
