using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SyncMetrics.Pipeline.Application;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.UnitTests.Pipeline;

/// <summary>
/// Unit tests for <see cref="PipelineCoordinator"/>.
///
/// SOLID demonstrated:
///   SRP — Coordinator only orchestrates: it runs sources, collects results,
///          delegates writing, and builds the summary. It never parses JSON or
///          writes files itself — those responsibilities live behind interfaces.
///   OCP — Adding a new IWeatherDataSource requires zero changes here; the
///          coordinator discovers all registered sources via IEnumerable&lt;T&gt;.
///   DIP — Every dependency (IWeatherDataSource, IOutputWriter, ILogger) is
///          injected as an abstraction. Tests swap in NSubstitute fakes without
///          touching production wiring.
///
/// Design patterns:
///   Strategy     — IWeatherDataSource is the strategy interface. Tests verify the
///                   coordinator works with any implementation, not a specific one.
///   NullObject   — NullLogger&lt;T&gt;.Instance satisfies the logger dependency without
///                  routing log output anywhere — clean, zero-noise test runs.
///   Test Double  — NSubstitute generates fakes at the interface boundary so each
///                  test only exercises the coordinator's logic, not the plumbing.
///
/// Good practices:
///   Boundary between unit and integration — coordinator tests do NOT involve real
///   HTTP, real files, or real configuration. They prove orchestration logic only.
/// </summary>
public sealed class PipelineCoordinatorTests
{
    private static readonly LocationConfig NewYork = new()
    {
        Name = "New York",
        Latitude = 40.7128,
        Longitude = -74.006,
    };

    private static Dictionary<string, IReadOnlyList<LocationConfig>> SingleSourceLocations(
        string sourceName = "OpenMeteo") =>
        new Dictionary<string, IReadOnlyList<LocationConfig>>
        {
            [sourceName] = new[] { NewYork },
        };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SourceSucceeds_SummaryReflectsAllRecords()
    {
        // Arrange
        var records = new[] { MakeRecord("New York"), MakeRecord("London") };
        var mockSource = BuildMockSource("OpenMeteo", records: records);

        var mockWriter = Substitute.For<IOutputWriter>();
        mockWriter.WriteAsync(Arg.Any<IReadOnlyList<NormalizedWeatherRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success("/tmp/weather.tsv")));

        var coordinator = BuildCoordinator(mockSource, mockWriter);

        // Act
        var summary = await coordinator.RunAsync(SingleSourceLocations(), CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(2);
        summary.HasErrors.Should().BeFalse();
        summary.OutputFilePath.Should().Be("/tmp/weather.tsv");
    }

    [Fact]
    public async Task RunAsync_SourceReturnsErrors_ErrorsAreAggregatedInSummary()
    {
        // Partial success: one location succeeds, one fails. The coordinator must
        // continue and surface both outcomes — no silent failures.
        var fetchError = new FetchError("Timeout", "London", 503);
        var mockSource = BuildMockSource("OpenMeteo",
            records: new[] { MakeRecord("New York") },
            errors: new PipelineError[] { fetchError });

        var mockWriter = Substitute.For<IOutputWriter>();
        mockWriter.WriteAsync(Arg.Any<IReadOnlyList<NormalizedWeatherRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success("/tmp/weather.tsv")));

        var coordinator = BuildCoordinator(mockSource, mockWriter);

        // Act
        var summary = await coordinator.RunAsync(SingleSourceLocations(), CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(1);
        summary.HasErrors.Should().BeTrue();
        summary.AllErrors.Should().ContainSingle().Which.Should().BeOfType<FetchError>();
    }

