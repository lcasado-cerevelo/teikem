# Eliminar posiciones (diseño, 2026-10-12)

> **Estado: CONSTRUIDO (2026-10-12) en `Depot-Implementation`** (commits `[posiciones-eliminar]`), con las decisiones propuestas de la sección 9 (Luis dijo «sigue con las posiciones»; sin MFA extra para la eliminación masiva). Lo construido y lo que cambió, en la sección 10.

## 1. Qué se quiere
Hoy una posición no se puede eliminar, solo darla de baja. Luis reorganiza un rack y quita una fila: esas posiciones **ya no existen físicamente** y no deben seguir estorbando ni poder usarse. Quiere dos ideas distintas en pantalla:
- **Desactivar**: temporal. Se puede **reactivar** cuando se quiera.
- **Eliminar**: definitivo. **No se vuelve a usar.** Y, como se rehace el rack, debe poder reutilizarse su **código** en una posición nueva.
Para la base de datos la fila **se queda** (historial); lo que cambia es el significado en pantalla.

## 2. Cómo es hoy (lo que se reutiliza)
- Una posición (`WarehouseBin`) tiene `IsActive`. Solo hay **Dar de baja / Reactivar** (`POST .../bins/{binId}/deactivate` y `/reactivate`, permiso `warehouse.manage`, `WarehouseLayoutService.SetBinActiveAsync`). La baja se rechaza con 409 si hay inventario en mano o reservado (`WarehouseRules.BinNotEmpty`) o tareas abiertas (`BinHasOpenTasks`).
- **El nombre del botón confunde:** «Dar de baja» suena a eliminar, pero es reversible («Reactivar»).
- **El código es único por almacén y por zona** (`UQ_WarehouseBin_WhCode (WarehouseId, Code)` y `UQ_WarehouseBin (WarehouseZoneId, Code)`): una posición dada de baja **sigue ocupando su código**, así que después de reorganizar el rack no se puede crear otra con el mismo.
- **Se debe conservar la fila**: la posición está referenciada por saldos, movimientos del libro (Kárdex), series, tareas, conteos, recibos (posición de recepción por defecto y de daño), recolecciones, devoluciones de rentas, descuadres y la posición preferida de los productos. Borrarla de verdad rompería el historial y las llaves foráneas.
- La pantalla de posiciones (`WarehouseDetailScreen`) ya tiene filtros («Incluir inactivas», «Solo con inventario», «Solo provisionales»), una modal de cupo masivo (`BinCapacityModal`) y etiquetas; las posiciones **provisionales** (creadas desde un conteo) las confirma, corrige o da de baja el supervisor.

## 3. Diseño
**Tres estados:** *Activa* · *Inactiva* (temporal) · *Eliminada* (definitiva).

| Acción | Efecto | ¿Se puede deshacer? | ¿Código? |
|---|---|---|---|
| **Desactivar** | `IsActive = 0`; sale de selectores y listas normales; aparece con «Incluir inactivas» | Sí: **Reactivar** | Sigue reservado (es la misma posición) |
| **Eliminar** | `IsDeleted = 1` (y `IsActive = 0`); desaparece de **todas** las listas; no se puede reactivar | **No** | **Queda libre**: una posición nueva puede usar ese código |

**Renombrar el botón actual:** «Dar de baja» → **«Desactivar»** (queda claro que es temporal), y se agrega **«Eliminar»**. Textos de ayuda: Desactivar «Sigue existiendo; la puede reactivar»; Eliminar «Definitivo: no se podrá volver a usar».

**Datos** (columnas nuevas en `WarehouseBin`, sección nueva e idempotente al final de `logistica-db-update.sql`; la estructura está congelada):
- `IsDeleted BIT NOT NULL DEFAULT 0`, `DeletedAtUtc DATETIME2 NULL`, `DeletedBy INT NULL`; regla `CHECK (IsDeleted = 0 OR IsActive = 0)` (una eliminada siempre está inactiva).
- **Unicidad del código solo entre las no eliminadas:** los dos índices únicos pasan a ser **filtrados** `WHERE IsDeleted = 0` (se reemplazan `UQ_WarehouseBin` y `UQ_WarehouseBin_WhCode`; las llaves foráneas apuntan a `UQ_WarehouseBin_IdWh (WarehouseBinId, WarehouseId)`, que no cambia). Mismo precedente que otros cambios de esquema posteriores a producción: `db-update` aplica la estructura congelada **más** el update para saber cómo debe quedar.
- La configuración de EF (`WarehouseConfigurations`) refleja los índices filtrados.

