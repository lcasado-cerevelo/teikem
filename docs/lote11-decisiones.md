# Lote 11 — Cambios de Almacén, tanda 1: qué se construyó y decisiones a revisar

> **Equivalencia de numeración:** este documento = **Lotes 0 y 1 del plan de cambios** (el que se trabaja con el dueño del
> producto). El repositorio ya tenía lote1…lote10, así que los cierres del plan usan el siguiente número libre:
> plan 0+1 = 11, plan 2 = 12, plan 3 = 13, plan 4 = 14, plan 5 = 15.

Fecha: 2026-09-29. Origen: el documento de cambios del dueño del producto sobre las pantallas de Almacén
(`F:\Download\Cambios.pdf`, 17 páginas). Plan: `C:\Users\Luis\.claude\plans\fuzzy-sleeping-cocoa.md`, secciones "Lote 0"
(kit transversal) y "Lote 1" (Almacenes, Zonas, Posiciones, Ubicaciones). En la numeración del repositorio esta tanda es
el **Lote 11** (el Lote 10 fue la migración de datos). Cierra el backend, el frontend web y el ajuste de la app móvil de
esta tanda. Los lotes 2 a 5 del plan (Productos/Compras/Proveedores; Recibo/Recolección; Transferencias, Conteo y Kárdex;
Pulso) siguen pendientes.

Este documento cubre también el frontend web: no se creó un `docs/frontend/loteF…-decisiones.md` aparte. El manual
funcional está en [`docs/manual/06-inventario-y-almacen.md`](manual/06-inventario-y-almacen.md), el de pantallas en
[`docs/manual/frontend/f6-almacen-e-inventario.md`](manual/frontend/f6-almacen-e-inventario.md) y los mensajes de error en
[`docs/manual/faq.md`](manual/faq.md) (sección "Lote 11").

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| Kit transversal (Lote 0) | Sin tablas | `web-app/src/kernel/ui/DataTable.tsx` (pie con rango, "Filas por página" y Exportar; props `pagination`, `onPageSize`, `exportRows`, `exportable`, `exportFileName`, `DataColumn.exportValue/exportable`), `exportTable.ts` + `ExportMenu.tsx` (CSV, Excel, PDF en el cliente), `kernel/api/fetchAllPages.ts`, `ComboSelect.tsx` (selección única con buscador), `styles/base.css` y `ui.css` (flecha de los desplegables a la izquierda, `.filters` que envuelve, encabezados numéricos sin letra monoespaciada) | Ninguno |
| Esquema | `WarehouseBin.MaxCapacityQty INT NULL` con `CK_WarehouseBin_MaxCapacityQty` (nulo o > 0); tabla global nueva `PostalLocality` (`City`, `PostalCode`, `State`, `Municipality`, `CountryLookupId`, `IsActive`; único `CountryLookupId + PostalCode + City`) | `Diseño/logistica-db-estructura.sql`, `Warehouse.cs` (`WarehouseBin.MaxCapacityQty`), `PostalLocality.cs`, `WarehouseConfigurations.cs`, `CatalogConfigurations.cs` | — |
| Seed | Sección 10 del seed: 42.522 localidades postales (42.346 de EE. UU. y territorios con país `US`; 176 de Puerto Rico con país `PR` y su municipio con acentos), insert-only idempotente en 43 lotes de 1.000 | `Diseño/logistica-db-seed.sql` | — |
| API — posiciones | (usa `WarehouseBin`, `StockBalance`) | `WarehouseLayoutService.ListBinsAsync` / `BinRowsQuery` (filtro, orden por código y paginación en SQL), `WarehouseRules` (`ValidateMaxCapacity`, `Occupancy`, `ParseOccupancy`, `BinPage`, `BinOccupancies`), `WarehouseContracts.cs` (`WarehouseBinPageDto`, `WarehouseBinQuery`, `WarehouseBinDto` con cupo, ocupación y producto único) | `GET /api/v1/warehouses/{publicId}/bins` (ahora paginado), `POST` y `PATCH .../bins` (campo `maxCapacityQty`, `clearMaxCapacity`) |
| API — zonas y almacenes | (calculado, no se guarda) | `WarehouseLayoutService.ZoneStatsQuery` (una consulta agrupada con la ocupación de todas las zonas), `UpdateZoneAsync` (el código se edita), `WarehouseService` (`ZoneTypeCodes` en la lista) | `GET .../zones` (ocupación por zona), `PATCH .../zones/{zoneId}` (acepta `code`), `GET /api/v1/warehouses` (con `zoneTypeCodes`) |
| API — localidades postales | `PostalLocality` (solo lectura) | `PostalLocalityService` (catálogo en memoria 1 h, comparación sin acentos), `PostalLocalityRules`, `PostalLocalitiesController` | `GET /api/v1/postal-localities?search=&take=` (solo `[Authorize]`) |
| App móvil | Sin tablas | `app-almacen/src/kernel/warehouse/binLookup.ts` (busca la posición escaneada en el servidor y toma la coincidencia exacta de código), `schema.d.ts` regenerado | — |
| Frontend — Almacenes | — | `WarehouseListScreen.tsx` (maestro-detalle con "Zonas de este almacén", filtros, alta), `PostalLocalityPicker.tsx`, `ZoneModal.tsx`, `warehouseFilters.ts` | — |
| Frontend — ficha | — | `WarehouseDetailScreen.tsx` (Datos, Zonas, Posiciones), `BinModal.tsx` (cupo máximo, zona con buscador) | — |
| Frontend — Ubicaciones | — | `LocationsScreen.tsx`, `locations.ts` (recuadros por zona con ocupado/capacidad, tabla paginada en el servidor) | — |
| Cableado de exportación de servidor | — | `features/warehouse/api.ts` (`exportProducts`, `exportInventoryBalances`, `exportInventoryTransactions`, `exportReceipts`, `exportWarehouseTasks`, `exportPickBatches`, `exportPurchaseOrders`, `exportOrders`, `exportWarehouseBins`) | — |

