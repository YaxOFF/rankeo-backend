using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rankeo.Api.Dominio;
using Rankeo.Api.Modelos;

namespace Rankeo.Api.Data;

public class RankeoDb(DbContextOptions<RankeoDb> options) : DbContext(options)
{
    public DbSet<Sala> Salas => Set<Sala>();
    public DbSet<EntradaBitacora> Bitacora => Set<EntradaBitacora>();
    public DbSet<Imagen> Imagenes => Set<Imagen>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Sala>(e =>
        {
            e.HasKey(s => s.Codigo);
            e.Property(s => s.Codigo).HasColumnType("char(6)");
            // Room es inmutable: el reducer devuelve otra instancia, así que Equals por referencia basta para detectar cambios.
            e.Property(s => s.Estado).HasColumnType("jsonb").HasConversion(
                r => JsonSerializer.Serialize(r, JsonSerializerOptions.Web),
                s => JsonSerializer.Deserialize<Room>(s, JsonSerializerOptions.Web)!);
            e.Property(s => s.Version).HasDefaultValue(0L);
            e.ToTable(t => t.HasCheckConstraint("CK_Salas_Codigo", "\"Codigo\" ~ '^[A-HJ-NP-Z2-9]{6}$'"));
        });

        b.Entity<EntradaBitacora>(e =>
        {
            e.ToTable("Bitacora");
            e.Property(x => x.SalaCodigo).HasColumnType("char(6)");
            e.Property(x => x.Quien).HasMaxLength(30);
            e.Property(x => x.Color).HasMaxLength(24);
            e.Property(x => x.Texto).HasMaxLength(120);
            e.HasOne<Sala>().WithMany().HasForeignKey(x => x.SalaCodigo).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SalaCodigo, x.Id }).IsDescending(false, true);
        });

        b.Entity<Imagen>(e =>
        {
            e.Property(x => x.SalaCodigo).HasColumnType("char(6)");
            e.Property(x => x.Archivo).HasMaxLength(80);
            e.HasOne<Sala>().WithMany().HasForeignKey(x => x.SalaCodigo).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Imagenes_Bytes", "\"Bytes\" > 0"));
        });
    }
}
