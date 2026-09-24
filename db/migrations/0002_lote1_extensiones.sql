/* ============================================================================
   TEIKEM — 0002 EXTENSIONES LOTE 1 (capas transversales A-I + módulo 0B)
   Se corre DESPUÉS de logistica-db-estructura.sql y logistica-db-seed.sql.
   Contiene lo que el documento maestro describe y el script de estructura
   todavía no tiene. Idempotente (guarded / MERGE).
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ---------- Tenant: defaults de captura, política MFA/sesión, marca ---------- */
IF COL_LENGTH('dbo.Tenant', 'DefaultServiceTypeLookupId') IS NULL
    ALTER TABLE dbo.Tenant ADD DefaultServiceTypeLookupId INT NULL CONSTRAINT FK_Tenant_DefaultServiceType FOREIGN KEY REFERENCES dbo.LookupCode(LookupCodeId);
IF COL_LENGTH('dbo.Tenant', 'DefaultPackageTypeLookupId') IS NULL
    ALTER TABLE dbo.Tenant ADD DefaultPackageTypeLookupId INT NULL CONSTRAINT FK_Tenant_DefaultPackageType FOREIGN KEY REFERENCES dbo.LookupCode(LookupCodeId);
IF COL_LENGTH('dbo.Tenant', 'MfaRequired') IS NULL
    ALTER TABLE dbo.Tenant ADD MfaRequired BIT NOT NULL CONSTRAINT DF_Tenant_MfaRequired DEFAULT 0;
IF COL_LENGTH('dbo.Tenant', 'Aal2WindowMinutes') IS NULL
    ALTER TABLE dbo.Tenant ADD Aal2WindowMinutes INT NOT NULL CONSTRAINT DF_Tenant_Aal2Window DEFAULT 30;
IF COL_LENGTH('dbo.Tenant', 'SessionDays') IS NULL
    ALTER TABLE dbo.Tenant ADD SessionDays INT NOT NULL CONSTRAINT DF_Tenant_SessionDays DEFAULT 30;
IF COL_LENGTH('dbo.Tenant', 'BrandingJson') IS NULL
    ALTER TABLE dbo.Tenant ADD BrandingJson NVARCHAR(MAX) NULL;
GO

/* ---------- ModuleDefinition: núcleo no apagable + catálogo completo (13) ---------- */
IF COL_LENGTH('dbo.ModuleDefinition', 'IsCore') IS NULL
    ALTER TABLE dbo.ModuleDefinition ADD IsCore BIT NOT NULL CONSTRAINT DF_ModuleDefinition_IsCore DEFAULT 0;
GO
MERGE dbo.ModuleDefinition AS t
USING (VALUES
 ('CATALOG','Catálogo','Catalog','Clientes y contratos, choferes y tarifas, flota','Catalogo',NULL,85,1),
 ('ANALYTICS','Análisis','Analytics','Vistas, campos personalizados, indicadores y gráficos','Analisis',NULL,88,0),
 ('SYSTEM','Sistema','System','Roles y usuarios, integraciones, seguridad y ajustes','Sistema',NULL,95,1)
) AS s(ModuleKey,Name,NameEn,Description,Category,DependsOnModuleKey,SortOrder,IsCore)
ON t.ModuleKey = s.ModuleKey
WHEN NOT MATCHED THEN INSERT (ModuleKey,Name,NameEn,Description,Category,DependsOnModuleKey,SortOrder,IsActive,IsCore)
    VALUES (s.ModuleKey,s.Name,s.NameEn,s.Description,s.Category,s.DependsOnModuleKey,s.SortOrder,1,s.IsCore);
UPDATE dbo.ModuleDefinition SET IsCore = 1 WHERE ModuleKey IN ('LTL_GROUND','SYSTEM');
UPDATE dbo.ModuleDefinition SET DependsOnModuleKey = 'ANALYTICS' WHERE ModuleKey = 'CUSTOM_FIELDS' AND DependsOnModuleKey IS NULL;
GO

