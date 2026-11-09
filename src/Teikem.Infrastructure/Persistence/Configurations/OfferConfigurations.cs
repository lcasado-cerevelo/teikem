using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 229 — ofertas. Mapeo 1:1 con la sección «2026-11-09 — Lote 229» de Diseño/logistica-db-update.sql.

public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> b)
    {
        b.ToTable("Offer");
        b.HasKey(o => o.OfferId);
        b.Property(o => o.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(o => o.Name).HasMaxLength(OfferCodes.MaxNameLength).IsRequired();
        b.Property(o => o.ValidFrom).HasColumnType("date");
        b.Property(o => o.ValidTo).HasColumnType("date");
        b.Property(o => o.Notes).HasMaxLength(OfferCodes.MaxNotesLength);
        b.Property(o => o.RowVersion).IsRowVersion();
        b.HasOne<Teikem.Domain.Files.StoredFile>().WithMany().HasForeignKey(o => o.ImageFileId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OfferId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(o => o.Clients).WithOne().HasForeignKey(c => c.OfferId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(o => o.PublicId).IsUnique().HasDatabaseName("UQ_Offer_PublicId");
        b.HasIndex(o => new { o.TenantId, o.IsActive, o.IsPublished }).HasDatabaseName("IX_Offer_Tenant");
    }
}

public sealed class OfferItemConfiguration : IEntityTypeConfiguration<OfferItem>
{
    public void Configure(EntityTypeBuilder<OfferItem> b)
    {
        b.ToTable("OfferItem");
        b.HasKey(i => i.OfferItemId);
        b.Property(i => i.Price).HasColumnType("decimal(18,4)");
        b.HasOne<Teikem.Domain.Wms.Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Files.StoredFile>().WithMany().HasForeignKey(i => i.ImageFileId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(i => new { i.OfferId, i.ProductId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_OfferItem_Product");
        b.HasIndex(i => new { i.TenantId, i.ProductId }).HasDatabaseName("IX_OfferItem_Product");
    }
}

public sealed class OfferClientConfiguration : IEntityTypeConfiguration<OfferClient>
{
    public void Configure(EntityTypeBuilder<OfferClient> b)
    {
        b.ToTable("OfferClient");
        b.HasKey(c => c.OfferClientId);
        b.HasOne<Teikem.Domain.Clients.Client>().WithMany().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(c => new { c.OfferId, c.ClientId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_OfferClient_Client");
        b.HasIndex(c => new { c.TenantId, c.ClientId }).HasDatabaseName("IX_OfferClient_Client");
    }
}
