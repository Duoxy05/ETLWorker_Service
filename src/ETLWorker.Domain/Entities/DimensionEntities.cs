using ETLWorker.Domain.Enums;

namespace ETLWorker.Domain.Entities;

// ---- Filas de las dimensiones del modelo estrella (SistemaOpinionesDW) ----

public sealed record DimClienteRow(
    int IdCliente, string Nombre, string? Email, string? Pais, int? Edad, string? TipoCliente, bool EsEspecial);

public sealed record DimProductoRow(int IdProducto, string Nombre, string? Categoria, bool EsEspecial);

/// <summary>El IdFuente lo asigna el repositorio: la clave natural es (Canal, Plataforma).</summary>
public sealed record DimFuenteRow(string Canal, string Plataforma);

public sealed record DimFechaRow(
    int IdFecha, DateTime Fecha, int Anio, int Trimestre, int Mes, string NombreMes, string AnioMes, int Dia);

public sealed record DimSentimientoRow(int IdSentimiento, string Nombre, int Polaridad);

// ---- Filas crudas de los catálogos (todo texto, igual que en staging) ----

public sealed record RawCliente(int NumeroFila, string? Id, string? Nombre, string? Email);

public sealed record RawProducto(int NumeroFila, string? Id, string? Nombre, string? Categoria);

// ---- Resultados ----

/// <summary>
/// Respuesta de un procedimiento almacenado de carga: cuántas filas insertó, cuántas ya estaban registradas
/// (no se tocan), el código de retorno (0 insertó, 1 ya registrada, 2 sin filas) y su mensaje.
/// </summary>
public sealed record LoadResult(long Inserted, long AlreadyRegistered, int ReturnCode, string? Message)
{
    public static LoadResult Empty => new(0, 0, 2, null);
}

/// <summary>Lo que devuelve cada cargador de dimensión; el orquestador lo convierte en <see cref="DimensionLoadSummary"/>.</summary>
public sealed record DimensionLoadResult(long Read, long Rejected, LoadResult Load);

public sealed record DimensionLoadSummary(
    string Dimension, LoadStatus Estado,
    long FilasLeidas, long Rechazadas, long Insertadas, long YaRegistradas, int CodigoRetorno,
    long DuracionMs, string? Mensaje);

/// <summary>
/// Miembros especiales del modelo (tarea de Modelado): dan una clave válida a las opiniones con
/// datos faltantes, así las 582 opiniones con ID vacío o inexistente no se pierden en la carga de hechos.
/// </summary>
public static class SpecialMembers
{
    public const int ClienteAnonimo = -1;
    public const int ClienteNoEncontrado = -2;
    public const int ProductoNoEncontrado = -1;
    public const int FechaDesconocida = -1;

    // Dim_Cliente.Email es UNIQUE: cada miembro especial lleva un correo ficticio propio.
    public const string AnonimoEmail = "anonimo@dw.local";
    public const string ClienteNoEncontradoEmail = "cliente-no-encontrado@dw.local";
}
