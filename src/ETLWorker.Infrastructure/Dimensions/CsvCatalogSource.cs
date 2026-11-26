using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Infrastructure.Dimensions;

/// <summary>Lee clients.csv y products.csv en streaming. Verifica que existan las columnas configuradas.</summary>
public sealed class CsvCatalogSource(IOptions<EtlOptions> options, ILogger<CsvCatalogSource> logger) : ICatalogSource
{
    public async IAsyncEnumerable<RawCliente> ReadClientesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var c = options.Value.Catalogs;
        var columns = new[] { c.ClientesIdColumn, c.ClientesNombreColumn, c.ClientesEmailColumn };

        await foreach (var (row, v) in ReadAsync(c.ClientesPath, "clientes", columns, cancellationToken))
            yield return new RawCliente(row, v[0], v[1], v[2]);
    }

    public async IAsyncEnumerable<RawProducto> ReadProductosAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var c = options.Value.Catalogs;
        var columns = new[] { c.ProductosIdColumn, c.ProductosNombreColumn, c.ProductosCategoriaColumn };

        await foreach (var (row, v) in ReadAsync(c.ProductosPath, "productos", columns, cancellationToken))
            yield return new RawProducto(row, v[0], v[1], v[2]);
    }

    private async IAsyncEnumerable<(int Row, string?[] Values)> ReadAsync(
        string? path, string name, string[] columns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Etl:Catalogs: falta la ruta del catálogo de {name}.");

        var full = PathResolver.Resolve(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"No se encontró el catálogo de {name}.", full);

        logger.LogInformation("[Catálogo {Nombre}] Leyendo {Ruta}", name, full);

        using var reader = new StreamReader(full, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, MissingFieldFound = null };
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync()) yield break;
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        foreach (var column in columns)
            if (!headers.Contains(column))
                throw new InvalidDataException(
                    $"El catálogo de {name} no tiene la columna '{column}'. Columnas encontradas: {string.Join(", ", headers)}.");

        var row = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            row++;
            var values = new string?[columns.Length];
            for (var i = 0; i < columns.Length; i++)
            {
                var raw = csv.GetField(columns[i]);
                values[i] = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            }
            yield return (row, values);
        }
    }
}
