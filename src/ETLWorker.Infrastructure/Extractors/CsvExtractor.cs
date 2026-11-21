using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;
using ETLWorker.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace ETLWorker.Infrastructure.Extractors;

/// <summary>Lee archivos CSV (encuestas internas) en streaming con CsvHelper.</summary>
public sealed class CsvExtractor(ILogger<CsvExtractor> logger) : IExtractor
{
    public SourceType Type => SourceType.Csv;

    public async IAsyncEnumerable<RawOpinion> ExtractAsync(
        SourceOptions source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opt = source.Csv ?? throw new InvalidOperationException($"La fuente '{source.Name}' no tiene sección Csv.");
        var path = PathResolver.Resolve(opt.Path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No se encontró el CSV de '{source.Name}'.", path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.GetEncoding(opt.Encoding), detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = opt.Delimiter,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = args => logger.LogWarning("[{Fuente}] Dato mal formado en fila {Fila}.", source.Name, args.Context?.Parser?.Row)
        };
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync()) yield break;
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        var f = source.Fields;
        foreach (var col in new[] { f.IdOrigen, f.IdCliente, f.IdProducto, f.Fecha, f.Comentario, f.Puntaje, f.Clasificacion, f.Plataforma })
            if (col is not null && !headers.Contains(col))
                throw new InvalidDataException($"El CSV de '{source.Name}' no tiene la columna '{col}'. Columnas: {string.Join(", ", headers)}.");

        var row = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            row++;
            yield return new RawOpinion
            {
                Fuente = source.Name,
                NumeroFila = row,
                IdOrigen = Get(csv, f.IdOrigen),
                IdClienteRaw = Get(csv, f.IdCliente),
                IdProductoRaw = Get(csv, f.IdProducto),
                FechaRaw = Get(csv, f.Fecha),
                ComentarioRaw = Get(csv, f.Comentario),
                PuntajeRaw = Get(csv, f.Puntaje),
                ClasificacionRaw = Get(csv, f.Clasificacion),
                PlataformaRaw = Get(csv, f.Plataforma)
            };
        }
    }

    private static string? Get(CsvReader csv, string? column)
    {
        if (column is null) return null;
        var v = csv.GetField(column);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
