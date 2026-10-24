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

El gestor de la escena es el GameObject raíz **`InfractionManager`** de `Assets/Scenes/car09/CityWithInfractions.unity`, con el catálogo asignado. Los assets están en `Assets/Settings/Evaluation/` (`InfractionCatalog.asset`, `Infraction_RED_LIGHT.asset`, `Infraction_SPEED_LIMIT.asset`, `Infraction_CRASH.asset`, `Infraction_GRAZE.asset` e `Infraction_WRONG_WAY.asset`).

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

Las fuentes son los cinco sistemas que detectan faltas. Los textos están en los assets y se editan sin tocar código.

| Id | Fuente | Gravedad | Título corto | Plantilla de la descripción | Parámetros |
|---|---|---|---|---|---|
| `RED_LIGHT` | `RedLightViolationDetector`: cruce de la línea con el semáforo en rojo | Grave | Semáforo en rojo | Cruzaste la línea de detención con el semáforo en rojo. Frená antes de la línea y esperá la luz verde. | (ninguno) |
| `SPEED_LIMIT` | `SpeedLimitEvaluator`: exceso de velocidad, **una por tramo** | Leve | Exceso de velocidad | Superaste el límite de {limit} km/h en este tramo. Respetá la velocidad máxima señalizada. | `limit` |
| `CRASH` | `ObstacleCollisionDetector`: choque con un cono, un vehículo (NPC o estacionado) o un peatón (siempre choque); `LessonBoundary`: el auto alcanza las vallas de la lección | Grave | Choque | Chocaste con {object} a {speed} km/h. Reducí la velocidad y mantené distancia con los obstáculos. | `object`, `speed` |
| `GRAZE` | `ObstacleCollisionDetector`: roce con un cono o un vehículo (NPC o estacionado) | Leve | Roce | Rozaste {object} a {speed} km/h. Calculá mejor las distancias al maniobrar. | `object`, `speed` |
| `WRONG_WAY` | `WrongWayDetector`: circulación en contramano, **una por episodio** (calle de doble mano por el carril contrario, o calle de mano única en contra del sentido) | Grave | Contramano | Circulaste en contramano {where}. Circulá siempre por la derecha y respetá el sentido de circulación señalizado. | `where` |

Los ids son pocos a propósito: qué se chocó no cambia la gravedad, así que viaja en el parámetro `object` y no en el id. `object` es el nombre con artículo indefinido y en minúscula, para que la plantilla se lea natural: `"un cono"`, `"un vehículo"`, `"un peatón"`, `"una valla"`. `speed` es la velocidad del jugador en km/h, formateada con `F0`.

`where` (contramano) es `"por el carril contrario de una calle de doble mano"` o `"en contra del sentido de una calle de mano única"` (constantes `WrongWayMath.WhereTwoWay` y `WrongWayMath.WhereOneWay`).

Ejemplo de un choque con parámetros (así reportan `ObstacleCollisionDetector` y `LessonBoundary`):

```csharp
InfractionReporting.Report(InfractionReport.Create(
    InfractionIds.Crash, this, contactPoint,
    ("object", ObstacleImpactMath.ObjectLabelFor(ObstacleKind.Cone)), ("speed", speedKmh.ToString("F0"))));
// Descripción: "Chocaste con un cono a 35 km/h. Reducí la velocidad y mantené distancia con los obstáculos."
```

Detalles de cada fuente:

