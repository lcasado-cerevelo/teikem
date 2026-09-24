using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Capa A: catálogos (CatalogDomain/LookupCode) con override por tenant y listas propias del tenant.</summary>
[ApiController]
[Route("api/v1/catalogs")]
[Authorize]
public sealed class CatalogsController(LookupService lookups) : ControllerBase
{
    [HttpGet("domains")]
    public Task<IReadOnlyList<CatalogDomainDto>> Domains([FromQuery] byte? scope, CancellationToken ct) => lookups.GetDomainsAsync(scope, ct);

    [HttpGet("domains/{domainKey}")]
    public Task<CatalogDomainDto> Domain(string domainKey, CancellationToken ct) => lookups.GetDomainAsync(domainKey, ct);

    /// <summary>Valores del dominio resueltos para el tenant activo (etiqueta por idioma, override aplicado).</summary>
    [HttpGet("{entity}")]
    public Task<IReadOnlyList<LookupValueDto>> Values(string entity, [FromQuery] bool includeDisabled, CancellationToken ct) => lookups.GetValuesAsync(entity, includeDisabled, ct);

    [HttpGet("{entity}/{code}")]
    public Task<LookupValueDto> Value(string entity, string code, CancellationToken ct) => lookups.GetValueAsync(entity, code, ct);

