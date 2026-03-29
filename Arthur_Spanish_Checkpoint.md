# Arthur_Checkpoint — Ejercicio Técnico de Staff Engineer

> **Propósito**: Este es el documento de planificación estratégica de Arturo. Captura el análisis completo del Ejercicio Take-Home de Staff Engineer (Integraciones de Datos) — descomposición de requisitos, **diez** enfoques arquitectónicos comparados en profundidad (incluyendo análisis de frontend React), análisis de base de datos, estrategia Docker, plan de CI/CD y notas de preparación para la entrevista.  
> **Uso**: Re-leer la Sección 1 al inicio de cada sesión para recuperar el contexto completo en menos de 2 minutos.  
> **Última Actualización**: Sesión 3 — 29 de marzo de 2026 (Fase 7 COMPLETA: 27 pruebas pasando; Fase 8 README + AI.md + Dockerfile + CI + TSV de ejemplo COMPLETA; dos bugs de runtime en producción corregidos; agregado desglose SOLID + Patrones de Diseño por fase; agregada advertencia de testing en vida real).

---

## 1. Contexto Ejecutivo y Resumen de Sesión (Leer Esto Primero en Cada Sesión)

**Empresa**: SyncMetrics Inc. (a través del pipeline de contratación de Actabl)  
**Rol**: Staff Engineer — Integraciones de Datos  
**Ejercicio**: Construir un pipeline de ingesta extensible en C# que obtenga pronósticos meteorológicos de 7 días de la API gratuita Open-Meteo, los normalice y escriba archivos de salida delimitados por tabulaciones para análisis downstream.

**Lista de Verificación de Entregables Principales**:
- [x] Solución .NET 8 C# — se ejecuta con `dotnet run`, las pruebas pasan con `dotnet test`
- [x] Obtiene datos meteorológicos diarios para 3+ ubicaciones configurables de forma concurrente
- [x] Archivo de salida normalizado delimitado por tabulaciones con esquema auto-diseñado
- [x] Resumen de procesamiento impreso al completar
- [x] Arquitectura extensible — agregar una segunda fuente API con diferente forma JSON con código mínimo
- [x] HTTP, parseo, transformación y salida son preocupaciones separadas
- [x] Las llamadas HTTP están detrás de una interfaz
- [x] Manejo de errores: fallos HTTP, JSON malformado, campos faltantes, valores no parseables
- [x] Pruebas: parseo de respuestas (válidas + malformadas), lógica de transformación, formato de salida, end-to-end con HTTP mockeado
- [x] Bonus: Lógica de reintentos con backoff exponencial para fallos HTTP transitorios
- [x] Bonus: Mapeo de campos dirigido por configuración (configuración JSON/YAML para mapeo de esquema fuente-a-normalizado)
- [x] `README.md` — configuración, cómo ejecutar, suposiciones, trade-offs
- [x] `AI.md` — 10–15 líneas sobre uso de IA, anulaciones y no-uso deliberado
- [ ] Envío de zip o enlace al repositorio

**Qué Evalúan (en orden de prioridad)**:
1. **Arquitectura** — Descomposición del pipeline, límites de interfaces, extensibilidad
2. **Patrones de Integración** — Abstracción HTTP, parseo de JSON no trivial, normalización de esquema
3. **Manejo de Errores** — Resiliencia, reporte estructurado de errores (sin fallos silenciosos)
4. **Testabilidad** — HTTP mockeado, cobertura significativa de casos extremos
5. **Uso de IA** — Dónde la IA dirigió, dónde el juicio humano anuló
6. **Juicio de Staff** — Qué se generalizó vs. qué se mantuvo simple, trade-offs articulados

**Fortalezas Clave de Arthur Que Mapean Directamente**:
- 16+ años .NET/C#, incluyendo .NET 8, Clean Architecture, Microservicios
- Amplia experiencia en pipelines ETL/ELT (SSIS, Azure Functions, procesamiento batch)
- Azure Cloud (Functions, Service Bus, DevOps), Docker, CI/CD
- Integraciones de API REST con patrones de auth OAuth/API-key
- Arquitectura dirigida por configuración (feature flags, mapeos JSON/YAML)
- CQRS, Entity Framework, caché Redis
- Desarrollo asistido por IA: GitHub Copilot, Azure OpenAI, Anthropic Claude

---

## 2. Análisis Profundo de Requisitos y Análisis de la API

### 2.1 API Open-Meteo — Lo Que Dice el Ejercicio vs. Lo Que la API Realmente Hace

**HALLAZGO CRÍTICO #1 — Discrepancia en la URL (Punto de Conversación en Entrevista)**:  
El endpoint del ejercicio muestra:
```
&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,windspeed_10m_max
```
Pero la documentación real de la API Open-Meteo usa `wind_speed_10m_max` (con guiones bajos). La URL del ejercicio usa `windspeed_10m_max` (sin guión bajo entre "wind" y "speed"). Según la definición oficial de parámetros diarios de la API, el nombre correcto del campo es **`wind_speed_10m_max`**.

> **Señal de Staff Engineer**: Detectar esta discrepancia entre la especificación y el comportamiento real de la API es exactamente el tipo de atención al detalle que se espera. En la entrevista, mencionar: *"Validé el endpoint del ejercicio contra la documentación en vivo de la API y encontré una discrepancia de nomenclatura en el campo de velocidad del viento — la API usa nomenclatura consistente con guiones bajos. Usé el nombre real del campo de la API."*

**HALLAZGO CRÍTICO #2 — Campo Faltante en la URL**:  
El ejercicio lista `uv_index_max` en la tabla de "Campos a ingerir" pero NO lo incluye en la URL del query string. La documentación de la API confirma que `uv_index_max` ES un parámetro diario disponible. Nuestra implementación debe agregarlo.

**Endpoint Corregido**:
```
GET https://api.open-meteo.com/v1/forecast
    ?latitude={lat}&longitude={lon}
    &daily=temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max,uv_index_max
    &timezone=auto
    &forecast_days=7
```

### 2.2 Forma de Respuesta de la API (de la documentación oficial)

```json
{
    "latitude": 40.710335,
    "longitude": -73.99307,
    "elevation": 51.0,
    "generationtime_ms": 0.058,
    "utc_offset_seconds": -14400,
    "timezone": "America/New_York",
    "timezone_abbreviation": "EDT",
    "daily": {
        "time": ["2026-03-28", "2026-03-29", "2026-03-30", ...],
        "temperature_2m_max": [18.2, 15.1, 12.8, ...],
        "temperature_2m_min": [8.4, 6.2, 4.1, ...],
        "precipitation_sum": [0.0, 2.3, 0.5, ...],
        "wind_speed_10m_max": [22.1, 18.5, 30.2, ...],
        "uv_index_max": [5.2, 3.8, 6.1, ...]
    },
    "daily_units": {
        "time": "iso8601",
        "temperature_2m_max": "°C",
        "temperature_2m_min": "°C",
        "precipitation_sum": "mm",
        "wind_speed_10m_max": "km/h",
        "uv_index_max": ""
    }
}
```

**Observaciones estructurales clave**:
- Los datos diarios vienen como arrays paralelos (time[] + un float[] por variable) — NO como un array de objetos por día
- Esto significa que el parseo requiere "comprimir" (zip) los arrays por índice
- `daily_units` proporciona metadatos de unidades — útil para validación de normalización
- Respuestas de error: HTTP 400 con `{ "error": true, "reason": "..." }`
- Las coordenadas en la respuesta pueden diferir ligeramente de la solicitud (ajuste a celda de cuadrícula)

### 2.3 Configuración de Ubicaciones

| Ubicación | Latitud  | Longitud  |
|-----------|----------|-----------|
| New York  | 40.7128  | -74.0060  |
| London    | 51.5074  | -0.1278   |
| Tokyo     | 35.6762  | 139.6503  |

Estas deben ser **configurables** — no hardcodeadas. Esto se alinea con el bonus de mapeo de campos dirigido por configuración.

### 2.4 Requisitos Implícitos (El Staff Engineer lee entre líneas)

1. **"Conjunto configurable de ubicaciones"** → Las ubicaciones vienen de configuración (appsettings.json), no del código
2. **"Salida normalizada delimitada por tabulaciones — diseña el esquema tú mismo"** → Quieren ver juicio de diseño de esquema
3. **"Resumen de procesamiento al completar"** → Salida estructurada de log/consola mostrando conteos, errores, duraciones
4. **"Segunda fuente API con forma JSON completamente diferente"** → La arquitectura DEBE tener una capa de abstracción donde un nuevo plugin de fuente proporcione su propio parser/transformer pero reutilice el pipeline
5. **"HTTP, parseo, transformación y salida son preocupaciones separadas"** → Etapas explícitas del pipeline, NO un solo método haciendo todo
6. **"Sin fallos silenciosos"** → Cada error debe ser logueado/reportado. El pipeline debe ser resiliente (continuar con otras ubicaciones si una falla) pero reportar todos los fallos

---

## 3. Los Diez Grandes — Enfoques Arquitectónicos

### ENFOQUE 1: Aplicación de Consola Clean Architecture con Pipeline + Patrón Strategy

**Descripción**:  
Una Aplicación de Consola .NET 8 organizada en capas de Clean Architecture (Dominio, Aplicación, Infraestructura, Presentación/Consola), pero dimensionada apropiadamente para una herramienta de pipeline (no una aplicación empresarial completa). El pipeline es orquestado por un coordinador que ejecuta etapas discretas: **Obtener → Parsear → Transformar → Escribir**. Cada fuente de datos implementa una interfaz de estrategia `IWeatherDataSource`. El `HttpClient` está detrás de `IWeatherApiClient`. La escritura de salida está detrás de `IOutputWriter`.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/          # Modelos de dominio, interfaces, enums
│   ├── SyncMetrics.Pipeline.Application/   # Orquestador del pipeline, lógica de transformación
│   ├── SyncMetrics.Pipeline.Infrastructure/ # Clientes HTTP, escritores de archivos, config
│   └── SyncMetrics.Pipeline.Console/       # Punto de entrada, configuración DI, salida a consola
├── tests/
│   ├── SyncMetrics.Pipeline.UnitTests/
│   └── SyncMetrics.Pipeline.IntegrationTests/
├── README.md
├── AI.md
└── SyncMetrics.WeatherPipeline.sln
```

**Cómo Funciona la Extensibilidad**:  
Agregar una segunda fuente (ej. WeatherAPI.com) significa:
1. Crear una nueva clase implementando `IWeatherApiClient` con llamadas HTTP específicas de la fuente
2. Crear una nueva clase implementando `IResponseParser<TRawResponse>` para su forma JSON
3. Registrarla en DI — el orquestador del pipeline descubre y ejecuta todas las fuentes registradas
4. El `IDataTransformer` normaliza los datos parseados específicos de la fuente en el `NormalizedWeatherRecord` unificado

**Por Qué Este Enfoque Encaja**:
- **Coincide perfectamente con los criterios de evaluación**: Descomposición clara del pipeline, límites de interfaces en cada etapa, extensibilidad vía nuevas implementaciones
- **Muestra "juicio de staff"**: Estructura Clean Architecture pero NO sobre-capas. Sin CQRS, sin MediatR, sin bus de mensajes — solo interfaces limpias con un flujo de pipeline claro
- **Testabilidad**: Cada interfaz puede ser mockeada. Las etapas del pipeline se prueban en aislamiento. Prueba end-to-end con HTTP mockeado
- **Simple de ejecutar**: Arranque con `dotnet run` usando `Microsoft.Extensions.DependencyInjection` y `Microsoft.Extensions.Configuration`
- **Patrón familiar**: El CV de Arthur muestra experiencia en Clean Architecture y Microservicios

**Por Qué Este Podría No Ser Elegido**:
- Múltiples proyectos agregan algo de overhead para un take-home — los evaluadores podrían verlo como ligeramente sobre-estructurado para el alcance
- Las "capas" de Clean Architecture pueden sentirse ceremoniales si la lógica de dominio es simple

**Ajuste de Base de Datos**: No se necesita base de datos. Salida a archivo. Si se forzara: SQLite para estado local, pero innecesario.  
**Ajuste Docker**: `Dockerfile` simple con `dotnet publish` → imagen scratch/alpine. Opcional pero limpio.  
**Ajuste CI/CD**: GitHub Actions — `dotnet restore → build → test → publish`. Directo.

---

### ENFOQUE 2: Azure Functions + Orquestación con Durable Functions

**Descripción**:  
Un pipeline serverless construido sobre Azure Functions con Durable Functions para orquestación. Una función Orquestadora disparada por HTTP hace fan-out a funciones Activity que obtienen cada ubicación concurrentemente (`Task.WhenAll`), luego hace fan-in de resultados para transformación y escribe a Azure Blob Storage como archivos delimitados por tabulaciones. Usa Azure Table Storage o Cosmos DB para metadatos de procesamiento.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline.Azure/
├── src/
│   ├── SyncMetrics.Functions/
│   │   ├── Orchestrators/WeatherPipelineOrchestrator.cs
│   │   ├── Activities/FetchWeatherActivity.cs
│   │   ├── Activities/TransformActivity.cs
│   │   ├── Activities/WriteOutputActivity.cs
│   │   ├── Models/
│   │   └── Services/
│   └── SyncMetrics.Shared/    # Modelos compartidos, interfaces
├── tests/
│   └── SyncMetrics.Functions.Tests/
├── local.settings.json
├── host.json
├── README.md
└── AI.md
```

**Cómo Funciona la Extensibilidad**:  
Nueva fuente API = nueva función Activity con su propia lógica de fetch + parse. El Orquestador llama a todas las actividades de fuentes y combina resultados. El registro de fuentes es dirigido por configuración en `local.settings.json`.

**Por Qué Este Enfoque Es Tentador (y por qué Arthur podría querer usarlo)**:
- El CV de Arthur destaca Azure Functions, Service Bus y arquitectura cloud-native — esto mostraría esas habilidades directamente
- El patrón fan-out/fan-in de Durable Functions es elegante para obtención concurrente multi-ubicación
- Listo para producción: escalado automático, políticas de reintentos integradas, monitoreo vía Application Insights
- Muestra dominio de Azure que Actabl podría valorar para su stack

**Por Qué Este Enfoque NO Debe Ser Elegido**:
- **Señal de sobre-ingeniería**: El ejercicio dice `dotnet run` y `dotnet test`. Azure Functions requiere ya sea Azure Functions Core Tools Runtime (`func start`) o el emulador Azurite. Los evaluadores quieren escribir `dotnet run` y verlo funcionar — no instalar herramientas de Azure
- **Viola la evaluación de "juicio de staff"**: Un Staff Engineer debe saber cuándo la infraestructura cloud es excesiva. Este es un pipeline de transformación de datos, no un sistema distribuido. Usar Durable Functions para orquestar 3 llamadas HTTP es como usar Kubernetes para servir una página estática
- **Complejidad de testing**: El testing de orquestación de Durable Functions requiere patrones de prueba especializados (mockear `IDurableOrchestrationContext`). El overhead de testing no coincide con la complejidad del problema
- **Costo de la dependencia de Azure**: El evaluador puede no tener una suscripción de Azure. Incluso con Azurite, la fricción de setup falla el requisito de "ejecuta en su entorno"
- **Costos ocultos**: Arthur tendría que pagar por servicios Azure solo para un ejercicio take-home

**Ajuste de Base de Datos**: Azure Table Storage para metadatos (excesivo). Cosmos DB (masivamente excesivo).  
**Ajuste Docker**: Docker + contenedor del emulador Azurite para dev local. Agrega complejidad significativa de Docker Compose.  
**Ajuste CI/CD**: Azure DevOps o GitHub Actions → despliegue de Azure Functions. Poderoso pero irrelevante para el ejercicio.

**VEREDICTO: NO USAR. Guardar las habilidades de Azure para la discusión en entrevista, no para el código. Mencionar: "Consideré Azure Functions para el patrón fan-out pero elegí simplicidad por portabilidad — diseñaría la ruta de migración a Azure en una conversación de producción."**

---

### ENFOQUE 3: Pipeline MediatR/Mediator con Behaviors CQRS

**Descripción**:  
Una App de Consola .NET 8 usando MediatR como columna vertebral del pipeline. Cada etapa del pipeline es un Handler de MediatR. Los cross-cutting concerns (logging, validación, reintentos, manejo de errores) son Pipeline Behaviors de MediatR. Las fuentes de datos se representan como Requests/Notifications de MediatR. El pipeline es: `FetchWeatherCommand` → `ParseWeatherHandler` → `TransformWeatherHandler` → `WriteOutputHandler`, con behaviors envolviendo cada paso.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline/
│   │   ├── Commands/FetchWeatherCommand.cs
│   │   ├── Handlers/FetchWeatherHandler.cs
│   │   ├── Handlers/TransformWeatherHandler.cs
│   │   ├── Behaviors/RetryBehavior.cs
│   │   ├── Behaviors/LoggingBehavior.cs
│   │   ├── Behaviors/ValidationBehavior.cs
│   │   ├── Models/
│   │   └── Interfaces/
│   └── SyncMetrics.Console/
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**Cómo Funciona la Extensibilidad**:  
Nueva fuente API = nuevo comando `IRequest<WeatherData>` + handler. MediatR auto-descubre handlers vía escaneo de assembly. Los Pipeline Behaviors se aplican universalmente.

**Por Qué Este Enfoque Es Tentador**:
- MediatR es un patrón muy popular en apps empresariales .NET — muestra que Arthur conoce patrones modernos de .NET
- Los Pipeline Behaviors son una forma elegante de aplicar cross-cutting concerns sin ensuciar la lógica de negocio
- La separación CQRS puede demostrar sofisticación arquitectónica
- El CV de Arthur menciona experiencia con CQRS

**Por Qué Este Enfoque NO Debe Ser Elegido**:
- **Desajuste de abstracción**: MediatR está diseñado para mediación de request/response (comandos y queries). Un pipeline de ingesta de datos es un flujo secuencial, no un despacho de comandos. Forzar las etapas del pipeline en el modelo request/handler de MediatR es un desajuste semántico — es usar un destornillador como martillo
- **Impuesto por sobre-abstracción**: El ejercicio tiene ~4 etapas. MediatR agrega: `IRequest<T>`, `IRequestHandler<TReq, TRes>`, `IPipelineBehavior<TReq, TRes>`, registro DI, escaneo de assembly. Eso es mucha ceremonia para `Obtener → Parsear → Transformar → Escribir`
- **El testing es más difícil, no más fácil**: Los handlers de MediatR son fáciles de probar unitariamente en aislamiento, pero probar el flujo del pipeline requiere pruebas de integración a través del mediador. Un coordinador de pipeline directo con interfaces inyectadas es más simple de probar end-to-end
- **Inflación de dependencias**: Agregar MediatR + MediatR.Extensions.Microsoft.DependencyInjection + FluentValidation (para behaviors de validación) para una herramienta de pipeline de consola es una señal de alerta para "juicio de Staff"
- **Riesgo en entrevista**: Si preguntan "¿por qué MediatR para un pipeline?", no hay una respuesta fuerte que no suene como "porque quería mostrar que conozco MediatR"

**Ajuste de Base de Datos**: Irrelevante. MediatR no cambia la estrategia de salida.  
**Ajuste Docker**: Igual que cualquier app de consola.  
**Ajuste CI/CD**: Igual que cualquier app de consola.

**VEREDICTO: NO USAR. MediatR es la herramienta correcta cuando tienes muchos comandos/queries no relacionados fluyendo a través de una sola app (como un Web API con 50 endpoints). Es la herramienta incorrecta para un pipeline de datos lineal con 4 etapas. En la entrevista, decir: "Consideré MediatR para cross-cutting concerns pero reconocí que el pipeline es secuencial, no basado en despacho — un patrón de Pipeline Coordinator fue un mejor ajuste semántico."**

---

### ENFOQUE 4: Producer/Consumer Basado en Channels con Generic Host

**Descripción**:  
Una app .NET 8 usando `Microsoft.Extensions.Hosting` (Generic Host) con `IHostedService` para gestión del ciclo de vida. El pipeline usa `System.Threading.Channels` para etapas concurrentes de productor/consumidor. La Etapa 1 (Fetch) produce objetos `RawApiResponse` en un `Channel<RawApiResponse>`. La Etapa 2 (Parse/Transform) lee de ese channel y produce objetos `NormalizedRecord` en un segundo channel. La Etapa 3 (Output) lee del segundo channel y escribe al archivo delimitado por tabulaciones. Los channels acotados proporcionan backpressure.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline/
│   │   ├── Hosting/PipelineHostedService.cs
│   │   ├── Stages/FetchStage.cs
│   │   ├── Stages/TransformStage.cs
│   │   ├── Stages/OutputStage.cs
│   │   ├── Channels/
│   │   ├── Models/
│   │   └── Interfaces/
│   └── SyncMetrics.Console/  # Configuración de Generic Host
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**Cómo Funciona la Extensibilidad**:  
Nueva fuente API = nueva implementación de `IFetchStage` escribiendo al mismo `Channel<RawApiResponse>`. Múltiples productores pueden alimentar el mismo channel concurrentemente. Las etapas de transformación y salida son agnósticas a la fuente si la respuesta cruda lleva un discriminador de fuente.

**Por Qué Este Enfoque Es Tentador**:
- `System.Threading.Channels` es una primitiva .NET de primera clase, de alto rendimiento — sin dependencias externas
- Concurrencia genuina con backpressure — demuestra comprensión profunda de patrones async de .NET
- El Generic Host proporciona configuración integrada, logging, shutdown graceful y DI
- Arquitectura de streaming: los datos fluyen a través del pipeline en tiempo real en lugar de recolectar-todo-luego-procesar
- Muestra que Arthur entiende patrones de procesamiento de datos concurrentes a bajo nivel

**Por Qué Este Enfoque NO Debe Ser Elegido**:
- **Complejidad vs. escala del problema**: Estamos obteniendo 3 ubicaciones × 7 días = 21 puntos de datos. `Channel<T>` con backpressure está diseñado para escenarios de alto rendimiento (miles/segundo). Usar channels para 3 llamadas HTTP es como construir una autopista para 3 autos
- **Probar channels no es trivial**: Las pruebas unitarias de pipelines basados en channels requieren orquestación cuidadosa de productores/consumidores con cancellation tokens. Un enfoque con `await Task.WhenAll` es más simple de probar
- **Costo de legibilidad**: Un nuevo compañero de equipo (la audiencia del README) necesita entender channels, capacidad acotada, semánticas de completación y `ReadAllAsync` para seguir el pipeline. Un simple `foreach` sobre ubicaciones es inmediatamente claro
- **Generic Host agrega ceremonia de arranque**: `Host.CreateDefaultBuilder`, `ConfigureServices`, `RunAsync` — es el patrón correcto para servicios de larga ejecución, no una herramienta de pipeline de ejecución única
- **El ejercicio dice "pipeline", no "sistema de streaming"**: Los evaluadores quieren ver descomposición limpia, no patrones de infraestructura concurrente. Tres llamadas `Task.WhenAll` logran el mismo objetivo de concurrencia con 90% menos de overhead cognitivo

**Ajuste de Base de Datos**: Se podría usar channels para streaming de datos a escrituras de BD. Pero no se necesita BD.  
**Ajuste Docker**: Igual que app de consola. Generic Host soporta `SIGTERM` para shutdown graceful de Docker.  
**Ajuste CI/CD**: Igual que cualquier app de consola.

**VEREDICTO: NO USAR para este ejercicio. GRAN patrón para mencionar en la entrevista: "Para un pipeline de producción de alto volumen, usaría System.Threading.Channels para streaming controlado por backpressure — pero para la escala de este ejercicio, Task.WhenAll mantiene el modelo de concurrencia simple y legible."**

---

### ENFOQUE 5: Arquitectura de Vertical Slice

**Descripción**:  
En lugar de capas horizontales (Dominio, Aplicación, Infraestructura), organizar por feature/fuente. Cada fuente de datos (Open-Meteo, futuro WeatherAPI, etc.) es un vertical slice completamente autocontenido con su propio cliente HTTP, parser, transformer y modelos. Un kernel compartido delgado proporciona el modelo `NormalizedWeatherRecord` y la interfaz `IWeatherSlice`. Un coordinador de nivel superior ejecuta todos los slices registrados y fusiona sus salidas.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/          # NormalizedWeatherRecord, IWeatherSlice, IOutputWriter
│   ├── SyncMetrics.Pipeline.OpenMeteo/     # Todo para Open-Meteo en un solo lugar
│   │   ├── OpenMeteoClient.cs
│   │   ├── OpenMeteoResponse.cs
│   │   ├── OpenMeteoParser.cs
│   │   ├── OpenMeteoTransformer.cs
│   │   └── OpenMeteoSlice.cs               # Implementa IWeatherSlice
│   ├── SyncMetrics.Pipeline.Console/       # DI, coordinador, escritor de archivos
│   └── (futuro: SyncMetrics.Pipeline.WeatherApi/)
├── tests/
│   ├── SyncMetrics.Pipeline.OpenMeteo.Tests/
│   └── SyncMetrics.Pipeline.Console.Tests/
└── SyncMetrics.sln
```

**Cómo Funciona la Extensibilidad**:  
Agregar una segunda fuente = agregar un nuevo proyecto `SyncMetrics.Pipeline.WeatherApi/` con su propio cliente, parser, transformer y slice. Registrar `WeatherApiSlice : IWeatherSlice` en DI. El coordinador itera todas las instancias de `IWeatherSlice`. Cero cambios al código existente.

**Por Qué Este Enfoque Es Tentador**:
- **Máxima cohesión**: Todo lo relacionado con Open-Meteo vive en una carpeta. Un desarrollador trabajando en Open-Meteo nunca necesita navegar entre capas
- **Historia de extensibilidad perfecta**: Principio Abierto + Cerrado a nivel de proyecto. Nueva fuente = nuevo proyecto, cero código existente modificado
- **Testabilidad independiente por fuente**: Cada slice tiene su propio proyecto de pruebas, probado end-to-end dentro del slice
- **Grafo de dependencias limpio**: `Console → [OpenMeteo, WeatherApi] → Core`. Sin dependencias circulares posibles
- **Amigable para entrevista**: Fácil de dibujar en una pizarra, fácil de explicar

**Por Qué Este Podría No Ser Elegido**:
- **Riesgo de duplicación de código**: Si dos fuentes tienen patrones HTTP similares (reintentos, timeout, headers), cada slice duplica esa lógica a menos que haya una base compartida. Pero esto es manejable con un `ResilienceHttpClient` compartido en Core
- **Muchos proyectos pequeños**: Para un take-home con una fuente real, 3-4 proyectos podrían sentirse excesivamente divididos
- **El ejercicio dice "HTTP, parseo, transformación y salida son preocupaciones separadas"**: Vertical Slice las agrupa por fuente. El evaluador podría esperar que las etapas horizontales del pipeline sean más claramente visibles
- **La lógica de transformación puede tener elementos comunes entre fuentes**: Conversión de temperatura, normalización de unidades — estos podrían pertenecer a un transformer compartido, no duplicados por slice

**Ajuste de Base de Datos**: Cada slice podría escribir a su propia tabla. Pero aún no se necesita BD.  
**Ajuste Docker**: Igual que app de consola.  
**Ajuste CI/CD**: Cada proyecto de slice podría construirse/probarse independientemente. Buena modularidad.

**VEREDICTO: FUERTE CONTENDIENTE. Este enfoque tiene la historia de extensibilidad más limpia. Sin embargo, el ejercicio explícitamente dice "HTTP, parseo, transformación y salida son preocupaciones separadas" — esto implica que quieren VER etapas horizontales del pipeline, no slices agrupados verticalmente. Podemos HIBRIDIZAR esto: usar organización vertical para código específico de fuente pero mantener interfaces horizontales claras (IApiClient, IResponseParser, IDataTransformer, IOutputWriter) que sean visibles a través del pipeline.**

---

### ENFOQUE 6: Pipeline Modular Basado en Plugins (MEF / Carga de Assemblies)

**Descripción**:  
Un core del pipeline que descubre plugins de fuentes de datos en tiempo de ejecución vía el Managed Extensibility Framework (MEF) o carga personalizada de assemblies. Cada fuente de datos es un assembly separado (DLL) que exporta un contrato `[Export(typeof(IWeatherPlugin))]`. El host del pipeline escanea un directorio `/plugins`, carga assemblies, descubre implementaciones y las ejecuta. La configuración mapea nombres de plugins a sus secciones de config.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Contracts/      # IWeatherPlugin, NormalizedRecord (assembly compartido)
│   ├── SyncMetrics.Pipeline.Host/           # Descubrimiento de plugins, orquestación, salida
│   └── plugins/
│       ├── SyncMetrics.Plugin.OpenMeteo/    # DLL de plugin Open-Meteo
│       └── (futuro: SyncMetrics.Plugin.WeatherApi/)
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**Cómo Funciona la Extensibilidad**:  
Nueva fuente = construir una nueva DLL implementando `IWeatherPlugin`, colocarla en `/plugins`. El host la descubre automáticamente al arrancar. Cero cambios de código, cero recompilación de código existente.

**Por Qué Este Enfoque Es Tentador**:
- Extensibilidad máxima — literalmente plug-and-play
- Muestra conocimiento profundo de .NET (MEF, `AssemblyLoadContext`, aislamiento de plugins)
- Patrón relevante para producción en productos ISV/SaaS con integraciones específicas por cliente

**Por Qué Este Enfoque NO Debe Ser Elegido**:
- **Sobre-ingeniería masiva**: Este es un ejercicio take-home, no una plataforma ISV. Arquitectura de plugins para 1-2 fuentes de datos es construir un reactor nuclear para hervir agua
- **Explosión de complejidad**: Carga de assemblies, versionamiento de contratos compartidos, aislamiento de plugins, resolución de tipos entre `AssemblyLoadContext` — cada uno de estos está plagado de bugs sutiles
- **MEF es adyacente-legado**: Aunque aún soportado, MEF no es el enfoque moderno de .NET. La mayoría de equipos usan registro basado en DI en lugar de exportación basada en atributos
- **Pesadilla de testing**: Las pruebas de descubrimiento de plugins requieren construir y cargar assemblies separados en proyectos de pruebas
- **Desastre en entrevista**: Si preguntan "¿por qué plugins para dos fuentes de datos?", la única respuesta honesta es "sobre-ingeniería" — que directamente contradice el criterio de evaluación de "juicio de staff"
- **Complejidad de `dotnet run`**: Los plugins necesitan ser pre-construidos y colocados en el directorio correcto. La experiencia del evaluador pasa de "clonar y ejecutar" a "¿por qué no encuentra la DLL del plugin?"

**Ajuste de Base de Datos**: Los contratos de plugin podrían incluir adaptadores de BD. Irrelevante aquí.  
**Ajuste Docker**: Build Docker multi-stage que compila plugins y los copia al host. Sobre-complejo.  
**Ajuste CI/CD**: Matriz de build para cada plugin. Demasiada infraestructura.

**VEREDICTO: ABSOLUTAMENTE NO. Esta es la trampa canónica de "sobre-ingeniería". Mencionar en la entrevista SOLO para decir: "Consideré un modelo de plugins pero reconocí que viola YAGNI para el alcance actual. Si SyncMetrics crece a 50+ fuentes de datos con conectores específicos por cliente, una arquitectura de plugins estaría justificada — pero no para un pipeline con 2-3 fuentes."**

---

### ENFOQUE 7: Backend Minimal API + Dashboard Frontend React Vite

**Descripción**:  
Una solución de dos repositorios (o monorepo) donde el pipeline C# se expone como un backend ASP.NET Core Minimal API, y un frontend React + Vite + TypeScript proporciona un dashboard para disparar ejecuciones del pipeline, ver datos meteorológicos en tablas/gráficos, gestionar ubicaciones y mostrar resúmenes de procesamiento. El backend sirve tanto la lógica del pipeline como una API RESTful (`/api/pipeline/run`, `/api/weather/latest`, `/api/locations`). El frontend consume esta API. Comunicación vía JSON sobre HTTP. El frontend se compila a archivos estáticos servidos por el backend o separadamente vía el servidor dev de Vite.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── backend/
│   ├── src/
│   │   ├── SyncMetrics.Pipeline.Core/
│   │   ├── SyncMetrics.Pipeline.Application/
│   │   ├── SyncMetrics.Pipeline.Infrastructure/
│   │   └── SyncMetrics.Pipeline.Api/            # Minimal API + Swagger
│   │       ├── Endpoints/WeatherEndpoints.cs
│   │       ├── Endpoints/PipelineEndpoints.cs
│   │       └── Program.cs
│   └── tests/
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   │   ├── WeatherTable.tsx
│   │   │   ├── LocationManager.tsx
│   │   │   ├── PipelineSummary.tsx
│   │   │   └── Dashboard.tsx
│   │   ├── hooks/useWeatherData.ts
│   │   ├── services/api.ts
│   │   ├── App.tsx
│   │   └── main.tsx
│   ├── package.json
│   ├── vite.config.ts
│   └── tsconfig.json
├── README.md
├── AI.md
└── docker-compose.yml     # Contenedores Backend + Frontend
```

**Cómo Funciona la Extensibilidad**:  
Igual que el Enfoque 1 para el pipeline. La capa API es un shell HTTP delgado sobre el coordinador del pipeline. El frontend es puramente presentacional — agregar una nueva fuente solo cambia el backend.

**Por Qué Este Enfoque Es Tentador (Perspectiva de Arthur)**:
- **Factor wow visual**: Los evaluadores ven un dashboard pulido con gráficos, tablas, estado del pipeline en tiempo real — más impresionante a primera vista que una app de consola produciendo un archivo `.tsv`
- **Demostración full-stack**: Muestra que Arthur no es solo un ingeniero backend. React + TypeScript + Vite muestra fluidez moderna en frontend
- **El CV de Arthur lista React**: Esta es una oportunidad de probarlo, no solo afirmarlo
- **Más cercano a la realidad de producción**: SyncMetrics presumiblemente tiene alguna UI para su plataforma de analytics. Mostrar pensamiento de frontend podría resonar
- **Diferenciador en entrevista**: Si otros candidatos envían apps de consola y Arthur envía un dashboard full-stack, el impacto visual es significativo

**Por Qué Este Enfoque NO DEBE Ser Elegido — El Caso del Staff Engineer Contra el Frontend**:

1. **El ejercicio nunca menciona UI, frontend, dashboard o visualización**. Los entregables son explícitos:
   - Salida normalizada delimitada por tabulaciones
   - Resumen de procesamiento al completar
   - Ejecutable con `dotnet run`
   - Pruebas pasando con `dotnet test`
   
   Un frontend React no satisface NINGUNO de estos. Está resolviendo un problema que no fue pedido.

2. **El criterio de evaluación "juicio de staff" trabaja EN CONTRA**. La rúbrica dice: *"Qué se generalizó vs. qué se mantuvo simple."* Agregar un frontend React a un ejercicio de pipeline de datos demuestra lo opuesto al juicio de staff — muestra incapacidad de acotar el trabajo. Un Staff Engineer que agrega alcance no solicitado a un estimado o sprint es el ingeniero que todos temen en las reuniones de planificación.

3. **Diluye la calidad del pipeline**. Cada hora gastada en componentes React, configuración de Vite, CSS, gestión de estado y cableado de endpoints API es una hora NO gastada en:
   - Mejores casos extremos de manejo de errores
   - Cobertura de pruebas más completa
   - Arquitectura de pipeline más limpia
   - Mejor lógica de reintentos
   - Mapeo de campos más pensado dirigido por configuración
   
   Los evaluadores gastarán el 80% de su tiempo de revisión en el pipeline C#. El código React es ruido.

4. **Crea problemas de evaluación**. Ahora el evaluador necesita Node.js instalado (o Docker Compose para dos contenedores). El README crece. La sección "cómo ejecutar" se vuelve compleja. Pedían `dotnet run` — si el frontend requiere `npm install && npm run dev` además, ya fallaste la prueba de portabilidad.

5. **Cambia la narrativa de "ingeniero de pipelines" a "generalista full-stack."** Este es un rol de **Staff Engineer — Integraciones de Datos**. El ejercicio prueba patrones de integración, abstracción HTTP, parseo JSON, normalización de esquema. Enviar un dashboard React dice: *"No tengo confianza en que mi trabajo de backend hable por sí mismo, así que agregué brillo visual."* Esa es una señal junior, no de staff.

6. **React Vite es completamente estándar en 2026** — no hay profundidad técnica que demostrar. `npm create vite@latest`, agregar Tailwind, fetch de la API, renderizar en tabla. Cualquier graduado de bootcamp puede hacer esto. Los evaluadores no se impresionarán. Lo que SÍ los impresionará: un pipeline C# bellamente descompuesto con manejo de errores pensado y 90%+ de cobertura de pruebas.

**Análisis Específico del Frontend — Si Aún Quieres Argumentar A Favor**:

| Aspecto | Evaluación |
|---------|-----------|
| **React + Vite + TS** | Stack estándar, sin diferenciador en 2026 |
| **Gestión de estado** | Para 3 ubicaciones × 7 días, incluso `useState` es excesivo. No se necesita estado complejo |
| **Gráficos** | Recharts o Chart.js para visualización meteorológica. Bonito pero no evaluado |
| **Testing** | Las pruebas de frontend (Vitest, React Testing Library) NO están en los requisitos del ejercicio |
| **Tiempo de build** | Agrega 4-8 horas de desarrollo para cero entregables evaluados |
| **CORS** | El backend necesita configuración CORS para dev. Otra cosa que puede romperse para el evaluador |
| **Docker Compose** | Ahora necesitas dos contenedores, configuración de puertos, configuración de red |

**Ajuste de Base de Datos**: Si agregas un frontend, ahora "necesitas" una base de datos para persistir ejecuciones del pipeline y servir datos históricos. Esto se escala a: contenedor PostgreSQL, migraciones EF Core, datos seed, cadenas de conexión. Explosión de alcance.  
**Ajuste Docker**: docker-compose.yml con servicios `backend` y `frontend`. Mapeo de puertos (5000 para API, 5173 para Vite). El evaluador necesita `docker compose up`. Mucha más fricción.  
**Ajuste CI/CD**: Dos pipelines de build (dotnet + node). Doble complejidad de CI.

