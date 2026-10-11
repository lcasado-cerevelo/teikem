using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P0: las constantes de Inventario y almacén coinciden con los literales de Diseño/logistica-db-seed.sql y
/// Diseño/logistica-db-estructura.sql; el catálogo de permisos pasa a 58 con inventory.* y warehouse.manage; los mapas de
/// permiso de dueño cubren las entidades WMS; y la estructura declara las guardas de última línea del lote (CHECKs del ledger,
/// índices únicos, FKs compuestas, WarehouseTaskId INT y la vista de genealogía ampliada).
/// </summary>
public class WmsCatalogTests
{
    private static readonly Lazy<string> Seed = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql")));
    private static readonly Lazy<string> Structure = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql")));

    private static bool SeedHas(string domain, string code) => Seed.Value.Contains($"('{domain}','{code}',", StringComparison.Ordinal);

    [Fact]
    public void Status_domains_and_codes_match_the_seed()
    {
        var map = new Dictionary<string, string[]>
        {
            [StatusDomains.WarehouseStatus] = new[] { WarehouseStatuses.Active, WarehouseStatuses.Inactive },
            [StatusDomains.DockStatus] = new[] { DockStatuses.Free, DockStatuses.Occupied, DockStatuses.Maintenance },
            [StatusDomains.SerialStatus] = new[] { SerialStatuses.Available, SerialStatuses.Reserved, SerialStatuses.Shipped, SerialStatuses.Scrapped },
            [StatusDomains.AsnStatus] = new[] { AsnStatuses.Expected, AsnStatuses.Received, AsnStatuses.Cancelled },
            [StatusDomains.ReceiptStatus] = new[]
            {
                ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.Received,
                ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Putaway,
            },
            [StatusDomains.WarehouseTaskStatus] = new[] { WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress, WarehouseTaskStatuses.Done, WarehouseTaskStatuses.Cancelled },
            [StatusDomains.CycleCountStatus] = new[] { CycleCountStatuses.Open, CycleCountStatuses.Counted, CycleCountStatuses.Reconciled, CycleCountStatuses.ReconciledVariance },
            [StatusDomains.PickBatchStatus] = new[] { PickBatchStatuses.Collected, PickBatchStatuses.Packed, PickBatchStatuses.Cancelled },
            [StatusDomains.PurchaseOrderStatus] = new[] { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received, PurchaseOrderStatuses.Cancelled },
            [StatusDomains.AppointmentStatus] = new[] { AppointmentStatuses.Scheduled, AppointmentStatuses.Arrived, AppointmentStatuses.Completed, AppointmentStatuses.NoShow, AppointmentStatuses.Cancelled },
            [StatusDomains.CrossDockStatus] = new[] { CrossDockStatuses.Open, CrossDockStatuses.Allocated, CrossDockStatuses.Completed },
            [StatusDomains.AllocationStatus] = new[] { AllocationStatuses.Planned, AllocationStatuses.Moved, AllocationStatuses.Cancelled },
        };
        foreach (var (domain, codes) in map)
        {
            Assert.Contains($"('{domain}',2,", Seed.Value);
            Assert.All(codes, c => Assert.True(SeedHas(domain, c), $"{domain} {c}"));
        }
        Assert.Equal("PickBatchStatus", StatusDomains.PickBatchStatus);
        // D16: RESERVED y SHIPPED laterales (en el INSERT y en el UPDATE idempotente); SCRAPPED terminal.
        Assert.Contains("('SerialStatus','RESERVED','Reservado','Reserved',@LAT,", Seed.Value);
        Assert.Contains("('SerialStatus','SHIPPED','Despachado','Shipped',@LAT,", Seed.Value);
        Assert.Contains("('SerialStatus','SCRAPPED','Dada de baja','Scrapped',@TERM,", Seed.Value);
        Assert.Contains("UPDATE dbo.StatusCode SET StageKindLookupId = @LAT\nWHERE Entity = 'SerialStatus' AND InternalCode IN ('RESERVED','SHIPPED')", Seed.Value.Replace("\r\n", "\n"));
        // D20: CANCELLED terminal en tareas, citas y asignaciones; PickBatchStatus con COLLECTED inicial.
        Assert.Contains("('WarehouseTaskStatus','CANCELLED','Cancelada','Cancelled',@TERM,", Seed.Value);
        Assert.Contains("('AppointmentStatus','CANCELLED','Cancelada','Cancelled',@TERM,", Seed.Value);
        Assert.Contains("('AllocationStatus','CANCELLED','Cancelada','Cancelled',@TERM,", Seed.Value);
        Assert.Contains("('PickBatchStatus','COLLECTED','Recolectada','Collected',@PIPE,1,'#F59E0B',1)", Seed.Value);
    }

