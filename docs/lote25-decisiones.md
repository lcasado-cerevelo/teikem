# Lote 25 — Conteo abierto con varios productos y posición opcional (2026-10-05)

Pedido del dueño: "Por producto" creaba **un conteo por producto**; quiere **abrir un conteo y empezar a contar productos**, y terminar con un conteo de varios
productos, sin estar contando por posición. La posición debe ser **opcional**: que la app y la web **muestren la posición actual con la opción de
cambiarla**, y que con solo la cantidad y confirmar la línea se guarde.

Decisiones del dueño (2026-10-05): posición por defecto = **la única con existencia; con varias, elegir** · producto **sin existencia = pedir la posición** ·
producto **repetido = abrir su línea para corregir** (no sumar) · **servidor + app + web en un solo lote**.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Conteo vacío | `POST /cycle-counts` con `allowEmpty` y **sin producto** abre un conteo `OPEN` de origen `PRODUCT` sin líneas (nunca "todo el almacén") | `CycleCountService.CreateAsync` |
| Dónde está el producto | `GET /cycle-counts/{id}/product-bins?productPublicId=` → posiciones y lotes con existencia y la línea que el conteo ya tiene (`lineId`); **sin cantidades** (sirve a ciegas) | `CycleCountService.ProductBinsAsync`, `CycleCountsController.ProductBins` (`warehouse.count.capture`) |
| Posición opcional | `POST /cycle-counts/{id}/lines` sin `binId`: usa la única posición con existencia; ninguna o varias → 400 en `binId` con mensaje exacto | `CycleCountService.DefaultBinIdAsync`, `CycleCountRules.SingleBin/BinRequiredNoStock/BinAmbiguous` |
| App | "Por producto" abre UN conteo vacío al primer escaneo y se agregan productos (posición propuesta + Cambiar posición, elegir con varias, escanear/crear si no hay, corregir un repetido, Terminar con un solo lote) | `OpenCountView.tsx`, `localCount.ts` (modo `OPEN`), `countApi.ts`, `countLogic.ts`, `count.tsx` |
| Web | "Nuevo conteo → Por producto" con producto **opcional** (vacío = conteo abierto); el escáner del detalle agrega al conteo un producto que no estaba (`by-barcode` → "Agregar lo encontrado" con el producto puesto); la posición del modal es opcional | `CreateCountModal`, `CountScanBox`, `CountDetailPanel`, `CountLineModals` |

**Sin cambio de esquema:** `CycleCountLine.WarehouseBinId` sigue `NOT NULL` y parte de la identidad de la línea (y del ajuste al reconciliar); la posición se **resuelve** en el
servidor/app, no se deja sin posición. Finish y Reconcile no cambian.

## Cómo se probó

- Servidor: `dotnet test`: **3014 pruebas** pasan (nuevas en `CycleCountByProductTests`: conteo abierto vacío sin tomar el almacén, `product-bins` sin cantidades y con
  `lineId`, agregar sin posición con una / ninguna / varias, regla `SingleBin`; ajustadas la de `allowEmpty` y el mapa de seguridad —130 acciones—).
- App: `tsc`, oxlint y jest (**388**): `countOpenProductsScreen.test.tsx` (flujo completo: abrir vacío, una posición, varias, repetido que corrige, Terminar con un lote;
  sin existencia con posición escaneada del aparato; retomar y quitar), pruebas de `countLogic`, `countApi` y `localCount`. Cinco pruebas del flujo anterior (un producto en todas
  sus posiciones) siembran ahora el conteo local en vez de escanear.
- Web: vitest (**1250**): modal sin producto (`allowEmpty`), escáner de producto ajeno que abre "Agregar lo encontrado" y manda `binId: null`; `npm run build`.
- **No probado**: recorrido Playwright (`loteF13-conteo-web.spec.ts` ajustado, no corrido), `scripts/smoke.sh` (sin paso nuevo) ni el aparato real.

## Decisiones a revisar

1. **Producto con serie** sigue sin contarse desde la app (no hay captura de series); en la web, desde "Agregar lo encontrado".
2. **Sin señal** no se puede abrir el conteo ni buscar dónde está el producto; con el conteo ya abierto se pide escanear la posición (tabla local) y todo se manda al terminar.
3. **Un conteo abierto largo** bloquea las posiciones de sus líneas para "Conteo de lo cambiado" solo cuando ya tiene líneas (como cualquier conteo Pendiente).
4. **Tope** de 1000 líneas por conteo (el de siempre).
5. El conteo "un producto en todas sus posiciones" (Lote A4) ya no se abre desde la app; la web lo sigue ofreciendo (Por producto con un producto elegido).
