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
| `Zone_Town` / `ScatterClips` | Pájaros del pueblo, gallo, campo a lo lejos | 2–6 s cada uno | no | mono | `rooster crow`, `countryside birds`, `village ambience birds`, `crow caw distant`, `farm ambience` |
| `NPCPedestrianAudio_Default` / `Voices` | Voces ocasionales de peatón: tos, charla corta, murmullo | 1–3 s cada uno | no | mono | `cough male`, `short chat murmur`, `person humming`, `hello greeting male` |

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

**Completados** (clips en `Organic/`, créditos en `Assets/Audio/CREDITS.md`):
`Zone_Plaza` (`ScatterClips` y además el lecho `BedLoop`, que reemplazó al placeholder de aire), `Emitter_PlazaLife`,
`Emitter_FoodAndDrink`, `Emitter_GasStation`, `Emitter_HouseDog`, `Emitter_Stadium` y `NPCPedestrianAudio_Default` / `Screams`.

`Zone_Town` / `BedLoop` usa `Organic/town_wind_soft_loop.wav`, un viento grave sintetizado que reemplazó al placeholder
`bed_town_wind_loop` (sonaba a ruido blanco). Si se consigue un ambiente de campo real, se puede reemplazar.

**Pendientes**: las 2 filas de la tabla de arriba (pájaros del pueblo, voces de peatón).

**Pasos de peatón**: `NPCPedestrianAudio_Default` / `Footsteps` usa `Organic/footstep_organic_01..04` (cortados de
`single_footstep_02.mp3`). Los placeholders `footstep_01..04` quedan sin uso.
Cuando completes un campo, la verificación deja de listarlo.
