using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Cod;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 84 (P8) — COD. CodCollection ya existe en el DDL original; sus columnas nuevas (CollectedBy, ReconciledBy, ReconcileRunId) y las tablas de las corridas las crea la sección «2026-10-17 (x)» de update.sql.

public sealed class CodCollectionConfiguration : IEntityTypeConfiguration<CodCollection>
{
    public void Configure(EntityTypeBuilder<CodCollection> b)
    {
        b.ToTable("CodCollection");
        b.HasKey(c => c.CodCollectionId);
        b.Property(c => c.AmountCollected).HasColumnType("decimal(18,4)");
        b.Property(c => c.CheckNumber).HasMaxLength(CodRules.MaxCheckNumberLength);
        b.Property(c => c.Reference).HasMaxLength(CodRules.MaxReferenceLength);
        b.Property(c => c.Notes).HasMaxLength(CodRules.MaxNoteLength);
        b.HasIndex(c => c.TransportOrderId).HasDatabaseName("IX_CodCollection_Order");
    }
}

public sealed class CodReconcileRunConfiguration : IEntityTypeConfiguration<CodReconcileRun>
{
    public void Configure(EntityTypeBuilder<CodReconcileRun> b)
    {
        b.ToTable("CodReconcileRun");
        b.HasKey(r => r.CodReconcileRunId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Source).HasMaxLength(CodRules.MaxSourceLength);
        b.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.CodReconcileRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.PublicId);
        b.HasIndex(r => new { r.TenantId, r.CreatedAtUtc }).HasDatabaseName("IX_CodReconcileRun_Tenant");
    }
}

public sealed class CodReconcileItemConfiguration : IEntityTypeConfiguration<CodReconcileItem>
{
    public void Configure(EntityTypeBuilder<CodReconcileItem> b)
    {
        b.ToTable("CodReconcileItem");
        b.HasKey(i => i.CodReconcileItemId);
        b.Property(i => i.Code).HasMaxLength(CodRules.MaxCodeLength).IsRequired();
        b.Property(i => i.Source).HasMaxLength(CodRules.MaxSourceLength);
        b.Property(i => i.Result).HasMaxLength(30).IsRequired();
        b.Property(i => i.Amount).HasColumnType("decimal(18,4)");
    }
}

// Lote 86 (P8) — remesa COD. Las tablas ya existen en el DDL original (NetRemitted y NetAmount son columnas calculadas); BatchId de CodCollection ya estaba mapeada; IsActive y los índices los crea la
// sección «2026-10-17 (y)» de update.sql.
public sealed class CodRemittanceBatchConfiguration : IEntityTypeConfiguration<CodRemittanceBatch>
{
    public void Configure(EntityTypeBuilder<CodRemittanceBatch> b)
    {
        b.ToTable("CodRemittanceBatch");
        b.HasKey(r => r.CodRemittanceBatchId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Number).HasMaxLength(40).IsRequired();
        b.Property(r => r.GrossCollected).HasColumnType("decimal(18,4)");
        b.Property(r => r.CommissionPct).HasColumnType("decimal(5,2)");
        b.Property(r => r.CommissionAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.NetRemitted).HasColumnType("decimal(18,4)").HasComputedColumnSql("[GrossCollected] - [CommissionAmount]", stored: true);
        b.Property(r => r.ExportRef).HasMaxLength(RemittanceRules.MaxExportRefLength);
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.CodRemittanceBatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.PublicId);
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UQ_CodRemittanceBatch_Number");
    }
}

public sealed class CodRemittanceLineConfiguration : IEntityTypeConfiguration<CodRemittanceLine>
{
    public void Configure(EntityTypeBuilder<CodRemittanceLine> b)
    {
        b.ToTable("CodRemittanceLine");
        b.HasKey(l => l.CodRemittanceLineId);
        b.Property(l => l.AmountCollected).HasColumnType("decimal(18,4)");
        b.Property(l => l.CommissionAmount).HasColumnType("decimal(18,4)");
        b.Property(l => l.NetAmount).HasColumnType("decimal(18,4)").HasComputedColumnSql("[AmountCollected] - [CommissionAmount]", stored: true);
    }
}

// Lote 90 (P12) — fotos diarias de indicadores. La tabla y su única (tenant, día, indicador, dimensión) ya existen en el DDL original.
public sealed class KpiSnapshotConfiguration : IEntityTypeConfiguration<Teikem.Domain.Analytics.KpiSnapshot>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Analytics.KpiSnapshot> b)
    {
        b.ToTable("KpiDailySnapshot");
        b.HasKey(k => k.KpiDailySnapshotId);
        b.Property(k => k.SnapshotDate).HasColumnType("date");
        b.Property(k => k.NumericValue).HasColumnType("decimal(18,4)");
        b.HasIndex(k => new { k.TenantId, k.SnapshotDate, k.KpiKeyLookupId, k.DimEntityLookupId, k.DimId }).IsUnique().HasDatabaseName("UQ_KpiSnapshot");
    }
}

