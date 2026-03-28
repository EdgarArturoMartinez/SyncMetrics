# Arthur_Checkpoint — Staff Engineer Technical Exercise

> **Purpose**: This is Arturo's strategic planning document. It captures the full analysis of the Staff Engineer Take-Home Exercise (Data Integrations) — requirements decomposition, **ten** architectural approaches compared in depth (including React frontend analysis), database analysis, Docker strategy, CI/CD plan, and interview preparation notes.  
> **Use**: Re-read Section 1 at the start of every session to regain full context in under 2 minutes.  
> **Last Updated**: Session 2 — March 28, 2026 (expanded from Big 6 to Big 10, added frontend assessment, updated final verdict).

---

## 1. Executive Context & Session Summary (Read This First Every Session)

**Company**: SyncMetrics Inc. (via Actabl hiring pipeline)  
**Role**: Staff Engineer — Data Integrations  
**Exercise**: Build an extensible C# ingestion pipeline that fetches 7-day weather forecasts from the free Open-Meteo API, normalizes them, and writes tab-delimited output files for downstream analytics.

**Core Deliverables Checklist**:
- [ ] .NET 8 C# solution — runs with `dotnet run`, tests pass with `dotnet test`
- [ ] Fetches daily weather data for 3+ configurable locations concurrently
- [ ] Normalized tab-delimited output file with self-designed schema
- [ ] Processing summary printed on completion
- [ ] Architecture extensible — add a second API source with different JSON shape with minimal code
- [ ] HTTP, parsing, transformation, and output are separate concerns
- [ ] HTTP calls behind an interface
- [ ] Error handling: HTTP failures, malformed JSON, missing fields, unparseable values
- [ ] Tests: response parsing (valid + malformed), transformation logic, output formatting, end-to-end with mocked HTTP
- [ ] Bonus: Retry logic with exponential backoff for transient HTTP failures
- [ ] Bonus: Config-driven field mapping (JSON/YAML configuration for source-to-normalized-schema mapping)
- [ ] `README.md` — setup, how to run, assumptions, trade-offs
- [ ] `AI.md` — 10–15 lines on AI usage, overrides, and deliberate non-use
- [ ] Zip or repo link submission

**What They Evaluate (in priority order)**:
1. **Architecture** — Pipeline decomposition, interface boundaries, extensibility
2. **Integration Patterns** — HTTP abstraction, non-trivial JSON parsing, schema normalization
3. **Error Handling** — Resilience, structured error reporting (no silent failures)
4. **Testability** — Mocked HTTP, meaningful edge-case coverage
5. **AI Usage** — Where AI drove, where human judgment overrode
6. **Staff Judgment** — What was generalized vs. kept simple, trade-offs articulated

**Arthur's Key Strengths That Map Directly**:
- 16+ years .NET/C#, including .NET 8, Clean Architecture, Microservices
- Extensive ETL/ELT pipeline experience (SSIS, Azure Functions, batch processing)
- Azure Cloud (Functions, Service Bus, DevOps), Docker, CI/CD
- REST API integrations with OAuth/API-key auth patterns
- Configuration-driven architecture (feature flags, JSON/YAML mappings)
- CQRS, Entity Framework, Redis caching
- AI-assisted development: GitHub Copilot, Azure OpenAI, Anthropic Claude

---

## 2. Requirements Deep Dive & API Analysis

### 2.1 Open-Meteo API — What the Exercise Says vs. What the API Actually Does

**CRITICAL CATCH #1 — URL Discrepancy (Interview Talking Point)**:  
The exercise endpoint shows:
```
&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,windspeed_10m_max
```
But the actual Open-Meteo API documentation uses `wind_speed_10m_max` (with underscores). The exercise URL uses `windspeed_10m_max` (no underscore between "wind" and "speed"). According to the official API docs daily parameter definition, the correct field name is **`wind_speed_10m_max`**.

> **Staff Engineer Signal**: Catching this discrepancy between spec and actual API behavior is exactly the kind of attention to detail expected. In the interview, mention: *"I validated the exercise endpoint against the live API docs and found a naming discrepancy in the wind speed field — the API uses underscored naming consistently. I used the actual API field name."*

**CRITICAL CATCH #2 — Missing Field in URL**:  
The exercise lists `uv_index_max` in the "Fields to ingest" table but does NOT include it in the query string URL. The API docs confirm `uv_index_max` IS an available daily parameter. Our implementation must add it.

**Corrected Endpoint**:
```
GET https://api.open-meteo.com/v1/forecast
    ?latitude={lat}&longitude={lon}
    &daily=temperature_2m_max,temperature_2m_min,precipitation_sum,wind_speed_10m_max,uv_index_max
    &timezone=auto
    &forecast_days=7
```

### 2.2 API Response Shape (from official docs)

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

**Key structural observations**:
- Daily data comes as parallel arrays (time[] + one float[] per variable) — NOT as an array of day objects
- This means parsing requires "zipping" the arrays by index
- `daily_units` provides unit metadata — useful for normalization validation
- Error responses: HTTP 400 with `{ "error": true, "reason": "..." }`
- Coordinates in response may differ slightly from request (grid-cell snapping)

### 2.3 Locations Configuration

| Location | Latitude | Longitude |
|----------|----------|-----------|
| New York | 40.7128  | -74.0060  |
| London   | 51.5074  | -0.1278   |
| Tokyo    | 35.6762  | 139.6503  |

These must be **configurable** — not hardcoded. This aligns with the bonus for config-driven field mapping.

### 2.4 Implicit Requirements (Staff Engineer reads between the lines)

1. **"Configurable set of locations"** → Locations come from configuration (appsettings.json), not code
2. **"Normalized tab-delimited output — design the schema yourself"** → They want to see schema design judgment
3. **"Processing summary on completion"** → Structured log/console output showing counts, errors, durations
4. **"Second API source with completely different JSON shape"** → The architecture MUST have an abstraction layer where a new source plugin provides its own parser/transformer but reuses the pipeline
5. **"HTTP, parsing, transformation, and output are separate concerns"** → Explicit pipeline stages, NOT a single method doing everything
6. **"No silent failures"** → Every error must be logged/reported. Pipeline should be resilient (continue other locations if one fails) but report all failures

---

## 3. The Big Ten — Architectural Approaches

### APPROACH 1: Clean Architecture Console App with Pipeline + Strategy Pattern

**Description**:  
A .NET 8 Console Application organized in Clean Architecture layers (Domain, Application, Infrastructure, Presentation/Console), but scoped appropriately for a pipeline tool (not a full enterprise app). The pipeline is orchestrated by a coordinator that executes discrete stages: **Fetch → Parse → Transform → Output**. Each data source implements a `IWeatherDataSource` strategy interface. The `HttpClient` is behind `IWeatherApiClient`. Output writing is behind `IOutputWriter`.

**Project Structure**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/          # Domain models, interfaces, enums
│   ├── SyncMetrics.Pipeline.Application/   # Pipeline orchestrator, transformation logic
│   ├── SyncMetrics.Pipeline.Infrastructure/ # HTTP clients, file writers, config
│   └── SyncMetrics.Pipeline.Console/       # Entry point, DI setup, console output
├── tests/
│   ├── SyncMetrics.Pipeline.UnitTests/
│   └── SyncMetrics.Pipeline.IntegrationTests/
├── README.md
├── AI.md
└── SyncMetrics.WeatherPipeline.sln
```

**How Extensibility Works**:  
Adding a second source (e.g., WeatherAPI.com) means:
1. Create a new class implementing `IWeatherApiClient` with source-specific HTTP calls
2. Create a new class implementing `IResponseParser<TRawResponse>` for its JSON shape
3. Register it in DI — the pipeline orchestrator discovers and runs all registered sources
4. The `IDataTransformer` normalizes source-specific parsed data into the unified `NormalizedWeatherRecord`

**Why This Approach Fits**:
- **Matches the evaluation criteria perfectly**: Clear pipeline decomposition, interface boundaries at every stage, extensibility via new implementations
- **Shows "staff judgment"**: Clean Architecture structure but NOT over-layered. No CQRS, no MediatR, no message bus — just clean interfaces with a clear pipeline flow
- **Testability**: Every interface can be mocked. Pipeline stages tested in isolation. End-to-end test with mocked HTTP
- **Simple to run**: `dotnet run` bootstrap with `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Configuration`
- **Familiar pattern**: Arthur's CV shows Clean Architecture and Microservices experience

**Why This One Might Not Be Chosen**:
- Multiple projects add some overhead for a take-home — evaluators may see it as slightly over-structured for the scope
- Clean Architecture "layers" can feel ceremonial if the domain logic is simple

**Database Fit**: No database needed. File output. If forced: SQLite for local state, but unnecessary.  
**Docker Fit**: Simple `Dockerfile` with `dotnet publish` → scratch/alpine image. Optional but clean.  
**CI/CD Fit**: GitHub Actions — `dotnet restore → build → test → publish`. Straightforward.

---

### APPROACH 2: Azure Functions + Durable Functions Orchestration

**Description**:  
A serverless pipeline built on Azure Functions with Durable Functions for orchestration. An HTTP-triggered Orchestrator function fans out to Activity functions that fetch each location concurrently (`Task.WhenAll`), then fans in results for transformation and writes to Azure Blob Storage as tab-delimited files. Uses Azure Table Storage or Cosmos DB for processing metadata.

**Project Structure**:
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
│   └── SyncMetrics.Shared/    # Shared models, interfaces
├── tests/
│   └── SyncMetrics.Functions.Tests/
├── local.settings.json
├── host.json
├── README.md
└── AI.md
```

**How Extensibility Works**:  
New API source = new Activity Function with its own fetch + parse logic. The Orchestrator calls all source activities and combines results. Source registration is config-driven in `local.settings.json`.

**Why This Approach Is Tempting (and why Arthur might want to use it)**:
- Arthur's CV highlights Azure Functions, Service Bus, and cloud-native architecture — this would showcase those skills directly
- Durable Functions' fan-out/fan-in pattern is elegant for concurrent multi-location fetching
- Production-ready: automatic scaling, retry policies built-in, monitoring via Application Insights
- Shows Azure mastery that Actabl might value for their stack

**Why This Approach Should NOT Be Chosen**:
- **Over-engineering signal**: The exercise says `dotnet run` and `dotnet test`. Azure Functions require either the Azure Functions Core Tools Runtime (`func start`) or Azurite emulator. The evaluators want to type `dotnet run` and see it work — not install Azure tooling
- **Violates "staff judgment" evaluation**: A Staff Engineer should know when cloud infrastructure is overkill. This is a data transformation pipeline, not a distributed system. Using Durable Functions to orchestrate 3 HTTP calls is like using Kubernetes to serve a static page
- **Testing complexity**: Durable Functions orchestration testing requires specialized test patterns (mock `IDurableOrchestrationContext`). The testing overhead doesn't match the problem complexity
- **Cost of Azure dependency**: Evaluator may not have an Azure subscription. Even with Azurite, the setup friction fails the "runs on their environment" requirement
- **Hidden costs**: Arthur would need to pay for Azure services just for a take-home exercise

**Database Fit**: Azure Table Storage for metadata (overkill). Cosmos DB (massive overkill).  
**Docker Fit**: Docker + Azurite emulator container for local dev. Adds significant Docker Compose complexity.  
**CI/CD Fit**: Azure DevOps or GitHub Actions → Azure Functions deployment. Powerful but irrelevant for the exercise.

**VERDICT: DO NOT USE. Keep Azure skills for the interview discussion, not the codebase. Mention: "I considered Azure Functions for the fan-out pattern but chose simplicity for portability — I'd design the migration path to Azure in a production conversation."**

---

### APPROACH 3: MediatR/Mediator Pipeline with CQRS Behaviors

**Description**:  
A .NET 8 Console App using MediatR as the pipeline backbone. Each pipeline stage is a MediatR Handler. Cross-cutting concerns (logging, validation, retry, error handling) are MediatR Pipeline Behaviors. Data sources are represented as MediatR Requests/Notifications. The pipeline is: `FetchWeatherCommand` → `ParseWeatherHandler` → `TransformWeatherHandler` → `WriteOutputHandler`, with behaviors wrapping each step.

**Project Structure**:
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

**How Extensibility Works**:  
New API source = new `IRequest<WeatherData>` command + handler. MediatR auto-discovers handlers via assembly scanning. Pipeline behaviors apply universally.

**Why This Approach Is Tempting**:
- MediatR is a very popular pattern in .NET enterprise apps — shows Arthur knows modern .NET patterns
- Pipeline Behaviors are an elegant way to apply cross-cutting concerns without cluttering business logic
- CQRS separation can demonstrate architectural sophistication
- Arthur's CV mentions CQRS experience

**Why This Approach Should NOT Be Chosen**:
- **Abstraction mismatch**: MediatR is designed for request/response mediation (commands and queries). A data ingestion pipeline is a sequential flow, not a command dispatch. Forcing pipeline stages into MediatR's request/handler model is a semantic mismatch — it's using a screwdriver as a hammer
- **Over-abstraction tax**: The exercise has ~4 stages. MediatR adds: `IRequest<T>`, `IRequestHandler<TReq, TRes>`, `IPipelineBehavior<TReq, TRes>`, DI registration, assembly scanning. That's a lot of ceremony for `Fetch → Parse → Transform → Write`
- **Testing is harder, not easier**: MediatR handlers are easy to unit test in isolation, but testing the pipeline flow requires integration testing through the mediator. A direct pipeline coordinator with injected interfaces is simpler to test end-to-end
- **Dependency bloat**: Adding MediatR + MediatR.Extensions.Microsoft.DependencyInjection + FluentValidation (for validation behaviors) for a console pipeline tool is a red flag for "Staff judgment"
- **Interview risk**: If asked "why MediatR for a pipeline?", there's no strong answer that doesn't sound like "because I wanted to show I know MediatR"

**Database Fit**: Irrelevant. MediatR doesn't change the output strategy.  
**Docker Fit**: Same as any console app.  
**CI/CD Fit**: Same as any console app.

**VERDICT: DO NOT USE. MediatR is the right tool when you have many unrelated commands/queries flowing through a single app (like a Web API with 50 endpoints). It's the wrong tool for a linear data pipeline with 4 stages. In the interview, say: "I considered MediatR for cross-cutting concerns but recognized the pipeline is sequential, not dispatch-based — a Pipeline Coordinator pattern was a better semantic fit."**

---

### APPROACH 4: Channel-based Producer/Consumer with Generic Host

**Description**:  
A .NET 8 app using `Microsoft.Extensions.Hosting` (Generic Host) with `IHostedService` for lifecycle management. The pipeline uses `System.Threading.Channels` for concurrent producer/consumer stages. Stage 1 (Fetch) produces `RawApiResponse` objects into a `Channel<RawApiResponse>`. Stage 2 (Parse/Transform) reads from that channel and produces `NormalizedRecord` objects into a second channel. Stage 3 (Output) reads from the second channel and writes to the tab-delimited file. Bounded channels provide backpressure.

**Project Structure**:
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
│   └── SyncMetrics.Console/  # Generic Host setup
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**How Extensibility Works**:  
New API source = new `IFetchStage` implementation writing to the same `Channel<RawApiResponse>`. Multiple producers can feed the same channel concurrently. The transform and output stages are source-agnostic if the raw response carries a source discriminator.

**Why This Approach Is Tempting**:
- `System.Threading.Channels` is a high-performance, first-party .NET primitive — no external dependencies
- Genuine concurrency with backpressure — demonstrates deep understanding of .NET async patterns
- The Generic Host provides built-in configuration, logging, graceful shutdown, and DI
- Streaming architecture: data flows through the pipeline in real-time rather than batch collect-then-process
- Shows that Arthur understands concurrent data processing patterns at a low level

