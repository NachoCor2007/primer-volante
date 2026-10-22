# Clips orgánicos pendientes (US20 #12)

Los sonidos "mecánicos" (motores, bocinas, sirena, pasos, semáforo, etc.) ya tienen un placeholder procedural en
`Placeholders/`. Los sonidos **orgánicos** (voces, pájaros, perros, multitudes) quedan como **espacios vacíos** en los
perfiles de `Profiles/`: el código funciona igual con esos campos en `null` (silencio, sin excepciones ni warnings).

Para completarlos **no hace falta tocar código**:

1. Descargá el clip (ver búsquedas sugeridas) y copialo a `Assets/Audio/Environment/` (por ejemplo en una subcarpeta `Organic/`).
2. Importación recomendada:
   - todo lo que suena en 3D (emisores, dispersos 3D, pasos, voces): **Force To Mono** activado;
   - one-shots cortos (< 3 s): Load Type `Decompress On Load`, compresión `ADPCM` o `Vorbis`;
   - loops largos (≥ 10 s): `Vorbis` + `Streaming` (o `Compressed In Memory` si dura ~10 s);
   - los lechos 2D del `Zone_*` pueden quedar estéreo.
3. Arrastrá el clip al campo indicado del perfil (columna "Perfil / campo").
4. Anotá el clip en `Assets/Audio/CREDITS.md` (archivo, URL, autor, licencia). Preferí CC0 o licencias que permitan uso en un proyecto de tesis con atribución.
5. Corré `Tools/Primer Volante/Run Environment Audio Verification`: la lista de orgánicos pendientes se imprime como información.

También podés ver qué falta de cada perfil con clic derecho sobre el asset > **Validar clips**.

## Tabla de clips pendientes

| Perfil / campo | Qué es | Duración sugerida | Loop | Canales | Búsqueda en inglés (Freesound / Pixabay / Sonniss) |
|---|---|---|---|---|---|
| `Zone_Plaza` / `ScatterClips` | Murmullo de plaza lejano, pájaros, palomas, chicos jugando a lo lejos (varios clips cortos) | 2–6 s cada uno | no | mono | `plaza ambience distant crowd`, `park birds chirping`, `pigeons cooing`, `children playing distant`, `kids laughing park far` |
| `Emitter_PlazaLife` / `OneShots` | Pájaros y palomas cercanos, risas de chicos, conversación corta | 1–4 s cada uno | no | mono | `sparrow chirp`, `pigeon wings flap`, `child laugh short`, `park bench talking low` |
| `Emitter_FoodAndDrink` / `LoopClip` | Murmullo de bar o restaurante: charla, vajilla, música apagada de fondo | 20–40 s | sí (sin corte) | mono | `restaurant ambience murmur`, `cafe ambience dishes`, `bar crowd muffled music`, `pizzeria ambience` |
| `Emitter_Stadium` / `LoopClip` | Multitud de estadio en loop (cantos, aliento) | 30–60 s | sí (sin corte) | mono | `stadium crowd loop`, `football crowd chant distant`, `soccer stadium ambience`; para aclamaciones puntuales: `stadium crowd cheer goal` |
| `Emitter_GasStation` / `OneShots` | Surtidor de nafta: manguera, clic de la pistola, bomba | 1–3 s cada uno | no | mono | `gas pump nozzle click`, `fuel pump`, `gas station ambience`, `petrol pump handle` |
| `Emitter_HouseDog` / `OneShots` | Ladridos de perro (varios, de distintos tamaños; el código dispara ráfagas de 1–3) | 0.5–2 s cada uno | no | mono | `dog bark single`, `dog barking distant`, `small dog bark`, `large dog bark` |
| `Zone_Town` / `ScatterClips` | Pájaros del pueblo, gallo, campo a lo lejos | 2–6 s cada uno | no | mono | `rooster crow`, `countryside birds`, `village ambience birds`, `crow caw distant`, `farm ambience` |
| `NPCPedestrianAudio_Default` / `Voices` | Voces ocasionales de peatón: tos, charla corta, murmullo | 1–3 s cada uno | no | mono | `cough male`, `short chat murmur`, `person humming`, `hello greeting male` |
| `NPCPedestrianAudio_Default` / `Screams` | Grito o "¡eh!" ante una frenada de emergencia cercana | 0.5–1.5 s cada uno | no | mono | `scream surprised male`, `hey shout`, `yell watch out`, `startled gasp` |

## Sugeridos sin campo asignado todavía

Estos sonidos están pedidos por el diseño de la US20 pero hoy no tienen un campo propio en los perfiles. Se pueden
incluir mezclados dentro de los loops de arriba o agregarse con una tarea futura:

| Sonido | Dónde encajaría | Duración | Loop | Canales | Búsqueda en inglés |
|---|---|---|---|---|---|
| Herramientas de taller (llave de impacto, martillo, compresor) | `Emitter_Workshop` hoy usa placeholders metálicos; sumar estos clips a `OneShots` | 0.5–3 s | no | mono | `workshop tools impact wrench`, `garage hammer metal`, `air compressor short` |
| Portazo (puerta de auto o de casa) | one-shots de `Emitter_FoodAndDrink` o de las casas | 0.5–1 s | no | mono | `door slam`, `car door close`, `house door slam` |
| Vajilla y cubiertos (aparte del murmullo) | `Emitter_FoodAndDrink` si se cambia a modo `RandomOneShots` | 0.5–2 s | no | mono | `dishes clatter`, `cutlery plate`, `cup on saucer` |
| Aclamaciones puntuales del estadio | aparte del loop de `Emitter_Stadium` | 3–8 s | no | mono | `stadium crowd cheer`, `crowd roar goal`, `crowd applause large` |

## Estado actual

Al crear los perfiles quedan **9 grupos de clips orgánicos pendientes** (las 9 filas de la primera tabla).
Cuando completes un campo, la verificación deja de listarlo.
