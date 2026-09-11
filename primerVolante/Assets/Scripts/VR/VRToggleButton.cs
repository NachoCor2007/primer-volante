using UnityEngine;
using UnityEngine.Events;

namespace PrimerVolante.VR
{
    public class VRToggleButton : MonoBehaviour
    {
        [Header("Button Settings")]
        [Tooltip("La distancia en unidades locales que se hunde el botón al presionarlo.")]
        public float pressDistance = 0.02f;
        [Tooltip("El eje local en el que se mueve el botón (usualmente Z o Y local).")]
        public Vector3 moveAxis = Vector3.forward;
        
        [Header("Events")]
        public UnityEvent OnToggledOn;
        public UnityEvent OnToggledOff;

        private bool _isToggled = false;
        private Vector3 _originalPosition;
        private Vector3 _pressedPosition;

        private void Start()
        {
            _originalPosition = transform.localPosition;
            // Calculamos hacia dónde se va a hundir
            _pressedPosition = _originalPosition + (moveAxis.normalized * pressDistance);
        }

        // Esta función se puede llamar desde un XR Simple Interactable
        public void ToggleButton()
        {
            _isToggled = !_isToggled;

            if (_isToggled)
            {
                transform.localPosition = _pressedPosition;
                Debug.Log($"[{gameObject.name}] Botón ENCENDIDO");
                OnToggledOn?.Invoke();
            }
            else
            {
                transform.localPosition = _originalPosition;
                Debug.Log($"[{gameObject.name}] Botón APAGADO");
                OnToggledOff?.Invoke();
            }
        }

        // Detección física directa si la mano virtual choca con el botón
        private void OnTriggerEnter(Collider other)
        {
            // OJO: Hay que asegurar que solo las manos activen esto (usando un Tag o Layer)
            if (other.CompareTag("Player") || other.name.Contains("Hand") || other.name.Contains("Controller"))
            {
                ToggleButton();
            }
        }
    }
}
