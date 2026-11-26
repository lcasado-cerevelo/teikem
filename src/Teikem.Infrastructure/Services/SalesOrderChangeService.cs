using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 249 (Lote E) — ORQUESTACIÓN de la «Modificación de la orden de venta»: todo cambio a una orden ya solicitada (cantidad, precio, agregar y quitar líneas,
/// IVU manual, términos, notas) o la orden interna de ADICIÓN pasa por aquí y queda en dbo.SalesOrderChange con quién la registró, quién la autorizó en el
/// cliente y cómo, el motivo, el adjunto, la foto de antes y después, los efectos y los totales.
/// <para>
/// Dos pasadas. (a) Sin transacción: valida la autorización y el adjunto (de la misma orden y sin usar), el contacto (del cliente de la orden), planea con las
/// reglas puras (SalesChangeRules.Plan), exige los permisos que tocan (sales.tax, sales.price contra el precio de hoy, warehouse.reopen-pack), proyecta los totales
/// sin escribir, revisa el crédito si el total sube (excedido = aviso guardado y 422; orders.credit_override lo autoriza) y decide si pide segunda aprobación.
/// (b) En una transacción: candados en el orden del lote (recolecciones → cabecera → líneas), vuelve a leer y a planear (si cambiaron las recolecciones: 409),
/// una sola pendiente por orden (409; UX_SalesOrderChange_Pending es la red), ChangeNo = máximo + 1 y, sin aprobación, aplica en este orden: reabre los empaques,
/// deshace SOLO las recolecciones de la reversa mínima, aplica las operaciones (SalesOrderService.ApplyOpsAsync), vuelve a revisar el crédito con los totales
/// reales, guarda la foto real y avisa «Orden de venta modificada».
/// </para>
/// Con segunda aprobación no se aplica nada hasta que otra persona (sales.modify.approve) la aprueba; al aprobar se compara la huella de la orden (409 si cambió).
/// Las transiciones de la modificación van por StatusService (PENDING_APPROVAL → APPLIED | REJECTED | WITHDRAWN); sus efectos son explícitos (sin IStatusTransitionEffect).
/// </summary>
public sealed class SalesOrderChangeService(
    TeikemDbContext db,
    ITenantContext tenant,
    SalesOrderService sales,
    PickBatchService batches,
    PickGroupService groups,
    StatusService statuses,
    PermissionService permissions,
    StoredFileService files,
    INumberSequenceService numbers,
    ILookupCache lookups,
    ITenantClock clock,
    ISecurityEventWriter security,
    INotificationPublisher? notifications = null)
{
    public const int MaxPageSize = 200;

    // ---------------------------------------------------------------- mensajes (públicos: el manual y la FAQ los citan)

    /// <summary>409: entre la vista previa y el candado cambiaron las recolecciones de la orden (otro despachó, empacó o deshizo).</summary>
    public const string OrderChangedConcurrently = "La orden cambió mientras se modificaba; vuelva a intentar.";
    /// <summary>409: la orden ya tiene una modificación esperando la segunda aprobación.</summary>
    public static string PendingExists(int changeNo) => $"La orden ya tiene una modificación por aprobar (n.º {changeNo}): apruébela, recházela o retírela antes de registrar otra.";
    public const string PendingExistsGeneric = "La orden ya tiene una modificación por aprobar: apruébela, recházela o retírela antes de registrar otra.";
    /// <summary>403: la segunda aprobación la da otra persona.</summary>
    public const string SelfApproval = "La segunda aprobación la da otra persona: quien registró la modificación no puede aprobarla.";
    /// <summary>409: al aprobar, la orden ya no es la que se modificó (se despachó, se facturó, se empacó o se modificó).</summary>
    public const string FingerprintChanged = "La orden cambió desde que se registró la modificación (se despachó, se facturó, se empacó o se modificó): recházela y registre otra.";
    /// <summary>409: la modificación ya se decidió.</summary>
    public const string NotPending = "La modificación ya no está pendiente de aprobar.";
    public const string RejectNoteRequired = "Indique por qué se rechaza la modificación.";
    public static readonly string DecisionNoteTooLong = $"La nota de la decisión no puede pasar de {SalesChangeRules.MaxDecisionNote} caracteres.";
    /// <summary>403: retirar una modificación pendiente.</summary>
    public const string WithdrawNotAllowed = "Solo quien registró la modificación o quien puede modificar órdenes la retira.";
    /// <summary>409: el archivo ya respalda otra modificación.</summary>
    public const string AttachmentUsed = "Ese archivo ya respalda otra modificación: adjunte otro.";
    public const string AttachmentFileRequired = "Adjunte el archivo de la evidencia (imagen o PDF).";
    public const string AdditionLinesRequired = "Indique los productos de la adición.";
    public const string SettingsModeInvalid = "El modo de segunda aprobación debe ser NONE, ALWAYS o ABOVE_AMOUNT.";
    public const string SettingsAmountRequired = "Con «Arriba de un monto» (ABOVE_AMOUNT) indique un monto mayor que 0.";
    public const string SettingsAmountDecimals = "El monto de la segunda aprobación admite hasta 2 decimales.";
    public const string ChangeNotFoundWhat = "Modificación";

    /// <summary>Por qué pide aprobación (variable {approvalRule} del aviso).</summary>
    public static string ApprovalRuleText(string? rule, decimal? amount)
        => rule == SalesChangeApprovalModes.AboveAmount
            ? $"el cambio pasa de {Money(amount ?? 0m)}"
            : "toda modificación de este cliente pide aprobación";

    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- formas guardadas (RequestJson / EffectsJson)

    /// <summary>La solicitud ya normalizada que se guarda en RequestJson (al aprobar se vuelve a aplicar tal cual).</summary>
    internal sealed record StoredRequest(
        List<ChangeOp> Ops,
        SalesChangeTermsInput? Terms,
        string? Notes,
        bool ReopenPack,
        string? ReopenReason,
        bool AllowBackorder,
        bool OverrideCredit,
        int? AdditionParentId = null,
        List<SalesOrderLineRequest>? AdditionLines = null,
        string? AdditionNotes = null);

    /// <summary>Los efectos en el almacén (EffectsJson).</summary>
    internal sealed record StoredEffects(List<string> ReversedBatches, List<string> ReopenedPacks, string WarehouseImpact);

    // ================================================================ opciones de edición

    /// <summary>
    /// La etapa de la orden y sus bloqueos, las recolecciones con su etapa, la posición por línea, las facturas de la orden y de sus adiciones (con su estado de
    /// QuickBooks), los ajustes vigentes, la modificación pendiente, si solo admite adición y los permisos del usuario. 404 si la orden no es de la compañía.
    /// </summary>
    public async Task<SalesOrderEditOptionsDto> EditOptionsAsync(Guid orderPublicId, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var load = await sales.LoadChangeStateAsync(orderId, ct);
        var order = load.Order;
        var classified = SalesChangeRules.ClassifyAll(load.State).ToDictionary(x => x.Batch.BatchId, x => x.Class);
        var positions = SalesChangeRules.LinePositions(load.State);
        var blocker = SalesChangeRules.OrderBlocker(load.State);
        var lines = order.Lines.ToDictionary(l => l.SalesOrderLineId);
        var productIds = load.Products.Keys.ToList();
        var productNames = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.Name, ct);

        var lineDtos = positions.Select(p =>
        {
            var l = lines[p.LineId];
            var prod = load.Products.GetValueOrDefault(l.ProductId);
            return new SalesChangeLinePositionDto(p.LineId, p.LineNo, prod?.PublicId ?? Guid.Empty, p.Sku, productNames.GetValueOrDefault(l.ProductId) ?? prod?.Name ?? string.Empty,
                p.QtyOrdered, p.MinQuantity, p.MinQuantityWithReopen, p.InCollected, p.InPacked, p.Out, p.Invoiced, p.IsRemoved, p.LockedReason, p.PriceLockedReason,
                l.UnitPrice, l.TaxRatePct, l.TaxReason == Teikem.Domain.Tax.TaxReasons.ManualOverride, l.Components.Count > 0);
        }).ToList();
        var batchDtos = load.Batches.Select(b =>
        {
            var state = load.State.Batches.First(s => s.BatchId == b.BatchId);
            var cls = classified.GetValueOrDefault(b.BatchId) ?? new ChangeBatchClassification(b.BatchId, ChangeBatchStages.Left, null);
            return new SalesChangeBatchDto(b.PublicId, b.Number, b.StatusCode, cls.Stage, cls.Reason, state.PackNumber, b.GroupPublicId, b.GroupNumber, b.ShipmentPublicId, b.ShipmentNumber);
        }).ToList();
        var invoices = load.Invoices.Select(i => new SalesOrderInvoiceRefDto(i.PublicId, i.Number, i.Status, i.QuickBooksState, i.IssueDate, i.Total, i.OrderPublicId, i.OrderNumber, i.IsAddition)).ToList();

        var pendingRow = await db.Set<SalesOrderChange>().AsNoTracking().FirstOrDefaultAsync(c => c.SalesOrderId == orderId && c.IsPending, ct);
        var pending = pendingRow is null ? null : (await ToItemsAsync(new List<SalesOrderChange> { pendingRow }, ct))[0];
        var perms = new SalesChangePermissionsDto(
            await permissions.HasPermissionAsync(PermissionCatalog.SalesModify, ct),
            await permissions.HasPermissionAsync(PermissionCatalog.SalesPrice, ct),
            await permissions.HasPermissionAsync(PermissionCatalog.SalesTax, ct),
            await permissions.HasPermissionAsync(PermissionCatalog.WarehouseReopenPack, ct),
            await permissions.HasPermissionAsync(PermissionCatalog.SalesModifyApprove, ct),
            await permissions.HasPermissionAsync(OrderStatusService.CreditOverridePermission, ct));
        var rootId = order.ParentSalesOrderId;
        var root = rootId is int rid
            ? await db.Set<SalesOrder>().AsNoTracking().Where(o => o.SalesOrderId == rid).Select(o => new { o.PublicId, o.Number }).FirstOrDefaultAsync(ct)
            : null;
        var dto = new SalesOrderEditOptionsDto(order.PublicId, order.Number, load.StatusCode, blocker,
            blocker is SalesChangeRules.Billed or SalesChangeRules.OrderLeftComplete,
            load.StatusCode is SalesOrderStatuses.Requested or SalesOrderStatuses.Partial or SalesOrderStatuses.Shipped,
            lineDtos, batchDtos, invoices, Effective(load.Settings), pending, perms, load.BillingTrigger,
            SalesChangeSnapshot.OrderTotal(order.Subtotal, order.TaxAmount, order.ShippingCharge, order.ShippingTaxAmount),
            RowVersionOf(order), order.ParentSalesOrderId is not null, root?.PublicId, root?.Number);
        db.ChangeTracker.Clear();   // solo lectura: la orden se cargó rastreada para las reglas
        return dto;
    }

    // ================================================================ vista previa (no escribe)

    /// <summary>
    /// Lo que pasaría con la modificación, SIN escribir nada: antes, después proyectado, diferencia, efectos (recolecciones que deshace la reversa mínima y empaques
    /// que se reabren), crédito, si pide segunda aprobación, si exige adjunto y los bloqueos (texto exacto).
    /// </summary>
    public async Task<SalesOrderChangePreviewDto> PreviewAsync(Guid orderPublicId, SalesOrderChangeRequest? req, CancellationToken ct)
    {
        req ??= new SalesOrderChangeRequest();
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var stored = await StoredOfAsync(req, ct);
        var load = await sales.LoadChangeStateAsync(orderId, ct);
        try
        {
            var blockers = new List<string>();
            if (PreValidationError(stored.Ops) is { } pre) blockers.Add(pre.Message);
            var plan = SalesChangeRules.Plan(load.State, stored.Ops, stored.ReopenPack);
            blockers.AddRange(plan.Blockers);
            SalesChangeProjection projection;
            if (blockers.Count == 0)
            {
                try { projection = await sales.ProjectAsync(load, Input(stored, 0), ct); }
                catch (TeikemException ex) when (ex is ValidationException or StatusRuleException or NotFoundException)
                {
                    blockers.Add(ex.Message);
                    projection = await NoChangeProjectionAsync(load, ct);
                }
            }
            else projection = await NoChangeProjectionAsync(load, ct);

            var credit = await sales.EvaluateCreditForChangeAsync(load.Order, projection.TotalBefore, projection.TotalAfter, notifyIfExceeded: false, ct);
            var rule = SalesChangeRules.ApprovalRule(load.Settings, projection.TotalDelta);
            var effects = EffectsOf(load, plan);
            return new SalesOrderChangePreviewDto(ToDto(projection.Before), ToDto(projection.After), ToDto(projection.Diff), SalesChangeSnapshot.Summary(projection.Diff), effects,
                credit.Credit, credit.Checked, credit.Checked && !credit.Ok, rule is not null, rule, load.Settings.AttachmentRequired, plan.NeedsReopen,
                blockers.Distinct().ToList(), projection.TotalBefore, projection.TotalAfter, projection.TotalDelta);
        }
        finally
        {
            db.ChangeTracker.Clear();   // nada de la vista previa se guarda
        }
    }

    private async Task<SalesChangeProjection> NoChangeProjectionAsync(SalesChangeOrderLoad load, CancellationToken ct)
    {
        var before = await sales.SnapshotAsync(load.Order, ct);
        return new SalesChangeProjection(before, before, SalesChangeSnapshot.Diff(before, before));
    }

    // ================================================================ registrar

    /// <summary>
    /// Registra la modificación. Sin segunda aprobación se APLICA en la misma llamada (estatus APPLIED, aviso «Orden de venta modificada»); con ella queda
    /// PENDING_APPROVAL sin tocar nada (aviso «Modificación por aprobar»). Errores: 400 (autorización, adjunto obligatorio, cantidades, precio, IVU), 403 (sales.tax,
    /// sales.price, warehouse.reopen-pack, orders.credit_override), 404 (orden, adjunto, contacto o producto de otra compañía), 409 (pendiente, la orden cambió,
    /// adjunto usado, RowVersion), 422 (bloqueos de la etapa, crédito excedido).
    /// </summary>
    public async Task<SalesOrderChangeDto> CreateAsync(Guid orderPublicId, SalesOrderChangeRequest? req, CancellationToken ct)
    {
        req ??= new SalesOrderChangeRequest();
        await permissions.EnsureAsync(PermissionCatalog.SalesModify, ct);
        var orderId = await OrderIdAsync(orderPublicId, ct);

        // ---- (a) sin transacción
        var load = await sales.LoadChangeStateAsync(orderId, ct);
        var order = load.Order;
        var (channelId, channelCode) = await ValidateAuthorizationAsync(req.AuthorizedByName, req.AuthorizedByTitle, req.AuthorizationChannel, req.AuthorizedAtUtc, req.Reason, order, ct);
        var contactId = await ValidateContactAsync(req.AuthorizedByContactId, order.ClientId, ct);
        var attachmentId = await ValidateAttachmentAsync(req.AttachmentFilePublicId, new[] { orderId }, ct);
        var stored = await StoredOfAsync(req, ct);
        if (PreValidationError(stored.Ops) is { } pre) throw new ValidationException(pre.Field, pre.Message);
        if (stored.ReopenPack && req.ReopenReason is not null && PickBatchRules.NormalizeReopenReason(req.ReopenReason).Error is { } re)
            throw new ValidationException("reopenReason", re);
        if (await PendingNoAsync(orderId, ct) is int pendingNo) throw new ConflictException(PendingExists(pendingNo));

        var plan = SalesChangeRules.Plan(load.State, stored.Ops, stored.ReopenPack);
        if (plan.IsBlocked) throw new StatusRuleException(plan.Blockers[0]);
        if (plan.NeedsTaxPermission) await permissions.EnsureAsync(PermissionCatalog.SalesTax, ct);
        await sales.EnsureChangePricePermissionAsync(load, stored.Ops, ct);
        if (plan.NeedsReopen) await batches.EnsureReopenPermissionAsync(ct);
        if (load.Settings.AttachmentRequired && attachmentId is null)
            throw new ValidationException("attachmentFilePublicId", SalesChangeRules.AttachmentRequired);

        var projection = await sales.ProjectAsync(load, Input(stored, 0), ct);
        if (projection.Diff.IsEmpty) throw new ValidationException("ops", SalesChangeRules.NothingToChange);
        var creditOverride = await CheckCreditAsync(order, projection.TotalBefore, projection.TotalAfter, stored.OverrideCredit, ct);
        var rule = SalesChangeRules.ApprovalRule(load.Settings, projection.TotalDelta);
        var baseBatchIds = load.ActiveBatchIds.ToList();
        db.ChangeTracker.Clear();

        // ---- (b) en una transacción (con reintento ante faltante: lo reintenta la estrategia de quien es dueño de la transacción)
        var changeId = await RunWithStockRetryAsync(ct2 => db.RunInTransactionAsync(async ct3 =>
        {
            var locked = await sales.LockForChangeAsync(orderId, ct3);
            EnsureRowVersion(locked.Order, req.RowVersion);
            if (!locked.ActiveBatchIds.SequenceEqual(baseBatchIds)) throw new ConflictException(OrderChangedConcurrently);
            var plan2 = SalesChangeRules.Plan(locked.State, stored.Ops, stored.ReopenPack);
            if (plan2.IsBlocked) throw new StatusRuleException(plan2.Blockers[0]);
            if (!plan2.ReverseBatchIds.SequenceEqual(plan.ReverseBatchIds) || !plan2.ReopenBatchIds.SequenceEqual(plan.ReopenBatchIds)
                || !plan2.ReopenGroupIds.SequenceEqual(plan.ReopenGroupIds))
                throw new ConflictException(OrderChangedConcurrently);
            if (await PendingNoAsync(orderId, ct3) is int pn) throw new ConflictException(PendingExists(pn));

            var proj = await sales.ProjectAsync(locked, Input(stored, 0), ct3);
            var fingerprint = SalesChangeSnapshot.Fingerprint(locked.State, proj.Before);
            var change = await NewChangeAsync(locked.Order, SalesChangeKinds.Modify, req.AuthorizedByName!, req.AuthorizedByTitle, contactId, channelId, req.AuthorizedAtUtc,
                req.Reason!, attachmentId, stored, proj, fingerprint, rule, creditOverride, ct3);
            change.HasPriceChange = plan2.PriceChangedLineIds.Count > 0;
            change.HasTaxOverride = stored.Ops.Any(o => o.Kind is ChangeOpKinds.Tax or ChangeOpKinds.ClearTax);
            change.HasTermsChange = stored.Ops.Any(o => o.Kind == ChangeOpKinds.Terms);
            await db.SaveGuardedAsync(PendingExistsGeneric, ct3);

            if (rule is not null)
            {
                change.IsPending = true;
                await db.SaveGuardedAsync(PendingExistsGeneric, ct3);
                await NotifyApprovalPendingAsync(locked.Order, change, rule, load.Settings.ApprovalAmount, ct3);
                await db.SaveGuardedAsync(PendingExistsGeneric, ct3);
                return change.SalesOrderChangeId;
            }
            await ApplyCoreAsync(change.SalesOrderChangeId, orderId, locked, plan2, stored, change.ChangeNo, change.Reason, creditOverride, null, ct3);
            return change.SalesOrderChangeId;
        }, ct2), ct);

        if (creditOverride) await WriteCreditOverrideAsync(orderPublicId, changeId, ct);
        return await GetByIdAsync(changeId, ct);
    }

    /// <summary>
    /// Adaptador de la ruta vieja PATCH /sales-orders/{id}/lines/{lineId}: arma una modificación con una operación QTY y/o PRICE y los campos de autorización
    /// (sin ellos, 400 con los mensajes de la modificación) y devuelve la orden como antes.
    /// </summary>
    public async Task<SalesOrderDto> ModifyLineAsync(Guid orderPublicId, int lineId, SalesOrderLineModifyRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var ops = new List<SalesOrderChangeLineOp>();
        if (req.Quantity is not null) ops.Add(new SalesOrderChangeLineOp(ChangeOpKinds.Qty, lineId, Quantity: req.Quantity));
        if (req.UnitPrice is not null) ops.Add(new SalesOrderChangeLineOp(ChangeOpKinds.Price, lineId, UnitPrice: req.UnitPrice));
        await CreateAsync(orderPublicId, new SalesOrderChangeRequest(ops, req.AuthorizedByName, req.AuthorizedByTitle, null, req.AuthorizationChannel, req.AuthorizedAtUtc,
            req.Reason, AllowBackorder: req.AllowBackorder), ct);
        return await sales.GetAsync(orderPublicId, ct);
    }

    // ================================================================ aplicar (en la transacción de quien llama)

    /// <summary>
    /// Aplica una modificación MODIFY ya guardada: reabre los empaques, deshace SOLO las recolecciones de la reversa mínima (y desactiva la maestra que quede vacía),
    /// aplica las operaciones, vuelve a revisar el crédito con los totales reales, guarda la foto real, los efectos y los contadores, pasa a APPLIED y avisa
    /// «Orden de venta modificada». Las llamadas anidadas pueden limpiar el contexto: todo se vuelve a leer por id.
    /// </summary>
    private async Task ApplyCoreAsync(int changeId, int orderId, SalesChangeOrderLoad locked, ChangePlan plan, StoredRequest stored, int changeNo, string reason,
        bool creditOverride, string? approvedByName, CancellationToken ct)
    {
        var before = await sales.SnapshotAsync(locked.Order, ct);
        var reopenReason = Trim500(stored.ReopenReason ?? $"{SalesOrderService.ChangeHistoryComment(changeNo)} de {locked.Order.Number}: {reason}");
        var reopened = new List<string>();
        var reversed = new List<string>();

        // 1) reabrir los empaques (maestras y recolecciones sueltas)
        foreach (var groupId in plan.ReopenGroupIds)
        {
            await groups.ReopenPackCoreAsync(groupId, reopenReason, ct);
            reopened.Add(locked.Batches.FirstOrDefault(b => b.GroupId == groupId)?.GroupNumber ?? groupId.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var batchId in plan.ReopenBatchIds)
        {
            await batches.ReopenPackCoreAsync(batchId, reopenReason, ct);
            reopened.Add(locked.Batches.First(b => b.BatchId == batchId).Number);
        }

        // 2) deshacer SOLO las recolecciones que escogió la reversa mínima (el inventario vuelve a su posición y se vuelve a reservar)
        var touchedGroups = new HashSet<int>();
        foreach (var batchId in plan.ReverseBatchIds)
        {
            var b = locked.Batches.First(x => x.BatchId == batchId);
            await batches.DeleteCoreAsync(b.PublicId, new PickBatchDeleteRequest(SalesOrderService.ChangeHistoryComment(changeNo)), orderHandled: false, ct);
            reversed.Add(b.Number);
            if (b.GroupId is int g) touchedGroups.Add(g);
        }
        foreach (var g in touchedGroups) await groups.DeactivateIfEmptyAsync(g, ct);

        // 3) las operaciones (el motor vuelve a leer la orden rastreada)
        var order = await sales.ApplyOpsAsync(orderId, Input(stored, changeNo), ct);

        // 4) el crédito con los totales reales
        var realTotal = SalesChangeSnapshot.OrderTotal(order.Subtotal, order.TaxAmount, order.ShippingCharge, order.ShippingTaxAmount);
        if (realTotal > before.Total && !creditOverride)
        {
            var credit = await sales.EvaluateCreditForChangeAsync(order, before.Total, realTotal, notifyIfExceeded: false, ct);
            if (credit.Checked && !credit.Ok) throw new CreditExceededException(credit.Check, CreditRules.Evaluate(credit.Check).Available);
        }

        // 5) la foto real del después, los efectos y los contadores
        var after = await sales.SnapshotAsync(order, ct);
        var diff = SalesChangeSnapshot.Diff(before, after);
        var impact = SalesOrderService.WarehouseImpactText(reversed, reopened);
        var change = await db.Set<SalesOrderChange>().AsTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct);
        change.AfterJson = after.ToJson();
        change.BeforeJson = before.ToJson();
        change.EffectsJson = SalesChangeJson.Serialize(new StoredEffects(reversed, reopened, impact));
        change.Summary = SalesChangeSnapshot.Summary(diff);
        change.TotalBefore = before.Total;
        change.TotalAfter = after.Total;
        change.TotalDelta = after.Total - before.Total;
        change.LinesAdded = diff.Added.Count;
        change.LinesRemoved = diff.Removed.Count;
        change.LinesChanged = diff.Changed.Count;
        change.ReversedBatchCount = reversed.Count;
        change.ReopenedPack = reopened.Count > 0;
        change.AppliedAtUtc = DateTime.UtcNow;
        var applied = await statuses.TransitionAsync(SalesChangeCodes.StatusDomain, SalesChangeCodes.EntityType, change.SalesOrderChangeId, change.StatusCodeId,
            SalesOrderChangeStatuses.Applied, approvedByName is null ? null : Trim500($"Aprobada por {approvedByName}"), ct);
        change.StatusCodeId = applied.StatusCodeId;
        change.IsPending = false;
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
        await sales.NotifyChangedAsync(order, change, impact, approvedByName, ct);
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
    }

    // ================================================================ segunda aprobación

    /// <summary>
    /// Aprueba (sales.modify.approve; otra persona que quien registró: 403). Mismos candados; si la huella de la orden cambió desde el registro → 409. Vuelve a
    /// planear y a revisar el crédito (OverrideCredit con orders.credit_override), reabre solo si todavía es posible y aplica; en una ADICIÓN crea la hija ahora.
    /// </summary>
    public async Task<SalesOrderChangeDto> ApproveAsync(Guid orderPublicId, Guid changePublicId, SalesOrderChangeDecisionRequest? req, CancellationToken ct)
    {
        req ??= new SalesOrderChangeDecisionRequest();
        await permissions.EnsureAsync(PermissionCatalog.SalesModifyApprove, ct);
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var change = await ChangeRowAsync(orderId, changePublicId, ct);
        if (!change.IsPending) throw new ConflictException(NotPending);
        if (tenant.UserId is int me && change.RegisteredBy == me) throw new ForbiddenException(SelfApproval);
        var note = NormalizeNote(req.Note, required: false);
        var stored = SalesChangeJson.Deserialize<StoredRequest>(change.RequestJson) ?? throw new ConflictException(NotPending);
        var overrideCredit = req.OverrideCredit == true;
        var approvedBy = await CurrentUserNameAsync(ct);
        var changeId = change.SalesOrderChangeId;
        db.ChangeTracker.Clear();

        if (change.Kind == SalesChangeKinds.Addition)
        {
            // si quien registró ya autorizó el crédito, no se le vuelve a pedir a quien aprueba
            var creditOverride = change.CreditOverride || await AdditionCreditAsync(stored, change.SalesOrderId, overrideCredit, ct);
            await numbers.EnsureAsync(SalesCodes.NumberKind, null, ct);
            await RunWithStockRetryAsync(ct2 => db.RunInTransactionAsync(async ct3 =>
            {
                await db.LockSalesOrderAsync(change.SalesOrderId, ct3);
                var row = await db.Set<SalesOrderChange>().AsTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct3);
                if (!row.IsPending) throw new ConflictException(NotPending);
                (row.DecidedBy, row.DecidedAtUtc, row.DecisionNote, row.CreditOverride) = (tenant.UserId, DateTime.UtcNow, note, row.CreditOverride || creditOverride);
                await db.SaveGuardedAsync(SalesRules.Concurrency, ct3);
                await CreateChildCoreAsync(changeId, stored, creditOverride, ct3);
                return 0;
            }, ct2), ct);
            if (creditOverride && !change.CreditOverride) await WriteCreditOverrideAsync(orderPublicId, changeId, ct);
            return await GetByIdAsync(changeId, ct);
        }

        // (a) sin transacción: el crédito con la orden de hoy (excedido = aviso guardado y 422)
        var load = await sales.LoadChangeStateAsync(orderId, ct);
        var projection = await sales.ProjectAsync(load, Input(stored, 0), ct);
        var creditOk = change.CreditOverride || await CheckCreditAsync(load.Order, projection.TotalBefore, projection.TotalAfter, overrideCredit, ct);
        db.ChangeTracker.Clear();

        await RunWithStockRetryAsync(ct2 => db.RunInTransactionAsync(async ct3 =>
        {
            var locked = await sales.LockForChangeAsync(orderId, ct3);
            var row = await db.Set<SalesOrderChange>().AsTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct3);
            if (!row.IsPending) throw new ConflictException(NotPending);
            var now = SalesChangeSnapshot.Fingerprint(locked.State, await sales.SnapshotAsync(locked.Order, ct3));
            if (!string.Equals(now, row.BaseFingerprint?.Trim(), StringComparison.OrdinalIgnoreCase)) throw new ConflictException(FingerprintChanged);
            var plan = SalesChangeRules.Plan(locked.State, stored.Ops, stored.ReopenPack);
            if (plan.IsBlocked) throw new StatusRuleException(plan.Blockers[0]);
            (row.DecidedBy, row.DecidedAtUtc, row.DecisionNote, row.CreditOverride) = (tenant.UserId, DateTime.UtcNow, note, row.CreditOverride || creditOk);
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct3);
            await ApplyCoreAsync(changeId, orderId, locked, plan, stored, row.ChangeNo, row.Reason, row.CreditOverride, approvedBy, ct3);
            return 0;
        }, ct2), ct);
        if (creditOk && !change.CreditOverride) await WriteCreditOverrideAsync(orderPublicId, changeId, ct);
        return await GetByIdAsync(changeId, ct);
    }

    /// <summary>Rechaza (sales.modify.approve; nota obligatoria): REJECTED, sin efectos.</summary>
    public async Task<SalesOrderChangeDto> RejectAsync(Guid orderPublicId, Guid changePublicId, SalesOrderChangeDecisionRequest? req, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesModifyApprove, ct);
        var note = NormalizeNote(req?.Note, required: true, RejectNoteRequired);
        return await DecideAsync(orderPublicId, changePublicId, SalesOrderChangeStatuses.Rejected, note, ct);
    }

    /// <summary>Retira (quien la registró o quien tenga sales.modify): WITHDRAWN, sin efectos.</summary>
    public async Task<SalesOrderChangeDto> WithdrawAsync(Guid orderPublicId, Guid changePublicId, SalesOrderChangeDecisionRequest? req, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var change = await ChangeRowAsync(orderId, changePublicId, ct);
        var mine = tenant.UserId is int me && change.RegisteredBy == me;
        if (!mine && !await permissions.HasPermissionAsync(PermissionCatalog.SalesModify, ct)) throw new ForbiddenException(WithdrawNotAllowed);
        var note = NormalizeNote(req?.Note, required: false);
        return await DecideAsync(orderPublicId, changePublicId, SalesOrderChangeStatuses.Withdrawn, note, ct);
    }

    private async Task<SalesOrderChangeDto> DecideAsync(Guid orderPublicId, Guid changePublicId, string toCode, string? note, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var changeId = (await ChangeRowAsync(orderId, changePublicId, ct)).SalesOrderChangeId;
        db.ChangeTracker.Clear();
        await db.RunInTransactionAsync(async ct2 =>
        {
            await db.LockSalesOrderAsync(orderId, ct2);
            var row = await db.Set<SalesOrderChange>().AsTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct2);
            if (!row.IsPending) throw new ConflictException(NotPending);
            var to = await statuses.TransitionAsync(SalesChangeCodes.StatusDomain, SalesChangeCodes.EntityType, row.SalesOrderChangeId, row.StatusCodeId, toCode, note, ct2);
            (row.StatusCodeId, row.IsPending, row.DecidedBy, row.DecidedAtUtc, row.DecisionNote) = (to.StatusCodeId, false, tenant.UserId, DateTime.UtcNow, note);
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
        }, ct);
        return await GetByIdAsync(changeId, ct);
    }

    // ================================================================ historial y bandeja

    /// <summary>El historial de modificaciones de la orden (la más reciente primero).</summary>
    public async Task<IReadOnlyList<SalesOrderChangeListItemDto>> ListAsync(Guid orderPublicId, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var rows = await db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.SalesOrderId == orderId).OrderByDescending(c => c.ChangeNo).ToListAsync(ct);
        return await ToItemsAsync(rows, ct);
    }

    /// <summary>Una modificación con su foto de antes y después, la diferencia y los efectos. 404 si no es de esa orden (o de la compañía).</summary>
    public async Task<SalesOrderChangeDto> GetAsync(Guid orderPublicId, Guid changePublicId, CancellationToken ct)
    {
        var orderId = await OrderIdAsync(orderPublicId, ct);
        var row = await ChangeRowAsync(orderId, changePublicId, ct);
        return await ToDtoAsync(row, ct);
    }

    /// <summary>
    /// La bandeja «Por aprobar» de la compañía, paginada en el servidor (skip/take, tope 200). Buscador libre: número de orden, cliente, quién autorizó y motivo.
    /// Orden: registeredAt (más reciente primero, por omisión), -registeredAt, order, client, delta, -delta.
    /// </summary>
    public async Task<SalesChangePendingPageDto> PendingApprovalsAsync(SalesChangePendingQuery? query, CancellationToken ct)
    {
        query ??= new SalesChangePendingQuery();
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, MaxPageSize);
        var q = from c in db.Set<SalesOrderChange>().AsNoTracking()
                join o in db.Set<SalesOrder>().AsNoTracking() on c.SalesOrderId equals o.SalesOrderId
                join cl in db.Set<Client>().AsNoTracking() on o.ClientId equals cl.ClientId
                where c.IsPending
                select new { Change = c, o.Number, ClientName = cl.Name };
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var s = query.Q.Trim();
            q = q.Where(x => x.Number.Contains(s) || x.ClientName.Contains(s) || x.Change.AuthorizedByName.Contains(s) || x.Change.Reason.Contains(s));
        }
        var total = await q.CountAsync(ct);
        var sort = (query.Sort ?? string.Empty).Trim().ToLowerInvariant();
        q = sort switch
        {
            "-registeredat" => q.OrderBy(x => x.Change.RegisteredAtUtc).ThenBy(x => x.Change.SalesOrderChangeId),
            "order" => q.OrderBy(x => x.Number).ThenBy(x => x.Change.SalesOrderChangeId),
            "-order" => q.OrderByDescending(x => x.Number).ThenByDescending(x => x.Change.SalesOrderChangeId),
            "client" => q.OrderBy(x => x.ClientName).ThenBy(x => x.Change.SalesOrderChangeId),
            "-client" => q.OrderByDescending(x => x.ClientName).ThenByDescending(x => x.Change.SalesOrderChangeId),
            "delta" => q.OrderBy(x => x.Change.TotalDelta).ThenBy(x => x.Change.SalesOrderChangeId),
            "-delta" => q.OrderByDescending(x => x.Change.TotalDelta).ThenByDescending(x => x.Change.SalesOrderChangeId),
            _ => q.OrderByDescending(x => x.Change.RegisteredAtUtc).ThenByDescending(x => x.Change.SalesOrderChangeId),
        };
        var rows = await q.Skip(skip).Take(take).Select(x => x.Change).ToListAsync(ct);
        return new SalesChangePendingPageDto(await ToItemsAsync(rows, ct), total, skip, take);
    }

    // ================================================================ adjunto

    /// <summary>
    /// Sube la evidencia de la autorización (imagen o PDF) como StoredFile DOCUMENT de la ORDEN (sales.modify). Se refiere después en la modificación por su
    /// PublicId; un archivo respalda una sola modificación.
    /// </summary>
    public async Task<StoredFileDto> UploadAttachmentAsync(Guid orderPublicId, string? fileName, string? contentType, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesModify, ct);
        var orderId = await OrderIdAsync(orderPublicId, ct);
        if (content.Length == 0) throw new ValidationException("file", AttachmentFileRequired);
        return await files.AttachAsync(SalesCodes.EntityType, orderId, StoredFileCodes.KindDocument, fileName, contentType, content, ct, enforceOwnerWrite: false);
    }

    // ================================================================ orden interna de adición

    /// <summary>
    /// La orden interna de ADICIÓN de una orden solicitada, parcial o despachada: registra la modificación ADDITION en la raíz (la madre o, si la madre ya es una
    /// adición, su raíz) y, sin segunda aprobación, crea y solicita la hija en la misma transacción (CreateCoreAsync + el RequestAsync de siempre). El crédito
    /// proyectado de la hija se revisa ANTES de abrir la transacción (excedido = aviso y 422, sin hija ni registro).
    /// </summary>
    public async Task<SalesOrderChangeDto> CreateAdditionAsync(Guid parentPublicId, SalesOrderAdditionRequest? req, CancellationToken ct)
    {
        req ??= new SalesOrderAdditionRequest();
        await permissions.EnsureAsync(PermissionCatalog.SalesModify, ct);
        var parentId = await OrderIdAsync(parentPublicId, ct);
        var parent = await db.Set<SalesOrder>().AsNoTracking().FirstAsync(o => o.SalesOrderId == parentId, ct);
        var parentCode = await StatusCodeOfOrderAsync(parent.StatusCodeId, ct);
        if (parentCode is not (SalesOrderStatuses.Requested or SalesOrderStatuses.Partial or SalesOrderStatuses.Shipped))
            throw new StatusRuleException(SalesOrderService.AdditionParentNotOpen);
        var rootId = parent.ParentSalesOrderId ?? parent.SalesOrderId;
        var root = rootId == parentId ? parent : await db.Set<SalesOrder>().AsNoTracking().FirstAsync(o => o.SalesOrderId == rootId, ct);

        var (channelId, _) = await ValidateAuthorizationAsync(req.AuthorizedByName, req.AuthorizedByTitle, req.AuthorizationChannel, req.AuthorizedAtUtc, req.Reason, parent, ct);
        var contactId = await ValidateContactAsync(req.AuthorizedByContactId, parent.ClientId, ct);
        var attachmentId = await ValidateAttachmentAsync(req.AttachmentFilePublicId, new[] { parentId, rootId }, ct);
        var lines = (req.Lines ?? Array.Empty<SalesOrderLineRequest>()).Where(l => l is not null).ToList();
        if (lines.Count == 0) throw new ValidationException("lines", AdditionLinesRequired);
        foreach (var l in lines)
            if (SalesRules.QuantityError(l.Quantity) is { } qe) throw new ValidationException("quantity", qe);
        if (await PendingNoAsync(rootId, ct) is int pendingNo) throw new ConflictException(PendingExists(pendingNo));

        var notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        var stored = new StoredRequest(new List<ChangeOp>(), null, null, false, null, req.AllowBackorder == true, req.OverrideCredit == true, parentId, lines, notes);
        var rootLoad = await sales.LoadChangeStateAsync(rootId, ct);
        if (rootLoad.Settings.AttachmentRequired && attachmentId is null)
            throw new ValidationException("attachmentFilePublicId", SalesChangeRules.AttachmentRequired);
        var (childProjection, addOps) = await ProjectAdditionAsync(parent, rootLoad, lines, ct);
        await sales.EnsureChangePricePermissionAsync(rootLoad with { Order = SyntheticChild(parent) }, addOps, ct);
        var creditOverride = await CheckCreditAsync(SyntheticChild(parent), 0m, childProjection.TotalAfter, stored.OverrideCredit, ct);
        var rule = SalesChangeRules.ApprovalRule(rootLoad.Settings, childProjection.TotalAfter);
        var rootBefore = await sales.SnapshotAsync(rootLoad.Order, ct);
        var fingerprint = SalesChangeSnapshot.Fingerprint(rootLoad.State, rootBefore);
        db.ChangeTracker.Clear();
        await numbers.EnsureAsync(SalesCodes.NumberKind, null, ct);   // el contador OV, fuera de la transacción

        var changeId = await RunWithStockRetryAsync(ct2 => db.RunInTransactionAsync(async ct3 =>
        {
            var rootOrder = await db.LockSalesOrderAsync(rootId, ct3);
            if (await PendingNoAsync(rootId, ct3) is int pn) throw new ConflictException(PendingExists(pn));
            var proj = new SalesChangeProjection(rootBefore, childProjection.After, childProjection.Diff);
            var change = await NewChangeAsync(rootOrder, SalesChangeKinds.Addition, req.AuthorizedByName!, req.AuthorizedByTitle, contactId, channelId, req.AuthorizedAtUtc,
                req.Reason!, attachmentId, stored, proj, fingerprint, rule, creditOverride, ct3);
            (change.TotalBefore, change.TotalAfter, change.TotalDelta, change.LinesAdded) = (0m, childProjection.TotalAfter, childProjection.TotalAfter, lines.Count);
            change.Summary = AdditionSummary(childProjection.After);
            await db.SaveGuardedAsync(PendingExistsGeneric, ct3);   // el registro ADDITION se guarda ANTES de la hija
            if (rule is not null)
            {
                change.IsPending = true;
                await db.SaveGuardedAsync(PendingExistsGeneric, ct3);
                await NotifyApprovalPendingAsync(rootOrder, change, rule, rootLoad.Settings.ApprovalAmount, ct3);
                await db.SaveGuardedAsync(PendingExistsGeneric, ct3);
                return change.SalesOrderChangeId;
            }
            await CreateChildCoreAsync(change.SalesOrderChangeId, stored, creditOverride, ct3);
            return change.SalesOrderChangeId;
        }, ct2), ct);

        if (creditOverride) await WriteCreditOverrideAsync(parentPublicId, changeId, ct);
        return await GetByIdAsync(changeId, ct);
    }

    /// <summary>
    /// Crea y solicita la hija de una modificación ADDITION ya guardada (en la transacción de quien llama), pone AdditionSalesOrderId, la foto real de la hija y
    /// los totales, pasa a APPLIED y avisa «Adición a una orden de venta».
    /// </summary>
    private async Task CreateChildCoreAsync(int changeId, StoredRequest stored, bool creditOverride, CancellationToken ct)
    {
        var head = await db.Set<SalesOrderChange>().AsNoTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct);
        var parentId = stored.AdditionParentId ?? head.SalesOrderId;
        var parent = await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.SalesOrderId == parentId, ct)
                     ?? await db.Set<SalesOrder>().AsNoTracking().FirstAsync(o => o.SalesOrderId == head.SalesOrderId, ct);
        var comment = Trim500($"Adición registrada con la modificación n.º {head.ChangeNo}: {head.Reason}");
        var childPublicId = await sales.CreateAdditionCoreAsync(parent, stored.AdditionLines ?? new List<SalesOrderLineRequest>(), stored.AdditionNotes,
            stored.AllowBackorder, creditOverride, ct, comment);
        var child = await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.PublicId == childPublicId, ct);
        var childSnap = await sales.SnapshotAsync(child, ct);

        var change = await db.Set<SalesOrderChange>().AsTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct);
        change.AdditionSalesOrderId = child.SalesOrderId;
        change.AfterJson = childSnap.ToJson();
        (change.TotalBefore, change.TotalAfter, change.TotalDelta, change.LinesAdded) = (0m, childSnap.Total, childSnap.Total, childSnap.Lines.Count(l => !l.IsRemoved));
        change.Summary = AdditionSummary(childSnap, child.Number);
        change.AppliedAtUtc = DateTime.UtcNow;
        var applied = await statuses.TransitionAsync(SalesChangeCodes.StatusDomain, SalesChangeCodes.EntityType, change.SalesOrderChangeId, change.StatusCodeId,
            SalesOrderChangeStatuses.Applied, Trim500($"Adición {child.Number}"), ct);
        (change.StatusCodeId, change.IsPending) = (applied.StatusCodeId, false);
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);

        var root = await db.Set<SalesOrder>().AsNoTracking().FirstAsync(o => o.SalesOrderId == change.SalesOrderId, ct);
        if (notifications is not null)
        {
            var values = await BaseValuesAsync(root, ct);
            foreach (var kv in await sales.ChangeNoticeValuesAsync(change, ct)) values[kv.Key] = kv.Value;
            values["additionNumber"] = child.Number;
            values["additionPublicId"] = child.PublicId.ToString("N");
            await notifications.PublishAsync(SalesChangeEventCodes.AdditionCreated, values, $"ADD-{change.PublicId:N}", ct);
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
        }
    }

    /// <summary>La hija proyectada (sin escribir): una copia sin líneas de la madre con las líneas de la adición como ADD (precios de hoy, IVU y envío).</summary>
    private async Task<(SalesChangeProjection Projection, List<ChangeOp> Ops)> ProjectAdditionAsync(SalesOrder parent, SalesChangeOrderLoad rootLoad,
        IReadOnlyList<SalesOrderLineRequest> lines, CancellationToken ct)
    {
        var publicIds = lines.Select(l => l.ProductPublicId ?? throw new ValidationException("lines", SalesChangeRules.AddProductRequired)).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => publicIds.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var ops = lines.Select(l => products.TryGetValue(l.ProductPublicId!.Value, out var p)
            ? new ChangeOp(ChangeOpKinds.Add, ProductId: p.ProductId, Quantity: l.Quantity, UnitPrice: l.UnitPrice, Sku: p.Sku)
            : throw new NotFoundException("Producto")).ToList();
        var dup = ops.GroupBy(o => o.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new ValidationException("lines", SalesChangeRules.ProductRepeated(dup.First().Sku ?? string.Empty));
        var projection = await sales.ProjectAsync(rootLoad with { Order = SyntheticChild(parent) }, new SalesChangeApplyInput(ops, 0), ct);
        return (projection, ops);
    }

    /// <summary>Una copia sin líneas ni totales de la madre (SalesOrderId 0: la exposición de crédito incluye a la madre).</summary>
    private SalesOrder SyntheticChild(SalesOrder parent) => new()
    {
        SalesOrderId = 0, PublicId = parent.PublicId, Number = parent.Number, TenantId = parent.TenantId, ClientId = parent.ClientId, WarehouseId = parent.WarehouseId,
        DeliveryMode = parent.DeliveryMode, ConsigneeId = parent.ConsigneeId, PaymentMethodLookupId = parent.PaymentMethodLookupId, PaymentTermLookupId = parent.PaymentTermLookupId,
        IsConsignment = parent.IsConsignment, RequestedDate = clock.Today, StatusCodeId = parent.StatusCodeId,
    };

    private async Task<bool> AdditionCreditAsync(StoredRequest stored, int rootId, bool overrideCredit, CancellationToken ct)
    {
        var parentId = stored.AdditionParentId ?? rootId;
        var parent = await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.SalesOrderId == parentId, ct)
                     ?? await db.Set<SalesOrder>().AsNoTracking().FirstAsync(o => o.SalesOrderId == rootId, ct);
        var rootLoad = await sales.LoadChangeStateAsync(rootId, ct);
        var (projection, _) = await ProjectAdditionAsync(parent, rootLoad, stored.AdditionLines ?? new List<SalesOrderLineRequest>(), ct);
        var result = await CheckCreditAsync(SyntheticChild(parent), 0m, projection.TotalAfter, overrideCredit, ct);
        db.ChangeTracker.Clear();
        return result;
    }

    private static string AdditionSummary(SalesChangeSnapshot child, string? number = null)
    {
        var items = string.Join(", ", child.Lines.Where(l => !l.IsRemoved).Select(l => $"{l.Sku} ×{SalesChangeSnapshot.Txt(l.QtyOrdered)}"));
        var text = (number is null ? "Adición: " : $"Adición {number}: ") + items + string.Create(CultureInfo.InvariantCulture, $". Total {child.Total:0.00}.");
        return text.Length <= SalesChangeRules.MaxSummary ? text : text[..(SalesChangeRules.MaxSummary - 1)] + "…";
    }

    // ================================================================ ajustes

    /// <summary>Ajustes de la compañía: modo de segunda aprobación, monto y si el adjunto es obligatorio.</summary>
    public async Task<SalesChangeSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        return new SalesChangeSettingsDto(t.SalesChangeApprovalMode, t.SalesChangeApprovalAmount, t.SalesChangeAttachmentRequired);
    }

    /// <summary>Cambia los ajustes de la compañía (null = no cambia). 400 con el mensaje exacto si el modo o el monto no son válidos.</summary>
    public async Task<SalesChangeSettingsDto> UpdateSettingsAsync(SalesChangeSettingsRequest? req, CancellationToken ct)
    {
        req ??= new SalesChangeSettingsRequest();
        var t = await db.Tenants.AsTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        var mode = req.ApprovalMode is null ? t.SalesChangeApprovalMode : NormalizeMode(req.ApprovalMode) ?? throw new ValidationException("approvalMode", SettingsModeInvalid);
        var amount = req.ApprovalMode is null && req.ApprovalAmount is null ? t.SalesChangeApprovalAmount : req.ApprovalAmount ?? t.SalesChangeApprovalAmount;
        (t.SalesChangeApprovalMode, t.SalesChangeApprovalAmount) = (mode, ValidateAmount(mode, amount));
        if (req.AttachmentRequired is bool att) t.SalesChangeAttachmentRequired = att;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return new SalesChangeSettingsDto(t.SalesChangeApprovalMode, t.SalesChangeApprovalAmount, t.SalesChangeAttachmentRequired);
    }

    /// <summary>Ajustes de un cliente (null = hereda) y los vigentes. 404 si el cliente no es de la compañía.</summary>
    public async Task<SalesClientChangeSettingsDto> GetClientSettingsAsync(Guid clientPublicId, CancellationToken ct)
    {
        var c = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        return await ClientSettingsDtoAsync(c, ct);
    }

    /// <summary>Reemplaza los ajustes del cliente: modo "" o null = hereda (sin monto); adjunto null = hereda. 400 con el mensaje exacto si no son válidos.</summary>
    public async Task<SalesClientChangeSettingsDto> UpdateClientSettingsAsync(Guid clientPublicId, SalesClientChangeSettingsRequest? req, CancellationToken ct)
    {
        req ??= new SalesClientChangeSettingsRequest();
        var c = await db.Set<Client>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        string? mode = null;
        if (!string.IsNullOrWhiteSpace(req.ApprovalMode)) mode = NormalizeMode(req.ApprovalMode) ?? throw new ValidationException("approvalMode", SettingsModeInvalid);
        (c.SalesChangeApprovalMode, c.SalesChangeApprovalAmount) = (mode, mode is null ? null : ValidateAmount(mode, req.ApprovalAmount));
        c.SalesChangeAttachmentRequired = req.AttachmentRequired;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await ClientSettingsDtoAsync(c, ct);
    }

    private async Task<SalesClientChangeSettingsDto> ClientSettingsDtoAsync(Client c, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == c.TenantId, ct);
        return new SalesClientChangeSettingsDto(c.PublicId, c.SalesChangeApprovalMode, c.SalesChangeApprovalAmount, c.SalesChangeAttachmentRequired,
            Effective(SalesChangeRules.EffectiveSettings(t, c)));
    }

    private static string? NormalizeMode(string? mode)
    {
        var m = mode?.Trim().ToUpperInvariant();
        return m is not null && SalesChangeApprovalModes.All.Contains(m) ? m : null;
    }

    private static decimal? ValidateAmount(string mode, decimal? amount)
    {
        if (mode != SalesChangeApprovalModes.AboveAmount) return null;
        if (amount is not decimal a || a <= 0m) throw new ValidationException("approvalAmount", SettingsAmountRequired);
        if (decimal.Round(a, 2) != a) throw new ValidationException("approvalAmount", SettingsAmountDecimals);
        return a;
    }

    private static SalesChangeEffectiveSettingsDto Effective(SalesChangeSettings s)
        => new(s.ApprovalMode, s.ApprovalAmount, s.AttachmentRequired, s.ApprovalFromClient, s.AttachmentFromClient);

    // ================================================================ validaciones de la pasada (a)

    /// <summary>Quién autorizó en el cliente y cómo (400 con el mensaje exacto) y el id del canal (catálogo SalesChangeChannel).</summary>
    private async Task<(int ChannelId, string Channel)> ValidateAuthorizationAsync(string? name, string? title, string? channel, DateTime? at, string? reason, SalesOrder order,
        CancellationToken ct)
    {
        var orderCreated = order.CreatedAtUtc == default ? DateTime.MinValue : order.CreatedAtUtc;
        if (SalesChangeRules.ValidateAuthorization(name, title, channel, at, reason, clock.UtcNow, orderCreated) is { } err)
            throw new ValidationException(err.Field, err.Message);
        var code = SalesChangeRules.NormalizeChannel(channel)!;
        var id = await lookups.TryGetIdAsync(SalesChangeCodes.ChannelDomain, code, ct) ?? throw new ValidationException("authorizationChannel", SalesChangeRules.ChannelInvalid);
        return (id, code);
    }

    /// <summary>El contacto que autorizó debe ser del cliente de la orden (404 sin oráculo si no).</summary>
    private async Task<int?> ValidateContactAsync(int? contactId, int clientId, CancellationToken ct)
    {
        if (contactId is not int id) return null;
        var ok = await (from cc in db.Set<ClientContact>().AsNoTracking()
                        join c in db.Set<Client>().AsNoTracking() on cc.ClientId equals c.ClientId
                        where cc.ClientContactId == id && cc.ClientId == clientId && cc.IsActive
                        select cc.ClientContactId).AnyAsync(ct);
        return ok ? id : throw new NotFoundException("Contacto");
    }

    /// <summary>El adjunto: activo, cuyo dueño es la orden (o, en una adición, la madre o la raíz) y que no respalde otra modificación (409).</summary>
    private async Task<int?> ValidateAttachmentAsync(Guid? filePublicId, IReadOnlyCollection<int> ownerOrderIds, CancellationToken ct)
    {
        if (filePublicId is not Guid fid) return null;
        var ownerType = await lookups.TryGetIdAsync(LookupDomains.EntityType, SalesCodes.EntityType, ct);
        var ids = ownerOrderIds.Distinct().ToList();
        var file = await db.Set<StoredFile>().AsNoTracking()
            .Where(f => f.PublicId == fid && f.IsActive && f.OwnerEntityLookupId == ownerType && ids.Contains(f.OwnerId))
            .Select(f => (int?)f.StoredFileId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Archivo");
        if (await db.Set<SalesOrderChange>().AsNoTracking().AnyAsync(c => c.AttachmentFileId == file, ct)) throw new ConflictException(AttachmentUsed);
        return file;
    }

    /// <summary>La solicitud normalizada: operaciones con el producto resuelto (404 si no es de la compañía), términos y notas (TERMS / NOTES se agregan solos si vienen).</summary>
    private async Task<StoredRequest> StoredOfAsync(SalesOrderChangeRequest req, CancellationToken ct)
    {
        var raw = (req.Ops ?? Array.Empty<SalesOrderChangeLineOp>()).Where(o => o is not null).ToList();
        var productPublicIds = raw.Where(o => o.ProductPublicId is not null).Select(o => o.ProductPublicId!.Value).Distinct().ToList();
        var products = productPublicIds.Count == 0 ? new Dictionary<Guid, (int Id, string Sku)>()
            : await db.Set<Product>().AsNoTracking().Where(p => productPublicIds.Contains(p.PublicId))
                .ToDictionaryAsync(p => p.PublicId, p => (Id: p.ProductId, p.Sku), ct);
        var ops = new List<ChangeOp>();
        foreach (var o in raw)
        {
            var kind = o.Kind?.Trim().ToUpperInvariant() ?? string.Empty;
            int? productId = null;
            string? sku = null;
            if (o.ProductPublicId is Guid g)
            {
                if (!products.TryGetValue(g, out var p)) throw new NotFoundException("Producto");
                (productId, sku) = (p.Id, p.Sku);
            }
            ops.Add(new ChangeOp(kind, o.LineId, productId, o.Quantity, o.UnitPrice, o.TaxPct, o.Note?.Trim(), sku));
        }
        SalesChangeTermsInput? terms = req.Terms is null ? null
            : new SalesChangeTermsInput(req.Terms.PaymentMethod, req.Terms.PaymentTerm, req.Terms.DeliveryMode, req.Terms.ConsigneePublicId, req.Terms.ClearConsignee, req.Terms.IsConsignment);
        if (terms is not null && !ops.Any(o => o.Kind == ChangeOpKinds.Terms)) ops.Add(new ChangeOp(ChangeOpKinds.Terms));
        if (req.Notes is not null && !ops.Any(o => o.Kind == ChangeOpKinds.Notes)) ops.Add(new ChangeOp(ChangeOpKinds.Notes));
        var reopenReason = string.IsNullOrWhiteSpace(req.ReopenReason) ? null : req.ReopenReason.Trim();
        return new StoredRequest(ops, terms, req.Notes, req.ReopenPack == true, reopenReason, req.AllowBackorder == true, req.OverrideCredit == true);
    }

    /// <summary>Lo que es 400 antes de planear: sin operaciones, operación desconocida, cantidad, precio, porcentaje y motivo del IVU manual.</summary>
    private static (string Field, string Message)? PreValidationError(IReadOnlyList<ChangeOp> ops)
    {
        if (ops.Count == 0) return ("ops", SalesChangeRules.NothingToChange);
        foreach (var op in ops)
        {
            if (!ChangeOpKinds.All.Contains(op.Kind)) return ("ops", SalesChangeRules.OpKindInvalid);
            switch (op.Kind)
            {
                case ChangeOpKinds.Qty or ChangeOpKinds.Add:
                    if (SalesRules.QuantityError(op.Quantity) is { } qe) return ("quantity", qe);
                    if (op.Kind == ChangeOpKinds.Add && op.UnitPrice is not null && SalesRules.PriceError(op.UnitPrice) is { } ape) return ("unitPrice", ape);
                    break;
                case ChangeOpKinds.Price:
                    if (op.UnitPrice is null) return ("unitPrice", SalesChangeRules.PriceRequired);
                    if (SalesRules.PriceError(op.UnitPrice) is { } pe) return ("unitPrice", pe);
                    break;
                case ChangeOpKinds.Tax:
                    if (SalesChangeRules.TaxPctError(op.TaxPct) is { } te) return ("taxPct", te);
                    if (SalesChangeRules.TaxNoteError(op.Note) is { } ne) return ("note", ne);
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// El crédito frente al total nuevo: si SUBE y se excede, sin autorización = aviso a quien autoriza (guardado ya) y 422 credit_exceeded; con OverrideCredit se
    /// exige orders.credit_override (403) y devuelve true (queda en la modificación y en la bitácora de seguridad).
    /// </summary>
    private async Task<bool> CheckCreditAsync(SalesOrder order, decimal before, decimal after, bool overrideCredit, CancellationToken ct)
    {
        var credit = await sales.EvaluateCreditForChangeAsync(order, before, after, notifyIfExceeded: !overrideCredit, ct);
        if (!credit.Checked || credit.Ok) return false;
        if (!overrideCredit) throw new CreditExceededException(credit.Check, CreditRules.Evaluate(credit.Check).Available);
        await permissions.EnsureAsync(OrderStatusService.CreditOverridePermission, ct);
        return true;
    }

    private async Task WriteCreditOverrideAsync(Guid orderPublicId, int changeId, CancellationToken ct)
    {
        var publicId = await db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.SalesOrderChangeId == changeId).Select(c => c.PublicId).FirstOrDefaultAsync(ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenant.TenantId,
            new { action = "credit_override", salesOrder = orderPublicId, salesOrderChange = publicId }, ct);
    }

    // ================================================================ piezas comunes

    /// <summary>La modificación nueva (PENDING_APPROVAL por StatusService), con ChangeNo = máximo + 1, la solicitud, la foto de antes y la proyectada de después.</summary>
    private async Task<SalesOrderChange> NewChangeAsync(SalesOrder order, string kind, string authorizedByName, string? authorizedByTitle, int? contactId, int channelId,
        DateTime? authorizedAt, string reason, int? attachmentId, StoredRequest stored, SalesChangeProjection proj, string fingerprint, string? rule, bool creditOverride,
        CancellationToken ct)
    {
        var maxNo = await db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.SalesOrderId == order.SalesOrderId).MaxAsync(c => (int?)c.ChangeNo, ct) ?? 0;
        var now = DateTime.UtcNow;
        var change = new SalesOrderChange
        {
            PublicId = Guid.NewGuid(), TenantId = order.TenantId, SalesOrderId = order.SalesOrderId, ChangeNo = maxNo + 1, Kind = kind, IsPending = false,
            RegisteredBy = tenant.UserId ?? 0, RegisteredAtUtc = now,
            AuthorizedByName = authorizedByName.Trim(), AuthorizedByTitle = string.IsNullOrWhiteSpace(authorizedByTitle) ? null : authorizedByTitle.Trim(),
            AuthorizedByContactId = contactId, AuthorizationChannelLookupId = channelId, AuthorizedAtUtc = authorizedAt ?? now, Reason = reason.Trim(),
            AttachmentFileId = attachmentId,
            RequestJson = SalesChangeJson.Serialize(stored), BeforeJson = proj.Before.ToJson(), AfterJson = proj.After.ToJson(),
            Summary = SalesChangeSnapshot.Summary(proj.Diff), TotalBefore = proj.TotalBefore, TotalAfter = proj.TotalAfter, TotalDelta = proj.TotalDelta,
            LinesAdded = proj.Diff.Added.Count, LinesRemoved = proj.Diff.Removed.Count, LinesChanged = proj.Diff.Changed.Count,
            RequiresApproval = rule is not null, ApprovalRule = rule, BaseFingerprint = fingerprint, CreditOverride = creditOverride,
            CreatedAtUtc = now, CreatedBy = tenant.UserId,
        };
        // el estatus provisional (el inicial) para guardar; la transición de nada a PENDING_APPROVAL lo deja en el historial
        var initial = await statuses.GetInitialAsync(SalesChangeCodes.StatusDomain, ct);
        change.StatusCodeId = initial.StatusCodeId;
        db.Set<SalesOrderChange>().Add(change);
        await db.SaveGuardedAsync(PendingExistsGeneric, ct);
        var born = await statuses.TransitionAsync(SalesChangeCodes.StatusDomain, SalesChangeCodes.EntityType, change.SalesOrderChangeId, null,
            SalesOrderChangeStatuses.PendingApproval, null, ct);
        change.StatusCodeId = born.StatusCodeId;
        return change;
    }

    private async Task NotifyApprovalPendingAsync(SalesOrder order, SalesOrderChange change, string rule, decimal? amount, CancellationToken ct)
    {
        if (notifications is null) return;
        var values = await BaseValuesAsync(order, ct);
        foreach (var kv in await sales.ChangeNoticeValuesAsync(change, ct)) values[kv.Key] = kv.Value;
        values["approvalRule"] = ApprovalRuleText(rule, amount);
        await notifications.PublishAsync(SalesChangeEventCodes.ApprovalPending, values, $"APPROVAL-{change.PublicId:N}", ct);
    }

    /// <summary>Las variables de toda orden en los avisos (número, cliente, almacén y PublicId sin guiones para el enlace).</summary>
    private async Task<Dictionary<string, string?>> BaseValuesAsync(SalesOrder order, CancellationToken ct)
    {
        var client = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var warehouse = await db.Set<Warehouse>().AsNoTracking().Where(w => w.WarehouseId == order.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return new Dictionary<string, string?>
        {
            ["orderNumber"] = order.Number, ["client"] = client, ["warehouse"] = warehouse, ["orderPublicId"] = order.PublicId.ToString("N"),
        };
    }

    private static SalesChangeApplyInput Input(StoredRequest s, int changeNo) => new(s.Ops, changeNo, s.Terms, s.Notes, s.AllowBackorder);

    private static SalesChangeEffectsDto EffectsOf(SalesChangeOrderLoad load, ChangePlan plan)
    {
        var reversed = plan.ReverseBatchIds.Select(id => load.Batches.FirstOrDefault(b => b.BatchId == id)?.Number ?? id.ToString(CultureInfo.InvariantCulture)).ToList();
        var reopened = plan.ReopenGroupIds.Select(g => load.Batches.FirstOrDefault(b => b.GroupId == g)?.GroupNumber ?? g.ToString(CultureInfo.InvariantCulture))
            .Concat(plan.ReopenBatchIds.Select(id => load.Batches.FirstOrDefault(b => b.BatchId == id)?.Number ?? id.ToString(CultureInfo.InvariantCulture))).ToList();
        return new SalesChangeEffectsDto(reversed, reopened, SalesOrderService.WarehouseImpactText(reversed, reopened));
    }

    /// <summary>Reintenta (hasta 3 veces) cuando otro reservó entre la lectura y el candado; si sigue faltando, el 409 de siempre.</summary>
    private static async Task<TResult> RunWithStockRetryAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await work(ct); }
            catch (InsufficientStockException) when (attempt < 3) { }
        }
    }

    private async Task<int> OrderIdAsync(Guid orderPublicId, CancellationToken ct)
        => await db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == orderPublicId).Select(o => (int?)o.SalesOrderId).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("Orden de venta", feminine: true);

    private async Task<SalesOrderChange> ChangeRowAsync(int orderId, Guid changePublicId, CancellationToken ct)
        => await db.Set<SalesOrderChange>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == changePublicId && c.SalesOrderId == orderId, ct)
           ?? throw new NotFoundException(ChangeNotFoundWhat, feminine: true);

    private async Task<int?> PendingNoAsync(int orderId, CancellationToken ct)
        => await db.Set<SalesOrderChange>().AsNoTracking().Where(c => c.SalesOrderId == orderId && c.IsPending).Select(c => (int?)c.ChangeNo).FirstOrDefaultAsync(ct);

    private async Task<string> StatusCodeOfOrderAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct) ?? string.Empty;

    private async Task<string> CurrentUserNameAsync(CancellationToken ct)
    {
        if (tenant.UserId is not int id) return "sistema";
        var names = await AssigneeCheck.NamesAsync(db, new[] { id }, ct);
        return names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : $"usuario {id}";
    }

    /// <summary>409 si el RowVersion que mandó la pantalla ya no es el de la orden (solo si los dos existen).</summary>
    private static void EnsureRowVersion(SalesOrder order, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion) || order.RowVersion is not { Length: > 0 } current) return;
        byte[] sent;
        try { sent = Convert.FromBase64String(rowVersion.Trim()); }
        catch (FormatException) { throw new ValidationException("rowVersion", "rowVersion inválido: se espera el valor base64 devuelto por la ficha."); }
        if (sent.Length > 0 && !sent.AsSpan().SequenceEqual(current)) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }

    private static string? RowVersionOf(SalesOrder o) => o.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null;

    private static string? NormalizeNote(string? note, bool required, string? requiredMessage = null)
    {
        var n = note?.Trim();
        if (string.IsNullOrEmpty(n))
        {
            if (required) throw new ValidationException("note", requiredMessage ?? RejectNoteRequired);
            return null;
        }
        if (n.Length > SalesChangeRules.MaxDecisionNote) throw new ValidationException("note", DecisionNoteTooLong);
        return n;
    }

    private static string Trim500(string s) => s.Length <= 500 ? s : s[..499] + "…";

    private static string Money(decimal v) => "$" + v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    // ================================================================ DTOs

    private async Task<SalesOrderChangeDto> GetByIdAsync(int changeId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.Set<SalesOrderChange>().AsNoTracking().FirstAsync(c => c.SalesOrderChangeId == changeId, ct);
        return await ToDtoAsync(row, ct);
    }

    private async Task<SalesOrderChangeDto> ToDtoAsync(SalesOrderChange c, CancellationToken ct)
    {
        var item = (await ToItemsAsync(new List<SalesOrderChange> { c }, ct))[0];
        var file = c.AttachmentFileId is int fid
            ? await db.Set<StoredFile>().AsNoTracking().Where(f => f.StoredFileId == fid).Select(f => new { f.PublicId, f.FileName }).FirstOrDefaultAsync(ct)
            : null;
        var before = SalesChangeSnapshot.FromJson(c.BeforeJson);
        var after = SalesChangeSnapshot.FromJson(c.AfterJson);
        var diff = before is not null && after is not null && c.Kind == SalesChangeKinds.Modify ? SalesChangeSnapshot.Diff(before, after) : null;
        var effects = SalesChangeJson.Deserialize<StoredEffects>(c.EffectsJson);
        return new SalesOrderChangeDto(item, c.AuthorizedByContactId, c.AuthorizedAtUtc, file?.PublicId, file?.FileName, c.LinesAdded, c.LinesRemoved, c.LinesChanged,
            c.ReversedBatchCount, c.HasPriceChange, c.HasTaxOverride, c.HasTermsChange, c.ReopenedPack, c.CreditOverride, c.DecidedBy, c.DecisionNote, c.AppliedAtUtc,
            before is null ? null : ToDto(before), after is null ? null : ToDto(after), diff is null ? null : ToDto(diff),
            effects is null ? null : new SalesChangeEffectsDto(effects.ReversedBatches, effects.ReopenedPacks, effects.WarehouseImpact));
    }

    private async Task<List<SalesOrderChangeListItemDto>> ToItemsAsync(List<SalesOrderChange> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new List<SalesOrderChangeListItemDto>();
        var orderIds = rows.Select(r => r.SalesOrderId).Concat(rows.Where(r => r.AdditionSalesOrderId != null).Select(r => r.AdditionSalesOrderId!.Value)).Distinct().ToList();
        var orders = await db.Set<SalesOrder>().AsNoTracking().Where(o => orderIds.Contains(o.SalesOrderId))
            .Select(o => new { o.SalesOrderId, o.PublicId, o.Number, o.ClientId }).ToDictionaryAsync(o => o.SalesOrderId, ct);
        var clientIds = orders.Values.Select(o => o.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var statusRows = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == SalesChangeCodes.StatusDomain).ToListAsync(ct);
        var statusMap = statusRows.ToDictionary(s => s.StatusCodeId);
        var userIds = rows.Select(r => r.RegisteredBy).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct().ToList();
        var names = await AssigneeCheck.NamesAsync(db, userIds, ct);
        var channels = new Dictionary<int, LookupCode?>();
        foreach (var id in rows.Select(r => r.AuthorizationChannelLookupId).Distinct()) channels[id] = await lookups.GetAsync(id, ct);
        var lang = tenant.Lang;

        return rows.Select(r =>
        {
            var o = orders.GetValueOrDefault(r.SalesOrderId);
            var st = statusMap.GetValueOrDefault(r.StatusCodeId);
            var ch = channels.GetValueOrDefault(r.AuthorizationChannelLookupId);
            var add = r.AdditionSalesOrderId is int aid ? orders.GetValueOrDefault(aid) : null;
            return new SalesOrderChangeListItemDto(r.PublicId, o?.PublicId ?? Guid.Empty, o?.Number ?? string.Empty, o is null ? null : clients.GetValueOrDefault(o.ClientId),
                r.ChangeNo, r.Kind, st?.InternalCode ?? string.Empty, st is null ? string.Empty : MultilingualText.Resolve(st.LabelJson, lang) ?? st.InternalCode,
                st?.ColorHex ?? "#6B7280", r.IsPending, r.RegisteredBy, names.GetValueOrDefault(r.RegisteredBy), r.RegisteredAtUtc, r.AuthorizedByName, r.AuthorizedByTitle,
                ch?.InternalCode ?? string.Empty, ch is null ? string.Empty : MultilingualText.Resolve(ch.LabelJson, lang) ?? ch.InternalCode,
                r.Reason, r.Summary, r.TotalBefore, r.TotalAfter, r.TotalDelta, r.RequiresApproval, r.ApprovalRule,
                r.DecidedBy is int d ? names.GetValueOrDefault(d) : null, r.DecidedAtUtc, add?.PublicId, add?.Number);
        }).ToList();
    }

    private static SalesChangeSnapshotDto ToDto(SalesChangeSnapshot s)
        => new(s.Lines.Select(l => new SalesChangeSnapshotLineDto(l.LineId, l.LineNo, l.Sku, l.Name, l.QtyOrdered, l.QtyShipped, l.QtyInvoiced, l.UnitPrice, l.ResolvedUnitPrice,
                l.PriceSource, l.TaxRatePct, l.TaxAmount, l.TaxReason, l.TaxComputedRatePct, l.TaxComputedAmount, l.IsRemoved)).ToList(),
            new SalesChangeSnapshotTermsDto(s.Terms.PaymentMethod, s.Terms.PaymentTerm, s.Terms.DeliveryMode, s.Terms.ConsigneeName, s.Terms.IsConsignment, s.Terms.Notes),
            s.Subtotal, s.TaxAmount, s.ShippingCharge, s.ShippingTaxAmount, s.Total);

    private static SalesChangeDiffDto ToDto(SalesChangeDiff d)
    {
        static SalesChangeDiffLineDto Line(SalesChangeLineDiff l)
            => new(l.LineId, l.LineNo, l.Sku, l.Name, l.Change, l.Fields.Select(f => new SalesChangeFieldDiffDto(f.Field, f.Before, f.After)).ToList());
        return new SalesChangeDiffDto(d.Added.Select(Line).ToList(), d.Removed.Select(Line).ToList(), d.Changed.Select(Line).ToList(),
            d.Terms.Select(f => new SalesChangeFieldDiffDto(f.Field, f.Before, f.After)).ToList(), d.TotalBefore, d.TotalAfter, d.Delta);
    }
}
