using FluentAssertions;
using SyncMetrics.Pipeline.Core;
using SyncMetrics.Pipeline.Infrastructure.OpenMeteo;

namespace SyncMetrics.Pipeline.UnitTests.OpenMeteo;

/// <summary>
/// Unit tests for <see cref="OpenMeteoResponseParser"/>.
///
/// SOLID demonstrated:
///   SRP — Parser has exactly one responsibility: JSON string → Result&lt;OpenMeteoApiResponse&gt;.
///         Each test verifies a single failure path or the happy path independently.
///   OCP — Adding a new validation rule in the parser doesn't break existing tests.
///
/// Design patterns:
///   Arrange/Act/Assert (AAA) — every test follows the same three-phase structure.
///   Object Mother    — TestFixtures.LoadJson() returns pre-baked fixture objects.
///
/// Good practices:
///   Parse-don't-validate — the parser guarantees structural integrity so the transformer
///   can iterate arrays without null checks. Tests confirm this contract.
///   Result&lt;T&gt; as return type — no exceptions thrown across method boundaries.
/// </summary>
public sealed class OpenMeteoResponseParserTests
{
    private readonly OpenMeteoResponseParser _sut = new();

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidJson_ReturnsSuccess()
    {
        var json = TestFixtures.LoadJson("valid_response.json");

        var result = _sut.Parse(json);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Parse_ValidJson_PopulatesSevenDays()
    {
        var json = TestFixtures.LoadJson("valid_response.json");

        var result = _sut.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Daily!.Time!.Should().HaveCount(7);
    }

    [Fact]
    public void Parse_ValidJson_ReturnsCorrectCoordinates()
    {
        var json = TestFixtures.LoadJson("valid_response.json");

        var result = _sut.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Latitude.Should().BeApproximately(40.71, precision: 0.01);
        result.Value.Longitude.Should().BeApproximately(-74.0, precision: 0.01);
    }

    [Fact]
    public void Parse_NullValuesInsideArrays_ReturnsSuccess()
    {
        // Null elements inside a daily array represent a missing sensor reading for that day.
        // The parser must NOT reject these — they flow through as nullable doubles
        // and surface in the output file as empty tab fields rather than "0.0".
        var json = TestFixtures.LoadJson("malformed_null_values.json");

        var result = _sut.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Daily!.TemperatureMax![1].Should().BeNull();
    }

    // ── Error paths ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ApiErrorResponse_ReturnsParseErrorWithReason()
    {
        // Open-Meteo returns { "error": true, "reason": "..." } for bad query parameters.
        var json = TestFixtures.LoadJson("error_response.json");

        var result = _sut.Parse(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("Open-Meteo API error");
    }

    [Fact]
    public void Parse_MissingDailyObject_ReturnsParseErrorWithDailyField()
    {
        var json = TestFixtures.LoadJson("malformed_missing_daily.json");

        var result = _sut.Parse(json);

        result.IsFailure.Should().BeTrue();
        var parseError = result.Error.Should().BeOfType<ParseError>().Subject;
        parseError.Field.Should().Be("daily");
    }

    [Fact]
    public void Parse_MismatchedArrayLengths_ReturnsParseErrorNamingOffendingField()
    {
        // temperature_2m_max has 2 elements while time has 3 — the parser must
        // name the exact offending field so the processing summary is actionable.
        var json = TestFixtures.LoadJson("malformed_mismatched_arrays.json");

        var result = _sut.Parse(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("temperature_2m_max");
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsParseErrorWithInvalidJsonMessage()
    {
        const string badJson = "{ this is not valid json: {{{{";

        var result = _sut.Parse(badJson);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ParseError>();
        result.Error.Message.Should().Contain("Invalid JSON");
    }

    [Fact]
    public void Parse_ValidLondonResponse_ReturnsSuccessWithNegativeLongitude()
    {
        // London has a negative longitude (-0.1278) — verifies the parser handles
        // negative coordinate values correctly, not just positive US coordinates.
        var json = TestFixtures.LoadJson("valid_london_response.json");

        var result = _sut.Parse(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Latitude.Should().BeApproximately(51.507, precision: 0.01);
        result.Value.Longitude.Should().BeNegative("London is east of the prime meridian but at a small negative longitude");
    }

    [Fact]
    public void Parse_EmptyTimeArray_ReturnsParseError()
    {
        // An empty time array means there is literally no forecast data.
        // The parser must reject this — a Success with 0 records would
        // silently produce an empty output file with no indication of failure.
        var json = TestFixtures.LoadJson("malformed_empty_arrays.json");

        var result = _sut.Parse(json);

        result.IsFailure.Should().BeTrue();
        var parseError = result.Error.Should().BeOfType<ParseError>().Subject;
        parseError.Field.Should().Be("daily.time");
    }
}
