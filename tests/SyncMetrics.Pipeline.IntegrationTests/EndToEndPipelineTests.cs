using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyncMetrics.Pipeline.Application;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.Configuration;
using SyncMetrics.Pipeline.Infrastructure.OpenMeteo;
using SyncMetrics.Pipeline.Infrastructure.Output;

namespace SyncMetrics.Pipeline.IntegrationTests;

/// <summary>
/// End-to-end pipeline tests that run the full stack —
/// real Parser, real Transformer, real FileWriter — with only the HTTP tier replaced
/// by an NSubstitute fake. This is the boundary where "unit" ends and "integration" begins.
///
/// WHY THIS LAYER EXISTS (real-life rationale):
///   Unit tests prove individual classes behave correctly in isolation.
///   Integration tests prove the DI wiring is correct and the classes COMPOSE correctly.
///   A real production incident: the transformer returned 0s instead of nulls because
///   a new field was added to the response model but the transformer was not updated.
///   Every unit test passed. The integration test would have caught it.
///
/// SOLID demonstrated:
///   DIP — The only concrete type we "know about" is IWeatherApiClient (the boundary).
///          Real Parser + Transformer + Writer are resolved by the DI container — the
///          test doesn't know or care which concrete classes they are.
///   OCP — Replacing the API client with a different source (WeatherAPI.com) requires
///          only substituting IWeatherApiClient. The rest of the DI graph is unchanged.
///
/// Design patterns:
///   Dependency Injection (service container) — tests build the real production DI
///   graph and override only the external integration point (HTTP tier).
///   Test Seam — IWeatherApiClient is the architectural seam: everything inside it is
///               exercised for real; everything outside (actual HTTP) is substituted.
///   IDisposable — temp directories are cleaned up; the service provider is disposed
///                 so HttpClient connections are closed properly.
/// </summary>
public sealed class EndToEndPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IWeatherApiClient _fakeApiClient;
    private readonly PipelineCoordinator _coordinator;
    private readonly ServiceProvider _serviceProvider;

    private static readonly LocationConfig NewYork = new()
    {
        Name = "New York",
        Latitude = 40.7128,
        Longitude = -74.006,
    };

    public EndToEndPipelineTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            $"syncmetrics_e2e_{Guid.NewGuid():N}");

        _fakeApiClient = Substitute.For<IWeatherApiClient>();
        _fakeApiClient.SourceName.Returns("OpenMeteo");

        var services = new ServiceCollection();

        // Logging infrastructure (no-op — keeps test output clean)
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Options
        services.AddOptions();
        services.Configure<PipelineOptions>(opts =>
        {
            opts.OutputDirectory = _tempDir;
            opts.OutputFilePattern = "weather_data_{timestamp}.tsv";
        });

        // Real pipeline — everything except the HTTP boundary
        services.AddSingleton(_fakeApiClient);
        services.AddSingleton<IResponseParser<OpenMeteoApiResponse>, OpenMeteoResponseParser>();
        services.AddSingleton<IDataTransformer<OpenMeteoApiResponse>, OpenMeteoTransformer>();
        services.AddSingleton<IWeatherDataSource, OpenMeteoDataSource>();
        services.AddSingleton<IOutputWriter, TabDelimitedFileWriter>();
        services.AddSingleton<PipelineCoordinator>();

        _serviceProvider = services.BuildServiceProvider();
        _coordinator = _serviceProvider.GetRequiredService<PipelineCoordinator>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullPipeline_WithValidApiResponse_ProducesPopulatedOutputFile()
    {
        // Arrange
        _fakeApiClient.FetchAsync(Arg.Any<LocationConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success(ValidSevenDayJson())));

        var sourceLocations = new Dictionary<string, IReadOnlyList<LocationConfig>>
        {
            ["OpenMeteo"] = new[] { NewYork },
        };

        // Act
        var summary = await _coordinator.RunAsync(sourceLocations, CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(7, "one record per forecast day");
        summary.HasErrors.Should().BeFalse();
        summary.OutputFilePath.Should().NotBeNullOrEmpty();
        File.Exists(summary.OutputFilePath!).Should().BeTrue();
    }

    [Fact]
    public async Task FullPipeline_WithApiErrorResponse_ProducesZeroRecordsAndSurfacesError()
    {
        // Arrange — API returns HTTP 503
        _fakeApiClient.FetchAsync(Arg.Any<LocationConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Failure(
                new FetchError("Service unavailable", "New York", 503))));

        var sourceLocations = new Dictionary<string, IReadOnlyList<LocationConfig>>
        {
            ["OpenMeteo"] = new[] { NewYork },
        };

        // Act
        var summary = await _coordinator.RunAsync(sourceLocations, CancellationToken.None);

        // Assert — pipeline resilience: error is surfaced, not swallowed
        summary.TotalRecordsWritten.Should().Be(0);
        summary.HasErrors.Should().BeTrue();
        summary.AllErrors.Should().ContainSingle().Which.Should().BeOfType<FetchError>();
    }

    [Fact]
    public async Task FullPipeline_ThreeLocations_ProducesTwentyOneRecordsTotal()
    {
        // Arrange — the fake returns 7 days for every location (real concurrent behaviour)
        _fakeApiClient.FetchAsync(Arg.Any<LocationConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success(ValidSevenDayJson())));

        var sourceLocations = new Dictionary<string, IReadOnlyList<LocationConfig>>
        {
            ["OpenMeteo"] = new[]
            {
                new LocationConfig { Name = "New York", Latitude = 40.7128,  Longitude = -74.006   },
                new LocationConfig { Name = "London",   Latitude = 51.5074,  Longitude = -0.1278   },
                new LocationConfig { Name = "Tokyo",    Latitude = 35.6762,  Longitude = 139.6503  },
            },
        };

        // Act
        var summary = await _coordinator.RunAsync(sourceLocations, CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(21, "3 locations × 7 days");
        summary.HasErrors.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal but complete 7-day Open-Meteo response — inlined so the integration
    /// tests have no file-system dependency of their own.
    /// </summary>
    private static string ValidSevenDayJson() => """
        {
          "latitude": 40.710335,
          "longitude": -74.00594,
          "timezone": "America/New_York",
          "daily": {
            "time": [
              "2026-03-29","2026-03-30","2026-03-31",
              "2026-04-01","2026-04-02","2026-04-03","2026-04-04"
            ],
            "temperature_2m_max": [12.5, 14.2, 11.8, 13.1, 15.6, 10.3, 9.8],
            "temperature_2m_min": [5.2,  6.8,  4.1,  5.9,  7.3,  3.2,  2.1],
            "precipitation_sum":  [0.0,  2.4,  5.1,  0.0,  0.0,  8.2,  3.6],
            "wind_speed_10m_max": [18.5, 22.1, 15.3, 19.8, 12.4, 25.6, 20.1],
            "uv_index_max":       [3.2,  2.8,  1.5,  4.1,  5.2,  1.0,  2.3]
          }
        }
        """;
}
