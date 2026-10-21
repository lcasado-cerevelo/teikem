# Lote 181 (H1) — Contabilización de compras y de despachos (trabajo autónomo, 2026-10-21)

Pedido de Luis (2026-10-21): hacer H1 «sin mi supervisión», avisando al final. La regla viene del análisis del plan y **está por confirmar con el dueño** (ver «Decisiones a revisar»).

## Qué se construyó
- **Corridas de contabilización** `AccountingRun` / `AccountingRunLine` (tablas nuevas en `Diseño/logistica-db-update.sql`, sección 2026-10-21 Lote 181; dominio de estatus `AccountingRunStatus`, EntityType `ACCOUNTING_RUN`, contadores `ACCTPURCH` → `CMP-#####` y `ACCTDISP` → `DSP-#####`). Flujo **Generada → Aceptada → Exportada**; **Cancelada** desde generada o aceptada.
- **Regla (por dueño del inventario):** se contabiliza lo **propio** (producto sin cliente dueño); lo de un cliente no. Sin importar el documento de origen. Con el **costo ya congelado** de cada línea (capa del recibo del Lote 147; costo de la salida del Lote 148); sin recalcular.
  - Compras: líneas de recibos **confirmados** del período (por día de la compañía), no devoluciones de una venta, con lo recibido.
  - Despachos: líneas de recolecciones y despachos **vigentes** (no eliminadas ni revertidas).
  - Una línea no entra en dos corridas vivas: índice único filtrado `UX_AccountingRunLine_Source` (se libera al cancelar).
- **`AccountingRunService`** + dos controladores finos: `PurchaseAccountingController` (`/api/v1/purchase-accounting`, módulo de compras, `purchasing.view` / `purchasing.manage`) y `DispatchAccountingController` (`/api/v1/dispatch-accounting`, módulo de almacén, `billing.view` / `billing.generate` / `billing.export`). **Sin permisos nuevos**: se reutilizan los que ya daban la entrada a cada pantalla (`MenuPermissions`: `billing.generate ⇒ dispatchaccounting.view`; `dispatchaccounting.view ⇒ billing.view`).
- **Exportación con plantillas** (el motor de los Lotes 77/86/95): alcances nuevos `PURCHASE_ACCOUNTING` y `DISPATCH_ACCOUNTING` con su catálogo de campos y su «Formato estándar». El controlador de plantillas ya no exige el módulo de Facturación (acepta cualquiera de Facturación, COD, Liquidación a choferes, Compras o Inventario por lote: `RequireAnyModule`) y acepta los permisos de compras.
- **Web:** las dos pantallas de Contabilidad (hasta hoy «llega en un lote posterior»): lista con filtros y exportación, «Generar corrida» con vista previa, ficha, aceptar, cancelar, exportar con plantilla (ver archivo, configurar, descargar). `TemplateEditor` de Facturación se reutiliza para cualquier alcance. Se **quitó la última ruta pendiente** de `routes.tsx` (la de despachos pasa de módulo `LTL_GROUND` a `WMS_LOTSERIAL`, que es el de lo que sale del almacén).
- Manual: capítulo 43 + L181 con capturas + FAQ.

## Cómo se probó
- xunit `AccountingRunTests` (6: reglas puras, solo lo propio y confirmado con su costo, línea nunca en dos corridas y cancelar la libera, aceptar → exportar con el archivo exacto, despachos y tipos que no se mezclan, período y validaciones) y `AccountingRunSqlTests` contra **SQL Server real** (las uniones y la subconsulta se traducen y el índice único filtrado rechaza el duplicado y lo permite tras cancelar).
- `scripts/smoke-contabilidad.sh` contra el **API real** (recibo ciego de producto propio y de cliente, recolecciones, corrida de compras: vista previa, generar, 422 de repetir, 422 de exportar sin aceptar, aceptar, exportar con la plantilla, 404 del otro tipo; despachos: generar, cancelar, volver a generar). Agregado al CI. **Encontró un defecto real:** el contador real rechazaba los tipos nuevos (`NumberingRules.IsKnownKind`) — la misma lección del aviso de salida (las pruebas usan un contador de mentira); queda cubierto por una aserción.
- `db-init` limpio y `db-update` (Lote «menores», orden update.sql → diff) con la sección nueva.
- vitest (8: reglas; pantalla: lista, generar con vista previa, aceptar, exportar con plantilla y descarga, cancelar con confirmación, solo lectura); `routes`/`navigation`/`Placeholder` actualizados; recorrido Playwright `lote181-contabilidad.spec.ts` (escritorio y móvil, en fila: una corrida junta todo lo propio, dos a la vez se quitarían las líneas).
- De paso se arreglaron tres defectos heredados que esta pasada destapó: el aviso «precio distinto del costo del producto» (Lote 180) leía `purchaseCost` del nivel equivocado (`data.product.purchaseCost`, nunca avisaba), las firmas de contratos de `WmsContractsTests` (zonas, tandas, maestra) y el mapa de auditoría (`PickGroup`, `ReceiptBatch`: sin bitácora propia, a propósito).

## Decisiones a revisar con Luis
1. **La regla de dueño** (propio sí, de cliente no, sin importar el documento) y **el costo congelado** son del plan, sin confirmar. Si el cliente 3PL también debe contabilizarse (p. ej. para facturarle el costo), se agrega un tercer tipo o un filtro.
2. **Un recibo cuenta cuando se cierra** (con tandas, no tanda por tanda) y por **la fecha de recepción**; un despacho cuenta por la fecha de la recolección.
3. **Lo que se elimina o revierte después de contabilizado no se corrige solo** (se cancela la corrida si no se exportó; si ya se exportó, se ajusta en el sistema contable).
4. **Sin aprobación fuerte (AAL2)**: es un registro de costo ya fijado, más ligero que facturación o liquidación (diseño del documento maestro: «Generada → Aceptada → Exportada»).
5. Las líneas **sin costo** se contabilizan en blanco y se avisan (no se inventa un costo).
6. El **formato definitivo** del archivo se ajusta con una muestra real del sistema contable de la compañía (como en Facturación).
7. Sin tope de líneas por corrida: la ficha las devuelve todas (la pantalla pinta 200 y el archivo trae todas). Con corridas de miles de líneas habrá que paginar.
