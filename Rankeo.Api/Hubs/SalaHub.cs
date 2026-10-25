using Microsoft.AspNetCore.SignalR;
using Rankeo.Api.Dominio;
using Rankeo.Api.Services;

namespace Rankeo.Api.Hubs;

// Transporte en vivo: cada sala es un grupo de SignalR. Emite state/activity solo después del commit.
// Presencia y cursores son efímeros (no se guardan): viven solo mientras dura la conexión.
public class SalaHub(SalaService salas, Presencia presencia) : Hub
{
    public async Task<object> Unirse(string codigo, string apodo)
    {
        apodo = await Errores(() => Task.FromResult(SalaService.Apodo(apodo)));
        var sala = await Errores(() => salas.Cargar(codigo));

        if (Context.Items.TryGetValue("sala", out var v) && v is string anterior && anterior != sala.Codigo)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, anterior);
            await Clients.Group(anterior).SendAsync("presence", presencia.Salir(anterior, Context.ConnectionId));
        }
        Context.Items["sala"] = sala.Codigo;
        Context.Items["apodo"] = apodo;
        await Groups.AddToGroupAsync(Context.ConnectionId, sala.Codigo);
        await Clients.Group(sala.Codigo).SendAsync("presence", presencia.Entrar(sala.Codigo, Context.ConnectionId, apodo));

        return new { Room = SalaService.Dto(sala), Activity = await salas.Actividad(sala.Codigo) };
    }

    public async Task Despachar(RoomAction accion)
    {
        if (!Context.Items.TryGetValue("sala", out var v) || v is not string codigo) throw new HubException("Primero únete a una sala.");
        // Las imágenes llegan por HTTP (multipart), nunca como item:add del cliente.
        if (accion is ItemAdd) throw new HubException("Las imágenes se suben por HTTP.");

        var hecho = await Errores(() => salas.Aplicar(codigo, accion, (string)Context.Items["apodo"]!));
        if (hecho is not { } h) return;

        await Clients.Group(codigo).SendAsync("state", SalaService.Dto(h.Sala));
        if (h.ConBitacora) await Clients.Group(codigo).SendAsync("activity", await salas.Actividad(codigo));
    }

    /// <summary>
    /// Posición del puntero (px relativos al tablero) y, si arrastra, la imagen que lleva. Solo se reenvía
    /// a los demás de la sala; no se valida ni se guarda. <c>away</c>: el puntero salió de la ventana.
    /// </summary>
    public Task Cursor(double x, double y, string? dragId, bool away)
    {
        if (!Context.Items.TryGetValue("sala", out var v) || v is not string codigo) return Task.CompletedTask;
        var apodo = (string)Context.Items["apodo"]!;
        return Clients.OthersInGroup(codigo).SendAsync("cursor", new
        {
            Id = Context.ConnectionId, Name = apodo, Color = Calculos.ColorPara(apodo), X = x, Y = y, DragId = dragId, Away = away,
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("sala", out var v) && v is string codigo)
            await Clients.Group(codigo).SendAsync("presence", presencia.Salir(codigo, Context.ConnectionId));
        await base.OnDisconnectedAsync(exception);
    }

    // Mismos mensajes que en HTTP, como HubException (el resto de errores llega genérico al cliente).
    static async Task<T> Errores<T>(Func<Task<T>> fn)
    {
        try { return await fn(); }
        catch (Exception e) when (e is ReglaException or NoEncontradoException) { throw new HubException(e.Message); }
    }
}
