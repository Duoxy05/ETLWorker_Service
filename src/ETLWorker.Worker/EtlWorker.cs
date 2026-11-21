using ETLWorker.Application.Services;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ETLWorker.Worker;

/// <summary>Host del proceso: ejecuta la extracción una vez (IntervalMinutes = 0) o periódicamente.</summary>
public sealed class EtlWorker(
    EtlOrchestrator orchestrator,
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
                var results = await orchestrator.RunAsync(stoppingToken);
                if (results.Any(r => r.Estado != ExtractionStatus.Ok)) Environment.ExitCode = 1;
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
}
