# Plantillas de importación/exportación para más entidades — diseño propuesto (2026-10-21)

Dictado de Luis (2026-10-19): casi todas las entidades con plantilla de importación **y de exportación** (la misma plantilla en las dos direcciones, para usarla en integraciones). Hoy solo hay importación de **envíos** (`/orders/import/*`) y **consignatarios** (`/locations/import/*`), ambas con plantillas en `ImportTemplate` (`Kind` = ORDER | CONSIGNEE) y la pantalla «Plantillas» (hecha hoy).

## Lo que hay hoy (medido)
- `OrderImportService` ≈ 880 líneas, `ConsigneeImportService` ≈ 650, `ImportTemplateService` ≈ 470: cada entidad tiene **su propio** validador y confirmador; solo comparten la tabla de plantillas y los editores web.
- Productos, proveedores, almacenes, posiciones, clientes, choferes, órdenes de venta, recibos, despachos y ajustes **no tienen importación** hoy.

## Propuesta
Un **motor genérico** en vez de repetir ~700 líneas por entidad:

1. `IImportEntity` (una clase pequeña por entidad registrada en `DependencyInjection`, clave = código `EntityType`, igual que `IDataSource`): lista de **campos** (código, obligatorio, tipo, si admite valor por defecto), **resolución** de referencias por código (cliente por código, almacén por código…), **validar fila** y **crear/actualizar fila**.
2. Un solo servicio genérico: leer CSV/XLSX (ya existe `FileFormatConverter`), aplicar la plantilla (columnas → campos, valores por defecto, uniones de columnas), validar por fila **sin guardar**, mostrar vista previa y confirmar en un lote (`ImportBatch`) con las mismas reglas que envíos.
3. `ImportTemplate.Kind` pasa a cualquier código de entidad (hoy hay un CHECK con 2 valores → sección nueva e idempotente en `logistica-db-update.sql`).
4. **Exportación**: el mismo catálogo de campos sirve para escribir el archivo (columnas de la plantilla → valores de la entidad), reutilizando la salida de archivos del Lote 137 (SFTP/FTP/HTTP, por evento u horario).
5. Web: la pantalla «Plantillas» ya tiene pestaña por entidad; el editor de columnas ya existe. Cada entidad agrega su botón **Importar** (asistente genérico) y **Exportar** (descarga con la plantilla).

## Orden sugerido (de más a menos valor / riesgo)
1. **Productos** (SKU, nombre, unidad, seguimiento, mínimo, costo, cliente dueño): la carga inicial de un almacén nuevo.
2. **Proveedores** y **Clientes**.
3. **Almacenes y posiciones**.
4. **Choferes**.
5. **Órdenes de venta**, **recibos**, **despachos** y **ajustes** (llevan líneas y afectan inventario: las más delicadas; cada una con vista previa obligatoria).

Estimado: el motor genérico + Productos es un lote grande (≈ 2 400 líneas entre backend, web, pruebas y manual); cada entidad siguiente es un lote mediano (≈ 400–700 líneas).

## Decisiones para Luis
1. ¿Se construye el motor genérico (recomendado) o se sigue entidad por entidad como envíos y consignatarios?
2. ¿Orden de entidades como arriba? ¿Cuál necesita primero algún cliente?
3. Importar **actualiza** lo existente (por SKU/código) o solo **crea** y rechaza los repetidos? (propuesta: solo crea por omisión y «actualizar» como opción de la plantilla).
4. Exportación: ¿solo bajo demanda y por integración, o también un botón «Exportar» en cada lista?
