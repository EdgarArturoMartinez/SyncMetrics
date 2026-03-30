using System.Globalization;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

/// <summary>
/// HTTP client for the Open-Meteo /v1/forecast endpoint.
/// Builds the query URL, makes the GET request, returns raw JSON wrapped in Result&lt;string&gt;.
/// Retry/resilience is NOT here — it's configured on the HttpClient pipeline via Microsoft.Extensions.Http.Resilience.
/// </summary>
public sealed class OpenMeteoApiClient : IWeatherApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly string[] DailyFields =
    [
        "temperature_2m_max",
        "temperature_2m_min",
        "precipitation_sum",
        "wind_speed_10m_max",
        "uv_index_max"
    ];

    public OpenMeteoApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string SourceName => "OpenMeteo";

    public async Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("OpenMeteo");

        var lat = location.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = location.Longitude.ToString(CultureInfo.InvariantCulture);
        var daily = string.Join(",", DailyFields);

        var url = $"?latitude={lat}&longitude={lon}&daily={daily}&timezone=auto&forecast_days=7";

        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Failure(new FetchError(
                    $"Open-Meteo API returned HTTP {(int)response.StatusCode} for {location.Name}.",
                    LocationName: location.Name,
                    StatusCode: (int)response.StatusCode,
                    Url: url));
            }

            return Result<string>.Success(json);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Respect caller cancellation — do not swallow
        }
        catch (TaskCanceledException ex)
        {
            // HTTP timeout — TaskCanceledException with inner TimeoutException.
            // The caller did NOT cancel, so this is a transient failure, not a signal to stop.
            return Result<string>.Failure(new FetchError(
                $"HTTP request timed out for {location.Name}: {ex.Message}",
                LocationName: location.Name,
                Url: url));
        }
        catch (HttpRequestException ex)
        {
            return Result<string>.Failure(new FetchError(
                $"HTTP request failed for {location.Name}: {ex.Message}",
                LocationName: location.Name,
                Url: url));
        }
    }
}
