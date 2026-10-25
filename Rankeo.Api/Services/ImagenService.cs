using Rankeo.Api.Data;
using Rankeo.Api.Dominio;
using Rankeo.Api.Modelos;

namespace Rankeo.Api.Services;

// Guarda la imagen tal cual en el volumen (sin re-codificar) y la agrega a la sala con item:add.
public class ImagenService(RankeoDb db, SalaService salas, IConfiguration cfg)
{
    public static string Dir(IConfiguration cfg) => cfg["IMAGENES_DIR"] ?? "/data/imagenes";

    public static string Ruta(IConfiguration cfg, string codigo, string archivo) => Path.Combine(Dir(cfg), codigo, archivo);

    public async Task<(Sala Sala, Item Item)> Subir(string codigo, IFormFile? archivo, string? nombre, string? apodo, string? subtitulo = null)
    {
        apodo = SalaService.Apodo(apodo);
        nombre = nombre?.Trim() is { Length: > 0 } n ? n : "Imagen";
        if (nombre.Length > 30) throw new ReglaException("El nombre admite máximo 30 caracteres.");
        subtitulo = subtitulo?.Trim() is { Length: > 0 } s ? s : null;
        if (subtitulo?.Length > SalaService.MaxSubtitulo) throw new ReglaException($"El subtítulo admite máximo {SalaService.MaxSubtitulo} caracteres.");
        if (archivo is null || archivo.Length == 0) throw new ReglaException("Falta el archivo.");
        var maxKb = int.Parse(cfg["IMAGEN_MAX_KB"] ?? "512");
        if (archivo.Length > maxKb * 1024L) throw new ReglaException($"La imagen admite máximo {maxKb} KB.");

        var bytes = new byte[archivo.Length];
        await using (var flujo = archivo.OpenReadStream()) await flujo.ReadExactlyAsync(bytes);
        var ext = Extension(bytes) ?? throw new ReglaException("Formato no admitido: usa webp, png, jpeg o gif.");

        codigo = Calculos.NormalizarCodigo(codigo);
        await salas.Existe(codigo);

        var id = Guid.NewGuid();
        var nombreArchivo = $"{id:N}{ext}";
        var ruta = Ruta(cfg, codigo, nombreArchivo);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllBytesAsync(ruta, bytes);
        try
        {
            // Queda en el contexto y se guarda en la misma transacción de Aplicar.
            db.Imagenes.Add(new Imagen { Id = id, SalaCodigo = codigo, Archivo = nombreArchivo, Bytes = bytes.Length });
            var item = new Item(id.ToString("N"), nombre, $"/imagenes/{codigo}/{nombreArchivo}", apodo, subtitulo);
            var hecho = await salas.Aplicar(codigo, new ItemAdd(item), apodo) ?? throw new InvalidOperationException("item:add no aplicó.");
            return (hecho.Sala, item);
        }
        catch
        {
            File.Delete(ruta); // sin huérfanos en el volumen
            throw;
        }
    }

    // Formato por firma de bytes, no por el nombre ni el Content-Type que manda el cliente.
    static string? Extension(byte[] b) => b switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ".png",
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ".gif",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ".webp",
        _ => null,
    };
}
