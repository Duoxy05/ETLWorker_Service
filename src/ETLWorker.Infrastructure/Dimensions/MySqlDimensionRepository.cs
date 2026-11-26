using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETLWorker.Application.Abstractions;
using ETLWorker.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace ETLWorker.Infrastructure.Dimensions;

/// <summary>
/// Carga las dimensiones llamando a procedimientos almacenados de MySQL (usp_LoadCliente, usp_LoadProducto,
/// usp_LoadFuente, usp_LoadFecha, usp_LoadSentimiento) con <see cref="CommandType.StoredProcedure"/>.
///
/// MySQL no tiene parámetros de tipo tabla (SqlDbType.Structured de SQL Server). Su equivalente es enviar TODAS
/// las filas de la dimensión como un arreglo JSON en un único parámetro (p_filas); el procedimiento lo expande con
/// JSON_TABLE. Así hay una sola llamada por dimensión, sin SQL armado a mano en C#.
///
/// Cada procedimiento devuelve por parámetros de salida: filas insertadas (p_insertadas), filas que ya estaban
/// registradas (p_existentes), código de retorno (p_codigo) y mensaje (p_result). Si el registro ya existe, NO se
/// inserta y el mensaje dice "... SE ENCUENTRA REGISTRADA". Los procedimientos corren en una transacción.
/// </summary>
public sealed class MySqlDimensionRepository(IConfiguration configuration) : IDimensionRepository
{
    // Los valores nulos no se escriben: la columna correspondiente de JSON_TABLE queda en NULL.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<LoadResult> LoadClientesAsync(IReadOnlyList<DimClienteRow> rows, CancellationToken ct) =>
        CallAsync("usp_LoadCliente",
            rows.Select(r => new
            {
                r.IdCliente, r.Nombre, r.Email, r.Pais, r.Edad, r.TipoCliente,
                EsEspecial = r.EsEspecial ? 1 : 0
            }).ToList(), ct);

    public Task<LoadResult> LoadProductosAsync(IReadOnlyList<DimProductoRow> rows, CancellationToken ct) =>
        CallAsync("usp_LoadProducto",
            rows.Select(r => new
            {
                r.IdProducto, r.Nombre, r.Categoria,
                EsEspecial = r.EsEspecial ? 1 : 0
            }).ToList(), ct);

    /// <summary>El procedimiento busca por clave natural (Canal, Plataforma) y asigna el siguiente IdFuente libre.</summary>
    public Task<LoadResult> LoadFuentesAsync(IReadOnlyList<DimFuenteRow> rows, CancellationToken ct) =>
        CallAsync("usp_LoadFuente",
            rows.Select(r => new { r.Canal, r.Plataforma }).ToList(), ct);

    public Task<LoadResult> LoadFechasAsync(IReadOnlyList<DimFechaRow> rows, CancellationToken ct) =>
        CallAsync("usp_LoadFecha",
            rows.Select(r => new
            {
                r.IdFecha,
                Fecha = r.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Anio, r.Trimestre, r.Mes, r.NombreMes, r.AnioMes, r.Dia
            }).ToList(), ct);

    public Task<LoadResult> LoadSentimientosAsync(IReadOnlyList<DimSentimientoRow> rows, CancellationToken ct) =>
        CallAsync("usp_LoadSentimiento",
            rows.Select(r => new { r.IdSentimiento, r.Nombre, r.Polaridad }).ToList(), ct);

    public async Task SaveRunAsync(Guid loteId, DimensionLoadSummary s, CancellationToken ct)
    {
        await using var conn = await DwConnection.OpenAsync(configuration, ct);
        await using var cmd = new MySqlCommand(
            "INSERT INTO etl_carga_dimensiones (LoteId,Dimension,Estado,FilasLeidas,Rechazadas,Insertadas,YaRegistradas,CodigoRetorno,DuracionMs,Mensaje) " +
            "VALUES (@lote,@dim,@est,@fl,@re,@ins,@reg,@cod,@du,@msg)", conn);
        cmd.Parameters.AddWithValue("@lote", loteId.ToString());
        cmd.Parameters.AddWithValue("@dim", s.Dimension);
        cmd.Parameters.AddWithValue("@est", s.Estado.ToString().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@fl", s.FilasLeidas);
        cmd.Parameters.AddWithValue("@re", s.Rechazadas);
        cmd.Parameters.AddWithValue("@ins", s.Insertadas);
        cmd.Parameters.AddWithValue("@reg", s.YaRegistradas);
        cmd.Parameters.AddWithValue("@cod", s.CodigoRetorno);
        cmd.Parameters.AddWithValue("@du", s.DuracionMs);
        cmd.Parameters.AddWithValue("@msg", (object?)(s.Mensaje is { Length: > 1000 } ? s.Mensaje[..1000] : s.Mensaje) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------------------------------------------------ llamada genérica al procedimiento

    /// <summary>
    /// Serializa las filas a JSON, llama al procedimiento y lee sus parámetros de salida.
    /// Si el procedimiento devuelve código -1 (error: ya hizo ROLLBACK) se lanza una excepción para que el
    /// orquestador marque la dimensión como ERROR y las demás continúen.
    /// </summary>
    private async Task<LoadResult> CallAsync<T>(string procedure, IReadOnlyCollection<T> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return LoadResult.Empty;

        var json = JsonSerializer.Serialize(rows, JsonOptions);

        await using var conn = await DwConnection.OpenAsync(configuration, ct);
        await using var cmd = new MySqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };

        // Parámetros de entrada y salida (mismos nombres que en el CREATE PROCEDURE).
        cmd.Parameters.Add(new MySqlParameter("p_filas", MySqlDbType.LongText) { Value = json });
        var pInsertadas = new MySqlParameter("p_insertadas", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
        var pExistentes = new MySqlParameter("p_existentes", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
        var pCodigo = new MySqlParameter("p_codigo", MySqlDbType.Int32) { Direction = ParameterDirection.Output };
        var pResult = new MySqlParameter("p_result", MySqlDbType.VarChar, 500) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(pInsertadas);
        cmd.Parameters.Add(pExistentes);
        cmd.Parameters.Add(pCodigo);
        cmd.Parameters.Add(pResult);

        await cmd.ExecuteNonQueryAsync(ct);

        var codigo = Convert.ToInt32(pCodigo.Value, CultureInfo.InvariantCulture);
        var mensaje = pResult.Value as string;

        if (codigo < 0)
            throw new InvalidOperationException($"{procedure} devolvió código {codigo}: {mensaje}");

        return new LoadResult(
            Convert.ToInt64(pInsertadas.Value, CultureInfo.InvariantCulture),
            Convert.ToInt64(pExistentes.Value, CultureInfo.InvariantCulture),
            codigo,
            mensaje);
    }
}
