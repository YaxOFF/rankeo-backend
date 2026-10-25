using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Rankeo.Api.Tests;

public class ArranqueTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task La_api_arranca_contra_postgres_real()
    {
        using var client = api.CreateClient();
        var cadena = api.Services.GetRequiredService<IConfiguration>().GetConnectionString("Rankeo");
        Assert.Equal(api.ConnectionString, cadena);

        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("select 1", conexion);
        Assert.Equal(1, await comando.ExecuteScalarAsync());
    }
}
