/* ============================================================================
   PLATAFORMA DE LOGÍSTICA — ESTRUCTURA DE BASE DE DATOS (UNIFICADA)
   SQL Server / compatible con EF Core (.NET 8)
   ----------------------------------------------------------------------------
   Consolida: v3 (módulos 1-3 + capa de catálogos/estatus), módulos 4-6
   (flota/WMS/cross-dock), módulos restantes (inventario, app, POD, portal,
   facturación, dashboards, API) y la capa de seguridad/auditoría.

   Orden de creación por capas (respeta dependencias de FK):
     0. Identidad (Identity core, mínimo)        9. Inventario
     1. Tenant + Catálogos/Estatus              10. Flota y choferes
     2. Seguridad (RBAC, MFA, sesiones)         11. Órdenes de transporte
     3. Motor de estatus + bitácora             12. Trips y rutas
     4. Contactos                               13. Detalle de flota/mant.
     5. Clientes y contratos                    14. WMS
     6. Tarifas                                 15. Cross-docking
     7. Almacenes (estructura física)           16. App móvil + POD
     8. (inventario va antes de órdenes)        17. Portal + Facturación
                                                18. Dashboards + API + Auditoría
   Convenciones: PK INT IDENTITY interno + PublicId UNIQUEIDENTIFIER externo;
   auditoría CreatedAtUtc/By, UpdatedAtUtc/By; soft-delete IsActive;
   concurrencia RowVersion; geo GEOGRAPHY; dinero DECIMAL(18,4).
   NOTA: en producción las tablas AspNetUsers/AspNetRoles las crea la migración
   de ASP.NET Core Identity (IdentityUser<int>). Aquí se crean mínimas (guarded)
   para que el script corra standalone y existan los destinos de FK.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* =========================================================================
   CAPA 0 — IDENTIDAD (ASP.NET Core Identity, llaves INT; guarded para poder
   convivir con la migración de Identity si algún día se genera desde EF)
   ========================================================================= */
IF OBJECT_ID('dbo.AspNetRoles') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoles (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetRoles PRIMARY KEY,
        Name             NVARCHAR(256) NULL,
        NormalizedName   NVARCHAR(256) NULL,
        ConcurrencyStamp NVARCHAR(MAX) NULL
    );
    CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles(NormalizedName) WHERE NormalizedName IS NOT NULL;
END
GO

IF OBJECT_ID('dbo.AspNetUsers') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUsers (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUsers PRIMARY KEY,
        UserName             NVARCHAR(256) NULL,
        NormalizedUserName   NVARCHAR(256) NULL,
        Email                NVARCHAR(256) NULL,
        NormalizedEmail      NVARCHAR(256) NULL,
        EmailConfirmed       BIT NOT NULL CONSTRAINT DF_AspNetUsers_EmailConfirmed DEFAULT 0,
        PasswordHash         NVARCHAR(MAX) NULL,
        SecurityStamp        NVARCHAR(MAX) NULL,
        ConcurrencyStamp     NVARCHAR(MAX) NULL,
        PhoneNumber          NVARCHAR(50) NULL,
        PhoneNumberConfirmed BIT NOT NULL CONSTRAINT DF_AspNetUsers_PhoneConfirmed DEFAULT 0,
        TwoFactorEnabled     BIT NOT NULL CONSTRAINT DF_AspNetUsers_TwoFactor DEFAULT 0,
        LockoutEnd           DATETIMEOFFSET NULL,
        LockoutEnabled       BIT NOT NULL CONSTRAINT DF_AspNetUsers_LockoutEnabled DEFAULT 1,
        AccessFailedCount    INT NOT NULL CONSTRAINT DF_AspNetUsers_AccessFailed DEFAULT 0,
        -- columnas de aplicación
        FullName             NVARCHAR(150) NULL,
        DefaultTenantId      INT NULL,
        UserKindLookupId     INT NULL,
        IsActive             BIT NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT 1,
        CreatedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_AspNetUsers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        LastLoginUtc         DATETIME2 NULL,
        IsPlatformAdmin      BIT NOT NULL CONSTRAINT DF_AspNetUsers_IsPlatformAdmin DEFAULT 0,
        -- 2026-09-30: primer ingreso obligatorio (verificar correo → contraseña propia → MFA). 1 = pendiente.
        OnboardingRequired   BIT NOT NULL CONSTRAINT DF_AspNetUsers_OnboardingRequired DEFAULT 1,
        MustChangePassword   BIT NOT NULL CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT 1,
        EmailVerifiedUtc     DATETIME2 NULL
    );
    CREATE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail);
    CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
END
ELSE IF COL_LENGTH('dbo.AspNetUsers', 'IsPlatformAdmin') IS NULL
BEGIN
    ALTER TABLE dbo.AspNetUsers ADD IsPlatformAdmin BIT NOT NULL CONSTRAINT DF_AspNetUsers_IsPlatformAdmin DEFAULT 0;
END
GO
-- 2026-09-30: primer ingreso obligatorio. En una base existente, todos los usuarios quedan con el primer ingreso pendiente
-- (pedido de Luis: nuevos y existentes); los usuarios de prueba de la demo los marca completos el seeder.
IF COL_LENGTH('dbo.AspNetUsers', 'OnboardingRequired') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD OnboardingRequired BIT NOT NULL CONSTRAINT DF_AspNetUsers_OnboardingRequired DEFAULT 1;
IF COL_LENGTH('dbo.AspNetUsers', 'MustChangePassword') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD MustChangePassword BIT NOT NULL CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT 1;
IF COL_LENGTH('dbo.AspNetUsers', 'EmailVerifiedUtc') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD EmailVerifiedUtc DATETIME2 NULL;
GO

IF OBJECT_ID('dbo.AspNetRoleClaims') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoleClaims (
        Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY,
        RoleId     INT NOT NULL CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId FOREIGN KEY REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE,
        ClaimType  NVARCHAR(MAX) NULL,
        ClaimValue NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_AspNetRoleClaims_RoleId ON dbo.AspNetRoleClaims(RoleId);
END
GO

IF OBJECT_ID('dbo.AspNetUserClaims') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserClaims (
        Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUserClaims PRIMARY KEY,
        UserId     INT NOT NULL CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId FOREIGN KEY REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
        ClaimType  NVARCHAR(MAX) NULL,
        ClaimValue NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims(UserId);
END
GO

IF OBJECT_ID('dbo.AspNetUserLogins') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserLogins (
        LoginProvider       NVARCHAR(450) NOT NULL,
        ProviderKey         NVARCHAR(450) NOT NULL,
        ProviderDisplayName NVARCHAR(MAX) NULL,
        UserId              INT NOT NULL CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId FOREIGN KEY REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
        CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey)
    );
    CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins(UserId);
END
GO

IF OBJECT_ID('dbo.AspNetUserRoles') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserRoles (
        UserId INT NOT NULL CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId FOREIGN KEY REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
        RoleId INT NOT NULL CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId FOREIGN KEY REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE,
        CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId)
    );
    CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles(RoleId);
END
GO

IF OBJECT_ID('dbo.AspNetUserTokens') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserTokens (
        UserId        INT NOT NULL CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId FOREIGN KEY REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
        LoginProvider NVARCHAR(450) NOT NULL,
        Name          NVARCHAR(450) NOT NULL,
        Value         NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name)
    );
END
GO

/* =========================================================================
   CAPA 1 — TENANT + CATÁLOGOS / ESTATUS
   ========================================================================= */
CREATE TABLE dbo.Tenant (
    TenantId        INT IDENTITY(1,1) PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    Name            NVARCHAR(200) NOT NULL,
    LegalName       NVARCHAR(250) NULL,
    TaxId           NVARCHAR(50)  NULL,
    DefaultLangCode CHAR(2) NOT NULL DEFAULT 'es',
    WorkDaysMask    TINYINT NOT NULL DEFAULT 62,  -- bits Dom=1,Lun=2,Mar=4,Mié=8,Jue=16,Vie=32,Sáb=64; 62 = L-V
    MaxStopsPerRouteDefault INT NOT NULL DEFAULT 25,  -- default de alerta; el chofer puede tener su propio límite (Driver.MaxStopsPerRoute)
    DefaultServiceTypeLookupId INT NULL,              -- Entity='ServiceType': default de captura (FK diferida, LookupCode se crea después)
    DefaultPackageTypeLookupId INT NULL,              -- Entity='PackageType': default de captura (FK diferida)
    MfaRequired     BIT NOT NULL DEFAULT 1,           -- política MFA del tenant (Lote F8a: por default exige MFA a todos)
    Aal2WindowMinutes INT NOT NULL DEFAULT 30,        -- ventana de reautenticación AAL2 (step-up)
    SessionDays     INT NOT NULL DEFAULT 30,          -- vida del refresh token
    DeviceSessionDays INT NOT NULL DEFAULT 30,        -- Lote 8A: vida de la sesión de un aparato de almacén (aparato + PIN)
    BrandingJson    NVARCHAR(MAX) NULL,               -- marca por compañía (tema de color y logos)
    -- Región y formatos de la compañía (pedido de Luis, 2026-10): zona horaria, moneda y formatos de fecha, hora, números y teléfono.
    -- RegionCode trae el juego de valores por defecto (solo 'PR' y 'US'); cada columna se puede cambiar por separado. Las fechas se guardan
    -- en UTC y se muestran en TimeZoneId; "hoy", los días hábiles y el SLA cuentan en esa zona. El idioma de la interfaz es por usuario
    -- (DefaultLangCode es solo el del usuario nuevo) y no cambia nada de esto. Los defaults reproducen el comportamiento previo (Puerto Rico, USD).
    RegionCode      CHAR(2)       NOT NULL DEFAULT 'PR',          -- 'PR' | 'US'
    TimeZoneId      VARCHAR(64)   NOT NULL DEFAULT 'America/Puerto_Rico',   -- nombre IANA; el servicio valida que la plataforma lo conozca
    CurrencyCode    CHAR(3)       NOT NULL DEFAULT 'USD',         -- moneda base (ISO 4217); un cliente puede tener otra en su contrato
    CurrencySymbol  NVARCHAR(3)   NOT NULL DEFAULT N'$',
    CurrencySymbolPosition CHAR(1) NOT NULL DEFAULT 'B',          -- B = antes del monto, A = después
    CurrencyDecimals TINYINT      NOT NULL DEFAULT 2,
    DateOrder       CHAR(3)       NOT NULL DEFAULT 'MDY',         -- orden de la fecha: MDY | DMY | YMD
    DateSeparator   CHAR(1)       NOT NULL DEFAULT '/',           -- '/', '-' o '.'
    TimeFormat      TINYINT       NOT NULL DEFAULT 12,            -- 12 o 24 horas
    WeekStartDay    TINYINT       NOT NULL DEFAULT 0,             -- 0 = domingo, 1 = lunes
    ThousandsSeparator NVARCHAR(1) NOT NULL DEFAULT N',',         -- ',', '.' o espacio
    DecimalSeparator CHAR(1)      NOT NULL DEFAULT '.',           -- '.' o ','; nunca igual al de miles
    PhoneCountryCode VARCHAR(5)   NOT NULL DEFAULT '+1',
    PhoneMask       VARCHAR(30)   NOT NULL DEFAULT '(###) ###-####',   -- cada # es un dígito; el teléfono se guarda solo con dígitos y se muestra con esta máscara
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion      ROWVERSION,
    CONSTRAINT CK_Tenant_Region CHECK (RegionCode IN ('PR','US')),
    CONSTRAINT CK_Tenant_CurrencySymbolPosition CHECK (CurrencySymbolPosition IN ('B','A')),
    CONSTRAINT CK_Tenant_CurrencyDecimals CHECK (CurrencyDecimals IN (0,2,3)),
    CONSTRAINT CK_Tenant_DateOrder CHECK (DateOrder IN ('MDY','DMY','YMD')),
    CONSTRAINT CK_Tenant_DateSeparator CHECK (DateSeparator IN ('/','-','.')),
    CONSTRAINT CK_Tenant_TimeFormat CHECK (TimeFormat IN (12,24)),
    CONSTRAINT CK_Tenant_WeekStartDay CHECK (WeekStartDay IN (0,1)),
    CONSTRAINT CK_Tenant_Separators CHECK (ThousandsSeparator IN (N',',N'.',N' ') AND DecimalSeparator IN ('.',',') AND ThousandsSeparator <> DecimalSeparator),
    CONSTRAINT CK_Tenant_PhoneMask CHECK (PhoneMask LIKE '%#%')
);
GO

-- Feriados del tenant (PR + federales + propios). Con WorkDaysMask define el calendario laboral:
-- alimenta SLA en días hábiles, fechas límite y tendencias de "últimos N días hábiles".
CREATE TABLE dbo.TenantHoliday (
    TenantHolidayId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    HolidayDate  DATE NOT NULL,
    Name         NVARCHAR(120) NOT NULL,
    IsRecurring  BIT NOT NULL DEFAULT 0,          -- mismo mes/día cada año
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_TenantHoliday UNIQUE (TenantId, HolidayDate)
);
GO

-- Logos de la marca de la compañía (Ajustes → Marca): una fila por (compañía, ranura). Ranuras: lockup (fondo claro), lockup-inverted
-- (fondo oscuro), mark y mark-inverted (marca cuadrada). El archivo vive aquí (SVG/PNG/JPG/WebP, hasta 512 KB; el servicio valida el
-- contenido real). Quitar el logo es soft delete (IsActive = 0, el binario se libera); subir otro reutiliza la fila. La bitácora de
-- cambios guarda quién y cuándo sin el binario.
CREATE TABLE dbo.TenantBrandLogo (
    TenantBrandLogoId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Slot         VARCHAR(20) NOT NULL,
    ContentType  VARCHAR(40) NOT NULL,            -- tipo REAL por cabecera: image/svg+xml | image/png | image/jpeg | image/webp
    SizeBytes    INT NOT NULL,
    ContentHash  CHAR(64) NOT NULL,               -- SHA-256 en hexadecimal (ETag)
    Content      VARBINARY(MAX) NOT NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT UQ_TenantBrandLogo UNIQUE (TenantId, Slot),
    CONSTRAINT CK_TenantBrandLogo_Slot CHECK (Slot IN ('lockup','lockup-inverted','mark','mark-inverted')),
    CONSTRAINT CK_TenantBrandLogo_Size CHECK (SizeBytes BETWEEN 0 AND 524288)
);
GO

/* =========================================================================
   CAPA 1B — CATÁLOGO DE MÓDULOS (plataforma multi-tenant, no solo Advance)
   Teikem es una plataforma para toda la industria de logística, cualquier
   modo de transporte. Cada tenant enciende solo los módulos que usa;
   el catálogo completo vive aquí SIEMPRE, activo o no.
   ========================================================================= */
