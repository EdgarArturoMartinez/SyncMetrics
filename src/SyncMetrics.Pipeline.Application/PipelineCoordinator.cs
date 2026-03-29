using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Application;

/// <summary>
/// Central pipeline orchestrator — runs all registered data sources concurrently,
/// collects results, writes output, and produces a structured summary.
/// Lives in Application layer: depends only on Core interfaces, not on Infrastructure.
/// Uses LoggerMessage source generators for high-performance structured logging (CA1848/CA1873 compliant).
/// </summary>
public sealed partial class PipelineCoordinator
{
    private readonly IEnumerable<IWeatherDataSource> _dataSources;
    private readonly IOutputWriter _outputWriter;
    private readonly ILogger<PipelineCoordinator> _logger;

    public PipelineCoordinator(
        IEnumerable<IWeatherDataSource> dataSources,
        IOutputWriter outputWriter,
        ILogger<PipelineCoordinator> logger)
    {
        _dataSources = dataSources;
        _outputWriter = outputWriter;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full pipeline: fetch → parse → transform → write → summarize.
    /// <paramref name="sourceLocations"/> maps source name → locations, resolved from config by the caller.
    /// This keeps the coordinator free of Infrastructure config types.
    /// </summary>
    public async Task<PipelineSummary> RunAsync(
        IReadOnlyDictionary<string, IReadOnlyList<LocationConfig>> sourceLocations,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        LogPipelineStarting(_logger);

        // Run all data sources concurrently
        var tasks = _dataSources.Select(source =>
        {
            var locations = sourceLocations.TryGetValue(source.SourceName, out var locs)
                ? locs
                : Array.Empty<LocationConfig>();

            LogProcessingSource(_logger, source.SourceName, locations.Count);

            return source.ProcessAsync(locations, cancellationToken);
        });

        var sourceResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Aggregate all successful records across all sources
        var allRecords = sourceResults
            .SelectMany(r => r.Records)
            .ToList();

        // Write output if we have any records
        string? outputFilePath = null;
        if (allRecords.Count > 0)
        {
            var writeResult = await _outputWriter.WriteAsync(allRecords, cancellationToken).ConfigureAwait(false);
            if (writeResult.IsSuccess)
            {
                outputFilePath = writeResult.Value;
            }
            else
            {
                LogOutputWriteFailed(_logger, writeResult.Error.Message);
            }
        }
        else
        {
            LogNoRecords(_logger);
        }

        stopwatch.Stop();

        var summary = new PipelineSummary
        {
            SourceResults = sourceResults,
            TotalRecordsWritten = allRecords.Count,
            TotalDuration = stopwatch.Elapsed,
            OutputFilePath = outputFilePath,
        };

        PrintSummary(summary);

        return summary;
    }

    private void PrintSummary(PipelineSummary summary)
    {
        var totalAttempted = summary.SourceResults.Sum(r => r.LocationsAttempted);
        var totalSucceeded = summary.SourceResults.Sum(r => r.LocationsSucceeded);
        var totalFailed = totalAttempted - totalSucceeded;
        var sourceNames = string.Join(", ", summary.SourceResults.Select(r => r.SourceName));

        LogRunSummary(_logger,
            summary.SourceResults.Count,
            sourceNames,
            totalAttempted,
            totalSucceeded,
            totalFailed,
            summary.TotalRecordsWritten,
            summary.OutputFilePath ?? "(none)",
            summary.TotalDuration.TotalSeconds);

        if (summary.HasErrors)
        {
            LogErrorsHeader(_logger);

            foreach (var error in summary.AllErrors)
            {
                LogErrorDetail(_logger,
                    error.GetType().Name,
                    error.LocationName ?? "Unknown",
                    error.Message);
            }

            LogErrorsFooter(_logger);
        }
    }

    // --- LoggerMessage source-generated delegates (CA1848/CA1873 compliant) ---

    [LoggerMessage(Level = LogLevel.Information, Message = "SyncMetrics Weather Pipeline starting...")]
    private static partial void LogPipelineStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing source {Source} with {Count} location(s)...")]
    private static partial void LogProcessingSource(ILogger logger, string source, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Output write failed: {Error}")]
    private static partial void LogOutputWriteFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No records to write — all sources returned empty or failed.")]
    private static partial void LogNoRecords(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = """

        ═══════════════════════════════════════════════════════════
          SyncMetrics Weather Pipeline — Run Summary
        ═══════════════════════════════════════════════════════════
          Sources processed:    {SourceCount} ({SourceNames})
          Locations attempted:  {Attempted}
          Locations succeeded:  {Succeeded}
          Locations failed:     {Failed}
          Records written:      {Records}
          Output file:          {OutputFile}
          Duration:             {Duration:F2}s
        ═══════════════════════════════════════════════════════════
        """)]
    private static partial void LogRunSummary(ILogger logger,
        int sourceCount, string sourceNames, int attempted, int succeeded,
        int failed, int records, string outputFile, double duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "  ERRORS:\n  ─────────────────────────────────────────────────────────")]
    private static partial void LogErrorsHeader(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "  [{ErrorType}] {Location} — {ErrorMessage}")]
    private static partial void LogErrorDetail(ILogger logger, string errorType, string location, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "  ─────────────────────────────────────────────────────────")]
    private static partial void LogErrorsFooter(ILogger logger);
}