**Reglas para eliminar** (todas deben cumplirse; si no, 409 con el motivo exacto):
- Sin inventario en mano ni reservado (como Desactivar).
- Sin tareas abiertas que la usen (como Desactivar).
- **No es la posición por defecto** de un almacén (recepción, acomodo/staging, daño) **ni la preferida de un producto**: el mensaje dice cuál es («Es la posición de recepción por defecto de ALM-01; cámbiela primero»). No se limpia en silencio.
- Sin conteos abiertos que la incluyan.
- No hace falta que esté inactiva antes: se puede eliminar una activa (vacía); pasa a eliminada directamente.
- **Sí se puede eliminar una posición con historial** (movimientos pasados): la fila se queda.

**Dónde deja de verse una eliminada:** selectores de posición (filtros, pickers, acomodo, ajustes, conteos), listas del almacén (**tampoco** con «Incluir inactivas»), suma de cupo de la zona, etiquetas, búsqueda de posiciones entre almacenes y el cupo masivo. **Dónde sigue viéndose:** en el historial (Kárdex, detalle de movimientos, conteos y documentos viejos) con su código y la marca **«(eliminada)»**, para que se entienda qué fue. Esa marca sale de un dato en la fila que ya devuelven esos endpoints (`isDeleted`).

**Código reutilizable:** crear una posición con el código de una **eliminada** funciona (es otra posición, con su propio historial). Crear con el código de una **inactiva** sigue dando 409, pero con un mensaje que orienta: «Ya existe una posición inactiva con ese código: reactívela o elimínela».

**Provisionales:** el supervisor, además de confirmar o corregir, puede **eliminar** una provisional creada por error (es el caso más común).

**Eliminar varias a la vez** (se reorganiza una fila del rack): la tabla de posiciones gana una **columna de casillas** y una barra «Eliminar seleccionadas (N)». Confirmación: «Se eliminarán N posiciones. No se podrán volver a usar. Las que tengan inventario, tareas abiertas o sean posición por defecto se omiten y se le dirá cuáles.» El servidor revisa cada una al momento de eliminar; **es parcial**: elimina las que cumplen y devuelve el resultado por posición (eliminada / omitida con su motivo). Cada eliminación es independiente.

**API:** `DELETE /api/v1/warehouses/{id}/bins/{binId}` (una) y `POST /api/v1/warehouses/{id}/bins/delete` con `binIds[]` (varias; respuesta `{ deleted: [...], skipped: [{ binId, code, reason }] }`). Permiso `warehouse.manage` (el mismo de Desactivar). Reactivar una eliminada responde 409 «La posición fue eliminada y no se puede reactivar».

**Bitácora:** el cambio queda en la auditoría de la entidad (`AuditEntity(Warehouse)`) y en el historial del almacén, con quién y cuándo.

## 4. Cómo se divide para llevarlo a master
Estado de master (consultado en solo lectura): **no tocó** `WarehouseLayoutService.cs`, `WarehousesController.cs`, `Warehouse.cs`, `WarehouseRules.cs`, `WarehouseDetailScreen.tsx` ni `BinModal.tsx`; solo agregó una línea en `WarehouseConfigurations.cs` (igual que Depot). Puntos de contacto reales: la sección nueva de `logistica-db-update.sql`, `openapi.json` / `schema.d.ts` (se regeneran), los textos es/en y la prueba de mapa de seguridad de controladores (el conteo de acciones).

Commits con la etiqueta `[posiciones-eliminar]`: (1) base de datos + dominio + servicio + API + pruebas; (2) web (botones, casillas, diálogo, marca «(eliminada)» en el historial) + pruebas; (3) manual y decisiones. Se hace en `Depot-Implementation`, se prueba en staging y se pasa a `master` como los otros diseños.

