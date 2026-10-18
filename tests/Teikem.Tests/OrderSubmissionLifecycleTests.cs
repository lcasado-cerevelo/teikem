using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 106 — flujo de estatus de la orden (SUBMITTED → PICKUP → … → DELIVERED; FAILED lateral; CANCELLED e IN_WAREHOUSE finales):
/// la orden se cotiza al someterla (OrderService.CreateAsync → OrderStatusService.QuoteOnSubmissionAsync) y se mide el crédito del
/// cliente (Sometida ya cuenta en el saldo en curso); cancelar exige razón; reabrir una orden en estatus final exige el permiso
/// orders.reopen y la razón, y sin destino vuelve al último estatus en curso que tuvo. InMemory sobre WmsFixture con OrderService,
/// OrderStatusService, OrderQuoteService, RateService, QuotedOrdersBalanceProvider y OrderStatusEffect reales.
/// Límite de InMemory: RunInTransactionAsync no abre una transacción real, así que «el 422 revierte la creación» se prueba como
/// «el 422 sale de dentro de la transacción de la creación» (el rollback lo hace SQL Server; lo cubre el smoke).
/// </summary>
public sealed class OrderSubmissionLifecycleTests
{
    private const decimal Rate = 50m;

    private sealed class RecordingSecurity : ISecurityEventWriter
    {
        public List<(string Type, string Outcome, string Detail)> Events { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add((eventType, outcome, detail is null ? "" : JsonSerializer.Serialize(detail)));
            return Task.CompletedTask;
        }

        public IEnumerable<string> Actions(string action)
            => Events.Where(e => e.Type == SecurityEventTypes.RoleChange && e.Detail.Contains($"\"action\":\"{action}\"", StringComparison.Ordinal)).Select(e => e.Detail);
    }