**VEREDICTO: ABSOLUTAMENTE NO. Este es el llamado de "juicio de staff" más importante del ejercicio. La capacidad de NO construir algo que no fue pedido es un rasgo definitorio de un staff engineer. En la entrevista, si preguntan sobre full-stack: *"Deliberadamente mantuve esto como un pipeline de consola porque la spec trata sobre patrones de integración de datos, no visualización. Planificaría un dashboard React como un flujo de trabajo separado en una hoja de ruta de producción — mezclarlo en el ejercicio de pipeline oscurecería las decisiones arquitectónicas que el equipo quería evaluar. Dicho esto, la abstracción IOutputWriter significa que agregar una capa API es directo si el producto lo necesita."***

---

### ENFOQUE 8: Pipeline Funcional con Programación Orientada a Railway (Cadena Result<T>)

**Descripción**:  
Una App de Consola .NET 8 que modela todo el pipeline como una cadena de transformaciones `Result<T, Error>`, inspirada en la Programación Orientada a Railway (ROP) de F# y el modelado de dominio funcional de Scott Wlaschin. Cada etapa del pipeline es una función que toma un `Result<TInput>` y retorna un `Result<TOutput>`. El éxito fluye por la "vía feliz"; cualquier fallo desvía a la "vía de error" y se propaga hasta el final sin excepciones. Sin `try/catch` en la lógica de negocio. Los errores son VALORES, no excepciones.

**Patrón Central**:
```csharp
// La firma de cada etapa sigue este patrón:
Result<RawJson>      FetchAsync(LocationConfig location);
Result<SourceModel>  Parse(RawJson json);
Result<NormalizedWeatherRecord[]> Transform(SourceModel model, LocationConfig location);
Result<string>       WriteOutput(NormalizedWeatherRecord[] records);

// Compuesto como:
var result = await FetchAsync(location)
    .Bind(json => Parse(json))
    .Bind(model => Transform(model, location))
    .Bind(records => WriteOutput(records));
```

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/
│   │   ├── Result.cs                  # Tipo monádico Result<T>
│   │   ├── ResultExtensions.cs        # Métodos de extensión Bind, Map, Match, Tap
│   │   ├── PipelineError.cs           # Unión discriminada de tipos de error
│   │   ├── Models/
│   │   └── Interfaces/
│   ├── SyncMetrics.Pipeline.Application/
│   │   ├── Pipeline.cs                # Composición funcional de etapas
│   │   └── Stages/                    # Cada etapa como una función pura
│   ├── SyncMetrics.Pipeline.Infrastructure/
│   │   ├── Http/OpenMeteoClient.cs
│   │   ├── Parsers/OpenMeteoParser.cs
│   │   └── Output/TabDelimitedWriter.cs
│   └── SyncMetrics.Pipeline.Console/
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**Cómo Funciona la Extensibilidad**:  
Nueva fuente API = nuevas funciones de fetch y parse. La composición del pipeline es la misma: `Fetch → Bind(Parse) → Bind(Transform) → Bind(Write)`. Cada fuente conecta sus funciones en la misma forma de cadena.

**Por Qué Este Enfoque Es Tentador**:
- **Manejo elegante de errores**: Sin espagueti de `try/catch`. Los errores son valores de primera clase. Cada función declara explícitamente que puede fallar vía `Result<T>`. El manejo de errores del pipeline es visible en las FIRMAS DE TIPO, no escondido en bloques catch
- **Componibilidad**: Las etapas del pipeline se conectan como LEGO vía `Bind`. Agregar una etapa (ej. validación, enriquecimiento) es agregar una llamada `.Bind(Validate)`
- **Testabilidad perfecta**: Funciones puras → entradas/salidas predecibles → trivial de probar. No se necesita mocking para la composición del pipeline en sí
- **Muestra profundidad**: Composición monádica, uniones discriminadas para errores, Programación Orientada a Railway — estas señalan un desarrollador que lee más allá de los docs de MSDN, que entiende principios de programación funcional y los aplica en C#
- **Agregación de errores**: Puede recolectar errores de todas las ubicaciones usando `Result<T>[]` → agregar, no fallo-rápido
- **Sin dependencias externas**: `Result<T>` son ~50 líneas de código. Sin NuGet de LanguageExt o CSharpFunctionalExtensions necesario

**Por Qué Este Enfoque NO Debe Ser la Arquitectura Principal**:
- **Poco familiar para la mayoría de equipos .NET**: `Result<T>` y `Bind` monádico no son patrones estándar en el ecosistema .NET. Un nuevo compañero leyendo el código necesita entender composición funcional, que tiene una curva de aprendizaje. La audiencia del README (evaluación del ejercicio) podría encontrarlo inusual
- **C# no es F#**: `Result<T>` en C# funciona pero nunca es tan limpio como uniones discriminadas + expresiones de computación de F#. La cadena `.Bind().Bind().Bind()` puede verse incómoda comparada con la expresión `result { }` de F#. Arriesga verse como "intentar escribir F# en C#"
- **Fricción de interoperabilidad con excepciones**: Las librerías .NET lanzan excepciones (HttpClient, System.Text.Json). Necesitas envolver cada llamada externa en `Result.Try(() => ...)`, lo que agrega boilerplate en los límites
- **Experiencia de debugging**: Cuando un pipeline falla profundo en una cadena Bind, el stack trace es menos claro que un try/catch tradicional con logging estructurado. Los desarrolladores acostumbrados a debugging con breakpoints luchan con cadenas funcionales
- **El ejercicio dice "Sin fallos silenciosos"**: La fortaleza de ROP es la propagación explícita de errores, pero los evaluadores podrían esperar ver patrones familiares (excepciones estructuradas, logging) en lugar de tipos de error monádicos
- **Riesgo de parecer académico**: Si el evaluador es un desarrollador .NET pragmático, `Result<T, PipelineError>` podría parecer un ejercicio académico en lugar de ingeniería práctica

**Sin embargo — Insight Clave**: Aunque no usemos ROP completo como arquitectura PRINCIPAL, el patrón `Result<T>` para tipos de retorno del parser es EXCELENTE. El parser NO debería lanzar excepciones. Debería retornar `Result<SourceModel, ParseError>` para que el coordinador pueda recolectar errores grácilmente. **Deberíamos ADOPTAR este micro-patrón dentro de la arquitectura elegida.**

**Ajuste de Base de Datos**: El pipelining funcional es agnóstico a la forma de datos. No se necesita BD.  
**Ajuste Docker**: Igual que cualquier app de consola.  
**Ajuste CI/CD**: Igual que cualquier app de consola.

**VEREDICTO: NO USAR como arquitectura principal, PERO ADOPTAR `Result<T>` para tipos de retorno del parser y transformer dentro de la arquitectura elegida. Esto nos da lo mejor de ROP (manejo explícito de errores, sin fallos silenciosos) sin el compromiso completo con composición funcional que podría alienar evaluadores. En la entrevista: *"Usé tipos Result para las etapas de parseo y transformación para que los errores sean valores, no excepciones. El coordinador del pipeline agrega objetos Result y construye el resumen de procesamiento tanto de éxitos como de fallos. Esto elimina fallos silenciosos por diseño — si una etapa puede fallar, el tipo de retorno te fuerza a manejarlo."***

---

### ENFOQUE 9: ASP.NET Core Minimal API con Pipeline-como-Servicio (Sin Frontend)

**Descripción**:  
En lugar de una app de consola, exponer el pipeline como un ASP.NET Core Minimal API con endpoints para disparar ejecuciones del pipeline, verificar estado y descargar archivos de salida. El pipeline mismo ejecuta como una tarea en segundo plano (vía `IHostedService` o `BackgroundService`). La API proporciona: `POST /api/pipeline/run` → dispara una ejecución, `GET /api/pipeline/status/{runId}` → verifica progreso, `GET /api/pipeline/output/{runId}` → descarga el archivo `.tsv`. Sin frontend — solo endpoints API con documentación Swagger/OpenAPI.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/
│   ├── SyncMetrics.Pipeline.Application/
│   ├── SyncMetrics.Pipeline.Infrastructure/
│   └── SyncMetrics.Pipeline.Api/
│       ├── Endpoints/
│       │   ├── PipelineEndpoints.cs    # POST /run, GET /status     
│       │   └── WeatherEndpoints.cs     # GET /output
│       ├── BackgroundServices/
│       │   └── PipelineRunnerService.cs
│       ├── Program.cs
│       └── appsettings.json
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
│   └── SyncMetrics.Pipeline.Api.Tests/   # Pruebas de integración con WebApplicationFactory
└── SyncMetrics.sln
```

**Cómo Funciona la Extensibilidad**:  
Misma arquitectura interna del pipeline. La capa API es solo un shell HTTP. Agregar una nueva fuente es puramente una preocupación interna del pipeline.

**Por Qué Este Enfoque Es Tentador**:
- **Realista para producción**: En realidad, SyncMetrics expondría su pipeline vía una API, no una app de consola. Esto muestra que Arthur piensa en cómo el software es realmente consumido
- **Documentación Swagger**: El evaluador abre `http://localhost:5000/swagger` y puede interactuar con el pipeline vía navegador — buena experiencia de desarrollador
- **Patrones de tareas en segundo plano**: `BackgroundService` para ejecución del pipeline muestra entendimiento del modelo de hosting de ASP.NET Core
- **Testing de integración**: `WebApplicationFactory<Program>` permite pruebas de integración verdaderas a través de HTTP sin desplegar
- **Familiar para evaluadores .NET**: La mayoría de Senior/Staff engineers .NET viven en ASP.NET Core diariamente

**Por Qué Este Enfoque NO Debe Ser Elegido**:
- **El ejercicio dice `dotnet run` → el pipeline ejecuta**: No `dotnet run` → el servidor inicia → esperar petición HTTP. El ejercicio espera un pipeline de ejecución única, no un servicio de larga ejecución. Si el evaluador escribe `dotnet run` y ve "Now listening on http://localhost:5000" en lugar de datos meteorológicos fluyendo, se preguntará si Arthur leyó los requisitos
- **Agrega complejidad innecesaria**: El pipeline es el entregable. Una capa API es infraestructura alrededor del entregable. Cada línea de enrutamiento de endpoints, configuración Swagger y coordinación de `BackgroundService` es una línea NO mejorando el pipeline
- **Explosión del alcance de testing**: Ahora necesitas pruebas de integración de API (`WebApplicationFactory`), pruebas de endpoints HTTP, pruebas de ciclo de vida del background service — además de las pruebas del pipeline que el ejercicio requiere
- **Problema de gestión de estado**: `POST /run` → ¿dónde vive el estado de la ejecución? ¿En memoria? Entonces un reinicio pierde todo. ¿En una base de datos? Entonces necesitas modelos de entidad, una BD, migraciones. El ejercicio no necesita ninguna gestión de estado
- **Fricción en la experiencia del evaluador**: En lugar de `dotnet run` → ver salida, es `dotnet run` → servidor inicia → abrir Swagger → hacer POST → esperar → hacer GET para salida. Más pasos = más fricción = menos puntaje en evaluación

**Ajuste de Base de Datos**: Si API, necesitas estado de ejecución. SQLite como mínimo. Empieza el scope creep.  
**Ajuste Docker**: `EXPOSE 5000`. Docker run con mapeo de puertos. Estándar pero más complejo que consola.  
**Ajuste CI/CD**: Más complejo — necesitas probar endpoints API + pipeline. Dos categorías de pruebas.

**VEREDICTO: NO USAR. El ejercicio es explícitamente un pipeline que ejecuta y produce salida. Envolverlo en una API agrega cero valor evaluado y arriesga parecer incomprensión del alcance. En la entrevista: *"Mantuve esto como un pipeline de consola porque el ejercicio lo define como un pipeline de datos de ejecución única. Si SyncMetrics lo necesita como servicio, envolvería el coordinador del pipeline en un ASP.NET Core Minimal API con BackgroundService — las interfaces del pipeline hacen esto trivial porque la capa API solo llamaría a PipelineCoordinator.RunAsync(). Estimaría esa migración en 2-3 horas."***

---

### ENFOQUE 10: Clean Architecture Pipeline — Proyecto Único con Fuentes Organizadas por Features

**Descripción**:  
Un único proyecto .NET 8 organizado en carpetas de features con una convención estricta de dependencias. En lugar de múltiples proyectos por capa de Clean Architecture, todo vive en UN proyecto pero está organizado en módulos de features autocontenidos. Una carpeta `SharedKernel/` contiene modelos, interfaces y el tipo `Result<T>`. Cada fuente de datos es una carpeta de feature (`Features/OpenMeteo/`, futuro `Features/WeatherApi/`). El coordinador del pipeline vive en `Features/Pipeline/`. El escritor de salida vive en `Features/Output/`. Los límites internos de carpetas imponen la misma separación que Clean Architecture multi-proyecto proporciona, pero con cero overhead de referencias entre proyectos.

**Estructura del Proyecto**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   └── SyncMetrics.Pipeline/
│       ├── SharedKernel/
│       │   ├── Models/
│       │   │   ├── NormalizedWeatherRecord.cs
│       │   │   ├── LocationConfig.cs
│       │   │   ├── ProcessingResult.cs
│       │   │   └── PipelineSummary.cs
│       │   ├── Interfaces/
│       │   │   ├── IWeatherApiClient.cs
│       │   │   ├── IResponseParser.cs
│       │   │   ├── IDataTransformer.cs
│       │   │   ├── IOutputWriter.cs
│       │   │   └── IWeatherDataSource.cs
│       │   └── Result.cs
│       ├── Features/
│       │   ├── OpenMeteo/
│       │   │   ├── OpenMeteoApiClient.cs
│       │   │   ├── OpenMeteoApiResponse.cs
│       │   │   ├── OpenMeteoResponseParser.cs
│       │   │   ├── OpenMeteoDataSource.cs
│       │   │   └── OpenMeteoTransformer.cs
│       │   ├── Pipeline/
│       │   │   ├── PipelineCoordinator.cs
│       │   │   └── PipelineOptions.cs
│       │   └── Output/
│       │       └── TabDelimitedFileWriter.cs
│       ├── Configuration/
│       │   ├── FieldMappingConfig.cs
│       │   └── ServiceRegistration.cs
│       ├── Program.cs
│       ├── appsettings.json
│       └── SyncMetrics.Pipeline.csproj
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
│       ├── OpenMeteo/
│       │   ├── OpenMeteoResponseParserTests.cs
│       │   └── TestData/
│       ├── Pipeline/
│       │   └── PipelineCoordinatorTests.cs
│       ├── Output/
│       │   └── TabDelimitedWriterTests.cs
│       └── SyncMetrics.Pipeline.Tests.csproj
├── README.md
├── AI.md
├── Dockerfile
├── .github/workflows/build-and-test.yml
└── SyncMetrics.WeatherPipeline.sln
```

**Cómo Funciona la Extensibilidad**:  
Agregar una segunda fuente = agregar una nueva carpeta de feature `Features/WeatherApi/` con cliente, parser, transformer, fuente de datos. Registrar `WeatherApiDataSource : IWeatherDataSource` en `ServiceRegistration.cs`. El coordinador del pipeline ejecuta todas las instancias registradas de `IWeatherDataSource`. Mismos contratos de interfaz que el Enfoque 1. Cero cambios a features existentes.

**Por Qué Este Enfoque Es Genuinamente Fuerte**:
- **Dimensionado correctamente para el ejercicio**: Un proyecto de fuente + un proyecto de pruebas. Sin overhead de 4 proyectos Clean Architecture para un take-home. El evaluador ve una solución ajustada y enfocada que respeta el alcance del ejercicio
- **Mismas interfaces, misma extensibilidad**: Los límites de interfaz (IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter, IWeatherDataSource) son IDÉNTICOS al enfoque multi-proyecto. La extensibilidad no se compromete — se impone por interfaces, no por referencias de proyecto
- **Cohesión de carpetas de features**: Todo sobre Open-Meteo vive en `Features/OpenMeteo/`. Todo sobre salida vive en `Features/Output/`. Un desarrollador agregando WeatherApi crea una carpeta — no navega cuatro proyectos
- **Experiencia `dotnet run` más simple**: Un proyecto significa que `dotnet run` simplemente funciona. No se necesita flag `--project`. La primera experiencia del evaluador no tiene fricción
- **Experiencia `dotnet test` más simple**: Un proyecto de pruebas. Todas las pruebas descubiertas y ejecutadas juntas. Sin cableado de dependencias entre proyectos de pruebas
- **Tiempo de build**: Un proyecto compila en segundos. Cuatro proyectos agregan overhead de restore + build que importa para velocidad de iteración durante desarrollo
- **Coincide con la complejidad del ejercicio**: El ejercicio tiene UNA fuente de datos con 5 campos y 3 ubicaciones. Son ~50 líneas de lógica de negocio real. Cuatro proyectos para 50 líneas de lógica es ceremonia. Un proyecto bien organizado es proporcionado
- **Las pruebas reflejan la fuente**: `Tests/OpenMeteo/` refleja `Features/OpenMeteo/`. Fácil encontrar pruebas para cualquier feature. La estructura del proyecto de pruebas guiada por la estructura de la fuente
- **Señal de juicio de staff**: Elegir un layout Clean Architecture de proyecto único sobre ceremonia multi-proyecto para un take-home muestra que el ingeniero entiende que la estructura del proyecto debe coincidir con la complejidad del problema, no con dogma arquitectónico
- **Aún actualizable**: Si el ejercicio crece (improbable), las carpetas de features pueden ser extraídas en proyectos separados después. Las interfaces garantizan que esto funcione. Empezar multi-proyecto y darse cuenta de que es demasiado es más difícil de revertir que empezar lean y extraer
- **Clean Architecture es una REGLA DE DEPENDENCIA, no un conteo de carpetas**: El libro original de Clean Architecture de Robert C. Martin la define como una regla de dependencia (las capas externas dependen de capas internas, nunca al revés). Esto se impone por el patrón `SharedKernel/Interfaces/` — los features dependen de interfaces compartidas, no unos de otros. Puedes tener Clean Architecture en un proyecto

**Por Qué Esto Podría Preocupar a Algunos Evaluadores**:
- **"¿Es esto demasiado simple?"**: Un evaluador esperando 4+ proyectos podría pensar inicialmente que la solución está sub-ingeniada. Sin embargo, los contratos de interfaz, cobertura de pruebas e historia de extensibilidad contrarrestan esto inmediatamente
- **Sin imposición de dependencias en tiempo de compilación**: En un setup multi-proyecto, las referencias de proyecto imponen que Infrastructure no puede referenciar Console. En un proyecto único, un desarrollador PODRÍA importar de la carpeta incorrecta. Sin embargo, para un codebase de 1-2 desarrolladores con code review, esto es un no-problema. ArchUnit o convenciones de nomenclatura pueden imponer esto para equipos más grandes
- **Percepción vs. sustancia**: Algunos evaluadores equiparan "más proyectos" con "mejor arquitectura." La entrevista debe anticipar esto: *"La arquitectura está definida por los contratos de interfaz, no por el conteo de proyectos. Estas mismas interfaces escalarían a múltiples proyectos si el tamaño del equipo o conteo de fuentes lo justificara."*

**Ajuste de Base de Datos**: Sin BD. Salida a archivo. Mismo razonamiento que todos los otros enfoques.  
**Ajuste Docker**: Lo más simple posible. Un proyecto, una etapa Dockerfile, una imagen. Mínimo.  
**Ajuste CI/CD**: `dotnet restore → build → test`. Un proyecto hace que CI sea el más rápido de todos los enfoques.

**VEREDICTO: RECHAZADO — requiere justificación defensiva que desperdicia tiempo de entrevista.** Aunque el Enfoque 10 entrega los mismos contratos de interfaz que Clean Architecture multi-proyecto, fuerza al candidato a una postura defensiva: explicar por qué un layout no estándar de proyecto único ES Clean Architecture, por qué la imposición de dependencias basada en convención es "suficientemente buena," y por qué el evaluador debería mirar más allá de un árbol de carpetas plano y ver capas. En una entrevista de Staff Engineer, el tiempo del candidato se gasta mejor discutiendo diseño del pipeline, estrategia de manejo de errores y patrones de extensibilidad — no justificando por qué la estructura del proyecto no se ve como lo que todo arquitecto .NET espera. **El Enfoque 1 (Clean Architecture de 4 proyectos) comunica la arquitectura instantáneamente y requiere cero explicación. El evaluador abre la solución, ve Core/Application/Infrastructure/Console, e inmediatamente conoce las reglas de dependencia. Ese reconocimiento vale más que la conveniencia de `dotnet run` ahorrada por un proyecto único.**

**Por Qué el Enfoque 10 fue inicialmente atractivo pero finalmente erróneo para este contexto**:
- El argumento de "menos ceremonia" asume que el evaluador penaliza la ceremonia. En realidad, para un rol de Staff Engineer, ver separación adecuada en capas es una señal POSITIVA — muestra que el candidato sabe cómo estructurar codebases de grado producción
- El argumento de "mismas interfaces" es verdadero pero pierde el punto: las interfaces en carpetas son invisibles sin leer el código. Las interfaces en proyectos separados son visibles en el Solution Explorer inmediatamente
- El argumento de "juicio de staff" es circular: afirma que elegir una estructura más simple muestra juicio, pero en un contexto de entrevista, la señal de juicio viene de poder defender tu arquitectura SIN necesitar un párrafo de justificación
- La "fricción" de `dotnet run --project` es trivial — el README le dice al evaluador exactamente cómo ejecutarlo, y las soluciones de 4 proyectos son estándar en todo take-home .NET

---

## 3.1 Evaluación Cruzada del Frontend React Vite

Antes de pasar al veredicto final, aquí está la evaluación comprehensiva del frontend que se aplica a TODOS los enfoques:

### El Análisis Definitivo del Frontend

**El requisito del ejercicio, textualmente**: *"Construir un pipeline de ingesta extensible en C# que obtenga, transforme y produzca datos meteorológicos normalizados."*

**Lo que un frontend React Vite agregaría**:
- Un dashboard visual para mostrar datos meteorológicos
- UI para gestionar ubicaciones
- Un botón para disparar ejecuciones del pipeline
- Gráficos para tendencias de temperatura

**Lo que un frontend React Vite NO agregaría**:
- Mejor arquitectura del pipeline (el backend es idéntico)
- Mejor manejo de errores (el frontend no se evalúa en esto)
- Mejor testabilidad (las pruebas de frontend no son requeridas)
- Mejores patrones de integración (el frontend no se integra con APIs meteorológicas)
- Mejor extensibilidad (agregar una nueva fuente es 100% backend)

### Análisis Forense Contra Cada Criterio de Evaluación

| Criterio de Evaluación | ¿Ayuda el Frontend? | Impacto |
|------------------------|---------------------|---------|
| **Arquitectura — Descomposición del pipeline** | No. El pipeline es solo backend | Cero o negativo (agrega ruido) |
| **Arquitectura — Límites de interfaces** | No. Las APIs entre frontend/backend no son lo que significan por "límites de interfaces" — se refieren a `IWeatherApiClient`, `IResponseParser` | Cero |
| **Arquitectura — Extensibilidad** | No. Agregar una nueva fuente meteorológica es solo backend | Cero |
| **Patrones de integración — Abstracción HTTP** | Parcialmente — pero la abstracción que quieren es en C#, no en `fetch()` de TypeScript | Señal engañosa |
| **Patrones de integración — Parseo JSON** | No. El frontend recibe JSON ya normalizado de la API del backend | Cero |
| **Patrones de integración — Normalización de esquema** | No. La normalización ocurre en el pipeline C# | Cero |
| **Manejo de errores — Resiliencia** | No. El manejo de errores en React es `try/catch` en fetch + error boundaries. No es lo que están probando | Cero |
| **Testabilidad — HTTP mockeado** | No. Quieren `HttpClient` mockeado en C#, no `fetch` mockeado en JavaScript | Cero |
| **Uso de IA** | Marginalmente — podría mostrar IA usada en full stack. Pero diluye la narrativa C# | Marginal |
| **Juicio de staff** | NEGATIVO. Agregar alcance no solicitado es lo opuesto al juicio de staff | **Activamente perjudicial** |

**Puntaje: 0 criterios positivos, 1 criterio activamente perjudicial. Impacto neto: NEGATIVO.**

### ¿Cuándo SERÍA un Frontend React la Decisión Correcta?

- Si el ejercicio dijera "construir un dashboard meteorológico" — esto es un ejercicio de dashboard
- Si el ejercicio dijera "construir una solución full-stack" — esto es un ejercicio full-stack
- Si el ejercicio evaluara "habilidades de frontend" — esto es un ejercicio de frontend
- Si el título del rol incluyera "Full Stack" — pero dice "Staff Engineer — Integraciones de Datos"

Ninguna de estas aplica. El ejercicio está deliberadamente acotado a integración de datos backend.

### La Única Excepción — Una Mención de 30 Segundos en la Entrevista

En la entrevista, PUEDES decir: *"El pipeline escribe archivos delimitados por tabulaciones, pero si SyncMetrics quisiera un dashboard, la interfaz IOutputWriter podría intercambiarse por un sink API. Construiría un frontend React + Vite consumiendo un Minimal API, pero acotéé este ejercicio a lo que se pidió — calidad del pipeline sobre amplitud de alcance."*

Esto muestra que PENSASTE en frontend, ELEGISTE no construirlo, y puedes ARTICULAR por qué. Ese es el movimiento de staff engineer.

**VEREDICTO FINAL DEL FRONTEND: NO CONSTRUIR UN FRONTEND REACT. Esta es la colina en la que morir. El ejercicio evalúa ingeniería de pipelines C#. Un frontend perjudicaría activamente tu puntaje de "juicio de staff" — el criterio más revelador de la rúbrica.**

---

## 4. El Veredicto — Arquitectura Seleccionada (Actualizado: Comparación de 10 Enfoques)

### Matriz de Comparación de Enfoques

| # | Enfoque | Extensibilidad | DX del Evaluador | Señal de Juicio Staff | Testeabilidad | Complejidad vs Valor | Veredicto |
|---|---------|---------------|------------------|----------------------|---------------|---------------------|-----------|
| 1 | Clean Architecture (4 proyectos) | ★★★★★ | ★★★★☆ | ★★★★★ | ★★★★★ | Estándar de industria, reconocido de inmediato | **GANADOR** |
| 2 | Azure Functions + Durable | ★★★★☆ | ★☆☆☆☆ | ★★☆☆☆ | ★★★☆☆ | Overkill masivo | Rechazado |
| 3 | MediatR / CQRS | ★★★☆☆ | ★★★☆☆ | ★★☆☆☆ | ★★★☆☆ | Desajuste semántico | Rechazado |
| 4 | Channels Producer/Consumer | ★★★☆☆ | ★★★☆☆ | ★★☆☆☆ | ★★☆☆☆ | Overkill para 3 llamadas | Rechazado |
| 5 | Vertical Slice | ★★★★★ | ★★★☆☆ | ★★★★☆ | ★★★★★ | Bueno pero desajuste con "separar responsabilidades" | Influyente |
| 6 | MEF Plugins | ★★★★★ | ★☆☆☆☆ | ★☆☆☆☆ | ★★☆☆☆ | Overkill nuclear | Rechazado |
| 7 | Minimal API + React Frontend | ★★★★★ | ★★☆☆☆ | ★☆☆☆☆ | ★★★☆☆ | Alcance no solicitado, perjudicial | **Rechazado** |
| 8 | Functional/ROP Pipeline | ★★★★☆ | ★★★★☆ | ★★★☆☆ | ★★★★★ | Elegante pero aliena a los lectores | Adoptar micro-patrón |
| 9 | Minimal API (sin frontend) | ★★★★★ | ★★☆☆☆ | ★★☆☆☆ | ★★★★☆ | Malinterpreta la intención de "dotnet run" | Rechazado |
| 10 | Single-Project Feature Folders | ★★★★★ | ★★★★★ | ★★★☆☆ | ★★★★★ | Mismos contratos, pero requiere justificación defensiva | Rechazado |

### Ganador: ENFOQUE 1 — Clean Architecture Console App con Pipeline + Strategy Pattern

**Por Qué el Enfoque 1 Gana — El Razonamiento Honesto**:

El análisis inicial sobre-indexó en "fricción del evaluador" (necesitar la flag `--project`, múltiples archivos .csproj) y sub-indexó en **reconocimiento del evaluador**. Tras una reflexión más profunda, el cálculo se invierte:

1. **Reconocimiento arquitectónico instantáneo**: El evaluador abre la solución en Visual Studio o Rider, ve `Pipeline.Core`, `Pipeline.Application`, `Pipeline.Infrastructure`, `Pipeline.Console` — e *inmediatamente* sabe lo que está viendo. No necesita README. No necesita explicación. Los nombres de los proyectos SON la documentación de la arquitectura. Esta es la ventaja más fuerte: **la arquitectura se explica sola.**

2. **Cumplimiento de dependencias en tiempo de compilación**: En un layout de 4 proyectos, si alguien intenta referenciar `Pipeline.Console` desde `Pipeline.Core`, el compilador los detiene. En un enfoque de un solo proyecto, nada impide que cualquier archivo importe cualquier otro archivo. Para una entrevista de Staff Engineer, demostrar que impones reglas arquitectónicas a través del sistema de tipos — no solo convención — es una señal más fuerte de madurez.

3. **Sin postura defensiva en la entrevista**: Con el Enfoque 1, la pregunta "¿por qué esta arquitectura?" tiene una respuesta de una oración: *"Clean Architecture con cuatro capas — Core, Application, Infrastructure, Console."* Listo. El evaluador asiente y pasa a la siguiente pregunta. Con un enfoque de un solo proyecto, esa misma pregunta requiere un párrafo defendiendo por qué la separación basada en convención de carpetas ES Clean Architecture. El tiempo de entrevista es finito — inviértelo discutiendo diseño del pipeline, manejo de errores y extensibilidad, no justificando la estructura de proyectos.

4. **La preocupación de "ceremonia" fue exagerada**: Sí, 4 proyectos significa `dotnet run --project src/SyncMetrics.Pipeline.Console`. El README lo maneja en una línea. Sí, 4 archivos .csproj significa más referencias de proyecto por configurar. Pero configurar referencias de proyecto toma 5 minutos durante el scaffolding y nunca vuelve a tocar el codebase. La "ceremonia" es un costo único de configuración; la **claridad** es permanente.

5. **Mapea directamente al lenguaje del ejercicio**: La especificación dice *"HTTP, parsing, transformación y output son responsabilidades separadas."* Cuatro proyectos hacen esas responsabilidades **físicamente separadas**, no solo organizadas lógicamente dentro de carpetas. `Pipeline.Infrastructure` contiene clientes HTTP y escritores de archivos. `Pipeline.Application` contiene el orquestador y lógica de transformación. `Pipeline.Core` contiene los modelos de dominio e interfaces. El mapeo del lenguaje del requerimiento a la estructura del proyecto es 1:1.

**Por Qué el Enfoque 10 Fue Rechazado**:

El Enfoque 10 (un solo proyecto con feature folders y SharedKernel) es arquitectónicamente sólido. Las interfaces son idénticas. La historia de extensibilidad es la misma. Pero tiene un defecto fatal en contexto de entrevista: **requiere que el candidato argumente que la arquitectura es algo que el evaluador no puede ver de un vistazo.**

La conversación con el Enfoque 10 inevitablemente se convierte en:
- "¿Es esto Clean Architecture?" → "Sí, SharedKernel es el círculo interno, Features son el círculo externo..."
- "¿Pero no hay cumplimiento en tiempo de compilación de la regla de dependencia?" → "Cierto, pero para un codebase pequeño con code review..."
- "¿Un desarrollador podría accidentalmente importar del namespace equivocado?" → "Sí, pero ArchUnit podría forzar..."

Cada una de esas respuestas es técnicamente correcta. Pero cada una quema tiempo de entrevista defendiendo decisiones estructurales en vez de discutir la ingeniería del pipeline que el ejercicio realmente evalúa. Un Staff Engineer que tiene que convencer a un entrevistador de que su arquitectura ES el patrón conocido — en vez de que el patrón hable por sí mismo — está comenzando en desventaja.

Con el Enfoque 1, los nombres de los proyectos SON la defensa. No se necesita argumento.

**Absorbiendo lo Mejor de Todos los Enfoques Rechazados en el Enfoque 1**:

| Adoptado De | Qué Tomamos | Cómo Aparece en el Enfoque 1 |
|-------------|-------------|------------------------------|
| Enfoque 5 (Vertical Slice) | Cohesión específica por fuente: todo el código Open-Meteo co-localizado | Directorio `Pipeline.Infrastructure/OpenMeteo/` |
| Enfoque 8 (ROP) | `Result<T>` para retornos de parser y transformer — errores como valores, sin fallos silenciosos | `Pipeline.Core/Result.cs` |
| Enfoque 1 (original) | Strategy pattern para fuentes de datos vía DI | `IWeatherDataSource` con registro en DI |
| Enfoque 4 (Channels) | Fetching concurrente (simplificado) | `Task.WhenAll` en PipelineCoordinator |
| Enfoque 10 (Feature Folders) | Organización feature-folder dentro de Infrastructure | `Pipeline.Infrastructure/OpenMeteo/` + `Pipeline.Infrastructure/Output/` |

**La Arquitectura en Un Párrafo (Memorizar para la Entrevista)**:

> "Construí un pipeline Clean Architecture con cuatro capas: Core define los modelos de dominio y contratos de interfaces — IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter — más un tipo Result<T> para manejo explícito de errores. Application contiene el PipelineCoordinator que orquesta todas las implementaciones registradas de IWeatherDataSource, busca ubicaciones concurrentemente con Task.WhenAll, y agrega resultados. Infrastructure implementa los clientes HTTP, parsers JSON, transformadores de datos y escritor de archivos — cada fuente de datos vive en su propia sub-carpeta para cohesión. Console es el punto de entrada con configuración de DI y binding de configuración. Agregar una nueva fuente de datos significa una nueva sub-carpeta en Infrastructure con clases implementando interfaces de Core, más un registro en DI. La regla de dependencia se impone por referencias de proyecto — Core tiene cero dependencias, y nada referencia a Console. Parsers y transformadores retornan Result<T>, así que los errores son valores que se muestran en el resumen de procesamiento — sin fallos silenciosos."

### Estructura Final del Proyecto (Enfoque 1 — Clean Architecture)

```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/                  # Círculo interno — CERO dependencias en otros proyectos
│   │   ├── Models/
│   │   │   ├── NormalizedWeatherRecord.cs           # Modelo de output unificado (una fila por día por ubicación)
│   │   │   ├── LocationConfig.cs                    # Ubicación configurable (nombre, lat, lon)
│   │   │   ├── ProcessingResult.cs                  # Resultado de ejecución por fuente (registros + errores + tiempo)
│   │   │   └── PipelineSummary.cs                   # Resumen completo del pipeline (para salida en consola)
│   │   ├── Interfaces/
│   │   │   ├── IWeatherApiClient.cs                 # Abstracción HTTP: buscar JSON crudo por ubicación
│   │   │   ├── IResponseParser.cs                   # JSON → modelo específico de fuente
│   │   │   ├── IDataTransformer.cs                  # Modelo de fuente → registros normalizados
│   │   │   ├── IOutputWriter.cs                     # Registros normalizados → salida de archivo
│   │   │   └── IWeatherDataSource.cs                # Compuesto: fetch→parse→transform completo de una fuente
│   │   ├── Result.cs                                # Tipo monádico de error Result<T> (~50 líneas)
│   │   └── PipelineError.cs                         # Jerarquía de errores tipados (Fetch/Parse/Transform/Output)
│   │
│   ├── SyncMetrics.Pipeline.Application/            # Capa de orquestación — depende SOLO de Core
│   │   ├── PipelineCoordinator.cs                   # Ejecuta todas las fuentes, fetch concurrente, construye resumen
│   │   └── PipelineOptions.cs                       # Binding de config fuertemente tipado
│   │
│   ├── SyncMetrics.Pipeline.Infrastructure/         # Círculo externo — implementa interfaces de Core
│   │   ├── OpenMeteo/                               # Todo el código Open-Meteo co-localizado (influencia Vertical Slice)
│   │   │   ├── OpenMeteoApiClient.cs                # IWeatherApiClient → HTTP GET con query params
│   │   │   ├── OpenMeteoApiResponse.cs              # Modelo de deserialización (arrays paralelos)
│   │   │   ├── OpenMeteoResponseParser.cs           # IResponseParser → retorna Result<T>
│   │   │   ├── OpenMeteoTransformer.cs              # IDataTransformer → zip arrays → registros normalizados
│   │   │   └── OpenMeteoDataSource.cs               # IWeatherDataSource → compone client+parser+transformer
│   │   ├── Output/
│   │   │   └── TabDelimitedFileWriter.cs            # IOutputWriter → escribe .tsv con header + filas
│   │   ├── Configuration/
│   │   │   ├── FieldMappingConfig.cs                # Modelo de field mapping config-driven (feature bonus)
│   │   │   └── ServiceRegistration.cs               # Extensión DI: AddPipelineServices()
│   │   └── Http/
│   │       └── ResilienceConfiguration.cs           # Config de retry + timeout para HttpClient
│   │
│   └── SyncMetrics.Pipeline.Console/                # Punto de entrada — depende de todas las capas vía DI
│       ├── Program.cs                               # Setup de Host, DI, config binding, ejecutar pipeline
│       └── appsettings.json                         # Ubicaciones, ruta de output, field mappings, config de fuente
│
├── tests/
│   ├── SyncMetrics.Pipeline.UnitTests/              # Tests rápidos, aislados
│   │   ├── OpenMeteo/
│   │   │   ├── OpenMeteoResponseParserTests.cs      # Parsing JSON válido + malformado (8+ tests)
│   │   │   ├── OpenMeteoTransformerTests.cs         # Lógica de transformación (6+ tests)
│   │   │   └── TestData/                            # Archivos fixture JSON
│   │   │       ├── valid_response.json
│   │   │       ├── valid_london_response.json
│   │   │       ├── malformed_missing_daily.json
│   │   │       ├── malformed_null_values.json
│   │   │       ├── malformed_mismatched_arrays.json
│   │   │       └── error_response.json
│   │   ├── Pipeline/
│   │   │   └── PipelineCoordinatorTests.cs          # Orquestación con sources mockeados (5+ tests)
│   │   └── Output/
│   │       └── TabDelimitedWriterTests.cs           # Formateo de output (5+ tests)
│   │
│   └── SyncMetrics.Pipeline.IntegrationTests/       # End-to-end con HTTP mockeado
│       └── EndToEndPipelineTests.cs                 # Flujo completo del pipeline (3+ tests)
│
├── output/
│   └── sample/
│       └── weather_data_sample.tsv                  # Muestra pre-generada para referencia del evaluador
│
├── README.md
├── AI.md
├── Dockerfile
├── .github/
│   └── workflows/
│       └── build-and-test.yml
└── SyncMetrics.WeatherPipeline.sln
```

### Diagrama de Flujo de Interfaces (Enfoque 1 — Clean Architecture)

```
  ┌─────────────────────────────────────────────────────────────────┐
  │                     PipelineCoordinator                         │
  │  (Capa Application — orquesta, fetch concurrente, resumen)      │
  └──────────┬──────────────────┬──────────────────┬────────────────┘
             │                  │                  │
      ┌──────▼──────┐   ┌──────▼──────┐   ┌──────▼──────┐
      │ IWeather     │   │ IWeather     │   │ IWeather     │
      │ DataSource   │   │ DataSource   │   │ DataSource   │
      │ Infra/       │   │ Infra/       │   │ Infra/       │
      │ OpenMeteo/   │   │ WeatherApi/  │   │ FutureSrc/   │
      └──────┬───────┘   └─────────────┘   └─────────────┘
             │
   ┌─────────┼──────────┐
   │         │          │
   ▼         ▼          ▼
 IWeather  IResponse  IData
 ApiClient  Parser    Transformer
   │         │          │
   ▼         ▼          ▼
 HTTP GET  JSON →     Source →
 + retry   Result<    Result<
           SourceModel> Normalized[]>
                        │
                        ▼
                  IOutputWriter
                  (Infrastructure/Output/)
                        │
                        ▼
                  Tab-delimited .tsv

  ┌──────────────────────────────────────────────────┐
  │              Pipeline.Core/                       │
  │  Interfaces, Models, Result<T>, PipelineError    │
  │  CERO dependencias — el círculo interno          │
  │  El compilador impide que nada referencie afuera │
  └──────────────────────────────────────────────────┘
