using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Jobs;

namespace Teikem.Infrastructure.Persistence.Configurations;

// Lote 50 (P2.2) — trabajos programados por compañía. Mapeo 1:1 con la sección «2026-10-14 (f)» de Diseño/logistica-db-update.sql
// (dbo.ScheduledJob, dbo.ScheduledJobRun). Los CHECK viven en SQL; aquí se espejan los índices con el mismo nombre.

public sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> b)
    {
        b.ToTable("ScheduledJob");
        b.HasKey(j => j.ScheduledJobId);
        b.Property(j => j.JobKey).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.Property(j => j.ScheduleKind).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(j => j.DailyAt).HasColumnType("time(0)");
        b.Property(j => j.LastStatus).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(j => j.LastMessage).HasMaxLength(400);
        b.Property(j => j.LastError).HasMaxLength(1000);
        b.Property(j => j.LockedBy).HasMaxLength(80);
        b.Property(j => j.RowVersion).IsRowVersion();

        b.HasIndex(j => new { j.TenantId, j.JobKey }).IsUnique().HasDatabaseName("UX_ScheduledJob_Key");
        b.HasIndex(j => j.NextRunUtc).HasDatabaseName("IX_ScheduledJob_Due");
    }
}

public sealed class ScheduledJobRunConfiguration : IEntityTypeConfiguration<ScheduledJobRun>
{
    public void Configure(EntityTypeBuilder<ScheduledJobRun> b)
    {
        b.ToTable("ScheduledJobRun");
        b.HasKey(r => r.ScheduledJobRunId);
        b.Property(r => r.JobKey).HasMaxLength(60).IsUnicode(false).IsRequired();
        b.Property(r => r.Status).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(r => r.TriggeredBy).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(r => r.Message).HasMaxLength(400);
        b.Property(r => r.Error).HasMaxLength(1000);
        b.Property(r => r.InstanceId).HasMaxLength(80).IsRequired();

        b.HasIndex(r => new { r.TenantId, r.JobKey, r.ScheduledJobRunId }).HasDatabaseName("IX_ScheduledJobRun_Job");
    }
}
