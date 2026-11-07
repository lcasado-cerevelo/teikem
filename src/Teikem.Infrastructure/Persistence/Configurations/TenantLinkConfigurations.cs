using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Integrations;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 227 (P7 del pedido de vendedores) — compañías enlazadas. Mapeo 1:1 con dbo.TenantLink y dbo.TenantLinkOrder (sección «2026-11-07 — Lote 227» de logistica-db-update.sql).

public sealed class TenantLinkConfiguration : IEntityTypeConfiguration<TenantLink>
{
    public void Configure(EntityTypeBuilder<TenantLink> b)
    {
        b.ToTable("TenantLink");
        b.HasKey(l => l.TenantLinkId);
        b.Property(l => l.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(l => l.Status).HasMaxLength(20).IsRequired();
        b.Property(l => l.Note).HasMaxLength(250);
        b.Property(l => l.ResponseNote).HasMaxLength(250);
        b.Property(l => l.EndReason).HasMaxLength(250);
        b.Property(l => l.RowVersion).IsRowVersion();
        b.HasIndex(l => l.PublicId).IsUnique().HasDatabaseName("UX_TenantLink_PublicId");
        // una compañía cliente despacha a lo sumo por UNA compañía a la vez (propuesto o activo)
        b.HasIndex(l => l.ClientTenantId).IsUnique().HasFilter("[Status] IN ('PROPOSED','ACTIVE')").HasDatabaseName("UX_TenantLink_ClientOpen");
        // un cliente del proveedor representa a una sola compañía enlazada activa
        b.HasIndex(l => new { l.ProviderTenantId, l.ProviderClientId }).IsUnique().HasFilter("[Status] = 'ACTIVE' AND [ProviderClientId] IS NOT NULL").HasDatabaseName("UX_TenantLink_ProviderClient");
        b.HasIndex(l => new { l.ProviderTenantId, l.Status }).HasDatabaseName("IX_TenantLink_Provider");
        b.HasOne<Teikem.Domain.Tenancy.Tenant>().WithMany().HasForeignKey(l => l.ClientTenantId);
        b.HasOne<Teikem.Domain.Tenancy.Tenant>().WithMany().HasForeignKey(l => l.ProviderTenantId);
        b.HasOne<Teikem.Domain.Clients.Client>().WithMany().HasForeignKey(l => l.ProviderClientId);
    }
}

public sealed class TenantLinkOrderConfiguration : IEntityTypeConfiguration<TenantLinkOrder>
{
    public void Configure(EntityTypeBuilder<TenantLinkOrder> b)
    {
        b.ToTable("TenantLinkOrder");
        b.HasKey(o => o.TenantLinkOrderId);
        b.Property(o => o.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(o => o.RequestJson).IsRequired();
        b.Property(o => o.State).HasMaxLength(20).IsRequired();
        b.Property(o => o.LastError).HasMaxLength(1000);
        b.Property(o => o.ClaimToken).IsConcurrencyToken();
        b.Property(o => o.ProviderOrderNumber).HasMaxLength(40);
        b.Property(o => o.ProviderStatusCode).HasMaxLength(40);
        b.Property(o => o.ProviderStatusLabelJson).HasMaxLength(400);
        b.Property(o => o.DeliveredTo).HasMaxLength(150);
        b.HasIndex(o => o.PublicId).IsUnique().HasDatabaseName("UX_TenantLinkOrder_PublicId");
        // idempotencia: un despacho tiene a lo sumo una entrega por la compañía enlazada
        b.HasIndex(o => o.PickBatchId).IsUnique().HasDatabaseName("UX_TenantLinkOrder_PickBatch");
        b.HasIndex(o => new { o.TenantId, o.SalesOrderId }).HasDatabaseName("IX_TenantLinkOrder_SalesOrder");
        b.HasIndex(o => new { o.TenantId, o.State, o.IsFinal }).HasDatabaseName("IX_TenantLinkOrder_Sync");
        b.HasOne<TenantLink>().WithMany().HasForeignKey(o => o.TenantLinkId);
        b.HasOne<Teikem.Domain.Sales.SalesOrder>().WithMany().HasForeignKey(o => o.SalesOrderId);
        b.HasOne<Teikem.Domain.Wms.PickBatch>().WithMany().HasForeignKey(o => o.PickBatchId);
        b.HasOne<Teikem.Domain.Tenancy.Tenant>().WithMany().HasForeignKey(o => o.ProviderTenantId);
        b.HasOne<Teikem.Domain.Orders.TransportOrder>().WithMany().HasForeignKey(o => o.ProviderOrderId);
    }
}