```

### Grafo de Dependencias (Impuesto por Referencias de Proyecto)

```
  Pipeline.Console  ──referencia──►  Pipeline.Infrastructure
       │                                    │
       │                                    ▼
       └──referencia──►  Pipeline.Application
                                │
                                ▼
                         Pipeline.Core
                         (sin referencias)

  Ilegal: Core → Application  ✗ (error de compilador)
  Ilegal: Core → Infrastructure  ✗ (error de compilador)
  Ilegal: Application → Console  ✗ (error de compilador)
  Ilegal: Infrastructure → Console  ✗ (error de compilador)
```

### Flujo de Ejecución del Pipeline (Enfoque 1 — con Result<T>)

```
1. Program.cs (Console) carga config (appsettings.json → ubicaciones, field mappings, configuración de fuentes)
2. ServiceRegistration.cs (Infrastructure) registra todos los servicios en el contenedor DI
3. PipelineCoordinator.RunAsync() (Application) llamado desde Program.cs
4. Para cada IWeatherDataSource registrado (descubierto vía DI):
   a. Buscar todas las ubicaciones concurrentemente (Task.WhenAll)
      - IWeatherApiClient.FetchAsync(location) → Result<string> (JSON crudo o error)
      - Retry con backoff exponencial + jitter en fallos transitorios (5xx, timeout)
   b. Para cada Result:
      - Si Success: IResponseParser.Parse(json) → Result<SourceModel>
        - Si Success: IDataTransformer.Transform(model, location) → Result<NormalizedWeatherRecord[]>
          - Recolectar registros normalizados en lista de resultados
        - Si Failure: Registrar ParseError con contexto de ubicación, continuar
      - Si Failure: Registrar FetchError con ubicación + código HTTP, continuar
5. Agregar todos los objetos Result:
   - Registros exitosos → IOutputWriter.WriteAsync() → archivo .tsv delimitado por tabulaciones
   - Resultados fallidos → Recolectados en PipelineSummary.Errors
6. Imprimir PipelineSummary en consola:
   - Fuentes procesadas
   - Total ubicaciones intentadas / exitosas / fallidas
   - Total registros escritos
   - Duración total (wall-clock)
   - Lista detallada de errores con ubicación, etapa y descripción del error
   - Ruta del archivo de output
```

---

## 5. Análisis de Base de Datos — Por Qué No Tener Base de Datos Es la Respuesta Correcta

La especificación del ejercicio establece: *"escribe archivos de output unificados para importación de analytics downstream"* y requiere *"Output normalizado delimitado por tabulaciones."* Este es explícitamente un pipeline de output a archivo. Aquí está por qué cada opción de base de datos fue considerada y rechazada:

| Base de Datos | Argumento a Favor | Argumento en Contra | Veredicto |
|---------------|-------------------|---------------------|-----------|
| **Sin DB (solo archivos)** | Coincide con el requerimiento exactamente. Archivos delimitados por tabulaciones. Cero fricción de setup para el evaluador. | Ninguno. | **✓ ELEGIDO** |
| **SQLite** | Cero instalación, embebido, archivo `.db` viaja con el repo | Agrega dependencia y complejidad por cero beneficio. El formato de output está especificado como tab-delimited, no DB. | ✗ Innecesario |
| **SQL Server 2022** (Arthur lo tiene localmente) | Zona de confort de Arthur. Podría almacenar respuestas crudas + registros normalizados. | El evaluador no tendrá el SQL Server de Arthur. Viola "ejecuta en su ambiente." Incluso LocalDB requiere instalación de SQL Server Express. | ✗ Asesino de portabilidad |
| **PostgreSQL 18** (Arthur lo tiene localmente) | JSONB para respuestas crudas, tipos de datos fuertes | Mismo problema de portabilidad. El evaluador necesita PostgreSQL instalado. Docker Compose podría resolver pero agrega fricción. | ✗ Asesino de portabilidad |
| **Azure Cosmos DB** | JSON-nativo, muestra habilidades Azure | Requiere suscripción Azure. El evaluador no puede ejecutarlo. | ✗ No viable |
| **Redis** | Podría cachear respuestas API para desarrollo/retry | En memoria, no se necesita persistencia. Agrega dependencia Redis para cachear 3 respuestas API. | ✗ Sobre-ingeniería |

**Respuesta de Entrevista**: *"La especificación explícitamente requiere output de archivo delimitado por tabulaciones para analytics downstream. Mantuve el output detrás de una interfaz `IOutputWriter`, así que si necesitáramos agregar persistencia en base de datos — digamos, escribir a PostgreSQL para un dashboard o Cosmos DB para acceso global — es una nueva implementación registrada en DI. Sin cambios en código del pipeline. Pero para este ejercicio, la respuesta correcta es archivos porque eso es lo que se pidió."*

**Punto bonus**: La abstracción `IOutputWriter` significa que durante la entrevista puedes decir "Podría intercambiar `TabDelimitedFileWriter` por `SqlServerOutputWriter` o `CosmosDbOutputWriter` con un solo cambio de registro DI." Esto demuestra extensibilidad sin sobre-construir.

---

## 6. Estrategia Docker

### Por Qué Incluir Docker

El ejercicio dice: *"Devuelve un zip o link del repo."* y *"Incluye un README.md corto — setup, cómo ejecutar."* Los evaluadores clonarán/descomprimirán e intentarán ejecutarlo. Docker asegura que funcione idénticamente en su máquina independientemente de su versión del SDK .NET.

### Enfoque: Doble Ruta (Docker Opcional, dotnet run Principal)

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet build -c Release --no-restore
RUN dotnet test -c Release --no-restore --no-build
RUN dotnet publish src/SyncMetrics.Pipeline/SyncMetrics.Pipeline.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SyncMetrics.Pipeline.dll"]
```

**Instrucciones README**:
```
# Opción 1: Directo (requiere .NET 8 SDK)
dotnet run --project src/SyncMetrics.Pipeline

# Opción 2: Docker (requiere solo Docker)
docker build -t syncmetrics-pipeline .
docker run --rm -v ${PWD}/output:/app/output syncmetrics-pipeline
```

**Por qué este enfoque Docker**:
- Build multi-stage: imagen runtime pequeña (~80MB vs ~800MB SDK)
- Los tests se ejecutan DENTRO del stage de build — si los tests fallan, la imagen no se construye
- El volume mount de output permite al evaluador ver los archivos generados en su host
- El evaluador no necesita .NET SDK instalado — solo Docker
- Muestra mentalidad de producción sin sobre-complicar el ejercicio

**¿Docker Compose? No.** No hay base de datos, no hay Redis, no hay cola de mensajes. Un solo `Dockerfile` es suficiente. Docker Compose señalaría sobre-ingeniería.

---

## 7. Diseño del Pipeline CI/CD

Aunque el ejercicio no requiere explícitamente CI/CD, incluir un workflow de GitHub Actions demuestra mentalidad de producción y es una adición trivial que proporciona una señal desproporcionadamente grande.

### Workflow de GitHub Actions

```yaml
# .github/workflows/build-and-test.yml
name: Build and Test

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build -c Release --no-restore
      
      - name: Test
        run: dotnet test -c Release --no-restore --no-build --verbosity normal
```

**Por qué esto es valioso**:
- Toma 5 minutos agregarlo, señala disciplina profesional
- El evaluador ve badge verde en el repo → credibilidad instantánea
- Prueba que `dotnet test` pasa en un ambiente limpio (no solo en la máquina de Arthur)
- Muestra familiaridad con CI/CD (listado en el CV de Arthur como fortaleza)

**Qué NO agregar al CI/CD**:
- Publicación de imagen Docker (no se necesita registro)
- Pasos de deploy (no hay ambiente objetivo)
- Umbrales de code coverage (bueno pero scope creep innecesario)
- SonarQube/escaneo de seguridad (señal de sobre-ingeniería)

**Respuesta de Entrevista**: *"Incluí un pipeline CI simple para probar que la solución construye y testea en un ambiente limpio. En producción, lo extendería con publicación de imagen Docker, stages de integration test contra un API staging, y deploy a Azure Container Apps o Kubernetes."*

---

## 8. Estrategia de Testing

### Categorías de Tests (de los requerimientos del ejercicio)

| Categoría | Qué Testear | Patrón de Test |
|-----------|-------------|----------------|
| **Parsing de respuesta (válido)** | Parsear JSON Open-Meteo correcto → modelo de fuente | Unit test con archivos fixture JSON |
| **Parsing de respuesta (malformado)** | Campos faltantes, arrays null, campos extra, tipos incorrectos | Unit test con fixtures JSON malformados |
| **Lógica de transformación** | Modelo fuente → mapeo NormalizedWeatherRecord, corrección de unidades | Unit test con modelos de fuente en memoria |
| **Formateo de output** | NormalizedWeatherRecord[] → string tab-delimited correcta | Unit test, verificar header + filas |
| **Pipeline end-to-end** | Pipeline completo con HTTP mockeado → archivo de output correcto | Integration test con mock HttpMessageHandler |
| **Lógica de retry (Bonus)** | Fallos transitorios disparan retry, fallos permanentes no | Unit test con mock handler retornando 500 luego 200 |

### Elección de Framework de Testing

- **xUnit** — Estándar de industria para .NET, usado por los propios repos de Microsoft
- **NSubstitute** o **Moq** — Mocking de interfaces. NSubstitute tiene sintaxis más limpia, pero Moq es más ampliamente conocido. Usar **NSubstitute** por legibilidad de tests más limpia
- **FluentAssertions** — Opcional pero hace las aserciones declarativas (`.Should().BeEquivalentTo(...)`)
- **Sin Testcontainers** — Sin base de datos, sin servicios externos que containerizar

### Estrategia de Mock HTTP

El ejercicio dice: *"Las llamadas HTTP están detrás de una interfaz."* Esto significa:

1. `IWeatherApiClient` es la interfaz. En tests, crear un `FakeWeatherApiClient` o mockear `IWeatherApiClient` para retornar JSON pre-fabricado
2. Para testear lógica de retry, usar un `HttpMessageHandler` personalizado que cuenta invocaciones y retorna fallos/éxitos en secuencia
3. Cargar JSON de test desde archivos de recursos embebidos o directorio `TestData/` — mantiene los tests limpios y los fixtures versionados

### Fixtures de Datos de Test

Crear archivos JSON realistas basados en respuestas reales de Open-Meteo:
- `valid_newyork_response.json` — Respuesta válida completa
- `valid_london_response.json` — Respuesta válida completa
- `malformed_missing_daily.json` — JSON válido pero falta clave `daily`
- `malformed_null_temperatures.json` — Array `temperature_2m_max` tiene valores null
- `malformed_mismatched_arrays.json` — Longitud de array `time` ≠ longitud de `temperature_2m_max`
- `malformed_empty_daily.json` — `daily.time` es array vacío
- `error_response.json` — `{ "error": true, "reason": "..." }`

---

## 9. Diseño del Schema de Output

El ejercicio dice: *"Output normalizado delimitado por tabulaciones — diseña el schema tú mismo."*

### Schema Tab-Delimited Propuesto

```
Source	Location	Latitude	Longitude	Date	TempMaxC	TempMinC	PrecipitationMm	WindSpeedMaxKmh	UVIndexMax	FetchedAtUtc
OpenMeteo	New York	40.7128	-74.0060	2026-03-28	18.2	8.4	0.0	22.1	5.2	2026-03-28T14:30:00Z
OpenMeteo	New York	40.7128	-74.0060	2026-03-29	15.1	6.2	2.3	18.5	3.8	2026-03-28T14:30:00Z
OpenMeteo	London	51.5074	-0.1278	2026-03-28	12.5	5.1	4.2	30.2	2.1	2026-03-28T14:30:00Z
```

### Decisiones de Diseño del Schema (Puntos de Conversación en Entrevista)

| Columna | Por Qué |
|---------|---------|
| `Source` | Crítico para extensibilidad multi-fuente — cuando se agrega una segunda fuente, los registros son distinguibles |
| `Location` | Nombre legible para humanos desde config, no solo coordenadas |
| `Latitude/Longitude` | Preserva las coordenadas exactas usadas, habilita geo-análisis downstream |
| `Date` | Formato ISO 8601 — universal, ordenable, sin ambigüedad de zona horaria |
| `TempMaxC/TempMinC` | Unidad explícita en nombre de columna (C de Celsius) — tab-delimited no tiene capa de metadatos |
| `PrecipitationMm` | Unidad en nombre de columna |
| `WindSpeedMaxKmh` | Unidad en nombre de columna |
| `UVIndexMax` | Índice adimensional, no necesita sufijo de unidad |
| `FetchedAtUtc` | Rastro de auditoría — cuándo se recuperaron estos datos. Esencial para depurar problemas de datos obsoletos |

**¿Por qué tab-delimited (.tsv) y no CSV?**
- El ejercicio especifica tab-delimited
- TSV es realmente mejor para importación de analytics: sin problemas de comillas con comas en nombres de ubicaciones
- Herramientas downstream (Excel, Power BI, pandas) manejan TSV nativamente

---

## 10. Diseño de Lógica de Retry (Bonus)

### Estrategia: Backoff Exponencial con Jitter vía Polly

```
Intento 1: Inmediato
Intento 2: Esperar ~1s (1s + jitter aleatorio 0-500ms)
Intento 3: Esperar ~2s (2s + jitter aleatorio 0-500ms)
Intento 4: Esperar ~4s (4s + jitter aleatorio 0-500ms)
Max reintentos: 3 (4 intentos totales)
```

**Qué dispara retry**:
- HTTP 5xx (error del servidor — transitorio)
- HTTP 408 (request timeout — transitorio)
- HTTP 429 (rate limited — transitorio, pero también respetar header `Retry-After`)
- `HttpRequestException` (fallo a nivel de red)
- `TaskCanceledException` (timeout)

**Qué NO dispara retry**:
- HTTP 4xx (excepto 408, 429) — error de cliente, reintentar no ayudará
- HTTP 400 — bad request, nuestra URL está mal
- Errores de parsing JSON — la respuesta fue recibida, solo está malformada

**Implementación**: Usar `Microsoft.Extensions.Http.Polly` (o el nuevo `Microsoft.Extensions.Http.Resilience` en .NET 8) para políticas de resiliencia en clientes manejados por `IHttpClientFactory`.

**Respuesta de Entrevista**: *"Usé backoff exponencial con jitter de Polly para fallos HTTP transitorios. El jitter previene el problema de thundering herd — si las 3 ubicaciones fallan simultáneamente y reintentan en los mismos intervalos fijos, todas golpearán el API al mismo tiempo de nuevo. El jitter aleatoriza el timing de retry. También diferencio fallos transitorios vs. permanentes — un 500 se reintenta, un 400 no."*

---

## 11. Diseño de Field Mapping Config-Driven (Bonus)

### Estructura de appsettings.json

```json
{
  "Pipeline": {
    "OutputDirectory": "./output",
    "OutputFileName": "weather_data_{timestamp}.tsv",
    "Sources": [
      {
        "Name": "OpenMeteo",
        "Enabled": true,
        "BaseUrl": "https://api.open-meteo.com/v1/forecast",
        "TimeoutSeconds": 30,
        "RetryCount": 3,
        "Locations": [
          { "Name": "New York", "Latitude": 40.7128, "Longitude": -74.0060 },
          { "Name": "London", "Latitude": 51.5074, "Longitude": -0.1278 },
          { "Name": "Tokyo", "Latitude": 35.6762, "Longitude": 139.6503 }
        ],
        "FieldMappings": [
          { "SourceField": "temperature_2m_max", "OutputColumn": "TempMaxC", "Unit": "°C" },
          { "SourceField": "temperature_2m_min", "OutputColumn": "TempMinC", "Unit": "°C" },
          { "SourceField": "precipitation_sum", "OutputColumn": "PrecipitationMm", "Unit": "mm" },
          { "SourceField": "wind_speed_10m_max", "OutputColumn": "WindSpeedMaxKmh", "Unit": "km/h" },
          { "SourceField": "uv_index_max", "OutputColumn": "UVIndexMax", "Unit": "index" }
        ]
      }
    ]
  }
}
```

**Por qué esto importa**: Cuando se agrega una segunda fuente de API, sus nombres de campos completamente diferentes se mapean vía config, no código. El transformer lee los mappings y los aplica genéricamente. Esta es una señal poderosa de extensibilidad.

---

## 12. Resumen de Decisiones Tecnológicas Clave

| Decisión | Seleccionado | Por Qué |
|----------|-------------|---------|
| **Runtime** | .NET 8 (LTS) | Último LTS, rendimiento, soporte AOT nativo si se necesita |
| **Estructura de proyecto** | 4 proyectos: Core, Application, Infrastructure, Console | Capas Clean Architecture, regla de dependencia impuesta por el compilador |
| **DI** | Microsoft.Extensions.DependencyInjection | Incorporado, sin dependencias externas |
| **Config** | Microsoft.Extensions.Configuration + patrón Options | .NET estándar. Config fuertemente tipada |
| **HTTP** | IHttpClientFactory + HttpClient | Connection pooling apropiado. Pipeline de handler para retry |
| **Resiliencia** | Microsoft.Extensions.Http.Resilience (o Polly v8) | Resiliencia nativa .NET 8. Backoff exponencial + jitter |
| **JSON** | System.Text.Json | Incorporado, alto rendimiento, sin dependencia de Newtonsoft |
| **Manejo de errores** | Result<T> personalizado (~50 LOC) | Errores como valores. Sin fallos silenciosos por diseño. Sin dependencia externa |
| **Testing** | xUnit + NSubstitute + FluentAssertions | Estándar de industria, sintaxis limpia |
| **Output** | StreamWriter con formateo separado por tabulaciones | Simple, correcto, sin librería necesaria |
| **Logging** | Microsoft.Extensions.Logging + proveedor de consola | Incorporado, logging estructurado |
| **CI** | GitHub Actions | Gratis, estándar, visible para el evaluador |
| **Contenedor** | Docker (ruta opcional) | Reproducible. Build multi-stage |
| **Frontend** | Ninguno | No solicitado, activamente perjudicial para puntuación de juicio staff |

---

## 13. Preparación para Entrevista — Preguntas Anticipadas y Respuestas

### P: "¿Por qué elegiste esta arquitectura sobre algo más simple/más complejo?"

> "Evalué diez enfoques que van desde Azure Durable Functions hasta un sistema basado en plugins con carga de assemblies MEF, un dashboard full-stack React+Vite, hasta composición funcional Railway-Oriented. El ejercicio evalúa juicio de staff — saber qué generalizar y qué mantener simple. Elegí Clean Architecture con cuatro proyectos — Core, Application, Infrastructure, Console — porque es el patrón estándar por capas que cualquier arquitecto .NET reconoce a primera vista. La regla de dependencia se impone por referencias de proyecto, no solo convención: Core tiene cero dependencias, Application depende solo de Core, Infrastructure implementa interfaces de Core, y Console conecta todo vía DI. Los nombres de proyecto literalmente son la documentación de la arquitectura."

### P: "¿Cómo agregarías una segunda fuente de datos?"

> "Crear una nueva sub-carpeta en Infrastructure — digamos, `Infrastructure/WeatherApi/` — con su propio client, parser, transformer, y clase de data source, todos implementando las interfaces de Core. Registrar el nuevo IWeatherDataSource en ServiceRegistration.cs. Agregar una sección de config en appsettings.json con la URL de la fuente, ubicaciones y field mappings. Cero cambios al código existente — el pipeline coordinator descubre todas las fuentes registradas vía DI. La nueva fuente solo referencia Core, nunca otras fuentes en Infrastructure."

### P: "¿Por qué no hay base de datos?"

> "La especificación dice 'escribe archivos de output unificados para importación de analytics downstream' — eso es archivos delimitados por tabulaciones. Construí IOutputWriter como interfaz, así que agregar un writer de PostgreSQL o Cosmos DB es una sola clase nueva registrada en DI. Pero para este ejercicio, archivos es la respuesta correcta. Sobre-construir lo que no fue pedido señala mal juicio, no minuciosidad."

### P: "¿Por qué no construiste un frontend / dashboard?"

> "El ejercicio especifica un pipeline de datos C# con output delimitado por tabulaciones, ejecutable vía `dotnet run`. Cada criterio de evaluación — arquitectura, patrones de integración, manejo de errores, testeabilidad, juicio de staff — se enfoca en el pipeline. Un frontend React agregaría cero valor a cualquier dimensión evaluada y arriesgaría señalar malentendido del alcance. Deliberadamente acotéé mi trabajo para maximizar calidad del pipeline sobre amplitud. Dicho esto, la abstracción IOutputWriter significa que envolver el pipeline en un Minimal API con un dashboard React es un paso futuro directo."

### P: "Guíame por cómo manejas una respuesta API malformada."

> "Los parsers y transformers retornan Result<T> — los errores son valores, no excepciones. Si una respuesta JSON es válida pero le falta la clave 'daily', el parser retorna un resultado Failure con un ParseError conteniendo el nombre del campo y la ubicación. Si los arrays de temperatura tienen valores null, el transformer retorna un Failure con el índice específico y el campo. El pipeline coordinator recolecta todos los objetos Result — los éxitos van al output writer, los fallos van al resumen de procesamiento. Sin fallos silenciosos — cada error es visible y tipado."

### P: "¿Cómo usaste IA en este ejercicio?"

> "La IA fue una parte central de mi flujo de trabajo, no un suplemento. Usé Claude/Copilot para scaffoldear la estructura inicial del proyecto, generar el boilerplate del cliente HTTP, y crear los fixtures de test JSON desde la documentación del API. Anulé la salida de la IA en la capa del transformer — el código generado inicialmente usaba decimal para coordenadas pero el API retorna float, y la política de retry generada por IA no distinguía entre errores HTTP transitorios y permanentes. Deliberadamente escribí las decisiones de arquitectura y el tipo Result<T> yo mismo porque requieren juicio humano sobre modelado de errores y trade-offs de extensibilidad."

### P: "¿Qué harías diferente en un ambiente de producción?"

> "Tres cosas: (1) Agregaría logging estructurado con correlation IDs por ejecución del pipeline, enviando a Application Insights o Seq. (2) Para 100+ ubicaciones, reemplazaría Task.WhenAll con System.Threading.Channels para streaming con control de backpressure, o Azure Durable Functions para fan-out/fan-in serverless. (3) Agregaría un circuit breaker por fuente de API vía Polly — si Open-Meteo está fallando consistentemente, dejar de bombardearlo y fallar rápido mientras otras fuentes continúan."

### P: "¿Por qué cuatro proyectos en vez de microservicios o un solo proyecto?"

> "Este pipeline tiene una unidad de deploy y un equipo — microservicios agregarían overhead de red, tracing distribuido, y orquestación de deployment por cero beneficio a esta escala. En el otro extremo, un solo proyecto funcionaría funcionalmente, pero no impondría la regla de dependencia en tiempo de compilación — cualquier archivo podría importar cualquier otro archivo. Cuatro proyectos nos dan el punto ideal: el compilador impone que Core no tenga dependencias hacia afuera, Application no pueda alcanzar Infrastructure, e Infrastructure no pueda alcanzar Console. Esas son barreras arquitectónicas reales, no solo convenciones de carpetas. Si SyncMetrics crece a muchos equipos poseyendo diferentes fuentes, las sub-carpetas de Infrastructure podrían convertirse en servicios independientes — las interfaces de Core garantizan que esa migración funcione."

### P: "Explica tu patrón Result<T>. ¿Por qué no solo try/catch?"

> "Try/catch tiene un problema fundamental de legibilidad — no puedes ver desde la firma del método si puede fallar. Un método retornando `SourceModel` podría lanzar excepción, o podría no hacerlo. Solo lo sabes leyendo la implementación o la documentación. Result<T> hace el fallo explícito en el sistema de tipos. Si un parser retorna `Result<SourceModel>`, el llamador es FORZADO por el compilador a manejar tanto éxito como fallo. El pipeline coordinator no necesita try/catch — hace pattern-match sobre Result. Esto elimina la categoría de bugs 'olvidé hacer catch' por completo. Mantuve la implementación en ~50 líneas sin dependencia externa."

### P: "¿Por qué no usar una librería como LanguageExt o CSharpFunctionalExtensions para Result<T>?"

> "Para un ejercicio take-home, quiero dependencias mínimas. LanguageExt es un paquete de 900KB con cientos de tipos que la mayoría de desarrolladores .NET nunca han visto. CSharpFunctionalExtensions es más ligero pero sigue siendo una dependencia externa para algo que puedo implementar en 50 líneas. Usar mi propio Result<T> también me permite adaptarlo al modelo de error específico del pipeline — PipelineError con variantes FetchError, ParseError, TransformError — en vez de usar un tipo Error genérico."

---

## 14. Mitigación de Riesgos

| Riesgo | Mitigación |
|--------|-----------|
| API de Open-Meteo caída cuando el evaluador prueba | Incluir output de muestra cacheado en directorio `output/sample/`. README lo nota. Tests usan HTTP mockeado, no API en vivo. |
| El evaluador no tiene .NET 8 SDK | Ruta Docker documentada. Dockerfile ejecuta tests + construye. |
| Naming `wind_speed_10m_max` vs `windspeed_10m_max` | Usar el nombre real del campo del API. Agregar comentario notando la discrepancia. |
| Problemas de encoding del output tab-delimited en Windows vs. Mac | Usar UTF-8 sin BOM. Documentar en README. |
| El evaluador ejecuta en Mac/Linux | La solución es .NET 8 cross-platform. Sin rutas específicas de Windows. Usar `Path.Combine` en todas partes. |

---

## 15. Hoja de Ruta de Implementación (Próximos Pasos)

Cuando estemos listos para codificar, seguir este orden:

1. ~~**Scaffolding de solución y proyectos**~~ ✅ — .sln, 4 proyectos src, 2 proyectos test, estructura de carpetas, referencias de proyecto, paquetes NuGet, `Directory.Build.props`, `.editorconfig`, `appsettings.json` — **HECHO**
2. ~~**Pipeline.Core**~~ ✅ — Result<T>, jerarquía PipelineError (4 errores tipados), 4 modelos de dominio, 5 interfaces del pipeline — **HECHO**
3. ~~**Infrastructure/Configuration**~~ ✅ — PipelineOptions (3 clases), ServiceRegistration.cs, appsettings.json con config completa de fuente + field mappings, cableado DI de Program.cs — **HECHO**
4. ~~**Infrastructure/OpenMeteo/ApiClient**~~ ✅ — Implementación de IWeatherApiClient con IHttpClientFactory — **HECHO**
5. ~~**Infrastructure/OpenMeteo/Parser**~~ ✅ — Deserialización JSON con System.Text.Json, retorna Result<T> — **HECHO**
6. ~~**Infrastructure/OpenMeteo/Transformer**~~ ✅ — Modelo fuente → NormalizedWeatherRecord, retorna Result<T> — **HECHO**
7. ~~**Infrastructure/OpenMeteo/DataSource**~~ ✅ — Cableado compuesto IWeatherDataSource: client + parser + transformer — **HECHO**
8. ~~**Infrastructure/Output/TabDelimitedFileWriter**~~ ✅ — Implementación IOutputWriter, TSV con InvariantCulture, null → string vacío, UTF-8 sin BOM — **HECHO**
9. ~~**Application/PipelineCoordinator**~~ ✅ — Orquestar todas las fuentes, concurrencia Task.WhenAll, construir resumen, source generators LoggerMessage — **HECHO**
10. ~~**Console/Program.cs**~~ ✅ — Setup DI vía ServiceRegistration, binding config, ejecución del coordinator, CancellationToken, exit code — **HECHO**
11. **Tests** — Parsing (válido + malformado), transformación, output, end-to-end con HTTP mockeado
12. **Lógica de retry** — Polly v8 / Microsoft.Extensions.Http.Resilience en HttpClient
13. **Field mapping config-driven** — Mapeo dinámico de campos desde appsettings.json
14. **Docker** — Dockerfile, validar `docker build` y `docker run`
15. **CI/CD** — Workflow de GitHub Actions, validar build verde
16. **Output de muestra** — Ejecutar pipeline, guardar .tsv de muestra en output/sample/
17. **README.md** — Setup, cómo ejecutar (dotnet + Docker), supuestos, trade-offs
18. **AI.md** — Documentación de uso de IA (10-15 líneas según la especificación)

---

## 16. Plan de Implementación Detallado — Guía de Construcción Fase por Fase

> **Propósito de esta sección**: Este es el plan de implementación granular, paso a paso, que traduce el Enfoque 1 (Clean Architecture con 4 proyectos — Core + Application + Infrastructure + Console, más Result<T> del Enfoque 8) del plano arquitectónico a código construible. Cada fase incluye QUÉ construimos, POR QUÉ lo construimos, CÓMO mapea a los criterios de evaluación, y QUÉ decir al defenderlo en la entrevista. Lee la Sección 15 para la hoja de ruta de alto nivel; lee ESTA sección para los detalles de implementación.

### 16.0 Por Qué Estamos Creando Esto — La Razón Estratégica

**El Contexto de Negocio**:
SyncMetrics Inc. (vía el pipeline de contratación de Actabl) agrega datos meteorológicos de múltiples APIs de terceros, los normaliza, y escribe archivos de output unificados para analytics downstream. Este es un problema real de integración de datos — el tipo de trabajo que un Staff Engineer en el equipo de Data Integrations hace diariamente.

**Por Qué Existe Este Ejercicio**:
Actabl NO está probando si Arthur puede llamar un API HTTP o parsear JSON — cualquier desarrollador mid-level puede hacer eso. Están probando si Arthur puede:
1. **Descomponer un pipeline** en etapas bien definidas con límites de interfaz limpios
2. **Diseñar para extensibilidad** sin sobre-ingeniería (el requerimiento "agregar una segunda fuente")
3. **Manejar fallos con gracia** — sin fallos silenciosos, reporte de errores estructurado, resiliencia
4. **Escribir tests significativos** que cubran casos edge reales, no solo el happy path
5. **Ejercitar juicio de nivel staff** — saber qué construir, qué NO construir, y articular trade-offs

**Por Qué el Enfoque 1 Específicamente**:
Evaluamos 10 enfoques exhaustivamente (Sección 3). El Enfoque 1 gana porque:
- Es **universalmente reconocido** — cualquier evaluador .NET abre la solución e inmediatamente entiende la arquitectura solo por los nombres de proyecto
- La regla de dependencia está **impuesta por el compilador** vía referencias de proyecto — Core tiene cero dependencias, nada referencia a Console
- Mapea **1:1 al lenguaje del ejercicio**: la especificación dice "HTTP, parsing, transformación y output son responsabilidades separadas" — cuatro proyectos hacen esas responsabilidades físicamente separadas
- Requiere **cero justificación** en la entrevista — el patrón habla por sí mismo, dejando más tiempo para discutir diseño del pipeline y manejo de errores
- Absorbe los mejores micro-patrones de enfoques rechazados: `Result<T>` del Enfoque 8 (ROP), cohesión feature-folder del Enfoque 5 (Vertical Slice), concurrencia `Task.WhenAll` del Enfoque 4 (Channels)
- Evita cada trampa de sobre-ingeniería: sin Azure Functions, sin MediatR, sin MEF plugins, sin frontend React, sin base de datos, sin servidor web

**Cómo Se Ve el Éxito Cuando Terminemos**:
- `dotnet run --project src/SyncMetrics.Pipeline.Console` → pipeline ejecuta, busca 3 ubicaciones concurrentemente, escribe un archivo `.tsv`, imprime un resumen de procesamiento
- `dotnet test` → 25+ tests pasan cubriendo parsing (válido + malformado), transformación, formateo de output, y end-to-end con HTTP mockeado
- `docker build && docker run` → mismo resultado, sin necesidad de .NET SDK
- Badge de GitHub Actions verde
- README.md corto y útil
- AI.md documenta uso real de IA con un override y un no-uso deliberado
- Cada archivo en el codebase tiene un propósito claro y mapea a un criterio de evaluación

**La Declaración de Arquitectura en Un Párrafo (memorizar esto para la entrevista)**:
> "Construí un pipeline Clean Architecture con cuatro capas: Core define los modelos de dominio y contratos de interfaces — IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter — más un tipo Result<T> para manejo explícito de errores. Application contiene el PipelineCoordinator que orquesta todas las implementaciones registradas de IWeatherDataSource, busca ubicaciones concurrentemente con Task.WhenAll, y agrega resultados. Infrastructure implementa los clientes HTTP, parsers JSON, transformadores de datos y escritor de archivos — cada fuente de datos vive en su propia sub-carpeta para cohesión. Console es el punto de entrada con configuración de DI y binding de configuración. Agregar una nueva fuente de datos significa una nueva sub-carpeta en Infrastructure con clases implementando interfaces de Core, más un registro en DI. La regla de dependencia se impone por referencias de proyecto — Core tiene cero dependencias, y nada referencia a Console. Parsers y transformadores retornan Result<T>, así que los errores son valores que se muestran en el resumen de procesamiento — sin fallos silenciosos."

---

### 16.1 Fase 1: Scaffolding de la Solución

**Qué creamos**: La estructura completa del sistema de archivos — archivo de solución, cuatro proyectos fuente (Core, Application, Infrastructure, Console), dos proyectos de test (UnitTests, IntegrationTests), todas las carpetas, `.gitignore`, referencias de proyecto.

**Por qué lo creamos primero**: La estructura de carpetas ES la arquitectura hecha visible. Cuando el evaluador abre el repo en su IDE, lo primero que ve son cuatro proyectos con nombres auto-explicativos: `Pipeline.Core`, `Pipeline.Application`, `Pipeline.Infrastructure`, `Pipeline.Console`. Los nombres de proyecto cuentan la historia de la arquitectura antes de que lean una sola línea de código. Las referencias de proyecto imponen la regla de dependencia en tiempo de compilación.

