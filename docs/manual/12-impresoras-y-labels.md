# 12 — Impresoras y labels

Este capítulo cubre el **Lote 43 (F-G1, 2026-10-12)**: las **impresoras de la compañía**, las **plantillas de label** (la construcción
del label de envío), las **opciones de impresión de cada estación**, el ajuste de la compañía **«Imprimir labels directo a la
impresora»**, la bandera del cliente **«Imprimir la factura automáticamente»** y la **impresión directa con QZ Tray** (con la
sección de soporte para el certificado). La pantalla está en el capítulo [FG1 — Impresoras y labels](frontend/fg1-impresoras.md).
El **Lote 44 (F-G2)** agrega la sección 10: los labels que salen de las órdenes (uno por pieza), el waybill summary, la packing list,
el informe de faltantes y la búsqueda por número de paquete en el escaneo.

## 1. Cómo se imprime en Teikem

- **El servidor nunca se conecta a una impresora.** Imprime la computadora del usuario:
  - **Directo (QZ Tray)**: QZ Tray es un programa gratuito y de código abierto que se instala en cada computadora. La página le
    habla por un canal local (websocket a `localhost`) y QZ Tray manda el PDF a la impresora del sistema por su **nombre exacto**
    (el de Windows o macOS), **sin diálogo**.
  - **PDF del navegador (respaldo)**: si la impresora no es de QZ Tray, o QZ Tray no está instalado, no está abierto o falla, se
    abre el diálogo de impresión del navegador con el PDF (y en un teléfono o tableta se descarga el PDF). Nunca se rompe: la
    pantalla avisa por qué no salió directo.
- El **label** es un PDF vectorial en blanco y negro, **una página del tamaño exacto de la etiqueta** (4×6, 4×4 o 4×2 pulgadas) por
  **pieza**: la cantidad de cada línea de carga (de la unidad que sea) es el número de labels de la línea. El **código de barras es
  el número del paquete de la línea** (el mismo en sus piezas); debajo va el número legible y «Paquete i de N». Una línea sin número
  de paquete no lleva labels (se avisa).
- El **diseño** (qué se ve, dónde y de qué tamaño) sale de la **plantilla**; el **tamaño** sale de la **impresora**. Por eso una
  plantilla sirve para cualquier tamaño de etiqueta.
- Cada **estación** (este navegador en esta computadora) guarda su propia configuración: impresora de labels, plantilla, impresora de
  esta computadora (QZ Tray), «Imprimir al guardar», «Confirmar antes» e impresora de documentos. El servidor no la recibe.
- Orden en que se decide qué impresora y qué plantilla se usan: **la de la estación → la predeterminada de la compañía → la
  integrada** («PDF del navegador» 4×6 o Carta y el **label integrado**).

## 2. Quién puede y dónde

| Acción | Permiso | Endpoint |
|---|---|---|
| Ver, crear y editar impresoras; hacer predeterminada, desactivar y reactivar | `printers.manage` | `GET/POST /api/v1/printers`, `GET/PATCH /api/v1/printers/{publicId}`, `POST …/make-default`, `…/deactivate`, `…/reactivate` |
| Ver, crear y editar plantillas de label; hacer predeterminada, desactivar y reactivar | `printers.manage` | `GET/POST /api/v1/label-templates`, `GET/PATCH /api/v1/label-templates/{publicId}`, `POST …/make-default`, `…/deactivate`, `…/reactivate` |
| Leer y cambiar «Imprimir labels directo a la impresora» | `printers.manage` | `GET/PUT /api/v1/printing/settings` |
| Leer las opciones de impresión de la estación (impresoras y plantillas activas, label integrado, ajuste directo, si hay firma de QZ Tray) | cualquiera de `orders.view`, `trips.scan`, `trips.view`, `inventory.view`, `printers.manage` | `GET /api/v1/printing/station-options` |
| Certificado público y firma para QZ Tray | los mismos de las opciones de la estación | `GET /api/v1/printing/qz/certificate`, `POST /api/v1/printing/qz/sign` |
| «Imprimir la factura automáticamente» del cliente | `clients.update` | `PATCH /api/v1/clients/{publicId}/profile` (`autoPrintInvoice`) |

