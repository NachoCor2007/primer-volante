# Cómo funciona el sistema de infracciones (faltas leves y graves)

Guía del gestor de infracciones implementado en **#15 US22**. Explica cómo un detector informa una falta, dónde se guarda, cómo se agrega una infracción nueva y cómo la van a leer el tablero (US24), el filtro de visión (US23) y el reporte final (US26).

> Alcance: el sistema **solo registra y cuenta**. No muestra nada en pantalla, no calcula puntaje ni nota final, no reinicia la prueba, no decide de quién es la culpa y no guarda a disco. Ver [Limitaciones](#limitaciones).

---

## Qué problema resuelve

Antes de US22 cada sistema detectaba su falta por su cuenta y solo escribía en consola o disparaba un evento sin nadie escuchando. No había un lugar donde preguntar "¿cuántas faltas leves y graves cometió el jugador?".

Ahora hay un **repositorio central** (`InfractionManager`) y una **interfaz común** por la que cualquier sistema, actual o futuro, informa una falta **sin saber quién la guarda**. Cada infracción registrada se puede mostrar de dos formas:

- **Título corto** ("Exceso de velocidad") en el tablero de la lección (US24).
- **Descripción completa** ("Superaste el límite de 40 km/h en este tramo. ...") en el reporte de fin de lección (US26).

Registrar una falta **nunca interrumpe la simulación**: no toca `Time.timeScale`, no frena el auto, no recarga la escena y no lanza excepciones hacia el detector que reporta.

---

## Flujo

```
 Detector                    Punto de acceso              Gestor                         Catálogo
 (semáforo, velocidad,       estático                     (en la escena)                 (assets)
  vallas, obstáculos)

 InfractionReporting   ->    InfractionReporting     ->   InfractionManager.Report  ->   InfractionCatalogSO
   .Report(InfractionReport      .Current                   1. busca el id                 .TryGet(id)
     .Create(id, this, pos,      (el gestor registrado          en el catálogo               -> InfractionDefinitionSO
     ("speed", "52")))            o el respaldo de consola)  2. formatea la descripción        (título, plantilla,
                                                             3. crea el InfractionRecord       gravedad)
                                                             4. cuenta leve / grave
                                                             5. dispara InfractionRecorded
                                                                      |
                                       +------------------------------+------------------------------+
                                       v                              v                              v
                              Tablero (US24)                 Filtro de visión (US23)         Reporte final (US26)
                              ShortTitle                     Severity                        Description + Records
```

Si **no hay gestor** en la escena, `InfractionReporting.Current` es un respaldo que escribe la falta en la consola (`[Infraction] ...`) y el flujo termina ahí.

---

## Piezas y dónde está cada una

Todo el código runtime está en `Assets/Scripts/Evaluation/` (namespace `PrimerVolante.Evaluation`).

| Pieza | Archivo | Responsabilidad |
|---|---|---|
| `InfractionSeverity` | `Data/InfractionSeverity.cs` | Enum `Minor` (falta leve) / `Major` (falta grave). |
| `InfractionIds` | `Data/InfractionIds.cs` | Constantes `string` con el id estable de cada infracción. Es la **única** forma en que un detector nombra una falta. |
| `InfractionReport` | `Data/InfractionReport.cs` | Struct de solo lectura que envía el detector: `InfractionId`, `Source`, `Position` y `Parameters` (valores ya formateados para la plantilla). `InfractionReport.Create(id, source, pos, ("clave", "valor"), ...)` lo arma en una línea. |
| `InfractionDefinitionSO` | `Data/InfractionDefinitionSO.cs` | ScriptableObject con id, título corto, plantilla de descripción y gravedad. Es solo dato de autoría: **no guarda estado de runtime**. |
| `InfractionCatalogSO` | `Data/InfractionCatalogSO.cs` | ScriptableObject con la lista de definiciones y `TryGet(id, out def)`. Avisa con `LogWarning` (en `OnValidate`) si hay ids repetidos o vacíos. |
| `InfractionRecord` | `Data/InfractionRecord.cs` | Una infracción ya registrada (inmutable). Solo la crea el gestor. |
| `InfractionTextFormatter` | `InfractionTextFormatter.cs` | Función pura `Format(plantilla, parámetros)`: reemplaza cada `{clave}`. Un marcador sin valor queda tal cual y nunca lanza. |
| `IInfractionReporter` | `IInfractionReporter.cs` | Interfaz de **escritura**: `Report(in InfractionReport)`. |
| `IInfractionLog` | `IInfractionLog.cs` | Interfaz de **lectura**: `Records`, `MinorCount`, `MajorCount`, `TotalCount`, evento `InfractionRecorded` y evento `Cleared`. |
| `InfractionReporting` | `InfractionReporting.cs` | Holder estático: `Current` (nunca null), `Log`, `Register`/`Unregister` y `Report(...)`, que atrapa cualquier excepción del reporter. Incluye el respaldo de consola. |
| `InfractionManager` | `InfractionManager.cs` | `MonoBehaviour` que implementa las dos interfaces. Se registra en `OnEnable`. Sin `Update`: no hace nada costoso por frame. |

El gestor de la escena es el GameObject raíz **`InfractionManager`** de `Assets/Scenes/car09/CityWithInfractions.unity`, con el catálogo asignado. Los assets están en `Assets/Settings/Evaluation/` (`InfractionCatalog.asset` y un `Infraction_<ID>.asset` por tipo).

### Qué guarda un `InfractionRecord`

| Campo | Contenido |
|---|---|
| `Sequence` | 1, 2, 3... desde el último `Clear`. |
| `Definition` (+ atajos `Id`, `Severity`, `ShortTitle`) | Definición del catálogo con la que se registró. |
| `Description` | La plantilla ya formateada con los parámetros del reporte. |
| `Parameters` | Copia defensiva de los parámetros. |
| `Time` | `Time.time` al registrarse. |
| `ElapsedSinceStart` | Segundos desde el último `Clear` o el inicio de la lección. |
| `Position` | Dónde ocurrió, en mundo. |
| `SourceName` | **Nombre** del GameObject que reportó (no la referencia: el registro sigue siendo válido si el objeto se destruye). |
| `LessonId`, `StepIndex` | Contexto de la lección si hay `LessonManager` y una lección activa. Si no, `null` y `-1`. |

---

## Infracciones actuales

Las fuentes son los cuatro sistemas que ya detectaban faltas. Los textos están en los assets y se editan sin tocar código.

| Id | Fuente | Gravedad | Título corto | Plantilla de la descripción | Parámetros |
|---|---|---|---|---|---|
| `RED_LIGHT` | `RedLightViolationDetector`: cruce de la línea con el semáforo en rojo | Grave | Semáforo en rojo | Cruzaste la línea de detención con el semáforo en rojo. Frená antes de la línea y esperá la luz verde. | (ninguno) |
| `SPEEDING` | `SpeedLimitEvaluator`: exceso de velocidad, **una por tramo** | Leve | Exceso de velocidad | Superaste el límite de {limit} km/h en este tramo. Respetá la velocidad máxima señalizada. | `limit` |
| `LESSON_BOUNDARY` | `LessonBoundary`: el auto alcanza las vallas de la lección | Grave | Fuera del área | Intentaste salir del área de práctica de la lección a {speed} km/h. Mantenete dentro del recorrido delimitado por las vallas. | `speed` |
| `CONE_GRAZE` | `ObstacleCollisionDetector`: roce con un cono | Leve | Roce con cono | Rozaste un cono a {speed} km/h. Calculá mejor las distancias en la maniobra. | `speed` |
| `CONE_CRASH` | `ObstacleCollisionDetector`: choque con un cono | Grave | Choque con cono | Chocaste un cono a {speed} km/h. Reducí la velocidad al maniobrar cerca de obstáculos. | `speed` |
| `VEHICLE_GRAZE` | `ObstacleCollisionDetector`: roce con un vehículo (NPC o estacionado) | Leve | Roce con vehículo | Rozaste un vehículo a {speed} km/h. Dejá más espacio lateral y usá los espejos. | `speed` |
| `VEHICLE_CRASH` | `ObstacleCollisionDetector`: choque con un vehículo | Grave | Choque con vehículo | Chocaste un vehículo a {speed} km/h. Mantené la distancia y anticipá las frenadas. | `speed` |
| `PEDESTRIAN_HIT` | `ObstacleCollisionDetector`: atropello (siempre choque) | Grave | Atropello de peatón | Atropellaste a un peatón a {speed} km/h. Cedé siempre el paso a los peatones y reducí la velocidad cerca de las sendas peatonales. | `speed` |

Detalles de cada fuente:

- **Semáforo**: se informa en `TryRecordViolation`, después del cooldown de 3 s por vehículo. La posición es la del auto.
- **Velocidad**: se informa **solo en la transición** de "sin infracción" a "con infracción" dentro de un tramo. Solo lleva el límite (`limit`), no la velocidad: en ese instante apenas se cruzó la tolerancia y no representa el máximo alcanzado. Seguir excediendo en el mismo tramo no suma. El tramo se rearma cuando el auto sale por completo, así que volver a entrar puede sumar una falta nueva. Si el auto sale sin `OnTriggerExit` (teletransportado, apagado o destruido), el tramo se cierra solo: al volver a entrar, si hace más de 0.25 s que no llega un `OnTriggerStay`, se considera abandonado. También se cierra al deshabilitar el componente.
- **Vallas**: se informa una vez por pasada (la deduplicación por vehículo de `LessonBoundary` sigue igual). La posición y la velocidad son las del contacto.
- **Obstáculos**: se informa un id por episodio de contacto. `ObstacleImpactMath.InfractionIdFor(ObstacleKind, ObstacleImpactSeverity)` hace el mapeo (peatón es siempre `PEDESTRIAN_HIT`). La velocidad es la del jugador (`PlayerSpeedKmh`); la posición es el punto de contacto. **Con el auto del jugador casi detenido no hay falta**: si `PlayerSpeedKmh` es menor que `Min Fault Speed Kmh` (1 km/h por defecto, configurable por obstáculo), quien chocó fue el obstáculo (un NPC que embiste, un peatón que camina contra el auto) y solo se emite el evento (`ObstacleImpactMath.IsPlayerFault`).

Las velocidades se formatean como enteros (`F0`). Los eventos C# y UnityEvents que ya existían (`OnViolationDetected`, `OnVehicleReachedBoundary`, `OnImpact`, `AnyImpact`) **se mantienen**: otros sistemas, como el audio, los usan. La falta se informa por `InfractionReporting` y el evento queda para reacciones. **No registres faltas suscribiéndote a esos eventos** (por ejemplo a `AnyImpact`): se contarían dos veces.

### Logs

El `InfractionManager` loguea cada falta si `m_LogEvents` está activo (por defecto sí):

```
[InfractionManager] 🚨 #1 [Leve] Exceso de velocidad: Superaste el límite de 40 km/h en este tramo. ... (EvaluationZone)
```

`RedLightViolationDetector` y `SpeedLimitEvaluator` tienen ahora un `m_LogEvents` (apagado por defecto) para sus logs propios, porque el gestor ya escribe la falta. Sin gestor, el respaldo de consola escribe un `[Infraction]`.

---

## Agregar una infracción nueva

Hacen falta **tres cosas** y no se toca `InfractionManager`.

Ejemplo: "Frenada brusca" (`HARD_BRAKE`), una falta leve que un sistema nuevo detecta con la desaceleración.

### 1. Una constante en `InfractionIds`

```csharp
// Assets/Scripts/Evaluation/Data/InfractionIds.cs
/// <summary>Frenada brusca (leve).</summary>
public const string HardBrake = "HARD_BRAKE";
```

### 2. Un asset `InfractionDefinitionSO` agregado al catálogo

- En el Project: clic derecho en `Assets/Settings/Evaluation/` > **Create > Primer Volante > Evaluation > Infraction Definition**. Nombralo `Infraction_HARD_BRAKE`.
- Completá los campos:

| Campo | Valor |
|---|---|
| Id | `HARD_BRAKE` (igual que la constante) |
| Short Title | `Frenada brusca` |
| Description Template | `Frenaste de golpe a {speed} km/h. Anticipá las detenciones y frená de forma progresiva.` |
| Severity | `Minor` |

- Abrí `InfractionCatalog.asset` y agregá la definición a la lista **Definitions**.

> Si querés que el setup lo cree solo (y que quede en el repositorio sin pasos manuales), agregá una fila a `InfractionManagerSetup.Specs` y corré **Tools > Primer Volante > Setup Infraction Manager**.

### 3. Una llamada en el sistema que la detecta

```csharp
using PrimerVolante.Evaluation;
using UnityEngine;

public class HardBrakeDetector : MonoBehaviour
{
    private void OnHardBrake(float speedKmh)
    {
        // Una línea: id + quién detectó + dónde + parámetros ya formateados para la plantilla.
        InfractionReporting.Report(InfractionReport.Create(
            InfractionIds.HardBrake, this, transform.position,
            ("speed", speedKmh.ToString("F0"))));
    }
}
```

No hace falta una referencia al gestor ni al catálogo: el detector no sabe quién guarda la falta. Si el id no está en el catálogo, el gestor escribe un `LogWarning` con el id y **no** registra (no inventa una gravedad).

### Comprobarlo

Corré **Tools > Primer Volante > Run Infraction Manager Verification**: recorre por reflexión todas las constantes de `InfractionIds` y falla si alguna no tiene su asset en el catálogo, si la plantilla deja marcadores sin reemplazar o si la gravedad no coincide con la acordada. Cuando agregues un id nuevo, sumá su gravedad esperada a `s_ExpectedSeverity` en `InfractionManagerVerification.cs`.

---

## Leer el registro desde una UI futura

La UI no debería depender del `InfractionManager` concreto sino de `IInfractionLog`, que se obtiene por `InfractionReporting.Log` (es `null` si no hay gestor en la escena).

```csharp
using PrimerVolante.Evaluation;
using UnityEngine;

public class InfractionBoardExample : MonoBehaviour
{
    private IInfractionLog m_Log;

    private void OnEnable()
    {
        m_Log = InfractionReporting.Log;
        if (m_Log == null) return;

        // Lo que ya pasó antes de que la UI existiera.
        foreach (InfractionRecord record in m_Log.Records) Show(record);

        m_Log.InfractionRecorded += Show;
        m_Log.Cleared += HandleCleared;
    }

    private void OnDisable()
    {
        if (m_Log == null) return;
        m_Log.InfractionRecorded -= Show;
        m_Log.Cleared -= HandleCleared;
        m_Log = null;
    }

    private void Show(InfractionRecord record)
    {
        // Tablero (US24): record.ShortTitle. Filtro de visión (US23): record.Severity.
        // Reporte final (US26): record.Description.
        Debug.Log($"{record.ShortTitle} ({record.Severity}) | leves {m_Log.MinorCount}, graves {m_Log.MajorCount}");
    }

    private void HandleCleared()
    {
        // Vaciar el tablero.
    }
}
```

Tené en cuenta:

- **Orden de habilitación**: el gestor se registra en `OnEnable`. Si tu UI se habilita antes, `InfractionReporting.Log` puede ser `null` en ese momento; pedilo en `Start` o reintentá.
- **`Records`** es una lista de solo lectura en orden de llegada; **`MinorCount`**, **`MajorCount`** y **`TotalCount`** son los contadores independientes de faltas leves y graves.
- **`InfractionRecorded`** se dispara una vez por falta nueva. Las excepciones de un suscriptor se atrapan una por una (se loguean con `Debug.LogException`), así un suscriptor roto no impide que reciban el evento los demás.
- **`Clear()`** vacía los registros, pone los contadores en 0, reinicia `ElapsedSinceStart` y la secuencia vuelve a 1. Dispara `Cleared`: la UI tiene que vaciarse.
- **`LessonStarted`**: si la escena tiene un `LessonManager`, el gestor se suscribe a su evento `LessonStarted`; al empezar una lección llama a `Clear()` y guarda su `lessonId`. Los registros posteriores traen `LessonId` y `StepIndex`. Si el `LessonManager` aparece después del gestor, se le puede asignar por código con `BindLessonManager(...)`.
- **Fin de lección**: el gestor no hace nada con `LessonCompleted`. Los registros siguen disponibles para la pantalla de resultados hasta el próximo `Clear()`.

---

## Sin gestor en la escena

Si ninguna escena cargada tiene un `InfractionManager` habilitado:

- `InfractionReporting.Current` devuelve el respaldo `ConsoleInfractionReporter`, nunca `null`.
- El respaldo escribe un `LogWarning` con prefijo `[Infraction]`, el id y los parámetros. No tiene catálogo, así que no conoce los textos.
- `InfractionReporting.Log` es `null`: no hay contadores ni registros.
- Los detectores funcionan igual y no necesitan referencias en el Inspector.

Si hay **dos** gestores habilitados, el segundo no reemplaza al primero: escribe un `LogWarning` y queda **en espera**. Si el primero se deshabilita o se destruye, el que estaba en espera pasa a ser el activo y empieza a recibir las infracciones (con su registro vacío).

Los estáticos se limpian al entrar a Play Mode (`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`), así que funciona con "Enter Play Mode Options" sin recarga de dominio.

---

## Setup y verificación

| Qué | MenuItem | Qué hace |
|---|---|---|
| Setup | `Tools/Primer Volante/Setup Infraction Manager` | Crea o actualiza las 8 definiciones y el catálogo en `Assets/Settings/Evaluation/`; crea `CityWithInfractions.unity` como copia de `CityMerged3` si no existe; deja un GameObject `InfractionManager` con el catálogo asignado y guarda la escena. Es idempotente. |
| Verificación | `Tools/Primer Volante/Run Infraction Manager Verification` | Catálogo, funciones puras, gestor en memoria, contexto de lección, escenas, y al final corre las suites existentes (`Run Obstacle Collision Verification`, `Run Lesson Boundary Verification` y `Run Traffic Light Tests`) de forma informativa. |

Notas sobre la verificación:

- Las suites existentes **no suman** al resultado propio. Si hay una escena con cambios sin guardar, no se corren.
- Dos fallos conocidos y ajenos a US22 en esas suites: `VehicleLightingVerification` espera "1 fallo preexistente" desactualizado (da un falso fallo en `ObstacleCollisionVerification`, `NPCVerification` y `SoundIntegrationVerification`), y `LessonBoundaryVerification` falla "ningún collider ajeno dentro de las vallas" si se corre con `CityMerged3` o `CityWithInfractions` abiertas, porque abre `CityWithBarriers` en modo aditivo y las vallas se duplican. Corrida con `CityWithBarriers` abierta pasa completa.
- Correr las suites existentes puede dejar cambios en `CityMerged2.unity` y en un material del auto (`Mat_Mirror_Casing`) por reserialización; descartalos con git si no son tuyos.

---

## Limitaciones

- **Solo registra**: sin UI (US23, US24, US26, US27 a US29), sin puntaje ni calificación final, sin persistencia a disco. Los registros viven en memoria y se pierden al salir de Play Mode.
- **No cuentan como infracción** (decisión del equipo, por ahora): arranque o apagado inválido del motor (`VehicleController.OnEngineStartRejected`), cambio de marcha inválido (`VRGearShifter.CanShiftTo`) y frenado de emergencia de un NPC (`NPCSafetyStop.OnEmergencyBrake`). El diseño permite sumarlos después sin tocar el gestor (ver [Agregar una infracción nueva](#agregar-una-infracción-nueva)).
- **Sin atribución de culpa** (#23 US31): con el auto del jugador en movimiento, un choque con un NPC cuenta como falta aunque el NPC haya tenido la culpa. Solo se descartan los contactos con el jugador casi detenido (< 1 km/h). `ObstacleImpact.ObstacleSpeedKmh` ya trae la velocidad del NPC para cuando se implemente.
- **Sin reinicio de la prueba** al tocar las vallas (US30): solo se registra la falta.
- **Una sola escena lista**: `CityWithInfractions` es la copia de `CityMerged3` donde se probó. `CityMerged3` y las escenas de lecciones no tienen gestor y usan el respaldo de consola; para sumarlo en otra escena, agregá un GameObject con `InfractionManager` y el catálogo `Assets/Settings/Evaluation/InfractionCatalog.asset`. Las lecciones con infracciones (#19 TASK-204) todavía no existen.
- **Ids desconocidos**: si un detector informa un id que no está en el catálogo, no se registra (solo un `LogWarning`).
- **Un reporter activo a la vez**: `InfractionReporting` entrega las faltas solo al primero registrado (los demás esperan). No hay un bus para varios receptores; los que necesiten reaccionar se suscriben a `InfractionRecorded`.
- **Textos**: las descripciones están en español rioplatense neutro; no hay localización.
