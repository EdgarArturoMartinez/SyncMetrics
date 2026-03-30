namespace SyncMetrics.Pipeline.Core.Models;

/// <summary>
/// A configured location to fetch weather data for. Bound from appsettings.json.
/// Uses mutable set-properties so IConfiguration.Bind() can populate via reflection
/// after Activator.CreateInstance() — required+init blocks the default binder.
/// </summary>
public record LocationConfig
{
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
