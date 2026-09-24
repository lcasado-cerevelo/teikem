using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Teikem.Domain.Audit;
using Teikem.Domain.Security;

namespace Teikem.Infrastructure.Persistence.Configurations;

public sealed class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> b)
    {
        b.ToTable("UserTenant");
        b.HasKey(m => m.UserTenantId);
        b.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();
        b.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId);
        b.HasOne(m => m.Status).WithMany().HasForeignKey(m => m.StatusCodeId);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permission");
        b.HasKey(p => p.PermissionId);
        b.Property(p => p.Code).HasMaxLength(80).IsRequired();
        b.HasIndex(p => p.Code).IsUnique();
        b.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryLookupId);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Role");
        b.HasKey(r => r.RoleId);
        b.Property(r => r.Name).HasMaxLength(80).IsRequired();
        b.Property(r => r.RowVersion).IsRowVersion();
        b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
        b.HasMany(r => r.Permissions).WithOne(rp => rp.Role).HasForeignKey(rp => rp.RoleId);
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermission");
        b.HasKey(rp => rp.RolePermissionId);
        b.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
        b.HasOne(rp => rp.Permission).WithMany().HasForeignKey(rp => rp.PermissionId);
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRole");
        b.HasKey(ur => ur.UserRoleId);
        b.HasIndex(ur => new { ur.UserId, ur.RoleId, ur.TenantId }).IsUnique();
        b.HasOne(ur => ur.User).WithMany().HasForeignKey(ur => ur.UserId);
        b.HasOne(ur => ur.Role).WithMany().HasForeignKey(ur => ur.RoleId);
    }
}

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> b)
    {
        b.ToTable("UserPermission");
        b.HasKey(up => up.UserPermissionId);
        b.HasIndex(up => new { up.UserId, up.TenantId, up.PermissionId }).IsUnique();
        b.HasOne(up => up.Permission).WithMany().HasForeignKey(up => up.PermissionId);
    }
}

public sealed class UserDataScopeConfiguration : IEntityTypeConfiguration<UserDataScope>
{
    public void Configure(EntityTypeBuilder<UserDataScope> b)
    {
        b.ToTable("UserDataScope");
        b.HasKey(s => s.UserDataScopeId);
        b.HasOne(s => s.ScopeEntity).WithMany().HasForeignKey(s => s.ScopeEntityLookupId);
    }
}

public sealed class UserMfaFactorConfiguration : IEntityTypeConfiguration<UserMfaFactor>
{
    public void Configure(EntityTypeBuilder<UserMfaFactor> b)
    {
        b.ToTable("UserMfaFactor");
        b.HasKey(f => f.UserMfaFactorId);
        b.Property(f => f.SecretEnc).HasColumnType("varbinary(512)");
        b.Property(f => f.PhoneE164).HasMaxLength(20);
        b.HasIndex(f => new { f.UserId, f.FactorTypeLookupId }).IsUnique();
        b.HasOne(f => f.FactorType).WithMany().HasForeignKey(f => f.FactorTypeLookupId);
    }
}

public sealed class MfaRecoveryCodeConfiguration : IEntityTypeConfiguration<MfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<MfaRecoveryCode> b)
    {
        b.ToTable("MfaRecoveryCode");
        b.HasKey(c => c.MfaRecoveryCodeId);
        b.Property(c => c.CodeHash).HasMaxLength(200).IsRequired();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshToken");
        b.HasKey(t => t.RefreshTokenId);
        b.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
        b.Property(t => t.DeviceInfo).HasMaxLength(200);
        b.Property(t => t.ReplacedByTokenHash).HasMaxLength(200);
        b.Ignore(t => t.IsActive);
        b.HasIndex(t => t.TokenHash);
    }
}

public sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> b)
    {
        b.ToTable("SecurityEvent");
        b.HasKey(e => e.SecurityEventId);
        b.Property(e => e.IpAddress).HasMaxLength(45);
        b.Property(e => e.UserAgent).HasMaxLength(300);
        b.HasOne(e => e.EventType).WithMany().HasForeignKey(e => e.EventTypeLookupId);
        b.HasOne(e => e.Outcome).WithMany().HasForeignKey(e => e.OutcomeLookupId);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLog");
        b.HasKey(a => a.AuditLogId);
        b.Property(a => a.IpAddress).HasMaxLength(45);
        b.Property(a => a.UserAgent).HasMaxLength(300);
        b.HasOne(a => a.EntityType).WithMany().HasForeignKey(a => a.EntityTypeLookupId);
        b.HasOne(a => a.Action).WithMany().HasForeignKey(a => a.ActionLookupId);
    }
}
