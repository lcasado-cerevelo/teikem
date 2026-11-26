# 56 — Documentos con vigencia, exención de IVU atada a su certificado y avisos de vencimiento

<!-- kemi:staff -->

Lote 248 (2026-11-26; Lote D del plan de Solutions). Cuatro cosas nuevas:

1. **Documentos del cliente con número y vigencia.** Cada documento (certificado, permiso, licencia…) puede llevar su número y sus fechas «desde» y «hasta». Si el tipo «lleva vigencia», el «hasta» es obligatorio.
2. **La exención de IVU del cliente queda atada al documento que la respalda** (el certificado). El número y la vigencia de la exención son los del documento. Hay tres tipos de certificado nuevos: estatal, municipal y total.
3. **Exención de producto por temporada**: un decreto que exime del IVU a todos los productos, o solo a categorías y productos escogidos, entre dos fechas.
4. **Avisos de vencimiento**: un trabajo diario avisa a los vendedores (y, si la compañía lo enciende, al cliente) cuando un documento está por vencer o ya venció, y también cuando vence un precio especial. Además hay una lista «Documentos por vencer» y un aviso, al vender, cuando la exención del cliente vence o ya venció.

> **Esto toca dinero.** Una exención vinculada vale lo que dice su documento: si el documento vence, el IVU se vuelve a cobrar. Las exenciones que ya existían **no cambian de monto**: siguen valiendo igual y salen marcadas «Falta el documento» (sección 4).

> **Ninguna acción de este capítulo manda un correo por sí sola.** Los avisos salen por las **reglas de aviso** (Sistema → Reglas de aviso). Sin regla, el sistema anota el aviso y no lo entrega a nadie (sección 7).

Pantallas con capturas: [L248](frontend/l248-documentos-ivu.md). Cada mensaje de error está en la [FAQ](faq.md). Lo que dice este capítulo sobre el impuesto completa el [17 — Impuestos (IVU)](17-impuestos.md) y el [55 — Precios especiales y ofertas](55-precios-especiales-y-ofertas.md) (el precio especial sigue siendo un precio **antes** del IVU).

## 1. Dónde está y quién puede

- **Dónde:**
  - **Listas → Clientes y contratos** → ficha del cliente → paneles **«Documentos del cliente»**, **«Impuestos»** y **«Avisos al cliente»** (`/catalog/clients`).
  - **Listas → Documentos por vencer** (`/catalog/expiring-documents`).
  - **Contabilidad → Impuestos** (`/money/tax`) → pestañas **«Exenciones»** y **«Exención por temporada»**.
  - **Sistema → Ajustes → Operación → «Avisos de vencimiento»** (`/system/settings`; solo se ve con el módulo de catálogo).
  - **Sistema → Catálogos de valores** → «Tipos de documento del cliente» (las marcas «Lleva vigencia» y «Es de impuestos»).
  - **Sistema → Reglas de aviso** (eventos «Documento del cliente por vencer», «Documento del cliente vencido» y «Precio especial por vencer») y **Sistema → Trabajos programados** («Avisos de documentos por vencer»).
  - El **pedido guiado**, su resumen y la **ventana de la orden**: el aviso de exención del cliente.
- **Módulos:** Catálogo (`CATALOG`: documentos, lista y avisos), Impuestos (`TAX`: exenciones y temporadas) y Órdenes de venta (`SALES_ORDERS`: aviso al vender y precio especial por vencer). Con un módulo apagado, la ruta responde 403 con `El módulo 'TAX' no está habilitado para esta compañía.` (con el nombre del módulo que corresponda).
- **Permisos** (no hay permisos nuevos):

| Qué | Permiso | Módulo |
|---|---|---|
| Ver los documentos de un cliente | `clients.read` | `CATALOG` |
| Subir, editar o quitar un documento | `clients.update` (o `clients.create_basic` si el cliente es **prospecto**) | `CATALOG` |
| Cambiar el número o las fechas de un documento que respalda una exención de IVU | además `tax.manage` | `CATALOG` y `TAX` |
| Ver la lista «Documentos por vencer» | `clients.read` | `CATALOG` |
| Ver en esa lista las exenciones sin documento | además `tax.manage` | `TAX` |
| Ver y cambiar exenciones de IVU, aplicarlas desde un documento, vincularlas | `tax.manage` | `TAX` |
| Captura única de la exención desde la ficha (`/clients/{id}/tax-exemptions`) | `tax.manage` | `TAX` |
| Crear, cambiar, dar de baja y reactivar períodos de exención por temporada | `tax.manage` | `TAX` |
| Ver el aviso de exención al vender | `sales.create` o `sales.view` | `SALES_ORDERS` |
| Cambiar los umbrales y los interruptores de «Avisos de vencimiento» | `admin.tenant` | — |
| Armar a quién llega cada aviso | `notifications.manage` | — |
| Correr el trabajo a mano | `jobs.manage` | — |
| Leer la fuente de datos «Documentos del cliente» (`CLIENT_DOCUMENT`) | `clients.read` | `CATALOG` |

- `TenantId` sale de la sesión. Un cliente, un documento, un período, un producto o una categoría de otra compañía responde 404, sin decir si existe.
- Sin el permiso, la ruta responde 403 con `Falta el permiso 'tax.manage'.` (con el código que falte) y queda un evento `PERMISSION_DENIED` en los eventos de seguridad.
- **Ni el portal ni la app del cliente muestran documentos ni avisos de vencimiento** en este lote.

## 2. Documentos del cliente con número y vigencia

**Qué hace.** Guarda, junto al archivo, el **número** del documento y su vigencia: «Desde» y «Hasta» (los dos días cuentan). El sistema calcula solo cuántos días faltan y el **estado** (sección 9). El estado nunca se escribe a mano.

**Quién puede.** Ver: `clients.read`. Subir, editar y quitar: `clients.update`, o `clients.create_basic` mientras el cliente es prospecto. Módulo `CATALOG`.

**Cómo se usa (pantalla).** Ficha del cliente → **«Documentos del cliente»**. Cada fila muestra el tipo, el número, las fechas (desde → hasta), los días y el estado: **Vigente**, **Por vencer**, **Vencido**, **Renovado**, **Falta «hasta»** o **Sin vigencia**. La marca **«Respalda exención»** sale en el documento que sostiene una exención de IVU.
- **Subir:** escoja el tipo, el archivo (JPG, PNG, WEBP, GIF o PDF), y escriba el número y las fechas. Si el tipo lleva vigencia, el campo dice **«Hasta (obligatorio)»**. Un «Hasta» ya pasado **sí** se acepta: es el registro de un documento real.
- **Editar** (lápiz): número, «Desde», «Hasta» y nota. El tipo y el archivo no se cambian.
- **Quitar** (papelera): el documento sale de la lista; el archivo no se borra del almacén.
- Si el tipo es **«de impuestos»** y usted tiene `tax.manage` con el módulo `TAX` encendido, al subirlo el programa pregunta **«¿Aplicar la exención de IVU?»**. «Sí, aplicar» abre la captura de la exención con ese documento.

