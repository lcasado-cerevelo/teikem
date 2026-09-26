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
/// Lote 4 (P1) — panel Vehículos: lista con buscador libre y filtros multi-valor, ficha, alta con código fijo, edición en
/// línea (PATCH), estatus vía StatusService (ACTIVE ↔ MAINTENANCE lateral, INACTIVE terminal = baja definitiva) y el
/// checkbox Activo (IsActive reversible). Nunca DELETE.
/// - El TenantId sale del principal; todo se lee bajo el filtro global de tenant.
/// - El odómetro manual solo se escribe con la fila bloqueada (FleetQueries.LockVehicleAsync) y nunca por debajo de la
///   última lectura registrada en combustible u OT cerrada (VehicleRules.ValidateManualOdometer).
/// - Ningún desbordamiento DECIMAL llega a SQL: FleetRules.DecimalError se aplica antes de guardar (400).
/// </summary>
public sealed class VehicleService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    public const int CodeMaxLength = 30;
    public const string CodeRequiredMessage = "El código del vehículo es obligatorio.";
    public const string DuplicateCodeMessage = "Ya existe un vehículo con ese código.";
    public const string DuplicateVinMessage = "Ya existe un vehículo activo con ese VIN.";
    public const string CodeImmutableMessage = "El código del vehículo se fija al crearlo; no se puede cambiar.";
    public const string RetiredCannotReactivateMessage = "El vehículo está dado de baja definitiva; no se puede reactivar.";
    public const string ToCodeRequiredMessage = "El estatus destino es obligatorio.";

    /// <summary>Campos que el PATCH rechaza aunque lleguen en el cuerpo (van a Extra por no estar en el contrato).</summary>
    private static readonly string[] ImmutableOnPatch = { "code", "homeWarehouseId" };

    private sealed record StatusInfo(string Code, string Label, string? Color, bool IsTerminal);

    // ---------------------------------------------------------------- lista

    public async Task<IReadOnlyList<VehicleListItemDto>> ListAsync(VehicleListQuery q, CancellationToken ct)
    {
        var query = db.Vehicles.AsNoTracking();
        if (!q.IncludeInactive) query = query.Where(v => v.IsActive);

        var typeIds = await LookupFilterAsync(LookupDomains.VehicleType, q.VehicleType, "vehicleType", "Tipo de vehículo desconocido", ct);
        if (typeIds is not null) query = query.Where(v => v.VehicleTypeLookupId != null && typeIds.Contains(v.VehicleTypeLookupId.Value));
        var ownershipIds = await LookupFilterAsync(LookupDomains.Ownership, q.Ownership, "ownership", "Propiedad desconocida", ct);
        if (ownershipIds is not null) query = query.Where(v => v.OwnershipLookupId != null && ownershipIds.Contains(v.OwnershipLookupId.Value));
        var fuelIds = await LookupFilterAsync(LookupDomains.FuelType, q.FuelType, "fuelType", "Tipo de combustible desconocido", ct);
        if (fuelIds is not null) query = query.Where(v => v.FuelTypeLookupId != null && fuelIds.Contains(v.FuelTypeLookupId.Value));

        var statusMap = await StatusMapAsync(ct);
        var statusCodes = SplitCodes(q.Status);
        if (statusCodes.Count > 0)
        {
            var statusIds = new List<int>();
            foreach (var code in statusCodes)
            {
                var hit = statusMap.FirstOrDefault(kv => string.Equals(kv.Value.Code, code, StringComparison.OrdinalIgnoreCase));
                if (hit.Value is null) throw new ValidationException("status", $"Estatus desconocido: '{code}'.");
                statusIds.Add(hit.Key);
            }
            query = query.Where(v => statusIds.Contains(v.StatusCodeId));
        }

        var rows = await query.OrderBy(v => v.Code).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<VehicleListItemDto>();

        var nextExpiry = await NextDocumentExpiryAsync(rows.Select(v => v.VehicleId).ToList(), ct);

        var items = new List<VehicleListItemDto>(rows.Count);
        foreach (var v in rows)
        {
            var type = await LookupAsync(v.VehicleTypeLookupId, ct);
            var ownership = await LookupAsync(v.OwnershipLookupId, ct);
            var fuel = await LookupAsync(v.FuelTypeLookupId, ct);
            var typeLabel = OptionalLabel(type);
            var ownershipLabel = OptionalLabel(ownership);
            var fuelLabel = OptionalLabel(fuel);

            // qbox en memoria: sin distinguir acentos ni mayúsculas, sobre identidad, etiquetas y códigos de catálogo.
            if (!string.IsNullOrWhiteSpace(q.Search)
                && !FleetRules.MatchesSearch(q.Search, v.Code, v.PlateNumber, typeLabel, type?.InternalCode, ownershipLabel, ownership?.InternalCode,
                    fuelLabel, fuel?.InternalCode, v.Vin, v.Make, v.Model))
                continue;

            var status = statusMap.GetValueOrDefault(v.StatusCodeId);
            items.Add(new VehicleListItemDto(v.VehicleId, v.PublicId, v.Code, v.PlateNumber,
                type?.InternalCode, typeLabel, ownership?.InternalCode, ownershipLabel, fuel?.InternalCode, fuelLabel,
                v.Make, v.Model, v.ModelYear, v.Vin, v.CurrentOdometerKm,
                status?.Code ?? "", status?.Label ?? "", status?.Color, v.IsActive,
                nextExpiry.TryGetValue(v.VehicleId, out var next) ? next : null));
        }
        return items;
    }

    // ---------------------------------------------------------------- ficha

    public async Task<VehicleDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var v = await db.ResolveVehicleAsync(publicId, false, ct);
        var statusMap = await StatusMapAsync(ct);
        var status = statusMap.GetValueOrDefault(v.StatusCodeId);
        var type = await LookupAsync(v.VehicleTypeLookupId, ct);
        var ownership = await LookupAsync(v.OwnershipLookupId, ct);
        var fuel = await LookupAsync(v.FuelTypeLookupId, ct);

        // Documentos activos del vehículo, siempre a través del vehículo resuelto (VehicleDocument no lleva TenantId).
        var docs = await db.Vehicles.AsNoTracking().Where(x => x.VehicleId == v.VehicleId)
            .SelectMany(x => x.Documents).Where(d => d.IsActive).ToListAsync(ct);
        var documents = await VehicleDocumentService.ToDtosAsync(v, docs, lookups, tenant.Lang, ct);

        return new VehicleDetailDto(v.VehicleId, v.PublicId, v.Code, v.PlateNumber, v.MaxWeightKg, v.MaxVolumeM3, v.MaxStops,
            type?.InternalCode, OptionalLabel(type), ownership?.InternalCode, OptionalLabel(ownership), fuel?.InternalCode, OptionalLabel(fuel),
            v.Make, v.Model, v.ModelYear, v.Vin, v.CurrentOdometerKm,
            status?.Code ?? "", status?.Label ?? "", status?.IsTerminal ?? false, v.IsActive,
            documents, v.CreatedAtUtc, v.UpdatedAtUtc, Convert.ToBase64String(v.RowVersion ?? Array.Empty<byte>()));
    }

    // ---------------------------------------------------------------- alta

    public async Task<VehicleDetailDto> CreateAsync(VehicleCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var errors = new Dictionary<string, string[]>();

        var (code, codeError) = FleetRules.NormalizeCode(req.Code, CodeMaxLength, CodeRequiredMessage);
        if (codeError is not null) errors["code"] = new[] { codeError };

        var plate = Text(req.PlateNumber, "plateNumber", 20, errors);
        var make = Text(req.Make, "make", 60, errors);
        var model = Text(req.Model, "model", 60, errors);
        var (vin, vinError) = VehicleRules.NormalizeVin(req.Vin);
        if (vinError is not null) errors["vin"] = new[] { vinError };
        ValidateNumbers(req.MaxWeightKg, req.MaxVolumeM3, req.MaxStops, req.ModelYear, req.CurrentOdometerKm, errors);

        var typeId = await LookupByCodeAsync(LookupDomains.VehicleType, req.VehicleType, "vehicleType", "Tipo de vehículo desconocido", errors, ct);
        var ownershipId = await LookupByCodeAsync(LookupDomains.Ownership, req.Ownership, "ownership", "Propiedad desconocida", errors, ct);
        var fuelId = await LookupByCodeAsync(LookupDomains.FuelType, req.FuelType, "fuelType", "Tipo de combustible desconocido", errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        // El código no se libera tras la baja: el duplicado se busca también entre inactivos (UQ_Vehicle_Code sin filtro).
        if (await db.Vehicles.AnyAsync(v => v.Code == code, ct)) throw new ConflictException(DuplicateCodeMessage);
        if (vin is not null && await db.Vehicles.AnyAsync(v => v.IsActive && v.Vin == vin, ct)) throw new ConflictException(DuplicateVinMessage);

        var initial = await statuses.GetInitialAsync(StatusDomains.VehicleStatus, ct);

        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var vehicle = new Vehicle
            {
                TenantId = tenantId, Code = code!, PlateNumber = plate, Make = make, Model = model, ModelYear = req.ModelYear, Vin = vin,
                MaxWeightKg = req.MaxWeightKg, MaxVolumeM3 = req.MaxVolumeM3, MaxStops = req.MaxStops, CurrentOdometerKm = req.CurrentOdometerKm,
                VehicleTypeLookupId = typeId, OwnershipLookupId = ownershipId, FuelTypeLookupId = fuelId,
                StatusCodeId = initial.StatusCodeId, IsActive = true,
            };
            db.Vehicles.Add(vehicle);
            await db.SaveGuardedAsync(DuplicateCodeMessage, ct2);
            // Historial de estatus desde el inicio (etapa inicial habilitada del tenant: ACTIVE en el seed).
            var to = await statuses.TransitionAsync(StatusDomains.VehicleStatus, EntityTypes.Vehicle, vehicle.VehicleId, null, initial.InternalCode, null, ct2);
            vehicle.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DuplicateCodeMessage, ct2);
            return vehicle.PublicId;
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- edición en línea

    public async Task<VehicleDetailDto> UpdateAsync(Guid publicId, VehiclePatchRequest req, CancellationToken ct)
    {
        if (req.Extra is not null)
            foreach (var key in req.Extra.Keys)
            {
                var hit = ImmutableOnPatch.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) throw new ValidationException(hit, CodeImmutableMessage);
            }

        var errors = new Dictionary<string, string[]>();
        var plate = Text(req.PlateNumber, "plateNumber", 20, errors);
        var make = Text(req.Make, "make", 60, errors);
        var model = Text(req.Model, "model", 60, errors);
        var (vin, vinError) = VehicleRules.NormalizeVin(req.Vin);
        if (vinError is not null) errors["vin"] = new[] { vinError };
        ValidateNumbers(req.MaxWeightKg, req.MaxVolumeM3, req.MaxStops, req.ModelYear, req.CurrentOdometerKm, errors);

        // null = sin cambio; "" = quitar el valor (columnas opcionales).
        var typeId = await LookupByCodeAsync(LookupDomains.VehicleType, req.VehicleType, "vehicleType", "Tipo de vehículo desconocido", errors, ct);
        var ownershipId = await LookupByCodeAsync(LookupDomains.Ownership, req.Ownership, "ownership", "Propiedad desconocida", errors, ct);
        var fuelId = await LookupByCodeAsync(LookupDomains.FuelType, req.FuelType, "fuelType", "Tipo de combustible desconocido", errors, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var current = await db.ResolveVehicleAsync(publicId, false, ct2);
            // Con odómetro: la fila se bloquea (UPDLOCK, ROWLOCK) para no competir con cargas de combustible ni cierres de OT.
            var vehicle = req.CurrentOdometerKm.HasValue
                ? await db.LockVehicleAsync(current.VehicleId, ct2) ?? throw new NotFoundException("Vehículo")
                : await db.ResolveVehicleAsync(publicId, true, ct2);
            db.ApplyRowVersion(vehicle, req.RowVersion);

            if (req.PlateNumber is not null) vehicle.PlateNumber = plate;
            if (req.Make is not null) vehicle.Make = make;
            if (req.Model is not null) vehicle.Model = model;
            if (req.ModelYear.HasValue) vehicle.ModelYear = req.ModelYear;
            if (req.MaxWeightKg.HasValue) vehicle.MaxWeightKg = req.MaxWeightKg;
            if (req.MaxVolumeM3.HasValue) vehicle.MaxVolumeM3 = req.MaxVolumeM3;
            if (req.MaxStops.HasValue) vehicle.MaxStops = req.MaxStops;
            if (req.VehicleType is not null) vehicle.VehicleTypeLookupId = typeId;
            if (req.Ownership is not null) vehicle.OwnershipLookupId = ownershipId;
            if (req.FuelType is not null) vehicle.FuelTypeLookupId = fuelId;
            if (req.Vin is not null)
            {
                if (vin is not null && vehicle.IsActive
                    && await db.Vehicles.AnyAsync(v => v.IsActive && v.Vin == vin && v.VehicleId != vehicle.VehicleId, ct2))
                    throw new ConflictException(DuplicateVinMessage);
                vehicle.Vin = vin;
            }

            if (req.CurrentOdometerKm is decimal km && km != vehicle.CurrentOdometerKm)
            {
                // La corrección manual puede bajar un error tecleado, pero no por debajo de la última lectura registrada.
                var last = await db.LastRecordedOdometerAsync(vehicle.VehicleId, ct2);
                var error = VehicleRules.ValidateManualOdometer(km, last is { } l ? (l.Km, l.Date) : null);
                if (error is not null) throw new ValidationException("currentOdometerKm", error);
                vehicle.CurrentOdometerKm = km;
            }

            await db.SaveGuardedAsync(DuplicateCodeMessage, ct2);
        }, ct);

        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- estatus y baja lógica

    /// <summary>ACTIVE ↔ MAINTENANCE (lateral) y → INACTIVE (terminal, baja definitiva), solo vía StatusService.</summary>
    public async Task<VehicleDetailDto> TransitionStatusAsync(Guid publicId, StatusChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ToCode)) throw new ValidationException("toCode", ToCodeRequiredMessage);
        var vehicle = await db.ResolveVehicleAsync(publicId, true, ct);
        // VehicleStatusEffect pone IsActive=0 en esta misma entidad tracked si el destino es terminal.
        var to = await statuses.TransitionAsync(StatusDomains.VehicleStatus, EntityTypes.Vehicle, vehicle.VehicleId, vehicle.StatusCodeId, req.ToCode.Trim(), req.Comment, ct);
        vehicle.StatusCodeId = to.StatusCodeId;
        await db.SaveGuardedAsync(DuplicateCodeMessage, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Checkbox Activo (IsActive reversible). Un vehículo dado de baja definitiva (terminal) no se reactiva (409).</summary>
    public async Task SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var vehicle = await db.ResolveVehicleAsync(publicId, true, ct);
        if (active && await db.IsTerminalAsync(vehicle.StatusCodeId, ct)) throw new ConflictException(RetiredCannotReactivateMessage);
        if (vehicle.IsActive == active) return;
        if (active && vehicle.Vin is not null
            && await db.Vehicles.AnyAsync(v => v.IsActive && v.Vin == vehicle.Vin && v.VehicleId != vehicle.VehicleId, ct))
            throw new ConflictException(DuplicateVinMessage);
        vehicle.IsActive = active;
        await db.SaveGuardedAsync(DuplicateCodeMessage, ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Próximo vencimiento por vehículo: mínima ExpiryDate de los documentos activos NO superados.</summary>
    private async Task<Dictionary<int, DateOnly>> NextDocumentExpiryAsync(List<int> vehicleIds, CancellationToken ct)
    {
        // Parte de db.Vehicles (filtro de tenant): VehicleDocument no lleva TenantId.
        var docs = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .SelectMany(v => v.Documents).Where(d => d.IsActive && d.ExpiryDate != null)
            .Select(d => new { d.VehicleDocumentId, d.VehicleId, d.DocTypeLookupId, d.DocNumber, d.IssuedDate, d.ExpiryDate })
            .ToListAsync(ct);
        if (docs.Count == 0) return new Dictionary<int, DateOnly>();

        var rows = new List<FleetDocumentRow>(docs.Count);
        foreach (var d in docs)
        {
            var typeCode = (await lookups.GetAsync(d.DocTypeLookupId, ct))?.InternalCode ?? d.DocTypeLookupId.ToString();
            rows.Add(new FleetDocumentRow($"VD-{d.VehicleDocumentId}", FleetOwnerKinds.Vehicle, d.VehicleId, Guid.Empty, "", "", true, false,
                typeCode, d.DocTypeLookupId, typeCode, d.DocNumber, d.IssuedDate, d.ExpiryDate));
        }
        return FleetDocuments.MarkSuperseded(rows)
            .Where(r => !r.IsSuperseded && r.ExpiryDate.HasValue)
            .GroupBy(r => r.OwnerId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.ExpiryDate!.Value));
    }

    private static void ValidateNumbers(decimal? maxWeightKg, decimal? maxVolumeM3, int? maxStops, int? modelYear, decimal? odometerKm,
        IDictionary<string, string[]> errors)
    {
        foreach (var (field, message) in VehicleRules.ValidateCapacities(maxWeightKg, maxVolumeM3, maxStops)) errors[field] = new[] { message };
        if (VehicleRules.ValidateOdometer(odometerKm) is string odoError) errors["currentOdometerKm"] = new[] { odoError };
        if (VehicleRules.ValidateModelYear(modelYear, DateTime.UtcNow.Year) is string yearError) errors["modelYear"] = new[] { yearError };

        // Precisión DECIMAL de las columnas: nunca un 500 por desbordamiento.
        if (!errors.ContainsKey("maxWeightKg") && FleetRules.DecimalError(maxWeightKg, 12, 3) is string w) errors["maxWeightKg"] = new[] { w };
        if (!errors.ContainsKey("maxVolumeM3") && FleetRules.DecimalError(maxVolumeM3, 12, 4) is string vol) errors["maxVolumeM3"] = new[] { vol };
        if (!errors.ContainsKey("currentOdometerKm") && FleetRules.DecimalError(odometerKm, 12, 1) is string o) errors["currentOdometerKm"] = new[] { o };
    }

    /// <summary>null = sin valor/sin cambio; "" = NULL; otro = recortado (error si excede el largo de la columna).</summary>
    private static string? Text(string? value, string field, int max, IDictionary<string, string[]> errors)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v.Length > max) errors[field] = new[] { $"No puede exceder {max} caracteres." };
        return v;
    }

    /// <summary>Código de catálogo → id. null o "" → null; código desconocido → error '{prefijo}: 'X'.' en el campo.</summary>
    private async Task<int?> LookupByCodeAsync(string domain, string? code, string field, string unknownPrefix, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var id = await lookups.TryGetIdAsync(domain, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors[field] = new[] { $"{unknownPrefix}: '{code.Trim()}'." };
        return id;
    }

    /// <summary>Filtro multi-valor por código (acepta ?x=A&amp;x=B y ?x=A,B). Sin valores → null (sin filtro); desconocido → 400.</summary>
    private async Task<List<int>?> LookupFilterAsync(string domain, string[]? codes, string field, string unknownPrefix, CancellationToken ct)
    {
        var list = SplitCodes(codes);
        if (list.Count == 0) return null;
        var ids = new List<int>(list.Count);
        foreach (var code in list)
        {
            var id = await lookups.TryGetIdAsync(domain, code, ct) ?? throw new ValidationException(field, $"{unknownPrefix}: '{code}'.");
            ids.Add(id);
        }
        return ids;
    }

    private static List<string> SplitCodes(string[]? codes)
        => codes is null
            ? new List<string>()
            : codes.Where(c => c is not null)
                .SelectMany(c => c.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(c => c.ToUpperInvariant()).Distinct().ToList();

    private async Task<LookupCode?> LookupAsync(int? id, CancellationToken ct) => id is int v ? await lookups.GetAsync(v, ct) : null;

    /// <summary>Estatus del dominio VehicleStatus con la etiqueta y el color personalizados del tenant si existen.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == StatusDomains.VehicleStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode,
                MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex,
                c.StageKind?.InternalCode == StageKinds.Terminal);
        });
    }

    private string? OptionalLabel(LookupCode? l) => l is null ? null : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
}
