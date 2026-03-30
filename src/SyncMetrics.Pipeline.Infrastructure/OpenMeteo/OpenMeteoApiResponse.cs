using System.Text.Json.Serialization;

namespace SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

/// <summary>
/// Deserialization model mirroring the exact JSON shape returned by Open-Meteo's /v1/forecast endpoint.
/// This is a DTO — NOT a domain model. It exists solely to deserialize the API's parallel-array structure.
/// Uses System.Text.Json attributes (zero dependency on Newtonsoft).
/// </summary>
public class OpenMeteoApiResponse
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("daily")]
    public OpenMeteoDailyData? Daily { get; set; }

    [JsonPropertyName("daily_units")]
    public Dictionary<string, string>? DailyUnits { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// The "daily" object: parallel arrays where index i across all arrays represents day i.
/// All list properties are List&lt;double?&gt; because the API can return null for individual measurements.
/// All collection properties are nullable because the entire array might be absent.
/// </summary>
public class OpenMeteoDailyData
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; set; }

    [JsonPropertyName("temperature_2m_max")]
    public List<double?>? TemperatureMax { get; set; }

    [JsonPropertyName("temperature_2m_min")]
    public List<double?>? TemperatureMin { get; set; }

    [JsonPropertyName("precipitation_sum")]
    public List<double?>? PrecipitationSum { get; set; }

    /// <summary>
    /// CRITICAL CATCH: The exercise URL uses "windspeed_10m_max" but the actual API parameter
    /// is "wind_speed_10m_max" (with underscore). Validated against live API docs.
    /// </summary>
    [JsonPropertyName("wind_speed_10m_max")]
    public List<double?>? WindSpeedMax { get; set; }

    [JsonPropertyName("uv_index_max")]
    public List<double?>? UvIndexMax { get; set; }
}