**Cómo se usa (API).**

| Qué | Ruta | Permiso |
|---|---|---|
| Listar | `GET /api/v1/clients/{clientPublicId}/documents` | `clients.read` |
| Subir (multipart) | `POST /api/v1/clients/{clientPublicId}/documents` | `clients.update` o `clients.create_basic` (prospecto) |
| Editar | `PATCH /api/v1/clients/{clientPublicId}/documents/{documentPublicId}` | igual (y `tax.manage` si respalda una exención) |
| Quitar | `DELETE /api/v1/clients/{clientPublicId}/documents/{documentPublicId}` | igual |

Subir (multipart/form-data): `file`, `documentType` (código del catálogo, por ejemplo `IVU_EXEMPTION_STATE`), `documentNumber`, `validFrom` y `validTo` (formato `AAAA-MM-DD`) y `notes`. Responde el documento con `daysToExpiry`, `expiryState`, `isTax`, `requiresValidTo`, `backsExemption`, `rowVersion` y, solo al subir, `suggestExemption` (el tipo es de impuestos, usted tiene `tax.manage` y `TAX` está encendido).

Editar (JSON). `null` o no mandarlo = sin cambio; `documentNumber` o `notes` vacíos = quitarlos; `clearValidFrom` / `clearValidTo` = quitar la fecha. `rowVersion` es el de la lista: si el documento cambió, 409.

```json
{
  "documentNumber": "E-12345",
  "validFrom": "2026-01-01",
  "validTo": "2027-12-31",
  "notes": "Renovado por Hacienda",
  "rowVersion": "AAAAAAAAB9E="
}
```

**Campos y reglas.**
- `documentType`: código **activo** del catálogo «Tipos de documento del cliente» y no apagado por la compañía. Ya no es una lista fija de tres: la compañía puede agregar tipos.
- `documentNumber`: hasta 60 caracteres. Se recorta; vacío = sin número.
- `validFrom`, `validTo`: «Hasta» no puede ser anterior a «Desde». Con un tipo que lleva vigencia, «Hasta» es obligatorio.
- `notes`: hasta 300 caracteres.
- **Se valida el estado final al editar.** Un documento viejo de un tipo que pasó a «lleva vigencia» no se bloquea, pero al editarlo, aunque sea solo la nota, hay que poner el «Hasta».
- El archivo se guarda **después** de validar todo: un error no deja archivos huérfanos.

**Un documento que respalda una exención de IVU tiene tres guardas:**
1. **No se puede quitar mientras la exención esté vigente** (409). Primero se da de baja la exención en Impuestos. Si la exención ya venció, sí se puede quitar.
2. **Cambiarle el número o las fechas exige además `tax.manage`** (403). Alargar el «Hasta» alarga la exención, y eso es dinero.
3. **No se le puede quitar el «Hasta»** (400).
Al cambiarle el número o las fechas, la exención toma los nuevos en el mismo guardado.

## 3. Tipos de documento y sus marcas

Los tipos son valores del catálogo **«Tipos de documento del cliente»** (Sistema → Catálogos de valores). Cada tipo tiene dos marcas:

| Marca | Qué hace |
|---|---|
| **Lleva vigencia** | Exige el «Hasta» al subir o editar el documento y lo incluye en los avisos de vencimiento y en «Documentos por vencer» (también cuando le falta el «Hasta»). |
| **Es de impuestos** | Deja que el documento respalde una exención de IVU. |

Las marcas se guardan en el valor del catálogo; la compañía las puede cambiar para ella sin afectar a las demás. Este lote siembra:

| Tipo (código) | Lleva vigencia | Es de impuestos |
|---|---|---|
| `MERCHANT_REGISTRATION` (registro de comerciante) | No | No |
| `RESELLER_CERTIFICATE` (certificado de revendedor) | **No** | **Sí** |
| `IVU_EXEMPTION_STATE` (certificado de exención de IVU estatal) | Sí | Sí |
| `IVU_EXEMPTION_CITY` (certificado de exención de IVU municipal) | Sí | Sí |
| `IVU_EXEMPTION_TOTAL` (certificado de exención de IVU total, estatal y municipal) | Sí | Sí |
| `OTHER` (otro) | No | No |

- **El certificado de revendedor no lleva vigencia a propósito:** la app de vendedores lo sube solo con el tipo, sin fechas. Si la compañía quiere que lo lleve, enciéndale «Lleva vigencia» (la app de vendedores dejaría de poder subirlo sin fechas).
- Apagar un tipo en Catálogos hace que subir con ese código dé 400 («El tipo de documento no existe o está apagado en Catálogos de valores.»).
- **El tipo no fija el alcance de la exención.** La pantalla propone «todo el impuesto» para el total y «solo los componentes marcados» para el estatal y el municipal, pero usted escoge. Pendiente de confirmar con el contador qué certificados eximen el IVU estatal.

## 4. Exención de IVU atada a su documento

**Qué hace.** La exención de un cliente (todo el impuesto, o solo algunos componentes, como el municipal) se respalda con un **documento del cliente de un tipo «de impuestos»**. Mientras la exención está vinculada, **su número y su vigencia son los del documento**: si el documento cambia su «Hasta», la exención cambia con él. Si el documento se quita, la exención **deja de aplicar**.

**Quién puede.** `tax.manage`. Módulo `TAX`.

**Cómo se usa (pantalla).**
- **Ficha del cliente → «Impuestos»**: las exenciones del cliente con el documento que las respalda. **«Nueva exención»** hace la **captura única**: sube el certificado y crea la exención en un solo paso, con el alcance que escoja (todo el impuesto, o los componentes marcados). **«Renovar»** hace lo mismo partiendo del alcance anterior.
- **Aplicar con un documento ya subido:** la pregunta **«¿Aplicar la exención de IVU?»** al subir un certificado, o **Contabilidad → Impuestos → Exenciones → «Nueva exención»** y escoger el documento.
- **Contabilidad → Impuestos → Exenciones:** columna «Documento», el chip **«Falta el documento»** y la acción **«Vincular documento»** en las exenciones de antes.
- **Baja y reactivación:** dar de baja siempre se puede. Reactivar exige que el documento siga activo, vigente y libre.

