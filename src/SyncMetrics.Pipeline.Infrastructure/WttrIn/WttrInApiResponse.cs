using System.Text.Json.Serialization;

namespace SyncMetrics.Pipeline.Infrastructure.WttrIn;

/// <summary>
/// Deserialization DTOs for the wttr.in JSON response.
/// Structure is completely different from Open-Meteo:
///   - Open-Meteo uses parallel arrays (time[], temperature_2m_max[], ...)
///   - wttr.in uses nested objects per day, with hourly sub-arrays
///   - wttr.in returns string values for numbers, not native doubles
/// This demonstrates that the IResponseParser/IDataTransformer abstraction
/// handles fundamentally different JSON shapes converging to the same NormalizedWeatherRecord.
/// </summary>
public sealed class WttrInApiResponse
{
    [JsonPropertyName("weather")]
    public List<WttrInWeatherDay>? Weather { get; set; }

    [JsonPropertyName("nearest_area")]
    public List<WttrInNearestArea>? NearestArea { get; set; }
}

public sealed class WttrInWeatherDay
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("maxtempC")]
    public string? MaxTempC { get; set; }

    [JsonPropertyName("mintempC")]
    public string? MinTempC { get; set; }

    [JsonPropertyName("uvIndex")]
    public string? UvIndex { get; set; }

    [JsonPropertyName("hourly")]
    public List<WttrInHourlyData>? Hourly { get; set; }
}

public sealed class WttrInHourlyData
{
    [JsonPropertyName("precipMM")]
    public string? PrecipMm { get; set; }

    [JsonPropertyName("windspeedKmph")]
    public string? WindSpeedKmph { get; set; }
}

public sealed class WttrInNearestArea
{
    [JsonPropertyName("latitude")]
    public string? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public string? Longitude { get; set; }
}