    [Fact]
    public void Lote13_receipt_statuses_lateral_rules_and_retirement_of_open_are_seeded()
    {
        var seed = Seed.Value.Replace("\r\n", "\n");
        // Seis estatus con etiqueta, tipo de etapa, orden y color del plan; EXPECTED es la única etapa inicial.
        Assert.Contains("('ReceiptStatus','EXPECTED','Esperado','Expected',@PIPE,1,'#9CA3AF',1)", seed);
        Assert.Contains("('ReceiptStatus','RECEIVING','Recibiendo','Receiving',@PIPE,2,'#3B82F6',0)", seed);
        Assert.Contains("('ReceiptStatus','DISCREPANCY','Discrepancia','Discrepancy',@LAT,3,'#EF4444',0)", seed);
        Assert.Contains("('ReceiptStatus','RECEIVED','Completado','Completed',@PIPE,4,'#10B981',0)", seed);
        Assert.Contains("('ReceiptStatus','RECEIVED_VARIANCE','Completado con diferencia','Completed with variance',@LAT,5,'#F59E0B',0)", seed);
        Assert.Contains("('ReceiptStatus','PUTAWAY','Acomodado','Put away',@TERM,6,'#059669',0)", seed);
        Assert.False(SeedHas(StatusDomains.ReceiptStatus, "OPEN"));
        // Bases ya sembradas: etiquetas nuevas de RECEIVED/PUTAWAY, recibos OPEN migrados con historial y OPEN retirado.
        Assert.Contains("WHERE Entity = 'ReceiptStatus' AND InternalCode IN ('RECEIVED','PUTAWAY');", seed);
        Assert.Contains("N'Lote 13: nuevo ciclo de estatus del recibo.'", seed);
        Assert.Contains("INSERT INTO dbo.EntityStatusHistory (TenantId, EntityTypeLookupId, EntityId, FromStatusCodeId, ToStatusCodeId, Comment, ChangedAtUtc, ChangedBy)", seed);
        Assert.Contains("UPDATE dbo.StatusCode SET IsActive = 0, IsInitial = 0 WHERE StatusCodeId = @RcOpen;", seed);

        var lateral = Block(seed, "3G) STATUS LATERAL ENTRY");
        Assert.Contains("lat.InternalCode='DISCREPANCY'", lateral);
        Assert.Contains("frm.InternalCode='RECEIVING'", lateral);
        Assert.Contains("lat.InternalCode='RECEIVED_VARIANCE'", lateral);
        Assert.Contains("frm.InternalCode IN ('RECEIVING','DISCREPANCY','EXPECTED')", lateral);
        Assert.Contains("lat.InternalCode='PUTAWAY'", lateral);
        Assert.Contains("frm.InternalCode='RECEIVED_VARIANCE'", lateral);
        // El fixture InMemory siembra las mismas reglas.
        Assert.Equal(5, ReceiptStatusSeed.Entries.Length);
    }

