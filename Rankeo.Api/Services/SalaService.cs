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

    /// <summary>
    /// Sub-tier list: sala nueva con las mismas filas (vacías, ids nuevos) y las mismas imágenes, todas en el banco.
    /// Los archivos se copian al volumen de la sala nueva: borrar en una no afecta a la otra.
    /// </summary>
    public async Task<Sala> Derivar(string codigo, string? titulo, string? apodo)
    {
        if (titulo?.Length > 80) throw new ReglaException("El título admite máximo 80 caracteres.");
        if (apodo is not null) apodo = Apodo(apodo);
        var origen = await Cargar(codigo);
        var padre = origen.Estado;

        string nuevo;
        do nuevo = Calculos.GenerarCodigo();
        while (await db.Salas.AnyAsync(s => s.Codigo == nuevo));

        var items = new Dictionary<string, Item>();
        var copiados = new List<string>();
        try
        {
            // El banco conserva el orden visual del padre: filas de arriba a abajo y luego el banco.
            foreach (var id in padre.Tiers.SelectMany(t => t.Items).Concat(padre.Pool))
            {
                if (!padre.Items.TryGetValue(id, out var item)) continue;
                var imagen = Guid.TryParse(id, out var guid) ? await db.Imagenes.FindAsync(guid) : null;
                if (imagen is null) { items[id] = item; continue; }

                var nuevoId = Guid.NewGuid();
                var archivo = $"{nuevoId:N}{Path.GetExtension(imagen.Archivo)}";
                var ruta = ImagenService.Ruta(cfg, nuevo, archivo);
                Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
                File.Copy(ImagenService.Ruta(cfg, origen.Codigo, imagen.Archivo), ruta);
                copiados.Add(ruta);
                db.Imagenes.Add(new Imagen { Id = nuevoId, SalaCodigo = nuevo, Archivo = archivo, Bytes = imagen.Bytes });
                items[nuevoId.ToString("N")] = item with { Id = nuevoId.ToString("N"), Url = $"/imagenes/{nuevo}/{archivo}" };
            }

            var estado = new Room(
                nuevo,
                string.IsNullOrEmpty(titulo) ? padre.Title : titulo,
                padre.Tiers.Select(t => t with { Id = Calculos.NuevoId(), Items = [] }).ToList(),
                [.. items.Keys],
                items,
                origen.Codigo);
            var sala = new Sala { Codigo = nuevo, Estado = estado };
            db.Salas.Add(sala);
            if (apodo is not null)
                db.Bitacora.Add(new EntradaBitacora { SalaCodigo = nuevo, Quien = apodo, Color = Calculos.ColorPara(apodo), Texto = $"creó esta sala a partir de {origen.Codigo}" });
            await db.SaveChangesAsync();
            return sala;
        }
        catch
        {
            foreach (var ruta in copiados) File.Delete(ruta); // sin huérfanos en el volumen
            var dir = Path.Combine(ImagenService.Dir(cfg), nuevo);
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            throw;
        }
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
    public const int MaxSubtitulo = 80;

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
            case ItemAdd a:
                if (room.Items.Count >= MaxImagenes) throw new ReglaException($"Máximo {MaxImagenes} imágenes por sala.");
                if (a.Item.Caption?.Length > MaxSubtitulo) throw new ReglaException($"El subtítulo admite máximo {MaxSubtitulo} caracteres.");
                break;
            case TierAdd a:
                if (room.Tiers.Count >= Room.MAX_TIERS) throw new ReglaException($"Máximo {Room.MAX_TIERS} filas.");
                if (!IdFila.IsMatch(a.Id) || room.Tiers.Any(t => t.Id == a.Id)) throw new ReglaException("Id de fila inválido.");
                if (!Color.IsMatch(a.Color)) throw new ReglaException("Color inválido.");
                break;
            case TierUpdate a:
                if (a.Label?.Length > 20) throw new ReglaException("La etiqueta admite máximo 20 caracteres.");
                if (a.Color is not null && !Color.IsMatch(a.Color)) throw new ReglaException("Color inválido.");
                if (a.Glow is < 0 or > 3) throw new ReglaException("Glow inválido.");
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
        new { s.Estado.Code, s.Estado.Title, s.Estado.Tiers, s.Estado.Pool, s.Estado.Items, s.Estado.Parent, s.Version };
}