/* ---------- Catálogo de listas por tenant (CatalogDomain/LookupCode con TenantId opcional) ---------- */
IF COL_LENGTH('dbo.CatalogDomain', 'TenantId') IS NULL
    ALTER TABLE dbo.CatalogDomain ADD TenantId INT NULL CONSTRAINT FK_CatalogDomain_Tenant FOREIGN KEY REFERENCES dbo.Tenant(TenantId);
IF COL_LENGTH('dbo.LookupCode', 'TenantId') IS NULL
    ALTER TABLE dbo.LookupCode ADD TenantId INT NULL CONSTRAINT FK_LookupCode_Tenant FOREIGN KEY REFERENCES dbo.Tenant(TenantId);
GO

/* ---------- Permisos extra por usuario (excepción puntual, sin tocar un rol) ---------- */
IF OBJECT_ID('dbo.UserPermission') IS NULL
BEGIN
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
END
GO

/* ---------- ReportDefinition: informes por default + fuentes combinadas ---------- */
IF COL_LENGTH('dbo.ReportDefinition', 'IsSystem') IS NULL
    ALTER TABLE dbo.ReportDefinition ADD IsSystem BIT NOT NULL CONSTRAINT DF_ReportDefinition_IsSystem DEFAULT 0;
IF COL_LENGTH('dbo.ReportDefinition', 'SecondaryJson') IS NULL
    ALTER TABLE dbo.ReportDefinition ADD SecondaryJson NVARCHAR(MAX) NULL;
GO

/* ---------- Módulo H: IndicatorDefinition ---------- */
IF OBJECT_ID('dbo.IndicatorDefinition') IS NULL
BEGIN
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
END
GO
IF OBJECT_ID('dbo.IndicatorShare') IS NULL
BEGIN
    CREATE TABLE dbo.IndicatorShare (
        IndicatorShareId INT IDENTITY(1,1) PRIMARY KEY,
        IndicatorDefinitionId INT NOT NULL REFERENCES dbo.IndicatorDefinition(IndicatorDefinitionId),
        RoleId INT NULL REFERENCES dbo.Role(RoleId),
        UserId INT NULL REFERENCES dbo.AspNetUsers(Id)
    );
END
GO

/* ---------- Módulo I: ChartDefinition ---------- */
IF OBJECT_ID('dbo.ChartDefinition') IS NULL
BEGIN
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
        CONSTRAINT UQ_ChartDefinition UNIQUE (TenantId, Name)
    );
    CREATE INDEX IX_ChartDefinition_Tenant ON dbo.ChartDefinition(TenantId) WHERE IsActive = 1;
END
GO
IF OBJECT_ID('dbo.ChartShare') IS NULL
BEGIN
    CREATE TABLE dbo.ChartShare (
        ChartShareId INT IDENTITY(1,1) PRIMARY KEY,
        ChartDefinitionId INT NOT NULL REFERENCES dbo.ChartDefinition(ChartDefinitionId),
        RoleId INT NULL REFERENCES dbo.Role(RoleId),
        UserId INT NULL REFERENCES dbo.AspNetUsers(Id)
    );
END
GO

/* ---------- Preferencias de visualización por usuario (Pulso del día por usuario) ---------- */
IF OBJECT_ID('dbo.UserAnalyticsPreference') IS NULL
BEGIN
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
        CONSTRAINT CK_UserAnalyticsPreference_Target CHECK (
            (IndicatorDefinitionId IS NOT NULL AND ChartDefinitionId IS NULL) OR
            (IndicatorDefinitionId IS NULL AND ChartDefinitionId IS NOT NULL))
    );
    CREATE UNIQUE INDEX UQ_UserAnalyticsPreference_Indicator ON dbo.UserAnalyticsPreference(UserId, IndicatorDefinitionId) WHERE IndicatorDefinitionId IS NOT NULL;
    CREATE UNIQUE INDEX UQ_UserAnalyticsPreference_Chart ON dbo.UserAnalyticsPreference(UserId, ChartDefinitionId) WHERE ChartDefinitionId IS NOT NULL;
END
GO

