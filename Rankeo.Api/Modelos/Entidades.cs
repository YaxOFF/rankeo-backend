using Rankeo.Api.Dominio;

namespace Rankeo.Api.Modelos;

public class Sala
{
    public required string Codigo { get; set; }
    /// <summary>Documento jsonb con el estado completo (ver <see cref="Room"/>).</summary>
    public required Room Estado { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreadaEn { get; set; } = DateTimeOffset.UtcNow;
}

public class EntradaBitacora
{
    public long Id { get; set; }
    public required string SalaCodigo { get; set; }
    public required string Quien { get; set; }
    public required string Color { get; set; }
    public required string Texto { get; set; }
    public DateTimeOffset En { get; set; } = DateTimeOffset.UtcNow;
}

public class Imagen
{
    public Guid Id { get; set; }
    public required string SalaCodigo { get; set; }
    public required string Archivo { get; set; }
    public int Bytes { get; set; }
    public DateTimeOffset SubidaEn { get; set; } = DateTimeOffset.UtcNow;
}
