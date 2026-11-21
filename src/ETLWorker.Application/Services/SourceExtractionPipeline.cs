using System.Diagnostics;
using System.Threading.Channels;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Application.Services;

/// <summary>
/// Pipeline productor/consumidor de UNA fuente:
///   extractor (lee en streaming) -> Channel acotado -> valida + agrupa en lotes -> staging.
/// Lectura y escritura se solapan y la memoria queda acotada (ChannelCapacity + BatchSize).
/// </summary>
public sealed class SourceExtractionPipeline(
    IStagingRepository staging,
    IOptions<EtlOptions> options,
    ILogger<SourceExtractionPipeline> logger)
{
    public async Task<ExtractionSummary> RunAsync(
        IExtractor extractor, SourceOptions src, Guid loteId, ReferenceCatalog catalog, CancellationToken ct)
    {
        var cfg = options.Value;
        var sw = Stopwatch.StartNew();
        long total = 0, flagged = 0, issueCount = 0;
        var batch = new List<RawOpinion>(cfg.BatchSize);
        var issues = new List<QualityIssue>();
        var validator = new OpinionValidator(src, catalog);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linked.Token;
        Task producer = Task.CompletedTask;

        async Task FlushAsync()
        {
            if (batch.Count > 0)
            {
                await staging.InsertBatchAsync(src.StagingTable, loteId, batch, token);
                logger.LogDebug("[{Fuente}] Lote de {N} filas guardado en {Tabla}.", src.Name, batch.Count, src.StagingTable);
                batch.Clear();
            }
            if (issues.Count > 0)
            {
                await staging.InsertIssuesAsync(loteId, issues, token);
                issues.Clear();
            }
        }

        try
        {
            logger.LogInformation("[{Fuente}] Extracción iniciada ({Tipo}).", src.Name, src.Type);
            await staging.PrepareAsync(src.StagingTable, token);

            var channel = Channel.CreateBounded<RawOpinion>(new BoundedChannelOptions(cfg.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            producer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var row in extractor.ExtractAsync(src, token))
                        await channel.Writer.WriteAsync(row, token);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);   // el consumidor recibirá la excepción
                }
            }, token);

            await foreach (var row in channel.Reader.ReadAllAsync(token))
            {
                total++;
                var found = validator.Validate(row);
                if (found.Count > 0) { flagged++; issueCount += found.Count; issues.AddRange(found); }
                batch.Add(row);
                if (batch.Count >= cfg.BatchSize) await FlushAsync();
            }
            await FlushAsync();

            sw.Stop();
            var rate = total / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            logger.LogInformation(
                "[{Fuente}] OK: {Filas} filas, {Marcadas} con observaciones ({Obs} hallazgos), {Ms} ms, {Rate:F0} filas/s.",
                src.Name, total, flagged, issueCount, sw.ElapsedMilliseconds, rate);
            return new ExtractionSummary(src.Name, src.Type, ExtractionStatus.Ok, total, flagged, issueCount, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Fuente}] Falló la extracción: {Mensaje}", src.Name, ex.Message);
            try { await staging.PrepareAsync(src.StagingTable, CancellationToken.None); }   // todo o nada por fuente
            catch (Exception cleanup) { logger.LogWarning(cleanup, "[{Fuente}] No se pudo limpiar el staging parcial.", src.Name); }
            return new ExtractionSummary(src.Name, src.Type, ExtractionStatus.Error, total, flagged, issueCount, sw.ElapsedMilliseconds, ex.Message);
        }
        finally
        {
            linked.Cancel();
            try { await producer; } catch { /* ya se reportó arriba */ }
        }
    }
}
