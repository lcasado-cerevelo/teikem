-- Anonimiza los clientes de la copia de grabación: nombres de farmacias ficticios, sin contactos ni direcciones de personas.
-- SOLO Teikem_video*. Idempotente (los nombres salen del ClientId). El cliente de la compañía (ClientId 2) y el del video (ClientId 3) se dejan con nombre propio.
SET QUOTED_IDENTIFIER ON;
IF DB_NAME() NOT LIKE N'Teikem[_]video%' BEGIN RAISERROR(N'Solo se corre en Teikem_video*.', 16, 1); SET NOEXEC ON; END
GO
DECLARE @pueblos TABLE (n INT, p NVARCHAR(40));
INSERT @pueblos VALUES (0,N'CAGUAS'),(1,N'PONCE'),(2,N'MAYAGUEZ'),(3,N'ARECIBO'),(4,N'BAYAMON'),(5,N'CAROLINA'),(6,N'GUAYAMA'),(7,N'HUMACAO'),(8,N'AGUADILLA'),(9,N'FAJARDO'),
 (10,N'CAYEY'),(11,N'UTUADO'),(12,N'YAUCO'),(13,N'VEGA BAJA'),(14,N'MANATI'),(15,N'ISABELA'),(16,N'COAMO'),(17,N'JUANA DIAZ'),(18,N'SAN GERMAN'),(19,N'LARES');
UPDATE c SET Name = N'FARMACIA ' + pu.p + N' ' + CAST(c.ClientId AS NVARCHAR(10)), LegalName = N'FARMACIA ' + pu.p + N' ' + CAST(c.ClientId AS NVARCHAR(10)), TaxId = NULL, MerchantRegistration = NULL
FROM dbo.Client c JOIN @pueblos pu ON pu.n = c.ClientId % 20
WHERE c.TenantId = 2 AND c.ClientId NOT IN (2, 3);
UPDATE dbo.Client SET Name = N'FARMACIA LA ESPERANZA', LegalName = N'FARMACIA LA ESPERANZA', TaxId = NULL, MerchantRegistration = NULL WHERE TenantId = 2 AND ClientId = 3;
UPDATE l SET l.Name = c.Name, l.ContactName = NULL, l.Line1 = N'CALLE DEMO ' + CAST(l.LocationId % 90 + 10 AS NVARCHAR(10)), l.Line2 = NULL, l.AccessNotes = NULL, l.DeliveryNotes = NULL
FROM dbo.Location l JOIN dbo.Client c ON c.ClientId = l.ClientId WHERE l.TenantId = 2;
-- código del cliente, código de QuickBooks (campo qb_code), contactos (correos y teléfonos) y notas de auditoría con nombres reales
UPDATE dbo.Client SET Code = N'CL-' + RIGHT(N'0000' + CAST(ClientId AS NVARCHAR(10)), 4) WHERE TenantId = 2 AND ClientId <> 2;
UPDATE v SET v.ValueText = c.Name FROM dbo.CustomFieldValue v JOIN dbo.CustomFieldDefinition d ON d.CustomFieldDefinitionId = v.CustomFieldDefinitionId AND d.FieldKey = N'qb_code'
  JOIN dbo.Client c ON c.ClientId = v.EntityId WHERE v.TenantId = 2;
UPDATE dbo.ContactPoint SET Value = CASE WHEN Value LIKE N'%@%' THEN N'contacto' + CAST(ContactPointId AS NVARCHAR(10)) + N'@ejemplo.com' ELSE N'78755' + RIGHT(N'00000' + CAST(ContactPointId AS NVARCHAR(10)), 5) END, Extension = NULL WHERE TenantId = 2;
UPDATE dbo.AuditLog SET ChangesJson = NULL WHERE ChangesJson LIKE N'%ALLMED%';
SELECT COUNT(*) AS ClientesRenombrados FROM dbo.Client WHERE TenantId = 2 AND Name LIKE N'FARMACIA %';
GO
SET NOEXEC OFF;
