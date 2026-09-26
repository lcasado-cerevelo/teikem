using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P2) — CRUD mínimo de zonas de despacho (código, nombre, activo). El nombre de la zona primaria es el 'Área'
/// del chofer.
/// - Código obligatorio, en mayúsculas, máximo 20, único por compañía (UQ_DispatchZone) e inmutable.
/// - Nunca DELETE: inactivar (IsActive = 0) exige que no tenga choferes activos asignados (409).
///
/// Lote 5 (P5) — Miembros de la zona (DispatchZoneMember: código postal, rango postal o municipio) y resolución
/// 'ZIP/pueblo → zona' para Despacho y el escaneo Outbound:
/// - El criterio es un LookupCode del dominio ZoneMatchType (se resuelve con ILookupCache; nunca un string suelto).
///   El valor se normaliza con DispatchZoneMatcher.NormalizeMember (400 con su mensaje exacto; POLYGON también es 400).
/// - Sin solapamiento entre zonas ACTIVAS del tenant (409 que nombra la zona dueña). UQ_DispatchZoneMember
///   (DispatchZoneId, MatchTypeLookupId, MatchValue) es la segunda barrera contra el repetido en la misma zona.
/// - Quitar un miembro es un DELETE físico auditado bajo DISPATCH_ZONE; el miembro debe ser de ESA zona del tenant
///   (BOLA por id hijo: DispatchZoneMember no tiene TenantId y solo se alcanza por una zona ya filtrada).
/// - Inactivar una zona con rutas abiertas (DRAFT/PLANNED) es 409; reactivarla con miembros que chocan con otra zona
///   activa es 409 con el mensaje de conflicto.
/// </summary>
public sealed class DispatchZoneService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    // ---------------- Mensajes exactos (los cita el manual y la FAQ) ----------------

    public const string MatchTypeRequiredMessage = "Indique el criterio de la zona (POSTAL_CODE, POSTAL_RANGE o MUNICIPALITY).";
    public const string DuplicateMemberMessage = "La zona ya tiene ese criterio.";
    public const string ResolveParamsRequiredMessage = "Indique el código postal o el pueblo.";
    public const string ZoneHasOpenTripsMessage = "La zona tiene rutas abiertas; ciérrelas o cámbielas de zona antes de inactivarla.";

    /// <summary>400 'Criterio de zona desconocido: 'X'.'</summary>
    public static string UnknownMatchTypeMessage(string? matchType) => $"Criterio de zona desconocido: '{matchType}'.";

    /// <summary>409 'El valor '{v}' ya pertenece a la zona {código}.'</summary>
    public static string MemberConflictMessage(string value, string zoneCode) => $"El valor '{value}' ya pertenece a la zona {zoneCode}.";

    // ---------------- Lote 4: zonas ----------------

    public async Task<IReadOnlyList<DispatchZoneDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.DispatchZones.AsNoTracking();
        if (!includeInactive) q = q.Where(z => z.IsActive);
        var zones = await q.OrderBy(z => z.Code).ToListAsync(ct);
        if (zones.Count == 0) return Array.Empty<DispatchZoneDto>();
        var counts = await DriverCountsAsync(zones.Select(z => z.DispatchZoneId).ToList(), ct);
        return zones.Select(z => ToDto(z, counts.GetValueOrDefault(z.DispatchZoneId))).ToList();
    }

    public async Task<DispatchZoneDto> CreateAsync(DispatchZoneRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (code, codeError) = FleetRules.NormalizeCode(req.Code, DriverRules.ZoneCodeMaxLength, DriverRules.ZoneCodeRequiredMessage);
        if (codeError is not null) throw new ValidationException("code", codeError);
        var (name, nameError) = DriverRules.ValidateZoneName(req.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);
        if (await db.DispatchZones.AnyAsync(z => z.Code == code, ct)) throw new ConflictException(DriverRules.DuplicateZoneMessage);

        var zone = new DispatchZone { TenantId = tenantId, Code = code!, Name = name, IsActive = true };
        db.DispatchZones.Add(zone);
        await db.SaveGuardedAsync(DriverRules.DuplicateZoneMessage, ct); // UQ_DispatchZone (TenantId, Code) es la segunda barrera
        return ToDto(zone, 0);
    }

    /// <summary>Solo el nombre es editable ("" lo vacía). El código se fija al crear la zona: si llega, 400.</summary>
    public async Task<DispatchZoneDto> UpdateAsync(int id, DispatchZoneRequest req, CancellationToken ct)
    {
        if (req.Code is not null) throw new ValidationException("code", DriverRules.ZoneCodeImmutableMessage);
        var zone = await LoadAsync(id, ct);
        if (req.Name is not null)
        {
            var (name, nameError) = DriverRules.ValidateZoneName(req.Name);
            if (nameError is not null) throw new ValidationException("name", nameError);
            zone.Name = name;
            await db.SaveGuardedAsync(DriverRules.DuplicateZoneMessage, ct);
        }
        return ToDto(zone, (await DriverCountsAsync(new List<int> { zone.DispatchZoneId }, ct)).GetValueOrDefault(zone.DispatchZoneId));
    }

    /// <summary>
    /// Baja lógica o reactivación.
    /// - Inactivar con choferes activos asignados (zona primaria) es 409 (Lote 4); con rutas abiertas (activas, DRAFT o
    ///   PLANNED) también es 409 (Lote 5).
    /// - Reactivar con un miembro que ya pertenece a otra zona activa es 409 con el mensaje de conflicto (Lote 5).
    /// </summary>
    public async Task<DispatchZoneDto> SetActiveAsync(int id, bool active, CancellationToken ct)
    {
        var zone = await LoadAsync(id, ct);
        var count = (await DriverCountsAsync(new List<int> { zone.DispatchZoneId }, ct)).GetValueOrDefault(zone.DispatchZoneId);
        if (zone.IsActive != active)
        {
            if (!active)
            {
                if (count > 0) throw new ConflictException(DriverRules.ZoneHasDriversMessage);
                if (await HasOpenTripsAsync(zone.DispatchZoneId, ct)) throw new ConflictException(ZoneHasOpenTripsMessage);
            }
            else
            {
                var own = await db.DispatchZoneMembers.AsNoTracking()
                    .Where(m => m.DispatchZoneId == zone.DispatchZoneId)
                    .OrderBy(m => m.DispatchZoneMemberId)
                    .Select(m => new { m.MatchTypeLookupId, m.MatchValue })
                    .ToListAsync(ct);
                if (own.Count > 0)
                {
                    var candidates = new List<(string MatchTypeCode, string MatchValue)>(own.Count);
                    foreach (var m in own) candidates.Add((await MatchTypeCodeAsync(m.MatchTypeLookupId, ct), m.MatchValue));
                    var conflict = await FirstConflictAsync(zone.Code, candidates, ct);
                    if (conflict is { } c) throw new ConflictException(MemberConflictMessage(c.Value, c.ZoneCode));
                }
            }
            zone.IsActive = active;
            await db.SaveGuardedAsync(DriverRules.DuplicateZoneMessage, ct);
        }
        return ToDto(zone, count);
    }

    // ---------------- Lote 5: miembros de la zona ----------------

    /// <summary>Miembros de la zona (activa o no) ordenados por criterio y valor. 404 si la zona no es del tenant.</summary>
    public async Task<DispatchZoneMembersDto> ListMembersAsync(int zoneId, CancellationToken ct)
    {
        var zone = await LoadAsync(zoneId, ct, track: false);
        return await MembersDtoAsync(zone, ct);
    }

    /// <summary>
    /// Agrega un criterio a la zona.
    /// - Zona del tenant (404) y activa (400 'La zona de despacho está inactiva.').
    /// - Criterio: LookupCode ZoneMatchType (400 'Criterio de zona desconocido: 'X'.').
    /// - Valor normalizado con DispatchZoneMatcher.NormalizeMember (400; POLYGON también es 400).
    /// - Repetido en la misma zona → 409 'La zona ya tiene ese criterio.' (UQ_DispatchZoneMember es la segunda barrera).
    /// - Choca con otra zona ACTIVA → 409 'El valor '{v}' ya pertenece a la zona {código}.'
    /// Se audita bajo DISPATCH_ZONE (DispatchZoneMember lleva [AuditEntity(DISPATCH_ZONE)]).
    /// </summary>
    public async Task<DispatchZoneMembersDto> AddMemberAsync(int zoneId, DispatchZoneMemberRequest req, CancellationToken ct)
    {
        var zone = await LoadAsync(zoneId, ct);
        if (!zone.IsActive) throw new ValidationException(DriverRules.ZoneInactiveMessage);

        var typeCode = req.MatchType?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(typeCode)) throw new ValidationException("matchType", MatchTypeRequiredMessage);
        var typeId = await lookups.TryGetIdAsync(LookupDomains.ZoneMatchType, typeCode, ct)
                     ?? throw new ValidationException("matchType", UnknownMatchTypeMessage(req.MatchType!.Trim()));

        var (value, valueError) = NormalizeMember(typeCode, req.MatchValue);
        if (valueError is not null) throw new ValidationException("matchValue", valueError);
        var normalized = value!;

        if (await db.DispatchZoneMembers.AnyAsync(m => m.DispatchZoneId == zone.DispatchZoneId
                                                      && m.MatchTypeLookupId == typeId && m.MatchValue == normalized, ct))
            throw new ConflictException(DuplicateMemberMessage);

        var conflict = await FirstConflictAsync(zone.Code, new[] { (typeCode, normalized) }, ct);
        if (conflict is { } c) throw new ConflictException(MemberConflictMessage(c.Value, c.ZoneCode));

        db.DispatchZoneMembers.Add(new DispatchZoneMember
        {
            DispatchZoneId = zone.DispatchZoneId,
            MatchTypeLookupId = typeId,
            MatchValue = normalized,
        });
        await db.SaveGuardedAsync(DuplicateMemberMessage, ct);
        return await MembersDtoAsync(zone, ct);
    }

    /// <summary>
    /// Quita un criterio (DELETE físico auditado). El miembro debe ser de ESA zona del tenant: un id de otra zona u otro
    /// tenant es 404 'Criterio de zona no encontrado.'
    /// </summary>
    public async Task RemoveMemberAsync(int zoneId, int memberId, CancellationToken ct)
    {
        var zone = await LoadAsync(zoneId, ct, track: false);
        var member = await db.DispatchZoneMembers
                         .FirstOrDefaultAsync(m => m.DispatchZoneMemberId == memberId && m.DispatchZoneId == zone.DispatchZoneId, ct)
                     ?? throw new NotFoundException("Criterio de zona");
        db.DispatchZoneMembers.Remove(member);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
    }

    /// <summary>
    /// Resolución 'ZIP/pueblo → zona' sobre los miembros de las zonas ACTIVAS del tenant (precedencia CP &gt; rango postal
    /// &gt; municipio sin acentos; empate en el mismo nivel = ambigua, sin zona). Sin coincidencia: DispatchZoneId null.
    /// </summary>
    public async Task<ZoneResolutionDto> ResolveAsync(string? postalCode, string? city, CancellationToken ct)
    {
        var pc = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();
        var town = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        if (pc is null && town is null) throw new ValidationException(ResolveParamsRequiredMessage);

        var r = await ResolveZoneAsync(pc, town, ct);
        int? zoneId = null;
        if (!r.Ambiguous && !string.IsNullOrEmpty(r.ZoneCode))
        {
            var code = r.ZoneCode;
            zoneId = await db.DispatchZones.AsNoTracking()
                .Where(z => z.IsActive && z.Code == code)
                .Select(z => (int?)z.DispatchZoneId)
                .FirstOrDefaultAsync(ct);
        }
        return new ZoneResolutionDto(pc, town, zoneId, zoneId is null ? null : r.ZoneCode, zoneId is null ? null : r.MatchedBy,
            r.Ambiguous, r.Candidates);
    }

    // ---------------- Helpers ----------------

    private async Task<DispatchZone> LoadAsync(int id, CancellationToken ct, bool track = true)
    {
        var q = track ? db.DispatchZones : db.DispatchZones.AsNoTracking();
        return await q.FirstOrDefaultAsync(z => z.DispatchZoneId == id, ct)
               ?? throw new NotFoundException("Zona de despacho", null, true);
    }

    /// <summary>Choferes activos asignados a cada zona (cualquier fila DriverZone; el chofer bajo el filtro de tenant).</summary>
    private async Task<Dictionary<int, int>> DriverCountsAsync(List<int> zoneIds, CancellationToken ct)
        => await (from dz in db.DriverZones.AsNoTracking()
                  join d in db.Drivers.AsNoTracking() on dz.DriverId equals d.DriverId
                  where zoneIds.Contains(dz.DispatchZoneId) && d.IsActive
                  group dz by dz.DispatchZoneId into g
                  select new { ZoneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneId, x => x.Count, ct);

    /// <summary>¿La zona tiene rutas abiertas (activas, en DRAFT o PLANNED)? El Trip va bajo el filtro de tenant.</summary>
    private async Task<bool> HasOpenTripsAsync(int zoneId, CancellationToken ct)
    {
        var codes = await (from t in db.Trips.AsNoTracking()
                           join s in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals s.StatusCodeId
                           where t.DispatchZoneId == zoneId && t.IsActive
                           select s.InternalCode)
            .Distinct()
            .ToListAsync(ct);
        return codes.Any(TripRules.IsEditable);
    }

    private async Task<DispatchZoneMembersDto> MembersDtoAsync(DispatchZone zone, CancellationToken ct)
    {
        var rows = await db.DispatchZoneMembers.AsNoTracking()
            .Where(m => m.DispatchZoneId == zone.DispatchZoneId)
            .Select(m => new { m.DispatchZoneMemberId, m.MatchTypeLookupId, m.MatchValue })
            .ToListAsync(ct);

        var items = new List<(int Sort, DispatchZoneMemberDto Dto)>(rows.Count);
        foreach (var r in rows)
        {
            var lc = await lookups.GetAsync(r.MatchTypeLookupId, ct);
            var code = lc?.InternalCode ?? string.Empty;
            var label = lc is null ? code : MultilingualText.Resolve(lc.LabelJson, tenant.Lang);
            items.Add((lc?.SortOrder ?? int.MaxValue, new DispatchZoneMemberDto(r.DispatchZoneMemberId, code, label, r.MatchValue)));
        }
        var members = items
            .OrderBy(i => i.Sort)
            .ThenBy(i => i.Dto.MatchValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Dto.Id)
            .Select(i => i.Dto)
            .ToList();
        return new DispatchZoneMembersDto(zone.DispatchZoneId, zone.Code, zone.Name, zone.IsActive, members);
    }

    private async Task<string> MatchTypeCodeAsync(int lookupId, CancellationToken ct)
        => (await lookups.GetAsync(lookupId, ct))?.InternalCode ?? string.Empty;

    private static DispatchZoneDto ToDto(DispatchZone z, int driverCount) => new(z.DispatchZoneId, z.Code, z.Name, driverCount, z.IsActive);

    // ================================================================ adaptadores a las costuras de P0 (DispatchZoneMatcher, TripQueries)
    // Toda dependencia de las firmas de DispatchZoneMatcher y TripQueries.ActiveZoneMembersAsync vive aquí.

    /// <summary>Normaliza el valor del criterio; (valor, null) o (null, mensaje exacto de 400). POLYGON devuelve error.</summary>
    private static (string? Value, string? Error) NormalizeMember(string matchTypeCode, string? rawValue)
        => DispatchZoneMatcher.NormalizeMember(matchTypeCode, rawValue);

    /// <summary>Resolución pura sobre los miembros de las zonas ACTIVAS del tenant (TripQueries.ActiveZoneMembersAsync).</summary>
    private async Task<ZoneResolution> ResolveZoneAsync(string? postalCode, string? city, CancellationToken ct)
    {
        var members = await db.ActiveZoneMembersAsync(ct);
        return DispatchZoneMatcher.Resolve(members, postalCode, city);
    }

    /// <summary>
    /// Primer candidato (criterio, valor normalizado) que choca con un miembro de OTRA zona activa del tenant: devuelve el
    /// valor y el código de la zona dueña, o null. Los miembros activos se cargan una sola vez.
    /// </summary>
    private async Task<(string Value, string ZoneCode)?> FirstConflictAsync(
        string ownZoneCode, IReadOnlyList<(string MatchTypeCode, string MatchValue)> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return null;
        var members = await db.ActiveZoneMembersAsync(ct);
        foreach (var (typeCode, value) in candidates)
        {
            var owner = DispatchZoneMatcher.FindConflict(members, ownZoneCode, typeCode, value);
            if (!string.IsNullOrEmpty(owner)) return (value, owner);
        }
        return null;
    }
}
