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
   CAPA 0 — IDENTIDAD (mínima; en prod la genera Identity)
   ========================================================================= */
IF OBJECT_ID('dbo.AspNetUsers') IS NULL
CREATE TABLE dbo.AspNetUsers (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    UserName            NVARCHAR(256) NULL,
    NormalizedUserName  NVARCHAR(256) NULL,
    Email               NVARCHAR(256) NULL,
    NormalizedEmail     NVARCHAR(256) NULL,
    EmailConfirmed      BIT NOT NULL DEFAULT 0,
    PasswordHash        NVARCHAR(MAX) NULL,
    SecurityStamp       NVARCHAR(MAX) NULL,
    ConcurrencyStamp    NVARCHAR(MAX) NULL,
    PhoneNumber         NVARCHAR(50) NULL,
    PhoneNumberConfirmed BIT NOT NULL DEFAULT 0,
    TwoFactorEnabled    BIT NOT NULL DEFAULT 0,
    LockoutEnd          DATETIMEOFFSET NULL,
    LockoutEnabled      BIT NOT NULL DEFAULT 1,
    AccessFailedCount   INT NOT NULL DEFAULT 0,
    -- columnas de app
    FullName            NVARCHAR(150) NULL,
    DefaultTenantId     INT NULL,
    UserKindLookupId    INT NULL,
    IsActive            BIT NOT NULL DEFAULT 1,
    CreatedAtUtc        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    LastLoginUtc        DATETIME2 NULL
);
GO
IF OBJECT_ID('dbo.AspNetRoles') IS NULL
CREATE TABLE dbo.AspNetRoles (
    Id   INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(256) NULL,
    NormalizedName NVARCHAR(256) NULL,
    ConcurrencyStamp NVARCHAR(MAX) NULL
);
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
    IsActive        BIT NOT NULL DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion      ROWVERSION
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
    ReplacedByTokenHash NVARCHAR(200) NULL
);
CREATE INDEX IX_RefreshToken_User ON dbo.RefreshToken(UserId) WHERE RevokedAtUtc IS NULL;
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
    ColumnsJson     NVARCHAR(MAX) NOT NULL,          -- columnas: campos nativos + custom fields (FieldKey)
    FilterJson      NVARCHAR(MAX) NULL,              -- DSL de filtros (reusa evaluador SEPHAS)
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
    CONSTRAINT UQ_Client_Tenant_Code UNIQUE (TenantId, Code)
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
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion   ROWVERSION
);
CREATE INDEX IX_Location_Tenant_Client ON dbo.Location(TenantId, ClientId) WHERE IsActive = 1;
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
    CodCommissionPct DECIMAL(5,2) NULL,                                     -- comisión COD por defecto (remesa)
    AutoRenew    BIT NOT NULL DEFAULT 0,
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_Contract_Number UNIQUE (TenantId, ContractNumber)
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
CREATE TABLE dbo.RateComponent (
    RateComponentId INT IDENTITY(1,1) PRIMARY KEY,
    RateCardId   INT NOT NULL REFERENCES dbo.RateCard(RateCardId),
    ComponentTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='RateComponentType'
    BasisLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),         -- Entity='RateBasis'
    FromZoneId   INT NULL REFERENCES dbo.RateZone(RateZoneId),
    ToZoneId     INT NULL REFERENCES dbo.RateZone(RateZoneId),
    ServiceTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    PricingModeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='PricingMode'
    TierModeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),          -- Entity='TierMode'
    FlatAmount   DECIMAL(18,4) NULL,
    UnitAmount   DECIMAL(18,4) NULL,
    MinCharge    DECIMAL(18,4) NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.RateTier (
    RateTierId   INT IDENTITY(1,1) PRIMARY KEY,
    RateComponentId INT NOT NULL REFERENCES dbo.RateComponent(RateComponentId),
    MinValue     DECIMAL(14,3) NOT NULL,
    MaxValue     DECIMAL(14,3) NULL,
    UnitAmount   DECIMAL(18,4) NULL,
    FlatAmount   DECIMAL(18,4) NULL,
    SortOrder    INT NOT NULL DEFAULT 0
);
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
    CONSTRAINT UQ_Warehouse_Code UNIQUE (TenantId, Code)
);
GO

