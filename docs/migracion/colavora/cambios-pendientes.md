# Migración Colavora — cambios pendientes por aplicar

> Anotado el 2026-10-20 por dictado de Luis. **Todavía no se implementa nada de esto** (ni en el código ni en ninguna base). La migración se corre sobre la producción restaurada, así que no hay repunte de datos locales: la primera corrida ya sale limpia.

## 1. Teléfonos: solo dígitos y prefijo internacional (`CountryCallingCode`)

> ✅ **HECHO (2026-10-21).** El modelo (Lote 162, `PhoneRules`) ya existía; la migración usa ahora `ColavoraRules.ParsePhones`: un número por cada teléfono del campo (separados por «/» o «;», el primero principal, los demás con etiqueta «Otro»), prefijo `1` si no trae otro, extensión («ext 12», «x45», «#12») aparte y solo dígitos. **Nada de texto suelto como teléfono**: lo que no se puede leer («PR», `9.99999E+11` de Excel, «(787», segundo tramo corto) va como **advertencia** en el reporte (con el valor original) y no se guarda. Aplica a teléfono y fax del cliente (servicio de contactos) y al teléfono del consignatario. Formatos reales de Colavora vistos (solo lectura): `(787) 555 - 1234` 20,935; `(787) 555-1234` 16,824; vacío 3,459; 7 dígitos sin código de área (`555-1234`) 1,316 — se guardan con prefijo `1` y 7 dígitos; ~60 con dos números; ~35 basura. Pruebas en `ColavoraRulesTests`.

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

> ✅ **HECHO (2026-10-21).** La migración ya no usa los tipos globales: todos los tipos de servicio y de paquete de Colavora se crean **propios de cada compañía** (`ColavoraRules.CompanyCatalogCode`; se quitaron `GlobalServiceType`/`GlobalPackageType`). El servicio y paquete «por omisión» (cuando una orden no trae el suyo) pasan a ser el «Next Day» y el «Box» de la compañía. **Limitación encontrada:** `UQ_LookupCode(Entity, InternalCode)` es **único a nivel global**, así que dos compañías no pueden tener el mismo código sin sufijo; por eso el código lleva siempre el CompanyID (`NEXT_DAY_10029`, `NEXT_DAY_10030`, `BOX_10029`…), de forma uniforme y sin depender del orden de las corridas. El nombre que ve el usuario sale de las etiquetas, no del código. Quitar el sufijo exigiría cambiar ese índice (estructura, `update.sql`) y todo lo que resuelve catálogos por código: no se hizo. Pendiente aparte: traducir las etiquetas es/en (hoy casi todas iguales).

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

> ✅ **HECHO (2026-10-21).** *Esquema:* `CargoLine.Description` pasa a `NVARCHAR(MAX)` (sección nueva al final de `update.sql`, idempotente); el tope de captura sube de 250 a 4000 (`OrderRules.DescriptionMaxLength`, también en la web). *Migración:* los ítems de AxisCare ya no son líneas aparte: van como texto en la descripción de la línea del paquete real (un ítem por renglón, «cantidad × código · texto», los repetidos sumados; `ColavoraRules.ContentText`), y se descartan los comentarios vacíos (`0`, `-`, `N/A`; `CleanComment`). *Pantallas:* la ficha cuenta las **piezas reales**, la tabla de paquetes tiene la columna **N.º de paquete** y respeta los saltos de línea de la descripción; en la lista, la descripción se corta con «…» (completa al pasar el cursor).


**Hallazgo (2026-10-20, orden `0079372073221100AXM`, AxisCare).** Colavora tiene 2 piezas físicas (`Qty = 2`) y 52 ítems de contenido. La migración guardó `TotalPieces = 2` y una línea con el número de paquete, pero los 52 ítems quedaron como **52 líneas de carga separadas** (tipo Caja, cantidad 1, sin número de paquete). En la pantalla de Envíos eso se ve como «cada cosa es un paquete», el encabezado dice «54 piezas» (suma las cantidades de todas las líneas) y la tabla de paquetes no tiene columna de número de paquete. La línea real además quedó con descripción `0` (comentario de la pieza en Colavora).

**Decisión de Luis (2026-10-20):** NO listar los ítems. Una sola línea por tipo de paquete real, con el contenido dentro de su descripción; se puede ampliar el campo `Description` a un máximo (recomendado `NVARCHAR(MAX)`).

**Qué cambia (sin hacerlo todavía):**
- **Migración:** las piezas de tipo descripción (`DescriptionPackageTypes`) ya no crean líneas: su texto se concatena en la descripción de la línea del paquete físico, un ítem por renglón con su cantidad (`1 × 166-1199 · DYNASHIELD…`). Si la orden no tiene pieza física, la línea de «sin piezas» (waybill-n) lleva ese contenido. La descripción `0` (comentario sin valor) queda vacía. Revisar `ColavoraImportService.Orders.cs` (~líneas 244–275) y `ColavoraRules.LineDescription`.
- **Estructura:** `CargoLine.Description` a `NVARCHAR(MAX)`: sección nueva al final de `Diseño/logistica-db-update.sql` (idempotente). Antes, revisar que ningún índice incluya la columna y los límites de longitud en el API, el importador y las apps.
- **Pantallas (Envíos y ficha de la orden):** el encabezado muestra las piezas reales (no la suma de cantidades de todas las líneas); columna **número de paquete** en la tabla de paquetes; en la lista, la columna Paquete se corta con «…» y el texto completo solo en la ficha.
- Se pierde la cantidad por ítem como dato aparte y el filtro exacto por producto; queda la búsqueda de texto en la descripción.
- **Cuándo:** con las otras sesiones en calma (toca esquema, API y pantallas compartidas).

## 4. Clientes de AxisCare: un solo cliente, división como catálogo, «Recogido/Devolución» como tipo de orden

> ✅ **HECHO en parte (2026-10-21).** *Hecho:* los clientes cuyo nombre lleva «RECOGIDO» / «DEVOLUCION» **ya no se crean como clientes**: se unen al cliente del primer nombre que más órdenes tenga (`ColavoraRules.ResolveSubClients`; para AxisCare, los 14 se unen al 10532 «AXISCARE HEALTH LOGISTIC NEW», 410,108 órdenes). Sus órdenes (y consignatarios) cuelgan del cliente real; las de un «Recogido» salen con el servicio **Pick up** de la compañía; las de una «Devolución» llevan «Devolución.» al inicio de la nota. Comparten el contrato del cliente real (no se crea otro). El reporte trae una línea «Cliente unido …» por cada uno, y las tarifas de esos clientes **no se migran solas**: salen como advertencia con sus precios (en Colavora son marcadores: 1.00, 5.75 y 100.00 para el mismo servicio/paquete). *Pendiente de Luis:* (a) qué hacer con esas tarifas (pasarlas a pick up / cargo por devolución del cliente); (b) cómo se trata una orden de «Devolución» (hoy solo nota; las órdenes de devolución de Teikem se ligan a una orden original, que aquí no existe); (c) la regla se aplica a **todos** los clientes con ese patrón, pero solo AxisCare lo tiene hoy; De La Paz tiene el mismo caso por servicio («MASCARO - PORTER (NEXT DAY)» y «(SAME DAY)») — ¿también se unen? *Sin cambios:* la división sigue atada a la orden (lista de las 11); falta revisar los valores fuera de la lista (`40`, `REC`, `AMA`, ~8 órdenes); la facturación por división sigue pendiente de tu respuesta.

**Hallazgo (2026-10-20, datos reales de Colavora, compañía 10029).** AxisCare tiene 17 registros de cliente, pero **410,108 de sus órdenes están en uno solo: el 10532 «AXISCARE HEALTH LOGISTIC (NEW)»** (2023-08 → hoy). Los otros 16 suman unas 12 órdenes (Devolución Equipo 2, Devolución Tirillas 1, Devolución RX 1, Health Logistic viejo 5, 10100 con 1) y los «Recogido X», AXIS MED y AXIS MED VET no tienen ninguna. Las divisiones (11 definidas en `Customers_Divisions`: GAM AxisMed, GLC Equipamiento, GLO Pick-up, GLR T/R, GLT T/L, GLV Vet, GN Nutrición, ADV, NA, OXY Oxígeno, AFM FirstMed, cada una con marcas Invoice/Inbound/Outbound/Dashboard) son la «línea de servicio» dentro del 10532; en Teikem hoy van como texto en el campo personalizado `division`.

**Decisiones de Luis (2026-10-20):**
1. **Un solo cliente por empresa.** Los clientes cuyo nombre termina en «RECOGIDO …» o «DEVOLUCION …» no son clientes aparte: **se atan al cliente que dice el primer nombre** (p. ej. «AXISCARE …» → AxisCare), y sus órdenes se trabajan **como recogido (pick up) o como devolución**, según lo que diga el nombre del cliente. Hay que aplicar la regla en general (no solo a AxisCare) y probarla con todos los nombres de clientes de las dos compañías.
2. **La división se queda atada a la orden** (campo personalizado `division`, lista desplegable), como está. **No es un catálogo por cliente**: en Colavora las 11 divisiones son una lista de la compañía (`Customers_Divisions` no las ata a ningún cliente). Solo revisar que la migración traiga las 11 y que los valores de las órdenes coincidan con esa lista (códigos GLT/GLC/OXY… vs el nombre que se ve en pantalla). Si luego se factura por división, se agrupa por ese campo, sin sub-clientes.
3. **Contratos y tarifas** se rehacen sobre esa base: un contrato por cliente real; las tarifas de los clientes «Recogido/Devolución» pasan al contrato del cliente como tarifas de pick up y de devolución (en vez de contratos propios de cada uno).
4. **Pregunta abierta (Luis todavía no sabe):** ¿se factura una factura por división o todo junto? Eso decide si la división es solo etiqueta o parte de la facturación (agrupar la factura por división / sub-cliente de facturación). No se implementa la facturación por división hasta tener la respuesta.

**Qué cambia en la migración (sin hacerlo todavía):** `ImportClientsAsync` y lo que cuelga de ella (contactos, ubicaciones, contrato, tarifas, `Customers_Service_Price` por `SenderID`), la resolución del cliente de cada orden en `ImportOrderBatchAsync` (tipo pick up / devolución por el nombre del cliente de origen) y el campo `division` (validar contra la lista de 11). Regla pura y probada en `ColavoraRules` (nombre → cliente principal + tipo).

## 5. Choferes: solo los que aparecen asignados en las órdenes

> ✅ **HECHO (2026-10-21).** Chofer = usuario cuyo ID aparece en `Shipments.AssignDL` o `AssignPK` de la compañía (`ColavoraReader.ReadAssignedUserIdsAsync`), ya no «grupo de seguridad 4». El reporte de la migración trae una línea con el cruce (asignados, cuántos son usuarios, cuántos NO estaban en el grupo de choferes, cuántos del grupo nunca aparecen asignados). Los usuarios del grupo de choferes que nunca aparecen asignados **no** se crean como chofer (si tienen correo se crean como usuario, con el rol de antes). **Análisis real (solo lectura, 2026-10-21):** *Advance Logistics (10029):* 105 IDs distintos en `AssignDL` y 76 en `AssignPK`, **todos son usuarios de la compañía**; de los 105, **50 están en el grupo 1 (staff) y no en el 4** — 22 inactivos con 3,550 órdenes y 28 activos con 2,879 (p. ej. `705`, `720`, `702`, `703`, y `001@advancelogisticspr.com` con 1,027 órdenes hasta hoy); 14 usuarios del grupo 4 nunca fueron asignados. *De La Paz (10030):* 35 en `AssignDL`, 33 en `AssignPK`; **13 del grupo 1 entregaron 263,578 órdenes** (`2006@delapaz` 58,199; `2012@delapaz` 42,721; `2008@delapaz` 36,477…), y 46 usuarios del grupo 4 nunca fueron asignados. Es decir, con la regla anterior los choferes más importantes de De La Paz no se creaban como choferes.

**Hallazgo.** Hoy la migración trae **todos los usuarios** de Colavora como choferes (activos e inactivos, D13). Luis lo corrige: **un chofer es quien aparece asignado en las órdenes**, no cualquier usuario.

**De dónde sale:** `Shipments.AssignDL` (chofer que entrega) y `Shipments.AssignPK` (chofer que recoge, pick up), ambas `INT` = ID de usuario. Entre las dos compañías hay 140 valores distintos de `AssignDL` y 109 de `AssignPK`.

**Decisión de Luis (2026-10-20):**
- Hacer un **análisis para machear** esos números de usuario con la tabla de usuarios (`colavora_courier_login`) y con los códigos de chofer que usan eventos y manifiestos (el código con y sin ceros, «003» y «3»), y **crear como choferes solo a esos**.
- Los demás usuarios (staff, administradores) no son choferes: siguen la regla de usuarios (por invitación, sin portal), no se crean como `Driver`.
- Los choferes que solo aparecen en eventos (hoy se crean inactivos como «Chofer N») salen de ese mismo cruce.

**Qué hacer antes de implementar:** consulta de solo lectura que cruce `AssignDL`/`AssignPK` (y los códigos de chofer de eventos/manifiestos) con los usuarios, y reporte con: cuántos matchean, cuántos `AssignDL`/`AssignPK` no tienen usuario, cuántos usuarios nunca fueron asignados, y duplicados por código con/sin ceros. Revisar `ImportUsersAndDriversAsync` (Masters.cs) y `ResolveDriver` (Orders.cs).

## 6. Pulso del día — «Paquetes en la calle» y «Almacén hoy» (dictado 2026-10-20)

> ✅ **HECHO (2026-10-21).** *Río «Paquetes en la calle»* (`PulseRiverService.OrdersAsync`, `RiversPanel`): cinco tarjetas en el orden dictado — **Entrada**; **En almacén** (envíos que ENTRARON al almacén por recogido o por el escaneo del almacén, cada envío una sola vez el día de su primera llegada, por el historial de estatus; barras de 7 días); **Rutas** (las que salieron ese día, por fecha de plan, con barras); **Entregado** (con las barras de los 7 días); **En ruta** (envíos en tránsito con los choferes y, abajo y más pequeño, las rutas **completadas** hoy). «Por despachar» salió del río; las existencias de ahora (en almacén y por despachar) siguen yendo a las fotos de KPI (`StockAsync`). *Almacén hoy:* la tarjeta «Productos bajo mínimo» muestra abajo «N productos en backorder» (productos con faltante pendiente en líneas abiertas de órdenes de venta; campo nuevo `backorderProducts` del pulso de almacén). *Migración:* el cierre de estatus (Entregada…) de lo que Colavora dice que está entregado y los eventos no llevaron ya se **fecha con la última actividad del envío**, no con el día de la migración (antes todo aparecía «entregado hoy» el día de la corrida). Para «En almacén» no hace falta generar escaneos: sale del historial de estatus que la migración ya escribe. *Suposición a confirmar con Luis:* en la última tarjeta, «completada» = **rutas completadas hoy** (no envíos); y «En ruta» sigue siendo envíos en tránsito. Si «completada» debía ser otra cosa (¿envíos completados/entregados?), dímelo.

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

> ✅ **HECHO en gran parte (2026-10-21).** *Lista de Envíos:* primera columna **Conduce** (con el número de orden en chico debajo solo si es distinto), columna **N.º de paquete** después de «Paquete», sin **Empaque** ni **Cotizado**; filtros **Conduce** y **N.º de paquete** (`packageNumber`, nuevo en el API) y el buscador libre también busca por número de paquete (por prefijo). *Ficha:* sin el campo Empaque. *Conduce del cliente:* `POST /orders` acepta `waybillNumber` (hasta 40 caracteres, `A-Z a-z 0-9 - _ . /`), único entre los envíos **activos** de la compañía (409 si se repite); sin él, la fórmula del Lote 110. *Migración:* el waybill de Colavora ya va como conduce, y como Luis dijo que el número es uno solo (el del cliente o el de Teikem), Orden y el número de empaque interno (oculto, ya no se muestra) siguen valiendo ese mismo número: no hace falta generar `EMP-` por cada uno de los ~480 mil envíos. *Falta:* que la **importación** (CSV/Excel y sus plantillas) y el **API de clientes** ofrezcan la columna/campo del conduce (hoy solo lo recibe `POST /orders`), y el filtro por número de paquete en las otras listas.


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

### 7.3 ¿Para qué sirve el número del lote (EMP) de Recolección y empaque? (pregunta de Luis, 2026-10-20)

- Es el nombre del **documento de trabajo** de la recolección: nace al recolectar (sale el inventario) y la orden solo nace al **empacar**. En esa etapa intermedia no hay orden, y por tanto no hay conduce: el lote necesita *algún* nombre. Además los movimientos de inventario (kardex) y la lista de Recolección y empaque lo referencian.
- Una vez empacado, el lote y la orden son 1 a 1: la orden ya lo identifica todo, y ese número solo se copia a la orden como «Empaque», que es el duplicado que Luis no quiere.
- Los despachos manuales (`DMA-#####`) no generan orden: su número es su único identificador.
- **Conclusión propuesta:** el usuario no necesita ver ni escanear el EMP. Se deja solo como identificador interno del lote (y para el despacho manual); al empacar, las pantallas, listas y movimientos muestran el **conduce** de la orden; el EMP deja de copiarse a la orden. Alternativa más grande (otro lote): crear la orden desde que se empieza a recolectar, con su conduce, y que el lote no tenga número propio.

**Decisión de Luis (2026-10-20) sobre 7.3:** el número del lote (`EMP-#####`) **se queda como está**: es el **número de la recolección**, y el **envío es otro** (su identificador es el conduce). No se toca Recolección y empaque. Lo que sigue pendiente es solo sacar el «Empaque» de la orden y de la lista de Envíos (§7, §7.2), sin cambiar el número de la recolección.

## 8. Decisiones de Luis que cerraron preguntas abiertas (2026-10-21)

- **Número del cliente repetido (§7.1):** el número (conduce) del cliente **no se puede repetir en la compañía**, pero solo cuenta lo que esté **activo**: un envío inactivo no bloquea. Si se repite entre activos, **se rechaza** (el cliente lo corrige); Teikem no le pone otro. (El índice `UX_Order_Waybill` ya es único entre las órdenes activas.)
- **Clientes por servicio (§4):** «MASCARO - PORTER (NEXT DAY)» y «(SAME DAY)» **se unen** en un solo cliente. ✅ **HECHO (2026-10-21):** `ColavoraRules.ResolveClientGroups` reconoce las variantes «(NEXT DAY)» / «(SAME DAY)»; si no existe un cliente con el nombre base, queda de principal la variante con más órdenes y se renombra al nombre base («MASCARO - PORTER»); las tarifas de las variantes se **suman** al contrato del principal (si el mismo servicio y paquete ya está, gana el del principal).
- **Facturación por división (§4):** todavía por averiguar, pero por ahora se toma así: **una sola factura por compañía**, y las **divisiones son líneas de productos** que se facturan (como sub-facturas), desglosadas por concepto; el procesamiento de las órdenes es inmaterial. No hay facturación por división ni sub-clientes de facturación.
- **Compañía nueva de transporte:** **parte de cero**, sin plantilla inicial de servicios ni paquetes.

## 9. ⚠ Pendiente destacado: `src/Teikem.Api/App_Data/` quedó en el historial de `origin/master` (2026-10-21)

Por un error mío (`git add src` incluyó la carpeta sin seguir), el commit `68c4b964` subió **5,052 archivos** de `src/Teikem.Api/App_Data/` (archivos subidos, entre ellos fotos y firmas de comprobantes de entrega). El commit siguiente los quitó del árbol, pero **siguen en el historial remoto**.
- **Decisión de Luis (2026-10-21): NO reescribir el historial por ahora**; se espera a que la otra sesión termine de trabajar. Tampoco se agrega todavía `App_Data/` a `.gitignore`.
- **Cuando se haga:** coordinar con todas las sesiones, quitar `68c4b964` del historial (push forzado de `master`), y agregar `src/Teikem.Api/App_Data/` al `.gitignore`.
- Mientras tanto: no usar `git add src` (ni directorios enteros); agregar archivos por nombre o con `git add -u`.
- **Tarifas de los clientes Recogido/Devolución (§4) — RESUELTO (2026-10-21):** no se migran. Análisis real: el cliente principal (10532) tiene **todas sus tarifas en 0.00** y ninguna orden suya trae precio; los 7 «Recogido» **no tienen ninguna orden**; de las 4 órdenes de «Devolución» **una sola** trae precio (5.75, servicio Next Day); los precios de sus tarifas (1.00 / 5.75 / 100.00) son marcadores que nunca se cobraron. El servicio de recogido (Pick up) ya existe en el catálogo de la compañía. Aparece como información en el reporte, ya no como advertencia.
- **Devoluciones (§4):** se quedan **solo con una nota** («Devolución.»): para Luis son **reclamaciones** que eventualmente salen de vuelta; no se modela nada más por ahora.
- **Divisiones fuera de las 11 (§4) — HECHO (2026-10-21):** análisis real: **GLH** (469 órdenes, de 2026-01 a hoy; es una división real que falta en la lista de Colavora), **REC** (4), **40** (2) y **AMA** (2) — las 8 viejas. Regla (`Options.StaleDivisionDays`, 90 por omisión): una división fuera de lista **usada en los últimos 90 días se agrega** a las divisiones (GLH); una **vieja es error**: sus órdenes se rechazan en el reporte («La división … no está en la lista de la compañía y es vieja…») y no se migran (REC, 40 y AMA: 8 órdenes).

## 10. TAREA: un solo control para todos los campos de fecha (dictado de Luis, 2026-10-21)

> ✅ **HECHO en la web (2026-10-21).** `DateField` / `DateTimeField` (`web-app/src/kernel/ui`) reemplazan los 31 `<input type="date">` y 8 `datetime-local`, y el `DateInput` del kit (que ya usaban unos 30 formularios). Respuestas de Luis aplicadas: la fecha y hora **abre el calendario y pide la hora aparte** (antes era el `datetime-local` nativo: un solo campo con segmentos y el formato del navegador, no el de la compañía); **se puede pegar una fecha en cualquier formato** y el control la cambia al de la compañía si la entiende o la **rechaza**; las apps móviles usarán el mismo control. *Pendiente:* las apps móviles (`app-cliente`, `app-almacen`) no tienen campos de fecha hoy; cuando los tengan, deben reutilizar las mismas reglas (`dateInput.ts` es lógica pura, sin React). Los recorridos Playwright que rellenan fechas con `fill('2026-01-01')` siguen valiendo (el control acepta un ISO que llega como evento), pero no se corrieron. La propuesta original queda abajo como referencia.

**Pedido.** Hacer **un control de fecha** y usarlo en **todos** los campos de fecha. Comportamiento:
1. **Al entrar al campo** (con el mouse, con el dedo o con el teclado) **se abre el calendario solo**, pero **se puede escribir** sin cerrarlo.
2. **Al escribir se va pintando solo el formato que la compañía eligió** (Región y formatos: `dateOrder` MDY/DMY/YMD y `dateSeparator`; hoy `kernel/format/settings.ts`). **Solo entran números**: el usuario no escribe rayas ni barras (igual que el teléfono: `PhoneInput`).
3. El valor que ve el resto de la pantalla **sigue siendo ISO `YYYY-MM-DD`** (como hoy), para no cambiar ningún formulario, API ni prueba de lógica.

**Inventario (2026-10-21, solo lectura).** 31 campos `<input type="date">` y 8 `<input type="datetime-local">` en `web-app/src` (apps de cliente y de almacén: ninguno), más el `DateInput` del kit (`kernel/ui/Form.tsx`, el que va dentro de un `<Field>`) y el rango de fechas del kit (`kernel/ui/DateRangeFilter.tsx`, que ya trae atajos «hoy», «este mes»…). Por carpeta: almacén (3: recepción, citas de muelle, conteo), impuestos (3: tasas, exenciones, simulador), cobros/facturación (2+2), pago a choferes (2), kernel/ui (2) y una cada uno en ventas, escaneo, órdenes (fecha y hora de acciones), monitor, flota (disponibilidad), despacho, campos personalizados, remesas COD, analítica, calendario de la compañía (feriados) y red de confianza. Hay pruebas que escriben fechas en esos campos (p. ej. la lista de Envíos), que habrá que adaptar.

**Cómo hacerlo (propuesta).**
- Componente `DateField` (texto con máscara + calendario emergente propio, no el nativo, porque el nativo no admite máscara ni abre solo) con las mismas props que usan hoy (`value` ISO, `min`, `max`, `id`, `aria-*`, `disabled`) y una variante para fecha y hora (`DateTimeField`, con la hora en 12 o 24 horas según `timeFormat`). `DateInput` del kit pasa a usarlo, y `DateRangeFilter` también.
- El calendario respeta `weekStartDay`, el idioma (es/en), el tema claro y oscuro, y se usa con el dedo (360 px en adelante, sin scroll horizontal); atajos «Hoy» y «Limpiar».
- Teclear: se reciben solo dígitos y se acomodan según el orden de la compañía; si la fecha no existe (31/02) no se acepta y se marca como inválida; al completar, el calendario se posiciona en esa fecha; Esc cierra, flechas mueven el día.
- Reemplazar los 39 campos (mecánico) y actualizar sus pruebas; documentar el control en `web-app/KIT.md` y en el manual (cómo se escribe una fecha).
- Preguntas para Luis antes de hacerlo: ¿la fecha y hora (`datetime-local`) también abre el calendario y pide la hora aparte? ¿Se permite pegar una fecha completa (p. ej. `2026-10-21` o `10/21/2026`) aunque no sea el formato de la compañía? ¿Las apps móviles (cliente y almacén) usan el mismo control cuando tengan campos de fecha?

## 11. Entrada rápida de envíos: fecha primero, Enter guarda, valores del cliente, fechas pasadas bloqueadas (dictado de Luis, 2026-10-21)

> ✅ **HECHO (2026-10-21).** *Pantalla (`OrderQuickEntry`):* la fecha es el primer campo (el `DateField`, por omisión hoy); al elegir el cliente el cursor va a la fecha y su calendario se abre; Tab sigue con el N.º de factura (apagado en «(automático)» si Teikem la genera), el consignatario…; Enter guarda; la fila nueva trae **la fecha de la última orden guardada** y el cursor vuelve a la fecha; las filas con fecha distinta de hoy se **sombrean** (marca amarilla a la izquierda, sin cambiar el alto) y la leyenda de la barra lo explica. *Servicio y paquete por cliente:* nuevas columnas `Client.DefaultServiceTypeLookupId` / `DefaultPackageTypeLookupId` (sección nueva de `update.sql`), campos `defaultServiceType` / `defaultPackageType` en el perfil del cliente (API y pantalla); la entrada rápida los usa y, si no hay, los de la compañía. *Regla:* un envío **no se crea con fecha anterior a hoy** (servidor: `OrderService.CreateAsync`, 400 `La fecha del envío no puede ser anterior a hoy.`; vale también para la importación y la API; pantallas rápida y detallada: se avisa y se bloquea, y el calendario no deja elegir días pasados). *Pendiente / a confirmar con Luis:* dijiste «un tipo por defecto, un tipo de paquete por defecto y un tipo de servicio por defecto»: implementé el **servicio** y el **paquete**; si el «tipo» es otra cosa (¿tipo de COD? ¿tipo de envío?), dime cuál. Los recorridos Playwright de la entrada rápida (`loteFC3`) no se corrieron.

## 12. Label de factura impreso solo (dictado de Luis, 2026-10-21)

> ✅ **HECHO (2026-10-21).** Cliente con «Label de factura» (`autoPrintInvoice`): al guardar un envío con número de factura, después de los labels de los paquetes, sale solo —sin vista previa ni pregunta, aunque la estación esté en «confirmar»— un label de factura por la impresora de labels. Los labels de los paquetes también salen solos (ya no preguntan). El diseño tiene su propio constructor («Construir el label de factura» en Sistema → Impresoras y labels): plantillas de clase `INVOICE` (columna nueva `LabelTemplate.Kind`, sección nueva de `logistica-db-update.sql`), por omisión código de barras del número de factura centrado y grande con el número pegado debajo. Detalle en `docs/manual/12-impresoras-y-labels.md` §6. *Falta confirmar con Luis:* si el label de factura debe salir una vez por envío (como está) o una vez por factura distinta cuando varias órdenes comparten factura.
