# Cómo desactivar el control de contramano

Guía práctica para quien arma una lección o una escena y necesita que el auto del jugador **no sume la infracción `WRONG_WAY`** en un área, durante un paso o en toda una escena. Complementa a [`INFRACCIONES.md`](INFRACCIONES.md), que explica cómo detecta el contramano el sistema.

> Alcance: apagar o suspender el **detector**. No cambia cómo detecta (ángulos, tolerancia de 10 m, episodios) ni qué calles son de mano única. Para eso ver [Cambiar el sentido de una calle](#4-cambiar-el-sentido-de-una-calle-en-lugar-de-desactivar) y `INFRACCIONES.md`.

---

## Resumen

El detector (`WrongWayDetector`) está en el auto del jugador y mira en qué carril está el **punto de consulta** del auto (el centro de la caja, a 0,45 m detrás del pivote). Hay tres formas de desactivarlo, de la más acotada a la más amplia:

| Quiero apagarlo... | Usá | Sección |
|---|---|---|
| En un **área** (práctica con conos, estacionamiento, playón) | Una `WrongWayExemptZone` | [1](#1-en-un-área-wrongwayexemptzone) |
| Durante un **paso de lección** o un tiempo | `WrongWayDetector.SetSuppressed(owner, true/false)` | [2](#2-durante-un-paso-de-lección-o-un-tiempo-setsuppressed) |
| En **toda una escena** | Deshabilitar el componente `WrongWayDetector` de la instancia del auto | [3](#3-en-toda-una-escena-deshabilitar-el-componente) |

En los tres casos **no se ignora la falta después: no se detecta**. Y en los tres, apagar el detector **descarta el episodio en curso**: si el jugador venía en contramano, al reanudar la cuenta arranca de cero.

---

## 1. En un área: `WrongWayExemptZone`

**Cuándo conviene:** el área está fija en la escena y el jugador entra y sale de ella, como la práctica con conos, un estacionamiento o un playón donde maniobrar en cualquier sentido es normal.

### Pasos en el Editor

1. Abrí la escena. Creá un GameObject vacío (por ejemplo `WrongWayExempt_Conos`), de preferencia agrupado con los conos de la práctica.
2. Agregale el componente **`WrongWayExemptZone`**. Trae solo un `BoxCollider`, que ya queda como **trigger** y en la capa **`LaneZone`** (si lo agregaste en otro orden, verificá las dos cosas a mano: sin la capa `LaneZone` el detector no la ve).
3. Dimensioná el `BoxCollider` (ver abajo).
4. Seleccioná el objeto: la caja se dibuja magenta semitransparente con la etiqueta **"Sin control de contramano"**.

No hace falta tocar el auto, el detector ni las piezas de calle.

### Cómo dimensionarla

**La zona tiene que cubrir el punto de consulta del auto, no la carrocería.** El detector pregunta "¿el centro del auto está adentro?". Por eso:

- En planta, agrandá la caja **al menos 2,5 m** más allá del área de conos por cada lado: el auto mide 4,5 m de largo y 2 m de ancho, y puede estar tocando un cono con el centro todavía fuera de una caja que llegue justo hasta los conos.
- En altura, el punto de consulta está a 0,5 m sobre el piso: una caja de y = 0 a 3 m (centro en y = 1,5 sobre el piso, tamaño y = 3) sirve siempre.
- La zona **no necesita** cubrir la calle entera: solo el tramo donde el jugador puede estar en contra.
- Si la caja se superpone con las zonas de carril de la calle (`LaneZone` también), no hay problema: la exención gana sobre cualquier carril.

### Qué pasa con un episodio en curso

Al entrar el punto de consulta a la zona, el detector **descarta el episodio**: la distancia en contramano vuelve a 0 y se olvida si ya había informado. Al salir, la cuenta arranca desde cero (hace falta recorrer otros 10 m en contra para una falta nueva). Una falta que ya se registró antes de entrar **no se borra**.

---

## 2. Durante un paso de lección o un tiempo: `SetSuppressed`

**Cuándo conviene:** la lección decide cuándo se apaga (por ejemplo, durante un paso con maniobras libres) o el área no es fija. Es la forma que se conecta a un `LessonStepSO` futuro.

```csharp
detector.SetSuppressed(owner, true);   // suspende en nombre de owner
detector.SetSuppressed(owner, false);  // libera lo de owner
bool suspendido = detector.IsSuppressed;
```

### El modelo de varios dueños

El detector guarda un conjunto de **dueños** (`object`, normalmente `this`). Está suspendido **mientras el conjunto no esté vacío**. Así dos sistemas (por ejemplo, una lección y una zona de práctica) no se pisan: si uno libera, el otro sigue suspendiéndolo. Reglas:

- Usá **siempre el mismo objeto** como dueño para suspender y para liberar (`this`).
- Suspender dos veces con el mismo dueño cuenta una sola vez; liberar un dueño que no suspendió no hace nada.
- Un dueño `null` se ignora con un warning.
- **Todo dueño que suspende tiene que liberar**, incluso si se deshabilita antes de tiempo (ver el ejemplo). Como red de seguridad, el detector descarta solo a los dueños que son objetos de Unity ya **destruidos**, pero un dueño que sigue vivo y no libera deja el detector suspendido.

### Ejemplo completo

Un componente que suspende el control al empezar un paso y lo libera al terminar, con `OnDisable` como red de seguridad para no dejarlo suspendido:

```csharp
using PrimerVolante.Environment;
using UnityEngine;

/// <summary>Suspende el control de contramano mientras un paso de lección está activo.</summary>
public class SuspendWrongWayWhileActive : MonoBehaviour
{
    private WrongWayDetector m_Detector;
    private bool m_Suspended;

    // Llamado por la lección al empezar el paso.
    public void BeginStep()
    {
        if (m_Suspended) return;

        if (m_Detector == null) m_Detector = FindAnyObjectByType<WrongWayDetector>();
        if (m_Detector == null) return;     // escena sin detector: no hay nada que suspender

        m_Detector.SetSuppressed(this, true);
        m_Suspended = true;
    }

    // Llamado por la lección al terminar el paso.
    public void EndStep()
    {
        if (!m_Suspended) return;

        if (m_Detector != null) m_Detector.SetSuppressed(this, false);
        m_Suspended = false;
    }

    // Red de seguridad: si este objeto se apaga o se destruye con el paso en curso, libera igual.
    private void OnDisable()
    {
        EndStep();
    }
}
```

Notas del ejemplo:

- `FindAnyObjectByType` (no `FindFirstObjectByType`, que está obsoleta en este proyecto).
- Si el auto se reemplaza durante la lección, el dueño nuevo tiene que volver a pedir el detector: este ejemplo lo guarda una sola vez por simplicidad.
- Como suspender descarta el episodio, al liberar la cuenta arranca de cero.

---

## 3. En toda una escena: deshabilitar el componente

**Cuándo conviene:** una escena donde el contramano no tiene sentido evaluarlo del todo (una pista cerrada, una lección que solo maneja dentro de un playón).

### Pasos en el Editor

1. En la escena, seleccioná la **instancia del auto del jugador** (Car07, Car08 o Car09).
2. En el Inspector, desmarcá la casilla de habilitado de **`WrongWayDetector`**.
3. Guardá la escena. Queda como un solo override de `enabled` en esa instancia.

Con el componente deshabilitado no corre `FixedUpdate` (cero costo) y `OnDisable` ya descartó cualquier episodio.

### Por qué esto y no borrar el componente o las zonas

- Es **un override de una propiedad en esa instancia**, fácil de ver (negrita en el Inspector) y de revertir (menú del componente > *Revert*).
- **No toca los prefabs** del auto ni de las calles, que comparten todas las escenas: borrar el componente del prefab apaga el control en todas, y borrar zonas de una instancia de calle deja un override difícil de ver (ver [Qué no hacer](#5-qué-no-hacer)).
- Si más adelante se quiere reactivar, es tildar la casilla.

Si querés apagarlo por código en tiempo de ejecución, usá `SetSuppressed` (sección 2) en lugar de `detector.enabled = false`: así queda registrado quién lo apagó y no se pisa con otro sistema.

---

## 4. Cambiar el sentido de una calle en lugar de desactivar

A veces el problema no es que el control sobre, sino que **la calle permite el otro sentido**. Eso no se arregla desactivando el detector: se cambia la regla de la pieza.

- Una calle que hoy es de doble mano y se quiere de **mano única**, o al revés, se cambia con el inspector de `RoadLaneDirection` (botones **Doble mano**, **Mano única → +Z local**, **Mano única → −Z local**) o con `Tools/Primer Volante/Wrong Way/Make Selected Roads One-Way (Scene View Direction)` y `.../Make Selected Roads Two-Way`.
- La regla vive en `RoadLaneDirection.Flow`, **no** en los carteles: poner o sacar un cartel no cambia nada.
- El detalle está en la sección *Calles de mano única* de [`INFRACCIONES.md`](INFRACCIONES.md).

Regla práctica: **si el jugador va por donde debería poder ir, cambiá el sentido de la calle; si el jugador va por donde no debería poder ir pero la lección lo necesita, desactivá el control.**

---

## 5. Qué NO hacer

- **No borres `LaneZones` de una instancia de calle** en la escena: deja un override *Removed GameObject* difícil de ver, esa pieza queda sin control para siempre sin que nadie lo recuerde y `Run Wrong Way Verification` lo marca como falla.
- **No cambies la capa `LaneZone`** (ni la de las zonas, ni la máscara del detector, ni la matriz de colisiones): el detector y las exenciones dependen de ella, y la capa está configurada para no colisionar con nada.
- **No filtres la falta suscribiéndote a `InfractionRecorded`** para ignorarla: la falta ya quedó registrada y contada (`MajorCount`) y el reporte final la va a mostrar. Si no tiene que existir, no se tiene que detectar.
- No dejes una `WrongWayExemptZone` temporal de pruebas guardada en una escena de la ciudad.

---

## 6. Cómo comprobar que quedó desactivado

- **Gizmo del punto de consulta**: seleccioná el auto con el detector. Se dibuja una esfera con una línea vertical en el centro del auto: **verde** a mano, **amarillo** neutro, **rojo** en contramano, **gris** suspendido. Con una zona de exención, en Play Mode se ve que el color deja de cambiar mientras estás adentro.
- **`IsSuppressed`**: en Play Mode, desde `eval` o un componente de prueba, `FindAnyObjectByType<WrongWayDetector>().IsSuppressed` es `true` mientras haya algún dueño. Con el componente deshabilitado, `enabled` es `false`.
- **`CurrentWrongDistanceM` / `IsWrongWay`**: en contramano dentro de una zona de exención o con el detector suspendido, quedan en `0` / `false`.
- **Logs**: activá `m_LogEvents` en el `WrongWayDetector` (Inspector) para ver `[WrongWayDetector]` de inicio y fin de episodio y de la falta. Si apagaste el control y aparece un `[WrongWayDetector] 🚫 Contramano`, no quedó apagado.
- **Registro de infracciones**: el `InfractionManager` loguea cada falta con `[InfractionManager] 🚨 ... Contramano ...`. Después de manejar unos 40 m en contra en la zona apagada, no debería haber ninguna.
- **Verificación**: `Tools/Primer Volante/Run Wrong Way Verification` comprueba que las escenas de la ciudad tengan todas las zonas de carril y que no haya una `WrongWayExemptZone` guardada en `CityWithWrongWay`.