    [Fact]
    public void Lote14_cycle_count_statuses_origins_lateral_rules_and_historic_variance_are_seeded()
    {
        var seed = Seed.Value.Replace("\r\n", "\n");
        // D7: Pendiente → Contado → Concordancia | Diferencia (terminales), con las etiquetas en el INSERT y en el UPDATE idempotente.
        Assert.Contains("('CycleCountStatus','OPEN','Pendiente','Pending',@PIPE,1,'#9CA3AF',1)", seed);
        Assert.Contains("('CycleCountStatus','COUNTED','Contado','Counted',@PIPE,2,'#F59E0B',0)", seed);
        Assert.Contains("('CycleCountStatus','RECONCILED','Concordancia','Matched',@TERM,3,'#059669',0)", seed);
        Assert.Contains("('CycleCountStatus','RECONCILED_VARIANCE','Diferencia','Variance',@TERM,4,'#F59E0B',0)", seed);
        Assert.Contains("WHERE Entity = 'CycleCountStatus' AND InternalCode IN ('OPEN','RECONCILED')", seed);
        // Históricos con ajuste → Diferencia con historial; conteos anteriores → origen MANUAL; indicador del Pulso corregido.
        Assert.Contains("N'Lote 14: conteo con diferencia.'", seed);
        Assert.Contains("l.AdjustmentTxnId IS NOT NULL", seed);
        Assert.Contains("UPDATE dbo.CycleCount SET OriginLookupId = @CcManual WHERE OriginLookupId IS NULL;", seed);
        Assert.Contains("SET FilterJson = N'" + Teikem.Infrastructure.Seeding.SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilter + "'", seed);
        Assert.Contains("AND FilterJson = N'" + Teikem.Infrastructure.Seeding.SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilterV1 + "'", seed);
        // D2: origen del conteo.
        Assert.Contains("('CycleCountOrigin',1,", seed);
        Assert.True(SeedHas(LookupDomains.CycleCountOrigin, CycleCountOrigins.Manual));
        Assert.True(SeedHas(LookupDomains.CycleCountOrigin, CycleCountOrigins.Changes));
        // D7/D8: 'Diferencia' desde Pendiente (confirmar en un paso) y desde Contado (a ciegas).
        var lateral = Block(seed, "3G) STATUS LATERAL ENTRY");
        Assert.Contains("lat.Entity='CycleCountStatus'    AND lat.InternalCode='RECONCILED_VARIANCE'", lateral);
        Assert.Contains("frm.Entity='CycleCountStatus'    AND frm.InternalCode IN ('OPEN','COUNTED')", lateral);
    }

    [Fact]
    public void Lote16_receiving_mode_catalog_backfill_and_guarded_schema()
    {
        var seed = Seed.Value.Replace("\r\n", "\n");
        var sql = Structure.Value.Replace("\r\n", "\n");
        Assert.Equal("ReceivingMode", LookupDomains.ReceivingMode);
        Assert.Contains("('ReceivingMode',1,'Modo de recepción','Receiving mode')", seed);
        Assert.Contains("('ReceivingMode','PUTAWAY','Con acomodo','With put-away',1)", seed);
        Assert.Contains("('ReceivingMode','DIRECT','Directo a posición','Direct to bin',2)", seed);
        Assert.All(ReceivingModes.All, m => Assert.True(SeedHas(LookupDomains.ReceivingMode, m), m));
        // Relleno idempotente: almacenes y recibos sin modo quedan con acomodo (solo los NULL).
        Assert.Contains("UPDATE dbo.Warehouse SET ReceivingModeLookupId = @L16Putaway WHERE ReceivingModeLookupId IS NULL;", seed);
        Assert.Contains("UPDATE dbo.ReceiptHeader SET ReceivingModeLookupId = @L16Putaway WHERE ReceivingModeLookupId IS NULL;", seed);
        // Estructura con guardas: Warehouse y ReceiptLine pasan a IF OBJECT_ID … ELSE COL_LENGTH; FKs en lotes propios.
        Assert.Contains("IF OBJECT_ID('dbo.Warehouse') IS NULL", sql);
        Assert.Contains("IF COL_LENGTH('dbo.Warehouse', 'ReceivingModeLookupId') IS NULL", sql);
        Assert.Contains("IF COL_LENGTH('dbo.Warehouse', 'DefaultReceivingBinId') IS NULL", sql);
        Assert.Contains("IF COL_LENGTH('dbo.ReceiptHeader', 'ReceivingModeLookupId') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID('dbo.ReceiptLine') IS NULL", sql);
        Assert.Contains("IF COL_LENGTH('dbo.ReceiptLine', 'TargetBinId') IS NULL", sql);
        foreach (var fk in new[] { "FK_Warehouse_ReceivingMode", "FK_Warehouse_DefaultReceivingBin", "FK_Receipt_ReceivingMode", "FK_ReceiptLine_TargetBin", "FK_UserDevice_DefaultWarehouse" })
            Assert.Contains($"IF OBJECT_ID('dbo.{fk}', 'F') IS NULL", sql);
        Assert.Contains("FOREIGN KEY (DefaultReceivingBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId)", sql);
        // La FK compuesta de la posición por defecto va DESPUÉS de crear WarehouseBin (orden por capas).
        Assert.True(sql.IndexOf("CREATE TABLE dbo.WarehouseBin (", StringComparison.Ordinal)
                    < sql.IndexOf("ADD CONSTRAINT FK_Warehouse_DefaultReceivingBin", StringComparison.Ordinal));
    }

