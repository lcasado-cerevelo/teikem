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