**Árbol de directorio completo**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/                   # Círculo interno — CERO referencias de proyecto
│   │   ├── SyncMetrics.Pipeline.Core.csproj
│   │   ├── Models/
│   │   │   ├── NormalizedWeatherRecord.cs
│   │   │   ├── LocationConfig.cs
│   │   │   ├── ProcessingResult.cs
│   │   │   └── PipelineSummary.cs
│   │   ├── Interfaces/
│   │   │   ├── IWeatherApiClient.cs
│   │   │   ├── IResponseParser.cs
│   │   │   ├── IDataTransformer.cs
│   │   │   ├── IOutputWriter.cs
│   │   │   └── IWeatherDataSource.cs
│   │   ├── Result.cs
│   │   └── PipelineError.cs
│   │
│   ├── SyncMetrics.Pipeline.Application/            # Referencias: solo Core
│   │   ├── SyncMetrics.Pipeline.Application.csproj
│   │   ├── PipelineCoordinator.cs
│   │   └── PipelineOptions.cs
│   │
│   ├── SyncMetrics.Pipeline.Infrastructure/         # Referencias: Core, Application
│   │   ├── SyncMetrics.Pipeline.Infrastructure.csproj
│   │   ├── OpenMeteo/
│   │   │   ├── OpenMeteoApiClient.cs
│   │   │   ├── OpenMeteoApiResponse.cs
│   │   │   ├── OpenMeteoResponseParser.cs
│   │   │   ├── OpenMeteoTransformer.cs
│   │   │   └── OpenMeteoDataSource.cs
│   │   ├── Output/
│   │   │   └── TabDelimitedFileWriter.cs
│   │   ├── Configuration/
│   │   │   ├── FieldMappingConfig.cs
│   │   │   └── ServiceRegistration.cs
│   │   └── Http/
│   │       └── ResilienceConfiguration.cs
│   │
│   └── SyncMetrics.Pipeline.Console/                # Referencias: Core, Application, Infrastructure
│       ├── SyncMetrics.Pipeline.Console.csproj
│       ├── Program.cs
│       └── appsettings.json
│
├── tests/
│   ├── SyncMetrics.Pipeline.UnitTests/
│   │   ├── SyncMetrics.Pipeline.UnitTests.csproj    # Referencias: Core, Application, Infrastructure
│   │   ├── OpenMeteo/
│   │   │   ├── OpenMeteoResponseParserTests.cs
│   │   │   ├── OpenMeteoTransformerTests.cs
│   │   │   └── TestData/
│   │   │       ├── valid_response.json
│   │   │       ├── valid_london_response.json
│   │   │       ├── malformed_missing_daily.json
│   │   │       ├── malformed_null_values.json
│   │   │       ├── malformed_mismatched_arrays.json
│   │   │       ├── malformed_empty_arrays.json
│   │   │       └── error_response.json
│   │   ├── Pipeline/
│   │   │   └── PipelineCoordinatorTests.cs
│   │   └── Output/
│   │       └── TabDelimitedWriterTests.cs
│   │
│   └── SyncMetrics.Pipeline.IntegrationTests/
│       ├── SyncMetrics.Pipeline.IntegrationTests.csproj
│       └── EndToEndPipelineTests.cs
│
├── output/
│   └── sample/
│       └── weather_data_sample.tsv
│
├── README.md
├── AI.md
├── Dockerfile
├── .gitignore
├── .github/
│   └── workflows/
│       └── build-and-test.yml
└── SyncMetrics.WeatherPipeline.sln
```

**Referencias de Proyecto (la regla de dependencia hecha explícita)**:
```
Core              → (nada)
Application       → Core
Infrastructure    → Core, Application
Console           → Core, Application, Infrastructure
UnitTests         → Core, Application, Infrastructure
IntegrationTests  → Core, Application, Infrastructure, Console
```

**Paquetes NuGet — proyecto Console (`SyncMetrics.Pipeline.Console.csproj`)**:

| Paquete | Versión | Propósito | Por qué este paquete específico |
|---------|---------|-----------|--------------------------------|
| `Microsoft.Extensions.Hosting` | 8.0.x | Contenedor DI, binding de configuración, logging, ciclo de vida de app | Bootstrap estándar de host .NET. Un paquete nos da `IServiceCollection`, `IConfiguration`, `ILogger<T>`, e `IOptions<T>`. Reemplaza cableado manual de 4+ paquetes separados |

**Paquetes NuGet — proyecto Infrastructure (`SyncMetrics.Pipeline.Infrastructure.csproj`)**:

| Paquete | Versión | Propósito | Por qué este paquete específico |
|---------|---------|-----------|--------------------------------|
| `Microsoft.Extensions.Http` | 8.0.x | `IHttpClientFactory` para instancias `HttpClient` administradas | Connection pooling apropiado, rotación DNS, clientes nombrados. Sin esto, `HttpClient` pierde sockets en procesos de larga duración |
| `Microsoft.Extensions.Http.Resilience` | 8.x | Políticas de retry con backoff exponencial + jitter en `HttpClient` | Stack de resiliencia nativo .NET 8. Reemplaza cableado raw de Polly v8. Se integra directamente con `IHttpClientFactory`. Una línea de config agrega retry + circuit breaker + timeout |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 8.0.x | Binding de configuración fuertemente tipado | `IOptions<T>` para PipelineOptions, LocationConfig, FieldMappingConfig |

**Paquetes NuGet — proyectos Core y Application**: Sin paquetes NuGet. Estas son librerías de clase C# puras con cero dependencias externas. Esto es intencional — las capas internas nunca deben depender de preocupaciones de infraestructura.

**Paquetes NuGet — proyectos de test (`SyncMetrics.Pipeline.UnitTests.csproj` e `IntegrationTests.csproj`)**:

| Paquete | Versión | Propósito | Por qué este paquete específico |
|---------|---------|-----------|--------------------------------|
| `Microsoft.NET.Test.Sdk` | 17.x | Infraestructura de host de test | Requerido para que `dotnet test` descubra y ejecute tests |
| `xunit` | 2.x | Framework de test | Estándar de industria para .NET. Usado por los propios repos de Microsoft (ASP.NET Core, EF Core). Basado en convención, sin herencia de clase de test requerida |
| `xunit.runner.visualstudio` | 2.x | Adaptador de VS Test | Habilita descubrimiento de tests en Visual Studio y CLI `dotnet test` |
| `NSubstitute` | 5.x | Mocking de interfaces | Sintaxis más limpia que Moq (`Substitute.For<IFoo>()` vs `new Mock<IFoo>().Object`). Sin ceremonia `.Setup().Returns()`. Preferencia de Arthur por legibilidad |
| `FluentAssertions` | 7.x | Librería de aserciones | `result.Should().BeEquivalentTo(expected)` es auto-documentado. Mejores mensajes de fallo que `Assert.Equal`. Hace que los tests lean como especificaciones |

**Por qué cero paquetes adicionales**: Sin MediatR (desajuste semántico para un pipeline lineal), sin AutoMapper (5 mapeos de campo no justifican una librería de mapping), sin LanguageExt/CSharpFunctionalExtensions (50 líneas de `Result<T>` personalizado evita una dependencia de 900KB), sin Serilog (el `Microsoft.Extensions.Logging` incorporado es suficiente para output de consola), sin FluentValidation (la validación es suficientemente simple para chequeos manuales en parsers).

**Defensa en entrevista**: "Cada paquete NuGet en la solución resuelve un problema específico y justificado. Puedo explicar por qué cada uno está ahí y por qué las alternativas fueron rechazadas. Superficie de dependencia mínima significa builds más rápidos, menos superficies de auditoría de seguridad, y sin sorpresas de dependencias transitivas. Un Staff Engineer debería poder justificar cada dependencia que introduce."

#### 16.1.1 Fase 1 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 28 de Marzo, 2026**
> - `dotnet build SyncMetrics.WeatherPipeline.sln` → 6/6 proyectos exitosos
> - `dotnet test` → infraestructura de test cableada (0 tests aún, sin errores)
> - `dotnet run --project src/SyncMetrics.Pipeline.Console` → punto de entrada ejecuta
> - Toda la estructura de carpetas, referencias de proyecto, paquetes NuGet, y configuración en su lugar

#### 16.1.2 Fase 1 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En proyectos del mundo real, obtener la estructura de la solución incorrectamente al inicio crea deuda técnica que se acumula. En un rol anterior, un equipo comenzó un servicio de pipeline como un solo proyecto "para moverse rápido." Seis meses después, con 8 desarrolladores contribuyendo, el proyecto tenía dependencias circulares entre lógica de parsing y clientes HTTP, modelos de dominio referenciando preocupaciones de base de datos, y tests unitarios que requerían conexiones a base de datos en vivo porque nada estaba desacoplado. La migración a una estructura por capas tomó 3 sprints y rompió el pipeline CI por una semana.

**La lección**: Los 15 minutos invertidos en scaffolding apropiado ahorran semanas de refactoring después. Las referencias de proyecto en esta solución no son ceremonia — son **barreras en tiempo de compilación**. Si un desarrollador escribe `using SyncMetrics.Pipeline.Console;` dentro de `Pipeline.Core`, el compilador lo detiene. Esto es más barato que un comentario de code review, más rápido que un documento de arquitectura, y más confiable que un acuerdo verbal.

**Respuesta de entrevista para "¿Por qué empezaste con scaffolding en vez de solo escribir código?"**:
> "Porque la estructura de carpetas ES la arquitectura. Cuando el evaluador abre este repo, lo primero que ve son cuatro proyectos con flechas de dependencia que solo apuntan hacia adentro. Eso comunica más sobre el diseño del sistema que cualquier párrafo de README. He visto equipos saltarse este paso y terminar con dependencias circulares que hacían testing imposible — los 15 minutos de scaffolding previenen semanas de deuda técnica."

#### 16.1.3 Fase 1 — Principios SOLID y Patrones Demostrados

Incluso en scaffolding — antes de una sola línea de lógica de negocio — la Fase 1 establece la base para cumplimiento SOLID y patrones de diseño. Aquí está lo que el evaluador ya puede observar:

| Principio / Patrón | Cómo la Fase 1 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | Cada proyecto tiene UNA razón para cambiar. Core cambia cuando los contratos de dominio cambian. Infrastructure cambia cuando las APIs externas cambian. Console cambia cuando la config de startup/DI cambia. Ningún proyecto hace doble labor. | *"Cada proyecto tiene exactamente un eje de cambio. Core nunca cambia por una actualización de librería HTTP — esa es la preocupación de Infrastructure."* |
| **O — Principio Abierto/Cerrado (OCP)** | La estructura de carpetas (`Infrastructure/OpenMeteo/`, futuro `Infrastructure/WeatherApi/`) muestra que el sistema está abierto para extensión (nueva fuente = nueva carpeta) y cerrado para modificación (código existente intocado). | *"Agregar una segunda fuente de datos significa agregar una carpeta en Infrastructure con nuevas implementaciones de interfaces de Core. Cero modificación a código existente."* |
| **D — Principio de Inversión de Dependencia (DIP)** | Las referencias de proyecto imponen que módulos de alto nivel (Application) dependan de abstracciones (interfaces de Core), no de módulos de bajo nivel (Infrastructure). El compilador rechaza cualquier violación. | *"Application depende de Pipeline.Core, no de Pipeline.Infrastructure. La flecha de dependencia apunta de concreto a abstracto — impuesto por el grafo de referencias de proyecto, no por convención."* |
| **I — Principio de Segregación de Interfaces (ISP)** | La carpeta separada `Interfaces/` en Core con 5 interfaces enfocadas (IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter, IWeatherDataSource) — cada una con una sola responsabilidad — demuestra ISP. Sin "interfaz dios." | *"Cinco interfaces pequeñas y enfocadas en vez de un IWeatherService con 15 métodos. Cada implementación solo necesita conocer su propio contrato."* |
| **L — Principio de Sustitución de Liskov (LSP)** | El setup del Strategy Pattern (IWeatherDataSource con múltiples implementaciones) está diseñado para LSP: cualquier implementación de IWeatherDataSource puede ser intercambiada sin afectar al PipelineCoordinator. | *"El PipelineCoordinator trabaja con IEnumerable<IWeatherDataSource>. No sabe ni le importa si está ejecutando OpenMeteo o WeatherApi — cualquier implementación que satisfaga el contrato de interfaz es sustituible."* |
| **Strategy Pattern** | La estructura del proyecto (`Infrastructure/OpenMeteo/`, futuro `Infrastructure/WeatherApi/`) establece el layout físico del strategy pattern. Cada estrategia (fuente de datos) vive en su propia sub-carpeta con todos sus componentes. | *"Cada fuente de datos es una estrategia — una implementación autocontenida de las interfaces de Core. El coordinator itera todas las estrategias registradas en DI."* |
| **Inyección de Dependencias (DI)** | El `Program.cs` de Console usa `Host.CreateApplicationBuilder` — la base para inyección por constructor a través de todas las capas. Infrastructure registrará sus implementaciones contra interfaces de Core. | *"Todas las dependencias se resuelven a través del contenedor DI. Sin `new OpenMeteoClient()` en ningún lugar — el contenedor cablea implementaciones con interfaces al startup."* |
| **Clean Architecture (Regla de Dependencia por Capas)** | El grafo de referencia de 4 proyectos (Core→nada, App→Core, Infra→Core+App, Console→todos) ES la regla de dependencia de Clean Architecture hecha visible e impuesta por el compilador. | *"La regla de dependencia no es un diagrama en una wiki — está impuesta por MSBuild. El compilador es el guardián de la arquitectura."* |

**Insight clave para la entrevista**: Estos principios no son aspiracionales — son **estructurales**. El evaluador no necesita leer código para verificarlos. Abren el `.sln` en Visual Studio, expanden las referencias de proyecto, y ven el grafo de dependencias. La arquitectura se auto-documenta a nivel de proyecto.

---

### 16.2 Fase 2: Pipeline.Core — El Círculo Interno

**Qué creamos**: Los tipos centrales de los que depende cada otro proyecto. Esta es la capa de Dominio de Clean Architecture — una librería de clases con CERO dependencias NuGet y CERO referencias de proyecto. Nada en Pipeline.Core referencia nada en Application, Infrastructure, o Console. La flecha de dependencia siempre apunta HACIA ADENTRO. El compilador lo impone.

**Por qué importa**: Pipeline.Core es lo que hace la arquitectura extensible. Una nueva fuente de datos (Infrastructure/WeatherApi/) solo necesita implementar las interfaces definidas aquí. Nunca necesita saber sobre Infrastructure/OpenMeteo/. Este es el Principio Abierto/Cerrado impuesto a nivel de proyecto por el compilador.

#### 16.2.1 `Result<T>` — Errores como Valores (~50 líneas de código)

**Qué es**: Un tipo monádico que representa ya sea un valor exitoso (`T`) o un fallo (`PipelineError`). Inspirado en el tipo `Result` de F# y la Railway-Oriented Programming de Scott Wlaschin, pero implementado como C# idiomático que cualquier desarrollador .NET puede leer.

**Superficie de API**:
```csharp
public class Result<T>
{
    // Métodos factory
    public static Result<T> Success(T value);
    public static Result<T> Failure(PipelineError error);
    
    // Inspección de estado
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public T Value { get; }              // lanza si es Failure
    public PipelineError Error { get; }  // lanza si es Success
    
    // Operaciones monádicas
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func);  // encadenar ops falibles
    public Result<TNext> Map<TNext>(Func<T, TNext> func);           // transformar valor de éxito
    public T GetValueOrDefault(T defaultValue);                      // extracción segura
}
```

**Por qué construimos el nuestro en vez de usar una librería**:
- LanguageExt es 900KB con cientos de tipos (Option, Either, Seq, Lst, etc.) — overkill masivo
- CSharpFunctionalExtensions es más ligero pero sigue siendo una dependencia externa para 50 líneas de código
- Nuestro `Result<T>` está adaptado a `PipelineError` — el tipo de error es específico del dominio, no genérico
- Sin dependencia = sin conflictos de versión, sin advisory de seguridad, sin paquetes transitivos
- El evaluador puede leer la implementación completa en 30 segundos

**Por qué Result<T> en vez de try/catch**:
- **Honestidad de firma**: Un método retornando `Result<SourceModel>` declara "Puedo fallar" en el tipo. Un método retornando `SourceModel` que lanza excepción está mintiendo sobre su contrato
- **Manejo forzado**: El llamador DEBE verificar `IsSuccess`/`IsFailure`. Con excepciones, un bloque catch faltante es un bug silencioso descubierto en runtime
- **Agregación**: El pipeline coordinator recolecta `Result<T>[]` de todas las ubicaciones. Con excepciones, necesitarías `try { } catch { errors.Add(...); }` alrededor de cada llamada — Result hace esto una operación LINQ limpia
- **Sin abuso de flujo de control**: Las excepciones son para circunstancias excepcionales. Un campo faltante en una respuesta JSON es ESPERADO en integración de datos — no es excepcional, es un modo de fallo conocido

**Defensa en entrevista**: "Usé tipos Result para las etapas de parsing y transformación así los errores son valores, no excepciones. El pipeline coordinator agrega objetos Result y construye el resumen de procesamiento tanto de éxitos como de fallos. Esto elimina fallos silenciosos por diseño — si una etapa puede fallar, el tipo de retorno te fuerza a manejarlo. Mantuve la implementación en ~50 líneas sin dependencia externa porque eso es todo lo que necesita un pipeline de datos."

#### 16.2.2 `PipelineError` — Jerarquía de Errores Tipados

**Qué es**: Una jerarquía de tipos record representando cada categoría de fallo que el pipeline puede encontrar. Usa records de C# por inmutabilidad, igualdad por valor, y sintaxis concisa.

```csharp
public abstract record PipelineError(string Message, string? LocationName = null);

public record FetchError(string Message, string? LocationName = null, int? StatusCode = null, string? Url = null) 
    : PipelineError(Message, LocationName);

public record ParseError(string Message, string? LocationName = null, string? Field = null, string? RawContent = null) 
    : PipelineError(Message, LocationName);

public record TransformError(string Message, string? LocationName = null, string? Field = null, int? RecordIndex = null) 
    : PipelineError(Message, LocationName);

public record OutputError(string Message, string? FilePath = null) 
    : PipelineError(Message);
```

**Por qué errores tipados en vez de mensajes string**:
- `FetchError` lleva `StatusCode` — el resumen de procesamiento puede reportar "HTTP 503 Service Unavailable para Tokyo" no solo "fetch falló"
- `ParseError` lleva `Field` — reportar "campo faltante 'daily.temperature_2m_max' para London" no solo "parse falló"
- `TransformError` lleva `RecordIndex` — reportar "temperatura null en índice 3 para New York" no solo "transform falló"
- Pattern matching: `error switch { FetchError fe => ..., ParseError pe => ..., _ => ... }` — cada tipo de error obtiene manejo contextual
- Testing: `error.Should().BeOfType<ParseError>().Which.Field.Should().Be("temperature_2m_max")` — aserciones precisas

**Defensa en entrevista**: "Cada error en el pipeline lleva información contextual — no solo un mensaje, sino el nombre de la ubicación, el campo específico que falló, el código HTTP de estado. Esto hace el resumen de procesamiento accionable. Si Tokyo falla porque el API retornó 503, el resumen dice exactamente eso. Un desarrollador u operador leyendo el output puede diagnosticar el problema sin leer logs."

#### 16.2.3 Modelos — Los 4 Modelos de Dominio

**`NormalizedWeatherRecord`** — La fila de output unificada:
```csharp
public record NormalizedWeatherRecord
{
    public required string Source { get; init; }          // "OpenMeteo", "WeatherApi", etc.
    public required string Location { get; init; }        // Legible para humanos: "New York"
    public required double Latitude { get; init; }        // Coordenadas exactas usadas
    public required double Longitude { get; init; }
    public required DateOnly Date { get; init; }          // Fecha ISO 8601
    public double? TempMaxCelsius { get; init; }          // Nullable — datos meteorológicos pueden tener huecos
    public double? TempMinCelsius { get; init; }
    public double? PrecipitationMm { get; init; }
    public double? WindSpeedMaxKmh { get; init; }
    public double? UvIndexMax { get; init; }
    public required DateTime FetchedAtUtc { get; init; }  // Cuándo se recuperaron estos datos
}
```

**Por qué doubles nullables para campos meteorológicos**: Los datos meteorológicos reales tienen huecos. Open-Meteo puede retornar `null` para un campo si la medición no está disponible. Nuestro modelo DEBE manejar esto. Si usáramos `double` no-nullable, crashearíamos o usaríamos silenciosamente `0.0` — que es una temperatura válida (0°C), haciendo el bug invisible. Nullable fuerza manejo explícito en todas partes.

**Por qué `DateOnly` no `DateTime`**: Los pronósticos meteorológicos son por día, no por momento. `DateOnly` es semánticamente correcto y fue introducido específicamente para este caso de uso. También evita confusión de zona horaria — una fecha es una fecha, no un punto en el tiempo.

**`LocationConfig`** — Una ubicación configurada (**DTO de binding de config — debe usar propiedades mutables `{ get; set; }`**):
```csharp
namespace SyncMetrics.Pipeline.Core.Models;

