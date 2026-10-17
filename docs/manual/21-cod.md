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
