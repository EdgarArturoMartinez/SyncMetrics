using FluentAssertions;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Infrastructure.WttrIn;

namespace SyncMetrics.Pipeline.UnitTests.WttrIn;

public class WttrInResponseParserTests
{
    private readonly WttrInResponseParser _parser = new();

    [Fact]
    public void Parse_ValidResponse_ReturnsSuccessWithThreeDays()
    {
        var json = TestFixtures.LoadJson("WttrIn", "valid_response.json");

        var result = _parser.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Weather.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_ValidResponse_HasNearestAreaCoordinates()
    {
        var json = TestFixtures.LoadJson("WttrIn", "valid_response.json");

        var result = _parser.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.NearestArea![0].Latitude.Should().Be("41.894");
        result.Value.NearestArea![0].Longitude.Should().Be("-87.626");
    }

    [Fact]
    public void Parse_MissingWeatherArray_ReturnsParseError()
    {
        var json = TestFixtures.LoadJson("WttrIn", "missing_weather.json");

        var result = _parser.Parse(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("weather");
    }

    [Fact]
    public void Parse_MissingNearestArea_ReturnsParseError()
    {
        var json = TestFixtures.LoadJson("WttrIn", "missing_nearest_area.json");

        var result = _parser.Parse(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("nearest_area");
    }

    [Fact]
    public void Parse_EmptyHourlyArray_ReturnsParseError()
    {
        var json = TestFixtures.LoadJson("WttrIn", "empty_hourly.json");

        var result = _parser.Parse(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("hourly");
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsParseError()
    {
        var result = _parser.Parse("not valid json {{{");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("Invalid JSON");
    }

    [Fact]
    public void Parse_NullHourlyValues_ReturnsSuccess()
    {
        var json = TestFixtures.LoadJson("WttrIn", "null_values.json");

        var result = _parser.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Weather.Should().HaveCount(1);
    }
}
