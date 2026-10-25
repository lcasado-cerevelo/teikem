// Genera los scripts SQL que CREAN la configuración de las integraciones por correo de Axiscare (Depot) y de CMS (Advance Logistics). Los lectores salen de
// web-app/src/features/integrations/readerPresets.json (la misma fuente que los botones «Usar un modelo» y las pruebas), así que el SQL y la pantalla nunca se separan.
//   node scripts/integraciones/generar-sql.cjs
// Salida (UTF-8 con BOM, para correrlos en SSMS o con sqlcmd -f 65001): scripts/integraciones/axiscare-depot.sql y scripts/integraciones/cms-advance-logistics.sql
const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '..', '..')
const presets = JSON.parse(fs.readFileSync(path.join(root, 'web-app/src/features/integrations/readerPresets.json'), 'utf8'))

const lit = (s) => `N'${String(s).replace(/'/g, "''")}'`
const nul = (s) => (s === undefined || s === null || s === '' ? null : s)

/** Un modelo de la pantalla → el lector tal como lo guarda el API (camelCase, nulos fuera). */
function readerOf(key, extra = {}) {
  const p = presets.find((x) => x.key === key)
  if (!p) throw new Error('No hay modelo ' + key)
  const r = p.reader
  const reader = {
    name: r.name,
    source: r.source,
    target: r.target,
    when: r.conditions.map((c) => ({ scope: c.scope, operator: c.operator, value: c.value })),
    fields: r.fields.map((f) => ({
      position: Number(f.position), kind: f.kind, label: nul(f.label), pattern: nul(f.pattern), required: !!f.required, transform: f.transform,
      start: nul(f.start), end: nul(f.end), separator: nul(f.separator),
      columns: f.columns ? f.columns.split(',').map((x) => x.trim()).filter(Boolean).map((x) => { const [a, b] = x.split('='); const [i, d] = b.split('/'); return d ? { position: Number(a), index: Number(i), divide: Number(d) } : { position: Number(a), index: Number(i) } }) : null,
      lineRegex: nul(f.lineRegex), repeat: !!f.repeat, fromHeader: !!f.fromHeader, appendWith: nul(f.appendWith),
    })),
    firstOnly: !!r.firstOnly,
    recordStart: nul(r.recordStart),
    mode: r.mode || 'SUGGEST',
    toStatus: r.target === 'ORDER_COMPLETION' ? (r.toStatus || 'PICKUP') : null,
    minConfidence: r.minConfidence === '' || r.minConfidence === undefined ? null : Number(r.minConfidence),
    checks: (r.checks || []).length === 0 ? null : r.checks.map((c) => ({ kind: c.kind, pattern: c.pattern, position: Number(c.position) || 1, distinct: !!c.distinct, tolerance: c.tolerance === '' || c.tolerance === undefined ? null : Number(c.tolerance), name: nul(c.name), scope: c.scope && c.scope !== 'RECORD' ? c.scope : null })),
    ...extra,
  }
  return JSON.parse(JSON.stringify(reader, (k, v) => (v === null ? undefined : v)))
}

/** La configuración del buzón: lo del servidor son marcadores que se cambian en la pantalla (Editar la integración) antes de prenderla. */
function emailConfig(readers) {
  return {
    // markMode LOG = Teikem anota en su propio log (dbo.IntegrationMailLog) lo que ya atendió y NO toca el buzón (ni marca leído ni mueve); readSince (AAAA-MM-DD) se pone en la pantalla si el buzón es grande.
    // Para un buzón de Microsoft 365 se cambia en la pantalla el «Mailbox type» (Tenant ID, Client ID y secreto de la aplicación); ahí no hay servidor ni contraseña.
    provider: 'IMAP', markMode: 'LOG', host: 'imap.CAMBIAR.com', port: 993, useSsl: true, username: 'buzon@CAMBIAR.com', mailbox: 'INBOX', processedFolder: 'Procesados',
    requireAttachment: false, attachmentMode: 'ALL', readBody: false, readPdf: false, readers,
  }
}

