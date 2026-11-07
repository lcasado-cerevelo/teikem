using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 221 — listas de precios. Mapeo 1:1 con la sección «2026-11-07 — Lote 221» de Diseño/logistica-db-update.sql.

public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> b)
    {
        b.ToTable("PriceList");
        b.HasKey(p => p.PriceListId);
        b.Property(p => p.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(p => p.Name).HasMaxLength(PriceListCodes.MaxNameLength).IsRequired();
        b.Property(p => p.ValidFrom).HasColumnType("date");
        b.Property(p => p.ValidTo).HasColumnType("date");
        b.Property(p => p.Notes).HasMaxLength(PriceListCodes.MaxNotesLength);
        b.Property(p => p.RowVersion).IsRowVersion();
        b.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PriceListId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(p => p.Clients).WithOne().HasForeignKey(c => c.PriceListId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(p => p.PublicId).IsUnique().HasDatabaseName("UQ_PriceList_PublicId");
        b.HasIndex(p => new { p.TenantId, p.IsActive, p.IsPublished }).HasDatabaseName("IX_PriceList_Tenant");
    }
}

public sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> b)
    {
        b.ToTable("PriceListItem");
        b.HasKey(i => i.PriceListItemId);
        b.Property(i => i.Price).HasColumnType("decimal(18,4)");
        b.HasOne<Teikem.Domain.Wms.Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Files.StoredFile>().WithMany().HasForeignKey(i => i.ImageFileId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(i => new { i.PriceListId, i.ProductId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_PriceListItem_Product");
        b.HasIndex(i => new { i.TenantId, i.ProductId }).HasDatabaseName("IX_PriceListItem_Product");
    }
}

public sealed class PriceListClientConfiguration : IEntityTypeConfiguration<PriceListClient>
{
    public void Configure(EntityTypeBuilder<PriceListClient> b)
    {
        b.ToTable("PriceListClient");
        b.HasKey(c => c.PriceListClientId);
        b.HasOne<Teikem.Domain.Clients.Client>().WithMany().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(c => new { c.PriceListId, c.ClientId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_PriceListClient_Client");
        b.HasIndex(c => new { c.TenantId, c.ClientId }).HasDatabaseName("IX_PriceListClient_Client");
    }
}
