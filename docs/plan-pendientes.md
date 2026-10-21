# Plan y pendientes (actualizado 2026-10-21)

Documento vivo: cada vez que se cierre o se decida algo se actualiza aquí. Estado: ☐ pendiente · ◐ en curso · ☑ hecho.

## Orden de ejecución acordado (2026-10-21) — todavía no se empieza (hay alguien más trabajando)
1. **I1** — lista «Avisos que puedo silenciar» sin texto.
2. **G1** — avisos de flota: documentos por vencer y mantenimiento.
3. **F** — visibilidad de indicadores y gráficos por compañía (incluidos los «Por defecto»).
4. **A5** — número del aviso de llegada (`Asn.Number`, patrón `LLE-#####`).
5. **B** — «Mis tareas» en la app y asignar recibos y recolecciones.
6. **C1** — agrupar por documento; luego C2, C3 y C4.
7. **H** (contabilización por dueño, por confirmar) y los **menores** (sección D).

## Principios acordados (2026-10-21)
- **La app móvil es la herramienta principal de trabajo del almacén:** fácil de entrar y de maniobrar (user friendly) por encima de la web; pocas pantallas, botones grandes, un solo paso para lo común, funciona sin señal.
- **Tareas sin asignar** (acomodo, recibo, recolección): las puede tomar **cualquiera**; no se bloquea nada. **Una tarea asignada** le aparece a quien se le asignó (y sigue visible, atenuada, para los demás con permiso de supervisión).
- **Asignación de tareas** aplica a: recibos, acomodo y despachos (recolección). Asignar solo vale si «Mis tareas» existe en la app (B1).
- **Un documento, varias ejecuciones:** el recibo y el despacho son un solo documento con tandas/movimientos adentro (ver C).
- Cambios de estructura solo como secciones nuevas del script de actualización; lo existente sigue válido.

## A. App móvil (`app-almacen`) — urgente, se están quejando
- ☑ **A1. El recibo exige documento en la app.** El servidor manda `receiptRequirement` en el registro y el latido; la app lo guarda y en Recibo oculta el ciego y solo ofrece lo permitido. Bajar `receiptRequirement` en la sincronización (como `dispatchRequirement`); al entrar a Recibo, selector «Aviso de llegada / Orden de compra» (solo lo que exija la compañía); el ciego desaparece o se bloquea. El servidor ya valida (Lote 149).
- ☑ **A2. Listado de avisos y órdenes pendientes** en la app tras escoger el tipo, con buscador (igual que las posiciones).
- ☑ **A3. Búsqueda parcial** de producto (SKU, nombre, código de barras) y de posición sobre la base local del aparato (hoy es exacta: `barcode = ? OR sku = ?`, `code = ?`). Lista que carga de 30 en 30 al deslizar, sin tope y sin señal; «no hay coincidencias» en vez de no hacer nada. (Hecho: la lista sin tope de 50 y las sugerencias al escribir a mano bajo el campo, con «Ver todas».)
- ☑ **A4. Quitar el tope de 50** de las consultas en línea (`lookupApi`: `take: 50`) → «Cargar más».

- ☑ **A5 (Lote 170). Número para los avisos de llegada** (hoy solo tienen un id interno `#12` y la referencia del cliente). Contador propio por compañía (patrón p. ej. `LLE-#####`, como `SAL-#####` del aviso de salida): columna `Asn.Number` + contador `ASN` (restricción `CK_NumberSequence_Kind` y `NumberingRules.IsKnownKind`), número a los avisos existentes (en orden de creación), y mostrarlo en la web, el portal, la app móvil (selector de documentos y buscador) y los avisos de notificación. Ojo con la lección: probar contra el contador real y SQL Server.

## B. Tareas y asignación (decisión del 2026-10-21: la asignación solo vale si la app la usa)
- ☑ **B1 (Lote 172). «Mis tareas» en la app** (acomodo, recibo, recolección): lo asignado al usuario, sin señal, aviso al asignar, tomar una tarea sin asignar. Hoy solo ordena el acomodo con lo asignado primero.
- ☑ **B2 (Lote 171; se asignan el aviso de llegada y el de salida, no el recibo ni la recolección: nacen al ejecutarse). Asignar el recibo y la recolección** (campo «Asignado a», como las tareas) y «Asignar todas» a nivel del documento.

