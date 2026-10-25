using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Rankeo.Api.Tests;

public class SalasHttpTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    // Ojo: el límite de "crear" es 10/min por IP y todas estas pruebas comparten la IP vacía del TestServer.
    async Task<JsonElement> Crear(string? titulo)
    {
        var r = await api.CreateClient().PostAsJsonAsync("/api/salas", new { title = titulo });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    static async Task<string?> Detalle(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    [Fact]
    public async Task Crear_con_titulo_vacio_usa_el_titulo_por_defecto_y_5_filas()
    {
        var sala = await Crear("");
        Assert.Equal("Mi Tier List", sala.GetProperty("title").GetString());
        Assert.Equal(["S", "A", "B", "C", "D"], sala.GetProperty("tiers").EnumerateArray().Select(t => t.GetProperty("label").GetString()));
        Assert.Equal(["#c96063", "#dba06d", "#dcc86c", "#89d07a", "#74bdd6"], sala.GetProperty("tiers").EnumerateArray().Select(t => t.GetProperty("color").GetString()));
        Assert.Empty(sala.GetProperty("pool").EnumerateArray());
        Assert.Empty(sala.GetProperty("items").EnumerateObject());
        Assert.Equal(0, sala.GetProperty("version").GetInt64());
        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", sala.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Crear_guarda_el_titulo()
    {
        var sala = await Crear("Pelis de terror");
        Assert.Equal("Pelis de terror", sala.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Titulo_de_mas_de_80_da_400_con_problem_details()
    {
        var r = await api.CreateClient().PostAsJsonAsync("/api/salas", new { title = new string('x', 81) });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("El título admite máximo 80 caracteres.", await Detalle(r));
    }

    [Fact]
    public async Task Existe_da_204_y_normaliza_el_codigo_de_la_ruta()
    {
        var codigo = (await Crear("x")).GetProperty("code").GetString()!;
        var cliente = api.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.GetAsync($"/api/salas/{codigo}")).StatusCode);
        var feo = Uri.EscapeDataString($"  {codigo.ToLowerInvariant().Insert(3, "-")} ");
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.GetAsync($"/api/salas/{feo}")).StatusCode);
    }

    [Fact]
    public async Task Sala_inexistente_da_404_con_el_mensaje_de_la_demo()
    {
        var r = await api.CreateClient().GetAsync("/api/salas/zz-zz-zz");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("No existe ninguna sala con el código ZZZZZZ.", await Detalle(r));
    }

    [Fact]
    public async Task Cors_permite_el_origen_configurado_con_credenciales()
    {
        var pedido = new HttpRequestMessage(HttpMethod.Options, "/api/salas");
        pedido.Headers.Add("Origin", ApiFactory.Origen);
        pedido.Headers.Add("Access-Control-Request-Method", "POST");
        var r = await api.CreateClient().SendAsync(pedido);
        Assert.Equal(ApiFactory.Origen, r.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", r.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var otro = new HttpRequestMessage(HttpMethod.Get, "/api/salas/ZZZZZZ");
        otro.Headers.Add("Origin", "http://malo.test");
        Assert.False((await api.CreateClient().SendAsync(otro)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Crear_mas_de_10_por_minuto_desde_la_misma_ip_da_429()
    {
        var ip = IPAddress.Parse("10.0.0.7");
        var estados = new List<int>();
        for (var i = 0; i < 11; i++)
        {
            var http = await api.Server.SendAsync(c =>
            {
                c.Connection.RemoteIpAddress = ip;
                c.Request.Method = "POST";
                c.Request.Path = "/api/salas";
                c.Request.ContentType = "application/json";
                c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
            });
            estados.Add(http.Response.StatusCode);
        }
        Assert.All(estados[..10], s => Assert.Equal(201, s));
        Assert.Equal(429, estados[10]);
    }
}
