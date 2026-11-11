using UnityEngine.XR;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Detecta el flanco de subida (al apretar, no mientras se mantiene apretado) de un botón físico de un
    /// control XR, leído con la API legada UnityEngine.XR.InputDevices: la única que, en este proyecto, refleja en
    /// vivo el estado de los controles OpenXR (la capa de dispositivos del nuevo Input System no se actualiza para
    /// estos perfiles). Guardar una instancia por botón y llamar a <see cref="Poll"/> una vez por frame.
    /// </summary>
    public struct XRButtonEdgeDetector
    {
        private bool m_WasPressed;

        /// <summary>
        /// Devuelve true solo en el frame en que el botón pasa de suelto a apretado. Si el dispositivo no es válido
        /// o no reporta el botón, cuenta como suelto (así un botón apretado al reconectar vuelve a disparar).
        /// </summary>
        public bool Poll(XRNode node, InputFeatureUsage<bool> usage)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            bool pressed = false;
            if (!device.isValid || !device.TryGetFeatureValue(usage, out pressed))
            {
                m_WasPressed = false;
                return false;
            }

            bool risingEdge = pressed && !m_WasPressed;
            m_WasPressed = pressed;
            return risingEdge;
        }
    }
}
