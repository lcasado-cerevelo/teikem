-- Usuario de grabación del video: el administrador de plataforma (teikem+admin) entra a Advance Solutions como TenantAdmin
-- y usa la contraseña de la base de pruebas Teikem_e2e (se copia el hash entre bases; ninguna contraseña se escribe en el repositorio).
-- SOLO en la copia Teikem_video*. Idempotente.
SET QUOTED_IDENTIFIER ON;
IF DB_NAME() NOT LIKE N'Teikem[_]video%' BEGIN RAISERROR(N'Solo se corre en Teikem_video*.', 16, 1); SET NOEXEC ON; END
IF DB_ID(N'Teikem_e2e') IS NULL BEGIN RAISERROR(N'Falta la base Teikem_e2e (de ahí sale el hash).', 16, 1); SET NOEXEC ON; END
GO
DECLARE @hash NVARCHAR(MAX), @stamp NVARCHAR(MAX);
SELECT @hash = PasswordHash, @stamp = SecurityStamp FROM Teikem_e2e.dbo.AspNetUsers WHERE Email = N'teikem+admin@cerevelo.com';
UPDATE dbo.AspNetUsers SET PasswordHash = @hash, SecurityStamp = ISNULL(@stamp, SecurityStamp), LockoutEnd = NULL, AccessFailedCount = 0
 WHERE Email = N'teikem+admin@cerevelo.com';
DECLARE @uid INT = (SELECT Id FROM dbo.AspNetUsers WHERE Email = N'teikem+admin@cerevelo.com');
IF @uid IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.UserTenant WHERE UserId = @uid AND TenantId = 2)
    INSERT dbo.UserTenant (UserId, TenantId, IsDefault, StatusCodeId, JoinedAtUtc, MfaRequired, MessagingMode)
    SELECT @uid, 2, 0, StatusCodeId, SYSUTCDATETIME(), 0, MessagingMode FROM dbo.UserTenant WHERE UserId = @uid AND TenantId = 1;
IF @uid IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.UserRole WHERE UserId = @uid AND TenantId = 2)
    INSERT dbo.UserRole (UserId, RoleId, TenantId, GrantedAtUtc) VALUES (@uid, 16, 2, SYSUTCDATETIME());
UPDATE dbo.UserTenant SET MfaRequired = 0 WHERE UserId = @uid;
UPDATE dbo.AspNetUsers SET TwoFactorEnabled = 0 WHERE Id = @uid;   -- sin código MFA al confirmar la identidad (copia de grabación)
SELECT @uid AS UserId, (SELECT COUNT(*) FROM dbo.UserTenant WHERE UserId = @uid) AS Compañías;
GO
SET NOEXEC OFF;
