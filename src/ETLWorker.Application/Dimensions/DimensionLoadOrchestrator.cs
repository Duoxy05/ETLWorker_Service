using System.Collections.Concurrent;
using System.Diagnostics;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Application.Dimensions;

/// <summary>
/// Ejecuta la carga de todas las dimensiones (en paralelo: no dependen entre sí) y deja una fila por
/// dimensión en la bitácora etl_carga_dimensiones. Si una dimensión falla, las demás continúan.
/// </summary>
public sealed class DimensionLoadOrchestrator(
    IEnumerable<IDimensionLoader> loaders,
    IDimensionRepository repository,
    IOptions<EtlOptions> options,
    ILogger<DimensionLoadOrchestrator> logger)
{
    public async Task<IReadOnlyList<DimensionLoadSummary>> RunAsync(CancellationToken ct)
    {
        var cfg = options.Value;
        var loteId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        logger.LogInformation("=== Inicio de carga de dimensiones. Lote {LoteId} ===", loteId);

        var results = new ConcurrentBag<DimensionLoadSummary>();
        await Parallel.ForEachAsync(loaders,
            new ParallelOptions { MaxDegreeOfParallelism = cfg.MaxDegreeOfParallelism, CancellationToken = ct },
            async (loader, token) => results.Add(await RunOneAsync(loader, loteId, token)));

        var list = results.OrderBy(r => r.Dimension).ToList();
        logger.LogInformation(
            "=== Fin de carga de dimensiones. Lote {LoteId}: {Ok}/{Total} OK, {Ins} insertadas, {Reg} ya registradas, {Ms} ms ===",
            loteId, list.Count(r => r.Estado == LoadStatus.Ok), list.Count,
            list.Sum(r => r.Insertadas), list.Sum(r => r.YaRegistradas), sw.ElapsedMilliseconds);
        return list;
    }

    private async Task<DimensionLoadSummary> RunOneAsync(IDimensionLoader loader, Guid loteId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        DimensionLoadSummary summary;
        try
        {
            logger.LogInformation("[{Dim}] Carga iniciada.", loader.Name);
            var r = await loader.LoadAsync(ct);
            sw.Stop();
            summary = new DimensionLoadSummary(loader.Name, LoadStatus.Ok, r.Read, r.Rejected,
                r.Load.Inserted, r.Load.AlreadyRegistered, r.Load.ReturnCode, sw.ElapsedMilliseconds, r.Load.Message);
            logger.LogInformation(
                "[{Dim}] OK (código {Codigo}): leídas={Leidas}, rechazadas={Rechazadas}, insertadas={Ins}, ya registradas={Reg}, {Ms} ms. {Mensaje}",
                loader.Name, summary.CodigoRetorno, summary.FilasLeidas, summary.Rechazadas, summary.Insertadas, summary.YaRegistradas,
                summary.DuracionMs, summary.Mensaje);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[{Dim}] Falló la carga (se revirtió la transacción): {Mensaje}", loader.Name, ex.Message);
            summary = new DimensionLoadSummary(loader.Name, LoadStatus.Error, 0, 0, 0, 0, -1, sw.ElapsedMilliseconds, ex.Message);
        }

        try { await repository.SaveRunAsync(loteId, summary, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[{Dim}] No se pudo guardar el resumen en etl_carga_dimensiones.", loader.Name);
        }
        return summary;
    }
}
