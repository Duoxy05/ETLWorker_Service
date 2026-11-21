using ETLWorker.Domain.Enums;

namespace ETLWorker.Domain.Entities;

/// <summary>Opinión tal como llega de la fuente: todo texto, sin validar ni transformar.</summary>
public sealed class RawOpinion
{
    public required string Fuente { get; init; }
    public int NumeroFila { get; init; }
    public string? IdOrigen { get; init; }
    public string? IdClienteRaw { get; init; }
    public string? IdProductoRaw { get; init; }
    public string? FechaRaw { get; init; }
    public string? ComentarioRaw { get; init; }
    public string? PuntajeRaw { get; init; }
    public string? ClasificacionRaw { get; init; }
    public string? PlataformaRaw { get; init; }
}

/// <summary>Hallazgo de calidad: se registra, pero la fila NO se descarta.</summary>
public sealed record QualityIssue(
    string Fuente, string? IdOrigen, string Campo, string Problema,
    IssueSeverity Severidad, string? ValorOriginal);

/// <summary>Resultado de la extracción de una fuente en una ejecución.</summary>
public sealed record ExtractionSummary(
    string Fuente, SourceType Tipo, ExtractionStatus Estado,
    long FilasLeidas, long FilasConObservaciones, long Observaciones,
    long DuracionMs, string? Mensaje);
