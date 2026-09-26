using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P2) — Choferes: maestro de la pantalla 'Choferes y tarifas' (grupo Catálogo).
/// - 'Código' = Driver.EmployeeCode: obligatorio, en mayúsculas, único por compañía aunque el otro esté eliminado, inmutable.
/// - 'Zona/Área' = zona de despacho primaria (DriverZone.IsPrimary); el Área es el nombre de esa zona.
/// - Tope de paradas efectivo = el propio o, si no tiene, Tenant.MaxStopsPerRouteDefault (R4).
/// - Vínculo con un usuario (para la app del Lote 7): exige además admin.users y un usuario INTERNAL con membresía ACTIVE
///   en el tenant, no vinculado a otro chofer (UX_Driver_User es la segunda barrera).
/// - 'Activo' (checkbox) es IsActive reversible; 'Eliminar' (DELETE) transiciona al estatus terminal INACTIVE sin
///   borrado físico. La cascada (usuario, dispositivos, zona, tarifas) la hacen los efectos de DriverStatus.
/// - Todo cambio de estatus pasa por StatusService.TransitionAsync. Un chofer eliminado solo se consulta.
/// - El chofer nace en el estatus inicial del tenant y SIN filas de tarifa (la 'ficha vacía' es la ausencia de filas).
/// </summary>
public sealed class DriverService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses, PermissionService permissions)
{
    private static readonly string[] ImmutableCodeKeys = ["code", "employeeCode"];

    // ---------------- Lista ----------------

    public async Task<IReadOnlyList<DriverListItemDto>> ListAsync(DriverListQuery query, CancellationToken ct)
    {
        var statusMap = await StatusMapAsync(ct);
        var statusIds = ResolveStatusFilter(query.Status, statusMap);

        var q = db.Drivers.AsNoTracking();
        if (!query.IncludeInactive) q = q.Where(d => d.IsActive);
        if (statusIds is not null) q = q.Where(d => statusIds.Contains(d.StatusCodeId));
        if (query.DispatchZoneId.HasValue)
        {
            var zoneId = query.DispatchZoneId.Value;
            q = q.Where(d => db.DriverZones.Any(z => z.DriverId == d.DriverId && z.IsPrimary && z.DispatchZoneId == zoneId));
        }
        var drivers = await q.OrderBy(d => d.EmployeeCode).ToListAsync(ct);
        if (drivers.Count == 0) return Array.Empty<DriverListItemDto>();

        var ids = drivers.Select(d => d.DriverId).ToList();
        var zones = await PrimaryZonesAsync(ids, ct);
        var tenantDefault = await TenantMaxStopsDefaultAsync(ct);
        var licenseExpiry = await NextLicenseExpiryAsync(drivers, ct);

        var items = new List<DriverListItemDto>(drivers.Count);
        foreach (var d in drivers)
        {
            var zone = zones.GetValueOrDefault(d.DriverId);
            if (!string.IsNullOrWhiteSpace(query.Search)
                && !FleetRules.MatchesSearch(query.Search, d.EmployeeCode, d.FullName, zone?.Code, zone?.Name)) continue;
            var status = statusMap.GetValueOrDefault(d.StatusCodeId);
            items.Add(new DriverListItemDto(d.DriverId, d.PublicId, d.EmployeeCode, d.FullName, zone?.DispatchZoneId, zone?.Code, zone?.Name,
                d.MaxStopsPerRoute, FleetRules.EffectiveMaxStops(d.MaxStopsPerRoute, tenantDefault), d.HireDate,
                status?.Code ?? "", status?.Label ?? "", status?.Color, d.IsActive, licenseExpiry.TryGetValue(d.DriverId, out var exp) ? exp : (DateOnly?)null, d.UserId.HasValue));
        }
        return items;
    }

    // ---------------- Ficha ----------------

