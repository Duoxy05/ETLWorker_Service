using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ETLWorker.Application.Abstractions;
using ETLWorker.Application.Settings;
using ETLWorker.Domain.Entities;
using ETLWorker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ETLWorker.Infrastructure.Extractors;

/// <summary>Consume una API REST paginada con IHttpClientFactory, con reintentos y backoff exponencial.</summary>
public sealed class ApiExtractor(IHttpClientFactory httpFactory, ILogger<ApiExtractor> logger) : IExtractor
{
    public SourceType Type => SourceType.Api;

    public async IAsyncEnumerable<RawOpinion> ExtractAsync(
        SourceOptions source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opt = source.Api ?? throw new InvalidOperationException($"La fuente '{source.Name}' no tiene sección Api.");
        var client = httpFactory.CreateClient(source.Name);   // cliente nombrado: URL, timeout y API key ya configurados
        var f = source.Fields;
        var page = 1; var fetched = 0; var row = 0;

        while (true)
        {
            using var doc = await FetchPageAsync(client, opt, source.Name, page, cancellationToken);
            var root = doc.RootElement;
            var data = root.ValueKind == JsonValueKind.Array ? root : Find(root, opt.DataProperty);
            if (data is not { ValueKind: JsonValueKind.Array } items)
                throw new InvalidDataException($"La respuesta de '{source.Name}' no contiene el arreglo '{opt.DataProperty}'.");

            var count = 0;
            foreach (var el in items.EnumerateArray())
            {
                count++; row++;
                yield return new RawOpinion
                {
                    Fuente = source.Name,
                    NumeroFila = row,
                    IdOrigen = Prop(el, f.IdOrigen),
                    IdClienteRaw = Prop(el, f.IdCliente),
                    IdProductoRaw = Prop(el, f.IdProducto),
                    FechaRaw = Prop(el, f.Fecha),
                    ComentarioRaw = Prop(el, f.Comentario),
                    PuntajeRaw = Prop(el, f.Puntaje),
                    ClasificacionRaw = Prop(el, f.Clasificacion),
                    PlataformaRaw = Prop(el, f.Plataforma)
                };
            }

            fetched += count;
            logger.LogDebug("[{Fuente}] Página {Pagina}: {N} elementos.", source.Name, page, count);
            if (count == 0 || count < opt.PageSize) break;
            if (Find(root, opt.TotalProperty) is { ValueKind: JsonValueKind.Number } t && t.TryGetInt32(out var total) && fetched >= total) break;
            page++;
        }
    }

    private async Task<JsonDocument> FetchPageAsync(HttpClient client, ApiSourceOptions opt, string name, int page, CancellationToken ct)
    {
        var url = $"{opt.Endpoint}?page={page}&pageSize={opt.PageSize}";
        for (var attempt = 0; ; attempt++)
        {
            string failure;
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }
                if (!IsTransient(response.StatusCode))
                    throw new HttpRequestException($"La API de '{name}' respondió {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
                failure = $"HTTP {(int)response.StatusCode}";
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null) { failure = ex.Message; }          // red caída
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { failure = "tiempo de espera agotado"; }

            if (attempt >= opt.MaxRetries)
                throw new HttpRequestException($"La API de '{name}' no respondió tras {attempt + 1} intentos: {failure}.");
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            logger.LogWarning("[{Fuente}] Fallo transitorio ({Fallo}). Reintento {N}/{Max} en {S}s.", name, failure, attempt + 1, opt.MaxRetries, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static JsonElement? Find(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? Prop(JsonElement el, string? name)
    {
        if (name is null || Find(el, name) is not { } v) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => v.GetString(),
            _ => v.GetRawText()
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