- **Módulo**: SYSTEM (núcleo, siempre encendido).
- **`printers.manage`** (categoría Administración, «Gestionar impresoras y plantillas de label») lo trae solo el rol **Admin de compañía**
  (también los roles ya creados con ese nombre en cada compañía). Para darlo a un supervisor de mostrador, agréguelo a su rol en
  Sistema → Roles y usuarios.
- Las rutas usan el `PublicId`; una impresora o plantilla de otra compañía responde 404. Nunca se borran: se desactivan.
- Todo cambio queda en la bitácora con los tipos **Impresora** (`PRINTER`) y **Plantilla de label** (`LABEL_TEMPLATE`).

## 3. Impresoras

Una impresora es un **perfil de impresión** de la compañía:

| Campo | Reglas |
|---|---|
| Nombre | Obligatorio, hasta 80 caracteres, **único entre las activas** (sin distinguir mayúsculas). Conviene que sea igual al de Windows. El nombre de una inactiva se puede reusar. |
| Nombre en el sistema | Opcional, hasta 200. El nombre **exacto** de la impresora en la computadora; QZ Tray imprime por él. Vacío = el Nombre. Cada estación puede elegir otro (el de su computadora). |
| Modelo | Opcional, hasta 80. |
| Uso | `LABEL` (labels) o `DOCUMENT` (documentos). Obligatorio al crearla y **no se cambia después**. |
| Conexión | `QZ_TRAY` (directo con QZ Tray) o `BROWSER` (PDF del navegador). Vacía = `BROWSER`. |
| Tamaño | Labels: `4X6`, `4X4` o `4X2` (vacío = `4X6`; también se acepta «4x6» o «4×6»). Documentos: `LETTER` (carta; vacío = `LETTER`). |
| Girar 90° | Para impresoras que alimentan la etiqueta de lado: el PDF sale con la página girada (con QZ Tray, la gira QZ Tray). |
| Nota | Opcional, hasta 300. No se muestra a las estaciones. |
| Predeterminada | Una por uso entre las activas. La **primera** impresora activa de un uso nace predeterminada. |

- **Hacer predeterminada** quita la anterior del mismo uso (la otra queda sin marcar) y pone esta.
- **Desactivar la predeterminada** se permite: el uso queda **sin predeterminada** y las estaciones que no eligieron otra imprimen
  con el PDF del navegador hasta que se elija otra. **Reactivar** no la vuelve predeterminada.
- En una edición (PATCH) lo que no se manda no cambia; `""` en Modelo, Nota o Nombre en el sistema los quita. Con `rowVersion`, si
  otro usuario guardó antes responde 409.
- La creación responde **200** con la impresora (convención del API de Teikem).

## 4. Plantillas de label (construcción del label)

La construcción tiene los **9 elementos** del constructor: Logo, Nivel de servicio, Información del sender, Consignatario, Dirección,
Código de barras, Número de waybill, Invoice y Monto COD. Cada uno tiene:
- **Mostrar** (sí/no);
- **Posición** en una cuadrícula de 9 zonas: `tl` (arriba izq.), `tc` (arriba centro), `tr` (arriba der.), `ml`, `mc`, `mr` (medio),
  `bl`, `bc`, `br` (abajo);
- **Tamaño**: `sm` (pequeño), `md` (mediano), `lg` (grande);
- **Como código de barras**: solo **Número de waybill** e **Invoice**. El **Código de barras** siempre lo es (el número del paquete).

| Elemento | Qué imprime |
|---|---|
| Logo | El nombre de la compañía en negritas (el logo gráfico no se imprime en P1). |
| Nivel de servicio | El servicio de la orden en mayúsculas (p. ej. NEXT DAY). |
| Información del sender | «De: cliente que manda». |
| Consignatario | «A: consignatario», en negritas. |
| Dirección | Las líneas de la dirección de entrega. |
| Código de barras | Code 128 del **número del paquete**, a todo el ancho, con el número legible y «Paquete i de N» debajo. |
| Número de waybill | «Waybill …» (o como código de barras). |
| Invoice | «Factura …» (o como código de barras). |
| Monto COD | «COD $…» en **caja negra con letra blanca**, solo si el COD es mayor que 0. Nunca se imprime el precio. |