const HEADER = (title, lines) => `/* ============================================================================
   ${title}
   Generado por scripts/integraciones/generar-sql.cjs (no se edita a mano: se cambia el generador o readerPresets.json y se vuelve a generar).
${lines.map((l) => '   ' + l).join('\n')}

   IDEMPOTENTE: se puede correr otra vez; lo que ya existe no se toca ni se duplica (para cambiar una integración ya creada se edita en la pantalla Integraciones).
   NO es parte de logistica-db-update.sql: son datos de ESTA operación, no estructura. Antes de correrlo, mire los parámetros de arriba (DECLARE).
   Las integraciones nacen APAGADAS (IsActive = 0) y con el servidor del buzón en marcadores «CAMBIAR»: en Integraciones → Editar ponga el servidor, el usuario y la
   CONTRASEÑA del buzón (la contraseña se guarda cifrada por la aplicación; no se puede poner desde SQL) —o, si el buzón es de Microsoft 365, el tipo «Microsoft 365» con su Tenant ID, Client ID y secreto—,
   los remitentes permitidos, y préndala. Requiere la tabla dbo.IntegrationMailLog (logistica-db-update.sql, sección 2026-10-24 (o)): el «ya lo leí» queda en Teikem y el buzón no se toca.
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
`

/** Crea el rol, la cuenta de servicio y la integración (espejo de IntegrationService.CreateAsync); recibe el JSON del buzón con marcadores {{CLIENT}} y {{WAREHOUSE}}. */
function integrationBlock({ name, description, cfg, template, scopesJson }) {
  return `
-- ---------------------------------------------------------------------------- Integración «${name}»
IF EXISTS (SELECT 1 FROM dbo.Integration WHERE TenantId = @TenantId AND Name = ${lit(name)})
    PRINT N'La integración «${name}» ya existe: no se toca.';
ELSE
BEGIN
    BEGIN TRAN;
    DECLARE @pid UNIQUEIDENTIFIER = NEWID(), @roleId INT, @userId INT;
    DECLARE @cfg NVARCHAR(MAX) = ${lit(JSON.stringify(cfg))};
    SET @cfg = REPLACE(@cfg, N'{{CLIENT}}', LOWER(CONVERT(NVARCHAR(36), @ClientPublicId)));
    ${'SET @cfg = REPLACE(@cfg, N\'{{WAREHOUSE}}\', ISNULL(LOWER(CONVERT(NVARCHAR(36), @WarehousePublicId)), N\'\'));'}

    INSERT dbo.Role (TenantId, Name, DescriptionJson, IsSystem, IsActive)
    VALUES (@TenantId, ${lit('Integración: ' + name)}, N'{"es":"Rol de la cuenta de servicio de una integración","en":"Role of an integration''s service account"}', 0, 1);
    SET @roleId = SCOPE_IDENTITY();
    INSERT dbo.RolePermission (RoleId, PermissionId)
    SELECT @roleId, PermissionId FROM dbo.Permission WHERE Code IN (N'orders.view', N'orders.create', N'locations.read', N'locations.create', N'clients.read');

    DECLARE @email NVARCHAR(256) = N'integration-' + REPLACE(LOWER(CONVERT(NVARCHAR(36), @pid)), N'-', N'') + N'@service.teikem.local';
    INSERT dbo.AspNetUsers (UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, SecurityStamp, ConcurrencyStamp, FullName, DefaultTenantId, UserKindLookupId,
                            IsActive, OnboardingRequired, MustChangePassword, EmailVerifiedUtc)
    VALUES (@email, UPPER(@email), @email, UPPER(@email), 1, REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), CONVERT(NVARCHAR(36), NEWID()), ${lit('Integración ' + name)}, @TenantId,
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'UserKind' AND InternalCode = N'SERVICE'), 1, 0, 0, SYSUTCDATETIME());
    SET @userId = SCOPE_IDENTITY();
    INSERT dbo.UserTenant (UserId, TenantId, IsDefault, StatusCodeId, JoinedAtUtc)
    VALUES (@userId, @TenantId, 1, (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'MembershipStatus' AND InternalCode = N'ACTIVE'), SYSUTCDATETIME());
    INSERT dbo.UserRole (UserId, RoleId, TenantId) VALUES (@userId, @roleId, @TenantId);

    INSERT dbo.Integration (PublicId, TenantId, Name, Description, Direction, ClientId, ConnectorKind, ConnectorConfigJson, ImportTemplateId, ScheduleKind, IntervalSeconds, ScopesJson,
                            ServiceUserId, ServiceRoleId, NotifyUserIdsJson, RetentionDays, IsActive)
    VALUES (@pid, @TenantId, ${lit(name)}, ${lit(description)}, 'INBOUND', @ClientId, 'EMAIL', @cfg, @TemplateId, 'INTERVAL', 300, ${lit(scopesJson)}, @userId, @roleId, N'[]', 30, 0);
    COMMIT;
    PRINT N'Integración «${name}» creada (apagada): falta el buzón.';
END
GO
`
}

