using Microsoft.EntityFrameworkCore;
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
/// Lote 4 (P1) — documentos del vehículo (registro, seguro, inspección, permiso). VehicleDocument no lleva TenantId ni
/// PublicId: se alcanza SIEMPRE a través del vehículo del tenant (FleetQueries.ResolveVehicleAsync) y el id del documento
/// tiene que pertenecer a ESE vehículo; si no (otro vehículo u otro tenant) responde 404 'Documento no encontrado.'
/// (BOLA por id hijo, sin oráculo). Nunca DELETE: quitar = IsActive 0.
/// - ExpiryState/DaysToExpiry con FleetRules (ventana de 30 días) e IsSuperseded con FleetDocuments.MarkSuperseded sobre
///   los documentos activos del vehículo (documento vigente por tipo): al renovar no hay que desactivar el viejo.
/// - Los adjuntos (FileName/StoragePath) no se exponen: no hay proveedor de archivos en este lote.
/// </summary>
public sealed class VehicleDocumentService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public const int ExpiringWithinDays = 30;
    public const int DocNumberMaxLength = 80;
    public const string DocTypeRequiredMessage = "El tipo de documento es obligatorio.";

    public async Task<IReadOnlyList<VehicleDocumentDto>> ListAsync(Guid vehiclePublicId, bool includeInactive, CancellationToken ct)
    {
        var vehicle = await db.ResolveVehicleAsync(vehiclePublicId, false, ct);
        var docs = await DocumentsOfAsync(vehicle.VehicleId, includeInactive, ct);
        return await ToDtosAsync(vehicle, docs, lookups, tenant.Lang, ct);
    }

    public async Task<VehicleDocumentDto> AddAsync(Guid vehiclePublicId, VehicleDocumentRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        int? docTypeId = null;
        if (string.IsNullOrWhiteSpace(req.DocType)) errors["docType"] = new[] { DocTypeRequiredMessage };
        else docTypeId = await DocTypeIdAsync(req.DocType, errors, ct);
        var number = DocNumber(req.DocNumber, errors);
        var issued = req.ClearIssuedDate == true ? null : req.IssuedDate;
        var expiry = req.ClearExpiryDate == true ? null : req.ExpiryDate;
        if (FleetRules.ValidateDocumentDates(issued, expiry) is string dateError) errors["expiryDate"] = new[] { dateError };
        if (errors.Count > 0) throw new ValidationException(errors);

        var vehicle = await db.ResolveVehicleAsync(vehiclePublicId, false, ct);
        var doc = new VehicleDocument
        {
            VehicleId = vehicle.VehicleId, DocTypeLookupId = docTypeId!.Value, DocNumber = number,
            IssuedDate = issued, ExpiryDate = expiry, IsActive = true,
        };
        db.VehicleDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(vehicle, doc.VehicleDocumentId, ct);
    }

    public async Task<VehicleDocumentDto> UpdateAsync(Guid vehiclePublicId, int id, VehicleDocumentRequest req, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        int? docTypeId = null;
        if (req.DocType is not null)
        {
            if (string.IsNullOrWhiteSpace(req.DocType)) errors["docType"] = new[] { DocTypeRequiredMessage };
            else docTypeId = await DocTypeIdAsync(req.DocType, errors, ct);
        }
        var number = DocNumber(req.DocNumber, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        var vehicle = await db.ResolveVehicleAsync(vehiclePublicId, false, ct);
        var doc = await LoadTrackedAsync(vehicle.VehicleId, id, ct);

        if (docTypeId is int typeId) doc.DocTypeLookupId = typeId;
        if (req.DocNumber is not null) doc.DocNumber = number;
        if (req.ClearIssuedDate == true) doc.IssuedDate = null; else if (req.IssuedDate.HasValue) doc.IssuedDate = req.IssuedDate;
        if (req.ClearExpiryDate == true) doc.ExpiryDate = null; else if (req.ExpiryDate.HasValue) doc.ExpiryDate = req.ExpiryDate;
        // Las fechas se validan sobre el resultado (lo que queda guardado), no solo sobre lo que llegó.
        if (FleetRules.ValidateDocumentDates(doc.IssuedDate, doc.ExpiryDate) is string dateError) throw new ValidationException("expiryDate", dateError);

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(vehicle, doc.VehicleDocumentId, ct);
    }

    /// <summary>Quitar el documento = IsActive 0 (idempotente). Deja de contar para vencimientos y disponibilidad.</summary>
    public async Task<VehicleDocumentDto> DeactivateAsync(Guid vehiclePublicId, int id, CancellationToken ct)
    {
        var vehicle = await db.ResolveVehicleAsync(vehiclePublicId, false, ct);
        var doc = await LoadTrackedAsync(vehicle.VehicleId, id, ct);
        if (doc.IsActive)
        {
            doc.IsActive = false;
            await db.SaveChangesAsync(ct);
        }
        return await ToDtoAsync(vehicle, doc.VehicleDocumentId, ct);
    }

    // ---------------------------------------------------------------- mapeo compartido con VehicleService

    /// <summary>
    /// DTOs de documentos de un vehículo. IsSuperseded se calcula con FleetDocuments.MarkSuperseded sobre los documentos
    /// ACTIVOS de la lista (un inactivo nunca supera ni queda superado). Orden: activos primero, por tipo y vencimiento.
    /// </summary>
    internal static async Task<IReadOnlyList<VehicleDocumentDto>> ToDtosAsync(Vehicle vehicle, IReadOnlyList<VehicleDocument> docs,
        ILookupCache lookups, string lang, CancellationToken ct)
    {
        if (docs.Count == 0) return Array.Empty<VehicleDocumentDto>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var types = new Dictionary<int, (string Code, string Label)>();
        foreach (var typeId in docs.Select(d => d.DocTypeLookupId).Distinct())
        {
            var lc = await lookups.GetAsync(typeId, ct);
            types[typeId] = (lc?.InternalCode ?? typeId.ToString(), lc is null ? "" : MultilingualText.Resolve(lc.LabelJson, lang));
        }

        var rows = docs.Where(d => d.IsActive).Select(d => new FleetDocumentRow(
            RowKey(d.VehicleDocumentId), FleetOwnerKinds.Vehicle, vehicle.VehicleId, vehicle.PublicId, vehicle.Code, vehicle.Code,
            vehicle.IsActive, false, types[d.DocTypeLookupId].Code, d.DocTypeLookupId, types[d.DocTypeLookupId].Code,
            d.DocNumber, d.IssuedDate, d.ExpiryDate)).ToList();
        var superseded = FleetDocuments.MarkSuperseded(rows).Where(r => r.IsSuperseded).Select(r => r.RowKey).ToHashSet();

        return docs
            .OrderByDescending(d => d.IsActive).ThenBy(d => types[d.DocTypeLookupId].Code).ThenBy(d => d.ExpiryDate ?? DateOnly.MaxValue).ThenBy(d => d.VehicleDocumentId)
            .Select(d => new VehicleDocumentDto(d.VehicleDocumentId, types[d.DocTypeLookupId].Code, types[d.DocTypeLookupId].Label, d.DocNumber,
                d.IssuedDate, d.ExpiryDate,
                FleetRules.ExpiryState(d.ExpiryDate, today, ExpiringWithinDays),
                d.ExpiryDate is DateOnly e ? FleetRules.DaysTo(e, today) : (int?)null,
                superseded.Contains(RowKey(d.VehicleDocumentId)), d.IsActive))
            .ToList();
    }

    // ---------------------------------------------------------------- helpers

    private static string RowKey(int id) => $"VD-{id}";

    /// <summary>Documentos del vehículo partiendo de db.Vehicles (filtro de tenant): VehicleDocument no lleva TenantId.</summary>
    private Task<List<VehicleDocument>> DocumentsOfAsync(int vehicleId, bool includeInactive, CancellationToken ct)
    {
        var q = db.Vehicles.AsNoTracking().Where(v => v.VehicleId == vehicleId).SelectMany(v => v.Documents);
        if (!includeInactive) q = q.Where(d => d.IsActive);
        return q.ToListAsync(ct);
    }

    /// <summary>El documento tiene que ser de ESE vehículo (ya resuelto bajo el tenant); si no, 404 sin oráculo.</summary>
    private async Task<VehicleDocument> LoadTrackedAsync(int vehicleId, int id, CancellationToken ct)
        => await db.Vehicles.Where(v => v.VehicleId == vehicleId).SelectMany(v => v.Documents)
               .FirstOrDefaultAsync(d => d.VehicleDocumentId == id, ct)
           ?? throw new NotFoundException("Documento");

    /// <summary>DTO de un documento con IsSuperseded calculado contra los demás documentos activos del vehículo.</summary>
    private async Task<VehicleDocumentDto> ToDtoAsync(Vehicle vehicle, int documentId, CancellationToken ct)
    {
        var docs = await DocumentsOfAsync(vehicle.VehicleId, true, ct);
        var dtos = await ToDtosAsync(vehicle, docs.Where(d => d.IsActive || d.VehicleDocumentId == documentId).ToList(), lookups, tenant.Lang, ct);
        return dtos.First(d => d.Id == documentId);
    }

    private async Task<int?> DocTypeIdAsync(string code, IDictionary<string, string[]> errors, CancellationToken ct)
    {
        var id = await lookups.TryGetIdAsync(LookupDomains.VehicleDocType, code.Trim().ToUpperInvariant(), ct);
        if (id is null) errors["docType"] = new[] { $"Tipo de documento de vehículo desconocido: '{code.Trim()}'." };
        return id;
    }

    /// <summary>null = sin cambio; "" = NULL; más de 80 caracteres → error.</summary>
    private static string? DocNumber(string? value, IDictionary<string, string[]> errors)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v.Length > DocNumberMaxLength) errors["docNumber"] = new[] { $"No puede exceder {DocNumberMaxLength} caracteres." };
        return v;
    }
}