CREATE TABLE dbo.ModuleDefinition (
    ModuleKey    VARCHAR(40) PRIMARY KEY,       -- ej. 'COD','WMS_LOTSERIAL','CROSSDOCK','RENTAL_EQUIPMENT','RENTAL_BILLING','MARITIME'
    Name         NVARCHAR(120) NOT NULL,
    NameEn       NVARCHAR(120) NOT NULL,
    Description  NVARCHAR(300) NULL,
    Category     NVARCHAR(40)  NOT NULL,        -- agrupación de menú: 'Operacion','Almacen','Dinero','Equipos','Catalogo','Analisis'
    DependsOnModuleKey VARCHAR(40) NULL REFERENCES dbo.ModuleDefinition(ModuleKey), -- ej. RENTAL_BILLING depende de RENTAL_EQUIPMENT
    SortOrder    INT NOT NULL DEFAULT 0,
    IsCore       BIT NOT NULL DEFAULT 0,          -- núcleo: ningún tenant lo puede apagar (LTL_GROUND, SYSTEM)
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

-- Encendido por tenant. Ausencia de fila = módulo apagado (default seguro).
CREATE TABLE dbo.TenantModule (
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ModuleKey    VARCHAR(40) NOT NULL REFERENCES dbo.ModuleDefinition(ModuleKey),
    IsEnabled    BIT NOT NULL DEFAULT 1,
    EnabledAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    EnabledBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    PRIMARY KEY (TenantId, ModuleKey)
);
GO

-- Registro de dominios de catálogo (Scope 1=Lookup, 2=Status). Piso del concepto.
CREATE TABLE dbo.CatalogDomain (
    CatalogDomainId INT IDENTITY(1,1) PRIMARY KEY,
    DomainKey       NVARCHAR(60) NOT NULL,
    Scope           TINYINT NOT NULL,            -- 1=Lookup, 2=Status (constante de sistema)
    LabelJson       NVARCHAR(MAX) NOT NULL,
    Description     NVARCHAR(300) NULL,
    IsSystem        BIT NOT NULL DEFAULT 1,
    IsActive        BIT NOT NULL DEFAULT 1,
    TenantId        INT NULL REFERENCES dbo.Tenant(TenantId),   -- NULL = dominio global; con valor = lista propia del tenant
    CONSTRAINT UQ_CatalogDomain UNIQUE (DomainKey)
);
GO

CREATE TABLE dbo.LookupCode (
    LookupCodeId    INT IDENTITY(1,1) PRIMARY KEY,
    Entity          NVARCHAR(60) NOT NULL,
    InternalCode    NVARCHAR(40) NOT NULL,
    LabelJson       NVARCHAR(MAX) NOT NULL,
    DescriptionJson NVARCHAR(MAX) NULL,
    ExtraJson       NVARCHAR(MAX) NULL,
    SortOrder       INT NOT NULL DEFAULT 100,
    IsSystem        BIT NOT NULL DEFAULT 0,
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc    DATETIME2 NULL,
    RowVersion      ROWVERSION,
    TenantId        INT NULL REFERENCES dbo.Tenant(TenantId),   -- NULL = valor global; con valor = valor de una lista del tenant
    CONSTRAINT UQ_LookupCode UNIQUE (Entity, InternalCode),
    CONSTRAINT FK_LookupCode_Domain FOREIGN KEY (Entity) REFERENCES dbo.CatalogDomain(DomainKey)
);
CREATE INDEX IX_LookupCode_Entity ON dbo.LookupCode(Entity) WHERE IsActive = 1;
GO

CREATE TABLE dbo.LookupCodeOverride (
    LookupCodeOverrideId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    LookupCodeId    INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    CustomLabelJson NVARCHAR(MAX) NULL,
    CustomExtraJson NVARCHAR(MAX) NULL,
    IsEnabled       BIT NOT NULL DEFAULT 1,
    SortOverride    INT NULL,
    CONSTRAINT UQ_LookupCodeOverride UNIQUE (TenantId, LookupCodeId)
);
GO

CREATE TABLE dbo.StatusCode (
    StatusCodeId    INT IDENTITY(1,1) PRIMARY KEY,
    Entity          NVARCHAR(60) NOT NULL,
    InternalCode    NVARCHAR(40) NOT NULL,
    LabelJson       NVARCHAR(MAX) NOT NULL,
    DescriptionJson NVARCHAR(MAX) NULL,
    ColorHex        CHAR(7) NULL,
    Icon            NVARCHAR(40) NULL,
    SortOrder       INT NOT NULL DEFAULT 100,
    StageKindLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='StageKind'
    IsInitial       BIT NOT NULL DEFAULT 0,
    IsActive        BIT NOT NULL DEFAULT 1,
    RowVersion      ROWVERSION,
    CONSTRAINT UQ_StatusCode UNIQUE (Entity, InternalCode),
    CONSTRAINT FK_StatusCode_Domain FOREIGN KEY (Entity) REFERENCES dbo.CatalogDomain(DomainKey)
);
CREATE INDEX IX_StatusCode_Entity ON dbo.StatusCode(Entity) WHERE IsActive = 1;
GO

CREATE TABLE dbo.StatusCodeOverride (
    StatusCodeOverrideId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    StatusCodeId    INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    CustomLabelJson NVARCHAR(MAX) NULL,
    CustomColorHex  CHAR(7) NULL,
    IsEnabled       BIT NOT NULL DEFAULT 1,
    SortOverride    INT NULL,
    CONSTRAINT UQ_StatusCodeOverride UNIQUE (TenantId, StatusCodeId)
);
GO

-- FKs diferidas de AspNetUsers hacia catálogos/tenant
ALTER TABLE dbo.AspNetUsers ADD CONSTRAINT FK_User_DefaultTenant
    FOREIGN KEY (DefaultTenantId) REFERENCES dbo.Tenant(TenantId);
ALTER TABLE dbo.AspNetUsers ADD CONSTRAINT FK_User_UserKind
    FOREIGN KEY (UserKindLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
ALTER TABLE dbo.Tenant ADD CONSTRAINT FK_Tenant_DefaultServiceType
    FOREIGN KEY (DefaultServiceTypeLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
ALTER TABLE dbo.Tenant ADD CONSTRAINT FK_Tenant_DefaultPackageType
    FOREIGN KEY (DefaultPackageTypeLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
GO

-- Lote 1 de cambios de Almacén: catálogo GLOBAL de referencia ciudad <-> código postal (sin TenantId, como los catálogos
-- globales). Lo siembra logistica-db-seed.sql (catálogo USPS: 42.522 ZIP de EE. UU., sus territorios y Puerto Rico); el API solo lo lee
-- (GET /api/v1/postal-localities) y el formulario de almacén copia ciudad, estado, código postal y país a las columnas de
-- texto de Warehouse. Un municipio tiene varios ZIP y un ZIP puede cubrir más de una localidad: único (País, ZIP, Ciudad).
-- Guardado (IF OBJECT_ID) para crearse en una base ya existente sin tocar nada más.
IF OBJECT_ID('dbo.PostalLocality') IS NULL
BEGIN
    CREATE TABLE dbo.PostalLocality (
        PostalLocalityId INT IDENTITY(1,1) PRIMARY KEY,
        City            NVARCHAR(100) NOT NULL,
        PostalCode      NVARCHAR(10)  NOT NULL,
        State           NVARCHAR(100) NULL,
        Municipality    NVARCHAR(100) NULL,  -- solo Puerto Rico: municipio del ZIP (la ciudad postal puede ser un barrio: 00952 SABANA SECA → Toa Baja)
        CountryLookupId INT NOT NULL CONSTRAINT FK_PostalLocality_Country REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='Country'
        IsActive        BIT NOT NULL CONSTRAINT DF_PostalLocality_IsActive DEFAULT 1,
        CONSTRAINT UQ_PostalLocality UNIQUE (CountryLookupId, PostalCode, City)
    );
END
ELSE IF COL_LENGTH('dbo.PostalLocality', 'Municipality') IS NULL
    ALTER TABLE dbo.PostalLocality ADD Municipality NVARCHAR(100) NULL;
GO

/* =========================================================================
   CAPA 2 — SEGURIDAD (RBAC, MFA, SESIONES)
   ========================================================================= */
CREATE TABLE dbo.UserTenant (
    UserTenantId INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    IsDefault    BIT NOT NULL DEFAULT 0,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),  -- Entity='MembershipStatus'
    InvitedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    JoinedAtUtc  DATETIME2 NULL,
    MfaRequired  BIT NOT NULL DEFAULT 0,  -- Lote F8a: exige MFA a esta persona en esta compañía, aparte de Tenant.MfaRequired
    CONSTRAINT UQ_UserTenant UNIQUE (UserId, TenantId)
);
CREATE INDEX IX_UserTenant_Tenant ON dbo.UserTenant(TenantId);
GO

CREATE TABLE dbo.Permission (
    PermissionId INT IDENTITY(1,1) PRIMARY KEY,
    Code         NVARCHAR(80) NOT NULL,
    CategoryLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='PermissionCategory'
    LabelJson    NVARCHAR(MAX) NOT NULL,
    IsSystem     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Permission_Code UNIQUE (Code)
);
GO

CREATE TABLE dbo.Role (
    RoleId       INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NULL REFERENCES dbo.Tenant(TenantId),   -- NULL = plantilla de sistema
    Name         NVARCHAR(80) NOT NULL,
    DescriptionJson NVARCHAR(MAX) NULL,
    IsSystem     BIT NOT NULL DEFAULT 0,
    IsActive     BIT NOT NULL DEFAULT 1,
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_Role UNIQUE (TenantId, Name)
);
GO

CREATE TABLE dbo.RolePermission (
    RolePermissionId INT IDENTITY(1,1) PRIMARY KEY,
    RoleId       INT NOT NULL REFERENCES dbo.Role(RoleId),
    PermissionId INT NOT NULL REFERENCES dbo.Permission(PermissionId),
    CONSTRAINT UQ_RolePermission UNIQUE (RoleId, PermissionId)
);
GO

CREATE TABLE dbo.UserRole (
    UserRoleId   INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    RoleId       INT NOT NULL REFERENCES dbo.Role(RoleId),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    GrantedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    GrantedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_UserRole UNIQUE (UserId, RoleId, TenantId)
);
CREATE INDEX IX_UserRole_User ON dbo.UserRole(UserId, TenantId);
GO

-- Permiso extra concedido directo a una persona (excepción puntual, sin crear ni tocar un rol)
CREATE TABLE dbo.UserPermission (
    UserPermissionId INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    PermissionId INT NOT NULL REFERENCES dbo.Permission(PermissionId),
    GrantedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    GrantedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_UserPermission UNIQUE (UserId, TenantId, PermissionId)
);
CREATE INDEX IX_UserPermission_User ON dbo.UserPermission(UserId, TenantId);
GO

CREATE TABLE dbo.UserDataScope (
    UserDataScopeId INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ScopeEntityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    ScopeId      INT NOT NULL,
    CONSTRAINT UQ_UserDataScope UNIQUE (UserId, TenantId, ScopeEntityLookupId, ScopeId)
);
GO

CREATE TABLE dbo.UserMfaFactor (
    UserMfaFactorId INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    FactorTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='MfaFactorType'
    SecretEnc    VARBINARY(512) NULL,
    PhoneE164    NVARCHAR(20) NULL,
    IsConfirmed  BIT NOT NULL DEFAULT 0,
    ConfirmedAtUtc DATETIME2 NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_UserMfaFactor UNIQUE (UserId, FactorTypeLookupId)
);
GO

CREATE TABLE dbo.MfaRecoveryCode (
    MfaRecoveryCodeId INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    CodeHash     NVARCHAR(200) NOT NULL,
    UsedAtUtc    DATETIME2 NULL
);
GO

CREATE TABLE dbo.RefreshToken (
    RefreshTokenId BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TokenHash    NVARCHAR(200) NOT NULL,
    DeviceInfo   NVARCHAR(200) NULL,
    Aal2VerifiedAtUtc DATETIME2 NULL,
    IssuedAtUtc  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAtUtc DATETIME2 NOT NULL,
    RevokedAtUtc DATETIME2 NULL,
    ReplacedByTokenHash NVARCHAR(200) NULL,
    UserDeviceId INT NULL,                             -- Lote 8A: sesión de un aparato de almacén (FK diferida: UserDevice va después)
    IpAddress    NVARCHAR(45) NULL                     -- Lote F10: IP de la emisión o de la última renovación (Seguridad y auditoría → Sesiones)
);
CREATE INDEX IX_RefreshToken_User ON dbo.RefreshToken(UserId) WHERE RevokedAtUtc IS NULL;
GO

-- Lote 8A — aparatos de confianza de la app de almacén (terminales con escáner). Los registra el administrador
-- (devices.manage) con un código único por compañía; el aparato se enlaza una vez con el código de registro de un solo uso
-- (8 caracteres, 24 h) y luego se autentica con su secreto. Código y secreto se guardan SOLO como hash. DriverDevice (Lote 4)
-- se conserva. FK del almacén por defecto diferida (Warehouse se crea en la capa 7): FK_UserDevice_DefaultWarehouse.
CREATE TABLE dbo.UserDevice (
    UserDeviceId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(30) NOT NULL,
    Name         NVARCHAR(100) NULL,
    Model        NVARCHAR(80) NULL,
    PlatformLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='DevicePlatform'
    EnrollCodeHash NVARCHAR(200) NULL,                 -- hash del código de registro pendiente (un solo uso)
    EnrollCodeExpiresUtc DATETIME2 NULL,
    SecretHash   NVARCHAR(200) NULL,                   -- hash del secreto del aparato (NULL = aún no registrado)
    EnrolledAtUtc DATETIME2 NULL,
    DefaultWarehouseId INT NULL,                       -- FK diferida (DefaultWarehouseId, TenantId) → Warehouse
    ThemeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),          -- Entity='UiTheme' (LIGHT/DARK)
    RegisteredBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RegisteredAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    IsActive     BIT NOT NULL DEFAULT 1,
    SessionsNotBeforeUtc DATETIME2 NULL,               -- sello: access tokens con `did` emitidos antes se rechazan (baja, reactivación, registro)
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_UserDevice_Code UNIQUE (TenantId, Code),
    CONSTRAINT UQ_UserDevice_PublicId UNIQUE (PublicId)
);
GO

-- Lote 8A — datos técnicos del aparato (último contacto, último usuario, versión de la app), 1:1 con UserDevice. Van en
-- tabla aparte y SIN ROWVERSION: el heartbeat y el login con PIN escriben aquí y no cambian el ROWVERSION de UserDevice,
-- que es el token de concurrencia del PATCH/desactivar/reactivar/código de registro (solo ediciones reales dan 409).
CREATE TABLE dbo.UserDeviceActivity (
    UserDeviceId INT NOT NULL PRIMARY KEY REFERENCES dbo.UserDevice(UserDeviceId),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    LastSeenUtc  DATETIME2 NULL,
    LastUserId   INT NULL REFERENCES dbo.AspNetUsers(Id),
    AppVersion   NVARCHAR(20) NULL
);
GO

-- Sesiones ligadas a un aparato: desactivarlo revoca todas las suyas.
ALTER TABLE dbo.RefreshToken ADD CONSTRAINT FK_RefreshToken_UserDevice
    FOREIGN KEY (UserDeviceId) REFERENCES dbo.UserDevice(UserDeviceId);
CREATE INDEX IX_RefreshToken_Device ON dbo.RefreshToken(UserDeviceId) WHERE UserDeviceId IS NOT NULL AND RevokedAtUtc IS NULL;
GO

-- Lote 8A — PIN personal (4 a 6 dígitos) para entrar en los aparatos de almacén; uno por usuario y compañía. Solo el hash
-- (mismo PasswordHasher de Identity). 5 intentos fallidos → bloqueo de 15 minutos (LockedUntilUtc).
CREATE TABLE dbo.UserPin (
    UserPinId    INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    PinHash      NVARCHAR(200) NOT NULL,
    FailedCount  INT NOT NULL DEFAULT 0,
    LockedUntilUtc DATETIME2 NULL,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT UQ_UserPin_User UNIQUE (TenantId, UserId)
);
GO

/* =========================================================================
   CAPA 3 — MOTOR DE ESTATUS + BITÁCORA
   ========================================================================= */
CREATE TABLE dbo.StatusLateralEntry (
    StatusLateralEntryId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NULL REFERENCES dbo.Tenant(TenantId),   -- NULL = regla por defecto
    EntityTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    LateralStatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    FromStatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    IsAllowed    BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_StatusLateralEntry UNIQUE (TenantId, EntityTypeLookupId, LateralStatusCodeId, FromStatusCodeId)
);
GO

CREATE TABLE dbo.StatusCapability (
    StatusCapabilityId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NULL REFERENCES dbo.Tenant(TenantId),
    EntityTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    CapabilityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='Capability'
    IsAllowed    BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_StatusCapability UNIQUE (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId)
);
GO

CREATE TABLE dbo.EntityStatusHistory (
    EntityStatusHistoryId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    EntityTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    EntityId     INT NOT NULL,
    FromStatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    ToStatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    Comment      NVARCHAR(500) NULL,
    ChangedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ChangedBy    INT NULL REFERENCES dbo.AspNetUsers(Id)
);
CREATE INDEX IX_EntityStatusHistory ON dbo.EntityStatusHistory(EntityTypeLookupId, EntityId);
-- Lote 7A: "Actividad reciente" lee el historial del tenant por ventana de fecha (últimas 24/48 h u hoy).
CREATE INDEX IX_EntityStatusHistory_TenantDate ON dbo.EntityStatusHistory(TenantId, ChangedAtUtc);
GO

/* =========================================================================
   CAPA 4 — CONTACTOS
   ========================================================================= */
CREATE TABLE dbo.ContactPoint (
    ContactPointId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    OwnerEntityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    OwnerId      INT NOT NULL,
    ContactTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ContactType'
    Value        NVARCHAR(200) NOT NULL,
    Extension    NVARCHAR(20) NULL,
    Label        NVARCHAR(80) NULL,
    IsPrimary    BIT NOT NULL DEFAULT 0,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_ContactPoint_Owner ON dbo.ContactPoint(OwnerEntityLookupId, OwnerId) WHERE IsActive = 1;
GO

/* =========================================================================
   CAPA 4B — CAMPOS PERSONALIZADOS E INFORMES (transversal, por entidad)
   Depende solo de Tenant, LookupCode, AspNetUsers, Role (capas 0-3).
   Aplica a entidades principales vía EntityType (CLIENT, TRANSPORT_ORDER,
   TRIP, VEHICLE, DRIVER, WAREHOUSE, PRODUCT, CONTRACT, INVOICE, LOCATION...).
   ========================================================================= */

-- Definición de campo personalizado por tenant + entidad (estilo FieldDefinition de SEPHAS)
CREATE TABLE dbo.CustomFieldDefinition (
    CustomFieldDefinitionId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    EntityTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    FieldKey        NVARCHAR(60) NOT NULL,          -- code estable, ej. 'cost_center'
    LabelJson       NVARCHAR(MAX) NOT NULL,         -- multilingüe
    DescriptionJson NVARCHAR(MAX) NULL,
    DataTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='CustomFieldDataType'
    IsRequired      BIT NOT NULL DEFAULT 0,
    IsUnique        BIT NOT NULL DEFAULT 0,          -- unicidad por entidad (validada en servicio)
    DefaultValue    NVARCHAR(400) NULL,
    ValidationJson  NVARCHAR(MAX) NULL,              -- regex/min/max/etc. — reusa el DSL de SEPHAS
    RefEntity       NVARCHAR(60) NULL,               -- para DataType=LOOKUP_REF: dominio LookupCode referenciado
    ShowInList      BIT NOT NULL DEFAULT 0,          -- columna candidata en listados/informes
    SortOrder       INT NOT NULL DEFAULT 100,
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc    DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion      ROWVERSION,
    CONSTRAINT UQ_CustomFieldDefinition UNIQUE (TenantId, EntityTypeLookupId, FieldKey)
);
CREATE INDEX IX_CustomFieldDefinition_Entity ON dbo.CustomFieldDefinition(TenantId, EntityTypeLookupId) WHERE IsActive = 1;
GO

-- Opciones para campos tipo SELECT/MULTISELECT
CREATE TABLE dbo.CustomFieldOption (
    CustomFieldOptionId INT IDENTITY(1,1) PRIMARY KEY,
    CustomFieldDefinitionId INT NOT NULL REFERENCES dbo.CustomFieldDefinition(CustomFieldDefinitionId),
    OptionValue     NVARCHAR(80) NOT NULL,
    LabelJson       NVARCHAR(MAX) NOT NULL,
    SortOrder       INT NOT NULL DEFAULT 100,
    IsActive        BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_CustomFieldOption UNIQUE (CustomFieldDefinitionId, OptionValue)
);
GO

-- Valor del campo personalizado por registro (EAV tipado para poder filtrar/ordenar)
CREATE TABLE dbo.CustomFieldValue (
    CustomFieldValueId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    CustomFieldDefinitionId INT NOT NULL REFERENCES dbo.CustomFieldDefinition(CustomFieldDefinitionId),
    EntityId        INT NOT NULL,                    -- PK del registro dueño (el EntityType lo da la definición)
    ValueText       NVARCHAR(MAX) NULL,
    ValueNumber     DECIMAL(18,4) NULL,
    ValueDate       DATETIME2 NULL,
    ValueBool       BIT NULL,
    UpdatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedBy       INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT UQ_CustomFieldValue UNIQUE (CustomFieldDefinitionId, EntityId)
);
CREATE INDEX IX_CustomFieldValue_Entity ON dbo.CustomFieldValue(CustomFieldDefinitionId, EntityId);
GO

-- Definición de informe personalizado por tenant + entidad base
CREATE TABLE dbo.ReportDefinition (
    ReportDefinitionId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    BaseEntityTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType'
    Name            NVARCHAR(150) NOT NULL,
    DescriptionJson NVARCHAR(MAX) NULL,
    VisibilityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='ReportVisibility'
    OwnerUserId     INT NULL REFERENCES dbo.AspNetUsers(Id),
    IsSystem        BIT NOT NULL DEFAULT 0,          -- informe por default del tenant: nadie lo edita ni lo elimina
    SecondaryJson   NVARCHAR(MAX) NULL,              -- fuentes secundarias combinadas (muchos-a-uno), JSON array
    ColumnsJson     NVARCHAR(MAX) NOT NULL,          -- columnas: campos nativos + custom fields (FieldKey)
    FilterJson      NVARCHAR(MAX) NULL,              -- DSL de filtros (mismo evaluador que la validación de campos)
    SortJson        NVARCHAR(MAX) NULL,
    GroupJson       NVARCHAR(MAX) NULL,              -- agrupaciones/agregados
    ChartTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='ReportChartType'
    ScheduleCron    NVARCHAR(60) NULL,               -- opcional: corrida programada
    DeliveryEmails  NVARCHAR(400) NULL,
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc    DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion      ROWVERSION,
    CONSTRAINT UQ_ReportDefinition UNIQUE (TenantId, BaseEntityTypeLookupId, Name)
);
CREATE INDEX IX_ReportDefinition_Entity ON dbo.ReportDefinition(TenantId, BaseEntityTypeLookupId) WHERE IsActive = 1;
GO

-- Compartir informe con roles/usuarios (cuando Visibility = SHARED)
CREATE TABLE dbo.ReportShare (
    ReportShareId   INT IDENTITY(1,1) PRIMARY KEY,
    ReportDefinitionId INT NOT NULL REFERENCES dbo.ReportDefinition(ReportDefinitionId),
    RoleId          INT NULL REFERENCES dbo.Role(RoleId),
    UserId          INT NULL REFERENCES dbo.AspNetUsers(Id),
    CanEdit         BIT NOT NULL DEFAULT 0
);
GO

-- Módulo H — Indicadores personalizados (un solo valor agregado sobre una fuente de datos)
CREATE TABLE dbo.IndicatorDefinition (
    IndicatorDefinitionId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Name            NVARCHAR(150) NOT NULL,
    DescriptionJson NVARCHAR(MAX) NULL,
    DataSourceKey   NVARCHAR(60) NOT NULL,             -- fuente de datos (registro de fuentes)
    FieldKey        NVARCHAR(80) NULL,                 -- campo agregado (NULL para COUNT)
    AggregateFnLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='AggregateFn'
    FilterJson      NVARCHAR(MAX) NULL,
    BusinessModuleLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),-- Entity='BusinessModule'
    IsMoney         BIT NOT NULL DEFAULT 0,
    IsSystem        BIT NOT NULL DEFAULT 0,
    OwnerUserId     INT NULL REFERENCES dbo.AspNetUsers(Id),
    VisibilityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='ReportVisibility'
    DateRangeModeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='DateRangeMode'
    DateFrom        DATE NULL, DateTo DATE NULL,
    ShowInPulse     BIT NOT NULL DEFAULT 0,
    SortOrder       INT NOT NULL DEFAULT 100,
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc    DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion      ROWVERSION,
    CONSTRAINT UQ_IndicatorDefinition UNIQUE (TenantId, Name)
);
CREATE INDEX IX_IndicatorDefinition_Tenant ON dbo.IndicatorDefinition(TenantId) WHERE IsActive = 1;
GO
CREATE TABLE dbo.IndicatorShare (
    IndicatorShareId INT IDENTITY(1,1) PRIMARY KEY,
    IndicatorDefinitionId INT NOT NULL REFERENCES dbo.IndicatorDefinition(IndicatorDefinitionId),
    RoleId INT NULL REFERENCES dbo.Role(RoleId),
    UserId INT NULL REFERENCES dbo.AspNetUsers(Id)
);
GO

-- Módulo I — Gráficos personalizados (un campo de agrupación + un agregado; BAR/DONUT/LINE)
CREATE TABLE dbo.ChartDefinition (
    ChartDefinitionId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Name            NVARCHAR(150) NOT NULL,
    DescriptionJson NVARCHAR(MAX) NULL,
    DataSourceKey   NVARCHAR(60) NOT NULL,
    GroupByField    NVARCHAR(80) NOT NULL,
    FieldKey        NVARCHAR(80) NULL,
    AggregateFnLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    ChartTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='ReportChartType' (BAR/DONUT/LINE)
    FilterJson      NVARCHAR(MAX) NULL,
    BusinessModuleLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    IsMoney         BIT NOT NULL DEFAULT 0,
    IsSystem        BIT NOT NULL DEFAULT 0,
    OwnerUserId     INT NULL REFERENCES dbo.AspNetUsers(Id),
    VisibilityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    DateRangeModeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    DateFrom        DATE NULL, DateTo DATE NULL,
    ShowInPulse     BIT NOT NULL DEFAULT 0,
    SortOrder       INT NOT NULL DEFAULT 100,
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc    DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion      ROWVERSION,
    SeedKey         NVARCHAR(40) NULL,                                        -- Lote 15: clave de siembra (ChartSeedKeys)
    CONSTRAINT UQ_ChartDefinition UNIQUE (TenantId, Name)
);
CREATE INDEX IX_ChartDefinition_Tenant ON dbo.ChartDefinition(TenantId) WHERE IsActive = 1;
GO

-- Lote 15 (D9, D14): SeedKey identifica los gráficos de la compañía que siembra la plataforma (INVENTORY_VALUE,
-- MOVEMENTS_BY_TYPE) sin depender del nombre, y deja constancia de la siembra: la fila sigue aunque se borre (IsActive = 0),
-- así que un gráfico renombrado o borrado no vuelve a crearse. Única por compañía cuando no es NULL. Guardado (COL_LENGTH /
-- sys.indexes) para una base ya creada; el índice en su propio lote (la columna ya existe al compilarlo).
IF COL_LENGTH('dbo.ChartDefinition', 'SeedKey') IS NULL
    ALTER TABLE dbo.ChartDefinition ADD SeedKey NVARCHAR(40) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ChartDefinition_SeedKey' AND object_id = OBJECT_ID('dbo.ChartDefinition'))
    CREATE UNIQUE INDEX UQ_ChartDefinition_SeedKey ON dbo.ChartDefinition(TenantId, SeedKey) WHERE SeedKey IS NOT NULL;
GO
CREATE TABLE dbo.ChartShare (
    ChartShareId INT IDENTITY(1,1) PRIMARY KEY,
    ChartDefinitionId INT NOT NULL REFERENCES dbo.ChartDefinition(ChartDefinitionId),
    RoleId INT NULL REFERENCES dbo.Role(RoleId),
    UserId INT NULL REFERENCES dbo.AspNetUsers(Id)
);
GO

-- Preferencias de visualización por usuario: Pulso del día y rango de fecha son POR USUARIO; la definición es el default
CREATE TABLE dbo.UserAnalyticsPreference (
    UserAnalyticsPreferenceId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    UserId       INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    IndicatorDefinitionId INT NULL REFERENCES dbo.IndicatorDefinition(IndicatorDefinitionId),
    ChartDefinitionId     INT NULL REFERENCES dbo.ChartDefinition(ChartDefinitionId),
    ShowInPulse  BIT NULL,
    DateRangeModeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    DateFrom     DATE NULL, DateTo DATE NULL,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    PulseSortOrder INT NULL,                                                  -- Lote F8a: orden propio en el Pulso (NULL = el de la compañía)
    CONSTRAINT CK_UserAnalyticsPreference_Target CHECK (
        (IndicatorDefinitionId IS NOT NULL AND ChartDefinitionId IS NULL) OR
        (IndicatorDefinitionId IS NULL AND ChartDefinitionId IS NOT NULL))
);
CREATE UNIQUE INDEX UQ_UserAnalyticsPreference_Indicator ON dbo.UserAnalyticsPreference(UserId, IndicatorDefinitionId) WHERE IndicatorDefinitionId IS NOT NULL;
CREATE UNIQUE INDEX UQ_UserAnalyticsPreference_Chart ON dbo.UserAnalyticsPreference(UserId, ChartDefinitionId) WHERE ChartDefinitionId IS NOT NULL;
GO

-- Lote F8a — orden y visibilidad de los paneles del Pulso del día (registro PulsePanels en código: INDICATORS, CHARTS,
-- WAREHOUSE, ACTIVITY). UserId NULL = nivel compañía (pulse.organize_company); con UserId = el Pulso personal. Sin fila =
-- visible, en el orden por defecto del registro. Auditada (EntityType PULSE_PANEL_SETTING).
CREATE TABLE dbo.PulsePanelSetting (
    PulsePanelSettingId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),          -- NULL = nivel compañía
    PanelKey     NVARCHAR(40) NOT NULL,
    IsVisible    BIT NOT NULL DEFAULT 1,
    SortOrder    INT NOT NULL DEFAULT 0,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UQ_PulsePanelSetting_Company ON dbo.PulsePanelSetting(TenantId, PanelKey) WHERE UserId IS NULL;
CREATE UNIQUE INDEX UQ_PulsePanelSetting_User    ON dbo.PulsePanelSetting(TenantId, UserId, PanelKey) WHERE UserId IS NOT NULL;
GO

/* =========================================================================
   CAPA 5 — CLIENTES Y CONTRATOS  +  CAPA 6 — TARIFAS
   ========================================================================= */
CREATE TABLE dbo.Client (
    ClientId     INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(30) NOT NULL,
    Name         NVARCHAR(200) NOT NULL,
    LegalName    NVARCHAR(250) NULL,
    TaxId        NVARCHAR(50) NULL,
    CreditLimit  DECIMAL(18,4) NULL,
    PaymentTermLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='PaymentTerm'
    CurrencyLookupId    INT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='Currency'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),     -- Entity='ClientStatus'
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion   ROWVERSION,
    -- Lote 2: numeración por cliente (0 = la asigna Teikem; NULL = patrón por defecto del sistema)
    ClientAssignsOrderNumber   BIT NOT NULL DEFAULT 0,
    ClientAssignsInvoiceNumber BIT NOT NULL DEFAULT 0,
    OrderNumberFormat   NVARCHAR(40) NULL,
    InvoiceNumberFormat NVARCHAR(40) NULL,
    PackageNumberFormat NVARCHAR(40) NULL,
    -- Lote 2: punto de recogido por defecto (Location tipo PICKUP/BOTH del cliente; NULL = en la dirección corporativa).
    -- La FK se agrega después de crear dbo.Location (FK_Client_DefaultPickupLocation).
    DefaultPickupLocationId INT NULL,
    CONSTRAINT UQ_Client_Tenant_Code UNIQUE (TenantId, Code),
    CONSTRAINT CK_Client_CreditLimit CHECK (CreditLimit IS NULL OR CreditLimit >= 0),  -- Lote 2
    CONSTRAINT UQ_Client_IdTenant UNIQUE (ClientId, TenantId)                          -- Lote 6: destino de FK_Product_Client y FK_Asn_Client
);
GO

CREATE TABLE dbo.ClientContact (
    ClientContactId INT IDENTITY(1,1) PRIMARY KEY,
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    FullName     NVARCHAR(150) NOT NULL,
    Role         NVARCHAR(80) NULL,
    IsPrimary    BIT NOT NULL DEFAULT 0,
    IsActive     BIT NOT NULL DEFAULT 1
);
-- Lote 2: un contacto principal activo por cliente
CREATE UNIQUE INDEX UX_ClientContact_Primary ON dbo.ClientContact(ClientId) WHERE IsPrimary = 1 AND IsActive = 1;
GO

-- Lote 3: contador atómico de numeración (ORDER/INVOICE/PACKAGE por cliente, PACKBATCH por tenant con ClientId NULL); se consume
-- con UPDATE … OUTPUT dentro de la transacción del alta (bloqueo de fila hasta el commit: sin huecos); la fila se asegura antes
-- en autocommit. UNIQUE de SQL Server admite un solo NULL en ClientId por (TenantId, Kind): exactamente lo que necesita PACKBATCH.
CREATE TABLE dbo.NumberSequence (
    NumberSequenceId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Kind         VARCHAR(20) NOT NULL,
    ClientId     INT NULL REFERENCES dbo.Client(ClientId),
    NextValue    BIGINT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_NumberSequence UNIQUE (TenantId, Kind, ClientId),
    CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN')),  -- Lote 27: RENTAL (REN-#####) y RENTALRETURN (DRN-#####) por tenant (ClientId NULL). Lote 4: WORKORDER = OT-##### por tenant (ClientId NULL). Lote 5: TRIP = número de ruta AAAA-#### por tenant (ClientId NULL). Lote 6: RECEIPT (REC-#####), CYCLECOUNT (CC-#####), CROSSDOCK (XD-#####) y PURCHASE (PO-#####) por tenant (ClientId NULL); la recolección reutiliza PACKBATCH
    CONSTRAINT CK_NumberSequence_Next CHECK (NextValue >= 1)
);
GO

-- Lote 2: tipos de servicio especial del tenant (compartidos por todos sus clientes; tabla propia y no LookupCode
-- porque UQ_LookupCode(Entity, InternalCode) es global) y tarifa efectivo-fechada por cliente (componente 5 del
-- modelo de facturación: editar = cerrar la fila y abrir otra; quitar = cerrar).
CREATE TABLE dbo.SpecialServiceType (
    SpecialServiceTypeId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Name         NVARCHAR(120) NOT NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SpecialServiceType UNIQUE (TenantId, Name),
    CONSTRAINT UQ_SpecialServiceType_IdTenant UNIQUE (SpecialServiceTypeId, TenantId)  -- Lote 4: destino de FKs compuestas (tarifas por viaje y DriverTrip)
);
GO

CREATE TABLE dbo.SpecialService (
    SpecialServiceId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    SpecialServiceTypeId INT NOT NULL REFERENCES dbo.SpecialServiceType(SpecialServiceTypeId),
    Rate         DECIMAL(18,4) NOT NULL,
    EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    EffectiveTo  DATE NULL,                                                  -- exclusivo; NULL = abierta
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_SpecialService_Rate CHECK (Rate >= 0),
    CONSTRAINT CK_SpecialService_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
-- Una sola fila abierta por cliente y tipo
CREATE UNIQUE INDEX UQ_SpecialService_Open ON dbo.SpecialService(ClientId, SpecialServiceTypeId) WHERE EffectiveTo IS NULL AND IsActive = 1;
CREATE INDEX IX_SpecialService_Client ON dbo.SpecialService(TenantId, ClientId);
GO

CREATE TABLE dbo.RateZone (
    RateZoneId   INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(30) NOT NULL,
    Name         NVARCHAR(120) NOT NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_RateZone UNIQUE (TenantId, Code)
);
GO

CREATE TABLE dbo.RateZoneMember (
    RateZoneMemberId INT IDENTITY(1,1) PRIMARY KEY,
    RateZoneId   INT NOT NULL REFERENCES dbo.RateZone(RateZoneId),
    MatchTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ZoneMatchType'
    PostalFrom   NVARCHAR(20) NULL,
    PostalTo     NVARCHAR(20) NULL,
    Municipality NVARCHAR(100) NULL,
    Polygon      GEOGRAPHY NULL
);
CREATE INDEX IX_RateZoneMember_Zone ON dbo.RateZoneMember(RateZoneId);
GO

CREATE TABLE dbo.Location (
    LocationId   INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NULL REFERENCES dbo.Client(ClientId),
    Code         NVARCHAR(40) NULL,
    Name         NVARCHAR(200) NOT NULL,
    LocationTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='LocationType'
    Line1        NVARCHAR(200) NOT NULL,
    Line2        NVARCHAR(200) NULL,
    City         NVARCHAR(100) NOT NULL,
    State        NVARCHAR(100) NULL,
    PostalCode   NVARCHAR(20) NULL,
    CountryLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='Country'
    GeoPoint     GEOGRAPHY NULL,
    RateZoneId   INT NULL REFERENCES dbo.RateZone(RateZoneId),
    DefaultServiceMinutes INT NOT NULL DEFAULT 0,
    DefaultWindowStart TIME NULL,
    DefaultWindowEnd   TIME NULL,
    AccessNotes  NVARCHAR(500) NULL,
    DeliveryNotes NVARCHAR(500) NULL,
    AllowDupInvoice BIT NOT NULL DEFAULT 0,      -- Lote 2: el consignatario acepta facturas repetidas (lo consume Órdenes, R36)
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion   ROWVERSION
);
CREATE INDEX IX_Location_Tenant_Client ON dbo.Location(TenantId, ClientId) WHERE IsActive = 1;
-- Lote 2: FK diferida del punto de recogido por defecto del cliente (Client se crea antes que Location)
ALTER TABLE dbo.Client ADD CONSTRAINT FK_Client_DefaultPickupLocation
    FOREIGN KEY (DefaultPickupLocationId) REFERENCES dbo.Location(LocationId);
GO

CREATE TABLE dbo.Contract (
    ContractId   INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    ContractNumber NVARCHAR(40) NOT NULL,
    Title        NVARCHAR(200) NOT NULL,
    StartDate    DATE NOT NULL,
    EndDate      DATE NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='ContractStatus'
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    BillingModelLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='BillingModel'
    CodCommissionPct DECIMAL(5,2) NULL,                                     -- comisión COD por defecto (remesa, módulo 11B)
    AutoRenew    BIT NOT NULL DEFAULT 0,                                    -- informativo: no hay renovación automática
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    RowVersion   ROWVERSION,
    -- Lote 2: modelo de facturación (5 componentes; "por servicio" encendido por defecto), cargo por despacho (por orden)
    -- y cargo por COD (FIXED = monto fijo por orden | PERCENT = por ciento del monto COD cobrado; "ninguno" = BillCodFee = 0).
    BillPerService      BIT NOT NULL DEFAULT 1,
    BillExtraPiece      BIT NOT NULL DEFAULT 0,
    BillDispatchFee     BIT NOT NULL DEFAULT 0,
    BillCodFee          BIT NOT NULL DEFAULT 0,
    BillSpecialServices BIT NOT NULL DEFAULT 0,
    DispatchFee         DECIMAL(18,4) NULL,
    CodFeeTypeLookupId  INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='PricingType' (FIXED|PERCENT)
    CodFeeValue         DECIMAL(18,4) NULL,
    CONSTRAINT UQ_Contract_Number UNIQUE (TenantId, ContractNumber),
    CONSTRAINT CK_Contract_Fees CHECK ((DispatchFee IS NULL OR DispatchFee >= 0) AND (CodFeeValue IS NULL OR CodFeeValue >= 0)),  -- Lote 2
    CONSTRAINT CK_Contract_Dates CHECK (EndDate IS NULL OR EndDate >= StartDate)                                                  -- Lote 2
);
GO

CREATE TABLE dbo.ContractServiceLevel (
    ServiceLevelId INT IDENTITY(1,1) PRIMARY KEY,
    ContractId   INT NOT NULL REFERENCES dbo.Contract(ContractId),
    ServiceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ServiceType'
    MaxTransitHours INT NULL,
    PickupWindowMin INT NULL,
    OnTimeTargetPct DECIMAL(5,2) NULL,
    PenaltyAmount DECIMAL(18,4) NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
-- Lote 2: un nivel de servicio activo por tipo de servicio y contrato
CREATE UNIQUE INDEX UX_ContractServiceLevel ON dbo.ContractServiceLevel(ContractId, ServiceTypeLookupId) WHERE IsActive = 1;
GO

CREATE TABLE dbo.ContractDocument (
    ContractDocumentId INT IDENTITY(1,1) PRIMARY KEY,
    ContractId   INT NOT NULL REFERENCES dbo.Contract(ContractId),
    DocTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='DocType'
    FileName     NVARCHAR(255) NOT NULL,
    ContentType  NVARCHAR(120) NOT NULL,
    StoragePath  NVARCHAR(500) NOT NULL,
    SizeBytes    BIGINT NOT NULL,
    UploadedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.RateCard (
    RateCardId   INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ContractId   INT NULL REFERENCES dbo.Contract(ContractId),
    Name         NVARCHAR(150) NOT NULL,
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    EffectiveFrom DATE NOT NULL,
    EffectiveTo  DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

-- Tarifas escalonadas (reemplazan el RateMatrix plano)
-- Lote 2: el componente cuelga directo del contrato (ContractId; NULL = tarifa genérica del tenant, con
-- ServiceType/PackageType NULL = comodín) con TenantId propio y clave servicio+paquete; RateCardId pasa a NULL.
-- Historial efectivo-fechado por fila: editar = cerrar (EffectiveTo) y abrir otra; solo una fila abierta por clave.
CREATE TABLE dbo.RateComponent (
    RateComponentId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),                  -- Lote 2
    RateCardId   INT NULL REFERENCES dbo.RateCard(RateCardId),                  -- Lote 2: NULL (RateCard no se usa todavía)
    ContractId   INT NULL REFERENCES dbo.Contract(ContractId),                  -- Lote 2: NULL = tarifa genérica del tenant
    ComponentTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='RateComponentType' (BASE_FREIGHT | EXTRA_PIECE ...)
    BasisLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),         -- Entity='RateBasis'
    FromZoneId   INT NULL REFERENCES dbo.RateZone(RateZoneId),
    ToZoneId     INT NULL REFERENCES dbo.RateZone(RateZoneId),
    ServiceTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='ServiceType'
    PackageTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Lote 2: Entity='PackageType'
    PricingModeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='PricingMode'
    TierModeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),          -- Entity='TierMode'
    FlatAmount   DECIMAL(18,4) NULL,
    UnitAmount   DECIMAL(18,4) NULL,
    MinCharge    DECIMAL(18,4) NULL,
    EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),        -- Lote 2
    EffectiveTo  DATE NULL,                                                     -- Lote 2: exclusivo; NULL = abierta
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_RateComponent_Amounts CHECK ((FlatAmount IS NULL OR FlatAmount >= 0) AND (UnitAmount IS NULL OR UnitAmount >= 0) AND (MinCharge IS NULL OR MinCharge >= 0)),  -- Lote 2
    CONSTRAINT CK_RateComponent_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)                                                                    -- Lote 2
);
-- Lote 2: UNIQUE(ContractId, ServiceTypeId, PackageTypeId) de la bitácora, acotado a filas vigentes (SQL Server trata NULL
-- como valor igual en el índice, lo que también acota la fila genérica comodín)
CREATE UNIQUE INDEX UQ_RateComponent_Open ON dbo.RateComponent(TenantId, ContractId, ComponentTypeLookupId, ServiceTypeLookupId, PackageTypeLookupId)
    WHERE EffectiveTo IS NULL AND IsActive = 1;
