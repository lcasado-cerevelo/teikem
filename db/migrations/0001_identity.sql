/* ============================================================================
   TEIKEM — 0001 IDENTIDAD (ASP.NET Core Identity, llaves INT)
   Se corre ANTES de Diseño/logistica-db-estructura.sql (cuyos CREATE de
   AspNetUsers/AspNetRoles van guarded y por lo tanto no chocan).
   Equivale a la migración inicial de Identity: mismas tablas, columnas e índices
   que genera IdentityDbContext<ApplicationUser, ApplicationRole, int>, más las
   columnas de aplicación del esquema (FullName, DefaultTenantId, UserKindLookupId,
   IsActive, CreatedAtUtc, LastLoginUtc) e IsPlatformAdmin (Lote 1).
   Idempotente (guarded).
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

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
        IsPlatformAdmin      BIT NOT NULL CONSTRAINT DF_AspNetUsers_IsPlatformAdmin DEFAULT 0
    );
    CREATE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail);
    CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
END
ELSE IF COL_LENGTH('dbo.AspNetUsers', 'IsPlatformAdmin') IS NULL
BEGIN
    ALTER TABLE dbo.AspNetUsers ADD IsPlatformAdmin BIT NOT NULL CONSTRAINT DF_AspNetUsers_IsPlatformAdmin DEFAULT 0;
END
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

PRINT '0001_identity: tablas de Identity listas.';
GO
