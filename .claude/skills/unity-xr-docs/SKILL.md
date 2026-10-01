---
name: unity-xr-docs
description: Documentación oficial de los paquetes XR de Unity instalados en el proyecto — XR Interaction Toolkit 3.6, OpenXR 1.18, XR Hands 1.9, XR Core Utils 2.6 (XR Origin), XR Plug-in Management 4.7 y Composition Layers 2.5. Usar antes de escribir o depurar código o configuración de VR/XR: interactors e interactables (grab, poke, simple, socket, near-far, ray, direct), eventos de interacción (hover/select/activate), interaction layers y filtros, attach transforms y grab transformers, locomoción (teleport, move, turn, climb), UI en world space, input actions y controladores, XR Device Simulator / Interaction Simulator, XR Origin y tracking, hand tracking y gestos, features y perfiles de interacción de OpenXR, loaders de XR Management y composition layers.
---

# Documentación de Unity XR

Copia de la documentación oficial (`Documentation~`) de cada paquete XR, tomada de `primerVolante/Library/PackageCache` y **en las versiones exactas instaladas en el proyecto**. Tiene prioridad sobre lo que recuerdes de versiones anteriores: XRI cambió mucho entre 2.x y 3.x (namespaces, interactors, input).

## Cómo usarla

1. Buscar el tema en la tabla de abajo y leer el archivo indicado.
2. Si no aparece, buscar en los archivos con Grep dentro de `references/` (p. ej. el nombre de un componente o propiedad).
3. Cada paquete tiene un `TableOfContents.md` con el índice completo.

Las imágenes no se copiaron; los enlaces `images/...` y los `[!include]` a `snippets/` pueden quedar rotos, pero el texto está completo.

## Paquetes

| Carpeta | Paquete | Versión |
|---|---|---|
| `references/xri/` | XR Interaction Toolkit (`com.unity.xr.interaction.toolkit`) | 3.6.0 |
| `references/openxr/` | OpenXR Plugin (`com.unity.xr.openxr`) | 1.18.0 |
| `references/hands/` | XR Hands (`com.unity.xr.hands`) | 1.9.0 |
| `references/core-utils/` | XR Core Utilities (`com.unity.xr.core-utils`) | 2.6.0 |
| `references/management/` | XR Plug-in Management (`com.unity.xr.management`) | 4.7.0 |
| `references/composition-layers/` | XR Composition Layers (`com.unity.xr.compositionlayers`) | 2.5.0 |

Se omitieron las páginas de interacción AR de XRI (`ar-*`) y el sample de visionOS.

## Índice por tema (XRI)

| Tema | Archivos en `references/xri/` |
|---|---|
| Arquitectura y ciclo de actualización | `architecture.md`, `interaction-update-loop.md`, `xr-interaction-manager.md` |
| Glosario y componentes clave | `glossary.md`, `interaction-key-components.md`, `components.md` |
| Eventos (hover, select, activate) | `interaction-events-landing.md`, `interactable-events.md`, `interactor-events.md`, `interaction-state-events.md`, `interaction-event-usage-examples.md` |
| Agarrar objetos | `xr-grab-interactable.md`, `interaction-attach-controller.md`, `xr-body-transformer.md`, `object-interaction.md` |
| Poke (botones con el dedo) | `xr-poke-interactor.md`, `xr-poke-filter.md`, `poke-interactables-guide-3d-objects.md`, `poke-interactions-with-ugui-objects.md`, `xr-hand-skeleton-poke-displacer.md` |
| Interactables simples y sockets | `xr-simple-interactable.md`, `xr-socket-interactor.md`, `xr-interactable-snap-volume.md` |
| Interactors | `near-far-interactor.md`, `xr-direct-interactor.md`, `xr-ray-interactor.md`, `xr-gaze-interactor.md`, `interactor-components.md`, `xr-interaction-group.md` |
| Filtros y capas | `interaction-layers.md`, `interaction-filters.md`, `target-filters.md`, `filter-components.md` |
| Input (Input System, controles, manos) | `input.md`, `configure-input-system.md`, `input-readers.md`, `input-action-manager.md`, `input-components.md`, `xr-input-modality-manager.md` |
| Locomoción | `locomotion-landing.md`, `locomotion.md`, `locomotion-mediator.md`, `locomotion-providers.md`, `continuous-move-provider.md`, `continuous-turn-provider.md`, `snap-turn-provider.md`, `teleportation*.md`, `climbing.md`, `grab-move-provider.md`, `gravity-provider.md`, `tunneling-vignette-controller.md` |
| UI en world space | `ui-interaction.md`, `ui-setup.md`, `xr-ui-input-module.md`, `tracked-device-graphic-raycaster.md`, `canvas-optimizer.md`, `ui-world-space-ui-toolkit-support.md`, `lazy-follow.md` |
| Feedback (hápticos, audio, affordances) | `haptic-impulse-player.md`, `simple-haptic-feedback.md`, `simple-audio-feedback.md`, `affordance-system.md`, `feedback-components.md` |
| Probar sin visor | `xr-interaction-simulator-overview.md`, `xr-interaction-simulator.md`, `xr-device-simulator-overview.md`, `xr-device-simulator.md`, `debugger-window.md` |
| Samples | `samples.md`, `samples-starter-assets.md`, `samples-hands-interaction-demo.md`, `samples-world-space-ui.md` |
| Extender XRI (interactables/interactors propios) | `extending-xri.md` |
| Migración y novedades | `upgrade-guide-3.0.md`, `whats-new-3.6.md` (y anteriores) |
| Configuración del proyecto | `general-setup.md`, `installation.md`, `xri-settings.md`, `create-basic-scene.md` |

## Índice por tema (otros paquetes)

| Tema | Archivos |
|---|---|
| XR Origin, cámara y tracking origin | `core-utils/xr-origin.md`, `core-utils/xr-origin-setup.md`, `core-utils/xr-origin-reference.md` |
| Project Validation | `core-utils/project-validation.md`, `openxr/project-validation.md` |
| Configurar OpenXR, features y perfiles de interacción | `openxr/project-configuration.md`, `openxr/features.md`, `openxr/features/`, `openxr/settings-reference.md` |
| Input de OpenXR | `openxr/input.md` |
| Loaders e inicialización de XR | `management/EndUser.md`, `management/index.md` |
| Hand tracking, joints y gestos | `hands/index.md`, `hands/hand-data/`, `hands/gestures/`, `hands/process-joints.md`, `hands/project-setup/` |
| Composition layers (UI/video nítidos en VR) | `composition-layers/overview.md`, `composition-layers/usage-guide.md`, `composition-layers/composition-layer-interactive-UI.md`, `composition-layers/component-reference.md` |

## Actualizar la documentación

Si se actualiza algún paquete XR, esta copia queda desactualizada. Para regenerarla, volver a copiar los `.md` desde `primerVolante/Library/PackageCache/<paquete>@*/Documentation~/` y actualizar la tabla de versiones.