CREATE INDEX IX_RateComponent_Contract ON dbo.RateComponent(ContractId) WHERE ContractId IS NOT NULL;
GO

CREATE TABLE dbo.RateTier (
    RateTierId   INT IDENTITY(1,1) PRIMARY KEY,
    RateComponentId INT NOT NULL REFERENCES dbo.RateComponent(RateComponentId),
    MinValue     DECIMAL(14,3) NOT NULL,
    MaxValue     DECIMAL(14,3) NULL,
    UnitAmount   DECIMAL(18,4) NULL,
    FlatAmount   DECIMAL(18,4) NULL,
    SortOrder    INT NOT NULL DEFAULT 0,
    EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),        -- Lote 2
    EffectiveTo  DATE NULL,                                                     -- Lote 2: exclusivo; NULL = abierto
    CONSTRAINT CK_RateTier_Range CHECK (MaxValue IS NULL OR MaxValue >= MinValue),                                             -- Lote 2
    CONSTRAINT CK_RateTier_Amounts CHECK ((UnitAmount IS NULL OR UnitAmount >= 0) AND (FlatAmount IS NULL OR FlatAmount >= 0)), -- Lote 2
    CONSTRAINT CK_RateTier_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)                                    -- Lote 2
);
-- Lote 2: el no-traslape de tramos abiertos por servicio+paquete lo valida el servicio (RateTierRules; SQL Server no tiene EXCLUDE)
GO

CREATE TABLE dbo.RateRule (    -- recargos / modificadores
    RateRuleId   INT IDENTITY(1,1) PRIMARY KEY,
    RateCardId   INT NOT NULL REFERENCES dbo.RateCard(RateCardId),
    SurchargeTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='SurchargeType'
    PricingTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='PricingType'
    MinWeightKg DECIMAL(12,3) NULL, MaxWeightKg DECIMAL(12,3) NULL,
    MinVolumeM3 DECIMAL(12,4) NULL, MaxVolumeM3 DECIMAL(12,4) NULL,
    Amount       DECIMAL(18,4) NOT NULL DEFAULT 0,
    Priority     INT NOT NULL DEFAULT 100,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

/* =========================================================================
   CAPA 7 — ALMACENES (ESTRUCTURA FÍSICA)
   ========================================================================= */
-- Lote 16 (recibo directo a posición): ReceivingModeLookupId = modo de recepción (LookupCode 'ReceivingMode': PUTAWAY |
-- DIRECT; NULL = PUTAWAY, el seed rellena los existentes) y DefaultReceivingBinId = posición de recepción por defecto (D12;
-- zona STAGING o CROSSDOCK del mismo almacén: FK compuesta FK_Warehouse_DefaultReceivingBin, creada tras WarehouseBin).
-- Guardado (IF OBJECT_ID / COL_LENGTH) para agregar las columnas a una base ya creada sin tocar sus datos.
IF OBJECT_ID('dbo.Warehouse') IS NULL
BEGIN
CREATE TABLE dbo.Warehouse (
    WarehouseId  INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(30) NOT NULL,
    Name         NVARCHAR(150) NOT NULL,
    Line1        NVARCHAR(200) NULL, City NVARCHAR(100) NULL, State NVARCHAR(100) NULL,
    PostalCode   NVARCHAR(20) NULL,
    CountryLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='Country'
    GeoPoint     GEOGRAPHY NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='WarehouseStatus'
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    ReceivingModeLookupId INT NULL,                                          -- Lote 16: Entity='ReceivingMode' (NULL = PUTAWAY)
    DefaultReceivingBinId INT NULL,                                          -- Lote 16 (D12): posición de recepción por defecto
    CONSTRAINT UQ_Warehouse_Code UNIQUE (TenantId, Code),
    CONSTRAINT UQ_Warehouse_IdTenant UNIQUE (WarehouseId, TenantId)          -- Lote 6: destino de las FKs compuestas (Id, TenantId)
);
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.Warehouse', 'ReceivingModeLookupId') IS NULL
        ALTER TABLE dbo.Warehouse ADD ReceivingModeLookupId INT NULL;
    IF COL_LENGTH('dbo.Warehouse', 'DefaultReceivingBinId') IS NULL
        ALTER TABLE dbo.Warehouse ADD DefaultReceivingBinId INT NULL;
END
GO

-- Lote 16: la FK del modo de recepción en su propio lote (la columna ya existe al compilarlo); solo si todavía no está.
IF OBJECT_ID('dbo.FK_Warehouse_ReceivingMode', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.Warehouse ADD CONSTRAINT FK_Warehouse_ReceivingMode
        FOREIGN KEY (ReceivingModeLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
END
GO

-- Lote 8A: FK diferida del almacén por defecto del aparato (capa 2); compuesta con TenantId (mismo tenant garantizado).
-- Lote 16: guardada (el bloque de Warehouse ya lo está).
IF OBJECT_ID('dbo.FK_UserDevice_DefaultWarehouse', 'F') IS NULL
BEGIN
ALTER TABLE dbo.UserDevice ADD CONSTRAINT FK_UserDevice_DefaultWarehouse
    FOREIGN KEY (DefaultWarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId);
END
GO

CREATE TABLE dbo.WarehouseZone (
    WarehouseZoneId INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Code         NVARCHAR(30) NOT NULL, Name NVARCHAR(120) NOT NULL,
    ZoneTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='ZoneType'
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_WarehouseZone UNIQUE (WarehouseId, Code),
    CONSTRAINT UQ_WarehouseZone_IdWh UNIQUE (WarehouseZoneId, WarehouseId)   -- Lote 6: destino de FK_WarehouseBin_Zone y FK_CdPlan_StagingZone
);
GO

-- Lote 6 (D18): la posición lleva WarehouseId (FK compuesta con su zona) y su código es único por almacén. Sin TenantId:
-- se alcanza SOLO a través de su almacén filtrado.
-- Lote 1 de cambios de Almacén: MaxCapacityQty = cupo máximo de la posición en unidades de producto (NULL = sin configurar).
-- La capacidad de una zona NO se guarda: se calcula sumando el cupo de sus posiciones activas. Guardado (IF OBJECT_ID /
-- COL_LENGTH) para agregar la columna y su CHECK a una base ya creada sin tocar sus datos.
IF OBJECT_ID('dbo.WarehouseBin') IS NULL
BEGIN
    CREATE TABLE dbo.WarehouseBin (
        WarehouseBinId INT IDENTITY(1,1) PRIMARY KEY,
        WarehouseZoneId INT NOT NULL,
        WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),        -- Lote 6
        Code         NVARCHAR(40) NOT NULL,
        Aisle NVARCHAR(20) NULL, Rack NVARCHAR(20) NULL, Level NVARCHAR(20) NULL, Position NVARCHAR(20) NULL,
        MaxWeightKg  DECIMAL(12,3) NULL, IsActive BIT NOT NULL DEFAULT 1,
        MaxCapacityQty INT NULL,                                                -- Lote 1 de cambios de Almacén
        IsProvisional BIT NOT NULL DEFAULT 0,                                   -- Lote 21: creada desde un conteo, pendiente de revisión
        ProvisionalCreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),           -- Lote 21: quién la creó
        ProvisionalCreatedAtUtc DATETIME2 NULL,                                 -- Lote 21: cuándo
        ProvisionalCycleCountId INT NULL,                                       -- Lote 21: conteo de origen (FK abajo: CycleCount se crea después)
        CONSTRAINT UQ_WarehouseBin UNIQUE (WarehouseZoneId, Code),
        CONSTRAINT FK_WarehouseBin_Zone FOREIGN KEY (WarehouseZoneId, WarehouseId) REFERENCES dbo.WarehouseZone(WarehouseZoneId, WarehouseId),  -- Lote 6
        CONSTRAINT UQ_WarehouseBin_IdWh UNIQUE (WarehouseBinId, WarehouseId),   -- Lote 6: destino de las FKs (Posición, Almacén)
        CONSTRAINT UQ_WarehouseBin_WhCode UNIQUE (WarehouseId, Code),           -- Lote 6: código único por almacén
        CONSTRAINT CK_WarehouseBin_MaxWeight CHECK (MaxWeightKg IS NULL OR MaxWeightKg > 0),  -- Lote 6
        CONSTRAINT CK_WarehouseBin_MaxCapacityQty CHECK (MaxCapacityQty IS NULL OR MaxCapacityQty > 0),  -- Lote 1 de cambios de Almacén
        CONSTRAINT CK_WarehouseBin_Provisional CHECK (IsProvisional = 0 OR ProvisionalCreatedAtUtc IS NOT NULL)  -- Lote 21
    );
END
ELSE IF COL_LENGTH('dbo.WarehouseBin', 'MaxCapacityQty') IS NULL
BEGIN
    ALTER TABLE dbo.WarehouseBin ADD MaxCapacityQty INT NULL;
END
GO

-- Lote 21: posición provisional (columnas nuevas en una base ya creada; el CHECK, el índice y la FK van en lotes propios).
IF COL_LENGTH('dbo.WarehouseBin', 'IsProvisional') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD IsProvisional BIT NOT NULL CONSTRAINT DF_WarehouseBin_IsProvisional DEFAULT 0;
IF COL_LENGTH('dbo.WarehouseBin', 'ProvisionalCreatedBy') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD ProvisionalCreatedBy INT NULL REFERENCES dbo.AspNetUsers(Id);
IF COL_LENGTH('dbo.WarehouseBin', 'ProvisionalCreatedAtUtc') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD ProvisionalCreatedAtUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.WarehouseBin', 'ProvisionalCycleCountId') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD ProvisionalCycleCountId INT NULL;
GO


-- Lote 21: CHECK de la marca provisional (en su propio lote: las columnas ya existen al compilarlo) e índice de las pendientes.
IF OBJECT_ID('dbo.CK_WarehouseBin_Provisional', 'C') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD CONSTRAINT CK_WarehouseBin_Provisional CHECK (IsProvisional = 0 OR ProvisionalCreatedAtUtc IS NOT NULL);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WarehouseBin_Provisional' AND object_id = OBJECT_ID('dbo.WarehouseBin'))
    CREATE INDEX IX_WarehouseBin_Provisional ON dbo.WarehouseBin(WarehouseId) WHERE IsProvisional = 1;
GO

-- El CHECK del cupo en su propio lote (la columna ya existe al compilarlo); solo si todavía no está.
IF OBJECT_ID('dbo.CK_WarehouseBin_MaxCapacityQty', 'C') IS NULL
BEGIN
    ALTER TABLE dbo.WarehouseBin ADD CONSTRAINT CK_WarehouseBin_MaxCapacityQty CHECK (MaxCapacityQty IS NULL OR MaxCapacityQty > 0);
END
GO

-- Lote 16 (D12): la posición de recepción por defecto es del mismo almacén (FK compuesta contra UQ_WarehouseBin_IdWh); en su
-- propio lote, tras WarehouseBin, y solo si todavía no está.
IF OBJECT_ID('dbo.FK_Warehouse_DefaultReceivingBin', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.Warehouse ADD CONSTRAINT FK_Warehouse_DefaultReceivingBin
        FOREIGN KEY (DefaultReceivingBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId);
END
GO

CREATE TABLE dbo.WarehouseDock (
    WarehouseDockId INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Code         NVARCHAR(30) NOT NULL,
    DockTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='DockType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='DockStatus'
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_WarehouseDock UNIQUE (WarehouseId, Code),
    CONSTRAINT UQ_WarehouseDock_IdWh UNIQUE (WarehouseDockId, WarehouseId)   -- Lote 6: destino de FK_Receipt_Dock y FK_DockAppt_Dock
);
GO

/* =========================================================================
   CAPA 8 — INVENTARIO  (antes de órdenes: CargoLine referencia Product)
   ========================================================================= */
CREATE TABLE dbo.ProductCategory (
    ProductCategoryId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ParentId     INT NULL,                                                  -- Lote 6: FK compuesta (ParentId, TenantId) abajo
    Name         NVARCHAR(150) NOT NULL, IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_ProductCategory_IdTenant UNIQUE (ProductCategoryId, TenantId)  -- Lote 6
);
-- Lote 6: la categoría padre es del mismo tenant; nombre único entre activas por nivel.
ALTER TABLE dbo.ProductCategory ADD CONSTRAINT FK_ProductCategory_Parent FOREIGN KEY (ParentId, TenantId) REFERENCES dbo.ProductCategory(ProductCategoryId, TenantId);
CREATE UNIQUE INDEX UX_ProductCategory_Name ON dbo.ProductCategory(TenantId, ParentId, Name) WHERE IsActive = 1;
GO

-- Lote 6: ClientId = DUEÑO DEL INVENTARIO (cliente 3PL de dbo.Client; NULL = propio del tenant), distinto de TenantId (el
-- dueño de los datos). FKs compuestas (Id, TenantId) contra Client, ProductCategory y Warehouse; la posición preferida con
-- (Posición, Almacén). Seguimiento, UoM y dueño son inmutables tras el primer movimiento (regla de servicio, D25).
-- Lote 12 (Lote 2 del plan de cambios): Brand y Model = marca y modelo en texto libre (recortados; vacío = NULL). Guardado
-- (IF OBJECT_ID / COL_LENGTH) para agregar las columnas a una base ya creada sin tocar sus datos.
IF OBJECT_ID('dbo.Product') IS NULL
BEGIN
CREATE TABLE dbo.Product (
    ProductId    INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NULL,
    Sku          NVARCHAR(60) NOT NULL, Name NVARCHAR(200) NOT NULL,
    ProductCategoryId INT NULL,
    BaseUomLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='UnitOfMeasure'
    TrackingTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='TrackingType'
    WeightKg DECIMAL(12,3) NULL, VolumeM3 DECIMAL(12,4) NULL, Barcode NVARCHAR(60) NULL,
    PurchaseCost DECIMAL(18,4) NULL,   -- default para la línea de PO; costo de compra al proveedor
    SalePrice    DECIMAL(18,4) NULL,   -- precio de venta al cliente final (alimenta InvoiceLine ChargeType='PRODUCT_SALE')
    PreferredWarehouseId INT NULL,     -- almacén por default al recibir
    PreferredBinId INT NULL,           -- posición por default al recibir; sugerencia de putaway, NO es la ubicación real (esa vive en StockBalance)
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    MinQty       DECIMAL(16,3) NULL,   -- Lote 6 (D23): mínimo del disponible total ('Inventario bajo mínimo')
    MinPickQty   DECIMAL(16,3) NULL,   -- Lote 6 (D23): reabasto de la posición preferida en PICKING
    MaxPickQty   DECIMAL(16,3) NULL,   -- Lote 6 (D23)
    Brand        NVARCHAR(100) NULL,   -- Lote 12: marca (texto libre)
    Model        NVARCHAR(100) NULL,   -- Lote 12: modelo (texto libre)
    CONSTRAINT UQ_Product_Sku UNIQUE (TenantId, ClientId, Sku),
    CONSTRAINT UQ_Product_IdTenant UNIQUE (ProductId, TenantId),                                                              -- Lote 6
    CONSTRAINT FK_Product_Client FOREIGN KEY (ClientId, TenantId) REFERENCES dbo.Client(ClientId, TenantId),                  -- Lote 6
    CONSTRAINT FK_Product_Category FOREIGN KEY (ProductCategoryId, TenantId) REFERENCES dbo.ProductCategory(ProductCategoryId, TenantId),  -- Lote 6
    CONSTRAINT FK_Product_PrefWarehouse FOREIGN KEY (PreferredWarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),     -- Lote 6
    CONSTRAINT FK_Product_PrefBin FOREIGN KEY (PreferredBinId, PreferredWarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),  -- Lote 6
    CONSTRAINT CK_Product_Numbers CHECK ((PurchaseCost IS NULL OR PurchaseCost >= 0) AND (SalePrice IS NULL OR SalePrice >= 0)
        AND (WeightKg IS NULL OR WeightKg >= 0) AND (VolumeM3 IS NULL OR VolumeM3 >= 0) AND (MinQty IS NULL OR MinQty >= 0)
        AND (MinPickQty IS NULL OR MinPickQty >= 0) AND (MaxPickQty IS NULL OR MaxPickQty >= 0)
        AND (MinPickQty IS NULL OR MaxPickQty IS NULL OR MaxPickQty >= MinPickQty)),                                          -- Lote 6
    CONSTRAINT CK_Product_PrefBin CHECK (PreferredBinId IS NULL OR PreferredWarehouseId IS NOT NULL)                          -- Lote 6
);
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.Product', 'Brand') IS NULL
        ALTER TABLE dbo.Product ADD Brand NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.Product', 'Model') IS NULL
        ALTER TABLE dbo.Product ADD Model NVARCHAR(100) NULL;
END
GO

-- Lote 6: código de barras único entre los productos activos del tenant (en su propio lote, solo si todavía no está).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Product_Barcode' AND object_id = OBJECT_ID('dbo.Product'))
    CREATE UNIQUE INDEX UX_Product_Barcode ON dbo.Product(TenantId, Barcode) WHERE Barcode IS NOT NULL AND IsActive = 1;
GO

-- Lote 6: sin TenantId propio; hereda la tenencia de Product por FK (se alcanza SOLO a través del producto filtrado).
CREATE TABLE dbo.InventoryLot (
    LotId        INT IDENTITY(1,1) PRIMARY KEY,
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotNumber    NVARCHAR(60) NOT NULL, ManufactureDate DATE NULL, ExpiryDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Lot UNIQUE (ProductId, LotNumber),
    CONSTRAINT UQ_Lot_IdProduct UNIQUE (LotId, ProductId),                  -- Lote 6: destino de las FKs (Lote, Producto)
    CONSTRAINT CK_Lot_Dates CHECK (ManufactureDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= ManufactureDate)  -- Lote 6
);
GO

-- Lote 6: sin TenantId propio; hereda la tenencia de Product por FK. La ubicación actual (D17) y el estatus los escribe SOLO
-- el ledger (InventoryLedger); su rastro es EntityStatusHistory (INVENTORY_SERIAL) más InventoryTransaction.
CREATE TABLE dbo.InventorySerial (
    SerialId     INT IDENTITY(1,1) PRIMARY KEY,
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL,
    SerialNumber NVARCHAR(80) NOT NULL,
    StatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),  -- Entity='SerialStatus'
    CurrentWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),   -- Lote 6 (D17)
    CurrentBinId INT NULL,                                                -- Lote 6 (D17)
    CONSTRAINT UQ_Serial UNIQUE (ProductId, SerialNumber),
    CONSTRAINT UQ_Serial_IdProduct UNIQUE (SerialId, ProductId),                                                           -- Lote 6
    CONSTRAINT FK_Serial_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),                -- Lote 6
    CONSTRAINT FK_Serial_CurrentBin FOREIGN KEY (CurrentBinId, CurrentWarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),  -- Lote 6
    CONSTRAINT CK_Serial_Location CHECK (CurrentBinId IS NULL OR CurrentWarehouseId IS NOT NULL)                          -- Lote 6
);
CREATE INDEX IX_Serial_CurrentBin ON dbo.InventorySerial(CurrentBinId) WHERE CurrentBinId IS NOT NULL;   -- Lote 6
GO

