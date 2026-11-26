using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Application.Dimensions;

/// <summary>
/// Dim_Fuente: combinaciones Canal/Plataforma (Encuesta, Web, Red social + Instagram/Facebook/Twitter).
/// Lo fijo viene de la configuración; las plataformas variables se descubren en el staging ya extraído,
/// así que si mañana llega una red social nueva aparece sola en la dimensión.
/// </summary>
public sealed class DimFuenteLoader(
    IStagingReader staging,
    IDimensionRepository repository,
    IOptions<EtlOptions> options,
    ILogger<DimFuenteLoader> logger) : IDimensionLoader
{
    private const int MaxCanal = 20;       // Dim_Fuente.Canal      VARCHAR(20)
    private const int MaxPlataforma = 30;  // Dim_Fuente.Plataforma VARCHAR(30)

    public string Name => "Dim_Fuente";

    public async Task<DimensionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var wanted = new List<DimFuenteRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long rejected = 0;

        foreach (var map in options.Value.Dimensions.Fuentes)
        {
            var canal = map.Canal.Trim();

            IReadOnlyList<string> plataformas;
            if (!string.IsNullOrWhiteSpace(map.Plataforma))
            {
                plataformas = new[] { map.Plataforma.Trim() };
            }
            else
            {
                plataformas = await staging.GetDistinctPlataformasAsync(map.StagingTable!, cancellationToken);
                if (plataformas.Count == 0)
                    logger.LogWarning("[Dim_Fuente] {Tabla} no tiene plataformas para el canal '{Canal}'. ¿Se ejecutó la extracción?", map.StagingTable, canal);
            }

            foreach (var plataforma in plataformas)
            {
                if (canal.Length > MaxCanal || plataforma.Length > MaxPlataforma)
                {
                    rejected++;
                    logger.LogWarning("[Dim_Fuente] Rechazada '{Canal}/{Plataforma}': excede el largo permitido ({MaxC}/{MaxP}).", canal, plataforma, MaxCanal, MaxPlataforma);
                    continue;
                }
                if (seen.Add($"{canal}|{plataforma}"))
                    wanted.Add(new DimFuenteRow(canal, plataforma));
            }
        }

        var load = await repository.LoadFuentesAsync(wanted, cancellationToken);
        return new DimensionLoadResult(wanted.Count + rejected, rejected, load);
    }
}
