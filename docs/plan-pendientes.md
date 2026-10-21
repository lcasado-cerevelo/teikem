# Plan y pendientes (actualizado 2026-10-21)

Documento vivo: cada vez que se cierre o se decida algo se actualiza aquí. Estado: ☐ pendiente · ◐ en curso · ☑ hecho · ⏸ por decidir.

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
- ⏸ **C2. Recibo con tandas dentro.** Un solo recibo por aviso que se **confirma por partes** (cada confirmación mueve lo capturado a la posición de recepción y saca sus tareas de acomodo; el recibo sigue abierto); cada línea muestra recibido / en staging / acomodado; la app avisa cuando el staging (capacidad máxima de la posición) se llena. Estructura nueva por secciones del script de actualización (tabla de tandas); recibos existentes = una tanda. Decidir después de C1.
- ⏸ **C3. Despacho igual:** una orden con un despacho de varios movimientos. Cambia el modelo de recolecciones; solo si C1 no basta.
- ⏸ **Decisión abierta:** «Recibido parcial» ¿avisa al cliente en el portal? (el aviso de llegada ya publica eventos).

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
☑ Lote 154 pantallas del portal · ☑ 155 precio del producto en aviso/orden y aviso «sin costo» · ☑ 156 el cliente crea desde su portal · ☑ 157 avisos de llegada (eventos, edición, vencimiento del lote) · ☑ 158 error al guardar aviso de salida, disponible en selector, edición del cliente · ☑ 159 pulido de pantallas · ☑ 160 acomodo sin cancelar y despacho de un clic.

## Lecciones (para no repetir)
- Probar los servicios que usan contadores/secuencias contra el servicio **real** y SQL Server (el aviso de salida falló en producción porque las pruebas usaban un contador de mentira). Las pruebas con base real corren con `TEIKEM_TEST_SQLSERVER` (ver `scripts/dev-sqlserver.sh`).
- Comitear y subir al terminar cada lote; avisar antes de seguir cuando el dueño lo pida.
