using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
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
/// Lote 2 — Clientes: alta compuesta (cliente + contrato inicial DRAFT por servicio), ficha (perfil, direcciones,
/// numeración, contactos, contratos), estatus vía StatusService y baja lógica.
/// El TenantId sale siempre del principal; las hijas sin TenantId (ClientContact) se alcanzan solo a través del cliente.
/// </summary>
public sealed class ClientService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses, ContactPointService contacts)
{
    private const string DefaultContractTitle = "Contrato marco";

    // ---------------- Lista ----------------

    public async Task<IReadOnlyList<ClientListItemDto>> GetListAsync(string? search, bool includeInactive, CancellationToken ct)
    {
        var q = db.Clients.AsNoTracking();
        if (!includeInactive) q = q.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Code.Contains(s) || c.Name.Contains(s) || (c.LegalName != null && c.LegalName.Contains(s)));
        }
        var list = await q.OrderBy(c => c.Name).ThenBy(c => c.Code).ToListAsync(ct);
        if (list.Count == 0) return Array.Empty<ClientListItemDto>();

        var ids = list.Select(c => c.ClientId).ToList();
        var contracts = await db.Contracts.AsNoTracking().Where(c => ids.Contains(c.ClientId) && c.IsActive).ToListAsync(ct);
        var clientStatus = await StatusMapAsync(StatusDomains.ClientStatus, ct);
        var contractStatus = await StatusMapAsync(StatusDomains.ContractStatus, ct);
        var byClient = contracts.GroupBy(c => c.ClientId).ToDictionary(g => g.Key, g => g.ToList());
        var today = Today();

        return list.Select(c =>
        {
            var own = byClient.GetValueOrDefault(c.ClientId) ?? new List<Contract>();
            var current = PickCurrent(own, contractStatus, today);
            var status = clientStatus.GetValueOrDefault(c.StatusCodeId);
            return new ClientListItemDto(c.ClientId, c.PublicId, c.Code, c.Name, status?.InternalCode ?? "", Label(status),
                c.IsActive, current is null ? "" : Summary(current), own.Count);
        }).ToList();
    }

    // ---------------- Alta compuesta ----------------

    public async Task<ClientDetailDto> CreateAsync(ClientCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var name = RequireText(req.Name, "name", "El nombre es obligatorio.", 200);
        var legalName = OptionalText(req.LegalName, "legalName", 250);
        var taxId = OptionalText(req.TaxId, "taxId", 50);
        if (req.CreditLimit is < 0) throw new ValidationException("creditLimit", "El límite de crédito no puede ser negativo.");
        var paymentTermId = await OptionalLookupAsync(LookupDomains.PaymentTerm, req.PaymentTerm, ct);
        var currencyId = await OptionalLookupAsync(LookupDomains.Currency, req.Currency, ct);
        string? explicitCode = null;
        if (!string.IsNullOrWhiteSpace(req.Code))
        {
            explicitCode = req.Code.Trim().ToUpperInvariant();
            if (explicitCode.Length > 30) throw new ValidationException("code", "El código no puede exceder 30 caracteres.");
        }

        // Contrato inicial: validaciones puras antes de abrir la transacción
        var contractReq = req.Contract;
        List<(int ServiceTypeId, ServiceLevelUpsert Level)>? levels = null;
        if (contractReq is not null)
        {
            ContractRules.ValidateDates(contractReq.StartDate, contractReq.EndDate, "contract.endDate");
            if (contractReq.Title is { Length: > 200 }) throw new ValidationException("contract.title", "El título no puede exceder 200 caracteres.");
            levels = await ValidateServiceLevelsAsync(contractReq.ServiceLevels, ct);
        }

        var initialClientStatus = await statuses.GetInitialAsync(StatusDomains.ClientStatus, ct);
        var initialContractStatus = contractReq is null ? null : await statuses.GetInitialAsync(StatusDomains.ContractStatus, ct);

        var publicId = await db.RunInTransactionAsync(async ct2 =>
        {
            var code = await ResolveCodeAsync(explicitCode, name, ct2);
            var client = new Client
            {
                TenantId = tenantId, Code = code, Name = name, LegalName = legalName, TaxId = taxId, CreditLimit = req.CreditLimit,
                PaymentTermLookupId = paymentTermId, CurrencyLookupId = currencyId, StatusCodeId = initialClientStatus.StatusCodeId, IsActive = true,
            };
            db.Clients.Add(client);
            await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct2);
            // Historial de estatus desde el inicio (etapa inicial habilitada del tenant: ACTIVE en el seed)
            await statuses.TransitionAsync(StatusDomains.ClientStatus, EntityTypes.Client, client.ClientId, null, initialClientStatus.InternalCode, null, ct2);

            if (contractReq is not null)
            {
                var contract = new Contract
                {
                    TenantId = tenantId, ClientId = client.ClientId,
                    ContractNumber = ContractRules.NextContractNumber(code, Array.Empty<string>()),
                    Title = string.IsNullOrWhiteSpace(contractReq.Title) ? DefaultContractTitle : contractReq.Title.Trim(),
                    StartDate = contractReq.StartDate, EndDate = contractReq.EndDate, AutoRenew = contractReq.AutoRenew ?? false,
                    BillPerService = true, BillExtraPiece = false, BillDispatchFee = false, BillCodFee = false, BillSpecialServices = false,
                    CurrencyLookupId = currencyId, StatusCodeId = initialContractStatus!.StatusCodeId, IsActive = true,
                };
                db.Contracts.Add(contract);
                await db.SaveGuardedAsync("Ya existe un contrato con ese número.", ct2);
                await statuses.TransitionAsync(StatusDomains.ContractStatus, EntityTypes.Contract, contract.ContractId, null, initialContractStatus.InternalCode, null, ct2);
                foreach (var (serviceTypeId, lv) in levels!)
                    db.ContractServiceLevels.Add(new ContractServiceLevel
                    {
                        ContractId = contract.ContractId, ServiceTypeLookupId = serviceTypeId, MaxTransitHours = lv.MaxTransitHours,
                        PickupWindowMin = lv.PickupWindowMin, OnTimeTargetPct = lv.OnTimeTargetPct, PenaltyAmount = lv.PenaltyAmount, IsActive = true,
                    });
            }
            await db.SaveGuardedAsync("Ya existe un nivel de servicio para ese tipo de servicio en el contrato.", ct2);
            return client.PublicId;
        }, ct);

        return await GetAsync(publicId, ct);
    }

    /// <summary>Código explícito (409 si existe) o generado desde el nombre con sufijo -2, -3… si choca.</summary>
    private async Task<string> ResolveCodeAsync(string? explicitCode, string name, CancellationToken ct)
    {
        if (explicitCode is not null)
        {
            if (await db.Clients.AnyAsync(c => c.Code == explicitCode, ct)) throw new ConflictException("Ya existe un cliente con ese código.");
            return explicitCode;
        }
        string baseCode;
        try { baseCode = ClientCode.FromName(name); }
        catch (ArgumentException ex) { throw new ValidationException("name", ex.Message); }
        if (!await db.Clients.AnyAsync(c => c.Code == baseCode, ct)) return baseCode;
        // Cada candidato se verifica contra la BD: WithSuffix recorta la base cuando no cabe el sufijo ('FARMACIA-LAS-MARIAS'
        // + '-2' → 'FARMACIA-LAS-MARIA-2'), así que un prefijo '{base}-' no vería los códigos ya tomados. Una consulta extra
        // solo cuando hay colisión; UQ_Client_Tenant_Code sigue siendo la última línea de defensa (409).
        for (var n = 2; ; n++)
        {
            var candidate = ClientCode.WithSuffix(baseCode, n);
            if (!await db.Clients.AnyAsync(c => c.Code == candidate, ct)) return candidate;
        }
    }

    // ---------------- Ficha ----------------

    public async Task<ClientDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new NotFoundException("Cliente");
        var clientStatus = await StatusMapAsync(StatusDomains.ClientStatus, ct);
        var contractStatus = await StatusMapAsync(StatusDomains.ContractStatus, ct);
        var today = Today();

        // Direcciones: CORPORATE = física, BILLING = postal, PICKUP/BOTH = almacenes (puntos de recogido)
        var corporateId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Corporate, ct);
        var billingId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Billing, ct);
        var pickupId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Pickup, ct);
        var bothId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Both, ct);
        var locations = await db.Locations.AsNoTracking().Where(l => l.ClientId == client.ClientId && l.IsActive).OrderBy(l => l.Name).ToListAsync(ct);
        var corporate = locations.FirstOrDefault(l => l.LocationTypeLookupId == corporateId);
        var postal = locations.FirstOrDefault(l => l.LocationTypeLookupId == billingId);
        var warehouses = locations.Where(l => l.LocationTypeLookupId == pickupId || l.LocationTypeLookupId == bothId).ToList();
        var defaultPickup = client.DefaultPickupLocationId.HasValue ? warehouses.FirstOrDefault(l => l.LocationId == client.DefaultPickupLocationId.Value) : null;
        ClientPickupAddressDto? pickup = null;
        if (defaultPickup is not null) pickup = await ToPickupAsync(defaultPickup, false, ct);
        else if (corporate is not null) pickup = await ToPickupAsync(corporate, true, ct);

        // Contratos
        var contractRows = await db.Contracts.AsNoTracking().Where(c => c.ClientId == client.ClientId)
            .OrderByDescending(c => c.StartDate).ThenByDescending(c => c.ContractId).ToListAsync(ct);
        var current = await db.CurrentContractAsync(client.ClientId, today, ct);
        var contractDtos = contractRows.Select(c => ToContractSummary(c, contractStatus, c.ContractId == current?.ContractId)).ToList();
        var currentDto = contractDtos.FirstOrDefault(c => c.IsCurrent);

        // Contactos (personas) con sus ContactPoints y ContactPoints del propio cliente
        var contactRows = await db.ClientContacts.AsNoTracking().Where(c => c.ClientId == client.ClientId && c.IsActive)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName).ToListAsync(ct);
        var contactDtos = new List<ClientContactDto>(contactRows.Count);
        foreach (var c in contactRows) contactDtos.Add(await ToContactDtoAsync(c, ct));
        var clientPoints = await contacts.GetForOwnerAsync(EntityTypes.Client, client.ClientId, false, ct);

        var paymentTerm = client.PaymentTermLookupId.HasValue ? await lookups.GetAsync(client.PaymentTermLookupId.Value, ct) : null;
        var currency = client.CurrencyLookupId.HasValue ? await lookups.GetAsync(client.CurrencyLookupId.Value, ct) : null;
        var status = clientStatus.GetValueOrDefault(client.StatusCodeId);
        var pickupLocations = new List<ClientAddressDto>(warehouses.Count);
        foreach (var w in warehouses) pickupLocations.Add(await ToAddressAsync(w, ct));

        return new ClientDetailDto(
            client.ClientId, client.PublicId, client.Code, client.Name, client.LegalName, client.TaxId, client.CreditLimit,
            paymentTerm?.InternalCode, Label(paymentTerm), currency?.InternalCode, Label(currency),
            status?.InternalCode ?? "", Label(status), client.IsActive,
            corporate is null ? null : await ToAddressAsync(corporate, ct),
            postal is null ? null : await ToAddressAsync(postal, ct),
            pickup, pickupLocations,
            ToNumberSettings(client),
            contactDtos, clientPoints,
            contractDtos, currentDto, currentDto?.BillingSummary ?? "",
            client.CreatedAtUtc, client.UpdatedAtUtc, RowVersionOf(client.RowVersion));
    }

    // ---------------- Perfil ----------------

    public async Task<ClientDetailDto> UpdateProfileAsync(Guid publicId, ClientProfileUpdateRequest req, CancellationToken ct)
    {
        var client = await LoadForUpdateAsync(publicId, ct);
        db.ApplyRowVersion(client, req.RowVersion);

        if (req.LegalName is not null) client.LegalName = OptionalText(req.LegalName, "legalName", 250);
        if (req.TaxId is not null) client.TaxId = OptionalText(req.TaxId, "taxId", 50);
        if (req.CreditLimit.HasValue)
        {
            if (req.CreditLimit.Value < 0) throw new ValidationException("creditLimit", "El límite de crédito no puede ser negativo.");
            client.CreditLimit = req.CreditLimit.Value;
        }
        if (req.PaymentTerm is not null) client.PaymentTermLookupId = await OptionalLookupAsync(LookupDomains.PaymentTerm, req.PaymentTerm, ct);
        if (req.Currency is not null) client.CurrencyLookupId = await OptionalLookupAsync(LookupDomains.Currency, req.Currency, ct);

        if (req.ClearDefaultPickup) client.DefaultPickupLocationId = null; // vuelve a 'se recoge en la corporativa'
        else if (req.DefaultPickupLocationPublicId.HasValue)
        {
            var pickupId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Pickup, ct);
            var bothId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Both, ct);
            var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.PublicId == req.DefaultPickupLocationPublicId.Value, ct);
            if (loc is null || loc.ClientId != client.ClientId || !loc.IsActive || (loc.LocationTypeLookupId != pickupId && loc.LocationTypeLookupId != bothId))
                throw new ValidationException("defaultPickupLocationPublicId", "El punto de recogido debe ser un almacén activo del cliente (tipo PICKUP o BOTH).");
            client.DefaultPickupLocationId = loc.LocationId;
        }

        await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct);
        return await GetAsync(publicId, ct);
    }

    // ---------------- Numeración ----------------

    public async Task<ClientDetailDto> UpdateNumberSettingsAsync(Guid publicId, ClientNumberSettingsRequest req, CancellationToken ct)
    {
        var client = await LoadForUpdateAsync(publicId, ct);
        var errors = new Dictionary<string, string[]>();
        var order = NormalizePattern(req.OrderNumberFormat, "orderNumberFormat", errors);
        var invoice = NormalizePattern(req.InvoiceNumberFormat, "invoiceNumberFormat", errors);
        var package = NormalizePattern(req.PackageNumberFormat, "packageNumberFormat", errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        if (req.ClientAssignsOrderNumber.HasValue) client.ClientAssignsOrderNumber = req.ClientAssignsOrderNumber.Value;
        if (req.ClientAssignsInvoiceNumber.HasValue) client.ClientAssignsInvoiceNumber = req.ClientAssignsInvoiceNumber.Value;
        if (req.OrderNumberFormat is not null) client.OrderNumberFormat = order;
        if (req.InvoiceNumberFormat is not null) client.InvoiceNumberFormat = invoice;
        if (req.PackageNumberFormat is not null) client.PackageNumberFormat = package;
        await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Vista previa en vivo del patrón (puro, sin BD). 400 si el patrón es inválido.</summary>
    public static NumberPreviewDto PreviewNumber(string? pattern, long seq)
    {
        var error = NumberFormat.Validate(pattern);
        if (error is not null) throw new ValidationException("pattern", error);
        if (seq < 0) throw new ValidationException("seq", "El consecutivo no puede ser negativo.");
        return new NumberPreviewDto(pattern!, seq, NumberFormat.Resolve(pattern!, seq));
    }

    /// <summary>null = sin cambio; "" = NULL (patrón por defecto); otro = validado.</summary>
    private static string? NormalizePattern(string? value, string field, IDictionary<string, string[]> errors)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        var error = NumberFormat.Validate(v);
        if (error is not null) errors[field] = new[] { error };
        return v;
    }

    // ---------------- Contactos (personas) ----------------

    public async Task<IReadOnlyList<ClientContactDto>> GetContactsAsync(Guid publicId, bool includeInactive, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new NotFoundException("Cliente");
        var q = db.ClientContacts.AsNoTracking().Where(c => c.ClientId == client.ClientId);
        if (!includeInactive) q = q.Where(c => c.IsActive);
        var rows = await q.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName).ToListAsync(ct);
        var list = new List<ClientContactDto>(rows.Count);
        foreach (var r in rows) list.Add(await ToContactDtoAsync(r, ct));
        return list;
    }

    public async Task<ClientContactDto> AddContactAsync(Guid publicId, ClientContactCreateRequest req, CancellationToken ct)
    {
        var fullName = RequireText(req.FullName, "fullName", "El nombre del contacto es obligatorio.", 150);
        var role = OptionalText(req.Role, "role", 80);
        var points = req.ContactPoints ?? new List<ContactPointUpsertRequest>();
        foreach (var p in points)
        {
            if (string.IsNullOrWhiteSpace(p.ContactType)) throw new ValidationException("contactPoints", "Cada medio de contacto necesita contactType.");
            ContactPointService.ValidateValue(p.ContactType, p.Value);
        }

        var contactId = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct2) ?? throw new NotFoundException("Cliente");
            if (req.IsPrimary) await ClearPrimaryContactAsync(client.ClientId, null, ct2); // respeta UX_ClientContact_Primary
            var contact = new ClientContact { ClientId = client.ClientId, FullName = fullName, Role = role, IsPrimary = req.IsPrimary, IsActive = true };
            db.ClientContacts.Add(contact);
            await db.SaveGuardedAsync("Ya existe un contacto principal activo para este cliente.", ct2);
            foreach (var p in points) await contacts.AddAsync(EntityTypes.ClientContact, contact.ClientContactId, p, ct2);
            return contact.ClientContactId;
        }, ct);

        var saved = await db.ClientContacts.AsNoTracking().FirstAsync(c => c.ClientContactId == contactId, ct);
        return await ToContactDtoAsync(saved, ct);
    }

    public async Task<ClientContactDto> UpdateContactAsync(Guid publicId, int id, ClientContactUpdateRequest req, CancellationToken ct)
    {
        var fullName = req.FullName is null ? null : RequireText(req.FullName, "fullName", "El nombre del contacto es obligatorio.", 150);
        var role = req.Role is null ? null : OptionalText(req.Role, "role", 80);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == publicId, ct2) ?? throw new NotFoundException("Cliente");
            // Siempre a través del cliente del tenant, nunca por id suelto (ClientContact no lleva TenantId)
            var contact = await db.ClientContacts.FirstOrDefaultAsync(c => c.ClientContactId == id && c.ClientId == client.ClientId, ct2)
                          ?? throw new NotFoundException("Contacto", id);
            if (fullName is not null) contact.FullName = fullName;
            if (req.Role is not null) contact.Role = role;
            if (req.IsActive.HasValue) contact.IsActive = req.IsActive.Value;
            if (req.IsPrimary.HasValue)
            {
                if (req.IsPrimary.Value && !contact.IsPrimary) await ClearPrimaryContactAsync(client.ClientId, contact.ClientContactId, ct2);
                contact.IsPrimary = req.IsPrimary.Value;
            }
            if (!contact.IsActive) contact.IsPrimary = false; // un inactivo nunca es el principal
            await db.SaveGuardedAsync("Ya existe un contacto principal activo para este cliente.", ct2);
        }, ct);

        var saved = await db.ClientContacts.AsNoTracking().FirstAsync(c => c.ClientContactId == id, ct);
        return await ToContactDtoAsync(saved, ct);
    }

    /// <summary>Quita IsPrimary del principal actual y guarda antes de insertar/marcar el nuevo (índice único filtrado).</summary>
    private async Task ClearPrimaryContactAsync(int clientId, int? keepId, CancellationToken ct)
    {
        var q = db.ClientContacts.Where(c => c.ClientId == clientId && c.IsPrimary);
        if (keepId.HasValue) { var keep = keepId.Value; q = q.Where(c => c.ClientContactId != keep); }
        var current = await q.ToListAsync(ct);
        if (current.Count == 0) return;
        foreach (var c in current) c.IsPrimary = false;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Estatus y baja ----------------

    public async Task<ClientDetailDto> TransitionStatusAsync(Guid publicId, StatusChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ToCode)) throw new ValidationException("toCode", "El estatus destino es obligatorio.");
        var client = await LoadForUpdateAsync(publicId, ct);
        var to = await statuses.TransitionAsync(StatusDomains.ClientStatus, EntityTypes.Client, client.ClientId, client.StatusCodeId, req.ToCode.Trim(), req.Comment, ct);
        client.StatusCodeId = to.StatusCodeId;
        await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Baja lógica (IsActive = 0) o reactivación. Nunca DELETE: el cliente tiene historial.</summary>
    public async Task SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var client = await LoadForUpdateAsync(publicId, ct);
        if (client.IsActive == active) return;
        client.IsActive = active;
        await db.SaveGuardedAsync("Ya existe un cliente con ese código.", ct);
    }

    // ---------------- Helpers ----------------

    private async Task<Client> LoadForUpdateAsync(Guid publicId, CancellationToken ct)
        => await db.Clients.FirstOrDefaultAsync(c => c.PublicId == publicId, ct) ?? throw new NotFoundException("Cliente");

    /// <summary>
    /// Mismas reglas y mensajes que PUT /contracts/{id}/service-levels (ContractRules.ValidateServiceLevels, probado con xunit),
    /// con el campo anidado bajo 'contract.'. Un tipo de servicio desconocido es 400 (no 404), como en ContractService.
    /// </summary>
    private async Task<List<(int ServiceTypeId, ServiceLevelUpsert Level)>> ValidateServiceLevelsAsync(IList<ServiceLevelUpsert>? items, CancellationToken ct)
    {
        var result = new List<(int, ServiceLevelUpsert)>();
        if (items is null) return result;
        ContractRules.ValidateServiceLevels(items, "contract.serviceLevels");
        var i = 0;
        foreach (var lv in items)
        {
            var code = lv.ServiceType.Trim().ToUpperInvariant();
            var id = await lookups.TryGetIdAsync(LookupDomains.ServiceType, code, ct)
                     ?? throw new ValidationException($"contract.serviceLevels[{i}].serviceType", $"Tipo de servicio desconocido: '{lv.ServiceType}'.");
            result.Add((id, lv));
            i++;
        }
        return result;
    }

    private async Task<int?> OptionalLookupAsync(string domain, string? code, CancellationToken ct)
        => string.IsNullOrWhiteSpace(code) ? null : await lookups.GetIdAsync(domain, code.Trim(), ct);

    private static string RequireText(string? value, string field, string message, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException(field, message);
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"No puede exceder {max} caracteres.");
        return v;
    }

    private static string? OptionalText(string? value, string field, int max)
    {
        if (value is null) return null;
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (v.Length > max) throw new ValidationException(field, $"No puede exceder {max} caracteres.");
        return v;
    }

    private Task<Dictionary<int, StatusCode>> StatusMapAsync(string domain, CancellationToken ct)
        => db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToDictionaryAsync(s => s.StatusCodeId, ct);

    /// <summary>Misma regla que ClientQueries.CurrentContractAsync, evaluada en memoria para la lista (evita N+1): CurrentContractRule.</summary>
    private static Contract? PickCurrent(IEnumerable<Contract> own, IReadOnlyDictionary<int, StatusCode> statusMap, DateOnly asOf)
        => CurrentContractRule.Pick(own, c => statusMap.GetValueOrDefault(c.StatusCodeId)?.InternalCode, asOf);

    private string Summary(Contract c)
        => BillingModelSummary.Build(c.BillPerService, c.BillExtraPiece, c.BillDispatchFee, c.BillCodFee, c.BillSpecialServices, tenant.Lang);

    private ClientContractSummaryDto ToContractSummary(Contract c, IReadOnlyDictionary<int, StatusCode> statusMap, bool isCurrent)
    {
        var status = statusMap.GetValueOrDefault(c.StatusCodeId);
        var summary = Summary(c);
        return new ClientContractSummaryDto(c.ContractId, c.PublicId, c.ContractNumber, c.Title, c.StartDate, c.EndDate,
            status?.InternalCode ?? "", Label(status), c.AutoRenew,
            new ClientContractBillingDto(c.BillPerService, c.BillExtraPiece, c.BillDispatchFee, c.BillCodFee, c.BillSpecialServices, summary),
            summary, c.IsActive, isCurrent);
    }

    private async Task<ClientContactDto> ToContactDtoAsync(ClientContact c, CancellationToken ct)
        => new(c.ClientContactId, c.FullName, c.Role, c.IsPrimary, c.IsActive, await contacts.GetForOwnerAsync(EntityTypes.ClientContact, c.ClientContactId, false, ct));

    private async Task<ClientAddressDto> ToAddressAsync(Location l, CancellationToken ct)
    {
        var type = await lookups.GetAsync(l.LocationTypeLookupId, ct);
        var country = await lookups.GetAsync(l.CountryLookupId, ct);
        return new ClientAddressDto(l.LocationId, l.PublicId, l.Code, l.Name, type?.InternalCode ?? "", Label(type),
            l.Line1, l.Line2, l.City, l.State, l.PostalCode, country?.InternalCode ?? "", Label(country), l.IsActive);
    }

    private async Task<ClientPickupAddressDto> ToPickupAsync(Location l, bool fromCorporate, CancellationToken ct)
    {
        var a = await ToAddressAsync(l, ct);
        return new ClientPickupAddressDto(a.Id, a.PublicId, a.Code, a.Name, a.LocationType, a.LocationTypeLabel, a.Line1, a.Line2, a.City, a.State,
            a.PostalCode, a.Country, a.CountryLabel, a.IsActive, fromCorporate);
    }

    private static ClientNumberSettingsDto ToNumberSettings(Client c)
    {
        var order = c.OrderNumberFormat ?? NumberFormat.Defaults.Order;
        var invoice = c.InvoiceNumberFormat ?? NumberFormat.Defaults.Invoice;
        var package = c.PackageNumberFormat ?? NumberFormat.Defaults.Package;
        return new ClientNumberSettingsDto(c.ClientAssignsOrderNumber, c.ClientAssignsInvoiceNumber,
            c.OrderNumberFormat, c.InvoiceNumberFormat, c.PackageNumberFormat, order, invoice, package,
            NumberFormat.Resolve(order, 1), NumberFormat.Resolve(invoice, 1), NumberFormat.Resolve(package, 1));
    }

    private string Label(LookupCode? l) => l is null ? "" : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
    private string Label(StatusCode? s) => s is null ? "" : MultilingualText.Resolve(s.LabelJson, tenant.Lang);
    private static string? RowVersionOf(byte[]? rv) => rv is null ? null : Convert.ToBase64String(rv);
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
