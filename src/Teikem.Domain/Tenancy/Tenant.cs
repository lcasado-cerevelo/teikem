using Teikem.Domain.Common;

namespace Teikem.Domain.Tenancy;

[AuditEntity(Constants.EntityTypes.Tenant)]
public class Tenant : ISoftDeletable
{
    public int TenantId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string DefaultLangCode { get; set; } = "es";
    /// <summary>Bits Dom=1,Lun=2,Mar=4,Mié=8,Jue=16,Vie=32,Sáb=64; 62 = L-V.</summary>
    public byte WorkDaysMask { get; set; } = 62;
    public int MaxStopsPerRouteDefault { get; set; } = 25;
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public byte[]? RowVersion { get; set; }

    // --- Extensiones Lote 1 (db/migrations/0002_lote1_extensiones.sql) ---
    /// <summary>Tipo de servicio por defecto para Entrada de órdenes (doc módulo 2).</summary>
    public int? DefaultServiceTypeLookupId { get; set; }
    /// <summary>Tipo de paquete por defecto para Entrada de órdenes (doc módulo 2).</summary>
    public int? DefaultPackageTypeLookupId { get; set; }
    /// <summary>Política MFA: obligatorio para usuarios internos del tenant.</summary>
    public bool MfaRequired { get; set; }
    /// <summary>Ventana de reautenticación AAL2 en minutos (step-up para acciones sensibles).</summary>
    public int Aal2WindowMinutes { get; set; } = 30;
    /// <summary>Vida del refresh token (sesión) en días.</summary>
    public int SessionDays { get; set; } = 30;
    /// <summary>Marca por compañía: tema de color y logos (JSON libre, validado en servicio).</summary>
    public string? BrandingJson { get; set; }

    public ICollection<TenantHoliday> Holidays { get; set; } = new List<TenantHoliday>();
    public ICollection<TenantModule> Modules { get; set; } = new List<TenantModule>();
}

/// <summary>Feriados del tenant; con WorkDaysMask define el calendario laboral (SLA en días hábiles, tendencias).</summary>
[AuditEntity(Constants.EntityTypes.Tenant)]
public class TenantHoliday : ITenantScoped, ISoftDeletable
{
    public int TenantHolidayId { get; set; }
    public int TenantId { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

/// <summary>Catálogo completo de capacidades de la plataforma (siempre vive en el esquema, activo o no).</summary>
public class ModuleDefinition
{
    public string ModuleKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Agrupación de menú: Operacion, Almacen, Dinero, Equipos, Catalogo, Analisis, Sistema.</summary>
    public string Category { get; set; } = string.Empty;
    public string? DependsOnModuleKey { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Extensión Lote 1: módulo núcleo que ningún tenant puede apagar (LTL_GROUND, SYSTEM).</summary>
    public bool IsCore { get; set; }

    public ModuleDefinition? DependsOn { get; set; }
}

/// <summary>Encendido por tenant. Ausencia de fila = módulo apagado (default seguro).</summary>
[AuditEntity(Constants.EntityTypes.TenantModule)]
public class TenantModule : ITenantScoped
{
    public int TenantId { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    [NotAudited] public DateTime EnabledAtUtc { get; set; } = DateTime.UtcNow;
    public int? EnabledBy { get; set; }
    public Tenant? Tenant { get; set; }
    public ModuleDefinition? Module { get; set; }
}
