# 17 — Impuestos (IVU)

Lote 55 (2026-10-15). Teikem trae el **mecanismo completo** de impuestos y la compañía lo **configura**: ninguna tasa, porcentaje ni regla está fija en el código. Cuando una tasa cambia, no hay que tocar el programa: se cierra la vigencia de la anterior y se crea la nueva.
Este capítulo describe el servidor (API). Las órdenes de venta (P4) y la facturación usarán este cálculo; la pantalla se describe en [L56 — Impuestos](frontend/l56-impuestos.md).

> **Importante:** Teikem **no siembra tasas ni cifras legales**. Las tasas, sus componentes (estatal, municipal…) y qué es gravable los define el contador de la compañía.

## 1. Quién puede

- Permiso **`tax.manage`** («Administrar impuestos (tasas, exenciones)»). Lo trae el **Admin de compañía** (plantilla y roles ya clonados); a los demás roles se lo asigna el administrador.
- Módulo **Impuestos (`TAX`)**: viene **apagado**; se enciende por compañía (Sistema → Módulos). Con el módulo apagado todo el API responde 403.
- Quienes no tienen el permiso solo ven el **resultado** del impuesto en la orden y en la factura.

## 2. Tasas

- Una **tasa** tiene un código (p. ej. `IVU`), un nombre, una **vigencia** (desde / hasta) y uno o más **componentes** (p. ej. Estatal 10.5 % + Municipal 1 %). El **porcentaje de la tasa es la suma de sus componentes**.
- **Una tasa no se edita.** Si cambia, se **cierra** su vigencia (último día en que rige) y se **crea otra** con el mismo código que empieza al día siguiente. El historial se conserva y los documentos ya emitidos guardan la tasa con que se calcularon.
- Para un mismo código, **las vigencias no se pueden traslapar**.
- Estado: **Vigente** (rige hoy), **Programada** (empieza después) o **Cerrada** (ya terminó).

## 3. Reglas de la compañía

Una sola fila por compañía; se crea sola con valores por omisión (precios sin impuesto incluido, redondeo por línea, sin tasa por omisión, ningún cargo gravable).

- **Precios con impuesto incluido**: si está activo, el monto de cada línea ya trae el impuesto (se extrae); si no, se suma encima.
- **Redondeo**: `LINE` redondea el impuesto de **cada línea** a 2 decimales y suma; `DOCUMENT` suma sin redondear y redondea **una sola vez** el impuesto del documento (puede diferir un centavo).
- **Tasa por omisión**: el código de la tasa que se usa en lo gravable (se busca la vigente en la fecha del documento).
- **Cargos gravables**: por tipo de cargo (flete, manejo, recargo, recogido) se indica si paga impuesto. La **venta de producto** la decide el producto.

## 4. Qué paga impuesto

| Línea | Resultado |
|---|---|
| Producto **gravable** (por omisión, sin categoría) | Impuesto con la tasa vigente (`TAXABLE`) |
| Producto **tasa cero** | Gravable al 0 % (`ZERO_RATE`) |
| Producto **exento** | Sin impuesto (`EXEMPT_PRODUCT`, marcado como exento) |
| Cargo (flete, manejo…) | Según la regla del cargo: gravable (`TAXABLE`) o `NOT_TAXABLE_CHARGE` |
| Cliente con **exención vigente** | Nada paga impuesto (`EXEMPT_CLIENT`), con el **certificado anotado** |
| Cliente con exención **vencida** | Se cobra el impuesto y el cálculo trae un **aviso** con el certificado que venció |

La categoría de impuesto de un producto es `TAXABLE`, `EXEMPT` o `ZERO_RATE` (se guarda vacía cuando es gravable).

## 5. Exenciones de clientes

Un cliente tiene una o más exenciones: **tipo**, **número de certificado**, **vigente desde / hasta** (sin fecha final = abierta) y notas. Estado: **Vigente**, **Programada**, **Vencida** o **De baja**. Nunca se borran: se dan de baja y se pueden reactivar.

## 6. Cálculo (lo que usarán las órdenes de venta y la factura)

`POST /api/v1/tax/calculate` recibe cliente (opcional), fecha (opcional, hoy por omisión) y las líneas (producto **o** tipo de cargo, y monto). Devuelve, por línea: neto, impuesto, total, motivo, si quedó exenta, **la tasa y el porcentaje usados** (la «copia» que cada línea guardará) y el reparto por componente; y para todo el documento: subtotal, impuesto, total, impuesto por componente y avisos. **No guarda nada.** También sirve de **simulador** para comprobar las reglas con el contador.

## 7. API (todo con `tax.manage`, módulo TAX)

