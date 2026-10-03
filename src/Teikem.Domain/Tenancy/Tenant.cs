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

    // --- Extensiones Lote 1 (estructura, Lote 1) ---
    /// <summary>Tipo de servicio por defecto para Entrada de órdenes (doc módulo 2).</summary>
    public int? DefaultServiceTypeLookupId { get; set; }
    /// <summary>Tipo de paquete por defecto para Entrada de órdenes (doc módulo 2).</summary>
    public int? DefaultPackageTypeLookupId { get; set; }
    /// <summary>Política MFA: obligatorio para usuarios internos del tenant. Por default true (Lote F8a); el tenant demo
    /// de desarrollo lo apaga explícitamente (DemoTenantSeeder) para no bloquear pruebas automatizadas.</summary>
    public bool MfaRequired { get; set; } = true;
    /// <summary>Ventana de reautenticación AAL2 en minutos (step-up para acciones sensibles).</summary>
    public int Aal2WindowMinutes { get; set; } = 30;
    /// <summary>Vida del refresh token (sesión) en días.</summary>
    public int SessionDays { get; set; } = 30;
    /// <summary>Lote 8A: vida en días de la sesión de un aparato de almacén (login por aparato + PIN); se renueva al refrescar.</summary>
    public int DeviceSessionDays { get; set; } = 30;
    /// <summary>Marca por compañía: tema de color y logos (JSON libre, validado en servicio).</summary>
    public string? BrandingJson { get; set; }

    // --- Región y formatos de la compañía (2026-10). Defaults = los de dbo.Tenant (Puerto Rico); reglas en TenantFormatRules. ---
    /// <summary>Región que trae el juego de valores por defecto: 'PR' o 'US'.</summary>
    public string RegionCode { get; set; } = TenantFormatRules.PuertoRico;
    /// <summary>Zona horaria (nombre IANA): "hoy", los días locales y el SLA cuentan en esta zona (ITenantClock).</summary>
    public string TimeZoneId { get; set; } = TenantFormatRules.PuertoRicoTimeZone;
    /// <summary>Moneda base ISO 4217.</summary>
    public string CurrencyCode { get; set; } = "USD";
    public string CurrencySymbol { get; set; } = "$";
    /// <summary>'B' = símbolo antes del monto, 'A' = después.</summary>
    public string CurrencySymbolPosition { get; set; } = TenantFormatRules.SymbolPositions.Before;
    /// <summary>0, 2 o 3.</summary>
    public byte CurrencyDecimals { get; set; } = 2;
    /// <summary>'MDY', 'DMY' o 'YMD'.</summary>
    public string DateOrder { get; set; } = TenantFormatRules.PuertoRicoDateOrder;
    /// <summary>'/', '-' o '.'.</summary>
    public string DateSeparator { get; set; } = "/";
    /// <summary>12 o 24 horas.</summary>
    public byte TimeFormat { get; set; } = 12;
    /// <summary>0 = domingo, 1 = lunes.</summary>
    public byte WeekStartDay { get; set; }
    /// <summary>',', '.' o espacio; nunca igual al decimal.</summary>
    public string ThousandsSeparator { get; set; } = ",";
    /// <summary>'.' o ','.</summary>
    public string DecimalSeparator { get; set; } = ".";
    public string PhoneCountryCode { get; set; } = "+1";
    /// <summary>Cada '#' es un dígito; el teléfono se guarda solo con dígitos y se muestra con esta máscara.</summary>
    public string PhoneMask { get; set; } = "(###) ###-####";

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
    /// <summary>Lote 1: módulo núcleo que ningún tenant puede apagar (LTL_GROUND, SYSTEM).</summary>
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
