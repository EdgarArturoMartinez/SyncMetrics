using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SyncMetrics.Pipeline.Application;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Infrastructure.OpenMeteo;
using SyncMetrics.Pipeline.Infrastructure.Output;

namespace SyncMetrics.Pipeline.Infrastructure.Configuration;

/// <summary>
/// Single entry point for all pipeline DI registrations.
/// Console's Program.cs calls builder.Services.AddPipelineServices(builder.Configuration).
/// Adding a new data source = add 4 lines here. Zero changes anywhere else.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddPipelineServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind strongly-typed configuration from appsettings.json
        services.Configure<PipelineOptions>(
            configuration.GetSection(PipelineOptions.SectionName));

        // Validate configuration at startup — catches bad coordinates, empty names, invalid paths
        services.AddSingleton<IValidateOptions<PipelineOptions>, PipelineOptionsValidator>();
        services.AddOptionsWithValidateOnStart<PipelineOptions>();

        // Phase 4 — HTTP client with resilience pipeline
        services.AddHttpClient("OpenMeteo", client =>
        {
            client.BaseAddress = new Uri(
                configuration.GetValue<string>("Pipeline:Sources:0:BaseUrl")
                ?? "https://api.open-meteo.com/v1/forecast");
            client.Timeout = TimeSpan.FromSeconds(
                configuration.GetValue<int>("Pipeline:Sources:0:TimeoutSeconds", 30));
        }).AddStandardResilienceHandler();

        // Phase 4 — Open-Meteo data source registrations
        services.AddSingleton<IWeatherApiClient, OpenMeteoApiClient>();
        services.AddSingleton<IResponseParser<OpenMeteoApiResponse>, OpenMeteoResponseParser>();
        services.AddSingleton<IDataTransformer<OpenMeteoApiResponse>, OpenMeteoTransformer>();
        services.AddSingleton<IWeatherDataSource, OpenMeteoDataSource>();

        // Phase 5 — Output writer
        services.AddSingleton<IOutputWriter, TabDelimitedFileWriter>();

        // Phase 6 — Pipeline coordinator
        services.AddSingleton<PipelineCoordinator>();

        return services;
    }
}
