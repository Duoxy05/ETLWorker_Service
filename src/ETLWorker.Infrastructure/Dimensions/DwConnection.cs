using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace ETLWorker.Infrastructure.Dimensions;

/// <summary>
/// Conexión del cargador de dimensiones. Usa su propia cadena (usuario etl_loader, mínimo privilegio:
/// solo EXECUTE sobre los procedimientos usp_Load*), distinta de la del extractor (etl_writer).
/// </summary>
internal static class DwConnection
{
    public const string ConnectionName = "AnaliticaCarga";

    public static async Task<MySqlConnection> OpenAsync(IConfiguration configuration, CancellationToken ct)
    {
        var cs = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException(
                $"Falta ConnectionStrings:{ConnectionName} (defínala en user-secrets o variable de entorno ConnectionStrings__{ConnectionName}).");

        var conn = new MySqlConnection(cs);
        await conn.OpenAsync(ct);
        return conn;
    }
}