Todo bajo el filtro de tenant existente: `WarehouseBin` y `WarehouseZone` se alcanzan siempre a través de su almacén ya
filtrado. `PostalLocality` es global (sin `TenantId`), de solo lectura desde el API y no se audita.

## Cómo se prueba

Resultados reportados por quien ejecutó el lote. **No se volvieron a ejecutar al escribir este documento.**

1. `dotnet build Teikem.sln && dotnet test Teikem.sln` — 2.402 de 2.402 en verde. Pruebas nuevas o ajustadas:
   `WarehouseOccupancyTests` (cupo, estado de ocupación, filtro `occupancy`, paginación acotada, listado de posiciones,
   ocupación por zona, código de zona editable, tipos de zona en la lista, traducción de las consultas a SQL Server sin
   base con `ToQueryString`), `PostalLocalityTests` (normalización sin acentos, rango de coincidencia por ciudad, municipio y
   prefijo de ZIP, tope de `take`, controlador solo lectura y autenticado, mapeo 1:1 de la tabla y contenido del seed), y `WmsContractsTests` / `WmsFixture` por los contratos nuevos.
2. Web (`cd web-app && npm run check`): `tsc -b`, lint sin errores, vitest 58 archivos / 529 pruebas, build. Archivos de
   prueba nuevos del lote: `fetchAllPages.test.ts`, `exportTable.test.ts`, `ComboSelect.test.tsx`,
   `PostalLocalityPicker.test.tsx`; ajustados `DataTable.test.tsx`, `locations.test.ts`, `warehouseFilters.test.ts` y las
   de pantallas de almacén.
3. App móvil (`cd app-almacen`): `tsc`, lint y jest (145 pruebas), con `binLookup.test.ts` y `dispatchApi.test.ts`
   ajustados al listado paginado.
4. Base desde cero, repetible: respaldo `Teikem_antes_lote1_20260929.bak`, luego
   `dotnet run --project src/Teikem.Api -- db-reset --yes` (12 s, incluye los 42.522 ZIP) y `import-legacy` de Advance
   Depot y Advance Solutions sin rechazos. Demuestra que la base se recrea completa desde los dos scripts SQL.
5. En el navegador, con Advance Depot (datos reales):
   - Pie de tabla, "Filas por página" contra el servidor y 375 px de ancho sin scroll horizontal.
   - Exportación de los 552 productos en los tres formatos: CSV con 553 líneas, XLSX de 75 KB y PDF de 75 KB (se
     comprobó el tamaño y el número de líneas; no se abrieron los archivos en Excel).
   - Almacenes: panel de zonas y alta con ciudad "toa baja" → 00952 SABANA SECA (Toa Baja).
   - Zonas: código repetido → 409 bajo el campo Código.
   - Posiciones: cupo 25 con 10 en mano → Parcial 40 %, reflejado en el recuadro de Picking de Ubicaciones como
     "10/25 · 40 %"; después se quitó el cupo de prueba.
   - Ubicaciones: clic en el recuadro de Picking filtra 3.029 posiciones; el filtro Posición con "01-A-0" da 9.
6. `scripts/smoke.sh` se actualizó (listado paginado, búsqueda por pasillo, filtro de ocupación, ocupación desconocida
   → 400, cupo 0 → 400, ocupación de zona, código de zona editable y repetido → 409, `warehouseId` en el PATCH de zona →
   400, búsqueda de localidades). **No se corrió localmente:** lo corre el CI.

