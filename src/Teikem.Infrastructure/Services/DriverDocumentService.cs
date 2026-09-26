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
/// Lote 4 (P2) — Licencias y certificaciones del chofer (detalle de la ficha 'Choferes y tarifas').
/// - Las hijas no llevan TenantId ni PublicId: se alcanzan SIEMPRE a través del chofer del tenant
///   (FleetQueries.ResolveDriverAsync + DriverId). Un id que no pertenece a ese chofer (otro chofer u otro tenant) es
///   404 'Licencia no encontrada.' / 'Certificación no encontrada.' (BOLA por id hijo).
/// - Clase de licencia (LicenseClass) y tipo de certificación (CertificationType) por código del catálogo: 400 si no existe.
/// - Fechas con FleetRules.ValidateDocumentDates; ExpiryState/DaysToExpiry con FleetRules (ventana de 30 días) e
///   IsSuperseded con FleetDocuments.MarkSuperseded sobre los documentos ACTIVOS del chofer ('vigente por tipo').
/// - Nunca DELETE: 'quitar' es IsActive = 0. Un chofer eliminado (estatus terminal) solo se consulta (409).
/// </summary>
public sealed class DriverDocumentService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    private const string LicenseWhat = "Licencia";
    private const string CertificationWhat = "Certificación";

    // ---------------- Licencias ----------------

    public async Task<IReadOnlyList<DriverLicenseDto>> ListLicensesAsync(Guid driverPublicId, bool includeInactive, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
        return await LicensesAsync(db, lookups, tenant.Lang, driver, includeInactive, Today(), ct);
    }

    public async Task<DriverLicenseDto> AddLicenseAsync(Guid driverPublicId, DriverLicenseRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.LicenseClass)) throw new ValidationException("licenseClass", "La clase de licencia es obligatoria.");
        var classId = await LicenseClassIdAsync(req.LicenseClass, ct);
        var (number, numberError) = DriverRules.ValidateLicenseNumber(req.LicenseNumber);
        if (numberError is not null) throw new ValidationException("licenseNumber", numberError);
        var issued = req.ClearIssuedDate == true ? null : req.IssuedDate;
        var expiry = req.ClearExpiryDate == true ? null : req.ExpiryDate;
        EnsureDates(issued, expiry);

        var driver = await LoadWritableDriverAsync(driverPublicId, ct);
        var row = new DriverLicense
        {
            DriverId = driver.DriverId, LicenseClassLookupId = classId, LicenseNumber = number!,
            IssuedDate = issued, ExpiryDate = expiry, IsActive = true,
        };
        db.DriverLicenses.Add(row);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await LicenseDtoAsync(driver, row.DriverLicenseId, ct);
    }

    /// <summary>PATCH: null = sin cambio; clearIssuedDate/clearExpiryDate vacían la fecha. La regla de fechas se evalúa sobre el resultado.</summary>
    public async Task<DriverLicenseDto> UpdateLicenseAsync(Guid driverPublicId, int id, DriverLicenseRequest req, CancellationToken ct)
    {
        int? classId = string.IsNullOrWhiteSpace(req.LicenseClass) ? null : await LicenseClassIdAsync(req.LicenseClass, ct);
        string? number = null;
        if (req.LicenseNumber is not null)
        {
            var (value, error) = DriverRules.ValidateLicenseNumber(req.LicenseNumber);
            if (error is not null) throw new ValidationException("licenseNumber", error);
            number = value;
        }

        var driver = await LoadWritableDriverAsync(driverPublicId, ct);
        var row = await db.DriverLicenses.FirstOrDefaultAsync(l => l.DriverLicenseId == id && l.DriverId == driver.DriverId, ct)
                  ?? throw new NotFoundException(LicenseWhat, null, true);
        var issued = req.ClearIssuedDate == true ? null : req.IssuedDate ?? row.IssuedDate;
        var expiry = req.ClearExpiryDate == true ? null : req.ExpiryDate ?? row.ExpiryDate;
        EnsureDates(issued, expiry);

        if (classId.HasValue) row.LicenseClassLookupId = classId.Value;
        if (number is not null) row.LicenseNumber = number;
        row.IssuedDate = issued;
        row.ExpiryDate = expiry;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await LicenseDtoAsync(driver, row.DriverLicenseId, ct);
    }

    /// <summary>Quitar la licencia conservando historial (IsActive = 0). Idempotente.</summary>
    public async Task<DriverLicenseDto> DeactivateLicenseAsync(Guid driverPublicId, int id, CancellationToken ct)
    {
        var driver = await LoadWritableDriverAsync(driverPublicId, ct);
        var row = await db.DriverLicenses.FirstOrDefaultAsync(l => l.DriverLicenseId == id && l.DriverId == driver.DriverId, ct)
                  ?? throw new NotFoundException(LicenseWhat, null, true);
        if (row.IsActive)
        {
            row.IsActive = false;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        }
        return await LicenseDtoAsync(driver, row.DriverLicenseId, ct);
    }

    // ---------------- Certificaciones ----------------

    public async Task<IReadOnlyList<DriverCertificationDto>> ListCertificationsAsync(Guid driverPublicId, bool includeInactive, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(driverPublicId, false, ct);
        return await CertificationsAsync(db, lookups, tenant.Lang, driver, includeInactive, Today(), ct);
    }

    public async Task<DriverCertificationDto> AddCertificationAsync(Guid driverPublicId, DriverCertificationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CertType)) throw new ValidationException("certType", "El tipo de certificación es obligatorio.");
        var typeId = await CertTypeIdAsync(req.CertType, ct);
        var (number, numberError) = DriverRules.ValidateCertNumber(req.CertNumber);
        if (numberError is not null) throw new ValidationException("certNumber", numberError);
        var issued = req.ClearIssuedDate == true ? null : req.IssuedDate;
        var expiry = req.ClearExpiryDate == true ? null : req.ExpiryDate;
        EnsureDates(issued, expiry);

        var driver = await LoadWritableDriverAsync(driverPublicId, ct);
        var row = new DriverCertification
        {
            DriverId = driver.DriverId, CertTypeLookupId = typeId, CertNumber = number,
            IssuedDate = issued, ExpiryDate = expiry, IsActive = true,
        };
        db.DriverCertifications.Add(row);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await CertificationDtoAsync(driver, row.DriverCertificationId, ct);
    }

    /// <summary>PATCH: null = sin cambio; certNumber "" = vacía el número; clearIssuedDate/clearExpiryDate vacían la fecha.</summary>
    public async Task<DriverCertificationDto> UpdateCertificationAsync(Guid driverPublicId, int id, DriverCertificationRequest req, CancellationToken ct)
    {
        int? typeId = string.IsNullOrWhiteSpace(req.CertType) ? null : await CertTypeIdAsync(req.CertType, ct);
        string? number = null;
        if (req.CertNumber is not null)
        {
            var (value, error) = DriverRules.ValidateCertNumber(req.CertNumber);
            if (error is not null) throw new ValidationException("certNumber", error);
            number = value;
        }

        var driver = await LoadWritableDriverAsync(driverPublicId, ct);
        var row = await db.DriverCertifications.FirstOrDefaultAsync(c => c.DriverCertificationId == id && c.DriverId == driver.DriverId, ct)
                  ?? throw new NotFoundException(CertificationWhat, null, true);
        var issued = req.ClearIssuedDate == true ? null : req.IssuedDate ?? row.IssuedDate;
        var expiry = req.ClearExpiryDate == true ? null : req.ExpiryDate ?? row.ExpiryDate;
        EnsureDates(issued, expiry);

        if (typeId.HasValue) row.CertTypeLookupId = typeId.Value;
        if (req.CertNumber is not null) row.CertNumber = number;
        row.IssuedDate = issued;
        row.ExpiryDate = expiry;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await CertificationDtoAsync(driver, row.DriverCertificationId, ct);
    }

    /// <summary>Quitar la certificación conservando historial (IsActive = 0). Idempotente.</summary>
    public async Task<DriverCertificationDto> DeactivateCertificationAsync(Guid driverPublicId, int id, CancellationToken ct)
    {
        var driver = await LoadWritableDriverAsync(driverPublicId, ct);
        var row = await db.DriverCertifications.FirstOrDefaultAsync(c => c.DriverCertificationId == id && c.DriverId == driver.DriverId, ct)
                  ?? throw new NotFoundException(CertificationWhat, null, true);
        if (row.IsActive)
        {
            row.IsActive = false;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        }
        return await CertificationDtoAsync(driver, row.DriverCertificationId, ct);
    }

    // ---------------- Proyecciones compartidas con DriverService (ficha del chofer) ----------------

    /// <summary>
    /// Licencias del chofer con estado de vencimiento. IsSuperseded se calcula solo sobre las ACTIVAS (FleetDocuments):
    /// una inactiva nunca supera ni queda superada. Orden: activas primero, luego vencimiento ascendente (sin fecha al final).
    /// </summary>
    internal static async Task<IReadOnlyList<DriverLicenseDto>> LicensesAsync(TeikemDbContext db, ILookupCache lookups, string lang,
        Driver driver, bool includeInactive, DateOnly today, CancellationToken ct)
    {
        var q = db.DriverLicenses.AsNoTracking().Where(l => l.DriverId == driver.DriverId);
        if (!includeInactive) q = q.Where(l => l.IsActive);
        var rows = await q.ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<DriverLicenseDto>();

        var classes = new Dictionary<int, LookupCode?>();
        foreach (var id in rows.Select(r => r.LicenseClassLookupId).Distinct()) classes[id] = await lookups.GetAsync(id, ct);

        var superseded = SupersededKeys(driver, FleetDocumentKinds.License, rows.Where(r => r.IsActive).Select(r =>
            (Key: LicenseKey(r.DriverLicenseId), TypeId: r.LicenseClassLookupId, TypeCode: classes[r.LicenseClassLookupId]?.InternalCode ?? r.LicenseClassLookupId.ToString(),
             Number: (string?)r.LicenseNumber, r.IssuedDate, r.ExpiryDate)));

        return rows
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.ExpiryDate ?? DateOnly.MaxValue).ThenBy(r => r.DriverLicenseId)
            .Select(r =>
            {
                var cls = classes[r.LicenseClassLookupId];
                return new DriverLicenseDto(r.DriverLicenseId, cls?.InternalCode ?? "", Label(cls, lang), r.LicenseNumber,
                    r.IssuedDate, r.ExpiryDate, FleetRules.ExpiryState(r.ExpiryDate, today), r.ExpiryDate is DateOnly e ? FleetRules.DaysTo(e, today) : (int?)null,
                    superseded.Contains(LicenseKey(r.DriverLicenseId)), r.IsActive);
            }).ToList();
    }

    /// <summary>Certificaciones del chofer con estado de vencimiento; misma regla de 'vigente por tipo' que las licencias.</summary>
    internal static async Task<IReadOnlyList<DriverCertificationDto>> CertificationsAsync(TeikemDbContext db, ILookupCache lookups, string lang,
        Driver driver, bool includeInactive, DateOnly today, CancellationToken ct)
    {
        var q = db.DriverCertifications.AsNoTracking().Where(c => c.DriverId == driver.DriverId);
        if (!includeInactive) q = q.Where(c => c.IsActive);
        var rows = await q.ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<DriverCertificationDto>();

        var types = new Dictionary<int, LookupCode?>();
        foreach (var id in rows.Select(r => r.CertTypeLookupId).Distinct()) types[id] = await lookups.GetAsync(id, ct);

        var superseded = SupersededKeys(driver, FleetDocumentKinds.Certification, rows.Where(r => r.IsActive).Select(r =>
            (Key: CertificationKey(r.DriverCertificationId), TypeId: r.CertTypeLookupId, TypeCode: types[r.CertTypeLookupId]?.InternalCode ?? r.CertTypeLookupId.ToString(),
             Number: r.CertNumber, r.IssuedDate, r.ExpiryDate)));

        return rows
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.ExpiryDate ?? DateOnly.MaxValue).ThenBy(r => r.DriverCertificationId)
            .Select(r =>
            {
                var type = types[r.CertTypeLookupId];
                return new DriverCertificationDto(r.DriverCertificationId, type?.InternalCode ?? "", Label(type, lang), r.CertNumber,
                    r.IssuedDate, r.ExpiryDate, FleetRules.ExpiryState(r.ExpiryDate, today), r.ExpiryDate is DateOnly e ? FleetRules.DaysTo(e, today) : (int?)null,
                    superseded.Contains(CertificationKey(r.DriverCertificationId)), r.IsActive);
            }).ToList();
    }

    /// <summary>Claves (RowKey) de los documentos activos superados por otro del mismo tipo con vencimiento posterior.</summary>
    private static HashSet<string> SupersededKeys(Driver driver, string documentKind,
        IEnumerable<(string Key, int TypeId, string TypeCode, string? Number, DateOnly? IssuedDate, DateOnly? ExpiryDate)> active)
    {
        var rows = active.Select(a => new FleetDocumentRow(a.Key, FleetOwnerKinds.Driver, driver.DriverId, driver.PublicId, driver.EmployeeCode,
            driver.FullName, driver.IsActive, false, documentKind, a.TypeId, a.TypeCode, a.Number, a.IssuedDate, a.ExpiryDate)).ToList();
        if (rows.Count < 2) return new HashSet<string>();
        return FleetDocuments.MarkSuperseded(rows).Where(r => r.IsSuperseded).Select(r => r.RowKey).ToHashSet();
    }

    private static string LicenseKey(int id) => "DL-" + id;
    private static string CertificationKey(int id) => "DC-" + id;

    // ---------------- Helpers ----------------

    /// <summary>Chofer del tenant (sin tracking) que admite cambios: 404 si no existe; 409 si fue eliminado (terminal).</summary>
    private async Task<Driver> LoadWritableDriverAsync(Guid publicId, CancellationToken ct)
    {
        var driver = await db.ResolveDriverAsync(publicId, false, ct);
        if (await db.IsTerminalAsync(driver.StatusCodeId, ct)) throw new ConflictException(DriverRules.RetiredReadOnlyMessage);
        return driver;
    }

    private async Task<DriverLicenseDto> LicenseDtoAsync(Driver driver, int id, CancellationToken ct)
        => (await LicensesAsync(db, lookups, tenant.Lang, driver, true, Today(), ct)).First(l => l.Id == id);

    private async Task<DriverCertificationDto> CertificationDtoAsync(Driver driver, int id, CancellationToken ct)
        => (await CertificationsAsync(db, lookups, tenant.Lang, driver, true, Today(), ct)).First(c => c.Id == id);

    private async Task<int> LicenseClassIdAsync(string code, CancellationToken ct)
        => await lookups.TryGetIdAsync(LookupDomains.LicenseClass, code.Trim().ToUpperInvariant(), ct)
           ?? throw new ValidationException("licenseClass", $"Clase de licencia desconocida: '{code.Trim()}'.");

    private async Task<int> CertTypeIdAsync(string code, CancellationToken ct)
        => await lookups.TryGetIdAsync(LookupDomains.CertificationType, code.Trim().ToUpperInvariant(), ct)
           ?? throw new ValidationException("certType", $"Tipo de certificación desconocido: '{code.Trim()}'.");

    private static void EnsureDates(DateOnly? issued, DateOnly? expiry)
    {
        var error = FleetRules.ValidateDocumentDates(issued, expiry);
        if (error is not null) throw new ValidationException("expiryDate", error);
    }

    private static string Label(LookupCode? l, string lang) => l is null ? "" : MultilingualText.Resolve(l.LabelJson, lang);
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