## C. Un documento, varias ejecuciones (modelo: documento ≠ trabajo ≠ ejecutado)
- ☑ **C1 (Lote 173; la lista de recibos por aviso llega con C2, hoy es 1:1). Agrupar en pantalla sin tocar datos:** «Recolección y empaque» una fila por orden o aviso con su avance (pedido, reservado, sacado) y las recolecciones como movimientos al abrirla; la lista de recibos igual por aviso.
- ☑ **C2. Recibo con tandas dentro (Lotes 174 y 175).** Servidor + web (174) y app: «Recibir una parte» con aviso de staging lleno (175). Pendiente menor: ver lo ya recibido del documento en el aparato, series/daño por partes, «Recibido parcial» al cliente. Un solo recibo por aviso que se **confirma por partes** (cada confirmación mueve lo capturado a la posición de recepción y saca sus tareas de acomodo; el recibo sigue abierto); cada línea muestra recibido / en staging / acomodado; la app avisa cuando el staging (capacidad máxima de la posición) se llena. Estructura nueva por secciones del script de actualización (tabla de tandas); recibos existentes = una tanda.
- ◐ **C3 + C4 (despacho), Lotes 176 (servidor), 177 (web) y 178 (app, solo avisos de salida) hechos; el empaque real de la maestra quedó verificado con `scripts/smoke-maestra.sh` (autónomo).** Decidido por el dueño (2026-10-21): la **recolección maestra** reúne varios documentos (órdenes de venta y/o avisos de salida) **del mismo cliente y almacén**, se arma **al recolectar** (no se suman después), tiene **un solo destino** (otro destino = otra recolección) y se le crea **un envío** al empacar; se ven todos sus movimientos (posiciones). Hecho: tabla `PickGroup`, endpoints `/pick-batches/groups`. Web hecha (vista «Maestras», «Recolectar juntos», ficha con movimientos, empacar, eliminar). App hecha (Lote 178: «Recolectar juntos» con avisos de salida; órdenes de venta y empacar desde el aparato quedan fuera). Falta: marcar la maestra en «Por documento», (el empaque real y la eliminación empacada ya se verificaron: `scripts/smoke-maestra.sh`).
- ☑ **C4 (recibo con varios documentos), Lotes 182-183, autónomo (ver `docs/lote182-decisiones.md`):** recepción maestra RCM = envoltorio sobre los recibos de siempre (un dueño, un almacén, se arma al abrir; se confirman juntos). Falta: la app de almacén, elegir órdenes de compra en la pantalla y repartir una captura única entre los avisos. **Texto original:**: un recibo que apunte a varios avisos u órdenes de compra. Sin pedir todavía: lo del despacho quedó resuelto arriba; el recibo se decide aparte.

## F. Análisis: quién ve cada indicador y gráfico (decisión del 2026-10-21)
**Lo que pide el dueño:** los indicadores y gráficos son **por compañía**. Se pueden crear nuevos con el permiso apropiado (`analytics.manage`; lo que crea el admin de la compañía queda para esa compañía). Y **la visibilidad se decide para todos, incluidos los «Por defecto»**: privado, toda la compañía, o compartido con personas y/o roles. «Por defecto» solo quiere decir **que el sistema lo creó**, no que sea intocable para la compañía.

**Hallazgos del código (para no repetir el análisis):**
- Ya son por compañía: `IndicatorDefinition`/`ChartDefinition` tienen `TenantId`; los «Por defecto» (`IsSystem`) son copias sembradas en cada compañía.
- Ya existen visibilidad (`ReportVisibility`: PRIVATE / TENANT / SHARED), lista de personas y de roles (`IndicatorShare`/`ChartShare`) y su editor en «Nuevo indicador/gráfico» — pero la regla del servidor (`IsVisible`) devuelve **verdadero siempre** si `IsSystem`, y la edición de un «Por defecto» está bloqueada.
- «Mostrar en Pulso del día» y el rango ya son **preferencia por usuario** (el valor de la definición es solo el predeterminado); la nota de la maqueta que decía «compartido» ya no aplica.
- Aparte de la visibilidad hay una regla de datos: para ver un indicador el usuario necesita permiso de **leer la fuente** y el módulo encendido; eso se mantiene.

**Diseño:**
- ☑ **F1 (Lote 169).** Quitar el atajo de `IsSystem` en la regla de visibilidad: los «Por defecto» nacen **TENANT** (todos, como hoy) pero respetan lo que se les ponga.
- ☑ **F2 (Lote 169, solo visibilidad; endpoint nuevo).** Quien tenga `analytics.manage` puede cambiar de un «Por defecto» **solo** la visibilidad (privado / compañía / compartido con personas y roles), el rango por omisión y si aparece en Pulso por omisión; **no** el nombre, la fuente, el campo ni los filtros (son las métricas oficiales). Quien lo desee, lo **duplica** para cambiarlo (la copia es de la compañía y editable).
- ☑ **F3 (Lote 169; Indicadores y Gráficos; falta Vistas).** Pantalla: en cada tarjeta, «Quién lo ve» (editor de visibilidad, el mismo de los creados por usuarios) también para los «Por defecto»; marca «Por defecto» = «lo creó el sistema».
- ☑ **F4 (Lote 169; sin cambios).** Permisos: crear, editar y compartir exigen `analytics.manage`; ver, `analytics.view`. Revisar que el admin de la compañía los tenga y que la plantilla de roles no se los dé a todos.
- ☑ Pruebas (Lote 169: lista, lectura y evaluación; Pulso usa la misma regla; la auditoría sale del interceptor, sin prueba propia): un rol sin acceso no ve el «Por defecto» restringido ni en Indicadores, ni en Gráficos, ni en Pulso; el administrador de plataforma sigue viéndolo todo; un cambio queda auditado.
- Lo mismo aplica a **Vistas e informes** (`ReportDefinition`).