### Qué no se probó

- No se corrió `scripts/smoke.sh` ni el CI de GitHub Actions con este código.
- No se corrieron los recorridos de Playwright (`web-app/e2e`) contra estas pantallas; `f6.spec.ts` no se modificó. **Por
  lectura, su prueba 2 quedó desactualizada:** hace clic en la fila de ALM-01 y espera llegar a
  `/warehouse/warehouses/<id>` (la ficha), pero ahora el clic solo elige el almacén en el panel derecho
  (`?warehouse=<id>`); la ficha se abre con el ícono del lápiz. Falta ajustarla antes de volver a correr Playwright.
- No se probó la app móvil en un teléfono ni contra el API real; solo jest.
- Los archivos exportados no se abrieron en Excel ni en un visor de PDF.
- No se revisó pantalla por pantalla el efecto de los cambios globales del kit (decisión 11) fuera de Almacén.
- Un cupo mayor que 2.147.483.647 se rechaza en la pantalla ("El cupo máximo es demasiado grande."); enviado directo al
  API no se probó qué respuesta da (es un `int` en el contrato).
- Las capturas de pantalla del manual quedaron pendientes.

## Decisiones a revisar

1. **El script de estructura no es idempotente fuera de lo guardado.** Solo `WarehouseBin` (columna y `CHECK`) y
   `PostalLocality` llevan guarda (`IF OBJECT_ID` / `COL_LENGTH`); el resto sigue con `CREATE TABLE` a secas. El runner
   registra (nombre, hash) en `__SchemaVersion` y, si el archivo cambia, lo vuelve a ejecutar completo. Aplicar estos
   cambios a una base existente es `db-reset --yes` + `import-legacy` (el dueño aceptó recrear la base). Un tenant con
   datos reales que no viniera de la migración necesitaría un script de cambio a mano.
2. **Estado y País del almacén son de solo lectura en la pantalla.** Salen de la localidad elegida. Un almacén cuya
   dirección no esté en el catálogo USPS no puede capturar su estado a mano desde la interfaz (el API sí acepta texto
   libre en `state`, `city`, `postalCode` y `country`; el catálogo es una ayuda de captura, no una validación).
3. **Qué se guarda como Ciudad.** En Puerto Rico, el municipio con acentos (`Toa Baja`); fuera de Puerto Rico, el nombre
   postal USPS en mayúsculas y sin acentos (`NEW YORK`). Por eso "mi ciudad sale en mayúsculas" fuera de Puerto Rico. La
   lista de opciones muestra el nombre postal y, entre paréntesis, el municipio (`00952 · SABANA SECA (Toa Baja), PR`).
4. **Ordenar por columna en las tablas paginadas por el servidor ordena solo la página visible.** El API de posiciones (y
   los demás listados) no tiene parámetro de orden: el servidor ordena siempre por código.
5. **La alta de un conteo cíclico lee todas las posiciones del almacén** con `fetchAllPages` (unas 20 peticiones de 200 en
   Depot; tope 10.000). Se revisa en el lote de Conteo. Si hay más, avisa "Solo se listan las primeras {count} posiciones:
   elija zonas para acotar la lista."
6. **El cupo se mide en unidades de producto y contra la existencia en mano** (`QtyOnHand`, incluye lo reservado). Las
   posiciones sin cupo no cuentan en el porcentaje de su zona: se avisan aparte ("N posiciones sin cupo"). La capacidad
   de una zona no se guarda: es la suma del cupo de sus posiciones **activas**. Pendiente de confirmar con el dueño
   que "unidades de producto" es la unidad que quiere (el plan lo dejaba "a confirmar").
7. **Los filtros "Código" (pestaña Posiciones) y "Posición" (Ubicaciones) usan `search`**, que compara por subcadena en
   código, zona, pasillo, rack, nivel y posición. Antes solo miraba código y zona: era un bug (lo que la tabla llama
   "ubicación" eran esos cuatro campos y no se podía buscar por ellos).
8. **Cambio de contrato del listado de posiciones, incompatible hacia atrás.** `GET /api/v1/warehouses/{publicId}/bins`
   devolvía un arreglo; ahora devuelve `{ total, skip, take, items }` (`take` 100 por defecto, máximo 200). La web, la
   app y `scripts/smoke.sh` se adaptaron. **Las compilaciones de la app de almacén ya instaladas en teléfonos leen un
   arreglo y fallarán al buscar una posición escaneada (Acomodar, Despacho, Conteo)** hasta instalar la versión nueva
   (conclusión por lectura del código anterior, no se probó en un aparato). Hay que publicar la app antes o junto con
   este cambio de API.
