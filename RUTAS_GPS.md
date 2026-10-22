# Cómo definir y utilizar rutas en el Sistema de GPS

Guía de uso y referencia arquitectónica del sistema de navegación GPS diegético implementado para **#16 PRI-40 (US17, US18, US19)**. Explica cómo funciona el sistema de navegación, cómo se calculan automáticamente las maniobras de giro, qué componentes intervienen y cómo crear nuevas rutas para futuras lecciones o escenarios.

---

## Resumen

El sistema de GPS diegético consta de:
1. Una **pantalla World Space** en la consola central del vehículo (arriba del botón de balizas `HazardButton`).
2. Una **cámara cenital ortográfica** (`GPS_TopDownCamera`) que renderiza un mapa esquemático minimalista a una `RenderTexture` a ~30 FPS en modo **Course-Up** (el mapa rota suavemente con el auto y la flecha UI apunta fija hacia arriba).
3. Una **ruta definida con Splines** (`SplineContainer`) que se dibuja con `LineRenderer` únicamente en la capa `GPSRoute` (invisible para los ojos del conductor por el parabrisas y visible solo en la pantalla del GPS).
4. Un sistema de **consumo de ruta** y **cálculo automático de maniobras de giro** (tipo Waze / Google Maps).

---

## Clases y Componentes que Intervienen

| Pieza | Dónde está | Qué hace |
|---|---|---|
| `GPSController` | En la raíz del vehículo (`Car09`) | Coordina la cámara cenital, la `RenderTexture`, la orientación Course-Up, la flecha y los textos de telemetría de la UI. |
| `GPSRouteRenderer` | En la raíz del vehículo (`Car09`) | Muestrea la spline, alimenta el `LineRenderer`, calcula la distancia restante metro a metro, consume el trazado y detecta curvas y arribo a destino. |
| `GPS_Display_Canvas` | Hijo de `Car09` en la consola central | World Space Canvas con marco biselado, pantalla digital, velocímetro, distancia restante y cartel de maniobras. |
| `GPS_TopDownCamera` | Hijo de `Car09` (a 45 m de altura) | Cámara ortográfica sin sombras que apunta hacia abajo y proyecta el mapa a la `RenderTexture`. Excluye vehículos, NPCs y decoraciones. |
| `GPS_Route_Line` | Hijo de `Car09` | Objeto con `LineRenderer` en la capa `GPSRoute` con material azul navegación (`#1F87FA`). |
| `SplineContainer` | En la escena (ej. `GPS_Circuit_Route`) | Guarda la spline (`com.unity.splines`) que define el recorrido esperado de la lección. |

---

## ¿Cómo sabe cuándo girar y para qué lado? (¿Es automático o scripteado?)

**Es 100% automático y puramente geométrico.** No hay triggers en la calle, ni zonas invisibles, ni scripts hardcodeados para cada esquina.

El método `GPSRouteRenderer.UpdateManeuverInstruction()` realiza el cálculo en tiempo real con matemática vectorial:

```
                  segDir (tramo futuro a K metros)
                         ↗
                        /
                       /   turnAngle = Vector3.SignedAngle(currentDir, segDir, Vector3.up)
                      /
[Auto] ─── currentDir ───>
```

### Algoritmo paso a paso:
1. **Vector actual:** Obtiene el vector normalizado desde la posición actual del auto hacia el próximo punto de la spline (`currentDir`).
2. **Escaneo hacia adelante:** Analiza los segmentos de la spline que están entre **2 y 60 metros por delante** del auto.
3. **Ángulo de giro (`SignedAngle`):**
   ```csharp
   float turnAngle = Vector3.SignedAngle(currentDir, segDir, Vector3.up);
   ```
   - Si `turnAngle > +30°`: La curva es hacia la **derecha**.
   - Si `turnAngle < -30°`: La curva es hacia la **izquierda**.
