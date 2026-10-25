using System.Text.Json;
using Rankeo.Api.Dominio;
using Rankeo.Api.Services;
using static Rankeo.Api.Dominio.RoomReducer;

namespace Rankeo.Api.Tests;

// Port 1:1 de src/room/roomReducer.test.ts.
public class RoomReducerTests
{
    static Item ItemDe(string id) => new(id, id, "");

    static Room RoomWith(params string[] ids) =>
        ids.Aggregate(Calculos.NuevaSala("TEST01", null), (room, id) => Reduce(room, new ItemAdd(ItemDe(id))));

    [Fact]
    public void Las_imagenes_nuevas_llegan_al_banco()
    {
        var room = RoomWith("a", "b");
        Assert.Equal(["a", "b"], room.Pool);
        Assert.Equal(["a", "b"], room.Items.Keys);
    }

    [Fact]
    public void Mueve_una_imagen_a_otra_fila_en_la_posicion_indicada()
    {
        var room = RoomWith("a", "b", "c");
        var s = room.Tiers[0].Id;
        room = Reduce(room, new ItemMove("a", s, 0));
        room = Reduce(room, new ItemMove("c", s, 0));
        Assert.Equal(["c", "a"], room.Tiers[0].Items);
        Assert.Equal(["b"], room.Pool);
        Assert.Equal(s, ContainerOf(room, "a"));
    }

    [Fact]
    public void Reordena_dentro_de_la_misma_fila()
    {
        var room = RoomWith("a", "b", "c");
        room = Reduce(room, new ItemMove("a", Room.POOL, 2));
        Assert.Equal(["b", "c", "a"], room.Pool);
    }

    [Fact]
    public void Ignora_movimientos_a_filas_o_imagenes_que_no_existen()
    {
        var room = RoomWith("a");
        Assert.Same(room, Reduce(room, new ItemMove("a", "nope")));
        Assert.Same(room, Reduce(room, new ItemMove("zzz", Room.POOL)));
    }

    [Fact]
    public void Borrar_una_fila_devuelve_sus_imagenes_al_banco()
    {
        var room = RoomWith("a", "b");
        var s = room.Tiers[0].Id;
        room = Reduce(room, new ItemMove("a", s));
        room = Reduce(room, new TierDelete(s));
        Assert.Equal(4, room.Tiers.Count);
        Assert.Equal(["b", "a"], room.Pool);
    }

    [Fact]
    public void No_deja_borrar_la_ultima_fila()
    {
        var room = Calculos.NuevaSala("TEST01", null);
        foreach (var t in room.Tiers.Skip(1)) room = Reduce(room, new TierDelete(t.Id));
        Assert.Single(room.Tiers);
        Assert.Same(room, Reduce(room, new TierDelete(room.Tiers[0].Id)));
    }

    [Fact]
    public void Vaciar_una_fila_manda_todo_al_banco()
    {
        var room = RoomWith("a", "b");
        var s = room.Tiers[0].Id;
        room = Reduce(room, new ItemMove("a", s));
        room = Reduce(room, new ItemMove("b", s));
        room = Reduce(room, new TierClear(s));
        Assert.Empty(room.Tiers[0].Items);
        Assert.Equal(["a", "b"], room.Pool);
    }

    [Fact]
    public void Eliminar_una_imagen_la_quita_de_su_fila_y_del_indice()
    {
        var room = RoomWith("a");
        room = Reduce(room, new ItemDelete("a"));
        Assert.Empty(room.Pool);
        Assert.Empty(room.Items);
    }

    [Fact]
    public void Sube_y_baja_filas_sin_salirse_de_los_bordes()
    {
        var room = Calculos.NuevaSala("TEST01", null);
        var (s, a) = (room.Tiers[0], room.Tiers[1]);
        Assert.Same(room, Reduce(room, new TierMove(s.Id, "up")));
        room = Reduce(room, new TierMove(s.Id, "down"));
        Assert.Equal([a.Id, s.Id], room.Tiers.Take(2).Select(t => t.Id));
    }

    // "No aplica → misma referencia" para cada acción.
    public static TheoryData<RoomAction> AccionesQueNoAplican => new()
    {
        new ItemAdd(ItemDe("a")),            // ya existe
        new ItemMove("a", "nope"),
        new ItemDelete("zzz"),
        new TierUpdate("nope", "X"),
        new TierMove("nope", "up"),
        new TierClear("nope"),
        new TierDelete("nope"),
    };

    [Theory, MemberData(nameof(AccionesQueNoAplican))]
    public void Accion_que_no_aplica_devuelve_la_misma_sala(RoomAction accion)
    {
        var room = RoomWith("a");
        Assert.Same(room, Reduce(room, accion));
    }

    [Fact]
    public void Vaciar_fila_vacia_y_bajar_la_ultima_no_cambian_nada()
    {
        var room = RoomWith("a");
        Assert.Same(room, Reduce(room, new TierClear(room.Tiers[0].Id)));
        Assert.Same(room, Reduce(room, new TierMove(room.Tiers[^1].Id, "down")));
    }

    [Fact]
    public void No_pasa_de_15_filas()
    {
        var room = Calculos.NuevaSala("TEST01", null);
        for (var i = room.Tiers.Count; i < Room.MAX_TIERS; i++) room = Reduce(room, new TierAdd($"t{i}", "#ffffff"));
        Assert.Equal(15, room.Tiers.Count);
        Assert.Same(room, Reduce(room, new TierAdd("extra", "#ffffff")));
    }

    [Fact]
    public void Indice_fuera_de_rango_se_acota()
    {
        var room = RoomWith("a", "b", "c");
        Assert.Equal(["b", "c", "a"], Reduce(room, new ItemMove("a", Room.POOL, 99)).Pool);
        Assert.Equal(["c", "a", "b"], Reduce(room, new ItemMove("c", Room.POOL, -5)).Pool);
    }

    [Fact]
    public void Editar_fila_cambia_solo_lo_que_llega()
    {
        var room = Calculos.NuevaSala("TEST01", null);
        var s = room.Tiers[0];
        var tier = Reduce(room, new TierUpdate(s.Id, Label: "Top")).Tiers[0];
        Assert.Equal(("Top", s.Color), (tier.Label, tier.Color));
        tier = Reduce(room, new TierUpdate(s.Id, Glow: 2)).Tiers[0];
        Assert.Equal((s.Label, s.Color, 2), (tier.Label, tier.Color, tier.Glow));
    }

    [Fact]
    public void Fila_guardada_sin_glow_carga_apagada()
    {
        var json = """{"id":"s","label":"S","color":"#c96063","items":[]}""";
        Assert.Equal(0, JsonSerializer.Deserialize<Tier>(json, JsonSerializerOptions.Web)!.Glow);
    }
}