-- Lote 6 (D2): proyección BLOQUEADA del ledger; solo la escribe InventoryLedger. CK_StockBalance_Qty es la última línea
-- contra saldos negativos o con más reservado que en mano (el ledger traduce la violación 547 a 409 insufficient_stock).
-- QtyAvailable es computada: la aplicación nunca la usa en lógica (InventoryRules.Available).
CREATE TABLE dbo.StockBalance (
    StockBalanceId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ProductId    INT NOT NULL,
    WarehouseId  INT NOT NULL,
    WarehouseBinId INT NULL,
    LotId        INT NULL,
    QtyOnHand    DECIMAL(16,3) NOT NULL DEFAULT 0,
    QtyReserved  DECIMAL(16,3) NOT NULL DEFAULT 0,
    QtyAvailable AS (QtyOnHand - QtyReserved) PERSISTED,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion ROWVERSION,
    CONSTRAINT UQ_StockBalance UNIQUE (ProductId, WarehouseId, WarehouseBinId, LotId),
    CONSTRAINT FK_StockBalance_Product FOREIGN KEY (ProductId, TenantId) REFERENCES dbo.Product(ProductId, TenantId),              -- Lote 6
    CONSTRAINT FK_StockBalance_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),      -- Lote 6
    CONSTRAINT FK_StockBalance_Bin FOREIGN KEY (WarehouseBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),  -- Lote 6
    CONSTRAINT FK_StockBalance_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),                   -- Lote 6
    CONSTRAINT CK_StockBalance_Qty CHECK (QtyOnHand >= 0 AND QtyReserved >= 0 AND QtyReserved <= QtyOnHand)                        -- Lote 6
);
CREATE INDEX IX_StockBalance_WH ON dbo.StockBalance(WarehouseId, ProductId);
CREATE INDEX IX_StockBalance_Bin ON dbo.StockBalance(WarehouseBinId);   -- Lote 6
GO

-- Lote 6 (D2, D3): ledger de SOLO INSERCIÓN (una reversa es un movimiento nuevo; Ref y motivo van en el INSERT).
-- Quantity con signo (maestro L331): + entra a To; − sale de From. TRANSFER + con From y To.
-- RECEIPT +, ISSUE y CROSSDOCK −, ADJUSTMENT ± (motivo obligatorio del catálogo AdjustmentReason).
CREATE TABLE dbo.InventoryTransaction (
    InventoryTransactionId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TxnTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='InventoryTxnType'
    ProductId    INT NOT NULL,
    LotId        INT NULL,
    SerialId     INT NULL,
    FromWarehouseId INT NULL,
    FromBinId    INT NULL,
    ToWarehouseId INT NULL,
    ToBinId      INT NULL,
    Quantity     DECIMAL(16,3) NOT NULL,
    RefEntityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='EntityType'
    RefId        INT NULL,
    Notes        NVARCHAR(300) NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    ReasonLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Lote 6: Entity='AdjustmentReason' (D7)
    CONSTRAINT FK_InvTxn_Product FOREIGN KEY (ProductId, TenantId) REFERENCES dbo.Product(ProductId, TenantId),                  -- Lote 6
    CONSTRAINT FK_InvTxn_FromWh FOREIGN KEY (FromWarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),          -- Lote 6
    CONSTRAINT FK_InvTxn_ToWh FOREIGN KEY (ToWarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),              -- Lote 6
    CONSTRAINT FK_InvTxn_FromBin FOREIGN KEY (FromBinId, FromWarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),  -- Lote 6
    CONSTRAINT FK_InvTxn_ToBin FOREIGN KEY (ToBinId, ToWarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),        -- Lote 6
    CONSTRAINT FK_InvTxn_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),                       -- Lote 6
    CONSTRAINT FK_InvTxn_Serial FOREIGN KEY (SerialId, ProductId) REFERENCES dbo.InventorySerial(SerialId, ProductId),           -- Lote 6
    CONSTRAINT CK_InvTxn_Quantity CHECK (Quantity <> 0),                                                                          -- Lote 6
    CONSTRAINT CK_InvTxn_Direction CHECK (((Quantity > 0 AND ToWarehouseId IS NOT NULL) OR (Quantity < 0 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NULL))
        AND (FromBinId IS NULL OR FromWarehouseId IS NOT NULL) AND (ToBinId IS NULL OR ToWarehouseId IS NOT NULL))                -- Lote 6
);
CREATE INDEX IX_InvTxn_Product ON dbo.InventoryTransaction(ProductId, CreatedAtUtc);
CREATE INDEX IX_InvTxn_Ref ON dbo.InventoryTransaction(RefEntityLookupId, RefId);
CREATE INDEX IX_InvTxn_Tenant_Date ON dbo.InventoryTransaction(TenantId, CreatedAtUtc);            -- Lote 6
CREATE INDEX IX_InvTxn_Lot ON dbo.InventoryTransaction(LotId) WHERE LotId IS NOT NULL;            -- Lote 6
CREATE INDEX IX_InvTxn_Serial ON dbo.InventoryTransaction(SerialId) WHERE SerialId IS NOT NULL;   -- Lote 6
GO

/* =========================================================================
   CAPA 10 — FLOTA Y CHOFERES (definición completa)
   ========================================================================= */
CREATE TABLE dbo.Vehicle (
    VehicleId    INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),               -- Lote 4: id de exposición externa (rutas /vehicles/{publicId})
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(30) NOT NULL, PlateNumber NVARCHAR(20) NULL,
    MaxWeightKg DECIMAL(12,3) NULL, MaxVolumeM3 DECIMAL(12,4) NULL, MaxStops INT NULL,
    VehicleTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='VehicleType'
    OwnershipLookupId   INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='Ownership'
    FuelTypeLookupId    INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='FuelType'
    Make NVARCHAR(60) NULL, Model NVARCHAR(60) NULL, ModelYear INT NULL, Vin NVARCHAR(40) NULL,
    CurrentOdometerKm DECIMAL(12,1) NULL,
    HomeWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='VehicleStatus' (Lote 4: NOT NULL, nace en la etapa inicial)
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),              -- Lote 4: auditoría inherente
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_Vehicle_Code UNIQUE (TenantId, Code),                     -- código irrepetible por compañía (no se libera tras la baja)
    CONSTRAINT UQ_Vehicle_IdTenant UNIQUE (VehicleId, TenantId),            -- Lote 4: destino de las FKs compuestas
    CONSTRAINT CK_Vehicle_Numbers CHECK ((MaxWeightKg IS NULL OR MaxWeightKg >= 0) AND (MaxVolumeM3 IS NULL OR MaxVolumeM3 >= 0)
        AND (MaxStops IS NULL OR MaxStops >= 1) AND (CurrentOdometerKm IS NULL OR CurrentOdometerKm >= 0)
        AND (ModelYear IS NULL OR ModelYear BETWEEN 1900 AND 2100))       -- Lote 4
);
GO

CREATE TABLE dbo.Driver (
    DriverId     INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),               -- Lote 4: id de exposición externa (rutas /drivers/{publicId})
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    FullName     NVARCHAR(150) NOT NULL,
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),                  -- usuario INTERNAL vinculado (único por compañía, UX_Driver_User)
    EmployeeCode NVARCHAR(30) NOT NULL,                                     -- Lote 4: es el 'Código' del chofer (obligatorio, inmutable)
    HireDate     DATE NULL,
    HomeWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='DriverStatus' (Lote 4: NOT NULL)
    MaxStopsPerRoute INT NULL,   -- override del default del tenant; NULL = usa Tenant.MaxStopsPerRouteDefault
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),              -- Lote 4: auditoría inherente
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_Driver_EmployeeCode UNIQUE (TenantId, EmployeeCode),      -- Lote 4: código irrepetible por compañía (no se libera tras la baja)
    CONSTRAINT UQ_Driver_IdTenant UNIQUE (DriverId, TenantId),              -- Lote 4: destino de las FKs compuestas
    CONSTRAINT CK_Driver_MaxStops CHECK (MaxStopsPerRoute IS NULL OR MaxStopsPerRoute >= 1)  -- Lote 4
);
-- Lote 4: un usuario se vincula a un solo chofer por compañía
CREATE UNIQUE INDEX UX_Driver_User ON dbo.Driver(TenantId, UserId) WHERE UserId IS NOT NULL;
GO

-- Lote 4: pago a choferes (maestro del módulo 11A; el chofer es el agregado raíz, sin tabla DriverRateAgreement).
-- Política del tenant (una fila por compañía, creada con el primer cambio; sin fila rigen 2 niveles y DELIVERY_PLUS_ATTEMPTS)
-- y tres tablas de tarifa efectivo-fechadas (editar = cerrar y abrir; EffectiveTo exclusivo) con FK compuesta al chofer.
CREATE TABLE dbo.DriverPayPolicy (
    TenantId     INT NOT NULL PRIMARY KEY REFERENCES dbo.Tenant(TenantId),
    AttemptLevels INT NOT NULL DEFAULT 2,                                   -- niveles de intento contiguos 1..N
    PayoutFormulaLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='DriverPayoutFormula'
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT CK_DriverPayPolicy_Levels CHECK (AttemptLevels BETWEEN 1 AND 20)
);
GO

CREATE TABLE dbo.DriverDeliveryRate (
    DriverDeliveryRateId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NOT NULL,
    ServiceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ServiceType'
    PackageTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='PackageType'
    Rate         DECIMAL(18,4) NOT NULL,
    EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    EffectiveTo  DATE NULL,                                                  -- exclusivo; NULL = abierta
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT FK_DriverDeliveryRate_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId),
    CONSTRAINT CK_DriverDeliveryRate_Rate CHECK (Rate >= 0),
    CONSTRAINT CK_DriverDeliveryRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE UNIQUE INDEX UQ_DriverDeliveryRate_Open ON dbo.DriverDeliveryRate(DriverId, ServiceTypeLookupId, PackageTypeLookupId) WHERE EffectiveTo IS NULL AND IsActive = 1;
CREATE INDEX IX_DriverDeliveryRate_Driver ON dbo.DriverDeliveryRate(TenantId, DriverId);
GO

CREATE TABLE dbo.DriverAttemptRate (
    DriverAttemptRateId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NOT NULL,
    AttemptNumber INT NOT NULL,                                              -- nivel 1..DriverPayPolicy.AttemptLevels
    Rate         DECIMAL(18,4) NOT NULL,
    EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    EffectiveTo  DATE NULL,                                                  -- exclusivo; NULL = abierta
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT FK_DriverAttemptRate_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId),
    CONSTRAINT CK_DriverAttemptRate_Number CHECK (AttemptNumber >= 1),
    CONSTRAINT CK_DriverAttemptRate_Rate CHECK (Rate >= 0),
    CONSTRAINT CK_DriverAttemptRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE UNIQUE INDEX UQ_DriverAttemptRate_Open ON dbo.DriverAttemptRate(DriverId, AttemptNumber) WHERE EffectiveTo IS NULL AND IsActive = 1;
CREATE INDEX IX_DriverAttemptRate_Driver ON dbo.DriverAttemptRate(TenantId, DriverId);
GO

CREATE TABLE dbo.DriverTripRate (
    DriverTripRateId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NOT NULL,
    SpecialServiceTypeId INT NOT NULL,                                       -- tipo de viaje = catálogo de servicios especiales del tenant (R21)
    Rate         DECIMAL(18,4) NOT NULL,
    EffectiveFrom DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    EffectiveTo  DATE NULL,                                                  -- exclusivo; NULL = abierta
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT FK_DriverTripRate_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId),
    CONSTRAINT FK_DriverTripRate_Type FOREIGN KEY (SpecialServiceTypeId, TenantId) REFERENCES dbo.SpecialServiceType(SpecialServiceTypeId, TenantId),
    CONSTRAINT UQ_DriverTripRate_IdTenant UNIQUE (DriverTripRateId, TenantId), -- destino de FK_DriverTrip_Rate
    CONSTRAINT CK_DriverTripRate_Rate CHECK (Rate >= 0),
    CONSTRAINT CK_DriverTripRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE UNIQUE INDEX UQ_DriverTripRate_Open ON dbo.DriverTripRate(DriverId, SpecialServiceTypeId) WHERE EffectiveTo IS NULL AND IsActive = 1;
CREATE INDEX IX_DriverTripRate_Driver ON dbo.DriverTripRate(TenantId, DriverId);
GO

/* =========================================================================
   CAPA 11 — ÓRDENES DE TRANSPORTE
   ========================================================================= */
-- Lote 3 (ajuste D): importador de órdenes por plantilla de posición de columna (reutilizable; ClientId NULL = general del
-- tenant) y lote de importación en dos pasos (validar → confirmar). RowsJson guarda una entrada por fila: número de fila,
-- valores parseados, consignatario resuelto/por crear, errores por campo y, tras confirmar, orderPublicId o el error.
CREATE TABLE dbo.ImportTemplate (
    ImportTemplateId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Kind         NVARCHAR(20) NOT NULL DEFAULT 'ORDER',
    Name         NVARCHAR(120) NOT NULL,
    ClientId     INT NULL REFERENCES dbo.Client(ClientId),                    -- NULL = plantilla general del tenant
    Delimiter    NCHAR(1) NOT NULL DEFAULT ',',
    HasHeader    BIT NOT NULL DEFAULT 1,
    ColumnsJson  NVARCHAR(MAX) NOT NULL,                                       -- [{position:int, field:string}] (catálogo ImportFields)
    DefaultsJson NVARCHAR(MAX) NULL,                                           -- valores fijos por campo, p. ej. serviceType STANDARD
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc DATETIME2 NULL,
    CONSTRAINT UQ_ImportTemplate UNIQUE (TenantId, Kind, Name)
);
GO

CREATE TABLE dbo.ImportBatch (
    ImportBatchId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Kind         NVARCHAR(20) NOT NULL DEFAULT 'ORDER',
    ImportTemplateId INT NOT NULL REFERENCES dbo.ImportTemplate(ImportTemplateId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    FileName     NVARCHAR(260) NULL,
    [RowCount]   INT NOT NULL,                                               -- palabra reservada de T-SQL: entre corchetes
    ValidRows    INT NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='ImportBatchStatus' (VALIDATED → CONFIRMED; DISCARDED)
    RowsJson     NVARCHAR(MAX) NOT NULL,
    CreatedBy    INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ConfirmedAtUtc DATETIME2 NULL,
    ConfirmedBy  INT NULL REFERENCES dbo.AspNetUsers(Id)
);
CREATE INDEX IX_ImportBatch_Client ON dbo.ImportBatch(TenantId, ClientId);
GO

CREATE TABLE dbo.TransportOrder (
    TransportOrderId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    ContractId   INT NULL REFERENCES dbo.Contract(ContractId),
    OrderNumber  NVARCHAR(40) NOT NULL,
    ClientInvoiceNumber NVARCHAR(40) NOT NULL,  -- Lote 3: número de factura del cliente; lo asigna el cliente o Teikem si queda en blanco (siempre tiene valor, L240)
    ClientInvoiceNumberTyped BIT NOT NULL CONSTRAINT DF_Order_InvoiceTyped DEFAULT 0, -- Lote 3: 1 = la factura se tecleó al crear (R36 solo aplica a números tecleados, L1124)
    PackBatchNumber NVARCHAR(40) NOT NULL,      -- Lote 3: número de empaque, siempre con valor (EMP-##### fijo o id del lote de Recolección y empaque)
    ServiceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ServiceType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='OrderStatus'
    PriorityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),        -- Entity='OrderPriority'
    RequestedDate DATETIME2 NULL, PromisedDate DATETIME2 NULL,
    TotalWeightKg DECIMAL(14,3) NULL, TotalVolumeM3 DECIMAL(14,4) NULL, TotalPieces INT NULL,
    QuotedAmount DECIMAL(18,4) NULL,
    QuotedAtUtc  DATETIME2 NULL,                -- Lote 3: la cotización se congela al confirmar (QuotedAmount/QuotedAtUtc/ContractId)
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    -- COD (cobro contra entrega)
    CodTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),         -- Entity='CodType' (NONE/CASH/CHECK/COMPANY_CHECK); el Lote 3 nunca lo escribe: se define al entregar
    CodAmount    DECIMAL(18,4) NULL,
    CodCurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    CodStatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),         -- Entity='CodStatus' (NULL = sin COD)
    -- Lote 3: entrega especial (servicio del catálogo del cliente; sin paquetes ni COD) y origen de la orden (patrón RefEntity/RefId)
    IsSpecialDelivery BIT NOT NULL DEFAULT 0,
    SpecialServiceId INT NULL REFERENCES dbo.SpecialService(SpecialServiceId),
    SourceEntityTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='EntityType' (lo llena Recolección y empaque / IMPORT_BATCH)
    SourceEntityId INT NULL,
    ConfirmedAtUtc DATETIME2 NULL,              -- Lote 3
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion   ROWVERSION,
    -- Lote 3: UQ_Order_Number (TenantId, OrderNumber) se reemplaza por el índice único filtrado UX_Order_Number por cliente
    CONSTRAINT CK_Order_Amounts CHECK ((CodAmount IS NULL OR CodAmount >= 0) AND (QuotedAmount IS NULL OR QuotedAmount >= 0) AND (TotalPieces IS NULL OR TotalPieces >= 0)),  -- Lote 3
    CONSTRAINT CK_Order_Special CHECK (IsSpecialDelivery = 0 OR SpecialServiceId IS NOT NULL),                                                                           -- Lote 3
    CONSTRAINT UQ_TransportOrder_IdTenant UNIQUE (TransportOrderId, TenantId)                                                                                            -- Lote 4: destino de FK_DriverTrip_Order
);
-- Lote 3: el número de orden es el consecutivo interno del cliente (L237/L240): único por cliente, no por tenant; una orden
-- eliminada en captura (IsActive = 0) libera su número. PackBatchNumber es el identificador de escaneo único por tenant.
CREATE UNIQUE INDEX UX_Order_Number ON dbo.TransportOrder(TenantId, ClientId, OrderNumber) WHERE IsActive = 1;
CREATE UNIQUE INDEX UX_Order_PackBatch ON dbo.TransportOrder(TenantId, PackBatchNumber) WHERE IsActive = 1;
CREATE INDEX IX_Order_Invoice ON dbo.TransportOrder(TenantId, ClientInvoiceNumber);                        -- Lote 3: búsqueda y chequeo de factura repetida (R36)
CREATE INDEX IX_Order_Client ON dbo.TransportOrder(TenantId, ClientId, CreatedAtUtc) WHERE IsActive = 1;   -- Lote 3: listado por cliente y saldo pendiente de crédito
CREATE INDEX IX_Order_Tenant_Status ON dbo.TransportOrder(TenantId, StatusCodeId) WHERE IsActive = 1;
CREATE INDEX IX_Order_Cod ON dbo.TransportOrder(TenantId, CodStatusCodeId) WHERE CodStatusCodeId IS NOT NULL;
GO