// Lote 93 (P9) — intentos de entrega (fuente de verdad de la liquidación a choferes).
public sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<Teikem.Domain.Fleet.DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Fleet.DeliveryAttempt> b)
    {
        b.ToTable("DeliveryAttempt");
        b.HasKey(a => a.DeliveryAttemptId);
        b.Property(a => a.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(a => a.Notes).HasMaxLength(Teikem.Domain.Fleet.DeliveryAttemptRules.MaxNotesLength);
        b.Property(a => a.Source).HasMaxLength(20);
        b.Property(a => a.VoidReason).HasMaxLength(Teikem.Domain.Fleet.DeliveryAttemptRules.MaxVoidReasonLength);
        b.HasIndex(a => new { a.TransportOrderId, a.AttemptNumber }).HasDatabaseName("IX_DeliveryAttempt_Order");
        b.HasIndex(a => new { a.TenantId, a.DriverId, a.AttemptedAtUtc }).HasDatabaseName("IX_DeliveryAttempt_Driver");
    }
}

// Lote 94 (P9) — liquidación a choferes.
public sealed class DriverSettlementRunConfiguration : IEntityTypeConfiguration<Teikem.Domain.Fleet.DriverSettlementRun>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Fleet.DriverSettlementRun> b)
    {
        b.ToTable("DriverSettlementRun");
        b.HasKey(r => r.DriverSettlementRunId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Number).HasMaxLength(40);
        b.Property(r => r.PayoutFormulaCode).HasMaxLength(40);
        b.Property(r => r.TotalAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.Note).HasMaxLength(Teikem.Domain.Fleet.SettlementRules.MaxNoteLength);
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.DriverSettlementRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.PublicId).HasDatabaseName("IX_DriverSettlementRun_PublicId");
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UQ_DriverSettlementRun_Number");
        b.HasIndex(r => new { r.TenantId, r.StatusCodeId }).HasDatabaseName("IX_DriverSettlementRun_Status");
    }
}

public sealed class DriverSettlementLineConfiguration : IEntityTypeConfiguration<Teikem.Domain.Fleet.DriverSettlementLine>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Fleet.DriverSettlementLine> b)
    {
        b.ToTable("DriverSettlementLine");
        b.HasKey(l => l.DriverSettlementLineId);
        b.Property(l => l.Kind).HasMaxLength(20);
        b.Property(l => l.ServiceDate).HasColumnType("date");
        b.Property(l => l.Description).HasMaxLength(250);
        b.Property(l => l.BreakdownJson).HasMaxLength(2000);
        b.Property(l => l.CalculatedAmount).HasColumnType("decimal(18,4)");
        b.Property(l => l.Amount).HasColumnType("decimal(18,4)");
        b.Property(l => l.AdjustmentNote).HasMaxLength(Teikem.Domain.Fleet.SettlementRules.MaxAdjustNoteLength);
        b.HasIndex(l => l.DriverSettlementRunId).HasDatabaseName("IX_DriverSettlementLine_Run");
    }
}

// Lote 98 (P10) — avisos al cliente.
public sealed class ClientNotificationSettingConfiguration : IEntityTypeConfiguration<Teikem.Domain.Clients.ClientNotificationSetting>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Clients.ClientNotificationSetting> b)
    {
        b.ToTable("ClientNotificationSetting");
        b.HasKey(s => s.ClientNotificationSettingId);
        b.Property(s => s.Kind).HasMaxLength(30);
        b.Property(s => s.ExtraEmails).HasMaxLength(Teikem.Domain.Clients.ClientNotificationRules.MaxListLength);
        b.Property(s => s.ExtraPhones).HasMaxLength(Teikem.Domain.Clients.ClientNotificationRules.MaxListLength);
        b.Ignore(s => s.AnyChannel);
        b.HasIndex(s => new { s.TenantId, s.ClientId, s.Kind }).IsUnique().HasDatabaseName("UQ_ClientNotificationSetting");
    }
}

public sealed class ClientNotificationEventConfiguration : IEntityTypeConfiguration<Teikem.Domain.Clients.ClientNotificationEvent>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Clients.ClientNotificationEvent> b)
    {
        b.ToTable("ClientNotificationEvent");
        b.HasKey(e => e.ClientNotificationEventId);
        b.Property(e => e.Kind).HasMaxLength(30);
        b.HasIndex(e => new { e.TenantId, e.ClientId, e.Kind }).HasDatabaseName("IX_ClientNotificationEvent_Pending");
    }
}

public sealed class ClientNotificationConfiguration : IEntityTypeConfiguration<Teikem.Domain.Clients.ClientNotification>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Clients.ClientNotification> b)
    {
        b.ToTable("ClientNotification");
        b.HasKey(n => n.ClientNotificationId);
        b.Property(n => n.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(n => n.Kind).HasMaxLength(30);
        b.Property(n => n.Subject).HasMaxLength(200);
        b.Property(n => n.BodyText).HasMaxLength(2000);
        b.Property(n => n.SummaryJson).HasMaxLength(1000);
        b.HasIndex(n => new { n.TenantId, n.ClientId, n.CreatedAtUtc }).HasDatabaseName("IX_ClientNotification_Client");
    }
}

public sealed class ClientNotificationDeliveryConfiguration : IEntityTypeConfiguration<Teikem.Domain.Clients.ClientNotificationDelivery>
{
    public void Configure(EntityTypeBuilder<Teikem.Domain.Clients.ClientNotificationDelivery> b)
    {
        b.ToTable("ClientNotificationDelivery");
        b.HasKey(d => d.ClientNotificationDeliveryId);
        b.Property(d => d.Channel).HasMaxLength(10);
        b.Property(d => d.Recipient).HasMaxLength(200);
        b.Property(d => d.Status).HasMaxLength(10);
        b.Property(d => d.Message).HasMaxLength(400);
        b.HasIndex(d => d.ClientNotificationId).HasDatabaseName("IX_ClientNotificationDelivery_Notification");
    }
}
