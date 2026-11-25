using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
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
/// Lote 246 (Lote F, P3) — plantilla de importación de órdenes de compra definida por el usuario (ImportTemplate Kind PURCHASE_ORDER) y la importación con el origen
/// TEMPLATE (InMemory con WmsFixture): alta y reglas de la plantilla (documentNumber obligatorio, sin cliente, nombre único por tipo, 404 cruzado y entre compañías, la ruta
/// de envíos no la acepta), agrupación por número de documento con proveedor fijo, BAD_VALUE (fecha, número, fila sin número), COST_CONFLICT, faltantes, vista previa,
/// creación SENT atómica con origen EXCEL e ImportKey, no duplicar (también frente a GENERIC) y aislamiento por compañía. La concurrencia real (dos importaciones a la
/// vez del mismo documento) está en <see cref="PurchaseOrderImportTemplateSqlTests"/>.
/// </summary>
public sealed class PurchaseOrderImportTemplateTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<PurchaseOrderService>();
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<PurchaseOrderImportService>();
    });

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static async Task<Supplier> AddSupplierAsync(WmsFixture f, string name, int tenantId = WmsFixture.TenantId)
    {
        var supplier = new Supplier { TenantId = tenantId, Name = name, IsActive = true };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return supplier;
    }

    /// <summary>«Excel compras»: ';' sin encabezado, documento, SKU, cantidad, costo y fecha; proveedor en la columna 6 o fijo (ACME).</summary>
    private static ImportTemplateCreateRequest ExcelTemplate(string name = "Excel compras", string? supplier = "ACME") => new(name,
        new List<ImportTemplateColumnDto> { new(1, "documentNumber"), new(2, "sku"), new(3, "quantity"), new(4, "unitCost"), new(5, "orderDate"), new(6, "supplier") },
        supplier is null ? null : new Dictionary<string, string> { ["supplier"] = supplier }, Delimiter: ";", HasHeader: false);

    private static Task<ImportTemplateDto> CreateTemplateAsync(WmsFixture f, ImportTemplateCreateRequest? req = null)
        => f.Get<ImportTemplateService>().CreateAsync(req ?? ExcelTemplate(), ImportKinds.PurchaseOrder, default);

    private static Task<PurchaseOrderImportResultDto> ImportAsync(WmsFixture f, Guid templateId, string csv, bool commit, string status = "DRAFT")
        => f.Get<PurchaseOrderImportService>().ImportAsync(null, Bytes(csv), "compras.csv", commit, status, null, templateId, default);

    // ---------------------------------------------------------------- plantillas

    [Fact]
    public async Task La_plantilla_exige_documentNumber_no_lleva_cliente_y_su_nombre_es_unico_por_tipo()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ImportTemplateService>();

        var tpl = await CreateTemplateAsync(f);
        Assert.Equal((ImportKinds.PurchaseOrder, ";", false, "ACME"), (tpl.Kind, tpl.Delimiter, tpl.HasHeader, tpl.Defaults!["supplier"]));
        Assert.Equal(new[] { "documentNumber", "sku", "quantity", "unitCost", "orderDate", "supplier" }, tpl.Columns.Select(c => c.Field).ToArray());

        var noDoc = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ImportTemplateCreateRequest("Sin número",
            new List<ImportTemplateColumnDto> { new(1, "sku"), new(2, "quantity"), new(3, "supplier") }), ImportKinds.PurchaseOrder, default));
        Assert.Equal("La plantilla debe incluir la columna documentNumber.", Assert.Single(noDoc.Errors!["columns"]));
        var noSupplier = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(ExcelTemplate("Sin proveedor", null) with
            { Columns = new List<ImportTemplateColumnDto> { new(1, "documentNumber"), new(2, "sku"), new(3, "quantity") } }, ImportKinds.PurchaseOrder, default));
        Assert.Equal("La plantilla debe indicar el proveedor: en la columna supplier o como valor fijo.", Assert.Single(noSupplier.Errors!["columns"]));
        var docDefault = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(ExcelTemplate("Número fijo") with
            { Defaults = new Dictionary<string, string> { ["documentNumber"] = "1" } }, ImportKinds.PurchaseOrder, default));
        Assert.Equal("El campo documentNumber no admite valor por defecto.", Assert.Single(docDefault.Errors!["defaults.documentNumber"]));

        // el mismo nombre como plantilla de envíos convive (el 409 del índice único se prueba contra SQL Server real)
        var asOrder = await svc.CreateAsync(new ImportTemplateCreateRequest("Excel compras",
            new List<ImportTemplateColumnDto> { new(1, "consigneeName"), new(2, "pieces") }), ImportKinds.Order, default);
        Assert.Equal(ImportKinds.Order, asOrder.Kind);

        // sin cliente: ni cliente ni cliente por defecto, al crear ni al editar
        var client = await f.AddClientAsync("C1");
        var withClient = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(ExcelTemplate("Con cliente") with { ClientPublicId = client.PublicId }, ImportKinds.PurchaseOrder, default));
        Assert.Equal("Este tipo de plantilla no lleva cliente.", Assert.Single(withClient.Errors!["clientPublicId"]));
        var withDefault = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(ExcelTemplate("Con cliente") with { DefaultClientPublicId = client.PublicId }, ImportKinds.PurchaseOrder, default));
        Assert.Equal("Este tipo de plantilla no lleva cliente.", Assert.Single(withDefault.Errors!["defaultClientPublicId"]));
        var patchClient = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(ClientPublicId: client.PublicId), ImportKinds.PurchaseOrder, default));
        Assert.Equal("Este tipo de plantilla no lleva cliente.", Assert.Single(patchClient.Errors!["clientPublicId"]));
        var patchDefault = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(DefaultClientPublicId: client.PublicId), ImportKinds.PurchaseOrder, default));
        Assert.Equal("Este tipo de plantilla no lleva cliente.", Assert.Single(patchDefault.Errors!["defaultClientPublicId"]));
        // productos y proveedores sí admiten (no cambian)
        Assert.True(ImportEntities.TryGet(ImportKinds.Supplier)!.AllowsClient);

        // varias líneas por envío: solo plantillas de envíos
        var multi = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(ExcelTemplate("Multi") with { MultiLine = true }, ImportKinds.PurchaseOrder, default));
        Assert.True(multi.Errors!.ContainsKey("multiLine"));

        // editar, baja y reactivación
        var renamed = await svc.UpdateAsync(tpl.PublicId, new ImportTemplatePatchRequest(Name: "Excel compras 2"), ImportKinds.PurchaseOrder, default);
        Assert.Equal("Excel compras 2", renamed.Name);
        Assert.False((await svc.DeactivateAsync(tpl.PublicId, ImportKinds.PurchaseOrder, default)).IsActive);
        Assert.Empty(await svc.GetListAsync(ImportKinds.PurchaseOrder, null, null, false, default));
        Assert.Single(await svc.GetListAsync(ImportKinds.PurchaseOrder, null, null, true, default));
        Assert.True((await svc.ReactivateAsync(tpl.PublicId, ImportKinds.PurchaseOrder, default)).IsActive);
    }

    [Fact]
    public async Task La_plantilla_no_se_ve_desde_otros_tipos_ni_desde_otra_compania()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ImportTemplateService>();
        var tpl = await CreateTemplateAsync(f);

        Assert.Equal("Plantilla de importación no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(tpl.PublicId, ImportKinds.Order, default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(tpl.PublicId, ImportKinds.Supplier, default));
        // la ruta de envíos no acepta el tipo
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => svc.GetListAsync(ImportKinds.Order, "PURCHASE_ORDER", null, false, default));
        Assert.Equal("Tipo de plantilla desconocido: PURCHASE_ORDER.", unknown.Message);
        Assert.Equal(new[] { "supplier", "documentNumber", "orderDate", "expectedDate", "sku", "description", "quantity", "unitCost" },
            ImportTemplateService.GetEntityFields(ImportKinds.PurchaseOrder).Select(x => x.Field).ToArray());

        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(tpl.PublicId, ImportKinds.PurchaseOrder, default));
            Assert.Empty(await svc.GetListAsync(ImportKinds.PurchaseOrder, null, null, true, default));
            await Assert.ThrowsAsync<NotFoundException>(() => f.Get<PurchaseOrderImportService>()
                .ImportAsync("TEMPLATE", Bytes("1;A;1"), "x.csv", false, "DRAFT", null, tpl.PublicId, default));
        }
    }

    // ---------------------------------------------------------------- importar

    [Fact]
    public async Task Agrupa_por_documento_con_proveedor_fijo_y_bloquea_lo_ilegible_lo_que_falta_y_los_costos_distintos()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ACME");
        await AddSupplierAsync(f, "PADIMONT");
        await f.AddProductAsync("A-1", purchaseCost: 2m);
        await f.AddProductAsync("B-2", purchaseCost: 3m);
        var tpl = await CreateTemplateAsync(f);
        var csv =
            "1045;A-1;10;1.50;2026-11-01;\r\n" +             // 1045 (ACME por omisión): dos productos
            "1045;B 2;5;;2026-11-01;\r\n" +                   //   B 2 → B-2 (alias), costo vacío = el del producto
            "\r\n" +                                          // fila vacía: se omite
            "1046;A-1;1,000;1.00;11/02/2026;PADIMONT\r\n" +   // 1046 de PADIMONT: mismo producto, mismo costo → se suman
            "1046;A-1;5;1;11/02/2026;PADIMONT\r\n" +
            ";A-1;3;1;2026-11-01;\r\n" +                      // fila sin número: su propio documento, bloqueado
            "1047;A-1;3;1;ayer;\r\n" +                        // fecha ilegible
            "1048;A-1;tres;1;2026-11-01;\r\n" +               // cantidad ilegible
            "1049;A-1;1;1.00;2026-11-01;\r\n" +               // mismo producto con costos distintos
            "1049;A-1;1;1.10;2026-11-01;\r\n" +
            "1050;ZZ-404;1;1;2026-11-01;\r\n" +               // producto que no existe
            "1051;A-1;1;1;2026-11-01;NADIE\r\n";             // proveedor que no existe

        var r = await ImportAsync(f, tpl.PublicId, csv, commit: false);

        Assert.Equal(("TEMPLATE", tpl.PublicId, "Excel compras", false), (r.Source, r.TemplatePublicId, r.TemplateName, r.Committed));
        Assert.Equal(8, r.TotalDocuments);
        var d1045 = Assert.Single(r.Documents, d => d.Number == "1045");
        Assert.Equal((PurchaseOrderImportRules.DocReady, "ACME", 2, new DateOnly(2026, 11, 1)), (d1045.Status, d1045.Vendor, d1045.Lines, d1045.Date));
        Assert.Contains(d1045.Issues, i => i.Code == PurchaseOrderImportRules.IssueAlias);
        Assert.Equal(1.5m * 10 + 3m * 5, d1045.Total);
        var d1046 = Assert.Single(r.Documents, d => d.Number == "1046");
        Assert.Equal((PurchaseOrderImportRules.DocReady, "PADIMONT", 1, 1005m), (d1046.Status, d1046.Vendor, d1046.Lines, d1046.Total));   // 1,000 = mil
        Assert.Contains(d1046.Issues, i => i.Code == PurchaseOrderImportRules.IssueMergedLines);

        var noNumber = Assert.Single(r.Documents, d => d.Number is null);
        Assert.Equal(PurchaseOrderImportRules.DocBlocked, noNumber.Status);
        Assert.Equal("La fila 6 no trae número de documento (documentNumber); sin él no se sabe a qué orden va.",
            Assert.Single(noNumber.Issues, i => i.Code == PurchaseOrderImportRules.IssueBadValue && i.Blocking).Message);
        var badDate = Assert.Single(r.Documents, d => d.Number == "1047");
        Assert.Equal(PurchaseOrderImportRules.DocBlocked, badDate.Status);
        Assert.Equal("La fecha «ayer» (orderDate) no es válida: use AAAA-MM-DD o MM/DD/AAAA.", Assert.Single(badDate.Issues, i => i.Code == PurchaseOrderImportRules.IssueBadValue).Message);
        var badQty = Assert.Single(r.Documents, d => d.Number == "1048");
        Assert.Equal(PurchaseOrderImportRules.DocBlocked, badQty.Status);
        Assert.Equal("«tres» (quantity) no es un número: use punto decimal, sin letras.", Assert.Single(badQty.Issues, i => i.Code == PurchaseOrderImportRules.IssueBadValue).Message);
        Assert.DoesNotContain(badQty.Issues, i => i.Code == PurchaseOrderImportRules.IssueZeroQty);
        var cost = Assert.Single(r.Documents, d => d.Number == "1049");
        Assert.Equal(PurchaseOrderImportRules.DocBlocked, cost.Status);
        Assert.Contains(cost.Issues, i => i.Code == PurchaseOrderImportRules.IssueCostConflict && i.Blocking && i.Message.Contains("(1, 1.1)"));
        var missing = Assert.Single(r.Documents, d => d.Number == "1050");
        Assert.Contains(missing.Issues, i => i.Code == PurchaseOrderImportRules.IssueMissingProduct && i.Blocking);
        Assert.Equal("ZZ-404", Assert.Single(r.MissingProducts).Sku);
        var noSupplier = Assert.Single(r.Documents, d => d.Number == "1051");
        Assert.Contains(noSupplier.Issues, i => i.Code == PurchaseOrderImportRules.IssueMissingSupplier && i.Blocking);
        Assert.Equal(new[] { "NADIE" }, r.MissingSuppliers);
        Assert.Equal((2, 6), (r.Ready, r.Blocked));
        Assert.Equal(0, await f.Db.Set<PurchaseOrder>().CountAsync());   // la vista previa no guarda nada
    }

    [Fact]
    public async Task Confirmar_crea_enviada_sin_mover_inventario_con_origen_excel_y_no_duplica_ni_frente_al_formato_generico()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ACME");
        await f.AddProductAsync("A-1", purchaseCost: 2m);
        await f.AddProductAsync("B-2", purchaseCost: 3m);
        var tpl = await CreateTemplateAsync(f);
        const string csv = "1045;A-1;10;1.50;2026-11-01;\r\n1045;B-2;5;;2026-11-01;\r\n";

        var done = await ImportAsync(f, tpl.PublicId, csv, commit: true, status: "SENT");

        Assert.Equal(1, done.Created);
        f.Db.ChangeTracker.Clear();
        var po = await f.Db.Set<PurchaseOrder>().AsNoTracking().Include(p => p.Lines).SingleAsync();
        Assert.Equal(("IMP:ACME|1045", (int?)f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.Excel)), (po.ImportKey, po.OriginLookupId));
        Assert.Equal("Importada con la plantilla «Excel compras» 1045. [IMP:ACME|1045]", po.Notes);
        Assert.Equal(new[] { 10m, 5m }, po.Lines.OrderBy(l => l.PurchaseOrderLineId).Select(l => l.QtyOrdered).ToArray());
        Assert.Equal(new[] { 1.5m, 3m }, po.Lines.OrderBy(l => l.PurchaseOrderLineId).Select(l => l.UnitCost).ToArray());
        Assert.Equal(new[] { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent }, await f.HistoryCodesAsync(EntityTypes.PurchaseOrder, po.PurchaseOrderId));
        Assert.Equal(0, await f.Db.Set<InventoryTransaction>().CountAsync());
        var dto = await f.Get<PurchaseOrderService>().GetAsync(po.PublicId, default);
        Assert.Equal((PurchaseOrderStatuses.Sent, PurchaseOrderOrigins.Excel, "PENDING"), (dto.StatusCode, dto.OriginCode, dto.QuickBooksState));

        // reimportar con la plantilla → DUPLICATE
        var again = await ImportAsync(f, tpl.PublicId, csv, commit: true, status: "SENT");
        Assert.Equal((0, 1), (again.Created, again.Duplicates));
        Assert.Equal(po.Number, Assert.Single(again.Documents).PurchaseOrderNumber);
        // el mismo documento por el formato genérico → también DUPLICATE (misma identidad IMP:)
        var generic = "Supplier,Number,Date,SKU,Qty,Cost\r\nACME,1045,2026-11-01,A-1,10,1.50\r\n";
        var viaGeneric = await f.Get<PurchaseOrderImportService>().ImportAsync("GENERIC", Bytes(generic), "o.csv", commit: true, "DRAFT", null, default);
        Assert.Equal((0, 1), (viaGeneric.Created, viaGeneric.Duplicates));
        Assert.Equal(1, await f.Db.Set<PurchaseOrder>().CountAsync());
    }

    [Fact]
    public async Task Origen_y_plantilla_se_validan_con_mensajes_exactos()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        var import = f.Get<PurchaseOrderImportService>();
        var tpl = await CreateTemplateAsync(f);
        var file = Bytes("1;A-1;1;1;2026-11-01;");

        var required = await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync("TEMPLATE", file, "x.csv", false, "DRAFT", null, null, default));
        Assert.Equal("Elija la plantilla de importación.", Assert.Single(required.Errors!["templatePublicId"]));
        var onlyTemplate = await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync("GENERIC", file, "x.csv", false, "DRAFT", null, tpl.PublicId, default));
        Assert.Equal("La plantilla solo se usa con el origen TEMPLATE.", Assert.Single(onlyTemplate.Errors!["templatePublicId"]));
        await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync("QUICKBOOKS", file, "x.csv", false, "DRAFT", null, tpl.PublicId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => import.ImportAsync("TEMPLATE", file, "x.csv", false, "DRAFT", null, Guid.NewGuid(), default));

        // una plantilla de otro tipo (proveedores) → 404 sin oráculo
        await f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Proveedores",
            new List<ImportTemplateColumnDto> { new(1, "name") }), ImportKinds.Supplier, default);
        // InMemory no genera el PublicId (en SQL es NEWID()): se le da uno propio para que no coincida con el de la plantilla de compras
        var supplierRow = await f.Db.ImportTemplates.SingleAsync(t => t.Kind == ImportKinds.Supplier);
        supplierRow.PublicId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => import.ImportAsync("TEMPLATE", file, "x.csv", false, "DRAFT", null, supplierRow.PublicId, default));

        // inactiva → 400
        await f.Get<ImportTemplateService>().DeactivateAsync(tpl.PublicId, ImportKinds.PurchaseOrder, default);
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync(null, file, "x.csv", false, "DRAFT", null, tpl.PublicId, default));
        Assert.Equal("La plantilla está inactiva; reactívela o elija otra.", Assert.Single(inactive.Errors!["templatePublicId"]));
    }

    [Fact]
    public async Task Cada_compania_importa_lo_suyo_con_la_misma_identidad_y_no_ve_proveedores_ajenos()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ACME");
        await f.AddProductAsync("A-1", purchaseCost: 2m);
        var tpl = await CreateTemplateAsync(f);
        await f.AddWarehouseAsync("W2", tenantId: WmsFixture.OtherTenantId);
        await AddSupplierAsync(f, "ACME", WmsFixture.OtherTenantId);
        await AddSupplierAsync(f, "SOLO-OTRA", WmsFixture.OtherTenantId);
        await f.AddProductAsync("A-1", tenantId: WmsFixture.OtherTenantId, purchaseCost: 2m);
        ImportTemplateDto otherTpl;
        using (f.AsTenant(WmsFixture.OtherTenantId)) otherTpl = await CreateTemplateAsync(f);
        const string csv = "1045;A-1;10;1.50;2026-11-01;\r\n";

        Assert.Equal(1, (await ImportAsync(f, tpl.PublicId, csv, commit: true)).Created);
        using (f.AsTenant(WmsFixture.OtherTenantId))
            Assert.Equal(1, (await ImportAsync(f, otherTpl.PublicId, csv, commit: true)).Created);   // misma identidad, otra compañía: se crea

        // un proveedor que solo existe en la otra compañía no se resuelve
        var r = await ImportAsync(f, tpl.PublicId, "2000;A-1;1;1;2026-11-01;SOLO-OTRA\r\n", commit: false);
        Assert.Contains(Assert.Single(r.Documents).Issues, i => i.Code == PurchaseOrderImportRules.IssueMissingSupplier);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(new[] { WmsFixture.TenantId, WmsFixture.OtherTenantId },
            await f.Db.Set<PurchaseOrder>().IgnoreQueryFilters().OrderBy(p => p.TenantId).Select(p => p.TenantId).ToArrayAsync());
    }
}

