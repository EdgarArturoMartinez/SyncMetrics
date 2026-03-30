using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Core.Interfaces;

/// <summary>
/// HTTP abstraction per data source. Returns raw JSON string, not HTTP plumbing —
/// a source might not even use HTTP (file, queue, etc.).
/// </summary>
public interface IWeatherApiClient
{
    string SourceName { get; }
    Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken cancellationToken);
}