CREATE TABLE dbo.WarehouseZone (
    WarehouseZoneId INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Code         NVARCHAR(30) NOT NULL, Name NVARCHAR(120) NOT NULL,
    ZoneTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='ZoneType'
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_WarehouseZone UNIQUE (WarehouseId, Code)
);
GO

CREATE TABLE dbo.WarehouseBin (
    WarehouseBinId INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseZoneId INT NOT NULL REFERENCES dbo.WarehouseZone(WarehouseZoneId),
    Code         NVARCHAR(40) NOT NULL,
    Aisle NVARCHAR(20) NULL, Rack NVARCHAR(20) NULL, Level NVARCHAR(20) NULL, Position NVARCHAR(20) NULL,
    MaxWeightKg  DECIMAL(12,3) NULL, IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_WarehouseBin UNIQUE (WarehouseZoneId, Code)
);
GO

CREATE TABLE dbo.WarehouseDock (
    WarehouseDockId INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Code         NVARCHAR(30) NOT NULL,
    DockTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='DockType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='DockStatus'
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_WarehouseDock UNIQUE (WarehouseId, Code)
);
GO

/* =========================================================================
   CAPA 8 — INVENTARIO  (antes de órdenes: CargoLine referencia Product)
   ========================================================================= */
CREATE TABLE dbo.ProductCategory (
    ProductCategoryId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ParentId     INT NULL REFERENCES dbo.ProductCategory(ProductCategoryId),
    Name         NVARCHAR(150) NOT NULL, IsActive BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.Product (
    ProductId    INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NULL REFERENCES dbo.Client(ClientId),
    Sku          NVARCHAR(60) NOT NULL, Name NVARCHAR(200) NOT NULL,
    ProductCategoryId INT NULL REFERENCES dbo.ProductCategory(ProductCategoryId),
    BaseUomLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='UnitOfMeasure'
    TrackingTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='TrackingType'
    WeightKg DECIMAL(12,3) NULL, VolumeM3 DECIMAL(12,4) NULL, Barcode NVARCHAR(60) NULL,
    PurchaseCost DECIMAL(18,4) NULL,   -- default para la línea de PO; costo de compra al proveedor
    SalePrice    DECIMAL(18,4) NULL,   -- precio de venta al cliente final (alimenta InvoiceLine ChargeType='PRODUCT_SALE')
    PreferredWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId), -- almacén por default al recibir
    PreferredBinId INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),  -- posición por default al recibir; sugerencia de putaway, NO es la ubicación real (esa vive en StockBalance)
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_Product_Sku UNIQUE (TenantId, ClientId, Sku)
);
GO

CREATE TABLE dbo.InventoryLot (
    LotId        INT IDENTITY(1,1) PRIMARY KEY,
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotNumber    NVARCHAR(60) NOT NULL, ManufactureDate DATE NULL, ExpiryDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Lot UNIQUE (ProductId, LotNumber)
);
GO

CREATE TABLE dbo.InventorySerial (
    SerialId     INT IDENTITY(1,1) PRIMARY KEY,
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialNumber NVARCHAR(80) NOT NULL,
    StatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),  -- Entity='SerialStatus'
    CONSTRAINT UQ_Serial UNIQUE (ProductId, SerialNumber)
);
GO

CREATE TABLE dbo.StockBalance (
    StockBalanceId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    WarehouseBinId INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    QtyOnHand    DECIMAL(16,3) NOT NULL DEFAULT 0,
    QtyReserved  DECIMAL(16,3) NOT NULL DEFAULT 0,
    QtyAvailable AS (QtyOnHand - QtyReserved) PERSISTED,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion ROWVERSION,
    CONSTRAINT UQ_StockBalance UNIQUE (ProductId, WarehouseId, WarehouseBinId, LotId)
);
CREATE INDEX IX_StockBalance_WH ON dbo.StockBalance(WarehouseId, ProductId);
GO

CREATE TABLE dbo.InventoryTransaction (
    InventoryTransactionId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TxnTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- Entity='InventoryTxnType'
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    FromWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    FromBinId    INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    ToWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    ToBinId      INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    Quantity     DECIMAL(16,3) NOT NULL,
    RefEntityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='EntityType'
    RefId        INT NULL,
    Notes        NVARCHAR(300) NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id)
);
CREATE INDEX IX_InvTxn_Product ON dbo.InventoryTransaction(ProductId, CreatedAtUtc);
CREATE INDEX IX_InvTxn_Ref ON dbo.InventoryTransaction(RefEntityLookupId, RefId);
GO

