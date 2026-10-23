/* ============================================================================
   Axiscare en Advance Depot — integración por correo de los avisos de llegada y de salida
   Generado por scripts/integraciones/generar-sql.cjs (no se edita a mano: se cambia el generador o readerPresets.json y se vuelve a generar).
   Crea: la plantilla de importación de apoyo, y la integración «Axiscare avisos por correo» con tres lectores:
     · «Despacho de mercancía» (el título del documento) → aviso de SALIDA;  · «Envío de mercancía» (el recibo) → aviso de LLEGADA;  · «Factura de Drive Medical» → aviso de LLEGADA.
   Los productos de los documentos (columna «Número de ítems») deben existir con ese mismo SKU como productos activos del cliente Axiscare, o el aviso va a la bandeja.

   IDEMPOTENTE: se puede correr otra vez; lo que ya existe no se toca ni se duplica (para cambiar una integración ya creada se edita en la pantalla Integraciones).
   NO es parte de logistica-db-update.sql: son datos de ESTA operación, no estructura. Antes de correrlo, mire los parámetros de arriba (DECLARE).
   Las integraciones nacen APAGADAS (IsActive = 0) y con el servidor del buzón en marcadores «CAMBIAR»: en Integraciones → Editar ponga el servidor, el usuario y la
   CONTRASEÑA del buzón (la contraseña se guarda cifrada por la aplicación; no se puede poner desde SQL), los remitentes permitidos y préndala.
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

-- ----------------------------------------------------------------------------- PARÁMETROS
DECLARE @TenantName   NVARCHAR(200) = N'Advance Depot';
DECLARE @ClientCode   NVARCHAR(50)  = N'AXISCARE';
DECLARE @WarehouseName NVARCHAR(200) = N'Almac' + NCHAR(233) + N'n Depot';   -- el almacén donde entran y de donde salen los avisos
-- -----------------------------------------------------------------------------

DECLARE @TenantId INT = (SELECT TOP 1 TenantId FROM dbo.Tenant WHERE Name = @TenantName);
IF @TenantId IS NULL THROW 50001, N'No existe la compañía indicada en @TenantName.', 1;
DECLARE @ClientId INT = (SELECT TOP 1 ClientId FROM dbo.Client WHERE TenantId = @TenantId AND Code = @ClientCode);
IF @ClientId IS NULL THROW 50002, N'No existe el cliente indicado en @ClientCode para esa compañía.', 1;
DECLARE @ClientPublicId UNIQUEIDENTIFIER = (SELECT PublicId FROM dbo.Client WHERE ClientId = @ClientId);

DECLARE @WarehouseId INT = (SELECT TOP 1 WarehouseId FROM dbo.Warehouse WHERE TenantId = @TenantId AND Name = @WarehouseName AND IsActive = 1);
IF @WarehouseId IS NULL THROW 50003, N'No existe un almacén activo con el nombre de @WarehouseName en esa compañía.', 1;
DECLARE @WarehousePublicId UNIQUEIDENTIFIER = (SELECT PublicId FROM dbo.Warehouse WHERE WarehouseId = @WarehouseId);

-- Plantilla de apoyo: los lectores de avisos entregan sus datos por POSICIÓN de columna (1 referencia, 2 SKU, 3 cantidad, 4 destino, 5 fecha, 6 lote), sin encabezado y con coma.
IF NOT EXISTS (SELECT 1 FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'Avisos por documento (por posición)')
    INSERT dbo.ImportTemplate (TenantId, Kind, Name, Delimiter, HasHeader, ColumnsJson, MultiLine, IsActive)
    VALUES (@TenantId, 'ORDER', N'Avisos por documento (por posición)', N',', 0, N'[{"position":1,"field":"reference"}]', 0, 1);
DECLARE @TemplateId INT = (SELECT TOP 1 ImportTemplateId FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'Avisos por documento (por posición)');

-- ---------------------------------------------------------------------------- Integración «Axiscare avisos por correo»
IF EXISTS (SELECT 1 FROM dbo.Integration WHERE TenantId = @TenantId AND Name = N'Axiscare avisos por correo')
    PRINT N'La integración «Axiscare avisos por correo» ya existe: no se toca.';
ELSE
BEGIN
    BEGIN TRAN;
    DECLARE @pid UNIQUEIDENTIFIER = NEWID(), @roleId INT, @userId INT;
    DECLARE @cfg NVARCHAR(MAX) = N'{"host":"imap.CAMBIAR.com","port":993,"useSsl":true,"username":"buzon@CAMBIAR.com","mailbox":"INBOX","processedFolder":"Procesados","requireAttachment":false,"attachmentMode":"ALL","readBody":false,"readPdf":false,"readers":[{"name":"Despacho de mercancía","source":"PDF","target":"EXIT_NOTICE","when":[{"scope":"TEXT","operator":"CONTAINS","value":"Despacho de Mercancia"}],"fields":[{"position":1,"kind":"BLOCK","required":true,"transform":"NONE","start":"NUMERO DE ITEMS","separator":"\\t","columns":[{"position":2,"index":1},{"position":3,"index":2},{"position":4,"index":3}],"lineRegex":"^\\d+\\t.+?\\t([A-Za-z0-9][A-Za-z0-9-]*\\d[A-Za-z0-9-]*)\\t(\\d+)\\t(?:\\d+\\t)?(.+)$","repeat":false,"fromHeader":false}],"firstOnly":false,"mode":"SUGGEST","warehousePublicId":"{{WAREHOUSE}}"},{"name":"Envío de mercancía","source":"PDF","target":"ARRIVAL_NOTICE","when":[{"scope":"TEXT","operator":"CONTAINS","value":"ENVIO DE MERCANCIA PARA"}],"fields":[{"position":1,"kind":"BLOCK","required":true,"transform":"NONE","start":"NUMERO DE ITEMS","separator":"\\t","columns":[{"position":2,"index":1},{"position":3,"index":2}],"lineRegex":"^\\d+\\t.+?\\t\\d+\\w*\\t([A-Za-z0-9][A-Za-z0-9-]*\\d[A-Za-z0-9-]*)\\t(\\d[\\d,]*)\\w*\\t\\d+$","repeat":false,"fromHeader":false}],"firstOnly":false,"mode":"SUGGEST","warehousePublicId":"{{WAREHOUSE}}"},{"name":"Factura de Drive Medical","source":"PDF","target":"ARRIVAL_NOTICE","when":[{"scope":"TEXT","operator":"CONTAINS","value":"Invoice Number"},{"scope":"TEXT","operator":"CONTAINS","value":"Delivery Number"}],"fields":[{"position":1,"kind":"LABEL","label":"Invoice Number","required":true,"transform":"NONE","repeat":false,"fromHeader":false},{"position":2,"kind":"BLOCK","required":true,"transform":"NONE","start":"Invoice Details","separator":"\\t","columns":[{"position":2,"index":1},{"position":3,"index":2}],"lineRegex":"^\\d{4}\\t([A-Za-z0-9][A-Za-z0-9-]*)(?:\\t[^\\t\\d]{0,3})?(?:\\t(\\d[\\d,]*) ?(?:EA|CV|CS|BX|PK))?(?:\\t|$)","repeat":true,"fromHeader":false}],"firstOnly":false,"mode":"SUGGEST","warehousePublicId":"{{WAREHOUSE}}"}]}';
    SET @cfg = REPLACE(@cfg, N'{{CLIENT}}', LOWER(CONVERT(NVARCHAR(36), @ClientPublicId)));
    SET @cfg = REPLACE(@cfg, N'{{WAREHOUSE}}', ISNULL(LOWER(CONVERT(NVARCHAR(36), @WarehousePublicId)), N''));

    INSERT dbo.Role (TenantId, Name, DescriptionJson, IsSystem, IsActive)
    VALUES (@TenantId, N'Integración: Axiscare avisos por correo', N'{"es":"Rol de la cuenta de servicio de una integración","en":"Role of an integration''s service account"}', 0, 1);
    SET @roleId = SCOPE_IDENTITY();
    INSERT dbo.RolePermission (RoleId, PermissionId)
    SELECT @roleId, PermissionId FROM dbo.Permission WHERE Code IN (N'orders.view', N'orders.create', N'locations.read', N'locations.create', N'clients.read');

    DECLARE @email NVARCHAR(256) = N'integration-' + REPLACE(LOWER(CONVERT(NVARCHAR(36), @pid)), N'-', N'') + N'@service.teikem.local';
    INSERT dbo.AspNetUsers (UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, SecurityStamp, ConcurrencyStamp, FullName, DefaultTenantId, UserKindLookupId,
                            IsActive, OnboardingRequired, MustChangePassword, EmailVerifiedUtc)
    VALUES (@email, UPPER(@email), @email, UPPER(@email), 1, REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), CONVERT(NVARCHAR(36), NEWID()), N'Integración Axiscare avisos por correo', @TenantId,
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'UserKind' AND InternalCode = N'SERVICE'), 1, 0, 0, SYSUTCDATETIME());
    SET @userId = SCOPE_IDENTITY();
    INSERT dbo.UserTenant (UserId, TenantId, IsDefault, StatusCodeId, JoinedAtUtc)
    VALUES (@userId, @TenantId, 1, (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'MembershipStatus' AND InternalCode = N'ACTIVE'), SYSUTCDATETIME());
    INSERT dbo.UserRole (UserId, RoleId, TenantId) VALUES (@userId, @roleId, @TenantId);

    INSERT dbo.Integration (PublicId, TenantId, Name, Description, Direction, ClientId, ConnectorKind, ConnectorConfigJson, ImportTemplateId, ScheduleKind, IntervalSeconds, ScopesJson,
                            ServiceUserId, ServiceRoleId, NotifyUserIdsJson, RetentionDays, IsActive)
    VALUES (@pid, @TenantId, N'Axiscare avisos por correo', N'Avisos de llegada y de salida de Axiscare leídos de los documentos que llegan por correo (despacho, envío/recibo y facturas de Drive Medical).', 'INBOUND', @ClientId, 'EMAIL', @cfg, @TemplateId, 'INTERVAL', 300, N'["files.orders_import"]', @userId, @roleId, N'[]', 30, 0);
    COMMIT;
    PRINT N'Integración «Axiscare avisos por correo» creada (apagada): falta el buzón.';
END
GO
