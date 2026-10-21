# Lote 172 — «Mis tareas» en la app de almacén (B1; pedido de Luis, 2026-10-21)

## Qué se construyó
- **App de almacén:** tarjeta «Mis tareas» en Inicio (con la cuenta de documentos asignados) y pantalla `my-tasks`: avisos de llegada por recibir, avisos de salida por despachar y acomodos asignados al usuario; lo que nadie tiene asignado se despliega aparte con **Tomar**. Tocar un aviso abre Recibir (`?doc=LLE-#####`) o Despacho de avisos de salida (`?notice=SAL-#####`) como si se hubiera escaneado.
- **Sin señal:** los avisos salen de la copia local (migración local v19: `assigned_to_user_id` en `asn` y `exit_notice`, y la marca de agua de avisos se reinicia para bajarlos completos una vez); los acomodos asignados se piden en línea (`assignedToMe`) y quedan guardados para la próxima vez sin señal.
- **Servidor:** nada nuevo (Lote 171); la sincronización ya lleva `assignedToUserId`.

## Cómo se probó
- jest: `myTasksLogic.test.ts` (6: mis avisos, solo de cliente, los sin asignar, solo con algo reservado, la cuenta, tomar en la copia local, código para abrir), `myTasksScreen.test.tsx` (4: Inicio con la cuenta y la lista con el acomodo en línea, tocar abre Recibir con el aviso, Tomar llama a `/asns/2/take` y lo deja a mi nombre, vacío), `homeLayout`, `download` (guarda `assigned_to_user_id`) y `database`/`localLookup`; `tsc --noEmit` limpio.

## Decisiones a revisar
1. **La asignación ordena, no bloquea:** Recibir y Despacho siguen listando todo. Si prefieres que un documento asignado a otra persona salga atenuado o no se pueda abrir, es un cambio chico.
2. **Los acomodos asignados se ven en línea** (con copia de la última vez): no se bajan en la sincronización porque Acomodar ya es una pantalla en línea.
3. Un aviso de salida sin nada reservado no baja al aparato (regla del Lote 146), así que no sale en «Mis tareas».
4. `Tomar` necesita señal (el servidor decide quién lo tomó primero).

## Qué quedó fuera
- Notificar a la persona cuando le asignan algo (push / aviso): el motor de avisos es por reglas; se arma en su propio lote.
- Recolecciones de órdenes de venta y recibos contra orden de compra como tareas asignables.