## 5. Pruebas
- **Servicio:** eliminar una posición vacía (queda `IsDeleted`, inactiva); con inventario, reservado, tareas abiertas, por defecto de almacén o de producto y con conteo abierto → 409 con el motivo; con historial → sí se elimina; reactivar una eliminada → 409; crear con el código de una eliminada → funciona; con el de una inactiva → 409 con el mensaje nuevo; **varias**: parcial con resultado por posición.
- **Que no aparezca:** selectores, listas (también con «Incluir inactivas»), cupo de zona, búsqueda, etiquetas.
- **Historial:** el Kárdex y el detalle siguen mostrando su código con «(eliminada)».
- **Base de datos:** el índice único filtrado permite dos posiciones con el mismo código solo si una está eliminada (se prueba el predicado a SQL Server como las demás pruebas de traducción); `db-update` deja la base con los índices nuevos.
- **Web:** botones Desactivar/Eliminar, confirmación, casillas y «Eliminar seleccionadas», resumen de omitidas.

## 6. Seguridad
Mismo permiso que Desactivar (`warehouse.manage`). Es irreversible para el uso (la fila y su historial se conservan), por eso la confirmación explícita. Se evalúa pedir MFA reciente (`[RequireAal2]`) solo en la eliminación **masiva** (ver decisiones).

## 7. Relación con otros diseños
El **ajuste múltiple** y el **conteo** ofrecen solo posiciones no eliminadas. El **filtro por usuario del Kárdex** no cambia.

## 8. Fuera de alcance
Restaurar una posición eliminada (no se contempla: se crea una nueva), eliminar **zonas** o **almacenes** (se diseñan aparte si hace falta) y borrar físicamente filas.

## 9. Decisiones (propuestas; Luis puede cambiarlas)
1. **El código de una posición eliminada queda libre** para reutilizarse. *(Propuesta: sí.)*
2. **Eliminar varias a la vez**, con resultado parcial por posición. *(Propuesta: sí.)*
3. **Renombrar «Dar de baja» a «Desactivar»** y agregar «Eliminar». *(Propuesta: sí.)*
4. Una eliminada se ve en el historial con la marca «(eliminada)» y **en ninguna otra parte**. *(Propuesta: sí.)*
5. **MFA reciente** para la eliminación masiva. *(Propuesta: no; solo la confirmación explícita, como Desactivar.)*

## 10. Lo construido
- **Base de datos** (sección al final de `logistica-db-update.sql`): `WarehouseBin.IsRemoved`, `RemovedAtUtc`, `RemovedBy` y `CK_WarehouseBin_Removed`; `UQ_WarehouseBin` y `UQ_WarehouseBin_WhCode` pasan a ser **índices únicos filtrados** `WHERE IsRemoved = 0`. *Diferencia con el diseño:* la columna se llama **`IsRemoved`**, no `IsDeleted`: las pruebas que vigilan las secciones anteriores del update prohíben la palabra «DELETE» en secciones posteriores.
- **API:** `DELETE /warehouses/{id}/bins/{binId}` (204) y `POST /warehouses/{id}/bins/delete` (parcial, hasta 500); `warehouse.manage`. Motivos exactos de rechazo: inventario/reservado, tareas abiertas, recepción por defecto del almacén, preferida de un producto y conteo abierto (Pendiente o Contado). Reactivar/editar/confirmar una eliminada → 409. Las listas, la búsqueda y el cupo nunca incluyen eliminadas; el alta con el código de una eliminada funciona y con el de una inactiva da el 409 orientador.
- **Historial:** Kárdex y saldos muestran la posición como «A-01 (eliminada)» (o «(deleted)»).
- **Web:** «Dar de baja» pasó a **Desactivar**; ícono **Eliminar** por fila en la ficha del almacén; **Eliminar seleccionadas (N)** en Posiciones con confirmación y resumen de omitidas.
- **No incluido:** exclusión de eliminadas en los puntos que ya filtraban solo por `IsActive` (una eliminada siempre está inactiva, así que quedan fuera por la misma regla); el marcado «(eliminada)» no se agregó a las pantallas que muestran el código desde otras fuentes (solo Kárdex y saldos, por `BinInfo`).