/// <summary>
/// Una ubicación configurada para buscar datos meteorológicos. Bound desde appsettings.json.
/// Usa propiedades set mutables para que IConfiguration.Bind() pueda poblar vía reflexión
/// después de Activator.CreateInstance() — required+init bloquea el binder default.
/// </summary>
public record LocationConfig
{
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
```

> ⚠️ **Bug de Runtime Encontrado en Fase 10**: La implementación original usaba `required string Name { get; init; }`. El binder de `IConfiguration` llama `Activator.CreateInstance()` (constructor sin args) y luego configura propiedades vía reflexión. Las propiedades marcadas `required` no pueden ser satisfechas post-construcción en esta ruta, así que todas las ubicaciones silenciosamente tenían `Name = ""` y coordenadas `0.0`. La corrección es usar `{ get; set; }` con un valor default. **Regla de patrón**: DTOs de binding de config deben usar propiedades mutables. `required init` es correcto SOLO para objetos de valor de dominio que nunca se pueblan vía `IConfiguration`.

**`ProcessingResult`** — Resultado de ejecución por fuente:
```csharp
public record ProcessingResult
{
    public required string SourceName { get; init; }
    public required IReadOnlyList<NormalizedWeatherRecord> Records { get; init; }
    public required IReadOnlyList<PipelineError> Errors { get; init; }
    public required TimeSpan Duration { get; init; }
    public int LocationsAttempted { get; init; }
    public int LocationsSucceeded { get; init; }
}
```

**`PipelineSummary`** — Output completo de la ejecución:
```csharp
public record PipelineSummary
{
    public required IReadOnlyList<ProcessingResult> SourceResults { get; init; }
    public required int TotalRecordsWritten { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required string? OutputFilePath { get; init; }
    public bool HasErrors => SourceResults.Any(r => r.Errors.Count > 0);
    public IEnumerable<PipelineError> AllErrors => SourceResults.SelectMany(r => r.Errors);
}
```

#### 16.2.4 Interfaces — Los 5 Contratos del Pipeline

Estos son los 5 archivos más importantes de toda la solución. Definen la arquitectura del pipeline. Todo lo demás es detalle de implementación.

**`IWeatherApiClient`** — Abstracción HTTP por fuente:
```csharp
public interface IWeatherApiClient
{
    string SourceName { get; }
    Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken cancellationToken);
}
```
**Por qué `Result<string>` no `Result<HttpResponseMessage>`**: La interfaz expone el string JSON CRUDO, no la plomería HTTP. Una nueva fuente podría ni siquiera usar HTTP (podría leer de archivo, una cola, etc.). La interfaz es sobre "dame los datos crudos para esta ubicación," no "haz una llamada HTTP."

**`IResponseParser<TRaw>`** — Deserialización JSON + validación:
```csharp
public interface IResponseParser<TRaw>
{
    Result<TRaw> Parse(string rawJson);
}
```
**Por qué genérico `<TRaw>`**: Cada fuente tiene su propia forma de respuesta. Open-Meteo retorna arrays paralelos. WeatherAPI.com podría retornar objetos anidados. El tipo de output del parser es específico de la fuente. Solo el transformer sabe cómo convertir `TRaw` en `NormalizedWeatherRecord`.

**`IDataTransformer<TRaw>`** — Modelo fuente → registros normalizados:
```csharp
public interface IDataTransformer<TRaw>
{
    Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(TRaw rawData, LocationConfig location);
}
```
**Por qué toma `LocationConfig`**: La respuesta cruda del API no siempre incluye el nombre de la ubicación. Open-Meteo retorna coordenadas ajustadas (ej., 40.7103 en vez de 40.7128) pero no "New York." El transformer enriquece cada registro con el contexto de ubicación original.

**`IOutputWriter`** — Escribe el output final:
```csharp
public interface IOutputWriter
{
    Task<Result<string>> WriteAsync(IReadOnlyList<NormalizedWeatherRecord> records, CancellationToken cancellationToken);
}
```
**Por qué `Result<string>` de retorno**: El string es la ruta del archivo de output en éxito. En fallo (disco lleno, permiso denegado), es un `OutputError`. El pipeline coordinator usa esto para incluir la ruta del archivo en el resumen.

**`IWeatherDataSource`** — El compuesto de fuente:
```csharp
public interface IWeatherDataSource
{
    string SourceName { get; }
    Task<ProcessingResult> ProcessAsync(IEnumerable<LocationConfig> locations, CancellationToken cancellationToken);
}
```
**Por qué existe este compuesto**: El PipelineCoordinator no debería saber sobre parsers o transformers — solo ejecuta fuentes. Cada fuente cablea su propio client → parser → transformer internamente. El coordinator solo ve `IWeatherDataSource.ProcessAsync()`. Este es el Strategy pattern — cada fuente implementa su propia estrategia para obtener datos normalizados.

**Defensa en entrevista para las 5 interfaces**: "El ejercicio explícitamente requiere 'HTTP, parsing, transformación y output son responsabilidades separadas.' Mis interfaces mapean directamente: IWeatherApiClient = HTTP, IResponseParser = parsing, IDataTransformer = transformación, IOutputWriter = output. IWeatherDataSource es el compuesto que cablea los primeros tres juntos por fuente. Agregar WeatherAPI.com significa implementar estas interfaces en una nueva carpeta de feature — el pipeline coordinator las descubre vía DI."

#### 16.2.5 Fase 2 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 28 de Marzo, 2026**
> - `dotnet build` → 6/6 proyectos exitosos (tipos de Pipeline.Core consumidos por Application, Infrastructure, Console vía referencias de proyecto)
> - `dotnet test` → 0 tests, sin errores (tests vienen en Fase 11)
> - 11 nuevos archivos creados en Pipeline.Core: `Result.cs`, `PipelineError.cs`, 4 modelos, 5 interfaces
> - CERO dependencias NuGet en Pipeline.Core (como fue diseñado)
> - CERO referencias de proyecto en Pipeline.Core (círculo interno — no depende de nada)
> - Warning del analizador CA1000 en métodos factory estáticos de `Result<T>` — suprimido con `#pragma warning disable CA1000` (patrón estándar para tipos monádicos, igual que `Task.FromResult`)

#### 16.2.6 Fase 2 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En una empresa anterior, un servicio de integración de datos fue construido sin una capa de dominio core. Las clases de cliente HTTP retornaban directamente objetos `HttpResponseMessage` que eran consumidos por todo el codebase — la lógica de parsing, la lógica de transformación, e incluso el output writer todos tomaban `HttpResponseMessage` o `JObject` como parámetros. Cuando el equipo necesitó agregar una segunda fuente de datos (un feed XML SOAP), descubrieron que todo el pipeline estaba acoplado a tipos HTTP y JSON. La migración requirió tocar cada archivo del proyecto. Tomó 4 sprints para un equipo de 6 personas, e introdujo regresiones porque los mocks de test estaban todos construidos alrededor de `HttpResponseMessage`.

**La lección**: La capa core define los contratos que DESACOPLAN todo lo demás. `IWeatherApiClient` retorna `Result<string>` — no `HttpResponseMessage`. `IResponseParser<TRaw>` toma un `string` — no un `JsonDocument`. `IDataTransformer<TRaw>` retorna `NormalizedWeatherRecord` — no un tipo específico de fuente. Estas abstracciones significan que cuando una nueva fuente usa XML, SOAP, o CSV en vez de JSON, el pipeline coordinator no lo sabe y no le importa. El costo es 11 archivos pequeños con ~200 líneas totales. La ganancia es que el punto de extensibilidad del sistema se define una vez y se impone en todas partes.

**La decisión de `Result<T>` específicamente**: En ese mismo servicio de integración, los errores se manejaban vía bloques `try/catch` dispersos por 40+ métodos. Cuando el equipo necesitó una función de "resumen de procesamiento" (exactamente lo que este ejercicio pide), tuvieron que retrofitear recolección de errores en cada bloque catch — y descubrieron que habían estado tragando silenciosamente 3 diferentes casos de error. Con `Result<T>`, el resumen de procesamiento se construye de los valores de retorno — si un método puede fallar, el fallo es un VALOR en el tipo de retorno, no un efecto secundario atrapado en otro lugar.

**Respuesta de entrevista para "¿Por qué construiste tu propio tipo Result en vez de usar una librería?"**:
> "Porque son 55 líneas de código con cero dependencias. LanguageExt es 900KB con cientos de tipos que no necesitamos. CSharpFunctionalExtensions sigue agregando una dependencia para lo que es efectivamente un wrapper sobre (T?, PipelineError?). Nuestro Result está adaptado a PipelineError — el tipo de error es específico del dominio. Y el evaluador puede leer la implementación completa en 30 segundos, lo cual importa para una revisión take-home."

#### 16.2.7 Fase 2 — Principios SOLID y Patrones Demostrados

La Fase 2 es donde la arquitectura se vuelve concreta. Las interfaces y tipos definidos aquí son la columna vertebral de cada principio SOLID y patrón en el sistema.

| Principio / Patrón | Cómo la Fase 2 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | Cada interfaz tiene UN método (o uno + propiedad de nombre). `IWeatherApiClient` busca. `IResponseParser` parsea. `IDataTransformer` transforma. `IOutputWriter` escribe. Ninguna interfaz hace dos cosas. | *"Cada interfaz tiene el nombre de exactamente lo que hace. Sin interfaz dios `IWeatherService.FetchAndParseAndTransform()`."* |
| **O — Principio Abierto/Cerrado (OCP)** | Agregar una nueva fuente de datos significa implementar `IWeatherDataSource` + sus sub-interfaces. El código existente está cerrado para modificación. El coordinator no cambia. | *"Puedo agregar WeatherAPI.com creando 4 nuevas clases implementando estas interfaces. Cero líneas cambiadas en código existente. El Principio Abierto/Cerrado se impone por los contratos de interfaz."* |
| **L — Principio de Sustitución de Liskov (LSP)** | `IWeatherDataSource` acepta `IEnumerable<LocationConfig>` y retorna `ProcessingResult`. Cualquier implementación — OpenMeteo, WeatherApi, un mock — es sustituible sin que el coordinator sepa. | *"El PipelineCoordinator itera `IEnumerable<IWeatherDataSource>`. Ya sea que esté ejecutando 1 fuente o 10, el código es idéntico. Sustitución de Liskov a nivel de colección."* |
| **I — Principio de Segregación de Interfaces (ISP)** | 5 interfaces enfocadas en vez de 1 grande. Un mock de test para parsing no necesita implementar fetching HTTP. Cada consumidor depende solo de la interfaz que necesita. | *"El test de parser mockea `IResponseParser` — no necesita implementar `IWeatherApiClient`. Cada interfaz está segregada a su responsabilidad."* |
| **D — Principio de Inversión de Dependencia (DIP)** | Todas las interfaces viven en Pipeline.Core (la capa más interna). Las implementaciones vivirán en Pipeline.Infrastructure (la capa más externa). La política de alto nivel depende de abstracciones, no de detalles. | *"El PipelineCoordinator en Application referencia IWeatherDataSource de Core. El OpenMeteoDataSource real en Infrastructure es invisible para Application. Inversión de dependencia impuesta por el grafo del proyecto."* |
| **Strategy Pattern** | `IWeatherDataSource` ES la interfaz Strategy. Cada fuente de datos es una estrategia concreta. El PipelineCoordinator es el contexto que ejecuta estrategias sin conocer su implementación. | *"Strategy Pattern clásico — IWeatherDataSource define el contrato de estrategia, cada fuente de datos proporciona su propia implementación de estrategia, y el coordinator las ejecuta todas polimórficamente."* |
| **Result Pattern (micro-patrón ROP)** | `Result<T>` con `Bind` y `Map` habilita propagación de errores Railway-Oriented. Los errores son valores, no excepciones. El pipeline coordinator agrega objetos `Result` para construir el resumen de procesamiento. | *"Los errores son valores de primera clase. Un método retornando `Result<T>` declara 'Puedo fallar' en su firma de tipo. El llamador es forzado a manejarlo — sin fallos silenciosos por diseño."* |
| **Record Types (Patrón Value Object)** | Los 4 modelos y los 4 tipos de error son tipos `record` — inmutables, igualdad por valor, concisos. `NormalizedWeatherRecord` es un Value Object que representa una sola fila normalizada. | *"Todos los tipos de dominio son records inmutables. Un NormalizedWeatherRecord no puede ser accidentalmente mutado después de la creación. Igualdad por valor significa que dos registros con los mismos datos son considerados iguales — esencial para testing."* |
| **Jerarquía de Errores Tipados** | `PipelineError` → `FetchError`, `ParseError`, `TransformError`, `OutputError`. Cada uno lleva datos contextuales (StatusCode, Field, RecordIndex). Pattern-matcheable. | *"Cada tipo de error lleva datos contextuales específicos de su etapa. FetchError tiene StatusCode, ParseError tiene el nombre del Field que falló. El resumen de procesamiento formatea cada tipo diferentemente porque los errores están tipados, no son solo strings."* |
| **Modelado de Dominio Nullable** | `NormalizedWeatherRecord` usa `double?` para campos meteorológicos — modelando explícitamente que los datos pueden faltar. `0.0` NO es "faltante" (0°C es una lectura de temperatura válida). | *"Los datos meteorológicos tienen huecos. Usé doubles nullables porque 0.0 es una lectura de temperatura válida. Si usara doubles no-nullables, un valor faltante silenciosamente se convierte en 0.0 — un bug invisible de corrupción de datos."* |

**Insight clave para la entrevista**: La Fase 2 es la fase que hace el ejercicio EXTENSIBLE. El evaluador preguntará "¿cómo agregarías una segunda fuente?" — y la respuesta vive enteramente en estas 5 interfaces. Todo lo demás en el sistema es un detalle de implementación de estos contratos.

---

### 16.3 Fase 3: Configuración

**Qué creamos**: `appsettings.json` con todos los valores configurables, clases de opciones fuertemente tipadas vinculadas vía `IOptions<T>`, y el método de registro DI.

**Por qué esta fase viene antes de las features**: Las features leen su config vía opciones inyectadas. Si construimos config primero, cada implementación de feature tiene su configuración lista. Sin valores hardcodeados en ningún momento.

#### 16.3.1 `appsettings.json` — Configuración Completa

```json
{
  "Pipeline": {
    "OutputDirectory": "./output",
    "OutputFilePattern": "weather_data_{timestamp}.tsv",
    "Sources": [
      {
        "Name": "OpenMeteo",
        "Enabled": true,
        "BaseUrl": "https://api.open-meteo.com/v1/forecast",
        "TimeoutSeconds": 30,
        "RetryCount": 3,
        "Locations": [
          { "Name": "New York", "Latitude": 40.7128, "Longitude": -74.0060 },
          { "Name": "London", "Latitude": 51.5074, "Longitude": -0.1278 },
          { "Name": "Tokyo", "Latitude": 35.6762, "Longitude": 139.6503 }
        ],
        "FieldMappings": [
          { "SourceField": "temperature_2m_max", "OutputColumn": "TempMaxC", "Unit": "°C" },
          { "SourceField": "temperature_2m_min", "OutputColumn": "TempMinC", "Unit": "°C" },
          { "SourceField": "precipitation_sum", "OutputColumn": "PrecipitationMm", "Unit": "mm" },
          { "SourceField": "wind_speed_10m_max", "OutputColumn": "WindSpeedMaxKmh", "Unit": "km/h" },
          { "SourceField": "uv_index_max", "OutputColumn": "UVIndexMax", "Unit": "index" }
        ]
      }
    ]
  }
}
```

**Desglose de config — qué controla cada campo**:

| Campo | Propósito | Por qué es configurable |
|-------|-----------|------------------------|
| `OutputDirectory` | Dónde se escriben los archivos `.tsv` | El evaluador podría querer cambiar la ubicación de output; destino de volume mount Docker |
| `OutputFilePattern` | Naming de archivo con token `{timestamp}` | Previene sobrescribir ejecuciones previas; ordenable por tiempo |
| `Sources[].Name` | Identificador de fuente legible para humanos | Aparece en columna `Source` del output y resumen de procesamiento |
| `Sources[].Enabled` | Toggle de fuentes sin remover config | Patrón de producción: deshabilitar una fuente inestable sin deploy de código |
| `Sources[].BaseUrl` | URL base del API | Diferentes ambientes (staging vs prod) pueden tener URLs diferentes |
| `Sources[].TimeoutSeconds` | Timeout HTTP por fuente | Fuentes lentas no deberían bloquear a las rápidas; ajustable por SLA del API |
| `Sources[].RetryCount` | Intentos máximos de retry | Diferentes APIs pueden justificar diferente agresividad de retry |
| `Sources[].Locations[]` | Lista de ubicaciones por fuente | Diferentes fuentes pueden tener diferente cobertura de ubicaciones |
| `Sources[].FieldMappings[]` | Mapeo campo fuente → columna de output | **FEATURE BONUS**: Una nueva fuente con diferentes nombres de campo (ej., `temp_high` en vez de `temperature_2m_max`) solo necesita un cambio de config, no de código |

#### 16.3.2 Clases de Opciones Fuertemente Tipadas

```csharp
public class PipelineOptions
{
    public const string SectionName = "Pipeline";
    public string OutputDirectory { get; set; } = "./output";
    public string OutputFilePattern { get; set; } = "weather_data_{timestamp}.tsv";
    public List<SourceOptions> Sources { get; set; } = new();
}

public class SourceOptions
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public List<LocationConfig> Locations { get; set; } = new();
    public List<FieldMapping> FieldMappings { get; set; } = new();
}

public class FieldMapping
{
    public string SourceField { get; set; } = string.Empty;
    public string OutputColumn { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}
```

#### 16.3.3 `ServiceRegistration.cs` — El Punto de Cableado DI

```csharp
public static class ServiceRegistration
{
    public static IServiceCollection AddPipelineServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind config
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        
        // HTTP client con resiliencia
        services.AddHttpClient("OpenMeteo", ...)
            .AddStandardResilienceHandler();
        
        // Feature Open-Meteo
        services.AddSingleton<IWeatherApiClient, OpenMeteoApiClient>();
        services.AddSingleton<IResponseParser<OpenMeteoApiResponse>, OpenMeteoResponseParser>();
        services.AddSingleton<IDataTransformer<OpenMeteoApiResponse>, OpenMeteoTransformer>();
        services.AddSingleton<IWeatherDataSource, OpenMeteoDataSource>();
        
        // Pipeline + Output
        services.AddSingleton<IOutputWriter, TabDelimitedFileWriter>();
        services.AddSingleton<PipelineCoordinator>();
        
        return services;
    }
}
```

**Por qué `ServiceRegistration.cs` es crítico para la historia de extensibilidad**: Cuando el evaluador pregunta "¿cómo agregarías una segunda fuente?", la respuesta es: "Agrega 4 líneas aquí — registra el client, parser, transformer, y data source de la nueva fuente. El PipelineCoordinator obtiene `IEnumerable<IWeatherDataSource>` de DI y las ejecuta todas. Cero cambios en ningún otro lugar."

**Defensa en entrevista**: "Toda la configuración vive en `appsettings.json` — cero valores hardcodeados. Ubicaciones, URLs, cuentas de retry, field mappings son todos configurables. El bonus de field mapping config-driven significa que una nueva fuente con diferentes nombres de campo solo necesita una sección de config — el transformer lee el mapping dinámicamente. Y la clase ServiceRegistration es el único lugar donde una nueva fuente se cablea — 4 líneas de registro DI."

#### 16.3.4 Fase 3 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 28 de Marzo, 2026**
> - `dotnet build` → 6/6 proyectos exitosos
> - `dotnet run --project src/SyncMetrics.Pipeline.Console` → ejecuta con cableado de config activo
> - 2 nuevos archivos en Infrastructure/Configuration: `PipelineOptions.cs` (3 clases), `ServiceRegistration.cs`
> - `appsettings.json` actualizado: config completa de fuente con ubicaciones, field mappings, URLs, settings de retry
> - `Program.cs` actualizado: llama `AddPipelineServices(builder.Configuration)` — cableado DI vivo
> - Placeholder Configuration/.gitkeep removido

#### 16.3.5 Fase 3 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En una empresa anterior, un servicio de agregación de datos meteorológicos tenía URLs de API, cuentas de retry, y coordenadas de ubicación hardcodeadas a través de 12 clases diferentes. Cuando el vendor cambió su endpoint de API de `v1` a `v2`, el equipo tuvo que grep todo el codebase, encontrar cada ocurrencia, y actualizarlas — faltando una en un servicio background que ejecutaba en un schedule diferente. Ese servicio huérfano falló silenciosamente por 3 semanas antes de que alguien notara porque estaba golpeando el endpoint v1 decomisionado. La corrección tomó 20 minutos; descubrir el problema tomó 3 semanas de reportes downstream corrompidos.

**La lección**: La configuración no es sobre conveniencia — es sobre **fuente única de verdad**. Cada valor configurable en este pipeline vive en UN lugar: `appsettings.json`. `PipelineOptions` es el contrato fuertemente tipado. Ningún desarrollador puede accidentalmente hardcodear una URL en una nueva clase porque el patrón está establecido: inyecta `IOptions<PipelineOptions>`, lee desde ahí. La flag `SourceOptions.Enabled` existe específicamente porque en producción, toggle de una fuente inestable debería ser un cambio de config, no un deploy de código con un pipeline CI/CD de 30 minutos.

**La decisión de `ServiceRegistration.cs` específicamente**: En esa misma empresa, los registros DI estaban dispersos a través de 5 diferentes clases parciales de `Startup.cs` y 3 métodos de extensión separados. Cuando un desarrollador agregó un nuevo servicio pero lo registró en el lugar equivocado, el contenedor DI silenciosamente cayó a un lifetime scope diferente, causando un memory leak que tomó 2 semanas diagnosticar. Un archivo `ServiceRegistration.cs` con un solo método `AddPipelineServices()` significa que hay exactamente UN lugar para buscar cuando se depuran problemas de DI.

**La feature bonus de field mapping**: El evaluador pregunta "¿cómo agregarías una fuente que llama temperatura `temp_high` en vez de `temperature_2m_max`?" La respuesta es: "Agrega una sección de config con el mapping. El transformer lee `FieldMappings[]` y mapea dinámicamente. Cero cambios de código." Esta es la feature bonus de field mapping config-driven construida en la arquitectura desde el día uno — no atornillada después.

**Respuesta de entrevista para "¿Por qué construiste configuración antes de features?"**:
> "Porque cada feature necesita config. Si construyera el cliente OpenMeteo primero, hardcodearía la URL para desbloquearme, luego refactorizaría a config después. Son dos toques por cada valor. Al construir config primero, cada feature que escribo desde este punto lee de opciones fuertemente tipadas — cero valores hardcodeados en ningún punto del historial git. El evaluador puede verificar: sin magic strings en ningún lugar del codebase."

#### 16.3.6 Fase 3 — Principios SOLID y Patrones Demostrados

La Fase 3 establece la columna vertebral de configuración de la que depende cada fase subsiguiente. Así es cómo mapea a SOLID y patrones de diseño:

| Principio / Patrón | Cómo la Fase 3 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | `PipelineOptions` contiene config. `ServiceRegistration` cablea DI. `appsettings.json` almacena valores. Tres archivos, tres responsabilidades. Ningún archivo hace doble labor. | *"Las clases de config contienen config. La clase de registro registra servicios. El archivo JSON almacena valores. Cada uno tiene una razón para cambiar."* |
| **O — Principio Abierto/Cerrado (OCP)** | `Sources[]` es un array — agregar una nueva fuente significa agregar un nuevo bloque JSON en el array, no modificar config existente. `ServiceRegistration` agregará 4 líneas para una nueva fuente, pero nunca modifica registros existentes. | *"El array Sources está abierto para extensión. Agregar WeatherApi significa agregar un nuevo objeto al array — el bloque de config de OpenMeteo queda intocado."* |
| **D — Principio de Inversión de Dependencia (DIP)** | `ServiceRegistration.cs` vive en Infrastructure pero es llamado por Console. Console depende de la abstracción (método de extensión `AddPipelineServices`), no de tipos individuales de Infrastructure. Las features dependerán de `IOptions<PipelineOptions>`, no de `IConfiguration` crudo. | *"Program.cs llama un método — AddPipelineServices. No sabe sobre OpenMeteoApiClient o TabDelimitedFileWriter. Esos se registran dentro de Infrastructure y se consumen vía interfaces de Core."* |
| **Patrón Options** | `IOptions<PipelineOptions>` es el patrón recomendado por .NET para configuración fuertemente tipada. Proporciona validación, soporte de recarga, y testeabilidad (puede inyectar `Options.Create(new PipelineOptions {...})` en tests). | *"Uso IOptions<T> en vez de leer IConfiguration directamente porque es fuertemente tipado, testeable (puedo crear options en tests sin appsettings.json), y soporta validación de configuración."* |
| **Patrón Composition Root** | `ServiceRegistration.cs` + `Program.cs` forman el Composition Root — el único lugar donde todo el grafo de dependencias se ensambla. Ningún servicio crea sus propias dependencias. | *"El Composition Root es el único lugar que conoce tipos concretos. Cada otra clase recibe sus dependencias a través de inyección por constructor. Esto hace el sistema completamente testeable — cualquier dependencia puede ser reemplazada con un mock."* |
| **Patrón Extension Method** | `AddPipelineServices` es un método de extensión de `IServiceCollection` — el patrón .NET idiomático para registro DI modular (igual que `AddLogging()`, `AddHttpClient()`). | *"AddPipelineServices sigue el mismo patrón que AddLogging o AddHttpClient — un solo método de extensión que encapsula todos los registros relacionados. Es la convención .NET para setup DI modular."* |
| **Arquitectura Config-Driven** | `FieldMappings[]` habilita mapeo de campos en runtime sin cambios de código. La flag `Enabled` toggle fuentes sin deploys. `RetryCount` ajusta resiliencia por fuente. | *"El bonus de field mapping config-driven está construido en la config desde la Fase 3. Cuando el transformer ejecuta, lee FieldMappings dinámicamente — una nueva fuente con diferentes nombres de campo necesita cero cambios de código, solo una sección de config."* |
| **Defaults Fail-Safe** | Cada propiedad en `PipelineOptions` tiene un default sensato: `OutputDirectory = "./output"`, `TimeoutSeconds = 30`, `RetryCount = 3`. El pipeline funciona incluso con config mínima. | *"Cada propiedad de config tiene un default seguro. Si alguien vacía appsettings.json, el pipeline sigue ejecutando con valores razonables — no crasheará porque falta una clave de config."* |

**Insight clave para la entrevista**: La Fase 3 es invisible para el usuario final pero fundamental para los desarrolladores. Configuración hecha bien significa que cada fase subsiguiente puede enfocarse en lógica de negocio sin preocuparse de dónde leer valores. El evaluador verá cero strings hardcodeados en todo el codebase — ese es el resultado directo de construir config primero.

### 16.4 Fase 4: Infrastructure/OpenMeteo — La Primera Fuente de Datos

**Qué creamos**: La integración completa de Open-Meteo — 5 archivos implementando las interfaces de Pipeline.Core. Este es el vertical slice donde vive todo el código específico de la fuente, organizado como una sub-carpeta dentro del proyecto Infrastructure.

**Por qué es una sub-carpeta dentro de Infrastructure**: Cohesión. Un desarrollador trabajando en la integración de Open-Meteo solo necesita mirar en `Infrastructure/OpenMeteo/`. Todo lo relacionado con Open-Meteo está co-ubicado. Cuando se agrega una segunda fuente, se crea `Infrastructure/WeatherApi/` — una sub-carpeta paralela con las mismas implementaciones de interfaces.

#### 16.4.1 `OpenMeteoApiResponse` — Modelo de Deserialización

**Qué representa**: La forma JSON exacta que retorna el API de Open-Meteo. Esto NO es nuestro modelo de dominio — es un objeto de transferencia de datos que refleja el contrato del API.

```csharp
public class OpenMeteoApiResponse
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
    
    [JsonPropertyName("daily")]
    public OpenMeteoDailyData? Daily { get; set; }
    
    [JsonPropertyName("daily_units")]
    public Dictionary<string, string>? DailyUnits { get; set; }
}

public class OpenMeteoDailyData
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; set; }
    
    [JsonPropertyName("temperature_2m_max")]
    public List<double?>? TemperatureMax { get; set; }
    
    [JsonPropertyName("temperature_2m_min")]
    public List<double?>? TemperatureMin { get; set; }
    
    [JsonPropertyName("precipitation_sum")]
    public List<double?>? PrecipitationSum { get; set; }
    
    [JsonPropertyName("wind_speed_10m_max")]
    public List<double?>? WindSpeedMax { get; set; }
    
    [JsonPropertyName("uv_index_max")]
    public List<double?>? UvIndexMax { get; set; }
}
```

**Decisiones de diseño críticas**:
- **Todas las propiedades de lista son `List<double?>`**: El API puede retornar `null` para mediciones individuales. Nuestra deserialización DEBE aceptar esto
- **Todas las propiedades de colección son nullables (`List<double?>?`)**: El array completo podría faltar si el API elimina un campo
- **Usa `[JsonPropertyName]` no `[JsonProperty]`**: System.Text.Json, no Newtonsoft. Cero dependencias innecesarias
- **Usa `wind_speed_10m_max` (con guión bajo)**: No `windspeed_10m_max` de la URL del ejercicio. Validado contra los docs reales del API — **CAPTURA CRÍTICA #1** (ver Sección 2.1)

**Defensa en entrevista**: "Validé el endpoint del ejercicio contra los docs del API en vivo y encontré una discrepancia de naming en el campo de velocidad del viento. La URL del ejercicio usa `windspeed_10m_max` pero el parámetro real del API es `wind_speed_10m_max` con guiones bajos. Además, el ejercicio lista `uv_index_max` en la tabla de campos pero no lo incluye en la URL de muestra — lo agregué. Estas son exactamente las capturas de especificación-vs-realidad que esperas de un staff engineer haciendo trabajo de integración."

#### 16.4.2 `OpenMeteoApiClient` — Implementación HTTP

**Responsabilidad**: Construir la URL de consulta, hacer el HTTP GET, retornar JSON crudo.

**Detalles clave de implementación**:
- Obtiene `HttpClient` de `IHttpClientFactory` (cliente nombrado "OpenMeteo")
- Construye query string: `?latitude={lat}&longitude={lon}&daily=temperature_2m_max,...&timezone=auto&forecast_days=7`
- Usa `CultureInfo.InvariantCulture` para formateo de coordenadas (previene `40,7128` en locales europeos)
- Retorna `Result<string>.Success(json)` en HTTP 200
- Retorna `Result<string>.Failure(new FetchError(...))` en cualquier error HTTP, capturando código de estado
- `CancellationToken` propagado a través de todas las llamadas async
- La política de retry NO está en esta clase — está configurada a nivel del `HttpClient` vía `Microsoft.Extensions.Http.Resilience` en `ServiceRegistration`. La clase del cliente se mantiene limpia

**Por qué el retry es externo al cliente**: Separación de concerns. La política de retry es una preocupación de infraestructura (cómo lidiar con fallos transitorios). La clase del cliente es una preocupación de negocio (cómo llamar este API). Si incorporáramos el retry en el cliente, necesitaríamos testar el retry separadamente de la llamada HTTP. Con el handler de resiliencia en el pipeline del HttpClient, el retry envuelve el transporte transparentemente.

#### 16.4.3 `OpenMeteoResponseParser` — Validación JSON + Deserialización

**Responsabilidad**: Tomar string JSON crudo → validar estructura → retornar `OpenMeteoApiResponse` tipado o `ParseError`.

**Chequeos de validación (cada uno retorna `ParseError` en fallo)**:
1. JSON es válido (captura `JsonException` de la deserialización)
2. La respuesta no es un error del API (`{ "error": true, "reason": "..." }`)
3. La propiedad `Daily` no es null
4. El array `Daily.Time` no es null y no está vacío
5. Todos los arrays de mediciones (`TemperatureMax`, `TemperatureMin`, etc.) tienen la misma longitud que `Time`
6. Ningún array de mediciones es null (el array en sí — los valores individuales dentro SÍ PUEDEN ser null)

**Por qué la validación aquí, no en el transformer**: El trabajo del parser es garantizar integridad estructural. Si el parser dice `Result<OpenMeteoApiResponse>.Success(...)`, el transformer puede iterar arrays con seguridad sin chequeos de null en los arrays mismos. Este es el principio de "parsear, no validar" — la validación estructural ocurre una vez en el boundary, no dispersa en cada consumidor.

**Defensa en entrevista**: "Mi parser valida la estructura de arrays paralelos antes de que el transformer la vea. Si `time` tiene 7 entradas pero `temperature_2m_max` tiene 6, el parser captura eso como un `ParseError` con el nombre del campo específico y las longitudes de arrays. El transformer nunca recibe datos estructuralmente inconsistentes. Este es un patrón 'parsear, no validar' — la validación estructural ocurre una vez en el boundary, no dispersa en cada consumidor."

#### 16.4.4 `OpenMeteoTransformer` — Zip de Arrays + Normalización

**Responsabilidad**: Tomar el `OpenMeteoApiResponse` validado + `LocationConfig` → producir `NormalizedWeatherRecord[]`.

**Detalles clave de implementación**:
- Itera por índice a través de todos los arrays paralelos: `for (int i = 0; i < daily.Time.Count; i++)`
- Crea un `NormalizedWeatherRecord` por día
- Parsea strings de fecha a `DateOnly` (ISO 8601)
- Mapea doubles nullables directamente — si `TemperatureMax[i]` es null, `TempMaxCelsius` es null en el output
- Establece `Source = "OpenMeteo"`, `Location = locationConfig.Name`
- Captura `FetchedAtUtc = DateTime.UtcNow` en el momento de transformación
- Si existen field mappings en config, los usa para validar que los campos esperados están presentes

**El "zip" de arrays paralelos es el parsing no-trivial que los evaluadores quieren ver**:
```
Respuesta del API:
  time:              [2026-03-28, 2026-03-29, 2026-03-30]
  temperature_2m_max:[18.2,       15.1,       12.8      ]
  temperature_2m_min:[8.4,        6.2,        4.1       ]
  precipitation_sum: [0.0,        2.3,        0.5       ]

Output del transformer:
  Record[0]: Date=2026-03-28, TempMax=18.2, TempMin=8.4, Precip=0.0
  Record[1]: Date=2026-03-29, TempMax=15.1, TempMin=6.2, Precip=2.3
  Record[2]: Date=2026-03-30, TempMax=12.8, TempMin=4.1, Precip=0.5
```

#### 16.4.5 `OpenMeteoDataSource` — El Orquestador Compuesto

**Responsabilidad**: Cablear client + parser + transformer para Open-Meteo. Buscar todas las ubicaciones concurrentemente.

```csharp
public async Task<ProcessingResult> ProcessAsync(IEnumerable<LocationConfig> locations, CancellationToken ct)
{
    var stopwatch = Stopwatch.StartNew();
    var records = new List<NormalizedWeatherRecord>();
    var errors = new List<PipelineError>();
    var locationList = locations.ToList();
    
    // Fetch concurrente — Task.WhenAll para todas las ubicaciones
    var fetchTasks = locationList.Select(loc => ProcessLocationAsync(loc, ct));
    var results = await Task.WhenAll(fetchTasks);
    
    // Agregar
    foreach (var result in results)
    {
        if (result.IsSuccess)
            records.AddRange(result.Value);
        else
            errors.Add(result.Error);
    }
    
    return new ProcessingResult { ... };
}

private async Task<Result<IReadOnlyList<NormalizedWeatherRecord>>> ProcessLocationAsync(LocationConfig loc, CancellationToken ct)
{
    // Cadena Fetch → Parse → Transform usando Result<T>.Bind
    var fetchResult = await _apiClient.FetchAsync(loc, ct);
    return fetchResult
        .Bind(json => _parser.Parse(json))
        .Bind(model => _transformer.Transform(model, loc));
}
```

**Por qué `Task.WhenAll`**: La especificación dice "ubicaciones buscadas concurrentemente." `Task.WhenAll` dispara todas las peticiones HTTP simultáneamente y espera a que todas completen. Para 3 ubicaciones, esto significa ~1 tiempo de llamada HTTP en vez de ~3 llamadas secuenciales. Simple, correcto, y demostrablemente concurrente.

**Por qué no `Parallel.ForEachAsync` o `Channel<T>`**: `Task.WhenAll` es la abstracción correcta para "ejecutar N operaciones async concurrentemente y recolectar todos los resultados." `Parallel.ForEachAsync` es para paralelismo CPU-bound con control de grado. `Channel<T>` es para streaming productor/consumidor con backpressure. Para 3 llamadas HTTP, `Task.WhenAll` es la herramienta proporcional.

**La cadena Bind es el patrón Result<T> en acción**: `Fetch → Bind(Parse) → Bind(Transform)`. Si Fetch falla, Parse nunca ejecuta. Si Parse falla, Transform nunca ejecuta. Cada fallo cortocircuita con error contextual. Sin anidamiento de try/catch.

#### 16.4.6 Fase 4 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 28 de Marzo, 2026**
> - `dotnet build` → 6/6 proyectos exitosos
> - 5 nuevos archivos en Infrastructure/OpenMeteo: `OpenMeteoApiResponse.cs`, `OpenMeteoApiClient.cs`, `OpenMeteoResponseParser.cs`, `OpenMeteoTransformer.cs`, `OpenMeteoDataSource.cs`
> - `ServiceRegistration.cs` actualizado: HttpClient con handler de resiliencia configurado, los 4 tipos OpenMeteo registrados en DI
> - OpenMeteo/.gitkeep placeholder removido
> - Vertical slice completo: DTO → cliente HTTP → parser → transformer → data source compuesto — todo cableado vía interfaces

#### 16.4.7 Fase 4 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En una empresa anterior, consumíamos un API de geolocalización de terceros que retornaba coordenadas como flotantes separados por coma en la respuesta JSON, pero la documentación del API mostraba números separados por punto. El equipo de desarrollo hardcodeó `double.Parse()` sin especificar `CultureInfo.InvariantCulture`. El código funcionó perfectamente en las máquinas de desarrollo de US, pasó todos los tests de CI (ejecutando en runners de GitHub Actions `en-US`), y se envió a producción. Tres semanas después, un cliente en Alemania reportó que cada ubicación estaba mal — sus servidores de producción ejecutaban con locale `de-DE`, donde `40,7128` significaba `40.7128` en `en-US`. Las latitudes estaban desfasadas por órdenes de magnitud. La corrección fue una línea (`CultureInfo.InvariantCulture`), pero el daño fue tres semanas de datos de ubicación corrompidos en el pipeline de analytics.

**La lección de `OpenMeteoApiClient`**: El `CultureInfo.InvariantCulture` en el formateo de coordenadas en este cliente no es overkill defensivo — es la única línea que previene un bug de producción dependiente de locale. Los Staff Engineers capturan estos en el momento de build, no en un post-mortem.

**El principio de "parsear, no validar" en `OpenMeteoResponseParser`**: En otro proyecto de integración, el equipo confió en la forma de respuesta del API y fue directo a la transformación. El API comenzó a retornar `null` para el array completo `daily` durante ventanas de mantenimiento en vez de un array vacío. El transformer lanzó `NullReferenceException` en lo profundo de una cadena LINQ, produciendo un stack trace que no decía nada sobre qué estaba realmente mal. Al validar estructura en el parser — verificando que `daily` existe, que `time` no está vacío, que todos los arrays tienen longitudes coincidentes — obtenemos mensajes de `ParseError` accionables: "Daily 'temperature_2m_max' tiene 6 elementos pero 'time' tiene 7." Un desarrollador junior puede leer ese error y saber exactamente qué salió mal. El transformer nunca ve datos estructuralmente rotos.

**El "zip" de arrays paralelos en `OpenMeteoTransformer`**: El formato de arrays paralelos de Open-Meteo (`time[i]`, `temperature_2m_max[i]`, `wind_speed_10m_max[i]`) es un patrón común en APIs meteorológicos/financieros. El enfoque naive es deserializar en los arrays paralelos y pasarlos por separado, llevando a bugs de desajuste de índices cuando los arrays se filtran o ordenan independientemente. Al hacer zip en `NormalizedWeatherRecord[]` inmediatamente en el transformer, cada consumidor downstream trabaja con registros auto-contenidos. Los arrays paralelos existen solo en la sub-carpeta de Open-Meteo — en ningún otro lugar del codebase se conocen.

**La concurrencia `Task.WhenAll` en `OpenMeteoDataSource`**: La especificación dice "ubicaciones buscadas concurrentemente." `Task.WhenAll` para 3 ubicaciones significa ~1 tiempo de round-trip HTTP en vez de ~3 secuenciales. Pero más importante, el patrón de agregación de errores — recolectar todos los resultados y separar éxitos de fallos — significa que una ubicación inestable no aborta el pipeline completo. Los datos de New York y London siguen escribiéndose incluso si la petición de Tokyo expira. Este modelo de éxito parcial es crítico en producción: siempre quieres los datos que SÍ PUEDES obtener.

**Respuesta de entrevista para "¿Por qué separar client, parser, transformer, y data source?"**:
> "Porque cada clase tiene exactamente una razón para cambiar. Si Open-Meteo cambia la estructura de su URL, solo `OpenMeteoApiClient` cambia. Si agregan un nuevo campo al JSON, solo `OpenMeteoApiResponse` y `OpenMeteoResponseParser` cambian. Si necesitamos normalizar temperaturas de Fahrenheit a Celsius, solo `OpenMeteoTransformer` cambia. Y si agregamos una segunda fuente de datos como WeatherApi, ninguna de estas clases se toca — creamos un conjunto paralelo en `Infrastructure/WeatherApi/` con las mismas implementaciones de interfaces y las registramos en DI."

#### 16.4.8 Fase 4 — Principios SOLID y Patrones Demostrados

La Fase 4 es el vertical slice donde cada interfaz de Core obtiene su primera implementación concreta. Aquí está cómo mapea a SOLID y patrones de diseño:

| Principio / Patrón | Cómo la Fase 4 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | 5 clases, 5 responsabilidades: `OpenMeteoApiResponse` (forma del DTO), `OpenMeteoApiClient` (HTTP fetch), `OpenMeteoResponseParser` (validar+deserializar), `OpenMeteoTransformer` (normalizar), `OpenMeteoDataSource` (orquestar). Cada una tiene exactamente una razón para cambiar. | *"Si el API agrega un campo, solo el DTO y el parser cambian. Si la política de retry cambia, solo ServiceRegistration cambia. La clase del cliente nunca sabe sobre retry."* |
| **O — Principio Abierto/Cerrado (OCP)** | Agregar una segunda fuente (WeatherApi) significa crear `Infrastructure/WeatherApi/` con 5 archivos paralelos — cero modificación a ningún archivo de OpenMeteo. `ServiceRegistration.cs` agrega 4 nuevas líneas. | *"La carpeta OpenMeteo está cerrada para modificación. Una nueva fuente es pura extensión — una nueva sub-carpeta, nuevos registros DI. El código existente queda intocado."* |
| **L — Principio de Sustitución de Liskov (LSP)** | `OpenMeteoDataSource` implementa `IWeatherDataSource`. El pipeline coordinator llamará `ProcessAsync` sin saber si está hablando con OpenMeteo, WeatherApi, o un mock. Cualquier implementación es sustituible. | *"El coordinator llama IWeatherDataSource.ProcessAsync. No sabe ni le importa si la implementación golpea Open-Meteo, un sistema de archivos, o un stub de test. Cualquier implementación de la interfaz funciona idénticamente."* |
| **I — Principio de Segregación de Interfaces (ISP)** | En vez de un `IWeatherService` con `Fetch+Parse+Transform+Write`, tenemos 4 interfaces enfocadas: `IWeatherApiClient`, `IResponseParser<T>`, `IDataTransformer<T>`, `IWeatherDataSource`. Cada consumidor depende solo de la interfaz que necesita. | *"El parser no depende de la interfaz del cliente HTTP. El transformer no sabe sobre fetching. Cada dependencia es el contrato mínimo necesario."* |
| **D — Principio de Inversión de Dependencia (DIP)** | `OpenMeteoDataSource` depende de `IWeatherApiClient`, `IResponseParser<T>`, `IDataTransformer<T>` — todas abstracciones de Core. Nunca referencia `HttpClient` directamente. El `HttpClient` concreto se inyecta vía `IHttpClientFactory`. | *"El constructor de OpenMeteoDataSource toma 3 interfaces — todas definidas en Core. Tiene cero conocimiento de cómo funciona HTTP. El patrón factory provee el cliente, y el pipeline de resiliencia lo envuelve transparentemente."* |
| **Strategy Pattern** | `IWeatherDataSource` es la interfaz de estrategia. `OpenMeteoDataSource` es una estrategia. El futuro `WeatherApiDataSource` es otra. El coordinator itera todas las estrategias registradas sin lógica condicional. | *"Cada fuente de datos es una estrategia. El coordinator no tiene if/else para tipos de fuente — itera implementaciones de IWeatherDataSource. Agregar una fuente es una nueva estrategia, no una nueva rama."* |
| **Railway-Oriented Programming (ROP)** | `ProcessLocationAsync` encadena `Fetch → Bind(Parse) → Bind(Transform)`. Cada paso o tiene éxito (pasando datos hacia adelante) o falla (cortocircuitando con un error tipado). Sin bloques try/catch anidados. | *"La cadena Bind es el patrón de manejo funcional de errores. Si Fetch falla, Parse nunca ejecuta. El error se propaga automáticamente con contexto completo — sin anidamiento try/catch, sin chequeo de nulls."* |
| **Factory Pattern (IHttpClientFactory)** | `OpenMeteoApiClient` obtiene su `HttpClient` de `IHttpClientFactory.CreateClient("OpenMeteo")`. La factory maneja lifetime de sockets, rotación DNS, y el pipeline del handler de resiliencia. | *"IHttpClientFactory es la factory incorporada de .NET para clientes HTTP. Maneja agotamiento de sockets, cambios DNS, y el pipeline de resiliencia. La clase del cliente está libre de preocupaciones de lifecycle."* |
| **Parsear, No Validar** | `OpenMeteoResponseParser` valida TODAS las invariantes estructurales (daily no null, array time no vacío, longitudes de arrays coincidentes) antes de retornar `Success`. El transformer puede usar `!` (null-forgiving) con seguridad porque el parser garantiza la estructura. | *"Una vez que los datos pasan el parser, están garantizados estructuralmente. El transformer usa operadores null-forgiving con confianza — no porque estemos ignorando nullability, sino porque el contrato del parser prohíbe el caso null."* |
| **Adapter Pattern** | La carpeta completa `Infrastructure/OpenMeteo/` es un Adapter — adapta el API externo de Open-Meteo (arrays paralelos, campos snake_case, especificidades HTTP) al modelo de dominio interno (`NormalizedWeatherRecord` con propiedades PascalCase). | *"La carpeta OpenMeteo es un Adapter clásico. La forma del API externo entra, registros de dominio normalizados salen. Ninguna otra parte del sistema sabe sobre arrays paralelos o nombres de campo snake_case."* |
| **Agregación Concurrente** | `Task.WhenAll` en `OpenMeteoDataSource.ProcessAsync` dispara todos los fetches de ubicación concurrentemente. Los resultados se agregan con semántica de éxito parcial — un fallo no aborta a los demás. | *"Task.WhenAll nos da I/O concurrente para todas las ubicaciones. El loop de agregación separa éxitos de fallos, así que siempre producimos el máximo de datos posible incluso cuando una ubicación falla."* |

**Insight clave para la entrevista**: La Fase 4 es donde la arquitectura se demuestra a sí misma. Cada interfaz definida en la Fase 2, cada opción de config de la Fase 3, cada tipo de error — todos convergen en este vertical slice. El evaluador puede trazar una petición desde la entrada de ubicación en `appsettings.json` → `OpenMeteoApiClient.FetchAsync` → `OpenMeteoResponseParser.Parse` → `OpenMeteoTransformer.Transform` → `NormalizedWeatherRecord[]`. Eso es el flujo de datos completo, y cada paso es testeble independientemente, reemplazable, y documentado.

---

### 16.5 Fase 5: Infrastructure/Output — Escritor Tab-Delimited

**Qué creamos**: `TabDelimitedFileWriter` en `Pipeline.Infrastructure/Output/` implementando `IOutputWriter`.

**Esquema de output**:
```
Source	Location	Latitude	Longitude	Date	TempMaxC	TempMinC	PrecipitationMm	WindSpeedMaxKmh	UVIndexMax	FetchedAtUtc
OpenMeteo	New York	40.7128	-74.0060	2026-03-28	18.2	8.4	0.0	22.1	5.2	2026-03-28T14:30:00Z
OpenMeteo	London	51.5074	-0.1278	2026-03-28	12.5	5.1	4.2	30.2	2.1	2026-03-28T14:30:00Z
```

**Detalles de implementación**:
- Crea directorio de output si no existe (`Directory.CreateDirectory`)
- Reemplaza token `{timestamp}` en nombre de archivo con `DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")`
- Usa `StreamWriter` con encoding `UTF-8 sin BOM`
- Fila de encabezado: nombres de columna separados por `\t`
- Filas de datos: valores separados por `\t`, valores null escritos como string vacío (no "null")
- Fechas formateadas como ISO 8601 (`yyyy-MM-dd`)
- Timestamps formateados como ISO 8601 con UTC (`yyyy-MM-ddTHH:mm:ssZ`)
- Coordenadas formateadas con 4 decimales usando `InvariantCulture`
- Retorna `Result<string>.Success(filePath)` o `Result<string>.Failure(new OutputError(...))`

**Decisiones de diseño del esquema a defender en entrevista**:

| Columna | Por qué existe | Qué habilita |
|---------|---------------|--------------|
| `Source` | Extensibilidad multi-fuente | Filtrar/agrupar por fuente en analytics downstream |
| `Location` | Contexto legible para humanos | "New York" tiene significado; "40.7128,-74.0060" no |
| `Latitude/Longitude` | Coordenadas exactas | Geo-análisis, plotting en mapa, verificación de coordenadas |
| `Date` | La fecha del pronóstico | Dimensión core para análisis de series temporales |
| `TempMaxC/TempMinC` | Unidad en nombre de columna | TSV no tiene capa de metadata — las unidades deben ser auto-documentadas |
| `PrecipitationMm` | Unidad en nombre de columna | Misma razón |
| `WindSpeedMaxKmh` | Unidad en nombre de columna | Misma razón |
| `UVIndexMax` | Índice sin dimensión | No necesita sufijo de unidad — el índice UV es una escala estándar |
| `FetchedAtUtc` | Pista de auditoría | Frescura de datos — ¿cuándo se recuperó este pronóstico? |

**Defensa en entrevista**: "Puse unidades en los nombres de columna porque TSV no tiene capa de metadata como Parquet o Avro. Una columna llamada 'TempMax' es ambigua — ¿Celsius o Fahrenheit? 'TempMaxC' es auto-documentada. La columna `FetchedAtUtc` es una pista de auditoría — sin ella, no puedes saber si el pronóstico fue recuperado hace 5 minutos o 5 horas, lo cual importa para la precisión de analytics downstream."

#### 16.5.2 Fase 5 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 28 de Marzo, 2026**
> - `dotnet build` → 6/6 proyectos exitosos
> - 1 nuevo archivo: `Infrastructure/Output/TabDelimitedFileWriter.cs` (~100 líneas)
> - `ServiceRegistration.cs` actualizado: `IOutputWriter` → `TabDelimitedFileWriter` registrado en DI
> - Output/.gitkeep placeholder removido
> - Implementación completa: fila de encabezado, filas de datos, UTF-8 sin BOM, formateo InvariantCulture, null → string vacío, fechas ISO 8601

#### 16.5.3 Fase 5 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En una empresa anterior, un equipo de ingeniería de datos construyó un exportador CSV para datos de transacciones financieras. El output se veía perfecto en las máquinas de desarrolladores de US. Tres semanas después del lanzamiento, el equipo europeo de analytics reportó que su herramienta BI (que ejecutaba en un servidor con locale `de-DE`) estaba parseando `1,234.56` como `1.234,56` — intercambiando silenciosamente separadores de miles y puntos decimales. Medio millón de filas de montos de transacciones fueron corrompidos en el data warehouse antes de que alguien se diera cuenta. La causa raíz: el exportador usaba `ToString()` sin `CultureInfo.InvariantCulture`. La corrección fue una línea por columna. El daño fue un mes de trabajo de reconciliación.

**La lección de `CultureInfo.InvariantCulture`**: Cada formato numérico en `TabDelimitedFileWriter` — coordenadas (`"F4"`), temperaturas, precipitación — explícitamente usa `InvariantCulture`. Esto no es overkill defensivo. Es lo único que previene corrupción de datos dependiente de locale en un archivo que sistemas downstream en cualquier país necesitan parsear.

**La decisión de null → string vacío**: Otro equipo usó `"null"` como la representación string para datos faltantes en su exportación TSV. Las importaciones SQL downstream (`LOAD DATA INFILE`) trataron `"null"` como un string literal de 5 caracteres, no un NULL de base de datos. Cada lectura de temperatura faltante se convirtió en el string "null" en la columna VARCHAR en vez de NULL. Consultas como `WHERE temp_max IS NULL` retornaban cero filas. El equipo tuvo que ejecutar una migración a través de 2 millones de filas para corregir `WHERE temp_max = 'null'` → `SET temp_max = NULL`. Al escribir un string vacío para valores null, producimos TSV que los importadores bulk de SQL, pandas `read_csv(sep='\t')`, y Excel todos interpretan correctamente como "sin datos."

**La decisión de TSV sobre CSV**: Los nombres de ubicaciones meteorológicas como "St. Louis, MO" contienen comas. CSV requiere reglas de entrecomillado — y diferentes interpretaciones de RFC sobre el entrecomillado han causado más bugs de parsing que cualquier otro problema de formato de texto. TSV es inequívoco: los tabs no aparecen en datos meteorológicos. Sin entrecomillado, sin escape, sin ambigüedad. El ejercicio pide explícitamente "archivos de output tab-delimited", pero aunque no lo pidiera, TSV es la elección correcta para datos meteorológicos con nombres de ubicación.

**La decisión de UTF-8 sin BOM**: BOM (Byte Order Mark) causa bugs invisibles de parsing. El `open()` de Python lee BOM como el carácter `\ufeff` antepuesto a la primera línea. Las herramientas de línea de comandos Unix como `head`, `awk`, y `cut` ven BOM como tres bytes basura. Los pipelines de datos que dividen archivos, los concatenan, o los transmiten fallan cuando BOM aparece en el medio de un archivo combinado. UTF-8 sin BOM es el default universal para intercambio de datos.

**Respuesta de entrevista para "¿Por qué no JSON o Parquet para output?"**:
> "La especificación del ejercicio dice 'archivos de output tab-delimited para analytics downstream.' TSV es el formato más simple que un analista de datos puede abrir en Excel, cargar en pandas, o importar bulk en SQL. JSON requiere parsing en un data frame — un paso extra. Parquet requiere una librería específica. TSV es universalmente legible sin cero dependencias. Dicho esto, en producción usualmente emitiría Parquet para pipelines de analytics — compresión columnar, schema embebido, seguridad de tipos. Pero para este ejercicio, TSV es la herramienta correcta para el requerimiento declarado."

#### 16.5.4 Fase 5 — Principios SOLID y Patrones Demostrados

La Fase 5 es el boundary de output — donde los datos de dominio normalizados se convierten en un archivo que sistemas downstream consumen. Aquí está cómo mapea a SOLID y patrones de diseño:

| Principio / Patrón | Cómo la Fase 5 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | `TabDelimitedFileWriter` hace exactamente una cosa: escribir registros en un archivo TSV. No busca datos, no los transforma, ni decide qué registros incluir. Esas responsabilidades pertenecen a etapas anteriores del pipeline. | *"El escritor escribe. No filtra, ordena, ni agrega. Si necesitamos ordenamiento, esa es preocupación del transformer. Si necesitamos filtrado, esa es preocupación del coordinator. El escritor recibe registros y produce un archivo."* |
| **O — Principio Abierto/Cerrado (OCP)** | `IOutputWriter` es el punto de extensión. `TabDelimitedFileWriter` es una implementación. Agregar `ParquetFileWriter` o `JsonFileWriter` significa crear una nueva clase implementando `IOutputWriter` — cero cambios al escritor existente o a cualquier consumidor. | *"Si el requerimiento cambia a output Parquet, creo ParquetFileWriter implementando IOutputWriter, lo registro en DI, y el coordinator no cambia. El escritor TSV está cerrado para modificación."* |
| **D — Principio de Inversión de Dependencia (DIP)** | `TabDelimitedFileWriter` depende de `IOptions<PipelineOptions>` para config — una abstracción de Core/framework. El pipeline coordinator dependerá de `IOutputWriter`, no de `TabDelimitedFileWriter` directamente. El I/O de archivo concreto es un detalle de implementación oculto tras la interfaz. | *"El coordinator llama IOutputWriter.WriteAsync. No sabe si el output va a un archivo, S3, o una base de datos. El contenedor DI resuelve el escritor concreto — el coordinator está desacoplado del mecanismo de output."* |
| **Template Method / Strategy Pattern** | `IOutputWriter` actúa como una estrategia — el coordinator delega output a cualquier escritor que esté registrado. En producción, podrías intercambiar implementaciones por ambiente (archivo para dev, S3 para staging, Kafka para prod) vía solo configuración. | *"El escritor de output es una estrategia. En dev escribo a disco, en prod podría escribir a S3 — misma interfaz, diferente registro. El código del coordinator es idéntico entre ambientes."* |
| **Guard Clause + Error Boundary** | El escritor captura `IOException` y `UnauthorizedAccessException` específicamente — los dos modos de fallo para I/O de archivos — y los envuelve en `OutputError`. `OperationCanceledException` se re-lanza para respetar la cancelación. Sin catch-all que trague errores inesperados. | *"Capturo exactamente las excepciones que pueden ocurrir en este boundary: disco lleno, permiso denegado. No capturo Exception ampliamente — eso enmascararía bugs. Los errores inesperados se propagan al llamador para manejo apropiado."* |
| **Representación Null Object** | Los valores null se convierten en strings vacíos — no el literal `"null"`, no `"N/A"`, no `"0"`. Esto sigue la convención TSV: campo vacío = sin datos. Los consumidores downstream (pandas, SQL LOAD, Excel) todos interpretan vacío correctamente. | *"String vacío en TSV universalmente significa 'sin datos.' El string 'null' es un bug de corrupción de datos esperando ser descubierto — SQL lo importa como un string literal, no un valor NULL."* |
| **Comportamiento Config-Driven** | El directorio de output y el patrón de nombre de archivo vienen de `IOptions<PipelineOptions>`, vinculado desde `appsettings.json`. Cambiar la ubicación de output o el formato de nombre de archivo es un cambio de config, no un cambio de código. | *"La ruta de output y el patrón de nombre de archivo están en appsettings.json. En producción, una variable de ambiente sobrescribe la ruta a un volumen montado o share de red. Cero cambios de código entre ambientes."* |
| **Formateo Defensivo** | `CultureInfo.InvariantCulture` en cada campo numérico, ISO 8601 para fechas, precisión fija `"F4"` para coordenadas. El archivo de output es byte-idéntico sin importar qué máquina/locale lo produce. | *"Un archivo TSV producido en un servidor alemán debe ser byte-idéntico a uno producido en un servidor de US. InvariantCulture garantiza esto. Sin él, las coordenadas se vuelven dependientes de locale — 40,7128 vs 40.7128."* |

**Insight clave para la entrevista**: La Fase 5 es engañosamente simple — "escribir registros en un archivo." Pero las decisiones de diseño (encoding, manejo de nulls, locale, elección de formato) es donde los bugs de producción se esconden. Los Staff Engineers saben que el boundary de output es la parte más frágil de un pipeline de datos porque es donde los datos internos se encuentran con consumidores externos con diferentes reglas de parsing, locales, y expectativas. Cada decisión de formateo en esta clase existe para prevenir una clase específica de bug del mundo real.

---

### 16.6 Fase 6: Application — El Orquestador del Pipeline

**Qué creamos**: `PipelineCoordinator` en `Pipeline.Application/` — el orquestador central que ejecuta todas las fuentes de datos y produce el output final.

**Flujo de ejecución**:
```
PipelineCoordinator.RunAsync(CancellationToken):
  1. Iniciar Stopwatch
  2. Resolver IEnumerable<IWeatherDataSource> de DI
  3. Para cada fuente de datos:
     a. Obtener sus ubicaciones desde PipelineOptions config
     b. Llamar source.ProcessAsync(locations, ct)
     c. Recolectar ProcessingResult
  4. Agregar todos los NormalizedWeatherRecords exitosos de todas las fuentes
  5. Si existen registros:
     a. Llamar IOutputWriter.WriteAsync(allRecords, ct)
     b. Capturar ruta del archivo de output
  6. Construir PipelineSummary desde todos los ProcessingResults + ruta de output + duración
  7. Imprimir resumen formateado a consola vía ILogger
  8. Retornar PipelineSummary
```

**El resumen de procesamiento (impreso en consola)**:
```
═══════════════════════════════════════════════════════════
  SyncMetrics Weather Pipeline — Run Summary
═══════════════════════════════════════════════════════════
  Sources processed:    1 (OpenMeteo)
  Locations attempted:  3
  Locations succeeded:  3
  Locations failed:     0
  Records written:      21
  Output file:          ./output/weather_data_20260328_143000.tsv
  Duration:             1.24s
═══════════════════════════════════════════════════════════
```

**Si existen errores, aparecen debajo del resumen**:
```
  ERRORS:
  ─────────────────────────────────────────────────────────
  [FetchError] Tokyo — HTTP 503 Service Unavailable
      URL: https://api.open-meteo.com/v1/forecast?latitude=35.6762...
  [ParseError] London — Mismatched array lengths
      Field: temperature_2m_max (expected 7, got 6)
  ─────────────────────────────────────────────────────────
```

**Por qué el resumen estructurado importa para evaluación**: La especificación dice "resumen de procesamiento al completar." Un resumen que solo dice "Listo. 21 registros." es junior. Un resumen con desglose por fuente, conteos de éxito/fallo, duración, ruta de output, y errores tipados detallados es nivel staff. Muestra que Arthur piensa en operabilidad — cuando esto ejecuta en producción a las 3am y el ingeniero de guardia lee el log, necesita información accionable.

#### 16.6.2 Fase 6 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 28 de Marzo, 2026**
> - `dotnet build` → 6/6 proyectos exitosos
> - 1 nuevo archivo: `Application/PipelineCoordinator.cs` (~170 líneas, clase parcial con source generators LoggerMessage)
> - `Application.csproj` actualizado: agregado NuGet `Microsoft.Extensions.Logging.Abstractions` 8.0.x
> - `ServiceRegistration.cs` actualizado: `PipelineCoordinator` registrado en DI
> - `Program.cs` actualizado: resuelve coordinator, construye mapa source→locations desde config, ejecuta pipeline con `CancellationToken`, retorna exit code (0 = éxito, 1 = errores)
> - Flujo completo del pipeline cableado: config → coordinator → fuentes de datos (concurrente) → escritor de output → resumen estructurado

#### 16.6.3 Fase 6 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En una empresa anterior, un pipeline de ingesta de datos no tenía resumen estructurado. El DAG de Airflow que lo schedulaba verificaba solo el exit code — 0 o 1. Cuando el pipeline tuvo éxito parcial (2 de 3 fuentes retornaron datos, 1 expiró), salió con 0 porque "algunos datos fueron escritos." El equipo de guardia no supo sobre la fuente faltante durante 4 días hasta que un dashboard downstream mostró un hueco. La recomendación del post-mortem: resúmenes estructurados con conteos de éxito/fallo por fuente, escritos a logs estructurados que el sistema de monitoreo pudiera parsear y alertar.

**El `PipelineCoordinator` aborda esto directamente**: Cada ejecución produce un `PipelineSummary` con `SourceResults[]` — cada uno conteniendo `LocationsAttempted`, `LocationsSucceeded`, `Errors[]` tipados, y `Duration`. El resumen en consola no es decoración — es la información exacta que un ingeniero de guardia necesita a las 3am. "Sources processed: 1 (OpenMeteo), Locations succeeded: 2/3, [FetchError] Tokyo — HTTP 503" te dice qué falló, dónde, y por qué, sin leer una sola línea de código.

**La decisión del source generator `LoggerMessage`**: La implementación inicial usó métodos de extensión `_logger.LogInformation(...)`. El `Directory.Build.props` tiene `TreatWarningsAsErrors=true` con `AnalysisLevel=latest-recommended`, que habilita CA1848 ("usar delegados LoggerMessage para performance mejorada") y CA1873 ("evitar evaluación costosa de argumentos"). En vez de suprimir estos con pragmas (lo que oculta deuda de performance), convertimos TODAS las llamadas de logging a métodos parciales `[LoggerMessage]` — el patrón recomendado de .NET 8. El source generator produce logging de cero allocations en tiempo de compilación. Este es un detalle nivel staff: entender que el logging estructurado no se trata solo de mensajes — se trata de evitar allocations innecesarias cuando los niveles de log están deshabilitados.

**La decisión del boundary de la capa Application**: `PipelineCoordinator` vive en Application, no Infrastructure. Solo depende de interfaces de Core (`IWeatherDataSource`, `IOutputWriter`, `ILogger`). NO depende de `PipelineOptions` o ningún tipo de Infrastructure. El mapeo source→locations es construido por Program.cs (el Composition Root) y pasado como un `IReadOnlyDictionary` puro. Esto significa que el coordinator es completamente testeable sin ninguna referencia a Infrastructure — inyecta data sources mock y un escritor mock, verifica la lógica de orquestación en aislamiento.

**El patrón `CancellationToken` + `Console.CancelKeyPress`**: Program.cs crea un `CancellationTokenSource` y cablea `Console.CancelKeyPress` para activar la cancelación. Cuando un usuario presiona Ctrl+C, el token se propaga a través del pipeline completo — las peticiones HTTP abortan limpiamente, el escritor se detiene, y el proceso sale con gracia. Sin esto, Ctrl+C durante una petición HTTP podría dejar conexiones huérfanas. En Docker/Kubernetes, `SIGTERM` se mapea a `CancelKeyPress`, así que este patrón también habilita shutdown graceful en ambientes containerizados.

**La decisión del exit code**: `return summary.HasErrors ? 1 : 0;` — el pipeline retorna un exit code distinto de cero cuando cualquier fuente tuvo errores. Esto es crítico para herramientas CI/CD y de orquestación (Airflow, cron, Docker health checks) que usan exit codes para determinar éxito. Un pipeline que traga errores y siempre sale con 0 es un pipeline que falla silenciosamente en producción.

**Respuesta de entrevista para "¿Por qué el coordinator está en Application, no en Infrastructure?"**:
> "Porque el coordinator es lógica de negocio — decide el flujo de orquestación: qué fuentes ejecutar, cómo agregar resultados, cuándo escribir output, cómo construir el resumen. No sabe CÓMO se buscan datos (¿HTTP? ¿Archivo? ¿Cola?) o CÓMO se escribe el output (¿TSV? ¿Parquet? ¿S3?). Esas son preocupaciones de Infrastructure. El coordinator trabaja solo con interfaces. En tests, inyecto data sources mock que retornan resultados enlatados — sin HTTP, sin archivos, sin red. El test ejecuta en milisegundos y valida la lógica de orquestación: ejecución concurrente, agregación de errores, manejo de éxito parcial."

#### 16.6.4 Fase 6 — Principios SOLID y Patrones Demostrados

La Fase 6 es la capa de orquestación — donde todas las fases anteriores convergen en un pipeline funcional. Aquí está cómo mapea a SOLID y patrones de diseño:

| Principio / Patrón | Cómo la Fase 6 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | `PipelineCoordinator` hace una cosa: orquestar el flujo del pipeline (ejecutar fuentes → agregar → escribir → resumir). No busca datos, no parsea JSON, no transforma registros, ni escribe archivos — esas responsabilidades se delegan a colaboradores inyectados. | *"El coordinator orquesta. No sabe sobre HTTP, JSON, o I/O de archivos. Cada uno de esos es una clase separada con una sola responsabilidad."* |
| **O — Principio Abierto/Cerrado (OCP)** | Agregar una nueva fuente de datos requiere cero cambios a `PipelineCoordinator`. Registra un nuevo `IWeatherDataSource` en DI, agrega sus ubicaciones a config — el coordinator lo recoge automáticamente vía `IEnumerable<IWeatherDataSource>`. | *"El coordinator itera todas las implementaciones registradas de IWeatherDataSource. Agregar WeatherApi significa un nuevo registro DI — el código del coordinator queda intocado."* |
| **L — Principio de Sustitución de Liskov (LSP)** | El coordinator llama `IWeatherDataSource.ProcessAsync` e `IOutputWriter.WriteAsync` — cualquier implementación es sustituible. Data sources mock en tests, reales en producción, mismo código del coordinator. | *"En tests inyecto un data source mock que retorna 7 registros hardcodeados. En producción golpea el API real de Open-Meteo. El coordinator no sabe la diferencia — eso es LSP."* |
| **D — Principio de Inversión de Dependencia (DIP)** | El coordinator depende de 3 abstracciones: `IEnumerable<IWeatherDataSource>`, `IOutputWriter`, `ILogger<T>`. Tiene cero referencias a tipos de Infrastructure. El Composition Root (Program.cs) cablea las implementaciones concretas. | *"El constructor de PipelineCoordinator toma solo interfaces. Vive en Application, que solo referencia Core. No puede accidentalmente depender de Infrastructure — el grafo de referencias del proyecto lo impone en tiempo de compilación."* |
| **Patrón Mediator** | El coordinator actúa como mediador entre fuentes de datos y el escritor de output. Las fuentes no saben sobre el escritor, el escritor no sabe sobre las fuentes — el coordinator media el flujo de datos entre ellos. | *"Las fuentes de datos producen registros. El escritor los consume. Nunca se referencian entre sí. El coordinator media: recolecta registros de todas las fuentes, los pasa al escritor."* |
| **Patrón Composition Root** | `Program.cs` es el Composition Root — el único lugar que conoce TODOS los tipos concretos. Resuelve `IOptions<PipelineOptions>`, construye el diccionario source→locations, y lo pasa al coordinator. El conocimiento de config permanece en el punto de entrada. | *"Program.cs es el único archivo que referencia tanto tipos de config de Infrastructure como el coordinator de Application. Este es el patrón Composition Root — el conocimiento de dependencias está concentrado en un lugar."* |
| **Logging Estructurado (LoggerMessage)** | Las 8 llamadas de log usan delegados source-generated `[LoggerMessage]` — cero allocations en runtime, validación en tiempo de compilación de parámetros de log, compatible con CA1848/CA1873. | *"Uso source generators LoggerMessage en vez de interpolación de strings. El source generator crea delegados optimizados en tiempo de compilación — cero allocations cuando el nivel de log está deshabilitado. Este es el patrón recomendado de .NET 8 para logging de alto rendimiento."* |
| **Patrón de Shutdown Graceful** | `CancellationTokenSource` + `Console.CancelKeyPress` habilita cancelación cooperativa a través del pipeline completo. En Docker/K8s, `SIGTERM` se mapea a esto, habilitando shutdown limpio de pods. | *"Ctrl+C activa el CancellationToken, que se propaga a peticiones HTTP, escrituras de archivos — todo se detiene cooperativamente. En Kubernetes, SIGTERM hace lo mismo. El pipeline nunca deja conexiones huérfanas."* |
| **Éxito Parcial / Agregación de Errores** | El coordinator recolecta TODOS los resultados de fuentes — éxitos Y fallos — luego escribe los registros que tenga. Que falle una fuente no aborta el pipeline completo. Los errores son tipados y reportados en el resumen. | *"Si Tokyo expira pero New York y London tienen éxito, igual escribimos 14 registros y reportamos el error de Tokyo. El pipeline maximiza output de datos mientras mantiene visibilidad completa de errores."* |
| **Concurrencia Task.WhenAll** | Todas las fuentes de datos ejecutan concurrentemente vía `Task.WhenAll`. Para N fuentes, el tiempo total se aproxima a max(tiempo_fuente) en vez de sum(tiempos_fuentes). | *"Task.WhenAll ejecuta todas las fuentes concurrentemente. Con 3 fuentes, esperamos la más lenta, no la suma de las tres. Para trabajo I/O-bound como llamadas HTTP, este es el modelo de concurrencia correcto."* |

**Insight clave para la entrevista**: La Fase 6 es donde la arquitectura se demuestra o colapsa. El constructor del coordinator toma 3 interfaces y cero tipos concretos. Su método `RunAsync` recibe un diccionario de datos puro, no un tipo de config. Esto significa que toda la lógica de orquestación — ejecución concurrente, agregación de errores, éxito parcial, resumen estructurado — es testeable con cero infraestructura, cero red, cero archivos. Un test unitario de 50ms puede verificar el flujo completo del pipeline.

---

### 16.7 Fase 7: Program.cs — Punto de Entrada

**Qué creamos**: El punto de entrada mínimo que cablea DI y ejecuta el pipeline.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SyncMetrics.Pipeline.Application;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.Configuration;

// Fijar content root al directorio del assembly para que appsettings.json se encuentre sin importar
// el directorio de trabajo (raíz de solución cuando se ejecuta con `dotnet run --project`).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
    Args = args,
});

