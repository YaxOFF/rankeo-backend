using Microsoft.AspNetCore.SignalR;
using Rankeo.Api.Dominio;
using Rankeo.Api.Services;

namespace Rankeo.Api.Hubs;

// Transporte en vivo: cada sala es un grupo de SignalR. Emite state/activity solo después del commit.
public class SalaHub(SalaService salas) : Hub
{
    public async Task<object> Unirse(string codigo, string apodo)
    {
        apodo = await Errores(() => Task.FromResult(SalaService.Apodo(apodo)));
        var sala = await Errores(() => salas.Cargar(codigo));

        if (Context.Items.TryGetValue("sala", out var v) && v is string anterior && anterior != sala.Codigo)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, anterior);
        Context.Items["sala"] = sala.Codigo;
        Context.Items["apodo"] = apodo;
        await Groups.AddToGroupAsync(Context.ConnectionId, sala.Codigo);

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

    // Mismos mensajes que en HTTP, como HubException (el resto de errores llega genérico al cliente).
    static async Task<T> Errores<T>(Func<Task<T>> fn)
    {
        try { return await fn(); }
        catch (Exception e) when (e is ReglaException or NoEncontradoException) { throw new HubException(e.Message); }
    }
}
