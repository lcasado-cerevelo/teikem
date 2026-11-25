using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 246 (Lote F, P4) — origen, identidad de importación y estado QuickBooks de las órdenes de compra en PurchaseOrderService (InMemory con WmsFixture):
/// el alta a mano y Reordenar nacen TEIKEM; CreateImportedAsync guarda origen e ImportKey y, con send, deja DRAFT → SENT con el comentario; un origen NULL se
/// expone como TEIKEM; la ficha trae etiqueta, estado y quién/cuándo exportó y marcó; la lista filtra por origen y estado (combinados con los demás filtros) y
/// el filtro quickBooks=X devuelve EXACTAMENTE las órdenes cuyo DTO dice X; valores inválidos → 400; editar, enviar y cancelar no tocan las marcas.
/// Lo que exige SQL Server real (reversión de la transacción y el índice único de ImportKey) está en <see cref="PurchaseOrderOriginSqlTests"/>.
/// </summary>
public sealed class PurchaseOrderOriginTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<PurchaseOrderService>();
        s.AddSingleton<PurchaseShortageService>();
    });

    private static async Task<Supplier> AddSupplierAsync(WmsFixture f, string name = "Proveedor Uno")
    {
        var s = new Supplier { TenantId = f.Tenant.TenantId!.Value, Name = name, IsActive = true };
        f.Db.Set<Supplier>().Add(s);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return s;
    }

    private static async Task<PurchaseOrder> StoredAsync(WmsFixture f, Guid publicId)
    {
        f.Db.ChangeTracker.Clear();
        return await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.PublicId == publicId);
    }

    [Fact]
    public async Task Manual_and_reorder_orders_are_born_teikem()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        var teikemId = f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.Teikem);

        var manual = await orders.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 4m, 2m) }), default);
        Assert.Equal((PurchaseOrderOrigins.Teikem, "TEIKEM", QuickBooksSyncStates.NotExportable), (manual.OriginCode, manual.Origin, manual.QuickBooksState));
        Assert.True(manual.QuickBooksExportedAtUtc is null && manual.QuickBooksExportedByName is null && manual.QuickBooksAtUtc is null && manual.QuickBooksByName is null);
        var stored = await StoredAsync(f, manual.PublicId);
        Assert.Equal(((int?)teikemId, (string?)null), (stored.OriginLookupId, stored.ImportKey));
        // enviada: ya se puede pasar a QuickBooks
        Assert.Equal(QuickBooksSyncStates.Pending, (await orders.SendAsync(manual.PublicId, null, default)).QuickBooksState);

        // Reordenar un faltante: la orden nueva también es TEIKEM (PurchaseShortageService llama a CreateDraftAsync sin origen).
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = "PO-09001",
            OrderDate = TenantClock.Default.Today, StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        var line = new PurchaseOrderLine { PurchaseOrderId = po.PurchaseOrderId, ProductId = pa.ProductId, QtyOrdered = 5m, QtyReceived = 3m, UnitCost = 2m };
        f.Db.Set<PurchaseOrderLine>().Add(line);
        var asn = new Asn
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, PurchaseOrderId = po.PurchaseOrderId,
            StatusCodeId = f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Received), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Asn>().Add(asn);
        await f.Db.SaveChangesAsync();
        f.Db.Set<ReceiptHeader>().Add(new ReceiptHeader
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, AsnId = asn.AsnId,
            ReceiptTypeLookupId = f.LookupId(LookupDomains.ReceiptType, ReceiptTypes.Asn), Number = "REC-09001",
            StatusCodeId = f.StatusId(StatusDomains.ReceiptStatus, ReceiptStatuses.Received), IsActive = true, CreatedAtUtc = DateTime.UtcNow, ReceivedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var resolved = await f.Get<PurchaseShortageService>().ResolveAsync(po.PublicId, line.PurchaseOrderLineId, new ShortageResolveRequest(ShortageActions.Reorder), default);
        var reorder = Assert.IsType<PurchaseOrderDto>(resolved.Reorder);
        Assert.Equal(PurchaseOrderOrigins.Teikem, reorder.OriginCode);
        Assert.Equal((int?)teikemId, (await StoredAsync(f, reorder.PublicId)).OriginLookupId);
    }

    [Fact]
    public async Task Imported_order_keeps_its_origin_and_import_key_and_send_leaves_it_sent_with_the_comment()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        PurchaseOrderCreateRequest Req() => new(supplier.SupplierId, w.PublicId, Notes: "Importada de archivo. [IMP:PROVEEDORUNO|D-1]",
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 3m, 1.5m) });

        var sent = await orders.CreateImportedAsync(Req(), "excel", " IMP:PROVEEDORUNO|D-1 ", send: true, sendComment: "Enviada al importar", default);
        Assert.Equal((PurchaseOrderStatuses.Sent, PurchaseOrderOrigins.Excel, "EXCEL", QuickBooksSyncStates.Pending),
            (sent.StatusCode, sent.OriginCode, sent.Origin, sent.QuickBooksState));
        var stored = await StoredAsync(f, sent.PublicId);
        Assert.Equal(((int?)f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.Excel), "IMP:PROVEEDORUNO|D-1"), (stored.OriginLookupId, stored.ImportKey));
        Assert.Equal(new[] { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent }, await f.HistoryCodesAsync(EntityTypes.PurchaseOrder, sent.Id));
        var history = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == sent.Id).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.Equal(((int?)null, (string?)null, "Enviada al importar"), (history[0].FromStatusCodeId, history[0].Comment, history[1].Comment));

        // sin enviar queda en borrador; la de QuickBooks nunca se exporta
        var fromQb = await orders.CreateImportedAsync(Req(), PurchaseOrderOrigins.QuickBooks, "QB:PROVEEDORUNO|D-2", send: false, sendComment: null, default);
        Assert.Equal((PurchaseOrderStatuses.Draft, PurchaseOrderOrigins.QuickBooks, QuickBooksSyncStates.FromQuickBooks), (fromQb.StatusCode, fromQb.OriginCode, fromQb.QuickBooksState));
        Assert.Equal(new[] { PurchaseOrderStatuses.Draft }, await f.HistoryCodesAsync(EntityTypes.PurchaseOrder, fromQb.Id));

        // origen o identidad inválidos → 400, sin crear nada
        var count = await f.Db.Set<PurchaseOrder>().CountAsync();
        var badOrigin = await Assert.ThrowsAsync<ValidationException>(() => orders.CreateImportedAsync(Req(), "CSV", "IMP:X|1", false, null, default));
        Assert.Equal("El origen «CSV» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.", Assert.Single(badOrigin.Errors!["origin"]));
        var longKey = await Assert.ThrowsAsync<ValidationException>(() => orders.CreateImportedAsync(Req(), "EXCEL", new string('K', 251), false, null, default));
        Assert.Equal(PurchaseOrderService.ImportKeyTooLongMessage, Assert.Single(longKey.Errors!["importKey"]));
        Assert.Equal(count, await f.Db.Set<PurchaseOrder>().CountAsync());
    }

    [Fact]
    public async Task Null_origin_reads_as_teikem_and_the_dto_says_who_exported_and_marked()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        f.Db.Users.AddRange(new ApplicationUser { Id = 501, UserName = "ana@x.test", Email = "ana@x.test", FullName = "Ana Compras" },
            new ApplicationUser { Id = 502, UserName = "beto@x.test", Email = "beto@x.test" });
        await f.Db.SaveChangesAsync();
        var exportedAt = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);
        var markedAt = new DateTime(2026, 11, 21, 9, 30, 0, DateTimeKind.Utc);
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = "PO-00777",
            OrderDate = new DateOnly(2026, 11, 19), StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow, OriginLookupId = null, QuickBooksExportedAt = exportedAt, QuickBooksExportedBy = 501, QuickBooksAt = markedAt, QuickBooksBy = 502,
        };
        f.Db.Set<PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var dto = await f.Get<PurchaseOrderService>().GetAsync(po.PublicId, default);
        Assert.Equal((PurchaseOrderOrigins.Teikem, "TEIKEM", QuickBooksSyncStates.InQuickBooks), (dto.OriginCode, dto.Origin, dto.QuickBooksState));
        Assert.Equal(((DateTime?)exportedAt, "Ana Compras", (DateTime?)markedAt, "beto@x.test"),
            (dto.QuickBooksExportedAtUtc, dto.QuickBooksExportedByName, dto.QuickBooksAtUtc, dto.QuickBooksByName));
        // la lista y la fuente de datos dicen lo mismo que la ficha
        var item = Assert.Single((await f.Get<PurchaseOrderService>().ListAsync(new PurchaseOrderQuery(Origins: new[] { "teikem" }), default)).Items);
        Assert.Equal((dto.OriginCode, dto.QuickBooksState), (item.OriginCode, item.QuickBooksState));
    }

    [Fact]
    public async Task List_filters_by_origin_and_by_quickbooks_state_combined_with_the_other_filters()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var s1 = await AddSupplierAsync(f, "Proveedor Uno");
        var s2 = await AddSupplierAsync(f, "Distribuidora Dos");
        var o = OriginIds(f);
        int St(string code) => f.StatusId(StatusDomains.PurchaseOrderStatus, code);
        var seeded = new (string Number, int Supplier, int? Origin, string Status, bool Exported, bool Marked)[]
        {
            ("PO-00001", s1.SupplierId, null, PurchaseOrderStatuses.Sent, false, false),                // TEIKEM (NULL), por pasar
            ("PO-00002", s1.SupplierId, o[1], PurchaseOrderStatuses.Sent, true, false),                 // TEIKEM, exportada
            ("PO-00003", s2.SupplierId, o[2], PurchaseOrderStatuses.Received, false, false),            // EXCEL, por pasar
            ("PO-00004", s2.SupplierId, o[2], PurchaseOrderStatuses.Draft, false, false),               // EXCEL, no exportable
            ("PO-00005", s1.SupplierId, o[3], PurchaseOrderStatuses.Sent, false, false),                // QUICKBOOKS
            ("PO-00006", s2.SupplierId, null, PurchaseOrderStatuses.Cancelled, true, false),            // exportada y luego cancelada: EXPORTED
            ("PO-00007", s2.SupplierId, o[1], PurchaseOrderStatuses.Partial, true, true),               // en QuickBooks
        };
        foreach (var r in seeded)
            f.Db.Set<PurchaseOrder>().Add(NewOrder(WmsFixture.TenantId, r.Number, r.Supplier, w.WarehouseId, r.Origin, St(r.Status), r.Exported, r.Marked));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var svc = f.Get<PurchaseOrderService>();
        async Task<string[]> Numbers(PurchaseOrderQuery q) => (await svc.ListAsync(q, default)).Items.Select(i => i.Number).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "PO-00001", "PO-00002", "PO-00006", "PO-00007" }, await Numbers(new PurchaseOrderQuery(Origins: new[] { "TEIKEM" })));
        Assert.Equal(new[] { "PO-00003", "PO-00004" }, await Numbers(new PurchaseOrderQuery(Origins: new[] { "EXCEL" })));
        Assert.Equal(new[] { "PO-00003", "PO-00004", "PO-00005" }, await Numbers(new PurchaseOrderQuery(Origins: new[] { "excel, QUICKBOOKS" })));
        Assert.Equal(new[] { "PO-00001", "PO-00003" }, await Numbers(new PurchaseOrderQuery(QuickBooksStates: new[] { "PENDING" })));
        Assert.Equal(new[] { "PO-00002", "PO-00006" }, await Numbers(new PurchaseOrderQuery(QuickBooksStates: new[] { "EXPORTED" })));
        Assert.Equal(new[] { "PO-00007" }, await Numbers(new PurchaseOrderQuery(QuickBooksStates: new[] { "IN_QUICKBOOKS" })));
        Assert.Equal(new[] { "PO-00005" }, await Numbers(new PurchaseOrderQuery(QuickBooksStates: new[] { "FROM_QUICKBOOKS" })));
        Assert.Equal(new[] { "PO-00004" }, await Numbers(new PurchaseOrderQuery(QuickBooksStates: new[] { "NOT_EXPORTABLE" })));
        Assert.Equal(new[] { "PO-00001", "PO-00002", "PO-00003", "PO-00006" }, await Numbers(new PurchaseOrderQuery(QuickBooksStates: new[] { "pending", "EXPORTED" })));
        // combinados con estatus, proveedor y búsqueda
        Assert.Equal(new[] { "PO-00001" }, await Numbers(new PurchaseOrderQuery(Status: new[] { PurchaseOrderStatuses.Sent }, Origins: new[] { "TEIKEM" }, QuickBooksStates: new[] { "PENDING" })));
        Assert.Equal(new[] { "PO-00003" }, await Numbers(new PurchaseOrderQuery(SupplierIds: new[] { s2.SupplierId }, QuickBooksStates: new[] { "PENDING" })));
        Assert.Equal(new[] { "PO-00006" }, await Numbers(new PurchaseOrderQuery(Search: "Distribuidora", Origins: new[] { "TEIKEM" }, QuickBooksStates: new[] { "EXPORTED" })));
        Assert.Empty(await Numbers(new PurchaseOrderQuery(Origins: new[] { "QUICKBOOKS" }, QuickBooksStates: new[] { "PENDING" })));
        // vacío = sin filtro
        Assert.Equal(7, (await svc.ListAsync(new PurchaseOrderQuery(Origins: new[] { " " }, QuickBooksStates: Array.Empty<string>()), default)).Total);
    }

    [Fact]
    public async Task Filter_quickbooks_returns_exactly_the_orders_whose_dto_says_that_state()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var count = await SeedMatrixAsync(f.Db, WmsFixture.TenantId, supplier.SupplierId, w.WarehouseId, OriginIds(f),
            code => f.StatusId(StatusDomains.PurchaseOrderStatus, code));
        f.Db.ChangeTracker.Clear();
        await AssertFilterMatchesDtoAsync(f.Get<PurchaseOrderService>(), count);
    }

    [Fact]
    public async Task Invalid_origin_or_quickbooks_state_is_a_400_with_the_exact_message()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<PurchaseOrderService>();
        var origin = await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync(new PurchaseOrderQuery(Origins: new[] { "EXCEL", "PAPEL" }), default));
        Assert.Equal((400, "El origen «PAPEL» no es válido: use TEIKEM, EXCEL o QUICKBOOKS."), (origin.StatusCode, Assert.Single(origin.Errors!["origin"])));
        var state = await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync(new PurchaseOrderQuery(QuickBooksStates: new[] { "PENDING,MARKED" }), default));
        Assert.Equal((400, "El estado de QuickBooks «MARKED» no es válido: use PENDING, EXPORTED, IN_QUICKBOOKS, FROM_QUICKBOOKS o NOT_EXPORTABLE."),
            (state.StatusCode, Assert.Single(state.Errors!["quickBooks"])));
    }

    [Fact]
    public async Task Patch_send_and_cancel_do_not_touch_the_quickbooks_marks()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var svc = f.Get<PurchaseOrderService>();
        var draft = await svc.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 2m, 1m) }), default);
        var exportedAt = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        var markedAt = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
        var po = await f.Db.Set<PurchaseOrder>().SingleAsync(p => p.PublicId == draft.PublicId);
        (po.QuickBooksExportedAt, po.QuickBooksExportedBy, po.QuickBooksAt, po.QuickBooksBy) = (exportedAt, 1, markedAt, 1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        async Task AssertMarksAsync()
        {
            var s = await StoredAsync(f, draft.PublicId);
            Assert.Equal(((DateTime?)exportedAt, (int?)1, (DateTime?)markedAt, (int?)1), (s.QuickBooksExportedAt, s.QuickBooksExportedBy, s.QuickBooksAt, s.QuickBooksBy));
        }
        var patched = await svc.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(Notes: "Cambiada después de exportar",
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 5m, 1m) }), default);
        Assert.Equal(QuickBooksSyncStates.InQuickBooks, patched.QuickBooksState);
        await AssertMarksAsync();
        Assert.Equal(QuickBooksSyncStates.InQuickBooks, (await svc.SendAsync(draft.PublicId, null, default)).QuickBooksState);
        await AssertMarksAsync();
        Assert.Equal(QuickBooksSyncStates.InQuickBooks, (await svc.CancelAsync(draft.PublicId, new PurchaseOrderStatusRequest("ya no"), default)).QuickBooksState);
        await AssertMarksAsync();
    }

    // ---------------------------------------------------------------- apoyo compartido con la prueba contra SQL Server

    /// <summary>[0] = NULL, [1] = TEIKEM, [2] = EXCEL, [3] = QUICKBOOKS.</summary>
    private static int?[] OriginIds(WmsFixture f) => new int?[]
    {
        null, f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.Teikem), f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.Excel),
        f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.QuickBooks),
    };

    internal static PurchaseOrder NewOrder(int tenantId, string number, int supplierId, int warehouseId, int? originId, int statusId, bool exported, bool marked) => new()
    {
        PublicId = Guid.NewGuid(), TenantId = tenantId, SupplierId = supplierId, WarehouseId = warehouseId, Number = number, OrderDate = new DateOnly(2026, 11, 20),
        StatusCodeId = statusId, IsActive = true, CreatedAtUtc = DateTime.UtcNow, OriginLookupId = originId,
        QuickBooksExportedAt = exported ? new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc) : null,
        QuickBooksAt = marked ? new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc) : null,
    };

    /// <summary>Todas las combinaciones origen (NULL/TEIKEM/EXCEL/QUICKBOOKS) × estatus (los 5) × exportada × marcada. Devuelve cuántas sembró.</summary>
    internal static async Task<int> SeedMatrixAsync(TeikemDbContext db, int tenantId, int supplierId, int warehouseId, int?[] originIds, Func<string, int> statusId)
    {
        var n = 0;
        foreach (var origin in originIds)
        foreach (var status in new[] { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received, PurchaseOrderStatuses.Cancelled })
        foreach (var exported in new[] { false, true })
        foreach (var marked in new[] { false, true })
            db.Set<PurchaseOrder>().Add(NewOrder(tenantId, $"PO-M{++n:D4}", supplierId, warehouseId, origin, statusId(status), exported, marked));
        await db.SaveChangesAsync();
        return n;
    }

    /// <summary>Para cada estado, quickBooks=X devuelve exactamente las órdenes con DTO.QuickBooksState == X; cada orden cae en un solo estado.</summary>
    internal static async Task AssertFilterMatchesDtoAsync(PurchaseOrderService svc, int expectedCount)
    {
        var all = (await svc.ListAsync(new PurchaseOrderQuery(Take: 200), default)).Items;
        Assert.Equal(expectedCount, all.Count);
        var total = 0;
        foreach (var state in QuickBooksSyncStates.All)
        {
            var expected = all.Where(d => d.QuickBooksState == state).Select(d => d.Number).OrderBy(x => x).ToArray();
            var page = await svc.ListAsync(new PurchaseOrderQuery(Take: 200, QuickBooksStates: new[] { state }), default);
            var got = page.Items.Select(d => d.Number).OrderBy(x => x).ToArray();
            Assert.True(expected.Length > 0, $"La matriz no sembró ninguna orden en {state}.");
            Assert.Equal(expected, got);
            Assert.Equal(expected.Length, page.Total);
            Assert.All(page.Items, d => Assert.Equal(state, d.QuickBooksState));
            total += got.Length;
        }
        Assert.Equal(expectedCount, total);
    }
}

