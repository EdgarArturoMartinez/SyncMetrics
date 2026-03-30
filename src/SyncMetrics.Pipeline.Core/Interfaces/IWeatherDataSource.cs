using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Core.Interfaces;

/// <summary>
/// The Strategy pattern composite — each data source wires its own client → parser → transformer
/// internally. The pipeline coordinator only sees this interface and calls ProcessAsync.
/// Adding a new source = implement this interface + register in DI.
/// </summary>
public interface IWeatherDataSource
{
    string SourceName { get; }
    Task<ProcessingResult> ProcessAsync(IEnumerable<LocationConfig> locations, CancellationToken cancellationToken);
}