    [Fact]
    public void Lookup_domains_and_codes_match_the_seed()
    {
        Assert.True(SeedHas(LookupDomains.ZoneType, ZoneTypes.Staging));
        foreach (var z in new[] { ZoneTypes.Picking, ZoneTypes.Reserve, ZoneTypes.Refrigerated, ZoneTypes.Quarantine, ZoneTypes.CrossDock })
            Assert.True(SeedHas(LookupDomains.ZoneType, z), z);
        var reasons = new[]
        {
            AdjustmentReasons.ReceiptVariance, AdjustmentReasons.CountVariance, AdjustmentReasons.Damage, AdjustmentReasons.Loss, AdjustmentReasons.Found,
            AdjustmentReasons.Expired, AdjustmentReasons.PoShortage, AdjustmentReasons.PickBatchReversal, AdjustmentReasons.Other,
        };
        Assert.Equal(9, reasons.Distinct().Count());
        Assert.All(reasons, r => Assert.True(SeedHas(LookupDomains.AdjustmentReason, r), r));
        Assert.Equal(new[] { "RECEIPT_VARIANCE", "COUNT_VARIANCE", "PICK_BATCH_REVERSAL", "OPENING_BALANCE", "TRACKING_CONVERSION" }, AdjustmentReasons.SystemAssigned);   // Lote 10: saldo inicial; Lote 26: conversión a serie
        Assert.True(SeedHas(LookupDomains.AdjustmentReason, AdjustmentReasons.TrackingConversion));
        var actions = new[] { ShortageActions.Close, ShortageActions.Reorder, ShortageActions.ManualAdjustment };
        Assert.All(actions, a => Assert.True(SeedHas(LookupDomains.ShortageAction, a), a));
        Assert.Contains("('AdjustmentReason',1,", Seed.Value);
        Assert.Contains("('ShortageAction',1,", Seed.Value);

        foreach (var (domain, codes) in new (string, string[])[]
                 {
                     (LookupDomains.TrackingType, new[] { TrackingTypes.None, TrackingTypes.Lot, TrackingTypes.Serial }),
                     (LookupDomains.InventoryTxnType, new[] { InventoryTxnTypes.Receipt, InventoryTxnTypes.Issue, InventoryTxnTypes.Transfer, InventoryTxnTypes.Adjustment, InventoryTxnTypes.CrossDock }),
                     (LookupDomains.ReceiptType, new[] { ReceiptTypes.Asn, ReceiptTypes.Blind, ReceiptTypes.Return }),
                     (LookupDomains.WarehouseTaskType, new[] { WarehouseTaskTypes.Putaway, WarehouseTaskTypes.Pick, WarehouseTaskTypes.Pack, WarehouseTaskTypes.Replenish, WarehouseTaskTypes.Count, WarehouseTaskTypes.Load, WarehouseTaskTypes.CrossDock }),
                     (LookupDomains.DockType, new[] { DockTypes.Inbound, DockTypes.Outbound, DockTypes.Both }),
                     (LookupDomains.DockDirection, new[] { DockDirections.Inbound, DockDirections.Outbound }),
                 })
            Assert.All(codes, c => Assert.True(SeedHas(domain, c), $"{domain} {c}"));
    }

    [Fact]
    public void Entity_types_and_capability_match_the_seed()
    {
        var added = new[]
        {
            EntityTypes.Receipt, EntityTypes.Asn, EntityTypes.WarehouseDock, EntityTypes.InventorySerial, EntityTypes.WarehouseTask,
            EntityTypes.PickBatch, EntityTypes.DockAppointment, EntityTypes.CrossDockAllocation, EntityTypes.InventoryTransaction, EntityTypes.StockBalance,
        };
        Assert.Equal(10, added.Distinct().Count());
        Assert.All(added, e => Assert.True(SeedHas(LookupDomains.EntityType, e), e));
        foreach (var e in new[] { EntityTypes.CycleCount, EntityTypes.CrossDockPlan, EntityTypes.PurchaseOrder, EntityTypes.Supplier, EntityTypes.ReceiptLine, EntityTypes.Warehouse, EntityTypes.Product,
                     EntityTypes.ProductCategory })   // Lote 7A: la categoría se audita con su propio EntityType
            Assert.True(SeedHas(LookupDomains.EntityType, e), e);
        Assert.Equal("EDIT_PURCHASE_ORDER", Capabilities.EditPurchaseOrder);
        Assert.True(SeedHas(LookupDomains.Capability, Capabilities.EditPurchaseOrder));
    }

