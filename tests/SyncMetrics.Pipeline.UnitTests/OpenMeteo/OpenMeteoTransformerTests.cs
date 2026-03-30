using System.Globalization;
using FluentAssertions;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

namespace SyncMetrics.Pipeline.UnitTests.OpenMeteo;

/// <summary>
/// Unit tests for <see cref="OpenMeteoTransformer"/>.
///
/// SOLID demonstrated:
///   SRP  — Transformer has one job: OpenMeteoApiResponse → NormalizedWeatherRecord[].
///          It never touches HTTP or file I/O. Its inputs and outputs are pure values.
///   DIP  — Tests depend on IDataTransformer&lt;T&gt; contract, not on any HTTP plumbing.
///   LSP  — OpenMeteoTransformer fully substitutes IDataTransformer&lt;OpenMeteoApiResponse&gt;;
///          callers never need to know the concrete type.
///
/// Design patterns:
///   Object Mother — BuildValidResponse() centralises test-data creation.
///                   One helper, many tests. When the model changes, one edit fixes all.
///   AAA            — Every test: Arrange (build response) → Act (Transform) → Assert.
///
/// Good practices:
///   Null-safe fields  — The API can omit a day's reading; tests prove null propagates
///                       as null (not 0.0) so downstream consumers can distinguish
///                       "no data" from "zero measurement".
///   Fail-fast on date — An unparseable date string fails the whole location, not silently
///                       skips the day. Tests verify the error carries RecordIndex for
///                       actionable diagnostics.
/// </summary>
public sealed class OpenMeteoTransformerTests
{
    private readonly OpenMeteoTransformer _sut = new();

    private static readonly LocationConfig NewYork = new()
    {
        Name = "New York",
        Latitude = 40.7128,
        Longitude = -74.006,
    };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Transform_ValidResponse_ReturnsOneRecordPerDay()
    {
        var response = BuildValidResponse(days: 7);

        var result = _sut.Transform(response, NewYork);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(7);
    }

    [Fact]
    public void Transform_ValidResponse_SetsSourceNameToOpenMeteo()
    {
        var response = BuildValidResponse(days: 1);

        var result = _sut.Transform(response, NewYork);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Source.Should().Be("OpenMeteo");
    }

    [Fact]
    public void Transform_ValidResponse_SetsLocationFromConfig()
    {
        var response = BuildValidResponse(days: 1);

        var result = _sut.Transform(response, NewYork);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Location.Should().Be("New York");
    }

    [Fact]
    public void Transform_ValidResponse_UsesGridSnappedCoordinatesFromResponse()
    {
        // The API returns grid-snapped coordinates that differ slightly from the request.
        // We store the response coords (what data was actually modelled for),
        // not the caller's requested coords — critical for data accuracy.
        var response = BuildValidResponse(days: 1);
        response.Latitude = 40.710335;   // grid-snapped value
        response.Longitude = -74.00594;

        var result = _sut.Transform(response, NewYork);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Latitude.Should().BeApproximately(40.710335, precision: 0.00001);
        result.Value[0].Longitude.Should().BeApproximately(-74.00594, precision: 0.00001);
    }

    [Fact]
    public void Transform_NullTemperatureMax_PreservesNullInRecord()
    {
        // Null in a daily array means the sensor had no reading — not zero.
        // Preserving null prevents downstream analytics from treating it as 0°C.
        var response = BuildValidResponse(days: 1);
        response.Daily!.TemperatureMax = new List<double?> { null };

        var result = _sut.Transform(response, NewYork);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].TempMaxCelsius.Should().BeNull();
    }

    // ── Error path ────────────────────────────────────────────────────────────

    [Fact]
    public void Transform_UnparsableDateString_ReturnsTransformErrorWithRecordIndex()
    {
        var response = BuildValidResponse(days: 2);
        response.Daily!.Time = new List<string> { "2026-03-29", "not-a-date" };

        var result = _sut.Transform(response, NewYork);

        result.IsFailure.Should().BeTrue();
        var transformError = result.Error.Should().BeOfType<TransformError>().Subject;
        transformError.RecordIndex.Should().Be(1);
        transformError.Field.Should().Be("time");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Object Mother: builds a structurally-valid Open-Meteo response for the
    /// requested number of days, using simple deterministic values.
    /// </summary>
    private static OpenMeteoApiResponse BuildValidResponse(int days) =>
        new()
        {
            Latitude = 40.710335,
            Longitude = -74.00594,
            Daily = new OpenMeteoDailyData
            {
                Time = Enumerable.Range(0, days)
                    .Select(i => new DateOnly(2026, 3, 29).AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .ToList(),
                TemperatureMax = Enumerable.Range(0, days)
                    .Select(i => (double?)(10.0 + i))
                    .ToList(),
                TemperatureMin = Enumerable.Range(0, days)
                    .Select(i => (double?)(5.0 + i))
                    .ToList(),
                PrecipitationSum = Enumerable.Range(0, days)
                    .Select(i => (double?)(i * 0.5))
                    .ToList(),
                WindSpeedMax = Enumerable.Range(0, days)
                    .Select(i => (double?)(15.0 + i))
                    .ToList(),
                UvIndexMax = Enumerable.Range(0, days)
                    .Select(i => (double?)(2.0 + i * 0.5))
                    .ToList(),
            },
        };
}
