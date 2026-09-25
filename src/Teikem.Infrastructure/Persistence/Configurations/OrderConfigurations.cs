using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Orders;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 3 — Órdenes de transporte. Mapeo 1:1 con Diseño/logistica-db-estructura.sql (CAPA 11 + NumberSequence en CAPA 5 +
// ImportTemplate/ImportBatch antes de TransportOrder). Los índices únicos filtrados y CHECKs también viven en SQL (guardas de
// última línea); aquí quedan documentados para que el modelo EF coincida con el esquema. GeoPoint (GEOGRAPHY) no se mapea;
// OrderDocument no se mapea (sin proveedor de blob, DECISIÓN 20).

public sealed class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public void Configure(EntityTypeBuilder<TransportOrder> b)
    {
        b.ToTable("TransportOrder");
        b.HasKey(o => o.TransportOrderId);
        b.Property(o => o.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(o => o.OrderNumber).HasMaxLength(40).IsRequired();
        b.Property(o => o.ClientInvoiceNumber).HasMaxLength(40).IsRequired();
        b.Property(o => o.PackBatchNumber).HasMaxLength(40).IsRequired();
        b.Property(o => o.TotalWeightKg).HasColumnType("decimal(14,3)");
        b.Property(o => o.TotalVolumeM3).HasColumnType("decimal(14,4)");
        b.Property(o => o.QuotedAmount).HasColumnType("decimal(18,4)");
        b.Property(o => o.CodAmount).HasColumnType("decimal(18,4)");
        b.Property(o => o.RowVersion).IsRowVersion();

        // Número de orden único por cliente entre las activas (consecutivo interno del cliente; una eliminada en captura lo libera)
        b.HasIndex(o => new { o.TenantId, o.ClientId, o.OrderNumber }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_Order_Number");
        // Identificador de escaneo único por tenant
        b.HasIndex(o => new { o.TenantId, o.PackBatchNumber }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_Order_PackBatch");
        b.HasIndex(o => new { o.TenantId, o.ClientInvoiceNumber }).HasDatabaseName("IX_Order_Invoice");
        b.HasIndex(o => new { o.TenantId, o.StatusCodeId }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_Order_Tenant_Status");
        b.HasIndex(o => new { o.TenantId, o.CodStatusCodeId }).HasFilter("[CodStatusCodeId] IS NOT NULL").HasDatabaseName("IX_Order_Cod");
        b.HasIndex(o => new { o.TenantId, o.ClientId, o.CreatedAtUtc }).HasFilter("[IsActive] = 1").HasDatabaseName("IX_Order_Client");
        b.HasIndex(o => o.PublicId);

        b.HasOne(o => o.Client).WithMany().HasForeignKey(o => o.ClientId);
        b.HasOne(o => o.Contract).WithMany().HasForeignKey(o => o.ContractId);
        b.HasOne(o => o.ServiceType).WithMany().HasForeignKey(o => o.ServiceTypeLookupId);
        b.HasOne(o => o.Status).WithMany().HasForeignKey(o => o.StatusCodeId);
        b.HasOne(o => o.CodStatus).WithMany().HasForeignKey(o => o.CodStatusCodeId);
        b.HasOne(o => o.SpecialService).WithMany().HasForeignKey(o => o.SpecialServiceId);
        b.HasMany(o => o.Stops).WithOne(s => s.Order).HasForeignKey(s => s.TransportOrderId);
        b.HasMany(o => o.CargoLines).WithOne(l => l.Order).HasForeignKey(l => l.TransportOrderId);
        b.HasMany(o => o.References).WithOne(r => r.Order).HasForeignKey(r => r.TransportOrderId);
    }
}

public sealed class OrderStopConfiguration : IEntityTypeConfiguration<OrderStop>
{
    public void Configure(EntityTypeBuilder<OrderStop> b)
    {
        b.ToTable("OrderStop");
        b.HasKey(s => s.OrderStopId);
        b.Property(s => s.SnapName).HasMaxLength(200);
        b.Property(s => s.SnapLine1).HasMaxLength(200).IsRequired();
        b.Property(s => s.SnapLine2).HasMaxLength(200);
        b.Property(s => s.SnapCity).HasMaxLength(100).IsRequired();
        b.Property(s => s.SnapState).HasMaxLength(100);
        b.Property(s => s.SnapPostalCode).HasMaxLength(20);
        b.Property(s => s.SnapCountryCode).HasColumnType("char(2)").HasMaxLength(2).IsRequired();
        b.Property(s => s.Notes).HasMaxLength(500);
        b.HasIndex(s => s.TransportOrderId).HasDatabaseName("IX_OrderStop_Order");
        b.HasIndex(s => s.LocationId).HasFilter("[LocationId] IS NOT NULL").HasDatabaseName("IX_OrderStop_Location");
        b.HasOne(s => s.StopType).WithMany().HasForeignKey(s => s.StopTypeLookupId);
        b.HasOne(s => s.Location).WithMany().HasForeignKey(s => s.LocationId);
        b.HasOne(s => s.Status).WithMany().HasForeignKey(s => s.StatusCodeId);
    }
}

public sealed class CargoLineConfiguration : IEntityTypeConfiguration<CargoLine>
{
    public void Configure(EntityTypeBuilder<CargoLine> b)
    {
        b.ToTable("CargoLine");
        b.HasKey(l => l.CargoLineId);
        b.Property(l => l.PackageNumber).HasMaxLength(40);
        b.Property(l => l.Description).HasMaxLength(250).IsRequired();
        b.Property(l => l.Quantity).HasColumnType("decimal(14,3)");
        b.Property(l => l.WeightKg).HasColumnType("decimal(12,3)");
        b.Property(l => l.VolumeM3).HasColumnType("decimal(12,4)");
        b.HasIndex(l => l.TransportOrderId).HasDatabaseName("IX_CargoLine_Order");
        // Dos FKs a OrderStop sin navegación (R9: siempre paradas de la misma orden por construcción)
        b.HasOne<OrderStop>().WithMany().HasForeignKey(l => l.PickupStopId);
        b.HasOne<OrderStop>().WithMany().HasForeignKey(l => l.DeliveryStopId);
        b.HasOne(l => l.PackageType).WithMany().HasForeignKey(l => l.PackageTypeLookupId);
    }
}

public sealed class OrderReferenceConfiguration : IEntityTypeConfiguration<OrderReference>
{
    public void Configure(EntityTypeBuilder<OrderReference> b)
    {
        b.ToTable("OrderReference");
        b.HasKey(r => r.OrderReferenceId);
        b.Property(r => r.RefValue).HasMaxLength(120).IsRequired();
        b.Property(r => r.Source).HasMaxLength(80);
        b.HasIndex(r => r.RefValue).HasDatabaseName("IX_OrderReference_Value");
        b.HasOne(r => r.RefType).WithMany().HasForeignKey(r => r.RefTypeLookupId);
    }
}

public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("NumberSequence");
        b.HasKey(n => n.NumberSequenceId);
        b.Property(n => n.Kind).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        // UNIQUE de SQL Server admite un solo NULL en ClientId por (TenantId, Kind): exactamente lo que necesita PACKBATCH.
        b.HasIndex(n => new { n.TenantId, n.Kind, n.ClientId }).IsUnique().HasFilter(null).HasDatabaseName("UQ_NumberSequence");
    }
}

public sealed class ImportTemplateConfiguration : IEntityTypeConfiguration<ImportTemplate>
{
    public void Configure(EntityTypeBuilder<ImportTemplate> b)
    {
        b.ToTable("ImportTemplate");
        b.HasKey(t => t.ImportTemplateId);
        b.Property(t => t.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(t => t.Kind).HasMaxLength(20).IsRequired();
        b.Property(t => t.Name).HasMaxLength(120).IsRequired();
        b.Property(t => t.Delimiter).HasColumnType("nchar(1)").HasMaxLength(1).IsRequired();
        b.Property(t => t.ColumnsJson).IsRequired();
        b.HasIndex(t => new { t.TenantId, t.Kind, t.Name }).IsUnique().HasDatabaseName("UQ_ImportTemplate");
        b.HasIndex(t => t.PublicId);
        b.HasOne(t => t.Client).WithMany().HasForeignKey(t => t.ClientId);
    }
}

public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("ImportBatch");
        b.HasKey(x => x.ImportBatchId);
        b.Property(x => x.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(260);
        b.Property(x => x.RowsJson).IsRequired();
        b.HasIndex(x => x.PublicId);
        b.HasIndex(x => new { x.TenantId, x.ClientId }).HasDatabaseName("IX_ImportBatch_Client");
        b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.ImportTemplateId);
        b.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId);
        b.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusCodeId);
        b.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
    }
}
