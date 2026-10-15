# Lote 55 — Impuestos (IVU): el mecanismo (2026-10-15)

P3 del plan, parte servidor. Diseño: `Diseño/logistica-funcionalidades-maestro.md`, sección «Impuestos (IVU)» (decisiones D1 y D6 de Luis: impuestos antes de órdenes de venta; las cifras las entrega el contador; Teikem programa el mecanismo para que todo sea configuración).
**Pedido de Luis: «Haz P3 nada más».**

## Qué se construyó
- **Tablas** (update.sql sección **2026-10-15 (j)**): `TaxRate` + `TaxRateComponent`, `TenantTaxSetting`, `TaxChargeRule`, `ClientTaxExemption`, `Product.TaxCategoryLookupId`, catálogo `TaxCategory` (TAXABLE / EXEMPT / ZERO_RATE), columnas de copia del impuesto en `InvoiceLine` (`TaxRateId`, `TaxRatePct`, `TaxAmount`, `IsTaxExempt`), EntityTypes de la bitácora y el permiso `tax.manage` (TenantAdmin de la plantilla y de los roles ya clonados).
- **Cálculo puro** (`TaxRules`): neto/impuesto/total, precios con impuesto incluido o no, redondeo por línea o por documento, reparto exacto entre componentes, exento / tasa cero / cargo no gravable.
- **Servicio** (`TaxService`) y **API** `/api/v1/tax` (módulo TAX, permiso `tax.manage`): tasas (crear y cerrar vigencia; sin editar ni borrar), reglas, exenciones de clientes, categoría del producto y `calculate` (también simulador).
- Manual: capítulo 17 y FAQ «Lote 55».

## Pruebas
- xunit puras: cálculo (redondeo que difiere un centavo, incluido, componentes que siempre suman, exentos), códigos y vigencias (el último día rige), sección de SQL, permiso solo del Admin, API sin PUT/PATCH/DELETE de tasas.
- 7 pruebas contra **SQL Server real**: tasa con componentes y traslape (409), todas las validaciones con su mensaje, **cambio de tasa a mitad de mes** (el 14 usa la vieja, el 15 la nueva, y la línea guarda la tasa usada), reglas, categoría de producto / regla de cargo / exención vigente (certificado anotado), exención vencida (cobra y avisa) y de baja, aislamiento entre compañías.

## Decisiones por omisión (a revisar con Luis y su contador)
1. **No se siembra ninguna tasa** (ni valores de ejemplo): el plan decía «valores de ejemplo claramente marcados», pero una tasa de ejemplo sembrada en producción podría usarse por error; mejor que la compañía la cree.
2. **Por omisión ningún cargo (flete, manejo…) es gravable** y los precios no incluyen el impuesto; el contador decide.
3. **Tasa por omisión por código**: la regla de la compañía guarda el código (IVU) y el cálculo busca la vigente en la fecha del documento. Una sola tasa por categoría (no hay tasas reducidas por producto todavía).
4. **Exención vigente exime todo** lo que de otro modo pagaría impuesto (productos gravables, tasa cero y cargos gravables).
5. **El informe de impuestos por período queda para P4 / facturación**: no hay documentos de los que informar; el cálculo y las columnas de copia (InvoiceLine) ya están. Las columnas de `SalesOrderLine` se crean con esa tabla en P4.
6. El módulo **TAX viene apagado** por compañía (se enciende en Sistema → Módulos).
7. `tax.manage` solo para el Admin de compañía (no se da a Facturación por omisión).
