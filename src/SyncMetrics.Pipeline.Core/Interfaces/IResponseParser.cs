namespace SyncMetrics.Pipeline.Core.Interfaces;

/// <summary>
/// Deserializes raw JSON into a source-specific model.
/// Generic TRaw because each source has its own response shape.
/// </summary>
public interface IResponseParser<TRaw>
{
    Result<TRaw> Parse(string rawJson);
}
