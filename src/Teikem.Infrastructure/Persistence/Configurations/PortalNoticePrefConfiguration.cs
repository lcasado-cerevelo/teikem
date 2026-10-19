using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Clients;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 119 (P10) — preferencias de aviso de cada persona del portal.
public sealed class PortalNoticePrefConfiguration : IEntityTypeConfiguration<PortalNoticePref>
{
    public void Configure(EntityTypeBuilder<PortalNoticePref> b)
    {
        b.ToTable("PortalNoticePref");
        b.HasKey(p => p.PortalNoticePrefId);
        b.Property(p => p.Kind).HasMaxLength(30);
        b.HasIndex(p => new { p.PortalUserId, p.Kind }).IsUnique().HasDatabaseName("UQ_PortalNoticePref");
    }
}
