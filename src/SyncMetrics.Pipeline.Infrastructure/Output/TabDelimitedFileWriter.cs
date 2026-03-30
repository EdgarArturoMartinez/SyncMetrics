using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.Configuration;

namespace SyncMetrics.Pipeline.Infrastructure.Output;

/// <summary>
/// Writes normalized weather records to a tab-delimited (.tsv) file.
/// TSV chosen over CSV because weather data can contain commas in location names —
/// tabs are unambiguous and require no quoting rules.
/// </summary>
public sealed class TabDelimitedFileWriter : IOutputWriter
{
    private readonly PipelineOptions _options;

    private static readonly string[] HeaderColumns =
    [
        "Source",
        "Location",
        "Latitude",
        "Longitude",
        "Date",
        "TempMaxC",
        "TempMinC",
        "PrecipitationMm",
        "WindSpeedMaxKmh",
        "UVIndexMax",
        "FetchedAtUtc"
    ];

    public TabDelimitedFileWriter(IOptions<PipelineOptions> options)
    {
        _options = options.Value;
    }

    public async Task<Result<string>> WriteAsync(
        IReadOnlyList<NormalizedWeatherRecord> records, CancellationToken cancellationToken)
    {
        var filePath = string.Empty;

        try
        {
            Directory.CreateDirectory(_options.OutputDirectory);

            var fileName = _options.OutputFilePattern.Replace(
                "{timestamp}",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

            if (Path.IsPathRooted(fileName) || fileName.Contains("..", StringComparison.Ordinal))
            {
                return Result<string>.Failure(new OutputError(
                    "OutputFilePattern must be a relative filename without path traversal.",
                    FilePath: fileName));
            }

            filePath = Path.Combine(_options.OutputDirectory, fileName);

            // UTF-8 without BOM — the universal default for data interchange files
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            await using var writer = new StreamWriter(filePath, append: false, encoding);

            // Header row
            await writer.WriteLineAsync(string.Join('\t', HeaderColumns)).ConfigureAwait(false);

            // Data rows
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = string.Join('\t',
                    record.Source,
                    record.Location,
                    record.Latitude.ToString("F4", CultureInfo.InvariantCulture),
                    record.Longitude.ToString("F4", CultureInfo.InvariantCulture),
                    record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    FormatNullableDouble(record.TempMaxCelsius),
                    FormatNullableDouble(record.TempMinCelsius),
                    FormatNullableDouble(record.PrecipitationMm),
                    FormatNullableDouble(record.WindSpeedMaxKmh),
                    FormatNullableDouble(record.UvIndexMax),
                    record.FetchedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }

            return Result<string>.Success(filePath);
        }
        catch (OperationCanceledException)
        {
            throw; // Respect caller cancellation
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(new OutputError(
                $"Failed to write output file: {ex.Message}",
                FilePath: filePath));
        }
    }

    /// <summary>
    /// Formats a nullable double: null → empty string (not "null"), value → InvariantCulture.
    /// Empty string in TSV means "no data" — distinct from 0.0 which means "measured as zero."
    /// </summary>
    private static string FormatNullableDouble(double? value) =>
        value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
}