CREATE TABLE dbo.OrderStop (
    OrderStopId  INT IDENTITY(1,1) PRIMARY KEY,
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    StopTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='StopType'
    Sequence     INT NOT NULL,
    LocationId   INT NULL REFERENCES dbo.Location(LocationId),
    SnapName     NVARCHAR(200) NULL,
    SnapLine1    NVARCHAR(200) NOT NULL, SnapLine2 NVARCHAR(200) NULL,
    SnapCity     NVARCHAR(100) NOT NULL, SnapState NVARCHAR(100) NULL,
    SnapPostalCode NVARCHAR(20) NULL, SnapCountryCode CHAR(2) NOT NULL DEFAULT 'PR',
    GeoPoint     GEOGRAPHY NULL,
    GeocodeAccuracyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='GeocodeAccuracy' (EXACT/ZIP_CENTROID/CITY_CENTROID/MANUAL); NULL hasta geocodificar
    WindowStartUtc DATETIME2 NULL, WindowEndUtc DATETIME2 NULL,
    ServiceMinutes INT NOT NULL DEFAULT 0,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='StopStatus'
    CompletedAtUtc DATETIME2 NULL,
    Notes        NVARCHAR(500) NULL
);
CREATE INDEX IX_OrderStop_Order ON dbo.OrderStop(TransportOrderId);
-- Lote 3: chequeo de factura repetida por consignatario (R36) y filtro por consignatario. Notes recibe el snapshot de
-- Location.DeliveryNotes (recortado); WindowStartUtc/WindowEndUtc reciben RequestedDate + ventana por defecto de la Location.
CREATE INDEX IX_OrderStop_Location ON dbo.OrderStop(LocationId) WHERE LocationId IS NOT NULL;
GO

CREATE TABLE dbo.CargoLine (
    CargoLineId  INT IDENTITY(1,1) PRIMARY KEY,
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    PickupStopId INT NULL REFERENCES dbo.OrderStop(OrderStopId),
    DeliveryStopId INT NULL REFERENCES dbo.OrderStop(OrderStopId),
    ProductId    INT NULL REFERENCES dbo.Product(ProductId),
    PackageTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Lote 3: Entity='PackageType' (L245; NULL solo en la línea de una entrega especial)
    PackageNumber NVARCHAR(40) NULL,                                          -- Lote 3: número de paquete por línea (PQT-##### del patrón del cliente si se deja en blanco)
    Description  NVARCHAR(250) NOT NULL,
    Quantity     DECIMAL(14,3) NOT NULL DEFAULT 1,
    UomLookupId  INT NULL REFERENCES dbo.LookupCode(LookupCodeId),            -- Entity='UnitOfMeasure'
    WeightKg DECIMAL(12,3) NULL, VolumeM3 DECIMAL(12,4) NULL,
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    HandlingFlags INT NOT NULL DEFAULT 0,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_CargoLine_Quantity CHECK (Quantity > 0)                     -- Lote 3
);
CREATE INDEX IX_CargoLine_Order ON dbo.CargoLine(TransportOrderId);          -- Lote 3
GO

CREATE TABLE dbo.OrderReference (
    OrderReferenceId INT IDENTITY(1,1) PRIMARY KEY,
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    RefTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='OrderRefType'
    RefValue     NVARCHAR(120) NOT NULL,
    Source       NVARCHAR(80) NULL
);
CREATE INDEX IX_OrderReference_Value ON dbo.OrderReference(RefValue);
GO

CREATE TABLE dbo.OrderDocument (
    OrderDocumentId INT IDENTITY(1,1) PRIMARY KEY,
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    DocTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='DocType'
    FileName     NVARCHAR(255) NOT NULL, ContentType NVARCHAR(120) NOT NULL,
    StoragePath  NVARCHAR(500) NOT NULL, SizeBytes BIGINT NOT NULL,
    UploadedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

/* =========================================================================
   CAPA 12 — TRIPS Y RUTAS
   ========================================================================= */
-- Lote 5: la ruta del día (Trip) + su versión vigente del plan (Route). FKs compuestas (Id, TenantId) hacia Vehicle, Driver y
-- DispatchZone (esta última con ALTER TABLE después del CREATE de DispatchZone, capa 12B): segunda barrera multi-tenant.
-- Número AAAA-#### (NumberSequence TRIP). UQ_Trip_IdTenant es el destino de las FKs compuestas de TripOrder y OptimizationRun.
CREATE TABLE dbo.Trip (
    TripId       INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(40) NOT NULL, PlanDate DATE NOT NULL,
    DispatchZoneId INT NULL,                                                  -- Lote 5: FK_Trip_DispatchZone (compuesta) en la capa 12B
    OriginWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    VehicleId    INT NULL,
    DriverId     INT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='TripStatus'
    PlannedStartUtc DATETIME2 NULL, PlannedEndUtc DATETIME2 NULL,
    ActualStartUtc DATETIME2 NULL, ActualEndUtc DATETIME2 NULL,
    TotalDistanceKm DECIMAL(12,3) NULL, TotalDurationMin INT NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                 -- Lote 5
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion ROWVERSION,
    CONSTRAINT UQ_Trip_Code UNIQUE (TenantId, Code),
    CONSTRAINT UQ_Trip_IdTenant UNIQUE (TripId, TenantId),                    -- Lote 5: destino de FKs compuestas
    CONSTRAINT FK_Trip_Vehicle FOREIGN KEY (VehicleId, TenantId) REFERENCES dbo.Vehicle(VehicleId, TenantId),
    CONSTRAINT FK_Trip_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId),
    CONSTRAINT CK_Trip_Numbers CHECK ((TotalDistanceKm IS NULL OR TotalDistanceKm >= 0) AND (TotalDurationMin IS NULL OR TotalDurationMin >= 0)
        AND (PlannedEndUtc IS NULL OR PlannedStartUtc IS NULL OR PlannedEndUtc >= PlannedStartUtc)
        AND (ActualEndUtc IS NULL OR ActualStartUtc IS NULL OR ActualEndUtc >= ActualStartUtc))
);
CREATE INDEX IX_Trip_Tenant_Date ON dbo.Trip(TenantId, PlanDate) WHERE IsActive = 1;
CREATE INDEX IX_Trip_Zone_Date ON dbo.Trip(TenantId, PlanDate, DispatchZoneId) WHERE IsActive = 1;                        -- Lote 5
CREATE INDEX IX_Trip_Driver_Date ON dbo.Trip(TenantId, DriverId, PlanDate) WHERE IsActive = 1 AND DriverId IS NOT NULL;   -- Lote 5
GO

-- Lote 5: una orden solo puede estar en UNA ruta vigente (UX_TripOrder_Current); IsCurrent pasa a 0 al completar la ruta.
-- Liberar una orden de una ruta abierta es un DELETE físico (L272). FKs compuestas: la orden y la ruta son del mismo tenant.
CREATE TABLE dbo.TripOrder (
    TripOrderId  INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),                -- Lote 5
    TripId       INT NOT NULL,
    TransportOrderId INT NOT NULL,
    SortHint     INT NULL,
    IsCurrent    BIT NOT NULL CONSTRAINT DF_TripOrder_IsCurrent DEFAULT 1,    -- Lote 5
    AssignedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                -- Lote 5
    AssignedBy   INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 5
    CONSTRAINT UQ_TripOrder UNIQUE (TripId, TransportOrderId),
    CONSTRAINT FK_TripOrder_Trip FOREIGN KEY (TripId, TenantId) REFERENCES dbo.Trip(TripId, TenantId),
    CONSTRAINT FK_TripOrder_Order FOREIGN KEY (TransportOrderId, TenantId) REFERENCES dbo.TransportOrder(TransportOrderId, TenantId)
);
CREATE UNIQUE INDEX UX_TripOrder_Current ON dbo.TripOrder(TransportOrderId) WHERE IsCurrent = 1;   -- Lote 5
CREATE INDEX IX_TripOrder_Trip ON dbo.TripOrder(TripId) WHERE IsCurrent = 1;                        -- Lote 5
GO

-- Versión del plan de la ruta. Lote 5: a lo sumo una vigente por Trip (UX_Route_Trip_Active) y versiones únicas por Trip.
-- StopCount y los totales los escribe la aplicación (RouteWriter.RecomputeAsync): sin columnas computadas ni triggers.
CREATE TABLE dbo.Route (
    RouteId      INT IDENTITY(1,1) PRIMARY KEY,
    TripId       INT NOT NULL REFERENCES dbo.Trip(TripId),
    Version      INT NOT NULL DEFAULT 1, IsActive BIT NOT NULL DEFAULT 1,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='RouteStatus'
    TotalDistanceKm DECIMAL(12,3) NULL, TotalDurationMin INT NULL, StopCount INT NOT NULL DEFAULT 0,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion ROWVERSION,
    CONSTRAINT UQ_Route_Version UNIQUE (TripId, Version),                    -- Lote 5
    CONSTRAINT CK_Route_Numbers CHECK (Version >= 1 AND StopCount >= 0 AND (TotalDistanceKm IS NULL OR TotalDistanceKm >= 0)
        AND (TotalDurationMin IS NULL OR TotalDurationMin >= 0))             -- Lote 5
);
CREATE UNIQUE INDEX UX_Route_Trip_Active ON dbo.Route(TripId) WHERE IsActive = 1;   -- Lote 5 (reemplaza IX_Route_Trip)
GO

-- Lote 5: sin UNIQUE sobre (RouteId, Sequence) a propósito: el reordenamiento reescribe la secuencia bajo el bloqueo del Trip.
CREATE TABLE dbo.RouteStop (
    RouteStopId  INT IDENTITY(1,1) PRIMARY KEY,
    RouteId      INT NOT NULL REFERENCES dbo.Route(RouteId),
    OrderStopId  INT NOT NULL REFERENCES dbo.OrderStop(OrderStopId),
    Sequence     INT NOT NULL,
    PlannedArrivalUtc DATETIME2 NULL, PlannedDepartureUtc DATETIME2 NULL,
    DistanceFromPrevKm DECIMAL(12,3) NULL, DurationFromPrevMin INT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='RouteStopStatus'
    ActualArrivalUtc DATETIME2 NULL, ActualDepartureUtc DATETIME2 NULL,
    CONSTRAINT UQ_RouteStop UNIQUE (RouteId, OrderStopId),
    CONSTRAINT CK_RouteStop_Numbers CHECK (Sequence >= 1 AND (DistanceFromPrevKm IS NULL OR DistanceFromPrevKm >= 0)
        AND (DurationFromPrevMin IS NULL OR DurationFromPrevMin >= 0))       -- Lote 5
);
CREATE INDEX IX_RouteStop_Route ON dbo.RouteStop(RouteId, Sequence);
GO

/* =========================================================================
   CAPA 12B — ZONAS DE DESPACHO Y ASIGNACIÓN ESTÁNDAR DE CHOFERES
   Territorio fijo (ej. "R-01") que un chofer cubre habitualmente. Sirve
   para reasignar en bloque ("todo lo de R-01 a Ana") y para filtrar en
   Despacho/Escaneo. Independiente de RateZone (esa es para tarifas).
   ========================================================================= */
CREATE TABLE dbo.DispatchZone (
    DispatchZoneId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(20) NOT NULL,          -- 'R-01'
    Name         NVARCHAR(120) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_DispatchZone UNIQUE (TenantId, Code),
    CONSTRAINT UQ_DispatchZone_IdTenant UNIQUE (DispatchZoneId, TenantId)    -- Lote 5: destino de FK_Trip_DispatchZone
);
GO

-- Lote 5: la zona de la ruta es del mismo tenant (FK compuesta; Trip se crea en la capa 12, antes que DispatchZone).
ALTER TABLE dbo.Trip ADD CONSTRAINT FK_Trip_DispatchZone FOREIGN KEY (DispatchZoneId, TenantId) REFERENCES dbo.DispatchZone(DispatchZoneId, TenantId);
GO

-- Lote 5: MatchValue se guarda normalizado (CP de 5 dígitos, rango 'AAAAA-BBBBB', municipio sin espacios de más); el
-- solapamiento entre zonas ACTIVAS lo impide el servicio (DispatchZoneService); el repetido en la misma zona, además, la UQ.
CREATE TABLE dbo.DispatchZoneMember (
    DispatchZoneMemberId INT IDENTITY(1,1) PRIMARY KEY,
    DispatchZoneId INT NOT NULL REFERENCES dbo.DispatchZone(DispatchZoneId),
    MatchTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- reutiliza Entity='ZoneMatchType'
    MatchValue   NVARCHAR(120) NOT NULL,                                    -- ej. '00949' o 'Toa Baja'
    CONSTRAINT UQ_DispatchZoneMember UNIQUE (DispatchZoneId, MatchTypeLookupId, MatchValue)   -- Lote 5
);
CREATE INDEX IX_DispatchZoneMember_Zone ON dbo.DispatchZoneMember(DispatchZoneId);
GO

-- Asignación estándar: qué chofer cubre qué zona(s) por defecto (no impide reasignar ese día)
CREATE TABLE dbo.DriverZone (
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    DispatchZoneId INT NOT NULL REFERENCES dbo.DispatchZone(DispatchZoneId),
    IsPrimary    BIT NOT NULL DEFAULT 1,
    PRIMARY KEY (DriverId, DispatchZoneId)
);
GO

-- Lote 5: OptimizationRunId pasa a INT (EntityStatusHistory.EntityId e IOwnedEntityResolver trabajan con int; nadie la
-- referenciaba). Bitácora de cada corrida (historial OPTIMIZATION_RUN; sin AuditLog). FK compuesta hacia la ruta del tenant.
CREATE TABLE dbo.OptimizationRun (
    OptimizationRunId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TripId       INT NULL,
    RouteId      INT NULL REFERENCES dbo.Route(RouteId),
    EngineLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='OptimizerEngine'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='OptimizationRunStatus'
    RequestJson NVARCHAR(MAX) NOT NULL, ResponseJson NVARCHAR(MAX) NULL, ErrorMessage NVARCHAR(MAX) NULL,
    TotalDistanceKm DECIMAL(12,3) NULL, TotalDurationMin INT NULL, UnassignedCount INT NULL,
    StartedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CompletedAtUtc DATETIME2 NULL,
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 5
    CONSTRAINT FK_OptimizationRun_Trip FOREIGN KEY (TripId, TenantId) REFERENCES dbo.Trip(TripId, TenantId),
    CONSTRAINT CK_OptimizationRun_Numbers CHECK ((UnassignedCount IS NULL OR UnassignedCount >= 0)
        AND (TotalDistanceKm IS NULL OR TotalDistanceKm >= 0) AND (TotalDurationMin IS NULL OR TotalDurationMin >= 0))
);
CREATE INDEX IX_OptimizationRun_Trip ON dbo.OptimizationRun(TenantId, TripId, StartedAtUtc);   -- Lote 5
GO

/* =========================================================================
   CAPA 13 — DETALLE DE FLOTA / MANTENIMIENTO
   ========================================================================= */
CREATE TABLE dbo.VehicleDocument (
    VehicleDocumentId INT IDENTITY(1,1) PRIMARY KEY,
    VehicleId    INT NOT NULL REFERENCES dbo.Vehicle(VehicleId),
    DocTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='VehicleDocType'
    DocNumber    NVARCHAR(80) NULL, IssuedDate DATE NULL, ExpiryDate DATE NULL,
    FileName NVARCHAR(255) NULL, StoragePath NVARCHAR(500) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_VehicleDocument_Dates CHECK (IssuedDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= IssuedDate)  -- Lote 4
);
CREATE INDEX IX_VehicleDocument_Expiry ON dbo.VehicleDocument(ExpiryDate) WHERE IsActive = 1;
CREATE INDEX IX_VehicleDocument_Vehicle ON dbo.VehicleDocument(VehicleId, DocTypeLookupId);   -- Lote 4: grupo de 'vigente por tipo'
GO

CREATE TABLE dbo.MaintenanceSchedule (
    MaintenanceScheduleId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    VehicleId    INT NULL REFERENCES dbo.Vehicle(VehicleId),
    VehicleTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Name         NVARCHAR(150) NOT NULL,
    TriggerLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='MaintenanceTrigger'
    IntervalKm DECIMAL(12,1) NULL, IntervalDays INT NULL,
    LastServiceKm DECIMAL(12,1) NULL, LastServiceDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),              -- Lote 4
    -- Lote 4: por vehículo o por tipo de vehículo, nunca ambos; intervalo coherente con el disparador
    CONSTRAINT CK_MaintSchedule_Target CHECK ((VehicleId IS NULL AND VehicleTypeLookupId IS NOT NULL) OR (VehicleId IS NOT NULL AND VehicleTypeLookupId IS NULL)),
    CONSTRAINT CK_MaintSchedule_Interval CHECK ((IntervalKm IS NULL OR IntervalKm > 0) AND (IntervalDays IS NULL OR IntervalDays > 0) AND (IntervalKm IS NOT NULL OR IntervalDays IS NOT NULL))
);
CREATE INDEX IX_MaintSchedule_Vehicle ON dbo.MaintenanceSchedule(VehicleId) WHERE VehicleId IS NOT NULL;  -- Lote 4
GO

CREATE TABLE dbo.MaintenanceWorkOrder (
    WorkOrderId  INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    VehicleId    INT NOT NULL,                                               -- Lote 4: FK compuesta FK_WorkOrder_Vehicle
    MaintenanceScheduleId INT NULL REFERENCES dbo.MaintenanceSchedule(MaintenanceScheduleId),
    Number       NVARCHAR(40) NOT NULL,
    MaintenanceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='MaintenanceType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='WorkOrderStatus'
    OdometerKm DECIMAL(12,1) NULL, ScheduledDate DATE NULL, CompletedDate DATE NULL,
    Vendor NVARCHAR(150) NULL,
    LaborCost DECIMAL(18,4) NULL, PartsCost DECIMAL(18,4) NULL,
    TotalCost AS (ISNULL(LaborCost,0) + ISNULL(PartsCost,0)) PERSISTED,
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Notes NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),              -- Lote 4: auditoría inherente
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT UQ_WorkOrder_Number UNIQUE (TenantId, Number),
    CONSTRAINT FK_WorkOrder_Vehicle FOREIGN KEY (VehicleId, TenantId) REFERENCES dbo.Vehicle(VehicleId, TenantId),  -- Lote 4
    CONSTRAINT CK_WorkOrder_Costs CHECK ((LaborCost IS NULL OR LaborCost >= 0) AND (PartsCost IS NULL OR PartsCost >= 0) AND (OdometerKm IS NULL OR OdometerKm >= 0))  -- Lote 4
);
CREATE INDEX IX_WorkOrder_Vehicle ON dbo.MaintenanceWorkOrder(VehicleId, StatusCodeId) WHERE IsActive = 1;                          -- Lote 4
CREATE INDEX IX_WorkOrder_Schedule ON dbo.MaintenanceWorkOrder(MaintenanceScheduleId) WHERE MaintenanceScheduleId IS NOT NULL;       -- Lote 4
GO

CREATE TABLE dbo.MaintenanceTask (
    MaintenanceTaskId INT IDENTITY(1,1) PRIMARY KEY,
    WorkOrderId  INT NOT NULL REFERENCES dbo.MaintenanceWorkOrder(WorkOrderId),
    Description  NVARCHAR(250) NOT NULL,
    PartCost DECIMAL(18,4) NULL, LaborCost DECIMAL(18,4) NULL,
    IsCompleted  BIT NOT NULL DEFAULT 0,
    IsActive     BIT NOT NULL DEFAULT 1,                                     -- Lote 4: quitar una tarea = IsActive 0 (nunca DELETE)
    CONSTRAINT CK_MaintTask_Costs CHECK ((PartCost IS NULL OR PartCost >= 0) AND (LaborCost IS NULL OR LaborCost >= 0))  -- Lote 4
);
CREATE INDEX IX_MaintenanceTask_WorkOrder ON dbo.MaintenanceTask(WorkOrderId);  -- Lote 4
GO

CREATE TABLE dbo.FuelLog (
    FuelLogId    INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    VehicleId    INT NOT NULL,                                               -- Lote 4: FK compuesta FK_FuelLog_Vehicle
    DriverId     INT NULL,                                                   -- Lote 4: FK compuesta FK_FuelLog_Driver (NULL no se evalúa)
    FillDateUtc  DATETIME2 NOT NULL, OdometerKm DECIMAL(12,1) NULL,
    Liters DECIMAL(10,3) NOT NULL, TotalCost DECIMAL(18,4) NOT NULL,
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Station NVARCHAR(150) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,                                     -- Lote 4: quitar una carga = IsActive 0 (deja de contar en km/L)
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),              -- Lote 4: auditoría inherente
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT FK_FuelLog_Vehicle FOREIGN KEY (VehicleId, TenantId) REFERENCES dbo.Vehicle(VehicleId, TenantId),  -- Lote 4
    CONSTRAINT FK_FuelLog_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId),      -- Lote 4
    CONSTRAINT CK_FuelLog_Amounts CHECK (Liters > 0 AND TotalCost >= 0 AND (OdometerKm IS NULL OR OdometerKm >= 0))  -- Lote 4
);
CREATE INDEX IX_FuelLog_Vehicle ON dbo.FuelLog(VehicleId, FillDateUtc);
GO

