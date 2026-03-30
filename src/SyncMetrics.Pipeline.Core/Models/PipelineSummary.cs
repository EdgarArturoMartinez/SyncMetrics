namespace SyncMetrics.Pipeline.Core.Models;

/// <summary>
/// Full pipeline run output — aggregates results from all data sources.
/// The console summary and exit code are derived from this.
/// </summary>
public record PipelineSummary
{
    public required IReadOnlyList<ProcessingResult> SourceResults { get; init; }
    public required int TotalRecordsWritten { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required string? OutputFilePath { get; init; }

    public bool HasErrors => SourceResults.Any(r => r.Errors.Count > 0);
    public IEnumerable<PipelineError> AllErrors => SourceResults.SelectMany(r => r.Errors);
}
