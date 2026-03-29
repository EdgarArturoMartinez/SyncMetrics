namespace SyncMetrics.Pipeline.Core.Models;

/// <summary>
/// A configured location to fetch weather data for. Bound from appsettings.json.
/// </summary>
public record LocationConfig
{
    public required string Name { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}