**Cómo se usa (API).**

| Qué | Ruta | Permiso |
|---|---|---|
| Las exenciones de un cliente | `GET /api/v1/clients/{clientPublicId}/tax-exemptions` | `tax.manage` |
| **Captura única** (archivo nuevo **o** documento ya subido) | `POST /api/v1/clients/{clientPublicId}/tax-exemptions` (multipart) | `tax.manage` |
| Aplicar con un documento ya subido | `POST /api/v1/tax/exemptions` | `tax.manage` |
| Vincular o renovar | `PATCH /api/v1/tax/exemptions/{id}` con `clientDocumentPublicId` | `tax.manage` |
| Dar de baja / reactivar | `POST /api/v1/tax/exemptions/{id}/deactivate` y `…/reactivate` | `tax.manage` |

Captura única con archivo nuevo (multipart/form-data, con la cabecera `Idempotency-Key`: repetir la misma clave devuelve la misma exención sin duplicar el documento):

```
POST /api/v1/clients/{clientPublicId}/tax-exemptions
Idempotency-Key: 3f1b6c7e-…
file=@certificado.pdf
documentType=IVU_EXEMPTION_CITY
documentNumber=MUN-2026-0042
validFrom=2026-11-01
validTo=2027-10-31
scope=COMPONENTS
componentCodes=CITY
notes=Renovación anual
```

Con un documento ya subido (sin archivo): `clientDocumentPublicId=<guid>`, más `scope` y `componentCodes`. **Archivo y documento a la vez: 400.** `exemptionType` vacío = la etiqueta del tipo de documento. La respuesta trae la exención, `documentPublicId` y `filePublicId`.

Aplicar con un documento ya subido (JSON). El número y las fechas del cuerpo **se ignoran**: son los del documento.

```json
{
  "clientId": 15,
  "clientDocumentPublicId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "scope": "COMPONENTS",
  "componentCodes": ["CITY"],
  "notes": "Certificado municipal"
}
```

La exención que responde la API trae `validFrom`, `validTo` y `certificateNumber` **efectivos** (los del documento), `documentPublicId`, `documentType`, `documentTypeLabel`, `filePublicId` y `missingDocument`. Su `status` es `VALID`, `SCHEDULED`, `EXPIRED`, `INACTIVE` o **Documento retirado** (`DOCUMENT_REMOVED`: el documento se quitó: ya no exime nada).

**Reglas del documento que respalda.** Debe ser **del mismo cliente** (otro cliente u otra compañía: 404), estar **activo**, ser de un tipo **«de impuestos»**, tener **número** y tener un **«Hasta» de hoy en adelante** (el último día todavía sirve). Un documento respalda a lo más **una** exención activa (409 si otra la usa).

**La captura única** valida **todo antes de guardar el archivo** y guarda el archivo, el documento y la exención en una sola transacción: si algo falla, no queda ni documento ni exención.

**Exenciones anteriores al lote («Falta el documento»).**
- **Siguen valiendo exactamente igual**, aun sin fecha «Hasta»: ningún monto cambió con este lote.
- Salen en «Documentos por vencer» (solo para quien tiene `tax.manage`) para que se les vincule el certificado.
- **Editarlas o reactivarlas exige vincular un documento**; darlas de baja siempre se puede.
- Para vincular: **«Vincular documento»**, o `PATCH` con `clientDocumentPublicId`. Desde ese momento, el número y la vigencia son los del documento.

**Vigencia efectiva.** Una sola regla la decide para el cálculo, la lista y el aviso al vender:
- Exención sin documento: sus propias fechas y su certificado, como siempre.
- Exención con documento activo: «desde», «hasta» y número del documento.
- Exención con documento quitado: no aplica.

**Impuesto.** El cálculo no cambió, salvo que lee la vigencia efectiva. Ejemplo con IVU estatal 10.5 % y municipal 1 %, línea de 100.00: sin exención, 11.50; con la exención municipal (componentes), 10.50; con la exención total, 0.00 (motivo `EXEMPT_CLIENT`). Con la exención vencida se cobra completo y el cálculo avisa «El cliente tiene una exención vencida (certificado …, venció el …): se cobró el impuesto.».

## 5. Exención de producto por temporada

**Qué hace.** Un decreto que exime del IVU a los productos, entre dos fechas (los dos días cuentan). Mientras rige, las **líneas de producto** cubiertas salen **sin impuesto**, con el motivo **`EXEMPT_PRODUCT_PERIOD`** y el nombre del período (`exemptionPeriod` en la respuesta del cálculo). La exención del cliente no se les suma.

**Quién puede.** `tax.manage`. Módulo `TAX`.

**Cómo se usa (pantalla).** **Contabilidad → Impuestos → «Exención por temporada»**: la lista de períodos (con «Ver los dados de baja»), **«Nuevo período»**, editar, dar de baja y reactivar.

**Cómo se usa (API).**

| Qué | Ruta |
|---|---|
| Listar (`includeInactive=true` agrega los dados de baja) | `GET /api/v1/tax/exemption-periods` |
| Crear | `POST /api/v1/tax/exemption-periods` |
| Ver uno | `GET /api/v1/tax/exemption-periods/{publicId}` |
| Cambiar | `PATCH /api/v1/tax/exemption-periods/{publicId}` |
| Dar de baja / reactivar | `POST …/{publicId}/deactivate` y `…/reactivate` |

No hay `DELETE`: un período se da de baja.

```json
{
  "name": "Útiles escolares agosto 2026",
  "reason": "Orden administrativa 2026-12 del Departamento de Hacienda",
  "validFrom": "2026-08-01",
  "validTo": "2026-08-31",
  "scope": "SELECTED",
  "categoryIds": [14],
  "productPublicIds": ["0f8fad5b-d9cb-469f-a165-70867728950e"]
}
```

**Campos.**
- `name` (obligatorio, hasta 100) y `reason` (obligatorio, hasta 300): por ejemplo, la orden administrativa que lo decreta.
- `validFrom`, `validTo`: obligatorios; «hasta» no antes de «desde» ni antes de hoy.
- `scope`: `ALL` (todos los productos; la selección se ignora) o `SELECTED` (categorías y productos escogidos: al menos uno; hasta 2000 productos y 200 categorías).
- `categoryIds` son las **categorías de producto** del catálogo de productos (no la categoría de impuesto) y **incluyen sus subcategorías**, no las hermanas.
- `rowVersion` (en el `PATCH`): el de la lista. Si cambió, 409.

