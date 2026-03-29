using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SyncMetrics.Pipeline.Application;
using SyncMetrics.Pipeline.Core.Models;
using SyncMetrics.Pipeline.Infrastructure.Configuration;

// Pin content root to the assembly's directory so appsettings.json is found regardless
// of the working directory (solution root when running with `dotnet run --project`).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
    Args = args,
});

// Wire all pipeline services — config binding, HTTP clients, DI registrations
builder.Services.AddPipelineServices(builder.Configuration);

var app = builder.Build();

// Resolve coordinator and config, build source→locations map, execute pipeline
var coordinator = app.Services.GetRequiredService<PipelineCoordinator>();
var options = app.Services.GetRequiredService<IOptions<PipelineOptions>>().Value;

var sourceLocations = options.Sources
    .Where(s => s.Enabled)
    .ToDictionary(
        s => s.Name,
        s => (IReadOnlyList<LocationConfig>)s.Locations);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var summary = await coordinator.RunAsync(sourceLocations, cts.Token);

// Exit code: 0 if no errors, 1 if any source had failures
return summary.HasErrors ? 1 : 0;
