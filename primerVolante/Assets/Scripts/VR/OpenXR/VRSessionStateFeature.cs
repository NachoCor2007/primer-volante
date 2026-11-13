using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace PrimerVolante.VR
{
    /// <summary>
    /// Feature de OpenXR propia que observa los cambios de estado de la sesión (IDLE, READY, SYNCHRONIZED, VISIBLE, FOCUSED,
    /// STOPPING, LOSS_PENDING, EXITING). Los callbacks de OpenXR pueden llegar fuera del flujo normal del juego, así que acá
    /// NO se toca nada del juego: solo se guarda el estado actual en un campo estático thread-safe y se encola la transición
    /// (anterior, nueva) para que <see cref="VRHardwareStateMonitor"/> la procese en el hilo principal.
    /// Las OpenXRLifeCycleFeature que ya hay habilitadas son internas de los paquetes meta-openxr y androidxr-openxr y no se
    /// pueden reutilizar. <c>OnApplicationFocus</c> no sirve como señal en PCVR (es el foco de la ventana del escritorio).
    /// </summary>
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Primer Volante: Session State",
        BuildTargetGroups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android },
        Company = "Primer Volante",
        Desc = "Observa los cambios de estado de la sesión OpenXR (foco, pérdida de sesión) para pausar la simulación cuando se pierde la conexión VR.",
        Category = FeatureCategory.Feature,
        FeatureId = FEATURE_ID,
        Version = "1.0.0")]
#endif
    public class VRSessionStateFeature : OpenXRFeature
    {
        public const string FEATURE_ID = "com.primervolante.sessionstate";

        // Valores de XrSessionState.
        public const int STATE_UNKNOWN = 0;
        public const int STATE_IDLE = 1;
        public const int STATE_READY = 2;
        public const int STATE_SYNCHRONIZED = 3;
        public const int STATE_VISIBLE = 4;
        public const int STATE_FOCUSED = 5;
        public const int STATE_STOPPING = 6;
        public const int STATE_LOSS_PENDING = 7;
        public const int STATE_EXITING = 8;

        /// <summary>Transición de estado de sesión (old -> new) encolada para el log y el procesamiento.</summary>
        public readonly struct Transition
        {
            public readonly int OldState;
            public readonly int NewState;

            public Transition(int oldState, int newState)
            {
                OldState = oldState;
                NewState = newState;
            }
        }

        private static int s_CurrentState = STATE_UNKNOWN;
        private static readonly ConcurrentQueue<Transition> s_Transitions = new ConcurrentQueue<Transition>();

        /// <summary>Saca la transición más antigua de la cola. false si no hay ninguna.</summary>
        public static bool TryDequeueTransition(out Transition transition) => s_Transitions.TryDequeue(out transition);

        /// <summary>Nombre legible de un XrSessionState (para los logs).</summary>
        public static string StateName(int state)
        {
            switch (state)
            {
                case STATE_UNKNOWN: return "UNKNOWN";
                case STATE_IDLE: return "IDLE";
                case STATE_READY: return "READY";
                case STATE_SYNCHRONIZED: return "SYNCHRONIZED";
                case STATE_VISIBLE: return "VISIBLE";
                case STATE_FOCUSED: return "FOCUSED";
                case STATE_STOPPING: return "STOPPING";
                case STATE_LOSS_PENDING: return "LOSS_PENDING";
                case STATE_EXITING: return "EXITING";
                default: return "STATE_" + state;
            }
        }

        // Con "Enter Play Mode Options" sin recarga de dominio los estáticos sobreviven entre Play: se reinician aquí.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ResetState();

        private static void ResetState()
        {
            Interlocked.Exchange(ref s_CurrentState, STATE_UNKNOWN);
            while (s_Transitions.TryDequeue(out _)) { }
        }

        // Una sesión nueva arranca de cero: lo que quedó de la anterior (p. ej. LOSS_PENDING) ya no vale.
        protected override void OnSessionCreate(ulong xrSession) => RecordSessionCreate();

        // Camino común del callback real y de SimulateSessionCreate (tests).
        private static void RecordSessionCreate()
        {
            Interlocked.Exchange(ref s_CurrentState, STATE_UNKNOWN);
            // Una sesión nueva puede traer un origen de tracking distinto aunque el subsistema de entrada sea el mismo (reconexión
            // de Quest Link, LOSS_PENDING seguido de una sesión nueva): la compensación de cabeza de la sesión de la app ya no vale.
            // Invalidate es thread-safe (solo sube una época), así que sirve aunque el callback llegue fuera del main thread.
            HeadPoseCompensationSession.Invalidate("se creó una sesión OpenXR nueva");
        }

        protected override void OnSessionStateChange(int oldState, int newState) => RecordStateChange(oldState, newState);

        protected override void OnSessionLossPending(ulong xrSession) => RecordLossPending();

        protected override void OnSessionExiting(ulong xrSession) => RecordExiting();

        // Camino común de los callbacks reales y de SimulateStateChange (tests): solo estado estático + cola.
        private static void RecordStateChange(int oldState, int newState)
        {
            Interlocked.Exchange(ref s_CurrentState, newState);
            s_Transitions.Enqueue(new Transition(oldState, newState));
        }

        private static void RecordLossPending()
        {
            int current = Volatile.Read(ref s_CurrentState);
            // El runtime suele mandar también el cambio a LOSS_PENDING; solo se encola si todavía no se registró.
            if (current != STATE_LOSS_PENDING) RecordStateChange(current, STATE_LOSS_PENDING);
        }

        private static void RecordExiting()
        {
            int current = Volatile.Read(ref s_CurrentState);
            if (current != STATE_EXITING) RecordStateChange(current, STATE_EXITING);
        }

#if UNITY_EDITOR
        /// <summary>Solo Editor (tests): recorre exactamente el mismo camino que el callback <c>OnSessionStateChange</c>.</summary>
        public static void SimulateStateChange(int oldState, int newState) => RecordStateChange(oldState, newState);

        /// <summary>Solo Editor (tests): recorre el mismo camino que <c>OnSessionLossPending</c>.</summary>
        public static void SimulateLossPending() => RecordLossPending();

        /// <summary>Solo Editor (tests): recorre el mismo camino que <c>OnSessionExiting</c>.</summary>
        public static void SimulateExiting() => RecordExiting();

        /// <summary>Solo Editor (tests): recorre el mismo camino que <c>OnSessionCreate</c>.</summary>
        public static void SimulateSessionCreate() => RecordSessionCreate();

        /// <summary>Solo Editor (tests): vuelve al estado inicial (sin sesión, cola vacía).</summary>
        public static void ResetForTests() => ResetState();
#endif
    }
}
