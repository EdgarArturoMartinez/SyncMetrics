using System.Globalization;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Infrastructure.WttrIn;

/// <summary>
/// Transforms validated WttrInApiResponse into NormalizedWeatherRecord[].
/// Key differences from OpenMeteoTransformer:
///   - wttr.in returns string values (not doubles) — requires parsing
///   - No daily precipitation/wind totals — derived from hourly sub-arrays
///   - Coordinates come from nearest_area[], not the root response
/// This demonstrates that IDataTransformer handles fundamentally different
/// transformation logic while producing the same NormalizedWeatherRecord schema.
/// </summary>
public sealed class WttrInTransformer : IDataTransformer<WttrInApiResponse>
{
    public Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(
        WttrInApiResponse rawData, LocationConfig location)
    {
        var area = rawData.NearestArea![0]; // Parser guarantees non-null and non-empty

        if (!double.TryParse(area.Latitude, CultureInfo.InvariantCulture, out var latitude) ||
            !double.TryParse(area.Longitude, CultureInfo.InvariantCulture, out var longitude))
        {
            return Result<IReadOnlyList<NormalizedWeatherRecord>>.Failure(new TransformError(
                $"Cannot parse coordinates from nearest_area for {location.Name}.",
                LocationName: location.Name,
                Field: "nearest_area"));
        }

        var records = new NormalizedWeatherRecord[rawData.Weather!.Count];
        var fetchedAt = DateTime.UtcNow;

        for (var i = 0; i < rawData.Weather.Count; i++)
        {
            var day = rawData.Weather[i];

            if (!DateOnly.TryParse(day.Date, out var date))
            {
                return Result<IReadOnlyList<NormalizedWeatherRecord>>.Failure(new TransformError(
                    $"Cannot parse date '{day.Date}' at index {i} for {location.Name}.",
                    LocationName: location.Name,
                    Field: "date",
                    RecordIndex: i));
            }

            records[i] = new NormalizedWeatherRecord
            {
                Source = "WttrIn",
                Location = location.Name,
                Latitude = latitude,
                Longitude = longitude,
                Date = date,
                TempMaxCelsius = ParseDouble(day.MaxTempC),
                TempMinCelsius = ParseDouble(day.MinTempC),
                PrecipitationMm = SumHourlyPrecipitation(day.Hourly!),
                WindSpeedMaxKmh = MaxHourlyWindSpeed(day.Hourly!),
                UvIndexMax = ParseDouble(day.UvIndex),
                FetchedAtUtc = fetchedAt,
            };
        }

        return Result<IReadOnlyList<NormalizedWeatherRecord>>.Success(records);
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>
    /// wttr.in provides precipitation per hour — sum all hourly values to get daily total.
    /// Open-Meteo provides this as a single daily field; here we derive it from hourly data.
    /// </summary>
    private static double? SumHourlyPrecipitation(List<WttrInHourlyData> hourly)
    {
        double sum = 0;
        var hasAnyValue = false;

        foreach (var h in hourly)
        {
            if (double.TryParse(h.PrecipMm, CultureInfo.InvariantCulture, out var precip))
            {
                sum += precip;
                hasAnyValue = true;
            }
        }

        return hasAnyValue ? sum : null;
    }

    /// <summary>
    /// wttr.in provides wind speed per hour — take the maximum to match Open-Meteo's daily max.
    /// </summary>
    private static double? MaxHourlyWindSpeed(List<WttrInHourlyData> hourly)
    {
        double max = double.MinValue;
        var hasAnyValue = false;

        foreach (var h in hourly)
        {
            if (double.TryParse(h.WindSpeedKmph, CultureInfo.InvariantCulture, out var speed))
            {
                if (speed > max) max = speed;
                hasAnyValue = true;
            }
        }

        return hasAnyValue ? max : null;
    }
}
