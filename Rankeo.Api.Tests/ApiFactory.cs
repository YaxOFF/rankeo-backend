using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
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

    /// <summary>Conexión al hub contra el TestServer (long polling: el TestServer no abre sockets reales).</summary>
    public async Task<HubConnection> Hub()
    {
        var conexion = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/sala"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await conexion.StartAsync();
        return conexion;
    }

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
