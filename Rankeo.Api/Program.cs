CargarEnv();

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Rankeo")))
    throw new InvalidOperationException("Falta ConnectionStrings__Rankeo (ver .env.example).");

var app = builder.Build();

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
