using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 1 (cambios de Almacén) — catálogo global de localidades postales (ciudad ↔ código postal; Puerto Rico). Solo lectura,
/// con el mismo nivel de acceso que la lectura de catálogos de referencia (GET /api/v1/catalogs/{entity}): usuario autenticado,
/// sin permiso ni módulo propios. El formulario de alta y edición de almacén lo usa para llenar ciudad, estado, código postal y país.
/// </summary>
[ApiController]
[Route("api/v1/postal-localities")]
[Authorize]
public sealed class PostalLocalitiesController(PostalLocalityService localities) : ControllerBase
{
    /// <summary>
    /// ?search = ciudad (sin acentos ni mayúsculas; empieza por, luego contiene) o prefijo de código postal ('0094');
    /// ?take (por defecto 20, máximo 100). Sin search devuelve las primeras por ciudad.
    /// </summary>
    [HttpGet]
    public Task<IReadOnlyList<PostalLocalityDto>> Search([FromQuery] string? search, CancellationToken ct, [FromQuery] int take = 20)
        => localities.SearchAsync(search, take, ct);
}
