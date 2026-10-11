# Categoría por defecto de productos nuevos, informes sin tope y formato de «Productos por posición» (2026-10-11)

Pedido del dueño (Luis) en tres partes. Rama de trabajo: `Depot-Implementation`.

## 1. Informes de Posiciones e Inventario: sin ningún tope, solo se anuncia
**Antes.** Un tope de 500 (aviso, ya sin bloqueo desde 2026-10-10) y un **corte de lectura a 100 000 filas** (`EXPORT_MAX_ROWS`) que sí recortaba los datos del informe o de la exportación.
**Ahora.** `fetchAllPages` **no corta en ningún número**: lee todas las filas del filtro. Se quitó `EXPORT_MAX_ROWS`; el `max` queda como opción explícita (nadie lo usa).
Los tres paneles que imprimen (Productos por posición, Etiquetas de posición, Etiquetas de producto) siguen **avisando** desde 500 («Son N: el PDF será grande y puede tardar un poco. Se imprimen todas; no hay límite.») y **Generar PDF nunca se deshabilita**.
Alcance revisado: Códigos de barras de posiciones y de productos, Reporte de inventario, Reporte de ajustes, Etiquetas de posición y de producto, Productos por posición y el botón Exportar de las tablas (todos leen con `fetchAllPages` o con su propia lectura de 200 en 200 hasta el total).
Los avisos «solo los primeros N… (límite de lectura)» se reescribieron: ya solo salen si el **servidor dejó de devolver filas** antes del total.

## 2. Categoría por defecto de los productos nuevos (por compañía)
- `Tenant.DefaultProductCategoryId` (NULL = ninguna) + FK a `ProductCategory`, en la sección **2026-10-11 (c)** de `Diseño/logistica-db-update.sql` (el seed y la estructura siguen congelados).
- **Advance Depot → AxisCare.** El mismo SQL la deja en la categoría `AxisCare` **solo para la compañía «Advance Depot», solo si estaba vacía y la categoría existe y está activa**. La migración de Depot la creó como `AxisCare` (junto); el SQL ignora espacios y mayúsculas. Si no existe, no hace nada.
- API: `PUT /tenant/settings` con `defaultProductCategoryId` (id activo de la compañía; `0` quita; `null` sin cambio; 400 `La categoría por defecto no existe o está inactiva.`). `GET /product-categories` trae `isDefault`.
- Pantalla: Ajustes → Operación → «Productos nuevos: categoría por defecto». **Nuevo producto** llega con esa categoría elegida; editar no la toca.
- **Decisión a revisar:** el default lo aplica **solo la pantalla** de Productos. Un producto creado por la API o por una importación sin categoría **no** la recibe.
- Una categoría por defecto dada de baja se ignora sin error.

## 3. «Productos por posición» con el aspecto del informe de Códigos de barras de posiciones
Se comparó el PDF enviado (`bin-barcodes-advance-depot-2026-10-11.pdf`) con el que genera hoy «Productos por posición» (renderizado con datos de ejemplo, con logo y subtítulo). **Es la misma plantilla** (`barcodeReportPdf.ts`: encabezado con logo, recuadro de filtros, 4 tarjetas, franja de grupo —aquí una por posición—, rejilla de 3 columnas con el código de barras de cada elemento, encabezado compacto y pie con «Página N de M»). Ya venía así desde el commit `a0884b1` (2026-10-10); no se cambió código de ese informe. Si en producción se sigue viendo el formato viejo (una posición por página), es una compilación anterior a ese commit.

## Cómo se probó
- Servidor (xunit, `ProductDefaultCategoryTests`): sin default; guardar y marcar `isDefault`; null = sin cambio; 0 quita; id inexistente, inactivo o de otra compañía = 400 con el mensaje exacto y sin guardar nada; default dado de baja se ignora; espejo en el SQL (columna, FK, dato de Depot).
- Web (vitest): ajustes (selector con solo activas, guardar, quitar con 0, error del servidor, solo lectura sin `admin.tenant`), formulario de producto (nuevo llega con el default y lo manda; sin default queda vacío; editar no lo toca), `fetchAllPages` lee más de 250 000 filas sin cortar.
- La compuerta y el CI de GitHub Actions son el árbitro del servidor (no hay SDK de .NET en el entorno de trabajo).
