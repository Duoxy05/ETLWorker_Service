using System.Globalization;
using System.Text;
using CsvHelper;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Services;
using ETLWorker.Application.Settings;
using ETLWorker.Infrastructure.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETLWorker.Infrastructure.Staging;

/// <summary>Carga los IDs válidos de clientes y productos para detectar referencias huérfanas (sin rechazar filas).</summary>
public sealed class CsvReferenceCatalogProvider(IOptions<EtlOptions> options, ILogger<CsvReferenceCatalogProvider> logger)
    : IReferenceCatalogProvider
{
    public async Task<ReferenceCatalog> LoadAsync(CancellationToken ct)
    {
        var c = options.Value.Catalogs;
        var clientes = await LoadIdsAsync(c.ClientesPath, c.ClientesIdColumn, "clientes", ct);
        var productos = await LoadIdsAsync(c.ProductosPath, c.ProductosIdColumn, "productos", ct);
        return new ReferenceCatalog { Clientes = clientes, Productos = productos };
    }

    private async Task<HashSet<int>?> LoadIdsAsync(string? path, string column, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogWarning("Catálogo de {Nombre} no configurado: no se validará existencia.", name);
            return null;
        }
        var full = PathResolver.Resolve(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"No se encontró el catálogo de {name}.", full);

        using var reader = new StreamReader(full, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        await csv.ReadAsync();
        csv.ReadHeader();
        var ids = new HashSet<int>();
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (IdNormalizer.TryNormalize(csv.GetField(column), out var id)) ids.Add(id);
        }
        logger.LogInformation("Catálogo de {Nombre}: {N} IDs cargados.", name, ids.Count);
        return ids;
    }
}
