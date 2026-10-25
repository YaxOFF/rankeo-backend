using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rankeo.Api.Data;
using Rankeo.Api.Dominio;

namespace Rankeo.Api.Tests;

public class ImagenesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);

    async Task<string> Sala()
    {
        var r = await api.CreateClient().PostAsJsonAsync("/api/salas", new { title = "x" });
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
    }

    Task<HttpResponseMessage> Subir(string codigo, byte[] bytes, string? nombre = "Gato", string apodo = "Ana")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "archivo", "gato.png" }, { new StringContent(apodo), "apodo" } };
        if (nombre is not null) form.Add(new StringContent(nombre), "nombre");
        return api.CreateClient().PostAsync($"/api/salas/{codigo}/imagenes", form);
    }

    static async Task<string?> Detalle(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    string Archivo(string url) => Path.Combine(api.ImagenesDir, url["/imagenes/".Length..].Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task Subir_guarda_el_archivo_lo_sirve_con_CORS_y_avisa_a_la_sala()
    {
        var codigo = await Sala();
        await using var b = await api.Hub();
        await b.InvokeAsync<JsonElement>("Unirse", codigo, "Beto");
        var estado = new TaskCompletionSource<JsonElement>();
        var actividad = new TaskCompletionSource<JsonElement>();
        b.On<JsonElement>("state", e => estado.TrySetResult(e));
        b.On<JsonElement>("activity", e => actividad.TrySetResult(e));

        var r = await Subir(codigo.ToLowerInvariant(), Png);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var item = await r.Content.ReadFromJsonAsync<JsonElement>();
        var url = item.GetProperty("url").GetString()!;
        Assert.Equal("Gato", item.GetProperty("name").GetString());
        Assert.Equal("Ana", item.GetProperty("by").GetString());
        Assert.Matches($"^/imagenes/{codigo}/[0-9a-f]{{32}}\\.png$", url);
        Assert.Equal(Png, await File.ReadAllBytesAsync(Archivo(url)));

        var s = await estado.Task.WaitAsync(Espera);
        Assert.Equal([item.GetProperty("id").GetString()], s.GetProperty("pool").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(1, s.GetProperty("version").GetInt64());
        Assert.Equal("subió Gato", (await actividad.Task.WaitAsync(Espera))[0].GetProperty("text").GetString());

        var get = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { { "Origin", ApiFactory.Origen } } };
        var img = await api.CreateClient().SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, img.StatusCode);
        Assert.Equal("image/png", img.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiFactory.Origen, img.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("immutable", img.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Sin_nombre_usa_Imagen()
    {
        var r = await Subir(await Sala(), Png, nombre: "  ");
        Assert.Equal("Imagen", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rechaza_firma_invalida_tamano_nombre_apodo_y_sala_inexistente()
    {
        var codigo = await Sala();
        async Task Falla(HttpResponseMessage r, HttpStatusCode status, string detalle)
        {
            Assert.Equal(status, r.StatusCode);
            Assert.Equal(detalle, await Detalle(r));
        }
        await Falla(await Subir(codigo, "<svg></svg>"u8.ToArray()), HttpStatusCode.BadRequest, "Formato no admitido: usa webp, png, jpeg o gif.");
        await Falla(await Subir(codigo, [.. Png, .. new byte[512 * 1024]]), HttpStatusCode.BadRequest, "La imagen admite máximo 512 KB.");
        await Falla(await Subir(codigo, Png, nombre: new string('x', 31)), HttpStatusCode.BadRequest, "El nombre admite máximo 30 caracteres.");
        await Falla(await Subir(codigo, Png, apodo: " "), HttpStatusCode.BadRequest, "El apodo debe tener entre 1 y 30 caracteres.");
        await Falla(await Subir("ZZZZZZ", Png), HttpStatusCode.NotFound, "No existe ninguna sala con el código ZZZZZZ.");
        Assert.False(Directory.Exists(Path.Combine(api.ImagenesDir, "ZZZZZZ")));
    }

    [Fact]
    public async Task Tope_de_200_imagenes_no_deja_archivo_huerfano()
    {
        var codigo = await Sala();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RankeoDb>();
            var sala = await db.Salas.SingleAsync(s => s.Codigo == codigo);
            var items = Enumerable.Range(0, 200).Select(i => new Item($"i{i}", "x", "/x")).ToDictionary(i => i.Id);
            sala.Estado = sala.Estado with { Items = items, Pool = [.. items.Keys] };
            await db.SaveChangesAsync();
        }

        var r = await Subir(codigo, Png);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Máximo 200 imágenes por sala.", await Detalle(r));
        Assert.Empty(Directory.GetFiles(Path.Combine(api.ImagenesDir, codigo)));
        using var s2 = api.Services.CreateScope();
        Assert.False(await s2.ServiceProvider.GetRequiredService<RankeoDb>().Imagenes.AnyAsync(i => i.SalaCodigo == codigo));
    }

    [Fact]
    public async Task Item_delete_borra_el_archivo_y_la_fila()
    {
        var codigo = await Sala();
        var item = await (await Subir(codigo, Png)).Content.ReadFromJsonAsync<JsonElement>();
        var ruta = Archivo(item.GetProperty("url").GetString()!);
        Assert.True(File.Exists(ruta));

        await using var a = await api.Hub();
        await a.InvokeAsync<JsonElement>("Unirse", codigo, "Ana");
        await a.InvokeAsync("Despachar", new { type = "item:delete", id = item.GetProperty("id").GetString() });

        Assert.False(File.Exists(ruta));
        using var scope = api.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<RankeoDb>().Imagenes.AnyAsync(i => i.SalaCodigo == codigo));
    }
}
