# 21 — COD: cobro contra entrega y reconciliación

Lote 84 (P8, 1 de 3, 2026-10-17). El courier cobra el COD al consignatario en la entrega, lo retiene y lo **cuadra** contra las hojas (facturas) que regresan de la calle; después se **remite** al cliente menos la comisión (remesa, lotes siguientes). Este lote construye el **cobro** y la **reconciliación**, incluida la reconciliación por **hojas escaneadas** (el código de barras de la hoja es el número de la factura). Módulo **COD** (`COD`). API: `/api/v1/cod`.

## 1. Quién puede y dónde

| Acción | Permiso |
|---|---|
| Ver órdenes con COD, su detalle y las corridas de reconciliación | `cod.view` |
| Registrar un cobro (también lo hará el chofer desde la app, P7) | `cod.collect` |
| Reconciliar (cuadrar) lo cobrado por códigos | `cod.reconcile` |

La remesa al cliente (`cod.remit`) llega en el siguiente lote.

## 2. El cobro

- Una orden con COD nace con el estatus **Por cobrar** (`PENDING`) y el monto en `CodAmount`. **`POST /cod/orders/{publicId}/collections`** registra un cobro: `method` (`CASH`, `CHECK`, `COMPANY_CHECK`, `ATH_MOVIL`, `CARD`), `amount` (mayor que cero, 2 decimales), `checkNumber` (obligatorio en cheque y company check), `reference` (ATH Móvil, depósito…), `notes` y, si se omite, la fecha es ahora.
- Se pueden registrar **varios cobros parciales**; la suma no puede pasar de lo que falta por cobrar. El COD de la orden pasa a **Parcial** y, al completarse, a **Cobrado** (cobrar todo de una vez pasa por Parcial en el historial). No se cobra en una orden en borrador o cancelada.
- Cada cobro queda en **Cobrado** hasta que se cuadra. La bitácora registra quién lo cobró.

## 3. La reconciliación (PWBack)

**`POST /cod/reconcile`** recibe una lista de **códigos** y, por cada uno, cuadra lo cobrado de su orden: los cobros pasan a **Cuadrado** y, si la orden ya estaba cobrada del todo, su COD pasa a **Cuadrado**. Si la orden está cobrada solo en parte, lo cobrado se cuadra pero el COD de la orden sigue en Parcial; lo que se cobre después queda por cuadrar.

- **De dónde salen los códigos:** del **código de barras de la hoja escaneada** (el número de la factura del cliente, o el de la orden) o escritos a mano. La lectura del código de barras de los archivos (PDF o imagen) la hace la pantalla en el navegador (lote siguiente); el API solo recibe los códigos, cada uno con su `source` (archivo y página).
- **Cómo se encuentra la orden:** primero por **número de orden**, y si no, por **número de factura del cliente**. La factura se cuenta **por cliente** (`FAC-00001` existe en cada cliente), así que:
  1. con `clientPublicId` en la solicitud (las hojas son de ese cliente) solo se busca entre sus órdenes;
  2. sin él, si el código coincide con varias órdenes se queda con las que tienen COD y, si todavía hay varias, con las que tienen cobros por cuadrar; si aun así no es una sola, el resultado es **ambiguo**.
- **`dryRun: true`** solo dice qué pasaría (vista previa): no cuadra nada ni guarda la corrida.
- Cada corrida queda guardada con sus códigos y resultados (`GET /cod/runs`, `GET /cod/runs/{id}`): quién, cuándo, el origen y cuántos se cuadraron. Un código que da un problema **no detiene** los demás.

### Resultados por código

| Resultado | Qué significa | Qué hacer |
|---|---|---|
| `RECONCILED` | Se cuadraron los cobros de la orden (`Cuadrado: N cobro(s) por X.`) | — |
| `ALREADY_RECONCILED` | `Ya estaba cuadrado.` | Nada: la hoja ya se había procesado |
| `NOT_FOUND` | `No hay ninguna orden con ese número de factura ni de orden.` | Revisar el código leído; probar a mano |
| `AMBIGUOUS` | `El código coincide con más de una orden: cuádrela a mano desde la orden.` | Indicar el cliente de las hojas o cuadrar a mano |
| `NO_COD` | `La orden no tiene COD.` | Revisar la hoja |
| `NOTHING_COLLECTED` | `La orden tiene COD, pero todavía no se registró ningún cobro.` | Registrar el cobro primero |
| `DUPLICATE` | `El código ya venía antes en esta corrida.` | La misma hoja dos veces: se ignora |

