# Salida por Integración con la plantilla de la entidad — diseño propuesto (2026-10-21)

Dictado de Luis: la plantilla de una entidad (columna → campo) debe servir también para **sacar** datos por una integración (SFTP/FTP/API saliente), no solo para importar. Luis aclaró (2026-10-21) que la exportación a archivo de las tablas ya existe en cada lista; lo que falta es la salida **por Integración**.

## Lo que hay hoy (Lote 137b)
- Integraciones de salida de **envíos**: una cola por orden que se llena con el cambio de estatus (`OutputOrderEffect`), 19 campos fijos de la orden, formatos CSV/JSON/XML, destinos SFTP/FTP/API genérica, reintentos y la pestaña «Salidas».
- Es **dirigida por eventos** (un estatus). Productos, proveedores, almacenes y posiciones no tienen un evento así.

## Qué cambia para las demás entidades
Una entidad de catálogo no «llega a un estatus»: se exporta como **foto** (todo el catálogo o lo que cambió desde la última vez) por **horario**. Eso pide decisiones:

1. **Qué manda cada corrida:** todo el catálogo (simple, archivo grande, el receptor reemplaza) o solo lo creado o cambiado desde la última salida (menos datos, hace falta saber qué cambió: `UpdatedAtUtc` o el historial de auditoría que ya existe).
2. **Qué columnas:** la plantilla de la entidad (columna → campo) ya dice qué campo va en cada posición. Para salir se usa la **misma plantilla** (misma posición y mismo campo), con el catálogo de campos de la entidad (los de `ImportEntities`) más los que solo se leen (p. ej. existencias del producto).
3. **Cuándo:** por horario (cada N minutos u hora fija, como los trabajos programados), con «Enviar ahora» para probar.
4. **Quién la ve:** la integración de salida de la entidad pertenece a la compañía (no a un cliente), a diferencia de las de envíos.

## Propuesta de construcción (por partes)
1. `IExportEntityRows` por entidad (el espejo de `IImportEntityRows`): lee las filas de la entidad como diccionario campo → texto.
2. Un conector de salida «Entidad» en `Integration` (`Direction = OUTBOUND`, `EntityKind`, `TemplatePublicId`, horario) que reutiliza el envío de `IntegrationOutputService` (SFTP/FTP/HTTP, formatos, reintentos, bitácora).
3. Web: en Nueva integración de salida, elegir «Productos / Proveedores / Almacenes / Posiciones» y la plantilla (con el botón «Nueva plantilla» que ya existe).
4. Primera entidad: **Productos** (el caso más pedido: sincronizar el catálogo con otro sistema).

Estimado: ≈ 1 500 líneas entre backend, web, SQL (sección nueva en `logistica-db-update.sql`), pruebas y manual.

## Respuesta de Luis (2026-10-21)
Sobre exportar los catálogos por Integración: **«no es necesario»**. Por eso NO se construye la salida de catálogos (productos, proveedores, almacenes, posiciones) por Integración. Dijo que, si se hiciera, debería ser por horario y al cambiar algo, pero la respuesta anterior deja sin efecto esa pregunta. En la tercera pregunta marcó «Salida de Productos ya» (la opción recomendada); como contradice lo escrito, se tomó lo escrito y se le pregunta de nuevo si de verdad la quiere.
