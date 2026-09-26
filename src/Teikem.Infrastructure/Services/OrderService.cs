using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 3 (P2): creación, edición y eliminación de órdenes de transporte (entrada rápida, detallada y especial).
/// - Todo lo de lectura se valida ANTES de la transacción (sin tracking); la transacción solo dibuja números, inserta y
///   registra el historial de nacimiento (RunInTransactionAsync limpia el tracker: lo tracked se carga dentro del delegado).
/// - Numeración (DECISIÓN 3/23): la fila del contador se asegura en autocommit (EnsureAsync) y el valor se consume DENTRO de la
///   transacción (NextAsync, bloqueo de fila hasta el commit: sin huecos) siempre en el orden ORDER → INVOICE → PACKBATCH → PACKAGE
///   (mismo orden de bloqueos: sin interbloqueos). Si un automático choca con uno tecleado (409 del índice) se salta el valor
///   chocado en autocommit y se reintenta (máximo NumberingRules.MaxAutoRetries).
/// - Consignatario (DECISIÓN 13): PublicId explícito (del cliente o compartido; 404 sin oráculo, 400 si inactivo) o escrito libre:
///   primero ConsigneeMatch contra el directorio del propio cliente por nombre + línea 1; si no coincide, se crea una Location
///   DELIVERY del cliente dentro de la transacción. La parada recibe el snapshot completo (Snap*, notas, ventana; L236).
/// - Factura repetida (R36, DECISIÓN 5): solo si el número fue tecleado y contra el mismo consignatario, ANTES de dibujar
///   números (un 409 no gasta consecutivos); 409 duplicate_invoice / duplicate_invoice_confirmable con errors estructurados.
/// - Cliente dado de baja → 409 (ajuste A); SUSPENDED → 422 (DECISIÓN 16). Campos fijos/prohibidos → 400 (DECISIÓN 4/15).
/// - Todo bajo OrderScope: el portal fija el cliente ignorando clientPublicId; PATCH/DELETE responden 404 fuera del scope.
/// </summary>
public sealed class OrderService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    LocationService locations,
    INumberSequenceService sequences,
    OrderStatusService orderStatuses,
    OrderReadService reader,
    PermissionService permissions)
{
    public const string ClientInactiveMessage = "El cliente está dado de baja; solo se consulta su historial.";
    public const string ClientSuspendedMessage = "El cliente está suspendido; no se pueden crear ni confirmar órdenes.";
    public const string OrderNumberTakenMessage = "Ya existe una orden con ese número para este cliente.";
    public const string PackBatchTakenMessage = "Ya existe una orden con ese número de empaque.";
    public const string NoFreeNumberMessage = "No se pudo asignar un número de orden libre; intente de nuevo.";
    public const string ConsigneeRequiredMessage = "El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.";
    public const string ConsigneeBothMessage = "Indique un consignatario del directorio o uno nuevo, no ambos.";
    public const string SpecialServiceRequiredMessage = "El servicio especial es obligatorio en una entrega especial.";
    public const string SpecialServiceNotCurrentMessage = "El servicio especial no está vigente para este cliente.";

    /// <summary>Savepoint de cada intento de alta cuando CreateAsync corre dentro de una transacción ambiente (importador).</summary>
    private const string CreateAttemptSavepoint = "OrderCreateAttempt";

    // ---------------------------------------------------------------- tipos internos de preparación

    /// <summary>Consignatario resuelto antes de la transacción: existente (explícito o coincidente) o por crear al vuelo.</summary>
    private sealed record ConsigneeInput(Location? Existing, LocationUpsertRequest? ToCreate, string Name);

    /// <summary>Línea de paquete ya resuelta contra el catálogo (tipo, etiqueta, número tecleado).</summary>
    private sealed record PreparedLine(int? PackageTypeId, string Description, int Pieces, decimal? WeightKg, decimal? VolumeM3, string? TypedPackageNumber);

    private sealed record PreparedReference(int RefTypeId, string Value, string? Source);

    // ================================================================ CREAR

    public async Task<OrderDetailDto> CreateAsync(OrderCreateRequest req, OrderScope scope, OrderCreationOptions? options, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();

        // (a) campos que la captura no admite (empaque siempre Teikem; tipo de COD al entregar)
        RejectExtra(req.Extra?.Keys, OrderRules.ForbiddenOnCreate);

        // Ajuste C: overrideCredit solo acompaña a confirmNow; el permiso se exige pronto (403 + PERMISSION_DENIED) y
        // ConfirmTrackedAsync lo vuelve a comprobar.
        if (req.OverrideCredit && !req.ConfirmNow) throw new ValidationException("overrideCredit", OrderImportService.OverrideNeedsConfirmNowMessage);
        if (req.OverrideCredit) await permissions.EnsureAsync(OrderStatusService.CreditOverridePermission, ct);

        // (b) cliente: el scope (portal) gana siempre; si no, clientPublicId. Dado de baja → 409; suspendido → 422.
        var client = await ResolveClientForScopeAsync(scope, req.ClientPublicId, ct);
        await EnsureClientCanTakeOrdersAsync(client, ct);
        var tenantRow = await LoadTenantAsync(tenantId, ct);

        // (c) tipo de servicio (código o default del tenant)
        var serviceTypeId = await ResolveServiceTypeAsync(req.ServiceType, tenantRow, ct);

        // (d) consignatario: exactamente uno de los dos; existente, coincidente o por crear
        var consignee = await ResolveConsigneeInputAsync(client, req.ConsigneeLocationPublicId, req.NewConsignee, ct);

        // (e) recogido explícito o por defecto del cliente (puede no haber: la orden nace solo con la parada DELIVERY)
        var pickup = req.PickupLocationPublicId is Guid pickupPublicId
            ? await db.ResolvePickupAsync(client.ClientId, pickupPublicId, ct)
            : await db.DefaultPickupAsync(client, ct);

        var priorityId = await ResolvePriorityAsync(req.Priority, ct);

        // (h) COD: negativo → 400; null o 0 → sin COD
        if (OrderRules.ValidateCod(req.CodAmount) is string codError) throw new ValidationException("codAmount", codError);
        decimal? cod = req.CodAmount is > 0 ? req.CodAmount : null;

        // (f)/(g) entrega especial o paquetes normales
        var inputLines = ToInputs(req.Packages);
        ThrowIfErrors(OrderRules.ValidatePackages(inputLines, req.IsSpecialDelivery, cod));
        SpecialService? special = null;
        List<PreparedLine> lines;
        if (req.IsSpecialDelivery)
        {
            special = await ResolveSpecialServiceAsync(client, req.SpecialServiceId, ct);
            lines = new List<PreparedLine> { new(null, special.Type!.Name, 1, null, null, null) };
        }
        else
        {
            lines = await PrepareLinesAsync(req.Packages!, tenantRow, ct);
        }

        // (i) referencias, contrato vigente (solo para la moneda: ContractId se congela al confirmar, DECISIÓN 17)
        var references = await PrepareReferencesAsync(req.References, ct);
        var contract = await db.CurrentContractAsync(client.ClientId, Today(), ct);
        var currencyId = client.CurrencyLookupId ?? contract?.CurrencyLookupId;

        // (j) numeración tecleada según las dos preguntas independientes del cliente
        var settings = NumberingSettings.FromClient(client);
        var typedOrder = EnsureTyped(NumberKinds.Order, settings, req.OrderNumber, "orderNumber");
        var typedInvoice = EnsureTyped(NumberKinds.Invoice, settings, req.ClientInvoiceNumber, "clientInvoiceNumber");
        var packBatchOverride = NormalizeTyped(options?.PackBatchNumberOverride, "packBatchNumber");
        int? sourceTypeId = string.IsNullOrWhiteSpace(options?.SourceEntityType)
            ? null
            : await lookups.GetIdAsync(LookupDomains.EntityType, options!.SourceEntityType!.Trim().ToUpperInvariant(), ct);

        // (k) factura repetida ANTES de dibujar números: solo si fue tecleada y el consignatario ya existía o coincidió
        if (typedInvoice is not null && consignee.Existing is not null)
            await CheckDuplicateInvoiceAsync(consignee.Existing, typedInvoice, req.ConfirmDuplicateInvoice, scope, ct);

        // (l) filas del contador en autocommit (idempotente), solo para lo automático
        var orderSeqClient = NumberingRules.ScopeClientId(NumberKinds.Order, client.ClientId);
        var invoiceSeqClient = NumberingRules.ScopeClientId(NumberKinds.Invoice, client.ClientId);
        var packBatchSeqClient = NumberingRules.ScopeClientId(NumberKinds.PackBatch, client.ClientId);
        var packageSeqClient = NumberingRules.ScopeClientId(NumberKinds.Package, client.ClientId);
        if (typedOrder is null) await sequences.EnsureAsync(NumberKinds.Order, orderSeqClient, ct);
        if (typedInvoice is null) await sequences.EnsureAsync(NumberKinds.Invoice, invoiceSeqClient, ct);
        if (packBatchOverride is null) await sequences.EnsureAsync(NumberKinds.PackBatch, packBatchSeqClient, ct);
        if (special is null && lines.Any(l => l.TypedPackageNumber is null)) await sequences.EnsureAsync(NumberKinds.Package, packageSeqClient, ct);

        var pickupTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup, ct);
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var pickupCountry = pickup is null ? null : await CountryCodeAsync(pickup.CountryLookupId, ct);
        var consigneeCountry = consignee.Existing is null ? consignee.ToCreate!.Country : await CountryCodeAsync(consignee.Existing.CountryLookupId, ct);
        var totals = OrderRules.Totals(lines.Select(l => new PackageLineInput(l.Description, l.Pieces, l.WeightKg, l.VolumeM3)));

        // (m) hasta MaxAutoRetries intentos: un choque de número automático con uno tecleado salta el valor y repite.
        // Con transacción propia, RunInTransactionAsync revierte el intento y limpia el tracker. Llamado dentro de una
        // transacción ambiente (importador: una transacción por fila) RunInTransactionAsync solo se une a ella, así que cada
        // intento se envuelve en un savepoint: el fallido se revierte (números dibujados, consignatario al vuelo, historial) y
        // sus entidades se desprenden del tracker, para que el reintento se comporte igual que con transacción propia.
        var ambientTx = db.Database.CurrentTransaction;
        Guid createdPublicId;
        for (var attempt = 1; ; attempt++)
        {
            HashSet<object>? trackedBefore = null;
            if (ambientTx is not null)
            {
                trackedBefore = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
                await ambientTx.CreateSavepointAsync(CreateAttemptSavepoint, ct);
            }
            try
            {
                createdPublicId = await db.RunInTransactionAsync(async ct2 =>
                {
                    // consignatario nuevo al vuelo: se crea dentro de la transacción (un rollback lo deshace también)
                    Location consigneeLoc;
                    if (consignee.Existing is not null)
                        consigneeLoc = consignee.Existing;
                    else
                    {
                        var created = await locations.CreateAsync(consignee.ToCreate!, ct2);
                        consigneeLoc = await db.Locations.AsNoTracking().FirstAsync(l => l.LocationId == created.Id, ct2);
                    }

                    // Números en el orden fijo ORDER → INVOICE → PACKBATCH → PACKAGE (DrawOrder)
                    var orderNumber = typedOrder ?? ResolveAuto(NumberKinds.Order, settings,
                        await sequences.NextAsync(NumberKinds.Order, orderSeqClient, ct2));
                    var invoiceNumber = typedInvoice ?? ResolveAuto(NumberKinds.Invoice, settings,
                        await sequences.NextAsync(NumberKinds.Invoice, invoiceSeqClient, ct2));
                    var packBatchNumber = packBatchOverride ?? ResolveAuto(NumberKinds.PackBatch, settings,
                        await sequences.NextAsync(NumberKinds.PackBatch, packBatchSeqClient, ct2));
                    var packageNumbers = new List<string?>(lines.Count);
                    foreach (var line in lines)
                    {
                        if (special is not null) { packageNumbers.Add(null); continue; }
                        packageNumbers.Add(line.TypedPackageNumber ?? ResolveAuto(NumberKinds.Package, settings,
                            await sequences.NextAsync(NumberKinds.Package, packageSeqClient, ct2)));
                    }

                    var initial = await statuses.GetInitialAsync(StatusDomains.OrderStatus, ct2);
                    var order = new TransportOrder
                    {
                        TenantId = tenantId,
                        ClientId = client.ClientId,
                        ContractId = null,
                        OrderNumber = orderNumber,
                        ClientInvoiceNumber = invoiceNumber,
                        ClientInvoiceNumberTyped = typedInvoice is not null,
                        PackBatchNumber = packBatchNumber,
                        ServiceTypeLookupId = serviceTypeId,
                        StatusCodeId = initial.StatusCodeId,
                        PriorityLookupId = priorityId,
                        RequestedDate = req.RequestedDate,
                        PromisedDate = req.PromisedDate,
                        TotalWeightKg = totals.WeightKg,
                        TotalVolumeM3 = totals.VolumeM3,
                        TotalPieces = totals.Pieces,
                        CurrencyLookupId = currencyId,
                        CodTypeLookupId = null, // se define al entregar (11B), nunca en la captura
                        CodAmount = cod,
                        CodCurrencyLookupId = cod is null ? null : currencyId,
                        CodStatusCodeId = null,
                        IsSpecialDelivery = special is not null,
                        SpecialServiceId = special?.SpecialServiceId,
                        SourceEntityTypeLookupId = sourceTypeId,
                        SourceEntityId = options?.SourceEntityId,
                        Notes = OptionalText(req.Notes),
                        IsActive = true,
                    };
                    db.TransportOrders.Add(order);
                    try
                    {
                        await db.SaveChangesAsync(ct2);
                    }
                    catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
                    {
                        // La orden fallida no debe reintentarse en el siguiente SaveChanges ni dejar bitácora pendiente.
                        db.Entry(order).State = EntityState.Detached;
                        db.PendingAudits.Clear();
                        var sqlMessage = SqlMessage(ex);
                        var collided = NumberingRules.CollidedKind(sqlMessage, orderAuto: typedOrder is null, packBatchAuto: packBatchOverride is null);
                        if (collided is not null) throw new OrderNumberCollision(collided);
                        throw new ConflictException(sqlMessage.Contains("UX_Order_PackBatch", StringComparison.OrdinalIgnoreCase) ? PackBatchTakenMessage : OrderNumberTakenMessage);
                    }

                    // Historial de nacimiento (null → DRAFT); el efecto de cotización solo actúa al salir de la etapa inicial.
                    var born = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId, null, initial.InternalCode, null, ct2);
                    order.StatusCodeId = born.StatusCodeId;

                    // Paradas: PICKUP (1) si hay recogido y DELIVERY (2) siempre, con snapshot completo (L236)
                    var stopInitial = await statuses.GetInitialAsync(StatusDomains.StopStatus, ct2);
                    OrderStop? pickupStop = null;
                    if (pickup is not null)
                    {
                        pickupStop = new OrderStop { TransportOrderId = order.TransportOrderId, StopTypeLookupId = pickupTypeId, Sequence = 1, StatusCodeId = stopInitial.StatusCodeId };
                        OrderRules.ApplySnapshot(pickupStop, pickup, pickupCountry, req.RequestedDate);
                        db.OrderStops.Add(pickupStop);
                    }
                    var deliveryStop = new OrderStop { TransportOrderId = order.TransportOrderId, StopTypeLookupId = deliveryTypeId, Sequence = 2, StatusCodeId = stopInitial.StatusCodeId };
                    OrderRules.ApplySnapshot(deliveryStop, consigneeLoc, consigneeCountry, req.RequestedDate);
                    db.OrderStops.Add(deliveryStop);
                    await db.SaveChangesAsync(ct2);
                    if (pickupStop is not null) await BornStopAsync(pickupStop, stopInitial.InternalCode, ct2);
                    await BornStopAsync(deliveryStop, stopInitial.InternalCode, ct2);

                    // Líneas (R9: paradas de la misma orden por construcción) y referencias
                    for (var i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        db.CargoLines.Add(new CargoLine
                        {
                            TransportOrderId = order.TransportOrderId,
                            PickupStopId = pickupStop?.OrderStopId,
                            DeliveryStopId = deliveryStop.OrderStopId,
                            PackageTypeLookupId = line.PackageTypeId,
                            PackageNumber = packageNumbers[i],
                            Description = line.Description,
                            Quantity = line.Pieces,
                            WeightKg = line.WeightKg,
                            VolumeM3 = line.VolumeM3,
                            HandlingFlags = 0,
                            IsActive = true,
                        });
                    }
                    foreach (var r in references)
                        db.OrderReferences.Add(new OrderReference { TransportOrderId = order.TransportOrderId, RefTypeLookupId = r.RefTypeId, RefValue = r.Value, Source = r.Source });

                    // COD pendiente bajo su propio EntityType (ORDER_COD, DECISIÓN 7)
                    if (cod is not null)
                    {
                        var codTo = await statuses.TransitionAsync(StatusDomains.CodStatus, EntityTypes.OrderCod, order.TransportOrderId, null, CodStatuses.Pending, null, ct2);
                        order.CodStatusCodeId = codTo.StatusCodeId;
                    }

                    await db.SaveGuardedAsync(OrderNumberTakenMessage, ct2);

                    // confirmNow (DECISIÓN 26): cotiza, verifica crédito y avanza en la misma transacción; un 422 revierte también la creación.
                    if (req.ConfirmNow) await orderStatuses.ConfirmTrackedAsync(order, req.OverrideCredit, ct2);

                    return order.PublicId;
                }, ct);
                break;
            }
            catch (OrderNumberCollision collision)
            {
                if (ambientTx is not null)
                {
                    // Revierte el intento dentro de la transacción ambiente (incluido el NextAsync que produjo el valor chocado)
                    // y desprende lo que el intento dejó tracked; así el salto de abajo consume exactamente el valor chocado.
                    await ambientTx.RollbackToSavepointAsync(CreateAttemptSavepoint, ct);
                    foreach (var entry in db.ChangeTracker.Entries().Where(e => !trackedBefore!.Contains(e.Entity)).ToList())
                        entry.State = EntityState.Detached;
                    db.PendingAudits.Clear();
                }
                if (attempt >= NumberingRules.MaxAutoRetries) throw new ConflictException(NoFreeNumberMessage);
                // El valor chocado es justo el que alguien tecleó: se consume en autocommit (el hueco coincide con un número en uso).
                await sequences.NextAsync(collision.Kind, NumberingRules.ScopeClientId(collision.Kind, client.ClientId), ct);
            }
        }

        return await reader.GetAsync(createdPublicId, scope, ct);
    }

    // ================================================================ EDITAR

    public async Task<OrderDetailDto> UpdateAsync(Guid publicId, OrderPatchRequest req, OrderScope scope, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();

        // Campos fijos al crear (cliente, números) y tipo de COD: 400 antes de tocar nada.
        RejectExtra(req.Extra?.Keys, OrderRules.ForbiddenOnPatch);
        if (req.ConsigneeLocationPublicId.HasValue && req.NewConsignee is not null) throw new ValidationException("consignee", ConsigneeBothMessage);
        if (OrderRules.ValidateCod(req.CodAmount) is string codError) throw new ValidationException("codAmount", codError);
        if (req.ClearCod == true && req.CodAmount is > 0) throw new ValidationException("codAmount", "Indique un monto COD o clearCod, no ambos.");

        // Cabecera ligera (bajo el scope: 404 sin oráculo) para preparar lo que no necesita tracking.
        var head = await db.ScopedOrders(scope).AsNoTracking()
            .Where(o => o.PublicId == publicId)
            .Select(o => new { o.ClientId, o.IsSpecialDelivery, o.IsActive })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Orden");
        if (!head.IsActive) throw new NotFoundException("Orden");

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == head.ClientId, ct) ?? throw new NotFoundException("Cliente");
        var tenantRow = await LoadTenantAsync(tenantId, ct);

        if (head.IsSpecialDelivery && ((req.Packages is { Count: > 0 }) || req.CodAmount is > 0))
            throw new ValidationException(req.Packages is { Count: > 0 } ? "packages" : "codAmount", OrderRules.SpecialDeliveryNoCargoMessage);

        List<PreparedLine>? newLines = null;
        if (req.Packages is not null && !head.IsSpecialDelivery)
        {
            ThrowIfErrors(OrderRules.ValidatePackages(ToInputs(req.Packages), isSpecialDelivery: false));
            newLines = await PrepareLinesAsync(req.Packages, tenantRow, ct);
        }
        int? serviceTypeId = req.ServiceType is null ? null : await ResolveServiceTypeAsync(req.ServiceType, tenantRow, ct);
        int? priorityId = req.Priority is null ? null : await ResolvePriorityAsync(req.Priority, ct);
        var references = req.References is null ? null : await PrepareReferencesAsync(req.References, ct);
        var consignee = req.ConsigneeLocationPublicId.HasValue || req.NewConsignee is not null
            ? await ResolveConsigneeInputAsync(client, req.ConsigneeLocationPublicId, req.NewConsignee, ct)
            : null;
        var pickup = req.PickupLocationPublicId is Guid pickupPublicId ? await db.ResolvePickupAsync(client.ClientId, pickupPublicId, ct) : null;
        var settings = NumberingSettings.FromClient(client);
        var packageSeqClient = NumberingRules.ScopeClientId(NumberKinds.Package, client.ClientId);
        if (newLines is not null && newLines.Any(l => l.TypedPackageNumber is null)) await sequences.EnsureAsync(NumberKinds.Package, packageSeqClient, ct);

        var pickupTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup, ct);
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var contract = await db.CurrentContractAsync(client.ClientId, Today(), ct);
        var currencyId = client.CurrencyLookupId ?? contract?.CurrencyLookupId;

        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(publicId, scope, ct2);
            if (!order.IsActive) throw new NotFoundException("Orden");
            db.ApplyRowVersion(order, req.RowVersion);
            await statuses.EnsureAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.EditCargo, ct2);

            var requestedDate = req.RequestedDate ?? order.RequestedDate;
            var deliveryStop = order.Stops.FirstOrDefault(s => s.StopTypeLookupId == deliveryTypeId)
                               ?? throw new InvalidOperationException("La orden no tiene parada de entrega.");
            var pickupStop = order.Stops.FirstOrDefault(s => s.StopTypeLookupId == pickupTypeId);

            // Consignatario: existente, coincidente o nuevo → re-snapshot de la parada DELIVERY
            if (consignee is not null)
            {
                // R36 también al mover la orden a otro consignatario existente (o coincidente): misma regla que al crear
                // (solo si la factura de ESTA orden se tecleó, L1124, sin importar el ajuste actual del cliente; la orden
                // editada no cuenta como duplicado de sí misma).
                if (consignee.Existing is not null && order.ClientInvoiceNumberTyped
                    && deliveryStop.LocationId != consignee.Existing.LocationId)
                    await CheckDuplicateInvoiceAsync(consignee.Existing, order.ClientInvoiceNumber, req.ConfirmDuplicateInvoice, scope, ct2, order.TransportOrderId);

                Location loc;
                if (consignee.Existing is not null) loc = consignee.Existing;
                else
                {
                    var created = await locations.CreateAsync(consignee.ToCreate!, ct2);
                    loc = await db.Locations.AsNoTracking().FirstAsync(l => l.LocationId == created.Id, ct2);
                }
                OrderRules.ApplySnapshot(deliveryStop, loc, await CountryCodeAsync(loc.CountryLookupId, ct2), requestedDate);
            }
            else if (req.RequestedDate is not null && deliveryStop.LocationId is int deliveryLocId)
            {
                var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationId == deliveryLocId, ct2);
                if (loc is not null) OrderRules.ApplyWindow(deliveryStop, loc, requestedDate);
            }

            // Recogido: re-snapshot de la parada PICKUP o alta de la parada si la orden nació sin recogido
            if (pickup is not null)
            {
                var pickupCountry = await CountryCodeAsync(pickup.CountryLookupId, ct2);
                if (pickupStop is null)
                {
                    var stopInitial = await statuses.GetInitialAsync(StatusDomains.StopStatus, ct2);
                    pickupStop = new OrderStop { TransportOrderId = order.TransportOrderId, StopTypeLookupId = pickupTypeId, Sequence = 1, StatusCodeId = stopInitial.StatusCodeId };
                    OrderRules.ApplySnapshot(pickupStop, pickup, pickupCountry, requestedDate);
                    order.Stops.Add(pickupStop);
                    await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
                    await BornStopAsync(pickupStop, stopInitial.InternalCode, ct2);
                    foreach (var line in order.CargoLines.Where(l => l.IsActive)) line.PickupStopId = pickupStop.OrderStopId;
                }
                else
                {
                    OrderRules.ApplySnapshot(pickupStop, pickup, pickupCountry, requestedDate);
                }
            }
            else if (req.RequestedDate is not null && pickupStop?.LocationId is int pickupLocId)
            {
                var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationId == pickupLocId, ct2);
                if (loc is not null) OrderRules.ApplyWindow(pickupStop, loc, requestedDate);
            }

            if (serviceTypeId is int st) order.ServiceTypeLookupId = st;
            if (req.Priority is not null) order.PriorityLookupId = priorityId;

            // Paquetes: reemplazo (las líneas actuales quedan IsActive=0 como rastro) y totales recalculados
            if (newLines is not null)
            {
                foreach (var line in order.CargoLines.Where(l => l.IsActive)) line.IsActive = false;
                foreach (var line in newLines)
                {
                    var packageNumber = line.TypedPackageNumber ?? ResolveAuto(NumberKinds.Package, settings,
                        await sequences.NextAsync(NumberKinds.Package, packageSeqClient, ct2));
                    order.CargoLines.Add(new CargoLine
                    {
                        TransportOrderId = order.TransportOrderId,
                        PickupStopId = pickupStop?.OrderStopId,
                        DeliveryStopId = deliveryStop.OrderStopId,
                        PackageTypeLookupId = line.PackageTypeId,
                        PackageNumber = packageNumber,
                        Description = line.Description,
                        Quantity = line.Pieces,
                        WeightKg = line.WeightKg,
                        VolumeM3 = line.VolumeM3,
                        HandlingFlags = 0,
                        IsActive = true,
                    });
                }
                var totals = OrderRules.Totals(newLines.Select(l => new PackageLineInput(l.Description, l.Pieces, l.WeightKg, l.VolumeM3)));
                order.TotalPieces = totals.Pieces;
                order.TotalWeightKg = totals.WeightKg;
                order.TotalVolumeM3 = totals.VolumeM3;
            }

            // COD: quitar, o fijar un monto (nuevo COD → PENDING bajo ORDER_COD)
            if (req.ClearCod == true || req.CodAmount is 0)
            {
                order.CodAmount = null;
                order.CodCurrencyLookupId = null;
                order.CodStatusCodeId = null;
            }
            else if (req.CodAmount is > 0)
            {
                var hadCod = order.CodStatusCodeId is not null;
                order.CodAmount = req.CodAmount;
                order.CodCurrencyLookupId ??= currencyId;
                if (!hadCod)
                {
                    var codTo = await statuses.TransitionAsync(StatusDomains.CodStatus, EntityTypes.OrderCod, order.TransportOrderId, null, CodStatuses.Pending, null, ct2);
                    order.CodStatusCodeId = codTo.StatusCodeId;
                }
            }

            if (req.RequestedDate is not null) order.RequestedDate = req.RequestedDate;
            if (req.PromisedDate is not null) order.PromisedDate = req.PromisedDate;
            if (req.Notes is not null) order.Notes = OptionalText(req.Notes);

            // Referencias: reemplazo completo (sin historial propio; el AuditLog de la orden conserva el rastro)
            if (references is not null)
            {
                db.OrderReferences.RemoveRange(order.References.ToList());
                order.References.Clear();
                foreach (var r in references)
                    order.References.Add(new OrderReference { TransportOrderId = order.TransportOrderId, RefTypeLookupId = r.RefTypeId, RefValue = r.Value, Source = r.Source });
            }

            // Ya cotizada: exige REPRICE y re-cotiza para no dejar un monto viejo (DECISIÓN 9)
            if (order.QuotedAmount is not null)
            {
                await statuses.EnsureAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.Reprice, ct2);
                await orderStatuses.RequoteTrackedAsync(order, ct2);
            }

            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(publicId, scope, ct);
    }

    // ================================================================ ELIMINAR (baja lógica en la etapa inicial)

    public async Task DeleteAsync(Guid publicId, OrderScope scope, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.ResolveOrderForWriteAsync(publicId, scope, ct2);
            if (!order.IsActive) throw new NotFoundException("Orden");
            var isInitial = await db.IsInitialAsync(order.StatusCodeId, ct2);
            if (!OrderRules.CanDelete(isInitial, order.IsActive)) throw new StatusRuleException(OrderRules.DeleteOnlyInitialMessage);

            // Sin transición de estatus ni DELETE: el índice filtrado libera OrderNumber/PackBatchNumber; el interceptor deja el AuditLog.
            order.IsActive = false;
            foreach (var line in order.CargoLines) line.IsActive = false;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ helpers de resolución (lectura, sin tracking)

    private async Task<Client> ResolveClientForScopeAsync(OrderScope scope, Guid? clientPublicId, CancellationToken ct)
    {
        if (scope.ClientId is int scopedClientId)
            return await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == scopedClientId, ct) ?? throw new NotFoundException("Cliente");
        if (clientPublicId is null) throw new ValidationException("clientPublicId", "El cliente es obligatorio.");
        return await db.ResolveClientAsync(clientPublicId.Value, ct);
    }

    /// <summary>Dado de baja → 409 (ajuste A: solo se consulta su historial); SUSPENDED → 422 (DECISIÓN 16).</summary>
    private async Task EnsureClientCanTakeOrdersAsync(Client client, CancellationToken ct)
    {
        if (!client.IsActive) throw new ConflictException(ClientInactiveMessage);
        var suspendedId = await db.StatusIdAsync(StatusDomains.ClientStatus, ClientStatuses.Suspended, ct);
        if (client.StatusCodeId == suspendedId) throw new StatusRuleException(ClientSuspendedMessage);
    }

    private async Task<Tenant> LoadTenantAsync(int tenantId, CancellationToken ct)
        => await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct) ?? throw new NotFoundException("Tenant");

    private async Task<int> ResolveServiceTypeAsync(string? code, Tenant tenantRow, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var c = code.Trim().ToUpperInvariant();
            return await TryGetEnabledLookupIdAsync(LookupDomains.ServiceType, c, ct)
                   ?? throw new ValidationException("serviceType", $"Tipo de servicio desconocido: {c}.");
        }
        return tenantRow.DefaultServiceTypeLookupId ?? throw new ValidationException("serviceType", "El tipo de servicio es obligatorio.");
    }

    private async Task<int?> ResolvePriorityAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpperInvariant();
        return await TryGetEnabledLookupIdAsync(LookupDomains.OrderPriority, c, ct)
               ?? throw new ValidationException("priority", $"Prioridad desconocida: {c}.");
    }

    /// <summary>
    /// Exactamente uno: PublicId del directorio (del cliente o compartido; 404/400) o consignatario nuevo. El nuevo se compara
    /// primero contra las Location activas DELIVERY/BOTH del propio cliente por nombre + línea 1 (las compartidas no se consideran
    /// para no reasignar direcciones del tenant); si coincide se reutiliza sin sobreescribir el directorio; si no, se creará
    /// dentro de la transacción asignada al cliente de la orden.
    /// </summary>
    private async Task<ConsigneeInput> ResolveConsigneeInputAsync(Client client, Guid? locationPublicId, OrderConsigneeCreateRequest? fresh, CancellationToken ct)
    {
        if (locationPublicId is null && fresh is null) throw new ValidationException("consignee", ConsigneeRequiredMessage);
        if (locationPublicId is not null && fresh is not null) throw new ValidationException("consignee", ConsigneeBothMessage);

        if (locationPublicId is Guid pid)
        {
            var loc = await db.ResolveConsigneeAsync(client.ClientId, pid, ct);
            return new ConsigneeInput(loc, null, loc.Name);
        }

        var name = RequireText(fresh!.Name, "newConsignee.name", "El nombre del consignatario es obligatorio.", 200);
        var line1 = RequireText(fresh.Line1, "newConsignee.line1", "La dirección (línea 1) del consignatario es obligatoria.", 200);
        var city = RequireText(fresh.City, "newConsignee.city", "La ciudad del consignatario es obligatoria.", 100);

        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Delivery, ct);
        var bothTypeId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Both, ct);
        var clientId = client.ClientId;
        var candidates = await db.Locations.AsNoTracking()
            .Where(l => l.ClientId == clientId && l.IsActive && (l.LocationTypeLookupId == deliveryTypeId || l.LocationTypeLookupId == bothTypeId))
            .OrderBy(l => l.LocationId)
            .Select(l => new { l.LocationId, l.Name, l.Line1 })
            .ToListAsync(ct);
        var matchId = ConsigneeMatch.FindMatch(candidates.Select(c => (c.LocationId, c.Name, c.Line1)), name, line1);
        if (matchId is int id)
        {
            var match = await db.Locations.AsNoTracking().FirstAsync(l => l.LocationId == id, ct);
            return new ConsigneeInput(match, null, match.Name);
        }

        // País ISO alfa-2: el snapshot de la parada es CHAR(2) (sin esto, un código largo termina en 500 por truncado).
        var country = string.IsNullOrWhiteSpace(fresh.Country) ? OrderRules.DefaultCountryCode : fresh.Country.Trim().ToUpperInvariant();
        if (!CountryCode.IsValid(country)) throw new ValidationException("newConsignee.country", CountryCode.InvalidMessage);
        var toCreate = new LocationUpsertRequest(
            ClientPublicId: client.PublicId,
            Code: string.IsNullOrWhiteSpace(fresh.Code) ? null : fresh.Code.Trim(),
            Name: name,
            LocationType: LocationTypes.Delivery,
            Line1: line1,
            Line2: fresh.Line2,
            City: city,
            State: fresh.State,
            PostalCode: fresh.PostalCode,
            Country: country,
            DefaultServiceMinutes: fresh.DefaultServiceMinutes,
            DefaultWindowStart: null,
            DefaultWindowEnd: null,
            AccessNotes: null,
            DeliveryNotes: fresh.DeliveryNotes,
            AllowDupInvoice: fresh.AllowDupInvoice);
        return new ConsigneeInput(null, toCreate, name);
    }

    private async Task<SpecialService> ResolveSpecialServiceAsync(Client client, int? specialServiceId, CancellationToken ct)
    {
        if (specialServiceId is null) throw new ValidationException("specialServiceId", SpecialServiceRequiredMessage);
        // Componente 5 del modelo de facturación apagado → el cliente no tiene servicios especiales (L221/L223/L229).
        var contract = await db.CurrentContractAsync(client.ClientId, Today(), ct);
        if (contract is null || !contract.BillSpecialServices)
            throw new ConflictException(OrderQuoteService.SpecialServiceComponentOffMessage);
        var clientId = client.ClientId;
        var id = specialServiceId.Value;
        var row = await db.SpecialServices.AsNoTracking().Include(s => s.Type)
            .FirstOrDefaultAsync(s => s.SpecialServiceId == id && s.ClientId == clientId && s.IsActive, ct);
        if (row is null || row.Type is null || !EffectiveDated.IsCurrentOn(row, Today()))
            throw new ValidationException("specialServiceId", SpecialServiceNotCurrentMessage);
        return row;
    }

    /// <summary>
    /// Id de un valor de catálogo tecleado en la captura, o null si no existe, está inactivo o el tenant lo deshabilitó con su
    /// override (L54/L1158: un valor desactivado deja de aparecer al capturar registros nuevos). La caché de LookupCode es global;
    /// LookupCodeOverride es ITenantScoped y el filtro global lo limita al tenant del JWT. Los defaults del tenant y las órdenes
    /// existentes no pasan por aquí.
    /// </summary>
    private async Task<int?> TryGetEnabledLookupIdAsync(string domain, string code, CancellationToken ct)
    {
        var id = await lookups.TryGetIdAsync(domain, code, ct);
        if (id is not int v) return null;
        var row = await lookups.GetAsync(v, ct);
        if (row is null || !row.IsActive) return null;
        var disabled = await db.LookupCodeOverrides.AsNoTracking().AnyAsync(o => o.LookupCodeId == v && !o.IsEnabled, ct);
        return disabled ? null : v;
    }

    private async Task<List<PreparedLine>> PrepareLinesAsync(IList<OrderPackageLineRequest> packages, Tenant tenantRow, CancellationToken ct)
    {
        var result = new List<PreparedLine>(packages.Count);
        for (var i = 0; i < packages.Count; i++)
        {
            var p = packages[i];
            int typeId;
            if (!string.IsNullOrWhiteSpace(p.PackageType))
            {
                var code = p.PackageType.Trim().ToUpperInvariant();
                typeId = await TryGetEnabledLookupIdAsync(LookupDomains.PackageType, code, ct)
                         ?? throw new ValidationException($"packages[{i}].packageType", $"Tipo de paquete desconocido: {code}.");
            }
            else
            {
                typeId = tenantRow.DefaultPackageTypeLookupId ?? throw new ValidationException($"packages[{i}].packageType", "El tipo de paquete es obligatorio.");
            }
            var typeRow = await lookups.GetAsync(typeId, ct);
            var label = MultilingualText.Resolve(typeRow?.LabelJson, tenant.Lang);
            var description = string.IsNullOrWhiteSpace(p.Description) ? (string.IsNullOrEmpty(label) ? typeRow?.InternalCode ?? "Paquete" : label) : p.Description.Trim();
            var typedNumber = NormalizeTyped(p.PackageNumber, $"packages[{i}].packageNumber");
            result.Add(new PreparedLine(typeId, description, p.Pieces, p.WeightKg, p.VolumeM3, typedNumber));
        }
        return result;
    }

    private async Task<List<PreparedReference>> PrepareReferencesAsync(IList<OrderReferenceRequest>? references, CancellationToken ct)
    {
        var result = new List<PreparedReference>();
        if (references is null) return result;
        for (var i = 0; i < references.Count; i++)
        {
            var r = references[i];
            var code = RequireText(r.RefType, $"references[{i}].refType", "El tipo de referencia es obligatorio.", 40).ToUpperInvariant();
            var typeId = await TryGetEnabledLookupIdAsync(LookupDomains.OrderRefType, code, ct)
                         ?? throw new ValidationException($"references[{i}].refType", $"Tipo de referencia desconocido: {code}.");
            var value = RequireText(r.Value, $"references[{i}].value", "El valor de la referencia es obligatorio.", OrderRules.ReferenceValueMaxLength);
            var source = OptionalText(r.Source);
            if (source is not null && source.Length > OrderRules.ReferenceSourceMaxLength)
                throw new ValidationException($"references[{i}].source", $"Máximo {OrderRules.ReferenceSourceMaxLength} caracteres.");
            result.Add(new PreparedReference(typeId, value, source));
        }
        return result;
    }

    /// <summary>
    /// R36: órdenes activas del tenant (cualquier estatus, cualquier scope) con la misma factura y una parada DELIVERY sobre
    /// el mismo consignatario. Blocked → 409 duplicate_invoice; NeedsConfirmation → 409 duplicate_invoice_confirmable.
    /// La regla abarca todo el tenant (DECISIÓN 19: corre igual para operador y portal), pero si el scope fija un cliente y la
    /// orden existente es de otro, el error no nombra esa orden ni expone su PublicId (sin oráculo entre clientes).
    /// </summary>
    private async Task CheckDuplicateInvoiceAsync(Location consignee, string invoice, bool confirmed, OrderScope scope, CancellationToken ct, int? excludeOrderId = null)
    {
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var locationId = consignee.LocationId;
        var existing = await db.TransportOrders.AsNoTracking()
            .Where(o => o.IsActive && o.ClientInvoiceNumber == invoice
                        && (excludeOrderId == null || o.TransportOrderId != excludeOrderId)
                        && db.OrderStops.Any(s => s.TransportOrderId == o.TransportOrderId && s.StopTypeLookupId == deliveryTypeId && s.LocationId == locationId))
            .OrderByDescending(o => o.TransportOrderId)
            .Select(o => new { o.OrderNumber, o.PublicId, o.ClientId })
            .FirstOrDefaultAsync(ct);

        var decision = OrderRules.DecideDuplicateInvoice(true, existing?.OrderNumber, consignee.AllowDupInvoice, confirmed);
        if (decision == DuplicateInvoiceDecision.Ok) return;
        var foreign = OrderRules.IsForeignDuplicate(scope.ClientId, existing!.ClientId);
        var existingNumber = foreign ? null : existing!.OrderNumber;
        Guid? existingPublicId = foreign ? null : existing!.PublicId;
        switch (decision)
        {
            case DuplicateInvoiceDecision.Blocked:
                throw new DuplicateInvoiceException(
                    foreign
                        ? DuplicateInvoiceBlockedForeignMessage(consignee.Name, invoice)
                        : $"El consignatario '{consignee.Name}' no permite facturas repetidas: la orden {existingNumber} ya usa el número {invoice}.",
                    confirmable: false, existingNumber, existingPublicId);
            case DuplicateInvoiceDecision.NeedsConfirmation:
                throw new DuplicateInvoiceException(
                    foreign
                        ? DuplicateInvoiceConfirmableForeignMessage(consignee.Name, invoice)
                        : $"El consignatario '{consignee.Name}' ya tiene la factura {invoice} en la orden {existingNumber}; confirme si desea crear la orden de todos modos.",
                    confirmable: true, existingNumber, existingPublicId);
        }
    }

    /// <summary>R36 contra una orden de otro cliente con el scope fijado (portal): mensaje sin nombrar la orden existente.</summary>
    public static string DuplicateInvoiceBlockedForeignMessage(string consigneeName, string invoice)
        => $"El consignatario '{consigneeName}' no permite facturas repetidas: el número {invoice} ya está en uso.";

    /// <summary>R36 confirmable contra una orden de otro cliente con el scope fijado (portal): mensaje sin nombrar la orden existente.</summary>
    public static string DuplicateInvoiceConfirmableForeignMessage(string consigneeName, string invoice)
        => $"El consignatario '{consigneeName}' ya tiene la factura {invoice}; confirme si desea crear la orden de todos modos.";

    private async Task BornStopAsync(OrderStop stop, string initialCode, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.StopStatus, EntityTypes.OrderStop, stop.OrderStopId, null, initialCode, null, ct);
        stop.StatusCodeId = to.StatusCodeId;
    }

    /// <summary>Código de país de una Location para el snapshot CHAR(2); un código del catálogo que no sea alfa-2 → 400.</summary>
    private async Task<string?> CountryCodeAsync(int countryLookupId, CancellationToken ct)
    {
        var code = (await lookups.GetAsync(countryLookupId, ct))?.InternalCode;
        if (code is not null && !CountryCode.IsValid(code)) throw new ValidationException("country", CountryCode.InvalidMessage);
        return code;
    }

    // ================================================================ helpers puros

    private static void RejectExtra(IEnumerable<string>? extraKeys, IEnumerable<string> forbidden)
    {
        if (OrderRules.RejectExtraFields(extraKeys, forbidden) is (string field, string message))
            throw new ValidationException(field, message);
    }

    private static void ThrowIfErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.Count == 0) return;
        throw new ValidationException(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
    }

    private static IReadOnlyList<PackageLineInput> ToInputs(IList<OrderPackageLineRequest>? packages)
        => packages is null
            ? Array.Empty<PackageLineInput>()
            : packages.Select(p => new PackageLineInput(p.Description, p.Pieces, p.WeightKg, p.VolumeM3)).ToList();

    /// <summary>Tecleado permitido → valor recortado; no permitido para este cliente → 400 con el mensaje exacto de NumberingRules.</summary>
    private static string? EnsureTyped(string kind, NumberingSettings settings, string? typed, string field)
    {
        try { return NumberingRules.EnsureTypedAllowed(kind, settings, typed); }
        catch (ArgumentException ex) { throw new ValidationException(field, ex.Message); }
    }

    /// <summary>
    /// Número automático con el patrón efectivo; si no cabe en las columnas NVARCHAR(40) (patrón largo + consecutivo con más
    /// dígitos que '#') → 409 con mensaje de negocio en vez de un 500 por truncado. Se lanza dentro de la transacción del
    /// alta/edición: el rollback devuelve también los consecutivos ya dibujados.
    /// </summary>
    private static string ResolveAuto(string kind, NumberingSettings settings, long seq)
    {
        try { return NumberingRules.ResolveChecked(kind, NumberingRules.EffectivePattern(kind, settings), seq); }
        catch (ArgumentException ex) { throw new ConflictException(ex.Message); }
    }

    private static string? NormalizeTyped(string? value, string field)
    {
        try { return NumberingRules.NormalizeTyped(value); }
        catch (ArgumentException ex) { throw new ValidationException(field, ex.Message); }
    }

    private static string RequireText(string? value, string field, string message, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException(field, message);
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"Máximo {max} caracteres.");
        return v;
    }

    private static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SqlMessage(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql) return sql.Message;
        return ex.InnerException?.Message ?? ex.Message;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
