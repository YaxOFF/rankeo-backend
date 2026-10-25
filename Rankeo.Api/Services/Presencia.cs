using System.Collections.Concurrent;

namespace Rankeo.Api.Services;

/// <summary>Quién está conectado a cada sala (vive en memoria: se pierde al reiniciar, igual que las conexiones).</summary>
public class Presencia
{
    public record Persona(string Id, string Name, string Color);

    readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Persona>> _salas = new();

    /// <summary>Agrega la conexión a la sala y devuelve la lista actualizada.</summary>
    public IReadOnlyList<Persona> Entrar(string codigo, string conexion, string apodo)
    {
        var sala = _salas.GetOrAdd(codigo, _ => new());
        sala[conexion] = new Persona(conexion, apodo, Calculos.ColorPara(apodo));
        return Lista(codigo);
    }

    /// <summary>Quita la conexión de la sala; devuelve la lista actualizada (vacía si la sala quedó sin nadie).</summary>
    public IReadOnlyList<Persona> Salir(string codigo, string conexion)
    {
        if (_salas.TryGetValue(codigo, out var sala))
        {
            sala.TryRemove(conexion, out _);
            if (sala.IsEmpty) _salas.TryRemove(codigo, out _);
        }
        return Lista(codigo);
    }

    public IReadOnlyList<Persona> Lista(string codigo) =>
        _salas.TryGetValue(codigo, out var sala) ? sala.Values.OrderBy(p => p.Name).ThenBy(p => p.Id).ToList() : [];
}