    private sealed class World(WmsFixture f, Client c, Location consignee, RecordingSecurity security)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public Location Consignee { get; } = consignee;
        public RecordingSecurity Security { get; } = security;
        public OrderService Orders => F.Get<OrderService>();
        public OrderStatusService Statuses => F.Get<OrderStatusService>();
        public TeikemDbContext Db => F.Db;
    }

    /// <summary>Cliente C1 con un consignatario; con contrato ACTIVE y tarifa STANDARD/BOX de 50 si <paramref name="contract"/>; límite de crédito opcional.</summary>
    private static async Task<World> NewWorldAsync(bool contract = true, bool rate = true, decimal? creditLimit = null)
    {
        var security = new RecordingSecurity();
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ISecurityEventWriter>(security);
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<OrderPriceVisibility>();
            s.AddSingleton<OrderReadService>();
            s.AddSingleton<IClientBalanceProvider, QuotedOrdersBalanceProvider>();
            s.AddSingleton<OrderStatusEffect>();
            s.AddSingleton<IStatusTransitionEffect>(sp => sp.GetRequiredService<OrderStatusEffect>());
            s.AddSingleton<OrderStatusService>();
            // LocationService (consignatario al vuelo) y SpecialDeliveryDispatchService (chofer) no se usan en estas pruebas.
            s.AddSingleton(sp => new OrderService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
                sp.GetRequiredService<StatusService>(), null!, sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<OrderStatusService>(),
                sp.GetRequiredService<OrderReadService>(), sp.GetRequiredService<PermissionService>(), null!, sp.GetRequiredService<ITenantClock>()));
        });
        var db = f.Db;

        // Catálogos que la captura, la cotización y la ficha necesitan (WmsFixture no los trae).
        var id = 5000;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        var standard = L(LookupDomains.ServiceType, "STANDARD");
        var box = L(LookupDomains.PackageType, "BOX");
        var baseFreight = L(LookupDomains.RateComponentType, RateComponentTypes.BaseFreight);
        db.LookupCodes.AddRange(standard, box, baseFreight,
            L(LookupDomains.RateComponentType, RateComponentTypes.ExtraPiece),
            L(LookupDomains.EntityType, EntityTypes.OrderStop),
            L(LookupDomains.Capability, Capabilities.EditCargo),
            L(LookupDomains.Capability, Capabilities.Reprice),
            L(LookupDomains.Capability, Capabilities.Cancel));
        var pipe = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        var term = f.LookupId(LookupDomains.StageKind, StageKinds.Terminal);
        db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 5100, Entity = StatusDomains.StopStatus, InternalCode = StopStatuses.Pending, LabelJson = "{\"es\":\"Pendiente\"}", StageKindLookupId = pipe, SortOrder = 1, IsInitial = true, IsActive = true },
            new StatusCode { StatusCodeId = 5101, Entity = StatusDomains.ContractStatus, InternalCode = ContractStatuses.Draft, LabelJson = "{\"es\":\"Borrador\"}", StageKindLookupId = pipe, SortOrder = 1, IsInitial = true, IsActive = true },
            new StatusCode { StatusCodeId = 5102, Entity = StatusDomains.OrderStatus, InternalCode = OrderStatuses.InWarehouse, LabelJson = "{\"es\":\"En almacén\"}", StageKindLookupId = term, SortOrder = 25, IsActive = true },
            new StatusCode { StatusCodeId = 5103, Entity = StatusDomains.ClientStatus, InternalCode = ClientStatuses.Suspended, LabelJson = "{\"es\":\"Suspendido\"}", StageKindLookupId = f.LookupId(LookupDomains.StageKind, StageKinds.Lateral), SortOrder = 2, IsActive = true });
        await db.SaveChangesAsync();
        f.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();

        var c = await f.AddClientAsync("C1");
        var consignee = new Location
        {
            LocationId = 5200, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, Name = "Tienda Uno",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), Line1 = "Calle 1", City = "Ponce",
            CountryLookupId = f.LookupId(LookupDomains.Country, "PR"), IsActive = true,
        };
        db.Locations.Add(consignee);
        if (creditLimit is not null)
        {
            var tracked = await db.Clients.AsTracking().SingleAsync(x => x.ClientId == c.ClientId);
            tracked.CreditLimit = creditLimit;
        }
        if (contract)
        {
            var k = new Contract
            {
                ContractId = 5300, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, ContractNumber = "K-1", Title = "Marco",
                StartDate = new DateOnly(2026, 1, 1), StatusCodeId = f.StatusId(StatusDomains.ContractStatus, ContractStatuses.Active), BillPerService = true, IsActive = true,
            };
            db.Set<Contract>().Add(k);
            if (rate)
                db.RateComponents.Add(new RateComponent
                {
                    RateComponentId = 5400, TenantId = WmsFixture.TenantId, ContractId = k.ContractId, ComponentTypeLookupId = baseFreight.LookupCodeId,
                    ServiceTypeLookupId = standard.LookupCodeId, PackageTypeLookupId = box.LookupCodeId, FlatAmount = Rate,
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
                });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new World(f, await db.Clients.AsNoTracking().SingleAsync(x => x.ClientId == c.ClientId), consignee, security);
    }

    private static OrderCreateRequest Req(World x, bool overrideCredit = false)
        => new(x.C.PublicId, x.Consignee.PublicId, null, null, "STANDARD", null,
            new List<OrderPackageLineRequest> { new("BOX", "Caja") }, null, null, null, null, null, null, null, OverrideCredit: overrideCredit);

    private static async Task<TransportOrder> OrderAsync(World x, Guid publicId)
        => await x.Db.TransportOrders.AsNoTracking().Include(o => o.Status).SingleAsync(o => o.PublicId == publicId);

    /// <summary>Orden ya cotizada (monto congelado) en el estatus dado, con su parada de entrega y sin historial.</summary>
    private static async Task<TransportOrder> QuotedOrderAsync(World x, string number, string status, decimal? quoted, int? statusCodeId = null)
    {
        var o = await x.F.AddOrderAsync(x.C.ClientId, number, status);
        var t = await x.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        t.QuotedAmount = quoted;
        if (statusCodeId is int sid) t.StatusCodeId = sid;
        x.Db.OrderStops.Add(new OrderStop
        {
            TransportOrderId = t.TransportOrderId, StopTypeLookupId = x.F.LookupId(LookupDomains.StopType, StopTypes.Delivery), Sequence = 2,
            SnapName = "Tienda Uno", SnapLine1 = "Calle 1", SnapCity = "Ponce", StatusCodeId = 5100,
        });
        await x.Db.SaveChangesAsync();
        x.Db.ChangeTracker.Clear();
        return t;
    }

    /// <summary>Mueve la orden con StatusService (como lo haría Trips/POD), dejando el historial.</summary>
    private static async Task MoveAsync(World x, Guid publicId, string to)
    {
        await x.Db.RunInTransactionAsync(async ct =>
        {
            var o = await x.Db.ResolveOrderForWriteAsync(publicId, OrderScope.Any, ct);
            var s = await x.F.Get<StatusService>().TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, o.TransportOrderId, o.StatusCodeId, to, null, ct);
            o.StatusCodeId = s.StatusCodeId;
            await x.Db.SaveChangesAsync(ct);
        }, default);
        x.Db.ChangeTracker.Clear();
    }

    private static async Task<List<(string? From, string To, string? Comment)>> HistoryAsync(World x, int orderId)
    {
        var typeId = x.F.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var rows = await x.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == orderId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => new { h.FromStatusCodeId, h.ToStatusCodeId, h.Comment }).ToListAsync();
        string Code(int statusId) => statusId == 5102 ? OrderStatuses.InWarehouse : x.F.StatusCodeOf(statusId);
        return rows.Select(r => (r.FromStatusCodeId is int f ? Code(f) : null, Code(r.ToStatusCodeId), r.Comment)).ToList();
    }

    // ================================================================ catálogo, plantillas y SQL

    [Fact]
    public void Reopen_permission_is_in_the_catalog_and_only_TenantAdmin_brings_it()
    {
        Assert.Equal("orders.reopen", PermissionCatalog.OrdersReopen);
        Assert.Equal(PermissionCatalog.OrdersReopen, OrderStatusService.ReopenPermission);
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == PermissionCatalog.OrdersReopen);
        Assert.Equal(("ORDERS", "Reabrir órdenes en estatus final", "Reopen orders in a final status"), (p.Category, p.LabelEs, p.LabelEn));
        Assert.Equal(new[] { "TenantAdmin" }, PermissionCatalog.RoleTemplates.Where(t => t.Value.Contains(PermissionCatalog.OrdersReopen)).Select(t => t.Key).ToArray());
    }

    [Fact]
    public void Update_sql_adds_the_reopen_permission_and_gives_it_to_cloned_TenantAdmin_roles_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-10-17 (ao) — Lote 106: permiso orders.reopen", StringComparison.Ordinal);
        Assert.True(start > 0, "Falta la sección 2026-10-17 (ao) en logistica-db-update.sql.");
        Assert.True(start > sql.IndexOf("2026-10-11 (g) — Motivo de despacho manual", StringComparison.Ordinal));
        var section = UpdateSqlSection.From(sql, start);
        Assert.Contains("('orders.reopen', N'{\"es\":\"Reabrir órdenes en estatus final\",\"en\":\"Reopen orders in a final status\"}')", section);
        Assert.Contains("WHEN NOT MATCHED THEN", section);
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'orders.reopen' AND r.Name = 'TenantAdmin'", section);
        Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);", section);
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ cotizar y medir el crédito al someter

    [Fact]
    public async Task Creating_quotes_the_order_on_submission_when_the_client_has_contract_and_rate()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default);

        var o = await OrderAsync(x, dto.PublicId);
        Assert.Equal(OrderStatuses.Submitted, o.Status!.InternalCode);   // nace Sometida: no avanza
        Assert.Equal(Rate, o.QuotedAmount);
        Assert.NotNull(o.QuotedAtUtc);
        Assert.Equal(5300, o.ContractId);
        Assert.Null(o.ConfirmedAtUtc);
        Assert.Equal(Rate, dto.QuotedAmount);   // administrador de plataforma: ve el precio en la ficha
        Assert.Equal(new[] { ((string?)null, OrderStatuses.Submitted, (string?)null) }, await HistoryAsync(x, o.TransportOrderId));
    }

    [Theory]
    [InlineData(false, false)]   // sin contrato
    [InlineData(true, false)]    // contrato sin tarifa para STANDARD/BOX
    public async Task Without_contract_or_rate_the_order_is_rejected_at_submission(bool contract, bool rate)
    {
        // Lote 107: el cernidor es al someter; sin contrato o sin tarifa la orden no se crea (mismos 409/422 de siempre).
        var x = await NewWorldAsync(contract, rate, creditLimit: 1m);
        await using var _ = x.F;
        await Assert.ThrowsAnyAsync<TeikemException>(() => x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default));
        Assert.Empty(x.Security.Actions("credit_override"));
    }

    [Fact]
    public async Task Exceeding_the_credit_on_submission_is_422_credit_exceeded_from_inside_the_creation()
    {
        var x = await NewWorldAsync(creditLimit: 40m);
        await using var _ = x.F;
        var ex = await Assert.ThrowsAsync<CreditExceededException>(() => x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default));
        Assert.Equal((422, "credit_exceeded"), (ex.StatusCode, ex.Code));
        Assert.Equal(CreditRules.Message(new CreditCheck(40m, 0m, Rate)), ex.Message);
        Assert.Empty(x.Security.Actions("credit_override"));
        // El 422 sale de QuoteOnSubmissionAsync, dentro de RunInTransactionAsync de la creación: en SQL Server la orden no queda.
        // (InMemory no revierte; aquí solo se comprueba que el servicio no devolvió una orden creada.)
    }

    [Fact]
    public async Task Submitted_quoted_orders_count_in_the_pending_balance_of_the_credit_check()
    {
        var x = await NewWorldAsync(creditLimit: 60m);
        await using var _ = x.F;
        await QuotedOrderAsync(x, "T-SOMETIDA", OrderStatuses.Submitted, 30m);   // antes del Lote 106 no contaba
        await QuotedOrderAsync(x, "T-CANCELADA", OrderStatuses.Cancelled, 500m);   // final: no cuenta

        // 30 en curso + 50 de esta orden = 80 > 60: excede (sin contar la Sometida serían 50 ≤ 60).
        var ex = await Assert.ThrowsAsync<CreditExceededException>(() => x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default));
        Assert.Equal(CreditRules.Message(new CreditCheck(60m, 30m, Rate)), ex.Message);
    }

    [Fact]
    public async Task Pending_balance_sums_quoted_non_final_orders_including_submitted()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var submitted = await QuotedOrderAsync(x, "T-1", OrderStatuses.Submitted, 30m);
        await QuotedOrderAsync(x, "T-2", OrderStatuses.Pickup, 20m);
        await QuotedOrderAsync(x, "T-3", OrderStatuses.Failed, 5m);
        await QuotedOrderAsync(x, "T-4", OrderStatuses.Submitted, null);      // sin cotizar: no suma
        await QuotedOrderAsync(x, "T-5", OrderStatuses.Delivered, 70m);       // final
        await QuotedOrderAsync(x, "T-6", OrderStatuses.Cancelled, 100m);      // final
        var balance = x.F.Get<IClientBalanceProvider>();

        Assert.Equal(55m, await balance.PendingBalanceAsync(x.C.ClientId, null, default));
        Assert.Equal(25m, await balance.PendingBalanceAsync(x.C.ClientId, submitted.TransportOrderId, default));   // excluye la que se mide
    }

    [Fact]
    public async Task Override_with_the_permission_creates_the_order_and_leaves_the_security_event()
    {
        var x = await NewWorldAsync(creditLimit: 40m);
        await using var _ = x.F;
        x.F.SetPermissions(PermissionCatalog.OrdersCreate, PermissionCatalog.OrdersView, PermissionCatalog.OrdersCreditOverride);
        var dto = await x.Orders.CreateAsync(Req(x, overrideCredit: true), OrderScope.Any, null, default);

        var o = await OrderAsync(x, dto.PublicId);
        Assert.Equal((OrderStatuses.Submitted, (decimal?)Rate), (o.Status!.InternalCode, o.QuotedAmount));
        Assert.Null(dto.QuotedAmount);   // sin orders.view_price la ficha no muestra el precio
        var detail = Assert.Single(x.Security.Actions("credit_override"));
        Assert.Contains($"\"order\":\"{o.PublicId}\"", detail);
        Assert.Contains("\"limit\":40", detail);
        Assert.Contains("\"amount\":50", detail);
    }

    [Fact]
    public async Task Override_within_the_credit_creates_the_order_without_a_security_event()
    {
        var x = await NewWorldAsync(creditLimit: 1000m);
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x, overrideCredit: true), OrderScope.Any, null, default);
        Assert.Equal(Rate, (await OrderAsync(x, dto.PublicId)).QuotedAmount);
        Assert.Empty(x.Security.Actions("credit_override"));
    }

    [Fact]
    public async Task Override_without_the_permission_is_403_before_writing_anything()
    {
        var x = await NewWorldAsync(creditLimit: 40m);
        await using var _ = x.F;
        x.F.SetPermissions(PermissionCatalog.OrdersCreate, PermissionCatalog.OrdersView);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => x.Orders.CreateAsync(Req(x, overrideCredit: true), OrderScope.Any, null, default));
        Assert.Equal((403, "Falta el permiso 'orders.credit_override'."), (ex.StatusCode, ex.Message));
        Assert.Empty(await x.Db.TransportOrders.AsNoTracking().ToListAsync());
        Assert.Contains(x.Security.Events, e => e.Type == SecurityEventTypes.PermissionDenied && e.Detail.Contains("orders.credit_override"));
    }

    [Fact]
    public async Task Override_without_the_permission_is_403_even_when_the_credit_is_not_exceeded()
    {
        var x = await NewWorldAsync(contract: false);
        await using var _ = x.F;
        x.F.SetPermissions(PermissionCatalog.OrdersCreate);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Orders.CreateAsync(Req(x, overrideCredit: true), OrderScope.Any, null, default));
        Assert.Empty(await x.Db.TransportOrders.AsNoTracking().ToListAsync());
    }

    // ================================================================ cancelar con razón

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_without_a_reason_is_400_on_comment(string? comment)
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Statuses.CancelAsync(dto.PublicId, new OrderCancelRequest(comment, null), OrderScope.Any, default));
        Assert.Equal((400, "Indique la razón de la cancelación."), (ex.StatusCode, ex.Message));
        Assert.Equal(OrderStatusService.CancelReasonRequiredMessage, ex.Message);
        Assert.Equal(new[] { "comment" }, ex.Errors!.Keys.ToArray());
        Assert.Equal(OrderStatuses.Submitted, (await OrderAsync(x, dto.PublicId)).Status!.InternalCode);
    }

    [Fact]
    public async Task Cancel_without_a_body_is_400_too()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Statuses.CancelAsync(dto.PublicId, null, OrderScope.Any, default));
        Assert.Equal(OrderStatusService.CancelReasonRequiredMessage, ex.Message);
    }

    [Fact]
    public async Task Cancel_with_a_reason_leaves_it_in_the_history_and_frees_the_credit()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default);
        var balance = x.F.Get<IClientBalanceProvider>();
        Assert.Equal(Rate, await balance.PendingBalanceAsync(x.C.ClientId, null, default));   // Sometida y cotizada: consume crédito

        var after = await x.Statuses.CancelAsync(dto.PublicId, new OrderCancelRequest("El cliente desistió", null), OrderScope.Any, default);
        Assert.Equal(OrderStatuses.Cancelled, after.Status);
        var o = await OrderAsync(x, dto.PublicId);
        Assert.Equal(OrderStatuses.Cancelled, o.Status!.InternalCode);
        Assert.Equal((OrderStatuses.Submitted, OrderStatuses.Cancelled, "El cliente desistió"), (await HistoryAsync(x, o.TransportOrderId))[^1]);
        Assert.Equal(0m, await balance.PendingBalanceAsync(x.C.ClientId, null, default));
    }

    // ================================================================ reabrir

    [Fact]
    public async Task Reopen_without_the_permission_is_403()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);
        x.F.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.OrdersEdit, PermissionCatalog.OrdersCancel);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(OrderStatuses.InTransit, "Se entregó por error", null), OrderScope.Any, default));
        Assert.Equal((403, "Falta el permiso 'orders.reopen'."), (ex.StatusCode, ex.Message));
        Assert.Equal(OrderStatuses.Delivered, (await OrderAsync(x, o.PublicId)).Status!.InternalCode);
        Assert.Empty(x.Security.Actions("order_reopen"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Reopen_without_a_reason_is_400_on_comment(string? comment)
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);
        x.F.SetPermissions(PermissionCatalog.OrdersReopen);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(OrderStatuses.InTransit, comment, null), OrderScope.Any, default));
        Assert.Equal((400, "Indique la razón para reabrir la orden."), (ex.StatusCode, ex.Message));
        Assert.Equal(OrderStatusService.ReopenReasonRequiredMessage, ex.Message);
        Assert.Equal(new[] { "comment" }, ex.Errors!.Keys.ToArray());
    }

    [Theory]
    [InlineData(OrderStatuses.Submitted)]
    [InlineData(OrderStatuses.Pickup)]
    [InlineData(OrderStatuses.Failed)]
    public async Task Reopen_from_a_non_final_status_is_422(string status)
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", status, 10m);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(null, "Razón", null), OrderScope.Any, default));
        Assert.Equal((422, "Solo se reabre una orden en estatus final."), (ex.StatusCode, ex.Message));
        Assert.Equal(OrderStatusService.NotFinalMessage, ex.Message);
        Assert.Equal(status, (await OrderAsync(x, o.PublicId)).Status!.InternalCode);
    }

    [Fact]
    public async Task Reopen_a_cancelled_order_without_target_goes_back_to_its_last_status_and_leaves_history_and_event()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default);
        await MoveAsync(x, dto.PublicId, OrderStatuses.Pickup);
        await x.Statuses.CancelAsync(dto.PublicId, new OrderCancelRequest("Cliente desistió", null), OrderScope.Any, default);
        x.F.SetPermissions(PermissionCatalog.OrdersReopen, PermissionCatalog.OrdersView);

        var after = await x.Statuses.ReopenAsync(dto.PublicId, new OrderReopenRequest(null, "  El cliente sí la quiere  ", null), OrderScope.Any, default);

        Assert.Equal(OrderStatuses.Pickup, after.Status);   // el último estatus en curso, no el inicial
        var o = await OrderAsync(x, dto.PublicId);
        Assert.Equal(OrderStatuses.Pickup, o.Status!.InternalCode);
        Assert.Equal(new (string?, string, string?)[]
        {
            (null, OrderStatuses.Submitted, null),
            (OrderStatuses.Submitted, OrderStatuses.Pickup, null),
            (OrderStatuses.Pickup, OrderStatuses.Cancelled, "Cliente desistió"),
            (OrderStatuses.Cancelled, OrderStatuses.Pickup, "El cliente sí la quiere"),   // la razón, recortada
        }, await HistoryAsync(x, o.TransportOrderId));
        var detail = Assert.Single(x.Security.Actions("order_reopen"));
        Assert.Contains($"\"order\":\"{o.PublicId}\"", detail);
        Assert.Contains("\"from\":\"CANCELLED\"", detail);
        Assert.Contains("\"to\":\"PICKUP\"", detail);
        Assert.Equal(Rate, o.QuotedAmount);   // no recotiza
    }

    [Fact]
    public async Task Reopen_a_cancelled_submitted_order_goes_back_to_submitted()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var dto = await x.Orders.CreateAsync(Req(x), OrderScope.Any, null, default);
        await x.Statuses.CancelAsync(dto.PublicId, new OrderCancelRequest("Duplicada", null), OrderScope.Any, default);

        var after = await x.Statuses.ReopenAsync(dto.PublicId, new OrderReopenRequest("", "No era duplicada", null), OrderScope.Any, default);
        Assert.Equal(OrderStatuses.Submitted, after.Status);
        Assert.Equal((OrderStatuses.Cancelled, OrderStatuses.Submitted, "No era duplicada"), (await HistoryAsync(x, (await OrderAsync(x, dto.PublicId)).TransportOrderId))[^1]);
    }

    [Fact]
    public async Task Reopen_a_delivered_order_with_an_explicit_target()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);

        var after = await x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(" in_transit ", "Se marcó entregada por error", null), OrderScope.Any, default);
        Assert.Equal(OrderStatuses.InTransit, after.Status);
        Assert.Equal(new[] { ((string?)OrderStatuses.Delivered, OrderStatuses.InTransit, (string?)"Se marcó entregada por error") }, await HistoryAsync(x, o.TransportOrderId));
        Assert.Contains("\"to\":\"IN_TRANSIT\"", Assert.Single(x.Security.Actions("order_reopen")));
    }

    [Fact]
    public async Task Reopen_an_order_in_warehouse_is_allowed_too()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Submitted, 10m, statusCodeId: 5102);   // IN_WAREHOUSE (final)

        var after = await x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(OrderStatuses.Unassigned, "Sale del almacén", null), OrderScope.Any, default);
        Assert.Equal(OrderStatuses.Unassigned, after.Status);
    }

    [Fact]
    public async Task Reopen_without_target_and_without_a_previous_status_is_422()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);   // sin historial

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(null, "Razón", null), OrderScope.Any, default));
        Assert.Equal((422, "La orden no tiene un estatus anterior al que volver; indique el estatus destino."), (ex.StatusCode, ex.Message));
        Assert.Equal(OrderStatusService.NoReopenTargetMessage, ex.Message);
    }

    [Theory]
    [InlineData(OrderStatuses.Cancelled)]
    [InlineData(OrderStatuses.Delivered)]
    [InlineData(OrderStatuses.InWarehouse)]
    public async Task Reopen_to_a_final_status_is_422(string target)
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest(target, "Razón", null), OrderScope.Any, default));
        Assert.Equal((422, "Una orden reabierta debe quedar en una etapa en curso."), (ex.StatusCode, ex.Message));
        Assert.Equal(OrderStatuses.Delivered, (await OrderAsync(x, o.PublicId)).Status!.InternalCode);
        Assert.Empty(await HistoryAsync(x, o.TransportOrderId));
        Assert.Empty(x.Security.Actions("order_reopen"));
    }

    [Fact]
    public async Task Reopen_to_an_unknown_status_is_422()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Statuses.ReopenAsync(o.PublicId, new OrderReopenRequest("NO_EXISTE", "Razón", null), OrderScope.Any, default));
        Assert.Equal("El estatus 'NO_EXISTE' no existe o no está habilitado para esta compañía.", ex.Message);
    }

    [Fact]
    public async Task Without_reopen_a_final_status_still_admits_no_transitions()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 10m);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => MoveAsync(x, o.PublicId, OrderStatuses.InTransit));
        Assert.Equal("'DELIVERED' es terminal: no admite más transiciones.", ex.Message);
    }
}
