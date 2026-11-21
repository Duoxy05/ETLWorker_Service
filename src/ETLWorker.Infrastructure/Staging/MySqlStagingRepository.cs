using System.Text;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Services;
using ETLWorker.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace ETLWorker.Infrastructure.Staging;

/// <summary>
/// Escribe en la BD analítica (MySQL). Inserciones multi-fila parametrizadas dentro de una transacción
/// por lote: rápido (pocos viajes a la BD) y sin riesgo de SQL injection.
/// </summary>
public sealed class MySqlStagingRepository(IConfiguration configuration) : IStagingRepository
{
    private const string ConnectionName = "Analitica";
    private const int Chunk = 500;

    public async Task PrepareAsync(string table, CancellationToken ct)
    {
        EnsureValid(table);
        await using var conn = await OpenAsync(ct);
        await using var cmd = new MySqlCommand($"DELETE FROM `{table}`", conn);
        await cmd.ExecuteNonQueryAsync(ct);   // DELETE (no TRUNCATE) para no exigir privilegio DROP
    }

    public async Task InsertBatchAsync(string table, Guid loteId, IReadOnlyList<RawOpinion> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        EnsureValid(table);
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        for (var start = 0; start < rows.Count; start += Chunk)
        {
            var part = rows.Skip(start).Take(Chunk).ToList();
            var sql = new StringBuilder($"INSERT INTO `{table}` (LoteId,Fuente,NumeroFila,IdOrigen,IdClienteRaw,IdProductoRaw,FechaRaw,ComentarioRaw,PuntajeRaw,ClasificacionRaw,PlataformaRaw) VALUES ");
            await using var cmd = new MySqlCommand { Connection = conn, Transaction = tx };
            cmd.Parameters.AddWithValue("@lote", loteId.ToString());
            cmd.Parameters.AddWithValue("@fuente", part[0].Fuente);
            for (var i = 0; i < part.Count; i++)
            {
                var r = part[i];
                if (i > 0) sql.Append(',');
                sql.Append($"(@lote,@fuente,@nf{i},@io{i},@ic{i},@ip{i},@fe{i},@co{i},@pu{i},@cl{i},@pl{i})");
                cmd.Parameters.AddWithValue($"@nf{i}", r.NumeroFila);
                cmd.Parameters.AddWithValue($"@io{i}", Db(r.IdOrigen));
                cmd.Parameters.AddWithValue($"@ic{i}", Db(r.IdClienteRaw));
                cmd.Parameters.AddWithValue($"@ip{i}", Db(r.IdProductoRaw));
                cmd.Parameters.AddWithValue($"@fe{i}", Db(r.FechaRaw));
                cmd.Parameters.AddWithValue($"@co{i}", Db(r.ComentarioRaw));
                cmd.Parameters.AddWithValue($"@pu{i}", Db(r.PuntajeRaw));
                cmd.Parameters.AddWithValue($"@cl{i}", Db(r.ClasificacionRaw));
                cmd.Parameters.AddWithValue($"@pl{i}", Db(r.PlataformaRaw));
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task InsertIssuesAsync(Guid loteId, IReadOnlyList<QualityIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return;
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        for (var start = 0; start < issues.Count; start += Chunk)
        {
            var part = issues.Skip(start).Take(Chunk).ToList();
            var sql = new StringBuilder("INSERT INTO etl_calidad_datos (LoteId,Fuente,IdOrigen,Campo,Problema,Severidad,ValorOriginal) VALUES ");
            await using var cmd = new MySqlCommand { Connection = conn, Transaction = tx };
            cmd.Parameters.AddWithValue("@lote", loteId.ToString());
            for (var i = 0; i < part.Count; i++)
            {
                var q = part[i];
                if (i > 0) sql.Append(',');
                sql.Append($"(@lote,@fu{i},@io{i},@ca{i},@pr{i},@se{i},@va{i})");
                cmd.Parameters.AddWithValue($"@fu{i}", q.Fuente);
                cmd.Parameters.AddWithValue($"@io{i}", Db(q.IdOrigen));
                cmd.Parameters.AddWithValue($"@ca{i}", q.Campo);
                cmd.Parameters.AddWithValue($"@pr{i}", q.Problema);
                cmd.Parameters.AddWithValue($"@se{i}", q.Severidad.ToString());
                cmd.Parameters.AddWithValue($"@va{i}", Db(q.ValorOriginal));
            }
            cmd.CommandText = sql.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task SaveRunAsync(Guid loteId, ExtractionSummary s, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            "INSERT INTO etl_ejecucion (LoteId,Fuente,TipoFuente,Estado,FilasLeidas,FilasConObservaciones,Observaciones,DuracionMs,Mensaje) " +
            "VALUES (@lote,@fu,@tipo,@est,@fl,@fo,@ob,@du,@msg)", conn);
        cmd.Parameters.AddWithValue("@lote", loteId.ToString());
        cmd.Parameters.AddWithValue("@fu", s.Fuente);
        cmd.Parameters.AddWithValue("@tipo", s.Tipo.ToString());
        cmd.Parameters.AddWithValue("@est", s.Estado.ToString().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@fl", s.FilasLeidas);
        cmd.Parameters.AddWithValue("@fo", s.FilasConObservaciones);
        cmd.Parameters.AddWithValue("@ob", s.Observaciones);
        cmd.Parameters.AddWithValue("@du", s.DuracionMs);
        cmd.Parameters.AddWithValue("@msg", Db(s.Mensaje is { Length: > 1000 } ? s.Mensaje[..1000] : s.Mensaje));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cs = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Falta ConnectionStrings:{ConnectionName} (defínala en user-secrets o variable de entorno).");
        var conn = new MySqlConnection(cs);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static void EnsureValid(string table)
    {
        if (!StagingTableName.IsValid(table)) throw new ArgumentException($"Nombre de tabla de staging no permitido: '{table}'.");
    }

    private static object Db(string? v) => (object?)v ?? DBNull.Value;
}
