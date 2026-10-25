using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rankeo.Api.Data;

namespace Rankeo.Api.Tests;

// Clase propia: "derivadas" comparte el límite "crear" (10/min por IP) con SalasHttpTests.
public class DerivadasTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    async Task<JsonElement> Sala(string titulo)
    {
        var r = await api.CreateClient().PostAsJsonAsync("/api/salas", new { title = titulo });
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    async Task<JsonElement> Subir(string codigo, string nombre, string? subtitulo = null)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Png), "archivo", "x.png" }, { new StringContent("Ana"), "apodo" }, { new StringContent(nombre), "nombre" },
        };
        if (subtitulo is not null) form.Add(new StringContent(subtitulo), "subtitulo");
        return await (await api.CreateClient().PostAsync($"/api/salas/{codigo}/imagenes", form)).Content.ReadFromJsonAsync<JsonElement>();
    }

    string Archivo(string url) => Path.Combine(api.ImagenesDir, url["/imagenes/".Length..].Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task Derivar_copia_filas_e_imagenes_al_banco_con_archivos_propios()
    {
        var padre = await Sala("Pelis");
        var codigo = padre.GetProperty("code").GetString()!;
        var filas = padre.GetProperty("tiers").EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToArray();
        var gato = await Subir(codigo, "Gato", "el mejor");
        var perro = await Subir(codigo, "Perro");

        await using var a = await api.Hub();
        await a.InvokeAsync<JsonElement>("Unirse", codigo, "Ana");
        await a.InvokeAsync("Despachar", new { type = "item:move", id = perro.GetProperty("id").GetString(), target = filas[0] });
        await a.InvokeAsync("Despachar", new { type = "tier:update", id = filas[0], label = "Top", glow = 2 });

        var r = await api.CreateClient().PostAsJsonAsync($"/api/salas/{codigo.ToLowerInvariant()}/derivadas", new { title = "Pelis 2", apodo = "Ana" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var hija = await r.Content.ReadFromJsonAsync<JsonElement>();
        var nuevo = hija.GetProperty("code").GetString()!;

        Assert.NotEqual(codigo, nuevo);
        Assert.Equal("Pelis 2", hija.GetProperty("title").GetString());
        Assert.Equal(codigo, hija.GetProperty("parent").GetString());
        Assert.Equal(0, hija.GetProperty("version").GetInt64());

        // Mismas filas (etiqueta, color, brillo) con ids nuevos y vacías.
        var tiers = hija.GetProperty("tiers").EnumerateArray().ToList();
        Assert.Equal(["Top", "A", "B", "C", "D"], tiers.Select(t => t.GetProperty("label").GetString()));
        Assert.Equal(2, tiers[0].GetProperty("glow").GetInt32());
        Assert.All(tiers, t => Assert.Empty(t.GetProperty("items").EnumerateArray()));
        Assert.Empty(tiers.Select(t => t.GetProperty("id").GetString()).Intersect(filas));

        // Todas las imágenes en el banco (primero las de las filas, luego las del banco), con ids y archivos nuevos.
        var pool = hija.GetProperty("pool").EnumerateArray().Select(p => p.GetString()!).ToList();
        var items = hija.GetProperty("items");
        Assert.Equal(["Perro", "Gato"], pool.Select(id => items.GetProperty(id).GetProperty("name").GetString()));
        Assert.Equal("el mejor", items.GetProperty(pool[1]).GetProperty("caption").GetString());
        Assert.Equal("Ana", items.GetProperty(pool[1]).GetProperty("by").GetString());
        Assert.DoesNotContain(gato.GetProperty("id").GetString(), pool);
        foreach (var id in pool)
        {
            var url = items.GetProperty(id).GetProperty("url").GetString()!;
            Assert.StartsWith($"/imagenes/{nuevo}/", url);
            Assert.Equal(Png, await File.ReadAllBytesAsync(Archivo(url)));
        }

        // Bitácora de la nueva sala y padre intacto.
        await using var b = await api.Hub();
        var unida = await b.InvokeAsync<JsonElement>("Unirse", nuevo, "Beto");
        Assert.Equal("creó esta sala a partir de " + codigo, unida.GetProperty("activity")[0].GetProperty("text").GetString());
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RankeoDb>();
        Assert.Equal(2, await db.Imagenes.CountAsync(i => i.SalaCodigo == nuevo));
        Assert.Equal([perro.GetProperty("id").GetString()], (await db.Salas.SingleAsync(s => s.Codigo == codigo)).Estado.Tiers[0].Items);
        Assert.True(File.Exists(Archivo(gato.GetProperty("url").GetString()!)));
    }

    [Fact]
    public async Task Derivar_sin_titulo_hereda_el_del_padre_y_valida_sala_y_titulo()
    {
        var codigo = (await Sala("Pelis")).GetProperty("code").GetString()!;
        var hija = await (await api.CreateClient().PostAsJsonAsync($"/api/salas/{codigo}/derivadas", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pelis", hija.GetProperty("title").GetString());
        Assert.Empty(hija.GetProperty("pool").EnumerateArray());

        var r = await api.CreateClient().PostAsJsonAsync("/api/salas/ZZZZZZ/derivadas", new { title = "x" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        r = await api.CreateClient().PostAsJsonAsync($"/api/salas/{codigo}/derivadas", new { title = new string('x', 81) });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("El título admite máximo 80 caracteres.", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }
}
