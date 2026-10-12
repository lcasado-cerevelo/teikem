# Lote 38 — Importación CSV de consignatarios (backend de F-B) (2026-10-11)

Backend de la pantalla web F-B («Importar consignatarios»): plantillas por posición de columna, validar (vista previa sin
crear nada) → confirmar o descartar, sobre las mismas tablas y el mismo patrón que el importador de órdenes (Lote 3). La
pantalla F-B es otro trabajo; aquí se regeneró `web-app/openapi.json` para que la consuma.

## Qué se construyó

| Pieza | Resultado | Dónde |
|---|---|---|
| Plantillas de consignatarios | `GET/POST /api/v1/locations/import-templates`, `GET/PATCH /{publicId}`, `POST /{publicId}/deactivate` y `/reactivate`. Mismo `ImportTemplateDto` con `kind = CONSIGNEE`. 17 campos (`code`, `name`, `line1`, `line2`, `city`, `state`, `postalCode`, `country`, `clientCode`, `clientName`, `defaultServiceMinutes`, `defaultWindowStart`, `defaultWindowEnd`, `accessNotes`, `deliveryNotes`, `allowDupInvoice`, `contactPhone`) | `ConsigneeImportTemplatesController`, `ImportTemplateService` (ahora por tipo), `ConsigneeImportMapping` |
| Catálogo de campos | `GET /api/v1/locations/import/fields` → `{ field, required, defaultAllowed }` | `ConsigneeImportController`, `ConsigneeImportService.GetFields` |
| Validar | `POST /api/v1/locations/import/validate` (JSON) y `/validate-file` (multipart, `file`). Límite 5.000 filas / 2 MB. Dueño por fila (`clientCode` → `clientName` → cliente del lote → compartida), reglas de la ficha del consignatario, teléfono, duplicados en el archivo y contra el directorio. Guarda un `ImportBatch` VALIDATED con `Kind = CONSIGNEE` | `ConsigneeImportService.ValidateAsync` |
| Consultar / confirmar / descartar | `GET /api/v1/locations/import/{id}`, `POST .../confirm` (`rows` opcional), `POST .../discard`. Confirmar: reserva atómica del lote, una transacción por fila, candado por compañía y re-chequeo de duplicados en cada fila; alta con `LocationService.CreateCoreAsync` (mismas reglas y auditoría que `POST /locations`) y teléfono como contacto PHONE principal | `ConsigneeImportService.ConfirmAsync` / `DiscardAsync` |
| Ruta de órdenes sin cambio de contrato | `/api/v1/import-templates` atiende solo `ORDER` (`kind=CONSIGNEE` → 400 `Tipo de plantilla desconocido: CONSIGNEE.`; una plantilla de consignatarios por esa ruta → 404, y al revés) | `ImportTemplatesController`, `ImportTemplateService` |
| Base de datos | Sección **«2026-10-11 (f)»** de `Diseño/logistica-db-update.sql`: `ImportBatch.ClientId` pasa a `NULL` (con el índice `IX_ImportBatch_Client` quitado y recreado) y `CK_ImportBatch_OrderClient` (`Kind <> 'ORDER' OR ClientId IS NOT NULL`). Sin datos nuevos: ni permisos, ni estatus, ni EntityType | `logistica-db-update.sql`, `ImportBatch.cs`, `OrderConfigurations.cs` |
| Smoke | Paso «importador de consignatarios (Lote 38)» después del importador de órdenes | `scripts/smoke.sh` |

Permisos y módulo: **sin permisos nuevos**. Leer (plantillas, campos, lote) = `locations.read`; importar y gestionar
plantillas = `locations.create`. Módulo `CATALOG`.

## Cómo se probó

- Servidor: `dotnet build` sin errores y **3493 pruebas verdes**. Nuevas del lote: `ConsigneeImportMappingTests`,
  `ConsigneeImportDuplicateRulesTests`, `ImportTemplateKindTests`, `ConsigneeImportServiceTests`,
  `ConsigneeImportControllerSecurityTests` (matriz de permisos y módulo por acción, también la de los controladores de
  órdenes) y `ConsigneeImportSqlTests` (la sección (f) existe, va después de la (e) y trae sus guardas).
