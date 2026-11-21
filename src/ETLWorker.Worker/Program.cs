using ETLWorker.Application;
using ETLWorker.Application.Services;
using ETLWorker.Application.Settings;
using ETLWorker.Infrastructure;
using ETLWorker.Worker;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Logging estructurado: ILogger<T> en todo el código, Serilog como proveedor (consola + archivo diario).
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Configuración tipada y validada al arrancar (falla rápido si el JSON está mal).
builder.Services.AddSingleton<IValidateOptions<EtlOptions>, EtlOptionsValidator>();
builder.Services.AddOptions<EtlOptions>()
    .Bind(builder.Configuration.GetSection(EtlOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<EtlWorker>();

await builder.Build().RunAsync();
return Environment.ExitCode;
