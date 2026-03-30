using System.Text.Json;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;

namespace SyncMetrics.Pipeline.Infrastructure.WttrIn;

/// <summary>
/// Deserializes raw JSON into WttrInApiResponse and validates structural integrity.
/// Follows the same "parse, don't validate" principle as OpenMeteoResponseParser:
/// if Parse returns Success, the transformer can safely iterate without null checks.
/// </summary>
public sealed class WttrInResponseParser : IResponseParser<WttrInApiResponse>
{
    public Result<WttrInApiResponse> Parse(string rawJson)
    {
        WttrInApiResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<WttrInApiResponse>(rawJson);
        }
        catch (JsonException ex)
        {
            return Result<WttrInApiResponse>.Failure(new ParseError(
                $"Invalid JSON: {ex.Message}",
                RawContent: Truncate(rawJson)));
        }

        if (response is null)
        {
            return Result<WttrInApiResponse>.Failure(new ParseError(
                "Deserialization returned null.",
                RawContent: Truncate(rawJson)));
        }

        if (response.Weather is null || response.Weather.Count == 0)
        {
            return Result<WttrInApiResponse>.Failure(new ParseError(
                "Response missing 'weather' array or it is empty.",
                Field: "weather",
                RawContent: Truncate(rawJson)));
        }

        if (response.NearestArea is null || response.NearestArea.Count == 0)
        {
            return Result<WttrInApiResponse>.Failure(new ParseError(
                "Response missing 'nearest_area' array or it is empty.",
                Field: "nearest_area",
                RawContent: Truncate(rawJson)));
        }

        // Validate each day has required fields
        foreach (var day in response.Weather)
        {
            if (string.IsNullOrEmpty(day.Date))
            {
                return Result<WttrInApiResponse>.Failure(new ParseError(
                    "Weather day missing 'date' field.",
                    Field: "weather[].date",
                    RawContent: Truncate(rawJson)));
            }

            if (day.Hourly is null || day.Hourly.Count == 0)
            {
                return Result<WttrInApiResponse>.Failure(new ParseError(
                    $"Weather day '{day.Date}' missing 'hourly' array.",
                    Field: "weather[].hourly",
                    RawContent: Truncate(rawJson)));
            }
        }

        return Result<WttrInApiResponse>.Success(response);
    }

    private static string Truncate(string value, int maxLength = 200) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
}