- Smoke completo (`scripts/smoke.sh`) contra una base nueva y desechable (`Teikem_l38_smoke` en el SQL Server local, `db-init`,
  API en Development con `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` apuntando a esa misma base):
  **SMOKE OK, 141 pasos `ok`, unos 15 minutos**; la base se borró al terminar. En Git Bash de Windows hay que correrlo con
  `TMPDIR` en forma Windows (`C:/…`): con `/tmp/…` la conversión de rutas de MSYS daña `-F "file=@/tmp/x;type=…"` y curl
  falla con `(26)` en el paso de logos del Lote 19 (no es del servidor). El paso nuevo cubre plantillas por tipo (cruces 400/404), reglas de plantilla, 17 campos, 415 sin
  Content-Type, un CSV mixto de 8 filas (3 válidas: compartida, del cliente por código con teléfono/ventana/«sí», y la
  compartida con aviso D8; 5 con el error exacto esperado), multipart con cliente de lote (y fila de otro cliente),
  cliente de baja (409 en el lote, error en la fila), confirmar (400 por fila elegida con error o inexistente; 3 creados
  con minutos 10, ventana 08:00–17:00, `allowDupInvoice` y teléfono principal), 409 al reconfirmar, 422 al descartar un
  confirmado, revalidar lo ya importado (todo duplicado) y 400 «sin filas válidas», descartar, tres carreras (dos
  confirmaciones del mismo lote → 200/409; confirmar y descartar a la vez → estado coherente; **dos lotes con el mismo
  código nuevo confirmados a la vez → un solo consignatario**, el otro lote reporta la fila fallida con «Ya existe un
  consignatario con el código …»), 5.001 filas → 400, lotes y plantillas de un tipo por la ruta del otro → 404, RBAC
  (sin `locations.read` 403, solo `orders.view` 403, otra compañía 404) y `CATALOG` apagado → 403 `module_disabled`.
- Rendimiento: confirmar un lote de **5.000 filas** tomó **34 s** en la máquina de desarrollo (una transacción, el candado,
  el re-chequeo, la auditoría y, si trae, el teléfono por fila).
- El smoke destapó un hueco respecto al plan: los **defaults tipados** de la plantilla no se revisaban al guardarla
  (`defaultServiceMinutes: "diez"` se guardaba y cada fila fallaba después). Se corrigió: al guardar la plantilla se revisan
  `defaultServiceMinutes`, `defaultWindowStart/End`, `allowDupInvoice`, el formato de `country`, los largos y el teléfono,
  con el mismo mensaje que daría la fila y la llave `defaults.<campo>` (`ConsigneeImportMapping.DefaultValueError`,
  `ImportTemplateService.ValidateColumns`; pruebas en `ConsigneeImportMappingTests` e `ImportTemplateKindTests`).

## Desviaciones del plan

1. **Candado**: en vez de un `sp_getapplock` propio (`AppLockExtensions`, 15 s y mensaje «Otra importación de consignatarios se
   está confirmando…»), cada fila toma `LockTenantPlanningAsync` (bloqueo U de la fila `Tenant` hasta el fin de la
   transacción de la fila), el mismo de «Planificar el día». No hay mensaje de tiempo agotado propio.
2. **`LocationRules.cs` no se creó**: los topes y `DefaultCountry` de `LocationService` se hicieron públicos y el importador los usa
   (`ConsigneeImportMapping` repite los mismos números para revisar los defaults de la plantilla).
3. **`contactPhone` admite valor por defecto** (el plan lo ponía entre los campos sin default). Un default de teléfono se
   revisa al guardar la plantilla con la regla de contactos.
4. **`clientName`** se compara recortado y sin distinguir mayúsculas, pero **sí distingue tildes y espacios dobles** (el plan
   decía `ConsigneeMatch.Normalize`).
5. Dentro del **mismo archivo**, una fila compartida y una de cliente con el mismo código no generan el aviso D8 (solo se
   avisa contra el directorio), y «mismo nombre con otra línea 1» tampoco se avisa entre filas del archivo (solo contra el
   directorio).
6. Lote en un estatus que no sea VALIDATED, CONFIRMED ni DISCARDED (no existe hoy) → 409 `El lote está en estatus X y no admite confirmación.`

## Decisiones a revisar

1. **D1 — Mismas tablas que órdenes.** `ImportTemplate`/`ImportBatch` con `Kind = 'CONSIGNEE'`; `ImportBatch.ClientId` pasa a
   NULL (lotes de compartidos o con cliente por fila). Alternativa: tabla `ConsigneeImportBatch` propia (sin ALTER en
   producción, pero duplica entidad, configuración, EntityType y resolver).
