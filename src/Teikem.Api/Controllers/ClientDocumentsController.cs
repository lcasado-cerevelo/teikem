using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 248 (Lote D del plan) — documentos del cliente vistos de todos los clientes: la lista «Documentos por vencer» (clients.read, módulo CATALOG). Los documentos de un
/// cliente (listar, subir, editar, quitar) siguen en /api/v1/clients/{id}/documents. El TenantId sale de la sesión; ninguna acción lo recibe.
/// </summary>
[ApiController]
[Route("api/v1/client-documents")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class ClientDocumentsController(ClientDocumentExpiryService expiry) : ControllerBase
{
    /// <summary>
    /// Documentos por vencer: withinDays (1 a 365; sin valor = el aviso de la compañía), includeExpired (también los vencidos; true por omisión), includeMissing (también
    /// los de un tipo con vigencia sin «hasta»; true por omisión), client (PublicId del cliente) y q (código o nombre del cliente, tipo o número). Sin renovados, por «hasta»
    /// ascendente, tope de 2000 (truncated). Las exenciones viejas sin documento solo salen con tax.manage y el módulo TAX.
    /// </summary>
    [HttpGet("expiring"), RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<ExpiringClientDocumentsDto> Expiring([FromQuery] int? withinDays = null, [FromQuery] bool includeExpired = true, [FromQuery] bool includeMissing = true,
        [FromQuery] Guid? client = null, [FromQuery] string? q = null, CancellationToken ct = default)
        => expiry.ListExpiringAsync(new ExpiringClientDocumentsQuery(withinDays, includeExpired, includeMissing, client, q), ct);
}
