using System.Collections.Concurrent;
using System.Diagnostics;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Application.Services;

/// <summary>Coordina una ejecución completa: carga catálogos y extrae todas las fuentes en paralelo.</summary>
public sealed class EtlOrchestrator(
    IEnumerable<IExtractor> extractors,
    IStagingRepository staging,
    IReferenceCatalogProvider catalogProvider,
    SourceExtractionPipeline pipeline,
    IOptions<EtlOptions> options,
    ILogger<EtlOrchestrator> logger)
{
    public async Task<IReadOnlyList<ExtractionSummary>> RunAsync(CancellationToken ct)
    {
        var cfg = options.Value;
        var loteId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        logger.LogInformation("=== Inicio de extracción. Lote {LoteId} ===", loteId);

        ReferenceCatalog catalog;
        try { catalog = await catalogProvider.LoadAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudieron cargar los catálogos; no se validará la existencia de IDs.");
            catalog = ReferenceCatalog.Empty;
        }

        var results = new ConcurrentBag<ExtractionSummary>();
        var enabled = cfg.Sources.Where(s => s.Enabled).ToList();

        await Parallel.ForEachAsync(enabled,
            new ParallelOptions { MaxDegreeOfParallelism = cfg.MaxDegreeOfParallelism, CancellationToken = ct },
            async (src, token) => results.Add(await ProcessAsync(src, loteId, catalog, token)));

        var list = results.OrderBy(r => r.Fuente).ToList();
        logger.LogInformation(
            "=== Fin. Lote {LoteId}: {Ok}/{Total} fuentes OK, {Filas} filas, {Ms} ms ===",
            loteId, list.Count(r => r.Estado == ExtractionStatus.Ok), list.Count, list.Sum(r => r.FilasLeidas), sw.ElapsedMilliseconds);
        return list;
    }

    private async Task<ExtractionSummary> ProcessAsync(SourceOptions src, Guid loteId, ReferenceCatalog catalog, CancellationToken ct)
    {
        ExtractionSummary summary;
        try
        {
            var extractor = extractors.FirstOrDefault(e => e.Type == src.Type)
                ?? throw new InvalidOperationException($"No hay extractor registrado para el tipo {src.Type}.");
            summary = await pipeline.RunAsync(extractor, src, loteId, catalog, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[{Fuente}] Error inesperado.", src.Name);
            summary = new ExtractionSummary(src.Name, src.Type, ExtractionStatus.Error, 0, 0, 0, 0, ex.Message);
        }

        try { await staging.SaveRunAsync(loteId, summary, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[{Fuente}] No se pudo guardar el resumen en etl_ejecucion.", src.Name);
        }
        return summary;
    }
}

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<SourceExtractionPipeline>();
        services.AddSingleton<EtlOrchestrator>();
        return services;
    }
}
