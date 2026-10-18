using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Lote 113 — «Devolver tras N intentos»: con cuántos intentos fallidos se sugiere devolver una orden. Ver: cualquier usuario de la compañía; cambiar: admin.tenant.</summary>
[ApiController]
[Route("api/v1/tenant/return-settings")]
[Authorize]
public sealed class ReturnSettingsController(ReturnSuggestionService suggestion) : ControllerBase
{
    [HttpGet]
    public Task<ReturnSettingsDto> Get(CancellationToken ct) => suggestion.GetSettingsAsync(ct);

    /// <summary>0 = sin sugerencia; 1 a 20 = con cuántos intentos fallidos se sugiere. 400 fuera de rango.</summary>
    [HttpPut, RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<ReturnSettingsDto> Put([FromBody] ReturnSettingsRequest req, CancellationToken ct) => suggestion.SetSettingsAsync(req, ct);
}
