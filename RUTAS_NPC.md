# Cómo definir el camino de los NPC (vehículos y peatones)

Guía de uso del sistema de NPC implementado en **#25 TASK-202**. Explica cómo hacer que un vehículo o un peatón siga una ruta, qué componentes intervienen y cómo se define la spline.

> Alcance: rutas **fijas** con Splines. Los NPC no esquivan obstáculos, no se frenan ante el jugador ni se ven entre sí (eso es de #23 US31). Ver [Qué no hace](#qué-no-hace).

---

## Resumen

Para que un NPC siga un camino se necesitan dos cosas:

1. Una **spline** en la escena, dentro de un `SplineContainer`.
2. Un **prefab `NPC_*`** en la escena con su `NPCPathFollower` apuntando a esa spline.

No hace falta agregar ni escribir ninguna clase: los prefabs ya vienen configurados.

---

## Clases que intervienen

| Pieza | Dónde está | Qué hace |
|---|---|---|
| `SplineContainer` | Un GameObject de la escena (paquete `com.unity.splines` 2.9.1) | Guarda la spline que define el camino. |
| `NPCPathFollower` | Ya viene en todos los prefabs `NPC_*` | Mueve al NPC por la spline. Es donde se asigna la ruta. |
| `NPCVehicleController` | Ya viene en los prefabs de vehículo | Gira las ruedas y orienta las delanteras (modelo de bicicleta inverso). |
| `NPCPedestrianController` | Ya viene en los prefabs de peatón | Escribe `Speed` en el Animator y ajusta su cadencia. |
| `NPCFootGrounding` | Ya viene en los prefabs de peatón | Corrige el sesgo de retargeting de los pies. |

Los prefabs están en:

- `Assets/Prefabs/NPCs/Vehicles/` (21 variantes de `NPC_*`).
- `Assets/Prefabs/NPCs/Pedestrians/` (`NPC_Pedestrian_01..04`).

Todos tienen ya un `Rigidbody` cinemático con interpolación y sin gravedad, un `BoxCollider` simple y (los vehículos) escala 0.69. **No cambies la escala** de los vehículos.

---

## Paso a paso

### 1. Crear la spline

1. Creá un GameObject vacío y agregale un `SplineContainer` (o usá el menú `GameObject > Spline`).
2. Dibujá los nudos con la herramienta de Splines de la Scene view.
3. Dejá el contenedor **como hermano** del NPC, nunca como hijo: si es hijo, la ruta se mueve junto con el NPC. En la demo, ruta y NPC cuelgan ambos del grupo `NPC_Demo`.
4. Mantené la **escala del contenedor uniforme** (1, 1, 1). Se admite posición y rotación.

### 2. Poner el NPC en la escena

Arrastrá el prefab `NPC_*` que quieras a la escena.

### 3. Asignar la ruta

En el componente `NPCPathFollower` de la **instancia en la escena** completá:

| Campo | Qué hace |
|---|---|
| `Path` | El `SplineContainer` de la ruta. |
| `Spline Index` | Qué spline del contenedor usar (0 si hay una sola). |
| `Initial Distance` | Metros sobre la ruta donde arranca. Al asignarlo, el NPC se coloca solo en el Editor sobre ese punto. |
| `End Behavior` | `Stop`: se detiene exactamente en el final. `Loop`: da la vuelta; **solo con spline cerrada** (si no, avisa y usa `Stop`). |
| `Play On Start` | Si empieza a moverse al iniciar la escena. |
| `Cruise Speed` | Velocidad crucero (m/s). |
| `Acceleration` / `Deceleration` | Aceleración y frenado máximos (m/s²). |
| `Max Lateral Acceleration` | Cuánto frena en las curvas: velocidad máxima en curva = √(aLat · R). |

> La ruta se asigna en la instancia de la escena. No se puede asignar en el **prefab asset**, porque un asset no puede referenciar objetos de una escena.

Al seleccionar el NPC, la ruta se dibuja en cian en la Scene view, con una esfera amarilla en el punto inicial.

#### Valores por defecto

| | Crucero | Aceleración | Frenado | Aceleración lateral |
|---|---|---|---|---|
| Vehículo | 8 m/s | 2.5 | 4 | 3 |
| Peatón | 1.4 m/s | 1 | 2 | 1.5 |

---

## Cómo definir bien la spline

### Altura (Y de los nudos)

El NPC toma la altura de la spline, así que la `y` de los nudos debe ser el piso. En `CityWithCar`:

- Asfalto: `y = 0`.
- Veredas: `y = 0.2`.
- Un peatón que cruza usa 0.2 sobre la vereda y 0 sobre la calzada.

### Posición lateral (vehículos)

El pivote del vehículo está en el piso, entre ejes, así que la spline es el **centro del carril**. En esta ciudad, el centro del carril está a **3.5 m** del eje de la calle y el tránsito es de **mano derecha**.

### Curvas

- Usá **radios amplios**: con el default de 3 m/s² laterales, un radio de 6 m se toma a 4.2 m/s.
- Evitá **esquinas vivas**: la curvatura es casi infinita, el NPC frena casi a cero y gira de golpe.
- Para esquinas de calle usá nudos de tangente rota (Bezier) con asa ≈ 0.55 × el radio. Para curvas suaves alcanzan las tangentes automáticas.

### Cerrada o abierta

| Caso | Spline | `End Behavior` |
|---|---|---|
| Circuito (una manzana) | Cerrada | `Loop` |
| Tramo de un solo sentido | Abierta | `Stop` (termina exacto en el final y dispara `OnReachedEnd`) |
| Peatón de ida y vuelta | Cerrada y muy angosta (como en la demo) | `Loop` |

### Varios NPC en la misma ruta

Los NPC **no se ven entre sí**. Para que no se superpongan:

- Separalos con `Initial Distance` (por ejemplo, uno a la mitad de la vuelta del otro).
- Dales la misma velocidad: con distinta velocidad terminarían alcanzándose y atravesándose.

---

## Atajo: armar un circuito con esquinas redondeadas

Hay un helper de Editor que arma una spline cerrada a partir de los vértices de un polígono, con esquinas redondeadas. Es lo que usa el setup de la demo y sirve de plantilla:

```csharp
// Assets/Scripts/Testing/Editor/NPCDemoRoutesSetup.cs
Spline spline = NPCDemoRoutesSetup.BuildRoundedLoop(
    vertices,          // IList<Vector3>, en el orden del recorrido
    cornerRadius: 6f,  // radio de las esquinas (se limita a la mitad del tramo)
    heightAt: null);   // opcional: (x, z) => y, para veredas y calzada

container.Spline = spline;
```

Los vértices colineales se conservan como nudos, lo que permite controlar la altura con `heightAt`. El contenedor debe estar en el origen sin rotación ni escala para que los nudos queden en espacio de mundo.

---

## Control por código

`NPCPathFollower` expone la API mínima que usará #23:

| Miembro | Descripción |
|---|---|
| `SetPath(SplineContainer, int)` | Asigna la ruta y vuelve a colocar al NPC en la distancia inicial. |
| `Play()` | Empieza o reanuda. Si había terminado, vuelve al inicio. |
| `Stop()` | Frena con la desaceleración configurada. |
| `SetCruiseSpeed(float)` | Cambia la velocidad crucero (m/s). |
| `TeleportToDistance(float)` | Coloca al NPC de inmediato en esa distancia, a velocidad cero. |
| `CurrentSpeed`, `CurrentAcceleration`, `CurrentYawRate` | Estado actual. |
| `DistanceTravelled`, `PathLength`, `IsMoving` | Progreso sobre la ruta. |
| `OnReachedEnd` | Evento al llegar al final (modo `Stop`). |

El movimiento es determinista: usa solo `Time.fixedDeltaTime` y no hay aleatoriedad.

---

## Verificar una ruta

Antes de dar por buena una ruta, comprobá que no pase por vehículos estacionados, farolas o árboles. Los NPC son cinemáticos y atraviesan cualquier cosa estática sin avisar.

```csharp
// Devuelve los GameObject raíz cuyos colliders toca la ruta (ignora calles, suelo y NPC_Demo).
List<GameObject> obstacles = NPCDemoRoutesSetup.FindRouteObstacles(container);

// Con una caja a medida (por ejemplo, un peatón):
NPCDemoRoutesSetup.FindRouteObstacles(container, new Vector3(0.3f, 0.75f, 0.3f), 1.1f);
```

El menú `Tools > Primer Volante > Run NPC Verification` hace esta comprobación **solo para el grupo `NPC_Demo`** de `CityWithCar`, no para rutas nuevas.

---

## Menús de Editor relacionados

| Menú (`Tools/Primer Volante/…`) | Qué hace |
|---|---|
| `Setup NPC Prefabs` | Regenera importers de Mixamo, Animator Controller y las 25 variantes de prefab. Es idempotente. |
| `Setup NPC Demo Routes (CityWithCar)` | Recrea el grupo `NPC_Demo` en `CityWithCar`. **Desactiva** los autos decorativos que bloquean la ruta de la manzana. |
| `Run NPC Verification` | Corre la verificación de NPC y las suites existentes. |

---

## Qué no hace

Está fuera del alcance de #25 (corresponde a #23 US31 y otros tickets):

- **No se frena ante obstáculos ni ante el jugador.** Si el auto del jugador choca un NPC, el NPC (cinemático, masa efectivamente infinita) sigue su ruta.
- **No revisa a otros NPC.** Dos NPC en la misma ruta con distinta velocidad se superponen.
- **No elige rutas solo.** Solo sigue splines fijas.
- **No tiene luces** (guiños, freno) ni reacción a choques.

---

## Notas

- Los nombres de menú de Splines (`GameObject > Spline`, "Draw Splines Tool") son del paquete 2.x y pueden variar entre versiones. Si alguno no coincide, buscá la herramienta de dibujo de splines en la barra de la Scene view.
- El diseño y las decisiones de este sistema están en `documents/prompts/Prompt Sonnet - Prefabs NPC (TASK-202).md`.
