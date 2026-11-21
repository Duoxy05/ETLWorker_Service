using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ETLWorker.Infrastructure.Extractors;

/// <summary>Consulta reseñas de una BD relacional (MySQL) con ADO.NET, leyendo fila a fila.</summary>
public sealed class DatabaseExtractor(IConfiguration configuration, ILogger<DatabaseExtractor> logger) : IExtractor
{
    public SourceType Type => SourceType.Database;

    public async IAsyncEnumerable<RawOpinion> ExtractAsync(
        SourceOptions source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opt = source.Database ?? throw new InvalidOperationException($"La fuente '{source.Name}' no tiene sección Database.");
        var connectionString = configuration.GetConnectionString(opt.ConnectionName)
            ?? throw new InvalidOperationException($"Falta ConnectionStrings:{opt.ConnectionName} (defínala en user-secrets o variable de entorno).");

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);
        logger.LogInformation("[{Fuente}] Conectado a la base de datos origen.", source.Name);

        await using var cmd = new MySqlCommand(opt.Query, conn) { CommandTimeout = opt.CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var f = source.Fields;
        var iId = Ord(reader, f.IdOrigen); var iCli = Ord(reader, f.IdCliente); var iPro = Ord(reader, f.IdProducto);
        var iFec = Ord(reader, f.Fecha); var iCom = Ord(reader, f.Comentario); var iPun = Ord(reader, f.Puntaje);
        var iCla = Ord(reader, f.Clasificacion); var iPla = Ord(reader, f.Plataforma);

        var row = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            row++;
            yield return new RawOpinion
            {
                Fuente = source.Name,
                NumeroFila = row,
                IdOrigen = Text(reader, iId),
                IdClienteRaw = Text(reader, iCli),
                IdProductoRaw = Text(reader, iPro),
                FechaRaw = Text(reader, iFec),
                ComentarioRaw = Text(reader, iCom),
                PuntajeRaw = Text(reader, iPun),
                ClasificacionRaw = Text(reader, iCla),
                PlataformaRaw = Text(reader, iPla)
            };
        }
    }

    private static int? Ord(DbDataReader reader, string? column)
    {
        if (column is null) return null;
        try { return reader.GetOrdinal(column); }
        catch (IndexOutOfRangeException) { throw new InvalidDataException($"La consulta no devuelve la columna '{column}'."); }
    }

    private static string? Text(DbDataReader reader, int? i)
    {
        if (i is null || reader.IsDBNull(i.Value)) return null;
        var s = reader.GetValue(i.Value) switch
        {
            DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString()
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
