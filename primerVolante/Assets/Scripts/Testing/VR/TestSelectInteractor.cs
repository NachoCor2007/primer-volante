using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Interactor mínimo sin input para pruebas de Play Mode desde el CLI: simula una "mano" que agarra
    /// un interactable llamando a <c>XRInteractionManager.SelectEnter/SelectExit</c> manualmente.
    /// No tiene targets válidos propios, así que no agarra nada por su cuenta; como
    /// <c>keepSelectedTargetValid</c> es true (por defecto), el manager no le cancela la selección
    /// mientras el interactable siga permitiéndola (por ejemplo, mientras coincidan las interaction layers).
    /// </summary>
    public class TestSelectInteractor : XRBaseInteractor
    {
    }
}
