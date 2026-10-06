# Modelo de Métricas Multi-Proyecto en Microsoft Dataverse
**Patrón Arquitectónico:** Snapshot / Time-Series (Log de Métricas)  
**Tabla Sugerida:** `Project KPI Snapshot` (`crxxx_kpisnapshot`)

---

## 📌 Contexto y Enfoque de Diseño

Para que múltiples proyectos independientes puedan reportar métricas en una única tabla de Microsoft Dataverse sin colisiones ni dependencias cruzadas:

> [!WARNING]
> **Error común:** Crear columnas fijas por métrica (ej. una columna `Coverage`, otra `Errores500`, etc.). Esto obliga a modificar el esquema de la tabla cada vez que un equipo introduce una nueva medición.

> [!TIP]
> **Patrón recomendado:** **Modelo de Log de Métricas (Snapshot / Time-Series)**.  
> Cada fila en Dataverse representa la **medición individual de un KPI para un proyecto en un período determinado**.

---

## 🏛️ Esquema de Columnas (Dataverse)

| Nombre de Columna (Display Name) | Tipo de Dato en Dataverse | Requerido | Descripción y Ejemplos |
| :--- | :--- | :---: | :--- |
| **Record Title** *(Primary Name)* | Single Line of Text | **Sí** | Identificador legible del registro.<br>• *Formato sugerido:* `[Proyecto] - [KPI] - [Período]`<br>• *Ej:* `"Grade-M8 - PR Cycle Time - Sprint 3"` |
| **Project Identifier** | Single Line of Text *(o Choice)* | **Sí** | Identificador o código único del proyecto para filtros en dashboards.<br>• *Ej:* `"grade-m8"`, `"crm-core"`, `"app-mobile"` |
| **Component / Repo** | Single Line of Text | No | Sub-módulo o repositorio específico si aplica.<br>• *Ej:* `"backend"`, `"frontend"`, `"all"` |
| **Period Type** | Choice *(OptionSet)* | **Sí** | Tipo de ciclo temporal:<br>• `Sprint`<br>• `Semanal`<br>• `Mensual`<br>• `Q / Trimestral` |
| **Period Identifier** | Single Line of Text | **Sí** | Identificador del ciclo.<br>• *Ej:* `"Sprint 3"`, `"2026-W39"`, `"Septiembre 2026"` |
| **Period Start Date** | Date Only | **Sí** | Fecha de inicio de la ventana de medición.<br>• *Ej:* `2026-09-15` |
| **Period End Date** | Date Only | **Sí** | Fecha de fin de la ventana de medición.<br>• *Ej:* `2026-09-30` |
| **KPI Name** | Single Line of Text *(o Choice)* | **Sí** | Nombre estandarizado de la métrica.<br>• *Ej:* `"PR Cycle Time"`, `"500 Error Rate"`, `"Test Coverage"`, `"Tickets Done"` |
| **KPI Category** | Choice *(OptionSet)* | **Sí** | Categoría para agregación en Power BI:<br>• `Calidad / QA`<br>• `DevOps / CI-CD`<br>• `Telemétrico / Salud`<br>• `Gestión / Agilidad`<br>• `Negocio / Producto` |
| **Metric Value** | Decimal Number *(2 a 4 dec)* | **Sí** | Valor numérico final medido.<br>• *Ej:* `85.50` (si es %), `1.20` (promedio/ratio), `14` (conteo) |
| **Unit of Measure** | Choice *(OptionSet)* | **Sí** | Unidad del valor:<br>• `Porcentaje (%)`<br>• `Horas`<br>• `Segundos / ms`<br>• `Cantidad (Conteo)`<br>• `Ratio / Promedio` |
| **Numerator / Sample** | Decimal Number | No | *(Opcional pero recomendado)* Parte superior de la fracción.<br>• *Ej:* `8` errores 500, `12` comentarios |
| **Denominator / Total** | Decimal Number | No | *(Opcional pero recomendado)* Universo total evaluado.<br>• *Ej:* `1548` requests totales, `10` PRs evaluados |
| **Target Value** | Decimal Number | No | Meta u objetivo esperado.<br>• *Ej:* `80.00` (% test), `1.00` (comentarios/PR) |
| **Health Status** *(Semáforo)* | Choice *(OptionSet)* | No | Estado cualitativo respecto a la meta:<br>• 🟢 `En Objetivo (Verde)`<br>• 🟡 `En Observación (Amarillo)`<br>• 🔴 `Crítico / Fuera de Meta (Rojo)`<br>• ⚪ `Informativo` |
| **Source System** | Choice *(OptionSet)* | **Sí** | Herramienta de origen:<br>• `GitHub Actions`<br>• `GitHub PRs`<br>• `Firebase / GCP`<br>• `Jira`<br>• `Manual` |
| **Notes / Context** | Multiple Lines of Text | No | Justificación de anomalías o contexto técnico.<br>• *Ej:* `"Hubo caída de conectividad en el host del modelo el día 22"` |

