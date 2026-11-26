using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Domain.Files;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Tax;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 55 (P3) — impuestos (IVU). Todo es configuración de la compañía; el código solo trae el mecanismo.
/// - Tasas: NO se editan. Se crean con sus componentes (la suma es el porcentaje) y se cierra su vigencia; para un mismo código las vigencias no se traslapan.
/// - Reglas: precios con impuesto incluido o no, redondeo por línea o por documento, tasa por omisión y qué tipos de cargo pagan impuesto.
/// - Exenciones de clientes (certificado con vigencia). Categoría de impuesto del producto (gravable, exento, tasa cero).
/// - Cálculo (<see cref="CalculateAsync"/>): lo usan, o lo usarán, las órdenes de venta y la facturación; también sirve de simulador.
/// - Lote 248: la exención del cliente queda atada al documento que la respalda (su vigencia y su número son los del documento) y hay exención de producto por
///   temporada (TaxExemptionPeriod, motivo EXEMPT_PRODUCT_PERIOD), que gana a la exención del cliente en su línea. El constructor no cambia.
/// </summary>
public sealed class TaxService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups)
{
    private TenantContext T => (TenantContext)tenant;
    private DateOnly Today => clock.Today;

    // ---------------------------------------------------------------- tasas

    /// <summary>Todas las tasas (el historial completo), la vigente primero; activeOnly deja solo las vigentes hoy.</summary>
    public async Task<IReadOnlyList<TaxRateDto>> ListRatesAsync(bool activeOnly, CancellationToken ct)
    {
        var rates = await db.Set<TaxRate>().Include(r => r.Components).OrderBy(r => r.Code).ThenByDescending(r => r.EffectiveFrom).ToListAsync(ct);
        return rates.Select(ToDto).Where(r => !activeOnly || r.Status == "ACTIVE").ToList();
    }

    public async Task<TaxRateDto> CreateRateAsync(TaxRateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        var codeError = TaxRules.ValidateCode(req.Code, out var code);
        if (codeError is not null) throw new ValidationException("code", codeError);
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ValidationException("name", TaxRules.NameRequired);
        if (name.Length > 100) throw new ValidationException("name", TaxRules.NameTooLong);
        if (req.EffectiveFrom is not DateOnly from) throw new ValidationException("effectiveFrom", "Escriba desde qué fecha rige la tasa.");
        if (req.EffectiveTo is DateOnly to0 && to0 < from) throw new ValidationException("effectiveTo", TaxRules.DatesOrder);

        var comps = req.Components ?? Array.Empty<TaxComponentDto>();
        if (comps.Count == 0) throw new ValidationException("components", TaxRules.ComponentsRequired);
        var seen = new HashSet<string>();
        var built = new List<TaxRateComponent>();
        var order = 0;
        foreach (var c in comps)
        {
            var cErr = TaxRules.ValidateCode(c.Code, out var cCode);
            if (cErr is not null) throw new ValidationException("components", cErr);
            if (!seen.Add(cCode)) throw new ValidationException("components", TaxRules.ComponentDuplicate);
            var cName = (c.Name ?? string.Empty).Trim();
            if (cName.Length == 0) throw new ValidationException("components", TaxRules.NameRequired);
            if (cName.Length > 100) throw new ValidationException("components", TaxRules.NameTooLong);
            if (c.Percent < 0 || c.Percent > 100) throw new ValidationException("components", TaxRules.ComponentPercentRange);
            built.Add(new TaxRateComponent { TenantId = tenantId, Code = cCode, Name = cName, Percent = Math.Round(c.Percent, 4), SortOrder = order++ });
        }
        var total = built.Sum(c => c.Percent);
        if (total > 100) throw new ValidationException("components", TaxRules.RatePercentRange);

        var sameCode = await db.Set<TaxRate>().Where(r => r.Code == code).Select(r => new { r.EffectiveFrom, r.EffectiveTo }).ToListAsync(ct);
        if (sameCode.Any(r => TaxRules.Overlaps(from, req.EffectiveTo, r.EffectiveFrom, r.EffectiveTo)))
            throw new ConflictException(TaxRules.RateOverlap);

        var rate = new TaxRate { TenantId = tenantId, Code = code, Name = name, Percent = total, EffectiveFrom = from, EffectiveTo = req.EffectiveTo, Components = built };
        db.Set<TaxRate>().Add(rate);
        await db.SaveGuardedAsync(TaxRules.RateOverlap, ct);
        return ToDto(rate);
    }

    /// <summary>Cierra la vigencia de una tasa abierta (única edición permitida). La fecha va desde el inicio de la vigencia en adelante.</summary>
    public async Task<TaxRateDto> CloseRateAsync(int id, TaxRateCloseRequest req, CancellationToken ct)
    {
        var rate = await db.Set<TaxRate>().AsTracking().Include(r => r.Components).FirstOrDefaultAsync(r => r.TaxRateId == id, ct)
            ?? throw new NotFoundException("Tasa", feminine: true);
        if (rate.EffectiveTo is not null) throw new ConflictException(TaxRules.RateAlreadyClosed);
        var to = req?.EffectiveTo ?? throw new ValidationException("effectiveTo", "Escriba el último día en que rige la tasa.");
        if (to < rate.EffectiveFrom) throw new ValidationException("effectiveTo", TaxRules.CloseBeforeStart);
        rate.EffectiveTo = to;
        await db.SaveGuardedAsync(TaxRules.RateOverlap, ct);
        return ToDto(rate);
    }