4. **Distancia al giro:**
   - Si la curva está a más de 20 metros: `"Gire a la derecha / izquierda en {X} m"`.
   - Si está a 20 metros o menos: `"Gire a la derecha / izquierda"`.
5. **Si no hay curvas pronunciadas:** Muestra `"Continúe recto"`.
6. **Últimos 15 metros:** Muestra `"Avanzando hacia el destino"`.
7. **Arribo (< 4 metros):** Muestra `"¡Llegaste a tu destino!"` y dispara el evento `OnDestinationReached`.

---

## Cómo Crear un Nuevo Circuito o Ruta

### Paso 1: Crear el contenedor de la Spline
1. En la escena abierta de Unity, creá un GameObject vacío en la raíz (ej. `Route_Lesson02`).
2. Agregale el componente **`SplineContainer`** (`Add Component > Splines > Spline Container`).
3. Mantené la **posición en (0, 0, 0)**, rotación en (0, 0, 0) y escala en (1, 1, 1) para que los nudos queden directamente en coordenadas de mundo.

### Paso 2: Dibujar los nudos en la Scene View
1. Con el `SplineContainer` seleccionado, activá la herramienta de dibujo de Splines en la barra de herramientas de la Scene view (o agregá nudos desde el Inspector).
2. **Nudo inicial:** Colocalo exactamente donde comienza el vehículo.
3. **Nudo final:** Colocalo exactamente en el punto de llegada (ej. cajón de estacionamiento, esquina, garaje).
4. **Modo de Tangente:** Usá `Broken` (tangente rota) o `Mirrored` en las esquinas para controlar los radios de curvatura suavemente.

---

## Cómo Definir Bien la Spline (Estándares del Proyecto)

Al igual que en [RUTAS_NPC.md](RUTAS_NPC.md), la ciudad tiene reglas geométricas estrictas:

### 1. Altura ($Y$ de los nudos)
- El asfalto está en $y = 0$.
- La spline debe tener sus nudos a **$y = 0.02$** (unos centímetros elevados para evitar z-fighting con el asfalto).
- El `GPSRouteRenderer` aplica automáticamente un offset configurado en `m_RouteHeight = 0.20f`.

### 2. Posición lateral (carril vehicular)
- El tránsito de la ciudad es de **mano derecha**.
- El centro del carril está a **3.5 metros** del eje central de la calle.
- Por ejemplo, en una avenida con eje en $X = 60$:
  - Carril hacia el Norte (+Z): $X = 63.5$.
  - Carril hacia el Sur (-Z): $X = 56.5$.

### 3. Curvas en esquinas
- **Radios amplios:** Usar radio $R \approx 6$ metros para esquinas de 90°.
- **Tangente Bézier ideal:** Para un arco circular de 90°, el tamaño del mango de la tangente se calcula como:
  $$K \approx 0.55228 \times R \approx 3.31 \text{ metros}$$
- Evitar esquinas vivas en ángulo recto (0 metros de radio); si la esquina es en ángulo recto, la línea se cortará visualmente y el cálculo de maniobra saltará de golpe.

---

## Asignación al Vehículo / GPS

Una vez creada la spline en la escena, tenés dos formas de asignarla:

### Opción A: Por Inspector en la Escena
1. Seleccioná `Car09` en la jerarquía.
2. Buscá el componente **`GPSRouteRenderer`**.
3. Arrastrá tu GameObject de la spline al campo **`Route Container`**.
4. ¡Listo! Al entrar en Play, el GPS inicializa la ruta, calcula las distancias y dibuja la línea automáticamente.

### Opción B: Por Código (para Lecciones)
```csharp
GPSRouteRenderer routeRenderer = car.GetComponent<GPSRouteRenderer>();
routeRenderer.RouteContainer = miSplineDeLaLeccion;
// Inicializa y dibuja automáticamente
```

---

## Capas y Renderizado (Por qué no se ve en la calle real)

