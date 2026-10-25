using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

// Cada ApiFactory pasa su cadena por una variable de entorno del proceso: las clases no pueden correr a la vez.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rankeo.Api.Tests;

// API real (TestServer) contra un Postgres real en contenedor; una instancia por clase de prueba.
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public const string Origen = "http://front.test";

    public string ConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // Gana sobre el .env local: CargarEnv no pisa variables ya definidas.
        Environment.SetEnvironmentVariable("ConnectionStrings__Rankeo", ConnectionString);
        Environment.SetEnvironmentVariable("CORS_ORIGINS", Origen);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}
