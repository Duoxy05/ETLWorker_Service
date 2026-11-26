using ETLWorker.Application.Abstractions;
using ETLWorker.Domain.Entities;

namespace ETLWorker.Application.Dimensions;

/// <summary>
/// Dim_Sentimiento: tres valores fijos del modelo (1 Positiva +1, 2 Neutra 0, 3 Negativa -1).
/// Las consultas del modelo dependen de estos IDs (por ejemplo IdSentimiento = 1 para "satisfechos").
/// </summary>
public sealed class DimSentimientoLoader(IDimensionRepository repository) : IDimensionLoader
{
    private static readonly IReadOnlyList<DimSentimientoRow> Values = new[]
    {
        new DimSentimientoRow(1, "Positiva", 1),
        new DimSentimientoRow(2, "Neutra", 0),
        new DimSentimientoRow(3, "Negativa", -1)
    };

    public string Name => "Dim_Sentimiento";

    public async Task<DimensionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var load = await repository.LoadSentimientosAsync(Values, cancellationToken);
        return new DimensionLoadResult(Values.Count, 0, load);
    }
}
