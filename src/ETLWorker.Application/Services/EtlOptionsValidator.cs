using ETLWorker.Application.Settings;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ETLWorker.Application.Services;

/// <summary>Falla rápido al arrancar si la configuración JSON es inválida.</summary>
public sealed class EtlOptionsValidator : IValidateOptions<EtlOptions>
{
    public ValidateOptionsResult Validate(string? name, EtlOptions o)
    {
        var e = new List<string>();
        if (o.MaxDegreeOfParallelism is < 1 or > 16) e.Add("Etl:MaxDegreeOfParallelism debe estar entre 1 y 16.");
        if (o.BatchSize is < 1 or > 5000) e.Add("Etl:BatchSize debe estar entre 1 y 5000.");
        if (o.ChannelCapacity < o.BatchSize) e.Add("Etl:ChannelCapacity no puede ser menor que BatchSize.");
        if (o.IntervalMinutes < 0) e.Add("Etl:IntervalMinutes no puede ser negativo.");
        if (o.Sources.Count == 0) e.Add("Etl:Sources no puede estar vacío.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < o.Sources.Count; i++)
        {
            var s = o.Sources[i];
            var p = $"Etl:Sources[{i}]";
            if (string.IsNullOrWhiteSpace(s.Name)) e.Add($"{p}.Name es obligatorio.");
            else if (!names.Add(s.Name)) e.Add($"{p}.Name '{s.Name}' está repetido.");

            if (!StagingTableName.IsValid(s.StagingTable))
                e.Add($"{p}.StagingTable debe cumplir ^stg_[a-z0-9_]+$.");

            var f = s.Fields;
            if (new[] { f.IdOrigen, f.IdCliente, f.IdProducto, f.Fecha, f.Comentario }.Any(string.IsNullOrWhiteSpace))
                e.Add($"{p}.Fields: IdOrigen, IdCliente, IdProducto, Fecha y Comentario son obligatorios.");

            switch (s.Type)
            {
                case SourceType.Csv:
                    if (string.IsNullOrWhiteSpace(s.Csv?.Path)) e.Add($"{p}.Csv.Path es obligatorio.");
                    break;
                case SourceType.Database:
                    if (string.IsNullOrWhiteSpace(s.Database?.ConnectionName)) e.Add($"{p}.Database.ConnectionName es obligatorio.");
                    if (s.Database is null || !s.Database.Query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                        e.Add($"{p}.Database.Query debe ser una consulta SELECT.");
                    break;
                case SourceType.Api:
                    if (s.Api is null || !Uri.TryCreate(s.Api.BaseUrl, UriKind.Absolute, out _)) e.Add($"{p}.Api.BaseUrl debe ser una URL absoluta.");
                    if (string.IsNullOrWhiteSpace(s.Api?.Endpoint)) e.Add($"{p}.Api.Endpoint es obligatorio.");
                    if (s.Api is not null && s.Api.PageSize is < 1 or > 1000) e.Add($"{p}.Api.PageSize debe estar entre 1 y 1000.");
                    break;
            }
        }
        return e.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(e);
    }
}
