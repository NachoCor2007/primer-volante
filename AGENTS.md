# Reglas del Proyecto "Primer Volante"

## Creación de Tickets (GitHub Project)
Cada vez que se te pida crear un ticket, tarea o issue, **SIEMPRE asígnalo por defecto al proyecto "Primer Volante"**.

* No es necesario que preguntes a qué proyecto o workspace pertenece si el usuario no lo especifica en su mensaje.
* Asume siempre "Primer Volante" a menos que el usuario indique explícitamente un proyecto distinto.

## APIs de Unity obsoletas
No uses APIs de Unity marcadas como obsoletas (warnings `CS0618` u otros). Antes de escribir código que llame a una API de Unity, verifica que no esté deprecada.

* **`Object.FindFirstObjectByType<T>()`** está obsoleta porque depende del orden de instance ID. Usa **`Object.FindAnyObjectByType<T>()`** en su lugar (o `FindObjectsByType` si se necesitan todas las instancias), salvo que el usuario pida explícitamente preservar el orden determinista de `FindFirstObjectByType`.
