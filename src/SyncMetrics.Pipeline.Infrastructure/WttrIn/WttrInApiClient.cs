using System.Globalization;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Infrastructure.WttrIn;

/// <summary>
/// HTTP client for the wttr.in weather API.
/// Endpoint: GET https://wttr.in/{lat},{lon}?format=j1
/// Free, no API key required, returns 3-day forecast in JSON.
/// Retry/resilience is configured on the HttpClient pipeline via AddStandardResilienceHandler().
/// </summary>
public sealed class WttrInApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WttrInApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public static string SourceName => "WttrIn";

    public async Task<Result<string>> FetchAsync(LocationConfig location, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("WttrIn");

        var lat = location.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = location.Longitude.ToString(CultureInfo.InvariantCulture);

        var url = $"{lat},{lon}?format=j1";

        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Failure(new FetchError(
                    $"wttr.in returned HTTP {(int)response.StatusCode} for {location.Name}.",
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
        catch (HttpRequestException ex)
        {
            return Result<string>.Failure(new FetchError(
                $"HTTP request failed for {location.Name}: {ex.Message}",
                LocationName: location.Name,
                Url: url));
        }
    }
}