/* =========================================================================
   CAPA 10 — FLOTA Y CHOFERES (definición completa)
   ========================================================================= */
CREATE TABLE dbo.Vehicle (
    VehicleId    INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(30) NOT NULL, PlateNumber NVARCHAR(20) NULL,
    MaxWeightKg DECIMAL(12,3) NULL, MaxVolumeM3 DECIMAL(12,4) NULL, MaxStops INT NULL,
    VehicleTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='VehicleType'
    OwnershipLookupId   INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='Ownership'
    FuelTypeLookupId    INT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='FuelType'
    Make NVARCHAR(60) NULL, Model NVARCHAR(60) NULL, ModelYear INT NULL, Vin NVARCHAR(40) NULL,
    CurrentOdometerKm DECIMAL(12,1) NULL,
    HomeWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    StatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),          -- Entity='VehicleStatus'
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Vehicle_Code UNIQUE (TenantId, Code)
);
GO

CREATE TABLE dbo.Driver (
    DriverId     INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    FullName     NVARCHAR(150) NOT NULL,
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),
    EmployeeCode NVARCHAR(30) NULL,
    HireDate     DATE NULL,
    HomeWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    StatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),          -- Entity='DriverStatus'
    MaxStopsPerRoute INT NULL,   -- override del default del tenant; NULL = usa Tenant.MaxStopsPerRouteDefault
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

/* =========================================================================
   CAPA 11 — ÓRDENES DE TRANSPORTE
   ========================================================================= */
CREATE TABLE dbo.TransportOrder (
    TransportOrderId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    ContractId   INT NULL REFERENCES dbo.Contract(ContractId),
    OrderNumber  NVARCHAR(40) NOT NULL,
    ServiceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ServiceType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='OrderStatus'
    PriorityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),        -- Entity='OrderPriority'
    RequestedDate DATETIME2 NULL, PromisedDate DATETIME2 NULL,
    TotalWeightKg DECIMAL(14,3) NULL, TotalVolumeM3 DECIMAL(14,4) NULL, TotalPieces INT NULL,
    QuotedAmount DECIMAL(18,4) NULL,
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    -- COD (cobro contra entrega)
    CodTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),         -- Entity='CodType' (NONE/CASH/CHECK/COMPANY_CHECK)
    CodAmount    DECIMAL(18,4) NULL,
    CodCurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    CodStatusCodeId INT NULL REFERENCES dbo.StatusCode(StatusCodeId),         -- Entity='CodStatus' (NULL = sin COD)
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL REFERENCES dbo.AspNetUsers(Id),
    RowVersion   ROWVERSION,
    CONSTRAINT UQ_Order_Number UNIQUE (TenantId, OrderNumber)
);
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
GO

CREATE TABLE dbo.CargoLine (
    CargoLineId  INT IDENTITY(1,1) PRIMARY KEY,
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    PickupStopId INT NULL REFERENCES dbo.OrderStop(OrderStopId),
    DeliveryStopId INT NULL REFERENCES dbo.OrderStop(OrderStopId),
    ProductId    INT NULL REFERENCES dbo.Product(ProductId),
    Description  NVARCHAR(250) NOT NULL,
    Quantity     DECIMAL(14,3) NOT NULL DEFAULT 1,
    UomLookupId  INT NULL REFERENCES dbo.LookupCode(LookupCodeId),            -- Entity='UnitOfMeasure'
    WeightKg DECIMAL(12,3) NULL, VolumeM3 DECIMAL(12,4) NULL,
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    HandlingFlags INT NOT NULL DEFAULT 0,
    IsActive     BIT NOT NULL DEFAULT 1
);
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
CREATE TABLE dbo.Trip (
    TripId       INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code         NVARCHAR(40) NOT NULL, PlanDate DATE NOT NULL,
    OriginWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),
    VehicleId    INT NULL REFERENCES dbo.Vehicle(VehicleId),
    DriverId     INT NULL REFERENCES dbo.Driver(DriverId),
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='TripStatus'
    PlannedStartUtc DATETIME2 NULL, PlannedEndUtc DATETIME2 NULL,
    ActualStartUtc DATETIME2 NULL, ActualEndUtc DATETIME2 NULL,
    TotalDistanceKm DECIMAL(12,3) NULL, TotalDurationMin INT NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_Trip_Code UNIQUE (TenantId, Code)
);
CREATE INDEX IX_Trip_Tenant_Date ON dbo.Trip(TenantId, PlanDate) WHERE IsActive = 1;
GO

