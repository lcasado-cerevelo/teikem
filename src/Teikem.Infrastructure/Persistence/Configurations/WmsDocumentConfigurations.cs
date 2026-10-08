using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Wms;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 6 — Documentos WMS: Compras (CAPA 13B), recepción, tareas, conteo y recolección (CAPA 14) y cruce de muelle (CAPA 15).
// Mapeo 1:1 con Diseño/logistica-db-estructura.sql. LineTotal y VarianceQty son columnas computadas PERSISTED: se mapean
// como computadas y NUNCA se usan en lógica. UQ_CycleCountLine se mapea con HasFilter(null). Las FKs compuestas del SQL no se
// mapean. PickWave, PickTask, Carton y CartonLine no se mapean (D1).

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("Supplier");
        b.HasKey(s => s.SupplierId);
        b.Property(s => s.Name).HasMaxLength(200).IsRequired();
        b.Property(s => s.ContactName).HasMaxLength(150);
        b.Property(s => s.Phone).HasMaxLength(40);
        b.Property(s => s.Email).HasMaxLength(150);
        b.Property(s => s.RowVersion).IsRowVersion();

        b.HasIndex(s => new { s.TenantId, s.Name }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_Supplier_Name");
    }
}

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrder");
        b.HasKey(p => p.PurchaseOrderId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Number).HasMaxLength(40).IsRequired();
        b.Property(p => p.OrderDate).HasColumnType("date");
        b.Property(p => p.ExpectedDate).HasColumnType("date");
        b.Property(p => p.RowVersion).IsRowVersion();

        b.HasIndex(p => new { p.TenantId, p.Number }).IsUnique().HasDatabaseName("UQ_PurchaseOrder_Number");
        b.HasIndex(p => new { p.TenantId, p.StatusCodeId }).HasDatabaseName("IX_PurchaseOrder_Status");

        b.HasOne(p => p.Status).WithMany().HasForeignKey(p => p.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("PurchaseOrderLine");
        b.HasKey(l => l.PurchaseOrderLineId);
        b.Property(l => l.QtyOrdered).HasColumnType("decimal(16,3)");
        b.Property(l => l.QtyReceived).HasColumnType("decimal(16,3)");
        b.Property(l => l.UnitCost).HasColumnType("decimal(18,4)");
        b.Property(l => l.LineTotal).HasColumnType("decimal(35,7)").HasComputedColumnSql("[QtyOrdered]*[UnitCost]", stored: true);

        b.HasIndex(l => l.PurchaseOrderId).HasDatabaseName("IX_POLine_Po");

        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PurchaseOrderShortageResolutionConfiguration : IEntityTypeConfiguration<PurchaseOrderShortageResolution>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderShortageResolution> b)
    {
        b.ToTable("PurchaseOrderShortageResolution");
        b.HasKey(r => r.PurchaseOrderShortageResolutionId);
        b.Property(r => r.Quantity).HasColumnType("decimal(16,3)");
        b.Property(r => r.Notes).HasMaxLength(300);

        b.HasIndex(r => r.PurchaseOrderLineId).HasDatabaseName("IX_PoShortage_Line");

        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(r => r.PurchaseOrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(r => r.PurchaseOrderLineId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AsnConfiguration : IEntityTypeConfiguration<Asn>
{
    public void Configure(EntityTypeBuilder<Asn> b)
    {
        b.ToTable("Asn");
        b.HasKey(a => a.AsnId);
        b.Property(a => a.Reference).HasMaxLength(80);
        b.Property(a => a.ExpectedDate).HasColumnType("date");

        b.HasIndex(a => a.PurchaseOrderId).HasFilter("[PurchaseOrderId] IS NOT NULL").HasDatabaseName("IX_Asn_Po");

        b.HasOne<Warehouse>().WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(a => a.PurchaseOrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(a => a.Lines).WithOne().HasForeignKey(l => l.AsnId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AsnLineConfiguration : IEntityTypeConfiguration<AsnLine>
{
    public void Configure(EntityTypeBuilder<AsnLine> b)
    {
        b.ToTable("AsnLine");
        b.HasKey(l => l.AsnLineId);
        b.Property(l => l.ExpectedQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.LotNumber).HasMaxLength(60);

        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(l => l.PurchaseOrderLineId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ReceiptHeaderConfiguration : IEntityTypeConfiguration<ReceiptHeader>
{
    public void Configure(EntityTypeBuilder<ReceiptHeader> b)
    {
        b.ToTable("ReceiptHeader");
        b.HasKey(r => r.ReceiptHeaderId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Number).HasMaxLength(40).IsRequired();
        b.Property(r => r.Carrier).HasMaxLength(80);     // Lote 13
        b.Property(r => r.Reference).HasMaxLength(80);   // Lote 13
        b.Property(r => r.RowVersion).IsRowVersion();

        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UQ_Receipt_Number");
        // Un ASN tiene un solo recibo activo (D6): última línea contra dos recibos simultáneos.
        b.HasIndex(r => r.AsnId).IsUnique().HasFilter("[AsnId] IS NOT NULL AND [IsActive] = 1").HasDatabaseName("UX_Receipt_Asn");
        b.HasIndex(r => new { r.TenantId, r.StatusCodeId }).HasDatabaseName("IX_Receipt_Tenant_Status");

        b.HasOne(r => r.Status).WithMany().HasForeignKey(r => r.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Asn>().WithMany().HasForeignKey(r => r.AsnId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseDock>().WithMany().HasForeignKey(r => r.DockId).OnDelete(DeleteBehavior.NoAction);
        // Lote 13: FK_Receipt_StagingBin es compuesta en SQL (DefaultStagingBinId, WarehouseId) → UQ_WarehouseBin_IdWh; en EF
        // basta la simple (como FK_Receipt_Dock): el servicio valida que la posición sea del almacén del recibo.
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(r => r.DefaultStagingBinId).OnDelete(DeleteBehavior.NoAction);
        // Lote 16: copia del modo de recepción (FK_Receipt_ReceivingMode → LookupCode, en SQL; columna simple en EF).
        b.Property(r => r.ReceivingModeLookupId);
        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.ReceiptHeaderId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> b)
    {
        b.ToTable("ReceiptLine");
        b.HasKey(l => l.ReceiptLineId);
        b.Property(l => l.ReceivedQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.ExpectedQty).HasColumnType("decimal(16,3)");

        b.HasIndex(l => l.ReceiptHeaderId).HasDatabaseName("IX_ReceiptLine_Header");

        b.HasOne<AsnLine>().WithMany().HasForeignKey(l => l.AsnLineId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventorySerial>().WithMany().HasForeignKey(l => l.SerialId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.StagingBinId).OnDelete(DeleteBehavior.NoAction);
        // Lote 16: posición destino (FK_ReceiptLine_TargetBin); el servicio garantiza que sea del almacén del recibo.
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.TargetBinId).OnDelete(DeleteBehavior.NoAction);
        // 2026-10-08: daño declarado en la línea (FK_ReceiptLine_DamageBin / DamageReport).
        b.Property(l => l.DamagedQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.DamageNote).HasMaxLength(300);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.DamageBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<DamageReport>().WithMany().HasForeignKey(l => l.DamageReportId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class WarehouseTaskConfiguration : IEntityTypeConfiguration<WarehouseTask>
{
    public void Configure(EntityTypeBuilder<WarehouseTask> b)
    {
        b.ToTable("WarehouseTask");
        b.HasKey(t => t.WarehouseTaskId);
        b.Property(t => t.Quantity).HasColumnType("decimal(16,3)");

        b.HasIndex(t => new { t.WarehouseId, t.TaskTypeLookupId, t.StatusCodeId, t.Priority }).HasDatabaseName("IX_WarehouseTask_Queue");
        b.HasIndex(t => new { t.RefEntityLookupId, t.RefId }).HasDatabaseName("IX_WarehouseTask_Ref");

        b.HasOne(t => t.Status).WithMany().HasForeignKey(t => t.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(t => t.WarehouseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CycleCountConfiguration : IEntityTypeConfiguration<CycleCount>
{
    public void Configure(EntityTypeBuilder<CycleCount> b)
    {
        b.ToTable("CycleCount");
        b.HasKey(c => c.CycleCountId);
        b.Property(c => c.Number).HasMaxLength(40).IsRequired();
        b.Property(c => c.RowVersion).IsRowVersion();

        b.HasIndex(c => new { c.TenantId, c.Number }).IsUnique().HasDatabaseName("UQ_CycleCount_Number");
        // Lote 14: ventana por defecto de "lo cambiado" (último ChangesToUtc del almacén con origen CHANGES).
        b.HasIndex(c => new { c.TenantId, c.WarehouseId, c.OriginLookupId, c.ChangesToUtc }).HasDatabaseName("IX_CycleCount_Origin");

        b.HasOne(c => c.Status).WithMany().HasForeignKey(c => c.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(c => c.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(c => c.Lines).WithOne().HasForeignKey(l => l.CycleCountId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CycleCountLineConfiguration : IEntityTypeConfiguration<CycleCountLine>
{
    public void Configure(EntityTypeBuilder<CycleCountLine> b)
    {
        b.ToTable("CycleCountLine");
        b.HasKey(l => l.CycleCountLineId);
        b.Property(l => l.SystemQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.CountedQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.VarianceQty).HasColumnType("decimal(17,3)").HasComputedColumnSql("isnull([CountedQty],(0))-[SystemQty]", stored: true);
        b.Property(l => l.ReconciledSystemQty).HasColumnType("decimal(16,3)");
        // Lote 21: evidencia de la captura original y de la corrección.
        b.Property(l => l.CapturedQty).HasColumnType("decimal(16,3)");
        // Tarea 25: verificación contra lo esperado (conteo informado al capturar).
        b.Property(l => l.CheckState).HasColumnType("varchar(8)");
        b.Property(l => l.FirstCheckQty).HasColumnType("decimal(16,3)");
        b.Property(l => l.LastCheckQty).HasColumnType("decimal(16,3)");

        b.HasIndex(l => new { l.CycleCountId, l.WarehouseBinId, l.ProductId, l.LotId }).IsUnique().HasFilter(null).HasDatabaseName("UQ_CycleCountLine");
        // Lote 14: posiciones con un conteo abierto ("lo cambiado" no las repite).
        b.HasIndex(l => l.WarehouseBinId).IncludeProperties(l => l.CycleCountId).HasDatabaseName("IX_CycleCountLine_Bin");

        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.WarehouseBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PickBatchConfiguration : IEntityTypeConfiguration<PickBatch>
{
    public void Configure(EntityTypeBuilder<PickBatch> b)
    {
        b.ToTable("PickBatch");
        b.HasKey(p => p.PickBatchId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Number).HasMaxLength(40).IsRequired();
        b.Property(p => p.ClientInvoiceNumber).HasMaxLength(40);
        b.Property(p => p.RowVersion).IsRowVersion();

        b.HasIndex(p => new { p.TenantId, p.Number }).IsUnique().HasDatabaseName("UQ_PickBatch_Number");
        // Una orden nace de a lo sumo una recolección activa (última línea contra el doble empaque).
        b.HasIndex(p => p.TransportOrderId).IsUnique().HasFilter("[TransportOrderId] IS NOT NULL AND [IsActive] = 1").HasDatabaseName("UX_PickBatch_Order");
        b.HasIndex(p => new { p.TenantId, p.CollectedAtUtc }).HasDatabaseName("IX_PickBatch_Tenant_Date");

        b.HasOne(p => p.Status).WithMany().HasForeignKey(p => p.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Orders.TransportOrder>().WithMany().HasForeignKey(p => p.TransportOrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.PickBatchId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PickBatchLineConfiguration : IEntityTypeConfiguration<PickBatchLine>
{
    public void Configure(EntityTypeBuilder<PickBatchLine> b)
    {
        b.ToTable("PickBatchLine");
        b.HasKey(l => l.PickBatchLineId);
        b.Property(l => l.Quantity).HasColumnType("decimal(16,3)");
        b.Property(l => l.UnitCost).HasColumnType("decimal(18,4)");

        b.HasIndex(l => l.PickBatchId).HasDatabaseName("IX_PickBatchLine_Batch");

        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventorySerial>().WithMany().HasForeignKey(l => l.SerialId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(l => l.FromBinId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class DockAppointmentConfiguration : IEntityTypeConfiguration<DockAppointment>
{
    public void Configure(EntityTypeBuilder<DockAppointment> b)
    {
        b.ToTable("DockAppointment");
        b.HasKey(a => a.DockAppointmentId);

        b.HasIndex(a => new { a.WarehouseDockId, a.ScheduledStartUtc }).HasDatabaseName("IX_DockAppointment_Dock");

        b.HasOne(a => a.Status).WithMany().HasForeignKey(a => a.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseDock>().WithMany().HasForeignKey(a => a.WarehouseDockId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Asn>().WithMany().HasForeignKey(a => a.AsnId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CrossDockPlanConfiguration : IEntityTypeConfiguration<CrossDockPlan>
{
    public void Configure(EntityTypeBuilder<CrossDockPlan> b)
    {
        b.ToTable("CrossDockPlan");
        b.HasKey(p => p.CrossDockPlanId);
        b.Property(p => p.Number).HasMaxLength(40).IsRequired();

        b.HasIndex(p => new { p.TenantId, p.Number }).IsUnique().HasDatabaseName("UQ_CrossDockPlan_Number");

        b.HasOne(p => p.Status).WithMany().HasForeignKey(p => p.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseZone>().WithMany().HasForeignKey(p => p.StagingZoneId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(p => p.Allocations).WithOne().HasForeignKey(a => a.CrossDockPlanId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CrossDockAllocationConfiguration : IEntityTypeConfiguration<CrossDockAllocation>
{
    public void Configure(EntityTypeBuilder<CrossDockAllocation> b)
    {
        b.ToTable("CrossDockAllocation");
        b.HasKey(a => a.CrossDockAllocationId);
        b.Property(a => a.AllocatedQty).HasColumnType("decimal(16,3)");
        b.Property(a => a.ConfirmedQty).HasColumnType("decimal(16,3)");

        b.HasIndex(a => a.ReceiptLineId).HasDatabaseName("IX_CdAlloc_ReceiptLine");

        b.HasOne<ReceiptLine>().WithMany().HasForeignKey(a => a.ReceiptLineId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Orders.TransportOrder>().WithMany().HasForeignKey(a => a.TransportOrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseTask>().WithMany().HasForeignKey(a => a.WarehouseTaskId).OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// Lote 14 (P1) — descuadre Kárdex ↔ saldo (CAPA 14). Un solo abierto por clave: UX_InvDiscrepancy_OpenKey filtrado por
/// ClosedAtUtc NULL (con varias instancias del API, la base es la última línea). Las FKs compuestas del SQL (producto, almacén,
/// posición, lote y conteo contra su tenant o su padre) se mapean simples, como en el resto del WMS.
/// </summary>
public sealed class InventoryDiscrepancyConfiguration : IEntityTypeConfiguration<InventoryDiscrepancy>
{
    public void Configure(EntityTypeBuilder<InventoryDiscrepancy> b)
    {
        b.ToTable("InventoryDiscrepancy");
        b.HasKey(d => d.InventoryDiscrepancyId);
        b.Property(d => d.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(d => d.LedgerQty).HasColumnType("decimal(16,3)");
        b.Property(d => d.BalanceQty).HasColumnType("decimal(16,3)");
        b.Property(d => d.CorrectedFromQty).HasColumnType("decimal(16,3)");
        b.Property(d => d.CorrectedToQty).HasColumnType("decimal(16,3)");
        b.Property(d => d.ResolutionNotes).HasMaxLength(500);
        b.Property(d => d.RowVersion).IsRowVersion();

        b.HasIndex(d => d.PublicId).IsUnique().HasDatabaseName("UQ_InvDiscrepancy_PublicId");
        b.HasIndex(d => new { d.TenantId, d.KindLookupId, d.ProductId, d.WarehouseId, d.WarehouseBinId, d.LotId })
            .IsUnique().HasFilter("[ClosedAtUtc] IS NULL").HasDatabaseName("UX_InvDiscrepancy_OpenKey");
        b.HasIndex(d => new { d.TenantId, d.ClosedAtUtc, d.DetectedAtUtc }).HasDatabaseName("IX_InvDiscrepancy_Tenant_Open");
        b.HasIndex(d => d.ProductId).HasDatabaseName("IX_InvDiscrepancy_Product");

        b.HasOne(d => d.Status).WithMany().HasForeignKey(d => d.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(d => d.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(d => d.WarehouseBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(d => d.LotId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryTransaction>().WithMany().HasForeignKey(d => d.LastTxnId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CycleCount>().WithMany().HasForeignKey(d => d.CycleCountId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class DamageReportConfiguration : IEntityTypeConfiguration<DamageReport>
{
    public void Configure(EntityTypeBuilder<DamageReport> b)
    {
        b.ToTable("DamageReport");
        b.HasKey(d => d.DamageReportId);
        b.Property(d => d.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(d => d.Quantity).HasColumnType("decimal(16,3)");
        b.Property(d => d.Notes).HasMaxLength(300);
        b.Property(d => d.ResolutionNotes).HasMaxLength(300);
        b.Property(d => d.RowVersion).IsRowVersion();
        b.HasIndex(d => new { d.TenantId, d.StatusCodeId }).HasDatabaseName("IX_DamageReport_Tenant_Status");
        b.HasOne(d => d.Status).WithMany().HasForeignKey(d => d.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(d => d.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Product>().WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryLot>().WithMany().HasForeignKey(d => d.LotId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(d => d.FromBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WarehouseBin>().WithMany().HasForeignKey(d => d.QuarantineBinId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ReceiptHeader>().WithMany().HasForeignKey(d => d.ReceiptHeaderId).OnDelete(DeleteBehavior.NoAction);
    }
}
