using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyncMetrics.Pipeline.Application;
using SyncMetrics.Pipeline.Core.Interfaces;
using SyncMetrics.Pipeline.Infrastructure.OpenMeteo;
using SyncMetrics.Pipeline.Infrastructure.Output;
using SyncMetrics.Pipeline.Infrastructure.WttrIn;

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

        // wttr.in data source — free, no API key, completely different JSON shape.
        // WttrInApiClient is registered as concrete type (not IWeatherApiClient) to avoid
        // DI collision. With 3+ sources, keyed services would be the next evolution.
        services.AddHttpClient("WttrIn", client =>
        {
            client.BaseAddress = new Uri(
                configuration.GetValue<string>("Pipeline:Sources:1:BaseUrl")
                ?? "https://wttr.in/");
            client.Timeout = TimeSpan.FromSeconds(
                configuration.GetValue<int>("Pipeline:Sources:1:TimeoutSeconds", 30));
        }).AddStandardResilienceHandler();

        services.AddSingleton<WttrInApiClient>();
        services.AddSingleton<IResponseParser<WttrInApiResponse>, WttrInResponseParser>();
        services.AddSingleton<IDataTransformer<WttrInApiResponse>, WttrInTransformer>();
        services.AddSingleton<IWeatherDataSource, WttrInDataSource>();

        // Phase 5 — Output writer
        services.AddSingleton<IOutputWriter, TabDelimitedFileWriter>();

        // Phase 6 — Pipeline coordinator
        services.AddSingleton<PipelineCoordinator>();

        return services;
    }
}
