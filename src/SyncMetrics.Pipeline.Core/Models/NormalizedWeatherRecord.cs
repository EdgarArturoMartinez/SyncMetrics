namespace SyncMetrics.Pipeline.Core.Models;

/// <summary>
/// Unified output row — the normalized schema that every data source transforms into.
/// Nullable doubles because real weather data has gaps (a missing reading is NOT 0.0°C).
/// </summary>
public record NormalizedWeatherRecord
{
    public required string Source { get; init; }
    public required string Location { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required DateOnly Date { get; init; }
    public double? TempMaxCelsius { get; init; }
    public double? TempMinCelsius { get; init; }
    public double? PrecipitationMm { get; init; }
    public double? WindSpeedMaxKmh { get; init; }
    public double? UvIndexMax { get; init; }
    public required DateTime FetchedAtUtc { get; init; }
}