**Why This Approach Should NOT Be Chosen**:
- **Complexity vs. problem scale**: We're fetching 3 locations × 7 days = 21 data points. `Channel<T>` with backpressure is designed for high-throughput scenarios (thousands/second). Using channels for 3 HTTP calls is like building a highway for 3 cars
- **Testing channels is non-trivial**: Unit testing channel-based pipelines requires careful orchestration of producers/consumers with cancellation tokens. An `await Task.WhenAll` approach is simpler to test
- **Readability cost**: A new teammate (the README audience) needs to understand channels, bounded capacity, completion semantics, and `ReadAllAsync` to follow the pipeline. A simple `foreach` over locations is immediately clear
- **Generic Host adds startup ceremony**: `Host.CreateDefaultBuilder`, `ConfigureServices`, `RunAsync` — it's the right pattern for long-running services, not a run-once pipeline tool
- **The exercise says "pipeline," not "streaming system"**: The evaluators want to see clean decomposition, not concurrent infrastructure patterns. Three `Task.WhenAll` calls achieve the same concurrency goal with 90% less cognitive overhead

**Database Fit**: Could use channels to stream data into DB writes. But no DB is needed.  
**Docker Fit**: Same as console app. Generic Host supports `SIGTERM` for graceful Docker shutdown.  
**CI/CD Fit**: Same as any console app.

**VERDICT: DO NOT USE for this exercise. GREAT pattern to mention in the interview: "For a high-volume production pipeline, I'd use System.Threading.Channels for backpressure-controlled streaming — but for this exercise's scale, Task.WhenAll keeps the concurrency model simple and readable."**

---

### APPROACH 5: Vertical Slice Architecture

**Description**:  
Instead of horizontal layers (Domain, Application, Infrastructure), organize by feature/source. Each data source (Open-Meteo, future WeatherAPI, etc.) is a completely self-contained vertical slice with its own HTTP client, parser, transformer, and models. A thin shared kernel provides the `NormalizedWeatherRecord` model and the `IWeatherSlice` interface. A top-level coordinator runs all registered slices and merges their outputs.

**Project Structure**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/          # NormalizedWeatherRecord, IWeatherSlice, IOutputWriter
│   ├── SyncMetrics.Pipeline.OpenMeteo/     # Everything for Open-Meteo in one place
│   │   ├── OpenMeteoClient.cs
│   │   ├── OpenMeteoResponse.cs
│   │   ├── OpenMeteoParser.cs
│   │   ├── OpenMeteoTransformer.cs
│   │   └── OpenMeteoSlice.cs               # Implements IWeatherSlice
│   ├── SyncMetrics.Pipeline.Console/       # DI, coordinator, file writer
│   └── (future: SyncMetrics.Pipeline.WeatherApi/)
├── tests/
│   ├── SyncMetrics.Pipeline.OpenMeteo.Tests/
│   └── SyncMetrics.Pipeline.Console.Tests/
└── SyncMetrics.sln
```

**How Extensibility Works**:  
Adding a second source = add a new project `SyncMetrics.Pipeline.WeatherApi/` with its own client, parser, transformer, and slice. Register `WeatherApiSlice : IWeatherSlice` in DI. The coordinator iterates all `IWeatherSlice` instances. Zero changes to existing code.

**Why This Approach Is Tempting**:
- **Maximum cohesion**: Everything related to Open-Meteo lives in one folder. A developer working on Open-Meteo never needs to navigate across layers
- **Perfect extensibility story**: Open + Closed principle at the project level. New source = new project, zero existing code modified
- **Independent testability per source**: Each slice has its own test project, tested end-to-end within the slice
- **Clean dependency graph**: `Console → [OpenMeteo, WeatherApi] → Core`. No circular dependencies possible
- **Interview-friendly**: Easy to draw on a whiteboard, easy to explain

**Why This Might Not Be Chosen**:
- **Code duplication risk**: If two sources have similar HTTP patterns (retry, timeout, headers), each slice duplicates that logic unless there's a shared base. But this is manageable with a shared `ResilienceHttpClient` in Core
- **Many small projects**: For a take-home with one actual source, 3-4 projects might feel over-split
- **The exercise says "HTTP, parsing, transformation, and output are separate concerns"**: Vertical Slice bundles them per source. The evaluator might expect horizontal pipeline stages more clearly visible
- **Transformation logic may have cross-source commonality**: Temperature conversion, unit normalization — these might belong in a shared transformer, not duplicated per slice

**Database Fit**: Each slice could write to its own table. But still no DB needed.  
**Docker Fit**: Same as console app.  
**CI/CD Fit**: Each slice project could be built/tested independently. Nice modularity.

**VERDICT: STRONG CONTENDER. This approach has the cleanest extensibility story. However, the exercise explicitly says "HTTP, parsing, transformation, and output are separate concerns" — this implies they want to SEE horizontal pipeline stages, not vertically-bundled slices. We can HYBRIDIZE this: use vertical organization for source-specific code but maintain clear horizontal interfaces (IApiClient, IResponseParser, IDataTransformer, IOutputWriter) that are visible across the pipeline.**

---

### APPROACH 6: Plugin-based Modular Pipeline (MEF / Assembly Loading)

**Description**:  
A pipeline core that discovers data source plugins at runtime via the Managed Extensibility Framework (MEF) or custom assembly loading. Each data source is a separate assembly (DLL) that exports an `[Export(typeof(IWeatherPlugin))]` contract. The pipeline host scans a `/plugins` directory, loads assemblies, discovers implementations, and runs them. Configuration maps plugin names to their config sections.

**Project Structure**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Contracts/      # IWeatherPlugin, NormalizedRecord (shared assembly)
│   ├── SyncMetrics.Pipeline.Host/           # Plugin discovery, orchestration, output
│   └── plugins/
│       ├── SyncMetrics.Plugin.OpenMeteo/    # Open-Meteo plugin DLL
│       └── (future: SyncMetrics.Plugin.WeatherApi/)
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**How Extensibility Works**:  
New source = build a new DLL implementing `IWeatherPlugin`, drop it in `/plugins`. The host discovers it automatically at startup. Zero code changes, zero recompilation of existing code.

**Why This Approach Is Tempting**:
- Maximum extensibility — literally plug-and-play
- Shows deep .NET knowledge (MEF, `AssemblyLoadContext`, plugin isolation)
- Production-relevant pattern for ISV/SaaS products with customer-specific integrations

**Why This Approach Should NOT Be Chosen**:
- **Massive over-engineering**: This is a take-home exercise, not an ISV platform. Plugin architecture for 1-2 data sources is building a nuclear reactor to boil water
- **Complexity explosion**: Assembly loading, shared contracts versioning, plugin isolation, type resolution across `AssemblyLoadContext` — each of these is fraught with subtle bugs
- **MEF is legacy-adjacent**: While still supported, MEF is not the modern .NET approach. Most teams use DI-based registration instead of attribute-based export
- **Testing nightmare**: Plugin discovery testing requires building and loading separate assemblies in test projects
- **Interview disaster**: If asked "why plugins for two data sources?", the only honest answer is "I over-engineered it" — which directly contradicts the "staff judgment" evaluation criterion
- **`dotnet run` complexity**: Plugins need to be pre-built and placed in the right directory. The evaluator's experience goes from "clone and run" to "why isn't it finding the plugin DLL?"

**Database Fit**: Plugin contracts could include DB adapters. Irrelevant here.  
**Docker Fit**: Multi-stage Docker build that compiles plugins and copies to host. Over-complex.  
**CI/CD Fit**: Build matrix for each plugin. Way too much infrastructure.

**VERDICT: ABSOLUTELY NOT. This is the canonical "over-engineering" trap. Mention in the interview ONLY to say: "I considered a plugin model but recognized it violates YAGNI for the current scope. If SyncMetrics grows to 50+ data sources with customer-specific connectors, a plugin architecture would be justified — but not for a pipeline with 2-3 sources."**

---

### APPROACH 7: Minimal API Backend + React Vite Frontend Dashboard

**Description**:  
A two-repository (or monorepo) solution where the C# pipeline is exposed as an ASP.NET Core Minimal API backend, and a React + Vite + TypeScript frontend provides a dashboard to trigger pipeline runs, view weather data in tables/charts, manage locations, and display processing summaries. The backend serves both the pipeline logic and a RESTful API (`/api/pipeline/run`, `/api/weather/latest`, `/api/locations`). The frontend consumes this API. Communication via JSON over HTTP. Frontend builds to static files served by the backend or separately via Vite dev server.

**Project Structure**:
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
└── docker-compose.yml     # Backend + Frontend containers
```

**How Extensibility Works**:  
Same as Approach 1 for the pipeline. The API layer is a thin HTTP shell over the pipeline coordinator. Frontend is purely presentational — adding a new source only changes the backend.

