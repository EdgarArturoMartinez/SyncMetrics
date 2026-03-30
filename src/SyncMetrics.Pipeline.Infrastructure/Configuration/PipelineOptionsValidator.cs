using Microsoft.Extensions.Options;

namespace SyncMetrics.Pipeline.Infrastructure.Configuration;

/// <summary>
/// Validates PipelineOptions at startup — catches invalid config before the pipeline runs.
/// Prevents silent failures from bad coordinates, empty names, or invalid paths.
/// </summary>
public sealed class PipelineOptionsValidator : IValidateOptions<PipelineOptions>
{
    public ValidateOptionsResult Validate(string? name, PipelineOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            failures.Add("Pipeline:OutputDirectory must not be empty.");

        if (string.IsNullOrWhiteSpace(options.OutputFilePattern))
            failures.Add("Pipeline:OutputFilePattern must not be empty.");

        if (Path.IsPathRooted(options.OutputFilePattern))
            failures.Add("Pipeline:OutputFilePattern must be a relative filename, not an absolute path.");

        foreach (var source in options.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Name))
                failures.Add("Each Pipeline:Sources entry must have a non-empty Name.");

            if (string.IsNullOrWhiteSpace(source.BaseUrl))
                failures.Add($"Source '{source.Name}': BaseUrl must not be empty.");

            if (source.TimeoutSeconds <= 0)
                failures.Add($"Source '{source.Name}': TimeoutSeconds must be positive.");

            if (source.RetryCount < 0)
                failures.Add($"Source '{source.Name}': RetryCount must not be negative.");

            foreach (var loc in source.Locations)
            {
                if (string.IsNullOrWhiteSpace(loc.Name))
                    failures.Add($"Source '{source.Name}': each location must have a non-empty Name.");

                if (loc.Latitude is < -90 or > 90)
                    failures.Add($"Source '{source.Name}', Location '{loc.Name}': Latitude {loc.Latitude} is outside ±90°.");

                if (loc.Longitude is < -180 or > 180)
                    failures.Add($"Source '{source.Name}', Location '{loc.Name}': Longitude {loc.Longitude} is outside ±180°.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
