using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

namespace SyncMetrics.Pipeline.UnitTests.Http;

/// <summary>
/// Verifies that the "OpenMeteo" named HttpClient resilience pipeline —
/// configured via AddStandardResilienceHandler() in ServiceRegistration —
/// retries transient failures and does NOT retry permanent ones.
///
/// SOLID demonstrated:
///   OCP — The retry policy is on the HttpClient pipeline, not inside OpenMeteoApiClient.
///          Adding a new source with a different resilience profile requires no change to
///          the existing client — just a new AddHttpClient registration.
///   DIP — OpenMeteoApiClient depends on IHttpClientFactory (abstraction), not on a concrete
///          HttpClient. This test swaps in a fake primary handler without touching the
///          production class.
///
/// Design patterns:
///   Test Seam    — MockHttpMessageHandler is the seam: everything inside the real resilience
///                  pipeline is exercised; only the network is replaced.
///   Queue-based stub — responses are pre-loaded into a Queue so the handler returns a
///                  deterministic sequence: 503 → 200, 400, 500 × 4, etc.
///
/// Good practices:
///   Zero-delay retry — production policy uses exponential backoff (1s base).
///   Tests override Delay = TimeSpan.Zero + BackoffType = Constant so the full
///   three-retry sequence completes in milliseconds, not seconds.
///   CallCount verification — asserting the number of HttpMessageHandler invocations proves
///   the retry policy fired (or didn't fire) the expected number of times, not just
///   that the final result was correct.
/// </summary>
public sealed class RetryPolicyTests
{
    private static readonly LocationConfig NewYork = new()
    {
        Name = "New York",
        Latitude = 40.7128,
        Longitude = -74.006,
    };

    // ── Retry fires on transient failures ─────────────────────────────────────

    [Fact]
    public async Task RetryPolicy_TransientFailureThenSuccess_ReturnsSuccessAfterRetry()
    {
        // Arrange — handler sequence: 503 (transient) → 200 (success on first retry)
        var mockHandler = new MockHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(MinimalValidJson()),
            },
        ]);

        await using var provider = BuildProvider(mockHandler);
        var apiClient = new OpenMeteoApiClient(
            provider.GetRequiredService<IHttpClientFactory>());

        // Act
        var result = await apiClient.FetchAsync(NewYork, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("the retry policy should recover from one transient 503");
        mockHandler.CallCount.Should().Be(2, "one initial request + one retry on the 503");
    }

    // ── Retry does NOT fire on permanent failures ──────────────────────────────

    [Fact]
    public async Task RetryPolicy_PermanentFailure_DoesNotRetry()
    {
        // 400 Bad Request is permanent — our URL is malformed.
        // Retrying the same malformed URL just sends the same bad request again:
        // it cannot self-heal without a code change, and retrying masks the bug.
        var mockHandler = new MockHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.BadRequest),
        ]);

        await using var provider = BuildProvider(mockHandler);
        var apiClient = new OpenMeteoApiClient(
            provider.GetRequiredService<IHttpClientFactory>());

        // Act
        var result = await apiClient.FetchAsync(NewYork, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<FetchError>();
        mockHandler.CallCount.Should().Be(1, "a 400 is permanent — the policy must not retry");
    }

    // ── Retry exhausts max attempts ────────────────────────────────────────────

    [Fact]
    public async Task RetryPolicy_ExhaustsMaxAttempts_ReturnsFetchErrorAfterFourRequests()
    {
        // All 4 requests return 500 — policy exhausts (1 original + 3 retries) and returns
        // the last failure as a FetchError. MaxRetryAttempts = 3 in the test configuration
        // below mirrors the production default in appsettings.json.
        var mockHandler = new MockHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
        ]);

        await using var provider = BuildProvider(mockHandler);
        var apiClient = new OpenMeteoApiClient(
            provider.GetRequiredService<IHttpClientFactory>());

        // Act
        var result = await apiClient.FetchAsync(NewYork, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue("all 4 attempts returned 500 — the policy gives up");
        result.Error.Should().BeOfType<FetchError>();
        mockHandler.CallCount.Should().Be(4, "1 original request + 3 retries = 4 total HTTP calls");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ServiceProvider with the "OpenMeteo" HttpClient wired to the
    /// given mock handler and configured with zero-delay retry so tests are fast.
    /// The retry count (3) and permanent-vs-transient classification mirror production.
    /// </summary>
    private static ServiceProvider BuildProvider(MockHttpMessageHandler mockHandler)
    {
        var services = new ServiceCollection();

        services
            .AddHttpClient("OpenMeteo", client =>
            {
                client.BaseAddress = new Uri("https://api.open-meteo.com/v1/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => mockHandler)
            .AddStandardResilienceHandler(options =>
            {
                // Mirror the production retry count
                options.Retry.MaxRetryAttempts = 3;

                // Zero base delay so the three-retry sequence completes in milliseconds.
                // Production uses Exponential with jitter (1s base) to avoid thundering herd.
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.BackoffType = DelayBackoffType.Constant;
                options.Retry.UseJitter = false;

                // Set circuit breaker threshold high so it never opens during these small tests.
                // Production default MinimumThroughput is 100 — we make it explicit here.
                options.CircuitBreaker.MinimumThroughput = 1000;
            });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Minimal structurally-valid Open-Meteo JSON for a single day.
    /// Used only to give FetchAsync a parseable body when testing the success path.
    /// </summary>
    private static string MinimalValidJson() => """
        {
          "latitude": 40.710335,
          "longitude": -74.00594,
          "timezone": "America/New_York",
          "daily": {
            "time": ["2026-03-29"],
            "temperature_2m_max": [12.5],
            "temperature_2m_min": [5.2],
            "precipitation_sum": [0.0],
            "wind_speed_10m_max": [18.5],
            "uv_index_max": [3.2]
          }
        }
        """;
}

/// <summary>
/// A deterministic HttpMessageHandler stub that serves responses from a pre-loaded queue.
/// Counts every SendAsync call so tests can assert how many HTTP requests were made.
///
/// Thread-safe call counting via Interlocked.Increment — required because
/// Task.WhenAll in OpenMeteoDataSource can call the handler from multiple threads.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;
    private int _callCount;

    public MockHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    /// <summary>Number of times SendAsync has been called.</summary>
    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);

        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"MockHttpMessageHandler has no more queued responses " +
                $"(already returned {_callCount - 1} response(s)). " +
                "Add more responses to the queue or reduce the expected call count.");

        return Task.FromResult(_responses.Dequeue());
    }
}
