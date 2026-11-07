/* ============================================================================================================================
   09-reglas-de-aviso.sql — Pedido de vendedores (lote 228, P8): reglas de aviso (campana y correo) de ventas en Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Por qué: los avisos de ventas no se siembran solos (Sistema → Reglas de aviso: cada compañía decide a quién y por qué canal). Sin
   regla, la oficina igual ve lo pendiente en «Necesita tu atención» y en el Pulso, pero no le suena la campana ni le llega el correo.
   Qué hace (crea la regla si no existe una con ese nombre; si ya existe, NO la toca y solo la muestra):
     1. «Facturas por pasar a QuickBooks (Oficina de ventas)» — evento SALES_QUICKBOOKS_PENDING (uno al día, 07:00 de la compañía, solo
        si hay algo) → rol «Oficina de ventas». Campana: sí. Correo: @CorreoQuickBooks (por omisión 1 = sí: es el único correo del día).
     2. «Cliente nuevo por aprobar (Supervisor de ventas)» — SALES_CLIENT_PENDING_APPROVAL (cada vez que un vendedor da de alta un cliente)
        → rol «Supervisor de ventas». Campana: sí. Correo: @CorreoClienteNuevo (por omisión 0: uno por cliente; la campana y «Necesita tu
        atención» ya avisan).
     3. «Pedido urgente (Almacén)» — SALES_ORDER_URGENT (al solicitar un pedido urgente) → rol «Operador de almacén». Campana: sí.
        Correo: no. Se omite con @ReglaUrgente = 0.
   El texto de cada aviso es el de fábrica del evento (es/en); se cambia en Sistema → Reglas de aviso. OJO: con el correo encendido,
   en producción SÍ salen correos reales a las personas del rol (es lo que se quiere); no corra este script en una base de prueba que
   tenga la llave de Brevo puesta.
   Requiere 03-roles.sql. Idempotente: la segunda corrida cambia 0 filas.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 09-reglas-de-aviso.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';
DECLARE @CorreoQuickBooks   BIT = 1;                    -- el correo de la mañana a la Oficina de ventas
DECLARE @CorreoClienteNuevo BIT = 0;                    -- correo al supervisor por cada cliente nuevo
DECLARE @ReglaUrgente       BIT = 1;                    -- crear la regla del pedido urgente para el almacén

DECLARE @Reglas TABLE (Orden INT, Name NVARCHAR(100), EventCode VARCHAR(60), RoleName NVARCHAR(80), Email BIT,
    SubjectJson NVARCHAR(1000), BodyJson NVARCHAR(4000));
INSERT @Reglas VALUES
 (1, N'Facturas por pasar a QuickBooks (Oficina de ventas)', 'SALES_QUICKBOOKS_PENDING', N'SalesOffice', @CorreoQuickBooks,
  N'{"es":"Facturas por pasar a QuickBooks: {count}","en":"Invoices to pass to QuickBooks: {count}"}',
  N'{"es":"Hay {count} factura(s) por pasar a QuickBooks (la más vieja es del {oldest}; {exported} ya salieron en un archivo sin confirmar). Entre a Órdenes de venta → QuickBooks para exportarlas o marcarlas.","en":"There are {count} invoice(s) to pass to QuickBooks (the oldest is from {oldest}; {exported} already went out in a file without confirmation). Go to Sales orders → QuickBooks to export or mark them."}'),
 (2, N'Cliente nuevo por aprobar (Supervisor de ventas)', 'SALES_CLIENT_PENDING_APPROVAL', N'SalesSupervisor', @CorreoClienteNuevo,
  N'{"es":"Cliente nuevo pendiente de aprobación: {client}","en":"New client pending approval: {client}"}',
  N'{"es":"{seller} dio de alta al cliente {client} ({clientCode}) desde un pedido. Verifique sus datos (registro de comerciante, certificado) y apruébelo o recházelo; sus pedidos no se despachan hasta entonces.","en":"{seller} added the client {client} ({clientCode}) from an order. Check its data (merchant registration, certificate) and approve or reject it; its orders are not shipped until then."}'),
 (3, N'Pedido urgente (Almacén)', 'SALES_ORDER_URGENT', N'WarehouseOperator', 0,
  N'{"es":"Pedido urgente {orderNumber} de {client}","en":"Urgent order {orderNumber} for {client}"}',
  N'{"es":"{seller} solicitó el pedido URGENTE {orderNumber} de {client} para {warehouse}. {note}","en":"{seller} requested the URGENT order {orderNumber} for {client} for {warehouse}. {note}"}');
IF @ReglaUrgente = 0 DELETE FROM @Reglas WHERE Orden = 3;

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'09-reglas-de-aviso · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);
SELECT @msg = STRING_AGG(CONVERT(NVARCHAR(MAX), x.RoleName), N', ') FROM @Reglas x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Role r WHERE r.TenantId = @TenantId AND r.Name = x.RoleName AND r.IsActive = 1);
IF @msg IS NOT NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: la compañía no tiene activos los roles ', @msg, N'. Corra antes 03-roles.sql (o ponga @ReglaUrgente = 0 si no hay operador de almacén). No se cambió nada.');
    THROW 50002, @msg, 1;
END

-- Otras reglas que ya existen para estos eventos (se muestran; no se tocan)
SELECT nr.Name AS [Regla que ya existe para el evento], nr.EventCode, nr.IsActive AS [Activa]
FROM dbo.NotificationRule nr WHERE nr.TenantId = @TenantId AND nr.EventCode IN (SELECT EventCode FROM @Reglas);

BEGIN TRAN;

DECLARE @Orden INT = 1, @Max INT = 3, @RuleId INT, @Name NVARCHAR(100), @Event VARCHAR(60), @Role NVARCHAR(80), @Email BIT, @Subj NVARCHAR(1000), @Body NVARCHAR(4000);
WHILE @Orden <= @Max
BEGIN
    SELECT @Name = NULL;
    SELECT @Name = Name, @Event = EventCode, @Role = RoleName, @Email = Email, @Subj = SubjectJson, @Body = BodyJson FROM @Reglas WHERE Orden = @Orden;
    IF @Name IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.NotificationRule WHERE TenantId = @TenantId AND Name = @Name)
            PRINT CONCAT(N'Ya existe la regla «', @Name, N'»: no se toca.');
        ELSE
        BEGIN
            INSERT dbo.NotificationRule (PublicId, TenantId, EventCode, Name, IsActive) VALUES (NEWID(), @TenantId, @Event, @Name, 1);
            SET @RuleId = SCOPE_IDENTITY(); SET @Cambios += 1;
            INSERT dbo.NotificationRuleChannel (TenantId, NotificationRuleId, Channel, IsEnabled, SubjectJson, BodyJson)
            SELECT @TenantId, @RuleId, c.Channel, CASE WHEN c.Channel = 'APP' THEN 1 WHEN c.Channel = 'EMAIL' THEN @Email ELSE 0 END, @Subj, @Body
            FROM (VALUES ('EMAIL'), ('SMS'), ('APP'), ('PUSH')) c (Channel);
            SET @Cambios += @@ROWCOUNT;
            INSERT dbo.NotificationRuleRecipient (TenantId, NotificationRuleId, Kind, RoleId)
            SELECT @TenantId, @RuleId, 'ROLE', r.RoleId FROM dbo.Role r WHERE r.TenantId = @TenantId AND r.Name = @Role;
            SET @Cambios += @@ROWCOUNT;
            PRINT CONCAT(N'Regla creada: «', @Name, N'» (campana', CASE WHEN @Email = 1 THEN N' + correo' ELSE N'' END, N').');
        END
    END
    SET @Orden += 1;
END

SELECT nr.Name AS [Regla], nr.EventCode AS [Evento], nr.IsActive AS [Activa],
       (SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), c.Channel), N', ') FROM dbo.NotificationRuleChannel c WHERE c.NotificationRuleId = nr.NotificationRuleId AND c.IsEnabled = 1) AS [Canales],
       (SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), ISNULL(JSON_VALUE(r.DescriptionJson, '$.es'), r.Name)), N', ') FROM dbo.NotificationRuleRecipient x JOIN dbo.Role r ON r.RoleId = x.RoleId
         WHERE x.NotificationRuleId = nr.NotificationRuleId) AS [Roles]
FROM dbo.NotificationRule nr WHERE nr.TenantId = @TenantId AND nr.EventCode IN (SELECT EventCode FROM @Reglas) ORDER BY nr.Name;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