CREATE TABLE dbo.TripOrder (
    TripOrderId  INT IDENTITY(1,1) PRIMARY KEY,
    TripId       INT NOT NULL REFERENCES dbo.Trip(TripId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    SortHint     INT NULL,
    CONSTRAINT UQ_TripOrder UNIQUE (TripId, TransportOrderId)
);
GO

CREATE TABLE dbo.Route (
    RouteId      INT IDENTITY(1,1) PRIMARY KEY,
    TripId       INT NOT NULL REFERENCES dbo.Trip(TripId),
    Version      INT NOT NULL DEFAULT 1, IsActive BIT NOT NULL DEFAULT 1,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='RouteStatus'
    TotalDistanceKm DECIMAL(12,3) NULL, TotalDurationMin INT NULL, StopCount INT NOT NULL DEFAULT 0,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion ROWVERSION
);
CREATE INDEX IX_Route_Trip ON dbo.Route(TripId) WHERE IsActive = 1;
GO

CREATE TABLE dbo.RouteStop (
    RouteStopId  INT IDENTITY(1,1) PRIMARY KEY,
    RouteId      INT NOT NULL REFERENCES dbo.Route(RouteId),
    OrderStopId  INT NOT NULL REFERENCES dbo.OrderStop(OrderStopId),
    Sequence     INT NOT NULL,
    PlannedArrivalUtc DATETIME2 NULL, PlannedDepartureUtc DATETIME2 NULL,
    DistanceFromPrevKm DECIMAL(12,3) NULL, DurationFromPrevMin INT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='RouteStopStatus'
    ActualArrivalUtc DATETIME2 NULL, ActualDepartureUtc DATETIME2 NULL,
    CONSTRAINT UQ_RouteStop UNIQUE (RouteId, OrderStopId)
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
    CONSTRAINT UQ_DispatchZone UNIQUE (TenantId, Code)
);
GO

CREATE TABLE dbo.DispatchZoneMember (
    DispatchZoneMemberId INT IDENTITY(1,1) PRIMARY KEY,
    DispatchZoneId INT NOT NULL REFERENCES dbo.DispatchZone(DispatchZoneId),
    MatchTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),  -- reutiliza Entity='ZoneMatchType'
    MatchValue   NVARCHAR(120) NOT NULL                                     -- ej. '00949' o 'Toa Baja'
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

CREATE TABLE dbo.OptimizationRun (
    OptimizationRunId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TripId       INT NULL REFERENCES dbo.Trip(TripId),
    RouteId      INT NULL REFERENCES dbo.Route(RouteId),
    EngineLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),      -- Entity='OptimizerEngine'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='OptimizationRunStatus'
    RequestJson NVARCHAR(MAX) NOT NULL, ResponseJson NVARCHAR(MAX) NULL, ErrorMessage NVARCHAR(MAX) NULL,
    TotalDistanceKm DECIMAL(12,3) NULL, TotalDurationMin INT NULL, UnassignedCount INT NULL,
    StartedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), CompletedAtUtc DATETIME2 NULL
);
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
    IsActive     BIT NOT NULL DEFAULT 1
);
CREATE INDEX IX_VehicleDocument_Expiry ON dbo.VehicleDocument(ExpiryDate) WHERE IsActive = 1;
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
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.MaintenanceWorkOrder (
    WorkOrderId  INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    VehicleId    INT NOT NULL REFERENCES dbo.Vehicle(VehicleId),
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
    CONSTRAINT UQ_WorkOrder_Number UNIQUE (TenantId, Number)
);
GO

