# Migración Colavora — cambios pendientes por aplicar

> Anotado el 2026-10-20 por dictado de Luis. **Todavía no se implementa nada de esto** (ni en el código ni en ninguna base). La migración se corre sobre la producción restaurada, así que no hay repunte de datos locales: la primera corrida ya sale limpia.

## 1. Teléfonos: solo dígitos y prefijo internacional (`CountryCallingCode`)

**Contexto.** Otra sesión cambia el modelo de teléfonos (diseño en `docs/busqueda-global-dictado-2026-10-20.md`, §6):
- `ContactPoint.CountryCallingCode` `VARCHAR(4) NULL`: prefijo internacional sin «+», solo dígitos (`1` para EE. UU. y Puerto Rico). `NULL` = el de la compañía (`Tenant.DefaultPhoneCountryCode`).
- `ContactPoint.Value`: pasa a guardar **solo los dígitos nacionales** (p. ej. `7875551234`), sin formato. `Extension` aparte, también solo dígitos.
- `Supplier.PhoneCountryCallingCode` igual para `Supplier.Phone`.
- El formato de despliegue (`(###) ###-####`) es de la compañía (`Tenant.PhoneFormat`) y solo se aplica al mostrar.

**Qué cambia en la migración** (cuando ese modelo esté aplicado):
1. Todo teléfono que migramos se normaliza antes de guardarlo: quitar espacios, guiones, paréntesis, puntos y «+»; quedan solo dígitos.
2. Si el número trae prefijo internacional (`+1`, `1` delante de 10 dígitos, `+593…`), se separa a `CountryCallingCode`; si no lo trae, se deja `NULL` (= el de la compañía, `1` para Advance y De La Paz).
3. Extensiones («ext 123», «x123», «#123») salen del número y van a `Extension`, solo dígitos.
4. Se aplica a todos los sitios donde la migración hoy escribe teléfonos:
   - Teléfono y fax del cliente (remitente): `ColavoraImportService.Masters.cs`, bloque «contactos», ~línea 367.
   - Teléfono del consignatario: mismo archivo, ~línea 479 (`ContactPoint` con `Value = Trim(src.Phone, 200)`).
   - Teléfonos de la orden (`SenderPhone`, `PhoneNumber`) que hoy van como texto en las notas de la dirección (`Orders.cs`, `JoinNotes`): se quedan en las notas tal cual; revisar si conviene también guardarlos como contacto.
5. Una función pura y probada en `ColavoraRules` (p. ej. `NormalizePhone(text) → (prefijo?, dígitos, extensión?)`), con pruebas de los formatos reales.
6. **Antes de escribirla, analizar los formatos reales de Colavora** (consulta de solo lectura sobre `Phone`/`FaxPhone`/`PhoneNumber`): cuántos traen letras, extensiones, varios números en un campo, menos de 10 dígitos, etc. Lo que no se pueda normalizar se reporta como advertencia (no se pierde: queda en el reporte con el valor original).
7. Fax: decidir si también va solo en dígitos (lo normal) con su `CountryCallingCode`.

**Orden de trabajo:** primero el modelo y el servicio de contactos (la otra sesión); después la migración llama al mismo servicio/regla para no duplicar la validación (`ContactPointService.ValidateValue`).

## 2. Tipos de servicio y de paquete: siempre por compañía (no globales)

**Estado hoy.** Mezcla: ND→`STANDARD`, SD→`SAMEDAY`, PK→`PICK_UP`, BX→`BOX`, EN→`ENVELOPE`, PL→`PALLET` apuntan a los catálogos **globales**; el resto se crea propio de la compañía, con sufijo `_10030` cuando el código choca (`PACKAGE_10030`, `INVOICE_10030`, `DRUM_10030`, `OTHER_10030`, `ROUNDTRIP_10030`). Las etiquetas en español salen sin traducir.

**Decisión de Luis (2026-10-20):** transporte aún no está en producción, así que **tipo de servicio y tipo de paquete dependen de la compañía**, no de catálogos globales.

**Qué cambia** (sin hacerlo todavía):
- `ColavoraRules.GlobalServiceType` y `GlobalPackageType` se quitan; `ColavoraImportService.Masters.cs` (~líneas 37–58) crea siempre el tipo como propio de la compañía, con el mismo código en las dos compañías (sin sufijos).
- Ajustar `ColavoraRulesTests.cs`.
- No se toca el seed/estructura congelados ni `logistica-db-update.sql`; los globales quedan como están.
- Sin script de repunte: se restaura producción y se corre la migración de nuevo.
- Mejorar las etiquetas es/en (hoy casi todas iguales; «Aceite»/«Gomas» de De La Paz están en el idioma contrario).

**Preguntas abiertas.**
- ¿Las variantes Isla (SAME DAY / NEXT DAY / PICKUP ISLAND) se quedan como servicios propios de la compañía? (Por defecto sí, con la decisión de arriba.)
- ¿Una compañía nueva de transporte parte de cero o con plantilla inicial de servicios y paquetes? (No afecta la migración.)

## 3. Piezas y contenido: una sola línea de paquete con el contenido en la descripción

**Hallazgo (2026-10-20, orden `0079372073221100AXM`, AxisCare).** Colavora tiene 2 piezas físicas (`Qty = 2`) y 52 ítems de contenido. La migración guardó `TotalPieces = 2` y una línea con el número de paquete, pero los 52 ítems quedaron como **52 líneas de carga separadas** (tipo Caja, cantidad 1, sin número de paquete). En la pantalla de Envíos eso se ve como «cada cosa es un paquete», el encabezado dice «54 piezas» (suma las cantidades de todas las líneas) y la tabla de paquetes no tiene columna de número de paquete. La línea real además quedó con descripción `0` (comentario de la pieza en Colavora).

**Decisión de Luis (2026-10-20):** NO listar los ítems. Una sola línea por tipo de paquete real, con el contenido dentro de su descripción; se puede ampliar el campo `Description` a un máximo (recomendado `NVARCHAR(MAX)`).

**Qué cambia (sin hacerlo todavía):**
- **Migración:** las piezas de tipo descripción (`DescriptionPackageTypes`) ya no crean líneas: su texto se concatena en la descripción de la línea del paquete físico, un ítem por renglón con su cantidad (`1 × 166-1199 · DYNASHIELD…`). Si la orden no tiene pieza física, la línea de «sin piezas» (waybill-n) lleva ese contenido. La descripción `0` (comentario sin valor) queda vacía. Revisar `ColavoraImportService.Orders.cs` (~líneas 244–275) y `ColavoraRules.LineDescription`.
- **Estructura:** `CargoLine.Description` a `NVARCHAR(MAX)`: sección nueva al final de `Diseño/logistica-db-update.sql` (idempotente). Antes, revisar que ningún índice incluya la columna y los límites de longitud en el API, el importador y las apps.
- **Pantallas (Envíos y ficha de la orden):** el encabezado muestra las piezas reales (no la suma de cantidades de todas las líneas); columna **número de paquete** en la tabla de paquetes; en la lista, la columna Paquete se corta con «…» y el texto completo solo en la ficha.
- Se pierde la cantidad por ítem como dato aparte y el filtro exacto por producto; queda la búsqueda de texto en la descripción.
- **Cuándo:** con las otras sesiones en calma (toca esquema, API y pantallas compartidas).

## 4. Clientes de AxisCare: un solo cliente, división como catálogo, «Recogido/Devolución» como tipo de orden

**Hallazgo (2026-10-20, datos reales de Colavora, compañía 10029).** AxisCare tiene 17 registros de cliente, pero **410,108 de sus órdenes están en uno solo: el 10532 «AXISCARE HEALTH LOGISTIC (NEW)»** (2023-08 → hoy). Los otros 16 suman unas 12 órdenes (Devolución Equipo 2, Devolución Tirillas 1, Devolución RX 1, Health Logistic viejo 5, 10100 con 1) y los «Recogido X», AXIS MED y AXIS MED VET no tienen ninguna. Las divisiones (11 definidas en `Customers_Divisions`: GAM AxisMed, GLC Equipamiento, GLO Pick-up, GLR T/R, GLT T/L, GLV Vet, GN Nutrición, ADV, NA, OXY Oxígeno, AFM FirstMed, cada una con marcas Invoice/Inbound/Outbound/Dashboard) son la «línea de servicio» dentro del 10532; en Teikem hoy van como texto en el campo personalizado `division`.

**Decisiones de Luis (2026-10-20):**
1. **Un solo cliente por empresa.** Los clientes cuyo nombre termina en «RECOGIDO …» o «DEVOLUCION …» no son clientes aparte: **se atan al cliente que dice el primer nombre** (p. ej. «AXISCARE …» → AxisCare), y sus órdenes se trabajan **como recogido (pick up) o como devolución**, según lo que diga el nombre del cliente. Hay que aplicar la regla en general (no solo a AxisCare) y probarla con todos los nombres de clientes de las dos compañías.
2. **La división se queda atada a la orden** (campo personalizado `division`, lista desplegable), como está. **No es un catálogo por cliente**: en Colavora las 11 divisiones son una lista de la compañía (`Customers_Divisions` no las ata a ningún cliente). Solo revisar que la migración traiga las 11 y que los valores de las órdenes coincidan con esa lista (códigos GLT/GLC/OXY… vs el nombre que se ve en pantalla). Si luego se factura por división, se agrupa por ese campo, sin sub-clientes.
3. **Contratos y tarifas** se rehacen sobre esa base: un contrato por cliente real; las tarifas de los clientes «Recogido/Devolución» pasan al contrato del cliente como tarifas de pick up y de devolución (en vez de contratos propios de cada uno).
4. **Pregunta abierta (Luis todavía no sabe):** ¿se factura una factura por división o todo junto? Eso decide si la división es solo etiqueta o parte de la facturación (agrupar la factura por división / sub-cliente de facturación). No se implementa la facturación por división hasta tener la respuesta.

**Qué cambia en la migración (sin hacerlo todavía):** `ImportClientsAsync` y lo que cuelga de ella (contactos, ubicaciones, contrato, tarifas, `Customers_Service_Price` por `SenderID`), la resolución del cliente de cada orden en `ImportOrderBatchAsync` (tipo pick up / devolución por el nombre del cliente de origen) y el campo `division` (validar contra la lista de 11). Regla pura y probada en `ColavoraRules` (nombre → cliente principal + tipo).

## 5. Choferes: solo los que aparecen asignados en las órdenes

**Hallazgo.** Hoy la migración trae **todos los usuarios** de Colavora como choferes (activos e inactivos, D13). Luis lo corrige: **un chofer es quien aparece asignado en las órdenes**, no cualquier usuario.

**De dónde sale:** `Shipments.AssignDL` (chofer que entrega) y `Shipments.AssignPK` (chofer que recoge, pick up), ambas `INT` = ID de usuario. Entre las dos compañías hay 140 valores distintos de `AssignDL` y 109 de `AssignPK`.

**Decisión de Luis (2026-10-20):**
- Hacer un **análisis para machear** esos números de usuario con la tabla de usuarios (`colavora_courier_login`) y con los códigos de chofer que usan eventos y manifiestos (el código con y sin ceros, «003» y «3»), y **crear como choferes solo a esos**.
- Los demás usuarios (staff, administradores) no son choferes: siguen la regla de usuarios (por invitación, sin portal), no se crean como `Driver`.
- Los choferes que solo aparecen en eventos (hoy se crean inactivos como «Chofer N») salen de ese mismo cruce.

**Qué hacer antes de implementar:** consulta de solo lectura que cruce `AssignDL`/`AssignPK` (y los códigos de chofer de eventos/manifiestos) con los usuarios, y reporte con: cuántos matchean, cuántos `AssignDL`/`AssignPK` no tienen usuario, cuántos usuarios nunca fueron asignados, y duplicados por código con/sin ceros. Revisar `ImportUsersAndDriversAsync` (Masters.cs) y `ResolveDriver` (Orders.cs).

## 6. Pulso del día — «Paquetes en la calle» y «Almacén hoy» (dictado 2026-10-20)

> Solo anotado; **nada implementado todavía**. Toca la pantalla Pulso (otra sesión trabaja en Pulse: `PulsePanels.cs`, `AnalyticsService.cs`, `Pulse*.tsx`) y la migración de Colavora; coordinar antes de escribir.

### 6.1 Los cinco indicadores de «Paquetes en la calle», en este orden

| # | Indicador | Qué cuenta | Notas |
|---|---|---|---|
| 1 | **Entrada** | Órdenes **sometidas** (nuevas) | Se queda como está. |
| 2 | **En almacén** | Lo **escaneado que entró al almacén**: por recogido (pick up escaneado) **o** por el escaneo de enrutamiento en el almacén | **Sin duplicar**: una orden cuenta una vez aunque tenga los dos escaneos. **Debe coincidir con Entrada** (las sometidas entran al almacén). |
| 3 | **Rutas** (con barras de datos) | Cantidad de **rutas de entrega que salieron ese día** | Luis duda de que tenga sentido porque es casi constante; se deja y se revisa al verlo. Va **antes** de Entregado. |
| 4 | **Entregado** | Entregadas, **con histórico de todos los días** (barras con «tanto, tanto, tanto…») | **Ojo con la migración**: hoy el indicador parece hablar solo de «hoy» y las barras salen casi vacías; con la migración debe verse entrega diaria en todos los días. |
| 5 | **Lo que pasa ahora mismo** | **En ruta** (grande) y, abajo y más pequeño, **Completada** | Sustituye al «En ruta» actual. |

Detalles de pantalla:
- «Almacén hoy» → la tarjeta **«Productos bajo mínimo»** no tiene nada en la parte de abajo: ponerle **«Productos en backorder»** para llenar ese espacio.
- Sobre el enlace de la última tarjeta de cada fila (dictado en dos sentidos): Luis dijo «olvídate de eso», **no se cambia nada** con el enlace.

### 6.2 Lo que debe tomar en cuenta la migración y la fuente de datos

- **En almacén** se alimenta del escaneo por recogido y del escaneo de enrutamiento en el almacén; la migración debe generar esos escaneos/eventos (o la fuente debe poder derivarlos del historial de estatus) **y la fuente no puede contarlos dos veces**.
- **Entregado**: la migración debe dejar fechas de entrega reales por orden (`deliveredAt` del historial) para que las barras diarias tengan datos todos los días.
- **Rutas del día**: viene de los viajes (`Trip`, uno por manifiesto `M-<manifiesto>`); verificar que cada viaje migrado tenga su fecha de salida.

### 6.3 «4,238 envíos sin chofer… la más antigua 92 días» — hallazgo

Luis tiene razón en que no tiene sentido (son órdenes viejas que ya se entregaron). Lo que muestran los datos (consulta de solo lectura, 2026-10-20):

- En la base local hay 4,235 órdenes de Advance Logistics y **30,837 de De La Paz** en estatus **Asignada** (todas de jul–oct 2026, repartidas por mes), más 772 «Sin asignar». El panel suma las que no tienen ruta con chofer.
- **No es un error de la migración: Colavora mismo las tiene así.** En Colavora, desde 2026-07-01, el estatus actual es `Assigned` en 4,277 órdenes de Advance y **31,421 de De La Paz** (frente a 47,135 `Completed` en De La Paz). De esas 31,421 de De La Paz: **0 tienen comprobante de entrega (POD) y 0 tienen evento de entrega**. Es decir, en Colavora nunca se cerraron: o se entregaron sin registrarlo, o De La Paz las cierra en lote (Completed) y estas quedaron atrás.
- La migración respeta el estatus de Colavora (`Assigned` → Asignada). Solo cierra solas las `Pending` de más de 5 días (D3).

**HECHO (2026-10-20): opción de Luis.** `-DiasAbiertas N` (script) / `--dias-abiertas N` (verbo) / `Options.OpenOrderDays` (config): las órdenes abiertas de **cualquier estatus** (sometida, recogida, sin asignar, asignada, en tránsito) con más de N días desde su creación se migran como **Entregada** (con nota en el historial «Cerrada por la migración…»); canceladas y devueltas no se tocan. Sin la opción, queda como antes (solo Pending de más de 5 días). Ej.: `.\scripts\migrar-colavora.ps1 -Compania advance -DiasAbiertas 30`. Límite: las órdenes con el estatus «basura» del bug de jun–jul 2026 se resuelven por su último evento y no pasan por esta regla.

**(Resuelto con la opción de arriba.)** ¿aplicar a las `Assigned` viejas la misma regla que a las `Pending` (D3: si pasaron N días sin movimiento se migran como Entregada)? Si sí, ¿cuántos días (¿5, 10, 30?) y se marca de alguna forma que se cerraron por la migración? Mientras tanto, el panel «Por despachar» (que cuenta las asignadas sin ruta) estará inflado por esas órdenes. Además, a futuro el panel debería **excluir lo que tenga más de X días sin movimiento** o mostrarlo aparte como «atascadas».

## 7. Números de la orden: Empaque, Conduce y Paquete (aclaración de Luis, 2026-10-20)

**Lo que dijo Luis:** el «número de empaque» es el número de **cada pieza**; un envío puede tener varias piezas, así que puede tener varios números. Hoy el listado de Envíos tiene una columna «Empaque» con un solo número por orden, y la migración pone el waybill de Colavora en Orden, Empaque y Conduce (los tres iguales).

**Lo que dice el diseño actual (maestro §2):** `OrderNumber` (orden), `ClientInvoiceNumber` (factura), `PackBatchNumber` (Empaque: **uno por orden**, identificador de escaneo del bulto/embarque, `EMP-#####` o el lote de Recolección y empaque), `WaybillNumber` (Conduce: **uno por orden**, fórmula del Lote 110: `{código del cliente}{código del consignatario o su ID}{aammdd}{secuencial del día de ese consignatario}`; antes del Lote 110 era `CND-#######`) y `CargoLine.PackageNumber` (Paquete: **uno por pieza**). Lo que Luis describe como «empaque» es, en el diseño, el **número de paquete** (por pieza).

**Cambios anotados (sin hacerlos todavía):**
- **Listado de Envíos:** quitar la columna «Empaque» de orden; poner una columna con **todos los números de paquete de la orden** (lista separada por comas) **después de «Paquete»**; **quitar la columna «Cotizado»**. El filtro por número se queda y debe buscar en los números de paquete (y en el de empaque).
- **Migración:** el waybill de Colavora es en esencia el **Conduce** (guía), así que `WaybillNumber` = waybill está bien. `PackBatchNumber` y el número de envío no deberían ser copias del waybill: decidir con Luis qué van a ser (`EMP-…` generado por Teikem, o el número de envío del cliente si Colavora trae uno aparte). Los números de pieza (`Shipments_Pieces.Barcode`) ya se migran como `PackageNumber`.
- **Decisión pendiente de Luis:** si el número de empaque por orden (`PackBatchNumber`) se conserva como identificador de escaneo de la orden (como dice el diseño) o se elimina de la pantalla y queda solo el de pieza.

### 7.1 Propuesta: un solo identificador por orden (análisis 2026-10-20, a validar con Luis)

**Problema que ve Luis:** hay un número de más. Su modelo: la orden tiene **un identificador**, el del código de barras; lo trae el cliente o, si no, lo pone Teikem. Solo si el cliente maneja **dos** números (uno de escaneo y otro de orden) hacen falta dos.

**Hallazgo:** Empaque (`PackBatchNumber`, por orden) y Conduce (`WaybillNumber`) son, a nivel de orden, lo mismo: un número único por orden que genera Teikem. El Conduce nació en el Lote 97 (pedido de Luis) porque «el número de orden se repite entre clientes y el de empaque es del almacén». Empaque nació del almacén (lote de Recolección y empaque). El escaneo ya busca por orden, conduce y paquete, así que no hace falta un campo exclusivo de escaneo.

**Propuesta:**
- **Un identificador por orden** (la columna de Conduce): el del cliente si lo trae y es único en la compañía; si no, el que arma la fórmula del Lote 110 (ya existe, no se inventa nada): `{código del cliente}{código del consignatario o su ID}{aammdd}{secuencial del día de ese consignatario}`.
- **Número de orden aparte** solo cuando el cliente maneja dos números.
- **Quitar Empaque de la orden.** El lote de Recolección y empaque conserva su propio `EMP-…` (es del lote, no de la orden). Se quedan factura y número de paquete por pieza.
- **Migración:** el waybill de Colavora (lo trae el cliente, p. ej. AxisCare con sufijo `AXM` y su factura dentro) es el identificador; Orden y Empaque dejan de ser copias suyas.
- **Costo:** Empaque se usa en escaneo de salida, cross-dock, viajes, trazabilidad y lote de empaque (~24 archivos): **lote aparte**. Versión barata mientras tanto: no mostrarlo en pantalla y llenarlo con el mismo identificador.

**Decisión pendiente:** si el número del cliente se **repite** en la compañía, ¿se rechaza (el cliente lo corrige) o Teikem le pone el suyo (el de la fórmula) y guarda el del cliente como número de orden?

### 7.2 Pantalla de Envíos y Recolección y empaque (dictado de Luis, 2026-10-20)

- **El conduce es el número de envío**: es uno solo y el más importante, por el que más se busca. Sale **una sola columna**: Conduce (la primera). El número de orden **no va junto**: solo aparece, de forma secundaria, cuando es distinto del conduce (caso excepcional) o como columna opcional.
- **Quitar de la tabla** la columna Empaque y Cotizado (ver §7). Agregar la columna con los números de paquete, después de Paquete.
- **Filtros de Envíos a cambiar (por dictar el detalle):** el filtro principal pasa a ser **Conduce**; el de número de orden se mueve a un segundo plano; se quita el filtro de Empaque y entra **Nº de paquete**; la búsqueda libre busca en conduce, número de orden, números de paquete y factura.
- **Recolección y empaque:** hoy usa el número del lote (`EMP-#####`, contador PACKBATCH), que además se copia a la orden como Empaque. Debe mostrar y usar el **conduce**; el conduce se crea ahí, cuando se empaca y nace la orden, con la misma fórmula (Lote 110). El número del lote queda solo como identificador interno del lote.
- **Cuando el cliente trae su propio número de envío** (como el waybill de Colavora) hoy no se puede: el conduce «no se teclea» (Lote 110). Hay que permitir que lo traiga el cliente (importación, API de clientes, migración) y que Teikem lo genere con la fórmula solo si no viene.
