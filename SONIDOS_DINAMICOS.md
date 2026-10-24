# Cómo trabajar con los sonidos dinámicos (bocinas, frenadas, sirenas, alarmas)

Guía de uso del audio de entorno implementado en **#12 US20**. Explica qué sonidos reaccionan a lo que pasa en la escena, cómo dispararlos o modificarlos desde un script de lección y cómo asociarlos a NPCs nuevos.

> Alcance: el audio **solo suena**. No hace que un NPC frene, esquive ni reaccione (eso es #23 US31) y no registra faltas (#15 US22). Ver [Limitaciones](#limitaciones).

---

## Resumen

Hay dos tipos de sonido de entorno:

- **Fijo**: el ambiente de cada zona (ciudad, plaza, pueblo) y los emisores de los edificios. Suena solo según dónde está el jugador. No hace falta tocarlo para una lección.
- **Dinámico**: reacciona a un evento. Es lo que documenta esta guía.

| Sonido | Componente | Cuándo suena |
|---|---|---|
| Bocina detrás del jugador | `GreenLightHonk` | El jugador está detenido en la línea con el semáforo en verde |
| Motor de un NPC | `NPCVehicleAudio` | Siempre que el NPC tenga ruta; el pitch sigue su velocidad |
| Frenada suave | `NPCVehicleAudio` | El NPC empieza a frenar (`IsBraking`) a más de 3 m/s |
| Chirrido + bocina corta | `NPCVehicleAudio` | El `NPCSafetyStop` del NPC hace una frenada de emergencia |
| Bocinas de impaciencia de un NPC | `NPCVehicleAudio` | El NPC sigue bloqueado por su `NPCSafetyStop` |
| Sirena | `NPCVehicleAudio` | Solo si se enciende por código (`SetSiren(true)`) |
| Pasos de un peatón | `NPCPedestrianAudio` | Mientras camina; la cadencia sigue su velocidad |
| Voces y grito de un peatón | `NPCPedestrianAudio` | Voces cada tanto; grito ante una frenada de emergencia cercana (**clips pendientes**) |
| Alarma de auto estacionado | `ParkedCarAlarm` | El jugador choca un auto estacionado |

Por encima de todos está el **`AmbienceDirector`**, que maneja la **intensidad** (`Calm`, `Normal`, `Tense`) y el **ducking** (bajar el ambiente cuando suena una alerta).

Todo el código está en `Assets/Scripts/Audio/` (namespace `PrimerVolante.Audio`).

---

## Dónde está cada cosa

| Pieza | Dónde está |
|---|---|
| `AmbienceDirector`, `ParkedCarAlarm` | GameObject `Audio_Environment` de `CityMerged3` |
| `GreenLightHonk` | GameObject `Controlled_Intersection_T` de `CityMerged3` |
| `NPCVehicleAudio` | Raíz de los 21 prefabs de `Assets/Prefabs/NPCs/Vehicles/` |
| `NPCPedestrianAudio` | Raíz de los 4 prefabs de `Assets/Prefabs/NPCs/Pedestrians/` |
| Perfiles (clips, volúmenes, distancias) | `Assets/Audio/Environment/Profiles/` |
| Clips placeholder | `Assets/Audio/Environment/Placeholders/` |
| Clips pendientes de conseguir | `Assets/Audio/Environment/SOUNDS_TODO.md` |
| Mixer | `Assets/Audio/Environment/EnvironmentAudioMixer.mixer` |

Como `NPCVehicleAudio` y `NPCPedestrianAudio` vienen en los prefabs, **cualquier NPC que pongas en una escena ya suena**. No hay que agregar nada.

---

## Intensidad: subir o bajar la presión

La intensidad cambia cuánto "aprieta" el entorno:

| Efecto | Calm | Normal | Tense |
|---|---|---|---|
| Volumen del ambiente | −3 dB | 0 dB | +2 dB |
| Frecuencia de sonidos dispersos (autos que pasan, pájaros) | ×0.5 | ×1 | ×2 |
| Segundos bloqueado antes de que un NPC toque bocina | 8 s | 5 s | 2.5 s |
| Segundos en verde antes de la bocina detrás del jugador | desactivada | 4 s | 2 s |

Los valores se editan en el inspector del `AmbienceDirector`.

### Desde un paso de lección (sin código)

Cada `LessonStepSO` tiene el campo **`Ambience Intensity`**. Por defecto es `Unchanged`, que no cambia nada. Si lo ponés en `Calm`, `Normal` o `Tense`, el director lo aplica cuando arranca ese paso. Al empezar o terminar la lección vuelve a la intensidad inicial.

Esto solo funciona si la escena tiene un `LessonManager`.

### Desde código

```csharp
using PrimerVolante.Audio;

// Subir la presión (por ejemplo, al entrar a una zona difícil de la lección).
AmbienceDirector.Instance?.SetIntensity(AmbienceIntensity.Tense);

// Leer la intensidad actual (devuelve Normal si no hay director en la escena).
AmbienceIntensity actual = AmbienceDirector.CurrentIntensity;

// Enterarse de los cambios.
AmbienceDirector.IntensityChanged += intensidad => Debug.Log($"Intensidad: {intensidad}");
```

`AmbienceDirector.Instance` es `null` si la escena no tiene director; por eso el `?.`.

---

## Bocina detrás del jugador (`GreenLightHonk`)

Es la que suena si no arrancás con el semáforo en verde.

**Cómo funciona.** El componente mira una caja: la de la línea de detención, alargada hacia atrás. Se activa cuando se cumple todo esto:

- el auto del jugador está dentro de la caja;
- el semáforo lleva en verde más que la demora de la intensidad actual;
- el jugador va a menos de 1 km/h.

Entonces toca bocina desde 8 m detrás del auto. La primera es corta y las siguientes largas, cada 3–4 s, hasta 3 veces. El contador se resetea cuando el jugador sale de la zona o el semáforo cambia de color.

### Ponerla en otra intersección

1. Agregá `GreenLightHonk` a un GameObject de la intersección (por ejemplo, la raíz).
2. Completá los campos:

   | Campo | Qué poner |
   |---|---|
   | `Traffic Light` | El `TrafficLightController` que controla el carril del jugador. |
   | `Stop Line Trigger` | El `BoxCollider` (trigger) de la línea de detención. Tiene que ser un `BoxCollider`. |
   | `Approach Depth` | Cuántos metros hacia atrás de la línea cuenta como "esperando". 8 por defecto. |
   | `Profile` | `EnvironmentAlerts_Default`. |
   | `Source` | Ver el paso 3. |
   | `Max Stop Speed Kmh`, `Repeat Min/Max Seconds`, `Max Honks` | Dejá los valores por defecto salvo que la lección pida otra cosa. |

3. Creá un hijo con un `AudioSource` y arrastralo a `Source`. Configuralo así:
   - `Output`: `EnvironmentAudioMixer > Alerts`;
   - `Spatial Blend`: 1 (3D);
   - `Play On Awake`: apagado;
   - `Doppler Level`: 0.

   Si dejás `Source` vacío, el componente crea la fuente solo, pero **sin grupo del mixer**: suena sin el filtro de cabina y sin el volumen de alertas.
4. La caja "hacia atrás" es el **−Z local** del trigger. Si la bocina no se activa al esperar detrás de la línea, el trigger está rotado al revés: revisá hacia dónde apunta su eje Z azul.

Para apagarla en una lección: `Calm` la desactiva, o deshabilitá el componente (`enabled = false`).

---

## Vehículos NPC (`NPCVehicleAudio`)

### Lo que hace solo

- **Motor**: dos loops (ralentí y régimen medio) mezclados según la velocidad. Un NPC **sin ruta** tiene el motor apagado; así un auto estacionado de lección no hace ralentí.
- **Frenada suave**: cuando `NPCVehicleController.IsBraking` pasa a verdadero.
- **Frenada de emergencia**: se engancha al evento `OnEmergencyBrake` del `NPCSafetyStop` del mismo NPC. Suena el chirrido (si venía a más de 2 m/s) y 0.25 s después una bocina corta. El ambiente baja 6 dB.
- **Impaciencia**: si el `NPCSafetyStop` lo mantiene frenado, toca bocina después de la demora de la intensidad, cada 3 s, hasta 4 veces, alternando corta y larga. Se resetea cuando el NPC vuelve a moverse.

O sea: **para que un NPC toque bocina en una lección, hacé que su `NPCSafetyStop` lo frene**. Pasa si el jugador se le cruza adelante, o si lo forzás:

```csharp
// El NPC frena de emergencia como si tuviera algo adelante: suena chirrido + bocina.
npcVehiculo.GetComponent<NPCSafetyStop>().ForceEmergencyBrake(colliderDelObstaculo);
```

### Campos del inspector que conviene conocer

| Campo | Qué hace |
|---|---|
| `Profile` | `NPCVehicleAudio_Default` o `NPCVehicleAudio_Heavy` (más grave, para buses, camiones y contenedores). |
| `Honk After Blocked Seconds` | Fija la demora de la bocina de impaciencia de **este** NPC. Con 0 usa la de la intensidad. Útil para un "conductor apurado" en una lección. |
| `Honk Repeat Seconds`, `Max Blocked Honks` | Cada cuánto repite y cuántas veces. |
| `Siren Clip`, `Siren Enabled` | Ver abajo. |
| `Log Events` | Escribe en consola cada frenada y cada bocina. |

### Sirena

Solo `NPC_Ambulance` y `NPC_Police Car` tienen asignado el clip de sirena, y viene **apagada**. Para una lección con un vehículo de emergencia que se acerca:

```csharp
var audio = ambulancia.GetComponent<NPCVehicleAudio>();
audio.SetSiren(true);   // suena en loop, en 3D, con un leve efecto Doppler
// ...
audio.SetSiren(false);
```

Si querés sirena en otro vehículo, asignale `Siren Clip` = `siren_wail_loop.wav` en la instancia de la escena.

Mientras la sirena está cerca (a menos de la mitad de su alcance), el ambiente se baja un poco.

### Escuchar las frenadas de todos los NPC

```csharp
// Se dispara cuando cualquier vehículo NPC hace una frenada de emergencia, con su posición.
NPCVehicleAudio.AnyEmergencyBrake += posicion => { /* ... */ };
```

Es estático. Desuscribite en `OnDisable`. Se limpia solo al entrar a Play Mode. Los peatones lo usan para gritar.

---

## Peatones NPC (`NPCPedestrianAudio`)

- **Pasos**: mientras el peatón camina, con un intervalo de largo de paso / velocidad. Se oyen hasta 15 m: sirven para anticipar a un peatón que va a cruzar.
- **Voces**: un clip al azar cada 15–40 s.
- **Grito**: si un vehículo NPC hace una frenada de emergencia a menos de 12 m.

**Las voces y el grito no tienen clips todavía.** El componente funciona igual y simplemente no suenan. Cuando los consigas, cargalos en `NPCPedestrianAudio_Default` (listas `Voices` y `Screams`).

Para que un peatón no suene, deshabilitá el componente en la instancia.

---

## Alarma de autos estacionados (`ParkedCarAlarm`)

Escucha `ObstacleCollisionDetector.AnyImpact` (US33). Si el jugador choca un auto estacionado (choque, no roce), suena una alarma desde ese auto durante 8 s. Cuenta como estacionado un auto decorativo o un NPC **sin ruta**. Suena una sola alarma a la vez.

Para que un auto estacionado de lección tenga alarma, tiene que tener un `ObstacleCollisionDetector` de tipo `Vehicle`. Los prefabs `NPC_*` y los autos decorativos de `CityMerged3` ya lo tienen.

Desde código:

```csharp
var alarma = Object.FindAnyObjectByType<ParkedCarAlarm>();
alarma.StartAlarm(autoConDetector);   // ObstacleCollisionDetector del auto
alarma.StopAlarm();
bool sonando = alarma.IsAlarmPlaying;
```

---

## Ducking: bajar el ambiente para que se oiga algo

Si un script tuyo reproduce un sonido importante, podés pedir que el ambiente baje unos segundos:

```csharp
// Baja el ambiente 6 dB durante 1.2 s y después vuelve solo.
AmbienceDirector.Duck(6f, 1.2f);
```

Si no hay director en la escena, no hace nada. Con varios pedidos a la vez queda el más fuerte.

---

## Agregar un NPC nuevo

1. Creá el prefab en `Assets/Prefabs/NPCs/Vehicles/` (o `Pedestrians/`) con nombre `NPC_*`, como explica [RUTAS_NPC.md](RUTAS_NPC.md).
2. Corré **`Tools/Primer Volante/Environment Audio/Setup NPC Audio Prefabs`**. Recorre todos los prefabs de esas carpetas y les agrega el audio. Es idempotente: no duplica nada en los que ya lo tienen.
   - Si el nombre empieza con `NPC_Bus`, `NPC_Truck` o `NPC_Container`, usa el perfil `Heavy`.
   - Si empieza con `NPC_Ambulance` o `NPC_Police Car`, le asigna el clip de sirena (apagada).
3. Corré `Tools/Primer Volante/Run Environment Audio Verification` para confirmar que quedó bien.

Si preferís hacerlo a mano: agregá `NPCVehicleAudio` a la raíz, asignale un perfil y creá las fuentes hijas ruteadas a los grupos `NPC` (motor, frenada) y `Alerts` (bocina, sirena). El menú lo hace todo junto, así que es mejor usarlo.

---

## Cambiar o agregar sonidos

- **Reemplazar un sonido**: abrí el perfil en `Assets/Audio/Environment/Profiles/` y arrastrá el clip nuevo al campo. No hace falta tocar código. Los que suenan en 3D tienen que importarse en **mono** (`Force To Mono`).
- **Bocinas**: hay dos perfiles con bocinas. `NPCVehicleAudio_Default`/`Heavy` son las de los NPC. `EnvironmentAlerts_Default` es la que suena detrás del jugador y también tiene la alarma.
- **Clips que faltan**: la lista, con los términos de búsqueda, está en `Assets/Audio/Environment/SOUNDS_TODO.md`. Anotá cada clip que bajes en `Assets/Audio/CREDITS.md`.
- **Fuentes nuevas que escribas vos**: ruteá cada `AudioSource` a un grupo de `EnvironmentAudioMixer`:
  - `Alerts` para lo que el jugador tiene que oír sí o sí (bocinas, sirenas, gritos). Pasa por un filtro de cabina más suave.
  - `NPC` para motores y pasos.
  - `Ambience` para ambiente.

  **Nunca** las rutees a `VehicleAudioMixer`: es el del auto del jugador. Para crear una fuente 3D bien configurada podés usar `EnvironmentAudioUtil.CreateSource3D(padre, nombre, grupo, EnvironmentAudioUtil.PriorityAlert, minDist, maxDist)`.

---

## Depuración

- Todos los componentes tienen **`Log Events`**: activalo en Play Mode para ver en consola cada bocina, frenada, alarma o cambio de intensidad.
- En el `AmbienceDirector` también se ve el ducking actual (`CurrentDuckDb`).
- Después de cambiar prefabs o la escena, corré `Tools/Primer Volante/Run Environment Audio Verification`.

---

## Limitaciones

- **No hay un "tocá bocina ya" público en `NPCVehicleAudio`.** Un NPC toca bocina solo por frenada de emergencia o por bloqueo. Para una bocina a demanda en una lección, por ahora hay que forzar la frenada (`ForceEmergencyBrake`) o reproducir el clip con un `AudioSource` propio en el grupo `Alerts`.
- `GreenLightHonk` solo está en `Controlled_Intersection_T` de `CityMerged3`. En otras intersecciones o escenas hay que agregarlo a mano (ver arriba).
- **No vuelvas a correr `Setup Environment Audio (CityMerged3)` si agregaste cosas a mano** dentro de `Audio_Environment/Emitters`: el menú recalcula los emisores y **borra** los que no conoce. Poné tus fuentes fuera de ese GameObject.
- La cabina se considera siempre cerrada. Si en el futuro hay ventanillas, `AmbienceDirector.SetCabinCutoffs(exteriorHz, alertsHz)` cambia el filtro en tiempo real.
- Ningún sonido cambia el comportamiento del NPC. Un NPC que toca bocina no se mueve distinto; eso es de #23 US31.
