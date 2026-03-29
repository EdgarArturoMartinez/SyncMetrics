using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // TODO: Phase 4 — HTTP client with resilience
        // services.AddHttpClient("OpenMeteo", client =>
        // {
        //     client.BaseAddress = new Uri(sourceOptions.BaseUrl);
        //     client.Timeout = TimeSpan.FromSeconds(sourceOptions.TimeoutSeconds);
        // }).AddStandardResilienceHandler();

        // TODO: Phase 7 — Open-Meteo feature registrations
        // services.AddSingleton<IWeatherApiClient, OpenMeteoApiClient>();
        // services.AddSingleton<IResponseParser<OpenMeteoApiResponse>, OpenMeteoResponseParser>();
        // services.AddSingleton<IDataTransformer<OpenMeteoApiResponse>, OpenMeteoTransformer>();
        // services.AddSingleton<IWeatherDataSource, OpenMeteoDataSource>();

        // TODO: Phase 8 — Output writer
        // services.AddSingleton<IOutputWriter, TabDelimitedFileWriter>();

        // TODO: Phase 9 — Pipeline coordinator
        // services.AddSingleton<PipelineCoordinator>();

        return services;
    }
}
