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
/// Lote 2 (P3): contratos del cliente. Modelo de facturación con 5 checkboxes (por servicio, pieza extra, despacho,
/// COD, especiales), cargo por despacho (por orden), cargo por COD (fijo o por ciento del monto COD), niveles de
/// servicio (SLA por reemplazo) y estatus vía StatusService (ContractStatusEffect al activar).
/// Reglas transversales:
/// - Desmarcar un componente NO borra lo configurado (R7b): los datos se conservan y simplemente no se cobran.
/// - EndDate y AutoRenew son informativos: no hay vencimiento ni renovación automáticos (decisión de Luis);
///   EXPIRED se alcanza solo por transición manual.
/// - Toda escritura de negocio exige la capacidad EDIT_CONTRACT en el estatus actual (422 en EXPIRED/CANCELLED por
///   defecto; configurable por tenant en /status/capabilities/CONTRACT).
/// - Los niveles de servicio no llevan TenantId: se alcanzan siempre a través del contrato del tenant.
/// </summary>
public sealed class ContractService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService status)
{
    private const string ConcurrencyMessage = "El contrato fue modificado por otro usuario; recargue e intente de nuevo.";

    // ---------------------------------------------------------------- consultas

    public async Task<IReadOnlyList<ContractSummaryDto>> GetListAsync(Guid? clientPublicId, bool includeInactive, CancellationToken ct)
    {
        var query = db.Contracts.AsNoTracking().Include(c => c.Client).Include(c => c.Status).AsQueryable();
        if (clientPublicId is Guid cpid)
        {
            var client = await db.ResolveClientAsync(cpid, ct); // 404 si no es del tenant
            var clientId = client.ClientId;
            query = query.Where(c => c.ClientId == clientId);
        }
        if (!includeInactive) query = query.Where(c => c.IsActive);
        var list = await query.OrderBy(c => c.Client!.Name).ThenByDescending(c => c.StartDate).ThenBy(c => c.ContractId).ToListAsync(ct);
        return list.Select(ToSummary).ToList();
    }

