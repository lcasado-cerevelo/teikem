# Lote 178 — Recolectar juntos en la app de almacén (C3/C4, app; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Cola de salida:** nuevo tipo `pickGroup` (POST `/api/v1/pick-batches/groups`) con su etiqueta en Sincronización. Funciona sin señal: refleja de inmediato en cada aviso y en los saldos locales (`queueExitGroup`), y si el servidor lo rechaza los saldos se deshacen solos (igual que el despacho de un aviso); al volver a bajar los avisos se **reaplican las maestras pendientes** (`reapplyPendingExitDispatches`).
- **Pantalla Despachar aviso de salida:** botón «Recolectar juntos» (con 2 o más avisos), selección con casillas (☐/☑), los de otro cliente se atenúan y avisan; una ruta guiada única con las tareas de todos los avisos elegidos (las claves de tarea empiezan por el id de línea, único, así que conviven) y envío de **un solo** `POST` con las líneas de cada aviso (posición y lote). Hace falta haber recogido algo de al menos dos avisos.
- Lógica pura en `exitGroupLogic.ts` (bloqueo por cliente, cuerpo de la solicitud, número de la respuesta).

## Cómo se probó
- jest: `exitGroupLogic` (3), `localExit` (+3: tareas de varios avisos y envío único con efecto local, menos de dos avisos no encola, reaplicar la maestra pendiente tras bajar los avisos), pantalla `exitGroupScreen` (flujo completo hasta «Recolección maestra EMP-00020 enviada» con el cuerpo exacto y el saldo bajado; rechazo del servidor con mensaje exacto y saldo restituido), `outboxKind`, i18n, y la pantalla de órdenes de venta (sin regresiones); `tsc`.

## Decisiones a revisar
1. **Solo avisos de salida** en la app (las órdenes de venta llevan kits y entrega en el aparato; se juntan desde la web). Ampliable.
2. En la app la maestra **recoge**; el **envío se crea al empacar en la web** (la app no tiene el formulario de empaque de la maestra).
3. La ruta mezcla las tareas en el orden en que se eligieron los avisos (no se reordena por posición).
4. Un aviso del grupo del que no se recogió nada se deja fuera del envío; con menos de dos avisos con algo recogido no se puede enviar.

## No se probó
- El envío de la maestra contra el API real desde un aparato (heredado del Lote 176: el empaque de la maestra tampoco se ejercitó de punta a punta).

## Qué quedó fuera
- Órdenes de venta en «Recolectar juntos» desde el aparato; empacar la maestra desde el aparato; mostrar la maestra en la lista del aparato.
