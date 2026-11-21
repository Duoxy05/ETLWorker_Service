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
    public CatalogOptions Catalogs { get; set; } = new();
    public List<SourceOptions> Sources { get; set; } = new();
}

public sealed class CatalogOptions
{
    public string? ClientesPath { get; set; }
    public string ClientesIdColumn { get; set; } = "IdCliente";
    public string? ProductosPath { get; set; }
    public string ProductosIdColumn { get; set; } = "IdProducto";
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
