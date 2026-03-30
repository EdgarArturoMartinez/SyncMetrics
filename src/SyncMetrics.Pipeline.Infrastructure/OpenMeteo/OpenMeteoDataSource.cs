using System.Diagnostics;
using Microsoft.Extensions.Options;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.Configuration;

namespace SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

/// <summary>
/// Composite orchestrator: wires client → parser → transformer for the Open-Meteo source.
/// Fetches all locations concurrently with Task.WhenAll, throttled by a SemaphoreSlim
/// to prevent overwhelming the API when many locations are configured.
/// Implements the Strategy pattern — the pipeline coordinator only sees IWeatherDataSource.
/// </summary>
public sealed class OpenMeteoDataSource : IWeatherDataSource
{
    private readonly IWeatherApiClient _apiClient;
    private readonly IResponseParser<OpenMeteoApiResponse> _parser;
    private readonly IDataTransformer<OpenMeteoApiResponse> _transformer;
    private readonly int _maxConcurrentRequests;

    public OpenMeteoDataSource(
        IWeatherApiClient apiClient,
        IResponseParser<OpenMeteoApiResponse> parser,
        IDataTransformer<OpenMeteoApiResponse> transformer,
        IOptions<PipelineOptions> options)
    {
        _apiClient = apiClient;
        _parser = parser;
        _transformer = transformer;

        var sourceConfig = options.Value.Sources
            .FirstOrDefault(s => s.Name == SourceName);
        _maxConcurrentRequests = sourceConfig?.MaxConcurrentRequests > 0
            ? sourceConfig.MaxConcurrentRequests
            : 10;
    }

    public string SourceName => "OpenMeteo";

    public async Task<ProcessingResult> ProcessAsync(
        IEnumerable<LocationConfig> locations, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var locationList = locations.ToList();

        // Concurrent fetch — throttled by SemaphoreSlim to avoid overwhelming the API
        using var semaphore = new SemaphoreSlim(_maxConcurrentRequests);
        var tasks = locationList.Select(async loc =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ProcessLocationAsync(loc, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var records = new List<NormalizedWeatherRecord>();
        var errors = new List<PipelineError>();
        var locationsSucceeded = 0;

        foreach (var result in results)
        {
            if (result.IsSuccess)
            {
                records.AddRange(result.Value);
                locationsSucceeded++;
            }
            else
            {
                errors.Add(result.Error);
            }
        }

        stopwatch.Stop();

        return new ProcessingResult
        {
            SourceName = SourceName,
            Records = records,
            Errors = errors,
            Duration = stopwatch.Elapsed,
            LocationsAttempted = locationList.Count,
            LocationsSucceeded = locationsSucceeded,
        };
    }

    /// <summary>
    /// Fetch → Parse → Transform chain using Result&lt;T&gt;.Bind.
    /// If Fetch fails, Parse never runs. If Parse fails, Transform never runs.
    /// </summary>
    private async Task<Result<IReadOnlyList<NormalizedWeatherRecord>>> ProcessLocationAsync(
        LocationConfig location, CancellationToken cancellationToken)
    {
        var fetchResult = await _apiClient.FetchAsync(location, cancellationToken).ConfigureAwait(false);

        return fetchResult
            .Bind(json => _parser.Parse(json))
            .Bind(response => _transformer.Transform(response, location));
    }
}
