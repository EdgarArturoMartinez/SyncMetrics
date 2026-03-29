# Ejercicio Técnico Staff Engineer — Integraciones de Datos

## Escenario

SyncMetrics Inc. agrega datos meteorológicos de múltiples APIs de terceros, los normaliza y escribe archivos de salida unificados para importación en sistemas de analítica downstream. Construye un pipeline de ingestión extensible en C# que obtenga, transforme y genere datos meteorológicos normalizados.

---

## Requisitos

### Pipeline — API Open-Meteo

Obtener datos de pronóstico a 7 días desde [Open-Meteo](https://open-meteo.com/en/docs) (gratuita, no requiere clave API) para un conjunto configurable de ubicaciones. Como mínimo:

| Ubicación | Latitud | Longitud |
|-----------|---------|----------|
| Nueva York | 40.7128 | -74.0060 |
| Londres | 51.5074 | -0.1278 |
| Tokio | 35.6762 | 139.6503 |

**Endpoint:**
```
GET https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}
    &daily=temperature_2m_max,temperature_2m_min,precipitation_sum,windspeed_10m_max
    &timezone=auto&forecast_days=7
```

**Campos a ingestar** (todos diarios):

| Campo API | Unidad | Descripción |
|-----------|--------|-------------|
| `temperature_2m_max` | °C | Máxima diaria |
| `temperature_2m_min` | °C | Mínima diaria |
| `precipitation_sum` | mm | Precipitación total |
| `wind_speed_10m_max` | km/h | Velocidad máxima del viento |
| `uv_index_max` | índice | Índice UV máximo |

**Entregables:**
- Las ubicaciones deben obtenerse **concurrentemente** (en paralelo)
- Salida normalizada delimitada por tabuladores — **diseña tú el esquema**
- Resumen de procesamiento al finalizar
- Ejecutable con `dotnet run` y pruebas pasando con `dotnet test`

### Arquitectura

Diseña el pipeline de modo que una **segunda fuente de API con una estructura JSON completamente diferente** pueda agregarse con cambios mínimos. HTTP, parseo, transformación y salida son **concerns separados**. Las llamadas HTTP deben estar **detrás de una interfaz**.

### Manejo de Errores

Manejar: fallos HTTP, JSON malformado, campos faltantes y valores no parseables. **Sin fallos silenciosos** — todo error debe reportarse.

### Pruebas (Testing)

Cubrir:
- Parseo de respuestas (válidas **y** malformadas)
- Lógica de transformación
- Formato de salida
- Pipeline end-to-end con respuestas HTTP **mockeadas**

### Bonus

- Lógica de **reintentos** (retry) para fallos HTTP transitorios
- **Mapeo de campos dirigido por configuración** (config-driven field mapping)

---

## Qué Se Evalúa

| Área | Qué buscan |
|------|------------|
| **Arquitectura** | Descomposición del pipeline, fronteras de interfaces, extensibilidad |
| **Patrones de integración** | Abstracción HTTP, parseo no trivial de JSON, normalización de esquema |
| **Manejo de errores** | Resiliencia, reporte de errores estructurado |
| **Testeabilidad** | HTTP mockeado, cobertura significativa de casos borde |
| **Uso de IA** | Dónde se usó IA vs. dónde el juicio humano la corrigió — documentar esto |
| **Juicio de Staff Engineer** | Qué se generalizó vs. qué se mantuvo simple, trade-offs articulados |

---

## Uso de IA

Este ejercicio **espera que la IA y los flujos de trabajo agénticos sean una parte central** de cómo lo construyes — no un suplemento. Usa Copilot, Cursor, Claude, ChatGPT, o cualquier herramienta agéntica que usarías en trabajo de producción.

Incluir un archivo `AI.md` (10–15 líneas) que cubra:
- **Cómo se usó la IA** a lo largo del pipeline — qué impulsó, qué generó como scaffolding
- **Un lugar donde corregiste o anulaste** lo que generó la IA, y por qué
- **Alguna parte de la solución donde deliberadamente elegiste NO usar IA**, y el razonamiento

> **IMPORTANTE**: Envíos que no muestren uso significativo de IA **no avanzarán**. Evalúan tu capacidad de trabajar **efectivamente con** estas herramientas, no evitándolas.

---

## Entrega

Devolver un **zip o enlace a repositorio**. Incluir un `README.md` breve — configuración, cómo ejecutar, cualquier supuesto o trade-off que valga la pena mencionar. No sobre-documentar; escribe lo que querrías que un nuevo compañero de equipo supiera.

---

## Notas Críticas Descubiertas por Arthur (del Checkpoint)

### Discrepancia #1 — Nombre del campo de viento
El endpoint del ejercicio usa `windspeed_10m_max` (sin guion bajo entre "wind" y "speed"), pero la documentación real de la API Open-Meteo usa **`wind_speed_10m_max`** (con guion bajo). La tabla de "Fields to ingest" del propio ejercicio también usa `wind_speed_10m_max`. **Usar el nombre correcto de la API: `wind_speed_10m_max`.**

### Discrepancia #2 — Campo faltante en la URL
El ejercicio lista `uv_index_max` en la tabla de campos pero **NO lo incluye en la URL de ejemplo**. Nuestra implementación **debe agregarlo** al query string.

### URL Corregida
```
GET https://api.open-meteo.com/v1/forecast
    ?latitude={lat}&longitude={lon}
    &daily=temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max,uv_index_max
    &timezone=auto
    &forecast_days=7
```
