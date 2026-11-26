using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Services;
using ETLWorker.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ETLWorker.Application.Dimensions;

/// <summary>Dim_Producto: catálogo de productos + miembro especial -1 "Producto no encontrado".</summary>
public sealed class DimProductoLoader(
    ICatalogSource catalog,
    IDimensionRepository repository,
    ILogger<DimProductoLoader> logger) : IDimensionLoader
{
    private const string SinCategoria = "Sin categoría";

    public string Name => "Dim_Producto";

    public async Task<DimensionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = new List<DimProductoRow>
        {
            new(SpecialMembers.ProductoNoEncontrado, "Producto no encontrado", SinCategoria, true)
        };

        var ids = new HashSet<int>();
        long read = 0, rejected = 0;

        await foreach (var raw in catalog.ReadProductosAsync(cancellationToken))
        {
            read++;

            if (!IdNormalizer.TryNormalize(raw.Id, out var id) || id <= 0)
            {
                rejected++;
                logger.LogWarning("[Dim_Producto] Fila {Fila} rechazada: IdProducto inválido ('{Id}').", raw.NumeroFila, raw.Id);
                continue;
            }
            if (string.IsNullOrWhiteSpace(raw.Nombre))
            {
                rejected++;
                logger.LogWarning("[Dim_Producto] Fila {Fila} rechazada: el producto {Id} no tiene nombre.", raw.NumeroFila, id);
                continue;
            }
            if (!ids.Add(id))
            {
                rejected++;
                logger.LogWarning("[Dim_Producto] Fila {Fila} rechazada: IdProducto {Id} duplicado en el catálogo.", raw.NumeroFila, id);
                continue;
            }

            rows.Add(new DimProductoRow(id, raw.Nombre.Trim(), raw.Categoria ?? SinCategoria, false));
        }

        logger.LogInformation("[Dim_Producto] Catálogo leído: {Leidas} filas, {Rechazadas} rechazadas, 1 miembro especial.", read, rejected);

        var load = await repository.LoadProductosAsync(rows, cancellationToken);
        return new DimensionLoadResult(read, rejected, load);
    }
}