## 3b. Validaciones y mensajes

| Situación | Mensaje | HTTP |
|---|---|---|
| Sin método / método inexistente | `Indique el método de cobro.` / `El método de cobro no existe.` | 400 |
| Monto inválido | `El monto debe ser mayor que cero (hasta 2 decimales).` | 400 |
| Cheque sin número / número largo | `Indique el número del cheque.` / `El número del cheque no puede pasar de 40 caracteres.` | 400 |
| Referencia o nota largas | `La referencia no puede pasar de 80 caracteres.` / `La nota no puede pasar de 300 caracteres.` | 400 |
| Orden sin COD | `La orden no tiene COD.` | 422 |
| Orden en borrador o cancelada | `No se registra un cobro en una orden en borrador o cancelada.` | 422 |
| Cobro mayor que lo pendiente | `El cobro pasa de lo que falta por cobrar de {orden}: {monto}.` | 422 |
| Reconciliar sin códigos / código vacío | `Indique al menos un código.` | 400 |
| Código largo / más de 2000 códigos | `Un código no puede pasar de 80 caracteres.` / `Una corrida no puede tener más de 2000 códigos.` | 400 |
| Origen largo | `El origen no puede pasar de 300 caracteres.` | 400 |
| Cliente inexistente | `El cliente no existe.` | 400 |
| Orden o corrida inexistente | `Orden no encontrada.` / `Corrida no encontrada.` | 404 |

## 4. Para soporte

- Tabla `CodCollection` (ya estaba en el esquema original) con las columnas `CollectedBy`, `ReconciledBy` y `ReconcileRunId`; tablas `CodReconcileRun` y `CodReconcileItem`; EntityType `COD_COLLECTION`: sección **2026-10-17 (x)** de `Diseño/logistica-db-update.sql`.
- El ciclo formal con historial es el del **COD de la orden** (`EntityType ORDER_COD`: Por cobrar → Parcial → Cobrado → Cuadrado → Remitido). El estatus de cada cobro (`CodCollectionStatus`) cambia sin historial propio.
- La **anulación de un cobro** y el **registro desde la app del chofer con el POD** (P7) quedan fuera de este lote.

## 5. En la pantalla (Lote 85)

**Contabilidad → Procesar entregas** ([L85](frontend/l85-cod.md)): órdenes con COD y registro de cobros; **Reconciliar** subiendo las hojas escaneadas (PDF o imágenes). El **navegador** lee el código de barras de cada hoja (pdf.js y zxing-wasm, servidos con la propia aplicación; los archivos **no se suben**) y al API solo llegan los códigos con su origen (archivo y página); vista previa (`dryRun`), cuadrar con confirmación y corridas anteriores. Las páginas donde no se encontró ningún código se avisan para revisarlas a mano. Si se elige el cliente de las hojas, se manda `clientPublicId` (ver §3).

## 6. La remesa al cliente (Lote 86)

La **remesa** (`REM-#####`) le devuelve al cliente el dinero que se cobró por él en la calle, menos la **comisión**. **Teikem calcula y exporta; no emite el pago.** API: `/api/v1/cod/remittances`. Ver con `cod.view`; todo lo demás con **`cod.remit`**.

### El flujo

**Abierta → Cuadrada → Aprobada → Remitida.** Una abierta o cuadrada se puede **cancelar**.

1. **Candidatos** (`GET /cod/remittances/candidates?clientPublicId=&periodStart=&periodEnd=&commissionPct=`): lo que entraría (sin crear nada) y la comisión que se aplicaría.
2. **Crear** (`POST /cod/remittances`: cliente y período): agrupa los cobros **cuadrados** (reconciliados) de las órdenes de ese cliente cuya **fecha de cobro** cae en el período (en la zona horaria de la compañía) y que todavía no están en otra remesa. **Cada cobro entra en una sola remesa a la vez.** Lo cobrado que no se ha cuadrado no entra.
3. **Cuadrar** (`POST .../review`): se revisó el cuadre. **Aprobar** (`POST .../approve`): exige **reautenticación (AAL2)** como aprobar una corrida de facturación; queda quién y cuándo. **Remitir** (`POST .../remit`, con una referencia opcional del pago, p. ej. el número de cheque): los cobros pasan a **Remitido** y, cuando todos los cobros de una orden ya se remitieron, el **COD de la orden pasa a Remitido** (terminal).
4. **Cancelar** (`POST .../cancel`): solo abierta o cuadrada; la remesa se desactiva (sale de la lista) y sus cobros quedan libres para otra remesa.

