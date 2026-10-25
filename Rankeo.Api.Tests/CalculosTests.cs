using Rankeo.Api.Dominio;
using Rankeo.Api.Services;

namespace Rankeo.Api.Tests;

public class CalculosTests
{
    // Vectores calculados con colorFor de src/lib/ids.ts en Node.
    [Theory]
    [InlineData("", 0)]
    [InlineData("Ana", 92)]
    [InlineData("Luis", 323)]
    [InlineData("José", 203)]
    [InlineData("😀", 277)]
    [InlineData("a😀b", 22)]
    [InlineData("Ñandú 🍕 crew", 9)]
    public void ColorPara_coincide_con_colorFor_de_js(string texto, int h) =>
        Assert.Equal($"hsl({h} 60% 70%)", Calculos.ColorPara(texto));

    // Sustitutos sueltos: xUnit los estropea en InlineData, van aparte.
    [Fact]
    public void ColorPara_con_sustitutos_sueltos_coincide_con_js()
    {
        Assert.Equal("hsl(277 60% 70%)", Calculos.ColorPara("\uD83D"));
        Assert.Equal("hsl(349 60% 70%)", Calculos.ColorPara("x\uDE00\uD83D"));
    }

    [Fact]
    public void NormalizarCodigo_quita_todo_lo_que_no_es_letra_o_numero() =>
        Assert.Equal("ABC123", Calculos.NormalizarCodigo("  ab-c 12 3 "));

    [Fact]
    public void GenerarCodigo_usa_solo_el_alfabeto_sin_ambiguos()
    {
        for (var i = 0; i < 1000; i++)
        {
            var codigo = Calculos.GenerarCodigo();
            Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", codigo);
            Assert.NotEqual("DEMO42", codigo);
        }
    }

    [Fact]
    public void NuevaSala_trae_titulo_por_defecto_y_cinco_filas()
    {
        var room = Calculos.NuevaSala("TEST01", "");
        Assert.Equal("Mi Tier List", room.Title);
        Assert.Equal(["S", "A", "B", "C", "D"], room.Tiers.Select(t => t.Label));
        Assert.Equal(Calculos.TIER_COLORS[..5], room.Tiers.Select(t => t.Color));
        Assert.All(room.Tiers, t => Assert.Matches("^[a-z0-9]{1,16}$", t.Id));
        Assert.Equal("Comida", Calculos.NuevaSala("TEST01", "Comida").Title);
    }

    static Room Sala()
    {
        var room = Calculos.NuevaSala("TEST01", null);
        room = RoomReducer.Reduce(room, new ItemAdd(new Item("p", "Pizza", "")));
        return RoomReducer.Reduce(room, new ItemAdd(new Item("t", "Tacos", "")));
    }

    [Fact]
    public void TextoBitacora_de_cada_accion()
    {
        var room = Sala();
        var s = room.Tiers[0].Id;
        var conTacosEnS = RoomReducer.Reduce(room, new ItemMove("t", s));

        Assert.Equal("subió Nachos", Calculos.TextoBitacora(room, new ItemAdd(new Item("n", "Nachos", ""))));
        Assert.Equal("movió Pizza a la fila S", Calculos.TextoBitacora(room, new ItemMove("p", s)));
        Assert.Equal("movió Tacos a el banco", Calculos.TextoBitacora(conTacosEnS, new ItemMove("t", Room.POOL)));
        Assert.Equal("eliminó Pizza", Calculos.TextoBitacora(room, new ItemDelete("p")));
        Assert.Equal("añadió una fila", Calculos.TextoBitacora(room, new TierAdd("x", "#ffffff")));
        Assert.Equal("vació la fila S", Calculos.TextoBitacora(conTacosEnS, new TierClear(s)));
        Assert.Equal("eliminó la fila S", Calculos.TextoBitacora(room, new TierDelete(s)));
    }

    [Fact]
    public void TextoBitacora_no_registra_reordenar_ni_cambios_de_fila_o_titulo()
    {
        var room = Sala();
        var s = room.Tiers[0].Id;
        Assert.Null(Calculos.TextoBitacora(room, new ItemMove("p", Room.POOL, 1)));
        Assert.Null(Calculos.TextoBitacora(room, new TierUpdate(s, "Top", "#000000")));
        Assert.Null(Calculos.TextoBitacora(room, new TierMove(s, "down")));
        Assert.Null(Calculos.TextoBitacora(room, new TitleUpdate("Otro")));
    }
}