2. **ALTER COLUMN en producción, lo aplica el comparador de esquema.** `db-update` compara la base real contra estructura + seed +
   `logistica-db-update.sql` (`SchemaSync`/`SchemaDiffer`); al ver `ImportBatch.ClientId` NULL en el estado deseado y NOT NULL en
   la base, **el propio comparador aplica `ALTER TABLE dbo.ImportBatch ALTER COLUMN ClientId INT NULL`** antes de correr el
   archivo de cambios, y la sección (f) queda como respaldo idempotente (su guarda `is_nullable = 0` ya no entra). Antes de
   producción: `db-update` **con simulación** sobre una COPIA de la base, revisar que el plan liste «Ajustar columna
   dbo.ImportBatch.ClientId» y que no choque con `IX_ImportBatch_Client`; si lo hiciera, una sección nueva que suelte y
   recree el índice (las publicadas no se reescriben).
3. **Candado por compañía con `LockTenantPlanningAsync`** (desviación 1). Ventaja: no hace falta otro mecanismo y un código no
   se duplica entre dos importaciones a la vez (probado en el smoke). Costo: mientras una importación confirma, «Planificar
   el día» de la misma compañía espera (y al revés) fila por fila, y una espera larga termina con el tiempo de la consulta, no
   con un mensaje propio. Alternativa: `sp_getapplock` con recurso `consignee-import:{tenant}`, 15 s y mensaje propio.
4. **34 s para confirmar 5.000 filas.** Cabe en una petición, pero se acerca a los límites de un proxy o de IIS (normalmente
   60–120 s). Si F-B importa archivos grandes en producción, valorar confirmar en segundo plano o en bloques.
5. **D14 — Historial del lote exige `orders.view`.** `GET /api/v1/status/history/IMPORT_BATCH/{id}` usa el permiso de la entidad
   dueña de IMPORT_BATCH (órdenes): un usuario solo con `locations.*` recibe 403. F-B no muestra ese historial.
6. **CSV de Excel en Windows-1252.** El archivo se lee como UTF-8; un CSV guardado por Excel como «CSV (delimitado por comas)»
   llega con las tildes dañadas («Mar�a»), igual que en órdenes. El manual y el FAQ piden «CSV UTF-8». Alternativa: detectar
   la codificación o aceptar Windows-1252.
7. **D2 — Sin permisos nuevos**: importar y gestionar plantillas = `locations.create`. Alternativa: plantillas con
   `locations.update` (solo administradores).
8. **D8 — Código de una fila compartida que ya usa un consignatario de un cliente = aviso**, no error (para ese cliente seguirá
   resolviendo al suyo). **D9 — Solo altas**: un código existente es error (no hay modo «actualizar»).
9. **D10 — El teléfono se guarda sin exigir `contacts.manage`** (como el importador de órdenes).
10. **D11 — Confirmar sin filas válidas = 400** `El lote no tiene filas válidas para crear.` (en órdenes se cierra con 0 creados).
11. **`Location.Code` no tiene índice único**: el candado solo serializa importaciones; un alta manual por `POST /locations`
    simultánea todavía puede repetir un código (riesgo aceptado; `LocationService` no valida códigos).
12. **Conexión cortada a mitad de una confirmación.** El confirmar usa el token de la petición: si el usuario cierra la pantalla
    o se corta la red, lo creado queda creado pero el lote se queda `VALIDATED` con la reserva puesta (confirmar y descartar
    responden 409) y sin el resultado por fila. Es el mismo comportamiento del importador de órdenes. Hoy se resuelve
    validando el mismo archivo de nuevo (lo ya creado sale como duplicado). Alternativa: terminar la confirmación con
    `CancellationToken.None` una vez reservada, o liberar/cerrar el lote al cancelarse.

## Qué quedó fuera

- La pantalla web F-B (consume `/api/v1/locations/import*`; `web-app/openapi.json` ya está regenerado).
- `db-update` con simulación sobre una copia de la base de producción antes de publicar (ver decisión 2).

Manual: `docs/manual/02-clientes-y-contratos.md` §2.1 «Importación de consignatarios» y `docs/manual/faq.md` («Lote 38 —
Importación de consignatarios»).
