using SyncMetrics.Pipeline.Core.Models;

namespace SyncMetrics.Pipeline.Infrastructure.Configuration;

/// <summary>
/// Top-level pipeline configuration bound from the "Pipeline" section of appsettings.json.
/// Uses the Options pattern (IOptions&lt;PipelineOptions&gt;) for strongly-typed config.
/// </summary>
public class PipelineOptions
{
    public const string SectionName = "Pipeline";

    public string OutputDirectory { get; set; } = "./output";
    public string OutputFilePattern { get; set; } = "weather_data_{timestamp}.tsv";
    public List<SourceOptions> Sources { get; set; } = new();
}

/// <summary>
/// Per-source configuration. Each source (OpenMeteo, future WeatherApi, etc.)
/// gets its own section with locations, field mappings, and resilience settings.
/// </summary>
public class SourceOptions
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public List<LocationConfig> Locations { get; set; } = new();
    public List<FieldMapping> FieldMappings { get; set; } = new();
}

/// <summary>
/// Maps a source-specific field name to the normalized output column.
/// This is the config-driven field mapping bonus feature.
/// </summary>
public class FieldMapping
{
    public string SourceField { get; set; } = string.Empty;
    public string OutputColumn { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}