CREATE TABLE dbo.DriverLicense (
    DriverLicenseId INT IDENTITY(1,1) PRIMARY KEY,
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    LicenseClassLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='LicenseClass'
    LicenseNumber NVARCHAR(60) NOT NULL, IssuedDate DATE NULL, ExpiryDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_DriverLicense_Dates CHECK (IssuedDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= IssuedDate)  -- Lote 4
);
CREATE INDEX IX_DriverLicense_Expiry ON dbo.DriverLicense(ExpiryDate) WHERE IsActive = 1;
CREATE INDEX IX_DriverLicense_Driver ON dbo.DriverLicense(DriverId, LicenseClassLookupId);   -- Lote 4: grupo de 'vigente por tipo'
GO

CREATE TABLE dbo.DriverCertification (
    DriverCertificationId INT IDENTITY(1,1) PRIMARY KEY,
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    CertTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='CertificationType'
    CertNumber NVARCHAR(60) NULL, IssuedDate DATE NULL, ExpiryDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_DriverCertification_Dates CHECK (IssuedDate IS NULL OR ExpiryDate IS NULL OR ExpiryDate >= IssuedDate)  -- Lote 4
);
CREATE INDEX IX_DriverCertification_Expiry ON dbo.DriverCertification(ExpiryDate) WHERE IsActive = 1;          -- Lote 4: paridad con licencias y documentos
CREATE INDEX IX_DriverCertification_Driver ON dbo.DriverCertification(DriverId, CertTypeLookupId);           -- Lote 4: grupo de 'vigente por tipo'
GO

CREATE TABLE dbo.FleetAssignment (
    FleetAssignmentId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NULL REFERENCES dbo.Driver(DriverId),
    VehicleId    INT NULL REFERENCES dbo.Vehicle(VehicleId),
    AssignmentTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='AssignmentType'
    StartDate DATE NOT NULL, EndDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

-- Lote 4: viaje pagado al chofer (monto congelado; insumo de la liquidación del Lote 9). Es la fila de pago, distinta de
-- dbo.Trip (Despacho). Un solo viaje vigente por orden, garantizado en BD (UX_DriverTrip_Order; CANCELLED implica IsActive = 0).
CREATE TABLE dbo.DriverTrip (
    DriverTripId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NOT NULL,
    SpecialServiceTypeId INT NOT NULL,                                       -- tipo de viaje (catálogo de servicios especiales, R21)
    DriverTripRateId INT NULL,                                               -- tarifa usada; NULL = sin tarifa
    TransportOrderId INT NULL,                                               -- entrega especial de origen; NULL = alta manual
    TripDate     DATE NOT NULL,
    Amount       DECIMAL(18,4) NOT NULL,                                     -- congelado al crear
    RateMissing  BIT NOT NULL DEFAULT 0,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='DriverTripStatus'
    Notes        NVARCHAR(500) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,                                     -- CANCELLED implica 0
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL,
    UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion   ROWVERSION,
    CONSTRAINT FK_DriverTrip_Driver FOREIGN KEY (DriverId, TenantId) REFERENCES dbo.Driver(DriverId, TenantId),
    CONSTRAINT FK_DriverTrip_Type FOREIGN KEY (SpecialServiceTypeId, TenantId) REFERENCES dbo.SpecialServiceType(SpecialServiceTypeId, TenantId),
    CONSTRAINT FK_DriverTrip_Rate FOREIGN KEY (DriverTripRateId, TenantId) REFERENCES dbo.DriverTripRate(DriverTripRateId, TenantId),
    CONSTRAINT FK_DriverTrip_Order FOREIGN KEY (TransportOrderId, TenantId) REFERENCES dbo.TransportOrder(TransportOrderId, TenantId),
    CONSTRAINT CK_DriverTrip_Amount CHECK (Amount >= 0),
    CONSTRAINT CK_DriverTrip_Rate CHECK ((RateMissing = 1 AND DriverTripRateId IS NULL AND Amount = 0) OR (RateMissing = 0 AND DriverTripRateId IS NOT NULL))
);
CREATE UNIQUE INDEX UX_DriverTrip_Order ON dbo.DriverTrip(TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1;
CREATE INDEX IX_DriverTrip_Driver_Date ON dbo.DriverTrip(TenantId, DriverId, TripDate);
GO

/* =========================================================================
   CAPA 13B — COMPRAS (módulo PURCHASING)
   Cuando Advance compra inventario propio a un proveedor para revenderlo
   (ej. medidores de glucosa a la marca, luego a las farmacias) — distinto
   de un cliente que manda su cargo a guardarse. Product.ClientId NULL ya
   marca "producto propio de Advance"; esto añade el lado de la compra.
   El recibo contra una PO usa el mismo Asn/ReceiptHeader de siempre.
   ========================================================================= */
CREATE TABLE dbo.Supplier (
    SupplierId   INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Name         NVARCHAR(200) NOT NULL,
    ContactName  NVARCHAR(150) NULL, Phone NVARCHAR(40) NULL, Email NVARCHAR(150) NULL,
    PaymentTermLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- reutiliza Entity='PaymentTerm'
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_Supplier_IdTenant UNIQUE (SupplierId, TenantId)            -- Lote 6: destino de FK_PurchaseOrder_Supplier
);
CREATE UNIQUE INDEX UX_Supplier_Name ON dbo.Supplier(TenantId, Name) WHERE IsActive = 1;   -- Lote 6
GO

CREATE TABLE dbo.PurchaseOrder (
    PurchaseOrderId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    SupplierId   INT NOT NULL,
    WarehouseId  INT NOT NULL,                                               -- a qué almacén llega
    Number       NVARCHAR(40) NOT NULL,
    OrderDate    DATE NOT NULL, ExpectedDate DATE NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),       -- Entity='PurchaseOrderStatus'
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                -- Lote 6
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                    -- Lote 6
    CONSTRAINT UQ_PurchaseOrder_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_PurchaseOrder_IdTenant UNIQUE (PurchaseOrderId, TenantId),                                                     -- Lote 6
    CONSTRAINT FK_PurchaseOrder_Supplier FOREIGN KEY (SupplierId, TenantId) REFERENCES dbo.Supplier(SupplierId, TenantId),       -- Lote 6
    CONSTRAINT FK_PurchaseOrder_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId)    -- Lote 6
);
CREATE INDEX IX_PurchaseOrder_Status ON dbo.PurchaseOrder(TenantId, StatusCodeId);
GO

CREATE TABLE dbo.PurchaseOrderLine (
    PurchaseOrderLineId INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId INT NOT NULL REFERENCES dbo.PurchaseOrder(PurchaseOrderId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    QtyOrdered   DECIMAL(16,3) NOT NULL,
    QtyReceived  DECIMAL(16,3) NOT NULL DEFAULT 0,
    UnitCost     DECIMAL(18,4) NOT NULL,
    LineTotal    AS (QtyOrdered * UnitCost) PERSISTED,
    CONSTRAINT CK_POLine_Numbers CHECK (QtyOrdered > 0 AND QtyReceived >= 0 AND UnitCost >= 0),   -- Lote 6
    CONSTRAINT UQ_POLine_IdPo UNIQUE (PurchaseOrderLineId, PurchaseOrderId)                       -- Lote 6: destino de FK_PoShortage_Line
);
-- Líneas de una orden (detalle, recibo contra PO y faltantes de 'Ajustes de inventario').
CREATE INDEX IX_POLine_Po ON dbo.PurchaseOrderLine(PurchaseOrderId);
GO

-- Lote 6 (D8): resolución de faltantes por línea de PO ('Ajustes de inventario', bitácora L762). CLOSE y REORDER resuelven el
-- faltante completo; MANUAL_ADJUSTMENT admite una cantidad parcial con su movimiento del ledger.
CREATE TABLE dbo.PurchaseOrderShortageResolution (
    PurchaseOrderShortageResolutionId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    PurchaseOrderId INT NOT NULL,
    PurchaseOrderLineId INT NOT NULL,
    ActionLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='ShortageAction'
    Quantity     DECIMAL(16,3) NOT NULL,
    ReasonLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),         -- Entity='AdjustmentReason'
    Notes        NVARCHAR(300) NULL,
    ReorderPurchaseOrderId INT NULL,
    InventoryTransactionId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT FK_PoShortage_Po FOREIGN KEY (PurchaseOrderId, TenantId) REFERENCES dbo.PurchaseOrder(PurchaseOrderId, TenantId),
    CONSTRAINT FK_PoShortage_Line FOREIGN KEY (PurchaseOrderLineId, PurchaseOrderId) REFERENCES dbo.PurchaseOrderLine(PurchaseOrderLineId, PurchaseOrderId),
    CONSTRAINT FK_PoShortage_Reorder FOREIGN KEY (ReorderPurchaseOrderId, TenantId) REFERENCES dbo.PurchaseOrder(PurchaseOrderId, TenantId),
    CONSTRAINT CK_PoShortage_Qty CHECK (Quantity > 0)
);
CREATE INDEX IX_PoShortage_Line ON dbo.PurchaseOrderShortageResolution(PurchaseOrderLineId);
GO

/* =========================================================================
   CAPA 14 — WMS
   ========================================================================= */
CREATE TABLE dbo.Asn (
    AsnId        INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL,
    ClientId     INT NULL,                                                    -- cargo de un cliente (pasa por almacén, no es de Advance)
    PurchaseOrderId INT NULL,                                                 -- compra de Advance a un proveedor (inventario propio)
    Reference NVARCHAR(80) NULL, ExpectedDate DATE NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='AsnStatus'
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                 -- Lote 6
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    CONSTRAINT UQ_Asn_IdTenant UNIQUE (AsnId, TenantId),                                                                 -- Lote 6
    CONSTRAINT FK_Asn_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),     -- Lote 6
    CONSTRAINT FK_Asn_Client FOREIGN KEY (ClientId, TenantId) REFERENCES dbo.Client(ClientId, TenantId),                 -- Lote 6
    CONSTRAINT FK_Asn_Po FOREIGN KEY (PurchaseOrderId, TenantId) REFERENCES dbo.PurchaseOrder(PurchaseOrderId, TenantId), -- Lote 6
    CONSTRAINT CK_Asn_Origin CHECK (ClientId IS NULL OR PurchaseOrderId IS NULL)                                         -- Lote 6: de un cliente o de una PO, nunca ambos
);
-- ASN de una orden de compra (banderas de recibo y faltantes de 'Ajustes de inventario').
CREATE INDEX IX_Asn_Po ON dbo.Asn(PurchaseOrderId) WHERE PurchaseOrderId IS NOT NULL;
GO

CREATE TABLE dbo.AsnLine (
    AsnLineId    INT IDENTITY(1,1) PRIMARY KEY,
    AsnId        INT NOT NULL REFERENCES dbo.Asn(AsnId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    ExpectedQty  DECIMAL(16,3) NOT NULL, LotNumber NVARCHAR(60) NULL,
    PurchaseOrderLineId INT NULL REFERENCES dbo.PurchaseOrderLine(PurchaseOrderLineId),  -- Lote 6: línea de PO de origen
    CONSTRAINT CK_AsnLine_Qty CHECK (ExpectedQty > 0)                                     -- Lote 6
);
GO

-- Lote 6: ReceivedAtUtc = fecha de confirmación del recibo (insumo de Contabilización de compras, Lote 10). Un ASN tiene un
-- solo recibo activo (UX_Receipt_Asn, D6).
-- Lote 13 (Lote 3 del plan de cambios): DefaultStagingBinId = posición de recepción por defecto del encabezado (del mismo
-- almacén: FK compuesta contra UQ_WarehouseBin_IdWh); Carrier = transporte y Reference = referencia libre (recortados;
-- vacío = NULL). Guardado (IF OBJECT_ID / COL_LENGTH) para agregar las columnas a una base ya creada sin tocar sus datos.
IF OBJECT_ID('dbo.ReceiptHeader') IS NULL
BEGIN
CREATE TABLE dbo.ReceiptHeader (
    ReceiptHeaderId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL,
    AsnId        INT NULL,
    DockId       INT NULL,
    ReceiptTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ReceiptType'
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='ReceiptStatus'
    ReceivedAtUtc DATETIME2 NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                 -- Lote 6
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    ReceivedBy   INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    DefaultStagingBinId INT NULL,                                             -- Lote 13: posición de recepción por defecto
    Carrier      NVARCHAR(80) NULL,                                           -- Lote 13: transporte
    Reference    NVARCHAR(80) NULL,                                           -- Lote 13: referencia
    ReceivingModeLookupId INT NULL,                                           -- Lote 16: copia del modo (Entity='ReceivingMode'; NULL = PUTAWAY)
    CONSTRAINT UQ_Receipt_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_Receipt_IdTenant UNIQUE (ReceiptHeaderId, TenantId),                                                     -- Lote 6
    CONSTRAINT FK_Receipt_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),   -- Lote 6
    CONSTRAINT FK_Receipt_Asn FOREIGN KEY (AsnId, TenantId) REFERENCES dbo.Asn(AsnId, TenantId),                           -- Lote 6
    CONSTRAINT FK_Receipt_Dock FOREIGN KEY (DockId, WarehouseId) REFERENCES dbo.WarehouseDock(WarehouseDockId, WarehouseId), -- Lote 6
    CONSTRAINT FK_Receipt_StagingBin FOREIGN KEY (DefaultStagingBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId)  -- Lote 13
);
CREATE UNIQUE INDEX UX_Receipt_Asn ON dbo.ReceiptHeader(AsnId) WHERE AsnId IS NOT NULL AND IsActive = 1;   -- Lote 6 (D6)
CREATE INDEX IX_Receipt_Tenant_Status ON dbo.ReceiptHeader(TenantId, StatusCodeId);                         -- Lote 6
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.ReceiptHeader', 'DefaultStagingBinId') IS NULL
        ALTER TABLE dbo.ReceiptHeader ADD DefaultStagingBinId INT NULL;
    IF COL_LENGTH('dbo.ReceiptHeader', 'Carrier') IS NULL
        ALTER TABLE dbo.ReceiptHeader ADD Carrier NVARCHAR(80) NULL;
    IF COL_LENGTH('dbo.ReceiptHeader', 'Reference') IS NULL
        ALTER TABLE dbo.ReceiptHeader ADD Reference NVARCHAR(80) NULL;
    IF COL_LENGTH('dbo.ReceiptHeader', 'ReceivingModeLookupId') IS NULL          -- Lote 16
        ALTER TABLE dbo.ReceiptHeader ADD ReceivingModeLookupId INT NULL;
END
GO

-- Lote 13: la FK de la posición por defecto en su propio lote (la columna ya existe al compilarlo); solo si todavía no está.
IF OBJECT_ID('dbo.FK_Receipt_StagingBin', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.ReceiptHeader ADD CONSTRAINT FK_Receipt_StagingBin
        FOREIGN KEY (DefaultStagingBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId);
END
GO

-- Lote 16: la FK del modo de recepción del recibo en su propio lote; solo si todavía no está.
IF OBJECT_ID('dbo.FK_Receipt_ReceivingMode', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.ReceiptHeader ADD CONSTRAINT FK_Receipt_ReceivingMode
        FOREIGN KEY (ReceivingModeLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
END
GO

-- Lote 6: ReceivedQty arranca igual a ExpectedQty (R8); AdjustmentTxnId enlaza el ADJUSTMENT RECEIPT_VARIANCE (D4).
-- Lote 16: TargetBinId = posición destino de la línea (de guardado; el servicio garantiza que sea del almacén del recibo).
-- Guardado (IF OBJECT_ID / COL_LENGTH) para agregar la columna a una base ya creada sin tocar sus datos.
IF OBJECT_ID('dbo.ReceiptLine') IS NULL
BEGIN
CREATE TABLE dbo.ReceiptLine (
    ReceiptLineId INT IDENTITY(1,1) PRIMARY KEY,
    ReceiptHeaderId INT NOT NULL REFERENCES dbo.ReceiptHeader(ReceiptHeaderId),
    AsnLineId    INT NULL REFERENCES dbo.AsnLine(AsnLineId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL,
    SerialId     INT NULL,
    ReceivedQty  DECIMAL(16,3) NOT NULL,
    StagingBinId INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    ExpectedQty  DECIMAL(16,3) NULL,                                                              -- Lote 6
    SerialNumbersJson NVARCHAR(MAX) NULL,                                                         -- Lote 6
    AdjustmentTxnId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),      -- Lote 6
    TargetBinId  INT NULL,                                                                        -- Lote 16: posición destino
    CONSTRAINT FK_ReceiptLine_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),               -- Lote 6
    CONSTRAINT FK_ReceiptLine_Serial FOREIGN KEY (SerialId, ProductId) REFERENCES dbo.InventorySerial(SerialId, ProductId),   -- Lote 6
    CONSTRAINT CK_ReceiptLine_Qty CHECK (ReceivedQty >= 0 AND (ExpectedQty IS NULL OR ExpectedQty >= 0))                       -- Lote 6
);
CREATE INDEX IX_ReceiptLine_Header ON dbo.ReceiptLine(ReceiptHeaderId);   -- Lote 6
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.ReceiptLine', 'TargetBinId') IS NULL
        ALTER TABLE dbo.ReceiptLine ADD TargetBinId INT NULL;
END
GO

-- Lote 16: la FK de la posición destino en su propio lote; solo si todavía no está.
IF OBJECT_ID('dbo.FK_ReceiptLine_TargetBin', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.ReceiptLine ADD CONSTRAINT FK_ReceiptLine_TargetBin
        FOREIGN KEY (TargetBinId) REFERENCES dbo.WarehouseBin(WarehouseBinId);
END
GO

-- Lote 6 (D19): WarehouseTaskId pasa a INT (historial de estatus, resolvers y RefId son int).
CREATE TABLE dbo.WarehouseTask (
    WarehouseTaskId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL,
    TaskTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='WarehouseTaskType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='WarehouseTaskStatus'
    ProductId    INT NULL,
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    Quantity     DECIMAL(16,3) NULL,
    FromBinId    INT NULL,
    ToBinId      INT NULL,
    RefEntityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='EntityType'
    RefId        INT NULL,
    AssignedToUserId INT NULL REFERENCES dbo.AspNetUsers(Id),
    Priority     INT NOT NULL DEFAULT 100,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAtUtc DATETIME2 NULL,
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    CONSTRAINT FK_WhTask_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),       -- Lote 6
    CONSTRAINT FK_WhTask_Product FOREIGN KEY (ProductId, TenantId) REFERENCES dbo.Product(ProductId, TenantId),               -- Lote 6
    CONSTRAINT FK_WhTask_FromBin FOREIGN KEY (FromBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),  -- Lote 6
    CONSTRAINT FK_WhTask_ToBin FOREIGN KEY (ToBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),      -- Lote 6
    CONSTRAINT CK_WarehouseTask_Qty CHECK (Quantity IS NULL OR Quantity >= 0)                                                  -- Lote 6
);
CREATE INDEX IX_WarehouseTask_Queue ON dbo.WarehouseTask(WarehouseId, TaskTypeLookupId, StatusCodeId, Priority);
CREATE INDEX IX_WarehouseTask_Ref ON dbo.WarehouseTask(RefEntityLookupId, RefId);   -- Lote 6
GO

CREATE TABLE dbo.PickWave (
    PickWaveId   INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='PickWaveStatus'
    TripId       INT NULL REFERENCES dbo.Trip(TripId),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_PickWave_Number UNIQUE (TenantId, Number)
);
GO

CREATE TABLE dbo.PickTask (
    PickTaskId   INT IDENTITY(1,1) PRIMARY KEY,
    PickWaveId   INT NOT NULL REFERENCES dbo.PickWave(PickWaveId),
    CargoLineId  INT NULL REFERENCES dbo.CargoLine(CargoLineId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    FromBinId    INT NOT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    QtyToPick    DECIMAL(16,3) NOT NULL, QtyPicked DECIMAL(16,3) NOT NULL DEFAULT 0,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='PickTaskStatus'
    Sequence     INT NULL
);
GO

-- Lote 6 (D1): PickWave, PickTask, Carton y CartonLine quedan SIN cambios y sin mapear (las olas se difieren). Carton no
-- tiene TenantId: el vacío se resuelve cuando se construyan las olas.
CREATE TABLE dbo.Carton (
    CartonId     INT IDENTITY(1,1) PRIMARY KEY,
    PickWaveId   INT NULL REFERENCES dbo.PickWave(PickWaveId),
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    CartonTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='CartonType'
    Barcode NVARCHAR(60) NULL, WeightKg DECIMAL(12,3) NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId)         -- Entity='CartonStatus'
);
GO

CREATE TABLE dbo.CartonLine (
    CartonLineId INT IDENTITY(1,1) PRIMARY KEY,
    CartonId     INT NOT NULL REFERENCES dbo.Carton(CartonId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    Quantity     DECIMAL(16,3) NOT NULL
);
GO

-- Lote 14 (D2, D3): OriginLookupId = origen del conteo (CycleCountOrigin: MANUAL selección | CHANGES lo cambiado);
-- ChangesFromUtc/ChangesToUtc = ventana [desde, hasta) de movimientos de "lo cambiado" (la siguiente generación del almacén
-- arranca en el último ChangesToUtc: IX_CycleCount_Origin). Guardado (IF OBJECT_ID / COL_LENGTH) para agregar las columnas a
-- una base ya creada sin tocar sus datos.
IF OBJECT_ID('dbo.CycleCount') IS NULL
BEGIN
CREATE TABLE dbo.CycleCount (
    CycleCountId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL,
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='CycleCountStatus'
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    IsActive     BIT NOT NULL DEFAULT 1,                                      -- Lote 6
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    ReconciledAtUtc DATETIME2 NULL,                                           -- Lote 6
    ReconciledBy INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    RowVersion   ROWVERSION,                                                  -- Lote 6
    OriginLookupId INT NULL,                                                  -- Lote 14: Entity='CycleCountOrigin'
    ChangesFromUtc DATETIME2 NULL,                                            -- Lote 14: lo cambiado, desde (inclusivo)
    ChangesToUtc DATETIME2 NULL,                                              -- Lote 14: lo cambiado, hasta (exclusivo)
    CONSTRAINT UQ_CycleCount_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_CycleCount_IdTenant UNIQUE (CycleCountId, TenantId),                                                     -- Lote 6
    CONSTRAINT FK_CycleCount_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId), -- Lote 6
    CONSTRAINT FK_CycleCount_Origin FOREIGN KEY (OriginLookupId) REFERENCES dbo.LookupCode(LookupCodeId),                  -- Lote 14
    CONSTRAINT CK_CycleCount_Changes CHECK (ChangesFromUtc IS NULL OR ChangesToUtc IS NULL OR ChangesFromUtc <= ChangesToUtc) -- Lote 14
);
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.CycleCount', 'OriginLookupId') IS NULL
        ALTER TABLE dbo.CycleCount ADD OriginLookupId INT NULL;
    IF COL_LENGTH('dbo.CycleCount', 'ChangesFromUtc') IS NULL
        ALTER TABLE dbo.CycleCount ADD ChangesFromUtc DATETIME2 NULL;
    IF COL_LENGTH('dbo.CycleCount', 'ChangesToUtc') IS NULL
        ALTER TABLE dbo.CycleCount ADD ChangesToUtc DATETIME2 NULL;
END
GO

-- Lote 14: FK y CHECK de las columnas nuevas en su propio lote (las columnas ya existen al compilarlo); solo si faltan.
IF OBJECT_ID('dbo.FK_CycleCount_Origin', 'F') IS NULL
    ALTER TABLE dbo.CycleCount ADD CONSTRAINT FK_CycleCount_Origin FOREIGN KEY (OriginLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
IF OBJECT_ID('dbo.CK_CycleCount_Changes', 'C') IS NULL
    ALTER TABLE dbo.CycleCount ADD CONSTRAINT CK_CycleCount_Changes CHECK (ChangesFromUtc IS NULL OR ChangesToUtc IS NULL OR ChangesFromUtc <= ChangesToUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CycleCount_Origin' AND object_id = OBJECT_ID('dbo.CycleCount'))
    CREATE INDEX IX_CycleCount_Origin ON dbo.CycleCount(TenantId, WarehouseId, OriginLookupId, ChangesToUtc);
GO

-- Lote 6 (D22): SystemQty = foto al crear; VarianceQty es informativa (contra la foto). ReconciledSystemQty = saldo en mano
-- bloqueado al reconciliar (base del ajuste); SystemQtyChanged marca que el saldo se movió desde la foto.
CREATE TABLE dbo.CycleCountLine (
    CycleCountLineId INT IDENTITY(1,1) PRIMARY KEY,
    CycleCountId INT NOT NULL REFERENCES dbo.CycleCount(CycleCountId),
    WarehouseBinId INT NOT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL,
    SystemQty    DECIMAL(16,3) NOT NULL, CountedQty DECIMAL(16,3) NULL,
    VarianceQty  AS (ISNULL(CountedQty,0) - SystemQty) PERSISTED,
    AdjustmentTxnId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),
    CountedSerialsJson NVARCHAR(MAX) NULL,                                    -- Lote 6
    ReconciledSystemQty DECIMAL(16,3) NULL,                                   -- Lote 6 (D22)
    SystemQtyChanged BIT NOT NULL DEFAULT 0,                                  -- Lote 6 (D22)
    -- Lote 21 (conteo por producto): evidencia. CountedQty es siempre el valor vigente; Captured* = lo que contó originalmente
    -- quien capturó; Corrected* = quién y cuándo lo corrigió (otro usuario, o cualquiera con el conteo ya Contado).
    CapturedQty  DECIMAL(16,3) NULL,
    CapturedSerialsJson NVARCHAR(MAX) NULL,
    CapturedBy   INT NULL REFERENCES dbo.AspNetUsers(Id),
    CapturedAtUtc DATETIME2 NULL,
    CorrectedBy  INT NULL REFERENCES dbo.AspNetUsers(Id),
    CorrectedAtUtc DATETIME2 NULL,
    CONSTRAINT FK_CycleCountLine_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),   -- Lote 6
    CONSTRAINT UQ_CycleCountLine UNIQUE (CycleCountId, WarehouseBinId, ProductId, LotId),                            -- Lote 6
    CONSTRAINT CK_CycleCountLine_Qty CHECK (SystemQty >= 0 AND (CountedQty IS NULL OR CountedQty >= 0) AND (ReconciledSystemQty IS NULL OR ReconciledSystemQty >= 0)),  -- Lote 6
    CONSTRAINT CK_CycleCountLine_Evidence CHECK ((CapturedQty IS NULL OR CapturedQty >= 0) AND (CorrectedAtUtc IS NULL OR CapturedQty IS NOT NULL))  -- Lote 21: no hay corrección sin captura original
);
GO

-- Lote 21: la posición provisional apunta al conteo desde el que se creó (WarehouseBin se crea antes que CycleCount).
IF OBJECT_ID('dbo.FK_WarehouseBin_ProvisionalCount', 'F') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD CONSTRAINT FK_WarehouseBin_ProvisionalCount FOREIGN KEY (ProvisionalCycleCountId) REFERENCES dbo.CycleCount(CycleCountId);
GO

-- Lote 14: posiciones con un conteo abierto ("lo cambiado" no repite una posición con conteo Pendiente o Contado).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CycleCountLine_Bin' AND object_id = OBJECT_ID('dbo.CycleCountLine'))
    CREATE INDEX IX_CycleCountLine_Bin ON dbo.CycleCountLine(WarehouseBinId) INCLUDE (CycleCountId);
GO

-- Lote 6 (D43): recolección y empaque ad hoc. Number = EMP-##### del contador PACKBATCH (D10): al empacar es el PackBatchNumber
-- de la orden. Una orden nace de a lo sumo una recolección activa (UX_PickBatch_Order).
CREATE TABLE dbo.PickBatch (
    PickBatchId  INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL,
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='PickBatchStatus'
    TransportOrderId INT NULL,
    ClientInvoiceNumber NVARCHAR(40) NULL,
    CollectedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CollectedBy  INT NULL REFERENCES dbo.AspNetUsers(Id),
    PackedAtUtc  DATETIME2 NULL,
    PackedBy     INT NULL REFERENCES dbo.AspNetUsers(Id),
    CancelledAtUtc DATETIME2 NULL,
    CancelledBy  INT NULL REFERENCES dbo.AspNetUsers(Id),
    IsActive     BIT NOT NULL DEFAULT 1,
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_PickBatch_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_PickBatch_IdTenant UNIQUE (PickBatchId, TenantId),
    CONSTRAINT FK_PickBatch_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),
    CONSTRAINT FK_PickBatch_Order FOREIGN KEY (TransportOrderId, TenantId) REFERENCES dbo.TransportOrder(TransportOrderId, TenantId),
    CONSTRAINT CK_PickBatch_Packed CHECK (TransportOrderId IS NULL OR PackedAtUtc IS NOT NULL)
);
CREATE UNIQUE INDEX UX_PickBatch_Order ON dbo.PickBatch(TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1;
CREATE INDEX IX_PickBatch_Tenant_Date ON dbo.PickBatch(TenantId, CollectedAtUtc);
GO

-- Lote 6: línea recolectada. Quantity en MAGNITUD (el ISSUE del ledger la guarda negativa); UnitCost congelado (D35);
-- ReversalTxnId = ADJUSTMENT PICK_BATCH_REVERSAL al eliminar la recolección (D13).
CREATE TABLE dbo.PickBatchLine (
    PickBatchLineId INT IDENTITY(1,1) PRIMARY KEY,
    PickBatchId  INT NOT NULL REFERENCES dbo.PickBatch(PickBatchId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL,
    SerialId     INT NULL,
    FromBinId    INT NOT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    Quantity     DECIMAL(16,3) NOT NULL,
    UnitCost     DECIMAL(18,4) NULL,
    IssueTxnId   BIGINT NOT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),
    ReversalTxnId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),
    CONSTRAINT FK_PickBatchLine_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),
    CONSTRAINT FK_PickBatchLine_Serial FOREIGN KEY (SerialId, ProductId) REFERENCES dbo.InventorySerial(SerialId, ProductId),
    CONSTRAINT CK_PickBatchLine CHECK (Quantity > 0 AND (UnitCost IS NULL OR UnitCost >= 0) AND (SerialId IS NULL OR Quantity = 1))
);
CREATE INDEX IX_PickBatchLine_Batch ON dbo.PickBatchLine(PickBatchId);
GO

