# Cómo activar la infracción de giro sin señalizar en otras escenas

Guía práctica para quien arma una lección o una escena y quiere que el auto del jugador **sume la infracción `TURN_SIGNAL`** (leve, "Giro sin señalizar") cuando dobla sin poner el guiño con anticipación. Complementa a [`INFRACCIONES.md`](INFRACCIONES.md), que explica el sistema de infracciones, y es la hermana de [`DESACTIVAR_CONTRAMANO.md`](DESACTIVAR_CONTRAMANO.md).

> Alcance: la detección está hoy **solo en `GPSTurnSignalDemo`**. Las demás escenas de la ciudad no la tienen hasta que apliques esta guía. El detector va como componente de la **instancia** del auto en la escena, no en los prefabs del auto.

---

## Resumen

| Quiero... | Usá | Sección |
|---|---|---|
| Activarla en la escena abierta | `Tools/Primer Volante/Turn Signal/Apply To Open Scene` | [2](#2-aplicarla-con-la-herramienta) |
| Activarla en varias escenas a la vez | `Tools/Primer Volante/Turn Signal/Apply To Selected Scenes` | [2](#2-aplicarla-con-la-herramienta) |
| Sacarla de la escena abierta | `Tools/Primer Volante/Turn Signal/Remove From Open Scene` | [5](#5-desactivarla-en-una-escena) |
| Hacerlo a mano, sin la herramienta | Agregar `TurnSignalDetector` al auto | [3](#3-el-equivalente-manual-paso-a-paso) |
| Eximir un área (rotonda, playón, práctica) | Una `TurnSignalExemptZone` | [4](#4-agregar-zonas-exentas) |
| Apagarla durante un paso de lección | `TurnSignalDetector.SetSuppressed(owner, true/false)` | [4](#suspenderla-durante-una-lección-setsuppressed) |

---

## 1. Qué detecta `TURN_SIGNAL` y cuáles son las reglas

Cuando el auto dobla, el detector mira si el **guiño** (la palanca de giro) estaba puesto **hacia el lado correcto, con anticipación**. Si no, suma una falta leve con la descripción, por ejemplo: *"Doblaste a la derecha sin poner el guiño. Poné el guiño con anticipación antes de doblar."*

- **Qué es un giro.** El detector guarda un rumbo de referencia mientras vas derecho. Hay un giro cuando el rumbo se desvía **45° o más** de esa referencia. Un cambio de carril (unos 15° y vuelta) **no** es un giro. Una vuelta en U es **un solo giro**: una sola falta.
- **Anticipación estricta de 20 m.** El guiño tiene que estar puesto, hacia el lado del giro, **en todo el tramo de los 20 m anteriores al inicio del giro** (incluido el instante en que empieza). Un guiño puesto tarde, con cortes o hacia el lado contrario es falta; el guiño que se apaga solo durante el giro no importa.
- **Ventana acortada.** Si el giro empieza a menos de 20 m del giro anterior, o de que arrancaste o frenaste del todo, la ventana se acorta: no se te exige lo que no pudiste hacer. Por eso dos giros seguidos bien señalizados no dan falta.
- **Margen de reacción de 15 m.** Después de **terminar un giro** (con falta, bien señalizado o exento) no se exige guiño durante los siguientes **15 m**: la palanca se auto-cancela al volver el volante y el conductor necesita unos metros para volver a ponerla. La ventana de 20 m arranca recién 15 m después del fin del giro anterior. En una cuadra de 40 m la ventana sigue siendo de 20 m completos; en una cuadra corta de 20 m alcanza con poner el guiño antes de girar (si lo ponés y lo cortás, igual es falta por anticipación). Si frenás casi del todo (menos de 3 km/h) o ponés marcha atrás sin girar dentro de esos 15 m, el rastreo se reinicia pero **conserva el margen que quedaba** (frenar un instante en una senda después de girar no te quita esos metros); si la frenada llega cuando ya pasaron los 15 m, no cambia nada. Una pausa con un giro parcial o ya detectado en curso conserva el giro y su borde. En cambio, un **arranque**, una **suspensión del detector** (`SetSuppressed`, deshabilitarlo) o `ResetTracking()` **no** conservan el margen: ahí la ventana se corta, como antes, una muestra después del reinicio. El valor configurado nunca baja de 0,5 m.
- **Las balizas no cuentan.** Las balizas (hazard) no son un guiño: solo cuenta la palanca.
- **Marcha atrás y baja velocidad.** Por debajo de 3 km/h y en marcha atrás **no se evalúa ni se acumula distancia**. Lo girado en reversa no cuenta como giro. Si ya estabas en medio de un giro y frenás o ponés reversa (una maniobra de 3 puntos, una U lenta), el giro se conserva: como máximo hay **una** falta por giro.
- **Curvas y calles sin salida exentas.** Hay zonas exentas dentro de los prefabs de calle: las curvas `Road Corner_01` y `Road Corner_02` (toda la pieza) y las puntas de calle sin salida `Road Lane_04` (el extremo cerrado más 6 m de la mitad abierta: z local de −6 a +10). Un giro en el que el auto estuvo en una zona exenta, entre el inicio y el momento en que se detecta, se consume **sin falta**.
- **Una evaluación por giro.** El giro se evalúa una sola vez, al llegar a 45°, y no vuelve a evaluarse hasta que termina: cuando el rumbo se estabiliza unos 5 m (con tolerancia a las correcciones del volante). Como tope, a los 30 m de la detección el giro se cierra igual, pero no se detectan giros nuevos hasta que el rumbo sea estable (una vuelta amplia o un círculo da una sola falta).
- **Marcha atrás en medio de un giro.** Si el giro ya se detectó (llegó a 45°), la marcha atrás o una frenada no lo reinician: una maniobra de 3 puntos da una sola falta. Si era un giro parcial (entre 10° y 45°) y vas en marcha atrás, se descarta: lo rotado en reversa no se suma al próximo giro hacia adelante (estacionar no da falta).

Todos los umbrales (ángulos, anticipación, velocidad mínima, estabilidad, tope) están en el Inspector del `TurnSignalDetector`, con un `Tooltip` cada uno.

---

## 2. Aplicarla con la herramienta

La herramienta está en `Assets/Scripts/Testing/Editor/TurnSignalSceneTool.cs`. Antes de tocar la escena **asegura todo lo que no es de la escena**: la capa `LaneZone` y su matriz de colisiones, la zona exenta en los prefabs `Road Corner_01`, `Road Corner_02` y `Road Lane_04`, la definición `Infraction_TURN_SIGNAL.asset` y el catálogo de infracciones. Es idempotente: podés correrla las veces que quieras.

### En la escena abierta

1. Abrí la escena y guardala si tiene cambios pendientes: si la escena abierta está con cambios sin guardar, la herramienta **aborta** con un error y no guarda nada (lo mismo vale para `Remove From Open Scene`).
2. Menú **`Tools/Primer Volante/Turn Signal/Apply To Open Scene`**.
3. La herramienta busca el **auto del jugador** (la instancia de `Car07`, `Car08` o `Car09`, o cualquier objeto con `VehicleController` + `WrongWayDetector`; si hay más de uno, procesa todos y lo informa), le agrega un `TurnSignalDetector` con la máscara de capas en `LaneZone`, marca la escena como modificada y **la guarda**.
4. Si el auto ya tenía el detector, **no lo duplica**: solo corrige la máscara si estaba mal.
5. En la consola aparece `[TurnSignalSceneTool] ✅ <escena>: <auto> (...)`. Si no encuentra el auto, avisa con un warning y no toca nada.

### En varias escenas

1. En la ventana **Project**, seleccioná las escenas (`.unity`) que querés procesar.
2. Menú **`Tools/Primer Volante/Turn Signal/Apply To Selected Scenes`**. Pide confirmación con la lista de escenas.
3. Abre cada escena, aplica, la guarda y, al final, **reabre la escena que tenías abierta**.
4. Si hay una escena abierta con cambios sin guardar, se corta antes de empezar: guardala primero.

Desde código o desde `eval`, el método público `TurnSignalSceneTool.ApplyToScenes(string[] paths)` hace lo mismo **sin diálogos** (también existe `RemoveFromScenes`).

Escenas de la ciudad que hoy tienen las piezas de calle y a las que probablemente quieras aplicarla: `Scenes/car09/CityMerged3`, `CityWithInfractions`, `CityWithWrongWay`, `Lesson_01_BasicDriving2`, `Scenes/Lessons/Lesson_01_BasicDriving`, `Scenes/TestingScenes/GPSNavigationDemo` y `GPSWrongWayDemo`. `GPSTurnSignalDemo` ya la tiene.

---

## 3. El equivalente manual, paso a paso

Si preferís no usar la herramienta (por ejemplo, para una sola escena):

1. **Una vez por proyecto**, corré **`Tools/Primer Volante/Setup Turn Signal Detection`**. Crea (o ajusta) la zona exenta de los prefabs, el asset de la infracción, el catálogo y la escena `GPSTurnSignalDemo`. Si ya se corrió en el proyecto, no hace falta repetirlo.
2. Abrí la escena y seleccioná la **instancia del auto del jugador** (el objeto raíz del auto, el que tiene `VehicleController` y `Rigidbody`).
3. `Add Component` > **`TurnSignalDetector`**. Queda como un override de esa instancia: no se modifica el prefab del auto.
4. En el Inspector, comprobá **`Zone Mask`**: tiene que ser **solo la capa `LaneZone`** (índice 15). Si dice `Nothing`, elegí `LaneZone`. (Si la agregás por código en tiempo de ejecución, `Awake` la completa sola.)
5. La escena tiene que tener un **`InfractionManager`** que registre la falta; sin él, el respaldo de consola la escribe como `[Infraction]` (ver `INFRACCIONES.md`, "Sin gestor en la escena").
6. Guardá la escena.

Para ver qué hace, activá **`Log Events`** en el detector: escribe `[TurnSignalDetector]` por cada giro detectado, falta, giro bien señalizado y exención.

---

## 4. Agregar zonas exentas, suspenderla y casos especiales

### Agregar más zonas exentas: `TurnSignalExemptZone`

**Cuándo conviene:** un área donde doblar sin guiño es normal (una rotonda, un estacionamiento, un playón, una práctica de maniobras).

1. En la escena, creá un GameObject vacío (por ejemplo `TurnSignalExempt_Playon`).
2. Agregale el componente **`TurnSignalExemptZone`** y un `BoxCollider` marcado como **trigger**.
3. Ponelo en la capa **`LaneZone`**. Sin esa capa el detector no la ve.
4. Dimensioná la caja. **La zona tiene que cubrir el punto de consulta del auto, no la carrocería**: es el centro del auto, a 0,45 m detrás del pivote y 0,5 m sobre el piso. Agrandala **al menos 2,5 m** más allá del área por cada lado, y en altura de y = 0 a 3 m (centro en y = 1,5, tamaño y = 3).

La regla: si el auto **estuvo dentro de la zona en cualquier momento entre el inicio del giro y el momento en que llega a 45°**, ese giro no suma falta. Por eso la zona de una punta sin salida cubre el extremo cerrado y 6 m de la mitad abierta (z local de −6 a +10, no la pieza entera): el auto pasa por ella antes de completar la U, pero salir de la calle y doblar sin guiño en la intersección contigua sigue dando falta.

Para una pieza de calle nueva que deba llevar la zona en todas sus instancias, agregala **al prefab** (como hace `TurnSignalSetup.EnsureExemptZone`), no a cada instancia.

### Suspenderla durante una lección: `SetSuppressed`

**Cuándo conviene:** la lección decide cuándo se apaga (por ejemplo, un paso de maniobras libres o un slalom de conos).

```csharp
detector.SetSuppressed(owner, true);   // suspende en nombre de owner
detector.SetSuppressed(owner, false);  // libera lo de owner
bool suspendido = detector.IsSuppressed;
```

Funciona igual que el de contramano: un conjunto de **dueños** (`this`), suspendido mientras no esté vacío; usá **siempre el mismo objeto** para suspender y liberar; todo dueño que suspende tiene que liberar (incluso en `OnDisable`). Suspender reinicia el rastreo y no se informa ninguna falta mientras dure. El ejemplo completo de `DESACTIVAR_CONTRAMANO.md` (sección 2) se adapta cambiando `WrongWayDetector` por `TurnSignalDetector`.

---

## 5. Desactivarla en una escena

- **Con la herramienta:** abrí la escena y corré **`Tools/Primer Volante/Turn Signal/Remove From Open Scene`**. Saca el `TurnSignalDetector` de la escena y la guarda.
- **Sin sacarlo:** en el Inspector de la instancia del auto, desmarcá la casilla de habilitado de `TurnSignalDetector`. Con el componente deshabilitado no corre ni cuesta nada.
- **Por código, durante la ejecución:** `SetSuppressed` (sección 4), no `enabled = false`, para que quede registrado quién lo apagó.

No hace falta tocar los prefabs ni las zonas exentas: las zonas, sin detector, no hacen nada.

---

## 6. Cómo verificar el resultado

1. **Verificación automática:** `Tools/Primer Volante/Run Turn Signal Infraction Verification`. Comprueba la lógica con pasos sintéticos, la definición y el catálogo, las zonas exentas de los prefabs (y que `Road Lane_04` cubra z local de −6 a +10), el detector en `GPSTurnSignalDemo`, y la herramienta de escenas sobre una copia temporal. Tiene que terminar con todas las pruebas pasadas. Esa verificación **no** es `Run Turn Signal Verification`, que verifica la palanca de guiño.
2. **Prueba manual en Play Mode** (con el visor o el simulador de dispositivo):
   1. Con `Log Events` activado, manejá hasta una esquina y doblá **sin guiño**: debería aparecer `[TurnSignalDetector] 🚫 Giro a la derecha sin poner el guiño` y una infracción leve en el `InfractionManager`.
   2. Repetí el giro poniendo el guiño **al menos 25 m antes**: sin falta (`✅ Giro ... bien señalizado`).
   3. Doblá en una curva `Road Corner_01` sin guiño: sin falta (`ℹ️ ... en zona exenta`).
   4. Hacé una maniobra de 3 puntos a mitad de cuadra sin guiño: **una** sola falta.
3. **Gizmo:** seleccioná el auto con el detector. Se dibuja una esfera con una línea vertical en el punto de consulta: **verde** va derecho, **cian** girando, **amarillo** en zona exenta, **gris** suspendido.

---

## 7. Observaciones y decisiones para el futuro

Cosas que se decidieron a propósito o que quedan pendientes. No son errores a corregir sin antes revisar este apartado.

- **Teletransporte o reinicio del auto.** Si el auto se teletransporta (por ejemplo `LessonManager` llama a `VehicleController.ResetToBaseState`) a mitad de un giro, el detector compara contra el rumbo previo y puede dar una falta falsa. **Decisión: no se resuelve en el algoritmo de detección.** Corresponde a un futuro gestor de estado (tipo `StateManager`) que reinicie todo el estado de la simulación al reiniciar, incluido el rastreo del `TurnSignalDetector`. Para eso existe la API pública **`TurnSignalDetector.ResetTracking()`**: descarta el historial, la referencia y el giro en curso, y el rumbo actual pasa a ser la referencia en el próximo paso. Deshabilitar y volver a habilitar el componente tiene el mismo efecto. El `StateManager` todavía no existe y no se implementó.
- **Detenido en la esquina.** Si el jugador está detenido cerca de la esquina, pone el guiño y dobla, el giro cuenta como **bien señalizado** aunque haya recorrido poco o nada con el guiño puesto: al detenerse (menos de 3 km/h) el rastreo se reinicia y la ventana de anticipación se acorta a lo recorrido desde entonces. **Es intencional, no un bug.**
- **Giro inmediato tras otro.** Un giro que empieza antes de que el rumbo se estabilice 5 m después del anterior se absorbe en ese y no se evalúa por separado. En la ciudad actual no ocurre porque las curvas no tocan intersecciones. Es un pendiente conocido.
- **Margen de reacción de 15 m tras un giro (cuadras cortas de las plazas).** Los lados cortos de las plazas son una sola pieza de 20 m entre dos intersecciones (por ejemplo, las calles x=0, x=120 y x=240 entre z=120 y z=160 en `GPSTurnSignalDemo`). Al terminar el primer giro la palanca ya se auto-canceló, y el conductor necesita unos metros para volver a ponerla; esos metros caían dentro de la ventana de 20 m y daban una falta "sin señalizar con suficiente anticipación" aunque hubiera puesto el guiño apenas pudo. Se agregó `ReactionMarginM` (15 m por defecto, `m_ReactionMarginM` en el `TurnSignalDetector`): la ventana arranca `borde + 15 m` cuando el borde es el fin de un giro anterior. Se eligieron 15 m como tiempo de reacción tras el auto-cancelado de la palanca (a 30 km/h son unos 2 s) dejando en la cuadra de 20 m un tramo final de unos 5 m en el que el guiño igual tiene que estar puesto. El borde por reinicio de arranque, supresión o `ResetTracking` no cambia; una frenada total o una reversa sin giro dentro de los 15 m reinician pero conservan el margen que quedaba. Consecuencia aceptada: en una cuadra corta, quien pone el guiño recién en el último momento (al iniciar el giro) no recibe falta; en cuadras de 40 m o más, la regla sigue siendo la de 20 m.
- **Duplicación con `WrongWayDetector` y doble `OverlapSphere` por paso.** Ambos detectores consultan las zonas de la capa `LaneZone` por separado y repiten lógica de velocidad y rumbo. Se posterga extraer código compartido o unificar la consulta de física hasta que aparezca un tercer detector similar.
