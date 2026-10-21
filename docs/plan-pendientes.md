# Plan y pendientes (actualizado 2026-10-21)

Documento vivo: cada vez que se cierre o se decida algo se actualiza aquí. Estado: ☐ pendiente · ◐ en curso · ☑ hecho.

## Principios acordados (2026-10-21)
- **La app móvil es la herramienta principal de trabajo del almacén:** fácil de entrar y de maniobrar (user friendly) por encima de la web; pocas pantallas, botones grandes, un solo paso para lo común, funciona sin señal.
- **Tareas sin asignar** (acomodo, recibo, recolección): las puede tomar **cualquiera**; no se bloquea nada. **Una tarea asignada** le aparece a quien se le asignó (y sigue visible, atenuada, para los demás con permiso de supervisión).
- **Asignación de tareas** aplica a: recibos, acomodo y despachos (recolección). Asignar solo vale si «Mis tareas» existe en la app (B1).
- **Un documento, varias ejecuciones:** el recibo y el despacho son un solo documento con tandas/movimientos adentro (ver C).
- Cambios de estructura solo como secciones nuevas del script de actualización; lo existente sigue válido.

## A. App móvil (`app-almacen`) — urgente, se están quejando
- ☐ **A1. El recibo exige documento en la app.** Bajar `receiptRequirement` en la sincronización (como `dispatchRequirement`); al entrar a Recibo, selector «Aviso de llegada / Orden de compra» (solo lo que exija la compañía); el ciego desaparece o se bloquea. El servidor ya valida (Lote 149).
- ☐ **A2. Listado de avisos y órdenes pendientes** en la app tras escoger el tipo, con buscador (igual que las posiciones).
- ☐ **A3. Búsqueda parcial** de producto (SKU, nombre, código de barras) y de posición sobre la base local del aparato (hoy es exacta: `barcode = ? OR sku = ?`, `code = ?`). Lista que carga de 30 en 30 al deslizar, sin tope y sin señal; «no hay coincidencias» en vez de no hacer nada.
- ☐ **A4. Quitar el tope de 50** de las consultas en línea (`lookupApi`: `take: 50`) → «Cargar más».

## B. Tareas y asignación (decisión del 2026-10-21: la asignación solo vale si la app la usa)
- ☐ **B1. «Mis tareas» en la app** (acomodo, recibo, recolección): lo asignado al usuario, sin señal, aviso al asignar, tomar una tarea sin asignar. Hoy solo ordena el acomodo con lo asignado primero.
- ☐ **B2. Asignar el recibo y la recolección** (campo «Asignado a», como las tareas) y «Asignar todas» a nivel del documento.

## C. Un documento, varias ejecuciones (modelo: documento ≠ trabajo ≠ ejecutado)
- ☐ **C1. Agrupar en pantalla sin tocar datos:** «Recolección y empaque» una fila por orden o aviso con su avance (pedido, reservado, sacado) y las recolecciones como movimientos al abrirla; la lista de recibos igual por aviso.
- ☐ **C2. Recibo con tandas dentro (acordado).** Un solo recibo por aviso que se **confirma por partes** (cada confirmación mueve lo capturado a la posición de recepción y saca sus tareas de acomodo; el recibo sigue abierto); cada línea muestra recibido / en staging / acomodado; la app avisa cuando el staging (capacidad máxima de la posición) se llena. Estructura nueva por secciones del script de actualización (tabla de tandas); recibos existentes = una tanda.
- ☐ **C3. Despacho igual (acordado):** una orden con un despacho de varios movimientos (de aquí, de allá…). Cambia el modelo de recolecciones; se hace después de C2.
- ☐ **C4. Recibo y despacho con varios documentos (acordado).** Hoy un recibo apunta a UN aviso u orden de compra (y un aviso solo tiene un recibo activo) y una recolección a UNA orden de venta o UN aviso de salida (no se mezclan). Permitir varios (cada línea ya guarda de qué línea de documento viene; la recolección por olas es una recolección para varias órdenes).
- **Detalle menor por confirmar al llegar a C2 (no bloquea nada):** si «Recibido parcial» avisa al cliente en el portal (el aviso de llegada ya publica eventos).

## D. Pendientes menores
- ☐ Líneas existentes de una orden de compra: mostrar el aviso de diferencia de precio al editar.
- ☐ Revisar `scripts/smoke.sh`: falla antes de llegar a lo nuevo, en el estatus de órdenes `INBOUND` (no se sabe si ya fallaba).
- ☐ «Mensajes de clientes» sin ítem de menú (decidir dónde).
- ☐ Ocultar opciones de recibo a ciegas en la web cuando la compañía exige documento.
- ☐ Gris claro de las superficies del modo claro.
- ☐ Color del encabezado de las tarjetas: opinión dada (no el naranja; sutil: fondo gris de superficie + borde/sombra fina; azul marino solo en lo que flota sobre la pantalla). Falta que el dueño decida y se aplique.
- ☐ Endurecer los endpoints con los permisos de acceso por ítem de menú.
- ☐ Prueba de `db-update` en simulación contra SQL Server.
- ☐ Capturas / Playwright de las pantallas nuevas (portal del cliente, avisos, filtros).
- ☐ EXIT_NOTICE como fuente de datos para Vistas.
- ☐ Saldos y Conciliación: aplicar también el filtro de zona (hoy solo Kárdex).
- ☐ El portal: adjuntar documentos y editar productos ya creados.

## E. Hecho recientemente (referencia)
☑ Espacio entre el buscador y la lista de recibos (web) · ☑ Lote 154 pantallas del portal · ☑ 155 precio del producto en aviso/orden y aviso «sin costo» · ☑ 156 el cliente crea desde su portal · ☑ 157 avisos de llegada (eventos, edición, vencimiento del lote) · ☑ 158 error al guardar aviso de salida, disponible en selector, edición del cliente · ☑ 159 pulido de pantallas · ☑ 160 acomodo sin cancelar y despacho de un clic.

## Lecciones (para no repetir)
- Probar los servicios que usan contadores/secuencias contra el servicio **real** y SQL Server (el aviso de salida falló en producción porque las pruebas usaban un contador de mentira). Las pruebas con base real corren con `TEIKEM_TEST_SQLSERVER` (ver `scripts/dev-sqlserver.sh`).
- Comitear y subir al terminar cada lote; avisar antes de seguir cuando el dueño lo pida.

---
Quedan pendientes los menores de siempre (mensajes de clientes sin ítem de menú, opciones de recibo a ciegas cuando se exige documento, el gris claro del modo claro, endurecer endpoints con permisos de menú) y revisar el smoke.sh.