-- Lote 14 (D5): descuadre Kárdex ↔ saldo detectado por la conciliación. Kind BALANCE = una clave (producto, almacén, posición,
-- lote); PRODUCT_TOTAL = el total del producto (sin almacén: SQL Server no revisa una FK compuesta con alguna columna NULL).
-- LedgerQty y BalanceQty son las cifras de la última revisión (la diferencia se calcula en código). OPEN → RESOLVED (el saldo
-- tomó lo que da el Kárdex: CorrectedFrom/To) | DISMISSED (con nota) | SELF_CORRECTED. Un solo abierto por clave
-- (UX_InvDiscrepancy_OpenKey, filtrado: con varias instancias del API la base es la última línea). Guardado (IF OBJECT_ID)
-- para agregarla a una base ya creada.
IF OBJECT_ID('dbo.InventoryDiscrepancy') IS NULL
BEGIN
CREATE TABLE dbo.InventoryDiscrepancy (
    InventoryDiscrepancyId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    KindLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),        -- Entity='InventoryDiscrepancyKind'
    TriggerLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='ReconciliationTrigger'
    ProductId    INT NOT NULL,
    WarehouseId  INT NULL,
    WarehouseBinId INT NULL,
    LotId        INT NULL,
    LedgerQty    DECIMAL(16,3) NOT NULL,
    BalanceQty   DECIMAL(16,3) NOT NULL,
    DetectedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    LastCheckedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CheckCount   INT NOT NULL DEFAULT 1,
    LastTxnId    BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='InventoryDiscrepancyStatus'
    ClosedAtUtc  DATETIME2 NULL,
    ResolvedBy   INT NULL REFERENCES dbo.AspNetUsers(Id),
    ResolutionNotes NVARCHAR(500) NULL,
    CorrectedFromQty DECIMAL(16,3) NULL,
    CorrectedToQty DECIMAL(16,3) NULL,
    CycleCountId INT NULL,
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_InvDiscrepancy_PublicId UNIQUE (PublicId),
    CONSTRAINT FK_InvDiscrepancy_Product FOREIGN KEY (ProductId, TenantId) REFERENCES dbo.Product(ProductId, TenantId),
    CONSTRAINT FK_InvDiscrepancy_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),
    CONSTRAINT FK_InvDiscrepancy_Bin FOREIGN KEY (WarehouseBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId),
    CONSTRAINT FK_InvDiscrepancy_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId),
    CONSTRAINT FK_InvDiscrepancy_CycleCount FOREIGN KEY (CycleCountId, TenantId) REFERENCES dbo.CycleCount(CycleCountId, TenantId),
    CONSTRAINT CK_InvDiscrepancy_Closed CHECK (ResolvedBy IS NULL OR ClosedAtUtc IS NOT NULL),
    CONSTRAINT CK_InvDiscrepancy_Corrected CHECK (CorrectedToQty IS NULL OR CorrectedToQty >= 0)
);
CREATE UNIQUE INDEX UX_InvDiscrepancy_OpenKey ON dbo.InventoryDiscrepancy(TenantId, KindLookupId, ProductId, WarehouseId, WarehouseBinId, LotId)
    WHERE ClosedAtUtc IS NULL;
CREATE INDEX IX_InvDiscrepancy_Tenant_Open ON dbo.InventoryDiscrepancy(TenantId, ClosedAtUtc, DetectedAtUtc);
CREATE INDEX IX_InvDiscrepancy_Product ON dbo.InventoryDiscrepancy(ProductId);
END
GO

/* =========================================================================
   CAPA 15 — CROSS-DOCKING
   ========================================================================= */
-- Lote 6 (D30): cita de muelle sin solapamiento por muelle (regla de servicio bajo el bloqueo del muelle); ASN o Trip, nunca ambos.
CREATE TABLE dbo.DockAppointment (
    DockAppointmentId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseDockId INT NOT NULL,
    DirectionLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='DockDirection'
    AsnId        INT NULL,
    TripId       INT NULL,
    ScheduledStartUtc DATETIME2 NOT NULL, ScheduledEndUtc DATETIME2 NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='AppointmentStatus'
    WarehouseId  INT NOT NULL,                                                -- Lote 6
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                 -- Lote 6
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    CONSTRAINT FK_DockAppt_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),          -- Lote 6
    CONSTRAINT FK_DockAppt_Dock FOREIGN KEY (WarehouseDockId, WarehouseId) REFERENCES dbo.WarehouseDock(WarehouseDockId, WarehouseId),  -- Lote 6
    CONSTRAINT FK_DockAppt_Asn FOREIGN KEY (AsnId, TenantId) REFERENCES dbo.Asn(AsnId, TenantId),                                  -- Lote 6
    CONSTRAINT FK_DockAppt_Trip FOREIGN KEY (TripId, TenantId) REFERENCES dbo.Trip(TripId, TenantId),                              -- Lote 6
    CONSTRAINT CK_DockAppt_Window CHECK (ScheduledEndUtc IS NULL OR ScheduledEndUtc > ScheduledStartUtc),                          -- Lote 6
    CONSTRAINT CK_DockAppt_Ref CHECK (AsnId IS NULL OR TripId IS NULL)                                                             -- Lote 6
);
CREATE INDEX IX_DockAppointment_Dock ON dbo.DockAppointment(WarehouseDockId, ScheduledStartUtc);
GO

CREATE TABLE dbo.CrossDockPlan (
    CrossDockPlanId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL,
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='CrossDockStatus'
    StagingZoneId INT NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    CompletedAtUtc DATETIME2 NULL,                                            -- Lote 6
    CONSTRAINT UQ_CrossDockPlan_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_CrossDockPlan_IdTenant UNIQUE (CrossDockPlanId, TenantId),                                                  -- Lote 6
    CONSTRAINT FK_CdPlan_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),       -- Lote 6
    CONSTRAINT FK_CdPlan_StagingZone FOREIGN KEY (StagingZoneId, WarehouseId) REFERENCES dbo.WarehouseZone(WarehouseZoneId, WarehouseId)  -- Lote 6
);
GO

-- Lote 6 (D29): ConfirmedQty = cantidad cubierta al confirmar el recibo; NULL mientras el recibo está abierto;
-- AllocatedQty − ConfirmedQty = faltante outbound visible (L320). Lo confirmado queda reservado en staging hasta moverlo.
CREATE TABLE dbo.CrossDockAllocation (
    CrossDockAllocationId INT IDENTITY(1,1) PRIMARY KEY,
    CrossDockPlanId INT NOT NULL REFERENCES dbo.CrossDockPlan(CrossDockPlanId),
    ReceiptLineId INT NOT NULL REFERENCES dbo.ReceiptLine(ReceiptLineId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),   -- Lote 6: NOT NULL
    CargoLineId  INT NULL REFERENCES dbo.CargoLine(CargoLineId),
    AllocatedQty DECIMAL(16,3) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='AllocationStatus'
    WarehouseTaskId INT NULL REFERENCES dbo.WarehouseTask(WarehouseTaskId),   -- Lote 6
    InventoryTransactionId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),  -- Lote 6
    ConfirmedQty DECIMAL(16,3) NULL,                                          -- Lote 6 (D29)
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),                 -- Lote 6
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),                     -- Lote 6
    CONSTRAINT CK_CdAlloc_Qty CHECK (AllocatedQty > 0 AND (ConfirmedQty IS NULL OR (ConfirmedQty >= 0 AND ConfirmedQty <= AllocatedQty)))  -- Lote 6
);
CREATE INDEX IX_CdAlloc_ReceiptLine ON dbo.CrossDockAllocation(ReceiptLineId);   -- Lote 6
GO

/* =========================================================================
   CAPA 16 — APP MÓVIL + PRUEBA DE ENTREGA
   ========================================================================= */
CREATE TABLE dbo.DriverDevice (
    DriverDeviceId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    PlatformLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='DevicePlatform'
    PushToken NVARCHAR(400) NULL, AppVersion NVARCHAR(20) NULL,
    LastSeenUtc DATETIME2 NULL, IsActive BIT NOT NULL DEFAULT 1
);
-- Lote 4: un token de push activo pertenece a un solo dispositivo por compañía
CREATE UNIQUE INDEX UX_DriverDevice_Token ON dbo.DriverDevice(TenantId, PushToken) WHERE PushToken IS NOT NULL AND IsActive = 1;
GO

CREATE TABLE dbo.DriverLocationPing (
    DriverLocationPingId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    TripId       INT NULL REFERENCES dbo.Trip(TripId),
    GeoPoint     GEOGRAPHY NOT NULL,
    SpeedKmh DECIMAL(6,2) NULL, HeadingDeg INT NULL,
    CapturedAtUtc DATETIME2 NOT NULL, ReceivedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_LocationPing_Trip ON dbo.DriverLocationPing(TripId, CapturedAtUtc);
CREATE INDEX IX_LocationPing_Driver ON dbo.DriverLocationPing(TenantId, DriverId, CapturedAtUtc);   -- Lote 5: ping de respaldo del chofer en el monitor
GO

CREATE TABLE dbo.ProofOfDelivery (
    ProofOfDeliveryId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    OrderStopId  INT NOT NULL REFERENCES dbo.OrderStop(OrderStopId),
    RouteStopId  INT NULL REFERENCES dbo.RouteStop(RouteStopId),
    DriverId     INT NULL REFERENCES dbo.Driver(DriverId),
    OutcomeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='PodOutcome'
    FailureReasonLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='DeliveryFailureReason'
    RecipientName NVARCHAR(150) NULL, RecipientRelation NVARCHAR(80) NULL,
    SignaturePath NVARCHAR(500) NULL, CapturedGeoPoint GEOGRAPHY NULL,
    CapturedAtUtc DATETIME2 NOT NULL, Notes NVARCHAR(500) NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Pod_Stop ON dbo.ProofOfDelivery(OrderStopId);
GO

CREATE TABLE dbo.ProofOfDeliveryPhoto (
    ProofOfDeliveryPhotoId INT IDENTITY(1,1) PRIMARY KEY,
    ProofOfDeliveryId INT NOT NULL REFERENCES dbo.ProofOfDelivery(ProofOfDeliveryId),
    StoragePath NVARCHAR(500) NOT NULL, GeoPoint GEOGRAPHY NULL,
    CapturedAtUtc DATETIME2 NULL, SortOrder INT NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.ProofOfDeliveryItem (
    ProofOfDeliveryItemId INT IDENTITY(1,1) PRIMARY KEY,
    ProofOfDeliveryId INT NOT NULL REFERENCES dbo.ProofOfDelivery(ProofOfDeliveryId),
    CargoLineId  INT NOT NULL REFERENCES dbo.CargoLine(CargoLineId),
    DeliveredQty DECIMAL(14,3) NOT NULL, RejectedQty DECIMAL(14,3) NOT NULL DEFAULT 0
);
GO

/* =========================================================================
   CAPA 16B — COD: COBRO CONTRA ENTREGA, RECONCILIACIÓN (PWBack) Y REMESA
   Depende de Client, TransportOrder, ProofOfDelivery, Driver.
   ========================================================================= */

-- Lote de remesa al cliente (sender): cuadre del dinero cobrado, comisión y neto a devolver
CREATE TABLE dbo.CodRemittanceBatch (
    CodRemittanceBatchId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    Number       NVARCHAR(40) NOT NULL,
    PeriodStart  DATE NOT NULL, PeriodEnd DATE NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),       -- Entity='RemittanceStatus'
    GrossCollected DECIMAL(18,4) NOT NULL DEFAULT 0,
    CommissionPct  DECIMAL(5,2) NULL,
    CommissionAmount DECIMAL(18,4) NOT NULL DEFAULT 0,
    NetRemitted  AS (GrossCollected - CommissionAmount) PERSISTED,
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    GeneratedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ApprovedBy   INT NULL REFERENCES dbo.AspNetUsers(Id), ApprovedAtUtc DATETIME2 NULL,
    RemittedAtUtc DATETIME2 NULL, ExportRef NVARCHAR(80) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_CodRemittanceBatch_Number UNIQUE (TenantId, Number)
);
CREATE INDEX IX_CodRemittanceBatch_Client ON dbo.CodRemittanceBatch(TenantId, ClientId, StatusCodeId);
GO

-- Cobro individual hecho en la entrega (puede haber parciales por orden)
CREATE TABLE dbo.CodCollection (
    CodCollectionId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    ProofOfDeliveryId INT NULL REFERENCES dbo.ProofOfDelivery(ProofOfDeliveryId),
    DriverId     INT NULL REFERENCES dbo.Driver(DriverId),
    MethodLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='CodPaymentMethod'
    AmountCollected DECIMAL(18,4) NOT NULL,
    CheckNumber  NVARCHAR(40) NULL,
    Reference    NVARCHAR(80) NULL,                                          -- ref ATH Móvil / depósito / etc.
    CollectedAtUtc DATETIME2 NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),       -- Entity='CodCollectionStatus'
    ReconciledAtUtc DATETIME2 NULL,
    CodRemittanceBatchId INT NULL REFERENCES dbo.CodRemittanceBatch(CodRemittanceBatchId),
    Notes        NVARCHAR(300) NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_CodCollection_Order ON dbo.CodCollection(TransportOrderId);
CREATE INDEX IX_CodCollection_Status ON dbo.CodCollection(TenantId, StatusCodeId);
CREATE INDEX IX_CodCollection_Batch ON dbo.CodCollection(CodRemittanceBatchId);
GO

-- Detalle de la remesa: qué cobros entraron, con su comisión y neto
CREATE TABLE dbo.CodRemittanceLine (
    CodRemittanceLineId BIGINT IDENTITY(1,1) PRIMARY KEY,
    CodRemittanceBatchId INT NOT NULL REFERENCES dbo.CodRemittanceBatch(CodRemittanceBatchId),
    CodCollectionId BIGINT NOT NULL REFERENCES dbo.CodCollection(CodCollectionId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    AmountCollected DECIMAL(18,4) NOT NULL,
    CommissionAmount DECIMAL(18,4) NOT NULL DEFAULT 0,
    NetAmount    AS (AmountCollected - CommissionAmount) PERSISTED,
    CONSTRAINT UQ_CodRemittanceLine UNIQUE (CodRemittanceBatchId, CodCollectionId)
);
GO

/* =========================================================================
   CAPA 16C — RENTAS (módulo RENTAL_EQUIPMENT "Rentas", submódulo de Almacén) — Lote 27, reescrita
   Una renta (REN-#####) despacha equipos PROPIOS con número de serie a una localidad del cliente. El equipo sigue en el
   inventario (posición EN-RENTA, reservado, serie ON_RENT) y regresa con una devolución de renta (DRN-#####) que puede abrir
   un proceso configurable. Envío y factura son enlaces nulos preparados (sin FK al envío: el módulo de Envíos no existe).
   Guardada (IF OBJECT_ID … IS NULL) para poder aplicarse sola sobre una base ya creada (Advance Depot): este bloque, del
   encabezado hasta CAPA 17, es autosuficiente (también amplía CK_NumberSequence_Kind y agrega las FKs a Invoice si ya existe).
   La capa anterior (RentalAsset, RentalContract, RentalAssetMaintenance, RentalBillingRule y el RentalCharge atado a
   RentalContract) se retira: en una base nueva no existe; en una existente se borra SOLO si está vacía (si no, se deja y se
   avisa por PRINT).
   ========================================================================= */

-- Retiro de la capa anterior (Lote 27): solo si todas sus tablas están vacías. Conteo por SQL dinámico (una tabla que falte no
-- rompe el lote).
DECLARE @OldRentalRows INT = 0, @OldRentalCount INT;
IF OBJECT_ID('dbo.RentalAsset') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RentalAsset', N'@n INT OUTPUT', @n = @OldRentalCount OUTPUT; SET @OldRentalRows += @OldRentalCount; END
IF OBJECT_ID('dbo.RentalContract') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RentalContract', N'@n INT OUTPUT', @n = @OldRentalCount OUTPUT; SET @OldRentalRows += @OldRentalCount; END
IF OBJECT_ID('dbo.RentalAssetMaintenance') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RentalAssetMaintenance', N'@n INT OUTPUT', @n = @OldRentalCount OUTPUT; SET @OldRentalRows += @OldRentalCount; END
IF OBJECT_ID('dbo.RentalBillingRule') IS NOT NULL BEGIN EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RentalBillingRule', N'@n INT OUTPUT', @n = @OldRentalCount OUTPUT; SET @OldRentalRows += @OldRentalCount; END
IF OBJECT_ID('dbo.RentalCharge') IS NOT NULL AND COL_LENGTH('dbo.RentalCharge', 'RentalContractId') IS NOT NULL
BEGIN EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RentalCharge', N'@n INT OUTPUT', @n = @OldRentalCount OUTPUT; SET @OldRentalRows += @OldRentalCount; END
IF @OldRentalRows > 0
    PRINT N'AVISO (Lote 27, Rentas): las tablas de la capa 16C anterior (RentalAsset, RentalContract, RentalAssetMaintenance, RentalBillingRule, RentalCharge) tienen '
        + CAST(@OldRentalRows AS NVARCHAR(20)) + N' fila(s): no se borran. Revíselas y retírelas a mano; RentalCharge no se recrea mientras tanto.';
ELSE
BEGIN
    IF OBJECT_ID('dbo.RentalCharge') IS NOT NULL AND COL_LENGTH('dbo.RentalCharge', 'RentalContractId') IS NOT NULL DROP TABLE dbo.RentalCharge;
    IF OBJECT_ID('dbo.RentalBillingRule') IS NOT NULL DROP TABLE dbo.RentalBillingRule;
    IF OBJECT_ID('dbo.RentalAssetMaintenance') IS NOT NULL DROP TABLE dbo.RentalAssetMaintenance;
    IF OBJECT_ID('dbo.RentalContract') IS NOT NULL DROP TABLE dbo.RentalContract;
    IF OBJECT_ID('dbo.RentalAsset') IS NOT NULL DROP TABLE dbo.RentalAsset;
END
GO

-- Lote 27: contadores RENTAL (REN-#####) y RENTALRETURN (DRN-#####) por tenant. En una base nueva el CHECK ya los trae (capa 5);
-- en una existente se recrea con la lista completa.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%RENTALRETURN%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN'));
END
GO

-- Renta: un cliente, una localidad del cliente, varios equipos de un almacén de origen. Contrato = número y fecha (D6).
-- PickupDate = recogido vigente (lo mueven las extensiones); OriginalPickupDate = el pactado. DeliveryShipmentId sin FK hasta
-- que exista Envíos; InvoiceId con FK a Invoice (se agrega cuando Invoice existe: abajo y en la capa 17).
IF OBJECT_ID('dbo.Rental') IS NULL
BEGIN
    CREATE TABLE dbo.Rental (
        RentalId     INT IDENTITY(1,1) PRIMARY KEY,
        PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
        Number       NVARCHAR(40) NOT NULL,                                        -- REN-#####
        ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
        LocationId   INT NOT NULL REFERENCES dbo.Location(LocationId),             -- localidad del cliente donde estará el equipo
        ClientContactId INT NULL REFERENCES dbo.ClientContact(ClientContactId),
        WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),           -- almacén de origen (y de su posición EN-RENTA)
        StartDate    DATE NOT NULL,
        PickupDate   DATE NOT NULL,                                                -- recogido vigente
        OriginalPickupDate DATE NOT NULL,                                          -- recogido pactado al inicio
        ContractNumber NVARCHAR(80) NULL,
        ContractSignedOn DATE NULL,
        EstimatedDeliveryCost DECIMAL(18,4) NULL,                                  -- solo dato (sin cálculo)
        TransportCurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='Currency'
        DeliveryShipmentId INT NULL,                                               -- envío futuro (sin FK: el módulo de Envíos no existe)
        InvoiceId    INT NULL,                                                     -- factura futura (FK FK_Rental_Invoice cuando existe Invoice)
        StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),         -- Entity='RentalStatus'
        DispatchedAtUtc DATETIME2 NULL,
        ClosedAtUtc  DATETIME2 NULL,
        Notes        NVARCHAR(1000) NULL,
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
        UpdatedAtUtc DATETIME2 NULL,
        UpdatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
        RowVersion   ROWVERSION,
        CONSTRAINT UQ_Rental_Number UNIQUE (TenantId, Number),
        CONSTRAINT CK_Rental_Dates CHECK (PickupDate >= StartDate AND OriginalPickupDate >= StartDate),
        CONSTRAINT CK_Rental_DeliveryCost CHECK (EstimatedDeliveryCost IS NULL OR EstimatedDeliveryCost >= 0)
    );
    CREATE INDEX IX_Rental_StatusPickup ON dbo.Rental(TenantId, StatusCodeId, PickupDate);
    CREATE INDEX IX_Rental_Client ON dbo.Rental(TenantId, ClientId);
END
GO

-- Equipo de la renta: una serie (FK compuesta con su producto). Una serie en UNA sola línea abierta (no devuelta y activa).
-- Sin TenantId: se alcanza por su renta filtrada. Quitar un equipo o cancelar la renta lo desactiva (IsActive = 0).
IF OBJECT_ID('dbo.RentalLine') IS NULL
BEGIN
    CREATE TABLE dbo.RentalLine (
        RentalLineId INT IDENTITY(1,1) PRIMARY KEY,
        RentalId     INT NOT NULL REFERENCES dbo.Rental(RentalId),
        ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
        SerialId     INT NOT NULL,
        LotId        INT NULL,
        FromBinId    INT NOT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),   -- posición de donde sale
        DispatchTxnId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),   -- TRANSFER del despacho
        DispatchedAtUtc DATETIME2 NULL,
        ReturnedAtUtc DATETIME2 NULL,
        IsActive     BIT NOT NULL DEFAULT 1,
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RentalLine_Serial FOREIGN KEY (SerialId, ProductId) REFERENCES dbo.InventorySerial(SerialId, ProductId),
        CONSTRAINT FK_RentalLine_Lot FOREIGN KEY (LotId, ProductId) REFERENCES dbo.InventoryLot(LotId, ProductId)
    );
    CREATE INDEX IX_RentalLine_Rental ON dbo.RentalLine(RentalId);
    CREATE UNIQUE INDEX UX_RentalLine_OpenSerial ON dbo.RentalLine(SerialId) WHERE ReturnedAtUtc IS NULL AND IsActive = 1;