const lookups = `
DECLARE @TenantId INT = (SELECT TOP 1 TenantId FROM dbo.Tenant WHERE Name = @TenantName);
IF @TenantId IS NULL THROW 50001, N'No existe la compañía indicada en @TenantName.', 1;
DECLARE @ClientId INT = (SELECT TOP 1 ClientId FROM dbo.Client WHERE TenantId = @TenantId AND Code = @ClientCode);
IF @ClientId IS NULL THROW 50002, N'No existe el cliente indicado en @ClientCode para esa compañía.', 1;
DECLARE @ClientPublicId UNIQUEIDENTIFIER = (SELECT PublicId FROM dbo.Client WHERE ClientId = @ClientId);
`

// ================================================================ Axiscare → Depot (avisos de llegada y de salida)
const axiscareReaders = ['dispatch-exit-notice', 'shipment-arrival-notice', 'drive-invoice-arrival-notice'].map((k) => readerOf(k, { warehousePublicId: '{{WAREHOUSE}}' }))
let axiscare = HEADER('Axiscare en Advance Depot — integración por correo de los avisos de llegada y de salida', [
  'Crea: la plantilla de importación de apoyo, y la integración «Axiscare avisos por correo» con tres lectores:',
  '  · «Despacho de mercancía» (el título del documento) → aviso de SALIDA;  · «Envío de mercancía» (el recibo) → aviso de LLEGADA;  · «Factura de Drive Medical» → aviso de LLEGADA.',
  'Los productos de los documentos (columna «Número de ítems») deben existir con ese mismo SKU como productos activos del cliente Axiscare, o el aviso va a la bandeja.',
])
axiscare += `
-- ----------------------------------------------------------------------------- PARÁMETROS
DECLARE @TenantName   NVARCHAR(200) = N'Advance Depot';
DECLARE @ClientCode   NVARCHAR(50)  = N'AXISCARE';
DECLARE @WarehouseName NVARCHAR(200) = N'Almac' + NCHAR(233) + N'n Depot';   -- el almacén donde entran y de donde salen los avisos
-- -----------------------------------------------------------------------------
${lookups}
DECLARE @WarehouseId INT = (SELECT TOP 1 WarehouseId FROM dbo.Warehouse WHERE TenantId = @TenantId AND Name = @WarehouseName AND IsActive = 1);
IF @WarehouseId IS NULL THROW 50003, N'No existe un almacén activo con el nombre de @WarehouseName en esa compañía.', 1;
DECLARE @WarehousePublicId UNIQUEIDENTIFIER = (SELECT PublicId FROM dbo.Warehouse WHERE WarehouseId = @WarehouseId);

-- Plantilla de apoyo: los lectores de avisos entregan sus datos por POSICIÓN de columna (1 referencia, 2 SKU, 3 cantidad, 4 destino, 5 fecha, 6 lote), sin encabezado y con coma.
IF NOT EXISTS (SELECT 1 FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'Avisos por documento (por posición)')
    INSERT dbo.ImportTemplate (TenantId, Kind, Name, Delimiter, HasHeader, ColumnsJson, MultiLine, IsActive)
    VALUES (@TenantId, 'ORDER', N'Avisos por documento (por posición)', N',', 0, N'[{"position":1,"field":"reference"}]', 0, 1);
DECLARE @TemplateId INT = (SELECT TOP 1 ImportTemplateId FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'Avisos por documento (por posición)');
`
axiscare += integrationBlock({
  name: 'Axiscare avisos por correo',
  description: 'Avisos de llegada y de salida de Axiscare leídos de los documentos que llegan por correo (despacho, envío/recibo y facturas de Drive Medical).',
  cfg: emailConfig(axiscareReaders), template: '@TemplateId', scopesJson: '["files.orders_import"]',
})