**Qué exime y qué no.**
- Exime las líneas de **producto** gravables o de tasa cero. Un producto de categoría «exento» conserva su motivo `EXEMPT_PRODUCT`.
- **No exime los cargos** (envío, flete).
- La exención parcial del cliente (por ejemplo, la municipal) no cambia en esa línea: gana el período. Las demás líneas siguen pagando.
- Si dos períodos se traslapan, se unen y se muestra el nombre del que empieza primero.
- Un destino dado de baja (producto o categoría) no exime.
- El período se evalúa con la fecha del cálculo. Una nota de crédito **manual** recalcula el IVU con la fecha de **hoy** (límite conocido, no se tocó).

## 6. Aviso de exención al vender

**Qué hace.** Al escoger el cliente en el pedido guiado, en su resumen y en la ventana de la orden, el programa avisa si la exención de IVU del cliente **vence pronto** o **ya venció**. **Solo avisa: no bloquea nada.**
- **«…vence el {fecha} (en N días): después se cobrará el IVU.»** cuando la cobertura vigente vence dentro del primer aviso de la compañía (el mayor umbral positivo; 30 días por omisión). Una cobertura sin «Hasta» no avisa.
- **«…venció el {fecha}: se cobrará el IVU.»** cuando una exención con documento venció y nada vigente cubre lo que ella cubría.
- Las exenciones viejas sin documento cuentan como cobertura, pero **nunca** son el motivo del aviso.
- Con el módulo `TAX` apagado, no hay aviso. El texto sale en el idioma de la interfaz y con la fecha de la compañía.

**Quién puede.** `sales.create` o `sales.view`. Módulo `SALES_ORDERS`.

**Cómo se usa (API).** `GET /api/v1/sales-orders/tax-notice?client={clientPublicId}`. Responde `level` (`NONE`, `EXPIRING` o `EXPIRED`), `message` (en español, solo de respaldo), `validTo`, `days` (los que faltan; negativo si ya venció), `certificateNumber` y `documentTypeLabel`.

## 7. Avisos de vencimiento

### Qué avisa y cuándo

Un trabajo diario, **«Avisos de documentos por vencer»** (`DOCUMENT_EXPIRY_SCAN`, 06:00 hora de la compañía; se cambia o se corre a mano en Sistema → Trabajos programados), revisa:
- Los **documentos del cliente** que tienen «Hasta» (de cualquier tipo), de clientes activos y que **no estén renovados**.
- Los **precios especiales** (Lote 247) publicados y vigentes, solo donde está encendido `SALES_ORDERS`. Estos avisan **antes de vencer o el día que vence**, nunca después y nunca al cliente.

Publica un aviso por documento y umbral:

| Evento | Cuándo |
|---|---|
| **Documento del cliente por vencer** (`SALES_CLIENT_DOCUMENT_EXPIRING`) | Faltan los días de un umbral, o el día que vence |
| **Documento del cliente vencido** (`SALES_CLIENT_DOCUMENT_EXPIRED`) | Ya pasó el «Hasta» (según los umbrales negativos o el aviso diario) |
| **Precio especial por vencer** (`SALES_SPECIAL_PRICE_EXPIRING`) | Faltan los días de un umbral, o el día que vence |

Los eventos son **opcionales** y salen en el grupo «Ventas» de Reglas de aviso. **No se siembra ninguna regla.** Para que alguien reciba el aviso, el administrador crea la regla en Sistema → Reglas de aviso.

### Los ajustes de la compañía

Ajustes → Operación → **«Avisos de vencimiento»** (`admin.tenant`):

| Ajuste | Qué hace | Por omisión |
|---|---|---|
| **Cuándo avisar** (umbrales, en días) | Positivo = antes de vencer, 0 = el día, negativo = después. Hasta 10, de -365 a 365, sin repetir | 30, 15 y 5 |
| **Avisar a diario después de vencido** | Mientras el documento siga vencido y sin renovar, avisa cada día | Apagado |
| **Avisar al cliente** | El aviso de documentos por vencer también llega al cliente (correo principal, contactos marcados «Recibe avisos por correo» y correos extra) | Apagado |

Por API: `PUT /api/v1/tenant/settings` con `docExpiryThresholdDays`, `docExpiryDailyAfterExpiry` y `docExpiryNotifyClient`. Lo que no se manda no cambia; `[]` = sin umbrales (no se avisa). Se guardan de mayor a menor.

```json
{ "docExpiryThresholdDays": [30, 15, 5, 0, -7], "docExpiryDailyAfterExpiry": false, "docExpiryNotifyClient": true }
```

### Cómo decide el trabajo

- Con la fecha «Hasta» a **d** días de hoy: si d ≥ 0, avisa **solo el umbral cruzado más cercano** (con 30, 15 y 5 y d = 12 avisa el de 15; un documento subido tarde no recibe el de 30 y el de 15 de golpe).
- Si ya venció (d < 0): con el **aviso diario** encendido, un aviso cada día; sin él, el umbral negativo más cercano. Si el trabajo no corrió el día del umbral, el aviso se **recupera hasta 7 días después**; un documento vencido hace meses no avisa (sin el diario).
- **No repite:** cada aviso lleva una clave (documento + fecha «Hasta» + umbral). Corregir la fecha «Hasta» vuelve a avisar. El umbral 0 publicado como «por vencer» no se repite al día siguiente como «vencido».
- **Tope de 500 avisos nuevos por corrida y por compañía.** Lo demás sale en la corrida siguiente; el resultado del trabajo lo dice («N quedaron para la siguiente corrida»).
- **Se anota aunque no haya regla.** Crear la regla después no dispara de golpe los umbrales que ya pasaron; solo avisa lo que se cruce de ahí en adelante.
- El resultado del trabajo dice, por ejemplo: «N aviso(s) nuevo(s): N por vencer y N vencido(s); N ya se habían avisado.» o «Nada que avisar.».

### A quién llega

Cada regla del evento escoge destinatarios; los dos del evento de documentos son:
- **«Vendedores del cliente (ruta y preferido)»** (`CLIENT_SELLERS`): los usuarios de la ruta de venta del cliente y su vendedor preferido (Lote 244). Si el cliente no tiene ni ruta ni vendedor preferido con usuario activo, **no le llega a nadie** y el Registro de avisos lo anota «Omitido» con el motivo «El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.». Ponga también un rol fijo en la regla (por ejemplo el supervisor de ventas).
- **«Cliente del documento»** (`CLIENT`): solo si la compañía enciende **«Avisar al cliente»**. Va al correo principal, a los contactos marcados «Recibe avisos por correo» y a los correos extra de su aviso; en el idioma del cliente. Cada cliente puede apagarlo en su ficha → **«Avisos al cliente» → «Documentos por vencer»**: **solo sale por correo** (la bandeja del portal y el SMS están apagados y no se pueden encender). Sin fila guardada, el cliente sigue a la compañía.
- Un precio especial de una **cadena** avisa a los vendedores de sus clientes activos (hasta 50 personas).

