using FluentAssertions;
using Microsoft.Extensions.Options;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.Configuration;
using SyncMetrics.Pipeline.Infrastructure.Output;

namespace SyncMetrics.Pipeline.UnitTests.Output;

/// <summary>
/// Unit tests for <see cref="TabDelimitedFileWriter"/>.
///
/// SOLID demonstrated:
///   SRP — Writer has one job: IReadOnlyList&lt;NormalizedWeatherRecord&gt; → .tsv file.
///          It does not parse, transform, or validate business logic.
///   LSP — TabDelimitedFileWriter fully substitutes IOutputWriter; the coordinator
///          never needs to know it writes to disk vs. a stream vs. a DB.
///   DIP — Writer depends on IOptions&lt;PipelineOptions&gt; (abstraction), not on
///          hard-coded paths or environment variables.
///
/// Design patterns:
///   Options pattern — IOptions&lt;T&gt; makes the output directory and filename pattern
///                     replaceable in tests without touching production config.
///   IDisposable     — Each test instance owns a fresh temp directory that is deleted
///                     on teardown. No shared state between tests.
///
/// Good practices:
///   Null-to-empty  — A null measurement in the record MUST write an empty tab field,
///                    not the string "null" or "0". Analytics tools treat empty as
///                    missing; "null" or "0" would silently corrupt the dataset.
///   InvariantCulture — Numbers must use "." as decimal separator regardless of the
///                       machine's locale. A CI server with a German locale would write
///                       "15,3" (comma) which breaks every downstream TSV parser.
/// </summary>
public sealed class TabDelimitedWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TabDelimitedFileWriter _sut;

    public TabDelimitedWriterTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            $"syncmetrics_test_{Guid.NewGuid():N}");

        var options = Options.Create(new PipelineOptions
        {
            OutputDirectory = _tempDir,
            OutputFilePattern = "weather_data_{timestamp}.tsv",
        });

        _sut = new TabDelimitedFileWriter(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── File creation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_ValidRecords_CreatesOutputFile()
    {
        var records = new[] { MakeRecord() };

        var result = await _sut.WriteAsync(records, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        File.Exists(result.Value).Should().BeTrue();
    }

    // ── Content correctness ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CreatesFileWithExpectedHeaderColumns()
    {
        var result = await _sut.WriteAsync(new[] { MakeRecord() }, CancellationToken.None);

        var header = (await File.ReadAllLinesAsync(result.Value!))[0];
        var columns = header.Split('\t');

        columns.Should().Contain("Source")
            .And.Contain("Location")
            .And.Contain("Date")
            .And.Contain("TempMaxC")
            .And.Contain("FetchedAtUtc");
    }

    [Fact]
    public async Task WriteAsync_OneRecord_WritesHeaderPlusOneDataRow()
    {
        var result = await _sut.WriteAsync(new[] { MakeRecord() }, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(result.Value!);

        // ReadAllLines strips the final newline, so: 1 header + 1 data = 2 lines
        lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task WriteAsync_NullMeasurement_WritesEmptyTabField()
    {
        // Null measurements must produce an empty field — NOT "null" or "0".
        // Column layout: Source(0) Location(1) Lat(2) Lon(3) Date(4)
        //   TempMaxC(5) TempMinC(6) PrecipitationMm(7) WindSpeedMaxKmh(8) UVIndexMax(9) FetchedAtUtc(10)
        var result = await _sut.WriteAsync(new[] { MakeRecordWithNullMeasurements() }, CancellationToken.None);

        var dataLine = (await File.ReadAllLinesAsync(result.Value!))[1];
        var fields = dataLine.Split('\t');

        fields[5].Should().BeEmpty("null TempMaxC must be empty, not '0' or 'null'");
        fields[6].Should().BeEmpty("null TempMinC must be empty");
        fields[9].Should().BeEmpty("null UVIndexMax must be empty");
    }

    [Fact]
    public async Task WriteAsync_UsesInvariantCultureForDecimalFields()
    {
        // A machine with a German locale would write "15,3" — breaking TSV parsers.
        // InvariantCulture guarantees the period "." separator on every platform.
        var record = MakeRecord();  // TempMaxCelsius = 15.3

        var result = await _sut.WriteAsync(new[] { record }, CancellationToken.None);
        var content = await File.ReadAllTextAsync(result.Value!);

        content.Should().Contain("15.3", "decimal separator must be a period (InvariantCulture)");
        content.Should().NotContain("15,3", "comma decimal separator indicates locale bug");
    }

    // ── Structural round-trip validation ──────────────────────────────────────

    [Fact]
    public async Task WriteAsync_OutputIsStructurallyValidTsv_AllRowsHaveCorrectFieldCount()
    {
        // Proves the writer produces parseable TSV — every data row has the same number
        // of tab-separated fields as the header. A missing tab would silently shift columns
        // in downstream parsers, corrupting the dataset.
        var records = new[]
        {
            MakeRecord(),
            MakeRecordWithNullMeasurements(),
        };

        var result = await _sut.WriteAsync(records, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(result.Value!);
        var headerFieldCount = lines[0].Split('\t').Length;

        // Every data row must have exactly as many fields as the header
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = lines[i].Split('\t');
            fields.Should().HaveCount(headerFieldCount,
                $"data row {i} must have {headerFieldCount} tab-separated fields, same as the header");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NormalizedWeatherRecord MakeRecord() => new()
    {
        Source = "OpenMeteo",
        Location = "New York",
        Latitude = 40.7128,
        Longitude = -74.006,
        Date = new DateOnly(2026, 3, 29),
        TempMaxCelsius = 15.3,
        TempMinCelsius = 7.8,
        PrecipitationMm = 2.1,
        WindSpeedMaxKmh = 18.5,
        UvIndexMax = 4.2,
        FetchedAtUtc = new DateTime(2026, 3, 29, 12, 0, 0, DateTimeKind.Utc),
    };

    private static NormalizedWeatherRecord MakeRecordWithNullMeasurements() => new()
    {
        Source = "OpenMeteo",
        Location = "New York",
        Latitude = 40.7128,
        Longitude = -74.006,
        Date = new DateOnly(2026, 3, 29),
        TempMaxCelsius = null,
        TempMinCelsius = null,
        PrecipitationMm = null,
        WindSpeedMaxKmh = null,
        UvIndexMax = null,
        FetchedAtUtc = new DateTime(2026, 3, 29, 12, 0, 0, DateTimeKind.Utc),
    };
}
