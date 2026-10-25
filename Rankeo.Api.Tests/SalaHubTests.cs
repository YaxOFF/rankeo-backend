using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rankeo.Api.Data;
using Rankeo.Api.Dominio;
using Rankeo.Api.Services;

namespace Rankeo.Api.Tests;

public class SalaHubTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);

    // Sala nueva con dos imágenes en el banco (item:add directo al servicio: por el hub no se permite).
    async Task<(string Codigo, string[] Filas)> SalaConImagenes()
    {
        var r = await api.CreateClient().PostAsJsonAsync("/api/salas", new { title = "x" });
        var sala = await r.Content.ReadFromJsonAsync<JsonElement>();
        var codigo = sala.GetProperty("code").GetString()!;
        using var scope = api.Services.CreateScope();
        var salas = scope.ServiceProvider.GetRequiredService<SalaService>();
        await salas.Aplicar(codigo, new ItemAdd(new Item("i1", "Gato", "/x/i1")), "Ana");
        await salas.Aplicar(codigo, new ItemAdd(new Item("i2", "Perro", "/x/i2")), "Ana");
        return (codigo, sala.GetProperty("tiers").EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToArray());
    }

    static Task<JsonElement> Siguiente(HubConnection c, string evento)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? sub = null;
        sub = c.On<JsonElement>(evento, e => { sub?.Dispose(); tcs.TrySetResult(e); });
        return tcs.Task.WaitAsync(Espera);
    }

    [Fact]
    public async Task Unirse_devuelve_sala_y_bitacora()
    {
        var (codigo, _) = await SalaConImagenes();
        await using var a = await api.Hub();
        var r = await a.InvokeAsync<JsonElement>("Unirse", codigo.ToLowerInvariant(), "Ana");
        Assert.Equal(codigo, r.GetProperty("room").GetProperty("code").GetString());
        Assert.Equal(2, r.GetProperty("room").GetProperty("version").GetInt64());
        var act = r.GetProperty("activity").EnumerateArray().ToList();
        Assert.Equal(["subió Perro", "subió Gato"], act.Select(e => e.GetProperty("text").GetString()));
        Assert.Equal("Ana", act[0].GetProperty("who").GetProperty("name").GetString());
        Assert.Equal(Calculos.ColorPara("Ana"), act[0].GetProperty("who").GetProperty("color").GetString());
        Assert.True(act[0].GetProperty("at").GetInt64() > 1_700_000_000_000);
    }

    [Fact]
    public async Task Sala_inexistente_o_apodo_vacio_da_HubException()
    {
        await using var a = await api.Hub();
        var e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync<JsonElement>("Unirse", "ZZZZZZ", "Ana"));
        Assert.Contains("No existe ninguna sala con el código ZZZZZZ.", e.Message);
        var (codigo, _) = await SalaConImagenes();
        await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync<JsonElement>("Unirse", codigo, "  "));
        e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "title:update", title = "y" }));
        Assert.Contains("Primero únete a una sala.", e.Message);
    }

    [Fact]
    public async Task A_mueve_y_B_recibe_state_y_activity()
    {
        var (codigo, filas) = await SalaConImagenes();
        await using var a = await api.Hub();
        await using var b = await api.Hub();
        await a.InvokeAsync<JsonElement>("Unirse", codigo, "Ana");
        await b.InvokeAsync<JsonElement>("Unirse", codigo, "Beto");
        var estado = Siguiente(b, "state");
        var bitacora = Siguiente(b, "activity");

        // "type" al final a propósito: el discriminador puede llegar en cualquier orden.
        await a.InvokeAsync("Despachar", new { id = "i1", target = filas[0], type = "item:move" });

        var s = await estado;
        Assert.Equal(3, s.GetProperty("version").GetInt64());
        Assert.Equal(["i1"], s.GetProperty("tiers")[0].GetProperty("items").EnumerateArray().Select(x => x.GetString()));
        var act = await bitacora;
        Assert.Equal("movió Gato a la fila S", act[0].GetProperty("text").GetString());
        Assert.Equal("Ana", act[0].GetProperty("who").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Accion_que_no_aplica_no_emite_nada_y_sin_texto_no_hay_activity()
    {
        var (codigo, filas) = await SalaConImagenes();
        await using var a = await api.Hub();
        await using var b = await api.Hub();
        await a.InvokeAsync<JsonElement>("Unirse", codigo, "Ana");
        await b.InvokeAsync<JsonElement>("Unirse", codigo, "Beto");
        var estados = new List<long>();
        var actividades = 0;
        b.On<JsonElement>("state", e => { lock (estados) estados.Add(e.GetProperty("version").GetInt64()); });
        b.On<JsonElement>("activity", _ => Interlocked.Increment(ref actividades));
        var siguiente = Siguiente(b, "state");

        await a.InvokeAsync("Despachar", new { type = "tier:move", id = filas[0], dir = "up" }); // borde: no aplica
        await a.InvokeAsync("Despachar", new { type = "tier:update", id = filas[0], label = "Top" }); // aplica, sin bitácora

        Assert.Equal(3, (await siguiente).GetProperty("version").GetInt64());
        lock (estados) Assert.Equal([3L], estados);
        Assert.Equal(0, actividades);
    }

    [Fact]
    public async Task Reglas_de_la_demo_llegan_como_HubException()
    {
        var (codigo, filas) = await SalaConImagenes();
        await using var a = await api.Hub();
        await a.InvokeAsync<JsonElement>("Unirse", codigo, "Ana");

        foreach (var id in filas[1..]) await a.InvokeAsync("Despachar", new { type = "tier:delete", id });
        var e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "tier:delete", id = filas[0] }));
        Assert.Contains("La tier list necesita al menos una fila.", e.Message);

        e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "tier:add", id = "Mal!", color = "#ffffff" }));
        Assert.Contains("Id de fila inválido.", e.Message);
        e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "tier:update", id = filas[0], color = "red" }));
        Assert.Contains("Color inválido.", e.Message);
        e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "item:add", item = new { id = "z", name = "z", url = "z" } }));
        Assert.Contains("Las imágenes se suben por HTTP.", e.Message);
        await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "title:update" })); // falta title

        for (var i = 1; i < Room.MAX_TIERS; i++) await a.InvokeAsync("Despachar", new { type = "tier:add", id = $"n{i}", color = "#ffffff" });
        e = await Assert.ThrowsAsync<HubException>(() => a.InvokeAsync("Despachar", new { type = "tier:add", id = "otra", color = "#ffffff" }));
        Assert.Contains("Máximo 15 filas.", e.Message);
    }

    [Fact]
    public async Task Movimientos_simultaneos_no_se_pierden()
    {
        var (codigo, filas) = await SalaConImagenes();
        await using var a = await api.Hub();
        await using var b = await api.Hub();
        await a.InvokeAsync<JsonElement>("Unirse", codigo, "Ana");
        await b.InvokeAsync<JsonElement>("Unirse", codigo, "Beto");

        // Cada movimiento cambia de fila (alterna S/A), así que todos aplican y dejan bitácora.
        Task Mover(HubConnection c, string item, int i) =>
            c.InvokeAsync("Despachar", new { type = "item:move", id = item, target = filas[i % 2], index = 0 });
        await Task.WhenAll(Enumerable.Range(0, 20).SelectMany(i => new[] { Mover(a, "i1", i), Mover(b, "i2", i) }));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RankeoDb>();
        var sala = await db.Salas.SingleAsync(s => s.Codigo == codigo);
        Assert.Equal(2 + 40, sala.Version);
        Assert.Equal(2 + 40, await db.Bitacora.CountAsync(x => x.SalaCodigo == codigo));
        // Último movimiento (i = 19) va a la fila A para las dos imágenes.
        Assert.Equal(["i1", "i2"], sala.Estado.Tiers[1].Items.Order());
        Assert.Empty(sala.Estado.Pool);
    }
}