**Why This Approach Is Tempting (Arthur's Perspective)**:
- **Visual wow factor**: Evaluators see a polished dashboard with charts, tables, real-time pipeline status — more impressive at first glance than a console app producing a `.tsv` file
- **Full-stack demonstration**: Shows Arthur is not just a backend engineer. React + TypeScript + Vite shows modern frontend fluency
- **Arthur's CV lists React**: This is a chance to prove it, not just claim it
- **Closer to production reality**: SyncMetrics presumably has some UI for their analytics platform. Showing frontend thinking could resonate
- **Interview differentiator**: If other candidates submit console apps and Arthur submits a full-stack dashboard, the visual impact is significant

**Why This Approach MUST NOT Be Chosen — The Staff Engineer Case Against Frontend**:

1. **The exercise never mentions UI, frontend, dashboard, or visualization**. The deliverables are explicit:
   - Normalized tab-delimited output
   - Processing summary on completion
   - Runnable with `dotnet run`
   - Passing tests with `dotnet test`
   
   A React frontend satisfies NONE of these. It's solving a problem that wasn't asked.

2. **"Staff judgment" evaluation criterion works AGAINST it**. The rubric says: *"What was generalized vs. kept simple."* Adding a React frontend to a data pipeline exercise demonstrates the opposite of staff judgment — it shows inability to scope work. A Staff Engineer who adds unrequested scope to an estimate or sprint is the engineer everyone dreads in planning meetings.

3. **It dilutes pipeline quality**. Every hour spent on React components, Vite config, CSS, state management, and API endpoint wiring is an hour NOT spent on:
   - Better error handling edge cases
   - More thorough test coverage
   - Cleaner pipeline architecture
   - Better retry logic
   - More thoughtful config-driven field mapping
   
   The evaluators will spend 80% of their review time on the C# pipeline. The React code is noise.

4. **It creates evaluation problems**. Now the evaluator needs Node.js installed (or Docker Compose for two containers). The README grows. The "how to run" section gets complex. They asked for `dotnet run` — if the frontend requires `npm install && npm run dev` in addition, you've already failed the portability test.

5. **It shifts the narrative from "pipeline engineer" to "full-stack generalist."** This is a **Staff Engineer — Data Integrations** role. The exercise is testing integration patterns, HTTP abstraction, JSON parsing, schema normalization. Submitting a React dashboard says: *"I'm not confident my backend work speaks for itself, so I added visual flair."* That's a junior signal, not a staff signal.

6. **React Vite is completely standard in 2026** — there's no technical depth to demonstrate. `npm create vite@latest`, add Tailwind, fetch from API, render in table. Every bootcamp grad can do this. The evaluators won't be impressed. What WILL impress them: a beautifully decomposed C# pipeline with thoughtful error handling and 90%+ test coverage.

**Frontend-Specific Analysis — If You Still Want to Argue For It**:

| Aspect | Assessment |
|--------|-----------|
| **React + Vite + TS** | Standard stack, no differentiator in 2026 |
| **State management** | For 3 locations × 7 days, even `useState` is overkill. No complex state needed |
| **Charting** | Recharts or Chart.js for weather visualization. Nice but not evaluated |
| **Testing** | Frontend tests (Vitest, React Testing Library) are NOT in the exercise requirements |
| **Build time** | Adds 4-8 hours of development for zero evaluated deliverables |
| **CORS** | Backend needs CORS configuration for dev. Another thing that can break for the evaluator |
| **Docker Compose** | Now you need two containers, port configuration, network setup |

**Database Fit**: If you add a frontend, you now "need" a database to persist pipeline runs and serve historical data. This cascades into: PostgreSQL container, EF Core migrations, seed data, connection strings. Scope explosion.  
**Docker Fit**: docker-compose.yml with `backend` and `frontend` services. Port mapping (5000 for API, 5173 for Vite). Evaluator needs `docker compose up`. Much more friction.  
**CI/CD Fit**: Two build pipelines (dotnet + node). Doubled CI complexity.

**VERDICT: ABSOLUTELY NOT. This is the single most important "staff judgment" call in this exercise. The ability to NOT build something that isn't asked for is a defining staff engineer trait. In the interview, if asked about full-stack: *"I deliberately kept this as a console pipeline because the spec is about data integration patterns, not visualization. I would scope a React dashboard as a separate workstream in a production roadmap — mixing it into the pipeline exercise would obscure the architectural decisions the team wanted to evaluate. That said, the IOutputWriter abstraction means adding an API layer is straightforward if the product needs it."***

---

### APPROACH 8: Functional Pipeline with Railway-Oriented Programming (Result<T> Chain)

**Description**:  
A .NET 8 Console App that models the entire pipeline as a chain of `Result<T, Error>` transformations, inspired by F#'s Railway-Oriented Programming (ROP) and Scott Wlaschin's functional domain modeling. Every pipeline stage is a function that takes a `Result<TInput>` and returns a `Result<TOutput>`. Success flows along the "happy track"; any failure shunts to the "error track" and propagates through to the end without exceptions. No `try/catch` in business logic. Errors are VALUES, not exceptions.

**Core Pattern**:
```csharp
// Every stage signature follows this pattern:
Result<RawJson>      FetchAsync(LocationConfig location);
Result<SourceModel>  Parse(RawJson json);
Result<NormalizedWeatherRecord[]> Transform(SourceModel model, LocationConfig location);
Result<string>       WriteOutput(NormalizedWeatherRecord[] records);

// Composed as:
var result = await FetchAsync(location)
    .Bind(json => Parse(json))
    .Bind(model => Transform(model, location))
    .Bind(records => WriteOutput(records));
```

**Project Structure**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   ├── SyncMetrics.Pipeline.Core/
│   │   ├── Result.cs                  # Result<T>, Result<T,E> monadic type
│   │   ├── ResultExtensions.cs        # Bind, Map, Match, Tap extension methods
│   │   ├── PipelineError.cs           # Discriminated union of error types
│   │   ├── Models/
│   │   └── Interfaces/
│   ├── SyncMetrics.Pipeline.Application/
│   │   ├── Pipeline.cs                # Functional composition of stages
│   │   └── Stages/                    # Each stage as a pure function
│   ├── SyncMetrics.Pipeline.Infrastructure/
│   │   ├── Http/OpenMeteoClient.cs
│   │   ├── Parsers/OpenMeteoParser.cs
│   │   └── Output/TabDelimitedWriter.cs
│   └── SyncMetrics.Pipeline.Console/
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
└── SyncMetrics.sln
```

**How Extensibility Works**:  
New API source = new fetch and parse functions. The pipeline composition is the same: `Fetch → Bind(Parse) → Bind(Transform) → Bind(Write)`. Each source plugs its functions into the same chain shape.

**Why This Approach Is Tempting**:
- **Elegant error handling**: No `try/catch` spaghetti. Errors are first-class values. Every function explicitly declares it can fail via `Result<T>`. The pipeline's error handling is visible in the TYPE SIGNATURES, not hidden in catch blocks
- **Composability**: Pipeline stages snap together like LEGO via `Bind`. Adding a stage (e.g., validation, enrichment) is adding one `.Bind(Validate)` call
- **Perfect testability**: Pure functions → predictable inputs/outputs → trivial to test. No mocking needed for the pipeline composition itself
- **Shows depth**: Monadic composition, discriminated unions for errors, Railway-Oriented Programming — these signal a developer who reads beyond MSDN docs, who understands functional programming principles and applies them in C#
- **Error aggregation**: Can collect errors across all locations using `Result<T>[]` → aggregate, not fail-fast
- **No external dependencies**: `Result<T>` is ~50 lines of code. No LanguageExt or CSharpFunctionalExtensions NuGet needed

**Why This Approach Should NOT Be the Primary Architecture**:
- **Unfamiliar to most .NET teams**: `Result<T>` and monadic `Bind` are not standard patterns in the .NET ecosystem. A new teammate reading the code needs to understand functional composition, which has a learning curve. The README audience (exercise evaluation) may find it unusual
- **C# is not F#**: `Result<T>` in C# works but is never as clean as F# discriminated unions + computation expressions. The `.Bind().Bind().Bind()` chain can look awkward compared to F#'s `result { }` computation expression. It risks looking like "trying to write F# in C#"
- **Exception interop friction**: .NET libraries throw exceptions (HttpClient, System.Text.Json). You need to wrap every external call in `Result.Try(() => ...)`, which adds boilerplate at the boundaries
- **Debugging experience**: When a pipeline fails deep in a Bind chain, the stack trace is less clear than a traditional try/catch with structured logging. Developers used to breakpoint-debugging struggle with functional chains
- **The exercise says "No silent failures"**: ROP's strength is explicit error propagation, but the evaluators might expect to see familiar patterns (structured exceptions, logging) rather than monadic error types
- **Risk of seeming academic**: If the evaluator is a pragmatic .NET developer, `Result<T, PipelineError>` might seem like an academic exercise rather than practical engineering

**However — Key Insight**: Even if we don't use full ROP as the PRIMARY architecture, the `Result<T>` pattern for parser return types is EXCELLENT. The parser should NOT throw exceptions. It should return `Result<SourceModel, ParseError>` so the coordinator can collect errors gracefully. **We should ADOPT this micro-pattern inside the chosen architecture.**

**Database Fit**: Functional pipelining is data-shape agnostic. No DB needed.  
**Docker Fit**: Same as any console app.  
**CI/CD Fit**: Same as any console app.

**VERDICT: DO NOT USE as the primary architecture, BUT ADOPT `Result<T>` for parser and transformer return types within the chosen architecture. This gives us the best of ROP (explicit error handling, no silent failures) without the full commitment to functional composition that might alienate evaluators. In the interview: *"I used Result types for the parsing and transformation stages so errors are values, not exceptions. The pipeline coordinator aggregates Result objects and builds the processing summary from both successes and failures. This eliminates silent failures by design — if a stage can fail, the return type forces you to handle it."***

---

### APPROACH 9: ASP.NET Core Minimal API with Pipeline-as-a-Service (No Frontend)

**Description**:  
Instead of a console app, expose the pipeline as an ASP.NET Core Minimal API with endpoints to trigger pipeline runs, check status, and download output files. The pipeline itself runs as a background task (via `IHostedService` or `BackgroundService`). The API provides: `POST /api/pipeline/run` → triggers a run, `GET /api/pipeline/status/{runId}` → check progress, `GET /api/pipeline/output/{runId}` → download the `.tsv` file. No frontend — just API endpoints with Swagger/OpenAPI documentation.

**Project Structure**:
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
│   └── SyncMetrics.Pipeline.Api.Tests/   # WebApplicationFactory integration tests
└── SyncMetrics.sln
```

**How Extensibility Works**:  
Same internal pipeline architecture. The API is just an HTTP shell. Adding a new source is purely an internal pipeline concern.

**Why This Approach Is Tempting**:
- **Production-realistic**: In reality, SyncMetrics would expose their pipeline via an API, not a console app. This shows Arthur thinks about how software is actually consumed
- **Swagger documentation**: Evaluator opens `http://localhost:5000/swagger` and can interact with the pipeline via browser — nice developer experience
- **Background task patterns**: `BackgroundService` for pipeline execution shows understanding of ASP.NET Core hosting model
- **Integration testing**: `WebApplicationFactory<Program>` allows true integration tests through HTTP without deploying
- **Familiar to .NET evaluators**: Most .NET Senior/Staff engineers live in ASP.NET Core daily

**Why This Approach Should NOT Be Chosen**:
- **The exercise says `dotnet run` → pipeline executes**: Not `dotnet run` → server starts → wait for HTTP request. The exercise expects a run-once pipeline, not a long-running service. If the evaluator types `dotnet run` and sees "Now listening on http://localhost:5000" instead of weather data flowing, they'll wonder if Arthur read the requirements
- **Adds unnecessary complexity**: The pipeline is the deliverable. An API layer is infrastructure around the deliverable. Every line of endpoint routing, Swagger config, and `BackgroundService` coordination is a line NOT making the pipeline better
- **Testing scope explosion**: Now you need API integration tests (`WebApplicationFactory`), HTTP endpoint tests, background service lifecycle tests — on top of the pipeline tests the exercise requires
- **State management problem**: `POST /run` → where does the run state live? In memory? Then a restart loses everything. In a database? Then you need entity models, a DB, migrations. The exercise doesn't need any state management
- **Evaluator experience friction**: Instead of `dotnet run` → see output, it's `dotnet run` → server starts → open Swagger → hit POST → wait → hit GET for output. More steps = more friction = lower evaluation score

**Database Fit**: If API, you need run state. SQLite at minimum. Scope creep begins.  
**Docker Fit**: `EXPOSE 5000`. Docker run with port mapping. Standard but more complex than console.  
**CI/CD Fit**: More complex — need to test API endpoints + pipeline. Two test categories.

**VERDICT: DO NOT USE. The exercise is explicitly a pipeline that runs and produces output. Wrapping it in an API adds zero evaluated value and risks appearing like scope misunderstanding. In the interview: *"I kept this as a console pipeline because the exercise defines it as a run-once data pipeline. If SyncMetrics needs it as a service, I'd wrap the pipeline coordinator in an ASP.NET Core Minimal API with BackgroundService — the pipeline's interfaces make this trivial because the API layer would only call PipelineCoordinator.RunAsync(). I'd estimate that migration at 2-3 hours."***

---

### APPROACH 10: Modular Monolith with Feature Folders and Shared Kernel

**Description**:  
A single .NET 8 project organized into feature folders with a strict dependency convention. Instead of multiple projects per Clean Architecture layer, everything lives in ONE project but is organized into self-contained feature modules. A `SharedKernel/` folder holds models, interfaces, and the `Result<T>` type. Each data source is a feature folder (`Features/OpenMeteo/`, future `Features/WeatherApi/`). The pipeline coordinator lives in `Features/Pipeline/`. The output writer lives in `Features/Output/`. Internal folder boundaries enforce the same separation that multi-project Clean Architecture provides, but with zero project-reference overhead.

**Project Structure**:
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

**How Extensibility Works**:  
Adding a second source = add a new feature folder `Features/WeatherApi/` with client, parser, transformer, data source. Register `WeatherApiDataSource : IWeatherDataSource` in `ServiceRegistration.cs`. The pipeline coordinator runs all registered `IWeatherDataSource` instances. Same interface contracts as Approach 1. Zero changes to existing features.

**Why This Approach Is Genuinely Strong**:
- **Right-sized for the exercise**: One source project + one test project. No 4-project Clean Architecture overhead for a take-home. The evaluator sees a tight, focused solution that respects the exercise scope
- **Same interfaces, same extensibility**: The interface boundaries (IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter, IWeatherDataSource) are IDENTICAL to the multi-project approach. Extensibility is not compromised — it's enforced by interfaces, not project references
- **Feature folder cohesion**: Everything about Open-Meteo lives in `Features/OpenMeteo/`. Everything about output lives in `Features/Output/`. A developer adding WeatherApi creates one folder — doesn't navigate four projects
- **Simpler `dotnet run` experience**: One project means `dotnet run` just works. No `--project` flag needed. The evaluator's first experience is frictionless
- **Simpler `dotnet test` experience**: One test project. All tests discovered and run together. No test project dependency wiring
- **Build time**: One project compiles in seconds. Four projects add restore + build overhead that matters for iteration speed during development
- **Matches exercise complexity**: The exercise has ONE data source with 5 fields and 3 locations. That's ~50 lines of real business logic. Four projects for 50 lines of logic is ceremony. One well-organized project is proportional
- **Tests mirror source**: `Tests/OpenMeteo/` mirrors `Features/OpenMeteo/`. Easy to find tests for any feature. Test project structure guided by source structure
- **Staff judgment signal**: Choosing a modular monolith over multi-project Clean Architecture for a take-home shows the engineer understands that project structure should match problem complexity, not architectural dogma
- **Still upgradeable**: If the exercise grows (unlikely), feature folders can be extracted into separate projects later. The interfaces guarantee this works. Starting multi-project and realizing it's too much is harder to walk back than starting monolith and extracting
- **Clean Architecture is a DEPENDENCY RULE, not a folder count**: The original Clean Architecture book by Robert C. Martin defines it as a dependency rule (outer layers depend on inner layers, never reverse). This is enforced by the `SharedKernel/Interfaces/` pattern — features depend on shared interfaces, not on each other. You can have Clean Architecture in one project

**Why This Might Concern Some Evaluators**:
- **"Is this too simple?"**: An evaluator expecting 4+ projects might initially think the solution is under-engineered. However, the interface contracts, test coverage, and extensibility story counter this immediately
- **No compile-time dependency enforcement**: In a multi-project setup, project references enforce that Infrastructure can't reference Console. In a single project, a developer COULD import from the wrong folder. However, for a 1-2 developer codebase with code review, this is a non-issue. ArchUnit or naming conventions can enforce this for larger teams
- **Perception vs. substance**: Some evaluators equate "more projects" with "better architecture." The interview must preempt this: *"The architecture is defined by the interface contracts, not the project count. These same interfaces would scale to multiple projects if the team size or source count justified it."*

**Database Fit**: No DB. File output. Same rationale as all other approaches.  
**Docker Fit**: Simplest possible. One project, one Dockerfile stage, one image. Minimal.  
**CI/CD Fit**: `dotnet restore → build → test`. One project makes CI the fastest of all approaches.

**VERDICT: *** STRONGEST CONTENDER ***. This approach delivers the EXACT SAME interface contracts and extensibility as the 4-project Clean Architecture approach (Approach 1), but with dramatically less ceremony, faster evaluator onboarding, and a cleaner "staff judgment" signal. It shows Arthur knows that architecture is about interface boundaries and dependency rules — not about how many `.csproj` files you have.**

---

## 3.1 React Vite Frontend — Cross-Approach Assessment

Before moving to the final verdict, here is the comprehensive frontend assessment that applies across ALL approaches:

### The Definitive Frontend Analysis

**The exercise requirement, verbatim**: *"Build an extensible ingestion pipeline in C# that fetches, transforms, and outputs normalized weather data."*

**What a React Vite frontend would add**:
- A visual dashboard to display weather data
- UI to manage locations
- A button to trigger pipeline runs
- Charts for temperature trends

**What a React Vite frontend would NOT add**:
- Better pipeline architecture (the backend is identical)
- Better error handling (frontend is not evaluated on this)
- Better testability (frontend tests are not required)
- Better integration patterns (frontend doesn't integrate with weather APIs)
- Better extensibility (adding a new source is 100% backend)

### Forensic Analysis Against Each Evaluation Criterion

| Evaluation Criterion | Does Frontend Help? | Impact |
|---------------------|-------------------|---------| 
| **Architecture — Pipeline decomposition** | No. Pipeline is backend-only | Zero or negative (adds noise) |
| **Architecture — Interface boundaries** | No. APIs between frontend/backend are not what they mean by "interface boundaries" — they mean `IWeatherApiClient`, `IResponseParser` | Zero |
| **Architecture — Extensibility** | No. Adding a new weather source is backend-only | Zero |
| **Integration patterns — HTTP abstraction** | Partially — but the abstraction they want is in C#, not TypeScript `fetch()` | Misleading signal |
| **Integration patterns — JSON parsing** | No. Frontend receives already-normalized JSON from the backend API | Zero |
| **Integration patterns — Schema normalization** | No. Normalization happens in the C# pipeline | Zero |
| **Error handling — Resilience** | No. Error handling in React is `try/catch` on fetch + error boundaries. Not what they're testing | Zero |
| **Testability — Mocked HTTP** | No. They want mocked `HttpClient` in C#, not mocked `fetch` in JavaScript | Zero |
| **AI usage** | Marginally — could show AI used across full stack. But dilutes the C# narrative | Marginal |
| **Staff judgment** | NEGATIVE. Adding unrequested scope is the opposite of staff judgment | **Actively harmful** |

**Score: 0 positive criteria, 1 actively harmful criterion. Net impact: NEGATIVE.**

### When WOULD a React Frontend Be the Right Call?

- If the exercise said "build a weather dashboard" — this is a dashboard exercise
- If the exercise said "build a full-stack solution" — this is full-stack exercise
- If the exercise evaluated "frontend skills" — this is a frontend exercise  
- If the role title included "Full Stack" — but it says "Staff Engineer — Data Integrations"

None of these apply. The exercise is deliberately scoped to backend data integration.

### The One Exception — A 30-Second Interview Mention

In the interview, you CAN say: *"The pipeline writes tab-delimited files, but if SyncMetrics wanted a dashboard, the IOutputWriter interface could be swapped for an API sink. I'd build a React + Vite frontend consuming a Minimal API, but I scoped this exercise to what was asked — pipeline quality over scope breadth."*

This shows you THOUGHT about frontend, CHOSE not to build it, and can ARTICULATE why. That's the staff engineer move.

**FINAL FRONTEND VERDICT: DO NOT BUILD A REACT FRONTEND. This is the hill to die on. The exercise evaluates C# pipeline engineering. A frontend would actively harm your "staff judgment" score — the most revealing criterion on the rubric.**

---

## 4. The Verdict — Selected Architecture (Updated: 10-Approach Comparison)

### Approach Comparison Matrix

| # | Approach | Extensibility | Evaluator DX | Staff Judgment Signal | Testability | Complexity vs Value | Verdict |
|---|---------|--------------|-------------|---------------------|-------------|--------------------|---------| 
| 1 | Clean Architecture (4 projects) | ★★★★★ | ★★★☆☆ | ★★★★☆ | ★★★★★ | Over-structured for scope | Runner-up |
| 2 | Azure Functions + Durable | ★★★★☆ | ★☆☆☆☆ | ★★☆☆☆ | ★★★☆☆ | Massive overkill | Rejected |
| 3 | MediatR / CQRS | ★★★☆☆ | ★★★☆☆ | ★★☆☆☆ | ★★★☆☆ | Semantic mismatch | Rejected |
| 4 | Channels Producer/Consumer | ★★★☆☆ | ★★★☆☆ | ★★☆☆☆ | ★★☆☆☆ | Overkill for 3 calls | Rejected |
| 5 | Vertical Slice | ★★★★★ | ★★★☆☆ | ★★★★☆ | ★★★★★ | Good but mismatches "separate concerns" | Influential |
| 6 | MEF Plugins | ★★★★★ | ★☆☆☆☆ | ★☆☆☆☆ | ★★☆☆☆ | Nuclear overkill | Rejected |
| 7 | Minimal API + React Frontend | ★★★★★ | ★★☆☆☆ | ★☆☆☆☆ | ★★★☆☆ | Unrequested scope, harmful | **Rejected** |
| 8 | Functional/ROP Pipeline | ★★★★☆ | ★★★★☆ | ★★★☆☆ | ★★★★★ | Elegant but alienates readers | Adopt micro-pattern |
| 9 | Minimal API (no frontend) | ★★★★★ | ★★☆☆☆ | ★★☆☆☆ | ★★★★☆ | Misreads "dotnet run" intent | Rejected |
| 10 | Modular Monolith Feature Folders | ★★★★★ | ★★★★★ | ★★★★★ | ★★★★★ | Perfect proportion | **WINNER** |

### Winner: APPROACH 10 — Modular Monolith with Feature Folders and Shared Kernel

**Absorbing the Best of All Rejected Approaches**:

| Adopted From | What We Take | How It Appears |
|-------------|-------------|---------------|
| Approach 1 (Clean Arch) | Interface contracts: IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter, IWeatherDataSource | `SharedKernel/Interfaces/` |
| Approach 1 (Clean Arch) | Clean Architecture dependency rule: features depend on shared kernel, not on each other | Folder convention + DI |
| Approach 5 (Vertical Slice) | Source-specific cohesion: all Open-Meteo code in one folder | `Features/OpenMeteo/` |
| Approach 8 (ROP) | `Result<T>` for parser and transformer returns — errors as values, no silent failures | `SharedKernel/Result.cs` used by parsers |
| Approach 1 (Clean Arch) | Strategy pattern for data sources | `IWeatherDataSource` with DI registration |
| Approach 4 (Channels) | Concurrent fetching (simplified) | `Task.WhenAll` in PipelineCoordinator |

**Why Approach 10 Wins Over Approach 1 (the Previous Winner)**:

The previous analysis selected a hybrid of Approaches 1 + 5 (multi-project Clean Architecture). After adding four more approaches and re-evaluating with fresh eyes, Approach 10 wins because:

1. **Evaluator's first 30 seconds matter**: They clone/unzip, open in IDE, see ONE `.csproj` file, type `dotnet run`, see weather data flowing. With Approach 1, they see 4 projects, need `--project` flag or must figure out which is the entry point. First impressions matter
2. **Same interfaces, zero ceremony tax**: Every I-interface from Approach 1 exists in Approach 10. The extensibility story is identical. But there's no inter-project reference wiring, no multi-project restore, no `.sln` complexity
3. **Test-to-source mapping is clearer**: `Tests/OpenMeteo/` → `Features/OpenMeteo/`. One mental hop. In multi-project, you need to map `Pipeline.UnitTests/Parsing/` → `Pipeline.Infrastructure/Http/OpenMeteo/`. More indirection
4. **Build and test speed**: One project compiles and tests faster than four. For a take-home where rapid iteration matters during development, this is practical
5. **The "Clean Architecture = many projects" misconception**: Clean Architecture is about the dependency rule, not project count. By keeping interfaces in `SharedKernel/` and features depending only on that kernel, Approach 10 IS Clean Architecture with a single deployment unit. If an evaluator challenges this, Arthur can say: *"Robert Martin's Clean Architecture defines the dependency rule — outer layers depend inward. My SharedKernel with interfaces IS the inner circle. Features are the outer circle. The rule is enforced by DI and convention, not by project boundaries. Project boundaries are a team-scaling concern, not an architecture concern."*

**The Architecture in One Paragraph (Updated Interview Version)**:

> "I built a modular monolith organized by feature folders with a shared kernel enforcing Clean Architecture's dependency rule. The SharedKernel defines interface contracts — IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter — and a Result<T> type for explicit error handling. Each data source lives in its own feature folder implementing these contracts. The PipelineCoordinator orchestrates all registered IWeatherDataSource implementations, fetches locations concurrently with Task.WhenAll, and aggregates results into a tab-delimited output file. Parsers and transformers return Result<T>, so errors are values — not exceptions — and every failure surfaces in the processing summary. Adding a new source means one new feature folder and one DI registration. I chose a single-project structure over multi-project Clean Architecture because the exercise scope doesn't justify inter-project overhead — but the interface contracts are identical and would scale to multiple projects if the team or source count justified it."

### Final Project Structure (Approach 10 — Modular Monolith)

```
SyncMetrics.WeatherPipeline/
├── src/
│   └── SyncMetrics.Pipeline/
│       ├── SharedKernel/
│       │   ├── Models/
│       │   │   ├── NormalizedWeatherRecord.cs       # The unified output model
│       │   │   ├── LocationConfig.cs                # Configurable location (name, lat, lon)
│       │   │   ├── ProcessingResult.cs              # Success/failure result per location
│       │   │   └── PipelineSummary.cs               # Summary of full pipeline run
│       │   ├── Interfaces/
│       │   │   ├── IWeatherApiClient.cs             # HTTP abstraction per source
│       │   │   ├── IResponseParser.cs               # JSON → source-specific model
│       │   │   ├── IDataTransformer.cs              # Source model → NormalizedWeatherRecord
│       │   │   ├── IOutputWriter.cs                 # NormalizedWeatherRecord → tab-delimited file
│       │   │   └── IWeatherDataSource.cs            # Composite: fetch + parse + transform for one source
│       │   ├── Result.cs                            # Result<T> monadic type (from Approach 8 insight)
│       │   └── PipelineError.cs                     # Typed error discriminations
│       │
│       ├── Features/
│       │   ├── OpenMeteo/                           # Vertical slice for Open-Meteo source
│       │   │   ├── OpenMeteoApiClient.cs            # IWeatherApiClient implementation
│       │   │   ├── OpenMeteoApiResponse.cs          # Raw API response deserialization model
│       │   │   ├── OpenMeteoResponseParser.cs       # IResponseParser → returns Result<T>
│       │   │   ├── OpenMeteoTransformer.cs          # IDataTransformer → returns Result<T>
│       │   │   └── OpenMeteoDataSource.cs           # IWeatherDataSource composite
│       │   │
│       │   ├── Pipeline/                            # Pipeline orchestration feature
│       │   │   ├── PipelineCoordinator.cs           # Runs all sources, concurrency, builds summary
│       │   │   └── PipelineOptions.cs               # Strongly-typed pipeline config
│       │   │
│       │   └── Output/                              # Output writing feature
│       │       └── TabDelimitedFileWriter.cs        # IOutputWriter implementation
│       │
│       ├── Configuration/
│       │   ├── FieldMappingConfig.cs                # Config-driven field mapping (bonus)
│       │   └── ServiceRegistration.cs               # DI extension method — all services registered here
│       │
│       ├── Program.cs                               # Entry point — DI setup, config binding, run
│       ├── appsettings.json                         # Locations, output path, field mappings
│       └── SyncMetrics.Pipeline.csproj
│
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
│       ├── OpenMeteo/
│       │   ├── OpenMeteoResponseParserTests.cs      # Valid and malformed JSON parsing
│       │   ├── OpenMeteoTransformerTests.cs         # Transformation logic tests
│       │   └── TestData/                            # JSON fixture files
│       │       ├── valid_newyork_response.json
│       │       ├── valid_london_response.json
│       │       ├── malformed_missing_daily.json
│       │       ├── malformed_null_temperatures.json
│       │       ├── malformed_mismatched_arrays.json
│       │       └── error_response.json
│       ├── Pipeline/
│       │   └── PipelineCoordinatorTests.cs          # End-to-end with mocked HTTP
│       ├── Output/
│       │   └── TabDelimitedWriterTests.cs           # Output formatting tests
│       ├── Http/
│       │   └── RetryPolicyTests.cs                  # Retry logic tests
│       └── SyncMetrics.Pipeline.Tests.csproj
│
├── output/
│   └── sample/                                      # Pre-generated sample output for evaluator
│       └── weather_data_sample.tsv
│
├── README.md
├── AI.md
├── Dockerfile
├── .github/
│   └── workflows/
│       └── build-and-test.yml
└── SyncMetrics.WeatherPipeline.sln
```

### Interface Flow Diagram (Approach 10)

```
  ┌─────────────────────────────────────────────────────────────────┐
  │                     PipelineCoordinator                         │
  │  (Features/Pipeline/ — orchestrates, concurrent fetch, summary) │
  └──────────┬──────────────────┬──────────────────┬────────────────┘
             │                  │                  │
      ┌──────▼──────┐   ┌──────▼──────┐   ┌──────▼──────┐
      │ IWeather     │   │ IWeather     │   │ IWeather     │
      │ DataSource   │   │ DataSource   │   │ DataSource   │
      │ Features/    │   │ Features/    │   │ Features/    │
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
                  (Features/Output/)
                        │
                        ▼
                  Tab-delimited .tsv

  ┌─────────────────────────────────────┐
  │          SharedKernel/              │
  │  Interfaces, Models, Result<T>,    │
  │  PipelineError — ALL features      │
  │  depend inward on this kernel      │
  └─────────────────────────────────────┘
```

### Pipeline Execution Flow (Updated for Result<T>)

```
1. Program.cs loads config (appsettings.json → locations, field mappings, source settings)
2. ServiceRegistration.cs registers all services in DI container
3. PipelineCoordinator.RunAsync() called from Program.cs
4. For each registered IWeatherDataSource (discovered via DI):
   a. Fetch all locations concurrently (Task.WhenAll)
      - IWeatherApiClient.FetchAsync(location) → Result<string> (raw JSON or error)
      - Retry with exponential backoff + jitter on transient failures (5xx, timeout)
   b. For each Result:
      - If Success: IResponseParser.Parse(json) → Result<SourceModel>
        - If Success: IDataTransformer.Transform(model, location) → Result<NormalizedWeatherRecord[]>
          - Collect normalized records into results list
        - If Failure: Record ParseError with location context, continue
      - If Failure: Record FetchError with location + HTTP status, continue
5. Aggregate all Result objects:
   - Successful records → IOutputWriter.WriteAsync() → tab-delimited .tsv file
   - Failed results → Collected into PipelineSummary.Errors
6. Print PipelineSummary to console:
   - Sources processed
   - Total locations attempted / succeeded / failed
   - Total records written
   - Wall-clock duration
   - Detailed error list with location, stage, and error description
   - Output file path
```

---

## 5. Database Analysis — Why No Database Is the Right Answer

The exercise specification states: *"writes unified output files for downstream analytics import"* and requires *"Normalized tab-delimited output."* This is explicitly a file-output pipeline. Here's why each database option was considered and rejected:

| Database | Argument For | Argument Against | Verdict |
|----------|-------------|-----------------|---------|
| **No DB (file-only)** | Matches requirement exactly. Tab-delimited files. Zero setup friction for evaluator. | None. | **✓ CHOSEN** |
| **SQLite** | Zero install, embedded, `.db` file travels with repo | Adds dependency and complexity for zero benefit. The output format is specified as tab-delimited, not DB. | ✗ Unnecessary |
| **SQL Server 2022** (Arthur has locally) | Arthur's comfort zone. Could store raw responses + normalized records. | Evaluator won't have Arthur's SQL Server. Violates "runs on their environment." Even LocalDB requires SQL Server Express installation. | ✗ Portability killer |
| **PostgreSQL 18** (Arthur has locally) | JSONB for raw responses, strong data types | Same portability problem. Evaluator needs PostgreSQL installed. Docker Compose could solve but adds friction. | ✗ Portability killer |
| **Azure Cosmos DB** | JSON-native, shows Azure skills | Requires Azure subscription. Evaluator can't run it. | ✗ Non-starter |
| **Redis** | Could cache API responses for development/retry | In-memory, no persistence needed. Adds Redis dependency for caching 3 API responses. | ✗ Over-engineering |

**Interview Answer**: *"The spec explicitly requires tab-delimited file output for downstream analytics. I kept the output behind an `IOutputWriter` interface, so if we needed to add database persistence — say, writing to PostgreSQL for a dashboard or Cosmos DB for global access — it's a new implementation registered in DI. No pipeline code changes. But for this exercise, the right answer is files because that's what was asked for."*

**Bonus point**: The `IOutputWriter` abstraction means during the interview you can say "I could swap `TabDelimitedFileWriter` for `SqlServerOutputWriter` or `CosmosDbOutputWriter` with a single DI registration change." This demonstrates extensibility without over-building.

---

## 6. Docker Strategy

### Why Include Docker

The exercise says: *"Return a zip or repo link."* and *"Include a short README.md — setup, how to run."* The evaluators will clone/unzip and try to run it. Docker ensures it works identically on their machine regardless of their .NET SDK version.

### Approach: Dual-Path (Docker Optional, dotnet run Primary)

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

**README instructions**:
```
# Option 1: Direct (requires .NET 8 SDK)
dotnet run --project src/SyncMetrics.Pipeline

# Option 2: Docker (requires Docker only)
docker build -t syncmetrics-pipeline .
docker run --rm -v ${PWD}/output:/app/output syncmetrics-pipeline
```

**Why this Docker approach**:
- Multi-stage build: small runtime image (~80MB vs ~800MB SDK)
- Tests run INSIDE the build stage — if tests fail, image doesn't build
- Output volume mount lets the evaluator see the generated files on their host
- The evaluator doesn't need .NET SDK installed — just Docker
- Shows production-readiness mindset without over-complicating the exercise

**Docker Compose? No.** There's no database, no Redis, no message queue. A single `Dockerfile` is sufficient. Docker Compose would signal over-engineering.

---

## 7. CI/CD Pipeline Design

Even though the exercise doesn't explicitly require CI/CD, including a GitHub Actions workflow demonstrates production mindset and is a trivial addition that provides outsized signal.

### GitHub Actions Workflow

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

**Why this is valuable**:
- Takes 5 minutes to add, signals professional discipline
- Evaluator sees green badge on the repo → instant credibility
- Proves `dotnet test` passes in a clean environment (not just on Arthur's machine)
- Shows familiarity with CI/CD (listed on Arthur's CV as a strength)

**What NOT to add to CI/CD**:
- Docker image publishing (no registry needed)
- Deployment steps (no target environment)
- Code coverage thresholds (nice but unnecessary scope creep)
- SonarQube/security scanning (over-engineering signal)

**Interview Answer**: *"I included a simple CI pipeline to prove the solution builds and tests in a clean environment. In production, I'd extend this with Docker image publishing, integration test stages against a staging API, and deployment to Azure Container Apps or Kubernetes."*

---

## 8. Testing Strategy

### Test Categories (from exercise requirements)

| Category | What to Test | Test Pattern |
|----------|-------------|-------------|
| **Response Parsing (valid)** | Parse correct Open-Meteo JSON → source model | Unit test with JSON fixture files |
| **Response Parsing (malformed)** | Missing fields, null arrays, extra fields, wrong types | Unit test with malformed JSON fixtures |
| **Transformation Logic** | Source model → NormalizedWeatherRecord mapping, unit correctness | Unit test with in-memory source models |
| **Output Formatting** | NormalizedWeatherRecord[] → correct tab-delimited string | Unit test, verify header + rows |
| **End-to-End Pipeline** | Full pipeline with mocked HTTP → correct output file | Integration test with mock HttpMessageHandler |
| **Retry Logic (Bonus)** | Transient failures trigger retry, permanent failures don't | Unit test with mock handler returning 500 then 200 |

### Testing Framework Choice

- **xUnit** — Industry standard for .NET, used by Microsoft's own repos
- **NSubstitute** or **Moq** — Interface mocking. NSubstitute has cleaner syntax, but Moq is more widely known. Use **NSubstitute** for cleaner test readability
- **FluentAssertions** — Optional but makes assertions declarative (`.Should().BeEquivalentTo(...)`)
- **No Testcontainers** — No database, no external services to containerize

### Mock HTTP Strategy

The exercise says: *"HTTP calls are behind an interface."* This means:

1. `IWeatherApiClient` is the interface. In tests, create a `FakeWeatherApiClient` or mock `IWeatherApiClient` to return canned JSON
2. For testing retry logic, use a custom `HttpMessageHandler` that counts invocations and returns failures/successes in sequence
3. Load test JSON from embedded resource files or `TestData/` directory — keeps tests clean and fixtures versioned

### Test Data Fixtures

Create realistic JSON files based on actual Open-Meteo responses:
- `valid_newyork_response.json` — Full valid response
- `valid_london_response.json` — Full valid response
- `malformed_missing_daily.json` — JSON valid but `daily` key missing
- `malformed_null_temperatures.json` — `temperature_2m_max` array has null values
- `malformed_mismatched_arrays.json` — `time` array length ≠ `temperature_2m_max` length
- `malformed_empty_daily.json` — `daily.time` is empty array
- `error_response.json` — `{ "error": true, "reason": "..." }`

---

## 9. Output Schema Design

The exercise says: *"Normalized tab-delimited output — design the schema yourself."*

### Proposed Tab-Delimited Schema

```
Source	Location	Latitude	Longitude	Date	TempMaxC	TempMinC	PrecipitationMm	WindSpeedMaxKmh	UVIndexMax	FetchedAtUtc
OpenMeteo	New York	40.7128	-74.0060	2026-03-28	18.2	8.4	0.0	22.1	5.2	2026-03-28T14:30:00Z
OpenMeteo	New York	40.7128	-74.0060	2026-03-29	15.1	6.2	2.3	18.5	3.8	2026-03-28T14:30:00Z
OpenMeteo	London	51.5074	-0.1278	2026-03-28	12.5	5.1	4.2	30.2	2.1	2026-03-28T14:30:00Z
```

### Schema Design Decisions (Interview Talking Points)

| Column | Why |
|--------|-----|
| `Source` | Critical for multi-source extensibility — when a second source is added, records are distinguishable |
| `Location` | Human-readable name from config, not just coordinates |
| `Latitude/Longitude` | Preserves the exact coordinates used, enables geo-analysis downstream |
| `Date` | ISO 8601 format — universal, sortable, timezone-unambiguous |
| `TempMaxC/TempMinC` | Explicit unit in column name (C for Celsius) — tab-delimited has no metadata layer |
| `PrecipitationMm` | Unit in column name |
| `WindSpeedMaxKmh` | Unit in column name |
| `UVIndexMax` | Dimensionless index, no unit suffix needed |
| `FetchedAtUtc` | Audit trail — when was this data retrieved. Essential for debugging stale data issues |

**Why tab-delimited (.tsv) and not CSV?**
- The exercise specifies tab-delimited
- TSV is actually better for analytics import: no quoting issues with commas in location names
- Downstream tools (Excel, Power BI, pandas) all handle TSV natively

---

## 10. Retry Logic Design (Bonus)

### Strategy: Exponential Backoff with Jitter via Polly

```
Attempt 1: Immediate
Attempt 2: Wait ~1s (1s + random 0-500ms jitter)
Attempt 3: Wait ~2s (2s + random 0-500ms jitter)
Attempt 4: Wait ~4s (4s + random 0-500ms jitter)
Max retries: 3 (4 total attempts)
```

**What triggers retry**:
- HTTP 5xx (server error — transient)
- HTTP 408 (request timeout — transient)
- HTTP 429 (rate limited — transient, but also respect `Retry-After` header)
- `HttpRequestException` (network-level failure)
- `TaskCanceledException` (timeout)

**What does NOT trigger retry**:
- HTTP 4xx (except 408, 429) — client error, retrying won't help
- HTTP 400 — bad request, our URL is wrong
- JSON parsing errors — the response was received, it's just malformed

**Implementation**: Use `Microsoft.Extensions.Http.Polly` (or the new `Microsoft.Extensions.Http.Resilience` in .NET 8) for resilience policies on `IHttpClientFactory`-managed clients.

**Interview Answer**: *"I used Polly's exponential backoff with jitter for transient HTTP failures. Jitter prevents the thundering herd problem — if all 3 locations fail simultaneously and retry at the same fixed intervals, they'll all hit the API at the same time again. Jitter randomizes the retry timing. I also differentiate transient vs. permanent failures — a 500 gets retried, a 400 doesn't."*

---

## 11. Config-Driven Field Mapping Design (Bonus)

### appsettings.json Structure

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

**Why this matters**: When a second API source is added, its completely different field names are mapped via config, not code. The transformer reads the mappings and applies them generically. This is a powerful extensibility signal.

---

## 12. Key Technology Choices Summary

| Choice | Selected | Why |
|--------|----------|-----|
| **Runtime** | .NET 8 (LTS) | Latest LTS, performance, native AOT support if needed |
| **Project structure** | Single project, feature folders + SharedKernel | Right-sized for exercise. Same interfaces as multi-project |
| **DI** | Microsoft.Extensions.DependencyInjection | Built-in, no external deps |
| **Config** | Microsoft.Extensions.Configuration + Options pattern | Standard .NET. Strongly-typed config |
| **HTTP** | IHttpClientFactory + HttpClient | Proper connection pooling. Handler pipeline for retry |
| **Resilience** | Microsoft.Extensions.Http.Resilience (or Polly v8) | .NET 8 native resilience. Exponential backoff + jitter |
| **JSON** | System.Text.Json | Built-in, high-performance, no Newtonsoft dependency |
| **Error handling** | Custom Result<T> (~50 LOC) | Errors as values. No silent failures by design. No external dep |
| **Testing** | xUnit + NSubstitute + FluentAssertions | Industry standard, clean syntax |
| **Output** | StreamWriter with tab-separated formatting | Simple, correct, no library needed |
| **Logging** | Microsoft.Extensions.Logging + Console provider | Built-in, structured logging |
| **CI** | GitHub Actions | Free, standard, evaluator-visible |
| **Container** | Docker (optional path) | Reproducible. Multi-stage build |
| **Frontend** | None | Not requested, actively harmful to staff judgment score |

---

## 13. Interview Preparation — Anticipated Questions & Answers

### Q: "Why did you choose this architecture over something simpler/more complex?"

> "I evaluated ten approaches ranging from Azure Durable Functions to a plugin-based system with MEF assembly loading, to a React+Vite full-stack dashboard, to Railway-Oriented functional composition. The exercise evaluates staff judgment — knowing what to generalize and what to keep simple. I chose a modular monolith with feature folders and a shared kernel because it delivers the exact same interface contracts and extensibility as a 4-project Clean Architecture solution, but without the inter-project ceremony that would be disproportionate for the exercise scope. Clean Architecture is a dependency rule, not a project count — my SharedKernel with interfaces enforces that features depend inward, never on each other."

### Q: "How would you add a second data source?"

> "Create a new feature folder — say, `Features/WeatherApi/` — with its own client, parser, transformer, and data source class, all implementing the SharedKernel interfaces. Register the new IWeatherDataSource in ServiceRegistration.cs. Add a config section in appsettings.json with the source's URL, locations, and field mappings. Zero changes to existing code — the pipeline coordinator discovers all registered sources via DI."

### Q: "Why no database?"

> "The spec says 'writes unified output files for downstream analytics import' — that's tab-delimited files. I built IOutputWriter as an interface, so adding a PostgreSQL or Cosmos DB writer is a single new class registered in DI. But for this exercise, files are the right answer. Over-building what wasn't asked for signals poor judgment, not thoroughness."

### Q: "Why didn't you build a frontend / dashboard?"

> "The exercise specifies a C# data pipeline with tab-delimited output, runnable via `dotnet run`. Every evaluation criterion — architecture, integration patterns, error handling, testability, staff judgment — focuses on the pipeline. A React frontend would add zero value to any evaluated dimension and would risk signaling scope misunderstanding. I deliberately scoped my work to maximize pipeline quality over breadth. That said, the IOutputWriter abstraction means wrapping the pipeline in a Minimal API with a React dashboard is a straightforward future step."

### Q: "Walk me through how you handle a malformed API response."

> "Parsers and transformers return Result<T> — errors are values, not exceptions. If a JSON response is valid but missing the 'daily' key, the parser returns a Failure result with a ParseError containing the field name and location. If temperature arrays have null values, the transformer returns a Failure with the specific index and field. The pipeline coordinator collects all Result objects — successes go to the output writer, failures go to the processing summary. No silent failures — every error is visible and typed."

### Q: "How did you use AI in this exercise?"

> "AI was a core part of my workflow, not a supplement. I used Claude/Copilot to scaffold the initial project structure, generate the HTTP client boilerplate, and create the JSON test fixtures from the API documentation. I overrode AI output in the transformer layer — the initial generated code used decimal for coordinates but the API returns float, and the AI-generated retry policy didn't distinguish between transient and permanent HTTP errors. I deliberately wrote the architecture decisions and the Result<T> type myself because those require human judgment about error modeling and extensibility trade-offs."

### Q: "What would you do differently in a production environment?"

> "Three things: (1) I'd add structured logging with correlation IDs per pipeline run, shipping to Application Insights or Seq. (2) For 100+ locations, I'd replace Task.WhenAll with System.Threading.Channels for backpressure-controlled streaming, or Azure Durable Functions for serverless fan-out/fan-in. (3) I'd add a circuit breaker per API source via Polly — if Open-Meteo is consistently failing, stop hammering it and fail fast while other sources continue."

### Q: "Why a modular monolith instead of microservices?"

> "This is a single pipeline with a single responsibility — fetch, normalize, and output weather data. Microservices make sense when you need independent deployment, different scaling profiles, or team ownership boundaries. This pipeline is one deployment unit, one team, one scaling concern. A modular monolith gives us the same interface boundaries as microservices but with zero network overhead, zero distributed tracing complexity, and zero deployment orchestration. If SyncMetrics had 10 teams each owning a different data source, I'd extract feature folders into independent services. But that's a team-scaling decision, not a technical one."

### Q: "Explain your Result<T> pattern. Why not just try/catch?"

> "Try/catch has a fundamental readability problem — you can't see from a method signature whether it can fail. A method returning `SourceModel` might throw, or might not. You only know by reading the implementation or the docs. Result<T> makes failure explicit in the type system. If a parser returns `Result<SourceModel>`, the caller is FORCED by the compiler to handle both success and failure. The pipeline coordinator doesn't need try/catch — it pattern-matches on Result. This eliminates the 'forgot to catch' category of bugs entirely. I kept the implementation to ~50 lines with no external dependency."

### Q: "Why not use a library like LanguageExt or CSharpFunctionalExtensions for Result<T>?"

> "For a take-home exercise, I want minimal dependencies. LanguageExt is a 900KB package with hundreds of types most .NET developers have never seen. CSharpFunctionalExtensions is lighter but still an external dependency for something I can implement in 50 lines. Using my own Result<T> also lets me tailor it to the pipeline's specific error model — PipelineError with FetchError, ParseError, TransformError variants — rather than using a generic Error type."

---

## 14. Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Open-Meteo API is down when evaluator tests | Include cached sample output in `output/sample/` directory. README notes this. Tests use mocked HTTP, not live API. |
| Evaluator doesn't have .NET 8 SDK | Docker path documented. Dockerfile runs tests + builds. |
| `wind_speed_10m_max` vs `windspeed_10m_max` naming | Use the actual API field name. Add a comment noting the discrepancy. |
| Tab-delimited output encoding issues on Windows vs. Mac | Use UTF-8 without BOM. Document in README. |
| Evaluator runs on Mac/Linux | Solution is cross-platform .NET 8. No Windows-specific paths. Use `Path.Combine` everywhere. |

---

## 15. Implementation Roadmap (Next Steps)

When we're ready to code, follow this order:

1. **Solution & project scaffolding** — Create .sln, single src project, test project, folder structure
2. **SharedKernel** — Result<T>, PipelineError, all interfaces, all models
3. **Configuration** — appsettings.json, strongly-typed PipelineOptions, FieldMappingConfig, ServiceRegistration.cs
4. **Features/OpenMeteo/ApiClient** — IWeatherApiClient implementation with IHttpClientFactory
5. **Features/OpenMeteo/Parser** — JSON deserialization with System.Text.Json, returns Result<T>
6. **Features/OpenMeteo/Transformer** — Source model → NormalizedWeatherRecord, returns Result<T>
7. **Features/OpenMeteo/DataSource** — IWeatherDataSource composite wiring client + parser + transformer
8. **Features/Output/TabDelimitedFileWriter** — IOutputWriter implementation
9. **Features/Pipeline/PipelineCoordinator** — Orchestrate all sources, Task.WhenAll concurrency, build summary
10. **Program.cs** — DI setup via ServiceRegistration, config binding, entry point
11. **Tests** — Parsing (valid + malformed), transformation, output, end-to-end with mocked HTTP
12. **Retry logic** — Polly v8 / Microsoft.Extensions.Http.Resilience on HttpClient
13. **Config-driven field mapping** — Dynamic field mapping from appsettings.json
14. **Docker** — Dockerfile, validate `docker build` and `docker run`
15. **CI/CD** — GitHub Actions workflow, validate green build
16. **Sample output** — Run pipeline, save sample .tsv to output/sample/
17. **README.md** — Setup, how to run (dotnet + Docker), assumptions, trade-offs
18. **AI.md** — AI usage documentation (10-15 lines per spec)

---

## 16. Detailed Implementation Plan — Phase-by-Phase Build Guide

> **Purpose of this section**: This is the granular, step-by-step implementation plan that translates Approach 10 (Modular Monolith with Feature Folders + SharedKernel + Result<T>) from architectural blueprint into buildable code. Every phase includes WHAT we build, WHY we build it, HOW it maps to the evaluation criteria, and WHAT to say when defending it in the interview. Read Section 15 for the high-level roadmap; read THIS section for the implementation details.

### 16.0 Why We Are Creating This — The Strategic Rationale

**The Business Context**:  
SyncMetrics Inc. (via Actabl's hiring pipeline) aggregates weather data from multiple third-party APIs, normalizes it, and writes unified output files for downstream analytics. This is a real-world data integration problem — the kind of work that a Staff Engineer on the Data Integrations team does daily.

**Why This Exercise Exists**:  
Actabl is not testing whether Arthur can call an HTTP API or parse JSON — any mid-level developer can do that. They are testing whether Arthur can:
1. **Decompose a pipeline** into well-defined stages with clean interface boundaries
2. **Design for extensibility** without over-engineering (the "add a second source" requirement)
3. **Handle failure gracefully** — no silent failures, structured error reporting, resilience
4. **Write meaningful tests** that cover real edge cases, not just the happy path
5. **Exercise staff-level judgment** — knowing what to build, what NOT to build, and articulating trade-offs

**Why Approach 10 Specifically**:  
We evaluated 10 approaches exhaustively (Section 3). Approach 10 wins because:
- It delivers **identical interface contracts and extensibility** to a 4-project Clean Architecture solution
- It has **zero unnecessary ceremony** — one `dotnet run`, one `dotnet test`, instant evaluator onboarding
- It shows **staff judgment**: architecture is about dependency rules and interface boundaries, not project count
- It absorbs the best micro-patterns from rejected approaches: `Result<T>` from Approach 8 (ROP), feature-folder cohesion from Approach 5 (Vertical Slice), `Task.WhenAll` concurrency from Approach 4 (Channels)
- It avoids every over-engineering trap: no Azure Functions, no MediatR, no MEF plugins, no React frontend, no database, no web server

**What Success Looks Like When We're Done**:
- `dotnet run` → pipeline executes, fetches 3 locations concurrently, writes a `.tsv` file, prints a processing summary
- `dotnet test` → 25+ tests pass covering parsing (valid + malformed), transformation, output formatting, and end-to-end with mocked HTTP
- `docker build && docker run` → same result, no .NET SDK required
- GitHub Actions badge is green
- README.md is short and useful
- AI.md documents real AI usage with one override and one deliberate non-use
- Every file in the codebase has a clear purpose and maps to an evaluation criterion

**The One-Paragraph Architecture Statement (memorize this for the interview)**:
> "I built a modular monolith organized by feature folders with a shared kernel enforcing Clean Architecture's dependency rule. The SharedKernel defines interface contracts — IWeatherApiClient, IResponseParser, IDataTransformer, IOutputWriter — and a Result<T> type for explicit error handling. Each data source lives in its own feature folder implementing these contracts. The PipelineCoordinator orchestrates all registered IWeatherDataSource implementations, fetches locations concurrently with Task.WhenAll, and aggregates results into a tab-delimited output file. Parsers and transformers return Result<T>, so errors are values — not exceptions — and every failure surfaces in the processing summary. Adding a new source means one new feature folder and one DI registration. I chose a single-project structure over multi-project Clean Architecture because the exercise scope doesn't justify inter-project overhead — but the interface contracts are identical and would scale to multiple projects if the team or source count justified it."

---

### 16.1 Phase 1: Solution Scaffolding

**What we create**: The complete file system structure — solution file, two projects (src + tests), all folders, `.gitignore`, empty placeholder structure.

**Why we create it first**: The folder structure IS the architecture made visible. When the evaluator opens the repo in their IDE, the first thing they see is the folder tree. A well-organized tree communicates more about the architecture than 1000 lines of code. Feature folders, SharedKernel, Configuration — the names tell the story.

**Complete directory tree**:
```
SyncMetrics.WeatherPipeline/
├── src/
│   └── SyncMetrics.Pipeline/
│       ├── SyncMetrics.Pipeline.csproj          # .NET 8 console application
│       ├── Program.cs                           # Entry point — DI setup, config, run
│       ├── appsettings.json                     # All configuration (locations, mappings, sources)
│       ├── SharedKernel/                        # Inner circle — depends on NOTHING
│       │   ├── Models/                          # Domain models shared across all features
│       │   │   ├── NormalizedWeatherRecord.cs   # The unified output row (one per day per location)
│       │   │   ├── LocationConfig.cs            # A configured location (name, lat, lon)
│       │   │   ├── ProcessingResult.cs          # Per-source execution result (records + errors + timing)
│       │   │   └── PipelineSummary.cs           # Full pipeline run summary (for console output)
│       │   ├── Interfaces/                      # The 5 interface contracts — pipeline stages
│       │   │   ├── IWeatherApiClient.cs         # HTTP abstraction: fetch raw JSON per location
│       │   │   ├── IResponseParser.cs           # JSON → source-specific model
│       │   │   ├── IDataTransformer.cs          # Source model → normalized records
│       │   │   ├── IOutputWriter.cs             # Normalized records → file output
│       │   │   └── IWeatherDataSource.cs        # Composite: one source's full fetch→parse→transform
│       │   ├── Result.cs                        # Result<T> monadic error type (~50 lines)
│       │   └── PipelineError.cs                 # Typed error hierarchy (Fetch/Parse/Transform/Output)
│       │
│       ├── Features/                            # Outer circle — depends on SharedKernel only
│       │   ├── OpenMeteo/                       # Everything for Open-Meteo lives HERE
│       │   │   ├── OpenMeteoApiClient.cs        # IWeatherApiClient → HTTP GET with query string
│       │   │   ├── OpenMeteoApiResponse.cs      # Deserialization model (parallel arrays)
│       │   │   ├── OpenMeteoResponseParser.cs   # IResponseParser → JSON validation + deserialization
│       │   │   ├── OpenMeteoTransformer.cs      # IDataTransformer → zip arrays → normalized records
│       │   │   └── OpenMeteoDataSource.cs       # IWeatherDataSource → wires client+parser+transformer
│       │   │
│       │   ├── Pipeline/                        # Orchestration feature
│       │   │   ├── PipelineCoordinator.cs       # Runs all sources, concurrent fetch, builds summary
│       │   │   └── PipelineOptions.cs           # Strongly-typed config binding for Pipeline section
│       │   │
│       │   └── Output/                          # Output writing feature
│       │       └── TabDelimitedFileWriter.cs    # IOutputWriter → writes .tsv with header + rows
│       │
│       └── Configuration/                       # DI registration + config binding
│           ├── FieldMappingConfig.cs            # Config-driven field mapping model (bonus feature)
│           └── ServiceRegistration.cs           # Single extension method: AddPipelineServices()
│
├── tests/
│   └── SyncMetrics.Pipeline.Tests/
│       ├── SyncMetrics.Pipeline.Tests.csproj    # xUnit + NSubstitute + FluentAssertions
│       ├── OpenMeteo/                           # Tests mirror source features
│       │   ├── OpenMeteoResponseParserTests.cs  # Valid + malformed JSON parsing (8+ tests)
│       │   ├── OpenMeteoTransformerTests.cs     # Transformation logic tests (6+ tests)
│       │   └── TestData/                        # JSON fixture files (versioned, realistic)
│       │       ├── valid_response.json
│       │       ├── valid_london_response.json
│       │       ├── malformed_missing_daily.json
│       │       ├── malformed_null_values.json
│       │       ├── malformed_mismatched_arrays.json
│       │       ├── malformed_empty_arrays.json
│       │       └── error_response.json
│       ├── Pipeline/
│       │   └── PipelineCoordinatorTests.cs      # End-to-end with mocked sources (5+ tests)
│       ├── Output/
│       │   └── TabDelimitedWriterTests.cs       # Output formatting tests (5+ tests)
│       └── Http/
│           └── RetryPolicyTests.cs              # Retry logic tests (3+ tests)
│
├── output/                                      # Generated at runtime — gitignored except sample
│   └── sample/
│       └── weather_data_sample.tsv              # Pre-generated sample for evaluator reference
│
├── README.md                                    # Setup, how to run, architecture, trade-offs
├── AI.md                                        # AI usage documentation (10-15 lines per spec)
├── Dockerfile                                   # Multi-stage: build → test → publish → runtime
├── .gitignore                                   # Standard .NET + output/ exclusions
├── .github/
│   └── workflows/
│       └── build-and-test.yml                   # CI: restore → build → test on ubuntu-latest
└── SyncMetrics.WeatherPipeline.sln              # Solution file binding src + tests
```

**NuGet packages — src project (`SyncMetrics.Pipeline.csproj`)**:

| Package | Version | Purpose | Why this specific package |
|---------|---------|---------|--------------------------|
| `Microsoft.Extensions.Hosting` | 8.0.x | DI container, configuration binding, logging, app lifecycle | Standard .NET host bootstrap. One package gives us `IServiceCollection`, `IConfiguration`, `ILogger<T>`, and `IOptions<T>`. Replaces manual wiring of 4+ separate packages |
| `Microsoft.Extensions.Http` | 8.0.x | `IHttpClientFactory` for managed `HttpClient` instances | Proper connection pooling, DNS rotation, named clients. Without this, `HttpClient` leaks sockets on long-running processes |
| `Microsoft.Extensions.Http.Resilience` | 8.x | Retry policies with exponential backoff + jitter on `HttpClient` | .NET 8 native resilience stack. Replaces raw Polly v8 wiring. Integrates directly with `IHttpClientFactory`. One line of config adds retry + circuit breaker + timeout |

**NuGet packages — test project (`SyncMetrics.Pipeline.Tests.csproj`)**:

| Package | Version | Purpose | Why this specific package |
|---------|---------|---------|--------------------------|
| `Microsoft.NET.Test.Sdk` | 17.x | Test host infrastructure | Required for `dotnet test` to discover and run tests |
| `xunit` | 2.x | Test framework | Industry standard for .NET. Used by Microsoft's own repos (ASP.NET Core, EF Core). Convention-based, no test class inheritance required |
| `xunit.runner.visualstudio` | 2.x | VS Test adapter | Enables test discovery in Visual Studio and `dotnet test` CLI |
| `NSubstitute` | 5.x | Interface mocking | Cleaner syntax than Moq (`Substitute.For<IFoo>()` vs `new Mock<IFoo>().Object`). No `.Setup().Returns()` ceremony. Arthur's preference for readability |
| `FluentAssertions` | 7.x | Assertion library | `result.Should().BeEquivalentTo(expected)` is self-documenting. Better failure messages than `Assert.Equal`. Makes tests read like specifications |

**Why zero additional packages**: No MediatR (semantic mismatch for a linear pipeline), no AutoMapper (5 field mappings don't justify a mapping library), no LanguageExt/CSharpFunctionalExtensions (50-line custom `Result<T>` avoids a 900KB dependency), no Serilog (built-in `Microsoft.Extensions.Logging` is sufficient for console output), no FluentValidation (validation is simple enough for manual checks in parsers).

**Interview defense**: "Every NuGet package in the solution solves a specific, justified problem. I can explain why each one is there and why alternatives were rejected. Minimal dependency surface means faster builds, fewer security audit surfaces, and no transitive dependency surprises. A Staff Engineer should be able to justify every dependency they introduce."

---

### 16.2 Phase 2: SharedKernel — The Inner Circle

**What we create**: The core types that every feature depends on. This folder is the "Domain" layer of Clean Architecture, scaled to the right size. Nothing in SharedKernel references anything in Features/ or Configuration/. The dependency arrow always points INWARD.

**Why it matters**: The SharedKernel is what makes the architecture extensible. A new data source (Features/WeatherApi/) only needs to implement the interfaces defined here. It never needs to know about Features/OpenMeteo/. This is the Open/Closed Principle enforced at the folder level.

#### 16.2.1 `Result<T>` — Errors as Values (~50 lines of code)

**What it is**: A monadic type that represents either a successful value (`T`) or a failure (`PipelineError`). Inspired by F#'s `Result` type and Scott Wlaschin's Railway-Oriented Programming, but implemented as idiomatic C# that any .NET developer can read.

**API surface**:
```csharp
public class Result<T>
{
    // Factory methods
    public static Result<T> Success(T value);
    public static Result<T> Failure(PipelineError error);
    
    // State inspection
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public T Value { get; }              // throws if Failure
    public PipelineError Error { get; }  // throws if Success
    
    // Monadic operations
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func);  // chain fallible ops
    public Result<TNext> Map<TNext>(Func<T, TNext> func);           // transform success value
    public T GetValueOrDefault(T defaultValue);                      // safe extraction
}
```

**Why we build our own instead of using a library**:
- LanguageExt is 900KB with hundreds of types (Option, Either, Seq, Lst, etc.) — massive overkill
- CSharpFunctionalExtensions is lighter but still an external dependency for 50 lines of code
- Our `Result<T>` is tailored to `PipelineError` — the error type is domain-specific, not generic
- No dependency = no version conflicts, no security advisories, no transitive packages
- The evaluator can read the entire implementation in 30 seconds

**Why Result<T> instead of try/catch**:
- **Signature honesty**: A method returning `Result<SourceModel>` declares "I can fail" in the type. A method returning `SourceModel` that throws is lying about its contract
- **Forced handling**: The caller MUST check `IsSuccess`/`IsFailure`. With exceptions, a missing catch block is a silent bug discovered at runtime
- **Aggregation**: The pipeline coordinator collects `Result<T>[]` from all locations. With exceptions, you'd need `try { } catch { errors.Add(...); }` around every call — Result makes this a clean LINQ operation
- **No control flow abuse**: Exceptions are for exceptional circumstances. A missing field in a JSON response is EXPECTED in data integration — it's not exceptional, it's a known failure mode

**Interview defense**: "I used Result types for the parsing and transformation stages so errors are values, not exceptions. The pipeline coordinator aggregates Result objects and builds the processing summary from both successes and failures. This eliminates silent failures by design — if a stage can fail, the return type forces you to handle it. I kept the implementation to ~50 lines with no external dependency because that's all a data pipeline needs."

#### 16.2.2 `PipelineError` — Typed Error Hierarchy

**What it is**: A hierarchy of record types representing every category of failure the pipeline can encounter. Uses C# records for immutability, value equality, and concise syntax.

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

**Why typed errors instead of string messages**:
- `FetchError` carries `StatusCode` — the processing summary can report "HTTP 503 Service Unavailable for Tokyo" not just "fetch failed"
- `ParseError` carries `Field` — report "missing field 'daily.temperature_2m_max' for London" not just "parse failed"
- `TransformError` carries `RecordIndex` — report "null temperature at index 3 for New York" not just "transform failed"
- Pattern matching: `error switch { FetchError fe => ..., ParseError pe => ..., _ => ... }` — each error type gets contextual handling
- Testing: `error.Should().BeOfType<ParseError>().Which.Field.Should().Be("temperature_2m_max")` — precise assertions

**Interview defense**: "Every error in the pipeline carries contextual information — not just a message, but the location name, the specific field that failed, the HTTP status code. This makes the processing summary actionable. If Tokyo fails because the API returned 503, the summary says exactly that. A developer or operator reading the output can diagnose the issue without reading logs."

#### 16.2.3 Models — The 4 Domain Models

**`NormalizedWeatherRecord`** — The unified output row:
```csharp
public record NormalizedWeatherRecord
{
    public required string Source { get; init; }          // "OpenMeteo", "WeatherApi", etc.
    public required string Location { get; init; }        // Human-readable: "New York"
    public required double Latitude { get; init; }        // Exact coordinates used
    public required double Longitude { get; init; }
    public required DateOnly Date { get; init; }          // ISO 8601 date
    public double? TempMaxCelsius { get; init; }          // Nullable — weather data can have gaps
    public double? TempMinCelsius { get; init; }
    public double? PrecipitationMm { get; init; }
    public double? WindSpeedMaxKmh { get; init; }
    public double? UvIndexMax { get; init; }
    public required DateTime FetchedAtUtc { get; init; }  // When this data was retrieved
}
```

**Why nullable doubles for weather fields**: Real weather data has gaps. Open-Meteo can return `null` for a field if the measurement is unavailable. Our model MUST handle this. If we used non-nullable `double`, we'd either crash or silently use `0.0` — which is a valid temperature (0°C), making the bug invisible. Nullable forces explicit handling everywhere.

**Why `DateOnly` not `DateTime`**: Weather forecasts are per-day, not per-moment. `DateOnly` is semantically correct and was introduced specifically for this use case. It also avoids timezone confusion — a date is a date, not a point in time.

**`LocationConfig`** — A configured location:
```csharp
public record LocationConfig
{
    public required string Name { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}
```

**`ProcessingResult`** — Per-source execution result:
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

**`PipelineSummary`** — Full run output:
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

#### 16.2.4 Interfaces — The 5 Pipeline Contracts

These are the most important 5 files in the entire solution. They define the pipeline's architecture. Everything else is implementation detail.

**`IWeatherApiClient`** — HTTP abstraction per source:
```csharp
public interface IWeatherApiClient
{
    string SourceName { get; }
    Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken cancellationToken);
}
```
**Why `Result<string>` not `Result<HttpResponseMessage>`**: The interface exposes the RAW JSON string, not the HTTP plumbing. A new source might not even use HTTP (could read from a file, a queue, etc.). The interface is about "get me the raw data for this location," not "make an HTTP call."

**`IResponseParser<TRaw>`** — JSON deserialization + validation:
```csharp
public interface IResponseParser<TRaw>
{
    Result<TRaw> Parse(string rawJson);
}
```
**Why generic `<TRaw>`**: Each source has its own response shape. Open-Meteo returns parallel arrays. WeatherAPI.com might return nested objects. The parser's output type is source-specific. Only the transformer knows how to turn `TRaw` into `NormalizedWeatherRecord`.

**`IDataTransformer<TRaw>`** — Source model → normalized records:
```csharp
public interface IDataTransformer<TRaw>
{
    Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(TRaw rawData, LocationConfig location);
}
```
**Why it takes `LocationConfig`**: The raw API response doesn't always include the location name. Open-Meteo returns snapped coordinates (e.g., 40.7103 instead of 40.7128) but not "New York." The transformer enriches each record with the original location context.

**`IOutputWriter`** — Writes the final output:
```csharp
public interface IOutputWriter
{
    Task<Result<string>> WriteAsync(IReadOnlyList<NormalizedWeatherRecord> records, CancellationToken cancellationToken);
}
```
**Why `Result<string>` return**: The string is the output file path on success. On failure (disk full, permission denied), it's an `OutputError`. The pipeline coordinator uses this to include the file path in the summary.

**`IWeatherDataSource`** — The source composite:
```csharp
public interface IWeatherDataSource
{
    string SourceName { get; }
    Task<ProcessingResult> ProcessAsync(IEnumerable<LocationConfig> locations, CancellationToken cancellationToken);
}
```
**Why this composite exists**: The PipelineCoordinator shouldn't know about parsers or transformers — it just runs sources. Each source wires its own client → parser → transformer internally. The coordinator only sees `IWeatherDataSource.ProcessAsync()`. This is the Strategy pattern — each source implements its own strategy for getting normalized data.

**Interview defense for all 5 interfaces**: "The exercise explicitly requires 'HTTP, parsing, transformation, and output are separate concerns.' My interfaces map directly: IWeatherApiClient = HTTP, IResponseParser = parsing, IDataTransformer = transformation, IOutputWriter = output. IWeatherDataSource is the composite that wires the first three together per source. Adding WeatherAPI.com means implementing these interfaces in a new feature folder — the pipeline coordinator discovers them via DI."

---

### 16.3 Phase 3: Configuration

**What we create**: `appsettings.json` with all configurable values, strongly-typed option classes bound via `IOptions<T>`, and the DI registration method.

**Why this phase comes before features**: Features read their config via injected options. If we build config first, every feature implementation has its configuration ready. No hardcoded values at any point.

#### 16.3.1 `appsettings.json` — Complete Configuration

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

**Config breakdown — what each field controls**:

| Field | Purpose | Why configurable |
|-------|---------|-----------------|
| `OutputDirectory` | Where `.tsv` files are written | Evaluator might want to change output location; Docker volume mount target |
| `OutputFilePattern` | File naming with `{timestamp}` token | Prevents overwriting previous runs; sortable by time |
| `Sources[].Name` | Human-readable source identifier | Appears in output `Source` column and processing summary |
| `Sources[].Enabled` | Toggle sources without removing config | Production pattern: disable a flaky source without code deploy |
| `Sources[].BaseUrl` | API base URL | Different environments (staging vs prod) may have different URLs |
| `Sources[].TimeoutSeconds` | Per-source HTTP timeout | Slow sources shouldn't block fast ones; tunable per API's SLA |
| `Sources[].RetryCount` | Max retry attempts | Different APIs may warrant different retry aggressiveness |
| `Sources[].Locations[]` | Location list per source | Different sources may have different location coverage |
| `Sources[].FieldMappings[]` | Source field → output column mapping | **BONUS FEATURE**: A new source with different field names (e.g., `temp_high` instead of `temperature_2m_max`) only needs a config change, not code |

#### 16.3.2 Strongly-Typed Option Classes

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

#### 16.3.3 `ServiceRegistration.cs` — The DI Wiring Point

```csharp
public static class ServiceRegistration
{
    public static IServiceCollection AddPipelineServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind config
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        
        // HTTP client with resilience
        services.AddHttpClient("OpenMeteo", ...)
            .AddStandardResilienceHandler();
        
        // Open-Meteo feature
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

**Why `ServiceRegistration.cs` is critical for the extensibility story**: When the evaluator asks "how would you add a second source?", the answer is: "Add 4 lines here — register the new source's client, parser, transformer, and data source. The PipelineCoordinator gets `IEnumerable<IWeatherDataSource>` from DI and runs them all. Zero changes anywhere else."

**Interview defense**: "All configuration lives in `appsettings.json` — zero hardcoded values. Locations, URLs, retry counts, field mappings are all configurable. The bonus config-driven field mapping means a new source with different field names only needs a config section — the transformer reads the mapping dynamically. And the ServiceRegistration class is the single place where a new source gets wired in — 4 lines of DI registration."

---

### 16.4 Phase 4: Features/OpenMeteo — The First Data Source

**What we create**: The complete Open-Meteo integration — 5 files implementing the SharedKernel interfaces. This is the vertical slice where all source-specific code lives.

**Why it's one folder**: Cohesion. A developer working on Open-Meteo integration only needs to look in `Features/OpenMeteo/`. They never navigate to a separate "Infrastructure" project for the HTTP client or a "Domain" project for the model. Everything related to Open-Meteo is colocated.

#### 16.4.1 `OpenMeteoApiResponse` — Deserialization Model

**What it represents**: The exact JSON shape the Open-Meteo API returns. This is NOT our domain model — it's a data transfer object that mirrors the API contract.

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

**Critical design decisions**:
- **All list properties are `List<double?>`**: The API can return `null` for individual measurements. Our deserialization MUST accept this
- **All collection properties are nullable (`List<double?>?`)**: The entire array might be missing if the API drops a field
- **Uses `[JsonPropertyName]` not `[JsonProperty]`**: System.Text.Json, not Newtonsoft. Zero unnecessary dependencies
- **Uses `wind_speed_10m_max` (with underscore)**: Not `windspeed_10m_max` from the exercise URL. Validated against actual API docs — **CRITICAL CATCH #1** (see Section 2.1)

**Interview defense**: "I validated the exercise endpoint against the live API docs and found a naming discrepancy in the wind speed field. The exercise URL uses `windspeed_10m_max` but the actual API parameter is `wind_speed_10m_max` with underscores. Also, the exercise lists `uv_index_max` in the fields table but doesn't include it in the sample URL — I added it. These are exactly the spec-vs-reality catches you expect from a staff engineer doing integration work."

#### 16.4.2 `OpenMeteoApiClient` — HTTP Implementation

**Responsibility**: Build the query URL, make the HTTP GET, return raw JSON.

**Key implementation details**:
- Gets `HttpClient` from `IHttpClientFactory` (named client "OpenMeteo")
- Builds query string: `?latitude={lat}&longitude={lon}&daily=temperature_2m_max,...&timezone=auto&forecast_days=7`
- Uses `CultureInfo.InvariantCulture` for coordinate formatting (prevents `40,7128` on European locales)
- Returns `Result<string>.Success(json)` on HTTP 200
- Returns `Result<string>.Failure(new FetchError(...))` on any HTTP error, capturing status code
- `CancellationToken` propagated through all async calls
- Retry policy is NOT in this class — it's configured at the `HttpClient` level via `Microsoft.Extensions.Http.Resilience` in `ServiceRegistration`. The client class stays clean

**Why retry is external to the client**: Separation of concerns. The retry policy is an infrastructure concern (how to deal with transient failures). The client class is a business concern (how to call this API). If we baked retry into the client, we'd need to test retry separately from the HTTP call. With the resilience handler on the HttpClient pipeline, retry wraps the transport transparently.

#### 16.4.3 `OpenMeteoResponseParser` — JSON Validation + Deserialization

**Responsibility**: Take raw JSON string → validate structure → return typed `OpenMeteoApiResponse` or `ParseError`.

**Validation checks (each returning `ParseError` on failure)**:
1. JSON is valid (catches `JsonException` from deserialization)
2. Response is not an API error (`{ "error": true, "reason": "..." }`)
3. `Daily` property is not null
4. `Daily.Time` array is not null and not empty
5. All measurement arrays (`TemperatureMax`, `TemperatureMin`, etc.) have the same length as `Time`
6. No measurement array is null (the array itself — individual values within CAN be null)

**Why validation here, not in the transformer**: The parser's job is to guarantee structural integrity. If the parser says `Result<OpenMeteoApiResponse>.Success(...)`, the transformer can safely iterate arrays without null checks on the arrays themselves. This is the "parse, don't validate" principle — once data passes the parser gate, it's guaranteed structurally sound.

**Interview defense**: "My parser validates the parallel array structure before the transformer ever sees it. If `time` has 7 entries but `temperature_2m_max` has 6, the parser catches that as a `ParseError` with the specific field name and array lengths. The transformer never gets structurally inconsistent data. This is a 'parse, don't validate' pattern — structural validation happens once at the boundary, not scattered across every consumer."

#### 16.4.4 `OpenMeteoTransformer` — Array Zipping + Normalization

**Responsibility**: Take the validated `OpenMeteoApiResponse` + `LocationConfig` → produce `NormalizedWeatherRecord[]`.

**Key implementation details**:
- Iterates by index across all parallel arrays: `for (int i = 0; i < daily.Time.Count; i++)`
- Creates one `NormalizedWeatherRecord` per day
- Parses date strings to `DateOnly` (ISO 8601)
- Maps nullable doubles directly — if `TemperatureMax[i]` is null, `TempMaxCelsius` is null in the output
- Sets `Source = "OpenMeteo"`, `Location = locationConfig.Name`
- Captures `FetchedAtUtc = DateTime.UtcNow` at transformation time
- If field mappings exist in config, uses them to validate expected fields are present

**The parallel array "zip" is the non-trivial parsing the evaluators want to see**:
```
API response:
  time:              [2026-03-28, 2026-03-29, 2026-03-30]
  temperature_2m_max:[18.2,       15.1,       12.8      ]
  temperature_2m_min:[8.4,        6.2,        4.1       ]
  precipitation_sum: [0.0,        2.3,        0.5       ]

Transformer output:
  Record[0]: Date=2026-03-28, TempMax=18.2, TempMin=8.4, Precip=0.0
  Record[1]: Date=2026-03-29, TempMax=15.1, TempMin=6.2, Precip=2.3
  Record[2]: Date=2026-03-30, TempMax=12.8, TempMin=4.1, Precip=0.5
```

#### 16.4.5 `OpenMeteoDataSource` — The Composite Orchestrator

**Responsibility**: Wire client + parser + transformer for Open-Meteo. Fetch all locations concurrently.

```csharp
public async Task<ProcessingResult> ProcessAsync(IEnumerable<LocationConfig> locations, CancellationToken ct)
{
    var stopwatch = Stopwatch.StartNew();
    var records = new List<NormalizedWeatherRecord>();
    var errors = new List<PipelineError>();
    var locationList = locations.ToList();
    
    // Concurrent fetch — Task.WhenAll for all locations
    var fetchTasks = locationList.Select(loc => ProcessLocationAsync(loc, ct));
    var results = await Task.WhenAll(fetchTasks);
    
    // Aggregate
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
    // Fetch → Parse → Transform chain using Result<T>.Bind
    var fetchResult = await _apiClient.FetchAsync(loc, ct);
    return fetchResult
        .Bind(json => _parser.Parse(json))
        .Bind(model => _transformer.Transform(model, loc));
}
```

**Why `Task.WhenAll`**: The spec says "locations fetched concurrently." `Task.WhenAll` fires all HTTP requests simultaneously and waits for all to complete. For 3 locations, this means ~1 HTTP call time instead of ~3 sequential calls. Simple, correct, and demonstrably concurrent.

**Why not `Parallel.ForEachAsync` or `Channel<T>`**: `Task.WhenAll` is the right abstraction for "run N async operations concurrently and collect all results." `Parallel.ForEachAsync` is for CPU-bound parallelism with degree control. `Channel<T>` is for streaming producer/consumer with backpressure. For 3 HTTP calls, `Task.WhenAll` is the proportionate tool.

**The Bind chain is the Result<T> pattern in action**: `Fetch → Bind(Parse) → Bind(Transform)`. If Fetch fails, Parse never runs. If Parse fails, Transform never runs. Each failure short-circuits with contextual error. No try/catch nesting.

---

### 16.5 Phase 5: Features/Output — Tab-Delimited Writer

**What we create**: `TabDelimitedFileWriter` implementing `IOutputWriter`.

**Output schema**:
```
Source	Location	Latitude	Longitude	Date	TempMaxC	TempMinC	PrecipitationMm	WindSpeedMaxKmh	UVIndexMax	FetchedAtUtc
OpenMeteo	New York	40.7128	-74.0060	2026-03-28	18.2	8.4	0.0	22.1	5.2	2026-03-28T14:30:00Z
OpenMeteo	London	51.5074	-0.1278	2026-03-28	12.5	5.1	4.2	30.2	2.1	2026-03-28T14:30:00Z
```

**Implementation details**:
- Creates output directory if it doesn't exist (`Directory.CreateDirectory`)
- Replaces `{timestamp}` token in filename with `DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")`
- Uses `StreamWriter` with `UTF-8 without BOM` encoding
- Header row: column names separated by `\t`
- Data rows: values separated by `\t`, null values written as empty string (not "null")
- Dates formatted as ISO 8601 (`yyyy-MM-dd`)
- Timestamps formatted as ISO 8601 with UTC (`yyyy-MM-ddTHH:mm:ssZ`)
- Coordinates formatted with 4 decimal places using `InvariantCulture`
- Returns `Result<string>.Success(filePath)` or `Result<string>.Failure(new OutputError(...))`

**Schema design decisions to defend in interview**:

| Column | Why it exists | What it enables |
|--------|--------------|----------------|
| `Source` | Multi-source extensibility | Filter/group by source in downstream analytics |
| `Location` | Human-readable context | "New York" is meaningful; "40.7128,-74.0060" is not |
| `Latitude/Longitude` | Exact coordinates | Geo-analysis, map plotting, coordinate verification |
| `Date` | The forecast date | Core dimension for time-series analysis |
| `TempMaxC/TempMinC` | Unit in column name | TSV has no metadata layer — units must be self-documenting |
| `PrecipitationMm` | Unit in column name | Same rationale |
| `WindSpeedMaxKmh` | Unit in column name | Same rationale |
| `UVIndexMax` | Dimensionless index | No unit suffix needed — UV index is a standard scale |
| `FetchedAtUtc` | Audit trail | Data freshness — when was this forecast retrieved? |

**Interview defense**: "I put units in column names because TSV has no metadata layer like Parquet or Avro. A column called 'TempMax' is ambiguous — Celsius or Fahrenheit? 'TempMaxC' is self-documenting. The `FetchedAtUtc` column is an audit trail — without it, you can't tell if the forecast was retrieved 5 minutes ago or 5 hours ago, which matters for downstream analytics accuracy."

---

### 16.6 Phase 6: Features/Pipeline — The Orchestrator

**What we create**: `PipelineCoordinator` — the central orchestrator that runs all data sources and produces the final output.

**Execution flow**:
```
PipelineCoordinator.RunAsync(CancellationToken):
  1. Start Stopwatch
  2. Resolve IEnumerable<IWeatherDataSource> from DI
  3. For each data source:
     a. Get its locations from PipelineOptions config
     b. Call source.ProcessAsync(locations, ct)
     c. Collect ProcessingResult
  4. Aggregate all successful NormalizedWeatherRecords from all sources
  5. If any records exist:
     a. Call IOutputWriter.WriteAsync(allRecords, ct)
     b. Capture output file path
  6. Build PipelineSummary from all ProcessingResults + output path + duration
  7. Print formatted summary to console via ILogger
  8. Return PipelineSummary
```

**The processing summary (printed to console)**:
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

**If errors exist, they appear below the summary**:
```
  ERRORS:
  ─────────────────────────────────────────────────────────
  [FetchError] Tokyo — HTTP 503 Service Unavailable
      URL: https://api.open-meteo.com/v1/forecast?latitude=35.6762...
  [ParseError] London — Mismatched array lengths
      Field: temperature_2m_max (expected 7, got 6)
  ─────────────────────────────────────────────────────────
```

**Why structured summary matters for evaluation**: The spec says "processing summary on completion." A summary that just says "Done. 21 records." is junior. A summary with source breakdown, success/failure counts, duration, output path, and detailed typed errors is staff-level. It shows Arthur thinks about operability — when this runs in production at 3am and the on-call engineer reads the log, they need actionable information.

---

### 16.7 Phase 7: Program.cs — Entry Point

**What we create**: The minimal entry point that wires DI and runs the pipeline.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncMetrics.Pipeline.Configuration;
using SyncMetrics.Pipeline.Features.Pipeline;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPipelineServices(builder.Configuration);

using var host = builder.Build();
var coordinator = host.Services.GetRequiredService<PipelineCoordinator>();
var summary = await coordinator.RunAsync(CancellationToken.None);

Environment.ExitCode = summary.HasErrors ? 1 : 0;
```

**Why this is ~10 lines, not 100**: The entry point's job is bootstrapping — create the container, run the pipeline, exit. All registration logic lives in `ServiceRegistration.cs`. All orchestration logic lives in `PipelineCoordinator`. Program.cs is intentionally trivial because it's the hardest thing to unit test (it's the composition root). Keep it thin.

**Why `Environment.ExitCode = 1` on errors**: The pipeline processes data for downstream systems. If locations fail, the downstream system should know the data is incomplete. A non-zero exit code signals CI/CD systems, cron schedulers, and orchestrators that something went wrong. This is production-mindset.

**Why `Host.CreateApplicationBuilder`**: .NET 8's minimal hosting model. One method gives us DI, configuration from `appsettings.json` + environment variables + command-line args, and `ILogger` configured for console output. We get enterprise-grade bootstrapping in one line.

---

### 16.8 Phase 8: Retry Logic (Bonus Feature)

**What we create**: Exponential backoff with jitter on the OpenMeteo HTTP client, configured via `Microsoft.Extensions.Http.Resilience`.

**Retry policy parameters**:

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Max retry attempts | 3 (4 total requests) | Enough to survive a brief API hiccup, not enough to delay the pipeline substantially |
| Base delay | 1 second | Start modest — the API might recover in a second |
| Backoff type | Exponential with jitter | 1s → 2s → 4s with random ±500ms. Exponential prevents hammering a struggling API. Jitter prevents thundering herd |
| Timeout per attempt | 30 seconds | From `SourceOptions.TimeoutSeconds` config |

**What triggers retry**:
| Trigger | HTTP Code | Why retry |
|---------|-----------|-----------|
| Server error | 5xx | Server-side transient failure. Likely recovers |
| Request timeout | 408 | Server overwhelmed. May recover with backoff |
| Rate limited | 429 | Respect rate limit, wait, try again |
| Network exception | — | `HttpRequestException`: DNS, connection refused. Transient |
| Timeout exception | — | `TaskCanceledException`: request didn't complete in time. Transient |

**What does NOT trigger retry**:
| Non-trigger | HTTP Code | Why NOT retry |
|-------------|-----------|---------------|
| Bad request | 400 | Our URL is malformed. Retrying sends the same bad request |
| Not found | 404 | Endpoint doesn't exist. Retrying won't create it |
| Other 4xx | 401, 403 | Auth/permission issue. Not transient |

**Where the policy lives**: In `ServiceRegistration.cs` on the named `HttpClient`:
```csharp
services.AddHttpClient("OpenMeteo", client => {
    client.BaseAddress = new Uri(sourceConfig.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(sourceConfig.TimeoutSeconds);
})
.AddStandardResilienceHandler(options => {
    options.Retry.MaxRetryAttempts = sourceConfig.RetryCount;
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
});
```

**Interview defense**: "Jitter prevents thundering herd — if all 3 locations fail and retry at identical intervals, they all hit the API simultaneously again. Jitter randomizes retry timing across requests. I also distinguish transient vs. permanent failures — a 500 deserves retry, a 400 doesn't. The retry policy is on the HttpClient pipeline via Microsoft.Extensions.Http.Resilience, not in application code — the API client class stays clean and testable."

---

### 16.9 Phase 9: Tests — Everything the Evaluators Will Scrutinize

**What we create**: 27+ tests in 5 test classes covering every requirement from the spec.

**Testing philosophy**: The exercise spec lists 4 explicit test categories plus retry. We implement ALL of them with meaningful edge cases, not just happy paths. Each test name describes the scenario: `Parse_ValidResponse_ReturnsAllDays`, `Parse_MissingDailyKey_ReturnsParseError`. Tests read like specifications.

#### 9A. `OpenMeteoResponseParserTests` — Parsing (valid + malformed)

| # | Test | Input | Expected | Evaluation criterion |
|---|------|-------|----------|---------------------|
| 1 | `Parse_ValidResponse_ReturnsAllSevenDays` | `valid_response.json` (7 days, all fields) | `Success` with 7 time entries, all arrays populated | Response parsing (valid) |
| 2 | `Parse_ValidResponse_PreservesAllFieldValues` | `valid_response.json` | Specific temp/precip/wind values match JSON | Response parsing (valid) |
| 3 | `Parse_MissingDailyKey_ReturnsParseError` | `malformed_missing_daily.json` (no `daily` property) | `Failure(ParseError)` with message about missing daily | Response parsing (malformed) |
| 4 | `Parse_NullTemperatureValues_ReturnsSuccessWithNulls` | `malformed_null_values.json` (nulls in arrays) | `Success` — nulls are valid weather data gaps | Response parsing (malformed) |
| 5 | `Parse_MismatchedArrayLengths_ReturnsParseError` | `malformed_mismatched_arrays.json` (time[7] vs temp[6]) | `Failure(ParseError)` with field name + lengths | Response parsing (malformed) |
| 6 | `Parse_EmptyTimeArray_ReturnsParseError` | `malformed_empty_arrays.json` (time[] empty) | `Failure(ParseError)` — no data to process | Response parsing (malformed) |
| 7 | `Parse_ApiErrorResponse_ReturnsParseError` | `error_response.json` (`{ "error": true, "reason": "..." }`) | `Failure(ParseError)` with API reason | Response parsing (malformed) |
| 8 | `Parse_InvalidJson_ReturnsParseError` | `"not valid json {{"` | `Failure(ParseError)` — deserialization failed | Response parsing (malformed) |

#### 9B. `OpenMeteoTransformerTests` — Transformation logic

| # | Test | Input | Expected | Evaluation criterion |
|---|------|-------|----------|---------------------|
| 9 | `Transform_ValidData_ProducesCorrectRecordCount` | 7-day model + NYC location | 7 `NormalizedWeatherRecord` | Transformation logic |
| 10 | `Transform_ValidData_MapsLocationCorrectly` | Model + "New York" config | All records have Source="OpenMeteo", Location="New York" | Transformation logic |
| 11 | `Transform_ValidData_MapsTemperaturesCorrectly` | Model with known values | TempMaxC and TempMinC match input | Transformation logic |
| 12 | `Transform_NullValues_PreservesNulls` | Model with null temp at index 3 | Record[3].TempMaxCelsius is null | Transformation logic |
| 13 | `Transform_ValidData_ParsesDatesCorrectly` | Model with ISO date strings | DateOnly values match | Transformation logic |
| 14 | `Transform_ValidData_SetsFetchedAtUtc` | Any valid model | All records have FetchedAtUtc near DateTime.UtcNow | Transformation logic |

#### 9C. `TabDelimitedWriterTests` — Output formatting

| # | Test | Input | Expected | Evaluation criterion |
|---|------|-------|----------|---------------------|
| 15 | `Write_ValidRecords_CreatesFileWithCorrectHeader` | List of records | First line is tab-separated column names | Output formatting |
| 16 | `Write_ValidRecords_FormatsDataRowsCorrectly` | Records with known values | Tab-separated values match expected format | Output formatting |
| 17 | `Write_NullValues_WritesEmptyString` | Record with null TempMax | Empty string between tabs, not "null" | Output formatting |
| 18 | `Write_EmptyRecordList_WritesHeaderOnly` | Empty list | File contains header row only | Output formatting |
| 19 | `Write_ValidRecords_UsesUtf8NoBom` | Any records | File encoding is UTF-8 without BOM | Output formatting |

#### 9D. `PipelineCoordinatorTests` — End-to-end with mocked HTTP

| # | Test | Setup | Expected | Evaluation criterion |
|---|------|-------|----------|---------------------|
| 20 | `Run_AllSourcesSucceed_WritesAllRecords` | Mock source returns 21 records | Writer receives 21 records, summary shows 0 errors | End-to-end (mocked) |
| 21 | `Run_OneLocationFails_ContinuesOthers` | Mock: NYC succeeds, London fails, Tokyo succeeds | 14 records written, 1 error in summary | End-to-end (mocked) + error handling |
| 22 | `Run_AllLocationsFail_WritesNoRecords` | Mock: all 3 fail | 0 records, 3 errors in summary, exit code 1 | Error handling |
| 23 | `Run_NoSources_ReturnsEmptySummary` | No IWeatherDataSource registered | Summary with 0 records, 0 errors | Edge case |
| 24 | `Run_SuccessfulRun_PrintsSummaryWithCounts` | Mock: all succeed | Summary has correct LocationsAttempted/Succeeded counts | Processing summary |

#### 9E. `RetryPolicyTests` — Retry logic (bonus)

| # | Test | Setup | Expected | Evaluation criterion |
|---|------|-------|----------|---------------------|
| 25 | `Retry_TransientThenSuccess_ReturnsSuccess` | Handler: 500, 500, 200 | Returns OK after 3 attempts | Retry logic |
| 26 | `Retry_PermanentFailure_DoesNotRetry` | Handler: 400 | 1 attempt only | Retry logic |
| 27 | `Retry_MaxAttemptsExceeded_ReturnsFinalError` | Handler: 500, 500, 500, 500 | Failure after max attempts | Retry logic |

**Test data fixtures** — JSON files in `tests/SyncMetrics.Pipeline.Tests/OpenMeteo/TestData/`:

| Fixture file | Content | Used by |
|-------------|---------|--------|
| `valid_response.json` | Full 7-day Open-Meteo response for NYC with realistic values | Tests 1, 2 |
| `valid_london_response.json` | Full 7-day response for London | E2E tests |
| `malformed_missing_daily.json` | Valid JSON, no `daily` property | Test 3 |
| `malformed_null_values.json` | Valid structure, nulls in temperature arrays | Test 4 |
| `malformed_mismatched_arrays.json` | `time[7]` but `temperature_2m_max[6]` | Test 5 |
| `malformed_empty_arrays.json` | `daily.time` is `[]` | Test 6 |
| `error_response.json` | `{ "error": true, "reason": "Invalid coordinates" }` | Test 7 |

**Interview defense**: "I covered all 4 test categories from the spec: response parsing with both valid and malformed inputs, transformation logic, output formatting, and end-to-end with mocked HTTP. The malformed input tests are where the real quality shows — I test missing keys, null values, mismatched array lengths, empty arrays, API error responses, and invalid JSON. Each test uses fixture files from a TestData directory, so test data is versioned and realistic."

---

### 16.10 Phase 10: Docker — Reproducible Build

**What we create**: A multi-stage Dockerfile that builds, tests, and produces a minimal runtime image.

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

**Why multi-stage**:
- **Build stage** (~800MB) has the SDK — builds, runs tests, publishes. If tests fail, the build fails. Nobody gets a Docker image with failing tests
- **Runtime stage** (~80MB) has only the .NET runtime — 10x smaller. No SDK, no source code, no test assemblies. This is what runs in production
- **Evaluator experience**: `docker build -t syncmetrics .` → sees tests pass during build. `docker run --rm -v ${PWD}/output:/app/output syncmetrics` → sees weather data. No .NET SDK needed on their machine

**Why NOT Docker Compose**: There's no database, no Redis, no message queue, no second service. A single Dockerfile is the right tool. Docker Compose for one container would signal over-engineering.

**Dual-path README instructions**:
```
# Option 1: Direct (requires .NET 8 SDK)
dotnet run --project src/SyncMetrics.Pipeline

# Option 2: Docker (requires Docker only)
docker build -t syncmetrics-pipeline .
docker run --rm -v ${PWD}/output:/app/output syncmetrics-pipeline
```

---

### 16.11 Phase 11: CI/CD + Documentation

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

**Why include CI when it's not required**: Takes 5 minutes to add, signals professional discipline. Green badge on the repo means the evaluator sees "tests passing in a clean environment" before they even clone. Proves it works beyond Arthur's machine.

**What we deliberately do NOT add to CI**: Docker publishing, deployment steps, code coverage thresholds, SonarQube scanning. Each would be unnecessary scope creep for a take-home.

#### 11B. `README.md` — Short, Useful, Staff-Level

Structure:
1. **One-sentence description**: "SyncMetrics Weather Pipeline — .NET 8 ingestion pipeline that fetches, normalizes, and outputs weather forecast data"
2. **Quick start**: `dotnet run` and `docker` commands (4 lines)
3. **Architecture**: One paragraph + the interface flow diagram from Section 4
4. **Extensibility**: "To add a second source: create a feature folder, implement 4 interfaces, register in DI. See Features/OpenMeteo/ as reference."
5. **Assumptions & Trade-offs**: Bullet list of key decisions (single project vs. multi, Result<T> vs. exceptions, no database, etc.)
6. **Testing**: `dotnet test` + what's covered

**Spec says**: "Don't over-document; write what you'd want a new teammate to know." We follow this exactly.

#### 11C. `AI.md` — 10-15 Lines, Honest and Specific

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

**Interview defense**: "AI scaffolded the plumbing — HTTP setup, JSON models, test fixtures. I overrode it on the retry policy because the generated code didn't distinguish transient from permanent failures. I deliberately kept architecture decisions human-driven because those require understanding what the evaluators value, not what generates fastest."

---

### 16.12 Build Order — Step-by-Step Execution Sequence

This is the order we will implement. Each step builds on the previous and results in a compilable state.

| Step | What | Depends on | Deliverable state after step |
|------|------|-----------|------------------------------|
| 1 | Solution + projects + folder structure + NuGet packages | — | Solution builds (empty) |
| 2 | `Result<T>` + `PipelineError` | Step 1 | SharedKernel compiles |
| 3 | Models (`NormalizedWeatherRecord`, `LocationConfig`, `ProcessingResult`, `PipelineSummary`) | Step 2 | All models available |
| 4 | Interfaces (all 5: `IWeatherApiClient`, `IResponseParser`, `IDataTransformer`, `IOutputWriter`, `IWeatherDataSource`) | Step 3 | Interface contracts defined |
| 5 | Configuration: `appsettings.json` + `PipelineOptions` + `FieldMapping` + `ServiceRegistration` | Step 4 | Config binding works |
| 6 | `OpenMeteoApiResponse` deserialization model | Step 4 | API contract modeled |
| 7 | `OpenMeteoApiClient` (HTTP fetch, returns `Result<string>`) | Steps 5, 6 | Can call Open-Meteo API |
| 8 | `OpenMeteoResponseParser` (JSON → validated model, returns `Result<T>`) | Step 6 | Can parse API responses |
| 9 | `OpenMeteoTransformer` (model → normalized records, returns `Result<T>`) | Steps 3, 6 | Can transform data |
| 10 | `OpenMeteoDataSource` (composite: fetch→parse→transform with `Task.WhenAll`) | Steps 7, 8, 9 | Complete source pipeline |
| 11 | `TabDelimitedFileWriter` (writes `.tsv`) | Step 4 | Can write output |
| 12 | `PipelineCoordinator` (orchestrate all sources, build summary) | Steps 4, 10, 11 | Pipeline runs end-to-end |
| 13 | `Program.cs` — wire DI, bind config, run coordinator | Steps 5, 12 | **`dotnet run` works!** |
| 14 | Test fixtures: all JSON files in TestData/ | Step 6 | Test data ready |
| 15 | Tests: `OpenMeteoResponseParserTests` (8 tests) | Steps 8, 14 | Parser fully tested |
| 16 | Tests: `OpenMeteoTransformerTests` (6 tests) | Step 9 | Transformer fully tested |
| 17 | Tests: `TabDelimitedWriterTests` (5 tests) | Step 11 | Output fully tested |
| 18 | Tests: `PipelineCoordinatorTests` (5 tests) | Step 12 | E2E fully tested |
| 19 | Retry logic: `.AddStandardResilienceHandler()` configuration | Step 7 | Retry works |
| 20 | Tests: `RetryPolicyTests` (3 tests) | Step 19 | Retry tested |
| 21 | Dockerfile (multi-stage build) | Step 13 | Docker build + run works |
| 22 | GitHub Actions workflow | Step 13 | CI green |
| 23 | Generate sample output: run pipeline, save `.tsv` to `output/sample/` | Step 13 | Sample available for evaluator |
| 24 | `README.md` | All steps | Documentation complete |
| 25 | `AI.md` | All steps | AI usage documented |

**Checkpoint after Step 13**: The pipeline runs end-to-end. This is the minimum viable submission. Steps 14-25 add tests, retry, Docker, CI, and docs — all required for a complete submission but the pipeline itself works at Step 13.

---

### 16.13 What We Deliberately Do NOT Build — And Why

| Excluded item | Why excluded | What to say if asked |
|--------------|-------------|---------------------|
| **React / any frontend** | Not in spec. Actively harms "staff judgment" score. Every eval criterion focuses on the C# pipeline | "I deliberately scoped to pipeline quality over breadth. The IOutputWriter abstraction means a Minimal API + React dashboard is a straightforward future step." |
| **Database (any)** | Spec says file output. Adds dependency the evaluator must install | "IOutputWriter allows swapping to PostgreSQL or Cosmos with a single DI registration. But files are what was asked for." |
| **ASP.NET Minimal API / web server** | Spec says `dotnet run` → execute → exit. Not `dotnet run` → server starts | "If SyncMetrics needs it as a service, wrapping PipelineCoordinator in an API with BackgroundService takes 2-3 hours." |
| **MediatR / CQRS** | Semantic mismatch. MediatR is for request/response dispatch, not linear pipeline flow | "I considered MediatR for cross-cutting concerns but the pipeline is sequential, not dispatch-based." |
| **Multiple .csproj layers** | Single project enforces the same dependency rule with zero inter-project ceremony | "Clean Architecture is a dependency rule, not a project count. My SharedKernel enforces inward dependencies." |
| **LanguageExt / functional libraries** | 900KB package for 50 lines of Result<T> | "Minimal dependency surface. I can implement Result<T> in 50 lines tailored to my error model." |
| **AutoMapper** | 5 field mappings. Manual mapping is clearer and easier to debug | "AutoMapper's value is proportional to the mapping count. 5 fields don't justify the abstraction." |
| **Serilog** | Built-in `Microsoft.Extensions.Logging` with console provider is sufficient | "Serilog adds structured logging to sinks. We only have console output. Built-in logging covers it." |
| **Code coverage tooling** | Nice but unnecessary scope creep for a take-home | "I'd add coverlet + ReportGenerator in a production CI. For this exercise, the test list is the coverage story." |

---

> **End of Checkpoint Document**  
> Re-read Section 1 at the start of each session. When ready to implement, proceed to Section 16.12 Build Order.  
> **10 approaches analyzed. Winner: Approach 10 — Modular Monolith with Feature Folders + SharedKernel + Result<T>.**  
> **Frontend verdict: DO NOT BUILD. This is the most important "staff judgment" call in the exercise.**  
> **Implementation plan: 25 steps across 11 phases. Minimum viable at Step 13. Full submission at Step 25.**
