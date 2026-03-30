# SyncMetrics Weather Pipeline

A .NET 8 console pipeline that fetches 7-day weather forecasts for configurable locations from the [Open-Meteo API](https://open-meteo.com/), normalizes the data into a unified schema, and writes tab-delimited output files for downstream analytics import.

---

## Quick Start

**Prerequisites**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)

```bash
# Clone and run
git clone <repo-url>
cd SyncMetrics.WeatherPipeline

dotnet run --project src/SyncMetrics.Pipeline.Console
```

Output is written to `./output/weather_data_{timestamp}.tsv`. A processing summary is printed to the console on completion.

**Run tests**:

```bash
dotnet test
```

**Docker** (no .NET SDK required):

```bash
docker build -t syncmetrics-pipeline .
docker run --rm -v "${PWD}/output:/app/output" syncmetrics-pipeline
```

---

## Output Schema

Each row in the tab-delimited output represents one location for one forecast day:

| Column | Type | Description |
|--------|------|-------------|
| `Source` | string | Data source name (`OpenMeteo`) |
| `Location` | string | Configured location name |
| `Latitude` | decimal | Grid-snapped latitude from API response |
| `Longitude` | decimal | Grid-snapped longitude from API response |
| `Date` | ISO 8601 | Forecast date (`yyyy-MM-dd`) |
| `TempMaxC` | decimal\|empty | Max temperature in °C — empty if not reported |
| `TempMinC` | decimal\|empty | Min temperature in °C — empty if not reported |
| `PrecipitationMm` | decimal\|empty | Total precipitation in mm |
| `WindSpeedMaxKmh` | decimal\|empty | Max wind speed in km/h |
| `UVIndexMax` | decimal\|empty | Max UV index |
| `FetchedAtUtc` | ISO 8601 | UTC timestamp of the fetch |

Null measurements write as empty strings — not `"null"`, not `"0"`. This distinguishes missing data from a zero measurement.

A sample output file is in [`output/sample/weather_data_sample.tsv`](output/sample/weather_data_sample.tsv).

---

## Configuration

All configuration lives in [`src/SyncMetrics.Pipeline.Console/appsettings.json`](src/SyncMetrics.Pipeline.Console/appsettings.json). Nothing is hardcoded.

**Locations** — add or remove entries under `Pipeline.Sources[0].Locations`:

```json
{ "Name": "Sydney", "Latitude": -33.8688, "Longitude": 151.2093 }
```

**Field mappings** — `Pipeline.Sources[0].FieldMappings` is the canonical registry that documents the source-to-output field correspondence, including units:

```json
{ "SourceField": "precipitation_hours", "OutputColumn": "PrecipitationHours", "Unit": "h" }
```

Adding a field to the pipeline requires three one-line code changes (a property in the response model, an assignment in the transformer, and a header entry in the writer) plus the config entry above. The config entry is not auto-wired at runtime — the type-safe static assignment in the transformer was chosen over reflection-based dynamic mapping to preserve compile-time type checking and keep the transformer testable in isolation.

**Output** — `Pipeline.OutputDirectory` and `Pipeline.OutputFilePattern` control where output files are written. The `{timestamp}` placeholder in the pattern is replaced with the run's UTC start time.

---

## Architecture

Four-project Clean Architecture. Dependencies flow inward only — the compiler enforces this via project references.

```
Console  ──►  Infrastructure  ──►  Application  ──►  Core
                                                   (zero deps)
```

| Project | Responsibility |
|---------|----------------|
| `Pipeline.Core` | Domain models, interface contracts, `Result<T>` error type |
| `Pipeline.Application` | `PipelineCoordinator` — orchestrates all sources concurrently |
| `Pipeline.Infrastructure` | HTTP client, JSON parser, data transformer, TSV writer, DI wiring |
| `Pipeline.Console` | Entry point, host setup, config binding, exit code |

**Pipeline flow per source**:

```
IWeatherApiClient.FetchAsync()   →  Result<string>          (raw JSON or FetchError)
IResponseParser.Parse()          →  Result<SourceModel>     (validated DTO or ParseError)
IDataTransformer.Transform()     →  Result<NormalizedList>  (normalized records or TransformError)
IOutputWriter.WriteAsync()       →  Result<string>          (output file path or OutputError)
```

All stages return `Result<T>` — errors are values, not exceptions. A failing location is collected in the summary without aborting other locations.

**Adding a second data source** requires four steps, zero changes to existing code:

1. Create `src/SyncMetrics.Pipeline.Infrastructure/YourSource/` with classes implementing `IWeatherApiClient`, `IResponseParser<T>`, `IDataTransformer<T>`, and `IWeatherDataSource`
2. Add a source entry to `appsettings.json`
3. Register the new `IWeatherDataSource` in `ServiceRegistration.cs`
4. The `PipelineCoordinator` discovers all registered sources via `IEnumerable<IWeatherDataSource>` — no coordinator code changes

See `Infrastructure/OpenMeteo/` as the reference implementation.

---

## Assumptions & Trade-offs

**No database** — the spec requires tab-delimited file output for downstream analytics import. The `IOutputWriter` abstraction makes swapping to a database writer a single DI registration change.

**Null vs. zero** — missing weather measurements write as empty strings. A `0.0` precipitation reading means it didn't rain; an empty string means the API didn't report a value. Conflating them would corrupt downstream aggregations.

**Grid-snapped coordinates** — Open-Meteo snaps request coordinates to its internal grid. The output columns use the coordinates from the API response, not the config, to keep the output consistent with what the API actually used.

**`wind_speed_10m_max`** — the exercise endpoint sample uses `windspeed_10m_max` (no underscore between "wind" and "speed"). The live API uses `wind_speed_10m_max`. The implementation uses the correct field name, validated against the Open-Meteo documentation.

**Retry on transient failures only** — the resilience pipeline retries on 5xx, 408, 429, and network exceptions. It does not retry 4xx responses: a 400 Bad Request means the URL is malformed and retrying sends the same bad request; a 404 means the endpoint doesn't exist.

**Unicode output** — UTF-8 without BOM. BOM causes parse errors in many downstream tools (Python `csv`, PostgreSQL `COPY`, Excel on some platforms).

**`InvariantCulture` for all numeric formatting** — decimal fields always use `.` as the decimal separator regardless of the host machine's locale. A pipeline run on a German server would otherwise write `"15,3"` instead of `"15.3"`.

---

## Testing

```
Test summary: total: 32; failed: 0; succeeded: 32; skipped: 0
```

| Test class | Tests | What it covers |
|-----------|-------|----------------|
| `OpenMeteoResponseParserTests` | 10 | Valid JSON, API error body, missing `daily` key, null array values, mismatched array lengths, invalid JSON, London negative-longitude coords, empty time array |
| `OpenMeteoTransformerTests` | 6 | Record count, source/location field mapping, null passthrough, unparseable date |
| `TabDelimitedWriterTests` | 5 | File creation, header columns, null→empty, InvariantCulture decimal formatting |
| `PipelineCoordinatorTests` | 5 | Success path, error aggregation, no-write on zero records, write failure handling, duration |
| `EndToEndPipelineTests` | 3 | Full pipeline with real Parser + Transformer + Writer, HTTP mocked at the boundary |
| `RetryPolicyTests` | 3 | Transient 503→success with retry, permanent 400 no-retry, 500×4 exhaustion (3 retries) |

JSON test fixtures are embedded resources in the test assembly — no relative path dependency.
