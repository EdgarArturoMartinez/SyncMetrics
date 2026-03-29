using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Core.Interfaces;

/// <summary>
/// Writes normalized records to the final output destination.
/// Returns the output file path on success or an OutputError on failure.
/// </summary>
public interface IOutputWriter
{
    Task<Result<string>> WriteAsync(IReadOnlyList<NormalizedWeatherRecord> records, CancellationToken cancellationToken);
}