Reglas de dibujo: los códigos de barras ocupan **todo el ancho de su renglón** (en una columna de un tercio no caben legibles); el
texto va en negro puro y nunca menor de 6 pt; si el contenido no cabe en la etiqueta se achica y, si ni así cabe, se avisa «El label
no cabe en 4×2: quite elementos o use una etiqueta más grande.».

Plantillas:
- Nombre obligatorio, hasta 80, **único entre las activas**. La **primera** plantilla activa nace predeterminada; «Hacer
  predeterminada», desactivar y reactivar funcionan igual que en las impresoras.
- En el alta la construcción es obligatoria; un elemento que no se manda toma el del **label integrado**; posición y tamaño se
  aceptan en mayúsculas. En el PATCH lo que no se manda no cambia.
- El label necesita **al menos un código de barras visible** (el del paquete, o waybill/factura como código).
- **Label integrado** (el que aplica sin plantilla): Logo arriba izq. mediano; Nivel de servicio arriba der. pequeño; Sender oculto;
  Consignatario y Dirección al medio izq.; Código de barras al centro mediano; Waybill oculto; Invoice abajo izq. pequeño; Monto COD
  abajo der. mediano.

## 5. Ajuste de la compañía: «Imprimir labels directo a la impresora»

- **Apagado por defecto** (ninguna compañía cambia de comportamiento hasta encenderlo). `PUT /api/v1/printing/settings`
  `{ "printLabelsDirect": true }` con `printers.manage`; también en la pantalla.
- Encendido, al **guardar una orden** en la entrada rápida o detallada sale **un label por pieza** a la impresora de labels de la
  estación (sin diálogo si la estación imprime con QZ Tray). La estación puede no imprimir apagando su «Imprimir al guardar».
  Desde F-G2 el interruptor «Label al guardar» de la entrada rápida y detallada viene encendido cuando el ajuste está encendido y la
  estación no se excluyó (sección 10).

## 6. Label de factura (bandera del cliente «Label de factura»)

- `Client.AutoPrintInvoice`, por cliente (el que manda, *sender*), apagada por defecto. Se ve en la ficha del cliente (`autoPrintInvoice`)
  y se cambia en el panel «Impresión y portal» de la ficha del cliente (antes estaba en el Perfil fiscal) (`PATCH …/profile` con `autoPrintInvoice: true|false`; sin el campo no cambia). Desde 2026-10-21
  enciende el **label de factura**.
- **Qué sale**: al guardar un envío de ese cliente que tenga **número de factura**, **después** de los labels de los paquetes sale **un** label de
  la factura (uno por envío, no por pieza) por la **misma impresora de labels** de la estación. Sale **solo, sin vista previa ni pregunta**, aunque la
  estación esté en «Confirmar antes de imprimir». Si el envío no tiene número de factura se avisa «{N} envíos sin número de factura no llevan label de factura.».
- **Cómo se ve**: lo define la plantilla de **clase Factura** (`kind = INVOICE`). Por omisión (sin plantilla) es el **código de barras del número de
  factura, centrado y grande, con el número pegado debajo** (sin «Paquete i de N»). En *Sistema → Impresoras y labels* hay un segundo constructor,
  «Construir el label de factura», igual al de las piezas (Nueva, Duplicar, Hacer predeterminada, Desactivar, Actualizar): ahí se puede agregar lo que
  haga falta (cliente, consignatario, dirección, COD, waybill…), moverlo y cambiar su tamaño. En ese constructor «Código de barras» **es el número de
  factura** y el elemento «Invoice» no aparece. Las plantillas de factura y las de piezas son listas aparte: cada clase tiene su predeterminada y sus
  nombres (se puede llamar igual una de piezas y una de factura). La clase no se puede cambiar después de creada.
- Se imprime desde la entrada rápida y la detallada. Los envíos de clientes sin la bandera no llevan label de factura.

## 7. Mensajes y códigos HTTP

