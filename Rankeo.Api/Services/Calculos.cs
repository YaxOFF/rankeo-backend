using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Rankeo.Api.Dominio;

namespace Rankeo.Api.Services;

// Funciones puras portadas de src/lib/ids.ts, src/lib/demo.ts y los textos de bitácora del cliente.
public static class Calculos
{
    const string CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sin 0/O ni 1/I

    public static readonly string[] TIER_COLORS = ["#c96063", "#dba06d", "#dcc86c", "#89d07a", "#74bdd6", "#9a8ad6",
                                                   "#d68ac4", "#e88b5a", "#a0a0a0", "#5fbfa8", "#f0e68c", "#ffffff"];

    // Nunca sale DEMO42: la "O" no está en el alfabeto.
    public static string GenerarCodigo() => RandomNumberGenerator.GetString(CODE_ALPHABET, 6);

    public static string NormalizarCodigo(string? codigo) =>
        Regex.Replace((codigo ?? "").Trim().ToUpperInvariant(), "[^A-Z0-9]", "");

    /// <summary>Id de fila como <c>newId()</c> del cliente: 8 caracteres base36.</summary>
    public static string NuevoId() => RandomNumberGenerator.GetString("0123456789abcdefghijklmnopqrstuvwxyz", 8);

    /// <summary>Color estable a partir de un texto, igual que <c>colorFor</c> de JS.</summary>
    public static string ColorPara(string texto)
    {
        var h = 0;
        for (var i = 0; i < texto.Length; i++)
        {
            // JS itera code points y toma charCodeAt(0): de un par sustituto solo cuenta el alto.
            if (char.IsLowSurrogate(texto[i]) && i > 0 && char.IsHighSurrogate(texto[i - 1])) continue;
            h = (h * 31 + texto[i]) % 360;
        }
        return $"hsl({h} 60% 70%)";
    }

    public static Room NuevaSala(string codigo, string? titulo) => new(
        codigo,
        string.IsNullOrEmpty(titulo) ? "Mi Tier List" : titulo,
        new[] { "S", "A", "B", "C", "D" }.Select((label, i) => new Tier(NuevoId(), label, TIER_COLORS[i], [])).ToList(),
        [],
        new Dictionary<string, Item>());

    /// <summary>Texto de bitácora de una acción ya aplicada, calculado con el estado previo; null si no se registra.</summary>
    public static string? TextoBitacora(Room antes, RoomAction accion)
    {
        string NombreItem(string id) => antes.Items.TryGetValue(id, out var item) ? item.Name : "la imagen";
        string? LabelFila(string id) => antes.Tiers.FirstOrDefault(t => t.Id == id)?.Label;
        string Lugar(string contenedor) => contenedor == Room.POOL ? "el banco" : $"la fila {LabelFila(contenedor) ?? ""}";

        return accion switch
        {
            ItemAdd a => $"subió {a.Item.Name}",
            ItemMove a when RoomReducer.ContainerOf(antes, a.Id) != a.Target => $"movió {NombreItem(a.Id)} a {Lugar(a.Target)}",
            ItemDelete a => $"eliminó {NombreItem(a.Id)}",
            TierAdd => "añadió una fila",
            TierClear a => $"vació la fila {LabelFila(a.Id)}",
            TierDelete a => $"eliminó la fila {LabelFila(a.Id)}",
            _ => null,
        };
    }
}