- **Semáforo**: se informa en `TryRecordViolation`, después del cooldown de 3 s por vehículo. La posición es la del auto.
- **Velocidad**: se informa **solo en la transición** de "sin infracción" a "con infracción" dentro de un tramo. Solo lleva el límite (`limit`), no la velocidad: en ese instante apenas se cruzó la tolerancia y no representa el máximo alcanzado. Seguir excediendo en el mismo tramo no suma. El tramo se rearma cuando el auto sale por completo, así que volver a entrar puede sumar una falta nueva. Si el auto sale sin `OnTriggerExit` (teletransportado, apagado o destruido), el tramo se cierra solo: al volver a entrar, si hace más de 0.25 s que no llega un `OnTriggerStay`, se considera abandonado. También se cierra al deshabilitar el componente.
- **Vallas**: se informa una vez por pasada (la deduplicación por vehículo de `LessonBoundary` sigue igual) y se reporta como `CRASH` con `object` = `"una valla"` (constante privada `k_BarrierLabel`). La posición y la velocidad son las del contacto. Ya no existe una infracción propia de las vallas.
- **Obstáculos**: se informa un id por episodio de contacto. `ObstacleImpactMath.InfractionIdFor(ObstacleKind, ObstacleImpactSeverity)` devuelve `CRASH` o `GRAZE` según la gravedad (un peatón ya se clasifica siempre como choque en `Classify`), y `ObstacleImpactMath.ObjectLabelFor(ObstacleKind)` da el valor del parámetro `object` (`"un cono"`, `"un vehículo"`, `"un peatón"`; `"un obstáculo"` si el tipo no tiene mapeo). La velocidad es la del jugador (`PlayerSpeedKmh`); la posición es el punto de contacto. **Con el auto del jugador casi detenido no hay falta**: si `PlayerSpeedKmh` es menor que `Min Fault Speed Kmh` (1 km/h por defecto, configurable por obstáculo), quien chocó fue el obstáculo (un NPC que embiste, un peatón que camina contra el auto) y solo se emite el evento (`ObstacleImpactMath.IsPlayerFault`).
- **Contramano**: el detector `WrongWayDetector` va en el **auto del jugador** (Car07, Car08 y Car09, junto al `VehicleController`) y la regla vive en **zonas por carril** dentro de los prefabs de calle. Ver [Contramano en detalle](#contramano-en-detalle).

Las velocidades se formatean como enteros (`F0`). Los eventos C# y UnityEvents que ya existían (`OnViolationDetected`, `OnVehicleReachedBoundary`, `OnImpact`, `AnyImpact`) **se mantienen**: otros sistemas, como el audio, los usan. La falta se informa por `InfractionReporting` y el evento queda para reacciones. **No registres faltas suscribiéndote a esos eventos** (por ejemplo a `AnyImpact`): se contarían dos veces.

### Logs

El `InfractionManager` loguea cada falta si `m_LogEvents` está activo (por defecto sí):

```
[InfractionManager] 🚨 #1 [Leve] Exceso de velocidad: Superaste el límite de 40 km/h en este tramo. ... (EvaluationZone)
```

`RedLightViolationDetector` y `SpeedLimitEvaluator` tienen ahora un `m_LogEvents` (apagado por defecto) para sus logs propios, porque el gestor ya escribe la falta. Sin gestor, el respaldo de consola escribe un `[Infraction]`.

---

## Contramano en detalle

Es la infracción `WRONG_WAY` (grave, como en la Ley 24.449). Hay dos casos y los dos son **la misma regla**: **cada carril tiene un sentido permitido**. En una calle de **doble mano** (todas las de la ciudad hoy) los dos carriles tienen sentidos opuestos, mano derecha como en Argentina; en una calle de **mano única** los dos tienen el mismo sentido. El cartel es solo la señal visible: la regla es un dato de la pieza de calle.

### Piezas

Todo en `Assets/Scripts/Environment/` (namespace `PrimerVolante.Environment`).

| Pieza | Responsabilidad |
|---|---|
| `TrafficFlow` / `LaneSide` | `TwoWay`, `OneWayPositiveZ`, `OneWayNegativeZ` / carril `PositiveX` o `NegativeX` de la pieza. |
| `RoadLaneDirection` | En la raíz de cada prefab de calle con zonas. Un solo campo, `Flow` (por defecto `TwoWay`). Devuelve el sentido permitido de cada carril (`AllowedLocalDirection` / `AllowedWorldDirection`). |
| `RoadLaneZone` | Un hijo por carril bajo `LaneZones` (`Lane_PosX` y `Lane_NegX`): un `BoxCollider` **trigger** en la capa **`LaneZone`**. Sin `Update` ni callbacks de trigger: solo expone el sentido permitido de su carril. |
| `WrongWayDetector` | En el auto del jugador. Una consulta de física por paso (`FixedUpdate`) y el estado del episodio. |
| `WrongWayMath` / `WrongWayEpisode` | Funciones puras (clasificación por ángulo y acumulación del episodio), testeables sin escena. |
| `WrongWayExemptZone` | Zona de exención del control (ver [Práctica con conos](#práctica-con-conos)). |

### Zonas por carril

Cada pieza de calle recta de doble carril lleva dos cajas. En el espacio local de la pieza la calle corre a lo largo de Z, la línea central está en x = 0 y los cordones en x = ±7,15. Con `TwoWay`, el carril **+X** permite ir hacia **+Z** local y el carril **−X** hacia **−Z** local. Las cajas miden de la línea central al cordón en X y de 0 a 3 m en Y; en Z:

| Prefab | Zona |
|---|---|
| `Road Lane_01` | z de −10 a 10 |
| `Road Lane Half` | z de −5 a 5 |
| `Road Lane_02` | z de −10 a 10 (la tolerancia de distancia cubre el giro hacia el lado abierto) |
| `Road Lane_04` (sin salida) | z de −10 a 7,15 (hasta la tapa del cordón) |
| `Road Lane Bus Stop` | z de −20 a 20; la zona −X llega a x = −14,15 (la bahía) |
| `Road Lane_03` (con senda peatonal en z de 1 a 4) | solo z de −10 a 0: termina 1 m antes de la senda, como margen para doblar |

**No llevan zonas** los cruces, empalmes y esquinas (`Road Intersection_*`, `Road T_Intersection_*`, `Road Corner_*`; las sendas peatonales del mapa están todas dentro de estas piezas) ni los planos (`Road Tile*`, `Road Concrete Tile*`, `Road Sidewalk`, `Road Split Line`). Como las zonas están **dentro de los prefabs**, todas las escenas de ciudad las heredan sin tocarlas.

La capa `LaneZone` (índice 9) **no colisiona con ninguna capa**, ni consigo misma (matriz de colisiones en `DynamicsManager.asset`). Así los `Rigidbody` de los NPC y del jugador no generan eventos de trigger contra las zonas y el detector las encuentra igual con una consulta explícita a esa capa. Las consultas de física existentes (`NPCSafetyStop`, `GreenLightHonk` y los interactores XR) ignoran los triggers, así que no se ven afectadas.

### Detector en el auto

En cada `FixedUpdate`:

1. Si el detector está suspendido, descarta el episodio y sale.
2. Toma la velocidad horizontal de `Rigidbody.linearVelocity`. Por debajo de `m_MinSpeedKmh` (3 km/h) sale sin cambiar el estado.
3. `Physics.OverlapSphereNonAlloc` con un buffer preasignado en el **punto de consulta** del auto (`m_ProbeLocalOffset`, el centro de la caja del auto). Si hay una `WrongWayExemptZone`, descarta el episodio y sale. Si hay `RoadLaneZone`s, toma la de centro más cercano (en la junta entre dos piezas puede haber dos). Si no hay ninguna (cruce, plaza, fuera de la calle), **sale sin cambiar el estado**.
4. Clasifica con `WrongWayMath.Classify`: ángulo entre la velocidad y el sentido permitido del carril, en el plano horizontal. Hasta `m_CorrectMaxAngleDeg` (60°) es **a mano**; desde `m_WrongMinAngleDeg` (120°) es **contramano**; en el medio es **neutro** (doblar, maniobrar, cruzar en perpendicular).
5. Acumula en un `WrongWayEpisode`: en contramano suma metros y pone en 0 los metros a mano; si supera `m_WrongWayDistanceM` (**10 m**) y todavía no reportó, informa la falta. A mano suma metros; al pasar `m_RearmDistanceM` (10 m) cierra el episodio y lo rearma. Neutro no cambia nada.
6. Informa `WRONG_WAY` con `where` según la pieza (`IsOneWay`).

Decisiones de diseño:

- **Se mide con la velocidad, no con la aceleración**: al frenar la aceleración apunta hacia atrás y se leería como contramano.
- **Pisar la línea central no cuenta; pasarla con el centro del auto, sí.**
- **La marcha atrás cuenta por velocidad**: retroceder más de 10 m en contra del sentido del carril también es contramano.
- **Una infracción por episodio.** El estado vive en el auto, no en las piezas (una cuadra son varias piezas). Pasar por un cruce sin zona no corta ni rearma el episodio: un contramano que cruza una esquina y sigue en la cuadra siguiente es una sola falta. Para que cuente otra hay que circular a mano más de 10 m y volver a pasar los 10 m en contra.
- **Dos esquivas cortas separadas por menos de 10 m de manejo a mano se suman** (intencional): esquivar dos autos estacionados seguidos en la misma cuadra cuenta como un episodio largo.
- El detector expone `OnWrongWayStarted` y `OnWrongWayEnded` (para audio o UI futuros; **no los uses para registrar la falta**), `IsWrongWay`, `CurrentWrongDistanceM`, `IsSuppressed` y un gizmo del punto de consulta (verde a mano, amarillo neutro, rojo contramano, gris suspendido).
- Con `m_LogEvents` activo escribe logs `[WrongWayDetector]` de inicio y fin de episodio y de la falta (apagado por defecto: el gestor ya loguea la falta).

---

## Calles de mano única

La herramienta para marcar una calle de mano única está lista. **Hoy ninguna calle de la ciudad lo es** salvo la de demostración en `CityWithWrongWay` (ver [Limitaciones](#limitaciones)).

### Cómo marcar una calle

La regla vive en `RoadLaneDirection.Flow` de cada pieza. Es un override de propiedad de la instancia en la escena (no agrega componentes).

- **Con el inspector**: seleccioná una o varias piezas (edición múltiple) y usá los botones **Doble mano**, **Mano única → +Z local** o **Mano única → −Z local**. +Z local depende de cómo esté rotada la pieza: para saber hacia dónde va, mirá la flecha del gizmo.
- **Con el MenuItem de la Scene view** (sirve para una calle entera aunque sus piezas estén rotadas distinto): seleccioná las piezas, **mirá con la Scene view hacia donde va el tránsito** y ejecutá `Tools/Primer Volante/Wrong Way/Make Selected Roads One-Way (Scene View Direction)`. Cada pieza elige `OneWayPositiveZ` u `OneWayNegativeZ` según el signo de `dot(pieza.forward, cámara.forward)` en XZ. Si una pieza queda casi perpendicular a la cámara (|dot| < 0,5) la saltea y avisa en la consola.
- `Tools/Primer Volante/Wrong Way/Make Selected Roads Two-Way` vuelve las piezas a doble mano.
- `Tools/Primer Volante/Wrong Way/Toggle Lane Direction Gizmos` muestra u oculta las flechas de sentido en **todas** las piezas (celeste doble mano, naranja mano única); sin eso se ven solo en la pieza seleccionada.

Todo con `Undo`, marcando la escena como modificada. **Ninguna herramienta guarda la escena por su cuenta**: guardala vos.

### Carteles

Prefabs en `Assets/Prefabs/TrafficSigns/`: `OneWayProhibitedSign_Argentine` (contramano: disco rojo con barra blanca) y `OneWayDirectionSign_Argentine` (sentido de circulación: panel azul con flecha). Los crea el setup con el método procedural de `TrafficSignSetup` (mallas de disco y poste, textura generada por código).

- El de **contramano** va a la **salida** del tramo de mano única, mirando hacia quien entraría en contra.
- El de **sentido de circulación** va a la **entrada** del tramo y en las esquinas, mirando hacia quien entra a favor.
- Sobre la vereda y sin tapar la calzada. El cartel mira hacia donde apunta su +Z local.
- Los carteles son **solo visuales**: no tienen collider de detección ni referencia a las piezas. **La regla vive en `RoadLaneDirection`, no en el cartel**: un cartel sin pieza marcada no hace nada, y una pieza marcada sin cartel igual se controla.
- No se pudo confirmar el código ni el diseño exactos en el Anexo L del Decreto 779/95 (el Anexo L fue sustituido por el Decreto 196/2025 y no se pudo consultar el texto), así que los assets no llevan código en el nombre. Si se confirma, conviene renombrarlos y revisar el diseño.

### Demostración

`CityWithWrongWay` (copia de `CityWithInfractions`) trae una calle de demostración bajo el grupo `OneWayDemo`: 5 piezas `Road Lane_01` seguidas sobre z = 160, de x = 140 a x = 220 (entre una `Road Intersection_01` y una `Road T_Intersection_01`), de mano única hacia **+X** (este), con un cartel de sentido de circulación a la entrada (oeste) y uno de contramano a la salida (este). Ninguna ruta de NPC pasa por esa calle.

---

## Práctica con conos

La detección de contramano se **desactiva** (no se ignora la falta después) donde hay una práctica con conos. Hay dos formas, que se pueden combinar. La guía práctica paso a paso está en [`DESACTIVAR_CONTRAMANO.md`](DESACTIVAR_CONTRAMANO.md).

- **`WrongWayExemptZone`**: un `BoxCollider` trigger en la capa `LaneZone` que se coloca a mano alrededor del área de conos. Mientras el punto de consulta del auto está adentro, el detector no evalúa y descarta el episodio en curso. Hoy no hay ninguna colocada en las escenas de la ciudad.
- **`WrongWayDetector.SetSuppressed(object owner, bool suppressed)`**: para que una lección futura lo apague por tiempo (por ejemplo durante un paso). El detector está suspendido mientras algún dueño lo tenga suspendido, así dos sistemas no se pisan. Suspender descarta el episodio. `IsSuppressed` informa el estado.

```csharp
// En un paso de lección futuro:
private void OnStepStarted() { m_Detector.SetSuppressed(this, true); }
private void OnStepFinished() { m_Detector.SetSuppressed(this, false); }
```

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
| Setup | `Tools/Primer Volante/Setup Infraction Manager` | Crea o actualiza las 5 definiciones (`RED_LIGHT`, `SPEED_LIMIT`, `CRASH`, `GRAZE`, `WRONG_WAY`) y el catálogo en `Assets/Settings/Evaluation/`; crea `CityWithInfractions.unity` como copia de `CityMerged3` si no existe; deja un GameObject `InfractionManager` con el catálogo asignado y guarda la escena. Es idempotente. |
| Setup del contramano | `Tools/Primer Volante/Setup Wrong Way Detection` | Idempotente. Crea la capa `LaneZone` y su matriz de colisiones, `RoadLaneDirection` y las zonas por carril en los prefabs de calle (y limpia las piezas excluidas), agrega `WrongWayDetector` a Car07, Car08 y Car09, crea los prefabs de los dos carteles y deja `CityWithWrongWay.unity` (copia de `CityWithInfractions`) con la calle de demostración `OneWayDemo`. Es la única escena que guarda. |
| Verificación del contramano | `Tools/Primer Volante/Run Wrong Way Verification` | Funciones puras (`Classify`, `WrongWayEpisode`), capa, zonas de cada prefab con sus rangos, detector en los autos del jugador (y no en los NPC), catálogo y escenas (cada instancia de calle con zonas las tiene, `CityWithWrongWay` y sus carteles, `CityMerged3` y `CityWithInfractions` sin mano única y sin cambios). Al final corre `Run Infraction Manager Verification` de forma informativa. |
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
- **Pocas escenas listas**: `CityWithInfractions` es la copia de `CityMerged3` donde se probó y `CityWithWrongWay` es la copia de esa donde se probó el contramano. `CityMerged3` y las escenas de lecciones no tienen gestor y usan el respaldo de consola; para sumarlo en otra escena, agregá un GameObject con `InfractionManager` y el catálogo `Assets/Settings/Evaluation/InfractionCatalog.asset`. Las lecciones con infracciones (#19 TASK-204) todavía no existen.
- **Contramano, adelantamientos**: con la tolerancia de 10 m, un adelantamiento completo por el carril contrario **cuenta como falta**. No se detecta la línea discontinua ni el giro de guiño.
- **Contramano, cruces y esquinas**: no se evalúan (no tienen zonas). No se deduce "doblar por el carril equivocado": solo cuenta el tramo recto, y el episodio sigue a través del cruce.
- **Contramano, esquivas cercanas**: dos esquivas cortas separadas por menos de 10 m de manejo a mano se suman y pueden pasar la tolerancia.
- **Contramano, demostración solo en `CityWithWrongWay`**: `CityMerged3`, `CityWithInfractions` y las escenas de lecciones no tienen ninguna calle de mano única (igual heredan las zonas por los prefabs, así que el control de doble mano funciona en todas). Qué calles pasan a ser de mano única lo decide quien arma cada escena con la herramienta de [Calles de mano única](#calles-de-mano-única).
- **Contramano, solo el jugador**: el detector está en Car07, Car08 y Car09; los NPC no lo tienen y no se evalúan. No hay lecciones con conos todavía: `SetSuppressed` y `WrongWayExemptZone` están listas pero no se conectaron a ningún `LessonStepSO`.
- **Ids desconocidos**: si un detector informa un id que no está en el catálogo, no se registra (solo un `LogWarning`).
- **Un reporter activo a la vez**: `InfractionReporting` entrega las faltas solo al primero registrado (los demás esperan). No hay un bus para varios receptores; los que necesiten reaccionar se suscriben a `InfractionRecorded`.
- **Textos**: las descripciones están en español rioplatense neutro; no hay localización.