## G. Avisos de flota (idea del dueño, 2026-10-21; recomendación: empezar corto)
- ☑ **G1 (Lote 168).** Con el mismo mecanismo que los avisos de inventario (trabajo diario + un aviso por conjunto + no se repite en la semana): **documentos por vencer / vencidos** (seguro, marbete, licencia, de vehículos y choferes; días de anticipación ajustables) y **mantenimiento próximo o vencido** (por fecha o millaje).
- ☑ **G2 (Lote 179, autónomo):** orden de trabajo abierta demasiado tiempo, combustible fuera de rango, vehículo o chofer sin viaje N días. Umbrales fijos (14 días, 30 %); ajustarlos por compañía queda pendiente.

## H. Contabilización de compras y despachos (aún sin construir en el servidor)
- ☑ **H1 (Lote 181, autónomo; la regla sigue por confirmar con el dueño; ver `docs/lote181-decisiones.md`).** Regla acordada por el análisis del 2026-10-21 (**por confirmar con el dueño**): se contabiliza por **dueño del inventario** (lo propio sí, lo de un cliente no), sin importar el tipo de documento (orden de compra o aviso de llegada), con el **costo congelado de cada línea** (capas, Lotes 147-148). Corridas Generada → Aceptada → Exportada con plantilla de exportación.

## I. Defectos conocidos por arreglar
- ☑ **I1.** «Avisos que puedo silenciar» (campana → lista) sale con los interruptores **sin texto**; sospecha de color tras el cambio de marca o nombre vacío del servidor; por revisar.

## D. Pendientes menores
- ☑ Líneas existentes de una orden de compra: aviso de diferencia de precio al editar (Lote 180).
- ☑ `scripts/smoke.sh` revisado: **está obsoleto**, no es un defecto reciente. Falla en `INBOUND` porque el script de actualización (2026-10, ~línea 4105) renombró los estatus de las órdenes (DRAFT→SUBMITTED, INBOUND→UNASSIGNED, PLANNED→ASSIGNED; se borraron CONFIRMED, ARRIVED, ON_HOLD, PARTIAL) y el smoke (5 459 líneas) sigue el modelo viejo en cientos de pasos. Arreglarlo es reescribirlo. Se dejó como está y se agregó `scripts/smoke-maestra.sh` (flujo de la recolección maestra) al CI.
- ☑ «Mensajes de clientes» sin ítem de menú: **decidido por Luis el 2026-10-20, sin menú** (la pantalla sigue en `/portal/messages`).
- ☑ Ocultar opciones de recibo a ciegas en la web cuando la compañía exige documento (Lote 180).
- ☑ **Encabezado de las tarjetas y paneles (decidido por el dueño, 2026-10-21; hecho, autónomo):** fondo = el tercer color de la marca (el gris `--money`) al 8 % mezclado con el fondo del panel; lo atenuado con 25 % del texto; contraste AA verificado en los 14 temas y los dos modos; recorrido Playwright con capturas.
- ☐ Endurecer los endpoints con los permisos de acceso por ítem de menú. **No se hizo (autónomo): exigirlos en el servidor puede dejar sin acceso a roles y aparatos que hoy leen con el permiso de datos; ver el informe. Decide Luis.**
- ☑ Prueba de `db-update` contra SQL Server (simulación y real; se corrigió el orden update.sql → diff).
- ◐ Capturas / Playwright de las pantallas nuevas: hechos los de la recolección maestra y el encabezado (`e2e/lote180-encabezado-maestra.spec.ts`, capturas `l180-*`); faltan los del portal del cliente, avisos y filtros.
- ☑ EXIT_NOTICE como fuente de datos para Vistas (menores, autónomo).
- ☑ Saldos y Conciliación: filtro de zona (menores, autónomo).
- ◐ El portal: editar productos ya creados ☑ (menores, autónomo); adjuntar documentos ☐ (lote propio: no hay almacenamiento de archivos; ver informe).

## E. Hecho recientemente (referencia)
☑ Espacio entre el buscador y la lista de recibos (web) · ☑ Lote 154 pantallas del portal · ☑ 155 precio del producto en aviso/orden y aviso «sin costo» · ☑ 156 el cliente crea desde su portal · ☑ 157 avisos de llegada (eventos, edición, vencimiento del lote) · ☑ 158 error al guardar aviso de salida, disponible en selector, edición del cliente · ☑ 159 pulido de pantallas · ☑ 160 acomodo sin cancelar y despacho de un clic.

## Lecciones (para no repetir)
- Probar los servicios que usan contadores/secuencias contra el servicio **real** y SQL Server (el aviso de salida falló en producción porque las pruebas usaban un contador de mentira). Las pruebas con base real corren con `TEIKEM_TEST_SQLSERVER` (ver `scripts/dev-sqlserver.sh`).
- Comitear y subir al terminar cada lote; avisar antes de seguir cuando el dueño lo pida.

---
Quedan pendientes los menores de siempre (mensajes de clientes sin ítem de menú, opciones de recibo a ciegas cuando se exige documento, endurecer endpoints con permisos de menú) y revisar el smoke.sh.