---

## 📊 Ejemplo Práctico: Registros Publicados por `Grade-M8`

A continuación se ilustra cómo viajarían los datos del proyecto **Grade-M8** para el cierre del **Sprint 3**:

| Record Title | Project | Period | KPI Name | Category | Metric Value | Unit | Numerator / Denominator | Status |
| :--- | :--- | :--- | :--- | :--- | :---: | :--- | :---: | :---: |
| `GradeM8-Sprint3-DoneTickets` | `grade-m8` | `Sprint 3` | **Tickets Done** | Gestión | **12** | Cantidad | — | 🟢 Verde |
| `GradeM8-Sprint3-PRComments` | `grade-m8` | `Sprint 3` | **PR Review Comments** | Calidad | **1.2** | Ratio | `12 / 10` | 🟢 Verde |
| `GradeM8-Sprint3-Error500` | `grade-m8` | `Sprint 3` | **500 Error Rate** | Telemétrico | **0.51** | Porcentaje (%) | `8 / 1548` | 🟢 Verde |
| `GradeM8-Sprint3-Coverage` | `grade-m8` | `Sprint 3` | **Test Coverage** | Calidad | **78.4** | Porcentaje (%) | — | 🟡 Amarillo |
| `GradeM8-Sprint3-CycleTime` | `grade-m8` | `Sprint 3` | **PR Cycle Time** | DevOps | **18.5** | Horas | — | 🟢 Verde |
| `GradeM8-Sprint3-CIFailures` | `grade-m8` | `Sprint 3` | **CI Failures** | DevOps | **3** | Cantidad | `3 / 42` | 🟢 Verde |

---

## 🚀 Ventajas del Modelo

### 1. Desacoplamiento Total entre Equipos
Cada equipo es autónomo en su stack (ej. Python, AWS, Jira, GitHub Actions). La integración no depende de herramientas compartidas, sino exclusivamente de un **contrato de datos unificado**. Nuevas métricas no requieren cambios de esquema DDL en Dataverse.

### 2. Evita el "Promedio de Promedios" en Power BI
> [!IMPORTANT]
> Al capturar `Numerator` y `Denominator`:
> $$\text{Tasa Global} = \frac{\sum \text{Numerator}}{\sum \text{Denominator}}$$
> La PMO o gerencia puede calcular ratios consolidados reales sumando los numeradores y denominadores de todos los proyectos, en lugar de promediar porcentajes locales (lo cual genera distorsiones estadísticas).

### 3. Automatización Sencilla
El modelo es directamente integrable mediante:
* **Dataverse Web API (REST / OData):** Inserción automatizada desde pipelines de CI/CD (ej. GitHub Actions):
  ```http
  POST /api/data/v9.2/crxxx_kpisnapshots
  Content-Type: application/json

  {
    "crxxx_recordtitle": "GradeM8-Sprint3-Coverage",
    "crxxx_projectidentifier": "grade-m8",
    "crxxx_periodtype": 1,
    "crxxx_periodidentifier": "Sprint 3",
    "crxxx_kpiname": "Test Coverage",
    "crxxx_metricvalue": 78.4,
    "crxxx_unitofmeasure": 1
  }
  ```
* **Power Automate:** Flujos programados al cierre de cada sprint o ciclo de entrega.
