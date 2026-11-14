using System.Collections;
using UnityEngine;
using PrimerVolante.Environment;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Evento scripteado para el Cruce (120, 60) en Circuit_01:
    /// - Al aproximarse el jugador a la intersección (Z ≈ 35m), detecta el auto con un Trigger volumétrico.
    /// - Fuerza el semáforo vehicular de la aproximación a ROJO para exigir detención reglamentaria antes de la senda.
    /// - Inicia la marcha de un peatón (NPC_Pedestrian) cruzando la calzada de vereda a vereda por la senda peatonal.
    /// - Al completar el cruce el peatón, conmuta el semáforo a VERDE para permitir continuar el circuito con giro a la izquierda.
    /// - Respeta estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    public class Circuit01PedestrianCrossingEvent : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Semáforo que controla el carril de aproximación del jugador.")]
        [SerializeField] private TrafficLightController m_TrafficLight;

        [Tooltip("Peatón NPC que cruzará la senda.")]
        [SerializeField] private NPCPathFollower m_Pedestrian;

        [Tooltip("Detector de cruce con semáforo en rojo en la línea de detención.")]
        [SerializeField] private RedLightViolationDetector m_RedLightDetector;

        [Header("Tiempos del Evento")]
        [Tooltip("Demora en segundos antes de iniciar el cruce del peatón tras ponerse en rojo.")]
        [SerializeField] private float m_PedestrianStartDelay = 1.0f;

        [Tooltip("Tiempo de espera adicional con semáforo en verde tras cruzar.")]
        [SerializeField] private float m_PostCrossingDelay = 1.5f;

        private bool m_EventTriggered = false;
        private bool m_EventCompleted = false;

        public bool IsEventTriggered => m_EventTriggered;
        public bool IsEventCompleted => m_EventCompleted;

        private void OnTriggerEnter(Collider other)
        {
            if (m_EventTriggered) return;

            VehicleController vehicle = other.GetComponentInParent<VehicleController>();
            if (vehicle != null)
            {
                TriggerEvent();
            }
        }

        public void TriggerEvent()
        {
            if (m_EventTriggered) return;
            m_EventTriggered = true;
            StartCoroutine(CrossingSequenceRoutine());
        }

        private IEnumerator CrossingSequenceRoutine()
        {
            Debug.Log("[Circuit01PedestrianCrossingEvent] 🚦 Evento iniciado: Semáforo en ROJO ante aproximación.");

            // 1. Poner semáforo en rojo
            if (m_TrafficLight != null)
            {
                m_TrafficLight.PauseCycle();
                m_TrafficLight.SetState(TrafficLightState.Red);
            }

            if (m_RedLightDetector != null)
            {
                m_RedLightDetector.enabled = true;
            }

            // 2. Esperar breve pausa para que el auto comience a frenar
            yield return new WaitForSeconds(m_PedestrianStartDelay);

            // 3. Activar y hacer cruzar al peatón
            if (m_Pedestrian != null)
            {
                m_Pedestrian.gameObject.SetActive(true);
                m_Pedestrian.Play();
                Debug.Log("[Circuit01PedestrianCrossingEvent] 🚶 Peatón cruzando por la senda peatonal...");

                // Esperar a que el peatón llegue al final de su ruta o hasta un tope de seguridad
                float timer = 0f;
                float timeout = 12f;
                while (m_Pedestrian.IsMoving && timer < timeout)
                {
                    timer += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                // Fallback temporal si no hay peatón asignado
                yield return new WaitForSeconds(6.0f);
            }

            yield return new WaitForSeconds(m_PostCrossingDelay);

            // 4. Cambiar el semáforo a VERDE (con fase amarilla intermedia)
            if (m_TrafficLight != null)
            {
                Debug.Log("[Circuit01PedestrianCrossingEvent] 🟢 Peatón cruzó. Semáforo cambiando a VERDE.");
                m_TrafficLight.SetState(TrafficLightState.Yellow);
                yield return new WaitForSeconds(1.5f);
                m_TrafficLight.SetState(TrafficLightState.Green);
            }

            m_EventCompleted = true;
            Debug.Log("[Circuit01PedestrianCrossingEvent] ✅ Evento de cruce completado con éxito. Vía libre.");
        }

        public void ResetEvent()
        {
            m_EventTriggered = false;
            m_EventCompleted = false;

            if (m_Pedestrian != null)
            {
                m_Pedestrian.TeleportToDistance(0f);
                m_Pedestrian.Stop();
            }

            if (m_TrafficLight != null)
            {
                m_TrafficLight.SetState(TrafficLightState.Green);
                m_TrafficLight.ResumeCycle();
            }
        }
    }
}