    [Fact]
    public void Permissions_are_69_with_the_four_warehouse_ones_and_templates()
    {
        Assert.Equal(72, PermissionCatalog.All.Count);   // 2026-10-08: + warehouse.damage; Lote 14: + pulse.attention; Lote 27: + rental.extend, rental.return; 2026-10-11: + warehouse.issue
        foreach (var (code, es) in new[] { ("inventory.view", "Ver inventario y almacén"), ("inventory.manage", "Gestionar productos"),
                     ("inventory.adjust", "Ajustar y transferir inventario"), ("warehouse.manage", "Gestionar almacenes y tareas") })
        {
            var p = Assert.Single(PermissionCatalog.All, x => x.Code == code);
            Assert.Equal("WAREHOUSE", p.Category);
            Assert.Equal(es, p.LabelEs);
            Assert.Contains($"('{code}','WAREHOUSE','{es}',", Seed.Value);
        }
        var t = PermissionCatalog.RoleTemplates;
        foreach (var role in new[] { "WarehouseOperator", "ReadOnly", "Billing" })
        {
            Assert.Contains(PermissionCatalog.InventoryView, t[role]);
            Assert.Contains($"('{role}','inventory.view')", Seed.Value);
        }
        // Administrar/ajustar inventario y administrar almacenes: el Admin y (2026-10-01, Luis) el Operador de almacén; nadie más.
        foreach (var code in new[] { PermissionCatalog.InventoryManage, PermissionCatalog.InventoryAdjust, PermissionCatalog.WarehouseManage })
        {
            Assert.Contains(code, t["TenantAdmin"]);
            Assert.Contains(code, t["WarehouseOperator"]);
            Assert.Contains($"('WarehouseOperator','{code}')", Seed.Value);
            Assert.All(t.Where(kv => kv.Key is not ("TenantAdmin" or "WarehouseOperator")), kv => Assert.DoesNotContain(code, kv.Value));
        }
        Assert.DoesNotContain(PermissionCatalog.CodReconcile, t["WarehouseOperator"]);
        Assert.DoesNotContain("('WarehouseOperator','cod.reconcile')", Seed.Value);
        // El Operador recolecta y reconcilia conteos (D22) pero no empaca (sin orders.create, D27).
        Assert.Contains(PermissionCatalog.WarehouseCount, t["WarehouseOperator"]);
        Assert.DoesNotContain(PermissionCatalog.OrdersCreate, t["WarehouseOperator"]);
        // Lote 8A: capturar a ciegas es un permiso aparte (el Operador lo tiene; Solo lectura no) y warehouse.count lo implica.
        var capture = Assert.Single(PermissionCatalog.All, x => x.Code == "warehouse.count.capture");
        Assert.Equal(("WAREHOUSE", "Capturar conteo (a ciegas)"), (capture.Category, capture.LabelEs));
        Assert.Contains("('warehouse.count.capture','WAREHOUSE','Capturar conteo (a ciegas)',", Seed.Value);
        Assert.Contains(PermissionCatalog.WarehouseCountCapture, t["WarehouseOperator"]);
        Assert.Contains("('WarehouseOperator','warehouse.count.capture')", Seed.Value);
        Assert.DoesNotContain(PermissionCatalog.WarehouseCountCapture, t["ReadOnly"]);
        var set = new HashSet<string>(new[] { PermissionCatalog.WarehouseCount }, StringComparer.OrdinalIgnoreCase);
        PermissionCatalog.ExpandImplied(set);
        Assert.Contains(PermissionCatalog.WarehouseCountCapture, set);
        var onlyCapture = new HashSet<string>(new[] { PermissionCatalog.WarehouseCountCapture }, StringComparer.OrdinalIgnoreCase);
        PermissionCatalog.ExpandImplied(onlyCapture);
        Assert.DoesNotContain(PermissionCatalog.WarehouseCount, onlyCapture);
        Assert.Contains("permisos (69)", Seed.Value);
    }