**Al enviar, se revisa el documento.** Si ya se quitó, se renovó, cambió su fecha «Hasta» o el cliente se dio de baja, el aviso **no sale** y queda «Omitido» en el Registro con su motivo (por ejemplo «El documento ya se renovó: no se avisa.»).

> **Cambio que afecta a todos los avisos al cliente.** La marca **«Recibe avisos por correo»** de un contacto vale ahora para **todos** los avisos al cliente. Los resúmenes de siempre (recogido, en ruta, entregado, almacén, remesa COD y factura) llegan también al **correo principal del cliente y a sus contactos marcados**, además de a los usuarios del portal y a los correos extra. Antes no llegaban ahí. Quien no quiera que un contacto reciba avisos, le quita la marca en su ficha.

### «Renovado»

Un documento **renovado deja de avisar** y sale de «Documentos por vencer». Está renovado si hay **otro documento activo del mismo cliente y del mismo tipo** con un «Hasta» posterior (con el mismo «Hasta», el subido después; o uno subido después sin «Hasta»). Un documento viejo **sin** «Hasta» nunca reemplaza a uno con fecha. Si se renueva con un tipo distinto, el viejo sigue avisando hasta que se quite.

## 8. Lista «Documentos por vencer» y fuente de datos

**Listas → Documentos por vencer** (`clients.read`, módulo `CATALOG`). Muestra, por «Hasta» ascendente, los documentos de clientes activos que vencen dentro de la ventana, los ya vencidos y los de un tipo con vigencia que **no tienen «Hasta»**. No salen los renovados. Tiene el buscador libre (cliente, tipo o número), el filtro **«Vence dentro de»** (por omisión, el primer aviso de la compañía), y los interruptores **«Incluir vencidos»** e **«Incluir los que faltan el «hasta»»**. El nombre del cliente lleva a su ficha. Con `tax.manage` y `TAX`, salen también las **exenciones sin documento** («Exención de IVU sin documento»). Hay un tope de 2000 filas: si se pasa, avisa y muestra las 2000 más cercanas a vencer.

API: `GET /api/v1/client-documents/expiring?withinDays=&includeExpired=&includeMissing=&client=&q=`. Responde `warnDays`, `withinDays`, `truncated` e `items[]` con `rowKind` (`DOCUMENT` o `EXEMPTION_WITHOUT_DOCUMENT`), cliente, tipo, número, fechas, `daysToExpiry`, `expiryState`, `isTax`, `backsExemption`.

**Fuente de datos «Documentos del cliente» (`CLIENT_DOCUMENT`).** Para Vistas, Indicadores y Gráficos: una fila por documento activo con `ClientName`, `ClientCode`, `DocumentType`, `DocumentTypeLabel`, `DocumentNumber`, `ValidFrom`, `ValidTo`, `DaysToExpiry`, `ExpiryState`, `IsTax`, `BacksExemption` y `CreatedAtUtc`. Es de **estado actual** (el de hoy), sin campo de fecha de período, como la de documentos de flota. Se lee con `clients.read`.

## 9. Estatus y transiciones

**No hay estatus de catálogo nuevos.** Ninguna acción de este capítulo usa el servicio de estatus ni escribe historial de estatus. Los estados se **calculan de la fecha**; cada cambio queda en la bitácora de auditoría.

**Estado de un documento** (`expiryState`):

| Estado | Cuándo | Sale en «Documentos por vencer» |
|---|---|---|
| **Renovado** (`RENEWED`) | Hay otro documento activo del mismo cliente y tipo que lo reemplaza | No |
| **Sin vigencia** (`NO_EXPIRY`) | No tiene «Hasta» y el tipo no lleva vigencia | No |
| **Falta «hasta»** (`MISSING_VALID_TO`) | No tiene «Hasta» y el tipo lleva vigencia (un documento viejo de un tipo que pasó a «lleva vigencia») | Sí |
| **Vencido** (`EXPIRED`) | El «Hasta» ya pasó | Sí |
| **Por vencer** (`EXPIRING`) | Faltan hoy o menos que el primer aviso de la compañía (el mayor umbral positivo; el día que vence cuenta) | Sí |
| **Vigente** (`VALID`) | Faltan más días que el primer aviso | Solo dentro de la ventana de «Vence dentro de» |

**Estado de una exención** (`status`): **Vigente** (`VALID`), **Programada** (`SCHEDULED`: todavía no empieza), **Vencida** (`EXPIRED`), **De baja** (`INACTIVE`) o **Documento retirado** (`DOCUMENT_REMOVED`: el documento se quitó y la exención ya no exime nada).

**Estado de un período por temporada** (`state`):

| Estado | Cuándo | Qué se puede hacer |
|---|---|---|
| **Programado** (`SCHEDULED`) | Empieza después de hoy | Se edita todo: nombre, motivo, fechas, alcance y selección; darlo de baja |
| **Vigente** (`ACTIVE`) | Hoy está entre «desde» y «hasta» | Solo mover la **fecha final** (de hoy en adelante) o darlo de baja; cualquier otro cambio da 409 |
| **Terminado** (`ENDED`) | Ya pasó su «hasta» | Solo consultarlo (y darlo de baja) |
| **De baja** (`INACTIVE`) | Se dio de baja | Reactivarlo (vuelve al estado que le toque por sus fechas) |

**Acciones y qué dispara cada una:**

