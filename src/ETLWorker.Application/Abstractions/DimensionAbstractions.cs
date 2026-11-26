using ETLWorker.Domain.Entities;

namespace ETLWorker.Application.Abstractions;

/// <summary>Carga una dimensión del almacén de datos. Una clase por dimensión (responsabilidad única).</summary>
public interface IDimensionLoader
{
    string Name { get; }
    Task<DimensionLoadResult> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>Origen de los catálogos de clientes y productos (hoy CSV; mañana podría ser una BD).</summary>
public interface ICatalogSource
{
    IAsyncEnumerable<RawCliente> ReadClientesAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<RawProducto> ReadProductosAsync(CancellationToken cancellationToken);
}

/// <summary>Lectura de la zona de staging para derivar dimensiones a partir de los datos ya extraídos.</summary>
public interface IStagingReader
{
    Task<IReadOnlyList<string>> GetDistinctPlataformasAsync(string stagingTable, CancellationToken cancellationToken);
}

/// <summary>
/// Escritura en las dimensiones mediante procedimientos almacenados (uno por dimensión). Cada llamada es
/// idempotente: si el registro ya existe no se inserta y el procedimiento responde "SE ENCUENTRA REGISTRADA".
/// </summary>
public interface IDimensionRepository
{
    Task<LoadResult> LoadClientesAsync(IReadOnlyList<DimClienteRow> rows, CancellationToken cancellationToken);
    Task<LoadResult> LoadProductosAsync(IReadOnlyList<DimProductoRow> rows, CancellationToken cancellationToken);
    /// <summary>Agrega solo las combinaciones Canal/Plataforma que no existan; el procedimiento asigna el IdFuente.</summary>
    Task<LoadResult> LoadFuentesAsync(IReadOnlyList<DimFuenteRow> rows, CancellationToken cancellationToken);
    Task<LoadResult> LoadFechasAsync(IReadOnlyList<DimFechaRow> rows, CancellationToken cancellationToken);
    Task<LoadResult> LoadSentimientosAsync(IReadOnlyList<DimSentimientoRow> rows, CancellationToken cancellationToken);
    Task SaveRunAsync(Guid loteId, DimensionLoadSummary summary, CancellationToken cancellationToken);
}
