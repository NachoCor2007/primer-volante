using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Bloquea las manos sobre el habitáculo mientras la simulación está pausada, sin importar quién tomó el token de
    /// <see cref="SimulationPause"/> (el menú in-game, la pausa por desconexión VR con o sin menú usable). Es el único dueño del
    /// bloqueo: se suscribe a <see cref="SimulationPause.PausedChanged"/> y, en la transición a pausado, pide
    /// <see cref="InteractorPauseBlock"/> para cada interactor de las manos de las escenas cargadas (NearFar, Poke y Direct, o
    /// sea controles y hand tracking), soltando sus selecciones; en la transición a no pausado los libera. Como la pausa es un
    /// único evento por transición, el estado original se restaura una sola vez aunque haya varios dueños solapados.
    /// Arranca solo en cada escena (<see cref="RuntimeInitializeOnLoadMethod"/>), así que cubre las lecciones, la conducción
    /// libre y NewMainMenu sin cablear nada. Los interactores no se cablean a mano: se buscan en las escenas cargadas.
    /// </summary>
    public static class CabinPauseInteractionBlock
    {
        private static readonly List<XRBaseInteractor> s_Blocked = new List<XRBaseInteractor>();
        private static GameObject s_Token;
        private static bool s_Enabled;

        /// <summary>true mientras el bloqueo está suscrito a <see cref="SimulationPause.PausedChanged"/>.</summary>
        public static bool IsEnabled => s_Enabled;

        /// <summary>Cantidad de interactores bloqueados en este momento.</summary>
        public static int BlockedCount
        {
            get
            {
                int count = 0;
                foreach (XRBaseInteractor interactor in s_Blocked)
                {
                    if (interactor != null) count++;
                }
                return count;
            }
        }

        // SimulationPause.ResetStatics (SubsystemRegistration) borra sus suscriptores; BeforeSceneLoad es posterior.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            s_Blocked.Clear();
            s_Enabled = false;
            Enable();
        }

        /// <summary>
        /// Se suscribe a <see cref="SimulationPause.PausedChanged"/> (idempotente). Si la simulación ya está pausada, bloquea de una vez.
        /// </summary>
        public static void Enable()
        {
            SimulationPause.PausedChanged -= OnPausedChanged;
            SimulationPause.PausedChanged += OnPausedChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // En el Editor se dispara al salir de Play: sin recarga de dominio los estáticos sobreviven y el bloqueo seguiría
            // armado en Edit Mode (agregando componentes a las escenas abiertas con cada pausa de las verificaciones).
            Application.quitting -= Disable;
            Application.quitting += Disable;
            s_Enabled = true;

            if (SimulationPause.IsPaused) Block();
        }

        /// <summary>Se desuscribe y libera todo lo que tenía bloqueado.</summary>
        public static void Disable()
        {
            SimulationPause.PausedChanged -= OnPausedChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= Disable;
            s_Enabled = false;
            Unblock();
            if (s_Token != null) DestroyToken();
        }

        private static void OnPausedChanged(bool paused)
        {
            if (paused) Block();
            else Unblock();
        }

        // Un interactor que aparece con la pausa vigente (escena aditiva) también queda bloqueado.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (SimulationPause.IsPaused) Block();
        }

        private static void Block()
        {
            EnsureToken();
            XRBaseInteractor[] interactors = Object.FindObjectsByType<XRBaseInteractor>(FindObjectsInactive.Include);
            foreach (XRBaseInteractor interactor in interactors)
            {
                if (!IsHandInteractor(interactor)) continue;

                InteractorPauseBlock.Request(interactor, s_Token);
                if (!s_Blocked.Contains(interactor)) s_Blocked.Add(interactor);
            }
        }

        private static void Unblock()
        {
            if (s_Token != null)
            {
                foreach (XRBaseInteractor interactor in s_Blocked)
                {
                    if (interactor != null) InteractorPauseBlock.Release(interactor, s_Token);
                }
            }
            s_Blocked.Clear();
        }

        // Interactores que pueden agarrar o tocar el habitáculo. Los de teletransporte (capa Teleport), la mirada y el climb no.
        private static bool IsHandInteractor(XRBaseInteractor interactor)
        {
            return interactor is NearFarInteractor || interactor is XRPokeInteractor || interactor is XRDirectInteractor;
        }

        // El solicitante tiene que ser un UnityEngine.Object: un GameObject oculto que vive mientras dure la sesión de Play.
        private static void EnsureToken()
        {
            if (s_Token != null) return;

            s_Token = new GameObject("CabinPauseInteractionBlock") { hideFlags = HideFlags.HideInHierarchy };
            if (Application.isPlaying) Object.DontDestroyOnLoad(s_Token);
        }

        private static void DestroyToken()
        {
            if (Application.isPlaying) Object.Destroy(s_Token);
            else Object.DestroyImmediate(s_Token);
            s_Token = null;
        }
    }
}
