using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.XR;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Compensación de la pose de la cabeza (yaw + desplazamiento horizontal) capturada en la sesión de la app, compartida por todos
    /// los <see cref="DriverSeatFollower"/>. La compensación se mide en el espacio de tracking del runtime XR, que no cambia al
    /// cargar otra escena: si cada follower nuevo la capturara de nuevo, tomaría la cabeza hacia donde el usuario esté mirando en ese
    /// momento (ej. al presionar un botón del menú) y el mundo quedaría girado. Por eso se captura una vez y los followers de las
    /// escenas siguientes la reutilizan.
    /// <para>
    /// La compensación se invalida (y los followers recapturan) cuando el runtime recentra (<c>trackingOriginUpdated</c> de cualquier
    /// <see cref="XRInputSubsystem"/>), cuando aparece un subsistema de entrada que la sesión no conocía, o cuando alguien llama a
    /// <see cref="DriverSeatFollower.RequestHeadPoseRecenter"/>. La suscripción a <c>trackingOriginUpdated</c> es del store (no de
    /// cada follower) para no perder un recentrado que llegue en una transición de escena sin follower vivo. El callback puede llegar
    /// desde el runtime XR: solo sube una época (<see cref="Epoch"/>), la lógica corre en el main thread.
    /// </para>
    /// No se persiste entre ejecuciones de la app: un arranque nuevo es una captura nueva.
    /// </summary>
    public static class HeadPoseCompensationSession
    {
        private static bool s_HasCompensation;
        private static float s_Yaw;
        private static Vector3 s_Offset;

        // Época de "recaptura pedida": sube con cada invalidación. Los followers comparan contra la que conocen.
        private static int s_Epoch;
        // Hay una invalidación sin capturar desde entonces: la compensación guardada ya no es de fiar.
        private static volatile bool s_RecapturePending;
        private static volatile string s_LastReason = string.Empty;

        // Subsistemas de entrada que la sesión ya conoce (las llaves son los XRInputSubsystem reales o, en tests, objetos cualquiera).
        private static readonly List<object> s_KnownSubsystems = new();
        private static readonly List<XRInputSubsystem> s_SubscribedSubsystems = new();
        private static readonly List<XRInputSubsystem> s_QueryBuffer = new();

        // Seam de tests: reemplaza la lista de subsistemas que devuelve SubsystemManager. Null = la real.
        private static List<object> s_SubsystemKeysOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < s_SubscribedSubsystems.Count; i++)
            {
                if (s_SubscribedSubsystems[i] != null) s_SubscribedSubsystems[i].trackingOriginUpdated -= OnTrackingOriginUpdated;
            }
            s_SubscribedSubsystems.Clear();
            s_KnownSubsystems.Clear();
            s_QueryBuffer.Clear();
            s_SubsystemKeysOverride = null;

            s_HasCompensation = false;
            s_Yaw = 0f;
            s_Offset = Vector3.zero;
            s_Epoch = 0;
            s_RecapturePending = false;
            s_LastReason = string.Empty;
        }

        /// <summary>Hay una compensación capturada en esta sesión (aunque puede estar invalidada, ver <see cref="TryGetValid"/>).</summary>
        public static bool HasCompensation => s_HasCompensation;

        /// <summary>Contador que sube cada vez que hay que recapturar. Se puede leer desde cualquier hilo.</summary>
        public static int Epoch => Volatile.Read(ref s_Epoch);

        /// <summary>Motivo de la última invalidación (para el log).</summary>
        public static string LastReason => s_LastReason;

        /// <summary>
        /// Devuelve la compensación de la sesión si hay una vigente: capturada y sin ninguna recaptura pendiente desde entonces.
        /// </summary>
        public static bool TryGetValid(out float yaw, out Vector3 offset)
        {
            yaw = s_Yaw;
            offset = s_Offset;
            return s_HasCompensation && !s_RecapturePending;
        }

        /// <summary>Guarda una captura exitosa como la compensación vigente de la sesión.</summary>
        public static void Store(float yaw, Vector3 offset)
        {
            s_Yaw = yaw;
            s_Offset = offset;
            s_HasCompensation = true;
            s_RecapturePending = false;
        }

        /// <summary>Invalida la compensación: los followers vivos recapturan y los nuevos no la reutilizan hasta que alguien capture.</summary>
        public static void Invalidate(string reason)
        {
            s_LastReason = reason;
            s_RecapturePending = true;
            Interlocked.Increment(ref s_Epoch);
        }

        /// <summary>
        /// Revisa el conjunto de subsistemas de entrada XR (pueden crearse después del arranque o recrearse al reiniciar la sesión VR).
        /// Se suscribe a <c>trackingOriginUpdated</c> de los nuevos. Un subsistema que la sesión no conocía implica un origen de
        /// tracking potencialmente nuevo, así que invalida si ya había una compensación; uno ya conocido no invalida.
        /// </summary>
        public static void RefreshSubsystems()
        {
            if (s_SubsystemKeysOverride != null)
            {
                SyncKnownSubsystems(s_SubsystemKeysOverride);
                return;
            }

            s_QueryBuffer.Clear();
            SubsystemManager.GetSubsystems(s_QueryBuffer);
            var keys = new List<object>(s_QueryBuffer.Count);
            for (int i = 0; i < s_QueryBuffer.Count; i++) keys.Add(s_QueryBuffer[i]);
            SyncKnownSubsystems(keys);
        }

        private static void SyncKnownSubsystems(List<object> current)
        {
            // Los que ya no existen se sueltan; si vuelven (instancia nueva) cuentan como nuevos.
            for (int i = s_KnownSubsystems.Count - 1; i >= 0; i--)
            {
                object known = s_KnownSubsystems[i];
                if (known is XRInputSubsystem knownSubsystem)
                {
                    if (knownSubsystem == null || !current.Contains(known))
                    {
                        if (knownSubsystem != null) knownSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                        s_SubscribedSubsystems.Remove(knownSubsystem);
                        s_KnownSubsystems.RemoveAt(i);
                    }
                }
                else if (!current.Contains(known))
                {
                    s_KnownSubsystems.RemoveAt(i);
                }
            }

            bool sawNew = false;
            for (int i = 0; i < current.Count; i++)
            {
                object subsystem = current[i];
                if (subsystem == null || s_KnownSubsystems.Contains(subsystem)) continue;

                if (subsystem is XRInputSubsystem inputSubsystem)
                {
                    inputSubsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                    s_SubscribedSubsystems.Add(inputSubsystem);
                }
                s_KnownSubsystems.Add(subsystem);
                sawNew = true;
            }

            // Sin compensación todavía no hay nada que invalidar: la primera captura va a medir el origen actual.
            if (sawNew && s_HasCompensation) Invalidate("nuevo subsistema de entrada XR");
        }

        // Puede llamarse desde el runtime XR: solo se marca el pedido, la recaptura ocurre en el Update del follower.
        private static void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            Invalidate("el runtime XR recentró el origen de tracking");
        }

        // ------------------------------------------------------------------ Seams de tests

        /// <summary>Seam de tests: descarta todo el estado de la sesión (compensación, época, subsistemas y suscripciones).</summary>
        public static void ResetForTests() => ResetStatics();

        /// <summary>Seam de tests: simula el evento <c>trackingOriginUpdated</c> del runtime (un recentrado del origen de tracking).</summary>
        public static void SimulateTrackingOriginUpdatedForTests() => OnTrackingOriginUpdated(null);

        /// <summary>
        /// Seam de tests: reemplaza los subsistemas de entrada que ve <see cref="RefreshSubsystems"/> por estas llaves (objetos cualquiera).
        /// Pasar null restaura la lectura real de <c>SubsystemManager</c>.
        /// </summary>
        public static void SetSubsystemKeysOverrideForTests(IList<object> keys)
        {
            s_SubsystemKeysOverride = keys != null ? new List<object>(keys) : null;
        }
    }
}
