# Scripts SQL de las integraciones por correo

Crean **los datos de configuración** de las integraciones (no estructura: eso va en `Diseño/logistica-db-update.sql`). Se corren a mano, en la base de la operación, y se pueden correr más de una vez (son **idempotentes**: lo que ya existe no se toca ni se duplica).

| Script | Compañía | Qué crea |
|---|---|---|
| `axiscare-depot.sql` | Advance Depot | La plantilla de apoyo «Avisos por documento (por posición)» y la integración **«Axiscare avisos por correo»** con tres lectores: *Despacho de mercancía* → aviso de **salida**; *Envío de mercancía* (el recibo) → aviso de **llegada**; *Factura de Drive Medical* → aviso de **llegada**. Cada lector lleva el almacén indicado en `@WarehouseName`. |
| `cms-advance-logistics.sql` | Advance Logistics | El consignatario fijo **CMS** (código `CMS`), las zonas **«ERD Pick Up 1»** y **«ERD Pick Up 3»**, la marca «el cliente trae su número de envío» en CMS, la plantilla **«CMS recogidas (hoja de ruta)»** y la integración **«CMS rutas por correo»** con dos lectores: *Hoja de ruta de CMS* → órdenes de recogida; *Ruta completada de CMS* → **propuestas** de cierre (modo Sugerir). |

## Cómo correrlos

1. Abra el script (SSMS, UTF-8) y mire los **parámetros** de arriba (`DECLARE @TenantName …`): compañía, código del cliente, almacén o tipo de servicio. Si algo no existe, el script se detiene con un mensaje claro y no deja nada a medias.
2. Corra con la base de la operación seleccionada, o por línea de comandos:

   ```
   sqlcmd -S <servidor> -d <base> -E -C -f 65001 -b -i scripts/integraciones/axiscare-depot.sql
   sqlcmd -S <servidor> -d <base> -E -C -f 65001 -b -i scripts/integraciones/cms-advance-logistics.sql
   ```
3. Las integraciones nacen **apagadas** y con el servidor del buzón en marcadores «CAMBIAR» y en el modo **«log de Teikem»** (`markMode: LOG`: Teikem anota en su propio log lo que ya atendió y NO marca ni mueve nada en el buzón; requiere la tabla `dbo.IntegrationMailLog` de `logistica-db-update.sql`, sección 2026-10-24 (o)). Si el buzón es de **Microsoft 365**, en vez del servidor y la contraseña escoja «Mailbox type» = Microsoft 365 (capítulo 39, sección 24). En **Integraciones → Editar**: servidor IMAP, usuario, **contraseña** (se guarda cifrada por la aplicación; desde SQL no se puede), remitentes permitidos y **prenderla**. Pruebe primero subiendo a mano un PDF (botón *Subir archivo*) y revise el resultado.

## Lo que queda por hacer a mano

- **Axiscare:** los productos de los documentos (columna «Número de ítems») deben existir con **ese mismo SKU** como productos activos del cliente Axiscare; si no, el aviso va a la bandeja con el motivo.
- **CMS:** asigne en Flota a cada zona «ERD Pick Up N» los choferes que la trabajan; **agregue las zonas que falten** (el nombre debe ser exactamente el de la hoja: «DRIVER: ERD Pick Up 1»). Si el tipo de servicio de recogida de la compañía no es `PICK_UP`, cambie `@ServiceTypeCode` antes de correr (por ejemplo `PICK_UP_10029`).
- Las propuestas de cierre de CMS se confirman en **Integraciones → Propuestas**.

## Regenerar

Los lectores salen de `web-app/src/features/integrations/readerPresets.json` (la misma fuente de los botones «Usar un modelo» y de las pruebas). Para cambiar algo: edite esa fuente o `generar-sql.cjs` y corra

```
node scripts/integraciones/generar-sql.cjs
```
