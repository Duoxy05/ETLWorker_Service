using System.Data;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Services;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace ETLWorker.Infrastructure.Dimensions;

/// <summary>Lee valores distintos del staging para derivar dimensiones (solo SELECT).</summary>
public sealed class MySqlStagingReader(IConfiguration configuration) : IStagingReader
{
    public async Task<IReadOnlyList<string>> GetDistinctPlataformasAsync(string stagingTable, CancellationToken cancellationToken)
    {
        // El nombre de tabla no se puede parametrizar: se valida contra ^stg_[a-z0-9_]+$ (anti inyección SQL).
        if (!StagingTableName.IsValid(stagingTable))
            throw new ArgumentException($"Nombre de tabla de staging no permitido: '{stagingTable}'.", nameof(stagingTable));

        await using var conn = await DwConnection.OpenAsync(configuration, cancellationToken);
        await using var cmd = new MySqlCommand(
            $"SELECT DISTINCT TRIM(PlataformaRaw) FROM `{stagingTable}` " +
            "WHERE PlataformaRaw IS NOT NULL AND TRIM(PlataformaRaw) <> '' ORDER BY 1", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var list = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            list.Add(reader.GetString(0));
        return list;
    }
}
