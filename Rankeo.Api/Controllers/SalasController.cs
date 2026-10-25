using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
}
