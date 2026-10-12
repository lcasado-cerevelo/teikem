namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// IP preferida / red de confianza (2026-10-12; docs/ip-preferida-mfa-diseno.md). Alta y cambio comparten el cuerpo: en el alta Name y Cidr son
/// obligatorios; en el cambio lo que viene null no cambia (Note vacío la quita; ClearExpiry quita el vencimiento). ConfirmInternalOnly = «esta IP es solo
/// de la red interna de la empresa» (obligatorio al crear y al cambiar la IP o el rango).
/// </summary>
public sealed record TrustedNetworkRequest(string? Name, string? Cidr, bool? IsActive = null, DateTime? ExpiresAtUtc = null, bool ClearExpiry = false,
    string? Note = null, bool? ConfirmInternalOnly = null);

/// <summary>
/// Una entrada de la lista. AddressCount = cuántas direcciones cubre (texto: en IPv6 no cabe en un entero de 64 bits). IsExpired = tiene vencimiento
/// y ya pasó (no vale aunque esté activa). ContainsCurrentIp = la IP desde la que se hizo esta consulta cae dentro («Usted está aquí»).
/// </summary>
public sealed record TrustedNetworkDto(Guid PublicId, string Name, string Cidr, bool IsActive, DateTime? ExpiresAtUtc, bool IsExpired, string? Note,
    DateTime? InternalOnlyConfirmedAtUtc, string? InternalOnlyConfirmedByName, DateTime CreatedAtUtc, string AddressCount, bool ContainsCurrentIp);

/// <summary>
/// La lista de la compañía. MaxEntries = máximo de entradas (null = sin tope). FeatureEnabled = el interruptor global del servidor
/// (<c>Auth:TrustedNetworks:Enabled</c>): apagado, la lista se puede cargar pero NO salta el código MFA. CurrentIp = la IP desde la que se consulta
/// (para «Agregar esta IP»); CurrentIpNetworkName = nombre de la entrada vigente que la cubre, si alguna.
/// </summary>
public sealed record TrustedNetworksDto(IReadOnlyList<TrustedNetworkDto> Items, int? MaxEntries, bool FeatureEnabled, string? CurrentIp, string? CurrentIpNetworkName);

/// <summary>Cambia el máximo de entradas de la compañía (null = sin tope).</summary>
public sealed record TrustedNetworkLimitRequest(int? MaxEntries);
