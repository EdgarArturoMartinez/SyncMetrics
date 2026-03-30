using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

/// <summary>
/// Transforms validated OpenMeteoApiResponse into NormalizedWeatherRecord[].
/// The core operation is "zipping" the parallel arrays by index — each index i across
/// all arrays represents one day's forecast data.
/// </summary>
public sealed class OpenMeteoTransformer : IDataTransformer<OpenMeteoApiResponse>
{
    public Result<IReadOnlyList<NormalizedWeatherRecord>> Transform(
        OpenMeteoApiResponse rawData, LocationConfig location)
    {
        var daily = rawData.Daily!; // Parser guarantees non-null
        var records = new NormalizedWeatherRecord[daily.Time!.Count];
        var fetchedAt = DateTime.UtcNow;

        for (var i = 0; i < daily.Time.Count; i++)
        {
            if (!DateOnly.TryParse(daily.Time[i], out var date))
            {
                return Result<IReadOnlyList<NormalizedWeatherRecord>>.Failure(new TransformError(
                    $"Cannot parse date '{daily.Time[i]}' at index {i} for {location.Name}.",
                    LocationName: location.Name,
                    Field: "time",
                    RecordIndex: i));
            }

            records[i] = new NormalizedWeatherRecord
            {
                Source = "OpenMeteo",
                Location = location.Name,
                Latitude = rawData.Latitude,
                Longitude = rawData.Longitude,
                Date = date,
                TempMaxCelsius = daily.TemperatureMax![i],
                TempMinCelsius = daily.TemperatureMin![i],
                PrecipitationMm = daily.PrecipitationSum![i],
                WindSpeedMaxKmh = daily.WindSpeedMax![i],
                UvIndexMax = daily.UvIndexMax![i],
                FetchedAtUtc = fetchedAt,
            };
        }

        return Result<IReadOnlyList<NormalizedWeatherRecord>>.Success(records);
    }
}
