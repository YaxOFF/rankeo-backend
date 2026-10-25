using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rankeo.Api.Data;
using Rankeo.Api.Dominio;
using Rankeo.Api.Modelos;

namespace Rankeo.Api.Services;

public class SalaService(RankeoDb db, IConfiguration cfg)
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

    public async Task<Sala> Cargar(string codigo)
    {
        codigo = Calculos.NormalizarCodigo(codigo);
        return await db.Salas.AsNoTracking().SingleOrDefaultAsync(s => s.Codigo == codigo)
            ?? throw new NoEncontradoException($"No existe ninguna sala con el código {codigo}.");
    }

    /// <summary>Últimas 100 entradas como <c>ActivityEntry</c> de types.ts (más reciente primero).</summary>
    public async Task<List<object>> Actividad(string codigo) =>
        (await db.Bitacora.Where(b => b.SalaCodigo == codigo).OrderByDescending(b => b.Id).Take(100).ToListAsync())
        .Select(b => (object)new { Id = b.Id.ToString(), Who = new { Name = b.Quien, b.Color }, Text = b.Texto, At = b.En.ToUnixTimeMilliseconds() })
        .ToList();

    /// <summary>
    /// Aplica una acción con la fila de la sala bloqueada (FOR UPDATE). Devuelve null si no aplica: no se guarda nada.
    /// </summary>
    public async Task<(Sala Sala, bool ConBitacora)?> Aplicar(string codigo, RoomAction accion, string apodo)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // ToListAsync: sin componer, para que el FOR UPDATE quede en la consulta de afuera.
        var sala = (await db.Salas.FromSql($"SELECT * FROM \"Salas\" WHERE \"Codigo\" = {codigo} FOR UPDATE").ToListAsync()).SingleOrDefault()
            ?? throw new NoEncontradoException($"No existe ninguna sala con el código {codigo}.");

        Validar(sala.Estado, accion);
        var nuevo = RoomReducer.Reduce(sala.Estado, accion);
        if (ReferenceEquals(nuevo, sala.Estado)) return null;

        var texto = Calculos.TextoBitacora(sala.Estado, accion);
        sala.Estado = nuevo;
        sala.Version++;
        if (texto is not null)
            db.Bitacora.Add(new EntradaBitacora { SalaCodigo = codigo, Quien = apodo, Color = Calculos.ColorPara(apodo), Texto = texto });
        var imagen = accion is ItemDelete d && Guid.TryParse(d.Id, out var id) ? await db.Imagenes.FindAsync(id) : null;
        if (imagen is not null) db.Imagenes.Remove(imagen);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        // Después del commit: si el borrado del archivo falla, la sala ya quedó bien.
        if (imagen is not null) File.Delete(ImagenService.Ruta(cfg, codigo, imagen.Archivo));
        return (sala, texto is not null);
    }

    public const int MaxImagenes = 200;

    public static string Apodo(string? apodo)
    {
        apodo = apodo?.Trim() ?? "";
        if (apodo.Length is < 1 or > 30) throw new ReglaException("El apodo debe tener entre 1 y 30 caracteres.");
        return apodo;
    }

    static readonly Regex Color = new("^#[0-9a-fA-F]{6}$");
    static readonly Regex IdFila = new("^[a-z0-9]{1,16}$");

    // Reglas que la demo comprueba en la UI antes de despachar; el reducer solo devolvería "sin cambio".
    static void Validar(Room room, RoomAction accion)
    {
        switch (accion)
        {
            case ItemAdd:
                if (room.Items.Count >= MaxImagenes) throw new ReglaException($"Máximo {MaxImagenes} imágenes por sala.");
                break;
            case TierAdd a:
                if (room.Tiers.Count >= Room.MAX_TIERS) throw new ReglaException($"Máximo {Room.MAX_TIERS} filas.");
                if (!IdFila.IsMatch(a.Id) || room.Tiers.Any(t => t.Id == a.Id)) throw new ReglaException("Id de fila inválido.");
                if (!Color.IsMatch(a.Color)) throw new ReglaException("Color inválido.");
                break;
            case TierUpdate a:
                if (a.Label?.Length > 20) throw new ReglaException("La etiqueta admite máximo 20 caracteres.");
                if (a.Color is not null && !Color.IsMatch(a.Color)) throw new ReglaException("Color inválido.");
                break;
            case TierDelete:
                if (room.Tiers.Count <= 1) throw new ReglaException("La tier list necesita al menos una fila.");
                break;
            case TitleUpdate a:
                if (a.Title.Length > 80) throw new ReglaException("El título admite máximo 80 caracteres.");
                break;
        }
    }

    /// <summary>Estado que ve el cliente: <c>Room</c> de types.ts + <c>version</c>.</summary>
    public static object Dto(Sala s) =>
        new { s.Estado.Code, s.Estado.Title, s.Estado.Tiers, s.Estado.Pool, s.Estado.Items, s.Version };
}