// ================================================================ CMS → Advance Logistics (órdenes de recogida y su cierre)
const cmsReaders = ['cms-referral-order', 'cms-route-completed'].map((k) => readerOf(k))
const columns = ['orderNumber', 'waybillNumber', 'pickupName', 'pickupLine1', 'pickupCity', 'pickupPostalCode', 'contactPhone', 'requestedDate', 'notes', 'zone', 'description']
let cms = HEADER('CMS (Clinical Medical Services) en Advance Logistics — integración por correo de las hojas de ruta', [
  'Crea: la dirección fija de CMS (consignatario «CMS»), las zonas «ERD Pick Up 1» y «ERD Pick Up 3» (las rutas de CMS; agregue las demás), la marca «el cliente trae su número de envío»,',
  'la plantilla de importación «CMS recogidas (hoja de ruta)» y la integración «CMS rutas por correo» con dos lectores:',
  '  · «Hoja de ruta de CMS» → ÓRDENES de recogida (la dirección del paciente es la recogida, CMS el consignatario, la ruta es la zona; el Order # es el número de la orden);',
  '  · «Ruta completada de CMS» → PROPUESTAS de cierre («Recogida») que una persona confirma en Integraciones → Propuestas (modo Sugerir).',
])
cms += `
-- ----------------------------------------------------------------------------- PARÁMETROS
DECLARE @TenantName      NVARCHAR(200) = N'Advance Logistics';
DECLARE @ClientCode      NVARCHAR(50)  = N'10297';           -- CLINICAL MEDICAL SERVICE
DECLARE @ServiceTypeCode NVARCHAR(50)  = N'PICK_UP';         -- el tipo de servicio de las órdenes de CMS (por ejemplo PICK_UP, o el propio de la compañía: PICK_UP_10029)
DECLARE @PackageTypeCode NVARCHAR(50)  = N'BOX';
-- -----------------------------------------------------------------------------
${lookups}
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
    VALUES (@TenantId, 'ORDER', N'CMS recogidas (hoja de ruta)', @ClientId, N',', 0, ${lit(JSON.stringify(columns.map((f, i) => ({ position: i + 1, field: f }))))}, @Defaults, 1, N'[]', 1);
DECLARE @TemplateId INT = (SELECT TOP 1 ImportTemplateId FROM dbo.ImportTemplate WHERE TenantId = @TenantId AND Kind = 'ORDER' AND Name = N'CMS recogidas (hoja de ruta)');
`
cms += integrationBlock({
  name: 'CMS rutas por correo',
  description: 'Hojas de ruta de CMS: crea las órdenes de recogida y deja propuestas de cierre con la hoja «Ruta completada».',
  cfg: emailConfig(cmsReaders), template: '@TemplateId', scopesJson: '["files.orders_import"]',
})

const bom = '﻿'
fs.writeFileSync(path.join(__dirname, 'axiscare-depot.sql'), bom + axiscare.replace(/\r?\n/g, '\r\n'))
fs.writeFileSync(path.join(__dirname, 'cms-advance-logistics.sql'), bom + cms.replace(/\r?\n/g, '\r\n'))
console.log('Generados: axiscare-depot.sql y cms-advance-logistics.sql')
