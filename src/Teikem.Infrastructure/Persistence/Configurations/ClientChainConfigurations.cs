using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Clients;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 245 (Lote B del plan) — cadenas de clientes. Mapeo 1:1 con la sección «2026-11-25 — Lote 245: Cadenas de clientes» de logistica-db-update.sql.
// La FK de dbo.Client.ChainId en la base es COMPUESTA (ChainId, TenantId) → UQ_ClientChain_IdTenant; aquí basta la simple (precedente: SalesQuota → SalesRoute).
// ClientConfigurations no se toca: la relación se declara desde la cadena (precedente: SalesRepConfiguration con Client.SalesRepId).

public sealed class ClientChainConfiguration : IEntityTypeConfiguration<ClientChain>
{
    private const int NameMaxLength = 100;
    private const int NotesMaxLength = 500;

    public void Configure(EntityTypeBuilder<ClientChain> b)
    {
        b.ToTable("ClientChain");
        b.HasKey(x => x.ClientChainId);
        b.Property(x => x.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(x => x.Name).HasMaxLength(NameMaxLength).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(NotesMaxLength);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("UX_ClientChain_Name").IsUnique().HasFilter("[IsActive] = 1");
        b.HasIndex(x => x.PublicId).HasDatabaseName("UX_ClientChain_PublicId").IsUnique();
        // dbo.Client.ChainId → la cadena del cliente (índice IX_Client_Chain filtrado en el SQL)
        b.HasMany<Client>().WithOne().HasForeignKey(c => c.ChainId).OnDelete(DeleteBehavior.NoAction);
    }
}
