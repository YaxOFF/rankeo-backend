using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Rankeo.Api.Data;
using Rankeo.Api.Hubs;
using Rankeo.Api.Services;

CargarEnv();

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Rankeo")))
    throw new InvalidOperationException("Falta ConnectionStrings__Rankeo (ver .env.example).");

builder.Services.AddDbContext<RankeoDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Rankeo")));
builder.Services.AddScoped<SalaService>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErroresHandler>();
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    // El cliente no garantiza que "type" vaya primero; y null/campos faltantes se rechazan al deserializar.
    o.PayloadSerializerOptions.AllowOutOfOrderMetadataProperties = true;
    o.PayloadSerializerOptions.RespectNullableAnnotations = true;
    o.PayloadSerializerOptions.RespectRequiredConstructorParameters = true;
});

// Orígenes explícitos (nunca *): SignalR necesita AllowCredentials.
var origenes = (builder.Configuration["CORS_ORIGINS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ponytail: por IP directa; detrás de un proxy hace falta UseForwardedHeaders.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    void PorIp(string nombre, int porMinuto) => o.AddPolicy(nombre, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = porMinuto, Window = TimeSpan.FromMinutes(1) }));
    PorIp("crear", 10);
    PorIp("existe", 30);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<RankeoDb>().Database.Migrate();

app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<SalaHub>("/hubs/sala");

app.Run();

// Lee el primer .env que encuentre (carpeta actual o padres) sin pisar variables ya definidas.
static void CargarEnv()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var ruta = Path.Combine(dir.FullName, ".env");
        if (!File.Exists(ruta)) continue;
        foreach (var linea in File.ReadAllLines(ruta))
        {
            var i = linea.IndexOf('=');
            if (i < 1 || linea.TrimStart().StartsWith('#')) continue;
            var clave = linea[..i].Trim();
            if (Environment.GetEnvironmentVariable(clave) is null)
                Environment.SetEnvironmentVariable(clave, linea[(i + 1)..].Trim());
        }
        return;
    }
}
