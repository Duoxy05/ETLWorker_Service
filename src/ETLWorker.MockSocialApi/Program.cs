// API REST simulada de redes sociales (solo para desarrollo/demostración).
// Sirve social_comments.csv paginado y protegido con X-Api-Key, como lo haría una API real.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CsvHelper;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var apiKey = builder.Configuration["ApiKey"]
    ?? throw new InvalidOperationException("Falta la configuración 'ApiKey'.");
var csvPath = Path.Combine(AppContext.BaseDirectory, "data", "social_comments.csv");
var comments = LoadComments(csvPath);

app.MapGet("/health", () => Results.Ok(new { status = "ok", total = comments.Count }));

app.MapGet("/api/comments", (HttpContext ctx, int? page, int? pageSize) =>
{
    var provided = ctx.Request.Headers["X-Api-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(apiKey)))
        return Results.Unauthorized();

    var p = Math.Max(page ?? 1, 1);
    var size = Math.Clamp(pageSize ?? 50, 1, 1000);
    var data = comments.Skip((p - 1) * size).Take(size).ToList();
    return Results.Ok(new { page = p, pageSize = size, total = comments.Count, data });
});

app.Run();

static List<SocialComment> LoadComments(string path)
{
    var list = new List<SocialComment>();
    using var reader = new StreamReader(path, Encoding.UTF8, true);
    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
    csv.Read();
    csv.ReadHeader();
    while (csv.Read())
    {
        list.Add(new SocialComment(
            csv.GetField("IdComment")!, Blank(csv.GetField("IdCliente")), Blank(csv.GetField("IdProducto")),
            csv.GetField("Fuente")!, csv.GetField("Fecha")!, csv.GetField("Comentario")!));
    }
    return list;
}

static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

record SocialComment(string IdComment, string? IdCliente, string? IdProducto, string Fuente, string Fecha, string Comentario);
