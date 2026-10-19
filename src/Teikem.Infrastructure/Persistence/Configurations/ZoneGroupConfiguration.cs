using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Fleet;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 129 — nivel de zona (Isla/Metro…).
public sealed class ZoneGroupConfiguration : IEntityTypeConfiguration<ZoneGroup>
{
    public void Configure(EntityTypeBuilder<ZoneGroup> b)
    {
        b.ToTable("ZoneGroup");
        b.HasKey(g => g.ZoneGroupId);
        b.Property(g => g.Code).HasMaxLength(ZoneGroupRules.CodeMaxLength).IsRequired();
        b.Property(g => g.Name).HasMaxLength(ZoneGroupRules.NameMaxLength);
        b.HasIndex(g => new { g.TenantId, g.Code }).IsUnique().HasDatabaseName("UQ_ZoneGroup");
        b.HasOne(g => g.DefaultDriver).WithMany().HasForeignKey(g => g.DefaultDriverId).OnDelete(DeleteBehavior.NoAction);
    }
}