    [Fact]
    public void Owner_permission_maps_cover_the_wms_entities()
    {
        var read = PermissionCatalog.OwnerReadPermission;
        foreach (var e in new[]
                 {
                     EntityTypes.Warehouse, EntityTypes.WarehouseDock, EntityTypes.Product, EntityTypes.InventorySerial, EntityTypes.Receipt, EntityTypes.Asn,
                     EntityTypes.WarehouseTask, EntityTypes.CycleCount, EntityTypes.PickBatch, EntityTypes.DockAppointment, EntityTypes.CrossDockPlan,
                     EntityTypes.CrossDockAllocation, EntityTypes.StockBalance, EntityTypes.InventoryTransaction, EntityTypes.ReceiptLine,
                 })
            Assert.Equal(PermissionCatalog.InventoryView, read[e]);
        Assert.Equal(PermissionCatalog.PurchasingView, read[EntityTypes.PurchaseOrder]);
        Assert.Equal(PermissionCatalog.PurchasingView, read[EntityTypes.Supplier]);

        var write = PermissionCatalog.OwnerWritePermission;
        Assert.Equal(PermissionCatalog.WarehouseManage, write[EntityTypes.Warehouse]);
        Assert.Equal(PermissionCatalog.WarehouseManage, write[EntityTypes.WarehouseDock]);
        Assert.Equal(PermissionCatalog.InventoryManage, write[EntityTypes.Product]);
        Assert.Equal(PermissionCatalog.WarehouseReceive, write[EntityTypes.Receipt]);
        Assert.Equal(PermissionCatalog.WarehouseReceive, write[EntityTypes.Asn]);
        Assert.Equal(PermissionCatalog.WarehouseCount, write[EntityTypes.CycleCount]);
        Assert.Equal(PermissionCatalog.WarehousePick, write[EntityTypes.PickBatch]);
        Assert.Equal(PermissionCatalog.WarehouseCrossdock, write[EntityTypes.DockAppointment]);
        Assert.Equal(PermissionCatalog.WarehouseCrossdock, write[EntityTypes.CrossDockPlan]);
        Assert.Equal(PermissionCatalog.PurchasingManage, write[EntityTypes.PurchaseOrder]);
        Assert.Equal(PermissionCatalog.PurchasingManage, write[EntityTypes.Supplier]);
        // Lote 8A, decisión 12: USER_DEVICE (resolver cerrado) exige devices.manage para leer y escribir en las rutas
        // polimórficas; sin la entrada quedaría abierta a cualquier autenticado (404 en vez de 403).
        Assert.Equal(PermissionCatalog.DevicesManage, read[EntityTypes.UserDevice]);
        Assert.Equal(PermissionCatalog.DevicesManage, write[EntityTypes.UserDevice]);
        foreach (var closed in new[]
                 {
                     EntityTypes.InventorySerial, EntityTypes.WarehouseTask, EntityTypes.CrossDockAllocation,
                     EntityTypes.StockBalance, EntityTypes.InventoryTransaction, EntityTypes.ReceiptLine,
                 })
            Assert.False(write.ContainsKey(closed), closed);
    }

