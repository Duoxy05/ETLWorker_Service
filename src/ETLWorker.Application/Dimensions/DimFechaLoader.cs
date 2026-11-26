using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Application.Dimensions;

/// <summary>Dim_Fecha: calendario completo generado por código (no viene de ninguna fuente) + miembro -1.</summary>
public sealed class DimFechaLoader(
    IDimensionRepository repository,
    IOptions<EtlOptions> options,
    ILogger<DimFechaLoader> logger) : IDimensionLoader
{
    public string Name => "Dim_Fecha";

    public async Task<DimensionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var cfg = options.Value.Dimensions;

        var rows = new List<DimFechaRow> { CalendarFactory.Unknown };
        rows.AddRange(CalendarFactory.Range(cfg.CalendarStartYear, cfg.CalendarEndYear));

        logger.LogInformation("[Dim_Fecha] Calendario generado: {Desde}-01-01 a {Hasta}-12-31 ({N} días + 1 miembro especial).",
            cfg.CalendarStartYear, cfg.CalendarEndYear, rows.Count - 1);

        var load = await repository.LoadFechasAsync(rows, cancellationToken);
        return new DimensionLoadResult(rows.Count, 0, load);
    }
}
