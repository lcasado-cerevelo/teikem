using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Sales;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 249 (Lote E) — la modificación de la orden de venta. Mapeo 1:1 con la sección «Lote 249» de logistica-db-update.sql.
// Las FK COMPUESTAS (SalesOrderId, TenantId) y (AdditionSalesOrderId, TenantId) → dbo.SalesOrder(SalesOrderId, TenantId) viven solo en el SQL (precedente de los
// Lotes 245 y 247); aquí van las simples. Los índices se declaran como documentación (no hay migraciones EF).

public sealed class SalesOrderChangeConfiguration : IEntityTypeConfiguration<SalesOrderChange>
{
    public void Configure(EntityTypeBuilder<SalesOrderChange> b)
    {
        b.ToTable("SalesOrderChange");
        b.HasKey(x => x.SalesOrderChangeId);
        b.Property(x => x.PublicId).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
        b.Property(x => x.Kind).HasMaxLength(10).IsRequired();
        b.Property(x => x.AuthorizedByName).HasMaxLength(150).IsRequired();
        b.Property(x => x.AuthorizedByTitle).HasMaxLength(100);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.RequestJson).IsRequired();
        b.Property(x => x.BeforeJson).IsRequired();
        b.Property(x => x.AfterJson).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.TotalBefore).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalAfter).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalDelta).HasColumnType("decimal(18,2)");
        b.Property(x => x.ApprovalRule).HasMaxLength(12);
        b.Property(x => x.BaseFingerprint).HasColumnType("char(64)").IsFixedLength().IsUnicode(false).HasMaxLength(64).IsRequired();
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.AdditionSalesOrderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Catalogs.StatusCode>().WithMany().HasForeignKey(x => x.StatusCodeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Catalogs.LookupCode>().WithMany().HasForeignKey(x => x.AuthorizationChannelLookupId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Clients.ClientContact>().WithMany().HasForeignKey(x => x.AuthorizedByContactId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Teikem.Domain.Files.StoredFile>().WithMany().HasForeignKey(x => x.AttachmentFileId).OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("UX_SalesOrderChange_PublicId");
        b.HasIndex(x => new { x.SalesOrderId, x.ChangeNo }).IsUnique().HasDatabaseName("UQ_SalesOrderChange_No");
        // una sola pendiente por orden, garantizada en la base
        b.HasIndex(x => x.SalesOrderId).IsUnique().HasFilter("[IsPending] = 1").HasDatabaseName("UX_SalesOrderChange_Pending");
        // dos índices sobre las mismas columnas (uno filtrado): se nombran en HasIndex para que EF no los junte
        b.HasIndex(x => new { x.TenantId, x.RegisteredAtUtc }, "IX_SalesOrderChange_PendingTenant").HasFilter("[IsPending] = 1");
        b.HasIndex(x => new { x.TenantId, x.RegisteredAtUtc }, "IX_SalesOrderChange_Registered");
        // un adjunto respalda una sola modificación
        b.HasIndex(x => x.AttachmentFileId).IsUnique().HasFilter("[AttachmentFileId] IS NOT NULL").HasDatabaseName("UX_SalesOrderChange_Attachment");
        b.HasIndex(x => x.AdditionSalesOrderId).HasFilter("[AdditionSalesOrderId] IS NOT NULL").HasDatabaseName("IX_SalesOrderChange_Addition");
    }
}