| Acción | Quién puede | Qué valida | Efecto |
|---|---|---|---|
| Subir un documento | `clients.update` (o `clients.create_basic` si es prospecto) | Tipo activo, número ≤ 60, «Hasta» si el tipo lleva vigencia, fechas en orden, nota ≤ 300, archivo válido | Crea el documento; con un tipo de impuestos y `tax.manage`, la respuesta sugiere aplicar la exención |
| Editar un documento | igual; además `tax.manage` si respalda una exención y cambia número o fechas | Lo mismo sobre el estado final; `rowVersion` | Guarda; sincroniza número y fechas de la exención que respalda |
| Quitar un documento | igual | No respalda una exención **vigente** | Desactiva el documento y su archivo (el contenido no se borra) |
| Aplicar la exención (captura única o con un documento) | `tax.manage` | Documento del cliente, activo, de impuestos, con número y «Hasta» de hoy en adelante, libre; alcance válido | Crea la exención vinculada (y, con archivo nuevo, el documento) |
| Vincular o renovar una exención | `tax.manage` | Las mismas del documento; libre | La exención pasa a las fechas y al número del documento nuevo |
| Cambiar fechas o número de una exención vinculada | — | No se puede: son del documento | 400; se corrigen en Documentos del cliente |
| Dar de baja una exención | `tax.manage` | Ninguna | La exención deja de aplicar |
| Reactivar una exención | `tax.manage` | Documento vinculado, activo, con «Hasta» vigente y libre | La exención vuelve a aplicar |
| Crear, editar, dar de baja o reactivar un período | `tax.manage` | Ver la sección 5 | Afecta el cálculo de las líneas en sus fechas |
| Cambiar umbrales o interruptores | `admin.tenant` | Hasta 10 umbrales de -365 a 365, sin repetir | Cambia lo que avisa el trabajo y los días del «primer aviso» |
| Correr el trabajo | `jobs.manage` (o el horario) | Módulo `CATALOG` encendido | Publica los avisos nuevos; no repite |

**Qué queda bloqueado:**
- **Documento que respalda una exención vigente:** no se quita (409); cambiar su número o fechas sin `tax.manage`, 403; quitarle el «Hasta», 400.
- **Exención vinculada:** no se le cambian fechas ni número directamente (400).
- **Exención sin documento (de antes):** no se edita ni se reactiva sin vincular un documento (400).
- **Documento que ya respalda otra exención activa:** no respalda una segunda (409).
- **Período vigente:** solo cambia la fecha final; terminado o de baja, solo se consulta.
- **Documento quitado:** su exención deja de aplicar (estado `DOCUMENT_REMOVED`).

## 10. Validaciones y mensajes

El mensaje sale tal cual en el campo `title` del error (y en rojo en la pantalla, salvo los que la pantalla avisa antes de enviar). Los textos del servidor van en español.

### Documentos del cliente (`/api/v1/clients/{id}/documents`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `documentType` | Código activo y encendido en Catálogos | `El tipo de documento no existe o está apagado en Catálogos de valores.` | 400 |
| `documentNumber` | Hasta 60 caracteres | `El número del documento no puede pasar de 60 caracteres.` | 400 |
| `validTo` | Obligatorio si el tipo lleva vigencia | `Este tipo de documento lleva vigencia: escriba la fecha «hasta».` | 400 |
| `validTo` | No anterior a «desde» | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 |
| `validFrom`, `validTo` | Formato `AAAA-MM-DD` (al subir) | `La fecha debe tener el formato AAAA-MM-DD.` | 400 |
| `notes` | Hasta 300 caracteres | `La nota del documento no puede pasar de 300 caracteres.` | 400 |
| `rowVersion` | Valor base64 de la lista | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| `rowVersion` | El documento no cambió desde que se leyó | `El documento cambió mientras lo editaba. Vuelva a abrirlo.` | 409 |
| `validTo` (documento que respalda una exención) | No se puede quitar | `Este documento respalda una exención de IVU: no se le puede quitar la fecha «hasta».` | 400 |
| Número o fechas (documento que respalda una exención) | Requiere `tax.manage` | `Este documento respalda una exención de IVU: cambiar su número o su vigencia exige el permiso de impuestos (tax.manage).` | 403 |
| Quitar (documento que respalda una exención vigente) | Primero se da de baja la exención | `Este documento respalda una exención de IVU vigente: dé de baja la exención en Impuestos antes de quitarlo.` | 409 |
| Permiso | `clients.update` (o `clients.create_basic` si es prospecto) | `Subir documentos de este cliente exige el permiso de editar clientes (clients.update).` | 403 |
| Cliente | Existe en la compañía | `Cliente no encontrado.` | 404 |
| Documento | Existe, es del cliente y está activo | `Documento '{id}' no encontrado.` | 404 |

El archivo (tamaño, tipo JPG, PNG, WEBP, GIF o PDF) lo valida el almacén de archivos con los mensajes del Lote 222; ver la [FAQ](faq.md).

### Exenciones de IVU (`/api/v1/tax/exemptions` y `/api/v1/clients/{id}/tax-exemptions`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `clientDocumentPublicId` | Obligatorio al aplicar, o al editar o reactivar una exención de antes | `La exención necesita el documento que la respalda.` | 400 |
| `file` y `clientDocumentPublicId` | Uno u otro, no los dos | `Mande el archivo del certificado o escoja un documento ya subido, no las dos cosas.` | 400 |
| `file` | No puede venir vacío | `Adjunte el archivo del certificado.` | 400 |
| `documentType` | Código activo y encendido | `El tipo de documento no existe o está apagado en Catálogos de valores.` | 400 |
| `documentType` / documento | El tipo lleva la marca «es de impuestos» | `Ese tipo de documento no es de impuestos: no puede respaldar una exención.` | 400 |
| `documentNumber` | Obligatorio (captura con archivo) | `Escriba el número del certificado.` | 400 |
| `documentNumber` | Hasta 60 caracteres | `El número del documento no puede pasar de 60 caracteres.` | 400 |
| `validFrom` | Obligatorio (captura con archivo) | `Escriba desde cuándo vale el certificado.` | 400 |
| `validTo` | Obligatorio (captura con archivo) | `Escriba hasta cuándo vale el certificado.` | 400 |
| `validTo` | No anterior a «desde» (captura con archivo) | `La fecha «hasta» no puede ser anterior a la fecha «desde».` | 400 |
| `validTo` | De hoy en adelante (captura con archivo) | `La fecha «hasta» ya pasó: la exención nacería vencida.` | 400 |
| `exemptionType` | Hasta 60 caracteres | `El tipo de exención no puede pasar de 60 caracteres.` | 400 |
| `notes` | Hasta 300 caracteres | `Las notas no pueden pasar de 300 caracteres.` | 400 |
| `scope` | `ALL` o `COMPONENTS` | `El alcance de la exención debe ser ALL (todo el impuesto) o COMPONENTS (solo algunos componentes).` | 400 |
| `componentCodes` | Con `COMPONENTS`, al menos uno | `Marque al menos un componente del impuesto que la exención cubre.` | 400 |
| `componentCodes` | Es un componente de alguna tasa de la compañía | `Ninguna tasa de la compañía tiene el componente «{código}».` | 400 |
| Documento | Está activo | `El documento que respaldaba la exención se quitó: suba el certificado vigente.` | 400 |
| Documento | Tiene número | `El documento no tiene número: escríbalo en el documento antes de aplicar la exención.` | 400 |
| Documento | Tiene «Hasta» | `El documento no tiene fecha «hasta»: la exención necesita la vigencia del documento.` | 400 |
| Documento | No está vencido | `El documento ya venció: suba el certificado vigente.` | 400 |
| `validFrom`, `validTo`, `clearValidTo`, `certificateNumber` en el `PATCH` de una exención vinculada | Son del documento | `La vigencia y el número de la exención son los del documento: corríjalos en Documentos del cliente.` | 400 |
| Documento | No respalda ya otra exención activa | `Ese documento ya respalda otra exención activa.` | 409 |
| Documento | Es del mismo cliente | `Documento no encontrado.` | 404 |
| Exención | Existe | `Exención no encontrada.` | 404 |
| Cliente | Existe en la compañía | `Cliente no encontrado.` | 404 |
| (cuerpo) | Debe venir | `El cuerpo de la solicitud es obligatorio.` | 400 |

