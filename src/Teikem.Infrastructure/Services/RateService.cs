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
/// Lote 2 (P4) — Tarifas del contrato y cotización.
/// Modelo: "Por servicio" = RateComponent{BASE_FREIGHT, FIXED, PER_SHIPMENT, servicio+paquete, FlatAmount};
/// "Pieza extra" = RateComponent{EXTRA_PIECE, TIERED, GRADUATED, PER_PIECE, servicio+paquete} + RateTier por rango de piezas.
/// Historial efectivo-fechado (principio #8): editar = cerrar la fila (EffectiveTo = fecha nueva) y abrir otra; quitar = cerrar.
/// Nunca se sobrescribe un monto ni se hace DELETE. Servicio y paquete son inmutables (R25).
/// Seguridad: el contrato se resuelve siempre bajo el filtro global del tenant (ClientQueries) y los tramos se alcanzan
/// SOLO a través del componente del contrato, nunca por id suelto (RateTier no tiene TenantId).
/// Toda escritura exige la capacidad EDIT_CONTRACT en el estatus actual del contrato y corre en RunInTransactionAsync.
/// </summary>
public sealed class RateService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    private const string ComponentOffPerService = "El componente 'Por servicio' está apagado en el modelo de facturación del contrato; enciéndalo antes de trabajar sus tarifas.";
    private const string ComponentOffExtraPiece = "El componente 'Pieza extra' está apagado en el modelo de facturación del contrato; enciéndalo antes de trabajar sus tramos.";
    private const string DuplicateOpenRow = "Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete en el contrato; edítela o ciérrela antes de crear otra.";
    private const string RepeatedLine = "El par servicio/tipo de paquete está repetido; envíe una sola línea por (servicio, tipo de paquete) con el total de piezas.";

    // ---------------- Consulta ----------------

    public async Task<RateComponentsDto> GetForContractAsync(Guid contractPublicId, DateOnly? asOf, bool includeHistory, CancellationToken ct)
    {
        var date = asOf ?? Today();
        var contract = await db.ResolveContractAsync(contractPublicId, ct);
        var (baseId, extraId) = await KindIdsAsync(ct);

        var rows = await db.RateComponents.AsNoTracking().Include(c => c.Tiers)
            .Where(c => c.ContractId == contract.ContractId && c.IsActive)
            .OrderBy(c => c.ServiceTypeLookupId).ThenBy(c => c.PackageTypeLookupId).ThenBy(c => c.EffectiveFrom).ThenBy(c => c.RateComponentId)
            .ToListAsync(ct);
        if (!includeHistory) rows = rows.Where(c => EffectiveDated.IsCurrentOn(c, date)).ToList();

        var perService = new List<RateRowDto>();
        var extraPiece = new List<ExtraPieceRowDto>();
        foreach (var c in rows)
        {
            if (c.ComponentTypeLookupId == baseId)
            {
                var (svc, svcLabel, pkg, pkgLabel) = await CodesAsync(c, ct);
                perService.Add(new RateRowDto(c.RateComponentId, svc, svcLabel, pkg, pkgLabel, c.FlatAmount ?? 0m,
                    c.EffectiveFrom, c.EffectiveTo, EffectiveDated.IsCurrentOn(c, date)));
            }
            else if (c.ComponentTypeLookupId == extraId)
            {
                var (svc, svcLabel, pkg, pkgLabel) = await CodesAsync(c, ct);
                extraPiece.Add(new ExtraPieceRowDto(c.RateComponentId, svc, svcLabel, pkg, pkgLabel,
                    c.EffectiveFrom, c.EffectiveTo, EffectiveDated.IsCurrentOn(c, date), TiersOf(c, date, includeHistory)));
            }
            // Otros tipos de componente (PICKUP_FEE, SURCHARGE...) no se administran en este lote.
        }
        return new RateComponentsDto(contract.PublicId, date, perService, extraPiece);
    }

    // ---------------- Componentes ----------------

    /// <summary>Alta de tarifa por servicio o de componente de pieza extra. 409 si el componente está apagado o ya hay fila abierta (R26).</summary>
    public async Task<RateComponentDto> AddComponentAsync(Guid contractPublicId, RateComponentCreateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        var kind = NormalizeKind(req.Kind);
        var serviceTypeId = await RequireLookupAsync(LookupDomains.ServiceType, req.ServiceType, "serviceType", "El tipo de servicio es obligatorio.", ct);
        var packageTypeId = await RequireLookupAsync(LookupDomains.PackageType, req.PackageType, "packageType", "El tipo de paquete es obligatorio.", ct);
        var effectiveFrom = req.EffectiveFrom ?? Today();
        decimal? rate = null;
        if (kind == RateKinds.PerService)
        {
            if (req.Rate is null) throw new ValidationException("rate", "La tarifa por servicio es obligatoria.");
            if (req.Rate.Value < 0) throw new ValidationException("rate", "La tarifa no puede ser negativa.");
            rate = req.Rate.Value;
        }
        var (baseId, extraId) = await KindIdsAsync(ct);
        var typeId = kind == RateKinds.PerService ? baseId : extraId;
        var basisId = await lookups.GetIdAsync(LookupDomains.RateBasis, kind == RateKinds.PerService ? RateBases.PerShipment : RateBases.PerPiece, ct);
        var pricingModeId = await lookups.GetIdAsync(LookupDomains.PricingMode, kind == RateKinds.PerService ? PricingModes.Fixed : PricingModes.Tiered, ct);
        int? tierModeId = kind == RateKinds.ExtraPiece ? await lookups.GetIdAsync(LookupDomains.TierMode, TierModes.Graduated, ct) : null;

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var contract = await LoadContractForWriteAsync(contractPublicId, ct2);
            EnsureComponentEnabled(contract, kind);

            // Guarda por intervalo (documento L639: nunca dos filas vigentes el mismo día para el mismo servicio+paquete). La fila
            // nueva nace abierta, así que choca con toda fila existente que no haya terminado antes de effectiveFrom
            // (EffectiveTo NULL o > effectiveFrom). UQ_RateComponent_Open solo cubre filas abiertas: es la segunda barrera.
            var duplicate = await db.RateComponents.AnyAsync(c =>
                c.ContractId == contract.ContractId && c.IsActive && (c.EffectiveTo == null || c.EffectiveTo > effectiveFrom)
                && c.ComponentTypeLookupId == typeId && c.ServiceTypeLookupId == serviceTypeId && c.PackageTypeLookupId == packageTypeId, ct2);
            if (duplicate) throw new ConflictException(DuplicateOpenRow);

            var row = new RateComponent
            {
                TenantId = tenantId, ContractId = contract.ContractId, RateCardId = null,
                ComponentTypeLookupId = typeId, BasisLookupId = basisId, PricingModeLookupId = pricingModeId, TierModeLookupId = tierModeId,
                ServiceTypeLookupId = serviceTypeId, PackageTypeLookupId = packageTypeId,
                FlatAmount = rate, UnitAmount = null, MinCharge = null,
                EffectiveFrom = effectiveFrom, EffectiveTo = null, IsActive = true,
            };
            db.RateComponents.Add(row);
            await db.SaveGuardedAsync(DuplicateOpenRow, ct2);
            return row.RateComponentId;
        }, ct);

        return await GetComponentDtoAsync(contractPublicId, id, ct);
    }

    /// <summary>
    /// Nueva versión de una tarifa por servicio (R28): cierra la fila vigente en EffectiveFrom e inserta otra con el monto nuevo
    /// copiando servicio y paquete. Misma fecha ⇒ la fila anterior queda de longitud cero (historial).
    /// </summary>
    public async Task<RateComponentDto> UpdateRateAsync(Guid contractPublicId, int componentId, RateUpdateRequest req, CancellationToken ct)
    {
        var tenantId = RequireTenantId();
        if (req.Rate < 0) throw new ValidationException("rate", "La tarifa no puede ser negativa.");
        var newFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(newFrom, "effectiveFrom");
        var (baseId, _) = await KindIdsAsync(ct);

        var newId = await db.RunInTransactionAsync(async ct2 =>
        {
            var contract = await LoadContractForWriteAsync(contractPublicId, ct2);
            var current = await LoadComponentAsync(contract.ContractId, componentId, includeTiers: false, ct2);
            EnsureComponentEnabled(contract, RateKinds.PerService);   // 404 por pertenencia antes que 409 por regla de negocio
            if (current.ComponentTypeLookupId != baseId)
                throw new ValidationException("Solo las tarifas por servicio se editan aquí; los tramos de pieza extra se editan en /tiers.");
            if (current.EffectiveTo is not null)
                throw new ConflictException("La tarifa ya está cerrada; cree una nueva en lugar de editarla.");

            NewVersion(current, newFrom, "effectiveFrom");
            // Dos SaveChanges en la misma transacción: primero se libera la fila abierta (UQ_RateComponent_Open) y luego se inserta la nueva.
            await db.SaveChangesAsync(ct2);

            var next = new RateComponent
            {
                TenantId = tenantId, ContractId = contract.ContractId, RateCardId = null,
                ComponentTypeLookupId = current.ComponentTypeLookupId, BasisLookupId = current.BasisLookupId,
                PricingModeLookupId = current.PricingModeLookupId, TierModeLookupId = current.TierModeLookupId,
                ServiceTypeLookupId = current.ServiceTypeLookupId, PackageTypeLookupId = current.PackageTypeLookupId,
                FromZoneId = current.FromZoneId, ToZoneId = current.ToZoneId, MinCharge = current.MinCharge,
                FlatAmount = req.Rate, UnitAmount = null,
                EffectiveFrom = newFrom, EffectiveTo = null, IsActive = true,
            };
            db.RateComponents.Add(next);
            await db.SaveGuardedAsync(DuplicateOpenRow, ct2);
            return next.RateComponentId;
        }, ct);

        return await GetComponentDtoAsync(contractPublicId, newId, ct);
    }

    /// <summary>Quitar conservando historial: cierra el componente (y los tramos abiertos de un EXTRA_PIECE) en EffectiveTo.</summary>
    public async Task<RateComponentDto> CloseComponentAsync(Guid contractPublicId, int componentId, RateCloseRequest? req, CancellationToken ct)
    {
        var to = req?.EffectiveTo ?? Today();
        EnsureNotPast(to, "effectiveTo");
        await db.RunInTransactionAsync(async ct2 =>
        {
            var contract = await LoadContractForWriteAsync(contractPublicId, ct2);
            var current = await LoadComponentAsync(contract.ContractId, componentId, includeTiers: true, ct2);
            if (current.EffectiveTo is not null) throw new ConflictException("El componente ya está cerrado.");
            CloseOn(current, to, "effectiveTo");
            foreach (var t in current.Tiers) EffectiveDated.CloseNotBefore(t, to); // nunca antes de su inicio (CK_RateTier_Dates)
            await db.SaveChangesAsync(ct2);
            return current.RateComponentId;
        }, ct);
        return await GetComponentDtoAsync(contractPublicId, componentId, ct);
    }

    // ---------------- Tramos de pieza extra ----------------

    /// <summary>Alta de tramo en un componente EXTRA_PIECE abierto: rango válido, tarifa >= 0 y sin traslape con los tramos abiertos (R27).</summary>
    public async Task<TierDto> AddTierAsync(Guid contractPublicId, int componentId, TierUpsertRequest req, CancellationToken ct)
    {
        ValidateTier(req.FromUnit, req.ToUnit, req.Rate);
        var from = req.EffectiveFrom ?? Today();
        var (_, extraId) = await KindIdsAsync(ct);

        var tierId = await db.RunInTransactionAsync(async ct2 =>
        {
            var contract = await LoadContractForWriteAsync(contractPublicId, ct2);
            var component = await LoadExtraPieceComponentAsync(contract.ContractId, componentId, extraId, ct2);
            EnsureComponentEnabled(contract, RateKinds.ExtraPiece);   // 404 por pertenencia antes que 409 por regla de negocio

            var candidate = new RateTierRules.TierRange(0, req.FromUnit, req.ToUnit);
            EnsureNoOverlap(candidate, component, excludeTierId: null, from);

            var tier = NewTier(component.RateComponentId, req.FromUnit, req.ToUnit, req.Rate, from);
            db.RateTiers.Add(tier);
            await db.SaveGuardedAsync("Ya existe un tramo equivalente en el componente.", ct2);
            return tier.RateTierId;
        }, ct);

        return await GetTierDtoAsync(contractPublicId, componentId, tierId, ct);
    }

    /// <summary>Nueva versión de un tramo: cierra el vigente en EffectiveFrom y abre otro con los valores nuevos, validando traslape contra los demás abiertos.</summary>
    public async Task<TierDto> UpdateTierAsync(Guid contractPublicId, int componentId, int tierId, TierPatchRequest req, CancellationToken ct)
    {
        var newFrom = req.EffectiveFrom ?? Today();
        EnsureNotPast(newFrom, "effectiveFrom");
        var (_, extraId) = await KindIdsAsync(ct);

        var newId = await db.RunInTransactionAsync(async ct2 =>
        {
            var contract = await LoadContractForWriteAsync(contractPublicId, ct2);
            var component = await LoadExtraPieceComponentAsync(contract.ContractId, componentId, extraId, ct2);
            EnsureComponentEnabled(contract, RateKinds.ExtraPiece);   // 404 por pertenencia antes que 409 por regla de negocio
            var current = component.Tiers.FirstOrDefault(t => t.RateTierId == tierId)
                ?? throw new NotFoundException("Tramo", tierId);
            if (current.EffectiveTo is not null) throw new ConflictException("El tramo ya está cerrado; cree uno nuevo en lugar de editarlo.");

            var fromUnit = req.FromUnit ?? (int)current.MinValue;
            int? toUnit = req.ClearToUnit == true ? null : (req.ToUnit ?? (current.MaxValue.HasValue ? (int)current.MaxValue.Value : null));
            var rate = req.Rate ?? current.UnitAmount ?? 0m;
            ValidateTier(fromUnit, toUnit, rate);
            EnsureNoOverlap(new RateTierRules.TierRange(tierId, fromUnit, toUnit), component, excludeTierId: tierId, newFrom);

            NewVersion(current, newFrom, "effectiveFrom");
            await db.SaveChangesAsync(ct2);

            var next = NewTier(component.RateComponentId, fromUnit, toUnit, rate, newFrom);
            db.RateTiers.Add(next);
            await db.SaveGuardedAsync("Ya existe un tramo equivalente en el componente.", ct2);
            return next.RateTierId;
        }, ct);

        return await GetTierDtoAsync(contractPublicId, componentId, newId, ct);
    }

    /// <summary>Cierra un tramo conservando el historial. EffectiveTo vacío = hoy.</summary>
    public async Task<TierDto> CloseTierAsync(Guid contractPublicId, int componentId, int tierId, RateCloseRequest? req, CancellationToken ct)
    {
        var to = req?.EffectiveTo ?? Today();
        EnsureNotPast(to, "effectiveTo");
        var (_, extraId) = await KindIdsAsync(ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var contract = await LoadContractForWriteAsync(contractPublicId, ct2);
            var component = await LoadExtraPieceComponentAsync(contract.ContractId, componentId, extraId, ct2, requireOpen: false);
            var current = component.Tiers.FirstOrDefault(t => t.RateTierId == tierId)
                ?? throw new NotFoundException("Tramo", tierId);
            if (current.EffectiveTo is not null) throw new ConflictException("El tramo ya está cerrado.");
            CloseOn(current, to, "effectiveTo");
            await db.SaveChangesAsync(ct2);
            return current.RateTierId;
        }, ct);
        return await GetTierDtoAsync(contractPublicId, componentId, tierId, ct);
    }

    // ---------------- Cotización ----------------

    /// <summary>
    /// Cotiza una orden contra el contrato vigente del cliente en AsOf (ClientQueries.CurrentContractAsync) más las tarifas
    /// genéricas del tenant (ContractId NULL). Sin contrato vigente: todo sale de las genéricas y despacho/COD = 0.
    /// </summary>
    public async Task<RateQuoteDto> QuoteAsync(RateQuoteRequest req, CancellationToken ct)
    {
        if (req.Lines is null || req.Lines.Count == 0)
            throw new ValidationException("lines", "Indique al menos una línea (servicio, tipo de paquete, piezas).");
        if (req.CodAmount is < 0) throw new ValidationException("codAmount", "El monto COD no puede ser negativo.");
        var asOf = req.AsOf ?? Today();

        var lines = new List<QuoteLine>(req.Lines.Count);
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var l = req.Lines[i];
            var svc = await RequireCodeAsync(LookupDomains.ServiceType, l.ServiceType, $"lines[{i}].serviceType", "El tipo de servicio es obligatorio.", ct);
            var pkg = await RequireCodeAsync(LookupDomains.PackageType, l.PackageType, $"lines[{i}].packageType", "El tipo de paquete es obligatorio.", ct);
            if (l.Pieces < 1) throw new ValidationException($"lines[{i}].pieces", "La cantidad de piezas debe ser al menos 1.");
            lines.Add(new QuoteLine(svc, pkg, l.Pieces));
        }
        // Una línea por (servicio, tipo de paquete): dos líneas del mismo par cobrarían dos bases en vez de base + pieza extra (L1096).
        if (ContractRateResolver.FindDuplicateLine(lines) is int dup)
            throw new ValidationException($"lines[{dup}]", RepeatedLine);

        var client = await db.ResolveClientAsync(req.ClientPublicId, ct);
        var contract = await db.CurrentContractAsync(client.ClientId, asOf, ct);
        var contractId = contract?.ContractId;

        var rows = await db.RateComponents.AsNoTracking().Include(c => c.Tiers)
            .Where(c => c.IsActive && (c.ContractId == null || c.ContractId == contractId))
            .ToListAsync(ct);
        var rateRows = await ToRateRowsAsync(rows.Where(c => EffectiveDated.IsCurrentOn(c, asOf)), asOf, ct);

        var flags = ContractFlags.None;
        string? contractStatus = null;
        if (contract is not null)
        {
            var codType = contract.CodFeeTypeLookupId.HasValue ? await lookups.GetAsync(contract.CodFeeTypeLookupId.Value, ct) : null;
            flags = new ContractFlags(contract.BillPerService, contract.BillExtraPiece, contract.BillDispatchFee, contract.BillCodFee,
                contract.DispatchFee, codType?.InternalCode, contract.CodFeeValue);
            contractStatus = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == contract.StatusCodeId)
                .Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        }

        var result = ContractRateResolver.QuoteOrder(rateRows, flags, lines, req.CodAmount ?? 0m);
        return new RateQuoteDto(contract?.PublicId, contractStatus, asOf,
            result.Lines.Select(l => new QuoteLineDto(l.ServiceType, l.PackageType, l.Pieces, l.BaseRate, l.BaseSource, l.ExtraPieces, l.ExtraSource, l.LineTotal)).ToList(),
            result.DispatchFee, result.CodFee, result.Total, result.RateComponentIds);
    }

    // ---------------- Helpers de carga (siempre bajo el filtro global y a través del contrato) ----------------

    private async Task<Contract> LoadContractForWriteAsync(Guid contractPublicId, CancellationToken ct)
    {
        var contract = await db.ResolveContractAsync(contractPublicId, ct);
        await statuses.EnsureAllowedAsync(EntityTypes.Contract, contract.StatusCodeId, Capabilities.EditContract, ct);
        return contract;
    }

    private async Task<RateComponent> LoadComponentAsync(int contractId, int componentId, bool includeTiers, CancellationToken ct)
    {
        IQueryable<RateComponent> q = db.RateComponents;
        if (includeTiers) q = q.Include(c => c.Tiers);
        return await q.FirstOrDefaultAsync(c => c.RateComponentId == componentId && c.ContractId == contractId && c.IsActive, ct)
            ?? throw new NotFoundException("Componente de tarifa", componentId);
    }

    private async Task<RateComponent> LoadExtraPieceComponentAsync(int contractId, int componentId, int extraId, CancellationToken ct, bool requireOpen = true)
    {
        var component = await LoadComponentAsync(contractId, componentId, includeTiers: true, ct);
        if (component.ComponentTypeLookupId != extraId)
            throw new ValidationException("Los tramos solo aplican a componentes de pieza extra.");
        if (requireOpen && component.EffectiveTo is not null)
            throw new ConflictException("El componente de pieza extra está cerrado; cree uno nuevo para agregar tramos.");
        return component;
    }

    /// <summary>Cierra la fila vigente en 'date' (nueva versión o baja). Una fecha anterior al inicio de la fila es 400.</summary>
    private static void CloseOn(IEffectiveDated row, DateOnly date, string field)
    {
        if (date < row.EffectiveFrom)
            throw new ValidationException(field, $"La fecha ({date:yyyy-MM-dd}) no puede ser anterior al inicio de vigencia de la fila ({row.EffectiveFrom:yyyy-MM-dd}).");
        try { EffectiveDated.Close(row, date); }
        catch (ArgumentException ex) { throw new ValidationException(field, ex.Message); }
    }

    /// <summary>Valida y cierra la fila vigente para abrir la versión nueva desde 'newFrom' (mismo día permitido: fila de longitud cero).</summary>
    private static void NewVersion(IEffectiveDated open, DateOnly newFrom, string field)
    {
        try { EffectiveDated.ValidateNewVersion(open, newFrom); }
        catch (ArgumentException ex) { throw new ValidationException(field, ex.Message); }
        CloseOn(open, newFrom, field);
    }

    private static void EnsureComponentEnabled(Contract contract, string kind)
    {
        if (kind == RateKinds.PerService && !contract.BillPerService) throw new ConflictException(ComponentOffPerService);
        if (kind == RateKinds.ExtraPiece && !contract.BillExtraPiece) throw new ConflictException(ComponentOffExtraPiece);
    }

    /// <summary>
    /// Sin traslape (R27) contra los tramos que siguen vivos desde la fecha de inicio del candidato (EffectiveTo NULL o
    /// posterior a 'from'): un tramo cerrado con fecha futura sigue contando hasta esa fecha; uno ya cerrado, no.
    /// </summary>
    private static void EnsureNoOverlap(RateTierRules.TierRange candidate, RateComponent component, int? excludeTierId, DateOnly from)
    {
        var open = component.Tiers.Where(t => EffectiveDated.IsLiveOnOrAfter(t, from))
            .Select(t => new RateTierRules.TierRange(t.RateTierId, (int)t.MinValue, t.MaxValue.HasValue ? (int)t.MaxValue.Value : null));
        var clash = RateTierRules.FindOverlap(candidate, open, excludeTierId);
        if (clash is not null)
            throw new ValidationException("fromUnit",
                $"El tramo {RateTierRules.Describe(candidate.FromUnit, candidate.ToUnit)} se traslapa con el tramo vigente {RateTierRules.Describe(clash.FromUnit, clash.ToUnit)}.");
    }

    private static void ValidateTier(int fromUnit, int? toUnit, decimal rate)
    {
        if (!RateTierRules.IsValidRange(fromUnit, toUnit))
            throw new ValidationException("fromUnit",
                $"Rango inválido: 'desde' debe ser al menos {RateTierRules.MinFromUnit} (la pieza 1 va en la tarifa por servicio) y 'hasta' debe ser mayor o igual que 'desde' o quedar vacío (abierto).");
        if (rate < 0) throw new ValidationException("rate", "La tarifa por pieza no puede ser negativa.");
    }

    private static RateTier NewTier(int componentId, int fromUnit, int? toUnit, decimal rate, DateOnly effectiveFrom) => new()
    {
        RateComponentId = componentId, MinValue = fromUnit, MaxValue = toUnit, UnitAmount = rate, FlatAmount = null,
        SortOrder = fromUnit, EffectiveFrom = effectiveFrom, EffectiveTo = null,
    };

    private static string NormalizeKind(string? kind)
    {
        var k = kind?.Trim().ToUpperInvariant();
        return k switch
        {
            RateKinds.PerService => RateKinds.PerService,
            RateKinds.ExtraPiece => RateKinds.ExtraPiece,
            _ => throw new ValidationException("kind", "Indique el tipo de componente: PER_SERVICE (tarifa por servicio) o EXTRA_PIECE (pieza extra)."),
        };
    }

    private async Task<int> RequireLookupAsync(string domain, string? code, string field, string requiredMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationException(field, requiredMessage);
        var c = code.Trim().ToUpperInvariant();
        return await lookups.TryGetIdAsync(domain, c, ct)
            ?? throw new ValidationException(field, $"Código '{c}' desconocido en el catálogo {domain}.");
    }

    private async Task<string> RequireCodeAsync(string domain, string? code, string field, string requiredMessage, CancellationToken ct)
    {
        var id = await RequireLookupAsync(domain, code, field, requiredMessage, ct);
        var l = await lookups.GetAsync(id, ct);
        return l?.InternalCode ?? code!.Trim().ToUpperInvariant();
    }

    private async Task<(int BaseId, int ExtraId)> KindIdsAsync(CancellationToken ct)
        => (await lookups.GetIdAsync(LookupDomains.RateComponentType, RateComponentTypes.BaseFreight, ct),
            await lookups.GetIdAsync(LookupDomains.RateComponentType, RateComponentTypes.ExtraPiece, ct));

    private int RequireTenantId() => tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");

    // ---------------- Proyección a DTOs ----------------

    private async Task<RateComponentDto> GetComponentDtoAsync(Guid contractPublicId, int componentId, CancellationToken ct)
    {
        var contract = await db.ResolveContractAsync(contractPublicId, ct);
        var c = await db.RateComponents.AsNoTracking().Include(x => x.Tiers)
            .FirstOrDefaultAsync(x => x.RateComponentId == componentId && x.ContractId == contract.ContractId, ct)
            ?? throw new NotFoundException("Componente de tarifa", componentId);
        var (baseId, _) = await KindIdsAsync(ct);
        var today = Today();
        var (svc, svcLabel, pkg, pkgLabel) = await CodesAsync(c, ct);
        var isBase = c.ComponentTypeLookupId == baseId;
        return new RateComponentDto(c.RateComponentId, isBase ? RateKinds.PerService : RateKinds.ExtraPiece, svc, svcLabel, pkg, pkgLabel,
            isBase ? c.FlatAmount ?? 0m : null, c.EffectiveFrom, c.EffectiveTo, EffectiveDated.IsCurrentOn(c, today),
            isBase ? Array.Empty<TierDto>() : TiersOf(c, today, includeHistory: true));
    }

    private async Task<TierDto> GetTierDtoAsync(Guid contractPublicId, int componentId, int tierId, CancellationToken ct)
    {
        var contract = await db.ResolveContractAsync(contractPublicId, ct);
        var t = await db.RateTiers.AsNoTracking()
            .Where(x => x.RateTierId == tierId && x.RateComponentId == componentId
                        && db.RateComponents.Any(c => c.RateComponentId == componentId && c.ContractId == contract.ContractId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Tramo", tierId);
        return ToTierDto(t, Today());
    }

    private static IReadOnlyList<TierDto> TiersOf(RateComponent c, DateOnly asOf, bool includeHistory)
        => c.Tiers.Where(t => includeHistory || EffectiveDated.IsCurrentOn(t, asOf))
            .OrderBy(t => t.MinValue).ThenBy(t => t.EffectiveFrom).ThenBy(t => t.RateTierId)
            .Select(t => ToTierDto(t, asOf)).ToList();

    private static TierDto ToTierDto(RateTier t, DateOnly asOf)
        => new(t.RateTierId, (int)t.MinValue, t.MaxValue.HasValue ? (int)t.MaxValue.Value : null, t.UnitAmount ?? t.FlatAmount ?? 0m,
            t.EffectiveFrom, t.EffectiveTo, EffectiveDated.IsCurrentOn(t, asOf));

    private async Task<(string Svc, string SvcLabel, string Pkg, string PkgLabel)> CodesAsync(RateComponent c, CancellationToken ct)
    {
        var svc = c.ServiceTypeLookupId.HasValue ? await lookups.GetAsync(c.ServiceTypeLookupId.Value, ct) : null;
        var pkg = c.PackageTypeLookupId.HasValue ? await lookups.GetAsync(c.PackageTypeLookupId.Value, ct) : null;
        return (svc?.InternalCode ?? "", Label(svc), pkg?.InternalCode ?? "", Label(pkg));
    }

    /// <summary>Filas vigentes → modelo puro del resolver (códigos en vez de ids; tramos vigentes en asOf).</summary>
    private async Task<List<RateRow>> ToRateRowsAsync(IEnumerable<RateComponent> rows, DateOnly asOf, CancellationToken ct)
    {
        var (baseId, extraId) = await KindIdsAsync(ct);
        var result = new List<RateRow>();
        foreach (var c in rows)
        {
            string kind;
            if (c.ComponentTypeLookupId == baseId) kind = RateKinds.PerService;
            else if (c.ComponentTypeLookupId == extraId) kind = RateKinds.ExtraPiece;
            else continue;
            var svc = c.ServiceTypeLookupId.HasValue ? (await lookups.GetAsync(c.ServiceTypeLookupId.Value, ct))?.InternalCode : null;
            var pkg = c.PackageTypeLookupId.HasValue ? (await lookups.GetAsync(c.PackageTypeLookupId.Value, ct))?.InternalCode : null;
            var tiers = c.Tiers.Where(t => EffectiveDated.IsCurrentOn(t, asOf))
                .OrderBy(t => t.MinValue)
                .Select(t => new TierRow(t.RateTierId, (int)t.MinValue, t.MaxValue.HasValue ? (int)t.MaxValue.Value : null, t.UnitAmount ?? t.FlatAmount ?? 0m))
                .ToList();
            result.Add(new RateRow(c.RateComponentId, c.ContractId, kind, svc, pkg, c.FlatAmount ?? c.UnitAmount ?? 0m, tiers));
        }
        return result;
    }

    private string Label(LookupCode? l) => l is null ? "" : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
    /// <summary>Principio #8: una versión nueva o un cierre nunca se fechan en el pasado; el historial ya escrito no se reescribe.</summary>
    private static void EnsureNotPast(DateOnly date, string field)
    {
        if (date < Today()) throw new ValidationException(field, PastDateMessage);
    }
    public const string PastDateMessage = "La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.";

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
