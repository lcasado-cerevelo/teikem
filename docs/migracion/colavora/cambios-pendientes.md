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
2. **La división pasa a ser un catálogo** (por cliente), con las divisiones definidas en Colavora, y no un texto suelto. La migración las crea y asigna a cada orden.
3. **Contratos y tarifas** se rehacen sobre esa base: un contrato por cliente real; las tarifas de los clientes «Recogido/Devolución» pasan al contrato del cliente como tarifas de pick up y de devolución (en vez de contratos propios de cada uno).
4. **Pregunta abierta (Luis todavía no sabe):** ¿se factura una factura por división o todo junto? Eso decide si la división es solo etiqueta o parte de la facturación (agrupar la factura por división / sub-cliente de facturación). No se implementa la facturación por división hasta tener la respuesta.

**Qué cambia en la migración (sin hacerlo todavía):** `ImportClientsAsync` y lo que cuelga de ella (contactos, ubicaciones, contrato, tarifas, `Customers_Service_Price` por `SenderID`), la resolución del cliente de cada orden en `ImportOrderBatchAsync` (tipo pick up / devolución por el nombre del cliente de origen) y el campo `division` (catálogo en vez de texto). Regla pura y probada en `ColavoraRules` (nombre → cliente principal + tipo).

## 5. Choferes: solo los que aparecen asignados en las órdenes

**Hallazgo.** Hoy la migración trae **todos los usuarios** de Colavora como choferes (activos e inactivos, D13). Luis lo corrige: **un chofer es quien aparece asignado en las órdenes**, no cualquier usuario.

**De dónde sale:** `Shipments.AssignDL` (chofer que entrega) y `Shipments.AssignPK` (chofer que recoge, pick up), ambas `INT` = ID de usuario. Entre las dos compañías hay 140 valores distintos de `AssignDL` y 109 de `AssignPK`.

**Decisión de Luis (2026-10-20):**
- Hacer un **análisis para machear** esos números de usuario con la tabla de usuarios (`colavora_courier_login`) y con los códigos de chofer que usan eventos y manifiestos (el código con y sin ceros, «003» y «3»), y **crear como choferes solo a esos**.
- Los demás usuarios (staff, administradores) no son choferes: siguen la regla de usuarios (por invitación, sin portal), no se crean como `Driver`.
- Los choferes que solo aparecen en eventos (hoy se crean inactivos como «Chofer N») salen de ese mismo cruce.

**Qué hacer antes de implementar:** consulta de solo lectura que cruce `AssignDL`/`AssignPK` (y los códigos de chofer de eventos/manifiestos) con los usuarios, y reporte con: cuántos matchean, cuántos `AssignDL`/`AssignPK` no tienen usuario, cuántos usuarios nunca fueron asignados, y duplicados por código con/sin ceros. Revisar `ImportUsersAndDriversAsync` (Masters.cs) y `ResolveDriver` (Orders.cs).