/// <summary>
/// Lote 246 (Lote F, P4) — lo que solo prueba SQL Server REAL (base desechable de NotificationSqlFixture; se omite sin TEIKEM_TEST_SQLSERVER): CreateImportedAsync
/// revierte TODO si DRAFT → SENT falla (no queda borrador huérfano ni se gasta el número, y la siguiente importación la crea); el índice único filtrado
/// UX_PurchaseOrder_ImportKey da 409 «Esa orden ya se importó…» (por compañía y solo entre las activas); y los filtros de origen y estado se traducen a SQL con
/// la misma respuesta que el DTO (semántica de NULL incluida).
/// </summary>
public sealed class PurchaseOrderOriginSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    private sealed class SqlLookups(NotificationSqlFixture fx) : ILookupCache
    {
        public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default) => (await TryGetIdAsync(entity, code, ct)) ?? throw new InvalidOperationException($"{entity}/{code}");
        public async Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().Where(l => l.Entity == entity && l.InternalCode == code).Select(l => (int?)l.LookupCodeId).FirstOrDefaultAsync(ct);
        }
        public async Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().AsNoTracking().FirstOrDefaultAsync(l => l.LookupCodeId == lookupCodeId, ct);
        }
        public async Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().AsNoTracking().Where(l => l.Entity == entity).ToListAsync(ct);
        }
        public void Invalidate() { }
    }

    /// <summary>Catálogos globales (la base de pruebas no tiene el seed): etapas, entidad PURCHASE_ORDER, capacidad EDIT_PURCHASE_ORDER (sin reglas = permitida), origen y
    /// estatus de la orden de compra. Idempotente.</summary>
    private async Task<Dictionary<string, int>> SeedCatalogsAsync()
    {
        await SeedGate.WaitAsync();
        try
        {
            await using var db = fx.NewDb(new TenantContext());
            async Task<LookupCode> L(string entity, string code)
            {
                var found = await db.Set<LookupCode>().FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code);
                if (found is not null) return found;
                var l = new LookupCode { Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true, IsSystem = true };
                db.Set<LookupCode>().Add(l);
                await db.SaveChangesAsync();
                return l;
            }
            var pipe = await L(LookupDomains.StageKind, StageKinds.Pipeline);
            var term = await L(LookupDomains.StageKind, StageKinds.Terminal);
            await L(LookupDomains.EntityType, EntityTypes.PurchaseOrder);
            await L(LookupDomains.Capability, Capabilities.EditPurchaseOrder);
            foreach (var c in PurchaseOrderOrigins.All) await L(PurchaseOrderOrigins.Domain, c);
            var statuses = new Dictionary<string, int>();
            var sort = 0;
            foreach (var (code, kind, initial) in new[] { (PurchaseOrderStatuses.Draft, pipe, true), (PurchaseOrderStatuses.Sent, pipe, false), (PurchaseOrderStatuses.Partial, pipe, false),
                         (PurchaseOrderStatuses.Received, term, false), (PurchaseOrderStatuses.Cancelled, term, false) })
            {
                sort++;
                var s = await db.StatusCodes.FirstOrDefaultAsync(x => x.Entity == StatusDomains.PurchaseOrderStatus && x.InternalCode == code);
                if (s is null)
                {
                    s = new StatusCode { Entity = StatusDomains.PurchaseOrderStatus, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId,
                        SortOrder = sort, IsInitial = initial, IsActive = true };
                    db.StatusCodes.Add(s);
                    await db.SaveChangesAsync();
                }
                statuses[code] = s.StatusCodeId;
            }
            return statuses;
        }
        finally { SeedGate.Release(); }
    }

    /// <summary>Un contenedor por «petición»: su propio DbContext, el contador real (NumberSequenceService) y los servicios reales; el usuario es administrador.</summary>
    private ServiceProvider Session(int tenantId)
    {
        var tenant = new TenantContext { TenantId = tenantId, UserId = null, IsAuthenticated = true, IsPlatformAdmin = true };
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton(tenant);
        s.AddSingleton<ITenantContext>(tenant);
        s.AddSingleton(fx.NewDb(tenant));
        s.AddSingleton<ILookupCache>(new SqlLookups(fx));
        s.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        s.AddSingleton<ISecurityEventWriter, ReceivingNullSecurityEventWriter>();
        s.AddSingleton<PermissionService>();
        s.AddSingleton(sp => new StatusService(sp.GetRequiredService<TeikemDbContext>(), tenant, sp.GetRequiredService<ILookupCache>(), Array.Empty<IStatusTransitionEffect>(),
            sp.GetRequiredService<PermissionService>()));
        s.AddSingleton<INumberSequenceService, NumberSequenceService>();
        s.AddSingleton<ITenantClock>(TenantClock.Default);
        s.AddSingleton<PurchaseOrderService>();
        return s.BuildServiceProvider();
    }

    private sealed record World(int TenantId, int SupplierId, Guid WarehousePublicId, int WarehouseId, Guid ProductPublicId, Dictionary<string, int> Statuses);

    private async Task<World> SeedAsync(string name)
    {
        var statuses = await SeedCatalogsAsync();
        var tenantId = await fx.NewTenantAsync(name);
        await using var db = fx.NewDb(tenantId);
        var supplier = new Supplier { TenantId = tenantId, Name = "Proveedor Uno", IsActive = true };
        var w = new Warehouse { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "W1", Name = "Almacén", CountryLookupId = 1, StatusCodeId = 1, IsActive = true };
        var p = new Product { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = "PA", Name = "Producto PA", BaseUomLookupId = 1, TrackingTypeLookupId = 1, IsActive = true };
        db.Set<Supplier>().Add(supplier);
        db.Warehouses.Add(w);
        db.Products.Add(p);
        await db.SaveChangesAsync();
        return new World(tenantId, supplier.SupplierId, w.PublicId, w.WarehouseId, p.PublicId, statuses);
    }

    private static PurchaseOrderCreateRequest Req(World w) => new(w.SupplierId, w.WarehousePublicId, Notes: "Importada. [IMP:PROVEEDORUNO|D-1]",
        Lines: new[] { new PurchaseOrderLineRequest(w.ProductPublicId, 2m, 1.25m) });

    private async Task<List<PurchaseOrder>> OrdersAsync(int tenantId)
    {
        await using var db = fx.NewDb(tenantId);
        return await db.Set<PurchaseOrder>().AsNoTracking().OrderBy(p => p.PurchaseOrderId).ToListAsync();
    }

    [Fact]
    public async Task A_failed_send_rolls_back_the_whole_import_and_the_next_import_creates_the_order()
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("po-origin-rollback");
        // SENT apagado para esta compañía: DRAFT → SENT falla dentro de la transacción
        await using (var db = fx.NewDb(w.TenantId))
        {
            db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = w.TenantId, StatusCodeId = w.Statuses[PurchaseOrderStatuses.Sent], IsEnabled = false });
            await db.SaveChangesAsync();
        }
        using (var s = Session(w.TenantId))
            await Assert.ThrowsAsync<StatusRuleException>(() => s.GetRequiredService<PurchaseOrderService>()
                .CreateImportedAsync(Req(w), PurchaseOrderOrigins.Excel, "IMP:PROVEEDORUNO|D-1", send: true, sendComment: "al importar", default));
        Assert.Empty(await OrdersAsync(w.TenantId));
        await using (var db = fx.NewDb(w.TenantId))
        {
            Assert.False(await db.Set<PurchaseOrderLine>().AnyAsync(l => db.Set<PurchaseOrder>().Any(p => p.PurchaseOrderId == l.PurchaseOrderId)));
            Assert.False(await db.EntityStatusHistories.AnyAsync());
            var o = await db.StatusCodeOverrides.SingleAsync();
            o.IsEnabled = true;
            await db.SaveChangesAsync();
        }

        PurchaseOrderDto created;
        using (var s = Session(w.TenantId))
            created = await s.GetRequiredService<PurchaseOrderService>()
                .CreateImportedAsync(Req(w), PurchaseOrderOrigins.Excel, "IMP:PROVEEDORUNO|D-1", send: true, sendComment: "al importar", default);
        Assert.Equal((PurchaseOrderStatuses.Sent, PurchaseOrderOrigins.Excel, QuickBooksSyncStates.Pending, "PO-00001"),
            (created.StatusCode, created.OriginCode, created.QuickBooksState, created.Number));
        var stored = Assert.Single(await OrdersAsync(w.TenantId));
        Assert.Equal("IMP:PROVEEDORUNO|D-1", stored.ImportKey);
    }

    [Fact]
    public async Task The_same_import_key_twice_is_a_409_per_company_and_only_among_active_orders()
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("po-origin-key");
        const string key = "IMP:PROVEEDORUNO|D-7";
        PurchaseOrderDto first;
        using (var s = Session(w.TenantId))
            first = await s.GetRequiredService<PurchaseOrderService>().CreateImportedAsync(Req(w), PurchaseOrderOrigins.Excel, key, false, null, default);

        using (var s = Session(w.TenantId))
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => s.GetRequiredService<PurchaseOrderService>()
                .CreateImportedAsync(Req(w), PurchaseOrderOrigins.Excel, key, send: true, sendComment: null, default));
            Assert.Equal(("Esa orden ya se importó (otra importación la creó al mismo tiempo).", 409), (ex.Message, ex.StatusCode));
        }
        Assert.Single(await OrdersAsync(w.TenantId));

        // otra compañía con la misma identidad: se crea
        var other = await SeedAsync("po-origin-key-otra");
        using (var s = Session(other.TenantId))
            await s.GetRequiredService<PurchaseOrderService>().CreateImportedAsync(Req(other), PurchaseOrderOrigins.Excel, key, false, null, default);
        Assert.Single(await OrdersAsync(other.TenantId));

        // dada de baja la primera, la identidad queda libre (el índice solo cuenta las activas)
        using (var s = Session(w.TenantId))
            await s.GetRequiredService<PurchaseOrderService>().DeleteAsync(first.PublicId, default);
        using (var s = Session(w.TenantId))
            await s.GetRequiredService<PurchaseOrderService>().CreateImportedAsync(Req(w), PurchaseOrderOrigins.Excel, key, false, null, default);
        Assert.Equal(new[] { false, true }, (await OrdersAsync(w.TenantId)).Select(p => p.IsActive).ToArray());
    }

    [Fact]
    public async Task Origin_and_quickbooks_filters_translate_to_sql_and_match_the_dto()
    {
        if (fx.ConnectionString is null) return;
        var w = await SeedAsync("po-origin-filters");
        var lookups = new SqlLookups(fx);
        var originIds = new int?[] { null }.Concat(await Task.WhenAll(PurchaseOrderOrigins.All.Select(async c => (int?)await lookups.GetIdAsync(PurchaseOrderOrigins.Domain, c)))).ToArray();
        int count;
        await using (var db = fx.NewDb(w.TenantId))
            count = await PurchaseOrderOriginTests.SeedMatrixAsync(db, w.TenantId, w.SupplierId, w.WarehouseId, originIds, code => w.Statuses[code]);

        using var s = Session(w.TenantId);
        var svc = s.GetRequiredService<PurchaseOrderService>();
        await PurchaseOrderOriginTests.AssertFilterMatchesDtoAsync(svc, count);
        // TEIKEM incluye las de origen NULL; varios orígenes y estados a la vez
        Assert.Equal(40, (await svc.ListAsync(new PurchaseOrderQuery(Take: 200, Origins: new[] { "TEIKEM" }), default)).Total);
        Assert.Equal(20, (await svc.ListAsync(new PurchaseOrderQuery(Take: 200, Origins: new[] { "QUICKBOOKS" }), default)).Total);
        var mixed = await svc.ListAsync(new PurchaseOrderQuery(Take: 200, Origins: new[] { "EXCEL", "QUICKBOOKS" }, QuickBooksStates: new[] { "PENDING", "FROM_QUICKBOOKS" },
            Status: new[] { PurchaseOrderStatuses.Sent }), default);
        // EXCEL en SENT sin exportar ni marcar = 1 (PENDING); QUICKBOOKS en SENT = 4 (FROM_QUICKBOOKS)
        Assert.Equal(5, mixed.Total);
    }
}
