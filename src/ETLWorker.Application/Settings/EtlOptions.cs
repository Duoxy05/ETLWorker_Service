using ETLWorker.Domain.Enums;

namespace ETLWorker.Application.Settings;

/// <summary>Configuración completa del ETL (sección "Etl" de appsettings.json).</summary>
public sealed class EtlOptions
{
    public const string SectionName = "Etl";

    public int MaxDegreeOfParallelism { get; set; } = 3;
    public int BatchSize { get; set; } = 500;
    public int ChannelCapacity { get; set; } = 2000;
    /// <summary>0 = ejecutar una sola vez y terminar; &gt;0 = repetir cada N minutos.</summary>
    public int IntervalMinutes { get; set; } = 0;
    /// <summary>true = extrae las fuentes a staging; false = solo carga dimensiones con el staging que ya existe.</summary>
    public bool RunExtraction { get; set; } = true;
    public CatalogOptions Catalogs { get; set; } = new();
    public DimensionOptions Dimensions { get; set; } = new();
    public List<SourceOptions> Sources { get; set; } = new();
}

public sealed class CatalogOptions
{
    public string? ClientesPath { get; set; }
    public string ClientesIdColumn { get; set; } = "IdCliente";
    public string? ProductosPath { get; set; }
    public string ProductosIdColumn { get; set; } = "IdProducto";

    // Columnas usadas al cargar Dim_Cliente y Dim_Producto desde los catálogos.
    public string ClientesNombreColumn { get; set; } = "Nombre";
    public string ClientesEmailColumn { get; set; } = "Email";
    public string ProductosNombreColumn { get; set; } = "Nombre";
    public string ProductosCategoriaColumn { get; set; } = "Categoría";
}

/// <summary>Carga de las dimensiones del almacén de datos (sección "Etl:Dimensions").</summary>
public sealed class DimensionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Años que cubre Dim_Fecha (el modelo define el calendario 2024–2025).</summary>
    public int CalendarStartYear { get; set; } = 2024;
    public int CalendarEndYear { get; set; } = 2025;

    /// <summary>Combinaciones Canal/Plataforma de Dim_Fuente.</summary>
    public List<FuenteMapOptions> Fuentes { get; set; } = new();
}

/// <summary>
/// Una fila (o grupo de filas) de Dim_Fuente. Si <see cref="Plataforma"/> viene fija se usa tal cual;
/// si no, las plataformas se descubren en <see cref="StagingTable"/> (valores distintos de PlataformaRaw).
/// </summary>
public sealed class FuenteMapOptions
{
    public string Canal { get; set; } = "";
    public string? Plataforma { get; set; }
    public string? StagingTable { get; set; }
}

/// <summary>Una fuente de datos. Agregar una nueva = agregar un bloque en el JSON.</summary>
public sealed class SourceOptions
{
    public string Name { get; set; } = "";
    public SourceType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public string StagingTable { get; set; } = "";
    public FieldMap Fields { get; set; } = new();
    public CsvSourceOptions? Csv { get; set; }
    public DatabaseSourceOptions? Database { get; set; }
    public ApiSourceOptions? Api { get; set; }
}

/// <summary>Nombre del campo en la fuente para cada campo canónico. Null = la fuente no lo tiene.</summary>
public sealed class FieldMap
{
    public string IdOrigen { get; set; } = "";
    public string IdCliente { get; set; } = "";
    public string IdProducto { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Comentario { get; set; } = "";
    public string? Puntaje { get; set; }
    public string? Clasificacion { get; set; }
    public string? Plataforma { get; set; }
}

public sealed class CsvSourceOptions
{
    public string Path { get; set; } = "";
    public string Delimiter { get; set; } = ",";
    public string Encoding { get; set; } = "utf-8";
}

public sealed class DatabaseSourceOptions
{
    /// <summary>Nombre dentro de ConnectionStrings (la cadena real vive en secretos).</summary>
    public string ConnectionName { get; set; } = "";
    public string Query { get; set; } = "";
    public int CommandTimeoutSeconds { get; set; } = 60;
}

public sealed class ApiSourceOptions
{
    public string BaseUrl { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public int PageSize { get; set; } = 100;
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
    public string ApiKeyHeader { get; set; } = "X-Api-Key";
    /// <summary>Nombre del secreto (Secrets:{ApiKeySecret}) que contiene la API key.</summary>
    public string? ApiKeySecret { get; set; }
    public string DataProperty { get; set; } = "data";
    public string TotalProperty { get; set; } = "total";
}
