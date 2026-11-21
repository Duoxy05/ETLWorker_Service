using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Enums;
using ETLWorker.Infrastructure.Extractors;
using ETLWorker.Infrastructure.Staging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETLWorker.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var etl = configuration.GetSection(EtlOptions.SectionName).Get<EtlOptions>() ?? new EtlOptions();

        // Un HttpClient nombrado por cada fuente API (IHttpClientFactory: reutiliza conexiones, evita agotar sockets).
        foreach (var src in etl.Sources.Where(s => s.Enabled && s.Type == SourceType.Api && s.Api is not null))
        {
            var api = src.Api!;
            if (!Uri.TryCreate(api.BaseUrl.EndsWith('/') ? api.BaseUrl : api.BaseUrl + "/", UriKind.Absolute, out var baseUri))
                throw new InvalidOperationException($"Etl:Sources '{src.Name}': BaseUrl inválida.");

            string? apiKey = null;
            if (!string.IsNullOrWhiteSpace(api.ApiKeySecret))
            {
                apiKey = configuration[$"Secrets:{api.ApiKeySecret}"];
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException($"Falta el secreto Secrets:{api.ApiKeySecret} (user-secrets o variable de entorno Secrets__{api.ApiKeySecret}).");
            }

            services.AddHttpClient(src.Name, client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(api.TimeoutSeconds);
                if (apiKey is not null) client.DefaultRequestHeaders.Add(api.ApiKeyHeader, apiKey);
            });
        }

        services.AddSingleton<IExtractor, CsvExtractor>();
        services.AddSingleton<IExtractor, DatabaseExtractor>();
        services.AddSingleton<IExtractor, ApiExtractor>();
        services.AddSingleton<IStagingRepository, MySqlStagingRepository>();
        services.AddSingleton<IReferenceCatalogProvider, CsvReferenceCatalogProvider>();
        return services;
    }
}
