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

        // Lote 247 (sección «Lote 247» de update.sql): lista dedicada (precio especial) de un cliente o de una cadena y cadenas de la lista. En la base las FK son
        // COMPUESTAS por compañía (FK_PriceList_SpecialClient / FK_PriceList_SpecialChain → UQ_Client_IdTenant / UQ_ClientChain_IdTenant) y
        // CK_PriceList_SpecialOwner exige un solo dueño y AppliesToAll = 0; aquí bastan las simples (precedente del Lote 245, decisión 20).
        b.Ignore(p => p.IsSpecial);
        b.HasOne<Teikem.Domain.Clients.Client>().WithMany().HasForeignKey(p => p.SpecialClientId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Clients.ClientChain>().WithMany().HasForeignKey(p => p.SpecialChainId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(p => p.Chains).WithOne().HasForeignKey(c => c.PriceListId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(p => new { p.TenantId, p.SpecialClientId, p.ValidFrom, p.ValidTo }).IsUnique()
            .HasFilter("[IsActive] = 1 AND [SpecialClientId] IS NOT NULL").HasDatabaseName("UX_PriceList_SpecialClient");
        b.HasIndex(p => new { p.TenantId, p.SpecialChainId, p.ValidFrom, p.ValidTo }).IsUnique()
            .HasFilter("[IsActive] = 1 AND [SpecialChainId] IS NOT NULL").HasDatabaseName("UX_PriceList_SpecialChain");
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

/// <summary>Lote 247 — cadena de una lista que no es general (FK compuestas por compañía en la base: FK_PriceListChain_List y FK_PriceListChain_Chain).</summary>
public sealed class PriceListChainConfiguration : IEntityTypeConfiguration<PriceListChain>
{
    public void Configure(EntityTypeBuilder<PriceListChain> b)
    {
        b.ToTable("PriceListChain");
        b.HasKey(c => c.PriceListChainId);
        b.HasOne<Teikem.Domain.Clients.ClientChain>().WithMany().HasForeignKey(c => c.ChainId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(c => new { c.PriceListId, c.ChainId }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_PriceListChain_Chain");
        b.HasIndex(c => new { c.TenantId, c.ChainId }).HasDatabaseName("IX_PriceListChain_Chain");
    }
}
