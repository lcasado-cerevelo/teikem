# Lote 44 — Labels de las órdenes: dirección en la lista y escaneo por número de paquete (servidor de F-G2) (2026-10-12)

Servidor mínimo del bloque F-G2 (labels y documentos de las órdenes). La pantalla es el lote de frontend F-G2
(`docs/frontend/loteFG2-decisiones.md`); el motor de impresión es el de F-G1 (`docs/lote43-decisiones.md`).

## Qué se construyó

- **Lista de órdenes con la dirección de entrega** (D-FG8): `OrderListItemDto` gana `ConsigneeLine1`, `ConsigneeLine2` y `ConsigneeState` al
  final y con valor por defecto (la proyección de `OrderReadService` los llena de la parada de entrega). Con eso los labels, el waybill
  summary y la packing list salen de `GET /orders` (todas las páginas, sin tope) sin un endpoint nuevo ni pedir la ficha de cada orden.
  También los trae `GET /orders/lookup` (misma proyección).
- **Escaneo por número de paquete** (cambio mínimo aceptado por Luis): `OrderReadService.LookupAsync` busca, como **cuarta precedencia**
  y solo si número de envío, empaque y factura no coinciden, las órdenes con una línea de carga ACTIVA cuyo `PackageNumber` es el código
  (`MatchedBy = "PACKAGE"`). Lo usan `GET /orders/lookup` y la estación de escaneo (sin otro cambio en su flujo). Dos o más órdenes con el
  mismo número = ambiguo (`MULTIPLE_MATCHES`) con su mensaje propio: «El número de paquete está en {n} órdenes (de clientes distintos o
  repetido); escanee el empaque o el número de envío.» (`OutboundScanRules.MultiplePackageMatchesMessage`).
- **Índice** `IX_CargoLine_PackageNumber (PackageNumber) INCLUDE (TransportOrderId) WHERE IsActive = 1 AND PackageNumber IS NOT NULL` en la
  sección nueva e idempotente «2026-10-12 (e)» al final de `Diseño/logistica-db-update.sql`, con su espejo en el modelo EF. No es único a
  propósito (el contador PACKAGE es por cliente y un número tecleado puede repetirse).
- `openapi.json` y los `schema.d.ts` de la web y de la app regenerados (solo agrega).

## Cómo se probó

- `dotnet test`: **3.664 pruebas en verde, 0 omitidas** (también las de aislamiento contra SQL Server real con `TEIKEM_TEST_SQLSERVER`).
  Nuevas en `PackageLookupTests`: cuarta precedencia (y que orden/empaque/factura ganan), líneas inactivas y otra compañía no coinciden, el
  mismo número en dos órdenes es ambiguo con su mensaje (y los demás códigos conservan el de siempre), la lista trae la dirección y la sección
  SQL con su espejo EF.
- Base desechable nueva `Teikem_fg2` (`db-init` con las secciones (d) y (e)); `scripts/smoke.sh` completo con el paso nuevo «lookup por número
  de paquete» (`matchedBy` PACKAGE, la orden correcta y su `consigneeLine1`).

## Decisiones tomadas por default (para el informe de fin de P1)

| | Decisión | Alternativa |
|---|---|---|
| D-FG8 | Campos de dirección en la lista en vez de un endpoint de datos de label | `GET /orders/label-data` con los mismos filtros |
| D-FG9/D-FG12 (Luis) | Código de barras = número del paquete; un label por pieza | Número de empaque; uno por orden |
| D-FG17 (Luis) | La cantidad de la línea = número de labels; el mismo número de paquete repetido en cada pieza de la línea, con «Paquete i de N» corrido sobre la orden | Un número distinto por pieza (exigiría numerar piezas en el servidor) |
| D-FG27 | Número de paquete como **cuarta** precedencia del lookup (solo si los otros tres no coinciden), para no cambiar ningún resultado de hoy | Buscarlo junto a los demás en la misma consulta |
| D-FG28 | Número de paquete repetido = ambiguo con mensaje propio; no se hace único (el contador es por cliente desde el Lote 2 y hay números tecleados) | Hacerlo único por compañía (cambiaría la numeración en producción) |
