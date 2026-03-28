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

> **End of Checkpoint Document**  
> Re-read Section 1 at the start of each session. When ready to implement, proceed to Section 15 Roadmap.  
> **10 approaches analyzed. Winner: Approach 10 — Modular Monolith with Feature Folders + SharedKernel + Result<T>.**  
> **Frontend verdict: DO NOT BUILD. This is the most important "staff judgment" call in the exercise.**
