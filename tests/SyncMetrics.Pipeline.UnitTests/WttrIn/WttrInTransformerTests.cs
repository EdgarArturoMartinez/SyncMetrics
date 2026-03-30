using System.Globalization;
using FluentAssertions;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.WttrIn;

namespace SyncMetrics.Pipeline.UnitTests.WttrIn;

public class WttrInTransformerTests
{
    private readonly WttrInTransformer _transformer = new();

    private static readonly LocationConfig TestLocation = new()
    {
        Name = "Chicago",
        Latitude = 41.8781,
        Longitude = -87.6298,
    };

    [Fact]
    public void Transform_ValidResponse_RecordCountMatchesDayCount()
    {
        var response = BuildValidResponse(3);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    [Fact]
    public void Transform_ValidResponse_SourceNameIsWttrIn()
    {
        var response = BuildValidResponse(1);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Source.Should().Be("WttrIn");
    }

    [Fact]
    public void Transform_ValidResponse_LocationPopulatedFromConfig()
    {
        var response = BuildValidResponse(1);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Location.Should().Be("Chicago");
    }

    [Fact]
    public void Transform_ValidResponse_CoordinatesFromNearestArea()
    {
        var response = BuildValidResponse(1);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Latitude.Should().Be(41.894);
        result.Value[0].Longitude.Should().Be(-87.626);
    }

    [Fact]
    public void Transform_HourlyPrecipitation_SumsToDaily()
    {
        var response = BuildResponseWithHourly(
            precipValues: ["1.5", "2.5", "0.0"],
            windValues: ["10", "20", "15"]);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].PrecipitationMm.Should().Be(4.0);
    }

    [Fact]
    public void Transform_HourlyWindSpeed_TakesMaximum()
    {
        var response = BuildResponseWithHourly(
            precipValues: ["0.0", "0.0"],
            windValues: ["10", "29", "15"]);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].WindSpeedMaxKmh.Should().Be(29.0);
    }

    [Fact]
    public void Transform_NullHourlyValues_ReturnsNullMeasurements()
    {
        var response = BuildResponseWithHourly(
            precipValues: [null, null],
            windValues: [null, null]);

        var result = _transformer.Transform(response, TestLocation);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].PrecipitationMm.Should().BeNull();
        result.Value[0].WindSpeedMaxKmh.Should().BeNull();
    }

    [Fact]
    public void Transform_UnparseableDate_ReturnsTransformError()
    {
        var response = BuildValidResponse(1);
        response.Weather![0].Date = "not-a-date";

        var result = _transformer.Transform(response, TestLocation);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TransformError>();
        result.Error.Message.Should().Contain("not-a-date");
    }

    [Fact]
    public void Transform_UnparseableCoordinates_ReturnsTransformError()
    {
        var response = BuildValidResponse(1);
        response.NearestArea![0].Latitude = "not-a-number";

        var result = _transformer.Transform(response, TestLocation);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TransformError>();
        result.Error.Message.Should().Contain("coordinates");
    }

    private static WttrInApiResponse BuildValidResponse(int days)
    {
        var weather = Enumerable.Range(0, days).Select(i => new WttrInWeatherDay
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(i)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            MaxTempC = (20 + i).ToString(CultureInfo.InvariantCulture),
            MinTempC = (5 + i).ToString(CultureInfo.InvariantCulture),
            UvIndex = (3 + i).ToString(CultureInfo.InvariantCulture),
            Hourly =
            [
                new WttrInHourlyData { PrecipMm = "0.0", WindSpeedKmph = "18" },
                new WttrInHourlyData { PrecipMm = "0.5", WindSpeedKmph = "22" },
            ],
        }).ToList();

        return new WttrInApiResponse
        {
            NearestArea =
            [
                new WttrInNearestArea { Latitude = "41.894", Longitude = "-87.626" },
            ],
            Weather = weather,
        };
    }

    private static WttrInApiResponse BuildResponseWithHourly(string?[] precipValues, string?[] windValues)
    {
        var hourly = new List<WttrInHourlyData>();
        var count = Math.Max(precipValues.Length, windValues.Length);
        for (var i = 0; i < count; i++)
        {
            hourly.Add(new WttrInHourlyData
            {
                PrecipMm = i < precipValues.Length ? precipValues[i] : null,
                WindSpeedKmph = i < windValues.Length ? windValues[i] : null,
            });
        }

        return new WttrInApiResponse
        {
            NearestArea =
            [
                new WttrInNearestArea { Latitude = "41.894", Longitude = "-87.626" },
            ],
            Weather =
            [
                new WttrInWeatherDay
                {
                    Date = "2026-03-30",
                    MaxTempC = "23",
                    MinTempC = "7",
                    UvIndex = "6",
                    Hourly = hourly,
                },
            ],
        };
    }
}
