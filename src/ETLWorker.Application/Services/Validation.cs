using System.Globalization;
using System.Text.RegularExpressions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;

namespace ETLWorker.Application.Services;

/// <summary>Convierte "C007", "P016" o "8537" en el entero del catálogo (7, 16, 8537).</summary>
public static class IdNormalizer
{
    private static readonly Regex Pattern = new(@"^[A-Za-z]?(\d{1,9})$", RegexOptions.Compiled);

    public static bool TryNormalize(string? raw, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var m = Pattern.Match(raw.Trim());
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
}

public static class StagingTableName
{
    private static readonly Regex Pattern = new("^stg_[a-z0-9_]{1,50}$", RegexOptions.Compiled);
    public static bool IsValid(string? name) => name is not null && Pattern.IsMatch(name);
}

/// <summary>IDs válidos de los catálogos. Null = no se pudo cargar, no se valida existencia.</summary>
public sealed class ReferenceCatalog
{
    public HashSet<int>? Clientes { get; init; }
    public HashSet<int>? Productos { get; init; }
    public static ReferenceCatalog Empty => new();
}

/// <summary>
/// Reglas de calidad de la extracción. NUNCA descarta filas: solo produce hallazgos.
/// Una instancia por fuente y ejecución (guarda los IdOrigen vistos para detectar duplicados).
/// </summary>
public sealed class OpinionValidator(SourceOptions source, ReferenceCatalog catalog)
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<QualityIssue> Validate(RawOpinion r)
    {
        var issues = new List<QualityIssue>(0);

        void Add(string campo, string problema, IssueSeverity sev, string? valor) =>
            issues.Add(new QualityIssue(r.Fuente, r.IdOrigen, campo, problema, sev,
                valor is { Length: > 200 } ? valor[..200] : valor));

        if (string.IsNullOrWhiteSpace(r.IdOrigen))
            Add("IdOrigen", "IdOrigen vacío", IssueSeverity.Error, null);
        else if (!_seen.Add(r.IdOrigen))
            Add("IdOrigen", "IdOrigen duplicado en la fuente", IssueSeverity.Error, r.IdOrigen);

        CheckReference("IdCliente", r.IdClienteRaw, catalog.Clientes, Add);
        CheckReference("IdProducto", r.IdProductoRaw, catalog.Productos, Add);

        if (string.IsNullOrWhiteSpace(r.FechaRaw))
            Add("Fecha", "Fecha vacía", IssueSeverity.Advertencia, null);
        else if (!DateTime.TryParseExact(r.FechaRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            Add("Fecha", "Fecha con formato inválido (se esperaba yyyy-MM-dd)", IssueSeverity.Advertencia, r.FechaRaw);

        if (string.IsNullOrWhiteSpace(r.ComentarioRaw))
            Add("Comentario", "Comentario vacío", IssueSeverity.Advertencia, null);

        if (source.Fields.Puntaje is not null)
        {
            if (string.IsNullOrWhiteSpace(r.PuntajeRaw))
                Add("Puntaje", "Puntaje vacío", IssueSeverity.Advertencia, null);
            else if (!int.TryParse(r.PuntajeRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p is < 1 or > 5)
                Add("Puntaje", "Puntaje fuera del rango 1-5", IssueSeverity.Error, r.PuntajeRaw);
        }

        return issues;
    }

    private static void CheckReference(string campo, string? raw, HashSet<int>? valid,
        Action<string, string, IssueSeverity, string?> add)
    {
        if (string.IsNullOrWhiteSpace(raw))
            add(campo, "ID vacío (Transform asignará el miembro especial)", IssueSeverity.Advertencia, null);
        else if (!IdNormalizer.TryNormalize(raw, out var id))
            add(campo, "Formato de ID no reconocido", IssueSeverity.Advertencia, raw);
        else if (valid is not null && !valid.Contains(id))
            add(campo, "ID inexistente en el catálogo (Transform asignará 'no encontrado')", IssueSeverity.Advertencia, raw);
    }
}