CREATE TABLE dbo.MaintenanceTask (
    MaintenanceTaskId INT IDENTITY(1,1) PRIMARY KEY,
    WorkOrderId  INT NOT NULL REFERENCES dbo.MaintenanceWorkOrder(WorkOrderId),
    Description  NVARCHAR(250) NOT NULL,
    PartCost DECIMAL(18,4) NULL, LaborCost DECIMAL(18,4) NULL,
    IsCompleted  BIT NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.FuelLog (
    FuelLogId    INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    VehicleId    INT NOT NULL REFERENCES dbo.Vehicle(VehicleId),
    DriverId     INT NULL REFERENCES dbo.Driver(DriverId),
    FillDateUtc  DATETIME2 NOT NULL, OdometerKm DECIMAL(12,1) NULL,
    Liters DECIMAL(10,3) NOT NULL, TotalCost DECIMAL(18,4) NOT NULL,
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Station NVARCHAR(150) NULL
);
CREATE INDEX IX_FuelLog_Vehicle ON dbo.FuelLog(VehicleId, FillDateUtc);
GO

CREATE TABLE dbo.DriverLicense (
    DriverLicenseId INT IDENTITY(1,1) PRIMARY KEY,
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    LicenseClassLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='LicenseClass'
    LicenseNumber NVARCHAR(60) NOT NULL, IssuedDate DATE NULL, ExpiryDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
CREATE INDEX IX_DriverLicense_Expiry ON dbo.DriverLicense(ExpiryDate) WHERE IsActive = 1;
GO

CREATE TABLE dbo.DriverCertification (
    DriverCertificationId INT IDENTITY(1,1) PRIMARY KEY,
    DriverId     INT NOT NULL REFERENCES dbo.Driver(DriverId),
    CertTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='CertificationType'
    CertNumber NVARCHAR(60) NULL, IssuedDate DATE NULL, ExpiryDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
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
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION
);
GO

CREATE TABLE dbo.PurchaseOrder (
    PurchaseOrderId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    SupplierId   INT NOT NULL REFERENCES dbo.Supplier(SupplierId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),         -- a qué almacén llega
    Number       NVARCHAR(40) NOT NULL,
    OrderDate    DATE NOT NULL, ExpectedDate DATE NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),       -- Entity='PurchaseOrderStatus'
    CurrencyLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_PurchaseOrder_Number UNIQUE (TenantId, Number)
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
    LineTotal    AS (QtyOrdered * UnitCost) PERSISTED
);
GO

/* =========================================================================
   CAPA 14 — WMS
   ========================================================================= */
CREATE TABLE dbo.Asn (
    AsnId        INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    ClientId     INT NULL REFERENCES dbo.Client(ClientId),                    -- cargo de un cliente (pasa por almacén, no es de Advance)
    PurchaseOrderId INT NULL REFERENCES dbo.PurchaseOrder(PurchaseOrderId),   -- compra de Advance a un proveedor (inventario propio)
    Reference NVARCHAR(80) NULL, ExpectedDate DATE NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='AsnStatus'
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.AsnLine (
    AsnLineId    INT IDENTITY(1,1) PRIMARY KEY,
    AsnId        INT NOT NULL REFERENCES dbo.Asn(AsnId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    ExpectedQty  DECIMAL(16,3) NOT NULL, LotNumber NVARCHAR(60) NULL
);
GO

CREATE TABLE dbo.ReceiptHeader (
    ReceiptHeaderId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    AsnId        INT NULL REFERENCES dbo.Asn(AsnId),
    DockId       INT NULL REFERENCES dbo.WarehouseDock(WarehouseDockId),
    ReceiptTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='ReceiptType'
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='ReceiptStatus'
    ReceivedAtUtc DATETIME2 NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_Receipt_Number UNIQUE (TenantId, Number)
);
GO

CREATE TABLE dbo.ReceiptLine (
    ReceiptLineId INT IDENTITY(1,1) PRIMARY KEY,
    ReceiptHeaderId INT NOT NULL REFERENCES dbo.ReceiptHeader(ReceiptHeaderId),
    AsnLineId    INT NULL REFERENCES dbo.AsnLine(AsnLineId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    ReceivedQty  DECIMAL(16,3) NOT NULL,
    StagingBinId INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId)
);
GO

CREATE TABLE dbo.WarehouseTask (
    WarehouseTaskId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    TaskTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),    -- Entity='WarehouseTaskType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='WarehouseTaskStatus'
    ProductId    INT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SerialId     INT NULL REFERENCES dbo.InventorySerial(SerialId),
    Quantity     DECIMAL(16,3) NULL,
    FromBinId    INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    ToBinId      INT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    RefEntityLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),       -- Entity='EntityType'
    RefId        INT NULL,
    AssignedToUserId INT NULL REFERENCES dbo.AspNetUsers(Id),
    Priority     INT NOT NULL DEFAULT 100,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAtUtc DATETIME2 NULL
);
CREATE INDEX IX_WarehouseTask_Queue ON dbo.WarehouseTask(WarehouseId, TaskTypeLookupId, StatusCodeId, Priority);
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

CREATE TABLE dbo.CycleCount (
    CycleCountId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='CycleCountStatus'
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CycleCount_Number UNIQUE (TenantId, Number)
);
GO

CREATE TABLE dbo.CycleCountLine (
    CycleCountLineId INT IDENTITY(1,1) PRIMARY KEY,
    CycleCountId INT NOT NULL REFERENCES dbo.CycleCount(CycleCountId),
    WarehouseBinId INT NOT NULL REFERENCES dbo.WarehouseBin(WarehouseBinId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    LotId        INT NULL REFERENCES dbo.InventoryLot(LotId),
    SystemQty    DECIMAL(16,3) NOT NULL, CountedQty DECIMAL(16,3) NULL,
    VarianceQty  AS (ISNULL(CountedQty,0) - SystemQty) PERSISTED,
    AdjustmentTxnId BIGINT NULL REFERENCES dbo.InventoryTransaction(InventoryTransactionId)
);
GO

/* =========================================================================
   CAPA 15 — CROSS-DOCKING
   ========================================================================= */
CREATE TABLE dbo.DockAppointment (
    DockAppointmentId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseDockId INT NOT NULL REFERENCES dbo.WarehouseDock(WarehouseDockId),
    DirectionLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),   -- Entity='DockDirection'
    AsnId        INT NULL REFERENCES dbo.Asn(AsnId),
    TripId       INT NULL REFERENCES dbo.Trip(TripId),
    ScheduledStartUtc DATETIME2 NOT NULL, ScheduledEndUtc DATETIME2 NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId)         -- Entity='AppointmentStatus'
);
CREATE INDEX IX_DockAppointment_Dock ON dbo.DockAppointment(WarehouseDockId, ScheduledStartUtc);
GO