| Código | Mensaje | Cuándo |
|---|---|---|
| 400 | «Escriba el nombre de la impresora.» | Alta sin nombre o PATCH con nombre vacío. |
| 400 | «El nombre no puede exceder 80 caracteres.» | Nombre de impresora o de plantilla de más de 80. |
| 400 | «El modelo no puede exceder 80 caracteres.» | |
| 400 | «La nota no puede exceder 300 caracteres.» | |
| 400 | «El nombre en el sistema no puede exceder 200 caracteres.» | |
| 400 | «Indique el uso de la impresora: LABEL (labels) o DOCUMENT (documentos).» | Alta sin uso. |
| 400 | «Uso de impresora desconocido: 'X'.» | Uso que no es LABEL ni DOCUMENT. |
| 400 | «El uso de la impresora no se puede cambiar; cree otra impresora.» | PATCH con otro uso. |
| 400 | «Conexión de impresora desconocida: 'X'. Use BROWSER (PDF del navegador) o QZ_TRAY (directo con QZ Tray).» | |
| 400 | «Una impresora de labels usa etiquetas de 4X6, 4X4 o 4X2.» | Tamaño que no es de labels. |
| 400 | «Una impresora de documentos usa papel carta (LETTER).» | Tamaño que no es carta. |
| 409 | «Ya existe una impresora activa con ese nombre.» | Alta, renombre o reactivación con el nombre de otra activa. |
| 409 | «La impresora está inactiva; reactívela antes de hacerla predeterminada.» | |
| 404 | «Impresora no encontrada.» | No existe o es de otra compañía. |
| 400 | «Escriba el nombre de la plantilla.» | |
| 400 | «Falta la construcción del label.» | Alta de plantilla sin `layout`. |
| 400 | «Posición desconocida en «Monto COD»: 'X'. Use tl, tc, tr, ml, mc, mr, bl, bc o br.» | El nombre del elemento cambia según el elemento. |
| 400 | «Tamaño desconocido en «Consignatario»: 'X'. Use sm, md o lg.» | Ídem. |
| 400 | «Solo el número de waybill y la factura se pueden mostrar como código de barras.» | |
| 400 | «El label necesita al menos un código de barras para poder escanearlo.» | |
| 400 | «El tipo de plantilla debe ser PIECE (label de pieza) o INVOICE (label de factura).» | `kind` desconocido en el alta o en `GET label-templates?kind=`. |
| 400 | «El tipo de la plantilla no se puede cambiar; cree otra.» | PATCH con un `kind` distinto al de la plantilla. |
| 409 | «Ya existe una plantilla de label activa con ese nombre.» | Dentro de la misma clase (pieza o factura). |
| 409 | «La plantilla está inactiva; reactívela antes de hacerla predeterminada.» | |
| 404 | «Plantilla de label no encontrada.» | |
| 409 | «El registro fue modificado por otro usuario; recargue e intente de nuevo.» | PATCH con `rowVersion` viejo. |
| 409 | «La impresión directa con QZ Tray no tiene certificado configurado en este servidor; QZ Tray pedirá «Permitir» en cada computadora (soporte: manual 12, «Certificado de QZ Tray»).» | `qz/certificate` o `qz/sign` sin certificado o sin clave. |
| 400 | «Falta el texto a firmar.» / «El texto a firmar no puede exceder 8192 caracteres.» | `qz/sign`. |
| 403 | `forbidden` | Sin el permiso de la acción. |

## 8. Casos frecuentes

- **«Al probar se abrió el diálogo del navegador en vez de imprimir directo»**: la impresora no es de conexión QZ Tray, o en esa
  computadora QZ Tray no está instalado o abierto. La pantalla lo dice («QZ Tray no detectado»). Instale QZ Tray, ábralo y pulse
  «Detectar impresoras de esta computadora».
- **«QZ Tray me pregunta "Allow / Permitir" cada vez»**: el servidor no tiene el certificado de Teikem configurado o esa computadora no
  tiene instalado el `override.crt` (sección 9).
- **«Desactivé la impresora predeterminada y ahora sale el PDF»**: el uso quedó sin predeterminada (sección 3). Haga otra predeterminada.
- **«La estación dice que la impresora elegida ya no está activa»**: alguien la desactivó; la estación usa la de la compañía hasta que
  elija otra y pulse Guardar.
