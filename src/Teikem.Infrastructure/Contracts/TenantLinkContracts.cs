namespace Teikem.Infrastructure.Contracts;

// Lote 227 (P7 del pedido de vendedores) — compañías enlazadas (módulo INTERCOMPANY). Records sin lógica: el contrato con el front.

/// <summary>
/// Un enlace visto desde la compañía de quien mira. Role = CLIENT (somos el cliente: despachamos por la otra) o PROVIDER (damos el transporte: la otra es nuestro cliente).
/// OtherCompany* = la otra compañía. ProviderClient* = el cliente del proveedor que la representa: SOLO lo ve el proveedor (al cliente le llega null). CanRespond = el enlace
/// está propuesto por la otra y quien mira puede aceptarlo o rechazarlo; CanEnd = está activo.
/// </summary>
public sealed record TenantLinkDto(Guid PublicId, string Role, Guid OtherCompanyPublicId, string OtherCompanyName, string Status, bool ProposedByUs, string? ProposedByName,
    DateTime ProposedAtUtc, string? Note, string? RespondedByName, DateTime? RespondedAtUtc, string? ResponseNote, DateTime? EndedAtUtc, string? EndReason, bool EndedByUs,
    Guid? ProviderClientPublicId, string? ProviderClientName, bool CanRespond, bool CanEnd, int DeliveryCount);

/// <summary>
/// Proponer un enlace. CompanyPublicId = la otra compañía (una de las que quien propone también pertenece: ver /options). Role = el papel de NUESTRA compañía:
/// CLIENT («somos cliente de la otra») o PROVIDER («la otra es nuestra cliente»; entonces ClientPublicId = nuestro cliente que la representa). La propuesta cuenta como la
/// aceptación de quien propone; falta la de la otra compañía.
/// </summary>
public sealed record TenantLinkProposeRequest(Guid? CompanyPublicId = null, string? Role = null, Guid? ClientPublicId = null, string? Note = null);

/// <summary>Aceptar un enlace propuesto por la otra compañía. Si nuestra compañía es la que da el transporte, ClientPublicId (obligatorio) = nuestro cliente que la representa.</summary>
public sealed record TenantLinkAcceptRequest(Guid? ClientPublicId = null, string? Note = null);

/// <summary>Rechazar un enlace propuesto o terminar uno activo: el motivo (obligatorio, hasta 250).</summary>
public sealed record TenantLinkReasonRequest(string? Reason = null);

/// <summary>Una compañía con la que quien mira se puede enlazar: las otras compañías a las que pertenece (activas). HasModule = tiene encendido INTERCOMPANY; HasTransport = LTL_GROUND.</summary>
public sealed record TenantLinkCompanyDto(Guid PublicId, string Name, bool HasModule, bool HasTransport);

/// <summary>Para el formulario de «Proponer enlace»: las compañías posibles y si la nuestra puede dar el transporte.</summary>
public sealed record TenantLinkOptionsDto(IReadOnlyList<TenantLinkCompanyDto> Companies, bool WeHaveTransport);

/// <summary>
/// Una entrega hecha por la compañía enlazada (desde la compañía cliente). State = PENDING (por mandar o reintentando), CREATED (la orden ya existe allá) o FAILED (la otra
/// compañía la rechazó: Error dice por qué; se reintenta a mano). Provider* = la orden de allá (número, estatus y su etiqueta, final, entrega) tal como se trajo la última vez.
/// </summary>
public sealed record TenantLinkDeliveryDto(Guid PublicId, string State, string? Error, int Attempts, string ProviderCompanyName, Guid? ProviderOrderPublicId, string? ProviderOrderNumber,
    string? ProviderStatus, string? ProviderStatusLabel, bool IsFinal, DateTime? DeliveredAtUtc, string? DeliveredTo, DateTime? LastSyncedAtUtc, DateTime CreatedAtUtc,
    Guid SalesOrderPublicId, string SalesOrderNumber, Guid PickBatchPublicId, string PickBatchNumber);

public sealed record TenantLinkDeliveryPageDto(IReadOnlyList<TenantLinkDeliveryDto> Items, int Total);
