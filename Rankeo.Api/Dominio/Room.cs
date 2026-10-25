using System.Text.Json.Serialization;

namespace Rankeo.Api.Dominio;

// Mismo contrato que src/types.ts del frontend (JSON camelCase).

/// <summary><c>Caption</c>: subtítulo opcional que se pide al subir la imagen (máx 80).</summary>
public record Item(string Id, string Name, string Url, string? By = null, string? Caption = null);

/// <summary><c>Glow</c>: brillo de la fila, 0 (apagado) a 3. Las salas guardadas antes no lo traen y quedan en 0.</summary>
public record Tier(string Id, string Label, string Color, IReadOnlyList<string> Items, int Glow = 0);

/// <summary><c>Parent</c>: código de la sala de la que se derivó (sub-tier list), o null.</summary>
public record Room(
    string Code,
    string Title,
    IReadOnlyList<Tier> Tiers,
    IReadOnlyList<string> Pool,
    IReadOnlyDictionary<string, Item> Items,
    string? Parent = null)
{
    /// <summary>Id del contenedor de imágenes sin clasificar.</summary>
    public const string POOL = "pool";
    public const int MAX_TIERS = 15;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ItemAdd), "item:add")]
[JsonDerivedType(typeof(ItemMove), "item:move")]
[JsonDerivedType(typeof(ItemDelete), "item:delete")]
[JsonDerivedType(typeof(TierUpdate), "tier:update")]
[JsonDerivedType(typeof(TierAdd), "tier:add")]
[JsonDerivedType(typeof(TierMove), "tier:move")]
[JsonDerivedType(typeof(TierClear), "tier:clear")]
[JsonDerivedType(typeof(TierDelete), "tier:delete")]
[JsonDerivedType(typeof(TitleUpdate), "title:update")]
public abstract record RoomAction;

public record ItemAdd(Item Item) : RoomAction;
public record ItemMove(string Id, string Target, int? Index = null) : RoomAction;
public record ItemDelete(string Id) : RoomAction;
public record TierUpdate(string Id, string? Label = null, string? Color = null, int? Glow = null) : RoomAction;
public record TierAdd(string Id, string Color) : RoomAction;
/// <summary><c>Dir</c> es <c>"up"</c> o <c>"down"</c>.</summary>
public record TierMove(string Id, string Dir) : RoomAction;
public record TierClear(string Id) : RoomAction;
public record TierDelete(string Id) : RoomAction;
public record TitleUpdate(string Title) : RoomAction;