- **«El label sale cortado en 4×2»**: demasiados elementos grandes; quite elementos o use Tamaño Pequeño (o una etiqueta más grande).

## 9. Soporte: QZ Tray y el certificado de Teikem

Esta sección es para **soporte** (no para el usuario final).

### 9.1 Instalar QZ Tray en cada computadora que imprime directo
1. Descargue QZ Tray de <https://qz.io/download/> e instálelo (Windows o macOS). Debe quedar abierto (ícono junto al reloj).
2. **Librería de la página**: la web carga `/vendor/qz-tray.js` (no se descarga de internet desde la página). Copie el archivo
   `qz-tray.js` de la versión instalada (en el paquete npm `qz-tray`, o en la carpeta `demo/js` de la instalación de QZ Tray) a
   `web-app/public/vendor/qz-tray.js` antes de publicar la web (queda en la raíz del sitio). Sin ese archivo la pantalla dice «La
   impresión directa no está disponible en esta computadora…» y todo sale con el PDF del navegador.
3. En Teikem → Sistema → Impresoras y labels: «Detectar impresoras de esta computadora», elegir la impresora de la estación y Guardar.

### 9.2 Generar el certificado propio de la plataforma (autofirmado)
Una sola vez, en una máquina de soporte con OpenSSL:
```
openssl req -x509 -newkey rsa:2048 -keyout teikem-qz.key -out teikem-qz.crt -days 3650 -nodes -subj "/CN=Teikem QZ Tray/O=Advance"
```
- `teikem-qz.crt` es el **certificado público** (se puede repartir). `teikem-qz.key` es la **clave privada**: no va en el repositorio,
  ni en un correo, ni en una respuesta del API.

### 9.3 Configurar el servidor
Una de estas dos formas (variables de entorno en el servidor del API, o archivos fuera del repositorio):
```
Printing__Qz__Certificate="<contenido PEM del .crt>"      o   Printing__Qz__CertificatePath=/etc/teikem/teikem-qz.crt
Printing__Qz__PrivateKey="<contenido PEM del .key>"       o   Printing__Qz__PrivateKeyPath=/etc/teikem/teikem-qz.key
Printing__Qz__PrivateKeyPassword=<solo si el .key está cifrado>
```
En las variables de una sola línea los saltos se pueden escribir como `\n`. Reinicie el API. `GET /api/v1/printing/station-options`
debe traer `"qzSigningConfigured": true` y `GET /api/v1/printing/qz/certificate` el certificado (sin la clave). Si la clave está
dañada o la contraseña no corresponde, el servidor la trata como «sin configurar» (409 claro, nunca 500).

### 9.4 Instalar el certificado en cada computadora (override.crt)
Para que QZ Tray confíe en Teikem sin preguntar:
1. Copie `teikem-qz.crt` a la carpeta de instalación de QZ Tray con el nombre **`override.crt`**
   (Windows: `C:\Program Files\QZ Tray\override.crt`; macOS: `/Applications/QZ Tray.app/Contents/Resources/override.crt`).
2. Reinicie QZ Tray. Al imprimir ya no aparece «Allow / Permitir».
La firma que pide QZ Tray (un resumen corto de cada petición) la hace el servidor con SHA512withRSA en `POST /printing/qz/sign`, con el
permiso de las opciones de la estación; solo sirve en las computadoras que instalaron ese `override.crt`.

## 10. Labels y documentos de las órdenes (Lote 44, F-G2)

La pantalla está en [F-G2 — Labels y documentos](frontend/fg2-labels-y-documentos.md). Todo se imprime con la configuración de la
estación (secciones 1 y 3) y nunca lleva el precio de la orden.

### 10.1 Labels de las órdenes
- **Uno por pieza**: la cantidad de cada línea de carga (de la unidad que sea) es el número de labels de esa línea. Cada label lleva en
  el código de barras el **número del paquete de su línea** (el mismo en las piezas de la línea) y «Paquete i de N» (i corrido sobre
  todas las piezas de la orden; N = sus piezas).
- Contenido: logo = nombre de la compañía; nivel de servicio; «De:» el cliente; «A:» el consignatario; la dirección de entrega; número de
  waybill = número de la orden; factura = factura del cliente; COD solo si la orden lo tiene.
