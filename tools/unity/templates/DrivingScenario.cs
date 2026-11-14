// Plantilla de escenario de Play Mode para `python tools/unity/pvu.py scenario <archivo.cs> [--scene Assets/...unity]`.
// Copiala al scratchpad y adaptá los pasos; no la edites acá.
//
// Contrato con pvu.py:
//  - Se ejecuta con eval_file con el Editor ya en Play Mode: registra una máquina de estados en UnityEditor.EditorApplication.update
//    y retorna enseguida.
//  - Loguea líneas "[TEST] ..." (las que contengan "FAIL" hacen fallar el escenario) y termina SIEMPRE con "[TEST] DONE".
//  - pvu.py sale de Play Mode y elimina los Gamepads virtuales al terminar (salvo --keep-playing).
//
// Manejo: simulate_key no mueve el auto; VehicleController lee Gamepad.current, por eso se usa un Gamepad virtual.
// El auto arranca con el freno de mano puesto: hay que soltarlo con VRHandbrake.SetEngagementImmediate(0).
//
// eval_file mete este código dentro de un método: NO se pueden usar directivas `using`; usá nombres completos.

if (!UnityEditor.EditorApplication.isPlaying) return "No está en Play Mode";

var car = UnityEngine.Object.FindAnyObjectByType<PrimerVolante.VR.VehicleController>();
if (car == null)
{
    UnityEngine.Debug.Log("[TEST] FAIL: no hay VehicleController en la escena");
    UnityEngine.Debug.Log("[TEST] DONE");
    return "sin auto";
}

var handbrake = car.GetComponentInChildren<PrimerVolante.VR.VRHandbrake>(true);
if (handbrake == null) handbrake = UnityEngine.Object.FindAnyObjectByType<PrimerVolante.VR.VRHandbrake>();
var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("TestPad");

handbrake?.SetEngagementImmediate(0f);
car.SetEngineRunning(true);
car.SetGear(PrimerVolante.VR.GearState.Drive);

// Opcional: teletransportar el auto a una pose inicial (verificar que no se superponga con colliders).
// var rb = car.GetComponent<UnityEngine.Rigidbody>();
// rb.position = new UnityEngine.Vector3(0, 0.5f, 0); rb.rotation = UnityEngine.Quaternion.Euler(0, 90, 0);
// UnityEngine.Physics.SyncTransforms();
// Velocidad máxima por escenario: campo privado m_MaxSpeedKmh de VehicleController, por reflexión.

int step = 0;
float stepStart = UnityEngine.Time.time;
float maxSpeed = 0f;
const float SCENARIO_TIMEOUT = 60f;
float scenarioStart = UnityEngine.Time.time;

void Next() { step++; stepStart = UnityEngine.Time.time; }

UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
    float t = UnityEngine.Time.time - stepStart;
    maxSpeed = UnityEngine.Mathf.Max(maxSpeed, car.CurrentSpeedKmh);

    if (UnityEngine.Time.time - scenarioStart > SCENARIO_TIMEOUT && step < 99)
    {
        UnityEngine.Debug.Log($"[TEST] FAIL: timeout del escenario en el paso {step}");
        step = 99;
    }

    switch (step)
    {
        case 0: // acelerar 3 s
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState { rightTrigger = 1f });
            if (t > 3f)
            {
                UnityEngine.Debug.Log($"[TEST] velocidad tras 3 s acelerando: {car.CurrentSpeedKmh:F1} km/h");
                UnityEngine.Debug.Log(car.CurrentSpeedKmh > 3f ? "[TEST] PASS: el auto acelera" : "[TEST] FAIL: el auto no acelera");
                Next();
            }
            break;

        case 1: // frenar hasta detenerse
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState { leftTrigger = 1f });
            if (car.CurrentSpeedKmh < 0.5f || t > 8f)
            {
                UnityEngine.Debug.Log(car.CurrentSpeedKmh < 0.5f
                    ? $"[TEST] PASS: se detuvo en {t:F1} s (máx {maxSpeed:F1} km/h)"
                    : $"[TEST] FAIL: no se detuvo, {car.CurrentSpeedKmh:F1} km/h");
                step = 99;
            }
            break;

        default: // terminar siempre con DONE
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
            UnityEditor.EditorApplication.update -= tick;
            UnityEngine.Debug.Log("[TEST] DONE");
            break;
    }
};
UnityEditor.EditorApplication.update += tick;
return "escenario registrado";
