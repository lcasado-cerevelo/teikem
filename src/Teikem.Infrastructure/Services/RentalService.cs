using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 27 (Rentas R1, plan 4.5) — rentas de equipos propios con número de serie hasta el despacho, con extensiones y cancelación.
/// - Alta en Borrador (REN-#####): cliente activo, localidad DEL cliente, contacto opcional del cliente, almacén de origen (o el único
///   activo), fechas de inicio y recogido (recogido ≥ inicio), contrato (número y fecha, D6), transporte estimado (solo dato) y
///   equipos opcionales. Cada equipo = una serie AVAILABLE en una posición recolectable del almacén de origen, de un producto propio
///   con seguimiento SERIAL, que no esté en otra renta abierta; tarifa opcional (D3: solo se guarda).
/// - Borrador y Programada se editan (datos, equipos, tarifas); despachada o devuelta → 422 'La renta {n} ya fue despachada; no se
///   puede modificar.'. Programar reserva las series (ledger.ReserveAsync: el disponible baja y la serie queda RESERVED).
/// - Despachar: por el ledger, una TRANSFER por equipo desde su posición hasta la posición EN-RENTA del almacén (RentalBinResolver),
///   consumiendo lo reservado y reservando en el destino; la serie queda ON_RENT con la referencia RENTAL. Sigue en mano (D1).
/// - Cancelar (solo Borrador o Programada; rental.manage): libera la reserva y desactiva los equipos.
/// - Extender (rental.extend, D4): bitácora RentalExtension (fecha anterior y nueva, motivo) y, si cambia, versión nueva de la tarifa
///   del equipo desde el día siguiente al recogido anterior. Sin aprobación de un segundo usuario.
/// - Todo dentro de RunInTransactionAsync con el orden de bloqueo del lote: Rental (U) → Warehouse (despacho) → saldos → series →
///   NumberSequence (alta). Los estatus solo por StatusService.TransitionAsync (RentalStatusEffect sella fechas).
/// El TenantId sale del principal; la renta se expone por PublicId y sus equipos por id SIEMPRE bajo su renta filtrada.
/// </summary>
public sealed class RentalService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    InventoryLedger ledger,
    RentalBinResolver rentalBins,
    ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    /// <summary>Mismo texto que la concurrencia optimista de la plataforma (rowVersion distinto o choque de escritura).</summary>
    public const string ConcurrencyMessage = DbExtensions.ConcurrencyMessage;
    public const string NumberTakenMessage = "Ya existe una renta con ese número; intente de nuevo.";
    public const string SerialTakenConcurrently = "Una de las series se acaba de agregar a otra renta; recargue e intente de nuevo.";
    public const string DueDaysNegative = "Los días deben ser 0 o más.";
    public const string NewPickupRequired = "Indique la nueva fecha de recogido.";
    public const string LineRequired = "Indique el equipo (lineId) de la tarifa.";

    /// <summary>Campos que el PATCH rechaza (llegan a Extra por no estar en el contrato).</summary>
    private static readonly string[] ImmutableOnPatch = { "number", "status", "statusCode", "originalPickupDate", "dispatchedAtUtc", "closedAtUtc", "deliveryShipmentId", "invoiceId" };
    private static readonly string[] ClientKeys = { "clientPublicId", "clientId", "client" };

    private sealed record RatePlan(int FrequencyId, decimal Amount, int CurrencyId);
    private sealed record LinePlan(int ProductId, string Sku, int SerialId, string SerialNumber, int? LotId, int BinId, RatePlan? Rate);
    private sealed record StatusInfo(string Code, string Label, string? Color);

    // ================================================================ lista y ficha

    public async Task<RentalPageDto> ListAsync(RentalQuery? q, CancellationToken ct)
    {
        q ??= new RentalQuery();
        var skip = Math.Max(0, q.Skip);
        var take = q.Take <= 0 ? 100 : Math.Min(q.Take, RentalRules.MaxPageSize);
        if (q.DueWithinDays is < 0) throw new ValidationException("dueWithinDays", DueDaysNegative);
        var today = _clock.Today;

        var query = db.Rentals.AsNoTracking();
        if (q.Status is { Length: > 0 })
        {
            var codes = q.Status.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
            var ids = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.RentalStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(r => ids.Contains(r.StatusCodeId));
        }
        if (q.ClientPublicId is Guid clientPublicId)
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct);
            query = query.Where(r => r.ClientId == client.ClientId);
        }
        var due = q.DueWithinDays is int days;
        if (due || q.Overdue)
        {
            var openIds = await db.StatusIdsAsync(StatusDomains.RentalStatus, RentalStatuses.OpenCodes, ct);
            query = query.Where(r => openIds.Contains(r.StatusCodeId));
            if (due && q.Overdue)
            {
                var limit = today.AddDays(q.DueWithinDays!.Value);
                query = query.Where(r => r.PickupDate <= limit);
            }
            else if (due)
            {
                var limit = today.AddDays(q.DueWithinDays!.Value);
                query = query.Where(r => r.PickupDate >= today && r.PickupDate <= limit);
            }
            else query = query.Where(r => r.PickupDate < today);
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(r => r.Number.Contains(s)
                || (r.ContractNumber != null && r.ContractNumber.Contains(s))
                || db.Clients.Any(c => c.ClientId == r.ClientId && c.Name.Contains(s))
                || db.Locations.Any(l => l.LocationId == r.LocationId && l.Name.Contains(s))
                || (from l in db.RentalLines
                    join se in db.InventorySerials on l.SerialId equals se.SerialId
                    where l.RentalId == r.RentalId && l.IsActive && se.SerialNumber.Contains(s)
                    select l.RentalLineId).Any());
        }

        var total = await query.CountAsync(ct);
        var ordered = due || q.Overdue
            ? query.OrderBy(r => r.PickupDate).ThenBy(r => r.RentalId)
            : query.OrderByDescending(r => r.RentalId);
        var page = await ordered.Skip(skip).Take(take).ToListAsync(ct);
        var items = await ToListItemsAsync(page, ct);
        return new RentalPageDto(total, skip, take, items);
    }

    public async Task<RentalDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var rental = await db.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.PublicId == publicId, ct)
                     ?? throw new NotFoundException(RentalRules.NotFound, feminine: true);
        return await ToDtoAsync(rental, ct);
    }

    private async Task<RentalDto> GetByIdAsync(int rentalId, CancellationToken ct)
    {
        var rental = await db.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.RentalId == rentalId, ct)
                     ?? throw new NotFoundException(RentalRules.NotFound, feminine: true);
        return await ToDtoAsync(rental, ct);
    }

    /// <summary>Bitácora de extensiones de la renta (más recientes al final) con las tarifas que abrió cada una.</summary>
    public async Task<IReadOnlyList<RentalExtensionDto>> ListExtensionsAsync(Guid publicId, CancellationToken ct)
    {
        var rentalId = await ResolveIdAsync(publicId, ct);
        var rows = await db.RentalExtensions.AsNoTracking().Where(e => e.RentalId == rentalId)
            .OrderBy(e => e.RentalExtensionId).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<RentalExtensionDto>();
        var extIds = rows.Select(e => (int?)e.RentalExtensionId).ToList();
        var rates = await (from r in db.RentalLineRates.AsNoTracking()
                           join l in db.RentalLines.AsNoTracking() on r.RentalLineId equals l.RentalLineId
                           join s in db.InventorySerials.AsNoTracking() on l.SerialId equals s.SerialId
                           where l.RentalId == rentalId && extIds.Contains(r.RentalExtensionId)
                           select new { Rate = r, s.SerialNumber }).ToListAsync(ct);
        var userIds = rows.Where(r => r.CreatedBy.HasValue).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        var result = new List<RentalExtensionDto>();
        foreach (var e in rows)
        {
            var mine = new List<RentalExtensionRateDto>();
            foreach (var r in rates.Where(x => x.Rate.RentalExtensionId == e.RentalExtensionId).OrderBy(x => x.Rate.RentalLineId))
                mine.Add(new RentalExtensionRateDto(r.Rate.RentalLineId, r.SerialNumber, await RateDtoAsync(r.Rate, ct)));
            result.Add(new RentalExtensionDto(e.RentalExtensionId, e.PreviousPickupDate, e.NewPickupDate,
                e.NewPickupDate.DayNumber - e.PreviousPickupDate.DayNumber, e.Reason, e.CreatedAtUtc, e.CreatedBy,
                e.CreatedBy is int uid ? users.GetValueOrDefault(uid) : null, mine));
        }
        return result;
    }

    // ================================================================ alta

    public async Task<RentalDto> CreateAsync(RentalCreateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (req.ClientPublicId is null) errors["clientPublicId"] = new[] { RentalRules.ClientRequired };
        if (req.LocationPublicId is null) errors["locationPublicId"] = new[] { RentalRules.LocationRequired };
        if (req.StartDate is null) errors["startDate"] = new[] { RentalRules.StartDateRequired };
        if (req.PickupDate is null) errors["pickupDate"] = new[] { RentalRules.PickupDateRequired };
        if (req.StartDate is DateOnly s0 && req.PickupDate is DateOnly p0 && RentalRules.ValidateDates(s0, p0) is { } datesError)
            errors["pickupDate"] = new[] { datesError };
        var (contract, contractError) = RentalRules.NormalizeText(req.ContractNumber, RentalRules.MaxContractNumberLength, RentalRules.ContractNumberTooLong);
        if (contractError is not null) errors["contractNumber"] = new[] { contractError };
        var (notes, notesError) = RentalRules.NormalizeText(req.Notes, RentalRules.MaxNotesLength, RentalRules.NotesTooLong);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        if (req.EstimatedDeliveryCost is < 0m) errors["estimatedDeliveryCost"] = new[] { RentalRules.NegativeDeliveryCost };
        int? transportCurrencyId = null;
        if (!string.IsNullOrWhiteSpace(req.TransportCurrency))
        {
            transportCurrencyId = await lookups.TryGetIdAsync(LookupDomains.Currency, req.TransportCurrency.Trim().ToUpperInvariant(), ct);
            if (transportCurrencyId is null) errors["transportCurrency"] = new[] { RentalRules.UnknownCurrency(req.TransportCurrency.Trim()) };
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var client = ClientQueries.EnsureClientActive(await db.ResolveClientAsync(req.ClientPublicId!.Value, ct));
        var location = await ResolveLocationAsync(req.LocationPublicId!.Value, client.ClientId, ct);
        var contactId = req.ClientContactId is int cid ? await ResolveContactAsync(cid, client.ClientId, ct) : (int?)null;
        var warehouse = await db.ResolveWarehouseOrDefaultAsync(req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(InventoryRules.WarehouseInactiveMessage(warehouse.Code));
        var plans = await ValidateLinesAsync(req.Lines, warehouse.WarehouseId, warehouse.Code, ct);
        if (plans.Count > RentalRules.MaxLines) throw new ValidationException("lines", RentalRules.TooManyLines);

        await numbers.EnsureAsync(NumberKinds.Rental, null, ct);
        var rentalId = await db.RunInTransactionAsync(async ct2 =>
        {
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            await EnsureSerialsFreeAsync(plans, ct2);   // antes de escribir nada (409 'La serie {s} ya está en la renta {n}.')
            var initial = await statuses.GetInitialAsync(StatusDomains.RentalStatus, ct2);
            var seq = await numbers.NextAsync(NumberKinds.Rental, null, ct2);
            var rental = new Rental
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Number = WmsNumbering.Format(NumberKinds.Rental, seq), ClientId = client.ClientId,
                LocationId = location.LocationId, ClientContactId = contactId, WarehouseId = warehouse.WarehouseId,
                StartDate = req.StartDate!.Value, PickupDate = req.PickupDate!.Value, OriginalPickupDate = req.PickupDate!.Value,
                ContractNumber = contract, ContractSignedOn = req.ContractSignedOn, EstimatedDeliveryCost = req.EstimatedDeliveryCost,
                TransportCurrencyLookupId = transportCurrencyId, StatusCodeId = initial.StatusCodeId, Notes = notes,
            };
            db.Rentals.Add(rental);
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);

            await AddLinesAsync(rental, plans, ct2);
            var born = await statuses.TransitionAsync(StatusDomains.RentalStatus, EntityTypes.Rental, rental.RentalId, null, initial.InternalCode, null, ct2);
            rental.StatusCodeId = born.StatusCodeId;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
            return rental.RentalId;
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    // ================================================================ edición

    /// <summary>
    /// PATCH (Borrador o Programada): localidad (del mismo cliente), contacto, almacén (sin equipos), fechas (sin extensiones; la
    /// fecha de recogido pactada sigue a la vigente y las tarifas iniciales siguen al inicio), contrato, transporte y notas.
    /// </summary>
    public async Task<RentalDto> UpdateAsync(Guid publicId, RentalPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Extra is not null)
            foreach (var key in req.Extra.Keys)
            {
                if (ClientKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) throw new ValidationException(key, RentalRules.ClientImmutable);
                var hit = ImmutableOnPatch.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) throw new ValidationException(hit, RentalRules.ImmutableField(hit));
            }
        var errors = new Dictionary<string, string[]>();
        var (contract, contractError) = RentalRules.NormalizeText(req.ContractNumber, RentalRules.MaxContractNumberLength, RentalRules.ContractNumberTooLong);
        if (contractError is not null) errors["contractNumber"] = new[] { contractError };
        var (notes, notesError) = RentalRules.NormalizeText(req.Notes, RentalRules.MaxNotesLength, RentalRules.NotesTooLong);
        if (notesError is not null) errors["notes"] = new[] { notesError };
        if (req.EstimatedDeliveryCost is < 0m) errors["estimatedDeliveryCost"] = new[] { RentalRules.NegativeDeliveryCost };
        int? transportCurrencyId = null;
        if (!string.IsNullOrWhiteSpace(req.TransportCurrency))
        {
            transportCurrencyId = await lookups.TryGetIdAsync(LookupDomains.Currency, req.TransportCurrency.Trim().ToUpperInvariant(), ct);
            if (transportCurrencyId is null) errors["transportCurrency"] = new[] { RentalRules.UnknownCurrency(req.TransportCurrency.Trim()) };
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        var rentalId = await ResolveIdAsync(publicId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req.RowVersion);
            await EnsureEditableAsync(rental, ct2);

            if (req.LocationPublicId is Guid locationPublicId)
                rental.LocationId = (await ResolveLocationAsync(locationPublicId, rental.ClientId, ct2)).LocationId;
            if (req.ClearClientContact == true) rental.ClientContactId = null;
            else if (req.ClientContactId is int contactId) rental.ClientContactId = await ResolveContactAsync(contactId, rental.ClientId, ct2);

            if (req.WarehousePublicId is Guid warehousePublicId)
            {
                var wh = await db.ResolveWarehouseAsync(warehousePublicId, false, ct2);
                if (wh.WarehouseId != rental.WarehouseId)
                {
                    if (await db.RentalLines.AnyAsync(l => l.RentalId == rental.RentalId && l.IsActive, ct2))
                        throw new ConflictException(RentalRules.WarehouseLockedByLines);
                    if (!wh.IsActive) throw new StatusRuleException(InventoryRules.WarehouseInactiveMessage(wh.Code));
                    rental.WarehouseId = wh.WarehouseId;
                }
            }

            var newStart = req.StartDate ?? rental.StartDate;
            var newPickup = req.PickupDate ?? rental.PickupDate;
            if (newStart != rental.StartDate || newPickup != rental.PickupDate)
            {
                if (await db.RentalExtensions.AnyAsync(e => e.RentalId == rental.RentalId, ct2))
                    throw new StatusRuleException(RentalRules.DatesLockedByExtensions);
                if (RentalRules.ValidateDates(newStart, newPickup) is { } datesError) throw new ValidationException("pickupDate", datesError);
                if (newStart != rental.StartDate)
                {
                    // Las tarifas iniciales (sin extensiones) empiezan en la fecha de inicio: la siguen.
                    var oldStart = rental.StartDate;
                    var lineIds = await db.RentalLines.Where(l => l.RentalId == rental.RentalId).Select(l => l.RentalLineId).ToListAsync(ct2);
                    var initialRates = await db.RentalLineRates.Where(r => lineIds.Contains(r.RentalLineId) && r.EffectiveFrom == oldStart).ToListAsync(ct2);
                    foreach (var r in initialRates) r.EffectiveFrom = newStart;
                    rental.StartDate = newStart;
                }
                rental.PickupDate = newPickup;
                rental.OriginalPickupDate = newPickup;
            }

            if (req.ContractNumber is not null) rental.ContractNumber = contract;
            if (req.ClearContractSignedOn == true) rental.ContractSignedOn = null;
            else if (req.ContractSignedOn is DateOnly signed) rental.ContractSignedOn = signed;
            if (req.ClearEstimatedDeliveryCost == true) rental.EstimatedDeliveryCost = null;
            else if (req.EstimatedDeliveryCost is decimal cost) rental.EstimatedDeliveryCost = cost;
            if (transportCurrencyId is int tc) rental.TransportCurrencyLookupId = tc;
            if (req.Notes is not null) rental.Notes = notes;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    // ================================================================ equipos

    /// <summary>Agrega equipos (Borrador o Programada). En Programada reserva las series agregadas en la misma transacción.</summary>
    public async Task<RentalDto> AddLinesAsync(Guid publicId, RentalLinesAddRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var rentalId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            var code = await EnsureEditableAsync(rental, ct2);
            var warehouseCode = await db.Warehouses.AsNoTracking().Where(w => w.WarehouseId == rental.WarehouseId).Select(w => w.Code).FirstAsync(ct2);
            var plans = await ValidateLinesAsync(new[] { req }, rental.WarehouseId, warehouseCode, ct2, indexed: false);
            var current = await db.RentalLines.CountAsync(l => l.RentalId == rental.RentalId && l.IsActive, ct2);
            if (current + plans.Count > RentalRules.MaxLines) throw new ValidationException("serialNumbers", RentalRules.TooManyLines);
            await EnsureSerialsFreeAsync(plans, ct2);
            await AddLinesAsync(rental, plans, ct2);
            rental.UpdatedAtUtc = DateTime.UtcNow;   // la ficha cambia: nueva RowVersion
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
            if (code == RentalStatuses.Scheduled)
                await ledger.ReserveAsync(plans.Select(p => new StockReservation(p.ProductId, rental.WarehouseId, p.BinId, p.LotId, 1m,
                    new[] { p.SerialNumber })).ToList(), ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    /// <summary>Quita un equipo (Borrador o Programada): en Programada libera su reserva; la línea queda inactiva (no se borra).</summary>
    public async Task<RentalDto> RemoveLineAsync(Guid publicId, int lineId, CancellationToken ct)
    {
        var rentalId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            var code = await EnsureEditableAsync(rental, ct2);
            var line = await ActiveLineAsync(rental.RentalId, lineId, ct2);
            if (code == RentalStatuses.Scheduled)
            {
                var serial = await SerialNumberAsync(line.SerialId, ct2);
                await ledger.ReleaseAsync(new[] { new StockReservation(line.ProductId, rental.WarehouseId, line.FromBinId, line.LotId, 1m, new[] { serial }) }, ct2);
            }
            line.IsActive = false;
            rental.UpdatedAtUtc = DateTime.UtcNow;   // la ficha cambia: nueva RowVersion
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    /// <summary>
    /// Tarifa vigente de un equipo (antes del despacho): edita en su lugar la versión vigente o crea la primera desde la fecha de
    /// inicio. Después del despacho la tarifa cambia solo con una extensión.
    /// </summary>
    public async Task<RentalDto> SetLineRateAsync(Guid publicId, int lineId, RentalLineRateRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var (plan, error, field) = await NormalizeRateAsync(req.Frequency, req.Amount, req.Currency, ct);
        if (error is not null) throw new ValidationException(field!, error);
        var rentalId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req.RowVersion);
            await EnsureEditableAsync(rental, ct2);
            var line = await ActiveLineAsync(rental.RentalId, lineId, ct2);
            var currentRate = await db.RentalLineRates.Where(r => r.RentalLineId == line.RentalLineId && r.EffectiveTo == null)
                .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.RentalLineRateId).FirstOrDefaultAsync(ct2);
            if (currentRate is null)
                db.RentalLineRates.Add(NewRate(line.RentalLineId, plan!, rental.StartDate, null));
            else
            {
                currentRate.BillingFrequencyLookupId = plan!.FrequencyId;
                currentRate.RateAmount = plan.Amount;
                currentRate.CurrencyLookupId = plan.CurrencyId;
            }
            // Toca el encabezado para que su RowVersion cambie con la tarifa (concurrencia optimista de la ficha).
            rental.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    // ================================================================ estatus

    /// <summary>Borrador → Programada: reserva cada serie en su posición actual del almacén de origen (el disponible baja).</summary>
    public async Task<RentalDto> ScheduleAsync(Guid publicId, RentalStatusRequest? req, CancellationToken ct)
    {
        var comment = PurchasingSupport.Comment(req?.Comment);
        var rentalId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req?.RowVersion);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, rental.StatusCodeId, ct2);
            if (RentalRules.ScheduleError(code, rental.Number) is { } error) throw new StatusRuleException(error);
            await EnsureClientStillActiveAsync(rental.ClientId, ct2);
            var lines = await db.RentalLines.Where(l => l.RentalId == rental.RentalId && l.IsActive).OrderBy(l => l.RentalLineId).ToListAsync(ct2);
            if (lines.Count == 0) throw new StatusRuleException(RentalRules.NoLines);

            // La serie pudo moverse desde que se agregó: se reserva donde está HOY (en una posición recolectable del almacén).
            var warehouseCode = await db.Warehouses.AsNoTracking().Where(w => w.WarehouseId == rental.WarehouseId).Select(w => w.Code).FirstAsync(ct2);
            var serialIds = lines.Select(l => l.SerialId).ToList();
            var serials = await db.InventorySerials.AsNoTracking().Where(s => serialIds.Contains(s.SerialId)).ToDictionaryAsync(s => s.SerialId, ct2);
            var pickable = await PickableBinsAsync(serials.Values.Where(s => s.CurrentBinId.HasValue).Select(s => s.CurrentBinId!.Value), ct2);
            var reservations = new List<StockReservation>();
            foreach (var line in lines)
            {
                var serial = serials[line.SerialId];
                if (serial.CurrentWarehouseId != rental.WarehouseId || serial.CurrentBinId is not int binId)
                    throw new ConflictException(SerialRules.NotAvailable(serial.SerialNumber, warehouseCode));
                if (!pickable.TryGetValue(binId, out var bin) || !bin.Pickable)
                    throw new ConflictException(SerialRules.NotAvailable(serial.SerialNumber, bin?.Code ?? warehouseCode));
                line.FromBinId = binId;
                line.LotId = serial.LotId;
                reservations.Add(new StockReservation(line.ProductId, rental.WarehouseId, binId, serial.LotId, 1m, new[] { serial.SerialNumber }));
            }
            await ledger.ReserveAsync(reservations, ct2);
            var to = await statuses.TransitionAsync(StatusDomains.RentalStatus, EntityTypes.Rental, rental.RentalId, rental.StatusCodeId,
                RentalStatuses.Scheduled, comment, ct2);
            rental.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    /// <summary>
    /// Programada → En renta: una TRANSFER por equipo de su posición a EN-RENTA (consume lo reservado y reserva en el destino; la
    /// serie queda ON_RENT con la referencia RENTAL). La unidad sigue en mano y su disponible queda en 0 (D1).
    /// </summary>
    public async Task<RentalDto> DispatchAsync(Guid publicId, RentalStatusRequest? req, CancellationToken ct)
    {
        var comment = PurchasingSupport.Comment(req?.Comment);
        var rentalId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req?.RowVersion);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, rental.StatusCodeId, ct2);
            if (RentalRules.DispatchError(code, rental.Number) is { } error) throw new StatusRuleException(error);
            await EnsureClientStillActiveAsync(rental.ClientId, ct2);
            var lines = await db.RentalLines.Where(l => l.RentalId == rental.RentalId && l.IsActive).OrderBy(l => l.RentalLineId).ToListAsync(ct2);
            if (lines.Count == 0) throw new StatusRuleException(RentalRules.NoLines);

            var rentalBinId = await rentalBins.ResolveAsync(rental.WarehouseId, ct2);
            var serialIds = lines.Select(l => l.SerialId).ToList();
            var serialNumbers = await db.InventorySerials.AsNoTracking().Where(s => serialIds.Contains(s.SerialId))
                .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct2);
            var notes = RentalRules.DispatchNotes(rental.Number);
            var postings = lines.Select(l => new InventoryPosting(InventoryTxnTypes.Transfer, l.ProductId, 1m, LotId: l.LotId,
                SerialNumber: serialNumbers[l.SerialId], FromWarehouseId: rental.WarehouseId, FromBinId: l.FromBinId,
                ToWarehouseId: rental.WarehouseId, ToBinId: rentalBinId, RefEntityType: EntityTypes.Rental, RefId: rental.RentalId,
                Notes: notes, FromReserved: true, TargetSerialStatus: SerialStatuses.OnRent, ReserveAtDestination: true)).ToList();
            var txnIds = await ledger.PostAsync(postings, ct2);
            var now = DateTime.UtcNow;
            for (var i = 0; i < lines.Count; i++)
            {
                lines[i].DispatchTxnId = txnIds[i];
                lines[i].DispatchedAtUtc = now;
            }
            var to = await statuses.TransitionAsync(StatusDomains.RentalStatus, EntityTypes.Rental, rental.RentalId, rental.StatusCodeId,
                RentalStatuses.OnRent, comment, ct2);
            rental.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    /// <summary>Cancela (Borrador o Programada): libera las reservas, desactiva los equipos y deja el comentario en el historial.</summary>
    public async Task<RentalDto> CancelAsync(Guid publicId, RentalStatusRequest? req, CancellationToken ct)
    {
        var comment = PurchasingSupport.Comment(req?.Comment);
        var rentalId = await ResolveIdAsync(publicId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req?.RowVersion);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, rental.StatusCodeId, ct2);
            if (!RentalRules.CanCancel(code)) throw new StatusRuleException(RentalRules.CancelNotAllowed);
            var lines = await db.RentalLines.Where(l => l.RentalId == rental.RentalId && l.IsActive).OrderBy(l => l.RentalLineId).ToListAsync(ct2);
            if (code == RentalStatuses.Scheduled && lines.Count > 0)
            {
                var serialIds = lines.Select(l => l.SerialId).ToList();
                var serialNumbers = await db.InventorySerials.AsNoTracking().Where(s => serialIds.Contains(s.SerialId))
                    .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct2);
                await ledger.ReleaseAsync(lines.Select(l => new StockReservation(l.ProductId, rental.WarehouseId, l.FromBinId, l.LotId, 1m,
                    new[] { serialNumbers[l.SerialId] })).ToList(), ct2);
            }
            var to = await statuses.TransitionAsync(StatusDomains.RentalStatus, EntityTypes.Rental, rental.RentalId, rental.StatusCodeId,
                RentalStatuses.Cancelled, comment, ct2);
            rental.StatusCodeId = to.StatusCodeId;
            foreach (var line in lines) line.IsActive = false;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    // ================================================================ extensiones (D4)

    /// <summary>
    /// Extiende la fecha de recogido (Programada o En renta): nueva fecha posterior a la vigente, motivo obligatorio; bitácora
    /// RentalExtension y, para cada tarifa que cambie, cierre de la vigente y versión nueva desde el día siguiente al recogido
    /// anterior (EffectiveTo exclusivo = EffectiveFrom de la nueva). rental.extend (sin aprobación de un segundo usuario).
    /// </summary>
    public async Task<RentalDto> ExtendAsync(Guid publicId, RentalExtendRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var errors = new Dictionary<string, string[]>();
        if (req.NewPickupDate is null) errors["newPickupDate"] = new[] { NewPickupRequired };
        var (reason, reasonError) = RentalRules.NormalizeReason(req.Reason);
        if (reasonError is not null) errors["reason"] = new[] { reasonError };
        var ratePlans = new List<(int LineId, RatePlan Plan)>();
        var rates = req.Rates ?? Array.Empty<RentalExtensionRateInput>();
        for (var i = 0; i < rates.Count; i++)
        {
            var r = rates[i];
            if (r?.LineId is not int lineId) { errors[$"rates[{i}].lineId"] = new[] { LineRequired }; continue; }
            var (plan, error, field) = await NormalizeRateAsync(r.Frequency, r.Amount, r.Currency, ct);
            if (error is not null) { errors[$"rates[{i}].{field}"] = new[] { error }; continue; }
            ratePlans.Add((lineId, plan!));
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        var rentalId = await ResolveIdAsync(publicId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var rental = await db.LockRentalAsync(rentalId, ct2);
            db.ApplyRowVersion(rental, req.RowVersion);
            var code = await PurchasingSupport.StatusCodeOfAsync(db, rental.StatusCodeId, ct2);
            if (!RentalRules.CanExtend(code)) throw new StatusRuleException(RentalRules.ExtendNotAllowed);
            var previous = rental.PickupDate;
            if (RentalRules.ValidateExtension(previous, req.NewPickupDate!.Value) is { } dateError)
                throw new ValidationException("newPickupDate", dateError);

            var extension = new RentalExtension
            {
                RentalId = rental.RentalId, PreviousPickupDate = previous, NewPickupDate = req.NewPickupDate!.Value, Reason = reason!,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.RentalExtensions.Add(extension);
            rental.PickupDate = req.NewPickupDate!.Value;
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);

            var from = RentalRules.ExtensionRateStart(previous);
            foreach (var (lineId, plan) in ratePlans)
            {
                var line = await ActiveLineAsync(rental.RentalId, lineId, ct2);
                var current = await db.RentalLineRates.Where(r => r.RentalLineId == line.RentalLineId && r.EffectiveTo == null)
                    .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.RentalLineRateId).FirstOrDefaultAsync(ct2);
                if (current is not null && !RentalRules.RateChanged(current.BillingFrequencyLookupId, current.RateAmount, current.CurrencyLookupId,
                        plan.FrequencyId, plan.Amount, plan.CurrencyId))
                    continue;   // la tarifa no cambia: no hay versión nueva
                // EffectiveTo es EXCLUSIVO (como RateComponent): la vigente cubre hasta el recogido anterior inclusive y la nueva
                // empieza el día siguiente.
                if (current is not null) EffectiveDated.CloseNotBefore(current, from);
                db.RentalLineRates.Add(NewRate(line.RentalLineId, plan, from, extension.RentalExtensionId));
            }
            await db.SaveGuardedAsync(ConcurrencyMessage, ct2);
        }, ct);
        return await GetByIdAsync(rentalId, ct);
    }

    // ================================================================ validación de equipos

    /// <summary>
    /// Resuelve y valida los equipos pedidos: producto (404), propio y con serie (400), series normalizadas sin repetir (400) y
    /// cada serie AVAILABLE en una posición recolectable del almacén de origen (409 'La serie {s} no está disponible en {bin}.').
    /// La serie en otra renta abierta se verifica después (EnsureSerialsFreeAsync), dentro de la transacción.
    /// </summary>
    private async Task<List<LinePlan>> ValidateLinesAsync(IReadOnlyList<RentalLinesAddRequest>? requests, int warehouseId, string warehouseCode, CancellationToken ct,
        bool indexed = true)
    {
        var plans = new List<LinePlan>();
        if (requests is null || requests.Count == 0) return plans;

        var errors = new Dictionary<string, string[]>();
        var normalized = new List<(int Index, Guid ProductPublicId, IReadOnlyList<string> Serials, RatePlan? Rate)>();
        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            var prefix = indexed ? $"lines[{i}]." : string.Empty;
            if (r?.ProductPublicId is not Guid productPublicId) { errors[prefix + "productPublicId"] = new[] { RentalRules.ProductRequired }; continue; }
            var (serials, serialError) = SerialRules.Normalize(r.SerialNumbers);
            if (serialError is not null) { errors[prefix + "serialNumbers"] = new[] { serialError }; continue; }
            if (serials.Count == 0) { errors[prefix + "serialNumbers"] = new[] { RentalRules.SerialsRequired }; continue; }
            RatePlan? rate = null;
            if (r.Rate is not null)
            {
                var (plan, rateError, field) = await NormalizeRateAsync(r.Rate.Frequency, r.Rate.Amount, r.Rate.Currency, ct);
                if (rateError is not null) { errors[prefix + "rate." + field] = new[] { rateError }; continue; }
                rate = plan;
            }
            normalized.Add((i, productPublicId, serials, rate));
        }
        var repeated = normalized.SelectMany(n => n.Serials.Select(s => (n.ProductPublicId, Serial: s)))
            .GroupBy(x => (x.ProductPublicId, x.Serial.ToUpperInvariant())).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null) errors["serialNumbers"] = new[] { SerialRules.Duplicated(repeated.First().Serial) };
        // Un solo error: el mensaje va también como título (ProblemDetails) además de en su campo.
        if (errors.Count == 1 && errors.First().Value.Length == 1) throw new ValidationException(errors.First().Key, errors.First().Value[0]);
        if (errors.Count > 0) throw new ValidationException(errors);

        var publicIds = normalized.Select(n => n.ProductPublicId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => publicIds.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var serialTypeId = await lookups.TryGetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var availableId = await db.StatusIdAsync(StatusDomains.SerialStatus, SerialStatuses.Available, ct);
        foreach (var n in normalized)
        {
            if (!products.TryGetValue(n.ProductPublicId, out var product)) throw WmsResolve.ProductNotFound();
            var field = indexed ? $"lines[{n.Index}].productPublicId" : "productPublicId";
            if (RentalRules.CheckEquipment(product.Sku, product.ClientId is null, serialTypeId is int st && product.TrackingTypeLookupId == st) is { } equipmentError)
                throw new ValidationException(field, equipmentError);

            var numbersWanted = n.Serials.ToList();
            var found = await db.InventorySerials.AsNoTracking()
                .Where(s => s.ProductId == product.ProductId && numbersWanted.Contains(s.SerialNumber)).ToListAsync(ct);
            var byNumber = found.ToDictionary(s => s.SerialNumber, StringComparer.OrdinalIgnoreCase);
            var pickable = await PickableBinsAsync(found.Where(s => s.CurrentBinId.HasValue).Select(s => s.CurrentBinId!.Value), ct);
            foreach (var number in n.Serials)
            {
                if (!byNumber.TryGetValue(number, out var serial) || serial.StatusCodeId != availableId
                    || serial.CurrentWarehouseId != warehouseId || serial.CurrentBinId is not int binId)
                {
                    var where = serial?.CurrentWarehouseId == warehouseId && serial.CurrentBinId is int b && pickable.TryGetValue(b, out var info)
                        ? info.Code : warehouseCode;
                    throw new ConflictException(SerialRules.NotAvailable(serial?.SerialNumber ?? number, where));
                }
                if (!pickable.TryGetValue(binId, out var bin) || !bin.Pickable)
                    throw new ConflictException(SerialRules.NotAvailable(serial.SerialNumber, bin?.Code ?? warehouseCode));
                plans.Add(new LinePlan(product.ProductId, product.Sku, serial.SerialId, serial.SerialNumber, serial.LotId, binId, n.Rate));
            }
        }
        return plans;
    }

    /// <summary>409 'La serie {s} ya está en la renta {REN-n}.' si alguna serie está en una línea abierta (activa y no devuelta).</summary>
    private async Task EnsureSerialsFreeAsync(IReadOnlyList<LinePlan> plans, CancellationToken ct)
    {
        if (plans.Count == 0) return;
        var serialIds = plans.Select(p => p.SerialId).ToList();
        var taken = await (from l in db.RentalLines.AsNoTracking()
                           join r in db.Rentals.AsNoTracking() on l.RentalId equals r.RentalId
                           where serialIds.Contains(l.SerialId) && l.IsActive && l.ReturnedAtUtc == null
                           orderby l.RentalLineId
                           select new { l.SerialId, r.Number }).FirstOrDefaultAsync(ct);
        if (taken is not null)
            throw new ConflictException(RentalRules.SerialInRental(plans.First(p => p.SerialId == taken.SerialId).SerialNumber, taken.Number));
    }

    /// <summary>
    /// Inserta los equipos (activos) y, con su id ya asignado, la tarifa inicial de los que la traen (desde la fecha de inicio).
    /// Dos guardados en la transacción del llamador: el primero choca con UX_RentalLine_OpenSerial si otra operación tomó la serie.
    /// </summary>
    private async Task AddLinesAsync(Rental rental, IReadOnlyList<LinePlan> plans, CancellationToken ct)
    {
        if (plans.Count == 0) return;
        var now = DateTime.UtcNow;
        var created = new List<(RentalLine Line, RatePlan? Rate)>();
        foreach (var p in plans)
        {
            var line = new RentalLine
            {
                RentalId = rental.RentalId, ProductId = p.ProductId, SerialId = p.SerialId, LotId = p.LotId, FromBinId = p.BinId, IsActive = true,
                CreatedAtUtc = now,
            };
            db.RentalLines.Add(line);
            created.Add((line, p.Rate));
        }
        await db.SaveGuardedAsync(SerialTakenConcurrently, ct);
        if (!created.Any(c => c.Rate is not null)) return;
        foreach (var (line, rate) in created.Where(c => c.Rate is not null))
            db.RentalLineRates.Add(NewRate(line.RentalLineId, rate!, rental.StartDate, null));
        await db.SaveGuardedAsync(ConcurrencyMessage, ct);
    }

    // ================================================================ apoyo

    private async Task<int> ResolveIdAsync(Guid publicId, CancellationToken ct)
        => await db.Rentals.AsNoTracking().Where(r => r.PublicId == publicId).Select(r => (int?)r.RentalId).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(RentalRules.NotFound, feminine: true);

    /// <summary>Estatus actual de la renta bloqueada; 422 si ya no se edita (despachada, devuelta o cancelada).</summary>
    private async Task<string> EnsureEditableAsync(Rental rental, CancellationToken ct)
    {
        var code = await PurchasingSupport.StatusCodeOfAsync(db, rental.StatusCodeId, ct);
        if (RentalRules.EditError(code, rental.Number) is { } error) throw new StatusRuleException(error);
        return code;
    }

    private async Task EnsureClientStillActiveAsync(int clientId, CancellationToken ct)
    {
        var active = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct);
        if (active != true) throw new ConflictException(ClientQueries.ClientInactiveMessage);
    }

    private async Task<RentalLine> ActiveLineAsync(int rentalId, int lineId, CancellationToken ct)
        => await db.RentalLines.FirstOrDefaultAsync(l => l.RentalLineId == lineId && l.RentalId == rentalId && l.IsActive, ct)
           ?? throw new NotFoundException("Línea", null, true);

    private async Task<string> SerialNumberAsync(int serialId, CancellationToken ct)
        => await db.InventorySerials.AsNoTracking().Where(s => s.SerialId == serialId).Select(s => s.SerialNumber).FirstAsync(ct);

    /// <summary>Localidad del tenant (404), del cliente de la renta (400) y activa (422).</summary>
    private async Task<Location> ResolveLocationAsync(Guid publicId, int clientId, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.PublicId == publicId, ct)
                       ?? throw new NotFoundException("Localidad", null, true);
        if (location.ClientId != clientId) throw new ValidationException("locationPublicId", RentalRules.LocationNotOfClient);
        if (!location.IsActive) throw new StatusRuleException(RentalRules.LocationInactive);
        return location;
    }

    /// <summary>Contacto activo del cliente de la renta (los contactos se alcanzan por su cliente filtrado); si no, 404.</summary>
    private async Task<int> ResolveContactAsync(int contactId, int clientId, CancellationToken ct)
    {
        var exists = await (from cc in db.ClientContacts.AsNoTracking()
                            join c in db.Clients.AsNoTracking() on cc.ClientId equals c.ClientId
                            where cc.ClientContactId == contactId && cc.ClientId == clientId && cc.IsActive
                            select cc.ClientContactId).AnyAsync(ct);
        if (!exists) throw new NotFoundException("Contacto");
        return contactId;
    }

    private sealed record BinInfo(string Code, bool Pickable);

    /// <summary>Código de cada posición y si es recolectable (ni cuarentena, ni cruce de muelle, ni En renta), dentro de almacenes filtrados.</summary>
    private async Task<Dictionary<int, BinInfo>> PickableBinsAsync(IEnumerable<int> binIds, CancellationToken ct)
    {
        var ids = binIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, BinInfo>();
        var rows = await (from b in db.WarehouseBins.AsNoTracking()
                          join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          where ids.Contains(b.WarehouseBinId)
                          select new { b.WarehouseBinId, b.Code, b.IsActive, z.ZoneTypeLookupId }).ToListAsync(ct);
        var result = new Dictionary<int, BinInfo>();
        foreach (var r in rows)
        {
            var zoneType = r.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
            result[r.WarehouseBinId] = new BinInfo(r.Code, r.IsActive && PickBatchRules.IsPickableZone(zoneType));
        }
        return result;
    }

    /// <summary>Frecuencia (obligatoria), monto (obligatorio, ≥ 0) y moneda (la de la compañía por defecto); devuelve (plan, error, campo).</summary>
    private async Task<(RatePlan? Plan, string? Error, string? Field)> NormalizeRateAsync(string? frequency, decimal? amount, string? currency, CancellationToken ct)
    {
        var (frequencyCode, frequencyError) = RentalRules.NormalizeFrequency(frequency);
        if (frequencyError is not null) return (null, frequencyError, "frequency");
        if (RentalRules.ValidateRateAmount(amount) is { } amountError) return (null, amountError, "amount");
        var frequencyId = await lookups.TryGetIdAsync(LookupDomains.RentalBillingFrequency, frequencyCode!, ct);
        if (frequencyId is null) return (null, RentalRules.UnknownFrequency(frequencyCode!), "frequency");
        var currencyCode = string.IsNullOrWhiteSpace(currency) ? await DefaultCurrencyAsync(ct) : currency.Trim().ToUpperInvariant();
        var currencyId = await lookups.TryGetIdAsync(LookupDomains.Currency, currencyCode, ct);
        if (currencyId is null) return (null, RentalRules.UnknownCurrency(currencyCode), "currency");
        return (new RatePlan(frequencyId.Value, amount!.Value, currencyId.Value), null, null);
    }

    private async Task<string> DefaultCurrencyAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var code = tenantId is int id
            ? await db.Tenants.AsNoTracking().Where(t => t.TenantId == id).Select(t => t.CurrencyCode).FirstOrDefaultAsync(ct)
            : null;
        return string.IsNullOrWhiteSpace(code) ? "USD" : code.Trim().ToUpperInvariant();
    }

    private RentalLineRate NewRate(int lineId, RatePlan plan, DateOnly from, int? extensionId) => new()
    {
        RentalLineId = lineId, BillingFrequencyLookupId = plan.FrequencyId, RateAmount = plan.Amount, CurrencyLookupId = plan.CurrencyId,
        EffectiveFrom = from, EffectiveTo = null, RentalExtensionId = extensionId, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
    };

    // ================================================================ DTOs

    private async Task<Dictionary<int, StatusInfo>> StatusInfoAsync(CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.RentalStatus).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, (string? Label, string? Color)>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId))
                .ToDictionaryAsync(o => o.StatusCodeId, o => (Label: o.CustomLabelJson, Color: o.CustomColorHex), ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o.Label), tenant.Lang), o.Color ?? c.ColorHex);
        });
    }

    private async Task<IReadOnlyList<RentalListItemDto>> ToListItemsAsync(List<Rental> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<RentalListItemDto>();
        var today = _clock.Today;
        var rentalIds = rows.Select(r => r.RentalId).ToList();
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var locationIds = rows.Select(r => r.LocationId).Distinct().ToList();
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.LocationId))
            .Select(l => new { l.LocationId, l.PublicId, l.Name, l.City }).ToDictionaryAsync(l => l.LocationId, ct);
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var units = await db.RentalLines.AsNoTracking().Where(l => rentalIds.Contains(l.RentalId) && l.IsActive)
            .GroupBy(l => l.RentalId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var extensions = await db.RentalExtensions.AsNoTracking().Where(e => rentalIds.Contains(e.RentalId))
            .GroupBy(e => e.RentalId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var statusMap = await StatusInfoAsync(ct);

        return rows.Select(r =>
        {
            var s = statusMap.GetValueOrDefault(r.StatusCodeId);
            var code = s?.Code ?? string.Empty;
            var c = clients.GetValueOrDefault(r.ClientId);
            var l = locations.GetValueOrDefault(r.LocationId);
            var w = warehouses.GetValueOrDefault(r.WarehouseId);
            return new RentalListItemDto(r.RentalId, r.PublicId, r.Number, c?.PublicId ?? Guid.Empty, c?.Name ?? string.Empty,
                l?.PublicId ?? Guid.Empty, l?.Name ?? string.Empty, l?.City, w?.PublicId ?? Guid.Empty, w?.Code ?? string.Empty,
                r.StartDate, r.PickupDate, r.OriginalPickupDate, RentalRules.DaysToPickup(r.PickupDate, today),
                RentalRules.IsOverdue(code, r.PickupDate, today), r.ContractNumber, code, s?.Label ?? code, s?.Color,
                units.GetValueOrDefault(r.RentalId), extensions.GetValueOrDefault(r.RentalId), r.DispatchedAtUtc, r.ClosedAtUtc, r.CreatedAtUtc);
        }).ToList();
    }

    private async Task<RentalDto> ToDtoAsync(Rental rental, CancellationToken ct)
    {
        var item = (await ToListItemsAsync(new List<Rental> { rental }, ct))[0];
        var code = item.StatusCode;
        var cancelled = code == RentalStatuses.Cancelled;
        var lineRows = await (from l in db.RentalLines.AsNoTracking()
                              join p in db.Products.AsNoTracking() on l.ProductId equals p.ProductId
                              join s in db.InventorySerials.AsNoTracking() on l.SerialId equals s.SerialId
                              join b in db.WarehouseBins.AsNoTracking() on l.FromBinId equals b.WarehouseBinId
                              where l.RentalId == rental.RentalId && (l.IsActive || cancelled)
                              orderby l.RentalLineId
                              select new { Line = l, ProductPublicId = p.PublicId, p.Sku, p.Name, s.SerialNumber, BinCode = b.Code }).ToListAsync(ct);
        var lineIds = lineRows.Select(x => x.Line.RentalLineId).ToList();
        var lotIds = lineRows.Where(x => x.Line.LotId.HasValue).Select(x => x.Line.LotId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.InventoryLots.AsNoTracking().Where(l => lotIds.Contains(l.LotId)).ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var rates = await db.RentalLineRates.AsNoTracking().Where(r => lineIds.Contains(r.RentalLineId))
            .OrderBy(r => r.EffectiveFrom).ThenBy(r => r.RentalLineRateId).ToListAsync(ct);

        var lines = new List<RentalLineDto>();
        foreach (var x in lineRows)
        {
            var history = new List<RentalLineRateDto>();
            foreach (var r in rates.Where(r => r.RentalLineId == x.Line.RentalLineId)) history.Add(await RateDtoAsync(r, ct));
            var current = history.LastOrDefault(h => h.EffectiveTo is null);
            lines.Add(new RentalLineDto(x.Line.RentalLineId, x.ProductPublicId, x.Sku, x.Name, x.Line.SerialId, x.SerialNumber,
                x.Line.LotId is int lid ? lots.GetValueOrDefault(lid) : null, x.Line.FromBinId, x.BinCode, x.Line.IsActive,
                x.Line.DispatchTxnId, x.Line.DispatchedAtUtc, x.Line.ReturnedAtUtc, current, history));
        }

        string? contactName = null;
        if (rental.ClientContactId is int contactId)
            contactName = await db.ClientContacts.AsNoTracking().Where(c => c.ClientContactId == contactId && c.ClientId == rental.ClientId)
                .Select(c => c.FullName).FirstOrDefaultAsync(ct);
        var currency = rental.TransportCurrencyLookupId is int tcid ? (await lookups.GetAsync(tcid, ct))?.InternalCode : null;
        var hasLines = lines.Any(l => l.IsActive);
        return new RentalDto(item, rental.ClientContactId, contactName, rental.ContractSignedOn, rental.EstimatedDeliveryCost, currency,
            rental.DeliveryShipmentId, rental.InvoiceId, rental.Notes,
            RentalRules.IsEditable(code), code == RentalStatuses.Draft && hasLines, code == RentalStatuses.Scheduled && hasLines,
            RentalRules.CanExtend(code), RentalRules.CanCancel(code), lines, Convert.ToBase64String(rental.RowVersion ?? Array.Empty<byte>()));
    }

    private async Task<RentalLineRateDto> RateDtoAsync(RentalLineRate r, CancellationToken ct)
    {
        var frequency = await lookups.GetAsync(r.BillingFrequencyLookupId, ct);
        var currency = await lookups.GetAsync(r.CurrencyLookupId, ct);
        return new RentalLineRateDto(r.RentalLineRateId, frequency?.InternalCode ?? string.Empty,
            frequency is null ? string.Empty : MultilingualText.Resolve(frequency.LabelJson, tenant.Lang), r.RateAmount,
            currency?.InternalCode ?? string.Empty, r.EffectiveFrom, r.EffectiveTo, r.RentalExtensionId);
    }
}