CREATE TABLE dbo.CrossDockPlan (
    CrossDockPlanId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId  INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    Number       NVARCHAR(40) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),        -- Entity='CrossDockStatus'
    StagingZoneId INT NULL REFERENCES dbo.WarehouseZone(WarehouseZoneId),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CrossDockPlan_Number UNIQUE (TenantId, Number)
);
GO

CREATE TABLE dbo.CrossDockAllocation (
    CrossDockAllocationId INT IDENTITY(1,1) PRIMARY KEY,
    CrossDockPlanId INT NOT NULL REFERENCES dbo.CrossDockPlan(CrossDockPlanId),
    ReceiptLineId INT NOT NULL REFERENCES dbo.ReceiptLine(ReceiptLineId),
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    CargoLineId  INT NULL REFERENCES dbo.CargoLine(CargoLineId),
    AllocatedQty DECIMAL(16,3) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId)         -- Entity='AllocationStatus'
);
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
   CAPA 16C — EQUIPOS EN ALQUILER (módulo RENTAL_EQUIPMENT / RENTAL_BILLING)
   Activos serializados que salen semanas/meses con el cliente: ubicación,
   vencimiento del lease, mantenimiento y (si el módulo de facturación está
   encendido) cargos recurrentes configurables. El movimiento de salida/
   devolución se ata a una TransportOrder real — usa el mismo Despacho.
   ========================================================================= */
CREATE TABLE dbo.RentalAsset (
    RentalAssetId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    AssetTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='RentalAssetType' (glucómetro, silla de ruedas, concentrador O2...)
    SerialNumber NVARCHAR(80) NOT NULL,
    Model        NVARCHAR(120) NULL, Brand NVARCHAR(120) NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='RentalAssetStatus' (disponible/en alquiler/mantenimiento/perdido/retirado)
    CurrentWarehouseId INT NULL REFERENCES dbo.Warehouse(WarehouseId),      -- cuando está en almacén
    CurrentClientId INT NULL REFERENCES dbo.Client(ClientId),               -- cuando está en sitio del cliente
    CurrentLocationNote NVARCHAR(200) NULL,
    AcquisitionDate DATE NULL, AcquisitionCost DECIMAL(18,4) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION,
    CONSTRAINT UQ_RentalAsset_Serial UNIQUE (TenantId, SerialNumber)
);
CREATE INDEX IX_RentalAsset_Status ON dbo.RentalAsset(TenantId, StatusCodeId);
GO