El sistema utiliza **Layer Culling** para garantizar que el juego se sienta como un simulador y no un juego arcade:
1. El `LineRenderer` se asigna a la capa **`GPSRoute`**.
2. **Cámara del Conductor (VR / Ojos):** Tiene desmarcada la capa `GPSRoute` en su `cullingMask`. Cuando mirás por el parabrisas, el asfalto se ve 100% normal.
3. **Cámara del GPS (`GPS_TopDownCamera`):** Es la única cámara que tiene activada la capa `GPSRoute`. Por ende, la ruta solo se renderiza en la textura de la pantalla del auto.

---

## Atajo: armar o asignar un circuito por Código

Si estás programando una lección o prueba automatizada, podés definir los nudos de una spline o generar un circuito por código:

```csharp
using UnityEngine.Splines;
using Unity.Mathematics;
using PrimerVolante.UI;

// 1. Obtener el renderer en el auto
GPSRouteRenderer routeRenderer = car.GetComponent<GPSRouteRenderer>();

// 2. Asignar un SplineContainer existente
routeRenderer.RouteContainer = miSplineContainer;

// 3. O crear los nudos programáticamente:
Spline spline = new Spline();
spline.Closed = false;

// Agregar nudos con tangentes Bézier (R = radio, K = 0.55228 * R)
spline.Add(new BezierKnot(new float3(63.5f, 0.02f, 95.0f)), TangentMode.Broken);
spline.Add(new BezierKnot(new float3(63.5f, 0.02f, 110.5f)), TangentMode.Broken);

container.Spline = spline;
routeRenderer.RouteContainer = container;
```

---

## Probar en Modo Desktop (Sin Casco VR)

Para probar la pantalla de GPS sin necesidad de colocarse el casco de Realidad Virtual:

1. Abrí la escena `Assets/Scenes/TestingScenes/GPSNavigationDemo.unity`.
2. Presioná **Play**.
3. El componente `[Desktop_Cockpit_Tester]` activará automáticamente una cámara en los ojos del conductor:
   - **Click derecho + Mouse**: Mirar alrededor de la cabina.
   - **F**: Enfocar la vista directamente sobre la pantalla del GPS.
   - **R**: Resetear la vista hacia adelante.
   - **Piloto Automático**: Por defecto el auto conduce solo siguiendo la ruta a 35 km/h.
   - **M**: Alternar entre Piloto Automático y control manual por teclado (**W/A/S/D**).

> Cuando se ejecute en un visor VR activo (Meta Quest / SteamVR), el tester de escritorio se desactiva solo automáticamente para no interferir con la experiencia inmersiva.

---

## Herramientas de Editor Disponibles

| Menú (`Tools/Primer Volante/GPS/...`) | Qué hace |
|---|---|
| `Setup GPS Demo Scene` | Configura de forma idempotente la escena de prueba `GPSNavigationDemo.unity`, cableando `Car09`, la pantalla y el circuito urbano. |
| `Setup GPS in Car09 Prefab` | Actualiza y guarda la pantalla, cámara cenital y scripts directamente en el asset `Car09.prefab`. |
| `Add Desktop Tester to Scene` | Agrega el simulador de prueba en escritorio para testear el circuito sin casco VR. |
| `Verify GPS Integration` | Ejecuta la suite de verificación automatizada (47+ tests) comprobando capas, cámara, prefab y reglas de `AGENTS.md`. |

---

## Qué no hace (Alcance actual)

Para mantener el diseño enfocado y el rendimiento óptimo:
- **No recalcula rutas alternativas en tiempo real (A* dinámico):** Si el jugador se desvía de la ruta establecida, el GPS no recalcula un nuevo camino por otra manzana (sigue mostrando la ruta asignada hasta que vuelva a ella).
- **No tiene sintetizador de voz (TTS audio):** Las indicaciones son exclusivamente visuales en la pantalla digital del tablero.
- **No proyecta flechas holográficas en el parabrisas:** Por realismo y diseño diegético de simulador, las flechas están en la pantalla del tablero, no en el parabrisas (salvo que en el futuro se agregue un HUD head-up display).