    [Fact]
    public void Number_kinds_are_known_and_allowed_by_the_check()
    {
        foreach (var kind in new[] { NumberKinds.Receipt, NumberKinds.CycleCount, NumberKinds.CrossDock, NumberKinds.Purchase })
        {
            Assert.True(NumberingRules.IsKnownKind(kind), kind);
            Assert.Matches(new Regex($@"CK_NumberSequence_Kind CHECK \(Kind IN \([^)]*'{kind}'[^)]*\)\)"), Structure.Value);
        }
        Assert.Equal(new[] { "RECEIPT", "CYCLECOUNT", "CROSSDOCK", "PURCHASE" }, new[] { NumberKinds.Receipt, NumberKinds.CycleCount, NumberKinds.CrossDock, NumberKinds.Purchase });
        Assert.Equal("REC-00007", WmsNumbering.Format(NumberKinds.Receipt, 7));
        Assert.Equal("CC-00001", WmsNumbering.Format(NumberKinds.CycleCount, 1));
        Assert.Equal("XD-00012", WmsNumbering.Format(NumberKinds.CrossDock, 12));
        Assert.Equal("PO-00003", WmsNumbering.Format(NumberKinds.Purchase, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => WmsNumbering.PatternFor(NumberKinds.PackBatch));
    }

    [Fact]
    public void Seed_lateral_entries_have_no_purchase_order_rule_and_capability_block()
    {
        var seed = Seed.Value;
        var lateral = Block(seed, "3G) STATUS LATERAL ENTRY");
        Assert.Contains("et.InternalCode='PICK_BATCH'", lateral);
        Assert.Contains("frm.InternalCode IN ('COLLECTED','PACKED')", lateral);
        Assert.Contains("et.InternalCode='WAREHOUSE_TASK'", lateral);
        Assert.Contains("frm.InternalCode IN ('PENDING','IN_PROGRESS')", lateral);
        Assert.Contains("lat.InternalCode IN ('NO_SHOW','CANCELLED')", lateral);
        Assert.Contains("et.InternalCode='CROSSDOCK_ALLOCATION'", lateral);
        Assert.Contains("et.InternalCode='ASN'", lateral);
        // D47: PURCHASE_ORDER sin regla (CANCELLED desde DRAFT, SENT y PARTIAL).
        Assert.DoesNotContain("et.InternalCode='PURCHASE_ORDER'", lateral);

        var capability = Block(seed, "3H) STATUS CAPABILITY");
        Assert.Contains("et.InternalCode='PURCHASE_ORDER'", capability);
        Assert.Contains("s.InternalCode IN ('SENT','PARTIAL','RECEIVED','CANCELLED')", capability);
        Assert.Contains("c.InternalCode='EDIT_PURCHASE_ORDER'", capability);
        Assert.Contains("VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0)", capability);
    }

    [Fact]
    public void Structure_declares_the_lote6_guards()
    {
        var sql = Structure.Value;
        Assert.Contains("CONSTRAINT CK_StockBalance_Qty CHECK (QtyOnHand >= 0 AND QtyReserved >= 0 AND QtyReserved <= QtyOnHand)", sql);
        Assert.Contains("CONSTRAINT CK_InvTxn_Quantity CHECK (Quantity <> 0)", sql);
        Assert.Contains("(Quantity > 0 AND ToWarehouseId IS NOT NULL) OR (Quantity < 0 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NULL)", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_Receipt_Asn ON dbo.ReceiptHeader(AsnId) WHERE AsnId IS NOT NULL AND IsActive = 1", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_PickBatch_Order ON dbo.PickBatch(TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1", sql);
        // Faltantes de compra ('Ajustes de inventario'): líneas y ASN de una orden por índice.
        Assert.Contains("CREATE INDEX IX_POLine_Po ON dbo.PurchaseOrderLine(PurchaseOrderId);", sql);
        Assert.Contains("CREATE INDEX IX_Asn_Po ON dbo.Asn(PurchaseOrderId) WHERE PurchaseOrderId IS NOT NULL;", sql);
        Assert.Contains("CONSTRAINT UQ_WarehouseBin_WhCode UNIQUE (WarehouseId, Code)", sql);
        Assert.Contains("CONSTRAINT FK_StockBalance_Bin FOREIGN KEY (WarehouseBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId)", sql);
        Assert.Contains("WarehouseTaskId INT IDENTITY(1,1) PRIMARY KEY", sql);
        Assert.DoesNotContain("WarehouseTaskId BIGINT", sql);
        Assert.Contains("ReconciledSystemQty DECIMAL(16,3) NULL", sql);
        Assert.Contains("SystemQtyChanged BIT NOT NULL DEFAULT 0", sql);
        Assert.Contains("ConfirmedQty DECIMAL(16,3) NULL", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_Product_Barcode ON dbo.Product(TenantId, Barcode) WHERE Barcode IS NOT NULL AND IsActive = 1", sql);
        Assert.Contains("CONSTRAINT UQ_Client_IdTenant UNIQUE (ClientId, TenantId)", sql);
        foreach (var table in new[] { "CREATE TABLE dbo.PurchaseOrderShortageResolution (", "CREATE TABLE dbo.PickBatch (", "CREATE TABLE dbo.PickBatchLine (" })
            Assert.Contains(table, sql);

        var view = sql[sql.IndexOf("CREATE VIEW dbo.vw_LotGenealogy", StringComparison.Ordinal)..];
        foreach (var col in new[] { "t.InventoryTransactionId", "t.FromBinId", "t.ToBinId", "NetQuantity = CASE WHEN txn.InternalCode = 'TRANSFER' THEN 0 ELSE t.Quantity END", "reason.InternalCode AS Reason", "t.CreatedBy" })
            Assert.Contains(col, view);

        // Orden de capas: destinos (Id, TenantId) antes de sus FKs compuestas.
        int At(string s) => sql.IndexOf(s, StringComparison.Ordinal);
        Assert.True(At("UQ_Client_IdTenant") < At("FK_Product_Client"));
        Assert.True(At("UQ_Warehouse_IdTenant") < At("FK_StockBalance_Warehouse"));
        Assert.True(At("UQ_WarehouseBin_IdWh") < At("FK_Product_PrefBin"));
        Assert.True(At("UQ_Lot_IdProduct") < At("FK_Serial_Lot"));
        Assert.True(At("UQ_Serial_IdProduct") < At("FK_InvTxn_Serial"));
        Assert.True(At("UQ_PurchaseOrder_IdTenant") < At("FK_Asn_Po"));
        Assert.True(At("UQ_Asn_IdTenant") < At("FK_Receipt_Asn"));
        Assert.True(At("CREATE TABLE dbo.WarehouseTask (") < At("CREATE TABLE dbo.CrossDockAllocation ("));
    }

    [Fact]
    public void Lote14_discrepancy_catalogs_statuses_and_table_are_declared()
    {
        var seed = Seed.Value.Replace("\r\n", "\n");
        foreach (var (domain, scope) in new[] { (StatusDomains.InventoryDiscrepancyStatus, 2), (LookupDomains.InventoryDiscrepancyKind, 1), (LookupDomains.ReconciliationTrigger, 1) })
            Assert.Contains($"('{domain}',{scope},", seed);
        Assert.Contains("('EntityType','INVENTORY_DISCREPANCY','Descuadre de inventario','Inventory discrepancy',82)", seed);
        Assert.Equal("INVENTORY_DISCREPANCY", EntityTypes.InventoryDiscrepancy);
        foreach (var k in new[] { DiscrepancyKinds.Balance, DiscrepancyKinds.ProductTotal })
            Assert.True(SeedHas(LookupDomains.InventoryDiscrepancyKind, k), k);
        foreach (var t in new[] { ReconciliationTriggers.Event, ReconciliationTriggers.Manual, ReconciliationTriggers.Scheduled, ReconciliationTriggers.Migration })
            Assert.True(SeedHas(LookupDomains.ReconciliationTrigger, t), t);
        // D5: Pendiente inicial; los tres cierres terminales.
        Assert.Contains("('InventoryDiscrepancyStatus','OPEN','Pendiente','Open',@PIPE,1,'#EF4444',1)", seed);
        Assert.Contains("('InventoryDiscrepancyStatus','RESOLVED','Resuelto','Resolved',@TERM,2,'#059669',0)", seed);
        Assert.Contains("('InventoryDiscrepancyStatus','DISMISSED','Descartado','Dismissed',@TERM,3,'#6B7280',0)", seed);
        Assert.Contains("('InventoryDiscrepancyStatus','SELF_CORRECTED','Se corrigió solo','Self-corrected',@TERM,4,'#0EA5E9',0)", seed);
        Assert.Equal(PermissionCatalog.InventoryView, PermissionCatalog.OwnerReadPermission[EntityTypes.InventoryDiscrepancy]);
        Assert.False(PermissionCatalog.OwnerWritePermission.ContainsKey(EntityTypes.InventoryDiscrepancy));

        var sql = Structure.Value;
        Assert.Contains("IF OBJECT_ID('dbo.InventoryDiscrepancy') IS NULL", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_InvDiscrepancy_OpenKey ON dbo.InventoryDiscrepancy(TenantId, KindLookupId, ProductId, WarehouseId, WarehouseBinId, LotId)", sql);
        Assert.Contains("CONSTRAINT CK_InvDiscrepancy_Closed CHECK (ResolvedBy IS NULL OR ClosedAtUtc IS NOT NULL)", sql);
        Assert.Contains("CONSTRAINT FK_InvDiscrepancy_CycleCount FOREIGN KEY (CycleCountId, TenantId) REFERENCES dbo.CycleCount(CycleCountId, TenantId)", sql);
        // Capas: la tabla va después de sus destinos (conteo, recolección y ledger) y antes del cruce de muelle.
        int At(string s) => sql.IndexOf(s, StringComparison.Ordinal);
        Assert.True(At("CREATE TABLE dbo.PickBatchLine (") < At("CREATE TABLE dbo.InventoryDiscrepancy ("));
        Assert.True(At("CONSTRAINT UQ_CycleCount_IdTenant") < At("FK_InvDiscrepancy_CycleCount"));
        Assert.True(At("CREATE TABLE dbo.InventoryDiscrepancy (") < At("CAPA 15"));
    }

    private static string Block(string seed, string title)
    {
        var start = seed.IndexOf(title, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No está el bloque '{title}' en el seed.");
        var end = seed.IndexOf("/* ----", start + title.Length, StringComparison.Ordinal);
        return end < 0 ? seed[start..] : seed[start..end];
    }
}
