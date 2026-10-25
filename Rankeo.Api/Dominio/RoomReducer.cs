namespace Rankeo.Api.Dominio;

// Port puro de src/room/roomReducer.ts: devuelve la misma instancia si la acción no aplica.
public static class RoomReducer
{
    public static IReadOnlyList<string>? ListOf(Room room, string container) =>
        container == Room.POOL ? room.Pool : room.Tiers.FirstOrDefault(t => t.Id == container)?.Items;

    /// <summary>Fila (o banco) donde está una imagen.</summary>
    public static string? ContainerOf(Room room, string id) =>
        room.Pool.Contains(id) ? Room.POOL : room.Tiers.FirstOrDefault(t => t.Items.Contains(id))?.Id;

    static Room UpdateList(Room room, string container, Func<IReadOnlyList<string>, IReadOnlyList<string>> fn) =>
        container == Room.POOL
            ? room with { Pool = fn(room.Pool) }
            : room with { Tiers = room.Tiers.Select(t => t.Id == container ? t with { Items = fn(t.Items) } : t).ToList() };

    static Room Detach(Room room, string id) => room with
    {
        Pool = room.Pool.Where(x => x != id).ToList(),
        Tiers = room.Tiers.Select(t => t.Items.Contains(id) ? t with { Items = t.Items.Where(x => x != id).ToList() } : t).ToList(),
    };

    public static Room Reduce(Room room, RoomAction action)
    {
        switch (action)
        {
            case ItemAdd a:
                if (room.Items.ContainsKey(a.Item.Id)) return room;
                return room with
                {
                    Items = new Dictionary<string, Item>(room.Items) { [a.Item.Id] = a.Item },
                    Pool = [.. room.Pool, a.Item.Id],
                };

            case ItemMove a:
                if (!room.Items.ContainsKey(a.Id) || ListOf(room, a.Target) is null) return room;
                return UpdateList(Detach(room, a.Id), a.Target, list =>
                {
                    var i = Math.Max(0, Math.Min(a.Index ?? list.Count, list.Count));
                    return [.. list.Take(i), a.Id, .. list.Skip(i)];
                });

            case ItemDelete a:
            {
                if (!room.Items.ContainsKey(a.Id)) return room;
                var items = new Dictionary<string, Item>(room.Items);
                items.Remove(a.Id);
                return Detach(room, a.Id) with { Items = items };
            }

            case TierUpdate a:
                if (!room.Tiers.Any(t => t.Id == a.Id)) return room;
                return room with
                {
                    Tiers = room.Tiers.Select(t => t.Id != a.Id ? t : t with
                    {
                        Label = a.Label ?? t.Label,
                        Color = a.Color ?? t.Color,
                        Glow = a.Glow ?? t.Glow,
                    }).ToList(),
                };

            case TierAdd a:
                if (room.Tiers.Count >= Room.MAX_TIERS) return room;
                return room with { Tiers = [.. room.Tiers, new Tier(a.Id, "Nuevo", a.Color, [])] };

            case TierMove a:
            {
                var i = room.Tiers.ToList().FindIndex(t => t.Id == a.Id);
                var j = i + (a.Dir == "up" ? -1 : 1);
                if (i == -1 || j < 0 || j >= room.Tiers.Count) return room;
                var tiers = room.Tiers.ToList();
                (tiers[i], tiers[j]) = (tiers[j], tiers[i]);
                return room with { Tiers = tiers };
            }

            case TierClear a:
            {
                var tier = room.Tiers.FirstOrDefault(t => t.Id == a.Id);
                if (tier is null || tier.Items.Count == 0) return room;
                return room with
                {
                    Pool = [.. room.Pool, .. tier.Items],
                    Tiers = room.Tiers.Select(t => t.Id == a.Id ? t with { Items = [] } : t).ToList(),
                };
            }

            case TierDelete a:
            {
                var tier = room.Tiers.FirstOrDefault(t => t.Id == a.Id);
                if (tier is null || room.Tiers.Count <= 1) return room;
                return room with { Pool = [.. room.Pool, .. tier.Items], Tiers = room.Tiers.Where(t => t.Id != a.Id).ToList() };
            }

            case TitleUpdate a:
                return room with { Title = a.Title };

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}
