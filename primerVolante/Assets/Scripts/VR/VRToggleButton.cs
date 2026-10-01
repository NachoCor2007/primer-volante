using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Alterna el estado visual (color) de un botón poke de XRI al seleccionarlo.
    /// El movimiento físico del botón lo maneja XRPokeFollowAffordance en un objeto aparte;
    /// este componente solo gestiona el estado ON/OFF y su color asociado.
    /// </summary>
    public class VRToggleButton : MonoBehaviour
    {
        [Header("Visual State")]
        [Tooltip("Renderer cuyo color cambia según el estado del botón. Si se deja vacío, se busca en los hijos.")]
        public Renderer targetRenderer;

        [Tooltip("Color del botón cuando está apagado/no apretado.")]
        public Color idleColor = Color.white;

        [Tooltip("Color del botón cuando está encendido/apretado.")]
        public Color toggledColor = Color.red;

        [Header("Debugging / Logging")]
        [Tooltip("Imprime logs detallados en la consola de Unity sobre cada contacto y conmutación.")]
        public bool enableVerboseLogs = true;

        [Header("Events")]
        public UnityEvent OnToggledOn;
        public UnityEvent OnToggledOff;

        private bool _isToggled = false;
        private Material _instancedMaterial;

        public bool IsToggled => _isToggled;

        private void Start()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();

            if (targetRenderer != null)
            {
                _instancedMaterial = targetRenderer.material;
                _instancedMaterial.color = idleColor;
            }
            else if (enableVerboseLogs)
            {
                Debug.LogWarning($"[VRToggleButton:{gameObject.name}] No se encontró Renderer para aplicar el color de estado.");
            }

            XRSimpleInteractable interactable = GetComponent<XRSimpleInteractable>();
            if (interactable != null)
            {
                interactable.selectEntered.AddListener(OnXRISelectEntered);
                interactable.firstHoverEntered.AddListener(OnXRIHoverEntered);
                interactable.lastHoverExited.AddListener(OnXRIHoverExited);
                if (enableVerboseLogs)
                    Debug.Log($"[VRToggleButton:{gameObject.name}] 🔌 Registrados listeners de XRI (Select & Hover) en XRSimpleInteractable.");
            }
            else if (enableVerboseLogs)
            {
                Debug.LogWarning($"[VRToggleButton:{gameObject.name}] No se encontró XRSimpleInteractable en este objeto.");
            }
        }

        private void OnDestroy()
        {
            XRSimpleInteractable interactable = GetComponent<XRSimpleInteractable>();
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(OnXRISelectEntered);
                interactable.firstHoverEntered.RemoveListener(OnXRIHoverEntered);
                interactable.lastHoverExited.RemoveListener(OnXRIHoverExited);
            }

            if (_instancedMaterial != null)
                Destroy(_instancedMaterial);
        }

        /// <summary>
        /// Alterna el estado del botón (ON/OFF) y actualiza su color.
        /// </summary>
        public void ToggleButton()
        {
            SetToggled(!_isToggled, invokeEvents: true);
        }

        /// <summary>
        /// Establece el estado del botón y actualiza su material visual.
        /// </summary>
        public void SetToggled(bool toggled, bool invokeEvents = true)
        {
            if (_isToggled == toggled) return;
            _isToggled = toggled;

            if (_instancedMaterial == null && targetRenderer != null)
                _instancedMaterial = targetRenderer.material;

            if (_instancedMaterial != null)
                _instancedMaterial.color = _isToggled ? toggledColor : idleColor;

            if (invokeEvents)
            {
                if (_isToggled)
                {
                    if (enableVerboseLogs)
                        Debug.Log($"[VRToggleButton:{gameObject.name}] 🟢 Botón ENCENDIDO");
                    OnToggledOn?.Invoke();
                }
                else
                {
                    if (enableVerboseLogs)
                        Debug.Log($"[VRToggleButton:{gameObject.name}] 🔴 Botón APAGADO");
                    OnToggledOff?.Invoke();
                }
            }
        }

        #region XRI Poke / Select Handlers
        public void OnXRISelectEntered(SelectEnterEventArgs args)
        {
            if (enableVerboseLogs)
                Debug.Log($"[VRToggleButton:{gameObject.name}] 👈 XRI SelectEntered recibido de interactor '{args.interactorObject?.transform.name}' -> Ejecutando ToggleButton()");

            ToggleButton();
        }

        public void OnXRIHoverEntered(HoverEnterEventArgs args)
        {
            if (enableVerboseLogs)
                Debug.Log($"[VRToggleButton:{gameObject.name}] 👈 XRI HoverEntered de '{args.interactorObject?.transform.name}'");
        }

        public void OnXRIHoverExited(HoverExitEventArgs args)
        {
            if (enableVerboseLogs)
                Debug.Log($"[VRToggleButton:{gameObject.name}] 👉 XRI HoverExited de '{args.interactorObject?.transform.name}'");
        }
        #endregion
    }
}
