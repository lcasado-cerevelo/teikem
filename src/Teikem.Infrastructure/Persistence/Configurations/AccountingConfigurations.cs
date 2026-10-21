using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Accounting;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 181 (H1): contabilización de compras y de despachos.
public sealed class AccountingRunConfiguration : IEntityTypeConfiguration<AccountingRun>
{
    public void Configure(EntityTypeBuilder<AccountingRun> b)
    {
        b.ToTable("AccountingRun");
        b.HasKey(r => r.AccountingRunId);
        b.Property(r => r.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(r => r.Kind).HasMaxLength(10);
        b.Property(r => r.Number).HasMaxLength(40);
        b.Property(r => r.PeriodStart).HasColumnType("date");
        b.Property(r => r.PeriodEnd).HasColumnType("date");
        b.Property(r => r.TotalAmount).HasColumnType("decimal(18,4)");
        b.Property(r => r.Note).HasMaxLength(AccountingRules.MaxNoteLength);
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.AccountingRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.PublicId).HasDatabaseName("IX_AccountingRun_PublicId");
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique().HasDatabaseName("UQ_AccountingRun_Number");
        b.HasIndex(r => new { r.TenantId, r.Kind, r.StatusCodeId }).HasDatabaseName("IX_AccountingRun_Kind_Status");
    }
}

public sealed class AccountingRunLineConfiguration : IEntityTypeConfiguration<AccountingRunLine>
{
    public void Configure(EntityTypeBuilder<AccountingRunLine> b)
    {
        b.ToTable("AccountingRunLine");
        b.HasKey(l => l.AccountingRunLineId);
        b.Property(l => l.SourceType).HasMaxLength(12);
        b.Property(l => l.DocDate).HasColumnType("date");
        b.Property(l => l.DocumentNumber).HasMaxLength(60);
        b.Property(l => l.ReferenceNumber).HasMaxLength(60);
        b.Property(l => l.PartyName).HasMaxLength(200);
        b.Property(l => l.WarehouseCode).HasMaxLength(20);
        b.Property(l => l.Sku).HasMaxLength(60);
        b.Property(l => l.ProductName).HasMaxLength(200);
        b.Property(l => l.LotNumber).HasMaxLength(60);
        b.Property(l => l.Quantity).HasColumnType("decimal(16,3)");
        b.Property(l => l.UnitCost).HasColumnType("decimal(18,4)");
        b.Property(l => l.TotalCost).HasColumnType("decimal(18,4)");
        b.Property(l => l.CostSource).HasMaxLength(10);
        b.HasIndex(l => l.AccountingRunId).HasDatabaseName("IX_AccountingRunLine_Run");
        b.HasIndex(l => new { l.SourceType, l.SourceId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_AccountingRunLine_Source");
    }
}
