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
    /// <see cref="XRInputSubsystem"/>), cuando aparece un subsistema de entrada que la sesión no conocía, cuando se crea una sesión
    /// OpenXR nueva (<see cref="VRSessionStateFeature"/>) o cuando alguien llama a
    /// <see cref="DriverSeatFollower.RequestHeadPoseRecenter"/>. La suscripción a <c>trackingOriginUpdated</c> es del store (no de
    /// cada follower) para no perder un recentrado que llegue en una transición de escena sin follower vivo. Los callbacks pueden
    /// llegar desde el runtime XR: <see cref="Invalidate"/> solo sube una época (<see cref="Epoch"/>) y marca el pedido; la lógica
    /// corre en el main thread.
    /// </para>
    /// No se persiste entre ejecuciones de la app: un arranque nuevo es una captura nueva.
    /// </summary>
    public static class HeadPoseCompensationSession
    {
        // Cada cuántos segundos se revisa si cambió el conjunto de subsistemas de entrada XR (un solo throttle para toda la sesión).
        private const float SUBSCRIPTION_REFRESH_SECONDS = 2f;

        private static bool s_HasCompensation;
        private static float s_Yaw;
        private static Vector3 s_Offset;
        // Época en la que se tomó la compensación guardada.
        private static int s_StoredEpoch;

        // Época de "recaptura pedida": sube con cada invalidación. Los followers comparan contra la que conocen.
        private static int s_Epoch;
        // Hay una invalidación sin capturar desde entonces: la compensación guardada ya no es de fiar.
        private static volatile bool s_RecapturePending;
        private static volatile string s_LastReason = string.Empty;

        // Frame (del main thread) en que se observó por primera vez la última invalidación: el callback del runtime solo sube la
        // época, el frame se fija cuando alguien en el main thread nota el cambio (ver ObserveInvalidation).
        private static int s_ObservedEpoch;
        private static int s_LastInvalidationFrame;

        private static float s_NextRefreshTime;
        private static float? s_TimeOverride;

        // Subsistemas de entrada que la sesión ya conoce (las llaves son los XRInputSubsystem reales o, en tests, objetos cualquiera).
        private static readonly List<object> s_KnownSubsystems = new();
        private static readonly List<XRInputSubsystem> s_SubscribedSubsystems = new();
        // Buffers reutilizados por RefreshSubsystems (no se aloca nada por llamada).
        private static readonly List<XRInputSubsystem> s_QueryBuffer = new();
        private static readonly List<object> s_CurrentKeys = new();

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
            s_CurrentKeys.Clear();
            s_SubsystemKeysOverride = null;
            s_TimeOverride = null;
            s_NextRefreshTime = 0f;

            s_HasCompensation = false;
            s_Yaw = 0f;
            s_Offset = Vector3.zero;
            s_StoredEpoch = 0;
            s_Epoch = 0;
            s_ObservedEpoch = 0;
            s_LastInvalidationFrame = 0;
            s_RecapturePending = false;
            s_LastReason = string.Empty;
        }

        /// <summary>Hay una compensación capturada en esta sesión (aunque puede estar invalidada, ver <see cref="TryGetValid"/>).</summary>
        public static bool HasCompensation => s_HasCompensation;

        /// <summary>Contador que sube cada vez que hay que recapturar. Se puede leer desde cualquier hilo.</summary>
        public static int Epoch => Volatile.Read(ref s_Epoch);

        /// <summary>Hay una invalidación sin capturar desde entonces (la compensación guardada, si la hay, no se reutiliza).</summary>
        public static bool RecapturePending => s_RecapturePending;

        /// <summary>Motivo de la última invalidación (para el log).</summary>
        public static string LastReason => s_LastReason;

        /// <summary>
        /// Devuelve la compensación de la sesión si hay una vigente: capturada en la época actual y sin ninguna recaptura pendiente
        /// desde entonces.
        /// </summary>
        public static bool TryGetValid(out float yaw, out Vector3 offset)
        {
            yaw = s_Yaw;
            offset = s_Offset;
            return s_HasCompensation && !s_RecapturePending && s_StoredEpoch == Epoch;
        }

        /// <summary>
        /// Guarda una captura exitosa como la compensación vigente de la sesión. <paramref name="expectedEpoch"/> es la
        /// <see cref="Epoch"/> que leyó quien capturó ANTES de muestrear la pose de la cabeza. Si la época ya avanzó (llegó una
        /// invalidación entre la lectura y este guardado) la pose pudo ser anterior al recentrado: la captura se descarta (no se guarda
        /// nada y la recaptura pendiente sigue) y devuelve false. El follower que capturó igual la usa en su rig y recaptura al notar
        /// el cambio de época.
        /// </summary>
        public static bool Store(int expectedEpoch, float yaw, Vector3 offset)
        {
            if (expectedEpoch != Epoch) return false;

            s_Yaw = yaw;
            s_Offset = offset;
            s_StoredEpoch = expectedEpoch;
            s_HasCompensation = true;
            s_RecapturePending = false;
            // Una invalidación que llegue justo después del chequeo deja pending = true y una época nueva: TryGetValid compara
            // s_StoredEpoch con la época actual, así que esta captura tampoco se reutiliza.
            return true;
        }

        /// <summary>
        /// Invalida la compensación: los followers vivos recapturan y los nuevos no la reutilizan hasta que alguien capture.
        /// Se puede llamar desde cualquier hilo (solo sube la época y marca el pedido).
        /// </summary>
        public static void Invalidate(string reason)
        {
            s_LastReason = reason;
            s_RecapturePending = true;
            Interlocked.Increment(ref s_Epoch);
        }

        /// <summary>
        /// Main thread. Si la época cambió desde la última observación, fija <paramref name="currentFrame"/> como el frame de la última
        /// invalidación. Devuelve ese frame: ninguna captura es válida antes de él + <c>RECAPTURE_DELAY_FRAMES</c> (la pose tiene que
        /// reflejar el origen nuevo).
        /// </summary>
        public static int ObserveInvalidation(int currentFrame)
        {
            int epoch = Epoch;
            if (epoch != s_ObservedEpoch)
            {
                s_ObservedEpoch = epoch;
                s_LastInvalidationFrame = currentFrame;
            }
            return s_LastInvalidationFrame;
        }

        /// <summary>
        /// Revisa el conjunto de subsistemas de entrada XR si pasó <c>SUBSCRIPTION_REFRESH_SECONDS</c> desde la última revisión (un solo
        /// throttle para toda la sesión, no uno por follower). Ver <see cref="RefreshSubsystems"/>.
        /// </summary>
        public static void RefreshSubsystemsIfDue()
        {
            float now = s_TimeOverride ?? Time.unscaledTime;
            if (now < s_NextRefreshTime) return;
            s_NextRefreshTime = now + SUBSCRIPTION_REFRESH_SECONDS;
            RefreshSubsystems();
        }

        /// <summary>
        /// Revisa ya el conjunto de subsistemas de entrada XR (pueden crearse después del arranque o recrearse al reiniciar la sesión VR).
        /// Se suscribe a <c>trackingOriginUpdated</c> de los nuevos. Un subsistema que la sesión no conocía implica un origen de
        /// tracking potencialmente nuevo, así que invalida si ya había una compensación; uno ya conocido no invalida.
        /// </summary>
        public static void RefreshSubsystems()
        {
            s_CurrentKeys.Clear();
            if (s_SubsystemKeysOverride != null)
            {
                s_CurrentKeys.AddRange(s_SubsystemKeysOverride);
            }
            else
            {
                s_QueryBuffer.Clear();
                SubsystemManager.GetSubsystems(s_QueryBuffer);
                for (int i = 0; i < s_QueryBuffer.Count; i++) s_CurrentKeys.Add(s_QueryBuffer[i]);
            }
            SyncKnownSubsystems(s_CurrentKeys);
        }

        private static void SyncKnownSubsystems(List<object> current)
        {
            // Los que ya no existen se sueltan; si vuelven (instancia nueva) cuentan como nuevos.
            for (int i = s_KnownSubsystems.Count - 1; i >= 0; i--)
            {
                object known = s_KnownSubsystems[i];
                if (current.Contains(known)) continue;

                if (known is XRInputSubsystem knownSubsystem)
                {
                    knownSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                    s_SubscribedSubsystems.Remove(knownSubsystem);
                }
                s_KnownSubsystems.RemoveAt(i);
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

        /// <summary>Seam de tests: fuerza el tiempo (<c>Time.unscaledTime</c>) que usa el throttle de <see cref="RefreshSubsystemsIfDue"/>. Null = el real.</summary>
        public static void SetTimeOverrideForTests(float? unscaledTime)
        {
            s_TimeOverride = unscaledTime;
        }

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