9. **El catálogo de localidades se carga entero de USPS (42.522 ZIP), no solo Puerto Rico.** El plan proponía solo
   Puerto Rico "a confirmar"; se cargó el archivo completo del dueño (`SEPHAS/Database/post_city.sql`). Insert-only: una
   corrección posterior de un ZIP en el seed no actualiza la fila ya cargada.
10. **`GET /api/v1/postal-localities` solo pide sesión** (`[Authorize]`): sin permiso ni módulo propios, igual que la
    lectura de catálogos. El catálogo se guarda en memoria 1 hora por instancia del API (clave `postal-localities:v2`):
    un cambio directo en la tabla tarda hasta una hora en verse. La comparación sin acentos se hace en código.
11. **Cambios globales del kit.** Toda tabla local ahora pagina a 25 por defecto (antes mostraba todo si no se pasaba
    `pageSize`), muestra el pie con rango y "Filas por página" y trae el botón Exportar, en cualquier módulo. Se
    desactiva con `pagination={false}` (usado en `InventoryAdjustmentsScreen` por las capturas por fila, y en
    `ActivityPanel`, que tiene "Ver más"). Además: la flecha de todo `<select>` pasa a la izquierda y los encabezados de
    columnas numéricas dejan la letra monoespaciada. No se revisó cada pantalla de otros módulos.
12. **La exportación es 100 % en el cliente.** Excel con SheetJS, PDF con jsPDF + autotable, CSV propio; cargados bajo
    demanda. Consecuencias: (a) tope de 10.000 filas en tablas de servidor, con aviso; (b) el PDF usa fuentes estándar
    (Latin-1): tildes y eñes salen bien, otros caracteres salen como `?`; (c) la dependencia `xlsx` se instala desde
    `https://cdn.sheetjs.com/xlsx-0.20.3/xlsx-0.20.3.tgz`, no del registro de npm: `npm ci` y el CI necesitan salida a
    ese host; (d) el CSV antepone `'` a un texto que empieza por `=`, `+`, `-`, `@`, tabulador o retorno (protección
    contra fórmulas) salvo que sea un número; (e) el archivo se llama como la etiqueta de la tabla (`label`) y, si no
    tiene, como el título del panel, más la fecha: `almacenes-2026-09-29.xlsx`.
13. **El código de zona ahora se edita.** Se retiró el mensaje `El código de la zona no se puede cambiar.`. Las posiciones
    y los saldos apuntan a la zona por id, así que renombrarla no mueve nada; sigue siendo único en el almacén (409) y
    con el mismo formato. Lo que no cambia es su almacén (`La zona no se puede mover a otro almacén.`). El código de una
    **posición** y su zona siguen sin poder cambiarse (regla existente).
14. **La lista de almacenes se filtra en el cliente.** Carga todos los almacenes (con inactivos) y filtra Código, Nombre,
    Tipo, Estatus y Dirección en el navegador; es una lista corta. Se quitó el selector "Mostrar (solo activos / incluir
    inactivos)": ahora los inactivos se ven por defecto (atenuados) y se filtran con Estatus. "Tipo" sale de
    `zoneTypeCodes`, que solo cuenta las zonas **activas** del almacén.
15. **Texto sobrante en los archivos de idioma.** `warehouse.zones.codeHelp` ("El código de la zona no se puede
    cambiar." / "The zone code cannot be changed.") sigue en `es.json` y `en.json` pero ninguna pantalla lo usa ya; la
    ayuda vigente es `warehouse.zones.codeEditHelp`. Conviene borrar la clave vieja para que nadie la reutilice.
16. **La baja de zona desde el panel derecho se bloquea si la zona tiene posiciones activas.** Es la misma regla del
    servidor (`La zona tiene posiciones activas; desactívelas primero.`, 409); la pantalla la adelanta con el ícono
    deshabilitado y un tooltip.

## Qué queda fuera de este lote (a propósito)

- **Barras de ocupación de los últimos 7 días en Ubicaciones y mini-gráficos históricos.** Requieren un historial diario
  que hoy no existe y un motor de trabajos programados que el dueño del producto aún está pensando. La fila de recuadros
  muestra solo el porcentaje real de hoy; no hay datos inventados.
- **Lotes 2 a 5 del plan:** Productos, Compras y Proveedores; Recibo y Recolección; Transferencias y ajustes, Conteo y
  Kárdex; Pulso.
- **Orden por columna en el servidor** para las tablas paginadas (decisión 4).
- **Capturas de pantalla** del manual de pantallas (quedan notas "captura pendiente").
- **Corrida del smoke, del CI y de Playwright** con este código (ver "Qué no se probó").
