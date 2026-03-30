namespace SyncMetrics.Pipeline.Core;

/// <summary>
/// Base error type for all pipeline failures. Each subtype carries contextual information
/// specific to the stage that failed, enabling actionable processing summaries.
/// </summary>
public abstract record PipelineError(string Message, string? LocationName = null);

/// <summary>
/// HTTP fetch failed — carries status code and URL for diagnostics.
/// </summary>
public record FetchError(
    string Message,
    string? LocationName = null,
    int? StatusCode = null,
    string? Url = null) : PipelineError(Message, LocationName);

/// <summary>
/// JSON deserialization or structural validation failed — carries the offending field and raw content snippet.
/// </summary>
public record ParseError(
    string Message,
    string? LocationName = null,
    string? Field = null,
    string? RawContent = null) : PipelineError(Message, LocationName);

/// <summary>
/// Source-to-normalized transformation failed — carries field name and record index.
/// </summary>
public record TransformError(
    string Message,
    string? LocationName = null,
    string? Field = null,
    int? RecordIndex = null) : PipelineError(Message, LocationName);

/// <summary>
/// Output writing failed — carries the target file path.
/// </summary>
public record OutputError(
    string Message,
    string? FilePath = null) : PipelineError(Message);