CREATE TABLE dbo.RentalContract (
    RentalContractId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    RentalAssetId INT NOT NULL REFERENCES dbo.RentalAsset(RentalAssetId),
    ClientId     INT NOT NULL REFERENCES dbo.Client(ClientId),
    ContactId    INT NULL REFERENCES dbo.Contact(ContactId),
    LeaseStartDate DATE NOT NULL, LeaseEndDate DATE NULL,                   -- NULL = plazo abierto
    ExpectedReturnDate DATE NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='RentalContractStatus' (activo/devuelto/vencido/cancelado)
    DepositAmount DECIMAL(18,4) NULL, DepositReturned BIT NOT NULL DEFAULT 0,
    DeliveryOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId), -- entrega que lo sacó
    ReturnOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),   -- recogido que lo trajo de vuelta
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1, RowVersion ROWVERSION
);
CREATE INDEX IX_RentalContract_Overdue ON dbo.RentalContract(TenantId, StatusCodeId, ExpectedReturnDate);
GO

-- Mantenimiento del equipo — mismo patrón de MaintenanceWorkOrder (Flota), tabla propia para no tocar Vehicle.
CREATE TABLE dbo.RentalAssetMaintenance (
    RentalAssetMaintenanceId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    RentalAssetId INT NOT NULL REFERENCES dbo.RentalAsset(RentalAssetId),
    MaintenanceTypeLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- reutiliza Entity='MaintenanceType'
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),            -- reutiliza Entity='WorkOrderStatus'
    ScheduledDate DATE NULL, CompletedDate DATE NULL,
    Vendor       NVARCHAR(150) NULL, Cost DECIMAL(18,4) NULL,
    Notes        NVARCHAR(MAX) NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

-- Regla de cobro recurrente (solo aplica si el tenant tiene RENTAL_BILLING encendido)
CREATE TABLE dbo.RentalBillingRule (
    RentalBillingRuleId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    RentalContractId INT NOT NULL REFERENCES dbo.RentalContract(RentalContractId),
    FrequencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId), -- Entity='RentalBillingFrequency' (semanal/mensual/único)
    RateAmount   DECIMAL(18,4) NOT NULL,
    ProrateFirstPeriod BIT NOT NULL DEFAULT 1,
    LateFeeAmount DECIMAL(18,4) NULL, LateFeeGraceDays INT NULL,
    NextChargeDate DATE NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.RentalCharge (
    RentalChargeId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    RentalContractId INT NOT NULL REFERENCES dbo.RentalContract(RentalContractId),
    PeriodStart  DATE NOT NULL, PeriodEnd DATE NOT NULL,
    Amount       DECIMAL(18,4) NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),      -- Entity='RentalChargeStatus' (pendiente/facturado/pagado)
    InvoiceId    INT NULL REFERENCES dbo.Invoice(InvoiceId),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_RentalCharge_Contract ON dbo.RentalCharge(RentalContractId, StatusCodeId);
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
    CONSTRAINT UQ_PortalUser UNIQUE (TenantId, Email)
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
    Endpoint NVARCHAR(200) NULL, IdempotencyKey NVARCHAR(80) NULL,
    RequestJson NVARCHAR(MAX) NULL, ResponseCode INT NULL,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_IntegrationLog_Idem ON dbo.IntegrationMessageLog(TenantId, IdempotencyKey);
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
CREATE VIEW dbo.vw_LotGenealogy AS
SELECT t.TenantId, t.ProductId, p.Sku, p.Name AS ProductName,
       t.LotId, l.LotNumber, l.ExpiryDate, t.SerialId, s.SerialNumber,
       t.TxnTypeLookupId, txn.InternalCode AS TxnType, t.Quantity,
       t.FromWarehouseId, t.ToWarehouseId,
       t.RefEntityLookupId, ref.InternalCode AS RefEntity, t.RefId, t.CreatedAtUtc
FROM dbo.InventoryTransaction t
JOIN dbo.Product p ON p.ProductId = t.ProductId
LEFT JOIN dbo.InventoryLot l ON l.LotId = t.LotId
LEFT JOIN dbo.InventorySerial s ON s.SerialId = t.SerialId
LEFT JOIN dbo.LookupCode txn ON txn.LookupCodeId = t.TxnTypeLookupId
LEFT JOIN dbo.LookupCode ref ON ref.LookupCodeId = t.RefEntityLookupId;
GO

PRINT 'Estructura creada: ~121 tablas (incl. campos personalizados, informes y ciclo COD), en capas ordenadas por dependencias + vista de genealogía.';
GO