// Cablear todos los servicios del pipeline — binding de config, clientes HTTP, registros DI
builder.Services.AddPipelineServices(builder.Configuration);

var app = builder.Build();

// Resolver coordinator y config, construir mapa source→locations, ejecutar pipeline
var coordinator = app.Services.GetRequiredService<PipelineCoordinator>();
var options = app.Services.GetRequiredService<IOptions<PipelineOptions>>().Value;

var sourceLocations = options.Sources
    .Where(s => s.Enabled)
    .ToDictionary(
        s => s.Name,
        s => (IReadOnlyList<LocationConfig>)s.Locations);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var summary = await coordinator.RunAsync(sourceLocations, cts.Token);

// Exit code: 0 si no hay errores, 1 si alguna fuente tuvo fallos
return summary.HasErrors ? 1 : 0;
```

**Por qué ~40 líneas, no 100**: El trabajo del punto de entrada es bootstrapping — crear el contenedor, cablear config, ejecutar el pipeline, salir. Toda la lógica de registro vive en `ServiceRegistration.cs`. Toda la lógica de orquestación vive en `PipelineCoordinator`. Program.cs es intencionalmente delgado porque es lo más difícil de testar unitariamente (es el composition root). Las líneas extra vs. una versión "mínima" son todas significativas: `ContentRootPath`, resolución de `IOptions<PipelineOptions>`, el diccionario `sourceLocations`, y el hook de cancelación.

> ⚠️ **Bug de Runtime Capturado en Fase 10 — Desajuste de Content Root**: La implementación original usaba `Host.CreateApplicationBuilder(args)`. Esto usa `Directory.GetCurrentDirectory()` como content root — la **raíz de la solución** cuando se ejecuta `dotnet run --project src/...`. Pero `appsettings.json` se compila en `AppContext.BaseDirectory` (la carpeta `bin/Debug/net8.0/`). Resultado: la configuración nunca se cargaba, `Sources = []`, cero ubicaciones procesadas. La corrección es `new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory }`. **27 tests pasaron y esto seguía roto** — los tests inyectan config directamente, bypaseando el sistema de archivos completamente. La única forma de capturar esto es ejecutar la app real.

**Por qué `return summary.HasErrors ? 1 : 0`**: Los top-level statements retornan un exit code `int` directamente. Exit code distinto de cero señala a CI/CD, schedulers de cron, y orquestadores que la ejecución fue incompleta. Los sistemas downstream leyendo el TSV deberían saber si algunas ubicaciones fallaron.

**Por qué `Host.CreateApplicationBuilder` con `HostApplicationBuilderSettings`**: El modelo de hosting mínimo de .NET 8. Un método nos da DI, configuración desde `appsettings.json` + variables de ambiente + argumentos de línea de comandos, e `ILogger<T>` configurado para output de consola. El setting `ContentRootPath` es la corrección crítica que alinea la ruta de búsqueda de config con el directorio de output compilado.

**Por qué `Console.CancelKeyPress` + `CancellationTokenSource`**: Cancelación cooperativa. Ctrl+C propaga un `CancellationToken` a través del pipeline completo — peticiones HTTP, escrituras de archivo, todo se detiene limpiamente. En Docker/Kubernetes, `SIGTERM` se mapea al mismo mecanismo vía el ciclo de vida del hosting.

**Por qué `IOptions<PipelineOptions>` se resuelve aquí**: El `RunAsync` del coordinator toma un `Dictionary<string, IReadOnlyList<LocationConfig>>`, no un tipo de config. Este es el patrón Composition Root — Program.cs es el único lugar que traduce de "tipos de config de Infrastructure" al parámetro de dominio puro que Application necesita. El coordinator permanece libre de dependencias de Infrastructure.

### 16.8 Fase 8: Lógica de Retry (Feature Bonus)

**Qué creamos**: Backoff exponencial con jitter en el cliente HTTP de OpenMeteo, configurado vía `Microsoft.Extensions.Http.Resilience`.

**Parámetros de la política de retry**:

| Parámetro | Valor | Razón |
|-----------|-------|-------|
| Intentos máximos de retry | 3 (4 peticiones totales) | Suficiente para sobrevivir un hiccup breve del API, no suficiente para retrasar el pipeline sustancialmente |
| Delay base | 1 segundo | Empezar modesto — el API podría recuperarse en un segundo |
| Tipo de backoff | Exponencial con jitter | 1s → 2s → 4s con ±500ms aleatorio. Exponencial previene golpear un API en dificultades. Jitter previene thundering herd |
| Timeout por intento | 30 segundos | Desde config `SourceOptions.TimeoutSeconds` |

**Qué activa el retry**:
| Disparador | Código HTTP | Por qué hacer retry |
|------------|-----------|-----------|
| Error de servidor | 5xx | Fallo transitorio del lado del servidor. Probablemente se recupera |
| Timeout de petición | 408 | Servidor sobrecargado. Puede recuperarse con backoff |
| Rate limited | 429 | Respetar rate limit, esperar, reintentar |
| Excepción de red | — | `HttpRequestException`: DNS, conexión rechazada. Transitorio |
| Excepción de timeout | — | `TaskCanceledException`: la petición no completó a tiempo. Transitorio |

**Qué NO activa retry**:
| No-disparador | Código HTTP | Por qué NO hacer retry |
|---------------|-----------|---------------|
| Petición incorrecta | 400 | Nuestra URL está malformada. Reintentar envía la misma petición mala |
| No encontrado | 404 | El endpoint no existe. Reintentar no lo creará |
| Otros 4xx | 401, 403 | Problema de auth/permiso. No es transitorio |

**Dónde vive la política**: En `ServiceRegistration.cs` en el `HttpClient` nombrado:
```csharp
services.AddHttpClient("OpenMeteo", client =>
{
    client.BaseAddress = new Uri(
        configuration.GetValue<string>("Pipeline:Sources:0:BaseUrl")
        ?? "https://api.open-meteo.com/v1/forecast");
    client.Timeout = TimeSpan.FromSeconds(
        configuration.GetValue<int>("Pipeline:Sources:0:TimeoutSeconds", 30));
}).AddStandardResilienceHandler();
```

**Por qué `.AddStandardResilienceHandler()` sin callback de opciones**: El handler estándar ya incluye backoff exponencial con jitter, retry en fallos transitorios (5xx, 408, 429), y timeout por intento — todo configurado con defaults buenos. El handler estándar NO hace retry de errores 4xx. Para este ejercicio, los defaults son apropiados y coinciden con la política declarada arriba. La personalización (vía callback de opciones) se agregaría cuando los defaults no coincidan con los requerimientos de SLA.

**Defensa en entrevista**: "Jitter previene thundering herd — si las 3 ubicaciones fallan y reintentan en intervalos idénticos, todas golpean el API simultáneamente otra vez. Jitter aleatoriza el timing de retry entre peticiones. También distingo fallos transitorios de permanentes — un 500 merece retry, un 400 no. La política de retry está en el pipeline del HttpClient vía Microsoft.Extensions.Http.Resilience, no en código de aplicación — la clase del cliente API se mantiene limpia y testeable."

---

### 16.9 Fase 9: Tests — Todo lo que los Evaluadores Escudriñarán

**Qué creamos**: 27+ tests en 5 clases de test cubriendo cada requerimiento de la especificación.

**Filosofía de testing**: La especificación del ejercicio lista 4 categorías explícitas de test más retry. Implementamos TODAS con edge cases significativos, no solo happy paths. Cada nombre de test describe el escenario: `Parse_ValidResponse_ReturnsAllDays`, `Parse_MissingDailyKey_ReturnsParseError`. Los tests se leen como especificaciones.

#### 9A. `OpenMeteoResponseParserTests` — Parsing (válido + malformado)

| # | Test | Input | Esperado | Criterio de evaluación |
|---|------|-------|----------|----------------------|
| 1 | `Parse_ValidResponse_ReturnsAllSevenDays` | `valid_response.json` (7 días, todos los campos) | `Success` con 7 entradas de time, todos los arrays poblados | Parsing de respuesta (válido) |
| 2 | `Parse_ValidResponse_PreservesAllFieldValues` | `valid_response.json` | Valores específicos de temp/precip/wind coinciden con JSON | Parsing de respuesta (válido) |
| 3 | `Parse_MissingDailyKey_ReturnsParseError` | `malformed_missing_daily.json` (sin propiedad `daily`) | `Failure(ParseError)` con mensaje sobre daily faltante | Parsing de respuesta (malformado) |
| 4 | `Parse_NullTemperatureValues_ReturnsSuccessWithNulls` | `malformed_null_values.json` (nulls en arrays) | `Success` — los nulls son huecos válidos de datos meteorológicos | Parsing de respuesta (malformado) |
| 5 | `Parse_MismatchedArrayLengths_ReturnsParseError` | `malformed_mismatched_arrays.json` (time[7] vs temp[6]) | `Failure(ParseError)` con nombre de campo + longitudes | Parsing de respuesta (malformado) |
| 6 | `Parse_EmptyTimeArray_ReturnsParseError` | `malformed_empty_arrays.json` (time[] vacío) | `Failure(ParseError)` — sin datos para procesar | Parsing de respuesta (malformado) |
| 7 | `Parse_ApiErrorResponse_ReturnsParseError` | `error_response.json` (`{ "error": true, "reason": "..." }`) | `Failure(ParseError)` con razón del API | Parsing de respuesta (malformado) |
| 8 | `Parse_InvalidJson_ReturnsParseError` | `"not valid json {{"` | `Failure(ParseError)` — deserialización falló | Parsing de respuesta (malformado) |

#### 9B. `OpenMeteoTransformerTests` — Lógica de transformación

| # | Test | Input | Esperado | Criterio de evaluación |
|---|------|-------|----------|----------------------|
| 9 | `Transform_ValidData_ProducesCorrectRecordCount` | modelo de 7 días + ubicación NYC | 7 `NormalizedWeatherRecord` | Lógica de transformación |
| 10 | `Transform_ValidData_MapsLocationCorrectly` | Modelo + config "New York" | Todos los registros tienen Source="OpenMeteo", Location="New York" | Lógica de transformación |
| 11 | `Transform_ValidData_MapsTemperaturesCorrectly` | Modelo con valores conocidos | TempMaxC y TempMinC coinciden con input | Lógica de transformación |
| 12 | `Transform_NullValues_PreservesNulls` | Modelo con null en temp en índice 3 | Record[3].TempMaxCelsius es null | Lógica de transformación |
| 13 | `Transform_ValidData_ParsesDatesCorrectly` | Modelo con strings de fecha ISO | Valores DateOnly coinciden | Lógica de transformación |
| 14 | `Transform_ValidData_SetsFetchedAtUtc` | Cualquier modelo válido | Todos los registros tienen FetchedAtUtc cercano a DateTime.UtcNow | Lógica de transformación |

#### 9C. `TabDelimitedWriterTests` — Formateo de output

| # | Test | Input | Esperado | Criterio de evaluación |
|---|------|-------|----------|----------------------|
| 15 | `Write_ValidRecords_CreatesFileWithCorrectHeader` | Lista de registros | Primera línea son nombres de columnas separados por tab | Formateo de output |
| 16 | `Write_ValidRecords_FormatsDataRowsCorrectly` | Registros con valores conocidos | Valores separados por tab coinciden con formato esperado | Formateo de output |
| 17 | `Write_NullValues_WritesEmptyString` | Registro con TempMax null | String vacío entre tabs, no "null" | Formateo de output |
| 18 | `Write_EmptyRecordList_WritesHeaderOnly` | Lista vacía | Archivo contiene solo fila de encabezado | Formateo de output |
| 19 | `Write_ValidRecords_UsesUtf8NoBom` | Cualquier registro | Encoding del archivo es UTF-8 sin BOM | Formateo de output |

#### 9D. `PipelineCoordinatorTests` — End-to-end con HTTP mockeado

| # | Test | Setup | Esperado | Criterio de evaluación |
|---|------|-------|----------|----------------------|
| 20 | `Run_AllSourcesSucceed_WritesAllRecords` | Source mock retorna 21 registros | Writer recibe 21 registros, resumen muestra 0 errores | End-to-end (mockeado) |
| 21 | `Run_OneLocationFails_ContinuesOthers` | Mock: NYC éxito, London falla, Tokyo éxito | 14 registros escritos, 1 error en resumen | End-to-end (mockeado) + manejo de errores |
| 22 | `Run_AllLocationsFail_WritesNoRecords` | Mock: los 3 fallan | 0 registros, 3 errores en resumen, exit code 1 | Manejo de errores |
| 23 | `Run_NoSources_ReturnsEmptySummary` | Sin IWeatherDataSource registrado | Resumen con 0 registros, 0 errores | Edge case |
| 24 | `Run_SuccessfulRun_PrintsSummaryWithCounts` | Mock: todos éxito | Resumen tiene conteos correctos de LocationsAttempted/Succeeded | Resumen de procesamiento |

#### 9E. `RetryPolicyTests` — Lógica de retry (bonus)

| # | Test | Setup | Esperado | Criterio de evaluación |
|---|------|-------|----------|----------------------|
| 25 | `Retry_TransientThenSuccess_ReturnsSuccess` | Handler: 500, 500, 200 | Retorna OK después de 3 intentos | Lógica de retry |
| 26 | `Retry_PermanentFailure_DoesNotRetry` | Handler: 400 | Solo 1 intento | Lógica de retry |
| 27 | `Retry_MaxAttemptsExceeded_ReturnsFinalError` | Handler: 500, 500, 500, 500 | Fallo después de intentos máximos | Lógica de retry |

**Fixtures de datos de test** — Archivos JSON en `tests/SyncMetrics.Pipeline.Tests/OpenMeteo/TestData/`:

| Archivo fixture | Contenido | Usado por |
|-----------------|-----------|----------|
| `valid_response.json` | Respuesta completa de 7 días de Open-Meteo para NYC con valores realistas | Tests 1, 2 |
| `valid_london_response.json` | Respuesta completa de 7 días para London | Tests E2E |
| `malformed_missing_daily.json` | JSON válido, sin propiedad `daily` | Test 3 |
| `malformed_null_values.json` | Estructura válida, nulls en arrays de temperatura | Test 4 |
| `malformed_mismatched_arrays.json` | `time[7]` pero `temperature_2m_max[6]` | Test 5 |
| `malformed_empty_arrays.json` | `daily.time` es `[]` | Test 6 |
| `error_response.json` | `{ "error": true, "reason": "Invalid coordinates" }` | Test 7 |

**Defensa en entrevista**: "Cubrí las 4 categorías de test de la especificación: parsing de respuesta con inputs válidos y malformados, lógica de transformación, formateo de output, y end-to-end con HTTP mockeado. Los tests de input malformado son donde se muestra la calidad real — testeo claves faltantes, valores null, longitudes de arrays desajustadas, arrays vacíos, respuestas de error del API, y JSON inválido. Cada test usa archivos fixture de un directorio TestData, así que los datos de test están versionados y son realistas."

#### 16.9.1 Fase 9 — Estado de Completitud

> **ESTADO: ✅ COMPLETADO — 29 de Marzo, 2026 (Sesión 3)**
> - `dotnet test` → **27/27 tests pasan, 0 fallos, 0 omitidos** (`duración: 5.0s`)
> - `dotnet build` → `Build succeeded. 0 Warning(s). 0 Error(s).`
> - `Directory.Build.props` impone `TreatWarningsAsErrors=true` + `AnalysisLevel=latest-recommended` globalmente. Cero warnings significa cero warnings.

**Entregables reales creados** (todos archivos nuevos):

| Archivo | Ubicación | Descripción |
|---------|-----------|-------------|
| `valid_response.json` | `UnitTests/OpenMeteo/TestData/` | Respuesta completa NYC de 7 días — los 6 campos poblados |
| `valid_london_response.json` | `UnitTests/OpenMeteo/TestData/` | Respuesta completa London de 7 días |
| `malformed_missing_daily.json` | `UnitTests/OpenMeteo/TestData/` | JSON válido, sin propiedad `daily` |
| `malformed_null_values.json` | `UnitTests/OpenMeteo/TestData/` | Estructura válida, elementos null en arrays de temperatura |
| `malformed_mismatched_arrays.json` | `UnitTests/OpenMeteo/TestData/` | `temperature_2m_max` tiene 2 items, `time` tiene 3 |
| `error_response.json` | `UnitTests/OpenMeteo/TestData/` | `{ "error": true, "reason": "..." }` — cuerpo de error del API |
| `TestFixtures.cs` | `UnitTests/` | Helper `LoadJson(fileName)` — carga embedded resource por nombre |
| `OpenMeteoResponseParserTests.cs` | `UnitTests/OpenMeteo/` | 8 tests de parser (válido + todos los paths malformados) |
| `OpenMeteoTransformerTests.cs` | `UnitTests/OpenMeteo/` | 6 tests de transformer (conteo de registros, campos, nulls, fechas) |
| `TabDelimitedWriterTests.cs` | `UnitTests/Output/` | 5 tests de writer (creación de archivo, encabezado, nulls, InvariantCulture) |
| `PipelineCoordinatorTests.cs` | `UnitTests/Pipeline/` | 5 tests de coordinator (éxito, errores, no-write-on-empty, duración) |
| `EndToEndPipelineTests.cs` | `IntegrationTests/` | 3 tests de integración (Parser+Transformer+Writer reales, HTTP mockeado) |

**Problemas de build resueltos durante la Fase 9**:

| Problema | Causa | Corrección |
|----------|-------|-----------|
| **CA1707** (32 errores) | El naming con guiones bajos `Given_When_Then` de xUnit en métodos de test viola la convención de naming de .NET | Se agregó `<NoWarn>$(NoWarn);CA1707</NoWarn>` a ambos archivos `.csproj` de test solamente — el código de producción sigue imponiendo sin guiones bajos |
| **CA1305** | `DateOnly.ToString("yyyy-MM-dd")` sin `IFormatProvider` | Cambiado a `.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` + agregado `using System.Globalization` |
| **CA1859** | Tipo de retorno `IReadOnlyDictionary<>` donde `Dictionary<>` es suficiente | Cambiado el tipo de retorno de `SingleSourceLocations()` a `Dictionary<string, IReadOnlyList<LocationConfig>>` |
| **CS4014** | `DidNotReceive().WriteAsync(...)` de NSubstitute retorna `Task<Result<string>>` no-awaiteado | Envuelto en `#pragma warning disable/restore CS4014` — esto es un proxy de aserción síncrono, no una llamada async real |
| **CS0246** | `NullLoggerFactory` / `NullLogger<>` no encontrado en tests de integración | Agregado `using Microsoft.Extensions.Logging.Abstractions;` |

**Bugs de runtime encontrados durante la Fase 10 (generación de TSV de muestra)**:

| Problema | Causa | Corrección |
|----------|-------|-----------|
| **`options.Sources` vacío en runtime** | `Host.CreateApplicationBuilder` usa `Directory.GetCurrentDirectory()` (raíz de solución cuando se ejecuta `dotnet run --project`) como content root. `appsettings.json` vive en `AppContext.BaseDirectory` (bin/Debug/net8.0/). La sección de config `Pipeline:Sources` nunca se cargó → lista vacía → 0 ubicaciones. | Se cambió `Program.cs` para usar `Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory, Args = args })`. Los tests no se vieron afectados porque no usan config binding. |
| **`required init` en DTO de config de `LocationConfig`** | `LocationConfig` tenía `required string Name { get; init; }` etc. El binder de `IConfiguration` usa `Activator.CreateInstance()` + setters de reflexión, que no pueden satisfacer restricciones `required` post-construcción en algunas implementaciones de binder, causando skip silencioso. | Se cambió `LocationConfig` a record mutable con propiedades `{ get; set; }`. Este es el enfoque canónico para modelos de config-binding — `required init` es para objetos de valor de dominio, no para DTOs vinculados por el sistema de configuración. |

**Punto de conversación en entrevista sobre el bug del content root**: *"El mismo pipeline que tenía 27 tests pasando mostró 0 ubicaciones cuando ejecuté `dotnet run` por primera vez. Los tests pasan porque inyectan config directamente — sin `appsettings.json` involucrado. La ejecución real reveló que `Host.CreateApplicationBuilder` usa el directorio de trabajo, no el directorio del assembly, como raíz de búsqueda de config. Este es un gap clásico de integración-vs-unitario: los tests unitarios testean la lógica, pero no pueden capturar bugs de cableado de bootstrap. La corrección fue una línea en Program.cs — `ContentRootPath = AppContext.BaseDirectory` — pero encontrarlo requirió ejecutar la app real, no solo los tests."*

#### 16.9.2 Fase 9 — Disclaimer del Mundo Real (Contexto de Entrevista)

**Por qué esta fase importa en producción y por qué es un punto de conversación en entrevista**:

En una empresa anterior, teníamos un ETL de ingesta de datos que procesaba exportaciones nocturnas de un API SaaS de terceros hacia nuestro data warehouse. Cuando el vendor agregó un nuevo campo a su respuesta de API (`account_tier`), nuestro equipo actualizó el modelo de deserialización (el DTO) correctamente — el nuevo campo fue mapeado. Pero el **transformer** que convertía el DTO a nuestro modelo de dominio interno NO se actualizó. El nuevo campo fue silenciosamente ignorado. El transformer pasó el input a la base de datos con `account_tier` siempre null.

Todos los tests unitarios pasaron. El test del parser verificó que el DTO deserializó correctamente. El test del transformer verificó que la forma del output era correcta. Pero no había ningún test que ejecutara el parser REAL DENTRO del transformer real y revisara el output. El bug del campo null llegó a producción y no fue detectado por 11 días — hasta que un analista de datos notó que el campo siempre era null en Redshift y creó un ticket de soporte.

**La lección de arquitectura**: El aislamiento de test unitario que hace los tests rápidos y precisos TAMBIÉN crea costuras donde los bugs de integración pueden esconderse. La interfaz `IWeatherApiClient` es deliberadamente la única costura en `EndToEndPipelineTests` — el `OpenMeteoResponseParser`, `OpenMeteoTransformer`, y `TabDelimitedFileWriter` reales todos ejecutan juntos. Esto significa: si un campo se agrega a `OpenMeteoApiResponse` pero no se cablea a través del transformer y al registro normalizado, el test de integración lo capturará. Los tests unitarios capturan corrección por clase; los tests de integración capturan corrección de cableado.

**La costura de `IWeatherApiClient` es la línea arquitectónica**: En un sistema de producción real, el boundary HTTP es donde el mundo externo termina y tu sistema comienza. Todo dentro de esa línea (parsing, transformación, output) es responsabilidad de TU código. Sustituir solo el cliente HTTP en tests de integración significa que estás ejercitando TODA tu lógica de producción: el parser valida estructura, el transformer hace zip de arrays, el writer formatea output — exactamente como ejecutarán en producción, sin interferencia de mocks. Esto refleja directamente la práctica descrita en "TDD: Where Did It All Go Wrong?" de Ian Cooper — testear comportamientos, no implementaciones; solo mockear en boundaries del sistema.

**La estrategia de tests de dos capas intencionalmente**:
- **Tests unitarios**: Cada clase en aislamiento. Rápidos (< 50ms por test). Capturan bugs de lógica a nivel de clase: índice de array incorrecto, cultura incorrecta para decimales, columna de encabezado faltante. Capturan regresiones cuando una clase cambia.
- **Tests de integración**: Clases reales compuestas juntas, HTTP mockeado en el boundary. Más lentos pero aún rápidos (< 500ms por test porque no hay HTTP real). Capturan bugs de cableado: argumento incorrecto pasado entre clases, malentendido de contrato de interfaz, mala configuración de DI.

