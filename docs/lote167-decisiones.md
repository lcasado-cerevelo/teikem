# Lote 167 — Búsqueda global («Buscar o ejecutar»)

Fecha: 2026-10-21. Dictado de Luis (2026-10-20), documentado en `docs/busqueda-global-dictado-2026-10-20.md`; capítulo 42 del manual.

## Qué se construyó
- **Servidor**: `GlobalSearchService` + `GET /api/v1/search?q=` (`SearchController`). Envíos (número, conduce, empaque, factura del cliente, paquete/pieza, referencia), rutas (`Trip.Code`, incluye las migradas `M-…`), reclamaciones, clientes, almacenes/zonas/posiciones, productos (SKU, código de barras, nombre), recibos, recolecciones/empaques, avisos de salida, órdenes de compra y de venta, proveedores (nombre), facturas, rentas y teléfonos (por dígitos; lleva al dueño). Prefijo para números, «contiene» solo en tablas chicas; 6 por tipo, 40 en total, lo exacto primero. Cada tipo exige su permiso de lectura y su módulo.
- **SQL** (`logistica-db-update.sql`, Lote 167): `IX_ContactPoint_Value` y `IX_WarehouseBin_Code`, idempotentes; reflejados en EF.
- **Web**: la paleta muestra los registros arriba (agrupados por tipo) y el menú debajo; con un único resultado exacto (4 letras o más) se abre sola. Reclamaciones, facturas, proveedores y consignatarios abren su lista ya filtrada (`?q=`).
- **Documento legal**: versión `2026-10-21.1` (todos aceptan de nuevo).

## Cómo se probó (solo lo tocado)
- C#: `GlobalSearchServiceTests` (5: texto corto, envío por número y empaque con lo exacto primero, permisos y módulos por tipo, almacén/zona/posición/producto, teléfono por dígitos con permiso del dueño) y las de legal. Sobre InMemory (ahí se distinguen mayúsculas).
- Web: reglas (`searchRules.test.ts`), paleta con registros, shell y las listas tocadas.
- SQL: sintaxis comprobada. No se probó contra la base local ni en el navegador.

## Fuera por ahora / a revisar
- **Avisos de llegada**: falta su `Number` (otra sesión); entrarán al buscador cuando exista.
- **Serie y lote**: fuera (decisión de Luis).
- Subtítulos de los resultados en español aun con la interfaz en inglés.
- Elegir un resultado que abre la misma lista en la que ya se está no aplica el filtro nuevo.
- Búsqueda de un envío por el nombre del cliente: no incluida (los clientes se encuentran por nombre y de ahí a sus envíos).