### Períodos de exención por temporada (`/api/v1/tax/exemption-periods`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| (cuerpo) | Debe venir | `El cuerpo de la solicitud es obligatorio.` | 400 |
| `name` | Obligatorio | `Escriba el nombre del período de exención.` | 400 |
| `name` | Hasta 100 caracteres | `El nombre del período no puede pasar de 100 caracteres.` | 400 |
| `reason` | Obligatorio | `Escriba el motivo de la exención (por ejemplo, la orden administrativa que la decreta).` | 400 |
| `reason` | Hasta 300 caracteres | `El motivo no puede pasar de 300 caracteres.` | 400 |
| `validFrom` | Obligatorio | `Escriba desde qué fecha rige la exención.` | 400 |
| `validTo` | Obligatorio | `Escriba hasta qué fecha rige la exención.` | 400 |
| `validTo` | No anterior a «desde» | `La fecha final no puede ser anterior a la inicial.` | 400 |
| `validTo` | No anterior a hoy | `La fecha final no puede quedar antes de hoy.` | 400 |
| `scope` | `ALL` o `SELECTED` | `El alcance debe ser ALL (todos los productos) o SELECTED (categorías o productos escogidos).` | 400 |
| `productPublicIds`, `categoryIds` | Con `SELECTED`, al menos uno | `Escoja al menos una categoría o un producto.` | 400 |
| `productPublicIds`, `categoryIds` | Hasta 2000 productos y 200 categorías | `Un período puede escoger hasta 2000 productos y 200 categorías.` | 400 |
| Período ya empezado | Solo se mueve la fecha final o se da de baja | `El período ya empezó: solo se le puede cambiar la fecha final o darlo de baja.` | 409 |
| `rowVersion` | El período no cambió desde que se leyó | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Período | Existe | `Período de exención no encontrado.` | 404 |
| Producto | Existe en la compañía | `Producto no encontrado.` | 404 |
| Categoría | Existe en la compañía | `Categoría no encontrada.` | 404 |

### Aviso al vender, lista y ajustes

| Dónde | Regla | Mensaje | HTTP |
|---|---|---|---|
| `GET /sales-orders/tax-notice` | Falta el cliente | `Indique el cliente.` | 400 |
| `GET /sales-orders/tax-notice` | El cliente existe en la compañía | `Cliente no encontrado.` | 404 |
| `GET /client-documents/expiring` | `withinDays` de 1 a 365 | `Los días deben ser un número entero entre 1 y 365.` | 400 |
| `GET /client-documents/expiring` | `client` existe | `Cliente no encontrado.` | 404 |
| `PUT /tenant/settings` | Cada umbral entero de -365 a 365 | `Cada umbral es un número entero de días entre -365 y 365 (positivo = antes de vencer, 0 = el día, negativo = después).` | 400 |
| `PUT /tenant/settings` | Sin repetir | `Hay un umbral repetido.` | 400 |
| `PUT /tenant/settings` | Hasta 10 | `Se pueden poner hasta 10 umbrales.` | 400 |
| `PUT /clients/{id}/notifications/settings` | `DOCUMENT_EXPIRING` solo por correo | `El aviso de documentos por vencer solo sale por correo.` | 400 |
| Permiso | Falta el permiso | `Falta el permiso '…'.` | 403 |
| Módulo | Apagado | `El módulo '…' no está habilitado para esta compañía.` | 403 |

### Avisos que no son errores

El **aviso de exención al vender** y los **avisos de vencimiento** nunca bloquean nada. Los motivos por los que un aviso queda «Omitido» en el Registro de avisos no son errores: «El documento ya se quitó: no se avisa.», «El documento ya se renovó: no se avisa.», «La fecha «hasta» del documento cambió: este aviso ya no aplica.», «El cliente está dado de baja: no se avisa.», «La compañía no tiene encendido «Avisar al cliente» (Ajustes → Avisos de vencimiento).», «El cliente tiene apagado el aviso de documentos por vencer.», «El cliente no tiene correo principal ni contactos marcados «Recibe avisos por correo».» y «El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.». Los del precio especial: «El precio especial se desactivó: no se avisa.», «El precio especial volvió a borrador: no se avisa.», «El precio especial ya se renovó: no se avisa.», «La fecha «hasta» del precio especial cambió: este aviso ya no aplica.», «El cliente o la cadena del precio especial está dado de baja: no se avisa.» y «Ningún cliente activo de la cadena tiene ruta de venta ni vendedor preferido con usuario activo.».

## 11. Preguntas frecuentes del módulo

**Subí el certificado, pero la exención no se aplicó. ¿Por qué?**
Subir el documento **no** crea la exención. Responda «Sí, aplicar» a **«¿Aplicar la exención de IVU?»**, o use «Nueva exención» (ficha del cliente → Impuestos, o Contabilidad → Impuestos). La pregunta solo sale si el tipo es «de impuestos» y usted tiene `tax.manage` con el módulo `TAX` encendido.

**¿Por qué no puedo quitar el documento?**
Respalda una exención de IVU vigente. Dé de baja la exención en Contabilidad → Impuestos → Exenciones y quite el documento después. Si lo que necesita es el certificado nuevo, use **«Renovar»**: sube el nuevo y la exención pasa a él.

**No me deja cambiar el «Hasta» de un documento.**
Si el documento respalda una exención, hace falta además el permiso `tax.manage` (403), y no se le puede quitar el «Hasta» (400). Si no respalda ninguna exención, hace falta `clients.update`.

