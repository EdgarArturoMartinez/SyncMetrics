using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Core.Interfaces;

/// <summary>
/// Transforms a source-specific parsed model into normalized weather records.
/// Takes LocationConfig because the raw API response may not include the location name.
/// </summary>
public interface IDataTransformer<TRaw>
{
    Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(TRaw rawData, LocationConfig location);
}