    public async Task<ContractDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var contract = await Detail().FirstOrDefaultAsync(c => c.PublicId == publicId, ct)
                       ?? throw new NotFoundException("Contrato", publicId);
        return await ToDetailAsync(contract, ct);
    }

    // ---------------------------------------------------------------- alta

    public async Task<ContractDetailDto> CreateAsync(ContractCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var title = RequireText(req.Title, "title", "El título del contrato es obligatorio.", ContractRules.TitleMaxLength);
        ContractRules.ValidateDates(req.StartDate, req.EndDate);
        var currencyId = await ResolveCurrencyAsync(req.Currency, ct);
        var billingModelId = await ResolveBillingTriggerAsync(req.BillingTrigger, ct);
        ContractRules.ValidateServiceLevels(req.ServiceLevels);
        var serviceTypeIds = await ResolveServiceTypesAsync(req.ServiceLevels, ct);
        var explicitNumber = OptionalText(req.ContractNumber, "contractNumber", ContractRules.ContractNumberMaxLength);

        // Todo lo que se carga se carga dentro del delegado (la estrategia de reintentos limpia el tracker al reintentar).
        var contractId = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(req.ClientPublicId, ct2);
            ClientQueries.EnsureClientActive(client); // Lote 3 (ajuste A): cliente dado de baja ⇒ 409, sin contratos nuevos

            string number;
            if (explicitNumber is not null)
            {
                if (await db.Contracts.AnyAsync(c => c.ContractNumber == explicitNumber, ct2))
                    throw new ConflictException($"Ya existe un contrato con el número '{explicitNumber}'.");
                number = explicitNumber;
            }
            else
            {
                var prefix = client.Code + "-C";
                var existing = await db.Contracts.Where(c => c.ContractNumber.StartsWith(prefix)).Select(c => c.ContractNumber).ToListAsync(ct2);
                number = ContractRules.NextContractNumber(client.Code, existing);
            }

            var initial = await status.GetInitialAsync(StatusDomains.ContractStatus, ct2);
            var contract = new Contract
            {
                TenantId = tenantId,
                ClientId = client.ClientId,
                ContractNumber = number,
                Title = title,
                StartDate = req.StartDate,
                EndDate = req.EndDate,
                AutoRenew = req.AutoRenew,
                CurrencyLookupId = currencyId,
                BillingModelLookupId = billingModelId,
                Notes = OptionalText(req.Notes, "notes", int.MaxValue),
                StatusCodeId = initial.StatusCodeId,
                // RC4: todo contrato nuevo cobra "por servicio"; los otros cuatro componentes nacen apagados.
                BillPerService = true,
                BillExtraPiece = false,
                BillDispatchFee = false,
                BillCodFee = false,
                BillSpecialServices = false,
                IsActive = true,
            };
            db.Contracts.Add(contract);
            await db.SaveGuardedAsync($"Ya existe un contrato con el número '{number}'.", ct2);

            // Historial de estatus desde "nada" hasta la etapa inicial (DRAFT).
            var to = await status.TransitionAsync(StatusDomains.ContractStatus, EntityTypes.Contract, contract.ContractId, null, initial.InternalCode, null, ct2);
            contract.StatusCodeId = to.StatusCodeId;

            foreach (var (item, serviceTypeId) in serviceTypeIds)
                db.ContractServiceLevels.Add(NewServiceLevel(contract.ContractId, serviceTypeId, item));

            await db.SaveGuardedAsync("Solo puede haber un nivel de servicio activo por tipo de servicio.", ct2);
            return contract.ContractId;
        }, ct);

        return await GetByIdAsync(contractId, ct);
    }

    // ---------------------------------------------------------------- edición

    public async Task<ContractDetailDto> UpdateAsync(Guid publicId, ContractPatchRequest req, CancellationToken ct)
    {
        var contract = await LoadForWriteAsync(publicId, ct);
        db.ApplyRowVersion(contract, req.RowVersion);

        if (req.ClearEndDate == true && req.EndDate.HasValue)
            throw new ValidationException("endDate", "Indique una fecha fin o márquela para borrar, no ambas.");
        // 'Cliente desde' (StartDate) es editable inline (documento); se valida la vigencia sobre el valor efectivo.
        var startDate = req.StartDate ?? contract.StartDate;
        var endDate = req.ClearEndDate == true ? null : req.EndDate ?? contract.EndDate;
        ContractRules.ValidateDates(startDate, endDate, req.StartDate.HasValue && !req.EndDate.HasValue ? "startDate" : "endDate");

        if (req.Title is not null) contract.Title = RequireText(req.Title, "title", "El título del contrato es obligatorio.", ContractRules.TitleMaxLength);
        contract.StartDate = startDate;
        contract.EndDate = endDate;
        if (req.AutoRenew.HasValue) contract.AutoRenew = req.AutoRenew.Value;
        if (req.Currency is not null) contract.CurrencyLookupId = await ResolveCurrencyAsync(req.Currency, ct);
        if (req.BillingTrigger is not null) contract.BillingModelLookupId = await ResolveBillingTriggerAsync(req.BillingTrigger, ct);
        if (req.Notes is not null) contract.Notes = OptionalText(req.Notes, "notes", int.MaxValue);

        await db.SaveGuardedAsync(ConcurrencyMessage, ct);
        return await GetByIdAsync(contract.ContractId, ct);
    }

    /// <summary>Solo banderas. Desmarcar no borra DispatchFee/CodFee/tarifas/servicios especiales (R7b).</summary>
    public async Task<ContractDetailDto> SetBillingModelAsync(Guid publicId, BillingModelRequest req, CancellationToken ct)
    {
        var contract = await LoadForWriteAsync(publicId, ct);
        if (req.BillPerService.HasValue) contract.BillPerService = req.BillPerService.Value;
        if (req.BillExtraPiece.HasValue) contract.BillExtraPiece = req.BillExtraPiece.Value;
        if (req.BillDispatchFee.HasValue) contract.BillDispatchFee = req.BillDispatchFee.Value;
        if (req.BillCodFee.HasValue) contract.BillCodFee = req.BillCodFee.Value;
        if (req.BillSpecialServices.HasValue) contract.BillSpecialServices = req.BillSpecialServices.Value;
        await db.SaveGuardedAsync(ConcurrencyMessage, ct);
        return await GetByIdAsync(contract.ContractId, ct);
    }

    /// <summary>Cargo por despacho: monto por orden (bitácora: "Cargo por despacho: por orden"). No toca el checkbox.</summary>
    public async Task<ContractDetailDto> SetDispatchFeeAsync(Guid publicId, DispatchFeeRequest req, CancellationToken ct)
    {
        if (req.Amount < 0) throw new ValidationException("amount", "El cargo por despacho no puede ser negativo.");
        var contract = await LoadForWriteAsync(publicId, ct);
        contract.DispatchFee = req.Amount;
        await db.SaveGuardedAsync(ConcurrencyMessage, ct);
        return await GetByIdAsync(contract.ContractId, ct);
    }

    /// <summary>Cargo por COD: FIXED (monto por orden) o PERCENT (0..100 sobre el monto COD). "Ninguno" = checkbox BillCodFee apagado.</summary>
    public async Task<ContractDetailDto> SetCodFeeAsync(Guid publicId, CodFeeRequest req, CancellationToken ct)
    {
        ContractRules.ValidateCodFee(req.Type, req.Value);
        var typeCode = req.Type.Trim().ToUpperInvariant();
        var typeId = await lookups.GetIdAsync(LookupDomains.PricingType, typeCode, ct);
        var contract = await LoadForWriteAsync(publicId, ct);
        contract.CodFeeTypeLookupId = typeId;
        contract.CodFeeValue = req.Value;
        await db.SaveGuardedAsync(ConcurrencyMessage, ct);
        return await GetByIdAsync(contract.ContractId, ct);
    }

    /// <summary>Reemplazo completo: los ausentes pasan a IsActive=0; un solo nivel activo por tipo de servicio.</summary>
    public async Task<ContractDetailDto> SetServiceLevelsAsync(Guid publicId, IList<ServiceLevelUpsert>? items, CancellationToken ct)
    {
        items ??= new List<ServiceLevelUpsert>();
        ContractRules.ValidateServiceLevels(items);
        var resolved = await ResolveServiceTypesAsync(items, ct);

        var contract = await LoadForWriteAsync(publicId, ct);
        // Las hijas no llevan TenantId: se cargan por el ContractId del contrato ya resuelto bajo el filtro global.
        var contractId = contract.ContractId;
        var rows = await db.ContractServiceLevels.Where(l => l.ContractId == contractId).ToListAsync(ct);

        var keep = new HashSet<int>();
        foreach (var (item, serviceTypeId) in resolved)
        {
            var row = rows.Where(r => r.ServiceTypeLookupId == serviceTypeId).OrderByDescending(r => r.IsActive).ThenByDescending(r => r.ServiceLevelId).FirstOrDefault();
            if (row is null)
            {
                row = NewServiceLevel(contractId, serviceTypeId, item);
                db.ContractServiceLevels.Add(row);
                rows.Add(row);
            }
            else
            {
                row.IsActive = true;
                row.MaxTransitHours = item.MaxTransitHours;
                row.PickupWindowMin = item.PickupWindowMin;
                row.OnTimeTargetPct = item.OnTimeTargetPct;
                row.PenaltyAmount = item.PenaltyAmount;
            }
            keep.Add(serviceTypeId);
        }
        foreach (var row in rows.Where(r => r.IsActive && !keep.Contains(r.ServiceTypeLookupId)))
            row.IsActive = false;

        await db.SaveGuardedAsync("Solo puede haber un nivel de servicio activo por tipo de servicio.", ct);
        return await GetByIdAsync(contractId, ct);
    }

    // ---------------------------------------------------------------- estatus

    /// <summary>Transición vía StatusService (valida etapa, escribe historial, dispara ContractStatusEffect) y asigna el estatus.</summary>
    public async Task<ContractDetailDto> TransitionStatusAsync(Guid publicId, StatusChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ToCode)) throw new ValidationException("toCode", "Indique el estatus destino.");
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.PublicId == publicId, ct)
                       ?? throw new NotFoundException("Contrato", publicId);
        var to = await status.TransitionAsync(StatusDomains.ContractStatus, EntityTypes.Contract, contract.ContractId, contract.StatusCodeId, req.ToCode.Trim(), req.Comment, ct);
        contract.StatusCodeId = to.StatusCodeId;
        await db.SaveGuardedAsync(ConcurrencyMessage, ct);
        return await GetByIdAsync(contract.ContractId, ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Carga tracked para escribir y aplica la guarda de estatus EDIT_CONTRACT (422 si el estatus no lo permite).</summary>
    private async Task<Contract> LoadForWriteAsync(Guid publicId, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.PublicId == publicId, ct)
                       ?? throw new NotFoundException("Contrato", publicId);
        await status.EnsureAllowedAsync(EntityTypes.Contract, contract.StatusCodeId, Capabilities.EditContract, ct);
        return contract;
    }

    private async Task<int?> ResolveCurrencyAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return await lookups.GetIdAsync(LookupDomains.Currency, code.Trim().ToUpperInvariant(), ct);
    }

    private async Task<int?> ResolveBillingTriggerAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return await lookups.GetIdAsync(LookupDomains.BillingModel, code.Trim().ToUpperInvariant(), ct);
    }

    private async Task<List<(ServiceLevelUpsert Item, int ServiceTypeId)>> ResolveServiceTypesAsync(IEnumerable<ServiceLevelUpsert>? items, CancellationToken ct)
    {
        var list = new List<(ServiceLevelUpsert, int)>();
        if (items is null) return list;
        foreach (var it in items)
        {
            var code = it.ServiceType.Trim().ToUpperInvariant();
            var id = await lookups.TryGetIdAsync(LookupDomains.ServiceType, code, ct)
                     ?? throw new ValidationException("serviceLevels", $"Tipo de servicio desconocido: '{it.ServiceType}'.");
            list.Add((it, id));
        }
        return list;
    }

    private static ContractServiceLevel NewServiceLevel(int contractId, int serviceTypeId, ServiceLevelUpsert item) => new()
    {
        ContractId = contractId,
        ServiceTypeLookupId = serviceTypeId,
        MaxTransitHours = item.MaxTransitHours,
        PickupWindowMin = item.PickupWindowMin,
        OnTimeTargetPct = item.OnTimeTargetPct,
        PenaltyAmount = item.PenaltyAmount,
        IsActive = true,
    };

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

    private IQueryable<Contract> Detail()
        => db.Contracts.AsNoTracking().Include(c => c.Client).Include(c => c.Status)
            .Include(c => c.Currency).Include(c => c.BillingModel).Include(c => c.CodFeeType);

    private async Task<ContractDetailDto> GetByIdAsync(int id, CancellationToken ct)
        => await ToDetailAsync(await Detail().FirstAsync(c => c.ContractId == id, ct), ct);

    private string Summary(Contract c)
        => BillingModelSummary.Build(c.BillPerService, c.BillExtraPiece, c.BillDispatchFee, c.BillCodFee, c.BillSpecialServices, tenant.Lang);

    private ContractSummaryDto ToSummary(Contract c) => new(
        c.ContractId, c.PublicId, c.Client!.PublicId, c.Client.Name, c.ContractNumber, c.Title, c.StartDate, c.EndDate,
        c.Status?.InternalCode ?? "", MultilingualText.Resolve(c.Status?.LabelJson, tenant.Lang),
        c.AutoRenew, Summary(c), c.IsActive);

    private async Task<ContractDetailDto> ToDetailAsync(Contract c, CancellationToken ct)
    {
        var levels = await db.ContractServiceLevels.AsNoTracking()
            .Where(l => l.ContractId == c.ContractId && l.IsActive).OrderBy(l => l.ServiceLevelId).ToListAsync(ct);
        var levelDtos = new List<ServiceLevelDto>(levels.Count);
        foreach (var l in levels)
        {
            var st = await lookups.GetAsync(l.ServiceTypeLookupId, ct);
            levelDtos.Add(new ServiceLevelDto(l.ServiceLevelId, st?.InternalCode ?? "", MultilingualText.Resolve(st?.LabelJson, tenant.Lang),
                l.MaxTransitHours, l.PickupWindowMin, l.OnTimeTargetPct, l.PenaltyAmount));
        }
        var canEdit = await status.IsAllowedAsync(EntityTypes.Contract, c.StatusCodeId, Capabilities.EditContract, ct);
        var codFee = c.CodFeeTypeLookupId is null ? null : new CodFeeDto(c.CodFeeType?.InternalCode ?? "", c.CodFeeValue ?? 0m);

        return new ContractDetailDto(
            c.ContractId, c.PublicId, c.Client!.PublicId, c.Client.Name, c.ContractNumber, c.Title, c.StartDate, c.EndDate, c.AutoRenew,
            c.Status?.InternalCode ?? "", MultilingualText.Resolve(c.Status?.LabelJson, tenant.Lang),
            c.Currency?.InternalCode, c.BillingModel?.InternalCode, c.Notes,
            new ContractBillingModelDto(c.BillPerService, c.BillExtraPiece, c.BillDispatchFee, c.BillCodFee, c.BillSpecialServices, Summary(c)),
            c.DispatchFee, codFee, levelDtos, canEdit, c.IsActive,
            Convert.ToBase64String(c.RowVersion ?? Array.Empty<byte>()));
    }
}
