using ETLWorker.Application.Dimensions;
using ETLWorker.Application.Services;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ETLWorker.Worker;

/// <summary>
/// Host del proceso. En cada ejecución: (1) extrae las fuentes a staging y (2) carga las dimensiones del
/// almacén de datos. Corre una vez (IntervalMinutes = 0) o periódicamente.
/// </summary>
public sealed class EtlWorker(
    EtlOrchestrator orchestrator,
    DimensionLoadOrchestrator dimensionOrchestrator,
    IOptions<EtlOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<EtlWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.IntervalMinutes;
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
                if (interval <= 0) break;

                logger.LogInformation("Próxima ejecución en {Minutos} min.", interval);
                await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
            } while (!stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Proceso cancelado por el host.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Error fatal en el worker.");
            Environment.ExitCode = 1;
        }
        finally
        {
            if (interval <= 0) lifetime.StopApplication();
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var cfg = options.Value;
        var extractionOk = true;

        // Etapa 1: extracción (E) a las tablas stg_*.
        if (cfg.RunExtraction)
        {
            var results = await orchestrator.RunAsync(ct);
            extractionOk = results.All(r => r.Estado == ExtractionStatus.Ok);
            if (!extractionOk) Environment.ExitCode = 1;
        }
        else
        {
            logger.LogInformation("Extracción omitida por configuración (Etl:RunExtraction = false): se usa el staging existente.");
        }

        // Etapa 2: carga de dimensiones. Si la extracción falló, el staging puede estar incompleto: no se continúa.
        if (!cfg.Dimensions.Enabled)
        {
            logger.LogInformation("Carga de dimensiones deshabilitada por configuración.");
            return;
        }
        if (!extractionOk)
        {
            logger.LogWarning("Se omite la carga de dimensiones porque la extracción terminó con errores.");
            return;
        }

        var loads = await dimensionOrchestrator.RunAsync(ct);
        if (loads.Any(l => l.Estado != LoadStatus.Ok)) Environment.ExitCode = 1;
    }
}
