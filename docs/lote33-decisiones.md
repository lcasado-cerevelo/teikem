# Lote 33 — Cerrar una ruta (trip), servidor (2026-10-11)

Pendiente P0.3 del plan (`docs/plan-de-desarrollo-pendiente.md`): `TripStatuses.Completed` y su efecto existían, pero ningún endpoint movía una
ruta a ese estatus (solo `StartAsync`). Este lote construye **solo el servidor** (API, SQL, pruebas, contrato y manual).

## Decisiones del dueño que se aplicaron (2026-10-11)

- Se cierra **solo si todas las órdenes de la ruta terminaron** (si no, 422 con las que faltan).
- Permiso **nuevo `trips.close`** (separado de `trips.dispatch`).
- Disparo **manual** por ahora (el cierre automático desde la app del chofer queda para P7).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Endpoint | `POST /api/v1/trips/{publicId}/complete` (`trips.close`), cuerpo opcional `{comment, rowVersion}`, devuelve la ficha | `Api/Controllers/TripDispatchController.cs` |
| Servicio | `TripLifecycleService.CompleteAsync` / `CompleteTrackedAsync`: bloquea la ruta y luego sus órdenes (orden de bloqueo del repo), valida estatus y órdenes, y transiciona con `StatusService` (el efecto `TripStatusEffect` fija `ActualEndUtc` y deja de vigentes los `TripOrder`, ya existía) | `Infrastructure/Services/TripLifecycleService.cs` |
| Reglas | `DispatchBatchRules`: mensajes del cierre y `OrderFinishedForClose` (terminal, **Fallida** o **Parcial**) | `Domain/Trips/DispatchBatchRules.cs` |
| Permiso | `trips.close` en `PermissionCatalog` (73 códigos), plantilla Dispatcher y TenantAdmin | `Domain/Constants/PermissionCatalog.cs` |
| SQL | Sección **2026-10-11 (d)** de `Diseño/logistica-db-update.sql`: permiso + roles Dispatcher/TenantAdmin ya clonados (idempotente) | `Diseño/logistica-db-update.sql` |
| Contrato | `web-app/openapi.json` (solo agregados: la ruta y `TripCompleteRequest`) y `schema.d.ts` de la web y de la app | `web-app/`, `app-almacen/` |
| Pruebas | 11 nuevas en `TripStatusEffectTests` (cierre feliz, comentario, bloqueo por orden en camino/llegada/retenida, tope de 10 en el mensaje, estatus incorrecto, ya cerrada, regla pura); actualizadas las de catálogo (72 → 73) y el mapa de seguridad de controladores | `tests/Teikem.Tests/` |
| Manual | §6.2b y fila de transición en `docs/manual/05-trips-y-rutas.md`; 3 preguntas en `faq.md` | `docs/manual/` |

## Corrección encontrada de paso: `db-update` y los cambios que solo viven en `logistica-db-update.sql`

`db-update` comparaba la base real contra el script de estructura **congelado** solamente. Los cambios de esquema que viven en
`logistica-db-update.sql` (p. ej. el `CHECK` de `NumberSequence.Kind` con `MANUALISSUE`, Lote 31) aparecían como «diferencias» y se intentaba
devolverlos al texto viejo: **fallaba en cuanto existía un despacho manual** (`DMA-`), porque la fila `MANUALISSUE` violaba el `CHECK` viejo.
Arreglo: la base temporal de comparación ahora se arma con estructura **+ seed + `logistica-db-update.sql`** (el estado deseado real).
Comprobado: `db-update` dos veces seguidas sobre una base con datos del humo (incluidas secuencias `MANUALISSUE`) → «0 cambio(s)», rc 0.
(`SchemaSync.PlanAsync`, `DatabaseInitializer.UpdateAsync`.) No hay prueba automática: exige SQL Server y el CI solo corre `db-init`.

## Cómo se probó

- Pruebas del servidor: **3368 verdes** (3357 + 11).
- Contra el API local: ruta inexistente → 404 `Ruta no encontrada.`; sin token → 401; usuario con rol WarehouseOperator (sin el permiso) → 403.
- `db-update` sobre base con datos (arriba) y roles Dispatcher/TenantAdmin de las 3 compañías con `trips.close`.
- Humo: no se agregó un paso (armar una ruta completa en `smoke.sh` pide chofer, vehículo y órdenes en curso); la lógica está cubierta por las
  pruebas del servicio con la misma base en memoria del resto de los efectos de ruta.

## Decisiones a revisar

1. **Fallida y Parcial cuentan como terminadas** (en el modelo de estatus son etapas laterales, no terminales). Si se prefiere exigir solo
   etapas terminales, basta quitar esos dos códigos de `OrderFinishedForClose`.
2. **Falta la web**: un botón «Cerrar ruta» (con `trips.close`) en la ficha de la ruta / sala de despacho. Cambia una pantalla existente: no se
   tocó (pide tu visto bueno).
3. **Cierre automático** al terminar la última orden: lo traerá la app del chofer (P7).
4. Una ruta sin ninguna orden vigente se puede cerrar (no hay nada pendiente).
