namespace SyncMetrics.Pipeline.Core.Models;

/// <summary>
/// Per-source execution result — aggregates records and errors from a single data source run.
/// </summary>
public record ProcessingResult
{
    public required string SourceName { get; init; }
    public required IReadOnlyList<NormalizedWeatherRecord> Records { get; init; }
    public required IReadOnlyList<PipelineError> Errors { get; init; }
    public required TimeSpan Duration { get; init; }
    public int LocationsAttempted { get; init; }
    public int LocationsSucceeded { get; init; }
}