/* ---------- Dominios y lookups nuevos ---------- */
MERGE dbo.CatalogDomain AS t
USING (VALUES
 ('AggregateFn',1,N'{"es":"Función de agregación","en":"Aggregate function"}'),
 ('BusinessModule',1,N'{"es":"Módulo de negocio","en":"Business module"}'),
 ('DateRangeMode',1,N'{"es":"Rango de fecha","en":"Date range"}'),
 ('PackageType',1,N'{"es":"Tipo de paquete","en":"Package type"}')
) AS s(DomainKey,Scope,LabelJson)
ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN INSERT (DomainKey,Scope,LabelJson,IsSystem,IsActive) VALUES (s.DomainKey,s.Scope,s.LabelJson,1,1);
GO

IF OBJECT_ID('tempdb..#L2') IS NOT NULL DROP TABLE #L2;
CREATE TABLE #L2 (Entity NVARCHAR(60), Code NVARCHAR(40), Es NVARCHAR(120), En NVARCHAR(120), Srt INT);
INSERT INTO #L2 VALUES
('AggregateFn','COUNT','Cantidad','Count',1),('AggregateFn','SUM','Suma','Sum',2),('AggregateFn','AVG','Promedio','Average',3),('AggregateFn','MIN','Mínimo','Minimum',4),('AggregateFn','MAX','Máximo','Maximum',5),
('BusinessModule','OPERATIONS','Operación','Operations',1),('BusinessModule','WAREHOUSE','Almacén','Warehouse',2),('BusinessModule','ACCOUNTING','Contabilidad','Accounting',3),
('DateRangeMode','LAST7','Últimos 7 días','Last 7 days',1),('DateRangeMode','LAST30','Últimos 30 días','Last 30 days',2),('DateRangeMode','THIS_MONTH','Este mes','This month',3),('DateRangeMode','CUSTOM','Rango personalizado','Custom range',4),('DateRangeMode','ALL','Todo el tiempo','All time',5),
('PackageType','BOX','Caja','Box',1),('PackageType','ENVELOPE','Sobre','Envelope',2),('PackageType','PALLET','Tarima','Pallet',3),
('ReportChartType','DONUT','Dona','Donut',5),
('PermissionCategory','ANALYTICS','Análisis','Analytics',10),
('SecurityEventType','PASSWORD_CHANGE','Cambio de contraseña','Password change',8),('SecurityEventType','LOCKOUT','Bloqueo de cuenta','Account lockout',9),
('SecurityEventType','API_CREDENTIAL','Credencial de API','API credential',10),('SecurityEventType','TENANT_SWITCH','Cambio de compañía','Tenant switch',11),
('EntityType','TENANT','Compañía','Tenant',40),('EntityType','USER','Usuario','User',41),('EntityType','ROLE','Rol','Role',42),
('EntityType','LOOKUP_CODE','Valor de catálogo','Lookup value',43),('EntityType','CATALOG_DOMAIN','Lista de catálogo','Catalog list',44),
('EntityType','STATUS_CODE','Estatus','Status',45),('EntityType','STATUS_CONFIG','Configuración de estatus','Status configuration',46),
('EntityType','CONTACT_POINT','Contacto','Contact point',47),('EntityType','CUSTOM_FIELD_DEFINITION','Campo personalizado','Custom field',48),
('EntityType','REPORT_DEFINITION','Vista / informe','Report',49),('EntityType','INDICATOR_DEFINITION','Indicador','Indicator',50),
('EntityType','CHART_DEFINITION','Gráfico','Chart',51),('EntityType','TENANT_MODULE','Módulo de compañía','Tenant module',52),
('EntityType','AUDIT_LOG','Bitácora de cambios','Audit log',53),('EntityType','SECURITY_EVENT','Evento de seguridad','Security event',54);

MERGE dbo.LookupCode AS t
USING #L2 AS s ON t.Entity = s.Entity AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (s.Entity, s.Code, N'{"es":"'+s.Es+'","en":"'+s.En+'"}', s.Srt, 1, 1);
GO

PRINT '0002_lote1_extensiones: aplicado.';
GO
