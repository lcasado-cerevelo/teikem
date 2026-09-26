using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 2 (P2): consignatarios y localizaciones. Una misma tabla guarda las direcciones del cliente
/// (CORPORATE = física, BILLING = postal; a lo sumo una activa de cada una por cliente), sus almacenes o puntos de
/// recogido (PICKUP/BOTH), sus consignatarios (DELIVERY) y las localizaciones compartidas del operador (ClientId NULL).
/// Nunca se borra: la baja es IsActive = 0 y conserva el historial (R5). El chequeo de factura duplicada (R36) lo
/// consume el lote de Órdenes; aquí solo se edita la bandera AllowDupInvoice.
/// </summary>
public sealed class LocationService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    private const string DefaultCountry = "PR";
    private const int NameMax = 200, CodeMax = 40, LineMax = 200, CityMax = 100, StateMax = 100, PostalMax = 20, NotesMax = 500;

    // ---------------------------------------------------------------- consultas

    public async Task<IReadOnlyList<LocationDto>> GetListAsync(LocationQuery q, CancellationToken ct)
    {
        var query = Base();

        if (q.ClientPublicId is Guid clientPublicId)
        {
            // 404 si el cliente no es del tenant (ResolveClientAsync corre bajo el filtro global).
            var client = await db.ResolveClientAsync(clientPublicId, ct);
            var clientId = client.ClientId;
            query = q.IncludeShared
                ? query.Where(l => l.ClientId == clientId || l.ClientId == null)
                : query.Where(l => l.ClientId == clientId);
        }

        if (!q.IncludeInactive) query = query.Where(l => l.IsActive);

        if (!string.IsNullOrWhiteSpace(q.LocationType))
        {
            var typeId = await lookups.TryGetIdAsync(LookupDomains.LocationType, q.LocationType.Trim(), ct)
                         ?? throw new ValidationException("locationType", "Tipo de localización desconocido.");
            query = query.Where(l => l.LocationTypeLookupId == typeId);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(l => l.Name.Contains(s)
                                     || (l.Code != null && l.Code.Contains(s))
                                     || l.City.Contains(s)
                                     || l.Line1.Contains(s)
                                     || (l.PostalCode != null && l.PostalCode.Contains(s)));
        }

        var list = await query.OrderBy(l => l.Name).ThenBy(l => l.LocationId).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<LocationDto> GetAsync(Guid publicId, CancellationToken ct)
        => ToDto(await Base().FirstOrDefaultAsync(l => l.PublicId == publicId, ct) ?? throw new NotFoundException("Localización", publicId));

    // ---------------------------------------------------------------- escritura

    public async Task<LocationDto> CreateAsync(LocationUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();

        int? clientId = null;
        if (req.ClientPublicId is Guid clientPublicId)
            clientId = (await db.ResolveClientAsync(clientPublicId, ct)).ClientId;

        var typeCode = RequireText(req.LocationType, "locationType", "El tipo de localización es obligatorio.", 40).ToUpperInvariant();
        var typeId = await lookups.GetIdAsync(LookupDomains.LocationType, typeCode, ct);
        var countryCode = string.IsNullOrWhiteSpace(req.Country) ? DefaultCountry : req.Country.Trim().ToUpperInvariant();
        if (!CountryCode.IsValid(countryCode)) throw new ValidationException("country", CountryCode.InvalidMessage);
        var countryId = await lookups.GetIdAsync(LookupDomains.Country, countryCode, ct);

        ValidateWindow(req.DefaultWindowStart, req.DefaultWindowEnd);
        ValidateServiceMinutes(req.DefaultServiceMinutes);
        EnsureAddressTypeAllowsOwner(typeCode, clientId);
        await EnsureSingleAddressAsync(typeCode, clientId, excludeLocationId: null, ct);

        var loc = new Location
        {
            TenantId = tenantId,
            ClientId = clientId,
            Code = OptionalText(req.Code, "code", CodeMax),
            Name = RequireText(req.Name, "name", "El nombre es obligatorio.", NameMax),
            LocationTypeLookupId = typeId,
            Line1 = RequireText(req.Line1, "line1", "La dirección (línea 1) es obligatoria.", LineMax),
            Line2 = OptionalText(req.Line2, "line2", LineMax),
            City = RequireText(req.City, "city", "La ciudad es obligatoria.", CityMax),
            State = OptionalText(req.State, "state", StateMax),
            PostalCode = OptionalText(req.PostalCode, "postalCode", PostalMax),
            CountryLookupId = countryId,
            DefaultServiceMinutes = req.DefaultServiceMinutes,
            DefaultWindowStart = req.DefaultWindowStart,
            DefaultWindowEnd = req.DefaultWindowEnd,
            AccessNotes = OptionalText(req.AccessNotes, "accessNotes", NotesMax),
            DeliveryNotes = OptionalText(req.DeliveryNotes, "deliveryNotes", NotesMax),
            AllowDupInvoice = req.AllowDupInvoice,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Locations.Add(loc);
        await db.SaveGuardedAsync("Ya existe una localización equivalente.", ct);
        return await GetByIdAsync(loc.LocationId, ct);
    }

    public async Task<LocationDto> UpdateAsync(Guid publicId, LocationPatchRequest req, CancellationToken ct)
    {
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.PublicId == publicId, ct)
                  ?? throw new NotFoundException("Localización", publicId);
        db.ApplyRowVersion(loc, req.RowVersion);

        // Dueño: visible y editable (documento). MakeShared quita el dueño; ClientPublicId lo cambia.
        if (req.MakeShared == true && req.ClientPublicId.HasValue)
            throw new ValidationException("clientPublicId", "Indique un cliente o marque la localización como compartida, no ambos.");
        var clientId = loc.ClientId;
        if (req.MakeShared == true) clientId = null;
        else if (req.ClientPublicId is Guid clientPublicId) clientId = (await db.ResolveClientAsync(clientPublicId, ct)).ClientId;

        // Tipo (código) actual o nuevo.
        var currentType = await lookups.GetAsync(loc.LocationTypeLookupId, ct);
        var typeCode = currentType?.InternalCode.ToUpperInvariant() ?? string.Empty;
        var typeId = loc.LocationTypeLookupId;
        if (req.LocationType is not null)
        {
            typeCode = RequireText(req.LocationType, "locationType", "El tipo de localización es obligatorio.", 40).ToUpperInvariant();
            typeId = await lookups.GetIdAsync(LookupDomains.LocationType, typeCode, ct);
        }

        var countryId = loc.CountryLookupId;
        if (req.Country is not null)
        {
            var countryCode = RequireText(req.Country, "country", "El país es obligatorio.", 10).ToUpperInvariant();
            if (!CountryCode.IsValid(countryCode)) throw new ValidationException("country", CountryCode.InvalidMessage);
            countryId = await lookups.GetIdAsync(LookupDomains.Country, countryCode, ct);
        }

        // Ventana horaria: se valida sobre el valor efectivo (lo que quede tras el PATCH).
        var (winStart, winEnd) = req.ClearWindow == true
            ? (null, null)
            : (req.DefaultWindowStart ?? loc.DefaultWindowStart, req.DefaultWindowEnd ?? loc.DefaultWindowEnd);
        ValidateWindow(winStart, winEnd);

        var minutes = req.DefaultServiceMinutes ?? loc.DefaultServiceMinutes;
        ValidateServiceMinutes(minutes);

        EnsureAddressTypeAllowsOwner(typeCode, clientId);
        if (loc.IsActive) await EnsureSingleAddressAsync(typeCode, clientId, excludeLocationId: loc.LocationId, ct);

        // Aplicar.
        loc.ClientId = clientId;
        loc.LocationTypeLookupId = typeId;
        loc.CountryLookupId = countryId;
        if (req.Name is not null) loc.Name = RequireText(req.Name, "name", "El nombre es obligatorio.", NameMax);
        if (req.Line1 is not null) loc.Line1 = RequireText(req.Line1, "line1", "La dirección (línea 1) es obligatoria.", LineMax);
        if (req.City is not null) loc.City = RequireText(req.City, "city", "La ciudad es obligatoria.", CityMax);
        if (req.Code is not null) loc.Code = OptionalText(req.Code, "code", CodeMax);
        if (req.Line2 is not null) loc.Line2 = OptionalText(req.Line2, "line2", LineMax);
        if (req.State is not null) loc.State = OptionalText(req.State, "state", StateMax);
        if (req.PostalCode is not null) loc.PostalCode = OptionalText(req.PostalCode, "postalCode", PostalMax);
        if (req.AccessNotes is not null) loc.AccessNotes = OptionalText(req.AccessNotes, "accessNotes", NotesMax);
        if (req.DeliveryNotes is not null) loc.DeliveryNotes = OptionalText(req.DeliveryNotes, "deliveryNotes", NotesMax);
        loc.DefaultServiceMinutes = minutes;
        loc.DefaultWindowStart = winStart;
        loc.DefaultWindowEnd = winEnd;
        if (req.AllowDupInvoice.HasValue) loc.AllowDupInvoice = req.AllowDupInvoice.Value;

        // Si dejó de ser un almacén válido de su cliente (cambió de dueño o de tipo), el recogido vuelve a la corporativa.
        await ReconcileDefaultPickupAsync(loc, typeCode, ct);

        await db.SaveGuardedAsync("La localización fue modificada por otro usuario; recargue e intente de nuevo.", ct);
        return await GetByIdAsync(loc.LocationId, ct);
    }

    /// <summary>Baja/alta lógica. NUNCA DELETE. Al desactivar el almacén por defecto del cliente, el recogido vuelve a la corporativa.</summary>
    public async Task SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.PublicId == publicId, ct)
                  ?? throw new NotFoundException("Localización", publicId);
        if (loc.IsActive == active) return; // idempotente

        var type = await lookups.GetAsync(loc.LocationTypeLookupId, ct);
        var typeCode = type?.InternalCode.ToUpperInvariant() ?? string.Empty;
        if (active) await EnsureSingleAddressAsync(typeCode, loc.ClientId, excludeLocationId: loc.LocationId, ct);

        loc.IsActive = active;
        await ReconcileDefaultPickupAsync(loc, typeCode, ct);
        await db.SaveGuardedAsync("La localización fue modificada por otro usuario; recargue e intente de nuevo.", ct);
    }

    // ---------------------------------------------------------------- reglas

    private static bool IsClientAddress(string typeCode) => typeCode is LocationTypes.Corporate or LocationTypes.Billing;
    private static bool IsWarehouse(string typeCode) => typeCode is LocationTypes.Pickup or LocationTypes.Both;

    /// <summary>Una dirección corporativa o postal siempre pertenece a un cliente: no puede ser compartida.</summary>
    private static void EnsureAddressTypeAllowsOwner(string typeCode, int? clientId)
    {
        if (IsClientAddress(typeCode) && clientId is null)
            throw new ValidationException("clientPublicId", "Una dirección corporativa o postal (facturación) debe pertenecer a un cliente; no puede ser compartida.");
    }

    /// <summary>A lo sumo una Location activa tipo CORPORATE y una tipo BILLING por cliente (su dirección física y postal).</summary>
    private async Task EnsureSingleAddressAsync(string typeCode, int? clientId, int? excludeLocationId, CancellationToken ct)
    {
        if (!IsClientAddress(typeCode) || clientId is null) return;
        var typeId = await lookups.GetIdAsync(LookupDomains.LocationType, typeCode, ct);
        var exists = await db.Locations.AnyAsync(l => l.ClientId == clientId && l.LocationTypeLookupId == typeId && l.IsActive
                                                      && (excludeLocationId == null || l.LocationId != excludeLocationId), ct);
        if (exists)
            throw new ConflictException(typeCode == LocationTypes.Corporate
                ? "El cliente ya tiene una dirección corporativa activa; desactívela o edítela."
                : "El cliente ya tiene una dirección postal (facturación) activa; desactívela o edítela.");
    }

    /// <summary>Si la localización es el punto de recogido por defecto de un cliente y ya no sirve como tal, se limpia el puntero.</summary>
    private async Task ReconcileDefaultPickupAsync(Location loc, string typeCode, CancellationToken ct)
    {
        var owners = await db.Clients.Where(c => c.DefaultPickupLocationId == loc.LocationId).ToListAsync(ct);
        if (owners.Count == 0) return;
        foreach (var owner in owners)
        {
            var stillValid = loc.IsActive && loc.ClientId == owner.ClientId && IsWarehouse(typeCode);
            if (!stillValid) owner.DefaultPickupLocationId = null; // vuelve a "misma que la corporativa"
        }
    }

    private static void ValidateWindow(TimeOnly? start, TimeOnly? end)
    {
        if (start is null && end is null) return;
        if (start is null || end is null)
            throw new ValidationException("defaultWindowStart", "La ventana horaria requiere hora de inicio y hora de fin.");
        if (start.Value >= end.Value)
            throw new ValidationException("defaultWindowEnd", "La hora de fin de la ventana debe ser posterior a la de inicio.");
    }

    private static void ValidateServiceMinutes(int minutes)
    {
        if (minutes < 0) throw new ValidationException("defaultServiceMinutes", "Los minutos de servicio no pueden ser negativos.");
    }

    private static string RequireText(string? value, string field, string message, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException(field, message);
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"Máximo {max} caracteres.");
        return v;
    }

    /// <summary>Texto opcional: vacío o solo espacios ⇒ NULL (así el PATCH puede borrar un valor).</summary>
    private static string? OptionalText(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"Máximo {max} caracteres.");
        return v;
    }

    // ---------------------------------------------------------------- mapeo

    private IQueryable<Location> Base()
        => db.Locations.AsNoTracking().Include(l => l.Client).Include(l => l.LocationType).Include(l => l.Country);

    private async Task<LocationDto> GetByIdAsync(int id, CancellationToken ct)
        => ToDto(await Base().FirstAsync(l => l.LocationId == id, ct));

    private LocationDto ToDto(Location l) => new(
        l.LocationId, l.PublicId, l.Client?.PublicId, l.Client?.Name, l.ClientId is null,
        l.Code, l.Name,
        l.LocationType?.InternalCode ?? "", MultilingualText.Resolve(l.LocationType?.LabelJson, tenant.Lang),
        l.Line1, l.Line2, l.City, l.State, l.PostalCode,
        l.Country?.InternalCode ?? "", MultilingualText.Resolve(l.Country?.LabelJson, tenant.Lang),
        l.DefaultServiceMinutes, l.DefaultWindowStart, l.DefaultWindowEnd, l.AccessNotes, l.DeliveryNotes,
        l.AllowDupInvoice, l.IsActive, l.CreatedAtUtc,
        Convert.ToBase64String(l.RowVersion ?? Array.Empty<byte>()));
}