- No se imprimen: las órdenes **canceladas o dadas de baja** («{N} órdenes canceladas no se imprimieron.») ni las piezas de una línea **sin
  número de paquete** («{N} piezas sin número de paquete no se imprimieron.»).
- **Sin tope**: desde 500 labels se avisa «Son {N} labels: el PDF será grande y puede tardar un poco. Se imprimen todos; no hay límite.».
- Dónde: lista de Órdenes («Imprimir label» de las marcadas y «Todos los labels» de todo lo filtrado), ficha de la orden («Imprimir label») y
  **«Label al guardar»** en la entrada rápida y detallada (viene encendido si la compañía tiene «Imprimir labels directo a la impresora» y la
  estación no apagó su «Imprimir al guardar»). Permiso: `orders.view` para imprimir; `orders.create` para capturar.
- **Un fallo de impresión nunca deshace la orden**: «La orden se guardó, pero no se pudo imprimir el label: {motivo}. Use «Imprimir label» en la ficha.».

### 10.2 Documentos (impresora de documentos de la estación, papel carta)
- **Waybill summary**: una fila por orden (Empaque, N.º de envío, Factura, Cliente, Consignatario, Pueblo, ZIP, Servicio, Paquetes, Piezas,
  COD), agrupado por cliente con subtotal de órdenes, piezas y COD, y total general. Carta apaisada.
- **Packing list**: una sección por orden (empaque, orden, factura, consignatario y dirección) con sus paquetes (tipo, n.º de paquete,
  descripción, piezas y peso) y subtotal; total general de piezas y peso. Carta vertical.
- De las órdenes **marcadas** o, sin marcas, de **todo lo filtrado** (todas las páginas). Las canceladas no entran (se avisa en el documento).
- **Informe de faltantes** (estación de escaneo, `trips.view`): las órdenes que siguen pendientes por escanear con los mismos filtros de
  «Pendientes» (todas las páginas, sin tope), agrupadas por zona («Sin zona» y «Zona ambigua» al final), con Empaque, Orden, Factura, Cliente,
  Consignatario, Pueblo, ZIP, Piezas y Fecha pedida, y el resumen de pendientes y escaneadas en esta sesión. Carta apaisada.

### 10.3 Escanear el número de paquete
- El escaneo de salida y `GET /api/v1/orders/lookup` buscan el **número de paquete** como cuarta precedencia (después de número de envío,
  empaque y factura): el resultado dice «Encontrada por número de paquete.».
- El número de paquete es por cliente (PQT-##### de cada cliente) o tecleado, así que puede repetirse: con dos o más órdenes el escaneo
  responde «El número de paquete está en {n} órdenes (de clientes distintos o repetido); escanee el empaque o el número de envío.» y no asigna.
- La búsqueda usa el índice `IX_CargoLine_PackageNumber` (sección «2026-10-12 (e)» de `logistica-db-update.sql`; se crea con `db-update`).

## Imprimir al crear el envío: es por cliente (2026-10-22)

Imprimir **el label** y **la factura** automáticamente al crear un envío es un dato **de cada cliente** (ficha del cliente → Perfil → «Imprimir los labels automáticamente» y «Imprimir el label de la factura automáticamente»), no de la compañía. Por eso Sistema → Impresoras y labels **ya no tiene** el interruptor «Imprimir labels directo a la impresora», ni la estación el de «Imprimir al guardar». En la entrada de envíos queda el interruptor de la sesión (apagado por omisión) para imprimir los labels de esa captura aunque el cliente no lo tenga. Las compañías que tenían el ajuste general encendido pasaron ese valor a todos sus clientes una sola vez (migración 2026-10-22).

**Ajuste (2026-10-22, mismo día).** El ajuste de la compañía vuelve, pero como **valor por omisión de los clientes nuevos**: Sistema → Ajustes de la compañía → Operación → Valores por defecto → «Imprimir los labels automáticamente en los clientes nuevos». Al crear un cliente nace con ese valor y luego se enciende o se apaga en su ficha (Perfil); no cambia a los clientes que ya existen. La decisión de imprimir al crear un envío sigue siendo la del cliente.