    [HttpPut("{entity}/{code}/override"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public Task<LookupValueDto> SetOverride(string entity, string code, [FromBody] LookupOverrideRequest req, CancellationToken ct) => lookups.SetOverrideAsync(entity, code, req, ct);

    [HttpDelete("{entity}/{code}/override"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public async Task<IActionResult> RemoveOverride(string entity, string code, CancellationToken ct) { await lookups.RemoveOverrideAsync(entity, code, ct); return NoContent(); }

    [HttpPost("{entity}"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public Task<LookupValueDto> AddValue(string entity, [FromBody] LookupCodeUpsertRequest req, CancellationToken ct) => lookups.AddValueAsync(entity, req, ct);

    [HttpPut("{entity}/{code}"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public Task<LookupValueDto> UpdateValue(string entity, string code, [FromBody] LookupCodeUpsertRequest req, CancellationToken ct) => lookups.UpdateValueAsync(entity, code, req, ct);

    /// <summary>Desactivar (no borrar) un valor: sigue leyéndose en el historial, deja de ofrecerse al capturar.</summary>
    [HttpDelete("{entity}/{code}"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public async Task<IActionResult> DeactivateValue(string entity, string code, CancellationToken ct) { await lookups.DeactivateValueAsync(entity, code, false, ct); return NoContent(); }

    [HttpPost("{entity}/{code}/restore"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public async Task<IActionResult> RestoreValue(string entity, string code, CancellationToken ct) { await lookups.DeactivateValueAsync(entity, code, true, ct); return NoContent(); }

    /// <summary>Catálogo de listas del tenant (para campos personalizados tipo lista): crea un dominio propio con sus valores.</summary>
    [HttpPost("lists"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public Task<CatalogDomainDto> CreateList([FromBody] CatalogListCreateRequest req, CancellationToken ct) => lookups.CreateTenantListAsync(req, ct);

    [HttpDelete("lists/{domainKey}"), RequirePermission(PermissionCatalog.AdminCatalogs)]
    public async Task<IActionResult> DeleteList(string domainKey, CancellationToken ct) { await lookups.DeleteTenantListAsync(domainKey, ct); return NoContent(); }
}

/// <summary>Capa B: pipeline de estatus por tenant, capacidades por estatus, entradas laterales e historial.</summary>
[ApiController]
[Route("api/v1/status")]
[Authorize]
public sealed class StatusController(StatusService status) : ControllerBase
{
    /// <summary>Etapas del dominio (ej. OrderStatus) resueltas para el tenant: etiqueta/color/orden/StageKind.</summary>
    [HttpGet("{entity}")]
    public Task<IReadOnlyList<StatusDto>> Pipeline(string entity, [FromQuery] bool includeDisabled, CancellationToken ct) => status.GetPipelineAsync(entity, includeDisabled, ct);

    [HttpGet("{entity}/validate")]
    public Task<PipelineValidationResult> Validate(string entity, CancellationToken ct) => status.ValidatePipelineAsync(entity, null, ct);

    /// <summary>Override del tenant (renombrar, color, habilitar/deshabilitar, reordenar). El validador corre al guardar.</summary>
    [HttpPut("{entity}/{code}/override"), RequirePermission(PermissionCatalog.AdminStatusConfig)]
    public Task<StatusDto> SetOverride(string entity, string code, [FromBody] StatusOverrideRequest req, CancellationToken ct) => status.SetOverrideAsync(entity, code, req, ct);

    [HttpGet("capabilities/{entityType}")]
    public Task<IReadOnlyList<StatusCapabilityDto>> Capabilities(string entityType, CancellationToken ct) => status.GetCapabilitiesAsync(entityType, ct);

    /// <summary>Matriz estatus × acción del tenant (ej. entityType=TRANSPORT_ORDER, statusDomain=OrderStatus).</summary>
    [HttpPut("capabilities/{entityType}"), RequirePermission(PermissionCatalog.AdminStatusConfig)]
    public Task<IReadOnlyList<StatusCapabilityDto>> SetCapabilities(string entityType, [FromQuery] string statusDomain, [FromBody] IList<StatusCapabilityUpsert> items, CancellationToken ct)
        => status.SetCapabilitiesAsync(entityType, statusDomain, items, ct);

    [HttpGet("lateral-entries/{entityType}")]
    public Task<IReadOnlyList<StatusLateralEntryDto>> LateralEntries(string entityType, CancellationToken ct) => status.GetLateralEntriesAsync(entityType, ct);

    [HttpPut("lateral-entries/{entityType}"), RequirePermission(PermissionCatalog.AdminStatusConfig)]
    public Task<IReadOnlyList<StatusLateralEntryDto>> SetLateralEntries(string entityType, [FromQuery] string statusDomain, [FromBody] IList<StatusLateralEntryUpsert> items, CancellationToken ct)
        => status.SetLateralEntriesAsync(entityType, statusDomain, items, ct);

    [HttpGet("history/{entityType}/{entityId:int}")]
    public Task<IReadOnlyList<StatusHistoryDto>> History(string entityType, int entityId, CancellationToken ct) => status.GetHistoryAsync(entityType, entityId, ct);
}

/// <summary>Capa C: contactos múltiples por entidad (asociación polimórfica validada en servicio).</summary>
[ApiController]
[Route("api/v1/contacts")]
[Authorize]
public sealed class ContactsController(ContactPointService contacts) : ControllerBase
{
    [HttpGet("{ownerEntity}/{ownerId:int}")]
    public Task<IReadOnlyList<ContactPointDto>> List(string ownerEntity, int ownerId, [FromQuery] bool includeInactive, CancellationToken ct) => contacts.GetForOwnerAsync(ownerEntity, ownerId, includeInactive, ct);

    [HttpPost("{ownerEntity}/{ownerId:int}"), RequirePermission(PermissionCatalog.ContactsManage)]
    public Task<ContactPointDto> Add(string ownerEntity, int ownerId, [FromBody] ContactPointUpsertRequest req, CancellationToken ct) => contacts.AddAsync(ownerEntity, ownerId, req, ct);

    [HttpPut("{id:int}"), RequirePermission(PermissionCatalog.ContactsManage)]
    public Task<ContactPointDto> Update(int id, [FromBody] ContactPointUpsertRequest req, CancellationToken ct) => contacts.UpdateAsync(id, req, ct);

    [HttpDelete("{id:int}"), RequirePermission(PermissionCatalog.ContactsManage)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct) { await contacts.DeactivateAsync(id, ct); return NoContent(); }
}
