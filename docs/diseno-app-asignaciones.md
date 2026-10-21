# Diseño — Recibir, Acomodar y Despachar en la app según lo asignado (propuesta, 2026-10-21)

Estado: **propuesta para aprobación de Luis. No se ha construido nada de lo que dice «Qué cambia».**

## 1. Idea central
En todo el aparato la pantalla de un trabajo se lee igual: **arriba el número (escanear o escribir) → lo asignado a mí → «Ver lo que nadie tiene asignado» → (con permiso) «Ver lo asignado a otras personas»**. Tomar algo sin dueño lo asigna a quien lo toma, y la bitácora dice por qué.

## 2. Recibir
1. Al tocar **Recibir** solo aparece arriba el **número del aviso/orden**: campo para escanear o escribir, botón del teclado y **Aceptar**. Los botones «Con acomodo / Directo a posición» **no** aparecen aquí.
2. Debajo, la lista de **avisos de llegada y órdenes de compra asignados a mí** (con las pestañas Avisos / Órdenes).
3. Debajo, **«Ver lo que nadie tiene asignado (N)»**: despliega lo que no tiene dueño. En esa lista se escoge **uno o varios** («Seleccionar varios»).
4. Con permiso «ver todo» aparece además **«Ver lo asignado a otras personas (N)»**, con el nombre de a quién está asignado.
5. Al escanear/escoger uno o varios, la **misma pantalla** pasa al paso de modo: aparecen **Con acomodo / Directo a posición**; al escoger uno, esos botones desaparecen y salen ahí mismo las opciones de acomodo, o la pantalla de directo a posición. Sin pantalla intermedia.
6. «Recibo ciego» queda al final, si la compañía lo permite.
7. Varios documentos del mismo dueño y almacén → carga automática y recepción maestra; de dueños distintos → recibos separados (como hoy).
8. Empezar a recibir algo sin dueño lo asigna a quien recibe (`$reason` en la bitácora).
9. Si el almacén tiene un modo fijo (Con acomodo o Directo), **se salta la pregunta** y va directo (propuesta; ver decisión D1).

## 3. Acomodar
Tras recibir en acomodo salen **todos los acomodos**: primero **los asignados a mí**, luego **«Ver lo que nadie tiene asignado»**, y con permiso «ver todo» **«Ver lo asignado a otras personas»**. Iniciar uno sin dueño lo asigna (ya ocurre en el servidor; se agrega el porqué, ya hecho).

## 4. Despacho
Igual: asignados a mí → «Ver lo que nadie tiene asignado» → (permiso) «Ver lo asignado a otras personas». Avisos de salida y órdenes de venta en una sola lista; «Seleccionar varios» arma la recolección maestra.

## 5. Mis tareas
Orden Recibir → Acomodar → Despachar. Lo mío arriba; «Ver lo que nadie tiene asignado»; con permiso «ver todo», también lo de otras personas.

## 6. Permisos nuevos
| Código | Nombre | Qué hace |
|---|---|---|
| `warehouse.app.viewall` | Ver todo en el aparato | Muestra, además de lo mío y lo sin asignar, **lo asignado a otras personas** en Recibir, Acomodar, Despacho y Mis tareas. |
| `warehouse.app.reassign` | Reasignar desde el aparato | Botón **Reasignar** en cada documento/tarea de esas listas: elegir a una persona (o dejarlo sin asignar). **Exige `warehouse.app.viewall`**: sin él, el botón no se ofrece (la app lo comprueba y el servidor también). |

- Se siembran en `PermissionCatalog` (categoría Almacén) y en el seed espejado vía una sección nueva de `Diseño/logistica-db-update.sql` (el SQL de estructura y seed está congelado), **propagados a los roles ya clonados** (regla del proyecto: solo agregar).
- Se dan al rol **Supervisor de almacén** (plantilla y roles ya clonados). El operador no los recibe.
- **Servidor:** los endpoints `assign` (aviso de llegada, aviso de salida, orden de compra, orden de venta, tarea) hoy exigen `warehouse.manage`; pasan a `warehouse.manage|warehouse.app.reassign`, y si se entra por `reassign` el servicio comprueba además que tenga `warehouse.app.viewall`. La persona asignada sigue debiendo ser miembro activo de la compañía.
- **Lista de personas asignables:** endpoint ligero `GET /api/v1/warehouse/assignees` (miembros activos, nombre) con `warehouse.app.reassign` — no se reutiliza el de administración de usuarios para no abrir ese permiso.
- **Bitácora:** al reasignar queda `$reason` «Reasignado por {nombre} desde la app móvil».

## 7. Relación con `warehouse.pick.any`
Hoy `warehouse.pick.any` ya existe (Lote 193) y está en la plantilla del operador. Propuesta: **`warehouse.app.viewall` lo reemplaza** como único interruptor; a los roles que tengan `pick.any` se les da `viewall` solo si son supervisor (no a todos los operadores), y `pick.any` se deja en el catálogo sin consultarse (no se quita nada). Ver decisión D2.

## 8. Qué cambia en el código (cuando se apruebe)
- App: `ReceiveDocumentList` y `receive.tsx` (campo arriba, lista mía, desplegables, paso de modo en la misma pantalla), `putaway.tsx`, `exit-dispatch.tsx`, `my-tasks.tsx` (tercera lista y botón Reasignar con selector de persona), `dispatchScope.ts` (nuevo alcance), `useMyPermissions`.
- Servidor: dos permisos, política OR en los 5 `assign`, endpoint de personas asignables, `AuditNote` al reasignar, pruebas.
- Web: los dos permisos aparecen en Roles automáticamente (por el catálogo).
- Documentación: manual 09 y FAQ, `docs/lote199-decisiones.md`.

## 9. Decisiones que necesito de Luis
- **D1.** Con modo fijo en el almacén, ¿se salta la pregunta de modo? (recomendado: sí).
- **D2.** ¿`viewall` reemplaza a `pick.any` y deja de dársele a todos los operadores? Hoy un operador ya ve en Despacho todo lo del almacén; con esto solo vería lo asignado a él y lo sin asignar (recomendado: sí).
- **D3.** Lo **sin asignar**, ¿lo ve cualquier operador (como dijiste hoy) o solo con permiso? (recomendado: lo ve cualquiera).
- **D4.** ¿«Reasignar» también puede **quitar** la asignación (dejarlo sin asignar)? (recomendado: sí).
- **D5.** ¿Reasignar notifica a la persona? (fuera de este lote salvo que digas que sí).