    [Fact]
    public async Task RunAsync_NoRecordsProduced_DoesNotCallOutputWriter()
    {
        // When every source location fails, the output writer must not be called —
        // creating an empty file would look like a successful zero-record run.
        var mockSource = BuildMockSource("OpenMeteo",
            records: Array.Empty<NormalizedWeatherRecord>(),
            errors: new PipelineError[] { new FetchError("All failed", "New York", 500) });

        var mockWriter = Substitute.For<IOutputWriter>();

        var coordinator = BuildCoordinator(mockSource, mockWriter);

        // Act
        var summary = await coordinator.RunAsync(SingleSourceLocations(), CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(0);
        // CS4014 suppressed: DidNotReceive().WriteAsync() returns a proxy Task for assertion tracking,
        // not an actual async operation — NSubstitute records the check synchronously.
        #pragma warning disable CS4014
        mockWriter.DidNotReceive().WriteAsync(
            Arg.Any<IReadOnlyList<NormalizedWeatherRecord>>(),
            Arg.Any<CancellationToken>());
        #pragma warning restore CS4014
    }

    [Fact]
    public async Task RunAsync_OutputWriterFails_SummaryHasNullOutputFilePath()
    {
        // If the disk is full or the path is unavailable, the coordinator must
        // keep the summary valid — the processing still happened, files just weren't persisted.
        var mockSource = BuildMockSource("OpenMeteo", records: new[] { MakeRecord("New York") });

        var mockWriter = Substitute.For<IOutputWriter>();
        mockWriter.WriteAsync(Arg.Any<IReadOnlyList<NormalizedWeatherRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Failure(
                new OutputError("Disk full", "/tmp/weather.tsv"))));

        var coordinator = BuildCoordinator(mockSource, mockWriter);

        // Act
        var summary = await coordinator.RunAsync(SingleSourceLocations(), CancellationToken.None);

        // Assert — record count is still correct even when write failed
        summary.TotalRecordsWritten.Should().Be(1);
        summary.OutputFilePath.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_Summary_ContainsDurationGreaterThanZero()
    {
        var mockSource = BuildMockSource("OpenMeteo", records: new[] { MakeRecord("New York") });

        var mockWriter = Substitute.For<IOutputWriter>();
        mockWriter.WriteAsync(Arg.Any<IReadOnlyList<NormalizedWeatherRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<string>.Success("/tmp/weather.tsv")));

        var coordinator = BuildCoordinator(mockSource, mockWriter);

        var summary = await coordinator.RunAsync(SingleSourceLocations(), CancellationToken.None);

        summary.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // When the caller signals cancellation (e.g., Ctrl+C), the coordinator must
        // propagate the OperationCanceledException — not swallow it into a summary.
        var cts = new CancellationTokenSource();

        var mockSource = Substitute.For<IWeatherDataSource>();
        mockSource.SourceName.Returns("OpenMeteo");
        mockSource.ProcessAsync(Arg.Any<IEnumerable<LocationConfig>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var token = callInfo.ArgAt<CancellationToken>(1);
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new ProcessingResult
                {
                    SourceName = "OpenMeteo",
                    Records = Array.Empty<NormalizedWeatherRecord>(),
                    Errors = Array.Empty<PipelineError>(),
                    Duration = TimeSpan.Zero,
                    LocationsAttempted = 0,
                    LocationsSucceeded = 0,
                });
            });

        var mockWriter = Substitute.For<IOutputWriter>();
        var coordinator = BuildCoordinator(mockSource, mockWriter);

        // Cancel before RunAsync starts
        cts.Cancel();

        // Act & Assert
        var act = () => coordinator.RunAsync(SingleSourceLocations(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── All-fail scenarios ────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AllLocationsFail_SummaryContainsAllErrors()
    {
        // When every location in every source fails, the summary must surface all
        // errors — not just the first — so operators can diagnose multi-point outages.
        var errors = new PipelineError[]
        {
            new FetchError("Timeout", "New York", 503),
            new FetchError("DNS failure", "London", null),
            new FetchError("Connection refused", "Tokyo", null),
        };

        var mockSource = Substitute.For<IWeatherDataSource>();
        mockSource.SourceName.Returns("OpenMeteo");
        mockSource.ProcessAsync(Arg.Any<IEnumerable<LocationConfig>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessingResult
            {
                SourceName = "OpenMeteo",
                Records = Array.Empty<NormalizedWeatherRecord>(),
                Errors = errors,
                Duration = TimeSpan.FromMilliseconds(50),
                LocationsAttempted = 3,
                LocationsSucceeded = 0,
            }));

        var mockWriter = Substitute.For<IOutputWriter>();
        var coordinator = BuildCoordinator(mockSource, mockWriter);

        var locations = new Dictionary<string, IReadOnlyList<LocationConfig>>
        {
            ["OpenMeteo"] = new[]
            {
                NewYork,
                new LocationConfig { Name = "London", Latitude = 51.5074, Longitude = -0.1278 },
                new LocationConfig { Name = "Tokyo", Latitude = 35.6762, Longitude = 139.6503 },
            },
        };

        // Act
        var summary = await coordinator.RunAsync(locations, CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(0);
        summary.HasErrors.Should().BeTrue();
        summary.AllErrors.Should().HaveCount(3, "all three location errors must surface");
        summary.OutputFilePath.Should().BeNull("no file should be written when zero records exist");

        // Writer should never be called when there are zero records
        #pragma warning disable CS4014
        mockWriter.DidNotReceive().WriteAsync(
            Arg.Any<IReadOnlyList<NormalizedWeatherRecord>>(),
            Arg.Any<CancellationToken>());
        #pragma warning restore CS4014
    }

    // ── Edge case: empty location list ────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SourceRegisteredButNoLocationsConfigured_ProducesEmptySummary()
    {
        // If a source is registered in DI but its name has no matching entry
        // in the sourceLocations map, the coordinator passes an empty array.
        // This must not crash — it should produce zero records, zero errors.
        var mockSource = BuildMockSource("OpenMeteo",
            records: Array.Empty<NormalizedWeatherRecord>());

        var mockWriter = Substitute.For<IOutputWriter>();
        var coordinator = BuildCoordinator(mockSource, mockWriter);

        // Empty map — no locations for any source
        var emptyLocations = new Dictionary<string, IReadOnlyList<LocationConfig>>();

        // Act
        var summary = await coordinator.RunAsync(emptyLocations, CancellationToken.None);

        // Assert
        summary.TotalRecordsWritten.Should().Be(0);
        summary.HasErrors.Should().BeFalse();
        summary.OutputFilePath.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PipelineCoordinator BuildCoordinator(
        IWeatherDataSource source,
        IOutputWriter writer) =>
        new(
            dataSources: new[] { source },
            outputWriter: writer,
            logger: NullLogger<PipelineCoordinator>.Instance);

    private static IWeatherDataSource BuildMockSource(
        string sourceName,
        IReadOnlyList<NormalizedWeatherRecord>? records = null,
        IReadOnlyList<PipelineError>? errors = null)
    {
        var mock = Substitute.For<IWeatherDataSource>();
        mock.SourceName.Returns(sourceName);
        mock.ProcessAsync(Arg.Any<IEnumerable<LocationConfig>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProcessingResult
            {
                SourceName = sourceName,
                Records = records ?? Array.Empty<NormalizedWeatherRecord>(),
                Errors = errors ?? Array.Empty<PipelineError>(),
                Duration = TimeSpan.FromMilliseconds(50),
                LocationsAttempted = 1,
                LocationsSucceeded = records is { Count: > 0 } ? 1 : 0,
            }));
        return mock;
    }

    private static NormalizedWeatherRecord MakeRecord(string location) => new()
    {
        Source = "OpenMeteo",
        Location = location,
        Latitude = 40.7128,
        Longitude = -74.006,
        Date = new DateOnly(2026, 3, 29),
        TempMaxCelsius = 12.0,
        TempMinCelsius = 5.0,
        PrecipitationMm = 0.0,
        WindSpeedMaxKmh = 20.0,
        UvIndexMax = 3.0,
        FetchedAtUtc = DateTime.UtcNow,
    };
}