**Una exención dice «Falta el documento». ¿Dejó de valer?**
No. Las exenciones de antes del lote siguen valiendo igual. La marca solo avisa que no tienen certificado en el sistema: no se les puede dar seguimiento al vencimiento. Use «Vincular documento».

**La exención se cobró vencida en una orden. ¿Por qué?**
El cálculo toma la vigencia del documento. Si el «Hasta» del documento ya pasó, el IVU se cobra y el cálculo avisa «El cliente tiene una exención vencida (certificado …, venció el …): se cobró el impuesto.». Suba el certificado vigente con «Renovar».

**El documento tiene «Falta «hasta»». ¿Qué hago?**
Es un documento de un tipo que pasó a «lleva vigencia» después de subirse. Edítelo y escriba el «Hasta». Mientras tanto sale en «Documentos por vencer» con ese estado.

**¿Por qué un documento deja de salir en «Documentos por vencer»?**
Está **renovado**: hay otro documento activo del mismo cliente y del mismo tipo con un «Hasta» posterior. O el documento se quitó, o el cliente está dado de baja.

**No recibí el aviso de que un documento vence.**
Revise, en orden: (1) que exista una **regla** para «Documento del cliente por vencer» en Sistema → Reglas de aviso; (2) que el cliente tenga **ruta de venta o vendedor preferido** con usuario activo, si la regla usa «Vendedores del cliente»; (3) que el trabajo «Avisos de documentos por vencer» esté encendido; (4) que el documento tenga «Hasta» y no esté renovado; (5) el **Registro de avisos**: ahí queda «Omitido» con el motivo. Si el aviso se anotó antes de que existiera la regla, no se repite.

**El cliente no recibe el aviso.**
Tiene que estar encendido **«Avisar al cliente»** en Ajustes → Operación → «Avisos de vencimiento», el cliente no debe haberlo apagado en su ficha («Avisos al cliente» → «Documentos por vencer»), y debe tener correo principal, un contacto marcado «Recibe avisos por correo» o un correo extra.

**Me llegan avisos del mismo documento cada día.**
Está encendido **«Avisar a diario después de vencido»** y el documento sigue vencido y sin renovar. Suba el documento renovado, quítelo o apague el interruptor.

**Cambié los umbrales y no pasó nada.**
Los umbrales solo se usan cuando corre el trabajo (06:00 por omisión) o cuando alguien lo corre a mano. Un umbral nuevo avisa en la corrida siguiente solo si es el más cercano que el documento ya cruzó; si uno más cercano ya se avisó, no se repite. El «primer aviso» (el mayor umbral positivo) también define cuándo un documento sale «Por vencer».

**Los resúmenes del cliente empezaron a llegar a más personas.**
Es el cambio de la marca «Recibe avisos por correo» (sección 7): ahora vale para todos los avisos al cliente y los resúmenes también llegan al correo principal y a los contactos marcados. Quite la marca al contacto que no deba recibirlos.

**¿Por qué mi línea no tiene IVU si el cliente no tiene exención?**
Puede haber un **período de exención por temporada** vigente que cubra el producto (o su categoría). El motivo de la línea es `EXEMPT_PRODUCT_PERIOD` y trae el nombre del período. Revise Contabilidad → Impuestos → «Exención por temporada».

**No me deja cambiar un período.**
Si ya empezó, solo se puede mover la fecha final o darlo de baja (409). Para cambiar productos o categorías de un período vigente, acorte su fecha final y cree otro período con la selección nueva.

**¿Qué pasa con una nota de crédito de un producto vendido en temporada?**
Una nota de crédito **manual** recalcula el IVU con la fecha de **hoy**. Si el período ya terminó, la nota calcula IVU. Es un límite conocido; hoy se corrige a mano.

**¿Qué certificado exime el IVU estatal?**
Pendiente de confirmar con el contador. Mientras tanto, el tipo de documento no obliga el alcance: usted escoge si la exención cubre todo el impuesto o solo algunos componentes.

## 12. Para soporte

- **Tablas:** `ClientDocument` (`DocumentNumber`, `ValidFrom`, `ValidTo`, `RowVersion`; quitar = `IsActive = 0`), `ClientTaxExemption.ClientDocumentId` (vacío = «falta el documento»), `TaxExemptionPeriod` con `TaxExemptionPeriodProduct` y `TaxExemptionPeriodCategory` (quitar un destino = `IsActive = 0`), `Tenant.DocExpiryThresholdDays`, `DocExpiryDailyAfterExpiry` y `DocExpiryNotifyClient`, y `NotificationOutbox` (la clave de no repetición).
- **Una exención por documento:** el índice `UX_ClientTaxExemption_Document` solo cuenta las exenciones activas. La llave compuesta `FK_ClientTaxExemption_Document` (documento, compañía y cliente) impide atar el certificado de otro cliente o de otra compañía (error 547 en la base).
- **«¿Por qué se cobró (o no) el IVU?»:** en el cálculo, el motivo de la línea (`EXEMPT_CLIENT`, `EXEMPT_CLIENT_PARTIAL`, `EXEMPT_PRODUCT`, `EXEMPT_PRODUCT_PERIOD`) y, para la temporada, `exemptionPeriod`. La línea de la orden guarda solo la copia de hoy (`IsTaxExempt`, `TaxRatePct`, `TaxAmount`): no dice qué exención la eximió. Eso llega en un lote posterior.
- **«¿Por qué no avisó?»:** busque en `NotificationOutbox` la clave `CD:{id del documento}:{AAAAMMDD del hasta}:T{umbral}` (o `D{AAAAMMDD}` con el aviso diario; `SP:…` para precios especiales). Si existe, ya se avisó. El Registro de avisos dice si se entregó o por qué se omitió.
- **Trabajo:** Sistema → Trabajos programados → «Avisos de documentos por vencer» (`DOCUMENT_EXPIRY_SCAN`): el último resultado dice cuántos avisos salieron y si quedaron para la siguiente corrida (tope de 500).
- **Auditoría:** los cambios de documentos quedan con el tipo `CLIENT_DOCUMENT`, los de exenciones con `TAX_EXEMPTION`, los de períodos con `TAX_EXEMPTION_PERIOD` y los de ajustes de la compañía con `TENANT`.
- **Dinero:** antes de poner esto en producción, revise cuántas exenciones activas quedan «sin documento» (la migración lo imprime: «Lote 248: N exenciones activas quedan marcadas «falta el documento»…»). Siguen valiendo igual; la lista «Documentos por vencer» las muestra a quien tiene `tax.manage`.
- Sin permiso, o con el módulo apagado, los 403 dejan evento de seguridad `PERMISSION_DENIED`.
