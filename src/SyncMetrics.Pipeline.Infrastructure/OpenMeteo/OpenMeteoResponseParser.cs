using System.Text.Json;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;

namespace SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

/// <summary>
/// Deserializes raw JSON into OpenMeteoApiResponse and validates structural integrity.
/// Follows the "parse, don't validate" principle: if Parse returns Success, the response
/// is guaranteed structurally sound — the transformer can iterate arrays without null checks.
/// </summary>
public sealed class OpenMeteoResponseParser : IResponseParser<OpenMeteoApiResponse>
{
    public Result<OpenMeteoApiResponse> Parse(string rawJson)
    {
        OpenMeteoApiResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<OpenMeteoApiResponse>(rawJson);
        }
        catch (JsonException ex)
        {
            return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                $"Invalid JSON: {ex.Message}",
                RawContent: Truncate(rawJson)));
        }

        if (response is null)
        {
            return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                "Deserialization returned null.",
                RawContent: Truncate(rawJson)));
        }

        // Check for Open-Meteo error response: { "error": true, "reason": "..." }
        if (response.Error)
        {
            return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                $"Open-Meteo API error: {response.Reason ?? "Unknown reason"}",
                RawContent: Truncate(rawJson)));
        }

        if (response.Daily is null)
        {
            return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                "Response missing 'daily' object.",
                Field: "daily",
                RawContent: Truncate(rawJson)));
        }

        var daily = response.Daily;

        if (daily.Time is null || daily.Time.Count == 0)
        {
            return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                "Daily 'time' array is null or empty.",
                Field: "daily.time",
                RawContent: Truncate(rawJson)));
        }

        var expectedLength = daily.Time.Count;

        // Validate all measurement arrays exist and have matching length
        var arrayChecks = new (List<double?>? Array, string FieldName)[]
        {
            (daily.TemperatureMax, "temperature_2m_max"),
            (daily.TemperatureMin, "temperature_2m_min"),
            (daily.PrecipitationSum, "precipitation_sum"),
            (daily.WindSpeedMax, "wind_speed_10m_max"),
            (daily.UvIndexMax, "uv_index_max"),
        };

        foreach (var (array, fieldName) in arrayChecks)
        {
            if (array is null)
            {
                return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                    $"Daily '{fieldName}' array is null.",
                    Field: fieldName,
                    RawContent: Truncate(rawJson)));
            }

            if (array.Count != expectedLength)
            {
                return Result<OpenMeteoApiResponse>.Failure(new ParseError(
                    $"Daily '{fieldName}' has {array.Count} elements but 'time' has {expectedLength}.",
                    Field: fieldName,
                    RawContent: Truncate(rawJson)));
            }
        }

        return Result<OpenMeteoApiResponse>.Success(response);
    }

    private static string Truncate(string value, int maxLength = 200) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
}
