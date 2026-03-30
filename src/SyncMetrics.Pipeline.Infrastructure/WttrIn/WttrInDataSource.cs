using System.Diagnostics;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Infrastructure.WttrIn;

/// <summary>
/// Composite orchestrator for the wttr.in data source: wires client → parser → transformer.
/// Mirrors OpenMeteoDataSource's structure — concurrent location fetching with Task.WhenAll,
/// Result&lt;T&gt;.Bind chain, and aggregated ProcessingResult.
/// The PipelineCoordinator discovers this via IEnumerable&lt;IWeatherDataSource&gt; — zero coordinator changes.
/// </summary>
public sealed class WttrInDataSource : IWeatherDataSource
{
    private readonly WttrInApiClient _apiClient;
    private readonly IResponseParser<WttrInApiResponse> _parser;
    private readonly IDataTransformer<WttrInApiResponse> _transformer;

    public WttrInDataSource(
        WttrInApiClient apiClient,
        IResponseParser<WttrInApiResponse> parser,
        IDataTransformer<WttrInApiResponse> transformer)
    {
        _apiClient = apiClient;
        _parser = parser;
        _transformer = transformer;
    }

    public string SourceName => "WttrIn";

    public async Task<ProcessingResult> ProcessAsync(
        IEnumerable<LocationConfig> locations, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var locationList = locations.ToList();

        // Concurrent fetch — Task.WhenAll fires all HTTP requests simultaneously
        var tasks = locationList.Select(loc => ProcessLocationAsync(loc, cancellationToken));
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