### La comisión

Por cada cobro: **monto × % ÷ 100**, redondeado a 2 decimales (el medio hacia arriba); **neto = bruto − comisión**. El % es, en este orden: el que se pide al crear (`commissionPct`); el del **contrato activo** del cliente (`CodCommissionPct`); el **por omisión de la compañía** (`GET/PUT /cod/settings`, 8 % de fábrica; lo cambia quien tiene `cod.remit`). La remesa guarda el % que usó. (No confundir con el *cargo por COD* de la facturación, que es un cobro de Advance al cliente por el servicio.)

### Exportar

`GET /cod/remittances/{id}/export?templatePublicId=` arma el archivo de una remesa **aprobada o remitida**: **una fila por cobro**, con los datos de la remesa repetidos, con una plantilla del alcance `COD_REMITTANCE` (el mismo motor de plantillas de la facturación: CSV o TXT, delimitador, formato de fecha, columnas y encabezados). Sin plantilla se usa la primera activa (**Formato estándar**, todas las columnas, que se crea sola). Las plantillas se administran en `/export-templates?scope=COD_REMITTANCE` (con los permisos de facturación). Campos: número de la remesa, período, cliente, moneda, orden, factura, fecha, método, cheque, referencia, monto cobrado, comisión (% y monto), neto y los totales de la remesa.

### Validaciones y mensajes

| Situación | Mensaje | HTTP |
|---|---|---|
| Sin cliente / cliente inexistente | `Indique el cliente de la remesa.` / `El cliente no existe.` | 400 |
| Sin período / período invertido | `Indique el período de la remesa (desde y hasta).` / `El período no es válido: la fecha final no puede ser anterior a la inicial.` | 400 |
| Comisión fuera de 0–100 o con más de 2 decimales | `La comisión debe estar entre 0 y 100 (hasta 2 decimales).` | 400 |
| Comisión por omisión inválida | `La comisión por omisión debe estar entre 0 y 100 (hasta 2 decimales).` | 400 |
| Nada que remitir | `No hay cobros cuadrados de ese cliente en ese período para remitir.` | 422 |
| Cuadrar una que no está abierta | `Solo se cuadra una remesa abierta.` | 422 |
| Aprobar una que no está cuadrada | `Solo se aprueba una remesa cuadrada.` | 422 |
| Remitir una que no está aprobada | `Solo se remite una remesa aprobada.` | 422 |
| Cancelar una aprobada o remitida | `Solo se cancela una remesa abierta o cuadrada (una aprobada o remitida ya no).` | 422 |
| Exportar una que no está aprobada ni remitida | `Solo se exporta una remesa aprobada o remitida.` | 422 |
| Referencia de pago larga | `La referencia no puede pasar de 80 caracteres.` | 400 |
| Aprobar sin reautenticación | `Esta acción requiere reautenticación reciente (AAL2).` (`aal2_required`) | 403 |
| Remesa inexistente (o cancelada) | `Remesa no encontrada.` | 404 |

### Para soporte

- Tablas `CodRemittanceBatch` y `CodRemittanceLine` (ya estaban en el esquema original; `NetRemitted` y `NetAmount` son columnas calculadas); `Tenant.CodDefaultCommissionPct`; EntityType `COD_REMITTANCE`; contador `CODREMIT` y su CHECK: sección **2026-10-17 (y)** de `Diseño/logistica-db-update.sql`.
- Una remesa **cancelada** queda en la base con `IsActive = 0` y sus líneas (no se borra); sus cobros vuelven a quedar sin remesa.
- La **comisión del contrato** se lee de `Contract.CodCommissionPct`; todavía no hay un campo para editarla en la pantalla de contratos (se puede escribir por base o API); la pantalla de remesas (Lote 87) muestra de dónde sale el % aplicado.

