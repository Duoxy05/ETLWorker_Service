using ETLWorker.Application.Services;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;

namespace ETLWorker.Application.Abstractions;

/// <summary>Contrato común de todas las fuentes. Devuelve un flujo (no carga todo en memoria).</summary>
public interface IExtractor
{
    SourceType Type { get; }
    IAsyncEnumerable<RawOpinion> ExtractAsync(SourceOptions source, CancellationToken cancellationToken);
}

/// <summary>Acceso a la zona de staging y a la bitácora del ETL.</summary>
public interface IStagingRepository
{
    Task PrepareAsync(string table, CancellationToken ct);
    Task InsertBatchAsync(string table, Guid loteId, IReadOnlyList<RawOpinion> rows, CancellationToken ct);
    Task InsertIssuesAsync(Guid loteId, IReadOnlyList<QualityIssue> issues, CancellationToken ct);
    Task SaveRunAsync(Guid loteId, ExtractionSummary summary, CancellationToken ct);
}

public interface IReferenceCatalogProvider
{
    Task<ReferenceCatalog> LoadAsync(CancellationToken ct);
}