`GET /tax/rates?activeOnly=` · `POST /tax/rates` · `POST /tax/rates/{id}/close` · `GET|PATCH /tax/settings` · `GET /tax/exemptions?clientId=` · `POST /tax/exemptions` · `PATCH /tax/exemptions/{id}` · `POST /tax/exemptions/{id}/deactivate|reactivate` · `GET|PUT /tax/products/{id}/category` · `POST /tax/calculate`. No existe PUT/PATCH/DELETE de tasas.

## 8. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Código de tasa vacío | `Escriba el código de la tasa.` | 400 (`code`) |
| Código de más de 30 caracteres | `El código de la tasa no puede pasar de 30 caracteres.` | 400 |
| Código con caracteres no permitidos | `El código solo puede llevar letras, números, guion y guion bajo.` | 400 |
| Nombre vacío | `Escriba el nombre de la tasa.` | 400 (`name`) |
| Nombre de más de 100 caracteres | `El nombre no puede pasar de 100 caracteres.` | 400 |
| Sin fecha de inicio | `Escriba desde qué fecha rige la tasa.` | 400 (`effectiveFrom`) |
| Fecha final anterior a la inicial | `La fecha final no puede ser anterior a la inicial.` | 400 |
| Tasa sin componentes | `La tasa necesita al menos un componente (por ejemplo, Estatal).` | 400 (`components`) |
| Dos componentes con el mismo código | `Hay dos componentes con el mismo código.` | 400 |
| Porcentaje de un componente fuera de 0 a 100 | `El porcentaje de cada componente debe estar entre 0 y 100.` | 400 |
| Suma de componentes mayor de 100 | `La suma de los componentes no puede pasar de 100 %.` | 400 |
| Vigencias que se traslapan con el mismo código | `Ya hay una tasa con ese código que cubre esas fechas. Cierre la vigencia de la anterior primero.` | 409 |
| Tasa inexistente (o de otra compañía) | `Tasa no encontrada.` | 404 |
| Cerrar una tasa que ya tiene fecha final | `La tasa ya tiene fecha final.` | 409 |
| Sin fecha de cierre | `Escriba el último día en que rige la tasa.` | 400 |
| Cierre anterior al inicio de la vigencia | `La fecha de cierre no puede ser anterior al inicio de la vigencia.` | 400 |
| Redondeo distinto de LINE / DOCUMENT | `El redondeo debe ser LINE (por línea) o DOCUMENT (por documento).` | 400 |
| Tasa por omisión que no existe | `No hay ninguna tasa con ese código.` | 400 |
| Regla de un tipo de cargo que no existe (o PRODUCT_SALE) | `Tipo de cargo no válido.` | 400 |
| Exención sin tipo | `Escriba el tipo de exención.` | 400 |
| Exención sin certificado | `Escriba el número del certificado.` | 400 |
| Tipo de más de 60 / certificado de más de 60 / notas de más de 300 | `El tipo de exención no puede pasar de 60 caracteres.` / `El número del certificado no puede pasar de 60 caracteres.` / `Las notas no pueden pasar de 300 caracteres.` | 400 |
| Cliente inexistente (o de otra compañía) | `Cliente no encontrado.` | 404 |
| Exención inexistente | `Exención no encontrada.` | 404 |
| Producto inexistente | `Producto no encontrado.` | 404 |
| Categoría distinta de TAXABLE / EXEMPT / ZERO_RATE | `La categoría de impuesto debe ser TAXABLE, EXEMPT o ZERO_RATE.` | 400 |
| Calcular sin líneas | `Agregue al menos una línea para calcular.` | 400 |
| Más de 500 líneas | `Se pueden calcular hasta 500 líneas a la vez.` | 400 |
| Monto vacío o negativo | `El monto de la línea no es válido.` | 400 |
| Línea sin producto ni tipo de cargo | `Cada línea necesita un producto o un tipo de cargo.` | 400 |
| Hay líneas gravables y no se escogió la tasa por omisión | `Escoja la tasa por omisión en Impuestos → Reglas.` | 400 |
| No hay tasa vigente en la fecha | `No hay una tasa de impuesto vigente en esa fecha. Configure una en Impuestos → Tasas.` | 400 |
| Sin el permiso o con el módulo apagado | «Sin permiso» | 403 |

## 9. Para soporte

- Tablas `TaxRate`, `TaxRateComponent`, `TenantTaxSetting`, `TaxChargeRule`, `ClientTaxExemption`, columna `Product.TaxCategoryLookupId` y las columnas `TaxRateId`, `TaxRatePct`, `TaxAmount`, `IsTaxExempt` de `InvoiceLine` (sección **2026-10-15 (j)** de `Diseño/logistica-db-update.sql`). La bitácora registra `TAX_RATE`, `TAX_EXEMPTION` y `TAX_SETTING`.
- El **informe de impuestos por período** y la copia del impuesto en cada línea de orden de venta y de factura se conectan cuando existan esos documentos (P4 y facturación): hoy no hay facturas de las que informar.