/// <summary>
/// Lote 246 (Lote F, P3) — SQL Server REAL (base desechable de NotificationSqlFixture; se omite sin TEIKEM_TEST_SQLSERVER): dos importaciones simultáneas del mismo documento
/// con la plantilla crean UNA sola orden y la otra sale DUPLICATE (por la lista de identidades o, si las dos la leyeron vacía, por UX_PurchaseOrder_ImportKey →
/// «Esa orden ya se importó…», que el importador informa como duplicado y no como error); y el nombre de la plantilla es único por compañía y tipo (409).
/// </summary>
public sealed class PurchaseOrderImportTemplateSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
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

    /// <summary>Catálogos globales mínimos (la base de pruebas no tiene el seed): etapas, entidad y capacidad de la orden de compra, origen y estatus. Idempotente.</summary>
    private async Task SeedCatalogsAsync()
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
            var sort = 0;
            foreach (var (code, kind, initial) in new[] { (PurchaseOrderStatuses.Draft, pipe, true), (PurchaseOrderStatuses.Sent, pipe, false), (PurchaseOrderStatuses.Partial, pipe, false),
                         (PurchaseOrderStatuses.Received, term, false), (PurchaseOrderStatuses.Cancelled, term, false) })
            {
                sort++;
                if (await db.StatusCodes.AnyAsync(x => x.Entity == StatusDomains.PurchaseOrderStatus && x.InternalCode == code)) continue;
                db.StatusCodes.Add(new StatusCode { Entity = StatusDomains.PurchaseOrderStatus, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
                    StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true });
                await db.SaveChangesAsync();
            }
        }
        finally { SeedGate.Release(); }
    }

    /// <summary>Un contenedor por «petición»: su propio DbContext, el contador real y los servicios reales; el usuario es administrador.</summary>
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
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<PurchaseOrderImportService>();
        return s.BuildServiceProvider();
    }

    [Fact]
    public async Task Two_simultaneous_commits_of_the_same_new_document_create_one_order_and_report_the_other_as_duplicate()
    {
        if (fx.ConnectionString is null) return;
        await SeedCatalogsAsync();
        var tenantId = await fx.NewTenantAsync("po-import-template-race");
        await using (var db = fx.NewDb(tenantId))
        {
            db.Set<Supplier>().Add(new Supplier { TenantId = tenantId, Name = "ACME", IsActive = true });
            db.Warehouses.Add(new Warehouse { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "W1", Name = "Almacén", CountryLookupId = 1, StatusCodeId = 1, IsActive = true });
            db.Products.Add(new Product { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = "A-1", Name = "Producto A-1", BaseUomLookupId = 1, TrackingTypeLookupId = 1, IsActive = true });
            await db.SaveChangesAsync();
        }
        Guid templateId;
        using (var s = Session(tenantId))
            templateId = (await s.GetRequiredService<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Excel compras",
                new List<ImportTemplateColumnDto> { new(1, "documentNumber"), new(2, "sku"), new(3, "quantity"), new(4, "unitCost") },
                new Dictionary<string, string> { ["supplier"] = "ACME" }, Delimiter: ";", HasHeader: false), ImportKinds.PurchaseOrder, default)).PublicId;
        using (var s = Session(tenantId))
        {
            var taken = await Assert.ThrowsAsync<ConflictException>(() => s.GetRequiredService<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Excel compras",
                new List<ImportTemplateColumnDto> { new(1, "documentNumber"), new(2, "sku"), new(3, "quantity"), new(4, "supplier") }), ImportKinds.PurchaseOrder, default));
            Assert.Equal(("Ya existe una plantilla de importación con ese nombre.", 409), (taken.Message, taken.StatusCode));
        }
        // varias rondas (documentos distintos) para que alguna caiga en la carrera de verdad: las dos leen la lista vacía y el índice único decide
        var keys = new List<string>();
        for (var round = 0; round < 8; round++)
        {
            var number = 1045 + round;
            var file = Encoding.UTF8.GetBytes($"{number};A-1;10;1.50\r\n");
            using var s1 = Session(tenantId);
            using var s2 = Session(tenantId);
            var results = await Task.WhenAll(
                Task.Run(() => s1.GetRequiredService<PurchaseOrderImportService>().ImportAsync(null, file, "a.csv", true, "SENT", null, templateId, default)),
                Task.Run(() => s2.GetRequiredService<PurchaseOrderImportService>().ImportAsync(null, file, "a.csv", true, "SENT", null, templateId, default)));

            Assert.Equal(1, results.Sum(r => r.Created));
            Assert.Equal(1, results.Sum(r => r.Duplicates));
            Assert.Equal(0, results.Sum(r => r.Failed));
            keys.Add($"IMP:ACME|{number}");
        }
        await using var check = fx.NewDb(tenantId);
        Assert.Equal(keys, await check.Set<PurchaseOrder>().AsNoTracking().OrderBy(p => p.PurchaseOrderId).Select(p => p.ImportKey!).ToListAsync());
    }
}