    private TaxRateDto ToDto(TaxRate r)
    {
        var status = r.EffectiveFrom > Today ? "SCHEDULED" : TaxRules.InEffect(r.EffectiveFrom, r.EffectiveTo, Today) ? "ACTIVE" : "CLOSED";
        return new TaxRateDto(r.TaxRateId, r.PublicId, r.Code, r.Name, r.Percent, r.EffectiveFrom, r.EffectiveTo, status,
            r.Components.OrderBy(c => c.SortOrder).Select(c => new TaxComponentDto(c.Code, c.Name, c.Percent)).ToList());
    }

    // ---------------------------------------------------------------- reglas

    public async Task<TaxSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var setting = await GetOrCreateSettingAsync(ct);
        return await ToSettingsDtoAsync(setting, ct);
    }

    public async Task<TaxSettingsDto> UpdateSettingsAsync(TaxSettingsRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        await GetOrCreateSettingAsync(ct);
        var setting = await db.Set<TenantTaxSetting>().AsTracking().FirstAsync(ct);

        if (req.RoundingMode is not null)
        {
            var mode = req.RoundingMode.Trim().ToUpperInvariant();
            if (!TaxRoundingModes.All.Contains(mode)) throw new ValidationException("roundingMode", TaxRules.RoundingInvalid);
            setting.RoundingMode = mode;
        }
        if (req.PricesIncludeTax is bool inc) setting.PricesIncludeTax = inc;
        if (req.ClearDefaultRate == true) setting.DefaultRateCode = null;
        else if (req.DefaultRateCode is not null)
        {
            var err = TaxRules.ValidateCode(req.DefaultRateCode, out var rateCode);
            if (err is not null) throw new ValidationException("defaultRateCode", err);
            if (!await db.Set<TaxRate>().AnyAsync(r => r.Code == rateCode, ct)) throw new ValidationException("defaultRateCode", TaxRules.DefaultRateUnknown);
            setting.DefaultRateCode = rateCode;
        }

        if (req.ChargeRules is not null)
        {
            var types = await ChargeTypesAsync(ct);
            var existing = await db.Set<TaxChargeRule>().AsTracking().ToListAsync(ct);
            foreach (var r in req.ChargeRules)
            {
                var key = (r.ChargeType ?? string.Empty).Trim().ToUpperInvariant();
                if (!types.TryGetValue(key, out var lk) || key == "PRODUCT_SALE") throw new ValidationException("chargeRules", TaxRules.ChargeTypeInvalid);
                var row = existing.FirstOrDefault(x => x.ChargeTypeLookupId == lk.LookupCodeId);
                if (row is null)
                    db.Set<TaxChargeRule>().Add(new TaxChargeRule { TenantId = setting.TenantId, ChargeTypeLookupId = lk.LookupCodeId, IsTaxable = r.IsTaxable ?? false });
                else row.IsTaxable = r.IsTaxable ?? false;
            }
        }
        await db.SaveGuardedAsync("Las reglas cambiaron al mismo tiempo. Vuelva a intentarlo.", ct);
        return await ToSettingsDtoAsync(await db.Set<TenantTaxSetting>().FirstAsync(ct), ct);
    }

    private async Task<TenantTaxSetting> GetOrCreateSettingAsync(CancellationToken ct)
    {
        var setting = await db.Set<TenantTaxSetting>().FirstOrDefaultAsync(ct);
        if (setting is not null) return setting;
        var created = new TenantTaxSetting { TenantId = T.RequireTenantId(), PricesIncludeTax = false, RoundingMode = TaxRoundingModes.Line };
        db.Set<TenantTaxSetting>().Add(created);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
        {
            db.Entry(created).State = EntityState.Detached;   // otra petición la creó a la vez
            return await db.Set<TenantTaxSetting>().FirstAsync(ct);
        }
        return created;
    }

    private async Task<Dictionary<string, LookupCode>> ChargeTypesAsync(CancellationToken ct)
        => (await lookups.GetDomainAsync("ChargeType", ct)).ToDictionary(l => l.InternalCode, StringComparer.OrdinalIgnoreCase);

    private async Task<TaxSettingsDto> ToSettingsDtoAsync(TenantTaxSetting s, CancellationToken ct)
    {
        var types = await ChargeTypesAsync(ct);
        var rules = await db.Set<TaxChargeRule>().ToListAsync(ct);
        var list = types.Values.Where(l => l.InternalCode != "PRODUCT_SALE").OrderBy(l => l.SortOrder)
            .Select(l => new TaxChargeRuleDto(l.InternalCode, MultilingualText.Resolve(l.LabelJson, T.Lang) ?? l.InternalCode,
                rules.Any(r => r.ChargeTypeLookupId == l.LookupCodeId && r.IsTaxable))).ToList();
        return new TaxSettingsDto(s.PricesIncludeTax, s.RoundingMode, s.DefaultRateCode, list);
    }

    // ---------------------------------------------------------------- exenciones de clientes
    // Lote 248 — la exención nueva siempre nace de un documento del cliente «de impuestos» (ClientDocumentId): su vigencia y su número son los del documento
    // (ClientExemptionBacking.Effective). Las viejas sin documento siguen valiendo igual («falta el documento»), pero editarlas o reactivarlas exige vincular uno.

    public async Task<IReadOnlyList<TaxExemptionDto>> ListExemptionsAsync(int? clientId, CancellationToken ct)
    {
        var q = db.Set<ClientTaxExemption>().AsNoTracking();
        if (clientId is int c) q = q.Where(e => e.ClientId == c);
        var rows = await q.OrderByDescending(e => e.ValidFrom).ThenByDescending(e => e.ClientTaxExemptionId).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<TaxExemptionDto> CreateExemptionAsync(TaxExemptionRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        if (req.ClientId is not int clientId || !await db.Set<Client>().AnyAsync(c => c.ClientId == clientId, ct))
            throw new NotFoundException("Cliente");
        if (req.ClientDocumentPublicId is not Guid documentPublicId)
            throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentRequired);
        var (doc, type) = await ResolveBackingDocumentAsync(clientId, documentPublicId, exceptExemptionId: null, ct);
        var label = type is null ? null : MultilingualText.Resolve(type.LabelJson, T.Lang);
        var (exType, cert, notes) = NormalizeExemption(ClientExemptionRules.DefaultExemptionType(req.ExemptionType, label), doc.DocumentNumber, req.Notes);
        var (scope, codes) = await NormalizeScopeAsync(req.Scope, req.ComponentCodes, ct);

        // el número y las fechas son los del documento (lo que traiga el cuerpo se ignora); sin «desde» en el documento, la exención rige desde hoy
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, ExemptionType = exType, CertificateNumber = cert, ValidFrom = doc.ValidFrom ?? Today,
            ValidTo = doc.ValidTo, Notes = notes, IsActive = true, Scope = scope, ComponentCodes = codes, ClientDocumentId = doc.ClientDocumentId,
        };
        db.Set<ClientTaxExemption>().Add(e);
        await db.SaveGuardedAsync(ClientExemptionRules.DocumentAlreadyBacks, ct);   // UX_ClientTaxExemption_Document: dos a la vez con el mismo documento → 409
        return (await ToDtosAsync(new[] { e }, ct))[0];
    }

    public async Task<TaxExemptionDto> UpdateExemptionAsync(int id, TaxExemptionPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var e = await db.Set<ClientTaxExemption>().AsTracking().FirstOrDefaultAsync(x => x.ClientTaxExemptionId == id, ct)
            ?? throw new NotFoundException("Exención", feminine: true);
        // una exención vieja «falta el documento» solo se edita vinculándole su documento
        if (e.ClientDocumentId is null && req.ClientDocumentPublicId is null)
            throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentRequired);
        var datesField = req.ValidFrom is not null ? "validFrom" : req.ValidTo is not null || req.ClearValidTo == true ? "validTo" : req.CertificateNumber is not null ? "certificateNumber" : null;
        if (datesField is not null) throw new ValidationException(datesField, ClientExemptionRules.DatesFollowDocument);

        ClientDocument? doc = null;
        if (req.ClientDocumentPublicId is Guid documentPublicId)
        {
            var current = e.ClientDocumentId is int cur
                ? await db.Set<ClientDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.ClientDocumentId == cur, ct) : null;
            if (current?.PublicId != documentPublicId)
            {
                (doc, _) = await ResolveBackingDocumentAsync(e.ClientId, documentPublicId, e.IsActive ? e.ClientTaxExemptionId : null, ct, checkFree: e.IsActive);
                e.ClientDocumentId = doc.ClientDocumentId;
            }
            else doc = current;
        }
        else if (e.ClientDocumentId is int linked)
            doc = await db.Set<ClientDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.ClientDocumentId == linked, ct);

        // la copia de la exención (para los listados) se sincroniza con el documento activo
        var cert = e.CertificateNumber;
        if (doc is { IsActive: true })
        {
            cert = ClientDocumentRules.NormalizeNumber(doc.DocumentNumber) ?? e.CertificateNumber;
            e.ValidFrom = doc.ValidFrom ?? e.ValidFrom;
            e.ValidTo = doc.ValidTo ?? e.ValidTo;
        }
        var (type, certificate, notes) = NormalizeExemption(req.ExemptionType ?? e.ExemptionType, cert, req.Notes ?? e.Notes);
        // Lote 237: el alcance solo se revisa si viene algo de él (lo que no viene no cambia)
        if (req.Scope is not null || req.ComponentCodes is not null)
            (e.Scope, e.ComponentCodes) = await NormalizeScopeAsync(req.Scope ?? e.Scope, req.ComponentCodes ?? TaxRules.SplitComponentCodes(e.ComponentCodes), ct);
        (e.ExemptionType, e.CertificateNumber, e.Notes) = (type, certificate, notes);
        await db.SaveGuardedAsync(ClientExemptionRules.DocumentAlreadyBacks, ct);
        return (await ToDtosAsync(new[] { e }, ct))[0];
    }

    public Task<TaxExemptionDto> SetExemptionActiveAsync(int id, bool active, CancellationToken ct) => SetActiveCoreAsync(id, active, ct);

    /// <summary>
    /// Dar de baja siempre se puede. Reactivar (Lote 248) exige el documento vinculado, activo y vigente, y que ninguna otra exención activa use ese documento (409).
    /// </summary>
    private async Task<TaxExemptionDto> SetActiveCoreAsync(int id, bool active, CancellationToken ct)
    {
        var e = await db.Set<ClientTaxExemption>().AsTracking().FirstOrDefaultAsync(x => x.ClientTaxExemptionId == id, ct)
            ?? throw new NotFoundException("Exención", feminine: true);
        if (active && !e.IsActive)
        {
            if (e.ClientDocumentId is not int documentId) throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentRequired);
            var doc = await db.Set<ClientDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.ClientDocumentId == documentId, ct);
            if (doc is null || !doc.IsActive) throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentInactive);
            if (doc.ValidTo is not DateOnly to) throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentWithoutValidTo);
            if (to < Today) throw new ValidationException("clientDocumentPublicId", ClientExemptionRules.DocumentExpired);
            await EnsureDocumentFreeAsync(documentId, e.ClientTaxExemptionId, ct);
        }
        e.IsActive = active;
        await db.SaveGuardedAsync(active ? ClientExemptionRules.DocumentAlreadyBacks : "La exención cambió al mismo tiempo. Vuelva a intentarlo.", ct);
        return (await ToDtosAsync(new[] { e }, ct))[0];
    }

    /// <summary>
    /// Lote 248 — el documento que va a respaldar una exención: del MISMO cliente (otro cliente u otra compañía = 404 «Documento no encontrado.»), activo, de un tipo
    /// «de impuestos», con número y con «hasta» de hoy en adelante; y (checkFree) que no respalde ya otra exención activa (409).
    /// </summary>
    internal async Task<(ClientDocument Doc, ClientDocumentTypeInfo? Type)> ResolveBackingDocumentAsync(int clientId, Guid documentPublicId, int? exceptExemptionId,
        CancellationToken ct, bool checkFree = true)
    {
        var tenantId = T.RequireTenantId();
        var doc = await db.Set<ClientDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.PublicId == documentPublicId && d.ClientId == clientId, ct)
                  ?? throw new NotFoundException("Documento");
        var type = (await ClientDocumentQueries.TypesByIdAsync(db, tenantId, new[] { doc.DocumentTypeLookupId }, ct)).GetValueOrDefault(doc.DocumentTypeLookupId);
        var (field, error) = ClientExemptionRules.ValidateBackingDocument(doc.IsActive, type?.Marks.IsTax == true, doc.DocumentNumber, doc.ValidTo, Today);
        if (error is not null) throw new ValidationException(field ?? "clientDocumentPublicId", error);
        if (checkFree) await EnsureDocumentFreeAsync(doc.ClientDocumentId, exceptExemptionId, ct);
        return (doc, type);
    }

    private async Task EnsureDocumentFreeAsync(int documentId, int? exceptExemptionId, CancellationToken ct)
    {
        if (await db.Set<ClientTaxExemption>().AsNoTracking()
                .AnyAsync(x => x.ClientDocumentId == documentId && x.IsActive && (exceptExemptionId == null || x.ClientTaxExemptionId != exceptExemptionId), ct))
            throw new ConflictException(ClientExemptionRules.DocumentAlreadyBacks);
    }

    private static (string Type, string Cert, string? Notes) NormalizeExemption(string? type, string? cert, string? notes)
    {
        var t = (type ?? string.Empty).Trim();
        if (t.Length == 0) throw new ValidationException("exemptionType", TaxRules.ExemptionTypeRequired);
        if (t.Length > 60) throw new ValidationException("exemptionType", TaxRules.ExemptionTypeTooLong);
        var c = (cert ?? string.Empty).Trim();
        if (c.Length == 0) throw new ValidationException("certificateNumber", TaxRules.CertificateRequired);
        if (c.Length > 60) throw new ValidationException("certificateNumber", TaxRules.CertificateTooLong);
        var n = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (n is { Length: > 300 }) throw new ValidationException("notes", TaxRules.NotesTooLong);
        return (t, c, n);
    }

    /// <summary>
    /// Lote 237 — alcance de la exención ya validado: (ALL, null) o (COMPONENTS, «CITY,STATE»). Cada código debe ser un componente de alguna tasa de la
    /// compañía (de cualquier vigencia): así un error de dedo no deja una exención que nunca aplica.
    /// Lote 248 — internal: la captura única (ClientTaxExemptionCaptureService) lo usa para validar TODO antes de guardar el archivo.
    /// </summary>
    internal async Task<(string Scope, string? Codes)> NormalizeScopeAsync(string? scope, IReadOnlyList<string>? componentCodes, CancellationToken ct)
    {
        var error = TaxRules.ValidateExemptionScope(scope, componentCodes, out var normalized, out var codes);
        if (error is not null) throw new ValidationException(error == TaxRules.ExemptionScopeInvalid ? "scope" : "componentCodes", error);
        if (normalized == TaxExemptionScopes.All) return (normalized, null);
        var known = await db.Set<TaxRateComponent>().Where(c => codes.Contains(c.Code)).Select(c => c.Code).Distinct().ToListAsync(ct);
        var unknown = codes.FirstOrDefault(c => !known.Contains(c, StringComparer.OrdinalIgnoreCase));
        if (unknown is not null) throw new ValidationException("componentCodes", string.Format(TaxRules.ExemptionComponentUnknownFormat, unknown));
        return (normalized, string.Join(',', codes));
    }

    /// <summary>Lote 248 — los documentos que respaldan exenciones (por id), con lo que necesita <see cref="ClientExemptionBacking.Effective"/> y la lista.</summary>
    private sealed record BackingDocRow(int ClientDocumentId, Guid PublicId, bool IsActive, DateOnly? ValidFrom, DateOnly? ValidTo, string? DocumentNumber,
        int DocumentTypeLookupId, int StoredFileId)
    {
        public ExemptionBackingDocument Backing => new(IsActive, ValidFrom, ValidTo, DocumentNumber);
    }

    private async Task<Dictionary<int, BackingDocRow>> BackingDocsAsync(IEnumerable<int?> documentIds, CancellationToken ct)
    {
        var ids = documentIds.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, BackingDocRow>();
        var rows = await db.Set<ClientDocument>().AsNoTracking().Where(d => ids.Contains(d.ClientDocumentId))
            .Select(d => new { d.ClientDocumentId, d.PublicId, d.IsActive, d.ValidFrom, d.ValidTo, d.DocumentNumber, d.DocumentTypeLookupId, d.StoredFileId })
            .ToListAsync(ct);
        return rows.ToDictionary(d => d.ClientDocumentId,
            d => new BackingDocRow(d.ClientDocumentId, d.PublicId, d.IsActive, d.ValidFrom, d.ValidTo, d.DocumentNumber, d.DocumentTypeLookupId, d.StoredFileId));
    }

    private static ExemptionBackingResult Effective(ClientTaxExemption e, IReadOnlyDictionary<int, BackingDocRow> docs)
        => ClientExemptionBacking.Effective(e.ClientDocumentId, e.ValidFrom, e.ValidTo, e.CertificateNumber,
            e.ClientDocumentId is int d && docs.TryGetValue(d, out var doc) ? doc.Backing : null);

    private async Task<IReadOnlyList<TaxExemptionDto>> ToDtosAsync(IReadOnlyList<ClientTaxExemption> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<TaxExemptionDto>();
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.Name, c.PublicId }).ToDictionaryAsync(c => c.ClientId, ct);
        var docs = await BackingDocsAsync(rows.Select(r => r.ClientDocumentId), ct);
        var fileIds = docs.Values.Select(d => d.StoredFileId).Distinct().ToList();
        var files = fileIds.Count == 0 ? new Dictionary<int, Guid>()
            : await db.Set<StoredFile>().AsNoTracking().Where(f => fileIds.Contains(f.StoredFileId) && f.IsActive).ToDictionaryAsync(f => f.StoredFileId, f => f.PublicId, ct);
        var types = docs.Count == 0 ? new Dictionary<int, ClientDocumentTypeInfo>()
            : await ClientDocumentQueries.TypesByIdAsync(db, T.RequireTenantId(), docs.Values.Select(d => d.DocumentTypeLookupId), ct);
        return rows.Select(e =>
        {
            var eff = Effective(e, docs);
            var doc = e.ClientDocumentId is int d ? docs.GetValueOrDefault(d) : null;
            var type = doc is null ? null : types.GetValueOrDefault(doc.DocumentTypeLookupId);
            var client = clients.GetValueOrDefault(e.ClientId);
            var status = !e.IsActive ? "INACTIVE" : !eff.Applies ? "DOCUMENT_REMOVED"
                : eff.From > Today ? "SCHEDULED" : TaxRules.InEffect(eff.From, eff.To, Today) ? "VALID" : "EXPIRED";
            return new TaxExemptionDto(e.ClientTaxExemptionId, e.PublicId, e.ClientId, client?.Name ?? string.Empty, e.ExemptionType, eff.Certificate, eff.From, eff.To,
                e.Notes, e.IsActive, status, e.Scope, TaxRules.SplitComponentCodes(e.ComponentCodes),
                ClientPublicId: client?.PublicId, DocumentPublicId: doc?.PublicId, DocumentType: type?.Code,
                DocumentTypeLabel: type is null ? null : MultilingualText.Resolve(type.LabelJson, T.Lang),
                FilePublicId: doc is not null && files.TryGetValue(doc.StoredFileId, out var f) ? f : null,
                MissingDocument: e.ClientDocumentId is null);
        }).ToList();
    }

    // ---------------------------------------------------------------- categoría del producto

    public async Task<ProductTaxCategoryDto> SetProductCategoryAsync(int productId, ProductTaxCategoryRequest req, CancellationToken ct)
    {
        var code = (req?.Category ?? string.Empty).Trim().ToUpperInvariant();
        if (!TaxCategories.All.Contains(code)) throw new ValidationException("category", TaxRules.CategoryInvalid);
        var product = await db.Set<Product>().AsTracking().FirstOrDefaultAsync(p => p.ProductId == productId, ct) ?? throw new NotFoundException("Producto");
        // Gravable es lo normal: se guarda NULL para no llenar de filas lo que es la regla general.
        product.TaxCategoryLookupId = code == TaxCategories.Taxable ? null : await lookups.GetIdAsync(TaxCodes.CategoryDomain, code, ct);
        await db.SaveGuardedAsync("El producto cambió al mismo tiempo. Vuelva a intentarlo.", ct);
        return new ProductTaxCategoryDto(product.ProductId, product.Sku, product.Name, code);
    }

    public async Task<ProductTaxCategoryDto> GetProductCategoryAsync(int productId, CancellationToken ct)
    {
        var p = await db.Set<Product>().FirstOrDefaultAsync(x => x.ProductId == productId, ct) ?? throw new NotFoundException("Producto");
        var code = p.TaxCategoryLookupId is int id ? (await lookups.GetAsync(id, ct))?.InternalCode ?? TaxCategories.Taxable : TaxCategories.Taxable;
        return new ProductTaxCategoryDto(p.ProductId, p.Sku, p.Name, code);
    }

    /// <summary>
    /// Productos activos con su categoría de impuesto, para cambiarla desde la pantalla. q = parte del código, del nombre o del código de barras;
    /// category = TAXABLE / EXEMPT / ZERO_RATE (vacío = todas). Orden por código; hasta 500 por página.
    /// </summary>
    public async Task<PagedResult<ProductTaxCategoryDto>> ListProductCategoriesAsync(string? q, string? category, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 500);
        var query = db.Set<Product>().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => p.Sku.Contains(s) || p.Name.Contains(s) || (p.Barcode != null && p.Barcode.Contains(s)));
        }
        var cat = (category ?? string.Empty).Trim().ToUpperInvariant();
        if (cat.Length > 0)
        {
            if (!TaxCategories.All.Contains(cat)) throw new ValidationException("category", TaxRules.CategoryInvalid);
            if (cat == TaxCategories.Taxable) query = query.Where(p => p.TaxCategoryLookupId == null);
            else
            {
                var id = await lookups.GetIdAsync(TaxCodes.CategoryDomain, cat, ct);
                query = query.Where(p => p.TaxCategoryLookupId == id);
            }
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Sku).ThenBy(p => p.ProductId).Skip(skip).Take(take).Select(p => new { p.ProductId, p.Sku, p.Name, p.TaxCategoryLookupId }).ToListAsync(ct);
        var codes = new Dictionary<int, string>();
        foreach (var id in rows.Where(r => r.TaxCategoryLookupId != null).Select(r => r.TaxCategoryLookupId!.Value).Distinct())
            codes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? TaxCategories.Taxable;
        return new PagedResult<ProductTaxCategoryDto>(
            rows.Select(r => new ProductTaxCategoryDto(r.ProductId, r.Sku, r.Name, r.TaxCategoryLookupId is int c ? codes[c] : TaxCategories.Taxable)).ToList(), total, skip, take);
    }

    // ---------------------------------------------------------------- cálculo

    public async Task<TaxCalcDto> CalculateAsync(TaxCalcRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var lines = req.Lines ?? Array.Empty<TaxCalcLineRequest>();
        if (lines.Count == 0) throw new ValidationException("lines", TaxRules.LinesRequired);
        if (lines.Count > 500) throw new ValidationException("lines", TaxRules.TooManyLines);
        var date = req.Date ?? Today;
        var setting = await GetOrCreateSettingAsync(ct);
        var warnings = new List<string>();

        // exención del cliente (Lote 237: de todo el impuesto, o solo de algunos componentes; las vigentes se combinan y una total gana)
        var exemptions = new TaxExemptionOutcome(null, new Dictionary<string, string>(), null);
        if (req.ClientId is int clientId)
        {
            if (!await db.Set<Client>().AnyAsync(c => c.ClientId == clientId, ct)) throw new NotFoundException("Cliente");
            var ex = await db.Set<ClientTaxExemption>().AsNoTracking().Where(e => e.ClientId == clientId && e.IsActive).OrderByDescending(e => e.ValidFrom).ToListAsync(ct);
            // Lote 248: la vigencia y el certificado efectivos salen del documento que respalda la exención (una vinculada a un documento quitado no exime)
            var docs = await BackingDocsAsync(ex.Select(e => e.ClientDocumentId), ct);
            var infos = new List<TaxExemptionInfo>();
            foreach (var e in ex)
            {
                var eff = Effective(e, docs);
                if (eff.Applies) infos.Add(new TaxExemptionInfo(e.Scope, TaxRules.SplitComponentCodes(e.ComponentCodes), eff.Certificate, eff.From, eff.To));
            }
            exemptions = TaxRules.ResolveExemptions(infos, date);
            if (exemptions.Expired is { } expired)
                warnings.Add($"El cliente tiene una exención vencida (certificado {expired.CertificateNumber}, venció el {expired.ValidTo:dd/MM/yyyy}): se cobró el impuesto.");
        }
        var valid = exemptions.Full;

        // productos y reglas de cargo
        var productIds = lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var categoryIds = products.Values.Where(p => p.TaxCategoryLookupId is not null).Select(p => p.TaxCategoryLookupId!.Value).Distinct().ToList();
        var categoryCodes = new Dictionary<int, string>();
        foreach (var id in categoryIds) categoryCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? TaxCategories.Taxable;
        var chargeTypes = await ChargeTypesAsync(ct);
        var chargeRules = await db.Set<TaxChargeRule>().ToListAsync(ct);
        // Lote 248: períodos de exención por temporada vigentes en la fecha (se carga UNA vez: períodos, destinos activos y el árbol de categorías)
        IReadOnlyList<TaxPeriodInfo> periods = productIds.Count == 0 ? Array.Empty<TaxPeriodInfo>() : await PeriodsInEffectAsync(date, productIds, ct);
        IReadOnlyDictionary<int, int?> parentOf = periods.Any(p => p.CategoryIds.Count > 0)
            ? await db.Set<ProductCategory>().AsNoTracking().Select(c => new { c.ProductCategoryId, c.ParentId }).ToDictionaryAsync(c => c.ProductCategoryId, c => c.ParentId, ct)
            : new Dictionary<int, int?>();
        var periodNames = new List<string?>();

        var inputs = new List<TaxLineInput>();
        var n = 0;
        foreach (var l in lines)
        {
            n++;
            var key = string.IsNullOrWhiteSpace(l.Key) ? n.ToString() : l.Key.Trim();
            if (l.Amount is not decimal amount || amount < 0) throw new ValidationException("lines", TaxRules.AmountInvalid);
            TaxTreatment t;
            string? periodName = null;
            if (l.ProductId is int pid)
            {
                if (!products.TryGetValue(pid, out var product)) throw new NotFoundException("Producto");
                var cat = product.TaxCategoryLookupId is int cid ? categoryCodes[cid] : TaxCategories.Taxable;
                t = cat switch { TaxCategories.Exempt => TaxTreatment.ExemptProduct, TaxCategories.ZeroRate => TaxTreatment.ZeroRate, _ => TaxTreatment.Taxable };
                // Lote 248: el producto gravable o de tasa cero que cubre un período de temporada sale exento ANTES de mirar la exención del cliente
                if ((t is TaxTreatment.Taxable or TaxTreatment.ZeroRate) && periods.Count > 0)
                {
                    var covering = TaxExemptionPeriodRules.Covering(periods, pid, product.ProductCategoryId, parentOf);
                    if (covering.Count > 0)
                    {
                        t = TaxTreatment.ExemptProductPeriod;
                        periodName = TaxExemptionPeriodRules.FirstName(covering);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(l.ChargeType))
            {
                var ct2 = l.ChargeType.Trim().ToUpperInvariant();
                if (!chargeTypes.TryGetValue(ct2, out var lk)) throw new ValidationException("lines", TaxRules.ChargeTypeInvalid);
                t = ct2 == "PRODUCT_SALE" || chargeRules.Any(r => r.ChargeTypeLookupId == lk.LookupCodeId && r.IsTaxable)
                    ? TaxTreatment.Taxable : TaxTreatment.NotTaxableCharge;
            }
            else throw new ValidationException("lines", "Cada línea necesita un producto o un tipo de cargo.");
            // Un cliente con exención vigente no paga impuesto en nada que, de otro modo, lo pagaría.
            if (valid is not null && (t == TaxTreatment.Taxable || t == TaxTreatment.ZeroRate)) t = TaxTreatment.ExemptClient;
            inputs.Add(new TaxLineInput(key, Math.Round(amount, 4), t));
            periodNames.Add(periodName);
        }

        // tasa vigente (solo si alguna línea la necesita)
        TaxRateInfo? rate = null;
        if (inputs.Any(i => i.Treatment == TaxTreatment.Taxable))
        {
            if (string.IsNullOrEmpty(setting.DefaultRateCode)) throw new ValidationException("lines", TaxRules.NoDefaultRate);
            var candidates = await db.Set<TaxRate>().Include(r => r.Components).Where(r => r.Code == setting.DefaultRateCode).ToListAsync(ct);
            var r = candidates.FirstOrDefault(x => TaxRules.InEffect(x.EffectiveFrom, x.EffectiveTo, date)) ?? throw new ValidationException("date", TaxRules.NoRateInEffect);
            rate = new TaxRateInfo(r.TaxRateId, r.Code, r.Name, r.Percent, r.Components.OrderBy(c => c.SortOrder).Select(c => new TaxComponentInfo(c.Code, c.Name, c.Percent)).ToList());
        }

        // Lote 237: exención parcial — un componente que la tasa vigente no tiene no cambia nada, y se avisa
        var partial = exemptions.ComponentCertificates;
        if (rate is not null)
            foreach (var code in TaxRules.UnknownComponents(rate, partial.Keys).OrderBy(c => c, StringComparer.Ordinal))
                warnings.Add(string.Format(TaxRules.ExemptionComponentNotInRateFormat, partial[code], code));
        var applied = TaxRules.ExemptCodesOf(rate, partial.Keys.ToList());
        // el certificado (o certificados) de los componentes que de verdad se dejaron de cobrar
        var partialCertificates = rate is null || applied.Count == 0 ? null
            : string.Join(", ", rate.Components.Where(c => applied.Contains(c.Code) && c.Percent > 0).Select(c => partial[c.Code]).Distinct());
        if (string.IsNullOrEmpty(partialCertificates)) partialCertificates = null;

        var result = TaxRules.Calculate(inputs, rate, setting.PricesIncludeTax, setting.RoundingMode, applied.ToList());
        TaxCalcComponentDto Component(TaxComponentAmount c) => c.IsExempt
            ? new TaxCalcComponentDto(c.Code, c.Name, c.Percent, c.Amount, TaxReasons.ExemptClientPartial, partial.GetValueOrDefault(c.Code))
            : new TaxCalcComponentDto(c.Code, c.Name, c.Percent, c.Amount);
        return new TaxCalcDto(date, rate?.Code, rate?.Percent ?? 0m, setting.PricesIncludeTax, setting.RoundingMode,
            result.Lines.Select((l, i) => new TaxCalcLineDto(l.Key, l.Amount, l.Net, l.TaxAmount, l.Total, l.Reason, l.IsTaxExempt, l.TaxRateId, l.TaxRatePct,
                // exento por una exención total → su certificado; exención parcial (o parciales que suman toda la tasa) → los certificados que aplicaron
                l.Reason == TaxReasons.ExemptClient ? valid?.CertificateNumber ?? partialCertificates
                    : l.Reason == TaxReasons.ExemptClientPartial ? partialCertificates : null,
                l.Components.Select(Component).ToList(),
                l.Reason == TaxReasons.ExemptProductPeriod ? periodNames[i] : null)).ToList(),
            result.Subtotal, result.TaxAmount, result.Total,
            result.Components.Select(Component).ToList(), warnings);
    }

    /// <summary>
    /// Lote 248 — los períodos de exención por temporada ACTIVOS y vigentes en la fecha (bordes incluidos) de la compañía, con sus destinos ACTIVOS (solo los
    /// productos de este cálculo; las categorías completas porque incluyen sus subcategorías).
    /// </summary>
    private async Task<IReadOnlyList<TaxPeriodInfo>> PeriodsInEffectAsync(DateOnly date, IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        var rows = await db.Set<TaxExemptionPeriod>().AsNoTracking().Where(p => p.IsActive && p.ValidFrom <= date && p.ValidTo >= date)
            .Select(p => new { p.TaxExemptionPeriodId, p.Name, p.ValidFrom, p.ValidTo, p.Scope }).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<TaxPeriodInfo>();
        var ids = rows.Select(r => r.TaxExemptionPeriodId).ToList();
        var products = await db.Set<TaxExemptionPeriodProduct>().AsNoTracking()
            .Where(x => x.IsActive && ids.Contains(x.TaxExemptionPeriodId) && productIds.Contains(x.ProductId))
            .Select(x => new { x.TaxExemptionPeriodId, x.ProductId }).ToListAsync(ct);
        var categories = await db.Set<TaxExemptionPeriodCategory>().AsNoTracking()
            .Where(x => x.IsActive && ids.Contains(x.TaxExemptionPeriodId))
            .Select(x => new { x.TaxExemptionPeriodId, x.ProductCategoryId }).ToListAsync(ct);
        return rows.Select(r => new TaxPeriodInfo(r.TaxExemptionPeriodId, r.Name, r.ValidFrom, r.ValidTo, r.Scope,
                products.Where(x => x.TaxExemptionPeriodId == r.TaxExemptionPeriodId).Select(x => x.ProductId).ToHashSet(),
                categories.Where(x => x.TaxExemptionPeriodId == r.TaxExemptionPeriodId).Select(x => x.ProductCategoryId).ToHashSet()))
            .ToList();
    }
}
