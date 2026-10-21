using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Búsqueda global de «Buscar o ejecutar» (2026-10-21): registros por número o nombre en varias pantallas. Sin permiso propio: el servicio busca solo en lo que el
/// usuario puede ver (permiso de lectura + módulo encendido de cada tipo). Menos de 2 letras responde vacío.
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize]
public sealed class SearchController(GlobalSearchService search) : ControllerBase
{
    [HttpGet]
    public Task<SearchResultDto> Get([FromQuery] string? q, CancellationToken ct = default) => search.SearchAsync(q, ct);
}
