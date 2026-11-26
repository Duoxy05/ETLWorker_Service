using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Services;
using ETLWorker.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ETLWorker.Application.Dimensions;

/// <summary>
/// Dim_Cliente: catálogo de clientes + miembros especiales (-1 Anónimo, -2 Cliente no encontrado).
/// Valida y normaliza cada fila; las inválidas se rechazan (con log) sin detener la carga.
/// País, edad y tipo no existen en el catálogo: quedan en NULL ("cuando existan", según el modelo).
/// </summary>
public sealed class DimClienteLoader(
    ICatalogSource catalog,
    IDimensionRepository repository,
    ILogger<DimClienteLoader> logger) : IDimensionLoader
{
    public string Name => "Dim_Cliente";

    public async Task<DimensionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = new List<DimClienteRow>
        {
            new(SpecialMembers.ClienteAnonimo, "Anónimo", SpecialMembers.AnonimoEmail, null, null, null, true),
            new(SpecialMembers.ClienteNoEncontrado, "Cliente no encontrado", SpecialMembers.ClienteNoEncontradoEmail, null, null, null, true)
        };

        var ids = new HashSet<int>();
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long read = 0, rejected = 0;

        await foreach (var raw in catalog.ReadClientesAsync(cancellationToken))
        {
            read++;

            if (!IdNormalizer.TryNormalize(raw.Id, out var id) || id <= 0)
            {
                rejected++;
                logger.LogWarning("[Dim_Cliente] Fila {Fila} rechazada: IdCliente inválido ('{Id}').", raw.NumeroFila, raw.Id);
                continue;
            }
            if (string.IsNullOrWhiteSpace(raw.Nombre))
            {
                rejected++;
                logger.LogWarning("[Dim_Cliente] Fila {Fila} rechazada: el cliente {Id} no tiene nombre.", raw.NumeroFila, id);
                continue;
            }
            if (!ids.Add(id))
            {
                rejected++;
                logger.LogWarning("[Dim_Cliente] Fila {Fila} rechazada: IdCliente {Id} duplicado en el catálogo.", raw.NumeroFila, id);
                continue;
            }

            // El correo es UNIQUE en la dimensión: si se repite, se conserva el primero y los demás quedan sin correo.
            var email = raw.Email;
            if (email is not null && !emails.Add(email))
            {
                logger.LogWarning("[Dim_Cliente] Cliente {Id}: el correo '{Email}' ya pertenece a otro cliente; se guarda sin correo.", id, email);
                email = null;
            }

            rows.Add(new DimClienteRow(id, raw.Nombre.Trim(), email, null, null, null, false));
        }

        logger.LogInformation("[Dim_Cliente] Catálogo leído: {Leidas} filas, {Rechazadas} rechazadas, {Especiales} miembros especiales.",
            read, rejected, 2);

        var load = await repository.LoadClientesAsync(rows, cancellationToken);
        return new DimensionLoadResult(read, rejected, load);
    }
}