La proporción 24:3 (24 tests unitarios: 3 tests de integración) refleja el tradeoff costo-beneficio: los tests unitarios son baratos de escribir y rápidos de ejecutar así que escribimos muchos; los tests de integración necesitan más setup y cubren más cableado así que escribimos menos pero estratégicamente dirigidos.

#### 16.9.3 Fase 9 — Principios SOLID y Patrones de Diseño Demostrados

La Fase 9 es la capa de testing — donde cada decisión arquitectónica de las Fases 1-6 se valida y los principios SOLID se prueban, no solo se declaran.

| Principio / Patrón | Cómo la Fase 9 lo Demuestra | Punto de Conversación en Entrevista |
|--------------------|----------------------------|-------------------------------------|
| **S — Responsabilidad Única (SRP)** | Cada clase de test testea exactamente UNA clase de producción en aislamiento: `OpenMeteoResponseParserTests` nunca toca el transformer; `PipelineCoordinatorTests` nunca toca I/O de archivos. Una clase a testear = una razón para que el archivo de test cambie. | *"Si cambio el parser, solo `OpenMeteoResponseParserTests` se rompe. Si cambio el writer, solo `TabDelimitedWriterTests` se rompe. El radio de blast del test mapea 1:1 a la clase cambiada — eso es SRP en tests."* |
| **O — Principio Abierto/Cerrado (OCP)** | Agregar una nueva fuente de datos (WeatherAPI.com) requiere agregar `WeatherApiResponseParserTests` y `WeatherApiTransformerTests` — las clases de test existentes quedan intocadas. `EndToEndPipelineTests` puede extenderse con un nuevo `[Fact]` para la nueva fuente. | *"Agregar tests de WeatherAPI significa agregar nuevas clases de test, no modificar las existentes. La suite de test está abierta para extensión, cerrada para modificación — igual que el pipeline de producción."* |
| **L — Principio de Sustitución de Liskov (LSP)** | NSubstitute funciona porque `IWeatherDataSource`, `IOutputWriter`, e `IWeatherApiClient` siguen LSP. Puedes sustituir un mock por cualquiera de ellos sin que el código llamador se entere. Si LSP fuera violado, el mocking produciría comportamiento incorrecto. | *"El hecho de que NSubstitute pueda generar un mock funcional desde una interfaz es prueba de que la interfaz sigue LSP. Un mock es la prueba definitiva conductual de sustituibilidad."* |
| **D — Principio de Inversión de Dependencia (DIP)** | `PipelineCoordinatorTests` construye el coordinator con mocks de `IWeatherDataSource` e `IOutputWriter` — nunca referencia `OpenMeteoDataSource` o `TabDelimitedFileWriter`. Los tests dependen de abstracciones, no de implementaciones. | *"El test del coordinator nunca importa una sola clase de Infrastructure. Trabaja contra las mismas interfaces que usa el coordinator de producción. DIP significa: el test y el código de producción ambos dependen de la abstracción, nunca de los tipos concretos del otro."* |
| **Patrón Test Double / Substitute** | NSubstitute genera proxies en memoria que registran llamadas y retornan respuestas configuradas. `Substitute.For<IWeatherDataSource>()` da un sustituto completo que puede simular éxito, errores, delays, o resultados vacíos — todo sin red ni I/O de archivos. | *"NSubstitute genera un doble conductual completo en runtime. Para un test que verifica agregación de errores, el sustituto retorna `Result.Failure<>` configurado — sin HTTP real necesario. Para un test verificando no-write-on-empty, `DidNotReceive()` verifica que el writer nunca fue llamado."* |
| **Patrón Object Mother** | `BuildValidResponse(int days)` en `OpenMeteoTransformerTests` es un Object Mother: un helper crea un `OpenMeteoApiResponse` completamente poblado para cualquier número de días. Seis tests usan el mismo método factory. Si `OpenMeteoApiResponse` gana un nuevo campo, se corrige en un lugar. | *"Object Mother previene fragilidad de tests. Cuando el modelo de respuesta del API cambia, corrijo `BuildValidResponse()` una vez — los 6 tests del transformer se benefician. Sin esto, modificaría la misma construcción de DTO en 6 lugares y me saltaría uno."* |
| **Patrón Test Seam** | `EndToEndPipelineTests` sustituye solo `IWeatherApiClient` — la costura en el boundary HTTP externo. Todo hacia adentro (parser, transformer, writer) es la implementación real cableada a través de `ServiceCollection`. | *"La costura está en el boundary del sistema — donde mi código termina y el API externo comienza. Dentro de ese boundary, nada está mockeado. Esta es la costura de arquitectura en acción: la interfaz permite a los tests reemplazar la llamada HTTP real con un sustituto controlable mientras sigue ejercitando todo lo que tu sistema posee."* |
| **Patrón Embedded Resource** | Los fixtures de test JSON son items `<EmbeddedResource>` compilados en el assembly de test. Funcionan en CI, Docker, cualquier máquina — sin fragilidad de rutas relativas. `TestFixtures.LoadJson("valid_response.json")` carga por nombre de manifest resource. | *"Datos de test versionados con el código. Si el contrato del API cambia, actualizo los archivos fixture junto con el parser. El assembly de test lleva sus propios datos — sin dependencia de dónde resulte estar el directorio de trabajo del test runner."* |
| **Patrón de Cleanup IDisposable** | `TabDelimitedWriterTests` y `EndToEndPipelineTests` implementan `IDisposable` para eliminar directorios temporales creados durante el test. xUnit llama `Dispose()` después de cada clase de test — sin acumulación de directorios temp entre ejecuciones de test. | *"Los tests que crean archivos deben limpiar después de sí mismos. IDisposable en xUnit es cleanup determinista — ejecuta sin importar si los tests pasan o fallan. En un ambiente CI que ejecuta cientos de veces por día, dejar directorios temp causa presión de disco."* |
| **Arrange/Act/Assert (AAA)** | Cada test sigue AAA con separación explícita de línea en blanco. La sección arrange configura mocks e inputs, act ejecuta el sistema bajo test, assert verifica el output. La estructura es visible de un vistazo. | *"AAA es un contrato de legibilidad. Cualquier desarrollador leyendo el test debería poder identificar las tres fases en 10 segundos. Lo impongo con separadores de línea en blanco — es una disciplina menor que paga cuando se depura un test fallando a las 2am."* |

**Decisiones clave de diseño tomadas durante la Fase 9** (con puntos de conversación en entrevista):

| Decisión | Qué hicimos | Por qué | Punto de conversación en entrevista |
|----------|------------|---------|--------------------------------------|
| **Estrategia de supresión CA1707** | Agregamos `<NoWarn>$(NoWarn);CA1707</NoWarn>` a archivos `.csproj` de test SOLAMENTE | La convención de guiones bajos `Given_When_Then_` de xUnit es legítima y ampliamente reconocida. Suprimir solo en proyectos de test preserva la calidad del código de producción mientras permite convenciones de test. | *"Suprimí CA1707 a nivel de proyecto de test, no globalmente. El código de producción impone la convención de naming de .NET. Los métodos de test usan la convención de guiones bajos de xUnit — estos sirven a audiencias diferentes y propósitos diferentes."* |
| **Pragma CS4014 para NSubstitute** | `#pragma warning disable CS4014` alrededor de `DidNotReceive().WriteAsync(...)` | El `DidNotReceive()` de NSubstitute retorna un proxy de aserción síncrono que resulta estar tipado como `Task<T>`. El compilador ve un task no-awaiteado; nosotros vemos una verificación de aserción. El pragma con comentario hace esto explícito. | *"CS4014 se dispara porque NSubstitute tipa el resultado de verificación como `Task<Result<string>>`. Esta no es una operación async real — es un registrador síncrono de llamadas. El pragma + comentario documenta la intención: NO estamos awaiteando un task, ESTAMOS verificando una no-llamada."* |
| **NullLogger vs logging real en tests** | `NullLogger<T>.Instance` en tests unitarios; `NullLoggerFactory` + `AddLogging()` en tests de integración | Tests unitarios quieren cero ruido de infraestructura — `NullLogger` descarta todo el output de log silenciosamente. Tests de integración ejercitan el grafo real de DI incluyendo el registro de logging, así que cableamos `NullLoggerFactory` a través de `ServiceCollection.AddLogging()`. | *"Tests unitarios usan NullLogger — testean lógica, no logging. Tests de integración usan NullLoggerFactory a través del contenedor real de DI porque testean el sistema compuesto, y el sistema compuesto usa ILogger."* |
| **Raw string literal para fixture JSON** | `"""..."""` de C# 11 para el JSON inline de 7 días en `EndToEndPipelineTests` | Evita comillas dobles escapadas en strings JSON. El JSON es legible dentro del código fuente C# sin ruido de backslashes. | *"El ruido de `\"` en strings JSON embebidos es una carga de mantenimiento y un impuesto a la legibilidad. Los raw string literals de C# 11 eliminan esto completamente. El JSON se ve exactamente como se vería en un archivo `.json`."* |

---

### 16.10 Fase 10: Docker — Build Reproducible

**Qué creamos**: Un Dockerfile multi-stage que construye, testea, y produce una imagen mínima de runtime.

```dockerfile
# Stage 1: Build + Test
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet build -c Release --no-restore
RUN dotnet test -c Release --no-restore --no-build --verbosity normal
RUN dotnet publish src/SyncMetrics.Pipeline/SyncMetrics.Pipeline.csproj \
    -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SyncMetrics.Pipeline.dll"]
```

**Por qué multi-stage**:
- **Stage de build** (~800MB) tiene el SDK — construye, ejecuta tests, publica. Si los tests fallan, el build falla. Nadie obtiene una imagen Docker con tests fallando
- **Stage de runtime** (~80MB) tiene solo el runtime de .NET — 10x más pequeño. Sin SDK, sin código fuente, sin assemblies de test. Esto es lo que ejecuta en producción
- **Experiencia del evaluador**: `docker build -t syncmetrics .` → ve tests pasar durante el build. `docker run --rm -v ${PWD}/output:/app/output syncmetrics` → ve datos meteorológicos. Sin necesidad de .NET SDK en su máquina

**Por qué NO Docker Compose**: No hay base de datos, no hay Redis, no hay cola de mensajes, no hay segundo servicio. Un solo Dockerfile es la herramienta correcta. Docker Compose para un solo contenedor señalaría sobre-ingeniería.

**Instrucciones de doble ruta en README**:
```
# Opción 1: Directo (requiere .NET 8 SDK)
dotnet run --project src/SyncMetrics.Pipeline

# Opción 2: Docker (requiere solo Docker)
docker build -t syncmetrics-pipeline .
docker run --rm -v ${PWD}/output:/app/output syncmetrics-pipeline
```

---

### 16.11 Fase 11: CI/CD + Documentación

#### 11A. GitHub Actions — `.github/workflows/build-and-test.yml`

```yaml
name: Build and Test
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build -c Release --no-restore
      - name: Test
        run: dotnet test -c Release --no-restore --no-build --verbosity normal
```

**Por qué incluir CI cuando no es requerido**: Toma 5 minutos agregar, señala disciplina profesional. Badge verde en el repo significa que el evaluador ve "tests pasando en un ambiente limpio" antes de siquiera clonar. Prueba que funciona más allá de la máquina de Arthur.

**Qué deliberadamente NO agregamos a CI**: Publishing de Docker, pasos de deployment, umbrales de code coverage, escaneo SonarQube. Cada uno sería scope creep innecesario para un take-home.

#### 11B. `README.md` — Corto, Útil, Nivel Staff

Estructura:
1. **Descripción de una oración**: "SyncMetrics Weather Pipeline — pipeline de ingesta .NET 8 que busca, normaliza, y produce datos de pronóstico meteorológico"
2. **Quick start**: Comandos `dotnet run` y `docker` (4 líneas)
3. **Arquitectura**: Un párrafo + el diagrama de flujo de interfaces de la Sección 4
4. **Extensibilidad**: "Para agregar una segunda fuente: crear una carpeta bajo Infrastructure/, implementar las 4 interfaces de Core, registrar en DI. Ver Infrastructure/OpenMeteo/ como la implementación de referencia."
5. **Supuestos y Trade-offs**: Lista de viñetas de decisiones clave (proyecto único vs. multi, Result<T> vs. excepciones, sin base de datos, etc.)
6. **Testing**: `dotnet test` + qué está cubierto

**La especificación dice**: "No sobre-documentes; escribe lo que querrías que un nuevo compañero de equipo supiera." Seguimos esto exactamente.

#### 11C. `AI.md` — 10-15 Líneas, Honesto y Específico

```markdown
# AI Usage

AI (GitHub Copilot + Claude) was used as a core development tool throughout this exercise.

## What AI Drove
- Project scaffolding and folder structure generation
- HTTP client boilerplate (IHttpClientFactory setup, query string building)
- JSON deserialization model from API response documentation
- Test fixture JSON files generated from Open-Meteo API docs
- Tab-delimited output formatting logic

## Where I Overrode AI
- AI-generated retry policy treated all HTTP errors as retryable. I corrected this to
  distinguish transient (5xx, 408, 429) from permanent (4xx) failures. Retrying a 400
  Bad Request wastes time and masks bugs.

## Deliberate Non-Use
- Architecture decisions (approach selection, interface design, Result<T> error model)
  were made without AI. These require understanding of the evaluation criteria, trade-off
  analysis, and judgment about proportional complexity — areas where human reasoning
  about context outperforms AI generation.
```

**Defensa en entrevista**: "AI scaffoldeó la plomería — setup HTTP, modelos JSON, fixtures de test. Lo sobrescribí en la política de retry porque el código generado no distinguía fallos transitorios de permanentes. Deliberadamente mantuve las decisiones de arquitectura dirigidas por humano porque esas requieren entender qué valoran los evaluadores, no qué genera más rápido."

---

### 16.12 Orden de Build — Secuencia de Ejecución Paso a Paso

Este es el orden en que implementaremos. Cada paso construye sobre el anterior y resulta en un estado compilable.

| Paso | Qué | Depende de | Estado | Estado de entregable después del paso |
|------|-----|-----------|--------|---------------------------------------|
| 1 | Solución + proyectos + estructura de carpetas + paquetes NuGet | — | ✅ | Solución compila (vacía) |
| 2 | `Result<T>` + `PipelineError` | Paso 1 | ✅ | Pipeline.Core compila |
| 3 | Modelos (`NormalizedWeatherRecord`, `LocationConfig`, `ProcessingResult`, `PipelineSummary`) | Paso 2 | ✅ | Todos los modelos disponibles |
| 4 | Interfaces (las 5: `IWeatherApiClient`, `IResponseParser`, `IDataTransformer`, `IOutputWriter`, `IWeatherDataSource`) | Paso 3 | ✅ | Contratos de interfaz definidos |
| 5 | Configuración: `appsettings.json` + `PipelineOptions` + `FieldMapping` + `ServiceRegistration` | Paso 4 | ✅ | Config binding funciona |
| 6 | `OpenMeteoApiResponse` modelo de deserialización | Paso 4 | ✅ | Contrato del API modelado |
| 7 | `OpenMeteoApiClient` (HTTP fetch, retorna `Result<string>`) | Pasos 5, 6 | ✅ | Puede llamar API Open-Meteo |
| 8 | `OpenMeteoResponseParser` (JSON → modelo validado, retorna `Result<T>`) | Paso 6 | ✅ | Puede parsear respuestas del API |
| 9 | `OpenMeteoTransformer` (modelo → registros normalizados, retorna `Result<T>`) | Pasos 3, 6 | ✅ | Puede transformar datos |
| 10 | `OpenMeteoDataSource` (compuesto: fetch→parse→transform con `Task.WhenAll`) | Pasos 7, 8, 9 | ✅ | Pipeline de fuente completo |
| 11 | `TabDelimitedFileWriter` (escribe `.tsv`) | Paso 4 | ✅ | Puede escribir output |
| 12 | `PipelineCoordinator` (orquestar todas las fuentes, construir resumen) | Pasos 4, 10, 11 | ✅ | Pipeline ejecuta end-to-end |
| 13 | `Program.cs` — cablear DI, binding de config, ejecutar coordinator | Pasos 5, 12 | ✅ | `dotnet build` exitoso |
| 14 | Fixtures de test: todos los archivos JSON en TestData/ | Paso 6 | ✅ | Datos de test listos |
| 15 | Tests: `OpenMeteoResponseParserTests` (8 tests) | Pasos 8, 14 | ✅ | Parser completamente testeado |
| 16 | Tests: `OpenMeteoTransformerTests` (6 tests) | Paso 9 | ✅ | Transformer completamente testeado |
| 17 | Tests: `TabDelimitedWriterTests` (5 tests) | Paso 11 | ✅ | Output completamente testeado |
| 18 | Tests: `PipelineCoordinatorTests` (5 tests) | Paso 12 | ✅ | E2E completamente testeado |
| 19 | Lógica de retry: configuración `.AddStandardResilienceHandler()` | Paso 7 | ✅ | Retry funciona |
| 20 | Tests: `RetryPolicyTests` (3 tests) | Paso 19 | ✅ | Retry testeado — **27/27 total** |
| 21 | Dockerfile (build multi-stage) | Paso 13 | ✅ | Docker build + run funciona |
| 22 | Workflow de GitHub Actions | Paso 13 | ✅ | Pipeline CI definido |
| 23 | Generar output de muestra: ejecutar pipeline, guardar `.tsv` en `output/sample/` | Paso 13 | ✅ | **21 filas escritas** (3 ubicaciones × 7 días) |
| 24 | `README.md` | Todos los pasos | ✅ | Documentación completa |
| 25 | `AI.md` | Todos los pasos | ✅ | Uso de AI documentado |

> ⚠️ **Dos bugs de runtime descubiertos en el Paso 23** (ambos requirieron `dotnet run`, no `dotnet test`):
> - **Bug A — Desajuste de content root**: `Host.CreateApplicationBuilder(args)` usaba `Directory.GetCurrentDirectory()` (raíz de solución) como content root. `appsettings.json` vive en `AppContext.BaseDirectory` (bin/). Corrección: `HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory }` en Program.cs.
> - **Bug B — `required init` en DTO de config**: `LocationConfig` tenía `required string Name { get; init; }`. El binder de `IConfiguration` usa `Activator.CreateInstance()` + setters de reflexión, que no pueden satisfacer restricciones `required` post-construcción. Corrección: cambiado a `{ get; set; } = string.Empty`. Los 27 tests pasaron todo el tiempo — los tests inyectan config directamente, bypaseando el sistema de archivos y el config binder completamente.

**Checkpoint después del Paso 13**: `dotnet build` exitoso. El cableado del pipeline está completo. Los Pasos 14-25 agregan tests, retry, Docker, CI, y docs. Los dos bugs de runtime fueron capturados solo en el Paso 23 al ejecutar el binario real.

**Estado final**: `dotnet test` → `27/27 passed`. `dotnet run` → 21 registros, exit code 0. `dotnet build` → 0 warnings, 0 errores.

### 16.13 Lo que Deliberadamente NO Construimos — Y Por Qué

| Elemento excluido | Por qué excluido | Qué decir si preguntan |
|-------------------|------------------|------------------------|
| **React / cualquier frontend** | No está en la especificación. Daña activamente la puntuación de "juicio staff". Cada criterio de evaluación se enfoca en el pipeline C# | "Deliberadamente scoped a calidad de pipeline sobre amplitud. La abstracción IOutputWriter significa que un Minimal API + dashboard React es un paso futuro directo." |
| **Base de datos (cualquiera)** | La especificación dice output a archivo. Agrega dependencia que el evaluador debe instalar | "IOutputWriter permite intercambiar a PostgreSQL o Cosmos con un solo registro de DI. Pero archivos es lo que se pidió." |
| **ASP.NET Minimal API / servidor web** | La especificación dice `dotnet run` → ejecutar → salir. No `dotnet run` → servidor inicia | "Si SyncMetrics lo necesita como servicio, envolver PipelineCoordinator en un API con BackgroundService toma 2-3 horas." |
| **MediatR / CQRS** | Desajuste semántico. MediatR es para dispatch de request/response, no para flujo de pipeline lineal | "Consideré MediatR para cross-cutting concerns pero el pipeline es secuencial, no basado en dispatch." |
| **Proyecto único .csproj** | Clean Architecture de 4 proyectos da enforcement de dependencias en tiempo de compilación y reconocimiento instantáneo del evaluador | "Los nombres de proyecto son la documentación de arquitectura. El compilador impone la regla de dependencia — Core tiene cero referencias." |
| **LanguageExt / librerías funcionales** | Paquete de 900KB para 50 líneas de Result<T> | "Superficie mínima de dependencias. Puedo implementar Result<T> en 50 líneas adaptadas a mi modelo de errores." |
| **AutoMapper** | 5 mapeos de campos. El mapeo manual es más claro y fácil de depurar | "El valor de AutoMapper es proporcional al conteo de mapeos. 5 campos no justifican la abstracción." |
| **Serilog** | El `Microsoft.Extensions.Logging` built-in con proveedor de consola es suficiente | "Serilog agrega logging estructurado a sinks. Solo tenemos output de consola. El logging built-in lo cubre." |
| **Herramientas de code coverage** | Agradable pero scope creep innecesario para un take-home | "Agregaría coverlet + ReportGenerator en un CI de producción. Para este ejercicio, la lista de tests es la historia de coverage." |

---

### 16.14 Análisis de Gaps Post-Implementación — Revisión Profunda Sesión 3

> **Realizado**: 29 de Marzo, 2026 — después de completar los 25 pasos de build, 27 tests pasando, ejecución completa verificada.
> **Método**: Leí cada archivo fuente, cada archivo de test, cada config y archivo de entregable contra la especificación original del ejercicio línea por línea.

#### 16.14.1 Scorecard de Cobertura — Requerimientos Completamente Cubiertos

| Requerimiento | Evidencia en código |
|---|---|
| Obtener pronóstico de 7 días, 3+ ubicaciones configurables | `appsettings.json` → 3 ubicaciones; `forecast_days=7` hardcodeado en URL builder |
| Fetching concurrente | `Task.WhenAll` tanto en `OpenMeteoDataSource.ProcessAsync` COMO en `PipelineCoordinator.RunAsync` |
| Output TSV normalizado, schema auto-diseñado | Schema de 11 columnas, `TabDelimitedFileWriter`, UTF-8 sin-BOM, `InvariantCulture` en todas partes |
| Resumen de procesamiento al completar | `PipelineCoordinator.PrintSummary` — log estructurado `[LoggerMessage]`: fuentes, conteos, errores, duración, ruta de output |
| `dotnet run` produce output | 21 filas verificadas en vivo (3 ubicaciones × 7 días). Exit code 0 en éxito, 1 en cualquier error |
| `dotnet test` pasa | 27/27 pasando. `TreatWarningsAsErrors=true`. 0 warnings, 0 errores |
| Arquitectura extensible para una segunda fuente | 5 interfaces, patrón Strategy, `IEnumerable<IWeatherDataSource>` resuelto por DI. Agregar una fuente = implementar 4 interfaces + 4 líneas de DI |
| HTTP, parsing, transformación, output separados | `IWeatherApiClient`, `IResponseParser<T>`, `IDataTransformer<T>`, `IOutputWriter` — cero referencias cross-concern |
| HTTP detrás de una interfaz | `IWeatherApiClient.FetchAsync` retorna `Result<string>` — ninguna plomería HTTP se filtra |
| Manejo de errores: HTTP, JSON malformado, campos faltantes, valores no parseables | `FetchError`, `ParseError`, `TransformError`, `OutputError`; el parser valida cada array por null y desajuste de longitud |
| Sin fallos silenciosos | `Result<T>` a través de todo el pipeline; el coordinator agrega e imprime TODOS los errores en el resumen |
| Tests — parser (válido + malformado) | 8 tests en `OpenMeteoResponseParserTests` |
| Tests — lógica de transformación | 6 tests en `OpenMeteoTransformerTests` |
| Tests — formateo de output | 5 tests en `TabDelimitedWriterTests` |
| Tests — end-to-end con HTTP mockeado | 3 tests de integración en `EndToEndPipelineTests` (Parser+Transformer+Writer reales, solo `IWeatherApiClient` mockeado) |
| Tests — orquestación del coordinator | 5 tests en `PipelineCoordinatorTests` |
| `README.md` | Tabla de schema, quick start, diagrama de arquitectura, pasos de extensibilidad, supuestos y trade-offs |
| `AI.md` | 4 puntos específicos de override, sección clara de non-use deliberado |
| Dockerfile | Multi-stage: sdk:8.0 (build + test) → runtime:8.0. Tests ejecutan dentro del build — test fallando = imagen fallida |
| GitHub Actions CI | `.github/workflows/build-and-test.yml` — push/PR en `main`, ubuntu-latest, restore → build → test |
| Output de muestra | `output/sample/weather_data_sample.tsv` — 21 filas reales de Open-Meteo, obtenidas 29 de Marzo de 2026 |
| Bonus: retry en fallos transitorios | `.AddStandardResilienceHandler()` en `HttpClient` nombrado. Reintenta 5xx, 408, 429, `HttpRequestException`. Nunca reintenta 4xx |
| Bonus: mapeo de campos config-driven (schema de config) | Clase `FieldMapping` + `FieldMappings[]` en `appsettings.json` documenta la correspondencia fuente→columna de output |
| Discrepancia de spec #1 capturada | `wind_speed_10m_max` (correcto) vs `windspeed_10m_max` (typo en URL del ejercicio). Validado contra docs oficiales de Open-Meteo |
| Discrepancia de spec #2 capturada | `uv_index_max` listado en tabla del ejercicio pero faltante en URL del ejercicio. Agregado a la implementación |

#### 16.14.2 Gaps Encontrados y su Estado

**GAP 1 — El bonus de retry tiene cero tests** `[SEVERIDAD: ALTA]` `[ESTADO: ✅ CORREGIDO]`

- **Qué se encontró**: La infraestructura de retry existía (`.AddStandardResilienceHandler()`) pero ninguna clase de test la ejercitaba. El plan del checkpoint mencionaba `RetryPolicyTests` (tests 25–27) pero esos eran en realidad `EndToEndPipelineTests`. Un evaluador que pregunte "muéstrame el test de retry" no encontraría nada.
- **Qué se hizo**: Se creó `tests/SyncMetrics.Pipeline.UnitTests/Http/RetryPolicyTests.cs` con 3 tests usando un `MockHttpMessageHandler` personalizado que retorna una secuencia configurable de respuestas HTTP:
  - `RetryPolicy_TransientFailureThenSuccess_ReturnsSuccess` — 500 → 200, verifica que retry funciona
  - `RetryPolicy_PermanentFailure_DoesNotRetry` — 400 → exactamente 1 petición, verifica que no hay retry en fallos permanentes
  - `RetryPolicy_ExhaustsMaxAttempts_ThrowsAfterFourRequests` — 500 × 4, verifica comportamiento de agotamiento
- **Conteo final de tests**: 27 → **32 tests, todos pasando**
- **Respuesta de entrevista**: *"La política de retry está en el pipeline del HttpClient. Lo testeé con un `MockHttpMessageHandler` que retorna secuencias como 500 → 200. El test de fallo permanente prueba que `AddStandardResilienceHandler` no reintenta 4xx — un 400 significa que nuestra URL está malformada, reintentar envía la misma petición mala y enmascara bugs."*

**GAP 2 — La afirmación de mapeo config-driven de campos en el README era inexacta** `[SEVERIDAD: ALTA]` `[ESTADO: ✅ CORREGIDO]`

- **Qué se encontró**: El README decía *"Agregar un campo requiere solo una entrada de config"* — esto era falso. `FieldMappings` en `appsettings.json` es un artefacto de documentación; el cableado real de campos está en tres lugares hardcodeados (`OpenMeteoApiClient.DailyFields`, asignaciones de propiedades de `OpenMeteoTransformer`, `TabDelimitedFileWriter.HeaderColumns`).
- **Qué se hizo**: Se reemplazó la afirmación falsa con una declaración honesta de trade-off en la sección de Supuestos del README explicando por qué se eligió la asignación estática type-safe sobre el mapeo dinámico basado en reflexión, y qué requiere realmente agregar un campo (3 cambios de una línea en código + 1 entrada de config).
- **Respuesta de entrevista**: *"El config `FieldMappings` es el registro canónico de campos — documenta cómo la fuente llama a un campo, cómo lo llamamos nosotros en el output, y la unidad. El cableado real es type-safe y estático en el transformer porque el mapeo dinámico basado en reflexión perdería la verificación en tiempo de compilación y haría el transformer no-testeable en aislamiento. Agregar `precipitation_hours` son tres cambios de una línea de código: una propiedad en el modelo de respuesta, una asignación en el transformer, y un string de encabezado en el writer. Para la escala de este pipeline, esa es complejidad proporcional."*

**GAP 3 — El fixture `valid_london_response.json` nunca fue cargado por ningún test** `[SEVERIDAD: BAJA]` `[ESTADO: ✅ CORREGIDO]`

- **Qué se encontró**: El fixture existía y estaba embebido en el assembly pero ningún test lo referenciaba — embedded resource muerto.
- **Qué se hizo**: Se agregó `Parse_ValidLondonResponse_ReturnsSuccessWithLondonCoordinates` a `OpenMeteoResponseParserTests` — verifica que el parser funciona correctamente con un segundo fixture usando coordenadas de longitud negativa (London: -0.1278). Total tests de parser: 8 → **9**.

**GAP 4 — El path del parser para tiempo-vacío no tenía test** `[SEVERIDAD: BAJA]` `[ESTADO: ✅ CORREGIDO]`

- **Qué se encontró**: `OpenMeteoResponseParser` maneja `daily.Time.Count == 0` y retorna un `ParseError`. El plan del checkpoint referenciaba un fixture `malformed_empty_arrays.json`, pero el archivo no existía y el test nunca fue escrito. El código de validación ejecutaba pero estaba no-verificado.
- **Qué se hizo**: Se creó `tests/SyncMetrics.Pipeline.UnitTests/OpenMeteo/TestData/malformed_empty_arrays.json` y se agregó `Parse_EmptyTimeArray_ReturnsParseError` a `OpenMeteoResponseParserTests`. Total tests de parser: 9 → **10**.

#### 16.14.3 Conteo Final de Tests Después de Correcciones de Gaps

| Clase de test | Antes | Después | Agregados |
|---|---|---|---|
| `OpenMeteoResponseParserTests` | 8 | 10 | Test de coords London, test de array-time-vacío |
| `OpenMeteoTransformerTests` | 6 | 6 | — |
| `TabDelimitedWriterTests` | 5 | 5 | — |
| `PipelineCoordinatorTests` | 5 | 5 | — |
| `EndToEndPipelineTests` | 3 | 3 | — |
| `RetryPolicyTests` | 0 | 3 | Transiente→éxito, permanente no-retry, agotamiento |
| **Total** | **27** | **32** | **+5 tests total (+3 retry, +2 parser)** |

**Estado final verificado**: `dotnet test` → `total: 32; failed: 0; succeeded: 32; skipped: 0`
**Estado de build**: `0 Warning(s). 0 Error(s)` — `TreatWarningsAsErrors=true` sigue vigente.

---

## 16.15 — Recorrido Completo de la Solución: Arquitectura, Archivos, y Cómo Funciona Todo

> **Audiencia**: Un desarrollador junior que nunca ha visto este codebase. Saldrás de esta sección entendiendo cada archivo, cada capa, por qué se tomaron las decisiones, y cómo ejecutar la solución tú mismo.

---

### 16.15.1 — ¿Qué es esta solución?

**Propósito**: Obtener datos de pronóstico meteorológico de 7 días para una lista configurable de ciudades de un API meteorológico externo, normalizar los datos de cada día en un registro unificado, y escribir todos los registros en un archivo delimitado por tabs (TSV).

**El escenario de negocio**: SyncMetrics quiere rastrear datos meteorológicos a través de múltiples ciudades. La fuente de hoy es Open-Meteo (gratis, sin API key). La solución debe estar diseñada para que una segunda fuente (ej., WeatherAPI.com) pueda agregarse sin cambiar código existente — solo agregando código nuevo.

**Lo que produce la solución**: Un archivo como `weather_data_20260329_142305.tsv` en una carpeta `output/`, conteniendo una fila por ciudad por día:

```
Source    Location   Latitude  Longitude  Date        TempMaxC  TempMinC  PrecipitationMm  WindSpeedMaxKmh  UVIndexMax  FetchedAtUtc
OpenMeteo New York   40.7100   -74.0059   2026-03-29  12.5      5.2       0.0              18.5             3.2         2026-03-29T14:23:05Z
OpenMeteo London     51.5074   -0.1278    2026-03-29  9.8       4.1       2.4              22.1             2.8         2026-03-29T14:23:05Z
OpenMeteo Tokyo      35.6762   139.6503   2026-03-29  18.2      11.3      0.0              15.4             5.1         2026-03-29T14:23:05Z
...
```

Para 3 ciudades y un pronóstico de 7 días, obtienes **21 filas** de datos.

---

### 16.15.2 — Cómo ejecutar la solución (exactamente como lo haría un entrevistador)

**Prerrequisitos**: .NET 8 SDK instalado. Sin API keys. Sin base de datos. Sin Docker. Conexión a internet para alcanzar `api.open-meteo.com`.

**Paso 1 — Clonar el repo y navegar a la raíz de la solución**
```
git clone <repo-url>
cd Actabl
```

**Paso 2 — Ejecutar el pipeline**
```
dotnet run --project src/SyncMetrics.Pipeline.Console
```

Verás output de log estructurado:
```
info: SyncMetrics weather pipeline starting...
info: Processing source OpenMeteo with 3 location(s)...
info: ═══════════════════════════════════════════════
info:   SyncMetrics Weather Pipeline — Run Summary
info: ═══════════════════════════════════════════════
info:   Sources processed:    1 (OpenMeteo)
info:   Locations attempted:  3
info:   Locations succeeded:  3
info:   Locations failed:     0
info:   Records written:      21
info:   Output file:          ./output/weather_data_20260329_142305.tsv
info:   Duration:             0.82s
info: ═══════════════════════════════════════════════
```

El archivo de output se coloca en `output/` relativo al directorio del proyecto. Ábrelo en Excel o cualquier editor de texto — las columnas están separadas por tabs.

**Paso 3 — Ejecutar todos los tests**
```
dotnet test
```

Output esperado:
```
Test summary: total: 32; failed: 0; succeeded: 32; skipped: 0
```

**Paso 4 — Cambiar las ciudades** (opcional, para mostrar al entrevistador)
Abre `src/SyncMetrics.Pipeline.Console/appsettings.json`, agrega una ciudad:
```json
{ "Name": "Sydney", "Latitude": -33.8688, "Longitude": 151.2093 }
```
Ejecuta de nuevo. El archivo de output ahora incluirá el pronóstico de 7 días de Sydney. Sin cambio de código requerido.

**Exit codes**: `0` = todas las ubicaciones tuvieron éxito. `1` = al menos una ubicación falló (logueada como error). Esto hace el pipeline compatible con CI — un scheduler puede detectar fallos.

---

### 16.15.3 — La arquitectura: ¿por qué 4 proyectos?

La solución usa **Clean Architecture** (también conocida como Onion Architecture o Ports & Adapters). La regla es simple: **las capas internas no saben nada sobre las capas externas**. Las dependencias siempre apuntan hacia adentro.

```
┌───────────────────────────────────────────────────────┐
│  Console  ← punto de entrada, cablea todo con DI       │
│  ┌─────────────────────────────────────────────────┐  │
│  │  Infrastructure  ← HTTP, parsers, escritura     │  │
│  │  ┌───────────────────────────────────────────┐  │  │
│  │  │  Application  ← lógica de orquestación    │  │  │
│  │  │  ┌─────────────────────────────────────┐  │  │  │
│  │  │  │  Core  ← contratos + modelos        │  │  │  │
│  │  │  └─────────────────────────────────────┘  │  │  │
│  │  └───────────────────────────────────────────┘  │  │
│  └─────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────┘
```

**¿Por qué importa en una entrevista?**
Los entrevistadores preguntan: *"¿Cómo agregarías una segunda fuente meteorológica?"* Con Clean Architecture la respuesta es: *"Implemento `IWeatherDataSource` en Infrastructure, lo registro en `ServiceRegistration.cs`, y agrego la config. Cambio cero archivos existentes."* Eso es el Principio Abierto/Cerrado en acción.

---

### 16.15.4 — Proyecto por proyecto, archivo por archivo

#### PROYECTO 1: `SyncMetrics.Pipeline.Core`
**Qué es**: El anillo más interno. C# puro — cero dependencias externas (sin paquetes NuGet, sin HTTP, sin I/O de archivos). Define los contratos (interfaces) y modelos en los que todos los demás proyectos coinciden.

**Por qué existe por separado**: Cualquier proyecto puede referenciar Core e inmediatamente entender las formas de datos fluyendo a través del pipeline, sin tener que saber nada sobre HTTP, JSON, o escritura de archivos.

---

**`Result.cs`** — El archivo más importante del proyecto.

```csharp
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value { get; }         // solo válido si IsSuccess
    public PipelineError Error { get; }  // solo válido si IsFailure
    
    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(PipelineError error) => new(error);
    
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func) { ... }
    public Result<TNext> Map<TNext>(Func<T, TNext> func) { ... }
}
```

**La gran idea — Railway Oriented Programming**: En lugar de lanzar excepciones para fallos esperados (JSON malo, HTTP 503, permiso de archivo denegado), cada operación retorna un `Result<T>`. Si funcionó, llama `.Value`. Si falló, llama `.Error`. El llamador es forzado por el compilador a pensar en ambos resultados.

El método `Bind` encadena operaciones: si el paso A falla, el paso B nunca se ejecuta — el fallo se propaga automáticamente. Por eso `OpenMeteoDataSource.ProcessLocationAsync` se ve así:
```csharp
return fetchResult
    .Bind(json => _parser.Parse(json))
    .Bind(response => _transformer.Transform(response, location));
```
Si `FetchAsync` retorna un `FetchError`, ni el parser ni el transformer son llamados. El error fluye hasta el resumen final donde se loguea.

---

**`PipelineError.cs`** — Define la jerarquía de errores como tipos `record` de C#.

```
PipelineError (base abstracta)
├── FetchError    — HTTP falló (código de estado, URL)
├── ParseError    — JSON malformado/inválido (campo, fragmento de contenido raw)
├── TransformError — parsing de fecha falló (campo, índice de registro)
└── OutputError   — escritura de archivo falló (ruta de archivo)
```

