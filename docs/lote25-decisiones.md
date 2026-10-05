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

## Adenda (2026-10-05): quién cuenta, asignación automática desde la app y PIN

- **Quién cuenta en la lista web.** `CycleCountDto` gana `createdByName` (quien abrió el conteo) y `capturedByNames` (hasta 3 que han capturado alguna línea; sin cantidades, también a ciegas). La lista y el
  encabezado del detalle dicen `Cuenta: …` (quienes capturaron) o, si nadie ha capturado todavía, `Abierto por …`. Lo capturado por la app llega al terminar (la app manda un solo lote), así que mientras se cuenta
  se ve `Abierto por …`. Un conteo por producto abierto y sin líneas se titula `Conteo abierto: sin productos todavía` (antes decía "Sin posiciones").
- **Qué es "Sin asignar".** La tarea COUNT del conteo no tiene usuario asignado (se asigna con el ícono de la lista, `warehouse.manage`).
- **Asignación automática.** `CycleCountCreateRequest.AssignToMe` (al final, por defecto `false`): la tarea COUNT nace asignada a quien crea el conteo. La app de almacén lo manda siempre (quien abre el conteo es quien lo
  cuenta), así que esos conteos nacen asignados y no hace falta asignarlos. El ícono de asignar se conserva para los conteos creados desde la web y para reasignar.
- **PIN.** Los campos del PIN (asignar/restablecer en Usuarios y cambiar el propio en Mi cuenta) llevan `autocomplete="new-password"` para que el navegador no los rellene con la contraseña guardada, y el primero tiene el foco.
- **No hecho: ver lo contado en la web mientras se cuenta en la app.** Hoy la app manda un solo lote al terminar. Enviar cada línea al servidor al agregarla es viable (hay `PUT /lines/batch` y la web se podría refrescar
  cada pocos segundos) pero cambia reglas: quitar o corregir una línea ya enviada necesita un endpoint nuevo, y la cola de salida deja de ser todo-o-nada. Pendiente de decisión del dueño.

## Adenda 2 (2026-10-05): despacho con posición sugerida / lote FEFO y panel "Dónde está"

- **App, Despacho.** Cursor en la cantidad; posición **sugerida** (la primera según la regla de salida del servidor, calculada en la app sobre `GET /inventory/balances`, `onlyAvailable`); con producto **con lote** la posición es obligatoria
  (la del próximo lote en salir, FEFO) y se rechaza otra o más de lo que hay; sin señal, sin sugerencia. Reglas en `dispatchLogic.ts` (`fefoOrder`, `nextStockOption`, `checkLotBin`), pantalla `dispatch.tsx`. No hay cambio de servidor.
- **Web, Editar producto.** Panel plegable **Dónde está (existencia por posición)** (`StockByBin` en `ProductEditorModal.tsx`): en mano, reservado y disponible por posición y lote, con total; se pide solo al abrirlo.
- **Decisiones a revisar:** (1) la regla FEFO está **replicada** en la app (no hay endpoint de sugerencia): si cambia `StockAllocator`, hay que cambiar `dispatchLogic.ts`; un endpoint de sugerencia en el servidor la dejaría en un solo lugar. (2) El FEFO obligatorio solo
  se exige con señal. (3) Un producto con serie no se despacha desde la app (sin cambio).

