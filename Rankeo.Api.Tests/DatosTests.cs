using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rankeo.Api.Data;
using Rankeo.Api.Modelos;
using Rankeo.Api.Services;

namespace Rankeo.Api.Tests;

public class DatosTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    RankeoDb NuevaDb()
    {
        _ = api.Server; // arranca la API: aplica la migración
        return api.Services.CreateScope().ServiceProvider.GetRequiredService<RankeoDb>();
    }

    [Fact]
    public async Task La_migracion_se_aplica_en_base_vacia()
    {
        await using var db = NuevaDb();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData("ABC10O")] // 0, 1 y O no están en el alfabeto
    [InlineData("abcdef")]
    [InlineData("ABCDE")]
    public async Task El_check_rechaza_codigos_invalidos(string codigo)
    {
        await using var db = NuevaDb();
        db.Salas.Add(new Sala { Codigo = codigo, Estado = Calculos.NuevaSala(codigo, "x") });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task El_estado_se_guarda_como_jsonb_camelCase_y_vuelve_igual()
    {
        var codigo = Calculos.GenerarCodigo();
        await using (var db = NuevaDb())
        {
            db.Salas.Add(new Sala { Codigo = codigo, Estado = Calculos.NuevaSala(codigo, "Pelis") });
            db.Bitacora.Add(new EntradaBitacora { SalaCodigo = codigo, Quien = "Ana", Color = Calculos.ColorPara("Ana"), Texto = "añadió una fila" });
            await db.SaveChangesAsync();
        }

        await using var otra = NuevaDb();
        var sala = await otra.Salas.SingleAsync(s => s.Codigo == codigo);
        Assert.Equal("Pelis", sala.Estado.Title);
        Assert.Equal(["S", "A", "B", "C", "D"], sala.Estado.Tiers.Select(t => t.Label));
        Assert.Equal(0, sala.Version);

        var titulo = await otra.Database
            .SqlQuery<string>($"""select "Estado"->>'title' as "Value" from "Salas" where "Codigo" = {codigo}""")
            .SingleAsync();
        Assert.Equal("Pelis", titulo);

        otra.Salas.Remove(sala);
        await otra.SaveChangesAsync();
        Assert.False(await otra.Bitacora.AnyAsync(e => e.SalaCodigo == codigo)); // CASCADE
    }
}
