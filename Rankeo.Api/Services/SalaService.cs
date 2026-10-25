using Microsoft.EntityFrameworkCore;
using Rankeo.Api.Data;
using Rankeo.Api.Modelos;

namespace Rankeo.Api.Services;

public class SalaService(RankeoDb db)
{
    public async Task<Sala> Crear(string? titulo)
    {
        if (titulo?.Length > 80) throw new ReglaException("El título admite máximo 80 caracteres.");

        string codigo;
        do codigo = Calculos.GenerarCodigo();
        while (await db.Salas.AnyAsync(s => s.Codigo == codigo));

        var sala = new Sala { Codigo = codigo, Estado = Calculos.NuevaSala(codigo, titulo) };
        db.Salas.Add(sala);
        await db.SaveChangesAsync();
        return sala;
    }

    public async Task Existe(string codigo)
    {
        codigo = Calculos.NormalizarCodigo(codigo);
        if (!await db.Salas.AnyAsync(s => s.Codigo == codigo))
            throw new NoEncontradoException($"No existe ninguna sala con el código {codigo}.");
    }

    /// <summary>Estado que ve el cliente: <c>Room</c> de types.ts + <c>version</c>.</summary>
    public static object Dto(Sala s) =>
        new { s.Estado.Code, s.Estado.Title, s.Estado.Tiers, s.Estado.Pool, s.Estado.Items, s.Version };
}