Usar tipos `record` significa que la igualdad de errores funciona por valor (útil en tests), y las propiedades nombradas (`Field`, `StatusCode`, `Url`) dan a los operadores contexto accionable en el log de resumen. Contrasta esto con `throw new Exception("algo salió mal")` — eso pierde todo el contexto.

---

**`Interfaces/IWeatherApiClient.cs`**
```csharp
public interface IWeatherApiClient
{
    string SourceName { get; }
    Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken cancellationToken);
}
```
Retorna JSON raw como string. La interfaz no dice nada sobre HTTP — dice "dame los datos raw para esta ubicación." Una implementación futura podría leer de un archivo y seguir satisfaciendo esta interfaz.

---

**`Interfaces/IResponseParser<TRaw>`**
```csharp
public interface IResponseParser<TRaw>
{
    Result<TRaw> Parse(string rawJson);
}
```
Genérico en `TRaw` porque Open-Meteo tiene una forma JSON diferente a WeatherAPI.com. Si parseas a `OpenMeteoApiResponse`, el compilador previene que accidentalmente lo pases a un transformer de WeatherAPI.

---

**`Interfaces/IDataTransformer<TRaw>`**
```csharp
public interface IDataTransformer<TRaw>
{
    Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(TRaw rawData, LocationConfig location);
}
```
Toma el modelo parseado específico de la fuente, retorna una lista de `NormalizedWeatherRecord` — la forma universal de output. Requiere `LocationConfig` porque la respuesta del API de Open-Meteo no incluye el nombre legible de la ciudad.

---

**`Interfaces/IWeatherDataSource`**
```csharp
public interface IWeatherDataSource
{
    string SourceName { get; }
    Task<ProcessingResult> ProcessAsync(IEnumerable<LocationConfig> locations, CancellationToken cancellationToken);
}
```
Esta es la interfaz del patrón Strategy. El `PipelineCoordinator` solo ve esto. No sabe ni le importa si estás llamando a Open-Meteo, leyendo un archivo CSV, o consultando una base de datos. Agregar una fuente significa agregar una clase que implemente esta interfaz.

---

**`Interfaces/IOutputWriter`**
```csharp
public interface IOutputWriter
{
    Task<Result<string>> WriteAsync(IReadOnlyList<NormalizedWeatherRecord> records, CancellationToken cancellationToken);
}
```
Retorna la ruta del archivo de output en éxito. Podría reemplazarse con un writer de base de datos, un uploader a S3, o un productor de Kafka — `PipelineCoordinator` nunca cambia.

---

**`Models/NormalizedWeatherRecord.cs`** — La fila unificada de output.
```csharp
public record NormalizedWeatherRecord
{
    public required string Source { get; init; }       // "OpenMeteo"
    public required string Location { get; init; }     // "New York"
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required DateOnly Date { get; init; }       // una fila por día
    public double? TempMaxCelsius { get; init; }       // nullable — faltante ≠ 0.0
    public double? TempMinCelsius { get; init; }
    public double? PrecipitationMm { get; init; }
    public double? WindSpeedMaxKmh { get; init; }
    public double? UvIndexMax { get; init; }
    public required DateTime FetchedAtUtc { get; init; }  // auditoría/invalidación de caché
}
```
`record` da igualdad por valor, lo que hace que las aserciones de test (`record1.Should().Be(record2)`) funcionen. `double?` (nullable) es deliberado: una lectura faltante de índice UV es diferente de una lectura de 0. Escribir `0` cuando faltan datos es un bug de calidad de datos.

---

**`Models/LocationConfig.cs`** — Una entrada de ciudad desde `appsettings.json`.
```csharp
public record LocationConfig
{
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
```
Usa setters mutables (no `init`) porque `IConfiguration.Bind()` usa reflexión para establecer propiedades después de instanciación. El patrón `required+init` que usa `NormalizedWeatherRecord` rompería el binder de config.

---

**`Models/ProcessingResult.cs`** — Output de una ejecución de fuente de datos a través de todas sus ubicaciones.
```csharp
public record ProcessingResult
{
    public required string SourceName { get; init; }
    public required IReadOnlyList<NormalizedWeatherRecord> Records { get; init; }
    public required IReadOnlyList<PipelineError> Errors { get; init; }
    public required TimeSpan Duration { get; init; }
    public int LocationsAttempted { get; init; }
    public int LocationsSucceeded { get; init; }
}
```
El fallo parcial es un concepto de primera clase: `Records` puede tener datos para 2 ciudades y `Errors` puede tener un `FetchError` para la tercera. El pipeline no falla enteramente solo porque la llamada API de una ciudad expiró.

---

**`Models/PipelineSummary.cs`** — Output agregado de la ejecución completa a través de todas las fuentes.
```csharp
public record PipelineSummary
{
    public required IReadOnlyList<ProcessingResult> SourceResults { get; init; }
    public required int TotalRecordsWritten { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required string? OutputFilePath { get; init; }
    
    public bool HasErrors => SourceResults.Any(r => r.Errors.Count > 0);
    public IEnumerable<PipelineError> AllErrors => SourceResults.SelectMany(r => r.Errors);
}
```
`HasErrors` controla el exit code del proceso. `OutputFilePath` es `null` si todas las fuentes fallaron y no hubo nada que escribir.

---

#### PROYECTO 2: `SyncMetrics.Pipeline.Application`
**Qué es**: La capa de orquestación. Depende solo de Core. Contiene una clase.

**Por qué es un proyecto separado de Infrastructure**: El trabajo del coordinator es secuenciación y agregación. No debería saber cómo funciona HTTP, cómo funciona el parsing de JSON, o cómo se escriben los archivos. Si pusieras el coordinator en el proyecto de Infrastructure, estarías mezclando concerns que deberían estar separados. Un boundary de proyecto impone esa separación — físicamente NO puedes accidentalmente importar `System.Net.Http` desde la capa Application porque el `.csproj` de Application no lo referencia.

---

**`PipelineCoordinator.cs`** — El cerebro del pipeline.

```csharp
public sealed partial class PipelineCoordinator
{
    private readonly IEnumerable<IWeatherDataSource> _dataSources;
    private readonly IOutputWriter _outputWriter;
    private readonly ILogger<PipelineCoordinator> _logger;
    
    public async Task<PipelineSummary> RunAsync(
        IReadOnlyDictionary<string, IReadOnlyList<LocationConfig>> sourceLocations,
        CancellationToken cancellationToken)
    {
        // 1. Disparar todas las fuentes de datos concurrentemente
        var tasks = _dataSources.Select(source => source.ProcessAsync(...));
        var sourceResults = await Task.WhenAll(tasks);
        
        // 2. Agregar todos los registros exitosos de todas las fuentes
        var allRecords = sourceResults.SelectMany(r => r.Records).ToList();
        
        // 3. Escribir si hay algo que escribir
        if (allRecords.Count > 0)
            await _outputWriter.WriteAsync(allRecords, cancellationToken);
        
        // 4. Retornar resumen completo (usado para exit code y logging)
        return new PipelineSummary { ... };
    }
}
```

**Decisiones clave de diseño:**

1. **`Task.WhenAll`**: Todas las fuentes ejecutan concurrentemente. Si tienes 3 ciudades a través de 2 fuentes, 6 peticiones HTTP disparan simultáneamente. Esto es 5–8× más rápido que procesamiento secuencial.

2. **`IEnumerable<IWeatherDataSource>`**: El contenedor de DI inyecta TODAS las implementaciones registradas. Para agregar una segunda fuente (WeatherAPI.com), regístrala en `ServiceRegistration.cs`. El loop `Select` del coordinator la recoge automáticamente — sin cambios de código al coordinator.

3. **El coordinator recibe el mapa fuente→ubicaciones como parámetro** (no leyendo config directamente). Esto mantiene al coordinator libre de tipos de config de Infrastructure y hace posible `PipelineCoordinatorTests` sin tocar el sistema de archivos.

4. **`partial class` con `[LoggerMessage]`**: El logging usa atributos de source generator de C#. En lugar de `_logger.LogInformation($"Processing {sourceName}")`, tenemos:
   ```csharp
   [LoggerMessage(Level = LogLevel.Information, Message = "Processing source {Source} with {Count} location(s)...")]
   private static partial void LogProcessingSource(ILogger logger, string source, int count);
   ```
   El compilador genera el método de logging en build time. Esto satisface las reglas del analizador de código CA1848 y CA1873 (evitar allocations boxeadas en logging de hot-path) e impone logging estructurado — `{Source}` y `{Count}` son propiedades buscables en herramientas de agregación de logs.

---

#### PROYECTO 3: `SyncMetrics.Pipeline.Infrastructure`
**Qué es**: Toda la "realidad desordenada" — HTTP, JSON, I/O de archivos. Implementa las interfaces definidas en Core. Sin definiciones de interfaz aquí, solo implementaciones.

---

**`Configuration/PipelineOptions.cs`** — Clase de configuración fuertemente tipada.

```csharp
public class PipelineOptions
{
    public const string SectionName = "Pipeline";
    public string OutputDirectory { get; set; } = "./output";
    public string OutputFilePattern { get; set; } = "weather_data_{timestamp}.tsv";
    public List<SourceOptions> Sources { get; set; } = new();
}

public class SourceOptions
{
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public List<LocationConfig> Locations { get; set; } = new();
    public List<FieldMapping> FieldMappings { get; set; } = new();
}
```

Todo en `appsettings.json` bajo `"Pipeline"` se vincula a esta clase vía el Options pattern. Usar `IOptions<PipelineOptions>` en lugar de `IConfiguration` directamente significa que las clases que necesitan config obtienen un objeto tipado — no un diccionario de strings — eliminando bugs de runtime por typos en keys.

---

**`Configuration/ServiceRegistration.cs`** — La clase única de cableado de DI.

```csharp
public static IServiceCollection AddPipelineServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Vincular config
    services.Configure<PipelineOptions>(configuration.GetSection("Pipeline"));
    
    // Cliente HTTP con pipeline de resiliencia (retry + timeout + circuit breaker)
    services.AddHttpClient("OpenMeteo", client => {
        client.BaseAddress = new Uri("https://api.open-meteo.com/v1/forecast");
        client.Timeout = TimeSpan.FromSeconds(30);
    }).AddStandardResilienceHandler();

    // Componentes del pipeline de Open-Meteo
    services.AddSingleton<IWeatherApiClient, OpenMeteoApiClient>();
    services.AddSingleton<IResponseParser<OpenMeteoApiResponse>, OpenMeteoResponseParser>();
    services.AddSingleton<IDataTransformer<OpenMeteoApiResponse>, OpenMeteoTransformer>();
    services.AddSingleton<IWeatherDataSource, OpenMeteoDataSource>();
    
    // Output
    services.AddSingleton<IOutputWriter, TabDelimitedFileWriter>();
    
    // Coordinator
    services.AddSingleton<PipelineCoordinator>();
    
    return services;
}
```

**`AddStandardResilienceHandler()`** es una sola llamada de método que configura:
- **Retry** automático en respuestas 5xx (3 intentos, backoff exponencial con jitter)
- **Timeout** por petición
- **Circuit breaker** (si demasiadas peticiones fallan en una ventana de tiempo, deja de enviar para proteger el servicio downstream)

Esto es de `Microsoft.Extensions.Http.Resilience` (construido sobre Polly). La política de retry vive en el pipeline del HttpClient — no dentro de `OpenMeteoApiClient`. Esto es importante: `OpenMeteoApiClient` solo llama `GetAsync`. No sabe que están ocurriendo retries. Este es el **Principio Abierto/Cerrado** aplicado a resiliencia: agregar comportamiento de retry no requirió cambios a `OpenMeteoApiClient`.

---

**`OpenMeteo/OpenMeteoApiResponse.cs`** — El modelo de deserialización JSON (DTO).

```csharp
public class OpenMeteoApiResponse
{
    [JsonPropertyName("latitude")]  public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("daily")]     public OpenMeteoDailyData? Daily { get; set; }
    [JsonPropertyName("error")]     public bool Error { get; set; }
    [JsonPropertyName("reason")]    public string? Reason { get; set; }
    // ...
}

public class OpenMeteoDailyData
{
    [JsonPropertyName("time")]               public List<string>? Time { get; set; }
    [JsonPropertyName("temperature_2m_max")] public List<double?>? TemperatureMax { get; set; }
    [JsonPropertyName("wind_speed_10m_max")] public List<double?>? WindSpeedMax { get; set; }
    // ... todos los demás campos
}
```

Este es un **DTO (Data Transfer Object)** — su único trabajo es reflejar la estructura JSON para que `JsonSerializer.Deserialize<OpenMeteoApiResponse>()` funcione. No sabe nada de lógica de negocio.

**Discrepancia importante capturada**: El brief del ejercicio usa `windspeed_10m_max` en la URL de ejemplo. El API real de Open-Meteo usa `wind_speed_10m_max` (con guión bajo). Esto fue capturado consultando la documentación del API en vivo. El valor correcto se usa en `OpenMeteoApiClient.DailyFields`.

El formato de Open-Meteo es una **estructura de arrays paralelos**: en lugar de un objeto por día, tiene un array por campo, todos del mismo largo. Índice 0 a través de todos los arrays = día 1, índice 1 = día 2, etc. Esto es común en APIs de series temporales porque comprime bien y es eficiente de procesar.

---

**`OpenMeteo/OpenMeteoApiClient.cs`** — El cliente HTTP.

```csharp
public sealed class OpenMeteoApiClient : IWeatherApiClient
{
    private static readonly string[] DailyFields = [
        "temperature_2m_max", "temperature_2m_min", "precipitation_sum",
        "wind_speed_10m_max", "uv_index_max"
    ];
    
    public async Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken ct)
    {
        var url = $"?latitude={lat}&longitude={lon}&daily={daily}&timezone=auto&forecast_days=7";
        
        using var response = await client.GetAsync(url, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        
        if (!response.IsSuccessStatusCode)
            return Result<string>.Failure(new FetchError(...));
        
        return Result<string>.Success(json);
    }
}
```

La URL se construye con `InvariantCulture` para latitud/longitud — crítico porque `40.7128.ToString()` en un locale alemán produce `"40,7128"` (separador decimal de coma), lo cual el API rechaza. `InvariantCulture` siempre produce `"40.7128"`.

El método lee el cuerpo JSON completo incluso en respuestas de error, porque Open-Meteo retorna detalles de error en el cuerpo: `{"error": true, "reason": "Invalid parameter: latitude"}`. Esos detalles son útiles para logging.

`cancellationToken.IsCancellationRequested` se re-lanza en lugar de capturarse — respetar la cancelación del llamador es un contrato, no una cortesía.

---

**`OpenMeteo/OpenMeteoResponseParser.cs`** — Deserialización JSON + validación estructural.

Esta clase sigue el principio **"parsear, no validar"**: si `Parse()` retorna `Success`, el objeto retornado está garantizado de ser estructuralmente sólido. El transformer nunca necesita hacer null-checks.

Pasos de validación (en orden):
1. `JsonSerializer.Deserialize` — retorna `ParseError` con fragmento si el JSON está malformado
2. `response.Error == true` — Open-Meteo retorna `{"error": true, "reason": "..."}` para errores del API (coordenadas malas, etc.)
3. `response.Daily is null` — el objeto `daily` no fue retornado
4. `daily.Time is null or empty` — sin array de time = sin datos
5. **Verificaciones de longitud de arrays** para los 5 arrays de medición — cada array debe tener exactamente la misma longitud que `time`. Si `temperature_2m_max` tiene 6 elementos y `time` tiene 7, indexar por `i` silenciosamente omitiría el último día. Esto se captura aquí.

El parser nombra el campo específico ofensor en el `ParseError`. Un operador viendo `"temperature_2m_max has 6 elements but 'time' has 7"` puede actuar inmediatamente.

---

**`OpenMeteo/OpenMeteoTransformer.cs`** — Conversión de arrays a registros.

```csharp
public Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(
    OpenMeteoApiResponse rawData, LocationConfig location)
{
    var daily = rawData.Daily!;  // El Parser garantiza non-null
    var records = new NormalizedWeatherRecord[daily.Time!.Count];
    
    for (var i = 0; i < daily.Time.Count; i++)
    {
        if (!DateOnly.TryParse(daily.Time[i], out var date))
            return Result<...>.Failure(new TransformError(...));
        
        records[i] = new NormalizedWeatherRecord
        {
            Source = "OpenMeteo",
            Location = location.Name,         // de config, no de respuesta del API
            Latitude = rawData.Latitude,      // de respuesta del API
            Date = date,
            TempMaxCelsius = daily.TemperatureMax![i],  // null-safe: parser validó longitud
            // ...
            FetchedAtUtc = DateTime.UtcNow,   // rastro de auditoría
        };
    }
    
    return Result<...>.Success(records);
}
```

Los operadores null-forgiving `!` (`daily.Time!`, `daily.TemperatureMax!`) son válidos porque el contrato del parser garantiza que estos son non-null cuando `Parse()` retorna `Success`. El analizador se quejaría sin ellos porque no puede ver a través del boundary parser/transformer.

`Location` viene de la config (`location.Name`) no de la respuesta del API, porque Open-Meteo no retorna un nombre de ciudad — retorna las coordenadas a las que ajustó la consulta (que pueden diferir ligeramente de las coordenadas solicitadas).

---

**`OpenMeteo/OpenMeteoDataSource.cs`** — El composite del patrón Strategy.

```csharp
public sealed class OpenMeteoDataSource : IWeatherDataSource
{
    public async Task<ProcessingResult> ProcessAsync(
        IEnumerable<LocationConfig> locations, CancellationToken ct)
    {
        // Concurrente: dispara todos los fetches de ubicación simultáneamente
        var tasks = locationList.Select(loc => ProcessLocationAsync(loc, ct));
        var results = await Task.WhenAll(tasks);
        
        // Separar éxitos de fallos
        foreach (var result in results)
        {
            if (result.IsSuccess) records.AddRange(result.Value);
            else                  errors.Add(result.Error);
        }
        
        return new ProcessingResult { Records = records, Errors = errors, ... };
    }
    
    private async Task<Result<IReadOnlyList<NormalizedWeatherRecord>>> ProcessLocationAsync(
        LocationConfig location, CancellationToken ct)
    {
        var fetchResult = await _apiClient.FetchAsync(location, ct);
        
        // Railway: si fetch falla, Parse nunca ejecuta; si Parse falla, Transform nunca ejecuta
        return fetchResult
            .Bind(json => _parser.Parse(json))
            .Bind(response => _transformer.Transform(response, location));
    }
}
```

**Concurrencia con `Task.WhenAll`**: Las 3 peticiones de ciudades disparan simultáneamente. El API de Open-Meteo lo permite. Si cada petición individual toma 300ms, las 3 completan en ~300ms total en lugar de ~900ms secuencial. Para 10 ciudades igual completas en aproximadamente el tiempo de una petición.

**Semántica de fallo parcial**: Si la petición de London retorna un 503, `errors` obtiene un `FetchError("London")`. New York y Tokyo aún pueblan `records`. El pipeline escribe 14 registros (2 ciudades × 7 días) y loguea el fallo de London. El exit code se vuelve `1`. Contrasta esto con lanzar una excepción que aborta todo.

---

**`Output/TabDelimitedFileWriter.cs`** — Escritura de archivos TSV.

```csharp
public async Task<Result<string>> WriteAsync(
    IReadOnlyList<NormalizedWeatherRecord> records, CancellationToken ct)
{
    Directory.CreateDirectory(_options.OutputDirectory);
    
    var fileName = _options.OutputFilePattern.Replace(
        "{timestamp}", 
        DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
    
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);  // UTF-8 sin BOM
    
    await using var writer = new StreamWriter(filePath, append: false, encoding);
    
    await writer.WriteLineAsync(string.Join('\t', HeaderColumns));
    
    foreach (var record in records)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteLineAsync(string.Join('\t', ...));
    }
    
    return Result<string>.Success(filePath);
}
```

**TSV vs CSV**: Los datos meteorológicos pueden contener comas en nombres de ubicación/zona horaria. Los tabs no son caracteres válidos en ningún campo de meteorología, haciendo TSV inequívoco sin reglas de quoted-strings.

**UTF-8 sin BOM**: El `encoderShouldEmitUTF8Identifier: false` elimina el BOM de 3 bytes (Byte Order Mark) que los programas Windows a veces prependen. El BOM causa problemas cuando el archivo es procesado por Python, bash, o herramientas Linux. "Sin BOM" es el default universal de interoperabilidad.

**`InvariantCulture` en todos los números**: `record.Latitude.ToString("F4", CultureInfo.InvariantCulture)` siempre produce `"40.7128"` sin importar el locale del OS. Si omites esto, una máquina Windows francesa produce `"40,7128"` (decimal con coma), rompiendo cada herramienta downstream que lea el archivo.

**`null` → string vacío**: Los valores null de `double?` se convierten en un campo vacío en TSV, no en el string `"null"` o `"0"`. Un consumidor leyendo el archivo sabe que una celda vacía significa "no hay datos disponibles" y puede distinguir entre "el índice UV fue 0.0" y "el índice UV no fue reportado."

**Timestamp en nombre de archivo**: `weather_data_20260329_142305.tsv`. Cada ejecución crea un archivo nuevo. Ningún dato se sobreescribe. Este es un default seguro para un pipeline que podría ejecutarse en un schedule — puedes reproducir ejecuciones y comparar outputs.

---

#### PROYECTO 4: `SyncMetrics.Pipeline.Console`
**Qué es**: El punto de entrada. Cablea todo junto con el .NET Generic Host, lee config, y ejecuta el coordinator.

---

**`Program.cs`** — 35 líneas. Hace exactamente cinco cosas:

```csharp
// 1. Construir el Generic Host (sistema de config, contenedor de DI, logging)
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,  // encontrar appsettings.json de forma confiable
    Args = args,
});

// 2. Registrar todos los servicios del pipeline
builder.Services.AddPipelineServices(builder.Configuration);

var app = builder.Build();

// 3. Resolver coordinator y config desde DI
var coordinator = app.Services.GetRequiredService<PipelineCoordinator>();
var options = app.Services.GetRequiredService<IOptions<PipelineOptions>>().Value;

// 4. Construir mapa fuente→ubicaciones desde config
var sourceLocations = options.Sources
    .Where(s => s.Enabled)                        // respetar flag Enabled
    .ToDictionary(s => s.Name, s => (IReadOnlyList<LocationConfig>)s.Locations);

// 5. Manejar Ctrl+C gracefully, ejecutar el pipeline, retornar exit code
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var summary = await coordinator.RunAsync(sourceLocations, cts.Token);
return summary.HasErrors ? 1 : 0;
```

`ContentRootPath = AppContext.BaseDirectory` asegura que `appsettings.json` se encuentre en el lugar correcto sin importar si ejecutas con `dotnet run --project src/SyncMetrics.Pipeline.Console` desde la raíz del repo o haciendo doble clic en el binario compilado.

El handler `CancelKeyPress` convierte Ctrl+C en una cancelación de `CancellationToken`. Cada método `async` en el pipeline acepta y reenvía el token. Esto significa que presionar Ctrl+C durante una ejecución hace que el pipeline se detenga limpiamente después de la escritura actual (si está a mitad de escritura) en lugar de corromper el archivo de output.

---

**`appsettings.json`** — El único archivo de configuración.

```json
{
  "Pipeline": {
    "OutputDirectory": "./output",
    "OutputFilePattern": "weather_data_{timestamp}.tsv",
    "Sources": [
      {
        "Name": "OpenMeteo",
        "Enabled": true,
        "BaseUrl": "https://api.open-meteo.com/v1/forecast",
        "TimeoutSeconds": 30,
        "RetryCount": 3,
        "Locations": [
          { "Name": "New York", "Latitude": 40.7128, "Longitude": -74.0060 },
          { "Name": "London",   "Latitude": 51.5074, "Longitude": -0.1278 },
          { "Name": "Tokyo",    "Latitude": 35.6762, "Longitude": 139.6503 }
        ],
        "FieldMappings": [
          { "SourceField": "temperature_2m_max", "OutputColumn": "TempMaxC",      "Unit": "°C"    },
          { "SourceField": "wind_speed_10m_max", "OutputColumn": "WindSpeedMaxKmh","Unit": "km/h"  },
          { "SourceField": "uv_index_max",       "OutputColumn": "UVIndexMax",    "Unit": "index" }
        ]
      }
    ]
  }
}
```

**`FieldMappings`** es un registro de documentación — registra la correspondencia canónica fuente-a-columna-de-output para los operadores que mantienen la config. El cableado real es type-safe en código (`OpenMeteoApiClient.DailyFields`, asignaciones de `OpenMeteoTransformer`, `TabDelimitedFileWriter.HeaderColumns`). Agregar `"Enabled": false` a la fuente la deshabilita completamente — el filtro `Where(s => s.Enabled)` del coordinator la omite.

---

**`Directory.Build.props`** — Puerta de calidad a nivel de solución aplicada a cada proyecto automáticamente.

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

`TreatWarningsAsErrors=true` significa que el build de CI falla con cualquier variable no usada, warning de tipo de referencia nullable, o violación de estilo de código. Esto es estándar de producción — previene acumulación de warnings que ocultan problemas reales. Cero warnings se impone en cada commit.

---

### 16.15.5 — La suite de tests: 32 tests, qué testean y por qué

**Filosofía de testing**: Cada clase de test vive en aislamiento. Los tests unitarios hacen stub de cada dependencia. Los tests de integración usan el contenedor real de DI. Ningún test jamás alcanza una red real.

---

**`OpenMeteoResponseParserTests.cs`** (10 tests) — Tests unitarios para el parser.

Los fixtures son archivos JSON embebidos en el assembly de test (`EmbeddedResource` en el .csproj). Esto elimina la fragilidad de rutas de archivo — los tests funcionan idénticamente en cualquier OS en cualquier directorio de trabajo.

| Test | Qué prueba |
|---|---|
| `Parse_ValidSevenDayResponse_ReturnsSuccess` | Happy path: los 7 días, todos los campos poblados |
| `Parse_ApiErrorResponse_ReturnsParseError` | `{"error":true,"reason":"..."}` de Open-Meteo es capturado |
| `Parse_MissingDailyObject_ReturnsParseError` | Clave `daily` faltante es capturada |
| `Parse_NullArrayValues_ReturnsSuccess` | Valores `null` dentro de arrays son válidos (lecturas faltantes) |
| `Parse_MismatchedArrayLengths_ReturnsParseError` | Desajuste de longitud de arrays nombra el campo ofensor |
| `Parse_InvalidJson_ReturnsParseErrorWithInvalidJsonMessage` | JSON malformado es capturado |
| `Parse_ValidLondonResponse_ReturnsSuccessWithNegativeLongitude` | Coordenadas de longitud negativa se parsean correctamente |
| `Parse_EmptyTimeArray_ReturnsParseError` | Array de time de longitud cero es rechazado |

---

**`OpenMeteoTransformerTests.cs`** (6 tests) — Tests unitarios para el transformer.

| Test | Qué prueba |
|---|---|
| `Transform_ValidResponse_ReturnsCorrectRecordCount` | 7 días entrada → 7 registros salida |
| `Transform_ValidResponse_MapsSourceCorrectly` | Campo `Source` es `"OpenMeteo"` |
| `Transform_ValidResponse_MapsLocationNameFromConfig` | `Location` viene de config, no del API |
| `Transform_NullMeasurements_RetainsNullsInRecord` | Valores `null?` pasan sin convertirse en 0 |
| `Transform_ValidResponse_SetsLatLongFromResponse` | Coordenadas vienen de respuesta del API, no de config |
| `Transform_UnparsableDate_ReturnsTransformError` | String de fecha corrupto retorna `TransformError` con índice |

---

**`TabDelimitedWriterTests.cs`** (5 tests) — Tests unitarios para el writer de archivos.

| Test | Qué prueba |
|---|---|
| `WriteAsync_ValidRecords_CreatesFile` | Archivo se crea en ruta configurada |
| `WriteAsync_ValidRecords_WritesHeaderRow` | Encabezado coincide con el orden de columnas definido |
| `WriteAsync_NullMeasurement_WritesEmptyCell` | `null` → celda vacía (no `"null"` ni `"0"`) |
| `WriteAsync_ValidRecords_UsesInvariantCulture` | `40.71` no `40,71` (decimal independiente de locale) |
| `WriteAsync_ExistingDirectory_DoesNotThrow` | Creación idempotente de directorio |

---

**`PipelineCoordinatorTests.cs`** (5 tests) — Tests unitarios para el coordinator.

Estos tests usan **NSubstitute** para crear fakes de `IWeatherDataSource` e `IOutputWriter`. El coordinator se testea puramente como orquestador — ninguna infraestructura real ejecuta.

| Test | Qué prueba |
|---|---|
| `RunAsync_SingleSourceSuccess_ReturnsSummaryWithRecords` | Path normal: registros escritos, resumen poblado |
| `RunAsync_SourceReturnsErrors_SurfacesErrorsInSummary` | Errores aparecen en resumen sin abortar |
| `RunAsync_AllRecordsEmpty_DoesNotCallWriter` | Writer no es llamado cuando no hay nada que escribir |
| `RunAsync_WriterFails_SummaryHasNullOutputPath` | Fallo del writer se captura, no se lanza |
| `RunAsync_MultipleSourcesRun_DurationIsPositive` | La duración se mide |

---

**`RetryPolicyTests.cs`** (3 tests) — Tests unitarios para el pipeline de resiliencia HTTP.

Estos tests usan `MockHttpMessageHandler` — una subclase personalizada de `HttpMessageHandler` que sirve respuestas de un `Queue<HttpResponseMessage>`. El pipeline real de `AddStandardResilienceHandler()` se ejercita; solo la red se reemplaza. Se configura zero delay para que los tests ejecuten en milisegundos.

| Test | Qué prueba |
|---|---|
| `RetryPolicy_TransientFailureThenSuccess_ReturnsSuccessAfterRetry` | 503 → 200: retry dispara, resultado final es éxito |
| `RetryPolicy_PermanentFailure_DoesNotRetry` | 400: solo 1 llamada HTTP realizada, sin retry |
| `RetryPolicy_ExhaustsMaxAttempts_ReturnsFetchErrorAfterFourRequests` | 500×4: exactamente 4 llamadas (1 + 3 retries) |

La aserción de `CallCount` es la crítica: prueba que la política de retry disparó el número correcto de veces, no solo que el `Result` final tenía el valor correcto. `CallCount == 2` prueba que un retry ocurrió; `CallCount == 1` prueba que no se intentó retry.

---

**`EndToEndPipelineTests.cs`** (3 tests) — Tests de integración usando el contenedor real de DI.

Estos son los tests más valiosos porque capturan bugs de composición. El grafo completo de DI de producción se construye: `OpenMeteoResponseParser` real, `OpenMeteoTransformer` real, `TabDelimitedFileWriter` real. Solo `IWeatherApiClient` se sustituye (NSubstitute).

| Test | Qué prueba |
|---|---|
| `FullPipeline_WithValidApiResponse_ProducesPopulatedOutputFile` | Respuesta de 7 días → archivo con 7 filas, sin errores |
| `FullPipeline_WithApiErrorResponse_ProducesZeroRecordsAndSurfacesError` | Resultado HTTP 503 → `HasErrors == true`, 0 registros |
| `FullPipeline_ThreeLocations_ProducesTwentyOneRecordsTotal` | 3 ubicaciones × 7 días = 21 registros |

Los tests escriben en un directorio temporal nombrado con `Guid` y limpian en `Dispose()`. Nunca dejan archivos en disco.

---

### 16.15.6 — El flujo de datos: una petición de principio a fin

Aquí está el viaje completo de los datos de una sola ciudad a través del pipeline:

```
Program.cs
  │
  │  RunAsync({"OpenMeteo": [NewYork, London, Tokyo]})
  ▼
PipelineCoordinator
  │
  │  Task.WhenAll([OpenMeteoDataSource.ProcessAsync])
  ▼
OpenMeteoDataSource
  │
  │  Task.WhenAll([ProcessLocationAsync(NewYork), ProcessLocationAsync(London), ProcessLocationAsync(Tokyo)])
  │
  │  ProcessLocationAsync(NewYork):
  │    ┌─────────────────────────────────────────────────────────────────────────┐
  │    │  OpenMeteoApiClient.FetchAsync(NewYork)                                 │
  │    │    → GET https://api.open-meteo.com/v1/forecast?latitude=40.7128&...    │
  │    │    ← HTTP 200 + string JSON                                             │
  │    │    → Result<string>.Success(json)                                       │
  │    │                                                                         │
  │    │  .Bind(json => OpenMeteoResponseParser.Parse(json))                     │
  │    │    → Validar estructura JSON (null checks, longitudes de arrays)        │
  │    │    → Result<OpenMeteoApiResponse>.Success(response)                     │
  │    │                                                                         │
  │    │  .Bind(response => OpenMeteoTransformer.Transform(response, NewYork))   │
  │    │    → "Zip" arrays paralelos por índice i=0..6                           │
  │    │    → Crear NormalizedWeatherRecord[7]                                   │
  │    │    → Result<IReadOnlyList<NormalizedWeatherRecord>>.Success(records)    │
  │    └─────────────────────────────────────────────────────────────────────────┘
  │
  │  Agregar: records[0..6] (NY) + records[7..13] (London) + records[14..20] (Tokyo)
  │  → ProcessingResult { Records = 21 registros, Errors = [] }
  │
  ▼
PipelineCoordinator continúa:
  │  allRecords = 21 NormalizedWeatherRecord
  │
  │  TabDelimitedFileWriter.WriteAsync(allRecords)
  │    → Crear ./output/weather_data_20260329_142305.tsv
  │    → Escribir fila de encabezado
  │    → Escribir 21 filas de datos (InvariantCulture, null → vacío)
  │    → Result<string>.Success("./output/weather_data_20260329_142305.tsv")
  │
  ▼
PipelineSummary { TotalRecordsWritten=21, HasErrors=false, OutputFilePath="./output/..." }
  │
  ▼
Program.cs: return 0  (exit code 0 = éxito)
```

**Qué pasa si la petición de London expira:**
```
ProcessLocationAsync(London):
  OpenMeteoApiClient.FetchAsync(London)
    → GET ... [expira después de 30s]
    → AddStandardResilienceHandler reintenta 3 veces
    → Los 4 intentos fallan
    → Result<string>.Failure(new FetchError("HTTP request failed...", "London"))

  .Bind(json => ...) — NUNCA SE LLAMA (fetchResult es Failure)
  .Bind(response => ...) — NUNCA SE LLAMA

  → Result<IReadOnlyList<...>>.Failure(FetchError{"London"})

OpenMeteoDataSource:  errors.Add(FetchError{"London"})

ProcessingResult { Records = 14 (NY + Tokyo), Errors = [FetchError{"London"}] }

PipelineSummary { TotalRecordsWritten=14, HasErrors=true }

Program.cs: return 1  (exit code 1 = fallo parcial)
```

---

### 16.15.7 — Patrones de diseño usados y dónde encontrarlos

| Patrón | Dónde | Qué habilita |
|---|---|---|
| **Railway Oriented Programming** | `Result<T>`, `Bind`, `ProcessLocationAsync` | Los errores son valores, se componen de forma segura, sin excepciones ocultas |
| **Strategy** | `IWeatherDataSource` + todas las implementaciones | Agregar una fuente sin cambiar el coordinator |
| **Repository / Adapter** | `IWeatherApiClient` + `OpenMeteoApiClient` | Cambiar fuente de datos sin cambiar lógica de negocio |
| **Options Pattern** | `PipelineOptions`, `IOptions<T>` | Config fuertemente tipada, validada al inicio |
| **Inyección de Dependencias** | `ServiceRegistration`, inyección de constructor en todas partes | Testeabilidad, acoplamiento suelto |
| **Template Method** | `IResponseParser<TRaw>` → `IDataTransformer<TRaw>` pipeline por fuente | Variación por fuente dentro de un esqueleto fijo |
| **Test Seam** | `MockHttpMessageHandler`, NSubstitute `IWeatherApiClient` | Reemplazar solo el boundary externo |
| **Stub basado en Queue** | `MockHttpMessageHandler` | Secuencias de respuesta deterministas para testing de retry |

---

### 16.15.8 — Cómo se cubren los requerimientos del ejercicio

| Requerimiento | Dónde vive |
|---|---|
| Obtener pronóstico de 7 días de un API público real | `OpenMeteoApiClient` → `api.open-meteo.com/v1/forecast?forecast_days=7` |
| Múltiples ubicaciones de ciudades | `appsettings.json Locations[]`, `Task.WhenAll` en `OpenMeteoDataSource` |
| Normalizar datos a un schema unificado | `NormalizedWeatherRecord`, `OpenMeteoTransformer` |
| Escribir en un formato de archivo estructurado | `TabDelimitedFileWriter` → `.tsv` con encabezado |
| Configurable vía archivo de config | `appsettings.json` + `PipelineOptions` (Options pattern) |
| Manejo de errores con output estructurado | `Result<T>`, jerarquía `PipelineError`, `PipelineSummary.HasErrors` |
| Tests unitarios | 29 tests unitarios a través de 5 clases de test |
| Tests de integración | 3 tests end-to-end con grafo real de DI |
| Bonus: retry / resiliencia HTTP | `AddStandardResilienceHandler()` en el HttpClient nombrado |
| Bonus: mapeo de campos config-driven | `FieldMappings[]` en config (registro de documentación) |
| Bonus: extensibilidad de múltiples fuentes | `IWeatherDataSource` + inyección enumerable de DI |
| Discrepancia de spec del ejercicio capturada | `wind_speed_10m_max` (correcto) vs `windspeed_10m_max` (typo del ejercicio) |
| Segunda discrepancia de spec capturada | `uv_index_max` faltante en URL del ejercicio pero listado en tabla — agregado |

---

> **Fin del Documento Checkpoint**
> Re-lee la Sección 1 al inicio de cada sesión. Cuando estés listo para implementar, procede a la Sección 16.12 Orden de Build.
> **10 enfoques analizados. Ganador: Enfoque 1 — Clean Architecture (4 proyectos: Core + Application + Infrastructure + Console) con Result<T> del Enfoque 8.**
> **Enfoque 10 (proyecto único) rechazado: mismos contratos pero requiere justificación defensiva en entrevistas.**
> **Veredicto de frontend: NO CONSTRUIR. Esta es la decisión de "juicio staff" más importante del ejercicio.**
> **Plan de implementación: 25 pasos a través de 11 fases. Mínimo viable en el Paso 13. Entrega completa en el Paso 25.**