END
GO

-- Extensión (D4): bitácora de SOLO INSERCIÓN; la fecha nueva siempre posterior a la anterior.
IF OBJECT_ID('dbo.RentalExtension') IS NULL
BEGIN
    CREATE TABLE dbo.RentalExtension (
        RentalExtensionId INT IDENTITY(1,1) PRIMARY KEY,
        RentalId     INT NOT NULL REFERENCES dbo.Rental(RentalId),
        PreviousPickupDate DATE NOT NULL,
        NewPickupDate DATE NOT NULL,
        Reason       NVARCHAR(300) NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_RentalExtension_Dates CHECK (NewPickupDate > PreviousPickupDate)
    );
    CREATE INDEX IX_RentalExtension_Rental ON dbo.RentalExtension(RentalId);
END
GO

-- Condiciones de cobro por equipo, efectivo-fechadas como RateComponent (EffectiveTo exclusivo). D3: solo se guardan.
IF OBJECT_ID('dbo.RentalLineRate') IS NULL
BEGIN
    CREATE TABLE dbo.RentalLineRate (
        RentalLineRateId INT IDENTITY(1,1) PRIMARY KEY,
        RentalLineId INT NOT NULL REFERENCES dbo.RentalLine(RentalLineId),
        BillingFrequencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='RentalBillingFrequency'
        RateAmount   DECIMAL(18,4) NOT NULL,
        CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),           -- Entity='Currency'
        EffectiveFrom DATE NOT NULL,
        EffectiveTo  DATE NULL,                                                          -- exclusivo; NULL = vigente
        RentalExtensionId INT NULL REFERENCES dbo.RentalExtension(RentalExtensionId),    -- extensión que abrió esta versión
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_RentalLineRate_Amount CHECK (RateAmount >= 0),
        CONSTRAINT CK_RentalLineRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
    );
    CREATE INDEX IX_RentalLineRate_Line ON dbo.RentalLineRate(RentalLineId, EffectiveFrom);
END
GO

-- Devolución de renta DRN-##### (bloque R2): motivo por devolución, costo de recogido estimado y enlace nulo al envío futuro.
IF OBJECT_ID('dbo.RentalReturn') IS NULL
BEGIN
    CREATE TABLE dbo.RentalReturn (
        RentalReturnId INT IDENTITY(1,1) PRIMARY KEY,
        PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
        Number       NVARCHAR(40) NOT NULL,                                        -- DRN-#####
        RentalId     INT NOT NULL REFERENCES dbo.Rental(RentalId),
        ReturnedOn   DATE NOT NULL,
        ReasonLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='RentalReturnReason'
        Notes        NVARCHAR(1000) NULL,
        EstimatedPickupCost DECIMAL(18,4) NULL,
        TransportCurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='Currency'
        PickupShipmentId INT NULL,                                                 -- envío futuro del recogido (sin FK)
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT UQ_RentalReturn_Number UNIQUE (TenantId, Number),
        CONSTRAINT CK_RentalReturn_PickupCost CHECK (EstimatedPickupCost IS NULL OR EstimatedPickupCost >= 0)
    );
    CREATE INDEX IX_RentalReturn_Rental ON dbo.RentalReturn(RentalId);
END
GO

-- Equipo devuelto (bloque R2): un equipo se devuelve una sola vez (UQ por línea de la renta).
IF OBJECT_ID('dbo.RentalReturnLine') IS NULL
BEGIN
    CREATE TABLE dbo.RentalReturnLine (
        RentalReturnLineId INT IDENTITY(1,1) PRIMARY KEY,
        RentalReturnId INT NOT NULL REFERENCES dbo.RentalReturn(RentalReturnId),
        RentalLineId INT NOT NULL REFERENCES dbo.RentalLine(RentalLineId),
        ConditionLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='RentalReturnCondition'
        ToWarehouseId INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
        ToBinId      INT NOT NULL,
        RequiresProcess BIT NOT NULL DEFAULT 1,
        ReturnTxnId  BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId),
        Notes        NVARCHAR(500) NULL,
        CONSTRAINT UQ_RentalReturnLine_Line UNIQUE (RentalLineId),
        CONSTRAINT FK_RentalReturnLine_ToBin FOREIGN KEY (ToBinId, ToWarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId)
    );
END
GO

-- Proceso del equipo devuelto (bloque R2): estatus RentalProcessStatus configurable; la serie queda IN_PROCESS y reservada.
IF OBJECT_ID('dbo.RentalProcess') IS NULL
BEGIN
    CREATE TABLE dbo.RentalProcess (
        RentalProcessId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
        SerialId     INT NOT NULL,
        ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
        WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
        BinId        INT NOT NULL,
        RentalReturnLineId INT NULL REFERENCES dbo.RentalReturnLine(RentalReturnLineId),
        StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),         -- Entity='RentalProcessStatus'
        StartedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc DATETIME2 NULL,
        Notes        NVARCHAR(1000) NULL,
        RowVersion   ROWVERSION,
        CONSTRAINT FK_RentalProcess_Serial FOREIGN KEY (SerialId, ProductId) REFERENCES dbo.InventorySerial(SerialId, ProductId),
        CONSTRAINT FK_RentalProcess_Bin FOREIGN KEY (BinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId)
    );
    CREATE INDEX IX_RentalProcess_Status ON dbo.RentalProcess(TenantId, StatusCodeId);
    CREATE INDEX IX_RentalProcess_Serial ON dbo.RentalProcess(SerialId);
END
GO

-- Cobro por período (futuro, SIN mapear): un cargo por equipo y período, con enlace a la factura (FK cuando existe Invoice).
-- Si quedó el RentalCharge viejo (con datos, ver arriba) no se recrea.
IF OBJECT_ID('dbo.RentalCharge') IS NULL
BEGIN
    CREATE TABLE dbo.RentalCharge (
        RentalChargeId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
        RentalLineId INT NOT NULL REFERENCES dbo.RentalLine(RentalLineId),
        PeriodStart  DATE NOT NULL, PeriodEnd DATE NOT NULL,
        Amount       DECIMAL(18,4) NOT NULL,
        StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),         -- Entity='RentalChargeStatus' (pendiente/facturado/pagado)
        InvoiceId    INT NULL,                                                     -- FK a Invoice (FK_RentalCharge_Invoice cuando existe Invoice)
        CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_RentalCharge_Period CHECK (PeriodEnd >= PeriodStart)
    );
    CREATE INDEX IX_RentalCharge_Line ON dbo.RentalCharge(RentalLineId, StatusCodeId);
END
GO

-- FKs a Invoice cuando la base ya la tiene (aplicación de este bloque sobre una base existente); en una base nueva Invoice se
-- crea en la capa 17 y las FKs se agregan ahí.
IF OBJECT_ID('dbo.Invoice') IS NOT NULL AND OBJECT_ID('dbo.FK_Rental_Invoice', 'F') IS NULL
    ALTER TABLE dbo.Rental ADD CONSTRAINT FK_Rental_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoice(InvoiceId);
GO
IF OBJECT_ID('dbo.Invoice') IS NOT NULL AND OBJECT_ID('dbo.FK_RentalCharge_Invoice', 'F') IS NULL
    ALTER TABLE dbo.RentalCharge ADD CONSTRAINT FK_RentalCharge_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoice(InvoiceId);
GO

/* =========================================================================
   CAPA 17 — PORTAL + FACTURACIÓN Y LIQUIDACIÓN
   ========================================================================= */
CREATE TABLE dbo.PortalUser (
    PortalUserId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),
    Email        NVARCHAR(150) NOT NULL, FullName NVARCHAR(150) NULL,
    RoleLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),        -- Entity='PortalRole'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='PortalUserStatus'
    LastLoginUtc DATETIME2 NULL, IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_PortalUser UNIQUE (TenantId, ClientId, Email)   -- Lote 3: una cuenta de portal por cliente (la unicidad del correo entre cuentas la da AspNetUsers)
);
GO

CREATE TABLE dbo.PortalNotificationPref (
    PortalNotificationPrefId INT IDENTITY(1,1) PRIMARY KEY,
    PortalUserId INT NOT NULL REFERENCES dbo.PortalUser(PortalUserId),
    EventTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='PortalEventType'
    ViaEmail BIT NOT NULL DEFAULT 1, ViaPush BIT NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.BillingRun (
    BillingRunId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    PeriodStart DATE NOT NULL, PeriodEnd DATE NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='BillingRunStatus'
    GeneratedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ApprovedBy   INT NULL REFERENCES dbo.AspNetUsers(Id), ApprovedAtUtc DATETIME2 NULL,
    InvoiceCount INT NOT NULL DEFAULT 0, TotalAmount DECIMAL(18,4) NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Invoice (
    InvoiceId    INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    ContractId   INT NULL REFERENCES dbo.Contract(ContractId),
    InvoiceNumber NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='InvoiceStatus'
    IssueDate DATE NULL, DueDate DATE NULL,
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Subtotal DECIMAL(18,4) NOT NULL DEFAULT 0, TaxAmount DECIMAL(18,4) NOT NULL DEFAULT 0,
    Total AS (Subtotal + TaxAmount) PERSISTED,
    BillingRunId INT NULL REFERENCES dbo.BillingRun(BillingRunId),
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_Invoice_Number UNIQUE (TenantId, InvoiceNumber)
);
GO
-- FKs diferidas: Rental y RentalCharge (capa 16C) se crean antes que Invoice. Guardadas (Lote 27): si la capa 16C se aplicó
-- sobre una base existente que ya tenía Invoice, ya están.
IF OBJECT_ID('dbo.FK_Rental_Invoice', 'F') IS NULL
    ALTER TABLE dbo.Rental ADD CONSTRAINT FK_Rental_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoice(InvoiceId);
GO
IF OBJECT_ID('dbo.FK_RentalCharge_Invoice', 'F') IS NULL
    ALTER TABLE dbo.RentalCharge ADD CONSTRAINT FK_RentalCharge_Invoice FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoice(InvoiceId);
GO

CREATE TABLE dbo.InvoiceLine (
    InvoiceLineId INT IDENTITY(1,1) PRIMARY KEY,
    InvoiceId    INT NOT NULL REFERENCES dbo.Invoice(InvoiceId),
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    ProductId    INT NULL REFERENCES dbo.Product(ProductId),                  -- venta de producto propio (ChargeType='PRODUCT_SALE'), NULL si es solo flete/servicio
    ChargeTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='ChargeType'
    Description  NVARCHAR(250) NOT NULL,
    Quantity DECIMAL(14,3) NOT NULL DEFAULT 1, UnitAmount DECIMAL(18,4) NOT NULL,
    LineTotal AS (Quantity * UnitAmount) PERSISTED,
    RateComponentId INT NULL REFERENCES dbo.RateComponent(RateComponentId)
);
GO

CREATE TABLE dbo.Payment (
    PaymentId    INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    InvoiceId    INT NOT NULL REFERENCES dbo.Invoice(InvoiceId),
    PaymentMethodLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='PaymentMethod'
    Amount DECIMAL(18,4) NOT NULL, PaidAtUtc DATETIME2 NOT NULL, Reference NVARCHAR(80) NULL
);
GO

CREATE TABLE dbo.CarrierSettlement (
    CarrierSettlementId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverId     INT NULL REFERENCES dbo.Driver(DriverId),
    PeriodStart DATE NOT NULL, PeriodEnd DATE NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='SettlementStatus'
    GrossAmount DECIMAL(18,4) NOT NULL DEFAULT 0, DeductionAmount DECIMAL(18,4) NOT NULL DEFAULT 0,
    NetAmount AS (GrossAmount - DeductionAmount) PERSISTED,
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId)
);
GO

CREATE TABLE dbo.SettlementLine (
    SettlementLineId INT IDENTITY(1,1) PRIMARY KEY,
    CarrierSettlementId INT NOT NULL REFERENCES dbo.CarrierSettlement(CarrierSettlementId),
    TripId       INT NULL REFERENCES dbo.Trip(TripId),
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    LineTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='SettlementLineType'
    Description NVARCHAR(250) NOT NULL, Amount DECIMAL(18,4) NOT NULL
);
GO

/* =========================================================================
   CAPA 18 — DASHBOARDS + API INTEGRACIONES + AUDITORÍA
   ========================================================================= */
CREATE TABLE dbo.KpiDailySnapshot (
    KpiDailySnapshotId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    SnapshotDate DATE NOT NULL,
    KpiKeyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='KpiKey'
    DimEntityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    DimId        INT NULL, NumericValue DECIMAL(18,4) NULL,
    CONSTRAINT UQ_KpiSnapshot UNIQUE (TenantId, SnapshotDate, KpiKeyLookupId, DimEntityLookupId, DimId)
);
GO

CREATE TABLE dbo.ApiCredential (
    ApiCredentialId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Name         NVARCHAR(120) NOT NULL,
    IntegrationTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='IntegrationType'
    ClientIdRef  NVARCHAR(80) NOT NULL, SecretHash NVARCHAR(200) NOT NULL, Scopes NVARCHAR(400) NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='ApiCredentialStatus'
    ExpiresAtUtc DATETIME2 NULL, CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.WebhookSubscription (
    WebhookSubscriptionId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ApiCredentialId INT NULL REFERENCES dbo.ApiCredential(ApiCredentialId),
    EventTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='WebhookEventType'
    TargetUrl NVARCHAR(500) NOT NULL, SigningSecret NVARCHAR(200) NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.WebhookDelivery (
    WebhookDeliveryId BIGINT IDENTITY(1,1) PRIMARY KEY,
    WebhookSubscriptionId INT NOT NULL REFERENCES dbo.WebhookSubscription(WebhookSubscriptionId),
    PayloadJson NVARCHAR(MAX) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='WebhookDeliveryStatus'
    AttemptCount INT NOT NULL DEFAULT 0, LastAttemptUtc DATETIME2 NULL, ResponseCode INT NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_WebhookDelivery_Sub ON dbo.WebhookDelivery(WebhookSubscriptionId, StatusCodeId);
GO

CREATE TABLE dbo.IntegrationMessageLog (
    IntegrationMessageLogId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ApiCredentialId INT NULL REFERENCES dbo.ApiCredential(ApiCredentialId),
    DirectionLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='MessageDirection'
    -- Lote 8A: collation binaria para que la clave de idempotencia distinga mayúsculas (también en los índices).
    Endpoint NVARCHAR(200) NULL, IdempotencyKey NVARCHAR(80) COLLATE Latin1_General_100_BIN2 NULL,
    RequestJson NVARCHAR(MAX) NULL, ResponseCode INT NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    -- Lote 8A: idempotencia del API (Idempotency-Key). Clave lógica (TenantId, UserId, IdempotencyKey); RequestHash = SHA-256
    -- hex de método, ruta y cuerpo; ResponseCode/ResponseJson NULL mientras la operación está en vuelo. Se borran a los 7 días.
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),
    RequestHash  NVARCHAR(64) NULL,
    ResponseJson NVARCHAR(MAX) NULL,
    Method       NVARCHAR(8) NULL,
    -- Lote 8A: comprobaciones de módulo/permiso hechas en el servicio o el controlador (JSON); se reevalúan antes de repetir.
    ReplayChecksJson NVARCHAR(MAX) NULL
);
CREATE INDEX IX_IntegrationLog_Idem ON dbo.IntegrationMessageLog(TenantId, IdempotencyKey);
CREATE UNIQUE INDEX UX_IntegrationLog_Idem ON dbo.IntegrationMessageLog(TenantId, UserId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
GO

CREATE TABLE dbo.AuditLog (
    AuditLogId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    EntityTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='EntityType'
    EntityId     INT NOT NULL,
    ActionLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='AuditAction'
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),
    ChangesJson  NVARCHAR(MAX) NULL, CorrelationId UNIQUEIDENTIFIER NULL,
    IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(300) NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_AuditLog_Entity ON dbo.AuditLog(EntityTypeLookupId, EntityId, CreatedAtUtc);
CREATE INDEX IX_AuditLog_User ON dbo.AuditLog(UserId, CreatedAtUtc);
GO

CREATE TABLE dbo.SecurityEvent (
    SecurityEventId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NULL REFERENCES dbo.Tenant(TenantId),
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),
    EventTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='SecurityEventType'
    OutcomeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),     -- Entity='SecurityOutcome'
    IpAddress NVARCHAR(45) NULL, UserAgent NVARCHAR(300) NULL, DetailJson NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_SecurityEvent_User ON dbo.SecurityEvent(UserId, CreatedAtUtc);
CREATE INDEX IX_SecurityEvent_Type ON dbo.SecurityEvent(EventTypeLookupId, CreatedAtUtc);
GO

/* =========================================================================
   VISTAS
   ========================================================================= */
-- Lote 6 (D32): ampliada para BI y SQL externo (la API arma la genealogía por LINQ con filtro de tenant). Conserva las columnas
-- existentes en su orden y agrega al final el id del movimiento, las posiciones, NetQuantity (0 en TRANSFER: Σ NetQuantity por
-- producto = existencia, maestro L331), el motivo y el usuario. Quantity viene CON SIGNO (D3).
CREATE VIEW dbo.vw_LotGenealogy AS
SELECT t.TenantId, t.ProductId, p.Sku, p.Name AS ProductName,
       t.LotId, l.LotNumber, l.ExpiryDate, t.SerialId, s.SerialNumber,
       t.TxnTypeLookupId, txn.InternalCode AS TxnType, t.Quantity,
       t.FromWarehouseId, t.ToWarehouseId,
       t.RefEntityLookupId, ref.InternalCode AS RefEntity, t.RefId, t.CreatedAtUtc,
       t.InventoryTransactionId, t.FromBinId, t.ToBinId,
       NetQuantity = CASE WHEN txn.InternalCode = 'TRANSFER' THEN 0 ELSE t.Quantity END,
       t.ReasonLookupId, reason.InternalCode AS Reason, t.CreatedBy
FROM dbo.InventoryTransaction t
JOIN dbo.Product p ON p.ProductId = t.ProductId
LEFT JOIN dbo.InventoryLot l ON l.LotId = t.LotId
LEFT JOIN dbo.InventorySerial s ON s.SerialId = t.SerialId
LEFT JOIN dbo.LookupCode txn ON txn.LookupCodeId = t.TxnTypeLookupId
LEFT JOIN dbo.LookupCode ref ON ref.LookupCodeId = t.RefEntityLookupId
LEFT JOIN dbo.LookupCode reason ON reason.LookupCodeId = t.ReasonLookupId;
GO

PRINT 'Estructura creada: ~139 tablas (Identity, campos personalizados, informes, indicadores, gráficos, ciclo COD, pago a choferes, faltantes de compras, recolección y empaque y rentas), en capas ordenadas por dependencias + vista de genealogía.';
GO