    public async Task<DriverDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(publicId, false, ct);
        var statusMap = await StatusMapAsync(ct);
        var status = statusMap.GetValueOrDefault(driver.StatusCodeId);
        var zone = (await PrimaryZonesAsync(new List<int> { driver.DriverId }, ct)).GetValueOrDefault(driver.DriverId);
        var tenantDefault = await TenantMaxStopsDefaultAsync(ct);
        var today = Today();

        DriverUserDto? user = null;
        if (driver.UserId.HasValue)
        {
            var userId = driver.UserId.Value;
            user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new DriverUserDto(u.Id, u.Email ?? "", u.FullName)).FirstOrDefaultAsync(ct);
        }

        var licenses = await DriverDocumentService.LicensesAsync(db, lookups, tenant.Lang, driver, false, today, ct);
        var certifications = await DriverDocumentService.CertificationsAsync(db, lookups, tenant.Lang, driver, false, today, ct);
        var devices = await DevicesAsync(driver.DriverId, false, ct);

        return new DriverDetailDto(driver.DriverId, driver.PublicId, driver.EmployeeCode, driver.FullName,
            zone?.DispatchZoneId, zone?.Code, zone?.Name, driver.MaxStopsPerRoute, FleetRules.EffectiveMaxStops(driver.MaxStopsPerRoute, tenantDefault),
            driver.HireDate, user, status?.Code ?? "", status?.Label ?? "", status?.IsTerminal ?? false, driver.IsActive,
            licenses, certifications, devices, driver.CreatedAtUtc, driver.UpdatedAtUtc, RowVersionOf(driver.RowVersion));
    }

    // ---------------- Alta ----------------

    public async Task<DriverDetailDto> CreateAsync(DriverCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (code, codeError) = FleetRules.NormalizeCode(req.Code, DriverRules.CodeMaxLength, DriverRules.CodeRequiredMessage);
        if (codeError is not null) throw new ValidationException("code", codeError);
        var (fullName, nameError) = DriverRules.ValidateFullName(req.FullName);
        if (nameError is not null) throw new ValidationException("fullName", nameError);
        var stopsError = DriverRules.ValidateMaxStops(req.MaxStopsPerRoute);
        if (stopsError is not null) throw new ValidationException("maxStopsPerRoute", stopsError);
        if (req.DispatchZoneId.HasValue) await EnsureZoneAssignableAsync(req.DispatchZoneId.Value, ct);
        if (req.UserId.HasValue) await ValidateUserLinkAsync(req.UserId.Value, null, ct);
        // Código único aunque el otro chofer esté eliminado (UNIQUE sin filtro); UQ_Driver_EmployeeCode es la segunda barrera.
        if (await db.Drivers.AnyAsync(d => d.EmployeeCode == code, ct)) throw new ConflictException(DriverRules.DuplicateCodeMessage);

        var initial = await statuses.GetInitialAsync(StatusDomains.DriverStatus, ct);
        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = new Driver
            {
                TenantId = tenantId, EmployeeCode = code!, FullName = fullName!, UserId = req.UserId, HireDate = req.HireDate,
                MaxStopsPerRoute = req.MaxStopsPerRoute, StatusCodeId = initial.StatusCodeId, IsActive = true,
            };
            db.Drivers.Add(driver);
            await SaveDriverAsync(ct2);

            // Historial de estatus desde el nacimiento (etapa inicial habilitada del tenant: ACTIVE en el seed).
            var to = await statuses.TransitionAsync(StatusDomains.DriverStatus, EntityTypes.Driver, driver.DriverId, null, initial.InternalCode, null, ct2);
            driver.StatusCodeId = to.StatusCodeId;
            if (req.DispatchZoneId.HasValue)
                db.DriverZones.Add(new DriverZone { DriverId = driver.DriverId, DispatchZoneId = req.DispatchZoneId.Value, IsPrimary = true });
            await SaveDriverAsync(ct2);
            return driver.PublicId;
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------- Edición en línea ----------------

    /// <summary>
    /// PATCH: null = sin cambio. clearZone / clearMaxStopsPerRoute / clearUser vacían el dato (y ganan si llegan junto con
    /// el valor). Cambiar o quitar el usuario vinculado exige además admin.users. El código no se cambia (400).
    /// </summary>
    public async Task<DriverDetailDto> UpdateAsync(Guid publicId, DriverPatchRequest req, CancellationToken ct)
    {
        if (req.Extra is not null && req.Extra.Keys.Any(k => ImmutableCodeKeys.Contains(k, StringComparer.OrdinalIgnoreCase)))
            throw new ValidationException("code", DriverRules.CodeImmutableMessage);

        string? fullName = null;
        if (req.FullName is not null)
        {
            var (value, error) = DriverRules.ValidateFullName(req.FullName);
            if (error is not null) throw new ValidationException("fullName", error);
            fullName = value;
        }
        if (req.ClearMaxStopsPerRoute != true)
        {
            var stopsError = DriverRules.ValidateMaxStops(req.MaxStopsPerRoute);
            if (stopsError is not null) throw new ValidationException("maxStopsPerRoute", stopsError);
        }

        var current = await db.ResolveDriverAsync(publicId, false, ct);
        if (await db.IsTerminalAsync(current.StatusCodeId, ct)) throw new ConflictException(DriverRules.RetiredReadOnlyMessage);

        var setZone = req.ClearZone != true && req.DispatchZoneId.HasValue;
        if (setZone) await EnsureZoneAssignableAsync(req.DispatchZoneId!.Value, ct);

        // Solo se exige admin.users cuando el vínculo realmente cambia (el formulario puede reenviar el mismo userId).
        var clearUser = req.ClearUser == true && current.UserId.HasValue;
        var linkUser = req.ClearUser != true && req.UserId.HasValue && req.UserId != current.UserId;
        if (clearUser) await permissions.EnsureAsync(PermissionCatalog.AdminUsers, ct);
        if (linkUser) await ValidateUserLinkAsync(req.UserId!.Value, current.DriverId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await db.ResolveDriverAsync(publicId, true, ct2);
            db.ApplyRowVersion(driver, req.RowVersion);

            if (fullName is not null) driver.FullName = fullName;
            if (req.ClearMaxStopsPerRoute == true) driver.MaxStopsPerRoute = null;
            else if (req.MaxStopsPerRoute.HasValue) driver.MaxStopsPerRoute = req.MaxStopsPerRoute.Value;
            if (req.HireDate.HasValue) driver.HireDate = req.HireDate.Value;
            if (clearUser) driver.UserId = null;
            else if (linkUser) driver.UserId = req.UserId!.Value;

            if (req.ClearZone == true) await ReplacePrimaryZoneAsync(driver.DriverId, null, ct2);
            else if (setZone) await ReplacePrimaryZoneAsync(driver.DriverId, req.DispatchZoneId!.Value, ct2);

            await SaveDriverAsync(ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------- Estatus, activo y baja definitiva ----------------

    /// <summary>ACTIVE ↔ UNAVAILABLE (lateral reversible) vía StatusService; INACTIVE (terminal) equivale a 'Eliminar'.</summary>
    public async Task<DriverDetailDto> TransitionStatusAsync(Guid publicId, StatusChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ToCode)) throw new ValidationException("toCode", "El estatus destino es obligatorio.");
        await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await db.ResolveDriverAsync(publicId, true, ct2);
            await MoveAsync(driver, req.ToCode.Trim(), req.Comment, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Checkbox 'Activo' (IsActive), reversible y sin DELETE. Un chofer eliminado no se reactiva (409).</summary>
    public async Task SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(publicId, true, ct);
        if (active && await db.IsTerminalAsync(driver.StatusCodeId, ct)) throw new ConflictException(DriverRules.CannotReactivateRetiredMessage);
        if (driver.IsActive == active) return;
        driver.IsActive = active;
        await SaveDriverAsync(ct);
    }

    /// <summary>
    /// 'Eliminar chofer' (DELETE): transición al estatus terminal INACTIVE con comentario, sin borrado físico. Los efectos
    /// de DriverStatus desactivan el chofer, desvinculan el usuario, desactivan sus dispositivos, quitan su zona y cierran
    /// hoy sus tarifas abiertas; sus viajes se conservan para pagarle lo pendiente.
    /// </summary>
    public async Task RetireAsync(Guid publicId, DriverRetireRequest? req, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var driver = await db.ResolveDriverAsync(publicId, true, ct2);
            if (await db.IsTerminalAsync(driver.StatusCodeId, ct2)) throw new ConflictException(DriverRules.AlreadyRetiredMessage);
            await MoveAsync(driver, DriverStatuses.Inactive, req?.Comment, ct2);
        }, ct);
    }

    // ---------------- Dispositivos (los registra la app del Lote 7) ----------------

    public async Task<IReadOnlyList<DriverDeviceDto>> GetDevicesAsync(Guid publicId, bool includeInactive, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(publicId, false, ct);
        return await DevicesAsync(driver.DriverId, includeInactive, ct);
    }

    /// <summary>Desactiva el dispositivo (deja de recibir notificaciones). Idempotente; nunca devuelve el PushToken.</summary>
    public async Task<DriverDeviceDto> DeactivateDeviceAsync(Guid publicId, int id, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(publicId, false, ct);
        var device = await db.DriverDevices.FirstOrDefaultAsync(d => d.DriverDeviceId == id && d.DriverId == driver.DriverId, ct)
                     ?? throw new NotFoundException("Dispositivo");
        if (device.IsActive)
        {
            device.IsActive = false;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        }
        return (await DevicesAsync(driver.DriverId, true, ct)).First(d => d.Id == id);
    }

    // ---------------- Helpers ----------------

    /// <summary>
    /// Reglas del vínculo chofer ↔ usuario, en este orden: admin.users (403); usuario de portal del tenant (400, no es
    /// interno); membresía en el tenant (404, no revela usuarios de otras compañías); tipo INTERNAL (400); membresía
    /// ACTIVE (409); no vinculado a otro chofer del tenant (409).
    /// </summary>
    private async Task ValidateUserLinkAsync(int userId, int? currentDriverId, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.AdminUsers, ct);

        var membership = await db.UserTenants.AsNoTracking().Include(m => m.User).Include(m => m.Status)
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);
        if (membership?.User is null)
        {
            // Los usuarios de portal no tienen UserTenant (R39): se reconocen por su fila PortalUser en el tenant.
            if (await db.PortalUsers.AsNoTracking().AnyAsync(p => p.UserId == userId, ct))
                throw new ValidationException("userId", DriverRules.UserNotInternalMessage);
            throw new NotFoundException("Usuario");
        }

        // Sin tipo = interno (misma regla que AuthService).
        var kind = membership.User.UserKindLookupId is null
            ? UserKinds.Internal
            : (await lookups.GetAsync(membership.User.UserKindLookupId.Value, ct))?.InternalCode ?? UserKinds.Internal;
        if (!string.Equals(kind, UserKinds.Internal, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("userId", DriverRules.UserNotInternalMessage);
        if (!string.Equals(membership.Status?.InternalCode, MembershipStatuses.Active, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException(DriverRules.UserMembershipInactiveMessage);

        var q = db.Drivers.AsNoTracking().Where(d => d.UserId == userId);
        if (currentDriverId.HasValue) { var id = currentDriverId.Value; q = q.Where(d => d.DriverId != id); }
        if (await q.AnyAsync(ct)) throw new ConflictException(DriverRules.UserAlreadyLinkedMessage);
    }

    /// <summary>Zona del tenant (404) y activa (400) para asignarla como primaria.</summary>
    private async Task EnsureZoneAssignableAsync(int zoneId, CancellationToken ct)
    {
        var zone = await db.DispatchZones.AsNoTracking().FirstOrDefaultAsync(z => z.DispatchZoneId == zoneId, ct)
                   ?? throw new NotFoundException("Zona de despacho", null, true);
        if (!zone.IsActive) throw new ValidationException("dispatchZoneId", DriverRules.ZoneInactiveMessage);
    }

    /// <summary>
    /// Reemplaza la fila primaria de DriverZone (asociación de estado actual: se borra, el historial queda en AuditLog bajo
    /// DRIVER). Si el chofer ya tenía una fila con la zona nueva (secundaria), se promueve en lugar de insertar otra.
    /// </summary>
    private async Task ReplacePrimaryZoneAsync(int driverId, int? zoneId, CancellationToken ct)
    {
        var rows = await db.DriverZones.Where(z => z.DriverId == driverId).ToListAsync(ct);
        foreach (var r in rows.Where(r => r.IsPrimary && r.DispatchZoneId != zoneId)) db.DriverZones.Remove(r);
        if (zoneId is null) return;
        var existing = rows.FirstOrDefault(r => r.DispatchZoneId == zoneId.Value);
        if (existing is null) db.DriverZones.Add(new DriverZone { DriverId = driverId, DispatchZoneId = zoneId.Value, IsPrimary = true });
        else existing.IsPrimary = true;
    }

    /// <summary>Transición vía StatusService (efectos incluidos) y asignación del estatus en la misma unidad de trabajo.</summary>
    private async Task MoveAsync(Driver driver, string toCode, string? comment, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.DriverStatus, EntityTypes.Driver, driver.DriverId, driver.StatusCodeId, toCode, comment, ct);
        driver.StatusCodeId = to.StatusCodeId;
        await SaveDriverAsync(ct);
    }

    /// <summary>
    /// SaveChanges con traducción a 409: concurrencia (RowVersion) o índice único. Distingue UX_Driver_User (usuario ya
    /// vinculado) de UQ_Driver_EmployeeCode (código repetido) por el nombre del índice en el error de SQL Server.
    /// </summary>
    private async Task SaveDriverAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(DbExtensions.ConcurrencyMessage);
        }
        catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
        {
            var text = ex.InnerException?.Message ?? ex.Message;
            throw new ConflictException(text.Contains("UX_Driver_User", StringComparison.OrdinalIgnoreCase)
                ? DriverRules.UserAlreadyLinkedMessage
                : DriverRules.DuplicateCodeMessage);
        }
    }

    private sealed record ZoneInfo(int DispatchZoneId, string Code, string? Name);

    /// <summary>Zona primaria por chofer (una consulta por lote; DispatchZone bajo el filtro de tenant).</summary>
    private async Task<Dictionary<int, ZoneInfo>> PrimaryZonesAsync(List<int> driverIds, CancellationToken ct)
    {
        var rows = await (from dz in db.DriverZones.AsNoTracking()
                          join z in db.DispatchZones.AsNoTracking() on dz.DispatchZoneId equals z.DispatchZoneId
                          where dz.IsPrimary && driverIds.Contains(dz.DriverId)
                          select new { dz.DriverId, z.DispatchZoneId, z.Code, z.Name })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.DriverId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.DispatchZoneId).Select(r => new ZoneInfo(r.DispatchZoneId, r.Code, r.Name)).First());
    }

    /// <summary>Próximo vencimiento de licencia por chofer: mínima ExpiryDate de las activas NO superadas (vigente por tipo).</summary>
    private async Task<Dictionary<int, DateOnly>> NextLicenseExpiryAsync(IReadOnlyList<Driver> drivers, CancellationToken ct)
    {
        var ids = drivers.Select(d => d.DriverId).ToList();
        var licenses = await db.DriverLicenses.AsNoTracking()
            .Where(l => ids.Contains(l.DriverId) && l.IsActive && l.ExpiryDate != null)
            .Select(l => new { l.DriverLicenseId, l.DriverId, l.LicenseClassLookupId, l.LicenseNumber, l.IssuedDate, l.ExpiryDate })
            .ToListAsync(ct);
        if (licenses.Count == 0) return new Dictionary<int, DateOnly>();

        var byId = drivers.ToDictionary(d => d.DriverId);
        var rows = licenses.Select(l =>
        {
            var d = byId[l.DriverId];
            // DocTypeCode = id de la clase: solo se usa para agrupar (mismo dueño + misma clase), no se muestra.
            return new FleetDocumentRow("DL-" + l.DriverLicenseId, FleetOwnerKinds.Driver, d.DriverId, d.PublicId, d.EmployeeCode, d.FullName,
                d.IsActive, false, FleetDocumentKinds.License, l.LicenseClassLookupId, l.LicenseClassLookupId.ToString(),
                l.LicenseNumber, l.IssuedDate, l.ExpiryDate);
        }).ToList();

        return FleetDocuments.MarkSuperseded(rows)
            .Where(r => !r.IsSuperseded && r.ExpiryDate.HasValue)
            .GroupBy(r => r.OwnerId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.ExpiryDate!.Value));
    }

    private async Task<IReadOnlyList<DriverDeviceDto>> DevicesAsync(int driverId, bool includeInactive, CancellationToken ct)
    {
        var q = db.DriverDevices.AsNoTracking().Where(d => d.DriverId == driverId);
        if (!includeInactive) q = q.Where(d => d.IsActive);
        // Proyección sin PushToken: el token nunca sale de la BD, solo si existe.
        var rows = await q.OrderByDescending(d => d.IsActive).ThenByDescending(d => d.LastSeenUtc).ThenBy(d => d.DriverDeviceId)
            .Select(d => new { d.DriverDeviceId, d.PlatformLookupId, d.AppVersion, HasPushToken = d.PushToken != null, d.LastSeenUtc, d.IsActive })
            .ToListAsync(ct);
        var list = new List<DriverDeviceDto>(rows.Count);
        foreach (var r in rows)
        {
            var platform = await lookups.GetAsync(r.PlatformLookupId, ct);
            list.Add(new DriverDeviceDto(r.DriverDeviceId, platform?.InternalCode ?? "", Label(platform), r.AppVersion, r.HasPushToken, r.LastSeenUtc, r.IsActive));
        }
        return list;
    }

    private async Task<int> TenantMaxStopsDefaultAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        return await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => t.MaxStopsPerRouteDefault).FirstOrDefaultAsync(ct);
    }

    private sealed record StatusInfo(string Code, string Label, string? Color, bool IsTerminal);

    /// <summary>Estatus del dominio DriverStatus con la etiqueta/color del tenant (StatusCodeOverride) si los personalizó.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == StatusDomains.DriverStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(s => s.StatusCodeId, s =>
        {
            var o = overrides.GetValueOrDefault(s.StatusCodeId);
            return new StatusInfo(s.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(s.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? s.ColorHex, s.StageKind?.InternalCode == StageKinds.Terminal);
        });
    }

    /// <summary>Filtro status[] por código del dominio DriverStatus; un código desconocido es 400.</summary>
    private static List<int>? ResolveStatusFilter(string[]? codes, Dictionary<int, StatusInfo> statusMap)
    {
        var wanted = codes?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        if (wanted is null || wanted.Count == 0) return null;
        var ids = new List<int>();
        foreach (var code in wanted)
        {
            var match = statusMap.FirstOrDefault(kv => string.Equals(kv.Value.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match.Value is null) throw new ValidationException("status", $"Estatus de chofer desconocido: '{code}'.");
            ids.Add(match.Key);
        }
        return ids;
    }

    private string Label(LookupCode? l) => l is null ? "" : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
    private static string RowVersionOf(byte[]? rv) => rv is null ? "" : Convert.ToBase64String(rv);
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
