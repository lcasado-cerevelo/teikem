using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
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

    public async Task<IReadOnlyList<TaxExemptionDto>> ListExemptionsAsync(int? clientId, CancellationToken ct)
    {
        var q = db.Set<ClientTaxExemption>().AsQueryable();
        if (clientId is int c) q = q.Where(e => e.ClientId == c);
        var rows = await q.OrderByDescending(e => e.ValidFrom).ThenByDescending(e => e.ClientTaxExemptionId).ToListAsync(ct);
        var names = await ClientNamesAsync(rows.Select(r => r.ClientId).Distinct().ToList(), ct);
        return rows.Select(e => ToDto(e, names.GetValueOrDefault(e.ClientId, string.Empty))).ToList();
    }

    public async Task<TaxExemptionDto> CreateExemptionAsync(TaxExemptionRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        if (req.ClientId is not int clientId || !await db.Set<Client>().AnyAsync(c => c.ClientId == clientId, ct))
            throw new NotFoundException("Cliente");
        var (type, cert, notes) = NormalizeExemption(req.ExemptionType, req.CertificateNumber, req.Notes);
        if (req.ValidFrom is not DateOnly from) throw new ValidationException("validFrom", "Escriba desde qué fecha vale la exención.");
        if (req.ValidTo is DateOnly to && to < from) throw new ValidationException("validTo", TaxRules.DatesOrder);

        var e = new ClientTaxExemption { TenantId = tenantId, ClientId = clientId, ExemptionType = type, CertificateNumber = cert, ValidFrom = from, ValidTo = req.ValidTo, Notes = notes, IsActive = true };
        db.Set<ClientTaxExemption>().Add(e);
        await db.SaveGuardedAsync("La exención cambió al mismo tiempo. Vuelva a intentarlo.", ct);
        return ToDto(e, (await ClientNamesAsync(new[] { clientId }, ct)).GetValueOrDefault(clientId, string.Empty));
    }

    public async Task<TaxExemptionDto> UpdateExemptionAsync(int id, TaxExemptionPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var e = await db.Set<ClientTaxExemption>().AsTracking().FirstOrDefaultAsync(x => x.ClientTaxExemptionId == id, ct)
            ?? throw new NotFoundException("Exención", feminine: true);
        var (type, cert, notes) = NormalizeExemption(req.ExemptionType ?? e.ExemptionType, req.CertificateNumber ?? e.CertificateNumber, req.Notes ?? e.Notes);
        var from = req.ValidFrom ?? e.ValidFrom;
        var to = req.ClearValidTo == true ? null : req.ValidTo ?? e.ValidTo;
        if (to is DateOnly t && t < from) throw new ValidationException("validTo", TaxRules.DatesOrder);
        (e.ExemptionType, e.CertificateNumber, e.Notes, e.ValidFrom, e.ValidTo) = (type, cert, notes, from, to);
        await db.SaveGuardedAsync("La exención cambió al mismo tiempo. Vuelva a intentarlo.", ct);
        return ToDto(e, (await ClientNamesAsync(new[] { e.ClientId }, ct)).GetValueOrDefault(e.ClientId, string.Empty));
    }

    public Task<TaxExemptionDto> SetExemptionActiveAsync(int id, bool active, CancellationToken ct) => SetActiveCoreAsync(id, active, ct);

    private async Task<TaxExemptionDto> SetActiveCoreAsync(int id, bool active, CancellationToken ct)
    {
        var e = await db.Set<ClientTaxExemption>().AsTracking().FirstOrDefaultAsync(x => x.ClientTaxExemptionId == id, ct)
            ?? throw new NotFoundException("Exención", feminine: true);
        e.IsActive = active;
        await db.SaveGuardedAsync("La exención cambió al mismo tiempo. Vuelva a intentarlo.", ct);
        return ToDto(e, (await ClientNamesAsync(new[] { e.ClientId }, ct)).GetValueOrDefault(e.ClientId, string.Empty));
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

    private async Task<Dictionary<int, string>> ClientNamesAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
        => await db.Set<Client>().Where(c => ids.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

    private TaxExemptionDto ToDto(ClientTaxExemption e, string clientName)
    {
        var status = !e.IsActive ? "INACTIVE" : e.ValidFrom > Today ? "SCHEDULED" : TaxRules.InEffect(e.ValidFrom, e.ValidTo, Today) ? "VALID" : "EXPIRED";
        return new TaxExemptionDto(e.ClientTaxExemptionId, e.PublicId, e.ClientId, clientName, e.ExemptionType, e.CertificateNumber, e.ValidFrom, e.ValidTo, e.Notes, e.IsActive, status);
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

        // exención del cliente
        ClientTaxExemption? valid = null;
        if (req.ClientId is int clientId)
        {
            if (!await db.Set<Client>().AnyAsync(c => c.ClientId == clientId, ct)) throw new NotFoundException("Cliente");
            var ex = await db.Set<ClientTaxExemption>().Where(e => e.ClientId == clientId && e.IsActive).OrderByDescending(e => e.ValidFrom).ToListAsync(ct);
            valid = ex.FirstOrDefault(e => TaxRules.InEffect(e.ValidFrom, e.ValidTo, date));
            if (valid is null && ex.FirstOrDefault(e => e.ValidTo is DateOnly vt && vt < date) is { } expired)
                warnings.Add($"El cliente tiene una exención vencida (certificado {expired.CertificateNumber}, venció el {expired.ValidTo:dd/MM/yyyy}): se cobró el impuesto.");
        }

        // productos y reglas de cargo
        var productIds = lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var categoryIds = products.Values.Where(p => p.TaxCategoryLookupId is not null).Select(p => p.TaxCategoryLookupId!.Value).Distinct().ToList();
        var categoryCodes = new Dictionary<int, string>();
        foreach (var id in categoryIds) categoryCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? TaxCategories.Taxable;
        var chargeTypes = await ChargeTypesAsync(ct);
        var chargeRules = await db.Set<TaxChargeRule>().ToListAsync(ct);

        var inputs = new List<TaxLineInput>();
        var n = 0;
        foreach (var l in lines)
        {
            n++;
            var key = string.IsNullOrWhiteSpace(l.Key) ? n.ToString() : l.Key.Trim();
            if (l.Amount is not decimal amount || amount < 0) throw new ValidationException("lines", TaxRules.AmountInvalid);
            TaxTreatment t;
            if (l.ProductId is int pid)
            {
                if (!products.TryGetValue(pid, out var product)) throw new NotFoundException("Producto");
                var cat = product.TaxCategoryLookupId is int cid ? categoryCodes[cid] : TaxCategories.Taxable;
                t = cat switch { TaxCategories.Exempt => TaxTreatment.ExemptProduct, TaxCategories.ZeroRate => TaxTreatment.ZeroRate, _ => TaxTreatment.Taxable };
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

        var result = TaxRules.Calculate(inputs, rate, setting.PricesIncludeTax, setting.RoundingMode);
        return new TaxCalcDto(date, rate?.Code, rate?.Percent ?? 0m, setting.PricesIncludeTax, setting.RoundingMode,
            result.Lines.Select(l => new TaxCalcLineDto(l.Key, l.Amount, l.Net, l.TaxAmount, l.Total, l.Reason, l.IsTaxExempt, l.TaxRateId, l.TaxRatePct,
                l.Reason == TaxReasons.ExemptClient ? valid?.CertificateNumber : null,
                l.Components.Select(c => new TaxCalcComponentDto(c.Code, c.Name, c.Percent, c.Amount)).ToList())).ToList(),
            result.Subtotal, result.TaxAmount, result.Total,
            result.Components.Select(c => new TaxCalcComponentDto(c.Code, c.Name, c.Percent, c.Amount)).ToList(), warnings);
    }
}
