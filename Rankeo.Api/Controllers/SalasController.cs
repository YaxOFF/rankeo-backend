using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Rankeo.Api.Hubs;
using Rankeo.Api.Services;

namespace Rankeo.Api.Controllers;

[ApiController, Route("api/salas")]
public class SalasController(SalaService salas) : ControllerBase
{
    public record CrearSala(string? Title);

    [HttpPost, EnableRateLimiting("crear")]
    public async Task<IActionResult> Crear(CrearSala body)
    {
        var sala = await salas.Crear(body.Title);
        return Created($"/api/salas/{sala.Codigo}", SalaService.Dto(sala));
    }

    [HttpGet("{codigo}"), EnableRateLimiting("existe")]
    public async Task<IActionResult> Existe(string codigo)
    {
        await salas.Existe(codigo);
        return NoContent();
    }

    public record DerivarSala(string? Title, string? Apodo);

    // Sub-tier list: sala nueva con las mismas filas e imágenes del padre, todas sin clasificar.
    [HttpPost("{codigo}/derivadas"), EnableRateLimiting("crear")]
    public async Task<IActionResult> Derivar(string codigo, DerivarSala body)
    {
        var sala = await salas.Derivar(codigo, body.Title, body.Apodo);
        return Created($"/api/salas/{sala.Codigo}", SalaService.Dto(sala));
    }

    // Sube la imagen, la agrega al banco y avisa a la sala (después del commit).
    [HttpPost("{codigo}/imagenes"), EnableRateLimiting("subir")]
    public async Task<IActionResult> Subir(string codigo, IFormFile? archivo, [FromForm] string? nombre, [FromForm] string? apodo,
        [FromForm] string? subtitulo, [FromServices] ImagenService imagenes, [FromServices] IHubContext<SalaHub> hub)
    {
        var (sala, item) = await imagenes.Subir(codigo, archivo, nombre, apodo, subtitulo);
        await hub.Clients.Group(sala.Codigo).SendAsync("state", SalaService.Dto(sala));
        await hub.Clients.Group(sala.Codigo).SendAsync("activity", await salas.Actividad(sala.Codigo));
        return Created(item.Url, item);
    }
}
