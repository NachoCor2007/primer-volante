using UnityEngine;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Disparador del volcado de la calibración guardada. Lo crea <see cref="DriverViewCalibrationStore"/> de forma lazy (objeto
    /// oculto y persistente entre escenas) en Play Mode. En el Quest la app puede morir sin pasar por <c>OnApplicationQuit</c>,
    /// por eso también se vuelca al pausar o perder el foco.
    /// </summary>
    public class DriverViewCalibrationFlusher : MonoBehaviour
    {
        private void OnApplicationPause(bool paused)
        {
            if (paused) DriverViewCalibrationStore.Flush();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) DriverViewCalibrationStore.Flush();
        }

        private void OnApplicationQuit()
        {
            DriverViewCalibrationStore.Flush();
        }
    }
}
