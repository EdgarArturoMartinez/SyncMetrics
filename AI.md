# AI Usage

GitHub Copilot and Claude were used as development tools throughout this exercise.

## What AI Drove

- Project scaffolding: folder structure, `.csproj` files, solution file, `.gitignore`
- HTTP client boilerplate: `IHttpClientFactory` named client setup, query string construction
- JSON deserialization model (`OpenMeteoApiResponse`, `OpenMeteoDailyData`) from API documentation
- Test fixture JSON files generated from Open-Meteo API response documentation
- Tab-delimited output formatting logic and `StreamWriter` boilerplate
- `LoggerMessage` source generator method signatures

## Where I Overrode AI

- **Retry policy**: AI-generated retry treated all HTTP errors as retryable. I corrected this to distinguish transient failures (5xx, 408, 429, `HttpRequestException`) from permanent ones (4xx). Retrying a 400 Bad Request sends the same malformed URL repeatedly — it masks bugs rather than recovering from them.
- **Wind speed field name**: AI used `windspeed_10m_max` from the exercise sample URL. The live Open-Meteo API uses `wind_speed_10m_max` (underscores between "wind", "speed", and the rest). I validated against the official API documentation directly.
- **Null handling in output**: AI defaulted to writing `"null"` for missing measurements. I changed this to empty string — a `0.0` precipitation reading and a missing reading are semantically different values; conflating them would corrupt downstream aggregations.
- **CA1707 suppression scope**: AI initially suppressed the no-underscore naming warning globally in `Directory.Build.props`. I moved the suppression to test project files only — xUnit's `Given_When_Then_` convention is legitimate in tests, but production code should enforce .NET naming conventions.

## Deliberate Non-Use

Architecture decisions — approach selection, interface design, the `Result<T>` error model, the four-project layer structure — were made without AI. These require understanding the evaluation criteria, judging proportional complexity for the scope, and recognizing which patterns communicate intent to an expert reviewer. AI generates code that works; architecture decisions require judgment about what the reader will value.
