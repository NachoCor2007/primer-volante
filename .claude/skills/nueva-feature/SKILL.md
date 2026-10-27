---
name: nueva-feature
description: Flujo de Primer Volante para investigar el código, acordar un diseño con el usuario y redactar el prompt de implementación para el subagente implementador-unity. Usar cuando el usuario describe un comportamiento, feature o user story nueva que quiere implementar en el proyecto Unity.
---

# Diseñar una feature de Primer Volante

## Contexto que dio el usuario

$ARGUMENTS

Si el contexto está vacío o es muy corto, pedile al usuario que describa qué quiere implementar antes de seguir.

## Regla de oro: esta fase es solo de lectura

Durante todo este flujo **no modifiques código, escenas, prefabs ni assets del proyecto**. Todo el esfuerzo va a investigar y diseñar. Lo único que podés escribir es:
- archivos de memoria (para guardar el diseño acordado),
- archivos temporales en el scratchpad,
- comandos de solo lectura (git log, grep, consultas a Unity que no cambien nada, etc.).

## Herramienta principal: Unity CLI

La tool más importante de este flujo es el **Unity CLI** (cargá la skill `unity:unity-cli` al empezar). Con el Editor conectado, permite investigar el proyecto real y no solo los archivos: jerarquía de escenas, componentes y sus valores serializados, referencias entre objetos, prefabs, assets, logs de consola y ejecución de C# en el Editor vivo (`eval`).

- Usala como fuente principal para entender escenas y configuración, antes que leer YAML de `.unity`/`.prefab` a mano.
- En esta fase, usala **solo para consultar**: nada de crear, modificar ni guardar objetos, escenas o assets.
- Si el Editor no está conectado, avisale al usuario y pedile que lo abra antes de seguir investigando escenas.

## Pasos

### 1. Investigar a fondo
- Leé el código relacionado de verdad (no te quedes con nombres de archivo): sistemas existentes que se tocan, patrones que ya usa el proyecto.
- Inspeccioná con el Unity CLI las escenas y prefabs involucrados: qué objetos existen, qué componentes tienen, cómo están configurados y conectados.
- Revisá las memorias del proyecto: puede haber diseños previos relacionados o decisiones que condicionan esta feature.
- Si hay un issue de GitHub mencionado, leelo.
- Usá también las demás tools y skills que correspondan (skills de Unity, GitHub MCP, docs de Unity XR, etc.). Anotá cuáles te resultaron útiles y para qué, porque van a ir en el prompt.

### 2. Preguntar lo que no esté claro
El contexto del usuario puede ser vago o dejar decisiones abiertas. **No asumas**: preguntá todo lo que necesite su opinión (alcance, comportamiento esperado, valores, UX, casos borde, qué queda afuera). Usá `AskUserQuestion` cuando haya opciones concretas para elegir, con tu recomendación primero. Es preferible preguntar de más a implementar algo que no va en la solución.

### 3. Proponer el diseño e iterar
Presentá una propuesta concreta: componentes nuevos o modificados, cómo se integran con lo existente, datos/ScriptableObjects, cambios en escena, casos borde. Iterá con el usuario hasta que lo apruebe.

Cuando esté acordado, guardalo en memoria (`project_<nombre>.md`, mismo estilo que las demás memorias de diseño), indicando que el prompt está pendiente o escrito.

### 4. Redactar el prompt de implementación
El subagente **no ve esta conversación**: el prompt tiene que ser autocontenido. Las reglas generales (APIs obsoletas, autoría de commits, formato del reporte) ya están en el agente `implementador-unity`; no las repitas. Estructura:

1. **Objetivo**: qué se implementa y por qué, en 2-4 líneas.
2. **Contexto del código**: archivos, clases y sistemas existentes relevantes (con rutas), y patrones a seguir.
3. **Diseño acordado**: detalle de cada pieza. Incluí las decisiones que tomó el usuario, para que el subagente no las vuelva a discutir.
4. **Escena nueva**: los cambios van en una escena nueva. Indicá la escena base a duplicar, el nombre y la ruta de la nueva (ej. `Assets/Scenes/car09/CityWith<Feature>.unity`). Preguntá al usuario si no está claro cuál es la base.
5. **Tools y skills a usar**: lista concreta de qué usar y para qué. El Unity CLI va siempre primero, con las operaciones concretas que va a necesitar (duplicar la escena base, crear GameObjects y componentes, asignar referencias, verificar compilación, probar en Play Mode). Después, el resto (ej. "las indicaciones de Play Mode driving tests para probar manejando con un Gamepad virtual"). El objetivo es que el subagente no tenga que pensar qué herramientas usar.
6. **Tests**: qué tiene que verificar la clase `<Feature>Verification` (casos concretos), qué escenarios de Play Mode hay que correr y qué se espera ver en la consola, y qué verificaciones existentes correr para descartar regresiones.
7. **Criterios de aceptación** y **fuera de alcance**.
8. **Git**: rama en la que trabajar, y si tiene que commitear o no (preguntale al usuario si no lo dijo).

Mostrale el prompt completo al usuario y **esperá su aprobación explícita**. Si pide cambios, ajustalo y volvé a mostrarlo.

### 5. Enviar al subagente
Con el prompt aprobado, lanzá el agente `implementador-unity` (Agent tool, `subagent_type: "implementador-unity"`) con el prompt tal cual fue aprobado.

Cuando termine:
- Resumile al usuario lo que reportó: qué se hizo, el resultado de los tests, qué quedó pendiente y los problemas encontrados.
- Si reporta algo que contradice el diseño o que no pudo resolver, planteáselo al usuario antes de seguir.

### 6. Code review
La revisión la hacés vos, no el subagente, para que el código lo revise alguien distinto de quien lo escribió:
1. Corré la skill `code-review` con nivel `high` sobre los cambios del subagente.
2. Mostrale los hallazgos al usuario y acordá cuáles corregir.
3. Mandale las correcciones al mismo subagente con `SendMessage`, así conserva su contexto. Pedile que vuelva a correr los tests afectados.
4. Repetí la revisión si las correcciones fueron grandes.

Al final, actualizá la memoria del diseño (ej. "implementado AAAA-MM-DD en la rama X, revisado").
