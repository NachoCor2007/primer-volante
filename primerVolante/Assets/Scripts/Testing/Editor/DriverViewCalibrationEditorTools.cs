using PrimerVolante.VR;
using UnityEditor;
using UnityEngine;

namespace PrimerVolante.Testing.Editor
{
    /// <summary>Utilidades de Editor para la calibración de vista guardada en PlayerPrefs.</summary>
    public static class DriverViewCalibrationEditorTools
    {
        [MenuItem("Primer Volante/Borrar calibración guardada")]
        public static void ClearSavedCalibration()
        {
            if (DriverViewCalibrationStore.TryLoad(out Vector3 saved))
            {
                DriverViewCalibrationStore.Clear();
                Debug.Log($"[DriverSeat] Calibración guardada borrada: {saved.ToString("F3")}");
            }
            else
            {
                DriverViewCalibrationStore.Clear();
                Debug.Log("[DriverSeat] No había una calibración guardada válida; se limpiaron igualmente las claves.");
            }
        }
    }
}
