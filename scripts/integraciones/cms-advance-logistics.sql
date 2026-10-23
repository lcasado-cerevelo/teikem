/* ============================================================================
   CMS (Clinical Medical Services) en Advance Logistics — integración por correo de las hojas de ruta
   Generado por scripts/integraciones/generar-sql.cjs (no se edita a mano: se cambia el generador o readerPresets.json y se vuelve a generar).
   Crea: la dirección fija de CMS (consignatario «CMS»), las zonas «ERD Pick Up 1» y «ERD Pick Up 3» (las rutas de CMS; agregue las demás), la marca «el cliente trae su número de envío»,
   la plantilla de importación «CMS recogidas (hoja de ruta)» y la integración «CMS rutas por correo» con dos lectores:
     · «Hoja de ruta de CMS» → ÓRDENES de recogida (la dirección del paciente es la recogida, CMS el consignatario, la ruta es la zona; el Order # es el número de la orden);
     · «Ruta completada de CMS» → PROPUESTAS de cierre («Recogida») que una persona confirma en Integraciones → Propuestas (modo Sugerir).

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
DECLARE @TenantName      NVARCHAR(200) = N'Advance Logistics';
DECLARE @ClientCode      NVARCHAR(50)  = N'10297';           -- CLINICAL MEDICAL SERVICE
DECLARE @ServiceTypeCode NVARCHAR(50)  = N'PICK_UP';         -- el tipo de servicio de las órdenes de CMS (por ejemplo PICK_UP, o el propio de la compañía: PICK_UP_10029)
DECLARE @PackageTypeCode NVARCHAR(50)  = N'BOX';
-- -----------------------------------------------------------------------------

DECLARE @TenantId INT = (SELECT TOP 1 TenantId FROM dbo.Tenant WHERE Name = @TenantName);
IF @TenantId IS NULL THROW 50001, N'No existe la compañía indicada en @TenantName.', 1;
DECLARE @ClientId INT = (SELECT TOP 1 ClientId FROM dbo.Client WHERE TenantId = @TenantId AND Code = @ClientCode);
IF @ClientId IS NULL THROW 50002, N'No existe el cliente indicado en @ClientCode para esa compañía.', 1;
DECLARE @ClientPublicId UNIQUEIDENTIFIER = (SELECT PublicId FROM dbo.Client WHERE ClientId = @ClientId);

IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'ServiceType' AND InternalCode = @ServiceTypeCode AND IsActive = 1) THROW 50004, N'No existe el tipo de servicio de @ServiceTypeCode.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'PackageType' AND InternalCode = @PackageTypeCode AND IsActive = 1) THROW 50005, N'No existe el tipo de paquete de @PackageTypeCode.', 1;
DECLARE @WarehousePublicId UNIQUEIDENTIFIER = NULL;   -- CMS no usa almacén

-- El Order # de CMS es el número de la orden: el cliente trae su propio número de envío.
UPDATE dbo.Client SET ClientAssignsOrderNumber = 1 WHERE ClientId = @ClientId AND ClientAssignsOrderNumber = 0;

-- El consignatario fijo de las órdenes de CMS (la dirección que sale en el encabezado de sus documentos).
IF NOT EXISTS (SELECT 1 FROM dbo.Location WHERE TenantId = @TenantId AND ClientId = @ClientId AND Code = N'CMS')
    INSERT dbo.Location (TenantId, ClientId, Code, Name, LocationTypeLookupId, Line1, City, State, PostalCode, CountryLookupId, IsActive)
    VALUES (@TenantId, @ClientId, N'CMS', N'Clinical Medical Services', (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'LocationType' AND InternalCode = N'DELIVERY'),
            N'Carrera PR-3 KM 9.5 Ave 65 Infanteria', N'Carolina', N'PR', N'00985', (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'Country' AND InternalCode = N'PR'), 1);

-- Las rutas de CMS son zonas de despacho (el nombre debe ser EXACTAMENTE el que sale en la hoja: «DRIVER: ERD Pick Up 1»). Asigne a cada zona los choferes que la trabajan en Flota.
IF NOT EXISTS (SELECT 1 FROM dbo.DispatchZone WHERE TenantId = @TenantId AND Name = N'ERD Pick Up 1') INSERT dbo.DispatchZone (TenantId, Code, Name, IsActive) VALUES (@TenantId, N'ERD1', N'ERD Pick Up 1', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.DispatchZone WHERE TenantId = @TenantId AND Name = N'ERD Pick Up 3') INSERT dbo.DispatchZone (TenantId, Code, Name, IsActive) VALUES (@TenantId, N'ERD3', N'ERD Pick Up 3', 1);

-- Plantilla de importación de las hojas de ruta (sin encabezado, coma, varias líneas por envío): 1 número de orden, 2 número de envío, 3-6 la recogida (paciente, dirección, pueblo, ZIP),
-- 7 teléfono, 8 fecha de entrega, 9 notas, 10 zona (la ruta), 11 descripción de la pieza. El consignatario («CMS»), el tipo de servicio y el de paquete van por defecto.
DECLARE @Defaults NVARCHAR(MAX) = N'{"consigneeCode":"CMS","serviceType":"' + @ServiceTypeCode + N'","packageType":"' + @PackageTypeCode + N'"}';
IF NOT EXISTS (SELECT 1 FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'CMS recogidas (hoja de ruta)')
    INSERT dbo.ImportTemplate (TenantId, Kind, Name, ClientId, Delimiter, HasHeader, ColumnsJson, DefaultsJson, MultiLine, LineRulesJson, IsActive)
    VALUES (@TenantId, 'ORDER', N'CMS recogidas (hoja de ruta)', @ClientId, N',', 0, N'[{"position":1,"field":"orderNumber"},{"position":2,"field":"waybillNumber"},{"position":3,"field":"pickupName"},{"position":4,"field":"pickupLine1"},{"position":5,"field":"pickupCity"},{"position":6,"field":"pickupPostalCode"},{"position":7,"field":"contactPhone"},{"position":8,"field":"requestedDate"},{"position":9,"field":"notes"},{"position":10,"field":"zone"},{"position":11,"field":"description"}]', @Defaults, 1, N'[]', 1);
DECLARE @TemplateId INT = (SELECT TOP 1 ImportTemplateId FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'CMS recogidas (hoja de ruta)');

-- ---------------------------------------------------------------------------- Integración «CMS rutas por correo»
IF EXISTS (SELECT 1 FROM dbo.Integration WHERE TenantId = @TenantId AND Name = N'CMS rutas por correo')
    PRINT N'La integración «CMS rutas por correo» ya existe: no se toca.';
ELSE
BEGIN
    BEGIN TRAN;
    DECLARE @pid UNIQUEIDENTIFIER = NEWID(), @roleId INT, @userId INT;
    DECLARE @cfg NVARCHAR(MAX) = N'{"host":"imap.CAMBIAR.com","port":993,"useSsl":true,"username":"buzon@CAMBIAR.com","mailbox":"INBOX","processedFolder":"Procesados","requireAttachment":false,"attachmentMode":"ALL","readBody":false,"readPdf":false,"readers":[{"name":"Hoja de ruta de CMS","source":"PDF","target":"ORDER","when":[{"scope":"TEXT","operator":"CONTAINS","value":"ROUTE SHEET #"},{"scope":"TEXT","operator":"CONTAINS","value":"Order Entry Date"}],"fields":[{"position":1,"kind":"REGEX","pattern":"Order #:\\s*(\\d+)","required":true,"transform":"NONE","repeat":false,"fromHeader":false},{"position":2,"kind":"REGEX","pattern":"Order #:\\s*(\\d+)","required":true,"transform":"NONE","repeat":false,"fromHeader":false},{"position":3,"kind":"REGEX","pattern":"^Name:\\s*([^\\t\\n]+)","required":true,"transform":"NONE","repeat":false,"fromHeader":false},{"position":4,"kind":"REGEX","pattern":"^Address:\\s*([^\\t\\n]+)","required":true,"transform":"NONE","repeat":false,"fromHeader":false},{"position":5,"kind":"REGEX","pattern":"^\\s*City:\\s*([^,\\t\\n]+)","required":false,"transform":"NONE","repeat":false,"fromHeader":false},{"position":6,"kind":"REGEX","pattern":"City:[^\\n]*?(\\d{5})","required":false,"transform":"NONE","repeat":false,"fromHeader":false},{"position":7,"kind":"REGEX","pattern":"City:[^\\n]*\\n\\s*Phone #:\\s*([\\d()\\- ]+)","required":false,"transform":"NONE","repeat":false,"fromHeader":false},{"position":8,"kind":"REGEX","pattern":"Delivery Date:\\s*(\\d{1,2}/\\d{1,2}/\\d{4})","required":false,"transform":"NONE","repeat":false,"fromHeader":false},{"position":9,"kind":"REGEX","pattern":"Delivery Instructions:[ \\t]*\\n[ \\t]*([^\\n]+)","required":false,"transform":"SPACES","repeat":false,"fromHeader":false},{"position":9,"kind":"REGEX","pattern":"^New DME\\t[^\\t\\n]*\\t[^\\t\\n]*\\t([^\\t\\n]+)\\tNOTES","required":false,"transform":"NONE","repeat":false,"fromHeader":false,"appendWith":" | "},{"position":10,"kind":"REGEX","pattern":"DRIVER:\\s*([^\\n]+)","required":false,"transform":"SPACES","repeat":false,"fromHeader":true},{"position":11,"kind":"BLOCK","required":true,"transform":"NONE","start":"ORDER TYPE","separator":"\\t","columns":[{"position":11,"index":1}],"lineRegex":"^Pick Up\\t[^\\t]+\\t[^\\t]+\\t(.+?)\\t\\S+\\t\\S+$","repeat":false,"fromHeader":false}],"firstOnly":false,"recordStart":"Order #:","mode":"SUGGEST"},{"name":"Ruta completada de CMS","source":"PDF","target":"ORDER_COMPLETION","when":[{"scope":"TEXT","operator":"CONTAINS","value":"REFERRAL #"},{"scope":"TEXT","operator":"CONTAINS","value":"HCPC Description"}],"fields":[{"position":1,"kind":"BLOCK","required":true,"transform":"NONE","start":"REFERRAL #","separator":"\\t","columns":[{"position":1,"index":1}],"lineRegex":"^\\w\\t(\\d{8,})\\tPU\\t","repeat":true,"fromHeader":false}],"firstOnly":false,"mode":"SUGGEST","toStatus":"PICKUP"}]}';
    SET @cfg = REPLACE(@cfg, N'{{CLIENT}}', LOWER(CONVERT(NVARCHAR(36), @ClientPublicId)));
    SET @cfg = REPLACE(@cfg, N'{{WAREHOUSE}}', ISNULL(LOWER(CONVERT(NVARCHAR(36), @WarehousePublicId)), N''));

    INSERT dbo.Role (TenantId, Name, DescriptionJson, IsSystem, IsActive)
    VALUES (@TenantId, N'Integración: CMS rutas por correo', N'{"es":"Rol de la cuenta de servicio de una integración","en":"Role of an integration''s service account"}', 0, 1);
    SET @roleId = SCOPE_IDENTITY();
    INSERT dbo.RolePermission (RoleId, PermissionId)
    SELECT @roleId, PermissionId FROM dbo.Permission WHERE Code IN (N'orders.view', N'orders.create', N'locations.read', N'locations.create', N'clients.read');

    DECLARE @email NVARCHAR(256) = N'integration-' + REPLACE(LOWER(CONVERT(NVARCHAR(36), @pid)), N'-', N'') + N'@service.teikem.local';
    INSERT dbo.AspNetUsers (UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, SecurityStamp, ConcurrencyStamp, FullName, DefaultTenantId, UserKindLookupId,
                            IsActive, OnboardingRequired, MustChangePassword, EmailVerifiedUtc)
    VALUES (@email, UPPER(@email), @email, UPPER(@email), 1, REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), CONVERT(NVARCHAR(36), NEWID()), N'Integración CMS rutas por correo', @TenantId,
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'UserKind' AND InternalCode = N'SERVICE'), 1, 0, 0, SYSUTCDATETIME());
    SET @userId = SCOPE_IDENTITY();
    INSERT dbo.UserTenant (UserId, TenantId, IsDefault, StatusCodeId, JoinedAtUtc)
    VALUES (@userId, @TenantId, 1, (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'MembershipStatus' AND InternalCode = N'ACTIVE'), SYSUTCDATETIME());
    INSERT dbo.UserRole (UserId, RoleId, TenantId) VALUES (@userId, @roleId, @TenantId);

    INSERT dbo.Integration (PublicId, TenantId, Name, Description, Direction, ClientId, ConnectorKind, ConnectorConfigJson, ImportTemplateId, ScheduleKind, IntervalSeconds, ScopesJson,
                            ServiceUserId, ServiceRoleId, NotifyUserIdsJson, RetentionDays, IsActive)
    VALUES (@pid, @TenantId, N'CMS rutas por correo', N'Hojas de ruta de CMS: crea las órdenes de recogida y deja propuestas de cierre con la hoja «Ruta completada».', 'INBOUND', @ClientId, 'EMAIL', @cfg, @TemplateId, 'INTERVAL', 300, N'["files.orders_import"]', @userId, @roleId, N'[]', 30, 0);
    COMMIT;
    PRINT N'Integración «CMS rutas por correo» creada (apagada): falta el buzón.';
END
GO
